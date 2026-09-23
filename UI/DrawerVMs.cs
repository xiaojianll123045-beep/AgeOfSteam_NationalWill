using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
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
            float fill, float cash, float cashCap, int owner,
            Action<DrawerRowVM> onAdd, Action<DrawerRowVM> onRemove)
        {
            Def = def;
            Count = count;
            Queued = queued;
            Mode = mode;
            Stalled = stalled;
            StallReason = stallReason;
            Fill = fill;
            Cash = cash;
            CashCap = cashCap;
            Owner = owner;
            _onAdd = onAdd;
            _onRemove = onRemove;
        }

        internal float Fill { get; private set; }
        internal float Cash { get; private set; }
        internal float CashCap { get; private set; }
        internal int Owner { get; private set; }

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

        // v3.0 建筑经营状态(文档 19.14.3): 到岗率 + 现金储备
        [DataSourceProperty]
        public string StateText
        {
            get
            {
                if (Count <= 0) return "";
                string t = "在岗" + (int)Math.Round(Fill * 100f) + "%";
                // 文档 20.6: 钱柜 x/上限; 20.4: 所有者
                if (CashCap > 1f) t += " 柜" + ((int)Cash) + "/" + ((int)CashCap);
                else if (Math.Abs(Cash) >= 1000f) t += " 储" + (Cash / 1000f).ToString("F1") + "K";
                else if (Math.Abs(Cash) >= 1f) t += " 储" + ((int)Math.Abs(Cash));
                if (Cash < -1f) t += "(亏)";
                t += " · " + Ownership.NameOf(Owner);
                return t;
            }
        }

        [DataSourceProperty]
        public string StateColor
        {
            get { return Cash < -1f ? "#D96A5AFF" : (Fill < 0.6f ? "#E8C33AFF" : "#8A9A7AFF"); }
        }

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
        private string _milText = "";
        private string _armyText = "";
        private readonly List<DrawerNodeVM> _allNodes = new List<DrawerNodeVM>();
        private readonly List<DrawerRowVM> _allRows = new List<DrawerRowVM>();
        internal int Scroll;

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

        // v4.100: 军事信息(驻守/守备营/附近部队)
        [DataSourceProperty]
        public string MilText
        {
            get { return _milText; }
            set { if (_milText != value) { _milText = value; OnPropertyChangedWithValue(value, "MilText"); } }
        }

        // v4.100: 组建军团(联军)信息
        [DataSourceProperty]
        public string ArmyText
        {
            get { return _armyText; }
            set { if (_armyText != value) { _armyText = value; OnPropertyChangedWithValue(value, "ArmyText"); } }
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
                _allNodes.Clear();
                foreach (var n in list) _allNodes.Add(n);
                RebuildTreeWindow();
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
                for (int i = 0; i < _allNodes.Count; i++)
                {
                    var n = _allNodes[i];
                    if (!n.HasChildren || n.Expanded) continue;
                    for (int j = i + 1; j < _allNodes.Count; j++)
                    {
                        if (_allNodes[j].Depth <= n.Depth) break;
                        _allNodes[j].IsVisible = false;
                    }
                }
                // 被折叠的父节点重新展开时, 恢复其后代可见(受各自父节点约束)
                for (int i = 0; i < _allNodes.Count; i++) _allNodes[i].IsVisible = IsNodeVisible(i);
                RebuildTreeWindow();
            }
            catch { }
        }

        private bool IsNodeVisible(int index)
        {
            try
            {
                int depth = _allNodes[index].Depth;
                if (depth == 0) return true;
                // 往前找最近的一个更浅的父节点, 父节点必须可见且展开
                for (int j = index - 1; j >= 0; j--)
                {
                    if (_allNodes[j].Depth < depth)
                        return _allNodes[j].IsVisible && _allNodes[j].Expanded;
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
                _allRows.Clear();
                foreach (var def in BuildDefs.All)
                {
                    if (!BuildDefs.AllowedAt(def, s.IsVillage, s.IsCastle, s.IsTown)) continue;
                    var g = sb != null ? sb.Find(def.Id) : null;
                    _allRows.Add(new DrawerRowVM(def,
                        g != null ? g.Count : 0,
                        sb != null ? sb.QueuedOf(def.Id) : 0,
                        g != null ? g.Mode : BuildMode.Wood,
                        g != null && g.Stalled,
                        g != null ? g.StallReason : null,
                        g != null ? g.Fill : 0f,
                        g != null ? g.Cash : 0f,
                        g != null ? InvestmentPool.ChestCap(g, s) : 0f,
                        g != null ? Ownership.OwnerOf(g, s) : 1,
                        OnRowAdd, OnRowRemove));
                }
                IsBuildMode = true;
                ApplyFilter();
                UpdateMilitary(s);
                DLog.Info("抽屉: 显示 " + Title + " 的建筑 " + _allRows.Count + " 项");
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
                foreach (var r in _allRows)
                {
                    bool show = key.Length == 0 || (r.Name != null && r.Name.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0);
                    r.IsVisible = show;
                }
                RebuildRowWindow();
            }
            catch { }
        }

        private int TreePage()
        {
            float h = 0f;
            try { h = PanelScreen.ScreenHeight(); } catch { }
            if (h <= 100f) h = 1080f;
            int n = (int)((h - 92f - 26f) / 52f);
            return n < 3 ? 3 : n;
        }

        private int RowPage()
        {
            float h = 0f;
            try { h = PanelScreen.ScreenHeight(); } catch { }
            if (h <= 100f) h = 1080f;
            int n = (int)((h - 224f - 92f) / 54f);
            return n < 3 ? 3 : n;
        }

        private void RebuildTreeWindow()
        {
            try
            {
                Nodes.Clear();
                var vis = new List<DrawerNodeVM>();
                foreach (var n in _allNodes) if (n.IsVisible) vis.Add(n);
                int max = Math.Max(0, vis.Count - TreePage());
                if (Scroll > max) Scroll = max;
                if (Scroll < 0) Scroll = 0;
                for (int i = Scroll; i < vis.Count && Nodes.Count < TreePage(); i++) Nodes.Add(vis[i]);
            }
            catch { }
        }

        private void RebuildRowWindow()
        {
            try
            {
                Rows.Clear();
                var vis = new List<DrawerRowVM>();
                foreach (var r in _allRows) if (r.IsVisible) vis.Add(r);
                int max = Math.Max(0, vis.Count - RowPage());
                if (Scroll > max) Scroll = max;
                if (Scroll < 0) Scroll = 0;
                for (int i = Scroll; i < vis.Count && Rows.Count < RowPage(); i++) Rows.Add(vis[i]);
            }
            catch { }
        }

        internal void ScrollStep(int dir)
        {
            try
            {
                Scroll += dir;
                if (Scroll < 0) Scroll = 0;
                if (_buildMode) RebuildRowWindow(); else RebuildTreeWindow();
            }
            catch { }
        }

        internal void Refresh()
        {
            try
            {
                if (_buildMode)
                {
                    if (_current != null) ShowBuildings(_current);
                    else UpdateMilitary(null);
                    return;
                }
                foreach (var n in _allNodes) n.RefreshTexts();
            }
            catch { }
        }

        // v4.100: 点城市 -> 军事信息(驻军/守备营/附近我军/组建的联军)
        private void UpdateMilitary(Settlement s)
        {
            try
            {
                if (s == null) { MilText = ""; ArmyText = ""; return; }
                var target = s.IsVillage && s.Village != null && s.Village.Bound != null ? s.Village.Bound : s;
                int garrison = 0;
                try { if (target != null && target.Town != null && target.Town.GarrisonParty != null) garrison = target.Town.GarrisonParty.MemberRoster.TotalManCount; } catch { }
                int ours = target != null ? DefArmy.GarrisonMenOf(target.StringId) : 0;
                var k = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                var pos = target.Position.ToVec2();
                int near = 0;
                try
                {
                    foreach (var p in MobileParty.All)
                    {
                        if (p == null || !p.IsActive || p.IsMainParty) continue;
                        if (p.IsGarrison || p.IsMilitia || p.IsCaravan) continue;
                        bool mine = DefArmy.IsDefArmyParty(p) || (k != null && ReferenceEquals(p.MapFaction, k));
                        if (!mine) continue;
                        float dx = p.Position.X - pos.X, dy = p.Position.Y - pos.Y;
                        if (dx * dx + dy * dy < 40f * 40f) near++;
                    }
                }
                catch { }
                MilText = "驻守: 驻军 " + garrison + " 人 · 国防军守备营 " + ours + " 人" + (near > 0 ? " · 附近我军 " + near + " 支" : "");

                var best = DefArmy.NearestArmyOf(target, out float bd);
                if (best != null)
                {
                    int men = 0, pc = 0;
                    try
                    {
                        foreach (var mp in best.Parties) { if (mp != null && mp.IsActive) { men += DefArmy.RegularsOf(mp); pc++; } }
                    }
                    catch { }
                    ArmyText = "组建军团: " + (best.Name != null ? best.Name.ToString() : "军团") + "(" + pc + " 支部队/" + men + " 人, 距离 " + (int)Math.Sqrt(bd) + ") ｜ 点[唤出]成立守备营新军团或召唤联军";
                }
                else
                {
                    ArmyText = "组建军团: 附近没有本国联军 ｜ 点[唤出]把守备营编成新军团";
                }
            }
            catch (Exception ex) { DLog.Force("抽屉军事信息异常: " + ex.Message); }
        }

        // v4.100: 一键唤出(守备营够 100 就编成野战军团, 否则召唤最近联军)
        internal void ExecuteCallOut()
        {
            try
            {
                var s = _current;
                if (s == null) return;
                var target = s.IsVillage && s.Village != null && s.Village.Bound != null ? s.Village.Bound : s;
                if (target == null) return;
                int ours = DefArmy.GarrisonMenOf(target.StringId);
                string msg;
                if (ours >= 100) msg = DefArmy.CreateLegion(target, ours);
                else msg = DefArmy.CallNearestArmy(target);
                MapSelection.Message(msg);
                UpdateMilitary(s);
            }
            catch (Exception ex) { DLog.Force("一键唤出失败: " + ex.Message); }
        }
    }
}
