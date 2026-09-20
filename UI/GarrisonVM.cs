using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace FeudalInternalAffairs
{
    // v4.101: 驻军列表一行(兵种行或部队行)
    public class GarrisonListRowVM : ViewModel
    {
        internal readonly MobileParty Party;   // 部队行才有; 兵种行为 null
        internal readonly Army ArmyRef;        // v4.104: 联军行
        private readonly string _name, _num, _tag, _tagColor, _act;

        internal GarrisonListRowVM(string name, string num, string tag, string tagColor, MobileParty party, string act)
        {
            Party = party;
            _name = name; _num = num; _tag = tag; _tagColor = tagColor; _act = act;
        }

        [DataSourceProperty] public string Name { get { return _name; } }
        [DataSourceProperty] public string Num { get { return _num; } }
        [DataSourceProperty] public string Tag { get { return _tag; } }
        [DataSourceProperty] public string TagColor { get { return _tagColor; } }
        [DataSourceProperty] public string ActionText { get { return _act; } }
    }

    // v4.101: 点城市弹出的驻军列表面板
    public class GarrisonPanelVM : PanelVMBase
    {
        private readonly Action _onClose;
        private Settlement _s;
        private string _title = "驻军";
        private string _milText = "";
        private string _armyText = "";
        private string _garrisonText = "0";
        private string _oursText = "0";
        private string _partiesText = "0";
        private int _partyRows;

        internal GarrisonPanelVM(Action onClose)
        {
            _onClose = onClose;
            Rows = new MBBindingList<GarrisonListRowVM>();
        }

        public MBBindingList<GarrisonListRowVM> Rows { get; private set; }

        [DataSourceProperty] public string Title { get { return _title; } }
        [DataSourceProperty] public string MilText { get { return _milText; } }
        [DataSourceProperty] public string ArmyText { get { return _armyText; } }
        [DataSourceProperty] public string GarrisonText { get { return _garrisonText; } }
        [DataSourceProperty] public string OursText { get { return _oursText; } }
        [DataSourceProperty] public string PartiesText { get { return _partiesText; } }
        [DataSourceProperty] public bool HasParties { get { return _partyRows > 0; } }

        public void ExecuteClose() { if (_onClose != null) _onClose(); }

        internal Settlement Current { get { return _s; } }

        internal void Show(Settlement s)
        {
            _s = s;
            Refresh();
        }

        internal void Refresh()
        {
            try
            {
                var s = _s;
                if (s == null) return;
                var target = s.IsVillage && s.Village != null && s.Village.Bound != null ? s.Village.Bound : s;
                _title = (target != null && target.Name != null ? target.Name.ToString() : "?") + " · 驻军";
                Rows.Clear();
                _partyRows = 0;
                int garrisonMen = 0, oursMen = 0;

                // 1) 原版驻军(合并一行; 用户: 把驻军合并, 别散着)
                try
                {
                    if (target.Town != null && target.Town.GarrisonParty != null && target.Town.GarrisonParty.MemberRoster != null)
                        garrisonMen = target.Town.GarrisonParty.MemberRoster.TotalManCount;
                    if (garrisonMen > 0)
                        Rows.Add(new GarrisonListRowVM("原版驻军(合计)", garrisonMen.ToString("N0") + " 人", "原版驻军", "#8A8070FF", null, ""));
                }
                catch { }

                // 2) 国防军守备营(我们募入的兵, 汇总一行)
                try
                {
                    int ours = DefArmy.GarrisonMenOf(target.StringId);
                    oursMen = ours;
                    if (ours > 0)
                        Rows.Add(new GarrisonListRowVM("国防军募入兵员", ours.ToString("N0") + " 人", "国防军守备营", "#7FBF6AFF", null, ""));
                }
                catch { }

                // 3) 城内我方部队(只显示城内的; 操作=出城)
                try
                {
                    var k = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                    foreach (var p in MobileParty.All)
                    {
                        if (p == null || !p.IsActive || p.IsMainParty) continue;
                        if (p.IsGarrison || p.IsMilitia || p.IsCaravan) continue;
                        if (p.CurrentSettlement == null || !ReferenceEquals(p.CurrentSettlement, target)) continue;   // v4.104: 只显示城内
                        bool mine = DefArmy.IsDefArmyParty(p) || (k != null && ReferenceEquals(p.MapFaction, k));
                        if (!mine) continue;
                        string tag;
                        string color;
                        bool def = DefArmy.IsDefArmyParty(p);
                        if (def) { tag = "国防军"; color = "#7FBF6AFF"; }
                        else { tag = "领主军"; color = "#E8C33AFF"; }
                        try { if (p.Army != null) { tag = "联军:" + (p.Army.Name != null ? p.Army.Name.ToString() : "军团"); color = "#6AB0FFFF"; } } catch { }
                        int men = DefArmy.RegularsOf(p);   // v4.123: 士兵数(不含将军)
                        Rows.Add(new GarrisonListRowVM(MapSelection.NameOf(p), men.ToString("N0") + " 人", tag, color, p, "出城"));
                        _partyRows++;
                    }
                }
                catch { }

                _milText = "原版驻军 " + garrisonMen.ToString("N0") + " 人 · 国防军守备营 " + oursMen.ToString("N0") + " 人"
                    + (_partyRows > 0 ? " · 城内我军 " + _partyRows + " 支(可命令出城)" : " · 城内没有我方部队");
                _garrisonText = garrisonMen.ToString("N0");
                _oursText = oursMen.ToString("N0");
                _partiesText = _partyRows.ToString("N0");

                float bd;
                var army = DefArmy.NearestArmyOf(target, out bd);
                if (army != null)
                {
                    int men = 0, pc = 0;
                    try { foreach (var mp in army.Parties) { if (mp != null && mp.IsActive) { men += DefArmy.RegularsOf(mp); pc++; } } } catch { }
                    _armyText = "组建军团: " + (army.Name != null ? army.Name.ToString() : "军团") + "(" + pc + " 支部队/" + men + " 人, 距离 " + (int)Math.Sqrt(bd) + ")";
                }
                else _armyText = "组建军团: 附近没有本国联军";

                OnPropertyChangedWithValue(_title, "Title");
                OnPropertyChangedWithValue(_milText, "MilText");
                OnPropertyChangedWithValue(_armyText, "ArmyText");
                OnPropertyChangedWithValue(_garrisonText, "GarrisonText");
                OnPropertyChangedWithValue(_oursText, "OursText");
                OnPropertyChangedWithValue(_partiesText, "PartiesText");
                OnPropertyChangedWithValue(_partyRows > 0, "HasParties");
            }
            catch (Exception ex) { DLog.Force("驻军列表刷新异常: " + ex.Message); }
        }

        // v4.104: 行操作 = 出城(命令城内部队离开城市到城门外待命)
        internal void ExecuteRowAction(int i)
        {
            try
            {
                if (i < 0 || i >= Rows.Count) return;
                var p = Rows[i].Party;
                if (p == null || !p.IsActive) return;
                var s = _s;
                var target = s != null && s.IsVillage && s.Village != null && s.Village.Bound != null ? s.Village.Bound : s;
                var gate = target != null ? target.GatePosition : p.Position;
                try { p.SetMoveGoToPoint(gate, MobileParty.NavigationType.Default); } catch { }
                try { CommandTimeout.Touch(p, gate); } catch { }
                try { if (p.CurrentSettlement != null) p.Position = gate; } catch { }   // 在城内则直接挪到城门外
                MapSelection.Message("已命令 " + MapSelection.NameOf(p) + " 出城, 到城门外待命");
                Refresh();
            }
            catch (Exception ex) { DLog.Force("命令出城失败: " + ex.Message); }
        }

        // 一键唤出(守备营够 100 编成野战军团, 否则召唤最近联军)
        internal void ExecuteCallOut()
        {
            try
            {
                var s = _s;
                if (s == null) return;
                var target = s.IsVillage && s.Village != null && s.Village.Bound != null ? s.Village.Bound : s;
                if (target == null) return;
                int ours = DefArmy.GarrisonMenOf(target.StringId);
                string msg;
                if (ours >= 100) msg = DefArmy.CreateLegion(target, ours);
                else msg = DefArmy.CallNearestArmy(target);
                MapSelection.Message(msg);
                Refresh();
            }
            catch (Exception ex) { DLog.Force("驻军列表唤出失败: " + ex.Message); }
        }

        internal void ExecuteOpenDrawer()
        {
            try { if (_s != null) SettlementDrawer.Open(_s); } catch { }
        }
    }
}
