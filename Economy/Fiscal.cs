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

        internal static void AddMint(int v) { if (v > 0) Mint += v; }   // 铸币只计"净创造"为正的收入
        internal static void AddBurn(int v) { if (v > 0) Burn += v; }   // 市场净销毁(买>卖) -> 计入支出
        internal static void AddExport(int v) { if (v > 0) Export += v; }
        internal static void AddDividend(int v) { if (v > 0) Dividend += v; }
        internal static void AddTariff(int v) { if (v > 0) Tariff += v; }
        internal static void AddTax(int v) { if (v > 0) Tax += v; }
        internal static void AddInterest(int v) { if (v > 0) Interest += v; }
        internal static void AddFee(int v) { if (v > 0) Fee += v; }

        internal static void Month()
        {
            LastMint = Mint; LastExport = Export; LastDividend = Dividend; LastTariff = Tariff; LastTax = Tax; LastBurn = Burn;
            LastInterest = Interest; LastFee = Fee;
            Mint = Export = Dividend = Tariff = Tax = Burn = Interest = Fee = 0;
            DLog.Force("财政月结: 铸币=" + LastMint + " 出口=" + LastExport + " 分红=" + LastDividend
                + " 税收=" + LastTax + " 销毁=" + LastBurn + " 关税=" + LastTariff
                + " 利息=" + LastInterest + " 年金=" + LastFee + " 净=" + Net);
        }

        internal static int Income { get { return LastMint + LastExport + LastDividend + LastTax; } }
        internal static int Expense { get { return LastTariff + LastBurn + LastInterest + LastFee; } }
        internal static int Net { get { return Income - Expense; } }
    }
}
