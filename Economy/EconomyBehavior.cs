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
        private int _lastDay = -1;

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
                    DLog.Force("存档: 经济数据 -> " + EconomyWorld.Describe());
                }
                dataStore.SyncData("FIA_Buildings", ref _buildings);
                dataStore.SyncData("FIA_Market", ref _markets);
                dataStore.SyncData("FIA_National", ref _national);
                dataStore.SyncData("FIA_Treasury", ref _treasury);
                dataStore.SyncData("FIA_Private", ref _lords);
                dataStore.SyncData("FIA_Brush", ref _brushes);
                dataStore.SyncData("FIA_AutoBuild", ref _autoBuild);
                dataStore.SyncData("FIA_Pops", ref _pops);
                dataStore.SyncData("FIA_Pool", ref _pool);
                dataStore.SyncData("FIA_Guild", ref _guild);
                dataStore.SyncData("FIA_Tax", ref _tax);
                dataStore.SyncData("FIA_Credit", ref _credit);
                dataStore.SyncData("FIA_Mint", ref _mint);
                dataStore.SyncData("FIA_Own", ref _own);
                dataStore.SyncData("FIA_Ledger2", ref _ledger2);
                dataStore.SyncData("FIA_Politics", ref _politics);
                if (dataStore.IsLoading)
                {
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
                    Politics.Load(_politics);
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
                        string res = PresetResourceForVillage(s.Village);
                        sb.GetOrCreate(res, BuildMode.Wood).Count += 1;
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

        // 村庄特产 -> 预置资源建筑(按 VillageType.StringId 判断)
        private static string PresetResourceForVillage(Village v)
        {
            try
            {
                if (v == null || v.VillageType == null) return "farm";
                string t = v.VillageType.StringId ?? "";
                if (t.Contains("wheat")) return "farm";
                if (t.Contains("lumber")) return "lumberjack";
                if (t.Contains("iron")) return "mine_iron";
                if (t.Contains("silver")) return "mine_silver";
                if (t.Contains("salt") || t.Contains("clay")) return "mine_clay_salt";
                if (t.Contains("fish")) return "fishery";
                if (t.Contains("vineyard") || t.Contains("date") || t.Contains("olive")
                    || t.Contains("silk") || t.Contains("flax")) return "specialty_farm";
                // 牛 / 羊 / 猪 / 猎户 / 各种马场 -> 牧场(马不在本系统商品表内, 先按牧场处理)
                if (t.Contains("cattle") || t.Contains("sheep") || t.Contains("swine")
                    || t.Contains("trapper") || t.Contains("horse")) return "pasture";
                return "farm";
            }
            catch { return "farm"; }
        }

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

        // 每日结算时序(11.3): P1 只做数据维护与日志; P2 起接入产出/市场/建造/军需/财政
        private void TickDay(int day)
        {
            try
            {
                var hero = Hero.MainHero;
                EconomyWorld.Treasury.LastGold = EconomyWorld.Treasury.Gold;
                EconomyWorld.Treasury.Gold = hero != null ? hero.Gold : 0;
                // 建筑产出 / 消耗 -> 本地市场(11.3 步骤 2~3)
                DailySettlement.Run();
                // v4.2: 税制四税取代旧的单一"定居点税收"(文档 20.1 的"替换"); SettlementTax 类保留未启用
                // v4.0: 税制四税 / 信贷利息 / 铸币权 / 经济统计(文档 20.1~20.3 / 20.9)
                TaxPolicy.Daily();
                Credit.Daily();
                MintRight.Daily();
                Stats.Capture();
                Politics.TickDay(day);
                // 月度结算: 按游戏历法(骑砍 1 月 = 7 天, 1 年 = 12 月 = 84 天) -> 每月第一天触发一次
                // (文档 19.15 原写"30 天"是误解, 见 v3.11 变更记录)
                if (TaleWorlds.CampaignSystem.CampaignTime.Now.GetDayOfWeek == 0)
                {
                    try { InvestmentPool.Monthly(); Fiscal.Month(); Guilds.MonthlyFee(); TaxPolicy.Month(); Politics.Month(day); } catch { }
                }
                EconomyWorld.MarkDirty();
                if (DLog.Flag("econ") && TaleWorlds.CampaignSystem.CampaignTime.Now.GetDayOfWeek == 0)
                    DLog.Force("经济日结: 第 " + day + " 天 -> " + EconomyWorld.Describe());
            }
            catch (Exception ex) { DLog.Force("经济日结异常: " + ex.Message); }
        }
    }
}
