using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;

namespace FeudalInternalAffairs
{
    // v4.100: 军队总览一行(v4.104: 支持联军合并行)
    public class TroopRowVM : ViewModel
    {
        internal readonly MobileParty Party;   // 普通行
        internal readonly Army ArmyRef;        // 联军合并行
        private readonly string _name, _men, _kind, _kindColor, _army, _armyColor, _loc, _act;

        internal TroopRowVM(MobileParty p)
        {
            Party = p;
            _name = MapSelection.NameOf(p);
            int men = 0;
            try { men = p.MemberRoster.TotalManCount; } catch { }
            _men = men.ToString("N0") + " 人";
            bool def = DefArmy.IsDefArmyParty(p);
            _kind = def ? "国防军" : (p.LeaderHero != null ? "领主军" : "其它");
            _kindColor = def ? "#7FBF6AFF" : "#E8C33AFF";
            _army = "—"; _armyColor = "#8A8070FF";
            _loc = LocOf(p);
            _act = "定位";
        }

        internal TroopRowVM(Army a, int men, int parties)
        {
            ArmyRef = a;
            _name = a != null && a.Name != null ? a.Name.ToString() : "军团";
            _men = men.ToString("N0") + " 人";
            _kind = "联军";
            _kindColor = "#6AB0FFFF";
            _army = parties + " 支部队"; _armyColor = "#6AB0FFFF";
            MobileParty lp = null;
            try { lp = a != null ? a.LeaderParty : null; } catch { }
            _loc = lp != null ? LocOf(lp) : "?";
            _act = "操纵";
        }

        private static string LocOf(MobileParty p)
        {
            try
            {
                Settlement best = null;
                float bd = float.MaxValue;
                var pos = p.Position.ToVec2();
                foreach (var s in Settlement.All)
                {
                    if (s == null) continue;
                    float dx = s.Position.X - pos.X, dy = s.Position.Y - pos.Y;
                    float d = dx * dx + dy * dy;
                    if (d < bd) { bd = d; best = s; }
                }
                if (best == null) return "?";
                return (best.Name != null ? best.Name.ToString() : best.StringId) + " (" + (int)Math.Sqrt(bd) + ")";
            }
            catch { return "?"; }
        }

        [DataSourceProperty] public string Name { get { return _name; } }
        [DataSourceProperty] public string Men { get { return _men; } }
        [DataSourceProperty] public string Kind { get { return _kind; } }
        [DataSourceProperty] public string KindColor { get { return _kindColor; } }
        [DataSourceProperty] public string ArmyTag { get { return _army; } }
        [DataSourceProperty] public string ArmyColor { get { return _armyColor; } }
        [DataSourceProperty] public string Location { get { return _loc; } }
        [DataSourceProperty] public string ActionText { get { return _act; } }
    }

    // v4.100: 军队总览面板 VM
    public class TroopsPanelVM : PanelVMBase
    {
        private const int Page = 12;
        private readonly Action _onClose;
        private readonly List<TroopRowVM> _view = new List<TroopRowVM>();
        private int _scroll;
        private string _summary = "";
        private string _armySummary = "";

        internal TroopsPanelVM(Action onClose)
        {
            _onClose = onClose;
            Rows = new MBBindingList<TroopRowVM>();
            Refresh();
        }

        public MBBindingList<TroopRowVM> Rows { get; private set; }

        [DataSourceProperty] public string Summary { get { return _summary; } }
        [DataSourceProperty] public string ArmySummary { get { return _armySummary; } }

        public void ExecuteClose() { if (_onClose != null) _onClose(); }

        internal int ShownCount { get { return Rows.Count; } }

        internal void Refresh()
        {
            try
            {
                var kingdom = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                var defs = new List<MobileParty>();
                var lords = new List<MobileParty>();
                var armies = new List<Army>();
                var armyMen = new Dictionary<Army, int>();
                var armyParties = new Dictionary<Army, int>();
                int total = 0, defMen = 0;

                foreach (var p in MobileParty.All)
                {
                    if (p == null || !p.IsActive) continue;
                    if (p.IsGarrison || p.IsMilitia || p.IsCaravan) continue;
                    if (p.IsMainParty) continue;
                    bool ours = DefArmy.IsDefArmyParty(p) || (kingdom != null && ReferenceEquals(p.MapFaction, kingdom));
                    if (!ours) continue;
                    int men = 0;
                    try { men = p.MemberRoster.TotalManCount; } catch { }
                    total += men;
                    // v4.104: 已加入联军的部队不单独列, 合并到联军行
                    Army a = null;
                    try { a = p.Army; } catch { }
                    if (a != null)
                    {
                        if (!armyMen.ContainsKey(a)) { armyMen[a] = 0; armyParties[a] = 0; armies.Add(a); }
                        armyMen[a] = armyMen[a] + men;
                        armyParties[a] = armyParties[a] + 1;
                        continue;
                    }
                    if (DefArmy.IsDefArmyParty(p)) { defs.Add(p); defMen += men; }
                    else lords.Add(p);
                }
                defs.Sort(delegate (MobileParty a, MobileParty b) { return MenOf(b).CompareTo(MenOf(a)); });
                lords.Sort(delegate (MobileParty a, MobileParty b) { return MenOf(b).CompareTo(MenOf(a)); });

                _view.Clear();
                // 联军合并行在前
                for (int i = 0; i < armies.Count; i++)
                {
                    var a = armies[i];
                    _view.Add(new TroopRowVM(a, armyMen.ContainsKey(a) ? armyMen[a] : 0, armyParties.ContainsKey(a) ? armyParties[a] : 0));
                }
                for (int i = 0; i < defs.Count; i++) _view.Add(new TroopRowVM(defs[i]));
                for (int i = 0; i < lords.Count; i++) _view.Add(new TroopRowVM(lords[i]));

                int max = Math.Max(0, _view.Count - Page);
                if (_scroll > max) _scroll = max;
                Rows.Clear();
                for (int i = _scroll; i < _view.Count && Rows.Count < Page; i++)
                    Rows.Add(_view[i]);

                _summary = "我方部队 " + _view.Count + " 支 · 共 " + total.ToString("N0") + " 人 ｜ 国防军 " + defs.Count + " 支(" + defMen.ToString("N0") + " 人) ｜ 联军 " + armies.Count + " 个";
                string asum = "";
                for (int i = 0; i < armies.Count; i++)
                {
                    try
                    {
                        var a = armies[i];
                        string nm = a.Name != null ? a.Name.ToString() : "军团";
                        var leader = a.LeaderParty;
                        asum += nm + "(" + (armyParties.ContainsKey(a) ? armyParties[a] : 0) + "支部队/"
                            + (armyMen.ContainsKey(a) ? armyMen[a] : 0).ToString("N0") + "人"
                            + (leader != null && leader.LeaderHero != null ? ", 首领 " + leader.LeaderHero.Name : "")
                            + ")  ";
                    }
                    catch { }
                }
                _armySummary = armies.Count > 0 ? ("组建的军团(联军): " + asum.TrimEnd()) : "组建的军团(联军): 暂无";
                OnPropertyChangedWithValue(_summary, "Summary");
                OnPropertyChangedWithValue(_armySummary, "ArmySummary");
            }
            catch (Exception ex) { DLog.Force("军队页刷新异常: " + ex.Message); }
        }

        private static int MenOf(MobileParty p)
        {
            try { return p.MemberRoster.TotalManCount; } catch { return 0; }
        }

        internal void ScrollStep(int dir)
        {
            try
            {
                int max = Math.Max(0, _view.Count - Page);
                _scroll += dir;
                if (_scroll < 0) _scroll = 0;
                if (_scroll > max) _scroll = max;
                Refresh();
            }
            catch { }
        }

        // v4.104: 联军行 -> 打开军团操作页; 普通行 -> 定位并选中
        internal void ExecuteRowAction(int i)
        {
            try
            {
                if (i < 0 || i >= Rows.Count) return;
                var row = Rows[i];
                if (row.ArmyRef != null)
                {
                    ArmyCommandPanel.Open(row.ArmyRef);
                    return;
                }
                var p = row.Party;
                if (p == null || !p.IsActive) return;
                MapSelection.Select(p);
                NationalWillCamera.FlyTo(p.Position);
                MapSelection.Message("已定位并选中: " + MapSelection.NameOf(p) + " · 左键点地图即可指挥");
            }
            catch (Exception ex) { DLog.Force("军队页行操作异常: " + ex.Message); }
        }
    }
}
