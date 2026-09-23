using System;
using HarmonyLib;
using SandBox.View;
using SandBox.View.Map;

namespace FeudalInternalAffairs
{
    // 国策页(现为全屏面板, 见 FocusPanel)打开时的地图输入拦截。
    // v4.146: 已不再是独立 Screen —— 不再跳过地图视觉 tick(那会让地图全冻), 地图照跑,
    //   点击被面板层与 PanelInputGuard 总闸拦住; 这里只拦键盘/相机输入, 防止 WASD/滚轮动到地图。
    internal static class FocusTreeMapGuardPatch
    {
        // 国策页打开时: 屏蔽游戏的地图按键监听(导航快捷键)
        [HarmonyPatch(typeof(MapScreen), "TickNavigationInput")]
        internal static class BlockMapNavigationInput
        {
            private static bool Prefix()
            {
                return !FocusTreeScreen.IsOpen;
            }
        }

        // 国策页/科技页打开时: 屏蔽地图相机输入(WASD/滚轮别动地图)
        // 这两页是全屏交互页, 自己就用 WASD/右键拖动/滚轮平移缩放页面(见 FocusPanel/TechPanel.PollInput),
        // 不屏蔽的话页面和地图会一起动。
        // v4.213: 不再对所有"鼠标压在侧边栏面板上"的情况屏蔽相机(TechPanel 以外) —— 那会让
        //   开页后 WASD 镜头失灵; 页内鼠标/滚轮不穿透改由 MapClickPatches.OnBeforeTick 清零保证。
        //   (v4.79k: 选国侧栏非模态, 本来就不能因它屏蔽相机)
        [HarmonyPatch(typeof(MapCameraView), "OnBeforeTick")]
        internal static class BlockMapCameraInput
        {
            private static bool Prefix()
            {
                if (FocusTreeScreen.IsOpen) return false;
                if (TechPanel.IsOpen) return false;
                return true;
            }
        }

        // 注意: 绝不能拦截静态 TaleWorlds.InputSystem.Input 的按键轮询!
        // 国策页的拖动/缩放/WASD 平移就是读静态 Input 轮询的, 拦了国策页就没法操作。
        // 游戏的地图热键都走下面的 InputContext, 不需要静态层。

        // ===== 输入上下文层: 游戏键 / 热键 / 原始按键 =====
        // 地图快捷键(K王国/N百科)、F5快存、空格暂停、WASD相机、Ctrl作弊键等全部走这里。
        // 注意: InputContext 每个方法都有两个重载(公开的 Int32/String/InputKey 版 + 内部的 GameKey/HotKey 版),
        // 只按名称打补丁会 Ambiguous match, 必须显式指定参数类型。
        // 只拦国策页(全屏界面)。侧边栏(抽屉/建造/外交/国家面板)是"非模态"的:
        // 不拦键盘 —— 否则 1/2/3 时间流逝、WASD 相机都会失效(用户反馈)
        private static bool Blocked()
        {
            return FocusTreeScreen.IsOpen;
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
