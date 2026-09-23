using System;
using System.Collections.Generic;
using System.Reflection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Map;
using TaleWorlds.Engine;
using TaleWorlds.Library;

namespace FeudalInternalAffairs
{
    // v4.162: 铁路系统入口(移植自 _FeudalRailway, 改造为"按需建线 + 数据驱动视觉")
    //   每帧由 FeudalMapView 驱动: 首次 -> 建网(仅准备站点) + 按 Railways 数据重建视觉
    //   之后每帧 -> 蓝点沿线移动(速度按游戏时间尺度自适应)
    //   线变化(建成/中断) -> Railways 调用 RebuildVisual() 重建白线与蓝点
    internal static class RailSystem
    {
        private static RailNetwork _network;
        private static readonly List<RailTrain> _trains = new List<RailTrain>();
        private static readonly List<GameEntity> _trackEntities = new List<GameEntity>();
        private static Scene _scene;
        private static bool _inited;
        private static float _hoursPerSec = 1.6f;
        private static CampaignTime _lastTime = CampaignTime.Zero;
        private static bool _haveTime;
        private static bool _logged;

        private const string TrackPrefix = "fia_track";
        private const string PreviewPrefix = "fia_track_preview";
        private const uint PreviewColor = 0xFFFFC24Au;   // 琥珀色, 区别于正式白线
        private const uint OperatingColor = 0xFFFFFFFFu; // 运营线: 白
        private const uint BrokenColor = 0xFFFF3B30u;    // 中断线: 红
        private const uint BuildingColor = 0xFFFFB300u;  // 在建线: 琥珀(只画到 Progress 比例)

        internal static RailNetwork Network { get { return _network; } }
        internal static int TrainCount { get { return _trains.Count; } }

        /// <summary>新档/读档: 重置, 下帧重新准备。</summary>
        internal static void Reset()
        {
            _inited = false;
            for (int i = 0; i < _trains.Count; i++)
            {
                try { if (_trains[i] != null) _trains[i].Dispose(); } catch { }
            }
            _trains.Clear();
            _trackEntities.Clear();   // 旧场景随读档废弃; 同名实体由 RebuildVisual 兜底清扫
            _network = null;
            _scene = null;
            _haveTime = false;
            _logged = false;
        }

        internal static void Tick(float dt)
        {
            try
            {
                MeasureTimeScale(dt);
                if (!_inited)
                {
                    _inited = true;
                    try { EnsureNetwork(); RebuildVisual(); }
                    catch (Exception e) { DLog.Force("铁路: 初始化失败 " + e.Message); }
                }
                for (int i = _trains.Count - 1; i >= 0; i--)
                {
                    try { _trains[i].Tick(dt, Campaign.Current.MapSceneWrapper); }
                    catch (Exception e) { DLog.Force("铁路: 蓝点异常 " + e.Message); _trains.RemoveAt(i); }
                }
            }
            catch { }
        }

        /// <summary>准备网络(站点/水体/宏网格) —— 建线 API 依赖。</summary>
        internal static void EnsureNetwork()
        {
            try
            {
                if (_network != null) return;
                var map = Campaign.Current != null ? Campaign.Current.MapSceneWrapper : null;
                if (map == null) return;
                _scene = GetMapScene(map);
                _network = new RailNetwork(map, _scene);
                _network.Prepare();
                DLog.Force("铁路: 网络已准备(站点就绪)");
            }
            catch (Exception ex) { DLog.Force("铁路: 准备网络失败 " + ex.Message); }
        }

        /// <summary>按 Railways 数据重建铁轨与蓝点(白=运营, 红=中断, 琥珀=在建)。</summary>
        internal static void RebuildVisual()
        {
            try
            {
                EnsureNetwork();
                var map = Campaign.Current != null ? Campaign.Current.MapSceneWrapper : null;
                if (map == null || _scene == null) return;

                // 清旧实体: 先清预览, 再按 fia_track* 前缀全清(含重名残留; AddEntityWithMesh 无返回值)
                ClearPreview();
                ClearTrackEntities();
                for (int i = 0; i < _trains.Count; i++)
                {
                    try { _trains[i].Dispose(); } catch { }
                }
                _trains.Clear();

                // 分类: 运营 / 中断 / 在建
                var operating = new List<RailNetwork.Line>();
                var broken = new List<RailNetwork.Line>();
                var building = new List<RailNetwork.Line>();
                for (int i = 0; i < Railways.All.Count; i++)
                {
                    var rl = Railways.All[i];
                    if (rl == null || rl.Points == null || rl.Points.Count < 2) continue;
                    if (!rl.Built)
                    {
                        if (rl.Progress <= 0f) continue;
                        float ratio = rl.Progress > 1f ? 1f : rl.Progress;
                        var part = Truncate(rl, ratio);
                        if (part != null) building.Add(part);
                    }
                    else if (rl.Broken)
                    {
                        broken.Add(Copy(rl));
                    }
                    else
                    {
                        operating.Add(Copy(rl));
                    }
                }

                // 三类分开建 mesh, 用 Mesh.Color 上色(与选线预览同路径)
                if (operating.Count > 0) BuildTrack(map, operating, "", OperatingColor);
                if (broken.Count > 0) BuildTrack(map, broken, "_broken", BrokenColor);
                if (building.Count > 0) BuildTrack(map, building, "_build", BuildingColor);

                // 蓝点(仅运营线)
                for (int i = 0; i < operating.Count; i++)
                {
                    var train = new RailTrain();
                    train.SetSpeed(DotUnitsPerSecond());
                    train.SetProfile(operating[i].RailZ != null ? operating[i].RailZ : RailTrackMesh.ComputeProfile(map, operating[i].Points));
                    train.Attach(_scene, operating[i]);
                    _trains.Add(train);
                }
                if (!_logged || operating.Count > 0 || broken.Count > 0 || building.Count > 0)
                {
                    _logged = true;
                    DLog.Force("铁路: 视觉重建 -> 运营 " + operating.Count + " 条 / 中断 " + broken.Count + " 条 / 在建 " + building.Count + " 条, 蓝点 " + _trains.Count + " 个");
                }
            }
            catch (Exception ex) { DLog.Force("铁路: 重建视觉失败 " + ex.Message); }
        }

        private static RailNetwork.Line Copy(RailLine rl)
        {
            return new RailNetwork.Line
            {
                FromId = rl.FromId,
                ToId = rl.ToId,
                Points = rl.Points,
                Length = rl.Length
            };
        }

        /// <summary>在建线: 只取前 ratio(0~1) 比例的折线。</summary>
        private static RailNetwork.Line Truncate(RailLine rl, float ratio)
        {
            var src = rl.Points;
            if (src == null || src.Count < 2) return null;
            var pts = new List<Vec2>();
            pts.Add(src[0]);
            float target = ratio * rl.Length;
            float acc = 0f;
            for (int i = 1; i < src.Count; i++)
            {
                float dx = src[i].x - src[i - 1].x;
                float dy = src[i].y - src[i - 1].y;
                float seg = (float)Math.Sqrt(dx * dx + dy * dy);
                if (acc + seg >= target)
                {
                    float f = seg > 1e-4f ? (target - acc) / seg : 0f;
                    if (f < 0f) f = 0f;
                    if (f > 1f) f = 1f;
                    pts.Add(new Vec2(src[i - 1].x + dx * f, src[i - 1].y + dy * f));
                    break;
                }
                acc += seg;
                pts.Add(src[i]);
            }
            if (pts.Count < 2) return null;
            return new RailNetwork.Line
            {
                FromId = rl.FromId,
                ToId = rl.ToId,
                Points = pts,
                Length = ratio * rl.Length
            };
        }

        private static void BuildTrack(IMapScene map, List<RailNetwork.Line> lines, string suffix, uint color)
        {
            Mesh bedMesh, railMesh, sleeperMesh;
            string stats;
            RailTrackMesh.Build(map, lines, out bedMesh, out railMesh, out sleeperMesh, out stats);
            bedMesh.Color = color;
            railMesh.Color = color;
            sleeperMesh.Color = color;
            AddNamedEntity(bedMesh, "fia_track_line" + suffix);
            AddNamedEntity(railMesh, "fia_track_rails" + suffix);
            AddNamedEntity(sleeperMesh, "fia_track_sleepers" + suffix);
        }

        /// <summary>加实体: 先按名清掉同名旧实体, 保证重名实体不重复创建。</summary>
        private static void AddNamedEntity(Mesh mesh, string name)
        {
            if (_scene == null || mesh == null) return;
            try
            {
                mesh.Name = name;
                var old = _scene.GetFirstEntityWithName(name);
                int guard = 0;
                while (old != null && guard++ < 32)
                {
                    _scene.RemoveEntity(old, 0);
                    old = _scene.GetFirstEntityWithName(name);
                }
                MatrixFrame frame = MatrixFrame.Identity;
                _scene.AddEntityWithMesh(mesh, ref frame);
                var ent = _scene.GetFirstEntityWithName(name);
                if (ent != null) _trackEntities.Add(ent);
            }
            catch { }
        }

        /// <summary>清掉场景里全部 fia_track* 实体(含 preview 前缀); 同名兜底由 AddNamedEntity 负责。</summary>
        private static void ClearTrackEntities()
        {
            try
            {
                if (_scene != null)
                {
                    var all = new List<GameEntity>();
                    try { _scene.GetEntities(ref all); } catch { all.Clear(); }
                    for (int i = 0; i < all.Count; i++)
                    {
                        var ent = all[i];
                        if (ent == null) continue;
                        string name;
                        try { name = ent.Name ?? ""; } catch { continue; }
                        if (name.StartsWith(TrackPrefix, StringComparison.Ordinal))
                        {
                            try { _scene.RemoveEntity(ent, 0); } catch { }
                        }
                    }
                }
            }
            catch { }
            _trackEntities.Clear();
        }

        /// <summary>选线预览: 生成临时线并画成琥珀色预览实体(fia_track_preview*); 返回是否成功。</summary>
        internal static bool ShowPreview(string fromId, string toId)
        {
            try
            {
                ClearPreview();
                if (string.IsNullOrEmpty(fromId) || string.IsNullOrEmpty(toId) || fromId == toId) return false;
                EnsureNetwork();
                var map = Campaign.Current != null ? Campaign.Current.MapSceneWrapper : null;
                if (map == null || _scene == null || _network == null) return false;

                var line = _network.MakeLineByIds(fromId, toId);
                if (line == null || line.Points == null || line.Points.Count < 2) return false;

                var lines = new List<RailNetwork.Line>();
                lines.Add(line);
                Mesh bedMesh, railMesh, sleeperMesh;
                string stats;
                RailTrackMesh.Build(map, lines, out bedMesh, out railMesh, out sleeperMesh, out stats);
                bedMesh.Name = PreviewPrefix + "_line";
                railMesh.Name = PreviewPrefix + "_rails";
                sleeperMesh.Name = PreviewPrefix + "_sleepers";
                bedMesh.Color = PreviewColor;
                railMesh.Color = PreviewColor;
                sleeperMesh.Color = PreviewColor;
                MatrixFrame frame = MatrixFrame.Identity;
                _scene.AddEntityWithMesh(bedMesh, ref frame);
                _scene.AddEntityWithMesh(railMesh, ref frame);
                _scene.AddEntityWithMesh(sleeperMesh, ref frame);
                return true;
            }
            catch (Exception ex) { DLog.Force("铁路: 预览失败 " + ex.Message); return false; }
        }

        /// <summary>清除全部选线预览实体(fia_track_preview*)。</summary>
        internal static void ClearPreview()
        {
            try
            {
                if (_scene == null) return;
                var all = new List<GameEntity>();
                try { _scene.GetEntities(ref all); } catch { return; }
                for (int i = 0; i < all.Count; i++)
                {
                    var ent = all[i];
                    if (ent == null) continue;
                    string name;
                    try { name = ent.Name ?? ""; } catch { continue; }
                    if (name.StartsWith(PreviewPrefix, StringComparison.Ordinal))
                    {
                        try { _scene.RemoveEntity(ent, 0); } catch { }
                    }
                }
            }
            catch { }
        }

        /// <summary>10 个玩家速度单位(= 地图单位/游戏小时)换算成真实秒的世界速度。</summary>
        private static float DotUnitsPerSecond()
        {
            return 10f * _hoursPerSec;
        }

        private static void MeasureTimeScale(float dt)
        {
            try
            {
                CampaignTime now = CampaignTime.Now;
                if (_haveTime && dt > 1e-4f)
                {
                    float hours = (float)(now - _lastTime).ToHours;
                    if (hours > 0f && hours < 100f)
                    {
                        float r = hours / dt;
                        _hoursPerSec = _hoursPerSec * 0.9f + r * 0.1f;
                    }
                }
                _lastTime = now;
                _haveTime = true;
            }
            catch { }
        }

        private static Scene GetMapScene(IMapScene map)
        {
            if (map == null) return null;
            try
            {
                PropertyInfo prop = map.GetType().GetProperty("Scene", BindingFlags.Public | BindingFlags.Instance);
                if (prop == null) return null;
                return prop.GetValue(map) as Scene;
            }
            catch { return null; }
        }
    }
}
