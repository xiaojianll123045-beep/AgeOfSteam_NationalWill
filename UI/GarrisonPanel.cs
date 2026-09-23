using System;

namespace FeudalInternalAffairs
{
    // v4.101: 点城市弹出的"驻军列表"面板(原版驻军兵种 + 国防军守备营 + 附近我军 + 联军信息)
    internal static class GarrisonPanel
    {
        private const string Key = "garrison";
        private const string Movie = "FeudalGarrison";
        private const float Width = 560f;
        private static GarrisonPanelVM _vm;
        private static float _acc;

        internal static bool IsOpen { get { return PanelScreen.IsOpen(Key); } }

        internal static void Open(TaleWorlds.CampaignSystem.Settlements.Settlement s)
        {
            Tutorials.Page("page_garrison");
            try
            {
                if (s == null) return;
                if (!IsOpen)
                {
                    _vm = new GarrisonPanelVM(Close);
                    _vm.Show(s);
                    _vm.OpenPanelAnim(Width);
                    PanelScreen.OpenPanel(Key, Movie, _vm, null,
                        delegate (float dt) { if (_vm != null) _vm.TickAnim(dt); OnTick(dt); },
                        delegate { if (_vm != null) _vm.ClosePanelAnim(Width); });
                    RegisterSpots();
                    DLog.Force("驻军列表: 打开 " + (s.Name != null ? s.Name.ToString() : s.StringId));
                }
                else if (_vm != null)
                {
                    _vm.Show(s);
                    RegisterSpots();
                }
            }
            catch (Exception ex) { DLog.Force("打开驻军列表失败: " + ex.Message); }
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
                if (_acc < 2f) return;
                _acc = 0f;
                if (_vm != null) { _vm.Refresh(); RegisterSpots(); }
            }
            catch { }
        }

        // 热区(与 FeudalGarrison.xml 布局常量一致)
        private static void RegisterSpots()
        {
            try
            {
                if (_vm == null) return;
                PanelScreen.ClearSpots();
                float sh = PanelScreen.ScreenHeight();
                PanelScreen.AddSpot(Width - 60f, 20f, 60f, 58f, _vm.ExecuteClose);           // X
                PanelScreen.AddSpot(26f, sh - 26f - 42f, 122f, 42f, _vm.ExecuteClose);       // 关闭
                PanelScreen.AddSpot(160f, sh - 26f - 42f, 150f, 42f, _vm.ExecuteCallOut);    // 唤出军团
                PanelScreen.AddSpot(322f, sh - 26f - 42f, 130f, 42f, _vm.ExecuteOpenDrawer); // 查看建筑
                PanelScreen.AddSpot(26f, sh - 26f - 42f - 50f, 150f, 42f, delegate { RailwaysUi.TransportMenu(_vm.Current); });   // v4.164: 铁路运输
                for (int i = 0; i < _vm.Rows.Count; i++)
                {
                    int idx = i;
                    float y = 232f + i * 42f;
                    PanelScreen.AddSpot(466f, y, 74f, 34f, delegate { _vm.ExecuteRowAction(idx); });   // 出城(部队行)
                }
            }
            catch (Exception ex) { DLog.Force("驻军列表热区失败: " + ex.Message); }
        }
    }
}
