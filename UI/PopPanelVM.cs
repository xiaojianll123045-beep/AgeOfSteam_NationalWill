using System;
using System.Collections.Generic;
using TaleWorlds.Core;
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
        private readonly string _lit, _stance, _stanceColor, _power;
        private readonly int _idx;

        internal PopRowVM(int idx, string name, float people, float sol, float needSat, string icon, int stratum,
                          float literacy, float radical, float loyalty, float power)
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
            // v4.147 V3 Pops 视角: 识字率 / 忠诚-激进立场 / 政治力量
            _lit = (int)Math.Round(literacy * 100f) + "%";
            _stance = "激 " + (int)Math.Round(radical * 100f) + "% · 忠 " + (int)Math.Round(loyalty * 100f) + "%";
            _stanceColor = radical > 0.30f ? "#D96A5AFF" : (loyalty > 0.30f ? "#39FF14FF" : "#C8B98FFF");
            _power = power.ToString("F1");
        }

        [DataSourceProperty] public string Name { get { return _name; } }
        [DataSourceProperty] public string Pops { get { return _pops; } }
        [DataSourceProperty] public string Sol { get { return _sol; } }
        [DataSourceProperty] public string SolColor { get { return _solColor; } }
        [DataSourceProperty] public string Need { get { return _need; } }
        [DataSourceProperty] public string NeedColor { get { return _needColor; } }
        [DataSourceProperty] public string Icon { get { return _icon; } }
        [DataSourceProperty] public string StratumColor { get { return _stratumColor; } }
        [DataSourceProperty] public string Lit { get { return _lit; } }
        [DataSourceProperty] public string Stance { get { return _stance; } }
        [DataSourceProperty] public string StanceColor { get { return _stanceColor; } }
        [DataSourceProperty] public string Power { get { return _power; } }
    }

    // 文化行(v4.147: V3 Pops 的文化视角 + 同化)
    public class PopCultureVM : ViewModel
    {
        private readonly string _name, _pops, _share, _accept, _acceptColor, _assim, _icon;

        internal PopCultureVM(string name, float people, float share, string accept, string acceptColor, string assim, string icon)
        {
            _name = name;
            _pops = ((int)people).ToString("N0");
            _share = (share * 100f).ToString("F1") + "%";
            _accept = accept;
            _acceptColor = acceptColor;
            _assim = assim;
            _icon = icon;
        }

        [DataSourceProperty] public string Name { get { return _name; } }
        [DataSourceProperty] public string Pops { get { return _pops; } }
        [DataSourceProperty] public string Share { get { return _share; } }
        [DataSourceProperty] public string Accept { get { return _accept; } }
        [DataSourceProperty] public string AcceptColor { get { return _acceptColor; } }
        [DataSourceProperty] public string Assim { get { return _assim; } }
        [DataSourceProperty] public string Icon { get { return _icon; } }
    }

    // 人口面板 VM(全国视角; 文档 19.14.2; v4.147 全屏 + V3 Pops 视角: 立场/识字率/政治力量/文化同化)
    public class PopPanelVM : PanelVMBase
    {
        private readonly Action _onClose;
        private string _total = "", _sol = "", _work = "", _lit = "", _tip = "";
        private string _dep = "", _power = "", _stance = "", _stanceColor = "#C8B98FFF";
        internal int Scroll;
        private float _maxScroll;
        private readonly List<string> _profKeys = new List<string>();       // 显示顺序(热区索引)
        private readonly List<string> _cultureKeys = new List<string>();
        private readonly List<float[]> _cultureAgg = new List<float[]>();   // [人数, 激进加权, 忠诚加权]

        public PopPanelVM(Action onClose)
        {
            _onClose = onClose;
            Rows = new MBBindingList<PopRowVM>();
            Strata = new MBBindingList<PopStrataVM>();
            CultureRows = new MBBindingList<PopCultureVM>();
            Refresh();
        }

        public MBBindingList<PopRowVM> Rows { get; private set; }
        public MBBindingList<PopStrataVM> Strata { get; private set; }
        public MBBindingList<PopCultureVM> CultureRows { get; private set; }

        [DataSourceProperty] public string TotalText { get { return _total; } }
        [DataSourceProperty] public string SolText { get { return _sol; } }
        [DataSourceProperty] public string WorkText { get { return _work; } }
        [DataSourceProperty] public string LitText { get { return _lit; } }
        [DataSourceProperty] public string TipText { get { return _tip; } }
        [DataSourceProperty] public string DepText { get { return _dep; } }
        [DataSourceProperty] public string PowerText { get { return _power; } }
        [DataSourceProperty] public string StanceText { get { return _stance; } }
        [DataSourceProperty] public string StanceColor { get { return _stanceColor; } }

        // ---- 全屏布局(v4.147: 同政治页; PanelOffset 在 PanelVMBase 基类) ----
        [DataSourceProperty]
        public float PanelWidth
        {
            get { try { return PanelScreen.FullWidth(); } catch { return 1856f; } }
        }
        [DataSourceProperty]
        public float ContentPad
        {
            get { try { return Math.Max(0f, (PanelScreen.FullWidth() - 1700f) / 2f); } catch { return 0f; } }
        }

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
                CultureRows.Clear();
                _profKeys.Clear();
                _cultureKeys.Clear();
                _cultureAgg.Clear();

                // ---- 汇总 ----
                float total = 0f, solNum = 0f, employed = 0f, workforce = 0f, litNum = 0f;
                float loyNum = 0f, radNum = 0f, powerSum = 0f;
                var st = new float[3]; var stSol = new float[3]; var stIncome = new float[3]; var stNeed = new float[3];
                var prof = new Dictionary<string, float[]>();   // profession -> [人数, SoL加权, 需求满足加权, 识字率加权, 激进加权, 忠诚加权, 政治力量]
                var cult = new Dictionary<string, float[]>();   // culture -> [人数, 激进加权, 忠诚加权]
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
                        loyNum += p.Loyalty * w; radNum += p.Radicalism * w;
                        powerSum += Pops.PoliticalStrengthOf(p) * w;
                        float inc = PopJob.IncomeOf(p);
                        if (inc > 0.0001f) employed += p.Workforce;
                        float incAll = PopSim.TotalIncomeOf(p);
                        st[s] += w; stSol[s] += p.WealthLevel * w;
                        stIncome[s] += incAll;
                        stNeed[s] += (1f - p.NeedShortfall) * w;
                        float[] a;
                        if (!prof.TryGetValue(p.Profession, out a)) { a = new float[7]; prof[p.Profession] = a; }
                        a[0] += w; a[1] += p.WealthLevel * w; a[2] += (1f - p.NeedShortfall) * w;
                        a[3] += p.Literacy * w; a[4] += p.Radicalism * w; a[5] += p.Loyalty * w;
                        a[6] += Pops.PoliticalStrengthOf(p) * w;
                        float[] c;
                        if (!cult.TryGetValue(p.Culture ?? "?", out c)) { c = new float[3]; cult[p.Culture ?? "?"] = c; }
                        c[0] += w; c[1] += p.Radicalism * w; c[2] += p.Loyalty * w;
                    }
                }
                if (total <= 0f) { _total = "暂无人口数据"; return; }
                float avgSol = solNum / total;
                _total = "全国人口 " + ((int)total).ToString("N0");
                _sol = "生活水平 " + avgSol.ToString("F1") + " " + PopStrataVM.SolLabel(avgSol);
                _work = "在岗 " + ((int)employed).ToString("N0") + " / 失业 " + ((int)Math.Max(0f, workforce - employed)).ToString("N0");
                if (!PopJob.HasRun) _work = "尚未结算: 走过一个游戏日后显示就业";   // 读档后未跳日时避免误读为"全员失业"
                _lit = "识字率 " + (int)Math.Round(litNum / total * 100f) + "%";
                _dep = "劳动力 " + ((int)workforce).ToString("N0") + " · 受抚养者 " + ((int)(total - workforce)).ToString("N0")
                     + " (" + (int)Math.Round(PopDefs.WorkforceRatio * 100f) + "% / " + (100 - (int)Math.Round(PopDefs.WorkforceRatio * 100f)) + "%)";
                _power = "政治力量 " + powerSum.ToString("N0");
                float lp = loyNum / total * 100f, rp = radNum / total * 100f, np = Math.Max(0f, 100f - lp - rp);
                _stance = "忠诚 " + (int)Math.Round(lp) + "% · 中立 " + (int)Math.Round(np) + "% · 激进 " + (int)Math.Round(rp) + "%";
                _stanceColor = rp > 25f ? "#D96A5AFF" : (lp > 25f ? "#39FF14FF" : "#C8B98FFF");
                _tip = BuildTip();

                OnPropertyChangedWithValue(_total, "TotalText");
                OnPropertyChangedWithValue(_sol, "SolText");
                OnPropertyChangedWithValue(_work, "WorkText");
                OnPropertyChangedWithValue(_lit, "LitText");
                OnPropertyChangedWithValue(_tip, "TipText");
                OnPropertyChangedWithValue(_dep, "DepText");
                OnPropertyChangedWithValue(_power, "PowerText");
                OnPropertyChangedWithValue(_stance, "StanceText");
                OnPropertyChangedWithValue(_stanceColor, "StanceColor");

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
                for (int i = 0; i < list.Count; i++) _profKeys.Add(list[i].Key);
                int shown = 0;
                for (int i = Scroll; i < list.Count && shown < VisibleRows(); i++, shown++)
                {
                    var k = list[i];
                    float n = k.Value[0];
                    var def = PopDefs.Get(k.Key);
                    Rows.Add(new PopRowVM(i, def.Name, n, k.Value[1] / n, k.Value[2] / n, IconOf(k.Key), def.Stratum,
                        k.Value[3] / n, k.Value[4] / n, k.Value[5] / n, k.Value[6] / n));
                }

                // ---- 文化列表(按人口排序; 接纳状态 + 同化) ----
                var clist = new List<KeyValuePair<string, float[]>>(cult);
                clist.Sort(delegate (KeyValuePair<string, float[]> a, KeyValuePair<string, float[]> b) { return b.Value[0].CompareTo(a.Value[0]); });
                string dom = clist.Count > 0 ? clist[0].Key : null;
                float policyMult = CultureMult();
                for (int i = 0; i < clist.Count && i < 8; i++)
                {
                    var k = clist[i];
                    _cultureKeys.Add(k.Key);
                    _cultureAgg.Add(k.Value);
                    bool isDom = k.Key == dom;
                    string accept = isDom ? "主导文化" : (policyMult >= 1.4f ? "同化中" : (policyMult >= 0.85f ? "隔离" : "受歧视"));
                    string ac = isDom ? "#E8C33AFF" : (policyMult >= 1.4f ? "#39FF14FF" : (policyMult >= 0.85f ? "#C8B98FFF" : "#D96A5AFF"));
                    string assim = isDom ? "—" : "同化 " + (policyMult * (1f + 0.125f * EduLevel()) * 0.2f).ToString("F2") + "%/月";
                    CultureRows.Add(new PopCultureVM(CultureName(k.Key), k.Value[0], k.Value[0] / total, accept, ac, assim, "fia_cat_admin"));
                }
            }
            catch (Exception ex) { DLog.Force("人口面板刷新失败: " + ex.Message); }
        }

        // v4.147: 文化接纳政策乘数(与 Pops.AssimilateWeekly 保持一致)
        private static float CultureMult()
        {
            try
            {
                int cp = Institutions.CulturePolicy;
                if (cp == 0) return 0.65f;
                if (cp == 1) return 0.9f;
                if (cp == 2) return 1.5f;
                return 1f;
            }
            catch { return 1f; }
        }

        private static int EduLevel()
        {
            try { return Institutions.Level[0]; } catch { return 0; }
        }

        internal static string CultureName(string id)
        {
            switch (id)
            {
                case "empire": return "帝国";
                case "vlandia": return "瓦兰迪亚";
                case "sturgia": return "斯特吉亚";
                case "aserai": return "阿塞莱";
                case "khuzait": return "库塞特";
                case "battania": return "巴旦尼亚";
                case "nord": return "诺德";
                default: return id ?? "?";
            }
        }

        // 职业详情弹窗(V3 Pops 详情: SoL/教育机会/预期 SoL/资格/立场/政治力量)
        internal void ProfessionAction(int rowIdx)
        {
            try
            {
                int idx = Scroll + rowIdx;
                if (idx < 0 || idx >= _profKeys.Count) return;
                string pid = _profKeys[idx];
                var def = PopDefs.Get(pid);
                float size = 0f, solN = 0f, litN = 0f, radN = 0f, loyN = 0f, powN = 0f, needN = 0f;
                float eduN = 0f, expN = 0f, qualN = 0f;
                foreach (var kv in Pops.BySettlement)
                {
                    var l = kv.Value;
                    if (l == null) continue;
                    for (int i = 0; i < l.Count; i++)
                    {
                        var p = l[i];
                        if (p == null || p.Profession != pid || p.Size < 0.5f) continue;
                        float w = p.Size;
                        size += w; solN += p.WealthLevel * w; litN += p.Literacy * w;
                        radN += p.Radicalism * w; loyN += p.Loyalty * w;
                        powN += Pops.PoliticalStrengthOf(p) * w;
                        needN += (1f - p.NeedShortfall) * w;
                        eduN += Pops.EducationAccessOf(p) * w;
                        expN += Pops.ExpectedSolOf(p) * w;
                        qualN += Pops.QualificationOf(p) * w;
                    }
                }
                if (size <= 0f) return;
                string body = "阶层: " + PopDefs.StratumName(def.Stratum) + "\n"
                    + "人口: " + ((int)size).ToString("N0") + " (劳动力 " + ((int)(size * PopDefs.WorkforceRatio)).ToString("N0")
                    + " · 受抚养者 " + ((int)(size * (1f - PopDefs.WorkforceRatio))).ToString("N0") + ")\n\n"
                    + "生活水平: " + (solN / size).ToString("F1") + " / 预期 " + (expN / size).ToString("F1") + " " + PopStrataVM.SolLabel(solN / size) + "\n"
                    + "识字率: " + (int)Math.Round(litN / size * 100f) + "% · 教育机会 " + (eduN / size * 100f).ToString("F1") + "%\n"
                    + "资格(流动准备): " + (qualN / size).ToString("F2") + "\n"
                    + "需求满足: " + (int)Math.Round(needN / size * 100f) + "%\n\n"
                    + "立场: 忠诚 " + (int)Math.Round(loyN / size * 100f) + "% · 激进 " + (int)Math.Round(radN / size * 100f) + "%\n"
                    + "政治力量: " + powN.ToString("F0") + "\n\n"
                    + "工资系数 ×" + def.WageFactor.ToString("F1") + " · 投资贡献 " + (int)(def.InvestShare * 100f) + "%";
                PanelInputGuard.ShowPopup(new MultiSelectionInquiryData(
                    def.Name, body,
                    new List<InquiryElement> { new InquiryElement("ok", "知道了", null, true, null) },
                    true, 1, 1, "确定", "取消", null, null, null, false));
            }
            catch (Exception ex) { DLog.Force("职业详情失败: " + ex.Message); }
        }

        // 文化详情弹窗(V3: 接纳状态 + 同化)
        internal void CultureAction(int idx)
        {
            try
            {
                if (idx < 0 || idx >= _cultureKeys.Count) return;
                string cid = _cultureKeys[idx];
                var v = _cultureAgg[idx];
                float size = v[0];
                string body = "人口: " + ((int)size).ToString("N0") + "\n"
                    + "立场: 激进 " + (int)Math.Round(v[1] / size * 100f) + "% · 忠诚 " + (int)Math.Round(v[2] / size * 100f) + "%\n\n"
                    + "文化接纳政策: " + CulturePolicyName() + "\n"
                    + (idx == 0 ? "该文化是本国主导文化(不被同化)" : ("同化速率: " + (CultureMult() * (1f + 0.125f * EduLevel()) * 0.2f).ToString("F2") + "%/月 (基础 0.2%)")) + "\n\n"
                    + "同化受文化接纳政策与教育机构等级影响; 激进者同化更慢(经典)。";
                PanelInputGuard.ShowPopup(new MultiSelectionInquiryData(
                    CultureName(cid), body,
                    new List<InquiryElement> { new InquiryElement("ok", "知道了", null, true, null) },
                    true, 1, 1, "确定", "取消", null, null, null, false));
            }
            catch (Exception ex) { DLog.Force("文化详情失败: " + ex.Message); }
        }

        private static string CulturePolicyName()
        {
            try { return Institutions.CultureStatus(); } catch { return "—"; }
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
