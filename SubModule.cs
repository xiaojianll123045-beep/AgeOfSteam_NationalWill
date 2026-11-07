using System;
using System.Collections.Generic;
using Bannerlord.UIExtenderEx;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Localization;   // v4.261: 主菜单教程入口要用 TextObject
using TaleWorlds.MountAndBlade;

namespace FeudalInternalAffairs
{
    public class SubModule : MBSubModuleBase
    {
        internal static Harmony HarmonyInstance;

        // v4.200: RTS 指挥系统开关(用户要求暂时移除; 改回 true 即恢复)
        internal const bool RtsEnabled = false;

        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();
            try
            {
                HarmonyInstance = new Harmony("FeudalInternalAffairs");
                // 先屏蔽外部单独安装的 RTS Camera(如果玩家装了), 只保留本模块内置版
                BanStandaloneRts.Apply(HarmonyInstance);
                var patches = new List<Type>
                {
                    typeof(StageSkipPatches.StageSkip),
                    typeof(StageSkipPatches.SkipCultureStage.NextStagePatch),
                    typeof(IntroSkipPatches.SkipStartupVideo),
                    typeof(IntroSkipPatches.SkipCampaignIntro),
                    typeof(NationSelectionPatches.NationList),
                    typeof(NationSelectionPatches.NationSelect),
                    typeof(MapBarPatches.MapBarHidePatch),
                    typeof(MapBarPatches.MapBarHideTickPatch),
                    typeof(MapBarPatches.MapBarVMDressHideTick),
                    typeof(MapBarPatches.MapBarVMDressHideRefresh),
                    typeof(FeudalMapViewPatch),
                    typeof(FocusTreeMapGuardPatch.BlockMapNavigationInput),
                    typeof(FocusTreeMapGuardPatch.BlockMapCameraInput),
                    typeof(FocusTreeMapGuardPatch.BlockGameKeyPressed),
                    typeof(FocusTreeMapGuardPatch.BlockGameKeyDown),
                    typeof(FocusTreeMapGuardPatch.BlockGameKeyReleased),
                    typeof(FocusTreeMapGuardPatch.BlockGameKeyDownImmediate),
                    typeof(FocusTreeMapGuardPatch.BlockHotKeyPressed),
                    typeof(FocusTreeMapGuardPatch.BlockHotKeyDown),
                    typeof(FocusTreeMapGuardPatch.BlockHotKeyReleased),
                    typeof(FocusTreeMapGuardPatch.BlockHotKeyDoublePressed),
                    typeof(FocusTreeMapGuardPatch.BlockGameKeyState),
                    typeof(FocusTreeMapGuardPatch.BlockGameKeyAxis),
                    typeof(NationalWillHotkeyPatches.BlockInventory),
                    typeof(NationalWillHotkeyPatches.BlockParty),
                    typeof(NationalWillHotkeyPatches.BlockClan),
                    typeof(NationalWillHotkeyPatches.BlockQuests),
                    typeof(NationalWillHotkeyPatches.BlockCharacter),
                    typeof(NationalWillHotkeyPatches.BlockFaceGen),
                    typeof(NationalWillHotkeyPatches.BlockBannerEditor),
                    typeof(MapClickPatches.GroundClick),
                    typeof(MapClickPatches.PartyClick),
                    typeof(MapClickPatches.SettlementClick),
                    typeof(MapClickPatches.NoEncyclopediaDuringPick),
                    typeof(MapClickPatches.NoClickTimeChange),
                    typeof(MapClickPatches.BlockOutlinerWheelZoom),
                    typeof(MapVisionPatches.NationalWillVision),
                    typeof(SettlementNameplateRelationPatch.SyncOnRefreshValues),
                    typeof(SettlementNameplateRelationPatch.SyncOnDynamicProperties),
                    typeof(TerritoryColorMode.HideNameplatesWhenZoomed),
                    typeof(MapCameraPatches.AfterSceneReady),
                    typeof(MapCameraPatches.NeverFollowPartyMode),
                    typeof(MapCameraPatches.NeverFollowPartyViaSetCameraMode),
                    typeof(MapCameraPatches.NoManualRotation),
                    typeof(MapCameraPatches.NoClickCameraMove),
                    typeof(MapCameraPatches.RedirectButtons),
                    typeof(MapCameraPatches.NoLeftDragPan),
                    typeof(MapCameraPatches.NoScreenFastMoveToMainParty),
                    typeof(MapCameraPatches.NoScreenTeleportToMainParty),
                    typeof(MapCameraPatches.NoViewFastMoveToMainParty),
                    typeof(MapCameraPatches.NoViewTeleportToMainParty),
                    typeof(MapCursorPatches.CursorVisibilityPatch),
                    typeof(MapCursorPatches.FakeRightKeyPatch),
                    typeof(MapCursorPatches.NoCursorLockPatch),
                    typeof(BattleRtsCamera.CameraTickPatch),
                    typeof(BattleRtsCamera.FinalizePatch),
                    typeof(MuteBattleNotifications.MuteLevelUp),
                    typeof(MuteBattleNotifications.MuteQuickInformation),
                    typeof(MuteBattleNotifications.MuteSceneNotification),
                    typeof(HidePlayerPartyRow.RemovePlayerPartyRow),
                    typeof(NationalWillClan.ZombieClanKingdomPatch),
                    typeof(NoAiControlPatches.BlockAiArmy),
                    // 注意: BlockWarPeaceDecision(拦截 Kingdom.AddDecision) 会破坏原版决策系统(投票后崩溃), 已停用;
                    //       战争/和平仍在动作层拦截(BlockDeclareWar / BlockMakePeace), 效果相同且安全
                    //typeof(NoAiControlPatches.BlockWarPeaceDecision),
                    typeof(NoAiControlPatches.BlockDeclareWar),
                    typeof(NoAiControlPatches.BlockMakePeace),
                    typeof(DefArmyPatches.PartySizeLimitPatch),
                    typeof(DefArmyPatches.DefArmyNoNativeWagePatch),   // v4.252: 国防军不吃原版军饷(军饷由 mod 收)
                    // v4.290: 下面两个 patch DefaultClanFinanceModel.AddPartyExpense 的类**不在此注册** ——
                    //   它们改到 OnGameStart 注册(理由见 OnGameStart 里"税收接管补丁"那段长注释:
                    //   启动期处理 DefaultClanFinanceModel 会提前触发它的静态初始化, 而它的静态字段依赖
                    //   Game.Current.GameTextManager -> TypeInitializationException -> 该类型永久不可用)。
                    typeof(DefArmyPatches.SpeedLockPatch),
                    typeof(DiplomacyPatches.AlwaysCanDeclareWar),
                    typeof(DiplomacyPatches.DeclareWarDirectly),
                    typeof(DiplomacyPatches.PeaceByEnemyVote),
                    typeof(MapInfoSelectionPatch.OverrideInfoBase),
                    // v4.235 地图名板人数 = 名册 − 将军(修 100 兵军团显示 101)
                    typeof(PartyNameplateCountPatch),
                    // 28.7 悬停劫持: 原版部队 tooltip 的兵种列表 -> 我们的
                    typeof(PartyTooltipPatch.TroopListReplace),
                    typeof(PartyTooltipPatch.RefreshPartyTooltip),
                    typeof(KingdomTraitPatches.Speed),
                    typeof(KingdomTraitPatches.TownProsperity),
                    typeof(KingdomTraitPatches.TownLoyalty),
                    typeof(KingdomTraitPatches.RaidLoot),
                    typeof(KingdomTraitPatches.RangedDamage),
                    typeof(KingdomTraitPatches.TerrainAttack.Missile),
                    typeof(KingdomTraitPatches.TerrainAttack.Swing),
                    typeof(KingdomTraitPatches.TerrainAttack.Thrust),
                    typeof(KingdomTraitPatches.AutoResolveStrength),
                    typeof(KingdomTraitPatches.KhuzaitForeignRecruit),
                    typeof(KingdomTraitPatches.VlandiaMercenaryIncome),
                    typeof(KingdomTraitPatches.VlandiaMercenaryHire.KingdomScore),
                    typeof(KingdomTraitPatches.VlandiaMercenaryHire.ClanScore),
                    typeof(KingdomTraitPatches.VlandiaPersuasion),
                    typeof(KingdomTraitPatches.AseraiTradeIncome.Caravan),
                    typeof(KingdomTraitPatches.AseraiTradeIncome.Workshop),
                    typeof(KingdomTraitPatches.AseraiTradeIncome.Tariffs),
                    typeof(KingdomTraitPatches.AseraiArcherAccuracy),
                    typeof(ProductionTakeover.SkipVillageGoods),
                    typeof(ProductionTakeover.SkipVillageFood),
                    typeof(ProductionTakeover.FoodRatio),
                    typeof(ProductionTakeover.TownFoodSource),
                    typeof(MessageListShift),
                    typeof(PanelInputGuard.ChatLogHandleInputPatch),
                    typeof(PanelInputGuard.InputMousePressedPatch),
                    typeof(PanelInputGuard.InputMouseReleasedPatch),
                    typeof(PanelInputGuard.AnyMultiInquiryShownPatch),
                    typeof(NotificationFilter.DisplayMessageFilter),
                    typeof(DefArmyAi.AiGuard),
                    typeof(DefArmyAi.NoAutoRecruit),
                    // v4.276: 舰队的"AI 守卫"(舰队必须能走, 又必须不被 AI 接管 —— 见 Navy\NavalTaskForceAi.cs)
                    typeof(NavalTaskForceAi.NoInitiative),
                    typeof(NavalTaskForceAi.LockBehavior),
                    // v4.278(第 1 项 ★ P0): 水手工钱补丁 —— v4.277 写好了类却**漏登记**, 等于没打:
                    //   原版照收舰队陆战部队工资(500 水手约 1200/日) + FIA 再收 250/日,
                    //   且原版那笔不记 Fiscal 科目 ⇒ 面板只显示 250、国库实际少更多。与国防军那条(上面 :97)并列。
                    typeof(NavalTaskForceAi.NoNativeWage),
                    // v4.278(第 2 项 ★ P0): 就算工资清零了, 原版每天仍会从国库搬 5000/舰队 去"补足"舰队长的
                    //   私人金库(反编译证据见该类长注释)。这条在源头整方法跳过, 只对 fia_navy_ 生效。
                    typeof(NavalTaskForceAi.NoNativePartyExpense),
                    // 28.3 禁止原版军队入口(招募/升级/组建军团/解散劫持)
                    typeof(VanillaArmySuppress.HideTownRecruitMenu),
                    typeof(VanillaArmySuppress.HideVillageRecruitMenu),
                    typeof(VanillaArmySuppress.BlockRecruitScreen),
                    typeof(VanillaArmySuppress.BlockPlayerRecruitApply),
                    typeof(VanillaArmySuppress.BlockPlayerRecruitMap),
                    typeof(VanillaArmySuppress.BlockPlayerRecruitIndividual),
                    typeof(VanillaArmySuppress.BlockUpgrade),
                    typeof(VanillaArmySuppress.GrayUpgrade),
                    typeof(VanillaArmySuppress.BlockPlayerArmyButton),
                    typeof(VanillaArmySuppress.BlockPlayerCreateArmy),
                    typeof(VanillaArmySuppress.HijackClanDisband)
                };
                int ok = 0;
                foreach (var t in patches)
                {
                    try
                    {
                        // v4.200: 暂时移除 RTS 指挥系统(相机/终结补丁不注册)
                        if (!RtsEnabled && (t == typeof(BattleRtsCamera.CameraTickPatch) || t == typeof(BattleRtsCamera.FinalizePatch)))
                        {
                            DLog.Force("RTS 已暂时移除, 跳过补丁 " + t.Name);
                            continue;
                        }
                        HarmonyInstance.CreateClassProcessor(t).Patch();
                        ok++;
                    }
                    catch (Exception ex) { DLog.Force("补丁失败 " + t.Name + ": " + ex.Message); }
                }
                DLog.Force("蒸汽时代：国家意志 加载完成, 补丁 " + ok + "/" + patches.Count);
                // v4.241: 装备/兵种 -> 科技树 门控自检(错一个 id 就等于那件装备永不开放)
                try
                {
                    string bad = Equipment.Validate();
                    if (string.IsNullOrEmpty(bad)) DLog.Force("装备科技校验: 39 件装备 + 14 兵种全部对齐科技表");
                    else DLog.Force("装备科技校验: 以下 id 在科技表里不存在 -> " + bad);
                }
                catch (Exception ex) { DLog.Force("装备科技校验异常: " + ex.Message); }
                KingdomTraitPatches.NavalTraitPatches.Apply(HarmonyInstance);
                NavalVisualGuard.Apply(HarmonyInstance);
                // v4.277(审计 A10): 海上巡逻收益 —— 给 DLC 的 NavalSettlementPatrolModel.GetPatrolPartySpawnDuration
                //   挂 Postfix(玩家王国港口巡逻队刷新周期 ÷ 巡逻科技系数)。DLC 缺失时内部直接跳过。
                try { NavalPatrol.Apply(HarmonyInstance); } catch (Exception ex) { DLog.Force("海军巡逻补丁异常: " + ex.Message); }
                // v4.278(第 1 项): 水手工钱补丁的**双保险** —— DLC 的 NavalDLCPartyWageModel.GetTotalWage
                //   内部确实 `BaseModel.GetTotalWage(...)` 转调基类(反编译实证, 所以上面的 NoNativeWage 会触发),
                //   但两个 Postfix 的先后不由我们决定, 且 DLC 那个 override 会拿 `totalWage.BaseNumber` 做除法。
                //   这里用**反射**(NavalDLC.* 一律反射)在 DLC 类型上再挂一个 Prefix, 命中就直接跳过整段计算。
                //   ★ 本调用是幂等的: 若此刻 NavalDLC.dll 还没加载(找不到类型)它只打一行日志,
                //     OnGameStart 时会再调一次补齐 —— 所以这里失败不是错误。
                try { NavalTaskForceAi.ApplyDlcWageGuard(HarmonyInstance); }
                catch (Exception ex) { DLog.Force("海军工资双保险补丁异常: " + ex.Message); }
                // v4.268: 海战 DLC 探测(纯反射探测 NavalDLC.dll 的类型, 不硬引用、不写进 SubModule.xml 依赖)
                //   没装 DLC -> 整条海军功能静默降级(海军页显示"未检测到海战 DLC"), 其它系统完全不受影响
                try
                {
                    if (NavalBridge.DlcLoaded)
                        DLog.Force("海战 DLC: 已检测到 NavalDLC(海军层启用; 港口 " + NavalBridge.AllPorts().Count + " 处)");
                    else
                        DLog.Force("海战 DLC: 未安装 -> 海军功能整体降级(不影响其它系统)");
                }
                catch (Exception ex) { DLog.Force("海战 DLC 探测异常(按未安装处理): " + ex.Message); }
                TerritoryColorMode.NameplateWidgetPatch.Apply(HarmonyInstance);
                MapInfoSelectionPatch.OverrideInfoNaval.Apply(HarmonyInstance);

                try
                {
                    var extender = UIExtender.Create("AgeOfSteam_NationalWill");
                    DLog.Info("UIExtender：Create 完成");
                    extender.Register(typeof(SubModule).Assembly);
                    DLog.Info("UIExtender：Register 完成");
                    extender.Enable();
                    DLog.Force("UIExtenderEx 已启用");
                }
                catch (Exception ex) { DLog.Force("UIExtenderEx 初始化失败: " + ex); }
            }
            catch (Exception ex) { DLog.Force("OnSubModuleLoad 异常: " + ex); }
        }

        protected override void OnBeforeInitialModuleScreenSetAsRoot()
        {
            base.OnBeforeInitialModuleScreenSetAsRoot();
            try { ModConflictGuard.TryPopup(); }
            catch (Exception ex) { DLog.Force("冲突弹窗异常: " + ex.Message); }
            // v4.261: 主菜单加「蒸汽时代教程」入口(点击 = 教程模式 + 开新战役)
            try { RegisterTutorialEntry(); }
            catch (Exception ex) { DLog.Force("教程入口注册异常: " + ex.Message); }
        }

        // ==================== v4.261: 主菜单「蒸汽时代教程」 ====================
        //   用户要求: 教程不强制 —— 正常点[战役]开始的游戏全程无引导; 想学就点这个入口,
        //   它会打开"教程模式"再转交给原版的新战役按钮, 于是接下来: 选国 -> 挑战链逐步引导。
        internal const string TutorialOptionId = "AoSSteamTutorial";

        private static void RegisterTutorialEntry()
        {
            var mod = Module.CurrentModule;
            if (mod == null) return;
            try { if (mod.GetInitialStateOptionWithId(TutorialOptionId) != null) return; } catch { }
            var opt = new InitialStateOption(
                TutorialOptionId,
                new TextObject("蒸汽时代教程"),
                9001,
                StartTutorialCampaign,
                () => (false, new TextObject("")),
                new TextObject("带你走一遍这套系统: 选国 → 经济 → 建军 → 立法 → 科技(以沙盒模式开局, 不带原版主线与新手教程)"),
                () => false);
            mod.AddInitialStateOption(opt);
            DLog.Force("教程入口: 主菜单已注册「蒸汽时代教程」");
        }

        private static void StartTutorialCampaign()
        {
            try
            {
                Guide.TutorialMode = true;
                var mod = Module.CurrentModule;
                // v4.261b: 教程局要的是**沙盒模式**入口 —— 战役(Campaign/StoryMode)会带上骑砍原版新手教程
                //   与龙旗主线任务, 混进我们的引导里很乱。这里不硬编码 id(沙盒按钮不在 SandBox.dll 里注册),
                //   改为运行时枚举 + 关键字匹配, 沙盒优先、战役兜底。
                try
                {
                    foreach (var o in mod.GetInitialStateOptions())
                    {
                        if (o == null) continue;
                        string nm = o.Name != null ? o.Name.ToString() : "";
                        bool hidden = false;
                        try { hidden = o.IsHidden != null && o.IsHidden(); } catch { }
                        DLog.Force("教程入口: 主菜单选项 id=" + o.Id + " 名称=" + nm + (hidden ? "(隐藏)" : ""));
                    }
                }
                catch { }
                string id = FindMenuOption(mod, "andbox");     // 沙盒(Sandbox)
                if (id == null) id = FindMenuOption(mod, "沙盒");
                if (id == null) id = FindMenuOption(mod, "ampaign");   // 兜底: 战役(会带原版教程/主线)
                if (id == null) id = FindMenuOption(mod, "战役");
                if (id != null)
                {
                    DLog.Force("教程入口: 教程模式已开启 -> 转交主菜单入口(" + id + ")");
                    mod.ExecuteInitialStateOptionWithId(id);
                }
                else
                {
                    DLog.Force("教程入口: 未找到沙盒/战役入口 —— 教程模式已开启, 请手动点[沙盒]开始");
                    try { MapSelection.Message("教程模式已开启: 请手动点[沙盒]开始新游戏, 选国后教程会自动开始"); } catch { }
                }
            }
            catch (Exception ex) { DLog.Force("教程入口启动异常: " + ex.Message); }
        }

        // 按关键字(不区分大小写)在主菜单选项里找一个: 先比 id, 再比显示名
        private static string FindMenuOption(Module mod, string key)
        {
            try
            {
                foreach (var o in mod.GetInitialStateOptions())
                {
                    if (o == null || string.IsNullOrEmpty(o.Id)) continue;
                    if (o.Id.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0) return o.Id;
                    if (o.Name != null && o.Name.ToString().IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0) return o.Id;
                }
            }
            catch { }
            return null;
        }

        // 前 10 秒内持续尝试屏蔽外部 RTS 模块(它们的程序集可能比我们晚加载)
        protected override void OnApplicationTick(float dt)
        {
            base.OnApplicationTick(dt);
            try { BanStandaloneRts.Apply(HarmonyInstance); } catch { }
            try { IconLoader.Tick(); } catch { }
        }

        protected override void OnGameStart(Game game, IGameStarter gameStarterObject)
        {
            base.OnGameStart(game, gameStarterObject);
            try
            {
                // v4.278(第 1 项): DLC 工资双保险补丁的**兜底重试** —— OnSubModuleLoad 时若 NavalDLC.dll
                //   尚未进入 AppDomain, 那次调用只打了日志; 到这里所有模块的程序集都加载完了, 必能命中。
                //   方法内部幂等(已打过就跳过), 所以重复调用无副作用。
                try { NavalTaskForceAi.ApplyDlcWageGuard(HarmonyInstance); }
                catch (Exception exD) { DLog.Force("海军工资双保险补丁(OnGameStart 重试)异常: " + exD.Message); }
                // ==================== v4.290: 税收接管补丁必须在**这里**注册 ====================
                // ★ 不能放进上面那个启动期数组。实测崩因链(已由完整堆栈确证):
                //   放进数组 -> Harmony 在 OnSubModuleLoad 阶段就去处理 DefaultClanFinanceModel.AddSettlementIncome
                //   -> 该类型的静态初始化被**提前**触发, 而它的静态字段是
                //        private static readonly TextObject _xxx = Game.Current.GameTextManager.FindText("...");
                //      —— 此时 Game.Current 还是 null -> TypeInitializationException。
                //   更严重的是: 类型一旦被判"初始化失败", **之后每次访问都抛**(不可恢复), 于是:
                //        MapScreen..ctor -> GauntletMapBarGlobalLayer.Initialize -> MapBarVM.Initialize
                //        -> MapInfoVM.UpdatePlayerInfo -> CalculateClanGoldChange -> 炸
                //        -> FinalizeCharacterCreationState 失败 -> 角色创建无法结束
                //        -> 玩家卡在原版"文化列表"选国界面(而且选完就崩)。
                //   到这里(Campaign.OnInitialize 的 OnGameStart 阶段)Game.Current / GameTextManager 都已就绪。
                try
                {
                    HarmonyInstance.CreateClassProcessor(typeof(KingdomTraitPatches.PlayerSettlementIncomeTakeover)).Patch();
                    // 同批: [原版补金库 5000] 与 [领主军军饷双扣] —— 它们也 patch 同一个类型
                    //   (DefaultClanFinanceModel.AddPartyExpense), 一并延后到 Game.Current 就绪之后再挂。
                    HarmonyInstance.CreateClassProcessor(typeof(DefArmyPatches.DefArmyNoNativePartyExpensePatch)).Patch();
                    HarmonyInstance.CreateClassProcessor(typeof(DefArmyPatches.LordArmyNoNativeExpensePatch)).Patch();
                }
                catch (Exception exT) { DLog.Force("财政类补丁(OnGameStart)异常: " + exT.Message); }
                // 商品注册必须早于存档数据加载(读档时 SyncData 在 OnGameInitializationFinished 之前跑)
                if (game != null && game.GameType is Campaign) FeudalItems.Register(game, "OnGameStart");
                if (gameStarterObject is CampaignGameStarter starter)
                {
                    starter.AddBehavior(new NationalWillBehavior());
                    starter.AddBehavior(new EconomyBehavior());
                    starter.AddBehavior(new DiplomacyBehavior());
                    starter.AddBehavior(new DefArmyBehavior());
                    starter.AddBehavior(new SoldiersBehavior());   // 28.5: FIA_Soldiers/FIA_Converted 存档接线 + 建档转化入口
                    starter.AddBehavior(new NavalBehavior());      // v4.268: 海军层(日结/存档键 FIA_Navy; 软依赖海战 DLC)
                    starter.AddModel(new FeudalCombatModel());   // v4.200: 接管坐镇指挥伤害(高级热武器高伤害)
                    DLog.Info("已注册 NationalWillBehavior / EconomyBehavior / DiplomacyBehavior / DefArmyBehavior");
                }
            }
            catch (Exception ex) { DLog.Force("OnGameStart 异常: " + ex.Message); }
        }

        // 游戏初始化完成 = XML 商品已加载完毕(兜底: 若 OnGameStart 时对象管理器还没就绪, 这里补注册)
        public override void OnGameInitializationFinished(Game game)
        {
            base.OnGameInitializationFinished(game);
            try
            {
                if (game != null && game.GameType is Campaign)
                {
                    FeudalItems.Register(game, "OnGameInitializationFinished");
                }
            }
            catch (Exception ex) { DLog.Force("商品注册异常: " + ex.Message); }
        }
    }
}
