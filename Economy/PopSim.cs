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
                // v4.269: 清空本日的只读缓存(需求买包值 / 需求份额权重 / 国别通胀)。这些缓存只覆盖
                //   "读"路径, 每个值的输入都在日结之间才变化, 所以按日清空即安全(逐条论证见各字段注释)。
                BeginReadCaches();
                var needs = PopNeeds.All;   // 需求总表(静态只读; 全仓只有读, 无写点)
                // v4.269: 税负激进(文档 20.1) + 政治激进(第 21 章: 法令/镇压/内战) + 民族精神(v4.150)
                //   这三项只取决于全局状态, 与具体 Pop 无关 -> 从"每个 Pop"提到循环外算一次(原来 ≈Pop 数 ×3 次)。
                //   等价性: 循环体内只写 Pop 字段与市场买卖单, 没有任何代码改动这三个输入
                //   (RadicalPressure 由"税制"步骤写, 而"税制"排在"建筑产出"之后、本函数之外)。
                float radPerDay = TaxPolicy.RadicalPressure + Politics.RadicalDaily();
                try
                {
                    var pk = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                    if (pk != null) radPerDay += NationalSpirits.RadicalDelta(pk);
                }
                catch { }
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

                        for (int ni = 0; ni < needs.Count; ni++)
                        {
                            var need = needs[ni];
                            // v4.269: 买包值只取决于(需求, 财富等级, 文化) —— 纯函数(见 NeedValueOf 论证), 按日缓存。
                            //   原来是每个 Pop 的每类需求都重算一遍(PopNeeds.ValueFor 内含商品遍历 + 痴迷/禁忌判定)。
                            float value = NeedValueOf(ni, need, pop.WealthLevel, pop.Culture);
                            if (value <= 0f) continue;
                            needCost += value;
                            float nm0;
                            needOrd[need.Id] = (needOrd.TryGetValue(need.Id, out nm0) ? nm0 : 0f) + value;

                            // v4.269: 份额(ShareOf)+权重(BuyWeight) 只取决于(市场, 需求, 文化) —— 纯只读, 按
                            //   (市场, 文化) 缓存一次即可。原代码在上面算 wsum、下面算 w 时把同一结果**算了两遍**。
                            //   逐项等价性见 PlanOf: 同一批商品、同一累加顺序 -> wsum 与每个 w 逐位相同。
                            float wsum;
                            var ws = PlanOf(ni, need, market, pop.Culture, out wsum);
                            if (wsum <= 0.001f) { unmet += value; continue; }

                            for (int g = 0; g < ws.Length; g++)
                            {
                                var e = need.Goods[g];
                                float w = ws[g];
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
                        // v4.269: 税负激进(文档 20.1) + 政治激进(第 21 章: 法令/镇压/内战) + 民族精神(v4.150)
                        //   已提到循环外(radPerDay), 值完全相同(见函数开头注释)
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
                        // v4.290 修复: 接入建筑效果 @prosperity(市政厅/水井/城市中心/神庙村)。
                        //   此前 @prosperity 在全仓没有任何消费者 —— 玩家建这几座建筑收益恒为 0。
                        //   放在 ±钳制之后加, 免得被 2f/-1f 上限吃掉。
                        try
                        {
                            float bp = BuildDefs.EffectSumOf(town.Settlement.StringId, BuildDefs.EffProsperity);
                            if (bp != 0f) gain += bp;
                        }
                        catch { }
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
                // v4.290 修复: 接入建筑效果 @population(医院/城市中心)。此前该效果全仓零消费者 ——
                //   玩家花钱建医院对人口零影响(Growth 只读制度"卫生机构"等级, 与 hospital 建筑无关)。
                //   按"出生率 +N%"口径乘在 birth 上(直接加进 rate 会被下面的 ±0.006 钳制整个吃掉)。
                try
                {
                    float bp = BuildDefs.EffectSumOf(kv.Key, BuildDefs.EffPopulation) / 100f;
                    if (bp != 0f) { birth *= (1f + bp); if (birth < 0f) birth = 0f; }
                }
                catch { }
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
                // v4.290 修复: 接入建筑效果 @loyalty(神庙/神庙村)。此前该效果全仓零消费者 ——
                //   这两座建筑的忠诚度收益是死数据。效果值口径与 delta 一致(忠诚度点/日), 直接累加。
                try
                {
                    float bl = BuildDefs.EffectSumOf(t.Settlement.StringId, BuildDefs.EffLoyalty);
                    if (bl != 0f) delta += bl;
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
            // v4.269: 前半段(Σ 各需求在该财富等级下的买包值)只取决于(财富等级, 文化) —— 纯函数, 按日缓存;
            //   后半段(乘通胀、乘本 Pop 的 NeedUnitsScale)逐字保持原来的运算与顺序 -> 浮点结果逐位相同。
            float t = NeedsCostOf(pop.Culture, level);
            // v4.149: AI 铸币通胀 -> 该国人口消费成本上升(财富与立场恶化)
            try { t *= InflationOf(pop.SettlementId); } catch { }
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
                int have = RosterMemo.Count(roster, item);   // v4.269: 等价于 roster.GetItemNumber(item)
                if (have <= 0) return 0f;
                int want = MBRandom.RoundRandomized(units);   // 注意: 随机调用次数/顺序与改前完全一致
                if (want > have) want = have;
                if (want <= 0) return 0f;
                RosterMemo.Take(roster, item, want);         // v4.269: 等价于 roster.AddToCounts(item, -want)
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

        // ==================== v4.269: 只读缓存(需求买包值 / 份额权重 / 国别通胀) ====================
        //   三条都只优化**读**路径, 不碰任何写点; 缓存的生命周期就是一次 DailyConsume(函数开头清空),
        //   因为三个输入(需求静态表、市场库存/产量、国别铸币)都只在日结之间变化。
        private sealed class ReadCache
        {
            internal float[] Val;      // [需求下标 * 128 + 财富等级] 买包值
            internal bool[] ValSet;
            internal float[] Cost;     // [财富等级] 全需求买包值合计(CostOf 用)
            internal bool[] CostSet;
        }
        private static readonly Dictionary<string, ReadCache> _read = new Dictionary<string, ReadCache>(StringComparer.Ordinal);
        private static readonly Dictionary<string, float> _infl = new Dictionary<string, float>(StringComparer.Ordinal);
        // 需求计划(份额/权重): 只在 (市场, 文化) 变化时整表重算; _planTick 是"世代号"
        private static float[][] _planW;      // [需求下标][商品下标] 购买权重
        private static float[] _planWSum;     // [需求下标] 权重合计
        private static int[] _planGen;        // [需求下标] 已算好的世代号
        private static int _planTick;
        private static int _planNeeds = -1;
        private static TownMarket _planMarket;
        private static string _planCulture;

        private static void BeginReadCaches()
        {
            _read.Clear();
            _infl.Clear();
            _planNeeds = -1;
            _planMarket = null;
            _planCulture = null;
            _planTick = 0;
        }

        private static ReadCache CacheOf(string culture)
        {
            string k = culture ?? "";    // 字典不接受 null 键(原逻辑允许 culture 为 null, 这里只做键归一化)
            ReadCache c;
            if (_read.TryGetValue(k, out c)) return c;
            var needs = PopNeeds.All;
            c = new ReadCache
            {
                Val = new float[needs.Count * 128],
                ValSet = new bool[needs.Count * 128],
                Cost = new float[128],
                CostSet = new bool[128]
            };
            _read[k] = c;
            return c;
        }

        // 等价于 PopNeeds.ValueFor(need, level, culture)
        //   纯函数依据: PopNeeds.ValueFor = PackageValue(纯算术: 只读 NeedDef 的曲线字段) + 痴迷/禁忌判定
        //   (PopDefs.ObsessionOf/IsTaboo 都是 switch/字符串比较, 无状态、无随机、无写入)。
        private static float NeedValueOf(int ni, NeedDef need, int level, string culture)
        {
            if (level < 0 || level > 127) return PopNeeds.ValueFor(need, level, culture);   // 越界不走缓存(与原式同解)
            var c = CacheOf(culture);
            int idx = ni * 128 + level;
            if (c.ValSet[idx]) return c.Val[idx];
            float v = PopNeeds.ValueFor(need, level, culture);
            c.Val[idx] = v; c.ValSet[idx] = true;
            return v;
        }

        // 等价于 Σ_i PopNeeds.ValueFor(PopNeeds.All[i], level, culture)(同样的遍历顺序与累加顺序)
        private static float NeedsCostOf(string culture, int level)
        {
            if (level < 0 || level > 127) return SumNeedsCost(culture, level);
            var c = CacheOf(culture);
            if (c.CostSet[level]) return c.Cost[level];
            float t = SumNeedsCost(culture, level);
            c.Cost[level] = t; c.CostSet[level] = true;
            return t;
        }

        private static float SumNeedsCost(string culture, int level)
        {
            float t = 0f;
            var needs = PopNeeds.All;
            for (int i = 0; i < needs.Count; i++) t += PopNeeds.ValueFor(needs[i], level, culture);
            return t;
        }

        // 等价于 AiEconomyDeep.InflationForSettlement(sid) —— 它内部按**线性扫 Settlement.All** 找定居点,
        //   而 CostOf 每个 Pop 要调两次(当前档 + 下一档), 全图就是上千次线性扫描。这里按定居点缓存当日结果。
        //   纯函数依据: 只读 s.MapFaction -> Kingdom -> MintOf(铸币厂数量), 三者在本次 DailyConsume 内都不变
        //   (铸币厂由 MintRight.Daily 写, 排在"建筑产出"之后的另一个调度步骤)。
        private static float InflationOf(string sid)
        {
            if (string.IsNullOrEmpty(sid)) return 1f;
            float v;
            if (_infl.TryGetValue(sid, out v)) return v;
            v = AiEconomyDeep.InflationForSettlement(sid);
            _infl[sid] = v;
            return v;
        }

        private static void EnsurePlan(TownMarket market, string culture)
        {
            var needs = PopNeeds.All;
            if (_planNeeds != needs.Count)
            {
                _planW = new float[needs.Count][];
                _planWSum = new float[needs.Count];
                _planGen = new int[needs.Count];   // 全 0
                _planNeeds = needs.Count;
                _planTick = 1;                     // 必须 >=1: 否则 _planGen 全 0 == 世代号 -> 会跳过建表(空指针)
                _planMarket = market;
                _planCulture = culture;
                return;
            }
            if (ReferenceEquals(_planMarket, market) && _planCulture == culture) return;
            _planMarket = market;
            _planCulture = culture;
            if (++_planTick == int.MaxValue) { for (int i = 0; i < _planGen.Length; i++) _planGen[i] = 0; _planTick = 1; }
        }

        // 某需求在本(市场, 文化)下的购买权重数组 + 权重合计
        //   等价于原来那两段:
        //     float wsum = 0f; for(g) wsum += BuyWeight(e, ShareOf(market, need, e.Good), culture);
        //     for(g) { float w = BuyWeight(e, ShareOf(market, need, e.Good), culture); ... }
        //   —— 两段的入参完全相同, 而 ShareOf/BuyWeight 都是纯只读(见两函数实现: ShareOf 只读
        //   MarketEntry.Stock/DailyProduction 与 NeedEntry.MinShare; BuyWeight 只读 NeedEntry 字段 + 文化判定),
        //   所以第二段重算出来的 w 与第一段逐位相同 —— 算一次就是同一个值。
        //   两段之间的"写"只有 market.GetOrCreate(...).BuyOrders/DailyConsumption += (第 86/92 行), 写的是
        //   **另外两个字段**, 不动 ShareOf 读的 Stock/DailyProduction; GetOrCreate 新建条目的 Stock/DailyProduction
        //   恒为 0(见 MarketData.GetOrCreate), 与"条目不存在"对 ShareOf 同解。
        //   计划按需求惰性建立: 只有 value > 0 的需求才会走到这里(与原代码进入 wsum 循环的条件一致),
        //   因此"哪些需求会去调 ShareOf"这一集合与改前完全相同。
        private static float[] PlanOf(int ni, NeedDef need, TownMarket market, string culture, out float wsum)
        {
            EnsurePlan(market, culture);
            if (_planGen[ni] != _planTick)
            {
                int gc = need.Goods != null ? need.Goods.Count : 0;
                var arr = _planW[ni];
                if (arr == null || arr.Length != gc) { arr = new float[gc]; _planW[ni] = arr; }
                float sum = 0f;
                for (int g = 0; g < gc; g++)
                {
                    var e = need.Goods[g];
                    float w = PopNeeds.BuyWeight(e, ShareOf(market, need, e.Good), culture);
                    arr[g] = w;
                    sum += w;
                }
                _planWSum[ni] = sum;
                _planGen[ni] = _planTick;
            }
            else if (VerifyOn())
            {
                // v4.269 自检(默认关闭, flags 含 memo 时打开): 缓存复用 vs 现算(份额+权重)逐个商品比对。
                var arr = _planW[ni];
                int gc = need.Goods != null ? need.Goods.Count : 0;
                for (int g = 0; g < gc && arr != null && g < arr.Length; g++)
                {
                    var e = need.Goods[g];
                    float fresh = PopNeeds.BuyWeight(e, ShareOf(market, need, e.Good), culture);
                    if (fresh != arr[g])
                    {
                        _planMismatch++;
                        if (_planMismatch <= 20)
                            DLog.Force("需求计划缓存自检不一致: 需求=" + (need.Id ?? "?") + " 商品=" + (e != null ? e.Good : "?")
                                + " 缓存=" + arr[g] + " 现算=" + fresh);
                    }
                }
            }
            wsum = _planWSum[ni];
            return _planW[ni];
        }

        private static long _planMismatch;
        private static bool _verifyChecked, _verify;
        private static bool VerifyOn()
        {
            if (!_verifyChecked) { _verifyChecked = true; try { _verify = DLog.Flag("memo"); } catch { _verify = false; } }
            return _verify;
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
