using System;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.MountAndBlade.GauntletUI.Widgets.Scoreboard;

namespace FeudalInternalAffairs
{
    // 亲自指挥的战斗里, 删掉战况记分板(截图里那个"进攻方 98 / 艾仁的部队 1"面板)。
    //
    // 两层处理:
    //   1) 拦截 ShowScoreboard 的设置 -> 从状态上就不打开;
    //   2) 每帧兜底: 把状态字段 _showScoreboard 与 IsVisible 一起强制为 false。
    //      (只设 IsVisible 不够: 状态仍为"打开中"会让游戏继续吞输入)
    internal static class HideScoreboard
    {
        private static FieldInfo _showField;

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

        [HarmonyPatch(typeof(ScoreboardScreenWidget), "OnUpdate")]
        internal static class ForceHide
        {
            private static void Postfix(ScoreboardScreenWidget __instance)
            {
                try
                {
                    if (!BattleRtsCamera.Active) return;
                    if (_showField == null) _showField = AccessTools.Field(typeof(ScoreboardScreenWidget), "_showScoreboard");
                    if (_showField != null) _showField.SetValue(__instance, false);
                    __instance.IsVisible = false;
                }
                catch { }
            }
        }
    }
}
