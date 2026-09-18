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
        // 全局激进调整(税制/破产等; 文档 20.1/20.2)
        internal static void ShiftRadicals(float delta)
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
                        if (p == null) continue;
                        p.Radicalism += delta;
                        if (p.Radicalism < 0f) p.Radicalism = 0f;
                        if (p.Radicalism > 1f) p.Radicalism = 1f;
                    }
                }
            }
            catch { }
        }
        internal static readonly Dictionary<string, List<PopRecord>> BySettlement = new Dictionary<string, List<PopRecord>>();
        internal static readonly Dictionary<string, PopRecord> Index = new Dictionary<string, PopRecord>();
        internal static bool Inited;

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
            r.Literacy = 0.05f;
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
                r.Literacy = 0.1f + (def.MinLiteracy > 0.1f ? def.MinLiteracy : 0.1f);
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
