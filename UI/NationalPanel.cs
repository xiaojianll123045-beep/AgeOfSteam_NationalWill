using System;
using SandBox.View.Map;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.Library;

namespace FeudalInternalAffairs
{
    // 国家面板: 左上角国旗(常驻地图层, 点击用轮询) + 侧边栏(PushScreen, 这样按钮才收得到点击)
    internal static class NationalPanel
    {
        private const string Key = "national";

        private static GauntletLayer _flagLayer;
        private static NationalPanelVM _flagVm;
        private static NationalPanelVM _sideVm;
        private static MapScreen _map;
        private static float _acc;

        internal static bool IsOpen { get { return PanelScreen.IsOpen(Key); } }

        // 每帧由 FeudalMapView 调用
        internal static void Tick(float dt)
        {
            try
            {
                EnsureFlagLayer();
                if (_flagLayer == null) return;

                // 国旗点击: 没开 -> 打开根面板(国家面板); 开着根面板 -> 关闭; 开着别的面板 -> 切回根面板
                if (!PanelInputGuard.SuppressAfterInquiry(TaleWorlds.InputSystem.Input.IsKeyDown(TaleWorlds.InputSystem.InputKey.LeftMouseButton)) && IsMouseOnFlag()   // v4.143: 弹窗时不点国旗
                    && TaleWorlds.InputSystem.Input.IsKeyPressed(TaleWorlds.InputSystem.InputKey.LeftMouseButton))
                {
                    if (IsOpen) CloseSidebar();
                    else OpenSidebar();
                }
            }
            catch { }
        }

        private static void EnsureFlagLayer()
        {
            try
            {
                var map = MapScreen.Instance;
                if (map == null)
                {
                    if (_flagLayer != null) CloseFlagLayer();
                    return;
                }
                if (_flagLayer != null)
                {
                    if (ReferenceEquals(_map, map)) return;   // 同一个地图屏, 层有效
                    // 地图屏被重建(游戏内读档等) -> 旧层随旧屏失效, 重建
                    DLog.Force("国家面板: 地图屏已重建, 重新挂国旗");
                    CloseFlagLayer();
                }
                if (!NationalWillOrders.ShouldControlCamera) return;
                if (NationPickMode.Active) return;   // v4.75e: 选国阶段不显示国旗

                _flagVm = new NationalPanelVM();
                _flagVm.OnOpenRequested = OpenSidebar;   // 国旗层不自己开面板, 一律走屏幕
                _flagLayer = new GauntletLayer("FeudalNationalFlag", 344, false);   // v4.75o: 高于国名标签(335)
                _flagLayer.LoadMovie("FeudalNational", _flagVm);
                map.AddLayer(_flagLayer);
                _map = map;
                DLog.Force("国家面板: 国旗已挂到地图(左上角)");
            }
            catch (Exception ex)
            {
                DLog.Force("国旗挂载失败: " + ex.Message);
                try { if (_flagLayer != null && _map != null) _map.RemoveLayer(_flagLayer); } catch { }
                _flagLayer = null; _flagVm = null; _map = null;
            }
        }

        private static void CloseFlagLayer()
        {
            try { if (_flagLayer != null && _map != null) _map.RemoveLayer(_flagLayer); } catch { }
            _flagLayer = null; _flagVm = null; _map = null;
        }

        // 打开侧边栏(独立屏幕, 点击可用)
        internal static void OpenSidebar()
        {
            Tutorials.Page("page_nation");
            try
            {
                if (IsOpen) return;
                _sideVm = new NationalPanelVM();
                _sideVm.OnCloseRequested = CloseSidebar;
                _sideVm.OpenPanel();
                PanelScreen.OpenPanel(Key, "FeudalNational", _sideVm, null,
                    delegate (float dt) { if (_sideVm != null) _sideVm.TickAnim(dt); },
                    delegate { if (_sideVm != null) _sideVm.ClosePanel(); });   // 关闭前先播滑出动画
                RegisterButtonSpots();
            }
            catch (Exception ex) { DLog.Force("打开国家面板失败: " + ex.Message); }
        }

        // 底部四个按钮的热区(兜底点击方案; 布局对应 prefab: 面板宽440, 两行 160x44, 间距16, MarginLeft24, MarginBottom24)
        private static void RegisterButtonSpots()
        {
            try
            {
                if (_sideVm == null) return;
                PanelScreen.ClearSpots();
                float sh = PanelScreen.ScreenHeight();
                float row2 = sh - 24f - 44f;
                float row1 = row2 - 12f - 44f;
                PanelScreen.AddSpot(24f, row1, 120f, 44f, _sideVm.ExecutePopulation);
                PanelScreen.AddSpot(160f, row1, 120f, 44f, _sideVm.ExecuteMarket);
                PanelScreen.AddSpot(296f, row1, 120f, 44f, _sideVm.ExecuteDiplomacy);
                PanelScreen.AddSpot(24f, row2, 160f, 44f, _sideVm.ExecuteBuild);
                PanelScreen.AddSpot(200f, row2, 160f, 44f, _sideVm.ExecuteClose);
                PanelScreen.AddSpot(24f, 322f, 190f, 42f, _sideVm.ExecuteFocus);   // 打开国策树(修复: 之前只有 Command.Click, 收不到点击)
                DLog.Force("国家面板: 已注册 5 个按钮热区(行1 y=" + (int)row1 + " 行2 y=" + (int)row2 + ")");
            }
            catch (Exception ex) { DLog.Force("注册热区失败: " + ex.Message); }
        }

        internal static void CloseSidebar()
        {
            try { PanelScreen.ClosePanel(Key); }
            catch { }
        }

        // 鼠标是否在左上角国旗上(地图点击要避开这块)
        internal static bool IsMouseOnFlag()
        {
            try
            {
                var m = TaleWorlds.InputSystem.Input.MousePositionPixel;
                return m.X >= FlagLeft && m.X <= FlagLeft + FlagWidth
                    && m.Y >= FlagTop && m.Y <= FlagTop + FlagHeight;
            }
            catch { return false; }
        }

        internal const float FlagLeft = 12f;
        internal const float FlagTop = 10f;
        internal const float FlagWidth = 124f;
        internal const float FlagHeight = 124f;
    }
}
