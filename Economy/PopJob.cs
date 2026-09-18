using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem.Settlements;

namespace FeudalInternalAffairs
{
    // 建筑的岗位配方(文档 19.7.1): 每 1 级建筑需要的职业人数
    internal class JobSpec
    {
        internal string[] Prof;
        internal int[] Num;
    }

    // 建筑经营: 雇佣 -> 工资 -> 收支 -> 现金储备 -> 倒闭(文档 19.7; P7a)
    // 结算位置: DailySettlement.Run 的逐定居点生产之后, MarketSim.Run 之前(文档 19.13.2 第 1 步)
    internal static class PopJob
    {
        private static readonly Dictionary<string, JobSpec> Specs = BuildSpecs();
        private static readonly Dictionary<PopRecord, float> IncomeCache = new Dictionary<PopRecord, float>();
        internal static bool HasRun;   // 是否已结算过(区分"未结算"与"失业0收入")
        internal static int LastWageTotal, LastCostTotal;   // 昨日汇总(统计页用)
        private static bool _warnedSpec;

        // PopSim 的收入来源(P7a 起取代占位工资)
        internal static float IncomeOf(PopRecord pop)
        {
            float v;
            return (pop != null && IncomeCache.TryGetValue(pop, out v)) ? v : 0f;
        }

        internal static JobSpec SpecOf(string defId)
        {
            JobSpec s;
            return (defId != null && Specs.TryGetValue(defId, out s)) ? s : null;
        }

        internal static void RunDaily()
        {
            int groups = 0, bankrupt = 0, stalled = 0;
            float wageTotal = 0f, incomeTotal = 0f, costTotal = 0f;
            try
            {
                IncomeCache.Clear();
                var hired = new Dictionary<string, float>();          // profession -> 已雇人数(逐定居点重置)
                var avgWage = new Dictionary<string, float>();          // profession -> 平均工资

                foreach (var kv in EconomyWorld.Buildings)
                {
                    var sb = kv.Value;
                    if (sb == null || sb.Groups.Count == 0) continue;
                    hired.Clear(); avgWage.Clear();

                    // 1) 需求: 职业 -> 岗位数
                    var need = new Dictionary<string, float>();
                    for (int i = 0; i < sb.Groups.Count; i++)
                    {
                        var g = sb.Groups[i];
                        if (g == null || g.Count <= 0) continue;
                        var spec = SpecOf(g.DefId);
                        if (spec == null) continue;
                        for (int j = 0; j < spec.Prof.Length; j++)
                        {
                            float n = spec.Num[j] * g.Count;
                            if (need.ContainsKey(spec.Prof[j])) need[spec.Prof[j]] += n; else need[spec.Prof[j]] = n;
                        }
                    }
                    if (need.Count == 0) continue;

                    // 2) 供给: 该定居点各职业的工作人口
                    var pops = Pops.Of(kv.Key);
                    var supply = new Dictionary<string, float>();
                    if (pops != null)
                        for (int i = 0; i < pops.Count; i++)
                        {
                            var p = pops[i];
                            if (p == null || p.Size < 1f) continue;
                            float w = p.Workforce;
                            if (supply.ContainsKey(p.Profession)) supply[p.Profession] += w; else supply[p.Profession] = w;
                        }

                    // 2.5) 农民/失业者填补低级岗位(转职; 文档 19.2.1: 劳工无资格要求, V3 农民可被雇)
                    ConvertToLowJobs(kv.Key, pops, need, supply);

                    // 3) 到岗率(职业级) + 平均工资
                    var fill = new Dictionary<string, float>();
                    foreach (var nd in need)
                    {
                        float sup; supply.TryGetValue(nd.Key, out sup);
                        float f = nd.Value > 0.01f ? Math.Min(1f, sup / nd.Value) : 0f;
                        fill[nd.Key] = f;
                    }
                    // 工资: 各建筑按自身利润率出价(文档 19.7.3), 取该职业的加权平均
                    var wageSum = new Dictionary<string, float>(); var wageCnt = new Dictionary<string, float>();
                    for (int i = 0; i < sb.Groups.Count; i++)
                    {
                        var g = sb.Groups[i];
                        if (g == null || g.Count <= 0) continue;
                        var spec = SpecOf(g.DefId); if (spec == null) continue;
                        g.WageMult = Clamp(1f + 0.5f * g.Margin, 0.6f, 2.0f);
                        for (int j = 0; j < spec.Prof.Length; j++)
                        {
                            var def = PopDefs.Get(spec.Prof[j]);
                            float w = PopDefs.BaseWage * def.WageFactor * g.WageMult;
                            float num = spec.Num[j] * g.Count * (fill.ContainsKey(spec.Prof[j]) ? fill[spec.Prof[j]] : 0f);
                            if (wageSum.ContainsKey(spec.Prof[j])) { wageSum[spec.Prof[j]] += w * num; wageCnt[spec.Prof[j]] += num; }
                            else { wageSum[spec.Prof[j]] = w * num; wageCnt[spec.Prof[j]] = num; }
                        }
                    }
                    foreach (var kv2 in wageSum) avgWage[kv2.Key] = wageCnt[kv2.Key] > 0.01f ? kv2.Value / wageCnt[kv2.Key] : PopDefs.BaseWage;

                    // 4) Pop 收入 = 受雇工作人口 × 该职业平均工资
                    //    就业上限 = 岗位数: 人多岗少时按比例就业(否则给全部失业者发工资 -> 财富会涨到 99)
                    if (pops != null)
                        for (int i = 0; i < pops.Count; i++)
                        {
                            var p = pops[i];
                            if (p == null || p.Size < 1f) continue;
                            float sup2; supply.TryGetValue(p.Profession, out sup2);
                            float nd2; need.TryGetValue(p.Profession, out nd2);
                            float emp = sup2 > 0.01f ? Math.Min(1f, nd2 / sup2) : 0f;
                            float wg = avgWage.ContainsKey(p.Profession) ? avgWage[p.Profession] : 0f;
                            IncomeCache[p] = p.Workforce * emp * wg;
                        }

                    // 5) 每个建筑组: 收支 -> 现金储备 -> 倒闭
                    for (int i = 0; i < sb.Groups.Count; i++)
                    {
                        var g = sb.Groups[i];
                        if (g == null || g.Count <= 0) continue;
                        var def = BuildDefs.Get(g.DefId);
                        var spec = SpecOf(g.DefId);
                        if (def == null || spec == null) continue;
                        int mode = (int)g.Mode;
                        groups++;

                        float staffN = 0f, demandN = 0f;
                        for (int j = 0; j < spec.Prof.Length; j++)
                        {
                            float dn = spec.Num[j] * g.Count;
                            demandN += dn;
                            staffN += dn * (fill.ContainsKey(spec.Prof[j]) ? fill[spec.Prof[j]] : 0f);
                        }
                        g.Fill = demandN > 0.01f ? staffN / demandN : 0f;   // 按人数加权的到岗率
                        if (g.Stalled) { g.Fill = 0f; continue; }   // 停业: 不雇人

                        float inc = 0f;
                        bool sellable = false;
                        for (int j = 0; j < def.Outputs.Count; j++)
                        {
                            var o = def.Outputs[j];
                            if (o == null || string.IsNullOrEmpty(o.Good) || o.Good.StartsWith("@")) continue;
                            inc += o.Value(g.Mode) * g.Count * g.Fill * PriceOf(o.Good);
                            sellable = true;
                        }
                        float cost = def.Maintenance[Math.Min(mode, def.Maintenance.Length - 1)];
                        var inputs = def.InputsByMode != null && mode < def.InputsByMode.Length && def.InputsByMode[mode] != null ? def.InputsByMode[mode] : def.Inputs;
                        if (inputs != null)
                            for (int j = 0; j < inputs.Count; j++)
                            {
                                var it = inputs[j];
                                if (it == null || string.IsNullOrEmpty(it.Good)) continue;
                                cost += it.Value(g.Mode) * g.Count * g.Fill * PriceOf(it.Good);
                            }
                        float wage = 0f;
                        for (int j = 0; j < spec.Prof.Length; j++)
                        {
                            float f = fill.ContainsKey(spec.Prof[j]) ? fill[spec.Prof[j]] : 0f;
                            wage += spec.Num[j] * g.Count * f * (avgWage.ContainsKey(spec.Prof[j]) ? avgWage[spec.Prof[j]] : PopDefs.BaseWage);
                        }
                        cost += wage;
                        wageTotal += wage; incomeTotal += inc; costTotal += cost;
                        BldHist.Record(kv.Key, g.DefId, inc - cost);   // 文档 20.8: 建筑盈亏历史(内存)

                        g.Cash += inc - cost;
                        g.Margin = cost > 0.01f ? (inc - cost) / cost : 0f;
                        if (!sellable)
                        {
                            // 纯效果建筑(行政/军事/建造部门等): 没有销货收入, 运营费由领主/国库承担(文档 6.1), 不参与倒闭判定
                            g.Cash = 0f;
                            g.Margin = 0f;
                            g.BankruptDays = 0;
                        }
                        else
                        {
                            // 倒闭判定(文档 19.7.4): 连续 90 天 < -(月支出×3)
                            float monthly = cost * 30f;
                            if (g.Cash < -(monthly * 3f) && monthly > 0.01f) g.BankruptDays++; else g.BankruptDays = 0;
                            if (g.BankruptDays >= 90)
                            {
                                g.Stalled = true; g.StallReason = "资不抵债停业";
                                g.Fill = 0f; g.Cash = 0f; g.BankruptDays = 0;
                                stalled++;
                            }
                            if (g.BankruptDays > 0) bankrupt++;
                        }
                    }
                }

                if (groups > 0)
                    DLog.Info("建筑经营: 组=" + groups + " 收入=" + ((int)incomeTotal) + " 支出=" + ((int)costTotal)
                        + "(工资=" + ((int)wageTotal) + ") 赤字组=" + bankrupt + " 新停业=" + stalled
                        + " 平均工资占比=" + (costTotal > 1f ? ((int)(wageTotal / costTotal * 100f)) + "%" : "-"));
                else if (!_warnedSpec)
                {
                    _warnedSpec = true;
                    DLog.Force("建筑经营: 未匹配到任何岗位配方(检查建筑 DefId 与 PopJob.Specs) —— 就业/工资将全为 0");
                }
                LastWageTotal = (int)wageTotal;
                LastCostTotal = (int)costTotal;
                HasRun = true;
            }
            catch (Exception ex) { DLog.Force("建筑经营异常: " + ex.Message); }
        }

        private static float Clamp(float v, float a, float b) { return v < a ? a : (v > b ? b : v); }

        // 低级岗位的低技能劳动力池: 农民与失业者可转职填补(文档 19.2.1; 高技能岗位仍靠本职业人口)
        private static readonly string[] LowJobs = { PopDefs.Laborers, PopDefs.Farmers, PopDefs.Miners, PopDefs.Soldiers };

        private static void ConvertToLowJobs(string sid, List<PopRecord> pops, Dictionary<string, float> need, Dictionary<string, float> supply)
        {
            try
            {
                if (pops == null) return;
                for (int k = 0; k < LowJobs.Length; k++)
                {
                    string job = LowJobs[k];
                    float demand;
                    if (!need.TryGetValue(job, out demand) || demand <= 0.01f) continue;
                    float have;
                    if (!supply.TryGetValue(job, out have)) have = 0f;
                    float missing = demand - have;
                    if (missing <= 0.5f) continue;
                    // 从农民/失业者中抽人转职(单次最多该 Pop 的 20%, 防止一夜之间村庄没有农民)
                    for (int i = 0; i < pops.Count && missing > 0.5f; i++)
                    {
                        var p = pops[i];
                        if (p == null || p.Size < 2f) continue;
                        if (p.Profession != PopDefs.Peasants && p.Profession != PopDefs.Unemployed) continue;
                        var def = PopDefs.Get(job);
                        if (p.Literacy + 0.001f < def.MinLiteracy) continue;   // 资格门槛
                    // missing 是"工作人口数"(岗位), 转职搬的是"总人数" -> 必须 ÷ 工作比例
                    // (原来按 1:1 搬人: 缺 7 个岗位只搬 7 个人 = 1.75 个劳动力, 到岗率永远卡在 ~21%)
                    float take = Math.Min(missing / PopDefs.WorkforceRatio, p.Size * 0.2f);
                    if (take < 1f) continue;
                    p.Size -= take;
                    var r = Pops.GetOrCreate(sid, job, p.Culture);
                    if (r != null) { r.Size += take; r.Wealth = p.Wealth; r.Literacy = p.Literacy; }
                    supply[job] = have + take * PopDefs.WorkforceRatio;
                    have = supply[job];
                    missing = demand - have;
                    }
                }
            }
            catch { }
        }

        private static float PriceOf(string goodId)
        {
            try { return EconomyWorld.National.PriceOf(goodId); } catch { return 0f; }
        }

        // 43 种建筑的岗位配方(文档 19.7.1 表 + 按类别补全)
        // v3.7 校准: 岗位数随产出同比例缩减(否则工资成本远超缩小后的产出价值)
        internal const float StaffScale = 1f / 6f;

        private static Dictionary<string, JobSpec> BuildSpecs()
        {
            var d = new Dictionary<string, JobSpec>();
            Add(d, "farm", P(PopDefs.Farmers, 1, PopDefs.Laborers, 7));
            Add(d, "specialty_farm", P(PopDefs.Farmers, 1, PopDefs.Laborers, 5));
            Add(d, "pasture", P(PopDefs.Farmers, 1, PopDefs.Laborers, 4));
            Add(d, "lumberjack", P(PopDefs.Laborers, 6));
            Add(d, "mine_iron", P(PopDefs.Miners, 6, PopDefs.Laborers, 2));
            Add(d, "mine_clay_salt", P(PopDefs.Miners, 4, PopDefs.Laborers, 3));
            Add(d, "mine_silver", P(PopDefs.Miners, 6, PopDefs.Laborers, 2));
            Add(d, "quarry", P(PopDefs.Miners, 5, PopDefs.Laborers, 3));
            Add(d, "fishery", P(PopDefs.Laborers, 6));
            Add(d, "herb_gatherer", P(PopDefs.Laborers, 4));
            Add(d, "weavery_shop", P(PopDefs.Laborers, 5));
            Add(d, "tannery_shop", P(PopDefs.Laborers, 5));
            Add(d, "brewery_shop", P(PopDefs.Laborers, 5));
            Add(d, "granary", P(PopDefs.Laborers, 2, PopDefs.Clerks, 1));
            Add(d, "temple_village", P(PopDefs.Clergymen, 2));
            Add(d, "militia_camp", P(PopDefs.Soldiers, 4, PopDefs.Officers, 1));
            Add(d, "ironworks", P(PopDefs.Machinists, 5, PopDefs.Laborers, 4, PopDefs.Engineers, 1));
            Add(d, "toolshop", P(PopDefs.Machinists, 5, PopDefs.Laborers, 2));
            Add(d, "weaponsmith", P(PopDefs.Machinists, 5, PopDefs.Engineers, 1));
            Add(d, "armorsmith", P(PopDefs.Machinists, 5, PopDefs.Engineers, 1));
            Add(d, "weavery", P(PopDefs.Machinists, 4, PopDefs.Laborers, 3));
            Add(d, "tannery", P(PopDefs.Machinists, 4, PopDefs.Laborers, 3));
            Add(d, "brewery", P(PopDefs.Machinists, 4, PopDefs.Laborers, 3));
            Add(d, "pottery_works", P(PopDefs.Machinists, 4, PopDefs.Laborers, 3));
            Add(d, "barracks", P(PopDefs.Soldiers, 10, PopDefs.Officers, 1));
            Add(d, "armory", P(PopDefs.Machinists, 3, PopDefs.Clerks, 2));
            Add(d, "stables", P(PopDefs.Laborers, 4, PopDefs.Soldiers, 1));
            Add(d, "walls", P(PopDefs.Laborers, 4));
            Add(d, "watchtower", P(PopDefs.Soldiers, 3));
            Add(d, "patrol", P(PopDefs.Soldiers, 4, PopDefs.Officers, 1));
            Add(d, "supply", P(PopDefs.Clerks, 3, PopDefs.Laborers, 2));
            Add(d, "builder", P(PopDefs.Laborers, 6, PopDefs.Engineers, 1));
            Add(d, "warehouse", P(PopDefs.Laborers, 3, PopDefs.Clerks, 1));
            Add(d, "market", P(PopDefs.Shopkeepers, 4, PopDefs.Clerks, 3));
            Add(d, "trade_post", P(PopDefs.Shopkeepers, 3, PopDefs.Clerks, 3));
            Add(d, "caravan_post", P(PopDefs.Laborers, 4, PopDefs.Clerks, 2));
            Add(d, "tax_office", P(PopDefs.Bureaucrats, 4, PopDefs.Clerks, 2));
            Add(d, "town_hall", P(PopDefs.Bureaucrats, 5, PopDefs.Clerks, 2));
            Add(d, "bank", P(PopDefs.Capitalists, 1, PopDefs.Clerks, 4, PopDefs.Bureaucrats, 1));
            Add(d, "mint", P(PopDefs.Bureaucrats, 2, PopDefs.Clerks, 2));   // v4.0 铸币厂(文档 20.3)
            Add(d, "university", P(PopDefs.Academics, 3, PopDefs.Bureaucrats, 1));
            Add(d, "hospital", P(PopDefs.Academics, 2, PopDefs.Clergymen, 2));
            Add(d, "temple", P(PopDefs.Clergymen, 3));
            Add(d, "well", P(PopDefs.Laborers, 1));
            return d;
        }

        private static void Add(Dictionary<string, JobSpec> d, string id, JobSpec s) { d[id] = s; }

        private static JobSpec P(params object[] pairs)
        {
            var s = new JobSpec();
            int n = pairs.Length / 2;
            s.Prof = new string[n]; s.Num = new int[n];
            for (int i = 0; i < n; i++)
            {
                s.Prof[i] = (string)pairs[i * 2];
                int raw = (int)pairs[i * 2 + 1];
                int num = (int)Math.Round(raw * StaffScale, MidpointRounding.AwayFromZero);
                s.Num[i] = num < 1 ? 1 : num;
            }
            return s;
        }
    }
}
