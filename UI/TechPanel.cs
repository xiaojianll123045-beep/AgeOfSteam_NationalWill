using System;

namespace FeudalInternalAffairs
{
    // 科技页(v4.171 重写; 照 V3 官方科技树: 三树页签 / 时代条带 / 徽章节点 / 贝塞尔连线)
    // 交互: 左键点节点研究 · 滚轮缩放(0.8~1.6) · 右键/中键拖动 · WASD 平移
    internal static class TechPanel
    {
        private const string Key = "tech";
        private const string Movie = "FeudalTech";
        private const float DesignW = 1700f;
        private const float TreeTop = TechPanelVM.TreeTopY;   // 与 FeudalTech.xml 的树区域 MarginTop 一致

        private static TechPanelVM _vm;
        private static float _acc;
        private static float _lastMouseX, _lastMouseY;
        private static bool _hasLastMouse;

        internal static bool IsOpen { get { return PanelScreen.IsOpen(Key); } }

        internal static void Open()
        {
            Tutorials.Page("page_tech");
            try
            {
                if (!IsOpen)
                {
                    _vm = new TechPanelVM(Close);
                    _vm.OpenPanelAnim(PanelScreen.FullWidth());
                    PanelScreen.OpenPanel(Key, Movie, _vm, null,
                        delegate (float dt) { if (_vm != null) _vm.TickAnim(dt); OnTick(dt); },
                        delegate { if (_vm != null) _vm.ClosePanelAnim(PanelScreen.FullWidth()); });
                    PanelScreen.SetScrollHandler(Key, delegate (int dir) { if (_vm != null) { _vm.ZoomBy(dir * 0.08f); RegisterSpots(); } });
                    RegisterSpots();
                }
                else if (_vm != null) { _vm.Refresh(); RegisterSpots(); }
            }
            catch (Exception ex) { DLog.Force("打开科技页失败: " + ex.Message); }
        }

        internal static void Close()
        {
            try { PanelScreen.ClosePanel(Key); }
            catch { }
        }

        internal static void Tick(float dt) { }

        private static void OnTick(float dt)
        {
            try
            {
                PollInput();
                _acc += dt;
                if (_acc >= 1f) { _acc = 0f; if (_vm != null) _vm.Refresh(); }
                RegisterSpots();   // 每帧: 热区跟随拖动/缩放
            }
            catch { }
        }

        private static void PollInput()
        {
            try
            {
                if (_vm == null) return;
                if (PanelInputGuard.AnyPopupActive()) { _hasLastMouse = false; return; }

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
                        // v4.181: 光标被摄像机锁住时坐标不变 -> 退回用原始位移, 否则"右键拖不动"
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
                else _hasLastMouse = false;

                if (Math.Abs(dx) > 0.01f || Math.Abs(dy) > 0.01f) _vm.Pan(dx, dy);
            }
            catch { }
        }

        private static float Origin()
        {
            try { return PanelScreen.PanelX + Math.Max(0f, (PanelScreen.FullWidth() - DesignW) / 2f); }
            catch { return 64f; }
        }

        private static void Spot(float ox, float x, float y, float w, float h, Action act)
        {
            PanelScreen.AddSpotRaw(ox + x, y, w, h, act);
        }

        // 热区: 关闭 X + 三树页签 + 每个科技节点(坐标 = 内容容器坐标 + 容器起点)
        private static void RegisterSpots()
        {
            try
            {
                if (_vm == null) return;
                PanelScreen.ClearSpots();
                float ox = Origin();

                // 关闭 X(y 18, 64×48)
                Spot(ox, 1616f, 48f, 64f, 48f, _vm.ExecuteClose);
                // 三树页签(y 100, 566 宽三等分)
                Spot(ox, 0f, 130f, 566f, 54f, delegate { _vm.SetTab(0); RegisterSpots(); });
                Spot(ox, 567f, 130f, 566f, 54f, delegate { _vm.SetTab(1); RegisterSpots(); });
                Spot(ox, 1134f, 130f, 566f, 54f, delegate { _vm.SetTab(2); RegisterSpots(); });

                // 节点(树区域 y = 214)
                int n = _vm.Nodes.Count;
                for (int i = 0; i < n; i++)
                {
                    var node = _vm.Nodes[i];
                    int idx = i;
                    if (node.Width <= 1f) continue;
                    PanelScreen.AddSpotRaw(ox + node.PosX, TreeTop + node.PosY, node.Width, node.Height,
                        delegate { _vm.NodeAction(idx); RegisterSpots(); });
                }
            }
            catch (Exception ex) { DLog.Force("科技页热区失败: " + ex.Message); }
        }
    }
}
