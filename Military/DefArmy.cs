using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace FeudalInternalAffairs
{
    // 野战军团(第 23 章): 一支国防军部队 + 一位番号将军
    internal class DefLegion
    {
        internal string PartyId = "";
        internal string GeneralId = "";
        internal string HomeId = "";
        internal int Number;
        internal int Expected;      // 上次对账后的编制(健康兵)
        internal int Starve;        // 连续断粮天数
        internal string Task = "驻守";
        // v27.x 装备驱动建军(设计 27.2/27.4): 编制步弓骑骑射 / 装备档 / 支援(工具·医院·电报) / 满足率 / 训练
        internal int[] Comp = new int[4];
        internal int EquipTier;                             // 0=旧档未定(27.12 按时代解析); 1~6
        internal bool[] Supports = new bool[3];
        internal float EquipFill = -1f;                     // <0 = 旧档默认 100%(27.15)
        internal float Training = -1f;                      // <0 = 按兵种默认(27.2)
        // v4.243 军队"活"起来: 军团状态 / 训练度(0~100) / 将军特质位掩码
        //   注意: Training 仍是 27.2 的兵种训练系数口径(0.85~1.15), Train 是新的"训练度"
        internal int Stance;                                // 0 戒备 / 1 操练 / 2 休整 / 3 行军 / 4 征粮
        internal float Train = -1f;                         // <0 = 旧档, 首次日结按兵种构成补齐
        internal int GeneralTraits;                         // 将军特质位掩码(见 DefArmy.Trait*)
        // v4.245: 玩家意志优先 —— 玩家对这支军团下过命令后, 军事总监/外交 AI/铁路 AI 一律不许再改道
        internal bool PlayerHold;
        internal int PlayerOrderDay = -1;                   // 最后一条玩家命令的日期(诊断用)
    }

    // 守备营: 我们投放在某镇/堡驻军里的国防军兵员
    internal class DefGarrison
    {
        internal string SettlementId = "";
        internal int Placed;        // 对账后的在册数
        // v4.255: 守备营装备账(型号 -> 件数) —— 募兵时已从国家军械库领走的装备记在这里,
        //   建军/补员时把这批转交军团, 避免同一批兵被领装 2~3 次
        internal Dictionary<string, int> Equip = new Dictionary<string, int>(StringComparer.Ordinal);
    }

    // 征兵批次(v4.239: 强制征兵 —— 无服役时效, 一直服役到 解甲归田/转常备/战损; 代价走民怨与请愿)
    internal class DefConscript
    {
        internal string SettlementId = "";
        internal int Count;
        internal int ExpireDay = -1;   // -1 = 无时效(强制征兵)
        internal bool Standing;
    }

    // 国防军核心系统(设计: 建筑与经济系统设计文档 第 23 章 v4.53)
    //   铁律: 农民↔士兵 1:1(总人口不变) / 装备扣库存 / 军饷军粮真实出库 /
    //         将军随机无属性 / 无自动补员 / 伤亡对账 / AI 没有国防军
    internal static class DefArmy
    {
        internal const int MaxLegionMen = 1000000;      // v4.89: 取消单军上限(100万≈无上限, 避免原版数值溢出)
        internal const float SpeedLock = 3.0f;          // 速度锁死
        internal const float WagePerManPerDay = 0.5f;   // 军饷/兵/日
        // v4.252: 军粮/兵/日 0.02 -> 0.006 —— 审计实测: 按 0.02 算, 全球军队日耗是粮食产出的 4~5 倍,
        //   于是所有国家长期判"饥荒"-> 部队每天 -2% 且 AI 进危机后**停止建设**(死亡螺旋)。
        internal const float FoodPerManPerDay = 0.006f;  // 军粮/兵/日
        internal const int RecruitCost = 20;            // 募兵/兵
        internal const int ConscriptCost = 5;           // 征兵/兵
        internal const int LegionFormCost = 200;       // 建军
        internal const int GeneralOutfitCost = 0;   // 已废除(置装价移除)
        internal const int LegionTrainCostPerMan = 10; // 训练费/人
        internal const int SplitCost = 500;              // 拆分(新建部队)
        internal const int DisbandCost = 15;            // 遣散费/兵
        internal const int GeneralWageMonth = 30;       // 将军月薪
        internal const int LegionMinMen = 100;          // 成立/拆分下限
        internal const int LegionKeepMin = 1;           // v4.235: 拆分后两边各至少留的正兵数(原 50 已取消)

        // ==================== v4.243: 军团状态(参考 V3 军队命令/姿态) ====================
        //   5 种状态, 每日结算各有取舍; 军饷/军粮/训练度/熟练度/士气/组织度/聚落反应都跟着变
        internal const int StanceStandby = 0, StanceDrill = 1, StanceRest = 2, StanceMarch = 3, StanceForage = 4;
        internal static readonly string[] StanceNames = { "戒备", "操练", "休整", "行军", "征粮" };
        internal static string StanceHelp(int s)
        {
            switch (s)
            {
                case StanceDrill: return "操练: 训练度 +0.35/日 · 熟练度 +0.15/人/日 · 组织度恢复 ×1.5; 军饷 ×1.3";
                case StanceRest: return "休整: 士气 +1.5/日 · 组织度恢复 ×2 · 伤员治疗 ×2; 战斗输出 -10%";
                case StanceMarch: return "行军: 军粮消耗 ×1.4 · 训练度 -0.05/日 · 组织度 -0.5/日";
                case StanceForage: return "征粮: 军粮自给(不再出库); 所在聚落 忠诚 -0.4/日 · 民怨 +0.2/日";
                default: return "戒备: 战斗输出 +5% · 组织度/士气小幅恢复(默认状态)";
            }
        }

        // ==================== v4.243: 将军特质(参考 V3 将军特质) ====================
        internal const int TraitArtillery = 1, TraitCavalry = 2, TraitInfantry = 4, TraitLogistics = 8,
            TraitTrench = 16, TraitDiscipline = 32, TraitPaternal = 64, TraitPolitical = 128;
        internal static readonly string[] TraitNames = { "炮兵专家", "骑兵将领", "步兵操典家", "后勤天才", "堑壕老兵", "严酷军纪", "爱兵如子", "政治将军" };
        internal static readonly int[] TraitBits = { TraitArtillery, TraitCavalry, TraitInfantry, TraitLogistics, TraitTrench, TraitDiscipline, TraitPaternal, TraitPolitical };
        internal static string TraitHelp(int bit)
        {
            switch (bit)
            {
                case TraitArtillery: return "炮击阶段 +20%";
                case TraitCavalry: return "追击缴获 +25%";
                case TraitInfantry: return "近战 +10% · 训练度 +0.1/日";
                case TraitLogistics: return "军粮消耗 -25% · 缺粮惩罚减半";
                case TraitTrench: return "承受伤亡 -15%";
                case TraitDiscipline: return "组织度 +10 · 士气 -5 · 征丁逃亡 -50%";
                case TraitPaternal: return "士气 +10 · 伤员治疗 ×1.5";
                case TraitPolitical: return "合法性 +1/月 · 战斗输出 -5%";
            }
            return "";
        }

        internal static bool HasTrait(DefLegion lg, int bit)
        {
            try { return lg != null && (lg.GeneralTraits & bit) != 0; } catch { return false; }
        }

        internal static bool HasTrait(MobileParty p, int bit)
        {
            try { return HasTrait(LegionOf(p), bit); } catch { return false; }
        }

        internal static string TraitText(int mask)
        {
            try
            {
                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < TraitBits.Length; i++)
                {
                    if ((mask & TraitBits[i]) == 0) continue;
                    if (sb.Length > 0) sb.Append(" · ");
                    sb.Append(TraitNames[i]);
                }
                return sb.Length > 0 ? sb.ToString() : "无特质";
            }
            catch { return "无特质"; }
        }

        // 将军特质生成: 技能越高给得越多(1~3 个), 与本军团主兵种倾向挂钩
        private static int RollGeneralTraits(Hero h, int[] comp)
        {
            try
            {
                int mask = 0;
                var rnd = new Random((h != null && h.StringId != null ? h.StringId.GetHashCode() : 12345) + Politics.Today());
                int skill = 0;
                try
                {
                    if (h != null)
                        skill = (h.GetSkillValue(DefaultSkills.Leadership) + h.GetSkillValue(DefaultSkills.Tactics)
                            + h.GetSkillValue(DefaultSkills.Steward)) / 3;
                }
                catch { }
                int cnt = skill >= 30 ? 3 : (skill >= 20 ? 2 : 1);
                var pool = new List<int>();
                int art = 0, cav = 0, inf = 0;
                if (comp != null && comp.Length >= 4) { inf = comp[0]; cav = comp[2] + comp[3]; art = comp[1]; }
                if (art > 0) pool.Add(TraitArtillery);
                if (cav > inf) pool.Add(TraitCavalry);
                if (inf >= cav) pool.Add(TraitInfantry);
                int[] rest = { TraitLogistics, TraitTrench, TraitDiscipline, TraitPaternal, TraitPolitical, TraitArtillery, TraitCavalry, TraitInfantry };
                for (int i = 0; i < rest.Length; i++) pool.Add(rest[i]);
                for (int n = 0; n < cnt && pool.Count > 0; n++)
                {
                    int at = rnd.Next(pool.Count);
                    mask |= pool[at];
                    pool.RemoveAt(at);
                }
                return mask;
            }
            catch { return 0; }
        }

        internal static string ClanId = "";
        internal static readonly List<DefLegion> Legions = new List<DefLegion>();
        internal static readonly List<DefGarrison> Garrisons = new List<DefGarrison>();
        internal static readonly List<DefConscript> Conscripts = new List<DefConscript>();
        internal static readonly Dictionary<string, Dictionary<string, int>> LegionEquip = new Dictionary<string, Dictionary<string, int>>();   // 军团装备快照(partyId -> 型号:件数)
        private static bool EquipMigrationPending;   // 旧档 FIA_LegEquip 迁移(Relink 后部队名单可用时执行)
        internal static int UnpaidDays;
        internal static int TodayMilLoss;      // 今日军损(统计翻牌)
        internal static bool MutinyWarned;
        private static int _lastSettledDay = -1;   // v4.255: 上次已日结的游戏日(同日防重, 随存档落盘)

        private static Clan _clan;
        private static readonly Dictionary<string, CharacterObject[]> TroopCache = new Dictionary<string, CharacterObject[]>();

        internal static Kingdom OurKingdom
        {
            get
            {
                try
                {
                    var b = NationalWillOrders.Behavior;
                    return b != null ? b.NationKingdom : null;
                }
                catch { return null; }
            }
        }

        // ==================== 判定 ====================
        internal static bool IsDefArmyParty(MobileParty p)
        {
            try
            {
                if (p == null) return false;
                // 不再依赖自定义家族: 用部队 id 前缀 + 军团登记表判定
                if (p.StringId != null && p.StringId.StartsWith("fia_def_", StringComparison.Ordinal)) return true;
                return LegionOf(p) != null;
            }
            catch { return false; }
        }

        internal static DefLegion LegionOf(MobileParty p)
        {
            try
            {
                if (p == null) return null;
                for (int i = 0; i < Legions.Count; i++)
                    if (Legions[i].PartyId == p.StringId) return Legions[i];
            }
            catch { }
            return null;
        }

        internal static int TotalMen()
        {
            int n = 0;
            try
            {
                for (int i = 0; i < Legions.Count; i++)
                {
                    var p = FindParty(Legions[i].PartyId);
                    if (p != null && p.IsActive) n += RegularsOf(p);   // v4.123: 士兵数(不含将军)
                    else n += Legions[i].Expected;
                }
                for (int i = 0; i < Garrisons.Count; i++) n += Garrisons[i].Placed;
            }
            catch { }
            return n;
        }

        internal static int DailyWageCost()
        {
            // v4.243: 逐军团按状态算(操练状态军饷 ×1.3)
            try
            {
                double sum = 0.0;
                for (int i = 0; i < Legions.Count; i++)
                {
                    var lg = Legions[i];
                    var p = LegionParty(lg);
                    if (p == null || !p.IsActive) continue;
                    sum += RegularsOf(p) * WagePerManPerDay * WageMultOf(lg);
                }
                // v4.255: 兜底改成 0 —— 原来 sum<=0 时按 TotalMen()(含守备营)收费,
                //   "没有活跃野战军团但有守备营"时会突然按守备营人数扣国库(守备营平时不进军饷账)
                if (sum <= 0.0) return 0;
                return (int)Math.Ceiling(sum);
            }
            catch { return 0; }
        }

        // 按当前编制, 国库还能撑多少天
        internal static int DaysAffordable()
        {
            try
            {
                int cost = DailyWageCost();
                if (cost <= 0) return 999;
                return EconomyWorld.Treasury.Gold / cost;
            }
            catch { return 0; }
        }

        // ==================== 兵种(原版 T2) ====================
        // 返回 [近战, 远程, 骑兵(可空)]
        internal static CharacterObject[] TroopsOf(CultureObject culture)
        {
            try
            {
                if (culture == null) return new CharacterObject[3];
                CharacterObject[] cached;
                string key = culture.StringId;
                if (string.IsNullOrEmpty(key)) key = culture.Name != null ? culture.Name.ToString() : "empire";
                if (TroopCache.TryGetValue(key, out cached)) return cached;

                var pool = new List<CharacterObject>();
                AddUpgrades(pool, culture.BasicTroop, culture);
                AddUpgrades(pool, culture.EliteBasicTroop, culture);
                AddUpgrades(pool, culture.MeleeMilitiaTroop, culture);
                AddUpgrades(pool, culture.RangedMilitiaTroop, culture);
                AddUpgrades(pool, culture.MeleeEliteMilitiaTroop, culture);
                AddUpgrades(pool, culture.RangedEliteMilitiaTroop, culture);
                if (pool.Count == 0)
                {
                    try
                    {
                        foreach (var t in CharacterObject.FindAll(delegate (CharacterObject x)
                        { return x != null && !x.IsHero && x.Tier == 2 && x.Culture != null && x.Culture.StringId == key; }))
                        { if (!pool.Contains(t)) pool.Add(t); }
                    }
                    catch { }
                }

                CharacterObject inf = null, rng = null, cav = null;
                for (int i = 0; i < pool.Count; i++)
                {
                    var t = pool[i];
                    if (t == null) continue;
                    if (t.IsMounted) { if (cav == null) cav = t; }
                    else if (t.IsRanged) { if (rng == null) rng = t; }
                    else if (inf == null) inf = t;
                }
                if (inf == null) inf = rng != null ? rng : cav;
                if (rng == null) rng = inf;
                var arr = new[] { inf, rng, cav };
                TroopCache[key] = arr;
                DLog.Force("国防军: 兵种解析(" + key + ") 近战=" + NameOf(inf) + " 远程=" + NameOf(rng) + " 骑兵=" + NameOf(cav));
                return arr;
            }
            catch (Exception ex) { DLog.Force("国防军: 兵种解析失败 " + ex.Message); return new CharacterObject[3]; }
        }

        private static void AddUpgrades(List<CharacterObject> pool, CharacterObject root, CultureObject culture)
        {
            try
            {
                if (root == null || root.UpgradeTargets == null) return;
                for (int i = 0; i < root.UpgradeTargets.Length; i++)
                {
                    var t = root.UpgradeTargets[i];
                    if (t == null || t.IsHero || t.Tier != 2) continue;
                    if (culture != null && t.Culture != null && t.Culture.StringId != culture.StringId) continue;
                    if (!pool.Contains(t)) pool.Add(t);
                }
            }
            catch { }
        }

        private static string NameOf(CharacterObject c)
        {
            try { return c != null ? c.Name.ToString() : "—"; } catch { return "—"; }
        }

        private static int[] Compose(int n, CharacterObject cav)
        {
            int c = cav != null ? (int)Math.Round(n * 0.05f) : 0;
            int r = (int)Math.Round((n - c) * 0.35f);
            int i = n - c - r;
            if (i < 0) i = 0;
            return new[] { i, r, c };
        }

        // 按 60/35/5 编成一批原版 T2
        private static void AddComposition(TroopRoster roster, CultureObject culture, int n)
        {
            if (roster == null || culture == null || n <= 0) return;
            var t = TroopsOf(culture);
            var want = Compose(n, t[2]);
            for (int k = 0; k < 3; k++)
            {
                if (t[k] == null || want[k] <= 0) continue;
                roster.AddToCounts(t[k], want[k], false, 0, 0, true, -1);
            }
        }

        // v4.106: AI 扩军用(按文化补兵)
        internal static void AddCompositionPublic(TroopRoster roster, CultureObject culture, int n)
        {
            AddComposition(roster, culture, n);
        }

        internal static int CountDefTroops(TroopRoster roster, CultureObject culture)
        {
            try
            {
                if (roster == null || culture == null) return 0;
                var t = TroopsOf(culture);
                int n = 0;
                for (int k = 0; k < 3; k++) if (t[k] != null) n += roster.GetTroopCount(t[k]);
                return n;
            }
            catch { return 0; }
        }

        // 从 from 移 n 名 T2 到 to(不足则尽力而为), 返回实际移动数
        //   want3 = 逐分支(近战/远程/骑兵)想要的人数; null = 按默认 60/35/5
        private static int MoveDefTroops(TroopRoster from, TroopRoster to, CultureObject culture, int n)
        {
            return MoveDefTroops(from, to, culture, n, null);
        }

        private static int MoveDefTroops(TroopRoster from, TroopRoster to, CultureObject culture, int n, int[] want3)
        {
            try
            {
                if (from == null || to == null || culture == null || n <= 0) return 0;
                var t = TroopsOf(culture);
                var want = (want3 != null && want3.Length >= 3) ? want3 : Compose(n, t[2]);
                int moved = 0;
                for (int k = 0; k < 3; k++)
                {
                    if (t[k] == null || want[k] <= 0) continue;
                    int avail = from.GetTroopCount(t[k]);
                    int take = Math.Min(want[k], avail);
                    if (take <= 0) continue;
                    from.AddToCounts(t[k], -take, false, 0, 0, true, -1);
                    to.AddToCounts(t[k], take, false, 0, 0, true, -1);
                    moved += take;
                }
                int guard = 0;
                while (moved < n && guard++ < 60)
                {
                    bool any = false;
                    for (int k = 0; k < 3; k++)
                    {
                        if (moved >= n) break;
                        if (t[k] == null) continue;
                        int avail = from.GetTroopCount(t[k]);
                        if (avail <= 0) continue;
                        int take = Math.Min(avail, n - moved);
                        from.AddToCounts(t[k], -take, false, 0, 0, true, -1);
                        to.AddToCounts(t[k], take, false, 0, 0, true, -1);
                        moved += take; any = true;
                    }
                    if (!any) break;
                }
                return moved;
            }
            catch (Exception ex) { DLog.Force("国防军: 调兵失败 " + ex.Message); return 0; }
        }

        private static int RemoveDefTroops(TroopRoster roster, CultureObject culture, int n)
        {
            try
            {
                if (roster == null || culture == null || n <= 0) return 0;
                var t = TroopsOf(culture);
                int removed = 0;
                int guard = 0;
                while (removed < n && guard++ < 60)
                {
                    bool any = false;
                    for (int k = 0; k < 3; k++)
                    {
                        if (removed >= n) break;
                        if (t[k] == null) continue;
                        int avail = roster.GetTroopCount(t[k]);
                        if (avail <= 0) continue;
                        int take = Math.Min(avail, n - removed);
                        roster.AddToCounts(t[k], -take, false, 0, 0, true, -1);
                        removed += take; any = true;
                    }
                    if (!any) break;
                }
                return removed;
            }
            catch { return 0; }
        }

        // ==================== 人口 1:1 ====================
        private static PopRecord PopOf(Settlement s, string prof)
        {
            try
            {
                if (s == null || string.IsNullOrEmpty(s.StringId)) return null;
                string culture = s.Culture != null ? s.Culture.StringId : "empire";
                return Pops.GetOrCreate(s.StringId, prof, culture);
            }
            catch { return null; }
        }

        internal static int CommonersOf(Settlement s)
        {
            try
            {
                if (s == null) return 0;
                var main = PopOf(s, s.IsVillage ? PopDefs.Peasants : PopDefs.Laborers);
                return main != null ? (int)main.Size : 0;
            }
            catch { return 0; }
        }

        // 从聚落抽 n 人转为士兵(1:1): 主职业不足 -> 次职业 -> 附属村庄
        private static int TakeCommoners(Settlement s, int n)
        {
            int taken = 0;
            if (s.IsVillage)
            {
                taken += TakeFrom(s, PopDefs.Peasants, n - taken);
                taken += TakeFrom(s, PopDefs.Laborers, n - taken);
            }
            else
            {
                taken += TakeFrom(s, PopDefs.Laborers, n - taken);
                taken += TakeFrom(s, PopDefs.Peasants, n - taken);
                if (taken < n)
                {
                    try
                    {
                        foreach (var v in Settlement.All)
                        {
                            if (taken >= n) break;
                            if (v == null || !v.IsVillage || v.Village == null) continue;
                            if (!ReferenceEquals(v.Village.Bound, s)) continue;
                            taken += TakeFrom(v, PopDefs.Peasants, n - taken);
                        }
                    }
                    catch { }
                }
            }
            var soldHost = GarrisonTarget(s) != null ? GarrisonTarget(s) : s;
            var sold = PopOf(soldHost, PopDefs.Soldiers);
            if (sold != null) sold.Size += taken;
            return taken;
        }

        // v4.122: 实际可征平民合计(与 TakeCommoners 的候选口径一致)
        internal static int RealCommonersOf(Settlement s)
        {
            try
            {
                if (s == null) return 0;
                int n = 0;
                if (s.IsVillage)
                {
                    n += SizeOf(s, PopDefs.Peasants);
                    n += SizeOf(s, PopDefs.Laborers);
                }
                else
                {
                    n += SizeOf(s, PopDefs.Laborers);
                    n += SizeOf(s, PopDefs.Peasants);
                    foreach (var v in Settlement.All)
                    {
                        if (v == null || !v.IsVillage || v.Village == null) continue;
                        if (!ReferenceEquals(v.Village.Bound, s)) continue;
                        n += SizeOf(v, PopDefs.Peasants);
                    }
                }
                return n;
            }
            catch { return 0; }
        }

        private static int SizeOf(Settlement s, string prof)
        {
            try { var r = PopOf(s, prof); return r != null ? (int)r.Size : 0; } catch { return 0; }
        }

        private static int TakeFrom(Settlement s, string prof, int want)
        {
            if (want <= 0) return 0;
            var r = PopOf(s, prof);
            if (r == null || r.Size <= 0.5f) return 0;
            int take = (int)Math.Min(want, Math.Floor(r.Size));
            if (take <= 0) return 0;
            r.Size -= take;
            return take;
        }

        private static void AddSoldiersPop(Settlement s, int n)
        {
            if (n <= 0) return;
            var sold = PopOf(s, PopDefs.Soldiers);
            if (sold != null) sold.Size += n;
        }

        private static void RemoveSoldiersPop(Settlement s, int n)
        {
            if (n <= 0 || s == null) return;
            var sold = PopOf(s, PopDefs.Soldiers);
            if (sold == null) return;
            int take = Math.Min(n, (int)Math.Floor(sold.Size));
            sold.Size -= take;
            // v4.255(P0-2): 人口扣减的唯一入口 —— 只在真实减员处登记一次供 UI 显示
            //   (BattleSim 里对全部军团按兵力 5%/12% 的重复扣减已删除)
            try { ArmyDoctrine.NoteSoldierPopLoss(s.Name != null ? s.Name.ToString() : s.StringId, take); } catch { }
        }

        // 士兵解甲归田(1:1 回农民/劳工)
        private static void ReturnSoldiersToCommoners(Settlement s, int n)
        {
            if (n <= 0 || s == null) return;
            var sold = PopOf(s, PopDefs.Soldiers);
            if (sold == null) return;
            int take = Math.Min(n, (int)Math.Floor(sold.Size));
            if (take <= 0) return;
            sold.Size -= take;
            var back = PopOf(s, s.IsVillage ? PopDefs.Peasants : PopDefs.Laborers);
            if (back != null) back.Size += take;
            else AddSoldiersPop(s, take);
        }

        // ==================== 装备 / 钱 ====================
        // v27.x 募兵领装(新体系): 逐人 1 武器 + 1 护甲, 型号按兵种需求(步/弓/骑)从国家军械库现货挑
        internal static Dictionary<string, int> RecruitEquipNeed(int n, CultureObject culture)
        {
            var need = new Dictionary<string, int>(StringComparer.Ordinal);
            try
            {
                if (n <= 0) return need;
                var t = TroopsOf(culture);
                var want = Compose(n, t != null && t.Length > 2 ? t[2] : null);
                string key = Armory.NationalOwner(OurKingdom);
                AddNeedTag(need, key, want[0], EquipTag.AnyWeapon);
                AddNeedTag(need, key, want[1], EquipTag.BowOrCrossbow);
                if (!AddNeedTag(need, key, want[2], EquipTag.Saber) && want[2] > 0)
                    AddNeedTag(need, key, want[2], EquipTag.AnyWeapon);
                AddNeedTag(need, key, n, EquipTag.LightArmor);
            }
            catch { }
            return need;
        }

        private static bool AddNeedTag(Dictionary<string, int> need, string ownerKey, int men, EquipTag tag)
        {
            try
            {
                if (need == null || men <= 0) return false;
                string id = BestStockFor(ownerKey, tag);
                if (string.IsNullOrEmpty(id))
                {
                    var pick = Equipment.PickForTag(tag, 6);
                    if (pick != null) id = pick.Id;
                }
                if (string.IsNullOrEmpty(id)) return false;
                int old;
                need.TryGetValue(id, out old);
                need[id] = old + men;
                return true;
            }
            catch { return false; }
        }

        // 从国家军械库现货里挑该标签的最佳型号(本国已解锁: tier 高者, 同档价高者)
        private static string BestStockFor(string ownerKey, EquipTag tag)
        {
            try
            {
                if (string.IsNullOrEmpty(ownerKey)) return null;
                string bestId = null;
                int bestTier = -1, bestPrice = -1;
                var k = OurKingdom;
                var entries = Armory.EntriesOf(ownerKey);
                for (int i = 0; i < entries.Count; i++)
                {
                    var kv = entries[i];
                    if (kv.Value <= 0) continue;
                    var def = Equipment.Get(kv.Key);
                    if (def == null || !Equipment.MatchesTag(def, tag)) continue;
                    if (!Research.UnlockedFor(k, def.TechId)) continue;
                    if (def.Tier > bestTier || (def.Tier == bestTier && def.Price > bestPrice))
                    { bestId = kv.Key; bestTier = def.Tier; bestPrice = def.Price; }
                }
                return bestId;
            }
            catch { return null; }
        }

        // 从国家军械库领装(逐型号尽量领足), 返回实际领到件数
        private static int TakeEquipFromArmory(Dictionary<string, int> need)
        {
            return TakeEquipFromArmory(need, null);
        }

        // v4.255: 带"实际领到明细"的重载 —— 供募兵记守备营装备账(避免建军时重复领装)
        private static int TakeEquipFromArmory(Dictionary<string, int> need, Dictionary<string, int> taken)
        {
            int got = 0;
            try
            {
                if (need == null || need.Count == 0) return 0;
                string key = Armory.NationalOwner(OurKingdom);
                if (string.IsNullOrEmpty(key)) return 0;
                foreach (var kv in need)
                {
                    int g = Armory.TakeKey(key, kv.Key, kv.Value);
                    got += g;
                    if (g > 0 && taken != null)
                    {
                        int old;
                        taken.TryGetValue(kv.Key, out old);
                        taken[kv.Key] = old + g;
                    }
                }
            }
            catch { }
            return got;
        }

        // v4.255: 把本次领到的装备记进守备营装备账
        private static void AddGarrisonEquip(DefGarrison rec, Dictionary<string, int> taken)
        {
            try
            {
                if (rec == null || taken == null || taken.Count == 0) return;
                if (rec.Equip == null) rec.Equip = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (var kv in taken)
                {
                    if (kv.Value <= 0 || string.IsNullOrEmpty(kv.Key)) continue;
                    int old;
                    rec.Equip.TryGetValue(kv.Key, out old);
                    rec.Equip[kv.Key] = old + kv.Value;
                }
            }
            catch { }
        }

        // v4.255: 建军/补员时把这批兵"在守备营账上已有的装备"转进军团快照,
        //   使随后的 EquipLegion 只补 need - have(原来同一批兵在募兵和建军时各领一次)
        private static void TransferGarrisonEquip(DefLegion lg, DefGarrison rec, int men, int leftInGarrison)
        {
            try
            {
                if (lg == null || rec == null || rec.Equip == null || rec.Equip.Count == 0 || men <= 0) return;
                var need = NeedOf(lg);
                if (need == null || need.Count == 0) return;
                var snap = SnapshotOf(lg, true);
                if (snap == null) return;
                float share = men / (float)Math.Max(1, men + Math.Max(0, leftInGarrison));
                int moved = 0;
                foreach (var kv in need)
                {
                    int have;
                    if (!rec.Equip.TryGetValue(kv.Key, out have) || have <= 0) continue;
                    int take = Math.Min(have, kv.Value);
                    int cap = (int)Math.Ceiling(have * share);
                    if (take > cap) take = cap;
                    if (take <= 0) continue;
                    int cur;
                    snap.TryGetValue(kv.Key, out cur);
                    snap[kv.Key] = cur + take;
                    rec.Equip[kv.Key] = have - take;
                    moved += take;
                }
                if (moved > 0)
                    DLog.Force("军械库: 守备营装备转交军团 " + moved + " 件(避免重复领装)");
            }
            catch { }
        }

        private static bool Spend(int v)
        {
            try
            {
                if (v <= 0) return true;
                if (EconomyWorld.Treasury.Gold < v) return false;
                EconomyWorld.TreasurySpend(v);
                return true;
            }
            catch { return false; }
        }

        // ==================== 守备营 ====================
        private static Settlement GarrisonTarget(Settlement s)
        {
            try
            {
                if (s == null) return null;
                if (s.IsTown || s.IsCastle) return s;
                if (s.IsVillage && s.Village != null && s.Village.Bound != null) return s.Village.Bound;
            }
            catch { }
            return null;
        }

        internal static MobileParty GarrisonPartyOf(Settlement s)
        {
            try
            {
                if (s == null) return null;
                foreach (var p in s.Parties)
                    if (p != null && p.IsActive && p.IsGarrison) return p;
                if (s.IsTown || s.IsCastle)
                {
                    s.AddGarrisonParty();
                    foreach (var p in s.Parties)
                        if (p != null && p.IsActive && p.IsGarrison) return p;
                }
            }
            catch { }
            return null;
        }

        private static DefGarrison GetGarrisonRec(string sid, bool create)
        {
            try
            {
                NormalizeGarrisons();   // v4.86: 先合并同城重复记录(同一城市多次募兵/旧档残留)
                for (int i = 0; i < Garrisons.Count; i++) if (Garrisons[i].SettlementId == sid) return Garrisons[i];
                if (!create) return null;
                var g = new DefGarrison { SettlementId = sid };
                Garrisons.Add(g);
                return g;
            }
            catch { return null; }
        }

        // v4.86: 同一聚落重复募兵(含来源村庄不同)只保留一条守备营记录; 旧存档重复项一并合并(用户要求)
        internal static void NormalizeGarrisons()
        {
            try
            {
                for (int i = Garrisons.Count - 1; i >= 0; i--)
                {
                    var a = Garrisons[i];
                    if (a == null || string.IsNullOrEmpty(a.SettlementId)) { Garrisons.RemoveAt(i); continue; }
                    for (int j = i - 1; j >= 0; j--)
                    {
                        var b = Garrisons[j];
                        if (b == null || b.SettlementId != a.SettlementId) continue;
                        b.Placed = Math.Max(b.Placed, a.Placed);
                        // v4.255: 装备账一并合并(否则合并后这批装备凭空消失, 建军时又要重新领)
                        if (a.Equip != null && a.Equip.Count > 0)
                        {
                            if (b.Equip == null) b.Equip = new Dictionary<string, int>(StringComparer.Ordinal);
                            foreach (var kv in a.Equip)
                            {
                                int old;
                                b.Equip.TryGetValue(kv.Key, out old);
                                b.Equip[kv.Key] = old + kv.Value;
                            }
                        }
                        Garrisons.RemoveAt(i);
                        DLog.Force("国防军: 守备营合并 " + a.SettlementId + " -> 一条记录(兵力 " + b.Placed + ")");
                        break;
                    }
                }
            }
            catch { }
        }

        internal static int GarrisonMen(DefGarrison g)
        {
            try
            {
                if (g == null) return 0;
                var s = FindSettlement(g.SettlementId);
                var party = GarrisonPartyOf(s);
                if (party == null || s == null || s.Culture == null) return g.Placed;
                int live = CountDefTroops(party.MemberRoster, s.Culture);
                // v4.90: 只算"我们募入的兵"(账面), 原版驻军自带的同名兵不计入(用户: 募100应显示100)
                return Math.Min(g.Placed, live);
            }
            catch { return g != null ? g.Placed : 0; }
        }

        // v4.90: 按聚落查守备营兵力(账面口径)
        internal static int GarrisonMenOf(string settlementId)
        {
            try
            {
                var g = GetGarrisonRec(settlementId, false);
                return g != null ? GarrisonMen(g) : 0;
            }
            catch { return 0; }
        }

        // v4.84: 失败/拒绝也写日志(用户"点了没反应"时便于定位), 并把当前国库写进提示
        private static string Fail(string msg)
        {
            try { DLog.Force("国防军: " + msg); } catch { }
            return msg;
        }

        // ==================== 募兵 ====================
        // 三要素: 人(1:1) / 钱(20/兵) / 装备(从国家军械库领装: 逐人 1 武器 + 1 护甲, 按兵种需求)
        internal static string Recruit(Settlement s, int n)
        {
            try
            {
                if (OurKingdom == null) return Fail("尚未建立国家意志");
                if (s == null || n <= 0) return Fail("请先选择募兵地");
                int townHallDiscount = Politics.SeatHeld(3) ? 1 : 0;   // 军务大臣: 募兵费 -10%
                int cost = (int)Math.Round(n * RecruitCost * (townHallDiscount == 1 ? 0.9f : 1f));
                if (EconomyWorld.Treasury.Gold < cost) return Fail("国库不足: 需要 " + cost.ToString("N0") + " 第纳尔(现有 " + EconomyWorld.Treasury.Gold.ToString("N0") + ")");

                int avail = CommonersOf(s);
                if (avail < n) return Fail("可转人口不足: " + s.Name + " 只有 " + avail + " 人可征募");
                var target = GarrisonTarget(s);
                if (target == null) return Fail("该聚落没有可驻防的城镇/城堡");
                // v4.266: 军事建筑"军营"(@garrison 20/35/55 每级) 接进守备营上限(基础上限 600 + 军营加成)
                int gcap = MilBuildings.GarrisonCapOf(target);
                int ghave = GarrisonMenOf(target.StringId);
                int room = gcap - ghave;
                if (room <= 0) return Fail("守备营已满(" + ghave + "/" + gcap + "): 建「军营」提高守备营上限");
                if (n > room) n = room;
                var garrison = GarrisonPartyOf(target);
                if (garrison == null) return Fail("驻军不存在: " + target.Name);

                int moved = TakeCommoners(s, n);
                if (moved <= 0) return Fail("可转人口不足");
                if (moved < n) n = moved;

                Dictionary<string, int> taken;   // v4.255: 本次实际领到的装备
                string equipMsg = DrawRecruitEquip(n, s.Culture, out taken);

                AddComposition(garrison.MemberRoster, s.Culture, n);
                var rec = GetGarrisonRec(target.StringId, true);
                if (rec != null) rec.Placed += n;
                AddGarrisonEquip(rec, taken);   // v4.255: 已领装备记进守备营账(建军/补员时转交, 不再重复领)

                Spend(cost);
                Fiscal.AddMilitary(cost);
                string msg = "募兵 " + n + " 人 → " + target.Name + "守备营 · 花费 " + cost.ToString("N0") + " 第纳尔" + equipMsg;
                DLog.Force("国防军: " + msg);
                return msg;
            }
            catch (Exception ex) { DLog.Force("募兵失败: " + ex); return "募兵失败, 见日志"; }
        }

        // 领装: 按新兵编成(步/弓/骑)从国家军械库逐型号尽量领足; 返回"军械库领装 N%"文案
        internal static string DrawRecruitEquip(int n, CultureObject culture)
        {
            Dictionary<string, int> taken;
            return DrawRecruitEquip(n, culture, out taken);
        }

        // v4.255: 增加 out taken(实际领到的型号/件数) —— 由调用方记进守备营装备账
        internal static string DrawRecruitEquip(int n, CultureObject culture, out Dictionary<string, int> taken)
        {
            taken = new Dictionary<string, int>(StringComparer.Ordinal);
            try
            {
                var need = RecruitEquipNeed(n, culture);
                int needT = 0;
                foreach (var kv in need) needT += kv.Value;
                if (needT <= 0) return "";
                int gotT = TakeEquipFromArmory(need, taken);
                int fill = (int)Math.Round(100.0 * gotT / needT);
                if (fill > 100) fill = 100;
                if (fill < 100) return " · 军械库领装 " + fill + "%(缺 " + (needT - gotT) + " 件)";
                return " · 军械库领装 100%";
            }
            catch { return ""; }
        }

        // ==================== 家族 / 将军 ====================
        private static Settlement FirstOwnedSettlement()
        {
            try
            {
                var k = OurKingdom;
                if (k != null)
                {
                    foreach (var s in Settlement.All)
                        if (s != null && (s.IsTown || s.IsCastle) && ReferenceEquals(s.MapFaction, k)) return s;
                }
            }
            catch { }
            return null;
        }

        // 注意: 不再创建原版新家族!
        // 新建 Clan 会与原版大量系统(财政/决策/旗帜/家族UI/继承)交互, 缺一项就原生崩溃且补不完
        // (0.2 版早期曾建"国防军家族", 因无领袖/初始化不全导致读档后崩溃)。
        // 改为: 将军直接隶属玩家(王室)家族; 番号/名字/绿圈/指挥均不受影响。
        internal static Clan EnsureClan()
        {
            try { return Clan.PlayerClan; } catch { return null; }
        }

        // ==================== v4.276: 海军舰队用的 internal 转发 ====================
        //   舰队实体化(Navy\NavalTaskForce.cs)要复用国防军已经踩过坑的两条路径, 但它们在别的
        //   系统里是 private。这里只做**转发**, 不改任何既有逻辑(改动量最小、回归风险最低):
        //   ① 司令生成 -> MakeGeneral(家族/模板/技能/命名那条路, 与军团将军完全同源);
        //   ② 从港口守备营抽兵 -> MoveDefTroops(只抽 mod 自己募来的三种基础兵, 不从天上掉水手)。
        internal static Hero MakeCaptainFor(Settlement home, string title)
        {
            try
            {
                var h = MakeGeneral(home, 0);
                if (h == null) return null;
                if (!string.IsNullOrEmpty(title))
                {
                    try { h.SetName(new TextObject(title), new TextObject(title)); } catch { }
                }
                return h;
            }
            catch (Exception ex) { DLog.Force("国防军: 生成舰队司令失败 " + ex.Message); return null; }
        }

        internal static int MoveGarrisonTroopsTo(Settlement home, TroopRoster dst, int n)
        {
            try
            {
                if (home == null || home.Culture == null || dst == null || n <= 0) return 0;
                var garrison = GarrisonPartyOf(home);
                if (garrison == null) return 0;
                int moved = MoveDefTroops(garrison.MemberRoster, dst, home.Culture, n);
                if (moved > 0)
                {
                    var rec = GetGarrisonRec(home.StringId, false);
                    if (rec != null) rec.Placed = Math.Max(0, rec.Placed - moved);
                }
                return moved;
            }
            catch (Exception ex) { DLog.Force("国防军: 向舰队调兵失败 " + ex.Message); return 0; }
        }

        internal static int GarrisonMen(Settlement home)
        {
            try { return home != null ? GarrisonMenOf(home.StringId) : 0; }
            catch { return 0; }
        }

        // v4.277: 军饷口径的**只读转发**(第 1 项: 海军舰队水手军饷)。
        //   舰队(Navy\NavalTaskForce)不在 DefArmy.Legions 登记表里, 所以 DailyWageCost() 永远不含它;
        //   但舰队水手的军饷口径**必须与陆军一字不差** —— 这里把两个权威入口原样暴露出去,
        //   舰队侧照抄同一算式(RegularsOf × WagePerManPerDay × 科研系数), 不另发明一套单价:
        //     · PerManPerDay   = WagePerManPerDay(0.5/兵/日)
        //     · RegularsOf(p)  = MemberRoster.TotalRegulars(不含英雄司令, 与陆军同口径)
        //     · ResearchWageSave() = WageMultOf 里那一段(v4.266 军事统计 -6% 军饷)的**只读**提取,
        //       使"研究军事统计 -> 陆军和海军的军饷一起降"这条因果对两支军队同时成立。
        //   ★ 这三个都是新增入口, 既有方法一个字节都没改(WageMultOf 的实现保持原样)。
        internal static float WagePerManPerDayForFleet() { return WagePerManPerDay; }

        internal static int RegularsOfForFleet(MobileParty p) { return RegularsOf(p); }

        internal static float ResearchWageSaveForFleet()
        {
            try { return Research.WageSave(OurKingdom); } catch { return 0f; }
        }

        // v4.277(第 1 项): 舰队欠饷哗变时的减员 —— 走**与军团哗变完全相同的移除路径**
        //   (v4.255 修过的 RemoveDefTroops: 按兵种逐类移除 + 不碰英雄司令)。
        //   兵种表按**母港文化**取 —— 与建军时"从哪个港口守备营抽的兵"同一来源
        //   (Found() 里 MoveGarrisonTroopsTo(port, ...) 抽的就是 port.Culture 那三种 T2),
        //   所以移除时用母港文化才能命中同一批兵种(用我们的王国文化可能一个都对不上)。
        //   返回实际移除人数(0 = 该舰队名册里没有该文化的国防军兵种, 那就什么都不做并返回 0)。
        internal static int RemoveRegularsForFleet(MobileParty p, int n)
        {
            try
            {
                if (p == null || n <= 0) return 0;
                CultureObject culture = null;
                try
                {
                    var f = NavalTaskForce.ForceOf(p);
                    var home = f != null ? FindSettlement(f.HomePortId) : null;
                    if (home != null) culture = home.Culture;
                }
                catch { }
                if (culture == null) { try { culture = p.CurrentSettlement != null ? p.CurrentSettlement.Culture : null; } catch { } }
                if (culture == null) { try { culture = OurKingdom != null ? OurKingdom.Culture : null; } catch { } }
                if (culture == null) return 0;
                int removed = RemoveDefTroops(p.MemberRoster, culture, n);
                if (removed > 0)
                    DLog.Force("国防军: 舰队水手哗变减员 " + removed + " 人(" + MapSelection.NameOf(p) + ")");
                return removed;
            }
            catch (Exception ex) { DLog.Force("国防军: 舰队减员失败 " + ex.Message); return 0; }
        }

        private static CharacterObject FindTemplate(CultureObject culture)
        {
            try
            {
                var k = OurKingdom;
                if (k != null)
                {
                    foreach (var c in k.Clans)
                    {
                        var h = c != null ? c.Leader : null;
                        if (h == null || h.IsDead || ReferenceEquals(h, Hero.MainHero)) continue;
                        var co = h.CharacterObject;
                        if (co == null || !co.IsHero) continue;
                        if (culture != null && co.Culture != null && co.Culture.StringId != culture.StringId) continue;
                        return co;
                    }
                    foreach (var c in k.Clans)
                    {
                        var h = c != null ? c.Leader : null;
                        if (h == null || h.IsDead || ReferenceEquals(h, Hero.MainHero)) continue;
                        var co = h.CharacterObject;
                        if (co != null && co.IsHero) return co;
                    }
                }
            }
            catch { }
            try { if (Hero.MainHero != null) return Hero.MainHero.CharacterObject; } catch { }
            return null;
        }

        private static void SetSkill(Hero h, SkillObject skill)
        {
            try { h.SetSkillValue(skill, MBRandom.RandomInt(5, 41)); } catch { }
        }

        private static Hero MakeGeneral(Settlement home, int number)
        {
            try
            {
                var clan = EnsureClan();
                if (clan == null) return null;
                var culture = home != null && home.Culture != null ? home.Culture : (OurKingdom != null ? OurKingdom.Culture : null);
                var template = FindTemplate(culture);
                if (template == null) { DLog.Force("国防军: 找不到将军模板"); return null; }
                var hero = HeroCreator.CreateSpecialHero(template, home, clan, null, MBRandom.RandomInt(22, 33));
                if (hero == null) return null;
                string nm = (OurKingdom != null && OurKingdom.Name != null ? OurKingdom.Name.ToString() : "王国")
                    + "国防军第" + number + "军团";
                try { hero.SetName(new TextObject(nm), new TextObject(nm)); } catch { }
                try { hero.SetNewOccupation(Occupation.Lord); } catch { }
                SetSkill(hero, DefaultSkills.OneHanded);
                SetSkill(hero, DefaultSkills.TwoHanded);
                SetSkill(hero, DefaultSkills.Polearm);
                SetSkill(hero, DefaultSkills.Bow);
                SetSkill(hero, DefaultSkills.Riding);
                SetSkill(hero, DefaultSkills.Athletics);
                SetSkill(hero, DefaultSkills.Tactics);
                SetSkill(hero, DefaultSkills.Steward);
                SetSkill(hero, DefaultSkills.Leadership);
                try { hero.Gold = 0; } catch { }
                if (clan.Leader == null) { try { clan.SetLeader(hero); } catch { } }
                DLog.Force("国防军: 生成将军 " + nm + " (" + hero.StringId + ")");
                return hero;
            }
            catch (Exception ex) { DLog.Force("国防军: 生成将军失败 " + ex); return null; }
        }

        private static int NextNumber()
        {
            for (int n = 1; n < 100000; n++)
            {
                bool used = false;
                for (int i = 0; i < Legions.Count; i++) if (Legions[i].Number == n) { used = true; break; }
                if (!used) return n;
            }
            return Legions.Count + 1;
        }

        private static void RetireGeneral(string heroId)
        {
            try
            {
                var h = Hero.Find(heroId);
                if (h == null || h.IsDead) return;
                h.ChangeState(Hero.CharacterStates.Disabled);   // 卸任(不再上阵)
            }
            catch { }
        }

        // ==================== 军团(野战) ====================
        private static string LegionName(int number)
        {
            return (OurKingdom != null && OurKingdom.Name != null ? OurKingdom.Name.ToString() : "王国")
                + "国防军第" + number + "军团";
        }

        // v4.254: 出城去抖(部队 id -> 上次搬出的游戏日)
        private static readonly Dictionary<string, int> _pullOutDay = new Dictionary<string, int>();

        // v4.91: 补 native 陆地导航(海军 DLC 下新建军团若缺此能力, native 侧不会移动)
        internal static void EnsureNavigation(MobileParty p)
        {
            try
            {
                if (p == null) return;
                // v4.95: 非活跃部队 native 完全不驱动 -> 强制激活
                try { if (!p.IsActive) { p.IsActive = true; DLog.Force("国防军: 部队非活跃, 已激活 " + MapSelection.NameOf(p)); } } catch { }
                if (!p.IsActive) return;
                // v4.272(审计 A2): **海运中的军团不许走下面的"陆地导航修补/拖回陆地"** ——
                //   原实现紧跟着就是 `if (p.IsCurrentyAtSea) { IsCurrentlyAtSea = false; MovePartyToTheClosestLand(); }`,
                //   而 EnsureNavigation 的调用点里有 NationalWillOrders.TakeOver(玩家任何一条命令都会走)
                //   -> 海运途中玩家点一下这支军团, 军团会被瞬间拖回陆地, 海运立刻中断(船还挂在它名下)。
                //   海运有自己的目标港与推进链路(见 NavalTransport.Depart 的 SetMoveGoToSettlement),
                //   到港/超时/召回时由 Arrive/Recall 负责下船, 所以这里直接放行, 一个字都不改。
                if (NavalTransport.InConvoy(p.StringId)) return;
                bool land = true;
                try { land = p.HasLandNavigationCapability; } catch { }
                if (!land)
                {
                    try { p.SetLandNavigationAccess(true); DLog.Force("国防军: 已补陆地导航 " + MapSelection.NameOf(p)); }
                    catch (Exception ex) { DLog.Force("国防军: 补陆地导航失败 " + ex.Message); }
                }
                try
                {
                    if (p.IsCurrentlyAtSea)
                    {
                        p.IsCurrentlyAtSea = false;
                        p.MovePartyToTheClosestLand();
                        DLog.Force("国防军: 部队在海上, 已拖回最近陆地 " + MapSelection.NameOf(p));
                    }
                }
                catch { }
                // v4.95: 部队若在定居点内(卡在城里), 移到城门外空地
                // v4.254: 加去抖 —— 每次玩家命令都会走到这里(TakeOver -> EnsureNavigation), 原来连点 6 次
                //   "前往"就把同一支部队瞬移出城 6 次(日志实证: 5 秒内两支军团 12 次), 部队在城门/城内
                //   之间来回跳, 玩家看到"点了没反应/停在半路"。现在同一支部队每天最多搬一次。
                try
                {
                    if (p.CurrentSettlement != null)
                    {
                        int today = Politics.Today();
                        int pulled;
                        if (_pullOutDay.TryGetValue(p.StringId, out pulled) && pulled == today) { }
                        else
                        {
                            _pullOutDay[p.StringId] = today;
                            var st = p.CurrentSettlement;
                            var gate = st.GatePosition;
                            if (gate.ToVec2().Distance(st.Position.ToVec2()) > 0.1f)
                            {
                                p.Position = gate;
                                try { p.MovePartyToTheClosestLand(); } catch { }
                                DLog.Force("国防军: 部队位于城(定居点)内, 已移出到城门外 " + MapSelection.NameOf(p));
                            }
                        }
                    }
                }
                catch { }
            }
            catch { }
        }

        internal static MobileParty LegionParty(DefLegion lg)
        {
            if (lg == null) return null;
            var p = FindParty(lg.PartyId);
            if (p == null && !string.IsNullOrEmpty(lg.GeneralId))
            {
                // v4.90: 读档后按将军找回部队(修"读档丢军团")
                p = FindLegionByGeneral(lg.GeneralId);
                if (p != null) lg.PartyId = p.StringId;
            }
            return p;
        }

        private static MobileParty FindLegionByGeneral(string generalId)
        {
            try
            {
                var c = Campaign.Current;
                if (c == null || c.MobileParties == null || string.IsNullOrEmpty(generalId)) return null;
                for (int i = 0; i < c.MobileParties.Count; i++)
                {
                    var p = c.MobileParties[i];
                    if (p == null || !p.IsActive) continue;
                    if (p.StringId == null || !p.StringId.StartsWith("fia_def_", StringComparison.Ordinal)) continue;
                    var ld = p.LeaderHero;
                    if (ld != null && ld.StringId == generalId) return p;
                }
            }
            catch { }
            return null;
        }

        internal static int LegionMen(DefLegion lg)
        {
            try
            {
                var p = LegionParty(lg);
                // v4.123: 显示"士兵数"(不含将军), 与守备营抽调数一致(100 抽 100 显示 100)
                if (p != null && p.IsActive) return RegularsOf(p);
            }
            catch { }
            return lg != null ? lg.Expected : 0;
        }

        // v4.87: 部队正兵数(不含英雄将军), 修"成立 200 显示 201"
        internal static int RegularsOf(MobileParty p)
        {
            try
            {
                if (p == null) return 0;
                int n;
                try { n = p.MemberRoster.TotalRegulars; }
                catch { n = p.MemberRoster.TotalManCount - p.MemberRoster.TotalHeroes; }
                return n > 0 ? n : 0;
            }
            catch { return 0; }
        }

        // v27.18: 兼容入口(抽屉/守备营页仍调用) —— 按默认编成(60/35/5)走 CreateLegionEx
        internal static string CreateLegion(Settlement home, int n)
        {
            try
            {
                var t = TroopsOf(home != null && home.Culture != null ? home.Culture : null);
                var want = Compose(n > 0 ? n : 0, t != null ? t[2] : null);
                var comp = new int[4];
                comp[0] = want[0];
                comp[1] = want[1];
                comp[2] = want[2];
                comp[3] = 0;
                string err = CreateLegionEx(home, n, comp, HasFirearmTech() ? 3 : 1, new bool[3]);
                if (string.IsNullOrEmpty(err))
                    return "成立军团: " + n + " 兵 · 花费 " + (LegionFormCost + LegionTrainCostPerMan * n).ToString("N0") + " 第纳尔";
                return err;
            }
            catch (Exception ex) { DLog.Force("成立军团失败: " + ex); return "成立军团失败, 见日志"; }
        }

        // v27.x: 装备驱动建军(设计 27.18 建军设计器) —— comp=步兵/弓手/骑兵/骑射手兵力, equipTier=装备档 1~6, supports=工具/野战医院/电报机
        // 返回错误串或 ""(成功)
        internal static string CreateLegionEx(Settlement home, int n, int[] comp, int equipTier, bool[] supports)
        {
            return CreateLegionEx(home, n, comp, equipTier, supports, null);
        }

        // perKind = 14 兵种逐项人数(建军设计器的原始选择); 非空时按它写逐人表,
        //   且花名册按"设计分支配比"抽兵(v4.236: 修"选了 100 线列步兵, 花名册却是 65 近战 + 35 远程")
        internal static string CreateLegionEx(Settlement home, int n, int[] comp, int equipTier, bool[] supports, int[] perKind)
        {
            try
            {
                if (OurKingdom == null) return Fail("尚未建立国家意志");
                if (home == null) return Fail("请先选择驻地");
                if (n < LegionMinMen) return Fail("成立军团至少需要 " + LegionMinMen + " 兵");
                if (n > MaxLegionMen) return Fail("成立军团人数超出范围");
                var c4 = NormalizeComp(comp, n);
                if (c4 == null) return Fail("编制必须为 4 项(步/弓/骑/骑射)");
                if (equipTier < 1) equipTier = 1;
                if (equipTier > 6) equipTier = 6;
                int cost = LegionFormCost + LegionTrainCostPerMan * n;
                if (EconomyWorld.Treasury.Gold < cost) return Fail("国库不足: 建军需要 " + cost.ToString("N0") + " 第纳尔(现有 " + EconomyWorld.Treasury.Gold.ToString("N0") + ")");
                var garrison = GarrisonPartyOf(home);
                if (garrison == null || home.Culture == null) return Fail("该地没有驻军");
                int actual = GarrisonMenOf(home.StringId);
                if (actual < n) return Fail("守备营兵力不足: 只有 " + actual + " 兵(计划 " + n + ")");

                int number = NextNumber();
                var general = MakeGeneral(home, number);
                if (general == null) return Fail("将军生成失败(见日志)");

                string pid = "fia_def_" + Guid.NewGuid().ToString("N");
                MobileParty party = null;
                try { party = LordPartyComponent.CreateLordParty(pid, general, home.Position, 1f, home, general); }
                catch (Exception ex) { DLog.Force("国防军: 创建部队失败 " + ex.Message); }
                if (party == null) { RetireGeneral(general.StringId); return Fail("部队创建失败"); }
                try { party.ActualClan = Clan.PlayerClan; } catch { }

                var mr = new TroopRoster(party.Party);
                // 花名册按设计的分支配比抽兵(骑射手并入骑兵档), 不再固定 60/35/5
                int[] rw = (c4 != null && c4.Length >= 4)
                    ? new[] { Math.Max(0, c4[0]), Math.Max(0, c4[1]), Math.Max(0, c4[2]) + Math.Max(0, c4[3]) }
                    : null;
                int moved = MoveDefTroops(garrison.MemberRoster, mr, home.Culture, n, rw);
                if (moved <= 0) { RetireGeneral(general.StringId); return Fail("守备营兵力不足"); }
                if (moved != n) c4 = NormalizeComp(c4, moved);
                CampaignVec2 spawn = home.Position;
                try { spawn = home.GatePosition; } catch { }
                party.InitializeMobilePartyAtPosition(mr, new TroopRoster(party.Party), spawn, false);
                try { party.Party.SetCustomName(new TextObject(LegionName(number))); } catch { }
                try { party.SetCustomHomeSettlement(home); } catch { }
                try { party.SetMoveModeHold(); } catch { }
                EnsureNavigation(party);
                // v4.236: 逐人表按设计编成写(不按花名册折算, 免得"线列步兵 100"被折成"民兵 65 + 弓兵 35")
                if (perKind != null)
                {
                    try
                    {
                        int[] pk = (moved == n) ? perKind : ScaleKinds(perKind, moved);
                        Soldiers.SetComposition(party, pk);
                    }
                    catch (Exception ex) { DLog.Force("逐人表写入失败: " + ex.Message); }
                }

                var lg = new DefLegion
                {
                    PartyId = pid,
                    GeneralId = general.StringId,
                    HomeId = home.StringId,
                    Number = number,
                    Expected = moved,
                    Task = "驻守",
                    Comp = c4,
                    EquipTier = equipTier,
                    Supports = CopySupports(supports),
                    EquipFill = 1f,
                    Training = -1f,           // 按兵种默认(27.2)
                    Stance = StanceStandby,   // v4.243: 默认戒备
                    Train = -1f,              // v4.243: 首次日结按兵种构成补齐
                    GeneralTraits = RollGeneralTraits(general, c4)   // v4.243: 将军特质(1~3 个)
                };
                Legions.Add(lg);
                // v4.243: 特质带来的初始士气/组织度修正(严酷军纪/爱兵如子)
                try
                {
                    if (HasTrait(lg, TraitPaternal)) party.RecentEventsMorale += 10f;
                    if (HasTrait(lg, TraitDiscipline))
                    {
                        party.RecentEventsMorale -= 5f;
                        ArmyDoctrine.OrgGain(ArmyDoctrine.LegionKey(lg), 10f);
                    }
                }
                catch { }
                var rec = GetGarrisonRec(home.StringId, true);
                if (rec != null) rec.Placed = Math.Max(0, rec.Placed - moved);
                // v4.255: 先把这批兵在守备营账上已有的装备转进军团快照, 再补装 ——
                //   否则同一批兵募兵时领一次、建军时又按 NeedOf 领一次
                TransferGarrisonEquip(lg, rec, moved, rec != null ? rec.Placed : 0);
                EquipLegion(lg, 1f);          // 从国家军械库领装(100%), 满足率据实更新

                Spend(cost);
                Fiscal.AddMilitary(cost);
                MapSelection.Select(party);
                DLog.Force("国防军: 成立 " + LegionName(number) + " " + moved + " 兵 编制["
                    + c4[0] + "/" + c4[1] + "/" + c4[2] + "/" + c4[3] + "] 装备档 T" + equipTier
                    + " 满足率 " + ((int)Math.Round(EquipFillOf(lg) * 100f)) + "%");
                return "";
            }
            catch (Exception ex) { DLog.Force("成立军团(设计器)失败: " + ex); return "成立军团失败, 见日志"; }
        }

        // 逐人表写入失败时的保护: 名册只抽到 target 人时, 把各兵种按比例缩到 target(余数给最大项)
        private static int[] ScaleKinds(int[] kinds, int target)
        {
            try
            {
                if (kinds == null || target <= 0) return kinds;
                int sum = 0;
                for (int i = 0; i < kinds.Length; i++) if (kinds[i] > 0) sum += kinds[i];
                if (sum <= 0) return kinds;
                var r = new int[kinds.Length];
                int given = 0, big = 0, bigV = -1;
                for (int i = 0; i < kinds.Length; i++)
                {
                    if (kinds[i] <= 0) continue;
                    if (kinds[i] > bigV) { bigV = kinds[i]; big = i; }
                    r[i] = (int)Math.Floor(kinds[i] * (double)target / sum);
                    given += r[i];
                }
                r[big] += Math.Max(0, target - given);
                return r;
            }
            catch { return kinds; }
        }

        // 编制归一化(27.18: 四项兵力; 合计不符时按比例折算到 n, 余数归步兵)
        private static int[] NormalizeComp(int[] comp, int n)
        {
            try
            {
                if (comp == null || comp.Length < 4 || n <= 0) return null;
                var r = new int[4];
                int sum = 0;
                for (int i = 0; i < 4; i++)
                {
                    if (comp[i] < 0) return null;
                    r[i] = comp[i];
                    sum += comp[i];
                }
                if (sum == n) return r;
                if (sum <= 0) { r[0] = n; return r; }
                int left = n;
                for (int i = 0; i < 4; i++)
                {
                    int v;
                    if (i == 3) v = left;
                    else v = (int)Math.Floor(r[i] * (double)n / sum);
                    if (v < 0) v = 0;
                    if (v > left) v = left;
                    r[i] = v;
                    left -= v;
                }
                if (left > 0) r[0] += left;
                return r;
            }
            catch { return null; }
        }

        private static bool[] CopySupports(bool[] supports)
        {
            var r = new bool[3];
            try
            {
                if (supports != null)
                    for (int i = 0; i < 3 && i < supports.Length; i++) r[i] = supports[i];
            }
            catch { }
            return r;
        }

        // 合并选中的国防军(相邻): 多余将军卸任转 1 名小兵, 免费
        internal static string MergeLegions(List<MobileParty> parties)
        {
            try
            {
                var list = new List<MobileParty>();
                for (int i = 0; i < parties.Count; i++)
                {
                    var p = parties[i];
                    if (p != null && p.IsActive && IsDefArmyParty(p) && LegionOf(p) != null && !list.Contains(p)) list.Add(p);
                }
                if (list.Count < 2) return "至少选中 2 支国防军部队";
                if (list.Count > 16) return "一次最多合并 16 支";
                for (int i = 0; i < list.Count; i++)
                {
                    for (int j = i + 1; j < list.Count; j++)
                    {
                        float d2 = list[i].Position.DistanceSquared(list[j].Position);
                        if (d2 > 60f * 60f) return "必须相邻(60 距离内)才能合并";
                    }
                }
                MobileParty main = list[0];
                for (int i = 1; i < list.Count; i++)
                    if (list[i].MemberRoster.TotalManCount > main.MemberRoster.TotalManCount) main = list[i];
                var mainLg = LegionOf(main);
                var culture = mainLg != null ? CultureOfLegion(mainLg) : null;
                if (culture == null) return "无法确定兵种文化";
                int total = main.MemberRoster.TotalManCount;
                int retired = 0;
                for (int i = 0; i < list.Count; i++)
                {
                    var p = list[i];
                    if (ReferenceEquals(p, main)) continue;
                    int men = RegularsOf(p);   // v4.255: 正兵口径
                    int moved = MoveDefTroops(p.MemberRoster, main.MemberRoster, culture, men);
                    total += moved;
                    // v4.255: 逐人熟练度/个人击杀随兵并入主军团(原来只搬原版兵, 逐人表整段丢)
                    try { Soldiers.Merge(main, p); } catch { }
                    var lg = LegionOf(p);
                    if (lg != null)
                    {
                        MergeEquip(mainLg, lg);   // 装备快照并入主军团
                        RetireGeneral(lg.GeneralId);
                        Legions.Remove(lg);
                    }
                    try { DestroyPartyAction.ApplyForDisbanding(p, FindSettlement(mainLg != null ? mainLg.HomeId : "")); } catch { }
                    AddSoldiersPop(HomeOf(mainLg), 1);   // 将军卸任转 1 名小兵
                    retired++;
                }
                if (mainLg != null) mainLg.Expected = RegularsOf(main);
                string msg = "合并完成: " + (list.Count - 1) + " 支并入 " + MapSelection.NameOf(main)
                    + ", 卸任将军 " + retired + " 名 → 兵员 +" + retired + " · 编制 " + total;
                DLog.Force("国防军: " + msg);
                MapSelection.Select(main);
                return msg;
            }
            catch (Exception ex) { DLog.Force("合并失败: " + ex); return "合并失败, 见日志"; }
        }

        // 拆分: 1 名小兵升任将军(编制 -1), 花 500; 单参入口 = 对半(右键菜单/地图菜单)
        internal static string SplitLegion(MobileParty p)
        {
            int men = 0;
            try { men = p != null && p.MemberRoster != null ? p.MemberRoster.TotalManCount : 0; } catch { }
            return SplitLegion(p, men / 2);
        }

        // want = 新军团带走人数; v4.235: 只要求两边各至少留 LegionKeepMin 名正兵
        //   (原"两支各 ≥50"取消: 用户实测 100 兵最多只能分出 51 人)
        internal static string SplitLegion(MobileParty p, int want)
        {
            try
            {
                var lg = LegionOf(p);
                if (lg == null) return "只能拆分国防军野战军团";
                int men = p.MemberRoster.TotalManCount;      // 名册(含将军)
                int regs = RegularsOf(p);                    // 正兵(名册 − 将军)
                if (men < LegionMinMen) return "军团兵力不足 " + LegionMinMen + ", 无法拆分";
                if (regs < LegionKeepMin + 1) return "军团正兵不足 " + (LegionKeepMin + 1) + " 人, 无法拆分";
                if (want < LegionKeepMin) want = LegionKeepMin;
                if (want > regs - LegionKeepMin) want = regs - LegionKeepMin;   // 原队至少留 LegionKeepMin 名正兵
                if (want <= 0) return "没有可分出的正兵";
                int remain = men - want;
                if (want < LegionKeepMin || remain < LegionKeepMin) return "拆分后两边各至少留 " + LegionKeepMin + " 名正兵";
                if (EconomyWorld.Treasury.Gold < SplitCost) return "国库不足: 拆分需要 " + SplitCost + " 第纳尔";
                var home = HomeOf(lg);
                if (home == null || home.Culture == null) return "找不到驻地";
                int number = NextNumber();
                var general = MakeGeneral(home, number);
                if (general == null) return "将军生成失败(见日志)";
                string pid = "fia_def_" + Guid.NewGuid().ToString("N");
                MobileParty party = null;
                try { party = LordPartyComponent.CreateLordParty(pid, general, p.Position, 1f, home, general); }
                catch (Exception ex) { DLog.Force("国防军: 创建部队失败 " + ex.Message); }
                if (party == null) { RetireGeneral(general.StringId); return Fail("部队创建失败"); }
                try { party.ActualClan = Clan.PlayerClan; } catch { }
                var mr = new TroopRoster(party.Party);
                int moved = MoveDefTroops(p.MemberRoster, mr, home.Culture, want);
                party.InitializeMobilePartyAtPosition(mr, new TroopRoster(party.Party), p.Position, false);
                // v4.255: 逐人表按比例分给新军团(原来 Split 零调用 -> 新军团熟练度回初值、
                //   掷弹兵/胸甲骑兵的 80 熟练与累计击杀全丢, 原队只能由 Reconcile 从表尾截断)
                try { Soldiers.Split(p, party, regs > 0 ? moved / (float)regs : 0f); } catch { }
                try { party.Party.SetCustomName(new TextObject(LegionName(number))); } catch { }
                try { party.SetCustomHomeSettlement(home); } catch { }
                // v4.254: 不再对新拆出的军团设 SetDoNotMakeNewDecisions(true)+SetMoveModeHold()。
                //   原版 AI 决策已由 DefArmyAi.AiGuard(屏蔽 MobilePartyAi.TickInternal) 挡掉, 再设这两个标志
                //   会让 native 侧疑似冻结移动 —— v4.90 已确认它是"指挥不动"的根因并在 TakeOver 里删掉,
                //   但**拆分这条路径漏改了**, 于是拆出来的新军团点[驻防]/点地图都不动。
                try { party.Ai.SetDoNotMakeNewDecisions(false); } catch { }
                var nl = new DefLegion
                {
                    PartyId = pid,
                    GeneralId = general.StringId,
                    HomeId = home.StringId,
                    Number = number,
                    Expected = moved,
                    Task = "驻守",
                    Comp = null,                 // 由实际名单折算(CompOf)
                    EquipTier = lg.EquipTier,
                    PlayerHold = lg.PlayerHold,   // v4.254: 拆分出的新军团继承"玩家接管"(否则刚拆出来就被 AI 调走)
                    Supports = CopySupports(lg.Supports),
                    EquipFill = 1f,
                    Training = lg.Training,
                    // v4.243: 新军团继承状态与训练度, 将军特质重新生成(换了将军)
                    Stance = StanceOf(lg),
                    Train = TrainLevelOf(lg),
                    GeneralTraits = RollGeneralTraits(general, CompOf(lg))
                };
                Legions.Add(nl);
                SplitEquip(lg, nl);              // 装备快照对半拆分
                lg.Expected = RegularsOf(p);      // v4.235: 口径 = 名册 − 将军
                Spend(SplitCost);
                Fiscal.AddMilitary(SplitCost);
                RemoveSoldiersPop(home, 1);   // 1 名小兵升任将军
                string msg = "拆分完成: " + LegionName(number) + " 得 " + moved + " 兵 · 花费 " + SplitCost + " 第纳尔";
                DLog.Force("国防军: " + msg);
                return msg;
            }
            catch (Exception ex) { DLog.Force("拆分失败: " + ex); return "拆分失败, 见日志"; }
        }

        private static CultureObject CultureOfLegion(DefLegion lg)
        {
            try
            {
                var s = FindSettlement(lg.HomeId);
                return s != null ? s.Culture : null;
            }
            catch { return null; }
        }

        private static Settlement HomeOf(DefLegion lg)
        {
            return lg != null ? FindSettlement(lg.HomeId) : null;
        }

        internal static string DisbandLegion(MobileParty p)
        {
            try
            {
                var lg = LegionOf(p);
                if (lg == null) return "只能解散国防军野战军团";
                int men = RegularsOf(p);   // v4.255: 正兵口径(不含将军) —— 原来按 TotalManCount 算,
                                           //   100 人军团按 101 收遣散费/守备营多记 1 人/强制遣散多还 1 人口
                var home = HomeOf(lg);
                var target = GarrisonTarget(home);
                int cost = men * DisbandCost;
                bool paid = EconomyWorld.Treasury.Gold >= cost;
                if (paid)
                {
                    Spend(cost);
                    Fiscal.AddMilitary(cost);
                    var garrison = GarrisonPartyOf(target);
                    if (garrison != null && home != null && home.Culture != null)
                    {
                        MoveDefTroops(p.MemberRoster, garrison.MemberRoster, home.Culture, men);
                        var rec = GetGarrisonRec(target.StringId, true);
                        if (rec != null) rec.Placed += men;
                    }
                }
                else
                {
                    // 付不起遣散费 = 强制遣散: 1:1 回乡 + 政治代价
                    if (home != null) ReturnSoldiersToCommoners(home, men);
                    RemoveDefTroops(p.MemberRoster, home != null ? home.Culture : null, men);
                    ApplyForceDisbandPenalty();
                }
                ReturnEquip(lg);   // 27.4: 解散返还 100% 入国家军械库
                RetireGeneral(lg.GeneralId);
                Legions.Remove(lg);
                try { DestroyPartyAction.ApplyForDisbanding(p, target); } catch { }
                string msg = paid
                    ? ("已解散 " + MapSelection.NameOf(p) + " (" + men + " 兵 → " + (target != null ? target.Name.ToString() : "?") + "守备营, 遣散费 " + cost.ToString("N0") + ")")
                    : ("国库付不起遣散费: 强制遣散 " + men + " 兵(回乡), 激进+3% 贵族怒+5 权威-10");
                DLog.Force("国防军: " + msg);
                return msg;
            }
            catch (Exception ex) { DLog.Force("解散失败: " + ex); return "解散失败, 见日志"; }
        }

        private static void ApplyForceDisbandPenalty()
        {
            try
            {
                Pops.ShiftRadicals(0.03f);
                foreach (var kv in Politics.Lords)
                {
                    var lp = kv.Value;
                    if (lp == null) continue;
                    lp.Anger += 5;
                    if (lp.Anger > 100) lp.Anger = 100;
                }
                Politics.Authority = Math.Max(0f, Politics.Authority - 10f);
                Politics.Aggregate();
            }
            catch { }
        }

        internal static string SetTask(MobileParty p, int kind)
        {
            try
            {
                var lg = LegionOf(p);
                if (lg == null) return "只能指挥国防军野战军团";
                MarkPlayerOrder(p);   // v4.245: 玩家点了驻防/巡逻 -> 该军团归玩家指挥
                if (kind == 0)
                {
                    var home = HomeOf(lg);
                    if (home == null) return "找不到驻地";
                    // v4.100: 恢复允许进城(用户撤销"不进城")
                    p.SetMoveGoToSettlement(home, MobileParty.NavigationType.Default, false);
                    try { CommandTimeout.Touch(p, home.GatePosition); } catch { }
                    lg.Task = "驻防";
                    return MapSelection.NameOf(p) + " 前往 " + home.Name + " 驻防";
                }
                var h = HomeOf(lg);
                if (h == null) return "找不到驻地";
                p.SetMovePatrolAroundSettlement(h, MobileParty.NavigationType.Default, false);
                CampaignVec2 pp;
                if (TryPatrolPoint(lg, out pp)) CommandTimeout.Touch(p, pp);   // v4.96: 手动驾驶绕圈
                lg.Task = "巡逻";
                return MapSelection.NameOf(p) + " 在 " + h.Name + " 一带巡逻";
            }
            catch (Exception ex) { DLog.Force("军团任务失败: " + ex.Message); return "任务下达失败"; }
        }

        // ==================== 征兵 ====================
        internal static bool ConscriptUnlocked { get { return Politics.LawLevel(2) >= 1; } }

        internal static int ConscriptPool(Settlement s)
        {
            try
            {
                int lv = Politics.LawLevel(2);
                if (s == null || lv <= 0) return 0;
                float mult = 0.10f * (1f + lv * 0.05f);
                // v4.266: 征兵类科技接进"可征人口"(征兵办公室 +15% / 义务兵役 +12% / 预备役 +9% / 战争宣传 +6%)
                //   原来这四项只有"征召上限 +N%"一条落点, 聚落可征人口不吃科技
                try { mult *= Research.ConscriptPoolMult(OurKingdom); } catch { }
                // v4.266: 军事建筑"军营"(@recruit 5/8/12 每级) 接进可征人口 —— 征兵面板上的数字直接变大
                try { mult *= 1f + MilBuildings.RecruitPoolBonusOf(s); } catch { }
                // v4.122: 与"实际可征人口"同口径(原来只算单一阶层, 会与实际抽取对不上)
                return (int)(RealCommonersOf(s) * mult);
            }
            catch { return 0; }
        }

        internal static int ConscriptCap()
        {
            try
            {
                int lv = Politics.LawLevel(2);
                if (lv <= 0) return 0;
                // v4.241: 科技("征召 +N%": 义务兵役/预备役/后勤学/征兵办公室/战争宣传) +
                //   民族精神(尚武传统/全民皆兵) 此前两处都没接线, 现在都吃进去
                float tech = 1f, spirit = 1f;
                try { tech = Research.ConscriptMult(); } catch { }
                try
                {
                    var pk = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                    if (pk != null) spirit = NationalSpirits.ConscriptMult(pk);
                }
                catch { }
                return (int)(Pops.TotalPopulation() * (0.02f * lv) * FeudalContracts.AvgLevyMult() * LawSystem.ConscriptMult() * WarMobilization.ConscriptMult() * tech * spirit);   // v4.126: 兵役契约; v5.0-P22: 兵役制度; v5.0-P25: 动员 ×1.5
            }
            catch { return 0; }
        }

        internal static int ConscriptedMen()
        {
            int n = 0;
            for (int i = 0; i < Conscripts.Count; i++) if (Conscripts[i] != null && !Conscripts[i].Standing) n += Conscripts[i].Count;
            return n;
        }

        internal static string Conscript(Settlement s, int n)
        {
            try
            {
                if (!ConscriptUnlocked) return "需「军务征召」法令 ≥ 1 级(未立法无征召权)";
                if (OurKingdom == null) return "尚未建立国家意志";
                if (s == null || n <= 0) return "请先选择征兵地";
                var target = GarrisonTarget(s);
                if (target == null) return "该聚落没有可驻防的城镇/城堡";
                // v4.266: 军事建筑"军营"(@garrison) 守备营上限(与募兵同口径)
                int gcap = MilBuildings.GarrisonCapOf(target);
                int room = gcap - GarrisonMenOf(target.StringId);
                if (room <= 0) return Fail("守备营已满(" + GarrisonMenOf(target.StringId) + "/" + gcap + "): 建「军营」提高守备营上限");
                if (n > room) n = room;
                var garrison = GarrisonPartyOf(target);
                if (garrison == null) return "驻军不存在: " + target.Name;

                // v4.122: 用"实际可征人口"校验(与抽取口径一致), 不足则拒绝(不做部分征召)
                int avail = RealCommonersOf(s);
                if (avail < n) return Fail("可征人口不足: " + s.Name + " 只有 " + avail + " 人可征(计划 " + n + ")");
                int cost = n * ConscriptCost;
                if (EconomyWorld.Treasury.Gold < cost) return "国库不足: 征召需要 " + cost.ToString("N0") + " 第纳尔";

                int moved = TakeCommoners(s, n);
                if (moved <= 0) return Fail("可征人口不足");
                if (moved < n) { DLog.Force("征兵: 实际只征到 " + moved + "/" + n + " 人(人口池截断)"); n = moved; }

                Dictionary<string, int> taken;   // v4.255: 本次实际领到的装备
                string equipMsg = DrawRecruitEquip(n, s.Culture, out taken);

                AddComposition(garrison.MemberRoster, s.Culture, n);
                var rec = GetGarrisonRec(target.StringId, true);
                if (rec != null) rec.Placed += n;
                AddGarrisonEquip(rec, taken);   // v4.255: 已领装备记进守备营账(建军/补员时转交, 不再重复领)

                Conscripts.Add(new DefConscript { SettlementId = s.StringId, Count = n, ExpireDay = -1 });   // v4.239: 强制征兵无时效

                Spend(cost);
                Fiscal.AddMilitary(cost);

                // 人心代价(v4.239 强化: 强制征兵无期限, 代价更重)
                //   每征召 1% 总人口 -> 激进 +0.5%(秋收月 ×2, 军令 3 级免) · 合法性 -1 · 民怨 +40
                //   v4.240: 民怨系数 1000 -> 4000(原来占人口 0.1% 的征召只涨 0.1, 界面永远显示 +0)
                //     口径: 1% 人口 = 40 民怨 -> 民怨 50(请愿) ≈ 动员 1.25% 人口, 民怨 80 ≈ 2%(法令 1 级征召上限)
                float total = Math.Max(1f, Pops.TotalPopulation());
                float pct = n / total;                      // 占总人口比例
                float perPct = 0.5f * n / total;
                int month = (int)(CampaignTime.Now.ToDays % 84) / 7;
                bool harvest = month >= 6 && month <= 8;
                if (harvest && Politics.LawLevel(2) < 3) perPct *= 2f;
                Pops.ShiftRadicals(perPct);
                int legHit = (int)Math.Max(1f, Math.Round(pct * 100f));
                Politics.Legitimacy = Math.Max(0f, Politics.Legitimacy - legHit);
                Politics.ConscriptGrievance = Math.Min(100f, Politics.ConscriptGrievance + pct * 4000f);
                try { if (target.Town != null) target.Town.Loyalty = Math.Max(0f, target.Town.Loyalty - 5f); } catch { }
                var owner = s.OwnerClan;
                bool lordHit = false;
                if (owner != null && owner.StringId != null && Politics.Lords.ContainsKey(owner.StringId))
                {
                    Politics.Lords[owner.StringId].Anger += 2;
                    lordHit = true;
                }
                foreach (var kv in Politics.Lords)
                {
                    var lp = kv.Value;
                    if (lp == null || (lordHit && kv.Key == owner.StringId)) continue;
                    lp.Anger += 1;
                    if (lp.Anger > 100) lp.Anger = 100;
                }
                if (n > total * 0.02f) Politics.ChurchAngerOffset += 5;
                Politics.Aggregate();

                // 超征: 超出 2/4/6% 上限的部分重罚
                int cap = ConscriptCap();
                int over = ConscriptedMen() - cap;
                if (over > 0)
                {
                    Pops.ShiftRadicals(0.015f);
                    Politics.Legitimacy = Math.Max(0f, Politics.Legitimacy - 3f);
                    Politics.Authority = Math.Max(0f, Politics.Authority - 10f);
                    foreach (var kv in Politics.Lords)
                    {
                        var lp = kv.Value;
                        if (lp == null) continue;
                        lp.Anger += 5;
                        if (lp.Anger > 100) lp.Anger = 100;
                    }
                    Politics.Aggregate();
                }

                string msg = "强制征兵 " + n + " 人 → " + target.Name + "守备营(无期限)" + equipMsg
                    + " · 民怨 " + Politics.ConscriptGrievance.ToString("0.#") + "/100 · 合法性 -" + legHit
                    + (harvest ? " · 秋收月(激进×2)" : "") + (over > 0 ? " · 超征!" : "");
                DLog.Force("国防军: " + msg);
                return msg;
            }
            catch (Exception ex) { DLog.Force("征兵失败: " + ex); return "征兵失败, 见日志"; }
        }

        // v4.239: 强制征兵无时效 -> 原[续征 +7 天]作废(保留空入口给旧调用方, 实际不再需要)
        internal static string RenewConscripts()
        {
            return "强制征兵无服役期限, 无需续征(要减员用[解甲归田])";
        }

        // v4.239: 放归一部分征丁(反征兵请愿的"同意"效果; frac = 0.25 表示放归 25%)
        internal static string ReleaseConscriptShare(float frac)
        {
            try
            {
                if (frac <= 0f) return "未放归";
                if (frac > 1f) frac = 1f;
                int total = 0;
                for (int i = Conscripts.Count - 1; i >= 0; i--)
                {
                    var c = Conscripts[i];
                    if (c == null) { Conscripts.RemoveAt(i); continue; }
                    if (c.Standing) continue;
                    int give = Math.Max(1, (int)Math.Round(c.Count * frac));
                    if (give >= c.Count) { ReleaseBatch(c); total += c.Count; Conscripts.RemoveAt(i); continue; }
                    var part = new DefConscript { SettlementId = c.SettlementId, Count = give, ExpireDay = -1 };
                    c.Count -= give;
                    ReleaseBatch(part);
                    total += give;
                }
                // v4.240: 放归征丁 -> 民怨按人数退回(与征召同系数: 1% 人口 = 40 民怨)
                RelieveGrievance(total);
                string msg = "放归征丁 " + total + " 人(1:1 回乡) · 民怨 " + Politics.ConscriptGrievance.ToString("0.#") + "/100";
                DLog.Force("国防军: " + msg);
                return msg;
            }
            catch (Exception ex) { DLog.Force("放归征丁失败: " + ex.Message); return "放归失败"; }
        }

        // 放归/逃亡都按人数退民怨(退回系数取征召的一半: 人心一旦伤了不会完全复原)
        private static void RelieveGrievance(int men)
        {
            try
            {
                if (men <= 0) return;
                float total = Math.Max(1f, Pops.TotalPopulation());
                Politics.ConscriptGrievance = Math.Max(0f, Politics.ConscriptGrievance - men / total * 2000f);
            }
            catch { }
        }

        // v4.239: 民怨沸腾 -> 每月逃亡(强制征兵的长期反噬)
        internal static string ConscriptDesertion()
        {
            try
            {
                float g = Politics.ConscriptGrievance;
                if (g < 70f) return "";
                float rate = g >= 85f ? 0.04f : 0.015f;
                // v4.243: 严酷军纪的将军压得住逃兵(逃亡率 ×0.5)
                try
                {
                    for (int i = 0; i < Legions.Count; i++)
                        if (HasTrait(Legions[i], TraitDiscipline)) { rate *= 0.5f; break; }
                }
                catch { }
                int total = 0;
                for (int i = Conscripts.Count - 1; i >= 0; i--)
                {
                    var c = Conscripts[i];
                    if (c == null) { Conscripts.RemoveAt(i); continue; }
                    if (c.Standing) continue;
                    int run = (int)Math.Round(c.Count * rate);
                    if (run <= 0) continue;
                    if (run >= c.Count) { ReleaseBatch(c); total += c.Count; Conscripts.RemoveAt(i); continue; }
                    var part = new DefConscript { SettlementId = c.SettlementId, Count = run, ExpireDay = -1 };
                    c.Count -= run;
                    ReleaseBatch(part);
                    total += run;
                }
                if (total <= 0) return "";
                string msg = "民怨沸腾, 征丁逃亡 " + total + " 人(民怨 " + g.ToString("0.#") + "/100)";
                DLog.Force("国防军: " + msg);
                return msg;
            }
            catch { return ""; }
        }

        internal static string ReleaseConscripts()
        {
            try
            {
                if (Conscripts.Count == 0) return "当前没有征兵批次";
                int total = 0;
                for (int i = Conscripts.Count - 1; i >= 0; i--)
                {
                    var c = Conscripts[i];
                    if (c == null) { Conscripts.RemoveAt(i); continue; }
                    if (!c.Standing) { ReleaseBatch(c); total += c.Count; Conscripts.RemoveAt(i); }
                }
                RelieveGrievance(total);
                string msg = "放归征丁 " + total + " 人(1:1 回乡) · 民怨 " + Politics.ConscriptGrievance.ToString("0.#") + "/100";
                DLog.Force("国防军: " + msg);
                return msg;
            }
            catch (Exception ex) { DLog.Force("放归失败: " + ex.Message); return "放归失败"; }
        }

        internal static string TransferConscripts()
        {
            try
            {
                int total = 0;
                for (int i = 0; i < Conscripts.Count; i++)
                {
                    var c = Conscripts[i];
                    if (c == null || c.Standing) continue;
                    total += c.Count;
                }
                if (total <= 0) return "没有可转常备的征召兵";
                int cost = total * RecruitCost;
                if (EconomyWorld.Treasury.Gold < cost) return "国库不足: 转常备需要 " + cost.ToString("N0") + " 第纳尔";
                Spend(cost);
                Fiscal.AddMilitary(cost);
                for (int i = 0; i < Conscripts.Count; i++)
                {
                    var c = Conscripts[i];
                    if (c == null || c.Standing) continue;
                    c.Standing = true;
                    c.ExpireDay = -1;
                }
                string msg = "转常备 " + total + " 人 · 花费 " + cost.ToString("N0") + " 第纳尔(原版 T2 直接入列)";
                DLog.Force("国防军: " + msg);
                return msg;
            }
            catch (Exception ex) { DLog.Force("转常备失败: " + ex.Message); return "转常备失败"; }
        }

        private static void ReleaseBatch(DefConscript c)
        {
            try
            {
                var s = FindSettlement(c.SettlementId);
                if (s == null) return;
                var target = GarrisonTarget(s);
                int left = c.Count;
                if (target != null)
                {
                    var garrison = GarrisonPartyOf(target);
                    if (garrison != null && s.Culture != null) left -= RemoveDefTroops(garrison.MemberRoster, s.Culture, left);
                    // 不足部分从该驻地的野战军团里回收
                    for (int i = 0; i < Legions.Count && left > 0; i++)
                    {
                        var lg = Legions[i];
                        if (lg == null || lg.HomeId != target.StringId) continue;
                        var p = LegionParty(lg);
                        if (p == null || !p.IsActive || s.Culture == null) continue;
                        int got = RemoveDefTroops(p.MemberRoster, s.Culture, left);
                        left -= got;
                        lg.Expected = RegularsOf(p);
                    }
                    var rec = GetGarrisonRec(target.StringId, false);
                    if (rec != null) rec.Placed = Math.Max(0, rec.Placed - (c.Count - left));
                }
                // v4.256: 人口必须还给**士兵实际所属的聚落**。征召记录里的 SettlementId 可能是附属村庄,
                //   而兵员在 Soldiers 池里记的是 GarrisonTarget(村庄)=所属城镇 —— 原来传村庄, 池里查不到人
                //   就一个平民都不返还(1:1 铁律被破坏, 平民池单调下降, 募兵/征兵/编制上限越来越低)。
                Settlement back = null;
                try { back = GarrisonTarget(s); } catch { }
                ReturnSoldiersToCommoners(back != null ? back : s, c.Count);
            }
            catch { }
        }

        private static void ConscriptTick(int day)
        {
            try
            {
                // v4.239: 强制征兵无时效 —— 旧档里带到期日的批次一律迁移为无期限(不再自动放归)
                for (int i = Conscripts.Count - 1; i >= 0; i--)
                {
                    var c = Conscripts[i];
                    if (c == null) { Conscripts.RemoveAt(i); continue; }
                    if (c.ExpireDay >= 0) c.ExpireDay = -1;
                }
            }
            catch { }
        }

        private static bool IsAtWar()
        {
            try
            {
                var k = OurKingdom;
                return k != null && k.FactionsAtWarWith != null && k.FactionsAtWarWith.Count > 0;
            }
            catch { return false; }
        }

        // ==================== 每日 ====================
        internal static void Daily(int day)
        {
            try
            {
                if (OurKingdom == null) return;
                // v4.255: 同日防重 —— 读档路径(DefArmyBehavior.OnGameLoaded)无条件补结算一次,
                //   反复读同一存档就能在不推进游戏日的情况下刷军饷/训练度/月结(日志实证同一天两次月结)。
                //   _lastSettledDay 随存档落盘, 所以"读旧档"不会被上一局的高值卡住。
                if (day == _lastSettledDay) return;
                _lastSettledDay = day;
                EnsureClan();
                PayWages();
                FeedLegions();
                try { for (int i = 0; i < Legions.Count; i++) StanceDaily(Legions[i]); } catch { }   // v4.243: 军团状态日结
                try { SpeedAudit(); } catch { }                                                     // v4.248: 速度诊断
                try { SettlementAudit(); } catch { }                                                // v4.249: 进出城诊断
                LogLegionChanges();   // v4.89: 人数变动诊断
                Reconcile();
                ConscriptTick(day);
                if (CampaignTime.Now.GetDayOfWeek == 0) Month(day);
                try { AiWarDirector.Daily(day); } catch { }   // v5.x AI: 军事总监周结(军力评估/轮换/铁路/和谈)
            }
            catch (Exception ex) { DLog.Force("国防军日结异常: " + ex); }
        }

        // ==================== v4.243: 军团状态 / 训练度 / 将军特质 ====================
        internal static int StanceOf(DefLegion lg) { return lg != null ? Math.Max(0, Math.Min(StanceNames.Length - 1, lg.Stance)) : 0; }

        internal static string StanceNameOf(DefLegion lg) { return StanceNames[StanceOf(lg)]; }

        // 军饷系数(操练状态加练: 军饷 ×1.3)
        internal static float WageMultOf(DefLegion lg)
        {
            float m = StanceOf(lg) == StanceDrill ? 1.3f : 1f;
            // v4.266: 军事统计 -6% 军饷(兵员/装备/粮秣统一造册 -> 军费效率); 落点 = 日军饷(DailyWageCost)
            try { m *= 1f - Research.WageSave(OurKingdom); } catch { }
            return m;
        }

        // 军粮系数(行军 ×1.4; 征粮自给 = 0; 后勤天才 -25%)
        internal static float FoodMultOf(DefLegion lg)
        {
            float m = 1f;
            if (StanceOf(lg) == StanceMarch) m *= 1.4f;
            if (StanceOf(lg) == StanceForage) m = 0f;
            if (HasTrait(lg, TraitLogistics)) m *= 0.75f;
            // v4.266: 军事科技省粮(后勤学 -12% / 总参谋部 -5% / 预备役 -3%);
            //   与 WarEconomy.ArmyFoodDaysOf 同口径, 军粮可支撑天数会一起变长
            try { m *= Math.Max(0.4f, 1f - Research.FoodSave(OurKingdom)); } catch { }
            return m;
        }

        // 训练度 0~100(旧档首次访问按兵种构成补齐)
        internal static float TrainLevelOf(DefLegion lg)
        {
            try
            {
                if (lg == null) return 0f;
                if (lg.Train < 0f) lg.Train = SeedTrain(lg);
                return lg.Train;
            }
            catch { return 0f; }
        }

        private static float SeedTrain(DefLegion lg)
        {
            try
            {
                var comp = CompOf(lg);
                int tier = TierOf(lg);
                float sum = 0f;
                int men = 0;
                for (int b = 0; b < 4; b++)
                {
                    if (comp[b] <= 0) continue;
                    var u = Equipment.Unit(Equipment.UnitOfBranch(b, tier));
                    if (u == null) continue;
                    sum += u.Training * comp[b];
                    men += comp[b];
                }
                float t = men > 0 ? sum / men : 1f;              // 兵种训练系数 0.85~1.15
                return Math.Max(0f, Math.Min(100f, t * 60f));    // 民兵 51 / 线列 60 / 掷弹兵 69
            }
            catch { return 60f; }
        }

        // 训练度 -> 战斗系数(0.8 ~ 1.2): 训练度 60 = ×1.04
        // v4.266: 叠加军事科技训练加成(士官训练 +8 / 兵棋推演 +6 / 军事统计 +5 / 装甲部队 +4 / 突击队 +4 /
        //   总参谋部 +3 / 军事航空 +3, 见 Research.TrainingBonus)。只改"战斗系数", 不写回 lg.Train —— 否则
        //   存档会把加成当成训练度存下来, 读档再加一次(双算)。
        internal static float TrainMultOf(DefLegion lg)
        {
            try
            {
                float t = TrainLevelOf(lg);
                try { t += Research.TrainingBonus(OurKingdom); } catch { }
                if (t > 100f) t = 100f;
                return 0.8f + 0.4f * (t / 100f);
            }
            catch { return 1f; }
        }

        internal static float TrainMultOf(MobileParty p)
        {
            try { return TrainMultOf(LegionOf(p)); } catch { return 1f; }
        }

        // 状态切换(UI 按钮循环): 戒备 -> 操练 -> 休整 -> 行军 -> 征粮 -> 戒备
        internal static string CycleStance(MobileParty p)
        {
            try
            {
                var lg = LegionOf(p);
                if (lg == null) return "不是国防军野战军团";
                lg.Stance = (StanceOf(lg) + 1) % StanceNames.Length;
                lg.PlayerHold = true;                 // v4.245: 玩家在管这支部队
                lg.PlayerOrderDay = Politics.Today();
                string nm = MapSelection.NameOf(p);
                DLog.Force("军团状态: " + nm + " -> " + StanceNames[lg.Stance]);
                return nm + " 状态改为「" + StanceNames[lg.Stance] + "」: " + StanceHelp(lg.Stance);
            }
            catch { return "状态切换失败"; }
        }

        // ==================== v4.245: 玩家意志优先(修"军团莫名其妙不听我话") ====================
        //   根因: 军事总监(AiWarDirector.LegionTick)每周、AiDevelopment 的解围/回防每日、
        //   铁路 AI 的运兵到达处理都会直接给国防军军团 SetMoveGoToSettlement/SetTask,
        //   把玩家刚下达的命令覆盖掉(玩家点远处目标后, 10 秒指挥窗口一过就被改道)。
        //   现在: 玩家任何命令 -> 该军团 PlayerHold=true, 上述所有 AI 路径一律跳过它;
        //   想交还给 AI 就点军务页[全军交还总监]或右键菜单[交还军事总监]。
        internal static void MarkPlayerOrder(MobileParty p)
        {
            try
            {
                var lg = LegionOf(p);
                if (lg == null) return;
                if (!lg.PlayerHold)
                {
                    lg.PlayerHold = true;
                    DLog.Force("军团接管: " + MapSelection.NameOf(p) + " 已归玩家指挥(军事总监不再调它)");
                }
                lg.PlayerOrderDay = Politics.Today();
            }
            catch { }
        }

        internal static bool IsPlayerHeld(DefLegion lg)
        {
            try { return lg != null && lg.PlayerHold; } catch { return false; }
        }

        internal static bool IsPlayerHeld(MobileParty p)
        {
            try { return IsPlayerHeld(LegionOf(p)); } catch { return false; }
        }

        // 交还军事总监(单支)
        //   v4.273(审计 #4): PlayerHold 是**所有守卫的总开关** —— 周结 AI / 铁路运兵 / EnsureNavigation /
        //   Hourly 的 SetMoveModeHold 全都先问它。原实现对海运中的军团也无条件置 false ⇒ 海运当天的
        //   推进链路(DefaultBehavior=GoToSettlement / DesiredAiNavigationType=Naval)立刻被 AI 改回陆地目标,
        //   军团带着一船人掉头回岸。现在: 海运中的军团**跳过**, 不改 PlayerHold/Task, 并给玩家中文提示。
        //   (只改这一处判定, 本文件其它逻辑一字不动。)
        internal static string ReleaseToAi(MobileParty p)
        {
            try
            {
                var lg = LegionOf(p);
                if (lg == null) return "不是国防军野战军团";
                if (p != null && NavalTransport.InConvoy(p.StringId))
                {
                    DLog.Force("军团交还拦截(海运中): " + MapSelection.NameOf(p) + " 保持玩家指挥");
                    return "该军团正在海运途中, 不能交还军事总监 —— 请先到海军页[召回], 或等它到港下船后再交还";
                }
                lg.PlayerHold = false;
                lg.Task = "驻守";
                DLog.Force("军团交还: " + MapSelection.NameOf(p) + " 交还军事总监");
                return MapSelection.NameOf(p) + " 已交还军事总监(恢复自动调度)";
            }
            catch { return "交还失败"; }
        }

        // 交还军事总监(全军)
        //   v4.273(审计 #4): 同上 —— 海运中的军团跳过(保留 PlayerHold), 并在返回值里如实说明跳过了几支,
        //   免得玩家看到"已交还 N 支"却发现自己那支还在自己名下。
        internal static string ReleaseAllToAi()
        {
            try
            {
                int n = 0;
                int skipped = 0;
                for (int i = 0; i < Legions.Count; i++)
                {
                    var lg = Legions[i];
                    if (lg == null || !lg.PlayerHold) continue;
                    if (NavalTransport.InConvoy(lg.PartyId))
                    {
                        skipped++;
                        DLog.Force("军团交还拦截(海运中): " + lg.PartyId + " 保持玩家指挥");
                        continue;
                    }
                    lg.PlayerHold = false;
                    lg.Task = "驻守";
                    n++;
                }
                DLog.Force("军团交还: 全军 " + n + " 支交还军事总监" + (skipped > 0 ? ("(海运中 " + skipped + " 支跳过)") : ""));
                string tail = skipped > 0 ? (" · 海运途中 " + skipped + " 支未交还(请先召回或等其到港)") : "";
                return n > 0
                    ? ("已交还 " + n + " 支军团给军事总监(恢复自动调度)" + tail)
                    : (skipped > 0 ? ("没有可交还的军团 —— " + skipped + " 支正在海运途中, 请先召回") : "没有玩家接管的军团");
            }
            catch { return "交还失败"; }
        }

        internal static int PlayerHeldCount()
        {
            try
            {
                int n = 0;
                for (int i = 0; i < Legions.Count; i++) if (Legions[i] != null && Legions[i].PlayerHold) n++;
                return n;
            }
            catch { return 0; }
        }

        // 军事总监/AI 是否可以对这支部队自动下命令(统一判据; 所有 AI 改道入口都该先问这里)
        internal static bool CanAutoMove(MobileParty p)
        {
            try
            {
                if (p == null || !p.IsActive) return false;
                if (!IsDefArmyParty(p)) return true;          // 非国防军(领主军/联军)不受此约束
                var lg = LegionOf(p);
                // v4.254: 登记表查不到但确实是国防军(id 前缀)时**不许** AI 动 —— 原来这里 return true,
                //   一旦 Legion.PartyId 与部队 id 脱钩(重建/异常读档/合并), PlayerHold 保护就凭空失效,
                //   军团立刻重新被军事总监和铁路 AI 调走。
                if (lg == null) return false;
                if (lg.PlayerHold) return false;              // 玩家接管中
                if (CommandTimeout.IsCommanded(p)) return false;   // 玩家刚下过命令
                if (MapSelection.Is(p)) return false;             // 玩家正选着它
                return true;
            }
            catch { return false; }
        }

        internal static string StanceHelpPublic(int s) { return StanceHelp(s); }

        internal static void SetStance(DefLegion lg, int s)
        {
            try { if (lg != null) lg.Stance = Math.Max(0, Math.Min(StanceNames.Length - 1, s)); } catch { }
        }

        // 状态日结(逐军团): 训练度/熟练度/士气/组织度/伤员/征粮
        private static void StanceDaily(DefLegion lg)
        {
            try
            {
                var p = LegionParty(lg);
                if (p == null || !p.IsActive) return;
                int men = RegularsOf(p);
                if (men <= 0) return;
                string key = ArmyDoctrine.LegionKey(lg);
                if (lg.Train < 0f) lg.Train = SeedTrain(lg);
                // v4.243: 旧档/旧军团补一次将军特质(只补一次: 补完掩码非 0)
                if (lg.GeneralTraits == 0 && !string.IsNullOrEmpty(lg.GeneralId))
                {
                    Hero gh = null;
                    try { gh = Hero.FindFirst(delegate (Hero x) { return x != null && x.StringId == lg.GeneralId; }); } catch { }
                    if (gh != null)
                    {
                        lg.GeneralTraits = RollGeneralTraits(gh, CompOf(lg));
                        DLog.Force("军团特质补齐: 第" + lg.Number + "军团 -> " + TraitText(lg.GeneralTraits));
                    }
                }
                float baseGrow = 0.05f + (HasTrait(lg, TraitInfantry) ? 0.10f : 0f);
                switch (StanceOf(lg))
                {
                    case StanceDrill:
                        lg.Train = Math.Min(100f, lg.Train + 0.35f + baseGrow);
                        Soldiers.TrainUp(p, 0.15f);                   // 逐人熟练度成长
                        ArmyDoctrine.OrgGain(key, 1.5f);
                        ArmyDoctrine.MoraleGain(key, 0.3f);
                        break;
                    case StanceRest:
                        lg.Train = Math.Min(100f, lg.Train + 0.10f);
                        ArmyDoctrine.OrgGain(key, 2f);
                        ArmyDoctrine.MoraleGain(key, 1.5f);
                        ArmyDoctrine.WoundedHeal(key, 2f * (HasTrait(lg, TraitPaternal) ? 1.5f : 1f) * (HasSupport(p, 1) ? 1.2f : 1f));
                        break;
                    case StanceMarch:
                        lg.Train = Math.Max(0f, lg.Train - 0.05f);
                        ArmyDoctrine.OrgLoss(key, 0.5f);
                        break;
                    case StanceForage:
                        lg.Train = Math.Min(100f, lg.Train + 0.02f);
                        ForageAt(lg, p, men);
                        break;
                    default:
                        lg.Train = Math.Min(100f, lg.Train + baseGrow);
                        ArmyDoctrine.OrgGain(key, 0.5f);
                        ArmyDoctrine.MoraleGain(key, 0.5f);
                        break;
                }
            }
            catch { }
        }

        // ==================== v4.248: 速度诊断 ====================
        //   用户反复反馈"人多的国防军部队速度快, 人少的又慢"; 速度锁补丁之外还有 铁路/王国特性 两个
        //   CalculateFinalSpeed 补丁, 为确认实测值, 每天把每个军团的速度/人数/识别状态记一行(变化时才记)
        private static readonly Dictionary<string, float> _lastSpeedSeen = new Dictionary<string, float>();

        private static void SpeedAudit()
        {
            try
            {
                for (int i = 0; i < Legions.Count; i++)
                {
                    var lg = Legions[i];
                    var p = LegionParty(lg);
                    if (p == null || !p.IsActive) continue;
                    float spd = 0f;
                    try { spd = p.Speed; } catch { }
                    float prev;
                    if (_lastSpeedSeen.TryGetValue(lg.PartyId, out prev) && Math.Abs(prev - spd) < 0.05f) continue;
                    _lastSpeedSeen[lg.PartyId] = spd;
                    DLog.Force("速度诊断: 第" + lg.Number + "军团 速度=" + spd.ToString("F2")
                        + " 人数=" + RegularsOf(p)
                        + " 识别=" + (IsDefArmyParty(p) ? "是" : "否")
                        + " 在城=" + (p.CurrentSettlement != null ? "是" : "否")
                        + " 铁路=" + (ArmyDoctrine.IsRailTransport(p) ? "开" : "关")
                        + " 状态=" + StanceNameOf(lg));
                }
            }
            catch { }
        }

        // ==================== v4.249: 进出城诊断 ====================
        //   用户反馈"部队莫名其妙进城了": 这里每天记录军团的进出城事件与当时的上下文
        //   (接管标志/任务/是否被指挥), 用来定位到底是谁把它弄进城的。
        private static readonly Dictionary<string, bool> _inSettlementSeen = new Dictionary<string, bool>();

        private static void SettlementAudit()
        {
            try
            {
                for (int i = 0; i < Legions.Count; i++)
                {
                    var lg = Legions[i];
                    var p = LegionParty(lg);
                    if (p == null || !p.IsActive) continue;
                    bool inSt = p.CurrentSettlement != null;
                    bool was;
                    if (_inSettlementSeen.TryGetValue(lg.PartyId, out was) && was != inSt)
                    {
                        string where = inSt && p.CurrentSettlement != null && p.CurrentSettlement.Name != null
                            ? p.CurrentSettlement.Name.ToString() : "";
                        DLog.Force("军团" + (inSt ? "进城" : "出城") + ": 第" + lg.Number + "军团"
                            + (inSt ? " -> " + where : "")
                            + " 接管=" + lg.PlayerHold
                            + " 任务=" + (lg.Task ?? "-")
                            + " 被指挥=" + CommandTimeout.IsCommanded(p)
                            + " 状态=" + StanceNameOf(lg));
                    }
                    _inSettlementSeen[lg.PartyId] = inSt;
                }
            }
            catch { }
        }

        // 征粮: 就地抽粮(代价: 该城忠诚 -0.4/日, 民怨 +0.2/日)
        private static void ForageAt(DefLegion lg, MobileParty p, int men)
        {
            try
            {
                lg.Starve = 0;   // 征粮期间不算断粮
                var s = p.CurrentSettlement;
                if (s == null) return;
                var grain = FeudalGoods.Item(FeudalGoods.Grain);
                if (grain != null && s.ItemRoster != null)
                {
                    int want = (int)Math.Ceiling(men * FoodPerManPerDay * 2f);
                    int have = s.ItemRoster.GetItemNumber(grain);
                    int got = Math.Min(have, want);
                    if (got > 0)
                    {
                        s.ItemRoster.AddToCounts(grain, -got);
                        // v4.290 修复: 与 FeedLegions 同一个问题 —— 征粮原先"只从聚落扣、不进部队",
                        //   于是 lg.Starve 被清成 0(mod 以为自给自足), 而原版照样判定军团饥饿、
                        //   照样按 14.87%/天逃兵。征粮必须是"把粮搬进部队", 不能只是账面自给。
                        try { p.ItemRoster.AddToCounts(grain, got); } catch { }
                    }
                }
                try { if (s.Town != null) s.Town.Loyalty = Math.Max(0f, s.Town.Loyalty - 0.4f); } catch { }
                try { Politics.ConscriptGrievance = Math.Min(100f, Politics.ConscriptGrievance + 0.2f); } catch { }
            }
            catch { }
        }

        private static void PayWages()
        {
            try
            {
                int men = TotalMen();
                if (men <= 0) { UnpaidDays = 0; return; }
                int cost = DailyWageCost();   // v4.243: 含状态系数(操练 ×1.3)
                if (EconomyWorld.Treasury.Gold >= cost)
                {
                    EconomyWorld.TreasurySpend(cost);
                    Fiscal.AddMilitary(cost);
                    // v4.252: 删除"把玩家金币补到 30 日军饷"的兜底 —— 那是直接改 Hero.Gold(不走
                    //   TreasuryAdd/账本), 而国库每天又与 Hero.Gold 对齐, 等于**每天凭空印钱**,
                    //   国防军实际免费(经济审计报告实测: 有军团时国库永久 ≥30 日饷, 永不破产)。
                    //   原版重复扣薪的问题改从源头解决: DefArmyPatches.DefArmyNoNativeWagePatch
                    //   让原版算国防军工资时返回 0(mod 自己收军饷, 不再被原版按家族金库扣第二遍)。
                    UnpaidDays = 0;
                    MutinyWarned = false;
                }
                else
                {
                    UnpaidDays++;
                    if (UnpaidDays >= 7 && !MutinyWarned)
                    {
                        MutinyWarned = true;
                        MapSelection.Message("国防军欠饷 " + UnpaidDays + " 天: 全军士气 -30%, 再不军饷将哗变!");
                        for (int i = 0; i < Legions.Count; i++)
                        {
                            var p = LegionParty(Legions[i]);
                            if (p != null && p.IsActive) { try { p.RecentEventsMorale = p.RecentEventsMorale - 30f; } catch { } }
                        }
                        DLog.Force("国防军: 欠饷 " + UnpaidDays + " 天警告");
                    }
                    if (UnpaidDays >= 14) MutinyAll();
                }
            }
            catch { }
        }

        // v4.89: 军团人数变动日志(诊断"莫名其妙减少人": 逃兵/战斗/原版欠薪都会在这里体现)
        private static readonly System.Collections.Generic.Dictionary<string, int> LastMen = new System.Collections.Generic.Dictionary<string, int>();

        private static void LogLegionChanges()
        {
            try
            {
                for (int i = 0; i < Legions.Count; i++)
                {
                    var lg = Legions[i];
                    var p = LegionParty(lg);
                    if (p == null || !p.IsActive) continue;
                    int men = RegularsOf(p);
                    int prev;
                    if (LastMen.TryGetValue(lg.PartyId, out prev) && prev != men)
                        DLog.Force("国防军: " + MapSelection.NameOf(p) + " 人数 " + prev + " -> " + men
                            + (men < prev ? " (减少 " + (prev - men) + ")" : ""));
                    LastMen[lg.PartyId] = men;
                }
            }
            catch { }
        }

        private static void FeedLegions()
        {
            try
            {
                var grain = FeudalGoods.Item(FeudalGoods.Grain);
                for (int i = 0; i < Legions.Count; i++)
                {
                    var lg = Legions[i];
                    var p = LegionParty(lg);
                    if (p == null || !p.IsActive) continue;
                    int men = p.MemberRoster.TotalManCount;
                    int regs = RegularsOf(p);   // v4.255: 逃散按正兵口径(不含将军)
                    int need = (int)Math.Ceiling(men * FoodPerManPerDay * FoodMultOf(lg));   // v4.243: 行军 ×1.4 / 征粮 0 / 后勤 -25%
                    // v4.266: 军事建筑"补给站/军械库"(@supply) —— 驻地军需容量降低该军团日耗粮(与 WarEconomy 的军粮天数同口径)
                    var h0 = HomeOf(lg);
                    if (h0 != null)
                    {
                        float save = MilBuildings.SupplyFoodSaveOf(h0);
                        if (save > 0f) need = (int)Math.Ceiling(need * (1f - save));
                    }
                    if (StanceOf(lg) == StanceForage || need <= 0) { lg.Starve = 0; continue; }   // 征粮自行解决
                    var home = HomeOf(lg);
                    bool ok = false;
                    if (home != null && grain != null && home.ItemRoster != null)
                    {
                        int have = home.ItemRoster.GetItemNumber(grain);
                        if (have >= need)
                        {
                            home.ItemRoster.AddToCounts(grain, -need);
                            // v4.290 修复(最严重的一条): 粮必须真的进部队。
                            //   原版 FoodConsumptionBehavior.DailyTickParty 每天扣的是**部队自己的**
                            //   ItemRoster, 判据 DoesPartyConsumeFood 含 LeaderHero.IsLord —— 而国防军将军
                            //   正是 SetNewOccupation(Occupation.Lord), 三重命中无法逃逸。
                            //   原先这里只从驻地粮仓扣、军团 ItemRoster 一件没收到 -> 吃光建军时原版给的
                            //   15~30 单位谷物后永久饥饿 -> -30 士气, 且连续两天起每天再 -5(衰减后稳态 -50)
                            //   -> 原版 PartyDesertionCampaignBehavior 真的会执行, 概率 1-0.2^((10-morale)/100)
                            //   = 稳态 14.87%/天, 1000 人军团约 25~30 个衰减日被打残。
                            //   更糟的是这些减员会被 Reconcile 记成"军损"并从士兵池扣人口(不回平民池),
                            //   等于逃兵当阵亡记, 总人口单调下降。
                            //   mod 自己的"休整 +1.5/日"走的是 ArmyDoctrine 的 Morale2 字典,
                            //   根本不碰原版 RecentEventsMorale, 对逃兵判定毫无作用。
                            try { p.ItemRoster.AddToCounts(grain, need); } catch { }
                            ok = true;
                        }
                    }
                    if (ok) { lg.Starve = 0; continue; }
                    lg.Starve++;
                    if (lg.Starve > 3)
                    {
                        int desert = Math.Max(1, regs / 50);
                        home = home != null ? home : FindSettlement(lg.HomeId);
                        int got = 0;
                        if (home != null && home.Culture != null)
                        {
                            got = RemoveDefTroops(p.MemberRoster, home.Culture, desert);
                            ReturnSoldiersToCommoners(home, got);
                        }
                        lg.Expected = RegularsOf(p);
                        // v4.92: 减员给消息(说明原因), 用户要求"给人减少的时候加上消息"
                        MapSelection.Message("国防军减员: " + MapSelection.NameOf(p) + " 断粮逃散 -" + got
                            + " 人(驻地 " + (home != null && home.Name != null ? home.Name.ToString() : "?") + " 粮仓无粮)");
                        DLog.Force("国防军: 断粮减员 " + MapSelection.NameOf(p) + " -" + got + " (连续断粮 " + lg.Starve + " 天)");
                    }
                }
            }
            catch { }
        }

        // 伤亡对账: 编制 vs 实际, 差额进"军损"(从关联人口扣)
        internal static void Reconcile()
        {
            try
            {
                int loss = 0;
                for (int i = Legions.Count - 1; i >= 0; i--)
                {
                    var lg = Legions[i];
                    if (lg == null) { Legions.RemoveAt(i); continue; }
                    var p = LegionParty(lg);
                    if (p == null || !p.IsActive)
                    {
                        int men = lg.Expected;
                        if (men > 0) { RemoveSoldiersPop(HomeOf(lg), men); loss += men; }
                        if (!string.IsNullOrEmpty(lg.PartyId)) LegionEquip.Remove(lg.PartyId);   // 部队覆灭 -> 装备随军损失
                        RetireGeneral(lg.GeneralId);
                        Legions.RemoveAt(i);
                        DLog.Force("国防军: " + LegionName(lg.Number) + " 编制消失(阵亡/被俘), 军损 " + men);
                        continue;
                    }
                    int actual = RegularsOf(p);   // v4.255: 正兵口径(含伤员, 不含将军) —— 原来用 TotalHealthyCount
                                                  //   会把伤员也算成阵亡扣 pop, 并把 Expected 写成 101
                    if (actual < lg.Expected)
                    {
                        int d = lg.Expected - actual;
                        RemoveSoldiersPop(HomeOf(lg), d);
                        WearEquipByLoss(lg, d, lg.Expected);   // 27.4: 战损按伤亡比例扣减装备
                        loss += d;
                    }
                    lg.Expected = actual;
                }
                for (int i = 0; i < Garrisons.Count; i++)
                {
                    var g = Garrisons[i];
                    if (g == null) continue;
                    var s = FindSettlement(g.SettlementId);
                    if (s == null) continue;
                    var party = GarrisonPartyOf(s);
                    if (party == null || s.Culture == null) continue;
                    int actual = CountDefTroops(party.MemberRoster, s.Culture);
                    if (actual < g.Placed)
                    {
                        int d = g.Placed - actual;
                        RemoveSoldiersPop(s, d);
                        loss += d;
                    }
                    g.Placed = actual;
                }
                if (loss > 0)
                {
                    TodayMilLoss += loss;
                    if (loss >= 50) MapSelection.Message("国防军今日军损 " + loss + " 人");
                }
            }
            catch { }
        }

        private static void MutinyAll()
        {
            try
            {
                int men = TotalMen();
                DLog.Force("国防军: 欠饷 14 天, 哗变! 兵力 " + men);
                MapSelection.Message("国防军欠饷 14 天: 全军哗变! 50% 回乡, 30% 流散, 20% 投敌");

                for (int i = 0; i < Legions.Count; i++)
                {
                    var lg = Legions[i];
                    var p = LegionParty(lg);
                    if (p != null && p.IsActive)
                    {
                        // v4.255: 哗变按正兵口径(原来 TotalManCount 会把将军当兵还给人口/移除)
                        int pm = RegularsOf(p);
                        ApplyMutinyPop(lg.HomeId, pm);
                        RemoveDefTroops(p.MemberRoster, CultureOfLegion(lg), pm);
                    }
                    RetireGeneral(lg.GeneralId);
                    try { if (p != null) DestroyPartyAction.ApplyForDisbanding(p, HomeOf(lg)); } catch { }
                }
                for (int i = 0; i < Garrisons.Count; i++)
                {
                    var g = Garrisons[i];
                    var s = FindSettlement(g.SettlementId);
                    if (s == null) continue;
                    ApplyMutinyPop(g.SettlementId, g.Placed);
                    var party = GarrisonPartyOf(s);
                    if (party != null && s.Culture != null) RemoveDefTroops(party.MemberRoster, s.Culture, g.Placed);
                }
                Legions.Clear();
                Garrisons.Clear();
                Conscripts.Clear();
                LegionEquip.Clear();   // 哗变: 装备随军散失(不返还)
                UnpaidDays = 0;
                MutinyWarned = false;
                Politics.Legitimacy = Math.Max(0f, Politics.Legitimacy - 3f);
                Politics.Authority = Math.Max(0f, Politics.Authority - 10f);
                Politics.Aggregate();
            }
            catch (Exception ex) { DLog.Force("哗变处理异常: " + ex); }
        }

        private static void ApplyMutinyPop(string settlementId, int men)
        {
            try
            {
                if (men <= 0) return;
                var s = FindSettlement(settlementId);
                if (s == null) return;
                int back = men / 2;
                int roam = (int)(men * 0.3f);
                int gone = men - back - roam;
                var sold = PopOf(s, PopDefs.Soldiers);
                if (sold == null) return;
                int avail = (int)Math.Floor(sold.Size);
                if (back > avail) back = avail;
                if (roam > avail - back) roam = Math.Max(0, avail - back);
                if (gone > avail - back - roam) gone = Math.Max(0, avail - back - roam);
                sold.Size -= (back + roam + gone);
                var peas = PopOf(s, PopDefs.Peasants);
                if (peas != null) peas.Size += back;
                var unemp = PopOf(s, PopDefs.Unemployed);
                if (unemp != null) unemp.Size += roam;
            }
            catch { }
        }

        // ==================== 每月 ====================
        private static void Month(int day)
        {
            try
            {
                EquipMonth(day);   // 27.4: 每月 1% 磨损 + 补装(前线 > 低满足率 > 后方, 国家库优先)
                // v4.243: 政治将军 -> 合法性 +1/月 · 爱兵如子 -> 士气 +2/月 · 严酷军纪 -> 士气 -1/月
                try
                {
                    int leg = 0, pol = 0, pat = 0, dis = 0;
                    for (int i = 0; i < Legions.Count; i++)
                    {
                        var lg2 = Legions[i];
                        if (lg2 == null) continue;
                        leg++;
                        if (HasTrait(lg2, TraitPolitical)) pol++;
                        if (HasTrait(lg2, TraitPaternal)) pat++;
                        if (HasTrait(lg2, TraitDiscipline)) dis++;
                    }
                    if (pol > 0) Politics.Legitimacy = Math.Min(100f, Politics.Legitimacy + pol);
                    if (pat > 0 || dis > 0)
                    {
                        float dm = pat * 2f - dis * 1f;
                        for (int i = 0; i < Legions.Count; i++)
                        {
                            var lp2 = LegionParty(Legions[i]);
                            if (lp2 != null && lp2.IsActive) { try { lp2.RecentEventsMorale += dm; } catch { } }
                        }
                    }
                    if (leg > 0) DLog.Force("军团月结: 军团=" + leg + " 政治将军=" + pol + " 爱兵如子=" + pat + " 严酷军纪=" + dis);
                }
                catch { }
                // 将军月薪 30
                int generals = Legions.Count;
                if (generals > 0)
                {
                    int cost = generals * GeneralWageMonth;
                    if (EconomyWorld.Treasury.Gold >= cost)
                    {
                        EconomyWorld.TreasurySpend(cost);
                        Fiscal.AddMilitary(cost);
                    }
                }
                // 贵族愤怒: 现役每 100 兵 +1/月, 军务征召每级抵消 1
                int men = TotalMen();
                int add = men / 100 - Politics.LawLevel(2);
                if (add > 0)
                {
                    foreach (var kv in Politics.Lords)
                    {
                        var lp = kv.Value;
                        if (lp == null) continue;
                        lp.Anger += add;
                        if (lp.Anger > 100) lp.Anger = 100;
                    }
                    Politics.Aggregate();
                }
                // 常备 > 领主私兵总和 50% -> 合法性 -1/月
                int lordMen = LordPartyMen();
                if (men > lordMen / 2 && men > 100)
                    Politics.Legitimacy = Math.Max(0f, Politics.Legitimacy - 1f);
                // v4.239: 强制征兵民怨沸腾 -> 征丁逃亡(民怨 >= 70 起, 85 以上翻倍)
                try
                {
                    string des = ConscriptDesertion();
                    if (!string.IsNullOrEmpty(des))
                    {
                        try { MapSelection.Message(des); } catch { }
                    }
                }
                catch { }
                TodayMilLoss = 0;
                DLog.Force("国防军月结: 兵力=" + men + " 军团=" + Legions.Count + " 守备=" + Garrisons.Count
                    + " 欠饷天=" + UnpaidDays + " 军费可撑=" + DaysAffordable() + "天"
                    + " 征兵民怨=" + ((int)Math.Round(Politics.ConscriptGrievance)));
            }
            catch { }
        }

        private static int LordPartyMen()
        {
            int n = 0;
            try
            {
                var k = OurKingdom;
                if (k == null) return 0;
                var c = Campaign.Current;
                if (c == null || c.MobileParties == null) return 0;
                foreach (var p in c.MobileParties)
                {
                    if (p == null || !p.IsActive || p.IsGarrison || p.IsVillager || p.IsMainParty) continue;
                    if (!ReferenceEquals(p.MapFaction, k)) continue;
                    if (IsDefArmyParty(p)) continue;
                    n += p.MemberRoster.TotalManCount;
                }
            }
            catch { }
            return n;
        }

        internal static string Parade()
        {
            try
            {
                if (EconomyWorld.Treasury.Gold < 1500) return "国库不足 1500 第纳尔";
                EconomyWorld.TreasurySpend(1500);
                Fiscal.AddMilitary(1500);
                Politics.Authority = Math.Min(1000f, Politics.Authority + 10f);
                foreach (var kv in Politics.Lords)
                {
                    var lp = kv.Value;
                    if (lp == null) continue;
                    lp.Anger = Math.Max(0, lp.Anger - 1);
                }
                Politics.Aggregate();
                string msg = "阅兵: 权威 +10, 贵族愤怒 -1";
                DLog.Force("国防军: " + msg);
                return msg;
            }
            catch (Exception ex) { DLog.Force("阅兵失败: " + ex.Message); return "阅兵失败"; }
        }

        internal static void Hourly()
        {
            try
            {
                // v4.105: 国防军全靠玩家指挥 —— 无任务/未被选中/不在指挥窗口内的军团强制待命(不自己溜达)
                for (int i = 0; i < Legions.Count; i++)
                {
                    var lg = Legions[i];
                    var p = LegionParty(lg);
                    if (p == null || !p.IsActive) continue;
                    // v5.x AI: "AI*" 任务由军事总监接管(周结下令), 不再强制待命(否则会取消围城等指令)
                    // v4.290 修复: 补上"驻守" —— ReleaseToAi/创建军团写的都是"驻守"(见 :1377/:1602/:2250/:2277),
                    //   而这里原先只认"驻防/巡逻/AI*", 于是玩家一点[交还军事总监]就被 SetMoveModeHold() 按住,
                    //   紧接着被原版 AI 接管(交还给的是原版 AI, 不是 mod 的军事总监)。
                    bool hasTask = lg.Task != null && (lg.Task.Contains("驻防") || lg.Task.Contains("巡逻")
                        || lg.Task.Contains("驻守")
                        || lg.Task.StartsWith("AI", StringComparison.Ordinal));
                    bool commanded = CommandTimeout.IsCommanded(p) || MapSelection.Is(p);
                    // v4.254: **玩家接管的军团不许被"强制待命"** —— 原来只要 10 秒指挥窗口一过、且没有
                    //   驻防/巡逻/AI* 任务, 每小时整点就会被 SetMoveModeHold() 按住, 于是:
                    //   ①"点城镇前往"走到城门附近(距离<1.5m 时驾驶目标被清)就被 Hold -> 停在城门外不进城;
                    //   ②"攻击"没有目标点、窗口一过就被 Hold -> 攻击命令被取消(用户: 点了没反应)。
                    if (lg.PlayerHold) continue;
                    if (!hasTask && !commanded)
                    {
                        try { p.SetMoveModeHold(); } catch { }
                    }
                }
            }
            catch { }
        }

        // ==================== 查询辅助 ====================
        // v4.96: 巡逻目标点(驻地城门外周围随机位置, 供手动驾驶绕圈)
        internal static bool TryPatrolPoint(DefLegion lg, out CampaignVec2 point)
        {
            point = default(CampaignVec2);
            try
            {
                var home = HomeOf(lg);
                if (home == null) return false;
                var basePos = home.GatePosition.ToVec2();
                float ang = (float)(MBRandom.RandomFloat * Math.PI * 2.0);
                float rad = 8f + MBRandom.RandomFloat * 10f;
                var v = new Vec2(basePos.X + (float)Math.Cos(ang) * rad, basePos.Y + (float)Math.Sin(ang) * rad);
                point = new CampaignVec2(v, true);
                return true;
            }
            catch { return false; }
        }

        // v4.100: 距离指定聚落最近的本国联军(用于城市抽屉展示/召唤)
        internal static Army NearestArmyOf(Settlement s, out float dist)
        {
            dist = float.MaxValue;
            try
            {
                if (s == null) return null;
                var k = OurKingdom;
                var pos = s.Position.ToVec2();
                Army best = null;
                var seen = new HashSet<Army>();
                foreach (var p in MobileParty.All)
                {
                    if (p == null || !p.IsActive || p.Army == null) continue;
                    var a = p.Army;
                    if (!seen.Add(a)) continue;
                    if (k == null || a.Kingdom == null || !ReferenceEquals(a.Kingdom, k)) continue;
                    var lp = a.LeaderParty;
                    if (lp == null) continue;
                    float dx = lp.Position.X - pos.X, dy = lp.Position.Y - pos.Y;
                    float d = dx * dx + dy * dy;
                    if (d < dist) { dist = d; best = a; }
                }
                return best;
            }
            catch { return null; }
        }

        // v4.100: 召唤距离该城最近的本国联军前往该城
        internal static string CallNearestArmy(Settlement s)
        {
            try
            {
                if (s == null) return "无效城市";
                float d;
                var best = NearestArmyOf(s, out d);
                if (best == null) return "附近没有可召唤的军团(联军), 且守备营不足 100 兵";
                var leader = best.LeaderParty;
                if (leader == null) return "该军团没有首领部队";
                leader.SetMoveGoToSettlement(s, MobileParty.NavigationType.Default, false);
                try { CommandTimeout.Touch(leader, s.GatePosition); } catch { }
                return "已召唤「" + (best.Name != null ? best.Name.ToString() : "军团") + "」前往 "
                    + (s.Name != null ? s.Name.ToString() : "目标城市");
            }
            catch (Exception ex) { DLog.Force("召唤军团失败: " + ex.Message); return "召唤失败, 见日志"; }
        }

        internal static MobileParty FindParty(string id)
        {
            try
            {
                if (string.IsNullOrEmpty(id)) return null;
                var c = Campaign.Current;
                if (c == null || c.MobileParties == null) return null;
                foreach (var p in c.MobileParties)
                    if (p != null && p.StringId == id) return p;
            }
            catch { }
            return null;
        }

        internal static Settlement FindSettlement(string id)
        {
            try
            {
                if (string.IsNullOrEmpty(id)) return null;
                foreach (var s in Settlement.All) if (s != null && s.StringId == id) return s;
            }
            catch { }
            return null;
        }

        private static Clan FindClan(string id)
        {
            try
            {
                if (string.IsNullOrEmpty(id)) return null;
                foreach (var c in Clan.FindAll(delegate (Clan x) { return x != null && x.StringId == id; }))
                    return c;
            }
            catch { }
            return null;
        }

        // 军务页可选聚落(本国城镇/城堡 + 附属村庄也可征募)
        internal static List<Settlement> CandidateSettlements()
        {
            var list = new List<Settlement>();
            try
            {
                var k = OurKingdom;
                if (k == null) return list;
                foreach (var s in Settlement.All)
                {
                    if (s == null) continue;
                    if (!(s.IsTown || s.IsCastle)) continue;
                    if (!ReferenceEquals(s.MapFaction, k)) continue;
                    list.Add(s);
                }
                list.Sort(delegate (Settlement a, Settlement b)
                { return string.Compare(a.Name != null ? a.Name.ToString() : "", b.Name != null ? b.Name.ToString() : "", StringComparison.Ordinal); });
            }
            catch { }
            return list;
        }

        // 募兵/征兵来源: 城镇自己(劳工) + 附属村庄(农民); 村庄则只有自己
        internal static List<Settlement> SourcesOf(Settlement s)
        {
            var list = new List<Settlement>();
            try
            {
                if (s == null) return list;
                if (s.IsVillage) { list.Add(s); return list; }
                list.Add(s);
                foreach (var v in Settlement.All)
                {
                    if (v == null || !v.IsVillage || v.Village == null) continue;
                    if (!ReferenceEquals(v.Village.Bound, s)) continue;
                    list.Add(v);
                }
            }
            catch { }
            return list;
        }

        // 军团补员: 从驻地守备营抽兵补入军团(不超过 10000)
        internal static string ReplenishLegion(MobileParty p, int want)
        {
            try
            {
                var lg = LegionOf(p);
                if (lg == null) return "只能给国防军野战军团补员";
                var home = HomeOf(lg);
                if (home == null) return "找不到驻地";
                var garrison = GarrisonPartyOf(home);
                if (garrison == null || home.Culture == null) return "驻地没有守备营";
                int cur = RegularsOf(p);   // v4.87: 容量按正兵算
                // v4.256: 补员必须看**编制上限**(其余三个入口都已用 CommandLimitOf) ——
                //   原来用 MaxLegionMen(100 万), 这条路能把军团一路补到百万级, 组织度被超编惩罚
                //   压到 0.6 而军务页不显示(界面审计发现)。
                int room;
                try { room = ArmyDoctrine.CommandLimitOf(p) - cur; } catch { room = MaxLegionMen - cur; }
                if (room <= 0) return "军团已满编(编制 " + cur + ")";
                int avail = GarrisonMenOf(home.StringId);   // v4.90: 账面口径
                if (avail <= 0) return "驻地守备营没有可调兵员(先募兵)";
                int n = Math.Min(Math.Min(want, room), avail);
                if (n <= 0) return "没有可补充的兵员";
                int moved = MoveDefTroops(garrison.MemberRoster, p.MemberRoster, home.Culture, n);
                var rec = GetGarrisonRec(home.StringId, true);
                if (rec != null) rec.Placed = Math.Max(0, rec.Placed - moved);
                // v4.255: 补进来的兵, 装备从守备营账转交(避免月结时按全额再领一遍)
                TransferGarrisonEquip(lg, rec, moved, rec != null ? rec.Placed : 0);
                try { RefreshFill(lg); } catch { }
                lg.Expected = RegularsOf(p);
                string msg = "补员 " + moved + " 人 → " + MapSelection.NameOf(p) + "(编制 " + RegularsOf(p) + ")";
                DLog.Force("国防军: " + msg);
                return msg;
            }
            catch (Exception ex) { DLog.Force("补员失败: " + ex); return "补员失败, 见日志"; }
        }

        // 扩充第二来源(征兵地)逐兵种上限(v4.234): 编制余额 ∩ 可征人口 ∩ 国库 ∩ 国家军械库装备
        //   与 ReplenishLegionFrom 完全同口径: 部队管理页滑条/征兵地列表都读这里, 修"选了有征兵名额的地仍扩不了"
        internal static int LegionRecruitCap(MobileParty p, Settlement s, int kindIdx)
        {
            try
            {
                if (LegionOf(p) == null || s == null) return 0;
                var u = Equipment.Unit(kindIdx);
                if (u == null) return 0;
                int cur = RegularsOf(p);
                int cmdRoom;
                try { cmdRoom = ArmyDoctrine.CommandLimitOf(p) - cur; } catch { cmdRoom = MaxLegionMen - cur; }
                if (cmdRoom <= 0) return 0;
                long cap = cmdRoom;
                int pop = RealCommonersOf(s);
                if (pop < cap) cap = pop;
                // v4.238: 原版志愿兵名额(28.4 条件 1 / 28.6 预览项"可用名额") —— 该地此刻能动员多少人
                int slots = SlotQuotaOf(s);
                if (slots < cap) cap = slots;
                double per = RecruitCost + LegionTrainCostPerMan;
                try { if (Politics.SeatHeld(3)) per = per * 0.9; } catch { }
                if (per < 1.0) per = 1.0;
                long gold = 0;
                try { gold = EconomyWorld.Treasury.Gold; } catch { }
                long goldCap = (long)Math.Floor(gold / per + 0.000001);
                if (goldCap < cap) cap = goldCap;
                long eq = EquipCapOfKind(kindIdx);
                if (eq < cap) cap = eq;
                if (cap < 0) cap = 0;
                return cap > int.MaxValue ? int.MaxValue : (int)cap;
            }
            catch { return 0; }
        }

        // v4.238: 该地原版志愿兵名额; 没有任何知名人物的地方(部分城堡)视为"不限名额"(只用人口/国库卡)
        internal static int SlotQuotaOf(Settlement s)
        {
            try
            {
                if (s == null) return 0;
                var nt = s.Notables;
                if (nt == null || nt.Count == 0) return int.MaxValue;
                return LordArmy.SlotLeft(s);
            }
            catch { return int.MaxValue; }
        }

        // 上限为 0 时的具体缺项(给 UI 显示, 避免只说"不可增补")
        internal static string LegionRecruitBlocker(MobileParty p, Settlement s, int kindIdx)
        {
            try
            {
                if (LegionOf(p) == null) return "不是国防军野战军团";
                if (s == null) return "未选征兵地";
                var u = Equipment.Unit(kindIdx);
                if (u == null) return "未知兵种";
                if (string.IsNullOrEmpty(Armory.NationalOwner(OurKingdom))) return "尚未建立国家军械库(先建国/定都)";
                int cur = RegularsOf(p);
                int cmdRoom;
                try { cmdRoom = ArmyDoctrine.CommandLimitOf(p) - cur; } catch { cmdRoom = MaxLegionMen - cur; }
                if (cmdRoom <= 0) return "编制无余额(现有 " + cur + ")";
                int pop = RealCommonersOf(s);
                if (pop <= 0) return (s.Name != null ? s.Name.ToString() : s.StringId) + " 没有可征平民(换城镇/村庄或先募兵)";
                int slots = SlotQuotaOf(s);
                if (slots <= 0) return (s.Name != null ? s.Name.ToString() : s.StringId) + " 征兵名额已用完(等原版刷新或换地方)";
                if (slots < cmdRoom) return (s.Name != null ? s.Name.ToString() : s.StringId) + " 征兵名额只剩 " + slots + " 人";
                if (pop < cmdRoom) return (s.Name != null ? s.Name.ToString() : s.StringId) + " 可征人口只有 " + pop + " 人";
                double per = RecruitCost + LegionTrainCostPerMan;
                try { if (Politics.SeatHeld(3)) per = per * 0.9; } catch { }
                if (per < 1.0) per = 1.0;
                long gold = 0;
                try { gold = EconomyWorld.Treasury.Gold; } catch { }
                if ((long)Math.Floor(gold / per + 0.000001) < cmdRoom) return "国库不足(每兵 " + ((int)Math.Round(per)) + " 金)";
                if (EquipCapOfKind(kindIdx) <= 0) return "国家军械库缺少该兵种所需装备(先工厂下单/临时补给)";
                return "当前不可增补";
            }
            catch { return "上限为 0"; }
        }

        // 国家军械库按该兵种需求可装的最高人数(逐需求标签取现货型号, 与 UnitEquipPlan 同选型)
        private static long EquipCapOfKind(int kindIdx)        {
            try
            {
                var u = Equipment.Unit(kindIdx);
                if (u == null) return 0;
                if (u.Req == null || u.Req.Length == 0) return long.MaxValue;
                string key = Armory.NationalOwner(OurKingdom);
                if (string.IsNullOrEmpty(key)) return 0;
                long cap = long.MaxValue;
                for (int r = 0; r < u.Req.Length; r++)
                {
                    var req = u.Req[r];
                    if (req.Per100 <= 0) continue;
                    string id = BestStockFor(key, req.Tag);
                    int have = string.IsNullOrEmpty(id) ? 0 : Armory.CountKey(key, id);
                    long c = have * 100L / req.Per100;
                    if (c < cap) cap = c;
                }
                return cap;
            }
            catch { return 0; }
        }

        // 扩充第二来源: 在选定征兵地招募新兵直接补入军团
        //   人口 1:1(实抽) + 国库招募/训练费(RecruitCost + LegionTrainCostPerMan) + 国家军械库按兵种需求领装
        //   逐人写入 Soldiers(熟练 20~30), 装备扣国家库并记入军团装备快照(回写满足率)
        internal static string ReplenishLegionFrom(MobileParty p, Settlement s, int kindIdx, int n)
        {
            try
            {
                var lg = LegionOf(p);
                if (lg == null) return "只能给国防军野战军团补员";
                if (s == null) return "请先选择征兵地";
                if (n <= 0) return "人数必须为正";
                var u = Equipment.Unit(kindIdx);
                if (u == null) return "未知兵种";

                int cur = RegularsOf(p);
                int cmdRoom;
                try { cmdRoom = ArmyDoctrine.CommandLimitOf(p) - cur; } catch { cmdRoom = MaxLegionMen - cur; }
                if (cmdRoom <= 0) return "编制已满(现有 " + cur + ")";
                if (n > cmdRoom) n = cmdRoom;

                // v4.234: 与部队管理页滑条同口径(人口/国库/装备); 招不到就直接说明缺哪一项
                int useCap = LegionRecruitCap(p, s, kindIdx);
                if (useCap <= 0)
                    return "无法在" + (s.Name != null ? s.Name.ToString() : s.StringId) + "征兵: " + LegionRecruitBlocker(p, s, kindIdx);
                if (n > useCap) n = useCap;

                int pop = RealCommonersOf(s);
                if (pop < n)
                    return "可征人口不足: " + (s.Name != null ? s.Name.ToString() : s.StringId) + " 只有 " + pop + " 人(计划 " + n + ")";

                int per = RecruitCost + LegionTrainCostPerMan;
                bool discount = false;
                try { discount = Politics.SeatHeld(3); } catch { }
                int cost = (int)Math.Round(n * per * (discount ? 0.9f : 1f));
                if (EconomyWorld.Treasury.Gold < cost)
                    return "国库不足: 需要 " + cost.ToString("N0") + " 第纳尔(现有 " + EconomyWorld.Treasury.Gold.ToString("N0") + ")";

                Dictionary<string, int> plan;
                string err = UnitEquipPlan(kindIdx, n, out plan);
                if (!string.IsNullOrEmpty(err)) return err;

                int moved = TakeCommoners(s, n);
                if (moved <= 0) return "可征人口不足";
                if (moved < n)
                {
                    n = moved;
                    err = UnitEquipPlan(kindIdx, n, out plan);
                    if (!string.IsNullOrEmpty(err)) return err;
                }

                string key = Armory.NationalOwner(OurKingdom);
                var taken = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (var kv in plan)
                {
                    int got = Armory.TakeKey(key, kv.Key, kv.Value);
                    if (got > 0) taken[kv.Key] = got;
                }

                try { Soldiers.Ensure(p); } catch { }
                for (int i = 0; i < n; i++)
                    Soldiers.Add(p, u.Id, 1, MBRandom.RandomInt(20, 31));
                AddKindToRoster(p, s.Culture, kindIdx, n);
                lg.Comp = null;   // 编成按实际名单重算(供装备需求/满足率)

                try
                {
                    var snap = SnapshotOf(lg, true);
                    if (snap != null)
                        foreach (var kv in taken)
                        {
                            int old;
                            snap.TryGetValue(kv.Key, out old);
                            snap[kv.Key] = old + kv.Value;
                        }
                    RefreshFill(lg);
                }
                catch { }

                Spend(cost);
                Fiscal.AddMilitary(cost);
                try { LordArmy.ConsumeSlots(s, n); } catch { }   // v4.238: 吃掉原版志愿兵名额(与领主招募共用同一池)
                lg.Expected = RegularsOf(p);

                int gotT = 0;
                foreach (var kv in taken) gotT += kv.Value;
                string msg = "征兵补员 " + n + " 人(" + u.Name + ") → " + MapSelection.NameOf(p)
                    + " · 花费 " + cost.ToString("N0") + " 第纳尔 · 军械库领装 " + gotT + " 件";
                DLog.Force("国防军: " + msg);
                return msg;
            }
            catch (Exception ex) { DLog.Force("征兵补员失败: " + ex.Message); return "征兵补员失败, 见日志"; }
        }

        // 按兵种主线把新兵加进原版名单(步/弓/骑 T2)
        private static void AddKindToRoster(MobileParty p, CultureObject culture, int kindIdx, int n)
        {
            try
            {
                if (p == null || p.MemberRoster == null || n <= 0) return;
                var t = TroopsOf(culture);
                if (t == null) return;
                int branch;
                switch (kindIdx)
                {
                    case Equipment.UDragoon:
                    case Equipment.UHussar:
                    case Equipment.UCuirassier:
                    case Equipment.UHorseArcher:
                        branch = 2; break;
                    case Equipment.UArcher:
                    case Equipment.UCrossbow:
                        branch = 1; break;
                    default:
                        branch = 0; break;
                }
                var ch = t[branch];
                if (ch == null) ch = t[0];
                if (ch == null) return;
                p.MemberRoster.AddToCounts(ch, n, false, 0, 0, true, -1);
            }
            catch { }
        }

        // 征兵领装清点: 按 Equipment.Unit(kind).Req 逐需求从国家军械库现货挑型号; 返回 "" 或缺口提示
        private static string UnitEquipPlan(int kindIdx, int n, out Dictionary<string, int> plan)
        {
            plan = new Dictionary<string, int>(StringComparer.Ordinal);
            try
            {
                var u = Equipment.Unit(kindIdx);
                if (u == null) return "未知兵种";
                if (u.Req == null || u.Req.Length == 0 || n <= 0) return "";
                string key = Armory.NationalOwner(OurKingdom);
                if (string.IsNullOrEmpty(key)) return "尚未建立国家军械库(先建国/定都)";
                for (int r = 0; r < u.Req.Length; r++)
                {
                    var req = u.Req[r];
                    int qty = (int)Math.Ceiling(req.Per100 * (double)n / 100.0);
                    if (qty <= 0) continue;
                    string id = BestStockFor(key, req.Tag);
                    if (string.IsNullOrEmpty(id))
                    {
                        var pick = Equipment.PickForTag(req.Tag, 6);
                        if (pick != null) id = pick.Id;
                    }
                    if (string.IsNullOrEmpty(id)) return "国家军械库缺少该兵种所需装备(先工厂下单/临时补给)";
                    int old;
                    plan.TryGetValue(id, out old);
                    plan[id] = old + qty;
                }
                foreach (var kv in plan)
                {
                    int have = Armory.CountKey(key, kv.Key);
                    if (have < kv.Value)
                    {
                        var def = Equipment.Get(kv.Key);
                        string nm = def != null ? def.Name : kv.Key;
                        return "国家军械库装备不足: " + nm + " 缺 " + (kv.Value - have) + " 件";
                    }
                }
                return "";
            }
            catch (Exception ex) { return "装备清点异常: " + ex.Message; }
        }

        // v5.x AI: 军团撤到指定本国城休整补员(与 ReplenishLegion 同口径, 但可用任意本国城守备营)
        internal static string ReplenishLegionAt(MobileParty p, Settlement city, int want)
        {
            try
            {
                var lg = LegionOf(p);
                if (lg == null || city == null) return "只能给国防军野战军团补员";
                var garrison = GarrisonPartyOf(city);
                if (garrison == null || city.Culture == null) return "该城没有守备营";
                int cur = RegularsOf(p);
                int room;
                try { room = ArmyDoctrine.CommandLimitOf(p) - cur; } catch { room = MaxLegionMen - cur; }   // v4.256: AI 补员同口径(原来也用 MaxLegionMen)
                if (room <= 0) return "军团已满员";
                int avail = GarrisonMenOf(city.StringId);
                if (avail <= 0) return "该城守备营没有可调兵员";
                int n = Math.Min(Math.Min(want, room), avail);
                if (n <= 0) return "没有可补充的兵员";
                int moved = MoveDefTroops(garrison.MemberRoster, p.MemberRoster, city.Culture, n);
                var rec = GetGarrisonRec(city.StringId, true);
                if (rec != null) rec.Placed = Math.Max(0, rec.Placed - moved);
                lg.Expected = RegularsOf(p);
                DLog.Info("国防军(AI): 补员 " + moved + " 人 -> " + MapSelection.NameOf(p) + "(编制 " + RegularsOf(p) + ")");
                return "补员 " + moved + " 人";
            }
            catch (Exception ex) { DLog.Force("AI 补员失败: " + ex.Message); return "补员失败"; }
        }

        // 全军回防: 所有军团返回各自驻地
        internal static string AllToHome()
        {
            try
            {
                int n = 0;
                for (int i = 0; i < Legions.Count; i++)
                {
                    var p = LegionParty(Legions[i]);
                    if (p == null || !p.IsActive) continue;
                    SetTask(p, 0);
                    n++;
                }
                string msg = n > 0 ? ("已命令 " + n + " 支军团返回驻地") : "当前没有野战军团";
                DLog.Force("国防军: " + msg);
                return msg;
            }
            catch (Exception ex) { DLog.Force("全军回防失败: " + ex.Message); return "全军回防失败"; }
        }

        // 最近一批征兵的到期天数(-1 = 无)
        internal static int NearestConscriptExpiry()
        {
            try
            {
                int day = (int)CampaignTime.Now.ToDays;
                int best = -1;
                for (int i = 0; i < Conscripts.Count; i++)
                {
                    var c = Conscripts[i];
                    if (c == null || c.Standing || c.ExpireDay < 0) continue;
                    int left = c.ExpireDay - day;
                    if (left < 0) left = 0;
                    if (best < 0 || left < best) best = left;
                }
                return best;
            }
            catch { return -1; }
        }

        internal static string MoraleText(MobileParty p)
        {
            try { return p != null ? ((int)p.Morale).ToString() : "—"; } catch { return "—"; }
        }

        // ==================== 军团装备(设计 27.1/27.2/27.4) ====================
        // 编制读取器(旧档无 Comp 时按实际名单折算: 步/弓/骑/骑射)
        internal static int[] CompOf(DefLegion lg)
        {
            var r = new int[4];
            try
            {
                if (lg == null) return r;
                if (lg.Comp == null) lg.Comp = new int[4];
                int sum = 0;
                for (int i = 0; i < 4; i++)
                {
                    if (lg.Comp[i] < 0) lg.Comp[i] = 0;
                    sum += lg.Comp[i];
                }
                if (sum <= 0)
                {
                    var p = LegionParty(lg);
                    if (p != null)
                    {
                        // v4.236: 兜底也走"我们的 14 兵种逐人表 + 主武器分支", 不读原版旗标
                        try
                        {
                            Dictionary<string, int> men;
                            Dictionary<string, float> profs;
                            Soldiers.Aggregate(p, out men, out profs);
                            foreach (var kv in men)
                            {
                                if (kv.Value <= 0) continue;
                                int b = Equipment.BranchOf(Soldiers.UnitIndexOf(kv.Key));
                                lg.Comp[b] += kv.Value;
                            }
                        }
                        catch { }
                    }
                }
                for (int i = 0; i < 4; i++) r[i] = lg.Comp[i];
            }
            catch { }
            return r;
        }

        // 装备档: 0 = 旧档(27.12: 有火器科技 -> 线列最低档 3, 无 -> 民兵档 1)
        internal static int TierOf(DefLegion lg)
        {
            try
            {
                if (lg != null && lg.EquipTier >= 1 && lg.EquipTier <= 6) return lg.EquipTier;
            }
            catch { }
            return HasFirearmTech() ? 3 : 1;
        }

        // v4.241: 只保留科技表里真实存在的火器类科技(原来还塞了 6 个中文名, 永远匹配不上)
        private static readonly string[] FirearmTechIds = { "line_infantry", "military_drill", "gunsmithing", "percussion_cap", "rifling", "repeaters", "bolt_action", "breech_artillery", "handcranked_mg", "auto_mg" };

        private static bool HasFirearmTech()
        {
            try { for (int i = 0; i < FirearmTechIds.Length; i++) if (Research.IsDone(FirearmTechIds[i])) return true; }
            catch { }
            return false;
        }

        // 训练等级(27.2 兵种默认按编制加权; lg.Training >= 0 时用显式值)
        internal static float TrainingOf(DefLegion lg)
        {
            try
            {
                if (lg != null && lg.Training >= 0f) return lg.Training;
                var comp = CompOf(lg);
                int tier = TierOf(lg);
                float sum = 0f;
                int men = 0;
                for (int b = 0; b < 4; b++)
                {
                    if (comp[b] <= 0) continue;
                    var u = Equipment.Unit(Equipment.UnitOfBranch(b, tier));
                    if (u == null) continue;
                    sum += u.Training * comp[b];
                    men += comp[b];
                }
                return men > 0 ? sum / men : 1f;
            }
            catch { return 1f; }
        }

        // 满足率(27.4): min(1, 可用(已装备) ÷ 需求); 旧档默认 100%(27.15)
        internal static float EquipFillOf(DefLegion lg)
        {
            try
            {
                if (lg == null) return 1f;
                float f = lg.EquipFill;
                if (f < 0f) return 1f;
                if (f > 1f) f = 1f;
                return f;
            }
            catch { return 1f; }
        }

        // 缺装乘子(27.4): 0.7 + 0.3 × 满足率, 供战斗系统调用
        internal static float EquipMultOf(MobileParty p)
        {
            try
            {
                var lg = LegionOf(p);
                if (lg == null) return 1f;   // 27.15: 原版部队视为满足率 100%
                return 0.7f + 0.3f * EquipFillOf(lg);
            }
            catch { return 1f; }
        }

        // v4.236: 装备需求改为按"逐人表的实际 14 兵种"精确算(原来按 4 分支 + 装备档折算,
        //   会把火器步兵算成弓/弩型号); 没有逐人表(旧档) 时回退老口径
        internal static Dictionary<string, int> NeedOf(DefLegion lg)
        {
            var need = new Dictionary<string, int>(StringComparer.Ordinal);
            try
            {
                if (lg == null) return need;
                var p = LegionParty(lg);
                var units = new Dictionary<int, int>();
                if (p != null)
                {
                    try
                    {
                        Dictionary<string, int> men;
                        Dictionary<string, float> profs;
                        Soldiers.Aggregate(p, out men, out profs);
                        foreach (var kv in men)
                        {
                            if (kv.Value <= 0) continue;
                            int idx = Soldiers.UnitIndexOf(kv.Key);
                            if (idx < 0) continue;
                            int old;
                            units.TryGetValue(idx, out old);
                            units[idx] = old + kv.Value;
                        }
                    }
                    catch { }
                }
                if (units.Count == 0)
                {
                    need = Equipment.NeedOf(CompOf(lg), TierOf(lg), lg.Supports);
                    AddMachineGunNeed(need, lg);
                    AddHelmetNeed(need, lg);
                    AddTechGearNeed(need, lg);     // v4.266: 化学战 -> 毒气炮弹
                    return need;
                }

                int tier = TierOf(lg);
                // v4.266: 军事建筑"马厩"(@cavalry 10/15/23 每级) -> 骑兵补充: 驻地马匹需求按比例下调
                //   (需求降 -> 满足率与补装压力同步下降 -> 骑兵/龙骑兵/骑炮兵能真正拉起来)
                float cavSupply = 0f;
                try { cavSupply = MilBuildings.CavalrySupplyOf(MilBuildings.HomeOf(LegionParty(lg))); } catch { }
                foreach (var kv in units)
                {
                    var u = Equipment.Unit(kv.Key);
                    if (u == null || u.Req == null) continue;
                    for (int r = 0; r < u.Req.Length; r++)
                    {
                        var pick = Equipment.PickForTag(u.Req[r].Tag, tier);
                        if (pick == null) continue;
                        int q = (int)Math.Ceiling(u.Req[r].Per100 * (double)kv.Value / 100.0);
                        if (q <= 0) continue;
                        if (cavSupply > 0f && (u.Req[r].Tag == EquipTag.AnyHorse || u.Req[r].Tag == EquipTag.HeavyHorse))
                        {
                            q = (int)Math.Ceiling(q * (1.0 - cavSupply));
                            if (q <= 0) q = 1;
                        }
                        int old;
                        need.TryGetValue(pick.Id, out old);
                        need[pick.Id] = old + q;
                    }
                }
                if (lg.Supports != null)
                {
                    for (int s = 0; s < 3 && s < lg.Supports.Length; s++)
                    {
                        if (!lg.Supports[s]) continue;
                        string id = Equipment.SupportIds[s];
                        if (string.IsNullOrEmpty(id)) continue;
                        int old;
                        need.TryGetValue(id, out old);
                        need[id] = old + 1;
                    }
                }
                AddMachineGunNeed(need, lg);   // v4.241: 机枪 1 挺/百人(27.2 炮数口径)
                AddHelmetNeed(need, lg);       // v4.241: 钢盔(附加件, 不计入满足率)
                AddTechGearNeed(need, lg);     // v4.266: 化学战 -> 毒气炮弹(每 200 人 1 发)
            }
            catch { }
            return need;
        }

        // v4.241 机枪编制(27.2 "机枪 1 挺/百人, 占 5 人力, 计入远程阶段, 防御时守方 ×1.3"):
        //   加特林/马克沁此前没有任何兵种需求, 研究完手摇机枪/自动机枪等于拿到两件死装备;
        //   现按每 100 人 1 挺挂到军团装备需求上(每挺另需 1 套弹药组), 满足率/缺口/补装/工厂订单全部自动跟上
        private static void AddMachineGunNeed(Dictionary<string, int> need, DefLegion lg)
        {
            try
            {
                if (need == null || lg == null) return;
                var pick = Equipment.PickForTag(EquipTag.MachineGun, 6);
                if (pick == null) return;                      // 未解锁手摇机枪/自动机枪 -> 不产生需求
                var p = LegionParty(lg);
                int men = p != null ? RegularsOf(p) : 0;
                if (men <= 0) return;
                int guns = (int)Math.Ceiling(men / 100.0);
                if (guns <= 0) return;
                int old;
                need.TryGetValue(pick.Id, out old);
                need[pick.Id] = old + guns;
                string ammo = "ammo_kit";
                need.TryGetValue(ammo, out old);
                need[ammo] = old + guns;                       // 弹药组: 机枪 1 套/挺
            }
            catch { }
        }

        // v4.266: 科技专属器材需求(照 AddMachineGunNeed 口径) —— 化学战"毒气炮弹"(gas_shell):
        //   解锁后每 200 人配 1 发, 自动进入军团装备需求/满足率/工厂订单/装备快照;
        //   炮击阶段取快照里最强一门炮(CannonStatsOf); v4.268 起毒气炮弹 Dmg 205 < 后装线膛炮 210,
        //   所以它不会抬高炮击伤害 —— 化学战的价值是"稀缺特种弹需求 + 战术进攻 +3%"(见 Research.TacticOffense)。
        //   该型号只带 HeavyGun 标签且排在既有型号之后, 不会顶掉野战/攻城炮兵的选型(PickForTag 同级取先出现的)。
        private static void AddTechGearNeed(Dictionary<string, int> need, DefLegion lg)
        {
            try
            {
                if (need == null || lg == null) return;
                if (!Research.Mil(OurKingdom, "chemical_warfare")) return;
                var p = LegionParty(lg);
                int men = p != null ? RegularsOf(p) : 0;
                if (men <= 0) return;
                int shells = (int)Math.Ceiling(men / 200.0);
                if (shells <= 0) return;
                int old;
                need.TryGetValue("gas_shell", out old);
                need["gas_shell"] = old + shells;
            }
            catch { }
        }

        // 军团实际带了多少挺机枪(读装备快照)
        internal static int MachineGunsOf(MobileParty p)
        {
            try
            {
                var lg = LegionOf(p);
                if (lg == null) return 0;
                var snap = SnapshotOf(lg, false);
                if (snap == null) return 0;
                int n = 0;
                foreach (var kv in snap)
                {
                    if (kv.Value <= 0) continue;
                    var def = Equipment.Get(kv.Key);
                    if (def != null && Equipment.HasTag(def, EquipTag.MachineGun)) n += kv.Value;
                }
                return n;
            }
            catch { return 0; }
        }

        // ==================== v4.241: 兵种特性 / 火炮 / 护甲 接进战斗 ====================
        // 逐人表的兵种人数(没有逐人表时返回空)
        private static Dictionary<int, int> UnitMenOf(MobileParty p)
        {
            var r = new Dictionary<int, int>();
            try
            {
                if (p == null) return r;
                // v4.267: 玩家方(玩家家族部队/国防军)走逐人表; AI 部队不再建表, 改为按原版兵种折算的
                //   轻量口径(不缓存跨调用)。两者在"首次折算"时结果一致, 所以 AI 的兵种占比/战斗力不掉。
                if (!Soldiers.IsPlayerSideParty(p))
                {
                    var lm = Soldiers.LightUnitMen(p, null);
                    if (lm == null) return r;
                    foreach (var kv in lm)
                    {
                        if (kv.Value <= 0) continue;
                        int old;
                        r.TryGetValue(kv.Key, out old);
                        r[kv.Key] = old + kv.Value;
                    }
                    return r;
                }
                Dictionary<string, int> men;
                Dictionary<string, float> profs;
                Soldiers.Aggregate(p, out men, out profs);
                if (men == null) return r;
                foreach (var kv in men)
                {
                    if (kv.Value <= 0) continue;
                    int idx = Soldiers.UnitIndexOf(kv.Key);
                    if (idx < 0) continue;
                    int old;
                    r.TryGetValue(idx, out old);
                    r[idx] = old + kv.Value;
                }
            }
            catch { }
            return r;
        }

        // v4.267: 战斗快照用重载 —— 传入 call-local 缓存, 把同一场战斗里 N 次 ShareOf 的重复折算摊掉
        //   (AI 部队每次 ShareOf 否则都要重新按名册折算一遍)
        internal static Dictionary<int, int> UnitMenOfCached(MobileParty p, Dictionary<string, Dictionary<int, int>> cache)
        {
            var r = new Dictionary<int, int>();
            try
            {
                if (p == null) return r;
                if (!Soldiers.IsPlayerSideParty(p))
                {
                    var lm = Soldiers.LightUnitMen(p, cache);
                    if (lm == null) return r;
                    foreach (var kv in lm)
                    {
                        if (kv.Value <= 0) continue;
                        int old;
                        r.TryGetValue(kv.Key, out old);
                        r[kv.Key] = old + kv.Value;
                    }
                    return r;
                }
                return UnitMenOf(p);
            }
            catch { return r; }
        }

        // 某兵种在军团里的占比(0~1); 兵种特性按占比折算, 避免"塞 1 个掷弹兵全军团 +20%"
        internal static float ShareOf(MobileParty p, int unitIdx)
        {
            try
            {
                var m = UnitMenOf(p);
                if (m.Count == 0) return 0f;
                int total = 0, mine = 0;
                foreach (var kv in m)
                {
                    total += kv.Value;
                    if (kv.Key == unitIdx) mine += kv.Value;
                }
                return total > 0 ? mine / (float)total : 0f;
            }
            catch { return 0f; }
        }

        // v4.267: 战斗快照专用(带缓存); 与 ShareOf 同口径
        internal static float ShareOfCached(MobileParty p, int unitIdx, Dictionary<string, Dictionary<int, int>> cache)
        {
            try
            {
                var m = UnitMenOfCached(p, cache);
                if (m.Count == 0) return 0f;
                int total = 0, mine = 0;
                foreach (var kv in m)
                {
                    total += kv.Value;
                    if (kv.Key == unitIdx) mine += kv.Value;
                }
                return total > 0 ? mine / (float)total : 0f;
            }
            catch { return 0f; }
        }

        // 参战炮数(27.2 特性: 野战炮兵 4 门/百人 · 攻城炮兵 2 · 骑炮兵 3)
        //   DefArmyStats.CannonsOf 一直在反射找这个方法, 但从来没有实现过 -> 参战炮数恒为 0, 整个炮击阶段空转
        internal static int CannonsOf(MobileParty p)
        {
            try
            {
                if (LegionOf(p) == null) return 0;
                int n = 0;
                foreach (var kv in UnitMenOf(p))
                {
                    int per = CannonsPer100(kv.Key);
                    if (per <= 0) continue;
                    n += (int)Math.Ceiling(per * (double)kv.Value / 100.0);
                }
                return n;
            }
            catch { return 0; }
        }

        private static int CannonsPer100(int unitIdx)
        {
            switch (unitIdx)
            {
                case Equipment.UFieldArtillery: return 4;
                case Equipment.USiegeArtillery: return 2;
                case Equipment.UHorseArtillery: return 3;
            }
            return 0;
        }

        // 军团火炮型号(伤害/破甲取快照里最强的一门); 没列装则回退前装 6 磅(90/60)
        internal static void CannonStatsOf(MobileParty p, out float dmg, out float pen)
        {
            dmg = 90f; pen = 60f;
            try
            {
                var lg = LegionOf(p);
                if (lg == null) return;
                var snap = SnapshotOf(lg, false);
                if (snap == null) return;
                foreach (var kv in snap)
                {
                    if (kv.Value <= 0) continue;
                    var def = Equipment.Get(kv.Key);
                    if (def == null || def.Cat != EquipCat.Artillery) continue;
                    if (Equipment.HasTag(def, EquipTag.MachineGun)) continue;   // 机枪不算炮
                    if (def.Dmg > dmg) dmg = def.Dmg;
                    if (def.Pen > pen) pen = def.Pen;
                }
                // v4.266: 化学战 -> 毒气炮弹。军械库还没入库该型号时也按战术条令口径计,
                //   保证"研究完立刻看得见"(炮击阶段的炮伤害/战报), 入库后由快照同一口径接管。
                //   v4.268: 230 -> 205(低于后装线膛炮 210): 特种弹只提供稀缺需求 + 战术进攻 +3%, 不替代主炮。
                if (Research.Mil(OurKingdom, "chemical_warfare"))
                {
                    if (dmg < 205f) dmg = 205f;
                    if (pen < 125f) pen = 125f;
                }
            }
            catch { }
        }

        // 军团实际护甲 = 甲胄(皮甲 10/锁子甲 25/胸甲 40/半身板甲 55, 按覆盖人数加权) + 钢盔(附加件 20)
        //   返回 <0 = 非我军编制(调用方走原版按兵种等级估算)
        internal static float ArmorOf(MobileParty p)
        {
            try
            {
                if (LegionOf(p) == null) return -1f;
                var lg = LegionOf(p);
                var snap = SnapshotOf(lg, false);
                if (snap == null) return 0f;
                int men = RegularsOf(p);
                if (men <= 0) return 0f;
                float armor = 0f, helmet = 0f;
                bool any = false;
                foreach (var kv in snap)
                {
                    if (kv.Value <= 0) continue;
                    var def = Equipment.Get(kv.Key);
                    if (def == null || def.Cat != EquipCat.Armor || def.Prot <= 0) continue;
                    any = true;
                    float cover = Math.Min(1f, kv.Value / (float)men);
                    if (def.ArmorClass == ArmorClass.Addon) helmet += def.Prot * cover;
                    else if (def.Prot * cover > armor) armor = def.Prot * cover;   // 取最好的一件甲
                }
                if (!any) return -1f;   // 快照里没有甲(旧档) -> 退回按兵种等级估算
                return armor + helmet;
            }
            catch { return 0f; }
        }

        // 军团是否真带着某支援器材(0 工具 / 1 野战医院 / 2 电报机)
        internal static bool HasSupport(MobileParty p, int slot)
        {
            try
            {
                var lg = LegionOf(p);
                if (lg == null || lg.Supports == null || slot < 0 || slot >= lg.Supports.Length) return false;
                if (!lg.Supports[slot]) return false;
                var snap = SnapshotOf(lg, false);
                if (snap == null) return false;
                string id = Equipment.SupportIds != null && slot < Equipment.SupportIds.Length ? Equipment.SupportIds[slot] : null;
                if (string.IsNullOrEmpty(id)) return false;
                int have;
                return snap.TryGetValue(id, out have) && have > 0;
            }
            catch { return false; }
        }

        // 钢盔(附加件): 解锁后按 1 件/人配发, 但不计入满足率分母(缺了只是少一层防护, 不算缺装)
        private static void AddHelmetNeed(Dictionary<string, int> need, DefLegion lg)
        {
            try
            {
                if (need == null || lg == null) return;
                var pick = Equipment.PickForTag(EquipTag.Helmet, 6);
                if (pick == null) return;                      // 未解锁(战壕工事) -> 不产生需求
                var p = LegionParty(lg);
                int men = p != null ? RegularsOf(p) : 0;
                if (men <= 0) return;
                int old;
                need.TryGetValue(pick.Id, out old);
                need[pick.Id] = old + men;
            }
            catch { }
        }

        private static Dictionary<string, int> SnapshotOf(DefLegion lg, bool create)
        {
            try
            {
                if (lg == null || string.IsNullOrEmpty(lg.PartyId)) return null;
                Dictionary<string, int> snap;
                if (LegionEquip.TryGetValue(lg.PartyId, out snap) && snap != null) return snap;
                if (!create) return null;
                snap = new Dictionary<string, int>();
                LegionEquip[lg.PartyId] = snap;
                return snap;
            }
            catch { return null; }
        }

        // 满足率更新: 已装备量 / 需求
        private static void RefreshFill(DefLegion lg)
        {
            try
            {
                if (lg == null) return;
                var need = NeedOf(lg);
                if (need.Count == 0) { lg.EquipFill = 1f; return; }
                var snap = SnapshotOf(lg, false);
                long needT = 0, haveT = 0;
                foreach (var kv in need)
                {
                    if (Equipment.IsOptional(kv.Key)) continue;   // v4.241: 附加件(钢盔)不进满足率分母
                    needT += kv.Value;
                    int have = 0;
                    if (snap != null) snap.TryGetValue(kv.Key, out have);
                    if (have > kv.Value) have = kv.Value;
                    if (have > 0) haveT += have;
                }
                lg.EquipFill = needT <= 0 ? 1f : (float)Math.Min(1.0, haveT / (double)needT);
            }
            catch { }
        }

        // 补装: 从国家军械库领料到 target × 需求(建军团 1.0 / 月补 1.01)
        private static void EquipLegion(DefLegion lg, float target)
        {
            try
            {
                if (lg == null) return;
                var need = NeedOf(lg);
                var snap = SnapshotOf(lg, need.Count > 0);
                if (snap != null)
                {
                    foreach (var kv in need)
                    {
                        int want = (int)Math.Ceiling(kv.Value * (double)target);
                        int have;
                        snap.TryGetValue(kv.Key, out have);
                        if (have < want)
                        {
                            int got = MilArmory.Take(kv.Key, want - have);
                            have += got;
                        }
                        if (have > 0) snap[kv.Key] = have;
                    }
                }
                RefreshFill(lg);
            }
            catch { }
        }

        private static void WearEquip(DefLegion lg, float ratio)
        {
            try
            {
                if (lg == null || ratio <= 0f) return;
                var snap = SnapshotOf(lg, false);
                if (snap == null || snap.Count == 0) return;
                var keys = new List<string>(snap.Keys);
                for (int i = 0; i < keys.Count; i++)
                {
                    int c;
                    if (!snap.TryGetValue(keys[i], out c)) continue;
                    int worn = (int)Math.Round(c * (double)ratio);
                    if (worn < 1 && c >= 50) worn = 1;   // 50+ 件的小堆也计 1% 损耗
                    if (worn <= 0) continue;
                    c -= worn;
                    if (c <= 0) snap.Remove(keys[i]);
                    else snap[keys[i]] = c;
                }
            }
            catch { }
        }

        // 战损扣装备(27.4: 按伤亡比例)
        private static void WearEquipByLoss(DefLegion lg, int loss, int expected)
        {
            try
            {
                if (lg == null || loss <= 0 || expected <= 0) return;
                float ratio = Math.Min(1f, loss / (float)expected);
                WearEquip(lg, ratio);
                RefreshFill(lg);
            }
            catch { }
        }

        // 解散返还 100%(27.4)
        private static void ReturnEquip(DefLegion lg)
        {
            try
            {
                if (lg == null || string.IsNullOrEmpty(lg.PartyId)) return;
                Dictionary<string, int> snap;
                if (LegionEquip.TryGetValue(lg.PartyId, out snap) && snap != null)
                    foreach (var kv in snap)
                        if (kv.Value > 0) MilArmory.Add(kv.Key, kv.Value);
                LegionEquip.Remove(lg.PartyId);
            }
            catch { }
        }

        private static void MergeEquip(DefLegion main, DefLegion src)
        {
            try
            {
                if (main == null || src == null) return;
                var a = SnapshotOf(main, true);
                Dictionary<string, int> b;
                if (a != null && !string.IsNullOrEmpty(src.PartyId) && LegionEquip.TryGetValue(src.PartyId, out b) && b != null)
                {
                    foreach (var kv in b)
                    {
                        int old;
                        a.TryGetValue(kv.Key, out old);
                        a[kv.Key] = old + kv.Value;
                    }
                }
                if (!string.IsNullOrEmpty(src.PartyId)) LegionEquip.Remove(src.PartyId);
                main.Comp = null;   // 合编后按实际名单重算编制
                RefreshFill(main);
            }
            catch { }
        }

        private static void SplitEquip(DefLegion src, DefLegion dst)
        {
            try
            {
                if (src == null || dst == null) return;
                var a = SnapshotOf(src, false);
                var b = SnapshotOf(dst, true);
                if (a != null && b != null)
                {
                    var keys = new List<string>(a.Keys);
                    for (int i = 0; i < keys.Count; i++)
                    {
                        int c;
                        if (!a.TryGetValue(keys[i], out c)) continue;
                        int take = c / 2;
                        if (take <= 0) continue;
                        a[keys[i]] = c - take;
                        int old;
                        b.TryGetValue(keys[i], out old);
                        b[keys[i]] = old + take;
                    }
                }
                src.Comp = null;   // 拆分后按实际名单重算编制
                RefreshFill(src);
                RefreshFill(dst);
            }
            catch { }
        }

        // 前线判定(27.4 补装顺序; 与 AI 口径一致: 离敌城 120 内 / AI 任务)
        private static bool IsFrontline(DefLegion lg)
        {
            try
            {
                if (lg == null) return false;
                if (lg.Task != null && lg.Task.StartsWith("AI", StringComparison.Ordinal)) return true;
                var k = OurKingdom;
                if (k == null || !IsAtWar()) return false;
                var p = LegionParty(lg);
                if (p == null || !p.IsActive) return false;
                foreach (var f in k.FactionsAtWarWith)
                {
                    var ek = f as Kingdom;
                    if (ek == null) continue;
                    foreach (var s in ek.Settlements)
                    {
                        if (s == null) continue;
                        if (p.Position.Distance(s.Position) < 120f) return true;
                    }
                }
            }
            catch { }
            return false;
        }

        private static int CompareResupply(DefLegion a, DefLegion b)
        {
            try
            {
                bool fa = IsFrontline(a), fb = IsFrontline(b);
                if (fa != fb) return fa ? -1 : 1;                       // 前线优先
                int c = EquipFillOf(a).CompareTo(EquipFillOf(b));       // 低满足率优先
                if (c != 0) return c;
                return a.Number.CompareTo(b.Number);
            }
            catch { return 0; }
        }

        // 月结(84 天/年 -> 7 天 = 1 月): 1% 磨损 -> 补装(101% 领料)
        private static void EquipMonth(int day)
        {
            try
            {
                for (int i = 0; i < Legions.Count; i++)
                {
                    var lg = Legions[i];
                    if (lg == null) continue;
                    WearEquip(lg, 0.01f);
                    RefreshFill(lg);
                }
                var order = new List<DefLegion>();
                for (int i = 0; i < Legions.Count; i++) if (Legions[i] != null) order.Add(Legions[i]);
                order.Sort(CompareResupply);
                for (int i = 0; i < order.Count; i++) EquipLegion(order[i], 1.01f);
                if (order.Count > 0)
                    DLog.Force("军械库: 月结 磨损1% 补装 " + order.Count + " 军团, 库存 " + MilArmory.Total() + " 件");
            }
            catch (Exception ex) { DLog.Force("装备月结异常: " + ex.Message); }
        }

        // ==================== 存档 FIA_LegEquip ====================
        internal static string SaveEquip()
        {
            try
            {
                var sb = new StringBuilder("v1;");
                sb.Append(';');   // 库存段落盘见 FIA_Armory(Armory); 本段只存军团装备快照与满足率
                bool first = true;
                for (int i = 0; i < Legions.Count; i++)
                {
                    var lg = Legions[i];
                    if (lg == null || string.IsNullOrEmpty(lg.PartyId)) continue;
                    if (!first) sb.Append('|');
                    first = false;
                    sb.Append(lg.PartyId).Append('=').Append((int)Math.Round(EquipFillOf(lg) * 100f));
                    Dictionary<string, int> snap;
                    if (LegionEquip.TryGetValue(lg.PartyId, out snap) && snap != null)
                        foreach (var kv in snap)
                            if (kv.Value > 0) sb.Append(',').Append(kv.Key).Append(':').Append(kv.Value);
                }
                return sb.ToString();
            }
            catch { return ""; }
        }

        internal static void LoadEquip(string data)
        {
            try
            {
                LegionEquip.Clear();
                MilArmory.Reset();
                if (string.IsNullOrEmpty(data)) { EquipMigrationPending = true; return; }   // 27.15: 旧档迁移(Relink 后执行)
                var seg = data.Split(';');
                if (seg.Length < 2 || seg[0] != "v1") { EquipMigrationPending = true; return; }
                // seg[1] 为历史库存段(现由 Armory/FIA_Armory 管理), 不再读取
                if (seg.Length > 2 && !string.IsNullOrEmpty(seg[2]))
                {
                    foreach (var part in seg[2].Split('|'))
                    {
                        if (string.IsNullOrEmpty(part)) continue;
                        int eq = part.IndexOf('=');
                        if (eq <= 0) continue;
                        string pid = part.Substring(0, eq);
                        var snap = new Dictionary<string, int>();
                        float fill = -1f;
                        var tail = part.Substring(eq + 1).Split(',');
                        for (int i = 0; i < tail.Length; i++)
                        {
                            if (string.IsNullOrEmpty(tail[i])) continue;
                            if (i == 0) { fill = PF(tail[0], -1f) / 100f; continue; }
                            int colon = tail[i].IndexOf(':');
                            if (colon <= 0) continue;
                            string mid = tail[i].Substring(0, colon);
                            int cnt = PI(tail[i].Substring(colon + 1));
                            if (cnt > 0) snap[mid] = cnt;
                        }
                        LegionEquip[pid] = snap;
                        var lg = LegionByPartyId(pid);
                        if (lg != null)
                        {
                            if (lg.EquipFill < 0f && fill >= 0f) lg.EquipFill = fill;
                            RefreshFill(lg);
                        }
                    }
                }
                for (int i = 0; i < Legions.Count; i++)
                {
                    var lg = Legions[i];
                    if (lg == null || string.IsNullOrEmpty(lg.PartyId)) continue;
                    if (!LegionEquip.ContainsKey(lg.PartyId)) EquipMigrationPending = true;
                }
            }
            catch (Exception ex) { DLog.Force("军团装备读档异常: " + ex.Message); }
        }

        private static DefLegion LegionByPartyId(string pid)
        {
            try
            {
                if (string.IsNullOrEmpty(pid)) return null;
                for (int i = 0; i < Legions.Count; i++)
                    if (Legions[i] != null && Legions[i].PartyId == pid) return Legions[i];
            }
            catch { }
            return null;
        }

        // 27.15: 军械库初始量 = 现有军团需求 × 60%; 旧军团视为满足率 100%(满装快照)
        private static void MigrateEquip()
        {
            try
            {
                var total = new Dictionary<string, int>();
                for (int i = 0; i < Legions.Count; i++)
                {
                    var lg = Legions[i];
                    if (lg == null) continue;
                    if (!MigrateLegion(lg)) continue;
                    var need = NeedOf(lg);
                    foreach (var kv in need)
                    {
                        int old;
                        total.TryGetValue(kv.Key, out old);
                        total[kv.Key] = old + kv.Value;
                    }
                }
                foreach (var kv in total)
                {
                    int seed = (int)Math.Floor(kv.Value * 0.6);
                    if (seed > 0) MilArmory.Add(kv.Key, seed);
                }
                if (Legions.Count > 0)
                    DLog.Force("军械库: 旧档迁移 军团=" + Legions.Count + " 折算库存=" + MilArmory.Total());
            }
            catch { }
        }

        // 返回是否本次迁移(新建满装快照)
        private static bool MigrateLegion(DefLegion lg)
        {
            try
            {
                if (lg == null) return false;
                if (lg.Comp == null) lg.Comp = new int[4];
                if (lg.Supports == null) lg.Supports = new bool[3];
                if (lg.EquipFill < 0f) lg.EquipFill = 1f;   // 27.15: 满足率 100%
                if (!string.IsNullOrEmpty(lg.PartyId) && LegionEquip.ContainsKey(lg.PartyId)) return false;
                var need = NeedOf(lg);
                var snap = SnapshotOf(lg, need.Count > 0);
                if (snap != null)
                    foreach (var kv in need) snap[kv.Key] = kv.Value;   // 旧档视为满装
                RefreshFill(lg);
                return true;
            }
            catch { return false; }
        }

        // ==================== 28.2 建档转化 ====================
        internal static bool Converted;   // FIA_Converted 标记(防重复; 由 SoldiersBehavior 落盘)

        // 新战役首次进入地图执行一次; 旧档缺标记宽容补跑一次(28.12)
        // TEMP 测试: 给玩家国家与家族塞满全套装备(40 型号 x 200 件; v4.266 起目录含毒气炮弹), 上线前删
        internal static void DebugFillPlayer()
        {
            try
            {
                if (Clan.PlayerClan == null || Clan.PlayerClan.Kingdom == null) return;
                string kk = Armory.NationalOwner(Clan.PlayerClan.Kingdom);
                string ll = "L:" + (Clan.PlayerClan.StringId ?? "");
                for (int i = 0; i < Equipment.All.Count; i++)
                {
                    var e = Equipment.All[i];
                    if (e == null || string.IsNullOrEmpty(e.Id)) continue;
                    // v4.252: 原来每型号塞 10 万件(共 780 万件) —— 会让"军需储备"算成 40 万%(军需体系失效),
                    //   现在改成够用不注水的量(每型号 200 件), 只是让玩家开局有装备可用
                    Armory.AddKey(kk, e.Id, 200);
                    Armory.AddKey(ll, e.Id, 200);
                }
                DLog.Force("TEMP: 玩家国家与家族已获全套装备各 200 件");
            }
            catch (Exception ex) { DLog.Force("TEMP 装备失败: " + ex.Message); }
        }

        private static bool TempFilled;   // TEMP: 玩家王国确定后只补给一次

        // TEMP: 玩家王国确定后执行一次(选国接管完成/日结时调用; 未确定则跳过, 等下次)
        internal static void TempFillIfReady()
        {
            try
            {
                if (TempFilled) return;
                // v4.256: 只在**开局**发放一次 —— TempFilled 是进程内静态标记, 退出重进就清零,
                //   于是每次读档又 +1 亿(日志实证: 同一存档三次读档 100,006,934 -> 100,011,798 -> 199,984,436)。
                //   判据用"国库里还没有这笔钱": 已有钱就直接标记为已发, 不再重复注入。
                if (EconomyWorld.Treasury.Gold >= 1000000) { TempFilled = true; return; }
                if (Clan.PlayerClan == null || Clan.PlayerClan.Kingdom == null) return;
                TempFilled = true;
                DebugFillPlayer();     // TEMP
                DebugGrantGold();      // TEMP: 开局给玩家所属国家 1 亿
            }
            catch { }
        }

        // TEMP 测试: 开局给玩家所属国家 1 亿第纳尔(用户要求"方便搞事情"); 上线前连同 DebugFillPlayer 一起删
        internal static void DebugGrantGold()
        {
            try
            {
                const int Grant = 100000000;   // 1 亿
                EconomyWorld.TreasuryAdd(Grant);
                DLog.Force("TEMP: 玩家国国库已注入 " + Grant.ToString("N0") + " 第纳尔(现 " + EconomyWorld.Treasury.Gold.ToString("N0") + ")");
                MapSelection.Message("测试补给: 国库 +" + Grant.ToString("N0") + " 第纳尔(现 " + EconomyWorld.Treasury.Gold.ToString("N0") + ")");
            }
            catch (Exception ex) { DLog.Force("TEMP 国库注入失败: " + ex.Message); }
        }

        internal static void EnsureConverted()
        {
            if (Converted) return;
            try { ConvertAll(); }
            catch (Exception ex) { DLog.Force("建档转化异常: " + ex.Message); }
            Converted = true;   // 一次性: 失败也不重复(宽容补跑)
            TempFillIfReady();  // TEMP: 进入地图时若玩家王国已确定(读档)则补一次
        }

        // 28.2 建档转化: 所有领主部队(含玩家家族)/城防驻军/民兵
        //   按 27.12 折算兵种 + 免费发装(不扣军械库) + 初始熟练度(T1~T6 -> 20/35/50/65/80/92)
        //   人数与构成不变, 只换表示(逐人表)
        internal static void ConvertAll()
        {
            int parties = 0, men = 0, items = 0;
            try
            {
                var c = Campaign.Current;
                if (c == null || c.MobileParties == null) return;
                for (int i = 0; i < c.MobileParties.Count; i++)
                {
                    var p = c.MobileParties[i];
                    if (!ShouldConvert(p)) continue;
                    var rec = Soldiers.Ensure(p);            // 懒初始化 = 按 27.12 生成逐人表
                    int n = rec != null ? rec.List.Count : 0;
                    if (n <= 0) continue;
                    parties++;
                    men += n;
                    var need = FreeIssueNeed(p);             // 免费发装(只折算, 不入库)
                    foreach (var kv in need) if (kv.Value > 0) items += kv.Value;
                }
            }
            catch (Exception ex) { DLog.Force("建档转化扫描异常: " + ex.Message); }
            DLog.Force("建档转化(28.2): 部队 " + parties + " 支 / 士兵 " + men + " 人 / 免费发装 " + items
                + " 件(不入库) / 初始熟练 T1~T6=20/35/50/65/80/92");
        }

        // 商队/村民/巡逻队不动(28.2); 只转化 领主部队(含玩家家族)/城防驻军/民兵
        private static bool ShouldConvert(MobileParty p)
        {
            try
            {
                if (p == null || !p.IsActive || p.MemberRoster == null) return false;
                if (p.IsGarrison || p.IsMilitia || p.IsMainParty) return true;
                return p.PartyComponent is LordPartyComponent;
            }
            catch { return false; }
        }

        // 免费发装(28.2): 按 27.12 装备档配齐, 不扣军械库; 转化与解散返还(28.9)共用
        internal static Dictionary<string, int> FreeIssueNeed(MobileParty p)
        {
            var need = new Dictionary<string, int>();
            try
            {
                var info = Equipment.VanillaApprox(p);
                for (int b = 0; b < 4; b++)
                {
                    int n = info.Men[b];
                    if (n <= 0 || info.Gear[b] == null) continue;
                    for (int g = 0; g < info.Gear[b].Length; g++)
                    {
                        string id = info.Gear[b][g];
                        if (string.IsNullOrEmpty(id)) continue;
                        int old;
                        need.TryGetValue(id, out old);
                        need[id] = old + n;
                    }
                }
            }
            catch { }
            return need;
        }

        // ==================== 存档 ====================
        // v4.255: 守备营装备账 <-> 文本(型号:件数, @ 分隔)
        private static string EquipLedgerText(Dictionary<string, int> d)
        {
            try
            {
                if (d == null || d.Count == 0) return "";
                var sb = new StringBuilder();
                foreach (var kv in d)
                {
                    if (kv.Value <= 0 || string.IsNullOrEmpty(kv.Key)) continue;
                    if (sb.Length > 0) sb.Append('@');
                    sb.Append(kv.Key).Append(':').Append(kv.Value);
                }
                return sb.ToString();
            }
            catch { return ""; }
        }

        private static void ParseEquipLedger(Dictionary<string, int> d, string text)
        {
            try
            {
                if (d == null || string.IsNullOrEmpty(text)) return;
                foreach (var pair in text.Split('@'))
                {
                    if (string.IsNullOrEmpty(pair)) continue;
                    int at = pair.LastIndexOf(':');
                    if (at <= 0) continue;
                    int n;
                    if (!int.TryParse(pair.Substring(at + 1), out n) || n <= 0) continue;
                    string id = pair.Substring(0, at);
                    int old;
                    d.TryGetValue(id, out old);
                    d[id] = old + n;
                }
            }
            catch { }
        }

        internal static string Save()
        {
            try
            {
                var sb = new StringBuilder("v2;");
                sb.Append(ClanId).Append(';').Append(UnpaidDays).Append(';');
                for (int i = 0; i < Legions.Count; i++)
                {
                    var lg = Legions[i];
                    if (i > 0) sb.Append('|');
                    sb.Append(lg.PartyId).Append(',').Append(lg.GeneralId).Append(',').Append(lg.Number).Append(',')
                      .Append(lg.HomeId).Append(',').Append(lg.Expected).Append(',').Append(lg.Starve).Append(',')
                      .Append('0').Append(',').Append((lg.Task ?? "").Replace(",", "").Replace("|", ""))   // 旧 LowEquip 位占位(兼容旧档索引)
                      .Append(',').Append(lg.EquipTier).Append(',').Append(SupportMask(lg)).Append(',')
                      .Append(lg.Training.ToString("F2", CultureInfo.InvariantCulture)).Append(',').Append((int)Math.Round(EquipFillOf(lg) * 100f))
                      .Append(',').Append(CompOf(lg)[0]).Append(',').Append(CompOf(lg)[1])
                      .Append(',').Append(CompOf(lg)[2]).Append(',').Append(CompOf(lg)[3])
                      // v4.243: 16=状态 17=将军特质 18=训练度
                      .Append(',').Append(StanceOf(lg)).Append(',').Append(lg.GeneralTraits)
                      .Append(',').Append(TrainLevelOf(lg).ToString("F1", CultureInfo.InvariantCulture))
                      // v4.245: 19=玩家接管 20=最后玩家命令日
                      .Append(',').Append(lg.PlayerHold ? 1 : 0).Append(',').Append(lg.PlayerOrderDay);
                }
                sb.Append(';');
                for (int i = 0; i < Garrisons.Count; i++)
                {
                    var g = Garrisons[i];
                    if (i > 0) sb.Append('|');
                    sb.Append(g.SettlementId).Append(',').Append(g.Placed).Append(",0");   // 旧 LowEquip 位占位(兼容旧档索引)
                    // v4.255: 第 4 位 = 守备营装备账(型号:件数, @ 分隔; 无则空)
                    sb.Append(',').Append(EquipLedgerText(g.Equip));
                }
                sb.Append(';');
                for (int i = 0; i < Conscripts.Count; i++)
                {
                    var c = Conscripts[i];
                    if (i > 0) sb.Append('|');
                    sb.Append(c.SettlementId).Append(',').Append(c.Count).Append(',').Append(c.ExpireDay).Append(',').Append(c.Standing ? 1 : 0);
                }
                sb.Append(';');
                // v4.255: 追加第 7 段 = 欠饷警告标记 + 上次日结日(旧档无此段 -> 默认 false/-1, 索引不冲突)
                sb.Append(MutinyWarned ? 1 : 0).Append(',').Append(_lastSettledDay);
                return sb.ToString();
            }
            catch { return ""; }
        }

        internal static void Load(string data)
        {
            try
            {
                Legions.Clear(); Garrisons.Clear(); Conscripts.Clear();
                _clan = null;
                ClanId = ""; UnpaidDays = 0; MutinyWarned = false;
                if (string.IsNullOrEmpty(data)) return;
                var seg = data.Split(';');
                if (seg.Length < 2 || (seg[0] != "v1" && seg[0] != "v2")) return;   // v1 旧档兼容(缺字段取 27.15 默认)
                ClanId = seg[1] ?? "";
                int u;
                if (seg.Length > 2 && int.TryParse(seg[2], out u)) UnpaidDays = u;
                // v4.255: MutinyWarned/上次日结日落盘 —— 原来 MutinyWarned 没存,
                //   读档后欠饷≥7 天会重复 -30 士气; 日结日用于同日防重
                if (seg.Length > 6 && !string.IsNullOrEmpty(seg[6]))
                {
                    var v6 = seg[6].Split(',');
                    MutinyWarned = v6[0] == "1";
                    int ld;
                    if (v6.Length > 1 && int.TryParse(v6[1], out ld)) _lastSettledDay = ld;
                }
                if (seg.Length > 3) foreach (var line in seg[3].Split('|'))
                {
                    if (string.IsNullOrEmpty(line)) continue;
                    var f = line.Split(',');
                    if (f.Length < 8) continue;
                    var lg = new DefLegion();
                    lg.PartyId = f[0]; lg.GeneralId = f[1];
                    lg.Number = PI(f[2]); lg.HomeId = f[3]; lg.Expected = PI(f[4]);
                    lg.Starve = PI(f[5]); lg.Task = f[7];   // f[6] = 旧 LowEquip, 已废弃(读到忽略)
                    if (f.Length > 8) lg.EquipTier = PI(f[8]);
                    if (f.Length > 9)
                    {
                        int mask = PI(f[9]);
                        for (int k = 0; k < 3; k++) lg.Supports[k] = (mask & (1 << k)) != 0;
                    }
                    if (f.Length > 10) lg.Training = PF(f[10], -1f);
                    if (f.Length > 11) lg.EquipFill = PF(f[11], -1f) / 100f;
                    if (f.Length > 15)
                    {
                        if (lg.Comp == null) lg.Comp = new int[4];
                        for (int k = 0; k < 4; k++) lg.Comp[k] = PI(f[12 + k]);
                    }
                    // v4.243: 16=状态 17=将军特质 18=训练度(旧档缺项 -> 状态 0/特质 0/训练度按兵种补齐)
                    if (f.Length > 16) lg.Stance = PI(f[16]);
                    if (f.Length > 17) lg.GeneralTraits = PI(f[17]);
                    if (f.Length > 18) lg.Train = PF(f[18], -1f);
                    // v4.245: 19=玩家接管 20=最后玩家命令日(旧档缺项 -> false / -1)
                    if (f.Length > 19) lg.PlayerHold = PI(f[19]) == 1;
                    if (f.Length > 20) lg.PlayerOrderDay = PI(f[20]);
                    if (!string.IsNullOrEmpty(lg.PartyId)) Legions.Add(lg);
                }
                if (seg.Length > 4) foreach (var line in seg[4].Split('|'))
                {
                    if (string.IsNullOrEmpty(line)) continue;
                    var f = line.Split(',');
                    if (f.Length < 3) continue;
                    // f[2] = 旧 LowEquip, 已废弃(读到忽略)
                    if (string.IsNullOrEmpty(f[0])) continue;
                    var ng = new DefGarrison { SettlementId = f[0], Placed = PI(f[1]) };
                    if (f.Length > 3) ParseEquipLedger(ng.Equip, f[3]);   // v4.255: 守备营装备账(旧档无 -> 空)
                    Garrisons.Add(ng);
                }
                NormalizeGarrisons();   // v4.86: 合并同城重复守备营记录
                if (seg.Length > 5) foreach (var line in seg[5].Split('|'))
                {
                    if (string.IsNullOrEmpty(line)) continue;
                    var f = line.Split(',');
                    if (f.Length < 4) continue;
                    if (!string.IsNullOrEmpty(f[0])) Conscripts.Add(new DefConscript { SettlementId = f[0], Count = PI(f[1]), ExpireDay = PI(f[2]), Standing = PI(f[3]) == 1 });
                }
                _clan = null;   // 不再使用自定义家族(将军归属玩家家族)
                DLog.Force("国防军: 读档 家族=" + ClanId + " 军团=" + Legions.Count + " 守备=" + Garrisons.Count + " 征兵=" + Conscripts.Count);
            }
            catch (Exception ex) { DLog.Force("国防军读档异常: " + ex); }
        }

        internal static void Relink()
        {
            try
            {
                _clan = null;   // 不再使用自定义家族(将军归属玩家家族)
                try { EnsureClan(); } catch { }
                // 停用旧版遗留的"国防军家族"(0.2 早期创建, 无领袖/初始化不全 -> 原版遍历时崩)
                try
                {
                    var old = FindClan(ClanId);
                    if (old != null && !ReferenceEquals(old, Clan.PlayerClan))
                    {
                        var fld = HarmonyLib.AccessTools.Field(typeof(Clan), "_isEliminated");
                        if (fld != null) fld.SetValue(old, true);
                        DLog.Force("国防军: 已停用旧家族(" + ClanId + ")");
                    }
                }
                catch { }
                for (int i = Legions.Count - 1; i >= 0; i--)
                {
                    var lg = Legions[i];
                    if (lg == null) { Legions.RemoveAt(i); continue; }
                    // v4.90: 找不到部队时不再删除记录(修"读档丢军团"), 保留并由 LegionParty 按将军找回
                    if (LegionParty(lg) == null)
                        DLog.Force("国防军: 读档暂未找到军团部队 " + lg.PartyId + " (保留记录, 按将军找回)");
                }
                if (EquipMigrationPending)   // 27.15: 旧档装备迁移(此时部队名单可用)
                {
                    EquipMigrationPending = false;
                    MigrateEquip();
                }
                DLog.Force("国防军: 重连完成 军团=" + Legions.Count);
            }
            catch { }
        }

        internal static void Reset()
        {
            try
            {
                ClanId = "";
                _clan = null;
                Legions.Clear(); Garrisons.Clear(); Conscripts.Clear();
                LegionEquip.Clear();
                MilArmory.Reset();
                EquipMigrationPending = false;
                UnpaidDays = 0; TodayMilLoss = 0; MutinyWarned = false;
                _lastSettledDay = -1;   // v4.255: 新战役重开日结防重
                TroopCache.Clear();
                Converted = false;   // 28.2: 新战役重新建档转化
                TempFilled = false;  // TEMP: 新战役重新补给
                try { AiWarDirector.Reset(); } catch { }   // v5.x AI: 军事总监状态复位
                DLog.Force("国防军: 新战役初始化");
            }
            catch { }
        }

        private static int PI(string s) { int v; return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? v : 0; }
        private static float PF(string s, float def) { float v; return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : def; }

        private static int SupportMask(DefLegion lg)
        {
            int m = 0;
            try
            {
                if (lg != null && lg.Supports != null)
                    for (int i = 0; i < 3 && i < lg.Supports.Length; i++)
                        if (lg.Supports[i]) m |= 1 << i;
            }
            catch { }
            return m;
        }
    }

    // 国家军械库调用适配(27.4 两级库存的国家库): 实际库存由 Armory 维护(两军共用, FIA_Armory 段落盘)
    internal static class MilArmory
    {
        private static string NationalKey()
        {
            try { return Armory.NationalOwner(DefArmy.OurKingdom); }
            catch { return ""; }
        }

        internal static int Count(string id)
        {
            try { return Armory.CountKey(NationalKey(), id); }
            catch { return 0; }
        }

        internal static void Add(string id, int n)
        {
            try
            {
                if (string.IsNullOrEmpty(id) || n <= 0) return;
                Armory.AddKey(NationalKey(), id, n);
            }
            catch { }
        }

        // 领料, 返回实际领出数
        internal static int Take(string id, int n)
        {
            try
            {
                if (string.IsNullOrEmpty(id) || n <= 0) return 0;
                return Armory.TakeKey(NationalKey(), id, n);
            }
            catch { return 0; }
        }

        internal static int Total()
        {
            try { return Armory.TotalCount(NationalKey()); }
            catch { return 0; }
        }

        // 库存生命周期归 Armory/WarEconomy(新档 Reset / 读档 Load 由那边负责), 此处不清空
        internal static void Reset() { }
    }
}
