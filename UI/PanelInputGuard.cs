using System;
using HarmonyLib;
using TaleWorlds.InputSystem;
using TaleWorlds.MountAndBlade.GauntletUI;

namespace FeudalInternalAffairs
{
    // v4.86: 自建侧边栏(内政面板/选国侧栏)打开时拦截回车键。
    //   原版回车 = 打开聊天日志(消息日志), 在面板里输入数字后按回车会误触发(用户要求拦截)。
    //   双保险: ①拦聊天视图的输入处理 ②面板打开时让 Input.IsKeyPressed 读不到回车。
    //   注意: 自建面板自己的回车处理(ArmyVM 数量键入选定)用 Input.IsKeyDown 边沿自检, 不受影响。
    internal static class PanelInputGuard
    {
        internal static bool Blocked
        {
            get
            {
                try { return PanelScreen.AnyOpen || NationPickPanel.IsOpen; }
                catch { return false; }
            }
        }

        private static bool EnterKey(InputKey key)
        {
            return key == InputKey.Enter || key == InputKey.NumpadEnter;
        }

        // ① 聊天日志视图的输入处理: 面板打开时把回车这一帧吞掉
        [HarmonyPatch(typeof(GauntletChatLogView), "HandleInput")]
        internal static class ChatLogHandleInputPatch
        {
            private static bool Prefix(ref bool chatOpened, ref bool chatClosed)
            {
                try
                {
                    if (!Blocked) return true;
                    if (Input.IsKeyPressed(InputKey.Enter) || Input.IsKeyPressed(InputKey.NumpadEnter))
                    {
                        chatOpened = false;
                        chatClosed = false;
                        return false;
                    }
                }
                catch { }
                return true;
            }
        }

        // ② 面板打开时, 原版读不到回车"按下/释放"(全局兜底)
        [HarmonyPatch(typeof(Input), "IsKeyPressed")]
        internal static class InputEnterPressedPatch
        {
            private static bool Prefix(InputKey key, ref bool __result)
            {
                if (EnterKey(key) && Blocked)
                {
                    __result = false;
                    return false;
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(Input), "IsKeyReleased")]
        internal static class InputEnterReleasedPatch
        {
            private static bool Prefix(InputKey key, ref bool __result)
            {
                if (EnterKey(key) && Blocked)
                {
                    __result = false;
                    return false;
                }
                return true;
            }
        }
    }
}
