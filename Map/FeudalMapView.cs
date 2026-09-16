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

        protected override void OnMapScreenUpdate(float dt)
        {
            base.OnMapScreenUpdate(dt);
            try
            {
                Current = this;
                MapClickPatches.ArmyRightClickMenu.Tick();
                MapVisionPatches.TickVisibility(dt);
                TerritoryColorMode.Tick(dt);
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
