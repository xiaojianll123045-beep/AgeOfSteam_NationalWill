using System;
using System.Collections.Generic;
using System.Text;
using TaleWorlds.CampaignSystem;

namespace FeudalInternalAffairs
{
    // v4.126: 封建契约(CK3 缝合) —— 每个领主家族一份契约(税赋档/兵役档):
    //   · 税档: 高 -> 国库实收 ×(0.5/1/1.5/2), 但领主每月愤怒 +
    //   · 兵役档: 高 -> 征兵上限 ×(0.5/1/1.5/2), 但领主每月愤怒 +
    //   · 调整消耗 20 权威, 同族 84 天(1 年)冷却; 领主即时反应(不满 +/-, 加税/加役更贵)
    internal static class FeudalContracts
    {
        internal class Contract
        {
            internal int Tax = 1;    // 0 低(0.5x) / 1 标准(1x) / 2 高(1.5x) / 3 勒索(2x)
            internal int Levy = 1;   // 0 低征 / 1 标准 / 2 高征 / 3 大规模
            internal int LastDay;    // 上次修改(战役日)
        }

        internal static readonly float[] Mult = { 0.5f, 1f, 1.5f, 2f };
        internal static readonly string[] TaxNames = { "低税", "标准", "高税", "勒索" };
        internal static readonly string[] LevyNames = { "低征", "标准", "高征", "大规模" };
        private const int ChangeCost = 20;      // 每次调整消耗权威
        private const int CooldownDays = 84;    // 同族 1 年只能改一次

        private static readonly Dictionary<string, Contract> Map = new Dictionary<string, Contract>();

        internal static Contract Of(string clanId)
        {
            try
            {
                if (string.IsNullOrEmpty(clanId)) return new Contract();
                Contract c;
                if (Map.TryGetValue(clanId, out c)) return c;
                c = new Contract();
                Map[clanId] = c;
                return c;
            }
            catch { return new Contract(); }
        }

        // 玩家王国的加权平均倍率(按领主势力加权; 无领主 -> 1)
        internal static float AvgTaxMult()
        {
            try
            {
                float sum = 0f, w = 0f;
                foreach (var kv in Politics.Lords)
                {
                    var lp = kv.Value;
                    if (lp == null) continue;
                    float ww = Math.Max(1, lp.Clout);
                    sum += Mult[Of(kv.Key).Tax] * ww;
                    w += ww;
                }
                return w > 0f ? sum / w : 1f;
            }
            catch { return 1f; }
        }

        internal static float AvgLevyMult()
        {
            try
            {
                float sum = 0f, w = 0f;
                foreach (var kv in Politics.Lords)
                {
                    var lp = kv.Value;
                    if (lp == null) continue;
                    float ww = Math.Max(1, lp.Clout);
                    sum += Mult[Of(kv.Key).Levy] * ww;
                    w += ww;
                }
                return w > 0f ? sum / w : 1f;
            }
            catch { return 1f; }
        }

        // 契约带来的每月领主愤怒(税档/兵役档超过标准的部分)
        internal static int MonthlyAngerOf(string clanId)
        {
            try
            {
                var c = Of(clanId);
                float extra = (Mult[c.Tax] - 1f) * 4f + (Mult[c.Levy] - 1f) * 3f;
                if (extra <= 0f) return (int)Math.Round(extra);   // 减档为负(安抚)
                return (int)Math.Round(extra);
            }
            catch { return 0; }
        }

        // 调整契约(玩家在政治页操作): 返回结果消息
        internal static string Adjust(string clanId, bool tax, bool up)
        {
            try
            {
                if (string.IsNullOrEmpty(clanId)) return "无效家族";
                var c = Of(clanId);
                int day = (int)CampaignTime.Now.ToDays;
                if (day - c.LastDay < CooldownDays)
                    return "该家族契约近期已调整(还需 " + (CooldownDays - (day - c.LastDay)) + " 天)";
                if (Politics.Authority < ChangeCost) return "权威不足(需要 " + ChangeCost + ", 现有 " + (int)Politics.Authority + ")";
                int cur = tax ? c.Tax : c.Levy;
                int next = up ? cur + 1 : cur - 1;
                if (next < 0) next = 0;
                if (next > 3) next = 3;
                if (next == cur) return "已经是" + (up ? "最高" : "最低") + "档了";
                Politics.Authority -= ChangeCost;
                if (tax) c.Tax = next; else c.Levy = next;
                c.LastDay = day;
                // 领主即时反应: 加税/加役 -> 愤怒; 减 -> 安抚
                int delta = up ? (tax ? 12 : 9) : -(tax ? 10 : 8);
                LordProfile lp;
                Politics.Lords.TryGetValue(clanId, out lp);
                if (lp != null) lp.Anger = Math.Max(0, Math.Min(100, lp.Anger + delta));
                string nm = tax ? TaxNames[next] : LevyNames[next];
                return (lp != null ? lp.Name : clanId) + " 的" + (tax ? "税赋" : "兵役") + "契约 -> " + nm
                    + "(权威 -" + ChangeCost + ", 领主不满 " + (delta >= 0 ? "+" : "") + delta + ")";
            }
            catch (Exception ex) { DLog.Force("契约调整失败: " + ex.Message); return "调整失败, 见日志"; }
        }

        // v4.141: 供侧栏 UI 使用
        internal static int Cost { get { return ChangeCost; } }

        internal static int CooldownLeft(string clanId)
        {
            try
            {
                var c = Of(clanId);
                int day = (int)CampaignTime.Now.ToDays;
                int left = CooldownDays - (day - c.LastDay);
                return left > 0 ? left : 0;
            }
            catch { return 0; }
        }

        internal static void Reset() { Map.Clear(); }

        internal static string Save()
        {
            try
            {
                var sb = new StringBuilder();
                sb.Append("v1");
                foreach (var kv in Map)
                    sb.Append(';').Append(kv.Key).Append(',').Append(kv.Value.Tax).Append(',').Append(kv.Value.Levy).Append(',').Append(kv.Value.LastDay);
                return sb.ToString();
            }
            catch { return "v1"; }
        }

        internal static void Load(string data)
        {
            try
            {
                Map.Clear();
                if (string.IsNullOrEmpty(data)) return;
                var seg = data.Split(';');
                if (seg.Length < 1 || seg[0] != "v1") return;
                for (int i = 1; i < seg.Length; i++)
                {
                    var f = seg[i].Split(',');
                    if (f.Length < 3) continue;
                    int t, l, d = 0;
                    if (!int.TryParse(f[1], out t) || !int.TryParse(f[2], out l)) continue;
                    int.TryParse(f.Length > 3 ? f[3] : "0", out d);
                    Map[f[0]] = new Contract { Tax = t, Levy = l, LastDay = d };
                }
            }
            catch { }
        }
    }
}
