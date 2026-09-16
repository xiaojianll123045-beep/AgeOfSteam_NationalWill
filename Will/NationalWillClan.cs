using System;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace FeudalInternalAffairs
{
    // 玩家家族(僵尸家族): 不进任何王国的名册, 但对外"属于"所选国家
    internal static class NationalWillClan
    {
        // 家族名/族徽兜底(玩家不进家族命名与选旗界面)
        internal static void EnsureIdentity(Kingdom kingdom, Clan clan)
        {
            try
            {
                string cur = clan.Name != null ? clan.Name.ToString() : null;
                if (string.IsNullOrEmpty(cur))
                {
                    string nm = PickName(kingdom.Culture != null ? kingdom.Culture.ClanNameList : null);
                    if (!string.IsNullOrEmpty(nm))
                    {
                        clan.ChangeClanName(new TextObject(nm), new TextObject(nm));
                        DLog.Force("已自动设置家族名: " + nm);
                    }
                }
                if (clan.Banner == null)
                {
                    clan.Banner = Banner.CreateRandomClanBanner(MBRandom.RandomInt(100000));
                    DLog.Force("已自动设置家族徽章");
                }
            }
            catch (Exception ex) { DLog.Force("家族名/徽章兜底失败: " + ex.Message); }
        }

        private static string PickName(MBReadOnlyList<TextObject> list)
        {
            if (list == null || list.Count == 0) return null;
            return list[MBRandom.RandomInt(list.Count)].ToString();
        }

        // 让所有"玩家属于哪个国家"的查询返回所选国家(王国界面/决策系统照常工作)
        [HarmonyPatch(typeof(Clan), "get_Kingdom")]
        internal static class ZombieClanKingdomPatch
        {
            private static void Postfix(Clan __instance, ref Kingdom __result)
            {
                try
                {
                    if (__result != null || __instance == null) return;
                    if (!ReferenceEquals(__instance, Clan.PlayerClan)) return;
                    var behavior = Campaign.Current != null ? Campaign.Current.GetCampaignBehavior<NationalWillBehavior>() : null;
                    if (behavior == null || !behavior.IsNationalWill) return;
                    var kingdom = behavior.NationKingdom;
                    if (kingdom != null) __result = kingdom;
                }
                catch { }
            }
        }
    }
}
