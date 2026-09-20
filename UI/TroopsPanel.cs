using System;

namespace FeudalInternalAffairs
{
    // v4.100: 军队总览(导航栏第 14 格; 领主军/国防军/原版联军标识)
    internal static class TroopsPanel
    {
        private const string Key = "troops";
        private const string Movie = "FeudalTroops";
        private const float Width = 680f;
        private static TroopsPanelVM _vm;
        private static float _acc;

        internal static bool IsOpen { get { return PanelScreen.IsOpen(Key); } }

        internal static void Open()
        {
            Tutorials.Page("page_troops");
            try
            {
                if (!IsOpen)
                {
                    _vm = new TroopsPanelVM(Close);
                    _vm.OpenPanelAnim(Width);
                    PanelScreen.OpenPanel(Key, Movie, _vm, null,
                        delegate (float dt) { if (_vm != null) _vm.TickAnim(dt); OnTick(dt); },
                        delegate { if (_vm != null) _vm.ClosePanelAnim(Width); });
                    PanelScreen.SetScrollHandler(Key, delegate (int dir) { if (_vm != null) _vm.ScrollStep(dir); });
                    RegisterSpots();
                    DLog.Force("军队页: 已打开");
                }
                else if (_vm != null)
                {
                    _vm.Refresh();
                    RegisterSpots();
                }
            }
            catch (Exception ex) { DLog.Force("打开军队页失败: " + ex.Message); }
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
                _acc += dt;
                if (_acc < 3f) return;
                _acc = 0f;
                if (_vm != null) { _vm.Refresh(); RegisterSpots(); }
            }
            catch { }
        }

        // 热区(与 FeudalTroops.xml 布局常量一致)
        private static void RegisterSpots()
        {
            try
            {
                if (_vm == null) return;
                PanelScreen.ClearSpots();
                float sh = PanelScreen.ScreenHeight();
                PanelScreen.AddSpot(Width - 60f, 20f, 60f, 58f, _vm.ExecuteClose);        // X
                PanelScreen.AddSpot(30f, sh - 26f - 42f, 122f, 42f, _vm.ExecuteClose);    // 关闭
                for (int i = 0; i < _vm.ShownCount; i++)
                {
                    int idx = i;
                    float y = 284f + i * 46f;
                    PanelScreen.AddSpot(560f, y, 90f, 34f, delegate { _vm.ExecuteRowAction(idx); });   // 定位/操纵
                }
            }
            catch (Exception ex) { DLog.Force("军队页热区失败: " + ex.Message); }
        }
    }
}
