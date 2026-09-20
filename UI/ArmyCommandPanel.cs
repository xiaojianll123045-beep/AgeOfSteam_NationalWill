using System;

namespace FeudalInternalAffairs
{
    // v4.104: 军团操作侧边栏(解散军团 / 命令某人出军团)
    internal static class ArmyCommandPanel
    {
        private const string Key = "armycmd";
        private const string Movie = "FeudalArmyCmd";
        private const float Width = 560f;
        private static ArmyCommandVM _vm;
        private static float _acc;

        internal static bool IsOpen { get { return PanelScreen.IsOpen(Key); } }

        internal static void Open(TaleWorlds.CampaignSystem.Army a)
        {
            Tutorials.Page("page_armycmd");
            try
            {
                if (a == null) return;
                if (!IsOpen)
                {
                    _vm = new ArmyCommandVM(Close);
                    _vm.Show(a);
                    _vm.OpenPanelAnim(Width);
                    PanelScreen.OpenPanel(Key, Movie, _vm, null,
                        delegate (float dt) { if (_vm != null) _vm.TickAnim(dt); OnTick(dt); },
                        delegate { if (_vm != null) _vm.ClosePanelAnim(Width); });
                    RegisterSpots();
                    DLog.Force("军团操作页: 已打开");
                }
                else if (_vm != null)
                {
                    _vm.Show(a);
                    RegisterSpots();
                }
            }
            catch (Exception ex) { DLog.Force("打开军团操作页失败: " + ex.Message); }
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

        // 热区(与 FeudalArmyCmd.xml 布局常量一致)
        private static void RegisterSpots()
        {
            try
            {
                if (_vm == null) return;
                PanelScreen.ClearSpots();
                float sh = PanelScreen.ScreenHeight();
                PanelScreen.AddSpot(Width - 60f, 20f, 60f, 58f, _vm.ExecuteClose);         // X
                PanelScreen.AddSpot(26f, sh - 26f - 42f, 122f, 42f, _vm.ExecuteClose);     // 关闭
                PanelScreen.AddSpot(160f, sh - 26f - 42f, 150f, 42f, _vm.ExecuteDisband);  // 解散军团
                for (int i = 0; i < _vm.Rows.Count; i++)
                {
                    int idx = i;
                    float y = 206f + i * 42f;
                    PanelScreen.AddSpot(430f, y, 96f, 34f, delegate { _vm.ExecuteLeave(idx); });   // 出军团
                }
            }
            catch (Exception ex) { DLog.Force("军团操作页热区失败: " + ex.Message); }
        }
    }
}
