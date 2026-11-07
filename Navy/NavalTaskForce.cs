using System;
using System.Collections.Generic;
using System.Text;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Naval;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace FeudalInternalAffairs
{
    // ==================== v4.276: 海军舰队实体化(用户: "像操控国防军一样操控海军") ====================
    //
    // 【要解决的问题】v4.275 及以前, 海军是一张**纯战略面板**: 船挂在氏族名下, 只能在海军页里
    //   抽象地买/造/封锁/海运 —— 玩家不能在地图上点选它、不能指挥它开去哪里。
    //   本文件把舰队做成**地图上的实体部队**(MobileParty), 于是"点选 / 框选 / 左键点地图让它开过去 /
    //   右键下命令"全部走已有的国防军那套指挥链(MapSelection + NationalWillOrders + MapBoxSelect)。
    //
    // 【为什么这是合法的原版状态, 而不是自造的非法状态】(反编译实证, 全部走基础游戏 public API)
    //   · 部队生成: `LordPartyComponent.CreateLordParty(id, hero, position, radius, spawnSettlement, leader)`
    //     -> `MobileParty.CreateParty` -> `LordPartyComponent.OnMobilePartySetOnCreation`
    //     -> `InitializationArgs.InitializeLordPartyProperties`:
    //          if (mobileParty == MainParty || owner.Clan == Clan.PlayerClan)
    //              mobileParty.InitializeMobilePartyAtPosition(Position);      // ★ 单参重载
    //       而 `MobileParty.InitializeMobilePartyAtPosition(CampaignVec2)` 的实现是
    //          `IsCurrentlyAtSea = !position.IsOnLand; CreateFigure(position); SetMoveModeHold();`
    //       并且 `Settlement.PortPosition` 在反序列化时就是 `new CampaignVec2(pos, isOnLand: false)`
    //       => **只要把出生点给成港口的海面点, 这支部队一出生就是合法的海上部队**。
    //   · 船的归属: `ChangeShipOwnerAction.ApplyByTransferring(PartyBase newOwner, Ship ship)`
    //       (基础游戏 Action; DLC 自己的海上巡逻队/氏族舰队管理也用它 ——
    //        NavalPatrolPartiesCampaignBehavior.cs:70 / ClanFleetManagementCampaignBehavior.cs:159)
    //   · 海上能力判据: DLC `NavalPartyNavigationModel.HasNavalNavigationCapability(p)`
    //       的实现就是 `p.Ships.Count > 0`(或挂靠/被挂靠方有船) => 过户完船, 原版眼里它就是海上部队。
    //   · `LordPartyComponent.CanHaveNavalNavigationCapability => true` —— 领主部队本来就允许有海上能力。
    //   因此本文件**一个 NavalDLC.* 类型都不引用**(软依赖铁律, 见 Map\NavalVisualGuard.cs 的范式),
    //   DLC 缺失时 `NavalBridge.Usable == false`, 整个舰队层静默降级。
    //
    // 【与海运(NavalTransport)的关系 —— 船源互斥】
    //   海运是"把船**借给**一支陆军军团, 运到目的港再还船"; 舰队是"船**归**舰队所有"。
    //   两者都要过户船, 所以必须互斥, 判据见 `ShipsBlockedByFleet()`:
    //   已经被舰队占用的船不会出现在海运的候选池里(见 NavalTransport.CargoShips 的 v4.276 注释)。
    //
    // 【"AI 不接管"与"必须能走"这两条为什么能同时成立】(详见 Navy\NavalTaskForceAi.cs)
    //   · 必须能走: 全游戏唯一把 `MobileParty.PartyMoveMode` 置成 `Point` 的入口是
    //       MobilePartyAi.TickInternal -> GetBehaviors -> SetAiBehavior -> UpdateBehavior
    //       -> MobileParty.SetNavigationModePoint(...)     (UpdateBehavior / SetNavigationMode* 都是 internal)
    //     所以必须在 `DefArmyAi.AiGuard` 里**放行**舰队的 TickInternal(与海运中的军团同一条判据链)。
    //   · AI 不接管: 放行 TickInternal 之后, `GetBehaviors` 的起点是 `DefaultBehavior`(= 我们设的
    //     GoToSettlement/GoToPoint/PatrolAroundPoint), 但它**还会**算一遍"主动行为"
    //     (打劫匪/追敌人/躲风暴)并可能覆盖掉 DefaultBehavior —— 判据是
    //     `MobilePartyAIModel.ShouldPartyCheckInitiativeBehavior(p)`, 而默认实现
    //     (DefaultMobilePartyAIModel.cs:88)对"非 MainParty 的独立部队"一律返回 true ⇒ 舰队会被接管。
    //     因此 `NavalTaskForceAi` 挂两个补丁:
    //       ① Postfix `DefaultMobilePartyAIModel.ShouldPartyCheckInitiativeBehavior` -> 舰队返回 false
    //          (DLC 的 NavalDLCMobilePartyAIModel 是 `return BaseModel.ShouldPartyCheckInitiativeBehavior(p)`,
    //           所以打到基础模型的这个方法上, 有无 DLC 都命中)
    //       ② Postfix `MobilePartyAi.GetBehaviors` -> 只要我们处于"玩家命令锁定"(PlayerHold),
    //          就把 AI 选出的行为**强制拉回 DefaultBehavior**, 并把 behaviorObject 一并修正成
    //          对应聚落/为空(否则 `UpdateBehavior` 里 `AiBehaviorPartyBase.IsValid` 判定会指向错目标)。
    //     于是"谁决定去哪"完全归玩家命令, "怎么走"仍由原版 AI 放出 PartyMoveMode —— 两条同时满足。
    internal static class NavalTaskForce
    {
        internal const string Prefix = "fia_navy_";
        internal const int MaxForces = 4;              // 同时最多 4 支舰队(与海运 MaxConvoys 同量级)
        internal const int MinShipsToFound = 1;        // 至少 1 艘船才能编成舰队
        internal const int KeepShipsInClan = 1;        // ★ 必须至少留 1 艘在氏族手里(否则海运/买船被搬空)

        // v4.276: "AI 不接管"的**主开关** —— 直接用原版自己的 `MobilePartyAi.DoNotMakeNewDecisions`。
        //   反编译实证(TaleWorlds.CampaignSystem 全量反编译, 读取点只有 4 处, 没有一处是 native):
        //     · MobilePartyAi.GetBehaviors(104746): 不采纳"主动行为"(打劫匪/追敌人/躲风暴), 但仍然
        //       以 `DefaultBehavior` 为起点继续走 switch 细化 -> SetAiBehavior -> UpdateBehavior
        //       -> SetNavigationModePoint  ⇒ **推进链路完好, 只是不许它自己改主意**;
        //     · AiHourlyTickBehavior.PartyHourlyAiTick(221345): 直接 return ⇒ 原版 AI 的"派单"
        //       (SandBox 那套给 AI 领主打分选目标的系统)根本不会作用到舰队上;
        //     · 另两处是部队贸易与召集军团, 与移动无关。
        //   历史上 v4.90 曾怀疑该标志会冻住移动, 但那次是与 `SetMoveModeHold()` 同时调的 ——
        //   Hold 才是"原地不动"的真正原因(UpdateBehavior 走 SetNavigationModeHold 分支)。
        //   若将来实测发现本标志确实有副作用, 把它改成 false 即退回"只靠 NavalTaskForceAi 兜底"。
        internal const bool UseNativeNoDecision = true;

        // ==================== 舰队记录 ====================
        internal class Force
        {
            internal string PartyId = "";
            internal string HomePortId = "";
            internal string HomePortName = "";
            internal int FoundedDay = 0;
            internal string Task = TaskStation;        // 驻港/巡航/远征/封锁/交战
            internal bool PlayerHold = true;           // ★ 玩家命令锁定(= "AI 不接管"的总开关)
            internal string OrderPortId = "";          // 当前命令的目标港("" = 无)
            internal int OrderDay = 0;
            // v4.277(第 4 项 诊断): 最近一次命令的"形状" —— 光有 OrderPortId 看不出这是"海面点/交战"命令,
            //   诊断日志要把"最近一次命令的目标"完整打出来, 所以补这两项(见 DiagLog)。
            internal string OrderKind = "";            // 见 OrderKind* 常量
            internal string OrderTarget = "";          // 目标港名 / "海面点(x,y)" / 目标部队名
            internal int UnpaidDays;                   // v4.277(第 1 项): 本舰队连续欠饷天数(不落盘, 只做警告/哗变计时)
            internal float LastDist = -1f;             // v4.277(第 4 项 诊断): 上一次采样的"距目标距离"(<0 = 还没采过; 不落盘)
            // v4.278(第 6 项): 命令的**可重放信息** —— 终态同步(TaskSync)与"被 SetMoveModeHold 冻住后的恢复"
            //   (RecoverFromHold)都要能在**不依赖 DefaultBehavior** 的前提下把命令重下一遍, 所以必须自己记:
            //   · OrderSettlementId = 命令目标港(GoToSettlement / PatrolAroundSettlement 用)
            //   · OrderPointX/Y     = 海面点(GoToPoint 用; 单独存 X/Y 免得为 Vec2 引入存档格式争议)
            internal string OrderSettlementId = "";
            internal float OrderPointX = 0f;
            internal float OrderPointY = 0f;
            internal bool HasOrderPoint = false;
            // v4.278(第 7 项 ③ 补充): 交战命令的**目标部队 id**。
            //   为什么非要单独记不可: `SetMoveModeHold()`(:ResetAllMovementParameters:4270-4278)会把
            //   `TargetParty` 一起清成 null —— 于是"被冻住之后"再想重下交战命令, 就再也找不到打谁了。
            //   记下 id 后, RecoverFromHold / TaskSync 都能从 `MobileParty.Find` 把目标找回来。
            internal string OrderTargetPartyId = "";
            // ==================== v4.282: 驻泊(进驻城市) ====================
            //   【与母港的区别 —— 两个字段各管一件事, 不再混用】
            //     · HomePortId/HomePortName = 母港: 归属港(组建地/指定地), [回港] 的目标, 解散时船回这里;
            //     · MooredPortId/MooredPortName = 驻泊港: 舰队**当前停泊在哪座城市的港口里**。
            //       进驻时两者会被一并设成同一座城(进驻必然以该城为母港, 语义最直白), 但**离港只清驻泊**,
            //       母港保留 —— 这样"离港后舰队仍然知道该回哪去"。
            //   【为什么要有它】用户要求"海军可以驻扎在城市内(仅沿海城市)", 并且进驻要有实际意义:
            //     驻泊(已到位)时 ①船维护 -35% ②每日按 DLC 修船价从国库付费修理(见 MooredRepairDaily)。
            internal string MooredPortId = "";
            internal string MooredPortName = "";
            internal int MoorDay = 0;                  // 进驻那天(面板显示"已驻泊 N 天"; 老档 = 0)
            internal bool HasMoor { get { return !string.IsNullOrEmpty(MooredPortId); } }
            internal readonly Dictionary<int, string> ShipOwner = new Dictionary<int, string>();
            // 船被击沉后从账上划掉(见 OnShipDestroyed), 免得解散时"还船数"永远对不上
            internal void ForgetShip(int key) { try { ShipOwner.Remove(key); } catch { } }

            internal string Line
            {
                get
                {
                    // v4.282: 驻泊态的 Task 自身已带城名("驻泊于「X」"), 末尾不再重复一遍驻泊港
                    return Task + (PlayerHold ? "" : "(自主)")
                        + " · 母港 " + (string.IsNullOrEmpty(HomePortName) ? "?" : HomePortName)
                        + (HasMoor && !IsMoorTask(Task) ? " · 驻泊 " + (string.IsNullOrEmpty(MooredPortName) ? "?" : MooredPortName) : "");
                }
            }
        }

        internal static readonly List<Force> Forces = new List<Force>();

        internal const string TaskStation = "驻港";
        internal const string TaskPatrol = "巡航";
        internal const string TaskVoyage = "远征";
        internal const string TaskBlockade = "封锁";
        internal const string TaskEngage = "交战";
        // v4.278(第 4 项): 交还海军部(自主巡航)时的任务名 —— 玩家不再指挥, 由原版 AI 自己决定去哪。
        //   单独一个值而**不是**复用 TaskStation: 面板要如实显示"这支队现在不归我管"(见 Force.Line 的用法)。
        internal const string TaskAuto = "自主";

        // ==================== v4.282: 进驻城市(驻泊)的任务名 ====================
        //   TaskMooring = 已下令进驻但还在路上(与"远征"同类, 但目的地是驻泊港);
        //   驻泊到位后的 Task 用 **带城名的动态串** "驻泊于「XX」" —— 理由: 地图小窗(UI\PartyInfoHud.cs)
        //   只会把 `Force.Task` 原样显示在标题上("XX · 舰队 · <这里>"), 而显示层不在本次可改文件内;
        //   把城名编进任务名, "驻泊于哪座城"在**面板、小窗、右键菜单**三处就都能一眼看到。
        //   ★ 因此判断"是否驻泊"必须用 IsMoorTask(前缀判据), **不能**再用 `== TaskMoor`。
        //   (核实过: 全仓读 Navy 的 `Force.Task` 只有本文件与 UI\PartyInfoHud.cs:181 的显示,
        //    没有任何按等值分流的逻辑会被动态串破坏 —— 见 TaskSyncOne 的 ⓪ 分支。)
        internal const string TaskMoor = "驻泊";          // 任务名前缀(不要直接与 f.Task 做 == 比较)
        internal const string TaskMooring = "进驻中";

        internal static bool IsMoorTask(string task)
        {
            try { return !string.IsNullOrEmpty(task) && task.StartsWith(TaskMoor, StringComparison.Ordinal); }
            catch { return false; }
        }

        internal static string MoorTaskText(string portName)
        {
            return TaskMoor + "于「" + (string.IsNullOrEmpty(portName) ? "?" : portName) + "」";
        }

        // ==================== v4.282: 进驻的收益(两项, 全部走既有结算口径) ====================
        //   ① 船维护 -35%: 停在自己港口的泊位上, 不用烧淡水/耗索具/雇临时工。
        //      落点 = `NavalFleet.DailyUpkeep`(日结唯一收船维护费的地方, 见 NavalBehavior.UpkeepDaily),
        //      以及面板/小窗的同一口径显示(NavalTaskForce.Stats / NavyPanelVM 的行内"维护"列)。
        //   ② 每日付费修船(修到满): 每支驻泊舰队每天最多修 `1 + 船坞等级/2` 艘(最破的先修),
        //      每艘按 **DLC 自己的修船价** `ShipCostModel.GetShipRepairCost` 从国库扣钱, 走 Fiscal.Navy 科目。
        //      【为什么不与 DLC 的 ShipRepairCampaignBehavior 重复】(反编译实证, 本机 NavalDLC.dll)
        //        · 该行为只在 `AfterSettlementEntered`(部队**进入**聚落)时修 AI 领主部队:
        //            `RepairLordPartyShips`: 要求 `mobileParty.PartyTradeGold > GetShipRepairCost(...)`
        //            才 `RepairShipAction.Apply(ship, settlement)`(钱从该部队自己的贸易金出)。
        //        · 而我们的舰队是 `LordPartyComponent` 建的领主部队, 队长由 `DefArmy.MakeGeneral` 生成,
        //          `Military\DefArmy.cs:1124` 明确 `hero.Gold = 0`; 领主部队的 `PartyTradeGold`
        //          get/set 的就是 `LeaderHero.Gold` ⇒ 恒为 0;
        //          何况 `NavalTaskForceAi.NoNativePartyExpense` 已经把原版"给部队补足 5000 金库"那条链
        //          整段跳过(否则每天从国库搬 5000/舰队) ⇒ **DLC 那条路对我们的舰队永远不会触发**。
        //        · 我们的舰队也不会"进入聚落": 海上部队停在 `Settlement.PortPosition`(海面点),
        //          `AfterSettlementEntered` 不响。⇒ 本项是**唯一**给舰队的修理, 不重复。
        //      【钱走我们自己的国库】`RepairShipAction.Apply` 会把钱付给聚落(GiveGoldAction), 而舰队
        //        口袋里没有钱; 所以这里用 `ApplyForFree`(原版 Action, 会正常发 OnShipRepaired 事件 +
        //        给船长记技能经验), 再自己从 `EconomyWorld.Treasury` 扣同额的钱 —— 账目一致、事件链完整。
        internal const float MooredUpkeepMul = 0.65f;        // 驻泊船维护乘数(-35%)
        internal const int MoorRepairYardDiv = 2;            // 每日可修船数 = 1 + 船坞等级 / 此值
        internal const int MoorRepairShipCap = 3;            // 每日上限(防船坞等级异常时一次修爆)

        // 最近一次驻泊修理的概况(面板显示用; **不落盘** —— 与 NavalFleet.LastUpkeepPaid 同性质)
        internal static int LastMoorRepairShips = 0;
        internal static int LastMoorRepairCost = 0;

        // ==================== v4.284(第 4 项): [海上演训] 与 [整备] 的口径常量 ====================
        //   海上演训提升的是**舰队士气**(MobileParty.Morale, 通道与落点见 SeaDrill 的长注释) ——
        //   报价 = 每名水手 SeaDrillPerSailor 第纳尔, 最低 SeaDrillMinCost, 取整到 10。
        //   为什么按水手数而不是固定价: 500 人的舰队搞一次演训不可能和 20 人同价; 量级参照陆军[阅兵 1500]
        //   (200 名水手 ≈ 1200, 与阅兵同一档), 且"人越多越贵"本身就让玩家有得算。
        internal const int SeaDrillPerSailor = 6;
        internal const int SeaDrillMinCost = 400;
        internal const float SeaDrillMoraleGain = 12f;   // 单次每支舰队的士气增幅(原版上限 100)
        internal const float MoraleCap = 100f;           // 原版 MobileParty.Morale 的上限口径

        // 最近一次[整备]的概况(面板显示用; **不落盘**, 与 LastMoorRepairShips 同性质)
        internal static int LastOverhaulShips = 0;
        internal static int LastOverhaulCost = 0;

        // v4.278(第 6 项): 命令超时 —— 命令下达超过这么多游戏日仍未到达 -> 判定远征失败, 自动召回母港
        //   依据(与海运同源): `NavalTransport` 的超时口径是"3×ETA + 3 天"。舰队没有 ETA 预估,
        //   取一个宽松值 30 天(≈ 1 个月): 足够绕半张地图, 又不至于让玩家等一整个季度才看到结论。
        internal const int OrderTimeoutDays = 30;

        // v4.278(第 6 项): "已到位"的判定半径(直线距离)。港口海面点本身是一个点, 但原版导航会在
        //   最后一段做靠泊/绕行, 所以留 8 的余量(与 DiagLogOne 里"距目标 ≤20 视为到位"的诊断口径同量级,
        //   这里更严一点, 避免"还差 15 米就宣告到达"然后在面板上写驻港却还在挪)。
        internal const float ArriveRadius = 8f;

        // ==================== v4.278(第 9 项): 出击(面板[出击])的冷却与费用 ====================
        //   【为什么要有冷却 —— 反编译/代码实证的闭环】
        //     `UI\NavyPanelVM.Sortie()` 每次都无条件走 `NavalBattle.Resolve(...)`(抽象结算),
        //     而 `NavalBattle.cs:152` 的战利品是 `worth × enLossRatio × 0.25 + **保底 300** + 敌阵亡×6`,
        //     且 :155 直接 `TreasuryAdd(r.Loot)` —— 点一次按钮就进账, 没有任何时间成本。
        //     于是"反复点出击"是一条**无本套利**闭环: 每次至少 300, 点 100 次就是 30000。
        //   【冷却时长 = 6 游戏小时】依据(三条, 都不是拍脑袋):
        //     ① 原版 AI 的决策周期就是小时级(`AiPartyThinkBehavior.DefaultThinkingPeriodInHours = 6`,
        //        反编译实证), 也就是"一场海上遭遇 -> 重新评估态势"的自然节奏就是 6 小时;
        //     ② 仓库里同级别的"战略动作"冷却都量在**天**上(和平冷却按天、海运按 3×ETA+3 天),
        //        出击是战术动作, 取 6 小时 = 一天最多 4 次, 既不堵死玩法, 也彻底断掉"连点刷钱";
        //     ③ 6 小时在时间加速下是"一点就到"的尺度, 玩家体验上不会觉得被卡。
        internal const double SortieCooldownHours = 6.0;
        //   【保底口径: 按舰数收"出击补给费", 让保底不再是净赚】
        //     保底 300 那行在 `Navy\NavalBattle.cs`(不归本文件改), 所以这里用**同一件事的另一半**把它抵掉:
        //     出海作战要烧补给/弹药/淡水, 费用 = 每艘参战舰 100 第纳尔(口径 = 与"封锁调动费 500/次"
        //     同量级的一次性开销; 舰越多, 一趟的消耗越大 —— 这比固定值更符合直觉, 也让小船队不至于被罚死)。
        //     ⇒ 1 艘 = 100、3 艘 = 300(**恰好抹平保底**)、5 艘 = 500。也就是说:
        //       "点一下就白拿 300" 变成 "至少要打赢才回本"; 真实战果(沉船/歼敌)照旧全额进国库。
        internal const int SortieSupplyPerShip = 100;

        // 出击冷却的时间戳(游戏内秒; <0 = 从未出击过)。**不落盘**: 读档后允许立刻出击一次,
        //   这是有意的 —— 存档读档本身要过载入画面, 不可能被用来刷钱(与"防重"性质的 _lastDay 不同)。
        private static double _sortieReadySec = -1.0;

        // v4.290 修复: 出击冷却的绝对时间戳(游戏秒)原先**不落盘** —— 读档链是
        //   NavalBehavior.SyncData(IsLoading) -> NavalFleet.Load() -> 第一行无条件 Reset()
        //   -> NavalTaskForce.Reset() -> 这里被清成 -1, 于是"出击 → 存档 → 读档 → 出击"
        //   可以无限重复刷钱(每次只付 SortieSupplyCost, 冷却完全失效)。
        //   现在由 NavalFleet 存档段的 srt= 键持久化; Load 里 Reset 之后再恢复, 顺序正确。
        internal static double SortieReadySecRaw
        {
            get { return _sortieReadySec; }
            set { _sortieReadySec = value; }
        }

        // 还有多少游戏小时才能再次出击(0 = 现在就可以)。面板可用它做按钮提示/灰化。
        internal static double SortieCooldownLeftHours()
        {
            try
            {
                if (_sortieReadySec < 0.0) return 0.0;
                double now = CampaignTime.Now.ToSeconds;
                double gap = (_sortieReadySec - now) / (double)CampaignTime.HoursInDay;
                return gap > 0.0 ? gap : 0.0;
            }
            catch { return 0.0; }
        }

        // 出击前的费用: 按**当前参战舰数**算(见 SortieSupplyPerShip 注释)。
        internal static int SortieSupplyCost()
        {
            try
            {
                int n = 0;
                try { n = NavalFleet.MyShips().Count; } catch { }
                if (n < 1) n = 1;
                return n * SortieSupplyPerShip;
            }
            catch { return SortieSupplyPerShip; }
        }

        // 【UI 接线点】出击的统一入口: 冷却检查 + 收补给费 + 登记冷却。返回 "" = 可以出击(钱已扣),
        //   否则返回**中文原因**(面板直接把它显示在状态栏即可)。
        //   调用方约定(给 UI 那位): 在 `NavyPanelVM.Sortie()` 开头插
        //       string e = NavalTaskForce.TryConsumeSortie();
        //       if (!string.IsNullOrEmpty(e)) { _status = e; return; }
        //   未接线时的行为: 冷却与费用都不生效(功能不会坏), 但也就没有防刷钱 —— 所以**必须**接线。
        internal static string TryConsumeSortie()
        {
            try
            {
                double left = SortieCooldownLeftHours();
                if (left > 0.01)
                    return "舰队正在补给/整备, 还需 " + (left >= 1.0 ? ((int)Math.Ceiling(left)).ToString() + " 小时"
                        : Math.Max(1, (int)Math.Round(left * 60.0)).ToString() + " 分钟") + "才能再次出击";
                int cost = SortieSupplyCost();
                if (EconomyWorld.Treasury.Gold < cost)
                    return "国库不足(出击补给需 " + cost + " 第纳尔)";
                if (cost > 0) { EconomyWorld.TreasurySpend(cost); Fiscal.AddCourtPaid(cost); }
                _sortieReadySec = CampaignTime.Now.ToSeconds + SortieCooldownHours * (double)CampaignTime.HoursInDay;
                DLog.Force("海军: 出击 —— 补给费 " + cost + " 第纳尔, 下次可出击需等 " + SortieCooldownHours + " 游戏小时");
                return "";
            }
            catch (Exception ex) { DLog.Force("海军: 出击冷却检查异常(放行): " + ex.Message); return ""; }
        }

        // ==================== v4.278(第 9 项): 抹掉"保底 300"的**第二半** ====================
        //   保底那行在 `Navy\NavalBattle.cs:152`(`worth × enLossRatio × 0.25f + **300f** + 敌阵亡 × 6`),
        //   该文件不归本次改动, 所以这里用"如实记账"的办法把它归零:
        //   出击结算后调一次本函数, 把"不足 300 的部分"从国库收回来。效果:
        //     实际收益 = Loot(显示值, 战报里照旧) − 补给费(见上) − 保底补收(max(0, 300 − Loot))
        //     ⇒ 净收益 == **真实战果**(沉船/歼敌折算), 与投入相称; Loot ≥ 300 时补收为 0, 一分不多扣。
        //   为什么不直接把 NavalBattle 里那个 300f 删掉: 那需要改别人的文件; 而本做法还有个副作用是**对的** ——
        //   它在账面上把"凭空印出来的 300"标出来了(否则这部分钱在财报里完全无源)。
        //   【UI 接线(一行)】在 `NavyPanelVM.Sortie()` 的 `var r = NavalBattle.Resolve(k, enemy, target);` 之后插:
        //       NavalTaskForce.SortieFloorAdjust(r != null ? r.Loot : 0);
        internal const int SortieLootFloor = 300;   // 与 NavalBattle.cs:152 的保底值保持同步(改那边要同步这里)

        internal static int SortieFloorAdjust(int actualLoot)
        {
            try
            {
                int cut = SortieLootFloor - actualLoot;
                if (cut <= 0) return 0;
                int gold = EconomyWorld.Treasury.Gold;
                if (cut > gold) cut = Math.Max(0, gold);      // 国库不够就只收到能收的(不会把国库弄成负数)
                if (cut <= 0) return 0;
                EconomyWorld.TreasurySpend(cut);
                Fiscal.AddCourtPaid(cut);
                DLog.Force("海军: 出击保底回冲 " + cut + " 第纳尔(战利品 " + actualLoot + " < 保底 " + SortieLootFloor + ")");
                return cut;
            }
            catch { return 0; }
        }

        // ==================== v4.277(第 1 项): 水手军饷 ====================
        //   【为什么必须收, 而且必须"摆在明处"】
        //   · DefArmy.DailyWageCost() 是按 DefArmy.Legions 求和的, **舰队不在那张登记表里**
        //     => 舰队水手的军饷原来没人收(只有船维护走 NavalFleet.DailyUpkeep/NavalFleet.LastUpkeepPaid),
        //        舰队是一支免费战力。本项把它接进日结。
        //   · 【★ 关键: 原版已经在收这笔钱】(反编译实证, 本机 TaleWorlds.CampaignSystem.dll)
        //       MobileParty.TotalWage(MobileParty.cs:1066) -> PartyWageModel.GetTotalWage
        //       -> DefaultClanFinanceModel.CalculatePartyWage(DefaultClanFinanceModel.cs:941-953) 读 TotalWage
        //       -> AddPartyExpense(DefaultClanFinanceModel.cs:826-...) 把它计入氏族金库支出
        //       而玩家氏族的金库就是 Hero.MainHero.Gold = EconomyWorld.Treasury(见 EconomyWorld.TreasuryAdd 注释)。
        //     舰队的 ActualClan = Clan.PlayerClan(FIA 自己设的), 所以**水手军饷原版已经扣过一次**。
        //     这与国防军 v4.252 的处置完全同源(见 DefArmyPatches.DefArmyNoNativeWagePatch 的长注释):
        //     若不掐断原版那一遍, FIA 再收一次就是"同一批人收两遍钱"(隐性双扣)。
        //   => 因此本项成对做两件事(缺一不可):
        //        ① NavalTaskForceAi.NoNativeWage: 原版对 fia_navy_ 部队算工资返回 0(v4.252 同款补丁);
        //        ② SailorWages(): 由 FIA 按**与陆军完全相同的口径**从国库收一次, 并记进 Fiscal 军费科目。
        //      ★ v4.278(第 1 项 修正): ①原来**根本没注册**(SubModule 的补丁表漏了它) —— 已补;
        //        并且 ① 只管工资, 不管 AddPartyExpense 的"金库补足", 后者由 NoNativePartyExpense 单独堵
        //        (见 NavalTaskForceAi 的两个补丁类)。现在"原版收 0、FIA 收一份"才是真的成立。
        //   【水手是不是 DLC 的 Mariner】不是(DefArmy.TroopsOf 抽的是 mod 自己的三种基础兵,
        //     XML 里没有 DLC 的水手标记)。核实结论: 不影响账目 —— 见 NavalTaskForceAi 第 3 项的长注释。
        internal const float SailorWagePerManPerDay = 0.5f;   // 与 DefArmy.WagePerManPerDay 同值(见 WageRate() 的取值顺序)
        internal const int SailorMutinyWarnDay = 7;           // 欠饷几天开始警告(与陆军一致)
        internal const int SailorMutinyDay = 14;              // 欠饷几天哗变(与陆军一致)

        // ==================== v4.277(第 4 项): 舰队移动诊断 ====================
        //   flags 文件(Documents\Mount and Blade II Bannerlord\Configs\FeudalInternalAffairs_flags.txt)里加:
        //     navydiag           = 打开诊断(默认关闭)
        //     navydiagsec=3600   = 间隔(游戏内秒, 缺省 3600 = 1 游戏日; 到小时结时才会真正判定一次)
        //   日志走 DLog.Force, 前缀 "舰队诊断:" 便于在日志里过滤。
        internal const string DiagFlag = "navydiag";
        internal const string DiagSecKey = "navydiagsec";
        internal const float DiagSecDefault = 3600f;

        // v4.277(第 4 项): 命令类别 —— 诊断日志要能说清"最近一次命令是哪种"
        internal const string OrderKindRecall = "回港";
        internal const string OrderKindPort = "前往港口";
        internal const string OrderKindBlockade = "封锁";
        internal const string OrderKindPoint = "海面点";
        internal const string OrderKindPatrol = "巡航";
        internal const string OrderKindEngage = "交战";
        // v4.282: 进驻命令(被原版 SetMoveModeHold 冻住时按这个恢复成"开往驻泊港")
        internal const string OrderKindMoor = "进驻";

        // v4.277(第 2 项): 战报来源标签 —— 面板[出击]的**抽象结算**与地图上**真实打的海战**必须分得清,
        //   否则玩家会以为在面板上点一下就真打过了。两个标签分别由 NavalBattle / NavalMapBattle 写入。
        internal const string SourceSim = "推演";
        internal const string SourceReal = "实战";

        // ==================== 判定 ====================
        internal static bool IsTaskForce(MobileParty p)
        {
            try
            {
                if (p == null || p.StringId == null) return false;
                return p.StringId.StartsWith(Prefix, StringComparison.Ordinal);
            }
            catch { return false; }
        }

        internal static Force ForceOf(MobileParty p)
        {
            try
            {
                if (p == null) return null;
                for (int i = 0; i < Forces.Count; i++)
                    if (Forces[i] != null && Forces[i].PartyId == p.StringId) return Forces[i];
            }
            catch { }
            return null;
        }

        internal static Force ForceOf(string partyId)
        {
            try
            {
                if (string.IsNullOrEmpty(partyId)) return null;
                for (int i = 0; i < Forces.Count; i++)
                    if (Forces[i] != null && Forces[i].PartyId == partyId) return Forces[i];
            }
            catch { }
            return null;
        }

        internal static MobileParty PartyOf(Force f)
        {
            try { return f != null ? DefArmy.FindParty(f.PartyId) : null; }
            catch { return null; }
        }

        // 我方全部健在的舰队(按 PartyId 稳定序, 免得 UI 每次刷新顺序都在跳)
        internal static List<Force> Alive()
        {
            var list = new List<Force>();
            try
            {
                for (int i = 0; i < Forces.Count; i++)
                {
                    var f = Forces[i];
                    if (f == null) continue;
                    var p = PartyOf(f);
                    if (p == null || !p.IsActive) continue;
                    list.Add(f);
                }
                list.Sort(delegate (Force a, Force b) { return string.CompareOrdinal(a.PartyId, b.PartyId); });
            }
            catch { }
            return list;
        }

        // ★★ "必须能走"的放行判据(DefArmyAi.AiGuard 调用它) ★★
        //   语义 = "这支部队的移动由玩家命令 + 原版导航链驱动, 请放行 TickInternal 让它把
        //   PartyMoveMode 置成 Point"; 与 NavalTransport.InConvoy 同一层级、互不重叠。
        //
        //   ==================== v4.278(第 8 项): 与 IsTaskForce 对齐 ★ ====================
        //   【原缺陷】原来要求 `ForceOf(partyId) != null`(必须"登记在册")才放行, 而
        //   `MapSelection` 的地图提示/右键菜单判据是 `IsTaskForce`(**只看 fia_navy_ 前缀**)。
        //   两个判据不一致的后果: 存档 F= 段坏掉被逐段容错跳过后, 地图上确实有一支 fia_navy_ 部队,
        //   玩家**看得见、点得着、还能对它下命令**(UI 认前缀), 但它既不放行 TickInternal
        //   (InCommand=false ⇒ DefArmyAi.AiGuard 不放行)又没人给它记账 ⇒ "看得见、指挥不了"的死部队。
        //   【修法】前缀匹配即放行, 并**顺手把它登记进 Forces**(复用 Adopt 的单支版本 AdoptOne)——
        //   这样"放行"与"能被指挥"同时成立, 且下一次日结的 Adopt 不会再把它当孤儿重复收养。
        //   (与海运 InConvoy 的"宁可不放行"口径的差异是**有意**的: 海运里那是一支别人的陆军军团,
        //     放行会让它带着一船人乱跑; 而 fia_navy_ 前缀是本 mod 独有的部队 id 空间, 不会有别人。)
        internal static bool InCommand(string partyId)
        {
            try
            {
                if (string.IsNullOrEmpty(partyId)) return false;
                if (!partyId.StartsWith(Prefix, StringComparison.Ordinal)) return false;
                if (ForceOf(partyId) != null) return true;
                AdoptOne(partyId);          // v4.278: 前缀匹配但没登记 -> 当场补一条(见上)
                return true;
            }
            catch { return false; }
        }

        internal static bool InCommand(MobileParty p)
        {
            try { return p != null && InCommand(p.StringId); } catch { return false; }
        }

        // 舰队是否处于"玩家命令锁定"(NavalTaskForceAi 的 GetBehaviors 兜底用它)
        internal static bool IsPlayerLocked(MobileParty p)
        {
            try
            {
                var f = ForceOf(p);
                return f != null && f.PlayerHold;
            }
            catch { return false; }
        }

        // ==================== 船源互斥(v4.276 C 方案) ====================
        //   海运(NavalTransport)与舰队都要"过户船"。被舰队占用的船必须从海运候选池里排除,
        //   否则同一艘船会被两套系统同时当成自己的 -> 到达后还船错乱、船的数量凭空翻倍。
        internal static bool ShipBlockedByFleet(int shipKey)
        {
            try
            {
                if (shipKey == 0) return false;
                for (int i = 0; i < Forces.Count; i++)
                {
                    var f = Forces[i];
                    if (f == null) continue;
                    if (f.ShipOwner.ContainsKey(shipKey)) return true;
                }
            }
            catch { }
            return false;
        }

        // 一支舰队当前是否已经占着这艘船(按对象比, 供 UI 标"已编入舰队")
        internal static bool ShipInFleet(Ship s)
        {
            try { return s != null && ShipBlockedByFleet(NavalTransport.ShipKey(s)); }
            catch { return false; }
        }

        // ==================== 组建 ====================
        // 返回值: "" = 成功(err 为空); ok 里是给玩家看的成功描述。失败时返回原因(ok 为空)。
        internal static string Found(Settlement port, List<Ship> pick, int sailors, out string ok)
        {
            ok = "";
            try
            {
                if (!NavalBridge.Usable) return "未检测到海战 DLC —— 舰队功能整体停用";
                var k = PlayerKingdom();
                if (k == null) return "尚未建立国家意志";
                if (port == null) return "请先选择一个自有港口";
                if (!port.HasPort) return "该地没有港口, 不能驻扎舰队";
                if (!ReferenceEquals(NavalBridge.KingdomOf(port), k)) return "只能在自有港口组建舰队";
                if (pick == null || pick.Count == 0) return "没有选中任何舰船";
                if (Forces.Count >= MaxForces) return "舰队数量已达上限 " + MaxForces + " 支";

                // 同一港口只允许一支舰队(否则点选/补给都要按港口二选一, 玩法上没有价值只有歧义)
                for (int i = 0; i < Forces.Count; i++)
                {
                    if (Forces[i] != null && Forces[i].HomePortId == port.StringId)
                        return "该港口已驻扎一支舰队 —— 先解散它, 或换一个港口";
                }

                // ★ 至少留 1 艘在氏族手里: 海运/买船/修船都以氏族舰队为口径, 搬空会让那些功能失效。
                //   ★★ 判据必须用"**没被任何舰队占用**的自由船"而不是 MyShips().Count ——
                //   舰队本身也是玩家氏族的 WarPartyComponent, 它的船**就在 MyShips() 里**,
                //   用总数算会让"再组建一支"时凭空多出额度, 最终把氏族手里的船抽干。
                int free = 0;
                var mineList = NavalFleet.MyShips();
                for (int i = 0; i < mineList.Count; i++)
                {
                    var s0 = mineList[i];
                    if (s0 == null) continue;
                    if (ShipBlockedByFleet(NavalTransport.ShipKey(s0))) continue;
                    free++;
                }
                if (free - pick.Count < KeepShipsInClan)
                    return "必须至少留 " + KeepShipsInClan + " 艘船在氏族手中(未被舰队占用的还有 " + free
                        + " 艘, 已选 " + pick.Count + " 艘) —— 否则海运/买船等其它海军功能会被搬空";
                if (pick.Count < MinShipsToFound) return "至少需要 " + MinShipsToFound + " 艘船";
                // 双保险: 调用方(UI)已排除, 这里再挡一次"同一艘船编入两支舰队"(船源互斥的另一半)
                for (int i = 0; i < pick.Count; i++)
                {
                    var s1 = pick[i];
                    if (s1 == null) continue;
                    if (ShipBlockedByFleet(NavalTransport.ShipKey(s1)))
                        return "有舰船已经编在别的舰队里(" + NavalBridge.HullName(s1) + ") —— 请刷新列表后重试";
                }

                // 1) 司令: 走国防军同一条将军生成路径(模板/家族/技能), 只换名字
                string title = (k.Name != null ? k.Name.ToString() : "王国") + "海军舰队";
                var captain = DefArmy.MakeCaptainFor(port, title);
                if (captain == null) return "舰队司令生成失败(见日志)";

                // 2) 实体部队: 出生点 = 港口海面点(PortPosition.IsOnLand == false)
                //    -> InitializeMobilePartyAtPosition(Position) 自动把 IsCurrentlyAtSea 置 true
                string pid = Prefix + Guid.NewGuid().ToString("N");
                MobileParty party = null;
                try { party = LordPartyComponent.CreateLordParty(pid, captain, port.PortPosition, 1f, port, captain); }
                catch (Exception ex) { DLog.Force("舰队: 创建部队失败 " + ex.Message); }
                if (party == null) { RetireCaptain(captain); return "舰队部队创建失败"; }
                try { party.ActualClan = Clan.PlayerClan; } catch { }

                // 3) 名册: 司令 + 水手(从港口守备营抽, 抽不到就只带司令 —— 见返回串里的如实说明)
                var mr = new TroopRoster(party.Party);
                int got = 0;
                if (sailors > 0) { try { got = DefArmy.MoveGarrisonTroopsTo(port, mr, sailors); } catch { } }
                CampaignVec2 spawn = port.PortPosition;
                try { party.InitializeMobilePartyAtPosition(mr, new TroopRoster(party.Party), spawn, false); } catch { }
                try { party.Party.SetCustomName(new TextObject(title + "「" + NavalBridge.NameOf(port) + "」")); } catch { }
                try { party.SetCustomHomeSettlement(port); } catch { }

                // 4) 船过户(过户后 DLC 的 HasNavalNavigationCapability = true, 原版就认它是海上部队)
                var f = new Force
                {
                    PartyId = party.StringId,
                    HomePortId = port.StringId,
                    HomePortName = NavalBridge.NameOf(port),
                    FoundedDay = SafeToday(),
                    Task = TaskStation,
                    PlayerHold = true
                };
                int moved = 0;
                for (int i = 0; i < pick.Count; i++)
                {
                    var s = pick[i];
                    if (s == null) continue;
                    try
                    {
                        string prevOwnerId = "";
                        try { prevOwnerId = s.Owner != null ? s.Owner.Id : ""; } catch { }
                        ChangeShipOwnerAction.ApplyByTransferring(party.Party, s);
                        int key = NavalTransport.ShipKey(s);
                        if (key != 0) f.ShipOwner[key] = prevOwnerId ?? "";
                        moved++;
                    }
                    catch (Exception ex) { DLog.Force("舰队: 移船失败 " + NavalBridge.HullName(s) + " " + ex.Message); }
                }
                if (moved == 0)
                {
                    // 一条船都没过户成功 -> 不能留下"海上无船"的非法部队, 立刻收摊
                    RetireCaptain(captain);
                    try { DestroyPartyAction.ApplyForDisbanding(party, port); } catch { }
                    return "编成失败: 没有任何舰船完成过户(已撤回)";
                }

                // 5) 出海 + 初始命令(驻港 = 停在母港海面)
                //    ★ 走 `SetMoveGoToSettlement(港, Naval, isTargetingThePort: true)` —— 与海运 Depart
                //      完全同一条链(反编译实证: 它设 TargetPosition = MoveTargetPoint = to.PortPosition,
                //      DesiredAiNavigationType = Naval, DefaultBehavior = GoToSettlement)。
                try
                {
                    if (!party.IsCurrentlyAtSea) party.SetSailAtPosition(port.PortPosition);
                    party.SetMoveGoToSettlement(port, MobileParty.NavigationType.Naval, true);
                }
                catch (Exception ex) { DLog.Force("舰队: 初始命令失败 " + ex.Message); }

                // 6) 玩家接管标记 —— 与国防军的 PlayerHold 同语义(见 DefArmy.MarkPlayerOrder)。
                //    这里**只设导航标志**, 绝不调 SetMoveModeHold()(那会让 UpdateBehavior 走
                //    SetNavigationModeHold -> PartyMoveMode 永远 Hold -> 船原地不动)。
                Arm(party, f);
                // v4.277(第 4 项 诊断): 组建时那条"开往母港驻泊"的命令也要记进 OrderKind/OrderTarget ——
                //   否则诊断日志里新建的舰队会显示"命令=(无记录)", 玩家会以为命令没下出去。
                // v4.278(第 6/7 项): 同时记下可重放的形状(港口对象) —— 被原版 SetMoveModeHold 冻住时靠它恢复。
                NoteOrder(f, OrderKindRecall, NavalBridge.NameOf(port));
                NoteMoveSettlement(f, port, true);

                Forces.Add(f);
                DLog.Force("海军: 组建舰队 " + party.StringId + " —— " + NavalBridge.NameOf(port)
                    + " 舰船 " + moved + " 艘 · 水手 " + got + " 人");

                string tail = got <= 0
                    ? "(该港守备营无兵可抽, 舰队只有司令 —— 遭遇战会很危险)"
                    : "(水手 " + got + " 人来自 " + NavalBridge.NameOf(port) + " 守备营)";
                ok = "已组建舰队: " + NavalBridge.NameOf(port) + " · 舰船 " + moved + " 艘 · 水手 " + got + " 人 " + tail
                    + "\n到地图上点选这支舰队即可指挥它(左键海面/港口, 右键菜单)。";
                return "";
            }
            catch (Exception ex) { DLog.Force("海军: 组建舰队异常 " + ex.Message); return "组建舰队失败: " + ex.Message; }
        }

        // 候选港口(自有 + 有港 + 尚未驻扎舰队)
        internal static List<Settlement> CandidatePorts()
        {
            var list = new List<Settlement>();
            try
            {
                var k = PlayerKingdom();
                if (k == null) return list;
                foreach (var s in NavalBridge.PortsOf(k))
                {
                    if (s == null || !s.HasPort) continue;
                    bool used = false;
                    for (int i = 0; i < Forces.Count; i++)
                        if (Forces[i] != null && Forces[i].HomePortId == s.StringId) { used = true; break; }
                    if (!used) list.Add(s);
                }
            }
            catch { }
            return list;
        }

        // ==================== 命令 ====================
        //   所有命令都只做两件事: ①把 DefaultBehavior/TargetPosition 设成我们想要的
        //   ②保持 PlayerHold = true(于是 NavalTaskForceAi 会把 AI 的改道拉回来)。
        //   **绝不自己动手推位置** —— 推进仍由原版导航链负责(与海运 Depart 完全一致)。

        private static void Arm(MobileParty p, Force f)
        {
            try
            {
                // v4.278(第 4 项): **这里就是"收回指挥权"的唯一入口** —— 玩家下达任何命令都会经过 Arm(),
                //   于是 "已交还海军部(PlayerHold=false)" 的舰队在玩家下一次下令时**自动**被收回,
                //   不需要 UI 再提供"接管"按钮(单向锁死的问题从根上消失)。
                if (f != null)
                {
                    if (!f.PlayerHold) DLog.Force("海军: 玩家下令, 收回 " + f.PartyId + " 的指挥权(自主 -> 受命)");
                    f.PlayerHold = true; f.OrderDay = SafeToday();
                }
                if (p != null)
                {
                    // ★ "AI 不接管": 用原版自己的开关(见 UseNativeNoDecision 的长注释)。
                    //   注意**绝不能**在这里调 SetMoveModeHold() —— 那是"原地不动"的来源。
                    if (UseNativeNoDecision) { try { p.Ai.SetDoNotMakeNewDecisions(true); } catch { } }
                    try { if (!p.IsActive) p.IsActive = true; } catch { }
                }
            }
            catch { }
        }

        // ==================== v4.278(第 4 项): 交还海军部 / 交由原版 AI 自主 ====================
        //   【为什么必须有这个入口】`PlayerHold` 原来只有 `= true` 五处赋值(组建 :361 / 收养 :760 /
        //   Arm 每次下令 / 存档缺省), 全仓 **零处** `= false` —— 于是舰队一旦组建就永远"玩家命令锁定":
        //     · 原版主动行为(躲风暴 / 避强敌 / 追击敌舰队)对它一律失效(NoInitiative + LockBehavior 两道锁);
        //     · 玩家也没有任何办法放手(没有"交还"按钮/菜单项), 舰队只能一直在地图上等命令。
        //   对照国防军: 那边有 `DefArmy.ReleaseToAi(MobileParty)`(:2238)+ 菜单项(Military\DefArmyMenu.cs:95),
        //   这里补齐同构的一对(交还 / 批量交还), 入口挂在地图右键菜单(与国防军同一处交互习惯)。
        //   【交还做什么/不做什么】
        //     · `PlayerHold = false` -> `LockBehavior` 与 `IsPlayerLocked` 立刻放行(判据见 NavalTaskForceAi:88);
        //     · `p.Ai.SetDoNotMakeNewDecisions(false)` -> 把"AI 不许改主意"的主开关也放开(否则舰队仍不会自己动);
        //     · 母港还是我方港口 -> 先给它一条"回母港待命"的命令作为**出发点**(反编译: GetBehaviors 从
        //       DefaultBehavior 起步, 给 Hold 的话 AI 即使被放行也无事可做);
        //     · **不动** `Task` 之外的用户可见口径(欠饷/军饷照收, 船维护照收 —— 舰队还在, 只是不听你的了)。
        //     · 与海运中的军团互斥: 舰队自己不会进 NavalTransport.Active(那是军团的口径), 所以无需额外判断。
        internal static string ReleaseToAi(MobileParty p)
        {
            try
            {
                var f = ForceOf(p);
                if (f == null) return "这不是舰队";
                if (!f.PlayerHold) return MapSelection.NameOf(p) + " 已经交由海军部自主巡航了";
                f.PlayerHold = false;
                if (UseNativeNoDecision) { try { p.Ai.SetDoNotMakeNewDecisions(false); } catch { } }
                ClearMoor(f, "交还海军部");   // v4.282: 交给原版 AI 就不再享受泊位收益(否则是"白拿折扣的自主舰队")
                // 出发点: 能回母港就回母港待命(不然 AI 会在原地打转一阵才决定去哪)
                var home = FindSettlement(f.HomePortId);
                if (home != null && home.HasPort)
                {
                    try
                    {
                        EnsureAtSea(p, f);
                        p.SetMoveGoToSettlement(home, MobileParty.NavigationType.Naval, true);
                        f.OrderSettlementId = home.StringId;
                        f.HasOrderPoint = false;
                    }
                    catch { }
                    AssignTask(f, TaskVoyage, "交还海军部, 返航母港待命");
                }
                else
                {
                    AssignTask(f, TaskAuto, "交还海军部(母港未知, 就地自主)");
                }
                NoteOrder(f, "", "");   // 命令记录清空: 已经没有"玩家命令"这回事了(否则会显示成假命令)
                string msg = MapSelection.NameOf(p) + " 已交还海军部自主巡航(原版 AI 接管: 会自行躲风暴/避强敌)";
                DLog.Force("海军: 交还舰队 " + f.PartyId + " 给原版 AI(PlayerHold=false)");
                try { MapSelection.Message("海军: " + msg + " —— 想收回, 直接对它下任何命令即可"); } catch { }
                return msg;
            }
            catch (Exception ex) { DLog.Force("海军: 交还舰队失败 " + ex.Message); return "交还失败: " + ex.Message; }
        }

        // 批量交还(与 DefArmy.ReleaseAllToAi 同形; 供"一键放手"入口/调试用)
        //   v4.284(第 3 项): 面板加了 [全部交还] —— 顺手补上**海运守卫**: 正在执行海运的部队不归舰队口径
        //     (NavalTransport.Active 收的是军团), 但守卫函数是现成的, 显式跳过并单独计数,
        //     免得将来舰队能搭海运时"一键交还"把在途部队的命令链打断。
        internal static string ReleaseAllToAi()
        {
            try
            {
                var list = Alive();
                int n = 0, skipped = 0, already = 0;
                for (int i = 0; i < list.Count; i++)
                {
                    var p = PartyOf(list[i]);
                    if (p == null) { skipped++; continue; }
                    try { if (NavalTransport.InConvoy(p.StringId)) { skipped++; continue; } } catch { }
                    string r = ReleaseToAi(p);
                    if (r.IndexOf("已交还", StringComparison.Ordinal) >= 0) n++;
                    else if (r.IndexOf("已经交由", StringComparison.Ordinal) >= 0) already++;
                }
                if (n > 0)
                    return "已交还 " + n + " 支舰队给海军部自主巡航"
                         + (already > 0 ? " · " + already + " 支本来就是自主状态" : "")
                         + (skipped > 0 ? " · " + skipped + " 支不在场/海运中跳过" : "");
                if (already > 0) return "没有需要交还的舰队(全部 " + already + " 支都已自主)";
                return "没有需要交还的舰队(可能都已自主)";
            }
            catch (Exception ex) { return "批量交还失败: " + ex.Message; }
        }

        // 当前是否"玩家在指挥"(供面板显示"受命/自主"两态)
        internal static bool IsPlayerControlled(MobileParty p)
        {
            try { var f = ForceOf(p); return f == null || f.PlayerHold; }
            catch { return true; }
        }

        // v4.278(第 4 项): **同一个开关的正面** —— 把已交还的舰队收回玩家指挥。
        //   注意这不是"必需"的入口: 玩家对它下**任何**命令都会经 Arm() 自动收回(见 Arm 注释);
        //   这里提供显式方法, 是为了让面板/菜单能做一个"接管"按钮, 且收回后立刻给它一条明确命令
        //   (默认 = 回母港驻泊), 否则舰队会停在原地等 AI 下一秒把它派走。
        internal static string TakeCommand(MobileParty p)
        {
            try
            {
                var f = ForceOf(p);
                if (f == null) return "这不是舰队";
                if (f.PlayerHold) return MapSelection.NameOf(p) + " 本来就在你的指挥之下";
                var home = FindSettlement(f.HomePortId);
                if (home != null && home.HasPort)
                {
                    string r = OrderRecall(p);     // OrderRecall 内部会调 Arm() -> 自动收回指挥权
                    return r;
                }
                // 母港未知: 也要把指挥权收回(只是没有落点可下命令)
                Arm(p, f);
                AssignTask(f, TaskStation, "玩家收回指挥权(母港未知, 就地待命)");
                NoteOrder(f, "", "");
                return MapSelection.NameOf(p) + " 已收回你方指挥(母港未知, 请右键[指定母港…])";
            }
            catch (Exception ex) { DLog.Force("海军: 收回舰队失败 " + ex.Message); return "收回失败: " + ex.Message; }
        }

        // 日结自愈: 万一有别的系统(AI/原版行为/读档)把标志清掉了, 这里补回来。
        //   与 CommandTimeout 的"手动驾驶兜底"同一思路: 不假设外部一定规矩, 自己保证不变量。
        //   ★ v4.278(第 4 项): **只在玩家还锁着(PlayerHold)时自愈** —— 否则它会把
        //     `ReleaseToAi` 刚放开的 `DoNotMakeNewDecisions(false)` 每天重新摁回 true,
        //     于是"交还海军部"变成假动作(舰队仍然不参与原版主动行为)。这是本项必须成对改的一处。
        internal static void Reassert(MobileParty p)
        {
            try
            {
                if (p == null) return;
                var f = ForceOf(p);
                if (f == null || !f.PlayerHold) return;   // 已交还 -> 不碰它的 AI 开关
                if (UseNativeNoDecision && !p.Ai.DoNotMakeNewDecisions) p.Ai.SetDoNotMakeNewDecisions(true);
                if (p.DefaultBehavior == AiBehavior.Hold) { /* Hold 说明命令已被清, 由玩家重新下令 */ }
            }
            catch { }
        }

        // v4.277(第 4 项): 记下"最近一次命令"的形状(诊断日志要打出来)。
        //   与 Arm() 分开是**故意的**: Arm 只负责"导航标志/不接管", 同一段代码可能被日结自愈路径调到;
        //   而"最近一次命令"只在玩家真的下命令时更新(否则诊断日志里会全是自愈噪声, 看不出玩家下过什么)。
        //
        // v4.278(第 6/7 项): 订单记录**从"给人看的"升级成"可重放的"** —— NoteOrder 现在同时把
        //   "命令的完整形状"写进 Force(港口对象 / 海面点坐标), 这样 RecoverFromHold 与 TaskSync
        //   都能在**不依赖 DefaultBehavior**(它可能已被 SetMoveModeHold 清成 Hold)的前提下重下同一条命令。
        private static void NoteOrder(Force f, string kind, string target)
        {
            try
            {
                if (f == null) return;
                f.OrderKind = kind ?? "";
                f.OrderTarget = target ?? "";
            }
            catch { }
        }

        // v4.278: 记"前往聚落"类命令的完整形状(港口 id 存下来, 供恢复/终态判定用)
        private static void NoteMoveSettlement(Force f, Settlement s, bool alsoPoint)
        {
            try
            {
                if (f == null) return;
                f.OrderSettlementId = s != null ? s.StringId : "";
                f.OrderTargetPartyId = "";   // ★ 换成聚落命令 -> 之前的交战目标作废(同 NoteMovePoint)
                if (alsoPoint) NoteMovePoint(f, s != null ? s.PortPosition : default(CampaignVec2));
                else { f.HasOrderPoint = false; f.OrderPointX = 0f; f.OrderPointY = 0f; }
            }
            catch { }
        }

        // v4.278: 记"前往海面点"类命令的坐标(没有港口可用, 恢复时只能靠这两个数)
        private static void NoteMovePoint(Force f, CampaignVec2 pt)
        {
            try
            {
                if (f == null) return;
                f.HasOrderPoint = true;
                f.OrderPointX = pt.X;
                f.OrderPointY = pt.Y;
                // ★ 命令换成了"去某个坐标" -> 之前那条交战命令的目标作废(否则 TaskSync 会拿旧目标判敌对)
                f.OrderTargetPartyId = "";
            }
            catch { }
        }

        // ==================== v4.278(第 7 项 ③): 从"被 SetMoveModeHold 冻住"中恢复 ====================
        //   谁会把舰队冻住(反编译实证):
        //     `AiPartyThinkBehavior.CheckMobilePartyActionAccordingToSettlement:284-333`
        //     —— 聚落易主 / 宣战 / 和谈 / 氏族换国 这些回调里, 只要**舰队的目标聚落**正好是那个聚落
        //     (`mobileParty.TargetSettlement == settlement`), 就调 `mobileParty.SetMoveModeHold()`。
        //     `MobileParty.SetMoveModeHold:4280-4290` 会 ResetAllMovementParameters():
        //       DefaultBehavior = Hold, ShortTermBehavior = Hold, TargetSettlement/TargetParty = null,
        //       TargetPosition = MoveTargetPoint = 当前位置, DesiredAiNavigationType = None。
        //     ★ 这条路径**完全不看 `DoNotMakeNewDecisions`**, 所以 NoInitiative + 主开关两道守卫都拦不住它 ——
        //       舰队会在半路突然停死, 而且因为 DefaultBehavior 已经变成 Hold, 我们再怎么修 bestAiBehavior
        //       也没用(UpdateBehavior 走 Hold 分支 -> SetNavigationModeHold)。
        //   ⇒ 唯一有效的修法 = **按记录重下同一条命令**(SetMove* 都是 public, 会重新写全 DefaultBehavior/
        //     TargetPosition/MoveTargetPoint/DesiredAiNavigationType)。
        //   【只在真被冻住时恢复】判据 = `DefaultBehavior == Hold`(调用方已保证 PlayerHold && 前缀匹配)。
        //     正常驻港到位的舰队 Natural 也会是 Hold, 所以必须再要求"任务尚未结束"(Task != 驻港),
        //     否则会把停在母港的舰队反复拉出去巡航 —— 那是 bug 不是修复。
        //   返回 true = 已成功重下命令(调用方应继续按 p.DefaultBehavior 走锁定逻辑)。
        internal static bool RecoverFromHold(MobileParty p)
        {
            try
            {
                var f = ForceOf(p);
                if (f == null) return false;
                if (f.Task == TaskStation) return false;              // 驻港(含已到位) -> 这是正常的 Hold, 不动它
                if (string.IsNullOrEmpty(f.OrderKind)) return false;  // 没有可重放的命令 -> 无从恢复

                // ① 交战: 目标部队还在 -> 重下交战命令(TargetParty 会被 SetMoveModeHold 清空, 所以按记下的 id 找)
                if (f.OrderKind == OrderKindEngage)
                {
                    var tp = p.TargetParty;
                    if (tp == null || !tp.IsActive)
                    {
                        try { tp = DefArmy.FindParty(f.OrderTargetPartyId); }
                        catch { tp = null; }
                    }
                    if (tp == null || !tp.IsActive) return false;
                    EnsureAtSea(p, f);
                    p.SetMoveEngageParty(tp, MobileParty.NavigationType.Naval);
                    f.OrderTargetPartyId = tp.StringId ?? "";
                    DLog.Force("海军: " + MapSelection.NameOf(p) + " 曾被原版 SetMoveModeHold 冻住, 已重下交战命令 -> "
                        + MapSelection.NameOf(tp));
                    return true;
                }

                // ② 前往/封锁/回港/巡航: 目标港还在 -> 重下"开往该港海面"
                var port = FindSettlement(f.OrderSettlementId);
                if (port != null && port.HasPort)
                {
                    EnsureAtSea(p, f);
                    if (f.OrderKind == OrderKindPatrol)
                        p.SetMovePatrolAroundSettlement(port, MobileParty.NavigationType.Naval, true);
                    else
                        p.SetMoveGoToSettlement(port, MobileParty.NavigationType.Naval, true);
                    DLog.Force("海军: " + MapSelection.NameOf(p) + " 曾被原版 SetMoveModeHold 冻住, 已恢复命令"
                        + f.OrderKind + " -> " + NavalBridge.NameOf(port));
                    return true;
                }

                // ③ 海面点命令: 用存下来的坐标重下
                if (f.HasOrderPoint)
                {
                    EnsureAtSea(p, f);
                    p.SetMoveGoToPoint(new CampaignVec2(new Vec2(f.OrderPointX, f.OrderPointY), false),
                        MobileParty.NavigationType.Naval);
                    DLog.Force("海军: " + MapSelection.NameOf(p) + " 曾被原版 SetMoveModeHold 冻住, 已恢复到海面点("
                        + (int)f.OrderPointX + "," + (int)f.OrderPointY + ")");
                    return true;
                }
                return false;
            }
            catch (Exception ex) { DLog.Force("海军: 冻结恢复失败 " + ex.Message); return false; }
        }

        // ==================== v4.278(第 6 项): 任务终态同步 ====================
        //   【原缺陷】`Force.Task` 只在玩家下令时赋值(OrderToPort/Recall/Patrol/Engage), 到达/超时/中断/
        //   召回**都不改** ⇒ 一支早就停在母港的舰队, 面板上仍然写着"远征/封锁", 与实际完全脱节。
        //   【修法】小时结统一对账(由 NavalBehavior.OnHourlyTick -> NavalTaskForce.HourlySync 调),
        //   把 Task 收敛到"这一秒真实在做的事"。每一条转换都写明理由并记日志, 不做静默改写。
        internal static void TaskSync()
        {
            try
            {
                var list = Alive();
                for (int i = 0; i < list.Count; i++) TaskSyncOne(list[i]);
            }
            catch { }
        }

        private static void TaskSyncOne(Force f)
        {
            try
            {
                if (f == null) return;
                var p = PartyOf(f);
                if (p == null || !p.IsActive) return;
                int today = SafeToday();

                // ⓪ ==================== v4.282: 驻泊态优先收敛 ====================
                //   "进驻中 -> 驻泊"的收敛点就是这里(与"远征 -> 驻港"同一套机制)。
                //   三条分支都有明确理由, 不做静默改写:
                //     · 驻泊港失效/易主 -> 自动离港(清 MooredPortId, 母港保留), 并在地图上告知;
                //     · 已在泊位(距 ≤ ArriveRadius+12) -> Task 收敛成"驻泊"(折扣/修理从这一态开始生效);
                //     · 还没到 -> Task = "进驻中" + 走与远征相同的超时判定(超时由 CheckTimeout 召回母港)。
                //   ★ 驻泊是**长期任务**, 到位后不参与超时(与封锁/巡航同一口径)。
                if (f.HasMoor)
                {
                    var mp = FindSettlement(f.MooredPortId);
                    var mine = PlayerKingdom();
                    bool lost = mp == null || !mp.HasPort
                        || (mine != null && !ReferenceEquals(NavalBridge.KingdomOf(mp), mine));
                    if (lost)
                    {
                        string gone = string.IsNullOrEmpty(f.MooredPortName) ? "?" : f.MooredPortName;
                        f.MooredPortId = ""; f.MooredPortName = ""; f.MoorDay = 0;
                        InvalidateMoored();
                        AssignTask(f, TaskStation, "驻泊港「" + gone + "」已失效或易主, 自动离港");
                        try { MapSelection.Message("海军: 「" + gone + "」不再是你方港口 —— 舰队已自动离港"); } catch { }
                    }
                    else
                    {
                        float md = DistTo(p, mp);
                        if (md <= ArriveRadius + 12f)
                        {
                            if (!IsMoorTask(f.Task))
                                AssignTask(f, MoorTaskText(f.MooredPortName), "已到达驻泊港「" + NavalBridge.NameOf(mp) + "」(距 " + (int)md + ")");
                            f.OrderDay = today;   // 在位: 重新计时(与封锁/巡航的"到位即在位"一致)
                            return;
                        }
                        if (f.Task != TaskMooring)
                            AssignTask(f, TaskMooring, "奉命进驻「" + NavalBridge.NameOf(mp) + "」");
                        CheckTimeout(f, p, today, f.Task);
                        return;
                    }
                }

                // ① 停在一处聚落里(靠港补给/进城) -> 记"驻港"(除非本来就写着驻港)
                if (p.CurrentSettlement != null)
                {
                    AssignTask(f, TaskStation, "已靠泊「" + NavalBridge.NameOf(p.CurrentSettlement) + "」");
                    return;
                }

                // ② 驻港任务: 只要还在母港海域就算驻港; 明显漂远了(玩家手操/被 AI 带走)也如实标成驻港
                //    —— "驻港"描述的是**任务意图**, 位置偏离不改意图, 只由玩家下一条命令改。
                //    ★ v4.278(第 4 项): 已交还海军部(PlayerHold=false)的舰队**不能**显示成"驻港" ——
                //      那会让玩家以为它还在听命令。统一显示"自主"(TaskAuto)。
                if (f.Task == TaskStation)
                {
                    if (!f.PlayerHold) AssignTask(f, TaskAuto, "已交还海军部, 由原版 AI 自主");
                    return;
                }
                if (!f.PlayerHold && f.Task != TaskAuto) { AssignTask(f, TaskAuto, "已交还海军部, 由原版 AI 自主"); return; }

                // ③ 交战中: 目标没了/不再敌对 -> 任务作废, 就地转驻港(停在原地等玩家下令)
                if (f.Task == TaskEngage)
                {
                    var tp = p.TargetParty;
                    if (tp == null || !tp.IsActive)
                    {
                        // v4.278: TargetParty 可能被 SetMoveModeHold/原版清掉 -> 按记下的 id 复核
                        //   (用本仓既有的 DefArmy.FindParty, 它扫 Campaign.Current.MobileParties)
                        try { tp = DefArmy.FindParty(f.OrderTargetPartyId); }
                        catch { tp = null; }
                    }
                    var k = PlayerKingdom();
                    bool stillHostile = false;
                    try
                    {
                        if (tp != null && tp.IsActive && k != null && tp.MapFaction != null)
                            stillHostile = !ReferenceEquals(tp.MapFaction, k) && FactionManager.IsAtWarAgainstFaction(k, tp.MapFaction);
                    }
                    catch { }
                    if (stillHostile) return;   // 还在打 -> 由原版遭遇流程处理, 不动 Task
                    AssignTask(f, TaskStation, "交战目标已消失或已停战");
                    p.SetMoveModeHold();   // 停在原地: 命令已经作废, 别让它继续追着一个不存在的目标跑
                    NoteOrder(f, OrderKindRecall, "");
                    f.OrderSettlementId = "";
                    f.OrderTargetPartyId = "";
                    return;
                }

                // ④ 封锁 / 巡航: **到位即在位**的长期任务 —— Task 不收敛成"驻港", 也不参与超时。
                //   理由: 这两条任务的"目的地"就是它们的**工作海域**, 到了才是开始:
                //     · 封锁: 面板与日结按 `NavalFleet.IsBlockading` 结算, 舰队就停在那片海域施压;
                //       旗标被清(停战/玩家解封) -> 任务终止转驻港。
                //     · 巡航: 到达即进入巡逻圈, 长期巡逻。
                //   若把这两条也按"到港即驻港"收敛, 面板会写"驻港 · 母港 X"而舰队其实停在敌港外 ——
                //   那正是第 6 项要修的"面板与实际不一致"。
                //   ★ 只有**还没到位**时才判超时(命令下达 N 天仍未抵达工作海域 = 远征失败 -> 召回)。
                if (f.Task == TaskBlockade || f.Task == TaskPatrol)
                {
                    if (f.Task == TaskBlockade && !NavalFleet.IsBlockading)
                    {
                        AssignTask(f, TaskStation, "封锁已解除");
                        return;
                    }
                    var wp = FindSettlement(f.OrderSettlementId);
                    if (wp != null && wp.HasPort && DistTo(p, wp) > ArriveRadius)
                    {
                        CheckTimeout(f, p, today, f.Task);
                        return;
                    }
                    // 已到位: 订单记录保留(恢复逻辑据此重下同一条命令), 重新计时以记录"在位时长"
                    f.OrderDay = today;
                    return;
                }

                // ⑤ 移动类(远征 / 前往某港 / 回港): 到达 = 目的达成 -> 转"驻港"
                var port = FindSettlement(f.OrderSettlementId);
                if (port != null && port.HasPort)
                {
                    float d = DistTo(p, port);
                    if (d <= ArriveRadius)
                    {
                        AssignTask(f, TaskStation, "已到达「" + NavalBridge.NameOf(port) + "」(距 " + (int)d + ")");
                        NoteOrder(f, OrderKindRecall, NavalBridge.NameOf(port));
                        return;
                    }
                }
                else if (f.HasOrderPoint)
                {
                    // 海面点命令: 用坐标判到位(没有港口可查距离)
                    try
                    {
                        var pt = new CampaignVec2(new Vec2(f.OrderPointX, f.OrderPointY), false);
                        float d = p.Position.Distance(pt);
                        if (d <= ArriveRadius)
                        {
                            AssignTask(f, TaskStation, "已到达指定海域(距 " + (int)d + ")");
                            NoteOrder(f, OrderKindRecall, "");
                            f.OrderSettlementId = ""; f.HasOrderPoint = false;
                            return;
                        }
                    }
                    catch { }
                }

                // ⑥ 母港就在眼前(回港命令 / 被别处逻辑带回母港) -> 也算到位
                var home = FindSettlement(f.HomePortId);
                if (home != null && home.HasPort && DistTo(p, home) <= ArriveRadius)
                {
                    AssignTask(f, TaskStation, "已回到母港「" + NavalBridge.NameOf(home) + "」");
                    NoteOrder(f, OrderKindRecall, NavalBridge.NameOf(home));
                    return;
                }

                // ⑦ 超时: 命令下达超过 OrderTimeoutDays 仍未到位 -> 判定远征失败, 自动召回母港
                CheckTimeout(f, p, today, f.Task);
            }
            catch { }
        }

        // 超时判定 + 自动召回(f.Task 由本函数收敛成"回港在途", 到位后 TaskSyncOne 再收敛成"驻港")
        private static void CheckTimeout(Force f, MobileParty p, int today, string what)
        {
            try
            {
                if (f == null || p == null) return;
                if (f.OrderDay <= 0) return;
                if (today - f.OrderDay < OrderTimeoutDays) return;
                if (f.Task == TaskStation) return;

                var home = FindSettlement(f.HomePortId);
                string nm = MapSelection.NameOf(p);
                if (home != null && home.HasPort)
                {
                    EnsureAtSea(p, f);
                    p.SetMoveGoToSettlement(home, MobileParty.NavigationType.Naval, true);
                    f.OrderSettlementId = home.StringId;
                    f.HasOrderPoint = false;
                    // ★ v4.278: 交战超时必须**改任务** —— 否则下一个小时又进"交战"分支, 再判一次超时,
                    //   舰队会被反复重下交战/召回命令(永远回不了母港)。改成"远征(回港在途)",
                    //   到达母港后由 TaskSyncOne 的 ⑤/⑥ 收敛成"驻港"。
                    AssignTask(f, TaskVoyage, what + "超时 " + OrderTimeoutDays + " 天, 已自动召回母港");
                    NoteOrder(f, OrderKindRecall, NavalBridge.NameOf(home));
                    f.OrderTargetPartyId = "";
                    DLog.Force("海军: " + nm + " " + what + "超时(命令下达于第 " + f.OrderDay + " 天, 已过 "
                        + OrderTimeoutDays + " 天), 自动召回母港");
                    f.OrderDay = today;   // 重新计时, 不然下一小时又判一次
                    try { MapSelection.Message("舰队「" + nm + "」" + what + "超时 " + OrderTimeoutDays + " 天未达成, 已自动返航母港"); }
                    catch { }
                }
                else
                {
                    AssignTask(f, TaskStation, what + "超时 " + OrderTimeoutDays + " 天且母港未知, 就地驻泊");
                    NoteOrder(f, OrderKindRecall, "");
                    f.OrderSettlementId = "";
                    f.OrderTargetPartyId = "";
                    f.HasOrderPoint = false;
                    f.OrderDay = today;   // ★ 同样要重新计时, 否则"母港未知"的舰队每小时刷一条超时日志
                    DLog.Force("海军: " + nm + " " + what + "超时, 但母港未知 —— 就地驻泊");
                    try { MapSelection.Message("舰队「" + nm + "」" + what + "超时, 但母港未知 —— 已就地驻泊, 请右键[指定母港…]"); }
                    catch { }
                }
            }
            catch { }
        }

        // Task 唯一写入口(除玩家命令外) —— 变更时如实记日志, 便于对账"面板上那行字是什么时候变的"
        private static void AssignTask(Force f, string task, string why)
        {
            try
            {
                if (f == null || string.IsNullOrEmpty(task)) return;
                if (f.Task == task) return;
                DLog.Force("海军: 舰队 " + f.PartyId + " 任务 " + f.Task + " -> " + task + "(" + why + ")");
                f.Task = task;
            }
            catch { }
        }

        // 确保舰队在海上(万一被原版逻辑弄上岸, 从最近的港口重新下水)
        private static void EnsureAtSea(MobileParty p, Force f)
        {
            try
            {
                if (p == null || p.IsCurrentlyAtSea) return;
                Settlement from = p.CurrentSettlement;
                if (from == null || !from.HasPort) from = FindSettlement(f != null ? f.HomePortId : null);
                if (from == null || !from.HasPort) return;
                p.SetSailAtPosition(from.PortPosition);
                DLog.Force("舰队: " + MapSelection.NameOf(p) + " 原本不在海上, 已从「" + NavalBridge.NameOf(from) + "」重新下水");
            }
            catch { }
        }

        // 前往某个港口(blockade = 到达后执行封锁)
        internal static string OrderToPort(MobileParty p, Settlement port, bool blockade)
        {
            try
            {
                var f = ForceOf(p);
                if (f == null) return "这不是舰队";
                if (port == null || !port.HasPort) return "目标不是港口";
                var k = PlayerKingdom();
                if (blockade)
                {
                    var owner = NavalBridge.KingdomOf(port);
                    if (owner == null) return "该港已无主, 无法封锁";
                    if (k == null || ReferenceEquals(owner, k)) return "不能封锁自己的港口";
                    if (!k.IsAtWarWith(owner)) return "未与该国交战, 不能封锁其港口";
                }
                EnsureAtSea(p, f);
                p.SetMoveGoToSettlement(port, MobileParty.NavigationType.Naval, true);
                Arm(p, f);
                ClearMoor(f, blockade ? "奉命出击封锁" : "奉命前往他港");   // v4.282: 离开泊位 = 解除驻泊
                f.Task = blockade ? TaskBlockade : TaskVoyage;
                f.OrderPortId = port.StringId;
                NoteOrder(f, blockade ? OrderKindBlockade : OrderKindPort, NavalBridge.NameOf(port));
                NoteMoveSettlement(f, port, true);   // v4.278: 记下可重放的命令形状(见 NoteOrder 注释)
                string verb = blockade ? "封锁" : "前往";
                DLog.Force("舰队命令: " + MapSelection.NameOf(p) + " -> " + verb + " " + NavalBridge.NameOf(port));
                return MapSelection.NameOf(p) + " 已奉命" + verb + "「" + NavalBridge.NameOf(port) + "」";
            }
            catch (Exception ex) { DLog.Force("舰队命令失败: " + ex.Message); return "命令失败: " + ex.Message; }
        }

        // 前往海面任意一点
        internal static string OrderToPoint(MobileParty p, CampaignVec2 point)
        {
            try
            {
                var f = ForceOf(p);
                if (f == null) return "这不是舰队";
                EnsureAtSea(p, f);
                // v4.276: 海面点必须用 Naval 导航 —— 舰队没有陆地导航需求, 用 Default 会试着上岸
                p.SetMoveGoToPoint(point, MobileParty.NavigationType.Naval);
                Arm(p, f);
                ClearMoor(f, "奉命前往指定海域");   // v4.282: 离开泊位 = 解除驻泊
                f.Task = TaskVoyage;
                f.OrderPortId = "";
                NoteOrder(f, OrderKindPoint, "海面点(" + (int)point.X + "," + (int)point.Y + ")");
                NoteMovePoint(f, point);   // v4.278: 海面点命令靠这两个坐标重放(没有港口可查)
                DLog.Force("舰队命令: " + MapSelection.NameOf(p) + " -> 海面点(" + (int)point.X + "," + (int)point.Y + ")");
                return MapSelection.NameOf(p) + " 已奉命前往指定海域";
            }
            catch (Exception ex) { DLog.Force("舰队移动失败: " + ex.Message); return "命令失败: " + ex.Message; }
        }

        // v4.282: 解除驻泊的**唯一内部入口** —— 玩家下达任何"离开泊位"的命令时调用(离港按钮也走它)。
        //   为什么不直接把字段清掉: 这里要成对做三件事(清字段 + 记日志 + 保留母港), 散落各处必然漏。
        //   ★ **不**改 Task —— Task 由各命令自己设(或由 TaskSync 收敛); 这里只负责"泊位没了"。
        private static void ClearMoor(Force f, string why)
        {
            try
            {
                if (f == null || !f.HasMoor) return;
                string nm = string.IsNullOrEmpty(f.MooredPortName) ? "?" : f.MooredPortName;
                f.MooredPortId = ""; f.MooredPortName = ""; f.MoorDay = 0;
                InvalidateMoored();   // v4.282: 维护折扣立刻失效(否则最多 10ms 内还按驻泊价算)
                DLog.Force("海军: 舰队 " + f.PartyId + " " + why + " -> 解除驻泊「" + nm + "」(母港保留「"
                    + (string.IsNullOrEmpty(f.HomePortName) ? "?" : f.HomePortName) + "」)");
            }
            catch { }
        }

        // 回母港驻泊
        internal static string OrderRecall(MobileParty p)
        {
            try
            {
                var f = ForceOf(p);
                if (f == null) return "这不是舰队";
                var home = FindSettlement(f.HomePortId);
                // v4.277(第 3 项): 母港未知/已失效时给**可操作的**提示 —— 原来只说"母港已不存在",
                //   玩家看不出该做什么。现在直接告诉他用右键菜单重新指定一处自有港口。
                if (home == null || !home.HasPort)
                {
                    f.HomePortId = ""; f.HomePortName = "";
                    return "母港未知(原母港已失效或被转让)—— 请右键这支舰队选[指定母港…]重新指定一处自有港口";
                }
                // v4.282: 已经**驻泊在母港泊位**里 -> [回港] 的语义就是"待在泊位", 不该顺手解除驻泊
                //   (否则玩家点一下[回港]就静默丢掉维护折扣与在港修理 —— 那是"看起来没反应, 其实扣了收益")。
                if (f.HasMoor && f.MooredPortId == home.StringId && InBerth(f))
                {
                    Arm(p, f);
                    f.Task = MoorTaskText(f.MooredPortName);
                    f.OrderPortId = home.StringId;
                    NoteOrder(f, OrderKindMoor, NavalBridge.NameOf(home));
                    NoteMoveSettlement(f, home, true);
                    return MapSelection.NameOf(p) + " 已进驻「" + NavalBridge.NameOf(home) + "」泊位, 无需返航(点[离港]才会离开)";
                }
                ClearMoor(f, "奉命返航母港");   // v4.282: 回港 = 离开别处的泊位(见上: 已在母港泊位时不走这里)
                EnsureAtSea(p, f);
                p.SetMoveGoToSettlement(home, MobileParty.NavigationType.Naval, true);
                Arm(p, f);
                f.Task = TaskStation;
                f.OrderPortId = home.StringId;
                NoteOrder(f, OrderKindRecall, NavalBridge.NameOf(home));
                NoteMoveSettlement(f, home, true);   // v4.278: 回港命令也要可重放(被冻住时靠它恢复)
                return MapSelection.NameOf(p) + " 已奉命返回母港「" + NavalBridge.NameOf(home) + "」";
            }
            catch (Exception ex) { DLog.Force("舰队回港失败: " + ex.Message); return "命令失败: " + ex.Message; }
        }

        // v4.277(第 3 项): 手动指定母港 —— 与 Adopt() 的自动推断互补:
        //   推断失败(非港口/无自家港口)时记录里 HomePortId 为空, 玩家可以在这里一次性指定,
        //   之后 [回港]/[巡航] 就有落点了(不必解散重建)。
        internal static string SetHomePort(MobileParty p, Settlement port)
        {
            try
            {
                var f = ForceOf(p);
                if (f == null) return "这不是舰队";
                if (port == null || !port.HasPort) return "目标不是港口";
                var k = PlayerKingdom();
                if (k != null && !ReferenceEquals(NavalBridge.KingdomOf(port), k))
                    return "只能指定自有港口为母港";
                f.HomePortId = port.StringId;
                f.HomePortName = NavalBridge.NameOf(port);
                DLog.Force("舰队命令: " + MapSelection.NameOf(p) + " 母港改为「" + f.HomePortName + "」");
                return MapSelection.NameOf(p) + " 的母港已指定为「" + f.HomePortName + "」";
            }
            catch (Exception ex) { DLog.Force("舰队指定母港失败: " + ex.Message); return "命令失败: " + ex.Message; }
        }

        // 在某个港口海域巡航(原版 PatrolAroundPoint 行为, 海上分支由 DLC 的 model 处理)
        internal static string OrderPatrol(MobileParty p, Settlement port)
        {
            try
            {
                var f = ForceOf(p);
                if (f == null) return "这不是舰队";
                if (port == null || !port.HasPort) return "目标不是港口";
                EnsureAtSea(p, f);
                p.SetMovePatrolAroundSettlement(port, MobileParty.NavigationType.Naval, true);
                Arm(p, f);
                ClearMoor(f, "奉命巡航");   // v4.282: 离开泊位 = 解除驻泊
                f.Task = TaskPatrol;
                f.OrderPortId = port.StringId;
                NoteOrder(f, OrderKindPatrol, NavalBridge.NameOf(port));
                NoteMoveSettlement(f, port, true);   // v4.278: 巡航命令可重放(恢复时用 PatrolAroundSettlement 重下)
                return MapSelection.NameOf(p) + " 已在「" + NavalBridge.NameOf(port) + "」海域巡航";
            }
            catch (Exception ex) { DLog.Force("舰队巡航失败: " + ex.Message); return "命令失败: " + ex.Message; }
        }

        // 攻击某支敌方舰队
        internal static string OrderEngage(MobileParty p, MobileParty target)
        {
            try
            {
                var f = ForceOf(p);
                if (f == null) return "这不是舰队";
                if (target == null || ReferenceEquals(target, p)) return "目标无效";
                var k = PlayerKingdom();
                bool hostile = false;
                try
                {
                    var tf = target.MapFaction;
                    hostile = k != null && tf != null && !ReferenceEquals(tf, k) && FactionManager.IsAtWarAgainstFaction(k, tf);
                }
                catch { }
                if (!hostile) return "该目标不是敌国部队";
                EnsureAtSea(p, f);
                p.SetMoveEngageParty(target, MobileParty.NavigationType.Naval);
                Arm(p, f);
                ClearMoor(f, "奉命交战");   // v4.282: 离开泊位 = 解除驻泊
                f.Task = TaskEngage;
                f.OrderPortId = "";
                NoteOrder(f, OrderKindEngage, MapSelection.NameOf(target));
                // v4.278: 交战命令**刻意不记坐标** —— 目标是一支会动的部队, 记下来的点下一秒就过期。
                //   改记**目标部队 id**(TargetParty 会被 SetMoveModeHold 清空, id 不会丢),
                //   恢复时按 id 找回来(见 RecoverFromHold); 找不回来就不恢复(宁可不接管, 不乱跑)。
                f.HasOrderPoint = false; f.OrderSettlementId = "";
                f.OrderTargetPartyId = target.StringId ?? "";
                return MapSelection.NameOf(p) + " 已奉命攻击 " + MapSelection.NameOf(target);
            }
            catch (Exception ex) { DLog.Force("舰队交战失败: " + ex.Message); return "命令失败: " + ex.Message; }
        }

        // ==================== v4.282: 进驻城市 / 离港(用户需求: 海军可以驻扎在城市内) ====================
        //
        // 【"驻扎在城市内"在这款游戏里到底是什么状态 —— 反编译实证】
        //   海上部队**不能**出现在城墙里: `MobileParty.SetSailAtPosition(CampaignVec2)`(公开)把
        //   `IsCurrentlyAtSea = true` + `Position = 参数`, 而 `Settlement.PortPosition` 在反序列化时就是
        //   `new CampaignVec2(pos, isOnLand: false)`(Settlement.cs 的 port_posX/port_posY)。
        //   DLC 自己的"进港/出海"用的也是这个点(`PortMenu` -> MainParty.SetSailAtPosition(CurrentSettlement.PortPosition))。
        //   ⇒ 本 mod 的"驻扎在城市内"= **停泊在该城的港口海面(泊位)**, 且
        //      ①地图位置就是该城的 PortPosition(不再随便漂着), ②登记为驻泊态,
        //      ③享受港口补给收益(维护 -35% + 每日付费修船)。这是这类部队在游戏里能达到的"在城内"。
        //
        // 【只在沿海城市】判据是 `Settlement.HasPort`(**装了海战 DLC 才有 true**: 港口坐标写在 DLC 的
        //   ModuleData\settlements.xml 的 port_posX/port_posY 上; 没装 DLC 时恒为 false ⇒ 本功能整体静默降级)。
        //   非沿海/非城镇/非自有的城市一律**明确拒绝并给中文原因**, 绝不静默忽略。
        //
        // 【到位判定】`ArriveRadius`(8)是"已经贴住泊位"的口径(与 TaskSync 的到位判定同源);
        //   `MoorSnapRadius`(40)是"允许直接挪到泊位"的口径 —— 已经在港口附近时, 用原版自己的
        //   `SetSailAtPosition(port.PortPosition)` 把部队精确放到泊位上(与 DLC 的"出海"同一手法);
        //   远在海上时**不许瞬移**, 只下"开往该港"的航海命令, 到了由 TaskSync 收敛成"驻泊"。
        internal const float MoorSnapRadius = 40f;

        // 进驻。ok 回填给玩家看的中文结果(与 Found 的约定一致: 返回 "" = 成功, 否则返回失败原因)
        internal static string Moor(Settlement port, out string ok) { return Moor(null, port, out ok); }

        internal static string Moor(MobileParty p, Settlement port, out string ok)
        {
            ok = "";
            try
            {
                if (!NavalBridge.Usable) return "未检测到海战 DLC —— 舰队功能整体停用";
                // 不指定部队(城市详情页只有一个"本城"上下文)时, 取当前唯一/第一支在编舰队
                if (p == null)
                {
                    var list0 = Alive();
                    if (list0.Count == 0) return "尚未组建舰队 —— 先到海军页[组建舰队]";
                    if (list0.Count > 1) return "有多支舰队, 请先在海军页[舰队]页签里选中要进驻的那一支";
                    p = PartyOf(list0[0]);
                }
                if (p == null) return "舰队不在场";
                var f = ForceOf(p);
                if (f == null) return "这不是舰队";
                string why = MoorBlockedReason(port);
                if (!string.IsNullOrEmpty(why)) return why;

                bool alreadyThere = DistTo(p, port) <= MoorSnapRadius;
                // 母港随进驻一并设为该城: "驻扎在这座城市"最直白的语义, 也让[回港]/解散有落点
                f.HomePortId = port.StringId;
                f.HomePortName = NavalBridge.NameOf(port);
                f.MooredPortId = port.StringId;
                f.MooredPortName = NavalBridge.NameOf(port);
                f.MoorDay = SafeToday();
                InvalidateMoored();   // v4.282: 折扣缓存立刻失效(进驻/换港后维护费要马上反映)

                EnsureAtSea(p, f);
                // 已经在港口附近 -> 用原版 SetSailAtPosition 精确落到该城泊位(PortPosition)
                if (alreadyThere) { try { p.SetSailAtPosition(port.PortPosition); } catch { } }
                // 无论远近都下同一条航海命令: 它把 TargetPosition/MoveTargetPoint 设成 PortPosition,
                //   于是"地图上的位置在该城港口海面"这件事由原版导航链保证(我们不自己推位置)。
                try { p.SetMoveGoToSettlement(port, MobileParty.NavigationType.Naval, true); } catch { }

                Arm(p, f);   // 收回指挥权 + "AI 不接管"
                NoteOrder(f, OrderKindMoor, NavalBridge.NameOf(port));
                NoteMoveSettlement(f, port, true);

                float d = DistTo(p, port);
                if (d <= ArriveRadius)
                {
                    f.Task = MoorTaskText(f.MooredPortName);
                    ok = MapSelection.NameOf(p) + " 已进驻「" + NavalBridge.NameOf(port) + "」停泊(船维护 -35%, 每日在港修理)";
                }
                else
                {
                    f.Task = TaskMooring;
                    ok = MapSelection.NameOf(p) + " 已奉命进驻「" + NavalBridge.NameOf(port) + "」, 全程约 "
                        + (int)d + " 距离 —— 到达后自动转为驻泊(维护 -35% + 每日修理)";
                }
                DLog.Force("海军: 舰队 " + f.PartyId + " 进驻「" + f.MooredPortName + "」(距 " + (int)d
                    + ", 到位=" + (d <= ArriveRadius ? "是" : "否") + ", 母港同步改为该城)");
                try { MapSelection.Message("海军: " + ok); } catch { }
                return "";
            }
            catch (Exception ex) { DLog.Force("海军: 进驻失败 " + ex.Message); return "进驻失败: " + ex.Message; }
        }

        // 进驻的合法性(全部返回中文原因; "" = 通过)。**面板点选与快速指令共用这一份** —— 拒绝理由不会两套。
        internal static string MoorBlockedReason(Settlement port)
        {
            try
            {
                if (port == null) return "没有选中任何城市";
                if (!port.IsTown) return "「" + NavalBridge.NameOf(port) + "」不是城镇 —— 海军只能进驻城镇";
                if (!port.HasPort) return "「" + NavalBridge.NameOf(port) + "」不是沿海城市(没有港口) —— 海军只能进驻沿海城市";
                var k = PlayerKingdom();
                if (k == null) return "尚未接管王国";
                var owner = NavalBridge.KingdomOf(port);
                if (owner == null) return "「" + NavalBridge.NameOf(port) + "」已无主, 不能进驻";
                if (!ReferenceEquals(owner, k))
                    return "「" + NavalBridge.NameOf(port) + "」属于「" + (owner.Name != null ? owner.Name.ToString() : "?")
                         + "」—— 只能进驻你自己的沿海城市";
                return "";
            }
            catch (Exception ex) { return "进驻校验异常: " + ex.Message; }
        }

        // 离港(解除驻泊)。母港保留 —— 舰队还知道该回哪去; 只是不再享受泊位收益。
        internal static string Unmoor(MobileParty p)
        {
            try
            {
                var f = ForceOf(p);
                if (f == null) return "这不是舰队";
                if (!f.HasMoor) return MapSelection.NameOf(p) + " 本来就没有进驻任何城市";
                string nm = string.IsNullOrEmpty(f.MooredPortName) ? "?" : f.MooredPortName;
                f.MooredPortId = ""; f.MooredPortName = ""; f.MoorDay = 0;
                InvalidateMoored();   // v4.282: 离港 -> 维护折扣立刻停止(面板与日结同源)
                if (IsMoorTask(f.Task) || f.Task == TaskMooring)
                {
                    f.Task = TaskStation;
                    var home = FindSettlement(f.HomePortId);
                    NoteOrder(f, OrderKindRecall, home != null ? NavalBridge.NameOf(home) : "");
                }
                DLog.Force("海军: 舰队 " + f.PartyId + " 离港(解除「" + nm + "」的驻泊, 母港保留)");
                string msg = MapSelection.NameOf(p) + " 已离开「" + nm + "」泊位 —— 转为在港待命(维护折扣与在港修理已停止)";
                try { MapSelection.Message("海军: " + msg); } catch { }
                return msg;
            }
            catch (Exception ex) { DLog.Force("海军: 离港失败 " + ex.Message); return "离港失败: " + ex.Message; }
        }

        // 驻扎在某城的舰队(城市详情页用; 按 PartyId 稳定序)
        internal static List<Force> MooredAt(Settlement port)
        {
            var list = new List<Force>();
            try
            {
                if (port == null || string.IsNullOrEmpty(port.StringId)) return list;
                var all = Alive();
                for (int i = 0; i < all.Count; i++)
                {
                    var f = all[i];
                    if (f != null && f.MooredPortId == port.StringId) list.Add(f);
                }
            }
            catch { }
            return list;
        }

        // 这支舰队是否"已经在泊位上"(驻泊态) —— 折扣/修理的唯一判据
        internal static bool InBerth(Force f)
        {
            try
            {
                if (f == null || !f.HasMoor) return false;
                var p = PartyOf(f);
                if (p == null || !p.IsActive) return false;
                var port = FindSettlement(f.MooredPortId);
                if (port == null || !port.HasPort) return false;
                if (!IsMoorTask(f.Task)) return false;      // 进驻中(在途)不算到位
                return DistTo(p, port) <= ArriveRadius + 12f;   // 留一点余量: 原版靠泊是"贴着点绕一下"
            }
            catch { return false; }
        }

        // 全部"已在泊位"的船 key(维护折扣用)。每次调用现算 —— 舰队数 ≤ MaxForces(4), 代价可忽略。
        internal static HashSet<int> MooredShipKeys()
        {
            var set = new HashSet<int>();
            try
            {
                for (int i = 0; i < Forces.Count; i++)
                {
                    var f = Forces[i];
                    if (f == null || !InBerth(f)) continue;
                    foreach (var kv in f.ShipOwner) if (kv.Key != 0) set.Add(kv.Key);
                }
            }
            catch { }
            return set;
        }

        // v4.282: 驻泊船集合的"批内缓存" —— 与 NavalBridge.MyShips() 的 10ms 窗口同一手法:
        //   `NavalFleet.DailyUpkeep` 会对**每一条我方船**调 ShipUpkeepOf, 而 InBerth 里的
        //   `PartyOf` 要扫一遍 MobileParty.All; 30 条船 × 4 支舰队 × 300 部队 = 3.6 万次比较,
        //   而海军页每 3 秒就要算一次日维护费。缓存后同一渲染/同一日结内只算一次。
        //   ★ 失效判据用"实时 10ms 窗口"而不是游戏日: 面板在地图暂停时也会刷新(CampaignTime 不动),
        //     而进驻/离港必须**立刻**反映到维护费上(两条写路径都会显式 Invalidate)。
        private const int MooredCacheMs = 10;
        private static HashSet<int> _mooredCache;
        private static int _mooredStamp;

        internal static void InvalidateMoored() { try { _mooredCache = null; } catch { } }

        private static HashSet<int> MooredShipKeysCached()
        {
            try
            {
                if (_mooredCache != null)
                {
                    int age = unchecked(Environment.TickCount - _mooredStamp);
                    if (age >= 0 && age < MooredCacheMs) return _mooredCache;
                }
            }
            catch { }
            var set = MooredShipKeys();
            try { _mooredCache = set; _mooredStamp = Environment.TickCount; } catch { }
            return set;
        }

        // 单船维护费(**含驻泊折扣**)—— 舰队维护费的唯一口径:
        //   `NavalFleet.DailyUpkeep`(日结真正扣钱的地方)与面板/小窗的"维护/日"都必须走这里,
        //   否则会出现"面板显示 100、日结扣 65"的口径分裂。
        internal static float ShipUpkeepOf(Ship s)
        {
            try
            {
                float v = NavalBridge.ShipUpkeep(s);
                if (v <= 0f) return v;
                int key = 0;
                try { key = NavalTransport.ShipKey(s); } catch { }
                if (key == 0) return v;
                return IsMooredShipKey(key) ? v * MooredUpkeepMul : v;
            }
            catch { return 0f; }
        }

        internal static bool IsMooredShipKey(int key)
        {
            try
            {
                if (key == 0) return false;
                // v4.282: 走批内缓存(见 MooredShipKeysCached 的注释) —— 每条船都现扫一遍部队表太贵。
                return MooredShipKeysCached().Contains(key);
            }
            catch { }
            return false;
        }

        // 面板/小窗用: 某舰队现在是不是驻泊(含"进驻中")
        internal static string MoorStateText(Force f)
        {
            try
            {
                if (f == null || !f.HasMoor) return "未进驻(点[进驻城市]选择要驻扎的沿海城市)";
                string nm = string.IsNullOrEmpty(f.MooredPortName) ? "?" : f.MooredPortName;
                if (f.Task == TaskMooring) return "进驻中 -> 「" + nm + "」";
                if (InBerth(f)) return "★已进驻「" + nm + "」泊位(船维护 -35%, 每日在港修理)";
                return "驻泊港「" + nm + "」(尚未到位)";
            }
            catch { return ""; }
        }

        // ==================== v4.282: 驻泊的每日收益之二 —— 在港修理 ====================
        //   由 `Tick()`(日结)调用。每支**已在泊位**的舰队每天最多修 `1 + 船坞等级/MoorRepairYardDiv` 艘
        //   (上限 MoorRepairShipCap), 挑最破的先修; 每艘按 DLC 的 `ShipCostModel.GetShipRepairCost`
        //   全额价从国库扣钱(国库不够就停在那一艘, 不赊账、不半修)。
        //   ★ 只修"在泊位"的: 进驻中(在途)不修 —— 修船必须在港。
        internal static void MooredRepairDaily()
        {
            LastMoorRepairShips = 0; LastMoorRepairCost = 0;
            try
            {
                if (!NavalBridge.Usable) return;
                var list = Alive();
                for (int i = 0; i < list.Count; i++)
                {
                    var f = list[i];
                    if (f == null || !InBerth(f)) continue;
                    var p = PartyOf(f);
                    var port = FindSettlement(f.MooredPortId);
                    if (p == null || port == null) continue;
                    int level = NavalBridge.ShipyardLevel(port);
                    int quota = 1 + (level > 0 ? level / MoorRepairYardDiv : 0);
                    if (quota > MoorRepairShipCap) quota = MoorRepairShipCap;

                    var ships = NavalBridge.ShipsOf(p);
                    // 挑最破的 N 艘(按"缺失耐久比例"降序; 船数很少, 直接选择排序)
                    for (int k = 0; k < quota; k++)
                    {
                        Ship worst = null; float worstMissing = 0f; float worstMax = 0f;
                        for (int j = 0; j < ships.Count; j++)
                        {
                            var s = ships[j];
                            if (s == null) continue;
                            float max = NavalBridge.MaxHitPoints(s), hp = NavalBridge.HitPoints(s);
                            if (max <= 0f) continue;
                            float missing = max - hp;
                            if (missing <= max * 0.02f) continue;          // 差不到 2% 视为完好, 不浪费额度
                            if (missing > worstMissing) { worstMissing = missing; worstMax = max; worst = s; }
                        }
                        if (worst == null) break;

                        // 价 = DLC 自己的修船价口径(与面板"修好需约 X 第纳尔"同一个函数)
                        PartyBase payer = null;
                        try { payer = MobileParty.MainParty != null ? MobileParty.MainParty.Party : null; } catch { }
                        int cost = 0;
                        try { cost = (int)Math.Round(NavalBridge.RepairCost(worst, payer)); } catch { }
                        if (cost < 0) cost = 0;

                        int gold = 0;
                        try { gold = EconomyWorld.Treasury.Gold; } catch { }
                        if (cost > gold)
                        {
                            DLog.Force("海军: 驻泊修理「" + f.MooredPortName + "」国库不足(需 " + cost + " 实有 " + gold
                                + "), 本日停修");
                            break;
                        }
                        if (cost > 0) { EconomyWorld.TreasurySpend(cost); Fiscal.AddNavy(cost); }
                        try { TaleWorlds.CampaignSystem.Actions.RepairShipAction.ApplyForFree(worst); }
                        catch (Exception exR) { DLog.Force("海军: 驻泊修理调用原版 Action 失败 " + exR.Message); }
                        LastMoorRepairShips++; LastMoorRepairCost += cost;
                        DLog.Force("海军: 驻泊修理 " + NavalBridge.HullName(worst) + "(舰队 " + f.PartyId
                            + " @「" + f.MooredPortName + "」)满修, 花费 " + cost + " 第纳尔(国库支付, DLC 修船价口径)");
                    }
                }
                if (LastMoorRepairShips > 0)
                {
                    try
                    {
                        MapSelection.Message("海军: 驻泊修理 —— 修复 " + LastMoorRepairShips + " 艘, 花费 "
                            + LastMoorRepairCost + " 第纳尔(计入财政[海军])");
                    }
                    catch { }
                }
            }
            catch (Exception ex) { DLog.Force("海军: 驻泊修理异常 " + ex.Message); }
        }

        // ==================== v4.284(第 2 项): 选中舰队的"构成"查询 ====================
        //   【为什么放在业务层而不是面板里】"一条船算哪个舰种档"必须与造船页/战力/维护费**同一口径**
        //   (NavalBridge.HullType -> DLC `ShipHull.Type`); 面板只负责排版。索引 0/1/2 = 轻/中/重,
        //   与 NavalBridge.HullTypeName 的取值一一对应(改那边必须同步这里)。
        internal static void HullBreakdown(MobileParty p, int[] count, int[] crew, float[] power, float[] upkeep)
        {
            try
            {
                for (int i = 0; i < 3; i++) { count[i] = 0; crew[i] = 0; power[i] = 0f; upkeep[i] = 0f; }
                var ships = NavalBridge.ShipsOf(p);
                for (int i = 0; i < ships.Count; i++)
                {
                    var s = ships[i];
                    if (s == null) continue;
                    int t = NavalBridge.HullType(s);
                    if (t < 0 || t > 2) t = 0;
                    count[t]++;
                    crew[t] += NavalBridge.Crew(s);              // 第 1 列 = 该档船的载员上限合计
                    power[t] += NavalBridge.ShipPowerScore(s);   // 第 3 列 = 战力(与舰队页行内同一函数)
                    upkeep[t] += ShipUpkeepOf(s);                // 第 4 列 = 日维护(含驻泊折扣, 与日结同源)
                }
            }
            catch { }
        }

        // v4.284(第 2 项): 舰种构成之外的两个数(载员上限/水手)由 **Stats** 直接给出, 面板不再另算 —— 
        //   `Stats` 的 crew = Σ 船 TotalCrewCapacity、sailors = DefArmy.RegularsOfForFleet,
        //   与水手军饷 DailySailorWage 同源; "缺员 = crew - sailors" 在面板里现减即可。

        // ==================== v4.284(第 3 项): 批量操作(全部召回 / 全部进驻母港) ====================
        //   与陆军页的 [全军回防]/[全军交还总监] 同形: 逐个尝试、**逐支如实报数**,
        //   跳过/失败一律在返回串里给出中文原因(用户要求"不要点了没反应")。
        internal static string RecallAllToHome()
        {
            try
            {
                if (!NavalBridge.Usable) return "未检测到海战 DLC —— 舰队功能整体停用";
                var list = Alive();
                if (list.Count == 0) return "尚未组建舰队 —— 先点[组建舰队]";
                int ok = 0, noHome = 0, skipped = 0, fail = 0;
                for (int i = 0; i < list.Count; i++)
                {
                    var p = PartyOf(list[i]);
                    if (p == null) { skipped++; continue; }
                    // 海运中的部队不归舰队口径(NavalTransport.Active 收的是军团), 但守卫是现成的, 显式跳过
                    try { if (NavalTransport.InConvoy(p.StringId)) { skipped++; continue; } } catch { }
                    var f = ForceOf(p);
                    var home = f != null ? FindSettlement(f.HomePortId) : null;
                    if (f == null) { skipped++; continue; }
                    if (home == null || !home.HasPort) { noHome++; continue; }   // 母港未知 -> OrderRecall 自己会给提示
                    string r = OrderRecall(p);
                    if (r != null && r.StartsWith("命令失败", StringComparison.Ordinal)) fail++;
                    else ok++;
                }
                string msg = "全部召回: 已命令 " + ok + " 支舰队回母港待命";
                if (noHome > 0) msg += " · " + noHome + " 支母港未知(右键该舰队选[指定母港…], 或直接点行内[回港]看提示)";
                if (skipped > 0) msg += " · " + skipped + " 支不在场/海运中跳过";
                if (fail > 0) msg += " · " + fail + " 支命令失败(详见日志)";
                if (ok == 0 && noHome == 0 && fail == 0) msg = "没有可召回的舰队(共 " + list.Count + " 支, 均不在场或海运中)";
                DLog.Force("海军: " + msg);
                return msg;
            }
            catch (Exception ex) { DLog.Force("海军: 全部召回异常 " + ex.Message); return "全部召回失败: " + ex.Message; }
        }

        // 全部进驻母港: 逐支走已有的 Moor(= 进驻城市, 全套校验 + 中文原因), **非沿海/非自有的一律跳过并计数**。
        internal static string MoorAllHomePorts()
        {
            try
            {
                if (!NavalBridge.Usable) return "未检测到海战 DLC —— 舰队功能整体停用";
                var list = Alive();
                if (list.Count == 0) return "尚未组建舰队 —— 先点[组建舰队]";
                int ok = 0, noHome = 0, blocked = 0, already = 0, skipped = 0, fail = 0;
                string lastWhy = "";
                for (int i = 0; i < list.Count; i++)
                {
                    var f = list[i];
                    var p = PartyOf(f);
                    if (p == null) { skipped++; continue; }
                    try { if (NavalTransport.InConvoy(p.StringId)) { skipped++; continue; } } catch { }
                    var home = FindSettlement(f.HomePortId);
                    if (home == null || !home.HasPort)
                    {
                        noHome++;
                        if (string.IsNullOrEmpty(lastWhy)) lastWhy = "母港未知或已不是沿海城市";
                        continue;
                    }
                    string why = MoorBlockedReason(home);           // 复用进驻的唯一一份判据(非沿海/非自有都给中文)
                    if (!string.IsNullOrEmpty(why)) { blocked++; lastWhy = why; continue; }
                    if (InBerth(f)) { already++; continue; }         // 已经在母港泊位里 -> 不必再下命令
                    string okMsg;
                    string err = Moor(p, home, out okMsg);
                    if (string.IsNullOrEmpty(err)) ok++; else { fail++; lastWhy = err; }
                }
                string msg = "全部进驻母港: " + ok + " 支已奉命进驻";
                if (already > 0) msg += " · " + already + " 支已在母港泊位";
                if (noHome > 0) msg += " · " + noHome + " 支母港未知";
                if (blocked > 0) msg += " · " + blocked + " 支被拒(" + lastWhy + ")";
                if (skipped > 0) msg += " · " + skipped + " 支不在场/海运中跳过";
                if (fail > 0) msg += " · " + fail + " 支失败(" + lastWhy + ")";
                if (ok == 0 && already == 0 && noHome == 0 && blocked == 0 && fail == 0)
                    msg = "没有可进驻的舰队(共 " + list.Count + " 支, 均不在场或海运中)";
                DLog.Force("海军: " + msg);
                return msg;
            }
            catch (Exception ex) { DLog.Force("海军: 全部进驻异常 " + ex.Message); return "全部进驻失败: " + ex.Message; }
        }

        // ==================== v4.284(第 4 项 ★): 花钱换属性 —— [海上演训] 与 [整备] ====================
        //   【海上演训】改的是**真实属性: MobileParty.Morale(舰队士气)**, 走原版 `RecentEventsMorale`
        //   (与国防军 DefArmy:1391/2737 同一条通道, 也与本文件欠饷哗变 :2120 的 `RecentEventsMorale -= 30`
        //   同一条通道)。落点如实说明:
        //     · 原版 MobileParty 体系: 士气参与地图遭遇/战斗的士气因子与部队行为(AI 是否接战/脱离);
        //     · 本 mod 面板: 舰队页 Summary / 状态行 / 构成条 / 地图小窗四处都读它(见 Stats 的 morale);
        //     · 与欠饷链条相连: 欠饷第 7 天起警告士气、第 14 天哗变减员并 -30 士气(见 SailorWages)。
        //   ★ **没有"训练度"**: 舰队(Force)里根本没有训练度字段 —— 陆军的训练度是 DefLegion.Train
        //     (0~100, 落点 = 战斗系数 ×0.8~1.2, 见 DefArmy.TrainLevelOf); 而 DefArmyStats.TrainingOfParty
        //     只是"按水手兵种熟练度算出的只读派生值", 海军没有任何结算读它。所以本次**不塞假训练度**。
        internal static int SeaDrillCost()
        {
            try
            {
                int men = NavalSailorsTotal();
                if (men <= 0) return 0;
                int c = men * SeaDrillPerSailor;          // 每名水手一笔, 人越多演训越贵
                if (c < SeaDrillMinCost) c = SeaDrillMinCost;
                c = (c + 9) / 10 * 10;                    // 取整到 10, 面板显示与扣款是同一个数
                return c;
            }
            catch { return 0; }
        }

        internal static string SeaDrill()
        {
            try
            {
                if (!NavalBridge.Usable) return "未检测到海战 DLC —— 舰队功能整体停用";
                var list = Alive();
                if (list.Count == 0) return "尚未组建舰队 —— 先点[组建舰队]";
                int cost = SeaDrillCost();
                if (cost <= 0) return "舰队没有水手 —— 无从演训(水手 = 舰队正兵, 见构成条)";
                // 先把"有几支真的能受益"数清楚: 士气已满的舰队不该让玩家白花钱
                int canGain = 0;
                for (int i = 0; i < list.Count; i++)
                {
                    var p = PartyOf(list[i]);
                    if (p == null) continue;
                    try { if (p.Morale < MoraleCap - 0.5f) canGain++; } catch { }
                }
                if (canGain == 0) return "全部舰队的士气都已经满了(" + (int)MoraleCap + ") —— 不必再演训";
                int gold = 0;
                try { gold = EconomyWorld.Treasury.Gold; } catch { }
                if (gold < cost) return "国库不足: 海上演训需 " + cost + " 第纳尔, 实有 " + gold + "(还差 " + (cost - gold) + ")";
                EconomyWorld.TreasurySpend(cost);
                Fiscal.AddMilitary(cost);   // 与陆军[阅兵]同一"军费"科目, 不另开科目(否则财政页口径失衡)
                int n = 0; float sumGain = 0f;
                for (int i = 0; i < list.Count; i++)
                {
                    var p = PartyOf(list[i]);
                    if (p == null) continue;
                    try
                    {
                        float cur = p.Morale;
                        if (cur >= MoraleCap - 0.5f) continue;
                        float gain = MoraleCap - cur;
                        if (gain > SeaDrillMoraleGain) gain = SeaDrillMoraleGain;
                        p.RecentEventsMorale += gain;   // 原版通道: 与日结的士气修正一起被 clamp 到 0~100
                        n++; sumGain += gain;
                    }
                    catch { }
                }
                string msg = "海上演训: " + n + " 支舰队士气 +" + (int)Math.Round(sumGain / Math.Max(1, n))
                    + "(各舰队封顶 " + (int)MoraleCap + ") · 花费 " + cost + " 第纳尔(财政[军费])";
                DLog.Force("海军: " + msg);
                try { MapSelection.Message("海军: " + msg); } catch { }
                return msg;
            }
            catch (Exception ex) { DLog.Force("海军: 海上演训异常 " + ex.Message); return "演训失败: " + ex.Message; }
        }

        // [整备] 的报价: 需修的船数 + 按 **DLC 自己的修船价**(ShipCostModel.GetShipRepairCost, 经
        //   NavalBridge.RepairCost)算出的总价 —— 与驻泊每日修理、以及舰队页行内"修好需约 X"完全同源。
        internal static void OverhaulQuote(out int needShips, out int needCost)
        {
            needShips = 0; needCost = 0;
            try
            {
                if (!NavalBridge.Usable) return;
                PartyBase payer = null;
                try { payer = MobileParty.MainParty != null ? MobileParty.MainParty.Party : null; } catch { }
                var ships = NavalBridge.MyShips();   // 我方口径(与舰队页列表同一份船)
                for (int i = 0; i < ships.Count; i++)
                {
                    var s = ships[i];
                    if (s == null) continue;
                    if (NavalBridge.HealthRatio(s) >= 0.99f) continue;
                    needShips++;
                    needCost += (int)Math.Round(NavalBridge.RepairCost(s, payer));
                }
                if (needCost < 0) needCost = 0;
            }
            catch { }
        }

        // [整备]: 一次性把**我方全部舰船**修到满(与驻泊每日修理同一套: 国库付费 + RepairShipAction.ApplyForFree)。
        //   ★ 与"驻泊每日自动修理"的去重(两条, 都是结构性而非提示性的):
        //     ① 判据不同: 驻泊日修只对 **已在泊位(InBerth)** 的舰队生效、每日额 = 1 + 船坞等级/2(≤3 艘)、
        //        且跳过"缺失 ≤ 2%"的船(MooredRepairDaily); [整备] 是玩家主动付费的一次性服务,
        //        覆盖全部我方船(含在航的), 修完完整度 ≥ 99%。
        //     ② 修完即互斥: 同一艘船被 [整备] 修满之后, 当日驻泊日修的 `missing <= max*0.02` 判据必然跳过它
        //        —— 不存在"同一艘船同一天被扣两次钱"。两笔支出在时间与科目上都可分辨(都记财政[海军])。
        internal static string OverhaulAll()
        {
            try
            {
                if (!NavalBridge.Usable) return "未检测到海战 DLC —— 舰队功能整体停用";
                int needShips, cost;
                OverhaulQuote(out needShips, out cost);
                if (needShips <= 0) return "我方全部舰船都已完好(完整度 ≥ 99%) —— 无需整备";
                if (cost <= 0) return "需整备 " + needShips + " 艘, 但 DLC 未给出修船价(船体数据缺失) —— 未扣款";
                int gold = 0;
                try { gold = EconomyWorld.Treasury.Gold; } catch { }
                if (gold < cost) return "国库不足: 整备需 " + cost + " 第纳尔(修 " + needShips + " 艘), 实有 " + gold
                                        + "(还差 " + (cost - gold) + ") —— 可先让舰队进驻自有港口, 每日自动付费修理少量船";
                EconomyWorld.TreasurySpend(cost);
                Fiscal.AddNavy(cost);   // 与驻泊修理同一科目(财政[海军]) —— 两笔支出在同一处可核对
                int fixedShips = 0;
                PartyBase payer = null;
                try { payer = MobileParty.MainParty != null ? MobileParty.MainParty.Party : null; } catch { }
                var ships = NavalBridge.MyShips();
                for (int i = 0; i < ships.Count; i++)
                {
                    var s = ships[i];
                    if (s == null) continue;
                    if (NavalBridge.HealthRatio(s) >= 0.99f) continue;
                    try { TaleWorlds.CampaignSystem.Actions.RepairShipAction.ApplyForFree(s); fixedShips++; }
                    catch (Exception exR) { DLog.Force("海军: 整备调用原版 Action 失败 " + exR.Message); }
                }
                LastOverhaulShips = fixedShips; LastOverhaulCost = cost;
                string msg = "整备完成: 修满 " + fixedShips + " 艘(全舰队), 花费 " + cost
                    + " 第纳尔(财政[海军], 按 DLC 修船价) · 今日驻泊自动修理不会再重复修这些船";
                DLog.Force("海军: " + msg);
                try { MapSelection.Message("海军: " + msg); } catch { }
                return msg;
            }
            catch (Exception ex) { DLog.Force("海军: 整备异常 " + ex.Message); return "整备失败: " + ex.Message; }
        }

        // ==================== 解散(船还回原主) ====================
        internal static string Disband(MobileParty p)
        {
            try
            {
                var f = ForceOf(p);
                if (f == null) return "这不是舰队";
                int back = ReturnShips(p, f);
                var home = FindSettlement(f.HomePortId);
                Forces.Remove(f);
                // 先把船还掉再拆部队: 原版 DestroyPartyAction 会把部队**名下剩下的船**一并摧毁
                RetireCaptain(p != null ? p.LeaderHero : null);
                try { if (p != null && p.IsActive) DestroyPartyAction.ApplyForDisbanding(p, home); }
                catch (Exception ex) { DLog.Force("舰队: 解散部队失败 " + ex.Message); }
                DLog.Force("海军: 解散舰队 " + f.PartyId + " —— 归还舰船 " + back + " 艘");
                return "舰队已解散, " + back + " 艘舰船已归还";
            }
            catch (Exception ex) { DLog.Force("舰队解散失败: " + ex.Message); return "解散失败: " + ex.Message; }
        }

        private static int ReturnShips(MobileParty p, Force f)
        {
            int n = 0;
            try
            {
                var keys = new List<int>(f.ShipOwner.Keys);
                var ships = NavalBridge.ShipsOf(p);
                for (int i = 0; i < ships.Count; i++)
                {
                    var s = ships[i];
                    if (s == null) continue;
                    int key = NavalTransport.ShipKey(s);
                    string ownerId = "";
                    f.ShipOwner.TryGetValue(key, out ownerId);
                    var target = FindPartyBase(ownerId);
                    if (target == null) target = SafeMainPartyBase();
                    if (target == null) continue;
                    try { ChangeShipOwnerAction.ApplyByTransferring(target, s); n++; }
                    catch (Exception ex) { DLog.Force("舰队: 还船失败 " + NavalBridge.HullName(s) + " " + ex.Message); }
                }
                for (int i = 0; i < keys.Count; i++) f.ShipOwner.Remove(keys[i]);
            }
            catch { }
            return n;
        }

        // 部队被歼灭/船被击沉时的收尾(日结调用)
        internal static void OnShipDestroyed(PartyBase owner, Ship ship, DestroyShipAction.ShipDestroyDetail detail)
        {
            try
            {
                if (owner == null || ship == null) return;
                if (!owner.IsMobile || owner.MobileParty == null) return;
                if (!IsTaskForce(owner.MobileParty)) return;
                var f = ForceOf(owner.MobileParty);
                if (f == null) return;
                int key = NavalTransport.ShipKey(ship);
                if (key == 0 || !f.ShipOwner.ContainsKey(key)) return;
                f.ForgetShip(key);
                DLog.Force("海军: 舰队 " + f.PartyId + " 损失 1 艘船(" + NavalBridge.HullName(ship) + "), 已从账上划掉");
            }
            catch { }
        }

        // ==================== 孤儿收养 ====================
        //   场景: 存档 F= 段坏掉(按段容错把它跳过了) / 被外部系统清过账 / 早期测试残留 —— 地图上
        //   确实有一支 fia_navy_ 部队, 但 Forces 里没有它。后果是它变成"没人管的残骸":
        //   玩家点它下令会被判成"这不是舰队", 原版 AI 又因为 DoNotMakeNewDecisions 不碰它。
        //   这里补一条登记, 让它重新可被指挥(宁可收养, 不要留死部队)。
        //
        //   ==================== v4.277(第 3 项): 修"母港推断"缺陷 ====================
        //   【原缺陷】原来直接拿 `p.HomeSettlement` 当母港。反编译实证它**不是**港口:
        //     · MobileParty.HomeSettlement(MobileParty.cs:627-641)
        //         => _customHomeSettlement(非空优先) ?? _partyComponent.HomeSettlement;
        //     · LordPartyComponent.HomeSettlement(LordPartyComponent.cs:77) => **Owner.HomeSettlement**;
        //     · Hero.HomeSettlement 是"这位英雄的家乡聚落"(可能是村庄/城堡/内陆城, 不是港口)。
        //     舰队的司令是 DefArmy.MakeCaptainFor(port, ...) 生成的特殊英雄, 他的 HomeSettlement
        //     只是**碰巧**可能等于母港 —— 收养路径上完全可能指向一个内陆聚落。
        //   【后果】记录里 HomePortId 指向非港口 -> 之后 [回港](OrderRecall)/[巡航](OrderPatrol)
        //     会返回"母港已不存在"(不崩, 但玩家得重新下令, 而且看不出为什么)。
        //   【修法 —— 三档降级, 全部留痕】(按"越可靠越优先"):
        //     ① HomeSettlement 本身就是港口 -> 用它(与旧行为一致, 老档结果不变);
        //     ② 否则取**我方(玩家王国)自有港口里离舰队最近的一处** —— 收养的前提是这支舰队
        //        本来就属于玩家的海军, 就近回自家港口最符合"母港"的语义(港口优先于距离,
        //        因为母港必须是自家港, 否则 [回港] 之后又会被 Tick 里的"母港易主"逻辑清掉);
        //     ③ 玩家王国一处港口都没有(或全都被别的舰队占用) -> 退而取"舰队当前位置最近的港口",
        //        不限于自家(至少舰队有个可回、可补给、可巡航的落点, 而且这是**推断值**)。
        //     ④ 连一处港口都找不到 -> **留空**(HomePortId=""), 不猜:
        //        面板/指挥条/小窗会显示"母港未知", [回港] 会提示"请右键[指定母港…]重新指定"。
        //   ★ 无论走哪一档, 日志都写明"推断来源", 收养消息也不再谎报"已重新纳入编制"了事。
        private static void Adopt()
        {
            try
            {
                var all = MobileParty.All;
                if (all == null) return;
                for (int i = 0; i < all.Count; i++)
                {
                    var p = all[i];
                    if (p == null || !p.IsActive) continue;
                    if (!IsTaskForce(p)) continue;
                    if (ForceOf(p) != null) continue;
                    if (Forces.Count >= MaxForces) return;
                    AdoptOne(p.StringId);   // v4.278(第 8 项): 收养本体抽成单支版本, 与 InCommand 的顺手登记共用
                }
            }
            catch { }
        }

        // v4.278(第 8 项): **单支**收养(Adopt 与 InCommand 共用) —— 按部队 id 就地补一条登记记录。
        //   与批量版(Adopt)的差别只有两点: ①按 id 找部队(调用方手上只有 id) ②不再扫全表。
        //   返回是否真的登记成功(已登记/找不到部队/已达上限都返回 false, 调用方不需要区分)。
        internal static bool AdoptOne(string partyId)
        {
            try
            {
                if (string.IsNullOrEmpty(partyId)) return false;
                if (ForceOf(partyId) != null) return true;      // 已登记 = 目的已达成
                if (Forces.Count >= MaxForces) return false;
                MobileParty p = null;
                try
                {
                    var all = MobileParty.All;
                    if (all != null)
                        for (int i = 0; i < all.Count; i++)
                        {
                            var q = all[i];
                            if (q == null || q.StringId != partyId) continue;
                            p = q; break;
                        }
                }
                catch { }
                if (p == null || !p.IsActive || !IsTaskForce(p)) return false;

                string how;
                var home = InferHomePort(p, out how);
                var f = new Force
                {
                    PartyId = p.StringId,
                    HomePortId = home != null ? home.StringId : "",
                    HomePortName = home != null ? NavalBridge.NameOf(home) : "",
                    FoundedDay = SafeToday(),
                    // ★ 与批量收养一致: Task=驻港 / PlayerHold=true。PlayerHold 必须是 true ——
                    //   恢复出来的残骸部队若默认"已交还 AI", 它会立刻被原版派单系统拉去别处, 玩家反而更懵。
                    Task = TaskStation,
                    PlayerHold = true
                };
                Forces.Add(f);
                DLog.Force("海军: 收养了一支未登记的舰队 " + p.StringId
                    + "(母港 " + (home != null ? f.HomePortName : "未知") + " · " + how + ")");
                try
                {
                    MapSelection.Message(home != null
                        ? "海军: 发现一支未登记的舰队, 已重新纳入编制(母港推断为「" + f.HomePortName + "」)"
                        : "海军: 发现一支未登记的舰队, 已重新纳入编制 —— 但母港未知, 请右键它选[指定母港…]");
                }
                catch { }
                return true;
            }
            catch { return false; }
        }

        // v4.277(第 3 项): 母港推断(三档降级 + 留痕)。how 回填"这一档是怎么选的", 供日志/面板说明。
        internal static Settlement InferHomePort(MobileParty p, out string how)
        {
            how = "";
            try
            {
                // ① 部队自己的 HomeSettlement 就是港口
                Settlement hs = null;
                try { hs = p != null ? p.HomeSettlement : null; } catch { }
                if (hs != null && hs.HasPort)
                {
                    how = "取自部队 HomeSettlement(本身是港口)";
                    return hs;
                }

                var k = PlayerKingdom();
                // ② 我方自有港口里最近的一处(被别的舰队占用也允许 —— 收养是恢复, 不该因为它没空位就留空)
                Settlement best = null;
                float bestD = float.MaxValue;
                var mine = NavalBridge.PortsOf(k);
                for (int i = 0; i < mine.Count; i++)
                {
                    var s = mine[i];
                    if (s == null || !s.HasPort) continue;
                    float d = DistTo(p, s);
                    if (d < bestD) { bestD = d; best = s; }
                }
                if (best != null)
                {
                    how = "HomeSettlement " + (hs != null ? "非港口" : "为空")
                        + " -> 改为**我方最近港口**(距 " + (int)bestD + ")";
                    return best;
                }

                // ③ 任意港口里最近的一处(不限于自家; 只作为可回可补给的落点, 且明确标注是推断)
                var any = NavalBridge.AllPorts();
                best = null; bestD = float.MaxValue;
                for (int i = 0; i < any.Count; i++)
                {
                    var s = any[i];
                    if (s == null || !s.HasPort) continue;
                    float d = DistTo(p, s);
                    if (d < bestD) { bestD = d; best = s; }
                }
                if (best != null)
                {
                    how = "我方暂无可用港口 -> 改为**全图最近港口**(距 " + (int)bestD + ", 可能非我方, 属推断值)";
                    return best;
                }

                how = "全图没有港口 -> **留空**(面板显示[母港未知], 可由玩家重新指定)";
                return null;
            }
            catch (Exception ex)
            {
                how = "推断异常(" + ex.Message + ") -> 留空";
                return null;
            }
        }

        // 舰队到某港的直线距离(容错: 任一侧坐标缺失/无效时返回一个很大的值, 让它排在最后而不是抢第一)
        private static float DistTo(MobileParty p, Settlement s)
        {
            try
            {
                if (p == null || s == null || !s.HasPort) return float.MaxValue;
                return p.Position.Distance(s.PortPosition);
            }
            catch { return float.MaxValue; }
        }

        // ==================== 日结: 存活校验 + 船空了自动解散 ====================
        internal static void Tick()
        {
            try
            {
                Adopt();   // v4.276: 先把"地图上有、账上没有"的孤儿舰队收养回来
                if (Forces.Count == 0) return;
                for (int i = Forces.Count - 1; i >= 0; i--)
                {
                    var f = Forces[i];
                    if (f == null) { Forces.RemoveAt(i); continue; }
                    var p = PartyOf(f);

                    // 部队被歼灭/被原版移除 -> 船已经随之蒸发, 只能把记录清掉(并如实记日志)
                    if (p == null || !p.IsActive || p.Party == null)
                    {
                        DLog.Force("海军: 舰队 " + f.PartyId + " 已不存在, 记录清除(编入时 " + f.ShipOwner.Count + " 艘船)");
                        MapSelection.Message("舰队「" + f.HomePortName + "」已不存在, 编制注销");
                        Forces.RemoveAt(i);
                        continue;
                    }

                    // 船被全部击沉 -> 这支部队已经没有海上能力了, 就地注销(否则它会变成非法海上部队)
                    int ships = 0;
                    try { ships = NavalBridge.ShipsOf(p).Count; } catch { }
                    if (ships <= 0)
                    {
                        DLog.Force("海军: 舰队 " + f.PartyId + " 已无船, 自动解散");
                        MapSelection.Message("舰队「" + f.HomePortName + "」舰船尽失, 编制注销");
                        // 已经没船了, ReturnShips 自然是 0 艘; 这里只需把残部收掉
                        RetireCaptain(p.LeaderHero);
                        try { DestroyPartyAction.ApplyForDisbanding(p, FindSettlement(f.HomePortId)); } catch { }
                        Forces.RemoveAt(i);
                        continue;
                    }

                    // 母港易主 -> 记录里把母港清掉(舰队还在, 只是没母港了; 玩家可重新下令)
                    var home = FindSettlement(f.HomePortId);
                    if (home == null || !home.HasPort)
                    {
                        f.HomePortId = ""; f.HomePortName = "";
                    }
                    // v4.282: 驻泊港易主/失效 -> 一并解除驻泊(否则"折扣挂在别人的港口上")
                    //   判据与 TaskSync 的 ⓪ 分支同源: 没有港口 / 已不属于玩家王国(玩家王国为空时不判归属)
                    if (f.HasMoor)
                    {
                        var mp2 = FindSettlement(f.MooredPortId);
                        var mine2 = PlayerKingdom();
                        if (mp2 == null || !mp2.HasPort
                            || (mine2 != null && !ReferenceEquals(NavalBridge.KingdomOf(mp2), mine2)))
                        {
                            string gone2 = string.IsNullOrEmpty(f.MooredPortName) ? "?" : f.MooredPortName;
                            f.MooredPortId = ""; f.MooredPortName = ""; f.MoorDay = 0;
                            InvalidateMoored();
                            DLog.Force("海军: 舰队 " + f.PartyId + " 的驻泊港「" + gone2 + "」已失效/易主 -> 日结自动离港");
                        }
                    }

                    // 不变量自愈: 每次日结把"AI 不接管"的标志补回来(见 Reassert 注释)
                    Reassert(p);
                }

                // v4.277(第 1 项): 收水手军饷 —— 放在**存活校验之后**(已注销的舰队不该再收钱),
                //   与陆军 Daily() 里 PayWages() 的位置语义一致(先对账, 再发饷)。
                SailorWages();
                // v4.282: 驻泊的每日收益之二 —— 在港付费修船(修船价走 DLC 口径, 钱从国库出)。
                //   放在军饷之后: 军饷是必须付的, 修船是有钱才修的(国库不足时停修, 不赊账)。
                try { MooredRepairDaily(); } catch (Exception exR) { DLog.Force("海军: 驻泊修理日结异常 " + exR.Message); }
                // v4.278(第 6 项): 任务终态**也在日结对一次账** —— 出题人要求"到达/超时/中断/召回等所有终态
                //   都要把 Task 更新成正确值", 小时结已经覆盖绝大多数场景(舰队的移动以小时为粒度),
                //   这里再兜一次, 是为了"游戏被暂停/长时间没走小时结"这类极端情况下面板不会停留在旧值。
                TaskSync();
            }
            catch (Exception ex) { DLog.Force("海军: 舰队日结异常 " + ex.Message); }
        }

        // ==================== v4.278(第 6 项): 小时结入口 ====================
        //   由 `NavalBehavior.OnHourlyTick` 调用(与 DiagTick 并列, 但**不受 navydiag 开关影响** ——
        //   任务对账是功能而不是诊断, 关闭诊断时也必须跑)。
        internal static void HourlySync()
        {
            try { TaskSync(); }
            catch (Exception ex) { DLog.Force("海军: 舰队任务对账异常 " + ex.Message); }
        }

        // ==================== v4.277(第 1 项): 水手军饷 ====================
        //   口径 = 与陆军一字不差: Σ(各舰队正兵数) × 0.5/兵/日 × 科研军饷系数, 向上取整。
        //   证据链(全部 file:line 实证, 见下面 WageRate/RegularsOfForFleet 的取值顺序):
        //     · DefArmy.cs:69   `internal const float WagePerManPerDay = 0.5f;`      <- 唯一单价来源
        //     · DefArmy.cs:251-270 DailyWageCost():
        //         sum += RegularsOf(p) * WagePerManPerDay * WageMultOf(lg);  return Ceiling(sum)
        //     · DefArmy.cs:1224-1235 RegularsOf(p) = p.MemberRoster.TotalRegulars(不含英雄)
        //     · DefArmy.cs:2059-2065  WageMultOf(lg) = (操练?1.3:1) × (1 - Research.WageSave(k))
        //   舰队的**两处刻意差异**(如实写明, 不假装完全一致):
        //     ① 舰队没有"操练/休整"这类军团状态(v4.243 的 Stance 只存在于 DefLegion), 所以不吃 ×1.3;
        //     ② 科研系数照吃 —— 研究"军事统计"时陆军和海军的军饷**一起**降(同一段因果, 见转发注释)。
        //   ★ 军饷按 RegularsOf(正兵)算, 与陆军一致; 船的载员(capacity)不是人头, 不参与计费。
        internal static float WageRate()
        {
            try
            {
                float per = DefArmy.WagePerManPerDayForFleet();
                if (per <= 0f) per = SailorWagePerManPerDay;      // 转发取不到时的兜底(同值)
                float save = DefArmy.ResearchWageSaveForFleet();
                if (save < 0f) save = 0f;
                if (save > 0.9f) save = 0.9f;
                return per * (1f - save);
            }
            catch { return SailorWagePerManPerDay; }
        }

        // 单支舰队的日军饷(第纳尔/日)
        internal static int DailySailorWage(MobileParty p)
        {
            try
            {
                if (p == null) return 0;
                int men = DefArmy.RegularsOfForFleet(p);
                if (men <= 0) return 0;
                double sum = men * (double)WageRate();
                if (sum <= 0.0) return 0;
                return (int)Math.Ceiling(sum);
            }
            catch { return 0; }
        }

        // 全部在编舰队的日军饷合计(供面板"船维护 X + 水手军饷 Y = 合计 Z"的 Y 项)
        internal static int NavalWageDaily()
        {
            try
            {
                int sum = 0;
                var list = Alive();
                for (int i = 0; i < list.Count; i++) sum += DailySailorWage(PartyOf(list[i]));
                return sum;
            }
            catch { return 0; }
        }

        // 全部在编舰队的水手合计(面板要显示"多少人在吃饷", 否则玩家无法核对 Y 是怎么来的)
        internal static int NavalSailorsTotal()
        {
            try
            {
                int sum = 0;
                var list = Alive();
                for (int i = 0; i < list.Count; i++)
                {
                    var p = PartyOf(list[i]);
                    if (p != null) sum += DefArmy.RegularsOfForFleet(p);
                }
                return sum;
            }
            catch { return 0; }
        }

        // 日结: 收水手军饷(与 DefArmy.PayWages 同结构: 够钱 -> 扣国库 + 记军费科目 + 清零欠饷;
        //   不够 -> 欠饷天数 +1, 第 7 天警告士气, 第 14 天哗变减员)。
        //   ★ 钱走 EconomyWorld.Treasury(与陆军同一条国库入口), 科目走 Fiscal.AddMilitary(与陆军同一个"军费"科目)。
        //     为什么不新开科目: Fiscal 的军事支出只有 Military 一项, 财政页/月结/今日净额三处都按它算;
        //     另开一个"海军军饷"科目就必须同时改 Expense/TodayExpense/Month 三处才不失衡, 风险大于收益。
        //     面板上用**同一科目里的拆解说明**做到"分开显示"(见 NavyPanelVM/FiscalVM), 见报告说明。
        internal static void SailorWages()
        {
            try
            {
                var list = Alive();
                for (int i = 0; i < list.Count; i++)
                {
                    var f = list[i];
                    var p = PartyOf(f);
                    if (p == null) continue;
                    int want = DailySailorWage(p);
                    if (want <= 0) { f.UnpaidDays = 0; continue; }

                    int gold = EconomyWorld.Treasury.Gold;
                    int pay = Math.Min(want, Math.Max(0, gold));
                    if (pay > 0)
                    {
                        EconomyWorld.TreasurySpend(pay);
                        Fiscal.AddMilitary(pay);      // ← 与陆军军饷同一个科目(见上面长注释)
                    }

                    if (pay >= want)
                    {
                        f.UnpaidDays = 0;
                        continue;
                    }

                    // 欠饷: 与陆军 PayWages 同一套节奏(7 天警告 / 14 天哗变)
                    f.UnpaidDays++;
                    if (f.UnpaidDays == SailorMutinyWarnDay)
                    {
                        try { p.RecentEventsMorale = p.RecentEventsMorale - 30f; } catch { }
                        MapSelection.Message("舰队「" + (string.IsNullOrEmpty(f.HomePortName) ? "母港未知" : f.HomePortName)
                            + "」欠水手军饷 " + f.UnpaidDays + " 天: 士气 -30%, 再不补发将哗变!");
                    }
                    if (f.UnpaidDays >= SailorMutinyDay)
                    {
                        int men = DefArmy.RegularsOfForFleet(p);
                        int desert = Math.Max(1, men / 10);
                        int got = 0;
                        try { got = DefArmy.RemoveRegularsForFleet(p, desert); } catch { }
                        MapSelection.Message("舰队「" + (string.IsNullOrEmpty(f.HomePortName) ? "母港未知" : f.HomePortName)
                            + "」水手哗变: " + got + " 人离舰");
                        f.UnpaidDays = 0;
                    }
                    DLog.Force("海军: 舰队 " + f.PartyId + " 欠水手军饷(需 " + want + " 实付 " + pay
                        + " 第纳尔, 连续 " + f.UnpaidDays + " 天)");
                }
            }
            catch (Exception ex) { DLog.Force("海军: 水手军饷结算失败 " + ex.Message); }
        }

        // ==================== v4.277(第 4 项): 舰队移动诊断 ====================
        //   【为什么需要它】上一轮最大的单点风险 = `DoNotMakeNewDecisions(true)` 在 1.4.8 下
        //   是否真的不冻结移动。"船在不在动"这个观察**不可靠**(顺风/环流/AI 兜底都可能让它挪一下),
        //   而真正的判据只有一个: `MobileParty.PartyMoveMode`(public 字段, MoveModeType.Hold/Point/Party)
        //   有没有被原版导航链放出来变成 Point —— 见 NavalTaskForceAi 的长注释(唯一入口
        //   MobilePartyAi.UpdateBehavior -> SetNavigationModePoint)。
        //   【怎么开】flags 文件加 `navydiag`(默认关闭, 零开销: 关闭时本函数第一行就返回)。
        //   【多久打一次】小时结调用一次, 用游戏内秒(CampaignTime.Now.ToSeconds)节流;
        //     间隔 = flags 里的 `navydiagsec=N`(缺省 3600 游戏秒 = 1 游戏日)。
        private static double _diagNextSec = -1.0;

        internal static void DiagTick()
        {
            try
            {
                if (!DLog.Flag(DiagFlag)) return;              // 默认关闭: 不读 flags 之外的任何东西
                double now = CampaignTime.Now.ToSeconds;
                if (_diagNextSec > 0.0 && now < _diagNextSec) return;
                float gap = DLog.FlagFloat(DiagSecKey, DiagSecDefault);
                if (gap < 60f) gap = 60f;                      // 下限 1 游戏分钟, 免得刷爆日志
                _diagNextSec = now + gap;
                DiagLog();
            }
            catch { }
        }

        // 一行/舰队(格式与项目现有日志风格一致: "海军: ..." 前缀 + 空格分隔的键值)
        internal static void DiagLog()
        {
            try
            {
                // ★ DLC 不可用时**不 early-return**: "舰队一出生就没有海上能力"恰恰是最该被诊断出来的一种
                //   失败(此前的舰队层会整体静默降级), 所以这里只加一行显著提示, 记录照打。
                if (!NavalBridge.Usable)
                    DLog.Force("舰队诊断: ★未检测到海战 DLC(NavalBridge.Usable=false) —— 下面若有记录, 说明是**读档残留**, 舰队不会动");
                var list = Alive();
                DLog.Force("舰队诊断: ===== 第 " + SafeToday() + " 天 · 游戏秒 " + (int)CampaignTime.Now.ToSeconds
                    + " · 在编舰队 " + list.Count + " 支 · 水手军饷 " + NavalWageDaily() + "/日 =====");
                if (list.Count == 0)
                {
                    // 未在编的"地图上有、账上没有"残骸也要报出来 —— 那正好是收养逻辑该管的东西
                    int stray = 0;
                    try
                    {
                        var all = MobileParty.All;
                        for (int i = 0; i < all.Count; i++)
                        {
                            var q = all[i];
                            if (q == null || !q.IsActive || !IsTaskForce(q)) continue;
                            if (ForceOf(q) == null) stray++;
                        }
                    }
                    catch { }
                    DLog.Force("舰队诊断: 当前没有在编舰队" + (stray > 0 ? "(但地图上有 " + stray + " 支未登记残骸!)" : ""));
                    return;
                }
                for (int i = 0; i < list.Count; i++) DiagLogOne(list[i]);
            }
            catch (Exception ex) { DLog.Force("舰队诊断失败: " + ex.Message); }
        }

        private static void DiagLogOne(Force f)
        {
            try
            {
                if (f == null) return;
                var p = PartyOf(f);
                if (p == null) { DLog.Force("舰队诊断: " + f.PartyId + " 记录在册但部队不在场"); return; }

                string nm = MapSelection.NameOf(p);
                // ★ 最关键的两个字段: PartyMoveMode(Hold=原地不动 / Point=原版已放出推进目标) + IsCurrentlyAtSea
                string mode = "?";
                try { mode = p.PartyMoveMode.ToString(); } catch { }
                string atSea = "?";
                try { atSea = p.IsCurrentlyAtSea ? "是" : "否"; } catch { }

                // 距离: Position -> TargetPosition(= 我们下命令时设的目标点, SetMoveGoToPoint/Settlement 都设它)
                float dist = -1f;
                try { dist = p.Position.Distance(p.TargetPosition); } catch { }
                // 与上一条"点到点"的命令比: 目标点没变而距离在缩 = 真的在走; 距离不变 = 停住了
                //   ★ 顺序要紧: 先用**上一条**的 LastDist 算方向, 再把它更新成本次的 dist。
                float prev = f.LastDist;
                string dir = dist < 0f ? "距离不可用"
                    : (prev < 0f ? "首次采样"
                        : (dist < prev - 1f ? "在靠近(" + (int)(prev - dist) + ")"
                            : (dist > prev + 1f ? "在远离(+" + (int)(dist - prev) + ")" : "距离未变")));
                f.LastDist = dist;

                string tgt = "-";
                try { if (p.TargetSettlement != null) tgt = NavalBridge.NameOf(p.TargetSettlement); } catch { }
                if (tgt == "-") { try { if (p.TargetParty != null) tgt = MapSelection.NameOf(p.TargetParty); } catch { } }

                string beh = "?";
                try { beh = p.DefaultBehavior.ToString(); } catch { }
                string noDec = "?";
                try { noDec = p.Ai.DoNotMakeNewDecisions ? "是" : "否"; } catch { }
                string order = string.IsNullOrEmpty(f.OrderKind) ? "(无记录)" : f.OrderKind;
                string orderTgt = string.IsNullOrEmpty(f.OrderTarget) ? "-" : f.OrderTarget;
                string orderDay = f.OrderDay > 0 ? ("第" + f.OrderDay + "天") : "(未知)";

                // v4.277(第 4 项 ★): 自动判定 —— 用户实机验证时**一眼**就能看出结论, 不必自己解读字段。
                //   判据(把"到港驻泊时的 Hold"与"该走却不动时的 Hold"分开, 否则会误报):
                //     · 还在执行**移动类**任务(远征/封锁/交战/巡航) 却 PartyMoveMode == Hold
                //       -> ★疑似被冻结(这正是要验证的那件事);
                //     · 驻港 且 距目标很近 -> "已到位驻泊"(这是正常的 Hold, 不是冻结);
                //     · PartyMoveMode == Point -> 原版已放行推进, 再看距离方向就知道走没走。
                bool movingTask = f.Task != TaskStation && !IsMoorTask(f.Task);
                string verdict;
                if (mode == "Point")
                    verdict = "原版已放行(Point)" + (dir.StartsWith("在靠近") ? " · 确实在走" : " · 但距离没在缩, 看是否刚下令");
                else if (movingTask && dist > 20f)
                    verdict = "★疑似被冻结: 任务[" + f.Task + "] 但 PartyMoveMode=Hold —— 见报告里的开关(UseNativeNoDecision)";
                else if (!movingTask && dist <= 20f)
                    verdict = "已到位驻泊(正常的 Hold)";
                else
                    verdict = "PartyMoveMode=" + mode + "(既非 Point 也非移动任务, 请结合命令时间判断)";

                DLog.Force("舰队诊断: " + nm
                    + " | id=" + f.PartyId
                    + " | 任务=" + f.Task
                    + " | PlayerHold=" + (f.PlayerHold ? "1" : "0")
                    + " | 不接管=" + noDec
                    + " | 海上=" + atSea
                    + " | ★PartyMoveMode=" + mode
                    + " | 行为=" + beh
                    + " | 目标点=" + (tgt == "-" ? "无" : tgt)
                    + " | 距目标=" + (dist < 0f ? "?" : ((int)dist).ToString()) + "(" + dir + ")"
                    + " | 命令=" + order + "->" + orderTgt + "@" + orderDay
                    + " | 母港=" + (string.IsNullOrEmpty(f.HomePortName) ? "未知" : f.HomePortName)
                    + " | 舰=" + NavalBridge.ShipsOf(p).Count
                    + " 水手=" + DefArmy.RegularsOfForFleet(p)
                    + " 军饷=" + DailySailorWage(p) + "/日"
                    + " | 位置=(" + (int)p.Position.X + "," + (int)p.Position.Y + ")"
                    + " | 判定=" + verdict);
            }
            catch (Exception ex) { DLog.Force("舰队诊断: 单支舰队输出失败 " + ex.Message); }
        }

        // ==================== 属性(小窗/面板显示) ====================
        //   v4.277: 增加 out sailorWage(日军饷) —— "船维护 + 水手军饷 = 合计"里的第二项。
        //   保留原来的 8 参数重载(调用点不用全改), 新代码请用带 sailorWage 的这个。
        internal static void Stats(MobileParty p, out int shipCount, out float power, out float hpRatio,
            out int crew, out int sailors, out float morale, out float upkeep)
        {
            float wage;
            Stats(p, out shipCount, out power, out hpRatio, out crew, out sailors, out morale, out upkeep, out wage);
        }

        internal static void Stats(MobileParty p, out int shipCount, out float power, out float hpRatio,
            out int crew, out int sailors, out float morale, out float upkeep, out float sailorWage)
        {
            bool moored;
            Stats(p, out shipCount, out power, out hpRatio, out crew, out sailors, out morale, out upkeep,
                out sailorWage, out moored);
        }

        // v4.282: 再补一个 out moored(是否已在泊位) —— 面板要显示"驻泊折扣已生效", 不想再多查一次 Force。
        internal static void Stats(MobileParty p, out int shipCount, out float power, out float hpRatio,
            out int crew, out int sailors, out float morale, out float upkeep, out float sailorWage,
            out bool moored)
        {
            shipCount = 0; power = 0f; hpRatio = 1f; crew = 0; sailors = 0; morale = 50f; upkeep = 0f;
            sailorWage = 0f; moored = false;
            try
            {
                if (p == null) return;
                var ships = NavalBridge.ShipsOf(p);
                shipCount = ships.Count;
                float hp = 0f, hpMax = 0f;
                bool berth = InBerth(ForceOf(p));
                for (int i = 0; i < ships.Count; i++)
                {
                    var s = ships[i];
                    if (s == null) continue;
                    power += NavalBridge.ShipPowerScore(s);
                    hp += NavalBridge.HitPoints(s);
                    hpMax += NavalBridge.MaxHitPoints(s);
                    crew += NavalBridge.Crew(s);
                    // v4.282: 走 ShipUpkeepOf(**含驻泊折扣**) —— 与 NavalFleet.DailyUpkeep(日结真正扣钱的
                    //   地方)同一口径, 面板上的"日耗"才不会与国库实际扣款对不上。
                    upkeep += ShipUpkeepOf(s);
                }
                moored = berth;
                if (hpMax > 0f) hpRatio = Math.Max(0f, Math.Min(1f, hp / hpMax));
                try { sailors = DefArmy.RegularsOf(p); } catch { }
                try { morale = DefArmyStats.MoraleOf(p.Party); } catch { }
                // ★ upkeep 只统计**船维护**(NavalFleet.DailyUpkeep 对 MyShips 求和时, 舰队名下的船
                //   已经天然算在里面 —— 见 NavalBridge.MyShips 的 v4.276 注释)。
                //   v4.277(第 1 项): **水手军饷不在这里**, 它由 sailorWage 单独给出 —— 面板把两项
                //   分开显示("船维护 X + 水手军饷 Y = 合计 Z"), 因为"养一支舰队花多少"必须看得清构成。
                sailorWage = DailySailorWage(p);
            }
            catch { }
        }

        // 舰队一行简介(面板/小窗共用)
        internal static string Summary(MobileParty p)
        {
            try
            {
                int ships, crew, sailors;
                float power, ratio, morale, upkeep, wage;
                bool moored;
                Stats(p, out ships, out power, out ratio, out crew, out sailors, out morale, out upkeep, out wage, out moored);
                var f = ForceOf(p);
                bool noHome = f == null || string.IsNullOrEmpty(f.HomePortId);
                return "舰 " + ships + " 艘 · 战力 " + (int)power + " · 完整度 " + (int)Math.Round(ratio * 100f) + "%"
                    + " · 水手 " + sailors + "/" + crew + " · 士气 " + (int)morale
                    // v4.277(第 1 项): 明处拆开 —— 船维护 + 水手军饷 = 合计(与海军页舰队页汇总行同一口径)
                    + " · 日耗 船维护 " + upkeep.ToString("0.#") + " + 水手军饷 " + wage.ToString("0.#")
                    + " = " + (upkeep + wage).ToString("0.#") + " 第纳尔/日"
                    + " · " + (f != null ? f.Task : "?")
                    // v4.282: 驻泊态必须在**小窗/面板同一行**看得出来(用户要求"看得见"), 并写明折扣已生效
                    + (moored && f != null ? " 于「" + (string.IsNullOrEmpty(f.MooredPortName) ? "?" : f.MooredPortName)
                        + "」(船维护已 -" + (int)Math.Round((1f - MooredUpkeepMul) * 100f) + "%)" : "")
                    // v4.278(第 4 项): 交还后的舰队必须**一眼能看出**它不归你管了(否则玩家会以为命令失灵)
                    + (f != null && !f.PlayerHold ? " · ★已交还海军部(自主)" : "")
                    // v4.277(第 3 项): 母港推断失败时不能显示成"驻港 · 母港 ?"了事, 要明说未知
                    + (noHome ? " · ★母港未知(请右键[指定母港…])" : "");
            }
            catch { return "?"; }
        }

        // ==================== 工具 ====================
        internal static Kingdom PlayerKingdom()
        {
            try
            {
                var b = NationalWillOrders.Behavior;
                return b != null ? b.NationKingdom : null;
            }
            catch { return null; }
        }

        internal static int SafeToday()
        {
            try { return Politics.Today(); } catch { return 0; }
        }

        internal static Settlement FindSettlement(string id)
        {
            try { return string.IsNullOrEmpty(id) ? null : DefArmy.FindSettlement(id); }
            catch { return null; }
        }

        private static PartyBase SafeMainPartyBase()
        {
            try { return MobileParty.MainParty != null ? MobileParty.MainParty.Party : null; }
            catch { return null; }
        }

        // PartyBase.Id = MobileParty.StringId(部队) / Settlement.StringId(聚落港口)
        internal static PartyBase FindPartyBase(string id)
        {
            try
            {
                if (string.IsNullOrEmpty(id)) return null;
                var mp = MobileParty.MainParty;
                if (mp != null && mp.StringId == id) return mp.Party;
                var p = DefArmy.FindParty(id);
                if (p != null && p.Party != null) return p.Party;
                var s = FindSettlement(id);
                if (s != null) return s.Party;
            }
            catch { }
            return null;
        }

        private static void RetireCaptain(Hero h)
        {
            try
            {
                if (h == null || h.IsDead) return;
                h.ChangeState(Hero.CharacterStates.Disabled);
            }
            catch { }
        }

        // ==================== 存档段(F=) ====================
        //   ★ 字段顺序 = 存档格式: 只能**在末尾追加**, 且读侧必须同时放宽最小字段数判定。
        //   0 PartyId | 1 HomePortId | 2 HomePortName | 3 FoundedDay | 4 Task | 5 PlayerHold
        //   | 6 OrderPortId | 7 OrderDay | 8 Ships(key:ownerId,key:ownerId,...)
        //   | 9 OrderKind | 10 OrderTarget          <- v4.277(第 4 项 诊断) 追加
        //   | 11 UnpaidDays | 12 OrderSettlementId | 13 OrderPointX | 14 OrderPointY  <- v4.278 追加
        //   | 15 OrderTargetPartyId                                                     <- v4.278 追加(交战目标)
        //   ★ 追加字段一律"只在末尾 + 读侧判长度缺省"(老档缺这些项 -> 0 / 空串, 零副作用):
        //     · UnpaidDays(追加 2): 欠饷计时原来**不落盘**, 于是读档即归零 ⇒ "欠饷第 7 天警告 /
        //       第 14 天哗变减员"永远不触发, 玩家反复读档就能永久免疫哗变。现在落盘。
        //     · OrderSettlementId / OrderPointX,Y(第 6/7 项): 命令的"可重放形状" ——
        //       被原版 `SetMoveModeHold`(聚落易主回调)冻住后要靠它恢复, 到达判定也要用它。
        //       老档没有 -> 空串/0 ⇒ 只是"不能自动恢复/自动判定到位", 不影响舰队存在与指挥。
        private const int FS_PartyId = 0;
        private const int FS_HomePortId = 1;
        private const int FS_HomePortName = 2;
        private const int FS_FoundedDay = 3;
        private const int FS_Task = 4;
        private const int FS_PlayerHold = 5;
        private const int FS_OrderPortId = 6;
        private const int FS_OrderDay = 7;
        private const int FS_Ships = 8;
        private const int FS_OrderKind = 9;
        private const int FS_OrderTarget = 10;
        private const int FS_UnpaidDays = 11;
        private const int FS_OrderSettlementId = 12;
        private const int FS_OrderPointX = 13;
        private const int FS_OrderPointY = 14;
        private const int FS_OrderTargetPartyId = 15;
        // v4.282: 驻泊(进驻城市) —— 追加 3 项
        //   16 MooredPortId | 17 MooredPortName | 18 MoorDay
        //   ★ 老档(16 项)读不到 -> 空串/0 = "未驻泊", 与"从来没进驻过"完全等价(折扣/修理都不生效),
        //     不报错、不算坏段(与上面 v4.277/v4.278 的追加字段同一套约定)。
        private const int FS_MooredPortId = 16;
        private const int FS_MooredPortName = 17;
        private const int FS_MoorDay = 18;
        private const int ForceFieldCountWrite = 19;
        private const int ForceFieldCountMin = 6;   // 少于它就是残缺记录 -> 丢弃(并记日志)

        internal static string Serialize()
        {
            var sb = new StringBuilder();
            try
            {
                for (int i = 0; i < Forces.Count; i++)
                {
                    var f = Forces[i];
                    if (f == null || string.IsNullOrEmpty(f.PartyId)) continue;
                    var ships = new StringBuilder();
                    foreach (var kv in f.ShipOwner)
                    {
                        if (kv.Key == 0) continue;
                        if (ships.Length > 0) ships.Append(',');
                        ships.Append(kv.Key).Append(':').Append(Escape(kv.Value ?? ""));
                    }
                    sb.Append(";F=").Append(Escape(f.PartyId)).Append('|').Append(Escape(f.HomePortId))
                      .Append('|').Append(Escape(f.HomePortName)).Append('|').Append(f.FoundedDay)
                      .Append('|').Append(Escape(f.Task)).Append('|').Append(f.PlayerHold ? 1 : 0)
                      .Append('|').Append(Escape(f.OrderPortId)).Append('|').Append(f.OrderDay)
                      .Append('|').Append(ships.ToString())
                      .Append('|').Append(Escape(f.OrderKind)).Append('|').Append(Escape(f.OrderTarget))
                      // v4.278 追加 4 项(见上面的字段表)
                      .Append('|').Append(f.UnpaidDays)
                      .Append('|').Append(Escape(f.OrderSettlementId))
                      .Append('|').Append(((int)f.OrderPointX))
                      .Append('|').Append(((int)f.OrderPointY))
                      .Append('|').Append(Escape(f.OrderTargetPartyId))
                      // v4.282 追加 3 项(驻泊: 港口 id / 港口名 / 进驻那天)
                      .Append('|').Append(Escape(f.MooredPortId))
                      .Append('|').Append(Escape(f.MooredPortName))
                      .Append('|').Append(f.MoorDay);
                }
            }
            catch (Exception ex) { DLog.Force("海军: 舰队序列化失败 " + ex.Message); }
            return sb.ToString();
        }

        // 返回值 = 该条是否真的读进来了(false -> Load 计一个坏段, 但**不中断**其余段)
        internal static bool LoadSegment(string body)
        {
            var f = body.Split('|');
            if (f.Length < ForceFieldCountMin)
                throw new FormatException("舰队段字段数不足(" + f.Length + " < " + ForceFieldCountMin + ")");
            var rec = new Force
            {
                PartyId = Unescape(f[FS_PartyId]),
                HomePortId = Unescape(f[FS_HomePortId]),
                HomePortName = Unescape(f[FS_HomePortName]),
                FoundedDay = f.Length > FS_FoundedDay ? NavalFleet.ParseInt(f[FS_FoundedDay]) : 0,
                Task = f.Length > FS_Task ? Unescape(f[FS_Task]) : TaskStation,
                PlayerHold = f.Length > FS_PlayerHold ? NavalFleet.ParseInt(f[FS_PlayerHold]) != 0 : true,
                OrderPortId = f.Length > FS_OrderPortId ? Unescape(f[FS_OrderPortId]) : "",
                OrderDay = f.Length > FS_OrderDay ? NavalFleet.ParseInt(f[FS_OrderDay]) : 0,
                // v4.277: 老档(9 字段)读不到这两项 -> 空串(诊断日志显示"(无记录)"), 不报错、不算坏段
                OrderKind = f.Length > FS_OrderKind ? Unescape(f[FS_OrderKind]) : "",
                OrderTarget = f.Length > FS_OrderTarget ? Unescape(f[FS_OrderTarget]) : "",
                // v4.278: 老档(11 字段)读不到这四项 -> 0/空串(见字段表注释), 不报错、不算坏段
                UnpaidDays = f.Length > FS_UnpaidDays ? NavalFleet.ParseInt(f[FS_UnpaidDays]) : 0,
                OrderSettlementId = f.Length > FS_OrderSettlementId ? Unescape(f[FS_OrderSettlementId]) : "",
                // v4.278: 老档(15 字段)读不到 -> 空串(只是"交战被冻住后无法自动恢复", 不影响指挥)
                OrderTargetPartyId = f.Length > FS_OrderTargetPartyId ? Unescape(f[FS_OrderTargetPartyId]) : "",
                // v4.282: 老档(16 字段)读不到这 3 项 -> 空串/0 = 未驻泊(等价于"从来没进驻过", 零副作用)
                MooredPortId = f.Length > FS_MooredPortId ? Unescape(f[FS_MooredPortId]) : "",
                MooredPortName = f.Length > FS_MooredPortName ? Unescape(f[FS_MooredPortName]) : "",
                MoorDay = f.Length > FS_MoorDay ? NavalFleet.ParseInt(f[FS_MoorDay]) : 0
            };
            if (f.Length > FS_OrderPointX || f.Length > FS_OrderPointY)
            {
                float px = f.Length > FS_OrderPointX ? NavalFleet.ParseInt(f[FS_OrderPointX]) : 0;
                float py = f.Length > FS_OrderPointY ? NavalFleet.ParseInt(f[FS_OrderPointY]) : 0;
                // ★ 只有"确实存过一个海面点"才算数: 老档两项都是 0, 而 (0,0) 在地图上是合法坐标 ——
                //   所以判据用"两项至少一个非 0"(海面点恰好落在原点附近时最多是"不恢复", 不会乱跑)。
                rec.OrderPointX = px; rec.OrderPointY = py;
                rec.HasOrderPoint = (px != 0f || py != 0f);
            }
            if (string.IsNullOrEmpty(rec.PartyId)) throw new FormatException("舰队段缺少部队 id");
            if (string.IsNullOrEmpty(rec.Task)) rec.Task = TaskStation;
            if (f.Length > FS_Ships && !string.IsNullOrEmpty(f[FS_Ships]))
            {
                var items = f[FS_Ships].Split(',');
                for (int i = 0; i < items.Length; i++)
                {
                    var it = items[i];
                    if (string.IsNullOrEmpty(it)) continue;
                    int c = it.IndexOf(':');
                    if (c <= 0) continue;
                    int key = NavalFleet.ParseInt(it.Substring(0, c));
                    if (key == 0) continue;
                    rec.ShipOwner[key] = Unescape(it.Substring(c + 1));
                }
            }
            // v4.276: 老档(没有 F= 段) -> 根本不会进这里; 从**更新版本**读到的多余字段一律忽略。
            for (int i = 0; i < Forces.Count; i++)
                if (Forces[i] != null && Forces[i].PartyId == rec.PartyId) return true;   // 重复段: 保留先到的
            Forces.Add(rec);
            return true;
        }

        internal static void Reset()
        {
            try
            {
                Forces.Clear();
                _diagNextSec = -1.0;   // v4.277(第 4 项): 诊断节流计时也要重置(新战役第一次小时结就该出日志)
                // v4.278(第 9 项): 出击冷却一并复位(新战役/返回主菜单重开时不该继承上一局的"补给中")
                _sortieReadySec = -1.0;
                // v4.282: 驻泊修理的昨日概况(纯显示口径, 新战役必须归零) + 折扣缓存失效
                LastMoorRepairShips = 0; LastMoorRepairCost = 0;
                InvalidateMoored();
            }
            catch { }
        }

        internal static string Escape(string s)
        {
            try { return NavalFleet.Escape(s); } catch { return s ?? ""; }
        }

        internal static string Unescape(string s)
        {
            try { return NavalFleet.Unescape(s); } catch { return s ?? ""; }
        }
    }
}
