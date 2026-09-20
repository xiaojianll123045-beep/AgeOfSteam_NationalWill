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
        internal bool LowEquip;     // 低配入列(士气 -10%)
        internal string Task = "驻守";
    }

    // 守备营: 我们投放在某镇/堡驻军里的国防军兵员
    internal class DefGarrison
    {
        internal string SettlementId = "";
        internal int Placed;        // 对账后的在册数
        internal bool LowEquip;
    }

    // 征兵批次(28 天服役 / 战时自动续征 / 转常备)
    internal class DefConscript
    {
        internal string SettlementId = "";
        internal int Count;
        internal int ExpireDay = -1;   // -1 = 转常备(永久)
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
        internal const float FoodPerManPerDay = 0.02f;  // 军粮/兵/日
        internal const int RecruitCost = 20;            // 募兵/兵
        internal const int ConscriptCost = 5;           // 征兵/兵
        internal const int LegionFormCost = 2000;       // 建军
        internal const int GeneralOutfitCost = 500;     // 将军袍服
        internal const int SplitCost = 500;             // 拆分(新将军)
        internal const int DisbandCost = 15;            // 遣散费/兵
        internal const int GeneralWageMonth = 30;       // 将军月薪
        internal const int LegionMinMen = 100;          // 成立/拆分下限
        internal const int LegionKeepMin = 50;          // 拆分后每支下限

        internal static string ClanId = "";
        internal static readonly List<DefLegion> Legions = new List<DefLegion>();
        internal static readonly List<DefGarrison> Garrisons = new List<DefGarrison>();
        internal static readonly List<DefConscript> Conscripts = new List<DefConscript>();
        internal static int UnpaidDays;
        internal static int TodayMilLoss;      // 今日军损(统计翻牌)
        internal static bool MutinyWarned;

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
            return (int)Math.Ceiling(TotalMen() * WagePerManPerDay);
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

        // v4.118: 沿升级链找 ≥wantTier 的同文化兵(供募兵/整编共用)
        internal static CharacterObject FindEliteTroop(CultureObject culture, int wantTier)
        {
            try
            {
                if (culture == null) return null;
                var t2 = TroopsOf(culture);
                for (int i = 0; i < 3; i++)
                {
                    var b = t2[i];
                    if (b == null || b.UpgradeTargets == null) continue;
                    foreach (var up in b.UpgradeTargets)
                    {
                        if (up == null || up.IsHero) continue;
                        if (up.Culture != null && up.Culture.StringId != culture.StringId) continue;
                        if (up.Tier >= wantTier) return up;
                        if (up.UpgradeTargets != null)
                        {
                            foreach (var up2 in up.UpgradeTargets)
                            {
                                if (up2 == null || up2.IsHero) continue;
                                if (up2.Culture != null && up2.Culture.StringId != culture.StringId) continue;
                                if (up2.Tier >= wantTier) return up2;
                            }
                        }
                    }
                }
            }
            catch { }
            return null;
        }

        // v4.116/4.117: AI 精锐补兵(沿升级链找 ≥wantTier 的兵; 找不到退回普通 T2)
        internal static void AddEliteCompositionPublic(TroopRoster roster, CultureObject culture, int n, int wantTier)
        {
            try
            {
                if (roster == null || culture == null || n <= 0) return;
                var pick = FindEliteTroop(culture, wantTier);
                if (pick == null) { AddComposition(roster, culture, n); return; }
                roster.AddToCounts(pick, n, false, 0, 0, true, -1);
            }
            catch { AddComposition(roster, culture, n); }
        }

        // v4.118: 精锐整编(把全部军团的 T2 兵升级为 T3/T4, 按国库能力; 玩家与 AI 对等)
        internal static string UpgradeLegionsToElite()
        {
            try
            {
                int tier = EconomyWorld.Treasury.Gold > 12000 ? 4 : (EconomyWorld.Treasury.Gold > 5000 ? 3 : 0);
                if (tier == 0) return "国库不足(需 >5,000 第纳尔才能整编精锐)";
                int unitCost = tier >= 4 ? 50 : 30;
                int total = 0, spent = 0;
                for (int i = 0; i < Legions.Count; i++)
                {
                    var lg = Legions[i];
                    var p = LegionParty(lg);
                    if (p == null || !p.IsActive) continue;
                    var culture = CultureOfLegion(lg);
                    if (culture == null) continue;
                    var elite = FindEliteTroop(culture, tier);
                    if (elite == null) continue;
                    var t2 = TroopsOf(culture);
                    int count = 0;
                    for (int b = 0; b < 3; b++)
                    {
                        if (t2[b] == null) continue;
                        try { count += p.MemberRoster.GetTroopCount(t2[b]); } catch { }
                    }
                    if (count <= 0) continue;
                    int can = (int)Math.Min(count, (EconomyWorld.Treasury.Gold - 500) / Math.Max(1, unitCost));
                    if (can <= 0) break;
                    int left = can;
                    for (int b = 0; b < 3 && left > 0; b++)
                    {
                        if (t2[b] == null) continue;
                        int have = 0;
                        try { have = p.MemberRoster.GetTroopCount(t2[b]); } catch { }
                        int take = Math.Min(have, left);
                        if (take <= 0) continue;
                        p.MemberRoster.AddToCounts(t2[b], -take, false, 0, 0, true, -1);
                        left -= take;
                    }
                    int moved = can - left;
                    if (moved <= 0) continue;
                    p.MemberRoster.AddToCounts(elite, moved, false, 0, 0, true, -1);
                    int cost = moved * unitCost;
                    EconomyWorld.TreasurySpend(cost);
                    Fiscal.AddMilitary(cost);
                    total += moved; spent += cost;
                    DLog.Force("国防军: 精锐整编 " + MapSelection.NameOf(p) + " " + moved + " 兵 → "
                        + (elite.Name != null ? elite.Name.ToString() : "?") + "(费 " + cost + ")");
                }
                if (total <= 0) return "没有可整编的部队(或国库不足)";
                return "精锐整编: " + total + " 兵升级为 " + (tier >= 4 ? "顶级精锐" : "精锐") + ", 花费 " + spent.ToString("N0") + " 第纳尔";
            }
            catch (Exception ex) { DLog.Force("精锐整编失败: " + ex); return "精锐整编失败, 见日志"; }
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
        private static int MoveDefTroops(TroopRoster from, TroopRoster to, CultureObject culture, int n)
        {
            try
            {
                if (from == null || to == null || culture == null || n <= 0) return 0;
                var t = TroopsOf(culture);
                var want = Compose(n, t[2]);
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
        private static void EquipmentNeed(int n, out int weapons, out int armor, out int leather)
        {
            weapons = Math.Max(1, (int)Math.Round(n / 100f));
            armor = Math.Max(1, (int)Math.Round(n / 200f));
            leather = armor;
        }

        private static bool TakeItem(Settlement s, string goodId, int need)
        {
            try
            {
                if (need <= 0) return true;
                var item = FeudalGoods.Item(goodId);
                var roster = s != null ? s.ItemRoster : null;
                if (item == null || roster == null) return false;
                int have = roster.GetItemNumber(item);
                int take = Math.Min(have, need);
                if (take > 0) roster.AddToCounts(item, -take);
                return take >= need;
            }
            catch { return false; }
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
                        b.LowEquip |= a.LowEquip;
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
        // 三要素: 人(1:1) / 钱(20/兵) / 装备(武器1 甲0.5 皮0.5 per 100)
        internal static string Recruit(Settlement s, int n, int tier)
        {
            try
            {
                if (OurKingdom == null) return Fail("尚未建立国家意志");
                if (s == null || n <= 0) return Fail("请先选择募兵地");
                int townHallDiscount = Politics.SeatHeld(3) ? 1 : 0;   // 军务大臣: 募兵费 -10%
                // v4.119: 档位由玩家在募兵时选择(0=普通, 3=精锐, 4=顶级)
                if (tier != 3 && tier != 4) tier = 0;
                int unitPrice = RecruitCost * (tier >= 4 ? 3 : (tier == 3 ? 2 : 1));
                int cost = (int)Math.Round(n * unitPrice * (townHallDiscount == 1 ? 0.9f : 1f));
                if (EconomyWorld.Treasury.Gold < cost) return Fail("国库不足: 需要 " + cost.ToString("N0") + " 第纳尔(现有 " + EconomyWorld.Treasury.Gold.ToString("N0") + ")");

                int avail = CommonersOf(s);
                if (avail < n) return Fail("可转人口不足: " + s.Name + " 只有 " + avail + " 人可征募");
                var target = GarrisonTarget(s);
                if (target == null) return Fail("该聚落没有可驻防的城镇/城堡");
                var garrison = GarrisonPartyOf(target);
                if (garrison == null) return Fail("驻军不存在: " + target.Name);

                int moved = TakeCommoners(s, n);
                if (moved <= 0) return Fail("可转人口不足");
                if (moved < n) n = moved;

                int needW, needA, needL;
                EquipmentNeed(n, out needW, out needA, out needL);
                bool okW = TakeItem(s, FeudalGoods.Weapons, needW);
                bool okA = TakeItem(s, FeudalGoods.Armor, needA);
                bool okL = TakeItem(s, FeudalGoods.Leather, needL);
                bool lowEquip = !(okW && okA && okL);

                if (tier > 0) AddEliteCompositionPublic(garrison.MemberRoster, s.Culture, n, tier);
                else AddComposition(garrison.MemberRoster, s.Culture, n);
                var rec = GetGarrisonRec(target.StringId, true);
                if (rec != null) { rec.Placed += n; rec.LowEquip |= lowEquip; }

                Spend(cost);
                Fiscal.AddMilitary(cost);
                string tierName = tier >= 4 ? "顶级精锐" : (tier == 3 ? "精锐" : "");
                string msg = "募兵 " + n + " 人" + (tierName.Length > 0 ? "(" + tierName + ")" : "") + " → " + target.Name + "守备营 · 花费 " + cost.ToString("N0") + " 第纳尔"
                    + (lowEquip ? " · 装备不足, 低配入列(士气-10%)" : " · 装备齐整");
                DLog.Force("国防军: " + msg);
                return msg;
            }
            catch (Exception ex) { DLog.Force("募兵失败: " + ex); return "募兵失败, 见日志"; }
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

        // v4.91: 补 native 陆地导航(海军 DLC 下新建军团若缺此能力, native 侧不会移动)
        internal static void EnsureNavigation(MobileParty p)
        {
            try
            {
                if (p == null) return;
                // v4.95: 非活跃部队 native 完全不驱动 -> 强制激活
                try { if (!p.IsActive) { p.IsActive = true; DLog.Force("国防军: 部队非活跃, 已激活 " + MapSelection.NameOf(p)); } } catch { }
                if (!p.IsActive) return;
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
                try
                {
                    if (p.CurrentSettlement != null)
                    {
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
                try { return p.MemberRoster.TotalRegulars; }
                catch { return p.MemberRoster.TotalManCount - p.MemberRoster.TotalHeroes; }
            }
            catch { return 0; }
        }

        internal static string CreateLegion(Settlement home, int n)
        {
            try
            {
                if (OurKingdom == null) return Fail("尚未建立国家意志");
                if (home == null) return Fail("请先选择驻地");
                if (n < LegionMinMen) return Fail("成立军团至少需要 " + LegionMinMen + " 兵");
                if (n > MaxLegionMen) return Fail("成立军团人数超出范围");
                int cost = LegionFormCost + GeneralOutfitCost;
                if (EconomyWorld.Treasury.Gold < cost) return Fail("国库不足: 建军需要 " + cost.ToString("N0") + " 第纳尔(现有 " + EconomyWorld.Treasury.Gold.ToString("N0") + ")");
                var garrison = GarrisonPartyOf(home);
                if (garrison == null || home.Culture == null) return Fail("该地没有驻军");
                // v4.123: 成立 N 人 = 从守备营抽 N 兵(将军免费附送, 不从守备营出)
                int need = n > 0 ? n : 1;
                int actual = GarrisonMenOf(home.StringId);   // v4.90: 按我们的账面口径
                int recWant = need;
                if (actual < recWant) return Fail("守备营兵力不足: 只有 " + actual + " 兵(计划 " + n + ")");

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
                int moved = MoveDefTroops(garrison.MemberRoster, mr, home.Culture, need);   // v4.123: 抽 n 兵(将军附送)
                // v4.95: 生成在城门外空地(城中心可能不在导航网格上, 部队会卡住不动)
                CampaignVec2 spawn = home.Position;
                try { spawn = home.GatePosition; } catch { }
                party.InitializeMobilePartyAtPosition(mr, new TroopRoster(party.Party), spawn, false);
                try { party.Party.SetCustomName(new TextObject(LegionName(number))); } catch { }
                try { party.SetCustomHomeSettlement(home); } catch { }
                // v4.105: 创建后原地待命(全靠玩家指挥; 手动驾驶兜底保证命令仍能驱动移动)
                try { party.SetMoveModeHold(); } catch { }
                EnsureNavigation(party);   // v4.91: 确保 native 陆地导航可用(否则 native 侧不移动)
                // v4.90: 不再对军团设 SetDoNotMakeNewDecisions/Hold(该标志会让 native 侧冻结移动, 导致"指挥不动")

                var rec = GetGarrisonRec(home.StringId, true);
                if (rec != null) rec.Placed = Math.Max(0, rec.Placed - moved);
                bool low = rec != null && rec.LowEquip;
                if (low) { try { party.RecentEventsMorale = party.RecentEventsMorale - 10f; } catch { } }
                Legions.Add(new DefLegion
                {
                    PartyId = pid,
                    GeneralId = general.StringId,
                    HomeId = home.StringId,
                    Number = number,
                    Expected = moved,
                    LowEquip = low,
                    Task = "驻守"
                });
                Spend(cost);
                Fiscal.AddMilitary(cost);
                MapSelection.Select(party);
                string msg = "成立「" + LegionName(number) + "」 " + moved + " 兵 · 花费 " + cost.ToString("N0") + " 第纳尔";
                DLog.Force("国防军: " + msg);
                return msg;
            }
            catch (Exception ex) { DLog.Force("成立军团失败: " + ex); return "成立军团失败, 见日志"; }
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
                    int men = p.MemberRoster.TotalManCount;
                    int moved = MoveDefTroops(p.MemberRoster, main.MemberRoster, culture, men);
                    total += moved;
                    var lg = LegionOf(p);
                    if (lg != null)
                    {
                        RetireGeneral(lg.GeneralId);
                        Legions.Remove(lg);
                    }
                    try { DestroyPartyAction.ApplyForDisbanding(p, FindSettlement(mainLg != null ? mainLg.HomeId : "")); } catch { }
                    AddSoldiersPop(HomeOf(mainLg), 1);   // 将军卸任转 1 名小兵
                    retired++;
                }
                if (mainLg != null) mainLg.Expected = main.MemberRoster.TotalHealthyCount;
                string msg = "合并完成: " + (list.Count - 1) + " 支并入 " + MapSelection.NameOf(main)
                    + ", 卸任将军 " + retired + " 名 → 兵员 +" + retired + " · 编制 " + total;
                DLog.Force("国防军: " + msg);
                MapSelection.Select(main);
                return msg;
            }
            catch (Exception ex) { DLog.Force("合并失败: " + ex); return "合并失败, 见日志"; }
        }

        // 拆分: 1 名小兵升任将军(编制 -1), 花 500
        internal static string SplitLegion(MobileParty p)
        {
            try
            {
                var lg = LegionOf(p);
                if (lg == null) return "只能拆分国防军野战军团";
                int men = p.MemberRoster.TotalManCount;
                if (men < LegionMinMen) return "军团兵力不足 " + LegionMinMen + ", 无法拆分";
                int half = men / 2;
                int remain = men - half;
                if (half < LegionKeepMin || remain < LegionKeepMin) return "拆分后每支不得少于 " + LegionKeepMin + " 兵";
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
                int moved = MoveDefTroops(p.MemberRoster, mr, home.Culture, half);
                party.InitializeMobilePartyAtPosition(mr, new TroopRoster(party.Party), p.Position, false);
                try { party.Party.SetCustomName(new TextObject(LegionName(number))); } catch { }
                try { party.SetCustomHomeSettlement(home); } catch { }
                try { party.Ai.SetDoNotMakeNewDecisions(true); party.SetMoveModeHold(); } catch { }
                Legions.Add(new DefLegion
                {
                    PartyId = pid,
                    GeneralId = general.StringId,
                    HomeId = home.StringId,
                    Number = number,
                    Expected = moved,
                    LowEquip = lg.LowEquip,
                    Task = "驻守"
                });
                lg.Expected = p.MemberRoster.TotalHealthyCount;
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
                int men = p.MemberRoster.TotalManCount;
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
                return (int)(Pops.TotalPopulation() * (0.02f * lv) * FeudalContracts.AvgLevyMult() * LawSystem.ConscriptMult() * WarMobilization.ConscriptMult());   // v4.126: 兵役契约; v5.0-P22: 兵役制度; v5.0-P25: 动员 ×1.5
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

                int needW, needA, needL;
                EquipmentNeed(n, out needW, out needA, out needL);
                bool okW = TakeItem(s, FeudalGoods.Weapons, needW);
                bool okA = TakeItem(s, FeudalGoods.Armor, needA);
                bool okL = TakeItem(s, FeudalGoods.Leather, needL);
                bool lowEquip = !(okW && okA && okL);

                AddComposition(garrison.MemberRoster, s.Culture, n);
                var rec = GetGarrisonRec(target.StringId, true);
                if (rec != null) { rec.Placed += n; rec.LowEquip |= lowEquip; }

                int day = (int)CampaignTime.Now.ToDays;
                Conscripts.Add(new DefConscript { SettlementId = s.StringId, Count = n, ExpireDay = day + 28 });

                Spend(cost);
                Fiscal.AddMilitary(cost);

                // 民心代价: 每征召 1% 总人口 -> 激进 +0.5%(秋收月 ×2, 军令 3 级免)
                float total = Math.Max(1f, Pops.TotalPopulation());
                float perPct = 0.5f * n / total;
                int month = (int)(CampaignTime.Now.ToDays % 84) / 7;
                bool harvest = month >= 6 && month <= 8;
                if (harvest && Politics.LawLevel(2) < 3) perPct *= 2f;
                Pops.ShiftRadicals(perPct);
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

                string msg = "征兵 " + n + " 人 → " + target.Name + "守备营 · 服役 28 天"
                    + (harvest ? " · 秋收月(民心惩罚×2)" : "") + (over > 0 ? " · 超征!" : "");
                DLog.Force("国防军: " + msg);
                return msg;
            }
            catch (Exception ex) { DLog.Force("征兵失败: " + ex); return "征兵失败, 见日志"; }
        }

        internal static string RenewConscripts()
        {
            try
            {
                int day = (int)CampaignTime.Now.ToDays;
                int n = 0;
                for (int i = 0; i < Conscripts.Count; i++)
                {
                    var c = Conscripts[i];
                    if (c == null || c.Standing) continue;
                    c.ExpireDay = Math.Max(c.ExpireDay, day) + 7;
                    n++;
                }
                if (n == 0) return "没有可续征的批次";
                Pops.ShiftRadicals(0.01f);
                Politics.Legitimacy = Math.Max(0f, Politics.Legitimacy - 1f);
                string msg = "续征 " + n + " 个批次(+7 天) · 激进+1% 合法性-1";
                DLog.Force("国防军: " + msg);
                return msg;
            }
            catch (Exception ex) { DLog.Force("续征失败: " + ex.Message); return "续征失败"; }
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
                string msg = "解甲归田 " + total + " 人(1:1 回乡)";
                DLog.Force("国防军: " + msg);
                return msg;
            }
            catch (Exception ex) { DLog.Force("解甲失败: " + ex.Message); return "解甲失败"; }
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
                        lg.Expected = p.MemberRoster.TotalHealthyCount;
                    }
                    var rec = GetGarrisonRec(target.StringId, false);
                    if (rec != null) rec.Placed = Math.Max(0, rec.Placed - (c.Count - left));
                }
                ReturnSoldiersToCommoners(s, c.Count);
            }
            catch { }
        }

        private static void ConscriptTick(int day)
        {
            try
            {
                bool war = IsAtWar();
                for (int i = Conscripts.Count - 1; i >= 0; i--)
                {
                    var c = Conscripts[i];
                    if (c == null) { Conscripts.RemoveAt(i); continue; }
                    if (c.Standing || c.ExpireDay < 0) continue;
                    if (war)
                    {
                        if (c.ExpireDay < day + 14) c.ExpireDay = day + 14;   // 战时自动续征
                        continue;
                    }
                    if (day > c.ExpireDay)
                    {
                        ReleaseBatch(c);
                        Conscripts.RemoveAt(i);
                    }
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
                EnsureClan();
                PayWages();
                FeedLegions();
                LogLegionChanges();   // v4.89: 人数变动诊断
                Reconcile();
                ConscriptTick(day);
                if (CampaignTime.Now.GetDayOfWeek == 0) Month(day);
            }
            catch (Exception ex) { DLog.Force("国防军日结异常: " + ex); }
        }

        private static void PayWages()
        {
            try
            {
                int men = TotalMen();
                if (men <= 0) { UnpaidDays = 0; return; }
                int cost = (int)Math.Ceiling(men * WagePerManPerDay);
                if (EconomyWorld.Treasury.Gold >= cost)
                {
                    EconomyWorld.TreasurySpend(cost);
                    Fiscal.AddMilitary(cost);
                    // v4.89: 原版按"家族金库 = 家族领袖(玩家)个人金币"给家族部队发工资
                    // (实测 Clan.Gold → get_Leader().get_Gold()), 玩家金库被扣空后会欠薪逃兵
                    // (用户反馈: 国防军莫名其妙减少人)。本系统已从国库扣过军饷, 这里给原版侧兜底:
                    // 把玩家金库补到至少 30 天军饷, 原版发薪不再欠薪
                    try
                    {
                        var hero = Hero.MainHero;
                        if (hero != null)
                        {
                            int need = cost * 30;
                            if (hero.Gold < need) hero.Gold = need;
                        }
                    }
                    catch { }
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
                    int need = (int)Math.Ceiling(men * FoodPerManPerDay);
                    var home = HomeOf(lg);
                    bool ok = false;
                    if (home != null && grain != null && home.ItemRoster != null)
                    {
                        int have = home.ItemRoster.GetItemNumber(grain);
                        if (have >= need)
                        {
                            home.ItemRoster.AddToCounts(grain, -need);
                            ok = true;
                        }
                    }
                    if (ok) { lg.Starve = 0; continue; }
                    lg.Starve++;
                    if (lg.Starve > 3)
                    {
                        int desert = Math.Max(1, men / 50);
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
                        RetireGeneral(lg.GeneralId);
                        Legions.RemoveAt(i);
                        DLog.Force("国防军: " + LegionName(lg.Number) + " 编制消失(阵亡/被俘), 军损 " + men);
                        continue;
                    }
                    int actual = p.MemberRoster.TotalHealthyCount;
                    if (actual < lg.Expected)
                    {
                        int d = lg.Expected - actual;
                        RemoveSoldiersPop(HomeOf(lg), d);
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
                        ApplyMutinyPop(lg.HomeId, p.MemberRoster.TotalManCount);
                        RemoveDefTroops(p.MemberRoster, CultureOfLegion(lg), p.MemberRoster.TotalManCount);
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
                TodayMilLoss = 0;
                DLog.Force("国防军月结: 兵力=" + men + " 军团=" + Legions.Count + " 守备=" + Garrisons.Count
                    + " 欠饷天=" + UnpaidDays + " 军费可撑=" + DaysAffordable() + "天");
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
                    bool hasTask = lg.Task != null && (lg.Task.Contains("驻防") || lg.Task.Contains("巡逻"));
                    bool commanded = CommandTimeout.IsCommanded(p) || MapSelection.Is(p);
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
                int room = MaxLegionMen - cur;
                if (room <= 0) return "军团已满员";
                int avail = GarrisonMenOf(home.StringId);   // v4.90: 账面口径
                if (avail <= 0) return "驻地守备营没有可调兵员(先募兵)";
                int n = Math.Min(Math.Min(want, room), avail);
                if (n <= 0) return "没有可补充的兵员";
                int moved = MoveDefTroops(garrison.MemberRoster, p.MemberRoster, home.Culture, n);
                var rec = GetGarrisonRec(home.StringId, true);
                if (rec != null) rec.Placed = Math.Max(0, rec.Placed - moved);
                lg.Expected = RegularsOf(p);
                string msg = "补员 " + moved + " 人 → " + MapSelection.NameOf(p) + "(编制 " + RegularsOf(p) + ")";
                DLog.Force("国防军: " + msg);
                return msg;
            }
            catch (Exception ex) { DLog.Force("补员失败: " + ex); return "补员失败, 见日志"; }
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

        // ==================== 存档 ====================
        internal static string Save()
        {
            try
            {
                var sb = new StringBuilder("v1;");
                sb.Append(ClanId).Append(';').Append(UnpaidDays).Append(';');
                for (int i = 0; i < Legions.Count; i++)
                {
                    var lg = Legions[i];
                    if (i > 0) sb.Append('|');
                    sb.Append(lg.PartyId).Append(',').Append(lg.GeneralId).Append(',').Append(lg.Number).Append(',')
                      .Append(lg.HomeId).Append(',').Append(lg.Expected).Append(',').Append(lg.Starve).Append(',')
                      .Append(lg.LowEquip ? 1 : 0).Append(',').Append((lg.Task ?? "").Replace(",", "").Replace("|", ""));
                }
                sb.Append(';');
                for (int i = 0; i < Garrisons.Count; i++)
                {
                    var g = Garrisons[i];
                    if (i > 0) sb.Append('|');
                    sb.Append(g.SettlementId).Append(',').Append(g.Placed).Append(',').Append(g.LowEquip ? 1 : 0);
                }
                sb.Append(';');
                for (int i = 0; i < Conscripts.Count; i++)
                {
                    var c = Conscripts[i];
                    if (i > 0) sb.Append('|');
                    sb.Append(c.SettlementId).Append(',').Append(c.Count).Append(',').Append(c.ExpireDay).Append(',').Append(c.Standing ? 1 : 0);
                }
                sb.Append(';');
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
                if (seg.Length < 2 || seg[0] != "v1") return;
                ClanId = seg[1] ?? "";
                int u;
                if (seg.Length > 2 && int.TryParse(seg[2], out u)) UnpaidDays = u;
                if (seg.Length > 3) foreach (var line in seg[3].Split('|'))
                {
                    if (string.IsNullOrEmpty(line)) continue;
                    var f = line.Split(',');
                    if (f.Length < 8) continue;
                    var lg = new DefLegion();
                    lg.PartyId = f[0]; lg.GeneralId = f[1];
                    lg.Number = PI(f[2]); lg.HomeId = f[3]; lg.Expected = PI(f[4]);
                    lg.Starve = PI(f[5]); lg.LowEquip = PI(f[6]) == 1; lg.Task = f[7];
                    if (!string.IsNullOrEmpty(lg.PartyId)) Legions.Add(lg);
                }
                if (seg.Length > 4) foreach (var line in seg[4].Split('|'))
                {
                    if (string.IsNullOrEmpty(line)) continue;
                    var f = line.Split(',');
                    if (f.Length < 3) continue;
                    if (!string.IsNullOrEmpty(f[0])) Garrisons.Add(new DefGarrison { SettlementId = f[0], Placed = PI(f[1]), LowEquip = PI(f[2]) == 1 });
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
                UnpaidDays = 0; TodayMilLoss = 0; MutinyWarned = false;
                TroopCache.Clear();
                DLog.Force("国防军: 新战役初始化");
            }
            catch { }
        }

        private static int PI(string s) { int v; return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? v : 0; }
    }
}
