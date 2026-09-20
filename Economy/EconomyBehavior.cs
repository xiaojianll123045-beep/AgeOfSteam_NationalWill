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
        private string _inst = "";           // v5.0-P26: 机构/文化
        private string _bloc = "";           // v5.0-P27: 权力集团
        private string _interests = "";      // v5.0-P27: 利益宣示
        private int _lastDay = -1;
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
                    _inst = Institutions.Save();
                    _bloc = PowerBlocs.Save();
                    _interests = Interests.Save();
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
                    SyncChunks.Save(dataStore, "FIA_Inst", _inst);
                    SyncChunks.Save(dataStore, "FIA_Bloc", _bloc);
                    SyncChunks.Save(dataStore, "FIA_Interests", _interests);
                    dataStore.SyncData("FIA_EcoDay", ref _lastDay);   // 上次结算日(读档补结算用)
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
                    _inst = SyncChunks.Load(dataStore, "FIA_Inst");
                    _bloc = SyncChunks.Load(dataStore, "FIA_Bloc");
                    _interests = SyncChunks.Load(dataStore, "FIA_Interests");
                    dataStore.SyncData("FIA_EcoDay", ref _lastDay);
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
                    Research.Load(_research);       // v5.0-P26: 研究
                    Institutions.Load(_inst);       // v5.0-P26: 机构/文化
                    PowerBlocs.Load(_bloc);         // v5.0-P27: 权力集团
                    Interests.Load(_interests);     // v5.0-P27: 利益宣示
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
                Pops.EnsureInit("新战役");
                Politics.Reset();
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
                // 旧存档(本系统还没有数据) -> 自动初始化: 预置建筑 + 市场初值(11.2)
                if (!EconomyWorld.HasAnyData)
                {
                    PresetBuildings();
                    InitTreasury();
                    DLog.Force("经济: 旧存档无数据, 已自动初始化");
                }
                EnsureTownBaseline();   // 旧存档补建: 没有产业的城镇补市场+产业, 保证所有国家都跑经济
                TryStarterFill("读档");   // v4.62: 玩家国起步建筑补发(一次; 修复老档经济死锁)
                // 读档后立即补结算一次(不管当天是否已结算过; 否则读档当天没有产出/财政变化)
                try
                {
                    int today = (int)CampaignTime.Now.ToDays;
                    _lastDay = today;
                    TickDay(today);
                    DLog.Force("经济: 读档补结算(第 " + today + " 天)");
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
                int day = (int)CampaignTime.Now.ToDays;
                if (day == _lastDay) return;
                OnDailyTick();
            }
            catch { }
        }

        // 每日结算时序(11.3): P1 只做数据维护与日志; P2 起接入产出/市场/建造/军需/财政
        private void TickDay(int day)
        {
            try
            {
                TryStarterFill("日结");   // v4.62: 读档时 pk 未就绪则在首次日结补发
                var hero = Hero.MainHero;
                EconomyWorld.Treasury.LastGold = EconomyWorld.Treasury.Gold;
                EconomyWorld.Treasury.Gold = hero != null ? hero.Gold : 0;
                // 关键顺序: 先记录"日初基线"(财政页"今日值"= 当前累计 - 日初基线), 再跑当日结算
                Fiscal.DayRoll();
                Fiscal.EndOfDayGold(EconomyWorld.Treasury.Gold);   // 国库日初基线(今日净额=实时国库-该基线)
                InvestmentPool.DayReset();
                // 建筑产出 / 消耗 -> 本地市场(11.3 步骤 2~3)
                DailySettlement.Run();
                // v4.2: 税制四税取代旧的单一"定居点税收"(文档 20.1 的"替换"); SettlementTax 类保留未启用
                // v4.0: 税制四税 / 信贷利息 / 铸币权 / 经济统计(文档 20.1~20.3 / 20.9)
                TaxPolicy.Daily();
                Credit.Daily();
                MintRight.Daily();
                Stats.Capture();
                LawSystem.TickDay(day);          // v5.0-P22: 立法推进(文档 24.3)
                InterestGroups.TickDay(day);     // v5.0-P22: 政治运动激进度(文档 24.4)
                Revolution.TickDay(day);         // v5.0-P23: 革命进度(文档 24.5)
                TradeRoutes.Daily(day);          // v5.0-P24: 贸易路线结算(文档 24.7)
                WarMobilization.Daily(day);      // v5.0-P25: 动员到期检查
                Research.Daily(day);             // v5.0-P26: 研究点累积
                Institutions.Daily(day);         // v5.0-P26: 机构/文化效果
                Elections.Daily(day);            // v5.0-P25/P26: 竞选期与开票(V3 官方)
                PowerBlocs.Weekly(day);          // v5.0-P27: 集团凝聚力/授权(V3 官方)
                Politics.TickDay(day);
                try { AiDevelopment.Daily(day); } catch { }   // v4.106: AI 国家自己建设(排队建筑/扣国库)
                try { AiDevelopment.DailyDefense(day); } catch { }   // v4.111: AI 防守(解围/回防)
                // 月度结算: 按游戏历法(骑砍 1 月 = 7 天, 1 年 = 12 月 = 84 天) -> 每月第一天触发一次
                // (文档 19.15 原写"30 天"是误解, 见 v3.11 变更记录)
                if (TaleWorlds.CampaignSystem.CampaignTime.Now.GetDayOfWeek == 0)
                {
                    try { InvestmentPool.Monthly(); Fiscal.Month(); Guilds.MonthlyFee(); TaxPolicy.Month(); InterestGroups.Monthly(day); Institutions.Monthly(); Politics.Month(day); } catch { }
                    try { int trib = PowerBlocs.EmpireTribute(); if (trib > 0) { EconomyWorld.TreasuryAdd(trib); Fiscal.Export += trib; } } catch { }   // v5.0-P27: 附庸贡金
                    try { AiDevelopment.Monthly(day); } catch { }   // v4.106: AI 国家自己扩军
                    try { AiDevelopment.MonthlyArmy(day); } catch { }   // v4.110: AI 军团集结
                    try { AiDevelopment.MonthlyOffensive(day); } catch { }   // v4.109: AI 主动进攻
                    try { AiDevelopment.MonthlyPressure(day); } catch { }   // v4.112: AI 外交施压索贡
                    try { AiDevelopment.MonthlyEconomy(day); } catch { }   // v4.113: AI 战略储备/饥荒自救
                    try { AiDevelopment.MonthlySanctions(day); } catch { }   // v4.115: AI 对外制裁(可制裁玩家)
                    try { AiDevelopment.MonthlyBalanceOfPower(day); } catch { }   // v4.117: 反霸权外交平衡
                }
                EconomyWorld.MarkDirty();
                if (DLog.Flag("econ") && TaleWorlds.CampaignSystem.CampaignTime.Now.GetDayOfWeek == 0)
                    DLog.Force("经济日结: 第 " + day + " 天 -> " + EconomyWorld.Describe());
            }
            catch (Exception ex) { DLog.Force("经济日结异常: " + ex.Message); }
        }
    }
}
