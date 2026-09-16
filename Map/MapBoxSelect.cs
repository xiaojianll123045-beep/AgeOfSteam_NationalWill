using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Library;

namespace FeudalInternalAffairs
{
    // 左键拖框多选:
    //   左键按下 -> 记录起点
    //   拖动超过阈值 -> 进入框选(同时关掉相机输入)
    //   松开 -> 选中框内所有我方部队
    //   左键单击(没拖动) -> 交给原版(点选部队/定居点/地面)
    internal static class MapBoxSelect
    {
        internal static bool BoxVisible;
        internal static float BoxLeft;
        internal static float BoxTop;
        internal static float BoxWidth;
        internal static float BoxHeight;

        private static bool _dragging;
        private static Vec2 _startPixel;
        private static Vec2 _lastPanPixel;
        private static bool _panning;
        private static bool _rightDown;
        private static bool _panLogged;
        private static bool _downLogged;
        private static bool _relLogged;
        private static float _panAccum;
        private static bool _viewNullLogged;

        // 返回 true = 这次左键算"拖框"(不要当点击处理)
        internal static bool HandleLeftButton(bool pressed, bool down, bool released, Vec2 mouse)
        {
            try
            {
                if (pressed)
                {
                    _startPixel = mouse;
                    _dragging = false;
                    // 注意: 不能在这里关相机输入! 原版 HandleLeftMouseButtonClick 里
                    // "ProcessCameraInput == false -> 跳过 ProcessTravel", 会把左键指挥移动一起吞掉。
                    // 只在真正进入框选拖动时才关。
                    return false;
                }

                if (down)
                {
                    if (!_dragging)
                    {
                        float dx = mouse.X - _startPixel.X;
                        float dy = mouse.Y - _startPixel.Y;
                        if (dx * dx + dy * dy > 400f)   // 20 像素以上算拖动
                        {
                            _dragging = true;
                            SetCameraInput(false);   // 开始框选 -> 关掉相机输入(防原版拖动平移泄漏)
                        }
                    }
                    if (_dragging)
                    {
                        BoxVisible = true;
                        BoxLeft = Math.Min(_startPixel.X, mouse.X);
                        BoxTop = Math.Min(_startPixel.Y, mouse.Y);
                        BoxWidth = Math.Abs(mouse.X - _startPixel.X);
                        BoxHeight = Math.Abs(mouse.Y - _startPixel.Y);
                        return true;
                    }
                    return false;
                }

                if (released)
                {
                    bool wasDragging = _dragging;
                    if (wasDragging)
                    {
                        SelectInBox();
                        BoxVisible = false;
                        _dragging = false;
                    }
                    SetCameraInput(true);   // 松开恢复相机输入
                    return wasDragging;
                }
            }
            catch (Exception ex) { DLog.Force("框选异常: " + ex.Message); }
            return false;
        }

        private static void SetCameraInput(bool enabled)
        {
            try
            {
                var view = NationalWillCamera.View;
                if (view != null) NationalWillCamera.SetProcessCameraInput(view, enabled);
            }
            catch { }
        }

        // 右键拖动 = 平移地图(原版是旋转地图)
        // 返回 true = 这次右键算"拖动平移"(不要弹右键菜单)
        private static Vec2 _lastMiddlePixel;
        private static bool _middleRotating;
        private static bool _middleLogged;
        private static bool _middleDragging;
        private static float _middleAccum;

        // 右键是否已进入"拖动"状态(供按键重定向判断: 单击不重定向, 免得触发原版左键点击)
        internal static bool RightDragging { get { return _panning; } }
        // 右键是否按下(从按下那一刻就重定向: 原版要对准按下点, 中途重定向会瞬移)
        internal static bool RightDown { get { return _rightDown; } }
        // 右键按下瞬间取得的地面点(原版左键拖动的对准点, 拖动期间固定不变)
        internal static Vec3 PressGround;
        private static bool _havePrev;

        // 右键拖动 = 平移地图: 让"按下时抓住的地面点"一直跟在光标下(1:1 跟手, 与原版左键拖动一致)
        // 原理: 目标点 T 修正 -(G - W), G=当前光标地面点, W=按下时抓住的点;
        // 平地投影下 G(T)=T+常数, 所以一步就收敛, 不会抖也不会过冲。
        private static void ApplyGrabPan()
        {
            try
            {
                var view = NationalWillCamera.View;
                if (view == null) return;
                var cur = CaptureGroundPoint();
                if (cur.Length < 0.001f) return;
                if (!_havePrev)
                {
                    _havePrev = true;
                    if (!_panLogged)
                    {
                        _panLogged = true;
                        DLog.Force("右键平移: 跟手平移已生效");
                    }
                    return;
                }
                if (PressGround.Length < 0.001f) return;
                var target = NationalWillCamera.GetIdealTarget(view);
                target.x -= (cur.x - PressGround.x);
                target.y -= (cur.y - PressGround.y);
                NationalWillCamera.SetIdealTarget(view, target);
                NationalWillCamera.SetCameraTarget(view, target);   // 立即跟手
            }
            catch { }
        }

        // 原版左键拖动取"按下点"的方式: 鼠标射线(近点/远点) + 地形射线检测
        private static Vec3 CaptureGroundPoint()
        {
            try
            {
                var screen = SandBox.View.Map.MapScreen.Instance;
                if (screen == null) return Vec3.Zero;
                var sv = screen.SceneLayer != null ? screen.SceneLayer.SceneView : null;
                var scene = screen.MapScene;
                if (sv == null || scene == null) return Vec3.Zero;
                Vec3 near = Vec3.Zero, far = Vec3.Zero;
                sv.TranslateMouse(ref near, ref far, -1f);
                float dist;
                Vec3 point;
                if (scene.RayCastForClosestEntityOrTerrain(near, far, out dist, out point, 0.01f,
                        (TaleWorlds.Engine.BodyFlags)544323529))
                    return point;
            }
            catch { }
            return Vec3.Zero;
        }
        // 中键是否已进入"拖动"状态
        internal static bool MiddleDragging { get { return _middleDragging; } }

        // 中键拖动 = 旋转视角(横向拖动转镜头)
        internal static void HandleMiddleButton(bool pressed, bool down, bool released, Vec2 mouse)
        {
            try
            {
                var view = NationalWillCamera.View;
                if (view == null) return;
                if (pressed)
                {
                    _lastMiddlePixel = mouse;
                    _middleRotating = true;
                    _middleLogged = false;
                    _middleDragging = false;
                    _middleAccum = 0f;
                    return;
                }
                if (down && _middleRotating)
                {
                    // 旋转已交给引擎(按键重定向: 中键->原版右键), 这里只统计拖动状态和日志
                    float dx = TaleWorlds.InputSystem.Input.MouseMoveX;
                    _middleAccum += Math.Abs(dx);
                    if (!_middleDragging && _middleAccum > 10f) _middleDragging = true;
                    if (Math.Abs(dx) > 0.01f && !_middleLogged)
                    {
                        _middleLogged = true;
                        DLog.Force("中键旋转: 已生效(dx=" + dx.ToString("F1") + ")");
                    }
                    return;
                }
                if (released)
                {
                    _middleRotating = false;
                    _middleDragging = false;
                }
            }
            catch { }
        }

        internal static bool HandleRightButton(bool pressed, bool down, bool released, Vec2 mouse)
        {
            try
            {
                if (pressed)
                {
                    _startPixel = mouse;
                    _lastPanPixel = mouse;
                    _panning = false;
                    _rightDown = true;
                    _panAccum = 0f;
                    _downLogged = false;
                    _relLogged = false;
                    _havePrev = false;
                    // 原版左键拖动: 按下瞬间用鼠标射线取地面点(这个点就是"抓住"的点)
                    PressGround = CaptureGroundPoint();
                    DLog.Force("右键: 按下(地面点" + (PressGround.Length > 0.001f ? "已取到" : "未取到") + ")");
                    return false;
                }

                if (down)
                {
                    // 和原版一致: 光标离开按下点约 17 像素就算进入拖动
                    float dx = mouse.X - _startPixel.X;
                    float dy = mouse.Y - _startPixel.Y;
                    float mx = TaleWorlds.InputSystem.Input.MouseMoveX;
                    float my = TaleWorlds.InputSystem.Input.MouseMoveY;
                    _panAccum += Math.Abs(mx) + Math.Abs(my);
                    if (!_panning && (dx * dx + dy * dy > 300f || _panAccum > 20f))
                    {
                        _panning = true;
                        _panLogged = false;
                    }
                    if (_panning)
                    {
                        ApplyGrabPan();
                        return true;
                    }
                    return false;
                }

                if (released)
                {
                    bool wasPanning = _panning;
                    _panning = false;
                    _rightDown = false;
                    if (!_relLogged)
                    {
                        _relLogged = true;
                        DLog.Force("右键: 松开(平移=" + wasPanning + ")");
                    }
                    return wasPanning;
                }
            }
            catch (Exception ex) { DLog.Force("平移异常: " + ex.Message); }
            return false;
        }

        // 拖动方向: 抓住地图拖动, 视野朝反方向走
        private static void PanBy(float dx, float dy)
        {
            try
            {
                var view = NationalWillCamera.View;
                if (view == null)
                {
                    if (!_viewNullLogged) { _viewNullLogged = true; DLog.Force("平移失败: 拿不到相机视图"); }
                    return;
                }
                float distance = view.CameraDistance;
                if (distance <= 0f) distance = 150f;
                var target = NationalWillCamera.GetIdealTarget(view);

                // 1:1 跟手: 用投影反算"每像素多少世界单位"(光标下的地面点保持不动)
                float kx = distance * 0.0006f;
                float ky = distance * 0.0006f;
                try
                {
                    var camera = TerritoryColorMode.GetCamera();
                    if (camera != null)
                    {
                        float sx1 = 0f, sy1 = 0f, w1 = 0f, sx2 = 0f, sy2 = 0f, w2 = 0f, sx3 = 0f, sy3 = 0f, w3 = 0f;
                        TaleWorlds.MountAndBlade.MBWindowManager.WorldToScreenInsideUsableArea(
                            camera, target, ref sx1, ref sy1, ref w1);
                        TaleWorlds.MountAndBlade.MBWindowManager.WorldToScreenInsideUsableArea(
                            camera, target + new Vec3(1f, 0f, 0f, 1f), ref sx2, ref sy2, ref w2);
                        TaleWorlds.MountAndBlade.MBWindowManager.WorldToScreenInsideUsableArea(
                            camera, target + new Vec3(0f, 1f, 0f, 1f), ref sx3, ref sy3, ref w3);
                        float px = Math.Abs(sx2 - sx1);
                        float py = Math.Abs(sy3 - sy1);
                        if (px > 0.5f) kx = 1f / px;
                        if (py > 0.5f) ky = 1f / py;
                    }
                }
                catch { }

                // 方向按原版(相机跟随光标): 拖右 -> 目标点往右, 拖下 -> 目标点往南
                target.x += dx * kx;
                target.y -= dy * ky;
                NationalWillCamera.SetIdealTarget(view, target);
                NationalWillCamera.SetCameraTarget(view, target);   // 立即跟手
            }
            catch { }
        }

        // 鼠标是否落在某个"已选中部队"的选中圈范围内(圈半径约 24)
        internal static MobileParty HitSelectedRing()
        {
            try
            {
                var mixin = PartyNameplatesVMMixin.Instance;
                var manager = mixin != null ? mixin.Manager : null;
                if (manager == null || manager.Nameplates == null) return null;

                var mouse = TaleWorlds.InputSystem.Input.MousePositionPixel;
                MobileParty best = null;
                float bestDist = float.MaxValue;
                foreach (var np in manager.Nameplates)
                {
                    if (np == null || np.Party == null) continue;
                    if (!MapSelection.Is(np.Party)) continue;
                    var pos = np.Position;
                    float dx = mouse.X - pos.X;
                    float dy = mouse.Y - pos.Y;
                    float d2 = dx * dx + dy * dy;
                    if (d2 <= 26f * 26f && d2 < bestDist)
                    {
                        bestDist = d2;
                        best = np.Party;
                    }
                }
                return best;
            }
            catch { return null; }
        }

        // 用部队名字板的屏幕坐标做框选(和玩家看到的图标一致, 不受相机旋转影响)
        private static void SelectInBox()
        {
            try
            {
                var vm = PartyNameplatesVMMixin.Instance != null ? PartyNameplatesVMMixin.Instance.Manager : null;
                if (vm == null || vm.Nameplates == null) { MapSelection.Message("框选: 名字板未就绪"); return; }

                var picked = new List<MobileParty>();
                foreach (var np in vm.Nameplates)
                {
                    if (np == null || np.Party == null) continue;
                    if (!NationalWillOrders.IsCommandable(np.Party)) continue;
                    var pos = np.Position;
                    if (pos.X < BoxLeft || pos.X > BoxLeft + BoxWidth) continue;
                    if (pos.Y < BoxTop || pos.Y > BoxTop + BoxHeight) continue;
                    if (!picked.Contains(np.Party)) picked.Add(np.Party);
                }
                MapSelection.SelectMany(picked);
                DLog.Force("框选: 命中 " + picked.Count + " 支部队");
            }
            catch (Exception ex) { DLog.Force("框选计算异常: " + ex.Message); }
        }
    }
}
