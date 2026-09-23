using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;

namespace FeudalInternalAffairs
{
    // 缔约候选行(v4.204: 自己实现的条约页, 每行 4 个"签署"小按钮)
    public class TreatyCandVM : ViewModel
    {
        internal readonly Kingdom K;
        private readonly string _name, _rel;
        private readonly string[] _icons = new string[4];
        private readonly string[] _costs = new string[4];
        private readonly bool[] _ok = new bool[4];

        internal TreatyCandVM(Kingdom k)
        {
            K = k;
            try { _name = k != null && k.Name != null ? k.Name.ToString() : "?"; } catch { _name = "?"; }
            int rel = 0;
            try { rel = Diplomacy.Get(PlayerKingdom(), k); } catch { }
            _rel = "关系 " + rel;
            int today = Politics.Today();
            for (int i = 0; i < 4; i++)
            {
                string type = Treaties.TypeIds[i];
                _icons[i] = IconOf(type);
                var t = Treaties.Find(k, type);
                if (t != null)
                {
                    _costs[i] = "撕毁(" + t.Left(today) + "日)";
                    _ok[i] = true;
                }
                else
                {
                    int cost = (int)Math.Round(Treaties.TypeCost[i] * Treaties.LevelCostMult[Treaties.SignLevel - 1] * (1f + (60f - Treaties.Credit) / 100f));
                    _costs[i] = cost.ToString();
                    _ok[i] = Politics.Authority >= cost;
                }
            }
        }

        internal static string IconOf(string type)
        {
            switch (type)
            {
                case "trade": return "fia_dip_trade_agreement";
                case "invest": return "fia_dip_investment_rights";
                case "ally": return "fia_dip_alliance";
                case "nap": return "fia_dip_non_aggression";
            }
            return "fia_dip_neutral";
        }

        internal static Kingdom PlayerKingdom()
        {
            try { return NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null; } catch { return null; }
        }

        [DataSourceProperty] public string Name { get { return _name; } }
        [DataSourceProperty] public string Relation { get { return _rel; } }
        [DataSourceProperty] public string Icon0 { get { return _icons[0]; } }
        [DataSourceProperty] public string Icon1 { get { return _icons[1]; } }
        [DataSourceProperty] public string Icon2 { get { return _icons[2]; } }
        [DataSourceProperty] public string Icon3 { get { return _icons[3]; } }
        [DataSourceProperty] public string Cost0 { get { return _costs[0]; } }
        [DataSourceProperty] public string Cost1 { get { return _costs[1]; } }
        [DataSourceProperty] public string Cost2 { get { return _costs[2]; } }
        [DataSourceProperty] public string Cost3 { get { return _costs[3]; } }
        [DataSourceProperty] public bool Ok0 { get { return _ok[0]; } }
        [DataSourceProperty] public bool Ok1 { get { return _ok[1]; } }
        [DataSourceProperty] public bool Ok2 { get { return _ok[2]; } }
        [DataSourceProperty] public bool Ok3 { get { return _ok[3]; } }
    }

    // 生效中条约行
    public class TreatyActiveVM : ViewModel
    {
        private readonly string _icon, _name, _desc, _color;
        internal TreatyActiveVM(Treaty t)
        {
            _icon = TreatyCandVM.IconOf(t.Type);
            var k = Treaties.FindKingdom(t.Partner);
            _name = (k != null && k.Name != null ? k.Name.ToString() : t.Partner) + " · " + Treaties.NameOf(t.Type);
            _desc = Treaties.LevelNames[Math.Min(2, Math.Max(0, t.Level - 1))] + " · 剩 " + t.Left(Politics.Today())
                  + " 日 · 月 " + t.Monthly;
            _color = t.Level >= 3 ? "#E8C33AFF" : (t.Level == 2 ? "#8FD8E8FF" : "#D8C9A0FF");
        }
        [DataSourceProperty] public string Icon { get { return _icon; } }
        [DataSourceProperty] public string Name { get { return _name; } }
        [DataSourceProperty] public string Desc { get { return _desc; } }
        [DataSourceProperty] public string DescColor { get { return _color; } }
    }

    // 条约页 VM(自己实现的 680 宽侧栏页: 等级选择 + 缔约行 + 生效中条约)
    public class TreatyVM : PanelVMBase
    {
        private readonly Action _onClose;
        private string _credit = "", _auth = "";
        private string _lv0 = "#C9A227FF", _lv1 = "#8A8070FF", _lv2 = "#8A8070FF";
        private string _lv0Bg = "#FFFFFF14", _lv1Bg = "#00000066", _lv2Bg = "#00000066";

        public TreatyVM(Action onClose)
        {
            _onClose = onClose;
            Cands = new MBBindingList<TreatyCandVM>();
            Actives = new MBBindingList<TreatyActiveVM>();
            Refresh();
        }

        public MBBindingList<TreatyCandVM> Cands { get; private set; }
        public MBBindingList<TreatyActiveVM> Actives { get; private set; }

        [DataSourceProperty] public string CreditText { get { return _credit; } }
        [DataSourceProperty] public string AuthText { get { return _auth; } }
        [DataSourceProperty] public string Lv0Color { get { return _lv0; } }
        [DataSourceProperty] public string Lv1Color { get { return _lv1; } }
        [DataSourceProperty] public string Lv2Color { get { return _lv2; } }
        [DataSourceProperty] public string Lv0Bg { get { return _lv0Bg; } }
        [DataSourceProperty] public string Lv1Bg { get { return _lv1Bg; } }
        [DataSourceProperty] public string Lv2Bg { get { return _lv2Bg; } }
        [DataSourceProperty] public int CandsCount { get { return Cands.Count; } }

        public void ExecuteClose() { if (_onClose != null) _onClose(); }

        internal void SetLevel(int lv)
        {
            Treaties.SignLevel = Math.Min(3, Math.Max(1, lv));
            Refresh();
        }

        internal void SignClick(int candIdx, int typeIdx)
        {
            try
            {
                if (candIdx < 0 || candIdx >= Cands.Count) return;
                var k = Cands[candIdx].K;
                string type = Treaties.TypeIds[typeIdx];
                string msg = Treaties.Find(k, type) != null ? Treaties.Break(k, type) : Treaties.Sign(k, type, Treaties.SignLevel);
                try { MapSelection.Message(msg); } catch { }
                Refresh();
            }
            catch (Exception ex) { DLog.Force("条约操作失败: " + ex.Message); }
        }

        internal void BreakClick(int idx)
        {
            try
            {
                int n = 0;
                var list = ActiveList();
                if (idx < 0 || idx >= list.Count) return;
                var t = list[idx];
                var k = Treaties.FindKingdom(t.Partner);
                string msg = Treaties.Break(k, t.Type);
                try { MapSelection.Message(msg); } catch { }
                Refresh();
            }
            catch (Exception ex) { DLog.Force("撕毁失败: " + ex.Message); }
        }

        private static List<Treaty> ActiveList()
        {
            var list = new List<Treaty>();
            try
            {
                int today = Politics.Today();
                for (int i = 0; i < Treaties.All.Count; i++)
                {
                    var t = Treaties.All[i];
                    if (t == null || t.Broken || t.Left(today) <= 0) continue;
                    list.Add(t);
                }
            }
            catch { }
            return list;
        }

        internal void Refresh()
        {
            try
            {
                _credit = "信誉: " + ((int)Treaties.Credit);
                _auth = "权威: " + ((int)Politics.Authority);
                int lv = Treaties.SignLevel;
                _lv0 = lv == 1 ? "#C9A227FF" : "#8A8070FF"; _lv1 = lv == 2 ? "#C9A227FF" : "#8A8070FF"; _lv2 = lv == 3 ? "#C9A227FF" : "#8A8070FF";
                _lv0Bg = lv == 1 ? "#FFFFFF14" : "#00000066"; _lv1Bg = lv == 2 ? "#FFFFFF14" : "#00000066"; _lv2Bg = lv == 3 ? "#FFFFFF14" : "#00000066";
                OnPropertyChangedWithValue(_credit, "CreditText");
                OnPropertyChangedWithValue(_auth, "AuthText");
                OnPropertyChangedWithValue(_lv0, "Lv0Color"); OnPropertyChangedWithValue(_lv1, "Lv1Color"); OnPropertyChangedWithValue(_lv2, "Lv2Color");
                OnPropertyChangedWithValue(_lv0Bg, "Lv0Bg"); OnPropertyChangedWithValue(_lv1Bg, "Lv1Bg"); OnPropertyChangedWithValue(_lv2Bg, "Lv2Bg");

                Cands.Clear();
                var ck = Treaties.Candidates();
                for (int i = 0; i < ck.Count && i < 6; i++)
                    if (ck[i] != null) Cands.Add(new TreatyCandVM(ck[i]));
                Actives.Clear();
                var list = ActiveList();
                for (int i = 0; i < list.Count && i < 10; i++) Actives.Add(new TreatyActiveVM(list[i]));
                OnPropertyChangedWithValue(CandsCount, "CandsCount");
            }
            catch (Exception ex) { DLog.Force("条约页刷新失败: " + ex.Message); }
        }
    }
}
