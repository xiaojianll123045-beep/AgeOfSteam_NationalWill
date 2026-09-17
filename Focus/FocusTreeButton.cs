using System;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ViewModelCollection.Map.MapBar;
using TaleWorlds.Localization;

namespace FeudalInternalAffairs
{
    // 走原版左下角菜单接口的"国策树"按钮
    internal class FocusTreeNavigationElement : INavigationElement
    {
        public string StringId { get { return "focus_tree"; } }
        public NavigationPermissionItem Permission { get { return new NavigationPermissionItem(true, null); } }
        public bool IsLockingNavigation { get { return false; } }
        public bool IsActive { get { return FocusTreeScreen.IsOpen; } }
        public TextObject Tooltip { get { return new TextObject("国策树"); } }
        public bool HasAlert { get { return false; } }
        public TextObject AlertTooltip { get { return null; } }
        public void OpenView() { FocusTreeScreen.Open(); }
        public void OpenView(object[] parameters) { FocusTreeScreen.Open(); }
        public void GoToLink() { }
    }

    // "批量建造"按钮(设计 10.3)
    internal class BuildNavigationElement : INavigationElement
    {
        public string StringId { get { return "fia_build"; } }
        public NavigationPermissionItem Permission { get { return new NavigationPermissionItem(true, null); } }
        public bool IsLockingNavigation { get { return false; } }
        public bool IsActive { get { return BuildPanel.IsOpen; } }
        public TextObject Tooltip { get { return new TextObject("批量建造"); } }
        public bool HasAlert { get { return false; } }
        public TextObject AlertTooltip { get { return null; } }
        public void OpenView() { BatchBuild.Toggle(); }
        public void OpenView(object[] parameters) { BatchBuild.Toggle(); }
        public void GoToLink() { }
    }

    // 把国策树按钮作为地图栏的一项加进去(排在"王国"右边)
    // 注意: 地图栏VM在"选国家之前"就创建了, 构造函数时机太早 -> 改成每帧确保存在
    internal static class FocusTreeButtonPatch
    {
        [HarmonyPatch(typeof(MapNavigationVM), "Tick")]
        internal static class EnsureFocusTreeItem
        {
            private static void Postfix(MapNavigationVM __instance)
            {
                EnsureItem(__instance);
            }
        }

        internal static void EnsureItem(MapNavigationVM vm)
        {
            try
            {
                if (vm == null || !NationalWillOrders.ShouldControlCamera) return;
                var items = vm.NavigationItems;
                if (items == null) return;

                bool hasFocus = false, hasBuild = false;
                foreach (var it in items)
                {
                    if (it == null) continue;
                    if (it.NavigationElement is FocusTreeNavigationElement) hasFocus = true;
                    if (it.NavigationElement is BuildNavigationElement) hasBuild = true;
                }

                if (!hasFocus)
                {
                    var item = MakeItem(new FocusTreeNavigationElement(), "character_developer");
                    if (item != null)
                    {
                        items.Insert(InsertIndexBefore(items, "kingdom"), item);
                        DLog.Force("国策按钮: 已加入地图栏(当前共 " + items.Count + " 项)");
                    }
                }
                if (!hasBuild)
                {
                    var item = MakeItem(new BuildNavigationElement(), "inventory");
                    if (item != null)
                    {
                        items.Insert(InsertIndexBefore(items, "kingdom"), item);
                        DLog.Force("建造按钮: 已加入地图栏(当前共 " + items.Count + " 项)");
                    }
                }
            }
            catch (Exception ex) { DLog.Force("地图栏按钮异常: " + ex.Message); }
        }

        private static MapNavigationItemVM MakeItem(INavigationElement element, string iconItemId)
        {
            var ctor = AccessTools.Constructor(typeof(MapNavigationItemVM), new Type[] { typeof(INavigationElement) });
            if (ctor == null) { DLog.Force("地图栏按钮: 找不到 MapNavigationItemVM 构造"); return null; }
            var item = ctor.Invoke(new object[] { element }) as MapNavigationItemVM;
            if (item != null) item.ItemId = iconItemId;
            return item;
        }

        private static int InsertIndexBefore(System.Collections.IList items, string keyword)
        {
            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i] as MapNavigationItemVM;
                var id = it != null ? it.ItemId : null;
                if (id != null && id.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0) return i;
            }
            return items.Count;
        }
    }
}
