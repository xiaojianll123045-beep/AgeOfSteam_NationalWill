using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;

namespace FeudalInternalAffairs
{
    // 国家/文化特性表
    //   巴丹尼亚: 森林 +15% 移速 / 远程 +10 攻击 / 城镇繁荣度 -15%
    //   诺德:     海上 +15% 移速 / 掠夺战利品与货币 +20% / 异文化城镇 繁荣度 -15%、忠诚度 -20%
    //   斯图吉亚: 雪地 +15% 移速、+10% 攻击 / 林地 -20% 攻击
    internal static class KingdomTraits
    {
        internal const string BattaniaCultureId = "battania";
        internal const string NordCultureId = "nord";
        internal const string SturgiaCultureId = "sturgia";
        internal const string KhuzaitCultureId = "khuzait";

        internal const float ForestSpeedBonus = 0.15f;
        internal const float RangedDamageBonus = 10f;
        internal const float TownProsperityPenalty = -0.15f;

        internal const float NordSeaSpeedBonus = 0.15f;
        internal const float NordRaidLootBonus = 0.20f;
        internal const float NordForeignProsperityPenalty = -0.15f;
        internal const float NordForeignLoyaltyPenalty = -0.20f;

        internal const float SturgiaSnowSpeedBonus = 0.15f;
        internal const float SturgiaSnowAttackBonus = 1.10f;
        internal const float SturgiaForestAttackPenalty = 0.80f;

        internal const float KhuzaitCavalrySpeedBonus = 0.30f;
        internal const float KhuzaitChargeDamageBonus = 1.15f;
        internal const float KhuzaitForeignRecruitPenalty = 0.75f;

        internal const string VlandiaCultureId = "vlandia";
        internal const float VlandiaMercenaryIncomeBonus = 0.30f;
        internal const float VlandiaMercenaryHireCostFactor = 0.40f;   // 费用/门槛 -60%
        internal const float VlandiaPersuasionDifficultyBonus = 1.30f;

        internal const string AseraiCultureId = "aserai";
        internal const float AseraiDesertAttackBonus = 1.15f;
        internal const float AseraiTradeIncomeBonus = 0.25f;
        internal const float AseraiAccuracyPenalty = 1.15f;   // 不精度 +15% = 精度 -15%

        // 队内骑兵占多数?
        internal static bool IsCavalryMajority(MobileParty party)
        {
            try
            {
                var roster = party != null && party.Party != null ? party.Party.MemberRoster : null;
                if (roster == null) return false;
                int total = 0, mounted = 0;
                foreach (var element in roster.GetTroopRoster())
                {
                    var character = element.Character;
                    if (character == null || character.IsHero) continue;
                    int n = element.Number;
                    if (n <= 0) continue;
                    total += n;
                    if (character.IsMounted) mounted += n;
                }
                return total > 0 && mounted * 2 > total;
            }
            catch { return false; }
        }

        // 开局选国家页面上显示的该国特性(绿=增益, 红=减益)
        internal static List<KeyValuePair<bool, string>> GetTraitLines(Kingdom kingdom)
        {
            var list = new List<KeyValuePair<bool, string>>();
            try
            {
                string id = kingdom != null && kingdom.Culture != null ? kingdom.Culture.StringId : null;
                switch (id)
                {
                    case BattaniaCultureId:
                        list.Add(new KeyValuePair<bool, string>(true, "森林地形：移动速度 +15%"));
                        list.Add(new KeyValuePair<bool, string>(true, "远程兵种：攻击 +10"));
                        list.Add(new KeyValuePair<bool, string>(false, "所有城镇：繁荣度增长 -15%"));
                        break;
                    case NordCultureId:
                        list.Add(new KeyValuePair<bool, string>(true, "海上：移动速度 +15%"));
                        list.Add(new KeyValuePair<bool, string>(true, "掠夺城市与农村：战利品和货币 +20%"));
                        list.Add(new KeyValuePair<bool, string>(false, "异文化城镇：繁荣度 -15%、忠诚度 -20%"));
                        break;
                    case SturgiaCultureId:
                        list.Add(new KeyValuePair<bool, string>(true, "雪地：攻击 +10%、移动速度 +15%"));
                        list.Add(new KeyValuePair<bool, string>(false, "林地：攻击 -20%"));
                        break;
                    case KhuzaitCultureId:
                        list.Add(new KeyValuePair<bool, string>(true, "队内骑兵占多数时：大地图移动速度 +30%"));
                        list.Add(new KeyValuePair<bool, string>(true, "骑兵冲撞伤害 +15%"));
                        list.Add(new KeyValuePair<bool, string>(false, "异文化村庄：可招募士兵 -25%"));
                        break;
                    case VlandiaCultureId:
                        list.Add(new KeyValuePair<bool, string>(true, "雇佣军收入 +30%"));
                        list.Add(new KeyValuePair<bool, string>(true, "招募雇佣军家族费用 -60%"));
                        list.Add(new KeyValuePair<bool, string>(false, "其他领主加入家族的谈判难度 +30%"));
                        break;
                    case AseraiCultureId:
                        list.Add(new KeyValuePair<bool, string>(true, "沙漠地形：攻击 +15%"));
                        list.Add(new KeyValuePair<bool, string>(true, "贸易收入 +25%"));
                        list.Add(new KeyValuePair<bool, string>(false, "射手精度 -15%"));
                        break;
                }
            }
            catch { }
            return list;
        }

        // 开局选国家页面上显示的该国特性说明(弹窗/日志用)
        internal static string DescribeKingdom(Kingdom kingdom)
        {
            try
            {
                string id = kingdom != null && kingdom.Culture != null ? kingdom.Culture.StringId : null;
                switch (id)
                {
                    case BattaniaCultureId:
                        return "【巴丹尼亚】\n· 森林地形：移动速度 +15%\n· 远程兵种：攻击 +10\n· 所有城镇：繁荣度增长 -15%";
                    case NordCultureId:
                        return "【诺德】\n· 海上：移动速度 +15%\n· 掠夺城市与农村：战利品和货币 +20%\n· 异文化城镇：繁荣度 -15%、忠诚度 -20%";
                    case SturgiaCultureId:
                        return "【斯图吉亚】\n· 雪地：攻击 +10%、移动速度 +15%\n· 林地：攻击 -20%";
                    case KhuzaitCultureId:
                        return "【库塞特】\n· 队内骑兵占多数时：大地图移动速度 +30%\n· 骑兵冲撞伤害 +15%\n· 异文化村庄：可招募士兵 -25%";
                    case VlandiaCultureId:
                        return "【瓦兰迪亚】\n· 雇佣军收入 +30%\n· 招募雇佣军家族费用 -60%\n· 其他领主加入家族的谈判难度 +30%";
                    case AseraiCultureId:
                        return "【阿塞莱】\n· 沙漠地形：攻击 +15%\n· 贸易收入 +25%\n· 射手精度 -15%";
                }
            }
            catch { }
            return null;
        }

        internal static string CultureIdOf(IFaction faction)
        {
            try
            {
                var culture = faction != null ? faction.Culture : null;
                return culture != null ? culture.StringId : null;
            }
            catch { return null; }
        }

        internal static bool IsForest(CampaignVec2 position)
        {
            try
            {
                var mapScene = Campaign.Current != null ? Campaign.Current.MapSceneWrapper : null;
                return mapScene != null && mapScene.GetTerrainTypeAtPosition(position) == TerrainType.Forest;
            }
            catch { return false; }
        }

        internal static bool IsSnow(CampaignVec2 position)
        {
            try
            {
                var mapScene = Campaign.Current != null ? Campaign.Current.MapSceneWrapper : null;
                return mapScene != null && mapScene.GetTerrainTypeAtPosition(position) == TerrainType.Snow;
            }
            catch { return false; }
        }

        internal static TerrainType? MissionTerrain()
        {
            try
            {
                var mission = Mission.Current;
                if (mission == null || !mission.HasValidTerrainType) return null;
                return mission.TerrainType;
            }
            catch { return null; }
        }

        internal static TerrainType? EventTerrain(PartyBase party)
        {
            try
            {
                var mapEvent = party != null ? party.MapEvent : null;
                return mapEvent != null ? (TerrainType?)mapEvent.EventTerrainType : null;
            }
            catch { return null; }
        }
    }

    internal static class KingdomTraitPatches
    {
        // ===== 移动速度: 巴丹森林 / 诺德海上 / 斯图吉亚雪地 =====
        [HarmonyPatch(typeof(DefaultPartySpeedCalculatingModel), "CalculateFinalSpeed")]
        internal static class Speed
        {
            private static void Postfix(MobileParty mobileParty, ref ExplainedNumber finalSpeed)
            {
                try
                {
                    if (mobileParty == null || mobileParty.MapFaction == null) return;
                    string culture = KingdomTraits.CultureIdOf(mobileParty.MapFaction);

                    if (culture == KingdomTraits.NordCultureId)
                    {
                        if (mobileParty.IsCurrentlyAtSea)
                            finalSpeed.AddFactor(KingdomTraits.NordSeaSpeedBonus, new TextObject("诺德航海"));
                        return;
                    }

                    if (mobileParty.CurrentSettlement != null) return;

                    if (culture == KingdomTraits.BattaniaCultureId && KingdomTraits.IsForest(mobileParty.Position))
                        finalSpeed.AddFactor(KingdomTraits.ForestSpeedBonus, new TextObject("巴丹尼亚森林"));
                    else if (culture == KingdomTraits.SturgiaCultureId && KingdomTraits.IsSnow(mobileParty.Position))
                        finalSpeed.AddFactor(KingdomTraits.SturgiaSnowSpeedBonus, new TextObject("斯图吉亚雪原"));
                    else if (culture == KingdomTraits.KhuzaitCultureId && KingdomTraits.IsCavalryMajority(mobileParty))
                        finalSpeed.AddFactor(KingdomTraits.KhuzaitCavalrySpeedBonus, new TextObject("库塞特骑军"));
                }
                catch { }
            }
        }

        // ===== 城镇繁荣度: 巴丹全境 -15% / 诺德异文化城 -15% =====
        [HarmonyPatch(typeof(DefaultSettlementProsperityModel), "CalculateProsperityChange")]
        internal static class TownProsperity
        {
            internal static void Postfix(Town fortification, ref ExplainedNumber __result)
            {
                try
                {
                    if (fortification == null || fortification.OwnerClan == null) return;
                    string ownerCulture = KingdomTraits.CultureIdOf(fortification.MapFaction);
                    string townCulture = fortification.Culture != null ? fortification.Culture.StringId : null;

                    if (ownerCulture == KingdomTraits.BattaniaCultureId)
                        __result.AddFactor(KingdomTraits.TownProsperityPenalty, new TextObject("巴丹尼亚衰败"));
                    else if (ownerCulture == KingdomTraits.NordCultureId && townCulture != KingdomTraits.NordCultureId)
                        __result.AddFactor(KingdomTraits.NordForeignProsperityPenalty, new TextObject("诺德异邦"));
                }
                catch { }
            }
        }

        // ===== 忠诚度: 诺德异文化城 -20% =====
        [HarmonyPatch(typeof(DefaultSettlementLoyaltyModel), "CalculateLoyaltyChange")]
        internal static class TownLoyalty
        {
            private static void Postfix(Town town, ref ExplainedNumber __result)
            {
                try
                {
                    if (town == null || town.OwnerClan == null) return;
                    if (KingdomTraits.CultureIdOf(town.MapFaction) != KingdomTraits.NordCultureId) return;
                    if (town.Culture != null && town.Culture.StringId == KingdomTraits.NordCultureId) return;
                    __result.AddFactor(KingdomTraits.NordForeignLoyaltyPenalty, new TextObject("诺德异邦"));
                }
                catch { }
            }
        }

        // ===== 掠夺战利品/货币 +20% =====
        [HarmonyPatch(typeof(DefaultRaidModel), "GetRaidLootMultiplier")]
        internal static class RaidLoot
        {
            private static void Postfix(PartyBase receivingParty, ref ExplainedNumber __result)
            {
                try
                {
                    if (receivingParty == null) return;
                    if (KingdomTraits.CultureIdOf(receivingParty.MapFaction) != KingdomTraits.NordCultureId) return;
                    __result.AddFactor(KingdomTraits.NordRaidLootBonus, new TextObject("诺德掠夺"));
                }
                catch { }
            }
        }

        // ===== 远程伤害: 巴丹 +10 =====
        [HarmonyPatch(typeof(DefaultStrikeMagnitudeModel), "CalculateStrikeMagnitudeForMissile")]
        internal static class RangedDamage
        {
            private static void Postfix(ref AttackInformation attackInformation, ref float __result)
            {
                try
                {
                    var agent = attackInformation.AttackerAgent;
                    var character = agent != null ? agent.Character : null;
                    var culture = character != null ? character.Culture : null;
                    if (culture == null || culture.StringId != KingdomTraits.BattaniaCultureId) return;
                    __result += KingdomTraits.RangedDamageBonus;
                }
                catch { }
            }
        }

        // ===== 斯图吉亚地形攻击(实战) + 库塞特骑兵冲撞 =====
        internal static class TerrainAttack
        {
            private static void Apply(ref AttackInformation attackInformation, ref float __result)
            {
                try
                {
                    var agent = attackInformation.AttackerAgent;
                    var character = agent != null ? agent.Character : null;
                    var culture = character != null ? character.Culture : null;
                    if (culture == null) return;

                    if (culture.StringId == KingdomTraits.SturgiaCultureId)
                    {
                        var terrain = KingdomTraits.MissionTerrain();
                        if (terrain == null) return;
                        if (terrain == TerrainType.Snow) __result *= KingdomTraits.SturgiaSnowAttackBonus;
                        else if (terrain == TerrainType.Forest) __result *= KingdomTraits.SturgiaForestAttackPenalty;
                    }
                    else if (culture.StringId == KingdomTraits.KhuzaitCultureId)
                    {
                        // 骑在马上造成的伤害(冲撞/骑战)
                        if (agent.HasMount) __result *= KingdomTraits.KhuzaitChargeDamageBonus;
                    }
                    else if (culture.StringId == KingdomTraits.AseraiCultureId)
                    {
                        var terrain = KingdomTraits.MissionTerrain();
                        if (terrain == TerrainType.Desert) __result *= KingdomTraits.AseraiDesertAttackBonus;
                    }
                }
                catch { }
            }

            [HarmonyPatch(typeof(DefaultStrikeMagnitudeModel), "CalculateStrikeMagnitudeForMissile")]
            internal static class Missile
            {
                private static void Postfix(ref AttackInformation attackInformation, ref float __result)
                {
                    Apply(ref attackInformation, ref __result);
                }
            }

            [HarmonyPatch(typeof(DefaultStrikeMagnitudeModel), "CalculateStrikeMagnitudeForSwing")]
            internal static class Swing
            {
                private static void Postfix(ref AttackInformation attackInformation, ref float __result)
                {
                    Apply(ref attackInformation, ref __result);
                }
            }

            [HarmonyPatch(typeof(DefaultStrikeMagnitudeModel), "CalculateStrikeMagnitudeForThrust")]
            internal static class Thrust
            {
                private static void Postfix(ref AttackInformation attackInformation, ref float __result)
                {
                    Apply(ref attackInformation, ref __result);
                }
            }
        }

        // ===== 地形攻击(大地图自动结算): 斯图吉亚雪地/林地, 阿塞莱沙漠 =====
        [HarmonyPatch(typeof(PartyBase), "GetCustomStrength")]
        internal static class AutoResolveStrength
        {
            private static void Postfix(PartyBase __instance, ref float __result)
            {
                try
                {
                    if (__instance == null) return;
                    string culture = KingdomTraits.CultureIdOf(__instance.MapFaction);
                    if (culture != KingdomTraits.SturgiaCultureId && culture != KingdomTraits.AseraiCultureId) return;
                    var terrain = KingdomTraits.EventTerrain(__instance);
                    if (terrain == null) return;

                    if (culture == KingdomTraits.SturgiaCultureId)
                    {
                        if (terrain == TerrainType.Snow) __result *= KingdomTraits.SturgiaSnowAttackBonus;
                        else if (terrain == TerrainType.Forest) __result *= KingdomTraits.SturgiaForestAttackPenalty;
                    }
                    else if (culture == KingdomTraits.AseraiCultureId)
                    {
                        if (terrain == TerrainType.Desert) __result *= KingdomTraits.AseraiDesertAttackBonus;
                    }
                }
                catch { }
            }
        }

        // ===== 阿塞莱: 贸易收入 +25%(商队 / 工坊 / 关税) =====
        internal static class AseraiTradeIncome
        {
            [HarmonyPatch(typeof(DefaultClanFinanceModel), "CalculateOwnerIncomeFromCaravan")]
            internal static class Caravan
            {
                private static void Postfix(MobileParty caravan, ref int __result)
                {
                    try
                    {
                        var owner = caravan != null ? caravan.Owner : null;
                        if (owner == null || owner.Clan == null) return;
                        if (KingdomTraits.CultureIdOf(owner.Clan) != KingdomTraits.AseraiCultureId) return;
                        __result = (int)(__result * (1f + KingdomTraits.AseraiTradeIncomeBonus));
                    }
                    catch { }
                }
            }

            [HarmonyPatch(typeof(DefaultClanFinanceModel), "CalculateOwnerIncomeFromWorkshop")]
            internal static class Workshop
            {
                private static void Postfix(TaleWorlds.CampaignSystem.Settlements.Workshops.Workshop workshop, ref int __result)
                {
                    try
                    {
                        var owner = workshop != null ? workshop.Owner : null;
                        if (owner == null || owner.Clan == null) return;
                        if (KingdomTraits.CultureIdOf(owner.Clan) != KingdomTraits.AseraiCultureId) return;
                        __result = (int)(__result * (1f + KingdomTraits.AseraiTradeIncomeBonus));
                    }
                    catch { }
                }
            }

            [HarmonyPatch(typeof(DefaultClanFinanceModel), "CalculateTownIncomeFromTariffs")]
            internal static class Tariffs
            {
                private static void Postfix(Clan clan, ref ExplainedNumber __result)
                {
                    try
                    {
                        if (clan == null) return;
                        if (KingdomTraits.CultureIdOf(clan) != KingdomTraits.AseraiCultureId) return;
                        __result.AddFactor(KingdomTraits.AseraiTradeIncomeBonus, new TextObject("阿塞莱商路"));
                    }
                    catch { }
                }
            }
        }

        // ===== 阿塞莱: 射手精度 -15%(武器不精度 +15%) =====
        [HarmonyPatch(typeof(AgentStatCalculateModel), "GetWeaponInaccuracy")]
        internal static class AseraiArcherAccuracy
        {
            private static void Postfix(Agent agent, WeaponComponentData weapon, ref float __result)
            {
                try
                {
                    if (agent == null || weapon == null) return;
                    if (!weapon.IsRangedWeapon) return;
                    var character = agent.Character;
                    var culture = character != null ? character.Culture : null;
                    if (culture == null || culture.StringId != KingdomTraits.AseraiCultureId) return;
                    __result *= KingdomTraits.AseraiAccuracyPenalty;
                }
                catch { }
            }
        }

        // ===== 库塞特: 异文化村庄可招募士兵 -25% =====
        [HarmonyPatch(typeof(DefaultVolunteerModel), "GetDailyVolunteerProductionProbability")]
        internal static class KhuzaitForeignRecruit
        {
            private static void Postfix(Settlement settlement, ref float __result)
            {
                try
                {
                    if (settlement == null) return;
                    if (KingdomTraits.CultureIdOf(settlement.MapFaction) != KingdomTraits.KhuzaitCultureId) return;
                    if (settlement.Culture != null && settlement.Culture.StringId == KingdomTraits.KhuzaitCultureId) return;
                    __result *= KingdomTraits.KhuzaitForeignRecruitPenalty;
                }
                catch { }
            }
        }

        // ===== 瓦兰迪亚: 雇佣军收入 +30% =====
        [HarmonyPatch(typeof(DefaultClanFinanceModel), "CalculateClanIncome")]
        internal static class VlandiaMercenaryIncome
        {
            private static void Postfix(Clan clan, ref ExplainedNumber __result)
            {
                try
                {
                    if (clan == null || !clan.IsUnderMercenaryService) return;
                    if (KingdomTraits.CultureIdOf(clan.MapFaction) != KingdomTraits.VlandiaCultureId) return;
                    __result.AddFactor(KingdomTraits.VlandiaMercenaryIncomeBonus, new TextObject("瓦兰迪亚佣兵"));
                }
                catch { }
            }
        }

        // ===== 瓦兰迪亚: 招募雇佣军家族费用/门槛 -60% =====
        internal static class VlandiaMercenaryHire
        {
            [HarmonyPatch(typeof(DefaultDiplomacyModel), "GetScoreOfKingdomToHireMercenary")]
            internal static class KingdomScore
            {
                private static void Postfix(Kingdom kingdom, ref float __result)
                {
                    try
                    {
                        if (kingdom == null) return;
                        if (KingdomTraits.CultureIdOf(kingdom) != KingdomTraits.VlandiaCultureId) return;
                        __result *= KingdomTraits.VlandiaMercenaryHireCostFactor;
                    }
                    catch { }
                }
            }

            [HarmonyPatch(typeof(DefaultDiplomacyModel), "GetScoreOfMercenaryToJoinKingdom")]
            internal static class ClanScore
            {
                private static void Postfix(Kingdom kingdom, ref float __result)
                {
                    try
                    {
                        if (kingdom == null) return;
                        if (KingdomTraits.CultureIdOf(kingdom) != KingdomTraits.VlandiaCultureId) return;
                        __result /= KingdomTraits.VlandiaMercenaryHireCostFactor;   // 更愿意来 = 更便宜
                    }
                    catch { }
                }
            }
        }

        // ===== 瓦兰迪亚: 其他领主加入家族的谈判难度 +30% =====
        [HarmonyPatch(typeof(DefaultPersuasionModel), "GetDifficulty")]
        internal static class VlandiaPersuasion
        {
            private static void Postfix(ref float __result)
            {
                try
                {
                    var clan = Clan.PlayerClan;
                    if (clan == null) return;
                    if (KingdomTraits.CultureIdOf(clan) != KingdomTraits.VlandiaCultureId) return;
                    __result *= KingdomTraits.VlandiaPersuasionDifficultyBonus;
                }
                catch { }
            }
        }

        // NavalDLC 会替换部分模型, 用运行时查找补上(不硬引用 NavalDLC)
        internal static class NavalTraitPatches
        {
            internal static void Apply(Harmony harmony)
            {
                TryPatch(harmony, "NavalDLC.GameComponents.NavalDLCSettlementProsperityModel", "CalculateProsperityChange",
                    typeof(TownProsperity).GetMethod("Postfix", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public));
                TryPatch(harmony, "NavalDLC.GameComponents.NavalDLCSettlementLoyaltyModel", "CalculateLoyaltyChange",
                    typeof(TownLoyalty).GetMethod("Postfix", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public));
            }

            private static void TryPatch(Harmony harmony, string typeName, string methodName, MethodInfo postfix)
            {
                try
                {
                    var t = AccessTools.TypeByName(typeName);
                    if (t == null) { DLog.Info("没有 " + typeName + ", 跳过"); return; }
                    var m = AccessTools.Method(t, methodName);
                    if (m == null) { DLog.Force(typeName + "." + methodName + " 未找到"); return; }
                    harmony.Patch(m, postfix: new HarmonyMethod(postfix));
                    DLog.Force("已接管 " + typeName);
                }
                catch (Exception ex) { DLog.Force("NavalDLC 补丁失败(" + typeName + "): " + ex.Message); }
            }
        }
    }
}
