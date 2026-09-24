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

        // v4.252: 国防军不吃原版军饷 —— 军饷由本 mod 收(0.5/兵/日, 走国库, 有欠饷/哗变机制)。
        //   原来国防军会被原版按"家族金库(= 玩家金币)"再扣一遍工资, 玩家金币被扣空后原版欠薪逃兵,
        //   于是 v4.89 在 PayWages 里加了"把玩家金币补到 30 日军饷"的兜底 —— 那是每天凭空印钱
        //   (经济审计实测: 有军团时国库永久 ≥30 日饷, 破产永不发生)。现在从源头掐断:
        //   原版算国防军工资时返回 0, 兜底随之删除。
        [HarmonyPatch(typeof(DefaultPartyWageModel), "GetTotalWage")]
        internal static class DefArmyNoNativeWagePatch
        {
            private static void Postfix(MobileParty mobileParty, ref ExplainedNumber __result)
            {
                try
                {
                    if (mobileParty != null && DefArmy.IsDefArmyParty(mobileParty))
                        __result = new ExplainedNumber(0f);
                }
                catch { }
            }
        }

        // 速度锁死 3.0(不论人数/负重/俘虏/地形, 也不吃道路加成)
        // v4.248: 全工程有**三个** CalculateFinalSpeed 补丁(本补丁 / ArmyDoctrine 铁路运输 / KingdomTraitPatches
        //   王国特性速度), Postfix 之间没有固定执行顺序 —— 任何在速度锁之后跑的 AddFactor 都会破坏"恒 3.0"
        //   (用户反复反馈"人多的国防军部队速度快, 人少的又慢")。现在再补一个 Finalizer: Finalizer 一定在所有
        //   Postfix 之后执行, 是最终仲裁者, 因此国防军速度无论如何都是 3.0。
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

            private static void Finalizer(MobileParty mobileParty, ref ExplainedNumber __result)
            {
                try
                {
                    if (mobileParty != null && DefArmy.IsDefArmyParty(mobileParty))
                        __result = new ExplainedNumber(DefArmy.SpeedLock);
                }
                catch { }
            }
        }
    }
}
