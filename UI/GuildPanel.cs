using System;

namespace FeudalInternalAffairs
{
    // 行会页(文档 19.14 扩展)
    internal static class GuildPanel
    {
        private const string Key = "guild";
        private const string Movie = "FeudalGuild";
        private const float Width = 620f;
        private static GuildPanelVM _vm;
        private static float _acc;

        internal static bool IsOpen { get { return PanelScreen.IsOpen(Key); } }

        internal static void Open()
        {
            Tutorials.Page("page_guild");
            try
            {
                if (!IsOpen)
                {
                    _vm = new GuildPanelVM(Close);
                    _vm.OpenPanelAnim(Width);
                    PanelScreen.OpenPanel(Key, Movie, _vm, null,
                        delegate (float dt) { if (_vm != null) _vm.TickAnim(dt); OnTick(dt); },
                        delegate { if (_vm != null) _vm.ClosePanelAnim(Width); });
                    RegisterSpots();
                }
                else if (_vm != null) _vm.Refresh();
            }
            catch (Exception ex) { DLog.Force("打开行会页失败: " + ex.Message); }
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

        // 热区: 行尾"成立"按钮(行布局: 起点 166, 行高 76)
        private static void RegisterSpots()
        {
            try
            {
                if (_vm == null) return;
                PanelScreen.ClearSpots();
                float sh = PanelScreen.ScreenHeight();
                PanelScreen.AddSpot(Width - 60f, 20f, 60f, 58f, _vm.ExecuteClose);
                PanelScreen.AddSpot(26f, sh - 26f - 42f, 122f, 42f, _vm.ExecuteClose);
                float top = 166f, step = 76f;
                for (int i = 0; i < Guilds.All.Count; i++)
                {
                    int idx = i;
                    PanelScreen.AddSpot(Width - 150f, top + i * step + 22f, 120f, 40f, delegate { _vm.Found(idx); });
                }
            }
            catch (Exception ex) { DLog.Force("行会页热区失败: " + ex.Message); }
        }
    }
}
