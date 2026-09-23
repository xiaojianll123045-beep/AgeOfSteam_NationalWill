using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace FeudalInternalAffairs
{
    // 大立法 / 议会(文档 24.12 / P25; v4.189)
    //   席位 = 利益集团政治力量(Clout)归一化到 100 席; 议案表决 = Σ(席位 × 该集团对该档的支持度)
    //   表决通过 -> 议案直接推进一档(LawSystem.ForceAdvance); 未通过 -> 合法性 -3
    internal static class Parliament
    {
        internal const int TotalSeats = 100;

        internal static int SeatsOf(int g)
        {
            try
            {
                float sum = 0f;
                for (int i = 0; i < InterestGroups.GroupCount; i++) sum += Math.Max(0f, InterestGroups.CloutOf(i));
                if (sum <= 0.01f) return 0;
                return (int)Math.Round(Math.Max(0f, InterestGroups.CloutOf(g)) / sum * TotalSeats);
            }
            catch { return 0; }
        }

        // 某集团对"法律 law 换到 target 档"的支持度 0..1
        internal static float SupportOf(int law, int target, int g)
        {
            try
            {
                int att = LawSystem.AttOf(law, target, g);
                float v = 0.5f + att / 40f;
                return v < 0f ? 0f : (v > 1f ? 1f : v);
            }
            catch { return 0.5f; }
        }

        internal static int YesSeats(int law, int target)
        {
            int yes = 0;
            for (int g = 0; g < InterestGroups.GroupCount; g++)
            {
                int s = SeatsOf(g);
                if (s <= 0) continue;
                yes += (int)Math.Round(s * SupportOf(law, target, g));
            }
            return yes;
        }

        internal static string SeatText()
        {
            try
            {
                var sb = new System.Text.StringBuilder();
                for (int g = 0; g < InterestGroups.GroupCount; g++)
                {
                    int s = SeatsOf(g);
                    if (s <= 0) continue;
                    sb.Append(InterestGroups.NameOf(g)).Append(' ').Append(s).Append(" 席  ");
                }
                return sb.Length > 0 ? sb.ToString() : "尚无席位数据。";
            }
            catch { return ""; }
        }

        // 议会表决: 通过则议案推进一档
        internal static string Vote(int law, int target)
        {
            try
            {
                if (law < 0 || law >= LawSystem.LawCount) return "法律无效。";
                if (!LawSystem.IsAvailable(law)) return "该法律不适用于当前国家。";
                int yes = YesSeats(law, target);
                int no = TotalSeats - yes;
                string head = "《" + LawSystem.NameOf(law) + "》→ " + LawSystem.TierName(law, target)
                            + "\n赞成 " + yes + " 席 / 反对 " + no + " 席\n";
                if (yes > TotalSeats / 2)
                {
                    try { LawSystem.ForceAdvance(law); } catch { }
                    try { Politics.Legitimacy = Politics.ClampF(Politics.Legitimacy + 2f, 0f, 100f); } catch { }
                    DLog.Force("议会: 《" + LawSystem.NameOf(law) + "》通过(" + yes + " 席)");
                    return head + "议案通过, 法律已推进。";
                }
                try { Politics.Legitimacy = Politics.ClampF(Politics.Legitimacy - 3f, 0f, 100f); } catch { }
                DLog.Force("议会: 《" + LawSystem.NameOf(law) + "》被否决(" + yes + " 席)");
                return head + "议案被否决(合法性 -3)。可先提升相关集团满意度再表决。";
            }
            catch (Exception ex) { return "表决失败: " + ex.Message; }
        }

        // ================= v4.21x: AI 议会自动投票 =================
        //   AI 无本国集团数据 -> 席位按性格估算(军方∝好战/商帮行会∝重商/地方农民∝建设),
        //   赞成票仍用法律自身态度表(LawSystem.AttOf)按集团立场计算; 通过后记入 AI 自己的法档,
        //   态度变化转为该国人口立场(与 AI 经济工具同口径)。
        private static readonly Dictionary<string, int[]> AiLaw = new Dictionary<string, int[]>();
        private static readonly Dictionary<string, float> AiLobby = new Dictionary<string, float>();   // "王国|类别" -> 加票
        private static int _aiLastDay = -1;

        internal static void AiLobbyPush(Kingdom k, string cat)
        {
            try
            {
                if (k == null || string.IsNullOrEmpty(cat)) return;
                string key = k.StringId + "|" + cat;
                float v;
                AiLobby.TryGetValue(key, out v);
                AiLobby[key] = Math.Min(12f, v + 6f);   // 单类至多加 12 席
            }
            catch { }
        }

        internal static int[] AiSeatsOf(Kingdom k)
        {
            var s = new int[InterestGroups.GroupCount];
            try
            {
                int agg = AiPersonality.AggressionOf(k), dev = AiPersonality.DevelopmentOf(k), com = AiPersonality.CommerceOf(k);
                s[0] = 15;                 // 王室
                s[1] = 8 + dev / 10;       // 大贵族
                s[2] = 10 + dev / 8;       // 地方贵族
                s[3] = 10;                 // 教会
                s[4] = 5 + com / 8;        // 商帮
                s[5] = 5 + com / 10;       // 行会
                s[6] = 12 + dev / 10;      // 农民
                s[7] = 5 + agg / 8;        // 军队
            }
            catch { }
            return s;
        }

        internal static int AiYesSeatsOf(Kingdom k, int law, int target)
        {
            try
            {
                var s = AiSeatsOf(k);
                int all = 0;
                float yes = 0f;
                for (int g = 0; g < s.Length; g++)
                {
                    int n = s[g];
                    if (n <= 0) continue;
                    all += n;
                    yes += n * SupportOf(law, target, g);
                }
                if (all <= 0) return 0;
                yes = yes / all * TotalSeats;
                float bonus;
                if (AiLobby.TryGetValue(k.StringId + "|" + LawSystem.CatOf(law), out bonus)) yes += bonus;
                if (yes < 0f) yes = 0f;
                if (yes > TotalSeats) yes = TotalSeats;
                return (int)Math.Round(yes);
            }
            catch { return 0; }
        }

        // 集团情绪代理(AI 无本国 IG 数据): 按现行法律档位对该集团的平均支持度(0~1)
        internal static float AiGroupMood(Kingdom k, int g)
        {
            try
            {
                if (k == null || g < 0 || g >= InterestGroups.GroupCount) return 0.5f;
                float sum = 0f;
                int n = 0;
                for (int i = 0; i < LawSystem.LawCount; i++)
                {
                    var d = LawSystem.Def(i);
                    if (d == null || !string.IsNullOrEmpty(d.Scope)) continue;
                    sum += SupportOf(i, AiLevelOf(k, i), g);
                    n++;
                }
                return n > 0 ? sum / n : 0.5f;
            }
            catch { return 0.5f; }
        }

        // 该国当前法律档位(供 AI 议程/选举读取)
        internal static int AiLevelOf(Kingdom k, int law)
        {
            try
            {
                if (k == null || law < 0 || law >= LawSystem.LawCount) return 0;
                int[] lv;
                if (!AiLaw.TryGetValue(k.StringId, out lv) || lv == null) return 0;
                return lv[law];
            }
            catch { return 0; }
        }

        internal static void AiMonthly(int day)
        {
            try
            {
                if (day < _aiLastDay) { AiLaw.Clear(); AiLobby.Clear(); }   // 新档/读档 -> 重置
                _aiLastDay = day;
                var pk = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                foreach (var k in Kingdom.All)
                {
                    if (k == null || k.IsEliminated) continue;
                    if (pk != null && ReferenceEquals(pk, k)) continue;
                    try { AiVote(k, day); } catch { }
                }
                var keys = new List<string>(AiLobby.Keys);   // 游说加票每月衰减
                for (int i = 0; i < keys.Count; i++)
                {
                    float v = AiLobby[keys[i]] * 0.7f;
                    if (v < 0.5f) AiLobby.Remove(keys[i]); else AiLobby[keys[i]] = v;
                }
            }
            catch (Exception ex) { DLog.Force("AI 议会月结异常: " + ex.Message); }
        }

        private static void AiVote(Kingdom k, int day)
        {
            try { AiAgenda.Ensure(k, day); } catch { }
            int[] lv;
            if (!AiLaw.TryGetValue(k.StringId, out lv)) { lv = new int[LawSystem.LawCount]; AiLaw[k.StringId] = lv; }

            // 1) 议程目标优先(每次只盯 1~2 部; 预计不过半 -> 攒票不表决)
            for (int slot = 0; slot < 2; slot++)
            {
                int law = AiAgenda.LawOf(k, slot);
                if (law < 0 || law >= LawSystem.LawCount) continue;
                var d = LawSystem.Def(law);
                if (d == null || !string.IsNullOrEmpty(d.Scope)) continue;
                if (lv[law] >= LawSystem.TierCount(law) - 1) continue;
                int target = lv[law] + 1;
                int yes = AiYesSeatsOf(k, law, target);
                if (yes <= TotalSeats / 2)
                {
                    try { AiLobbyPush(k, d.Cat); } catch { }                   // 攒票: 继续游说该类别
                    try { AiAgenda.NoteLawResult(k, law, false, yes); } catch { }   // 差得远 -> 计数换目标
                    continue;
                }
                if (MBRandom.RandomFloat > 0.65f) return;
                DoAiVote(k, lv, law, target, yes);
                return;
            }

            // 2) 无议程 -> 旧逻辑兜底(选预票最高的一部, 50% 概率表决)
            int bestLaw = -1, bestTarget = -1, bestYes = 0;
            for (int i = 0; i < LawSystem.LawCount; i++)
            {
                try
                {
                    var d = LawSystem.Def(i);
                    if (d == null || !string.IsNullOrEmpty(d.Scope)) continue;   // 通用法律才进 AI 议会
                    if (lv[i] >= LawSystem.TierCount(i) - 1) continue;
                    int t = lv[i] + 1;
                    int yes = AiYesSeatsOf(k, i, t);
                    if (yes <= TotalSeats / 2) continue;                          // 预计不过半不提
                    if (d.Cat == "权力") yes += AiPersonality.AggressionOf(k) / 10;
                    else if (d.Cat == "经济") yes += AiPersonality.CommerceOf(k) / 10;
                    else yes += AiPersonality.DevelopmentOf(k) / 10;
                    if (yes > bestYes) { bestYes = yes; bestLaw = i; bestTarget = t; }
                }
                catch { }
            }
            if (bestLaw < 0) return;
            if (MBRandom.RandomFloat > 0.5f) return;
            int finalYes = AiYesSeatsOf(k, bestLaw, bestTarget);
            if (finalYes > TotalSeats / 2)
            {
                DoAiVote(k, lv, bestLaw, bestTarget, finalYes);
            }
            else
            {
                Decrees.AiAuthAdd(k, -4f);
                try { AiAgenda.NoteLawResult(k, bestLaw, false, finalYes); } catch { }
                AiAgenda.LogPol(k, "议会否决《" + LawSystem.NameOf(bestLaw) + "》(" + finalYes + " 席)");
            }
        }

        private static void DoAiVote(Kingdom k, int[] lv, int law, int target, int yes)
        {
            try
            {
                var delta = new int[InterestGroups.GroupCount];
                for (int g = 0; g < delta.Length; g++)
                    delta[g] = LawSystem.AttOf(law, target, g) - LawSystem.AttOf(law, target - 1, g);
                lv[law] = target;
                try { AiEconomyDeep.ShiftPublicStance(k, delta); } catch { }
                Decrees.AiAuthAdd(k, 4f);
                try { AiAgenda.NoteLawResult(k, law, true, yes); } catch { }
                AiAgenda.LogPol(k, "议会通过《" + LawSystem.NameOf(law) + "》→ "
                    + LawSystem.TierName(law, target) + "(" + yes + " 席)");
            }
            catch { }
        }

        // 议会 UI: 先选法律, 再选目标档, 然后表决
        internal static void ShowUi()
        {
            try
            {
                var opts = new List<InquiryElement>();
                for (int i = 0; i < LawSystem.LawCount; i++)
                {
                    if (!LawSystem.IsAvailable(i)) continue;
                    if (LawSystem.Maxed(i)) continue;
                    int t = LawSystem.NextTier(i);
                    opts.Add(new InquiryElement(i.ToString(), LawSystem.NameOf(i) + " → " + LawSystem.TierName(i, t),
                        null, true, "赞成预计 " + YesSeats(i, t) + " / 100 席"));
                }
                PanelInputGuard.ShowPopup(new MultiSelectionInquiryData(
                    "议会表决",
                    "席位分配(按集团政治力量): " + SeatText() + "\n过半(51 席)即通过, 通过后法律立即推进一档; 否决则合法性 -3",
                    opts, true, 1, 1, "表决", "取消",
                    delegate (List<InquiryElement> sel)
                    {
                        if (sel == null || sel.Count == 0) return;
                        int law;
                        if (!int.TryParse(sel[0].Identifier as string, out law)) return;
                        string msg = Vote(law, LawSystem.NextTier(law));
                        try { MapSelection.Message(msg); } catch { }
                    }, null, null, false));
            }
            catch (Exception ex) { DLog.Force("议会界面失败: " + ex.Message); }
        }
    }
}
