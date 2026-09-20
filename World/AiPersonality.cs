using System;
using System.Collections.Generic;
using System.Text;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace FeudalInternalAffairs
{
    // v4.106: 国家性格(开局随机; 影响宣战/建设/扩军)
    //   三轴(0~100):
    //     鹰派 Aggression —— 宣战概率、扩军投入
    //     建设 Development —— 自动建设频率与预算
    //     商贸 Commerce   —— 预留(商贸政策倾向)
    internal static class AiPersonality
    {
        private class P { internal int Agg, Dev, Com; }
        private static readonly Dictionary<string, P> Map = new Dictionary<string, P>();
        private static bool _inited;

        // 开局随机所有国家性格(新战役)
        internal static void ResetNewCampaign()
        {
            try
            {
                Map.Clear();
                _inited = true;
                foreach (var k in Kingdom.All)
                {
                    if (k == null || k.IsEliminated) continue;
                    Map[k.StringId] = new P
                    {
                        Agg = MBRandom.RandomInt(15, 90),
                        Dev = MBRandom.RandomInt(15, 90),
                        Com = MBRandom.RandomInt(15, 90)
                    };
                }
                DLog.Force("国家性格: 已为 " + Map.Count + " 国随机生成");
            }
            catch { }
        }

        private static void Ensure()
        {
            if (_inited) return;
            _inited = true;
            try
            {
                foreach (var k in Kingdom.All)
                {
                    if (k == null || k.IsEliminated) continue;
                    if (!Map.ContainsKey(k.StringId))
                        Map[k.StringId] = new P
                        {
                            Agg = MBRandom.RandomInt(15, 90),
                            Dev = MBRandom.RandomInt(15, 90),
                            Com = MBRandom.RandomInt(15, 90)
                        };
                }
            }
            catch { }
        }

        private static P Of(Kingdom k)
        {
            try
            {
                Ensure();
                if (k == null) return null;
                P p;
                if (Map.TryGetValue(k.StringId, out p)) return p;
                p = new P { Agg = MBRandom.RandomInt(15, 90), Dev = MBRandom.RandomInt(15, 90), Com = MBRandom.RandomInt(15, 90) };
                Map[k.StringId] = p;
                return p;
            }
            catch { return null; }
        }

        internal static int AggressionOf(Kingdom k) { var p = Of(k); return p != null ? p.Agg : 50; }
        internal static int DevelopmentOf(Kingdom k) { var p = Of(k); return p != null ? p.Dev : 50; }
        internal static int CommerceOf(Kingdom k) { var p = Of(k); return p != null ? p.Com : 50; }

        // v4.116: 性格漂移(领土变化影响: 扩张->更鹰派/更重商, 失地->更保守)
        internal static void Drift(Kingdom k, int territoryDelta)
        {
            try
            {
                var p = Of(k);
                if (p == null || territoryDelta == 0) return;
                p.Agg = Clamp(p.Agg + territoryDelta * 3, 5, 95);
                p.Dev = Clamp(p.Dev - territoryDelta * 2, 5, 95);
                p.Com = Clamp(p.Com + territoryDelta, 5, 95);
            }
            catch { }
        }

        private static int Clamp(int v, int lo, int hi) { return v < lo ? lo : (v > hi ? hi : v); }

        private static string Word(int v, string hi, string mid, string lo)
        {
            if (v >= 70) return hi;
            if (v <= 30) return lo;
            return mid;
        }

        internal static string Describe(Kingdom k)
        {
            if (k == null) return "?";
            int a = AggressionOf(k), d = DevelopmentOf(k), c = CommerceOf(k);
            return "鹰派 " + a + "(" + Word(a, "好战", "稳健", "和平") + ")"
                + " · 建设 " + d + "(" + Word(d, "大兴土木", "按部就班", "保守") + ")"
                + " · 商贸 " + c + "(" + Word(c, "重商", "平衡", "自守") + ")";
        }

        // ---- 存档 ----
        internal static string Save()
        {
            try
            {
                Ensure();
                var sb = new StringBuilder();
                sb.Append("v1");
                foreach (var kv in Map)
                    sb.Append(';').Append(kv.Key).Append(',').Append(kv.Value.Agg).Append(',').Append(kv.Value.Dev).Append(',').Append(kv.Value.Com);
                return sb.ToString();
            }
            catch { return "v1"; }
        }

        internal static void Load(string data)
        {
            try
            {
                Map.Clear();
                _inited = true;
                if (string.IsNullOrEmpty(data)) return;
                var seg = data.Split(';');
                if (seg.Length < 1 || seg[0] != "v1") return;
                for (int i = 1; i < seg.Length; i++)
                {
                    var f = seg[i].Split(',');
                    if (f.Length < 4) continue;
                    int a, d, c;
                    if (!int.TryParse(f[1], out a) || !int.TryParse(f[2], out d) || !int.TryParse(f[3], out c)) continue;
                    Map[f[0]] = new P { Agg = a, Dev = d, Com = c };
                }
                DLog.Force("国家性格: 读档 " + Map.Count + " 国");
            }
            catch { }
        }
    }
}
