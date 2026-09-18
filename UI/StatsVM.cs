using System;
using TaleWorlds.Library;

namespace FeudalInternalAffairs
{
    // 统计页一行(指标 / 今日 / 7日均 / 30日均)
    public class StatRowVM : ViewModel
    {
        private readonly string _name, _today, _avg7, _avg30;

        internal StatRowVM(string name, float today, float avg7, float avg30)
        {
            _name = name;
            _today = ((int)today).ToString("N0");
            _avg7 = ((int)avg7).ToString("N0");
            _avg30 = ((int)avg30).ToString("N0");
        }

        [DataSourceProperty] public string Name { get { return _name; } }
        [DataSourceProperty] public string Today { get { return _today; } }
        [DataSourceProperty] public string Avg7 { get { return _avg7; } }
        [DataSourceProperty] public string Avg30 { get { return _avg30; } }
    }

    // 统计面板 VM(文档 20.9; 对齐 V3 国家统计)
    public class StatsVM : PanelVMBase
    {
        private readonly Action _onClose;
        private string _title = "", _popLine = "", _priceLine = "";

        public StatsVM(Action onClose)
        {
            _onClose = onClose;
            Rows = new MBBindingList<StatRowVM>();
            Refresh();
        }

        public MBBindingList<StatRowVM> Rows { get; private set; }

        [DataSourceProperty] public string TitleText { get { return _title; } }
        [DataSourceProperty] public string PopLine { get { return _popLine; } }
        [DataSourceProperty] public string PriceLine { get { return _priceLine; } }

        public void ExecuteClose() { if (_onClose != null) _onClose(); }

        internal void Refresh()
        {
            try
            {
                Rows.Clear();
                var last = Stats.Ring.Count > 0 ? Stats.Ring[Stats.Ring.Count - 1] : new Stats.Snapshot();
                _title = "经济统计 · 近 " + Stats.Ring.Count + " 天" + (Stats.Ring.Count == 0 ? "(走过一个游戏日后出数)" : "");
                _popLine = "人口 " + last.Pop.ToString("N0") + " · 在岗 " + PopJob.LastWageTotal.ToString("N0")
                    + "(工资) · 建筑支出 " + PopJob.LastCostTotal.ToString("N0");
                _priceLine = Stats.Ring.Count == 0 ? "物价指数 —(未结算)"
                    : "物价指数 " + last.PriceIndex.ToString("F2") + " · 较基准 " + ((last.PriceIndex - 1f) * 100f).ToString("+0.0;-0.0;0.0") + "%";

                Add("税收", delegate (Stats.Snapshot s) { return s.Tax; }, last.Tax);
                Add("关税(支出)", delegate (Stats.Snapshot s) { return s.Tariff; }, last.Tariff);
                Add("铸币", delegate (Stats.Snapshot s) { return s.Mint; }, last.Mint);
                Add("分红", delegate (Stats.Snapshot s) { return s.Dividend; }, last.Dividend);
                Add("利息(支出)", delegate (Stats.Snapshot s) { return s.Interest; }, last.Interest);
                Add("工资总额", delegate (Stats.Snapshot s) { return s.Wages; }, last.Wages);
                Add("材料与维护", delegate (Stats.Snapshot s) { return s.Materials; }, last.Materials);
                Add("进口件", delegate (Stats.Snapshot s) { return s.Imports; }, last.Imports);
                Add("出口件", delegate (Stats.Snapshot s) { return s.Exports; }, last.Exports);
                Add("人口", delegate (Stats.Snapshot s) { return s.Pop; }, last.Pop);

                // 所有权池(文档 20.4)
                Rows.Add(new StatRowVM("领主私产池", (int)Ownership.LordPool, 0f, 0f));
                Rows.Add(new StatRowVM("教会池", (int)Ownership.ChurchPool, 0f, 0f));
                Rows.Add(new StatRowVM("行会基金", (int)Ownership.GuildFund, 0f, 0f));
                Rows.Add(new StatRowVM("投资池(本国)", (int)InvestmentPool.Of(Kid()), 0f, 0f));

                // 汇总文字必须发通知(否则只停留在打开瞬间的值)
                OnPropertyChangedWithValue(_title, "TitleText");
                OnPropertyChangedWithValue(_popLine, "PopLine");
                OnPropertyChangedWithValue(_priceLine, "PriceLine");
            }
            catch (Exception ex) { DLog.Force("统计页刷新失败: " + ex.Message); }
        }

        private void Add(string name, Func<Stats.Snapshot, float> pick, float today)
        {
            Rows.Add(new StatRowVM(name, today, Stats.Avg(pick, 7), Stats.Avg(pick, 30)));
        }

        private static string Kid()
        {
            try { return NationalWillOrders.Behavior != null && NationalWillOrders.Behavior.NationKingdom != null ? NationalWillOrders.Behavior.NationKingdom.StringId : null; }
            catch { return null; }
        }
    }
}
