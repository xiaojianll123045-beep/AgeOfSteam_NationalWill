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
                    if (pk.IsAtWarWith(K)) return "战争中";
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
                    if (Diplomacy.IsAlly(pk, K)) return "#7DC97DFF";
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

        internal DiplomacyVM(Action onClose)
        {
            _onClose = onClose;
            Rows = new MBBindingList<DiplomacyRowVM>();
            Refresh();
        }

        [DataSourceProperty]
        public MBBindingList<DiplomacyRowVM> Rows { get; private set; }

        [DataSourceProperty]
        public string Title { get { return "外交"; } }

        [DataSourceProperty]
        public string Hint
        {
            get
            {
                switch (_tab)
                {
                    case 0: return "交战国: 可在此求和";
                    case 1: return "同盟国: 一方遭入侵时盟友一起宣战(关系 ≥ " + Diplomacy.AllyThreshold + " 才能缔结)";
                    default: return "所有国家的关系值(-100 ~ +100): 影响宣战 / 和谈 / 同盟";
                }
            }
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

                Rows.Clear();
                for (int i = 0; i < list.Count; i++)
                    Rows.Add(new DiplomacyRowVM(list[i], i % 2 == 1, OnWar, OnPeace, OnAlly, OnBreak, OnSelect));
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
                MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                    "宣战", "确定向 " + name + " 宣战? (关系 -" + Math.Abs(Diplomacy.WarPenalty) + ", 其盟友会一起参战)",
                    new List<InquiryElement> { new InquiryElement("yes", "宣战", null, true, null) },
                    true, 1, 1, "确定", "取消",
                    delegate (List<InquiryElement> sel)
                    {
                        if (sel != null && sel.Count > 0 && DiplomacyBehavior.PlayerDeclareWar(row.K))
                            MapSelection.Message("已向 " + name + " 宣战");
                        Refresh();
                    }, null, null, false));
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
