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
                        // v4.290 修复: ApplyFinalEffects 原先和下面的 FinalizeCharacterCreationState 在**同一个
                        //   try** 里, 于是它一旦抛异常, 收尾那一步就被整个跳过 —— 角色创建没有结束,
                        //   玩家会**卡在原版的"文化列表"界面上**(用户反馈: "地图选择国家怎么没了, 变成列表选择了")。
                        //   现在单独 try 住它: 即使它失败, 也保证 FinalizeCharacterCreationState 一定执行。
                        //   同时把**内层异常**打出来 —— TargetInvocationException.Message 只有
                        //   "Exception has been thrown by the target of an invocation", 真正的成因在 InnerException。
                        try { __instance.ApplyFinalEffects(); }
                        catch (Exception ex1)
                        {
                            string inner = ex1.InnerException != null
                                ? (ex1.InnerException.GetType().Name + ": " + ex1.InnerException.Message)
                                : "无内层";
                            DLog.Force("ApplyFinalEffects 异常(已忽略, 继续收尾): " + ex1.Message
                                + " | 内层 " + inner
                                + (ex1.InnerException != null && ex1.InnerException.StackTrace != null
                                    ? "\n" + ex1.InnerException.StackTrace : ""));
                            // v4.290 兜底: ApplyFinalEffects 里最关键的一步是 CharacterCreationContent.ApplyCulture,
                            //   它把文化写进主角与玩家氏族:
                            //     Hero.MainHero.Culture = SelectedCulture;
                            //     Clan.PlayerClan.Culture = SelectedCulture;
                            //     Clan.PlayerClan.ResetPlayerHomeAndFactionMidSettlement();
                            //     Hero.MainHero.BornSettlement = Clan.PlayerClan.HomeSettlement;
                            //   它一旦失败, 玩家就"没有文化" —— 后续一切按文化取值的逻辑(兵种/建筑/特性/
                            //   征兵/外交)都会走异常分支。这里单独补最关键的两步(设文化);
                            //   补设失败也不阻断下面的收尾。
                            try
                            {
                                var cul = __instance.CharacterCreationContent != null
                                    ? __instance.CharacterCreationContent.SelectedCulture : null;
                                if (cul != null)
                                {
                                    if (Hero.MainHero != null && Hero.MainHero.Culture == null) Hero.MainHero.Culture = cul;
                                    if (Clan.PlayerClan != null && Clan.PlayerClan.Culture == null) Clan.PlayerClan.Culture = cul;
                                    DLog.Force("ApplyFinalEffects 兜底: 已把文化补设为 " + cul.StringId);
                                }
                                else DLog.Force("ApplyFinalEffects 兜底: SelectedCulture 为 null, 无法补设文化");
                            }
                            catch (Exception ex2) { DLog.Force("ApplyFinalEffects 兜底失败: " + ex2.Message); }
                        }
                        var state = GameStateManager.Current != null ? GameStateManager.Current.ActiveState as CharacterCreationState : null;
                        // v4.290: 这一步也必须单独 try —— 实测异常就是**从这里**抛的, 不是 ApplyFinalEffects
                        //   (证据: "角色创建: 进入选国家阶段" 打印了, 但 "ApplyFinalEffects 异常" 没有)。
                        //   它内部是: UnregisterActiveStateDisableRequest -> CreateState<MapState>() ->
                        //   CleanAndPushState(...) -> SetVisualAsDirty -> OnCharacterCreationIsOver()。
                        //   任何一环抛异常都会让角色创建**没有结束** -> 玩家卡在原版"文化列表"界面,
                        //   而且选完之后状态不一致会崩(用户反馈: "还是列表选国, 选完了之后就崩了")。
                        //   即使失败也把类型/内层/堆栈打全, 便于定位。
                        if (state != null)
                        {
                            try { state.FinalizeCharacterCreationState(); }
                            catch (Exception ex3)
                            {
                                DLog.Force("FinalizeCharacterCreationState 异常: " + ex3.GetType().Name + ": " + ex3.Message
                                    + (ex3.InnerException != null
                                        ? " | 内层 " + ex3.InnerException.GetType().Name + ": " + ex3.InnerException.Message
                                            + " @ " + ex3.InnerException.StackTrace
                                        : "")
                                    + " @ " + ex3.StackTrace);
                            }
                        }
                        else DLog.Force("警告: 找不到 CharacterCreationState, 角色创建可能未正确结束");
                    }
                    // v4.290: 把堆栈也打出来 —— 原来只有 ex.Message, 无法定位是哪一步抛的。
                    //   实测 "角色创建: 进入选国家阶段" 那行**没有**打印, 说明异常发生在它之前,
                    //   而 :57-88 每一段都自带 catch, 所以必须靠堆栈才能看出确切位置。
                    catch (Exception ex)
                    {
                        DLog.Force("跳过选国家阶段异常: " + ex.GetType().Name + ": " + ex.Message
                            + (ex.InnerException != null
                                ? " | 内层 " + ex.InnerException.GetType().Name + ": " + ex.InnerException.Message
                                : "")
                            + "\n" + ex.StackTrace);
                    }
                }
            }
        }
    }
}
