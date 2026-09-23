using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using TaleWorlds.CampaignSystem.Settlements;

namespace FeudalInternalAffairs
{
    // 一个 Pop = (工作场所定居点, 职业, 文化) 三元组唯一记录(文档 19.1.1)
    internal class PopRecord
    {
        internal string SettlementId;
        internal string Profession;
        internal string Culture;
        internal float Size;          // 总人数
        internal float Wealth;        // 人均财富(第纳尔) -> SoL = 取整
        internal float Literacy;      // 识字率 0~1
        internal float Radicalism;    // 激进度 0~1
        internal float Loyalty;       // 忠诚度 0~1
        internal float NeedShortfall; // 上期需求未满足比例(缓存, UI 与激进度用)
        internal float DividendMonthly; // 上月分红(人均; 摊到每天计入收入, 文档 19.7.4)

        internal int WealthLevel
        {
            get { int w = (int)Wealth; return w < 1 ? 1 : (w > 99 ? 99 : w); }
        }

        internal float Workforce { get { return Size * PopDefs.WorkforceRatio; } }
        internal float Dependents { get { return Size - Workforce; } }

        // 买包口径: (工作人口 + 依赖×0.5)/10000 × 消费系数
        internal float NeedUnitsScale
        {
            get
            {
                var p = PopDefs.Get(Profession);
                return (Workforce + Dependents * PopDefs.DependentRatio) / 10000f * p.ConsumeMult;
            }
        }

        internal string Key { get { return SettlementId + "|" + Profession + "|" + Culture; } }
    }

    // 全图 Pop 表: 按定居点分组 + 存档(文档 19.1 / 19.13.1)
    internal static class Pops
    {
        // 全局激进调整(税制/破产等; 文档 20.1/20.2); v4.147: 走 V3 三态立场(一次一步, 互斥)
        internal static void ShiftRadicals(float delta)
        {
            try
            {
                if (Math.Abs(delta) < 0.000001f) return;
                foreach (var kv in BySettlement)
                {
                    var l = kv.Value;
                    if (l == null) continue;
                    for (int i = 0; i < l.Count; i++)
                        ShiftStance(l[i], -delta);   // delta>0 = 增加激进 -> 向激进方向
                }
            }
            catch { }
        }
        internal static readonly Dictionary<string, List<PopRecord>> BySettlement = new Dictionary<string, List<PopRecord>>();
        internal static readonly Dictionary<string, PopRecord> Index = new Dictionary<string, PopRecord>();
        internal static bool Inited;

        // v4.133: 识字率月度增长(教育制度/平民权利/言论自由/科举 -> LawSystem.LiteracyMonthly)
        internal static void ShiftLiteracy(float delta)
        {
            try
            {
                if (delta == 0f) return;
                foreach (var kv in BySettlement)
                {
                    var l = kv.Value;
                    if (l == null) continue;
                    for (int i = 0; i < l.Count; i++)
                    {
                        var p = l[i];
                        if (p == null) continue;
                        p.Literacy += delta;
                        if (p.Literacy < 0f) p.Literacy = 0f;
                        if (p.Literacy > 1f) p.Literacy = 1f;
                    }
                }
            }
            catch { }
        }

        // ==================== v4.147: V3 官方 Pops 机制(忠诚/激进、预期生活水平、教育机会、同化、政治力量、资格) ====================

        // 立场变化(V3 官方: 忠诚/中立/激进三态, 变化一次一步, 且不能同时有忠诚与激进)
        //   delta > 0 向忠诚: 先把激进者转中立, 再把中立转忠诚; delta < 0 反之
        internal static void ShiftStance(PopRecord pop, float delta)
        {
            try
            {
                if (pop == null || Math.Abs(delta) < 0.000001f) return;
                if (delta > 0f)
                {
                    float toNeutral = pop.Radicalism < delta ? pop.Radicalism : delta;
                    pop.Radicalism -= toNeutral;
                    delta -= toNeutral;
                    pop.Loyalty += delta;
                    if (pop.Loyalty > 1f) pop.Loyalty = 1f;
                }
                else
                {
                    float d = -delta;
                    float toNeutral = pop.Loyalty < d ? pop.Loyalty : d;
                    pop.Loyalty -= toNeutral;
                    d -= toNeutral;
                    pop.Radicalism += d;
                    if (pop.Radicalism > 1f) pop.Radicalism = 1f;
                }
                if (pop.Radicalism < 0f) pop.Radicalism = 0f;
                if (pop.Loyalty < 0f) pop.Loyalty = 0f;
            }
            catch { }
        }

        // 预期生活水平(V3 官方: expected SoL 随识字率与社会科技上升; 低于预期会随时间变激进)
        internal static float ExpectedSolOf(PopRecord pop)
        {
            try
            {
                var def = PopDefs.Get(pop.Profession);
                float lit = 1f + pop.Literacy * 0.5f;                 // 识字率驱动
                float tech = 1f;
                try { tech = 1f + 0.02f * Research.Done.Count; } catch { }   // 每项已完成科技 +2%
                return def.ExpectedSol * lit * tech;
            }
            catch { return 5f; }
        }

        // 教育机会(V3 官方: 基础 +0.5% 每财富; 职业固有加成; 教育机构/学校法/科技)
        internal static float EducationAccessOf(PopRecord pop)
        {
            try
            {
                float acc = 0.005f * pop.WealthLevel;
                switch (pop.Profession)
                {
                    case PopDefs.Clerks:
                    case PopDefs.Engineers: acc *= 1.25f; break;     // V3 官方: +25%
                    case PopDefs.Academics:
                    case PopDefs.Bureaucrats:
                    case PopDefs.Clergymen: acc *= 1.50f; break;     // V3 官方: +50%
                }
                int edu = 0; try { edu = Institutions.Level[0]; } catch { }
                acc *= 1f + 0.125f * edu;                             // V3 官方: 公立学校 +12.5%/级
                try { acc *= LawSystem.EducationAccessMult(); } catch { }   // v4.148 V3 官方: 学校法(教会 +10% / 公立 +12.5%)
                try { acc += Research.LiteracyBonus(); } catch { }          // 社会科技提升教育机会(V3 官方: 理性主义/学术界/经验主义)
                if (acc > 0.95f) acc = 0.95f;
                return acc;
            }
            catch { return 0.05f; }
        }

        // 政治力量(V3 官方: 主要基于财富水平, 职业为基础)
        internal static float PoliticalStrengthOf(PopRecord pop)
        {
            try
            {
                var def = PopDefs.Get(pop.Profession);
                return def.Political * (0.5f + pop.WealthLevel / 20f);
            }
            catch { return 0f; }
        }

        // 资格(V3 官方: 资格基于识字率、财富与歧视; 这里用派生值, 不做逐职业资格表)
        internal static float QualificationOf(PopRecord pop)
        {
            try
            {
                float w = pop.WealthLevel / 30f;
                if (w > 1f) w = 1f;
                return pop.Literacy * 0.6f + w * 0.4f;
            }
            catch { return 0f; }
        }

        // 识字率向教育机会靠拢(V3 官方: 高于教育机会时识字者死亡导致缓慢下降)
        //   v4.148: 速率按长期演化校准(每周靠拢 2%, 约 10 年接近教育机会; 下降 0.5%/周)
        internal static void EducationWeekly()
        {
            try
            {
                foreach (var kv in BySettlement)
                {
                    var l = kv.Value;
                    if (l == null) continue;
                    for (int i = 0; i < l.Count; i++)
                    {
                        var p = l[i];
                        if (p == null || p.Size < 1f) continue;
                        float access = EducationAccessOf(p);
                        if (p.Literacy < access) p.Literacy += (access - p.Literacy) * 0.02f;   // 上升: 每周 2%
                        else p.Literacy -= (p.Literacy - access) * 0.001f;                     // 下降: 每周 0.1%(识字者死亡, 需一代人)
                        if (p.Literacy < 0f) p.Literacy = 0f;
                        if (p.Literacy > 1f) p.Literacy = 1f;
                    }
                }
            }
            catch { }
        }

        // 同化(V3 官方: 基础 0.2%/月; 政策与教育机构修正; 激进者减少; 只在有更被接纳文化时发生)
        internal static void AssimilateWeekly()
        {
            try
            {
                foreach (var kv in BySettlement)
                {
                    var l = kv.Value;
                    if (l == null || l.Count < 2) continue;
                    string dom = null; float best = 0f;
                    for (int i = 0; i < l.Count; i++)
                    {
                        var p = l[i];
                        if (p == null) continue;
                        float s = 0f;
                        for (int j = 0; j < l.Count; j++) { var q = l[j]; if (q != null && q.Culture == p.Culture) s += q.Size; }
                        if (s > best) { best = s; dom = p.Culture; }
                    }
                    if (dom == null) continue;
                    // 政策乘数(对应 V3 官方: 文化抹除 +5% / 公开偏见 +15% / 促进民族价值观 +50%)
                    float pol = 1f;
                    int cp = 0; try { cp = Institutions.CulturePolicy; } catch { }
                    if (cp == 0) pol = 0.65f; else if (cp == 1) pol = 0.9f; else if (cp == 2) pol = 1.5f;
                    int edu = 0; try { edu = Institutions.Level[0]; } catch { }
                    float eduM = 1f + 0.125f * edu;                       // V3 官方: 公立学校 ×(1+12.5%/级)
                    for (int i = 0; i < l.Count; i++)
                    {
                        var p = l[i];
                        if (p == null || p.Size < 10f || p.Culture == dom) continue;
                        // v4.186: 该文化自己的法律决定同化速度(文档 24.13); 旧全局政策仅兜底
                        float pol2 = pol;
                        try { pol2 = CultureSystem.AssimOf(p.Culture); } catch { }
                        float rate = 0.002f * pol2 * eduM * (1f - p.Radicalism);   // V3 官方基础 0.2%/月
                        float move = p.Size * rate;
                        if (move < 0.5f) continue;
                        p.Size -= move;
                        var r = GetOrCreate(p.SettlementId, p.Profession, dom);
                        if (r != null) { r.Size += move; r.Wealth = p.Wealth; r.Literacy = p.Literacy; }
                    }
                }
            }
            catch { }
        }

        // ---- 查询 ----
        internal static List<PopRecord> Of(string settlementId)
        {
            List<PopRecord> l;
            return (settlementId != null && BySettlement.TryGetValue(settlementId, out l)) ? l : null;
        }

        internal static float TotalPopulation()
        {
            float t = 0f;
            foreach (var kv in BySettlement) { var l = kv.Value; if (l == null) continue; for (int i = 0; i < l.Count; i++) t += l[i].Size; }
            return t;
        }

        internal static int Count
        {
            get
            {
                int n = 0;
                foreach (var kv in BySettlement) { var l = kv.Value; if (l != null) n += l.Count; }
                return n;
            }
        }

        internal static PopRecord GetOrCreate(string settlementId, string profession, string culture)
        {
            if (string.IsNullOrEmpty(settlementId)) return null;
            List<PopRecord> l;
            if (!BySettlement.TryGetValue(settlementId, out l)) { l = new List<PopRecord>(); BySettlement[settlementId] = l; }
            var key = settlementId + "|" + profession + "|" + culture;
            PopRecord r;
            if (Index.TryGetValue(key, out r)) return r;
            r = new PopRecord { SettlementId = settlementId, Profession = profession, Culture = culture };
            l.Add(r);
            Index[key] = r;
            return r;
        }

        // ---- 初始化(文档 19.1.2) ----
        internal static void EnsureInit(string source)
        {
            try
            {
                if (Inited) return;
                int towns = 0, castles = 0, villages = 0;
                foreach (var s in Settlement.All)
                {
                    if (s == null || string.IsNullOrEmpty(s.StringId)) continue;
                    if (!s.IsTown && !s.IsCastle && !s.IsVillage) continue;   // 跳过藏身处等非定居点(否则会白吃最近城镇的粮)
                    if (BySettlement.ContainsKey(s.StringId)) continue;   // 已有(旧存档补齐时跳过)
                    string culture = s.Culture != null ? s.Culture.StringId : "empire";
                    float size = 0f;
                    if (s.IsTown) size = s.Town != null ? s.Town.Prosperity * 3f : 600f;
                    else if (s.IsCastle) size = s.Town != null ? s.Town.Prosperity * 1.5f : 400f;
                    else if (s.IsVillage) size = (s.Village != null ? s.Village.Hearth : 400f) * 4f;
                    if (size < 10f) size = 10f;

                    if (s.IsVillage) { InitVillage(s, culture, size); villages++; }
                    else if (s.IsCastle) { InitCompo(s, culture, size, false); castles++; }
                    else { InitCompo(s, culture, size, true); towns++; }
                }
                Inited = true;
                DLog.Force("人口: 初始化完成(" + source + ") 城=" + towns + " 堡=" + castles + " 村=" + villages
                    + " Pop记录=" + Count + " 总人口=" + ((int)TotalPopulation()));
            }
            catch (Exception ex) { DLog.Force("人口初始化失败: " + ex); }
        }

        private static void InitVillage(Settlement s, string culture, float size)
        {
            var r = GetOrCreate(s.StringId, PopDefs.Peasants, culture);
            if (r == null) return;
            r.Size = size;
            r.Wealth = PopDefs.Get(PopDefs.Peasants).ExpectedSol + (s.Village != null ? s.Village.Hearth / 1000f : 0f);
            r.Literacy = 0.03f;
            r.Loyalty = 0.5f;
        }

        // 城镇/城堡职业构成(文档 19.1.2; 城镇与城堡比例不同)
        private static readonly string[] TownProf = { PopDefs.Laborers, PopDefs.Machinists, PopDefs.Engineers, PopDefs.Clerks, PopDefs.Shopkeepers, PopDefs.Bureaucrats, PopDefs.Academics, PopDefs.Clergymen, PopDefs.Aristocrats, PopDefs.Soldiers, PopDefs.Officers };
        private static readonly float[] TownShare = { 0.60f, 0.08f, 0.04f, 0.06f, 0.05f, 0.03f, 0.02f, 0.03f, 0.02f, 0.03f, 0.04f };
        private static readonly float[] CastleShare = { 0.63f, 0.05f, 0.02f, 0.02f, 0.03f, 0.02f, 0.01f, 0.02f, 0.02f, 0.12f, 0.06f };

        private static void InitCompo(Settlement s, string culture, float size, bool town)
        {
            var shares = town ? TownShare : CastleShare;
            float pros = s.Town != null ? s.Town.Prosperity : 0f;
            for (int i = 0; i < TownProf.Length; i++)
            {
                if (shares[i] <= 0f) continue;
                var r = GetOrCreate(s.StringId, TownProf[i], culture);
                if (r == null) continue;
                r.Size = size * shares[i];
                var def = PopDefs.Get(TownProf[i]);
                r.Wealth = def.ExpectedSol + pros / 1000f * 2f;          // 富裕城镇 SoL 起点更高
                r.Literacy = 0.08f + def.MinLiteracy * 0.5f;             // v4.148: 初始识字率贴近教育机会(避免开局大幅下降)
                r.Loyalty = 0.5f;
            }
        }

        // ---- 存档(文档 19.13.1; 一行一个 Pop) ----
        internal static string Save()
        {
            try
            {
                var sb = new StringBuilder();
                foreach (var kv in BySettlement)
                {
                    var l = kv.Value;
                    if (l == null) continue;
                    for (int i = 0; i < l.Count; i++)
                    {
                        var r = l[i];
                        if (r == null || r.Size < 0.5f) continue;
                        sb.Append(r.SettlementId).Append('|').Append(r.Profession).Append('|').Append(r.Culture).Append('|')
                          .Append(F(r.Size, 1)).Append('|').Append(F(r.Wealth, 2)).Append('|').Append(F(r.Literacy, 3)).Append('|')
                          .Append(F(r.Radicalism, 3)).Append('|').Append(F(r.Loyalty, 3)).Append('|').Append(F(r.NeedShortfall, 3))
                          .Append('\n');
                    }
                }
                return sb.ToString();
            }
            catch (Exception ex) { DLog.Force("人口存档失败: " + ex.Message); return ""; }
        }

        internal static void Load(string data)
        {
            try
            {
                BySettlement.Clear();
                Index.Clear();
                if (!string.IsNullOrEmpty(data))
                {
                    var lines = data.Split('\n');
                    for (int i = 0; i < lines.Length; i++)
                    {
                        var ln = lines[i];
                        if (string.IsNullOrEmpty(ln)) continue;
                        var f = ln.Trim().Split('|');
                        if (f.Length < 9) continue;
                        var r = GetOrCreate(f[0], f[1], f[2]);
                        if (r == null) continue;
                        r.Size = PF(f[3], 0f);
                        r.Wealth = PF(f[4], 5f);
                        r.Literacy = PF(f[5], 0.1f);
                        r.Radicalism = PF(f[6], 0f);
                        r.Loyalty = PF(f[7], 0.5f);
                        r.NeedShortfall = PF(f[8], 0f);
                    }
                }
                Inited = BySettlement.Count > 0;
                DLog.Force("人口: 读档完成 Pop记录=" + Count + " 总人口=" + ((int)TotalPopulation()));
            }
            catch (Exception ex) { DLog.Force("人口读档失败: " + ex); }
        }

        private static string F(float v, int dp)
        {
            return v.ToString("F" + dp, CultureInfo.InvariantCulture);
        }

        private static float PF(string s, float def)
        {
            float v;
            return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : def;
        }
    }
}
