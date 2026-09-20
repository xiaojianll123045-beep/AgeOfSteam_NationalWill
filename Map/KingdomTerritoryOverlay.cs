using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using SandBox.View.Map;

namespace FeudalInternalAffairs
{
    // 政治地图覆盖层(领土填色):
    //   1. 生成一张贴图: 每个像素 = 该地所属王国的旗帜主色, 透明度 = 缩放档位
    //   2. 用贴图做一个关闭光照/阴影的半透明材质
    //   3. 建一张覆盖全图的四边形网格(贴图铺上去), 挂到实体上放进地图场景
    //   4. 缩放时按档位切换贴图(缓存当前档), 关掉时隐藏实体并恢复地图雪贴图
    // 接口: Texture.CreateFromByteArray / Material.CreateCopy + SetTexture / Mesh.CreateMeshWithMaterial + MetaMesh
    internal static class KingdomTerritoryOverlay
    {
        private const int TexWidth = 768;          // 贴图宽度(像素); 高度按地图比例
        private const int LandRowsPerTick = 12;    // 陆地掩码: 每帧行数(调用引擎, 只能主线程)
        private const int OwnerRowsPerTick = 48;   // 归属: 每帧行数(纯托管, 可并行)
        private const int AlphaSteps = 12;         // 透明度档位
        private const string BaseMaterial = "plain_white_noshadow_alpha";

        private static Scene _scene;
        private static GameEntity _entity;
        private static Material _material;
        private static Texture _texture;
        private static Mesh _mesh;

        private static int _w, _h;
        private static Vec2 _min, _max;
        private static int[] _owner;               // 每像素: 最近定居点索引(-1=无主/水面)
        private static byte[] _landHalf;           // 半分辨率陆地掩码(引擎查询很贵, 所以只查一半)
        private static int _landHalfRows;
        private static bool _landDone;
        private static int _builtRows;
        private static bool _built;
        private static int _bucket = -1;
        private static int _dataVer = int.MinValue;   // v4.70: 数据模式的贴图版本(切换/刷新时重建)
        private static bool _dataColors;              // 当前贴图是否为数据色
        private static bool _visible;
        private static bool _snowSaved;
        private static Texture _savedSnow;

        // 覆盖层已经能显示了吗
        internal static bool Ready { get { return _entity != null && _texture != null && _visible; } }

        // 覆盖层是否彻底失败(材质缺失等), 失败时才用色块兜底
        internal static bool Failed { get; private set; }

        // 各国领土的质心与半径(给地图上的国名用: 保证落在自己领土里, 字号按领土大小自适应)
        internal struct KingdomLabelInfo
        {
            public Kingdom K;
            public float X, Y, Radius;
        }
        internal static readonly List<KingdomLabelInfo> KingdomLabels = new List<KingdomLabelInfo>();

        internal static void MarkDirty()
        {
            // 只重建归属(陆地掩码与领土无关, 保留)
            _built = false;
            _builtRows = 0;
            _owner = null;
            _bucket = -1;
        }

        internal static void Reset()
        {
            _scene = null;
            _entity = null;
            _material = null;
            _texture = null;
            _mesh = null;
            _visible = false;
            _snowSaved = false;
            _savedSnow = null;
            Failed = false;
            _landHalf = null;
            _landHalfRows = 0;
            _landDone = false;
            KingdomLabels.Clear();
            MarkDirty();
        }

        internal static void Tick(float alpha)
        {
            try
            {
                if (Campaign.Current == null) return;
                var mapScreen = MapScreen.Instance;
                if (mapScreen == null) return;
                var scene = mapScreen.MapScene;
                if (scene == null) return;

                if (!ReferenceEquals(_scene, scene))
                {
                    // 场景换了(读档/新战役): 旧对象全失效
                    _scene = scene;
                    _entity = null;
                    _material = null;
                    _texture = null;
                    _mesh = null;
                    _visible = false;
                    _snowSaved = false;
                    _savedSnow = null;
                    MarkDirty();
                }

                // 数据尽早建(与是否显示无关): 开局地图一出现就开始算, 别等玩家拉远
                EnsureSize();
                if (!_landDone) BuildLandRows(scene, LandRowsPerTick);
                if (_landDone && !_built) BuildOwnerRows(OwnerRowsPerTick);

                if (alpha <= 0.01f)
                {
                    SetVisible(false);
                    return;
                }
                if (!_built) return;   // 还没建完, 下一帧继续

                EnsureObjects(scene);
                if (_entity == null) return;

                // v4.70: 数据模式(繁荣/驻军/...)= 同源覆盖层, 颜色按数据; 政治模式按缩放档位
                bool dataMode = !MapDataMode.IsPolitical;
                if (dataMode)
                {
                    if (_dataVer != MapDataMode.Version)
                    {
                        _dataVer = MapDataMode.Version;
                        _dataColors = true;
                        BuildTexture(0, true);
                    }
                }
                else
                {
                    int bucket = Math.Min(AlphaSteps - 1, (int)(alpha * AlphaSteps));
                    if (bucket != _bucket || _dataColors)
                    {
                        _bucket = bucket;
                        _dataColors = false;
                        BuildTexture(bucket, false);
                    }
                }
                SetVisible(true);
            }
            catch (Exception ex) { DLog.Force("政治地图覆盖层异常: " + ex.Message); }
        }

        private static void EnsureSize()
        {
            if (_owner != null) return;
            _min = Campaign.MapMinimumPosition;
            _max = Campaign.MapMaximumPosition;
            float spanX = _max.x - _min.x;
            float spanY = _max.y - _min.y;
            if (spanX <= 0.01f || spanY <= 0.01f)
            {
                _w = _h = 0;
                _owner = new int[0];
                _landHalf = new byte[0];
                _landDone = true;
                _built = true;
                return;
            }
            _w = TexWidth;
            _h = Math.Max(1, Math.Min(_w, (int)Math.Round(_w * spanY / spanX)));
            _owner = new int[_w * _h];
            _landHalf = null;
            _landHalfRows = 0;
            _landDone = false;
            _builtRows = 0;
            _built = false;
            // 诊断: 地图范围与定居点坐标是否同一空间
            DLog.Force("政治地图: 范围 min=(" + _min.x.ToString("F0") + "," + _min.y.ToString("F0")
                + ") max=(" + _max.x.ToString("F0") + "," + _max.y.ToString("F0") + ") 贴图 " + _w + "x" + _h);
            try
            {
                TerritoryData.Ensure();
                if (TerritoryData.PointCount > 0)
                    DLog.Force("政治地图: 首个定居点=(" + TerritoryData.PointX(0).ToString("F0")
                        + "," + TerritoryData.PointY(0).ToString("F0") + ") 共 " + TerritoryData.PointCount + " 个点");
            }
            catch { }
        }

        // ===== 阶段1: 陆地掩码(半分辨率; 引擎查询只能在主线程, 所以不能并行) =====
        private static void BuildLandRows(Scene scene, int rows)
        {
            if (_w <= 0 || _h <= 0) { _landDone = true; return; }
            int hw = (_w + 1) / 2;
            int hh = (_h + 1) / 2;
            if (_landHalf == null) { _landHalf = new byte[hw * hh]; _landHalfRows = 0; }
            int end = Math.Min(hh, _landHalfRows + rows);
            float spanX = _max.x - _min.x;
            float spanY = _max.y - _min.y;
            for (int py = _landHalfRows; py < end; py++)
            {
                float wy = _min.y + (py + 0.5f) / hh * spanY;
                for (int px = 0; px < hw; px++)
                {
                    float wx = _min.x + (px + 0.5f) / hw * spanX;
                    _landHalf[py * hw + px] = (byte)(IsLand(scene, wx, wy) ? 1 : 0);
                }
            }
            _landHalfRows = end;
            if (_landHalfRows >= hh)
            {
                _landDone = true;
                DLog.Force("政治地图: 陆地掩码已生成 " + hw + "x" + hh + "(半分辨率)");
            }
        }

        // 某像素是否陆地(从半分辨率掩码取)
        private static bool LandAt(int px, int py)
        {
            if (_landHalf == null) return false;
            int hw = (_w + 1) / 2;
            int hh = (_h + 1) / 2;
            int x = px >> 1;
            int y = py >> 1;
            if (x >= hw) x = hw - 1;
            if (y >= hh) y = hh - 1;
            return _landHalf[y * hw + x] != 0;
        }

        // ===== 阶段2: 归属(纯托管, 可并行) =====
        private static void BuildOwnerRows(int rows)
        {
            if (_owner == null || _w <= 0 || _h <= 0) { _built = true; return; }
            int start = _builtRows;
            int end = Math.Min(_h, start + rows);
            if (end <= start) { _built = true; return; }
            try { TerritoryData.Ensure(); } catch { }

            bool threaded = false;
            if (!DLog.Flag("nothreads") && Environment.ProcessorCount > 1)
            {
                try
                {
                    System.Threading.Tasks.Parallel.For(start, end, BuildOwnerRow);
                    threaded = true;
                }
                catch { threaded = false; }
            }
            if (!threaded)
            {
                for (int py = start; py < end; py++) BuildOwnerRow(py);
            }

            _builtRows = end;
            if (_builtRows >= _h)
            {
                _built = true;
                DLog.Force("政治地图: 归属数据已生成 " + _w + "x" + _h
                    + " (定居点 " + TerritoryData.PointCount + " 个, 线程="
                    + (threaded ? Environment.ProcessorCount.ToString() : "单线程") + ")");
                ComputeKingdomLabels();
            }
        }

        // 用领土像素算每国质心与等效半径(国名放在自己领土中心, 大小按领土)
        private static void ComputeKingdomLabels()
        {
            KingdomLabels.Clear();
            try
            {
                if (_owner == null || _w <= 0 || _h <= 0) return;
                float spanX = _max.x - _min.x;
                float spanY = _max.y - _min.y;
                var sums = new Dictionary<Kingdom, double[]>();
                for (int i = 0; i < _owner.Length; i++)
                {
                    int pi = _owner[i];
                    if (pi < 0) continue;
                    int px = i % _w, py = i / _w;
                    if (!LandAt(px, py)) continue;
                    var k = TerritoryData.PointKingdom(pi);
                    if (k == null) continue;
                    double[] s;
                    if (!sums.TryGetValue(k, out s)) { s = new double[3]; sums[k] = s; }
                    s[0] += _min.x + (px + 0.5f) / _w * spanX;
                    s[1] += _min.y + (py + 0.5f) / _h * spanY;
                    s[2] += 1.0;
                }
                float texelArea = (spanX / _w) * (spanY / _h);
                foreach (var kv in sums)
                {
                    double n = kv.Value[2];
                    if (n < 4.0) continue;
                    float cx = (float)(kv.Value[0] / n);
                    float cy = (float)(kv.Value[1] / n);
                    float radius = (float)Math.Sqrt(n * texelArea / Math.PI);
                    KingdomLabels.Add(new KingdomLabelInfo { K = kv.Key, X = cx, Y = cy, Radius = radius });
                }
                DLog.Force("政治地图: 国名标签已计算 " + KingdomLabels.Count + " 个(领土质心)");
            }
            catch { }
        }

        private static void BuildOwnerRow(int py)
        {
            float spanX = _max.x - _min.x;
            float spanY = _max.y - _min.y;
            float wy = _min.y + (py + 0.5f) / _h * spanY;
            for (int px = 0; px < _w; px++)
            {
                int idx = py * _w + px;
                if (!LandAt(px, py)) { _owner[idx] = -1; continue; }
                float wx = _min.x + (px + 0.5f) / _w * spanX;
                _owner[idx] = TerritoryData.OwnerPointAt(wx, wy);   // 最近定居点的索引
            }
        }

        // 陆地判定: 地形高度有效, 且水面不高于地面(抄的是原版地形/水面接口, 不是抄代码)
        private static bool IsLand(Scene scene, float x, float y)
        {
            try
            {
                var p = new Vec2(x, y);
                float h = scene.GetTerrainHeight(p, false);
                if (float.IsNaN(h)) return false;
                float water = scene.GetWaterLevelAtPosition(p, true, true);
                if (float.IsNaN(water)) return false;
                return water <= h + 0.05f;
            }
            catch { return false; }
        }

        private static void EnsureObjects(Scene scene)
        {
            if (_entity != null) return;
            var baseMat = Material.GetFromResource(BaseMaterial);
            if (baseMat == null) baseMat = Material.GetFromResource("show_texture");
            if (baseMat == null) baseMat = Material.GetFromResource("decal_material");
            if (baseMat == null)
            {
                Failed = true;
                DLog.Force("政治地图: 找不到基础材质(" + BaseMaterial + ")");
                _built = true;
                return;
            }

            _material = baseMat.CreateCopy();
            _material.Name = "FeudalTerritoryMaterial";
            _material.SetAlphaBlendMode(Material.MBAlphaBlendMode.Modulate);
            _material.UsingDynamicLight = false;
            _material.UsingSunLight = false;
            _material.UsingSpecular = false;
            _material.UsingSpecularMap = false;
            _material.UsingEnvironmentMap = false;
            _material.UsingFresnel = false;
            _material.IsSunShadowReceiver = false;
            _material.IsDynamicShadowReceiver = false;
            _material.Flags |= (MaterialFlags)1048878;

            _mesh = Mesh.CreateMeshWithMaterial(_material);
            _mesh.Name = "FeudalTerritoryQuad";
            _mesh.CullingMode = (MBMeshCullingMode)0;
            _mesh.Color = 0xFFFFFFFFu;
            _mesh.SetMeshRenderOrder(250);

            var normal = new Vec3(0f, 0f, 1f, -1f);
            var handle = _mesh.LockEditDataWrite();
            int c0 = _mesh.AddFaceCorner(new Vec3(_min.x, _min.y, 0.5f, -1f), normal, new Vec2(0f, 1f), 0xFFFFFFFFu, handle);
            int c1 = _mesh.AddFaceCorner(new Vec3(_max.x, _min.y, 0.5f, -1f), normal, new Vec2(1f, 1f), 0xFFFFFFFFu, handle);
            int c2 = _mesh.AddFaceCorner(new Vec3(_max.x, _max.y, 0.5f, -1f), normal, new Vec2(1f, 0f), 0xFFFFFFFFu, handle);
            int c3 = _mesh.AddFaceCorner(new Vec3(_min.x, _max.y, 0.5f, -1f), normal, new Vec2(0f, 0f), 0xFFFFFFFFu, handle);
            _mesh.AddFace(c0, c1, c2, handle);
            _mesh.AddFace(c0, c2, c3, handle);
            _mesh.UnlockEditDataWrite(handle);

            var meta = MetaMesh.CreateMetaMesh("FeudalTerritorySurface");
            meta.AddMesh(_mesh);
            meta.RecomputeBoundingBox(true);

            _entity = GameEntity.CreateEmpty(scene, false, false, false);
            _entity.Name = "FeudalTerritoryOverlay";
            _entity.AddComponent(meta);
            _entity.SetVisibilityExcludeParents(false);
            DLog.Force("政治地图: 覆盖层网格已创建(贴图 " + _w + "x" + _h + ")");
        }

        private static void BuildTexture(int bucket, bool dataMode)
        {
            if (_owner == null || _material == null) return;
            int alphaByte;
            if (dataMode) alphaByte = 200;
            else
            {
                float a = (bucket + 1) / (float)AlphaSteps;
                alphaByte = Math.Max(1, Math.Min(255, (int)Math.Round(a * 255f)));
            }

            // 数据模式: 预计算每个定居点的"白->深绿"颜色(按全局最大值归一化; 激进模式取反)
            byte[] pRgb = null;
            if (dataMode)
            {
                int n = TerritoryData.PointCount;
                pRgb = new byte[n * 3];
                float max = Math.Max(1f, MapDataMode.MaxValue());
                for (int pi = 0; pi < n; pi++)
                {
                    var st = TerritoryData.PointSettlement(pi);
                    string dummy;
                    int v = MapDataMode.ValueOf(st, out dummy);
                    float t = v >= 0 ? v / max : 0f;
                    if (MapDataMode.Current == MapData.Radicals) t = 1f - t;
                    byte r, g, b;
                    MapDataMode.GradientRgb(t, out r, out g, out b);
                    pRgb[pi * 3] = r;
                    pRgb[pi * 3 + 1] = g;
                    pRgb[pi * 3 + 2] = b;
                }
            }

            var bytes = new byte[_w * _h * 4];
            for (int i = 0; i < _owner.Length; i++)
            {
                int pi = _owner[i];
                if (pi < 0) continue;
                if (!LandAt(i % _w, i / _w)) continue;   // 透明(无主/水面)
                byte r, g, b;
                if (dataMode)
                {
                    if (pRgb == null || pi * 3 + 2 >= pRgb.Length) continue;
                    r = pRgb[pi * 3];
                    g = pRgb[pi * 3 + 1];
                    b = pRgb[pi * 3 + 2];
                }
                else
                {
                    // 预转换好的 RGB(每个定居点只转一次, 这里不再做字符串转换)
                    if (!TerritoryData.PointRgb(pi, out r, out g, out b)) continue;
                }
                int o = i * 4;
                bytes[o] = r;
                bytes[o + 1] = g;
                bytes[o + 2] = b;
                bytes[o + 3] = (byte)alphaByte;
            }
            var tex = Texture.CreateFromByteArray(bytes, _w, _h);
            if (tex == null) return;
            tex.Name = "FeudalTerritoryTexture";
            tex.SetTextureAsAlwaysValid();
            _material.SetTexture(Material.MBTextureType.DiffuseMap, tex);
            var old = _texture;
            _texture = tex;
            if (old != null)
            {
                try { old.ReleaseAfterNumberOfFrames(3); } catch { }
            }
        }

        // 取王国颜色(RGB): 用旗帜主色(和城市头牌上的旗底色一致), 而不是暗色的 Kingdom.Color
        private static bool TryGetRgb(Kingdom k, out int r, out int g, out int b)
        {
            r = g = b = 0;
            try
            {
                uint color = k.Color;
                try
                {
                    var banner = k.Banner;
                    if (banner != null) color = banner.GetPrimaryColor();
                }
                catch { }
                string hex = Color.UIntToColorString(color);   // "RRGGBBAA"
                if (hex == null || hex.Length < 6) return false;
                r = Convert.ToInt32(hex.Substring(0, 2), 16);
                g = Convert.ToInt32(hex.Substring(2, 2), 16);
                b = Convert.ToInt32(hex.Substring(4, 2), 16);
                return true;
            }
            catch { return false; }
        }

        private static void SetVisible(bool visible)
        {
            if (_visible == visible) return;
            _visible = visible;
            try
            {
                if (_entity != null) _entity.SetVisibilityExcludeParents(visible);
            }
            catch { }

            // 覆盖层开着时关掉地图雪(否则雪会盖在色块上), 关掉后恢复
            try
            {
                var scene = _scene;
                if (scene == null) return;
                if (visible)
                {
                    if (!_snowSaved)
                    {
                        _snowSaved = true;
                        var ent = scene.GetFirstEntityWithScriptComponent<SnowAndRainTextureDefiner>();
                        var def = ent != null ? ent.GetFirstScriptOfType<SnowAndRainTextureDefiner>() : null;
                        _savedSnow = def != null ? def.SnowAndRainTexture : null;
                    }
                    if (_savedSnow != null) scene.SetDynamicSnowTexture(null);
                }
                else if (_snowSaved)
                {
                    _snowSaved = false;
                    if (_savedSnow != null) scene.SetDynamicSnowTexture(_savedSnow);
                    _savedSnow = null;
                }
            }
            catch { }
        }
    }
}
