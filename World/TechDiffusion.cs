using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;

namespace FeudalInternalAffairs
{
    // 科技扩散(文档 28.10 B, 低频): 每 42 天(半年)判定一次;
    //   对每国每项未完成科技(他国已完成): 概率 = 12% × 接触系数 × 差距系数 × 识字系数, 上限 40%;
    //   触发后给该科技总成本的 8%~15%(差距越大给得越多); 接触矩阵按月结缓存。
    //   一次性加速: 缴获装备 +20 / 占领城市 +50 / 技术条约 +80。
    internal static class TechDiffusion
    {
        internal const int CheckInterval = 42;      // 半年(骑砍 84 天/年)
        internal const float BaseChance = 0.12f;
        internal const float MaxChance = 0.40f;
        internal static int LastCheckDay = -1;      // 由 Research 存档(FIA_Tech)携带
        internal static int LastTriggerCount;       // 上轮触发数(展示/诊断)

        private static readonly Dictionary<string, float> ContactCache = new Dictionary<string, float>();
        private static int _contactCacheMonth = -9999;

        internal static void ClearCache()
        {
            ContactCache.Clear();
            _contactCacheMonth = -9999;
            LastCheckDay = -1;
            LastTriggerCount = 0;
        }

        // 日结入口(由 Research.Daily 调用): 每 42 天判定一次
        internal static void Daily(int day)
        {
            try
            {
                if (LastCheckDay < 0) { LastCheckDay = day; return; }   // 开局/读档后从当前日起算
                if (day - LastCheckDay < CheckInterval) return;
                LastCheckDay = day;
                Run();
            }
            catch (Exception ex) { DLog.Force("科技扩散异常: " + ex.Message); }
        }

        private static void Run()
        {
            var kingdoms = new List<Kingdom>();
            foreach (var k in Kingdom.All) if (k != null && !k.IsEliminated) kingdoms.Add(k);
            if (kingdoms.Count < 2) { LastTriggerCount = 0; return; }

            // 已完成索引: techId -> 已完成的王国
            var byTech = new Dictionary<string, List<Kingdom>>();
            for (int i = 0; i < kingdoms.Count; i++)
            {
                var nat = Research.Of(kingdoms[i]);
                foreach (var id in nat.Done)
                {
                    List<Kingdom> list;
                    if (!byTech.TryGetValue(id, out list)) { list = new List<Kingdom>(); byTech[id] = list; }
                    list.Add(kingdoms[i]);
                }
            }

            int triggers = 0;
            string pkey = Research.PlayerKey();
            for (int i = 0; i < kingdoms.Count; i++)
            {
                var k = kingdoms[i];
                var nat = Research.Of(k);
                bool isPlayer = Research.KeyOf(k) == pkey;
                float lit = 0.6f + 0.4f * LiteracyRateOf(k);
                try
                {
                    for (int t = 0; t < Research.All.Count; t++)
                    {
                        var tech = Research.All[t];
                        if (tech == null || nat.Done.Contains(tech.Id)) continue;
                        List<Kingdom> srcs;
                        if (!byTech.TryGetValue(tech.Id, out srcs) || srcs.Count == 0) continue;

                        // 取最优来源: 接触系数(含同文化 +0.15), 同分取更高差距
                        float bestContact = 0f; float srcEra = 0f;
                        Kingdom bestSrc = null;
                        for (int s = 0; s < srcs.Count; s++)
                        {
                            var src = srcs[s];
                            if (src == null || ReferenceEquals(src, k)) continue;
                            float contact = ContactBase(k, src);
                            if (SameCulture(k, src)) contact += 0.15f;
                            if (contact <= 0f) continue;
                            float era = MaxEraOf(src, tech.Tree);
                            if (contact > bestContact || (Math.Abs(contact - bestContact) < 0.0001f && era > srcEra))
                            {
                                bestContact = contact; bestSrc = src; srcEra = era;
                            }
                        }
                        if (bestSrc == null) continue;

                        float gap = (float)Math.Min(2.0, 1.0 + 0.2 * (srcEra - MaxEraOf(k, tech.Tree)));
                        if (gap < 0.2f) gap = 0.2f;
                        float chance = BaseChance * bestContact * gap * lit;
                        if (chance > MaxChance) chance = MaxChance;
                        if (TaleWorlds.Core.MBRandom.RandomFloat >= chance) continue;

                        // 触发: 给总成本的 8%~15%(差距越大给得越多)
                        float frac = 0.08f + 0.07f * Clamp01((gap - 1f) / 1.0f);
                        float gain = Research.CostOf(k, tech) * frac;
                        bool done = Research.AddProgress(k, tech.Id, gain);
                        nat.SpreadFrom[tech.Id] = bestSrc.StringId;   // 保留来源(UI 展示; 自行完成时由 Complete 清除)
                        triggers++;
                        if (isPlayer)
                            InterestGroups.NotifyPlayer("技术扩散: " + tech.Name + " ← " + bestSrc.Name
                                + " (+" + (int)gain + "/" + (int)Research.CostOf(k, tech) + ")", false);
                        DLog.Force("科技扩散: " + Research.KeyOf(k) + " <- " + Research.KeyOf(bestSrc) + " " + tech.Id
                            + " +" + gain.ToString("F1") + (done ? " 完成" : "") + " (接触 " + bestContact.ToString("F2")
                            + " 差距 " + gap.ToString("F2") + " 概率 " + chance.ToString("F2") + ")");
                    }
                }
                catch (Exception ex) { DLog.Force("科技扩散(" + Research.KeyOf(k) + ")异常: " + ex.Message); }
            }
            LastTriggerCount = triggers;
            if (triggers > 0) DLog.Force("科技扩散: 第 " + LastCheckDay + " 天判定, 本轮触发 " + triggers + " 项");
        }

        // 接触系数(不含同文化加成): 同盟或条约 1.0 ｜ 贸易路线 0.7 ｜ 接壤(未交战) 0.5 ｜ 交战 0.3
        // 按月结缓存(接触矩阵)
        private static float ContactBase(Kingdom a, Kingdom b)
        {
            string ck;
            try
            {
                int today = Politics.Today();
                int month = today / 7;
                if (_contactCacheMonth != month) { ContactCache.Clear(); _contactCacheMonth = month; }
                ck = Research.KeyOf(a) + "|" + Research.KeyOf(b);
                float cached;
                if (ContactCache.TryGetValue(ck, out cached)) return cached;
                float v = ComputeContact(a, b);
                ContactCache[ck] = v;
                return v;
            }
            catch { return 0f; }
        }

        private static float ComputeContact(Kingdom a, Kingdom b)
        {
            try
            {
                bool war = a.IsAtWarWith(b);
                bool playerInvolved = ReferenceEquals(a, Research.PlayerKingdom()) || ReferenceEquals(b, Research.PlayerKingdom());
                if (!war)
                {
                    if (Diplomacy.IsAlly(a, b)) return 1.0f;   // 同盟
                    if (playerInvolved && (Treaties.Has(b, "ally") || Treaties.Has(b, "trade")
                        || Treaties.Has(b, "invest") || Treaties.Has(b, "nap"))) return 1.0f;   // 条约
                    if (TradeLink(a, b)) return 0.7f;           // 贸易路线
                    if (AiDiplomacy.Borders(a, b)) return 0.5f; // 接壤未交战
                    return 0f;
                }
                return 0.3f;   // 交战(占领/缴获也会扩散)
            }
            catch { return 0f; }
        }

        private static bool TradeLink(Kingdom a, Kingdom b)
        {
            try
            {
                var pk = Research.PlayerKingdom();
                Kingdom other = ReferenceEquals(a, pk) ? b : (ReferenceEquals(b, pk) ? a : null);
                if (other == null || other.StringId == null) return false;
                for (int i = 0; i < TradeRoutes.Routes.Count; i++)
                {
                    var r = TradeRoutes.Routes[i];
                    if (r != null && r.PartnerId == other.StringId) return true;
                }
            }
            catch { }
            return false;
        }

        internal static bool SameCulture(Kingdom a, Kingdom b)
        {
            try
            {
                return a != null && b != null && a.Culture != null && b.Culture != null
                    && a.Culture.StringId == b.Culture.StringId;
            }
            catch { return false; }
        }

        // 该国该树(0 生产/1 军事/2 社会)已完成科技的最高时代层级
        internal static float MaxEraOf(Kingdom k, int tree)
        {
            try
            {
                var nat = Research.Of(k);
                int best = 0;
                for (int i = 0; i < Research.All.Count; i++)
                {
                    var t = Research.All[i];
                    if (t == null || t.Tree != tree) continue;
                    if (nat.Done.Contains(t.Id) && t.Era > best) best = t.Era;
                }
                return best;
            }
            catch { return 0f; }
        }

        // 本国识字率(0~1): 按本国定居点聚合人口表; 无数据回退 5%
        internal static float LiteracyRateOf(Kingdom k)
        {
            try
            {
                if (k == null) return 0.05f;
                var ids = new HashSet<string>();
                foreach (var s in k.Settlements) if (s != null && s.StringId != null) ids.Add(s.StringId);
                float num = 0f, den = 0f;
                foreach (var kv in Pops.BySettlement)
                {
                    if (!ids.Contains(kv.Key)) continue;
                    var l = kv.Value;
                    if (l == null) continue;
                    for (int i = 0; i < l.Count; i++)
                    {
                        var p = l[i];
                        if (p == null || p.Size < 0.5f) continue;
                        num += p.Literacy * p.Size; den += p.Size;
                    }
                }
                if (den > 0f) return num / den;
            }
            catch { }
            return 0.05f;
        }

        private static float Clamp01(float v) { return v < 0f ? 0f : (v > 1f ? 1f : v); }

        // ================= 一次性加速(不掷骰, 直接给点; 文档 28.10 B) =================
        internal static string CaptureBoost(Kingdom k, string techId) { return Boost(k, techId, 20f, "缴获装备"); }
        internal static string CityCaptureBoost(Kingdom k, string techId) { return Boost(k, techId, 50f, "占领城市"); }
        internal static string TreatyBoost(Kingdom k, string techId) { return Boost(k, techId, 80f, "技术条约"); }

        private static string Boost(Kingdom k, string techId, float n, string why)
        {
            try
            {
                var t = Research.Find(techId);
                if (t == null) return "";
                bool done = Research.AddProgress(k, techId, n);
                DLog.Force("科技扩散: " + why + " " + Research.KeyOf(k) + " " + techId + " +" + (int)n + (done ? " 完成" : ""));
                return why + ": " + t.Name + " +" + (int)n + (done ? "(完成)" : "");
            }
            catch { return ""; }
        }
    }
}
