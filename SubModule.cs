using System;
using System.Collections.Generic;
using Bannerlord.UIExtenderEx;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace FeudalInternalAffairs
{
    public class SubModule : MBSubModuleBase
    {
        internal static Harmony HarmonyInstance;

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
                    typeof(NationSelectionPatches.NationList),
                    typeof(NationSelectionPatches.NationSelect),
                    typeof(MapBarPatches.MapBarHidePatch),
                    typeof(MapBarPatches.MapBarHideTickPatch),
                    typeof(FocusTreeButtonPatch.EnsureFocusTreeItem),
                    typeof(FocusTreeMapGuardPatch.SkipMapVisualTick),
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
                    typeof(MapClickPatches.NoClickTimeChange),
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
                    typeof(NationalWillClan.ZombieClanKingdomPatch),
                    typeof(NoAiControlPatches.BlockAiArmy),
                    typeof(NoAiControlPatches.BlockDeclareWar),
                    typeof(NoAiControlPatches.BlockMakePeace),
                    typeof(DiplomacyPatches.AlwaysCanDeclareWar),
                    typeof(DiplomacyPatches.DeclareWarDirectly),
                    typeof(DiplomacyPatches.PeaceByEnemyVote),
                    typeof(MapInfoSelectionPatch.OverrideInfoBase),
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
                    typeof(KingdomTraitPatches.AseraiArcherAccuracy)
                };
                int ok = 0;
                foreach (var t in patches)
                {
                    try
                    {
                        HarmonyInstance.CreateClassProcessor(t).Patch();
                        ok++;
                    }
                    catch (Exception ex) { DLog.Force("补丁失败 " + t.Name + ": " + ex.Message); }
                }
                DLog.Force("内政扩展加载完成, 补丁 " + ok + "/" + patches.Count);
                KingdomTraitPatches.NavalTraitPatches.Apply(HarmonyInstance);
                TerritoryColorMode.NameplateWidgetPatch.Apply(HarmonyInstance);
                MapInfoSelectionPatch.OverrideInfoNaval.Apply(HarmonyInstance);

                try
                {
                    var extender = UIExtender.Create("_FeudalInternalAffairs");
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
        }

        // 前 10 秒内持续尝试屏蔽外部 RTS 模块(它们的程序集可能比我们晚加载)
        protected override void OnApplicationTick(float dt)
        {
            base.OnApplicationTick(dt);
            try { BanStandaloneRts.Apply(HarmonyInstance); } catch { }
        }

        protected override void OnGameStart(Game game, IGameStarter gameStarterObject)
        {
            base.OnGameStart(game, gameStarterObject);
            try
            {
                if (gameStarterObject is CampaignGameStarter starter)
                {
                    starter.AddBehavior(new NationalWillBehavior());
                    DLog.Info("已注册 NationalWillBehavior");
                }
            }
            catch (Exception ex) { DLog.Force("OnGameStart 异常: " + ex.Message); }
        }
    }
}
