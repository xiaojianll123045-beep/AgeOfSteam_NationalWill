using System;
using SandBox.View.Map;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.InputSystem;

namespace FeudalInternalAffairs
{
    internal static class Outliner
    {
        private const string Movie = "FeudalOutliner";
        private const int Depth = 340;
        private const float HandleTop = 184f;
        private const float HandleW = 30f;
        private const float HandleH = 60f;

        private static GauntletLayer _layer;
        private static OutlinerVM _vm;
        private static MapScreen _map;
        private static float _wheelAcc;

        internal static bool BlocksMapInput;

        internal static void Tick()
        {
            BlocksMapInput = false;
            try
            {
                var map = MapScreen.Instance;
                if (map == null)
                {
                    CloseLayer();
                    return;
                }
                if (_layer != null && !ReferenceEquals(_map, map)) CloseLayer();
                if (_layer == null)
                {
                    if (!NationalWillOrders.ShouldControlCamera) return;
                    if (NationPickMode.Active) return;
                    _vm = new OutlinerVM();
                    _layer = new GauntletLayer("FeudalOutliner", Depth, false);
                    _layer.LoadMovie(Movie, _vm);
                    try { _layer.InputRestrictions.SetInputRestrictions(true, TaleWorlds.Library.InputUsageMask.Mouse); } catch { }
                    map.AddLayer(_layer);
                    _map = map;
                    DLog.Force("概览栏: 层已建立(" + Depth + "), movie=" + Movie);
                }
                if (_vm == null) return;
                UpdateBlocksMapInput();
                _vm.TickRefresh();
                PollInput();
            }
            catch (Exception ex) { BlocksMapInput = false; DLog.Force("概览栏失败: " + ex.Message); }
        }

        private static void UpdateBlocksMapInput()
        {
            try
            {
                var m = Input.MousePositionPixel;
                float sw = PanelScreen.ScreenWidth();
                float sh = PanelScreen.ScreenHeight();
                float right = sw - OutlinerVM.RightMargin;
                float left = right - _vm.PanelWidth;
                if (m.X >= left - 2f && m.X <= right && m.Y >= OutlinerVM.TopMargin && m.Y <= sh - OutlinerVM.BottomMargin)
                {
                    BlocksMapInput = true;
                    return;
                }
                float hx = sw - (OutlinerVM.RightMargin + _vm.PanelWidth + 2f) - HandleW;
                BlocksMapInput = m.X >= hx && m.X <= hx + HandleW && m.Y >= HandleTop && m.Y <= HandleTop + HandleH;
            }
            catch { BlocksMapInput = false; }
        }

        private static void CloseLayer()
        {
            try { if (_layer != null && _map != null) _map.RemoveLayer(_layer); } catch { }
            _layer = null;
            _vm = null;
            _map = null;
            _wheelAcc = 0f;
            BlocksMapInput = false;
        }

        private static void PollInput()
        {
            try
            {
                var m = Input.MousePositionPixel;
                float sw = PanelScreen.ScreenWidth();

                // 侧边栏(层深345)盖在概览栏(340)上时整体不响应: 否则点面板内容会穿透到被遮住的把手/窄条
                if (PanelScreen.IsMouseOnPanel()) { _vm.SetHover(-1); _wheelAcc = 0f; return; }

                float handleRight = OutlinerVM.RightMargin + _vm.PanelWidth + 2f;
                float hx = sw - handleRight - HandleW;
                if (m.X >= hx && m.X <= hx + HandleW && m.Y >= HandleTop && m.Y <= HandleTop + HandleH)
                {
                    _vm.SetHover(-1);
                    _wheelAcc = 0f;
                    if (Input.IsKeyPressed(InputKey.LeftMouseButton)
                        && !PanelInputGuard.SuppressAfterInquiry(Input.IsKeyDown(InputKey.LeftMouseButton)))
                    {
                        _vm.ToggleAll();
                    }
                    return;
                }

                if (!_vm.Expanded)
                {
                    float sx = sw - OutlinerVM.RightMargin - _vm.PanelWidth;
                    float stripBottom = PanelScreen.ScreenHeight() - OutlinerVM.BottomMargin;
                    if (m.X >= sx && m.X <= sx + _vm.PanelWidth && m.Y >= OutlinerVM.TopMargin && m.Y <= stripBottom
                        && Input.IsKeyPressed(InputKey.LeftMouseButton)
                        && !PanelInputGuard.SuppressAfterInquiry(Input.IsKeyDown(InputKey.LeftMouseButton)))
                    {
                        _vm.ToggleAll();
                    }
                    _wheelAcc = 0f;
                    return;
                }

                float px = sw - OutlinerVM.RightMargin - _vm.PanelWidth;
                if (m.X < px || m.X > px + _vm.PanelWidth) { _vm.SetHover(-1); _wheelAcc = 0f; return; }
                float listTop = OutlinerVM.ListTopAbs;
                if (m.Y < listTop)
                {
                    _vm.SetHover(-1);
                    _wheelAcc = 0f;
                    if (Input.IsKeyPressed(InputKey.LeftMouseButton)
                        && !PanelInputGuard.SuppressAfterInquiry(Input.IsKeyDown(InputKey.LeftMouseButton)))
                    {
                        _vm.ToggleAll();
                    }
                    return;
                }

                float wheel = Input.DeltaMouseScroll;
                if (Math.Abs(wheel) > 0.01f)
                {
                    _wheelAcc += wheel;
                    if (Math.Abs(_wheelAcc) >= 1f)
                    {
                        _vm.Scroll(_wheelAcc > 0f ? -1 : 1);
                        _wheelAcc = 0f;
                    }
                }

                int hit = _vm.HitTest(m.Y - listTop);
                _vm.SetHover(hit);
                if (hit < 0) return;
                if (!Input.IsKeyPressed(InputKey.LeftMouseButton)) return;
                if (PanelInputGuard.SuppressAfterInquiry(Input.IsKeyDown(InputKey.LeftMouseButton))) return;
                _vm.Activate(hit);
            }
            catch { }
        }
    }
}
