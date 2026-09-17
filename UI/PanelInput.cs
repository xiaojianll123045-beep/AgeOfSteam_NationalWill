using System;
using HarmonyLib;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.ScreenSystem;

namespace FeudalInternalAffairs
{
    // 让挂在 MapScreen 上的自建面板层能收到鼠标点击。
    // 原因: 原版地图各层(地图栏/名牌/信息栏...)都有自己的 InputRestrictions 优先级,
    // 我们的层默认优先级最低 -> 点击先被原版层吃掉, 控件收不到 Command.Click。
    // 处理: 1) 反射把 InputRestrictions.Order 顶到最高(该属性对外只读)
    //       2) 顺手 TrySetFocus, 让层拿到焦点(文本框也能输入)
    internal static class PanelInput
    {
        internal static void Apply(GauntletLayer layer)
        {
            try
            {
                if (layer == null) return;
                var ir = layer.InputRestrictions;
                if (ir != null)
                {
                    var f = AccessTools.Field(ir.GetType(), "<Order>k__BackingField");
                    if (f != null) f.SetValue(ir, 900);
                }
            }
            catch (Exception ex) { DLog.Info("面板输入优先级设置失败: " + ex.Message); }
            try { ScreenManager.TrySetFocus(layer); }
            catch { }
        }
    }
}
