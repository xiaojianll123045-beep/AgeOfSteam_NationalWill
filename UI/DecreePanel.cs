using System;

namespace FeudalInternalAffairs
{
    internal static class DecreePanel
    {
        private const string Key = "decree";
        private const string Movie = "FeudalDecree";
        internal const float Width = 680f;
        private static DecreeVM _vm;
        internal static bool IsOpen { get { return PanelScreen.IsOpen(Key); } }
        internal static void Open()
        {
            try
            {
                if (IsOpen) { if (_vm != null) { _vm.Refresh(); RegisterSpots(); } return; }
                _vm = new DecreeVM(Close);
                _vm.OpenPanelAnim(Width);
                PanelScreen.OpenPanel(Key, Movie, _vm, null, delegate (float dt) { if (_vm != null) { _vm.TickAnim(dt); OnTick(dt); RegisterSpots(); } }, null);
                RegisterSpots();
            }
            catch (Exception ex) { DLog.Force("Open decree page failed: " + ex.Message); }
        }
        internal static void Close()
        {
            try { PanelScreen.ClosePanel(Key); } catch { }
        }
        private static void Spot(float x, float y, float w, float h, Action act)
        {
            PanelScreen.AddSpotRaw(PanelScreen.PanelX + x, y, w, h, act);
        }
        private static float _acc;
        private static void OnTick(float dt)
        {
            try { _acc += dt; if (_acc < 3f) return; _acc = 0f; if (_vm != null) _vm.Refresh(); } catch { }
        }
        private static void RegisterSpots()
        {
            try
            {
                if (_vm == null) return;
                PanelScreen.ClearSpots();
                int n = _vm.Cards.Count;
                float lim = PanelScreen.ScreenHeight() - 26f - 42f;
                float y = 136f;
                for (int i = 0; i < n; i++)
                {
                    int idx = i;
                    float h = 64f;
                    try { h = 34f + _vm.Cards[i].Body.Split((char)10).Length * 24f; } catch { }
                    if (y >= lim) break;
                    Spot(26f, y, 628f, Math.Min(h, lim - y), delegate { _vm.EnactClick(idx); });
                    y += h + 10f;
                }
                Spot(Width - 60f, 20f, 60f, 58f, _vm.ExecuteClose);
                Spot(30f, PanelScreen.ScreenHeight() - 26f - 42f, 122f, 42f, _vm.ExecuteClose);
            }
            catch (Exception ex) { DLog.Force("Decree spots failed: " + ex.Message); }
        }
    }
}