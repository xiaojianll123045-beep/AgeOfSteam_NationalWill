using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using SandBox.ViewModelCollection.Nameplate;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace FeudalInternalAffairs
{
    // 一块领土色块(屏幕坐标, 由 TerritoryColorMode 计算, 名字板 VM 负责画)
    internal struct TintTile
    {
        public float X, Y, Size;
        public string Color;
    }

    // 地图上的大字国家名(领土中心)
    internal struct TintLabel
    {
        public string Id;      // v4.80: 稳定标识(Kingdom.StringId) —— 渲染端据此固定槽位, 避免列表顺序变动导致"标签换国"跳变
        public float X, Y;
        public string Text;
        public string Color;
        public int FontSize;
    }

    // 政治地图模式: 相机拉近到一定程度后给领土上色(国家旗帜背景色), 越近越不透明
    //   最大放大时: 隐藏军团/城市名牌与国界线, 只显示大大的国家名
    // 实现: 把领土网格格子投影到屏幕, 用实心方块(BlankWhiteSquare)填充 -> 纯色块, 无花纹
    internal static class TerritoryColorMode
    {
        // 原版相机距离范围: 最小 2.5(贴地), 最大 = Campaign.MapMaximumHeight(约 750, 看全图)
        // 方向: 拉远(看全图)才上色 -> 距离越大颜色越浓, 最远处完全不透明
        // 可用 flags 调整: colorstart=300 / colorfull=720
        private const float StartRatio = 0.50f;
        private const float FullRatio = 1.0f;
        private const float WorldCell = 6f;    // 领土格子大小(地图单位)
        private const int MaxTiles = 900;      // 色块数量上限

        internal static bool Active;
        internal static bool FullyZoomed;
        internal static bool HideNameplates;   // 相机距离超过阈值 -> 隐藏城市/军团名牌
        internal static float Alpha;
        internal static string CenterKingdomName = "";
        internal static string CenterKingdomColor = "#FFFFFFFF";
        internal static readonly List<TintTile> Tiles = new List<TintTile>();
        internal static readonly List<TintLabel> Labels = new List<TintLabel>();

        private static readonly Dictionary<long, Kingdom> _ownerCache = new Dictionary<long, Kingdom>();
        private static readonly Dictionary<int, string> _colorCache = new Dictionary<int, string>();
        private static FieldInfo _cameraField;
        private static MethodInfo _maxHeightGetter;
        private static bool _dirty = true;
        private static float _lastX = float.NaN, _lastY = float.NaN, _lastDist = -1f, _lastAlpha = -1f, _lastBearing = float.NaN;
        private static bool _bordersHidden;
        private static float _hideCityDist = -1f;
        private static readonly System.Diagnostics.Stopwatch _sw = new System.Diagnostics.Stopwatch();
        private static double _tickMsSum;
        private static int _tickCount;
        private static float _diagTimer;
        private static bool _loggedActive;

        internal static void MarkDirty()
        {
            _ownerCache.Clear();
            _dirty = true;
        }

        internal static void Reset()
        {
            Tiles.Clear();
            Labels.Clear();
            _ownerCache.Clear();
            _colorCache.Clear();
            _lastX = _lastY = float.NaN;
            _lastDist = -1f;
            _lastAlpha = -1f;
            _lastBearing = float.NaN;
            _bordersHidden = false;
            _loggedActive = false;
            _diagTimer = 0f;
            Active = false;
            FullyZoomed = false;
            Alpha = 0f;
            CenterKingdomName = "";
            _dirty = true;
        }

        internal static void Tick(float dt)
        {
            _sw.Restart();
            try
            {
                if (Campaign.Current == null) return;
                var mapScreen = SandBox.View.Map.MapScreen.Instance;
                if (mapScreen == null) return;
                var view = mapScreen.MapCameraView;
                if (view == null) return;

                if (DLog.Flag("nocolor"))
                {
                    if (Active) { Active = false; FullyZoomed = false; Alpha = 0f; Tiles.Clear(); }
                    return;
                }

                float maxH = 60f;
                try
                {
                    if (_maxHeightGetter == null)
                        _maxHeightGetter = AccessTools.PropertyGetter(typeof(SandBox.View.Map.MapCameraView), "MaximumCameraHeight");
                    if (_maxHeightGetter != null) maxH = (float)_maxHeightGetter.Invoke(view, null);
                }
                catch { }
                if (maxH < 10f) maxH = 10f;
                float startDist = DLog.FlagFloat("colorstart", maxH * StartRatio);
                float fullDist = DLog.FlagFloat("colorfull", maxH * FullRatio);
                if (fullDist < startDist + 4f) fullDist = startDist + 4f;

                float dist = view.CameraDistance;
                // v4.69: 非默认模式(无条件政治/数据模式) -> 恒显示, 不随缩放淡入
                // v4.75: 选国模式 -> 恒显示(国家名和国家颜色正常填充)
                bool always = MapDataMode.AlwaysOn || NationPickMode.Active;
                float a;
                bool full;
                if (always)
                {
                    a = 1f;
                    full = false;
                }
                else
                {
                    // 拉远(距离变大) -> 颜色变浓
                    a = (dist - startDist) / (fullDist - startDist);
                    if (a < 0f) a = 0f;
                    if (a > 1f) a = 1f;
                    full = a >= 0.999f;
                }
                Active = always || a > 0.01f;
                Alpha = a;
                FullyZoomed = full;

                // 拉远超过阈值就隐藏城市/军团名牌(仅"有条件政治"模式); 数据模式要始终看得到数值
                if (_hideCityDist < 0f) _hideCityDist = DLog.FlagFloat("hidecity", 700f);
                HideNameplates = !always && dist > _hideCityDist;

                if (MapDataMode.AlwaysOn)
                {
                    _dataTimer -= dt;
                    if (_dataTimer <= 0f) { _dataTimer = 30f; MapDataMode.MarkDirty(); }   // v4.70: 30 秒刷新一次(贴图重建成本高)
                }
                if (Active && !_loggedActive)
                {
                    _loggedActive = true;
                    DLog.Force("政治地图: 开始上色(距离=" + dist.ToString("F1") + ")");
                }

                // 领土填色覆盖层(贴图网格): 自己处理显隐
                KingdomTerritoryOverlay.Tick(a);

                if (!Active)
                {
                    if (Tiles.Count > 0) Tiles.Clear();
                    if (Labels.Count > 0) Labels.Clear();
                    CenterKingdomName = "";
                    SettlementNameplatesVMMixin.SyncFromTerritory();   // v4.75m: 清空也要同步(顶层标签)
                    return;
                }

                // 相机中心 -> 决定大标题显示哪个国家
                Vec3 target = NationalWillCamera.GetIdealTarget(view);
                float cx = target.x, cy = target.y;
                float bearing = NationalWillCamera.GetBearing(view);
                var centerKingdom = OwnerCached(cx, cy);
                if (centerKingdom != null && centerKingdom.Name != null)
                {
                    CenterKingdomName = centerKingdom.Name.ToString();
                    try
                    {
                        // 注意: 游戏 UI 颜色是 8 位 RRGGBBAA, 少一位会崩
                        var c = Color.FromUint(centerKingdom.Color);
                        CenterKingdomColor = "#" + ToHex(c.Red) + ToHex(c.Green) + ToHex(c.Blue) + "FF";
                    }
                    catch { CenterKingdomColor = "#FFFFFFFF"; }
                }
                else
                {
                    CenterKingdomName = "";
                }

                _diagTimer -= dt;
                if (_diagTimer <= 0f)
                {
                    _diagTimer = 15f;
                    double avg = _tickCount > 0 ? _tickMsSum / _tickCount : 0;
                    _tickMsSum = 0; _tickCount = 0;
                    DLog.Force("上色诊断: 距离=" + dist.ToString("F1") + " 上限=" + maxH.ToString("F1")
                        + " alpha=" + a.ToString("F2") + " 色块=" + Tiles.Count
                        + " 隐藏名牌=" + HideNameplates
                        + " 平均耗时=" + avg.ToString("F2") + "ms"
                        + " 目标=(" + cx.ToString("F0") + "," + cy.ToString("F0") + ")");
                }

                bool moved = float.IsNaN(_lastX)
                             || Math.Abs(cx - _lastX) > 1.2f
                             || Math.Abs(cy - _lastY) > 1.2f
                             || Math.Abs(dist - _lastDist) > 0.6f
                             || Math.Abs(a - _lastAlpha) > 0.02f
                             || Math.Abs(bearing - _lastBearing) > 0.002f;   // 旋转也要重算(国名要跟着转)
                if (!_dirty && !moved)
                {
                    // v4.75e: 相机可能被别的逻辑平滑移动(如点城市名飞行/原版回主角),
                    // 色块可以不动, 但国名/数值标签必须每帧按真实相机重算坐标(否则错位)
                    RefreshLabelsOnly(a);
                    return;
                }
                _dirty = false;
                _lastX = cx; _lastY = cy; _lastDist = dist; _lastAlpha = a; _lastBearing = bearing;

                Paint(cx, cy, dist, a);
            }
            catch (Exception ex) { DLog.Force("政治地图 tick 异常: " + ex.Message); }
            finally
            {
                try
                {
                    _sw.Stop();
                    _tickMsSum += _sw.Elapsed.TotalMilliseconds;
                    _tickCount++;
                }
                catch { }
            }
        }

        private static string ToHex(float v)
        {
            int i = (int)Math.Round(Math.Max(0f, Math.Min(1f, v)) * 255f);
            return i.ToString("X2");
        }

        private static Kingdom OwnerCached(float x, float y)
        {
            long ix = (long)Math.Floor(x / WorldCell);
            long iy = (long)Math.Floor(y / WorldCell);
            long key = (ix << 32) ^ (iy & 0xFFFFFFFFL);
            Kingdom k;
            if (_ownerCache.TryGetValue(key, out k)) return k;
            k = TerritoryData.OwnerAt(x, y);
            if (_ownerCache.Count < 40000) _ownerCache[key] = k;
            return k;
        }

        private static string ColorString(Kingdom k, int alphaStep)
        {
            int key = k.GetHashCode() * 16 + alphaStep;
            string s;
            if (_colorCache.TryGetValue(key, out s)) return s;
            try
            {
                var c = Color.FromUint(k.Color);
                int alphaByte = (int)Math.Round(alphaStep / 15f * 255f);
                s = "#" + ToHex(c.Red) + ToHex(c.Green) + ToHex(c.Blue) + alphaByte.ToString("X2");
            }
            catch { s = "#FFFFFFFF"; }
            if (_colorCache.Count < 2048) _colorCache[key] = s;
            return s;
        }

        internal static Camera GetCamera()
        {
            try
            {
                var mixin = PartyNameplatesVMMixin.Instance;
                var manager = mixin != null ? mixin.Manager : null;
                if (manager == null) return null;
                if (_cameraField == null) _cameraField = AccessTools.Field(typeof(PartyNameplatesVM), "_mapCamera");
                return _cameraField != null ? _cameraField.GetValue(manager) as Camera : null;
            }
            catch { return null; }
        }

        // v4.79i: 供名牌 VM 每帧时机调用(与城市名牌同帧同序计算, 根治移动抖动)
        internal static void RefreshLabelsNow()
        {
            try
            {
                if (!Active) return;
                RefreshLabelsOnly(Alpha);
            }
            catch { }
        }

        // v4.75e: 只刷新标签(每帧, 跟随真实相机; 不重建色块)
        private static void RefreshLabelsOnly(float alpha)
        {
            try
            {
                Labels.Clear();
                var camera = GetCamera();
                if (camera == null) return;
                float sw = 1920f, sh = 1080f;
                try { sw = Screen.RealScreenResolutionWidth; sh = Screen.RealScreenResolutionHeight; } catch { }
                if (sw < 100f) sw = 1920f;
                if (sh < 100f) sh = 1080f;
                if (MapDataMode.IsPolitical) BuildLabels(camera, alpha, sw, sh);
                else BuildDataLabels(camera, sw, sh);
                SettlementNameplatesVMMixin.SyncFromTerritory();   // v4.75m: 每帧(label-only 帧)同步顶层标签
            }
            catch { }
        }

        private static void Paint(float cx, float cy, float dist, float alpha)
        {
            Tiles.Clear();
            Labels.Clear();
            var camera = GetCamera();
            if (camera == null) return;
            var mapScene = Campaign.Current != null ? Campaign.Current.MapSceneWrapper : null;

            // 格子大小随缩放自适应: 放得越大格子越细, 填充越细腻
            float cell = dist * 0.15f;
            if (cell < 1.5f) cell = 1.5f;
            if (cell > 8f) cell = 8f;
            float span = dist * 2.8f;
            while ((span / cell) * (span / cell) > MaxTiles) cell *= 1.2f;
            float half = span * 0.5f;
            int alphaStep = (int)Math.Round(alpha * 15f);

            float sw = 1920f, sh = 1080f;
            try { sw = Screen.RealScreenResolutionWidth; sh = Screen.RealScreenResolutionHeight; } catch { }
            if (sw < 100f) sw = 1920f;
            if (sh < 100f) sh = 1080f;

            // 覆盖层正常时完全不用色块(避免构建期间满屏方块); 只有覆盖层彻底失败才兜底
            // v4.70: 数据模式也走覆盖层同源着色(不再用色块拼, 视觉与政治地图一致)
            bool dataMode = !MapDataMode.IsPolitical;
            bool needTiles = KingdomTerritoryOverlay.Failed;
            if (dataMode) PrepareDataColors();

            for (float x = cx - half; needTiles && x <= cx + half; x += cell)
            {
                for (float y = cy - half; y <= cy + half; y += cell)
                {
                    var k = OwnerCached(x, y);
                    if (k == null) continue;
                    string color;
                    if (dataMode)
                    {
                        color = DataColorOf(TerritoryData.SettlementAt(x, y));
                        if (color == null) continue;
                    }
                    else color = ColorString(k, alphaStep);

                    float h = 0f;
                    Vec3 n = Vec3.Up;
                    try { if (mapScene != null) mapScene.GetTerrainHeightAndNormal(new Vec2(x, y), out h, out n); } catch { }
                    if (h < 0.05f) continue;   // 水面不上色

                    float sx = 0f, sy = 0f, w = 0f;
                    try { MBWindowManager.WorldToScreenInsideUsableArea(camera, new Vec3(x, y, h, 1f), ref sx, ref sy, ref w); }
                    catch { continue; }
                    if (sx < -300f || sy < -300f || sx > sw + 300f || sy > sh + 300f) continue;

                    float sx2 = sx + 20f, sy2 = 0f, w2 = 0f;
                    try { MBWindowManager.WorldToScreenInsideUsableArea(camera, new Vec3(x + cell, y, h, 1f), ref sx2, ref sy2, ref w2); }
                    catch { }
                    float size = Math.Abs(sx2 - sx);
                    if (size < 4f) size = 4f;
                    if (size > 320f) size = 320f;

                    Tiles.Add(new TintTile
                    {
                        X = sx - size * 0.5f,
                        Y = sy - size * 0.5f,
                        Size = size * 1.04f,
                        Color = color
                    });
                    if (Tiles.Count >= MaxTiles) break;
                }
                if (Tiles.Count >= MaxTiles) break;
            }

            if (dataMode) BuildDataLabels(camera, sw, sh);
            else BuildLabels(camera, alpha, sw, sh);
            SettlementNameplatesVMMixin.SyncFromTerritory();   // v4.75m: 重算帧也要同步顶层标签
        }

        // v4.69: 数据模式辅助(颜色缓存 + 名牌数值标签)
        private static readonly Dictionary<string, string> _dataColor = new Dictionary<string, string>();
        private static int _dataColorVer = int.MinValue;
        private static float _dataMax = 1f;
        private static float _dataTimer;

        private static void PrepareDataColors()
        {
            try
            {
                if (_dataColorVer == MapDataMode.Version) return;
                _dataColorVer = MapDataMode.Version;
                _dataColor.Clear();
                _dataMax = Math.Max(1f, MapDataMode.MaxValue());
            }
            catch { }
        }

        private static string DataColorOf(TaleWorlds.CampaignSystem.Settlements.Settlement st)
        {
            try
            {
                if (st == null) return null;
                string c;
                if (_dataColor.TryGetValue(st.StringId, out c)) return c;
                string dummy;
                int v = MapDataMode.ValueOf(st, out dummy);
                if (v < 0) c = null;
                else
                {
                    float t = v / _dataMax;
                    if (MapDataMode.Current == MapData.Radicals) t = 1f - t;   // 激进越少越绿
                    c = MapDataMode.Gradient(t) + "E6";
                }
                if (_dataColor.Count < 4096) _dataColor[st.StringId] = c;
                return c;
            }
            catch { return null; }
        }

        // 数据模式: 数值显示在"名牌上方"(用名牌 VM 的三档列表拿真实屏幕位置与档位高度)
        private static void BuildDataLabels(Camera camera, float sw, float sh)
        {
            try
            {
                var vm = SettlementNameplatesVMMixin.Instance;
                if (vm == null) return;
                AddDataLabelsFrom(vm.SmallNameplates, 22f, sw, sh);
                AddDataLabelsFrom(vm.MediumNameplates, 22f, sw, sh);
                AddDataLabelsFrom(vm.LargeNameplates, 22f, sw, sh);
            }
            catch { }
        }

        private static void AddDataLabelsFrom(System.Collections.Generic.IEnumerable<SandBox.ViewModelCollection.Nameplate.SettlementNameplateVM> list,
            float upOffset, float sw, float sh)
        {
            try
            {
                if (list == null) return;
                foreach (var np in list)
                {
                    if (np == null) continue;
                    var st = np.Settlement;
                    if (st == null) continue;
                    string txt;
                    int v = MapDataMode.ValueOf(st, out txt);
                    if (v < 0 || string.IsNullOrEmpty(txt)) continue;
                    var pos = np.Position;
                    if (pos.X < -200f || pos.Y < -200f || pos.X > sw + 200f || pos.Y > sh + 200f) continue;
                    Labels.Add(new TintLabel
                    {
                        Id = st.StringId,
                        X = pos.X - txt.Length * 4.2f,
                        Y = pos.Y - upOffset,
                        Text = txt,
                        Color = "#FFFFFFEE",
                        FontSize = 15
                    });
                }
            }
            catch { }
        }

        // 各国领土中心的大字国家名(位置=领土质心, 字号按领土大小自适应)
        private static void BuildLabels(Camera camera, float alpha, float sw, float sh)
        {
            try
            {
                var list = KingdomTerritoryOverlay.KingdomLabels;
                if (list.Count == 0) return;
                int alphaByte = (int)Math.Round((0.18f + 0.72f * alpha) * 255f);
                if (alphaByte > 255) alphaByte = 255;
                if (alphaByte < 0) alphaByte = 0;
                var mapScene = Campaign.Current != null ? Campaign.Current.MapSceneWrapper : null;
                foreach (var info in list)
                {
                    var k = info.K;
                    if (k == null) continue;
                    float h = 0f;
                    Vec3 nrm = Vec3.Up;
                    try { if (mapScene != null) mapScene.GetTerrainHeightAndNormal(new Vec2(info.X, info.Y), out h, out nrm); } catch { }
                    float sx = 0f, sy = 0f, w = 0f;
                    try { MBWindowManager.WorldToScreenInsideUsableArea(camera, new Vec3(info.X, info.Y, h, 1f), ref sx, ref sy, ref w); }
                    catch { continue; }
                    if (sx < -300f || sy < -300f || sx > sw + 300f || sy > sh + 300f) continue;
                    string name = k.Name != null ? k.Name.ToString() : "";
                    if (name.Length == 0) continue;
                    // 字号: 领土半径的 0.42 倍(42~150), 再随缩放提升
                    float baseSize = info.Radius * 0.42f;
                    if (baseSize < 42f) baseSize = 42f;
                    if (baseSize > 150f) baseSize = 150f;
                    int fs = (int)Math.Round(baseSize * (0.55f + 0.45f * alpha));
                    Labels.Add(new TintLabel
                    {
                        Id = k.StringId,
                        X = sx - name.Length * fs * 0.27f,
                        Y = sy - fs * 0.6f,
                        Text = name,
                        Color = "#FFFFFF" + alphaByte.ToString("X2"),
                        FontSize = fs
                    });
                }
            }
            catch { }
        }

        // 名牌隐藏: UI 读取 IsVisibleOnMap 时直接返回 false(VM 层, 兜底)
        [HarmonyPatch(typeof(NameplateVM), "get_IsVisibleOnMap")]
        internal static class HideNameplatesWhenZoomed
        {
            private static bool Prefix(ref bool __result)
            {
                if (HideNameplates)
                {
                    __result = false;
                    return false;
                }
                return true;
            }
        }

        // 名牌隐藏(真正生效的一层): 部件层每帧都会读 IsVisibleOnMap 来决定显隐,
        // VM 层只在值变化时才发通知(拦 VM 不可靠), 所以直接拦部件
        internal static class NameplateWidgetPatch
        {
            internal static void Apply(Harmony harmony)
            {
                try
                {
                    int n = 0;
                    var types = new[]
                    {
                        "TaleWorlds.MountAndBlade.GauntletUI.Widgets.Nameplate.PartyNameplateWidget",
                        "TaleWorlds.MountAndBlade.GauntletUI.Widgets.Nameplate.SettlementNameplateWidget"
                    };
                    foreach (var tn in types)
                    {
                        var t = AccessTools.TypeByName(tn);
                        if (t == null) { DLog.Force("名牌隐藏: 找不到类型 " + tn); continue; }
                        var getter = AccessTools.PropertyGetter(t, "IsVisibleOnMap");
                        if (getter == null) { DLog.Force("名牌隐藏: 找不到属性 " + tn); continue; }
                        harmony.Patch(getter, prefix: new HarmonyMethod(AccessTools.Method(typeof(NameplateWidgetPatch), "Prefix")));
                        n++;
                    }
                    DLog.Force("名牌隐藏补丁: 已挂载 " + n + " 个(部件层)");
                }
                catch (Exception ex) { DLog.Force("名牌隐藏补丁失败: " + ex.Message); }
            }

            private static bool Prefix(ref bool __result)
            {
                if (HideNameplates)
                {
                    __result = false;
                    return false;
                }
                return true;
            }
        }
    }
}
