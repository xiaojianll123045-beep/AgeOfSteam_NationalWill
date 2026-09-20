using System;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Party;

namespace FeudalInternalAffairs
{
    // 国防军的原版适配补丁(第 23 章 23.14)
    internal static class DefArmyPatches
    {
        // 单军上限 10000: 原版队伍上限只有几百, 不覆写名册装不下、AI 会遣散超额
        [HarmonyPatch(typeof(PartyBase), "get_PartySizeLimit")]
        internal static class PartySizeLimitPatch
        {
            private static void Postfix(PartyBase __instance, ref int __result)
            {
                try
                {
                    var mp = __instance != null ? __instance.MobileParty : null;
                    if (mp != null && DefArmy.IsDefArmyParty(mp)) __result = DefArmy.MaxLegionMen;
                }
                catch { }
            }
        }

        // 速度锁死 3.0(不论人数/负重/俘虏/地形, 也不吃道路加成)
        [HarmonyPatch(typeof(DefaultPartySpeedCalculatingModel), "CalculateFinalSpeed")]
        internal static class SpeedLockPatch
        {
            private static void Postfix(MobileParty mobileParty, ref ExplainedNumber finalSpeed)
            {
                try
                {
                    if (mobileParty != null && DefArmy.IsDefArmyParty(mobileParty))
                        finalSpeed = new ExplainedNumber(DefArmy.SpeedLock);
                }
                catch { }
            }
        }
    }
}
