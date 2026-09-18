using System;
using System.Collections.Generic;
using System.Text;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;

namespace FeudalInternalAffairs
{
    // 诉求一行
    public class GoalRowVM : ViewModel
    {
        private string _text, _color;

        internal GoalRowVM(WarGoal g)
        {
            this.Init(g, true);
        }

        internal GoalRowVM(WarGoal g, bool byInitiator)
        {
            this.Init(g, byInitiator);
        }

        private void Init(WarGoal g, bool byInitiator)
        {
            _text = DiploPlays.DemandText(g);
            if (g.Enforced) _color = "#7DC97DFF";
            else if (g.Secondary) _color = "#8A8272FF";
            else _color = byInitiator ? "#A8D8A8FF" : "#D8A8A8FF";   // 发起方诉求偏绿, 目标诉求偏红
        }

        [DataSourceProperty] public string Text { get { return _text; } }
        [DataSourceProperty] public string Color { get { return _color; } }
    }

    // 参加国一行
    public class MemberRowVM : ViewModel
    {
        internal readonly string KingdomId;
        private readonly string _name, _status, _pref, _prefColor, _stats;
        private readonly bool _canSway;

        internal MemberRowVM(DiploPlay p, PlayMember m, Kingdom me, bool canSway)
        {
            KingdomId = m.KingdomId;
            _name = DiploPlays.NameOf(m.KingdomId);
            var rowK = DiploPlays.K(m.KingdomId);
            // 显示"该国对我方阵营的倾向"(此前误显示玩家自身倾向, 恒为 0)
            int ourSide = 0;
            try
            {
                if (me != null && p != null)
                    ourSide = (p.InitiatorId == me.StringId) ? 0 : ((p.TargetId == me.StringId) ? 1 : 0);
            }
            catch { }
            int pref = (rowK != null) ? DiploPlays.Preference(p, rowK, ourSide) : 0;
            bool warUs = me != null && DiploPlays.AtWarWithOurSide(me, rowK);
            _status = warUs ? "与我国交战" : DiploPlays.MemberStatus(m);
            _pref = pref.ToString();
            _prefColor = warUs ? "#D96A5AFF" : (pref >= 40 ? "#39FF14FF" : (pref <= -40 ? "#D96A5AFF" : "#C8B98FFF"));
            float str = 0f;
            try { if (rowK != null) str = rowK.CurrentTotalStrength; } catch { }
            _stats = "军力 " + (int)str;
            _canSway = canSway && !warUs && rowK != null && !Diplomacy.IsAlly(me, rowK)
                && (me == null || (rowK.StringId != p.InitiatorId && rowK.StringId != p.TargetId))
                && !(m.Stance == 3 || m.Stance == 4 || m.Stance == 5);
        }

        [DataSourceProperty] public string Name { get { return _name; } }
        [DataSourceProperty] public string Status { get { return _status; } }
        [DataSourceProperty] public string Pref { get { return _pref; } }
        [DataSourceProperty] public string PrefColor { get { return _prefColor; } }
        [DataSourceProperty] public string Stats { get { return _stats; } }
        [DataSourceProperty] public string DescLine { get { return _stats + " · " + _status; } }
        [DataSourceProperty] public bool CanSway { get { return _canSway; } }
    }

    // 拉拢条件一行(参照 V3 拉拢界面)
    public class OfferRowVM : ViewModel
    {
        internal readonly int OfferIndex;
        private readonly string _name, _cost, _value, _desc, _color;

        internal OfferRowVM(int idx, SwayKind kind, bool valid, string why, string acceptHint, bool willAccept)
        {
            OfferIndex = idx;
            _name = DiploPlays.SwayOfferName(kind);
            _cost = "机动 20";
            _value = "+" + DiploPlays.SwayOfferValue(kind);
            _desc = (valid ? DiploPlays.SwayOfferDesc(kind) : why) + (valid && acceptHint.Length > 0 ? " ｜ " + acceptHint : "");
            _color = valid ? (willAccept ? "#39FF14FF" : "#F0E4C8FF") : "#7A7060FF";
        }

        [DataSourceProperty] public string Name { get { return _name; } }
        [DataSourceProperty] public string Cost { get { return _cost; } }
        [DataSourceProperty] public string Value { get { return _value; } }
        [DataSourceProperty] public string Desc { get { return _desc; } }
        [DataSourceProperty] public string Color { get { return _color; } }
    }

    // 割让候选领地一行
    public class CandidateRowVM : ViewModel
    {
        internal readonly string Sid;
        private readonly string _name, _dist;

        internal CandidateRowVM(Settlement s, Kingdom by)
        {
            Sid = s.StringId;
            _name = s.Name != null ? s.Name.ToString() : s.StringId;
            float d = float.MaxValue;
            try
            {
                if (by != null && by.Settlements != null)
                    for (int i = 0; i < by.Settlements.Count; i++)
                    {
                        var a = by.Settlements[i];
                        if (a == null) continue;
                        float dd = a.Position.Distance(s.Position);
                        if (dd < d) d = dd;
                    }
            }
            catch { }
            _dist = d < float.MaxValue ? ("距离 " + (int)d) : "";
        }

        [DataSourceProperty] public string Name { get { return _name; } }
        [DataSourceProperty] public string Dist { get { return _dist; } }
    }

    // 世界博弈一行
    public class PlayRowVM : ViewModel
    {
        internal readonly int Id;
        private readonly string _title, _info, _color;

        internal PlayRowVM(DiploPlay p, bool selected)
        {
            Id = p.Id;
            _title = DiploPlays.NameOf(p.InitiatorId) + " ⚔ " + DiploPlays.NameOf(p.TargetId);
            _info = DiploPlays.PhaseText(p) + " · 升级 " + ((int)p.Escalation) + "/100";
            _color = selected ? "#39FF14FF" : "#C8B98FFF";
        }

        [DataSourceProperty] public string Title { get { return _title; } }
        [DataSourceProperty] public string Info { get { return _info; } }
        [DataSourceProperty] public string Color { get { return _color; } }
    }

    // 激化度十段进度
    public class EscPipVM : ViewModel
    {
        private string _color = "#3A342ACC";
        [DataSourceProperty] public string Color { get { return _color; } }
        internal void Set(string c) { if (_color != c) { _color = c; OnPropertyChangedWithValue(c, "Color"); } }
    }

    // 外交博弈页 VM(文档 22 章; 版式参照维多利亚 3)
    public class DiploPlayVM : PanelVMBase
    {
        private readonly Action _onClose;
        private string _playTitle = "外交博弈", _maneuverLine = "", _escText = "", _phaseHint = "", _roleText = "", _roleColor = "", _status = "", _others = "", _hint = "";
        private string _initName = "", _initStats = "", _targetName = "", _targetStats = "", _supportA = "", _supportB = "", _leanHint = "";
        private string _initHeader = "", _targetHeader = "";
        private string _goalHint = "", _tabOverviewColor = "", _tabInvolvedColor = "";
        private bool _isInitiator, _isTarget, _isThird, _isPrimary, _isPicking, _tabOverview = true, _isSwaying, _isAmountPicking, _isSwayPicking;
        private string _swayTitle = "", _swayTargetId = "", _amountTitle = "", _amountText = "";
        private GoalKind _amountKind = GoalKind.Tribute;
        private int _amountValue = 3000;
        private List<Settlement> _candData = new List<Settlement>();
        private int _viewPlayId = -1;

        public DiploPlayVM(Action onClose)
        {
            _onClose = onClose;
            DemandsA = new MBBindingList<GoalRowVM>();
            DemandsB = new MBBindingList<GoalRowVM>();
            Members = new MBBindingList<MemberRowVM>();
            Leaners = new MBBindingList<MemberRowVM>();
            LeanersA = new MBBindingList<MemberRowVM>();
            LeanersB = new MBBindingList<MemberRowVM>();
            SwayList = new MBBindingList<OfferRowVM>();
            SwayCands = new MBBindingList<MemberRowVM>();
            Candidates = new MBBindingList<CandidateRowVM>();
            Plays = new MBBindingList<PlayRowVM>();
            EscPips = new MBBindingList<EscPipVM>();
            Refresh();
        }

        public MBBindingList<GoalRowVM> DemandsA { get; private set; }
        public MBBindingList<GoalRowVM> DemandsB { get; private set; }
        public MBBindingList<MemberRowVM> Members { get; private set; }
        public MBBindingList<MemberRowVM> Leaners { get; private set; }
        public MBBindingList<MemberRowVM> LeanersA { get; private set; }
        public MBBindingList<MemberRowVM> LeanersB { get; private set; }
        public MBBindingList<OfferRowVM> SwayList { get; private set; }
        public MBBindingList<MemberRowVM> SwayCands { get; private set; }
        public MBBindingList<CandidateRowVM> Candidates { get; private set; }
        public MBBindingList<PlayRowVM> Plays { get; private set; }
        public MBBindingList<EscPipVM> EscPips { get; private set; }

        [DataSourceProperty] public string PlayTitle { get { return _playTitle; } }
        [DataSourceProperty] public string ManeuverLine { get { return _maneuverLine; } }
        [DataSourceProperty] public string EscText { get { return _escText; } }
        [DataSourceProperty] public string PhaseHint { get { return _phaseHint; } }
        [DataSourceProperty] public string RoleText { get { return _roleText; } }
        [DataSourceProperty] public string RoleColor { get { return _roleColor; } }
        [DataSourceProperty] public string StatusText { get { return _status; } }
        [DataSourceProperty] public string OthersText { get { return _others; } }
        [DataSourceProperty] public string HintText { get { return _hint; } }
        [DataSourceProperty] public string InitName { get { return _initName; } }
        [DataSourceProperty] public string InitStats { get { return _initStats; } }
        [DataSourceProperty] public string TargetName { get { return _targetName; } }
        [DataSourceProperty] public string TargetStats { get { return _targetStats; } }
        [DataSourceProperty] public string SupportA { get { return _supportA; } }
        [DataSourceProperty] public string SupportB { get { return _supportB; } }
        [DataSourceProperty] public string LeanHint { get { return _leanHint; } }
        [DataSourceProperty] public string GoalHint { get { return _goalHint; } }
        [DataSourceProperty] public string TabOverviewColor { get { return _tabOverviewColor; } }
        [DataSourceProperty] public string TabInvolvedColor { get { return _tabInvolvedColor; } }
        [DataSourceProperty] public bool IsInitiator { get { return _isInitiator; } }
        [DataSourceProperty] public bool IsTarget { get { return _isTarget; } }
        [DataSourceProperty] public bool IsThird { get { return _isThird; } }
        [DataSourceProperty] public bool IsPrimary { get { return _isPrimary; } }
        [DataSourceProperty] public bool IsPicking { get { return _isPicking; } }
        [DataSourceProperty] public bool ShowActions { get { return _isPrimary && !Busy; } }
        [DataSourceProperty] public bool ShowBackDown { get { return _isInitiator && !Busy; } }
        [DataSourceProperty] public bool ShowGiveIn { get { return _isTarget && !Busy; } }
        [DataSourceProperty] public bool ShowOverviewActions { get { return _isPrimary && !Busy && _tabOverview; } }
        [DataSourceProperty] public bool ShowInvolvedActions { get { return _isPrimary && !Busy && !_tabOverview; } }
        [DataSourceProperty] public bool ShowOverviewBackDown { get { return _isInitiator && !Busy && _tabOverview; } }
        [DataSourceProperty] public bool ShowOverviewGiveIn { get { return _isTarget && !Busy && _tabOverview; } }
        [DataSourceProperty] public bool ShowWorld { get { return !Busy && !_tabOverview; } }
        [DataSourceProperty] public bool ShowOverview { get { return !Busy && _tabOverview; } }
        [DataSourceProperty] public bool ShowInvolved { get { return !Busy && !_tabOverview; } }
        private bool Busy { get { return _isPicking || _isSwaying || _isAmountPicking || _isSwayPicking; } }
        [DataSourceProperty] public bool IsSwaying { get { return _isSwaying; } }
        [DataSourceProperty] public bool IsSwayPicking { get { return _isSwayPicking; } }
        [DataSourceProperty] public string SwayTitle { get { return _swayTitle; } }
        [DataSourceProperty] public bool IsAmountPicking { get { return _isAmountPicking; } }
        [DataSourceProperty] public string AmountTitle { get { return _amountTitle; } }
        [DataSourceProperty] public string AmountText { get { return _amountText; } }
        [DataSourceProperty] public string InitHeader { get { return _initHeader; } }
        [DataSourceProperty] public string TargetHeader { get { return _targetHeader; } }

        public void ExecuteClose() { if (_onClose != null) _onClose(); }

        internal int MemberCount { get { return Members.Count; } }
        internal int LeanerCount { get { return Leaners.Count; } }
        internal int SwayCount { get { return SwayList.Count; } }
        internal int SwayCandCount { get { return SwayCands.Count; } }
        internal bool RowCanSwaySide(int side, int idx)
        {
            try
            {
                var l = side == 0 ? LeanersA : LeanersB;
                return idx >= 0 && idx < l.Count && l[idx].CanSway;
            }
            catch { return false; }
        }
        internal bool RowCanSwayLeaner(int idx)
        {
            try { return idx >= 0 && idx < Leaners.Count && Leaners[idx].CanSway; } catch { return false; }
        }
        internal int CandidateCount { get { return Candidates.Count; } }
        internal int PlayCount { get { return Plays.Count; } }

        // ================= 操作 =================
        internal void TabOverview() { CancelSubPages(); _tabOverview = true; NotifyRole(); Refresh(); DiploPlayPanel.RefreshSpotsNow(); }
        internal void TabInvolved() { CancelSubPages(); _tabOverview = false; NotifyRole(); Refresh(); DiploPlayPanel.RefreshSpotsNow(); }

        // 切页签时把未完成的子页收掉(避免"旧 UI 没卸掉"的观感)
        private void CancelSubPages()
        {
            try
            {
                _isPicking = false;
                _isSwaying = false;
                _isSwayPicking = false;
                _isAmountPicking = false;
            }
            catch { }
        }

        internal void SelectPlay(int idx)
        {
            try
            {
                if (idx < 0 || idx >= Plays.Count) return;
                _viewPlayId = Plays[idx].Id;
                DiploPlays.UiSelectedPlayId = _viewPlayId;
                _status = "正在查看: " + Plays[idx].Title;
                Refresh();
            }
            catch { }
        }

        internal void AddGoal(int kind)
        {
            try
            {
                var gk = (GoalKind)kind;
                if (gk == GoalKind.Conquer) { BeginPickConquer(); return; }
                if (gk == GoalKind.Tribute || gk == GoalKind.Vassalize) { BeginAmountPick(gk); return; }
                _status = DiploPlays.PlayerAddGoal(gk);
                Refresh();
            }
            catch (Exception ex) { DLog.Force("博弈加诉求失败: " + ex.Message); }
        }

        // 自定义金额(赔款/纳贡)子页
        internal void BeginAmountPick(GoalKind kind)
        {
            try
            {
                _amountKind = kind;
                _isAmountPicking = true;
                _amountValue = DiploPlays.DefaultAmount(kind);
                _amountTitle = kind == GoalKind.Tribute ? "索要赔款 — 设定金额" : "要求纳贡 — 设定月贡";
                _amountText = _amountValue.ToString("N0") + " 第纳尔";
                NotifyRole();
                OnPropertyChangedWithValue(_amountTitle, "AmountTitle");
                OnPropertyChangedWithValue(_amountText, "AmountText");
                Refresh();
            }
            catch { }
        }

        internal void AmountDelta(int delta)
        {
            try
            {
                _amountValue += delta;
                if (_amountValue < 500) _amountValue = 500;
                if (_amountValue > 30000) _amountValue = 30000;
                _amountText = _amountValue.ToString("N0") + " 第纳尔";
                OnPropertyChangedWithValue(_amountText, "AmountText");
            }
            catch { }
        }

        internal void ConfirmAmount()
        {
            try
            {
                _status = DiploPlays.PlayerAddGoalAmount(_amountKind, _amountValue);
                _isAmountPicking = false;
                NotifyRole();
                Refresh();
            }
            catch { }
        }

        internal void CancelAmount()
        {
            try
            {
                _isAmountPicking = false;
                _status = "已取消";
                NotifyRole();
                OnPropertyChangedWithValue(_status, "StatusText");
                Refresh();
            }
            catch { }
        }

        internal void BeginPickConquer()
        {
            try
            {
                _isPicking = true;
                _status = "请选择要对方割让的领地:";
                BuildCandidates();
                NotifyRole();
                OnPropertyChangedWithValue(_status, "StatusText");
                Refresh();
            }
            catch { }
        }

        internal void CancelPick()
        {
            try
            {
                _isPicking = false;
                _status = "已取消选择";
                NotifyRole();
                OnPropertyChangedWithValue(_status, "StatusText");
                Refresh();
            }
            catch { }
        }

        internal void PickCandidate(int idx)
        {
            try
            {
                if (idx < 0 || idx >= _candData.Count) return;
                var s = _candData[idx];
                _status = DiploPlays.PlayerAddGoalAt(GoalKind.Conquer, s.StringId);
                _isPicking = false;
                NotifyRole();
                Refresh();
            }
            catch { }
        }

        private void BuildCandidates()
        {
            try
            {
                Candidates.Clear();
                _candData = new List<Settlement>();
                var pk = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                var p = DiploPlays.PlayerPlay();
                if (pk == null || p == null) return;
                Kingdom other = null;
                if (p.InitiatorId == pk.StringId) other = DiploPlays.K(p.TargetId);
                else if (p.TargetId == pk.StringId) other = DiploPlays.K(p.InitiatorId);
                if (other == null) return;
                var list = DiploPlays.ConquerCandidates(pk, other, 5);
                for (int i = 0; i < list.Count; i++)
                {
                    _candData.Add(list[i]);
                    Candidates.Add(new CandidateRowVM(list[i], pk));
                }
                if (list.Count == 0) _status = "对方没有接壤的领地可索取(隔着太远)";
            }
            catch { }
        }

        internal void SwayLeaner(int idx)
        {
            try
            {
                if (idx < 0 || idx >= Leaners.Count) return;
                BeginSway(Leaners[idx].KingdomId);
            }
            catch (Exception ex) { DLog.Force("博弈拉拢失败: " + ex.Message); }
        }

        // 卷入的国家(两栏)里的拉拢
        internal void SwaySide(int side, int idx)
        {
            try
            {
                var list = side == 0 ? LeanersA : LeanersB;
                if (idx < 0 || idx >= list.Count) return;
                BeginSway(list[idx].KingdomId);
            }
            catch { }
        }

        // 拉拢国家选择页(总览页的醒目按钮)
        internal void BeginSwayPick()
        {
            try
            {
                _isSwayPicking = true;
                _status = "请选择要拉拢的国家(盟友会自动参战, 无需拉拢):";
                NotifyRole();
                OnPropertyChangedWithValue(_status, "StatusText");
                Refresh();
            }
            catch { }
        }

        internal void PickSwayTarget(int idx)
        {
            try
            {
                if (idx < 0 || idx >= SwayCands.Count) return;
                var id = SwayCands[idx].KingdomId;
                _isSwayPicking = false;
                BeginSway(id);
            }
            catch { }
        }

        internal void CancelSwayPick()
        {
            try
            {
                _isSwayPicking = false;
                _status = "已取消";
                NotifyRole();
                Refresh();
            }
            catch { }
        }

        // 打开"拉拢"条件子页(参照 V3: 提供义务/赔款分成/开放商路/割让征服地/扶植附庸/佣金)
        internal void BeginSway(string kingdomId)
        {
            try
            {
                _isSwaying = true;
                _swayTargetId = kingdomId;
                _swayTitle = "拉拢 " + DiploPlays.NameOf(kingdomId);
                FillSwayList();
                NotifyRole();
                Refresh();
            }
            catch { }
        }

        internal void OfferSwayIdx(int idx)
        {
            try
            {
                if (idx < 0 || idx >= SwayList.Count) return;
                var kind = (SwayKind)SwayList[idx].OfferIndex;
                _status = DiploPlays.PlayerOfferSway(_swayTargetId, kind);
                _isSwaying = false;
                NotifyRole();
                Refresh();
            }
            catch { }
        }

        internal void CancelSway()
        {
            try
            {
                _isSwaying = false;
                _status = "已取消拉拢";
                NotifyRole();
                OnPropertyChangedWithValue(_status, "StatusText");
                DiploPlayPanel.RefreshSpotsNow();
            }
            catch { }
        }

        private void FillSwayList()
        {
            try
            {
                SwayList.Clear();
                var pk = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                var p = DiploPlays.PlayerPlay();
                var who = DiploPlays.K(_swayTargetId);
                int pref = 0;
                if (p != null && pk != null && who != null)
                {
                    bool isInit = p.InitiatorId == pk.StringId;
                    pref = DiploPlays.Preference(p, who, isInit ? 0 : 1);
                }
                for (int i = 0; i < 6; i++)
                {
                    var k = (SwayKind)i;
                    string why = "";
                    bool valid = p != null && pk != null && DiploPlays.SwayOfferValid(p, pk, k, out why);
                    int acc = pref + DiploPlays.SwayOfferValue(k);
                    bool willAccept = acc >= 30;
                    string hint = valid
                        ? ("当前好感 " + pref + " → " + (willAccept ? "对方会接受" : "对方会拒绝(还差 " + (30 - acc) + ")"))
                        : "";
                    SwayList.Add(new OfferRowVM(i, k, valid, why, hint, willAccept));
                }
                OnPropertyChangedWithValue(_swayTitle, "SwayTitle");
            }
            catch { }
        }

        internal void BackDown()
        {
            try { _status = DiploPlays.PlayerBackDown(); _viewPlayId = -1; DiploPlays.UiSelectedPlayId = -1; Refresh(); }
            catch { }
        }

        internal void GiveIn()
        {
            try { _status = DiploPlays.PlayerGiveIn(); _viewPlayId = -1; DiploPlays.UiSelectedPlayId = -1; Refresh(); }
            catch { }
        }

        internal void Join(int side)
        {
            try { _status = DiploPlays.PlayerJoin(side); Refresh(); }
            catch { }
        }

        internal void Decline()
        {
            try { _status = DiploPlays.PlayerDecline(); _viewPlayId = -1; DiploPlays.UiSelectedPlayId = -1; Refresh(); }
            catch { }
        }

        private void NotifyRole()
        {
            try
            {
                OnPropertyChangedWithValue(_isInitiator, "IsInitiator");
                OnPropertyChangedWithValue(_isTarget, "IsTarget");
                OnPropertyChangedWithValue(_isThird, "IsThird");
                OnPropertyChangedWithValue(_isPrimary, "IsPrimary");
                OnPropertyChangedWithValue(_isPicking, "IsPicking");
                bool show = !_isPicking && !_isSwaying && !_isAmountPicking && !_isSwayPicking;
                OnPropertyChangedWithValue(_isPrimary && show, "ShowActions");
                OnPropertyChangedWithValue(_isInitiator && show, "ShowBackDown");
                OnPropertyChangedWithValue(_isTarget && show, "ShowGiveIn");
                OnPropertyChangedWithValue(_isPrimary && show && _tabOverview, "ShowOverviewActions");
                OnPropertyChangedWithValue(_isPrimary && show && !_tabOverview, "ShowInvolvedActions");
                OnPropertyChangedWithValue(_isInitiator && show && _tabOverview, "ShowOverviewBackDown");
                OnPropertyChangedWithValue(_isTarget && show && _tabOverview, "ShowOverviewGiveIn");
                OnPropertyChangedWithValue(show && !_tabOverview, "ShowWorld");
                OnPropertyChangedWithValue(show && _tabOverview, "ShowOverview");
                OnPropertyChangedWithValue(show && !_tabOverview, "ShowInvolved");
                OnPropertyChangedWithValue(_isSwaying, "IsSwaying");
                OnPropertyChangedWithValue(_isSwayPicking, "IsSwayPicking");
                OnPropertyChangedWithValue(_swayTitle, "SwayTitle");
                OnPropertyChangedWithValue(_isAmountPicking, "IsAmountPicking");
                OnPropertyChangedWithValue(_amountTitle, "AmountTitle");
                OnPropertyChangedWithValue(_amountText, "AmountText");
            }
            catch { }
        }

        private void FillEscPips(float esc)
        {
            try
            {
                while (EscPips.Count < 10) EscPips.Add(new EscPipVM());
                int filled = (int)Math.Round(esc / 10f);
                for (int i = 0; i < 10; i++)
                    EscPips[i].Set(i < filled ? (filled >= 8 ? "#FF4B4BFF" : "#E8C33AFF") : "#3A342ACC");
            }
            catch { }
        }

        // ================= 刷新 =================
        internal void Refresh()
        {
            try
            {
                if (_viewPlayId >= 0 && DiploPlays.FindPlay(_viewPlayId) == null) _viewPlayId = -1;
                DiploPlays.UiSelectedPlayId = _viewPlayId;

                var pk = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                var p = DiploPlays.PlayerPlay();

                _isInitiator = p != null && pk != null && p.InitiatorId == pk.StringId;
                _isTarget = p != null && pk != null && p.TargetId == pk.StringId;
                _isThird = p != null && !_isInitiator && !_isTarget;
                _isPrimary = _isInitiator || _isTarget;

                _tabOverviewColor = _tabOverview ? "#39FF14FF" : "#7A7060FF";
                _tabInvolvedColor = _tabOverview ? "#7A7060FF" : "#39FF14FF";

                DemandsA.Clear();
                DemandsB.Clear();
                Members.Clear();
                Leaners.Clear();
                LeanersA.Clear();
                LeanersB.Clear();
                if (!_isPicking) Candidates.Clear();
                if (!_isSwaying) SwayList.Clear();
                SwayCands.Clear();   // 每次刷新都重填(否则 3 秒刷新会重复追加)
                if (_isAmountPicking && !_tabOverview) { /* 占位: 金额页只显示金额界面 */ }
                // ListPanel 的 IsVisible 在部分版本下不生效 -> 用清空数据的方式保证跨页签不串显示
                bool busy = _isPicking || _isSwaying || _isAmountPicking || _isSwayPicking;
                bool inOverview = !busy && _tabOverview;
                bool inWorld = !busy && !_tabOverview;

                // 世界博弈列表(只列 4 场; 仅"卷入的国家"页; 排除自己正在看的那场)
                Plays.Clear();
                if (inWorld)
                {
                    int shown = 0;
                    for (int i = 0; i < DiploPlays.Active.Count && shown < 4; i++)
                    {
                        var o = DiploPlays.Active[i];
                        if (o.WarDeclared) continue;
                        if (p != null && o.Id == p.Id) continue;   // 自己那场不在世界列表重复出现
                        Plays.Add(new PlayRowVM(o, false));
                        shown++;
                    }
                }

                if (p == null)
                {
                    _playTitle = "外交博弈";
                    _maneuverLine = "";
                    _escText = "";
                    _phaseHint = "没有进行中的博弈";
                    _roleText = "世界和平。想开战就去外交页点『宣战』——会先进入外交博弈, 由你挑选诉求";
                    _roleColor = "#7A7060FF";
                    _hint = "";
                    _initName = _targetName = _initStats = _targetStats = "";
                    _supportA = _supportB = "";
                    _leanHint = "";
                    _goalHint = "";
                    FillEscPips(0f);
                }
                else
                {
                    var init = DiploPlays.K(p.InitiatorId);
                    var target = DiploPlays.K(p.TargetId);
                    string region = "";
                    for (int i = 0; i < p.Demands.Count; i++)
                    {
                        var g = p.Demands[i];
                        if (g.Kind == GoalKind.Conquer && !string.IsNullOrEmpty(g.SettlementId))
                        {
                            var st = DiploPlays.FindSettlement(g.SettlementId);
                            if (st != null && st.Name != null) { region = st.Name.ToString(); break; }
                        }
                    }
                    _playTitle = (region.Length > 0 ? region + "的" : "对" + DiploPlays.NameOf(p.TargetId) + "的") + "外交博弈";

                    float myManeuver = _isInitiator ? p.ManeuverA : (_isTarget ? p.ManeuverB : 0f);
                    _maneuverLine = "外交操作 · 我方机动点 " + (int)myManeuver + " · 双方军力 "
                        + (int)SafeStr(init) + " vs " + (int)SafeStr(target);

                    _escText = "激化 " + ((int)p.Escalation) + "/100 · " + DiploPlays.PhaseText(p);
                    _phaseHint = "外交博弈按激化度分三阶段: 落子(<20, 只加诉求) / 外交操作(20-79, 可拉拢) / 战争倒计时(>=80, 锁定); 到 100 开战";
                    if (inOverview) FillEscPips(p.Escalation); else EscPips.Clear();

                    _initName = DiploPlays.NameOf(p.InitiatorId) + (_isInitiator ? " (我方)" : "");
                    _targetName = DiploPlays.NameOf(p.TargetId) + (_isTarget ? " (我方)" : "");
                    _initStats = "军力 " + (int)SafeStr(init) + " · 战争支持 " + (int)DiploPlays.SupportOf(p.InitiatorId) + "%";
                    _targetStats = "军力 " + (int)SafeStr(target) + " · 战争支持 " + (int)DiploPlays.SupportOf(p.TargetId) + "%";

                    if (_isInitiator) { _roleText = "你是【发起方】: 诉求是你想要的; 对方屈服则全部生效"; _roleColor = "#39FF14FF"; }
                    else if (_isTarget) { _roleText = "你是【防御方】: 对方在要你的东西, 可屈服认怂或加反诉求硬刚"; _roleColor = "#D96A5AFF"; }
                    else { _roleText = "你【未参战】: 可在下方选一场博弈, 加入一方(需接壤/同盟)或保持中立"; _roleColor = "#C8B98FFF"; }

                    _hint = "选择你要的诉求(第一条免费): 割地=让对方割给我国 · 赔款 · 纳贡 · 羞辱 · 通商";
                    _goalHint = "战争目标(点下方按钮添加; 第一条免费)";

                    // 诉求分列 + 支持国
                    var sa = new StringBuilder(); var sb2 = new StringBuilder();
                    for (int i = 0; i < p.Demands.Count; i++)
                    {
                        var g = p.Demands[i];
                        if (!inOverview) break;
                        if (g.OwnerId == p.InitiatorId) { if (DemandsA.Count < 3) DemandsA.Add(new GoalRowVM(g, true)); }
                        else { if (DemandsB.Count < 3) DemandsB.Add(new GoalRowVM(g, false)); }
                    }
                    for (int i = 0; i < p.Members.Count; i++)
                    {
                        var m = p.Members[i];
                        if (m.Stance == 3) { if (sa.Length > 0) sa.Append('、'); sa.Append(DiploPlays.NameOf(m.KingdomId)); }
                        else if (m.Stance == 4) { if (sb2.Length > 0) sb2.Append('、'); sb2.Append(DiploPlays.NameOf(m.KingdomId)); }
                        else Members.Add(new MemberRowVM(p, m, pk, false));
                        if (Members.Count >= 5) break;
                    }
                    _supportA = "支持发起方: " + (sa.Length > 0 ? sa.ToString() : "无");
                    _supportB = "支持防御方: " + (sb2.Length > 0 ? sb2.ToString() : "无");

                    // 倾向列表: 全部潜在参与国(不要求已有记录; 排除已参战/盟友——盟友会自动参战)
                    var tmp = new List<MemberRowVM>();
                    foreach (var kk in Kingdom.All)
                    {
                        if (kk == null || kk.IsEliminated) continue;
                        if (kk.StringId == p.InitiatorId || kk.StringId == p.TargetId) continue;
                        if (pk != null && Diplomacy.IsAlly(pk, kk)) continue;
                        var mm = p.FindMember(kk.StringId);
                        if (mm != null && (mm.Stance == 3 || mm.Stance == 4)) continue;
                        var pm = mm ?? new PlayMember { KingdomId = kk.StringId, Side = 2, Stance = 0 };
                        bool canSwayNow = _isPrimary && !Busy && !(pm.Stance == 3 || pm.Stance == 4 || pm.Stance == 5);
                        tmp.Add(new MemberRowVM(p, pm, pk, canSwayNow));
                    }
                    tmp.Sort(delegate (MemberRowVM x, MemberRowVM y)
                    {
                        int a = 0, b = 0;
                        int.TryParse(x.Pref, out a); int.TryParse(y.Pref, out b);
                        return b.CompareTo(a);
                    });
                    for (int i = 0; i < tmp.Count && i < 4; i++) { if (inOverview) Leaners.Add(tmp[i]); }
                    // 两栏(卷入的国家页): 倾向我方 / 倾向对方
                    for (int i = 0; i < tmp.Count && i < 4; i++)
                    {
                        if (!inWorld) break;
                        int pr = 0;
                        int.TryParse(tmp[i].Pref, out pr);
                        var rowKK = DiploPlays.K(tmp[i].KingdomId);
                        bool warUsRow = pk != null && rowKK != null && DiploPlays.AtWarWithOurSide(pk, rowKK);
                        if (pr >= 0 && !warUsRow) LeanersA.Add(tmp[i]); else LeanersB.Add(tmp[i]);
                    }
                    _leanHint = "倾向支持我方 ← → 倾向支持对方 (点『拉拢』花 20 机动点拉人)";
                    _initHeader = "发起方: " + DiploPlays.NameOf(p.InitiatorId) + " (军力 " + (int)SafeStr(init) + ")";
                    _targetHeader = "目标: " + DiploPlays.NameOf(p.TargetId) + " (军力 " + (int)SafeStr(target) + ")";

                    // 拉拢对象选择页(总览按钮进来): 非盟友、非交战、未参战
                    if (_isSwayPicking)
                    {
                        foreach (var kk in Kingdom.All)
                        {
                            if (kk == null || kk.IsEliminated) continue;
                            if (kk.StringId == p.InitiatorId || kk.StringId == p.TargetId) continue;
                            if (pk != null && Diplomacy.IsAlly(pk, kk)) continue;
                            if (pk != null && DiploPlays.AtWarWithOurSide(pk, kk)) continue;
                            var mm2 = p.FindMember(kk.StringId);
                            if (mm2 != null && (mm2.Stance == 3 || mm2.Stance == 4)) continue;
                            var pm2 = mm2 ?? new PlayMember { KingdomId = kk.StringId, Side = 2, Stance = 0 };
                            SwayCands.Add(new MemberRowVM(p, pm2, pk, true));
                        }
                    }
                }

                int others = 0;
                for (int i = 0; i < DiploPlays.Active.Count; i++)
                {
                    var o = DiploPlays.Active[i];
                    if (o.WarDeclared) continue;
                    if (p != null && o.Id == p.Id) continue;   // 不算自己那场
                    others++;
                }
                _others = "世界其它博弈: " + others + " 场" + (others > 0 ? "(『卷入的国家』页可切换查看)" : "");

                OnPropertyChangedWithValue(_playTitle, "PlayTitle");
                OnPropertyChangedWithValue(_maneuverLine, "ManeuverLine");
                OnPropertyChangedWithValue(_escText, "EscText");
                OnPropertyChangedWithValue(_phaseHint, "PhaseHint");
                OnPropertyChangedWithValue(_roleText, "RoleText");
                OnPropertyChangedWithValue(_roleColor, "RoleColor");
                OnPropertyChangedWithValue(_status, "StatusText");
                OnPropertyChangedWithValue(_others, "OthersText");
                OnPropertyChangedWithValue(_hint, "HintText");
                OnPropertyChangedWithValue(_initName, "InitName");
                OnPropertyChangedWithValue(_initStats, "InitStats");
                OnPropertyChangedWithValue(_targetName, "TargetName");
                OnPropertyChangedWithValue(_targetStats, "TargetStats");
                OnPropertyChangedWithValue(_supportA, "SupportA");
                OnPropertyChangedWithValue(_supportB, "SupportB");
                OnPropertyChangedWithValue(_leanHint, "LeanHint");
                OnPropertyChangedWithValue(_goalHint, "GoalHint");
                OnPropertyChangedWithValue(_tabOverviewColor, "TabOverviewColor");
                OnPropertyChangedWithValue(_tabInvolvedColor, "TabInvolvedColor");
                OnPropertyChangedWithValue(_initHeader, "InitHeader");
                OnPropertyChangedWithValue(_targetHeader, "TargetHeader");
                NotifyRole();
                DiploPlayPanel.RefreshSpotsNow();
            }
            catch (Exception ex) { DLog.Force("博弈页刷新失败: " + ex.Message); }
        }

        private static float SafeStr(Kingdom k)
        {
            try { return k != null ? Math.Max(0f, k.CurrentTotalStrength) : 0f; } catch { return 0f; }
        }
    }
}
