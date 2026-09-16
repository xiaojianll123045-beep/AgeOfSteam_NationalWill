using System;
using System.Reflection;
using HarmonyLib;
using SandBox.View.Map;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Engine;
using TaleWorlds.Library;

namespace FeudalInternalAffairs
{
    // 视角总控
    //
    // 已确认的行为: 开局、点击地面/定居点之后, 游戏会把相机目标重新指到主角部队,
    // 相机于是"缓缓滑回玩家中心"(它的缓动: _cameraTarget 向 IdealCameraTarget 靠)。
    // 修法: 每帧检查一次目标, 一旦发现被指到主角附近, 就把目标和"当前位置"一起还原到
    // 玩家自己放的位置 —— 不拦原版调用(不影响输入初始化), 也不产生滑动。
    internal static class MapCameraPatches
    {
        // 地图场景/相机初始化完成之后再瞬移一次视角(只做一次)
        [HarmonyPatch(typeof(MapScreen), "HandleIfSceneIsReady")]
        internal static class AfterSceneReady
        {
            private static void Postfix()
            {
                try
                {
                    var behavior = NationalWillOrders.Behavior;
                    if (behavior == null || !NationalWillOrders.ShouldControlCamera) return;
                    NationalWillCamera.CenterOnKingdomOnce(behavior.NationKingdom);
                }
                catch { }
            }
        }

        // 真根因: 每次左键点击(HandleLeftMouseButtonClick)和地图激活(OnActivate)都会把相机
        // 切成 FollowParty 模式, 跟随目标设成主角 -> 相机于是缓缓滑回玩家。
        // 这里在国家意志模式下彻底禁止进入 FollowParty 模式(玩家部队根本不动, 也不需要跟随)。
        [HarmonyPatch(typeof(MapCameraView), "set_CurrentCameraFollowMode")]
        internal static class NeverFollowPartyMode
        {
            private static bool Prefix(MapCameraView.CameraFollowMode value)
            {
                try
                {
                    if (value == MapCameraView.CameraFollowMode.FollowParty && NationalWillOrders.ShouldControlCamera) return false;
                }
                catch { }
                return true;
            }
        }

        [HarmonyPatch(typeof(MapCameraView), "SetCameraMode")]
        internal static class NeverFollowPartyViaSetCameraMode
        {
            private static bool Prefix(MapCameraView.CameraFollowMode cameraMode)
            {
                try
                {
                    if (cameraMode == MapCameraView.CameraFollowMode.FollowParty && NationalWillOrders.ShouldControlCamera) return false;
                }
                catch { }
                return true;
            }
        }

        // 原版右键拖动=旋转地图, 我们改成平移地图(由 MapBoxSelect 自己实现) -> 关掉原版旋转
        // 注意: 右键不再旋转, 旋转改由中键接管(中键按住时才允许原版旋转)
        [HarmonyPatch(typeof(MapCameraView), "HandleMouse")]
        internal static class NoManualRotation
        {
            private static void Prefix(ref bool rightMouseButtonPressed)
            {
                try
                {
                    if (!NationalWillOrders.IsActive) return;
                    rightMouseButtonPressed = TaleWorlds.InputSystem.Input.IsKeyDown(
                        TaleWorlds.InputSystem.InputKey.MiddleMouseButton);
                }
                catch { }
            }
        }

        // 原版左键拖动=平移地图, 我们改成框选 -> 一律关掉原版拖动平移
        [HarmonyPatch(typeof(MapCameraView), "UpdateMapCamera")]
        internal static class NoLeftDragPan
        {
            private static bool _logged;

            private static void Prefix(ref bool _leftButtonDraggingMode)
            {
                try
                {
                    if (!NationalWillOrders.IsActive) return;
                    if (!_leftButtonDraggingMode) return;
                    _leftButtonDraggingMode = false;
                    if (!_logged)
                    {
                        _logged = true;
                        DLog.Force("已拦截原版左键拖动平移");
                    }
                }
                catch { }
            }
        }

        // 左键点击时相机也会动(移动目标/跟随) -> 关掉
        [HarmonyPatch(typeof(MapCameraView), "HandleLeftMouseButtonClick")]
        internal static class NoClickCameraMove
        {
            private static bool Prefix() { return !NationalWillOrders.ShouldControlCamera; }
        }

        // 按键重定向:
        //   中键拖动 -> 原版右键(旋转视角)
        [HarmonyPatch(typeof(MapCameraView), "GetMapCameraInput")]
        internal static class RedirectButtons
        {
            private static void Prefix(ref MapCameraView.InputInformation inputInformation)
            {
                try
                {
                    if (!NationalWillOrders.IsActive) return;

                    // 中键: 一律禁掉原版中键行为(缩放); 拖动时重定向成原版右键(旋转)
                    if (inputInformation.MiddleMouseButtonDown)
                    {
                        inputInformation.MiddleMouseButtonDown = false;
                        if (MapBoxSelect.MiddleDragging)
                        {
                            inputInformation.RightMouseButtonDown = true;
                        }
                    }

                    // 真右键按住时: 清零移动量, 禁掉原版右键旋转
                    if (TaleWorlds.InputSystem.Input.IsKeyDown(TaleWorlds.InputSystem.InputKey.RightMouseButton))
                    {
                        inputInformation.MouseMoveX = 0f;
                        inputInformation.MouseMoveY = 0f;
                    }
                }
                catch { }
            }
        }

        // 点击部队/定居点名板时, GauntletMapPartyNameplateView 会调这个委托 -> 镜头平滑飞回主角
        // ("缓缓回到玩家中心"的直接来源)。这些方法只负责把镜头移向主角, 不参与输入初始化。
        [HarmonyPatch(typeof(MapScreen), "FastMoveCameraToMainParty")]
        internal static class NoScreenFastMoveToMainParty
        {
            private static bool Prefix() { return !NationalWillOrders.ShouldControlCamera; }
        }

        [HarmonyPatch(typeof(MapScreen), "TeleportCameraToMainParty")]
        internal static class NoScreenTeleportToMainParty
        {
            private static bool Prefix() { return !NationalWillOrders.ShouldControlCamera; }
        }

        [HarmonyPatch(typeof(MapCameraView), "FastMoveCameraToMainParty")]
        internal static class NoViewFastMoveToMainParty
        {
            private static bool Prefix() { return !NationalWillOrders.ShouldControlCamera; }
        }

        [HarmonyPatch(typeof(MapCameraView), "TeleportCameraToMainParty")]
        internal static class NoViewTeleportToMainParty
        {
            private static bool Prefix() { return !NationalWillOrders.ShouldControlCamera; }
        }
    }
}
