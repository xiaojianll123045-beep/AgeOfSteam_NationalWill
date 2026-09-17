using System;
using System.Collections.Generic;
using System.Text;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.Core;

namespace FeudalInternalAffairs
{
    // 国家关系(-100 ~ +100) + 同盟辅助(同盟走游戏自带的 AllianceCampaignBehavior)
    internal static class Diplomacy
    {
        internal const int MinValue = -100;
        internal const int MaxValue = 100;
        internal const int AllyThreshold = 50;      // 缔结同盟所需关系
        internal const int WarPenalty = -40;        // 宣战
        internal const int PeaceBonus = 25;         // 和谈
        internal const int BreakAllyPenalty = -30;  // 解除同盟

        private static readonly Dictionary<string, int> _rel = new Dictionary<string, int>();

        internal static void Reset() { _rel.Clear(); }

        internal static string Key(Kingdom a, Kingdom b)
        {
            if (a == null || b == null) return null;
            return string.CompareOrdinal(a.StringId, b.StringId) <= 0
                ? a.StringId + "|" + b.StringId
                : b.StringId + "|" + a.StringId;
        }

        internal static int Get(Kingdom a, Kingdom b)
        {
            var k = Key(a, b);
            if (k == null) return 0;
            int v;
            return _rel.TryGetValue(k, out v) ? v : 0;
        }

        internal static void Change(Kingdom a, Kingdom b, int delta)
        {
            Set(a, b, Get(a, b) + delta);
        }

        internal static void Set(Kingdom a, Kingdom b, int value)
        {
            var k = Key(a, b);
            if (k == null) return;
            _rel[k] = Math.Max(MinValue, Math.Min(MaxValue, value));
        }

        internal static string Describe(int v)
        {
            if (v >= 75) return "亲密";
            if (v >= AllyThreshold) return "友好";
            if (v >= 20) return "温和";
            if (v > -20) return "中立";
            if (v > -50) return "冷淡";
            if (v > -75) return "敌对";
            return "死敌";
        }

        // 关系颜色(8 位 #RRGGBBAA)
        internal static string ColorOf(int v)
        {
            if (v >= AllyThreshold) return "#7DC97DFF";   // 绿
            if (v >= 20) return "#C9C97DFF";
            if (v > -20) return "#E8D9B5FF";              // 米
            if (v > -50) return "#E8C33AFF";              // 黄
            return "#D96A5AFF";                           // 红
        }

        // ================= 同盟 =================
        internal static bool IsAlly(Kingdom a, Kingdom b)
        {
            try { return a != null && b != null && a.IsAllyWith(b); }
            catch { return false; }
        }

        internal static AllianceCampaignBehavior Behavior()
        {
            try { return Campaign.Current != null ? Campaign.Current.GetCampaignBehavior<AllianceCampaignBehavior>() : null; }
            catch { return null; }
        }

        internal static bool StartAlliance(Kingdom a, Kingdom b)
        {
            try
            {
                var beh = Behavior();
                if (beh == null) { DLog.Force("缔结同盟失败: 找不到 AllianceCampaignBehavior"); return false; }
                beh.StartAlliance(a, b);
                return true;
            }
            catch (Exception ex) { DLog.Force("缔结同盟失败: " + ex.Message); return false; }
        }

        internal static void EndAlliance(Kingdom a, Kingdom b)
        {
            try
            {
                var beh = Behavior();
                if (beh != null) beh.EndAlliance(a, b);
            }
            catch (Exception ex) { DLog.Force("解除同盟失败: " + ex.Message); }
        }

        // ================= 阵营与和谈表决 =================
        // 一国所在阵营 = 本国 + 其所有盟国
        internal static List<Kingdom> SideOf(Kingdom k)
        {
            var list = new List<Kingdom>();
            try
            {
                if (k == null) return list;
                list.Add(k);
                foreach (var x in Kingdom.All)
                {
                    if (x == null || x == k || x.IsEliminated) continue;
                    if (IsAlly(k, x)) list.Add(x);
                }
            }
            catch { }
            return list;
        }

        // 一方阵营的领主表决: 赞成票 / 总票数 > 50% 视为通过
        // 赞成倾向 = 0.5 + 与对方关系/300 + 战争天数/300 (夹在 5%~95%)
        internal static bool VotePeace(List<Kingdom> side, Kingdom enemy, out int yes, out int total, out int avgDays)
        {
            yes = 0; total = 0; avgDays = 0;
            try
            {
                int daysSum = 0, daysN = 0;
                foreach (var k in side)
                {
                    if (k == null || k.IsEliminated) continue;
                    int warDays = 0;
                    try
                    {
                        var st = k.GetStanceWith(enemy);
                        if (st != null && st.IsAtWar && st.WarStartDate != TaleWorlds.CampaignSystem.CampaignTime.Never)
                            warDays = (int)(TaleWorlds.CampaignSystem.CampaignTime.Now - st.WarStartDate).ToDays;
                    }
                    catch { }
                    if (warDays > 0) { daysSum += warDays; daysN++; }

                    float rel = Get(k, enemy);
                    float p = 0.5f + rel / 300f + warDays / 300f;
                    if (p < 0.05f) p = 0.05f;
                    if (p > 0.95f) p = 0.95f;

                    try
                    {
                        foreach (var c in k.Clans)
                        {
                            if (c == null || c.Leader == null || c.Leader.IsDead) continue;
                            total++;
                            if (MBRandom.RandomFloat < p) yes++;
                        }
                    }
                    catch { }
                }
                avgDays = daysN > 0 ? daysSum / daysN : 0;
            }
            catch { }
            return total > 0 && yes * 2 > total;   // 严格多数(>50%)
        }

        // ================= 和谈冷却(防止无限重试) =================
        private static readonly Dictionary<string, int> _peaceCooldown = new Dictionary<string, int>();

        internal static bool PeaceOnCooldown(string enemyId, out int daysLeft)
        {
            daysLeft = 0;
            try
            {
                if (string.IsNullOrEmpty(enemyId)) return false;
                int until;
                if (!_peaceCooldown.TryGetValue(enemyId, out until)) return false;
                int now = (int)TaleWorlds.CampaignSystem.CampaignTime.Now.ToDays;
                if (now >= until) { _peaceCooldown.Remove(enemyId); return false; }
                daysLeft = until - now;
                return true;
            }
            catch { return false; }
        }

        internal static void SetPeaceCooldown(string enemyId, int days)
        {
            try
            {
                if (string.IsNullOrEmpty(enemyId)) return;
                _peaceCooldown[enemyId] = (int)TaleWorlds.CampaignSystem.CampaignTime.Now.ToDays + Math.Max(1, days);
            }
            catch { }
        }

        // ================= 每日漂移 =================
        // 交战中 -> 向 -100 靠拢; 同盟 -> 向 +100; 否则向 0
        internal static void DailyDrift()
        {
            try
            {
                var keys = new List<string>(_rel.Keys);
                foreach (var k in keys)
                {
                    int v = _rel[k];
                    int target = 0;
                    try
                    {
                        var parts = k.Split('|');
                        if (parts.Length == 2)
                        {
                            Kingdom a = null, b = null;
                            foreach (var kd in Kingdom.All)
                            {
                                if (kd == null) continue;
                                if (kd.StringId == parts[0]) a = kd;
                                else if (kd.StringId == parts[1]) b = kd;
                            }
                            if (a != null && b != null)
                            {
                                if (a.IsAtWarWith(b)) target = MinValue;
                                else if (IsAlly(a, b)) target = MaxValue;
                            }
                        }
                    }
                    catch { }
                    if (v > target) _rel[k] = Math.Max(target, v - 1);
                    else if (v < target) _rel[k] = Math.Min(target, v + 1);
                }
            }
            catch { }
        }

        // ================= 存档 =================
        internal static string Save()
        {
            try
            {
                var sb = new StringBuilder();
                sb.Append("v1;");
                foreach (var kv in _rel) sb.Append(kv.Key).Append(',').Append(kv.Value).Append(';');
                return sb.ToString();
            }
            catch { return "v1;"; }
        }

        internal static void Load(string data)
        {
            _rel.Clear();
            if (string.IsNullOrEmpty(data)) return;
            try
            {
                var parts = data.Split(';');
                for (int i = 1; i < parts.Length; i++)
                {
                    var f = parts[i].Split(',');
                    if (f.Length < 2 || string.IsNullOrEmpty(f[0])) continue;
                    int v;
                    if (int.TryParse(f[1], out v)) _rel[f[0]] = Math.Max(MinValue, Math.Min(MaxValue, v));
                }
            }
            catch (Exception ex) { DLog.Force("国家关系读取失败: " + ex.Message); }
        }
    }
}
