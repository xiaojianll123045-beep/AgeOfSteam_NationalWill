using System;
using HarmonyLib;
using TaleWorlds.CampaignSystem.CharacterCreationContent;

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
    }
}
