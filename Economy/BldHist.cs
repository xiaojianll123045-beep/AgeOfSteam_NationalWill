using System;
using System.Collections.Generic;

namespace FeudalInternalAffairs
{
    // 建筑经营历史(文档 20.8; 内存态, 每建筑最近 12 天盈亏)
    internal static class BldHist
    {
        private static readonly Dictionary<string, List<float>> _data = new Dictionary<string, List<float>>();
        private const int Max = 12;

        internal static void Record(string sid, string defId, float profit)
        {
            try
            {
                string k = sid + "|" + defId;
                List<float> l;
                if (!_data.TryGetValue(k, out l)) { l = new List<float>(); _data[k] = l; }
                l.Add(profit);
                if (l.Count > Max) l.RemoveAt(0);
            }
            catch { }
        }

        internal static string TextOf(string sid, string defId)
        {
            try
            {
                List<float> l;
                if (!_data.TryGetValue(sid + "|" + defId, out l) || l.Count == 0) return "近 12 天: 暂无记录";
                var sb = new System.Text.StringBuilder("近 12 天: ");
                for (int i = 0; i < l.Count; i++)
                {
                    if (i > 0) sb.Append(" / ");
                    float v = l[i];
                    sb.Append(v >= 0f ? "+" : "").Append((int)v);
                }
                return sb.ToString();
            }
            catch { return ""; }
        }
    }
}
