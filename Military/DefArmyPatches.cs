using System;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Party;

namespace FeudalInternalAffairs
{
    // 国防军的原版适配补丁(第 23 章 23.14)
    internal static class DefArmyPatches
    {
        // 单军上限: 原版队伍上限只有几百, 不覆写名册装不下、AI 会遣散超额
        //   (注: 实际值是 DefArmy.MaxLegionMen = 1000000, 旧注释写的 10000 已过时)
        [HarmonyPatch(typeof(PartyBase), "get_PartySizeLimit")]
        internal static class PartySizeLimitPatch
        {
            private static void Postfix(PartyBase __instance, ref int __result)
            {
                try
                {
                    var mp = __instance != null ? __instance.MobileParty : null;
                    if (mp == null) return;
                    bool ours = DefArmy.IsDefArmyParty(mp);
                    // v4.290: 舰队也要放宽。它由 LordPartyComponent 创建、ActualClan = Clan.PlayerClan,
                    //   但前缀是 "fia_navy_", 不在 IsDefArmyParty 的判据里 —— 于是走原版 limit
                    //   (≈20 + 25×玩家氏族 Tier ≈ 95~170), 玩家想带几百水手会被原版上限卡住。
                    //   语义上它与国防军同级(都是 mod 自建、都不吃原版军饷)。
                    //   只放宽"名册容量", 不改变其它任何判据。
                    if (!ours)
                    {
                        try { ours = NavalTaskForce.IsTaskForce(mp); } catch { }
                    }
                    if (ours) { __result = DefArmy.MaxLegionMen; FixProbe.HitPartySizeWiden++; }
                }
                catch { }
            }
        }

        // v4.290: 国防军也不吃原版的"补金库"分支。
        //   背景: DefaultClanFinanceModel.AddPartyExpense 除了扣工资, 后面还有一段"补足":
        //     partyTradeGold = party.PartyTradeGold;   // 对 IsLordParty && LeaderHero != null 就是 LeaderHero.Gold
        //     partyTradeGold -= num3;                  // num3 已被下面的 GetTotalWage 补丁清零 = 0
        //     if (partyTradeGold < PartyGoldLowerThreshold /*= 5000*/) {
        //         num4 = 5000 - partyTradeGold;  num4 = Min(num4, num2);   // PlayerClan 不被砍预算
        //         party.PartyTradeGold += num4;  return -num4;             // 将军 +5000, 家族金 -5000
        //     }
        //   国防军登记在 Clan.PlayerClan.WarPartyComponents 里(ActualClan = PlayerClan), 所以每天
        //   都会从玩家金库搬 5000 塞进将军私囊。v4.252 的 GetTotalWage 清零只让 num3 = 0,
        //   **拦不住这个分支**(0 < 5000 照样成立)。
        //   而 MakeGeneral 又写了 hero.Gold = 0, 于是补足必然触发; 上限恰好 5000 使
        //   AddIncomeFromParty 的 >10000 回流门槛永不达成 -> 钱永久沉淀在玩家看不见的将军金库里;
        //   退休/解散/哗变又都只 ChangeState(Disabled), 完全不回收 hero.Gold。
        //   修法与海军侧同源(NavalTaskForceAi.NoNativePartyExpense): 必须 Prefix 返回 false
        //   整条跳过, 不能改返回值 —— 那时 party.PartyTradeGold += num4 已经写进去了,
        //   只改返回值等于"国库不扣钱但将军白拿 5000"(凭空印钱)。
        [HarmonyPatch(typeof(DefaultClanFinanceModel), "AddPartyExpense")]
        internal static class DefArmyNoNativePartyExpensePatch
        {
            private static void Prepare()
            {
                DLog.Force("国防军: 补丁已应用 —— DefaultClanFinanceModel.AddPartyExpense 对 fia_def_ 整条跳过"
                    + "(否则每天从国库搬 5000/军团 给将军补足 PartyTradeGold)");
            }

            private static bool Prefix(MobileParty party)
            {
                try
                {
                    if (party == null) return true;
                    if (DefArmy.IsDefArmyParty(party)) { FixProbe.HitPartyExpenseSkip++; return false; }
                    return true;
                }
                catch { return true; }
            }
        }

        // v4.290: 领主军军饷双扣。
        //   mod 的 LordArmy.DailyWages 已经对**全世界**领主部队扣过一遍
        //   (遍历 Campaign.MobileParties -> IsLordArmy -> 非主队 -> PayOne), 而原版 AddPartyExpense
        //   又扣一遍, 扣的是**同一个口袋**:
        //     MobileParty.PartyTradeGold 对 IsLordParty && LeaderHero != null 就是 LeaderHero.Gold,
        //     而 LordArmy.PayerOf 也优先返回 p.LeaderHero。
        //   原先只豁免了玩家主队(LordArmy.cs:266), 于是玩家家族非主队 + 全国领主 + 全地图 AI 领主
        //   每天被扣两次; 再叠加 mod 自己的欠饷链(家族借债 -> 逐人逃兵)与原版独立的
        //   HasUnpaidWages 士气链, 两条链路互相不知道对方。
        //   修法与海军侧/国防军侧同源: 整条 Prefix 跳过, 军饷只由 mod 收。
        //   判据复用 LordArmy.IsLordArmyForWage, 与 DailyWages 的收费范围逐字一致。
        [HarmonyPatch(typeof(DefaultClanFinanceModel), "AddPartyExpense")]
        internal static class LordArmyNoNativeExpensePatch
        {
            private static void Prepare()
            {
                DLog.Force("领主军: 补丁已应用 —— DefaultClanFinanceModel.AddPartyExpense 对领主部队整条跳过"
                    + "(军饷由 LordArmy.DailyWages 收, 消除双扣)");
            }

            private static bool Prefix(MobileParty party)
            {
                try
                {
                    if (party != null && LordArmy.IsLordArmyForWage(party)) { FixProbe.HitPartyExpenseSkip++; return false; }
                    return true;
                }
                catch { return true; }
            }
        }

        // v4.252: 国防军不吃原版军饷 —— 军饷由本 mod 收(0.5/兵/日, 走国库, 有欠饷/哗变机制)。
        //   原来国防军会被原版按"家族金库(= 玩家金币)"再扣一遍工资, 玩家金币被扣空后原版欠薪逃兵,
        //   于是 v4.89 在 PayWages 里加了"把玩家金币补到 30 日军饷"的兜底 —— 那是每天凭空印钱
        //   (经济审计实测: 有军团时国库永久 ≥30 日饷, 破产永不发生)。现在从源头掐断:
        //   原版算国防军工资时返回 0, 兜底随之删除。
        [HarmonyPatch(typeof(DefaultPartyWageModel), "GetTotalWage")]
        internal static class DefArmyNoNativeWagePatch
        {
            private static void Postfix(MobileParty mobileParty, ref ExplainedNumber __result)
            {
                try
                {
                    if (mobileParty != null && DefArmy.IsDefArmyParty(mobileParty))
                        __result = new ExplainedNumber(0f);
                }
                catch { }
            }
        }

        // 速度锁死 3.0(不论人数/负重/俘虏/地形, 也不吃道路加成)
        // v4.248: 全工程有**三个** CalculateFinalSpeed 补丁(本补丁 / ArmyDoctrine 铁路运输 / KingdomTraitPatches
        //   王国特性速度), Postfix 之间没有固定执行顺序 —— 任何在速度锁之后跑的 AddFactor 都会破坏"恒 3.0"
        //   (用户反复反馈"人多的国防军部队速度快, 人少的又慢")。现在再补一个 Finalizer: Finalizer 一定在所有
        //   Postfix 之后执行, 是最终仲裁者, 因此国防军速度无论如何都是 3.0。
        [HarmonyPatch(typeof(DefaultPartySpeedCalculatingModel), "CalculateFinalSpeed")]
        internal static class SpeedLockPatch
        {
            private static void Postfix(MobileParty mobileParty, ref ExplainedNumber finalSpeed)
            {
                try
                {
                    if (mobileParty != null && DefArmy.IsDefArmyParty(mobileParty))
                        finalSpeed = new ExplainedNumber(DefArmy.SpeedLock);
                }
                catch { }
            }

            private static void Finalizer(MobileParty mobileParty, ref ExplainedNumber __result)
            {
                try
                {
                    if (mobileParty != null && DefArmy.IsDefArmyParty(mobileParty))
                        __result = new ExplainedNumber(DefArmy.SpeedLock);
                }
                catch { }
            }
        }
    }
}
