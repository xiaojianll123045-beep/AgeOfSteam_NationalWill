using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace FeudalInternalAffairs
{
    // v4.149: 战争计划(目标驱动) —— 每个(王国,敌国)一份
    //   宣战/被宣战 -> 生成计划: 目标清单(战略评分排序) + 所需兵力估算 + 主目标/防守点 + 阶段机
    //   每日评估战况 -> 阶段: 0 集结 / 1 进攻 / 2 僵持 / 3 撤退止损; 撤退时主动触发和谈倾向
    internal class WarPlan
    {
        internal string KingdomId;
        internal string EnemyId;
        internal int CreatedDay;
        internal int Phase;                      // 0 集结 1 进攻 2 僵持 3 撤退止损
        internal string MainTargetId;            // 主目标
        internal List<string> TargetIds = new List<string>();      // 攻城目标(评分排序)
        internal List<string> RaidIds = new List<string>();        // 掠夺目标(村庄)
        internal string HoldId;                  // 防守点(本土关键城)
        internal float NeededTroops;             // 打主目标所需兵力
        internal float Progress;                 // 战况评分(正=优势)
        internal int Victories, Defeats;
        internal bool Retreating;
        internal int Role;                       // 战争议会角色: 0 主攻 1 助攻 2 掠夺 3 防守 4 支援
        internal int LastEvalDay = -9999;
        internal int LastTargetDay = -9999;
        internal float EnemySettlementsAtStart;
    }

    internal static class WarPlans
    {
        private static readonly Dictionary<string, WarPlan> Map = new Dictionary<string, WarPlan>();

        internal static string KeyOf(Kingdom k, Kingdom e)
        {
            try { return (k != null ? k.StringId : "?") + ">" + (e != null ? e.StringId : "?"); }
            catch { return "?"; }
        }

        internal static WarPlan Get(Kingdom k, Kingdom e)
        {
            try
            {
                WarPlan p;
                return Map.TryGetValue(KeyOf(k, e), out p) ? p : null;
            }
            catch { return null; }
        }

        internal static WarPlan GetOrCreate(Kingdom k, Kingdom e, int day)
        {
            try
            {
                if (k == null || e == null) return null;
                var key = KeyOf(k, e);
                WarPlan p;
                if (Map.TryGetValue(key, out p))
                {
                    if (p.CreatedDay == day) return p;   // 同日已生成
                    return p;
                }
                p = new WarPlan { KingdomId = k.StringId, EnemyId = e.StringId, CreatedDay = day, Phase = 0 };
                Map[key] = p;
                RebuildTargets(k, e, p, day);
                DLog.Force("战争计划: " + k.Name + " -> " + e.Name + " 目标 " + p.TargetIds.Count + " 城 + "
                    + p.RaidIds.Count + " 村, 主目标 " + (p.MainTargetId ?? "无") + ", 需兵 " + (int)p.NeededTroops);
                return p;
            }
            catch { return null; }
        }

        // 最强敌人的计划(主动作战用); v5.x AI: 统一大脑指定目标优先
        internal static WarPlan MainOf(Kingdom k, int day)
        {
            try
            {
                if (k == null) return null;
                var brain = FindKingdom(AiBrain.WarTargetId(k));
                if (brain != null && !brain.IsEliminated && !ReferenceEquals(brain, k))
                {
                    bool bwar = false;
                    try { bwar = k.IsAtWarWith(brain); } catch { }
                    if (bwar)
                    {
                        var bp = GetOrCreate(k, brain, day);
                        if (bp != null) return bp;
                    }
                }
                WarPlan best = null;
                float bestStr = 0f;
                foreach (var e in Kingdom.All)
                {
                    if (e == null || e.IsEliminated || ReferenceEquals(e, k)) continue;
                    bool war = false;
                    try { war = k.IsAtWarWith(e); } catch { }
                    if (!war) continue;
                    float s = StrengthOf(e);
                    if (s > bestStr) { bestStr = s; best = GetOrCreate(k, e, day); }
                }
                return best;
            }
            catch { return null; }
        }

        internal static void Clear(Kingdom k, Kingdom e)
        {
            try { Map.Remove(KeyOf(k, e)); } catch { }
        }

        internal static void ClearAllOf(Kingdom k)
        {
            try
            {
                if (k == null) return;
                var del = new List<string>();
                foreach (var kv in Map) if (kv.Value != null && kv.Value.KingdomId == k.StringId) del.Add(kv.Key);
                for (int i = 0; i < del.Count; i++) Map.Remove(del[i]);
            }
            catch { }
        }

        internal static Settlement Find(string id)
        {
            try
            {
                if (string.IsNullOrEmpty(id)) return null;
                foreach (var s in Settlement.All) if (s != null && s.StringId == id) return s;
            }
            catch { }
            return null;
        }

        internal static Kingdom FindKingdom(string id)
        {
            try
            {
                if (string.IsNullOrEmpty(id)) return null;
                foreach (var k in Kingdom.All) if (k != null && k.StringId == id) return k;
            }
            catch { }
            return null;
        }

        internal static float StrengthOf(Kingdom k)
        {
            float s = 0f;
            try
            {
                foreach (var p in MobileParty.All)
                {
                    if (p == null || !p.IsActive) continue;
                    if (p.IsGarrison || p.IsMilitia || p.IsCaravan) continue;
                    if (!ReferenceEquals(p.MapFaction, k)) continue;
                    s += p.MemberRoster.TotalManCount;
                }
            }
            catch { }
            return s;
        }

        // ================= 目标评估(战略评分, 不简化) =================
        //   价值: 城 3 / 堡 2 / 村 1 + 繁荣度 + 首都 + 补给枢纽(贸易绑定村多的城)
        //   守军: 驻军 + 民兵; 敌援军: 敌方全国可调动部队(按目标距离折算)
        //   距离: 我方主力到目标; 补给: 我方粮食天数; 焦土: 被掠夺村不再作为目标
        internal static float ScoreTarget(Kingdom k, Kingdom e, Settlement s, MobileParty from, out float needed, out bool isVillage)
        {
            needed = 0f;
            isVillage = false;
            try
            {
                if (s == null) return float.MaxValue;
                isVillage = s.IsVillage;
                float value = isVillage ? 1f : (s.IsCastle ? 2f : 3f);
                float def = 0f;
                try
                {
                    if (s.Town != null)
                    {
                        value += s.Town.Prosperity / 80f;
                        if (s.Town.GarrisonParty != null) def += s.Town.GarrisonParty.MemberRoster.TotalManCount;
                        def += s.Town.Prosperity * 0.08f;   // 民兵估算(繁荣度驱动)
                    }
                    if (isVillage && s.Village != null) value += s.Village.Hearth / 400f;
                    // 首都/重镇加成
                    try { if (e.RulingClan != null && s.OwnerClan == e.RulingClan) value += 1.5f; } catch { }
                    // 补给枢纽: 贸易绑定村多的城镇
                    if (s.Town != null)
                    {
                        int vb = 0;
                        foreach (var v in Settlement.All)
                        {
                            if (v == null || v.Village == null) continue;
                            if (v.Village.TradeBound == s) vb++;
                        }
                        value += vb * 0.25f;
                    }
                }
                catch { }

                // 敌援军(该目标 150 距离内的敌方部队)
                float enemyNear = 0f;
                try
                {
                    foreach (var p in MobileParty.All)
                    {
                        if (p == null || !p.IsActive) continue;
                        if (p.IsGarrison || p.IsMilitia || p.IsCaravan) continue;
                        if (!ReferenceEquals(p.MapFaction, e)) continue;
                        float d = p.Position.Distance(s.Position);
                        if (d < 150f) enemyNear += p.MemberRoster.TotalManCount;
                    }
                }
                catch { }

                float dist = 100f;
                if (from != null) dist = from.Position.Distance(s.Position);

                // 所需兵力 = 守军×1.5 + 近敌×0.5 + 预备 20%
                needed = def * 1.5f + enemyNear * 0.5f;
                needed *= 1.2f;
                if (isVillage) needed = Math.Min(needed, 60f);   // 掠夺村庄小股即可

                // 评分: 价值高 / 所需兵力少 / 距离近 -> 分低者优先
                float score = (dist + 40f) * (1f + needed / 120f) / (1f + value);
                // 焦土: 被掠夺的村庄短期内不再打
                if (isVillage && WarEconomy.IsScorched(s.StringId)) score *= 3f;
                return score;
            }
            catch { return float.MaxValue; }
        }

        internal static void RebuildTargets(Kingdom k, Kingdom e, WarPlan p, int day)
        {
            try
            {
                p.TargetIds.Clear();
                p.RaidIds.Clear();
                var from = FrontierPartyOf(k);
                var list = new List<KeyValuePair<Settlement, float>>();
                var raids = new List<KeyValuePair<Settlement, float>>();
                foreach (var s in e.Settlements)
                {
                    if (s == null) continue;
                    float need;
                    bool vill;
                    float sc = ScoreTarget(k, e, s, from, out need, out vill);
                    if (vill) raids.Add(new KeyValuePair<Settlement, float>(s, sc));
                    else list.Add(new KeyValuePair<Settlement, float>(s, sc));
                }
                list.Sort(delegate (KeyValuePair<Settlement, float> a, KeyValuePair<Settlement, float> b) { return a.Value.CompareTo(b.Value); });
                raids.Sort(delegate (KeyValuePair<Settlement, float> a, KeyValuePair<Settlement, float> b) { return a.Value.CompareTo(b.Value); });
                for (int i = 0; i < list.Count && i < 6; i++) p.TargetIds.Add(list[i].Key.StringId);
                for (int i = 0; i < raids.Count && i < 6; i++) p.RaidIds.Add(raids[i].Key.StringId);
                p.MainTargetId = p.TargetIds.Count > 0 ? p.TargetIds[0] : (p.RaidIds.Count > 0 ? p.RaidIds[0] : null);
                p.LastTargetDay = day;
                // 需兵(主目标)
                var mt = Find(p.MainTargetId);
                if (mt != null)
                {
                    float need;
                    bool vill;
                    ScoreTarget(k, e, mt, from, out need, out vill);
                    p.NeededTroops = need;
                }
                // 防守点: 本土最靠近前线的城
                Settlement hold = null;
                float bestD = float.MaxValue;
                try
                {
                    foreach (var s in k.Settlements)
                    {
                        if (s == null || s.IsVillage) continue;
                        float d = from != null ? from.Position.Distance(s.Position) : 0f;
                        if (d < bestD) { bestD = d; hold = s; }
                    }
                }
                catch { }
                p.HoldId = hold != null ? hold.StringId : null;
                p.EnemySettlementsAtStart = e.Settlements != null ? e.Settlements.Count : 0f;
            }
            catch { }
        }

        internal static MobileParty FrontierPartyOf(Kingdom k)
        {
            try
            {
                MobileParty best = null;
                float bestStr = -1f;
                foreach (var p in MobileParty.All)
                {
                    if (p == null || !p.IsActive) continue;
                    if (p.IsGarrison || p.IsMilitia || p.IsCaravan) continue;
                    if (p.LeaderHero == null) continue;
                    if (!ReferenceEquals(p.MapFaction, k)) continue;
                    float s = p.MemberRoster.TotalManCount;
                    if (s > bestStr) { bestStr = s; best = p; }
                }
                return best;
            }
            catch { return null; }
        }

        // ================= 每日评估: 阶段推进 / 战况 / 撤退 =================
        internal static void DailyAll(int day)
        {
            try
            {
                foreach (var k in Kingdom.All)
                {
                    if (k == null || k.IsEliminated) continue;
                    try { Daily(k, day); } catch { }
                }
            }
            catch { }
        }

        internal static void CouncilAll(int day)
        {
            try
            {
                foreach (var k in Kingdom.All)
                {
                    if (k == null || k.IsEliminated) continue;
                    try { Council(k, day); } catch { }
                }
            }
            catch { }
        }

        internal static void Daily(Kingdom k, int day)
        {
            try
            {
                if (k == null) return;
                foreach (var e in Kingdom.All)
                {
                    if (e == null || e.IsEliminated || ReferenceEquals(e, k)) continue;
                    bool war = false;
                    try { war = k.IsAtWarWith(e); } catch { }
                    if (!war) continue;
                    var p = GetOrCreate(k, e, day);
                    if (p == null) continue;
                    if (day == p.LastEvalDay) continue;
                    p.LastEvalDay = day;

                    // 目标刷新(每 28 天或目标失效)
                    if (day - p.LastTargetDay >= 28 || Find(p.MainTargetId) == null) RebuildTargets(k, e, p, day);

                    // ---- 战况评估 ----
                    float score = 0f;
                    try
                    {
                        foreach (var s in e.Settlements)
                        {
                            if (s == null) continue;
                            bool besiegedByUs = false;
                            try { besiegedByUs = s.IsUnderSiege && s.SiegeEvent != null && s.SiegeEvent.BesiegerCamp != null && ReferenceEquals(s.SiegeEvent.BesiegerCamp.LeaderParty.MapFaction, k); } catch { }
                            if (besiegedByUs) score += 0.6f;
                        }
                        foreach (var s in k.Settlements)
                        {
                            if (s == null) continue;
                            bool besiegedByEnemy = false;
                            try { besiegedByEnemy = s.IsUnderSiege; } catch { }
                            if (besiegedByEnemy) score -= 0.8f;
                        }
                        // 定居点易手(相对上次评估): 攻下/丢失 -> 战况评分 + 性格漂移
                        float now = e.Settlements != null ? e.Settlements.Count : 0f;
                        if (p.EnemySettlementsAtStart <= 0f) p.EnemySettlementsAtStart = now;
                        else if (now < p.EnemySettlementsAtStart)
                        {
                            score += (p.EnemySettlementsAtStart - now) * 2f;
                            AiPersonality.Observe(k, "city_taken", 1f);
                            AiPersonality.Observe(e, "city_lost", 1f);
                            p.EnemySettlementsAtStart = now;
                        }
                        else if (now > p.EnemySettlementsAtStart)
                        {
                            score -= (now - p.EnemySettlementsAtStart) * 2f;
                            AiPersonality.Observe(k, "city_lost", 1f);
                            AiPersonality.Observe(e, "city_taken", 1f);
                            p.EnemySettlementsAtStart = now;
                        }
                        // 厌战/危机压力
                        try
                        {
                            if (WarEconomy.IsCrisis(k)) score -= 1.5f;
                            float wear = WarWeariness.MaxWearOf(k);
                            if (wear >= 60f) score -= 1f;
                        }
                        catch { }
                    }
                    catch { }
                    p.Progress = p.Progress * 0.7f + score * 0.3f;   // 平滑

                    // ---- 阶段机 ----
                    if (p.Progress <= -3f) p.Phase = 3;              // 止损撤退
                    else if (p.Progress <= -0.5f) p.Phase = 2;       // 僵持
                    else if (p.Progress >= 0.5f) p.Phase = 1;        // 进攻
                    else p.Phase = p.Phase == 3 ? 2 : p.Phase;

                    // ---- 撤退判定 ----
                    bool crisis = false;
                    float wear2 = 0f;
                    try { crisis = WarEconomy.IsCrisis(k); wear2 = WarWeariness.MaxWearOf(k); } catch { }
                    p.Retreating = p.Phase == 3 && (crisis || wear2 >= 50f);
                    if (p.Retreating && day % 14 == 0)
                        DLog.Force("战争计划: " + k.Name + " 对 " + e.Name + " 转入撤退止损(战况 " + p.Progress.ToString("F1") + ")");

                    // v5.x AI: 统一大脑(周结) —— 兵力对比/主目标对齐/和谈
                    if (day % 7 == 0) { try { Weekly(k, e, p, day); } catch { } }
                }
            }
            catch (Exception ex) { DLog.Force("战争计划日结异常: " + ex.Message); }
        }

        // ================= v5.x: 统一大脑(周结) =================
        //   ① 兵力对比(含驻军): 劣势 <0.83 收缩; 指定方向且机动 ≥1.2 -> 恢复进攻
        //   ② 多线: 非 AiDirector.WarTargetId 方向收缩(兵力向指定方向集中)
        //   ③ 和谈: 目标达成/厌战/劣势/多线 -> 走 AiDiplomacy.MakeAiPeace(涉及玩家王国的由玩家/厌战系统处理)
        private static void Weekly(Kingdom k, Kingdom e, WarPlan p, int day)
        {
            if (k == null || e == null || p == null) return;
            float force = AiWarDirector.ForceRatioOf(k, e);
            float field = AiWarDirector.FieldRatioOf(k, e);
            bool survival = AiBrain.GoalOf(k) == AiDirector.Goal.Survival;
            string bid = AiBrain.WarTargetId(k);                       // 统一大脑目标 = 敌国 id
            bool onTarget = string.IsNullOrEmpty(bid) || bid == e.StringId;
            int wars = AiWarDirector.WarCount(k);

            if (force < AiWarDirector.RetreatRatio || survival)
            {
                if (p.Phase == 1) p.Phase = 2;
                p.Retreating = true;
            }
            else if (!onTarget && wars >= 2)
            {
                // 多线: 统一大脑主攻方向不在此 -> 本线收缩(兵力集中到指定方向)
                if (p.Phase == 1) p.Phase = 2;
                p.Retreating = true;
            }
            else if (p.Retreating && onTarget && field >= AiWarDirector.AttackRatio
                && !WarEconomy.IsCrisis(k) && WarWeariness.MaxWearOf(k) < 50f)
            {
                p.Retreating = false;
                if (p.Phase == 3) p.Phase = 2;
            }

            // 和谈(涉及玩家王国的由玩家决定/厌战系统处理)
            var pk = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
            if (ReferenceEquals(k, pk) || ReferenceEquals(e, pk)) return;
            int wd = AiDiplomacy.WarDays(k, e);
            if (wd < 14) return;
            bool achieved = false;
            var mt = Find(p.MainTargetId);
            if (mt != null && ReferenceEquals(mt.MapFaction, k)) achieved = true;   // 拿下目标城 = 目标达成
            float wear = WarWeariness.MaxWearOf(k);
            bool multi = wars >= 2;
            bool seek = achieved || wear >= 65f || force < 0.7f || (multi && (wear >= 45f || force < 0.9f || !onTarget));
            if (!seek) return;
            // 统一大脑还想打(开战条件)且非大劣 -> 不急谈
            if (AiBrain.WantsWar(k) && !achieved && wear < 70f && force >= 0.6f) return;
            // 多线: 先停次要敌人, 保留统一大脑指定方向的敌人
            if (multi && !achieved && onTarget && wear < 65f && force >= 0.7f) return;
            AiDiplomacy.MakeAiPeace(k, e, wd);
            try { AiWarDirector.Log(k, "目标达成/厌战, 与 " + e.Name + " 停战"); } catch { }
        }

        // ================= 战争议会: 同阵营角色分配(月度) =================
        //   盟主(实力最强)决定: 主攻/助攻/掠夺/防守/支援 —— 避免多国挤同一目标、避免全员进攻被偷家
        internal static void Council(Kingdom k, int day)
        {
            try
            {
                if (k == null) return;
                var side = AiDiplomacy.SideOf(k);
                if (side.Count < 2) return;   // 无盟友
                // 找盟主
                Kingdom leader = null;
                float bestStr = 0f;
                for (int i = 0; i < side.Count; i++)
                {
                    float s = StrengthOf(side[i]);
                    if (s > bestStr) { bestStr = s; leader = side[i]; }
                }
                if (leader == null) return;
                // 只由盟主执行一次分配(避免每个成员各分一次)
                if (!ReferenceEquals(k, leader)) return;

                // 主要敌人 = 阵营交战对手里最强的
                Kingdom mainEnemy = null;
                float meStr = 0f;
                var enemies = new HashSet<string>();
                for (int i = 0; i < side.Count; i++)
                {
                    try
                    {
                        foreach (var f in side[i].FactionsAtWarWith)
                        {
                            var ek = f as Kingdom;
                            if (ek == null || ek.IsEliminated) continue;
                            if (enemies.Contains(ek.StringId)) continue;
                            enemies.Add(ek.StringId);
                            float s = StrengthOf(ek);
                            if (s > meStr) { meStr = s; mainEnemy = ek; }
                        }
                    }
                    catch { }
                }
                if (mainEnemy == null) return;

                // v5.x AI: 统一大脑指定方向优先作为联盟主攻方向(兵力集中)
                try
                {
                    var brain = FindKingdom(AiBrain.WarTargetId(leader));
                    if (brain != null && !brain.IsEliminated && !ReferenceEquals(brain, leader))
                    {
                        bool war = false;
                        try { war = leader.IsAtWarWith(brain); } catch { }
                        if (war) mainEnemy = brain;
                    }
                }
                catch { }

                // 主目标(盟主视角)
                var leadPlan = GetOrCreate(leader, mainEnemy, day);
                var mainT = leadPlan != null ? Find(leadPlan.MainTargetId) : null;

                // 给每个成员分配角色
                for (int i = 0; i < side.Count; i++)
                {
                    var m = side[i];
                    if (m == null || m.IsEliminated) continue;
                    var p = GetOrCreate(m, mainEnemy, day);
                    if (p == null) continue;
                    float s = StrengthOf(m);
                    float ratio = s / Math.Max(1f, meStr);
                    bool isLeader = ReferenceEquals(m, leader);
                    bool homeThreat = HomeUnderThreat(m);
                    int agg = AiPersonality.AggressionOf(m);
                    // v5.x AI: 统一大脑 —— 劣势/生存威胁收缩, 指定方向且机动 ≥1.2 才主攻
                    float field = AiWarDirector.FieldRatioOf(m, mainEnemy);
                    bool survival = AiBrain.GoalOf(m) == AiDirector.Goal.Survival;
                    float threat = AiBrain.ThreatOf(m);                     // 0~1
                    string bid = AiBrain.WarTargetId(m);
                    bool onTarget = string.IsNullOrEmpty(bid) || bid == mainEnemy.StringId;

                    if (homeThreat || survival || threat >= 0.75f) p.Role = 3;              // 老家/生存威胁 -> 防守
                    else if (field < AiWarDirector.RetreatRatio) p.Role = 3;      // 劣势收缩
                    else if ((isLeader || ratio >= 0.55f) && onTarget
                        && field >= AiWarDirector.AttackRatio) p.Role = 0;        // 占优(≥1.2)才主攻
                    else if (field >= AiWarDirector.AttackRatio) p.Role = 1;      // 助攻(次要目标)
                    else if (agg >= 60) p.Role = 2;                                         // 掠夺(断补给)
                    else p.Role = 4;                                                        // 支援(跟随主力)
                }
                if (day % 28 == 0)
                {
                    var sb = new StringBuilder("战争议会: 盟主 " + leader.Name + " 对 " + mainEnemy.Name + " [");
                    for (int i = 0; i < side.Count; i++)
                    {
                        var p = Get(side[i], mainEnemy);
                        if (p == null) continue;
                        if (i > 0) sb.Append(", ");
                        sb.Append(side[i].Name).Append('=').Append(RoleName(p.Role));
                    }
                    sb.Append(']');
                    DLog.Force(sb.ToString());
                }
            }
            catch (Exception ex) { DLog.Force("战争议会异常: " + ex.Message); }
        }

        internal static string RoleName(int role)
        {
            switch (role)
            {
                case 0: return "主攻";
                case 1: return "助攻";
                case 2: return "掠夺";
                case 3: return "防守";
                default: return "支援";
            }
        }

        internal static bool HomeUnderThreat(Kingdom k)
        {
            try
            {
                foreach (var s in k.Settlements)
                {
                    if (s == null) continue;
                    bool b = false;
                    try { b = s.IsUnderSiege; } catch { }
                    if (b) return true;
                }
            }
            catch { }
            return false;
        }

        // ================= 军事强化: 合流 / 劣势不送 / 撤退 =================
        //   劣势时把附近友军合流(避免被逐个击破); 撤退时回防本土关键城
        internal static bool TryRegroup(Kingdom k, MobileParty p, int day)
        {
            try
            {
                if (k == null || p == null) return false;
                var plan = MainOf(k, day);
                if (plan == null) return false;
                if (!plan.Retreating && plan.Phase != 2) return false;   // 只在僵持/撤退时合流
                var pos = p.Position.ToVec2();
                MobileParty best = null;
                float bestD = 260f;
                foreach (var q in MobileParty.All)
                {
                    if (q == null || !q.IsActive || ReferenceEquals(q, p)) continue;
                    if (q.IsGarrison || q.IsMilitia || q.IsCaravan || q.IsMainParty) continue;
                    if (q.LeaderHero == null || DefArmy.IsDefArmyParty(q)) continue;
                    if (!ReferenceEquals(q.MapFaction, k)) continue;
                    bool inArmy = false;
                    try { inArmy = q.Army != null; } catch { }
                    if (inArmy) continue;
                    float d = q.Position.Distance(p.Position);
                    if (d < bestD) { bestD = d; best = q; }
                }
                if (best == null) return false;
                try { p.SetMoveEscortParty(best, MobileParty.NavigationType.Default, false); } catch { return false; }
                DLog.Info("军事 AI: " + k.Name + " 部队合流 -> " + MapSelection.NameOf(best));
                return true;
            }
            catch { return false; }
        }

        // 撤退: 回防本土关键城(不打无谓进攻)
        internal static bool TryRetreat(Kingdom k, MobileParty p, int day)
        {
            try
            {
                if (k == null || p == null) return false;
                var plan = MainOf(k, day);
                if (plan == null || !plan.Retreating) return false;
                var hold = Find(plan.HoldId);
                if (hold == null) return false;
                try { p.SetMoveGoToSettlement(hold, MobileParty.NavigationType.Default, false); } catch { return false; }
                DLog.Info("军事 AI: " + k.Name + " 撤退回防 " + (hold.Name != null ? hold.Name.ToString() : hold.StringId));
                return true;
            }
            catch { return false; }
        }

        // 劣势不送: 守城方援军不足时不出城野战
        internal static bool ShouldHoldInstead(Kingdom k, Settlement target, float reliefTroops)
        {
            try
            {
                if (k == null || target == null) return false;
                float enemyNear = 0f;
                try
                {
                    foreach (var p in MobileParty.All)
                    {
                        if (p == null || !p.IsActive) continue;
                        if (p.IsGarrison || p.IsMilitia || p.IsCaravan) continue;
                        if (!ReferenceEquals(p.MapFaction, target.MapFaction)) continue;   // 敌对方
                        if (k.IsAtWarWith(p.MapFaction) && p.Position.Distance(target.Position) < 120f)
                            enemyNear += p.MemberRoster.TotalManCount;
                    }
                }
                catch { }
                return reliefTroops < enemyNear * 1.15f;   // 援军不够优势 -> 不出城
            }
            catch { return false; }
        }

        // ================= 存档 =================
        internal static string Save()
        {
            try
            {
                var sb = new StringBuilder("v1");
                foreach (var kv in Map)
                {
                    var p = kv.Value;
                    if (p == null) continue;
                    sb.Append(';').Append(p.KingdomId).Append(',').Append(p.EnemyId).Append(',')
                      .Append(p.CreatedDay).Append(',').Append(p.Phase).Append(',')
                      .Append(p.MainTargetId ?? "").Append(',').Append(p.HoldId ?? "").Append(',')
                      .Append(p.NeededTroops.ToString("F0", CultureInfo.InvariantCulture)).Append(',')
                      .Append(p.Progress.ToString("F1", CultureInfo.InvariantCulture)).Append(',')
                      .Append(p.Retreating ? 1 : 0).Append(',').Append(p.Role).Append(',')
                      .Append(p.LastTargetDay).Append(',').Append(p.EnemySettlementsAtStart.ToString("F0", CultureInfo.InvariantCulture)).Append(',')
                      .Append(string.Join("+", p.TargetIds.ToArray())).Append(',').Append(string.Join("+", p.RaidIds.ToArray()));
                }
                return sb.ToString();
            }
            catch { return "v1"; }
        }

        internal static void Load(string data)
        {
            try
            {
                Map.Clear();
                if (string.IsNullOrEmpty(data)) return;
                var seg = data.Split(';');
                if (seg.Length < 2 || seg[0] != "v1") return;
                for (int i = 1; i < seg.Length; i++)
                {
                    var f = seg[i].Split(',');
                    if (f.Length < 13) continue;
                    var p = new WarPlan
                    {
                        KingdomId = f[0], EnemyId = f[1],
                        CreatedDay = I(f[2], 0), Phase = I(f[3], 0),
                        MainTargetId = string.IsNullOrEmpty(f[4]) ? null : f[4],
                        HoldId = string.IsNullOrEmpty(f[5]) ? null : f[5],
                        NeededTroops = P(f[6], 0f), Progress = P(f[7], 0f),
                        Retreating = f[8] == "1", Role = I(f[9], 0),
                        LastTargetDay = I(f[10], -9999), EnemySettlementsAtStart = P(f[11], 0f)
                    };
                    if (!string.IsNullOrEmpty(f[12])) foreach (var x in f[12].Split('+')) if (x.Length > 0) p.TargetIds.Add(x);
                    if (f.Length > 13 && !string.IsNullOrEmpty(f[13])) foreach (var x in f[13].Split('+')) if (x.Length > 0) p.RaidIds.Add(x);
                    Map[p.KingdomId + ">" + p.EnemyId] = p;
                }
                DLog.Force("战争计划: 读档 " + Map.Count + " 份");
            }
            catch { }
        }

        internal static void Reset() { Map.Clear(); }

        private static int I(string s, int def) { int v; return int.TryParse(s, out v) ? v : def; }
        private static float P(string s, float def) { float v; return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : def; }
    }
}
