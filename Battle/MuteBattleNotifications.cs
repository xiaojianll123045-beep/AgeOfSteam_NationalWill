using System;
using HarmonyLib;
using TaleWorlds.Library;

namespace FeudalInternalAffairs
{
    // 亲自指挥的战斗里, 屏蔽"主角升级 / 技能提升"类通知(其它战斗消息照常显示)。
    // 玩家在上帝视角指挥, 不需要这些刷屏提示。
    internal static class MuteBattleNotifications
    {
        [HarmonyPatch(typeof(InformationManager), "DisplayMessage")]
        internal static class MuteLevelUp
        {
            private static bool Prefix(InformationMessage message)
            {
                try
                {
                    if (!BattleRtsCamera.Active) return true;   // 只在"亲自指挥"的战斗里生效
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
    }
}
