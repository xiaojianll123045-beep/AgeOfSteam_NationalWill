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
                    DLog.Force("存档: 经济数据 -> " + EconomyWorld.Describe());
                }
                dataStore.SyncData("FIA_Buildings", ref _buildings);
                dataStore.SyncData("FIA_Market", ref _markets);
                dataStore.SyncData("FIA_National", ref _national);
                dataStore.SyncData("FIA_Treasury", ref _treasury);
                dataStore.SyncData("FIA_Private", ref _lords);
                dataStore.SyncData("FIA_Brush", ref _brushes);
                dataStore.SyncData("FIA_AutoBuild", ref _autoBuild);
                if (dataStore.IsLoading)
                {
                    EconomyWorld.LoadBuildings(_buildings);
                    EconomyWorld.LoadMarkets(_markets);
                    EconomyWorld.LoadNational(_national);
                    EconomyWorld.LoadTreasury(_treasury);
                    EconomyWorld.LoadLords(_lords);
                    EconomyWorld.LoadBrushes(_brushes);
                    EconomyWorld.LoadAutoBuild(_autoBuild);
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
                DLog.Force("经济: 新战役初始化 -> " + EconomyWorld.Describe());
            }
            catch (Exception ex) { DLog.Force("经济初始化异常: " + ex.Message); }
        }

        private void OnGameLoaded(CampaignGameStarter starter)
        {
            try
            {
                EconomyWorld.EnsureContainers();
                // 旧存档(本系统还没有数据) -> 自动初始化: 预置建筑 + 市场初值(11.2)
                if (!EconomyWorld.HasAnyData)
                {
                    PresetBuildings();
                    InitTreasury();
                    DLog.Force("经济: 旧存档无数据, 已自动初始化");
                }
                DLog.Force("经济: 读档完成 -> " + EconomyWorld.Describe());
            }
            catch (Exception ex) { DLog.Force("经济读档异常: " + ex.Message); }
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
                EconomyWorld.MarkDirty();
                if (DLog.Flag("econ") && day % 30 == 0)
                    DLog.Force("经济日结: 第 " + day + " 天 -> " + EconomyWorld.Describe());
            }
            catch (Exception ex) { DLog.Force("经济日结异常: " + ex.Message); }
        }
    }
}
