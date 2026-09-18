using System;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;

namespace FeudalInternalAffairs
{
    // 建筑详情(文档 20.8; 对齐 V3 建筑详情)
    public class BldDetailVM : PanelVMBase
    {
        private readonly Action _onClose;
        private string _sid, _defId;
        private string _title = "", _sub = "", _l1 = "", _l2 = "", _l3 = "", _hist = "", _status = "";
        private string _modeText = "";

        internal BldDetailVM(Action onClose, string sid, string defId)
        {
            _onClose = onClose;
            _sid = sid;
            _defId = defId;
            Refresh();
        }

        // 面板已开时切换目标(点建筑总表另一行)
        internal void SetTarget(string sid, string defId)
        {
            _sid = sid;
            _defId = defId;
            _status = "";
            Refresh();
        }

        [DataSourceProperty] public string Title { get { return _title; } }
        [DataSourceProperty] public string Subtitle { get { return _sub; } }
        [DataSourceProperty] public string Line1 { get { return _l1; } }
        [DataSourceProperty] public string Line2 { get { return _l2; } }
        [DataSourceProperty] public string Line3 { get { return _l3; } }
        [DataSourceProperty] public string HistText { get { return _hist; } }
        [DataSourceProperty] public string Status { get { return _status; } }
        [DataSourceProperty] public string ModeText { get { return _modeText; } }

        public void ExecuteClose() { if (_onClose != null) _onClose(); }

        private BuildingGroup G()
        {
            try
            {
                var sb = EconomyWorld.Find(_sid);
                return sb != null ? sb.Find(_defId) : null;
            }
            catch { return null; }
        }

        private Settlement S()
        {
            try
            {
                foreach (var s in Settlement.All) if (s != null && s.StringId == _sid) return s;
            }
            catch { }
            return null;
        }

        internal void Refresh()
        {
            try
            {
                var g = G();
                var s = S();
                var def = BuildDefs.Get(_defId);
                if (g == null || def == null) { _title = "建筑不存在"; return; }
                _title = def.Name;
                _sub = (s != null && s.Name != null ? s.Name.ToString() : "?") + " · " + (s != null && s.IsTown ? "城镇" : (s != null && s.IsCastle ? "城堡" : "村庄"));
                int owner = Ownership.OwnerOf(g, s);
                _modeText = g.Mode == BuildMode.Iron ? "铁档" : (g.Mode == BuildMode.Stone ? "石档" : "木档");
                _l1 = "等级 ×" + g.Count + " · 所有者 " + Ownership.NameOf(owner) + " · 模式 " + _modeText
                    + (g.Stalled ? " · 停产: " + (g.StallReason ?? "") : "");
                float cap = InvestmentPool.ChestCap(g, s);
                _l2 = "到岗 " + (int)Math.Round(g.Fill * 100f) + "% · 工资系数 ×" + g.WageMult.ToString("F2")
                    + " · 钱柜 " + ((int)g.Cash).ToString("N0") + " / " + ((int)cap).ToString("N0")
                    + " · 上月利润率 " + (g.Margin * 100f).ToString("F0") + "%";
                var spec = PopJob.SpecOf(_defId);
                string jobs = "";
                if (spec != null)
                {
                    for (int i = 0; i < spec.Prof.Length; i++)
                    {
                        if (jobs.Length > 0) jobs += " / ";
                        jobs += PopDefs.NameOf(spec.Prof[i]) + "×" + (spec.Num[i] * g.Count);
                    }
                }
                _l3 = "岗位: " + (jobs.Length > 0 ? jobs : "—");
                _hist = BldHist.TextOf(_sid, _defId);
                var clan = s != null && s.OwnerClan != null ? s.OwnerClan : null;
                if (clan != null)
                {
                    int fav = Ownership.FavorOf(clan.StringId);
                    if (fav != 0) _sub += " · 领主好感 " + (fav > 0 ? "+" : "") + fav;
                }
            }
            catch (Exception ex) { DLog.Force("建筑详情刷新失败: " + ex.Message); }
        }

        internal void CycleMode()
        {
            try
            {
                var g = G();
                var s = S();
                var def = BuildDefs.Get(_defId);
                if (g == null || def == null) return;
                int next = ((int)g.Mode + 1) % 3;
                int curWork = def.Work[(int)g.Mode];
                int nextWork = def.Work[next];
                if (nextWork > curWork)
                {
                    int cost = (nextWork - curWork) * 8 * Math.Max(1, g.Count);
                    if (EconomyWorld.Treasury.Gold < cost) { _status = "国库不足(升级需 " + cost.ToString("N0") + ")"; return; }
                    EconomyWorld.TreasurySpend(cost);
                    _status = "已切换生产方法(花费 " + cost.ToString("N0") + ")";
                }
                else _status = "已切换生产方法";
                g.Mode = (BuildMode)next;
                EconomyWorld.MarkDirty();
                Refresh();
            }
            catch { }
        }

        internal void Buy() { try { var g = G(); _status = Ownership.Buy(g, S()); Refresh(); } catch { } }
        internal void Sell() { try { var g = G(); _status = Ownership.Sell(g, S()); Refresh(); } catch { } }
        internal void Confiscate()
        {
            try
            {
                var g = G();
                int today = 0;
                try { today = (int)TaleWorlds.CampaignSystem.CampaignTime.Now.ToDays; } catch { }
                _status = Ownership.Confiscate(g, S(), today);
                Refresh();
            }
            catch { }
        }
    }
}
