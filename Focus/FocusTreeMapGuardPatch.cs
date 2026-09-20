using System;
using HarmonyLib;
using SandBox.View;
using SandBox.View.Map;

namespace FeudalInternalAffairs
{
    // 国策树打开时跳过地图视觉系统的 tick。
    // 原因: 我们的屏幕是 PushScreen 上去的, MapState 仍在跑; 而原版屏幕是 GameState,
    // 会把 MapState 停掉。不跳过的话 NavalDLC 的船可视化(NavalMobilePartyVisual.TickOurs/
    // UpdateEntityPosition) 会在这种状态下访问非法内存 -> AccessViolation 崩溃。
    internal static class FocusTreeMapGuardPatch
    {
        [HarmonyPatch(typeof(SandBoxViewVisualManager), "OnTick")]
        internal static class SkipMapVisualTick
        {
            private static bool Prefix()
            {
                try
                {
                    if (FocusTreeScreen.IsOpen)
                    {
                        FocusTreeScreen.PollInput();   // 顺便每帧处理滚轮/WASD/拖动
                        return false;
                    }
                    // 侧边栏面板(PanelScreen)不再整体跳过地图视觉 tick —— 那会让地图全冻(用户反馈)。
                    // 改为由 NavalVisualGuard 精准跳过海战DLC的船可视化。
                }
                catch { }
                return true;
            }
        }

        // 国策树打开时: 屏蔽游戏的地图按键监听(导航快捷键)
        [HarmonyPatch(typeof(MapScreen), "TickNavigationInput")]
        internal static class BlockMapNavigationInput
        {
            private static bool Prefix()
            {
                return !FocusTreeScreen.IsOpen;
            }
        }

        // 国策树打开时: 屏蔽地图相机输入(WASD/滚轮别动地图)
        // 另外: 鼠标压在侧边栏面板上时也屏蔽(否则滚轮会穿透去缩放地图, 而面板自己的列表滚不动)
        // v4.79k 修复: 选国侧栏是非模态(滚轮自轮询/交互全走热区), 不能因它屏蔽相机 —— 否则
        //   选国面板打开且鼠标在右侧时 WASD 全部失效(用户反馈"开局一段时间不能移动"的根因)
        [HarmonyPatch(typeof(MapCameraView), "OnBeforeTick")]
        internal static class BlockMapCameraInput
        {
            private static bool Prefix()
            {
                if (FocusTreeScreen.IsOpen) return false;
                try
                {
                    if (PanelScreen.IsMouseOnPanel() && !NationPickPanel.IsOpen) return false;
                }
                catch { }
                return true;
            }
        }

        // 注意: 绝不能拦截静态 TaleWorlds.InputSystem.Input 的按键轮询!
        // 搜索框(EditableTextWidget.HandleInput)就是用 Input.IsKeyDown 处理输入的,
        // 拦了会导致国策树里完全无法打字。游戏的地图热键都走下面的 InputContext, 不需要静态层。

        // ===== 输入上下文层: 游戏键 / 热键 / 原始按键 =====
        // 地图快捷键(K王国/N百科)、F5快存、空格暂停、WASD相机、Ctrl作弊键等全部走这里。
        // 注意: InputContext 每个方法都有两个重载(公开的 Int32/String/InputKey 版 + 内部的 GameKey/HotKey 版),
        // 只按名称打补丁会 Ambiguous match, 必须显式指定参数类型。
        // 只拦国策树(全屏界面)。侧边栏(抽屉/建造/外交/国家面板)是"非模态"的:
        // 不拦键盘 —— 否则 1/2/3 时间流逝、WASD 相机都会失效(用户反馈)
        private static bool Blocked()
        {
            return FocusTreeScreen.IsOpen && !FocusTreeScreen.PollingInput;
        }

        [HarmonyPatch(typeof(TaleWorlds.InputSystem.InputContext), "IsGameKeyPressed", new[] { typeof(int) })]
        internal static class BlockGameKeyPressed
        {
            private static bool Prefix(ref bool __result)
            {
                if (Blocked()) { __result = false; return false; }
                return true;
            }
        }

        [HarmonyPatch(typeof(TaleWorlds.InputSystem.InputContext), "IsGameKeyDown", new[] { typeof(int) })]
        internal static class BlockGameKeyDown
        {
            private static bool Prefix(ref bool __result)
            {
                if (Blocked()) { __result = false; return false; }
                return true;
            }
        }

        [HarmonyPatch(typeof(TaleWorlds.InputSystem.InputContext), "IsGameKeyReleased", new[] { typeof(int) })]
        internal static class BlockGameKeyReleased
        {
            private static bool Prefix(ref bool __result)
            {
                if (Blocked()) { __result = false; return false; }
                return true;
            }
        }

        [HarmonyPatch(typeof(TaleWorlds.InputSystem.InputContext), "IsGameKeyDownImmediate", new[] { typeof(int) })]
        internal static class BlockGameKeyDownImmediate
        {
            private static bool Prefix(ref bool __result)
            {
                if (Blocked()) { __result = false; return false; }
                return true;
            }
        }

        [HarmonyPatch(typeof(TaleWorlds.InputSystem.InputContext), "IsHotKeyPressed", new[] { typeof(string) })]
        internal static class BlockHotKeyPressed
        {
            private static bool Prefix(ref bool __result)
            {
                if (Blocked()) { __result = false; return false; }
                return true;
            }
        }

        [HarmonyPatch(typeof(TaleWorlds.InputSystem.InputContext), "IsHotKeyDown", new[] { typeof(string) })]
        internal static class BlockHotKeyDown
        {
            private static bool Prefix(ref bool __result)
            {
                if (Blocked()) { __result = false; return false; }
                return true;
            }
        }

        [HarmonyPatch(typeof(TaleWorlds.InputSystem.InputContext), "IsHotKeyReleased", new[] { typeof(string) })]
        internal static class BlockHotKeyReleased
        {
            private static bool Prefix(ref bool __result)
            {
                if (Blocked()) { __result = false; return false; }
                return true;
            }
        }

        [HarmonyPatch(typeof(TaleWorlds.InputSystem.InputContext), "IsHotKeyDoublePressed", new[] { typeof(string) })]
        internal static class BlockHotKeyDoublePressed
        {
            private static bool Prefix(ref bool __result)
            {
                if (Blocked()) { __result = false; return false; }
                return true;
            }
        }

        [HarmonyPatch(typeof(TaleWorlds.InputSystem.InputContext), "GetGameKeyState", new[] { typeof(int) })]
        internal static class BlockGameKeyState
        {
            private static bool Prefix(ref float __result)
            {
                if (Blocked()) { __result = 0f; return false; }
                return true;
            }
        }

        [HarmonyPatch(typeof(TaleWorlds.InputSystem.InputContext), "GetGameKeyAxis", new[] { typeof(string) })]
        internal static class BlockGameKeyAxis
        {
            private static bool Prefix(ref float __result)
            {
                if (Blocked()) { __result = 0f; return false; }
                return true;
            }
        }
    }
}
