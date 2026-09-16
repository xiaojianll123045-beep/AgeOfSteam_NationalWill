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
                foreach (var it in items)
                    if (it != null && it.NavigationElement is FocusTreeNavigationElement) return;   // 我们的按钮已在

                var ctor = AccessTools.Constructor(typeof(MapNavigationItemVM), new Type[] { typeof(INavigationElement) });
                if (ctor == null) { DLog.Force("国策按钮: 找不到 MapNavigationItemVM 构造"); return; }
                var item = ctor.Invoke(new object[] { new FocusTreeNavigationElement() }) as MapNavigationItemVM;
                if (item == null) return;

                item.ItemId = "character_developer";   // 借用原版"角色/技能"图标(笔刷里已存在)

                int insertAt = items.Count;
                for (int i = 0; i < items.Count; i++)
                {
                    var id = items[i] != null ? items[i].ItemId : null;
                    if (id != null && id.IndexOf("kingdom", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        insertAt = i;   // 插在"王国"左边
                        break;
                    }
                }
                items.Insert(insertAt, item);
                DLog.Force("国策按钮: 已加入地图栏(位置 " + insertAt + ", 当前共 " + items.Count + " 项)");
            }
            catch (Exception ex) { DLog.Force("国策按钮异常: " + ex.Message); }
        }
    }
}
