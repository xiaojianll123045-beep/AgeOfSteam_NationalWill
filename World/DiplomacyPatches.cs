using System;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.CampaignSystem.ViewModelCollection.KingdomManagement.Diplomacy;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace FeudalInternalAffairs
{
    // 国家意志的外交规则:
    //   宣战: 玩家想发就发(无代价, 不走本国领主投票)
    //   和谈: 玩家发起 -> 变成"敌国的领主投票"(对方表决通过才和)
    internal static class DiplomacyPatches
    {
        // 玩家自己发起宣战时置 true(绕过 AI 禁令)
        internal static bool PlayerDeclaringWar;

        // 宣战按钮: 永远可用(去掉代价/投票限制)
        [HarmonyPatch(typeof(KingdomDiplomacyVM), "GetIsProposingWarEnabledWithReason")]
        internal static class AlwaysCanDeclareWar
        {
            private static bool Prefix(ref bool __result, ref float actionInfluenceCost, ref TextObject disabledReason)
            {
                try
                {
                    if (!NationalWillOrders.IsActive) return true;
                    actionInfluenceCost = 0f;
                    disabledReason = null;
                    __result = true;
                    return false;
                }
                catch { return true; }
            }
        }

        // 点"宣战": 第 22 章起改为发出宣战诏书(开启外交博弈); 无法开博弈时兜底直接开战
        [HarmonyPatch(typeof(KingdomDiplomacyVM), "OnDeclareWar")]
        internal static class DeclareWarDirectly
        {
            private static bool Prefix(KingdomTruceItemVM item)
            {
                try
                {
                    var behavior = NationalWillOrders.Behavior;
                    var our = behavior != null ? behavior.NationKingdom : null;
                    if (our == null) return true;
                    var target = GetOtherFaction(item, our);
                    if (target == null) return true;

                    var play = DiploPlays.StartPlay(our, target as Kingdom, true);
                    if (play != null)
                    {
                        MapSelection.Message("已向 " + (target.Name != null ? target.Name.ToString() : "?") + " 发出宣战诏书(进入外交博弈)");
                        DLog.Force("玩家发起外交博弈: " + our.StringId + " -> " + target.StringId);
                        return false;
                    }

                    // 兜底: 直接开战(已有博弈/无法开博弈时)
                    PlayerDeclaringWar = true;
                    try { DeclareWarAction.ApplyByKingdomDecision(our, target); }
                    finally { PlayerDeclaringWar = false; }

                    MapSelection.Message("已向 " + (target.Name != null ? target.Name.ToString() : "?") + " 宣战");
                    DLog.Force("玩家宣战: " + our.StringId + " -> " + target.StringId);
                    return false;
                }
                catch (Exception ex) { DLog.Force("宣战失败: " + ex.Message); return true; }
            }
        }

        // 点"和谈": 在敌国发起领主投票(对方同意才和)
        [HarmonyPatch(typeof(KingdomDiplomacyVM), "OnDeclarePeace")]
        internal static class PeaceByEnemyVote
        {
            private static bool Prefix(KingdomWarItemVM item, int tributeToPay, int tributeDurationInDays)
            {
                try
                {
                    var behavior = NationalWillOrders.Behavior;
                    var our = behavior != null ? behavior.NationKingdom : null;
                    if (our == null) return true;
                    var enemyKingdom = GetOtherFaction(item, our) as Kingdom;
                    if (enemyKingdom == null) return true;
                    ProposePeace(our, enemyKingdom, tributeToPay, tributeDurationInDays);
                    return false;
                }
                catch (Exception ex) { DLog.Force("和谈投票失败: " + ex.Message); return true; }
            }
        }

        // 玩家发起和谈: 统一走我们自己的双方领主表决(不再使用原版王国决议)
        // 外交面板(DiplomacyBehavior.PlayerMakePeace)与王国页共用这条路径
        internal static bool ProposePeace(Kingdom our, Kingdom enemy, int tributeToPay, int tributeDurationInDays)
        {
            try
            {
                if (our == null || enemy == null) return false;
                return DiplomacyBehavior.PlayerMakePeace(enemy);
            }
            catch (Exception ex) { DLog.Force("和谈失败: " + ex.Message); return false; }
        }

        // 从外交项里取"另一方阵营"
        private static IFaction GetOtherFaction(KingdomDiplomacyItemVM item, Kingdom our)
        {
            try
            {
                var f1 = AccessTools.Field(typeof(KingdomDiplomacyItemVM), "Faction1") != null
                    ? AccessTools.Field(typeof(KingdomDiplomacyItemVM), "Faction1").GetValue(item) as IFaction : null;
                var f2 = AccessTools.Field(typeof(KingdomDiplomacyItemVM), "Faction2") != null
                    ? AccessTools.Field(typeof(KingdomDiplomacyItemVM), "Faction2").GetValue(item) as IFaction : null;
                if (f1 != null && ReferenceEquals(f1, our)) return f2;
                if (f2 != null && ReferenceEquals(f2, our)) return f1;
                return f2;
            }
            catch { return null; }
        }
    }
}
