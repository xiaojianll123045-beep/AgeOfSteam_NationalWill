using System;

namespace FeudalInternalAffairs
{
    // 政治面板(文档 21.10; 导航栏第 11 格)
    internal static class PoliticsPanel
    {
        private const string Key = "pol";
        private const string Movie = "FeudalPolitics";
        private const float Width = 680f;
        private static PoliticsPanelVM _vm;
        private static float _acc;

        internal static bool IsOpen { get { return PanelScreen.IsOpen(Key); } }

        internal static void Open()
        {
            try
            {
                if (!IsOpen)
                {
                    _vm = new PoliticsPanelVM(Close);
                    _vm.OpenPanelAnim(Width);
                    PanelScreen.OpenPanel(Key, Movie, _vm, null,
                        delegate (float dt) { if (_vm != null) _vm.TickAnim(dt); OnTick(dt); },
                        delegate { if (_vm != null) _vm.ClosePanelAnim(Width); });
                    PanelScreen.SetScrollHandler(Key, delegate (int dir) { if (_vm != null) { _vm.ScrollStep(dir); RegisterSpots(); } });
                    RegisterSpots();
                }
                else if (_vm != null) _vm.Refresh();
            }
            catch (Exception ex) { DLog.Force("打开政治页失败: " + ex.Message); }
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
                if (_vm != null) { _vm.Refresh(); RegisterSpots(); }   // 领主名册行数变化时同步热区
            }
            catch { }
        }

        // 热区(与 FeudalPolitics.xml v4.61 布局常量一致: 页签 240, 领主页内容 300 起)
        private static void RegisterSpots()
        {
            try
            {
                if (_vm == null) return;
                PanelScreen.ClearSpots();
                float sh = PanelScreen.ScreenHeight();
                PanelScreen.AddSpot(Width - 60f, 20f, 60f, 58f, _vm.ExecuteClose);       // X
                PanelScreen.AddSpot(30f, sh - 26f - 42f, 122f, 42f, _vm.ExecuteClose);   // 关闭
                // 页签: 领主 / 法令 / 请愿
                PanelScreen.AddSpot(26f, 240f, 196f, 40f, delegate { _vm.SetTab(0); });
                PanelScreen.AddSpot(238f, 240f, 196f, 40f, delegate { _vm.SetTab(1); });
                PanelScreen.AddSpot(450f, 240f, 196f, 40f, delegate { _vm.SetTab(2); });
                if (_vm.Tab == 0)
                {
                    // 全局操作
                    PanelScreen.AddSpot(26f, 386f, 150f, 36f, _vm.Feast);
                    PanelScreen.AddSpot(181f, 386f, 150f, 36f, _vm.Patrol);
                    PanelScreen.AddSpot(336f, 386f, 150f, 36f, _vm.Suppress);
                    PanelScreen.AddSpot(491f, 386f, 150f, 36f, _vm.Concede);
                    // 领主名册(行位 486 + i*40; 按钮 y+3)
                    for (int i = 0; i < _vm.ShownCount; i++)
                    {
                        int idx = i;
                        float y = 486f + i * 40f + 3f;
                        PanelScreen.AddSpot(424f, y, 48f, 34f, delegate { _vm.LordAction(idx, 0); });
                        PanelScreen.AddSpot(472f, y, 48f, 34f, delegate { _vm.LordAction(idx, 1); });
                        PanelScreen.AddSpot(520f, y, 48f, 34f, delegate { _vm.LordAction(idx, 2); });
                        PanelScreen.AddSpot(568f, y, 48f, 34f, delegate { _vm.LordAction(idx, 3); });
                        PanelScreen.AddSpot(616f, y, 48f, 34f, delegate { _vm.LordAction(idx, 4); });
                    }
                }
                else if (_vm.Tab == 1)
                {
                    // 法令(行位 352 + i*34; 提案 420 / 强推 510)
                    for (int i = 0; i < 6; i++)
                    {
                        int cat = i;
                        float y = 352f + i * 34f;
                        PanelScreen.AddSpot(30f, y - 1f, 380f, 34f, delegate { _vm.SelectLaw(cat); });
                        PanelScreen.AddSpot(420f, y + 2f, 80f, 30f, delegate { _vm.LawAction(cat, false); });
                        PanelScreen.AddSpot(510f, y + 2f, 80f, 30f, delegate { _vm.LawAction(cat, true); });
                    }
                }
                else
                {
                    // 请愿(行位 326 + i*46; 同意 520 / 拒绝 590)
                    int petN = _vm.PetitionRows.Count;
                    for (int i = 0; i < 3 && i < petN; i++)
                    {
                        int idx = i;
                        float y = 326f + i * 46f + 8f;
                        PanelScreen.AddSpot(520f, y, 64f, 30f, delegate { _vm.PetitionAction(idx, true); });
                        PanelScreen.AddSpot(590f, y, 64f, 30f, delegate { _vm.PetitionAction(idx, false); });
                    }
                }
            }
            catch (Exception ex) { DLog.Force("政治页热区失败: " + ex.Message); }
        }
    }
}
