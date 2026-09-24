using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Library;

namespace FeudalInternalAffairs
{
    // v4.152: 战略储备(重做 —— 从"一键买粮 500 石"升级为完整储备系统)
    //   ①多资源: 粮食 / 铁 / 木材 / 布匹(4 类关键物资)
    //   ②容量: 物流类建筑(仓库/驿站/贸易站)每级 +500 单位
    //   ③采购: 按市价从本国市场买入(真实抽走库存 -> 推高价格; 国库付款)
    //   ④释放: 手动释放平抑价格; 粮食储备在饥荒时自动释放(饥荒免疫判定)
    //   ⑤消耗: 战时每日消耗(按部队规模); 储备见底 -> 失去饥荒免疫
    internal static class StrategicReserve
    {
        internal const int KindCount = 4;
        internal static readonly string[] KindNames = { "粮食", "铁料", "木材", "布匹" };
        internal static readonly string[] KindGoods = { FeudalGoods.Grain, FeudalGoods.Iron, FeudalGoods.Hardwood, FeudalGoods.Linen };
        internal static readonly float[] Stock = new float[KindCount];       // 玩家储备存量
        internal const int CapacityPerLevel = 500;                            // 物流建筑每级容量
        internal const int TargetDays = 30;                                   // 目标: 30 天消耗量
        private static int _lastDay = -1;

        // 容量(物流类建筑每级 +500 + 基础 500)
        internal static int Capacity()
        {
            try
            {
                int cap = 500;
                var pk = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                if (pk == null) return cap;
                foreach (var kv in EconomyWorld.Buildings)
                {
                    var sb = kv.Value;
                    if (sb == null) continue;
                    bool mine = false;
                    try
                    {
                        foreach (var s in TaleWorlds.CampaignSystem.Settlements.Settlement.All)
                            if (s != null && s.StringId == kv.Key) { mine = s.MapFaction == pk; break; }
                    }
                    catch { }
                    if (!mine) continue;
                    for (int i = 0; i < sb.Groups.Count; i++)
                    {
                        var g = sb.Groups[i];
                        if (g == null || g.Count <= 0) continue;
                        string id = g.DefId ?? "";
                        if (id.IndexOf("warehouse", StringComparison.OrdinalIgnoreCase) >= 0
                            || id.IndexOf("granary", StringComparison.OrdinalIgnoreCase) >= 0
                            || id.IndexOf("logistics", StringComparison.OrdinalIgnoreCase) >= 0
                            || id.IndexOf("tradepost", StringComparison.OrdinalIgnoreCase) >= 0)
                            cap += CapacityPerLevel * g.Count;
                    }
                }
                return cap;
            }
            catch { return 500; }
        }

        internal static int TotalStock()
        {
            float t = 0f;
            for (int i = 0; i < KindCount; i++) t += Stock[i];
            return (int)t;
        }

        // 每日消耗量(全国人口 + 部队) —— 用于"天数"显示与目标采购
        internal static float DailyDemand(int kind)
        {
            try
            {
                if (kind == 0)   // 粮食: 人口/2000 + 部队/50
                {
                    float pop = Pops.TotalPopulation();
                    float troops = 0f;
                    try
                    {
                        var pk = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                        foreach (var p in MobileParty.All)
                        {
                            if (p == null || !p.IsActive) continue;
                            if (!ReferenceEquals(p.MapFaction, pk)) continue;
                            troops += p.MemberRoster != null ? p.MemberRoster.TotalManCount : 0;
                        }
                    }
                    catch { }
                    return Math.Max(1f, pop / 2000f + troops / 50f);
                }
                // 物资: 建筑维护需求(简化: 按建筑数)
                float b = 0f;
                try
                {
                    var pk = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                    foreach (var kv in EconomyWorld.Buildings)
                    {
                        var sb = kv.Value;
                        if (sb == null) continue;
                        foreach (var s in TaleWorlds.CampaignSystem.Settlements.Settlement.All)
                            if (s != null && s.StringId == kv.Key && s.MapFaction == pk) { b += sb.Groups.Count; break; }
                    }
                }
                catch { }
                return Math.Max(0.5f, b * 0.5f);
            }
            catch { return 1f; }
        }

        // 储备可支撑天数
        internal static float DaysOf(int kind)
        {
            try { return Stock[kind] / Math.Max(0.1f, DailyDemand(kind)); } catch { return 0f; }
        }

        // ================= 采购: 按市价从本国市场买入 =================
        internal static string Buy(int kind, int amount)
        {
            try
            {
                if (kind < 0 || kind >= KindCount) return "参数错误";
                if (amount <= 0) return "数量错误";
                string good = KindGoods[kind];
                float price = TradeRoutes.MyPriceOf(good);
                if (price <= 0f) price = FeudalGoods.BasePrice(good);
                int room = Capacity() - TotalStock();
                if (room <= 0) return "储备已满(" + TotalStock() + "/" + Capacity() + "); 建仓库/粮仓/贸易站可扩容";
                if (amount > room) amount = room;

                // 从本国市场抽货(按城市均摊; 库存不足则买不到)
                float available = 0f;
                foreach (var kv in EconomyWorld.Markets)
                {
                    var e = kv.Value != null ? kv.Value.Get(good) : null;
                    if (e != null && e.Stock > 1f) available += e.Stock * 0.5f;   // 最多抽一半库存
                }
                if (available < 1f) return "市面无货可购(" + KindNames[kind] + " 库存不足)";
                if (amount > available) amount = (int)available;

                int cost = (int)(amount * price * 1.05f);   // 5% 采购溢价
                if (EconomyWorld.Treasury.Gold < cost) return "国库不足(需 " + cost + " 第纳尔)";
                EconomyWorld.TreasurySpend(cost);
                Fiscal.AddCourt(cost);

                // 抽库存(推高价格)
                // v4.252: 原来按"想买的量"记账(Stock += amount), 而逐市场扣减时很多市场库存不足只能扣到一部分
                //   -> 差额凭空变成实物(经济审计: 破坏守恒)。现在只把**实际扣到的量**入库。
                float per = amount / Math.Max(1f, EconomyWorld.Markets.Count);
                float got = 0f;
                foreach (var kv in EconomyWorld.Markets)
                {
                    var e = kv.Value != null ? kv.Value.GetOrCreate(good) : null;
                    if (e == null) continue;
                    float take = Math.Min(e.Stock, per);
                    if (take <= 0f) continue;
                    e.Stock = Math.Max(0f, e.Stock - take);
                    got += take;
                }
                int gotI = (int)got;
                if (gotI <= 0) return "市面无货可购(" + KindNames[kind] + " 已耗尽)";
                Stock[kind] += gotI;
                DLog.Force("战略储备: 购入 " + KindNames[kind] + " " + gotI + "(费 " + cost + ") 存量 " + (int)Stock[kind]);
                return "已购入 " + KindNames[kind] + " " + gotI + "(费 " + cost + " 第纳尔, 存量 " + (int)Stock[kind] + ")";
            }
            catch (Exception ex) { DLog.Force("战略储备采购失败: " + ex.Message); return "采购失败, 见日志"; }
        }

        // ================= 释放: 注入本国市场(平抑价格) =================
        internal static string Release(int kind, int amount)
        {
            try
            {
                if (kind < 0 || kind >= KindCount) return "参数错误";
                if (Stock[kind] < 1f) return KindNames[kind] + " 储备为空";
                if (amount <= 0) return "数量错误";
                if (amount > Stock[kind]) amount = (int)Stock[kind];
                string good = KindGoods[kind];
                Stock[kind] -= amount;
                float per = amount / Math.Max(1f, EconomyWorld.Markets.Count);
                foreach (var kv in EconomyWorld.Markets)
                {
                    var e = kv.Value != null ? kv.Value.GetOrCreate(good) : null;
                    if (e == null) continue;
                    e.Stock += per;
                }
                DLog.Force("战略储备: 释放 " + KindNames[kind] + " " + amount + " 存量 " + (int)Stock[kind]);
                return "已释放 " + KindNames[kind] + " " + amount + " 到市场(存量 " + (int)Stock[kind] + ")";
            }
            catch { return "释放失败"; }
        }

        // ================= 每日: 战时消耗 + 饥荒自动释放 =================
        internal static void Daily(int day)
        {
            try
            {
                if (day == _lastDay) return;
                _lastDay = day;

                // 战时消耗(粮食): 交战国每日抽 20% 需求
                bool atWar = false;
                try
                {
                    var pk = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                    if (pk != null)
                        foreach (var x in Kingdom.All)
                        {
                            if (x == null || x.IsEliminated) continue;
                            if (pk.IsAtWarWith(x)) { atWar = true; break; }
                        }
                }
                catch { }
                if (atWar && Stock[0] > 0f)
                    Stock[0] = Math.Max(0f, Stock[0] - DailyDemand(0) * 0.2f);

                // 饥荒自动释放: 全国粮食天数过低 -> 放粮(真实注入市场)
                try
                {
                    var pk = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                    if (pk != null && Stock[0] > 10f)
                    {
                        float days;
                        WarEconomy.FoodDaysOf(pk, out days);
                        if (days < 8f)
                        {
                            int release = (int)Math.Min(Stock[0] * 0.3f, DailyDemand(0) * 5f);
                            if (release > 0)
                            {
                                string msg = Release(0, release);
                                InterestGroups.NotifyPlayer("饥荒自救: " + msg, true);
                                DLog.Force("战略储备: 饥荒自动释放 -> " + msg);
                            }
                        }
                    }
                }
                catch { }

                // 物资短缺自动释放(价格高于基础价 40% -> 释放平抑)
                for (int kind = 1; kind < KindCount; kind++)
                {
                    try
                    {
                        if (Stock[kind] < 5f) continue;
                        float price = TradeRoutes.MyPriceOf(KindGoods[kind]);
                        float baseP = FeudalGoods.BasePrice(KindGoods[kind]);
                        if (baseP > 0f && price > baseP * 1.4f)
                        {
                            int release = (int)Math.Min(Stock[kind] * 0.25f, 50f);
                            if (release > 0) Release(kind, release);
                        }
                    }
                    catch { }
                }
            }
            catch (Exception ex) { DLog.Force("战略储备日结异常: " + ex.Message); }
        }

        // 饥荒免疫(V3 官方 Shortage 机制的对应物): 粮食储备 >= 10 天 -> 视为有粮
        internal static bool FamineImmune()
        {
            try { return DaysOf(0) >= 10f; } catch { return false; }
        }

        // ================= 存档 =================
        internal static string Save()
        {
            try
            {
                var sb = new StringBuilder("v1");
                for (int i = 0; i < KindCount; i++)
                    sb.Append(';').Append(Stock[i].ToString("F0", CultureInfo.InvariantCulture));
                return sb.ToString();
            }
            catch { return "v1"; }
        }

        internal static void Load(string data)
        {
            try
            {
                for (int i = 0; i < KindCount; i++) Stock[i] = 0f;
                if (string.IsNullOrEmpty(data)) return;
                var seg = data.Split(';');
                if (seg.Length < 2 || seg[0] != "v1") return;
                for (int i = 1; i < seg.Length && i <= KindCount; i++)
                {
                    float v;
                    if (float.TryParse(seg[i], NumberStyles.Float, CultureInfo.InvariantCulture, out v))
                        Stock[i - 1] = Math.Max(0f, v);
                }
                DLog.Force("战略储备: 读档 存量 " + TotalStock() + "/" + Capacity());
            }
            catch { }
        }

        internal static void Reset()
        {
            for (int i = 0; i < KindCount; i++) Stock[i] = 0f;
            _lastDay = -1;
        }
    }
}
