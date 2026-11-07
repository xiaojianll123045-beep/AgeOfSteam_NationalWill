using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Roster;   // v4.267: ItemRoster(物品数量快照用)
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

        // ==================== v4.267: 日结分片(让"建筑产出"吃上调度器每帧 3ms 预算) ====================
        //   问题: 整步 2295ms(实测日志"最慢: 建筑产出 2295ms") -> 分帧调度器对"单步就是 2 秒"的任务无效,
        //         跨日那一帧照样顿一下。做法: 按**定居点**切片, 把同一个 Run 拆成 8 步交给 DailyScheduler。
        //   等价性: 下面 _planFrom/_planTo 严格按 Settlement.All 的下标区间划分, 8 片拼起来就是一次完整遍历
        //         (顺序不变、每个定居点只处理一次、不把一个定居点的建筑拆到两片), 因此产出/耗料/建造推进
        //         与"一口气跑完"逐字节等价; 跨片只是把累加器放到静态字段上而已。
        //   本组静态字段**不入档**(纯当日内状态), 日结第 0 片负责清零, 因此不需要新存档字段。
        private static bool _tailDone;              // 收尾(④⑤⑥)是否已执行: 防止被重复调用时二次结算
        // v4.269: 16 -> 32。依据: 上一轮实测单片均 ≈90ms(即"最大片"必然 >100ms, 用户的切分阈值就是 100ms),
        //   而 [清流量+建缓存](≈12ms)与 [收尾] 不随片数下降 —— 提到 32 片把"最大的那一帧卡顿"再砍一半。
        //   代价: 日结链路多 16 帧(≈0.3s 实时)才跑完; 若日志里 [逐定居点]最大 明显低于 100ms 想省帧数,
        //   把这一行改回 16 即可(全仓只有这一处定义, EconomyBehavior 按它登记步骤数)。
        internal const int Parts = 32;

        private static int _accSettlements, _accBuilt, _accStalled, _accProduced, _accConsumed;
        private static int _accAdv, _accDone, _accStall;

        // v4.267: 分片耗时归因(仅在 flags 打开 econ 时进正式日志, 不影响日结结果)
        //   目的: 判断"单片还剩多少、花在哪个函数", 供下一轮针对性优化。
        private static float _msHead;                       // 片 0 的清流量+建缓存
        private static float _msLoop, _msLoopMax;          // 各片的逐定居点循环(合计/单片最大)
        private static float _msTail, _msTailPop, _msTailMarket, _msTailMirror, _msTailDiag;   // 收尾各段
        private static float _msTailStat, _msLog;           // v4.269: 收尾里的 粮食统计 / 日志自身
        private static int _partsRun;
        // v4.267: 更细归因(回答"roster 线性查找是否真降下去")
        private static float _msBuild;                      //   └ 建筑产出/耗料(不含建造推进)
        private static float _msConstr;                     //   └ Construction.TickSettlement
        private static long _rosterHits, _rosterMiss;       //   └ 快照命中次数 / 实际回源(线性)查找次数

        // ==================== v4.267: 市场 roster 的"物品数量快照"(方案②: 快照 + 写路径增量更新) ====================
        //   原版 ItemRoster.GetItemNumber(it) 的实现(已反编译核对):
        //     FindIndexOfItem(it) = for(i<_count) if(_data[i].EquipmentElement.Item == it) return i;  -> 线性查找
        //     命中则返回 _data[i].Amount; 未命中返回 0。
        //   也就是说它是 O(堆叠数) 且"按 ItemObject 找**第一个**命中堆叠"(带 ItemModifier 的其它堆叠不计入)。
        //   而逐定居点结算里对同一个市场 roster 要反复查(每建筑每输入备选一次 + 产出上限一次 + 建造材料 3 次),
        //   -> 纯浪费。做法: 一次查找建立 ItemObject -> 数量 的字典; 之后读取走字典, 写入在改 roster 的同时把
        //   同样的增量写进字典 —— 字典因此始终 = "进入本次 TickSettlement 时的 GetItemNumber 值" + "本次我们
        //   造成的全部增量", 与直接调用 GetItemNumber 在任一时刻的结果相同(逐条证明见 ItemCount/ItemTake/ItemGive)。
        //   关键依据: 我们的写入恒用无 modifier 的 EquipmentElement(AddToCounts(ItemObject,int) 内部就是 new
        //   EquipmentElement(item)), 其语义是"有同物品同 modifier 的堆叠就加减它, 否则新建一个 Amount=±n 的堆叠";
        //   无论哪种, FindIndexOfItem 返回的那个堆叠数量都恰好变化 ±n -> 增量同步与真实值恒等。
        private static readonly Dictionary<ItemObject, int> _invMemo = new Dictionary<ItemObject, int>();
        private static ItemRoster _invRoster;
        private static bool _memoVerifyRead;      // flags 里含 "memo" 时逐次校验快照 vs 真实 roster(开发用自检)
        private static long _memoMismatch;

        // 本次 TickSettlement 的对象范围起点: 清空快照, 把"权威 roster"设为该定居点的市场 roster
        private static void BeginItemMemo(ItemRoster roster)
        {
            _invMemo.Clear();
            _invRoster = roster;
        }

        private static void EndItemMemo()
        {
            _invMemo.Clear();
            _invRoster = null;
        }

        // 读取: 等价于 roster.GetItemNumber(item)
        internal static int ItemCount(ItemRoster roster, ItemObject item)
        {
            if (item == null) return 0;
            if (!ReferenceEquals(roster, _invRoster))
            {
                // 不在本次结算范围内的 roster(例如收尾段读别的城): 老老实实回源, 不动快照
                _rosterMiss++;
                return roster != null ? roster.GetItemNumber(item) : 0;
            }
            int v;
            if (_invMemo.TryGetValue(item, out v))
            {
                _rosterHits++;
                // 自检(默认关闭): 校验"快照 == 真实 roster"。若日志出现这行, 说明有写入点没走 ItemTake/ItemGive。
                if (!_memoVerifyRead) _memoVerifyRead = DLog.Flag("memo");
                if (_memoVerifyRead)
                {
                    int liveV = roster.GetItemNumber(item);
                    if (liveV != v)
                    {
                        _memoMismatch++;
                        if (_memoMismatch <= 20)
                            DLog.Force("roster快照自检不一致: 快照=" + v + " 实际=" + liveV + " 差=" + (v - liveV)
                                + " 物品=" + (item.StringId ?? "?"));
                    }
                }
                return v;
            }
            _rosterMiss++;
            v = roster.GetItemNumber(item);   // 建立该物品的条目 = 原版口径
            _invMemo[item] = v;
            return v;
        }

        // 写入(扣料): 等价于 roster.AddToCounts(item, -n) + 快照同步递减
        internal static void ItemTake(ItemRoster roster, ItemObject item, int n)
        {
            if (item == null || n == 0) return;
            if (n < 0) { ItemGive(roster, item, -n); return; }
            roster.AddToCounts(item, -n);
            if (!ReferenceEquals(roster, _invRoster)) return;
            int v;
            if (!_invMemo.TryGetValue(item, out v)) v = roster.GetItemNumber(item) + n;   // 未建条目: 由写入后值反推原值
            v -= n;
            if (v != 0) _invMemo[item] = v; else _invMemo.Remove(item);
        }

        // 写入(产出入库): 等价于 roster.AddToCounts(item, n) + 快照同步递增
        private static void ItemGive(ItemRoster roster, ItemObject item, int n)
        {
            if (item == null || n == 0) return;
            if (n < 0) { ItemTake(roster, item, -n); return; }
            roster.AddToCounts(item, n);
            if (!ReferenceEquals(roster, _invRoster)) return;
            int v;
            if (!_invMemo.TryGetValue(item, out v)) v = roster.GetItemNumber(item) - n;   // 未建条目: 由写入后值反推原值
            _invMemo[item] = v + n;
        }

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

        // v4.267: 一次性跑完(兼容其它调用者)。等价于 RunPart(0, 1)。
        internal static void Run()
        {
            RunPart(0, 1);
        }

        // v4.267: 分片日结 —— part/parts 为 [0, parts) 的片号。
        //   片 0: ①清空当日流量 + ②建库存缓存 + 第一批定居点; 片 1..parts-1: 各自的定居点;
        //   最后一片(part == parts-1): 全部定居点跑完后 ④PopJob ⑤MarketSim+库存镜像 ⑥停产统计与日志。
        //   注意: 切片只按定居点, TickSettlement 内部对该定居点的处理仍然完整(不拆建筑)。
        internal static void RunPart(int part, int parts)
        {
            try
            {
                if (parts <= 0) parts = 1;
                if (part < 0) part = 0;
                if (part >= parts) return;                                 // 片号越界: 忽略
                if (parts == 1) { part = 0; _tailDone = false; }            // Run(): 单次跑完, 允许再次调用

                var all = Settlement.All;
                int total = all != null ? all.Count : 0;
                int from = total > 0 ? (int)((long)part * total / parts) : 0;
                int to = part == parts - 1 ? total : (int)((long)(part + 1) * total / parts);

                // 片 0: ①清空当日流量 + ②库存上限缓存(与原来同序: 清流量 -> 建缓存 -> 逐定居点)
                if (part == 0)
                {
                    _accSettlements = 0; _accBuilt = 0; _accStalled = 0; _accProduced = 0; _accConsumed = 0;
                    _accAdv = 0; _accDone = 0; _accStall = 0;
                    _tailDone = false;
                    _msHead = 0f; _msLoop = 0f; _msLoopMax = 0f;
                    _msTail = 0f; _msTailPop = 0f; _msTailMarket = 0f; _msTailMirror = 0f; _msTailDiag = 0f;
                    _msTailStat = 0f; _msLog = 0f;   // v4.269
                    _msBuild = 0f; _msConstr = 0f; _rosterHits = 0; _rosterMiss = 0;   // v4.267: 细分归因
                    _partsRun = 0;
                    // v4.269: 当日的只读缓存(输出乘子 / 商品备选表)在这里统一失效 —— 它们的输入
                    //   (法律/政治/税负/基建/建筑等级)都是"按日"变化的量, 且这两个缓存只服务于本日的这些片。
                    try { OutputMultCache.Clear(); GoodAltCache.Clear(); } catch { }
                    var w = System.Diagnostics.Stopwatch.StartNew();   // v4.267: 仅计时, 不改变任何结算逻辑
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
                    _msHead += (float)w.Elapsed.TotalMilliseconds;
                }

                // ③ 本片的定居点(逐定居点完整结算, 累加进静态统计)
                var wLoop = System.Diagnostics.Stopwatch.StartNew();   // v4.267
                for (int i = from; all != null && i < to; i++)
                {
                    var s = all[i];
                    if (s == null || string.IsNullOrEmpty(s.StringId)) continue;
                    SettlementBuildings sb;
                    if (!EconomyWorld.Buildings.TryGetValue(s.StringId, out sb) || sb == null || sb.Groups.Count == 0) continue;
                    _accSettlements++;
                    var r = TickSettlement(s, sb);
                    _accBuilt += r[0]; _accStalled += r[1]; _accProduced += r[2]; _accConsumed += r[3];
                    _accAdv += r[4]; _accDone += r[5]; _accStall += r[6];
                }
                float msLoop = (float)wLoop.Elapsed.TotalMilliseconds;
                _msLoop += msLoop;
                if (msLoop > _msLoopMax) _msLoopMax = msLoop;
                _partsRun++;

                // ④⑤⑥ 最后一片收尾(必须在**所有**定居点都结算完之后)
                if (part == parts - 1 && !_tailDone)
                {
                    _tailDone = true;
                    RunTail();
                }
            }
            catch (Exception ex) { DLog.Force("经济日结异常(片 " + (part + 1) + "/" + parts + "): " + ex.Message); }
        }

        // v4.267: 全定居点跑完后的收尾(原 Run 的 2.5/3/诊断日志原样搬过来)
        private static void RunTail()
        {
            var wTail = System.Diagnostics.Stopwatch.StartNew();   // v4.267: 仅计时
            // 2.5) 人口就业与建筑经营(v3.0 文档 19.7; 必须在市场汇总之前)
            var wPop = System.Diagnostics.Stopwatch.StartNew();
            PopJob.RunDaily();
            _msTailPop += (float)wPop.Elapsed.TotalMilliseconds;

            // 3) 市场结算(价格/民用消耗/繁荣/调运/进口) + 刷新市场库存镜像
            var wMkt = System.Diagnostics.Stopwatch.StartNew();
            var mk = MarketSim.Run();
            _msTailMarket += (float)wMkt.Elapsed.TotalMilliseconds;
            var wMir = System.Diagnostics.Stopwatch.StartNew();
            foreach (var kv in EconomyWorld.Markets)
            {
                var m = kv.Value;
                if (m == null) continue;
                var town = FindSettlement(m.TownId);
                if (town == null || town.ItemRoster == null) continue;
                // v4.267: 这里**故意不改**成"一次遍历建表"。反编译原版确认:
                //   ItemRoster.GetItemNumber(item) -> FindIndexOfItem(item)(按 EquipmentElement.Item 引用线性找)
                //   -> 返回**第一个命中堆叠**的数量(带 ItemModifier 的同物品堆叠不会被合并计入)。
                //   若改成"按 ItemObject 汇总所有堆叠", 遇到带 modifier 的堆叠就会得到比原版更大的库存镜像,
                //   从而改变 MarketData.Stock 的语义。此处 18 次/城 × ~100 城 ≈ 1800 次, 相对日结总量可忽略,
                //   按你的"别改变结算语义"要求保持原口径不动。
                foreach (var g in FeudalGoods.Main)
                {
                    var it = FeudalGoods.Item(g.Id);
                    if (it == null) continue;
                    m.GetOrCreate(g.Id).Stock = town.ItemRoster.GetItemNumber(it);
                }
            }
            _msTailMirror += (float)wMir.Elapsed.TotalMilliseconds;

            // 停产原因分类(诊断)
            var wDiag = System.Diagnostics.Stopwatch.StartNew();
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
            _msTailDiag += (float)wDiag.Elapsed.TotalMilliseconds;
            if (DLog.Flag("econ"))
            {
                // 全球 + 本国粮食概况(P2 验收: 全国不缺粮)
                var wStat = System.Diagnostics.Stopwatch.StartNew();   // v4.269: 这段统计自己也要能被归因(它要扫全图城镇 roster)
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
                _msTailStat = (float)wStat.Elapsed.TotalMilliseconds;
                int delta = _lastGrainStock < 0 ? 0 : grainStock - _lastGrainStock;
                _lastGrainStock = grainStock;
                if (total == 0) worst = 0f;
                // v4.267: 分片耗时归因(只统计, 不影响结果) —— 用来判断"单片还剩多少、花在哪个函数"
                // v4.269: [收尾] 再拆细: 市场段内部按 MarketSim 的分段计时(PopSim/全国价/本地价/调运/进口/出口/商队),
                //   并单列 [统计](扫全图粮食) 与 [日志](本两行日志自身的字符串拼接) —— 这两段原来是混在 [收尾] 里的。
                _msTail += (float)wTail.Elapsed.TotalMilliseconds;
                long lookups = _rosterHits + _rosterMiss;
                long mLookups = RosterMemo.Hits + RosterMemo.Miss;
                DLog.Force("经济日结: 定居点=" + _accSettlements + " 生产单位=" + _accBuilt + " 停产组=" + _accStalled
                    + "(缺料" + lack + "/满仓" + full + "/停产" + halt + ")"
                    + " 产出件=" + _accProduced + " 耗料件=" + _accConsumed
                    + " | 建造: 推进" + _accAdv + " 完工" + _accDone + " 缺料" + _accStall
                    + " | 市场: 调运" + mk[0] + " 进口" + mk[1] + " 繁荣+" + (mk[2] / 10f).ToString("F1")
                    + " | 停产样本: " + (samples.Count > 0 ? string.Join(" , ", samples) : "无")
                    + " | 全球: 粮库存=" + wGrain + " 零粮城镇=" + wZero + "/" + wTowns + " 食物储备=" + wFood
                    + " | 本国城镇 " + total + ": 粮库存=" + grainStock + "(" + (delta >= 0 ? "+" : "") + delta + ")"
                    + " 食物储备=" + foodStock
                    + " 食物负增长=" + neg + " 最低结余=" + worst.ToString("F1"));
                var wLog = System.Diagnostics.Stopwatch.StartNew();   // v4.269: 日志自身的开销
                DLog.Force("日结耗时归因(共 " + _partsRun + " 片/" + Parts + "): [清流量+建缓存]=" + _msHead.ToString("F1")
                    + "ms | [逐定居点]合计=" + _msLoop.ToString("F1") + "ms 单片均="
                    + (_partsRun > 0 ? (_msLoop / _partsRun).ToString("F1") : "0") + "ms 最大=" + _msLoopMax.ToString("F1")
                    + "ms(其中 建筑产出/耗料=" + _msBuild.ToString("F1") + "ms 建造推进=" + _msConstr.ToString("F1")
                    + "ms 其它=定居点定位+快照开销) | [收尾]=" + _msTail.ToString("F1") + "ms(PopJob=" + _msTailPop.ToString("F1")
                    + " 市场=" + _msTailMarket.ToString("F1") + "ms[人口消费=" + MarketSim.MsPop.ToString("F1")
                    + " 全国价=" + MarketSim.MsNat.ToString("F1") + " 本地价=" + MarketSim.MsLocal.ToString("F1")
                    + " 调运=" + MarketSim.MsMove.ToString("F1") + " 进口=" + MarketSim.MsImp.ToString("F1")
                    + " 出口=" + MarketSim.MsExp.ToString("F1") + " 商队=" + MarketSim.MsCar.ToString("F1")
                    + "] 库存镜像=" + _msTailMirror.ToString("F1") + " 停产诊断=" + _msTailDiag.ToString("F1")
                    + " 粮食统计=" + _msTailStat.ToString("F1") + "]"
                    + " | [乘子缓存] 条目=" + OutputMultCache.Count + " 命中=" + OutputMultCache.Hits
                    + " 回源=" + OutputMultCache.Misses + " 备选表=" + GoodAltCache.Count
                    + " | [roster查询] 建筑日结 共=" + lookups + " 快照命中=" + _rosterHits + " 回源(线性)=" + _rosterMiss
                    + (lookups > 0 ? " 命中率=" + ((_rosterHits * 100L / lookups)).ToString() + "%" : "")
                    + " | 市场/人口 共=" + mLookups + " 快照命中=" + RosterMemo.Hits + " 回源(线性)=" + RosterMemo.Miss
                    + (mLookups > 0 ? " 命中率=" + ((RosterMemo.Hits * 100L / mLookups)).ToString() + "%" : "")
                    + " 建表roster=" + RosterMemo.Rosters + "(条目" + RosterMemo.Entries + ") 放弃缓存=" + RosterMemo.Skipped);
                _msLog = (float)wLog.Elapsed.TotalMilliseconds;
                // v4.269: 归因日志自身的开销(只在明显时单列一行, 免得每天多刷一行日志)
                if (_msLog >= 1f)
                    DLog.Force("日结归因日志自身开销: " + _msLog.ToString("F1") + "ms(上面那两行的字符串拼接+落盘)");
            }
            else
            {
                _msTail += (float)wTail.Elapsed.TotalMilliseconds;
            }
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
                // v4.267: 本次定居点结算的物品数量快照(与该市场 roster 一一对应, 建造扣料也走它)
                BeginItemMemo(roster);
                var wBuild = System.Diagnostics.Stopwatch.StartNew();
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
                        var alts = GoodAltCache.Of(inp.Good);   // v4.269: 等价于 BuildDefs.Alternatives(纯函数, 只读返回数组)
                        ItemObject pick = null;
                        string pickGood = null;
                        int best = 0;
                        foreach (var alt in alts)
                        {
                            var it = FeudalGoods.Item(alt);
                            if (it == null) continue;
                            int have = ItemCount(roster, it);   // v4.267: 走快照(等价于 roster.GetItemNumber)
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
                        ItemTake(roster, p.Item, p.Amount);   // v4.267: 扣料 + 快照同步递减
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
                        // v4.269: 六个乘子原来在这里逐条产出重算一遍; 它们的入参只有(定居点, 建筑类别) ->
                        //   改为按(定居点, 类别)当日缓存(乘法顺序逐字不变, 浮点结果逐位相同; 无副作用论证见 DayCaches.cs)
                        float guildMult = OutputMultCache.Of(s, catName);
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
                        int have = ItemCount(roster, it);   // v4.267: 走快照
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
                        ItemGive(roster, it, got);   // v4.267: 产出入库 + 快照同步递增
                        produced += got;
                        if (md != null) md.GetOrCreate(goodId).DailyProduction += got;
                    }
                    built++;
                }
                _msBuild += (float)wBuild.Elapsed.TotalMilliseconds;

                // ---- 建造推进(设计 3.4: 材料采购 -> 投入建造点 -> 进度) ----
                var wConstr = System.Diagnostics.Stopwatch.StartNew();
                var cr = Construction.TickSettlement(s, sb, roster);
                cAdv = cr[0]; cDone = cr[1]; cStall = cr[2];
                _msConstr += (float)wConstr.Elapsed.TotalMilliseconds;
            }
            catch (Exception ex) { DLog.Force("日结异常 " + (s != null ? s.StringId : "?") + ": " + ex.Message); }   // v4.267: 关键路径(逐定居点日结)不再静默
            finally { EndItemMemo(); }   // v4.267: 快照严格限定在"本定居点本次结算"内, 绝不跨定居点复用
            return new[] { built, stalled, produced, consumed, cAdv, cDone, cStall };
        }

        // 产出里带 "|" 的按村庄特产选(如 矿场黏土/盐)
        private static string ResolveOutputGood(Settlement s, string spec)
        {
            var alts = GoodAltCache.Of(spec);   // v4.269: 等价于 BuildDefs.Alternatives
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
            var alts = GoodAltCache.Of(spec);   // v4.269: 等价于 BuildDefs.Alternatives
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

        // v4.267: 定居点 id -> Settlement 的缓存字典。
        //   原来这里是**每次调用都线性扫 Settlement.All**(实机 540 个定居点), 而日结里 ChargeUpkeep、
        //   市场库存镜像、停产诊断各调一遍 -> 纯浪费。改成"按 Settlement.All.Count 指纹重建"的字典查表。
        //   等价性保证:
        //     ① 命中缓存 -> 返回对象与线性扫描命中的**同一个 Settlement 实例**(键就是 StringId, 一一对应),
        //        所有上层读取(IsUnderSiege/VillageState/OwnerClan/ItemRoster...)完全一致;
        //     ② 未命中(缓存里没有该 id, 含缓存为空/刚被清) -> **回退原来的线性扫描**, 行为与改前逐字一致;
        //     ③ 定居点集合在战役内稳定, 只有数量变化时才重建(建造/摧毁/读档切换), 指纹不同必然重建 ->
        //        不会出现"新定居点查不到"或"旧定居点查到过期对象"的静默错误; 查询异常同样回退线性扫描。
        private static Dictionary<string, Settlement> _settleIndex;
        private static int _settleStamp = -1;
        private static object _settleIndexOf;   // 索引对应的 Settlement.All 容器对象(换战役必然不同)

        // v4.267: 换战役/复位时清掉定居点索引(与 StorageCache 一同失效, 免得跨战役复用旧对象)
        internal static void DropCaches()
        {
            try { StorageCache.Clear(); _settleIndex = null; _settleStamp = -1; _settleIndexOf = null; } catch { }
        }

        private static Settlement FindSettlement(string id)
        {
            try
            {
                if (string.IsNullOrEmpty(id)) return null;
                var all = Settlement.All;
                int now = all != null ? all.Count : 0;
                if (_settleIndex == null || _settleStamp != now || !ReferenceEquals(_settleIndexOf, all))
                {
                    _settleIndexOf = all;   // v4.267: 容器换了(换战役/换档)也必须重建, 不能只看数量
                    var idx = new Dictionary<string, Settlement>(StringComparer.Ordinal);
                    for (int i = 0; i < now; i++)
                    {
                        var s = all[i];
                        if (s == null || string.IsNullOrEmpty(s.StringId)) continue;
                        idx[s.StringId] = s;    // StringId 唯一 -> 与线性扫描"先命中者"同解
                    }
                    _settleIndex = idx;
                    _settleStamp = now;
                }
                Settlement hit;
                if (_settleIndex.TryGetValue(id, out hit) && hit != null) return hit;
                // 回退: 缓存不命中 -> 老老实实线性扫描(不改行为)
                for (int i = 0; all != null && i < now; i++)
                {
                    var s = all[i];
                    if (s != null && s.StringId == id) return s;
                }
            }
            catch { }
            return null;
        }
    }
}
