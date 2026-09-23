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
        private string _originalRulerId = "";   // 存档: 原统治者只存 StringId(对象引用会坏档)
        private int _mapMode;                   // v4.69: 地图模式
        private string _mapPickB = "", _mapPickI = "";   // 自选建筑/物品
        private bool _renamedToRuler;           // 是否已把玩家名字改成原元首的名字
        private float _hbTimer;                 // v4.75j: 心跳诊断计时

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
            try { WarWeariness.OnBattleEnded(mapEvent); } catch { }   // v4.73: 伤亡 -> 厌战度
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
                dataStore.SyncData("FIA_SetupDone", ref _setupDone);
                dataStore.SyncData("FIA_RenamedToRuler", ref _renamedToRuler);
                // 原统治者: 只存 StringId(Hero 对象引用写入存档会导致读档 Resolve Objects 崩溃)
                // 字符串统一分块存取(规避原版单字符串 ~32KB 隐性上限)
                if (dataStore.IsSaving)
                {
                    _nationKingdomId = _nationKingdomId ?? "";
                    _originalRulerId = _originalRuler != null ? _originalRuler.StringId : "";
                    _focusState = FocusTreeData.Serialize();
                    _cameraState = CaptureCameraState();
                    SyncChunks.Save(dataStore, "FIA_NationKingdomId", _nationKingdomId);
                    SyncChunks.Save(dataStore, "FIA_OriginalRulerId", _originalRulerId);
                    SyncChunks.Save(dataStore, "FIA_FocusState", _focusState);
                    SyncChunks.Save(dataStore, "FIA_CameraState", _cameraState);
                    dataStore.SyncData("FIA_MapMode", ref _mapMode);                       // v4.69: 地图模式
                    SyncChunks.Save(dataStore, "FIA_MapPickB", _mapPickB ?? "");
                    SyncChunks.Save(dataStore, "FIA_MapPickI", _mapPickI ?? "");
                }
                if (dataStore.IsLoading)
                {
                    _nationKingdomId = SyncChunks.Load(dataStore, "FIA_NationKingdomId");
                    _originalRulerId = SyncChunks.Load(dataStore, "FIA_OriginalRulerId");
                    _focusState = SyncChunks.Load(dataStore, "FIA_FocusState");
                    _cameraState = SyncChunks.Load(dataStore, "FIA_CameraState");
                    _originalRuler = string.IsNullOrEmpty(_originalRulerId) ? null : Hero.Find(_originalRulerId);
                    FocusTreeData.Deserialize(_focusState);
                    _pendingCamera = _cameraState;
                    dataStore.SyncData("FIA_MapMode", ref _mapMode);                       // v4.69: 地图模式
                    _mapPickB = SyncChunks.Load(dataStore, "FIA_MapPickB");
                    _mapPickI = SyncChunks.Load(dataStore, "FIA_MapPickI");
                    try { MapDataMode.Set((MapData)_mapMode, _mapPickB, _mapPickI); } catch { }
                    DLog.Force("读档: 国策=" + (_focusState ?? "空") + " 视角=" + (_cameraState ?? "空") + " 地图模式=" + MapDataMode.NameOf(MapDataMode.Current));
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
                NationChoice.ChosenKingdomId = null;   // v4.75: 同进程残留的旧选择必须清掉(新档重新选)
                NationalWillCamera.ResetForNewCampaign();   // v4.65: 新战役允许再居中一次
                Tutorials.ResetForNewCampaign();   // v4.124: 重置分步教程状态
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
                    DLog.Info("新战役已建立: 进入地图选国模式(玩家在领地上选择国家)");
                    NationPickMode.Start();
                }
            }
            catch (Exception ex) { DLog.Force("OnNewGameCreated 异常: " + ex.Message); }
        }

        private void OnGameLoaded(CampaignGameStarter starter)
        {
            try
            {
                if (!_setupDone)
                {
                    // v4.75: 未接管的存档(在选国阶段存的档/旧档) -> 重新进入地图选国流程
                    NationChoice.ChosenKingdomId = null;
                    NationPickMode.Start();
                    return;
                }
                NationPickMode.Stop();   // v4.75: 老档读入没有选国流程
                if (!string.IsNullOrEmpty(_cameraState)) NationalWillCamera.MarkDone();   // v4.65: 读档按存档视角, 不居中
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
            try { Tutorials.Tick(dt); } catch { }   // v4.124: 分步教程计时
            try { CommandTimeout.Tick(dt); } catch (Exception ex) { DLog.Force("指挥超时 tick 异常: " + ex.Message); }
            try { BattleCommand.TickPostBattleLeave(dt); } catch { }   // 战后自动离开结算菜单
            // v4.75j: 心跳诊断(每 3 秒): 排查"接管不推进"类问题
            _hbTimer += dt;
            if (_hbTimer >= 3f)
            {
                _hbTimer = 0f;
                try
                {
                    DLog.Force("心跳: setup=" + _setupDone + " 选国=" + NationPickMode.Active
                        + " 放弃=" + _gaveUp + " 已选=" + (NationChoice.ChosenKingdomId ?? "空")
                        + " 国家=" + (_nationKingdomId ?? "空") + " 时间=" + (Campaign.Current != null ? Campaign.Current.TimeControlMode.ToString() : "-"));
                }
                catch { }
            }
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
            // v4.75f: 选国阶段同样维持玩家部队控制(冻结+隐藏+名牌), 与接管后走同一套
            if (NationPickMode.Active)
            {
                NationalWillParty.Freeze(false);
                return;
            }
            if (_gaveUp) return;
            try { TrySetup(); }
            catch (Exception ex) { DLog.Force("初始化异常(将继续重试): " + ex.Message); }
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

        // v4.75j: 由地图视图每帧推进(FeudalMapView 暂停时也跑; CampaignEvents.TickEvent 在时间锁/暂停下可能不触发)
        internal void DriveSetup()
        {
            try { if (!_setupDone && !_gaveUp) TrySetup(); }
            catch (Exception ex) { DLog.Force("初始化异常(MapView): " + ex.Message); }
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
                if (NationPickMode.Active) return;   // v4.75: 等玩家在地图上点击领选定国家
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

            try { NationalWillClan.EnsureIdentity(kingdom, clan); }
            catch (Exception ex) { DLog.Force("家族身份初始化异常: " + ex.Message); }

            // v4.75c: 玩家文化 = 所选国家文化(与旧"角色创建选国"流程保持一致;
            // 角色创建阶段用的是默认文化, 这里在接管时纠正)
            try
            {
                if (kingdom.Culture != null)
                {
                    if (Hero.MainHero != null) Hero.MainHero.Culture = kingdom.Culture;
                    if (clan != null) clan.Culture = kingdom.Culture;
                }
            }
            catch (Exception ex) { DLog.Force("设置玩家文化失败: " + ex.Message); }

            // v4.82: 接管时玩家国库 = 该国战经金库(与选国侧栏"国库"显示一致, 各国不同)
            try
            {
                float kg;
                if (WarEconomy.Gold.TryGetValue(kingdom.StringId, out kg) && kg > 0f)
                {
                    int gold = (int)kg;
                    if (Hero.MainHero != null) Hero.MainHero.Gold = gold;
                    EconomyWorld.Treasury.Gold = gold;
                    EconomyWorld.Treasury.LastGold = gold;
                    Fiscal.EndOfDayGold(gold);   // v4.83: 同步"今日净额"基线, 接管注入的资金不计入今日收入
                    DLog.Force("接管国库: " + gold.ToString("N0") + " 第纳尔(与选国预览一致, 今日净额基线已对齐)");
                }
            }
            catch (Exception ex) { DLog.Force("接管国库设置失败: " + ex.Message); }

            // 玩家名字改成国家原元首的名字(用户需求)
            EnsureRulerName();

            _setupDone = true;
            try { DefArmy.TempFillIfReady(); } catch { }   // TEMP: 选国完成, 玩家王国已确定 -> 补给一次
            Tutorials.StartIntro();   // v4.124: 接管完成 -> 3 秒后弹开局分步教程
            NationPickMode.Stop();   // v4.75: 接管完成 -> 退出选国模式(相机解锁)
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
