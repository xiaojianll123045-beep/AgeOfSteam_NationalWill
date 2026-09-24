using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;

namespace FeudalInternalAffairs
{
    // 投资池 + 私人建造(文档 19.8; P7b)
    // 分红规则(19.7.4): 每 30 天取 max(0,现金储备)×50%; 国有 -> 国库, 私有 -> 股东(按职业占比归一)
    //   股东的"投资池贡献率"(19.2.1)抽入本国投资池, 余额计入其个人财富(SoL 会随之上升)
    // 私人建造(19.8.2): 每国每 30 天用池子出资建 1 个"预期利润×缺口×战略"得分最高的建筑
    internal static class InvestmentPool
    {
        internal static readonly Dictionary<string, float> Pools = new Dictionary<string, float>();
        internal static int MonthBuilt, TotalBuilt;   // UI: 本月/累计私人建造次数
        internal static int DayBuilt;                 // UI: 今日私人建造次数(每日日结清零)

        internal static float Of(string kid) { float v; return (kid != null && Pools.TryGetValue(kid, out v)) ? v : 0f; }

        // 每日日结: 今日计数清零
        internal static void DayReset() { DayBuilt = 0; }
        internal static void Add(string kid, float v)
        {
            if (string.IsNullOrEmpty(kid) || v <= 0f) return;
            Pools[kid] = Of(kid) + v;
        }
        private static void Spend(string kid, float v)
        {
            if (string.IsNullOrEmpty(kid)) return;
            float cur = Of(kid) - v;
            Pools[kid] = cur < 0f ? 0f : cur;
        }

        private static readonly string[] ShareProf = { PopDefs.Aristocrats, PopDefs.Capitalists, PopDefs.Shopkeepers, PopDefs.Machinists };
        private static readonly float[] SharePct = { 40f, 50f, 5f, 10f };
        private const float ShareTotal = 105f;

        // 钱柜容量(文档 20.6): 每级 60/120/240, 王室 ×1.5
        internal static float ChestCap(BuildingGroup g, Settlement s)
        {
            try
            {
                if (g == null) return 60f;
                float perLevel = g.Mode == BuildMode.Iron ? 240f : (g.Mode == BuildMode.Stone ? 120f : 60f);
                float cap = perLevel * Math.Max(1, g.Count);
                if (Ownership.OwnerOf(g, s) == 0) cap *= 1.5f;    // 王室金库
                return cap;
            }
            catch { return 60f; }
        }

        internal static void Monthly()
        {
            int groups = 0, built = 0;
            float toPool = 0f, toPops = 0f, toTreasury = 0f;
            MonthBuilt = 0;
            try
            {
                // 重置本月分红记录(所有权分配会写入)
                foreach (var kvp in Pops.BySettlement)
                {
                    var l = kvp.Value;
                    if (l == null) continue;
                    for (int i = 0; i < l.Count; i++) if (l[i] != null) l[i].DividendMonthly = 0f;
                }

                foreach (var kv in EconomyWorld.Buildings)
                {
                    var sb = kv.Value;
                    if (sb == null || sb.Groups.Count == 0) continue;
                    var s = FindSettlement(kv.Key);
                    if (s == null) continue;
                    var pops = Pops.Of(kv.Key);
                    string kid = s.MapFaction != null ? s.MapFaction.StringId : null;
                    for (int i = 0; i < sb.Groups.Count; i++)
                    {
                        var g = sb.Groups[i];
                        if (g == null || g.Count <= 0 || g.Cash <= 1f) continue;
                        float cap = ChestCap(g, s);
                        float div = g.Cash > cap ? g.Cash : g.Cash * 0.5f;   // 20.6: 超钱柜上限部分全额分红
                        if (div <= 1f) continue;
                        g.Cash -= div;
                        groups++;
                        // v4.252: 只有**玩家国**的建筑分红才进玩家国库/投资池。
                        //   原来这里对全世界(含 AI 与敌国)的建筑调 PayDividend, 而 PayDividend 的
                        //   王室/领主/教会份额全部 TreasuryAdd 进玩家国库 —— 实测每座外国农田每月给玩家
                        //   送 ≈600 金(全图 130 村 -> 月 1e5 量级, 是税收的十倍以上), 等于无上限刷钱。
                        //   现在非玩家国的分红只是离开建筑现金(由该国自己的经济体系消化), 不进玩家账。
                        bool ours = PlayerKingdom() != null && s.MapFaction == PlayerKingdom();
                        if (!ours) continue;
                        if (s.MapFaction != null && s.MapFaction == PlayerKingdom()) toTreasury += div * 0.3f;
                        else toPool += div * 0.4f;
                        toPops += div * 0.3f;
                        Ownership.PayDividend(Ownership.OwnerOf(g, s), div, kid, pops);
                    }
                }
                if (groups > 0) built = PrivateBuild();
                MonthlyAiSeed();   // 别国也要发展(用户): AI 王国每月按规模积累资本 -> 私人建造替他们盖房
                // v4.252: 教会池/行会基金原来**只进不出**(全仓只有 += 没有 -=), 于是
                //   ChurchClout = 池/20 与 BurgerClout 随月份单调无限增长, 中期政治数值失控。
                //   现在每月按 15% 消耗(教会维持/行会运营), 池子自动趋于稳态。
                try
                {
                    Ownership.ChurchPool *= 0.85f;
                    Ownership.GuildFund *= 0.85f;
                }
                catch { }
                DLog.Force("投资池月结: 分红组=" + groups + " 入国库≈" + ((int)toTreasury) + " 入池≈" + ((int)toPool)
                    + " 入个人≈" + ((int)toPops) + " 私人新建=" + built + " 池=" + Describe());
            }
            catch (Exception ex) { DLog.Force("投资池月结异常: " + ex.Message); }
        }

        // AI 王国资本积累(用户: 别国也要有我们的经济系统)
        // 非玩家王国每月按城镇规模注入本地投资池, 由 PrivateBuild 自动替他们建造产业
        internal static void MonthlyAiSeed()
        {
            try
            {
                var pk = PlayerKingdom();
                int n = 0;
                foreach (var k in Kingdom.All)
                {
                    if (k == null || k.IsEliminated || k == pk) continue;
                    int sc = k.Settlements != null ? k.Settlements.Count : 0;
                    if (sc <= 0) continue;
                    if (Of(k.StringId) >= 4000f) continue;   // 上限, 防止无限积累
                    Add(k.StringId, sc * 60f);
                    n++;
                }
                if (n > 0) DLog.Info("AI 资本积累: " + n + " 个国家获得月度投资池注入");
            }
            catch { }
        }

        private static Kingdom PlayerKingdom()
        {
            try { return NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null; }            catch { return null; }
        }

        // 分红计入个人财富(按该职业 Pop 人均分摊)
        private static void WealthTo(List<PopRecord> pops, string profession, float money)
        {
            try
            {
                if (pops == null || money <= 0.01f) return;
                float total = 0f;
                for (int i = 0; i < pops.Count; i++) { var p = pops[i]; if (p != null && p.Profession == profession) total += p.Size; }
                if (total < 0.5f) return;   // 本城无该类股东: 简化不计(小概率)
                float perCapita = money / total;   // 人均分红: 记录后由收入口径摊到每天(财富演化统一推进)
                for (int i = 0; i < pops.Count; i++)
                {
                    var p = pops[i];
                    if (p == null || p.Profession != profession || p.Size < 0.5f) continue;
                    p.DividendMonthly = perCapita;
                }
            }
            catch { }
        }

        // ---- 私人建造(文档 19.8.2) ----
        private static int PrivateBuild()
        {
            int built = 0;
            try
            {
                var player = PlayerKingdom();
                foreach (var k in Kingdom.All)
                {
                    if (k == null || k.IsEliminated) continue;   // v4.0: 玩家王国也参与私人建造(池子由私人所有者分红注入)
                    float pool = Of(k.StringId);
                    if (pool < 800f) continue;

                    Settlement bestS = null; BuildDef bestDef = null; float bestScore = 0f; float bestCost = 0f;
                    foreach (var s in k.Settlements)
                    {
                        if (s == null) continue;
                        SettlementBuildings sb;
                        if (!EconomyWorld.Buildings.TryGetValue(s.StringId, out sb) || sb == null) continue;
                        int limit = BuildingRules.SlotLimit(s.IsTown, s.IsCastle,
                            s.Town != null ? (int)s.Town.Prosperity : 0,
                            s.Village != null ? (int)s.Village.Hearth : 0);
                        if (sb.BuiltCount + sb.Queue.Count >= limit) continue;
                        for (int i = 0; i < BuildDefs.All.Count; i++)
                        {
                            var def = BuildDefs.All[i];
                            if (def == null || def.IsEffect) continue;
                            if (!BuildDefs.AllowedAt(def, s.IsVillage, s.IsCastle, s.IsTown)) continue;
                            // v4.252: 私人资本不得替国家造国有建筑(铸币厂/城墙/港口/税务局等) ——
                            //   原来这里不检查 CanBePrivate, 私人池能造出铸币厂, 而铸币厂一存在就会把
                            //   国家收入口径从"市场净创造"(可上万/日)切成"铸币公式"(个位数), 无声无息砍掉收入
                            if (!BuildDefs.CanBePrivate(def, s.IsCastle)) continue;
                            var existing = sb.Find(def.Id);
                            if (existing != null && existing.Count >= 6) continue;   // 单建筑上限 6 级(防刷)
                            float cost = def.Work[0] * 8f;                            // 池子出资口径: 木档工时 × 8(文档 19.8.2 的简化口径)
                            if (cost > pool) continue;
                            float sc = ScoreOf(def, s);
                            if (sc > bestScore) { bestScore = sc; bestS = s; bestDef = def; bestCost = cost; }
                        }
                    }
                    if (bestS == null || bestDef == null || bestScore <= 0f) continue;
                    var bsb = EconomyWorld.Buildings[bestS.StringId];
                    var grp = bsb.Find(bestDef.Id);
                    if (grp != null) grp.Count += 1;
                    else bsb.Groups.Add(new BuildingGroup(bestDef.Id, 1, BuildMode.Wood));
                    Spend(k.StringId, bestCost);
                    built++;
                    MonthBuilt++;
                    TotalBuilt++;
                    DayBuilt++;
                    DLog.Info("私人建造: " + k.Name + " 在 " + bestS.Name + " 出资建 " + bestDef.Name
                        + " 花费=" + ((int)bestCost) + " 得分=" + bestScore.ToString("F0"));
                }
            }
            catch (Exception ex) { DLog.Force("私人建造异常: " + ex.Message); }
            return built;
        }

        // 打分(文档 19.8.2): 预期利润率 + 缺口系数 + 战略分
        private static float ScoreOf(BuildDef def, Settlement s)
        {
            try
            {
                float rev = 0f, cost = 0f;
                string firstGood = null;
                for (int i = 0; i < def.Outputs.Count; i++)
                {
                    var o = def.Outputs[i];
                    if (o == null || string.IsNullOrEmpty(o.Good) || o.Good.StartsWith("@")) continue;
                    if (firstGood == null) firstGood = o.Good;
                    rev += o.Value(BuildMode.Wood) * PriceOf(o.Good);
                }
                var inputs = def.Inputs;
                if (inputs != null)
                    for (int i = 0; i < inputs.Count; i++)
                        if (inputs[i] != null) cost += inputs[i].Value(BuildMode.Wood) * PriceOf(inputs[i].Good);
                cost += def.Maintenance[0];
                var spec = PopJob.SpecOf(def.Id);
                if (spec != null)
                    for (int i = 0; i < spec.Prof.Length; i++) cost += spec.Num[i] * PopDefs.BaseWage * 1.2f;
                float profit = rev - cost;
                if (profit <= 0.5f) return -1f;
                // 缺口系数: 该商品买/卖单比
                float gap = 1f;
                if (firstGood != null)
                {
                    var n = EconomyWorld.National;
                    float b = 0f, sl = 0f;
                    n.BuyVolume.TryGetValue(firstGood, out b);
                    n.SellVolume.TryGetValue(firstGood, out sl);
                    gap = sl > 0.01f ? b / sl : (b > 0.01f ? 2.5f : 1f);
                    if (gap < 0.8f) gap = 0.8f;
                    if (gap > 3f) gap = 3f;
                }
                // 战略分: 军需/粮食/物流加权
                float strat = 1f;
                if (def.Cat == BuildCat.Military) strat = 1.6f;
                else if (def.Cat == BuildCat.Logistics) strat = 1.4f;
                else if (def.Id == "farm" || def.Id == "pasture" || def.Id == "fishery") strat = 1.3f;
                return profit * gap * 0.6f + profit * strat * 0.4f;
            }
            catch { return -1f; }
        }

        private static float PriceOf(string goodId)
        {
            try { return EconomyWorld.National.PriceOf(goodId); } catch { return 0f; }
        }

        private static Settlement FindSettlement(string sid)
        {
            try
            {
                foreach (var s in Settlement.All) if (s != null && s.StringId == sid) return s;
            }
            catch { }
            return null;
        }

        // ---- 存档(FIA_Pool) ----
        internal static string Save()
        {
            var sb = new StringBuilder();
            sb.Append("v1;");
            foreach (var kv in Pools)
            {
                if (kv.Value <= 0.5f) continue;
                sb.Append(kv.Key).Append(',').Append(kv.Value.ToString("F0", CultureInfo.InvariantCulture)).Append(';');
            }
            return sb.ToString();
        }

        internal static void Load(string data)
        {
            try
            {
                Pools.Clear();
                if (string.IsNullOrEmpty(data)) return;
                var parts = data.Split(';');
                for (int i = 1; i < parts.Length; i++)
                {
                    var ln = parts[i];
                    if (string.IsNullOrEmpty(ln)) continue;
                    var f = ln.Split(',');
                    if (f.Length < 2) continue;
                    float v;
                    if (float.TryParse(f[1], NumberStyles.Float, CultureInfo.InvariantCulture, out v)) Pools[f[0]] = v;
                }
                DLog.Force("投资池: 读档 " + Describe());
            }
            catch (Exception ex) { DLog.Force("投资池读档失败: " + ex.Message); }
        }

        internal static string Describe()
        {
            var sb = new StringBuilder();
            foreach (var kv in Pools)
            {
                if (kv.Value <= 0.5f) continue;
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(kv.Key).Append('=').Append((int)kv.Value);
            }
            return sb.Length > 0 ? sb.ToString() : "空";
        }
    }
}
