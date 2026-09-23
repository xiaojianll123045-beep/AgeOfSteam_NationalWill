using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Map;
using TaleWorlds.Engine;
using TaleWorlds.Library;

namespace FeudalInternalAffairs
{
    /// <summary>沿铁路线行驶的列车（运行时几何 + CreateEmpty/AddMesh，与 MapBlockade 同路径）。</summary>
    public class RailTrain
    {
        private static int _placeLogCount;

        private static void Log(string msg)
        {
            try { DLog.Force("[铁路] " + msg); } catch { }
        }

        private readonly List<Vehicle> _vehicles = new List<Vehicle>();
        private RailNetwork.Line _line;
        private float[] _cum;
        private int _segHint = 1;   // 上次所在区段: 命中相邻段则免线性扫
        private float _s;
        private float _dir = 1f;
        private float[] _zRail;
        private float _speed = 4.0f;

        private class Vehicle
        {
            public readonly List<GameEntity> Parts = new List<GameEntity>();
            public float Offset;
        }

        public float HeightOffset = 0.14f;
        public string Status = "not spawned";
        public bool Ready => _vehicles.Count > 0 && _line != null;

        /// <summary>蓝点：CreateEmpty + AddMesh + SetLocalFrame（实测可渲染且拿得到句柄）。</summary>
        public void Attach(Scene scene, RailNetwork.Line line)
        {
            _line = line;
            BuildCumulative();
            var dot = new Vehicle { Offset = 0f };
            try
            {
                var geo = new GeoBuilder { Color = 0xFF2E6BFFu };
                geo.AddConeZ(new Vec3(0f, 0f, -0.125f), 0.2f, 0.2f, 0.25f, 16);   // 直径 0.4 米、高 0.25 米
                Mesh mesh = geo.Build("plain_white");
                mesh.Name = "fw_rail_dot";
                GameEntity entity = GameEntity.CreateEmpty(scene, true, false, false);
                if (entity != null)
                {
                    entity.AddMesh(mesh, true);
                    entity.SetVisibilityExcludeParents(true);
                    dot.Parts.Add(entity);
                }
            }
            catch
            {
            }
            _vehicles.Add(dot);
            Status = $"dot on {line.FromId}->{line.ToId} len={line.Length:F0} parts={dot.Parts.Count}";
            int hash = ((line.FromId ?? "").GetHashCode() + (line.ToId ?? "").GetHashCode()) & 0x7FFFFFFF;   // 不用 Math.Abs: int.MinValue 会抛异常
            float total = _cum[_cum.Length - 1];
            _s = Math.Max(1f, total * ((hash % 997) / 997f));
        }

        private void BuildCumulative()
        {
            _cum = new float[_line.Points.Count];
            for (int i = 1; i < _line.Points.Count; i++)
            {
                float dx = _line.Points[i].x - _line.Points[i - 1].x;
                float dy = _line.Points[i].y - _line.Points[i - 1].y;
                _cum[i] = _cum[i - 1] + (float)Math.Sqrt(dx * dx + dy * dy);
            }
            _segHint = 1;
        }

        public void SetSpeed(float speed)
        {
            _speed = speed;
        }

        /// <summary>清理蓝点实体(重建视觉时调用)。</summary>
        public void Dispose()
        {
            try
            {
                for (int i = 0; i < _vehicles.Count; i++)
                {
                    var v = _vehicles[i];
                    if (v == null) continue;
                    for (int p = 0; p < v.Parts.Count; p++)
                    {
                        try { if (v.Parts[p] != null) v.Parts[p].Remove(0); } catch { }
                    }
                }
                _vehicles.Clear();
            }
            catch { }
        }

        public void SetProfile(float[] zRail)
        {
            _zRail = zRail;
        }

        /// <summary>定位 s 所在区段(首个 cum[i] >= s, 上限末段; 结果与线性扫一致)。</summary>
        private int FindSeg(float s)
        {
            int last = _cum.Length - 1;
            if (last < 1)
            {
                return 1;
            }
            int i = _segHint;
            if (i < 1)
            {
                i = 1;
            }
            else if (i > last)
            {
                i = last;
            }
            if (_cum[i] >= s && (i == 1 || _cum[i - 1] < s))
            {
                _segHint = i;
                return i;
            }
            if (i + 1 <= last && _cum[i] < s && _cum[i + 1] >= s)
            {
                _segHint = i + 1;
                return i + 1;
            }
            if (i - 1 >= 1 && _cum[i - 1] >= s && (i - 1 == 1 || _cum[i - 2] < s))
            {
                _segHint = i - 1;
                return i - 1;
            }
            i = 1;
            while (i < last && _cum[i] < s)
            {
                i++;
            }
            _segHint = i;
            return i;
        }

        private float SampleRailZ(float s)
        {
            if (_zRail == null || _zRail.Length != _line.Points.Count)
            {
                return float.MinValue;
            }
            int i = FindSeg(s);
            float segLen = _cum[i] - _cum[i - 1];
            float f = segLen > 1e-4f ? (s - _cum[i - 1]) / segLen : 0f;
            return _zRail[i - 1] + (_zRail[i] - _zRail[i - 1]) * f;
        }

        public void Tick(float dt, IMapScene map)
        {
            if (!Ready)
            {
                return;
            }
            _s += _dir * _speed * dt;
            float total = _cum[_cum.Length - 1];
            float minS = 1f;
            if (_dir > 0 && _s > total)
            {
                _s = total;
                _dir = -1;
            }
            else if (_dir < 0 && _s < minS)
            {
                _s = minS;
                _dir = 1;
            }
            for (int i = 0; i < _vehicles.Count; i++)
            {
                Vehicle v = _vehicles[i];
                float s = _s - v.Offset; // 车厢永远挂在机车尾（煤水车）一侧
                if (s < 0f)
                {
                    s = 0f;
                }
                if (s > total)
                {
                    s = total;
                }
                float x, y, dx, dy;
                Sample(s, out x, out y, out dx, out dy);
                float railZ = SampleRailZ(s);
                float z = (railZ > float.MinValue ? railZ : SampleHeight(map, x, y)) + HeightOffset;
                foreach (GameEntity part in v.Parts)
                {
                    Place(part, x, y, z, dx, dy);
                }
            }
        }

        private void Sample(float s, out float x, out float y, out float dx, out float dy)
        {
            var pts = _line.Points;
            int i = FindSeg(s);
            float segLen = _cum[i] - _cum[i - 1];
            float f = segLen > 1e-4f ? (s - _cum[i - 1]) / segLen : 0f;
            Vec2 a = pts[i - 1];
            Vec2 b = pts[i];
            x = a.x + (b.x - a.x) * f;
            y = a.y + (b.y - a.y) * f;
            float len = (float)Math.Sqrt((b.x - a.x) * (b.x - a.x) + (b.y - a.y) * (b.y - a.y));
            dx = len > 1e-4f ? (b.x - a.x) / len : 1f;
            dy = len > 1e-4f ? (b.y - a.y) / len : 0f;
        }

        private static float SampleHeight(IMapScene map, float x, float y)
        {
            var pos = new CampaignVec2(new Vec2(x, y), true);
            float h = 0f;
            map.GetHeightAtPoint(in pos, ref h);
            return h;
        }

        private static void Place(GameEntity entity, float x, float y, float z, float dx, float dy)
        {
            if (entity == null)
            {
                return;
            }
            MatrixFrame frame;
            try
            {
                Mat3 rot = Mat3.CreateMat3WithForward(new Vec3(dx, dy, 0f));
                rot.RotateAboutUp(1.57079633f); // 引擎前向与模型 +X 相差 90°，绕 Z 补偿
                frame = new MatrixFrame(rot, new Vec3(x, y, z));
            }
            catch (Exception e)
            {
                if (_placeLogCount++ < 3)
                {
                    Log("Place: mat3/frame failed: " + e.Message);
                }
                return;
            }
            if (_placeLogCount == 0)
            {
                _placeLogCount++;
                Log("Place: using SetLocalFrame");
            }
            try
            {
                entity.SetLocalFrame(ref frame, false);
            }
            catch (Exception e)
            {
                if (_placeLogCount++ < 5)
                {
                    Log("Place: update failed: " + e.Message);
                }
            }
        }
    }
}
