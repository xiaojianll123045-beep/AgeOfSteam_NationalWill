using System;
using HarmonyLib;
using SandBox.View.Map;
using TaleWorlds.InputSystem;

namespace FeudalInternalAffairs
{
    // 让"右键拖动=平移地图"的手感与原版左键拖动完全一致(系统光标保持可见、位置自由)。
    //
    // 原版: 右键=旋转地图。旋转期间原版会做两件事:
    //   1) MapCursor.SetVisible(false) 内部用 IsKeyDown(右键/中键) 判断"是否在旋转",
    //      旋转中 -> MapScreen.SetMouseVisible(false) 隐藏系统光标 + 换成地面光圈;
    //   2) MapScreen.HandleMouse 调 MBWindowManager.DontChangeCursorPos() 锁住光标位置
    //      (所以位置差恒为 0, 只能拿到原始增量)。
    //
    // 我们的右键是平移(原版左键的行为), 左键拖动时原版不做这两件事 ->
    // 这里在右键按住期间把右键"伪装成没按", 让光标行为和原版左键一致。
    internal static class MapCursorPatches
    {
        // 右键按住时, 在 MapCursor.SetVisible 执行期间伪装"右键没按"
        // v4.81: 条件从 IsActive 扩展到 ShouldControlCamera —— 选国阶段同样反制
        //        (原先选国阶段不生效, 原版旋转模式会锁死光标位置, 导致右键平移完全无效)
        [HarmonyPatch(typeof(MapCursor), "SetVisible")]
        internal static class CursorVisibilityPatch
        {
            internal static bool Faking;

            private static void Prefix()
            {
                try { Faking = NationalWillOrders.ShouldControlCamera && Input.IsKeyDown(InputKey.RightMouseButton); }
                catch { Faking = false; }
            }

            private static void Postfix() { Faking = false; }
        }

        // 只伪装右键(中键仍按原版: 中键拖动=旋转, 旋转时隐藏光标是正常的)
        [HarmonyPatch(typeof(InputContext), "IsKeyDown", new[] { typeof(InputKey) })]
        internal static class FakeRightKeyPatch
        {
            private static bool Prefix(InputKey key, ref bool __result)
            {
                try
                {
                    if (CursorVisibilityPatch.Faking && key == InputKey.RightMouseButton)
                    {
                        __result = false;
                        return false;
                    }
                }
                catch { }
                return true;
            }
        }

        // 右键拖动期间不锁光标位置(原版左键拖动也不锁) -> 光标地面点能跟着走
        // 只在"地图右键拖动中"生效, 免得影响战场里按住右键格挡之类的情况
        // v4.81: 条件从 IsActive 扩展到 ShouldControlCamera(选国阶段同样需要)
        [HarmonyPatch(typeof(TaleWorlds.MountAndBlade.MBWindowManager), "DontChangeCursorPos")]
        internal static class NoCursorLockPatch
        {
            private static bool Prefix()
            {
                try
                {
                    if (NationalWillOrders.ShouldControlCamera && MapBoxSelect.RightDown) return false;
                }
                catch { }
                return true;
            }
        }
    }
}
