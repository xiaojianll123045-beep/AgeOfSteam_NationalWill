using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using TaleWorlds.MountAndBlade;

namespace FeudalInternalAffairs
{
    // v4.82: 跳过两个开场视频
    //   ① 游戏启动片头: Module.SetInitialModuleScreenAsRootScreen 里播放 Native/Videos/TWLogo_and_Partners.ivf
    //      (Transpiler 把第一次 File.Exists 检查替换为 false, 原方法会走"无视频"分支直接进主菜单,
    //       其余前置逻辑如 OnBeforeInitialModuleScreenSetAsRoot 事件通知全部保留)
    //   ② 创建新战役动画: SandBoxGameManager.OnLoadFinished 播放 campaign_intro, 播完才启动角色创建
    //      (Prefix: 非读档时直接调 LaunchSandboxCharacterCreation 并置 IsLoaded, 跳过视频)
    // 调试: flags 文件写 keepvideo 可恢复原版行为。
    internal static class IntroSkipPatches
    {
        [HarmonyPatch(typeof(TaleWorlds.MountAndBlade.Module), "SetInitialModuleScreenAsRootScreen")]
        internal static class SkipStartupVideo
        {
            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                if (DLog.Flag("keepvideo")) return instructions;
                try
                {
                    var list = new List<CodeInstruction>(instructions);
                    for (int i = 0; i < list.Count; i++)
                    {
                        var ci = list[i];
                        if (ci.opcode == OpCodes.Call
                            && ci.operand is MethodInfo mi
                            && mi.DeclaringType == typeof(System.IO.File)
                            && mi.Name == "Exists")
                        {
                            // 原: ldloc.1 ; call File.Exists  ->  改: pop ; ldc.i4.0(false)
                            list[i] = new CodeInstruction(OpCodes.Pop);
                            list.Insert(i + 1, new CodeInstruction(OpCodes.Ldc_I4_0));
                            DLog.Force("开场跳过: 已拦截游戏启动片头视频(TWLogo_and_Partners)");
                            break;
                        }
                    }
                    return list;
                }
                catch (Exception ex)
                {
                    DLog.Force("开场跳过: Transpiler 失败, 保持原版(" + ex.Message + ")");
                    return instructions;
                }
            }
        }

        [HarmonyPatch]
        internal static class SkipCampaignIntro
        {
            private static MethodBase TargetMethod()
            {
                try
                {
                    var t = AccessTools.TypeByName("SandBox.SandBoxGameManager");
                    return t != null ? AccessTools.Method(t, "OnLoadFinished") : null;
                }
                catch { return null; }
            }

            private static bool Prefix(object __instance)
            {
                try
                {
                    if (DLog.Flag("keepvideo")) return true;
                    if (__instance == null) return true;
                    var t = __instance.GetType();

                    var loadingProp = AccessTools.Property(t, "LoadingSavedGame");
                    bool loading = loadingProp != null && (bool)loadingProp.GetValue(__instance);
                    if (loading) return true;   // 读档路径保持原样

                    var launch = AccessTools.Method(t, "LaunchSandboxCharacterCreation");
                    if (launch == null) return true;

                    launch.Invoke(__instance, null);
                    try
                    {
                        var loadedProp = t.GetProperty("IsLoaded", BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
                        if (loadedProp != null && loadedProp.CanWrite) loadedProp.SetValue(__instance, true);
                    }
                    catch { }
                    DLog.Force("开场跳过: 已跳过创建新战役动画(campaign_intro), 直接进入角色创建");
                    return false;
                }
                catch (Exception ex)
                {
                    DLog.Force("开场跳过: 战役动画跳过失败, 回退原逻辑(" + ex.Message + ")");
                    return true;
                }
            }
        }
    }
}
