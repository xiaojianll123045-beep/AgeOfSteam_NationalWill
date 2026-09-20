using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace FeudalInternalAffairs
{
    // 经济统计(文档 20.9): 每日快照环形缓冲(30 天) + 存档 FIA_Ledger2
    internal static class Stats
    {
        internal const int Cap = 30;

        internal struct Snapshot
        {
            public int Tax, Tariff, Mint, Dividend, Interest, Wages, Materials, Imports, Exports, Pop;
            public int Mil, MilLoss;   // v4.53: 国防军军费 / 军损
            public float PriceIndex;
        }

        internal static readonly List<Snapshot> Ring = new List<Snapshot>();

        internal static void Capture()
        {
            try
            {
                var s = new Snapshot();
                s.Tax = Fiscal.Tax;
                s.Tariff = Fiscal.Tariff;
                s.Mint = Fiscal.Mint;
                s.Dividend = Fiscal.Dividend;
                s.Interest = Fiscal.Interest;
                s.Wages = PopJob.LastWageTotal;
                s.Materials = PopJob.LastCostTotal - PopJob.LastWageTotal;
                s.Imports = MarketSim.LastImported;
                s.Exports = MarketSim.LastExported;
                s.Pop = (int)Pops.TotalPopulation();
                s.Mil = Fiscal.Military;
                s.MilLoss = DefArmy.TodayMilLoss;
                s.PriceIndex = PriceIndex();
                Ring.Add(s);
                while (Ring.Count > Cap) Ring.RemoveAt(0);
            }
            catch { }
        }

        private static float PriceIndex()
        {
            try
            {
                var n = EconomyWorld.National;
                float sum = 0f;
                int cnt = 0;
                for (int i = 0; i < FeudalGoods.Main.Count; i++)
                {
                    var g = FeudalGoods.Main[i];
                    if (g == null || g.BasePrice <= 0.01f) continue;
                    sum += n.PriceOf(g.Id) / g.BasePrice;
                    cnt++;
                }
                return cnt > 0 ? sum / cnt : 1f;
            }
            catch { return 1f; }
        }

        // 近 N 天均值(含今日); N<=0 用全部
        internal static float Avg(Func<Snapshot, float> pick, int days)
        {
            try
            {
                if (Ring.Count == 0) return 0f;
                int n = days <= 0 || days > Ring.Count ? Ring.Count : days;
                float sum = 0f;
                for (int i = Ring.Count - n; i < Ring.Count; i++) sum += pick(Ring[i]);
                return sum / n;
            }
            catch { return 0f; }
        }

        internal static string Save()
        {
            try
            {
                var sb = new StringBuilder("v1;");
                for (int i = 0; i < Ring.Count; i++)
                {
                    var s = Ring[i];
                    if (i > 0) sb.Append('|');
                    sb.Append(s.Tax).Append(',').Append(s.Tariff).Append(',').Append(s.Mint).Append(',')
                      .Append(s.Dividend).Append(',').Append(s.Interest).Append(',').Append(s.Wages).Append(',')
                      .Append(s.Materials).Append(',').Append(s.Imports).Append(',').Append(s.Exports).Append(',')
                      .Append(s.Pop).Append(',').Append(s.PriceIndex.ToString("F3", CultureInfo.InvariantCulture))
                      .Append(',').Append(s.Mil).Append(',').Append(s.MilLoss);
                }
                return sb.ToString();
            }
            catch { return ""; }
        }

        internal static void Load(string data)
        {
            try
            {
                Ring.Clear();
                if (string.IsNullOrEmpty(data)) return;
                var p = data.Split(';');
                if (p.Length < 2) return;
                foreach (var line in p[1].Split('|'))
                {
                    if (string.IsNullOrEmpty(line)) continue;
                    var f = line.Split(',');
                    if (f.Length < 11) continue;
                    var s = new Snapshot();
                    s.Tax = PI(f[0]); s.Tariff = PI(f[1]); s.Mint = PI(f[2]); s.Dividend = PI(f[3]); s.Interest = PI(f[4]);
                    s.Wages = PI(f[5]); s.Materials = PI(f[6]); s.Imports = PI(f[7]); s.Exports = PI(f[8]); s.Pop = PI(f[9]);
                    float pf;
                    s.PriceIndex = float.TryParse(f[10], NumberStyles.Float, CultureInfo.InvariantCulture, out pf) ? pf : 1f;
                    if (f.Length >= 13) { s.Mil = PI(f[11]); s.MilLoss = PI(f[12]); }
                    Ring.Add(s);
                }
            }
            catch { }
        }

        private static int PI(string s) { int v; return int.TryParse(s, out v) ? v : 0; }
    }
}
