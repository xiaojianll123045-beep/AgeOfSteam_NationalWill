using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;

namespace FeudalInternalAffairs
{
    // 一部文化法律(文档 24.13; v4.186 实装)
    //   效果口径: 同化倍率 / 迁移吸引 / 满意度(->忠诚/日) / 税收倍率 / 教会态度
    internal class CultureLawDef
    {
        internal string Id;
        internal string Name;
        internal string Desc;
        internal string Effect;
        internal float Assim = 1f;      // 同化速度倍率
        internal float Attract = 0f;    // 迁移吸引加成(绝对值, 直接加进 Attraction)
        internal float Satis = 0f;      // 满意度(每 +10 = 忠诚 +0.1/日)
        internal float Tax = 0f;        // 税收倍率加成
        internal int[] Att;             // IG 态度变化[8]: 王权/权贵/地方/教会/商帮/行会/农民/军队
    }

    // 文化系统(文档 24.13): 每个文化一部法律, 文化取自原版(适配任意地图)
    internal static class CultureSystem
    {
        internal const int ChangeCooldown = 90;      // 改法冷却(日, 照 V3 法律 90 日)

        internal static readonly List<CultureLawDef> Laws = new List<CultureLawDef>
        {
            new CultureLawDef
            {
                Id = "tolerance", Name = "宽容", Desc = "默认状态: 各族共处, 缓慢融合。",
                Effect = "同化 ×0.65, 迁移吸引 +10, 满意度 +5, 税收 ×1.00",
                Assim = 0.65f, Attract = 10f, Satis = 5f, Tax = 0f,
                Att = new[] { 0, 0, 0, 0, 0, 0, 0, 0 }
            },
            new CultureLawDef
            {
                Id = "assimilation", Name = "同化", Desc = "强制推广主体文化, 少数族裔不满。",
                Effect = "同化 ×1.50, 迁移吸引 -10, 满意度 -8, 税收 ×1.05",
                Assim = 1.5f, Attract = -10f, Satis = -8f, Tax = 0.05f,
                Att = new[] { 4, 2, 4, -6, -4, -2, -8, 6 }
            },
            new CultureLawDef
            {
                Id = "multicultural", Name = "多元", Desc = "承认各族习俗与语言, 融合极慢但人心安定。",
                Effect = "同化 ×0.30, 迁移吸引 +20, 满意度 +12, 税收 ×0.98",
                Assim = 0.3f, Attract = 20f, Satis = 12f, Tax = -0.02f,
                Att = new[] { -4, 0, -2, 4, 8, 6, 10, -2 }
            },
            new CultureLawDef
            {
                Id = "state_church", Name = "国教", Desc = "立主体文化信仰为国教, 教会得势, 异教受压。",
                Effect = "同化 ×1.20, 迁移吸引 0, 主体 +8 / 异文化 -10 满意度, 税收 ×1.03",
                Assim = 1.2f, Attract = 0f, Satis = 8f, Tax = 0.03f,
                Att = new[] { 6, 2, -2, 14, -4, -2, -6, 4 }
            }
        };

        // 文化 StringId -> 法律 Id(未设置 = 宽容)
        private static readonly Dictionary<string, string> Law = new Dictionary<string, string>();
        private static readonly Dictionary<string, int> ChangedDay = new Dictionary<string, int>();
        private static bool _migrated;

        internal static CultureLawDef Def(string lawId)
        {
            for (int i = 0; i < Laws.Count; i++) if (Laws[i].Id == lawId) return Laws[i];
            return Laws[0];
        }

        internal static string LawOf(string culture)
        {
            if (string.IsNullOrEmpty(culture)) return "tolerance";
            string id;
            return Law.TryGetValue(culture, out id) && !string.IsNullOrEmpty(id) ? id : "tolerance";
        }

        internal static string LawNameOf(string culture) { return Def(LawOf(culture)).Name; }

        // 改法冷却提示(UI)
        internal static string CooldownTextOf(string culture)
        {
            try
            {
                int last;
                if (!string.IsNullOrEmpty(culture) && ChangedDay.TryGetValue(culture, out last))
                {
                    int left = ChangeCooldown - (Politics.Today() - last);
                    if (left > 0) return "改法冷却: 还需 " + left + " 日";
                }
            }
            catch { }
            return "可以改法";
        }

        internal static float AssimOf(string culture) { return Def(LawOf(culture)).Assim; }

        internal static float AttractOf(string culture) { return Def(LawOf(culture)).Attract; }

        internal static float SatisOf(string culture) { return Def(LawOf(culture)).Satis; }

        internal static float TaxOf(string culture) { return Def(LawOf(culture)).Tax; }

        // 异文化在本文化国教下额外扣满意度(教会法)
        internal static float SatisFor(string lawCulture, string popCulture)
        {
            var d = Def(LawOf(lawCulture));
            if (d.Id == "state_church" && lawCulture != popCulture) return -10f;
            return d.Satis;
        }

        // 改法: 冷却 + IG 反应 + 日志
        internal static string SetLaw(string culture, string lawId)
        {
            try
            {
                if (string.IsNullOrEmpty(culture)) return "文化无效。";
                var def = Def(lawId);
                if (LawOf(culture) == def.Id) return "已经是「" + def.Name + "」。";
                int today = Politics.Today();
                int last;
                if (ChangedDay.TryGetValue(culture, out last) && today - last < ChangeCooldown)
                    return "该文化刚改过法律, 还需 " + (ChangeCooldown - (today - last)) + " 日。";
                Law[culture] = def.Id;
                ChangedDay[culture] = today;
                try
                {
                    if (def.Att != null)
                        for (int g = 0; g < def.Att.Length && g < InterestGroups.GroupCount; g++)
                            if (def.Att[g] != 0) InterestGroups.AddEventMod(g, def.Att[g]);
                }
                catch { }
                string nm = NameOf(culture);
                DLog.Force("文化法律: " + nm + " -> " + def.Name);
                return nm + " 的文化法律已改为「" + def.Name + "」。";
            }
            catch (Exception ex) { return "改法失败: " + ex.Message; }
        }

        internal static string NameOf(string culture)
        {
            try
            {
                var c = CultureOf(culture);
                if (c != null && c.Name != null) return c.Name.ToString();
            }
            catch { }
            return string.IsNullOrEmpty(culture) ? "?" : culture;
        }

        internal static CultureObject CultureOf(string id)
        {
            try
            {
                if (string.IsNullOrEmpty(id)) return null;
                return Game.Current.ObjectManager.GetObject<CultureObject>(id);
            }
            catch { return null; }
        }

        // 我方王国境内的文化构成(人口/SoL/满意度/忠诚/激进/法律)
        internal class CultureRow
        {
            internal string Id;
            internal string Name;
            internal float Pop;
            internal float Share;
            internal float Sol;
            internal float Satis;        // 该文化法律的满意度修正(显示用)
            internal float Loyalty;
            internal float Radical;
            internal string LawId;
            internal string LawName;
            internal string LawEffect;
            internal bool IsRuling;
            internal string Color;
        }

        internal static List<CultureRow> Snapshot()
        {
            var rows = new List<CultureRow>();
            try
            {
                var pk = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                if (pk == null) return rows;
                string ruling = pk.Culture != null ? pk.Culture.StringId : null;

                var agg = new Dictionary<string, float[]>();   // id -> [pop, sol*w, loy*w, rad*w]
                float total = 0f;
                foreach (var s in pk.Settlements)
                {
                    if (s == null) continue;
                    var l = Pops.Of(s.StringId);
                    if (l == null) continue;
                    for (int i = 0; i < l.Count; i++)
                    {
                        var p = l[i];
                        if (p == null || p.Size <= 0f) continue;
                        string cid = string.IsNullOrEmpty(p.Culture) ? "?" : p.Culture;
                        float[] a;
                        if (!agg.TryGetValue(cid, out a)) { a = new float[4]; agg[cid] = a; }
                        a[0] += p.Size;
                        a[1] += p.WealthLevel * p.Size;
                        a[2] += p.Loyalty * p.Size;
                        a[3] += p.Radicalism * p.Size;
                        total += p.Size;
                    }
                }
                foreach (var kv in agg)
                {
                    var a = kv.Value;
                    if (a[0] <= 0f) continue;
                    var def = Def(LawOf(kv.Key));
                    var r = new CultureRow
                    {
                        Id = kv.Key,
                        Name = NameOf(kv.Key),
                        Pop = a[0],
                        Share = total > 0f ? a[0] / total : 0f,
                        Sol = a[1] / a[0],
                        Satis = def.Satis,
                        Loyalty = a[2] / a[0],
                        Radical = a[3] / a[0],
                        LawId = def.Id,
                        LawName = def.Name,
                        LawEffect = def.Effect,
                        IsRuling = ruling != null && kv.Key == ruling,
                        Color = ColorOf(kv.Key)
                    };
                    rows.Add(r);
                }
                rows.Sort(delegate (CultureRow x, CultureRow y) { return y.Pop.CompareTo(x.Pop); });
            }
            catch (Exception ex) { DLog.Force("文化快照异常: " + ex.Message); }
            return rows;
        }

        private static float AvgOf(Dictionary<string, float> d)
        {
            float s = 0f; int n = 0;
            foreach (var kv in d) { s += kv.Value; n++; }
            return n > 0 ? s / n : 0.5f;
        }

        // 文化色(原版 CultureObject 无统一颜色接口 -> 用稳定散列色, 任意地图都可用)
        internal static string ColorOf(string culture)
        {
            try
            {
                int h = 0;
                if (!string.IsNullOrEmpty(culture)) foreach (char c in culture) h = (h * 31 + c) & 0x7FFFFFFF;
                string[] pal = { "#C9A227FF", "#7FA8C8FF", "#A88FC8FF", "#8FBF7FFF", "#C88F7FFF", "#C87FA8FF", "#7FC8B8FF", "#B8B87FFF" };
                return pal[h % pal.Length];
            }
            catch { return "#C9A227FF"; }
        }

        // ================= v4.21x: AI 文化法律(每国按性格为其文化定法; 90 日冷却由 SetLaw 保证) =================
        private static int _aiLastDay = -1;

        internal static void AiMonthly(int day)
        {
            try
            {
                if (day < _aiLastDay) _aiLastDay = -1;   // 新档/读档 -> 重置
                if (day == _aiLastDay) return;
                _aiLastDay = day;
                var pk = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                foreach (var k in Kingdom.All)
                {
                    if (k == null || k.IsEliminated || k.Culture == null) continue;
                    if (pk != null && ReferenceEquals(pk, k)) continue;
                    try { AiStep(k); } catch { }
                }
            }
            catch (Exception ex) { DLog.Force("AI 文化法律异常: " + ex.Message); }
        }

        private static void AiStep(Kingdom k)
        {
            int agg = AiPersonality.AggressionOf(k);
            int dev = AiPersonality.DevelopmentOf(k);
            int com = AiPersonality.CommerceOf(k);
            string lawId;
            if (agg >= 65) lawId = "assimilation";                              // 好战 -> 强制同化
            else if (com >= 65) lawId = "multicultural";                        // 重商 -> 多元吸引移民
            else if (dev >= 65 && agg < 55) lawId = "state_church";             // 重建设 -> 国教
            else return;                                                        // 其余维持默认宽容
            string cul = k.Culture.StringId;
            if (string.IsNullOrEmpty(cul) || LawOf(cul) == lawId) return;
            if (CooldownTextOf(cul) != "可以改法") return;                       // 全局文化冷却(90 日)
            if (MBRandom.RandomFloat > 0.3f) return;
            SetLaw(cul, lawId);   // 自带 DLog.Force(每国每次决策一条)
        }

        // ---- 存档: FIA_Cult ----
        internal static string Serialize()
        {
            try
            {
                var sb = new System.Text.StringBuilder();
                foreach (var kv in Law)
                {
                    if (string.IsNullOrEmpty(kv.Key)) continue;
                    sb.Append(kv.Key).Append('=').Append(kv.Value);
                    int d;
                    if (ChangedDay.TryGetValue(kv.Key, out d)) sb.Append('@').Append(d);
                    sb.Append(';');
                }
                return sb.ToString();
            }
            catch { return ""; }
        }

        internal static void Deserialize(string s)
        {
            try
            {
                Law.Clear(); ChangedDay.Clear();
                if (!string.IsNullOrEmpty(s))
                {
                    foreach (var seg in s.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        var kv = seg.Split('=');
                        if (kv.Length != 2 || string.IsNullOrEmpty(kv[0])) continue;
                        string lawId = kv[1];
                        int day = -9999;
                        int at = lawId.IndexOf('@');
                        if (at >= 0)
                        {
                            int.TryParse(lawId.Substring(at + 1), out day);
                            lawId = lawId.Substring(0, at);
                        }
                        Law[kv[0]] = lawId;
                        ChangedDay[kv[0]] = day;
                    }
                }
                MigrateOldPolicy();
            }
            catch { }
        }

        // 老存档兼容: 旧的全局 Institutions.CulturePolicy(0 宽容/1 同化/2 多元) -> 铺到所有文化
        internal static void MigrateOldPolicy()
        {
            try
            {
                if (_migrated) return;
                _migrated = true;
                if (Law.Count > 0) return;
                int cp = Institutions.CulturePolicy;
                if (cp <= 0) return;
                string lawId = cp == 1 ? "assimilation" : "multicultural";
                var pk = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                if (pk != null && pk.Culture != null) Law[pk.Culture.StringId] = lawId;
            }
            catch { }
        }
    }
}
