using System;

namespace FeudalInternalAffairs
{
    // 建筑详情面板(文档 20.8; 从建筑总表点行进入)
    internal static class BldDetailPanel
    {
        private const string Key = "bld";
        private const string Movie = "FeudalBldDetail";
        private const float Width = 680f;
        private static BldDetailVM _vm;
        private static float _acc;

        internal static bool IsOpen { get { return PanelScreen.IsOpen(Key); } }

        internal static void Open(string sid, string defId)
        {
            try
            {
                if (IsOpen && _vm != null) { _vm.SetTarget(sid, defId); return; }   // 已开: 直接换目标
                _vm = new BldDetailVM(Close, sid, defId);
                _vm.OpenPanelAnim(Width);
                PanelScreen.OpenPanel(Key, Movie, _vm, null,
                    delegate (float dt) { if (_vm != null) _vm.TickAnim(dt); OnTick(dt); },
                    delegate { if (_vm != null) _vm.ClosePanelAnim(Width); });
                RegisterSpots();
            }
            catch (Exception ex) { DLog.Force("打开建筑详情失败: " + ex.Message); }
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
                // v4.58: 点标题栏(城市名) -> 视野飞到该城
                PanelScreen.AddSpot(14f, 14f, 500f, 62f, _vm.FlyToCity);
                // 按钮行(与 FeudalBldDetail.xml 的 MarginTop=430 对应)
                float y = 430f;
                PanelScreen.AddSpot(26f, y, 150f, 44f, _vm.CycleMode);      // 切换生产方法
                PanelScreen.AddSpot(190f, y, 150f, 44f, _vm.Buy);           // 赎买
                PanelScreen.AddSpot(354f, y, 150f, 44f, _vm.Confiscate);    // 没收
                PanelScreen.AddSpot(518f, y, 130f, 44f, _vm.Sell);          // 出售
            }
            catch (Exception ex) { DLog.Force("建筑详情热区失败: " + ex.Message); }
        }
    }
}
