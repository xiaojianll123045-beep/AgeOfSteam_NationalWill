using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace FeudalInternalAffairs
{
    // ===================== 态A: 定居点树节点 =====================
    public class DrawerNodeVM : ViewModel
    {
        private readonly Action<DrawerNodeVM> _onSelect;
        private readonly Action<DrawerNodeVM> _onToggle;
        private bool _expanded = true;
        private bool _visible = true;

        internal DrawerNodeVM(Settlement s, int depth, Action<DrawerNodeVM> onSelect, Action<DrawerNodeVM> onToggle)
        {
            Settlement = s;
            Depth = depth;
            _onSelect = onSelect;
            _onToggle = onToggle;
        }

        internal Settlement Settlement { get; private set; }
        internal int Depth { get; private set; }
        internal bool HasChildren { get; set; }

        [DataSourceProperty]
        public string Name
        {
            get
            {
                try { return Settlement != null && Settlement.Name != null ? Settlement.Name.ToString() : "?"; }
                catch { return "?"; }
            }
        }

        // 图标: 城/堡/村
        [DataSourceProperty]
        public string Sprite
        {
            get
            {
                try
                {
                    if (Settlement == null) return "fia_settle_town";
                    if (Settlement.IsVillage) return "fia_settle_village";
                    if (Settlement.IsCastle) return "fia_settle_castle";
                    return "fia_settle_town";
                }
                catch { return "fia_settle_town"; }
            }
        }

        // 已建 + 在建 / 上限
        [DataSourceProperty]
        public string Slots
        {
            get
            {
                try
                {
                    if (Settlement == null || string.IsNullOrEmpty(Settlement.StringId)) return "";
                    var sb = EconomyWorld.Find(Settlement.StringId);
                    int built = sb != null ? sb.BuiltCount : 0;
                    int queued = sb != null ? sb.QueuedCount : 0;
                    int limit = Limit();
                    string txt = "已建 " + built;
                    if (queued > 0) txt += " + 在建 " + queued;
                    return txt + " / " + limit;
                }
                catch { return ""; }
            }
        }

        private int Limit()
        {
            try
            {
                if (Settlement == null) return 0;
                if (Settlement.IsVillage)
                {
                    var v = Settlement.Village;
                    return BuildingRules.SlotLimit(false, false, 0, v != null ? (int)v.Hearth : 0);
                }
                var t = Settlement.Town;
                int prosperity = t != null ? (int)t.Prosperity : 0;
                return BuildingRules.SlotLimit(Settlement.IsTown, Settlement.IsCastle, prosperity, 0);
            }
            catch { return 0; }
        }

        [DataSourceProperty]
        public string Glyph
        {
            get
            {
                if (!HasChildren) return "";
                return _expanded ? "▼" : "▶";
            }
        }

        [DataSourceProperty]
        public float Indent { get { return 12f + Depth * 22f; } }

        // 名字按钮的缩进(避开左侧展开按钮; 展开按钮画框会外扩, 这里留 16px 净空)
        [DataSourceProperty]
        public float IndentName { get { return 58f + Depth * 22f; } }

        [DataSourceProperty]
        public bool IsVisible
        {
            get { return _visible; }
            set { if (_visible != value) { _visible = value; OnPropertyChangedWithValue(value, "IsVisible"); } }
        }

        public void ExecuteToggle()
        {
            try { if (_onToggle != null) _onToggle(this); }
            catch (Exception ex) { DLog.Info("抽屉展开异常: " + ex.Message); }
        }

        public void ExecuteSelect()
        {
            try { if (_onSelect != null) _onSelect(this); }
            catch (Exception ex) { DLog.Info("抽屉选择异常: " + ex.Message); }
        }

        internal void SetExpanded(bool v)
        {
            if (_expanded == v) return;
            _expanded = v;
            OnPropertyChangedWithValue(Glyph, "Glyph");
        }

        internal bool Expanded { get { return _expanded; } }

        // 刷新显示文本(槽位变化后)
        internal void RefreshTexts()
        {
            try
            {
                OnPropertyChangedWithValue(Slots, "Slots");
                OnPropertyChangedWithValue(Glyph, "Glyph");
            }
            catch { }
        }
    }

    // ===================== 态B: 建筑列表行 =====================
    public class DrawerRowVM : ViewModel
    {
        private readonly Action<DrawerRowVM> _onAdd;
        private readonly Action<DrawerRowVM> _onRemove;

        internal DrawerRowVM(BuildDef def, int count, int queued, BuildMode mode, bool stalled, string stallReason,
            Action<DrawerRowVM> onAdd, Action<DrawerRowVM> onRemove)
        {
            Def = def;
            Count = count;
            Queued = queued;
            Mode = mode;
            Stalled = stalled;
            StallReason = stallReason;
            _onAdd = onAdd;
            _onRemove = onRemove;
        }

        internal BuildDef Def { get; private set; }
        internal int Count { get; private set; }
        internal int Queued { get; private set; }
        internal BuildMode Mode { get; private set; }
        internal bool Stalled { get; private set; }
        internal string StallReason { get; private set; }

        private bool _visible = true;

        [DataSourceProperty]
        public bool IsVisible
        {
            get { return _visible; }
            set { if (_visible != value) { _visible = value; OnPropertyChangedWithValue(value, "IsVisible"); } }
        }

        [DataSourceProperty]
        public string Name { get { return Def != null ? Def.Name : "?"; } }

        [DataSourceProperty]
        public string Icon { get { return Def != null ? Def.Sprite : ""; } }

        [DataSourceProperty]
        public string CountText
        {
            get
            {
                // 紧凑格式, 给大字号省空间: ×12(+3)
                string t = "×" + Count;
                if (Queued > 0) t += "(+" + Queued + ")";
                return t;
            }
        }

        [DataSourceProperty]
        public string ModeText { get { return BuildDefs.ModeName(Mode); } }

        [DataSourceProperty]
        public string ModeSprite { get { return BuildDefs.ModeSprite(Mode); } }

        [DataSourceProperty]
        public string CategoryText { get { return Def != null ? BuildDefs.CategoryName(Def.Cat) : ""; } }

        [DataSourceProperty]
        public string CategorySprite { get { return Def != null ? BuildDefs.CategorySprite(Def.Cat) : ""; } }

        [DataSourceProperty]
        public string Status
        {
            get
            {
                if (Queued > 0) return "建造中";
                if (Stalled)
                {
                    if (!string.IsNullOrEmpty(StallReason))
                    {
                        if (StallReason.StartsWith("缺料")) return "缺料";
                        if (StallReason.StartsWith("库存")) return "满仓";
                        return "停产";
                    }
                    return "停产";
                }
                return Count > 0 ? "已建" : "未建";
            }
        }

        // 8 位 #RRGGBBAA: 已建=米色, 缺料=黄, 未建=灰
        [DataSourceProperty]
        public string StatusColor
        {
            get
            {
                if (Queued > 0) return "#7DA8C9FF";
                if (Stalled) return "#E8C33AFF";
                return Count > 0 ? "#E8D9B5FF" : "#8A8A8AFF";
            }
        }

        [DataSourceProperty]
        public string IconColor
        {
            get { return Count > 0 || Queued > 0 ? "#FFFFFFFF" : "#66FFFFFF"; }
        }

        [DataSourceProperty]
        public string Tooltip
        {
            get
            {
                try
                {
                    var s = Def != null ? ("【" + BuildDefs.CategoryName(Def.Cat) + "】" + BuildDefs.ModeName(Mode)) : "";
                    if (Stalled && !string.IsNullOrEmpty(StallReason)) s += "  " + StallReason;
                    return s;
                }
                catch { return ""; }
            }
        }

        public void ExecuteAdd()
        {
            try { if (_onAdd != null) _onAdd(this); }
            catch (Exception ex) { DLog.Info("抽屉+异常: " + ex.Message); }
        }

        public void ExecuteRemove()
        {
            try { if (_onRemove != null) _onRemove(this); }
            catch (Exception ex) { DLog.Info("抽屉-异常: " + ex.Message); }
        }
    }

    // ===================== 抽屉主 VM =====================
    public class SettlementDrawerVM : PanelVMBase
    {
        private readonly Action _onClose;
        private bool _buildMode;
        private Settlement _current;
        private string _searchText = "";
        private string _title = "";
        private string _subtitle = "";

        internal SettlementDrawerVM(Action onClose)
        {
            _onClose = onClose;
            Nodes = new MBBindingList<DrawerNodeVM>();
            Rows = new MBBindingList<DrawerRowVM>();
            BuildTree();
            Title = "定居点";
            Subtitle = "左键点树里的名字查看建筑";
        }

        [DataSourceProperty]
        public MBBindingList<DrawerNodeVM> Nodes { get; private set; }

        [DataSourceProperty]
        public MBBindingList<DrawerRowVM> Rows { get; private set; }

        [DataSourceProperty]
        public bool IsTreeMode
        {
            get { return !_buildMode; }
            set { if (_buildMode == value) { _buildMode = !value; OnPropertyChangedWithValue(!value, "IsBuildMode"); OnPropertyChangedWithValue(value, "IsTreeMode"); } }
        }

        [DataSourceProperty]
        public bool IsBuildMode
        {
            get { return _buildMode; }
            set { if (_buildMode != value) { _buildMode = value; OnPropertyChangedWithValue(value, "IsBuildMode"); OnPropertyChangedWithValue(!value, "IsTreeMode"); } }
        }

        [DataSourceProperty]
        public string Title
        {
            get { return _title; }
            set { if (_title != value) { _title = value; OnPropertyChangedWithValue(value, "Title"); } }
        }

        [DataSourceProperty]
        public string Subtitle
        {
            get { return _subtitle; }
            set { if (_subtitle != value) { _subtitle = value; OnPropertyChangedWithValue(value, "Subtitle"); } }
        }

        [DataSourceProperty]
        public string SearchText
        {
            get { return _searchText; }
            set
            {
                if (_searchText == value) return;
                _searchText = value;
                OnPropertyChangedWithValue(value, "SearchText");
                ApplyFilter();
            }
        }

        public void ExecuteClose()
        {
            try { if (_onClose != null) _onClose(); }
            catch (Exception ex) { DLog.Force("关闭抽屉失败: " + ex.Message); }
        }

        public void ExecuteBack()
        {
            try { IsBuildMode = false; }
            catch { }
        }

        // ---- 态A: 建树(只显示本国) ----
        private void BuildTree()
        {
            try
            {
                var b = NationalWillOrders.Behavior;
                var k = b != null ? b.NationKingdom : null;
                if (k == null) return;
                var list = new List<DrawerNodeVM>();
                foreach (var s in Settlement.All)
                {
                    if (s == null || string.IsNullOrEmpty(s.StringId)) continue;
                    if (s.OwnerClan == null || s.OwnerClan.Kingdom != k) continue;
                    if (s.IsVillage) continue;   // 村庄挂在城/堡下面
                    var node = new DrawerNodeVM(s, 0, OnNodeSelect, OnNodeToggle);
                    // 挂靠村庄
                    var children = new List<DrawerNodeVM>();
                    try
                    {
                        foreach (var v in s.BoundVillages)
                        {
                            if (v == null || v.Settlement == null) continue;
                            children.Add(new DrawerNodeVM(v.Settlement, 1, OnNodeSelect, OnNodeToggle));
                        }
                    }
                    catch { }
                    node.HasChildren = children.Count > 0;
                    list.Add(node);
                    list.AddRange(children);
                }
                Nodes.Clear();
                foreach (var n in list) Nodes.Add(n);
            }
            catch (Exception ex) { DLog.Force("抽屉建树失败: " + ex.Message); }
        }

        private void OnNodeToggle(DrawerNodeVM node)
        {
            try
            {
                if (node == null || !node.HasChildren) return;
                node.SetExpanded(!node.Expanded);
                ApplyCollapse();
            }
            catch { }
        }

        // 折叠: 隐藏父节点之后、缩进更深的连续节点
        private void ApplyCollapse()
        {
            try
            {
                for (int i = 0; i < Nodes.Count; i++)
                {
                    var n = Nodes[i];
                    if (!n.HasChildren || n.Expanded) continue;
                    for (int j = i + 1; j < Nodes.Count; j++)
                    {
                        if (Nodes[j].Depth <= n.Depth) break;
                        Nodes[j].IsVisible = false;
                    }
                }
                // 被折叠的父节点重新展开时, 恢复其后代可见(受各自父节点约束)
                for (int i = 0; i < Nodes.Count; i++) Nodes[i].IsVisible = IsNodeVisible(i);
            }
            catch { }
        }

        private bool IsNodeVisible(int index)
        {
            try
            {
                int depth = Nodes[index].Depth;
                if (depth == 0) return true;
                // 往前找最近的一个更浅的父节点, 父节点必须可见且展开
                for (int j = index - 1; j >= 0; j--)
                {
                    if (Nodes[j].Depth < depth)
                        return Nodes[j].IsVisible && Nodes[j].Expanded;
                }
                return true;
            }
            catch { return true; }
        }

        private void OnNodeSelect(DrawerNodeVM node)
        {
            try
            {
                if (node == null || node.Settlement == null) return;
                _current = node.Settlement;
                // 相机飘移
                NationalWillCamera.FlyTo(node.Settlement);
                ShowBuildings(node.Settlement);
            }
            catch (Exception ex) { DLog.Force("抽屉选择失败: " + ex.Message); }
        }

        // ---- 态B: 建筑列表 ----
        internal void ShowBuildings(Settlement s)
        {
            try
            {
                if (s == null) return;
                _current = s;
                Title = s.Name != null ? s.Name.ToString() : "?";
                var sb = EconomyWorld.Find(s.StringId);
                int limit = LimitOf(s);
                Subtitle = "已建 " + (sb != null ? sb.BuiltCount : 0)
                    + ((sb != null && sb.QueuedCount > 0) ? " + 在建 " + sb.QueuedCount : "")
                    + " / 上限 " + limit;
                Rows.Clear();
                foreach (var def in BuildDefs.All)
                {
                    if (!BuildDefs.AllowedAt(def, s.IsVillage, s.IsCastle, s.IsTown)) continue;
                    var g = sb != null ? sb.Find(def.Id) : null;
                    Rows.Add(new DrawerRowVM(def,
                        g != null ? g.Count : 0,
                        sb != null ? sb.QueuedOf(def.Id) : 0,
                        g != null ? g.Mode : BuildMode.Wood,
                        g != null && g.Stalled,
                        g != null ? g.StallReason : null,
                        OnRowAdd, OnRowRemove));
                }
                IsBuildMode = true;
                ApplyFilter();
                DLog.Info("抽屉: 显示 " + Title + " 的建筑 " + Rows.Count + " 项");
            }
            catch (Exception ex) { DLog.Force("抽屉建筑列表失败: " + ex.Message); }
        }

        private int LimitOf(Settlement s)
        {
            try
            {
                if (s.IsVillage)
                {
                    var v = s.Village;
                    return BuildingRules.SlotLimit(false, false, 0, v != null ? (int)v.Hearth : 0);
                }
                var t = s.Town;
                return BuildingRules.SlotLimit(s.IsTown, s.IsCastle, t != null ? (int)t.Prosperity : 0, 0);
            }
            catch { return 0; }
        }

        private void OnRowAdd(DrawerRowVM row)
        {
            try
            {
                if (_current == null || row == null || row.Def == null) return;
                var sb = EconomyWorld.Of(_current.StringId);
                int limit = LimitOf(_current);
                if (sb.UsedSlots >= limit)
                {
                    MapSelection.Message("槽位已满 (" + sb.UsedSlots + "/" + limit + ")");
                    return;
                }
                if (sb.Queue.Count >= BuildingRules.MaxQueuePerSettlement)
                {
                    MapSelection.Message("队列已满(最多 " + BuildingRules.MaxQueuePerSettlement + " 项)");
                    return;
                }
                var mode = row.Mode;
                sb.Queue.Add(new QueuedBuild(row.Def.Id, BuildDefs.WorkHours(row.Def, mode), mode));
                MapSelection.Message("已排队: " + row.Name + "(" + BuildDefs.ModeName(mode) + ")");
                ShowBuildings(_current);
            }
            catch (Exception ex) { DLog.Force("排队失败: " + ex.Message); }
        }

        private void OnRowRemove(DrawerRowVM row)
        {
            try
            {
                if (_current == null || row == null || row.Def == null) return;
                var sb = EconomyWorld.Find(_current.StringId);
                if (sb == null) return;
                // 优先减少队列
                for (int i = sb.Queue.Count - 1; i >= 0; i--)
                {
                    if (sb.Queue[i] != null && sb.Queue[i].DefId == row.Def.Id)
                    {
                        sb.Queue.RemoveAt(i);
                        MapSelection.Message("已取消在建: " + row.Name);
                        ShowBuildings(_current);
                        return;
                    }
                }
                // 没有在建 -> 拆除 1 个已建, 退还 50% 一次性材料(2.7)
                var g = sb.Find(row.Def.Id);
                if (g == null || g.Count <= 0) { MapSelection.Message("没有可拆除的 " + row.Name); return; }
                g.Count -= 1;
                sb.Prune();
                RefundHalf(_current, row.Def, g.Mode);
                MapSelection.Message("已拆除 1 个 " + row.Name + "(退还 50% 材料)");
                ShowBuildings(_current);
            }
            catch (Exception ex) { DLog.Force("拆除失败: " + ex.Message); }
        }

        // 退还 50% 一次性材料到本地市场
        private static void RefundHalf(Settlement s, BuildDef def, BuildMode mode)
        {
            try
            {
                var market = DailySettlement.MarketOf(s);
                var roster = market != null ? market.ItemRoster : null;
                if (roster == null) return;
                float wood, stone, iron;
                BuildDefs.BuildMaterials(mode, out wood, out stone, out iron);
                var wi = FeudalGoods.Item(FeudalGoods.Hardwood);
                var si = FeudalGoods.Item(FeudalGoods.Stone);
                var ii = FeudalGoods.Item(FeudalGoods.Iron);
                int w = MBRandom.RoundRandomized(wood * 0.5f);
                int st = MBRandom.RoundRandomized(stone * 0.5f);
                int ir = MBRandom.RoundRandomized(iron * 0.5f);
                if (w > 0 && wi != null) roster.AddToCounts(wi, w);
                if (st > 0 && si != null) roster.AddToCounts(si, st);
                if (ir > 0 && ii != null) roster.AddToCounts(ii, ir);
            }
            catch { }
        }

        private void ApplyFilter()
        {
            try
            {
                string key = _searchText != null ? _searchText.Trim() : "";
                foreach (var r in Rows)
                {
                    bool show = key.Length == 0 || (r.Name != null && r.Name.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0);
                    r.IsVisible = show;
                }
            }
            catch { }
        }

        internal void Refresh()
        {
            try
            {
                if (_buildMode) return;   // 建筑列表在打开时构建(P4 起接入实时状态)
                foreach (var n in Nodes) n.RefreshTexts();
            }
            catch { }
        }
    }
}
