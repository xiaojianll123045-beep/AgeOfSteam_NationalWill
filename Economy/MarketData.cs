using System;
using System.Collections.Generic;

namespace FeudalInternalAffairs
{
    // 城市小市场里的一种商品
    internal class MarketEntry
    {
        internal string GoodId;
        internal float Stock;             // 库存
        internal float Price;             // 本地价
        internal float PrevPrice;         // 昨日本地价(算变化%)
        internal float DailyProduction;   // 日产出(本地建筑)
        internal float DailyConsumption; // 日消耗(建筑 + 军队 + 民生)

        internal MarketEntry() { }

        internal MarketEntry(string id)
        {
            GoodId = id;
        }

        internal float Gap { get { return Math.Max(0f, DailyConsumption - DailyProduction); } }
    }

    // 一个城市的小市场(挂靠村庄与城堡的产出/消耗都汇入这里)
    internal class TownMarket
    {
        internal string TownId;
        internal Dictionary<string, MarketEntry> Entries = new Dictionary<string, MarketEntry>();

        internal TownMarket() { }

        internal TownMarket(string id)
        {
            TownId = id;
        }

        internal MarketEntry Get(string goodId)
        {
            MarketEntry e;
            return Entries.TryGetValue(goodId, out e) ? e : null;
        }

        internal MarketEntry GetOrCreate(string goodId)
        {
            var e = Get(goodId);
            if (e == null)
            {
                e = new MarketEntry(goodId);
                e.Price = FeudalGoods.BasePrice(goodId);
                Entries.Add(goodId, e);
            }
            return e;
        }

        internal float StockOf(string goodId)
        {
            var e = Get(goodId);
            return e != null ? e.Stock : 0f;
        }

        internal int Count { get { return Entries.Count; } }
    }

    // 全国大市场(汇总视图 + 基准价)
    internal class NationalMarket
    {
        internal Dictionary<string, float> BasePrice = new Dictionary<string, float>();  // 全国基准价
        internal Dictionary<string, float> PrevBasePrice = new Dictionary<string, float>(); // 昨日基准价
        internal Dictionary<string, float> BuyVolume = new Dictionary<string, float>();  // 全国买入单(日需求)
        internal Dictionary<string, float> SellVolume = new Dictionary<string, float>(); // 全国卖出单(日供给)

        internal float PriceOf(string goodId)
        {
            float p;
            return BasePrice.TryGetValue(goodId, out p) ? p : FeudalGoods.BasePrice(goodId);
        }

        internal void SetPrice(string goodId, float price)
        {
            if (string.IsNullOrEmpty(goodId)) return;
            if (BasePrice.ContainsKey(goodId)) BasePrice[goodId] = price;
            else BasePrice.Add(goodId, price);
        }

        internal void Reset()
        {
            BasePrice.Clear();
            BuyVolume.Clear();
            SellVolume.Clear();
            foreach (var g in FeudalGoods.All) BasePrice[g.Id] = g.BasePrice;
        }
    }

    // 市场参数(12.5)
    internal static class MarketRules
    {
        internal const float MinPriceFactor = 0.25f;    // 价格区间 25% ~ 175%
        internal const float MaxPriceFactor = 1.75f;
        internal const float MaxDailyPriceMove = 0.10f; // 日价格变动上限 10%
        internal const float MinScarcity = 0.5f;        // 本地稀缺系数区间
        internal const float MaxScarcity = 1.5f;
        internal const float TransferTrigger = 0.20f;   // 跨城调运触发价差 20%
        internal const float TransferFreight = 0.10f;   // 运费 +10%
        internal const float ImportPremium = 0.30f;     // 进口溢价 +30%
        internal const float FoodSafetyPremium = 0.50f; // 粮食保底进口溢价 +50%
        internal const float FoodSafetyDays = 3f;       // 粮食库存 < 3 天消耗时触发
        internal const float BaseStockLimit = 100f;     // 城市库存基础上限(12.10)
    }
}
