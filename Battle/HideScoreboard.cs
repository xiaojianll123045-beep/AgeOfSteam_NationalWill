using System;
using HarmonyLib;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.MountAndBlade.ViewModelCollection.Scoreboard;

namespace FeudalInternalAffairs
{
    // 亲自指挥的战斗里, 战况记分板(Tab 面板)中不显示"玩家自己的部队"那一行。
    // 玩家是"国家意志", 没有个人部队, 面板里只应出现各国领主的部队。
    // 注意: 面板本身保持可用(不再整体屏蔽)。
    internal static class HidePlayerPartyRow
    {
        [HarmonyPatch(typeof(SPScoreboardSideVM), "RefreshValues")]
        internal static class RemovePlayerPartyRow
        {
            private static void Postfix(SPScoreboardSideVM __instance)
            {
                try
                {
                    if (!BattleRtsCamera.Active) return;
                    if (__instance == null || __instance.Parties == null) return;
                    var main = MobileParty.MainParty != null ? MobileParty.MainParty.Party : null;
                    if (main == null) return;
                    string mainName = null;
                    try { mainName = main.Name != null ? main.Name.ToString() : null; } catch { }

                    for (int i = __instance.Parties.Count - 1; i >= 0; i--)
                    {
                        var row = __instance.Parties[i];
                        if (row == null) continue;
                        bool isPlayerParty = false;
                        try { isPlayerParty = ReferenceEquals(row.BattleCombatant, main); } catch { }
                        if (!isPlayerParty && mainName != null)
                        {
                            try
                            {
                                var n = row.BattleCombatant != null && row.BattleCombatant.Name != null
                                    ? row.BattleCombatant.Name.ToString() : null;
                                if (n == mainName) isPlayerParty = true;
                            }
                            catch { }
                        }
                        if (isPlayerParty)
                        {
                            __instance.Parties.RemoveAt(i);
                            DLog.Force("记分板: 已移除玩家自己的部队行");
                        }
                    }
                }
                catch { }
            }
        }
    }
}
