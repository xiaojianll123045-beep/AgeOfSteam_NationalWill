using System;

namespace FeudalInternalAffairs
{
    // 社会页(文化×阶级; 文档 19.14)
    internal static class SocietyPanel
    {
        private const string Key = "soc";
        private const string Movie = "FeudalSociety";
        private const float Width = 680f;
        private static SocietyVM _vm;
        private static float _acc;

        internal static bool IsOpen { get { return PanelScreen.IsOpen(Key); } }

        internal static void Open()
        {
            Tutorials.Page("page_society");
            try
            {
                if (!IsOpen)
                {
                    _vm = new SocietyVM(Close);
                    _vm.OpenPanelAnim(Width);
                    PanelScreen.OpenPanel(Key, Movie, _vm, null,
                        delegate (float dt) { if (_vm != null) _vm.TickAnim(dt); OnTick(dt); },
                        delegate { if (_vm != null) _vm.ClosePanelAnim(Width); });
                    RegisterSpots();
                }
                else if (_vm != null) _vm.Refresh();
            }
            catch (Exception ex) { DLog.Force("打开社会页失败: " + ex.Message); }
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
                PanelScreen.AddSpot(Width - 60f, 20f, 60f, 58f, _vm.ExecuteClose);       // X
                PanelScreen.AddSpot(26f, sh - 26f - 42f, 122f, 42f, _vm.ExecuteClose);  // 关闭
                PanelScreen.AddSpot(160f, sh - 26f - 42f, 160f, 42f, InstitutionsUi.Show); // v5.0-P26: 国家机构/研究/文化
            }
            catch (Exception ex) { DLog.Force("社会页热区失败: " + ex.Message); }
        }
    }
}
