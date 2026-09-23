using System;
using TaleWorlds.CampaignSystem;

namespace FeudalInternalAffairs
{
    // 外交面板(战争/同盟/关系 三页签)
    internal static class DiplomacyPanel
    {
        private const string Key = "diplomacy";
        private const string Movie = "FeudalDiplomacy";
        private static DiplomacyVM _vm;
        private static float _acc;

        internal static bool IsOpen { get { return PanelScreen.IsOpen(Key); } }

        internal static void Open(Kingdom select)
        {
            Tutorials.Page("page_diplo");
            try
            {
                if (!IsOpen)
                {
                    _vm = new DiplomacyVM(Close);
                    _vm.OpenPanelAnim(540f);
                    PanelScreen.OpenPanel(Key, Movie, _vm, null,
                        delegate (float dt) { if (_vm != null) _vm.TickAnim(dt); OnTick(dt); },
                        delegate { if (_vm != null) _vm.ClosePanelAnim(540f); });
                    PanelScreen.SetScrollHandler(Key, delegate (int dir) { if (_vm != null) { _vm.ScrollStep(dir); RegisterSpots(); } });
                }
                if (_vm != null)
                {
                    if (select != null) _vm.SelectKingdom(select);
                    else _vm.Refresh();
                    RegisterSpots();   // 必须先刷新出国家行, 再按行注册热区(否则行按钮点不动)
                }
            }
            catch (Exception ex) { DLog.Force("打开外交面板失败: " + ex.Message); }
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
                if (_vm != null) { _vm.Refresh(); RegisterSpots(); }   // 列表行会随刷新增减, 热区跟着重建
            }
            catch { }
        }

        internal static void Tick(float dt) { }

        // 热区(按 FeudalDiplomacy.xml 优化版布局: 面板宽 540)
        private static void RegisterSpots()
        {
            try
            {
                if (_vm == null) return;
                PanelScreen.ClearSpots();
                float sh = PanelScreen.ScreenHeight();
                PanelScreen.AddSpot(480f, 20f, 60f, 58f, _vm.ExecuteClose);        // X
                PanelScreen.AddSpot(24f, 116f, 150f, 40f, _vm.ExecuteTabWars);      // 页签: 战争
                PanelScreen.AddSpot(180f, 116f, 150f, 40f, _vm.ExecuteTabAllies);   // 页签: 同盟
                PanelScreen.AddSpot(336f, 116f, 150f, 40f, _vm.ExecuteTabRelations);// 页签: 关系
                PanelScreen.AddSpot(420f, 238f, 104f, 32f, _vm.ExecuteSanction);    // v4.114: 经济制裁
                PanelScreen.AddSpot(26f, sh - 24f - 42f, 122f, 42f, _vm.ExecuteClose); // 关闭
                PanelScreen.AddSpot(160f, sh - 24f - 42f, 150f, 42f, PowerBlocUi.Show); // v5.0-P27: 权力集团
        PanelScreen.AddSpot(340f, sh - 24f - 42f, 150f, 42f, TreatyPanel.Open);  // v4.204: 条约(自建页)
                // 国家行(动态): 左段=选中看详情, 右侧单一行动按钮(按状态自动: 求和/解除/缔结/宣战)
                int i = 0;
                foreach (var row in _vm.Rows)
                {
                    if (row == null) continue;
                    float y = 354f + i * 54f;
                    if (y > sh - 120f) break;
                    var r = row;
                    PanelScreen.AddSpot(14f, y, 384f, 54f, r.ExecuteSelect);
                    PanelScreen.AddSpot(400f, y + 7f, 104f, 40f, delegate
                    {
                        if (r.CanMakePeace) r.ExecutePeace();
                        else if (r.CanBreakAlly) r.ExecuteBreakAlly();
                        else if (r.CanAlly) r.ExecuteAlly();
                        else r.ExecuteWar();
                    });
                    i++;
                }
            }
            catch (Exception ex) { DLog.Force("外交面板热区失败: " + ex.Message); }
        }
    }
}
