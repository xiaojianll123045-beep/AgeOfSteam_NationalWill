using System;
using System.Collections.Generic;

namespace FeudalInternalAffairs
{
    // 需求中的一种商品(文档 19.3: 权重 / 最小最大市场份额照搬 V3)
    internal class NeedEntry
    {
        internal string Good;
        internal float Weight;
        internal float MinShare;
        internal float MaxShare;
    }

    // 需求定义(文档 19.3 / 19.4.2)
    internal class NeedDef
    {
        internal string Id;
        internal string Name;
        internal int Curve;        // 0=临时 1=常量 2=指数
        internal int Start;        // 出现财富等级
        internal int Peak;         // 峰值/常量起点(临时: 峰值等级; 常量: 恒定起点; 指数: unused)
        internal int End;          // 消失等级(临时; 0=不消失)
        internal float V1;         // 起始金额
        internal float V2;         // 峰值/恒定/末端金额
        internal float Conv;       // 实物量同构折算系数 = 本mod默认商品基础价 / V3默认商品基础价
        internal List<NeedEntry> Goods = new List<NeedEntry>();
    }

    // 需求总表 + 买包曲线(文档 19.3 / 19.4)
    internal static class PopNeeds
    {
        internal static string NameOf(string id)
        {
            for (int i = 0; i < All.Count; i++) if (All[i].Id == id) return All[i].Name;
            return id;
        }
        internal static readonly List<NeedDef> All = new List<NeedDef>
        {
            new NeedDef { Id = "basic_food", Name = "基本食物", Curve = 0, Start = 1, Peak = 19, End = 29, V1 = 90f, V2 = 168f, Conv = 13f / 20f, Goods = {
                // v4.252: 给非粮食主食加"最小份额" 0.05 —— 原来 MinShare 全 0, 加上"份额=当日产量"的算法,
                //   某城只要有一座农田就会把整个主食买包砸在粮食上, 鱼/肉/枣/橄榄一条也卖不出去
                E(FeudalGoods.Grain, 0.85f, 0f, 0.9f), E(FeudalGoods.Fish, 1f, 0.05f, 0.9f),
                E(FeudalGoods.Meat, 1f, 0.05f, 0.9f), E(FeudalGoods.DateFruit, 1f, 0.05f, 0.9f), E(FeudalGoods.Olives, 1.15f, 0.05f, 0.9f),
                E(FeudalGoods.Groceries, 1.1f, 0.05f, 0.9f) } },
            new NeedDef { Id = "luxury_food", Name = "奢侈食物", Curve = 2, Start = 20, End = 0, V1 = 8f, V2 = 3859f, Conv = 30f / 30f, Goods = {
                E(FeudalGoods.Meat, 1.25f, 0.1f, 0.75f), E(FeudalGoods.DateFruit, 0.75f, 0.1f, 0.75f),
                E(FeudalGoods.Spice, 1.5f, 0f, 1f), E(FeudalGoods.Salt, 0.5f, 0f, 0.5f) } },
            new NeedDef { Id = "simple_clothing", Name = "简陋衣物", Curve = 0, Start = 1, Peak = 9, End = 14, V1 = 23f, V2 = 43f, Conv = 22f / 20f, Goods = {
                E(FeudalGoods.Wool, 1f, 0f, 0.5f), E(FeudalGoods.Leather, 2f, 0f, 1f) } },
            new NeedDef { Id = "standard_clothing", Name = "标准衣物", Curve = 0, Start = 10, Peak = 25, End = 39, V1 = 7f, V2 = 161f, Conv = 245f / 30f, Goods = {
                E(FeudalGoods.Linen, 1f, 0f, 1f), E(FeudalGoods.Clothes, 1.2f, 0f, 1f) } },
            new NeedDef { Id = "crude_items", Name = "粗糙用品", Curve = 0, Start = 5, Peak = 9, End = 14, V1 = 13f, V2 = 43f, Conv = 25f / 20f, Goods = {
                E(FeudalGoods.Hardwood, 1f, 0f, 0.5f), E(FeudalGoods.Clay, 2f, 0f, 1f) } },
            new NeedDef { Id = "household_items", Name = "家居用品", Curve = 0, Start = 10, Peak = 31, End = 44, V1 = 7f, V2 = 238f, Conv = 210f / 30f, Goods = {
                E(FeudalGoods.Pottery, 1f, 0.1f, 0.75f), E(FeudalGoods.Tools, 1f, 0f, 0.5f), E(FeudalGoods.Leather, 0.5f, 0f, 0.5f),
                E(FeudalGoods.Furniture, 1.2f, 0f, 0.75f), E(FeudalGoods.Glass, 0.8f, 0f, 0.5f), E(FeudalGoods.Paper, 0.6f, 0f, 0.5f) } },
            new NeedDef { Id = "luxury_items", Name = "奢侈品", Curve = 2, Start = 15, End = 0, V1 = 11f, V2 = 9648f, Conv = 100f / 40f, Goods = {
                E(FeudalGoods.Silver, 0.5f, 0.1f, 0.25f), E(FeudalGoods.Spice, 1f, 0.1f, 0.5f),
                E(FeudalGoods.Leather, 1f, 0.1f, 0.5f), E(FeudalGoods.Tools, 0.5f, 0.1f, 0.5f),
                E(FeudalGoods.Silk, 0.8f, 0.1f, 0.5f), E(FeudalGoods.Gold, 0.4f, 0.1f, 0.25f), E(FeudalGoods.Radios, 0.5f, 0.1f, 0.5f),
                E(FeudalGoods.Telephones, 0.5f, 0.1f, 0.5f), E(FeudalGoods.Automobiles, 0.3f, 0.1f, 0.25f) } },
            new NeedDef { Id = "heating", Name = "取暖", Curve = 1, Start = 1, Peak = 10, End = 0, V1 = 0f, V2 = 26f, Conv = 25f / 20f, Goods = {
                E(FeudalGoods.Hardwood, 0.75f, 0f, 0.5f), E(FeudalGoods.Charcoal, 2f, 0f, 0.8f) } },
            new NeedDef { Id = "intoxicants", Name = "麻醉品", Curve = 1, Start = 1, Peak = 30, End = 0, V1 = 0f, V2 = 216f, Conv = 50f / 30f, Goods = {
                E(FeudalGoods.Beer, 1f, 0f, 0.75f), E(FeudalGoods.Grape, 0.9f, 0f, 0.75f),
                E(FeudalGoods.Spice, 0.75f, 0f, 0.75f), E(FeudalGoods.Wine, 0.25f, 0f, 0.25f),
                E(FeudalGoods.Tobacco, 0.9f, 0f, 0.75f), E(FeudalGoods.Opium, 0.4f, 0f, 0.5f) } },
            new NeedDef { Id = "stimulants", Name = "提神品", Curve = 0, Start = 6, Peak = 23, End = 30, V1 = 7f, V2 = 56f, Conv = 50f / 30f, Goods = {
                E(FeudalGoods.DateFruit, 1f, 0f, 0.75f), E(FeudalGoods.Spice, 0.95f, 0f, 0.75f), E(FeudalGoods.Salt, 0.8f, 0f, 0.75f),
                E(FeudalGoods.Coffee, 1.1f, 0f, 0.75f), E(FeudalGoods.Tea, 1.1f, 0f, 0.75f), E(FeudalGoods.Sugar, 0.9f, 0f, 0.75f) } },
            new NeedDef { Id = "luxury_drinks", Name = "奢侈饮品", Curve = 2, Start = 15, End = 0, V1 = 21f, V2 = 3859f, Conv = 300f / 50f, Goods = {
                E(FeudalGoods.Spice, 1f, 0f, 0.75f), E(FeudalGoods.Wine, 0.45f, 0f, 0.33f) } },
            new NeedDef { Id = "services", Name = "服务", Curve = 2, Start = 10, End = 0, V1 = 24f, V2 = 6561f, Conv = 30f / 30f, Goods = {
                E(FeudalGoods.Service, 1f, 0f, 1f) } },
            new NeedDef { Id = "free_movement", Name = "自由迁移", Curve = 2, Start = 10, End = 0, V1 = 3f, V2 = 3859f, Conv = 150f / 30f, Goods = {
                E(FeudalGoods.Horse, 1f, 0f, 0.75f), E(FeudalGoods.Service, 1.25f, 0f, 1f) } },
            new NeedDef { Id = "communication", Name = "通讯", Curve = 2, Start = 20, End = 0, V1 = 16f, V2 = 3859f, Conv = 150f / 30f, Goods = {
                E(FeudalGoods.Service, 1f, 0f, 0.75f), E(FeudalGoods.Horse, 2f, 0f, 1f) } },
            new NeedDef { Id = "leisure", Name = "休闲", Curve = 2, Start = 20, End = 0, V1 = 16f, V2 = 9648f, Conv = 30f / 30f, Goods = {
                E(FeudalGoods.Service, 0.1f, 0f, 1f), E(FeudalGoods.Weapons, 0.75f, 0f, 0.25f),
                E(FeudalGoods.Silver, 0.5f, 0f, 0.5f), E(FeudalGoods.Horse, 0.75f, 0f, 0.25f) } }
        };

        private static NeedEntry E(string good, float w, float min, float max)
        {
            return new NeedEntry { Good = good, Weight = w, MinShare = min, MaxShare = max };
        }

        internal static List<string> MainGoodIds()
        {
            var l = new List<string>();
            foreach (var g in FeudalGoods.Main) l.Add(g.Id);
            return l;
        }

        // 某财富等级下该类需求的"应花费金额"(买包值, 文档 19.4.2 曲线 × 折算系数)
        internal static float PackageValue(NeedDef n, int wealth)
        {
            if (n == null || wealth < n.Start) return 0f;
            float v;
            if (n.Curve == 0)
            {
                // 临时: 起点线性升到峰值等级, 再线性降到消失等级
                if (wealth <= n.Peak)
                    v = n.V1 + (n.V2 - n.V1) * (wealth - n.Start) / Math.Max(1, n.Peak - n.Start);
                else if (n.End <= 0)
                    v = n.V2;
                else if (wealth >= n.End)
                    return 0f;
                else
                    v = n.V2 * (n.End - wealth) / Math.Max(1, n.End - n.Peak);
            }
            else if (n.Curve == 1)
            {
                // 常量: 出现后线性升到恒定值
                v = wealth >= n.Peak ? n.V2 : n.V2 * (wealth - n.Start + 1) / Math.Max(1, n.Peak - n.Start + 1);
            }
            else
            {
                // 指数: 起点 → 60 级末端 的几何插值
                float t = (wealth - n.Start) / (float)Math.Max(1, 60 - n.Start);
                if (t < 0f) t = 0f;
                v = n.V2 > 0f && n.V1 > 0f ? n.V1 * (float)Math.Pow(n.V2 / n.V1, t) : n.V1;
            }
            return v * n.Conv;
        }

        // 含文化修正的类金额(痴迷 +25% / 禁忌 −25%; 文档 19.4.4)
        internal static float ValueFor(NeedDef n, int wealth, string culture)
        {
            float v = PackageValue(n, wealth);
            if (v <= 0f || n == null) return v;
            bool obs = false, tab = false;
            foreach (var e in n.Goods)
            {
                if (culture == null) break;
                if (PopDefs.ObsessionOf(culture) == e.Good) obs = true;
                if (PopDefs.IsTaboo(culture, e.Good)) tab = true;
            }
            if (obs) v *= 1.25f;
            if (tab) v *= 0.75f;
            return v;
        }

        // 单个商品的购买权重(文档 19.4.4): 权重 × (份额区间内) × 痴迷×2 / 禁忌×0.5
        internal static float BuyWeight(NeedEntry e, float marketShare, string culture)
        {
            if (e == null) return 0f;
            float w = e.Weight;
            if (e.MinShare > 0f && marketShare < e.MinShare) w = 0f;          // 低于最小份额 → 仍按权重(必买商品不缩水, 见文档)
            if (marketShare <= 0f && e.MinShare <= 0f) w = 0f;               // 市场无卖单 → 不买(除非有最小份额)
            if (e.MaxShare > 0f && e.MaxShare < 1f && marketShare > e.MaxShare) w = Math.Min(w, e.Weight); // 超上限不再加成
            if (culture != null)
            {
                if (PopDefs.ObsessionOf(culture) == e.Good && w > 0f) { if (w < 1f) w = 1f; w *= 2f; }
                if (PopDefs.IsTaboo(culture, e.Good)) w *= 0.5f;
            }
            return w;
        }
    }
}
