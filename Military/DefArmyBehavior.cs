using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.SaveSystem;

namespace FeudalInternalAffairs
{
    // 国防军的存档与结算入口(第 23 章)
    // 存档键: FIA_DefArmy
    internal class DefArmyBehavior : CampaignBehaviorBase
    {
        internal static DefArmyBehavior Current;

        private string _data = "";
        private int _lastDay = -1;

        internal DefArmyBehavior()
        {
            Current = this;
        }

        public override void RegisterEvents()
        {
            Current = this;
            CampaignEvents.OnNewGameCreatedEvent.AddNonSerializedListener(this, OnNewGameCreated);
            CampaignEvents.OnGameLoadedEvent.AddNonSerializedListener(this, OnGameLoaded);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
            CampaignEvents.TickEvent.AddNonSerializedListener(this, OnTick);   // v4.57: 跨日兜底
            CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, OnHourlyTick);
        }

        public override void SyncData(IDataStore dataStore)
        {
            try
            {
                if (dataStore.IsSaving)
                {
                    _data = DefArmy.Save();
                    SyncChunks.Save(dataStore, "FIA_DefArmy", _data);
                    SyncChunks.Save(dataStore, "FIA_LegEquip", DefArmy.SaveEquip());   // v27.x: 军团装备快照 + 满足率(库存归 FIA_Armory)
                    SyncChunks.Save(dataStore, "FIA_Armory", Armory.SaveArmory());    // v27.17: 两级库存逐型号(国家/领主)
                    SyncChunks.Save(dataStore, "FIA_Orders", Armory.SaveOrders());    // v27.17: 军用工厂订单(型号×数量×优先级)
                    SyncChunks.Save(dataStore, "FIA_WarEco", WarEconomy.Save());      // v4.72: 战争经济
                    SyncChunks.Save(dataStore, "FIA_WarWear", WarWeariness.Save());   // v4.73: 厌战度
                    SyncChunks.Save(dataStore, "FIA_AiPersona", AiPersonality.Save());   // v4.106: 国家性格
                    SyncChunks.Save(dataStore, "FIA_Contract", FeudalContracts.Save());   // v4.126: 封建契约
                    dataStore.SyncData("FIA_DefDay", ref _lastDay);   // 上次结算日(读档补结算用)
                }
                if (dataStore.IsLoading)
                {
                    _data = SyncChunks.Load(dataStore, "FIA_DefArmy");
                    WarEconomy.Load(SyncChunks.Load(dataStore, "FIA_WarEco"));
                    WarWeariness.Load(SyncChunks.Load(dataStore, "FIA_WarWear"));
                    AiPersonality.Load(SyncChunks.Load(dataStore, "FIA_AiPersona"));
                    FeudalContracts.Load(SyncChunks.Load(dataStore, "FIA_Contract"));   // v4.126
                    dataStore.SyncData("FIA_DefDay", ref _lastDay);
                    DefArmy.Load(_data);
                    DefArmy.LoadEquip(SyncChunks.Load(dataStore, "FIA_LegEquip"));   // 旧档无此段 -> 27.15 迁移
                    Armory.LoadArmory(SyncChunks.Load(dataStore, "FIA_Armory"));     // v27.17: 军械库(旧档无 -> 空库)
                    Armory.LoadOrders(SyncChunks.Load(dataStore, "FIA_Orders"));     // v27.17: 工厂订单(旧档无 -> 无订单)
                }
            }
            catch (Exception ex) { DLog.Force("国防军 SyncData 异常: " + ex); }
        }

        private void OnNewGameCreated(CampaignGameStarter starter)
        {
            try { DefArmy.Reset(); WarEconomy.Reset(); WarWeariness.Reset(); AiPersonality.ResetNewCampaign(); FeudalContracts.Reset(); }
            catch (Exception ex) { DLog.Force("国防军初始化异常: " + ex.Message); }
        }

        private void OnGameLoaded(CampaignGameStarter starter)
        {
            try
            {
                DefArmy.Relink();
                // v4.245: 清掉上一局残留的异步日结队列(读档不继承旧队列)
                try { DailyScheduler.Reset(); } catch { }
                // 读档后立即补结算一次(军饷/军粮/对账/征兵)
                try
                {
                    int today = (int)CampaignTime.Now.ToDays;
                    _lastDay = today;
                    DefArmy.Daily(today);
                    WarEconomy.Daily(today);      // v4.72
                    WarWeariness.Daily(today);    // v4.73
                    DLog.Force("国防军: 读档补结算(第 " + today + " 天)");
                }
                catch (Exception ex) { DLog.Force("国防军补结算异常: " + ex.Message); }
            }
            catch (Exception ex) { DLog.Force("国防军重连异常: " + ex.Message); }
        }

        private void OnDailyTick()
        {
            try
            {
                int day = (int)CampaignTime.Now.ToDays;
                if (day == _lastDay) return;
                _lastDay = day;
                // v4.245: 军事日结也走异步分帧队列(与经济活动共用一个调度器; 军费/军粮要在经济之后)
                DailyScheduler.Add("国防军", delegate { try { DefArmy.Daily(day); } catch (Exception ex) { DLog.Force("国防军日结异常: " + ex.Message); } });
                DailyScheduler.Add("军团组织", delegate { try { ArmyDoctrine.Daily(day); } catch { } });      // v4.205: 军团组织度/士气/伤员/每周损耗推进
                DailyScheduler.Add("战争经济", delegate { try { WarEconomy.Daily(day); } catch { } });        // v4.72: 经济-军事联动(粮/钱/装备/焦土)
                DailyScheduler.Add("厌战度", delegate { try { WarWeariness.Daily(day); } catch { } });      // v4.73: 厌战度
            }
            catch (Exception ex) { DLog.Force("国防军日结异常(入口): " + ex.Message); }
        }

        // v4.57: 每帧检查跨日(兜底 DailyTickEvent 未触发)
        private void OnTick(float dt)
        {
            try
            {
                WarWeariness.Tick();   // v4.73: 停战请求弹窗/接受执行(每帧安全时机)
                int day = (int)CampaignTime.Now.ToDays;
                if (day == _lastDay) return;
                OnDailyTick();
            }
            catch { }
        }

        private void OnHourlyTick()
        {
            try { DefArmy.Hourly(); }
            catch { }
        }
    }
}
