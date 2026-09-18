using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;

namespace FeudalInternalAffairs
{
    // 税制档位小格(五格选择器; v4.8)
    public class PipVM : ViewModel
    {
        private string _color = "#FFFFFF2E";
        [DataSourceProperty] public string Color { get { return _color; } }
        internal void Set(string c)
        {
            if (_color == c) return;
            _color = c;
            OnPropertyChangedWithValue(c, "Color");
        }
    }

    // 财政页一行(收入/支出条目)
    public class FiscalRowVM : ViewModel
    {
        private readonly string _name, _value, _color;
        internal FiscalRowVM(string name, int amount, bool income)
        {
            _name = name;
            _value = (income ? "+" : "-") + Math.Abs(amount).ToString("N0");
            _color = income ? "#39FF14FF" : "#D96A5AFF";
        }
        [DataSourceProperty] public string Name { get { return _name; } }
        [DataSourceProperty] public string Value { get { return _value; } }
        [DataSourceProperty] public string Color { get { return _color; } }
    }

    // 财政面板 VM(文档 19.14.4)
    public class FiscalPanelVM : PanelVMBase
    {
        private readonly Action _onClose;
        private string _treasury = "", _net = "", _netColor = "", _incomeSum = "", _expenseSum = "";
        private string _poolText = "", _poolSubText = "", _poolSub2Text = "", _ledgerText = "";
        private string _taxA = "", _taxB = "", _taxC = "", _taxD = "", _taxEffect = "", _creditText = "", _mintText = "", _mintSubText = "", _status = "";
        private string _taxLA = "", _taxLB = "", _taxLC = "", _taxLD = "";
        // v4.7 悬停光带
        private float _hoverY;
        private bool _hoverVisible;

        [DataSourceProperty] public float HoverY { get { return _hoverY; } }
        [DataSourceProperty] public bool HoverVisible { get { return _hoverVisible; } }

        // idx: 0~3 = 四税行, 4 = 铸币权行, -1 = 无
        internal void SetHoverRow(int idx)
        {
            try
            {
                float y = idx < 0 ? _hoverY : (idx < 4 ? 632f + idx * 32f : 840f);
                bool vis = idx >= 0;
                if (Math.Abs(_hoverY - y) > 0.5f) { _hoverY = y; OnPropertyChangedWithValue(y, "HoverY"); }
                if (_hoverVisible != vis) { _hoverVisible = vis; OnPropertyChangedWithValue(vis, "HoverVisible"); }
            }
            catch { }
        }

        public FiscalPanelVM(Action onClose)
        {
            _onClose = onClose;
            IncomeRows = new MBBindingList<FiscalRowVM>();
            ExpenseRows = new MBBindingList<FiscalRowVM>();
            PipsA = new MBBindingList<PipVM>();
            PipsB = new MBBindingList<PipVM>();
            PipsC = new MBBindingList<PipVM>();
            PipsD = new MBBindingList<PipVM>();
            PipsM = new MBBindingList<PipVM>();
            Refresh();
        }

        // 铸币成色三格(足银/九成/七成; v4.11)
        public MBBindingList<PipVM> PipsM { get; private set; }

        internal void SetMintPurity(int level)
        {
            try { _status = MintRight.SetPurity(level); Refresh(); }
            catch { }
        }

        // v4.8 五格档位选择器
        public MBBindingList<PipVM> PipsA { get; private set; }
        public MBBindingList<PipVM> PipsB { get; private set; }
        public MBBindingList<PipVM> PipsC { get; private set; }
        public MBBindingList<PipVM> PipsD { get; private set; }

        private void FillPips(MBBindingList<PipVM> l, int kind)
        {
            for (int i = 0; i < 5; i++)
            {
                if (l.Count <= i) l.Add(new PipVM());
                bool on = TaxPolicy.Level[kind] == i;
                l[i].Set(on ? "#39FF14FF" : "#6B6152DD");   // 当前档=荧光绿实心块, 其余=暗格
            }
        }

        internal void SetTaxLevel(int kind, int level)
        {
            try { _status = TaxPolicy.SetLevel(kind, level, Today()); Refresh(); }
            catch { }
        }

        public MBBindingList<FiscalRowVM> IncomeRows { get; private set; }
        public MBBindingList<FiscalRowVM> ExpenseRows { get; private set; }

        [DataSourceProperty] public string Treasury { get { return _treasury; } }
        [DataSourceProperty] public string NetText { get { return _net; } }
        [DataSourceProperty] public string NetColor { get { return _netColor; } }
        [DataSourceProperty] public string IncomeSum { get { return _incomeSum; } }
        [DataSourceProperty] public string ExpenseSum { get { return _expenseSum; } }
        [DataSourceProperty] public string PoolText { get { return _poolText; } }
        [DataSourceProperty] public string PoolSubText { get { return _poolSubText; } }
        [DataSourceProperty] public string PoolSubText2 { get { return _poolSub2Text; } }
        [DataSourceProperty] public string LedgerText { get { return _ledgerText; } }
        [DataSourceProperty] public string TaxA { get { return _taxA; } }
        [DataSourceProperty] public string TaxB { get { return _taxB; } }
        [DataSourceProperty] public string TaxC { get { return _taxC; } }
        [DataSourceProperty] public string TaxD { get { return _taxD; } }
        [DataSourceProperty] public string TaxEffect { get { return _taxEffect; } }
        [DataSourceProperty] public string TaxLA { get { return _taxLA; } }
        [DataSourceProperty] public string TaxLB { get { return _taxLB; } }
        [DataSourceProperty] public string TaxLC { get { return _taxLC; } }
        [DataSourceProperty] public string TaxLD { get { return _taxLD; } }
        [DataSourceProperty] public string CreditText { get { return _creditText; } }
        [DataSourceProperty] public string MintText { get { return _mintText; } }
        [DataSourceProperty] public string MintSubText { get { return _mintSubText; } }
        [DataSourceProperty] public string StatusText { get { return _status; } }

        public void ExecuteClose() { if (_onClose != null) _onClose(); }

        // v4.0: 点击税种切档 / 切铸币成色
        internal void TaxClick(int kind)
        {
            try { _status = TaxPolicy.Cycle(kind, Today()); Refresh(); }
            catch { }
        }

        internal void MintClick()
        {
            try { _status = MintRight.Cycle(Today()); Refresh(); }
            catch { }
        }

        private static int Today()
        {
            try { return (int)CampaignTime.Now.ToDays; } catch { return 0; }
        }

        internal void Refresh()
        {
            try
            {
                IncomeRows.Clear();
                ExpenseRows.Clear();
                _treasury = ((int)EconomyWorld.Treasury.Gold).ToString("N0");
                int inc = Fiscal.Mint + Fiscal.Export + Fiscal.Dividend + Fiscal.Tax, exp = Fiscal.Tariff + Fiscal.Burn;
                _incomeSum = "+" + inc.ToString("N0");
                _expenseSum = exp == 0 ? "0" : "-" + exp.ToString("N0");
                int net = inc - exp;
                _net = (net >= 0 ? "+" : "") + net.ToString("N0");
                _netColor = net >= 0 ? "#39FF14FF" : "#D96A5AFF";

                // 显示"本月累计"(上月的滚存见顶部"上月净额")
                if (Fiscal.Tax != 0) IncomeRows.Add(new FiscalRowVM("税收(土地/人头/什一/市场)", Fiscal.Tax, true));
                if (Fiscal.Mint != 0) IncomeRows.Add(new FiscalRowVM("铸币收入(市场净创造)", Fiscal.Mint, true));
                if (Fiscal.Export != 0) IncomeRows.Add(new FiscalRowVM("出口收入(贸易路线)", Fiscal.Export, true));
                if (Fiscal.Dividend != 0) IncomeRows.Add(new FiscalRowVM("国有建筑分红", Fiscal.Dividend, true));
                if (IncomeRows.Count == 0) IncomeRows.Add(new FiscalRowVM("本月暂无收入", 0, true));

                if (Fiscal.Burn != 0) ExpenseRows.Add(new FiscalRowVM("铸币销毁(本国市场×20%)", Fiscal.Burn, false));
                if (Fiscal.Tariff != 0) ExpenseRows.Add(new FiscalRowVM("进口关税(贸易路线)", Fiscal.Tariff, false));
                if (ExpenseRows.Count == 0) ExpenseRows.Add(new FiscalRowVM("本月暂无支出", 0, false));

                // 投资池(V3 财政窗口同款: 独立于国库的第二本账, 分红注入 -> 私人建造)
                var k = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                string kid = k != null ? k.StringId : null;
                _poolText = "本国投资池: " + ((int)InvestmentPool.Of(kid)).ToString("N0") + " 第纳尔";
                _poolSubText = "本月私人建造 " + InvestmentPool.MonthBuilt + " 次 · 累计建成 " + InvestmentPool.TotalBuilt + " 次";
                _poolSub2Text = "领主私产 " + ((int)Ownership.LordPool) + " · 教会池 " + ((int)Ownership.ChurchPool)
                    + " · 行会基金 " + ((int)Ownership.GuildFund);
                _ledgerText = "铸币账本(今日/累计): " + ((int)MarketSim.LedgerToday) + " / " + ((int)MarketSim.LedgerTotal);

                // v4.0: 税制 / 信贷 / 铸币权(文档 20.1~20.3); v4.3: 档位值单列(绿色)
                _taxA = TaxPolicy.LabelOf(0);
                _taxB = TaxPolicy.LabelOf(1);
                _taxC = TaxPolicy.LabelOf(2);
                _taxD = TaxPolicy.LabelOf(3);
                _taxLA = TaxPolicy.LevelOf(0);
                _taxLB = TaxPolicy.LevelOf(1);
                _taxLC = TaxPolicy.LevelOf(2);
                _taxLD = TaxPolicy.LevelOf(3);
                _taxEffect = TaxPolicy.EffectText();
                _creditText = "信贷: " + Credit.StatusText() + (Credit.DailyInterest > 0 ? " · 今日利息 " + Credit.DailyInterest : "");
                _mintText = MintRight.StatusText();
                _mintSubText = MintRight.RateText();

                // 汇总数字必须发通知(否则面板只会在打开瞬间取一次值, 看起来"净收入永远是0")
                OnPropertyChangedWithValue(_treasury, "Treasury");
                OnPropertyChangedWithValue(_net, "NetText");
                OnPropertyChangedWithValue(_netColor, "NetColor");
                OnPropertyChangedWithValue(_incomeSum, "IncomeSum");
                OnPropertyChangedWithValue(_expenseSum, "ExpenseSum");
                OnPropertyChangedWithValue(_poolText, "PoolText");
                OnPropertyChangedWithValue(_poolSubText, "PoolSubText");
                OnPropertyChangedWithValue(_poolSub2Text, "PoolSubText2");
                OnPropertyChangedWithValue(_ledgerText, "LedgerText");
                OnPropertyChangedWithValue(_taxA, "TaxA");
                OnPropertyChangedWithValue(_taxB, "TaxB");
                OnPropertyChangedWithValue(_taxC, "TaxC");
                OnPropertyChangedWithValue(_taxD, "TaxD");
                OnPropertyChangedWithValue(_taxLA, "TaxLA");
                OnPropertyChangedWithValue(_taxLB, "TaxLB");
                OnPropertyChangedWithValue(_taxLC, "TaxLC");
                OnPropertyChangedWithValue(_taxLD, "TaxLD");
                FillPips(PipsA, 0);
                FillPips(PipsB, 1);
                FillPips(PipsC, 2);
                FillPips(PipsD, 3);
                for (int i = 0; i < 3; i++)
                {
                    if (PipsM.Count <= i) PipsM.Add(new PipVM());
                    PipsM[i].Set(MintRight.Purity == i ? "#39FF14FF" : "#6B6152DD");
                }
                OnPropertyChangedWithValue(_taxEffect, "TaxEffect");
                OnPropertyChangedWithValue(_creditText, "CreditText");
                OnPropertyChangedWithValue(_mintText, "MintText");
                OnPropertyChangedWithValue(_mintSubText, "MintSubText");
                OnPropertyChangedWithValue(_status, "StatusText");
            }
            catch (Exception ex) { DLog.Force("财政页刷新失败: " + ex.Message); }
        }
    }
}
