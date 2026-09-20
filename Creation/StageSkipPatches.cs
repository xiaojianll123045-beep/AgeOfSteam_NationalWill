using System;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterCreationContent;
using TaleWorlds.Core;

namespace FeudalInternalAffairs
{
    // 玩家是国家意志: 个人成长阶段全部不加载(捏脸/出身叙事/属性点/旗帜/家族命名/总览)
    // 只留"选国家"。选完之后 NextStage 会直接 ApplyFinalEffects + 结束角色创建。
    internal static class StageSkipPatches
    {
        [HarmonyPatch(typeof(CharacterCreationManager), "AddStage")]
        internal static class StageSkip
        {
            private static bool Prefix(CharacterCreationStageBase stage)
            {
                try
                {
                    if (stage == null || DLog.Flag("nostageskip")) return true;
                    if (ModConflictGuard.Check()) return true;
                    if (stage is CharacterCreationFaceGeneratorStage
                        || stage is CharacterCreationNarrativeStage
                        || stage is CharacterCreationOptionsStage
                        || stage is CharacterCreationBannerEditorStage
                        || stage is CharacterCreationClanNamingStage
                        || stage is CharacterCreationReviewStage)
                    {
                        DLog.Info("跳过角色创建阶段: " + stage.GetType().Name);
                        return false;
                    }
                }
                catch (Exception ex) { DLog.Force("StageSkip 异常: " + ex.Message); }
                return true;
            }
        }

        // v4.75c: 新流程不在角色创建页选国家(改到地图上点领选国):
        // 第一次推进到"选国家"阶段后, 直接走原版收尾(ApplyFinalEffects + FinalizeCharacterCreationState)完成角色创建。
        // 不能在这里再调 NextStage: 原版实现是"索引自增 -> 旧阶段 OnFinalize -> 激活下一阶段",
        // 此刻 Culture 界面刚被激活, 再走一次会在未初始化的界面 VM 上抛空引用并弄乱索引(已实测崩溃)。
        internal static class SkipCultureStage
        {
            [HarmonyPatch(typeof(CharacterCreationManager), "NextStage")]
            internal static class NextStagePatch
            {
                private static readonly System.Reflection.FieldInfo StageIndexField
                    = AccessTools.Field(typeof(CharacterCreationManager), "_stageIndex");
                private static readonly System.Reflection.FieldInfo FurthestField
                    = AccessTools.Field(typeof(CharacterCreationManager), "_furthestStageIndex");

                private static void Postfix(CharacterCreationManager __instance)
                {
                    try
                    {
                        if (DLog.Flag("keepcreation") || ModConflictGuard.Check()) return;
                        if (!(__instance.CurrentStage is CharacterCreationCultureStage)) return;

                        // 1) 给一个默认文化(ApplyCulture 会把它写进主角/家族, 不能为空)
                        var content = __instance.CharacterCreationContent;
                        try
                        {
                            var cur = content != null ? content.SelectedCulture : null;
                            if (content != null && cur == null)
                            {
                                foreach (var k in Kingdom.All)
                                {
                                    if (k == null || k.IsMinorFaction || k.IsBanditFaction || k.Culture == null) continue;
                                    // SelectedCulture 的 setter 非公开, 走反射
                                    var prop = AccessTools.Property(typeof(CharacterCreationContent), "SelectedCulture");
                                    var setter = prop != null ? prop.GetSetMethod(true) : null;
                                    if (setter != null) setter.Invoke(content, new object[] { k.Culture });
                                    break;
                                }
                            }
                        }
                        catch { }

                        // 2) 阶段索引安全置尾(防止后续任何推进触发 ActivateStage 越界)
                        try
                        {
                            int count = __instance.GetTotalStagesCount();
                            if (StageIndexField != null) StageIndexField.SetValue(__instance, count);
                            if (FurthestField != null) FurthestField.SetValue(__instance, count);
                        }
                        catch { }

                        // 3) 原版收尾: 应用最终效果 + 结束角色创建(进入地图)
                        DLog.Force("角色创建: 进入选国家阶段 -> 直接完成角色创建(进入地图选国)");
                        __instance.ApplyFinalEffects();
                        var state = GameStateManager.Current != null ? GameStateManager.Current.ActiveState as CharacterCreationState : null;
                        if (state != null) state.FinalizeCharacterCreationState();
                        else DLog.Force("警告: 找不到 CharacterCreationState, 角色创建可能未正确结束");
                    }
                    catch (Exception ex) { DLog.Force("跳过选国家阶段异常: " + ex.Message); }
                }
            }
        }
    }
}
