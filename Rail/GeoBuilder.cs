using System;
using System.Collections.Generic;
using TaleWorlds.Engine;
using TaleWorlds.Library;

namespace FeudalInternalAffairs
{
    /// <summary>把三角形累积成一个游戏网格（运行时生成用，自动修正面朝向）。</summary>
    public class GeoBuilder
    {
        private readonly List<Vec3> _pos = new List<Vec3>();
        private readonly List<Vec2> _uv = new List<Vec2>();
        private readonly List<int> _idx = new List<int>();

        public int TriangleCount => _idx.Count / 3;

        /// <summary>最大三角形边长（诊断用：过大说明有拉飞的面）。</summary>
        public float MaxEdgeLength;
        public Vec3 MaxEdgeAt;

        /// <summary>顶点色 (0xAARRGGBB)。</summary>
        public uint Color = 0xFFFFFFFFu;

        public void AddTriangle(Vec3 a, Vec3 b, Vec3 c, Vec2 ua, Vec2 ub, Vec2 uc)
        {
            float e = Max3(Length(a, b), Length(b, c), Length(c, a));
            if (e > MaxEdgeLength)
            {
                MaxEdgeLength = e;
                MaxEdgeAt = a;
            }
            int i = _pos.Count;
            _pos.Add(a);
            _pos.Add(b);
            _pos.Add(c);
            _uv.Add(ua);
            _uv.Add(ub);
            _uv.Add(uc);
            _idx.Add(i);
            _idx.Add(i + 1);
            _idx.Add(i + 2);
        }

        public void AddQuad(Vec3 a, Vec3 b, Vec3 c, Vec3 d)
        {
            AddTriangle(a, b, c, new Vec2(0, 0), new Vec2(1, 0), new Vec2(1, 1));
            AddTriangle(a, c, d, new Vec2(0, 0), new Vec2(1, 1), new Vec2(0, 1));
        }

        /// <summary>按期望外法向自动修正顶点顺序。</summary>
        public void AddQuadOriented(Vec3 a, Vec3 b, Vec3 c, Vec3 d, Vec3 outward)
        {
            Vec3 n = Vec3.CrossProduct(b - a, c - b);
            if (Vec3.DotProduct(n, outward) < 0f)
            {
                AddQuad(a, d, c, b);
            }
            else
            {
                AddQuad(a, b, c, d);
            }
        }

        public void AddTriangleOriented(Vec3 a, Vec3 b, Vec3 c, Vec3 outward, Vec2 ua, Vec2 ub, Vec2 uc)
        {
            Vec3 n = Vec3.CrossProduct(b - a, c - a);
            if (Vec3.DotProduct(n, outward) < 0f)
            {
                AddTriangle(a, c, b, ua, uc, ub);
            }
            else
            {
                AddTriangle(a, b, c, ua, ub, uc);
            }
        }

        /// <summary>任意基向量的盒子。</summary>



        /// <summary>沿 Z 轴的圆锥（baseCenter 为底面中心）。</summary>
        public void AddConeZ(Vec3 baseCenter, float r0, float r1, float height, int segments = 16)
        {
            Vec3 top = baseCenter + new Vec3(0, 0, height);
            for (int i = 0; i < segments; i++)
            {
                float a0 = (float)(i * 2.0 * Math.PI / segments);
                float a1 = (float)((i + 1) * 2.0 * Math.PI / segments);
                var p0 = baseCenter + new Vec3((float)Math.Cos(a0) * r0, (float)Math.Sin(a0) * r0, 0);
                var p1 = baseCenter + new Vec3((float)Math.Cos(a1) * r0, (float)Math.Sin(a1) * r0, 0);
                var p2 = top + new Vec3((float)Math.Cos(a1) * r1, (float)Math.Sin(a1) * r1, 0);
                var p3 = top + new Vec3((float)Math.Cos(a0) * r1, (float)Math.Sin(a0) * r1, 0);
                Vec3 outward = ((p0 + p1 + p2 + p3) * 0.25f) - baseCenter + new Vec3(0, 0, -height * 0.5f);
                AddQuadOriented(p0, p1, p2, p3, outward);
                AddTriangleOriented(baseCenter, p0, p1, new Vec3(0, 0, -1), new Vec2(0.5f, 0.5f), new Vec2(0, 0), new Vec2(1, 0));
                // 顶盖：r1 不为 0 时是个圆面，必须封顶（否则从上往下看是空心的）
                if (r1 > 0.02f)
                {
                    AddTriangleOriented(top, p2, p3, new Vec3(0, 0, 1), new Vec2(0.5f, 0.5f), new Vec2(1, 0), new Vec2(0, 0));
                }
            }
        }

        /// <summary>车轮：外圈 + 辐条 + 轮毂（轴线沿 Y）。</summary>

        public Mesh Build(string materialName)
        {
            // MeshBuilder 纯托管累积顶点/面，Finalize 一次性批量上传（比逐个三角形调原生接口快几十倍）
            var builder = new MeshBuilder();
            for (int t = 0; t < _idx.Count; t += 3)
            {
                AddFaceWithNormal(builder, _idx[t], _idx[t + 1], _idx[t + 2]);
            }
            Mesh mesh = builder.Finalize();
            if (!string.IsNullOrEmpty(materialName))
            {
                mesh.SetMaterial(materialName);
            }
            mesh.Color = Color;
            mesh.UpdateBoundingBox();
            return mesh;
        }

        private static float Length(Vec3 a, Vec3 b)
        {
            float dx = a.x - b.x;
            float dy = a.y - b.y;
            float dz = a.z - b.z;
            return (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        private static float Max3(float a, float b, float c)
        {
            return Math.Max(a, Math.Max(b, c));
        }

        private void AddFaceWithNormal(MeshBuilder builder, int i0, int i1, int i2)
        {
            Vec3 a = _pos[i0];
            Vec3 b = _pos[i1];
            Vec3 c = _pos[i2];
            Vec3 n = Vec3.CrossProduct(b - a, c - a);
            float len = (float)Math.Sqrt(n.x * n.x + n.y * n.y + n.z * n.z);
            if (len > 1e-8f)
            {
                n = new Vec3(n.x / len, n.y / len, n.z / len);
            }
            else
            {
                n = new Vec3(0f, 0f, 1f);
            }
            int c0 = builder.AddFaceCorner(a, n, _uv[i0], Color);
            int c1 = builder.AddFaceCorner(b, n, _uv[i1], Color);
            int c2 = builder.AddFaceCorner(c, n, _uv[i2], Color);
            builder.AddFace(c0, c1, c2);
        }
    }
}
