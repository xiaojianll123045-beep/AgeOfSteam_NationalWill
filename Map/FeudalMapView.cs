using System;
using HarmonyLib;
using SandBox.View.Map;

namespace FeudalInternalAffairs
{
    // 地图视图: OnMapScreenUpdate 每帧都会调用(暂停时也调用),
    // 政治地图覆盖层 / 国界线的更新挂在这里(而不是 CampaignEvents.Tick, 那个暂停时不触发)
    internal class FeudalMapView : MapView
    {
        internal static FeudalMapView Current;

        // 帧耗时诊断: 面板打开时若帧间隔异常大, 写日志(每 3 秒最多一条)
        private static float _slowAcc;
        private static float _lastLogTime;
        private static readonly DateTime _startTime = DateTime.UtcNow;
        private static void DiagFrameTime(float dt)
        {
            try
            {
                if (dt < 0.05f) return;   // 正常帧(<20ms 的间隔不会被记为慢)
                _slowAcc += dt;
                float now = (float)(DateTime.UtcNow - _startTime).TotalSeconds;
                if (now - _lastLogTime < 3f) return;
                _lastLogTime = now;
                DLog.Force("帧耗时诊断: 单帧 dt=" + (dt * 1000f).ToString("F0") + "ms (面板=" + PanelScreen.AnyOpen
                    + ", 侧边栏数=" + (PanelScreen.AnyOpen ? 1 : 0) + "), 3秒内累计慢帧=" + (_slowAcc * 1000f).ToString("F0") + "ms");
                _slowAcc = 0f;
            }
            catch { }
        }

        protected override void OnMapScreenUpdate(float dt)
        {
            base.OnMapScreenUpdate(dt);
            try
            {
                Current = this;
                DiagFrameTime(dt);   // 帧耗时诊断(定位卡顿)
                // v4.75j: 每帧推进国家意志初始化(不依赖 CampaignEvents.TickEvent, 它暂停/锁时间时可能不触发)
                try
                {
                    var b = NationalWillOrders.Behavior;
                    if (b != null) b.DriveSetup();
                }
                catch { }
                MapClickPatches.ArmyRightClickMenu.Tick();
                MapVisionPatches.TickVisibility(dt);
                NationPickMode.Tick(dt);      // v4.75: 新档选国模式(锁最高视角/锁时间)
                NationPickPanel.Tick(dt);     // v4.75: 右侧选国侧栏动画
                StartGameFade.Tick(dt);       // v4.75b: 开始游戏黑屏过渡
                TerritoryColorMode.Tick(dt);  // 先生成标签数据(用当前相机)
                FeudalMapLabels.Tick(dt);     // 再同步到标签层(同帧, 消除一帧滞后导致的移动抖动)
                SettlementDrawer.Tick(dt);
                BuildPanel.Tick();
                NationalPanel.Tick(dt);
                NavRail.Tick(dt);   // v3.0: 左侧导航栏
                PanelScreen.Tick(dt);   // 侧边栏刷新(层方式, 地图照常更新)
            }
            catch { }
        }

        protected override void OnFinalize()
        {
            try { if (ReferenceEquals(Current, this)) Current = null; } catch { }
            base.OnFinalize();
        }
    }

    // MapScreen 初始化时把我们的地图视图挂上去
    [HarmonyPatch(typeof(MapScreen), "OnInitialize")]
    internal static class FeudalMapViewPatch
    {
        private static void Postfix(MapScreen __instance)
        {
            try
            {
                if (FeudalMapView.Current != null) return;
                var view = __instance.AddMapView<FeudalMapView>(Array.Empty<object>()) as FeudalMapView;
                FeudalMapView.Current = view;
                DLog.Force(view != null ? "政治地图: MapView 已挂载(每帧回调)" : "政治地图: MapView 挂载失败");
            }
            catch (Exception ex) { DLog.Force("挂载 MapView 失败: " + ex.Message); }
        }
    }
}
