using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace FeudalInternalAffairs
{
    // 外交列表的一行(一个国家)
    public class DiplomacyRowVM : ViewModel
    {
        private readonly Action<DiplomacyRowVM> _war, _peace, _ally, _break, _select;
        private readonly bool _alt;

        internal DiplomacyRowVM(Kingdom k, bool alt, Action<DiplomacyRowVM> war, Action<DiplomacyRowVM> peace,
            Action<DiplomacyRowVM> ally, Action<DiplomacyRowVM> brk, Action<DiplomacyRowVM> select)
        {
            K = k; _alt = alt; _war = war; _peace = peace; _ally = ally; _break = brk; _select = select;
        }

        // 隔行底色(视觉分层)
        [DataSourceProperty]
        public string RowColor { get { return _alt ? "#FFFFFF0C" : "#00000010"; } }

        // 左侧竖条: 按状态着色
        [DataSourceProperty]
        public string AccentColor
        {
            get
            {
                try
                {
                    var pk = PlayerKingdom();
                    if (pk == null || K == null) return "#00000000";
                    if (Diplomacy.IsAlly(pk, K)) return "#7DC97DCC";
                    if (pk.IsAtWarWith(K)) return "#D96A5ACC";
                    return "#C9A22755";
                }
                catch { return "#00000000"; }
            }
        }

        internal Kingdom K { get; private set; }

        [DataSourceProperty]
        public string Name
        {
            get { try { return K != null && K.Name != null ? K.Name.ToString() : "?"; } catch { return "?"; } }
        }

        [DataSourceProperty]
        public string RelationText
        {
            get { try { return Diplomacy.Get(PlayerKingdom(), K).ToString(); } catch { return "0"; } }
        }

        [DataSourceProperty]
        public string RelationColor
        {
            get { try { return Diplomacy.ColorOf(Diplomacy.Get(PlayerKingdom(), K)); } catch { return "#E8D9B5FF"; } }
        }

        [DataSourceProperty]
        public string StanceText
        {
            get
            {
                try
                {
                    var pk = PlayerKingdom();
                    if (pk == null || K == null) return "";
                    if (Diplomacy.IsAlly(pk, K)) return "同盟";
                    if (pk.IsAtWarWith(K))
                    {
                        // v4.74: 战争行显示双方厌战度
                        int ours = (int)WarWeariness.WearOf(pk, K);
                        int theirs = (int)WarWeariness.WearOf(K, pk);
                        return "战争中 · 厌战 我方 " + ours + " / 对方 " + theirs;
                    }
                    return "和平";
                }
                catch { return ""; }
            }
        }

        [DataSourceProperty]
        public string StanceColor
        {
            get
            {
                try
                {
                    var pk = PlayerKingdom();
                    if (pk == null || K == null) return "#E8D9B5FF";
                    if (Diplomacy.IsAlly(pk, K)) return "#39FF14FF";
                    if (pk.IsAtWarWith(K)) return "#D96A5AFF";
                    return "#E8D9B5FF";
                }
                catch { return "#E8D9B5FF"; }
            }
        }

        [DataSourceProperty]
        public string StrengthText
        {
            get
            {
                try
                {
                    int towns = 0, castles = 0;
                    long troops = 0;
                    foreach (var s in K.Settlements)
                    {
                        if (s == null) continue;
                        if (s.IsTown) towns++;
                        else if (s.IsCastle) castles++;
                    }
                    foreach (var p in MobileParty.All)
                    {
                        if (p == null || !p.IsActive || p.MapFaction != K) continue;
                        try { troops += p.MemberRoster != null ? p.MemberRoster.TotalManCount : 0; } catch { }
                    }
                    return towns + "城" + castles + "堡 · " + troops + "兵";
                }
                catch { return ""; }
            }
        }

        [DataSourceProperty]
        public bool CanDeclareWar
        {
            get
            {
                try
                {
                    var pk = PlayerKingdom();
                    return pk != null && K != null && !pk.IsAtWarWith(K) && !Diplomacy.IsAlly(pk, K);
                }
                catch { return false; }
            }
        }

        [DataSourceProperty]
        public bool CanMakePeace
        {
            get
            {
                try
                {
                    var pk = PlayerKingdom();
                    return pk != null && K != null && pk.IsAtWarWith(K);
                }
                catch { return false; }
            }
        }

        [DataSourceProperty]
        public bool CanAlly
        {
            get
            {
                try
                {
                    var pk = PlayerKingdom();
                    if (pk == null || K == null) return false;
                    if (Diplomacy.IsAlly(pk, K) || pk.IsAtWarWith(K)) return false;
                    return Diplomacy.Get(pk, K) >= Diplomacy.AllyThreshold;
                }
                catch { return false; }
            }
        }

        [DataSourceProperty]
        public bool CanBreakAlly
        {
            get { try { return Diplomacy.IsAlly(PlayerKingdom(), K); } catch { return false; } }
        }

        // 单一行动按钮的文案(按状态自动切换: 求和/解除/缔结/宣战)
        [DataSourceProperty]
        public string ActionText
        {
            get
            {
                try
                {
                    if (CanMakePeace) return "求和";
                    if (CanBreakAlly) return "解除";
                    if (CanAlly) return "缔结";
                    if (CanDeclareWar) return "宣战";
                }
                catch { }
                return "—";
            }
        }

        [DataSourceProperty]
        public string ActionColor
        {
            get
            {
                try
                {
                    if (CanMakePeace) return "#E8C33AFF";
                    if (CanBreakAlly) return "#D96A5AFF";
                    if (CanAlly) return "#39FF14FF";
                    if (CanDeclareWar) return "#D96A5AFF";
                }
                catch { }
                return "#7A7060FF";
            }
        }

        // 关系描述(亲密/友好/中立/冷淡/敌对/死敌)
        [DataSourceProperty]
        public string RelationDesc
        {
            get { try { return Diplomacy.Describe(Diplomacy.Get(PlayerKingdom(), K)); } catch { return ""; } }
        }

        // v4.192: 关系图标(fia_dip_*, 圆角方形新标准)
        [DataSourceProperty]
        public string RelationIcon
        {
            get
            {
                try
                {
                    int v = Diplomacy.Get(PlayerKingdom(), K);
                    if (v >= 60) return "fia_dip_friendly";
                    if (v >= 30) return "fia_dip_amicable";
                    if (v >= 10) return "fia_dip_cordial";
                    if (v > -10) return "fia_dip_neutral";
                    if (v > -30) return "fia_dip_poor";
                    if (v > -60) return "fia_dip_cold";
                    return "fia_dip_hostile";
                }
                catch { return "fia_dip_neutral"; }
            }
        }

        // 关系值 + 描述(一行显示)
        [DataSourceProperty]
        public string RelationFull
        {
            get
            {
                try
                {
                    int v = Diplomacy.Get(PlayerKingdom(), K);
                    return v + " · " + Diplomacy.Describe(v);
                }
                catch { return ""; }
            }
        }

        private static Kingdom PlayerKingdom()
        {
            var b = NationalWillOrders.Behavior;
            return b != null ? b.NationKingdom : null;
        }

        public void ExecuteSelect() { try { if (_select != null) _select(this); } catch { } }
        public void ExecuteWar() { try { if (_war != null) _war(this); } catch { } }
        public void ExecutePeace() { try { if (_peace != null) _peace(this); } catch { } }
        public void ExecuteAlly() { try { if (_ally != null) _ally(this); } catch { } }
        public void ExecuteBreakAlly() { try { if (_break != null) _break(this); } catch { } }
    }

    // 外交面板 VM
    public class DiplomacyVM : PanelVMBase
    {
        private readonly Action _onClose;
        private int _tab;                 // 0=战争 1=同盟 2=关系
        private Kingdom _selected;
        private int _scroll;

        internal DiplomacyVM(Action onClose)
        {
            _onClose = onClose;
            Rows = new MBBindingList<DiplomacyRowVM>();
            SelPips = new MBBindingList<PipVM>();
            Refresh();
        }

        [DataSourceProperty]
        public MBBindingList<DiplomacyRowVM> Rows { get; private set; }

        [DataSourceProperty]
        public MBBindingList<PipVM> SelPips { get; private set; }

        [DataSourceProperty]
        public string Title { get { return "外交"; } }

        [DataSourceProperty]
        public string Hint
        {
            get
            {
                switch (_tab)
                {
                    case 0: return "交战国: 点右侧[求和]发起双方领主表决";
                    case 1: return "同盟国: 一方遭入侵时盟友一起宣战(关系 ≥ " + Diplomacy.AllyThreshold + " 才能缔结)";
                    default: return "所有国家: 关系 ≥ " + Diplomacy.AllyThreshold + " 可[缔结], 否则可[宣战](先进入外交博弈)";
                }
            }
        }

        // 顶部概览: 盟友/交战/恶名
        [DataSourceProperty]
        public string SummaryText
        {
            get
            {
                try
                {
                    var pk = PlayerKingdom();
                    if (pk == null) return "";
                    int allies = 0, wars = 0;
                    foreach (var k in Kingdom.All)
                    {
                        if (k == null || k == pk || k.IsEliminated) continue;
                        if (Diplomacy.IsAlly(pk, k)) allies++;
                        else if (pk.IsAtWarWith(k)) wars++;
                    }
                    return "我方: " + pk.Name + "   盟友 " + allies + " · 交战 " + wars + " · 恶名 " + ((int)DiploPlays.InfamyOf(pk.StringId));
                }
                catch { return ""; }
            }
        }

        [DataSourceProperty]
        public string TabWarsText { get { return "战争 (" + CountTab(0) + ")"; } }
        [DataSourceProperty]
        public string TabAlliesText { get { return "同盟 (" + CountTab(1) + ")"; } }
        [DataSourceProperty]
        public string TabRelationsText { get { return "关系 (" + CountTab(2) + ")"; } }

        [DataSourceProperty]
        public string TabWarsColor { get { return _tab == 0 ? "#39FF14FF" : "#7A7060FF"; } }
        [DataSourceProperty]
        public string TabAlliesColor { get { return _tab == 1 ? "#39FF14FF" : "#7A7060FF"; } }
        [DataSourceProperty]
        public string TabRelationsColor { get { return _tab == 2 ? "#39FF14FF" : "#7A7060FF"; } }

        private int CountTab(int tab)
        {
            try
            {
                var pk = PlayerKingdom();
                if (pk == null) return 0;
                int n = 0;
                foreach (var k in Kingdom.All)
                {
                    if (k == null || k == pk || k.IsEliminated) continue;
                    if (tab == 0 && !pk.IsAtWarWith(k)) continue;
                    if (tab == 1 && !Diplomacy.IsAlly(pk, k)) continue;
                    n++;
                }
                return n;
            }
            catch { return 0; }
        }

        [DataSourceProperty]
        public bool IsTabWars { get { return _tab == 0; } }
        [DataSourceProperty]
        public bool IsTabAllies { get { return _tab == 1; } }
        [DataSourceProperty]
        public bool IsTabRelations { get { return _tab == 2; } }

        // ===== 选中王国详情 =====
        [DataSourceProperty]
        public bool HasSelection { get { return _selected != null; } }

        // v4.114: 经济制裁(玩家对外; 收入 -25%)
        [DataSourceProperty]
        public string SanctionText
        {
            get
            {
                try
                {
                    var pk = PlayerKingdom();
                    return _selected != null && pk != null && WarEconomy.SanctionedBy(_selected, pk) ? "解除制裁" : "经济制裁";
                }
                catch { return "经济制裁"; }
            }
        }

        [DataSourceProperty]
        public bool SanctionVisible { get { return _selected != null; } }

        public void ExecuteSanction()
        {
            try
            {
                if (_selected == null) return;
                var pk = PlayerKingdom();
                if (pk == null) return;
                bool on = !WarEconomy.SanctionedBy(_selected, pk);
                WarEconomy.SetSanction(_selected, pk, on);
                MapSelection.Message((on ? "已对 " : "已解除对 ")
                    + (_selected.Name != null ? _selected.Name.ToString() : "?")
                    + (on ? " 的经济制裁(其收入 -25%)" : " 的经济制裁"));
                OnPropertyChanged("SanctionText");
                OnPropertyChanged("SanctionVisible");
            }
            catch (Exception ex) { DLog.Force("制裁操作失败: " + ex.Message); }
        }

        [DataSourceProperty]
        public string SelName
        {
            get { try { return _selected != null && _selected.Name != null ? _selected.Name.ToString() : ""; } catch { return ""; } }
        }

        [DataSourceProperty]
        public string SelRuler
        {
            get
            {
                try
                {
                    if (_selected == null || _selected.Leader == null) return "";
                    return "领袖: " + _selected.Leader.Name;
                }
                catch { return ""; }
            }
        }

        [DataSourceProperty]
        public string SelRelation
        {
            get
            {
                try
                {
                    var pk = PlayerKingdom();
                    int v = Diplomacy.Get(pk, _selected);
                    return "关系 " + v + " (" + Diplomacy.Describe(v) + ")";
                }
                catch { return ""; }
            }
        }

        [DataSourceProperty]
        public string SelRelationColor
        {
            get { try { return Diplomacy.ColorOf(Diplomacy.Get(PlayerKingdom(), _selected)); } catch { return "#E8D9B5FF"; } }
        }

        [DataSourceProperty]
        public string SelStance
        {
            get
            {
                try
                {
                    var pk = PlayerKingdom();
                    if (pk == null || _selected == null) return "";
                    if (Diplomacy.IsAlly(pk, _selected)) return "同盟中";
                    if (pk.IsAtWarWith(_selected)) return "战争中";
                    return "和平";
                }
                catch { return ""; }
            }
        }

        [DataSourceProperty]
        public string SelDetail
        {
            get
            {
                try
                {
                    if (_selected == null) return "";
                    int towns = 0, castles = 0, villages = 0;
                    long troops = 0;
                    foreach (var s in _selected.Settlements)
                    {
                        if (s == null) continue;
                        if (s.IsTown) towns++;
                        else if (s.IsCastle) castles++;
                        else if (s.IsVillage) villages++;
                    }
                    foreach (var p in MobileParty.All)
                    {
                        if (p == null || !p.IsActive || p.MapFaction != _selected) continue;
                        try { troops += p.MemberRoster != null ? p.MemberRoster.TotalManCount : 0; } catch { }
                    }
                    return "领土 " + towns + " 城 / " + castles + " 堡 / " + villages + " 村   兵力 " + troops;
                }
                catch { return ""; }
            }
        }

        private static Kingdom PlayerKingdom()
        {
            var b = NationalWillOrders.Behavior;
            return b != null ? b.NationKingdom : null;
        }

        // ===== 命令 =====
        public void ExecuteTabWars() { _tab = 0; Refresh(); NotifyTabs(); }
        public void ExecuteTabAllies() { _tab = 1; Refresh(); NotifyTabs(); }
        public void ExecuteTabRelations() { _tab = 2; Refresh(); NotifyTabs(); }

        private void NotifyTabs()
        {
            OnPropertyChangedWithValue(IsTabWars, "IsTabWars");
            OnPropertyChangedWithValue(IsTabAllies, "IsTabAllies");
            OnPropertyChangedWithValue(IsTabRelations, "IsTabRelations");
            OnPropertyChangedWithValue(Hint, "Hint");
            OnPropertyChangedWithValue(TabWarsText, "TabWarsText");
            OnPropertyChangedWithValue(TabAlliesText, "TabAlliesText");
            OnPropertyChangedWithValue(TabRelationsText, "TabRelationsText");
            OnPropertyChangedWithValue(TabWarsColor, "TabWarsColor");
            OnPropertyChangedWithValue(TabAlliesColor, "TabAlliesColor");
            OnPropertyChangedWithValue(TabRelationsColor, "TabRelationsColor");
            OnPropertyChangedWithValue(SummaryText, "SummaryText");
        }

        public void ExecuteClose()
        {
            try { if (_onClose != null) _onClose(); }
            catch (Exception ex) { DLog.Force("关闭外交面板失败: " + ex.Message); }
        }

        internal void SelectKingdom(Kingdom k)
        {
            try
            {
                _selected = k;
                Refresh();
                NotifySelection();
            }
            catch { }
        }

        private void NotifySelection()
        {
            OnPropertyChangedWithValue(HasSelection, "HasSelection");
            OnPropertyChangedWithValue(SelName, "SelName");
            OnPropertyChangedWithValue(SelRuler, "SelRuler");
            OnPropertyChangedWithValue(SelRelation, "SelRelation");
            OnPropertyChangedWithValue(SelRelationColor, "SelRelationColor");
            OnPropertyChangedWithValue(SelStance, "SelStance");
            OnPropertyChangedWithValue(SelDetail, "SelDetail");
            // 关系条: 10 格, 从红到绿
            try
            {
                SelPips.Clear();
                int rel = _selected != null ? Diplomacy.Get(PlayerKingdom(), _selected) : 0;
                int filled = (int)Math.Round((rel + 100) / 20f);
                if (filled < 0) filled = 0;
                if (filled > 10) filled = 10;
                string on = rel >= 25 ? "#39FF14FF" : (rel <= -25 ? "#D96A5AFF" : "#E8C33AFF");
                for (int i = 0; i < 10; i++)
                {
                    var pip = new PipVM();
                    pip.Set(i < filled ? on : "#3A342ACC");
                    SelPips.Add(pip);
                }
            }
            catch { }
        }

        private int Page()
        {
            float h = 0f;
            try { h = PanelScreen.ScreenHeight(); } catch { }
            if (h <= 100f) h = 1080f;
            int n = (int)((h - 354f - 120f) / 54f);
            return n < 4 ? 4 : n;
        }

        internal void ScrollStep(int dir)
        {
            try
            {
                _scroll += dir;
                if (_scroll < 0) _scroll = 0;
                Refresh();
            }
            catch { }
        }

        internal void Refresh()
        {
            try
            {
                var pk = PlayerKingdom();
                if (pk == null) return;
                var list = new List<Kingdom>();
                foreach (var k in Kingdom.All)
                {
                    if (k == null || k == pk || k.IsEliminated) continue;
                    if (_tab == 0 && !pk.IsAtWarWith(k)) continue;
                    if (_tab == 1 && !Diplomacy.IsAlly(pk, k)) continue;
                    list.Add(k);
                }
                // 关系降序
                list.Sort(delegate (Kingdom a, Kingdom b)
                {
                    return Diplomacy.Get(pk, b).CompareTo(Diplomacy.Get(pk, a));
                });

                int max = Math.Max(0, list.Count - Page());
                if (_scroll > max) _scroll = max;
                if (_scroll < 0) _scroll = 0;
                Rows.Clear();
                for (int i = _scroll; i < list.Count && Rows.Count < Page(); i++)
                    Rows.Add(new DiplomacyRowVM(list[i], i % 2 == 1, OnWar, OnPeace, OnAlly, OnBreak, OnSelect));
                NotifyTabs();
                if (_selected != null) NotifySelection();
                OnPropertyChanged("SanctionText");   // v4.114
                OnPropertyChanged("SanctionVisible");
            }
            catch (Exception ex) { DLog.Info("外交列表刷新异常: " + ex.Message); }
        }

        private void OnSelect(DiplomacyRowVM row) { try { if (row != null) SelectKingdom(row.K); } catch { } }

        private void OnWar(DiplomacyRowVM row)
        {
            try
            {
                if (row == null || row.K == null) return;
                string name = row.K.Name.ToString();
                var pk = PlayerKingdom();
                if (pk == null) return;
                // 第 22 章: 统一入口——盟友战争直接参战 / 盟友博弈自动加入 / 否则开新博弈自选诉求
                string msg;
                var play = DiploPlays.PlayerDeclareOn(row.K, out msg);
                if (play != null)
                {
                    MapSelection.Message("已向 " + name + " 发出宣战诏书 — 去『博弈』页选择你要的诉求");
                    DiploPlayPanel.Open();
                }
                else if (!string.IsNullOrEmpty(msg))
                    MapSelection.Message(msg);
                Refresh();
            }
            catch (Exception ex) { DLog.Force("宣战失败: " + ex.Message); }
        }

        private void OnPeace(DiplomacyRowVM row)
        {
            try
            {
                if (row == null || row.K == null) return;
                if (DiplomacyBehavior.PlayerMakePeace(row.K))
                    MapSelection.Message("已与 " + row.K.Name + " 停战");
                Refresh();
            }
            catch { }
        }

        private void OnAlly(DiplomacyRowVM row)
        {
            try
            {
                if (row == null || row.K == null) return;
                if (DiplomacyBehavior.PlayerStartAlliance(row.K))
                    MapSelection.Message("已与 " + row.K.Name + " 缔结同盟");
                Refresh();
            }
            catch { }
        }

        private void OnBreak(DiplomacyRowVM row)
        {
            try
            {
                if (row == null || row.K == null) return;
                if (DiplomacyBehavior.PlayerBreakAlliance(row.K))
                    MapSelection.Message("已解除与 " + row.K.Name + " 的同盟");
                Refresh();
            }
            catch { }
        }
    }
}
