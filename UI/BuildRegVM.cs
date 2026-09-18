using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;

namespace FeudalInternalAffairs
{
    // 建筑总表的一行(文档 19.14 / V3 建筑窗口列结构: 名称 规模 类型 人均年产值)
    public class BuildRegRowVM : ViewModel
    {
        private readonly string _name, _icon, _count, _cat, _value, _fill, _fillColor, _cash, _owner;
        internal readonly string DefId, Sid;

        internal BuildRegRowVM(string name, string icon, int count, string cat, float valuePerWorker, float fillAvg, float cashSum,
            string owner, string defId, string sid)
        {
            _name = name;
            _icon = icon;
            _count = "×" + count;
            _cat = cat;
            _value = valuePerWorker.ToString("F0");
            _fill = (int)Math.Round(fillAvg * 100f) + "%";
            _fillColor = fillAvg < 0.6f ? "#E8C33AFF" : (fillAvg < 0.95f ? "#C8B98FFF" : "#39FF14FF");
            _cash = cashSum >= 0f ? "储 " + ((int)(cashSum / 1000f)).ToString("N0") + "K" : "亏 " + ((int)(-cashSum / 1000f)).ToString("N0") + "K";
            _owner = owner;
            DefId = defId;
            Sid = sid;
        }

        [DataSourceProperty] public string Name { get { return _name; } }
        [DataSourceProperty] public string Icon { get { return _icon; } }
        [DataSourceProperty] public string Count { get { return _count; } }
        [DataSourceProperty] public string Category { get { return _cat; } }
        [DataSourceProperty] public string Value { get { return _value; } }
        [DataSourceProperty] public string Fill { get { return _fill; } }
        [DataSourceProperty] public string FillColor { get { return _fillColor; } }
        [DataSourceProperty] public string Cash { get { return _cash; } }
        [DataSourceProperty] public string Owner { get { return _owner; } }
    }

    // 建筑总表 VM(全图汇总; 筛选: 位置 + 分类)
    public class BuildRegVM : PanelVMBase
    {
        private readonly Action _onClose;
        private int _loc;          // 0=全部 1=城镇 2=城堡 3=村庄
        private string _cat;       // null = 全部
        internal int Scroll;

        public BuildRegVM(Action onClose)
        {
            _onClose = onClose;
            Rows = new MBBindingList<BuildRegRowVM>();
            Refresh();
        }

        public MBBindingList<BuildRegRowVM> Rows { get; private set; }
        internal int ShownCount;   // 当前可见行数(建筑详情热区用)
        internal BuildRegRowVM RowAt(int i) { return i >= 0 && i < Rows.Count ? Rows[i] : null; }

        private string _subtitle = "", _filterText = "";
        [DataSourceProperty] public string Subtitle { get { return _subtitle; } }
        [DataSourceProperty] public string FilterText { get { return _filterText; } }

        public void ExecuteClose() { if (_onClose != null) _onClose(); }

        internal void ExecuteLocAll() { _loc = 0; Refresh(); }
        internal void ExecuteLocTown() { _loc = 1; Refresh(); }
        internal void ExecuteLocCastle() { _loc = 2; Refresh(); }
        internal void ExecuteLocVillage() { _loc = 3; Refresh(); }
        internal void ExecuteCatAll() { _cat = null; Refresh(); }
        internal void ExecuteCat(string c) { _cat = c; Refresh(); }

        internal void ScrollStep(int dir)
        {
            try
            {
                int next = Scroll + dir;
                if (next < 0) next = 0;
                int max = Math.Max(0, Rows.Count - VisibleRows());
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
                n = (int)((h - 470f) / 40f);   // 顶部 250 + 表头 28 + 底部 82 + 余量
            }
            catch { }
            return n < 4 ? 4 : n;
        }

        internal void Refresh()
        {
            try
            {
                Rows.Clear();
                // 汇总: DefId -> [count, fillSum, cashSum]
                var agg = new Dictionary<string, float[]>();
                var ownerAgg = new Dictionary<string, int[]>();
                var firstSid = new Dictionary<string, string>();
                int queued = 0;
                foreach (var kv in EconomyWorld.Buildings)
                {
                    var sb = kv.Value;
                    if (sb == null) continue;
                    var s = FindSettlement(kv.Key);
                    if (s == null) continue;
                    if (_loc == 1 && !s.IsTown) continue;
                    if (_loc == 2 && !s.IsCastle) continue;
                    if (_loc == 3 && !s.IsVillage) continue;
                    queued += sb.Queue.Count;
                    for (int i = 0; i < sb.Groups.Count; i++)
                    {
                        var g = sb.Groups[i];
                        if (g == null || g.Count <= 0) continue;
                        var def = BuildDefs.Get(g.DefId);
                        if (def == null) continue;
                        if (_cat != null && BuildDefs.CategoryName(def.Cat) != _cat) continue;
                        float[] a;
                        if (!agg.TryGetValue(g.DefId, out a)) { a = new float[3]; agg[g.DefId] = a; }
                        a[0] += g.Count;
                        a[1] += g.Fill * g.Count;
                        a[2] += g.Cash;
                        // v4.0: 所有者聚合(文档 20.4) + 详情入口的样本定居点
                        int[] ow;
                        if (!ownerAgg.TryGetValue(g.DefId, out ow)) { ow = new int[5]; ownerAgg[g.DefId] = ow; }
                        ow[Ownership.OwnerOf(g, s)] += g.Count;
                        if (!firstSid.ContainsKey(g.DefId)) firstSid[g.DefId] = kv.Key;
                    }
                }

                var list = new List<KeyValuePair<string, float[]>>(agg);
                list.Sort(delegate (KeyValuePair<string, float[]> a, KeyValuePair<string, float[]> b) { return b.Value[0].CompareTo(a.Value[0]); });
                _subtitle = ((int)TotalCount(agg)).ToString("N0") + " 座建筑" + (queued > 0 ? " · " + queued + " 处于建造中" : "");
                _filterText = "位置: " + LocName(_loc) + "   分类: " + (_cat ?? "全部") + "   共 " + list.Count + " 种";

                int shown = 0;
                for (int i = Scroll; i < list.Count && shown < VisibleRows(); i++, shown++)
                {
                    var def = BuildDefs.Get(list[i].Key);
                    var a = list[i].Value;
                    float fill = a[0] > 0.01f ? a[1] / a[0] : 0f;
                    string ownerText = "—";
                    int[] ow;
                    if (ownerAgg.TryGetValue(list[i].Key, out ow))
                    {
                        int best = 0;
                        for (int j = 1; j < 5; j++) if (ow[j] > ow[best]) best = j;
                        ownerText = Ownership.NameOf(best);
                        int kinds = 0;
                        for (int j = 0; j < 5; j++) if (ow[j] > 0) kinds++;
                        if (kinds > 1) ownerText = "主" + ownerText;
                    }
                    string fsid;
                    firstSid.TryGetValue(list[i].Key, out fsid);
                    Rows.Add(new BuildRegRowVM(def.Name, def.Sprite, (int)a[0], BuildDefs.CategoryName(def.Cat),
                        ValuePerWorker(def), fill, a[2], ownerText, list[i].Key, fsid));
                }
                ShownCount = shown;
            }
            catch (Exception ex) { DLog.Force("建筑总表刷新失败: " + ex.Message); }
        }

        private static float TotalCount(Dictionary<string, float[]> agg)
        {
            float t = 0f;
            foreach (var kv in agg) { var a = kv.Value; float baseC = a[0]; t += baseC; }
            return t;
        }

        private static string LocName(int loc)
        {
            return loc == 1 ? "城镇" : (loc == 2 ? "城堡" : (loc == 3 ? "村庄" : "全部"));
        }

        // 人均年产值 = 年产出值(按木档×当前市场价) / 岗位数
        private static float ValuePerWorker(BuildDef def)
        {
            try
            {
                float annual = 0f;
                for (int i = 0; i < def.Outputs.Count; i++)
                {
                    var o = def.Outputs[i];
                    if (o == null || string.IsNullOrEmpty(o.Good) || o.Good.StartsWith("@")) continue;
                    annual += o.Value(BuildMode.Wood) * PriceOf(o.Good) * 365f;
                }
                var spec = PopJob.SpecOf(def.Id);
                float jobs = 1f;
                if (spec != null)
                {
                    jobs = 0f;
                    for (int i = 0; i < spec.Num.Length; i++) jobs += spec.Num[i];
                }
                return jobs > 0.5f ? annual / jobs : 0f;
            }
            catch { return 0f; }
        }

        private static float PriceOf(string goodId)
        {
            try { return EconomyWorld.National.PriceOf(goodId); } catch { return 0f; }
        }

        private static Settlement FindSettlement(string sid)
        {
            try
            {
                foreach (var s in Settlement.All) if (s != null && s.StringId == sid) return s;
            }
            catch { }
            return null;
        }
    }
}
