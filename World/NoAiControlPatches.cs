using System;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Election;

namespace FeudalInternalAffairs
{
    // 国家意志的外交控制(用户需求: 所有战争/和平都走我们的接口):
    //   · 原版战争/和平决议一律拦截(Kingdom.AddDecision), 所有国家都不例外
    //   · 宣战/和谈动作只放行: 玩家操作 / 同盟参战 / 我们的 AI 外交系统(AiDiplomacy)
    //   · 原版必要的系统行为(叛乱/建国/王位索求/呼战协定/玩家惹事)放行
    //   · 军团: 仅玩家王国不能由 AI 自动组建, 外国照常
    internal static class NoAiControlPatches
    {
        // 玩家自己组建军团时置 true(否则会被下面的拦截挡掉)
        internal static bool PlayerFormingArmy;

        private static Kingdom PlayerKingdom()
        {
            try { return NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null; }
            catch { return null; }
        }

        // AI 组建军团: 只拦玩家王国的 AI(玩家自己组建时 PlayerFormingArmy=true 放行)
        [HarmonyPatch(typeof(Kingdom), "CreateArmy")]
        internal static class BlockAiArmy
        {
            private static bool Prefix(Kingdom __instance)
            {
                try
                {
                    if (PlayerFormingArmy) return true;
                    if (!NationalWillOrders.IsActive) return true;
                    var pk = PlayerKingdom();
                    if (pk == null) return true;
                    return !ReferenceEquals(__instance, pk);
                }
                catch { return true; }
            }
        }

        // 原版决议拦截(已停用! 保留代码备查):
        // 拦截 Kingdom.AddDecision 会让原版决策系统状态不一致(投票/取消时原生崩溃 0xC0000005),
        // 战争/和平改由动作层 BlockDeclareWar / BlockMakePeace 拦截, 效果相同且安全。
        internal static class BlockWarPeaceDecision
        {
            private static bool Prefix(Kingdom __instance, KingdomDecision kingdomDecision)
            {
                try
                {
                    if (!NationalWillOrders.IsActive) return true;
                    if (kingdomDecision is DeclareWarDecision || kingdomDecision is MakePeaceKingdomDecision)
                    {
                        DLog.Info("拦截原版战争/和平决议: " + kingdomDecision.GetType().Name);
                        return false;
                    }
                    var pk = PlayerKingdom();
                    if (pk != null && ReferenceEquals(__instance, pk)
                        && (kingdomDecision is TradeAgreementDecision || kingdomDecision is StartAllianceDecision))
                    {
                        DLog.Info("拦截玩家王国的原版条约决议: " + kingdomDecision.GetType().Name);
                        return false;
                    }
                }
                catch { }
                return true;
            }
        }

        // 宣战: 只放行玩家操作 / 同盟参战 / 我们的 AI 外交系统 / 原版必要行为
        [HarmonyPatch(typeof(DeclareWarAction), "ApplyInternal")]
        internal static class BlockDeclareWar
        {
            private static bool Prefix(DeclareWarAction.DeclareWarDetail declareWarDetail)
            {
                try
                {
                    if (!NationalWillOrders.IsActive) return true;
                    if (DiplomacyPatches.PlayerDeclaringWar || DiplomacyBehavior.AllyForcingWar || AiDiplomacy.AiActing) return true;
                    // 原版必要行为放行: 叛乱 / 建国 / 王位索求 / 呼战协定 / 玩家惹事(敌对行为、犯罪)
                    if (declareWarDetail == DeclareWarAction.DeclareWarDetail.CausedByRebellion
                        || declareWarDetail == DeclareWarAction.DeclareWarDetail.CausedByKingdomCreation
                        || declareWarDetail == DeclareWarAction.DeclareWarDetail.CausedByClaimOnThrone
                        || declareWarDetail == DeclareWarAction.DeclareWarDetail.CausedByCallToWarAgreement
                        || declareWarDetail == DeclareWarAction.DeclareWarDetail.CausedByPlayerHostility
                        || declareWarDetail == DeclareWarAction.DeclareWarDetail.CausedByCrimeRatingChange) return true;
                    return false;   // 其余(含原版决议、AI 自主宣战)一律拦
                }
                catch { return true; }
            }
        }

        // 和谈: 只放行我们的流程(玩家表决 / AI 外交系统)与灭国清理
        [HarmonyPatch(typeof(MakePeaceAction), "ApplyInternal")]
        internal static class BlockMakePeace
        {
            private static bool Prefix(IFaction faction1, IFaction faction2)
            {
                try
                {
                    if (!NationalWillOrders.IsActive) return true;
                    if (DiplomacyBehavior.ForcingPeace || AiDiplomacy.AiActing) return true;
                    var k1 = faction1 as Kingdom;
                    var k2 = faction2 as Kingdom;
                    if ((k1 != null && k1.IsEliminated) || (k2 != null && k2.IsEliminated)) return true;   // 灭国清理
                    return false;
                }
                catch { return true; }
            }
        }
    }
}
