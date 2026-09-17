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
            try
            {
                if (!IsOpen)
                {
                    _vm = new DiplomacyVM(Close);
                    _vm.OpenPanelAnim(540f);
                    PanelScreen.OpenPanel(Key, Movie, _vm, null,
                        delegate (float dt) { if (_vm != null) _vm.TickAnim(dt); OnTick(dt); },
                        delegate { if (_vm != null) _vm.ClosePanelAnim(540f); });
                    RegisterSpots();
                }
                if (_vm != null)
                {
                    if (select != null) _vm.SelectKingdom(select);
                    else _vm.Refresh();
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
                if (_vm != null) _vm.Refresh();
            }
            catch { }
        }

        internal static void Tick(float dt) { }

        // 热区(按 FeudalDiplomacy.xml: 面板宽 540, 无 MarginTop)
        private static void RegisterSpots()
        {
            try
            {
                if (_vm == null) return;
                PanelScreen.ClearSpots();
                float sh = PanelScreen.ScreenHeight();
                PanelScreen.AddSpot(480f, 20f, 60f, 58f, _vm.ExecuteClose);        // X
                PanelScreen.AddSpot(24f, 138f, 122f, 46f, _vm.ExecuteTabWars);     // 页签: 战争
                PanelScreen.AddSpot(160f, 138f, 122f, 46f, _vm.ExecuteTabAllies);  // 页签: 同盟
                PanelScreen.AddSpot(296f, 138f, 122f, 46f, _vm.ExecuteTabRelations); // 页签: 关系
                PanelScreen.AddSpot(24f, sh - 24f - 42f, 122f, 42f, _vm.ExecuteClose); // 关闭
                // 国家行(动态): 左段=选中看详情, 右两个按钮=求和/宣战 与 缔结/解除同盟
                int i = 0;
                foreach (var row in _vm.Rows)
                {
                    if (row == null) continue;
                    float y = 322f + i * 56f;
                    if (y > sh - 120f) break;
                    var r = row;
                    PanelScreen.AddSpot(14f, y, 270f, 56f, r.ExecuteSelect);
                    if (r.CanMakePeace) PanelScreen.AddSpot(292f, y + 8f, 100f, 40f, r.ExecutePeace);
                    else if (r.CanDeclareWar) PanelScreen.AddSpot(292f, y + 8f, 100f, 40f, r.ExecuteWar);
                    if (r.CanAlly) PanelScreen.AddSpot(396f, y + 8f, 128f, 40f, r.ExecuteAlly);
                    else if (r.CanBreakAlly) PanelScreen.AddSpot(396f, y + 8f, 128f, 40f, r.ExecuteBreakAlly);
                    i++;
                }
            }
            catch (Exception ex) { DLog.Force("外交面板热区失败: " + ex.Message); }
        }
    }
}
