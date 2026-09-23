using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.Core;

namespace FeudalInternalAffairs
{
    // AI 宣战/和谈系统(用户需求): 所有国家的战争外交都走这一套, 不走原版决议。
    //   宣战: AI 之间与 AI 对玩家由本系统判定; 玩家的战争由玩家在外交页决定。
    //   和谈: 只有满足我们的多数条件(3 选 2)才可能停战; 涉及玩家王国的和谈走玩家表决流程。
    internal static class AiDiplomacy
    {
        // 本系统执行动作时置 true(放行 NoAiControlPatches 的拦截)
        internal static bool AiActing;

        private static readonly Dictionary<string, int> _warStart = new Dictionary<string, int>();
        private static readonly Dictionary<string, int> _peaceDay = new Dictionary<string, int>();
        private static int _lastWarDay = -9999;

        internal static int Today()
        {
            try { return (int)CampaignTime.Now.ToDays; } catch { return 0; }
        }

        internal static void NoteWar(Kingdom a, Kingdom b)
        {
            try
            {
                var k = Diplomacy.Key(a, b);
                if (k != null) _warStart[k] = Today();
            }
            catch { }
        }

        internal static void NotePeace(Kingdom a, Kingdom b)
        {
            try
            {
                var k = Diplomacy.Key(a, b);
                if (k == null) return;
                _peaceDay[k] = Today();
                _warStart.Remove(k);
            }
            catch { }
        }

        internal static int WarDays(Kingdom a, Kingdom b)
        {
            try
            {
                int d;
                var k = Diplomacy.Key(a, b);
                if (k != null && _warStart.TryGetValue(k, out d)) return Math.Max(0, Today() - d);
                var st = a.GetStanceWith(b);
                if (st != null && st.IsAtWar && st.WarStartDate != CampaignTime.Never)
                    return (int)(CampaignTime.Now - st.WarStartDate).ToDays;
            }
            catch { }
            return 0;
        }

        private static int PeaceAge(Kingdom a, Kingdom b)
        {
            try
            {
                int d;
                var k = Diplomacy.Key(a, b);
                if (k != null && _peaceDay.TryGetValue(k, out d)) return Math.Max(0, Today() - d);
            }
            catch { }
            return 9999;
        }

        private static int WarsOf(Kingdom k)
        {
            try
            {
                int n = 0;
                var war = k.FactionsAtWarWith;
                if (war == null) return 0;
                for (int i = 0; i < war.Count; i++)
                {
                    var kk = war[i] as Kingdom;
                    if (kk != null && !kk.IsEliminated) n++;
                }
                return n;
            }
            catch { return 0; }
        }

        // 领袖野心(好战程度): 勇气/算计 +, 仁慈 -; 0.6~1.6 倍率
        private static float Aggression(Kingdom a)
        {
            try
            {
                var l = a.Leader;
                if (l == null) return 1f;
                int valor = 0, calc = 0, mercy = 0;
                try { valor = l.GetTraitLevel(TaleWorlds.CampaignSystem.CharacterDevelopment.DefaultTraits.Valor); } catch { }
                try { calc = l.GetTraitLevel(TaleWorlds.CampaignSystem.CharacterDevelopment.DefaultTraits.Calculating); } catch { }
                try { mercy = l.GetTraitLevel(TaleWorlds.CampaignSystem.CharacterDevelopment.DefaultTraits.Mercy); } catch { }
                float g = 1f + valor * 0.15f + calc * 0.1f - mercy * 0.1f;
                if (g < 0.6f) g = 0.6f;
                if (g > 1.6f) g = 1.6f;
                return g;
            }
            catch { return 1f; }
        }

        // 边境摩擦(用户: 解决实力接近不打仗): 接壤且非盟友的国家关系保底 -25(冷淡/敌对)
        private static void BorderFriction()
        {
            try
            {
                var all = new List<Kingdom>();
                foreach (var k in Kingdom.All) if (k != null && !k.IsEliminated) all.Add(k);
                for (int i = 0; i < all.Count; i++)
                {
                    for (int j = i + 1; j < all.Count; j++)
                    {
                        var a = all[i];
                        var b = all[j];
                        bool atWar = false;
                        try { atWar = a.IsAtWarWith(b); } catch { }
                        if (atWar || Diplomacy.IsAlly(a, b)) continue;
                        if (!Borders(a, b)) continue;
                        if (Diplomacy.Get(a, b) > -25) Diplomacy.Set(a, b, -25);
                    }
                }
            }
            catch { }
        }

        private static float StrengthOf(Kingdom k)
        {
            try { return Math.Max(1f, k.CurrentTotalStrength); } catch { return 1f; }
        }

        // 接壤判定(用户要求): 两国王国最近两个定居点距离 <= 阈值 视为接壤
        private const float BorderDistance = 70f;

        internal static bool Borders(Kingdom a, Kingdom b)
        {
            try
            {
                var sa = a.Settlements;
                var sb = b.Settlements;
                if (sa == null || sb == null || sa.Count == 0 || sb.Count == 0) return false;
                for (int i = 0; i < sa.Count; i++)
                {
                    var s1 = sa[i];
                    if (s1 == null) continue;
                    for (int j = 0; j < sb.Count; j++)
                    {
                        var s2 = sb[j];
                        if (s2 == null) continue;
                        if (s1.Position.Distance(s2.Position) <= BorderDistance) return true;
                    }
                }
            }
            catch { }
            return false;
        }

        // ================= 月度: AI 和谈 + AI 宣战 =================
        internal static void Month(int day)
        {
            try
            {
                var pk = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                var all = new List<Kingdom>();
                foreach (var k in Kingdom.All) if (k != null && !k.IsEliminated) all.Add(k);

                BorderFriction();   // 边境摩擦: 接壤国家关系保底 -25, 给宣战提供理由

                // v5.x: 统一大脑 —— AI 选举周期/集团周结/AI 政治议程(每国每月至多 2 条政治 + 2 条外交日志)
                try { Elections.AiMonthly(day); } catch { }
                try { PowerBlocs.AiWeekly(day); } catch { }
                for (int ai = 0; ai < all.Count; ai++)
                {
                    var kk = all[ai];
                    if (kk == pk) continue;
                    try { AiAgenda.Month(kk, day); } catch { }
                }

                // ---- 1) AI-AI 和谈: 按"联盟阵营"整体停战 ----
                var done = new HashSet<string>();
                for (int i = 0; i < all.Count; i++)
                {
                    for (int j = i + 1; j < all.Count; j++)
                    {
                        var a = all[i];
                        var b = all[j];
                        if (a == pk || b == pk) continue;
                        bool atWar = false;
                        try { atWar = a.IsAtWarWith(b); } catch { }
                        if (!atWar) continue;
                        var pairKey = Diplomacy.Key(a, b);
                        if (pairKey == null || done.Contains(pairKey)) continue;

                        int wd = WarDays(a, b);
                        if (wd < 28) continue;                       // 条件 1: 战争至少 28 天

                        // 阵营(传递闭包): 同盟一起谈, 一起停
                        var sideA = SideOf(a);
                        var sideB = SideOf(b);
                        if (ContainsPlayer(sideA, pk) || ContainsPlayer(sideB, pk)) continue;   // 涉及玩家的由玩家主导
                        if (PowerBlocs.AiShouldNotPeace(a, b)) continue;   // v5.x: 集团成员仍在交战 -> 不单独媾和

                        if (!PeaceConditionsMetSides(sideA, sideB, wd)) continue;   // 条件 2: 多数条件成立
                        if (MBRandom.RandomFloat > 0.45f) continue;
                        MakeSidePeace(sideA, sideB, wd);
                        foreach (var x in sideA) foreach (var y in sideB) { var k2 = Diplomacy.Key(x, y); if (k2 != null) done.Add(k2); }
                    }
                }

                // ---- 2) AI 宣战(全局每 21 天最多一场; 实力接近也能打, 看关系/野心) ----
                if (day - _lastWarDay < 21) return;
                Kingdom bestA = null, bestB = null;
                float bestChance = 0f;
                int bestRel = 1;
                for (int i = 0; i < all.Count; i++)
                {
                    var a = all[i];
                    if (a == pk) continue;                            // 玩家王国的战争由玩家决定
                    if (WarEconomy.IsCrisis(a)) continue;             // v4.72: 经济危机国不发动新战争
                    if (WarsOf(a) >= 2) continue;                     // 已经在两线作战, 不再开战
                    for (int j = 0; j < all.Count; j++)
                    {
                        var b = all[j];
                        if (b == a) continue;
                        if (WarEconomy.IsCrisis(b)) continue;         // v4.72: 不打经济崩溃的国家(除非已在战)
                        bool peace = true;
                        try { peace = !a.IsAtWarWith(b); } catch { }
                        if (!peace) continue;
                        if (Diplomacy.IsAlly(a, b)) continue;
                        if (PeaceAge(a, b) < 56) continue;            // 停战不足 56 天
                        if (!Borders(a, b)) continue;                 // 用户要求: 不接壤不宣战
                        int rel = Diplomacy.Get(a, b);
                        // v5.x: 集团协同(同集团不打) + 统一大脑军费底线(储备不足不打)
                        if (PowerBlocs.AiSameBloc(a, b)) continue;
                        float dirReserve = 0f;
                        try { dirReserve = AiBrain.ReserveGold(a); } catch { }
                        if (dirReserve > 1f && WarEconomy.GoldOfPublic(a) < dirReserve) continue;
                        // v4.111: 军力碾压(≥2倍)+高鹰派(≥75) -> 允许对关系>0的弱邻"背刺"
                        bool crush = AiPersonality.AggressionOf(a) >= 75 && StrengthOf(a) >= StrengthOf(b) * 2f;
                        if (rel > 0 && !crush) continue;              // 默认只打关系 <=0 的

                        // 宣战欲望: 优势型/均势型/劣势型 + 领袖野心 + 关系恶化
                        float sa = StrengthOf(a), sb = StrengthOf(b);
                        float ratio = sa / Math.Max(1f, sb);
                        float chance = ratio >= 1.25f ? 0.50f : (ratio >= 0.75f ? 0.28f : 0.08f);
                        chance *= Aggression(a);
                        // v4.106/4.107: 国家性格 —— 双方鹰派越高越易开战, 重商国家更不愿开战
                        int agA = AiPersonality.AggressionOf(a), agB = AiPersonality.AggressionOf(b);
                        chance *= (0.5f + (agA + agB) / 200f);
                        chance *= (1.1f - AiPersonality.CommerceOf(a) / 250f);
                        if (rel <= -40) chance *= 1.3f;
                        if (rel > 0) chance *= 0.55f;   // v4.111: 背刺概率低
                        // v4.117: 若目标是玩家且玩家明显更强, AI 敌意更高
                        if (pk != null && ReferenceEquals(b, pk) && StrengthOf(b) > StrengthOf(a) * 1.3f)
                            chance *= 1.25f;
                        // v4.149: 玩家虚弱窗口(内战/合法性崩坏/破产/多线) -> 趁火打劫宣战欲望
                        if (pk != null && ReferenceEquals(b, pk))
                            chance *= 1f + AiOpportunism.PlayerWarWindowBonus() * 0.8f;
                        // v5.x: 统一大脑外交战略(意愿/鹰派/指定目标/竞选期/集团协同)
                        try
                        {
                            chance *= 0.7f + 0.6f * AiAgenda.Norm01(AiBrain.AggressionOf(a));
                            if (!AiBrain.WantsWar(a)) chance *= 0.4f;
                            string wantId = AiBrain.WarTargetId(a);
                            if (wantId != null && wantId == b.StringId) chance *= 1.5f;
                        }
                        catch { }
                        try { if (Elections.AiInCampaign(a)) chance *= 0.25f; } catch { }
                        try { if (PowerBlocs.AiMateAtWarWith(a, b)) chance *= 1.6f; } catch { }
                        if (chance > bestChance || (Math.Abs(chance - bestChance) < 0.001f && rel < bestRel))
                        {
                            bestChance = chance; bestRel = rel; bestA = a; bestB = b;
                        }
                    }
                }
                if (bestA != null && MBRandom.RandomFloat < bestChance)
                {
                    // 第 22 章: 先开外交博弈, 谈崩了才打; 无法开博弈时兜底直接宣战
                    if (DiploPlays.StartPlay(bestA, bestB, false) == null)
                        MakeAiWar(bestA, bestB);
                }
            }
            catch (Exception ex) { DLog.Force("AI 外交月结异常: " + ex.Message); }
        }

        // 阵营(传递闭包; 与 Diplomacy.SideOf 单跳不同, 这里把盟友的盟友也并进来)
        internal static List<Kingdom> SideOf(Kingdom k)
        {
            var side = new List<Kingdom>();
            try
            {
                if (k == null) return side;
                var seen = new HashSet<string>();
                var queue = new Queue<Kingdom>();
                queue.Enqueue(k); seen.Add(k.StringId); side.Add(k);
                int guard = 0;
                while (queue.Count > 0 && guard++ < 64)
                {
                    var cur = queue.Dequeue();
                    foreach (var x in Kingdom.All)
                    {
                        if (x == null || x.IsEliminated || seen.Contains(x.StringId)) continue;
                        if (!Diplomacy.IsAlly(cur, x)) continue;
                        seen.Add(x.StringId); side.Add(x); queue.Enqueue(x);
                    }
                }
            }
            catch { }
            return side;
        }

        private static bool ContainsPlayer(List<Kingdom> side, Kingdom pk)
        {
            if (pk == null) return false;
            for (int i = 0; i < side.Count; i++) if (ReferenceEquals(side[i], pk)) return true;
            return false;
        }

        // 阵营的敌人数量(不重复)
        private static int EnemyCount(List<Kingdom> side)
        {
            try
            {
                var set = new HashSet<string>();
                for (int i = 0; i < side.Count; i++)
                {
                    var war = side[i].FactionsAtWarWith;
                    if (war == null) continue;
                    for (int j = 0; j < war.Count; j++)
                    {
                        var f = war[j] as Kingdom;
                        if (f == null || f.IsEliminated) continue;
                        set.Add(f.StringId);
                    }
                }
                return set.Count;
            }
            catch { return 0; }
        }

        private static float SideStrength(List<Kingdom> side)
        {
            float s = 0f;
            for (int i = 0; i < side.Count; i++) s += StrengthOf(side[i]);
            return Math.Max(1f, s);
        }

        // 和谈多数条件(5 选 2, 按阵营整体评估): 军力差距>=1.6 / 任一阵营敌人>=3 / 打满 84 天 / 任一国有经济危机 / 任一国有厌战>=60 (v4.72/4.73)
        private static bool PeaceConditionsMetSides(List<Kingdom> sideA, List<Kingdom> sideB, int warDays)
        {
            try
            {
                float sa = SideStrength(sideA), sb = SideStrength(sideB);
                float ratio = sa > sb ? sa / sb : sb / sa;
                int ea = EnemyCount(sideA), eb = EnemyCount(sideB);
                int conds = 0;
                if (ratio >= 1.6f) conds++;
                if (ea >= 3 || eb >= 3) conds++;
                if (warDays >= 84) conds++;
                if (AnyCrisis(sideA) || AnyCrisis(sideB)) conds++;   // v4.72: 经济崩了就想谈
                if (MaxWearSide(sideA) >= 60f || MaxWearSide(sideB) >= 60f) conds++;   // v4.73: 厌战了就想谈
                // v4.108: 性格影响和谈门槛 —— 鹰派阵营更顽固(需 3 条), 重商阵营更务实(1 条即谈)
                int need = 2;
                try
                {
                    int aggSum = 0, comSum = 0, cnt = 0;
                    for (int i = 0; i < sideA.Count; i++) { if (sideA[i] == null) continue; aggSum += AiPersonality.AggressionOf(sideA[i]); comSum += AiPersonality.CommerceOf(sideA[i]); cnt++; }
                    for (int i = 0; i < sideB.Count; i++) { if (sideB[i] == null) continue; aggSum += AiPersonality.AggressionOf(sideB[i]); comSum += AiPersonality.CommerceOf(sideB[i]); cnt++; }
                    if (cnt > 0)
                    {
                        int avgAgg = aggSum / cnt, avgCom = comSum / cnt;
                        if (avgAgg >= 70) need = 3;
                        else if (avgCom >= 70) need = 1;
                    }
                }
                catch { }
                return conds >= need;
            }
            catch { return false; }
        }

        private static float MaxWearSide(List<Kingdom> side)
        {
            float m = 0f;
            try
            {
                for (int i = 0; i < side.Count; i++)
                {
                    float w = WarWeariness.MaxWearOf(side[i]);
                    if (w > m) m = w;
                }
            }
            catch { }
            return m;
        }

        private static bool AnyCrisis(List<Kingdom> side)
        {
            try
            {
                for (int i = 0; i < side.Count; i++)
                    if (side[i] != null && WarEconomy.IsCrisis(side[i])) return true;
            }
            catch { }
            return false;
        }

        // 整条阵营一起停战(所有交战对)
        internal static void MakeSidePeace(List<Kingdom> sideA, List<Kingdom> sideB, int warDays)
        {
            try
            {
                int pairs = 0;
                AiActing = true;
                try
                {
                    for (int i = 0; i < sideA.Count; i++)
                    {
                        for (int j = 0; j < sideB.Count; j++)
                        {
                            var x = sideA[i];
                            var y = sideB[j];
                            if (x == null || y == null || x == y) continue;
                            bool atWar = false;
                            try { atWar = x.IsAtWarWith(y); } catch { }
                            if (!atWar) continue;
                            try
                            {
                                MakePeaceAction.Apply(x, y);
                                NotePeace(x, y);
                                try { WarPlans.Clear(x, y); WarPlans.Clear(y, x); } catch { }   // v4.149: 清除战争计划
                                pairs++;
                            }
                            catch (Exception ex) { DLog.Info("阵营停战失败 " + x.StringId + "/" + y.StringId + ": " + ex.Message); }
                        }
                    }
                }
                finally { AiActing = false; }
                string na = sideA.Count > 0 ? sideA[0].Name.ToString() : "?";
                string nb = sideB.Count > 0 ? sideB[0].Name.ToString() : "?";
                DLog.Force("AI 阵营和谈: [" + na + " 阵营 " + sideA.Count + "国] <-> [" + nb + " 阵营 " + sideB.Count + "国] 停战 " + pairs + " 对 (战争 " + warDays + " 天)");
            }
            catch (Exception ex) { DLog.Force("阵营和谈失败: " + ex.Message); }
        }

        internal static void MakeAiPeace(Kingdom a, Kingdom b, int warDays)
        {
            try
            {
                AiActing = true;
                try { if (a.IsAtWarWith(b)) MakePeaceAction.Apply(a, b); }
                finally { AiActing = false; }
                NotePeace(a, b);
                try { WarPlans.Clear(a, b); WarPlans.Clear(b, a); } catch { }   // v4.149: 清除战争计划
                DLog.Force("AI 和谈: " + a.Name + " <-> " + b.Name + " (战争 " + warDays + " 天, 多数条件达成)");
            }
            catch (Exception ex) { DLog.Force("AI 和谈失败: " + ex.Message); }
        }

        internal static void MakeAiWar(Kingdom a, Kingdom b)
        {
            try
            {
                AiActing = true;
                try { DeclareWarAction.ApplyByDefault(a, b); }
                finally { AiActing = false; }
                _lastWarDay = Today();
                NoteWar(a, b);
                // v4.149: 开战即生成战争计划(目标驱动), 双方各一份
                try { WarPlans.GetOrCreate(a, b, Today()); } catch { }
                try { WarPlans.GetOrCreate(b, a, Today()); } catch { }
                var pk = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                if (a == pk || b == pk)
                    MapSelection.Message("宣战: " + a.Name + " 进攻 " + b.Name + "(AI 判定)");
                DLog.Force("AI 宣战: " + a.Name + " -> " + b.Name);
            }
            catch (Exception ex) { DLog.Force("AI 宣战失败: " + ex.Message); }
        }

        // ================= 清除原版战争/和平决议(已停用!) =================
        // 原因: 从原版 KingdomDecisionManager 里 RemoveDecision 会让决策系统状态不一致,
        //       原版随后处理"被取消的决策"时原生崩溃(0xC0000005, 日志 "has been cancelled" 后崩)。
        //       战争/和平实际由动作层 BlockDeclareWar / BlockMakePeace 拦截, 不需要动决策系统。
        internal static void CleanNativeWarPeaceDecisions()
        {
            return;
#pragma warning disable CS0162
            try
            {
                foreach (var k in Kingdom.All)
                {
                    if (k == null || k.IsEliminated) continue;
                    var list = k.UnresolvedDecisions;
                    if (list == null || list.Count == 0) continue;
                    var arr = new List<TaleWorlds.CampaignSystem.Election.KingdomDecision>(list);
                    for (int i = 0; i < arr.Count; i++)
                    {
                        var d = arr[i];
                        if (d is TaleWorlds.CampaignSystem.Election.DeclareWarDecision
                            || d is TaleWorlds.CampaignSystem.Election.MakePeaceKingdomDecision)
                        {
                            try
                            {
                                k.RemoveDecision(d);
                                DLog.Force("清除原版决议: " + k.Name + " " + d.GetType().Name);
                            }
                            catch { }
                        }
                    }
                }
            }
            catch { }
#pragma warning restore CS0162
        }

        // ================= 存档 FIA_WarDairy =================
        internal static string Save()
        {
            try
            {
                var sb = new StringBuilder("v1;");
                foreach (var kv in _warStart) sb.Append("W").Append(kv.Key).Append(',').Append(kv.Value).Append(';');
                foreach (var kv in _peaceDay) sb.Append("P").Append(kv.Key).Append(',').Append(kv.Value).Append(';');
                sb.Append("L").Append(_lastWarDay).Append(';');
                sb.Append(AiAgenda.Save());   // v5.x: AI 政治议程状态(旧档无此段, Load 兼容)
                return sb.ToString();
            }
            catch { return "v1;"; }
        }

        internal static void Load(string data)
        {
            try
            {
                _warStart.Clear();
                _peaceDay.Clear();
                AiAgenda.Clear();   // v5.x: 读档重置 AI 议程
                try { AiDirector.Reset(); } catch { }   // v5.x: 读档重置统一大脑
                if (string.IsNullOrEmpty(data)) return;
                var parts = data.Split(';');
                for (int i = 1; i < parts.Length; i++)
                {
                    var s = parts[i];
                    if (string.IsNullOrEmpty(s)) continue;
                    char tag = s[0];
                    var body = s.Substring(1);
                    if (tag == 'A') { AiAgenda.LoadEntry(body); continue; }   // v5.x: AI 议程
                    if (tag == 'L')
                    {
                        int lv;
                        if (int.TryParse(body, out lv)) _lastWarDay = lv;
                        continue;
                    }
                    int idx = body.LastIndexOf(',');
                    if (idx <= 0) continue;
                    string key = body.Substring(0, idx);
                    int day;
                    if (!int.TryParse(body.Substring(idx + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out day)) continue;
                    if (tag == 'W') _warStart[key] = day;
                    else if (tag == 'P') _peaceDay[key] = day;
                }
                DLog.Force("AI 外交: 读档 战争记录=" + _warStart.Count + " 停战记录=" + _peaceDay.Count);
            }
            catch { }
        }
    }

    // ================= v5.x: AI 政治议程与外交战略(统一大脑驱动) =================
    //   读 AiDirector(GoalOf/ThreatOf/Aggression/WantsWar/WarTargetId/ReserveGold/BudgetFor);
    //   让 AI 像玩家一样组合出牌: 立法议程 + 合法性(民怨)管理 + 竞选 + 均势外交 + 集团;
    //   全部月结/周结; 每国每月最多 2 条 "AI 政治" + 2 条 "AI 外交" 日志; 读档重置。
    internal class AiAgendaState
    {
        internal AiDirector.Goal CurGoal;
        internal int LastMonth = -9999;
        internal int LastDipMonth = -9999;
        internal int Law0 = -1, Tier0 = -1, Law1 = -1, Tier1 = -1;   // 立法议程(1~2 部)
        internal int Fail0, Fail1;
        internal int Ban0 = -1, Ban1 = -1;                           // 连败后暂时不再盯的法律
        internal int BanDay0 = -9999, BanDay1 = -9999;
        internal int LastAppeaseDay = -9999;
        internal int LastUnrestDay = -9999;
        internal int LastAllyDay = -9999;
        internal int LastRebelDay = -9999;
        internal bool ElectionLost;
        internal int ElectionLossDay = -9999;
        internal int LogMonth = -9999;
        internal int PolLogs, DipLogs;
    }

    internal static class AiAgenda
    {
        private static readonly Dictionary<string, AiAgendaState> Map = new Dictionary<string, AiAgendaState>();
        private static int _lastDay = -1;

        // 目标 -> 立法议程候选(只用通用法律; 特有法律不适用于 AI 议会)
        private static readonly int[] EconomyLaws = { LawSystem.LTax, LawSystem.LEconomy, LawSystem.LTrade, LawSystem.LCharter, LawSystem.LCoinage, LawSystem.LLand };
        private static readonly int[] MilitaryLaws = { LawSystem.LLevy, LawSystem.LSecurity, LawSystem.LPolity, LawSystem.LTax };
        private static readonly int[] PoliticsLaws = { LawSystem.LFranchise, LawSystem.LSuccession, LawSystem.LJustice, LawSystem.LBureaucracy, LawSystem.LChurch, LawSystem.LPolity };
        private static readonly int[] SurvivalLaws = { LawSystem.LSecurity, LawSystem.LPolicing, LawSystem.LSpeech, LawSystem.LLevy, LawSystem.LJustice };
        private static readonly int[] ConsolidationLaws = { LawSystem.LRights, LawSystem.LWelfare, LawSystem.LEducation, LawSystem.LMigration, LawSystem.LLand, LawSystem.LAssoc };

        internal static AiAgendaState Of(Kingdom k)
        {
            try
            {
                if (k == null || string.IsNullOrEmpty(k.StringId)) return null;
                AiAgendaState st;
                if (!Map.TryGetValue(k.StringId, out st)) { st = new AiAgendaState(); Map[k.StringId] = st; }
                return st;
            }
            catch { return null; }
        }

        internal static void Clear() { Map.Clear(); _lastDay = -1; }

        // 统一大脑数值口径归一化(兼容 0~1 / 0~100 两种标度)
        internal static float Norm01(float v)
        {
            if (v > 1.5f) v = v / 100f;
            if (v < 0f) v = 0f;
            if (v > 1f) v = 1f;
            return v;
        }

        private static int MonthKey(int day) { return day / 7; }

        internal static AiDirector.Goal GoalOf(Kingdom k)
        {
            try { return AiBrain.GoalOf(k); } catch { return AiDirector.Goal.Economy; }
        }

        internal static int LawOf(Kingdom k, int slot)
        {
            try
            {
                var st = Of(k);
                if (st == null) return -1;
                return slot == 0 ? st.Law0 : st.Law1;
            }
            catch { return -1; }
        }

        internal static void LogPol(Kingdom k, string msg) { Log(k, "AI 政治: ", true, msg); }
        internal static void LogDip(Kingdom k, string msg) { Log(k, "AI 外交: ", false, msg); }

        private static void Log(Kingdom k, string tag, bool pol, string msg)
        {
            try
            {
                if (k == null || string.IsNullOrEmpty(msg)) return;
                var st = Of(k);
                if (st == null) return;
                int mk = MonthKey(AiDiplomacy.Today());
                if (st.LogMonth != mk) { st.LogMonth = mk; st.PolLogs = 0; st.DipLogs = 0; }
                if (pol) { if (st.PolLogs >= 2) return; st.PolLogs++; }
                else { if (st.DipLogs >= 2) return; st.DipLogs++; }
                DLog.Force(tag + k.Name + " " + msg);
            }
            catch { }
        }

        // 月度: 计算议程(幂等; 游说/议会/法令/条约的 AI 段先 Ensure 再读目标)
        internal static void Ensure(Kingdom k, int day)
        {
            try
            {
                if (k == null || k.IsEliminated) return;
                if (day < _lastDay) Map.Clear();   // 新档/读档回溯 -> 重置
                _lastDay = day;
                var st = Of(k);
                if (st == null || st.LastMonth == day) return;
                st.LastMonth = day;
                int mk = MonthKey(day);
                if (st.LogMonth != mk) { st.LogMonth = mk; st.PolLogs = 0; st.DipLogs = 0; }
                st.CurGoal = GoalOf(k);
                ChooseLaws(k, st, day);
            }
            catch { }
        }

        // 月度: 政治管理 + 外交战略(每国每月只跑一次)
        internal static void Month(Kingdom k, int day)
        {
            try
            {
                Ensure(k, day);
                var st = Of(k);
                if (st == null || st.LastDipMonth == day) return;
                st.LastDipMonth = day;
                PoliticalManagement(k, st, day);
                DiploAgenda(k, st, day);
            }
            catch (Exception ex) { DLog.Info("AI 议程异常: " + ex.Message); }
        }

        // ---------- 立法议程: 按 Goal 选 1~2 部法 ----------
        // 注意: AI 的法档在 Parliament.AiLaw 里, 不能用玩家侧的 LawSystem.Level/NextTier
        private static int AiNextTier(Kingdom k, int law)
        {
            int lv = Parliament.AiLevelOf(k, law);
            int max = LawSystem.TierCount(law) - 1;
            return lv >= max ? max : lv + 1;
        }

        private static bool AiMaxed(Kingdom k, int law)
        {
            return Parliament.AiLevelOf(k, law) >= LawSystem.TierCount(law) - 1;
        }

        private static void ChooseLaws(Kingdom k, AiAgendaState st, int day)
        {
            try
            {
                if (st.Ban0 >= 0 && day - st.BanDay0 >= 56) st.Ban0 = -1;   // 封禁 56 天后可再试
                if (st.Ban1 >= 0 && day - st.BanDay1 >= 56) st.Ban1 = -1;
                if (st.Law0 >= 0 && (st.Law0 >= LawSystem.LawCount || AiMaxed(k, st.Law0) || st.Fail0 >= 2 || LawSystem.IsSpecial(st.Law0))) { if (st.Fail0 >= 2) { st.Ban0 = st.Law0; st.BanDay0 = day; } st.Law0 = -1; }
                if (st.Law1 >= 0 && (st.Law1 >= LawSystem.LawCount || AiMaxed(k, st.Law1) || st.Fail1 >= 2 || LawSystem.IsSpecial(st.Law1))) { if (st.Fail1 >= 2) { st.Ban1 = st.Law1; st.BanDay1 = day; } st.Law1 = -1; }
                if (st.Law0 >= 0 && st.Law1 >= 0) return;   // 每次只盯 1~2 部
                int[] pool = PoolFor(st.CurGoal, Elections.AiInCampaign(k));
                int best = -1, bestYes = -1;
                for (int i = 0; i < pool.Length; i++)
                {
                    int law = pool[i];
                    var d = LawSystem.Def(law);
                    if (d == null || !string.IsNullOrEmpty(d.Scope)) continue;
                    if (AiMaxed(k, law)) continue;
                    if (law == st.Law0 || law == st.Law1) continue;
                    if (law == st.Ban0 || law == st.Ban1) continue;   // 失败过的不再盯
                    int yes = 0;
                    try { yes = Parliament.AiYesSeatsOf(k, law, AiNextTier(k, law)); } catch { }
                    if (d.Cat == "权力") yes += AiPersonality.AggressionOf(k) / 10;
                    else if (d.Cat == "经济") yes += AiPersonality.CommerceOf(k) / 10;
                    else yes += AiPersonality.DevelopmentOf(k) / 10;
                    if (yes > bestYes) { bestYes = yes; best = law; }
                }
                if (best < 0) return;
                if (st.Law0 < 0) { st.Law0 = best; st.Tier0 = AiNextTier(k, best); st.Fail0 = 0; st.Ban0 = -1; }
                else { st.Law1 = best; st.Tier1 = AiNextTier(k, best); st.Fail1 = 0; st.Ban1 = -1; }
            }
            catch { }
        }

        private static int[] PoolFor(AiDirector.Goal g, bool campaign)
        {
            if (campaign) return PoliticsLaws;   // 竞选期: 推选举/合法性类
            switch (g)
            {
                case AiDirector.Goal.Economy: return EconomyLaws;
                case AiDirector.Goal.Military: return MilitaryLaws;
                case AiDirector.Goal.Politics: return PoliticsLaws;
                case AiDirector.Goal.Survival: return SurvivalLaws;
                default: return ConsolidationLaws;
            }
        }

        // 表决结果: 通过 -> 清目标; 差得远 -> 计数(2 次换目标); 接近 -> 保留继续攒票
        internal static void NoteLawResult(Kingdom k, int law, bool passed, int yes)
        {
            try
            {
                var st = Of(k);
                if (st == null || law < 0) return;
                if (law == st.Law0)
                {
                    if (passed) { st.Law0 = -1; st.Tier0 = -1; st.Fail0 = 0; }
                    else if (yes < 40) st.Fail0++;
                }
                else if (law == st.Law1)
                {
                    if (passed) { st.Law1 = -1; st.Tier1 = -1; st.Fail1 = 0; }
                    else if (yes < 40) st.Fail1++;
                }
            }
            catch { }
        }

        // 选举失利: 转向联盟/妥协 —— 改推胜选集团的诉求法
        internal static void NoteElectionLoss(Kingdom k, int winnerGroup)
        {
            try
            {
                var st = Of(k);
                if (st == null) return;
                st.ElectionLost = true;
                st.ElectionLossDay = AiDiplomacy.Today();
                int best = -1, bestV = 8;
                for (int i = 0; i < LawSystem.LawCount; i++)
                {
                    var d = LawSystem.Def(i);
                    if (d == null || !string.IsNullOrEmpty(d.Scope) || AiMaxed(k, i)) continue;
                    int v = LawSystem.AdvanceStance(i, winnerGroup);
                    if (v > bestV) { bestV = v; best = i; }
                }
                st.Law0 = best;
                st.Tier0 = best >= 0 ? AiNextTier(k, best) : -1;
                st.Fail0 = 0;
                st.Ban0 = -1;
                st.Law1 = -1; st.Tier1 = -1; st.Fail1 = 0;
                LogPol(k, "选举失利 -> 妥协: 改推胜选集团诉求《" + (best >= 0 ? LawSystem.NameOf(best) : "—") + "》");
            }
            catch { }
        }

        // ---------- 供游说/议会/法令/条约的 AI 段读取 ----------
        internal static string LobbyIdFor(Kingdom k)
        {
            try
            {
                var st = Of(k);
                if (st == null) return null;
                int law = st.Law0 >= 0 ? st.Law0 : st.Law1;
                if (law < 0) return null;
                string cat = LawSystem.CatOf(law);
                if (cat == "经济") return MBRandom.RandomFloat < 0.5f ? "fund_lobbies" : "appeasement";
                if (cat == "权力") return MBRandom.RandomFloat < 0.5f ? "loyalist" : "pro_country";
                return MBRandom.RandomFloat < 0.5f ? "pro_country" : "fund_lobbies";
            }
            catch { return null; }
        }

        internal static string LobbyCategoryFor(Kingdom k)
        {
            try
            {
                var st = Of(k);
                if (st == null) return null;
                int law = st.Law0 >= 0 ? st.Law0 : st.Law1;
                return law >= 0 ? LawSystem.CatOf(law) : null;
            }
            catch { return null; }
        }

        internal static string DecreeIdFor(Kingdom k)
        {
            try
            {
                var st = Of(k);
                if (st == null) return null;
                float days = 99f;
                try { WarEconomy.FoodDaysOf(k, out days); } catch { }
                if (days < 10f) return "emergency_relief";
                if (Elections.AiInCampaign(k)) return "promote_social_mobility";
                switch (st.CurGoal)
                {
                    case AiDirector.Goal.Survival: return AiPersonality.AggressionOf(k) >= 60 ? "violent_suppression" : "emergency_relief";
                    case AiDirector.Goal.Military: return MBRandom.RandomFloat < 0.6f ? "enlistment_efforts" : "violent_suppression";
                    case AiDirector.Goal.Economy: return MBRandom.RandomFloat < 0.5f ? "encourage_manufacturing" : "encourage_resource";
                    case AiDirector.Goal.Politics: return MBRandom.RandomFloat < 0.5f ? "promote_national_values" : "establish_missions";
                    default: return MBRandom.RandomFloat < 0.5f ? "encourage_agriculture" : "road_maintenance";
                }
            }
            catch { return null; }
        }

        internal static string TreatyTypeFor(Kingdom k)
        {
            try
            {
                var st = Of(k);
                if (st == null) return null;
                float days = 99f;
                try { WarEconomy.FoodDaysOf(k, out days); } catch { }
                if (days < 20f) return "trade";                    // 缺粮 -> 粮食/贸易条约
                if (EngineStockOf(k) < 20f) return "invest";       // 缺引擎 -> 工业/投资条约
                if (st.CurGoal == AiDirector.Goal.Military || st.CurGoal == AiDirector.Goal.Survival) return "nap";
                if (st.CurGoal == AiDirector.Goal.Politics) return MBRandom.RandomFloat < 0.5f ? "ally" : "trade";
                return MBRandom.RandomFloat < 0.6f ? "trade" : "invest";
            }
            catch { return null; }
        }

        private static float EngineStockOf(Kingdom k)
        {
            try
            {
                float n = 0f;
                foreach (var s in k.Settlements)
                {
                    if (s == null) continue;
                    var m = EconomyWorld.FindMarket(s.StringId);
                    if (m == null) continue;
                    var e = m.Get(FeudalGoods.Engines);
                    if (e != null) n += e.Stock;
                }
                return n;
            }
            catch { return 999f; }
        }

        // ---------- 集团与合法性(民怨)管理: 镇压或妥协, 避免革命 ----------
        private static void PoliticalManagement(Kingdom k, AiAgendaState st, int day)
        {
            try
            {
                float rad = RadicalOf(k);
                bool crisis = WarEconomy.IsCrisis(k);
                bool bankrupt = WarEconomy.DeficitDaysOf(k) >= 5;
                float auth = Decrees.AiAuthOf(k);

                // 1) 竞选期: 压税安抚 + 游说加票, 不做惹怒选民的事
                if (Elections.AiInCampaign(k))
                {
                    if (st.LastAppeaseDay != day)
                    {
                        st.LastAppeaseDay = day;
                        LowerTax(k);
                        try { Parliament.AiLobbyPush(k, "民权"); } catch { }
                        try { Parliament.AiLobbyPush(k, "权力"); } catch { }
                        if (auth >= 60f) Decrees.AiEnact(k, "promote_social_mobility", day);
                        LogPol(k, "竞选: 压税安抚+游说加票(推《" + LawName(st.Law0) + "》)");
                    }
                    return;
                }

                // 2) 民怨/饥荒/危机: 镇压或妥协(避免革命)
                if ((rad >= 0.45f || crisis || bankrupt) && day - st.LastUnrestDay >= 14)
                {
                    st.LastUnrestDay = day;
                    int agg = AiPersonality.AggressionOf(k);
                    bool suppress = auth >= 150f && agg >= 60 && !crisis;
                    if (suppress && Decrees.AiEnact(k, "violent_suppression", day))
                    {
                        RadicalShift(k, -0.02f);
                        LogPol(k, "镇压: 民怨 " + (int)(rad * 100) + "%(军队满意, 暴政换稳定)");
                    }
                    else
                    {
                        bool ok = Decrees.AiEnact(k, "emergency_relief", day);
                        LowerTax(k);
                        try { AiEconomyDeep.ShiftPublicStance(k, new[] { 0, -1, 1, 2, 1, 1, 6, 0 }); } catch { }
                        RadicalShift(k, -0.012f);
                        LogPol(k, "妥协: " + (ok ? "开仓救济" : "减税安抚") + "(民怨 " + (int)(rad * 100) + "%)");
                    }
                }
                // 3) 权威不足 / 关键集团情绪低: 定向安抚 + 攒票(政策 API: 法令/立场/游说)
                else
                {
                    int worstG = -1;
                    float worstMood = 1f;
                    for (int g = 1; g < InterestGroups.GroupCount; g++)
                    {
                        float mood = 0.5f;
                        try { mood = Parliament.AiGroupMood(k, g); } catch { }
                        if (mood < worstMood) { worstMood = mood; worstG = g; }
                    }
                    if ((auth < 120f || worstMood < 0.35f) && day - st.LastAppeaseDay >= 28)
                    {
                        st.LastAppeaseDay = day;
                        Decrees.AiEnact(k, "promote_national_values", day);
                        if (worstG > 0)
                        {
                            var att = new int[InterestGroups.GroupCount];
                            att[worstG] = 8;
                            try { AiEconomyDeep.ShiftPublicStance(k, att); } catch { }
                        }
                        try { Parliament.AiLobbyPush(k, "权力"); } catch { }
                        LogPol(k, "安抚 " + (worstG > 0 ? InterestGroups.NameOf(worstG) : "关键集团")
                            + " + 攒票(权威 " + (int)auth + ", 情绪 " + (int)(worstMood * 100) + ")");
                    }
                }
            }
            catch { }
        }

        private static string LawName(int law) { return law >= 0 ? LawSystem.NameOf(law) : "—"; }

        // 竞选期不加税: 回退一档(安抚选民)
        private static void LowerTax(Kingdom k)
        {
            try
            {
                int lv;
                if (AiEconomyDeep.TaxLevel.TryGetValue(k.StringId, out lv) && lv > 1)
                    AiEconomyDeep.TaxLevel[k.StringId] = lv - 1;
            }
            catch { }
        }

        internal static float RadicalOf(Kingdom k)
        {
            try
            {
                float num = 0f, den = 0f;
                foreach (var s in k.Settlements)
                {
                    if (s == null) continue;
                    var list = Pops.Of(s.StringId);
                    if (list == null) continue;
                    for (int i = 0; i < list.Count; i++)
                    {
                        var p = list[i];
                        if (p == null || p.Size < 0.5f) continue;
                        num += p.Radicalism * p.Size; den += p.Size;
                    }
                }
                return den > 0f ? num / den : 0f;
            }
            catch { return 0f; }
        }

        internal static void RadicalShift(Kingdom k, float delta)
        {
            try
            {
                foreach (var s in k.Settlements)
                {
                    if (s == null) continue;
                    var list = Pops.Of(s.StringId);
                    if (list == null) continue;
                    for (int i = 0; i < list.Count; i++)
                    {
                        var p = list[i];
                        if (p == null || p.Size < 0.5f) continue;
                        p.Radicalism = LawSystem.ClampF(p.Radicalism + delta, 0f, 1f);
                    }
                }
            }
            catch { }
        }

        // ---------- 外交战略: 按 ThreatOf 构建均势 ----------
        private static void DiploAgenda(Kingdom k, AiAgendaState st, int day)
        {
            try
            {
                try { PowerBlocs.AiMonthly(k, day); } catch { }   // 集团: 创建/加入/升级
                float threat = 0f;
                try { threat = Norm01(AiBrain.ThreatOf(k)); } catch { }
                if (Elections.AiInCampaign(k)) return;     // 竞选期不做战争冒险
                float gold = WarEconomy.GoldOfPublic(k);
                if (threat >= 0.5f) ThreatResponse(k, st, day, threat);
                else if (threat <= 0.25f) Opportunism(k, st, day, gold);
            }
            catch { }
        }

        // 威胁高 -> 拉同盟/靠近大国集团
        private static void ThreatResponse(Kingdom k, AiAgendaState st, int day, float threat)
        {
            try
            {
                Kingdom best = null;
                int bestRel = -101;
                foreach (var b in Kingdom.All)
                {
                    if (b == null || b.IsEliminated || ReferenceEquals(b, k)) continue;
                    if (Diplomacy.IsAlly(k, b)) continue;
                    if (!AiDiplomacy.Borders(k, b)) continue;
                    bool war = false;
                    try { war = k.IsAtWarWith(b); } catch { }
                    if (war) continue;
                    int rel = Diplomacy.Get(k, b);
                    if (rel > bestRel) { bestRel = rel; best = b; }
                }
                if (best == null || day - st.LastAllyDay < 14) return;
                st.LastAllyDay = day;
                // 外交预算: 预算紧且不富裕 -> 只攒关系, 不缔盟(用 BudgetFor "dip")
                float dipBudget = 0f;
                try { dipBudget = AiBrain.BudgetFor(k, "dip", WarEconomy.GoldOfPublic(k)); } catch { }
                bool rich = WarEconomy.GoldOfPublic(k) > 8000f;
                if (bestRel >= Diplomacy.AllyThreshold - 15 && (dipBudget <= 1f || dipBudget >= 400f || rich))
                {
                    if (Decrees.AiAuthSpend(k, 50f) && Diplomacy.StartAlliance(k, best))
                        LogDip(k, "威胁 " + (int)(threat * 100) + "% -> 与 " + best.Name + " 缔结同盟");
                    else Diplomacy.Change(k, best, 5);
                }
                else Diplomacy.Change(k, best, 5);   // 先攒关系
            }
            catch { }
        }

        // 威胁低 -> 机会主义扩张: 支持叛乱 / 断盟(开战由 AiDiplomacy 按 WarTargetId 选目标)
        private static void Opportunism(Kingdom k, AiAgendaState st, int day, float gold)
        {
            try
            {
                if (day - st.LastRebelDay >= 56 && gold > 1500f && AiPersonality.AggressionOf(k) >= 50)
                {
                    Kingdom weak = null;
                    float weakPow = float.MaxValue;
                    foreach (var b in Kingdom.All)
                    {
                        if (b == null || b.IsEliminated || ReferenceEquals(b, k)) continue;
                        if (Diplomacy.IsAlly(k, b)) continue;
                        if (!AiDiplomacy.Borders(k, b)) continue;
                        float pow = 0f;
                        try { pow = b.CurrentTotalStrength; } catch { }
                        if (pow < weakPow) { weakPow = pow; weak = b; }
                    }
                    if (weak != null)
                    {
                        st.LastRebelDay = day;
                        WarEconomy.SpendPublic(k, 500f);
                        RadicalShift(weak, 0.012f);   // 资助叛乱: 目标国人口激进化
                        LogDip(k, "机会主义: 资助 " + weak.Name + " 叛乱(其民怨上升)");
                    }
                }
                if (day - st.LastAllyDay >= 28)
                {
                    foreach (var b in Kingdom.All)
                    {
                        if (b == null || b.IsEliminated || ReferenceEquals(b, k)) continue;
                        if (!Diplomacy.IsAlly(k, b)) continue;
                        if (Diplomacy.Get(k, b) >= 15) continue;
                        st.LastAllyDay = day;
                        Diplomacy.EndAlliance(k, b);
                        LogDip(k, "断盟: 与 " + b.Name + " 关系恶化, 解除同盟");
                        break;
                    }
                }
            }
            catch { }
        }

        // ---------- 存档(由 AiDiplomacy 统一存取) ----------
        internal static string Save()
        {
            try
            {
                var sb = new StringBuilder();
                foreach (var kv in Map)
                {
                    var st = kv.Value;
                    if (st == null) continue;
                    sb.Append('A').Append(kv.Key).Append(',')
                      .Append((int)st.CurGoal).Append(',').Append(st.Law0).Append(',').Append(st.Tier0).Append(',').Append(st.Fail0).Append(',')
                      .Append(st.Law1).Append(',').Append(st.Tier1).Append(',').Append(st.Fail1).Append(',')
                      .Append(st.ElectionLost ? 1 : 0).Append(',').Append(st.ElectionLossDay).Append(',')
                      .Append(st.Ban0).Append(',').Append(st.Ban1).Append(',')
                      .Append(st.BanDay0).Append(',').Append(st.BanDay1).Append(';');
                }
                return sb.ToString();
            }
            catch { return ""; }
        }

        internal static void LoadEntry(string body)
        {
            try
            {
                if (string.IsNullOrEmpty(body)) return;
                var f = body.Split(',');
                if (f.Length < 10) return;
                var st = new AiAgendaState();
                int v;
                if (int.TryParse(f[1], out v)) st.CurGoal = (AiDirector.Goal)v;
                if (int.TryParse(f[2], out v)) st.Law0 = v;
                if (int.TryParse(f[3], out v)) st.Tier0 = v;
                if (int.TryParse(f[4], out v)) st.Fail0 = v;
                if (int.TryParse(f[5], out v)) st.Law1 = v;
                if (int.TryParse(f[6], out v)) st.Tier1 = v;
                if (int.TryParse(f[7], out v)) st.Fail1 = v;
                st.ElectionLost = f[8] == "1";
                if (int.TryParse(f[9], out v)) st.ElectionLossDay = v;
                if (f.Length > 10 && int.TryParse(f[10], out v)) st.Ban0 = v;
                if (f.Length > 11 && int.TryParse(f[11], out v)) st.Ban1 = v;
                if (f.Length > 12 && int.TryParse(f[12], out v)) st.BanDay0 = v;
                if (f.Length > 13 && int.TryParse(f[13], out v)) st.BanDay1 = v;
                Map[f[0]] = st;
            }
            catch { }
        }
    }
}
