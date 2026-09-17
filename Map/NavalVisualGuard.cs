using System;
using HarmonyLib;
using TaleWorlds.MountAndBlade;

namespace FeudalInternalAffairs
{
    // 精准修补: 我们的面板屏幕(PushScreen)打开时, 地图屏会被顶失活, 但 MapState 仍在跑。
    // 这个状态下 NavalDLC 的船可视化会在 Tick/UpdateEntityPosition 里访问非法内存 -> AccessViolation 崩溃
    // (国策树时代就是踩到这个, 当时用"跳过整个地图视觉 tick"绕开, 导致地图全冻)。
    //
    // 现在改成只跳过 NavalDLC 的船可视化: 地图其余部分照常更新, 也不会崩。
    // NavalDLC 未安装时整段跳过(用 AccessTools.TypeByName 懒加载)。
    internal static class NavalVisualGuard
    {
        private static bool _applied;

        internal static void Apply(Harmony harmony)
        {
            try
            {
                if (_applied) return;
                var t = AccessTools.TypeByName("NavalDLC.View.Map.Visuals.NavalMobilePartyVisual");
                if (t == null) { DLog.Info("海战DLC船可视化: 未安装, 跳过修补"); _applied = true; return; }

                int n = 0;
                var tick = AccessTools.Method(t, "Tick");
                if (tick != null) { harmony.Patch(tick, prefix: new HarmonyMethod(AccessTools.Method(typeof(NavalVisualGuard), "Prefix"))); n++; }
                var upd = AccessTools.Method(t, "UpdateEntityPosition");
                if (upd != null) { harmony.Patch(upd, prefix: new HarmonyMethod(AccessTools.Method(typeof(NavalVisualGuard), "Prefix"))); n++; }
                _applied = true;
                DLog.Force("海战DLC船可视化: 已挂精准跳过补丁 " + n + " 个(面板打开时跳过, 防崩)");
            }
            catch (Exception ex) { DLog.Force("海战DLC船可视化补丁失败: " + ex.Message); }
        }

        // 面板打开时: 跳过船可视化更新
        private static bool Prefix()
        {
            try { return !PanelScreen.AnyOpen; }
            catch { return true; }
        }
    }
}
