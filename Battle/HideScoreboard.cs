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
                        try { if (row.Score != null && row.Score.IsMainParty) isPlayerParty = true; } catch { }
                        if (!isPlayerParty)
                        {
                            try { isPlayerParty = ReferenceEquals(row.BattleCombatant, main); } catch { }
                        }
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
                            // 把这一行的数字也从"队伍总数"里扣掉(否则总人数会比可见部队多出玩家那 1 人)
                            try
                            {
                                var s = __instance.Score;
                                var r = row.Score;
                                if (s != null && r != null)
                                {
                                    s.Remaining -= r.Remaining;
                                    s.Dead -= r.Dead;
                                    s.Wounded -= r.Wounded;
                                    s.Routed -= r.Routed;
                                    s.Kill -= r.Kill;
                                    s.ReadyToUpgrade -= r.ReadyToUpgrade;
                                    if (s.Remaining < 0) s.Remaining = 0;
                                    if (s.Dead < 0) s.Dead = 0;
                                    if (s.Wounded < 0) s.Wounded = 0;
                                    if (s.Routed < 0) s.Routed = 0;
                                    if (s.Kill < 0) s.Kill = 0;
                                    if (s.ReadyToUpgrade < 0) s.ReadyToUpgrade = 0;
                                }
                            }
                            catch { }
                            __instance.Parties.RemoveAt(i);
                            DLog.Force("记分板: 已移除玩家自己的部队行并修正总人数");
                        }
                    }
                }
                catch { }
            }
        }
    }
}
