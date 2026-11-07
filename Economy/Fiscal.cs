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
        // v4.251: 建筑维护费(玩家国, 每栋每日; 用户要求"完全不管经济就该崩盘" —— 原来建筑只吃材料不花钱)
        internal static int Upkeep, LastUpkeep;
        internal static void AddUpkeep(int v) { if (v > 0) Upkeep += v; }

        // v4.268: 海军维护费(玩家国, 每舰每日; 由 NavalFleet.DailyUpkeep 算出, NavalBehavior 日结时入账)
        //   与军费(Military)/建筑维护(Upkeep)并列的第三类经常性支出
        internal static int Navy, LastNavy;
        internal static void AddNavy(int v) { if (v > 0) Navy += v; }

        // v4.57: 宫廷与外交往来(净开销: 正=支出(宴会/赏赐/赔款/购买等), 负=收入(赔款/贡金/保释金等))
        // ==================== v4.278(追加审计 1): 拆成"只增"的 Paid / Got 两侧 ====================
        //   【原 bug】Court 是**净额**科目, 而 TodayIncome / TodayExpense 各按符号取一侧 —— 两边都取了。
        //   反例(实机可复现): 今天只点一次[出击]拿到 500 战利品 -> AddCourt(-500) ->
        //     TodayIncome  += -TodayCourt = +500        (对)
        //     TodayExpense +=  TodayCourt = -500        (★ 收入被同时算成"负支出")
        //   UI(FiscalVM:182/205)按正负分支渲染, 于是屏幕**同时**显示"收入 +500 / 支出 +500",
        //   月度 Income()/Expense() 同样虚高(Net 因逐项相减碰巧还对, 所以一直没被发现)。
        //   【修法】把科目拆成两个**只增不减**的字段:
        //     · CourtPaid = 只累计支出(宴会/赏赐/赔款支出/购船/封锁调动费/海运补给费...)
        //     · CourtGot  = 只累计收入(战利品/封锁战果/退款/赔款收入...)
        //   聚合里各取一侧 ⇒ "收入 − 支出 = 国库变化"严格成立(不再有同一笔钱被两边各算一次)。
        //   【兼容】AddCourt(int) 保留原签名与**正负语义**(正=支出, 负=收入), 内部按符号分流;
        //     新增 AddCourtPaid / AddCourtGot 供新代码显式选边(不依赖符号, 更难写错)。
        //   【对外符号】Court(净值 = Paid − Got) / LastCourt / TodayCourt 全部**保持原样**,
        //     所以 FiscalVM:182/205 与 NationalPanelVM:474 一处都不用改, 老档也没有新增落盘字段。
        internal static int CourtPaid, CourtGot;
        internal static int LastCourtPaid, LastCourtGot;
        internal static void AddCourtPaid(int v) { if (v > 0) CourtPaid += v; }
        internal static void AddCourtGot(int v) { if (v > 0) CourtGot += v; }
        internal static void AddCourt(int v)
        {
            if (v > 0) CourtPaid += v;
            else if (v < 0) CourtGot += -v;
        }
        internal static int Court { get { return CourtPaid - CourtGot; } }
        internal static int LastCourt { get { return LastCourtPaid - LastCourtGot; } }

        // ---- 日口径(财政页显示"今日"用; 每日日结后由 DayRoll 记录基线) ----
        private static int SnapMint, SnapExport, SnapDividend, SnapTariff, SnapTax, SnapBurn, SnapInterest, SnapFee, SnapMilitary, SnapCourtPaid, SnapCourtGot, SnapUpkeep, SnapNavy;

        internal static void DayRoll()
        {
            SnapMint = Mint; SnapExport = Export; SnapDividend = Dividend; SnapTariff = Tariff;
            SnapTax = Tax; SnapBurn = Burn; SnapInterest = Interest; SnapFee = Fee; SnapMilitary = Military;
            SnapCourtPaid = CourtPaid; SnapCourtGot = CourtGot;   // v4.278: 两侧各自快照(净额由两者相减得到)
            SnapUpkeep = Upkeep;
            SnapNavy = Navy;
        }

        // v4.272: 修"今日值"在**可正可负**科目上的失效(审计 A8)。
        //   原实现 `cur >= snap ? cur - snap : cur` 是为"只增不减"的科目写的: 那种科目唯一会
        //   出现 cur < snap 的场合就是**月度归零**(Month() 把累计清零, 而日基线还停在上月末的
        //   大数), 此时返回 cur(= 本月累计)恰好等于"今日新增", 是个巧合的正确。
        //   但 Court(宫廷与外交)是**净额科目**: AddCourt 收正负两种值(购船/宴会=支出, 赔款/战利品=收入),
        //   Court 为负时 cur < snap 恒成立 -> 返回 cur(本月累计的净收入) 被当成"今日收入",
        //   当日只要有收入就把**整个月**的战利品/赔款重复计一次 -> "今日收入"虚增。
        //   海军每天稳定触发该路径(封锁收益 NavalBehavior.BlockadeDaily / 海战战利品 NavalBattle)。
        //   改成纯差值后: 只增不减科目在正常游戏里的结果与旧式完全一致(它们不会自发减少),
        //   而月度归零那条边路由 Month() 末尾的 DayRoll() 兜住(snap 同步归零, 差值为正), 不会出现负数。
        private static int TodayOf(int cur, int snap) { return cur - snap; }

        internal static int TodayMint { get { return TodayOf(Mint, SnapMint); } }
        internal static int TodayExport { get { return TodayOf(Export, SnapExport); } }
        internal static int TodayDividend { get { return TodayOf(Dividend, SnapDividend); } }
        internal static int TodayTariff { get { return TodayOf(Tariff, SnapTariff); } }
        internal static int TodayTax { get { return TodayOf(Tax, SnapTax); } }
        internal static int TodayBurn { get { return TodayOf(Burn, SnapBurn); } }
        internal static int TodayInterest { get { return TodayOf(Interest, SnapInterest); } }
        internal static int TodayFee { get { return TodayOf(Fee, SnapFee); } }
        internal static int TodayMilitary { get { return TodayOf(Military, SnapMilitary); } }
        // v4.278: 净额(保持原符号约定: 正=净支出, 负=净收入) + **两侧只增口径**给聚合用
        internal static int TodayCourt { get { return TodayOf(CourtPaid - CourtGot, SnapCourtPaid - SnapCourtGot); } }
        internal static int TodayCourtPaid { get { return TodayOf(CourtPaid, SnapCourtPaid); } }
        internal static int TodayCourtGot { get { return TodayOf(CourtGot, SnapCourtGot); } }
        internal static int TodayUpkeep { get { return TodayOf(Upkeep, SnapUpkeep); } }
        internal static int TodayNavy { get { return TodayOf(Navy, SnapNavy); } }

        // ★ 严格恒等式: (TodayIncome − TodayExpense) == 国库今日实际变动中"由本账本记录的"那一部分。
        //   关键 = Court 只按**各自那一侧**计入, 不再两边各算一次(见 CourtPaid/CourtGot 的长注释)。
        internal static int TodayIncome { get { return TodayMint + TodayExport + TodayDividend + TodayTax + TodayCourtGot; } }
        internal static int TodayExpense { get { return TodayTariff + TodayBurn + TodayInterest + TodayFee + TodayMilitary + TodayUpkeep + TodayNavy + TodayCourtPaid; } }

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
            LastInterest = Interest; LastFee = Fee; LastMilitary = Military; LastUpkeep = Upkeep;
            // v4.278: Court 的月度结转也拆成两侧(LastCourt / LastCourtPaid / LastCourtGot 都是只读属性)
            LastCourtPaid = CourtPaid; LastCourtGot = CourtGot;
            LastNavy = Navy;   // v4.268
            Mint = Export = Dividend = Tariff = Tax = Burn = Interest = Fee = Military = 0;
            Upkeep = 0;
            Navy = 0;
            CourtPaid = 0; CourtGot = 0;
            DayRoll();   // 月重置后日基线同步归零
            DLog.Force("财政月结: 铸币=" + LastMint + " 出口=" + LastExport + " 分红=" + LastDividend
                + " 税收=" + LastTax + " 销毁=" + LastBurn + " 关税=" + LastTariff
                + " 利息=" + LastInterest + " 年金=" + LastFee + " 军费=" + LastMilitary + " 建筑维护=" + LastUpkeep
                + " 海军维护=" + LastNavy + " 外交往来支出=" + LastCourtPaid + " 外交往来收入=" + LastCourtGot
                + " 净=" + Net);
        }

        // v4.278(追加审计 1): 月度口径与今日口径同源 —— Court 只在各自那一侧计一次。
        internal static int Income { get { return LastMint + LastExport + LastDividend + LastTax + LastCourtGot; } }
        internal static int Expense { get { return LastTariff + LastBurn + LastInterest + LastFee + LastMilitary + LastUpkeep + LastNavy + LastCourtPaid; } }
        internal static int Net { get { return Income - Expense; } }
    }
}
