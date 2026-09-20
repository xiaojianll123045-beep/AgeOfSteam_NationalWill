using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterCreationContent;
using TaleWorlds.CampaignSystem.ViewModelCollection.CharacterCreation;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace FeudalInternalAffairs
{
    // 把角色创建的"选文化"界面换成"选国家"
    internal static class NationSelectionPatches
    {
        [HarmonyPatch(typeof(CharacterCreationCultureStageVM), MethodType.Constructor,
            new Type[] { typeof(CharacterCreationManager), typeof(Action), typeof(TextObject), typeof(Action), typeof(TextObject), typeof(Action<CultureObject>) })]
        internal static class NationList
        {
            internal static readonly Dictionary<CharacterCreationCultureVM, Kingdom> ItemToKingdom
                = new Dictionary<CharacterCreationCultureVM, Kingdom>();

            private static void Postfix(CharacterCreationCultureStageVM __instance)
            {
                try
                {
                    if (DLog.Flag("nonation") || ModConflictGuard.Check()) return;
                    Rebuild(__instance);
                }
                catch (Exception ex) { DLog.Force("NationList 异常: " + ex); }
            }

            private static void Rebuild(CharacterCreationCultureStageVM vm)
            {
                var kingdoms = Kingdom.All.Where(k => k != null
                    && !k.IsMinorFaction
                    && !k.IsBanditFaction
                    && !k.IsRebelClan
                    && k.Culture != null
                    && k.RulingClan != null).ToList();
                DLog.Info("文化阶段: Kingdom.All=" + Kingdom.All.Count + " 可用国家=" + kingdoms.Count);

                var source = vm.Cultures;
                if (source == null || source.Count == 0)
                {
                    DLog.Force("文化阶段: 原版文化列表为空, 保持原样");
                    return;
                }
                if (kingdoms.Count == 0)
                {
                    DLog.Force("文化阶段: 王国尚未生成, 本次保持原版文化列表");
                    return;
                }

                Action<CharacterCreationCultureVM> onSelect = new Action<CharacterCreationCultureVM>(vm.OnCultureSelection);

                ItemToKingdom.Clear();
                var list = new MBBindingList<CharacterCreationCultureVM>();
                foreach (var k in kingdoms)
                {
                    try
                    {
                        string name = k.Name != null ? k.Name.ToString() : k.Culture.Name.ToString();
                        var item = new CharacterCreationCultureVM(k.Culture, onSelect);
                        item.NameText = name;
                        item.ShortenedNameText = k.InformalName != null ? k.InformalName.ToString() : name;
                        item.CultureID = k.Culture.StringId;
                        item.CultureColor1 = Color.FromUint(k.Color);
                        string cultureDesc = k.EncyclopediaText != null ? k.EncyclopediaText.ToString() : null;
                        if (string.IsNullOrEmpty(cultureDesc) && k.Culture.EncyclopediaText != null) cultureDesc = k.Culture.EncyclopediaText.ToString();
                        if (!string.IsNullOrEmpty(cultureDesc)) item.DescriptionText = cultureDesc;
                        // 我们的特性加成/减益 -> 加进绿/红特性列表(和原版同样的显示方式)
                        foreach (var line in KingdomTraits.GetTraitLines(k))
                            item.Feats.Add(new CharacterCreationCultureFeatVM(line.Key, line.Value));
                        ItemToKingdom[item] = k;
                        list.Add(item);
                    }
                    catch (Exception ex) { DLog.Force("生成国家项失败: " + ex.Message); }
                }
                if (list.Count == 0) return;

                vm.Cultures = list;
                vm.Title = "选择你的国家";
                vm.Description = "你将成为这个国家的意志: 指挥领主与军团、颁布国策、决定战争与和平。";
                vm.SelectionText = "国家";

                Kingdom prev = null;
                if (!string.IsNullOrEmpty(NationChoice.ChosenKingdomId))
                {
                    prev = kingdoms.FirstOrDefault(x => x.StringId == NationChoice.ChosenKingdomId);
                }
                if (prev != null)
                {
                    var item = list.FirstOrDefault(x => ItemToKingdom.TryGetValue(x, out var kk) && kk == prev);
                    if (item != null) vm.OnCultureSelection(item);
                }
                else
                {
                    vm.CurrentSelectedCulture = null;
                }

                DLog.Force("文化阶段已替换为国家列表, 共 " + list.Count + " 个国家: "
                    + string.Join(",", kingdoms.Select(k => k.StringId)));
                // 注意(v4.75b): 自动跳过改由 StageSkipPatches.SkipCultureStage 在 manager 层做
                // (在这里调 VM 的 OnNextStage 会在构造期间空引用, 已废弃)
            }

            internal static CharacterCreationContent GetContent()
            {
                try
                {
                    var state = GameStateManager.Current != null ? GameStateManager.Current.ActiveState as CharacterCreationState : null;
                    var manager = state != null ? state.CharacterCreationManager : null;
                    return manager != null ? manager.CharacterCreationContent : null;
                }
                catch { return null; }
            }
        }

        [HarmonyPatch(typeof(CharacterCreationCultureStageVM), "OnCultureSelection")]
        internal static class NationSelect
        {
            private static void Postfix(CharacterCreationCultureVM selectedCulture)
            {
                try
                {
                    if (selectedCulture == null) return;
                    if (NationList.ItemToKingdom.TryGetValue(selectedCulture, out var kingdom) && kingdom != null)
                    {
                        NationChoice.ChosenKingdomId = kingdom.StringId;
                        DLog.Force("玩家选择了国家: " + (kingdom.Name != null ? kingdom.Name.ToString() : kingdom.StringId));
                        SetDefaultHeroName(kingdom);
                    }
                }
                catch (Exception ex) { DLog.Force("NationSelect 异常: " + ex.Message); }
            }

            // 玩家不输入姓名: 用所选国家的文化名字自动取一个
            private static void SetDefaultHeroName(Kingdom kingdom)
            {
                try
                {
                    var content = NationList.GetContent();
                    if (content == null || kingdom.Culture == null) return;
                    if (!string.IsNullOrEmpty(content.MainCharacterName)) return;
                    var list = Hero.MainHero != null && Hero.MainHero.IsFemale
                        ? kingdom.Culture.FemaleNameList : kingdom.Culture.MaleNameList;
                    if (list == null || list.Count == 0) return;
                    string name = list[MBRandom.RandomInt(list.Count)].ToString();
                    content.SetMainCharacterName(name);
                    DLog.Force("已自动设置玩家姓名: " + name);
                }
                catch (Exception ex) { DLog.Force("自动姓名失败: " + ex.Message); }
            }
        }
    }
}
