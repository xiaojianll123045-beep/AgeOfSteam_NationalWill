using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace FeudalInternalAffairs
{
    // v4.162: 铁路线数据(第 26 章设计): 玩家/AI 按需建线
    //   建线: 科技 railways + 端点城市 + 名额 + 花费(距离×2 金) -> 选线( RailNetwork.MakeLineByIds )
    //   工期: 长度/1000 × 15 天, 每日推进; 建成 -> 运营(白线+蓝点) ; 端点被敌占 -> 中断
    internal class RailLine
    {
        internal string FromId;
        internal string ToId;
        internal string OwnerId;          // 王国 StringId
        internal int StartDay;
        internal float Progress;          // 0~1
        internal bool Built;
        internal bool Broken;
        internal float Length;
        internal List<Vec2> Points = new List<Vec2>();
        internal int Tier = 1;            // v4.21x: 线路等级(列车 PM 1~4): 基建/运输 20/25/30/40
        internal bool StateOwned = true;  // v4.21x: 国有(收入 100% 国库)/私有(50% 投资池)

        internal string Key { get { return FromId + ">" + ToId; } }
    }

    // v4.164: 铁路运输任务(城市详情页"上火车": 部队隐藏 -> 沿线移动 -> 到达/围城)
    internal class RailTransport
    {
        internal string PartyId;      // MobileParty.StringId
        internal string FromId;
        internal string ToId;
        internal int StartDay;
        internal float Progress;      // 0~1
        internal float TotalDays;     // 运输总天数
    }

    internal static class Railways
    {
        internal static readonly List<RailLine> All = new List<RailLine>();
        internal static readonly List<RailTransport> Transports = new List<RailTransport>();   // v4.164 商品运输队列
        internal static readonly List<RailTransport> MilTransports = new List<RailTransport>(); // v4.21x 军列队列(独立运力池/冷却)
        internal static readonly Dictionary<string, int> MilUsedToday = new Dictionary<string, int>();       // 城 -> 今日已用军列运力
        internal static readonly Dictionary<string, int> MilLastDispatchDay = new Dictionary<string, int>(); // 城 -> 上次军列发车日
        private static readonly Dictionary<string, int> _aiLegionDispatchDay = new Dictionary<string, int>(); // AI 军列: 军团上次投送日(读档/重置清空)
        private static readonly Dictionary<string, int> _dayIncome = new Dictionary<string, int>();          // v4.21x: 线路 Key -> 当日运费收入(日结清零, UI/DailyNetOf 用)
        internal const int MaxTier = 4;
        internal const int MilCooldownDays = 2;   // 军列发车冷却(天), 与商品运输互不影响
        private static int _lastDay = -1;
        private static int _lastMilResetDay = -1;
        private static int _nextVisualCheck;
        private static string _estKey;
        private static RailNetwork.Line _estLine;   // UI 预估的选线结果(Establish 复用, 避免重复选线)

        // ================= 查询 =================
        internal static bool HasLine(string a, string b)
        {
            try
            {
                for (int i = 0; i < All.Count; i++)
                {
                    var l = All[i];
                    if (l == null) continue;
                    if ((l.FromId == a && l.ToId == b) || (l.FromId == b && l.ToId == a)) return true;
                }
            }
            catch { }
            return false;
        }

        internal static List<RailLine> LinesOf(string settlementId)
        {
            var list = new List<RailLine>();
            try
            {
                for (int i = 0; i < All.Count; i++)
                {
                    var l = All[i];
                    if (l == null || !l.Built) continue;
                    if (l.FromId == settlementId || l.ToId == settlementId) list.Add(l);
                }
            }
            catch { }
            return list;
        }

        // 运营中的线路(两端连接; 无则 null)
        internal static RailLine FindLine(string a, string b)
        {
            try
            {
                for (int i = 0; i < All.Count; i++)
                {
                    var l = All[i];
                    if (l == null || !l.Built || l.Broken) continue;
                    if ((l.FromId == a && l.ToId == b) || (l.FromId == b && l.ToId == a)) return l;
                }
            }
            catch { }
            return null;
        }

        // ================= v4.21x: V3 铁路产出 / 市场准入 / 运力 =================
        // 线路等级(列车 PM)产出: 基建 20/25/30/40, 运输 20/25/30/40
        internal static int TierOf(RailLine l) { return l == null ? 1 : ClampTier(l.Tier); }
        internal static void UpgradeCost(int targetTier, out int gold, out int engines) { gold = UpgradeGoldCost(targetTier); engines = UpgradeEngineCount(targetTier); }
        internal static bool IsStateOwned(RailLine l) { return l == null || l.StateOwned; }
        internal static string Upgrade(RailLine l) { return UpgradeLine(l); }
        internal static string LineSummary(RailLine l)
        {
            if (l == null) return "";
            int t = ClampTier(l.Tier);
            return "等级 " + t + " · 基建 +" + TierInfra(t) + " · 运输 +" + TierTransport(t) + (l.StateOwned ? " · 国有" : " · 私有");
        }

        internal static int ClampTier(int tier) { return tier < 1 ? 1 : (tier > MaxTier ? MaxTier : tier); }
        internal static int TierInfra(int tier)
        {
            switch (ClampTier(tier)) { case 1: return 20; case 2: return 25; case 3: return 30; default: return 40; }
        }
        internal static int TierTransport(int tier)
        {
            switch (ClampTier(tier)) { case 1: return 20; case 2: return 25; case 3: return 30; default: return 40; }
        }

        // 该城铁路基建容量(运营中线路按等级求和; Infrastructure.BaseOf 复用此口径, 不另造数)
        internal static int InfraOf(string settlementId)
        {
            int v = 0;
            try
            {
                var lines = LinesOf(settlementId);
                for (int i = 0; i < lines.Count; i++)
                {
                    var l = lines[i];
                    if (l == null || l.Broken) continue;
                    v += TierInfra(l.Tier);
                }
            }
            catch { }
            return v;
        }

        // 市场准入: 复用 Infrastructure 的基建/占用口径, MA = min(1, 基建/占用)
        internal static float MarketAccessOf(string settlementId, out int capacity, out int load)
        {
            capacity = 0; load = 0;
            try
            {
                capacity = (int)Infrastructure.BaseOf(settlementId);
                load = (int)Infrastructure.UsageOf(settlementId);
                return Infrastructure.AccessOf(settlementId);
            }
            catch { return 1f; }
        }

        // 运输: produced = 线路干线运输产出(按等级) + 铁路建筑(车站/枢纽)20/级; consumed = 本城(含属村)矿业/种植园/伐木/油井类建筑 5/级
        internal static void TransportOf(string settlementId, out int produced, out int consumed)
        {
            produced = 0; consumed = 0;
            try
            {
                if (string.IsNullOrEmpty(settlementId)) return;
                var lines = LinesOf(settlementId);
                for (int i = 0; i < lines.Count; i++)
                {
                    var l = lines[i];
                    if (l == null || l.Broken) continue;
                    produced += TierTransport(l.Tier);
                }
                produced += 20 * RailwayBuildingLevelOf(settlementId);   // 车站/枢纽运力, 与线路干线运力相加(来源不同, 不重复)
                AddExtractiveLoad(settlementId, ref consumed);
                var s = FindSettlement(settlementId);
                if (s != null)
                {
                    try
                    {
                        foreach (var v in s.BoundVillages)
                        {
                            if (v == null || v.Settlement == null) continue;
                            AddExtractiveLoad(v.Settlement.StringId, ref consumed);
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        private static void AddExtractiveLoad(string sid, ref int consumed)
        {
            try
            {
                var sb = EconomyWorld.Find(sid);
                if (sb == null) return;
                for (int i = 0; i < sb.Groups.Count; i++)
                {
                    var g = sb.Groups[i];
                    if (g == null || g.Count <= 0) continue;
                    if (!IsExtractive(g.DefId)) continue;
                    consumed += 5 * g.Count;
                }
            }
            catch { }
        }

        private static bool IsExtractive(string defId)
        {
            if (string.IsNullOrEmpty(defId)) return false;
            return defId.IndexOf("mine", StringComparison.OrdinalIgnoreCase) >= 0
                || defId.IndexOf("quarry", StringComparison.OrdinalIgnoreCase) >= 0
                || defId.IndexOf("plantation", StringComparison.OrdinalIgnoreCase) >= 0
                || defId.IndexOf("lumberjack", StringComparison.OrdinalIgnoreCase) >= 0
                || defId.IndexOf("oil_rig", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // ================= v4.21x: 所有权 / 升级(列车 PM) =================
        internal static string SetOwnership(RailLine l, bool stateOwned)
        {
            try
            {
                if (l == null) return "无效线路";
                l.StateOwned = stateOwned;
                return stateOwned ? "已收归国有(线路收入 100% 入国库)" : "已转为私有(线路收入 50% 入投资池)";
            }
            catch { return "设置所有权失败"; }
        }

        internal static bool HasEngineGood
        {
            get { try { return FeudalGoods.Get(FeudalGoods.Engines) != null; } catch { return false; } }
        }

        // 引擎按现有商品/价格体系折算成金(无引擎商品返回 0)
        internal static float EngineUnitPrice()
        {
            try
            {
                if (!HasEngineGood) return 0f;
                return EconomyWorld.National.PriceOf(FeudalGoods.Engines);
            }
            catch { return 0f; }
        }

        internal static int UpgradeEngineCount(int targetTier) { return 20 * ClampTier(targetTier); }

        // 升到 targetTier 的金费: 500×N + 引擎 20×N 按当前价折金
        internal static int UpgradeGoldCost(int targetTier)
        {
            int n = ClampTier(targetTier);
            int gold = 500 * n;
            float p = EngineUnitPrice();
            if (p > 0f) gold += (int)Math.Round(20 * n * p);
            return gold;
        }

        internal static int UpgradeGoldCost(RailLine l)
        {
            if (l == null || l.Tier >= MaxTier) return 0;
            return UpgradeGoldCost(l.Tier + 1);
        }

        // 升级立即生效(基建/运输产出随 Tier 变化); 返回空串 = 成功
        internal static string UpgradeLine(RailLine l)
        {
            try
            {
                if (l == null) return "无效线路";
                if (l.Tier >= MaxTier) return "已是最高等级(" + MaxTier + " 级)";
                int target = ClampTier(l.Tier + 1);
                int eng = UpgradeEngineCount(target);
                float price = EngineUnitPrice();
                bool withEngine = price > 0f;
                int gold = UpgradeGoldCost(target);
                var owner = FindKingdom(l.OwnerId);
                if (owner == null) return "线路无归属国";
                if (!TryPayKingdom(owner, gold)) return "国库不足(需 " + gold + " 金)";
                l.Tier = target;
                string msg = "铁路升级至 " + target + " 级: 基建/运输 " + TierInfra(target) + "/" + TierTransport(target)
                    + ", 费 " + gold + " 金";
                msg += withEngine ? "(含引擎 " + eng + " 台×" + (int)price + " 金)" : "(无发动机商品: 引擎按等值金币折算)";
                DLog.Force("铁路: 升级 " + l.FromId + " -> " + l.ToId + " 至 " + target + " 级, 费 " + gold + " 金");
                return "";
            }
            catch (Exception ex) { DLog.Force("铁路升级异常: " + ex.Message); return "升级异常, 见日志"; }
        }

        // 线路收入路由: 国有 -> 100% 国库; 私有 -> 50% 投资池 + 50% 国库(投资池不可用时折半入库)
        internal static void PayLineRevenue(RailLine l, int amount)
        {
            try
            {
                if (l == null || amount <= 0) return;
                int today;
                _dayIncome.TryGetValue(l.Key, out today);
                _dayIncome[l.Key] = today + amount;   // v4.21x: 当日日收入累计(DailyNetOf 输出)
                var k = FindKingdom(l.OwnerId);
                int toPool = 0;
                if (!l.StateOwned && k != null)
                {
                    toPool = amount / 2;
                    try { InvestmentPool.Add(k.StringId, toPool); }
                    catch { toPool = 0; }   // 找不到投资池: 该 50% 折半入国库
                }
                int toTreasury = amount - toPool;
                bool player = k != null && ReferenceEquals(k, PlayerKingdom());
                if (player)
                {
                    EconomyWorld.TreasuryAdd(toTreasury);
                    try { Fiscal.AddCourt(-toTreasury); } catch { }
                }
                else if (k != null)
                {
                    try { WarEconomy.AddPublic(k, toTreasury); } catch { }
                }
                if (toPool > 0) DLog.Info("铁路私有收入: " + k.Name + " 投资池 +" + toPool);
            }
            catch { }
        }

        private static Kingdom PlayerKingdom()
        {
            try { return NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null; }
            catch { return null; }
        }

        // 统一付费入口(玩家王国 -> 国库, AI -> 公共金库)
        internal static bool TryPayKingdom(Kingdom k, int cost)
        {
            try
            {
                if (cost <= 0) return true;
                if (k == null) return false;
                if (ReferenceEquals(k, PlayerKingdom()))
                {
                    if (EconomyWorld.Treasury.Gold < cost) return false;
                    EconomyWorld.TreasurySpend(cost);
                    try { Fiscal.AddCourt(cost); } catch { }
                    return true;
                }
                if (WarEconomy.GoldOfPublic(k) < cost) return false;
                WarEconomy.SpendPublic(k, cost);
                return true;
            }
            catch { return false; }
        }

        // ================= v4.21x: 维护费 / 拆除返还 =================
        // 维护费: 10 × 线路等级 金/日
        internal static int MaintenanceOf(RailLine l)
        {
            try { return l == null ? 0 : 10 * ClampTier(l.Tier); }
            catch { return 0; }
        }

        // 线路日收支(UI 用): income = 当日累计运费收入(现有 PayLineRevenue 口径), upkeep = 维护费
        internal static int DailyNetOf(RailLine l, out int income, out int upkeep)
        {
            income = 0; upkeep = 0;
            try
            {
                if (l == null) return 0;
                upkeep = MaintenanceOf(l);
                int v;
                if (_dayIncome.TryGetValue(l.Key, out v)) income = v;
            }
            catch { }
            return income - upkeep;
        }

        // 维护费扣款: 私有 -> 投资池, 国有 -> 国库(玩家)/公共金库(AI); 返回是否付清
        private static bool PayLineUpkeep(RailLine l, int upkeep)
        {
            try
            {
                if (l == null || upkeep <= 0) return true;
                var k = FindKingdom(l.OwnerId);
                if (k == null) return false;
                if (!IsStateOwned(l))
                {
                    float pool = InvestmentPool.Of(k.StringId);
                    if (pool < upkeep) return false;
                    InvestmentPool.Pools[k.StringId] = pool - upkeep;
                    return true;
                }
                return TryPayKingdom(k, upkeep);
            }
            catch { return false; }
        }

        // 拆除返还: 建线花费(距离×2 金)的 50%
        internal static int RemovalRefund(RailLine l)
        {
            try
            {
                if (l == null) return 0;
                return (int)(l.Length * 2f) / 2;
            }
            catch { return 0; }
        }

        // 名额: 2 + 全国铁路建筑等级×2
        internal static int MaxLines(Kingdom k)
        {
            try
            {
                int n = 2;
                if (k != null) n += RailwayBuildingLevels(k) * 2;
                return n;
            }
            catch { return 2; }
        }

        // ================= 铁路建筑等级(按天缓存) =================
        // 建筑系统无版本号 -> 以日结天数为缓存键; 跨日/读档/重置才重算
        private static int _railLevelDay = int.MinValue;
        private static readonly Dictionary<string, int> _railLevelBySettlement = new Dictionary<string, int>();
        private static readonly Dictionary<string, int> _railLevelByKingdom = new Dictionary<string, int>();

        // 该城铁路建筑等级(车站/枢纽; BuildingDefs railway: 运输/基建 20/级)
        private static int RailwayBuildingLevelOf(string settlementId)
        {
            try
            {
                if (string.IsNullOrEmpty(settlementId)) return 0;
                EnsureRailLevelCache();
                int v;
                return _railLevelBySettlement.TryGetValue(settlementId, out v) ? v : 0;
            }
            catch { return 0; }
        }

        // 单遍重建缓存: O(定居点) 建势力表 + O(建筑) 扫描, 替代原先每国 O(建筑×定居点) 的势力匹配
        private static void EnsureRailLevelCache()
        {
            int day = Politics.Today();
            if (day == _railLevelDay) return;
            _railLevelDay = day;
            _railLevelBySettlement.Clear();
            _railLevelByKingdom.Clear();
            try
            {
                var faction = new Dictionary<string, Kingdom>();
                try
                {
                    foreach (var s in Settlement.All)
                        if (s != null && !string.IsNullOrEmpty(s.StringId)) faction[s.StringId] = s.MapFaction as Kingdom;
                }
                catch { }
                foreach (var kv in EconomyWorld.Buildings)
                {
                    var sb = kv.Value;
                    if (sb == null) continue;
                    int n = 0;
                    for (int i = 0; i < sb.Groups.Count; i++)
                    {
                        var g = sb.Groups[i];
                        if (g == null || g.Count <= 0) continue;
                        string id = g.DefId ?? "";
                        if (id.IndexOf("railway", StringComparison.OrdinalIgnoreCase) >= 0) n += g.Count;
                    }
                    if (n <= 0) continue;
                    _railLevelBySettlement[kv.Key] = n;
                    Kingdom k;
                    if (faction.TryGetValue(kv.Key, out k) && k != null)
                    {
                        int cur;
                        _railLevelByKingdom.TryGetValue(k.StringId, out cur);
                        _railLevelByKingdom[k.StringId] = cur + n;
                    }
                }
            }
            catch { }
        }

        // 统计某国全部"铁路"建筑等级(MaxLines/AI 月结共用; 读按天缓存)
        internal static int RailwayBuildingLevels(Kingdom k)
        {
            try
            {
                if (k == null) return 0;
                EnsureRailLevelCache();
                int v;
                return _railLevelByKingdom.TryGetValue(k.StringId, out v) ? v : 0;
            }
            catch { return 0; }
        }

        internal static int CountOf(Kingdom k)
        {
            try
            {
                int n = 0;
                for (int i = 0; i < All.Count; i++)
                    if (All[i] != null && All[i].OwnerId == (k != null ? k.StringId : "")) n++;
                return n;
            }
            catch { return 0; }
        }

        // ================= 建线 =================
        // 返回空字符串 = 成功; 否则为失败原因
        internal static string Establish(Kingdom owner, string fromId, string toId)
        {
            try
            {
                if (owner == null) return "无归属国";
                var from = FindSettlement(fromId);
                var to = FindSettlement(toId);
                if (from == null || to == null) return "端点城市无效";
                if (fromId == toId) return "起终点相同";
                if (!from.IsTown && !from.IsCastle) return "起点必须是城镇或城堡";
                if (!to.IsTown && !to.IsCastle) return "终点必须是城镇或城堡";
                if (HasLine(fromId, toId)) return "两地之间已有铁路";

                // 科技前置(玩家国; AI 由 AI 逻辑保证)
                bool tech = false;
                try { tech = Research.IsDone("railways"); } catch { }
                if (!tech) return "需要先研究「铁路」科技";

                // 名额
                if (CountOf(owner) >= MaxLines(owner))
                    return "铁路名额已满(" + CountOf(owner) + "/" + MaxLines(owner) + "); 升级铁路建筑可增加";

                // 选线
                var net = RailSystem.Network;
                if (net == null) return "铁路网络未就绪";
                if (!net.HasStation(fromId) || !net.HasStation(toId)) return "端点不在铁路站点(仅城镇/城堡)";
                string estKey = fromId + ">" + toId;
                var line = (_estLine != null && _estKey == estKey) ? _estLine : net.MakeLineByIds(fromId, toId);
                _estLine = null; _estKey = null;
                if (line == null || line.Points == null || line.Points.Count < 2) return "选线失败(地形过于复杂)";

                // 花费(距离×2 金): 先校验余额, 入队成功后再扣费(扣费异常回滚, 保证钱与线一致)
                int cost = (int)(line.Length * 2f);
                var pk = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                bool isPlayer = ReferenceEquals(owner, pk);
                if (isPlayer)
                {
                    if (EconomyWorld.Treasury.Gold < cost) return "国库不足(需 " + cost + " 第纳尔)";
                }
                else
                {
                    if (WarEconomy.GoldOfPublic(owner) < cost) return "国库不足(需 " + cost + " 第纳尔)";
                }

                var rl = new RailLine
                {
                    FromId = fromId,
                    ToId = toId,
                    OwnerId = owner.StringId,
                    StartDay = Politics.Today(),
                    Progress = 0f,
                    Built = false,
                    Broken = false,
                    Length = line.Length,
                    Points = line.Points
                };
                All.Add(rl);
                try
                {
                    if (isPlayer)
                    {
                        EconomyWorld.TreasurySpend(cost);
                        try { Fiscal.AddCourt(cost); } catch { }
                    }
                    else
                    {
                        WarEconomy.SpendPublic(owner, cost);
                    }
                }
                catch (Exception payEx)
                {
                    All.Remove(rl);
                    DLog.Force("铁路: 建线扣费失败, 已回滚: " + payEx.Message);
                    return "扣费失败, 建线已取消";
                }
                float days = TotalDays(rl);
                DLog.Force("铁路: " + owner.Name + " 开建 " + (from.Name != null ? from.Name.ToString() : fromId)
                    + " -> " + (to.Name != null ? to.Name.ToString() : toId)
                    + " 长度 " + (int)line.Length + " 费 " + cost + " 工期 " + (int)days + " 天");
                return "";
            }
            catch (Exception ex) { DLog.Force("铁路建线异常: " + ex.Message); return "建线异常, 见日志"; }
        }

        // 建线预估(UI 确认前显示花费/工期; 结果缓存供 Establish 复用)
        internal static bool Estimate(string fromId, string toId, out int cost, out int days, out string err)
        {
            cost = 0; days = 0; err = "";
            try
            {
                if (string.IsNullOrEmpty(fromId) || string.IsNullOrEmpty(toId)) { err = "参数错误"; return false; }
                var from = FindSettlement(fromId);
                var to = FindSettlement(toId);
                if (from == null || to == null) { err = "端点城市无效"; return false; }
                if (fromId == toId) { err = "起终点相同"; return false; }
                if (!from.IsTown && !from.IsCastle) { err = "起点必须是城镇或城堡"; return false; }
                if (!to.IsTown && !to.IsCastle) { err = "终点必须是城镇或城堡"; return false; }
                if (HasLine(fromId, toId)) { err = "两地之间已有铁路"; return false; }
                bool tech = false;
                try { tech = Research.IsDone("railways"); } catch { }
                if (!tech) { err = "需要先研究「铁路」科技"; return false; }
                var owner = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                if (owner != null && CountOf(owner) >= MaxLines(owner))
                { err = "铁路名额已满(" + CountOf(owner) + "/" + MaxLines(owner) + "); 升级铁路建筑可增加"; return false; }
                var net = RailSystem.Network;
                if (net == null) { try { RailSystem.EnsureNetwork(); } catch { } net = RailSystem.Network; }
                if (net == null) { err = "铁路网络未就绪"; return false; }
                if (!net.HasStation(fromId) || !net.HasStation(toId)) { err = "端点不在铁路站点(仅城镇/城堡)"; return false; }
                string key = fromId + ">" + toId;
                var line = (_estLine != null && _estKey == key) ? _estLine : net.MakeLineByIds(fromId, toId);
                if (line == null || line.Points == null || line.Points.Count < 2) { err = "选线失败(地形过于复杂)"; return false; }
                _estKey = key; _estLine = line;
                cost = (int)(line.Length * 2f);
                days = (int)Math.Max(3f, line.Length / 1000f * 15f);
                return true;
            }
            catch (Exception ex) { err = "预估失败: " + ex.Message; return false; }
        }

        // 选线预览数值: 复用 Estimate 把花费/工期带出(给 UI 显示用)
        internal static bool TryPreviewPoints(string fromId, string toId, out int cost, out int days)
        {
            cost = 0;
            days = 0;
            try
            {
                string err;
                return Estimate(fromId, toId, out cost, out days, out err);
            }
            catch { return false; }
        }

        internal static float TotalDays(RailLine l)
        {
            try { return Math.Max(3f, l.Length / 1000f * 15f); } catch { return 15f; }
        }

        /// <summary>拆除铁路线: 取消该线在途运输 + 返还 50% 建线花费。</summary>
        internal static string Remove(RailLine l)
        {
            try
            {
                if (l == null) return "无效线路";
                // v4.21x: 取消该线所有在途商品运输与军列运输(恢复部队可见)
                int cancelled = 0;
                for (int i = Transports.Count - 1; i >= 0; i--)
                {
                    var t = Transports[i];
                    if (t == null) { Transports.RemoveAt(i); continue; }
                    if ((t.FromId == l.FromId && t.ToId == l.ToId) || (t.FromId == l.ToId && t.ToId == l.FromId))
                    {
                        try { var p = FindParty(t.PartyId); if (p != null) p.IsVisible = true; } catch { }
                        Transports.RemoveAt(i);
                        cancelled++;
                    }
                }
                for (int i = MilTransports.Count - 1; i >= 0; i--)
                {
                    var t = MilTransports[i];
                    if (t == null) { MilTransports.RemoveAt(i); continue; }
                    if ((t.FromId == l.FromId && t.ToId == l.ToId) || (t.FromId == l.ToId && t.ToId == l.FromId))
                    {
                        try { var p = FindParty(t.PartyId); if (p != null) p.IsVisible = true; } catch { }
                        MilTransports.RemoveAt(i);
                        cancelled++;
                    }
                }
                MilUsedToday.Remove(l.FromId); MilUsedToday.Remove(l.ToId);
                MilLastDispatchDay.Remove(l.FromId); MilLastDispatchDay.Remove(l.ToId);
                All.Remove(l);
                // v4.21x: 返还 50% 建线花费
                int refund = RemovalRefund(l);
                if (refund > 0)
                {
                    var k = FindKingdom(l.OwnerId);
                    if (k != null)
                    {
                        if (ReferenceEquals(k, PlayerKingdom()))
                        {
                            try { EconomyWorld.TreasuryAdd(refund); } catch { }
                            try { Fiscal.AddCourt(-refund); } catch { }
                        }
                        else
                        {
                            try { WarEconomy.AddPublic(k, refund); } catch { }
                        }
                    }
                }
                try { RailSystem.RebuildVisual(); } catch { }
                if (cancelled > 0)
                {
                    try { MapSelection.Message("线路已拆除，在途运输已取消"); } catch { }
                }
                DLog.Force("铁路: 拆除 " + l.FromId + " -> " + l.ToId + " 返还 " + refund + " 金, 取消在途 " + cancelled + " 支");
                return "已拆除铁路: " + NameOf(l.FromId) + " ↔ " + NameOf(l.ToId);
            }
            catch { return "拆除失败"; }
        }

        // ================= 军队铁路运输(v4.164) =================
        // 装车公共流程(商品/军列共用): 查重 -> 距离 -> 找线 -> 付款前检查 -> 扣费/入账 -> 隐藏 -> 入队
        // dupMessage: 同一队列内重复的提示; unit: 余额不足提示单位; prePay: 军列运力/冷却等付款前检查(可空)
        // costOf/daysOf: 按线路计算运费/总天数(与各自原口径一致); usedLine/usedCost 供调用方日志复用
        private static string LoadArmyCore(TaleWorlds.CampaignSystem.Party.MobileParty p, Settlement from, string toId,
            List<RailTransport> queue, string dupMessage, string unit,
            Func<Settlement, RailLine, string> prePay, Func<RailLine, int> costOf, Func<RailLine, float> daysOf,
            out RailLine usedLine, out int usedCost)
        {
            usedLine = null; usedCost = 0;
            if (p == null || from == null || string.IsNullOrEmpty(toId)) return "参数错误";
            if (toId == from.StringId) return "起终点相同";
            for (int i = 0; i < queue.Count; i++)
                if (queue[i] != null && queue[i].PartyId == p.StringId) return dupMessage;
            // 部队须在本城附近
            float dx = p.Position.X - from.Position.X;
            float dy = p.Position.Y - from.Position.Y;
            if (dx * dx + dy * dy > 30f * 30f) return "部队不在本城附近";

            // 找线路(运营中且连接两端)
            RailLine line = FindLine(from.StringId, toId);
            if (line == null) return "两地之间没有运营中的铁路";
            if (prePay != null)
            {
                string e = prePay(from, line);
                if (!string.IsNullOrEmpty(e)) return e;
            }

            // 花费(距离×2 金); v4.21x: 运费按线路所有权入账(国有 100% 国库 / 私有 50% 投资池)
            int cost = costOf != null ? costOf(line) : (int)(line.Length * 2f);
            var payer = p.MapFaction as Kingdom;
            if (payer == null) return "该部队没有可付款的势力";
            if (!TryPayKingdom(payer, cost)) return "国库不足(需 " + cost + " " + unit + ")";
            PayLineRevenue(line, cost);

            // 隐藏 + 入队
            try { p.IsVisible = false; } catch { }
            queue.Add(new RailTransport
            {
                PartyId = p.StringId,
                FromId = from.StringId,
                ToId = toId,
                StartDay = Politics.Today(),
                Progress = 0f,
                TotalDays = daysOf != null ? daysOf(line) : Math.Max(1f, line.Length / 1000f * 1.5f)
            });
            usedLine = line; usedCost = cost;
            return "";
        }

        // 校验 -> 扣费(距离×2 金) -> 部队隐藏 -> 加入运输队列
        internal static string LoadArmy(TaleWorlds.CampaignSystem.Party.MobileParty p, Settlement from, string toId)
        {
            try
            {
                RailLine line; int cost;
                string err = LoadArmyCore(p, from, toId, Transports, "该部队已在铁路运输中", "第纳尔", null,
                    l => (int)(l.Length * 2f), l => Math.Max(1f, l.Length / 1000f * 1.5f), out line, out cost);
                if (err != "") return err;
                DLog.Force("铁路运输: " + (p.Name != null ? p.Name.ToString() : p.StringId) + " " + from.StringId + " -> " + toId
                    + " 费 " + cost + " 预计 " + (int)(line.Length / 1000f * 1.5f) + " 天");
                return "";
            }
            catch (Exception ex) { DLog.Force("铁路运输异常: " + ex.Message); return "装车异常, 见日志"; }
        }

        // ================= v4.21x: 军列(独立于商品运输的运力池与冷却) =================
        // 动员/集结天数(V3): 5 + (100 - min(100, 基建)) × 0.15, 取整后限 5~20 天
        internal static int MobilizationDays(string settlementId)
        {
            try
            {
                float infra = Infrastructure.BaseOf(settlementId);
                float d = 5f + (100f - Math.Min(100f, infra)) * 0.15f;
                int v = (int)Math.Round(d);
                if (v < 5) v = 5;
                if (v > 20) v = 20;
                return v;
            }
            catch { return 20; }
        }

        // 军列运力: 50 × ΣTier(运营中线路); 1 运力单位 = 100 兵力, 每日重置
        internal static int MilitaryCapacity(string settlementId)
        {
            int cap = 0;
            try
            {
                var lines = LinesOf(settlementId);
                for (int i = 0; i < lines.Count; i++)
                {
                    var l = lines[i];
                    if (l == null || l.Broken) continue;
                    cap += 50 * ClampTier(l.Tier);
                }
            }
            catch { }
            return cap;
        }

        internal static int MilitaryUsedToday(string settlementId)
        {
            int v;
            return (!string.IsNullOrEmpty(settlementId) && MilUsedToday.TryGetValue(settlementId, out v)) ? v : 0;
        }

        // UI 用军列状态: 今日已用/总运力/剩余冷却天数
        internal static void MilStatusOf(string settlementId, out int usedToday, out int capacity, out int cooldownDays)
        {
            usedToday = 0; capacity = 0; cooldownDays = 0;
            try
            {
                if (string.IsNullOrEmpty(settlementId)) return;
                usedToday = MilitaryUsedToday(settlementId);
                capacity = MilitaryCapacity(settlementId);
                int last;
                if (MilLastDispatchDay.TryGetValue(settlementId, out last))
                {
                    int left = MilCooldownDays - (Politics.Today() - last);
                    cooldownDays = left > 0 ? left : 0;
                }
            }
            catch { }
        }

        // 军列运费(金): 距离×2 + 每百人 1 金(与商品运输同底价口径)
        internal static int MilitaryCost(string fromId, string toId, TaleWorlds.CampaignSystem.Party.MobileParty p)
        {
            try
            {
                var l = FindLine(fromId, toId);
                if (l == null) return 0;
                int men = 0;
                try { men = p != null && p.MemberRoster != null ? p.MemberRoster.TotalManCount : 0; } catch { }
                return (int)(l.Length * 2f) + men / 100;
            }
            catch { return 0; }
        }

        // 军列天数 = 车程(商品运输 1.5 天/千单位, 军列快 25%) + 发车前集结等待(动员天数)
        internal static int MilitaryDays(string fromId, string toId)
        {
            try
            {
                var l = FindLine(fromId, toId);
                if (l == null) return 0;
                int travel = (int)Math.Max(1f, l.Length / 1000f * 1.125f);
                return travel + MobilizationDays(fromId);
            }
            catch { return 0; }
        }

        // 装军列: 走 MilTransports(不占用商品运输队列), 冷却 2 天/城; 返回空串 = 成功
        internal static string LoadArmyMilitary(TaleWorlds.CampaignSystem.Party.MobileParty p, Settlement from, string toId)
        {
            try
            {
                if (p == null || from == null || string.IsNullOrEmpty(toId)) return "参数错误";
                if (toId == from.StringId) return "起终点相同";
                for (int i = 0; i < Transports.Count; i++)
                    if (Transports[i] != null && Transports[i].PartyId == p.StringId) return "该部队已在商品运输中(同一部队只能选其一)";

                int men = 0;
                try { men = p.MemberRoster != null ? p.MemberRoster.TotalManCount : 0; } catch { }
                int load = (men + 99) / 100;                 // 每 100 兵力 = 1 运力单位
                int cap = MilitaryCapacity(from.StringId);
                int used = MilitaryUsedToday(from.StringId);
                int mobDays = MobilizationDays(from.StringId);
                int today = Politics.Today();
                RailLine line; int cost;
                string err = LoadArmyCore(p, from, toId, MilTransports, "该部队已在军列运输中", "金",
                    (f, l) =>
                    {
                        if (cap <= 0) return "本城无军列运力(需运营中的铁路)";
                        if (load > cap - used) return "军列运力不足(需 " + load + ", 今日余 " + Math.Max(0, cap - used) + "/" + cap + ")";
                        int last;
                        if (MilLastDispatchDay.TryGetValue(f.StringId, out last) && today - last < MilCooldownDays)
                            return "军列调度冷却中(还需 " + (MilCooldownDays - (today - last)) + " 天)";
                        return "";
                    },
                    l => (int)(l.Length * 2f) + men / 100,
                    l => Math.Max(1f, (int)Math.Max(1f, l.Length / 1000f * 1.125f) + mobDays),
                    out line, out cost);
                if (err != "") return err;

                MilUsedToday[from.StringId] = used + load;
                MilLastDispatchDay[from.StringId] = today;
                DLog.Force("军列: " + (p.Name != null ? p.Name.ToString() : p.StringId) + " " + from.StringId + " -> " + toId
                    + " 兵力 " + men + " 运力 " + load + "/" + cap + " 费 " + cost
                    + " 集结 " + mobDays + " 天 总 " + (int)Math.Max(1f, (int)Math.Max(1f, line.Length / 1000f * 1.125f) + mobDays) + " 天(含集结)");
                return "";
            }
            catch (Exception ex) { DLog.Force("军列装车异常: " + ex.Message); return "军列装车异常, 见日志"; }
        }

        // 运输推进(日结内调用): 商品队列 + 军列队列分别推进
        private static void TickTransports()
        {
            TickTransportList(Transports, "铁路", "铁路运输");
            TickTransportList(MilTransports, "军列", "军列");
        }

        private static void TickTransportList(List<RailTransport> list, string msgTag, string logTag)
        {
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var t = list[i];
                if (t == null) { list.RemoveAt(i); continue; }
                t.Progress += 1f / Math.Max(0.5f, t.TotalDays);
                if (t.Progress < 1f) continue;
                // 到达
                list.RemoveAt(i);
                var p = FindParty(t.PartyId);
                var to = FindSettlement(t.ToId);
                if (p == null) continue;
                try
                {
                    p.IsVisible = true;
                    if (to != null)
                    {
                        try { p.Position = new CampaignVec2(to.Position.ToVec2(), true); } catch { }
                        bool enemy = false;
                        try { enemy = p.MapFaction != null && to.MapFaction != null && p.MapFaction.IsAtWarWith(to.MapFaction); } catch { }
                        if (enemy)
                        {
                            try { p.SetMoveBesiegeSettlement(to, TaleWorlds.CampaignSystem.Party.MobileParty.NavigationType.Default); } catch { }
                            var pk = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                            if (pk != null && ReferenceEquals(p.MapFaction, pk))
                                MapSelection.Message(msgTag + "运兵: " + (p.Name != null ? p.Name.ToString() : "部队") + " 已抵达 " + NameOf(t.ToId) + ", 开始围城");
                            DLog.Force(logTag + ": 到达敌城 " + t.ToId + " -> 围城");
                        }
                        else
                        {
                            try { p.SetMoveGoToSettlement(to, TaleWorlds.CampaignSystem.Party.MobileParty.NavigationType.Default, false); } catch { }
                            DLog.Force(logTag + ": 到达 " + t.ToId);
                        }
                    }
                }
                catch (Exception ex) { DLog.Force("铁路运输到达处理异常: " + ex.Message); }
            }
        }

        internal static TaleWorlds.CampaignSystem.Party.MobileParty FindParty(string id)
        {
            try
            {
                foreach (var p in TaleWorlds.CampaignSystem.Party.MobileParty.All)
                    if (p != null && p.StringId == id) return p;
            }
            catch { }
            return null;
        }

        // ================= AI 运兵(v4.166) =================
        // 交战国: 把后方城市附近的部队经铁路送到最靠近前线的自己城市(铁路调度)
        internal static void AiTransportMonthly(int day)
        {
            try
            {
                try { AiLegionRailMonthly(day); } catch { }   // v4.21x: 国防军军列投送 + 铁路运输开关(和平期也要能关)
                if (All.Count == 0) return;
                var pk = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                foreach (var k in Kingdom.All)
                {
                    if (k == null || k.IsEliminated || ReferenceEquals(k, pk)) continue;
                    try
                    {
                        if (Transports.Count > 20) break;
                        // 交战对手
                        bool atWar = false;
                        try
                        {
                            foreach (var x in Kingdom.All)
                            {
                                if (x == null || x.IsEliminated || ReferenceEquals(x, k)) continue;
                                if (k.IsAtWarWith(x)) { atWar = true; break; }
                            }
                        }
                        catch { }
                        if (!atWar) continue;
                        if (MBRandom.RandomFloat > 0.5f) continue;

                        // 我方城市
                        var myCities = new List<Settlement>();
                        foreach (var s in k.Settlements)
                            if (s != null && (s.IsTown || s.IsCastle)) myCities.Add(s);
                        if (myCities.Count < 2) continue;

                        // 最近敌城(以我方首都为参照)
                        var capital = k.RulingClan != null && k.RulingClan.Leader != null ? k.RulingClan.Leader.HomeSettlement : null;
                        var refPos = capital != null ? capital.Position : myCities[0].Position;
                        Settlement target = null;
                        float td = float.MaxValue;
                        try
                        {
                            foreach (var x in Kingdom.All)
                            {
                                if (x == null || x.IsEliminated || ReferenceEquals(x, k)) continue;
                                if (!k.IsAtWarWith(x)) continue;
                                foreach (var s in x.Settlements)
                                {
                                    if (s == null || !s.IsTown) continue;
                                    float d = s.Position.Distance(refPos);
                                    if (d < td) { td = d; target = s; }
                                }
                            }
                        }
                        catch { }
                        if (target == null) continue;

                        // 后方(离目标最远) -> 前线(离目标最近)
                        Settlement rear = null, front = null;
                        float rd = -1f, fd = float.MaxValue;
                        for (int i = 0; i < myCities.Count; i++)
                        {
                            float d = myCities[i].Position.Distance(target.Position);
                            if (d > rd) { rd = d; rear = myCities[i]; }
                            if (d < fd) { fd = d; front = myCities[i]; }
                        }
                        if (rear == null || front == null || rear == front) continue;
                        if (!HasLine(rear.StringId, front.StringId)) continue;

                        // 后方城市附近的部队
                        var p = FindPartyNear(rear, k, 40f);
                        if (p == null) continue;
                        string err = LoadArmy(p, rear, front.StringId);
                        if (err == "")
                            DLog.Force("AI 铁路运兵: " + k.Name + " " + rear.StringId + " -> " + front.StringId + " (前线调度)");
                    }
                    catch { }
                }
            }
            catch (Exception ex) { DLog.Force("AI 铁路运兵异常: " + ex.Message); }
        }

        private static TaleWorlds.CampaignSystem.Party.MobileParty FindPartyNear(Settlement s, Kingdom k, float radius)
        {
            try
            {
                TaleWorlds.CampaignSystem.Party.MobileParty best = null;
                float bestStr = 0f;
                foreach (var p in TaleWorlds.CampaignSystem.Party.MobileParty.All)
                {
                    if (p == null || !p.IsActive) continue;
                    if (p.IsMainParty || p.IsGarrison || p.IsMilitia || p.IsCaravan) continue;
                    if (p.LeaderHero == null) continue;
                    if (!ReferenceEquals(p.MapFaction, k)) continue;
                    float dx = p.Position.X - s.Position.X;
                    float dy = p.Position.Y - s.Position.Y;
                    if (dx * dx + dy * dy > radius * radius) continue;
                    float str = p.MemberRoster != null ? p.MemberRoster.TotalManCount : 0f;
                    if (str > bestStr) { bestStr = str; best = p; }
                }
                return best;
            }
            catch { return null; }
        }

        // ================= v4.21x: AI 军列投送(国防军) =================
        // 玩家王国(国家意志)战时: 把 DefArmy 军团从后方城经军列投送到前线城
        //   后方 = 军团附近(≤30)且有运营中线路直达前线的本国城; 前线 = 本国通铁路城中离 WarPlan 主目标(或最近敌城)最近者
        //   受 MilitaryCapacity/发车冷却限制, 失败跳过; 和平期不用; 投送后开 ArmyDoctrine 铁路运输
        private static void AiLegionRailMonthly(int day)
        {
            try
            {
                if (DefArmy.Legions.Count == 0) return;
                var pk = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                if (pk == null || pk.IsEliminated) return;

                // 主敌(实力最强交战国) -> 战区目标(WarPlan 主目标, 退化用最近敌城)
                Kingdom mainEnemy = null;
                float bestStr = 0f;
                try
                {
                    foreach (var x in Kingdom.All)
                    {
                        if (x == null || x.IsEliminated || ReferenceEquals(x, pk)) continue;
                        if (!pk.IsAtWarWith(x)) continue;
                        float s = WarPlans.StrengthOf(x);
                        if (s > bestStr) { bestStr = s; mainEnemy = x; }
                    }
                }
                catch { }
                bool atWar = mainEnemy != null;
                Settlement target = null;
                if (atWar)
                {
                    try
                    {
                        var plan = WarPlans.GetOrCreate(pk, mainEnemy, day);
                        if (plan != null) target = WarPlans.Find(plan.MainTargetId);
                    }
                    catch { }
                    if (target == null) target = NearestEnemyTown(pk, mainEnemy);
                }
                Settlement front = target != null ? FrontRailCity(pk, target) : null;

                float treasury = 0f;
                try { treasury = EconomyWorld.Treasury.Gold; } catch { }

                // 铁路运输开关: 战时远距行军/投送后开, 和平或引擎钱不足关(DailyRailTransport 会自动扣费停用)
                for (int i = 0; i < DefArmy.Legions.Count; i++)
                {
                    var p = DefArmy.LegionParty(DefArmy.Legions[i]);
                    if (p == null || !p.IsActive) continue;
                    bool on = false;
                    try { on = ArmyDoctrine.IsRailTransport(p); } catch { }
                    if (!atWar || treasury < 500f)
                    {
                        if (on) ArmyDoctrine.SetRailTransport(p, false);
                        continue;
                    }
                    float d = front != null ? p.Position.Distance(front.Position) : float.MaxValue;
                    if (!on && front != null && d > 150f && treasury >= 3000f) ArmyDoctrine.SetRailTransport(p, true);
                }

                if (!atWar || target == null || front == null) return;

                // 军列投送: 每军团每月最多一次(运力/冷却由 LoadArmyMilitary 校验, 失败跳过)
                for (int i = 0; i < DefArmy.Legions.Count; i++)
                {
                    try
                    {
                        var lg = DefArmy.Legions[i];
                        if (lg == null) continue;
                        var p = DefArmy.LegionParty(lg);
                        if (p == null || !p.IsActive || InRailQueue(p.StringId)) continue;
                        int men = DefArmy.LegionMen(lg);
                        if (men < 100) continue;   // 至少 1 运力单位
                        int last;
                        if (_aiLegionDispatchDay.TryGetValue(p.StringId, out last) && day - last < 28) continue;

                        Settlement rear = LegionRearCity(pk, p, front);
                        if (rear == null || rear == front) continue;
                        int load = (men + 99) / 100;
                        if (MilitaryCapacity(rear.StringId) - MilitaryUsedToday(rear.StringId) < load) continue;
                        int lastGo;
                        if (MilLastDispatchDay.TryGetValue(rear.StringId, out lastGo) && day - lastGo < MilCooldownDays) continue;

                        string err = LoadArmyMilitary(p, rear, front.StringId);
                        if (err != "") continue;   // 运力/冷却/付款失败 -> 跳过
                        _aiLegionDispatchDay[p.StringId] = day;
                        DLog.Force("AI 军列: " + pk.Name + " 军团 " + (p.Name != null ? p.Name.ToString() : p.StringId)
                            + " " + rear.StringId + " -> " + front.StringId + " (兵力 " + men + ", 目标 "
                            + (target.Name != null ? target.Name.ToString() : target.StringId) + ")");
                        try { ArmyDoctrine.SetRailTransport(p, true); } catch { }   // 投送后开铁路运输
                    }
                    catch { }
                }
            }
            catch (Exception ex) { DLog.Force("AI 军列决策异常: " + ex.Message); }
        }

        private static bool InRailQueue(string partyId)
        {
            try
            {
                for (int i = 0; i < Transports.Count; i++)
                    if (Transports[i] != null && Transports[i].PartyId == partyId) return true;
                for (int i = 0; i < MilTransports.Count; i++)
                    if (MilTransports[i] != null && MilTransports[i].PartyId == partyId) return true;
            }
            catch { }
            return false;
        }

        // 前线城: 本国运营中铁路城, 离战区目标最近
        private static Settlement FrontRailCity(Kingdom k, Settlement target)
        {
            try
            {
                Settlement best = null;
                float bd = float.MaxValue;
                foreach (var s in k.Settlements)
                {
                    if (s == null || (!s.IsTown && !s.IsCastle)) continue;
                    if (LinesOf(s.StringId).Count == 0) continue;   // 通铁路(运营中)
                    float d = s.Position.Distance(target.Position);
                    if (d < bd) { bd = d; best = s; }
                }
                return best;
            }
            catch { return null; }
        }

        // 后方城: 军团附近(≤30)且有运营中线路直达前线的本国城
        private static Settlement LegionRearCity(Kingdom k, TaleWorlds.CampaignSystem.Party.MobileParty p, Settlement front)
        {
            try
            {
                Settlement best = null;
                float bd = 30f;
                foreach (var s in k.Settlements)
                {
                    if (s == null || (!s.IsTown && !s.IsCastle) || s == front) continue;
                    if (FindLine(s.StringId, front.StringId) == null) continue;
                    float dx = p.Position.X - s.Position.X, dy = p.Position.Y - s.Position.Y;
                    float d = (float)Math.Sqrt(dx * dx + dy * dy);
                    if (d < bd) { bd = d; best = s; }
                }
                return best;
            }
            catch { return null; }
        }

        private static Settlement NearestEnemyTown(Kingdom k, Kingdom enemy)
        {
            try
            {
                if (enemy == null) return null;
                Settlement refS = null;
                try { if (k.RulingClan != null && k.RulingClan.Leader != null) refS = k.RulingClan.Leader.HomeSettlement; } catch { }
                if (refS == null)
                {
                    foreach (var s in k.Settlements)
                        if (s != null && (s.IsTown || s.IsCastle)) { refS = s; break; }
                }
                if (refS == null) return null;
                Settlement best = null;
                float bd = float.MaxValue;
                foreach (var s in enemy.Settlements)
                {
                    if (s == null || !s.IsTown) continue;
                    float d = s.Position.Distance(refS.Position);
                    if (d < bd) { bd = d; best = s; }
                }
                return best;
            }
            catch { return null; }
        }

        // ================= AI 建线/升级/所有权(v4.165 / v4.21x 升级 / v4.22x 网络规划) =================
        // 月度流水线(每国): 欠费止损 -> 升级 -> 所有权 -> 建线, 每步成功即止(统一月志每国至多一条)
        // 建线评分: 城市价值(人口/繁荣/建筑/准入缺口/商品缺口) + "接入网络"优先(一端通网/一端新城)
        //   战时/军事目标: 优先"后方 -> 前线"干线并优先升级干线; 预算受 AiBrain.BudgetFor("rail") 与 ReserveGold 限制
        internal static void AiMonthly(int day)
        {
            try
            {
                bool tech = false;
                try { tech = Research.IsDone("railways"); } catch { }
                if (tech)
                {
                    var pk = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                    foreach (var k in Kingdom.All)
                    {
                        if (k == null || k.IsEliminated || ReferenceEquals(k, pk)) continue;
                        try { AiRailMonthOf(k, day); }
                        catch (Exception ex) { DLog.Force("AI 铁路决策异常(" + k.Name + "): " + ex.Message); }
                    }
                }
            }
            catch (Exception ex) { DLog.Force("AI 铁路月结异常: " + ex.Message); }
            finally { try { AiEconomyLog.Flush(); } catch { } }   // v4.22x: 铁路是月结最后一站 -> 统一输出每国一条 AI 经济日志
        }

        private static void AiRailMonthOf(Kingdom k, int day)
        {
            float gold = 0f;
            try { gold = WarEconomy.GoldOfPublic(k); } catch { }
            bool atWar = AtWarOf(k, out _);
            AiDirector.Goal goal = AiBrain.GoalOf(k);
            if (AiStopLoss(k, gold, atWar, goal)) return;         // ① 欠费停运止血
            string up = AiUpgradeOne(k, gold, atWar, goal);       // ② 升级: 战时优先通往战区方向的干线
            if (up != "") { AiEconomyLog.Add(k, "铁路=升级 " + up); return; }
            if (AiOwnershipOne(k, gold, atWar, goal)) return;     // ③ 战时/军事国有化, 生存/经济私有化
            string line = AiBuildOne(k, gold, atWar, goal, AiBrain.WarTargetId(k));   // ④ 建线(接入网络优先)
            if (line != "") AiEconomyLog.Add(k, "建线=" + line);
        }

        private static bool AtWarOf(Kingdom k, out Kingdom mainEnemy)
        {
            mainEnemy = null;
            float best = 0f;
            try
            {
                foreach (var x in Kingdom.All)
                {
                    if (x == null || x.IsEliminated || ReferenceEquals(x, k)) continue;
                    if (!k.IsAtWarWith(x)) continue;
                    float s = WarPlans.StrengthOf(x);
                    if (s > best) { best = s; mainEnemy = x; }
                }
            }
            catch { }
            return mainEnemy != null;
        }

        // 全国线路 30 天维护费(AI 决策留维护余量用)
        private static int Upkeep30Of(Kingdom k)
        {
            int v = 0;
            try
            {
                for (int i = 0; i < All.Count; i++)
                {
                    var l = All[i];
                    if (l == null || l.OwnerId != k.StringId) continue;
                    v += MaintenanceOf(l);
                }
            }
            catch { }
            return v * 30;
        }

        // 欠费止损: 线路因欠维护费停运 -> 国有先私有化(维护转投资池); 仍紧张则拆日净最低的线止血
        // v4.22x: 战时/军事目标保护通往战区方向的干线(优先拆其它)
        private static bool AiStopLoss(Kingdom k, float gold, bool atWar, AiDirector.Goal goal)
        {
            RailLine broke = null;
            for (int i = 0; i < All.Count; i++)
            {
                var l = All[i];
                if (l == null || l.OwnerId != k.StringId || !l.Built || !l.Broken) continue;
                if (IsBroken(l)) continue;   // 端点被敌占/交战 -> 非欠费停运
                broke = l;
                break;
            }
            if (broke == null) return false;

            int upkeep30 = Upkeep30Of(k);
            bool canRecover = gold >= upkeep30 + 3000f;
            if (!IsStateOwned(broke))
            {
                try { canRecover = InvestmentPool.Of(k.StringId) >= upkeep30; } catch { canRecover = false; }
            }
            if (canRecover) return false;   // 已缓过来 -> 留给日结自动复运

            if (IsStateOwned(broke))
            {
                SetOwnership(broke, false);
                AiEconomyLog.Add(k, "铁路=止血私有化 " + NameOf(broke.FromId) + "→" + NameOf(broke.ToId));
                return true;
            }

            bool protectFront = atWar || goal == AiDirector.Goal.Military;
            Settlement front = protectFront ? WarTargetSettlement(k, AiBrain.WarTargetId(k)) : null;
            RailLine worst = null, worstProt = null;
            int worstNet = int.MaxValue, worstProtNet = int.MaxValue;
            for (int i = 0; i < All.Count; i++)
            {
                var l = All[i];
                if (l == null || l.OwnerId != k.StringId || !l.Built) continue;
                int inc, up;
                int net = DailyNetOf(l, out inc, out up);
                if (l == broke) net -= 1;   // 平手优先拆停运线
                bool frontLine = false;
                if (front != null)
                {
                    var a = FindSettlement(l.FromId);
                    var b = FindSettlement(l.ToId);
                    float df = float.MaxValue;
                    if (a != null) df = Math.Min(df, a.Position.Distance(front.Position));
                    if (b != null) df = Math.Min(df, b.Position.Distance(front.Position));
                    frontLine = df < 300f;
                }
                if (frontLine)
                {
                    if (worstProt == null || net < worstProtNet) { worstProt = l; worstProtNet = net; }
                }
                else if (worst == null || net < worstNet) { worst = l; worstNet = net; }
            }
            if (worst == null) { worst = worstProt; worstNet = worstProtNet; }   // 全是干线 -> 只能拆干线
            if (worst == null) return false;
            if (worst != broke && worstNet >= 0) worst = broke;   // 都不亏只拆停运线
            int refund = RemovalRefund(worst);
            Remove(worst);
            AiEconomyLog.Add(k, "铁路=止血拆线 " + NameOf(worst.FromId) + "→" + NameOf(worst.ToId) + "(返还 " + refund + ")");
            return true;
        }

        // 升级: 战时/军事目标 -> 优先升级通往战区方向的干线(次看日净收入), 和平期按日净收入最高(回本快);
        //   预算受 AiBrain.BudgetFor("rail") 与 ReserveGold 限制; 返回 "城→城(升T级)" 或空串
        private static string AiUpgradeOne(Kingdom k, float gold, bool atWar, AiDirector.Goal goal)
        {
            bool military = atWar || goal == AiDirector.Goal.Military;
            float reserve = AiBrain.ReserveGold(k);
            float budget = AiBrain.BudgetFor(k, "rail", gold);
            if (budget <= 0f) return "";
            Settlement front = military ? WarTargetSettlement(k, AiBrain.WarTargetId(k)) : null;
            RailLine best = null;
            float bestSc = float.MinValue;
            for (int i = 0; i < All.Count; i++)
            {
                var l = All[i];
                if (l == null || l.OwnerId != k.StringId) continue;
                if (!l.Built || l.Broken || l.Tier >= MaxTier) continue;
                int inc, up;
                int net = DailyNetOf(l, out inc, out up);
                float sc = net;
                if (military)
                {
                    float df = float.MaxValue;
                    if (front != null)
                    {
                        var a = FindSettlement(l.FromId);
                        var b = FindSettlement(l.ToId);
                        if (a != null) df = Math.Min(df, a.Position.Distance(front.Position));
                        if (b != null) df = Math.Min(df, b.Position.Distance(front.Position));
                    }
                    else
                    {
                        df = Math.Min(DistanceToEnemy(k, FindSettlement(l.FromId)), DistanceToEnemy(k, FindSettlement(l.ToId)));
                    }
                    if (df < float.MaxValue) sc *= 1f + 250f / (250f + df);   // 通往前线的干线优先
                }
                if (best == null || sc > bestSc || (sc == bestSc && l.Tier < best.Tier)) { best = l; bestSc = sc; }
            }
            if (best == null) return "";
            int cost = UpgradeGoldCost(best);
            if (cost <= 0) return "";
            if (gold - cost < reserve) return "";          // 不碰储备金
            if (cost > budget) return "";                  // 铁路预算上限
            if (!military && gold < cost + Upkeep30Of(k) + 12000f) return "";   // 和平期留厚余量
            if (MBRandom.RandomFloat > (military ? 0.8f : 0.5f)) return "";
            string err = UpgradeLine(best);
            if (err != "") return "";
            return NameOf(best.FromId) + "→" + NameOf(best.ToId) + "(升 T" + best.Tier + ")";
        }

        // 所有权(按 Goal): 战时+军事 -> 国有化保运输(收入 100% 国库); 生存/经济 -> 私有化省国库(维护转投资池);
        //   私有化前确认投资池付得起 30 天维护, 避免刚私有化就欠费停运
        private static bool AiOwnershipOne(Kingdom k, float gold, bool atWar, AiDirector.Goal goal)
        {
            bool wantState;
            if (goal == AiDirector.Goal.Military && atWar)
            {
                if (gold >= Upkeep30Of(k) + 3000f) wantState = true;
                else return false;   // 战时钱不够: 保持现状(欠费由止损处理)
            }
            else if (goal == AiDirector.Goal.Survival || goal == AiDirector.Goal.Economy)
                wantState = false;
            else if (gold < 3000f) wantState = false;
            else if ((atWar || gold > 30000f) && gold >= Upkeep30Of(k) + 3000f) wantState = true;
            else return false;

            RailLine l = null;
            for (int i = 0; i < All.Count; i++)
            {
                var c = All[i];
                if (c == null || c.OwnerId != k.StringId || !c.Built) continue;
                if (IsStateOwned(c) == wantState) continue;
                if (l == null || c.Tier > l.Tier) l = c;   // 优先调整高等级线
            }
            if (l == null) return false;
            if (!wantState)
            {
                float pool = 0f;
                try { pool = InvestmentPool.Of(k.StringId); } catch { }
                if (pool < Upkeep30Of(k)) return false;   // 池子付不起 -> 保持现状
            }
            if (MBRandom.RandomFloat > 0.4f) return false;
            SetOwnership(l, wantState);
            AiEconomyLog.Add(k, "铁路=" + (wantState ? "国有化 " : "私有化 ")
                + NameOf(l.FromId) + "→" + NameOf(l.ToId));
            return true;
        }

        // 建线(网络规划): 优先把"未接入网络的高价值城"接入现有网络(一端通网/一端新城);
        //   全通但多子网时合并子网; 战时/军事目标优先"后方 -> 前线"干线;
        //   预算受 AiBrain.BudgetFor("rail") 与 ReserveGold 限制, 名额受 MaxLines 限制; 返回 "城A→城B(费N)" 或空串
        private static string AiBuildOne(Kingdom k, float gold, bool atWar, AiDirector.Goal goal, string warTargetId)
        {
            if (CountOf(k) >= MaxLines(k)) return "";                 // 名额上限
            float reserve = AiBrain.ReserveGold(k);
            if (gold - reserve < Upkeep30Of(k) + 3000f) return "";    // 留出维护费与储备金
            float budget = AiBrain.BudgetFor(k, "rail", gold);
            if (budget <= 0f) return "";
            float cap = Math.Min(budget, gold - reserve);
            if (cap <= 0f) return "";
            bool military = atWar || goal == AiDirector.Goal.Military || AiBrain.WantsWar(k);
            if (MBRandom.RandomFloat > (military ? 0.8f : 0.55f)) return "";

            var cities = new List<Settlement>();
            foreach (var s in k.Settlements)
                if (s != null && (s.IsTown || s.IsCastle)) cities.Add(s);
            if (cities.Count < 2) return "";

            // 连通分量(仅运营中线路, 端点均属本国)
            int n = cities.Count;
            var idx = new Dictionary<string, int>();
            for (int i = 0; i < n; i++) idx[cities[i].StringId] = i;
            var parent = new int[n];
            for (int i = 0; i < n; i++) parent[i] = i;
            for (int i = 0; i < All.Count; i++)
            {
                var l = All[i];
                if (l == null || !l.Built || l.Broken || l.OwnerId != k.StringId) continue;
                int x, y;
                if (idx.TryGetValue(l.FromId, out x) && idx.TryGetValue(l.ToId, out y)) UfUnion(parent, x, y);
            }
            var compSize = new Dictionary<int, int>();
            for (int i = 0; i < n; i++)
            {
                int r = UfFind(parent, i);
                int c; compSize.TryGetValue(r, out c);
                compSize[r] = c + 1;
            }
            bool allConnected = compSize.Count == 1;
            bool anyNetwork = false;
            foreach (var kv in compSize) if (kv.Value > 1) { anyNetwork = true; break; }
            int unconnected = 0;
            for (int i = 0; i < n; i++) if (compSize[UfFind(parent, i)] <= 1) unconnected++;

            // 城市价值(人口/繁荣/建筑规模 × 准入缺口 × 商品缺口 × 运力缺口; 每城一次)
            var val = new Dictionary<string, float>();
            for (int i = 0; i < n; i++) val[cities[i].StringId] = RailCityValue2(cities[i].StringId);

            // 战区前线参照(战时/军事姿态: 后方 -> 前线 干线优先)
            Settlement front = null;
            if (military) front = WarTargetSettlement(k, warTargetId);

            var cands = new List<RailCandidate>();
            for (int i = 0; i < n; i++)
            {
                for (int j = i + 1; j < n; j++)
                {
                    var a = cities[i];
                    var b = cities[j];
                    if (a == null || b == null) continue;
                    if (HasLine(a.StringId, b.StringId)) continue;   // 避开已有/在建线路
                    int ra = UfFind(parent, i), rb = UfFind(parent, j);
                    bool aNet = compSize[ra] > 1, bNet = compSize[rb] > 1;
                    if (ra == rb && allConnected) continue;          // 单一全通网络 -> 无新线价值
                    if (anyNetwork)
                    {
                        if (ra == rb) continue;                      // 同一子网内互连无意义
                        if (unconnected > 0 && !(aNet ^ bNet)) continue;   // 有未接入城: 必须一端已通网(另一端新城)
                    }
                    float d = a.Position.Distance(b.Position);
                    float score = (val[a.StringId] + val[b.StringId]) / (d + 80f);
                    if (anyNetwork && (aNet ^ bNet)) score *= 1.35f; // 新城接入网络优先
                    if (front != null)
                    {
                        float da = a.Position.Distance(front.Position), db = b.Position.Distance(front.Position);
                        float df = Math.Min(da, db), dr = Math.Max(da, db);
                        score *= 1f + 250f / (250f + df);            // 靠近前线方向
                        if (df < 200f && dr > 400f) score *= 1.3f;   // 后方 -> 前线 干线
                    }
                    cands.Add(new RailCandidate { A = a, B = b, Score = score });
                }
            }
            if (cands.Count == 0) return "";
            cands.Sort(delegate (RailCandidate x, RailCandidate y) { return y.Score.CompareTo(x.Score); });

            var net = RailSystem.Network;
            if (net == null) { try { RailSystem.EnsureNetwork(); } catch { } net = RailSystem.Network; }
            if (net == null) return "";

            // 只对得分最高的前 3 对做选线(地形寻路开销大, 月结也只算 3 次)
            int tried = 0;
            for (int i = 0; i < cands.Count && tried < 3; i++)
            {
                var c = cands[i];
                if (!net.HasStation(c.A.StringId) || !net.HasStation(c.B.StringId)) continue;
                RailNetwork.Line ln = null;
                try { ln = net.MakeLineByIds(c.A.StringId, c.B.StringId); } catch { }
                tried++;
                if (ln == null || ln.Points == null || ln.Points.Count < 2) continue;
                int cost = (int)(ln.Length * 2f);
                if (cost > cap) continue;
                if (gold - cost < reserve + Upkeep30Of(k)) continue;
                _estKey = c.A.StringId + ">" + c.B.StringId;
                _estLine = ln;   // 供 Establish 复用, 避免重复选线
                string err = Establish(k, c.A.StringId, c.B.StringId);
                if (err != "")
                {
                    _estKey = null; _estLine = null;
                    continue;
                }
                return NameOf(c.A.StringId) + "→" + NameOf(c.B.StringId) + "(费 " + cost + (atWar ? ", 前线" : "") + ")";
            }
            return "";
        }

        private class RailCandidate
        {
            internal Settlement A;
            internal Settlement B;
            internal float Score;
        }

        private static int UfFind(int[] parent, int x)
        {
            while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; }
            return x;
        }

        private static void UfUnion(int[] parent, int a, int b)
        {
            a = UfFind(parent, a);
            b = UfFind(parent, b);
            if (a != b) parent[b] = a;
        }

        // 战区目标城: AiDirector.WarTargetId(敌国 id, 兼容定居点 id) -> 最近敌城, 退化用最强交战国的最近城
        private static Settlement WarTargetSettlement(Kingdom k, string warTargetId)
        {
            try
            {
                if (!string.IsNullOrEmpty(warTargetId))
                {
                    var s = FindSettlement(warTargetId);
                    if (s != null) return s;                          // 兼容直接给定居点 id
                    var ek = FindKingdom(warTargetId);
                    if (ek != null) return NearestEnemyTown(k, ek);   // AiDirector 口径: 敌国 StringId
                }
                Kingdom enemy;
                if (AtWarOf(k, out enemy) && enemy != null) return NearestEnemyTown(k, enemy);
            }
            catch { }
            return null;
        }

        // 城市铁路价值: 原口径(人口/繁荣/建筑) × 市场准入缺口 × 商品缺口 × 运力缺口
        private static float RailCityValue2(string sid)
        {
            float v = RailCityValue(sid);
            try
            {
                int cap = 0, load = 0;
                float access = MarketAccessOf(sid, out cap, out load);
                float need = 1f - access;
                if (need < 0f) need = 0f;
                if (need > 0.9f) need = 0.9f;
                v *= 1f + need * 1.6f;
                v *= 1f + Math.Min(1.2f, MarketGapOf(sid));
                int prod, cons;
                TransportOf(sid, out prod, out cons);
                if (cons > prod) v *= 1f + Math.Min(0.8f, (cons - prod) / Math.Max(1f, cons));
            }
            catch { }
            return v;
        }

        // 本城市场缺口: 各商品 日耗/日产 > 1 的平均超出幅度(0~3)
        private static float MarketGapOf(string sid)
        {
            try
            {
                var m = EconomyWorld.FindMarket(sid);
                if (m == null || m.Entries == null) return 0f;
                float gap = 0f;
                int n = 0;
                foreach (var kv in m.Entries)
                {
                    var e = kv.Value;
                    if (e == null) continue;
                    float g = e.DailyProduction > 0.01f
                        ? e.DailyConsumption / e.DailyProduction
                        : (e.DailyConsumption > 0.01f ? 2f : 1f);
                    if (g > 1f) { gap += Math.Min(3f, g - 1f); n++; }
                }
                return n > 0 ? gap / n : 0f;
            }
            catch { return 0f; }
        }

        // 城市建线价值: 城镇/城堡 + 繁荣 + 人口 + 铁路建筑等级
        private static float RailCityValue(string sid)
        {
            float v = 1f;
            try
            {
                var s = FindSettlement(sid);
                if (s != null)
                {
                    v += s.IsCastle ? 1f : 2f;
                    if (s.Town != null) v += Math.Min(3f, s.Town.Prosperity / 150f);
                }
                var pops = Pops.Of(sid);
                if (pops != null)
                {
                    float total = 0f;
                    for (int i = 0; i < pops.Count; i++)
                        if (pops[i] != null) total += pops[i].Size;
                    v += Math.Min(2.5f, total / 8000f);
                }
                v += 0.8f * RailwayBuildingLevelOf(sid);
            }
            catch { }
            return v;
        }

        private static float DistanceToEnemy(Kingdom k, Settlement s)
        {
            float best = float.MaxValue;
            try
            {
                foreach (var x in Kingdom.All)
                {
                    if (x == null || x.IsEliminated || ReferenceEquals(x, k)) continue;
                    if (!k.IsAtWarWith(x)) continue;
                    foreach (var e in x.Settlements)
                    {
                        if (e == null) continue;
                        float d = s.Position.Distance(e.Position);
                        if (d < best) best = d;
                    }
                }
            }
            catch { }
            return best;
        }

        // ================= 日结 =================
        internal static void Daily(int day)
        {
            try
            {
                if (day == _lastDay) return;
                _lastDay = day;
                if (_lastMilResetDay != day) { _lastMilResetDay = day; MilUsedToday.Clear(); }   // v4.21x: 军列运力每日重置
                _dayIncome.Clear();   // v4.21x: 当日运费收入累计清零(新的一天重新累计)
                bool changed = false;

                for (int i = All.Count - 1; i >= 0; i--)
                {
                    var l = All[i];
                    if (l == null) continue;
                    bool wasBroken = l.Broken;   // v4.21x: 本日处理前的状态(欠费停运提示去重)
                    if (!l.Built)
                    {
                        l.Progress += 1f / TotalDays(l);
                        if (l.Progress >= 1f)
                        {
                            l.Progress = 1f;
                            l.Built = true;
                            changed = true;
                            DLog.Force("铁路: 建成 " + l.FromId + " -> " + l.ToId + "(长度 " + (int)l.Length + ")");
                            try
                            {
                                var k = FindKingdom(l.OwnerId);
                                var pk = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                                if (k != null && ReferenceEquals(k, pk))
                                    MapSelection.Message("铁路建成: " + NameOf(l.FromId) + " ↔ " + NameOf(l.ToId));
                            }
                            catch { }
                        }
                    }
                    // 状态检查(每 7 天; v4.21x: 修正恒真条件)
                    if (l.Built && day - _nextVisualCheck >= 7)
                    {
                        bool broken = IsBroken(l);
                        if (broken != l.Broken)
                        {
                            l.Broken = broken;
                            changed = true;
                            if (broken)
                            {
                                try { MapSelection.Message("线路 " + NameOf(l.FromId) + " ↔ " + NameOf(l.ToId) + " 已中断（端点被敌占或欠费）"); } catch { }
                            }
                            DLog.Force("铁路: " + l.FromId + " -> " + l.ToId + (broken ? " 中断" : " 恢复"));
                        }
                    }
                    // v4.21x: 维护费实扣(仅运营中线路; 扣不起 -> 欠费停运)
                    if (l.Built && !l.Broken)
                    {
                        int upkeep = MaintenanceOf(l);
                        if (upkeep > 0 && !PayLineUpkeep(l, upkeep))
                        {
                            l.Broken = true;
                            changed = true;
                            if (!wasBroken)
                            {
                                try { MapSelection.Message("线路 " + NameOf(l.FromId) + " ↔ " + NameOf(l.ToId) + " 因欠费停运"); } catch { }
                            }
                            DLog.Force("铁路: " + l.FromId + " -> " + l.ToId + " 欠维护费 " + upkeep + " 金/日, 停运");
                        }
                    }
                }
                if (day - _nextVisualCheck >= 7) _nextVisualCheck = day;   // v4.21x: 状态检查节流(7 天)

                if (changed)
                {
                    try { RailSystem.RebuildVisual(); } catch { }
                }
                try { TickTransports(); } catch { }   // v4.164: 铁路运输推进/到达
            }
            catch (Exception ex) { DLog.Force("铁路日结异常: " + ex.Message); }
        }

        // 端点城市被敌占 -> 中断
        private static bool IsBroken(RailLine l)
        {
            try
            {
                var owner = FindKingdom(l.OwnerId);
                if (owner == null) return true;
                var a = FindSettlement(l.FromId);
                var b = FindSettlement(l.ToId);
                if (a == null || b == null) return true;
                bool aMine = a.MapFaction == owner;
                bool bMine = b.MapFaction == owner;
                if (!aMine || !bMine) return true;
                // 与端点城市所属势力交战 -> 中断
                try
                {
                    var ka = a.MapFaction as Kingdom;
                    var kb = b.MapFaction as Kingdom;
                    if (ka != null && owner.IsAtWarWith(ka)) return true;
                    if (kb != null && owner.IsAtWarWith(kb)) return true;
                }
                catch { }
                return false;
            }
            catch { return true; }   // v4.21x: 异常时保守视作中断
        }

        internal static string NameOf(string settlementId)
        {
            try
            {
                var s = FindSettlement(settlementId);
                return s != null && s.Name != null ? s.Name.ToString() : settlementId;
            }
            catch { return settlementId; }
        }

        internal static Settlement FindSettlement(string id)
        {
            try
            {
                if (string.IsNullOrEmpty(id)) return null;
                foreach (var s in Settlement.All) if (s != null && s.StringId == id) return s;
            }
            catch { }
            return null;
        }

        internal static Kingdom FindKingdom(string id)
        {
            try
            {
                if (string.IsNullOrEmpty(id)) return null;
                foreach (var k in Kingdom.All) if (k != null && k.StringId == id) return k;
            }
            catch { }
            return null;
        }

        // ================= 存档 FIA_Rail =================
        internal static string Save()
        {
            try
            {
                var sb = new StringBuilder();
                sb.Append("v3");   // v4.21x: 线路追加 Tier/StateOwned 字段
                for (int i = 0; i < All.Count; i++)
                {
                    var l = All[i];
                    if (l == null) continue;
                    sb.Append(';').Append(l.FromId).Append(',').Append(l.ToId).Append(',').Append(l.OwnerId ?? "").Append(',')
                      .Append(l.StartDay).Append(',').Append(l.Progress.ToString("F3", CultureInfo.InvariantCulture)).Append(',')
                      .Append(l.Built ? "1" : "0").Append(',').Append(l.Broken ? "1" : "0").Append(',')
                      .Append(l.Length.ToString("F0", CultureInfo.InvariantCulture)).Append(',');
                    for (int p = 0; p < l.Points.Count; p++)
                    {
                        if (p > 0) sb.Append(':');
                        sb.Append(l.Points[p].x.ToString("F0", CultureInfo.InvariantCulture)).Append('_')
                          .Append(l.Points[p].y.ToString("F0", CultureInfo.InvariantCulture));
                    }
                    sb.Append(',').Append(ClampTier(l.Tier)).Append(',').Append(l.StateOwned ? "1" : "0");
                }
                // v4.164: 商品运输队列
                sb.Append(";T");
                for (int i = 0; i < Transports.Count; i++)
                {
                    var t = Transports[i];
                    if (t == null) continue;
                    sb.Append(';').Append(t.PartyId).Append(',').Append(t.FromId).Append(',').Append(t.ToId).Append(',')
                      .Append(t.StartDay).Append(',').Append(t.Progress.ToString("F3", CultureInfo.InvariantCulture)).Append(',')
                      .Append(t.TotalDays.ToString("F1", CultureInfo.InvariantCulture));
                }
                // v4.21x: 军列队列
                sb.Append(";M");
                for (int i = 0; i < MilTransports.Count; i++)
                {
                    var t = MilTransports[i];
                    if (t == null) continue;
                    sb.Append(';').Append(t.PartyId).Append(',').Append(t.FromId).Append(',').Append(t.ToId).Append(',')
                      .Append(t.StartDay).Append(',').Append(t.Progress.ToString("F3", CultureInfo.InvariantCulture)).Append(',')
                      .Append(t.TotalDays.ToString("F1", CultureInfo.InvariantCulture));
                }
                // v4.21x: 军列状态(城 -> 当日已用, 上次发车日); 旧档无 S 段 -> 默认 0
                sb.Append(";S");
                var milIds = new List<string>();
                foreach (var kv in MilUsedToday) milIds.Add(kv.Key);
                foreach (var kv in MilLastDispatchDay) if (!MilUsedToday.ContainsKey(kv.Key)) milIds.Add(kv.Key);
                for (int i = 0; i < milIds.Count; i++)
                {
                    int used, last;
                    MilUsedToday.TryGetValue(milIds[i], out used);
                    MilLastDispatchDay.TryGetValue(milIds[i], out last);
                    sb.Append(';').Append(milIds[i]).Append(',').Append(used).Append(',').Append(last);
                }
                return sb.ToString();
            }
            catch { return "v3;T;M"; }
        }

        internal static void Load(string data)
        {
            try
            {
                All.Clear();
                Transports.Clear();
                MilTransports.Clear();
                MilUsedToday.Clear();
                MilLastDispatchDay.Clear();
                _aiLegionDispatchDay.Clear();   // v4.21x: AI 军列状态随读档重置
                if (string.IsNullOrEmpty(data)) return;
                var seg = data.Split(';');
                if (seg.Length < 1 || (seg[0] != "v1" && seg[0] != "v2" && seg[0] != "v3")) return;
                bool inTransport = false, inMilitary = false, inMilState = false;
                for (int i = 1; i < seg.Length; i++)
                {
                    if (seg[i] == "T") { inTransport = true; inMilitary = false; inMilState = false; continue; }
                    if (seg[i] == "M") { inMilitary = true; inTransport = false; inMilState = false; continue; }
                    if (seg[i] == "S") { inMilState = true; inTransport = false; inMilitary = false; continue; }
                    var f = seg[i].Split(',');
                    if (inMilState)
                    {
                        if (f.Length < 3) continue;
                        MilUsedToday[f[0]] = I(f[1], 0);
                        MilLastDispatchDay[f[0]] = I(f[2], 0);
                        continue;
                    }
                    if (inTransport || inMilitary)
                    {
                        if (f.Length < 6) continue;
                        var tr = new RailTransport
                        {
                            PartyId = f[0],
                            FromId = f[1],
                            ToId = f[2],
                            StartDay = I(f[3], 0),
                            Progress = P(f[4], 0f),
                            TotalDays = P(f[5], 1f)
                        };
                        if (inMilitary) MilTransports.Add(tr); else Transports.Add(tr);
                        continue;
                    }
                    if (f.Length < 9) continue;
                    var l = new RailLine
                    {
                        FromId = f[0],
                        ToId = f[1],
                        OwnerId = f[2],
                        StartDay = I(f[3], 0),
                        Progress = P(f[4], 0f),
                        Built = f[5] == "1",
                        Broken = f[6] == "1",
                        Length = P(f[7], 0f),
                        Tier = f.Length >= 10 ? ClampTier(I(f[9], 1)) : 1,             // 旧档(v1/v2)默认 1 级
                        StateOwned = f.Length < 11 || f[10] != "0"                     // 旧档默认国有
                    };
                    if (!string.IsNullOrEmpty(f[8]))
                    {
                        var pts = f[8].Split(':');
                        for (int p = 0; p < pts.Length; p++)
                        {
                            var xy = pts[p].Split('_');
                            if (xy.Length < 2) continue;
                            l.Points.Add(new Vec2(P(xy[0], 0f), P(xy[1], 0f)));
                        }
                    }
                    All.Add(l);
                }
                try { _lastMilResetDay = Politics.Today(); } catch { }   // v4.21x: 读档当日不重置军列用量, 跨日才重置
                _nextVisualCheck = 0;                                    // v4.21x: 读档后重置状态检查节流
                _railLevelDay = int.MinValue;                            // 铁路建筑等级缓存失效, 下次查询重算
                DLog.Force("铁路: 读档 " + All.Count + " 条线, 运输中 " + Transports.Count + ", 军列中 " + MilTransports.Count
                    + ", 军列状态 " + MilUsedToday.Count + " 城");
            }
            catch (Exception ex) { DLog.Force("铁路读档异常: " + ex.Message); }
        }

        internal static void Reset()
        {
            All.Clear();
            Transports.Clear();
            MilTransports.Clear();
            MilUsedToday.Clear();
            MilLastDispatchDay.Clear();
            _dayIncome.Clear();
            _aiLegionDispatchDay.Clear();   // v4.21x: 新战役/重置清空 AI 军列状态
            _lastDay = -1;
            _lastMilResetDay = -1;
            _nextVisualCheck = 0;
            _railLevelDay = int.MinValue;
            _railLevelBySettlement.Clear();
            _railLevelByKingdom.Clear();
        }

        private static int I(string s, int def) { int v; return int.TryParse(s, out v) ? v : def; }
        private static float P(string s, float def) { float v; return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : def; }
    }
}
