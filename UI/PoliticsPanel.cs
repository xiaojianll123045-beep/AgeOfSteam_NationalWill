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
                    PanelScreen.SetScrollHandler(Key, delegate (int dir) { if (_vm != null) _vm.ScrollStep(dir); });
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

        // 热区(与 FeudalPolitics.xml 布局常量一致)
        private static void RegisterSpots()
        {
            try
            {
                if (_vm == null) return;
                PanelScreen.ClearSpots();
                float sh = PanelScreen.ScreenHeight();
                PanelScreen.AddSpot(Width - 60f, 20f, 60f, 58f, _vm.ExecuteClose);       // X
                PanelScreen.AddSpot(30f, sh - 26f - 42f, 122f, 42f, _vm.ExecuteClose); // 关闭
                // 全局操作
                PanelScreen.AddSpot(26f, 352f, 150f, 36f, _vm.Feast);
                PanelScreen.AddSpot(181f, 352f, 150f, 36f, _vm.Patrol);
                PanelScreen.AddSpot(336f, 352f, 150f, 36f, _vm.Suppress);
                PanelScreen.AddSpot(491f, 352f, 150f, 36f, _vm.Concede);
                // 领主名册: 每行五个按钮(赏赐/授勋/任职/罢免/处决; 行位 404 + i*38)
                for (int i = 0; i < _vm.ShownCount; i++)
                {
                    int idx = i;
                    float y = 440f + i * 40f;
                    PanelScreen.AddSpot(424f, y + 2f, 48f, 34f, delegate { _vm.LordAction(idx, 0); });
                    PanelScreen.AddSpot(472f, y + 2f, 48f, 34f, delegate { _vm.LordAction(idx, 1); });
                    PanelScreen.AddSpot(520f, y + 2f, 48f, 34f, delegate { _vm.LordAction(idx, 2); });
                    PanelScreen.AddSpot(568f, y + 2f, 48f, 34f, delegate { _vm.LordAction(idx, 3); });
                    PanelScreen.AddSpot(616f, y + 2f, 48f, 34f, delegate { _vm.LordAction(idx, 4); });
                }
                // 法令: 点整行看效果/名单 + 提案 / 强推(行位 822 + i*27)
                for (int i = 0; i < 6; i++)
                {
                    int cat = i;
                    float y = 890f + i * 29f;
                    PanelScreen.AddSpot(30f, y - 1f, 380f, 27f, delegate { _vm.SelectLaw(cat); });
                    PanelScreen.AddSpot(420f, y, 80f, 26f, delegate { _vm.LawAction(cat, false); });
                    PanelScreen.AddSpot(510f, y, 80f, 26f, delegate { _vm.LawAction(cat, true); });
                }
                // 请愿: 同意 / 拒绝(行位 1022 + i*27)
                for (int i = 0; i < 3; i++)
                {
                    int idx = i;
                    float y = 1108f + i * 29f;
                    PanelScreen.AddSpot(520f, y, 64f, 26f, delegate { _vm.PetitionAction(idx, true); });
                    PanelScreen.AddSpot(590f, y, 64f, 26f, delegate { _vm.PetitionAction(idx, false); });
                }
            }
            catch (Exception ex) { DLog.Force("政治页热区失败: " + ex.Message); }
        }
    }
}
