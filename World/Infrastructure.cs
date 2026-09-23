using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem.Settlements;

namespace FeudalInternalAffairs
{
    // v4.163: 基础设施与市场准入(第 26 章, V3 官方公式)
    //   基建 = 3(基础) + 人口/10万×0.1(上限+20) + 铁路建筑×20 + 铁路线按等级 20/25/30/40 + 贸易站/市场×5 + 市政厅×1
    //   用量 = Σ 建筑等级 × 分类系数(铁路/城市中心不消耗)
    //   市场准入 = min(100%, 基建/用量) -> 建筑产出乘数(V3: 买卖单按准入缩减)
    //   v4.21x: 线路基建改走 Railways.InfraOf(单一口径), 市场准入/动员天数等 API 均复用此处
    internal static class Infrastructure
    {
        // ================= 基建 =================
        internal static float BaseOf(string sid)
        {
            float v = 3f;
            try
            {
                if (string.IsNullOrEmpty(sid)) return v;
                // 人口: 每 10 万 +0.1, 上限 +20(V3 官方)
                float pop = 0f;
                var pops = Pops.Of(sid);
                if (pops != null)
                {
                    for (int i = 0; i < pops.Count; i++)
                    {
                        var p = pops[i];
                        if (p != null) pop += p.Size;
                    }
                }
                v += Math.Min(20f, pop / 100000f * 0.1f);

                // 建筑: @infra 效果 + 贸易站/市场 +5 + 市政厅 +1
                var sb = EconomyWorld.Of(sid);
                if (sb != null)
                {
                    for (int i = 0; i < sb.Groups.Count; i++)
                    {
                        var g = sb.Groups[i];
                        if (g == null || g.Count <= 0) continue;
                        var def = BuildDefs.Get(g.DefId);
                        if (def == null) continue;
                        float infra = EffectValue(def, BuildDefs.EffInfra);
                        if (infra > 0f) v += infra * g.Count;   // 铁路每级 +20
                        string id = g.DefId ?? "";
                        if (id.IndexOf("tradepost", StringComparison.OrdinalIgnoreCase) >= 0
                            || id.IndexOf("market", StringComparison.OrdinalIgnoreCase) >= 0)
                            v += 5f * g.Count;                  // 港口/贸易中心(V3 港口 +5/级)
                        if (id.IndexOf("townhall", StringComparison.OrdinalIgnoreCase) >= 0)
                            v += 1f * g.Count;                  // 城市中心 +1/级
                    }
                }

                // 铁路线(运营中): 按线路等级 20/25/30/40(映射 V3 列车 PM 升级)
                try { v += Railways.InfraOf(sid); } catch { }
            }
            catch { }
            return v;
        }

        // ================= 用量 =================
        internal static float UsageOf(string sid)
        {
            float v = 0f;
            try
            {
                if (string.IsNullOrEmpty(sid)) return v;
                var sb = EconomyWorld.Of(sid);
                if (sb == null) return v;
                for (int i = 0; i < sb.Groups.Count; i++)
                {
                    var g = sb.Groups[i];
                    if (g == null || g.Count <= 0) continue;
                    var def = BuildDefs.Get(g.DefId);
                    if (def == null) continue;
                    switch (def.Cat)
                    {
                        case BuildCat.Resource: v += 1.5f * g.Count; break;   // 农业/采掘
                        case BuildCat.Industry: v += 2f * g.Count; break;     // 加工/重工
                        case BuildCat.Military: v += 0.5f * g.Count; break;   // 军事
                        case BuildCat.Admin: v += 1f * g.Count; break;        // 行政/大学
                        case BuildCat.Trade: v += 1.5f * g.Count; break;      // 贸易
                        case BuildCat.Logistics: v += 0.5f * g.Count; break;  // 后勤(铁路自身按 0 计 -> 见下)
                        case BuildCat.Living: v += 0.5f * g.Count; break;     // 民生
                    }
                    // V3 官方: 铁路与城市中心不消耗基建
                    string id = g.DefId ?? "";
                    if (id.IndexOf("railway", StringComparison.OrdinalIgnoreCase) >= 0)
                        v -= 0.5f * g.Count;
                    if (id.IndexOf("townhall", StringComparison.OrdinalIgnoreCase) >= 0)
                        v -= 0.5f * g.Count;
                }
            }
            catch { }
            return v < 0f ? 0f : v;
        }

        // ================= 市场准入(0~1) =================
        internal static float AccessOf(string sid)
        {
            try
            {
                float use = UsageOf(sid);
                if (use <= 0.01f) return 1f;
                float a = BaseOf(sid) / use;
                if (a > 1f) a = 1f;
                if (a < 0.1f) a = 0.1f;   // 最低 10%(避免完全锁死)
                return a;
            }
            catch { return 1f; }
        }

        // ================= UI =================
        internal static string StatusText(string sid)
        {
            try
            {
                float b = BaseOf(sid);
                float u = UsageOf(sid);
                // 复用已算出的基建/用量, 不再调 AccessOf(其内部会重算两者)
                float a = u <= 0.01f ? 1f : Math.Min(1f, Math.Max(0.1f, b / u));
                int rail = 0;
                try { rail = Railways.InfraOf(sid); } catch { }
                int prod = 0, cons = 0;
                try { Railways.TransportOf(sid, out prod, out cons); } catch { }
                int mob = 0;
                try { mob = Railways.MobilizationDays(sid); } catch { }
                return "基建 " + (int)b + " / 用量 " + (int)u + " · 市场准入 " + (int)Math.Round(a * 100f) + "%"
                    + " · 铁路基建 " + rail + " · 运力 " + prod + "/" + cons + "(余 " + (prod - cons) + ")"
                    + " · 动员 " + mob + " 天";
            }
            catch { return "—"; }
        }

        private static float EffectValue(BuildDef def, string effId)
        {
            try
            {
                if (def == null || def.Outputs == null) return 0f;
                for (int i = 0; i < def.Outputs.Count; i++)
                {
                    var o = def.Outputs[i];
                    if (o == null || o.Good != effId) continue;
                    return o.V != null && o.V.Length > 0 ? o.V[0] : 0f;
                }
            }
            catch { }
            return 0f;
        }
    }
}
