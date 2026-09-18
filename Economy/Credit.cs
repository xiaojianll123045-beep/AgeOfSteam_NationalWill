using System;
using TaleWorlds.CampaignSystem;

namespace FeudalInternalAffairs
{
    // 钱庄与信贷(文档 20.2): 国库负债 = -Gold; 利息/违约/破产
    internal static class Credit
    {
        internal static int CleanDays;      // 连续履约天数(降息)
        internal static int OverdueDays;    // 超信用额度天数
        internal static int BankruptCount;

        internal static int Debt
        {
            get { try { return (int)Math.Max(0f, -EconomyWorld.Treasury.Gold); } catch { return 0; } }
        }

        // 月利率 3%~12%: 履约每 30 天 -1%, 破产史 +2%
        internal static float MonthlyRate
        {
            get
            {
                float r = 0.06f - 0.01f * (CleanDays / 30);
                r += 0.02f * BankruptCount;
                if (r < 0.03f) r = 0.03f;
                if (r > 0.12f) r = 0.12f;
                return r;
            }
        }

        // 信用额度 = 500 + 建筑现金储备×0.5 + 离库税收×6 (+ 钱庄每级 200)
        internal static int Limit
        {
            get
            {
                try
                {
                    float reserves = 0f, banks = 0f;
                    var pk = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                    foreach (var kv in EconomyWorld.Buildings)
                    {
                        var sb = kv.Value;
                        if (sb == null) continue;
                        bool mine = false;
                        foreach (var s in TaleWorlds.CampaignSystem.Settlements.Settlement.All)
                            if (s != null && s.StringId == kv.Key) { mine = s.MapFaction == pk; break; }
                        if (!mine) continue;
                        for (int i = 0; i < sb.Groups.Count; i++)
                        {
                            var g = sb.Groups[i];
                            if (g == null) continue;
                            if (g.Cash > 0f) reserves += g.Cash;
                            if (g.DefId == "bank") banks += g.Count * 200f;
                        }
                    }
                    float monthTax = Math.Max(Fiscal.Tax, Fiscal.LastTax);
                    return (int)(500f + reserves * 0.5f + monthTax * 6f + banks);
                }
                catch { return 500; }
            }
        }

        internal static int DailyInterest;
        internal static bool InDefault { get { return Debt > 0 && Debt > Limit; } }
        internal static int DefaultWarnDays { get { return InDefault ? Math.Max(0, 7 - OverdueDays) : -1; } }

        internal static void Daily()
        {
            try
            {
                int debt = Debt;
                DailyInterest = 0;
                if (debt > 0)
                {
                    // 利息
                    int interest = (int)(debt * MonthlyRate / 84f);
                    if (interest > 0)
                    {
                        EconomyWorld.TreasurySpend(interest);
                        Fiscal.AddInterest(interest);
                        DailyInterest = interest;
                    }
                    if (debt > Limit)
                    {
                        OverdueDays++;
                        if (OverdueDays == 7)
                        {
                            // 领主讨债: 强制清 30% 债 + 没收一处领主建筑
                            int bail = (int)(debt * 0.3f);
                            if (bail > 0) EconomyWorld.TreasuryAdd(bail);
                            Ownership.ConfiscateOneLord();
                            Pops.ShiftRadicals(0.03f);
                            DLog.Force("信贷: 领主讨债 清债 " + bail + " 并没收一处领主建筑");
                        }
                        else if (OverdueDays >= 14)
                        {
                            // 破产: 清债; 激进飙升; 建筑储备清零; 铸币权降档
                            int d2 = Debt;
                            if (d2 > 0) EconomyWorld.TreasuryAdd(d2);
                            BankruptCount++;
                            CleanDays = 0;
                            Pops.ShiftRadicals(0.10f);
                            Ownership.ClearAllCash();
                            MintRight.Downgrade();
                            Politics.OnBankruptcy();   // 第 21 章: 破产 -> 领主愤怒/合法性
                            DLog.Force("信贷: 破产! 清除债务 " + d2 + " 第 " + BankruptCount + " 次");
                            OverdueDays = 0;
                        }
                    }
                    else { OverdueDays = 0; CleanDays++; }
                }
                else { OverdueDays = 0; CleanDays++; }
            }
            catch (Exception ex) { DLog.Force("信贷结算异常: " + ex.Message); }
        }

        internal static string StatusText()
        {
            try
            {
                int debt = Debt;
                string s = "信用额度 " + Limit.ToString("N0") + " · 负债 " + debt.ToString("N0")
                    + " · 月利率 " + (MonthlyRate * 100f).ToString("F1") + "%";
                if (InDefault) s += " · 违约倒计时 " + DefaultWarnDays + " 天";
                return s;
            }
            catch { return ""; }
        }

        // ---- 存档 FIA_Credit ----
        internal static string Save()
        {
            return "v1;" + CleanDays + "," + OverdueDays + "," + BankruptCount + ";";
        }

        internal static void Load(string data)
        {
            try
            {
                if (string.IsNullOrEmpty(data)) return;
                var p = data.Split(';');
                if (p.Length >= 2)
                {
                    var f = p[1].Split(',');
                    int v;
                    if (f.Length > 0 && int.TryParse(f[0], out v)) CleanDays = v;
                    if (f.Length > 1 && int.TryParse(f[1], out v)) OverdueDays = v;
                    if (f.Length > 2 && int.TryParse(f[2], out v)) BankruptCount = v;
                }
            }
            catch { }
        }
    }
}
