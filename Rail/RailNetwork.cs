using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Map;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Engine;
using TaleWorlds.Library;

namespace FeudalInternalAffairs
{
    /// <summary>
    /// 铁路网（重构版）
    /// 站点 = 城镇中心；拓扑 = k 近邻候选 + 最小生成树；
    /// 选线 = 航点搜索（地形坡度/水/城镇避让/偏离惩罚/曲率惩罚）+ 走廊缓动合轨 + 二项式圆化。
    /// </summary>
    public class RailNetwork
    {
        public class Line
        {
            public string FromId;
            public string ToId;
            public List<Vec2> Points = new List<Vec2>();
            public float Length;
            public float[] RailZ;
            public float[] TerrainZ;
            public Vec2 FromPos;
            public Vec2 ToPos;
        }

        private class Station
        {
            public string Id;
            public Vec2 Pos;
            }

        private class Edge
        {
            public int A;
            public int B;
            public float Cost;
        }

        // ---- 代价权重 ----
        private const float SlopeWeight = 320f;
        private const float WaterPenalty = 20f;
        private const float BlockedPenalty = 12f;
        private const float Cell = 5f;

        private readonly IMapScene _map;
        private readonly Scene _scene;

        // v4.21x: 高度采样分块惰性缓存(每块 32×32 个 5m 格 ≈ 160m, LRU 上限 64 块): 常驻内存封顶, 采样结果不变
        private const int HeightChunkShift = 5;
        private const int HeightCacheMaxChunks = 64;
        private readonly Dictionary<long, Dictionary<long, float>> _heightChunks = new Dictionary<long, Dictionary<long, float>>();
        private readonly Dictionary<long, LinkedListNode<long>> _heightChunkNodes = new Dictionary<long, LinkedListNode<long>>();
        private readonly LinkedList<long> _heightChunkLru = new LinkedList<long>();

        private readonly Dictionary<long, bool> _blockedCache = new Dictionary<long, bool>();
        private readonly Dictionary<long, bool> _waterCache = new Dictionary<long, bool>();
        private float _waterLevel = float.NegativeInfinity;
        private struct WaterRegion { public BoundingBox Box; public float Level; }
        private readonly List<WaterRegion> _waterRegions = new List<WaterRegion>();
        public int WaterRegions;
        public int DebugAdjacency;
        public int SkippedEdges;
        public int WaterRegionsSkipped;

        private readonly Dictionary<long, List<Vec2>> _corridor = new Dictionary<long, List<Vec2>>();
        private readonly Dictionary<long, List<Vec2>> _corridorDir = new Dictionary<long, List<Vec2>>();

        public List<Line> Lines = new List<Line>();
        public string Log = "";
        public int WaterHits;

        // v4.162: 站点提升为字段(供按需建线 API 复用); _prepared = 水位/水体/站点/宏网格已就绪
        private List<Station> _stations;
        private bool _prepared;

        public RailNetwork(IMapScene map, Scene scene)
        {
            _map = map;
            _scene = scene;
        }

        /// <summary>准备(水位/水体/站点/宏网格) —— 建网与按需建线共用。</summary>
        public void Prepare()
        {
            if (_prepared) return;
            _prepared = true;
            try
            {
                _waterLevel = _scene != null ? _scene.GetWaterLevel() : float.NegativeInfinity;
            }
            catch
            {
                _waterLevel = float.NegativeInfinity;
            }
            CollectWater();
            _stations = new List<Station>();
            foreach (var s in Settlement.All)
            {
                if (!s.IsTown && !s.IsCastle)
                {
                    continue;
                }
                _stations.Add(new Station { Id = s.StringId, Pos = s.Position.ToVec2() });
            }
            if (_stations.Count >= 2)
            {
                BuildMacroGrid(_stations);
            }
        }

        /// <summary>按需建线(v4.162): 给定两端定居点 StringId, 生成一条铁路线(选线/合轨/圆角)。</summary>
        public Line MakeLineByIds(string fromId, string toId)
        {
            Prepare();
            if (_stations == null || _stations.Count < 2) return null;
            int ia = -1, ib = -1;
            for (int i = 0; i < _stations.Count; i++)
            {
                if (_stations[i].Id == fromId) ia = i;
                if (_stations[i].Id == toId) ib = i;
            }
            if (ia < 0 || ib < 0 || ia == ib) return null;
            return MakeLine(_stations, ia, ib);
        }

        /// <summary>站点是否可用(城镇/城堡)。</summary>
        public bool HasStation(string settlementId)
        {
            Prepare();
            if (_stations == null) return false;
            for (int i = 0; i < _stations.Count; i++)
                if (_stations[i].Id == settlementId) return true;
            return false;
        }

        // =====================================================================
        // 主流程
        // =====================================================================
        public void Build(int kNearest = 4)
        {
            var sw = Stopwatch.StartNew();
            Prepare();
            var stations = _stations;
            if (stations == null || stations.Count < 2)
            {
                Log = "not enough settlements";
                return;
            }

            long t0 = sw.ElapsedMilliseconds;
            // 邻接 = Voronoi 领地相邻（Delaunay 边）：地图上挨着的城镇/城堡都要连
            var pairs = DelaunayEdges(stations);
            if (pairs.Count < stations.Count)
            {
                // 兜底：剖分异常时退回 k 近邻
                int k = Math.Min(Math.Max(kNearest, 4), Math.Max(2, stations.Count - 1));
                for (int i = 0; i < stations.Count; i++)
                {
                    var best = new List<Tuple<float, int>>();
                    for (int j = 0; j < stations.Count; j++)
                    {
                        if (i == j)
                        {
                            continue;
                        }
                        float dx = stations[i].Pos.x - stations[j].Pos.x;
                        float dy = stations[i].Pos.y - stations[j].Pos.y;
                        best.Add(Tuple.Create(dx * dx + dy * dy, j));
                    }
                    best.Sort((x, y) => x.Item1.CompareTo(y.Item1));
                    for (int m = 0; m < Math.Min(k, best.Count); m++)
                    {
                        int a = Math.Min(i, best[m].Item2);
                        int b = Math.Max(i, best[m].Item2);
                        pairs.Add(((long)a << 32) | (uint)b);
                    }
                }
            }
            DebugAdjacency = pairs.Count;

            var edges = new List<Edge>();
            foreach (long key in pairs)
            {
                int a = (int)(key >> 32);
                int b = (int)(key & 0xFFFFFFFF);
                edges.Add(new Edge { A = a, B = b, Cost = ApproxCost(stations[a].Pos, stations[b].Pos) });
            }
            long t1 = sw.ElapsedMilliseconds;

            var uf = new UnionFind(stations.Count);
            edges.Sort((x, y) => x.Cost.CompareTo(y.Cost));
            var used = new List<Edge>();
            var usedKeys = new HashSet<long>();
            foreach (var e in edges)
            {
                if (uf.Union(e.A, e.B))
                {
                    used.Add(e);
                    usedKeys.Add(((long)e.A << 32) | (uint)e.B);
                }
            }

            // 兜底：k 近邻候选图有可能不连通，用全量候选补到所有城镇都连上
            if (used.Count < stations.Count - 1)
            {
                var extra = new List<Edge>();
                for (int i = 0; i < stations.Count; i++)
                {
                    for (int j = i + 1; j < stations.Count; j++)
                    {
                        long key = ((long)i << 32) | (uint)j;
                        if (pairs.Contains(key))
                        {
                            continue;
                        }
                        extra.Add(new Edge { A = i, B = j, Cost = ApproxCost(stations[i].Pos, stations[j].Pos) });
                    }
                }
                extra.Sort((x, y) => x.Cost.CompareTo(y.Cost));
                foreach (var e in extra)
                {
                    if (used.Count >= stations.Count - 1)
                    {
                        break;
                    }
                    if (uf.Union(e.A, e.B))
                    {
                        used.Add(e);
                    }
                }
            }

            // 相邻就相连：Delaunay 邻接边全部铺上（只挡掉地形代价离谱到 4 倍的）
            const float EdgeRatio = 30.0f;
            int extraCount = 0;
            int skipped = 0;
            foreach (var e in edges)
            {
                long key = ((long)e.A << 32) | (uint)e.B;
                if (usedKeys.Contains(key))
                {
                    continue;
                }
                float len = Dist(stations[e.A].Pos, stations[e.B].Pos);
                if (e.Cost > EdgeRatio * len * 1.16f)
                {
                    skipped++;
                    continue;
                }
                usedKeys.Add(key);
                used.Add(e);
                extraCount++;
            }
            SkippedEdges = skipped;

            // 连通性检查（必须有 1 个连通分量，覆盖全部城镇）
            int roots = 0;
            for (int i = 0; i < stations.Count; i++)
            {
                if (uf.Find(i) == i)
                {
                    roots++;
                }
            }
            int covered = 0;
            {
                var seen = new HashSet<int>();
                foreach (var e in used)
                {
                    seen.Add(e.A);
                    seen.Add(e.B);
                }
                covered = seen.Count;
            }

            int failed = 0;
            foreach (var e in used)
            {
                var line = MakeLine(stations, e.A, e.B);
                if (line == null)
                {
                    failed++;
                    continue;
                }
                Lines.Add(line);
            }
            long t2 = sw.ElapsedMilliseconds;

            float total = 0f;
            float worstTurn = 0f;
            float worstRouteSlope = 0f;
            foreach (var ln in Lines)
            {
                total += ln.Length;
                float mt = MaxTurnDegrees(ln.Points);
                if (mt > worstTurn)
                {
                    worstTurn = mt;
                }
                for (int i = 0; i < ln.Points.Count; i += 3)
                {
                    float s = Slope(ln.Points[i]);
                    if (s > worstRouteSlope)
                    {
                        worstRouteSlope = s;
                    }
                }
            }

            var sb = new StringBuilder();
            sb.AppendLine(
                $"stations={stations.Count} adjacent={DebugAdjacency} edges={used.Count} (extra={extraCount} skipped={SkippedEdges}) components={roots} covered={covered}/{stations.Count} lines={Lines.Count} failed={failed} fallback={FallbackLines} fallbackIds={string.Join(",", FallbackIds.ToArray())}"
            );
            sb.AppendLine(
                $"totalLength={total:F0}m buildMs={sw.ElapsedMilliseconds} (pairs={t0} cost={t1 - t0} align={t2 - t1})"
                + $" maxTurn={worstTurn:F1}deg routeSlope={worstRouteSlope:P0} waterHits={WaterHits} water={_waterLevel:F1} waterRegions={WaterRegions} waterSkipped={WaterRegionsSkipped}"
            );
            Log = sb.ToString();
            sw.Stop();
        }

        /// <summary>给调试用的地形高度采样。</summary>
        public float SampleHeight(float x, float y)
        {
            return Height(new Vec2(x, y));
        }

        /// <summary>导出地形高度场 + 城镇点 + 水体框（给离线跑选线用）。</summary>
        public void DumpMap(string path)
        {
            try
            {
                float minX = float.MaxValue;
                float minY = float.MaxValue;
                float maxX = float.MinValue;
                float maxY = float.MinValue;
                var stations = new List<Station>();
                foreach (var s in Settlement.All)
                {
                    if (!s.IsTown && !s.IsCastle)
                    {
                        continue;
                    }
                    Vec2 p = s.Position.ToVec2();
                    stations.Add(new Station { Id = s.StringId, Pos = p });
                    if (p.x < minX) { minX = p.x; }
                    if (p.y < minY) { minY = p.y; }
                    if (p.x > maxX) { maxX = p.x; }
                    if (p.y > maxY) { maxY = p.y; }
                }
                if (stations.Count == 0)
                {
                    return;
                }
                minX -= 600f;
                minY -= 600f;
                maxX += 600f;
                maxY += 600f;
                const float step = 5f;
                int nx = (int)((maxX - minX) / step) + 1;
                int ny = (int)((maxY - minY) / step) + 1;
                var ci = System.Globalization.CultureInfo.InvariantCulture;
                var sb = new StringBuilder();
                sb.AppendLine(FormattableString.Invariant($"MAP {minX:F1} {minY:F1} {step:F1} {nx} {ny}"));
                foreach (var st in stations)
                {
                    sb.AppendLine(FormattableString.Invariant($"ST {st.Pos.x:F1} {st.Pos.y:F1} {st.Id}"));
                }
                sb.AppendLine("WATER");
                for (int i = 0; i < _waterRegions.Count; i++)
                {
                    var r = _waterRegions[i];
                    sb.AppendLine(
                        FormattableString.Invariant(
                            $"{r.Box[0].x:F0} {r.Box[0].y:F0} {r.Box[1].x:F0} {r.Box[1].y:F0} {r.Level:F1}"
                        )
                    );
                }
                sb.AppendLine("H");
                for (int y = 0; y < ny; y++)
                {
                    for (int x = 0; x < nx; x++)
                    {
                        sb.Append(SampleHeight(minX + x * step, minY + y * step).ToString("F1", ci));
                        sb.Append(' ');
                    }
                    sb.AppendLine();
                }
                System.IO.File.WriteAllText(path, sb.ToString());
            }
            catch
            {
            }
        }

        /// <summary>把地形高度场和所有线路写成文本，供离线渲染排查。</summary>
        public void DumpDebug(string path)
        {
            try
            {
                if (Lines.Count == 0)
                {
                    return;
                }
                float minX = float.MaxValue;
                float minY = float.MaxValue;
                float maxX = float.MinValue;
                float maxY = float.MinValue;
                foreach (var ln in Lines)
                {
                    foreach (var p in ln.Points)
                    {
                        if (p.x < minX) { minX = p.x; }
                        if (p.y < minY) { minY = p.y; }
                        if (p.x > maxX) { maxX = p.x; }
                        if (p.y > maxY) { maxY = p.y; }
                    }
                }
                minX -= 420f;
                minY -= 420f;
                maxX += 420f;
                maxY += 420f;
                const float step = 15f;
                int nx = (int)((maxX - minX) / step) + 1;
                int ny = (int)((maxY - minY) / step) + 1;
                var ci = System.Globalization.CultureInfo.InvariantCulture;
                var sb = new StringBuilder();
                sb.AppendLine(FormattableString.Invariant($"MAP {minX:F1} {minY:F1} {step:F1} {nx} {ny}"));
                for (int y = 0; y < ny; y++)
                {
                    for (int x = 0; x < nx; x++)
                    {
                        sb.Append(SampleHeight(minX + x * step, minY + y * step).ToString("F1", ci));
                        sb.Append(' ');
                    }
                    sb.AppendLine();
                }
                sb.AppendLine("WATER");
                for (int i = 0; i < _waterRegions.Count; i++)
                {
                    var r = _waterRegions[i];
                    sb.AppendLine(
                        FormattableString.Invariant(
                            $"{r.Box[0].x:F0} {r.Box[0].y:F0} {r.Box[1].x:F0} {r.Box[1].y:F0} {r.Level:F1}"
                        )
                    );
                }
                sb.AppendLine("LINES");
                foreach (var ln in Lines)
                {
                    sb.Append(ln.FromId).Append(' ').Append(ln.ToId).Append(' ');
                    var pts = new List<Vec2>();
                    for (int i = 0; i < ln.Points.Count; i += 2)
                    {
                        pts.Add(ln.Points[i]);
                    }
                    if (pts.Count == 0 || pts[pts.Count - 1] != ln.Points[ln.Points.Count - 1])
                    {
                        pts.Add(ln.Points[ln.Points.Count - 1]);
                    }
                    sb.Append(pts.Count);
                    foreach (var p in pts)
                    {
                        sb.Append(FormattableString.Invariant($" {p.x:F1} {p.y:F1}"));
                    }
                    sb.AppendLine();
                    // 逐点：轨面高 + 地形高（index 与 ln.Points 一致）
                    if (ln.RailZ != null && ln.TerrainZ != null)
                    {
                        sb.Append("Z");
                        for (int i = 0; i < ln.RailZ.Length; i += 2)
                        {
                            sb.Append(FormattableString.Invariant($" {ln.RailZ[i]:F1} {ln.TerrainZ[i]:F1}"));
                        }
                        sb.AppendLine();
                    }
                }
                System.IO.File.WriteAllText(path, sb.ToString());
            }
            catch
            {
            }
        }

        private Line MakeLine(List<Station> stations, int ia, int ib)
        {
            Station a = stations[ia];
            Station b = stations[ib];
            float dist = Dist(a.Pos, b.Pos);
            if (dist < 3f)
            {
                return null;
            }
            List<Vec2> pts = Align(stations, ia, ib);
            if (pts == null || pts.Count < 2)
            {
                FallbackLines++;
                if (FallbackIds.Count < 6)
                {
                    FallbackIds.Add(a.Id + "->" + b.Id + "@" + (int)dist + "m/r" + AlignNullReason);
                }
                // 回退：直线也要按 4 米加密，否则纵断面跨谷只能悬空、立柱也没法生成
                pts = new List<Vec2>();
                int steps = Math.Max(2, (int)(dist / 4f));
                for (int i = 0; i <= steps; i++)
                {
                    float f = (float)i / steps;
                    pts.Add(new Vec2(a.Pos.x + (b.Pos.x - a.Pos.x) * f, a.Pos.y + (b.Pos.y - a.Pos.y) * f));
                }
            }
            else if (pts.Count >= 5)
            {
                // 合轨：平行且靠近的线路缓缓贴合，避免一束平行双线
                BlendToCorridor(pts);
                RemoveFolds(pts, 120f);
                RoundCorners(pts, 10);
                pts[0] = a.Pos;
                pts[pts.Count - 1] = b.Pos;
            }
            for (int i = pts.Count - 1; i > 0; i--)
            {
                if (Dist(pts[i], pts[i - 1]) < 0.05f)
                {
                    pts.RemoveAt(i);
                }
            }
            if (pts.Count < 2)
            {
                return null;
            }
            var line = new Line
            {
                FromId = a.Id,
                ToId = b.Id,
                Points = pts,
                FromPos = a.Pos,
                ToPos = b.Pos,
            };
            float len = 0f;
            for (int i = 1; i < pts.Count; i++)
            {
                len += Dist(pts[i], pts[i - 1]);
            }
            line.Length = len;
            AddCorridor(pts);
            return line;
        }

        // =====================================================================
        // 邻接关系：Delaunay 三角剖分（= Voronoi 领地相邻）
        // =====================================================================
        private sealed class Dtri
        {
            public int A;
            public int B;
            public int C;
            public double Cx;
            public double Cy;
            public double R2;
            public bool Dead;
        }

        /// <summary>返回 Delaunay 边（a,b 已排序，打包成 long）。</summary>
        private static HashSet<long> DelaunayEdges(List<Station> stations)
        {
            int n = stations.Count;
            var edges = new HashSet<long>();
            if (n < 3)
            {
                for (int i = 0; i < n; i++)
                {
                    for (int j = i + 1; j < n; j++)
                    {
                        edges.Add(((long)i << 32) | (uint)j);
                    }
                }
                return edges;
            }
            var px = new double[n];
            var py = new double[n];
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            for (int i = 0; i < n; i++)
            {
                px[i] = stations[i].Pos.x;
                py[i] = stations[i].Pos.y;
                if (px[i] < minX) { minX = px[i]; }
                if (py[i] < minY) { minY = py[i]; }
                if (px[i] > maxX) { maxX = px[i]; }
                if (py[i] > maxY) { maxY = py[i]; }
            }
            double cx0 = (minX + maxX) * 0.5;
            double cy0 = (minY + maxY) * 0.5;
            double span = Math.Max(maxX - minX, maxY - minY);
            if (span < 1.0)
            {
                span = 1.0;
            }
            // 超三角形
            double m = span * 40.0;
            var ptsX = new double[n + 3];
            var ptsY = new double[n + 3];
            for (int i = 0; i < n; i++)
            {
                ptsX[i] = px[i];
                ptsY[i] = py[i];
            }
            ptsX[n] = cx0 - m;
            ptsY[n] = cy0 - m;
            ptsX[n + 1] = cx0 + m;
            ptsY[n + 1] = cy0 - m;
            ptsX[n + 2] = cx0;
            ptsY[n + 2] = cy0 + m;

            var tris = new List<Dtri>();
            tris.Add(MakeTri(ptsX, ptsY, n, n + 1, n + 2));
            for (int i = 0; i < n; i++)
            {
                var bad = new List<Dtri>();
                for (int k = 0; k < tris.Count; k++)
                {
                    Dtri tr = tris[k];
                    double dx = ptsX[i] - tr.Cx;
                    double dy = ptsY[i] - tr.Cy;
                    if (dx * dx + dy * dy <= tr.R2 + 1e-9)
                    {
                        bad.Add(tr);
                    }
                }
                // 空洞边界
                var border = new List<int[]>();
                for (int k = 0; k < bad.Count; k++)
                {
                    Dtri tr = bad[k];
                    AddBorder(border, tr.A, tr.B);
                    AddBorder(border, tr.B, tr.C);
                    AddBorder(border, tr.C, tr.A);
                }
                for (int k = 0; k < bad.Count; k++)
                {
                    bad[k].Dead = true;
                }
                tris.RemoveAll(x => x.Dead);
                for (int k = 0; k < border.Count; k++)
                {
                    int[] e = border[k];
                    if (e[0] == i || e[1] == i)
                    {
                        continue;
                    }
                    tris.Add(MakeTri(ptsX, ptsY, e[0], e[1], i));
                }
            }
            for (int k = 0; k < tris.Count; k++)
            {
                Dtri tr = tris[k];
                if (tr.A >= n || tr.B >= n || tr.C >= n)
                {
                    continue;
                }
                int a = tr.A;
                int b = tr.B;
                int c = tr.C;
                edges.Add(a < b ? (((long)a << 32) | (uint)b) : (((long)b << 32) | (uint)a));
                edges.Add(b < c ? (((long)b << 32) | (uint)c) : (((long)c << 32) | (uint)b));
                edges.Add(a < c ? (((long)a << 32) | (uint)c) : (((long)c << 32) | (uint)a));
            }
            return edges;
        }

        private static void AddBorder(List<int[]> border, int a, int b)
        {
            for (int i = 0; i < border.Count; i++)
            {
                int[] e = border[i];
                if ((e[0] == a && e[1] == b) || (e[0] == b && e[1] == a))
                {
                    border.RemoveAt(i);
                    return;
                }
            }
            border.Add(new[] { a, b });
        }

        private static Dtri MakeTri(double[] xs, double[] ys, int a, int b, int c)
        {
            double ax = xs[a];
            double ay = ys[a];
            double bx = xs[b];
            double by = ys[b];
            double cx = xs[c];
            double cy = ys[c];
            double d = 2.0 * (ax * (by - cy) + bx * (cy - ay) + cx * (ay - by));
            var tr = new Dtri { A = a, B = b, C = c };
            if (Math.Abs(d) < 1e-12)
            {
                tr.Cx = (ax + bx + cx) / 3.0;
                tr.Cy = (ay + by + cy) / 3.0;
                tr.R2 = 1e12;
                return tr;
            }
            double ux = ((ax * ax + ay * ay) * (by - cy) + (bx * bx + by * by) * (cy - ay) + (cx * cx + cy * cy) * (ay - by)) / d;
            double uy = ((ax * ax + ay * ay) * (cx - bx) + (bx * bx + by * by) * (ax - cx) + (cx * cx + cy * cy) * (bx - ax)) / d;
            tr.Cx = ux;
            tr.Cy = uy;
            double dx = ax - ux;
            double dy = ay - uy;
            tr.R2 = dx * dx + dy * dy;
            return tr;
        }

        // =====================================================================
        // 选线：网格 A*（全局找山口/山谷）+ 合轨 + 圆化
        // =====================================================================
        private const float GridCell = 8f;
        public int FallbackLines;
        public List<string> FallbackIds = new List<string>();
        public int AlignNullReason;
        private const int MaxGridCells = 340000;

        private List<Vec2> Align(List<Station> stations, int ia, int ib)
        {
            Vec2 start = stations[ia].Pos;
            Vec2 end = stations[ib].Pos;
            float dist = Dist(start, end);
            if (dist < 30f)
            {
                return new List<Vec2> { start, end };
            }
            AlignNullReason = 0;

            float margin = Math.Max(260f, dist * 0.65f);
            float cell = GridCell;
            int nx;
            int ny;
            float minX;
            float minY;
            for (int attempt = 0; attempt < 4; attempt++)
            {
                minX = Math.Min(start.x, end.x) - margin;
                minY = Math.Min(start.y, end.y) - margin;
                float maxX = Math.Max(start.x, end.x) + margin;
                float maxY = Math.Max(start.y, end.y) + margin;
                nx = (int)((maxX - minX) / cell) + 1;
                ny = (int)((maxY - minY) / cell) + 1;
                if ((long)nx * ny <= MaxGridCells)
                {
                    var path = GridSearch(start, end, minX, minY, cell, nx, ny);
                    if (path != null && path.Count >= 2)
                    {
                        return FinishAlign(path, start, end);
                    }
                    AlignNullReason = 2;
                    return null;
                }
                cell *= 1.6f;
                margin *= 0.8f;
            }
            return null;
        }

        private List<Vec2> FinishAlign(List<Vec2> path, Vec2 start, Vec2 end)
        {
            // 均匀重采样（3 米）后圆化，得到顺滑曲线
            var resampled = Resample(path, 3f);
            // A* 格心可能"过冲"到城中心后面，先裁掉端点附近的点，否则会形成 180° 折返
            TrimNear(resampled, start, 6f, true);
            TrimNear(resampled, end, 6f, false);
            RoundCorners(resampled, 60);
            resampled[0] = start;
            resampled[resampled.Count - 1] = end;
            // 折返点清除（锐角/发卡弯）
            RemoveFolds(resampled, 120f);
            RoundCorners(resampled, 12);
            resampled[0] = start;
            resampled[resampled.Count - 1] = end;
            return resampled;
        }

        /// <summary>裁掉端点附近（keep 米内）的点，避免端点方向折返。</summary>
        private static void TrimNear(List<Vec2> pts, Vec2 anchor, float keep, bool fromStart)
        {
            if (pts.Count < 5)
            {
                return;
            }
            int guard = 0;
            while (pts.Count > 4 && guard++ < pts.Count)
            {
                int idx = fromStart ? 0 : pts.Count - 1;
                if (Dist(pts[idx], anchor) >= keep)
                {
                    break;
                }
                pts.RemoveAt(idx);
            }
        }

        /// <summary>删除急折返点：转角超过 maxTurn 的点直接去掉（发卡弯）。</summary>
        private static void RemoveFolds(List<Vec2> pts, float maxTurn)
        {
            for (int pass = 0; pass < 4; pass++)
            {
                bool removed = false;
                for (int i = pts.Count - 2; i >= 1; i--)
                {
                    if (pts.Count <= 4)
                    {
                        break;
                    }
                    float ax = pts[i].x - pts[i - 1].x;
                    float ay = pts[i].y - pts[i - 1].y;
                    float bx = pts[i + 1].x - pts[i].x;
                    float by = pts[i + 1].y - pts[i].y;
                    float la = (float)Math.Sqrt(ax * ax + ay * ay);
                    float lb = (float)Math.Sqrt(bx * bx + by * by);
                    if (la < 1e-5f || lb < 1e-5f)
                    {
                        pts.RemoveAt(i);
                        removed = true;
                        continue;
                    }
                    float dot = (ax * bx + ay * by) / (la * lb);
                    float ang = (float)(Math.Acos(Math.Max(-1f, Math.Min(1f, dot))) * 180.0 / Math.PI);
                    if (ang > maxTurn)
                    {
                        pts.RemoveAt(i);
                        removed = true;
                    }
                }
                if (!removed)
                {
                    break;
                }
            }
        }

        /// <summary>网格 A*：代价 = 步长 × (1 + 坡度²×8 + 陡坡/水/无路附加)。</summary>
        private List<Vec2> GridSearch(Vec2 start, Vec2 end, float minX, float minY, float cell, int nx, int ny)
        {
            int count = nx * ny;
            var g = new float[count];
            var came = new int[count];
            for (int i = 0; i < count; i++)
            {
                g[i] = float.MaxValue;
                came[i] = -1;
            }
            int si = CellIndex(start, minX, minY, cell, nx, ny);
            int gi = CellIndex(end, minX, minY, cell, nx, ny);
            if (si < 0 || gi < 0)
            {
                return null;
            }
            var heap = new MinHeap();
            g[si] = 0f;
            heap.Push(0f, si);
            var costCache = new float[count];
            for (int i = 0; i < count; i++)
            {
                costCache[i] = -1f;
            }
            while (heap.Count > 0)
            {
                int u = heap.Pop();
                if (u == gi)
                {
                    break;
                }
                int ux = u % nx;
                int uy = u / nx;
                float gu = g[u];
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0)
                        {
                            continue;
                        }
                        int vx = ux + dx;
                        int vy = uy + dy;
                        if (vx < 0 || vy < 0 || vx >= nx || vy >= ny)
                        {
                            continue;
                        }
                        int v = vy * nx + vx;
                        float step = (dx != 0 && dy != 0) ? cell * 1.4142f : cell;
                        float f = costCache[v];
                        if (f < 0f)
                        {
                            Vec2 vp = new Vec2(minX + vx * cell, minY + vy * cell);
                            f = CellCost(vp);
                            costCache[v] = f;
                        }
                        float ng = gu + step * (1f + f);
                        if (ng < g[v])
                        {
                            g[v] = ng;
                            came[v] = u;
                            var vp2 = new Vec2(minX + vx * cell, minY + vy * cell);
                            float hx = vp2.x - end.x;
                            float hy = vp2.y - end.y;
                            heap.Push(ng + (float)Math.Sqrt(hx * hx + hy * hy), v);
                        }
                    }
                }
            }
            if (gi != si && came[gi] < 0)
            {
                return null;
            }
            var cells = new List<int>();
            int cur = gi;
            cells.Add(cur);
            while (cur != si)
            {
                cur = came[cur];
                if (cur < 0)
                {
                    return null;
                }
                cells.Add(cur);
            }
            cells.Reverse();
            var result = new List<Vec2>(cells.Count);
            foreach (int c in cells)
            {
                result.Add(new Vec2(minX + (c % nx) * cell, minY + (c / nx) * cell));
            }
            return result;
        }

        private float CellCost(Vec2 p)
        {
            float s = Slope(p);
            float f = 80f * s * s;
            if (s > 0.5f)
            {
                f += 60f;
            }
            if (s > 0.9f)
            {
                f += 300f;
            }
            f += 0.35f * ((MacroHeight(p) - _mgHeightMin) / _mgHeightMax);
            if (IsWater(p))
            {
                f += 30f;
            }
            else if (IsBlocked(p))
            {
                f += 12f;
            }
            return f;
        }

        private static int CellIndex(Vec2 p, float minX, float minY, float cell, int nx, int ny)
        {
            int cx = (int)Math.Round((p.x - minX) / cell);
            int cy = (int)Math.Round((p.y - minY) / cell);
            if (cx < 0 || cy < 0 || cx >= nx || cy >= ny)
            {
                return -1;
            }
            return cy * nx + cx;
        }

        private static List<Vec2> Resample(List<Vec2> pts, float spacing)
        {
            var result = new List<Vec2>();
            if (pts.Count < 2)
            {
                return new List<Vec2>(pts);
            }
            result.Add(pts[0]);
            float carry = 0f;
            for (int i = 1; i < pts.Count; i++)
            {
                Vec2 a = pts[i - 1];
                Vec2 b = pts[i];
                float dx = b.x - a.x;
                float dy = b.y - a.y;
                float seg = (float)Math.Sqrt(dx * dx + dy * dy);
                if (seg < 1e-4f)
                {
                    continue;
                }
                float tt = carry;
                while (tt + spacing <= seg)
                {
                    tt += spacing;
                    float f = tt / seg;
                    result.Add(new Vec2(a.x + dx * f, a.y + dy * f));
                }
                carry = tt - seg;
            }
            result.Add(pts[pts.Count - 1]);
            return result;
        }

        private float ApproxCost(Vec2 a, Vec2 b)
        {
            float len = Dist(a, b);
            if (len < 1f)
            {
                return 1e9f;
            }
            int n = Math.Max(4, Math.Min(24, (int)(len / 25f)));
            float total = 0f;
            var samples = new List<Vec2>(n + 1);
            for (int i = 0; i <= n; i++)
            {
                float t = (float)i / n;
                samples.Add(new Vec2(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t));
            }
            foreach (var p in samples)
            {
                total += TerrainCost(p);
            }
            return len * (1f + total / samples.Count);
        }

        // =====================================================================
        // 代价项
        // =====================================================================
        private float TerrainCost(Vec2 p)
        {
            float cost = 0f;
            float slope = Slope(p);
            cost += SlopeWeight * slope * slope;
            if (IsWater(p))
            {
                cost += WaterPenalty;
            }
            else if (IsBlocked(p))
            {
                cost += BlockedPenalty;
            }
            return cost;
        }


        // =====================================================================
        // 地形采样（带缓存）
        // =====================================================================
        private static long CellKey(Vec2 p)
        {
            long cx = (long)Math.Floor(p.x / Cell);
            long cy = (long)Math.Floor(p.y / Cell);
            return (cx << 32) ^ (cy & 0xFFFFFFFFL);
        }

        private static long ChunkKey(Vec2 p)
        {
            long cx = (long)Math.Floor(p.x / (Cell * (1 << HeightChunkShift)));
            long cy = (long)Math.Floor(p.y / (Cell * (1 << HeightChunkShift)));
            return (cx << 32) ^ (cy & 0xFFFFFFFFL);
        }

        private float Height(Vec2 p)
        {
            long key = CellKey(p);
            long ck = ChunkKey(p);
            Dictionary<long, float> chunk;
            if (_heightChunks.TryGetValue(ck, out chunk))
            {
                float cached;
                if (chunk.TryGetValue(key, out cached))
                {
                    LinkedListNode<long> node;
                    if (_heightChunkNodes.TryGetValue(ck, out node))
                    {
                        _heightChunkLru.Remove(node);
                        _heightChunkLru.AddLast(node);
                    }
                    return cached;
                }
            }
            else
            {
                chunk = new Dictionary<long, float>(1 << (HeightChunkShift * 2));
                _heightChunks[ck] = chunk;
                _heightChunkNodes[ck] = _heightChunkLru.AddLast(ck);
                while (_heightChunks.Count > HeightCacheMaxChunks)
                {
                    var oldest = _heightChunkLru.First;
                    if (oldest == null || oldest.Value == ck)
                    {
                        break;
                    }
                    _heightChunkLru.RemoveFirst();
                    _heightChunkNodes.Remove(oldest.Value);
                    _heightChunks.Remove(oldest.Value);
                }
            }
            var pos = new CampaignVec2(p, true);
            float v = 0f;
            try
            {
                _map.GetHeightAtPoint(in pos, ref v);
            }
            catch
            {
            }
            chunk[key] = v;
            return v;
        }

        /// <summary>
        /// 地形坡度（tan 值）：直接用地形高度差分算（法线数据有的地图不可靠）。
        /// 取东西/南北各 8 米的高差，得到局部最大坡度。
        /// </summary>
        // 宏观地形网格（10 米格 + 30 米盒模糊）：过滤掉 5 米级的假台阶/断崖
        private float[] _mgHeight;
        private float[] _mgGrad;
        private int _mgNx;
        private int _mgNy;
        private float _mgMinX;
        private float _mgMinY;
        private float _mgHeightMin;
        private float _mgHeightMax = 1f;
        private const float MgStep = 10f;

        /// <summary>宏观地形坡度（tan 值）：来自 30 米模糊网格，忽略微观台阶。</summary>
        private float Slope(Vec2 p)
        {
            return _mgGrad == null ? 0f : SampleGrid(_mgGrad, p);
        }

        /// <summary>宏观海拔（米）。</summary>
        private float MacroHeight(Vec2 p)
        {
            return _mgHeight == null ? 0f : SampleGrid(_mgHeight, p);
        }

        /// <summary>双线性采样宏观网格。</summary>
        private float SampleGrid(float[] grid, Vec2 p)
        {
            float fx = (p.x - _mgMinX) / MgStep;
            float fy = (p.y - _mgMinY) / MgStep;
            if (fx < 0f)
            {
                fx = 0f;
            }
            if (fy < 0f)
            {
                fy = 0f;
            }
            int x0 = (int)fx;
            int y0 = (int)fy;
            if (x0 > _mgNx - 2)
            {
                x0 = _mgNx - 2;
            }
            if (y0 > _mgNy - 2)
            {
                y0 = _mgNy - 2;
            }
            if (x0 < 0 || y0 < 0)
            {
                return 0f;
            }
            float tx = fx - x0;
            float ty = fy - y0;
            int i = y0 * _mgNx + x0;
            float h00 = grid[i];
            float h10 = grid[i + 1];
            float h01 = grid[i + _mgNx];
            float h11 = grid[i + _mgNx + 1];
            return (h00 * (1f - tx) + h10 * tx) * (1f - ty) + (h01 * (1f - tx) + h11 * tx) * ty;
        }

        /// <summary>构造宏观地形网格：10 米格取均值 → 30 米盒模糊 → 梯度。</summary>
        private void BuildMacroGrid(List<Station> stations)
        {
            float minX = float.MaxValue;
            float minY = float.MaxValue;
            float maxX = float.MinValue;
            float maxY = float.MinValue;
            foreach (var st in stations)
            {
                if (st.Pos.x < minX) { minX = st.Pos.x; }
                if (st.Pos.y < minY) { minY = st.Pos.y; }
                if (st.Pos.x > maxX) { maxX = st.Pos.x; }
                if (st.Pos.y > maxY) { maxY = st.Pos.y; }
            }
            _mgMinX = minX - 700f;
            _mgMinY = minY - 700f;
            _mgNx = (int)((maxX + 700f - _mgMinX) / MgStep) + 2;
            _mgNy = (int)((maxY + 700f - _mgMinY) / MgStep) + 2;
            int count = _mgNx * _mgNy;
            var raw = new float[count];
            for (int y = 0; y < _mgNy; y++)
            {
                for (int x = 0; x < _mgNx; x++)
                {
                    float cx = _mgMinX + x * MgStep;
                    float cy = _mgMinY + y * MgStep;
                    float h = Height(new Vec2(cx, cy));
                    h += Height(new Vec2(cx + MgStep, cy));
                    h += Height(new Vec2(cx, cy + MgStep));
                    h += Height(new Vec2(cx + MgStep, cy + MgStep));
                    raw[y * _mgNx + x] = h * 0.25f;
                }
            }
            var blurred = new float[count];
            for (int y = 0; y < _mgNy; y++)
            {
                for (int x = 0; x < _mgNx; x++)
                {
                    float sum = 0f;
                    int n = 0;
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        int yy = y + dy;
                        if (yy < 0 || yy >= _mgNy)
                        {
                            continue;
                        }
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int xx = x + dx;
                            if (xx < 0 || xx >= _mgNx)
                            {
                                continue;
                            }
                            sum += raw[yy * _mgNx + xx];
                            n++;
                        }
                    }
                    blurred[y * _mgNx + x] = n > 0 ? sum / n : raw[y * _mgNx + x];
                }
            }
            var grad = new float[count];
            for (int y = 0; y < _mgNy; y++)
            {
                for (int x = 0; x < _mgNx; x++)
                {
                    int xm = x > 0 ? x - 1 : x;
                    int xp = x < _mgNx - 1 ? x + 1 : x;
                    int ym = y > 0 ? y - 1 : y;
                    int yp = y < _mgNy - 1 ? y + 1 : y;
                    float dx = (blurred[y * _mgNx + xp] - blurred[y * _mgNx + xm]) / ((xp - xm) * MgStep);
                    float dy = (blurred[yp * _mgNx + x] - blurred[ym * _mgNx + x]) / ((yp - ym) * MgStep);
                    grad[y * _mgNx + x] = (float)Math.Sqrt(dx * dx + dy * dy);
                }
            }
            float hmin = float.MaxValue;
            float hmax = float.MinValue;
            for (int i = 0; i < count; i++)
            {
                if (blurred[i] < hmin)
                {
                    hmin = blurred[i];
                }
                if (blurred[i] > hmax)
                {
                    hmax = blurred[i];
                }
            }
            _mgHeightMin = hmin;
            _mgHeightMax = Math.Max(0.001f, hmax - hmin);
            _mgHeight = blurred;
            _mgGrad = grad;
        }

        /// <summary>收集场景里的水体实体（名字含 water/lake/river/sea 的实体），用包围盒判水。</summary>
        private void CollectWater()
        {
            if (_scene == null)
            {
                return;
            }
            List<GameEntity> all = new List<GameEntity>();
            try
            {
                _scene.GetEntities(ref all);
            }
            catch
            {
                return;
            }
            for (int i = 0; i < all.Count; i++)
            {
                GameEntity ent = all[i];
                string name;
                try
                {
                    name = ent.Name ?? "";
                }
                catch
                {
                    continue;
                }
                string lower = name.ToLowerInvariant();
                if (lower.IndexOf("hideout", StringComparison.Ordinal) >= 0)
                {
                    continue;
                }
                bool isWater =
                    lower.IndexOf("water", StringComparison.Ordinal) >= 0
                    || lower.IndexOf("lake", StringComparison.Ordinal) >= 0
                    || lower.IndexOf("river", StringComparison.Ordinal) >= 0
                    || lower.IndexOf("sea", StringComparison.Ordinal) >= 0
                    || lower.IndexOf("ocean", StringComparison.Ordinal) >= 0
                    || lower.IndexOf("coast", StringComparison.Ordinal) >= 0;
                if (!isWater)
                {
                    continue;
                }
                BoundingBox bb;
                try
                {
                    bb = ent.GetGlobalBoundingBox();
                }
                catch
                {
                    continue;
                }
                float level = Math.Max(bb[0].z, bb[1].z);
                if (level < -100f)
                {
                    continue;
                }
                float dx = Math.Abs(bb[1].x - bb[0].x);
                float dy = Math.Abs(bb[1].y - bb[0].y);
                float diag = (float)Math.Sqrt(dx * dx + dy * dy);
                if (diag > 600f || dx * dy > 300000f)
                {
                    WaterRegionsSkipped++;
                    continue;
                }
                _waterRegions.Add(new WaterRegion { Box = bb, Level = level });
            }
            WaterRegions = _waterRegions.Count;
        }

        private bool IsWater(Vec2 p)
        {
            long key = CellKey(p);
            bool v;
            if (_waterCache.TryGetValue(key, out v))
            {
                return v;
            }
            v = false;
            float h = Height(p);
            // 1) 场景水体实体：包围盒内 + 地形低于水面
            for (int i = 0; i < _waterRegions.Count; i++)
            {
                WaterRegion r = _waterRegions[i];
                if (h < r.Level - 0.3f)
                {
                    var pt = new Vec3(p.x, p.y, r.Level);
                    if (r.Box.PointInsideBox(pt, 0.5f))
                    {
                        v = true;
                        break;
                    }
                }
            }
            if (_scene != null && !v)
            {
                try
                {
                    float wl = _scene.GetWaterLevelAtPosition(p, true, true);
                    if (wl > -9000f && h < wl + 0.6f)
                    {
                        v = true;
                    }
                }
                catch
                {
                }
            }
            if (!v && _waterLevel > float.NegativeInfinity && h < _waterLevel + 0.6f)
            {
                v = true;
            }
            if (v)
            {
                WaterHits++;
            }
            _waterCache[key] = v;
            return v;
        }

        private bool IsBlocked(Vec2 p)
        {
            long key = CellKey(p);
            bool v;
            if (_blockedCache.TryGetValue(key, out v))
            {
                return v;
            }
            v = false;
            try
            {
                var pos = new CampaignVec2(p, true);
                v = !_map.GetFaceIndex(in pos).IsValid();
            }
            catch
            {
            }
            _blockedCache[key] = v;
            return v;
        }

        // =====================================================================
        // 走廊合轨
        // =====================================================================
        private static long CorridorKey(Vec2 p)
        {
            long cx = (long)Math.Floor(p.x / 4f);
            long cy = (long)Math.Floor(p.y / 4f);
            return (cx << 32) ^ (cy & 0xFFFFFFFFL);
        }

        private void AddCorridor(List<Vec2> pts)
        {
            for (int i = 1; i < pts.Count; i++)
            {
                float dx = pts[i].x - pts[i - 1].x;
                float dy = pts[i].y - pts[i - 1].y;
                float dl = (float)Math.Sqrt(dx * dx + dy * dy);
                if (dl < 1e-4f)
                {
                    continue;
                }
                long key = CorridorKey(pts[i]);
                List<Vec2> list;
                if (!_corridor.TryGetValue(key, out list))
                {
                    list = new List<Vec2>();
                    _corridor[key] = list;
                    _corridorDir[key] = new List<Vec2>();
                }
                list.Add(pts[i]);
                _corridorDir[key].Add(new Vec2(dx / dl, dy / dl));
            }
        }

        /// <summary>缓动合轨：距离越近贴得越多；只有方向一致（平行）的线路才吸附。</summary>
        private void BlendToCorridor(List<Vec2> pts)
        {
            const float capture = 10f;
            const float core = 2.2f;
            int n = pts.Count;
            if (n < 5)
            {
                return;
            }
            for (int pass = 0; pass < 3; pass++)
            {
                for (int i = 2; i < n - 2; i++)
                {
                    float ldx = pts[i + 1].x - pts[i - 1].x;
                    float ldy = pts[i + 1].y - pts[i - 1].y;
                    float ldl = (float)Math.Sqrt(ldx * ldx + ldy * ldy);
                    if (ldl < 1e-4f)
                    {
                        continue;
                    }
                    ldx /= ldl;
                    ldy /= ldl;
                    long c0 = CorridorKey(pts[i]);
                    long c0x = c0 >> 32;
                    long c0y = c0 & 0xFFFFFFFFL;
                    Vec2 best = new Vec2(0f, 0f);
                    float bestD2 = capture * capture;
                    bool found = false;
                    for (long gx = -2; gx <= 2; gx++)
                    {
                        for (long gy = -2; gy <= 2; gy++)
                        {
                            long key = ((c0x + gx) << 32) ^ ((c0y + gy) & 0xFFFFFFFFL);
                            List<Vec2> list;
                            List<Vec2> dirs;
                            if (!_corridor.TryGetValue(key, out list) || !_corridorDir.TryGetValue(key, out dirs))
                            {
                                continue;
                            }
                            for (int k = 0; k < list.Count; k++)
                            {
                                float ddx = list[k].x - pts[i].x;
                                float ddy = list[k].y - pts[i].y;
                                float d2 = ddx * ddx + ddy * ddy;
                                if (d2 >= bestD2)
                                {
                                    continue;
                                }
                                float dot = dirs[k].x * ldx + dirs[k].y * ldy;
                                if (dot < 0.88f && dot > -0.88f)
                                {
                                    continue;
                                }
                                bestD2 = d2;
                                best = list[k];
                                found = true;
                            }
                        }
                    }
                    if (!found)
                    {
                        continue;
                    }
                    float d = (float)Math.Sqrt(bestD2);
                    if (d <= core)
                    {
                        pts[i] = best;
                        continue;
                    }
                    float w = 1f - (d - core) / (capture - core);
                    w = w * w * (3f - 2f * w);
                    pts[i] = new Vec2(pts[i].x + (best.x - pts[i].x) * w, pts[i].y + (best.y - pts[i].y) * w);
                }
            }
        }

        // =====================================================================
        // 曲线工具
        // =====================================================================
        public static void RoundCorners(List<Vec2> pts, int passes)
        {
            int n = pts.Count;
            if (n < 5)
            {
                return;
            }
            for (int it = 0; it < passes; it++)
            {
                for (int i = 1; i < n - 1; i++)
                {
                    pts[i] = new Vec2(
                        pts[i - 1].x * 0.25f + pts[i].x * 0.5f + pts[i + 1].x * 0.25f,
                        pts[i - 1].y * 0.25f + pts[i].y * 0.5f + pts[i + 1].y * 0.25f
                    );
                }
            }
        }

        public static float MaxTurnDegrees(List<Vec2> pts)
        {
            int n = pts.Count;
            float worst = 0f;
            for (int i = 1; i < n - 1; i++)
            {
                float d1x = pts[i].x - pts[i - 1].x;
                float d1y = pts[i].y - pts[i - 1].y;
                float d2x = pts[i + 1].x - pts[i].x;
                float d2y = pts[i + 1].y - pts[i].y;
                float l1 = (float)Math.Sqrt(d1x * d1x + d1y * d1y);
                float l2 = (float)Math.Sqrt(d2x * d2x + d2y * d2y);
                if (l1 < 1e-4f || l2 < 1e-4f)
                {
                    continue;
                }
                float dot = Clamp((d1x * d2x + d1y * d2y) / (l1 * l2), -1f, 1f);
                float ang = (float)(Math.Acos(dot) * 180.0 / Math.PI);
                if (ang > worst)
                {
                    worst = ang;
                }
            }
            return worst;
        }

        private static float Clamp(float v, float lo, float hi)
        {
            return v < lo ? lo : (v > hi ? hi : v);
        }

        public static float Dist(Vec2 a, Vec2 b)
        {
            float dx = a.x - b.x;
            float dy = a.y - b.y;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        private class MinHeap
        {
            private readonly List<float> _keys = new List<float>();
            private readonly List<int> _values = new List<int>();

            public int Count => _keys.Count;

            public void Push(float key, int value)
            {
                _keys.Add(key);
                _values.Add(value);
                int i = _keys.Count - 1;
                while (i > 0)
                {
                    int parent = (i - 1) / 2;
                    if (_keys[parent] <= _keys[i])
                    {
                        break;
                    }
                    Swap(parent, i);
                    i = parent;
                }
            }

            public int Pop()
            {
                int top = _values[0];
                int last = _keys.Count - 1;
                _keys[0] = _keys[last];
                _values[0] = _values[last];
                _keys.RemoveAt(last);
                _values.RemoveAt(last);
                int i = 0;
                while (true)
                {
                    int l = i * 2 + 1;
                    int r = l + 1;
                    int small = i;
                    if (l < _keys.Count && _keys[l] < _keys[small])
                    {
                        small = l;
                    }
                    if (r < _keys.Count && _keys[r] < _keys[small])
                    {
                        small = r;
                    }
                    if (small == i)
                    {
                        break;
                    }
                    Swap(i, small);
                    i = small;
                }
                return top;
            }

            private void Swap(int a, int b)
            {
                float k = _keys[a];
                _keys[a] = _keys[b];
                _keys[b] = k;
                int v = _values[a];
                _values[a] = _values[b];
                _values[b] = v;
            }
        }

        private class UnionFind
        {
            private readonly int[] _parent;

            public UnionFind(int n)
            {
                _parent = new int[n];
                for (int i = 0; i < n; i++)
                {
                    _parent[i] = i;
                }
            }

            public int Find(int a)
            {
                while (_parent[a] != a)
                {
                    _parent[a] = _parent[_parent[a]];
                    a = _parent[a];
                }
                return a;
            }

            public bool Union(int a, int b)
            {
                int ra = Find(a);
                int rb = Find(b);
                if (ra == rb)
                {
                    return false;
                }
                _parent[rb] = ra;
                return true;
            }
        }
    }
}
