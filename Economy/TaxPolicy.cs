using System;
using TaleWorlds.CampaignSystem;

namespace FeudalInternalAffairs
{
    // 税制政策(文档 20.1): 土地税/人头税/什一税/市场税 × 五档(免征/低/中/高/苛征)
    // 影响: 国库收入 + 各阶层税负 -> 激进/工商产值/迁移吸引力
    internal static class TaxPolicy
    {
        internal static readonly string[] Names = { "土地税", "人头税", "什一税", "市场税" };
        internal static readonly string[] LevelNames = { "免征", "低", "中", "高", "苛征" };
        internal static readonly float[] Mult = { 0f, 0.5f, 1f, 1.5f, 2f };

        internal static readonly int[] Level = { 2, 1, 2, 1 };   // 默认: 土地中/人头低/什一中/市场低
        internal static int ChangeDay = -9999;                    // 上次调整(战役日; 保留字段, 冷却已取消)
        internal const int CooldownDays = 0;                      // v4.2: 冷却取消(用户要求)

        internal static readonly int[] MonthIncome = new int[4];  // 本期各税收入(UI)
        internal static readonly int[] LastIncome = new int[4];   // 上期

        // 政策效果(由税档合成; 在 Recompute 中刷新)
        internal static float RadicalPressure;   // 每人每日额外激进
        internal static float IndustryMult = 1f; // 工商产出乘数
        internal static float AttractMult = 1f;  // 迁移吸引力乘数
        internal static float PopNeedMult = 1f;  // 税负对需求盘子的压缩(暂未用)

        internal static void Recompute()
        {
            try
            {
                float sum = 0f;
                for (int i = 0; i < 4; i++) sum += Mult[Level[i]];
                // 中档全开 = 4.0 为基准; 超出部分开始惩罚
                float over = Math.Max(0f, sum - 4f);
                RadicalPressure = over * 0.004f;                 // 苛征全开(8.0) -> 0.016/日
                IndustryMult = 1f - over * 0.05f;                // 苛征全开 -> -20%
                AttractMult = 1f - over * 0.06f;                 // 苛征全开 -> -24%
                if (IndustryMult < 0.6f) IndustryMult = 0.6f;
                if (AttractMult < 0.5f) AttractMult = 0.5f;
            }
            catch { }
        }

        internal static int CooldownLeft(int today) { return 0; }
        internal static bool CanChange(int today) { return true; }

        // 切档(0~4 循环); 返回提示文本
        internal static string Cycle(int kind, int today)
        {
            try
            {
                if (kind < 0 || kind > 3) return "";
                Level[kind] = (Level[kind] + 1) % 5;
                ChangeDay = today;
                Recompute();
                DLog.Force("税制: " + Names[kind] + " -> " + LevelNames[Level[kind]]);
                return Names[kind] + ": " + LevelNames[Level[kind]];
            }
            catch { return ""; }
        }

        // 直接设档(点小格; v4.8)
        internal static string SetLevel(int kind, int level, int today)
        {
            try
            {
                if (kind < 0 || kind > 3) return "";
                if (level < 0) level = 0;
                if (level > 4) level = 4;
                Level[kind] = level;
                ChangeDay = today;
                Recompute();
                DLog.Force("税制: " + Names[kind] + " -> " + LevelNames[Level[kind]]);
                return Names[kind] + ": " + LevelNames[Level[kind]];
            }
            catch { return ""; }
        }

        internal static string LineOf(int kind)
        {
            try
            {
                return Names[kind] + ": " + LevelNames[Level[kind]] + " ×" + Mult[Level[kind]].ToString("F1")
                    + " · 本期 +" + MonthIncome[kind].ToString("N0");
            }
            catch { return ""; }
        }

        internal static string LabelOf(int kind)
        {
            try { return Names[kind]; } catch { return ""; }
        }

        // 当前选中档位(绿色显示; v4.3)
        internal static string LevelOf(int kind)
        {
            try { return LevelNames[Level[kind]] + " ×" + Mult[Level[kind]].ToString("F1"); }
            catch { return ""; }
        }

        internal static string EffectText()
        {
            try
            {
                return "税负效果: 激进 +" + (RadicalPressure * 1000f).ToString("F1") + "‰/日 · 工商 ×" + IndustryMult.ToString("F2")
                    + " · 吸引力 ×" + AttractMult.ToString("F2");
            }
            catch { return ""; }
        }

        private static int CurDay()
        {
            try { return (int)CampaignTime.Now.ToDays; } catch { return 0; }
        }

        // 日结算: 四税 -> 国库/教会池; 返回总额
        internal static int Daily()
        {
            int total = 0;
            try
            {
                var pk = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                if (pk == null) return 0;
                float landBase = 0f, pollBase = 0f, marketBase = 0f;
                foreach (var s in pk.Settlements)
                {
                    if (s == null) continue;
                    float tf = Politics.SettlementTaxFactor(s);   // 第 21 章: 愤怒领主抗命拒税
                    if (s.Town != null && s.IsTown) pollBase += s.Town.Prosperity * 0.05f * tf;
                    else if (s.Village != null) landBase += s.Village.Hearth * 0.02f * tf;
                }
                // 市场税: 本国市场当日成交量价值 × 5%
                var n = EconomyWorld.National;
                for (int i = 0; i < FeudalGoods.Main.Count; i++)
                {
                    var g = FeudalGoods.Main[i];
                    foreach (var kv in EconomyWorld.Markets)
                    {
                        var st = MarketSettlement(kv.Key);
                        if (st == null || st.MapFaction != pk) continue;
                        var e = kv.Value != null ? kv.Value.Get(g.Id) : null;
                        if (e == null) continue;
                        marketBase += e.DailyConsumption * n.PriceOf(g.Id) * 0.05f * Politics.SettlementTaxFactor(st);
                    }
                }
                float titheBase = (landBase + pollBase) * 0.4f;
                float[] bases = { landBase, pollBase, titheBase, marketBase };
                float pMult = Politics.TaxIncomeMult();   // 第 21 章: 法令特权/王权合法性影响实征率
                for (int i = 0; i < 4; i++)
                {
                    int v = (int)Math.Round(bases[i] * Mult[Level[i]] * pMult);
                    if (v <= 0) continue;
                    MonthIncome[i] += v;
                    total += v;
                    if (i == 2) Ownership.ChurchPool += v;            // 什一税 -> 教会池
                    else EconomyWorld.TreasuryAdd(v);                 // 其余 -> 国库
                }
                if (total > 0) Fiscal.AddTax(total);
            }
            catch (Exception ex) { DLog.Force("税制结算异常: " + ex.Message); }
            return total;
        }

        internal static void Month()
        {
            for (int i = 0; i < 4; i++) { LastIncome[i] = MonthIncome[i]; MonthIncome[i] = 0; }
        }

        private static bool IsPlayerMarket(string townId, Kingdom pk)
        {
            try
            {
                var s = MarketSettlement(townId);
                return s != null && s.MapFaction == pk;
            }
            catch { }
            return false;
        }

        private static TaleWorlds.CampaignSystem.Settlements.Settlement MarketSettlement(string townId)
        {
            try
            {
                foreach (var s in TaleWorlds.CampaignSystem.Settlements.Settlement.All)
                    if (s != null && s.StringId == townId) return s;
            }
            catch { }
            return null;
        }

        // ---- 存档 FIA_Tax ----
        internal static string Save()
        {
            return "v1;" + Level[0] + "," + Level[1] + "," + Level[2] + "," + Level[3] + ";" + ChangeDay + ";";
        }

        internal static void Load(string data)
        {
            try
            {
                if (string.IsNullOrEmpty(data)) { Recompute(); return; }
                var p = data.Split(';');
                if (p.Length >= 3)
                {
                    var lv = p[1].Split(',');
                    for (int i = 0; i < 4 && i < lv.Length; i++)
                    {
                        int v;
                        if (int.TryParse(lv[i], out v)) Level[i] = v < 0 ? 0 : (v > 4 ? 4 : v);
                    }
                    int d;
                    if (int.TryParse(p[2], out d)) ChangeDay = d;
                }
                Recompute();
                DLog.Force("税制: 读档 土=" + LevelNames[Level[0]] + " 人=" + LevelNames[Level[1]]
                    + " 什=" + LevelNames[Level[2]] + " 市=" + LevelNames[Level[3]]);
            }
            catch { }
        }
    }
}
