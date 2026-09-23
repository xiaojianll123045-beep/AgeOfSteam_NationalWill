using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
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

        // ================= v5.x: AI 选举(AI 段) =================
        //   AI 国家按自己的权力分配档位跑 4 年周期; 竞选期 42 天: 压税/游说加票/不启战端;
        //   开票后若胜选集团诉求与议程不符 -> 转联盟/妥协(AiAgenda.NoteElectionLoss)。
        internal class AiElectState
        {
            internal int LastDay = -9999;
            internal int CampaignStart = -9999;
            internal bool Campaign;
            internal int Winner = -1;
            internal readonly float[] Momentum = new float[InterestGroups.GroupCount];
        }

        private static readonly Dictionary<string, AiElectState> AiMap = new Dictionary<string, AiElectState>();
        private static int _aiLastDay = -1;

        private static AiElectState AiOf(Kingdom k)
        {
            try
            {
                if (k == null || string.IsNullOrEmpty(k.StringId)) return null;
                AiElectState st;
                if (!AiMap.TryGetValue(k.StringId, out st)) { st = new AiElectState(); AiMap[k.StringId] = st; }
                return st;
            }
            catch { return null; }
        }

        internal static bool AiInCampaign(Kingdom k)
        {
            try
            {
                var st = AiOf(k);
                return st != null && st.Campaign;
            }
            catch { return false; }
        }

        internal static int AiDaysToElection(Kingdom k)
        {
            try
            {
                var st = AiOf(k);
                if (st == null || st.LastDay < 0) return -1;
                return Math.Max(0, st.LastDay + CycleDays - Politics.Today());
            }
            catch { return -1; }
        }

        // 月度: 推进各国选举周期与竞选行为(由 AiDiplomacy.Month 调用)
        internal static void AiMonthly(int day)
        {
            try
            {
                if (day < _aiLastDay) AiMap.Clear();   // 新档/读档 -> 重置
                _aiLastDay = day;
                var pk = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                foreach (var k in Kingdom.All)
                {
                    if (k == null || k.IsEliminated) continue;
                    if (pk != null && ReferenceEquals(pk, k)) continue;
                    try { AiStep(k, day); } catch { }
                }
            }
            catch (Exception ex) { DLog.Force("AI 选举月结异常: " + ex.Message); }
        }

        private static void AiStep(Kingdom k, int day)
        {
            var st = AiOf(k);
            if (st == null) return;
            int franchise = 0;
            try { franchise = Parliament.AiLevelOf(k, LawSystem.LFranchise); } catch { }
            if (franchise <= 0) { st.Campaign = false; return; }   // 无选举 -> 不竞选
            if (st.LastDay < 0) { st.LastDay = day; return; }
            int next = st.LastDay + CycleDays;
            if (!st.Campaign && day >= next - CampaignDays)
            {
                st.Campaign = true;
                st.CampaignStart = day;
                AiAgenda.LogPol(k, "竞选期开始(大选剩 " + Math.Max(0, next - day) + " 天)");
            }
            if (st.Campaign)
            {
                AiCampaign(k, st);
                for (int w = 0; w < 4; w++) AiFluctuate(st, day + w * 7);
            }
            if (day >= next) AiRunElection(k, st, day);
        }

        // 竞选期: 压税安抚 + 游说加票(避免做惹怒选民的事)
        private static void AiCampaign(Kingdom k, AiElectState st)
        {
            try
            {
                int tax;
                if (AiEconomyDeep.TaxLevel.TryGetValue(k.StringId, out tax) && tax > 1)
                    AiEconomyDeep.TaxLevel[k.StringId] = tax - 1;
                try { Parliament.AiLobbyPush(k, "民权"); } catch { }
                if (MBRandom.RandomFloat < 0.5f) { try { Parliament.AiLobbyPush(k, "权力"); } catch { } }
            }
            catch { }
        }

        private static void AiFluctuate(AiElectState st, int day)
        {
            try
            {
                var rnd = new Random(day * 31 + 11);
                for (int g = 0; g < InterestGroups.GroupCount; g++)
                {
                    float swing = (float)(rnd.NextDouble() * 40.0 - 18.0);
                    st.Momentum[g] = Math.Max(-50f, Math.Min(100f, st.Momentum[g] + swing));
                }
            }
            catch { }
        }

        private static void AiRunElection(Kingdom k, AiElectState st, int day)
        {
            try
            {
                st.LastDay = day;
                st.Campaign = false;
                int winner = -1;
                float best = -1f;
                int[] seats = null;
                try { seats = Parliament.AiSeatsOf(k); } catch { }
                for (int g = 1; g < InterestGroups.GroupCount; g++)
                {
                    float s = seats != null ? seats[g] : 0f;
                    float vote = s * (1f + st.Momentum[g] / 100f);
                    if (vote > best) { best = vote; winner = g; }
                }
                st.Winner = winner;
                bool lost = false;
                try
                {
                    int law = AiAgenda.LawOf(k, 0);
                    if (law < 0) law = AiAgenda.LawOf(k, 1);
                    if (law >= 0 && winner > 0)
                    {
                        int pref = -1, pv = 8;
                        for (int i = 0; i < LawSystem.LawCount; i++)
                        {
                            var d = LawSystem.Def(i);
                            if (d == null || !string.IsNullOrEmpty(d.Scope)) continue;
                            if (Parliament.AiLevelOf(k, i) >= LawSystem.TierCount(i) - 1) continue;
                            int v = LawSystem.AdvanceStance(i, winner);
                            if (v > pv) { pv = v; pref = i; }
                        }
                        lost = pref >= 0 && pref != law;
                    }
                }
                catch { }
                if (lost) { try { AiAgenda.NoteElectionLoss(k, winner); } catch { } }
                else AiAgenda.LogPol(k, "大选: " + (winner >= 0 ? InterestGroups.NameOf(winner) : "无") + " 胜出(议程不变)");
                for (int g = 0; g < InterestGroups.GroupCount; g++) st.Momentum[g] = 0f;
            }
            catch { }
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
            // v5.x: AI 选举段(旧档无此段, Load 兼容)
            sb.Append('~');
            foreach (var kv in AiMap)
            {
                var st = kv.Value;
                if (st == null) continue;
                sb.Append(kv.Key).Append(',').Append(st.LastDay).Append(',').Append(st.CampaignStart).Append(',')
                  .Append(st.Campaign ? "1" : "0").Append(',').Append(st.Winner).Append(';');
            }
            return sb.ToString();
        }

        internal static void Load(string data)
        {
            try
            {
                if (string.IsNullOrEmpty(data)) return;
                string aiPart = null;
                int tilde = data.IndexOf('~');
                if (tilde >= 0) { aiPart = data.Substring(tilde + 1); data = data.Substring(0, tilde); }
                LoadAi(aiPart);
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

        private static void LoadAi(string aiPart)
        {
            try
            {
                AiMap.Clear();
                if (string.IsNullOrEmpty(aiPart)) return;
                foreach (var seg in aiPart.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var f = seg.Split(',');
                    if (f.Length < 5) continue;
                    var st = new AiElectState();
                    int v;
                    if (int.TryParse(f[1], out v)) st.LastDay = v;
                    if (int.TryParse(f[2], out v)) st.CampaignStart = v;
                    st.Campaign = f[3] == "1";
                    if (int.TryParse(f[4], out v)) st.Winner = v;
                    AiMap[f[0]] = st;
                }
                DLog.Force("选举: AI 读档 " + AiMap.Count + " 国");
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
            AiMap.Clear();      // v5.x: AI 选举状态
            _aiLastDay = -1;
        }
    }
}
