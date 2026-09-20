using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace FeudalInternalAffairs
{
    // v4.106: AI 国家自己跑建设与扩军(受国库与性格约束)
    // v4.107 优化: 建设按"行政(建造点) > 基础粮食民生 > 性格倾向"智能评分选建; 扩军加战时加成并合并遍历
    internal static class AiDevelopment
    {
        private static readonly Dictionary<string, int> LastTerritory = new Dictionary<string, int>();   // v4.116: 上月领土数(性格漂移用)
        // ---- 每日: AI 排队建造 ----
        internal static void Daily(int day)
        {
            try
            {
                var pk = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                foreach (var k in Kingdom.All)
                {
                    if (k == null || k.IsEliminated) continue;
                    if (pk != null && ReferenceEquals(pk, k)) continue;   // 玩家王国由玩家建
                    // v4.112: 战时资源倾斜 —— 交战中建设放缓(钱给军费), 和平期加速发展
                    int wars0 = 0;
                    try { foreach (var x in Kingdom.All) { if (x == null || x.IsEliminated) continue; if (k.IsAtWarWith(x)) wars0++; } } catch { }
                    float warMult = wars0 > 0 ? 0.45f : 1.2f;
                    // 每国每日概率 ≈ 建设/100 × 0.5 × 战和系数
                    if (MBRandom.RandomFloat > AiPersonality.DevelopmentOf(k) / 100f * 0.5f * warMult) continue;
                    TryQueueOneBuild(k, day);
                }
            }
            catch (Exception ex) { DLog.Force("AI 建设日结异常: " + ex.Message); }
        }

        // v4.107: 建设评分(行政/粮食优先, 军事看鹰派, 贸易看商贸, 其余看建设)
        private static float ScoreBuild(BuildDef def, Kingdom k)
        {
            float sc = 1f;
            try
            {
                int agg = AiPersonality.AggressionOf(k);
                int dev = AiPersonality.DevelopmentOf(k);
                int com = AiPersonality.CommerceOf(k);
                if (def.Cat == BuildCat.Admin) sc += 4.5f;                                   // 行政部门: 建造点来源, 最优先
                else if (def.Cat == BuildCat.Military) sc += agg / 35f;                      // 军事: 鹰派爱军备
                else if (def.Cat == BuildCat.Resource || def.Cat == BuildCat.Living) sc += 1.6f;  // 资源/民生: 粮食基础
                else if (def.Cat == BuildCat.Trade) sc += com / 45f;                         // 贸易: 重商
                else if (def.Cat == BuildCat.Logistics) sc += 0.8f + dev / 200f;             // 后勤
                else sc += dev / 90f;                                                        // 加工等
            }
            catch { }
            sc += MBRandom.RandomFloat * 0.9f;   // 随机扰动(避免各国一模一样)
            return sc;
        }

        private static void TryQueueOneBuild(Kingdom k, int day)
        {
            try
            {
                var list = new List<Settlement>();
                foreach (var s in k.Settlements)
                    if (s != null && (s.IsTown || s.IsCastle || s.IsVillage)) list.Add(s);
                if (list.Count == 0) return;
                int start = (int)(((uint)(k.StringId != null ? k.StringId.GetHashCode() : 0)) + (uint)day) % list.Count;
                for (int i = 0; i < list.Count && i < 8; i++)
                {
                    var s = list[(start + i) % list.Count];
                    var sb = EconomyWorld.Of(s.StringId);
                    if (sb == null) continue;
                    if (sb.Queue.Count >= 2) continue;
                    int limit = BuildingRules.SlotLimit(s.IsTown, s.IsCastle,
                        s.Town != null ? (int)s.Town.Prosperity : 0,
                        s.Village != null ? (int)s.Village.Hearth : 0);
                    if (sb.UsedSlots >= limit) continue;

                    // v4.107: 评分选最优可建项
                    BuildDef bestDef = null;
                    float bestScore = -1f;
                    foreach (var def in BuildDefs.All)
                    {
                        if (def == null) continue;
                        if (!BuildDefs.AllowedAt(def, s.IsVillage, s.IsCastle, s.IsTown)) continue;
                        if (sb.Find(def.Id) != null) continue;
                        bool dup = false;
                        foreach (var q in sb.Queue) if (q != null && q.DefId == def.Id) { dup = true; break; }
                        if (dup) continue;
                        float sc = ScoreBuild(def, k);
                        if (sc > bestScore) { bestScore = sc; bestDef = def; }
                    }
                    if (bestDef == null) continue;

                    // v4.108: 档位按国库与建设性格选择(木/石/铁, 越高档造价与加成越高)
                    float gold0 = WarEconomy.GoldOfPublic(k);
                    int dev0 = AiPersonality.DevelopmentOf(k);
                    BuildMode mode = BuildMode.Wood;
                    if (gold0 > 8000f && dev0 >= 75) mode = BuildMode.Iron;
                    else if (gold0 > 4000f && dev0 >= 60) mode = BuildMode.Stone;
                    int work = BuildDefs.WorkHours(bestDef, mode);
                    float cost = work * 0.08f;
                    float gold = WarEconomy.GoldOfPublic(k);
                    if (gold < 600f + cost) return;   // 国库太薄先不建(留着军费)
                    WarEconomy.SpendPublic(k, cost);
                    sb.Queue.Add(new QueuedBuild(bestDef.Id, work, mode));
                    DLog.Info("AI 建设: " + k.Name + " 在 " + s.Name + " 开建 " + bestDef.Name + "[" + BuildDefs.ModeName(mode) + "](费 " + (int)cost + ")");
                    return;
                }
            }
            catch { }
        }

        // ---- 每月: AI 扩军(一次遍历收集各国最弱领主部队; 战时加成) ----
        internal static void Monthly(int day)
        {
            try
            {
                var pk = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                var weakest = new Dictionary<string, MobileParty>();
                var weakestMen = new Dictionary<string, int>();
                foreach (var p in MobileParty.All)
                {
                    if (p == null || !p.IsActive) continue;
                    if (p.IsMainParty || p.IsGarrison || p.IsMilitia || p.IsCaravan) continue;
                    if (p.LeaderHero == null) continue;
                    if (DefArmy.IsDefArmyParty(p)) continue;
                    var k = p.MapFaction as Kingdom;
                    if (k == null || k.IsEliminated) continue;
                    if (pk != null && ReferenceEquals(pk, k)) continue;
                    int men = 0;
                    try { men = p.MemberRoster.TotalManCount; } catch { }
                    int cur;
                    if (!weakestMen.TryGetValue(k.StringId, out cur) || men < cur)
                    {
                        weakestMen[k.StringId] = men;
                        weakest[k.StringId] = p;
                    }
                }

                foreach (var k in Kingdom.All)
                {
                    if (k == null || k.IsEliminated) continue;
                    if (pk != null && ReferenceEquals(pk, k)) continue;
                    // v4.116: 领土变化 -> 性格漂移(每月)
                    try
                    {
                        int cur = k.Settlements != null ? k.Settlements.Count : 0;
                        int prev;
                        if (LastTerritory.TryGetValue(k.StringId, out prev) && prev != cur)
                        {
                            AiPersonality.Drift(k, cur - prev);
                            DLog.Force("AI 性格漂移: " + k.Name + " 领土 " + prev + "→" + cur + " · " + AiPersonality.Describe(k));
                        }
                        LastTerritory[k.StringId] = cur;
                    }
                    catch { }
                    if (WarEconomy.IsCrisis(k)) continue;   // 危机国不扩军
                    if (k.Culture == null) continue;
                    MobileParty best;
                    if (!weakest.TryGetValue(k.StringId, out best) || best == null) continue;
                    int agg = AiPersonality.AggressionOf(k);
                    // v4.107: 交战中扩军更积极
                    int wars = 0;
                    try { foreach (var x in Kingdom.All) { if (x == null || x.IsEliminated) continue; if (k.IsAtWarWith(x)) wars++; } } catch { }
                    // v4.108: 交战国优先补强靠近敌国的部队(边境优先)
                    if (wars > 0)
                    {
                        var fp = FrontierPartyOf(k);
                        if (fp != null) best = fp;
                    }
                    float mult = wars > 0 ? 1.8f : 1f;
                    if (MBRandom.RandomFloat > agg / 100f * 0.7f * mult) continue;
                    int n = (int)((5 + agg / 10) * (wars > 0 ? 1.5f : 1f));   // 战时补得更多
                    float gold = WarEconomy.GoldOfPublic(k);
                    // v4.116/4.117: 富国 + 重视建设 -> 补精锐(T3; 国库很厚则 T4), 花费翻倍/三倍
                    int wantTier = 0;
                    if (gold > 12000f && AiPersonality.DevelopmentOf(k) >= 70) wantTier = 4;
                    else if (gold > 5000f && AiPersonality.DevelopmentOf(k) >= 60) wantTier = 3;
                    float cost = n * (wantTier >= 4 ? 60f : (wantTier == 3 ? 40f : 20f));
                    if (gold < 1000f + cost) continue;
                    WarEconomy.SpendPublic(k, cost);
                    if (wantTier > 0) DefArmy.AddEliteCompositionPublic(best.MemberRoster, k.Culture, n, wantTier);
                    else DefArmy.AddCompositionPublic(best.MemberRoster, k.Culture, n);
                    DLog.Force("AI 扩军: " + k.Name + " 为 " + MapSelection.NameOf(best) + " 补充 " + n
                        + (wantTier >= 4 ? " 顶级精锐" : (wantTier == 3 ? " 精锐" : " 兵")) + "(费 " + (int)cost + ")");
                }
            }
            catch (Exception ex) { DLog.Force("AI 扩军月结异常: " + ex.Message); }
        }

        // v4.108: 距离敌国定居点最近的领主部队(边境部队)
        private static MobileParty FrontierPartyOf(Kingdom k)
        {
            try
            {
                MobileParty best = null;
                float bd = float.MaxValue;
                foreach (var p in MobileParty.All)
                {
                    if (p == null || !p.IsActive) continue;
                    if (p.IsMainParty || p.IsGarrison || p.IsMilitia || p.IsCaravan) continue;
                    if (p.LeaderHero == null || DefArmy.IsDefArmyParty(p)) continue;
                    if (!ReferenceEquals(p.MapFaction, k)) continue;
                    float d = MinDistToEnemy(p, k);
                    if (d < bd) { bd = d; best = p; }
                }
                return best;
            }
            catch { return null; }
        }

        private static float MinDistToEnemy(MobileParty p, Kingdom k)
        {
            try
            {
                float bd = float.MaxValue;
                var pos = p.Position.ToVec2();
                foreach (var env in Kingdom.All)
                {
                    if (env == null || env.IsEliminated || ReferenceEquals(env, k)) continue;
                    bool war = false;
                    try { war = k.IsAtWarWith(env); } catch { }
                    if (!war) continue;
                    foreach (var s in env.Settlements)
                    {
                        if (s == null) continue;
                        float dx = s.Position.X - pos.X, dy = s.Position.Y - pos.Y;
                        float d = dx * dx + dy * dy;
                        if (d < bd) bd = d;
                    }
                }
                return bd;
            }
            catch { return float.MaxValue; }
        }

        // v4.109: AI 主动进攻(交战国: 鹰派部队围攻/掠夺最近的敌国聚落)
        internal static void MonthlyOffensive(int day)
        {
            try
            {
                var pk = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                foreach (var k in Kingdom.All)
                {
                    if (k == null || k.IsEliminated) continue;
                    if (pk != null && ReferenceEquals(pk, k)) continue;
                    if (WarEconomy.IsCrisis(k)) continue;
                    int wars = 0;
                    float enemyStr = 0f;
                    try
                    {
                        foreach (var x in Kingdom.All)
                        {
                            if (x == null || x.IsEliminated || ReferenceEquals(x, k)) continue;
                            bool war = false;
                            try { war = k.IsAtWarWith(x); } catch { }
                            if (!war) continue;
                            wars++;
                            enemyStr += StrengthOfKingdom(x);
                        }
                    }
                    catch { }
                    if (wars == 0) continue;
                    int agg = AiPersonality.AggressionOf(k);
                    if (MBRandom.RandomFloat > agg / 100f * 0.6f) continue;

                    var troops = FrontierPartyOf(k);
                    if (troops == null) continue;
                    bool strong = StrengthOfParty(troops) >= enemyStr / Math.Max(1, wars) * 1.15f;

                    Settlement target = null;
                    bool isVillage = false;
                    float bd = float.MaxValue;
                    var pos = troops.Position.ToVec2();
                    foreach (var env in Kingdom.All)
                    {
                        if (env == null || env.IsEliminated || ReferenceEquals(env, k)) continue;
                        bool war = false;
                        try { war = k.IsAtWarWith(env); } catch { }
                        if (!war) continue;
                        foreach (var s in env.Settlements)
                        {
                            if (s == null) continue;
                            bool village = s.IsVillage;
                            if (!strong && !village) continue;   // 军力不占优则只打村庄
                            float dx = s.Position.X - pos.X, dy = s.Position.Y - pos.Y;
                            float dist = (float)Math.Sqrt(dx * dx + dy * dy);
                            float value = 1f;
                            int def = 0;
                            try
                            {
                                if (s.Town != null && s.Town.GarrisonParty != null)
                                {
                                    def = s.Town.GarrisonParty.MemberRoster.TotalManCount;
                                    value += s.Town.Prosperity / 100f;
                                }
                                if (village) value += 0.3f;
                            }
                            catch { }
                            // v4.112: 战略目标评分 = 价值高 / 防守弱 / 距离近 优先
                            float score = (dist + 1f) / (1f + def / 150f) / value;
                            if (score < bd) { bd = score; target = s; isVillage = village; }
                        }
                    }
                    if (target == null) continue;
                    try
                    {
                        if (isVillage) troops.SetMoveRaidSettlement(target, MobileParty.NavigationType.Default, false);
                        else troops.SetMoveBesiegeSettlement(target, MobileParty.NavigationType.Default);
                        DLog.Force("AI 进攻: " + k.Name + " 的 " + MapSelection.NameOf(troops)
                            + (isVillage ? " 前往掠夺 " : " 前往围攻 ") + (target.Name != null ? target.Name.ToString() : target.StringId));
                    }
                    catch (Exception ex) { DLog.Info("AI 进攻指令失败: " + ex.Message); }
                }
            }
            catch (Exception ex) { DLog.Force("AI 进攻月结异常: " + ex.Message); }
        }

        private static float StrengthOfParty(MobileParty p)
        {
            try { return p.MemberRoster.TotalManCount; } catch { return 0f; }
        }

        // v4.110: AI 军团集结(交战国鹰派: 边境部队为帅创建原版军团, 召集附近部队一起进攻)
        internal static void MonthlyArmy(int day)
        {
            try
            {
                var pk = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                foreach (var k in Kingdom.All)
                {
                    if (k == null || k.IsEliminated) continue;
                    if (pk != null && ReferenceEquals(pk, k)) continue;
                    if (WarEconomy.IsCrisis(k)) continue;
                    int wars = 0;
                    try { foreach (var x in Kingdom.All) { if (x == null || x.IsEliminated) continue; if (k.IsAtWarWith(x)) wars++; } } catch { }
                    if (wars == 0) continue;
                    int agg = AiPersonality.AggressionOf(k);
                    if (agg < 55) continue;                       // 只有鹰派会组团
                    if (MBRandom.RandomFloat > agg / 100f * 0.5f) continue;

                    var leader = FrontierPartyOf(k);
                    if (leader == null || leader.LeaderHero == null) continue;
                    bool already = false;
                    try { already = leader.Army != null; } catch { }
                    if (already) continue;

                    // 召集附近同国领主部队(未在军团中)
                    var joiners = new List<MobileParty>();
                    var lp = leader.Position.ToVec2();
                    foreach (var p in MobileParty.All)
                    {
                        if (p == null || !p.IsActive || ReferenceEquals(p, leader)) continue;
                        if (p.IsMainParty || p.IsGarrison || p.IsMilitia || p.IsCaravan) continue;
                        if (p.LeaderHero == null || DefArmy.IsDefArmyParty(p)) continue;
                        if (!ReferenceEquals(p.MapFaction, k)) continue;
                        bool inArmy = false;
                        try { inArmy = p.Army != null; } catch { }
                        if (inArmy) continue;
                        float dx = p.Position.X - lp.X, dy = p.Position.Y - lp.Y;
                        if (dx * dx + dy * dy > 150f * 150f) continue;
                        joiners.Add(p);
                        if (joiners.Count >= 3) break;
                    }
                    if (joiners.Count < 1) continue;

                    Settlement target = NearestEnemySettlement(k, leader);
                    try { k.CreateArmy(leader.LeaderHero, target, Army.ArmyTypes.Besieger, null); }
                    catch (Exception ex) { DLog.Info("AI 建军失败: " + ex.Message); }
                    // CreateArmy 无返回值 -> 从王国军团列表里找首领对应军团
                    Army army = null;
                    try
                    {
                        foreach (var a in k.Armies)
                        {
                            if (a == null) continue;
                            if (ReferenceEquals(a.LeaderParty, leader)) { army = a; break; }
                        }
                    }
                    catch { }
                    if (army == null) continue;
                    int n = 0;
                    for (int i = 0; i < joiners.Count; i++)
                    {
                        try { joiners[i].Army = army; n++; } catch { }
                    }
                    DLog.Force("AI 集结军团: " + k.Name + " " + MapSelection.NameOf(leader) + " 为帅, " + n + " 支部队加入"
                        + (target != null && target.Name != null ? (", 目标 " + target.Name.ToString()) : ""));
                }
            }
            catch (Exception ex) { DLog.Force("AI 军团集结异常: " + ex.Message); }
        }

        // v4.111: AI 防守(每日): 被围城市派兵解围; 敌军逼近定居点则回防
        internal static void DailyDefense(int day)
        {
            try
            {
                var pk = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                foreach (var k in Kingdom.All)
                {
                    if (k == null || k.IsEliminated) continue;
                    if (pk != null && ReferenceEquals(pk, k)) continue;

                    // 1) 解围: 被围定居点
                    Settlement besieged = null;
                    try
                    {
                        foreach (var s in k.Settlements)
                        {
                            if (s == null) continue;
                            bool under = false;
                            try { under = s.IsUnderSiege; } catch { }
                            if (under) { besieged = s; break; }
                        }
                    }
                    catch { }
                    if (besieged != null)
                    {
                        var defenders = NearestParties(k, besieged, 2, 220f);
                        for (int i = 0; i < defenders.Count; i++)
                        {
                            try { defenders[i].SetMoveGoToSettlement(besieged, MobileParty.NavigationType.Default, false); } catch { }
                        }
                        if (defenders.Count > 0)
                            DLog.Force("AI 解围: " + k.Name + " 派 " + defenders.Count + " 支部队增援 "
                                + (besieged.Name != null ? besieged.Name.ToString() : "?"));
                        continue;   // 每日每国最多一次响应
                    }

                    // 2) 回防: 敌军部队逼近本国定居点(60 内)
                    Settlement threat = null;
                    try
                    {
                        foreach (var s in k.Settlements)
                        {
                            if (s == null) continue;
                            var pos = s.Position.ToVec2();
                            foreach (var e in MobileParty.All)
                            {
                                if (e == null || !e.IsActive || e.IsMainParty) continue;
                                if (e.IsGarrison || e.IsMilitia || e.IsCaravan) continue;
                                bool war = false;
                                try { war = e.MapFaction != null && k.IsAtWarWith(e.MapFaction); } catch { }
                                if (!war) continue;
                                float dx = e.Position.X - pos.X, dy = e.Position.Y - pos.Y;
                                if (dx * dx + dy * dy < 60f * 60f) { threat = s; break; }
                            }
                            if (threat != null) break;
                        }
                    }
                    catch { }
                    if (threat != null)
                    {
                        var defenders = NearestParties(k, threat, 1, 260f);
                        if (defenders.Count > 0)
                        {
                            try { defenders[0].SetMoveGoToSettlement(threat, MobileParty.NavigationType.Default, false); } catch { }
                            DLog.Info("AI 回防: " + k.Name + " 派 " + MapSelection.NameOf(defenders[0])
                                + " 回防 " + (threat.Name != null ? threat.Name.ToString() : "?"));
                        }
                    }
                }
            }
            catch (Exception ex) { DLog.Force("AI 防守异常: " + ex.Message); }
        }

        private static List<MobileParty> NearestParties(Kingdom k, Settlement s, int max, float radius)
        {
            var list = new List<MobileParty>();
            try
            {
                var pos = s.Position.ToVec2();
                var cand = new List<MobileParty>();
                foreach (var p in MobileParty.All)
                {
                    if (p == null || !p.IsActive || p.IsMainParty) continue;
                    if (p.IsGarrison || p.IsMilitia || p.IsCaravan) continue;
                    if (p.LeaderHero == null || DefArmy.IsDefArmyParty(p)) continue;
                    if (!ReferenceEquals(p.MapFaction, k)) continue;
                    bool busy = false;
                    try { busy = p.Army != null; } catch { }
                    if (busy) continue;   // 已在军团中的不动
                    float dx = p.Position.X - pos.X, dy = p.Position.Y - pos.Y;
                    if (dx * dx + dy * dy > radius * radius) continue;
                    cand.Add(p);
                }
                cand.Sort(delegate (MobileParty a, MobileParty b)
                {
                    return SqDist(a, pos).CompareTo(SqDist(b, pos));
                });
                for (int i = 0; i < cand.Count && i < max; i++) list.Add(cand[i]);
            }
            catch { }
            return list;
        }

        private static float SqDist(MobileParty p, Vec2 pos)
        {
            try { float dx = p.Position.X - pos.X, dy = p.Position.Y - pos.Y; return dx * dx + dy * dy; }
            catch { return float.MaxValue; }
        }

        // v4.112: AI 外交施压 —— 军力碾压的鹰派 AI 向弱邻发起外交博弈索贡(不战而屈人之兵)
        internal static void MonthlyPressure(int day)
        {
            try
            {
                var pk = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                foreach (var k in Kingdom.All)
                {
                    if (k == null || k.IsEliminated) continue;
                    if (pk != null && ReferenceEquals(pk, k)) continue;
                    if (WarEconomy.IsCrisis(k)) continue;
                    int wars = 0;
                    try { foreach (var x in Kingdom.All) { if (x == null || x.IsEliminated) continue; if (k.IsAtWarWith(x)) wars++; } } catch { }
                    if (wars >= 2) continue;   // 两线作战的没空施压
                    int agg = AiPersonality.AggressionOf(k);
                    if (agg < 60) continue;
                    if (MBRandom.RandomFloat > agg / 100f * 0.35f) continue;
                    float my = StrengthOfKingdom(k);
                    Kingdom best = null;
                    float bestRatio = 1.6f;
                    foreach (var b in Kingdom.All)
                    {
                        if (b == null || b.IsEliminated || ReferenceEquals(b, k)) continue;
                        if (Diplomacy.IsAlly(k, b)) continue;
                        bool war = false;
                        try { war = k.IsAtWarWith(b); } catch { }
                        if (war) continue;
                        if (!AiDiplomacy.Borders(k, b)) continue;   // 只压邻国
                        float r = my / Math.Max(1f, StrengthOfKingdom(b));
                        if (r > bestRatio) { bestRatio = r; best = b; }
                    }
                    if (best == null) continue;
                    try
                    {
                        var play = DiploPlays.StartPlay(k, best, false);
                        if (play != null)
                            DLog.Force("AI 施压: " + k.Name + " 向 " + best.Name + " 发起外交博弈索贡(军力 "
                                + bestRatio.ToString("F2") + " 倍)");
                    }
                    catch (Exception ex) { DLog.Info("AI 施压失败: " + ex.Message); }
                }
            }
            catch (Exception ex) { DLog.Force("AI 施压月结异常: " + ex.Message); }
        }

        // v4.113: AI 战略储备与危机自救(用国库从本国市场买粮: 战时囤粮推高粮价, 饥荒时自救)
        internal static void MonthlyEconomy(int day)
        {
            try
            {
                var pk = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                foreach (var k in Kingdom.All)
                {
                    if (k == null || k.IsEliminated) continue;
                    if (pk != null && ReferenceEquals(pk, k)) continue;
                    float gold = WarEconomy.GoldOfPublic(k);
                    if (gold < 800f) continue;
                    int wars = 0;
                    try { foreach (var x in Kingdom.All) { if (x == null || x.IsEliminated) continue; if (k.IsAtWarWith(x)) wars++; } } catch { }
                    bool war = wars > 0;
                    bool crisis = WarEconomy.IsCrisis(k);
                    if (!war && !crisis) continue;   // 平时不囤积

                    // 找存粮最紧的城镇
                    Settlement needTown = null;
                    float needDays = float.MaxValue;
                    try
                    {
                        foreach (var s in k.Settlements)
                        {
                            if (s == null || s.Town == null) continue;
                            var m = EconomyWorld.FindMarket(s.StringId);
                            if (m == null) continue;
                            var e = m.Get(FeudalGoods.Grain);
                            float stock = e != null ? e.Stock : 0f;
                            float cons = e != null ? e.DailyConsumption : 0f;
                            float days = cons > 0.01f ? stock / cons : 99f;
                            if (days < needDays) { needDays = days; needTown = s; }
                        }
                    }
                    catch { }
                    if (needTown == null) continue;
                    var mm = EconomyWorld.FindMarket(needTown.StringId);
                    if (mm == null) continue;
                    var grain = FeudalGoods.Item(FeudalGoods.Grain);
                    if (grain == null) continue;
                    var me = mm.Get(FeudalGoods.Grain);
                    float cons2 = me != null ? me.DailyConsumption : 0f;
                    float stock2 = me != null ? me.Stock : 0f;
                    float wantDays = war ? 30f : 20f;
                    float need = cons2 * wantDays - stock2;
                    if (need <= 0.5f) continue;
                    int buy = (int)Math.Min(need, 150f);
                    float price = me != null && me.Price > 0.1f ? me.Price : 20f;
                    float cost = buy * price;
                    if (gold < cost + 500f)
                    {
                        buy = (int)((gold - 500f) / price);
                        cost = buy * price;
                    }
                    if (buy <= 0) continue;
                    WarEconomy.SpendPublic(k, cost);
                    try { needTown.ItemRoster.AddToCounts(grain, buy); } catch { }   // 入仓(军粮/民食系统读这里)
                    if (me != null) me.Stock = Math.Max(0f, me.Stock - buy);         // 市场买走 -> 供给减少(价格上行)
                    DLog.Force("AI 经济: " + k.Name + " 购入 " + buy + " 粮(费 " + (int)cost + ", "
                        + (war ? "战时储备" : "饥荒自救") + ", " + (needTown.Name != null ? needTown.Name.ToString() : "?") + ")");
                }
            }
            catch (Exception ex) { DLog.Force("AI 经济月结异常: " + ex.Message); }
        }

        // v4.115: AI 对外制裁(每月重建自己的制裁名单: 交战/敌视关系 + 鹰派; 可制裁玩家)
        internal static void MonthlySanctions(int day)
        {
            try
            {
                var pk = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                foreach (var k in Kingdom.All)
                {
                    if (k == null || k.IsEliminated) continue;
                    if (pk != null && ReferenceEquals(pk, k)) continue;
                    WarEconomy.ClearSanctionsBy(k);   // 重建本国名单
                    int agg = AiPersonality.AggressionOf(k);
                    if (agg < 45) continue;
                    foreach (var t in Kingdom.All)
                    {
                        if (t == null || t.IsEliminated || ReferenceEquals(t, k)) continue;
                        bool war = false;
                        try { war = k.IsAtWarWith(t); } catch { }
                        int rel = 0;
                        try { rel = Diplomacy.Get(k, t); } catch { }
                        bool want = false;
                        if (war && MBRandom.RandomFloat < 0.7f) want = true;
                        else if (rel <= -40 && agg >= 60 && MBRandom.RandomFloat < 0.4f) want = true;
                        if (!want) continue;
                        WarEconomy.SetSanction(t, k, true);
                        if (pk != null && ReferenceEquals(t, pk))
                        {
                            MapSelection.Message("⚠ " + (k.Name != null ? k.Name.ToString() : "?") + " 对你实施经济制裁(国库每日 -25% 贸易收入)");
                            DLog.Force("AI 制裁玩家: " + k.Name);
                        }
                    }
                }
            }
            catch (Exception ex) { DLog.Force("AI 制裁月结异常: " + ex.Message); }
        }

        // v4.117: 反霸权外交平衡(军力第一的国家被其它国家敌视: 关系 -6/月; 其它国家互相 +2/月抱团)
        internal static void MonthlyBalanceOfPower(int day)
        {
            try
            {
                Kingdom strongest = null;
                float best = 0f;
                float second = 0f;
                foreach (var k in Kingdom.All)
                {
                    if (k == null || k.IsEliminated) continue;
                    float s = StrengthOfKingdom(k);
                    if (s > best) { second = best; best = s; strongest = k; }
                    else if (s > second) second = s;
                }
                if (strongest == null || second <= 0f) return;
                if (best < second * 1.6f) return;   // 未形成霸权
                int n = 0;
                foreach (var k in Kingdom.All)
                {
                    if (k == null || k.IsEliminated || ReferenceEquals(k, strongest)) continue;
                    Diplomacy.Change(k, strongest, -6);   // 对霸权国敌视
                    n++;
                    foreach (var t in Kingdom.All)
                    {
                        if (t == null || t.IsEliminated || ReferenceEquals(t, k) || ReferenceEquals(t, strongest)) continue;
                        Diplomacy.Change(k, t, 2);        // 互相抱团
                    }
                }
                if (n > 0)
                    DLog.Force("反霸权: " + (strongest.Name != null ? strongest.Name.ToString() : "?")
                        + " 军力第一(" + (int)best + " vs 次 " + (int)second + "), " + n + " 国对其关系 -6, 互相 +2");
            }
            catch (Exception ex) { DLog.Force("反霸权月结异常: " + ex.Message); }
        }

        private static Settlement NearestEnemySettlement(Kingdom k, MobileParty from)
        {
            try
            {
                Settlement best = null;
                float bd = float.MaxValue;
                var pos = from.Position.ToVec2();
                foreach (var env in Kingdom.All)
                {
                    if (env == null || env.IsEliminated || ReferenceEquals(env, k)) continue;
                    bool war = false;
                    try { war = k.IsAtWarWith(env); } catch { }
                    if (!war) continue;
                    foreach (var s in env.Settlements)
                    {
                        if (s == null) continue;
                        float dx = s.Position.X - pos.X, dy = s.Position.Y - pos.Y;
                        float d = dx * dx + dy * dy;
                        if (d < bd) { bd = d; best = s; }
                    }
                }
                return best;
            }
            catch { return null; }
        }

        private static float StrengthOfKingdom(Kingdom k)
        {
            try
            {
                float s = 0f;
                foreach (var p in MobileParty.All)
                {
                    if (p == null || !p.IsActive) continue;
                    if (p.IsGarrison || p.IsMilitia || p.IsCaravan) continue;
                    if (!ReferenceEquals(p.MapFaction, k)) continue;
                    try { s += p.MemberRoster.TotalManCount; } catch { }
                }
                return s;
            }
            catch { return 0f; }
        }
    }
}
