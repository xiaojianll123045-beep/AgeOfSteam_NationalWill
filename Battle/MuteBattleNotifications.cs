using System;
using HarmonyLib;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace FeudalInternalAffairs
{
    // 亲自指挥的战斗里, 屏蔽各种"升级 / 技能提升"提示(其它战斗消息照常显示)。
    // 玩家在上帝视角指挥, 不需要这些刷屏提示。
    internal static class MuteBattleNotifications
    {
        // 左上角文字通知(InformationManager)
        [HarmonyPatch(typeof(InformationManager), "DisplayMessage")]
        internal static class MuteLevelUp
        {
            private static bool Prefix(InformationMessage message)
            {
                try
                {
                    if (!BattleRtsCamera.Active) return true;
                    var t = message != null ? message.Information : null;
                    if (string.IsNullOrEmpty(t)) return true;
                    if (t.Contains("提升") || t.Contains("升级") || t.Contains("熟练度")
                        || t.IndexOf("increased", StringComparison.OrdinalIgnoreCase) >= 0
                        || t.IndexOf("level up", StringComparison.OrdinalIgnoreCase) >= 0
                        || t.IndexOf("levelled up", StringComparison.OrdinalIgnoreCase) >= 0
                        || t.IndexOf("leveled up", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        DLog.Info("战斗中已屏蔽通知: " + t);
                        return false;
                    }
                }
                catch { }
                return true;
            }
        }

        // 屏幕中下方的"快速弹窗"(技能+1 那类, 截图1)
        [HarmonyPatch(typeof(MBInformationManager), "AddQuickInformation")]
        internal static class MuteQuickInformation
        {
            private static bool Prefix(TextObject message)
            {
                try
                {
                    if (!BattleRtsCamera.Active) return true;
                    try { DLog.Info("战斗中已屏蔽弹窗: " + (message != null ? message.ToString() : "?")); } catch { }
                    return false;
                }
                catch { }
                return true;
            }
        }

        // 羊皮纸横幅通知(提高了1级 那类, 截图2)
        [HarmonyPatch(typeof(MBInformationManager), "ShowSceneNotification")]
        internal static class MuteSceneNotification
        {
            private static bool Prefix()
            {
                try
                {
                    if (!BattleRtsCamera.Active) return true;
                    DLog.Info("战斗中已屏蔽横幅通知");
                    return false;
                }
                catch { }
                return true;
            }
        }
    }
}
