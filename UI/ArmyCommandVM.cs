using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Library;

namespace FeudalInternalAffairs
{
    // v4.104: 军团操作页一行(成员部队)
    public class ArmyMemberRowVM : ViewModel
    {
        internal readonly MobileParty Party;
        private readonly string _name, _men, _leader;

        internal ArmyMemberRowVM(MobileParty p)
        {
            Party = p;
            _name = MapSelection.NameOf(p);
            int men = DefArmy.RegularsOf(p);   // v4.123: 士兵数
            _men = men.ToString("N0") + " 人";
            string ld = "—";
            try { if (p.LeaderHero != null) ld = p.LeaderHero.Name != null ? p.LeaderHero.Name.ToString() : "?"; } catch { }
            _leader = ld;
        }

        [DataSourceProperty] public string Name { get { return _name; } }
        [DataSourceProperty] public string Men { get { return _men; } }
        [DataSourceProperty] public string Leader { get { return _leader; } }
    }

    // v4.104: 军团操作面板(解散军团 / 命令某人出军团)
    public class ArmyCommandVM : PanelVMBase
    {
        private readonly Action _onClose;
        private Army _army;
        private string _title = "军团";
        private string _info = "";

        internal ArmyCommandVM(Action onClose)
        {
            _onClose = onClose;
            Rows = new MBBindingList<ArmyMemberRowVM>();
        }

        public MBBindingList<ArmyMemberRowVM> Rows { get; private set; }

        [DataSourceProperty] public string Title { get { return _title; } }
        [DataSourceProperty] public string Info { get { return _info; } }
        [DataSourceProperty] public bool EmptyVisible { get { return Rows == null || Rows.Count == 0; } }

        public void ExecuteClose() { if (_onClose != null) _onClose(); }

        internal Army Current { get { return _army; } }

        internal void Show(Army a) { _army = a; Refresh(); }

        internal void Refresh()
        {
            try
            {
                var a = _army;
                if (a == null) { Rows.Clear(); return; }
                _title = (a.Name != null ? a.Name.ToString() : "军团") + " · 操作";
                Rows.Clear();
                int men = 0, pc = 0;
                var list = new List<MobileParty>();
                try { foreach (var p in a.Parties) if (p != null && p.IsActive) list.Add(p); } catch { }
                for (int i = 0; i < list.Count; i++)
                {
                    Rows.Add(new ArmyMemberRowVM(list[i]));
                    try { men += list[i].MemberRoster.TotalManCount; } catch { }
                    pc++;
                }
                string leader = "—";
                try { if (a.LeaderParty != null && a.LeaderParty.LeaderHero != null) leader = a.LeaderParty.LeaderHero.Name.ToString(); } catch { }
                _info = "成员 " + pc + " 支部队 · 共 " + men.ToString("N0") + " 人 · 首领 " + leader;
                OnPropertyChangedWithValue(_title, "Title");
                OnPropertyChangedWithValue(_info, "Info");
                OnPropertyChangedWithValue(EmptyVisible, "EmptyVisible");
            }
            catch (Exception ex) { DLog.Force("军团操作页刷新异常: " + ex.Message); }
        }

        // 命令某支部队离开军团
        internal void ExecuteLeave(int i)
        {
            try
            {
                if (i < 0 || i >= Rows.Count) return;
                var p = Rows[i].Party;
                if (p == null || !p.IsActive) return;
                var a = _army;
                bool isLeader = false;
                try { isLeader = a != null && ReferenceEquals(a.LeaderParty, p); } catch { }
                if (isLeader) { MapSelection.Message("首领部队不能退出军团(需要先解散军团)"); return; }
                try { p.Army = null; } catch (Exception ex) { DLog.Force("离团失败: " + ex.Message); }
                MapSelection.Message(MapSelection.NameOf(p) + " 已离开军团");
                Refresh();
            }
            catch (Exception ex) { DLog.Force("军团离团异常: " + ex.Message); }
        }

        // 解散整个军团(所有成员离开, 含首领)
        internal void ExecuteDisband()
        {
            try
            {
                var a = _army;
                if (a == null) return;
                var list = new List<MobileParty>();
                try { foreach (var p in a.Parties) if (p != null && p.IsActive) list.Add(p); } catch { }
                int n = 0;
                for (int i = 0; i < list.Count; i++)
                {
                    try { list[i].Army = null; n++; } catch { }
                }
                MapSelection.Message("已解散军团(" + n + " 支部队恢复独立)");
                _army = null;
                Rows.Clear();
                if (_onClose != null) _onClose();
            }
            catch (Exception ex) { DLog.Force("解散军团异常: " + ex.Message); }
        }
    }
}
