using System;

namespace FeudalInternalAffairs
{
    internal static class BattleReportPanel
    {
        private const string Key = "breport";
        private const string Movie = "FeudalBattleReport";
        internal const float Width = 680f;
        private static BattleReportVM _vm;
        internal static bool IsOpen { get { return PanelScreen.IsOpen(Key); } }
        internal static BattleReportVM VM { get { return _vm; } }
        internal static void Open()
        {
            try
            {
                if (IsOpen) { if (_vm != null) { _vm.Refresh(); RegisterSpots(); } return; }
                _vm = new BattleReportVM(Close);
                _vm.OpenPanelAnim(Width);
                PanelScreen.OpenPanel(Key, Movie, _vm, null, delegate (float dt) { if (_vm != null) { _vm.TickAnim(dt); RegisterSpots(); } }, null);
                RegisterSpots();
                DLog.Force("Battle report page opened");
            }
            catch (Exception ex) { DLog.Force("Open battle report failed: " + ex.Message); }
        }
        internal static void Close()
        {
            try { PanelScreen.ClosePanel(Key); } catch { }
        }
        private static void Spot(float x, float y, float w, float h, Action act)
        {
            PanelScreen.AddSpotRaw(PanelScreen.PanelX + x, y, w, h, act);
        }
        private static void RegisterSpots()
        {
            try
            {
                if (_vm == null) return;
                PanelScreen.ClearSpots();
                Spot(Width - 60f, 20f, 60f, 58f, _vm.ExecuteClose);
                Spot(30f, PanelScreen.ScreenHeight() - 26f - 42f, 122f, 42f, _vm.ExecuteClose);
            }
            catch (Exception ex) { DLog.Force("Battle report spots failed: " + ex.Message); }
        }
    }
}