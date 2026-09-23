using System;

namespace FeudalInternalAffairs
{
    // 财政面板(左侧边栏; 文档 19.14.4)
    internal static class FiscalPanel
    {
        private const string Key = "fiscal";
        private const string Movie = "FeudalFiscal";
        private const float Width = 680f;
        private static FiscalPanelVM _vm;
        private static float _acc;

        internal static bool IsOpen { get { return PanelScreen.IsOpen(Key); } }

        internal static void Open()
        {
            Tutorials.Page("page_fiscal");
            try
            {
                if (!IsOpen)
                {
                    _vm = new FiscalPanelVM(Close);
                    _vm.OpenPanelAnim(Width);
                    PanelScreen.OpenPanel(Key, Movie, _vm, null,
                        delegate (float dt) { if (_vm != null) _vm.TickAnim(dt); OnTick(dt); },
                        delegate { if (_vm != null) _vm.ClosePanelAnim(Width); });
                    RegisterSpots();
                }
                else if (_vm != null) _vm.Refresh();
            }
            catch (Exception ex) { DLog.Force("打开财政面板失败: " + ex.Message); }
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

        private static void RegisterSpots()
        {
            try
            {
                if (_vm == null) return;
                PanelScreen.ClearSpots();
                float sh = PanelScreen.ScreenHeight();
                PanelScreen.AddSpot(Width - 60f, 20f, 60f, 58f, _vm.ExecuteClose);       // X
                PanelScreen.AddSpot(30f, sh - 26f - 42f, 122f, 42f, _vm.ExecuteClose); // 关闭
                PanelScreen.AddSpot(160f, sh - 26f - 42f, 150f, 42f, _vm.ExecuteReserve); // v4.114: 战略储备
                PanelScreen.AddSpot(320f, sh - 26f - 42f, 150f, 42f, TradeUi.Open);      // v5.0-P24/v4.152: 贸易路线
                PanelScreen.AddSpot(480f, sh - 26f - 42f, 150f, 42f, RailwaysUi.Open);   // v4.162: 铁路管理
        PanelScreen.AddSpot(30f, sh - 78f - 42f, 150f, 42f, delegate { ActionPage.Open("chest"); });     // v4.211: 钱箱(自建页)
        PanelScreen.AddSpot(200f, sh - 78f - 42f, 150f, 42f, delegate { ActionPage.Open("parly"); }); // v4.211: 议会表决(自建页)
                // 重构版: 税制四行(点格选档 / 整行循环) 右栏 x=360 起; 行位 250 + i*34
                for (int i = 0; i < 4; i++)
                {
                    float y = 250f + i * 34f;
                    for (int k = 0; k < 5; k++)
                    {
                        int kind = i, lv = k;
                        float px = 552f + k * 21f;
                        PanelScreen.AddSpot(px, y, 20f, 20f, delegate { _vm.SetTaxLevel(kind, lv); });
                    }
                    {
                        int kind = i;
                        PanelScreen.AddSpot(360f, y - 4f, 300f, 30f, delegate { _vm.TaxClick(kind); });
                    }
                }
                // 铸币权: 三格(足银/九成/七成) + 整行循环
                for (int k = 0; k < 3; k++)
                {
                    int lv = k;
                    PanelScreen.AddSpot(600f + k * 21f, 474f, 20f, 20f, delegate { _vm.SetMintPurity(lv); });
                }
                PanelScreen.AddSpot(360f, 470f, 300f, 30f, _vm.MintClick);
            }
            catch (Exception ex) { DLog.Force("财政面板热区失败: " + ex.Message); }
        }
    }
}
