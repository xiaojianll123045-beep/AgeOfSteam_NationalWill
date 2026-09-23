using System;

namespace FeudalInternalAffairs
{
    // v4.199: 军务总览页(加宽 860; 12 项军务统计 + 兵种图鉴 2 列)
    internal static class MilitaryPanel
    {
        private const string Key = "military";
        private const string Movie = "FeudalMilitary";
        internal const float Width = 860f;
        private static MilitaryVM _vm;

        internal static bool IsOpen { get { return PanelScreen.IsOpen(Key); } }
        internal static MilitaryVM VM { get { return _vm; } }

        internal static void Open()
        {
            try
            {
                if (IsOpen)
                {
                    if (_vm != null) { _vm.Refresh(); RegisterSpots(); }
                    return;
                }
                _vm = new MilitaryVM(Close);
                _vm.OpenPanelAnim(Width);
                PanelScreen.OpenPanel(Key, Movie, _vm, null,
                    delegate (float dt) { if (_vm != null) _vm.TickAnim(dt); RegisterSpots(); }, null);
                RegisterSpots();
                DLog.Force("军务总览页已打开");
            }
            catch (Exception ex) { DLog.Force("打开军务总览页失败: " + ex.Message); }
        }

        internal static void Close()
        {
            try { PanelScreen.ClosePanel(Key); }
            catch { }
        }

        // 热区(与 FeudalMilitary.xml 布局一致; AddSpotRaw 坐标已含 PanelX)
        private static void RegisterSpots()
        {
            try
            {
                if (_vm == null) return;
                PanelScreen.ClearSpots();
                float sh = PanelScreen.ScreenHeight();
                PanelScreen.AddSpotRaw(PanelScreen.PanelX + 780f, 26f, 56f, 52f, _vm.ExecuteClose);                      // X
                PanelScreen.AddSpotRaw(PanelScreen.PanelX + 26f, sh - 70f, 150f, 44f, _vm.ExecuteClose);                 // 关闭
                PanelScreen.AddSpotRaw(PanelScreen.PanelX + 190f, sh - 70f, 170f, 44f, BattleReportPanel.Open);          // v4.209: 战报(自建页)
            }
            catch (Exception ex) { DLog.Force("军务总览页热区失败: " + ex.Message); }
        }
    }
}
