using System;
using System.Collections.Generic;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.GameState;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.ViewModelCollection.ClanManagement.Categories;
using TaleWorlds.Localization;

namespace FeudalInternalAffairs
{
    // 28.3 禁止原版入口(Patch 清单, 方法名经反射核对 1.4.x):
    //   1) 村庄/城镇菜单"招募士兵"入口 -> 禁用(改走 28.4 领主装备→征兵闭环)
    //   2) 部队界面"升级"按钮 -> 禁用
    //   3) 原版"组建军团(Army)" -> 禁用; 只保留我们多选右键的"组建军团"
    //   4) 原版部队界面"解散" -> 劫持为我们的解散(装备按 28.9 返还)
    internal static class VanillaArmySuppress
    {
        // ================= 1) 招募士兵入口(村庄/城镇) =================
        [HarmonyPatch(typeof(PlayerTownVisitCampaignBehavior), "game_menu_town_recruit_troops_on_condition")]
        internal static class HideTownRecruitMenu
        {
            private static bool Prefix(ref bool __result)
            {
                __result = false;
                return false;
            }
        }

        [HarmonyPatch(typeof(PlayerTownVisitCampaignBehavior), "game_menu_recruit_volunteers_on_condition")]
        internal static class HideVillageRecruitMenu
        {
            private static bool Prefix(ref bool __result)
            {
                __result = false;
                return false;
            }
        }

        // 入口兜底: 招募志愿兵界面直接打不开
        [HarmonyPatch(typeof(MenuContext), "OpenRecruitVolunteers")]
        internal static class BlockRecruitScreen
        {
            private static bool Prefix()
            {
                return false;
            }
        }

        // 入口兜底: 玩家的一切原版募兵动作不生效(AI 保留, 走 28.4)
        [HarmonyPatch(typeof(RecruitmentCampaignBehavior), "ApplyInternal")]
        internal static class BlockPlayerRecruitApply
        {
            private static bool Prefix(MobileParty side1Party)
            {
                return !IsPlayerParty(side1Party);
            }
        }

        [HarmonyPatch(typeof(RecruitmentCampaignBehavior), "GetRecruitVolunteerFromMap")]
        internal static class BlockPlayerRecruitMap
        {
            private static bool Prefix(MobileParty side1Party)
            {
                return !IsPlayerParty(side1Party);
            }
        }

        [HarmonyPatch(typeof(RecruitmentCampaignBehavior), "GetRecruitVolunteerFromIndividual")]
        internal static class BlockPlayerRecruitIndividual
        {
            private static bool Prefix(MobileParty side1Party)
            {
                return !IsPlayerParty(side1Party);
            }
        }

        // ================= 2) 升级按钮 =================
        [HarmonyPatch(typeof(PartyScreenLogic), "UpgradeTroop")]
        internal static class BlockUpgrade
        {
            private static bool Prefix()
            {
                return false;
            }
        }

        // 部队界面"升级"按钮置灰(28.3)
        [HarmonyPatch(typeof(PartyScreenLogic), "get_IsTroopUpgradesDisabled")]
        internal static class GrayUpgrade
        {
            private static void Postfix(ref bool __result)
            {
                __result = true;
            }
        }

        // ================= 3) 原版组建军团(Army) =================
        [HarmonyPatch(typeof(DefaultArmyManagementCalculationModel), "CanPlayerCreateArmy")]
        internal static class BlockPlayerArmyButton
        {
            private static bool Prefix(ref bool __result, ref TextObject disabledReason)
            {
                __result = false;
                try { disabledReason = new TextObject("原版军团已停用：请在地图上多选部队后右键「组建军团」"); }
                catch { }
                return false;
            }
        }

        // 玩家原版创建入口一律拦; 我们自己的多选右键组建军团(PlayerFormingArmy)与 AI 放行
        [HarmonyPatch(typeof(Kingdom), "CreateArmy")]
        internal static class BlockPlayerCreateArmy
        {
            private static bool Prefix(Hero armyLeader)
            {
                try
                {
                    if (NoAiControlPatches.PlayerFormingArmy) return true;
                    return armyLeader != Hero.MainHero;
                }
                catch { return true; }
            }
        }

        // ================= 4) 原版部队界面"解散"劫持 =================
        [HarmonyPatch(typeof(ClanPartiesVM), "OnDisbandCurrentParty")]
        internal static class HijackClanDisband
        {
            private static bool Prefix(ClanPartiesVM __instance)
            {
                try
                {
                    var item = __instance != null ? __instance.CurrentSelectedParty : null;
                    var pb = item != null ? item.Party : null;
                    var p = pb != null ? pb.MobileParty : null;
                    string msg;
                    if (OurDisband(p, out msg))
                    {
                        if (!string.IsNullOrEmpty(msg))
                        {
                            try { MapSelection.Message(msg); } catch { }
                            DLog.Force("解散(28.9): " + msg);
                        }
                        return false;   // 已按我们的流程处理, 跳过原版
                    }
                }
                catch (Exception ex) { DLog.Force("劫持解散异常: " + ex.Message); }
                return true;
            }
        }

        private static bool IsPlayerParty(MobileParty p)
        {
            try { return p != null && p.IsMainParty; } catch { return false; }
        }

        // 28.9 解散返还去向: 领主/家族部队 -> 领主私库(L:clan); 国防军/城防 -> 国家军械库(K:)
        internal static bool OurDisband(MobileParty p, out string msg)
        {
            msg = "";
            try
            {
                if (p == null || !p.IsActive || p.IsMainParty || p.IsCaravan || p.IsVillager) return false;
                if (p.IsDisbanding || p.Army != null) return false;   // 已在解散/军团成员 -> 交给原版军团流程

                // 国防军军团: 复用既有解散(兵员回守备营, 快照装备 100% 回国家军械库)
                if (DefArmy.LegionOf(p) != null)
                {
                    msg = DefArmy.DisbandLegion(p);
                    Soldiers.Remove(p);
                    return true;
                }

                var need = DefArmy.FreeIssueNeed(p);
                if (p.IsGarrison)
                {
                    // 城防守备: 装备回国家军械库(驻军部队本身不销毁)
                    string where = Deposit(need, Armory.NationalOwner(OurKingdom()), "国家军械库");
                    Soldiers.Remove(p);
                    msg = "城防守备装备已退还" + where;
                    return true;
                }

                // 领主/家族部队: 装备回领主私库
                string key = Armory.KeyOf(p);
                string to = Deposit(need, key, "领主私库");
                Soldiers.Remove(p);
                try { DisbandPartyAction.StartDisband(p); }
                catch { try { DestroyPartyAction.ApplyForDisbanding(p, p.CurrentSettlement); } catch { } }
                msg = "已解散「" + MapSelection.NameOf(p) + "」: 装备退还" + to;
                return true;
            }
            catch (Exception ex)
            {
                DLog.Force("我们的解散异常: " + ex.Message);
                msg = "";
                return false;
            }
        }

        private static Kingdom OurKingdom()
        {
            try { return NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null; }
            catch { return null; }
        }

        private static string Deposit(Dictionary<string, int> need, string key, string fallbackName)
        {
            int n = 0;
            try
            {
                if (need != null && !string.IsNullOrEmpty(key))
                {
                    foreach (var kv in need)
                    {
                        if (kv.Value <= 0 || string.IsNullOrEmpty(kv.Key)) continue;
                        Armory.AddKey(key, kv.Key, kv.Value);
                        n += kv.Value;
                    }
                }
            }
            catch { }
            string name = Armory.OwnerName(key);
            if (string.IsNullOrEmpty(name) || name == "?") name = fallbackName;
            return name + "(" + n + " 件)";
        }
    }
}
