using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;

namespace FeudalInternalAffairs
{
    // 每日经济结算(11.3 的 2~3 步): 建筑产出/消耗 -> 本地市场(原版 ItemRoster 作为实物库存)
    // 市场价/调运/进口在 P5 接入; 本阶段先保证"产出能进市场、缺料会停产、全国不缺粮"
    internal static class DailySettlement
    {
        private class Move
        {
            internal ItemObject Item;
            internal string GoodId;
            internal int Amount;
        }

        private static readonly Dictionary<string, int> StorageCache = new Dictionary<string, int>();
        private static int _lastGrainStock = -1;

        // ==================== 查询接口(供食物模型/UI 使用) ====================

        // 某定居点建筑的食物产出合计(粮食/肉/鱼)
        internal static float FoodOutputOf(Settlement s)
        {
            try
            {
                if (s == null || string.IsNullOrEmpty(s.StringId)) return 0f;
                SettlementBuildings sb;
                if (!EconomyWorld.Buildings.TryGetValue(s.StringId, out sb) || sb == null) return 0f;
                if (IsHalted(s)) return 0f;
                float sum = 0f;
                foreach (var g in sb.Groups)
                {
                    if (g == null || g.Count <= 0) continue;
                    var def = g.Def;
                    if (def == null || def.IsEffect) continue;
                    foreach (var outp in def.Outputs)
                    {
                        if (outp == null || outp.Good == null) continue;
                        var gd = FeudalGoods.Get(FirstGood(outp.Good));
                        if (gd == null || !gd.IsFood) continue;
                        sum += outp.Value(g.Mode) * g.Count;
                    }
                }
                return sum;
            }
            catch { return 0f; }
        }

        // 某城镇的食物来源 = 挂靠村庄的食物产出合计
        internal static float OurVillageFood(Settlement town)
        {
            float sum = 0f;
            if (town == null) return 0f;
            try
            {
                foreach (var v in town.BoundVillages)
                {
                    if (v == null || v.Settlement == null) continue;
                    sum += FoodOutputOf(v.Settlement);
                }
            }
            catch { }
            return sum;
        }

        // 定居点 -> 所属市场(城镇)。村庄挂靠原版 TradeBound; 城堡取其村庄的 TradeBound
        internal static Settlement MarketOf(Settlement s)
        {
            try
            {
                if (s == null) return null;
                if (s.IsVillage)
                {
                    var v = s.Village;
                    if (v == null) return null;
                    return v.TradeBound ?? v.Bound;
                }
                if (s.IsCastle)
                {
                    foreach (var v in s.BoundVillages)
                        if (v != null && v.TradeBound != null) return v.TradeBound;
                    return null;
                }
                return s;
            }
            catch { return null; }
        }

        // 围城 / 掠夺 / 村庄非正常状态 -> 停产(2.2)
        internal static bool IsHalted(Settlement s)
        {
            try
            {
                if (s == null) return false;
                if (s.IsUnderSiege || s.IsUnderRaid) return true;
                var v = s.Village;
                if (v != null && v.VillageState != Village.VillageStates.Normal) return true;
            }
            catch { }
            return false;
        }

        // ==================== 每日结算 ====================

        // v4.251: 建筑维护费(玩家国, 从国库出)
        //   用户要求"过一段时间完全不管经济就该崩盘"。原来建筑只吃材料、搬砖不要钱: 玩家(和 AI)把材料
        //   全变成建筑, 既没有维护费也没有工资, 国库只增不减 -> 不存在"不管就崩"。现在每栋建筑每日
        //   收 1.5 金维护费(按栋, 停产也照收), 让"扩张建筑"有持续代价: 60 栋≈90/日(开局税收的 1/6 左右,
        //   不破坏开局安稳), 堆到 400 栋≈600/日 就吃光全部税收, 必须提税/裁军/关停。
        internal const float UpkeepPerBuilding = 1.5f;

        internal static int ChargeUpkeep()
        {
            try
            {
                var pk = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                if (pk == null) return 0;
                int units = 0;
                foreach (var kv in EconomyWorld.Buildings)
                {
                    var sb = kv.Value;
                    if (sb == null || sb.Groups.Count == 0) continue;
                    var st = FindSettlement(kv.Key);
                    if (st == null || st.MapFaction != pk) continue;
                    for (int i = 0; i < sb.Groups.Count; i++)
                    {
                        var g = sb.Groups[i];
                        if (g != null && g.Count > 0) units += g.Count;
                    }
                }
                if (units <= 0) return 0;
                int cost = (int)Math.Ceiling(units * UpkeepPerBuilding);
                if (cost <= 0) return 0;
                EconomyWorld.TreasurySpend(cost);
                Fiscal.AddUpkeep(cost);
                LastUpkeepUnits = units;
                return cost;
            }
            catch { return 0; }
        }

        internal static int LastUpkeepUnits;   // 供财政页显示"本国建筑 N 栋"

        // v4.252: 开局铺底库存 —— 审计结论: 开局**完全没有**任何初始库存(设计文档 11.2 承诺的"市场初值"
        //   从未实现), 而原版村庄产出已被 ProductionTakeover 关掉, 于是 mod 新商品全世界为 0:
        //   铁链(木->炭->铁)要很多天才自举, 没有对应村庄的城镇永远起不来(实测缺料: 铁 228 次 / 木材 85 次 /
        //   粮食 139 次)。这里给每座城镇市场按"约 3~5 日需求"铺一层底货。
        internal static int SeedStartingStock()
        {
            int towns = 0;
            try
            {
                string[] ids =
                {
                    FeudalGoods.Grain, FeudalGoods.Meat, FeudalGoods.Fish, FeudalGoods.Wool, FeudalGoods.Hides,
                    FeudalGoods.Hardwood, FeudalGoods.IronOre, FeudalGoods.Charcoal, FeudalGoods.Clay,
                    FeudalGoods.Linen, FeudalGoods.Leather, FeudalGoods.Tools, FeudalGoods.Stone,
                    FeudalGoods.Beer, FeudalGoods.Pottery, FeudalGoods.Herbs, FeudalGoods.Weapons, FeudalGoods.Armor
                };
                int[] nums = { 40, 15, 15, 20, 20, 30, 20, 20, 15, 10, 10, 8, 25, 10, 10, 6, 8, 5 };
                foreach (var t in TaleWorlds.CampaignSystem.Settlements.Town.AllTowns)
                {
                    if (t == null || t.Settlement == null || t.Settlement.ItemRoster == null) continue;
                    var roster = t.Settlement.ItemRoster;
                    for (int i = 0; i < ids.Length && i < nums.Length; i++)
                    {
                        var it = FeudalGoods.Item(ids[i]);
                        if (it == null) continue;
                        roster.AddToCounts(it, nums[i]);
                    }
                    towns++;
                }
            }
            catch (Exception ex) { DLog.Force("开局铺货异常: " + ex.Message); }
            return towns;
        }

        internal static void Run()
        {
            int settlements = 0, built = 0, stalled = 0, produced = 0, consumed = 0;
            int cAdv = 0, cDone = 0, cStall = 0;
            try
            {
                // 1) 清空当日流量
                foreach (var kv in EconomyWorld.Markets)
                {
                    var m = kv.Value;
                    if (m == null) continue;
                    foreach (var ekv in m.Entries)
                    {
                        var e = ekv.Value;
                        if (e == null) continue;
                        e.DailyProduction = 0f;
                        e.DailyConsumption = 0f;
                        e.BuyOrders = 0f;
                    }
                }
                BuildStorageCache();

                // 2) 逐定居点结算
                foreach (var s in Settlement.All)
                {
                    if (s == null || string.IsNullOrEmpty(s.StringId)) continue;
                    SettlementBuildings sb;
                    if (!EconomyWorld.Buildings.TryGetValue(s.StringId, out sb) || sb == null || sb.Groups.Count == 0) continue;
                    settlements++;
                    var r = TickSettlement(s, sb);
                    built += r[0]; stalled += r[1]; produced += r[2]; consumed += r[3];
                    cAdv += r[4]; cDone += r[5]; cStall += r[6];
                }

                // 2.5) 人口就业与建筑经营(v3.0 文档 19.7; 必须在市场汇总之前)
                PopJob.RunDaily();

                // 3) 市场结算(价格/民用消耗/繁荣/调运/进口) + 刷新市场库存镜像
                var mk = MarketSim.Run();
                foreach (var kv in EconomyWorld.Markets)
                {
                    var m = kv.Value;
                    if (m == null) continue;
                    var town = FindSettlement(m.TownId);
                    if (town == null || town.ItemRoster == null) continue;
                    foreach (var g in FeudalGoods.Main)
                    {
                        var it = FeudalGoods.Item(g.Id);
                        if (it == null) continue;
                        m.GetOrCreate(g.Id).Stock = town.ItemRoster.GetItemNumber(it);
                    }
                }

                // 停产原因分类(诊断)
                int lack = 0, full = 0, halt = 0;
                var samples = new List<string>();
                foreach (var kv in EconomyWorld.Buildings)
                {
                    var sb0 = kv.Value;
                    if (sb0 == null) continue;
                    foreach (var g in sb0.Groups)
                    {
                        if (g == null || !g.Stalled || g.StallReason == null) continue;
                        if (g.StallReason.StartsWith("缺料")) lack++;
                        else if (g.StallReason.StartsWith("库存")) full++;
                        else halt++;
                        if (samples.Count < 3)
                        {
                            string state = "?";
                            try
                            {
                                var s2 = FindSettlement(kv.Key);
                                if (s2 != null)
                                {
                                    if (s2.IsUnderSiege) state = "围城";
                                    else if (s2.IsUnderRaid) state = "被掠";
                                    else if (s2.Village != null) state = "村庄状态" + (int)s2.Village.VillageState;
                                    else state = "正常";
                                }
                            }
                            catch { }
                            samples.Add(kv.Key + ":" + g.DefId + ":" + g.StallReason + "[" + state + "]");
                        }
                    }
                }
                if (DLog.Flag("econ"))
                {
                    // 全球 + 本国粮食概况(P2 验收: 全国不缺粮)
                    int neg = 0, total = 0, grainStock = 0, foodStock = 0;
                    int wGrain = 0, wFood = 0, wZero = 0, wTowns = 0;
                    float worst = 99f;
                    try
                    {
                        var b = NationalWillOrders.Behavior;
                        var k = b != null ? b.NationKingdom : null;
                        var grain = FeudalGoods.Item(FeudalGoods.Grain);
                        foreach (var t in TaleWorlds.CampaignSystem.Settlements.Town.AllTowns)
                        {
                            if (t == null) continue;
                            int g = 0;
                            if (grain != null && t.Settlement != null && t.Settlement.ItemRoster != null)
                                g = t.Settlement.ItemRoster.GetItemNumber(grain);
                            wTowns++;
                            wGrain += g;
                            wFood += (int)Math.Round(t.FoodStocks);
                            if (g <= 0) wZero++;
                            if (k == null || t.OwnerClan == null || t.OwnerClan.Kingdom != k) continue;
                            total++;
                            float fc = t.FoodChange;
                            if (fc < 0f) neg++;
                            if (fc < worst) worst = fc;
                            foodStock += (int)Math.Round(t.FoodStocks);
                            grainStock += g;
                        }
                    }
                    catch { }
                    int delta = _lastGrainStock < 0 ? 0 : grainStock - _lastGrainStock;
                    _lastGrainStock = grainStock;
                    if (total == 0) worst = 0f;
                    DLog.Force("经济日结: 定居点=" + settlements + " 生产单位=" + built + " 停产组=" + stalled
                        + "(缺料" + lack + "/满仓" + full + "/停产" + halt + ")"
                        + " 产出件=" + produced + " 耗料件=" + consumed
                        + " | 建造: 推进" + cAdv + " 完工" + cDone + " 缺料" + cStall
                        + " | 市场: 调运" + mk[0] + " 进口" + mk[1] + " 繁荣+" + (mk[2] / 10f).ToString("F1")
                        + " | 停产样本: " + (samples.Count > 0 ? string.Join(" , ", samples) : "无")
                        + " | 全球: 粮库存=" + wGrain + " 零粮城镇=" + wZero + "/" + wTowns + " 食物储备=" + wFood
                        + " | 本国城镇 " + total + ": 粮库存=" + grainStock + "(" + (delta >= 0 ? "+" : "") + delta + ")"
                        + " 食物储备=" + foodStock
                        + " 食物负增长=" + neg + " 最低结余=" + worst.ToString("F1"));
                }
            }
            catch (Exception ex) { DLog.Force("经济日结异常: " + ex.Message); }
        }

        // 返回 [生产组数, 停产组数, 产出件数, 耗料件数]
        private static int[] TickSettlement(Settlement s, SettlementBuildings sb)
        {
            int built = 0, stalled = 0, produced = 0, consumed = 0;
            int cAdv = 0, cDone = 0, cStall = 0;
            try
            {
                var market = MarketOf(s);
                if (market == null || market.ItemRoster == null) return new[] { 0, 0, 0, 0, 0, 0, 0 };
                var roster = market.ItemRoster;
                var md = EconomyWorld.MarketOf(market.StringId);
                bool halted = IsHalted(s) || IsHalted(market);
                int limit = StockLimitFor(market);

                foreach (var g in sb.Groups)
                {
                    if (g == null) continue;
                    g.Stalled = false;
                    g.StallReason = null;
                    if (g.Count <= 0) continue;
                    var def = g.Def;
                    if (def == null || def.IsEffect) continue;   // 效果类建筑由 P4+ 消费
                    if (halted)
                    {
                        g.Stalled = true;
                        g.StallReason = "围城/掠夺停产";
                        stalled++;
                        continue;
                    }

                    var mode = g.Mode;
                    // ---- 输入检查 ----
                    var inputs = BuildDefs.InputsFor(def, mode);
                    var plan = new List<Move>();
                    bool ok = true;
                    foreach (var inp in inputs)
                    {
                        if (inp == null || inp.Good == null) continue;
                        int want = MBRandom.RoundRandomized(inp.Value(mode) * g.Count);
                        if (want <= 0) continue;
                        var alts = BuildDefs.Alternatives(inp.Good);
                        ItemObject pick = null;
                        string pickGood = null;
                        int best = 0;
                        foreach (var alt in alts)
                        {
                            var it = FeudalGoods.Item(alt);
                            if (it == null) continue;
                            int have = roster.GetItemNumber(it);
                            if (have > best) { best = have; pick = it; pickGood = alt; }
                            if (have >= want) { pick = it; pickGood = alt; best = have; break; }
                        }
                        if (pick == null || best < want)
                        {
                            ok = false;
                            g.StallReason = "缺料 " + FeudalGoods.NameOf(FirstGood(inp.Good));
                            break;
                        }
                        plan.Add(new Move { Item = pick, GoodId = pickGood, Amount = want });
                    }
                    if (!ok)
                    {
                        g.Stalled = true;
                        stalled++;
                        continue;
                    }

                    // ---- 扣输入 ----
                    foreach (var p in plan)
                    {
                        roster.AddToCounts(p.Item, -p.Amount);
                        consumed += p.Amount;
                        if (md != null) { var me = md.GetOrCreate(p.GoodId); me.DailyConsumption += p.Amount; me.BuyOrders += p.Amount; }
                    }

                    // ---- 产出 ----
                    foreach (var outp in def.Outputs)
                    {
                        if (outp == null || outp.Good == null) continue;
                        if (outp.Good.StartsWith("@")) continue;   // 效果类数值不走市场
                        string goodId = ResolveOutputGood(s, outp.Good);
                        var it = FeudalGoods.Item(goodId);
                        if (it == null) continue;
                        // 实物产出按到岗率缩放(文档 19.7.2: 与记账口径一致) + 行会加成 + 政治修正(第 21 章)
                        string catName = BuildDefs.CategoryName(def.Cat);
                        float guildMult = (1f + Guilds.OutputBonus(catName)) * Politics.OutputMult(catName) * Politics.SettlementOutputMult(s) * WarEconomy.OutputMultOf(s.StringId);   // v4.72: 焦土(被掠夺村庄产出-50% 30天)
                        guildMult *= TaxPolicy.IndustryMult;   // v4.252: 税负对工商产出的压制(苛征全开 -20%) —— 原来这个效果只写在面板上, 产出链从不读它(死效果)
                        guildMult *= Infrastructure.AccessOf(s.StringId);   // v4.163: 市场准入(基建/用量) -> 建筑产出乘数(V3 官方)
                        // 到岗率(v4.252): 无人到岗 = 停产(原来 Fill=0 时按满产 1.0 计算, 零工人的建筑照满产照吃料)
                        if (g.Fill <= 0.001f)
                        {
                            g.Stalled = true;
                            g.StallReason = "无人到岗";
                            stalled++;
                            continue;
                        }
                        int got = MBRandom.RoundRandomized(outp.Value(mode) * g.Count * g.Fill * guildMult);
                        if (got <= 0) continue;
                        // 库存上限(12.10)
                        int have = roster.GetItemNumber(it);
                        int room = limit - have;
                        if (room <= 0)
                        {
                            // v4.252: 原来这里只写 StallReason 不置 Stalled —— 于是它是个"僵尸分支":
                            //   日志里"满仓"计数恒为 0, 建筑仍被算作在产(PopJob 照记销售收入、照发工资),
                            //   而原料已经在上面被扣掉了, 等于每天白烧原料、产出被丢。
                            g.Stalled = true;
                            g.StallReason = "库存已满";
                            stalled++;
                            continue;
                        }
                        if (got > room) got = room;
                        roster.AddToCounts(it, got);
                        produced += got;
                        if (md != null) md.GetOrCreate(goodId).DailyProduction += got;
                    }
                    built++;
                }

                // ---- 建造推进(设计 3.4: 材料采购 -> 投入建造点 -> 进度) ----
                var cr = Construction.TickSettlement(s, sb, roster);
                cAdv = cr[0]; cDone = cr[1]; cStall = cr[2];
            }
            catch (Exception ex) { DLog.Info("日结异常 " + (s != null ? s.StringId : "?") + ": " + ex.Message); }
            return new[] { built, stalled, produced, consumed, cAdv, cDone, cStall };
        }

        // 产出里带 "|" 的按村庄特产选(如 矿场黏土/盐)
        private static string ResolveOutputGood(Settlement s, string spec)
        {
            var alts = BuildDefs.Alternatives(spec);
            if (alts.Length == 0) return null;
            if (alts.Length == 1) return alts[0];
            try
            {
                var v = s != null ? s.Village : null;
                if (v != null && v.VillageType != null)
                {
                    foreach (var alt in alts)
                    {
                        var item = FeudalGoods.Item(alt);
                        if (item == null) continue;
                        foreach (var prod in v.VillageType.Productions)
                            if (prod.Item1 == item) return alt;
                    }
                }
            }
            catch { }
            return alts[0];
        }

        private static string FirstGood(string spec)
        {
            var alts = BuildDefs.Alternatives(spec);
            return alts.Length > 0 ? alts[0] : spec;
        }

        // ---- 库存上限: 基础上限 + 仓库/粮仓(12.10) ----
        private static void BuildStorageCache()
        {
            StorageCache.Clear();
            try
            {
                foreach (var s in Settlement.All)
                {
                    if (s == null || string.IsNullOrEmpty(s.StringId)) continue;
                    int st = StorageOf(s);
                    if (st <= 0) continue;
                    var m = MarketOf(s);
                    if (m == null) continue;
                    if (StorageCache.ContainsKey(m.StringId)) StorageCache[m.StringId] += st;
                    else StorageCache[m.StringId] = st;
                }
            }
            catch { }
        }

        private static int StorageOf(Settlement s)
        {
            int sum = 0;
            try
            {
                if (s == null || string.IsNullOrEmpty(s.StringId)) return 0;
                SettlementBuildings sb;
                if (!EconomyWorld.Buildings.TryGetValue(s.StringId, out sb) || sb == null) return 0;
                foreach (var g in sb.Groups)
                {
                    if (g == null || g.Count <= 0) continue;
                    var def = g.Def;
                    if (def == null || !def.IsEffect) continue;
                    foreach (var outp in def.Outputs)
                        if (outp != null && outp.Good == BuildDefs.EffStorage)
                            sum += (int)Math.Round(outp.Value(g.Mode) * g.Count);
                }
            }
            catch { }
            return sum;
        }

        private static int StockLimitFor(Settlement market)
        {
            int limit = (int)MarketRules.BaseStockLimit;
            if (market == null || string.IsNullOrEmpty(market.StringId)) return limit;
            int extra;
            if (StorageCache.TryGetValue(market.StringId, out extra)) limit += extra;
            return limit;
        }

        private static Settlement FindSettlement(string id)
        {
            try
            {
                if (string.IsNullOrEmpty(id)) return null;
                foreach (var s in Settlement.All) if (s != null && s.StringId == id) return s;
            }
            catch { }
            return null;
        }
    }
}
