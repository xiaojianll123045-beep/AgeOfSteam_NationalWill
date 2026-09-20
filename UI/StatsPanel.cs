using System;

namespace FeudalInternalAffairs
{
    // 统计面板(文档 20.9; 导航栏第 10 格)
    internal static class StatsPanel
    {
        private const string Key = "stats";
        private const string Movie = "FeudalStats";
        private const float Width = 680f;
        private static StatsVM _vm;
        private static float _acc;

        internal static bool IsOpen { get { return PanelScreen.IsOpen(Key); } }

        internal static void Open()
        {
            try
            {
                if (!IsOpen)
                {
                    _vm = new StatsVM(Close);
                    _vm.OpenPanelAnim(Width);
                    PanelScreen.OpenPanel(Key, Movie, _vm, null,
                        delegate (float dt) { if (_vm != null) _vm.TickAnim(dt); OnTick(dt); },
                        delegate { if (_vm != null) _vm.ClosePanelAnim(Width); });
                    RegisterSpots();
                }
                else if (_vm != null) _vm.Refresh();
            }
            catch (Exception ex) { DLog.Force("打开统计页失败: " + ex.Message); }
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
                if (_acc < 4f) return;
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
                PanelScreen.AddSpot(Width - 60f, 20f, 60f, 58f, _vm.ExecuteClose);
                PanelScreen.AddSpot(Width - 244f, 20f, 168f, 58f, MapDataModeUi.Open);   // v4.69: 地图模式
                PanelScreen.AddSpot(26f, sh - 26f - 42f, 122f, 42f, _vm.ExecuteClose);
            }
            catch (Exception ex) { DLog.Force("统计页热区失败: " + ex.Message); }
        }
    }
}
