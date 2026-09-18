using System;

namespace FeudalInternalAffairs
{
    // 人口面板(左侧边栏; 文档 19.14.2)
    internal static class PopPanel
    {
        private const string Key = "pop";
        private const string Movie = "FeudalPop";
        private const float Width = 620f;
        private static PopPanelVM _vm;
        private static float _acc;

        internal static bool IsOpen { get { return PanelScreen.IsOpen(Key); } }

        internal static void Open()
        {
            try
            {
                if (!IsOpen)
                {
                    _vm = new PopPanelVM(Close);
                    _vm.OpenPanelAnim(Width);
                    PanelScreen.OpenPanel(Key, Movie, _vm, null,
                        delegate (float dt) { if (_vm != null) _vm.TickAnim(dt); OnTick(dt); },
                        delegate { if (_vm != null) _vm.ClosePanelAnim(Width); });
                    PanelScreen.SetScrollHandler(Key, delegate (int dir) { if (_vm != null) _vm.ScrollStep(dir); });
                    RegisterSpots();
                }
                else if (_vm != null) _vm.Refresh();
            }
            catch (Exception ex) { DLog.Force("打开人口面板失败: " + ex.Message); }
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
                _acc += dt;
                if (_acc < 3f) return;
                _acc = 0f;
                if (_vm != null) _vm.Refresh();
            }
            catch { }
        }

        private static void RegisterSpots()
        {
            try
            {
                if (_vm == null) return;
                PanelScreen.ClearSpots();
                float sh = PanelScreen.ScreenHeight();
                PanelScreen.AddSpot(Width - 60f, 20f, 60f, 58f, _vm.ExecuteClose);          // X
                PanelScreen.AddSpot(26f, sh - 26f - 42f, 122f, 42f, _vm.ExecuteClose);     // 关闭
            }
            catch (Exception ex) { DLog.Force("人口面板热区失败: " + ex.Message); }
        }
    }
}
