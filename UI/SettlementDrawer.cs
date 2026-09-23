using System;
using TaleWorlds.CampaignSystem.Settlements;

namespace FeudalInternalAffairs
{
    // 定居点抽屉: 态A 本国定居点树 / 态B 建筑列表
    // 用 PushScreen(带按钮的地图界面只有屏幕方案能收到点击)
    internal static class SettlementDrawer
    {
        private const string Key = "drawer";
        private const string Movie = "FeudalDrawer";
        private static SettlementDrawerVM _vm;
        private static float _acc;

        internal static bool IsOpen { get { return PanelScreen.IsOpen(Key); } }

        internal static void Open(Settlement s)
        {
            Tutorials.Page("page_drawer");
            try
            {
                if (IsOpen)
                {
                    if (_vm != null && s != null) _vm.ShowBuildings(s);
                    return;
                }
                _vm = new SettlementDrawerVM(Close);
                if (s != null) _vm.ShowBuildings(s);
                _vm.OpenPanelAnim(560f);
                PanelScreen.OpenPanel(Key, Movie, _vm, null,
                    delegate (float dt) { if (_vm != null) _vm.TickAnim(dt); OnTick(dt); },
                    delegate { if (_vm != null) _vm.ClosePanelAnim(560f); });
                PanelScreen.SetScrollHandler(Key, delegate (int dir) { if (_vm != null) { _vm.ScrollStep(dir); RegisterSpots(); } });
                RegisterSpots();
            }
            catch (Exception ex) { DLog.Force("打开抽屉失败: " + ex.Message); }
        }

        internal static void Close()
        {
            try { PanelScreen.ClosePanel(Key); }
            catch { }
        }

        // 低频刷新(2 秒一次)
        private static void OnTick(float dt)
        {
            try
            {
                _acc += dt;
                if (_acc < 2f) return;
                _acc = 0f;
                if (_vm != null) _vm.Refresh();
                RegisterSpots();   // 列表内容会变(切态/折叠/搜索), 定期重建热区
            }
            catch { }
        }

        internal static void Tick(float dt) { }

        // 热区(按 FeudalDrawer.xml 布局: 面板宽 560, 无 MarginTop)
        private static void RegisterSpots()
        {
            try
            {
                if (_vm == null) return;
                PanelScreen.ClearSpots();
                float sh = PanelScreen.ScreenHeight();
                PanelScreen.AddSpot(500f, 20f, 60f, 58f, _vm.ExecuteClose);                    // X
                if (_vm.IsBuildMode)
                {
                    PanelScreen.AddSpot(26f, sh - 26f - 42f, 124f, 42f, _vm.ExecuteBack);      // 返回树
                    PanelScreen.AddSpot(160f, sh - 26f - 42f, 150f, 42f, _vm.ExecuteCallOut);  // v4.100: 唤出军团
                    int i = 0;
                    foreach (var row in _vm.Rows)
                    {
                        if (row == null) continue;
                        float y = 224f + i * 54f;
                        if (y > sh - 120f) break;
                        var r = row;
                        PanelScreen.AddSpot(448f, y + 6f, 42f, 42f, r.ExecuteRemove);          // −
                        PanelScreen.AddSpot(506f, y + 6f, 42f, 42f, r.ExecuteAdd);             // +
                        i++;
                    }
                }
                else
                {
                    int i = 0;
                    foreach (var node in _vm.Nodes)
                    {
                        if (node == null || !node.IsVisible) continue;
                        float y = 92f + i * 52f;
                        if (y > sh - 60f) break;
                        var n = node;
                        PanelScreen.AddSpot(0f, y, 42f, 52f, n.ExecuteToggle);                // 展开/折叠
                        PanelScreen.AddSpot(42f, y, 480f, 52f, n.ExecuteSelect);               // 选中(飘相机+看建筑)
                        i++;
                    }
                }
            }
            catch (Exception ex) { DLog.Force("抽屉热区失败: " + ex.Message); }
        }
    }
}
