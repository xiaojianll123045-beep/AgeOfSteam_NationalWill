using System;
using System.Collections.Generic;
using TaleWorlds.Library;

namespace FeudalInternalAffairs
{
    // ===================== 旧派系页行(文档 21.3/21.5) =====================
    public class LordRowVM : ViewModel
    {
        internal readonly string ClanId;
        private readonly string _name, _clout, _attitude, _attitudeColor, _anger, _angerColor, _tendency, _seat;

        internal LordRowVM(LordProfile lp)
        {
            ClanId = lp.ClanId;
            string nm = lp.Name ?? "?";
            _name = nm.Length > 5 ? nm.Substring(0, 5) : nm;
            _clout = lp.Clout.ToString("N0");
            _attitude = (lp.Attitude >= 0 ? "+" : "") + lp.Attitude;
            _attitudeColor = lp.Attitude >= 40 ? "#39FF14FF" : (lp.Attitude <= -30 ? "#D96A5AFF" : "#C8B98FFF");
            _anger = lp.Anger.ToString("N0");
            _angerColor = lp.Anger >= 75 ? "#FF4B4BFF" : (lp.Anger >= 40 ? "#E8C33AFF" : "#39FF14FF");
            _tendency = Politics.TendencyNames[lp.Tendency >= 0 && lp.Tendency < 4 ? lp.Tendency : 1];
            _seat = lp.Seat >= 0 && lp.Seat < 5 ? Politics.SeatNames[lp.Seat] : "—";
        }

        [DataSourceProperty] public string Name { get { return _name; } }
        [DataSourceProperty] public string Clout { get { return _clout; } }
        [DataSourceProperty] public string Attitude { get { return _attitude; } }
        [DataSourceProperty] public string AttitudeColor { get { return _attitudeColor; } }
        [DataSourceProperty] public string Anger { get { return _anger; } }
        [DataSourceProperty] public string AngerColor { get { return _angerColor; } }
        [DataSourceProperty] public string Tendency { get { return _tendency; } }
        [DataSourceProperty] public string Seat { get { return _seat; } }
    }

    public class PetitionRowVM : ViewModel
    {
        internal readonly int Index;
        private readonly string _text;

        internal PetitionRowVM(int index, string text)
        {
            Index = index;
            _text = text;
        }

        [DataSourceProperty] public string Text { get { return _text; } }
    }

    // ===================== 集团行(文档 24.2; V3 参考: 内阁/在野 左右竖排列表) =====================
    public class GroupRowVM : ViewModel
    {
        internal readonly int GroupIndex;
        private readonly string _name, _nameColor, _leader, _rank, _cloutText, _cloutBarColor, _satText, _satColor, _satBarColor, _btnText, _btnColor, _icon;
        private readonly float _cloutBarW, _satBarW;

        internal GroupRowVM(int g)
        {
            GroupIndex = g;
            var grp = InterestGroups.G[g];
            _name = grp.Name;
            _leader = "领袖 " + (grp.Leader ?? "—");
            _icon = grp.Icon;
            _rank = "";   // 用户要求: 内阁成员不显示序号
            _cloutText = "影响力 " + ((int)grp.Clout).ToString("N0") + " · " + (int)Math.Round(grp.Share * 100) + "%";
            _cloutBarW = (float)Math.Max(2.0, 170.0 * grp.Share);
            _cloutBarColor = grp.Share >= 0.25f ? "#C9A227FF" : (grp.Share >= 0.12f ? "#A89F82FF" : "#6A6355FF");
            _satText = InterestGroups.SatLevelName(g) + " " + grp.Satisfaction;
            _satColor = ColorOfSat(grp.Satisfaction);
            _satBarColor = _satColor;
            _satBarW = (float)Math.Max(2.0, 140.0 * (grp.Satisfaction + 100) / 200.0);
            bool marg = InterestGroups.IsMarginalized(g);
            _nameColor = marg ? "#8A8070FF" : "#F0E4C8FF";
            _btnText = g == 0 ? "元首" : (marg ? "边缘" : (grp.InGov ? "出阁" : "入阁"));
            _btnColor = g == 0 ? "#8A8070FF" : (marg ? "#6A6355FF" : (grp.InGov ? "#D96A5AFF" : "#7FBF6AFF"));
        }

        internal static string ColorOfSat(int sat)
        {
            if (sat >= 30) return "#7FBF6AFF";
            if (sat >= 0) return "#B8C060FF";
            if (sat >= -30) return "#E8C33AFF";
            return "#D96A5AFF";
        }

        // V3 风格排名徽章(按影响力占比)
        internal static int RankOf(int g)
        {
            try
            {
                int rank = 1;
                for (int i = 0; i < InterestGroups.GroupCount; i++)
                    if (i != g && InterestGroups.G[i] != null && InterestGroups.G[i].Share > InterestGroups.G[g].Share) rank++;
                return rank;
            }
            catch { return g + 1; }
        }

        [DataSourceProperty] public string Rank { get { return _rank; } }

        [DataSourceProperty] public string Name { get { return _name; } }
        [DataSourceProperty] public string NameColor { get { return _nameColor; } }
        [DataSourceProperty] public string LeaderText { get { return _leader; } }
        [DataSourceProperty] public string Icon { get { return _icon; } }
        // v4.195: 意识形态图标(IG 主导意识形态)
        [DataSourceProperty] public string IdeoIcon { get { try { return Parties.IdeologyIconOf(GroupIndex); } catch { return "fia_ideo_loyalist"; } } }
        [DataSourceProperty] public string IdeoName { get { try { return Parties.IdeologyNameOf(GroupIndex); } catch { return ""; } } }
        [DataSourceProperty] public string CloutText { get { return _cloutText; } }
        [DataSourceProperty] public float CloutBarW { get { return _cloutBarW; } }
        [DataSourceProperty] public string CloutBarColor { get { return _cloutBarColor; } }
        [DataSourceProperty] public string SatText { get { return _satText; } }
        [DataSourceProperty] public string SatColor { get { return _satColor; } }
        [DataSourceProperty] public string SatBarColor { get { return _satBarColor; } }
        [DataSourceProperty] public float SatBarW { get { return _satBarW; } }
        [DataSourceProperty] public string BtnText { get { return _btnText; } }
        [DataSourceProperty] public string BtnColor { get { return _btnColor; } }
    }

    // ===================== 法律列表行(类别标题 + 法律行) =====================
    public class LawItemVM : ViewModel
    {
        internal readonly int Law;       // -1 = 类别标题(用分类序号)
        private readonly bool _header, _selected, _isBill;
        private readonly string _name, _tier, _nameColor, _tierColor, _bg;

        internal LawItemVM(int law, bool header, bool selected)
        {
            Law = law; _header = header; _selected = selected;
            int lv = header ? 0 : LawSystem.Level(law);
            bool bill = !header && LawSystem.InBill && LawSystem.BillLaw == law;
            _isBill = bill;
            if (header)
            {
                _name = LawSystem.CatOrder[law];
                _nameColor = "#C9A227FF";
                _tier = "";
                _tierColor = "#8A8070FF";
            }
            else
            {
                _name = LawSystem.NameOf(law);
                // 选中 = 金色; 已实施过(档>0) = 米白; 未实施 = 灰
                _nameColor = selected ? "#E8C33AFF" : (lv > 0 ? "#D8CCACFF" : "#9A9078FF");
                _tier = bill ? ("审议中 " + (int)LawSystem.BillProgress + "%") : LawSystem.TierName(law, lv);
                _tierColor = bill ? "#E8C33AFF" : (lv >= 3 ? "#C9A227FF" : (lv > 0 ? "#9FB08AFF" : "#8A8070FF"));
            }
            _bg = selected ? "#FFFFFF14" : "#00000000";
        }

        [DataSourceProperty] public string Name { get { return _name; } }
        [DataSourceProperty] public string Tier { get { return _tier; } }
        [DataSourceProperty] public string NameColor { get { return _nameColor; } }
        [DataSourceProperty] public string TierColor { get { return _tierColor; } }
        [DataSourceProperty] public string Bg { get { return _bg; } }
        [DataSourceProperty] public bool IsHeader { get { return _header; } }
        [DataSourceProperty] public bool IsLaw { get { return !_header; } }
        [DataSourceProperty] public bool IsSelected { get { return _selected; } }
        [DataSourceProperty] public bool IsBill { get { return _isBill; } }
        // v4.191: 法律图标(圆角方形新标准 fia_law_<id>)
        [DataSourceProperty]
        public string Icon
        {
            get
            {
                try
                {
                    if (_header || Law < 0) return "";
                    var d = LawSystem.Def(Law);
                    return d != null ? ("fia_law_" + d.Id) : "";
                }
                catch { return ""; }
            }
        }
    }

    // ===================== 法律档位块(4 档条) =====================
    // ===================== 法律卡(v4.142 V3 形式: 组内可选"法律"列表, 点击选为目标) =====================
    public class LawTierVM : ViewModel
    {
        internal readonly int Tier;
        private readonly string _text, _bg, _fg, _effect, _stanceText, _stanceColor, _stateText, _stateColor, _sideColor;
        private readonly bool _cur, _isTarget;

        internal LawTierVM(int law, int tier, int target)
        {
            Tier = tier;
            _text = LawSystem.TierName(law, tier);
            int cur = LawSystem.Level(law);
            _cur = tier == cur;
            _isTarget = tier == target && target != cur;
            _bg = _cur ? "#C9A2272A" : (_isTarget ? "#FFFFFF1E" : "#FFFFFF0C");
            _fg = _cur ? "#E8C33AFF" : (tier > cur ? "#D8CCACFF" : "#9A9078FF");
            _sideColor = _cur ? "#C9A227FF" : (_isTarget ? "#6AB0FFFF" : "#00000000");
            _effect = LawSystem.EffectOfTier(law, tier);
            if (_cur)
            {
                _stanceText = "现行法律";
                _stanceColor = "#8A8070FF";
            }
            else
            {
                int sup = (int)Math.Round(LawSystem.SupportPctFor(law, tier) * 100f);
                int opp = (int)Math.Round(LawSystem.OpposePctFor(law, tier) * 100f);
                _stanceText = "支持 " + sup + "% / 反对 " + opp + "%";
                _stanceColor = sup >= opp ? "#7FBF6AFF" : "#D96A5AFF";
            }
            _stateText = _cur ? "当前" : (_isTarget ? "◆ 目标" : "");
            _stateColor = _cur ? "#C9A227FF" : "#6AB0FFFF";
        }

        [DataSourceProperty] public string Text { get { return _text; } }
        [DataSourceProperty] public string Bg { get { return _bg; } }
        [DataSourceProperty] public string Fg { get { return _fg; } }
        [DataSourceProperty] public string Effect { get { return _effect; } }
        [DataSourceProperty] public string StanceText { get { return _stanceText; } }
        [DataSourceProperty] public string StanceColor { get { return _stanceColor; } }
        [DataSourceProperty] public string StateText { get { return _stateText; } }
        [DataSourceProperty] public string StateColor { get { return _stateColor; } }
        [DataSourceProperty] public string SideColor { get { return _sideColor; } }
        [DataSourceProperty] public bool IsCurrent { get { return _cur; } }
        [DataSourceProperty] public bool IsTarget { get { return _isTarget; } }
    }

    // ===================== 运动卡(文档 24.4) =====================
    // v4.195: 政党席位行(图标 fia_party_*, 支持度 = 对齐 IG 的政治力量占比)
    public class PartyRowVM : ViewModel
    {
        internal readonly string PartyId;
        private readonly string _icon, _name, _seats, _share, _badge, _badgeColor, _plate;
        private readonly float _barW;
        internal PartyRowVM(PartyDef p, bool ruling)
        {
            PartyId = p.Id;
            _icon = p.Icon; _name = p.Name;
            int seats = Parties.SeatsOf(p.Id);
            _seats = seats + " 席";
            _share = (Parties.SupportOf(p.Id) * 100f).ToString("F0") + "%";
            _badge = ruling ? "执政" : "在野";
            _badgeColor = ruling ? "#E8C33AFF" : "#8A8070FF";
            _plate = ruling ? "#C9A2272A" : "#FFFFFF0C";
            _barW = 140f * Parties.SupportOf(p.Id);
        }
        [DataSourceProperty] public string Icon { get { return _icon; } }
        [DataSourceProperty] public string Name { get { return _name; } }
        [DataSourceProperty] public string Seats { get { return _seats; } }
        [DataSourceProperty] public string Share { get { return _share; } }
        [DataSourceProperty] public string Badge { get { return _badge; } }
        [DataSourceProperty] public string BadgeColor { get { return _badgeColor; } }
        [DataSourceProperty] public string Plate { get { return _plate; } }
        [DataSourceProperty] public float BarW { get { return _barW; } }
    }

    public class MovementVM : ViewModel
    {
        internal readonly int Index;
        private readonly string _title, _info, _radText, _radColor;
        private readonly float _radW;

        internal MovementVM(int idx, Movement m)
        {
            Index = idx;
            string nm = InterestGroups.NameOf(m.Group);
            if (m.Law >= 0)
                _title = nm + " · 诉求《" + LawSystem.NameOf(m.Law) + "》→ " + LawSystem.TierName(m.Law, LawSystem.NextTier(m.Law));
            else
                _title = nm + " · 要求王室让步(补贴/减税)";
            _info = "支持人口 " + (int)Math.Round(m.Support * 100) + "% · 激进度 " + (int)m.Radical + "%"
                + " · 始于第 " + m.StartDay + " 天";
            _radText = (int)m.Radical + "%";
            _radColor = m.Radical >= 80f ? "#FF4B4BFF" : (m.Radical >= 50f ? "#E8C33AFF" : "#B8C060FF");
            _radW = (float)Math.Max(2.0, 900.0 * m.Radical / 100.0);
        }

        [DataSourceProperty] public string Title { get { return _title; } }
        [DataSourceProperty] public string Info { get { return _info; } }
        [DataSourceProperty] public string RadicalText { get { return _radText; } }
        [DataSourceProperty] public string RadicalColor { get { return _radColor; } }
        [DataSourceProperty] public float RadicalW { get { return _radW; } }
    }

    // ===================== 政治页主 VM(文档 24.14: 4 页签; v5.0-P22b: 全屏 + 大字号) =====================
    public class PoliticsPanelVM : PanelVMBase
    {
        private readonly Action _onClose;
        private const int MaxRows = 5;          // 派系页领主名册可见行
        private const int LawRowsWindow = 13;   // 法律页左列表可见行(超出滚动)
        private readonly List<LordRowVM> _allRows = new List<LordRowVM>();
        private readonly List<LawItemVM> _lawAll = new List<LawItemVM>();
        private int _scroll;
        private int _lawScroll;
        private int _tab;              // 0=集团 1=法律 2=运动 3=派系
        private int _lawSelected;

        // 概览
        private string _authText = "", _authLabel = "", _legitText = "", _legitLabel = "", _legitBarColor = "", _cabinetText = "", _cabinetBarColor = "";
        private float _legitBarW, _cabinetBarW;
        private string _situationText = "", _situationColor = "", _status = "", _legitBreak = "", _revText = "";
        private string _mobText = "", _mobBtnText = "动员令", _mobBtnColor = "#6AB0FFFF";
        // 法律详情
        private int _lawNow = -1;
        private int _lawViewTier = -1;   // 点档位条查看某档效果(-1 = 当前档)
        private string _lawName = "", _lawCatNow = "", _lawEffect = "", _lawSupportText = "", _lawOpposeText = "";
        private string _lawSuccessText = "", _lawProgressText = "", _lawProgressColor = "", _lawHint = "", _lawBtnMain = "", _lawBtnForce = "", _lawBtnMainColor = "";
        private float _lawSupportW, _lawOpposeW, _lawProgressW;
        private bool _lawProgressVisible, _lawHasBill, _lawMaxed, _lawSelectedAny, _lawForceVisible, _lawSupportVisible, _lawMaxedVisible;
        private string _lawMaxedHint = "";
        // 派系
        private string _warText = "", _warColor = "", _councilText = "", _cloutText = "", _petitionHint = "";
        private bool _factionNoPetition;

        public PoliticsPanelVM(Action onClose)
        {
            _onClose = onClose;
            Rows = new MBBindingList<LordRowVM>();
            PetitionRows = new MBBindingList<PetitionRowVM>();
            CabinetRows = new MBBindingList<GroupRowVM>();
            OppositionRows = new MBBindingList<GroupRowVM>();
            LawItems = new MBBindingList<LawItemVM>();
            LawTiers = new MBBindingList<LawTierVM>();
            MovementRows = new MBBindingList<MovementVM>();
        PartyRows = new MBBindingList<PartyRowVM>();
            Refresh();
        }

        public MBBindingList<LordRowVM> Rows { get; private set; }
        public MBBindingList<PetitionRowVM> PetitionRows { get; private set; }
        public MBBindingList<GroupRowVM> CabinetRows { get; private set; }
        public MBBindingList<GroupRowVM> OppositionRows { get; private set; }
        public MBBindingList<LawItemVM> LawItems { get; private set; }
        public MBBindingList<LawTierVM> LawTiers { get; private set; }
        public MBBindingList<MovementVM> MovementRows { get; private set; }
        public MBBindingList<PartyRowVM> PartyRows { get; private set; }        // v4.195: 政党席位
        [DataSourceProperty] public string PartyTitle { get { return _partyTitle; } }

        // ---- 全屏布局(v5.0-P22b) ----
        [DataSourceProperty]
        public float PanelWidth
        {
            get { try { return PanelScreen.FullWidth(); } catch { return 1856f; } }
        }
        [DataSourceProperty]
        public float ContentPad
        {
            get { try { return Math.Max(0f, (PanelScreen.FullWidth() - 1700f) / 2f); } catch { return 0f; } }
        }

        // ---- 概览 ----
        [DataSourceProperty] public string AuthNumText { get { return _authText; } }
        [DataSourceProperty] public string AuthLabelText { get { return _authLabel; } }
        [DataSourceProperty] public string LegitText { get { return _legitText; } }
        [DataSourceProperty] public string LegitLabelText { get { return _legitLabel; } }
        [DataSourceProperty] public string LegitBreakText { get { return _legitBreak; } }
        [DataSourceProperty] public float LegitBarW { get { return _legitBarW; } }
        [DataSourceProperty] public string LegitBarColor { get { return _legitBarColor; } }
        [DataSourceProperty] public string CabinetText { get { return _cabinetText; } }
        [DataSourceProperty] public float CabinetBarW { get { return _cabinetBarW; } }
        [DataSourceProperty] public string CabinetBarColor { get { return _cabinetBarColor; } }
        [DataSourceProperty] public string SituationText { get { return _situationText; } }
        [DataSourceProperty] public string SituationColor { get { return _situationColor; } }
        [DataSourceProperty] public string StatusText { get { return _status; } }

        // ---- 法律 ----
        [DataSourceProperty] public string LawName { get { return _lawName; } }
        [DataSourceProperty] public string LawCatNow { get { return _lawCatNow; } }
        [DataSourceProperty] public string LawEffect { get { return _lawEffect; } }
        [DataSourceProperty] public string LawSupportText { get { return _lawSupportText; } }
        [DataSourceProperty] public string LawOpposeText { get { return _lawOpposeText; } }
        [DataSourceProperty] public float LawSupportW { get { return _lawSupportW; } }
        [DataSourceProperty] public float LawOpposeW { get { return _lawOpposeW; } }
        [DataSourceProperty] public string LawSuccessText { get { return _lawSuccessText; } }
        [DataSourceProperty] public string LawProgressText { get { return _lawProgressText; } }
        [DataSourceProperty] public string LawProgressColor { get { return _lawProgressColor; } }
        [DataSourceProperty] public float LawProgressW { get { return _lawProgressW; } }
        [DataSourceProperty] public bool LawProgressVisible { get { return _lawProgressVisible; } }
        [DataSourceProperty] public bool LawHasBill { get { return _lawHasBill; } }
        [DataSourceProperty] public bool LawSelectedAny { get { return _lawSelectedAny; } }
        [DataSourceProperty] public string LawBtnMain { get { return _lawBtnMain; } }
        [DataSourceProperty] public string LawBtnForce { get { return _lawBtnForce; } }
        [DataSourceProperty] public string LawBtnMainColor { get { return _lawBtnMainColor; } }
        [DataSourceProperty] public bool LawForceVisible { get { return _lawForceVisible; } }
        [DataSourceProperty] public bool LawSupportVisible { get { return _lawSupportVisible; } }
        [DataSourceProperty] public bool LawMaxedVisible { get { return _lawMaxedVisible; } }
        [DataSourceProperty] public string LawMaxedHint { get { return _lawMaxedHint; } }
        [DataSourceProperty] public string LawHint { get { return _lawHint; } }

        // ---- 派系 ----
        [DataSourceProperty] public string WarText { get { return _warText; } }
        [DataSourceProperty] public string WarColor { get { return _warColor; } }
        [DataSourceProperty] public string CouncilText { get { return _councilText; } }
        [DataSourceProperty] public string CloutText { get { return _cloutText; } }
        [DataSourceProperty] public string PetitionHint { get { return _petitionHint; } }
        [DataSourceProperty] public bool FactionNoPetition { get { return _factionNoPetition; } }
        [DataSourceProperty] public string MobText { get { return _mobText; } }
        [DataSourceProperty] public string MobBtnText { get { return _mobBtnText; } }
        [DataSourceProperty] public string MobBtnColor { get { return _mobBtnColor; } }

        public void ExecuteClose() { if (_onClose != null) _onClose(); }

        internal int ShownCount { get { return Rows.Count; } }
        internal int SelectedLaw { get { return _lawSelected; } }
        internal int Tab { get { return _tab; } }

        internal LordRowVM RowAt(int i)
        {
            try { return i >= 0 && i < Rows.Count ? Rows[i] : null; } catch { return null; }
        }

        internal void ScrollStep(int dir)
        {
            try
            {
                if (_tab == 1) { _lawScroll += dir; ApplyLawWindow(); return; }   // 法律列表滚动(v5.0-P22b)
                if (_tab != 3) return;
                int max = Math.Max(0, _allRows.Count - MaxRows);
                _scroll += dir;
                if (_scroll < 0) _scroll = 0;
                if (_scroll > max) _scroll = max;
                RebuildRows();
            }
            catch { }
        }

        internal LawItemVM LawItemAt(int i)
        {
            try { return (i >= 0 && i < LawItems.Count) ? LawItems[i] : null; } catch { return null; }
        }

        // ---- 页签 ----
        internal void SetTab(int t)
        {
            try { _tab = t < 0 ? 0 : (t > 3 ? 3 : t); Refresh(); }
            catch { }
        }

        private string _partyTitle = "政党席位";
        [DataSourceProperty] public bool ShowGroup { get { return _tab == 0; } }
        [DataSourceProperty] public bool ShowLaw { get { return _tab == 1; } }
        [DataSourceProperty] public bool ShowMove { get { return _tab == 2; } }
        [DataSourceProperty] public bool ShowFaction { get { return _tab == 3; } }
        [DataSourceProperty] public string TabGroupColor { get { return _tab == 0 ? "#E8C33AFF" : "#8A8070FF"; } }
        [DataSourceProperty] public string TabLawColor { get { return _tab == 1 ? "#E8C33AFF" : "#8A8070FF"; } }
        [DataSourceProperty] public string TabMoveColor { get { return _tab == 2 ? "#E8C33AFF" : "#8A8070FF"; } }
        [DataSourceProperty] public string TabFactionColor { get { return _tab == 3 ? "#E8C33AFF" : "#8A8070FF"; } }
        [DataSourceProperty] public bool TabLineGroup { get { return _tab == 0; } }
        [DataSourceProperty] public bool TabLineLaw { get { return _tab == 1; } }
        [DataSourceProperty] public bool TabLineMove { get { return _tab == 2; } }
        [DataSourceProperty] public bool TabLineFaction { get { return _tab == 3; } }
        [DataSourceProperty] public bool MovementEmpty { get { return MovementRows.Count == 0; } }
        [DataSourceProperty] public bool OppositionEmpty { get { return OppositionRows.Count == 0; } }
        [DataSourceProperty] public bool LawEmpty { get { return !_lawSelectedAny; } }
        [DataSourceProperty] public string RevText { get { return _revText; } }
        [DataSourceProperty] public bool RevVisible { get { return !string.IsNullOrEmpty(_revText); } }

        private void NotifyTabs()
        {
            OnPropertyChangedWithValue(ShowGroup, "ShowGroup");
            OnPropertyChangedWithValue(ShowLaw, "ShowLaw");
            OnPropertyChangedWithValue(ShowMove, "ShowMove");
            OnPropertyChangedWithValue(ShowFaction, "ShowFaction");
            OnPropertyChangedWithValue(TabGroupColor, "TabGroupColor");
            OnPropertyChangedWithValue(TabLawColor, "TabLawColor");
            OnPropertyChangedWithValue(TabMoveColor, "TabMoveColor");
            OnPropertyChangedWithValue(TabFactionColor, "TabFactionColor");
            OnPropertyChangedWithValue(TabLineGroup, "TabLineGroup");
            OnPropertyChangedWithValue(TabLineLaw, "TabLineLaw");
            OnPropertyChangedWithValue(TabLineMove, "TabLineMove");
            OnPropertyChangedWithValue(TabLineFaction, "TabLineFaction");
            OnPropertyChangedWithValue(MovementEmpty, "MovementEmpty");
            OnPropertyChangedWithValue(OppositionEmpty, "OppositionEmpty");
            OnPropertyChangedWithValue(LawEmpty, "LawEmpty");
        }

        // ---- 集团操作 ----
        internal void GroupAction(int g)
        {
            try
            {
                if (g == 0) { _status = "王室是元首集团, 不能出阁"; Refresh(); return; }
                _status = InterestGroups.ToggleCabinet(g);
                Refresh();
            }
            catch (Exception ex) { DLog.Force("组阁操作失败: " + ex.Message); }
        }

        // ---- 法律操作 ----
        internal void SelectLaw(int law)
        {
            try
            {
                _lawSelected = law;
                _lawViewTier = -1;
                Refresh();
            }
            catch { }
        }

        // 点档位条: 查看该档效果(再点一次回到当前档)
        internal void SelectTier(int t)
        {
            try
            {
                if (_lawSelected < 0) return;
                _lawViewTier = (_lawViewTier == t) ? -1 : t;
                Refresh();
            }
            catch { }
        }

        internal void LawAction(bool force)
        {
            try
            {
                if (_lawSelected < 0) return;
                int target = _lawViewTier >= 0 ? _lawViewTier : LawSystem.NextTier(_lawSelected);
                _status = LawSystem.StartBillTo(_lawSelected, target, force);   // v4.142: V3 形式(任意目标档, 含倒退)
                Refresh();
            }
            catch (Exception ex) { DLog.Force("立法操作失败: " + ex.Message); }
        }

        internal void CancelBill()
        {
            try { _status = LawSystem.CancelBill(); Refresh(); }
            catch { }
        }

        // ---- 运动操作 ----
        internal void MovementAction(int idx, bool suppress)
        {
            try
            {
                _status = suppress ? InterestGroups.SuppressMovement(idx) : InterestGroups.ConcedeMovement(idx);
                Refresh();
            }
            catch (Exception ex) { DLog.Force("运动操作失败: " + ex.Message); }
        }

        // ---- 派系操作(旧) ----
        internal void LordAction(int i, int kind)
        {
            try
            {
                var r = RowAt(i);
                if (r == null) return;
                switch (kind)
                {
                    case 0: _status = Politics.Grant(r.ClanId); break;
                    case 1: _status = Politics.Honor(r.ClanId); break;
                    case 2: _status = Politics.Appoint(r.ClanId); break;
                    case 3: _status = Politics.Dismiss(r.ClanId); break;
                    default: _status = Politics.Execute(r.ClanId); break;
                }
                Refresh();
            }
            catch (Exception ex) { DLog.Force("政治操作失败: " + ex.Message); }
        }

        internal void PetitionAction(int index, bool accept)
        {
            try { _status = Politics.Respond(index, accept, Today()); Refresh(); }
            catch (Exception ex) { DLog.Force("请愿处理失败: " + ex.Message); }
        }

        internal void Feast() { try { _status = Politics.Feast(); Refresh(); } catch { } }
        internal void Patrol() { try { _status = Politics.Patrol(); Refresh(); } catch { } }
        internal void Suppress() { try { _status = Politics.Suppress(); Refresh(); } catch { } }
        internal void Concede() { try { _status = Politics.Concede(); Refresh(); } catch { } }

        // v5.0-P25: 动员 / 遣散
        internal void Mobilize()
        {
            try
            {
                _status = WarMobilization.Active ? WarMobilization.Dismiss() : WarMobilization.Call();
                Refresh();
            }
            catch (Exception ex) { DLog.Force("动员操作失败: " + ex.Message); }
        }

        // v5.0-P23: 革命的强力镇压/妥协(V3 官方: 花权威镇压运动 / 让步)
        internal void RevSuppress()
        {
            try { _status = Revolution.Suppress(); Refresh(); }
            catch (Exception ex) { DLog.Force("镇压革命失败: " + ex.Message); }
        }

        internal void RevConcede()
        {
            try { _status = Revolution.Concede(); Refresh(); }
            catch (Exception ex) { DLog.Force("妥协革命失败: " + ex.Message); }
        }

        private static int Today()
        {
            try { return (int)TaleWorlds.CampaignSystem.CampaignTime.Now.ToDays; } catch { return 0; }
        }

        // ================= 刷新 =================
        internal void Refresh()
        {
            try
            {
                Politics.RefreshLords();
                Politics.Aggregate();
                InterestGroups.Recompute();

                RefreshOverview();
                RefreshGroups();
                RefreshLaw();
                RefreshMovements();
        RefreshParties();   // v4.195
                RefreshFaction();

                OnPropertyChangedWithValue(_status, "StatusText");
                NotifyTabs();
            }
            catch (Exception ex) { DLog.Force("政治页刷新失败: " + ex.Message); }
        }

        private void RefreshOverview()
        {
            _authText = ((int)Politics.Authority).ToString();
            _authLabel = "权威 / 1000 · 每月 +" + ((int)Politics.MonthRegen());
            float legit = Politics.Legitimacy;
            _legitText = ((int)legit) + "%";
            _legitBarW = (float)Math.Max(2.0, 340.0 * legit / 100.0);
            _legitBarColor = InterestGroups.LegitLevelColor();
            _legitLabel = "合法性(" + InterestGroups.LegitLevelName() + ") · 内阁 "
                + InterestGroups.CabinetCount() + "/" + InterestGroups.CabinetLimit() + " 位";
            float share = InterestGroups.CabinetShare();
            _cabinetText = (int)Math.Round(share * 100) + "%";
            _cabinetBarW = (float)Math.Max(2.0, 340.0 * share);
            _cabinetBarColor = share >= 0.6f ? "#7FBF6AFF" : (share >= 0.35f ? "#E8C33AFF" : "#D96A5AFF");

            if (Politics.CivilWar)
            {
                _situationText = Wrap("局势: 内战 ｜ 内阁: " + InterestGroups.CabinetText() + " ｜ 在野不满: " + InterestGroups.OppositionText(), 21);
                _situationColor = "#FF4B4BFF";
            }
            else if (InterestGroups.Movements.Count > 0)
            {
                _situationText = Wrap("局势: " + Politics.StageText() + " ｜ 内阁: " + InterestGroups.CabinetText()
                    + " ｜ 运动 " + InterestGroups.Movements.Count + " 起 ｜ 在野不满: " + InterestGroups.OppositionText(), 21);
                _situationColor = "#E8C33AFF";
            }
            else
            {
                _situationText = Wrap("局势: " + Politics.StageText() + " ｜ 内阁: " + InterestGroups.CabinetText()
                    + " ｜ 在野不满: " + InterestGroups.OppositionText(), 21);
                _situationColor = "#9A9078FF";
            }
            OnPropertyChangedWithValue(_authText, "AuthNumText");
            OnPropertyChangedWithValue(_authLabel, "AuthLabelText");
            OnPropertyChangedWithValue(_legitText, "LegitText");
            OnPropertyChangedWithValue(_legitLabel, "LegitLabelText");
            OnPropertyChangedWithValue(_legitBarW, "LegitBarW");
            OnPropertyChangedWithValue(_legitBarColor, "LegitBarColor");
            OnPropertyChangedWithValue(_cabinetText, "CabinetText");
            OnPropertyChangedWithValue(_cabinetBarW, "CabinetBarW");
            OnPropertyChangedWithValue(_cabinetBarColor, "CabinetBarColor");
            OnPropertyChangedWithValue(_situationText, "SituationText");
            OnPropertyChangedWithValue(_situationColor, "SituationColor");
        }

        private void RefreshGroups()
        {
            try
            {
                CabinetRows.Clear();
                OppositionRows.Clear();
                var list = new List<InterestGroup>();
                for (int i = 0; i < InterestGroups.GroupCount; i++)
                    if (InterestGroups.G[i] != null) list.Add(InterestGroups.G[i]);
                list.Sort(delegate (InterestGroup a, InterestGroup b) { return b.Share.CompareTo(a.Share); });
                for (int i = 0; i < list.Count; i++)
                {
                    var row = new GroupRowVM(list[i].Index);
                    if (list[i].InGov) CabinetRows.Add(row);
                    else OppositionRows.Add(row);
                }
                _legitBreak = InterestGroups.LegitBreakdownText();
                OnPropertyChangedWithValue(_legitBreak, "LegitBreakText");
            }
            catch { }
        }

        // 一键优化内阁(V3 quick reform)
        internal void OptimizeCabinet()
        {
            try { _status = InterestGroups.BestCabinet(); Refresh(); }
            catch (Exception ex) { DLog.Force("推荐内阁失败: " + ex.Message); }
        }

        // 内阁/在野列表里的操作(按行索引取集团)
        internal void CabinetAction(int i)
        {
            try
            {
                if (i < 0 || i >= CabinetRows.Count) return;
                GroupAction(CabinetRows[i].GroupIndex);
            }
            catch { }
        }

        internal void OppositionAction(int i)
        {
            try
            {
                if (i < 0 || i >= OppositionRows.Count) return;
                GroupAction(OppositionRows[i].GroupIndex);
            }
            catch { }
        }

        private void RefreshLaw()
        {
            try
            {
                // 左侧列表(分类标题 + 法律; 固定窗口滚动)
                _lawAll.Clear();
                string lastCat = null;
                for (int i = 0; i < LawSystem.LawCount; i++)
                {
                    if (!LawSystem.IsAvailable(i)) continue;   // v4.186: 特有法律只在对应国家显示
                    string cat = LawSystem.CatOf(i);
                    if (cat != lastCat)
                    {
                        _lawAll.Add(new LawItemVM(CatIndex(cat), true, false));
                        lastCat = cat;
                    }
                    _lawAll.Add(new LawItemVM(i, false, i == _lawSelected));
                }
                ApplyLawWindow();

                // 右侧详情
                _lawSelectedAny = _lawSelected >= 0 && _lawSelected < LawSystem.LawCount;
                if (!_lawSelectedAny)
                {
                    _lawName = "选择一部法律";
                    _lawCatNow = "左侧按类别列出 " + LawSystem.LawCount + " 部法律; 点击查看其档位与形势";
                    _lawEffect = "";
                    _lawSupportText = ""; _lawOpposeText = "";
                    _lawSupportW = 2f; _lawOpposeW = 2f;
                    _lawSuccessText = ""; _lawProgressText = ""; _lawProgressVisible = false;
                    _lawHasBill = false; _lawMaxed = false;
                    _lawBtnMain = "立案"; _lawBtnForce = "强推"; _lawBtnMainColor = "#39FF14FF";
                    _lawForceVisible = false;
                    _lawSupportVisible = false;
                    _lawMaxedVisible = false;
                    _lawMaxedHint = "";
                    LawTiers.Clear();
                    _lawHint = Wrap("左侧选组, 右侧点法律卡选择要更换成的法律 → [立案] → 每 14 天检查点判定(成功推进/稳步推进/僵局/辩论) → 进度满 100 更换法律; 支持率高于反对率成功率高, 反之僵局多", 40);
                }
                else
                {
                    int i = _lawSelected;
                    _lawName = LawSystem.NameOf(i);
                    int lv = LawSystem.Level(i);
                    // v4.142(V3 形式): 目标法律 = 选中的档位(未选时默认下一档); 支持任意档位(含倒退)
                    int target = _lawViewTier >= 0 ? _lawViewTier : LawSystem.NextTier(i);
                    _lawCatNow = LawSystem.CatOf(i) + " · 现行: " + LawSystem.TierName(i, lv)
                        + (target != lv ? "  →  目标: " + LawSystem.TierName(i, target) : "  (点下方法律卡选择要更换成的法律)");
                    _lawEffect = Wrap(LawSystem.EffectOfTier(i, target), 40);
                    int sup = (int)Math.Round(LawSystem.SupportPctFor(i, target) * 100);
                    int opp = (int)Math.Round(LawSystem.OpposePctFor(i, target) * 100);
                    _lawSupportText = "支持 " + sup + "%";
                    _lawOpposeText = "反对 " + opp + "%";
                    _lawSupportW = (float)Math.Max(2.0, 700.0 * sup / 100.0);
                    _lawOpposeW = (float)Math.Max(2.0, 700.0 * opp / 100.0);
                    _lawSuccessText = "检查点概率(每 14 天判定): 成功推进 " + LawSystem.SuccessPctFor(i, target)
                        + "% · 僵局 " + LawSystem.StallPctFor(i, target) + "%";
                    _lawMaxed = lv >= LawSystem.TierCount(i) - 1 && target == lv;
                    _lawForceVisible = target != lv;
                    _lawSupportVisible = target != lv;   // 目标=现行时不显示支持度
                    _lawMaxedVisible = _lawMaxed;
                    _lawMaxedHint = _lawMaxed ? ("已至最高档: " + LawSystem.TierName(i, lv) + " · 无需再议") : "";
                    LawTiers.Clear();
                    int tc = LawSystem.TierCount(i);
                    for (int t = 0; t < tc; t++) LawTiers.Add(new LawTierVM(i, t, target));

                    if (LawSystem.BillLaw == i)
                    {
                        _lawHasBill = true;
                        _lawForceVisible = false;
                        _lawSupportVisible = false;   // 审议中: 只显示立法进度, 不再显示支持度
                        _lawProgressVisible = true;
                        _lawProgressText = "审议中 · 阶段 " + LawSystem.PhaseText() + " " + (int)LawSystem.BillProgress + "/100"
                            + " · 目标 " + LawSystem.TierName(i, LawSystem.BillTarget < 0 ? lv : LawSystem.BillTarget)
                            + (LawSystem.BillForced ? " · 强推" : "")
                            + (string.IsNullOrEmpty(LawSystem.LastOutcome) ? "" : " · 上次检查点: " + LawSystem.LastOutcome)
                            + " · 下次判定 " + Math.Max(0, LawSystem.CheckpointDays - (Politics.Today() - LawSystem.LastCheckDay)) + " 天后";
                        _lawProgressW = (float)Math.Max(2.0, 700.0 * LawSystem.BillProgress / 100.0);
                        _lawProgressColor = "#E8C33AFF";
                        _lawBtnMain = "搁置法案"; _lawBtnMainColor = "#D96A5AFF";
                        _lawBtnForce = "";
                    }
                    else if (LawSystem.InBill)
                    {
                        _lawHasBill = false;
                        _lawProgressVisible = false;
                        _lawForceVisible = false;
                        _lawProgressText = "另有法案在审议: 《" + LawSystem.NameOf(LawSystem.BillLaw) + "》";
                        _lawProgressW = 0f;
                        _lawProgressColor = "#8A8070FF";
                        _lawBtnMain = (target != lv ? ("立案 → " + LawSystem.TierName(i, target)) : "立案"); _lawBtnMainColor = "#39FF14FF";
                        _lawBtnForce = "强推";
                    }
                    else
                    {
                        _lawHasBill = false;
                        _lawProgressVisible = false;
                        _lawProgressText = "未在审议";
                        _lawProgressW = 0f;
                        _lawProgressColor = "#8A8070FF";
                        _lawBtnMain = (target != lv ? ("立案 → " + LawSystem.TierName(i, target)) : "立案"); _lawBtnMainColor = "#39FF14FF";
                        _lawBtnForce = "强推";
                    }
                    _lawHint = Wrap("点下方法律卡选择要更换成的法律(可任意档, 含倒退), [立案]后每 14 天一个检查点(成功推进/稳步推进/僵局/辩论) · 费用 = 40 + 距离×30 权威(强推 ×1.5, 进度 +25, 合法性 -3) · 合法性 <25 时需集团运动推动", 40);
                }
                OnPropertyChangedWithValue(_lawName, "LawName");
                OnPropertyChangedWithValue(_lawCatNow, "LawCatNow");
                OnPropertyChangedWithValue(_lawEffect, "LawEffect");
                OnPropertyChangedWithValue(_lawSupportText, "LawSupportText");
                OnPropertyChangedWithValue(_lawOpposeText, "LawOpposeText");
                OnPropertyChangedWithValue(_lawSupportW, "LawSupportW");
                OnPropertyChangedWithValue(_lawOpposeW, "LawOpposeW");
                OnPropertyChangedWithValue(_lawSuccessText, "LawSuccessText");
                OnPropertyChangedWithValue(_lawProgressText, "LawProgressText");
                OnPropertyChangedWithValue(_lawProgressColor, "LawProgressColor");
                OnPropertyChangedWithValue(_lawProgressW, "LawProgressW");
                OnPropertyChangedWithValue(_lawProgressVisible, "LawProgressVisible");
                OnPropertyChangedWithValue(_lawHasBill, "LawHasBill");
                OnPropertyChangedWithValue(_lawBtnMain, "LawBtnMain");
                OnPropertyChangedWithValue(_lawBtnForce, "LawBtnForce");
                OnPropertyChangedWithValue(_lawBtnMainColor, "LawBtnMainColor");
                OnPropertyChangedWithValue(_lawForceVisible, "LawForceVisible");
                OnPropertyChangedWithValue(_lawSupportVisible, "LawSupportVisible");
                OnPropertyChangedWithValue(_lawMaxedVisible, "LawMaxedVisible");
                OnPropertyChangedWithValue(_lawMaxedHint, "LawMaxedHint");
                OnPropertyChangedWithValue(_lawHint, "LawHint");
                OnPropertyChangedWithValue(_lawSelectedAny, "LawSelectedAny");
            }
            catch (Exception ex) { DLog.Force("法律页刷新失败: " + ex.Message); }
        }

        private void ApplyLawWindow()
        {
            try
            {
                int max = Math.Max(0, _lawAll.Count - LawRowsWindow);
                if (_lawScroll < 0) _lawScroll = 0;
                if (_lawScroll > max) _lawScroll = max;
                LawItems.Clear();
                for (int i = 0; i < LawRowsWindow && _lawScroll + i < _lawAll.Count; i++)
                    LawItems.Add(_lawAll[_lawScroll + i]);
            }
            catch { }
        }

        private static int CatIndex(string cat)
        {
            for (int i = 0; i < LawSystem.CatOrder.Length; i++) if (LawSystem.CatOrder[i] == cat) return i;
            return 0;
        }

        // 简易折行(TextWidget 单行裁剪规避)
        private static string Wrap(string s, int n)
        {
            if (string.IsNullOrEmpty(s) || n <= 0) return s;
            var sb = new System.Text.StringBuilder();
            int line = 0;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '\n') { sb.Append(c); line = 0; continue; }
                sb.Append(c); line++;
                if (line >= n) { sb.Append('\n'); line = 0; }
            }
            return sb.ToString();
        }

        // v4.195: 政党席位(按支持度排序取前 8)
        private void RefreshParties()
        {
            try
            {
                PartyRows.Clear();
                var list = new List<PartyDef>(Parties.All);
                list.Sort(delegate (PartyDef a, PartyDef b) { return Parties.SupportOf(b.Id).CompareTo(Parties.SupportOf(a.Id)); });
                int n = 0;
                for (int i = 0; i < list.Count && n < 8; i++)
                {
                    if (Parties.SupportOf(list[i].Id) <= 0.001f) continue;
                    PartyRows.Add(new PartyRowVM(list[i], list[i].Id == Parties.RulingId));
                    n++;
                }
                _partyTitle = "政党席位 · 执政: " + Parties.RulingName;
                OnPropertyChangedWithValue(_partyTitle, "PartyTitle");
            }
            catch (Exception ex) { DLog.Force("政党列表刷新失败: " + ex.Message); }
        }

        internal void PartyClick(int idx)
        {
            try
            {
                if (idx < 0 || idx >= PartyRows.Count) return;
                string msg = Parties.SetRuling(PartyRows[idx].PartyId);
                try { MapSelection.Message(msg); } catch { }
                DLog.Force("政党: " + msg);
                Refresh();
            }
            catch (Exception ex) { DLog.Force("组阁失败: " + ex.Message); }
        }

        private void RefreshMovements()
        {
            try
            {
                MovementRows.Clear();
                for (int i = 0; i < InterestGroups.Movements.Count && i < 3; i++)
                    MovementRows.Add(new MovementVM(i, InterestGroups.Movements[i]));
                _revText = Revolution.StatusText();
                if (string.IsNullOrEmpty(_revText) && Revolution.Won)
                    _revText = "上次革命以胜利告终: 政府已改组";
                OnPropertyChangedWithValue(_revText, "RevText");
                OnPropertyChangedWithValue(RevVisible, "RevVisible");
            }
            catch { }
        }

        private void RefreshFaction()
        {
            try
            {
                _councilText = "御前会议: ";
                for (int i = 0; i < 5; i++)
                {
                    if (i > 0) _councilText += " · ";
                    string id = Politics.Council[i];
                    string who = "—";
                    LordProfile lp;
                    if (!string.IsNullOrEmpty(id) && Politics.Lords.TryGetValue(id, out lp)) who = lp.Name;
                    _councilText += Politics.SeatNames[i] + " " + who;
                }
                if (Politics.CivilWar)
                {
                    _warText = "【内战】税收 -40% · 全境产出 -15% ｜ 阶段: " + Politics.StageText();
                    _warColor = "#FF4B4BFF";
                }
                else if (Politics.NobleAnger >= 60)
                {
                    _warText = "局势: " + Politics.StageText() + " ｜ 领主愤怒 " + Politics.NobleAnger
                        + " ｜ ⚠ 愤怒≥90 且合法性<25 将爆发内战";
                    _warColor = Politics.NobleAnger >= 75 ? "#FF4B4BFF" : "#E8C33AFF";
                }
                else
                {
                    _warText = "局势: " + Politics.StageText() + " ｜ 领主愤怒 " + Politics.NobleAnger + " ｜ 政局尚稳";
                    _warColor = "#9A9078FF";
                }
                _cloutText = "大领主势力 " + Politics.NobleClout + " vs 王室 " + ((int)Politics.CrownClout())
                    + " ｜ 领主愤怒 " + Politics.NobleAnger + " ｜ 合法性 " + ((int)Politics.Legitimacy) + "%";

                PetitionRows.Clear();
                for (int i = 0; i < Politics.Petitions.Count && i < 1; i++)
                    PetitionRows.Add(new PetitionRowVM(i, Politics.Petitions[i].Text));
                _petitionHint = Politics.Petitions.Count == 0
                    ? "暂无请愿(月结时视民情生成)"
                    : (Politics.Petitions.Count > 1 ? "另有 " + (Politics.Petitions.Count - 1) + " 条请愿待处理" : "");
                _factionNoPetition = Politics.Petitions.Count == 0;

                // v5.0-P25: 动员 + 选举状态; v4.137: 竞选动量
                string mom = Elections.MomentumText();
                _mobText = WarMobilization.StatusText() + " ｜ " + Elections.StatusText()
                    + (string.IsNullOrEmpty(mom) ? "" : " ｜ " + mom);
                _mobBtnText = WarMobilization.Active ? "遣散动员" : "动员令";
                _mobBtnColor = WarMobilization.Active ? "#D96A5AFF" : "#6AB0FFFF";
                OnPropertyChangedWithValue(_mobText, "MobText");
                OnPropertyChangedWithValue(_mobBtnText, "MobBtnText");
                OnPropertyChangedWithValue(_mobBtnColor, "MobBtnColor");

                // 领主名册
                _allRows.Clear();
                var views = new List<LordProfile>(Politics.Lords.Values);
                views.Sort(delegate (LordProfile a, LordProfile b) { return b.Clout.CompareTo(a.Clout); });
                for (int i = 0; i < views.Count; i++) _allRows.Add(new LordRowVM(views[i]));
                int max = Math.Max(0, _allRows.Count - MaxRows);
                if (_scroll > max) _scroll = max;
                if (_scroll < 0) _scroll = 0;
                RebuildRows();

                OnPropertyChangedWithValue(_councilText, "CouncilText");
                OnPropertyChangedWithValue(_warText, "WarText");
                OnPropertyChangedWithValue(_warColor, "WarColor");
                OnPropertyChangedWithValue(_cloutText, "CloutText");
                OnPropertyChangedWithValue(_petitionHint, "PetitionHint");
                OnPropertyChangedWithValue(_factionNoPetition, "FactionNoPetition");
            }
            catch (Exception ex) { DLog.Force("派系页刷新失败: " + ex.Message); }
        }

        private void RebuildRows()
        {
            try
            {
                Rows.Clear();
                for (int i = 0; i < MaxRows && _scroll + i < _allRows.Count; i++) Rows.Add(_allRows[_scroll + i]);
            }
            catch { }
        }
    }
}
