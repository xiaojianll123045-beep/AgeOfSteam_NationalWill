using System;
using TaleWorlds.Library;

namespace FeudalInternalAffairs
{
    // 战报条目(整段文字; v4.209)
    public class ReportRowVM : ViewModel
    {
        private readonly string _title, _body;
        internal ReportRowVM(string title, string body) { _title = title; _body = body; }
        [DataSourceProperty] public string Title { get { return _title; } }
        [DataSourceProperty] public string Body { get { return _body; } }
    }

    // 战报页 VM(自建页: 最近 8 场战斗的完整战报)
    public class BattleReportVM : PanelVMBase
    {
        private readonly Action _onClose;
        public BattleReportVM(Action onClose)
        {
            _onClose = onClose;
            Rows = new MBBindingList<ReportRowVM>();
            Refresh();
        }

        public MBBindingList<ReportRowVM> Rows { get; private set; }

        [DataSourceProperty] public string Summary
        {
            get
            {
                try { return "最近 " + BattleSim.Recent.Count + " 场坐镇指挥 · 战斗宽度 = ceil((5+0.5×基建)×地形) · 兵种克制 骑>弓>步>骑"; }
                catch { return ""; }
            }
        }

        [DataSourceProperty] public string LastDetail
        {
            get
            {
                try
                {
                    return string.IsNullOrEmpty(BattleSim.LastDetail)
                        ? "最近战斗明细: 暂无(尚无坐镇指挥记录)"
                        : "最近战斗明细: " + BattleSim.LastDetail;
                }
                catch { return ""; }
            }
        }

        public void ExecuteClose() { if (_onClose != null) _onClose(); }

        internal void Refresh()
        {
            try
            {
                Rows.Clear();
                Rows.Add(new ReportRowVM("本周损耗 / 文化战损", ArmyDoctrine.LossSummary()));
                int battles = 0;
                for (int i = 0; i < BattleSim.Recent.Count && i < 8; i++)
                {
                    string r = BattleSim.Recent[i];
                    if (string.IsNullOrEmpty(r)) continue;
                    string title = "第 " + (i + 1) + " 近";
                    string body = r;
                    int nl = r.IndexOf('\n');
                    if (nl > 0) { title = r.Substring(0, nl); body = r.Substring(nl + 1); }
                    Rows.Add(new ReportRowVM(title, body));
                    battles++;
                }
                if (battles == 0) Rows.Add(new ReportRowVM("暂无战报", "还没有坐镇指挥过的战斗。\n开战后选择「坐镇指挥」, 系统会按 模型逐场结算: 组织度/士气/伤员/损耗/文化分摊。"));
                OnPropertyChangedWithValue(Rows.Count, "Rows");
                OnPropertyChangedWithValue(Summary, "Summary");
                OnPropertyChangedWithValue(LastDetail, "LastDetail");
            }
            catch (Exception ex) { DLog.Force("战报页刷新失败: " + ex.Message); }
        }
    }
}
