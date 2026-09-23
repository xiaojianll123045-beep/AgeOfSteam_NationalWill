using System;

namespace FeudalInternalAffairs
{
    // v4.100: 军队总览(导航栏第 14 格; 领主军/国防军/原版联军标识); 加宽到 860
    internal static class TroopsPanel
    {
        private const string Key = "troops";
        private const string Movie = "FeudalTroops";
        private const float Width = 860f;
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
                PanelScreen.AddSpot(780f, 26f, 56f, 52f, _vm.ExecuteClose);                // X
                PanelScreen.AddSpot(26f, sh - 70f, 150f, 44f, _vm.ExecuteClose);          // 关闭
                PanelScreen.AddSpot(190f, sh - 70f, 170f, 44f, MilitaryPanel.Open);       // v4.199: 军务
                for (int i = 0; i < _vm.ShownCount; i++)
                {
                    int idx = i;
                    float y = 338f + i * 90f;   // 列表顶 308 + 按钮偏移 30; 行距 84+6
                    PanelScreen.AddSpot(726f, y, 108f, 26f, delegate { _vm.ExecuteRowAction(idx); });   // 定位/操纵
                }
            }
            catch (Exception ex) { DLog.Force("军队页热区失败: " + ex.Message); }
        }
    }
}
