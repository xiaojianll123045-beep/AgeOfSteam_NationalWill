using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;

namespace FeudalInternalAffairs
{
    // 人口消费模拟(文档 19.4 买包驱动 / 19.6 财富与 SoL; P6b)
    // 结算位置: MarketSim 汇总买卖单之前(文档 19.13.2 第 2 步)
    internal static class PopSim
    {
        private static readonly Dictionary<string, string> TownOfCache = new Dictionary<string, string>();

        internal static void DailyConsume(ref int prosp10)
        {
            int pops = 0;
            float spend = 0f, food = 0f, shortfall = 0f;
            var needOrd = new Dictionary<string, float>();
            var needMiss = new Dictionary<string, float>();
            try
            {
                foreach (var kv in Pops.BySettlement)
                {
                    var list = kv.Value;
                    if (list == null || list.Count == 0) continue;
                    var town = TownOf(kv.Key);
                    var market = town != null && town.Settlement != null ? EconomyWorld.FindMarket(town.Settlement.StringId) : null;
                    var roster = town != null && town.Settlement != null ? town.Settlement.ItemRoster : null;
                    float soldLiving = 0f;

                    for (int i = 0; i < list.Count; i++)
                    {
                        var pop = list[i];
                        if (pop == null || pop.Size < 1f) continue;
                        pops++;
                        float income = IncomeOf(pop);
                        float needCost = 0f, spent = 0f, unmet = 0f;

                        foreach (var need in PopNeeds.All)
                        {
                            float value = PopNeeds.ValueFor(need, pop.WealthLevel, pop.Culture);
                            if (value <= 0f) continue;
                            needCost += value;
                            float nm0;
                            needOrd[need.Id] = (needOrd.TryGetValue(need.Id, out nm0) ? nm0 : 0f) + value;

                            float wsum = 0f;
                            for (int g = 0; g < need.Goods.Count; g++)
                            {
                                var e = need.Goods[g];
                                wsum += PopNeeds.BuyWeight(e, ShareOf(market, need, e.Good), pop.Culture);
                            }
                            if (wsum <= 0.001f) { unmet += value; continue; }

                            for (int g = 0; g < need.Goods.Count; g++)
                            {
                                var e = need.Goods[g];
                                float w = PopNeeds.BuyWeight(e, ShareOf(market, need, e.Good), pop.Culture);
                                if (w <= 0.001f) continue;
                                var def = FeudalGoods.Def(e.Good);
                                if (def == null || def.BasePrice <= 0) continue;

                                float money = value * (w / wsum);
                                float units = money / def.BasePrice * pop.NeedUnitsScale * PopDefs.DemandScale;   // 文档 19.4.1 + 19.12 实机标定
                                if (units <= 0.0001f) continue;

                                if (market != null) market.GetOrCreate(e.Good).BuyOrders += units;   // 买单(意图) -> 驱动价格
                                float got = Take(roster, e.Good, units);                              // 实物买入(受库存限制)
                                float price = PriceOf(e.Good);
                                spent += got * price;
                                if (def.IsFood) food += got;
                                if (e.Good == FeudalGoods.Linen || e.Good == FeudalGoods.Beer || e.Good == FeudalGoods.Pottery) soldLiving += got;
                                if (market != null && got > 0.0001f) market.GetOrCreate(e.Good).DailyConsumption += got;   // 物理消耗
                                float miss = units - got;
                                if (miss > 0.0001f)
                                {
                                    unmet += miss * def.BasePrice;
                                    float nm1;
                                    needMiss[need.Id] = (needMiss.TryGetValue(need.Id, out nm1) ? nm1 : 0f) + miss * def.BasePrice;
                                }
                            }
                        }

                        spend += spent;
                        shortfall += unmet;
                        WealthStep(pop, income, needCost, needCost > 0.0001f ? unmet / needCost : 0f);
                        // v4.147 V3 官方: 低于预期生活水平 -> 随时间积累激进(高于则缓慢忠诚)
                        float expSol = Pops.ExpectedSolOf(pop);
                        if (pop.WealthLevel < expSol - 2f) Pops.ShiftStance(pop, -0.0005f);
                        else if (pop.WealthLevel > expSol + 2f) Pops.ShiftStance(pop, 0.0002f);
                        // 税负激进(文档 20.1) + 政治激进(第 21 章: 法令/镇压/内战) + 民族精神(v4.150)
                        float radPerDay = TaxPolicy.RadicalPressure + Politics.RadicalDaily();
                        try
                        {
                            var pk = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                            if (pk != null) radPerDay += NationalSpirits.RadicalDelta(pk);
                        }
                        catch { }
                        if (radPerDay > 0.00001f || radPerDay < -0.00001f)
                            Pops.ShiftStance(pop, -radPerDay);
                    }

                    // 繁荣度(文档 19.10.3 v3.0): 由人口生活水平驱动, 不再是商品的直接函数
                    if (town != null)
                    {
                        float avg = AvgSol(list);
                        float gain = (avg - 5f) * 0.02f;
                        if (gain > 2f) gain = 2f;
                        if (gain < -1f) gain = -1f;
                        if (Math.Abs(gain) > 0.001f) { town.Prosperity += gain; prosp10 += (int)Math.Round(gain * 10f); }
                    }
                    _soldLog += soldLiving;   // 仅保留旧的售出口径用于日志对照
                }
                _day++;
                // 需求满足快照(UI): 1 - 未买到/想买
                LastNeedSat.Clear();
                foreach (var kvn in needOrd)
                {
                    float miss; needMiss.TryGetValue(kvn.Key, out miss);
                    float sat = kvn.Value > 0.01f ? 1f - miss / kvn.Value : 1f;
                    if (sat < 0f) sat = 0f;
                    if (sat > 1f) sat = 1f;
                    LastNeedSat[kvn.Key] = sat;
                }
                if (_day % 7 == 0) Weekly();
                DLog.Info("人口消费: Pop=" + pops + " 支出=" + ((int)spend) + " 食物=" + ((int)food)
                    + " 未满足值=" + ((int)shortfall) + " 旧口径生活用品售出=" + ((int)_soldLog));
                _soldLog = 0f;
            }
            catch (Exception ex) { DLog.Force("人口消费异常: " + ex); }
        }

        private static int _day;
        private static float _soldLog;
        private static readonly Dictionary<string, float> _attr = new Dictionary<string, float>();
        // UI 用: 今日各需求满足率(needId -> 0~1) + 上周迁移人数(文档 19.14.2)
        internal static readonly Dictionary<string, float> LastNeedSat = new Dictionary<string, float>();
        internal static int LastMigration;

        // ==================== 周结: 迁移 + 人口增长 + 忠诚聚合(文档 19.10 / 19.11) ====================
        internal static void Weekly()
        {
            int mig = 0;
            try
            {
                mig = Migration();
                LastMigration = mig;
                Growth();
                Pops.EducationWeekly();     // v4.147 V3: 识字率向教育机会靠拢
                Pops.AssimilateWeekly();    // v4.147 V3: 同化(基础 0.2%/月)
                Aggregate();
            }
            catch (Exception ex) { DLog.Force("人口周结异常: " + ex.Message); }
            DLog.Force("人口周结: 迁移=" + mig + " 总人口=" + ((int)Pops.TotalPopulation()));
        }

        private static float AvgSol(List<PopRecord> l)
        {
            float num = 0f, den = 0f;
            for (int i = 0; i < l.Count; i++) { var p = l[i]; if (p == null) continue; num += p.WealthLevel * p.Size; den += p.Size; }
            return den > 0f ? num / den : 5f;
        }

        // v4.186: 当地主体文化(人口最多) —— 文化法律按主体文化作用于该聚落
        private static string DominantCulture(List<PopRecord> l)
        {
            try
            {
                string dom = null; float best = 0f;
                if (l == null) return null;
                for (int i = 0; i < l.Count; i++)
                {
                    var p = l[i];
                    if (p == null || p.Size <= 0f) continue;
                    float s = 0f;
                    for (int j = 0; j < l.Count; j++) { var q = l[j]; if (q != null && q.Culture == p.Culture) s += q.Size; }
                    if (s > best) { best = s; dom = p.Culture; }
                }
                return dom;
            }
            catch { return null; }
        }

        private static float Attraction(Settlement s)
        {
            try
            {
                var l = Pops.Of(s.StringId);
                if (l == null || l.Count == 0) return 0f;
                float sol = AvgSol(l);
                float loy = 0f, den = 0f;
                for (int i = 0; i < l.Count; i++) { var p = l[i]; loy += p.Loyalty * p.Size; den += p.Size; }
                loy = den > 0f ? loy / den : 0.5f;
                float food = 0.8f;
                if (s.Town != null) food = Math.Min(1.5f, s.Town.FoodStocks / 100f);
                float infra = 0f;
                try { infra = Infrastructure.BaseOf(s.StringId) * 0.5f; } catch { }   // v4.165: 基建提升迁移吸引力(V3)
                // v4.186: 文化法律的迁移吸引(文档 24.13)
                float cul = 0f;
                try { cul = CultureSystem.AttractOf(DominantCulture(l)); } catch { }
                return (30f + sol * 2f + loy * 10f + food * 20f + infra + cul) * TaxPolicy.AttractMult;   // 文档 19.10.1 基础项 + 20.1 税负
            }
            catch { return 0f; }
        }

        private static int Migration()
        {
            int moved = 0;
            foreach (var k in Kingdom.All)
            {
                if (k == null || k.IsEliminated) continue;
                var list = new List<Settlement>();
                float sum = 0f;
                foreach (var s in k.Settlements)
                {
                    if (s == null) continue;
                    float a = Attraction(s);
                    list.Add(s); sum += a; _attr[s.StringId] = a;
                }
                if (list.Count < 2) continue;
                float avg = sum / list.Count;
                for (int i = 0; i < list.Count; i++)
                {
                    var dst = list[i];
                    float diff = _attr[dst.StringId] - avg;
                    if (diff <= 0.5f) continue;
                    Settlement src = null; float worst = 0f;
                    for (int j = 0; j < list.Count; j++)
                    {
                        var t = list[j];
                        float d = avg - _attr[t.StringId];
                        if (d > worst) { worst = d; src = t; }
                    }
                    if (src == null || worst <= 0.5f || src == dst) continue;
                    float amount = 5f * Math.Min(diff, worst);   // V3: 5 × 吸引力差(文档 19.10.1)
                    // v4.165 V3 官方: 每州每周迁移上限 = 500 + 5 × 基建(铁路驱动)
                    try
                    {
                        float cap = 500f + 5f * Infrastructure.BaseOf(dst.StringId);
                        if (amount > cap) amount = cap;
                    }
                    catch { }
                    moved += MovePeople(src, dst, amount);
                }
            }
            return moved;
        }

        private static int MovePeople(Settlement src, Settlement dst, float amount)
        {
            try
            {
                var sl = Pops.Of(src.StringId);
                if (sl == null || sl.Count == 0) return 0;
                float total = 0f;
                for (int i = 0; i < sl.Count; i++) total += sl[i].Size;
                if (total < 1f) return 0;
                float cap = total * 0.05f;                       // 单次 ≤ 源定居点 5%
                if (amount > cap) amount = cap;
                if (amount < 1f) return 0;
                int moved = 0;
                for (int i = 0; i < sl.Count; i++)
                {
                    var p = sl[i];
                    var def = PopDefs.Get(p.Profession);
                    if (!def.CanMigrate || p.Size < 2f) continue;
                    float take = amount * (p.Size / total);
                    if (take > p.Size * 0.1f) take = p.Size * 0.1f;
                    if (take < 0.5f) continue;
                    p.Size -= take;
                    var r = Pops.GetOrCreate(dst.StringId, p.Profession, p.Culture);
                    if (r != null) { r.Size += take; r.Wealth = p.Wealth; r.Literacy = p.Literacy; }
                    moved += (int)take;
                }
                return moved;
            }
            catch { return 0; }
        }

        // 人口增长(文档 19.10.2 锚点; v4.147 按 V3 官方补: 卫生机构/妇女权利/人口稀少)
        private static void Growth()
        {
            int health = 0; try { health = Institutions.Level[1]; } catch { }
            float womenMult = 1f;
            try { womenMult = 1f - LawSystem.WorkforceBonus() * 2f; } catch { }   // V3: 妇女权利法降低出生率
            if (womenMult < 0.5f) womenMult = 0.5f;
            foreach (var kv in Pops.BySettlement)
            {
                var l = kv.Value;
                if (l == null || l.Count == 0) continue;
                float avg = AvgSol(l);
                float total = 0f;
                for (int i = 0; i < l.Count; i++) { var q = l[i]; if (q != null) total += q.Size; }
                float birth = BirthRate(avg) * womenMult;
                if (total < 5000f) birth *= 1.5f;                       // V3 官方: 人口稀少(<5000) 出生率 +50%
                float death = DeathRate(avg) * (1f - 0.02f * health);   // V3 官方: 卫生系统降低死亡率
                float rate = birth - death;
                if (rate > 0.006f) rate = 0.006f;
                if (rate < -0.006f) rate = -0.006f;
                float f = 1f + rate;
                for (int i = 0; i < l.Count; i++)
                {
                    var p = l[i];
                    if (p == null || p.Size < 1f) continue;
                    p.Size *= f;
                    if (p.Size < 5f) p.Size = 5f;
                }
            }
        }

        private static float BirthRate(float sol)
        {
            if (sol <= 5f) return 0.0010f;
            if (sol <= 10f) return 0.0010f + (sol - 5f) / 5f * 0.0015f;
            if (sol <= 20f) return 0.0025f + (sol - 10f) / 10f * 0.0015f;
            if (sol <= 30f) return 0.0040f + (sol - 20f) / 10f * 0.0010f;
            return 0.0045f - (sol - 30f) * 0.00005f;
        }

        private static float DeathRate(float sol)
        {
            if (sol <= 5f) return 0.0045f;
            if (sol <= 10f) return 0.0045f - (sol - 5f) / 5f * 0.0020f;
            if (sol <= 20f) return 0.0025f - (sol - 10f) / 10f * 0.0010f;
            return 0.0010f;
        }

        // 定居点级激进度 -> 忠诚度(文档 19.11.1)
        private static void Aggregate()
        {
            foreach (var kv in Pops.BySettlement)
            {
                var l = kv.Value;
                if (l == null || l.Count == 0) continue;
                float num = 0f, den = 0f;
                for (int i = 0; i < l.Count; i++)
                {
                    var p = l[i];
                    float w = p.Size * Pops.PoliticalStrengthOf(p);   // v4.147 V3: 政治力量主要基于财富
                    num += p.Radicalism * w; den += w;
                }
                if (den <= 0f) continue;
                float rad = num / den;
                var t = TownOf(kv.Key);
                if (t == null || t.Settlement == null) continue;
                float delta = -rad * 0.5f + (1f - rad) * 0.2f;
                // v4.186: 文化法律满意度 -> 忠诚(每 +10 满意度 = +0.1/日, 按人口加权; 国教对异文化扣分)
                try
                {
                    string dom2 = DominantCulture(l);
                    float sat = 0f, wsum = 0f;
                    for (int i2 = 0; i2 < l.Count; i2++)
                    {
                        var p2 = l[i2];
                        if (p2 == null || p2.Size <= 0f) continue;
                        sat += CultureSystem.SatisFor(dom2, p2.Culture) * p2.Size;
                        wsum += p2.Size;
                    }
                    if (wsum > 0f) delta += (sat / wsum) * 0.01f;
                }
                catch { }
                t.Loyalty += delta;
                if (t.Loyalty > 100f) t.Loyalty = 100f;
                if (t.Loyalty < 0f) t.Loyalty = 0f;
            }
        }

        // 收入(工资 + 上月分红摊到每天): 消费结算与 UI 共用(文档 19.7.4)
        internal static float TotalIncomeOf(PopRecord pop)
        {
            if (pop == null) return 0f;
            float daily = pop.DividendMonthly / 30f;
            if (PopJob.HasRun) return PopJob.IncomeOf(pop) + daily;
            var def = PopDefs.Get(pop.Profession);
            return PopDefs.BaseWage * def.WageFactor * pop.Workforce + daily;
        }

        // 收入: P7a 起用建筑经营结算的真实工资; 尚未结算时退回职业基准工资占位(文档 19.2.1)
        private static float IncomeOf(PopRecord pop)
        {
            return TotalIncomeOf(pop);
        }

        // 财富演化(文档 19.6.2): 周收入 > 1.02×下一档买包 -> 涨; < 当前档 -> 跌; 差额 0.2/周
        private static void WealthStep(PopRecord pop, float income, float needCost, float shortfallRatio)
        {
            if (shortfallRatio > 1f) shortfallRatio = 1f;   // 未满足比例钳制(避免出现 -56% 的"负满足度")
            if (shortfallRatio < 0f) shortfallRatio = 0f;
            float before = pop.Wealth;                       // v4.147: 记录财富变化 -> 驱动忠诚/激进
            float wk = income * 7f;
            int lvl = pop.WealthLevel;
            float cur = CostOf(pop, lvl);
            float next = CostOf(pop, lvl + 1);
            if (next > 0f && wk > PopDefs.WealthGate * next)
                pop.Wealth += PopDefs.WealthGainRate * (wk - PopDefs.WealthGate * next) / 7f;
            else if (cur > 0f && wk < cur)
                pop.Wealth -= PopDefs.WealthGainRate * (cur - wk) / 7f;
            if (pop.Wealth < 1f) pop.Wealth = 1f;
            if (pop.Wealth > 99f) pop.Wealth = 99f;
            pop.NeedShortfall = shortfallRatio;
            // v4.147 V3 官方: SoL 上升 -> 忠诚, 下降 -> 激进(立场一次一步, 互斥); 需求未满足额外转激进
            float dW = pop.Wealth - before;
            if (dW > 0.0001f) Pops.ShiftStance(pop, 0.01f);
            else if (dW < -0.0001f) Pops.ShiftStance(pop, -0.01f);
            if (shortfallRatio > 0.001f) Pops.ShiftStance(pop, -0.02f * Math.Min(1f, shortfallRatio * 3f));
        }

        private static float CostOf(PopRecord pop, int level)
        {
            float t = 0f;
            for (int i = 0; i < PopNeeds.All.Count; i++) t += PopNeeds.ValueFor(PopNeeds.All[i], level, pop.Culture);
            // v4.149: AI 铸币通胀 -> 该国人口消费成本上升(财富与立场恶化)
            try { t *= AiEconomyDeep.InflationForSettlement(pop.SettlementId); } catch { }
            return t * pop.NeedUnitsScale;
        }

        // 从定居点仓库取货(受库存限制)
        private static float Take(ItemRoster roster, string goodId, float units)
        {
            try
            {
                if (roster == null || units <= 0f) return 0f;
                var item = FeudalGoods.Item(goodId);
                if (item == null) return 0f;
                int have = roster.GetItemNumber(item);
                if (have <= 0) return 0f;
                int want = MBRandom.RoundRandomized(units);
                if (want > have) want = have;
                if (want <= 0) return 0f;
                roster.AddToCounts(item, -want);
                return want;
            }
            catch { return 0f; }
        }

        private static float PriceOf(string goodId)
        {
            try { return EconomyWorld.National.PriceOf(goodId); } catch { return 0f; }
        }

        // 市场份额(文档 19.4.4 简化实现: 用当日产量占比; 无市场则为 1 = 不替代)
        private static float ShareOf(TownMarket m, NeedDef need, string goodId)
        {
            try
            {
                if (m == null) return 1f;
                float sum = 0f, self = 0f;
                for (int i = 0; i < need.Goods.Count; i++)
                {
                    var e = need.Goods[i];
                    var me = m.Get(e.Good);
                    // v4.252: 份额 = **库存 + 当日产量**。原来只看"当日产量" -> 昨天进的货/仓里的鱼和肉
                    //   份额为 0 -> 权重被清零 -> 卖不掉(D6 赢家通吃: 有农田的城只买粮食, 市场里的肉鱼烂在仓里,
                    //   而粮食需求被整包砸中, 永远补不上)
                    float s = me != null ? (me.Stock + me.DailyProduction) : 0f;
                    sum += s;
                    if (e.Good == goodId) self = s;
                }
                if (sum <= 0.0001f)
                {
                    // v4.252: 本地完全不产这类商品时: 带"最小份额"的必买商品仍按权重买(保持原语义),
                    //   其余一律返回 0 —— 原来一律返回 1f, 于是"本国根本不产的商品"(收音机/电话/汽车/黄金/丝绸)
                    //   也照样产生买单, 制造永久性的"幽灵净销毁"从国库扣钱(经济审计认定的开局失血主因之一)
                    for (int i = 0; i < need.Goods.Count; i++)
                        if (need.Goods[i] != null && need.Goods[i].MinShare > 0f) return 1f;
                    return 0f;
                }
                return self / sum;
            }
            catch { return 1f; }
        }

        // 定居点 -> 挂靠城镇(市场): 城=自己; 村=贸易绑定城镇; 堡=最近的城镇(缓存)
        private static Town TownOf(string settlementId)
        {
            try
            {
                string tid;
                if (TownOfCache.TryGetValue(settlementId, out tid))
                {
                    if (tid == null) return null;
                    return FindTown(tid);
                }
                var s = FindSettlement(settlementId);
                if (s == null) { TownOfCache[settlementId] = null; return null; }
                if (s.IsTown) { TownOfCache[settlementId] = s.StringId; return s.Town; }
                if (s.IsVillage && s.Village != null && s.Village.TradeBound != null)
                {
                    var ts = s.Village.TradeBound;                 // 贸易绑定城镇(是 Settlement, 不是 Town)
                    TownOfCache[settlementId] = ts.StringId;
                    return ts.Town;
                }
                // 城堡: 找最近城镇
                Town best = null; float bd = float.MaxValue;
                foreach (var t in Town.AllTowns)
                {
                    if (t == null || t.Settlement == null) continue;
                    float d = t.Settlement.Position.Distance(s.Position);
                    if (d < bd) { bd = d; best = t; }
                }
                TownOfCache[settlementId] = best != null ? best.Settlement.StringId : null;
                return best;
            }
            catch { return null; }
        }

        private static Town FindTown(string id)
        {
            try
            {
                foreach (var t in Town.AllTowns) if (t != null && t.Settlement != null && t.Settlement.StringId == id) return t;
            }
            catch { }
            return null;
        }

        private static Settlement FindSettlement(string id)
        {
            try
            {
                foreach (var s in Settlement.All) if (s != null && s.StringId == id) return s;
            }
            catch { }
            return null;
        }
    }
}
