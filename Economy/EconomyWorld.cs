using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace FeudalInternalAffairs
{
    // 国库(6.1): 国库 = 玩家金钱, 允许为负 = 债务
    internal class TreasuryData
    {
        internal int Gold;              // 国库余额(镜像, 可为负)
        internal int LastGold;          // 昨日国库(检测负增长)
        internal int NegativeDays;      // 连续负增长天数(破产倒计时 7 天)
        internal int PenaltyDaysLeft;   // 破产惩罚剩余天数(30)
        internal int BankruptCount;     // 破产次数
        internal int LordDebt;          // 破产后下发领主的剩余债务
    }

    // 领主私有数据(7.1 / 7.4, P6 使用)
    internal class LordPrivateData
    {
        internal string LordId;
        internal string Personality;    // 性格 id
        internal int CooldownDays;      // 建造冷却
    }

    // 多处建造方案(2.8 / P8)
    internal class BrushPreset
    {
        internal string Name;
        internal List<string> DefIds = new List<string>();
    }

    // 自动化开关(9.1 / 9.2 / P9)
    internal class AutoBuildData
    {
        internal bool AutoBalance;      // 玩家自动平衡市场
        internal bool AutoBuild;        // 玩家自动建造
        internal bool LordFreeArmy;     // 领主可自由建军团(政策)
    }

    // 经济系统的世界数据(全部走 NationalWillBehavior 同款 SyncData 机制存档)
    internal static class EconomyWorld
    {
        internal const int Version = 1;

        internal static readonly Dictionary<string, SettlementBuildings> Buildings =
            new Dictionary<string, SettlementBuildings>();
        internal static readonly Dictionary<string, TownMarket> Markets =
            new Dictionary<string, TownMarket>();
        internal static readonly NationalMarket National = new NationalMarket();
        internal static readonly TreasuryData Treasury = new TreasuryData();
        internal static readonly Dictionary<string, LordPrivateData> Lords =
            new Dictionary<string, LordPrivateData>();
        internal static readonly List<BrushPreset> Brushes = new List<BrushPreset>();
        internal static readonly AutoBuildData AutoBuild = new AutoBuildData();

        internal static bool Initialized { get; private set; }

        // 是否有实际数据(旧存档检测用)
        internal static bool HasAnyData
        {
            get
            {
                foreach (var kv in Buildings)
                    if (kv.Value != null && kv.Value.Groups.Count > 0) return true;
                return false;
            }
        }

        internal static void Reset()
        {
            Buildings.Clear();
            Markets.Clear();
            National.Reset();
            Treasury.Gold = 0;
            Treasury.LastGold = 0;
            Treasury.NegativeDays = 0;
            Treasury.PenaltyDaysLeft = 0;
            Treasury.BankruptCount = 0;
            Treasury.LordDebt = 0;
            Lords.Clear();
            Brushes.Clear();
            AutoBuild.AutoBalance = false;
            AutoBuild.AutoBuild = false;
            AutoBuild.LordFreeArmy = false;
            Initialized = false;
        }

        // 开局/读档后补齐容器(旧存档兼容: 检测无数据 -> 自动初始化)
        internal static void EnsureContainers()
        {
            try
            {
                foreach (var s in TaleWorlds.CampaignSystem.Settlements.Settlement.All)
                {
                    if (s == null || string.IsNullOrEmpty(s.StringId)) continue;
                    if (!Buildings.ContainsKey(s.StringId)) Buildings.Add(s.StringId, new SettlementBuildings(s.StringId));
                    if (s.IsTown && !Markets.ContainsKey(s.StringId)) Markets.Add(s.StringId, new TownMarket(s.StringId));
                }
                if (National.BasePrice.Count == 0) National.Reset();
                Initialized = true;
            }
            catch (Exception ex) { DLog.Force("经济数据初始化异常: " + ex.Message); }
        }

        internal static SettlementBuildings Of(string settlementId)
        {
            if (string.IsNullOrEmpty(settlementId)) return null;
            SettlementBuildings b;
            if (Buildings.TryGetValue(settlementId, out b)) return b;
            b = new SettlementBuildings(settlementId);
            Buildings.Add(settlementId, b);
            return b;
        }

        // 只读查询(不存在则返回 null, 不创建条目)
        internal static SettlementBuildings Find(string settlementId)
        {
            if (string.IsNullOrEmpty(settlementId)) return null;
            SettlementBuildings b;
            return Buildings.TryGetValue(settlementId, out b) ? b : null;
        }

        internal static TownMarket MarketOf(string townId)
        {
            if (string.IsNullOrEmpty(townId)) return null;
            TownMarket m;
            if (Markets.TryGetValue(townId, out m)) return m;
            m = new TownMarket(townId);
            Markets.Add(townId, m);
            return m;
        }

        // 只读查询(不存在则返回 null, 不创建)
        internal static TownMarket FindMarket(string townId)
        {
            if (string.IsNullOrEmpty(townId)) return null;
            TownMarket m;
            return Markets.TryGetValue(townId, out m) ? m : null;
        }

        internal static void MarkDirty()
        {
            // 预留给 UI 脏标记(P3 起使用)
        }

        // ==================== 存档: 序列化 ====================

        internal static string SaveBuildings()
        {
            var sb = new StringBuilder();
            sb.Append("v2;");
            foreach (var kv in Buildings)
            {
                var sb2 = kv.Value;
                if (sb2 == null) continue;
                sb.Append(kv.Key).Append(':');
                bool first = true;
                foreach (var g in sb2.Groups)
                {
                    if (g == null || string.IsNullOrEmpty(g.DefId) || g.Count <= 0) continue;
                    if (!first) sb.Append('|');
                    first = false;
                    sb.Append(g.DefId).Append(',').Append(g.Count).Append(',').Append((int)g.Mode);
                }
                if (sb2.Queue.Count > 0)
                {
                    sb.Append('*');
                    for (int i = 0; i < sb2.Queue.Count; i++)
                    {
                        var q = sb2.Queue[i];
                        if (q == null || string.IsNullOrEmpty(q.DefId)) continue;
                        if (i > 0) sb.Append('+');
                        sb.Append(q.DefId).Append(',').Append(q.Progress).Append(',')
                          .Append(q.TotalWork).Append(',').Append(q.Paused ? 1 : 0).Append(',').Append((int)q.Mode);
                    }
                }
                sb.Append(';');
            }
            return sb.ToString();
        }

        internal static void LoadBuildings(string data)
        {
            Buildings.Clear();
            if (string.IsNullOrEmpty(data)) return;
            try
            {
                var parts = data.Split(';');
                for (int i = 1; i < parts.Length; i++)   // [0] = 版本头
                {
                    var line = parts[i];
                    if (string.IsNullOrEmpty(line)) continue;
                    int colon = line.IndexOf(':');
                    if (colon <= 0) continue;
                    string sid = line.Substring(0, colon);
                    var sb = new SettlementBuildings(sid);
                    string body = line.Substring(colon + 1);

                    // 队列段(新格式)
                    string queueText = null;
                    int star = body.IndexOf('*');
                    if (star >= 0)
                    {
                        queueText = body.Substring(star + 1);
                        body = body.Substring(0, star);
                    }

                    if (body.Length > 0)
                    {
                        foreach (var gtext in body.Split('|'))
                        {
                            if (string.IsNullOrEmpty(gtext)) continue;
                            var g = ParseGroup(gtext);
                            if (g != null) sb.Groups.Add(g);
                        }
                    }
                    if (!string.IsNullOrEmpty(queueText))
                    {
                        foreach (var qtext in queueText.Split('+'))
                        {
                            if (string.IsNullOrEmpty(qtext)) continue;
                            var q = ParseQueueItem(qtext);
                            if (q != null) sb.Queue.Add(q);
                        }
                    }
                    if (Buildings.ContainsKey(sid)) Buildings[sid] = sb;
                    else Buildings.Add(sid, sb);
                }
            }
            catch (Exception ex) { DLog.Force("建筑数据读取失败: " + ex.Message); }
        }

        private static BuildingGroup ParseGroup(string text)
        {
            try
            {
                var f = text.Split(',');
                if (f.Length < 3) return null;
                return new BuildingGroup(f[0], PI(f[1]), (BuildMode)PI(f[2]));
            }
            catch { return null; }
        }

        private static QueuedBuild ParseQueueItem(string text)
        {
            try
            {
                var f = text.Split(',');
                if (f.Length < 5) return null;
                var q = new QueuedBuild(f[0], PI(f[2]), (BuildMode)PI(f[4]));
                q.Progress = PI(f[1]);
                q.Paused = PI(f[3]) != 0;
                return q;
            }
            catch { return null; }
        }

        internal static string SaveMarkets()
        {
            var sb = new StringBuilder();
            sb.Append("v").Append(Version).Append(';');
            foreach (var kv in Markets)
            {
                var m = kv.Value;
                if (m == null) continue;
                sb.Append(kv.Key).Append(':');
                bool first = true;
                foreach (var ekv in m.Entries)
                {
                    var e = ekv.Value;
                    if (e == null) continue;
                    if (!first) sb.Append('|');
                    first = false;
                    sb.Append(e.GoodId).Append(',').Append(FF(e.Stock)).Append(',').Append(FF(e.Price))
                      .Append(',').Append(FF(e.DailyProduction)).Append(',').Append(FF(e.DailyConsumption));
                }
                sb.Append(';');
            }
            return sb.ToString();
        }

        internal static void LoadMarkets(string data)
        {
            Markets.Clear();
            if (string.IsNullOrEmpty(data)) return;
            try
            {
                var parts = data.Split(';');
                for (int i = 1; i < parts.Length; i++)
                {
                    var line = parts[i];
                    if (string.IsNullOrEmpty(line)) continue;
                    int colon = line.IndexOf(':');
                    if (colon <= 0) continue;
                    string tid = line.Substring(0, colon);
                    var m = new TownMarket(tid);
                    string entriesText = line.Substring(colon + 1);
                    if (entriesText.Length > 0)
                    {
                        foreach (var etext in entriesText.Split('|'))
                        {
                            if (string.IsNullOrEmpty(etext)) continue;
                            var f = etext.Split(',');
                            if (f.Length < 5) continue;
                            var e = new MarketEntry(f[0]);
                            e.Stock = PF(f[1]);
                            e.Price = PF(f[2]);
                            e.DailyProduction = PF(f[3]);
                            e.DailyConsumption = PF(f[4]);
                            if (m.Entries.ContainsKey(e.GoodId)) m.Entries[e.GoodId] = e;
                            else m.Entries.Add(e.GoodId, e);
                        }
                    }
                    if (Markets.ContainsKey(tid)) Markets[tid] = m;
                    else Markets.Add(tid, m);
                }
            }
            catch (Exception ex) { DLog.Force("市场数据读取失败: " + ex.Message); }
        }

        internal static string SaveNational()
        {
            var sb = new StringBuilder();
            sb.Append("v").Append(Version).Append(';');
            foreach (var kv in National.BasePrice) sb.Append(kv.Key).Append(',').Append(FF(kv.Value)).Append(';');
            return sb.ToString();
        }

        internal static void LoadNational(string data)
        {
            National.BasePrice.Clear();
            if (string.IsNullOrEmpty(data)) return;
            try
            {
                var parts = data.Split(';');
                for (int i = 1; i < parts.Length; i++)
                {
                    var f = parts[i].Split(',');
                    if (f.Length < 2) continue;
                    National.BasePrice[f[0]] = PF(f[1]);
                }
            }
            catch (Exception ex) { DLog.Force("全国市场数据读取失败: " + ex.Message); }
        }

        internal static string SaveTreasury()
        {
            var sb = new StringBuilder();
            sb.Append("v").Append(Version).Append(';')
              .Append(Treasury.Gold).Append(';').Append(Treasury.LastGold).Append(';')
              .Append(Treasury.NegativeDays).Append(';').Append(Treasury.PenaltyDaysLeft).Append(';')
              .Append(Treasury.BankruptCount).Append(';').Append(Treasury.LordDebt);
            return sb.ToString();
        }

        internal static void LoadTreasury(string data)
        {
            if (string.IsNullOrEmpty(data)) return;
            try
            {
                var f = data.Split(';');
                if (f.Length < 7) return;
                Treasury.Gold = PI(f[1]);
                Treasury.LastGold = PI(f[2]);
                Treasury.NegativeDays = PI(f[3]);
                Treasury.PenaltyDaysLeft = PI(f[4]);
                Treasury.BankruptCount = PI(f[5]);
                Treasury.LordDebt = PI(f[6]);
            }
            catch (Exception ex) { DLog.Force("国库数据读取失败: " + ex.Message); }
        }

        internal static string SaveLords()
        {
            var sb = new StringBuilder();
            sb.Append("v").Append(Version).Append(';');
            foreach (var kv in Lords)
            {
                var l = kv.Value;
                if (l == null) continue;
                sb.Append(l.LordId).Append(',').Append(l.Personality ?? "").Append(',').Append(l.CooldownDays).Append(';');
            }
            return sb.ToString();
        }

        internal static void LoadLords(string data)
        {
            Lords.Clear();
            if (string.IsNullOrEmpty(data)) return;
            try
            {
                var parts = data.Split(';');
                for (int i = 1; i < parts.Length; i++)
                {
                    var f = parts[i].Split(',');
                    if (f.Length < 3 || string.IsNullOrEmpty(f[0])) continue;
                    var l = new LordPrivateData { LordId = f[0], Personality = f[1], CooldownDays = PI(f[2]) };
                    if (Lords.ContainsKey(l.LordId)) Lords[l.LordId] = l;
                    else Lords.Add(l.LordId, l);
                }
            }
            catch (Exception ex) { DLog.Force("领主数据读取失败: " + ex.Message); }
        }

        internal static string SaveBrushes()
        {
            var sb = new StringBuilder();
            sb.Append("v").Append(Version).Append(';');
            foreach (var b in Brushes)
            {
                if (b == null || string.IsNullOrEmpty(b.Name)) continue;
                sb.Append(b.Name.Replace(';', '_').Replace(',', '_').Replace('|', '_')).Append(',');
                for (int i = 0; i < b.DefIds.Count; i++)
                {
                    if (i > 0) sb.Append('|');
                    sb.Append(b.DefIds[i]);
                }
                sb.Append(';');
            }
            return sb.ToString();
        }

        internal static void LoadBrushes(string data)
        {
            Brushes.Clear();
            if (string.IsNullOrEmpty(data)) return;
            try
            {
                var parts = data.Split(';');
                for (int i = 1; i < parts.Length; i++)
                {
                    var f = parts[i].Split(',');
                    if (f.Length < 1 || string.IsNullOrEmpty(f[0])) continue;
                    var b = new BrushPreset { Name = f[0] };
                    if (f.Length > 1 && f[1].Length > 0)
                        foreach (var d in f[1].Split('|')) if (d.Length > 0) b.DefIds.Add(d);
                    Brushes.Add(b);
                }
            }
            catch (Exception ex) { DLog.Force("方案数据读取失败: " + ex.Message); }
        }

        internal static string SaveAutoBuild()
        {
            return "v" + Version + ";"
                + (AutoBuild.AutoBalance ? 1 : 0) + ";"
                + (AutoBuild.AutoBuild ? 1 : 0) + ";"
                + (AutoBuild.LordFreeArmy ? 1 : 0);
        }

        internal static void LoadAutoBuild(string data)
        {
            if (string.IsNullOrEmpty(data)) return;
            try
            {
                var f = data.Split(';');
                if (f.Length < 4) return;
                AutoBuild.AutoBalance = PI(f[1]) != 0;
                AutoBuild.AutoBuild = PI(f[2]) != 0;
                AutoBuild.LordFreeArmy = PI(f[3]) != 0;
            }
            catch (Exception ex) { DLog.Force("自动化设置读取失败: " + ex.Message); }
        }

        // 统计(日志 / 调试)
        internal static string Describe()
        {
            int groups = 0, built = 0, queued = 0, entries = 0;
            foreach (var kv in Buildings)
            {
                if (kv.Value == null) continue;
                groups += kv.Value.Groups.Count;
                built += kv.Value.BuiltCount;
                queued += kv.Value.QueuedCount;
            }
            foreach (var kv in Markets)
            {
                if (kv.Value == null) continue;
                entries += kv.Value.Count;
            }
            return "定居点=" + Buildings.Count + " 建筑组=" + groups + " 已建=" + built
                + " 在建=" + queued + " 市场=" + Markets.Count + " 商品条目=" + entries
                + " 国库=" + Treasury.Gold;
        }

        private static float PF(string s)
        {
            float v;
            float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
            return v;
        }

        private static int PI(string s)
        {
            int v;
            int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v);
            return v;
        }

        private static string FF(float v)
        {
            return v.ToString("F2", CultureInfo.InvariantCulture);
        }
    }
}
