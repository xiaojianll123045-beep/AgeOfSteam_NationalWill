using System;

namespace FeudalInternalAffairs
{
    // v4.146: 国策页改全屏面板(用户: 改造成类似政治页面那样的全屏)
    // v4.182: 照科技页重做 —— 横卡节点 / 粗棕曲线 + 当前路径青色高亮 / 顶栏三栏状态;
    //         交互与科技页完全一致: 左键点节点(热区) / 右键或中键拖动 / 滚轮缩放 / WASD 平移。
    internal static class FocusPanel
    {
        private const string Key = "focus";
        private const string Movie = "FeudalFocusTree";
        private static FocusTreeVM _vm;
        private static float _acc;
        private static float _lastMouseX, _lastMouseY;
        private static bool _hasLastMouse;

        internal static bool IsOpen { get { return PanelScreen.IsOpen(Key); } }
        internal static FocusTreeVM VM { get { return _vm; } }

        internal static void Open()
        {
            try
            {
                if (IsOpen)
                {
                    if (_vm != null) { _vm.Refresh(); RegisterSpots(); }
                    return;
                }
                _vm = new FocusTreeVM(Close);
                _vm.OpenPanelAnim(PanelScreen.FullWidth());
                PanelScreen.OpenPanel(Key, Movie, _vm, null, OnTick, null);
                PanelScreen.SetScrollHandler(Key, delegate (int dir)
                {
                    if (_vm != null) { _vm.ZoomBy(dir * 0.08f); RegisterSpots(); }
                });
                RegisterSpots();
                DLog.Force("国策页已打开(照科技页重做的全屏面板)");
            }
            catch (Exception ex) { DLog.Force("打开国策页失败: " + ex.Message); }
        }

        internal static void Close()
        {
            try { PanelScreen.ClosePanel(Key); }
            catch { }
        }

        private static void OnTick(float dt)
        {
            try
            {
                PollInput();
                if (_vm != null) _vm.TickAnim(dt);   // 滑入/滑出动画
                _acc += dt;
                if (_acc >= 1f) { _acc = 0f; if (_vm != null) _vm.Refresh(); }
                RegisterSpots();   // 每帧: 节点热区跟随拖动/缩放
            }
            catch { }
        }

        // 拖动/缩放/滚轮(读静态 Input, 不受面板输入拦截影响)
        private static void PollInput()
        {
            try
            {
                if (_vm == null) return;
                if (PanelInputGuard.AnyPopupActive()) { _hasLastMouse = false; return; }   // 国策弹窗期间不操作

                float scroll = TaleWorlds.InputSystem.Input.DeltaMouseScroll;
                if (Math.Abs(scroll) > 0.01f)
                {
                    if (scroll > 1f) scroll = 1f;
                    if (scroll < -1f) scroll = -1f;
                    _vm.ZoomBy(scroll * 0.08f);
                }

                float step = 12f;
                float dx = 0f, dy = 0f;
                if (TaleWorlds.InputSystem.Input.IsKeyDown(TaleWorlds.InputSystem.InputKey.A)) dx += step;
                if (TaleWorlds.InputSystem.Input.IsKeyDown(TaleWorlds.InputSystem.InputKey.D)) dx -= step;
                if (TaleWorlds.InputSystem.Input.IsKeyDown(TaleWorlds.InputSystem.InputKey.W)) dy += step;
                if (TaleWorlds.InputSystem.Input.IsKeyDown(TaleWorlds.InputSystem.InputKey.S)) dy -= step;

                var mouse = TaleWorlds.InputSystem.Input.MousePositionPixel;
                bool dragging = TaleWorlds.InputSystem.Input.IsKeyDown(TaleWorlds.InputSystem.InputKey.RightMouseButton)
                             || TaleWorlds.InputSystem.Input.IsKeyDown(TaleWorlds.InputSystem.InputKey.MiddleMouseButton);
                if (dragging)
                {
                    if (_hasLastMouse)
                    {
                        float px = mouse.X - _lastMouseX;
                        float py = mouse.Y - _lastMouseY;
                        // 光标被摄像机锁住时坐标不变 -> 退回用原始位移
                        if (Math.Abs(px) + Math.Abs(py) < 0.5f)
                        {
                            px = TaleWorlds.InputSystem.Input.MouseMoveX;
                            py = TaleWorlds.InputSystem.Input.MouseMoveY;
                        }
                        dx += px;
                        dy += py;
                    }
                    _lastMouseX = mouse.X; _lastMouseY = mouse.Y;
                    _hasLastMouse = true;
                }
                else
                {
                    _hasLastMouse = false;
                }

                if (Math.Abs(dx) > 0.01f || Math.Abs(dy) > 0.01f) _vm.Pan(dx, dy);
            }
            catch { }
        }

        private static float Origin()
        {
            try { return PanelScreen.PanelX + Math.Max(0f, (PanelScreen.FullWidth() - 1700f) / 2f); }
            catch { return 64f; }
        }

        private static void Spot(float ox, float x, float y, float w, float h, Action act)
        {
            PanelScreen.AddSpotRaw(ox + x, y, w, h, act);
        }

        // 热区(与 FeudalFocusTree.xml 布局常量严格一致; 坐标 = 屏幕绝对坐标)
        private static void RegisterSpots()
        {
            try
            {
                if (_vm == null) return;
                PanelScreen.ClearSpots();
                float ox = Origin();

                // 关闭 X(y 50, 64x48)
                Spot(ox, 1616f, 50f, 64f, 48f, Close);

                // 节点(树容器 MarginTop = 210)
                int n = _vm.Items.Count;
                for (int i = 0; i < n; i++)
                {
                    var item = _vm.Items[i];
                    if (item == null || item.Width <= 1f) continue;
                    int idx = i;
                    Spot(ox, item.PosX, FocusTreeVM.TreeTopY + item.PosY, item.Width, item.Height,
                        delegate { _vm.NodeAction(idx); RegisterSpots(); });
                }
            }
            catch (Exception ex) { DLog.Force("国策页热区失败: " + ex.Message); }
        }
    }
}
