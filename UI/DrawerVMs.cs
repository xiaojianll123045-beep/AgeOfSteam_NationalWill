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

    // ===================== v4.282: 驻扎的海军行(城市详情页) =====================
    //   用户要求: "城市详情页扩充, 海军可以驻扎在城市内(仅沿海城市)" —— 这一块就列在该城**驻泊**的舰队。
    //   渲染方式**复用本页既有的行风格**(DrawerRowVM 那一套): 同一张 fia_ui_row 底图、同样的
    //   图标 + 名称 + 数值 排版、右侧同样两枚小按钮(行内按钮点击同样走 SettlementDrawer 的热区轮询)。
    //   ★ 操作按钮的定位用**稳定 key**(舰队 PartyId)而不是行号 —— 与海军页 v4.279 的模式一致:
    //     舰队可能在这 2 秒刷新窗口里离港/被歼灭, 按 key 反查才不会点错目标。
    public class DrawerNavyRowVM : ViewModel
    {
        private readonly string _name, _icon, _nums, _numsColor, _note, _noteColor;
        internal readonly string Key;

        internal DrawerNavyRowVM(string key, string icon, string name, string nums, string numsColor,
            string note, string noteColor)
        {
            Key = key ?? "";
            _icon = string.IsNullOrEmpty(icon) ? "fia_bld_port" : icon;
            _name = name ?? "";
            _nums = nums ?? "";
            _numsColor = string.IsNullOrEmpty(numsColor) ? "#D8C9A0FF" : numsColor;
            _note = note ?? "";
            _noteColor = string.IsNullOrEmpty(noteColor) ? "#8FD8E8FF" : noteColor;
        }

        [DataSourceProperty] public string Icon { get { return _icon; } }
        [DataSourceProperty] public string Name { get { return _name; } }
        [DataSourceProperty] public string Nums { get { return _nums; } }
        [DataSourceProperty] public string NumsColor { get { return _numsColor; } }
        [DataSourceProperty] public string Note { get { return _note; } }
        [DataSourceProperty] public string NoteColor { get { return _noteColor; } }
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

        // ==================== v4.282: 驻扎的海军(城市详情页) ====================
        //   版面(与 GUI\Prefabs\FeudalDrawer.xml、UI\SettlementDrawer.cs 的热区一一对应):
        //     标题行     底距 230 (h20)  "驻扎的海军" + 本城驻扎支数
        //     港口信息行 底距 210 (h18)  此城是不是沿海港口 / 船坞等级 / 能造什么(如实说明未解锁)
        //     舰队行 i   底距 164/120/76  最多 3 行(行高 40, 行距 4) —— 每行 [查看] [召回]
        //   ★ 建筑列表的可用高度因此被"让出" `NavyReserve`(258 = 76 + 174 + 8) 像素; 这个常量在这里
        //     定义、由 SettlementDrawer 注册热区与 RowPage() 共同使用, 三处**只有一个数字来源**。
        internal const float NavyReserve = 258f;
        internal const float NavyRowH = 44f;
        internal const int NavyRowMax = 3;

        private string _navyTitle = "", _navyInfo = "";
        private bool _navyShow;
        private readonly List<DrawerNavyRowVM> _allNavyRows = new List<DrawerNavyRowVM>();

        [DataSourceProperty] public MBBindingList<DrawerNavyRowVM> NavyRows { get; private set; }
        [DataSourceProperty] public bool ShowNavy { get { return _navyShow; } }
        [DataSourceProperty] public string NavyTitle { get { return _navyTitle; } }
        [DataSourceProperty] public string NavyInfo { get { return _navyInfo; } }

        internal SettlementDrawerVM(Action onClose)
        {
            _onClose = onClose;
            Nodes = new MBBindingList<DrawerNodeVM>();
            Rows = new MBBindingList<DrawerRowVM>();
            NavyRows = new MBBindingList<DrawerNavyRowVM>();
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
                UpdateNavy(s);   // v4.282: 驻扎的海军区块(与建筑列表同页, 见 NavyReserve 的版面说明)
                DLog.Info("抽屉: 显示 " + Title + " 的建筑 " + _allRows.Count + " 项");
            }
            catch (Exception ex) { DLog.Force("抽屉建筑列表失败: " + ex.Message); }
        }

        // ==================== v4.282: 驻扎的海军区块 ====================
        //   内容三行: ①本城驻泊的舰队(每行 舰数/战力/水手 + [查看][召回]) ②港口与船坞实况
        //   ③没有舰队时的一句引导(而不是留一片空白让人以为坏了)。
        //   ★ 只对**沿海城镇**显示"港口/船坞"实况; 内陆城镇如实写"不是沿海港口, 海军无法进驻"。
        internal void UpdateNavy(Settlement s)
        {
            try
            {
                _allNavyRows.Clear();
                NavyRows.Clear();
                _navyShow = false;
                _navyTitle = ""; _navyInfo = "";
                if (s == null) return;
                var target = s.IsVillage && s.Village != null && s.Village.Bound != null ? s.Village.Bound : s;
                if (target == null) return;

                _navyShow = true;
                _navyTitle = "驻扎的海军";

                bool coastal = false;
                try { coastal = target.HasPort && target.IsTown; } catch { }
                var list = new List<NavalTaskForce.Force>();
                try { list = NavalTaskForce.MooredAt(target); } catch { }

                _navyTitle = "驻扎的海军(" + list.Count + " 支)";

                // ---- ② 港口/船坞实况(顺便回答"这城能造什么船、为什么不能造") ----
                if (!coastal)
                {
                    _navyInfo = NavalBridge.Usable
                        ? "此城不是沿海港口 —— 海军只能进驻沿海城镇(港口由海战 DLC 的地图数据决定)"
                        : "未检测到海战 DLC —— 海军功能整体停用";
                }
                else
                {
                    var k = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                    int lvl = 0;
                    try { lvl = NavalBridge.ShipyardLevel(target); } catch { }
                    string yardInfo;
                    if (lvl <= 0) yardInfo = "无造船厂建筑(不能造船/不能加装升级件) —— 可在建筑列表里排建[造船厂]";
                    else
                    {
                        int can = 0, locked = 0, stock = 0;
                        try
                        {
                            var offers = NavalFleet.Offers(target, k);
                            for (int i = 0; i < offers.Count; i++)
                            {
                                if (offers[i] == null) continue;
                                if (offers[i].Buildable) { can++; if (offers[i].Days == 0) stock++; }
                                else locked++;
                            }
                        }
                        catch { }
                        yardInfo = "造船厂 " + lvl + " 级(1级轻/2级中/3级重) · 在售 " + stock + " 型 · 可造 " + can + " 型"
                                 + (locked > 0 ? " · 未解锁 " + locked + " 型(需更高船坞等级/海军科技)" : "");
                    }
                    _navyInfo = "沿海港口 · " + yardInfo;
                }

                // ---- ① 驻泊舰队行 ----
                for (int i = 0; i < list.Count; i++)
                {
                    var f = list[i];
                    if (f == null) continue;
                    var p = NavalTaskForce.PartyOf(f);
                    if (p == null) continue;
                    int ships, crew, sailors;
                    float power, ratio, morale, upkeep, wage;
                    bool moored;
                    NavalTaskForce.Stats(p, out ships, out power, out ratio, out crew, out sailors, out morale,
                        out upkeep, out wage, out moored);
                    string nm = MapSelection.NameOf(p);
                    string st = NavalTaskForce.MoorStateText(f);
                    string nums = "舰 " + ships + " 艘 · 战力 " + (int)power + " · 水手 " + sailors + "/" + crew;
                    _allNavyRows.Add(new DrawerNavyRowVM(
                        "fleet:" + f.PartyId, NavalBridge.IconNavalPort, nm, nums,
                        moored ? "#E9CD74FF" : "#D8C9A0FF",
                        st + " · 完整度 " + (int)Math.Round(ratio * 100f) + "%" + (moored ? " · 维护 -35%" : ""),
                        moored ? "#8FD8E8FF" : "#A89C7ACC"));
                }
                if (_allNavyRows.Count == 0)
                    _navyInfo += " ｜ 本城暂无驻扎舰队: 到海军页[舰队]页签选一支舰队, 点[进驻城市]后在地图上点本城";

                for (int i = 0; i < _allNavyRows.Count && i < NavyRowMax; i++) NavyRows.Add(_allNavyRows[i]);
                if (_allNavyRows.Count > NavyRowMax)
                    _navyInfo += " ｜ 另有 " + (_allNavyRows.Count - NavyRowMax) + " 支未列出(到海军页看全部)";

                OnPropertyChangedWithValue(NavyRows, "NavyRows");
                OnPropertyChangedWithValue(_navyShow, "ShowNavy");
                OnPropertyChangedWithValue(_navyTitle, "NavyTitle");
                OnPropertyChangedWithValue(_navyInfo, "NavyInfo");
            }
            catch (Exception ex) { DLog.Force("抽屉驻扎海军区块异常: " + ex.Message); }
        }

        // 驻扎舰队的行内操作(key = "fleet:<PartyId>"; action: 0 = 查看, 1 = 召回/离港)
        //   ★ 按 key 反查而不是按行号: 舰队可能在面板 2 秒刷新窗口内离港/被歼灭(与海军页 v4.279 同一理由)。
        internal void NavyActionByKey(string key, int action)
        {
            try
            {
                var cur = _current;
                var target = cur != null && cur.IsVillage && cur.Village != null && cur.Village.Bound != null
                    ? cur.Village.Bound : cur;
                string pid = !string.IsNullOrEmpty(key) && key.StartsWith("fleet:", StringComparison.Ordinal)
                    ? key.Substring(6) : "";
                NavalTaskForce.Force hit = null;
                var list = target != null ? NavalTaskForce.MooredAt(target) : new List<NavalTaskForce.Force>();
                for (int i = 0; i < list.Count; i++)
                    if (list[i] != null && list[i].PartyId == pid) { hit = list[i]; break; }
                if (hit == null)
                {
                    MapSelection.Message("驻扎海军: 该舰队已不在本城(可能已离港/被歼灭) —— 列表已刷新");
                    UpdateNavy(target);
                    return;
                }
                var p = NavalTaskForce.PartyOf(hit);
                if (p == null) { MapSelection.Message("驻扎海军: 舰队不在场"); UpdateNavy(target); return; }

                if (action == 0)
                {
                    // [查看]: 切到海军页并定位这支舰队(PanelScreen 会自动关掉本抽屉 —— 侧边栏互斥)
                    MapSelection.Message("驻扎海军: 打开海军页定位「" + MapSelection.NameOf(p) + "」");
                    NavyPanel.OpenAtFleet(hit.PartyId);
                    return;
                }
                // [召回]: 解除驻扎(离港)。母港保留 —— 舰队转为在港待命, 地图上仍归你指挥。
                string msg = NavalTaskForce.Unmoor(p);
                try { PanelScreen.JumpToSettlement(target); } catch { }
                MapSelection.Message(msg);
                UpdateNavy(target);
            }
            catch (Exception ex) { DLog.Force("抽屉驻扎海军操作异常: " + ex.Message); }
        }

        // 热区用: 第 i 行(屏幕可见行)的 key
        internal string NavyKeyAt(int i)
        {
            try { return i >= 0 && i < NavyRows.Count ? NavyRows[i].Key : null; }
            catch { return null; }
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
            // v4.282: 列表底部要**让出驻扎海军区块**(NavyReserve = 258)。这里与 SettlementDrawer 注册
            //   热区时的守卫用同一个常量 —— 否则会出现"VM 渲染了 14 行, 后 3 行没有热区, 点不动"。
            int n = (int)((h - 224f - NavyReserve) / 54f);
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
                    else { UpdateMilitary(null); UpdateNavy(null); }   // v4.282: 海军区块一并清空
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
