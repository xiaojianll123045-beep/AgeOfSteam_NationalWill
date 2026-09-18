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
        private string _focusState = "";
        private string _cameraState = "";
        private string _pendingCamera = null;   // 读档后等相机就绪再恢复视角
        private Hero _originalRuler = null;     // 接管前的原统治者(玩家家族成为统治家族后 Kingdom.Leader 会变成玩家)
        private bool _renamedToRuler;           // 是否已把玩家名字改成原元首的名字

        // 国家的原统治者(用于界面显示"国家领袖")
        public Hero OriginalRuler
        {
            get
            {
                try
                {
                    if (_originalRuler != null && !_originalRuler.IsDead) return _originalRuler;
                    // 旧存档没有记录 -> 启发式: 本国里影响力+领地最多的非玩家家族领袖(通常就是原统治家族)
                    var k = NationKingdom;
                    var me = Clan.PlayerClan;
                    if (k == null) return null;
                    Clan best = null;
                    float bestScore = -1f;
                    foreach (var c in k.Clans)
                    {
                        if (c == null || c.Leader == null || ReferenceEquals(c, me)) continue;
                        float score = c.Influence + c.Settlements.Count * 100f;
                        if (score > bestScore) { bestScore = score; best = c; }
                    }
                    return best != null ? best.Leader : (k.Leader);
                }
                catch { return null; }
            }
        }

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
            CampaignEvents.MapEventEnded.AddNonSerializedListener(this, OnMapEventEnded);
        }

        // 战斗结束 -> 亲自指挥的部队归位
        private void OnMapEventEnded(TaleWorlds.CampaignSystem.MapEvents.MapEvent mapEvent)
        {
            try { BattleCommand.OnBattleEnded(mapEvent); } catch { }
        }

        // 定居点易主 -> 国界要重画
        private void OnSettlementOwnerChanged(Settlement settlement, bool openToClaim, Hero newOwner, Hero oldOwner,
            Hero capturerHero, TaleWorlds.CampaignSystem.Actions.ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail detail)
        {
            TerritoryColorMode.MarkDirty();
            KingdomTerritoryOverlay.MarkDirty();
            try { DiploPlays.OnSettlementChanged(settlement, oldOwner, newOwner); } catch { }   // 第 22 章: 战争支持度
        }

        public override void SyncData(IDataStore dataStore)
        {
            try
            {
                dataStore.SyncData("FIA_NationKingdomId", ref _nationKingdomId);
                dataStore.SyncData("FIA_SetupDone", ref _setupDone);
                dataStore.SyncData("FIA_OriginalRuler", ref _originalRuler);
                dataStore.SyncData("FIA_RenamedToRuler", ref _renamedToRuler);
                // 存: 国策状态 + 地图视角位置
                if (dataStore.IsSaving)
                {
                    _focusState = FocusTreeData.Serialize();
                    _cameraState = CaptureCameraState();
                }
                dataStore.SyncData("FIA_FocusState", ref _focusState);
                dataStore.SyncData("FIA_CameraState", ref _cameraState);
                if (dataStore.IsLoading)
                {
                    FocusTreeData.Deserialize(_focusState);
                    _pendingCamera = _cameraState;
                    DLog.Force("读档: 国策=" + (_focusState ?? "空") + " 视角=" + (_cameraState ?? "空"));
                }
            }
            catch (Exception ex) { DLog.Force("SyncData 异常: " + ex.Message); }
        }

        // ---- 地图视角的存档 ----
        private static string CaptureCameraState()
        {
            try
            {
                var view = NationalWillCamera.View;
                if (view == null) return "";
                var t = NationalWillCamera.GetIdealTarget(view);
                float bearing = NationalWillCamera.GetBearing(view);
                float dist = NationalWillCamera.GetCameraDistance(view);
                var ci = System.Globalization.CultureInfo.InvariantCulture;
                return t.x.ToString("F2", ci) + "," + t.y.ToString("F2", ci) + ","
                     + bearing.ToString("F3", ci) + "," + dist.ToString("F2", ci);
            }
            catch { return ""; }
        }

        private static bool ApplyCameraState(string s)
        {
            try
            {
                if (string.IsNullOrEmpty(s)) return true;
                var parts = s.Split(',');
                if (parts.Length < 4) return true;
                var ci = System.Globalization.CultureInfo.InvariantCulture;
                float x = float.Parse(parts[0], ci);
                float y = float.Parse(parts[1], ci);
                float bearing = float.Parse(parts[2], ci);
                float dist = float.Parse(parts[3], ci);
                var view = NationalWillCamera.View;
                if (view == null) return false;   // 相机还没就绪, 下帧再试
                var target = new Vec3(x, y, 0f, 1f);
                NationalWillCamera.SetIdealTarget(view, target);
                NationalWillCamera.SetCameraTarget(view, target);
                NationalWillCamera.SetLastUsedTarget(view, target);
                NationalWillCamera.SetBearing(view, bearing);
                NationalWillCamera.SetCameraDistance(view, dist);
                DLog.Force("读档: 地图视角已恢复 (" + x.ToString("F0", ci) + "," + y.ToString("F0", ci) + ")");
                return true;
            }
            catch { return true; }
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
                // 旧存档: 读档后把玩家名字改成国家原元首的名字(只做一次)
                EnsureRulerName();
                TerritoryColorMode.Reset();
                KingdomTerritoryOverlay.Reset();
            }
            catch (Exception ex) { DLog.Force("OnGameLoaded 异常: " + ex.Message); }
        }

        private void OnTick(float dt)
        {
            CommandTimeout.Tick(dt);   // 指挥超时 -> 恢复 AI 自主
            try { BattleCommand.TickPostBattleLeave(dt); } catch { }   // 战后自动离开结算菜单
            // 读档后恢复地图视角(等相机就绪)
            if (_pendingCamera != null && ApplyCameraState(_pendingCamera)) _pendingCamera = null;
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
                    // 先记住原统治者(界面要显示真正的国家领袖)
                    if (_originalRuler == null && kingdom.Leader != null)
                    {
                        _originalRuler = kingdom.Leader;
                        DLog.Force("已记录原统治者: " + _originalRuler.Name);
                    }
                    kingdom.RulingClan = clan;
                    DLog.Force("玩家家族已成为 " + kingdom.Name + " 的统治家族");
                }
                catch (Exception ex) { DLog.Force("成为统治家族失败: " + ex.Message); }
            }

            NationalWillClan.EnsureIdentity(kingdom, clan);

            // 玩家名字改成国家原元首的名字(用户需求)
            EnsureRulerName();

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

        // 把玩家(国家意志)的名字改成国家原元首的名字 —— 只做一次
        private void EnsureRulerName()
        {
            try
            {
                if (_renamedToRuler) return;
                var me = Hero.MainHero;
                if (me == null) return;
                var ruler = OriginalRuler;
                if (ruler == null) return;
                if (ReferenceEquals(ruler, me)) { _renamedToRuler = true; return; }
                var full = ruler.Name;
                if (full == null) return;
                var first = ruler.FirstName;
                if (first == null) first = full;
                me.SetName(full, first);
                _renamedToRuler = true;
                DLog.Force("玩家已改名为国家元首: " + full);
            }
            catch (Exception ex) { DLog.Force("玩家改名失败: " + ex.Message); }
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
