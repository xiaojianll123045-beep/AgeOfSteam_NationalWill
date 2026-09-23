using System;

namespace FeudalInternalAffairs
{
    // v4.186: 文化页(文档 24.13) —— 全屏面板, 交互与科技页/国策页一致
    //   左键点文化行选中; 右键点法律卡立即改法; 关闭 X; 滚轮/WASD 不用(页面无树)
    internal static class CulturePanel
    {
        private const string Key = "culture";
        private const string Movie = "FeudalCulture";
        private static CultureVM _vm;
        private static float _acc;

        internal static bool IsOpen { get { return PanelScreen.IsOpen(Key); } }
        internal static CultureVM VM { get { return _vm; } }

        internal static void Open()
        {
            try
            {
                if (IsOpen)
                {
                    if (_vm != null) { _vm.Refresh(); RegisterSpots(); }
                    return;
                }
                _vm = new CultureVM(Close);
                _vm.OpenPanelAnim(PanelScreen.FullWidth());
                PanelScreen.OpenPanel(Key, Movie, _vm, null, OnTick, null);
                RegisterSpots();
                DLog.Force("文化页已打开(参考 24.13)");
            }
            catch (Exception ex) { DLog.Force("打开文化页失败: " + ex.Message); }
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
                if (_vm != null) _vm.TickAnim(dt);
                _acc += dt;
                if (_acc >= 1f) { _acc = 0f; if (_vm != null) _vm.Refresh(); }
                RegisterSpots();
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

        // 热区(与 FeudalCulture.xml 布局常量一致)
        private static void RegisterSpots()
        {
            try
            {
                if (_vm == null) return;
                PanelScreen.ClearSpots();
                float ox = Origin();

                // 关闭 X
                Spot(ox, 1616f, 50f, 64f, 48f, Close);

                // 文化行(y 206 起, 每行 46 + 2)
                int n = _vm.Rows.Count;
                for (int i = 0; i < n; i++)
                {
                    int idx = i;
                    Spot(ox, 14f, 206f + i * 48f, 672f, 46f, delegate { _vm.Select(idx); RegisterSpots(); });
                }

                // 法律卡(y 348 起, 每张 96 + 8)
                int m = _vm.Laws.Count;
                for (int i = 0; i < m; i++)
                {
                    int idx = i;
                    Spot(ox, 700f, 348f + i * 104f, 972f, 96f, delegate { _vm.PickLaw(idx); RegisterSpots(); });
                }
            }
            catch (Exception ex) { DLog.Force("文化页热区失败: " + ex.Message); }
        }
    }
}
