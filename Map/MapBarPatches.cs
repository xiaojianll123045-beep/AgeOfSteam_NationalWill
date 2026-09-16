using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ViewModelCollection.Map.MapBar;

namespace FeudalInternalAffairs
{
    // 国家意志没有"个人生活": 地图左下角只留"王国"和系统菜单按钮
    // 想恢复原版: flags 文件写 showall
    internal static class MapBarPatches
    {
        [HarmonyPatch(typeof(MapNavigationVM), MethodType.Constructor,
            new Type[] { typeof(INavigationHandler), typeof(Func<MapBarShortcuts>) })]
        internal static class MapBarHidePatch
        {
            private static void Postfix(MapNavigationVM __instance)
            {
                try { Enforce(__instance); }
                catch (Exception ex) { DLog.Force("MapBarHidePatch 异常: " + ex.Message); }
            }
        }

        [HarmonyPatch(typeof(MapNavigationVM), "Tick")]
        internal static class MapBarHideTickPatch
        {
            private static void Postfix(MapNavigationVM __instance)
            {
                try { MapBarPatches.Enforce(__instance); }
                catch { }
            }
        }

        internal static void Enforce(MapNavigationVM vm)
        {
            if (vm == null || DLog.Flag("showall")) return;
            var behavior = Campaign.Current != null ? Campaign.Current.GetCampaignBehavior<NationalWillBehavior>() : null;
            if (behavior == null || !behavior.IsNationalWill) return;
            var items = vm.NavigationItems;
            if (items == null || items.Count <= 1) return;

            bool log = !_loggedOnce;
            var remove = new List<MapNavigationItemVM>();
            foreach (var it in items)
            {
                string id = it != null && it.ItemId != null ? it.ItemId : "";
                bool isKingdom = id.IndexOf("kingdom", StringComparison.OrdinalIgnoreCase) >= 0;
                // 我们自己的国策树按钮要保留(它借用了角色图标, id 会是 character_developer)
                bool isOurs = it != null && it.NavigationElement is FocusTreeNavigationElement;
                bool keep = isKingdom || isOurs || id.IndexOf("escape", StringComparison.OrdinalIgnoreCase) >= 0;
                if (!keep) remove.Add(it);
                else if (isKingdom && !it.IsEnabled) it.IsEnabled = true;
                if (log) DLog.Info("地图栏按钮: " + id + (keep ? " [保留]" : " [隐藏]"));
            }
            if (remove.Count == 0) return;
            foreach (var it in remove) items.Remove(it);
            if (log)
            {
                _loggedOnce = true;
                DLog.Force("地图栏已只保留王国与系统菜单, 隐藏 " + remove.Count + " 个(" + string.Join(",", remove.Select(x => x.ItemId)) + ")");
            }
        }

        private static bool _loggedOnce;
    }
}
