using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;

namespace FeudalInternalAffairs
{
    // 贸易路线(v4.152 按 V3 官方 Market/Trade routes 重做):
    //   ①贸易容量: 贸易中心类建筑每级 +10 容量, 路线货量占用容量(总货量受限)
    //   ②货量自动调整(V3 Weekly Trades): 每周按利润增减货量, 长期亏损自动关停
    //   ③关税按"基础价格"计算(V3 官方: 关税基于 base price 而非市场价)
    //   ④贸易优势(Trade Advantage): 基础 100 + 产量份额 + 利益 - 战争/禁运; 每点 +0.25% 价格优惠
    //   ⑤禁运(Embargo): 交战国自动禁运(免费); 手动禁运花国库
    //   ⑥垄断加成: 某商品全球出口份额越高, 出口价越高(V3: 100% 份额 = +20%)
    internal class TradeRoute
    {
        internal string GoodId;
        internal string PartnerId;     // 伙伴王国 StringId
        internal string PartnerName;
        internal int Level = 1;        // 1-5 级(货量 = Level × 5)
        internal bool Export;          // true = 出口(我方卖出)
        internal int StartDay;
        internal int LastProfit;       // 昨日净收益(UI)
        internal int TotalProfit;      // 累计
        internal int LastVolume;       // 昨日实际货量
    }

    internal static class TradeRoutes
    {
        internal const int VolumePerLevel = 5;    // 每级每日货量(单位)
        internal const int CapacityPerBuildingLevel = 10;   // V3 官方: 贸易中心每级 10 容量
        internal static readonly List<TradeRoute> Routes = new List<TradeRoute>();
        private static int _lastDay = -1;
        private static readonly HashSet<string> Embargoed = new HashSet<string>();   // 我方手动禁运的王国
        private static readonly Dictionary<string, int> EmbargoDay = new Dictionary<string, int>();

        // ================= 贸易容量(V3 官方: 贸易中心每级 10) =================
        internal static int TotalCapacity()
        {
            try
            {
                int cap = 20;   // 基础(首都商埠)
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
                        if (id.IndexOf("tradepost", StringComparison.OrdinalIgnoreCase) >= 0
                            || id.IndexOf("market", StringComparison.OrdinalIgnoreCase) >= 0
                            || id.IndexOf("port", StringComparison.OrdinalIgnoreCase) >= 0
                            || id.IndexOf("railway", StringComparison.OrdinalIgnoreCase) >= 0)   // v4.165: 铁路提升贸易容量
                            cap += CapacityPerBuildingLevel * g.Count;
                    }
                }
                cap += PowerBlocs.TradeCapacityBonus() * 5;    // 权力集团贸易容量
                return cap;
            }
            catch { return 20; }
        }

        internal static int UsedCapacity()
        {
            int n = 0;
            for (int i = 0; i < Routes.Count; i++) if (Routes[i] != null) n += Routes[i].Level * VolumePerLevel;
            return n;
        }

        // 可建路线数(关税政策 + 贸易行会)
        internal static int MaxRoutes()
        {
            try
            {
                int n = 2;
                n += LawSystem.Level(LawSystem.LTrade);          // 关税政策: 越开放越多
                n += PowerBlocs.TradeCapacityBonus();            // 权力集团贸易容量
                foreach (var g in Guilds.All) if (g.Id == "guild_trade" && Guilds.IsFounded(g.Id)) { n += 1; break; }
                return n;
            }
            catch { return 3; }
        }

        // 关税率(V3 官方: 按贸易政策法; 权力集团关税同盟 -50%)
        internal static float TariffRate()
        {
            try
            {
                float r;
                switch (LawSystem.Level(LawSystem.LTrade))
                {
                    case 0: r = 0.30f; break;   // 孤立自守
                    case 1: r = 0.22f; break;   // 重商保护
                    case 2: r = 0.15f; break;   // 通商互惠
                    default: r = 0.08f; break;  // 自由贸易
                }
                return r * PowerBlocs.TariffMult();   // 集团关税同盟/原则
            }
            catch { return 0.2f; }
        }

        // ================= 贸易优势(V3 官方: 基础 100; 100% 优势 = 25% 更好价格) =================
        //   基础 100 + 全球产量份额×2 + 利益(邻国自动) + 修正; 每 1 点优势 = 0.25% 价格优惠
        internal static float TradeAdvantageOf(string partnerId)
        {
            try
            {
                float a = 100f;
                // 利益: 已宣示利益的国家 +10
                try
                {
                    if (Interests.Has(partnerId)) a += 10f;
                    var pk = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                    foreach (var x in Kingdom.All)
                    {
                        if (x == null || x.StringId != partnerId) continue;
                        if (Diplomacy.IsAlly(pk, x)) a += 15f;
                        break;
                    }
                }
                catch { }
                // 战争/禁运: 大幅下降(交战本就会断路线)
                try { if (IsEmbargoed(partnerId)) a -= 60f; } catch { }
                // 贸易法(自由贸易 +25%)
                try { if (LawSystem.Level(LawSystem.LTrade) >= 3) a += 25f; } catch { }
                // v4.241: 科技"贸易优势 +N"(铁路/电话/航海术/浮式港口/海路战略/经验主义/国际关系/多边联盟/国际汇兑标准)
                try { a += Research.TradeBonus(); } catch { }
                if (a < 10f) a = 10f;
                return a;
            }
            catch { return 100f; }
        }

        // 价格优惠系数(V3 官方: 100% 贸易优势 -> 25% 更好价格)
        internal static float AdvantagePriceMult(string partnerId)
        {
            float a = TradeAdvantageOf(partnerId);
            return 1f - (a - 100f) / 100f * 0.25f;
        }

        // ================= 垄断加成(V3 官方: 控制 100% 出口 -> 世界价格 +20%) =================
        internal static float MonopolyMult(string goodId)
        {
            try
            {
                float mine = 0f, world = 0f;
                for (int i = 0; i < Routes.Count; i++)
                {
                    var r = Routes[i];
                    if (r == null || r.GoodId != goodId || !r.Export) continue;
                    mine += r.Level * VolumePerLevel;
                }
                if (mine <= 0f) return 1f;
                // 全球产量近似: 全国卖单(产量)
                try
                {
                    float sell = 0f;
                    EconomyWorld.National.SellVolume.TryGetValue(goodId, out sell);
                    world = Math.Max(1f, sell);
                }
                catch { world = 100f; }
                float share = Math.Min(1f, mine / Math.Max(1f, world));
                return 1f + 0.20f * share;
            }
            catch { return 1f; }
        }

        internal static int LevelCap()
        {
            try { return LawSystem.Level(LawSystem.LTrade) >= 2 ? 5 : 3; } catch { return 3; }   // v4.152: 1~5 级
        }

        internal static string Establish(string partnerId, string partnerName, string goodId, bool export)
        {
            try
            {
                if (Routes.Count >= MaxRoutes())
                    return "贸易路线名额已满(" + Routes.Count + "/" + MaxRoutes() + "); 提升关税政策或建贸易行会可增加";
                if (string.IsNullOrEmpty(partnerId) || string.IsNullOrEmpty(goodId)) return "参数错误";
                for (int i = 0; i < Routes.Count; i++)
                    if (Routes[i].PartnerId == partnerId && Routes[i].GoodId == goodId && Routes[i].Export == export)
                        return "已存在相同的贸易路线";
                if (UsedCapacity() + VolumePerLevel > TotalCapacity())
                    return "贸易容量不足(" + UsedCapacity() + "/" + TotalCapacity() + "); 建贸易站/市场类建筑或提升集团可增加";
                var r = new TradeRoute
                {
                    PartnerId = partnerId,
                    PartnerName = partnerName,
                    GoodId = goodId,
                    Export = export,
                    Level = 1,
                    StartDay = Politics.Today()
                };
                Routes.Add(r);
                DLog.Force("贸易: 建立路线 -> " + (export ? "出口 " : "进口 ") + FeudalGoods.NameOf(goodId) + " @ " + partnerName);
                return "已建立贸易路线: " + (export ? "出口" : "进口") + " " + FeudalGoods.NameOf(goodId) + " ↔ " + partnerName
                     + "(占用容量 " + (r.Level * VolumePerLevel) + "/" + TotalCapacity() + ")";
            }
            catch { return "建立路线失败"; }
        }

        internal static string Upgrade(int idx)
        {
            try
            {
                if (idx < 0 || idx >= Routes.Count) return "";
                var r = Routes[idx];
                if (r.Level >= LevelCap()) return "该路线已达当前政策下的最高等级(" + r.Level + ")";
                if (UsedCapacity() + VolumePerLevel > TotalCapacity()) return "贸易容量不足(需建贸易站/市场类建筑)";
                r.Level++;
                DLog.Force("贸易: 升级路线 -> " + FeudalGoods.NameOf(r.GoodId) + " Lv" + r.Level);
                return "路线升级为 Lv" + r.Level + "(货量 " + (r.Level * VolumePerLevel) + "/日, 容量 "
                     + UsedCapacity() + "/" + TotalCapacity() + ")";
            }
            catch { return "升级失败"; }
        }

        internal static string Cancel(int idx)
        {
            try
            {
                if (idx < 0 || idx >= Routes.Count) return "";
                var r = Routes[idx];
                Routes.RemoveAt(idx);
                DLog.Force("贸易: 切断路线 -> " + FeudalGoods.NameOf(r.GoodId));
                return "已切断与 " + r.PartnerName + " 的 " + FeudalGoods.NameOf(r.GoodId) + " 贸易路线";
            }
            catch { return "切断失败"; }
        }

        // ================= 禁运(V3 官方: 手动禁运; 交战自动) =================
        internal static bool IsEmbargoed(string partnerId)
        {
            try { return !string.IsNullOrEmpty(partnerId) && Embargoed.Contains(partnerId); }
            catch { return false; }
        }

        internal static string SetEmbargo(string partnerId, string partnerName, bool on)
        {
            try
            {
                if (string.IsNullOrEmpty(partnerId)) return "参数错误";
                if (on)
                {
                    if (Embargoed.Contains(partnerId)) return "已对该国禁运";
                    int cost = 500;
                    if (EconomyWorld.Treasury.Gold < cost) return "国库不足(需 " + cost + " 第纳尔)";
                    EconomyWorld.TreasurySpend(cost);
                    Fiscal.AddCourt(cost);
                    Embargoed.Add(partnerId);
                    EmbargoDay[partnerId] = Politics.Today();
                    // 立即切断与该国的所有路线
                    for (int i = Routes.Count - 1; i >= 0; i--)
                        if (Routes[i] != null && Routes[i].PartnerId == partnerId)
                        {
                            Notify("禁运: 已切断与 " + Routes[i].PartnerName + " 的 " + FeudalGoods.NameOf(Routes[i].GoodId) + " 路线", false);
                            Routes.RemoveAt(i);
                        }
                    DLog.Force("贸易: 对 " + partnerName + " 实施禁运(费 " + cost + ")");
                    return "已对 " + partnerName + " 实施禁运(贸易优势 -60)";
                }
                Embargoed.Remove(partnerId);
                EmbargoDay.Remove(partnerId);
                DLog.Force("贸易: 解除对 " + partnerName + " 的禁运");
                return "已解除对 " + partnerName + " 的禁运";
            }
            catch { return "禁运操作失败"; }
        }

        // 外国市场价格模拟(以本国基准价 × 确定性扰动; V3: 市场间存在差价)
        internal static float PartnerPriceOf(TradeRoute r, int day)
        {
            try
            {
                float baseP = FeudalGoods.BasePrice(r.GoodId);
                int h = 0;
                string key = (r.PartnerId ?? "") + r.GoodId;
                for (int i = 0; i < key.Length; i++) h = (h * 31 + key[i]) & 0x7fffffff;
                float m = 0.72f + (h % 100) / 100f * 0.56f;                     // 0.72~1.28
                m *= 1f + 0.10f * (float)Math.Sin((day + h % 41) * 0.045f);      // 缓慢波动
                // 贸易优势: 我方在该伙伴处买/卖更有利
                m *= AdvantagePriceMult(r.PartnerId);
                return baseP * m;
            }
            catch { return FeudalGoods.BasePrice(r.GoodId); }
        }

        internal static float MyPriceOf(string goodId)
        {
            try { return EconomyWorld.National.PriceOf(goodId); } catch { return FeudalGoods.BasePrice(goodId); }
        }

        // 预计日关税(UI 单列; 与日结口径一致: 基础价 × 货量 × 关税率)
        internal static float EstimateDailyTariff(TradeRoute r)
        {
            try
            {
                if (r == null) return 0f;
                return FeudalGoods.BasePrice(r.GoodId) * (r.Level * VolumePerLevel) * TariffRate();
            }
            catch { return 0f; }
        }

        // 预计日利润(UI 与自动调量共用; 正=赚)
        internal static float EstimateDailyProfit(TradeRoute r, int day)
        {
            try
            {
                float baseP = FeudalGoods.BasePrice(r.GoodId);
                float my = MyPriceOf(r.GoodId);
                float partner = PartnerPriceOf(r, day);
                float vol = r.Level * VolumePerLevel;
                float tariff = baseP * vol * TariffRate();     // V3 官方: 关税按基础价
                if (r.Export)
                {
                    float income = (partner - my) * vol * MonopolyMult(r.GoodId);
                    return income + tariff;
                }
                else
                {
                    float cost = (my - partner) * vol;
                    return -(cost + tariff);
                }
            }
            catch { return 0f; }
        }

        // ================= 日结: 差价×货量 -> 国库; 库存增减; 交战切断 =================
        internal static void Daily(int day)
        {
            try
            {
                if (day == _lastDay) return;
                _lastDay = day;
                if (Routes.Count == 0) return;
                var k = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                for (int i = Routes.Count - 1; i >= 0; i--)
                {
                    var r = Routes[i];
                    // 交战/禁运: 强制切断(V3 官方)
                    if (k != null && (IsAtWarWith(k, r.PartnerId) || IsEmbargoed(r.PartnerId)))
                    {
                        Routes.RemoveAt(i);
                        Notify("战争/禁运, 已切断与 " + r.PartnerName + " 的贸易路线", true);
                        continue;
                    }
                    float baseP = FeudalGoods.BasePrice(r.GoodId);
                    float my = MyPriceOf(r.GoodId);
                    float partner = PartnerPriceOf(r, day);
                    float diff = partner - my;   // >0: 外国更贵 -> 出口有利
                    float vol = r.Level * VolumePerLevel;
                    float tariff = baseP * vol * TariffRate();     // V3 官方: 关税按基础价
                    r.LastVolume = (int)vol;
                    if (r.Export)
                    {
                        if (diff < baseP * 0.05f) { AutoClose(i, r, "差价过小"); continue; }
                        DrainStock(r.GoodId, vol * 0.5f);
                        float income = diff * vol * MonopolyMult(r.GoodId);
                        int net = (int)(income + tariff);
                        EconomyWorld.TreasuryAdd(net);
                        Fiscal.Export += net;
                        r.LastProfit = net;
                        r.TotalProfit += net;
                    }
                    else
                    {
                        if (-diff < baseP * 0.05f) { AutoClose(i, r, "差价过小"); continue; }
                        float cost = -diff * vol;
                        int pay = (int)(cost + tariff);
                        if (EconomyWorld.Treasury.Gold < pay) { AutoClose(i, r, "国库不足"); continue; }
                        EconomyWorld.TreasurySpend(pay);
                        Fiscal.AddCourt(pay);
                        FillStock(r.GoodId, vol * 0.5f);
                        r.LastProfit = -pay;
                        r.TotalProfit += r.LastProfit;
                    }
                }
                // 每周: 货量自动调整(V3 Weekly Trades)
                if (day % 7 == 0) WeeklyAdjust(day);
            }
            catch (Exception ex) { DLog.Force("贸易日结异常: " + ex.Message); }
        }

        // 每周按利润自动增减货量(V3 官方: 贸易中心每周调整贸易量)
        private static void WeeklyAdjust(int day)
        {
            try
            {
                for (int i = Routes.Count - 1; i >= 0; i--)
                {
                    var r = Routes[i];
                    if (r == null) continue;
                    float profit = EstimateDailyProfit(r, day);
                    if (profit > 0f && r.Level < LevelCap() && UsedCapacity() + VolumePerLevel <= TotalCapacity())
                    {
                        r.Level++;   // 有利润 -> 扩量
                        DLog.Info("贸易: " + FeudalGoods.NameOf(r.GoodId) + " @ " + r.PartnerName + " 利润好 -> 自动扩至 Lv" + r.Level);
                    }
                    else if (profit < 0f)
                    {
                        // 亏损: 先降货量, 降到 1 级仍亏损 -> 关停
                        if (r.Level > 1) r.Level--;
                        else if (r.TotalProfit < 0 && profit < -50f) { AutoClose(i, r, "持续亏损"); }
                    }
                }
            }
            catch { }
        }

        private static void AutoClose(int idx, TradeRoute r, string why)
        {
            try
            {
                Routes.RemoveAt(idx);
                Notify("贸易路线自动关停(" + why + "): " + FeudalGoods.NameOf(r.GoodId) + " ↔ " + r.PartnerName, false);
                DLog.Force("贸易: 路线关停(" + why + ") -> " + r.PartnerName + " " + r.GoodId);
            }
            catch { }
        }

        private static bool IsAtWarWith(Kingdom k, string kingdomId)
        {
            try
            {
                foreach (var other in Kingdom.All)
                {
                    if (other == null || other.StringId != kingdomId) continue;
                    return k.IsAtWarWith(other);
                }
            }
            catch { }
            return false;
        }

        // 出口: 从本国市场抽走库存(推高价格) - 各城按比例
        private static void DrainStock(string goodId, float amount)
        {
            try
            {
                if (amount <= 0f) return;
                int towns = 0;
                foreach (var kv in EconomyWorld.Markets)
                {
                    var e = kv.Value != null ? kv.Value.Get(goodId) : null;
                    if (e != null && e.Stock > 0.5f) towns++;
                }
                if (towns == 0) return;
                float per = amount / towns;
                foreach (var kv in EconomyWorld.Markets)
                {
                    var e = kv.Value != null ? kv.Value.Get(goodId) : null;
                    if (e == null) continue;
                    e.Stock = Math.Max(0f, e.Stock - per);
                }
            }
            catch { }
        }

        // 进口: 注入本国市场库存(压低价格)
        private static void FillStock(string goodId, float amount)
        {
            try
            {
                if (amount <= 0f) return;
                int towns = 0;
                foreach (var kv in EconomyWorld.Markets) if (kv.Value != null) towns++;
                if (towns == 0) return;
                float per = amount / towns;
                foreach (var kv in EconomyWorld.Markets)
                {
                    var e = kv.Value != null ? kv.Value.GetOrCreate(goodId) : null;
                    if (e == null) continue;
                    e.Stock += per;
                }
            }
            catch { }
        }

        private static void Notify(string msg, bool alert)
        {
            try
            {
                var c = Color.FromUint(alert ? 4294901760U : 4294953344U);
                InformationManager.DisplayMessage(new InformationMessage(msg, c));
            }
            catch { }
        }

        // ================= 存档 FIA_Trade =================
        internal static string Save()
        {
            try
            {
                var sb = new StringBuilder();
                sb.Append("v2;");
                for (int i = 0; i < Routes.Count; i++)
                {
                    var r = Routes[i];
                    if (i > 0) sb.Append('|');
                    sb.Append(r.GoodId).Append(',').Append(r.PartnerId ?? "").Append(',').Append(r.PartnerName ?? "").Append(',')
                      .Append(r.Level).Append(',').Append(r.Export ? "1" : "0").Append(',').Append(r.StartDay).Append(',')
                      .Append(r.LastProfit).Append(',').Append(r.TotalProfit);
                }
                sb.Append(';');
                foreach (var e in Embargoed) sb.Append(e).Append(',');
                return sb.ToString();
            }
            catch { return "v2;;"; }
        }

        internal static void Load(string data)
        {
            try
            {
                Routes.Clear();
                Embargoed.Clear();
                if (string.IsNullOrEmpty(data)) return;
                var seg = data.Split(';');
                if (seg.Length > 1 && !string.IsNullOrEmpty(seg[1]))
                {
                    foreach (var line in seg[1].Split('|'))
                    {
                        if (string.IsNullOrEmpty(line)) continue;
                        var f = line.Split(',');
                        if (f.Length < 9) continue;
                        var r = new TradeRoute();
                        r.GoodId = f[0];
                        r.PartnerId = f[1];
                        r.PartnerName = f[2];
                        int x;
                        if (int.TryParse(f[3], out x)) r.Level = Math.Max(1, Math.Min(5, x));
                        r.Export = f[4] == "1";
                        if (int.TryParse(f[5], out x)) r.StartDay = x;
                        if (int.TryParse(f[6], out x)) r.LastProfit = x;
                        if (int.TryParse(f[7], out x)) r.TotalProfit = x;
                        Routes.Add(r);
                    }
                }
                if (seg.Length > 2 && !string.IsNullOrEmpty(seg[2]))
                    foreach (var e in seg[2].Split(',')) if (e.Length > 0) Embargoed.Add(e);
                DLog.Force("贸易: 读档 路线=" + Routes.Count + " 禁运=" + Embargoed.Count);
            }
            catch { }
        }

        internal static void Reset()
        {
            Routes.Clear();
            Embargoed.Clear();
            EmbargoDay.Clear();
            _lastDay = -1;
        }

        // 伙伴王国列表(UI)
        internal static List<Kingdom> PartnerKingdoms()
        {
            var list = new List<Kingdom>();
            try
            {
                var k = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                foreach (var other in Kingdom.All)
                {
                    if (other == null || other == k) continue;
                    if (other.IsEliminated) continue;
                    list.Add(other);
                }
            }
            catch { }
            return list;
        }
    }
}
