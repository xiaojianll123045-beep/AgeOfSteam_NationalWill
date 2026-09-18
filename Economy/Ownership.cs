using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;

namespace FeudalInternalAffairs
{
    // 所有权(文档 20.4): 王室/领主/教会/行会/私人; 决定分红去向 + 赎买/没收/出售
    internal static class Ownership
    {
        internal static readonly string[] Names = { "王室", "领主", "教会", "行会", "私人" };
        internal static readonly string[] ShortNames = { "王", "领", "会", "行", "私" };

        internal static float LordPool;     // 领主私产池(全图, UI 展示)
        internal static float ChurchPool;   // 教会池
        internal static float GuildFund;    // 行会基金

        internal static readonly Dictionary<string, int> LordFavor = new Dictionary<string, int>();          // clanId -> 好感
        internal static readonly Dictionary<string, int> ConfiscateDay = new Dictionary<string, int>();      // clanId -> 上次没收日

        internal static string NameOf(int owner) { return owner >= 0 && owner < 5 ? Names[owner] : "领主"; }
        internal static string ShortOf(int owner) { return owner >= 0 && owner < 5 ? ShortNames[owner] : "领"; }

        // 默认所有者: 城堡=王室; 村庄=领主; 城镇按哈希 60/25/15
        internal static int DefaultOwner(string defId, bool isTown, bool isCastle, bool isVillage)
        {
            try
            {
                if (isCastle) return 0;
                if (isVillage) return 1;
                int h = Fnv(defId ?? "") % 100;
                if (h < 60) return 1;      // 领主
                if (h < 85) return 3;      // 行会
                return 4;                  // 私人
            }
            catch { return 1; }
        }

        private static int Fnv(string s)
        {
            unchecked
            {
                int h = (int)2166136261;
                for (int i = 0; i < s.Length; i++) { h = h ^ s[i]; h = h * 16777619; }
                return h & 0x7fffffff;
            }
        }

        internal static int OwnerOf(BuildingGroup g, Settlement s)
        {
            if (g == null) return 1;
            if (g.Owner >= 0) return g.Owner;
            return DefaultOwner(g.DefId, s != null && s.IsTown, s != null && s.IsCastle, s != null && s.IsVillage);
        }

        // 月度分红分配(替代旧写死比例; 文档 20.4)
        internal static void PayDividend(int owner, float div, string kid, List<PopRecord> pops)
        {
            try
            {
                if (div <= 1f) return;
                switch (owner)
                {
                    case 0:   // 王室
                        EconomyWorld.TreasuryAdd((int)div);
                        Fiscal.AddDividend((int)div);
                        break;
                    case 1:   // 领主庄园: 70% 私产 / 30% 上缴
                        LordPool += div * 0.7f;
                        EconomyWorld.TreasuryAdd((int)(div * 0.3f));
                        Fiscal.AddDividend((int)(div * 0.3f));
                        Politics.OnLordDividend((int)(div * 0.7f));   // 第 21 章: 分红影响领主态度
                        break;
                    case 2:   // 教会: 80% 教会池 / 20% 上缴
                        ChurchPool += div * 0.8f;
                        EconomyWorld.TreasuryAdd((int)(div * 0.2f));
                        Fiscal.AddDividend((int)(div * 0.2f));
                        break;
                    case 3:   // 行会作坊: 60% 行会基金 / 40% 店主
                        GuildFund += div * 0.6f;
                        WealthTo(pops, PopDefs.Shopkeepers, div * 0.4f);
                        break;
                    default:  // 私人: 富商 50% / 机械工 10% / 投资池 40%
                        WealthTo(pops, PopDefs.Capitalists, div * 0.5f);
                        WealthTo(pops, PopDefs.Machinists, div * 0.1f);
                        InvestmentPool.Add(kid, div * 0.4f);
                        break;
                }
            }
            catch { }
        }

        private static void WealthTo(List<PopRecord> pops, string profession, float money)
        {
            try
            {
                if (pops == null || money <= 0.01f) return;
                float total = 0f;
                for (int i = 0; i < pops.Count; i++) { var p = pops[i]; if (p != null && p.Profession == profession) total += p.Size; }
                if (total < 0.5f) return;
                // 记录为"本月分红"(摊到每天计入收入, 与投资池分红同口径)
                for (int i = 0; i < pops.Count; i++)
                {
                    var p = pops[i];
                    if (p == null || p.Profession != profession || p.Size < 0.5f) continue;
                    p.DividendMonthly += money / total;
                }
            }
            catch { }
        }

        // 没收一处领主建筑(违约讨债用)
        internal static void ConfiscateOneLord()
        {
            try
            {
                foreach (var kv in EconomyWorld.Buildings)
                {
                    var sb = kv.Value;
                    if (sb == null) continue;
                    var s = FindSettlement(kv.Key);
                    if (s == null) continue;
                    for (int i = 0; i < sb.Groups.Count; i++)
                    {
                        var g = sb.Groups[i];
                        if (g == null || g.Count <= 0) continue;
                        if (OwnerOf(g, s) == 1) { g.Owner = 0; return; }
                    }
                }
            }
            catch { }
        }

        internal static void ClearAllCash()
        {
            try
            {
                foreach (var kv in EconomyWorld.Buildings)
                {
                    var sb = kv.Value;
                    if (sb == null) continue;
                    for (int i = 0; i < sb.Groups.Count; i++) if (sb.Groups[i] != null) sb.Groups[i].Cash = 0f;
                }
            }
            catch { }
        }

        // ---- 建筑详情操作(文档 20.7) ----
        internal static string Buy(BuildingGroup g, Settlement s)
        {
            try
            {
                int owner = OwnerOf(g, s);
                if (owner == 0) return "已是王室产业";
                var def = BuildDefs.Get(g.DefId);
                if (def == null) return "未知建筑";
                int cost = (int)(def.Work[0] * 8f * 0.9f);
                if (EconomyWorld.Treasury.Gold < cost) return "国库不足(需 " + cost.ToString("N0") + ")";
                EconomyWorld.TreasurySpend(cost);
                g.Owner = 0;
                return "已赎买为王室产业(花费 " + cost.ToString("N0") + ")";
            }
            catch { return "赎买失败"; }
        }

        internal static string Sell(BuildingGroup g, Settlement s)
        {
            try
            {
                if (OwnerOf(g, s) != 0) return "只能出售王室产业";
                var def = BuildDefs.Get(g.DefId);
                if (def == null) return "未知建筑";
                int gain = (int)(def.Work[0] * 8f * 0.6f);
                EconomyWorld.TreasuryAdd(gain);
                g.Owner = s != null && s.IsTown ? 1 : 1;   // 出售给领主(简化)
                return "已出售给领主(得 " + gain.ToString("N0") + ")";
            }
            catch { return "出售失败"; }
        }

        internal static string Confiscate(BuildingGroup g, Settlement s, int today)
        {
            try
            {
                if (OwnerOf(g, s) == 0) return "已是王室产业";
                string clanId = s != null && s.OwnerClan != null ? s.OwnerClan.StringId : "?";
                g.Owner = 0;
                ConfiscateDay[clanId] = today;
                int fav;
                LordFavor.TryGetValue(clanId, out fav);
                LordFavor[clanId] = fav - 25;
                Pops.ShiftRadicals(0.005f);
                Politics.OnConfiscate(clanId);   // 第 21 章: 没收 -> 领主愤怒/暴政
                return "已没收(该领主好感 -25)";   // v4.4: 每年的没收上限已删
            }
            catch { return "没收失败"; }
        }

        internal static int FavorOf(string clanId)
        {
            int v;
            return clanId != null && LordFavor.TryGetValue(clanId, out v) ? v : 0;
        }

        // ---- 存档 FIA_Own ----
        internal static string Save()
        {
            var sb = new StringBuilder();
            sb.Append("v1;");
            sb.Append(F((int)LordPool)).Append(',').Append(F((int)ChurchPool)).Append(',').Append(F((int)GuildFund)).Append(';');
            foreach (var kv in LordFavor)
            {
                if (kv.Value == 0) continue;
                sb.Append(kv.Key).Append(',').Append(kv.Value).Append(';');
            }
            return sb.ToString();
        }

        private static string F(int v) { return v.ToString(CultureInfo.InvariantCulture); }

        internal static void Load(string data)
        {
            try
            {
                LordFavor.Clear();
                if (string.IsNullOrEmpty(data)) return;
                var p = data.Split(';');
                if (p.Length >= 2)
                {
                    var f = p[1].Split(',');
                    float v;
                    if (f.Length > 0 && float.TryParse(f[0], NumberStyles.Float, CultureInfo.InvariantCulture, out v)) LordPool = v;
                    if (f.Length > 1 && float.TryParse(f[1], NumberStyles.Float, CultureInfo.InvariantCulture, out v)) ChurchPool = v;
                    if (f.Length > 2 && float.TryParse(f[2], NumberStyles.Float, CultureInfo.InvariantCulture, out v)) GuildFund = v;
                }
                for (int i = 2; i < p.Length; i++)
                {
                    var f = p[i].Split(',');
                    int v;
                    if (f.Length >= 2 && int.TryParse(f[1], out v)) LordFavor[f[0]] = v;
                }
                DLog.Force("所有权: 读档 领主池=" + ((int)LordPool) + " 教会池=" + ((int)ChurchPool) + " 行会基金=" + ((int)GuildFund));
            }
            catch { }
        }

        private static Settlement FindSettlement(string sid)
        {
            try
            {
                foreach (var s in Settlement.All) if (s != null && s.StringId == sid) return s;
            }
            catch { }
            return null;
        }
    }
}
