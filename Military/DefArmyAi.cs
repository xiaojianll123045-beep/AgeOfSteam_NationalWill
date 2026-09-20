using System;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Party;

namespace FeudalInternalAffairs
{
    // v4.96: 国防军 AI 抑制(用户: 没领主部队听话, 走着走着去打劫匪)
    //   v4.95 起有"手动驾驶"兜底(CommandTimeout 0.5 秒未动即直接推进位置),
    //   因此可以安全屏蔽原版 AI 决策: 军团不再自主选目标(不会跑去打劫匪/追商队), 移动由命令+兜底驱动。
    //   注意: 只屏蔽决策(TickInternal), 战斗/地图事件等原版机制不受影响。
    internal static class DefArmyAi
    {
        private static readonly FieldInfo PartyField = AccessTools.Field(typeof(MobilePartyAi), "_mobileParty");

        // v4.97: AI 抑制 —— 只屏蔽"决策"(TickInternal), 不能屏蔽 Tick 整体
        //   (实测屏蔽 Tick 后连移动都不执行了: Tick 里有驱动移动所需的环节)
        [HarmonyPatch(typeof(MobilePartyAi), "TickInternal")]
        internal static class AiGuard
        {
            private static bool Prefix(MobilePartyAi __instance)
            {
                try
                {
                    var p = PartyField != null ? PartyField.GetValue(__instance) as MobileParty : null;
                    if (p != null && DefArmy.IsDefArmyParty(p)) return false;   // 国防军不参与原版 AI 决策
                }
                catch { }
                return true;
            }
        }

        // v4.97: 国防军不参与原版自动招募(用户: 他竟然会征兵)
        //   原版 RecruitmentCampaignBehavior 每日对 AI 领主部队在定居点自动招募志愿兵;
        //   我们的军团名义上是"玩家家族的领主部队", 会被原版当 AI 部队招募 -> 屏蔽。
        [HarmonyPatch(typeof(RecruitmentCampaignBehavior), "CheckRecruiting")]
        internal static class NoAutoRecruit
        {
            private static bool Prefix(MobileParty mobileParty)
            {
                try
                {
                    if (mobileParty != null && DefArmy.IsDefArmyParty(mobileParty)) return false;
                }
                catch { }
                return true;
            }
        }
    }
}
