using System;
using System.Collections.Generic;
using TaleWorlds.Library;

namespace FeudalInternalAffairs
{
    // 文化行(人口/占比/生活水平/需求/阶级构成)
    public class CultureRowVM : ViewModel
    {
        private readonly string _name, _pops, _share, _sol, _solColor, _need, _needColor, _classes, _loyalty;

        internal CultureRowVM(string name, float people, float total, float sol, float needSat, float[] strata, float loyalty)
        {
            _name = name;
            _pops = ((int)people).ToString("N0");
            _share = total > 0.5f ? (people / total * 100f).ToString("F1") + "%" : "-";
            _sol = sol.ToString("F1") + " " + PopStrataVM.SolLabel(sol);
            _solColor = PopStrataVM.ColorOfSol(sol);
            _need = (int)Math.Round(needSat * 100f) + "%";
            _needColor = needSat >= 0.9f ? "#39FF14FF" : (needSat >= 0.7f ? "#E8C33AFF" : "#D96A5AFF");
            _loyalty = "忠诚 " + (int)Math.Round(loyalty * 100f) + "%";
            // 阶级构成: 上层/中层/下层 占比 (strata 传入顺序 = [上, 中, 下])
            float sUp = strata != null ? strata[0] : 0f;
            float sMid = strata != null ? strata[1] : 0f;
            float sLow = strata != null ? strata[2] : 0f;
            float sum = Math.Max(1f, sUp + sMid + sLow);
            _classes = "上" + (int)Math.Round(sUp / sum * 100f) + "% 中" + (int)Math.Round(sMid / sum * 100f)
                     + "% 下" + (int)Math.Round(sLow / sum * 100f) + "%";
        }

        [DataSourceProperty] public string Name { get { return _name; } }
        [DataSourceProperty] public string Pops { get { return _pops; } }
        [DataSourceProperty] public string Share { get { return _share; } }
        [DataSourceProperty] public string Sol { get { return _sol; } }
        [DataSourceProperty] public string SolColor { get { return _solColor; } }
        [DataSourceProperty] public string Need { get { return _need; } }
        [DataSourceProperty] public string NeedColor { get { return _needColor; } }
        [DataSourceProperty] public string Classes { get { return _classes; } }
        [DataSourceProperty] public string Loyalty { get { return _loyalty; } }
    }

    // 社会页 VM(文化维度; 文档 19.14 / 参照 V3 社会窗口)
    public class SocietyVM : PanelVMBase
    {
        private readonly Action _onClose;
        private string _summary = "";

        public SocietyVM(Action onClose)
        {
            _onClose = onClose;
            Rows = new MBBindingList<CultureRowVM>();
            Refresh();
        }

        public MBBindingList<CultureRowVM> Rows { get; private set; }

        [DataSourceProperty] public string Summary { get { return _summary; } }

        public void ExecuteClose() { if (_onClose != null) _onClose(); }

        internal void Refresh()
        {
            try
            {
                Rows.Clear();
                var agg = new Dictionary<string, float[]>();   // culture -> [人数, SoL加权, 需求加权, 忠诚加权, 上层, 中层, 下层]
                float total = 0f;
                foreach (var kv in Pops.BySettlement)
                {
                    var l = kv.Value;
                    if (l == null) continue;
                    for (int i = 0; i < l.Count; i++)
                    {
                        var p = l[i];
                        if (p == null || p.Size < 0.5f) continue;
                        float[] a;
                        if (!agg.TryGetValue(p.Culture ?? "?", out a)) { a = new float[8]; agg[p.Culture ?? "?"] = a; }
                        a[0] += p.Size;
                        a[1] += p.WealthLevel * p.Size;
                        a[2] += (1f - p.NeedShortfall) * p.Size;
                        a[3] += p.Loyalty * p.Size;
                        a[7] += p.Radicalism * p.Size;
                        int st = PopDefs.StratumOf(p.Profession);
                        a[4 + st] += p.Size;
                        total += p.Size;
                    }
                }

                var list = new List<KeyValuePair<string, float[]>>(agg);
                list.Sort(delegate (KeyValuePair<string, float[]> a, KeyValuePair<string, float[]> b) { return b.Value[0].CompareTo(a.Value[0]); });
                for (int i = 0; i < list.Count; i++)
                {
                    var k = list[i];
                    var a = k.Value;
                    float n = Math.Max(1f, a[0]);
                    var strata = new[] { a[6], a[5], a[4] };
                    Rows.Add(new CultureRowVM(CultureName(k.Key), a[0], total, a[1] / n, a[2] / n, strata, a[3] / n));
                }
                float radNum = 0f;
                for (int i = 0; i < list.Count; i++) radNum += list[i].Value[7];
                _summary = "文化 " + list.Count + " 种 · 总人口 " + ((int)total).ToString("N0")
                    + " · 接纳度未实现, 仅按人口与生活水平统计 · 平均激进 "
                    + (total > 0.5f ? (int)Math.Round(radNum / total * 100f) : 0) + "%";
                OnPropertyChangedWithValue(_summary, "Summary");
            }
            catch (Exception ex) { DLog.Force("社会页刷新失败: " + ex.Message); }
        }

        private static string CultureName(string id)
        {
            try
            {
                var c = TaleWorlds.ObjectSystem.MBObjectManager.Instance.GetObject<TaleWorlds.CampaignSystem.CultureObject>(id);
                if (c != null && !string.IsNullOrEmpty(c.Name != null ? c.Name.ToString() : null)) return c.Name.ToString();
            }
            catch { }
            switch (id)
            {
                case "empire": return "帝国";
                case "vlandia": return "瓦兰迪亚";
                case "battania": return "巴旦尼亚";
                case "sturgia": return "斯特吉亚";
                case "khuzait": return "库塞特";
                case "aserai": return "阿塞莱";
                default: return id;
            }
        }
    }
}
