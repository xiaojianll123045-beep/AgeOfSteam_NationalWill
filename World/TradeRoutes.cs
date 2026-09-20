using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;

namespace FeudalInternalAffairs
{
    // 贸易路线(文档 24.7; 对照 V3 Trade: 差价×货量 + 关税, 差价过小自动关停, 交战切断)
    internal class TradeRoute
    {
        internal string GoodId;
        internal string PartnerId;     // 伙伴王国 StringId
        internal string PartnerName;
        internal int Level = 1;        // 1-3 级(货量)
        internal bool Export;          // true = 出口(我方卖出)
        internal int StartDay;
        internal int LastProfit;       // 昨日净收益(UI)
        internal int TotalProfit;      // 累计
    }

    internal static class TradeRoutes
    {
        internal const int VolumePerLevel = 5;    // 每级每日货量(单位)
        internal static readonly List<TradeRoute> Routes = new List<TradeRoute>();
        private static int _lastDay = -1;

        // 可建路线数(关税政策 + 贸易行会 + 商路建筑)
        internal static int MaxRoutes()
        {
            try
            {
                int n = 2;
                n += LawSystem.Level(LawSystem.LTrade);          // 关税政策: 越开放越多
                n += PowerBlocs.TradeCapacityBonus();            // v4.138: 权力集团贸易容量(V3 Trade League +原则)
                foreach (var g in Guilds.All) if (g.Id == "guild_trade" && Guilds.IsFounded(g.Id)) { n += 1; break; }
                return n;
            }
            catch { return 3; }
        }

        // 关税率(V3: 贸易政策决定; 权力集团关税同盟 -50%)
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
                return r * PowerBlocs.TariffMult();   // v4.138: 集团关税同盟/原则
            }
            catch { return 0.2f; }
        }

        internal static int LevelCap()
        {
            try { return LawSystem.Level(LawSystem.LTrade) >= 2 ? 3 : 2; } catch { return 2; }
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
                return "已建立贸易路线: " + (export ? "出口" : "进口") + " " + FeudalGoods.NameOf(goodId) + " ↔ " + partnerName;
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
                r.Level++;
                DLog.Force("贸易: 升级路线 -> " + FeudalGoods.NameOf(r.GoodId) + " Lv" + r.Level);
                return "路线升级为 Lv" + r.Level + "(货量 " + (r.Level * VolumePerLevel) + "/日)";
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

        // 外国市场价格模拟(以本国基准价 × 确定性扰动, 缓慢波动; V3: MAPI 收敛但存在差价)
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
                return baseP * m;
            }
            catch { return FeudalGoods.BasePrice(r.GoodId); }
        }

        internal static float MyPriceOf(string goodId)
        {
            try { return EconomyWorld.National.PriceOf(goodId); } catch { return FeudalGoods.BasePrice(goodId); }
        }

        // 日结(文档 24.7): 差价×货量 -> 国库; 我方市场库存增减; 差价过小/交战则关停
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
                    // 交战: 强制切断(V3)
                    if (k != null && IsAtWarWith(k, r.PartnerId))
                    {
                        Routes.RemoveAt(i);
                        Notify("战争爆发, 已切断与 " + r.PartnerName + " 的贸易路线", true);
                        continue;
                    }
                    float baseP = FeudalGoods.BasePrice(r.GoodId);
                    float my = MyPriceOf(r.GoodId);
                    float partner = PartnerPriceOf(r, day);
                    float diff = partner - my;   // >0: 外国更贵 -> 出口有利
                    float vol = r.Level * VolumePerLevel;
                    if (r.Export)
                    {
                        if (diff < baseP * 0.10f) { AutoClose(i, r, "差价过小"); continue; }
                        DrainStock(r.GoodId, vol * 0.5f);
                        int income = (int)(diff * vol);
                        int tariff = (int)(income * TariffRate());
                        EconomyWorld.TreasuryAdd(income + tariff);
                        Fiscal.Export += income + tariff;
                        r.LastProfit = income + tariff;
                        r.TotalProfit += r.LastProfit;
                    }
                    else
                    {
                        if (-diff < baseP * 0.10f) { AutoClose(i, r, "差价过小"); continue; }
                        int cost = (int)((-diff) * vol);
                        int tariff = (int)(cost * TariffRate());
                        if (EconomyWorld.Treasury.Gold < cost + tariff) { AutoClose(i, r, "国库不足"); continue; }
                        EconomyWorld.TreasurySpend(cost + tariff);
                        Fiscal.AddCourt(cost + tariff);
                        FillStock(r.GoodId, vol * 0.5f);
                        r.LastProfit = -(cost + tariff);
                        r.TotalProfit += r.LastProfit;
                    }
                }
            }
            catch (Exception ex) { DLog.Force("贸易日结异常: " + ex.Message); }
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
                sb.Append("v1;");
                for (int i = 0; i < Routes.Count; i++)
                {
                    var r = Routes[i];
                    if (i > 0) sb.Append('|');
                    sb.Append(r.GoodId).Append(',').Append(r.PartnerId ?? "").Append(',').Append(r.PartnerName ?? "").Append(',')
                      .Append(r.Level).Append(',').Append(r.Export ? "1" : "0").Append(',').Append(r.StartDay).Append(',')
                      .Append(r.LastProfit).Append(',').Append(r.TotalProfit);
                }
                sb.Append(';');
                return sb.ToString();
            }
            catch { return "v1;;"; }
        }

        internal static void Load(string data)
        {
            try
            {
                Routes.Clear();
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
                        if (int.TryParse(f[3], out x)) r.Level = Math.Max(1, Math.Min(3, x));
                        r.Export = f[4] == "1";
                        if (int.TryParse(f[5], out x)) r.StartDay = x;
                        if (int.TryParse(f[6], out x)) r.LastProfit = x;
                        if (int.TryParse(f[7], out x)) r.TotalProfit = x;
                        Routes.Add(r);
                    }
                }
                DLog.Force("贸易: 读档 路线=" + Routes.Count);
            }
            catch { }
        }

        internal static void Reset()
        {
            Routes.Clear();
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
