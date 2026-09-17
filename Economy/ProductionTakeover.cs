using System;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Localization;

namespace FeudalInternalAffairs
{
    // 接管原版村庄产出(13.2) 与城镇食物量级(13.5)
    // 挂点选择说明: 不用 VillageProductionCalculatorModel 的前缀(它同时被"村民队伍规模/村庄仓库容量"使用,
    // 直接返回 0 会连带把村民队伍变成 0 人), 改为直接跳过每日产出行为, 效果一致且无副作用
    internal static class ProductionTakeover
    {
        private static TextObject _villageFoodLabel;

        // 1) 关闭原版村庄商品产出(全世界) —— 改由本系统每日结算
        [HarmonyPatch(typeof(VillageGoodProductionCampaignBehavior), "TickGoodProduction")]
        internal static class SkipVillageGoods
        {
            private static bool _logged;

            private static bool Prefix()
            {
                if (!_logged)
                {
                    _logged = true;
                    DLog.Force("村庄产出接管: 原版村庄产出已关闭, 改由本系统结算");
                }
                return false;
            }
        }

        // 2) 关闭原版村庄食物产出(往市场塞食物物品)
        [HarmonyPatch(typeof(VillageGoodProductionCampaignBehavior), "TickFoodProduction")]
        internal static class SkipVillageFood
        {
            private static bool Prefix() { return false; }
        }

        // 3) 城镇食物消耗量级: 繁荣/40 -> 繁荣/1000 (13.5 A)
        [HarmonyPatch(typeof(DefaultSettlementFoodModel), "get_NumberOfProsperityToEatOneFood")]
        internal static class FoodRatio
        {
            private static void Postfix(ref int __result) { __result = 1000; }
        }

        // 4) 城镇食物来源: 抵消原版"按户数给的食物", 换成本系统村庄建筑的食物产出
        [HarmonyPatch(typeof(DefaultSettlementFoodModel), "CalculateTownFoodStocksChange")]
        internal static class TownFoodSource
        {
            private static void Postfix(Town town, ref ExplainedNumber __result)
            {
                try
                {
                    if (town == null || town.Settlement == null) return;
                    float vanilla = VanillaVillageFood(town.Settlement);
                    float ours = DailySettlement.OurVillageFood(town.Settlement);
                    float delta = ours - vanilla;
                    if (Math.Abs(delta) < 0.001f) return;
                    if (_villageFoodLabel == null)
                        _villageFoodLabel = new TextObject("{=fia_food_village}Villages (internal affairs)");
                    __result.Add(delta, _villageFoodLabel, null);
                }
                catch { }
            }
        }

        // 原版公式: 每个"正常"村庄贡献 (户数等级+1) * 6 食物/日
        internal static float VanillaVillageFood(Settlement s)
        {
            float sum = 0f;
            if (s == null) return 0f;
            try
            {
                foreach (var v in s.BoundVillages)
                {
                    if (v == null) continue;
                    if (v.VillageState != Village.VillageStates.Normal) continue;
                    sum += (v.GetHearthLevel() + 1) * 6f;
                }
            }
            catch { }
            return sum;
        }
    }
}
