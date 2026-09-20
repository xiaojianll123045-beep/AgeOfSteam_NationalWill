using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;

namespace FeudalInternalAffairs
{
    // 铸币权(文档 20.3): 银矿 + 铸币厂 -> 铸币收入; 成色越低收入越高但物价涨、民心差
    internal static class MintRight
    {
        internal static int Purity;                                    // 0 足银 / 1 九成 / 2 七成
        internal static readonly string[] PurityNames = { "足银", "九成", "七成" };
        internal static readonly float[] IncomeMult = { 1f, 1.4f, 1.9f };
        internal static readonly float[] DailyInfl = { 0f, 0.0007f, 0.0017f };

        internal static int DailyIncome;
        internal static int PurityDay = -9999;                         // 上次改成色(无冷却; v4.4)
        internal const int PurityCooldown = 0;

        internal static float DailyInflation { get { return DailyInfl[Purity < 0 ? 0 : (Purity > 2 ? 2 : Purity)]; } }

        // 玩家是否有铸币厂(有则接管铸币口径)
        internal static bool HasMint
        {
            get { return MintLevels() > 0; }
        }

        internal static int MintLevels()
        {
            int n = 0;
            try
            {
                var pk = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                if (pk == null) return 0;
                foreach (var s in pk.Settlements)
                {
                    if (s == null) continue;
                    var sb = EconomyWorld.Find(s.StringId);
                    if (sb == null) continue;
                    for (int i = 0; i < sb.Groups.Count; i++)
                    {
                        var g = sb.Groups[i];
                        if (g != null && g.DefId == "mint" && g.Count > 0) n += g.Count;
                    }
                }
            }
            catch { }
            return n;
        }

        // 日结算: 铸币收入 = min(本国银矿日产×0.8, 铸币厂等级×3) × 成色
        internal static int Daily()
        {
            DailyIncome = 0;
            try
            {
                int levels = MintLevels();
                if (levels <= 0) return 0;
                float silver = 0f;
                var pk = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                if (pk == null) return 0;
                foreach (var kv in EconomyWorld.Markets)
                {
                    var m = kv.Value;
                    if (m == null) continue;
                    bool mine = false;
                    var s = FindSettlement(kv.Key);
                    if (s != null) mine = s.MapFaction == pk;
                    if (!mine) continue;
                    var e = m.Get(FeudalGoods.Silver);
                    if (e != null) silver += e.DailyProduction;
                }
                int v = (int)Math.Round(Math.Min(silver * 0.8f, levels * 3f) * IncomeMult[Purity]);
                if (v > 0)
                {
                    EconomyWorld.TreasuryAdd(v);
                    Fiscal.AddMint(v);
                    DailyIncome = v;
                }
            }
            catch (Exception ex) { DLog.Force("铸币结算异常: " + ex.Message); }
            return DailyIncome;
        }

        internal static string Cycle(int today)
        {
            Purity = (Purity + 1) % 3;
            PurityDay = today;
            DLog.Force("铸币权: 成色 -> " + PurityNames[Purity]);
            return "铸币成色: " + PurityNames[Purity];
        }

        internal static void Downgrade()
        {
            if (Purity < 2) Purity++;
        }

        // 直接设成色档(点格; v4.11)
        internal static string SetPurity(int level)
        {
            try
            {
                if (level < 0) level = 0;
                if (level > 2) level = 2;
                Purity = level;
                DLog.Force("铸币权: 成色 -> " + PurityNames[Purity]);
                return "铸币成色: " + PurityNames[Purity];
            }
            catch { return ""; }
        }

        internal static string StatusText()
        {
            return (HasMint ? "铸币厂 " + MintLevels() + " 级" : "无铸币厂")
                + " · 成色 " + PurityNames[Purity];
        }

        internal static string RateText()
        {
            return "今日 +" + DailyIncome.ToString("N0")
                + " · 物价 +" + (DailyInflation * 100f).ToString("F2") + "%/日";
        }

        // ---- 存档 FIA_Mint ----
        internal static string Save() { return "v1;" + Purity + ";" + PurityDay + ";"; }

        internal static void Load(string data)
        {
            try
            {
                if (string.IsNullOrEmpty(data)) return;
                var p = data.Split(';');
                int v;
                if (p.Length >= 2 && int.TryParse(p[1], out v)) Purity = v < 0 ? 0 : (v > 2 ? 2 : v);
                if (p.Length >= 3 && int.TryParse(p[2], out v)) PurityDay = v;
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
