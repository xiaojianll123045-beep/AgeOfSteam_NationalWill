using System;

namespace FeudalInternalAffairs
{
    // v4.197: 随机事件面板(左侧 560 宽; 与其它侧栏面板同一套交互)
    //   选项热区: 3 条(每条 64 + 10 间距, y 从 450 起); 底部「暂不处理」; 关闭 X 在右上
    internal static class EventPanel
    {
        private const string Key = "event";
        private const string Movie = "FeudalEvent";
        internal const float Width = 560f;
        private static EventVM _vm;

        internal static bool IsOpen { get { return PanelScreen.IsOpen(Key); } }
        internal static EventVM VM { get { return _vm; } }

        internal static void Open()
        {
            try
            {
                if (IsOpen)
                {
                    if (_vm != null) { _vm.Refresh(); RegisterSpots(); }
                    return;
                }
                if (Events.Pending == null) return;
                _vm = new EventVM(Close);
                _vm.OpenPanelAnim(Width);
                PanelScreen.OpenPanel(Key, Movie, _vm, null,
                    delegate (float dt) { if (_vm != null) _vm.TickAnim(dt); RegisterSpots(); }, null);
                RegisterSpots();
                DLog.Force("事件面板已打开: " + (Events.Pending != null ? Events.Pending.Name : "?"));
            }
            catch (Exception ex) { DLog.Force("打开事件面板失败: " + ex.Message); }
        }

        internal static void Close()
        {
            try { PanelScreen.ClosePanel(Key); }
            catch { }
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
                int n = _vm.Choices.Count;
                for (int i = 0; i < n && i < 3; i++)
                {
                    int idx = i;
                    Spot(20f, 450f + i * 74f, 520f, 64f, delegate { _vm.ChoiceClick(idx); });
                }
                Spot(20f, PanelScreen.ScreenHeight() - 24f - 48f, 180f, 48f, _vm.Dismiss);
            }
            catch (Exception ex) { DLog.Force("事件面板热区失败: " + ex.Message); }
        }
    }
}
