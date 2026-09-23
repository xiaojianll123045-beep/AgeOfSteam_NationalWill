using System;
using System.Collections.Generic;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ViewModelCollection.Map.MapBar;
using TaleWorlds.Core.ViewModelCollection.Information;

namespace FeudalInternalAffairs
{
    // 右下角信息栏改成"选中对象的数值"(连悬停提示一起改):
    //   单选 -> 该部队(领主)的数值
    //   多选 -> 钱 = 选中人员总和, 其余显示 ***
    //   没选 -> 钱 = 全国(本国所有家族)总和, 其余显示 ***
    // 舰船数量: 海战DLC 自带该项(NavalMapInfoVM._shipHealthInfo), 这里接管它跟随选中部队
    internal static class MapInfoSelectionPatch
    {
        [HarmonyPatch(typeof(MapInfoVM), "UpdatePlayerInfo")]
        internal static class OverrideInfoBase
        {
            private static void Postfix(MapInfoVM __instance) { ApplyOverride(__instance); }
        }

        // DLC 的信息栏是 NavalMapInfoVM(重写了 UpdatePlayerInfo, 基类补丁不会跑) -> 单独挂
        internal static class OverrideInfoNaval
        {
            internal static void Apply(Harmony harmony)
            {
                try
                {
                    var t = AccessTools.TypeByName("NavalDLC.ViewModelCollection.Map.MapBar.NavalMapInfoVM");
                    if (t == null) { DLog.Info("信息栏: 没有海战DLC(NavalMapInfoVM 不存在)"); return; }
                    var m = AccessTools.Method(t, "UpdatePlayerInfo");
                    if (m == null) { DLog.Force("信息栏: 找不到 NavalMapInfoVM.UpdatePlayerInfo"); return; }
                    harmony.Patch(m, postfix: new HarmonyMethod(
                        AccessTools.Method(typeof(OverrideInfoNaval), "Postfix")));
                    DLog.Force("信息栏: 已挂载海战DLC版补丁(NavalMapInfoVM)");
                }
                catch (Exception ex) { DLog.Force("信息栏补丁失败: " + ex.Message); }
            }

            private static void Postfix(MapInfoVM __instance) { ApplyOverride(__instance); }
        }

        private static void ApplyOverride(MapInfoVM __instance)
        {
            try
            {
                if (!NationalWillOrders.IsActive) return;
                var sel = MapSelection.SelectedList;
                int n = MapSelection.Count;

                if (n == 1 && sel != null && sel.Count > 0 && sel[0] != null)
                {
                    var p = sel[0];
                    var h = p.LeaderHero;
                    Set(__instance, "_goldInfo", "选中部队的金钱", h != null ? h.Gold.ToString() : "***");
                    Set(__instance, "_influenceInfo", "其家族影响力",
                        h != null && h.Clan != null ? ((int)h.Clan.Influence).ToString() : "***");
                    Set(__instance, "_hitPointsInfo", "领主生命值",
                        h != null ? ((int)h.HitPoints).ToString() : "***");
                    Set(__instance, "_troopsInfo", "部队人数",
                        p.MemberRoster != null ? DefArmy.RegularsOf(p).ToString() : "***");
                    Set(__instance, "_foodInfo", "部队食物", ((int)p.Food).ToString());
                    Set(__instance, "_moraleInfo", "部队士气", ((int)p.Morale).ToString());
                    Set(__instance, "_speedInfo", "部队速度", p.Speed.ToString("F1"));
                    Set(__instance, "_viewDistanceInfo", "视野(不适用)", "***");
                    Set(__instance, "_troopWageInfo", "军饷(不适用)", "***");
                    SetShipCount(__instance, p.Ships != null ? p.Ships.Count.ToString() : "0");
                    return;
                }

                long gold = 0;
                string goldTitle;
                if (n == 0)
                {
                    goldTitle = "全国(本国所有家族)金钱总和";
                    var kingdom = NationalWillOrders.Behavior != null
                        ? NationalWillOrders.Behavior.NationKingdom : null;
                    if (kingdom != null)
                    {
                        foreach (var clan in kingdom.Clans)
                        {
                            if (clan == null) continue;
                            try { if (clan.Leader != null) gold += clan.Leader.Gold; } catch { }
                        }
                    }
                }
                else
                {
                    goldTitle = "选中人员金钱总和";
                    foreach (var p in sel)
                    {
                        if (p == null) continue;
                        try { if (p.LeaderHero != null) gold += p.LeaderHero.Gold; } catch { }
                    }
                }

                Set(__instance, "_goldInfo", goldTitle, gold.ToString());
                Set(__instance, "_influenceInfo", "未选中具体部队", "***");
                Set(__instance, "_hitPointsInfo", "未选中具体部队", "***");
                Set(__instance, "_troopsInfo", "未选中具体部队", "***");
                Set(__instance, "_foodInfo", "未选中具体部队", "***");
                Set(__instance, "_moraleInfo", "未选中具体部队", "***");
                Set(__instance, "_speedInfo", "未选中具体部队", "***");
                Set(__instance, "_viewDistanceInfo", "未选中具体部队", "***");
                Set(__instance, "_troopWageInfo", "未选中具体部队", "***");

                // 舰船数量: 多选时统计所有选中部队的船; 没选则 ***
                if (n > 0)
                {
                    int ships = 0;
                    foreach (var p in sel)
                    {
                        if (p == null || p.Ships == null) continue;
                        try { ships += p.Ships.Count; } catch { }
                    }
                    SetShipCount(__instance, ships.ToString());
                }
                else
                {
                    SetShipCount(__instance, "***");
                }
            }
            catch { }
        }

        // 舰船数量: 海战DLC 的信息栏项(字段 _shipHealthInfo), 跟随选中部队
        private static void SetShipCount(MapInfoVM vm, string text)
        {
            try
            {
                var f = AccessTools.Field(vm.GetType(), "_shipHealthInfo");
                var item = f != null ? f.GetValue(vm) as MapInfoItemVM : null;
                if (item != null && item.Value != text) item.Value = text;
            }
            catch { }
        }

        private static readonly Dictionary<string, string> _lastValue = new Dictionary<string, string>();

        private static void Set(MapInfoVM vm, string field, string title, string value)
        {
            try
            {
                var f = AccessTools.Field(typeof(MapInfoVM), field);
                var item = f != null ? f.GetValue(vm) as MapInfoItemVM : null;
                if (item == null) return;

                if (item.Value != value) item.Value = value;

                // 悬停提示也换成同样的内容(只在变化时重建, 避免每帧分配)
                string key = field;
                string stamp = title + "|" + value;
                string last;
                if (_lastValue.TryGetValue(key, out last) && last == stamp) return;
                _lastValue[key] = stamp;

                var tip = new BasicTooltipViewModel(() => new List<TooltipProperty>
                {
                    new TooltipProperty(title, value, 0, false, TooltipProperty.TooltipPropertyFlags.None)
                });
                var tf = AccessTools.Field(typeof(MapInfoItemVM), "_tooltip");
                if (tf != null) tf.SetValue(item, tip);
            }
            catch { }
        }
    }
}
