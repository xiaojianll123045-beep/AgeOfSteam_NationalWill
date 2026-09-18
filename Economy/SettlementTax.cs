using System;
using TaleWorlds.CampaignSystem.Settlements;

namespace FeudalInternalAffairs
{
    // 定居点税收(文档 6.2; v3.6 实现)
    //   某定居点税收 = (城镇繁荣 × 0.05 + 非本族繁荣 × 0.03 + 村庄户数 × 0.02) × (1 + 税率加成) × 税率
    //   税率默认 100%; 税率加成来自政策(13.6, 未实现 -> 0)
    //   只统计"玩家王国"的定居点(国库 = 玩家金钱, 6.1); AI 王国不记账
    internal static class SettlementTax
    {
        internal const float TaxRate = 1.0f;    // 税率(默认 100%)
        internal static float RateBonus = 0f;   // 税率加成(政策 13.6)

        internal static int Daily()
        {
            int total = 0;
            try
            {
                var pk = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                if (pk == null) return 0;
                var culture = pk.Culture;
                foreach (var s in pk.Settlements)
                {
                    if (s == null) continue;
                    float v = 0f;
                    if (s.Town != null && s.IsTown)
                    {
                        float pros = s.Town.Prosperity;
                        v += pros * 0.05f;
                        if (culture != null && s.Culture != culture) v += pros * 0.03f;   // 非本族
                    }
                    else if (s.Village != null)
                    {
                        v += s.Village.Hearth * 0.02f;
                    }
                    if (v <= 0f) continue;
                    total += (int)Math.Round(v * (1f + RateBonus) * TaxRate);
                }
                if (total > 0)
                {
                    EconomyWorld.TreasuryAdd(total);
                    Fiscal.AddTax(total);
                }
                DLog.Force("税收日结: 今日+" + total + " 本月+" + Fiscal.Tax);
            }
            catch (Exception ex) { DLog.Force("税收日结异常: " + ex.Message); }
            return total;
        }
    }
}
