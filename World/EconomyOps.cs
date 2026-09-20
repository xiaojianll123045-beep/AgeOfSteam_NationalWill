using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;

namespace FeudalInternalAffairs
{
    // v4.114: 玩家经济操作(国家战略储备)
    internal static class EconomyOps
    {
        // 玩家: 用国库从市面买粮, 把存粮最紧的城镇补到 wantDays 天(单次最多 maxBuy 石)
        internal static string StrategicReserve(float wantDays, int maxBuy)
        {
            try
            {
                var pk = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                if (pk == null) return "尚未建立国家意志";
                Settlement needTown = null;
                float needDays = float.MaxValue;
                foreach (var s in pk.Settlements)
                {
                    if (s == null || s.Town == null) continue;
                    var m = EconomyWorld.FindMarket(s.StringId);
                    if (m == null) continue;
                    var e = m.Get(FeudalGoods.Grain);
                    float stock = e != null ? e.Stock : 0f;
                    float cons = e != null ? e.DailyConsumption : 0f;
                    float days = cons > 0.01f ? stock / cons : 99f;
                    if (days < needDays) { needDays = days; needTown = s; }
                }
                if (needTown == null) return "本国没有可储备的城镇";
                var mm = EconomyWorld.FindMarket(needTown.StringId);
                if (mm == null) return "市场不可用";
                var grain = FeudalGoods.Item(FeudalGoods.Grain);
                if (grain == null) return "粮食商品缺失";
                var me = mm.Get(FeudalGoods.Grain);
                float cons2 = me != null ? me.DailyConsumption : 0f;
                float stock2 = me != null ? me.Stock : 0f;
                float need = cons2 * wantDays - stock2;
                string townName = needTown.Name != null ? needTown.Name.ToString() : "?";
                if (need <= 0.5f) return townName + " 存粮已够 " + (int)wantDays + " 天";
                int buy = (int)Math.Min(need, maxBuy);
                float price = me != null && me.Price > 0.1f ? me.Price : 20f;
                float cost = buy * price;
                if (EconomyWorld.Treasury.Gold < cost)
                {
                    buy = (int)(EconomyWorld.Treasury.Gold / Math.Max(1f, price));
                    cost = buy * price;
                }
                if (buy <= 0) return "国库不足, 买不起粮";
                EconomyWorld.TreasurySpend((int)cost);
                try { needTown.ItemRoster.AddToCounts(grain, buy); } catch { }
                if (me != null) me.Stock = Math.Max(0f, me.Stock - buy);   // 市场买走 -> 供给减少(价格上行)
                return "战略储备: 购入 " + buy + " 粮(费 " + (int)cost + " 第纳尔, " + townName
                    + " 存粮 " + (int)needDays + "→约 " + (int)wantDays + " 天)";
            }
            catch (Exception ex) { DLog.Force("战略储备失败: " + ex.Message); return "战略储备失败, 见日志"; }
        }
    }
}
