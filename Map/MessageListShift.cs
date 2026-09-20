using System;
using HarmonyLib;
using TaleWorlds.MountAndBlade.GauntletUI;

namespace FeudalInternalAffairs
{
    // 消息流避让: 侧边栏打开时, 把左下角消息列表(SPChatLog)整体右滑让开。
    //
    // 原理(1.4.8 实证, 做法参考隔壁 _FeudalCalradia 的 MessageListShift):
    //   · 原版 prefab 自带 Default / Offset 两个视觉状态(0.14s 过渡动画)
    //   · 由 MPChatVM.ShouldHaveOffset 经 BoolStateChangerWidget 驱动切换
    //   · GauntletChatLogView.OnTick 每帧调用 GetShouldHaveOffset() 写入该值
    //   → 我们 Postfix 该方法, 面板打开时让它返回 true 即可(原版动画自动生效)
    [HarmonyPatch(typeof(GauntletChatLogView), "GetShouldHaveOffset")]
    internal static class MessageListShift
    {
        private static bool _lastState;

        private static void Postfix(ref bool __result)
        {
            try
            {
                // v4.104: 两档避让 —— 默认(Default=70)已让开导航栏; 侧边栏打开时切 Offset(760) 让开面板
                bool want = PanelScreen.AnyOpen && NationalWillOrders.IsActive;
                if (want != _lastState)
                {
                    _lastState = want;
                    DLog.Force("消息流避让: " + (want ? "右移让开侧边栏" : "滑回复位"));
                }
                if (want) __result = true;
            }
            catch { }
        }
    }
}
