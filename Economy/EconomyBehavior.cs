using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.SaveSystem;

namespace FeudalInternalAffairs
{
    // 经济系统的存档与每日结算入口
    // 存档键: FIA_Buildings / FIA_Market / FIA_National / FIA_Treasury / FIA_Private / FIA_Brush / FIA_AutoBuild
    internal class EconomyBehavior : CampaignBehaviorBase
    {
        internal static EconomyBehavior Current;

        private string _buildings = "";
        private string _markets = "";
        private string _national = "";
        private string _treasury = "";
        private string _lords = "";
        private string _brushes = "";
        private string _autoBuild = "";
        private string _pops = "";           // v3.0: 人口表(文档 19.13.1)
        private string _pool = "";           // v3.0: 投资池
        private string _guild = "";          // v3.0: 行会
        private string _tax = "";            // v4.0: 税制(文档 20.1)
        private string _credit = "";         // v4.0: 信贷(20.2)
        private string _mint = "";           // v4.0: 铸币权(20.3)
        private string _own = "";            // v4.0: 所有权池(20.4)
        private string _ledger2 = "";        // v4.0: 统计快照(20.9)
        private string _politics = "";       // v4.1: 政治系统(第 21 章)
        private string _laws = "";           // v5.0-P22: 法律体系(文档 24.3)
        private string _ig = "";             // v5.0-P22: 利益集团(文档 24.2)
        private string _rev = "";            // v5.0-P23: 革命(文档 24.5)
        private string _trade = "";          // v5.0-P24: 贸易路线(文档 24.7)
        private string _warmob = "";         // v5.0-P25: 动员
        private string _elec = "";           // v5.0-P25: 选举
        private string _research = "";       // v5.0-P26: 研究
        private string _tech = "";           // v6.0: 按国科技(文档 28.10, FIA_Tech)
        private string _inst = "";           // v5.0-P26: 机构/文化
        private string _bloc = "";           // v5.0-P27: 权力集团
        private string _interests = "";      // v5.0-P27: 利益宣示
        private string _warplan = "";        // v4.149: 战争计划
        private string _aiecon = "";         // v4.149: AI 经济工具状态
        private string _reserve = "";        // v4.152: 战略储备
        private string _rail = "";           // v4.162: 铁路线
        private string _culture = "";        // v4.186: 文化法律(文档 24.13)
        private string _chest = "";          // v4.187: 钱箱(文档 20.6)
        private string _treaty = "";         // v4.188: 条约(文档 24.8)
        private string _party = "";          // v4.195: 政党(文档 24.2)
        private string _decree = "";         // v4.196: 法令(文档 24.5)
        private string _event = "";          // v4.197: 随机事件
        private string _lobby = "";          // v4.198: 游说
        private string _battle = "";         // v4.201: 战报
        private string _vet = "";            // v4.202: 军团老兵度
        private int _lastDay = -1;
        private bool _seedDone;             // v4.252: 开局铺底库存是否已发过(一次)
        private const int StarterVer = 4;   // v4.66: 起步建筑版本(1=基础, 2=+炭窑, 3=+炼铁厂/工具坊/纺织厂, 4=+武器/盔甲作坊; 老档升级自动补新项)
        private int _starterVersion;        // 已补发到的版本(每档记录)

        internal EconomyBehavior()
        {
            Current = this;
        }

        internal static EconomyBehavior Get()
        {
            if (Current != null) return Current;
            try { return Campaign.Current != null ? Campaign.Current.GetCampaignBehavior<EconomyBehavior>() : null; }
            catch { return null; }
        }

        public override void RegisterEvents()
        {
            Current = this;
            CampaignEvents.OnNewGameCreatedEvent.AddNonSerializedListener(this, OnNewGameCreated);
            CampaignEvents.OnGameLoadedEvent.AddNonSerializedListener(this, OnGameLoaded);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
            CampaignEvents.TickEvent.AddNonSerializedListener(this, OnTick);   // v4.57: 跨日兜底(防 DailyTick 未触发导致"第二天不结算")
        }

        // ---------------- 存档 ----------------
        public override void SyncData(IDataStore dataStore)
        {
            try
            {
                if (dataStore.IsSaving)
                {
                    // v4.245: 存档前把异步日结队列强制跑完, 免得把"半结算"的状态写进档
                    try { DailyScheduler.Flush(); } catch { }
                    _buildings = EconomyWorld.SaveBuildings();
                    _markets = EconomyWorld.SaveMarkets();
                    _national = EconomyWorld.SaveNational();
                    _treasury = EconomyWorld.SaveTreasury();
                    _lords = EconomyWorld.SaveLords();
                    _brushes = EconomyWorld.SaveBrushes();
                    _autoBuild = EconomyWorld.SaveAutoBuild();
                    _pops = Pops.Save();
                    _pool = InvestmentPool.Save();
                    _guild = Guilds.Save();
                    _tax = TaxPolicy.Save();
                    _credit = Credit.Save();
                    _mint = MintRight.Save();
                    _own = Ownership.Save();
                    _ledger2 = Stats.Save();
                    _politics = Politics.Save();
                    _laws = LawSystem.Save();
                    _ig = InterestGroups.Save();
                    _rev = Revolution.Save();
                    _trade = TradeRoutes.Save();
                    _warmob = WarMobilization.Save();
                    _elec = Elections.Save();
                    _research = Research.Save();
                    _tech = Research.SaveNations();   // v6.0: FIA_Tech(按国)
                    _inst = Institutions.Save();
                _bloc = PowerBlocs.Save();
                _interests = Interests.Save();
                _warplan = WarPlans.Save();       // v4.149
                _aiecon = AiEconomyDeep.Save();   // v4.149
                _reserve = StrategicReserve.Save();   // v4.152
                _rail = Railways.Save();          // v4.162
                _culture = CultureSystem.Serialize();   // v4.186: 文化法律
                _chest = CashChest.Save();              // v4.187: 钱箱
                _treaty = Treaties.Save();              // v4.188: 条约
                _party = Parties.Save();                // v4.195: 政党
                _decree = Decrees.Save();               // v4.196: 法令
                _event = Events.Save();                 // v4.197: 随机事件
                _lobby = Lobbying.Save();               // v4.198: 游说
                _battle = BattleSim.Save();             // v4.201: 战报
                _vet = ArmyDoctrine.Save();             // v4.202: 军团老兵度
                    DLog.Force("存档: 经济数据 -> " + EconomyWorld.Describe());
                    DLog.Force("存档长度: bld=" + (_buildings ?? "").Length + " mkt=" + (_markets ?? "").Length
                        + " nat=" + (_national ?? "").Length + " pops=" + (_pops ?? "").Length
                        + " pool=" + (_pool ?? "").Length + " guild=" + (_guild ?? "").Length
                        + " tax=" + (_tax ?? "").Length + " credit=" + (_credit ?? "").Length
                        + " mint=" + (_mint ?? "").Length + " own=" + (_own ?? "").Length
                        + " ledger=" + (_ledger2 ?? "").Length + " pol=" + (_politics ?? "").Length
                        + " laws=" + (_laws ?? "").Length + " ig=" + (_ig ?? "").Length + " rev=" + (_rev ?? "").Length
                        + " lords=" + (_lords ?? "").Length + " brush=" + (_brushes ?? "").Length
                        + " autob=" + (_autoBuild ?? "").Length + " trea=" + (_treasury ?? "").Length);
                }
                // 统一分块存取: 单块 <=24KB, 规避原版 SaveSystem 单字符串 ~32KB 隐性上限(超限->读档"数组维度"崩溃)
                if (dataStore.IsSaving)
                {
                    SyncChunks.Save(dataStore, "FIA_Buildings", _buildings);
                    SyncChunks.Save(dataStore, "FIA_Market", _markets);
                    SyncChunks.Save(dataStore, "FIA_National", _national);
                    SyncChunks.Save(dataStore, "FIA_Treasury", _treasury);
                    SyncChunks.Save(dataStore, "FIA_Private", _lords);
                    SyncChunks.Save(dataStore, "FIA_Brush", _brushes);
                    SyncChunks.Save(dataStore, "FIA_AutoBuild", _autoBuild);
                    SyncChunks.Save(dataStore, "FIA_Pops", _pops);
                    SyncChunks.Save(dataStore, "FIA_Pool", _pool);
                    SyncChunks.Save(dataStore, "FIA_Guild", _guild);
                    SyncChunks.Save(dataStore, "FIA_Tax", _tax);
                    SyncChunks.Save(dataStore, "FIA_Credit", _credit);
                    SyncChunks.Save(dataStore, "FIA_Mint", _mint);
                    SyncChunks.Save(dataStore, "FIA_Own", _own);
                    SyncChunks.Save(dataStore, "FIA_Ledger2", _ledger2);
                    SyncChunks.Save(dataStore, "FIA_Politics", _politics);
                    SyncChunks.Save(dataStore, "FIA_Laws", _laws);
                    SyncChunks.Save(dataStore, "FIA_IG", _ig);
                    SyncChunks.Save(dataStore, "FIA_Rev", _rev);
                    SyncChunks.Save(dataStore, "FIA_Trade", _trade);
                    SyncChunks.Save(dataStore, "FIA_WarMob", _warmob);
                    SyncChunks.Save(dataStore, "FIA_Elec", _elec);
                    SyncChunks.Save(dataStore, "FIA_Research", _research);
                    SyncChunks.Save(dataStore, "FIA_Tech", _tech);   // v6.0: 按国科技(位图+进度)
                    SyncChunks.Save(dataStore, "FIA_Inst", _inst);
                    SyncChunks.Save(dataStore, "FIA_Bloc", _bloc);
                    SyncChunks.Save(dataStore, "FIA_Interests", _interests);
                    SyncChunks.Save(dataStore, "FIA_WarPlan", _warplan);   // v4.149: 战争计划
                    SyncChunks.Save(dataStore, "FIA_AiEcon", _aiecon);     // v4.149: AI 经济工具状态
                    SyncChunks.Save(dataStore, "FIA_Reserve", _reserve);   // v4.152: 战略储备
                    SyncChunks.Save(dataStore, "FIA_Rail", _rail);       // v4.162: 铁路
                    SyncChunks.Save(dataStore, "FIA_Cult", _culture);    // v4.186: 文化法律
                    SyncChunks.Save(dataStore, "FIA_Chest", _chest);     // v4.187: 钱箱
                    SyncChunks.Save(dataStore, "FIA_Treaty", _treaty);   // v4.188: 条约
                    SyncChunks.Save(dataStore, "FIA_Party", _party);     // v4.195: 政党
                    SyncChunks.Save(dataStore, "FIA_Decree", _decree);   // v4.196: 法令
                    SyncChunks.Save(dataStore, "FIA_Event", _event);     // v4.197: 随机事件
                    SyncChunks.Save(dataStore, "FIA_Lobby", _lobby);     // v4.198: 游说
                    SyncChunks.Save(dataStore, "FIA_Battle", _battle);   // v4.201: 战报
                    SyncChunks.Save(dataStore, "FIA_Vet", _vet);         // v4.202: 军团老兵度
                    dataStore.SyncData("FIA_EcoDay", ref _lastDay);   // 上次结算日(读档补结算用)
                    dataStore.SyncData("FIA_SeedStock", ref _seedDone);   // v4.252: 开局铺底库存标记
                    dataStore.SyncData("FIA_StarterVer", ref _starterVersion);   // v4.64: 起步建筑补发版本
                }
                if (dataStore.IsLoading)
                {
                    _buildings = SyncChunks.Load(dataStore, "FIA_Buildings");
                    _markets = SyncChunks.Load(dataStore, "FIA_Market");
                    _national = SyncChunks.Load(dataStore, "FIA_National");
                    _treasury = SyncChunks.Load(dataStore, "FIA_Treasury");
                    _lords = SyncChunks.Load(dataStore, "FIA_Private");
                    _brushes = SyncChunks.Load(dataStore, "FIA_Brush");
                    _autoBuild = SyncChunks.Load(dataStore, "FIA_AutoBuild");
                    _pops = SyncChunks.Load(dataStore, "FIA_Pops");
                    _pool = SyncChunks.Load(dataStore, "FIA_Pool");
                    _guild = SyncChunks.Load(dataStore, "FIA_Guild");
                    _tax = SyncChunks.Load(dataStore, "FIA_Tax");
                    _credit = SyncChunks.Load(dataStore, "FIA_Credit");
                    _mint = SyncChunks.Load(dataStore, "FIA_Mint");
                    _own = SyncChunks.Load(dataStore, "FIA_Own");
                    _ledger2 = SyncChunks.Load(dataStore, "FIA_Ledger2");
                    _politics = SyncChunks.Load(dataStore, "FIA_Politics");
                    _laws = SyncChunks.Load(dataStore, "FIA_Laws");
                    _ig = SyncChunks.Load(dataStore, "FIA_IG");
                    _rev = SyncChunks.Load(dataStore, "FIA_Rev");
                    _trade = SyncChunks.Load(dataStore, "FIA_Trade");
                    _warmob = SyncChunks.Load(dataStore, "FIA_WarMob");
                    _elec = SyncChunks.Load(dataStore, "FIA_Elec");
                    _research = SyncChunks.Load(dataStore, "FIA_Research");
                    _tech = SyncChunks.Load(dataStore, "FIA_Tech");   // v6.0: 按国科技
                    _inst = SyncChunks.Load(dataStore, "FIA_Inst");
                    _bloc = SyncChunks.Load(dataStore, "FIA_Bloc");
                    _interests = SyncChunks.Load(dataStore, "FIA_Interests");
                    _warplan = SyncChunks.Load(dataStore, "FIA_WarPlan");
                    _aiecon = SyncChunks.Load(dataStore, "FIA_AiEcon");
                    _reserve = SyncChunks.Load(dataStore, "FIA_Reserve");
                    _rail = SyncChunks.Load(dataStore, "FIA_Rail");
                    _culture = SyncChunks.Load(dataStore, "FIA_Cult");   // v4.186: 文化法律
                    _chest = SyncChunks.Load(dataStore, "FIA_Chest");    // v4.187: 钱箱
                    _treaty = SyncChunks.Load(dataStore, "FIA_Treaty");  // v4.188: 条约
                    _party = SyncChunks.Load(dataStore, "FIA_Party");    // v4.195: 政党
                    _decree = SyncChunks.Load(dataStore, "FIA_Decree");  // v4.196: 法令
                    _event = SyncChunks.Load(dataStore, "FIA_Event");    // v4.197: 随机事件
                    _lobby = SyncChunks.Load(dataStore, "FIA_Lobby");    // v4.198: 游说
                    _battle = SyncChunks.Load(dataStore, "FIA_Battle");  // v4.201: 战报
                    _vet = SyncChunks.Load(dataStore, "FIA_Vet");        // v4.202: 军团老兵度
                    dataStore.SyncData("FIA_EcoDay", ref _lastDay);
                    dataStore.SyncData("FIA_SeedStock", ref _seedDone);  // v4.252: 开局铺底库存标记
                    dataStore.SyncData("FIA_StarterVer", ref _starterVersion);
                    EconomyWorld.LoadBuildings(_buildings);
                    EconomyWorld.LoadMarkets(_markets);
                    EconomyWorld.LoadNational(_national);
                    EconomyWorld.LoadTreasury(_treasury);
                    EconomyWorld.LoadLords(_lords);
                    EconomyWorld.LoadBrushes(_brushes);
                    EconomyWorld.LoadAutoBuild(_autoBuild);
                    Pops.Load(_pops);
                    InvestmentPool.Load(_pool);
                    Guilds.Load(_guild);
                    TaxPolicy.Load(_tax);
                    Credit.Load(_credit);
                    MintRight.Load(_mint);
                    Ownership.Load(_own);
                    Stats.Load(_ledger2);
                    LawSystem.Load(_laws);          // v5.0-P22: 先于 Politics(旧 6 法迁移判定)
                    Politics.Load(_politics);
                    InterestGroups.Load(_ig);       // v5.0-P22: 集团(依赖 Politics.Lords)
                    Revolution.Load(_rev);          // v5.0-P23: 革命
                    TradeRoutes.Load(_trade);       // v5.0-P24: 贸易路线
                    WarMobilization.Load(_warmob);  // v5.0-P25: 动员
                    Elections.Load(_elec);          // v5.0-P25: 选举
                    Research.LoadNations(_tech);    // v6.0: 按国科技(FIA_Tech; 旧档缺段由 Load 迁移)
                    Research.Load(_research);       // v5.0-P26: 研究
                    Institutions.Load(_inst);       // v5.0-P26: 机构/文化
                    CultureSystem.Deserialize(_culture);   // v4.186: 文化法律(晚于 Institutions, 兼容旧全局政策)
                    CashChest.Load(_chest);                // v4.187: 钱箱
                    Treaties.Load(_treaty);                // v4.188: 条约
                    Parties.Load(_party);                  // v4.195: 政党
                    Decrees.Load(_decree);                 // v4.196: 法令
                    Events.Load(_event);                   // v4.197: 随机事件
                    Lobbying.Load(_lobby);                 // v4.198: 游说
                    BattleSim.Load(_battle);               // v4.201: 战报
                    ArmyDoctrine.Load(_vet);               // v4.202: 军团老兵度
                    PowerBlocs.Load(_bloc);         // v5.0-P27: 权力集团
                    Interests.Load(_interests);     // v5.0-P27: 利益宣示
                    WarPlans.Load(_warplan);        // v4.149: 战争计划
                    AiEconomyDeep.Load(_aiecon);    // v4.149: AI 经济工具
                    StrategicReserve.Load(_reserve);   // v4.152: 战略储备
                    Railways.Load(_rail);              // v4.162: 铁路
                    DLog.Force("读档: 经济数据 -> " + EconomyWorld.Describe());
                }
            }
            catch (Exception ex) { DLog.Force("经济 SyncData 异常: " + ex); }
        }

        // ---------------- 开局 / 读档 ----------------
        private void OnNewGameCreated(CampaignGameStarter starter)
        {
            try
            {
                EconomyWorld.Reset();
                EconomyWorld.EnsureContainers();
                PresetBuildings();
                InitTreasury();
                // v4.252: 开局铺底库存(一次)
                try { int sn = DailySettlement.SeedStartingStock(); _seedDone = true; DLog.Force("经济: 开局铺货 -> " + sn + " 座城镇"); } catch { }
                Pops.EnsureInit("新战役");
                Politics.Reset();
                RailSystem.Reset();   // v4.161: 铁路(下帧重新建网)
                DLog.Force("经济: 新战役初始化 -> " + EconomyWorld.Describe());
            }
            catch (Exception ex) { DLog.Force("经济初始化异常: " + ex.Message); }
        }

        private void OnGameLoaded(CampaignGameStarter starter)
        {
            try
            {
                EconomyWorld.EnsureContainers();
                Pops.EnsureInit("读档补齐");   // 旧存档无 FIA_Pops 时补建
                RailSystem.Reset();   // v4.161: 铁路(下帧重新建网)
                // 旧存档(本系统还没有数据) -> 自动初始化: 预置建筑 + 市场初值(11.2)
                if (!EconomyWorld.HasAnyData)
                {
                    PresetBuildings();
                    InitTreasury();
                    DLog.Force("经济: 旧存档无数据, 已自动初始化");
                }
                EnsureTownBaseline();   // 旧存档补建: 没有产业的城镇补市场+产业, 保证所有国家都跑经济
                // v4.252: 老档也补一次开局铺底库存(原来完全没有初始库存, 新商品全世界为 0)
                if (!_seedDone)
                {
                    try { int sn = DailySettlement.SeedStartingStock(); _seedDone = true; DLog.Force("经济: 补铺底库存 -> " + sn + " 座城镇"); } catch { }
                }
                TryStarterFill("读档");   // v4.62: 玩家国起步建筑补发(一次; 修复老档经济死锁)
                // 读档后检查是否需要补结算
                try
                {
                    int today = (int)CampaignTime.Now.ToDays;
                    int prev = _lastDay;
                    // v4.252: 删掉原来的 `_lastDay = today;` —— 那一行让紧跟其后的 TickDay 立刻 return
                    //   (TickDay 自带 `if (day == _lastDay) return;` 防重), 结果是"读档永远不补结算";
                    //   而审计报告担心的"读档重复收税"因为那行防重其实并不会发生。现在交给 TickDay 判断:
                    //   存档里记着上次已结算日(FIA_EcoDay), 只有真的漏了才补, 且天然不会重复。
                    TickDay(today);
                    DLog.Force("经济: 读档结算检查(第 " + today + " 天, 上次已结算 " + prev
                        + (prev >= today ? ", 无需补算" : ", 已补算 " + (today - prev) + " 天") + ")");
                }
                catch (Exception ex) { DLog.Force("读档补结算异常: " + ex.Message); }
                DLog.Force("经济: 读档完成 -> " + EconomyWorld.Describe());
            }
            catch (Exception ex) { DLog.Force("经济读档异常: " + ex.Message); }
        }

        // 兜底: 每个城镇至少 市场 + 一种产业(旧存档/新占城镇自动补齐)
        private void EnsureTownBaseline()
        {
            try
            {
                string[] inds = { "weavery", "brewery", "tannery", "toolshop" };
                int added = 0;
                foreach (var s in Settlement.All)
                {
                    if (s == null || !s.IsTown || string.IsNullOrEmpty(s.StringId)) continue;
                    var sb = EconomyWorld.Of(s.StringId);
                    if (sb == null) continue;
                    bool hasInd = false;
                    for (int i = 0; i < sb.Groups.Count; i++)
                    {
                        var g = sb.Groups[i];
                        if (g == null || g.Count <= 0) continue;
                        var def = BuildDefs.Get(g.DefId);
                        if (def == null) continue;
                        if (def.Cat == BuildCat.Industry || def.Cat == BuildCat.Trade) { hasInd = true; break; }
                    }
                    if (hasInd) continue;
                    sb.GetOrCreate("market", BuildMode.Wood).Count += 1;
                    int h = (s.StringId.GetHashCode() & 0x7fffffff);
                    sb.GetOrCreate(inds[h % inds.Length], BuildMode.Wood).Count += 1;
                    added++;
                }
                if (added > 0) DLog.Force("经济: 城镇经济兜底补建 " + added + " 座城镇(市场+产业)");
            }
            catch (Exception ex) { DLog.Force("城镇兜底异常: " + ex.Message); }
        }

        // v4.62: 玩家国起步建筑补发(按版本; pk 未就绪时跳过, 不置位)
        private void TryStarterFill(string why)
        {
            try
            {
                if (_starterVersion >= StarterVer) return;
                var pk = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                if (pk == null) return;
                int n = EconomyWorld.StarterFill(pk);
                _starterVersion = StarterVer;
                if (n > 0) DLog.Force("经济: 起步建筑补发(" + why + ") v" + StarterVer + " +" + n + " 个");
                else DLog.Force("经济: 起步建筑检查(" + why + ") v" + StarterVer + " 无需补发");
            }
            catch (Exception ex) { DLog.Force("起步建筑补发失败: " + ex.Message); }
        }

        // 国库 = 玩家(国家意志)金钱(6.1)
        private void InitTreasury()
        {
            try
            {
                var hero = Hero.MainHero;
                EconomyWorld.Treasury.Gold = hero != null ? hero.Gold : 0;
                EconomyWorld.Treasury.LastGold = EconomyWorld.Treasury.Gold;
            }
            catch { }
        }

        // 开局预置建筑(2.8): 对所有国家生效
        private void PresetBuildings()
        {
            int villages = 0, towns = 0, castles = 0;
            try
            {
                foreach (var s in Settlement.All)
                {
                    if (s == null || string.IsNullOrEmpty(s.StringId)) continue;
                    var sb = EconomyWorld.Of(s.StringId);
                    if (s.IsVillage)
                    {
                        string res = EconomyWorld.PresetResourceForVillage(s.Village);
                        sb.GetOrCreate(res, BuildMode.Wood).Count += 1;
                        sb.GetOrCreate("charcoal_kiln", BuildMode.Wood).Count += 1;   // v4.64: 炭窑(铁链燃料)
                        // 特产不是粮食 -> 额外预置 1 个农田, 保证温饱
                        if (res != "farm") sb.GetOrCreate("farm", BuildMode.Wood).Count += 1;
                        villages++;
                    }
                    else if (s.IsTown)
                    {
                        sb.GetOrCreate("builder", BuildMode.Wood).Count += 2;
                        // 所有国家都要有基础经济(用户): 每城保底市场 + 一种产业
                        string[] inds = { "weavery", "brewery", "tannery", "toolshop" };
                        int h = (s.StringId != null ? s.StringId.GetHashCode() : 0) & 0x7fffffff;
                        sb.GetOrCreate("market", BuildMode.Wood).Count += 1;
                        sb.GetOrCreate(inds[h % inds.Length], BuildMode.Wood).Count += 1;
                        towns++;
                    }
                    else if (s.IsCastle)
                    {
                        sb.GetOrCreate("builder", BuildMode.Wood).Count += 1;
                        // v4.252: 城堡补一座采石场 —— 审计发现"石料"唯一产地 `quarry` 限定在城堡,
                        //   而城堡只预置建造部门、从不预置采石场 -> 开局全图石料恒为 0, 石造/铁造模式
                        //   与建造部门的石/铁档直接缺料, 全图"建造推进 0 / 完工 0"。
                        sb.GetOrCreate("quarry", BuildMode.Wood).Count += 1;
                        castles++;
                    }
                }
                DLog.Force("经济: 预置建筑完成 村庄=" + villages + " 城镇=" + towns + " 城堡=" + castles);
            }
            catch (Exception ex) { DLog.Force("预置建筑异常: " + ex.Message); }
        }

        // 村庄特产 -> 预置资源建筑(已移至 EconomyWorld.PresetResourceForVillage 共用)

        // ---------------- 每日结算 ----------------
        private void OnDailyTick()
        {
            try
            {
                int day = (int)CampaignTime.Now.ToDays;
                if (day == _lastDay) return;
                // v4.245: 上一天的异步日结还没跑完 -> 先收尾(保证每天结算完整、不丢步)
                try { if (DailyScheduler.Running) DailyScheduler.Flush(); } catch { }
                _lastDay = day;
                TickDay(day);
            }
            catch (Exception ex) { DLog.Force("经济日结异常: " + ex.Message); }
        }

        // v4.57: 每帧检查跨日 -> 到点即结算(不依赖 DailyTickEvent 的触发时机)
        private void OnTick(float dt)
        {
            try
            {
                DailyScheduler.Tick();   // v4.245: 每帧推进异步日结队列(每帧最多 3ms, 不卡帧)
                int day = (int)CampaignTime.Now.ToDays;
                if (day == _lastDay) return;
                OnDailyTick();
            }
            catch { }
        }

        // 每日结算时序(11.3): P1 只做数据维护与日志; P2 起接入产出/市场/建造/军需/财政
        // v4.245: 改为"异步分帧结算" —— 全部步骤登记进 DailyScheduler, 每帧只跑 3ms, 跨几帧跑完,
        //   顺序严格不变(语义与原来一口气跑完一致), 但跨日那一帧不再卡住(用户: 每日结算搞多线程异步结算)
        private void TickDay(int day)
        {
            try
            {
                DailyScheduler.Begin(day);
                DailyScheduler.Add("开局补发", delegate { TryStarterFill("日结"); });
                DailyScheduler.Add("国库基线", delegate
                {
                    var hero = Hero.MainHero;
                    EconomyWorld.Treasury.LastGold = EconomyWorld.Treasury.Gold;
                    EconomyWorld.Treasury.Gold = hero != null ? hero.Gold : 0;
                    // 关键顺序: 先记录"日初基线"(财政页"今日值"= 当前累计 - 日初基线), 再跑当日结算
                    Fiscal.DayRoll();
                    Fiscal.EndOfDayGold(EconomyWorld.Treasury.Gold);   // 国库日初基线(今日净额=实时国库-该基线)
                    InvestmentPool.DayReset();
                });
                // 建筑产出 / 消耗 -> 本地市场(11.3 步骤 2~3)
                DailyScheduler.Add("建筑产出", delegate { DailySettlement.Run(); });
                // v4.251: 建筑维护费(玩家国, 从国库出) —— "完全不管经济就崩盘"缺的那条持续成本
                DailyScheduler.Add("建筑维护", delegate
                {
                    int up = DailySettlement.ChargeUpkeep();
                    if (up > 0 && DLog.Flag("econ"))
                        DLog.Force("建筑维护: 本国 " + DailySettlement.LastUpkeepUnits + " 栋 -> -" + up + " 金/日");
                });
                // v4.252: 破产倒计时 —— 原来 NegativeDays/PenaltyDaysLeft 全仓没有自增点(死字段),
                //   于是 WarEconomy 的"玩家破产"判定与 Construction 的"破产建造 -75%"永不触发。
                //   现在国库见底就累加天数, 连续 7 天 -> 惩罚 30 天, 并且把破产次数记进账。
                DailyScheduler.Add("破产倒计时", delegate
                {
                    try
                    {
                        var t = EconomyWorld.Treasury;
                        if (t.Gold <= 0)
                        {
                            t.NegativeDays++;
                            if (t.NegativeDays >= 7 && t.PenaltyDaysLeft <= 0)
                            {
                                t.PenaltyDaysLeft = 30;
                                t.BankruptCount++;
                                DLog.Force("破产: 国库连续 " + t.NegativeDays + " 天见底 -> 未来 30 天建造效率 -75%, 第 "
                                    + t.BankruptCount + " 次");
                                try { MapSelection.Message("国库已连续 " + t.NegativeDays + " 天见底: 进入破产状态 30 天(建造效率 -75%, 领主讨债, 军饷告急)"); } catch { }
                            }
                        }
                        else t.NegativeDays = 0;
                        if (t.PenaltyDaysLeft > 0) t.PenaltyDaysLeft--;
                    }
                    catch { }
                });
                // v4.2: 税制四税取代旧的单一"定居点税收"(文档 20.1 的"替换"); SettlementTax 类保留未启用
                // v4.0: 税制四税 / 信贷利息 / 铸币权 / 经济统计(文档 20.1~20.3 / 20.9)
                DailyScheduler.Add("税制", delegate { TaxPolicy.Daily(); });
                DailyScheduler.Add("信贷", delegate { Credit.Daily(); });
                DailyScheduler.Add("铸币", delegate { MintRight.Daily(); });
                DailyScheduler.Add("统计快照", delegate { Stats.Capture(); });
                DailyScheduler.Add("立法", delegate { LawSystem.TickDay(day); });          // v5.0-P22: 立法推进(文档 24.3)
                DailyScheduler.Add("利益集团", delegate { InterestGroups.TickDay(day); });     // v5.0-P22: 政治运动激进度(文档 24.4)
                DailyScheduler.Add("革命", delegate { Revolution.TickDay(day); });         // v5.0-P23: 革命进度(文档 24.5)
                DailyScheduler.Add("贸易路线", delegate { TradeRoutes.Daily(day); });          // v5.0-P24: 贸易路线结算(文档 24.7)
                DailyScheduler.Add("战略储备", delegate { StrategicReserve.Daily(day); });     // v4.152: 战略储备(战时消耗/饥荒自动释放)
                DailyScheduler.Add("铁路", delegate { Railways.Daily(day); });             // v4.162: 铁路(工期推进/建成/中断)
                DailyScheduler.Add("动员", delegate { WarMobilization.Daily(day); });      // v5.0-P25: 动员到期检查
                DailyScheduler.Add("科技", delegate { Research.Daily(day); });             // v5.0-P26: 研究点累积
                DailyScheduler.Add("机构", delegate { Institutions.Daily(day); });         // v5.0-P26: 机构/文化效果
                DailyScheduler.Add("选举", delegate { Elections.Daily(day); });            // v5.0-P25/P26: 竞选期与开票(V3 官方)
                DailyScheduler.Add("权力集团", delegate { PowerBlocs.Weekly(day); });          // v5.0-P27: 集团凝聚力/授权(V3 官方)
                DailyScheduler.Add("政治", delegate { Politics.TickDay(day); });
                DailyScheduler.Add("AI 建设", delegate { try { AiDevelopment.Daily(day); } catch { } });   // v4.106: AI 国家自己建设(排队建筑/扣国库)
                DailyScheduler.Add("AI 战略缓存", delegate { try { foreach (var k in Kingdom.All) { if (k != null && !k.IsEliminated) AiDirector.Daily(k); } } catch { } });   // v5.0: AI 战略层日缓存(O(1))
                DailyScheduler.Add("AI 防守", delegate { try { AiDevelopment.DailyDefense(day); } catch { } });   // v4.111: AI 防守(解围/回防)
                DailyScheduler.Add("战争计划", delegate { try { WarPlans.DailyAll(day); } catch { } });   // v4.149: 战争计划评估(战况/阶段/撤退)
                // 月度结算: 按游戏历法(骑砍 1 月 = 7 天, 1 年 = 12 月 = 84 天) -> 每月第一天触发一次
                // (文档 19.15 原写"30 天"是误解, 见 v3.11 变更记录)
                if (TaleWorlds.CampaignSystem.CampaignTime.Now.GetDayOfWeek == 0)
                {
                    DailyScheduler.Add("月结·财政政治", delegate
                    {
                        try { InvestmentPool.Monthly(); Fiscal.Month(); Guilds.MonthlyFee(); TaxPolicy.Month(); InterestGroups.Monthly(day); Institutions.Monthly(); Politics.Month(day); } catch { }
                        try { int trib = PowerBlocs.EmpireTribute(); if (trib > 0) { EconomyWorld.TreasuryAdd(trib); Fiscal.Export += trib; } } catch { }   // v5.0-P27: 附庸贡金
                    });
                    DailyScheduler.Add("月结·AI 扩军", delegate { try { AiDevelopment.Monthly(day); } catch { } });   // v4.106: AI 国家自己扩军
                    DailyScheduler.Add("月结·AI 军团", delegate { try { AiDevelopment.MonthlyArmy(day); } catch { } });   // v4.110: AI 军团集结
                    DailyScheduler.Add("月结·AI 进攻", delegate { try { AiDevelopment.MonthlyOffensive(day); } catch { } });   // v4.109: AI 主动进攻
                    DailyScheduler.Add("月结·AI 外交", delegate
                    {
                        try { AiDevelopment.MonthlyPressure(day); } catch { }   // v4.112: AI 外交施压索贡
                        try { AiDevelopment.MonthlySanctions(day); } catch { }   // v4.115: AI 对外制裁(可制裁玩家)
                        try { AiDevelopment.MonthlyBalanceOfPower(day); } catch { }   // v4.117: 反霸权外交平衡
                    });
                    DailyScheduler.Add("月结·AI 经济", delegate
                    {
                        try { AiDevelopment.MonthlyEconomy(day); } catch { }   // v4.113: AI 战略储备/饥荒自救
                        try { AiEconomyDeep.Monthly(day); } catch { }
                    });
                    // v4.149: AI 深化(性格活体化 / 经济工具 / 机会主义反制 / 战争议会)
                    DailyScheduler.Add("月结·AI 深化", delegate
                    {
                        try { AiPersonality.MonthlyWatch(day); } catch { }
                        try { AiOpportunism.Monthly(day); } catch { }
                        try { WarPlans.CouncilAll(day); } catch { }
                    });
                    DailyScheduler.Add("月结·铁路", delegate
                    {
                        try { Railways.AiMonthly(day); } catch { }   // v4.165: AI 建线
                        try { Railways.AiTransportMonthly(day); } catch { }   // v4.166: AI 铁路运兵
                    });
                    DailyScheduler.Add("月结·AI 战略", delegate { try { foreach (var k in Kingdom.All) { if (k != null && !k.IsEliminated) AiDirector.Monthly(k); } } catch { } });   // v5.0: AI 战略层月结(目标/威胁/预算/战争意愿)
                }
                DailyScheduler.Add("落盘标记", delegate
                {
                    EconomyWorld.MarkDirty();
                    if (DLog.Flag("econ") && TaleWorlds.CampaignSystem.CampaignTime.Now.GetDayOfWeek == 0)
                        DLog.Force("经济日结: 第 " + day + " 天 -> " + EconomyWorld.Describe());
                });
            }
            catch (Exception ex) { DLog.Force("经济日结异常: " + ex.Message); }
        }
    }
}
