using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;

namespace FeudalInternalAffairs
{
    // v4.100: 军队总览一行(v4.104: 支持联军合并行)
    // 列表行结构: 图标(64) + 名称(20) + 类型 + 兵力/士气/组织度/装备满足率(右对齐) + 编成摘要 + 状态色
    public class TroopRowVM : ViewModel
    {
        internal readonly MobileParty Party;   // 普通行
        internal readonly Army ArmyRef;        // 联军合并行
        private string _name, _comp, _icon, _men, _kind, _kindColor, _army, _armyColor,
            _loc, _act, _actColor, _morale, _moraleColor, _org, _orgColor, _equip, _equipColor;

        internal TroopRowVM(MobileParty p)
        {
            Party = p;
            _name = MilIcons.Clip(MapSelection.NameOf(p), 13);
            int men = DefArmy.RegularsOf(p);   // v4.123: 士兵数(不含将军)
            _men = men.ToString("N0") + " 人";
            bool def = DefArmy.IsDefArmyParty(p);
            _kind = def ? "国防军" : (p.LeaderHero != null ? "领主军" : "其它");
            _kindColor = def ? "#7FBF6AFF" : "#E8C33AFF";
            _army = LocShortOf(p);
            _armyColor = "#8A8070FF";
            _loc = LocOf(p);
            _act = "定位";
            _actColor = "#7FBF6AFF";
            _comp = CompOf(p);                       // v4.194: 编成摘要
            _icon = def ? "fia_unit_line_infantry" : (p.LeaderHero != null ? "fia_unit_hussar" : "fia_goods_grain");
            SetQuality(p);
        }

        internal TroopRowVM(Army a, int men, int parties)
        {
            ArmyRef = a;
            _name = MilIcons.Clip(a != null && a.Name != null ? a.Name.ToString() : "联军", 13);
            _men = men.ToString("N0") + " 人";
            _kind = "联军";
            _kindColor = "#6AB0FFFF";
            _army = parties + " 支部队"; _armyColor = "#6AB0FFFF";
            MobileParty lp = null;
            try { lp = a != null ? a.LeaderParty : null; } catch { }
            _loc = lp != null ? LocOf(lp) : "?";
            _act = "操纵";
            _actColor = "#6AB0FFFF";
            _comp = "";
            _icon = "fia_unit_cuirassier";
            SetQualityArmy(a);
        }

        // 士气/组织度/装备满足率(全部内核口径: DefArmyStats / DefArmy.EquipMultOf)
        private void SetQuality(MobileParty p)
        {
            int mor = 50, org = 100, fill = 100;
            try { mor = (int)Math.Round(DefArmyStats.MoraleOf(p.Party)); } catch { }
            try { org = (int)Math.Round(DefArmyStats.OrgOf(p.Party)); } catch { }
            try { fill = (int)Math.Round((DefArmy.EquipMultOf(p) - 0.7f) / 0.3f * 100f); } catch { }
            ApplyQuality(mor, org, fill);
        }

        private void SetQualityArmy(Army a)
        {
            int cnt = 0, morS = 0, orgS = 0, fillS = 0;
            try
            {
                foreach (var p in a.Parties)
                {
                    if (p == null) continue;
                    cnt++;
                    try { morS += (int)Math.Round(DefArmyStats.MoraleOf(p.Party)); } catch { }
                    try { orgS += (int)Math.Round(DefArmyStats.OrgOf(p.Party)); } catch { }
                    try { fillS += (int)Math.Round((DefArmy.EquipMultOf(p) - 0.7f) / 0.3f * 100f); } catch { }
                }
            }
            catch { }
            if (cnt <= 0) { _morale = "—"; _moraleColor = "#8A8070FF"; _org = "—"; _orgColor = "#8A8070FF"; _equip = "—"; _equipColor = "#8A8070FF"; return; }
            ApplyQuality(morS / cnt, orgS / cnt, fillS / cnt);
        }

        private void ApplyQuality(int mor, int org, int fill)
        {
            if (mor < 0) mor = 0;
            if (mor > 100) mor = 100;
            if (org < 0) org = 0;
            if (org > 100) org = 100;
            if (fill < 0) fill = 0;
            if (fill > 100) fill = 100;
            _morale = mor.ToString();
            _moraleColor = mor >= 60 ? "#7FBF6AFF" : (mor >= 35 ? "#E8C33AFF" : "#C96A5AFF");
            _org = org.ToString();
            _orgColor = org >= 70 ? "#7FBF6AFF" : (org >= 40 ? "#E8C33AFF" : "#C96A5AFF");
            _equip = fill + "%";
            _equipColor = fill >= 90 ? "#7FBF6AFF" : (fill >= 60 ? "#E8C33AFF" : "#C96A5AFF");
        }

        // v4.194: 编成摘要(步/弓/骑/骑射, 只列非零; 超长截断防出框)
        private static string CompOf(MobileParty p)
        {
            try
            {
                if (p == null || p.MemberRoster == null) return "";
                int inf = 0, arch = 0, cav = 0, hcav = 0;
                foreach (var e in p.MemberRoster.GetTroopRoster())
                {
                    var c = e.Character;
                    if (c == null || c.IsHero || e.Number <= 0) continue;
                    if (c.IsMounted && c.IsRanged) hcav += e.Number;
                    else if (c.IsMounted) cav += e.Number;
                    else if (c.IsRanged) arch += e.Number;
                    else inf += e.Number;
                }
                var sb = new System.Text.StringBuilder();
                if (inf > 0) sb.Append("步 ").Append(inf);
                if (arch > 0) { if (sb.Length > 0) sb.Append(" · "); sb.Append("弓 ").Append(arch); }
                if (cav > 0) { if (sb.Length > 0) sb.Append(" · "); sb.Append("骑 ").Append(cav); }
                if (hcav > 0) { if (sb.Length > 0) sb.Append(" · "); sb.Append("骑射 ").Append(hcav); }
                return MilIcons.Clip(sb.ToString(), 25);
            }
            catch { return ""; }
        }

        private static Settlement NearestOf(MobileParty p)
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
                return best;
            }
            catch { return null; }
        }

        private static string LocShortOf(MobileParty p)
        {
            try
            {
                var best = NearestOf(p);
                if (best == null || best.Name == null) return "?";
                return MilIcons.Clip(best.Name.ToString(), 6);
            }
            catch { return "?"; }
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
        [DataSourceProperty] public string Comp { get { return _comp; } }
        [DataSourceProperty] public string Icon { get { return _icon; } }
        [DataSourceProperty] public string Men { get { return _men; } }
        [DataSourceProperty] public string Kind { get { return _kind; } }
        [DataSourceProperty] public string KindColor { get { return _kindColor; } }
        [DataSourceProperty] public string ArmyTag { get { return _army; } }
        [DataSourceProperty] public string ArmyColor { get { return _armyColor; } }
        [DataSourceProperty] public string Location { get { return _loc; } }
        [DataSourceProperty] public string ActionText { get { return _act; } }
        [DataSourceProperty] public string ActionColor { get { return _actColor; } }
        [DataSourceProperty] public string Morale { get { return _morale; } }
        [DataSourceProperty] public string MoraleColor { get { return _moraleColor; } }
        [DataSourceProperty] public string Org { get { return _org; } }
        [DataSourceProperty] public string OrgColor { get { return _orgColor; } }
        [DataSourceProperty] public string Equip { get { return _equip; } }
        [DataSourceProperty] public string EquipColor { get { return _equipColor; } }
    }

    // v4.100: 军队总览面板 VM
    public class TroopsPanelVM : PanelVMBase
    {
        private const int Page = 6;   // 行高 84 + 行距 6, 底部留出状态行
        private readonly Action _onClose;
        private readonly List<TroopRowVM> _view = new List<TroopRowVM>();
        private int _scroll;
        private string _summary = "";
        private string _armySummary = "";
        private string _menText = "0";
        private string _legionText = "0";
        private string _composedText = "0";
        private string _status = "";

        internal TroopsPanelVM(Action onClose)
        {
            _onClose = onClose;
            Rows = new MBBindingList<TroopRowVM>();
            Refresh();
        }

        public MBBindingList<TroopRowVM> Rows { get; private set; }

        [DataSourceProperty] public string Summary { get { return _summary; } }
        [DataSourceProperty] public string ArmySummary { get { return _armySummary; } }
        [DataSourceProperty] public string MenText { get { return _menText; } }
        [DataSourceProperty] public string LegionText { get { return _legionText; } }
        [DataSourceProperty] public string ComposedText { get { return _composedText; } }
        [DataSourceProperty] public string StatusText { get { return _status; } }
        [DataSourceProperty] public bool EmptyVisible { get { return Rows == null || Rows.Count == 0; } }

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
                    try { men = DefArmy.RegularsOf(p); } catch { }   // v4.123: 士兵数
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

                _summary = _view.Count + " 支部队" + (_view.Count > Page ? ("(第 " + (_scroll + 1) + " 页, 滚轮翻页)") : "");
                int armyMenTotal = 0;
                foreach (var kv in armyMen) armyMenTotal += kv.Value;
                _menText = total.ToString("N0");
                _legionText = defs.Count + " 支 · " + defMen.ToString("N0") + " 人";
                _composedText = armies.Count + " 个 · " + armyMenTotal.ToString("N0") + " 人";
                string asum = "";
                for (int i = 0; i < armies.Count && i < 3; i++)
                {
                    try
                    {
                        var a = armies[i];
                        string nm = a.Name != null ? a.Name.ToString() : "军团";
                        var leader = a.LeaderParty;
                        asum += nm + "(" + (armyParties.ContainsKey(a) ? armyParties[a] : 0) + "支/"
                            + (armyMen.ContainsKey(a) ? armyMen[a] : 0).ToString("N0") + "人"
                            + (leader != null && leader.LeaderHero != null ? ", 首领 " + leader.LeaderHero.Name : "")
                            + ")  ";
                    }
                    catch { }
                }
                if (armies.Count > 3) asum += "等 " + armies.Count + " 个军团";
                _armySummary = armies.Count > 0 ? ("联军: " + MilIcons.Clip(asum.TrimEnd(), 41)) : "联军: 暂无";
                OnPropertyChangedWithValue(_summary, "Summary");
                OnPropertyChangedWithValue(_armySummary, "ArmySummary");
                OnPropertyChangedWithValue(_menText, "MenText");
                OnPropertyChangedWithValue(_legionText, "LegionText");
                OnPropertyChangedWithValue(_composedText, "ComposedText");
                OnPropertyChangedWithValue(_status, "StatusText");
                OnPropertyChangedWithValue(EmptyVisible, "EmptyVisible");
            }
            catch (Exception ex) { DLog.Force("军队页刷新异常: " + ex.Message); }
        }

        private static int MenOf(MobileParty p)
        {
            try { return DefArmy.RegularsOf(p); } catch { return 0; }   // v4.123: 士兵数
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
                if (p == null || !p.IsActive)
                {
                    _status = "该部队已不存在(列表将在下次刷新时更新)";
                    OnPropertyChangedWithValue(_status, "StatusText");
                    return;
                }
                MapSelection.Select(p);
                NationalWillCamera.FlyTo(p.Position);
                _status = "已定位并选中: " + MapSelection.NameOf(p) + " · 左键点地图即可指挥";
                OnPropertyChangedWithValue(_status, "StatusText");
                MapSelection.Message(_status);
            }
            catch (Exception ex) { DLog.Force("军队页行操作异常: " + ex.Message); }
        }
    }
}
