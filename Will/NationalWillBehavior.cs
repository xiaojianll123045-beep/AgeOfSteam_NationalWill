using System;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterCreationContent;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;
using TaleWorlds.SaveSystem;

namespace FeudalInternalAffairs
{
    // 国家意志: 玩家没有自己的部队, 自己就是国家
    // 职责: 事件/存档/开局初始化的编排, 具体操作交给 NationalWillParty / NationalWillClan / NationalWillCamera
    public class NationalWillBehavior : CampaignBehaviorBase
    {
        private string _nationKingdomId = "";
        private bool _setupDone;
        private bool _gaveUp;

        public bool IsNationalWill
        {
            get { return _setupDone; }
        }

        // 所选国家(按 id 查, 不缓存引用)
        public Kingdom NationKingdom
        {
            get
            {
                try
                {
                    if (string.IsNullOrEmpty(_nationKingdomId)) return null;
                    return Kingdom.All.FirstOrDefault(k => k.StringId == _nationKingdomId);
                }
                catch { return null; }
            }
        }

        public override void RegisterEvents()
        {
            CampaignEvents.OnNewGameCreatedEvent.AddNonSerializedListener(this, OnNewGameCreated);
            CampaignEvents.OnGameLoadedEvent.AddNonSerializedListener(this, OnGameLoaded);
            CampaignEvents.TickEvent.AddNonSerializedListener(this, OnTick);
            CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, OnHourlyTick);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
            CampaignEvents.OnSettlementOwnerChangedEvent.AddNonSerializedListener(this, OnSettlementOwnerChanged);
        }

        // 定居点易主 -> 国界要重画
        private void OnSettlementOwnerChanged(Settlement settlement, bool openToClaim, Hero newOwner, Hero oldOwner,
            Hero capturerHero, TaleWorlds.CampaignSystem.Actions.ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail detail)
        {
            TerritoryColorMode.MarkDirty();
            KingdomTerritoryOverlay.MarkDirty();
        }

        public override void SyncData(IDataStore dataStore)
        {
            try
            {
                dataStore.SyncData("FIA_NationKingdomId", ref _nationKingdomId);
                dataStore.SyncData("FIA_SetupDone", ref _setupDone);
            }
            catch (Exception ex) { DLog.Force("SyncData 异常: " + ex.Message); }
        }

        private void OnNewGameCreated(CampaignGameStarter starter)
        {
            try
            {
                if (!string.IsNullOrEmpty(NationChoice.ChosenKingdomId))
                {
                    _nationKingdomId = NationChoice.ChosenKingdomId;
                    _setupDone = false;
                    _gaveUp = false;
                    TerritoryColorMode.Reset();
                    KingdomTerritoryOverlay.Reset();
                    DLog.Force("新战役: 玩家选择的国家 = " + _nationKingdomId);
                }
                else
                {
                    DLog.Info("新战役已建立(此时还没走选国家界面, 等选完再取)");
                }
            }
            catch (Exception ex) { DLog.Force("OnNewGameCreated 异常: " + ex.Message); }
        }

        private void OnGameLoaded(CampaignGameStarter starter)
        {
            try
            {
                if (!_setupDone) return;
                NationalWillParty.Freeze(false);
                var kingdom = NationKingdom;
                var clan = Clan.PlayerClan;
                if (kingdom != null && clan != null) NationalWillClan.EnsureIdentity(kingdom, clan);
                TerritoryColorMode.Reset();
                KingdomTerritoryOverlay.Reset();
            }
            catch (Exception ex) { DLog.Force("OnGameLoaded 异常: " + ex.Message); }
        }

        private void OnTick(float dt)
        {
            CommandTimeout.Tick(dt);   // 指挥超时 -> 恢复 AI 自主
            if (_setupDone)
            {
                NationalWillParty.Freeze(false);   // 每帧维持(时间流逝后游戏可能把部队状态改回去)
                // 正常情况下由 FeudalMapView.OnMapScreenUpdate 每帧驱动(暂停也会跑);
                // 这里只是 MapView 没挂上时的兜底
                if (FeudalMapView.Current == null)
                {
                    TerritoryColorMode.Tick(dt);
                }
                return;
            }
            if (_gaveUp) return;
            TrySetup();
        }

        private void OnHourlyTick()
        {
            try
            {
                if (!_setupDone && !_gaveUp) TrySetup();
                if (_setupDone)
                {
                    NationalWillParty.Freeze(false);
                    NationalWillParty.EnsureFood();
                }
            }
            catch (Exception ex) { DLog.Force("OnHourlyTick 异常: " + ex.Message); }
        }

        // 国策: 每天推进一次
        private void OnDailyTick()
        {
            try
            {
                var done = FocusTreeData.TickDay();
                if (done != null)
                {
                    try
                    {
                        InformationManager.DisplayMessage(new InformationMessage(
                            "国策完成: " + done.Name + " —— " + done.Effects, Color.FromUint(4294953344U)));
                    }
                    catch { }
                    DLog.Force("国策完成: " + done.Name);
                }
            }
            catch (Exception ex) { DLog.Force("国策日结异常: " + ex.Message); }
        }

        private void TrySetup()
        {
            if (ModConflictGuard.Check())
            {
                _gaveUp = true;
                DLog.Force("检测到旧 MOD 同时加载, 不进行国家意志初始化");
                return;
            }

            // 角色创建界面在我们之后才选国家, 所以这里补取
            if (string.IsNullOrEmpty(_nationKingdomId) && !string.IsNullOrEmpty(NationChoice.ChosenKingdomId))
            {
                _nationKingdomId = NationChoice.ChosenKingdomId;
                DLog.Force("从角色创建界面取得国家: " + _nationKingdomId);
            }

            if (string.IsNullOrEmpty(_nationKingdomId))
            {
                if (IsCharacterCreationActive()) return;
                _gaveUp = true;
                DLog.Force("没有国家记录, 放弃国家意志初始化(这个存档不是从选国家开局的)");
                return;
            }

            var kingdom = NationKingdom;
            if (kingdom == null) return;
            var clan = Clan.PlayerClan;
            var mainParty = MobileParty.MainParty;
            if (clan == null || mainParty == null) return;

            // 僵尸家族: 默认不做"加入王国"动作(没有提示/剧情弹窗/名册登记),
            // 由 ZombieClanKingdomPatch 让"玩家属于哪个国家"的查询返回该国
            if (DLog.Flag("realjoin") && clan.Kingdom != kingdom)
            {
                try
                {
                    ChangeKingdomAction.ApplyByJoinToKingdom(clan, kingdom, default(CampaignTime), false);
                    DLog.Force("(调试)玩家家族已实际加入 " + kingdom.Name);
                }
                catch (Exception ex) { DLog.Force("(调试)加入国家失败: " + ex.Message); }
            }

            if (!DLog.Flag("noruler") && kingdom.RulingClan != clan)
            {
                try
                {
                    kingdom.RulingClan = clan;
                    DLog.Force("玩家家族已成为 " + kingdom.Name + " 的统治家族");
                }
                catch (Exception ex) { DLog.Force("成为统治家族失败: " + ex.Message); }
            }

            NationalWillClan.EnsureIdentity(kingdom, clan);

            _setupDone = true;
            // 国家一确定就立即把视角瞬移到版图中心(地图已就绪时一次成功)
            NationalWillCamera.CenterOnKingdomOnce(kingdom);
            // 立刻刷新名板(否则要等时间流逝, 定居点敌我颜色才会变)
            try
            {
                if (SettlementNameplatesVMMixin.Instance != null) SettlementNameplatesVMMixin.Instance.RefreshValues();
                if (PartyNameplatesVMMixin.Instance != null) PartyNameplatesVMMixin.Instance.Manager.RefreshValues();
            }
            catch (Exception ex) { DLog.Force("名板刷新异常: " + ex.Message); }
            NationalWillParty.Freeze(true);
            NationalWillParty.EnsureFood();

            string kname = kingdom.Name != null ? kingdom.Name.ToString() : _nationKingdomId;
            try
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    "你已成为" + kname + "的国家意志: 它的一切由你决定。", Color.FromUint(4294953344U)));
            }
            catch { }
            DLog.Force("国家意志初始化完成: " + kname
                + " 统治者=" + (kingdom.Leader != null ? kingdom.Leader.Name.ToString() : "?")
                + " 玩家家族=" + clan.Name);
            // 诊断: 家族归属与主角地图阵营是否已指向所选国家
            try
            {
                DLog.Force("诊断: PlayerClan.Kingdom="
                    + (Clan.PlayerClan != null && Clan.PlayerClan.Kingdom != null ? Clan.PlayerClan.Kingdom.StringId : "null")
                    + " MainHero.MapFaction="
                    + (Hero.MainHero != null && Hero.MainHero.MapFaction != null ? Hero.MainHero.MapFaction.StringId : "null")
                    + " 目标国家=" + kingdom.StringId);
            }
            catch (Exception ex) { DLog.Force("诊断异常: " + ex.Message); }
        }

        private static bool IsCharacterCreationActive()
        {
            try
            {
                var gsm = GameStateManager.Current;
                return gsm != null && gsm.ActiveState is CharacterCreationState;
            }
            catch { return false; }
        }
    }
}
