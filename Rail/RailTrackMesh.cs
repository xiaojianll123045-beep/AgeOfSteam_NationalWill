using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Map;
using TaleWorlds.Engine;
using TaleWorlds.Library;

namespace FeudalInternalAffairs
{
    /// <summary>
    /// 铁路渲染（重构版）：把线路画成【完全贴地】的白色细线。
    /// - 沿地形按 SampleStep 米密集采样，每个采样点直接取地面高度（只抬 Lift 米防闪面）
    /// - 不做限坡剖面、不做填方/立柱/道床：地形什么样，线就贴成什么样
    /// - 恒定宽度 HalfWidth*2 = 0.4 米
    /// </summary>
    public static class RailTrackMesh
    {
        private const float HalfWidth = 0.2f;      // 0.4 米宽白线
        private const float Lift = 0.06f;          // 离地抬升（防 z-fighting）
        private const float SampleStep = 1.5f;     // 沿线采样间距（米）
        private const float MinStep = 0.02f;

        /// <summary>贴地高度（列车运行用，索引与 pts 对齐）。</summary>
        public static float[] ComputeProfile(IMapScene map, List<Vec2> pts)
        {
            float[] terrain;
            return ComputeProfile(map, pts, out terrain);
        }

        public static float[] ComputeProfile(IMapScene map, List<Vec2> pts, out float[] terrain)
        {
            int n = pts.Count;
            terrain = new float[n];
            var z = new float[n];
            for (int i = 0; i < n; i++)
            {
                float h = Height(map, pts[i]);
                terrain[i] = h;
                z[i] = h + Lift;
            }
            return z;
        }

        public static void Build(
            IMapScene map,
            List<RailNetwork.Line> lines,
            out Mesh bed,
            out Mesh rails,
            out Mesh sleepers,
            out string stats
        )
        {
            var lineGeo = new GeoBuilder { Color = 0xFFFFFFFFu };
            var emptyGeo = new GeoBuilder { Color = 0xFFFFFFFFu };

            var sw = new System.Diagnostics.Stopwatch();
            sw.Start();
            int linesUsed = 0;
            int segments = 0;

            var dense = new List<Vec2>();
            for (int li = 0; li < lines.Count; li++)
            {
                RailNetwork.Line line = lines[li];
                List<Vec2> pts = line.Points;
                if (pts == null || pts.Count < 2)
                {
                    continue;
                }
                int n = pts.Count;

                // 列车/调试用的贴地高度（与 line.Points 对齐）
                float[] terrain;
                float[] zRail = ComputeProfile(map, pts, out terrain);
                line.RailZ = zRail;
                line.TerrainZ = terrain;

                // 密集重采样：完全贴着地形走
                Resample(pts, SampleStep, dense);
                int m = dense.Count;
                if (m < 2)
                {
                    continue;
                }

                for (int i = 0; i < m - 1; i++)
                {
                    Vec2 a = dense[i];
                    Vec2 b = dense[i + 1];
                    float dx = b.x - a.x;
                    float dy = b.y - a.y;
                    float len = (float)Math.Sqrt(dx * dx + dy * dy);
                    if (len < MinStep)
                    {
                        continue;
                    }
                    float nx = -dy / len;
                    float ny = dx / len;
                    float za = Height(map, a) + Lift;
                    float zb = Height(map, b) + Lift;

                    Vec3 p0 = new Vec3(a.x - nx * HalfWidth, a.y - ny * HalfWidth, za);
                    Vec3 p1 = new Vec3(b.x - nx * HalfWidth, b.y - ny * HalfWidth, zb);
                    Vec3 p2 = new Vec3(b.x + nx * HalfWidth, b.y + ny * HalfWidth, zb);
                    Vec3 p3 = new Vec3(a.x + nx * HalfWidth, a.y + ny * HalfWidth, za);
                    lineGeo.AddQuadOriented(p0, p1, p2, p3, new Vec3(0f, 0f, 1f));
                    lineGeo.AddQuadOriented(p3, p2, p1, p0, new Vec3(0f, 0f, -1f));
                    segments++;
                }
                linesUsed++;
            }

            long geoMs = sw.ElapsedMilliseconds;
            sw.Restart();
            bed = lineGeo.Build("plain_white");
            rails = emptyGeo.Build("plain_white");
            sleepers = emptyGeo.Build("plain_white");
            long meshMs = sw.ElapsedMilliseconds;
            stats =
                $"lines={linesUsed} segments={segments}"
                + $" tris={lineGeo.TriangleCount}"
                + $" worstTri={lineGeo.MaxEdgeLength:F1}m@{lineGeo.MaxEdgeAt.x:F0},{lineGeo.MaxEdgeAt.y:F0}"
                + $" geoMs={geoMs} meshMs={meshMs}";
        }

        /// <summary>按固定间距重采样折线（含首尾点）。</summary>
        private static void Resample(List<Vec2> pts, float step, List<Vec2> outPts)
        {
            outPts.Clear();
            outPts.Add(pts[0]);
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
                float t = carry;
                while (t + step <= seg)
                {
                    t += step;
                    float f = t / seg;
                    outPts.Add(new Vec2(a.x + dx * f, a.y + dy * f));
                }
                carry = t - seg;
            }
            Vec2 last = pts[pts.Count - 1];
            if (Dist(outPts[outPts.Count - 1], last) > MinStep)
            {
                outPts.Add(last);
            }
        }

        private static float Height(IMapScene map, Vec2 p)
        {
            var pos = new CampaignVec2(p, true);
            float h = 0f;
            map.GetHeightAtPoint(in pos, ref h);
            return h;
        }

        private static float Dist(Vec2 a, Vec2 b)
        {
            float dx = a.x - b.x;
            float dy = a.y - b.y;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }
    }
}
