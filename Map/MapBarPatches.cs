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

        // v4.75k: 整条原版地图栏(底部时间/金钱/装饰底框)隐藏 —— 根 widget 的 IsVisible 绑 MapBarVM.IsEnabled
        [HarmonyPatch(typeof(MapBarVM), "Tick")]
        internal static class MapBarVMDressHideTick
        {
            private static void Postfix(MapBarVM __instance) { try { HideBar(__instance); } catch { } }
        }

        [HarmonyPatch(typeof(MapBarVM), "RefreshValues")]
        internal static class MapBarVMDressHideRefresh
        {
            private static void Postfix(MapBarVM __instance) { try { HideBar(__instance); } catch { } }
        }

        private static void HideBar(MapBarVM vm)
        {
            if (vm == null || DLog.Flag("showall")) return;
            var behavior = Campaign.Current != null ? Campaign.Current.GetCampaignBehavior<NationalWillBehavior>() : null;
            bool active = behavior != null && behavior.IsNationalWill;
            if (!active && !NationPickMode.Active) return;
            // v4.75s: 不再整条隐藏 —— 恢复原版时间盘(左中时间/日期/暂停播放); 只隐藏金钱信息栏与召集军队
            try { if (vm.MapInfo != null && vm.MapInfo.IsInfoBarEnabled) vm.MapInfo.IsInfoBarEnabled = false; } catch { }
            try { if (vm.IsGatherArmyVisible) vm.IsGatherArmyVisible = false; } catch { }
        }

        internal static void Enforce(MapNavigationVM vm)
        {
            if (vm == null || DLog.Flag("showall")) return;
            var behavior = Campaign.Current != null ? Campaign.Current.GetCampaignBehavior<NationalWillBehavior>() : null;
            bool active = behavior != null && behavior.IsNationalWill;
            // v4.75i: 选国阶段也先清掉这排按钮(还没接管, 家族/王国/物品等都点不开, 留着碍事)
            if (!active && !NationPickMode.Active) return;
            var items = vm.NavigationItems;
            if (items == null || items.Count <= 1) return;

            bool log = !_loggedOnce;
            var remove = new List<MapNavigationItemVM>();
            foreach (var it in items)
            {
                string id = it != null && it.ItemId != null ? it.ItemId : "";
                // 国家意志: 王国页面完全取缔 -> 连"王国"按钮一起隐藏; 只留系统菜单(esc)
                // 我们自己的按钮(国旗不在这一栏)如仍存在也保留
                bool isOurs = it != null && (it.NavigationElement is FocusTreeNavigationElement
                                             || it.NavigationElement is BuildNavigationElement);
                bool keep = isOurs || id.IndexOf("escape", StringComparison.OrdinalIgnoreCase) >= 0;
                if (!keep) remove.Add(it);
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
