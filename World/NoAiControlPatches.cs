using System;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;

namespace FeudalInternalAffairs
{
    // 国家意志: 军团组建 / 宣战 / 和谈 只能由玩家决定, AI 一律禁止
    internal static class NoAiControlPatches
    {
        // 玩家自己组建军团时置 true(否则会被下面的拦截挡掉)
        internal static bool PlayerFormingArmy;

        // AI 组建军团: 直接跳过(所有 ApplyBy* 都走 ApplyInternal)
        [HarmonyPatch(typeof(Kingdom), "CreateArmy")]
        internal static class BlockAiArmy
        {
            private static bool Prefix()
            {
                try { return PlayerFormingArmy; }
                catch { return true; }
            }
        }

        // AI 宣战: 跳过(所有 ApplyBy* 都走 ApplyInternal); 玩家自己发起的放行
        [HarmonyPatch(typeof(DeclareWarAction), "ApplyInternal")]
        internal static class BlockDeclareWar
        {
            private static bool Prefix()
            {
                try { return !NationalWillOrders.IsActive || DiplomacyPatches.PlayerDeclaringWar; }
                catch { return true; }
            }
        }

        // AI 和谈: 跳过; 但"领主投票通过的和谈"要放行(玩家发起的和谈就是走这条路)
        [HarmonyPatch(typeof(MakePeaceAction), "ApplyInternal")]
        internal static class BlockMakePeace
        {
            private static bool Prefix(MakePeaceAction.MakePeaceDetail detail)
            {
                try
                {
                    return !NationalWillOrders.IsActive
                        || detail == MakePeaceAction.MakePeaceDetail.ByKingdomDecision;
                }
                catch { return true; }
            }
        }
    }
}
