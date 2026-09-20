using System;
using TaleWorlds.CampaignSystem.Settlements;

namespace FeudalInternalAffairs
{
    // 市场面板(商品/贸易/粮食安全 三页签)
    internal static class MarketPanel
    {
        private const string Key = "market";
        private const string Movie = "FeudalMarket";
        private static MarketVM _vm;
        private static float _acc;

        internal static bool IsOpen { get { return PanelScreen.IsOpen(Key); } }

        // "本城"模式下等玩家在地图上点城市(供 MapClickPatches 判断)
        internal static bool IsPickingCity
        {
            get { try { return _vm != null && IsOpen && _vm.IsPickingCity; } catch { return false; } }
        }

        // 地图上点了城市 -> 显示该城市场(不打开定居点抽屉)
        internal static void PickCity(Settlement s)
        {
            try { if (s != null && s.IsTown && _vm != null) _vm.SetTown(s.StringId); }
            catch (Exception ex) { DLog.Force("选择城市市场失败: " + ex.Message); }
        }

        internal static void Open(string townId)
        {
            try
            {
                if (!IsOpen)
                {
                    _vm = new MarketVM(Close);
                    _vm.OpenPanelAnim(780f);
                    PanelScreen.OpenPanel(Key, Movie, _vm, null,
                        delegate (float dt) { if (_vm != null) _vm.TickAnim(dt); OnTick(dt); },
                        delegate { if (_vm != null) _vm.ClosePanelAnim(780f); });
                    // 滚轮: 层收不到 -> 注册"滚一行"的回调, 由 PanelScreen 轮询触发
                    PanelScreen.SetScrollHandler(Key, delegate (int dir) { if (_vm != null) _vm.ScrollStep(dir); });
                    RegisterSpots();
                }
                if (_vm != null) _vm.SetTown(townId);
            }
            catch (Exception ex) { DLog.Force("打开市场面板失败: " + ex.Message); }
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
                // "本城"待选: 自己轮询左键(原版 SettlementVisual.OnMapClick 会被面板层吃掉)
                if (_vm != null && _vm.IsPickingCity && !PanelScreen.IsMouseOnPanel()
                    && TaleWorlds.InputSystem.Input.IsKeyPressed(TaleWorlds.InputSystem.InputKey.LeftMouseButton))
                {
                    try
                    {
                        var pt = MapBoxSelect.CaptureGroundPoint();
                        var s = TerritoryData.SettlementAt(pt.x, pt.y);
                        if (s != null && s.IsTown) _vm.SetTown(s.StringId);
                    }
                    catch { }
                }
                // 悬停提示(每帧): 表格行区从 y=378 起(320 顶部区 + 28 表头 + 30 间距), 行高 68
                if (_vm != null)
                {
                    var m = TaleWorlds.InputSystem.Input.MousePositionPixel;
                    if (PanelScreen.IsMouseOnPanel() && m.Y >= 378f)
                        _vm.SetHover((int)((m.Y - 378f) / 68f), m.X, m.Y);   // 行高 68(第二行字号加大后)
                    else
                        _vm.SetHover(-1, m.X, m.Y);
                }
                _acc += dt;
                if (_acc < 3f) return;
                _acc = 0f;
                if (_vm != null) { _vm.Refresh(); RegisterSpots(); }
            }
            catch { }
        }

        internal static void Tick(float dt) { }

        // 热区(按 FeudalMarket.xml: 面板宽 720, 无 MarginTop)
        private static void RegisterSpots()
        {
            try
            {
                if (_vm == null) return;
                PanelScreen.ClearSpots();
                float sh = PanelScreen.ScreenHeight();
                PanelScreen.AddSpot(720f, 20f, 60f, 58f, _vm.ExecuteClose);          // X
                PanelScreen.AddSpot(616f, 20f, 100f, 46f, _vm.ExecuteToggleScope);   // 全国/本城
                PanelScreen.AddSpot(26f, 186f, 130f, 44f, _vm.ExecuteTabGoods);      // 页签: 商品
                PanelScreen.AddSpot(170f, 186f, 130f, 44f, _vm.ExecuteTabTrade);     // 页签: 贸易
                PanelScreen.AddSpot(314f, 186f, 150f, 44f, _vm.ExecuteTabFood);      // 页签: 粮食安全
                PanelScreen.AddSpot(26f, sh - 26f - 42f, 122f, 42f, _vm.ExecuteClose); // 关闭
                // 分类过滤(仅在商品页签可见, 位置固定)
                float fy = 240f;
                PanelScreen.AddSpot(26f, fy, 76f, 52f, _vm.ExecuteFilterAll);
                PanelScreen.AddSpot(112f, fy, 76f, 52f, _vm.ExecuteFilter0);
                PanelScreen.AddSpot(198f, fy, 76f, 52f, _vm.ExecuteFilter1);
                PanelScreen.AddSpot(284f, fy, 76f, 52f, _vm.ExecuteFilter2);
                PanelScreen.AddSpot(370f, fy, 76f, 52f, _vm.ExecuteFilter3);
                PanelScreen.AddSpot(456f, fy, 76f, 52f, _vm.ExecuteFilter4);
                // v4.58: 城市名点击 -> 视野飞到该城(标题=本城名; 贸易/粮食行=行首城市名)
                PanelScreen.AddSpot(14f, 14f, 400f, 62f, _vm.FlyToTown);
                if (_vm.IsTradeTab)
                {
                    for (int i = 0; i < _vm.TradeRows.Count; i++)
                    {
                        int idx = i;
                        float ry = 310f + i * 48f;
                        if (ry > sh - 160f) break;
                        PanelScreen.AddSpot(14f, ry, 250f, 44f, delegate { _vm.FlyToTradeRow(idx); });
                    }
                }
                else if (_vm.IsFoodTab)
                {
                    for (int i = 0; i < _vm.FoodRows.Count; i++)
                    {
                        int idx = i;
                        float ry = 310f + i * 48f;
                        if (ry > sh - 160f) break;
                        PanelScreen.AddSpot(14f, ry, 250f, 44f, delegate { _vm.FlyToFoodRow(idx); });
                    }
                }
            }
            catch (Exception ex) { DLog.Force("市场面板热区失败: " + ex.Message); }
        }
    }
}
