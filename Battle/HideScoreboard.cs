using HarmonyLib;
using TaleWorlds.MountAndBlade.GauntletUI.Widgets.Scoreboard;

namespace FeudalInternalAffairs
{
    // 亲自指挥的战斗里, 删掉战况记分板(截图里那个"进攻方 98 / 艾仁的部队 1"面板)。
    // 做法: 记分板部件每帧更新后强制隐藏。
    internal static class HideScoreboard
    {
        [HarmonyPatch(typeof(ScoreboardScreenWidget), "OnUpdate")]
        internal static class HideScoreboardPatch
        {
            private static void Postfix(ScoreboardScreenWidget __instance)
            {
                try
                {
                    if (BattleRtsCamera.Active) __instance.IsVisible = false;
                }
                catch { }
            }
        }
    }
}
