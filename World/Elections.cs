using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;

namespace FeudalInternalAffairs
{
    // 选举(v4.137 照 V3 官方 Elections 重做): 每 4 年, 竞选期 6 个月(我们 42 天),
    // 政党/集团有竞选动量(随机+领袖声望+事件), 开票后集团获得选票政治力量(影响合法性),
    // 选举不直接改组政府, 但选后 6 个月内第一次改组免费(不激化下野集团支持者)
    internal static class Elections
    {
        internal const int CycleDays = 336;        // 4 年(骑砍 1 年 84 天)
        internal const int CampaignDays = 42;      // 竞选期 6 个月
        internal const int FreeReformDays = 42;    // 选后免费改组窗口(官方: 6 个月)

        internal static int LastElectionDay = -9999;
        internal static int CampaignStartDay = -9999;
        internal static bool CampaignActive;
        internal static int LastWinnerGroup = -1;
        internal static string LastResult = "";

        internal static readonly float[] Momentum = new float[InterestGroups.GroupCount];
        internal static readonly float[] VoteStrength = new float[InterestGroups.GroupCount];
        private static int _lastMomentumDay = -9999;

        internal static bool HasVoting()
        {
            try { return LawSystem.Level(LawSystem.LFranchise) >= 1; } catch { return false; }
        }

        internal static int DaysToNext()
        {
            try
            {
                if (!HasVoting()) return -1;
                int next = LastElectionDay < 0 ? Politics.Today() : LastElectionDay + CycleDays;
                return Math.Max(0, next - Politics.Today());
            }
            catch { return -1; }
        }

        // 选后免费改组窗口(V3 官方: 选举后 6 个月)
        internal static bool InFreeReformWindow()
        {
            try { return LastElectionDay >= 0 && Politics.Today() - LastElectionDay <= FreeReformDays; }
            catch { return false; }
        }

        // ===== 投票法(V3 官方 Voting laws 的票值差异) =====
        // 返回各阶层票值修正(up/mid/low)
        internal static void VoteWeights(out float up, out float mid, out float low)
        {
            try
            {
                switch (LawSystem.Level(LawSystem.LFranchise))
                {
                    case 0: up = 2.0f; mid = 1.0f; low = 0.3f; break;   // 贵族会议(寡头): 上层票值高
                    case 1: up = 1.5f; mid = 1.3f; low = 0.6f; break;   // 财产选举
                    case 2: up = 1.2f; mid = 1.2f; low = 0.9f; break;   // 等级议会(识字加权)
                    default: up = 1.0f; mid = 1.0f; low = 1.0f; break;  // 平民大会
                }
            }
            catch { up = 1f; mid = 1f; low = 1f; }
        }

        // 合法性来自选票(V3 官方: Landed +40 / Wealth +65 / Census +85 / Universal +110, 缩放 1/5)
        internal static float LegitFromVotes()
        {
            try
            {
                if (!HasVoting()) return 0f;
                float baseV;
                switch (LawSystem.Level(LawSystem.LFranchise))
                {
                    case 0: baseV = 8f; break;
                    case 1: baseV = 13f; break;
                    case 2: baseV = 17f; break;
                    default: baseV = 22f; break;
                }
                // 执政集团的选票政治力量占比
                float all = 0f, ing = 0f;
                for (int g = 0; g < InterestGroups.GroupCount; g++)
                {
                    all += VoteStrength[g];
                    if (InterestGroups.InGovOf(g)) ing += VoteStrength[g];
                }
                float share = all > 0f ? ing / all : 0f;
                return baseV * share;
            }
            catch { return 0f; }
        }

        // ===== 竞选与开票 =====
        internal static void Daily(int day)
        {
            try
            {
                if (!HasVoting()) return;
                if (LastElectionDay < 0) { LastElectionDay = day; return; }
                int next = LastElectionDay + CycleDays;
                if (!CampaignActive && day >= next - CampaignDays)
                {
                    CampaignActive = true;
                    CampaignStartDay = day;
                    StartCampaign();
                }
                // 竞选期: 每 7 天动量波动(V3: 随机 + 领袖声望 + 事件)
                if (CampaignActive && day - _lastMomentumDay >= 7)
                {
                    _lastMomentumDay = day;
                    FluctuateMomentum();
                }
                if (day >= next)
                {
                    RunElection(day);
                }
            }
            catch (Exception ex) { DLog.Force("选举日结异常: " + ex.Message); }
        }

        private static void StartCampaign()
        {
            try
            {
                float up, mid, low;
                VoteWeights(out up, out mid, out low);
                for (int g = 0; g < InterestGroups.GroupCount; g++)
                {
                    var grp = InterestGroups.G[g];
                    if (grp == null) continue;
                    // 初始动量: 满意度 + 领袖声望(简化: 用满意度的偏差)
                    Momentum[g] = (grp.Satisfaction - 50) * 0.5f * (float)(new Random(g * 7 + Politics.Today()).NextDouble() * 2 - 1);
                }
                InterestGroups.NotifyPlayer("🗳 竞选期开始(为期 " + CampaignDays + " 天): 各集团开始角逐", true);
                DLog.Force("选举: 竞选期开始(第 " + Politics.Today() + " 天)");
            }
            catch { }
        }

        private static void FluctuateMomentum()
        {
            try
            {
                var rnd = new Random(Politics.Today() * 31 + 7);
                for (int g = 0; g < InterestGroups.GroupCount; g++)
                {
                    var grp = InterestGroups.G[g];
                    if (grp == null) continue;
                    float swing = (float)(rnd.NextDouble() * 45.0 - 20.0);   // -20 ~ +25(V3: 动量波动)
                    Momentum[g] += swing;
                    if (Momentum[g] > 100f) Momentum[g] = 100f;
                    if (Momentum[g] < -50f) Momentum[g] = -50f;
                }
            }
            catch { }
        }

        internal static string RunElection(int day)
        {
            try
            {
                LastElectionDay = day;
                CampaignActive = false;
                float up, mid, low;
                VoteWeights(out up, out mid, out low);
                float best = -1f;
                int winner = -1;
                for (int g = 1; g < InterestGroups.GroupCount; g++)
                {
                    var grp = InterestGroups.G[g];
                    if (grp == null) continue;
                    if (InterestGroups.IsMarginalized(g)) { VoteStrength[g] = 0f; continue; }
                    // 得票 = 影响力 × 动量修正 × 阶层票值修正
                    float stratum = grp.Share >= 0.15f ? up : (grp.Share >= 0.08f ? mid : low);
                    float vote = grp.Share * (1f + Momentum[g] / 100f) * stratum;
                    if (vote < 0f) vote = 0f;
                    VoteStrength[g] = vote;
                    if (vote > best) { best = vote; winner = g; }
                }
                LastWinnerGroup = winner;
                string result = winner >= 0 ? InterestGroups.NameOf(winner) + " 赢得最多选票" : "无胜者";
                LastResult = result;
                // 选举效应: 合法性波动 + 民意(V3: 选举不直接改组政府, 但可免费改组一次)
                Politics.Legitimacy = LawSystem.ClampF(Politics.Legitimacy + LegitFromVotes() * 0.3f + 2f, 0f, 100f);
                Pops.ShiftRadicals(-0.008f);
                InterestGroups.NotifyPlayer("🗳 大选结果: " + result + "(选后 42 天内改组政府免费, 不激化下野集团)", true);
                DLog.Force("选举: " + result + " (第 " + day + " 天)");
                return result;
            }
            catch { return "选举异常"; }
        }

        internal static string StatusText()
        {
            try
            {
                if (!HasVoting()) return "选举: 未开(需权力分配 ≥ 财产选举)";
                if (CampaignActive)
                    return "竞选期: 剩 " + Math.Max(0, LastElectionDay + CycleDays - Politics.Today()) + " 天开庭 · 领跑 "
                        + (LastWinnerGroup >= 0 ? InterestGroups.NameOf(LastWinnerGroup) : "未定");
                int d = DaysToNext();
                string s = "选举: 下次 " + d + " 天后";
                if (InFreeReformWindow()) s += "(改组免费窗口中)";
                if (LastWinnerGroup >= 0) s += " · 上届 " + InterestGroups.NameOf(LastWinnerGroup) + " 胜出";
                return s;
            }
            catch { return ""; }
        }

        internal static string MomentumText()
        {
            try
            {
                if (!CampaignActive) return "";
                var list = new List<KeyValuePair<int, float>>();
                for (int g = 1; g < InterestGroups.GroupCount; g++)
                    if (InterestGroups.G[g] != null && !InterestGroups.IsMarginalized(g))
                        list.Add(new KeyValuePair<int, float>(g, Momentum[g]));
                list.Sort(delegate (KeyValuePair<int, float> a, KeyValuePair<int, float> b) { return b.Value.CompareTo(a.Value); });
                string s = "竞选动量: ";
                for (int i = 0; i < list.Count && i < 4; i++)
                {
                    if (i > 0) s += " · ";
                    s += InterestGroups.NameOf(list[i].Key) + (list[i].Value >= 0 ? " +" : " ") + (int)list[i].Value;
                }
                return s;
            }
            catch { return ""; }
        }

        internal static string Save()
        {
            var sb = new StringBuilder();
            sb.Append("v2;");
            sb.Append(LastElectionDay).Append(',').Append(CampaignStartDay).Append(',').Append(CampaignActive ? "1" : "0").Append(',')
              .Append(LastWinnerGroup).Append(',');
            for (int g = 0; g < InterestGroups.GroupCount; g++) { if (g > 0) sb.Append(','); sb.Append(((int)Momentum[g])); }
            sb.Append(';');
            for (int g = 0; g < InterestGroups.GroupCount; g++) { if (g > 0) sb.Append(','); sb.Append(((int)(VoteStrength[g] * 1000))); }
            sb.Append(';');
            return sb.ToString();
        }

        internal static void Load(string data)
        {
            try
            {
                if (string.IsNullOrEmpty(data)) return;
                var seg = data.Split(';');
                if (seg.Length > 1)
                {
                    var f = seg[1].Split(',');
                    int x;
                    if (f.Length > 0 && int.TryParse(f[0], out x)) LastElectionDay = x;
                    if (f.Length > 1 && int.TryParse(f[1], out x)) CampaignStartDay = x;
                    if (f.Length > 2) CampaignActive = f[2] == "1";
                    if (f.Length > 3 && int.TryParse(f[3], out x)) LastWinnerGroup = x;
                    for (int g = 0; g < InterestGroups.GroupCount && 4 + g < f.Length; g++)
                        if (int.TryParse(f[4 + g], out x)) Momentum[g] = x;
                }
                if (seg.Length > 2 && !string.IsNullOrEmpty(seg[2]))
                {
                    var f = seg[2].Split(',');
                    for (int g = 0; g < InterestGroups.GroupCount && g < f.Length; g++)
                    {
                        int x;
                        if (int.TryParse(f[g], out x)) VoteStrength[g] = x / 1000f;
                    }
                }
                DLog.Force("选举: 读档 " + StatusText());
            }
            catch { }
        }

        internal static void Reset()
        {
            LastElectionDay = -9999;
            CampaignStartDay = -9999;
            CampaignActive = false;
            LastWinnerGroup = -1;
            LastResult = "";
            _lastMomentumDay = -9999;
            for (int g = 0; g < InterestGroups.GroupCount; g++) { Momentum[g] = 0f; VoteStrength[g] = 0f; }
        }
    }
}
