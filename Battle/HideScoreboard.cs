using HarmonyLib;
using TaleWorlds.MountAndBlade.GauntletUI.Widgets.Scoreboard;

namespace FeudalInternalAffairs
{
    // 亲自指挥的战斗里, 删掉战况记分板(截图里那个"进攻方 98 / 艾仁的部队 1"面板)。
    //
    // 注意: 不能只在 OnUpdate 里把 IsVisible 设成 false —— 那样记分板的"打开状态"仍是 true,
    // 游戏会继续吞掉输入(Tab 等按键就会失灵)。正确做法是拦截 ShowScoreboard 的设置,
    // 让它从状态上就保持关闭。
    internal static class HideScoreboard
    {
        [HarmonyPatch(typeof(ScoreboardScreenWidget), "set_ShowScoreboard")]
        internal static class BlockScoreboardOpen
        {
            private static bool Prefix(ref bool value)
            {
                try
                {
                    if (BattleRtsCamera.Active) value = false;
                }
                catch { }
                return true;
            }
        }
    }
}
