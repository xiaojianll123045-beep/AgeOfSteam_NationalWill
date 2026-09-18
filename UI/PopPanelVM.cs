using System;
using System.Collections.Generic;
using TaleWorlds.Library;

namespace FeudalInternalAffairs
{
    // 阶级卡(文档 19.14.2; 布局参照维多利亚3"人口"窗口的三张阶级卡)
    public class PopStrataVM : ViewModel
    {
        private readonly string _name, _pops, _sol, _solColor, _income, _need, _surplus, _surplusColor;

        internal PopStrataVM(string name, float people, float sol, float incomePerAdult, float needSat, float surplusPct, bool settled)
        {
            _name = name;
            _pops = ((int)people).ToString("N0") + " 人";
            _sol = "生活水平 " + sol.ToString("F1") + " " + SolLabel(sol);
            _solColor = ColorOfSol(sol);
            // 未结算(读档后没走过一个游戏日)时显示 —, 避免看起来像 0 收入的 bug
            if (needSat < 0f) needSat = 0f;
            if (needSat > 1f) needSat = 1f;
            _income = settled ? "收入 " + incomePerAdult.ToString("F1") + " / 人·周" : "收入 — / 人·周";
            _need = settled ? "需求满足 " + (int)Math.Round(needSat * 100f) + "%" : "需求满足 —";
            _surplus = settled ? (surplusPct >= 0f ? "盈余 +" : "赤字 ") + (int)Math.Round(surplusPct * 100f) + "%" : "盈余 —";
            _surplusColor = !settled ? "#8A8070FF" : (surplusPct >= 0f ? "#39FF14FF" : "#D96A5AFF");
        }

        [DataSourceProperty] public string Name { get { return _name; } }
        [DataSourceProperty] public string Pops { get { return _pops; } }
        [DataSourceProperty] public string Sol { get { return _sol; } }
        [DataSourceProperty] public string SolColor { get { return _solColor; } }
        [DataSourceProperty] public string Income { get { return _income; } }
        [DataSourceProperty] public string Need { get { return _need; } }
        [DataSourceProperty] public string Surplus { get { return _surplus; } }
        [DataSourceProperty] public string SurplusColor { get { return _surplusColor; } }

        internal static string SolLabel(float sol)
        {
            if (sol < 1f) return "赤贫";
            if (sol < 5f) return "困顿";
            if (sol < 6f) return "挣扎";
            if (sol < 10f) return "贫困";
            if (sol < 15f) return "小康";
            if (sol < 20f) return "安稳";
            if (sol < 25f) return "兴旺";
            if (sol < 30f) return "富足";
            if (sol < 31f) return "富裕";
            if (sol < 40f) return "奢华";
            if (sol < 45f) return "豪奢";
            if (sol < 50f) return "巨富";
            if (sol < 60f) return "顶级";
            return "极奢";
        }

        internal static string ColorOfSol(float sol)
        {
            if (sol < 5f) return "#D96A5AFF";
            if (sol < 10f) return "#E8A33AFF";
            if (sol < 15f) return "#C8B98FFF";
            if (sol < 25f) return "#E8C33AFF";
            return "#F5D76EFF";
        }
    }

    // 职业行
    public class PopRowVM : ViewModel
    {
        private readonly string _name, _pops, _sol, _solColor, _need, _needColor, _icon, _stratumColor;
        private readonly int _idx;

        internal PopRowVM(int idx, string name, float people, float sol, float needSat, string icon, int stratum)
        {
            _idx = idx;
            _name = name;
            _pops = ((int)people).ToString("N0");
            if (needSat < 0f) needSat = 0f;
            if (needSat > 1f) needSat = 1f;
            _sol = sol.ToString("F1") + " " + PopStrataVM.SolLabel(sol);
            _solColor = PopStrataVM.ColorOfSol(sol);
            _need = (int)Math.Round(needSat * 100f) + "%";
            _needColor = needSat >= 0.9f ? "#39FF14FF" : (needSat >= 0.6f ? "#E8C33AFF" : "#D96A5AFF");
            _icon = icon;
            _stratumColor = stratum == 2 ? "#E8C33AFF" : (stratum == 1 ? "#C8B98FFF" : "#8FA8C8FF");
        }

        [DataSourceProperty] public string Name { get { return _name; } }
        [DataSourceProperty] public string Pops { get { return _pops; } }
        [DataSourceProperty] public string Sol { get { return _sol; } }
        [DataSourceProperty] public string SolColor { get { return _solColor; } }
        [DataSourceProperty] public string Need { get { return _need; } }
        [DataSourceProperty] public string NeedColor { get { return _needColor; } }
        [DataSourceProperty] public string Icon { get { return _icon; } }
        [DataSourceProperty] public string StratumColor { get { return _stratumColor; } }
    }

    // 人口面板 VM(全国视角; 文档 19.14.2)
    public class PopPanelVM : PanelVMBase
    {
        private readonly Action _onClose;
        private string _total = "", _sol = "", _work = "", _lit = "", _tip = "";
        internal int Scroll;
        private float _maxScroll;

        public PopPanelVM(Action onClose)
        {
            _onClose = onClose;
            Rows = new MBBindingList<PopRowVM>();
            Strata = new MBBindingList<PopStrataVM>();
            Refresh();
        }

        public MBBindingList<PopRowVM> Rows { get; private set; }
        public MBBindingList<PopStrataVM> Strata { get; private set; }

        [DataSourceProperty] public string TotalText { get { return _total; } }
        [DataSourceProperty] public string SolText { get { return _sol; } }
        [DataSourceProperty] public string WorkText { get { return _work; } }
        [DataSourceProperty] public string LitText { get { return _lit; } }
        [DataSourceProperty] public string TipText { get { return _tip; } }

        public void ExecuteClose() { if (_onClose != null) _onClose(); }

        internal void ScrollStep(int dir)
        {
            try
            {
                int next = Scroll + dir;
                if (next < 0) next = 0;
                int max = (int)_maxScroll;
                if (next > max) next = max;
                if (next == Scroll) return;
                Scroll = next;
                Refresh();
            }
            catch { }
        }

        private int VisibleRows()
        {
            int n = 12;
            try
            {
                float h = TaleWorlds.Engine.Screen.RealScreenResolutionHeight;
                if (h <= 100f) h = 1080f;
                n = (int)((h - 548f) / 40f);   // 412 顶部 + 28 表头 + 26 表 + 82 底部
            }
            catch { }
            return n < 4 ? 4 : n;
        }

        internal void Refresh()
        {
            try
            {
                Rows.Clear();
                Strata.Clear();

                // ---- 汇总 ----
                float total = 0f, solNum = 0f, employed = 0f, workforce = 0f, litNum = 0f;
                // 按阶级 与 按职业 聚合
                var st = new float[3]; var stSol = new float[3]; var stIncome = new float[3]; var stNeed = new float[3];
                var prof = new Dictionary<string, float[]>();   // profession -> [人数, SoL加权, 需求满足加权]
                foreach (var kv in Pops.BySettlement)
                {
                    var l = kv.Value;
                    if (l == null) continue;
                    for (int i = 0; i < l.Count; i++)
                    {
                        var p = l[i];
                        if (p == null || p.Size < 0.5f) continue;
                        int s = PopDefs.StratumOf(p.Profession);
                        float w = p.Size;
                        total += w; solNum += p.WealthLevel * w; litNum += p.Literacy * w;
                        workforce += p.Workforce;
                        float inc = PopJob.IncomeOf(p);
                        if (inc > 0.0001f) employed += p.Workforce;
                        float incAll = PopSim.TotalIncomeOf(p);
                        st[s] += w; stSol[s] += p.WealthLevel * w;
                        stIncome[s] += incAll;
                        stNeed[s] += (1f - p.NeedShortfall) * w;
                        float[] a;
                        if (!prof.TryGetValue(p.Profession, out a)) { a = new float[3]; prof[p.Profession] = a; }
                        a[0] += w; a[1] += p.WealthLevel * w; a[2] += (1f - p.NeedShortfall) * w;
                    }
                }
                if (total <= 0f) { _total = "暂无人口数据"; return; }
                float avgSol = solNum / total;
                _total = "全国人口 " + ((int)total).ToString("N0");
                _sol = "平均生活水平 " + avgSol.ToString("F1") + " " + PopStrataVM.SolLabel(avgSol);
                _work = "在岗 " + ((int)employed).ToString("N0") + " / 失业 " + ((int)Math.Max(0f, workforce - employed)).ToString("N0");
                if (!PopJob.HasRun) _work = "尚未结算: 走过一个游戏日后显示就业";   // 读档后未跳日时避免误读为"全员失业"
                _lit = "识字率 " + (int)Math.Round(litNum / total * 100f) + "%";
                _tip = BuildTip();

                OnPropertyChangedWithValue(_total, "TotalText");
                OnPropertyChangedWithValue(_sol, "SolText");
                OnPropertyChangedWithValue(_work, "WorkText");
                OnPropertyChangedWithValue(_lit, "LitText");
                OnPropertyChangedWithValue(_tip, "TipText");

                // ---- 三张阶级卡 ----
                for (int s = 2; s >= 0; s--)
                {
                    if (st[s] <= 0.5f) continue;
                    float solS = stSol[s] / st[s];
                    float incPer = stIncome[s] > 0f ? stIncome[s] * 7f / Math.Max(1f, st[s] * PopDefs.WorkforceRatio) : 0f;
                    float needS = stNeed[s] / st[s];
                    // 盈亏: 收入 vs 该阶级平均买包成本
                    float cost = NeedCostOf(s, (int)Math.Max(1f, solS)) * (st[s] * PopDefs.WorkforceRatio + st[s] * (1f - PopDefs.WorkforceRatio) * PopDefs.DependentRatio) / 10000f;
                    float surplus = cost > 0.01f ? (stIncome[s] * 7f - cost) / cost : 0f;
                    Strata.Add(new PopStrataVM(PopDefs.StratumName(s) + "阶级", st[s], solS, incPer, needS, surplus, PopJob.HasRun));
                }

                // ---- 职业清单(按人口排序) ----
                var list = new List<KeyValuePair<string, float[]>>(prof);
                list.Sort(delegate (KeyValuePair<string, float[]> a, KeyValuePair<string, float[]> b) { return b.Value[0].CompareTo(a.Value[0]); });
                _maxScroll = Math.Max(0, list.Count - VisibleRows());
                if (Scroll > _maxScroll) Scroll = (int)_maxScroll;
                int shown = 0;
                for (int i = Scroll; i < list.Count && shown < VisibleRows(); i++, shown++)
                {
                    var k = list[i];
                    float n = k.Value[0];
                    var def = PopDefs.Get(k.Key);
                    Rows.Add(new PopRowVM(i, def.Name, n, k.Value[1] / n, k.Value[2] / n, IconOf(k.Key), def.Stratum));
                }
            }
            catch (Exception ex) { DLog.Force("人口面板刷新失败: " + ex.Message); }
        }

        // 最缺需求(满足率最低的 3 项) + 上周迁移(文档 19.14.2 的"需求满足条/迁移近况"轻量版)
        private static string BuildTip()
        {
            try
            {
                var sb = new System.Text.StringBuilder("最缺: ");
                var list = new List<KeyValuePair<string, float>>(PopSim.LastNeedSat);
                list.Sort(delegate (KeyValuePair<string, float> a, KeyValuePair<string, float> b) { return a.Value.CompareTo(b.Value); });
                int n = 0;
                for (int i = 0; i < list.Count && n < 3; i++, n++)
                {
                    if (n > 0) sb.Append(" · ");
                    sb.Append(PopNeeds.NameOf(list[i].Key)).Append(' ').Append((int)Math.Round(list[i].Value * 100f)).Append('%');
                }
                if (n == 0) sb.Append("—");
                sb.Append(" · 上周迁移 ").Append(PopSim.LastMigration >= 0 ? "+" : "").Append(PopSim.LastMigration);
                return sb.ToString();
            }
            catch { return ""; }
        }

        private static float NeedCostOf(int stratum, int level)
        {
            float t = 0f;
            try
            {
                for (int i = 0; i < PopNeeds.All.Count; i++) t += PopNeeds.PackageValue(PopNeeds.All[i], level);
            }
            catch { }
            return t;
        }

        private static string IconOf(string professionId)
        {
            switch (professionId)
            {
                case PopDefs.Peasants:
                case PopDefs.Farmers: return "fia_cat_resource";
                case PopDefs.Laborers:
                case PopDefs.Miners:
                case PopDefs.Machinists:
                case PopDefs.Engineers: return "fia_cat_industry";
                case PopDefs.Clerks:
                case PopDefs.Shopkeepers:
                case PopDefs.Aristocrats:
                case PopDefs.Capitalists: return "fia_cat_trade";
                case PopDefs.Bureaucrats:
                case PopDefs.Academics: return "fia_cat_admin";
                case PopDefs.Officers:
                case PopDefs.Soldiers: return "fia_cat_military";
                case PopDefs.Clergymen: return "fia_cat_living";
                default: return "fia_cat_admin";
            }
        }
    }
}
