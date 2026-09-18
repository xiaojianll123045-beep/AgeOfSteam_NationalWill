using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;

namespace FeudalInternalAffairs
{
    // 市场结算(设计第 5 章, 挂在每日结算的"市场结算"步骤):
    //   1) 汇总全国买卖 -> 全国基准价(5.2.1)
    //   2) 每城本地价 = 全国基准价 × 本地稀缺系数(5.2) + 市场建筑改善
    //   3) 民用消耗(生活用品) + 繁荣度循环(5.6/12.9)
    //   4) 跨城调运(5.4, 同国城市间)
    //   5) 进出口(5.5, 有贸易站的城市)
    internal static class MarketSim
    {
        // 铸币账本(文档 19.9.3; 正 = 净创造); PlayerToday = 玩家王国市场的份额(负 = 净销毁, 计入财政支出)
        internal static float LedgerToday, LedgerTotal, LedgerTodayPlayer;
        internal static int LastImported, LastExported;   // 统计页(文档 20.9)
        // 净销毁由国库承担的比例(v3.9): 全额承担每天 -5.9K 直接把国库击穿, 只按铸币税口径承担 20%
        // (其余按文档 19.9.3 由买方自行消化; 数值可调)
        internal const float BurnTreasuryShare = 0.20f;
        private static readonly Dictionary<string, bool> _playerMkt = new Dictionary<string, bool>();
        // 返回 [调运件数, 进口件数, 繁荣增量*10]
        internal static int[] Run()
        {
            int moved = 0, imported = 0, prosp = 0;
            try
            {
                PopSim.DailyConsume(ref prosp);   // 【必须最先】人口按买包消费(文档 19.13.2 第 2 步); 旧 5.6 民用循环已被 19.x 取代
                LedgerToday = 0f;
                NationalPrices();          // 全国基准价 + 汇总买卖量(含今日消耗)
                // 铸币: 有铸币厂时按 20.3 铸币权结算(这里只记账); 否则维持"净创造入国库/销毁按20%计支出"
                if (!MintRight.HasMint)
                {
                    int mint = (int)LedgerTodayPlayer;
                    if (mint > 0) { EconomyWorld.TreasuryAdd(mint); Fiscal.AddMint(mint); }
                    else if (mint < 0)
                    {
                        int burn = (int)(-mint * BurnTreasuryShare);
                        if (burn > 0) { EconomyWorld.TreasurySpend(burn); Fiscal.AddBurn(burn); }
                    }
                }
                LocalPrices();
                moved = Transfers();
                int imp = Imports();
                int exp = Exports();
                imported = imp + exp;
                LastImported = imp;
                LastExported = exp;
                Caravans.Daily();          // 商队损耗(文档 20.10)
            }
            catch (Exception ex) { DLog.Force("市场结算异常: " + ex.Message); }
            return new[] { moved, imported, prosp };
        }

        // ==================== 1) 全国基准价 ====================
        private static void NationalPrices()
        {
            var n = EconomyWorld.National;
            var pk = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
            CachePlayerMarkets(pk);
            LedgerTodayPlayer = 0f;
            foreach (var g in FeudalGoods.Main)
            {
                float buy = 0f, sell = 0f;
                float pBuy = 0f, pSell = 0f;
                foreach (var kv in EconomyWorld.Markets)
                {
                    var m = kv.Value;
                    if (m == null) continue;
                    var e = m.Get(g.Id);
                    if (e == null) continue;
                    buy += e.BuyOrders;
                    sell += e.DailyProduction;
                    bool own;
                    if (_playerMkt.TryGetValue(kv.Key, out own) && own) { pBuy += e.BuyOrders; pSell += e.DailyProduction; }
                }
                float denom = Math.Min(buy, sell);
                if (denom < 0.01f) denom = 0.01f;
                float t = (buy - sell) / denom;                 // 文档 19.9.2 V3 精确式: (买-卖)/min(买,卖)
                if (t > 1f) t = 1f;
                if (t < -1f) t = -1f;
                float target = g.BasePrice * (1f + 0.75f * t);  // 25%~175% 基础价
                float cur = n.PriceOf(g.Id);
                if (n.PrevBasePrice.ContainsKey(g.Id)) n.PrevBasePrice[g.Id] = cur;
                else n.PrevBasePrice.Add(g.Id, cur);
                n.SetPrice(g.Id, MoveTowards(cur, target, MarketRules.MaxDailyPriceMove));
                // 铸币成色 -> 物价通胀(文档 20.3: 成色越低物价涨得越快)
                if (MintRight.DailyInflation > 0f) n.SetPrice(g.Id, n.PriceOf(g.Id) * (1f + MintRight.DailyInflation));
                if (n.BuyVolume.ContainsKey(g.Id)) n.BuyVolume[g.Id] = buy; else n.BuyVolume.Add(g.Id, buy);
                if (n.SellVolume.ContainsKey(g.Id)) n.SellVolume[g.Id] = sell; else n.SellVolume.Add(g.Id, sell);
                // 铸币/烧钱账本(文档 19.9.3): 卖>买 市场净创造货币, 买>卖 净销毁
                float ledger = (sell - buy) * n.PriceOf(g.Id);
                LedgerToday += ledger;
                LedgerTotal += ledger;
                LedgerTodayPlayer += (pSell - pBuy) * n.PriceOf(g.Id);
            }
        }

        // 玩家王国市场缓存(每日一次; 铸币收支只算自己王国)
        private static void CachePlayerMarkets(Kingdom pk)
        {
            _playerMkt.Clear();
            if (pk == null) return;
            try
            {
                foreach (var kv in EconomyWorld.Markets)
                {
                    bool own = false;
                    try
                    {
                        var sid = kv.Key;
                        foreach (var s in Settlement.All)
                        {
                            if (s == null || s.StringId != sid) continue;
                            own = s.MapFaction == pk;
                            break;
                        }
                    }
                    catch { }
                    _playerMkt[kv.Key] = own;
                }
            }
            catch { }
        }

        // ==================== 2) 本地价 ====================
        private static void LocalPrices()
        {
            foreach (var kv in EconomyWorld.Markets)
            {
                var m = kv.Value;
                if (m == null || string.IsNullOrEmpty(m.TownId)) continue;
                    float bonus = PriceBonusOf(m.TownId);   // 市场建筑的价格改善(负值 = 改善)
                    foreach (var g in FeudalGoods.Main)
                    {
                        var e = m.Get(g.Id);
                        if (e == null) continue;
                        // 本地价 = V3 精确式(本地买卖单); 实际价 = MAPI×全国价 + (1-MAPI)×本地价(文档 19.9.2)
                        float lBuy = e.BuyOrders, lSell = e.DailyProduction;
                        float ld = Math.Min(lBuy, lSell);
                        if (ld < 0.01f) ld = 0.01f;
                        float lt = (lBuy - lSell) / ld;
                        if (lt > 1f) lt = 1f;
                        if (lt < -1f) lt = -1f;
                        float localPrice = g.BasePrice * (1f + 0.75f * lt) * (1f + bonus);
                        float basePrice = EconomyWorld.National.PriceOf(g.Id);
                        float mapi = MapiOf(m.TownId);
                        float target = mapi * basePrice + (1f - mapi) * localPrice;
                    if (target < g.BasePrice * MarketRules.MinPriceFactor) target = g.BasePrice * MarketRules.MinPriceFactor;
                    if (target > g.BasePrice * MarketRules.MaxPriceFactor) target = g.BasePrice * MarketRules.MaxPriceFactor;
                    e.PrevPrice = e.Price;
                    e.Price = MoveTowards(e.Price, target, MarketRules.MaxDailyPriceMove);
                }
            }
        }

        // 市场接入度 -> MAPI(文档 19.9.2): 市场/贸易站/驿站/仓库/粮仓提供接入分(基准 20), 结果 0.5~1.0
        internal static float MapiOf(string townId)
        {
            try
            {
                var sb = EconomyWorld.Find(townId);
                if (sb == null) return 0.5f;
                float score = 10f;   // 城镇自带基础道路/市集
                for (int i = 0; i < sb.Groups.Count; i++)
                {
                    var grp = sb.Groups[i];
                    if (grp == null || grp.Count <= 0) continue;
                    switch (grp.DefId)
                    {
                        case "market": score += 6f * grp.Count; break;
                        case "trade_post": score += 6f * grp.Count; break;
                        case "caravan_post": score += 5f * grp.Count; break;
                        case "warehouse": score += 3f * grp.Count; break;
                        case "granary": score += 3f * grp.Count; break;
                    }
                }
                float v = 0.5f + 0.5f * (score / 20f);
                if (v > 1f) v = 1f;
                if (v < 0.5f) v = 0.5f;
                return v;
            }
            catch { return 0.5f; }
        }

        // ==================== 3) 民用消耗 + 繁荣 ====================
        private static void CivilianTick(ref int prosp10)
        {
            foreach (var t in Town.AllTowns)
            {
                if (t == null || t.Settlement == null) continue;
                var roster = t.Settlement.ItemRoster;
                if (roster == null) continue;
                var m = EconomyWorld.Markets.ContainsKey(t.Settlement.StringId) ? EconomyWorld.Markets[t.Settlement.StringId] : null;

                float prosperity = t.Prosperity;
                // 粮食消耗(账目口径: 民生池由 13.5 的食物模型结算, 这里只记入市场流量)
                float foodNeed = prosperity / 1000f;
                if (m != null)
                {
                    var ge = m.GetOrCreate(FeudalGoods.Grain);
                    ge.DailyConsumption += foodNeed;
                }

                // 生活用品: 布/酒/陶器 三者合计 繁荣/2000, 直接从市场扣(物理消耗 = "售出")
                float livingNeed = prosperity / 2000f;
                float per = livingNeed / 3f;
                float sold = 0f;
                sold += TakeGood(roster, m, FeudalGoods.Linen, per);
                sold += TakeGood(roster, m, FeudalGoods.Beer, per);
                sold += TakeGood(roster, m, FeudalGoods.Pottery, per);

                // 繁荣度: 每 2 单位售出 +1, 每城每日上限 +2(5.6)
                float gain = Math.Min(2f, sold / 2f);
                if (gain > 0.001f)
                {
                    t.Prosperity += gain;
                    prosp10 += (int)Math.Round(gain * 10f);
                }
            }
        }

        private static float TakeGood(TaleWorlds.CampaignSystem.Roster.ItemRoster roster, TownMarket m, string goodId, float want)
        {
            try
            {
                var item = FeudalGoods.Item(goodId);
                if (item == null || want <= 0f) return 0f;
                int have = roster.GetItemNumber(item);
                if (have <= 0) return 0f;
                int take = MBRandom.RoundRandomized(want);
                if (take > have) take = have;
                if (take <= 0) return 0f;
                roster.AddToCounts(item, -take);
                if (m != null) m.GetOrCreate(goodId).DailyConsumption += take;
                return take;
            }
            catch { return 0f; }
        }

        // ==================== 4) 跨城调运(5.4) ====================
        private static int Transfers()
        {
            int moved = 0;
            try
            {
                foreach (var g in FeudalGoods.Main)
                {
                    // 按国家分组: 同国城市之间才能调运
                    foreach (var kingdom in Kingdom.All)
                    {
                        if (kingdom == null || kingdom.IsEliminated) continue;
                        var list = new List<Town>();
                        foreach (var t in kingdom.Fiefs)
                        {
                            if (t == null || !t.IsTown || t.Settlement == null) continue;
                            var m = EconomyWorld.FindMarket(t.Settlement.StringId);
                            if (m != null && m.Get(g.Id) != null) list.Add(t);
                        }
                        if (list.Count < 2) continue;

                        // 按本地价升序(便宜的先当卖方)
                        list.Sort(delegate (Town a, Town b)
                        {
                            float pa = EconomyWorld.FindMarket(a.Settlement.StringId).Get(g.Id).Price;
                            float pb = EconomyWorld.FindMarket(b.Settlement.StringId).Get(g.Id).Price;
                            return pa.CompareTo(pb);
                        });

                        for (int i = 0; i < list.Count; i++)
                        {
                            for (int j = list.Count - 1; j > i; j--)
                            {
                                var cheap = list[i];
                                var dear = list[j];
                                var mc = EconomyWorld.FindMarket(cheap.Settlement.StringId);
                                var md = EconomyWorld.FindMarket(dear.Settlement.StringId);
                                if (mc == null || md == null) continue;
                                var ec = mc.Get(g.Id);
                                var ed = md.Get(g.Id);
                                if (ec == null || ed == null || ec.Price <= 0.01f) continue;
                                float gap = (ed.Price - ec.Price) / ec.Price;
                                if (gap < MarketRules.TransferTrigger) continue;   // 价差 < 20% 不调运

                                int cap = TradeCapacityOf(dear.Settlement.StringId);      // 买方城市的贸易容量
                                if (cap <= 0) continue;
                                var item = FeudalGoods.Item(g.Id);
                                if (item == null) continue;
                                var sellerRoster = cheap.Settlement.ItemRoster;
                                var buyerRoster = dear.Settlement.ItemRoster;
                                if (sellerRoster == null || buyerRoster == null) continue;

                                int have = sellerRoster.GetItemNumber(item);
                                int reserve = 20;   // 保留本地底仓, 不搬空
                                int can = Math.Min(cap, have - reserve);
                                if (can <= 0) continue;
                                int freight = Math.Max(1, can / 10);          // 运费 = 10% 损耗
                                int arrive = can - freight;
                                if (arrive <= 0) continue;

                                sellerRoster.AddToCounts(item, -can);
                                buyerRoster.AddToCounts(item, arrive);
                                ec.DailyProduction -= 0f;   // 流量不变(库存搬运)
                                moved += arrive;
                                if (DLog.Flag("econ"))
                                    DLog.Info("调运: " + g.Id + " " + arrive + " 件 " + cheap.Name + " -> " + dear.Name
                                        + " (价差 " + ((int)(gap * 100)) + "%)");
                            }
                        }
                    }
                }
            }
            catch (Exception ex) { DLog.Info("调运异常: " + ex.Message); }
            return moved;
        }

        // 交战判定 / 同盟计数(19.9.4)
        private static bool AnyWar(Kingdom pk)
        {
            try
            {
                foreach (var k in Kingdom.All)
                {
                    if (k == null || k == pk || k.IsEliminated) continue;
                    if (FactionManager.IsAtWarAgainstFaction(pk, k)) return true;
                }
            }
            catch { }
            return false;
        }

        private static int AllyCount(Kingdom pk)
        {
            int n = 0;
            try
            {
                foreach (var k in Kingdom.All)
                {
                    if (k == null || k == pk || k.IsEliminated) continue;
                    if (pk.IsAllyWith(k)) n++;
                }
            }
            catch { }
            return n;
        }

        // 出口(19.9.4): 本地价远低于基准 -> 卖到国外, 收入入国库
        private static int Exports()
        {
            int exported = 0;
            try
            {
                var pk = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                if (pk == null || AnyWar(pk)) return 0;
                bool ally = AllyCount(pk) > 0;
                float capMult = ally ? 1.5f : 1f;
                foreach (var t in pk.Fiefs)
                {
                    if (t == null || !t.IsTown || t.Settlement == null) continue;
                    var roster = t.Settlement.ItemRoster;
                    var m = EconomyWorld.FindMarket(t.Settlement.StringId);
                    if (roster == null || m == null) continue;
                    int cap = (int)(ImportCapacityOf(t.Settlement.StringId) * capMult * (1f + Guilds.CapacityBonus()));
                    if (cap <= 0) continue;
                    foreach (var g in FeudalGoods.Main)
                    {
                        var e = m.Get(g.Id);
                        if (e == null) continue;
                        float basePrice = EconomyWorld.National.PriceOf(g.Id);
                        if (e.Price > basePrice * 0.85f) continue;    // 价差 <15% -> 路线自动取消
                        var item = FeudalGoods.Item(g.Id);
                        if (item == null) continue;
                        int have = roster.GetItemNumber(item);
                        int can = Math.Min(cap, have - 30);           // 保留本地底仓
                        if (can <= 0) continue;
                        roster.AddToCounts(item, -can);
                        int rev = (int)Math.Round(can * basePrice * (ally ? 1.0f : 0.9f));
                        EconomyWorld.TreasuryAdd(rev);
                        Fiscal.AddExport(rev);
                        exported += can;
                        if (DLog.Flag("econ"))
                            DLog.Info("贸易路线(出口): " + g.Id + " " + can + " 件 " + t.Name + " 收入 " + rev);
                    }
                }
            }
            catch (Exception ex) { DLog.Info("出口异常: " + ex.Message); }
            return exported;
        }

        // ==================== 5) 贸易路线: 进口(5.5 -> 19.9.4 升级) ====================
        // 文档 19.9.4: 固定量路线 + 关税 20% + 价差不足自动取消 + 同盟扩量 1.5x + 交战强制取消
        private static int Imports()
        {
            int imported = 0;
            try
            {
                var pk = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                if (pk == null) return 0;
                bool atWar = AnyWar(pk);
                if (atWar) return 0;                                  // 交战: 路线强制取消
                float capMult = AllyCount(pk) > 0 ? 1.5f : 1f;        // 同盟: 容量 ×1.5
                foreach (var t in pk.Fiefs)
                {
                    if (t == null || !t.IsTown || t.Settlement == null) continue;
                    var roster = t.Settlement.ItemRoster;
                    var m = EconomyWorld.FindMarket(t.Settlement.StringId);
                    if (roster == null || m == null) continue;
                    int cap = (int)(ImportCapacityOf(t.Settlement.StringId) * capMult * (1f + Guilds.CapacityBonus()));
                    if (cap <= 0) continue;   // 没有贸易站 -> 不能进出口

                    foreach (var g in FeudalGoods.Main)
                    {
                        var e = m.Get(g.Id);
                        if (e == null) continue;
                        var item = FeudalGoods.Item(g.Id);
                        if (item == null) continue;
                        // 本地价高于全国基准 30% 以上 -> 进口有利可图(价差不足则路线自动取消)
                        float basePrice = EconomyWorld.National.PriceOf(g.Id);
                        if (e.Price < basePrice * (1f + MarketRules.ImportPremium)) continue;
                        int room = cap;
                        int stock = roster.GetItemNumber(item);
                        int limit = (int)MarketRules.BaseStockLimit;
                        if (stock >= limit) continue;
                        int can = Math.Min(room, limit - stock);
                        if (can <= 0) continue;
                        roster.AddToCounts(item, can);
                        // 关税(19.9.4): |价差| 的 20% 由国库承担
                        int cost = (int)Math.Round(can * Math.Abs(e.Price - basePrice) * 0.20f);
                        if (cost <= 0) cost = (int)Math.Round(can * basePrice * 0.05f);
                        SpendGold(cost);
                        Fiscal.AddTariff(cost);
                        imported += can;
                        if (DLog.Flag("econ"))
                            DLog.Info("贸易路线(进口): " + g.Id + " " + can + " 件 -> " + t.Name + " 关税 " + cost);
                    }
                }
            }
            catch (Exception ex) { DLog.Info("进口异常: " + ex.Message); }
            return imported;
        }

        private static void SpendGold(int amount)
        {
            try
            {
                if (amount <= 0) return;
                EconomyWorld.TreasurySpend(amount);   // 关税从国库出(6.1: 国库=玩家金钱, 统一入口)
            }
            catch { }
        }

        // ==================== 工具 ====================
        // 5.2: clamp((买 - 卖) / min(买, 卖), -1, +1) + 除零边界
        private static float Ratio(float buy, float sell)
        {
            if (buy <= 0f && sell <= 0f) return 0f;
            if (sell <= 0f) return 1f;
            if (buy <= 0f) return -1f;
            float r = (buy - sell) / Math.Min(buy, sell);
            if (r > 1f) r = 1f;
            if (r < -1f) r = -1f;
            return r;
        }

        // 5.2: clamp(1 + (需求 - 供给) / max(需求, 供给, 1), 0.5, 1.5)
        private static float Scarcity(float demand, float supply)
        {
            float denom = Math.Max(Math.Max(demand, supply), 1f);
            float s = 1f + (demand - supply) / denom;
            if (s < MarketRules.MinScarcity) s = MarketRules.MinScarcity;
            if (s > MarketRules.MaxScarcity) s = MarketRules.MaxScarcity;
            return s;
        }

        // 每日变动幅度限制(5.2: 每日价格变动 ≤ 10%) —— factor 是比例, 需要换算成价格差
        private static float MoveTowards(float cur, float target, float maxRate)
        {
            if (cur <= 0.01f) return target;
            float maxDelta = cur * maxRate;
            float d = target - cur;
            if (d > maxDelta) d = maxDelta;
            if (d < -maxDelta) d = -maxDelta;
            return cur + d;
        }

        // 城市: 市场建筑的本地价改善(负值)
        private static float PriceBonusOf(string townId)
        {
            float bonus = 0f;
            var sb = EconomyWorld.Find(townId);
            if (sb == null) return 0f;
            foreach (var grp in sb.Groups)
            {
                if (grp == null || grp.Count <= 0) continue;
                var def = grp.Def;
                if (def == null || !def.IsEffect) continue;
                foreach (var outp in def.Outputs)
                    if (outp != null && outp.Good == BuildDefs.EffPrice)
                        bonus += outp.Value(grp.Mode) * grp.Count / 100f;   // 表里是百分数, 负值=改善
            }
            return bonus;
        }

        // 城市: 贸易容量(@trade_cap, 调运容量)
        private static int TradeCapacityOf(string townId)
        {
            int cap = 0;
            var sb = EconomyWorld.Find(townId);
            if (sb == null) return 0;
            foreach (var grp in sb.Groups)
            {
                if (grp == null || grp.Count <= 0) continue;
                var def = grp.Def;
                if (def == null || !def.IsEffect) continue;
                foreach (var outp in def.Outputs)
                    if (outp != null && outp.Good == BuildDefs.EffTradeCap)
                        cap += (int)Math.Round(outp.Value(grp.Mode) * grp.Count);
            }
            return cap;
        }

        // 城市: 进出口容量(@import_cap)
        private static int ImportCapacityOf(string townId)
        {
            int cap = 0;
            var sb = EconomyWorld.Find(townId);
            if (sb == null) return 0;
            foreach (var grp in sb.Groups)
            {
                if (grp == null || grp.Count <= 0) continue;
                var def = grp.Def;
                if (def == null || !def.IsEffect) continue;
                foreach (var outp in def.Outputs)
                    if (outp != null && outp.Good == BuildDefs.EffImportCap)
                        cap += (int)Math.Round(outp.Value(grp.Mode) * grp.Count);
            }
            return cap;
        }
    }
}
