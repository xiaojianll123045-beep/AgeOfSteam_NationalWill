using System;
using System.Collections.Generic;
using System.Text;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace FeudalInternalAffairs
{
    // ===================== 部队管理页: 兵种表行 =====================
    public class ArmyUnitRowVM : ViewModel
    {
        internal const float HandleW = 28f;   // 滑块尺寸(与 prefab 严格一致: 28x28)
        internal const float TrackH = 14f;    // 轨道高度(与 prefab 严格一致: 264x14)

        internal readonly int Kind;
        private readonly string _icon, _name;
        private string _men = "0", _prof = "—", _fill = "—", _value = "0",
            _barColor = "#7FBF6AFF", _nameColor = "#6F6858FF",
            _selText = "0 / —", _selColor = "#6F6858FF", _reqText = "—", _reqColor = "#8A8070FF",
            _rowColor = "#FFFFFF08", _trackColor = "#FFFFFF12", _handleColor = "#8A8070FF";
        private float _fillW, _handleX;
        private int _max;
        private bool _locked = true, _hover, _drag;

        internal ArmyUnitRowVM(int kind)
        {
            Kind = kind;
            var u = Equipment.Unit(kind);
            _icon = IconOf(kind);
            _name = u != null ? u.Name : ("兵种 " + kind);
        }

        [DataSourceProperty] public string Icon { get { return _icon; } }
        [DataSourceProperty] public string Name { get { return _name; } }
        [DataSourceProperty] public string MenText { get { return _men; } }
        [DataSourceProperty] public string ProfText { get { return _prof; } }
        [DataSourceProperty] public string FillText { get { return _fill; } }
        [DataSourceProperty] public string ValueText { get { return _value; } }
        [DataSourceProperty] public string BarColor { get { return _barColor; } }
        [DataSourceProperty] public string NameColor { get { return _nameColor; } }
        [DataSourceProperty] public string SelText { get { return _selText; } }
        [DataSourceProperty] public string SelColor { get { return _selColor; } }
        [DataSourceProperty] public string ReqText { get { return _reqText; } }
        [DataSourceProperty] public string ReqColor { get { return _reqColor; } }
        [DataSourceProperty] public string RowColor { get { return _rowColor; } }
        [DataSourceProperty] public string TrackColor { get { return _trackColor; } }
        [DataSourceProperty] public string HandleColor { get { return _handleColor; } }
        [DataSourceProperty] public float FillWidth { get { return _fillW; } }
        [DataSourceProperty] public float HandleX { get { return _handleX; } }

        internal int Max { get { return _max; } }
        internal bool Locked { get { return _locked; } }
        internal bool IsHover { get { return _hover; } }

        internal void SetLive(int men, float prof, float fill)
        {
            _men = MilFmt.N(men);
            _prof = men > 0 ? ((int)Math.Round(prof)).ToString() : "—";
            _fill = men > 0 ? ((int)Math.Round(Math.Max(0f, Math.Min(1f, fill)) * 100f)) + "%" : "—";
            OnPropertyChangedWithValue(_men, "MenText");
            OnPropertyChangedWithValue(_prof, "ProfText");
            OnPropertyChangedWithValue(_fill, "FillText");
        }

        // 上限/可用: ok=false 或 max<=0 时整行置灰(原因由 SetReq 写在第三行)
        internal void SetAvailable(bool ok, int max)
        {
            _locked = !ok || max <= 0;
            _max = ok && max > 0 ? max : 0;
            if (_locked) _drag = false;
            _nameColor = _locked ? "#6F6858FF" : "#E8DCC0FF";
            _selColor = _locked ? "#6F6858FF" : "#E8C33AFF";
            _trackColor = _locked ? "#FFFFFF12" : (_hover ? "#FFFFFF55" : "#FFFFFF33");
            _handleColor = _drag ? "#E8C33AFF" : (_locked ? "#8A8070FF" : "#FFF3C4FF");
            OnPropertyChangedWithValue(_nameColor, "NameColor");
            OnPropertyChangedWithValue(_selColor, "SelColor");
            OnPropertyChangedWithValue(_trackColor, "TrackColor");
            OnPropertyChangedWithValue(_handleColor, "HandleColor");
        }

        internal void SetSelection(int value, int max, float trackW)
        {
            if (value < 0) value = 0;
            if (max > 0 && value > max) value = max;
            if (_locked) value = 0;
            _value = value.ToString();
            _selText = max > 0 ? (MilFmt.N(value) + " / " + MilFmt.N(max)) : "0 / —";
            float ratio = max > 0 ? Math.Min(1f, value / (float)max) : 0f;
            _fillW = ratio * trackW;
            _handleX = ratio * Math.Max(0f, trackW - HandleW);
            OnPropertyChangedWithValue(_value, "ValueText");
            OnPropertyChangedWithValue(_selText, "SelText");
            OnPropertyChangedWithValue(_fillW, "FillWidth");
            OnPropertyChangedWithValue(_handleX, "HandleX");
        }

        // 需求摘要: 兵种行第三行(可补/需求/费用/原因; VM 主动断行为 ≤2 行)
        internal void SetReq(string t, string color)
        {
            if (t == null) t = "—";
            if (_reqText == t && _reqColor == color) return;
            _reqText = t;
            _reqColor = color;
            OnPropertyChangedWithValue(_reqText, "ReqText");
            OnPropertyChangedWithValue(_reqColor, "ReqColor");
        }

        // 悬停高亮(整行底 + 轨道底); 拖动时另亮滑块
        internal void SetHover(bool on)
        {
            if (_hover == on) return;
            _hover = on;
            _rowColor = on ? "#FFFFFF1A" : "#FFFFFF08";
            _trackColor = _locked ? "#FFFFFF12" : (on ? "#FFFFFF55" : "#FFFFFF33");
            OnPropertyChangedWithValue(_rowColor, "RowColor");
            OnPropertyChangedWithValue(_trackColor, "TrackColor");
        }

        // 拖动状态机(面板按下/移动/松开时切换)
        internal void SetDrag(bool on)
        {
            if (_drag == on) return;
            _drag = on;
            _handleColor = _locked ? "#8A8070FF" : (on ? "#E8C33AFF" : "#FFF3C4FF");
            OnPropertyChangedWithValue(_handleColor, "HandleColor");
        }

        // 每帧通知: 滑块位置随值实时刷新(面板 OnTick 调用)
        internal void TickVisual()
        {
            OnPropertyChangedWithValue(_fillW, "FillWidth");
            OnPropertyChangedWithValue(_handleX, "HandleX");
        }

        internal void SetBarColor(string c)
        {
            if (_barColor == c) return;
            _barColor = c;
            OnPropertyChangedWithValue(_barColor, "BarColor");
        }

        internal static string IconOf(int kind)
        {
            return MilIcons.ByUnit(kind);
        }
    }

    // ===================== 装备区行(可领用/缺口) =====================
    public class ArmyEquipRowVM : ViewModel
    {
        private readonly string _name, _haveNeed, _gap, _gapColor;

        internal ArmyEquipRowVM(string name, int have, int need)
        {
            _name = string.IsNullOrEmpty(name) ? "?" : name;
            _haveNeed = "有 " + MilFmt.N(have) + " · 需 " + MilFmt.N(need);
            int gap = need - have;
            if (gap < 0) gap = 0;
            _gap = gap > 0 ? ("缺 " + MilFmt.N(gap)) : "齐";
            _gapColor = gap > 0 ? "#D96A5AFF" : "#7FBF6AFF";
        }

        [DataSourceProperty] public string Name { get { return _name; } }
        [DataSourceProperty] public string HaveNeed { get { return _haveNeed; } }
        [DataSourceProperty] public string Gap { get { return _gap; } }
        [DataSourceProperty] public string GapColor { get { return _gapColor; } }
    }

    // ===================== 部队管理页 VM =====================
    public class ArmyManageVM : PanelVMBase
    {
        internal const float TrackW = 264f;     // 与 FeudalArmyManage.xml 拖动条同宽
        internal const float RowHeight = 124f;  // 行高(与 xml SuggestedHeight 一致; 图标 80 + 两行摘要)
        internal const float FirstRowTop = 268f;// 列表首行顶(与 xml ListPanel MarginTop 一致)
        internal const int ModeAdd = 0;
        internal const int ModeSplit = 1;
        private const float ListBottomReserve = 132f;   // 状态行 + 底部操作栏预留

        private int _hoverKind = -1;            // 鼠标悬停/拖动中的行(高亮用)
        private int _scroll;                    // 可见窗口第一行的兵种号
        private readonly List<ArmyUnitRowVM> _allRows = new List<ArmyUnitRowVM>();

        private readonly Action _onClose;
        private int _mode;
        private bool _isArmy;
        private readonly int[] _add;
        private readonly int[] _split;
        private readonly int[] _men = new int[Equipment.Units.Count];
        private readonly Dictionary<int, LordArmy.KindOption> _opts = new Dictionary<int, LordArmy.KindOption>();
        private Settlement _settlement;

        // 上限口径缓存(每次 Refresh 重算): 国防军 = 守备营可抽 ∩ 军械库武器可装 ∩ 编制余额
        private string _nationalKey = "", _lordKey = "";
        private int _menNow, _cmdLimit, _cmdRoom, _garrisonAvail;

        private string _title = "部队管理";
        private string _sub1 = "", _sub2 = "", _sub3 = "";
        private string _previewTitle = "预览", _previewBody = "", _previewColor = "#D8C9A0FF";
        private string _status = "拖动条选择人数; 绿色 = 可增补, 红色 = 分出";
        private string _tabAddColor = "#8A8070FF", _tabSplitColor = "#8A8070FF";
        private string _tabAddBg = "#FFFFFF12", _tabSplitBg = "#FFFFFF12";
        private string _settleText = "征兵地: —";
        private string _executeText = "确认扩容", _executeColor = "#7FBF6AFF";

        internal MobileParty Party { get { return _party; } }
        private MobileParty _party;

        public ArmyManageVM(Action onClose, MobileParty p, int mode)
        {
            _onClose = onClose;
            _party = p;
            _mode = mode == ModeSplit ? ModeSplit : ModeAdd;
            int n = Equipment.Units.Count;
            _add = new int[n];
            _split = new int[n];
            Rows = new MBBindingList<ArmyUnitRowVM>();
            EquipRows = new MBBindingList<ArmyEquipRowVM>();
            for (int i = 0; i < n; i++) _allRows.Add(new ArmyUnitRowVM(i));
            SyncRows();
            try { _settlement = DefaultSettlement(p); } catch { }
            Refresh();
        }

        public MBBindingList<ArmyUnitRowVM> Rows { get; private set; }
        public MBBindingList<ArmyEquipRowVM> EquipRows { get; private set; }

        [DataSourceProperty] public string Title { get { return _title; } }
        [DataSourceProperty] public string Sub1 { get { return _sub1; } }
        [DataSourceProperty] public string Sub2 { get { return _sub2; } }
        [DataSourceProperty] public string Sub3 { get { return _sub3; } }
        [DataSourceProperty] public string PreviewTitle { get { return _previewTitle; } }
        [DataSourceProperty] public string PreviewBody { get { return _previewBody; } }
        [DataSourceProperty] public string PreviewColor { get { return _previewColor; } }
        [DataSourceProperty] public string StatusLine { get { return _status; } }
        [DataSourceProperty] public string TabAddColor { get { return _tabAddColor; } }
        [DataSourceProperty] public string TabSplitColor { get { return _tabSplitColor; } }
        [DataSourceProperty] public string TabAddBg { get { return _tabAddBg; } }
        [DataSourceProperty] public string TabSplitBg { get { return _tabSplitBg; } }
        [DataSourceProperty] public string SettleText { get { return _settleText; } }
        [DataSourceProperty] public string ExecuteText { get { return _executeText; } }
        [DataSourceProperty] public string ExecuteColor { get { return _executeColor; } }
        [DataSourceProperty] public bool IsArmy { get { return _isArmy; } }
        [DataSourceProperty] public bool IsNormal { get { return !_isArmy; } }
        [DataSourceProperty] public bool IsAddMode { get { return !_isArmy && _mode == ModeAdd; } }

        internal int Mode { get { return _mode; } }

        public void ExecuteClose() { if (_onClose != null) _onClose(); }

        // 面板已打开时切换目标部队(地图小窗再次点[管理/扩容/分裂])
        internal void Retarget(MobileParty p, int mode)
        {
            try
            {
                if (p == null || !p.IsActive) return;
                _party = p;
                _mode = mode == ModeSplit ? ModeSplit : ModeAdd;
                for (int k = 0; k < _add.Length; k++) { _add[k] = 0; _split[k] = 0; }
                for (int k = 0; k < _men.Length; k++) _men[k] = 0;
                try { _settlement = DefaultSettlement(p); } catch { }
                _status = "已切换目标: " + MapSelection.NameOf(p);
                Refresh();
            }
            catch (Exception ex) { DLog.Force("部队管理: 切换目标失败 " + ex.Message); }
        }

        // ==================== 刷新 ====================
        internal void Refresh()
        {
            try
            {
                SyncRows();
                if (_party == null || !_party.IsActive)
                {
                    _isArmy = false;
                    _sub1 = "部队已不存在"; _sub2 = ""; _sub3 = "";
                    _previewBody = "该部队已解散/销毁, 请关闭页面。";
                    NotifyAll();
                    return;
                }
                _title = MapSelection.NameOf(_party);
                _isArmy = _party.Army != null;

                _opts.Clear();
                if (!_isArmy)
                {
                    try
                    {
                        var list = LordArmy.AvailableKinds(_party, _settlement);
                        if (list != null)
                            for (int i = 0; i < list.Count; i++) _opts[list[i].KindIdx] = list[i];
                    }
                    catch (Exception ex) { DLog.Force("部队管理: 兵种可选刷新失败 " + ex.Message); }
                }

                Dictionary<string, int> menByIdx;
                Dictionary<string, float> profByIdx;
                Soldiers.Aggregate(_party, out menByIdx, out profByIdx);

                var lg = DefArmy.LegionOf(_party);
                bool defLg = lg != null;
                _nationalKey = "";
                try { _nationalKey = Armory.NationalOwner(DefArmy.OurKingdom); } catch { }
                _lordKey = "";
                try { _lordKey = Armory.LordOwner(_party.ActualClan); } catch { }
                string ownerKey = defLg ? _nationalKey : _lordKey;

                for (int k = 0; k < _men.Length; k++) _men[k] = 0;
                for (int i = 0; i < _allRows.Count; i++)   // 全兵种(不只看可见窗口), 供装备/编制口径用
                {
                    var row = _allRows[i];
                    string uid = UnitIdOf(row.Kind);
                    int c = 0;
                    if (!string.IsNullOrEmpty(uid) && menByIdx != null && menByIdx.ContainsKey(uid)) c = menByIdx[uid];
                    _men[row.Kind] = c;
                }
                _menNow = 0;
                for (int k = 0; k < _men.Length; k++) _menNow += _men[k];
                if (_menNow <= 0) { try { _menNow = _party.MemberRoster.TotalManCount; } catch { } }
                _cmdLimit = 0;
                try { _cmdLimit = ArmyDoctrine.CommandLimitOf(_party); } catch { }
                _cmdRoom = Math.Max(0, _cmdLimit - _menNow);
                _garrisonAvail = 0;
                if (defLg) { try { _garrisonAvail = DefArmy.GarrisonMenOf(lg.HomeId); } catch { } }
                ClampSplitSelections();   // 换目标/换模式后: 已选总数不得超全局上限

                for (int i = 0; i < Rows.Count; i++)
                {
                    var row = Rows[i];
                    int c = _men[row.Kind];
                    string uid = UnitIdOf(row.Kind);
                    float pAvg = 0f;
                    if (!string.IsNullOrEmpty(uid) && profByIdx != null && profByIdx.ContainsKey(uid)) pAvg = profByIdx[uid];
                    row.SetLive(c, pAvg, FillOf(row.Kind, c, ownerKey));

                    bool avail;
                    if (_isArmy) avail = false;
                    else if (_mode == ModeSplit) avail = c > 0;
                    else if (defLg) avail = MaxOf(row.Kind) > 0;   // 守备营抽调 或 征兵地招募
                    else avail = _opts.ContainsKey(row.Kind) && _cmdRoom > 0;
                    int max = avail ? MaxOf(row.Kind) : 0;
                    row.SetAvailable(avail, max);
                    if (_add[row.Kind] > max) _add[row.Kind] = max;
                    if (_split[row.Kind] > c) _split[row.Kind] = c;
                    row.SetSelection(ValueOf(row.Kind), max, TrackW);
                    row.SetBarColor(_mode == ModeSplit ? "#C96A5AFF" : "#7FBF6AFF");
                    row.SetReq(SummaryOf(row.Kind, c, avail, defLg),
                        avail ? "#9A8F78FF" : "#6F6858FF");
                }

                BuildEquipRows(lg, ownerKey);
                UpdateHeader(lg);
                UpdatePreview();
                NotifyAll();
            }
            catch (Exception ex) { DLog.Force("部队管理页刷新失败: " + ex.Message); }
        }

        // 可见行号 -> 兵种号(滚动窗口映射; 越界返回 -1)
        internal int KindAt(int visibleIndex)
        {
            try
            {
                if (visibleIndex < 0 || visibleIndex >= Rows.Count) return -1;
                return Rows[visibleIndex].Kind;
            }
            catch { return -1; }
        }

        internal int UnitCount { get { return _allRows.Count; } }

        // 一屏可见行数: 列表从顶部一直排到底部预留线
        private int VisibleRowCount()
        {
            try
            {
                float h = 0f;
                try { h = TaleWorlds.Engine.Screen.RealScreenResolutionHeight; } catch { }
                if (h <= 100f) h = 1080f;
                int n = (int)((h - FirstRowTop - ListBottomReserve) / RowHeight);
                if (n < 1) n = 1;
                if (n > _allRows.Count) n = _allRows.Count;
                return n;
            }
            catch { return 7; }
        }

        // 重建可见窗口(滚轮换窗口)
        private void SyncRows()
        {
            try
            {
                int visible = VisibleRowCount();
                int max = Math.Max(0, _allRows.Count - visible);
                if (_scroll > max) _scroll = max;
                if (_scroll < 0) _scroll = 0;
                if (Rows == null) Rows = new MBBindingList<ArmyUnitRowVM>();
                Rows.Clear();
                for (int i = _scroll; i < _allRows.Count && Rows.Count < visible; i++) Rows.Add(_allRows[i]);
                OnPropertyChangedWithValue(Rows.Count, "Rows");
            }
            catch { }
        }

        // 滚轮: 上下滚动可见窗口(与其它页 ScrollStep 同口径)
        internal void ScrollStep(int dir)
        {
            try
            {
                if (dir == 0) return;
                int max = Math.Max(0, _allRows.Count - VisibleRowCount());
                int next = _scroll + dir;
                if (next < 0) next = 0;
                if (next > max) next = max;
                if (next == _scroll) return;
                _scroll = next;
                _hoverKind = -1;
                for (int i = 0; i < _allRows.Count; i++) _allRows[i].SetHover(false);
                SyncRows();
            }
            catch { }
        }

        // 每帧(节流后)只换头部文字, 不动列表
        internal void TickLive()
        {
            try
            {
                if (_party == null || !_party.IsActive) return;
                UpdateHeader(DefArmy.LegionOf(_party));
                Notify("Sub1"); Notify("Sub2"); Notify("Sub3"); Notify("StatusLine");
            }
            catch { }
        }

        // 悬停/拖动行高亮(面板每帧调用; -1 = 无)
        internal void SetHoverRow(int kind)
        {
            try
            {
                if (kind >= _allRows.Count) kind = _allRows.Count - 1;
                if (kind < -1) kind = -1;
                if (_hoverKind == kind) return;
                _hoverKind = kind;
                for (int i = 0; i < Rows.Count; i++) Rows[i].SetHover(Rows[i].Kind == kind);
            }
            catch { }
        }

        // 每帧通知所有行的 FillWidth/HandleX(滑块实时跟随)
        internal void TickVisuals()
        {
            try { for (int i = 0; i < Rows.Count; i++) Rows[i].TickVisual(); }
            catch { }
        }

        // 拖动状态机: 面板按下时亮该行滑块(松开/取消时灭)
        internal void SetRowDrag(int kind, bool on)
        {
            try
            {
                for (int i = 0; i < Rows.Count; i++)
                    if (Rows[i].Kind == kind) { Rows[i].SetDrag(on); break; }
            }
            catch { }
        }

        // 兵种行第三行摘要: 可补/需求/费用/原因(主动断行 ≤2 行)
        private string SummaryOf(int kind, int men, bool avail, bool defLg)
        {
            try
            {
                if (_isArmy) return "军团部队 · 请到军务页管理";
                if (_mode == ModeSplit)
                    return men > 0 ? ("现有 " + MilFmt.N(men) + " 人 · 可分出上限 " + MilFmt.N(MaxOf(kind)) + " 人") : "该兵种现无人员";
                if (!avail) return Wrap(CapReasonOf(kind, defLg), 40, 2);
                if (defLg)
                {
                    int g = GarrisonDrawCapOf(kind);
                    int r = RecruitDrawCapOf(kind);
                    int slots = 0;
                    try { slots = DefArmy.SlotQuotaOf(_settlement); } catch { }
                    if (slots == int.MaxValue) slots = -1;   // -1 = 该地没有知名人物, 不限名额
                    string sn = _settlement != null && _settlement.Name != null ? _settlement.Name.ToString() : "未选";
                    return Wrap("来源: 守备营 " + MilFmt.N(g) + " 人 / 征兵地" + sn + " 名额 "
                        + (slots < 0 ? "不限" : MilFmt.N(slots))
                        + " 共 " + MilFmt.N(r) + " 人(各兵种共用) · 本行可补 " + MilFmt.N(MaxOf(kind)) + " 人", 40, 2);
                }
                LordArmy.KindOption opt;
                if (_opts.TryGetValue(kind, out opt))
                {
                    string s = opt.Need;
                    if (string.IsNullOrEmpty(s)) s = "可增补";
                    s = s.Replace(" /百人", "").Replace(" + ", "+");
                    return Wrap("每百人 " + s + " · " + MilFmt.N(opt.Fee) + " 金/人 · 私库支撑 " + MilFmt.N(MaxOf(kind)), 40, 2);
                }
                return Wrap(CapReasonOf(kind, defLg), 40, 2);
            }
            catch { return "—"; }
        }

        // 上限为 0 时的原因(逐兵种; 行置灰时显示在第三行)
        private string CapReasonOf(int kind, bool defLg)
        {
            try
            {
                var u = Equipment.Unit(kind);
                if (u == null) return "未知兵种";
                if (defLg)
                {
                    if (string.IsNullOrEmpty(_nationalKey)) return "尚未建立国家军械库(先建国/定都)";
                    if (_cmdRoom <= 0) return "编制无余额(现有 " + MilFmt.N(_menNow) + " / 上限 " + MilFmt.N(_cmdLimit) + ")";
                    if (GarrisonDrawCapOf(kind) <= 0 && RecruitDrawCapOf(kind) <= 0)
                    {
                        int g0 = GarrisonDrawCapOf(kind);
                        // v4.234: 征兵地那一侧用内核同口径给出具体缺项(人口/国库/装备), 不再只说"不可增补"
                        string why = DefArmy.LegionRecruitBlocker(_party, _settlement, kind);
                        if (g0 <= 0) return "驻地守备营无可抽兵力 · 征兵地: " + why;
                        return why;
                    }
                    return "当前不可增补";
                }
                string gate = "";
                try { gate = LordArmy.UnitGate(u.Id); } catch { }
                if (!TechOk(kind)) return "科技未解锁: 需「" + LordArmy.TechName(gate) + "」(科技页研究)";
                if (string.IsNullOrEmpty(_lordKey)) return "未识别领主私库(需家族领袖)";
                if (_cmdRoom <= 0) return "编制无余额(现有 " + MilFmt.N(_menNow) + " / 上限 " + MilFmt.N(_cmdLimit) + ")";
                if (LordSupportCap(kind) <= 0) return "领主私库无法支撑该兵种装备(军械库页采购)";
                return "同族优先更高档位, 本档不出现在可征列表";
            }
            catch { return "上限为 0"; }
        }

        // ==================== 上限口径 ====================
        private static bool TechOk(int kind)
        {
            try
            {
                var u = Equipment.Unit(kind);
                if (u == null) return false;
                return Research.UnlockedFor(DefArmy.OurKingdom, LordArmy.UnitGate(u.Id));
            }
            catch { return false; }
        }

        private static bool IsWeaponTag(EquipTag t)
        {
            return t == EquipTag.AnyWeapon || t == EquipTag.Firearm || t == EquipTag.Rifle || t == EquipTag.Bow
                || t == EquipTag.Crossbow || t == EquipTag.BowOrCrossbow || t == EquipTag.Carbine
                || t == EquipTag.Saber || t == EquipTag.Lance || t == EquipTag.Gun
                || t == EquipTag.HeavyGun || t == EquipTag.LightGun || t == EquipTag.MachineGun;
        }

        // 军械库(国家/私库)该兵种武器可装人数 = min(逐武器需求: 库存×100/每百人)
        private static int WeaponCapOf(int kind, string key)
        {
            try
            {
                if (string.IsNullOrEmpty(key)) return 0;
                var u = Equipment.Unit(kind);
                if (u == null || u.Req == null) return 0;
                long cap = long.MaxValue;
                for (int r = 0; r < u.Req.Length; r++)
                {
                    var req = u.Req[r];
                    if (!IsWeaponTag(req.Tag)) continue;
                    var m = StockPick(key, req.Tag);
                    int have = m != null ? Armory.CountKey(key, m.Id) : 0;
                    long c = req.Per100 > 0 ? have * 100L / req.Per100 : long.MaxValue;
                    if (c < cap) cap = c;
                }
                if (cap == long.MaxValue) return int.MaxValue;   // 无武器需求(如工兵): 不卡武器库存
                return cap > int.MaxValue ? int.MaxValue : (int)cap;
            }
            catch { return 0; }
        }

        // 领主私库支撑口径(与 LordArmy.AvailableKinds 同): 逐需求标签汇总已解锁库存×100/每百人, 取最小
        private int LordSupportCap(int kind)
        {
            try
            {
                if (string.IsNullOrEmpty(_lordKey)) return 0;
                var u = Equipment.Unit(kind);
                if (u == null || u.Req == null || u.Req.Length == 0) return 0;
                long cap = long.MaxValue;
                for (int r = 0; r < u.Req.Length; r++)
                {
                    var req = u.Req[r];
                    long total = 0;
                    var entries = Armory.EntriesOf(_lordKey);
                    for (int i = 0; i < entries.Count; i++)
                    {
                        var kv = entries[i];
                        if (kv.Value <= 0) continue;
                        var def = Equipment.Get(kv.Key);
                        if (def == null || !Equipment.MatchesTag(def, req.Tag)) continue;
                        bool unlocked = false;
                        try { unlocked = Research.UnlockedFor(DefArmy.OurKingdom, def.TechId); } catch { }
                        if (unlocked) total += kv.Value;
                    }
                    long c = req.Per100 > 0 ? total * 100L / req.Per100 : long.MaxValue;
                    if (c < cap) cap = c;
                }
                if (cap == long.MaxValue) return 0;
                return cap > int.MaxValue ? int.MaxValue : (int)cap;
            }
            catch { return 0; }
        }

        // 军械库中有库存的同标签型号: 只认已解锁(v4.241 与内核 BestStockFor 同口径), 同级取贵
        private static EquipDef StockPick(string key, EquipTag tag)
        {
            try
            {
                if (string.IsNullOrEmpty(key)) return null;
                EquipDef best = null;
                var entries = Armory.EntriesOf(key);
                for (int i = 0; i < entries.Count; i++)
                {
                    var kv = entries[i];
                    if (kv.Value <= 0) continue;
                    var def = Equipment.Get(kv.Key);
                    if (def == null || !Equipment.MatchesTag(def, tag)) continue;
                    bool unlocked = false;
                    try { unlocked = Equipment.IsUnlocked(def); } catch { }
                    if (!unlocked) continue;
                    if (best == null || def.Tier > best.Tier || (def.Tier == best.Tier && def.Price > best.Price)) best = def;
                }
                return best;
            }
            catch { return null; }
        }

        // 主动断行(CJK 2 单位/ASCII 1 单位; 按空格优先, 超长词硬断; 最多 maxLines 行, 溢出补 …)
        private static string Wrap(string s, int unitsPerLine, int maxLines)
        {
            if (string.IsNullOrEmpty(s) || unitsPerLine <= 0 || maxLines <= 0) return s;
            try
            {
                if (UnitsOf(s) <= unitsPerLine) return s;
                var lines = new List<string>();
                var cur = new StringBuilder();
                int curW = 0;
                var parts = s.Split(' ');
                for (int i = 0; i < parts.Length; i++)
                {
                    string token = parts[i];
                    if (token.Length == 0) continue;
                    int w = UnitsOf(token);
                    if (curW > 0 && curW + 1 + w > unitsPerLine)
                    {
                        if (lines.Count >= maxLines) break;
                        lines.Add(cur.ToString());
                        cur.Length = 0; curW = 0;
                    }
                    if (w > unitsPerLine)
                    {
                        int pos = 0;
                        while (pos < token.Length)
                        {
                            int avail = unitsPerLine - curW - (cur.Length > 0 ? 1 : 0);
                            int take = 0, tw = 0;
                            while (pos + take < token.Length)
                            {
                                int cw = token[pos + take] > (char)0x2E7F ? 2 : 1;
                                if (tw + cw > avail) break;
                                tw += cw; take++;
                            }
                            if (take <= 0)
                            {
                                if (lines.Count >= maxLines) break;
                                lines.Add(cur.ToString());
                                cur.Length = 0; curW = 0;
                                continue;
                            }
                            if (cur.Length > 0) { cur.Append(' '); curW += 1; }
                            cur.Append(token, pos, take);
                            curW += tw;
                            pos += take;
                        }
                    }
                    else
                    {
                        if (curW > 0) { cur.Append(' '); curW += 1; }
                        cur.Append(token);
                        curW += w;
                    }
                }
                if (cur.Length > 0 && lines.Count < maxLines) lines.Add(cur.ToString());
                if (lines.Count == 0) return s;
                bool truncated = false;
                if (lines.Count >= maxLines)
                {
                    int used = 0;
                    for (int i = 0; i < lines.Count; i++) used += UnitsOf(lines[i]) + 2;
                    if (used < UnitsOf(s)) truncated = true;
                }
                if (truncated)
                {
                    string last = lines[lines.Count - 1];
                    while (last.Length > 0 && UnitsOf(last) + 2 > unitsPerLine) last = last.Substring(0, last.Length - 1);
                    lines[lines.Count - 1] = last + "…";
                }
                return string.Join("\n", lines.ToArray());
            }
            catch { return s; }
        }

        private static int UnitsOf(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            int n = 0;
            for (int i = 0; i < s.Length; i++) n += s[i] > (char)0x2E7F ? 2 : 1;
            return n;
        }

        // 逻辑行包装: 行间 \n, 行内主动断行(供预览区)
        private static void AppendLine(StringBuilder sb, string s, int unitsPerLine, int maxLines)
        {
            if (sb.Length > 0) sb.Append('\n');
            sb.Append(Wrap(s, unitsPerLine, maxLines));
        }

        private void NotifyAll()
        {
            Notify("Title"); Notify("Sub1"); Notify("Sub2"); Notify("Sub3");
            Notify("PreviewTitle"); Notify("PreviewBody"); Notify("PreviewColor");
            Notify("StatusLine"); Notify("TabAddColor"); Notify("TabSplitColor");
            Notify("TabAddBg"); Notify("TabSplitBg");
            Notify("SettleText"); Notify("ExecuteText"); Notify("ExecuteColor");
            OnPropertyChangedWithValue(_isArmy, "IsArmy");
            OnPropertyChangedWithValue(!_isArmy, "IsNormal");
            OnPropertyChangedWithValue(!_isArmy && _mode == ModeAdd, "IsAddMode");
        }

        private void Notify(string name)
        {
            try { OnPropertyChanged(name); } catch { }
        }

        // ==================== 头部/预览 ====================
        private void UpdateHeader(DefLegion lg)
        {
            try
            {
                int men = 0;
                try { men = DefArmy.RegularsOf(_party); } catch { }   // 显示口径 = 名册 − 将军
                if (men <= 0)
                    for (int i = 0; i < _men.Length; i++) men += _men[i];
                int lim = ArmyDoctrine.CommandLimitOf(_party);
                _sub1 = "兵力 " + MilFmt.N(men) + " / 编制上限 " + MilFmt.N(lim)
                    + (men > lim ? "  [超编!]" : "");
                float morale = 50f, org = 100f;
                try { morale = DefArmyStats.MoraleOf(_party.Party); } catch { }
                try { org = DefArmyStats.OrgOf(_party.Party); } catch { }
                _sub2 = "士气 " + ((int)Math.Round(morale)) + " / 100 · 组织度 " + ((int)Math.Round(org)) + " / 100";
                int wage;
                string payer;
                if (lg != null)
                {
                    wage = (int)Math.Ceiling(men * DefArmy.WagePerManPerDay);
                    int treasury = 0;
                    try { treasury = EconomyWorld.Treasury.Gold; } catch { }
                    payer = "国库 " + MilFmt.N(treasury);
                }
                else
                {
                    try { wage = LordArmy.DailyWageOf(_party); } catch { wage = (int)Math.Ceiling(men * DefArmy.WagePerManPerDay); }
                    Hero h = Payer();
                    int gold = h != null ? h.Gold : 0;
                    payer = (h != null && h.Name != null ? h.Name.ToString() : "领主") + "金 " + MilFmt.N(gold);
                }
                _sub3 = "日饷 " + MilFmt.N(wage) + " 金 · " + payer;
            }
            catch (Exception ex) { DLog.Force("部队管理: 头部刷新失败 " + ex.Message); }
        }

        private void UpdatePreview()
        {
            try
            {
                bool add = _mode == ModeAdd;
                _tabAddColor = add ? "#FFF3C4FF" : "#8A8070FF";
                _tabSplitColor = add ? "#8A8070FF" : "#FFF3C4FF";
                _tabAddBg = add ? "#8C6D1FFF" : "#FFFFFF12";
                _tabSplitBg = add ? "#FFFFFF12" : "#8C6D1FFF";
                _executeText = add ? "确认扩容" : "确认分裂";
                _executeColor = add ? "#7FBF6AFF" : "#D96A5AFF";
                _settleText = "征兵地: " + (_settlement != null && _settlement.Name != null ? _settlement.Name.ToString() : "未选择");

                if (_isArmy)
                {
                    _previewTitle = "军团部队";
                    _previewColor = "#D96A5AFF";
                    var sb = new StringBuilder();
                    AppendLine(sb, "该部队属于「多选右键组建」的军团。", 19, 2);
                    AppendLine(sb, "军团整体指挥/解散请在军务页或军团菜单里操作。", 19, 2);
                    AppendLine(sb, "本页的扩容/分裂/合并/解散仅适用于非军团部队。", 19, 2);
                    _previewBody = sb.ToString();
                    return;
                }

                if (add) PreviewAdd();
                else PreviewSplit();
            }
            catch (Exception ex) { DLog.Force("部队管理: 预览失败 " + ex.Message); }
        }

        private void PreviewAdd()
        {
            var sb = new StringBuilder();
            int total = 0;
            long fee = 0;
            bool blocked = false;
            var lines = new List<string>();
            var lg = DefArmy.LegionOf(_party);
            for (int k = 0; k < _add.Length; k++)
            {
                int n = _add[k];
                if (n <= 0) continue;
                total += n;
                LordArmy.KindOption opt;
                string feeText = "";
                if (_opts.TryGetValue(k, out opt))
                {
                    fee += (long)opt.Fee * n;
                    feeText = " · " + opt.Fee + " 金/人";
                }
                lines.Add(UnitName(k) + " ×" + MilFmt.N(n) + feeText);
            }
            _previewTitle = "扩容预览";
            if (lg != null)
            {
                int canG = 0, canR = 0;
                for (int k = 0; k < _add.Length; k++)
                {
                    int n = _add[k];
                    if (n <= 0) continue;
                    int g = Math.Min(n, GarrisonDrawCapOf(k));
                    canG += g;
                    int rem = n - g;
                    if (rem > 0) canR += Math.Min(rem, RecruitDrawCapOf(k));
                }
                long feeR = (long)canR * PerManRecruitCost();
                int wageAdd = (int)Math.Ceiling(total * DefArmy.WagePerManPerDay);
                if (total > canG + canR) blocked = true;
                if (total > _cmdRoom) blocked = true;
                string sn = _settlement != null && _settlement.Name != null ? _settlement.Name.ToString() : "未选";
                AppendLine(sb, "国防军军团: 扩容 = 守备营抽调 或 征兵地招募", 19, 2);
                AppendLine(sb, "总人数: 现有 " + MilFmt.N(_menNow) + " / 上限 " + MilFmt.N(_cmdLimit)
                    + " · 选定 " + MilFmt.N(total) + " 人", 19, 2);
                AppendLine(sb, "来源: 守备营 " + MilFmt.N(canG) + " 人 / 征兵地" + sn + " " + MilFmt.N(canR) + " 人", 19, 2);
                int popAll = 0, slotAll = 0;
                try { popAll = DefArmy.RealCommonersOf(_settlement); } catch { }
                try { slotAll = DefArmy.SlotQuotaOf(_settlement); } catch { }
                AppendLine(sb, "征兵地: 名额 " + (slotAll == int.MaxValue ? "不限(该地无知名人物)" : MilFmt.N(slotAll))
                    + " 人(原版志愿兵, 会刷新) · 平民 " + MilFmt.N(popAll)
                    + " 人 · 编制余额 " + MilFmt.N(_cmdRoom) + " 人(以上都是各兵种共用)", 19, 2);
                AppendLine(sb, "预计花费: 招募 " + MilFmt.N(feeR) + " 金(国库, 含训练费) · 日饷 +" + MilFmt.N(wageAdd) + " 金", 19, 2);
                if (!blocked)
                {
                    for (int i = 0; i < lines.Count && i < 3; i++) AppendLine(sb, lines[i], 19, 1);
                    if (lines.Count > 3) AppendLine(sb, "…共 " + lines.Count + " 个兵种", 19, 1);
                }
                else
                {
                    if (total > canG + canR) AppendLine(sb, "可用不足: 守备营可抽 " + MilFmt.N(canG) + " 人 + 征兵地可招 " + MilFmt.N(canR) + " 人", 19, 1);
                    if (total > _cmdRoom) AppendLine(sb, "超出编制余额(余 " + MilFmt.N(_cmdRoom) + " 人)", 19, 1);
                }
                _previewColor = blocked ? "#D96A5AFF" : (total > 0 ? "#7FBF6AFF" : "#D8C9A0FF");
                _previewBody = sb.ToString();
                return;
            }
            if (total <= 0)
            {
                AppendLine(sb, "拖动每行的拖动条选择要增补的人数。", 19, 2);
                AppendLine(sb, "绿条 = 私库装备可支撑(上限 ∩ 编制余额); 0 的行已置灰并注明原因。", 19, 2);
                AppendLine(sb, "新兵熟练 20~30; 装备从私库扣, 招募费从领主金扣。", 19, 2);
                _previewColor = "#D8C9A0FF";
                _previewBody = sb.ToString();
                return;
            }

            int slots = -1;
            try { slots = _settlement != null ? LordArmy.SlotLeft(_settlement) : -1; } catch { }
            int head = 0;
            try { head = _party.Party.PartySizeLimit - _party.MemberRoster.TotalManCount; } catch { }
            if (head < 0) head = 0;
            Hero payer = Payer();
            int gold = payer != null ? payer.Gold : 0;
            if (_settlement == null) blocked = true;
            if (gold < fee) blocked = true;
            if (slots >= 0 && total > slots) blocked = true;
            if (total > head) blocked = true;
            if (total > _cmdRoom) blocked = true;

            AppendLine(sb, "领主军: 从征兵地补员(装备走私库)", 19, 2);
            AppendLine(sb, "总人数: 现有 " + MilFmt.N(_menNow) + " / 上限 " + MilFmt.N(_cmdLimit)
                + " · 选定 " + MilFmt.N(total) + " 人", 19, 2);
            AppendLine(sb, "来源: " + (_settlement != null && _settlement.Name != null ? _settlement.Name.ToString() : "未选择")
                + (slots >= 0 ? ("(名额 " + MilFmt.N(slots) + ")") : ""), 19, 2);
            AppendLine(sb, "预计花费: " + MilFmt.N(fee) + " 金(领主金 " + MilFmt.N(gold) + ")", 19, 2);
            if (!blocked)
            {
                for (int i = 0; i < lines.Count && i < 3; i++) AppendLine(sb, lines[i], 19, 1);
                if (lines.Count > 3) AppendLine(sb, "…共 " + lines.Count + " 个兵种", 19, 1);
            }
            else
            {
                if (_settlement == null) AppendLine(sb, "请先点下方[选征兵地]", 19, 1);
                if (gold < fee) AppendLine(sb, "领主金不足(差 " + MilFmt.N(fee - gold) + " 金)", 19, 1);
                if (slots >= 0 && total > slots) AppendLine(sb, "名额不足(剩 " + MilFmt.N(slots) + " 人)", 19, 1);
                if (total > head) AppendLine(sb, "超过本队余位(" + MilFmt.N(head) + " 人)", 19, 1);
                if (total > _cmdRoom) AppendLine(sb, "超出编制余额(余 " + MilFmt.N(_cmdRoom) + " 人)", 19, 1);
            }
            _previewColor = blocked ? "#D96A5AFF" : "#7FBF6AFF";
            _previewBody = sb.ToString();
        }

        private void PreviewSplit()
        {
            int total = 0;
            for (int k = 0; k < _split.Length; k++) total += _split[k];
            _previewTitle = "分裂预览";
            int all = 0;
            for (int k = 0; k < _men.Length; k++) all += _men[k];
            int cap = SplitGlobalCap();
            var sb = new StringBuilder();
            if (total <= 0)
            {
                AppendLine(sb, "拖动每行的拖动条选择要分出的兵种与人数。", 19, 2);
                AppendLine(sb, "红条上限 = 该兵种现有人数; 总上限 = 兵力 − " + DefArmy.LegionKeepMin
                    + "(原队至少留 " + DefArmy.LegionKeepMin + " 名正兵)。", 19, 2);
                AppendLine(sb, "本队可分出上限: " + MilFmt.N(cap) + " 人(各兵种行相加到该数即可)", 19, 2);
                AppendLine(sb, "装备随人走; 新部队自动命名「第 N 队」。", 19, 2);
                _previewColor = "#D8C9A0FF";
                _previewBody = sb.ToString();
                return;
            }
            bool bad = total > cap;
            AppendLine(sb, "分裂 = 把选定兵员拆到新部队", 19, 2);
            AppendLine(sb, "总人数: 现有 " + MilFmt.N(all) + " 人", 19, 2);
            AppendLine(sb, "选定: " + MilFmt.N(total) + " 人 -> 新队", 19, 2);
            AppendLine(sb, "原队剩余: " + MilFmt.N(Math.Max(0, all - total)) + " 人", 19, 2);
            int shownKinds = 0;
            for (int k = 0; k < _split.Length; k++)
                if (_split[k] > 0 && shownKinds++ < 4) AppendLine(sb, UnitName(k) + " ×" + MilFmt.N(_split[k]), 19, 1);
            if (shownKinds > 4) AppendLine(sb, "…共 " + shownKinds + " 个兵种", 19, 1);
            AppendLine(sb, "新部队: 「第 " + ArmyMoves.NextSquadNumber() + " 队」 · 装备随人走", 19, 2);
            if (bad) AppendLine(sb, "超出上限: 可分出 " + MilFmt.N(cap) + " 人(原队至少留 "
                + DefArmy.LegionKeepMin + " 名正兵)", 19, 2);
            _previewColor = bad ? "#D96A5AFF" : "#7FBF6AFF";
            _previewBody = sb.ToString();
        }

        // ==================== 拖动条/步进 ====================
        internal int MaxOf(int kind)
        {
            try
            {
                if (kind < 0 || kind >= _men.Length) return 0;
                if (_isArmy) return 0;
                if (_mode == ModeSplit)
                {
                    int others = 0;
                    for (int k = 0; k < _split.Length; k++)
                        if (k != kind && _split[k] > 0) others += _split[k];
                    int cap = SplitGlobalCap() - others;
                    if (cap > _men[kind]) cap = _men[kind];
                    return cap > 0 ? cap : 0;
                }
                var lg = DefArmy.LegionOf(_party);
                if (lg != null)
                {
                    long cap = (long)GarrisonDrawCapOf(kind) + RecruitDrawCapOf(kind);   // ①守备营抽调 + ②征兵地招募
                    if (cap > _cmdRoom) cap = _cmdRoom;
                    cap = SharedRoomOf(kind, cap);   // v4.237: 人口/国库/编制是所有行共用的池子
                    return cap > 0 ? (int)cap : 0;
                }
                LordArmy.KindOption opt;
                if (!_opts.TryGetValue(kind, out opt)) return 0;
                long v2 = opt.MaxN;
                if (_cmdRoom < v2) v2 = _cmdRoom;
                v2 = SharedRoomOf(kind, v2);
                return v2 > 0 ? (int)v2 : 0;
            }
            catch { return 0; }
        }

        // v4.237: 共享池 —— 人口/国库/编制余额是"所有兵种行共用"的, 原来每行独立报同一个上限
        //   (6 个兵种各报 74, 看着像能招 444 人, 实际只有 74 个人); 这里扣掉其它行已占用的部分
        private int SharedRoomOf(int kind, long perKindCap)
        {
            try
            {
                bool defLg = false;
                try { defLg = DefArmy.LegionOf(_party) != null; } catch { }
                int othersAll = 0, othersRecruit = 0;
                for (int k = 0; k < _add.Length; k++)
                {
                    if (k == kind) continue;
                    int s = _add[k];
                    if (s <= 0) continue;
                    othersAll += s;
                    if (defLg)
                    {
                        int g = Math.Min(s, GarrisonDrawCapOf(k));
                        if (s > g) othersRecruit += (s - g);
                    }
                    else othersRecruit += s;
                }
                long room = perKindCap;
                long cmd = (long)_cmdRoom - othersAll;
                if (cmd < room) room = cmd;
                if (defLg)
                {
                    long pop = 0;
                    try { pop = DefArmy.RealCommonersOf(_settlement); } catch { }
                    long popRoom = pop - othersRecruit;
                    if (popRoom < room) room = popRoom;
                    long slots = 0;
                    try { slots = DefArmy.SlotQuotaOf(_settlement); } catch { }
                    if (slots != int.MaxValue)                 // 该地无知名人物 -> 不限名额
                    {
                        long slotRoom = slots - othersRecruit;  // v4.238: 名额也是共用池
                        if (slotRoom < room) room = slotRoom;
                    }
                }
                int per = PerManRecruitCost();
                long goldRoom = 0;
                try { goldRoom = (defLg ? (long)EconomyWorld.Treasury.Gold : (long)(Payer() != null ? Payer().Gold : 0)) / Math.Max(1, per); } catch { }
                goldRoom -= othersRecruit;
                if (goldRoom < room) room = goldRoom;
                return room > 0 ? (int)Math.Min(room, int.MaxValue) : 0;
            }
            catch { return 0; }
        }

        // 来源①: 驻地守备营抽调上限 = 国家军械库武器可装 ∩ 守备营账面 ∩ 编制余额
        private int GarrisonDrawCapOf(int kind)
        {
            try
            {
                int cap = WeaponCapOf(kind, _nationalKey);
                if (_garrisonAvail < cap) cap = _garrisonAvail;
                if (_cmdRoom < cap) cap = _cmdRoom;
                return cap > 0 ? cap : 0;
            }
            catch { return 0; }
        }

        // 来源② 选定征兵地招募上限 = 编制余额 ∩ 可征人口 ∩ 国库 ∩ 国家军械库装备
        //   v4.234: 直接调内核 DefArmy.LegionRecruitCap(与 ReplenishLegionFrom 同一函数), 滑条与执行不再两套口径
        private int RecruitDrawCapOf(int kind)
        {
            try
            {
                if (_settlement == null || _cmdRoom <= 0) return 0;
                return DefArmy.LegionRecruitCap(_party, _settlement, kind);
            }
            catch { return 0; }
        }

        // 征兵每兵费(招募 + 训练; 军务大臣 -10%, 与募兵同口径)
        private static int PerManRecruitCost()
        {
            int per = DefArmy.RecruitCost + DefArmy.LegionTrainCostPerMan;
            try { if (Politics.SeatHeld(3)) per = (int)Math.Round(per * 0.9f); } catch { }
            return per > 0 ? per : 1;
        }

        // 该兵种招募装备可支撑人数: 逐需求标签取军械库现货型号, min(库存×100/每百人)
        private long UnitEquipCapOf(int kind)
        {
            try
            {
                if (string.IsNullOrEmpty(_nationalKey)) return 0;
                var u = Equipment.Unit(kind);
                if (u == null || u.Req == null || u.Req.Length == 0) return long.MaxValue;
                long cap = long.MaxValue;
                for (int r = 0; r < u.Req.Length; r++)
                {
                    var req = u.Req[r];
                    if (req.Per100 <= 0) continue;
                    var m = StockPick(_nationalKey, req.Tag);
                    int have = m != null ? Armory.CountKey(_nationalKey, m.Id) : 0;
                    long c = have * 100L / req.Per100;
                    if (c < cap) cap = c;
                }
                return cap;
            }
            catch { return 0; }
        }

        // 分裂全局上限(v4.235): 两边各至少留 1 名正兵 => 可分出上限 = 正兵 − 1(军团与非军团同口径)
        //   v4.234: 口径从"逐人表条数"改成"名册非英雄人数"(对账后再报), 修 100 人只能分出 65 人
        private int SplitGlobalCap()
        {
            try
            {
                int all = 0;
                try { all = DefArmy.RegularsOf(_party); } catch { }
                if (all <= 0) { try { all = Soldiers.RosterMenOf(_party); } catch { } }
                if (all <= 0) all = _menNow;
                int cap = all - DefArmy.LegionKeepMin;
                return cap > 0 ? cap : 0;
            }
            catch { return 0; }
        }

        // 已选总数超过全局上限时, 从末行起回削(切换模式/换目标后防越界)
        private void ClampSplitSelections()
        {
            try
            {
                if (_mode != ModeSplit) return;
                int cap = SplitGlobalCap();
                int sum = 0;
                for (int k = 0; k < _split.Length; k++) if (_split[k] > 0) sum += _split[k];
                if (sum <= cap) return;
                int over = sum - cap;
                for (int k = _split.Length - 1; k >= 0 && over > 0; k--)
                {
                    if (_split[k] <= 0) continue;
                    int cut = Math.Min(_split[k], over);
                    _split[k] -= cut;
                    over -= cut;
                }
            }
            catch { }
        }

        // 拖动条上限为 0 时给出原因(v4.234): 拖了没反应会让玩家以为界面坏了
        internal void NoteCapReason(int kind)
        {
            try
            {
                if (MaxOf(kind) > 0) return;
                bool defLg = false;
                try { defLg = DefArmy.LegionOf(_party) != null; } catch { }
                if (_mode == ModeSplit)
                {
                    _status = "已达到全局上限(可分出 " + MilFmt.N(SplitGlobalCap()) + " 人, 原队至少留 "
                        + DefArmy.LegionKeepMin + " 名正兵)";
                }
                else
                {
                    _status = CapReasonOf(kind, defLg);
                }
                Notify("StatusLine");
            }
            catch { }
        }

        internal int MenOf(int kind)
        {
            return kind >= 0 && kind < _men.Length ? _men[kind] : 0;
        }

        internal int ValueOf(int kind)
        {
            if (kind < 0 || kind >= _men.Length) return 0;
            return _mode == ModeSplit ? _split[kind] : _add[kind];
        }

        internal void StepRow(int kind, int delta)
        {
            try
            {
                if (_isArmy || kind < 0 || kind >= _men.Length) return;
                int max = MaxOf(kind);
                if (max <= 0 && delta > 0)
                {
                    if (_mode == ModeSplit)
                    {
                        _status = "已达到全局上限(可分出 " + MilFmt.N(SplitGlobalCap()) + " 人, 原队至少留 "
                            + DefArmy.LegionKeepMin + " 名正兵)";
                    }
                    else _status = CapReasonOf(kind, DefArmy.LegionOf(_party) != null);
                    Notify("StatusLine");
                    return;
                }
                int v = ValueOf(kind) + delta;
                if (v < 0) v = 0;
                if (v > max) v = max;
                SetValue(kind, v);
            }
            catch { }
        }

        // 拖动: 按鼠标在轨道上的比例换算人数
        internal void SetRowFromPx(int kind, float mouseX, float trackX, float trackW)
        {
            try
            {
                if (_isArmy || kind < 0 || kind >= _men.Length) return;
                int max = MaxOf(kind);
                if (max <= 0) return;
                float rel = (mouseX - trackX) / Math.Max(1f, trackW);
                if (rel < 0f) rel = 0f;
                if (rel > 1f) rel = 1f;
                SetValue(kind, (int)Math.Round(rel * max));
            }
            catch { }
        }

        private void SetValue(int kind, int v)
        {
            if (v < 0) v = 0;
            int max = MaxOf(kind);   // 分裂: 还会受全局上限(总人数−1 / −50)约束
            if (v > max) v = max;
            if (_mode == ModeSplit)
            {
                if (_split[kind] == v) return;      // 拖动中值未变: 不重复刷新预览
                _split[kind] = v;
            }
            else
            {
                if (_add[kind] == v) return;
                _add[kind] = v;
            }
            for (int i = 0; i < Rows.Count; i++)
                if (Rows[i].Kind == kind) { Rows[i].SetSelection(v, MaxOf(kind), TrackW); break; }
            UpdatePreview();
            Notify("PreviewBody"); Notify("PreviewColor"); Notify("PreviewTitle");
        }

        internal void ClearSel()
        {
            try
            {
                for (int k = 0; k < _men.Length; k++)
                {
                    if (_mode == ModeSplit) _split[k] = 0; else _add[k] = 0;
                }
                for (int i = 0; i < Rows.Count; i++) Rows[i].SetSelection(0, MaxOf(Rows[i].Kind), TrackW);
                UpdatePreview();
                Notify("PreviewBody"); Notify("PreviewColor"); Notify("PreviewTitle");
            }
            catch { }
        }

        internal void SetMode(int m)
        {
            try
            {
                _mode = m == ModeSplit ? ModeSplit : ModeAdd;
                Refresh();   // 模式切换会改变可增补口径/置灰原因, 整页重算
                OnPropertyChangedWithValue(!_isArmy && _mode == ModeAdd, "IsAddMode");
            }
            catch { }
        }

        // ==================== 执行: 扩容 / 分裂 / 合并 / 解散 ====================
        internal void ExecuteApply()
        {
            if (_isArmy) { _status = "军团部队请去军务页操作"; Notify("StatusLine"); return; }
            int sel = 0;
            try
            {
                for (int k = 0; k < _add.Length; k++) sel += _mode == ModeSplit ? _split[k] : _add[k];
            }
            catch { }
            DLog.Force("部队管理: 确认按下 模式=" + (_mode == ModeSplit ? "分裂" : "扩容") + " 已选=" + sel
                + " 征兵地=" + (_settlement != null && _settlement.Name != null ? _settlement.Name.ToString() : "无"));
            if (_mode == ModeSplit) ExecuteSplit(); else ExecuteAdd();
        }

        private void ExecuteAdd()
        {
            try
            {
                int total = 0;
                for (int k = 0; k < _add.Length; k++) total += _add[k];
                if (total <= 0) { _status = "请先拖动条(或点 ±)选择增补人数"; Notify("StatusLine"); return; }

                var lg = DefArmy.LegionOf(_party);
                if (lg != null)
                {
                    if (_cmdRoom <= 0) { _status = "编制已满(现有 " + MilFmt.N(_menNow) + " / 上限 " + MilFmt.N(_cmdLimit) + ")"; Notify("StatusLine"); return; }
                    int gMen = 0, rMen = 0;
                    var failNotes = new List<string>();
                    for (int kk = 0; kk < _add.Length; kk++)
                    {
                        int need = _add[kk];
                        if (need <= 0) continue;
                        int take = Math.Min(need, GarrisonDrawCapOf(kk));
                        if (take > 0)
                        {
                            int before = DefArmy.RegularsOf(_party);
                            try { DefArmy.ReplenishLegion(_party, take); }
                            catch (Exception ex) { DLog.Force("扩容守备营抽调异常: " + ex.Message); }
                            int movedNow = Math.Max(0, DefArmy.RegularsOf(_party) - before);
                            gMen += movedNow;
                            need -= movedNow;
                        }
                        if (need <= 0) continue;
                        if (_settlement == null) { failNotes.Add(UnitName(kk) + ": 未选征兵地"); continue; }
                        string msg2;
                        int before2 = DefArmy.RegularsOf(_party);
                        try { msg2 = DefArmy.ReplenishLegionFrom(_party, _settlement, kk, need); }
                        catch (Exception ex) { msg2 = "征兵补员异常: " + ex.Message; }
                        int got2 = Math.Max(0, DefArmy.RegularsOf(_party) - before2);   // v4.234: 按实际到位人数记账
                        if (got2 > 0) rMen += got2;
                        else failNotes.Add(UnitName(kk) + ": " + Trim(msg2, 46));
                    }
                    for (int kk = 0; kk < _add.Length; kk++) _add[kk] = 0;
                    string done = "扩容完成: 守备营 +" + gMen + " 人 · 征兵地 +" + rMen + " 人";
                    if (failNotes.Count == 0) _status = done;
                    else if (gMen + rMen == 0) _status = "扩容失败: " + string.Join(" | ", failNotes.ToArray());
                    else _status = "部分成功: " + done + "; 失败 " + failNotes.Count + " 项 → " + string.Join(" | ", failNotes.ToArray());
                    DLog.Force("部队管理: " + _status);
                    Refresh();
                    return;
                }

                if (_settlement == null) { _status = "请先选择征兵地(村庄/城镇的名额与刷新照原版)"; Notify("StatusLine"); return; }

                int okKinds = 0, okMen = 0;
                var fails = new List<string>();
                for (int k = 0; k < _add.Length; k++)
                {
                    int n = _add[k];
                    if (n <= 0) continue;
                    string msg = "";
                    try { msg = LordArmy.Recruit(_party, _settlement, k, n); }
                    catch (Exception ex) { msg = "征募异常: " + ex.Message; }
                    if (LooksFail(msg)) fails.Add(UnitName(k) + ": " + Trim(msg, 46));
                    else { okKinds++; okMen += n; _add[k] = 0; }
                }
                if (fails.Count == 0) _status = "扩容完成: 增补 " + okMen + " 人(" + okKinds + " 个兵种)";
                else if (okMen == 0) _status = "扩容失败: " + string.Join(" | ", fails.ToArray());
                else _status = "部分成功: +" + okMen + " 人; 失败 " + fails.Count + " 项 → " + string.Join(" | ", fails.ToArray());
                DLog.Force("部队管理: " + _status);
                Refresh();
            }
            catch (Exception ex) { DLog.Force("扩容异常: " + ex.Message); _status = "扩容异常, 见日志"; Notify("StatusLine"); }
        }

        private void ExecuteSplit()
        {
            try
            {
                int total = 0;
                for (int k = 0; k < _split.Length; k++) total += _split[k];
                if (total <= 0) { _status = "请先拖动条(或点 ±)选择分出人数"; Notify("StatusLine"); return; }

                var lg = DefArmy.LegionOf(_party);
                if (lg != null)
                {
                    _status = DefArmy.SplitLegion(_party, total);   // 按选定人数(内核夹到"两边各留 LegionKeepMin 名正兵")
                    for (int k = 0; k < _split.Length; k++) _split[k] = 0;
                    DLog.Force("部队管理: " + _status);
                    Refresh();
                    return;
                }

                string msg;
                bool ok = ArmyMoves.Split(_party, _split, out msg);
                _status = msg;
                if (ok) for (int k = 0; k < _split.Length; k++) _split[k] = 0;
                DLog.Force("部队管理: " + _status);
                Refresh();
            }
            catch (Exception ex) { DLog.Force("分裂异常: " + ex.Message); _status = "分裂异常, 见日志"; Notify("StatusLine"); }
        }

        internal void ExecuteMerge()
        {
            try
            {
                if (_isArmy) { _status = "军团部队请去军务页操作"; Notify("StatusLine"); return; }
                if (PanelInputGuard.AnyPopupActive()) return;
                var cands = new List<MobileParty>();
                try
                {
                    var c = Campaign.Current;
                    if (c != null && c.MobileParties != null)
                    {
                        for (int i = 0; i < c.MobileParties.Count; i++)
                        {
                            var q = c.MobileParties[i];
                            if (q == null || !q.IsActive || ReferenceEquals(q, _party)) continue;
                            if (q.IsMainParty || q.IsCaravan || q.IsVillager || q.IsMilitia || q.IsGarrison) continue;
                            if (q.Army != null) continue;
                            if (!NationalWillOrders.IsOurs(q)) continue;
                            float d2 = _party.Position.DistanceSquared(q.Position);
                            if (d2 > 150f * 150f) continue;
                            cands.Add(q);
                        }
                    }
                }
                catch { }
                if (cands.Count == 0) { _status = "附近没有可合并的部队(150 距离内, 需本国非军团部队)"; Notify("StatusLine"); return; }
                cands.Sort(delegate (MobileParty a, MobileParty b)
                {
                    try { return _party.Position.DistanceSquared(a.Position).CompareTo(_party.Position.DistanceSquared(b.Position)); }
                    catch { return 0; }
                });
                var options = new List<InquiryElement>();
                for (int i = 0; i < cands.Count && i < 12; i++)
                {
                    var q = cands[i];
                    int men = 0;
                    try { men = Soldiers.CountOf(q); } catch { }
                    string t = MapSelection.NameOf(q) + " · " + men + " 人";
                    options.Add(new InquiryElement(q, t, null, true, "并入本部队(逐人+装备, 受编制上限约束)"));
                }
                PanelInputGuard.ShowPopup(new MultiSelectionInquiryData(
                    "合并部队",
                    "选择要并入「" + MapSelection.NameOf(_party) + "」的部队。合并后目标部队解散, 逐人表与装备并入本队。",
                    options, true, 1, 1, "合并", "取消",
                    OnMergePicked, null, null, false));
            }
            catch (Exception ex) { DLog.Force("合并列表异常: " + ex.Message); _status = "合并列表异常, 见日志"; Notify("StatusLine"); }
        }

        private void OnMergePicked(List<InquiryElement> sel)
        {
            try
            {
                if (sel == null || sel.Count == 0) return;
                var src = sel[0].Identifier as MobileParty;
                if (src == null) return;
                string msg;
                ArmyMoves.Merge(_party, src, out msg);
                _status = msg;
                DLog.Force("部队管理: " + _status);
                Refresh();
            }
            catch (Exception ex) { DLog.Force("合并异常: " + ex.Message); }
        }

        internal void ExecuteDisband()
        {
            try
            {
                if (_party == null) return;
                if (_party.IsMainParty) { _status = "不能解散玩家自己的部队"; Notify("StatusLine"); return; }
                string msg;
                bool ok;
                try { ok = VanillaArmySuppress.OurDisband(_party, out msg); }
                catch (Exception ex) { ok = false; msg = "解散异常: " + ex.Message; }
                _status = !string.IsNullOrEmpty(msg) ? msg : (ok ? "已解散" : "该部队不支持解散");
                DLog.Force("部队管理: " + _status);
                if (ok)
                {
                    try { MapSelection.Message(_status); } catch { }
                    bool destroyed = false;
                    try { destroyed = _party == null || !_party.IsActive || _party.IsDisbanding; } catch { destroyed = true; }
                    if (destroyed) { if (_onClose != null) _onClose(); return; }
                }
                Refresh();
            }
            catch (Exception ex) { DLog.Force("解散异常: " + ex.Message); }
        }

        internal void ExecuteGoMilitary()
        {
            try
            {
                if (_onClose != null) _onClose();
                ArmyPanel.Open();
            }
            catch (Exception ex) { DLog.Force("跳转军务页失败: " + ex.Message); }
        }

        // ==================== 征兵地 ====================
        internal void PickSettlement()
        {
            try
            {
                if (PanelInputGuard.AnyPopupActive()) return;
                var list = new List<Settlement>();
                try
                {
                    var all = Settlement.All;
                    if (all != null)
                    {
                        for (int i = 0; i < all.Count; i++)
                        {
                            var s = all[i];
                            if (s == null || s.MapFaction == null) continue;
                            if (_party.MapFaction != null && !ReferenceEquals(s.MapFaction, _party.MapFaction)) continue;
                            list.Add(s);
                        }
                    }
                }
                catch { }
                if (list.Count == 0) { _status = "本国暂无可用征兵地"; Notify("StatusLine"); return; }

                // v4.234: 列表必须显示"实际可招多少人"(内核同口径), 否则会出现"有名额却招不到"
                bool legion = false;
                try { legion = DefArmy.LegionOf(_party) != null; } catch { }
                int refKind = RefKindForSettle();
                var caps = new Dictionary<Settlement, int>();
                var whys = new Dictionary<Settlement, string>();
                for (int i = 0; i < list.Count; i++)
                {
                    string why;
                    caps[list[i]] = SettleCapOf(list[i], legion, refKind, out why);
                    whys[list[i]] = why;
                }
                list.Sort(delegate (Settlement a, Settlement b)
                {
                    try
                    {
                        int ca = caps.ContainsKey(a) ? caps[a] : 0;
                        int cb = caps.ContainsKey(b) ? caps[b] : 0;
                        bool oa = ca > 0, ob = cb > 0;
                        if (oa != ob) return oa ? -1 : 1;      // 招得到的排前面
                        if (oa && ob && ca != cb) return cb.CompareTo(ca);
                        return Dist2(a).CompareTo(Dist2(b));
                    }
                    catch { return 0; }
                });

                var options = new List<InquiryElement>();
                for (int i = 0; i < list.Count && i < 20; i++)
                {
                    var s = list[i];
                    int slots = 0;
                    try { slots = LordArmy.SlotLeft(s); } catch { }
                    int cap = caps.ContainsKey(s) ? caps[s] : 0;
                    string why = whys.ContainsKey(s) ? whys[s] : "";
                    string t = (s.Name != null ? s.Name.ToString() : s.StringId)
                        + " · 可招 " + MilFmt.N(cap) + " 人 · 名额 " + slots
                        + (cap <= 0 && !string.IsNullOrEmpty(why) ? " · " + why : "");
                    options.Add(new InquiryElement(s, t, null, true,
                        cap > 0 ? "受人口/国库/装备与编制上限共同约束" : "当前招不到, 换个地方"));
                }
                PanelInputGuard.ShowPopup(new MultiSelectionInquiryData(
                    "选择征兵地", "「可招」= 该地此刻真实能补进部队的人数(与执行时同一口径)。",
                    options, true, 1, 1, "选择", "取消",
                    OnSettlementPicked, null, null, false));
            }
            catch (Exception ex) { DLog.Force("征兵地列表异常: " + ex.Message); }
        }

        // 征兵地列表的参考兵种: 已选人数最多的兵种; 没选就用第一个已解锁兵种
        private int RefKindForSettle()
        {
            try
            {
                int best = -1, bestV = 0;
                for (int k = 0; k < _add.Length; k++)
                    if (_add[k] > bestV) { bestV = _add[k]; best = k; }
                if (best >= 0) return best;
                bool legion = false;
                try { legion = DefArmy.LegionOf(_party) != null; } catch { }
                if (!legion && _opts.Count > 0)
                {
                    foreach (var kv in _opts) return kv.Key;
                }
            }
            catch { }
            return Equipment.UMilita;
        }

        // 某聚落此刻真实能补进部队的人数(国防军走内核 LegionRecruitCap; 领主军走 LordArmy.AvailableKinds)
        private int SettleCapOf(Settlement s, bool legion, int refKind, out string why)
        {
            why = "";
            try
            {
                if (legion)
                {
                    int c = DefArmy.LegionRecruitCap(_party, s, refKind);
                    if (c <= 0) why = DefArmy.LegionRecruitBlocker(_party, s, refKind);
                    return c;
                }
                int best = 0;
                var av = LordArmy.AvailableKinds(_party, s);
                if (av != null)
                    for (int i = 0; i < av.Count; i++)
                        if (av[i].MaxN > best) best = av[i].MaxN;
                if (best <= 0) why = "私库装备/名额不足";
                return best;
            }
            catch { return 0; }
        }

        private void OnSettlementPicked(List<InquiryElement> sel)
        {
            try
            {
                if (sel == null || sel.Count == 0) return;
                var s = sel[0].Identifier as Settlement;
                if (s == null) return;
                _settlement = s;
                Refresh();   // 换地后上限口径变化, Refresh 会把已选人数重钳到新上限
                int cap = 0;
                try
                {
                    bool legion = DefArmy.LegionOf(_party) != null;
                    string why;
                    cap = SettleCapOf(s, legion, RefKindForSettle(), out why);
                }
                catch { }
                string nm = s.Name != null ? s.Name.ToString() : s.StringId;
                _status = cap > 0
                    ? ("征兵地已设为 " + nm + " · 可招 " + MilFmt.N(cap) + " 人")
                    : ("征兵地 " + nm + " 当前招不到人(见行内原因), 请换一处");
                Notify("StatusLine");
                DLog.Force("部队管理: 征兵地=" + nm + " 可招=" + cap);
            }
            catch (Exception ex) { DLog.Force("征兵地选择异常: " + ex.Message); }
        }

        private float Dist2(Settlement s)
        {
            try
            {
                float dx = s.Position.X - _party.Position.X;
                float dy = s.Position.Y - _party.Position.Y;
                return dx * dx + dy * dy;
            }
            catch { return float.MaxValue; }
        }

        // ==================== 内部工具 ====================
        private Hero Payer()
        {
            try
            {
                if (_party == null) return null;
                if (_party.LeaderHero != null) return _party.LeaderHero;
                if (_party.ActualClan != null && _party.ActualClan.Leader != null) return _party.ActualClan.Leader;
                if (_party.Party != null && _party.Party.Owner != null) return _party.Party.Owner;
            }
            catch { }
            return null;
        }

        private Settlement DefaultSettlement(MobileParty p)
        {
            try
            {
                if (p == null) return null;
                var cur = p.CurrentSettlement;
                if (cur != null && p.MapFaction != null && ReferenceEquals(cur.MapFaction, p.MapFaction)) return cur;
                Settlement best = null;
                float bd = float.MaxValue;
                var all = Settlement.All;
                if (all == null) return null;
                for (int i = 0; i < all.Count; i++)
                {
                    var s = all[i];
                    if (s == null || s.MapFaction == null) continue;
                    if (p.MapFaction != null && !ReferenceEquals(s.MapFaction, p.MapFaction)) continue;
                    float dx = s.Position.X - p.Position.X;
                    float dy = s.Position.Y - p.Position.Y;
                    float d = dx * dx + dy * dy;
                    if (d < bd) { bd = d; best = s; }
                }
                return best;
            }
            catch { return null; }
        }

        private static string UnitIdOf(int kind)
        {
            try
            {
                var u = Equipment.Unit(kind);
                return u != null ? u.Id : null;
            }
            catch { return null; }
        }

        private static string UnitName(int kind)
        {
            try
            {
                var u = Equipment.Unit(kind);
                return u != null ? u.Name : ("兵种" + kind);
            }
            catch { return "兵种" + kind; }
        }

        // 装备满足率: 逐型号 min(可领/需求); 国防军读国家军械库, 领主军读私库 L:clan
        private static float FillOf(int kind, int men, string ownerKey)
        {
            try
            {
                if (men <= 0 || string.IsNullOrEmpty(ownerKey)) return 1f;
                var need = NeedOfUnit(kind, men, ownerKey);
                if (need.Count == 0) return 1f;
                float worst = 1f;
                foreach (var kv in need)
                {
                    if (kv.Value <= 0) continue;
                    int have = Armory.CountKey(ownerKey, kv.Key);
                    float r = have >= kv.Value ? 1f : have / (float)kv.Value;
                    if (r < worst) worst = r;
                }
                return worst;
            }
            catch { return 1f; }
        }

        // 该兵种 n 人的逐型号需求(优先军械库有库存的高 tier 已解锁型号, 否则用目录兜底型号)
        private static Dictionary<string, int> NeedOfUnit(int kind, int n, string ownerKey)
        {
            var need = new Dictionary<string, int>(StringComparer.Ordinal);
            try
            {
                var u = Equipment.Unit(kind);
                if (u == null || u.Req == null || n <= 0) return need;
                for (int r = 0; r < u.Req.Length; r++)
                {
                    var req = u.Req[r];
                    string id = null;
                    var stock = StockPick(ownerKey, req.Tag);
                    if (stock != null) id = stock.Id;
                    if (string.IsNullOrEmpty(id))
                    {
                        var pick = Equipment.PickForTag(req.Tag, 6);
                        if (pick != null) id = pick.Id;
                    }
                    if (string.IsNullOrEmpty(id)) continue;
                    int q = (int)Math.Ceiling(req.Per100 * (double)n / 100.0);
                    if (q <= 0) continue;
                    int old;
                    need.TryGetValue(id, out old);
                    need[id] = old + q;
                }
            }
            catch { }
            return need;
        }

        private void BuildEquipRows(DefLegion lg, string ownerKey)
        {
            try
            {
                EquipRows.Clear();
                if (_isArmy)
                {
                    EquipRows.Add(new ArmyEquipRowVM("军团部队: 装备按军团快照管理", 0, 0));
                    OnPropertyChangedWithValue(EquipRows.Count, "EquipRows");
                    return;
                }
                string key = lg != null ? _nationalKey : _lordKey;   // 国防军 = 国家军械库, 领主军 = L:clan
                var need = new Dictionary<string, int>(StringComparer.Ordinal);
                var have = new Dictionary<string, int>(StringComparer.Ordinal);
                if (lg != null)
                {
                    var n2 = DefArmy.NeedOf(lg);                     // 编制需求
                    if (n2 != null) foreach (var kv in n2) if (kv.Value > 0) need[kv.Key] = kv.Value;
                    foreach (var kv in need)
                        have[kv.Key] = Armory.CountKey(key, kv.Key); // 可领 = 国家军械库该型号库存
                }
                else
                {
                    for (int k = 0; k < _men.Length; k++)
                    {
                        if (_men[k] <= 0) continue;
                        var needK = NeedOfUnit(k, _men[k], key);
                        foreach (var kv in needK)
                        {
                            int old;
                            need.TryGetValue(kv.Key, out old);
                            need[kv.Key] = old + kv.Value;
                        }
                    }
                    if (!string.IsNullOrEmpty(key))
                    {
                        var entries = Armory.EntriesOf(key);
                        for (int i = 0; i < entries.Count; i++)
                            if (entries[i].Value > 0) have[entries[i].Key] = entries[i].Value;
                    }
                }

                var ids = new List<string>();
                foreach (var kv in need) ids.Add(kv.Key);
                foreach (var kv in have) if (!need.ContainsKey(kv.Key)) ids.Add(kv.Key);
                ids.Sort(delegate (string a, string b)
                {
                    int ga = NeedOf(need, a) - HaveOf(have, a);
                    int gb = NeedOf(need, b) - HaveOf(have, b);
                    if (ga != gb) return gb.CompareTo(ga);      // 缺口大的在前
                    return string.CompareOrdinal(a, b);
                });
                int shown = 0;
                for (int i = 0; i < ids.Count && shown < 6; i++)   // 卡片一屏 6 行(行高 40)
                {
                    string id = ids[i];
                    var def = Equipment.Get(id);
                    if (def == null) continue;                   // 只显示内核装备 id(跳过旧商品名)
                    string name = def.Name;
                    int n = NeedOf(need, id), h = HaveOf(have, id);
                    if (n <= 0 && h <= 0) continue;
                    EquipRows.Add(new ArmyEquipRowVM(name, h, n));   // 缺口 = 编制需求 − 可领
                    shown++;
                }
                if (shown == 0)
                    EquipRows.Add(new ArmyEquipRowVM(lg != null ? "国家军械库暂无该编制库存" : "私库暂无库存/需求", 0, 0));
                OnPropertyChangedWithValue(EquipRows.Count, "EquipRows");
            }
            catch (Exception ex) { DLog.Force("部队管理: 装备区刷新失败 " + ex.Message); }
        }

        private static int NeedOf(Dictionary<string, int> d, string id)
        {
            int v;
            return (d != null && id != null && d.TryGetValue(id, out v)) ? v : 0;
        }

        private static int HaveOf(Dictionary<string, int> d, string id)
        {
            return NeedOf(d, id);
        }

        internal static bool LooksFail(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            return s.IndexOf("失败", StringComparison.Ordinal) >= 0
                || s.IndexOf("不足", StringComparison.Ordinal) >= 0
                || s.IndexOf("无法", StringComparison.Ordinal) >= 0
                || s.IndexOf("未解锁", StringComparison.Ordinal) >= 0
                || s.IndexOf("异常", StringComparison.Ordinal) >= 0
                || s.IndexOf("不能", StringComparison.Ordinal) >= 0;
        }

        private static string Trim(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length <= max ? s : s.Substring(0, max) + "…";
        }
    }

    // ===================== 分裂 / 合并(逐人表 + 名单 + 装备) =====================
    internal static class ArmyMoves
    {
        // 「第 N 队」自动编号(在现有同名部队里找最大 N)
        internal static int NextSquadNumber()
        {
            int max = 0;
            try
            {
                var c = Campaign.Current;
                if (c == null || c.MobileParties == null) return 1;
                for (int i = 0; i < c.MobileParties.Count; i++)
                {
                    var p = c.MobileParties[i];
                    if (p == null || !p.IsActive) continue;
                    int n = ParseSquad(MapSelection.NameOf(p));
                    if (n > max) max = n;
                }
            }
            catch { }
            return max + 1;
        }

        private static int ParseSquad(string name)
        {
            try
            {
                if (string.IsNullOrEmpty(name)) return 0;
                if (!name.StartsWith("第", StringComparison.Ordinal) || !name.EndsWith("队", StringComparison.Ordinal)) return 0;
                string mid = name.Substring(1, name.Length - 2).Trim();
                int n;
                return int.TryParse(mid, out n) ? n : 0;
            }
            catch { return 0; }
        }

        // 分裂: take[k] = 各兵种分出人数(28.6); 逐人表按兵种搬, 名单按分支搬, 新队「第 N 队」
        internal static bool Split(MobileParty src, int[] take, out string msg)
        {
            msg = "";
            try
            {
                int kindCount = Equipment.Units.Count;
                if (src == null || !src.IsActive) { msg = "分裂失败: 部队无效"; return false; }
                var a = Soldiers.Get(src);
                if (a == null) a = Soldiers.Ensure(src);
                int srcMen = Soldiers.CountOf(src);   // v4.234: 先与名册对账, 免得只报旧表人数
                if (a != null && srcMen > a.List.Count) srcMen = a.List.Count;
                if (srcMen <= 0) { msg = "分裂失败: 该部队没有逐人记录"; return false; }

                int total = 0;
                if (take != null)
                    for (int k = 0; k < take.Length && k < kindCount; k++) total += Math.Max(0, take[k]);
                if (total <= 0) { msg = "分裂失败: 未选择分出人数"; return false; }
                if (total >= srcMen) { msg = "分裂失败: 原队至少要留 1 人(现有 " + srcMen + ")"; return false; }

                var home = src.HomeSettlement;
                if (home == null) home = src.CurrentSettlement;

                string pid = "fia_amg_" + Guid.NewGuid().ToString("N");
                MobileParty dst = null;
                try { dst = MobileParty.CreateParty(pid, null); }
                catch (Exception ex) { DLog.Force("分裂: CreateParty 失败 " + ex.Message); }
                if (dst == null) { msg = "分裂失败: 新部队创建失败(见日志)"; return false; }

                // 空名单上建立空逐人表(避免懒初始化按名单重复折算)
                Soldiers.Ensure(dst);
                var b = Soldiers.Get(dst);

                int movedRec = 0;
                for (int k = 0; k < kindCount && k < take.Length; k++)
                {
                    int n = Math.Max(0, take[k]);
                    if (n <= 0 || b == null) continue;
                    byte idx = (byte)k;
                    var pos = new List<int>();
                    for (int i = 0; i < a.List.Count; i++) if (a.List[i].Unit == idx) pos.Add(i);
                    int takeN = Math.Min(n, pos.Count);
                    for (int j = 0; j < takeN; j++)
                    {
                        var rec = a.List[pos[pos.Count - 1 - j]];
                        b.List.Add(new SoldierRec { Unit = rec.Unit, Prof = rec.Prof, Kills = rec.Kills });
                        a.List.RemoveAt(pos[pos.Count - 1 - j]);
                        movedRec++;
                    }
                }
                if (movedRec <= 0)
                {
                    try { DestroyPartyAction.ApplyForDisbanding(dst, home); } catch { }
                    msg = "分裂失败: 选中兵种没有可分出的人";
                    return false;
                }

                var roster = new TroopRoster(dst.Party);
                var prisoners = new TroopRoster(dst.Party);
                MoveTroops(src, roster, take, movedRec);

                int squad = NextSquadNumber();
                try { dst.InitializeMobilePartyAtPosition(roster, prisoners, src.Position, false); }
                catch (Exception ex) { DLog.Force("分裂: 初始化新部队失败 " + ex.Message); }
                try { dst.ActualClan = src.ActualClan; } catch { }
                try { dst.Party.SetCustomName(new TextObject("第 " + squad + " 队")); } catch { }
                if (home != null) { try { dst.SetCustomHomeSettlement(home); } catch { } }
                try { dst.Ai.SetDoNotMakeNewDecisions(true); dst.SetMoveModeHold(); } catch { }

                int dstMen = b != null ? b.List.Count : movedRec;
                msg = "分裂完成: 「" + MapSelection.NameOf(src) + "」分出 " + movedRec + " 人 → 「第 " + squad + " 队」("
                    + Math.Max(0, srcMen - movedRec) + " + " + dstMen + ")";
                DLog.Force("部队管理: " + msg);
                return true;
            }
            catch (Exception ex)
            {
                DLog.Force("分裂异常: " + ex.Message);
                msg = "分裂失败: " + ex.Message;
                return false;
            }
        }

        // 合并: dst += src(逐人 + 名单 + 装备), 受编制上限约束; 双方国防军走 DefArmy.MergeLegions
        internal static bool Merge(MobileParty dst, MobileParty src, out string msg)
        {
            msg = "";
            try
            {
                if (dst == null || src == null || !dst.IsActive || !src.IsActive || ReferenceEquals(dst, src))
                { msg = "合并失败: 部队无效"; return false; }
                if (src.Army != null || dst.Army != null) { msg = "合并失败: 军团部队请先在军团菜单处理"; return false; }

                var dstLg = DefArmy.LegionOf(dst);
                var srcLg = DefArmy.LegionOf(src);
                if (dstLg != null && srcLg != null)
                {
                    msg = DefArmy.MergeLegions(new List<MobileParty> { dst, src });
                    return !ArmyManageVM.LooksFail(msg);
                }

                int lim = ArmyDoctrine.CommandLimitOf(dst);
                Soldiers.Ensure(src);
                int dm = Soldiers.CountOf(dst), sm = Soldiers.CountOf(src);
                if (dm <= 0) dm = SafeTotal(dst);
                if (sm <= 0) sm = SafeTotal(src);
                if (dm + sm > lim)
                { msg = "合并失败: 超过编制上限(" + lim + ", 现有 " + dm + " + " + sm + ")"; return false; }

                // 对方队里有其他英雄 -> 不做(避免英雄无部队可归)
                try
                {
                    foreach (var e in src.MemberRoster.GetTroopRoster())
                    {
                        var ch = e.Character;
                        if (ch == null || !ch.IsHero || e.Number <= 0) continue;
                        if (src.LeaderHero != null && ReferenceEquals(ch, src.LeaderHero)) continue;
                        if (src.Party != null && src.Party.Owner != null && ReferenceEquals(ch, src.Party.Owner)) continue;
                        msg = "合并失败: 对方队中有其他英雄(" + ch.Name + "), 请先安置";
                        return false;
                    }
                }
                catch { }

                int moved = 0;
                Dictionary<string, int> srcNeed = null;
                try { srcNeed = DefArmy.FreeIssueNeed(src); } catch { }   // 搬人前快照
                try { moved = Soldiers.Merge(dst, src); } catch (Exception ex) { DLog.Force("合并: 逐人表并入失败 " + ex.Message); }
                int rosterMoved = MoveAllTroops(src, dst);
                MoveEquip(dstLg, dst, srcLg, src, srcNeed);

                var home = src.CurrentSettlement;
                if (home == null && dst.CurrentSettlement != null) home = dst.CurrentSettlement;
                try { DestroyPartyAction.ApplyForDisbanding(src, home); }
                catch { try { DisbandPartyAction.StartDisband(src); } catch { } }

                msg = "合并完成: 「" + MapSelection.NameOf(src) + "」并入「" + MapSelection.NameOf(dst)
                    + "」(逐人 " + Math.Max(moved, rosterMoved) + " 人 · 编制 " + (dm + sm) + "/" + lim + ")";
                DLog.Force("部队管理: " + msg);
                return true;
            }
            catch (Exception ex)
            {
                DLog.Force("合并异常: " + ex.Message);
                msg = "合并失败: " + ex.Message;
                return false;
            }
        }

        private static int SafeTotal(MobileParty p)
        {
            try { return p != null && p.MemberRoster != null ? p.MemberRoster.TotalManCount : 0; }
            catch { return 0; }
        }

        private static int BranchOf(int kind)
        {
            switch (kind)
            {
                case Equipment.UDragoon:
                case Equipment.UHussar:
                case Equipment.UCuirassier:
                case Equipment.UHorseArcher:
                    return 2;
                case Equipment.UArcher:
                case Equipment.UCrossbow:
                    return 1;
                default:
                    return 0;
            }
        }

        private static CharacterObject TroopFor(MobileParty p, int branch)
        {
            try
            {
                CultureObject culture = null;
                if (p != null && p.Party != null) culture = p.Party.Culture;
                if (culture == null && p != null && p.ActualClan != null) culture = p.ActualClan.Culture;
                if (culture == null && p != null && p.LeaderHero != null) culture = p.LeaderHero.Culture;
                var t = DefArmy.TroopsOf(culture);
                if (t == null) return null;
                if (branch < 0) branch = 0;
                if (branch > 2) branch = 2;
                return t[branch];
            }
            catch { return null; }
        }

        // 从 src 名单移出所选兵种对应分支的原版兵到 to(尽力而为)
        private static int MoveTroops(MobileParty src, TroopRoster to, int[] take, int want)
        {
            int moved = 0;
            try
            {
                if (src == null || to == null || src.MemberRoster == null || take == null) return 0;
                for (int k = 0; k < take.Length && k < Equipment.Units.Count; k++)
                {
                    int n = Math.Max(0, take[k]);
                    if (n <= 0) continue;
                    var ch = TroopFor(src, BranchOf(k));
                    if (ch == null) continue;
                    int have = src.MemberRoster.GetTroopCount(ch);
                    int t = Math.Min(n, have);
                    if (t <= 0) continue;
                    src.MemberRoster.AddToCounts(ch, -t, false, 0, 0, true, -1);
                    to.AddToCounts(ch, t, false, 0, 0, true, -1);
                    moved += t;
                }
                // 名单数与逐人表脱节时: 用任意兵种补足, 保证新队"看得见"
                int guard = 0;
                while (moved < want && guard++ < 8)
                {
                    bool any = false;
                    for (int b = 0; b < 3 && moved < want; b++)
                    {
                        var ch = TroopFor(src, b);
                        if (ch == null) continue;
                        int have = src.MemberRoster.GetTroopCount(ch);
                        if (have <= 0) continue;
                        int t = Math.Min(have, want - moved);
                        src.MemberRoster.AddToCounts(ch, -t, false, 0, 0, true, -1);
                        to.AddToCounts(ch, t, false, 0, 0, true, -1);
                        moved += t; any = true;
                    }
                    if (!any) break;
                }
            }
            catch (Exception ex) { DLog.Force("分裂: 名单搬运失败 " + ex.Message); }
            return moved;
        }

        // 合并: 除英雄外全部兵员(含伤员)搬入
        private static int MoveAllTroops(MobileParty src, MobileParty dst)
        {
            int moved = 0;
            try
            {
                if (src == null || dst == null || src.MemberRoster == null || dst.MemberRoster == null) return 0;
                var from = src.MemberRoster;
                var to = dst.MemberRoster;
                var snap = new List<TroopRosterElement>();
                foreach (var e in from.GetTroopRoster()) snap.Add(e);
                for (int i = 0; i < snap.Count; i++)
                {
                    var e = snap[i];
                    var ch = e.Character;
                    if (ch == null || ch.IsHero || e.Number <= 0) continue;
                    to.AddToCounts(ch, e.Number, false, e.WoundedNumber, 0, false, -1);
                    from.AddToCounts(ch, -e.Number, false, -e.WoundedNumber, 0, false, -1);
                    moved += e.Number;
                }
            }
            catch (Exception ex) { DLog.Force("合并: 名单搬运失败 " + ex.Message); }
            return moved;
        }

        // 装备并入: 领主私库/国家库之间搬运; 国防军快照一并处理
        private static void MoveEquip(DefLegion dstLg, MobileParty dst, DefLegion srcLg, MobileParty src, Dictionary<string, int> srcNeed)
        {
            try
            {
                string dstKey = "";
                string srcKey = "";
                try { dstKey = Armory.KeyOf(dst); } catch { }
                try { srcKey = Armory.KeyOf(src); } catch { }

                var need = srcNeed;
                if (need != null && !string.IsNullOrEmpty(dstKey) && !string.IsNullOrEmpty(srcKey) && srcKey != dstKey)
                {
                    foreach (var kv in need)
                    {
                        if (kv.Value <= 0) continue;
                        int got = Armory.TakeKey(srcKey, kv.Key, kv.Value);
                        if (got > 0) Armory.AddKey(dstKey, kv.Key, got);
                    }
                }

                if (srcLg != null && dstLg != null)
                {
                    // 已在 MergeLegions 处理
                }
                else if (srcLg != null && !string.IsNullOrEmpty(dstKey))
                {
                    Dictionary<string, int> snap;
                    if (!string.IsNullOrEmpty(src.StringId) && DefArmy.LegionEquip.TryGetValue(src.StringId, out snap) && snap != null)
                    {
                        foreach (var kv in snap)
                            if (kv.Value > 0) Armory.AddKey(dstKey, kv.Key, kv.Value);
                        DefArmy.LegionEquip.Remove(src.StringId);
                    }
                    DefArmy.Legions.Remove(srcLg);
                }
            }
            catch (Exception ex) { DLog.Force("合并: 装备并入失败 " + ex.Message); }
        }
    }
}
