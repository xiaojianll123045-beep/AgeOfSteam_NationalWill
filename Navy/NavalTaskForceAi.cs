using System;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Map;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Library;

namespace FeudalInternalAffairs
{
    // ==================== v4.276: 舰队的"AI 守卫" ====================
    //
    // 【两条看似矛盾的要求, 各自由谁满足】
    //
    //   ① "必须能走" —— 靠**放行**。
    //      全游戏唯一把 `MobileParty.PartyMoveMode` 置成 `MoveModeType.Point` 的入口是:
    //        MobilePartyAi.Tick(dt) -> TickInternal() -> GetBehaviors(out 行为, out 对象, out 目标点)
    //        -> SetAiBehavior(...) -> UpdateBehavior()
    //        -> MobileParty.SetNavigationModePoint(BehaviorTarget)      ← internal, 外部 mod 调不到
    //      (`MobilePartyAi` 反编译: Tick=311, TickInternal=427, GetBehaviors=477,
    //        SetAiBehavior=1405, UpdateBehavior=1412~1418)
    //      所以 `DefArmyAi.AiGuard`(Harmony Prefix on TickInternal) 必须对舰队**放行**,
    //      判据见 `NavalTaskForce.InCommand`(与海运的 `NavalTransport.InConvoy` 同一层级)。
    //      而 `MobileParty.GetTargetCampaignPosition` 只在 `PartyMoveMode == Point` 时
    //      才把 `MoveTargetPoint` 当推进目标, 否则(Hold)目标 = 自己当前位置 => 原地不动。
    //
    //   ② "AI 不接管" —— 靠**原版自己的开关 + 这里的两道兜底**。
    //      主开关是 `MobilePartyAi.DoNotMakeNewDecisions`(由 NavalTaskForce.Arm 设置, 见那边的长注释):
    //        · GetBehaviors:104746 不采纳主动行为 —— 但**仍然**以 DefaultBehavior 为起点走完整条
    //          细化/推进链, 所以"能走"不受影响;
    //        · AiHourlyTickBehavior.PartyHourlyAiTick:221345 直接 return —— 原版那套
    //          "给 AI 领主打分选目标"的派单系统根本不会作用到舰队上。
    //      本文件再加两道兜底, 因为主开关可能被别的系统清掉(例如读档、其它 mod、原版某些 Action):
    //        · NoInitiative —— 从"根因"上让 `ShouldPartyCheckInitiativeBehavior` 对舰队返回 false。
    //          默认实现(DefaultMobilePartyAIModel:88)对"非 MainParty 的独立部队"一律返回 true,
    //          也就是**默认就会给舰队算主动行为**(打劫匪/追敌人/躲风暴) —— 这是长期在航的舰队
    //          最可能被"接管"的路径。DLC 的 NavalDLCMobilePartyAIModel 是
    //          `return BaseModel.ShouldPartyCheckInitiativeBehavior(p)`, 所以打在基础模型上,
    //          装/不装 DLC 都命中。
    //        · LockBehavior —— 结果层: 只要舰队处于玩家命令锁定, 就把 AI 算出来的行为**强制拉回**
    //          我们设的 DefaultBehavior, 并把 behaviorObject 一并修正(否则 UpdateBehavior 里
    //          `AiBehaviorPartyBase != null && IsValid` 会指向别的目标)。
    //
    //   两条合起来 = "谁决定去哪"完全归玩家命令; "怎么走"仍由原版放出 PartyMoveMode。
    //
    //   ③ v4.277 追加第三个补丁(与前两条无关, 属"钱"的问题):
    //      NoNativeWage —— 原版对 `fia_navy_` 部队算工资时返回 0。理由见该类上的长注释
    //      (原版已经从氏族金库 = 国库扣过一遍水手工资, 不掐断就会与 FIA 新收的水手军饷双扣)。
    internal static class NavalTaskForceAi
    {
        private static readonly FieldInfo PartyField = AccessTools.Field(typeof(MobilePartyAi), "_mobileParty");

        private static MobileParty PartyOf(MobilePartyAi ai)
        {
            try { return PartyField != null ? PartyField.GetValue(ai) as MobileParty : null; }
            catch { return null; }
        }

        // ==================== 兜底 A: 舰队不参与"主动行为" ====================
        [HarmonyPatch(typeof(DefaultMobilePartyAIModel), "ShouldPartyCheckInitiativeBehavior")]
        internal static class NoInitiative
        {
            private static void Postfix(MobileParty mobileParty, ref bool __result)
            {
                try
                {
                    if (!__result || mobileParty == null) return;
                    if (NavalTaskForce.IsTaskForce(mobileParty)) __result = false;
                }
                catch { }
            }
        }

        // ==================== 兜底 B: 结果层强制拉回玩家命令 ====================
        //   仅在 `PlayerHold`(玩家下过命令且未交还)时生效 —— 玩家主动"放开"舰队后不该被这里锁住。
        //
        //   ==================== v4.278(第 6/7 项): 三处修正 ====================
        //   ① `bestTargetPoint` 原来写的是 `p.TargetPosition` —— 但 Point 模式**只认 MoveTargetPoint**
        //      (反编译实证: MobileParty.GetTargetCampaignPosition:4113-4115 `if (PartyMoveMode == MoveModeType.Point)
        //       finalTargetPosition = MoveTargetPoint;`, 而 `SetNavigationModePoint:4418` 也只写 MoveTargetPoint)。
        //      当前因为 SetMoveGoToPoint/GoToSettlement 都会把两者同步成同一个值, 所以"碰巧没出错",
        //      但**权威来源是 MoveTargetPoint**, 这里改成直接写它(语义正确, 且不怕将来原版只更新其中一个)。
        //   ② 白名单缺 `EngageParty` —— 交战命令(OrderEngage)期间"AI 不接管"直接失效:
        //      GetBehaviors 会拿主动行为(DLC 的躲风暴/避强敌)把我们的交战行为顶掉。补上。
        //      behaviorObject 用 `p.TargetParty.Party`(与 GetBehaviors 里 `_mobileParty.TargetParty` 同源),
        //      因为 UpdateBehavior 的 EngageParty 分支要调 `SetNavigationModeParty(AiBehaviorPartyBase.MobileParty)`。
        //   ③ 白名单缺 `Hold` —— 这条是**真实会冻死舰队**的路径(反编译实证):
        //      `AiPartyThinkBehavior.CheckMobilePartyActionAccordingToSettlement:284-333`(聚落易主/和谈/宣战回调)
        //      在"舰队的目标聚落刚易主"时直接调 `mobileParty.SetMoveModeHold()`, 而 `SetMoveModeHold:4280-4290`
        //      会 `ResetAllMovementParameters()` —— 把 DefaultBehavior 置成 `Hold`、TargetSettlement/TargetParty 清空、
        //      TargetPosition/MoveTargetPoint 都变成当前位置。★ 这条路径**完全不检查 `DoNotMakeNewDecisions`**,
        //      所以它绕过 NoInitiative / DoNotMakeNewDecisions 两道守卫, 舰队会突然停死且再也不会自己走。
        //      ⇒ 这里加"把行为拉回 OrderPortId/OrderPoint 对应目标"的分支: 只要玩家还锁着(PlayerHold)
        //        且任务还没结束(Task != 驻港), 就地重下同一条命令(SetMove* 是 public, 会重设 DefaultBehavior/
        //        TargetPosition/MoveTargetPoint/导航类型), 然后照常把 bestAiBehavior 交回原版链路。
        //        ★ 只在 `DefaultBehavior == Hold`(真被冻住)时重下命令, 避免每次 tick 都重设造成抖动。
        [HarmonyPatch(typeof(MobilePartyAi), "GetBehaviors")]
        internal static class LockBehavior
        {
            private static void Postfix(MobilePartyAi __instance,
                ref AiBehavior bestAiBehavior, ref IInteractablePoint behaviorObject, ref CampaignVec2 bestTargetPoint)
            {
                try
                {
                    var p = PartyOf(__instance);
                    if (p == null) return;
                    if (!NavalTaskForce.InCommand(p.StringId)) return;      // 只看登记在册的舰队
                    if (!NavalTaskForce.IsPlayerLocked(p)) return;          // 没锁定(已交还 AI) -> 不管

                    var def = p.DefaultBehavior;
                    // v4.278(第 7 项 ③): 被 SetMoveModeHold 冻住 -> 先按"最近一次命令"把命令重下一遍
                    if (def == AiBehavior.Hold)
                    {
                        if (!NavalTaskForce.RecoverFromHold(p)) return;     // 无需/无法恢复(例如正常驻港) -> 放行
                        def = p.DefaultBehavior;                            // 恢复成功后 DefaultBehavior 已是新值
                    }

                    // 只锁"我们命令设出来的"四种行为; None 等一律放行(那说明玩家没在指挥)
                    if (def != AiBehavior.GoToSettlement && def != AiBehavior.GoToPoint
                        && def != AiBehavior.PatrolAroundPoint && def != AiBehavior.EngageParty) return;

                    var targetParty = p.TargetParty;   // ★ 先取: 下面 GoToPoint 的恢复可能重置 TargetParty

                    bestAiBehavior = def;
                    bestTargetPoint = p.MoveTargetPoint;   // ★ v4.278: Point 模式的权威目标点(见本类注释 ①)
                    // GoToSettlement 需要 behaviorObject 有效, UpdateBehavior 才肯走 SetNavigationModePoint
                    //   (反编译: `(ShortTermBehavior == GoToSettlement || ...) && AiBehaviorPartyBase != null
                    //     && AiBehaviorPartyBase.IsValid`) —— 而 AiBehaviorPartyBase 由 SetShortTermBehavior
                    //   从 behaviorObject 更新, 所以这里必须一起给对。
                    if (def == AiBehavior.GoToSettlement)
                        behaviorObject = p.TargetSettlement != null ? p.TargetSettlement.Party : null;
                    else if (def == AiBehavior.EngageParty)
                        behaviorObject = targetParty != null ? targetParty.Party : null;   // v4.278(第 7 项 ②)
                    else
                        behaviorObject = null;
                }
                catch { }
            }
        }
        // ==================== 兜底 C(v4.277): 舰队水手军饷不由原版收 ====================
        //   与国防军的 `DefArmyPatches.DefArmyNoNativeWagePatch`(v4.252) **完全同源**, 理由也一样:
        //   原版按 `MobileParty.TotalWage` 从**氏族金库**扣部队工资, 而玩家氏族的金库就是
        //   Hero.MainHero.Gold = EconomyWorld.Treasury(反编译链: MobileParty.cs:1066 TotalWage
        //   -> DefaultClanFinanceModel.CalculatePartyWage(:941) -> AddPartyExpense(:826))。
        //   舰队的 ActualClan 被 FIA 设成 Clan.PlayerClan, 所以**原版本来就在为水手付工资**;
        //   若不同时掐断这一遍, FIA 新收的水手军饷(NavalTaskForce.SailorWages)就是同一批人收两遍钱。
        //   ★ 反过来也成立: 只有这个补丁生效, "舰队水手军饷"才是**唯一**一次收费 —— 两边口径必须成对存在。
        //   注意判据用 `IsTaskForce`(只认 fia_navy_ 前缀), **不动**任何其它部队(含 AI 领主舰队)。
        //
        //   ==================== v4.278(第 1 项): 补丁**注册**才是关键 ====================
        //   v4.277 写了这个类却忘了在 `SubModule.OnSubModuleLoad` 的补丁表里登记它 —— 等于没打。
        //   现在 SubModule.cs 里已登记(与国防军那条并列), 并且加了 `Prepare` 自检: 补丁没打上就在日志里喊出来。
        //
        //   ==================== ★★ Harmony 继承陷阱的定论(反编译实证) ★★ ====================
        //   审计员给了互相矛盾的结论, 这里一锤定音 —— **会触发**:
        //     · DLC 注册的是 `NavalDLC.GameComponents.NavalDLCPartyWageModel`(override `GetTotalWage`)。
        //     · 反编译 `NavalDLCPartyWageModel.decompiled.cs:31-33` 第一行就是
        //         `ExplainedNumber totalWage = ((MBGameModel<PartyWageModel>)this).BaseModel
        //              .GetTotalWage(mobileParty, troopRoster, includeDescriptions);`
        //       ⇒ 它在**内部转调基类**(不是"另写一套、完全不碰基类"), 所以打在
        //       `DefaultPartyWageModel.GetTotalWage` 上的 Postfix **一定会被执行**;
        //       我们的 Postfix 把 `__result` 整个换成 `new ExplainedNumber(0f)` 之后, DLC 后续那些
        //       `AddFactor`(-20% 海上折扣 / 水手长特长)都只是在 0 上做乘法 ⇒ 结果仍是 0。
        //     · 同理 `NavalDLCMobilePartyAIModel.ShouldPartyCheckInitiativeBehavior`(:88-90)也是
        //       `return BaseModel.ShouldPartyCheckInitiativeBehavior(...)`, 所以 NoInitiative 打在基类上也命中。
        //
        //   ==================== v4.278(第 3 项): "水手不是 Mariner"这条的核实结论 ====================
        //   审计说"`NavalDLCPartyWageModel.cs:51-54` 只把 `!character.IsMariner` 算成陆战部队工资,
        //   而 mod 的水手是从守备营抽的普通兵(NavalTaskForce.cs:347 -> DefArmy.MoveDefTroops),
        //   所以原版按**陆战部队**收费"。核实结果: **描述属实, 但修完第 1、2 项后它不再产生任何费用**。
        //     · 属实: 反编译 `NavalDLCPartyWageModel.GetTotalWage:42-54` 的那段循环只在
        //       `mobileParty.IsCurrentlyAtSea`(同文件 :55)里被使用, 而且**只用于两个特长折扣的分子**:
        //       `num`(陆战部队工资额)喂给 `NavalPerks.Boatswain.Optimization`,
        //       `num2`(骑乘部队工资额)喂给 `NavalPerks.Boatswain.NavalHorde`(:62-79)。
        //       mod 的三种基础兵(DefArmy.TroopsOf)都不是 DLC 的水手兵, `IsMariner == false`,
        //       所以它们**确实**会被算进那个 `num`。
        //     · 不再收费: 那一段是在 `BaseModel.GetTotalWage(...)` 的返回值上做 `AddFactor`,
        //       而我们的 NoNativeWage 已经把返回值整个换成 `new ExplainedNumber(0f)`;
        //       何况舰队司令是 `HeroCreator.CreateSpecialHero` 生成的特殊英雄(不点海军特长),
        //       `HasPerk(NavalPerks.Boatswain.Optimization)` 恒 false ⇒ 连 AddFactor 都不会发生。
        //       FIA 自己那份水手军饷(NavalTaskForce.SailorWages)按 **RegularsOf(正兵) × 0.5**
        //       收, 与 `IsMariner` 无关, 所以"是不是 Mariner"对账目没有任何影响。
        //     ⇒ 结论: **无需为此改任何东西**; 真正的钱漏在第 1 项(补丁没注册)与第 2 项(每天 5000 补足),
        //       两项修完后"原版对这支部队收 0 第纳尔"是确定的。
        //   ★ 既然如此, 为什么**还要**打 DLC 那个 override? 因为执行顺序不归我们管:
        //     Harmony 对"基类 Postfix"与"DLC override Postfix"没有固定先后。假如某天 DLC(或别的 mod)
        //     把 override 改成"不再转调基类", 基类补丁就静默失效; 反过来, 若 DLC 的 Postfix 先跑,
        //     它的 `totalWage.BaseNumber` 还会是我们清零后的 0(有 0 除风险)。
        //     所以这里做**双保险**: DLC 类型上再挂一个 Prefix 直接返回 false(跳过计算, 连 AddFactor 都不做),
        //     并把它记进日志。判据同样只用前缀, 对别的部队零影响。
        //     ★ DLC 类型走反射(NavalDLC.* 一律反射 —— 软依赖铁律), 所以这个类是**反射应用**的,
        //       不像其它补丁那样写进 SubModule 的补丁表(见 ApplyDlcWageGuard)。
        private const string BaseWagePatchName = "NoNativeWage";

        [HarmonyPatch(typeof(DefaultPartyWageModel), "GetTotalWage")]
        internal static class NoNativeWage
        {
            // v4.278: 自检 —— 补丁**真的挂上去了**才记这一行(替代"以为打了其实没打"的静默失败)
            private static void Prepare()
            {
                DLog.Force("海军: 补丁已应用 —— DefaultPartyWageModel.GetTotalWage 对 fia_navy_ 返回 0 工资"
                    + "(水手军饷改由 FIA 从国库收, 见 NavalTaskForce.SailorWages)");
            }

            private static void Postfix(MobileParty mobileParty, ref ExplainedNumber __result)
            {
                try
                {
                    if (mobileParty == null) return;
                    if (NavalTaskForce.IsTaskForce(mobileParty)) __result = new ExplainedNumber(0f);
                }
                catch { }
            }
        }

        // ==================== 兜底 D(v4.278 第 2 项): 原版不许给舰队"补足金库" ====================
        //   【为什么光掐工资还不够 —— 反编译实证的完整链条】
        //     `DefaultClanFinanceModel.AddPartyExpense(party, clan, goldChange, applyWithdrawals)`(:828-865):
        //         int num  = clan.Gold + (int)goldChange.ResultNumber;      // 玩家氏族: clan.Gold = Hero.MainHero.Gold
        //         int num2 = num;                                            // ★ PlayerClan **不**走"没钱就置 0"那条(它写了 clan != Clan.PlayerClan)
        //         int num3 = CalculatePartyWage(party, num2, applyWithdrawals);   // 工资(已被上面的补丁清零 -> 0)
        //         party.PartyTradeGold -= num3;                              // 从**队长英雄的金库**扣
        //         if (party.PartyTradeGold < PartyGoldLowerThreshold)        // ★ 阈值 = 5000(该类 :107)
        //         {
        //             int num4 = PartyGoldLowerThreshold - party.PartyTradeGold;   // = 5000 - 队长金库
        //             if (applyWithdrawals) { num4 = Min(num4, num2); party.PartyTradeGold += num4; }
        //             return -num4;                                          // ★ 负数 = 从氏族金库(国库)出这笔钱
        //         }
        //     而 `MobileParty.PartyTradeGold`(:428-449)对**领主部队**根本不是一个独立口袋, 它 get/set 的是
        //     `LeaderHero.Gold`。舰队队长的金库被 `Military\DefArmy.cs:1124` 显式设成 0
        //     (`hero.Gold = 0`, 国防军与舰队共用 `MakeGeneral`), 于是:
        //        工资=0 -> 队长金库仍 0 < 5000 -> num4 = 5000 -> 从国库扣 5000 补进队长的私人金库
        //     ⇒ **即使工资被清零, 原版每天仍然从玩家国库搬 5000/舰队 到舰队司令私人金库**
        //       (而司令是特殊英雄, 玩家看不到也拿不回; 舰队解散时他只是 ChangeState(Disabled), 那笔钱留在原地)。
        //   【堵法为什么选"Prefix 拦截"而不是"给队长留够金库"】
        //     ① 留金库(方案①)要求队长私人金库恒 ≥5000: 那是把 5000×舰队数**长期冻结在玩家看不见的地方**,
        //        而且 `AddIncomeFromParty`(:691-719)会在 PartyTradeGold > 10000 时每天抽走 10% 记成"部队贸易收入",
        //        反而生出新的鬼账 —— 治标不治本。
        //     ② 拦 `AddPartyExpense`(方案②)是在**源头**让"原版对 fia_navy_ 的这笔账"整条不存在:
        //        不扣工资、不补金库、不写 morale 惩罚, 返回值 0 ⇒ 对氏族金库净影响恰好 0。
        //     ③ Prefix 返回 false 而不是"Postfix 改返回值": 后者拦不住 —— `party.PartyTradeGold += num4`
        //        **已经写进去了**, 只改返回值等于"国库不扣钱但队长白拿 5000"(凭空印钱)。必须整方法跳过。
        //   【判据只认前缀】`NavalTaskForce.IsTaskForce` = `fia_navy_` 前缀 ⇒ **完全不碰**:
        //     · 原版 AI 领主部队(它们的 IsLordParty && LeaderHero != null, 走英雄自己的钱, 原版口径一字不变)
        //     · 国防军(DefArmy 前缀 fia_def_, 有它自己的 v4.252 处置, 本补丁看不见它)
        //     · 商队/驻军/玩家主队(前缀都不匹配)
        //   【DLC 侧】`NavalDLCClanFinanceModel`(:104-141)确实**又抄了一份同名私有 AddPartyExpense**,
        //     但它是 `private`(非重写): DLC 自己的 `CalculateClanGoldChange`(:21-38)只在**沿海卫戍法令**下
        //     给**驻军**调那一份, 而舰队工资走的是基类那条链(基类 CalculateClanGoldChange -> 基类私有方法)。
        //     ⇒ 打基类这一处即可全覆盖; 无需(也无法)给 DLC 的私有同名方法打补丁。
        //   【调用链自检】该方法只被 `AddExpensesFromPartiesAndGarrisons`(:721-801, 逐氏族部队/驻军)
        //     与 DLC 的驻军那一段调用; 两处都经 `Campaign.Current.Models.ClanFinanceModel`
        //     -> 基类实现 -> 本方法。签名 `(MobileParty, Clan, ExplainedNumber, bool)` 与 Prefix 参数名一一对应。
        [HarmonyPatch(typeof(DefaultClanFinanceModel), "AddPartyExpense")]
        internal static class NoNativePartyExpense
        {
            private static void Prepare()
            {
                DLog.Force("海军: 补丁已应用 —— DefaultClanFinanceModel.AddPartyExpense 对 fia_navy_ 整条跳过"
                    + "(否则每天从国库搬 5000/舰队 给司令补足 PartyTradeGold)");
            }

            private static bool Prefix(MobileParty party)
            {
                try
                {
                    if (party == null) return true;
                    // 命中 -> 整个方法跳过: 工资不扣、金库不补、返回值保持 default(0 = 对氏族金库零影响)
                    return !NavalTaskForce.IsTaskForce(party);
                }
                catch { return true; }
            }
        }

        // ==================== 兜底 C 的双保险: DLC override 上也挂一个 ====================
        //   见 NoNativeWage 的长注释: 基类那份**已经会**被 DLC 转调而触发, 这里只是防"将来 DLC 改了实现"
        //   + 防"两个 Postfix 的执行顺序让 DLC 在 0 上做除法"。DLC 类型一律反射(软依赖铁律)。
        internal static void ApplyDlcWageGuard(Harmony harmony)
        {
            try
            {
                if (harmony == null) return;
                var t = FindDlcType("NavalDLC.GameComponents.NavalDLCPartyWageModel");
                if (t == null)
                {
                    // ★ 这是**正常**情形之一, 不是错误: 不装 DLC 时必然找不到; 而"装了 DLC 但它的程序集
                    //   比我们晚加载"也会走到这里(所以 OnGameStart 时会再调一次, 见 SubModule)。
                    DLog.Force("海军: DLC 工资模型暂不可见(未装 DLC, 或 DLC 程序集尚未加载) —— 基类补丁已覆盖");
                    return;
                }
                var mi = AccessTools.Method(t, "GetTotalWage");
                if (mi == null) { DLog.Force("海军: DLC 工资模型无 GetTotalWage(版本变化?) —— 基类补丁仍生效"); return; }
                // 幂等: 本方法可能被调两次(OnSubModuleLoad + OnGameStart 兜底重试), 已经打过就不再打。
                //   用显式循环而不是 Linq 的 Contains —— 这个工程多数文件不带 System.Linq 的 using。
                try
                {
                    foreach (var pm in harmony.GetPatchedMethods())
                        if (ReferenceEquals(pm, mi)) return;
                }
                catch { }
                var prefix = new HarmonyMethod(typeof(NavalTaskForceAi).GetMethod("DlcWagePrefix",
                    BindingFlags.NonPublic | BindingFlags.Static));
                harmony.Patch(mi, prefix);
                DLog.Force("海军: 补丁已应用 —— " + t.FullName + ".GetTotalWage 对 fia_navy_ 直接跳过(双保险)");
            }
            catch (Exception ex) { DLog.Force("海军: DLC 工资双保险补丁失败(基类补丁仍生效): " + ex.Message); }
        }

        // DLC 类型查找 —— 与 NavalBridge.DlcLoaded 同一手法(先按 "类型, 程序集" 找, 再遍历已加载程序集),
        //   绝不写 `typeof(NavalDLC.*)`(软依赖铁律: 不引用 DLC 类型, 它就永远不会带来 TypeLoadException)。
        private static Type FindDlcType(string fullName)
        {
            try
            {
                var t = Type.GetType(fullName + ", NavalDLC", false);
                if (t != null) return t;
                var asms = AppDomain.CurrentDomain.GetAssemblies();
                for (int i = 0; i < asms.Length; i++)
                {
                    var a = asms[i];
                    if (a == null || a.GetName().Name != "NavalDLC") continue;
                    t = a.GetType(fullName, false);
                    if (t != null) return t;
                }
            }
            catch { }
            return null;
        }

        // DLC override 的 Prefix —— 与 NoNativeWage 同一判据(只认 fia_navy_ 前缀)
        private static bool DlcWagePrefix(MobileParty mobileParty)
        {
            try
            {
                if (mobileParty == null) return true;
                return !NavalTaskForce.IsTaskForce(mobileParty);
            }
            catch { return true; }
        }
    }
}
