using System;
using HarmonyLib;
using TaleWorlds.MountAndBlade;

namespace FeudalInternalAffairs
{
    // 屏蔽"外部单独安装的 RTS Camera": 玩家如果另外装了原版 RTSCamera / RTSCamera.CommandSystem 模块,
    // 这里把它们全部停掉, 只保留我们模块内置的这套(避免两套补丁互相打架)。
    // 注意: 我们自己的移植版 Harmony ID 已改成 FeudalRts*, 所以卸补丁时不会误伤自己。
    internal static class BanStandaloneRts
    {
        private static bool _applied;
        private static int _attempts;

        internal static void Apply(Harmony harmony)
        {
            if (_applied) return;
            _attempts++;
            if (_attempts > 600) { _applied = true; return; }   // 约 10 秒后停止扫描
            try
            {
                bool found = false;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    string n;
                    try { n = asm.GetName().Name; } catch { continue; }
                    if (n != "RTSCamera" && n != "RTSCamera.CommandSystem") continue;
                    if (asm == typeof(BanStandaloneRts).Assembly) continue;   // 我们自己
                    found = true;

                    foreach (var tn in new[] { "RTSCamera.RTSCameraSubModule", "RTSCamera.CommandSystem.CommandSystemSubModule" })
                    {
                        var t = asm.GetType(tn);
                        if (t == null) continue;
                        foreach (var mn in new[] { "OnSubModuleLoad", "OnSubModuleUnloaded", "OnGameStart", "OnGameEnd",
                                                   "OnBeforeInitialModuleScreenSetAsRoot", "OnApplicationTick",
                                                   "OnMissionBehaviorInitialize", "InitializeGameStarter" })
                        {
                            try
                            {
                                var mi = AccessTools.Method(t, mn);
                                if (mi == null) continue;
                                harmony.Patch(mi, prefix: new HarmonyMethod(
                                    AccessTools.Method(typeof(BanStandaloneRts), nameof(SkipPrefix))));
                            }
                            catch { }
                        }
                        DLog.Force("已屏蔽外部 RTS 模块: " + t.FullName);
                    }
                }
                if (found)
                {
                    _applied = true;
                    UnpatchTheirs();
                }
            }
            catch (Exception ex) { DLog.Force("屏蔽外部RTS异常: " + ex.Message); }
        }

        // 外部 RTS 模块的初始化一律跳过
        private static bool SkipPrefix()
        {
            return false;
        }

        // 如果他们比我们加载得早(补丁已经打上), 按他们的 Harmony ID 卸掉
        internal static void UnpatchTheirs()
        {
            try { new Harmony("FeudalBanHelper").UnpatchAll("RTSCameraPatch"); DLog.Force("已卸载外部 RTSCameraPatch 补丁"); } catch { }
            try { new Harmony("FeudalBanHelper").UnpatchAll("RTSCommandPatch"); DLog.Force("已卸载外部 RTSCommandPatch 补丁"); } catch { }
        }
    }
}
