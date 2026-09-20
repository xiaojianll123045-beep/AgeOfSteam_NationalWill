using System;

namespace FeudalInternalAffairs
{
    // 封建契约侧栏面板(v4.141; 独立侧边栏, 用户要求)
    internal static class ContractsPanel
    {
        private const string Key = "contracts";
        private const string Movie = "FeudalContracts";
        private const float Width = 720f;
        private static ContractsPanelVM _vm;
        private static float _acc;

        internal static bool IsOpen { get { return PanelScreen.IsOpen(Key); } }

        internal static void Open()
        {
            Tutorials.Page("page_politics");
            try
            {
                if (!IsOpen)
                {
                    _vm = new ContractsPanelVM(Close);
                    _vm.OpenPanelAnim(Width);
                    PanelScreen.OpenPanel(Key, Movie, _vm, null,
                        delegate (float dt) { if (_vm != null) _vm.TickAnim(dt); OnTick(dt); },
                        delegate { if (_vm != null) _vm.ClosePanelAnim(Width); });
                    PanelScreen.SetScrollHandler(Key, delegate (int dir) { if (_vm != null) { _vm.ScrollStep(dir); RegisterSpots(); } });
                    RegisterSpots();
                }
                else if (_vm != null) { _vm.Refresh(); RegisterSpots(); }
            }
            catch (Exception ex) { DLog.Force("打开契约侧栏失败: " + ex.Message); }
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
                if (_vm != null) { _vm.Refresh(); RegisterSpots(); }
            }
            catch { }
        }

        private static void Act(Action a)
        {
            try { a(); } catch (Exception ex) { DLog.Force("契约侧栏动作异常: " + ex.Message); }
            RegisterSpots();
        }

        // 热区(与 FeudalContracts.xml 布局常量一致; 面板宽 720, 行高 72, 起点 206)
        private static void RegisterSpots()
        {
            try
            {
                if (_vm == null) return;
                PanelScreen.ClearSpots();
                float sh = PanelScreen.ScreenHeight();
                PanelScreen.AddSpot(660f, 20f, 60f, 58f, _vm.ExecuteClose);            // X
                PanelScreen.AddSpot(30f, sh - 26f - 42f, 122f, 42f, _vm.ExecuteClose); // 关闭
                for (int row = 0; row < _vm.RowCount; row++)
                {
                    int r = row;
                    float y = 206f + row * 72f + 20f;
                    // 税赋 4 格
                    for (int i = 0; i < 4; i++)
                    {
                        int tier = i;
                        PanelScreen.AddSpot(240f + i * 32f, y, 28f, 28f, delegate { Act(delegate { _vm.SetTaxTier(r, tier); }); });
                    }
                    // 兵役 4 格
                    for (int i = 0; i < 4; i++)
                    {
                        int tier = i;
                        PanelScreen.AddSpot(400f + i * 32f, y, 28f, 28f, delegate { Act(delegate { _vm.SetLevyTier(r, tier); }); });
                    }
                }
            }
            catch (Exception ex) { DLog.Force("契约侧栏热区失败: " + ex.Message); }
        }
    }
}
