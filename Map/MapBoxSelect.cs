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
        private static bool _leftDown;          // v4.75l: 左键按住中(按下即关相机输入, 根除框选起手漏帧)
        private static Vec2 _startPixel;
        private static Vec2 _lastPanPixel;
        private static bool _panning;
        private static bool _rightDown;
        private static bool _rightStartedOnPanel;   // v4.213: 本次右键手势起点在面板上 -> 整个手势作废
        private static bool _leftStartedOnPanel;    // v4.240: 本次左键手势起点在面板上 -> 整个手势作废
        private static bool _panLogged;
        private static bool _downLogged;
        private static bool _relLogged;
        private static float _panAccum;
        private static bool _viewNullLogged;
        private static bool _camInputOn = true;

        // 上一次左键释放是否为"单击"(非框选拖动) —— 供自研的"点击地面=部队移动"使用
        internal static bool LastReleaseWasClick;
        internal static void ClearClickFlag() { LastReleaseWasClick = false; }

        // 鼠标光标附近找一支部队(不依赖原版悬停拾取; 遍历部队名板位置)
        internal static MobileParty FindPartyAtCursor(float maxPx)
        {
            try
            {
                var mixin = PartyNameplatesVMMixin.Instance;
                var mgr = mixin != null ? mixin.Manager : null;
                if (mgr == null || mgr.Nameplates == null) return null;
                var m = TaleWorlds.InputSystem.Input.MousePositionPixel;
                MobileParty best = null;
                float bestD = maxPx * maxPx;
                foreach (var np in mgr.Nameplates)
                {
                    if (np == null || np.Party == null) continue;
                    var pos = np.Position;   // 名板左上角
                    float cx = pos.X + 60f, cy = pos.Y + 20f;
                    float d1 = (pos.X - m.X) * (pos.X - m.X) + (pos.Y - m.Y) * (pos.Y - m.Y);
                    float d2 = (cx - m.X) * (cx - m.X) + (cy - m.Y) * (cy - m.Y);
                    float d = Math.Min(d1, d2);
                    if (d < bestD) { bestD = d; best = np.Party; }
                }
                return best;
            }
            catch { return null; }
        }

        internal static bool IsDragging { get { return _dragging; } }

        // 每帧兜底: 若不在框选拖动中而相机输入被关过, 立刻恢复
        // (原版 "ProcessCameraInput==false 会跳过 ProcessTravel" -> 会吞掉左键指挥移动)
        // v4.213: 面板打开时也照常恢复 —— v4.181 在这里把相机输入按住不放, 导致开页后
        //         ProcessCameraInput 永久为 false, WASD 镜头失灵(关页才恢复);
        //         页内鼠标不穿透改由 MapClickPatches.OnBeforeTick 清零 + Handle* 的 IsMouseOnPanel 判断保证。
        internal static void EnsureCameraInputOn()
        {
            if (_camInputOn) return;
            if (_leftDown)
            {
                // v4.80: 自愈 —— 左键按下标记残留(释放帧丢失, 如角色创建切地图的一帧)会导致
                //       相机输入永久关闭(WASD 失灵), 直到下一次完整点击才恢复。
                //       这里用物理键状态校正: 键已松开就复位标记并恢复输入。
                bool physicallyDown = false;
                try { physicallyDown = TaleWorlds.InputSystem.Input.IsKeyDown(TaleWorlds.InputSystem.InputKey.LeftMouseButton); } catch { }
                if (physicallyDown) return;
                _leftDown = false;
                DLog.Force("框选: 检测到左键释放帧丢失, 自动复位(相机输入恢复)");
            }
            SetCameraInput(true);
            DLog.Force("框选: 相机输入已自动恢复(防吞左键指挥)");
        }

        // 返回 true = 这次左键算"拖框"(不要当点击处理)
        internal static bool HandleLeftButton(bool pressed, bool down, bool released, Vec2 mouse)
        {
            try
            {
                // v4.240: 按下点落在自建页面上 -> 整个手势作废(不当地图单击/框选/指挥), 直到松手
                //   (原来只判"有面板打开就早退", 单击标记 LastReleaseWasClick 会保留开面板前的旧值:
                //    在页面里按下、拖到地图上松开 -> 被当成一次地图单击, 选中部队会被指挥走)
                if (pressed && (PanelScreen.IsMouseOnPanel() || PanelScreen.AnyOpen))
                {
                    _leftStartedOnPanel = true;
                    ClearClickFlag();
                    _dragging = false; _leftDown = false; _panning = false;
                    return false;
                }
                if (_leftStartedOnPanel)
                {
                    if (released) _leftStartedOnPanel = false;
                    ClearClickFlag();
                    return true;   // 当作拖动: 调用方据此跳过地面单击
                }
                // 面板打开时地图左键不吃鼠标(右键见 HandleRightButton v4.213)
                if (PanelScreen.AnyOpen)
                {
                    _dragging = false; _leftDown = false; _panning = false; _rightDown = false;
                    _middleRotating = false; _middleDragging = false;
                    ClearClickFlag();
                    return false;
                }
                if (pressed)
                {
                    _startPixel = mouse;
                    _dragging = false;
                    _leftDown = true;
                    // v4.75l: 按下瞬间就关掉原版相机输入 -> 根除"框选起手一瞬间移动屏幕"老 bug
                    // (原版判定"左键拖动平移"比我们的 20px 框选阈值更早, 关晚了会漏 1-2 帧真实位移;
                    //  点击链已全部由自研逻辑兜底: 选国 HandlePickClick / 接管后 HandleLeftClickGround,
                    //  不再依赖原版 ProcessTravel 的点击移动)
                    SetCameraInput(false);
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
                    LastReleaseWasClick = !wasDragging;
                    if (wasDragging)
                    {
                        SelectInBox();
                        BoxVisible = false;
                        _dragging = false;
                    }
                    _leftDown = false;
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
                _camInputOn = enabled;
                var view = NationalWillCamera.View;
                if (view != null) NationalWillCamera.SetProcessCameraInput(view, enabled);
                else DLog.Force("框选: 设置相机输入(" + enabled + ") 失败: 视图为空");
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
        private static int _moveLog;

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
                // v4.79j: 场景未就绪(射线取不到地面点)时回退为"鼠标位移平移" -> 开局即可拖动地图
                if (cur.Length < 0.001f || PressGround.Length < 0.001f)
                {
                    float mx = TaleWorlds.InputSystem.Input.MouseMoveX;
                    float my = TaleWorlds.InputSystem.Input.MouseMoveY;
                    if (Math.Abs(mx) > 0.01f || Math.Abs(my) > 0.01f)
                    {
                        PanBy(-mx, -my);   // 方向与"抓取跟手"一致(拖右 -> 目标左移)
                        if (!_panLogged)
                        {
                            _panLogged = true;
                            DLog.Force("右键平移: 射线未就绪, 回退位移平移(开局可用)");
                        }
                    }
                    return;
                }
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
                var target = NationalWillCamera.GetIdealTarget(view);
                float dx = cur.x - PressGround.x;
                float dy = cur.y - PressGround.y;
                target.x -= dx;
                target.y -= dy;
                NationalWillCamera.SetIdealTarget(view, target);
                NationalWillCamera.SetCameraTarget(view, target);   // 立即跟手
                // v4.80: 限频日志 + 读回校验(诊断"设置了目标但画面不动")
                _moveLog++;
                if (_moveLog % 30 == 0)
                {
                    var back = NationalWillCamera.GetIdealTarget(view);
                    var cam = NationalWillCamera.GetCameraTargetValue(view);
                    var mp = TaleWorlds.InputSystem.Input.MousePositionPixel;
                    DLog.Force("右键平移: 修正=(" + dx.ToString("F1") + "," + dy.ToString("F1") + ") 写入=("
                        + target.x.ToString("F0") + "," + target.y.ToString("F0") + ") 读回=("
                        + back.x.ToString("F0") + "," + back.y.ToString("F0") + ") 相机=("
                        + cam.x.ToString("F0") + "," + cam.y.ToString("F0") + ") 鼠标=("
                        + mp.X.ToString("F0") + "," + mp.Y.ToString("F0") + ") 抓取点=("
                        + PressGround.x.ToString("F0") + "," + PressGround.y.ToString("F0") + ") 当前点=("
                        + cur.x.ToString("F0") + "," + cur.y.ToString("F0") + ")");
                }
            }
            catch { }
        }

        // 原版左键拖动取"按下点"的方式: 鼠标射线(近点/远点) + 地形射线检测
        internal static Vec3 CaptureGroundPoint()
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
                // v4.180: 面板打开时地图中键不吃鼠标
                if (PanelScreen.AnyOpen)
                {
                    _dragging = false; _leftDown = false; _panning = false; _rightDown = false;
                    _middleRotating = false; _middleDragging = false;
                    return;
                }
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
                // v4.213: 只有"鼠标落在自建页面上"才吃右键; 起点在页面上 -> 整个手势作废(不穿透)
                if (pressed && PanelScreen.IsMouseOnPanel())
                {
                    _rightStartedOnPanel = true;
                    _panning = false;
                    _rightDown = false;
                    _dragging = false; _leftDown = false;
                    _middleRotating = false; _middleDragging = false;
                    return false;
                }
                if (_rightStartedOnPanel)
                {
                    _panning = false;
                    _rightDown = false;
                    if (released) _rightStartedOnPanel = false;
                    return true;   // 当作拖动: 调用方据此跳过右键菜单
                }
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
