using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace FeudalInternalAffairs
{
    // 市场面板: 一行 = 一种商品(维多利亚3 风格: 价格条 + 变化% + 供需)
    public class MarketRowVM : ViewModel
    {
        internal MarketRowVM(GoodDef g, float price, float prevPrice, float supply, float demand, float stock,
            float buyOrders = 0f, float sellOrders = 0f, float mapi = 0f)
        {
            Good = g; _price = price; _prev = prevPrice; _supply = supply; _demand = demand; _stock = stock;
            _buy = buyOrders; _sell = sellOrders; _mapi = mapi;
        }

        internal GoodDef Good { get; private set; }
        private readonly float _price, _prev, _supply, _demand, _stock;
        private readonly float _buy, _sell, _mapi;   // v3.0: 买单/卖单/市场接入(文档 19.14.1)

        // v3.0 第二行: 买单/卖单/市场余额/市场接入(文档 19.9)
        [DataSourceProperty]
        public string SubText
        {
            get
            {
                if (_buy <= 0f && _sell <= 0f && _mapi <= 0f) return "";
                float bal = _buy - _sell;
                string bals = (bal >= 0f ? "+" : "") + bal.ToString("F1");
                return "买单 " + _buy.ToString("F1") + " · 卖单 " + _sell.ToString("F1")
                     + " · 余额 " + bals + (_mapi > 0f ? " · 市场接入 " + (int)Math.Round(_mapi * 100f) + "%" : "");
            }
        }

        [DataSourceProperty]
        public string SubColor
        {
            get
            {
                float bal = _buy - _sell;
                if (bal > 0.05f) return "#E8C33ACC";     // 需大于供 = 黄
                if (bal < -0.05f) return "#7DC97DCC";    // 供大于需 = 绿
                return "#8A8070CC";
            }
        }

        // 市场平衡条(文档 19.14.1; 参照维多利亚3市场窗口行内的供需平衡条): 宽度 = 失衡程度, 颜色 = 方向
        [DataSourceProperty]
        public float BalBarWidth
        {
            get
            {
                float bal = _buy - _sell;
                float baseN = Math.Max(Math.Max(_buy, _sell), 0.01f);
                float t = Math.Abs(bal) / baseN;
                if (t > 1f) t = 1f;
                return t * 60f;
            }
        }

        [DataSourceProperty]
        public string BalBarColor
        {
            get { return _buy - _sell > 0.05f ? "#D96A5ACC" : "#39FF14FF"; }   // 缺货红 / 过剩绿
        }

        [DataSourceProperty]
        public string Icon { get { return Good != null ? Good.Sprite : ""; } }

        [DataSourceProperty]
        public string Name { get { return Good != null ? Good.Name : "?"; } }

        [DataSourceProperty]
        public string CategoryText { get { return FeudalGoods.CategoryOf(Good); } }

        [DataSourceProperty]
        public string PriceText { get { return _price.ToString("F1"); } }

        // 价格条: 25% ~ 175% 基础价 -> 0 ~ 1
        [DataSourceProperty]
        public float PriceBarWidth
        {
            get
            {
                float b = Good != null ? Good.BasePrice : 1f;
                float t = (_price - b * 0.25f) / (b * 1.5f);
                if (t < 0f) t = 0f;
                if (t > 1f) t = 1f;
                return t * 200f;   // 像素宽(条总宽 200)
            }
        }

        [DataSourceProperty]
        public string PriceBarColor
        {
            get
            {
                float b = Good != null ? Good.BasePrice : 1f;
                if (_price > b * 1.05f) return "#D96A5ACC";   // 高于基础价 = 红
                if (_price < b * 0.95f) return "#7DC97DCC";   // 低于基础价 = 绿
                return "#C9A227CC";
            }
        }

        [DataSourceProperty]
        public string ChangeText
        {
            get
            {
                if (_prev <= 0.01f) return "-";
                float d = (_price - _prev) / _prev * 100f;
                return (d >= 0 ? "+" : "") + d.ToString("F0") + "%";
            }
        }

        [DataSourceProperty]
        public string ChangeColor
        {
            get
            {
                if (_prev <= 0.01f) return "#C8B98FFF";
                float d = _price - _prev;
                if (d > 0.01f) return "#D96A5AFF";
                if (d < -0.01f) return "#39FF14FF";
                return "#C8B98FFF";
            }
        }

        [DataSourceProperty]
        public string SupplyText { get { return _supply >= 1000f ? (_supply / 1000f).ToString("F2") + "K" : _supply.ToString("F1"); } }

        [DataSourceProperty]
        public string DemandText { get { return _demand >= 1000f ? (_demand / 1000f).ToString("F2") + "K" : _demand.ToString("F1"); } }

        [DataSourceProperty]
        public string StockText { get { return _stock >= 1000f ? (_stock / 1000f).ToString("F2") + "K" : ((int)Math.Round(_stock)).ToString(); } }

        [DataSourceProperty]
        public string GapText
        {
            get
            {
                float gap = _demand - _supply;
                if (Math.Abs(gap) < 0.05f) return "-";
                float a = Math.Abs(gap);
                string num = a >= 1000000f ? (a / 1000000f).ToString("F1") + "M"
                           : a >= 1000f ? (a / 1000f).ToString("F1") + "K"
                           : a.ToString("F1");
                return (gap > 0 ? "+" : "-") + num;
            }
        }

        [DataSourceProperty]
        public string GapColor { get { return _demand - _supply > 0.05f ? "#E8C33AFF" : "#39FF14FF"; } }
    }

    // 粮食安全页签: 一行 = 一个城市(食物 = 所有 IsFood 商品; 日耗/日产来自市场真实流量)
    public class MarketFoodRowVM : ViewModel
    {
        internal MarketFoodRowVM(string name, float stock, float need, float prod, float days)
        {
            _name = name; _stock = stock; _need = need; _prod = prod; _days = days;
        }

        private readonly string _name;
        private readonly float _stock, _need, _prod, _days;

        [DataSourceProperty]
        public string Name { get { return _name; } }

        [DataSourceProperty]
        public string StockText { get { return ((int)Math.Round(_stock)).ToString(); } }

        [DataSourceProperty]
        public string NeedText { get { return _need.ToString("F1"); } }

        [DataSourceProperty]
        public string ProdText { get { return _prod.ToString("F1"); } }

        [DataSourceProperty]
        public string DaysText
        {
            get
            {
                if (_days < 0f) return "∞";
                return _days >= 10f ? ((int)_days).ToString() : _days.ToString("F1");
            }
        }

        [DataSourceProperty]
        public string DaysColor
        {
            get
            {
                if (_days < 0f) return "#C8B98FFF";
                if (_days < 3f) return "#D96A5AFF";     // < 3 天 = 危险
                if (_days < 7f) return "#E8C33AFF";     // < 7 天 = 警告
                return "#39FF14FF";
            }
        }
    }

    // 贸易页签: 一行 = 一个城市(含商队列; 文档 20.10)
    public class MarketTradeRowVM : ViewModel
    {
        internal MarketTradeRowVM(string name, int tradeCap, int importCap, int queued, string caravans)
        {
            _name = name; _trade = tradeCap; _import = importCap; _queued = queued; _caravans = caravans;
        }

        private readonly string _name;
        private readonly int _trade, _import, _queued;
        private readonly string _caravans;

        [DataSourceProperty]
        public string Name { get { return _name; } }

        [DataSourceProperty]
        public string TradeCap { get { return _trade > 0 ? _trade.ToString() : "-"; } }

        [DataSourceProperty]
        public string ImportCap { get { return _import > 0 ? _import.ToString() : "-"; } }

        [DataSourceProperty]
        public string QueuedText { get { return _queued.ToString(); } }

        [DataSourceProperty]
        public string CaravansText { get { return _caravans; } }
    }

    public class MarketVM : PanelVMBase
    {
        private readonly Action _onClose;
        private int _tab;          // 0=商品 1=贸易 2=粮食安全
        private bool _national = true;
        private string _filter;    // null = 全部
        private string _search = "";

        internal MarketVM(Action onClose)
        {
            _onClose = onClose;
            Rows = new MBBindingList<MarketRowVM>();
            TradeRows = new MBBindingList<MarketTradeRowVM>();
            FoodRows = new MBBindingList<MarketFoodRowVM>();
            Filters = new MBBindingList<MarketFilterVM>();
            RebuildFilters();
            Refresh();
        }

        [DataSourceProperty]
        public MBBindingList<MarketRowVM> Rows { get; private set; }

        [DataSourceProperty]
        public MBBindingList<MarketTradeRowVM> TradeRows { get; private set; }

        [DataSourceProperty]
        public MBBindingList<MarketFoodRowVM> FoodRows { get; private set; }

        [DataSourceProperty]
        public MBBindingList<MarketFilterVM> Filters { get; private set; }

        [DataSourceProperty]
        public bool IsGoodsTab { get { return _tab == 0; } }

        [DataSourceProperty]
        public bool IsTradeTab { get { return _tab == 1; } }

        [DataSourceProperty]
        public bool IsFoodTab { get { return _tab == 2; } }

        [DataSourceProperty]
        public string Title
        {
            get
            {
                if (_national) return "本市场";
                if (string.IsNullOrEmpty(_townId)) return "本城市场";
                var t = FindTown(_townId);
                return (t != null && t.Name != null ? t.Name.ToString() : "?") + " 市场";
            }
        }

        // "本城"模式但还没选城: 列表清空, 提示点城市(用户需求)
        [DataSourceProperty]
        public bool IsPickingCity { get { return !_national && string.IsNullOrEmpty(_townId); } }

        [DataSourceProperty]
        public string PickText { get { return "请在地图上点击一座城市"; } }

        [DataSourceProperty]
        public string Hint
        {
            get
            {
                if (IsPickingCity)
                    return "本城市场: 请在地图上点击你想查看的城市(城镇图标)\n"
                         + "点过了就会显示那座城市的本地价/产出/消耗; 点上面\"全国\"可以切回全国行情";
                if (_tab == 2)
                    return "粮食安全: 看各城「食物库存 ÷ 每天口粮」; 少于 3 天(红)就要赶紧补粮\n"
                         + "日消耗/日产 = 该城人口实际吃掉的与每天产出的食物(吃不到会涨激进)";
                if (_tab == 1)
                    return "调运/进口: 本国城市之间会自动搬运便宜的货(差价 >20% 时)\n" +
                           "市场建筑 = 提高每日搬运上限 · 贸易站 = 允许从国外进口(贵 30%)";
                return (_national
                        ? "全国行情: 所有城市买卖汇总的基准价(金线=基础价, 红=偏贵 绿=便宜)\n"
                        : "本城行情: 本地价 = 基准价 × 本城紧缺程度(市场建筑能压低本地价)\n")
                     + "价格=卖价 · 变化=比昨天 · 产出/消耗=每天产/用多少 · 现存=市场里还有多少\n"
                     + "缺口=消耗−产出(正数=不够用) · 悬停看产地与简介 · 滚轮翻看 · 今日铸币 "
                     + (MarketSim.LedgerToday >= 0f ? "+" : "") + ((int)MarketSim.LedgerToday)
                     + " · 累计 " + (MarketSim.LedgerTotal >= 0f ? "+" : "") + ((int)MarketSim.LedgerTotal);
            }
        }

        [DataSourceProperty]
        public string ScopeText { get { return _national ? "全国" : "本城"; } }

        // 顶部筛选状态(原搜索框位置; 键盘输入进不来, 改成显示当前筛选 + 滚轮提示)
        [DataSourceProperty]
        public string FilterSummary
        {
            get
            {
                string label = _filter ?? "全部";
                return "当前筛选: " + label + " · 共 " + FeudalGoods.Main.Count + " 种商品 · 鼠标滚轮上下翻看";
            }
        }

        // ---- 悬停提示(层收不到鼠标事件 -> 由 MarketPanel 每帧按行高换算行号后调 SetHover) ----
        private int _hover = -1;
        private float _tipX, _tipY;

        [DataSourceProperty]
        public bool TooltipVisible { get { return _hover >= 0 && _hover < Rows.Count; } }

        [DataSourceProperty]
        public float TooltipX { get { return _tipX; } }

        [DataSourceProperty]
        public float TooltipY { get { return _tipY; } }

        [DataSourceProperty]
        public string TooltipName
        {
            get
            {
                var r = HoverRow();
                if (r == null || r.Good == null) return "";
                return r.Good.Name + "  ·  " + (string.IsNullOrEmpty(r.Good.Category) ? "商品" : r.Good.Category);
            }
        }

        [DataSourceProperty]
        public string TooltipLine2
        {
            get
            {
                var r = HoverRow();
                if (r == null || r.Good == null) return "";
                return "产地建筑: " + ProducersOf(r.Good.Id) + "   ·   基础价 " + r.Good.BasePrice + "   ·   现价 " + r.PriceText;
            }
        }

        [DataSourceProperty]
        public string TooltipDesc
        {
            get
            {
                var r = HoverRow();
                if (r == null || r.Good == null) return "";
                return FeudalGoods.DescOf(r.Good.Id);
            }
        }

        private MarketRowVM HoverRow()
        {
            if (_tab != 0) return null;                    // 只有商品页有提示
            if (_hover < 0 || _hover >= Rows.Count) return null;
            return Rows[_hover];
        }

        internal void SetHover(int row, float mx, float my)
        {
            try
            {
                if (_tab != 0) row = -1;
                if (row != _hover)
                {
                    _hover = row;
                    OnPropertyChanged("TooltipVisible");
                    OnPropertyChanged("TooltipName");
                    OnPropertyChanged("TooltipLine2");
                    OnPropertyChanged("TooltipDesc");
                }
                if (_hover >= 0)
                {
                    float h = 1080f;
                    try { h = TaleWorlds.Engine.Screen.RealScreenResolutionHeight; } catch { }
                    float nx = Math.Min(mx + 20f, PanelScreen.PanelX + 780f - 384f);
                    float ny = Math.Min(my + 12f, h - 230f);
                    if (Math.Abs(nx - _tipX) > 1f) { _tipX = nx; OnPropertyChanged("TooltipX"); }
                    if (Math.Abs(ny - _tipY) > 1f) { _tipY = ny; OnPropertyChanged("TooltipY"); }
                }
            }
            catch { }
        }

        // 生产该商品的建筑(反查建筑总表; 一个商品可能多个建筑产)
        private static string ProducersOf(string goodId)
        {
            if (string.IsNullOrEmpty(goodId)) return "—";
            var sb = new System.Text.StringBuilder();
            try
            {
                foreach (var b in BuildDefs.All)
                {
                    if (b == null || string.IsNullOrEmpty(b.Name) || b.Outputs == null || b.Outputs.Count == 0) continue;
                    var g = b.Outputs[0].Good;
                    if (string.IsNullOrEmpty(g) || g.StartsWith("@")) continue;
                    bool hit = false;
                    foreach (var part in g.Split('|')) if (part == goodId) { hit = true; break; }
                    if (!hit) continue;
                    if (sb.Length > 0) sb.Append('、');
                    sb.Append(b.Name);
                }
            }
            catch { }
            return sb.Length > 0 ? sb.ToString() : "—";
        }

        [DataSourceProperty]
        public string SearchText
        {
            get { return _search; }
            set
            {
                if (_search == value) return;
                _search = value;
                OnPropertyChangedWithValue(value, "SearchText");
                Refresh();
            }
        }

        internal string _townId;

        // 滚动(层收不到滚轮 -> 由 PanelScreen 轮询后调这里, 用"起始行偏移"实现)
        internal int Scroll;
        // 一屏显示多少行: 按真实屏幕高度动态算(面板满高, 顶部 320 + 底部按钮 82 + 表头/间距 58, 行高 48)
        // 与 FeudalMarket.xml 里的 MarginTop="320" MarginBottom="82"、表头+间距 58、行高 48 保持一致
        // 一屏显示多少行: 按真实屏幕高度动态算(面板满高; 460 = 顶部区 320 + 底部按钮 82 + 表头/间距 58)
        // 数值按 FeudalMarket.xml 的 MarginTop="320" / MarginBottom="82" / 行高 48 推得; 略偏紧(多显示不溢出后滚轮补)
        private int VisibleGoods
        {
            get
            {
                float h = 0f;
                try { h = TaleWorlds.Engine.Screen.RealScreenResolutionHeight; } catch { }
                if (h <= 100f) h = 1080f;
                int n = (int)((h - 460f) / 68f);
                if (n < 4) n = 4;
                if (n > FeudalGoods.Main.Count) n = FeudalGoods.Main.Count;
                return n;
            }
        }

        internal void ScrollStep(int delta)
        {
            try
            {
                if (_tab != 0) return;   // 只有商品表需要滚
                int max = Math.Max(0, FeudalGoods.Main.Count - VisibleGoods);
                int next = Scroll + delta;
                if (next < 0) next = 0;
                if (next > max) next = max;
                if (next == Scroll) return;
                Scroll = next;
                Refresh();
            }
            catch { }
        }

        public void ExecuteTabGoods() { _tab = 0; Refresh(); Notify(); }
        public void ExecuteTabTrade() { _tab = 1; Refresh(); Notify(); }
        public void ExecuteTabFood() { _tab = 2; Refresh(); Notify(); }

        public void ExecuteToggleScope()
        {
            _national = !_national;
            if (!_national) _townId = null;   // 切到"本城": 先清空, 等玩家在地图上点城市
            Refresh();
            Notify();
        }

        public void ExecuteFilterAll() { _filter = null; RefreshFilters(); Refresh(); }
        public void ExecuteFilter0() { _filter = "原料"; RefreshFilters(); Refresh(); }
        public void ExecuteFilter1() { _filter = "加工品"; RefreshFilters(); Refresh(); }
        public void ExecuteFilter2() { _filter = "军需品"; RefreshFilters(); Refresh(); }
        public void ExecuteFilter3() { _filter = "生活用品"; RefreshFilters(); Refresh(); }
        public void ExecuteFilter4() { _filter = "特产"; RefreshFilters(); Refresh(); }

        private void RebuildFilters()
        {
            Filters.Clear();
            Filters.Add(new MarketFilterVM("全部", "fia_cat_admin", _filter == null, 0, OnFilterPicked));
            for (int i = 0; i < FeudalGoods.Categories.Length; i++)
            {
                var c = FeudalGoods.Categories[i];
                Filters.Add(new MarketFilterVM(c, FeudalGoods.CategorySprite(c), _filter == c, i + 1, OnFilterPicked));
            }
        }

        private void OnFilterPicked(string category)
        {
            _filter = category;
            RefreshFilters();
            Refresh();
        }

        internal void RefreshFilters()
        {
            foreach (var f in Filters) f.SetSelected(_filter == f.Category);
        }

        private void Notify()
        {
            OnPropertyChangedWithValue(IsGoodsTab, "IsGoodsTab");
            OnPropertyChangedWithValue(IsTradeTab, "IsTradeTab");
            OnPropertyChangedWithValue(IsFoodTab, "IsFoodTab");
            OnPropertyChangedWithValue(Title, "Title");
            OnPropertyChangedWithValue(Hint, "Hint");
            OnPropertyChangedWithValue(ScopeText, "ScopeText");
            OnPropertyChangedWithValue(IsPickingCity, "IsPickingCity");
            OnPropertyChangedWithValue(PickText, "PickText");
        }

        public void ExecuteClose()
        {
            try { if (_onClose != null) _onClose(); }
            catch (Exception ex) { DLog.Force("关闭市场面板失败: " + ex.Message); }
        }

        internal void SetTown(string townId)
        {
            _townId = townId;
            _national = string.IsNullOrEmpty(townId);
            Refresh();
            Notify();
        }

        internal void Refresh()
        {
            try
            {
                Rows.Clear();
                TradeRows.Clear();
                FoodRows.Clear();
                if (IsPickingCity) return;   // 等待选城: 列表留空(面板上有"请点击城市"提示)
                if (_tab == 0) BuildGoods();
                else if (_tab == 1) BuildTrade();
                else BuildFood();
            }
            catch (Exception ex) { DLog.Info("市场刷新异常: " + ex.Message); }
        }

        private bool PassFilter(GoodDef g)
        {
            if (_filter != null && FeudalGoods.CategoryOf(g) != _filter) return false;
            if (!string.IsNullOrEmpty(_search) && g.Name != null
                && g.Name.IndexOf(_search, StringComparison.OrdinalIgnoreCase) < 0) return false;
            return true;
        }

        private void BuildGoods()
        {
            var national = EconomyWorld.National;
            TownMarket local = null;
            if (!_national && !string.IsNullOrEmpty(_townId)) local = EconomyWorld.FindMarket(_townId);

            int shown = 0;
            for (int gi = Scroll; gi < FeudalGoods.Main.Count && shown < VisibleGoods; gi++)
            {
                var g = FeudalGoods.Main[gi];
                if (!PassFilter(g)) continue;
                shown++;
                if (local != null)
                {
                    var e = local.Get(g.Id);
                    Rows.Add(new MarketRowVM(g,
                        e != null ? e.Price : g.BasePrice,
                        e != null ? e.PrevPrice : 0f,
                        e != null ? e.DailyProduction : 0f,
                        e != null ? e.DailyConsumption : 0f,
                        e != null ? e.Stock : 0f,
                        e != null ? e.BuyOrders : 0f,
                        e != null ? e.DailyProduction : 0f,
                        MarketSim.MapiOf(_townId)));
                }
                else
                {
                    float buy = 0f, sell = 0f, prev = 0f;
                    national.BuyVolume.TryGetValue(g.Id, out buy);
                    national.SellVolume.TryGetValue(g.Id, out sell);
                    national.PrevBasePrice.TryGetValue(g.Id, out prev);
                    // 全国现存 = 各城市场库存合计(原来写死 0)
                    float stockSum = 0f;
                    foreach (var mk in EconomyWorld.Markets)
                    {
                        if (mk.Value == null) continue;
                        var me = mk.Value.Get(g.Id);
                        if (me != null) stockSum += me.Stock;
                    }
                    Rows.Add(new MarketRowVM(g, national.PriceOf(g.Id), prev, sell, buy, stockSum, buy, sell, AvgMapi()));
                }
            }
        }

        // 全国平均市场接入度(MAPI)
        private static float AvgMapi()
        {
            try
            {
                float s = 0f; int n = 0;
                foreach (var kv in EconomyWorld.Markets) { s += MarketSim.MapiOf(kv.Key); n++; }
                return n > 0 ? s / n : 0.5f;
            }
            catch { return 0.5f; }
        }

        private void BuildTrade()
        {
            var pk = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
            if (pk == null) return;
            foreach (var t in pk.Fiefs)
            {
                if (t == null || !t.IsTown || t.Settlement == null) continue;
                var sb = EconomyWorld.Find(t.Settlement.StringId);
                int trade = 0, import = 0, queued = sb != null ? sb.QueuedCount : 0;
                if (sb != null)
                {
                    foreach (var grp in sb.Groups)
                    {
                        if (grp == null || grp.Count <= 0) continue;
                        var def = grp.Def;
                        if (def == null || !def.IsEffect) continue;
                        foreach (var outp in def.Outputs)
                        {
                            if (outp == null) continue;
                            if (outp.Good == BuildDefs.EffTradeCap) trade += (int)Math.Round(outp.Value(grp.Mode) * grp.Count);
                            else if (outp.Good == BuildDefs.EffImportCap) import += (int)Math.Round(outp.Value(grp.Mode) * grp.Count);
                        }
                    }
                }
                TradeRows.Add(new MarketTradeRowVM(t.Name != null ? t.Name.ToString() : "?", trade, import, queued,
                    Caravans.LineOf(t.Settlement.StringId)));
            }
        }

        private void BuildFood()
        {
            var pk = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
            if (pk == null) return;
            foreach (var t in pk.Fiefs)
            {
                if (t == null || !t.IsTown || t.Settlement == null) continue;
                var roster = t.Settlement.ItemRoster;
                var m = EconomyWorld.FindMarket(t.Settlement.StringId);
                float stock = 0f, need = 0f, prod = 0f;
                // 食物 = 所有 IsFood 商品(不只谷物; 文档 19.3 基本食物)
                for (int i = 0; i < FeudalGoods.Main.Count; i++)
                {
                    var g = FeudalGoods.Main[i];
                    if (g == null || !g.IsFood) continue;
                    var item = FeudalGoods.Item(g.Id);
                    if (item != null && roster != null) stock += roster.GetItemNumber(item);
                    if (m != null)
                    {
                        var e = m.Get(g.Id);
                        if (e != null) { need += e.DailyConsumption; prod += e.DailyProduction; }
                    }
                }
                if (need <= 0.01f) need = t.Prosperity / 1000f;   // 兜底: 旧口径(繁荣/1000)
                float days = need > 0.01f ? stock / need : -1f;
                FoodRows.Add(new MarketFoodRowVM(t.Name != null ? t.Name.ToString() : "?", stock, need, prod, days));
            }
        }

        private static Town FindTown(string townId)
        {
            try
            {
                foreach (var t in Town.AllTowns)
                    if (t != null && t.Settlement != null && t.Settlement.StringId == townId) return t;
            }
            catch { }
            return null;
        }
    }

    // 分类过滤按钮
    public class MarketFilterVM : ViewModel
    {
        private readonly Action<string> _onPick;

        internal MarketFilterVM(string category, string sprite, bool selected, int index, Action<string> onPick)
        {
            Category = category; _sprite = sprite; _selected = selected; Index = index; _onPick = onPick;
        }

        internal string Category { get; private set; }
        internal int Index { get; private set; }
        private readonly string _sprite;
        private bool _selected;

        [DataSourceProperty]
        public string Sprite { get { return _sprite; } }

        [DataSourceProperty]
        public string Label { get { return Category; } }

        [DataSourceProperty]
        public string Color { get { return _selected ? "#FFFFFFFF" : "#77FFFFFF"; } }

        public void ExecuteSelect()
        {
            try { if (_onPick != null) _onPick(Index <= 0 ? null : Category); }
            catch { }
        }

        internal void SetSelected(bool v)
        {
            if (_selected == v) return;
            _selected = v;
            OnPropertyChangedWithValue(Color, "Color");
        }
    }
}
