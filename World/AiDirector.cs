using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;

namespace FeudalInternalAffairs
{
    internal static class AiDirector
    {
        internal enum Goal { Survival, Economy, Military, Politics, Consolidation }

        private class Brain
        {
            internal Goal Goal = Goal.Economy;
            internal Goal Candidate = Goal.Economy;
            internal int Streak;
            internal float Threat;
            internal float Aggro;
            internal float ArmyRatio = 1f;
            internal int Gold;
            internal int Reserve;
            internal bool WarWant;
            internal string TargetId;
            internal int LastMonthDay = -9999;
            internal int Vassals;
        }

        private static readonly Dictionary<string, Brain> Map = new Dictionary<string, Brain>();
        private static readonly Dictionary<string, int> LastTerritory = new Dictionary<string, int>();

        internal static Goal GoalOf(Kingdom k) { var b = Ensure(k); return b != null ? b.Goal : Goal.Economy; }

        internal static float ThreatOf(Kingdom k) { var b = Ensure(k); return b != null ? b.Threat : 0f; }

        internal static float Aggression(Kingdom k) { var b = Ensure(k); return b != null ? b.Aggro : 0.5f; }

        internal static bool WantsWar(Kingdom k) { var b = Ensure(k); return b != null && b.WarWant; }

        internal static string WarTargetId(Kingdom k) { var b = Ensure(k); return b != null ? b.TargetId : null; }

        internal static int ReserveGold(Kingdom k)
        {
            try
            {
                var b = Ensure(k);
                if (b == null) return 0;
                if (b.Reserve <= 0) b.Reserve = ComputeReserve(k);
                return b.Reserve;
            }
            catch { return 0; }
        }

        internal static int BudgetFor(Kingdom k, string domain, int gold)
        {
            try
            {
                if (gold <= 0 || string.IsNullOrEmpty(domain)) return 0;
                if (domain == "res") return Math.Min(gold, ReserveGold(k));
                var b = Ensure(k);
                int spendable = gold - ReserveGold(k);
                if (spendable <= 0) return 0;
                float pct = PctOf(b != null ? b.Goal : Goal.Economy, domain);
                if (pct <= 0f) return 0;
                long v = (long)Math.Round(spendable * (double)pct);
                if (v > gold) v = gold;
                if (v < 0) v = 0;
                return (int)v;
            }
            catch { return 0; }
        }

        internal static void Daily(Kingdom k)
        {
            try
            {
                var b = Ensure(k);
                if (b == null) return;
                b.Gold = (int)WarEconomy.GoldOfPublic(k);
            }
            catch { }
        }

        internal static void Monthly(Kingdom k)
        {
            try
            {
                var b = Ensure(k);
                if (b == null) return;
                int day = Today();
                if (b.LastMonthDay == day) return;
                b.LastMonthDay = day;

                bool playerK = IsPlayerKingdom(k);
                float myStr = Math.Max(1f, WarPlans.StrengthOf(k));
                int gold = (int)WarEconomy.GoldOfPublic(k);
                int reserve = ComputeReserve(k);
                float wear = WarWeariness.MaxWearOf(k);
                bool crisis = WarEconomy.IsCrisis(k);

                int wars = 0, neighbors = 0, borderAggN = 0, enemyAggSum = 0, borderAggSum = 0;
                float enemyStr = 0f, neighborStr = 0f;
                var all = new List<Kingdom>();
                foreach (var e in Kingdom.All)
                {
                    if (e == null || e.IsEliminated || ReferenceEquals(e, k)) continue;
                    all.Add(e);
                    float es = Math.Max(1f, WarPlans.StrengthOf(e));
                    bool war = false;
                    try { war = k.IsAtWarWith(e); } catch { }
                    if (war)
                    {
                        wars++;
                        enemyStr += es;
                        enemyAggSum += AiPersonality.AggressionOf(e);
                        continue;
                    }
                    if (Diplomacy.IsAlly(k, e)) continue;
                    if (AiDiplomacy.Borders(k, e))
                    {
                        neighbors++;
                        neighborStr += es;
                        borderAggSum += AiPersonality.AggressionOf(e);
                        borderAggN++;
                    }
                }

                float threat = 0f;
                if (wars > 0)
                {
                    float er = enemyStr / myStr;
                    threat += Math.Min(0.50f, 0.18f * wars + Math.Max(0f, er - 1f) * 0.25f);
                    threat += Math.Min(0.15f, enemyAggSum / (wars * 100f) * 0.15f);
                }
                if (neighbors > 0)
                {
                    float nr = neighborStr / neighbors / myStr;
                    if (nr > 1f) threat += Math.Min(0.35f, 0.10f + (nr - 1f) * 0.30f);
                    threat += Math.Min(0.12f, borderAggSum / (borderAggN * 100f) * 0.12f);
                }
                threat += wear / 100f * 0.15f;
                if (crisis) threat += 0.10f;
                if (threat < 0f) threat = 0f;
                if (threat > 1f) threat = 1f;

                float avgN = neighbors > 0 ? neighborStr / neighbors : (wars > 0 ? enemyStr / wars : myStr);
                float ratio = avgN > 0.01f ? myStr / avgN : 1.2f;
                float aggro = AiPersonality.AggressionOf(k) / 100f * 0.60f;
                if (wars == 0) aggro += 0.15f;
                if (ratio >= 1.25f) aggro += 0.15f;
                else if (ratio >= 1.00f) aggro += 0.08f;
                if (gold >= reserve * 2) aggro += 0.08f;
                aggro -= wear / 100f * 0.25f;
                if (crisis) aggro -= 0.20f;
                if (aggro < 0f) aggro = 0f;
                if (aggro > 1f) aggro = 1f;

                int terr = k.Settlements != null ? k.Settlements.Count : 0;
                int prevT;
                int gainedT = LastTerritory.TryGetValue(k.StringId, out prevT) ? terr - prevT : 0;
                LastTerritory[k.StringId] = terr;
                int vassals = CountVassals(k);
                int gainedV = vassals - b.Vassals;
                b.Vassals = vassals;

                float sSurv = threat * 2f + wars * 0.45f + (crisis ? 0.6f : 0f) + (threat >= 0.70f ? 1f : 0f);
                float sCons = (gainedT > 0 ? 1f + Math.Min(1f, gainedT * 0.25f) : 0f)
                    + (gainedV > 0 ? 1f : 0f) + Math.Min(0.6f, vassals * 0.2f);

                float unstable = 0f;
                if (playerK)
                {
                    float legit = Politics.Legitimacy;
                    if (legit < 35f) unstable += (35f - legit) / 35f * 1.2f;
                    float rad = Politics.AvgRadicalism();
                    if (rad > 0.35f) unstable += Math.Min(1f, (rad - 0.35f) * 2f);
                    int elec = Elections.DaysToNext();
                    if (elec >= 0 && elec <= Elections.CampaignDays) unstable += 0.8f;
                }
                else
                {
                    if (wear >= 50f) unstable += (wear - 50f) / 50f * 0.8f;
                    if (crisis) unstable += 0.5f;
                }

                int railMax = Railways.MaxLines(k);
                float infraWeak = railMax > 0 ? 1f - Math.Min(1f, CountRailLines(k) / (float)railMax) : 0f;
                bool rich = gold >= reserve + 8000;

                float sEco = (rich ? 0.7f : 0f) + AiPersonality.DevelopmentOf(k) / 100f * 0.8f + infraWeak * 0.6f
                    - threat * 0.9f - (wars > 0 ? 0.5f : 0f);
                float sMil = aggro * 1.2f + (ratio >= 1.15f ? 0.4f : 0f) + (wars > 0 ? 0.3f : 0f)
                    - threat * 1.4f - wear / 200f;
                float sPol = unstable;

                Goal best = Goal.Economy;
                float bestScore = sEco;
                if (sMil > bestScore) { best = Goal.Military; bestScore = sMil; }
                if (sPol > bestScore) { best = Goal.Politics; bestScore = sPol; }
                if (sCons > bestScore) { best = Goal.Consolidation; bestScore = sCons; }
                bool survivalNow = threat >= 0.70f || (wars >= 2 && threat >= 0.50f) || (crisis && threat >= 0.45f);
                if (survivalNow && best != Goal.Survival) { best = Goal.Survival; bestScore = sSurv + 2f; }

                float curScore = ScoreOf(b.Goal, sSurv, sEco, sMil, sPol, sCons);
                if (best != b.Goal)
                {
                    if (best == b.Candidate) b.Streak++;
                    else { b.Candidate = best; b.Streak = 1; }
                    bool significant = bestScore - curScore >= 1.0f || best == Goal.Survival;
                    if (b.Streak >= 2 || significant)
                    {
                        DLog.Force("AI 战略切换: " + k.Name + " " + b.Goal + " -> " + best
                            + " (威胁 " + threat.ToString("F2") + ", 好战 " + aggro.ToString("F2") + ", 金 " + gold + ")");
                        b.Goal = best;
                        b.Streak = 0;
                    }
                }
                else { b.Candidate = b.Goal; b.Streak = 0; }

                var cands = new List<Kingdom>();
                for (int i = 0; i < all.Count; i++)
                {
                    var e = all[i];
                    bool war = false;
                    try { war = k.IsAtWarWith(e); } catch { }
                    if (war) { cands.Add(e); continue; }
                    if (!AiDiplomacy.Borders(k, e)) continue;
                    if (Diplomacy.IsAlly(k, e)) continue;
                    if (DiploPlays.IsVassalOf(e.StringId, k.StringId)) continue;
                    cands.Add(e);
                }
                Kingdom bestT = null;
                float bestV = -1f;
                for (int i = 0; i < cands.Count; i++)
                {
                    var e = cands[i];
                    float es = Math.Max(1f, WarPlans.StrengthOf(e));
                    float weak = 1f - Math.Min(1f, es / (myStr + es));
                    int railVal = CountRailLines(e);
                    int terrVal = e.Settlements != null ? e.Settlements.Count : 0;
                    float val = weak * 1.5f + Math.Min(1f, railVal / 4f) * 0.5f + Math.Min(1f, terrVal / 20f) * 0.5f;
                    bool war = false;
                    try { war = k.IsAtWarWith(e); } catch { }
                    if (war) val += 0.3f;
                    try { var plan = WarPlans.Get(k, e); if (plan != null && !string.IsNullOrEmpty(plan.MainTargetId)) val += 0.4f; } catch { }
                    if (val > bestV) { bestV = val; bestT = e; }
                }

                bool wants = bestT != null && threat < 0.45f && aggro >= 0.55f && ratio >= 1.15f
                    && gold >= reserve && wear < 45f && !crisis && wars < 2;

                b.Threat = threat;
                b.Aggro = aggro;
                b.ArmyRatio = ratio;
                b.Gold = gold;
                b.Reserve = reserve;
                b.WarWant = wants;
                b.TargetId = (wars > 0 || wants) && bestT != null ? bestT.StringId : null;

                DLog.Force("AI 战略: " + k.Name + " 目标=" + b.Goal + " 威胁=" + threat.ToString("F2")
                    + " 好战=" + aggro.ToString("F2") + " 储备=" + reserve);
            }
            catch (Exception ex) { DLog.Force("AI 战略月结异常: " + ex.Message); }
        }

        internal static void Reset()
        {
            try { Map.Clear(); LastTerritory.Clear(); } catch { }
        }

        private static Brain Ensure(Kingdom k)
        {
            try
            {
                if (k == null || k.IsEliminated) return null;
                Brain b;
                if (Map.TryGetValue(k.StringId, out b) && b != null) return b;
                int agg = AiPersonality.AggressionOf(k), dev = AiPersonality.DevelopmentOf(k);
                b = new Brain { Goal = dev >= agg ? Goal.Economy : Goal.Military };
                b.Candidate = b.Goal;
                Map[k.StringId] = b;
                return b;
            }
            catch { return null; }
        }

        private static int Today()
        {
            try { return (int)CampaignTime.Now.ToDays; } catch { return 0; }
        }

        private static bool IsPlayerKingdom(Kingdom k)
        {
            try { return k != null && NationalWillOrders.Behavior != null && ReferenceEquals(NationalWillOrders.Behavior.NationKingdom, k); }
            catch { return false; }
        }

        private static int ComputeReserve(Kingdom k)
        {
            try
            {
                if (k == null) return 2000;
                int wage30;
                if (IsPlayerKingdom(k)) wage30 = DefArmy.DailyWageCost() * 30;
                else wage30 = (int)Math.Ceiling(Math.Max(0f, WarPlans.StrengthOf(k)) * DefArmy.WagePerManPerDay) * 30;
                int railDay = 0;
                for (int i = 0; i < Railways.All.Count; i++)
                {
                    var l = Railways.All[i];
                    if (l == null || !l.Built || l.OwnerId != k.StringId) continue;
                    railDay += Railways.MaintenanceOf(l);
                }
                return wage30 + railDay * 30 + 2000;
            }
            catch { return 2000; }
        }

        private static int CountRailLines(Kingdom k)
        {
            int n = 0;
            try
            {
                if (k == null) return 0;
                for (int i = 0; i < Railways.All.Count; i++)
                {
                    var l = Railways.All[i];
                    if (l == null || !l.Built) continue;
                    if (l.OwnerId == k.StringId) n++;
                }
            }
            catch { }
            return n;
        }

        private static int CountVassals(Kingdom k)
        {
            int n = 0;
            try
            {
                if (k == null) return 0;
                int today = Today();
                for (int i = 0; i < DiploPlays.Vassals.Count; i++)
                {
                    var v = DiploPlays.Vassals[i];
                    if (v == null) continue;
                    if (v.OverlordId == k.StringId && today < v.UntilDay) n++;
                }
            }
            catch { }
            return n;
        }

        private static float ScoreOf(Goal g, float sSurv, float sEco, float sMil, float sPol, float sCons)
        {
            switch (g)
            {
                case Goal.Survival: return sSurv;
                case Goal.Military: return sMil;
                case Goal.Politics: return sPol;
                case Goal.Consolidation: return sCons;
                default: return sEco;
            }
        }

        private static float PctOf(Goal g, string domain)
        {
            switch (domain)
            {
                case "bld":
                    if (g == Goal.Survival) return 0.10f;
                    if (g == Goal.Economy) return 0.45f;
                    if (g == Goal.Military) return 0.15f;
                    if (g == Goal.Politics) return 0.20f;
                    return 0.35f;
                case "army":
                    if (g == Goal.Survival) return 0.70f;
                    if (g == Goal.Economy) return 0.15f;
                    if (g == Goal.Military) return 0.55f;
                    if (g == Goal.Politics) return 0.20f;
                    return 0.25f;
                case "rail":
                    if (g == Goal.Survival) return 0.05f;
                    if (g == Goal.Economy) return 0.25f;
                    if (g == Goal.Military) return 0.15f;
                    if (g == Goal.Politics) return 0.10f;
                    return 0.10f;
                case "pol":
                    if (g == Goal.Survival) return 0.05f;
                    if (g == Goal.Economy) return 0.10f;
                    if (g == Goal.Military) return 0.10f;
                    if (g == Goal.Politics) return 0.40f;
                    return 0.25f;
                case "dip":
                    if (g == Goal.Survival) return 0.10f;
                    if (g == Goal.Economy) return 0.05f;
                    if (g == Goal.Military) return 0.05f;
                    if (g == Goal.Politics) return 0.10f;
                    return 0.05f;
                default:
                    return 0f;
            }
        }
    }
}
