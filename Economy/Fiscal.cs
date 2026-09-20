using System;

namespace FeudalInternalAffairs
{
    // 财政账本(月度累计; 文档 19.14.4 财政页)
    // 数据来源: 铸币(MarketSim.LedgerToday) / 出口收入 / 国有分红 / 进口关税
    internal static class Fiscal
    {
        internal static int Mint, Export, Dividend, Tariff, Tax, Burn;              // 本月累计
        internal static int LastMint, LastExport, LastDividend, LastTariff, LastTax, LastBurn;  // 上月(界面显示用)
        internal static int Interest, Fee;                                          // v4.0: 利息 / 行会年金(支出)
        internal static int LastInterest, LastFee;
        internal static int Military;                                               // v4.53: 国防军军费(募兵/建军/军饷/军粮工具)
        internal static int LastMilitary;

        // v4.57: 宫廷与外交往来(净开销: 正=支出(宴会/赏赐/赔款/购买等), 负=收入(赔款/贡金/保释金等))
        internal static int Court, LastCourt;
        internal static void AddCourt(int v) { Court += v; }

        // ---- 日口径(财政页显示"今日"用; 每日日结后由 DayRoll 记录基线) ----
        private static int SnapMint, SnapExport, SnapDividend, SnapTariff, SnapTax, SnapBurn, SnapInterest, SnapFee, SnapMilitary, SnapCourt;

        internal static void DayRoll()
        {
            SnapMint = Mint; SnapExport = Export; SnapDividend = Dividend; SnapTariff = Tariff;
            SnapTax = Tax; SnapBurn = Burn; SnapInterest = Interest; SnapFee = Fee; SnapMilitary = Military; SnapCourt = Court;
        }

        private static int TodayOf(int cur, int snap) { return cur >= snap ? cur - snap : cur; }

        internal static int TodayMint { get { return TodayOf(Mint, SnapMint); } }
        internal static int TodayExport { get { return TodayOf(Export, SnapExport); } }
        internal static int TodayDividend { get { return TodayOf(Dividend, SnapDividend); } }
        internal static int TodayTariff { get { return TodayOf(Tariff, SnapTariff); } }
        internal static int TodayTax { get { return TodayOf(Tax, SnapTax); } }
        internal static int TodayBurn { get { return TodayOf(Burn, SnapBurn); } }
        internal static int TodayInterest { get { return TodayOf(Interest, SnapInterest); } }
        internal static int TodayFee { get { return TodayOf(Fee, SnapFee); } }
        internal static int TodayMilitary { get { return TodayOf(Military, SnapMilitary); } }
        internal static int TodayCourt { get { return TodayOf(Court, SnapCourt); } }

        internal static int TodayIncome { get { return TodayMint + TodayExport + TodayDividend + TodayTax + (TodayCourt < 0 ? -TodayCourt : 0); } }
        internal static int TodayExpense { get { return TodayTariff + TodayBurn + TodayInterest + TodayFee + TodayMilitary + (TodayCourt > 0 ? TodayCourt : 0); } }

        // 国库日末基线: "今日净额"直接用国库实际变动(含宴会/赏赐/赔款等未入主账的收支)
        private static int _goldBase = int.MinValue;
        internal static void EndOfDayGold(int gold) { _goldBase = gold; }
        internal static int TodayGoldDelta(int curGold)
        {
            if (_goldBase == int.MinValue) return 0;
            return curGold - _goldBase;
        }

        internal static void AddMint(int v) { if (v > 0) Mint += v; }   // 铸币只计"净创造"为正的收入
        internal static void AddBurn(int v) { if (v > 0) Burn += v; }   // 市场净销毁(买>卖) -> 计入支出
        internal static void AddExport(int v) { if (v > 0) Export += v; }
        internal static void AddDividend(int v) { if (v > 0) Dividend += v; }
        internal static void AddTariff(int v) { if (v > 0) Tariff += v; }
        internal static void AddTax(int v) { if (v > 0) Tax += v; }
        internal static void AddInterest(int v) { if (v > 0) Interest += v; }
        internal static void AddFee(int v) { if (v > 0) Fee += v; }
        internal static void AddMilitary(int v) { if (v > 0) Military += v; }

        internal static void Month()
        {
            LastMint = Mint; LastExport = Export; LastDividend = Dividend; LastTariff = Tariff; LastTax = Tax; LastBurn = Burn;
            LastInterest = Interest; LastFee = Fee; LastMilitary = Military; LastCourt = Court;
            Mint = Export = Dividend = Tariff = Tax = Burn = Interest = Fee = Military = 0;
            Court = 0;
            DayRoll();   // 月重置后日基线同步归零
            DLog.Force("财政月结: 铸币=" + LastMint + " 出口=" + LastExport + " 分红=" + LastDividend
                + " 税收=" + LastTax + " 销毁=" + LastBurn + " 关税=" + LastTariff
                + " 利息=" + LastInterest + " 年金=" + LastFee + " 军费=" + LastMilitary + " 净=" + Net);
        }

        internal static int Income { get { return LastMint + LastExport + LastDividend + LastTax + (LastCourt < 0 ? -LastCourt : 0); } }
        internal static int Expense { get { return LastTariff + LastBurn + LastInterest + LastFee + LastMilitary + (LastCourt > 0 ? LastCourt : 0); } }
        internal static int Net { get { return Income - Expense; } }
    }
}
