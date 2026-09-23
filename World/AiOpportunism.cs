using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace FeudalInternalAffairs
{
    // v4.149: AI 机会主义与战略协调
    //   ①针对性反制玩家: 玩家内战/合法性崩坏/破产/多线/危机/被制裁 -> 趁火打劫(索贡博弈/煽动激进/宣战窗口)
    //   ②报复制裁: 被玩家制裁 -> 联合其他被制裁国与友邦一起制裁玩家
    //   ③反霸权联合围堵: 霸权国(军力>第二×1.6) -> 多国联合制裁 + 协调博弈 + 抱团互不侵犯
    //   ④危机外交: 破产/兵源枯竭/多线作战/厌战 -> 主动求和止损
    internal static class AiOpportunism
    {
        // 宣战窗口: 玩家虚弱时 AI 对玩家宣战欲望加成(供 AiDiplomacy 读取)
        private static float _playerWarWindow;      // 0~1
        private static int _playerWarWindowUntil = -9999;

        internal static float PlayerWarWindowBonus()
        {
            try { return AiDiplomacy.Today() <= _playerWarWindowUntil ? _playerWarWindow : 0f; }
            catch { return 0f; }
        }

        internal static void Monthly(int day)
        {
            try
            {
                var pk = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                if (pk == null) return;

                float weakness = PlayerWeakness(pk);
                _playerWarWindow = weakness;
                _playerWarWindowUntil = day + 28;

                foreach (var k in Kingdom.All)
                {
                    if (k == null || k.IsEliminated || ReferenceEquals(k, pk)) continue;
                    try { ExploitPlayer(k, pk, weakness, day); } catch { }
                    try { RetaliateSanctions(k, pk, day); } catch { }
                    try { EmergencyPeace(k, day); } catch { }
                }
                try { ContainHegemon(day); } catch { }
            }
            catch (Exception ex) { DLog.Force("AI 机会主义异常: " + ex.Message); }
        }

        // ================= 玩家弱点评估(0~1) =================
        //   合法性低 + 内战 + 破产 + 多线作战 + 经济危机 + 被制裁
        internal static float PlayerWeakness(Kingdom pk)
        {
            float w = 0f;
            try
            {
                float legit = Politics.Legitimacy;
                if (legit < 50f) w += (50f - legit) / 50f * 0.35f;         // 合法性崩坏
                bool civil = false;
                try { civil = Politics.CivilWar; } catch { }
                if (civil) w += 0.30f;                                     // 内战
                try
                {
                    if (EconomyWorld.Treasury.NegativeDays > 7) w += 0.15f; // 破产边缘
                    if (WarEconomy.IsCrisis(pk)) w += 0.15f;               // 经济危机(饥荒/兵源)
                }
                catch { }
                int wars = 0;
                try
                {
                    foreach (var x in Kingdom.All)
                    {
                        if (x == null || x.IsEliminated || ReferenceEquals(x, pk)) continue;
                        if (pk.IsAtWarWith(x)) wars++;
                    }
                }
                catch { }
                if (wars >= 2) w += 0.10f;                                 // 多线作战
                if (wars >= 3) w += 0.10f;
                try { if (WarEconomy.IsSanctioned(pk)) w += 0.05f; } catch { }
                if (w > 1f) w = 1f;
            }
            catch { }
            return w;
        }

        // ================= 机会主义行动 =================
        private static void ExploitPlayer(Kingdom k, Kingdom pk, float weakness, int day)
        {
            if (weakness < 0.35f) return;                                  // 玩家还稳, 不冒险
            bool atWar = false;
            try { atWar = k.IsAtWarWith(pk); } catch { }
            if (atWar) return;                                             // 已开战: 走正常战争计划

            int agg = AiPersonality.AggressionOf(k);
            int com = AiPersonality.CommerceOf(k);
            float aggression = agg / 100f;

            // ① 索贡博弈: 鹰派 + 玩家虚弱 -> 趁机要钱(重商国更爱)
            if (weakness >= 0.5f && (aggression >= 0.55f || com >= 0.6f))
            {
                if (MBRandom.RandomFloat < weakness * aggression * 0.35f)
                {
                    try
                    {
                        var play = DiploPlays.StartPlay(k, pk, false);
                        if (play != null)
                        {
                            DLog.Force("AI 机会主义: " + k.Name + " 趁玩家虚弱发起索贡博弈(弱点 " + weakness.ToString("F2") + ")");
                            MapSelection.Message(k.Name + " 趁我国势弱提出索贡要求(外交博弈)");
                        }
                    }
                    catch { }
                }
            }

            // ② 煽动: 向玩家人口输出"外国煽动"(激进 +, 有真实效果)
            if (weakness >= 0.55f && aggression >= 0.5f)
            {
                try
                {
                    float rad = 0.02f * weakness * aggression;
                    Pops.ShiftRadicals(rad);
                    if (day % 28 == 0)
                        DLog.Force("AI 机会主义: " + k.Name + " 向我国输出煽动(激进 +" + rad.ToString("F3") + ")");
                }
                catch { }
            }
        }

        // ================= 报复制裁 =================
        private static void RetaliateSanctions(Kingdom k, Kingdom pk, int day)
        {
            bool sanctionedByPlayer = false;
            try { sanctionedByPlayer = WarEconomy.SanctionedBy(k, pk); } catch { }
            if (!sanctionedByPlayer) return;

            // 联合其他被玩家制裁的国家一起反制
            try
            {
                int allies = 0;
                foreach (var x in Kingdom.All)
                {
                    if (x == null || x.IsEliminated || ReferenceEquals(x, k) || ReferenceEquals(x, pk)) continue;
                    bool also = false;
                    try { also = WarEconomy.SanctionedBy(x, pk); } catch { }
                    int rel = 0;
                    try { rel = Diplomacy.Get(k, x); } catch { }
                    if (also || rel >= 20) { allies++; }
                }
                bool joint = allies >= 2 && MBRandom.RandomFloat < 0.5f;
                if (joint)
                {
                    // 联合反制: 拉更多国家一起制裁玩家
                    int cnt = 0;
                    foreach (var x in Kingdom.All)
                    {
                        if (x == null || x.IsEliminated || ReferenceEquals(x, k) || ReferenceEquals(x, pk)) continue;
                        int rel = 0;
                        try { rel = Diplomacy.Get(k, x); } catch { }
                        if (rel < 20) continue;
                        bool already = false;
                        try { already = WarEconomy.SanctionedBy(x, pk); } catch { }
                        if (already) continue;
                        try { WarEconomy.SetSanction(pk, x, true); cnt++; } catch { }
                        if (cnt >= 2) break;
                    }
                    if (cnt > 0)
                    {
                        DLog.Force("AI 报复: " + k.Name + " 联合 " + cnt + " 国制裁我国");
                        MapSelection.Message(k.Name + " 联合多国对我国实施贸易制裁");
                    }
                }
            }
            catch { }
        }

        // ================= 反霸权联合围堵 =================
        private static void ContainHegemon(int day)
        {
            try
            {
                var pk = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                // 军力排行
                Kingdom first = null, second = null;
                float f1 = 0f, f2 = 0f;
                foreach (var k in Kingdom.All)
                {
                    if (k == null || k.IsEliminated) continue;
                    float s = WarPlans.StrengthOf(k);
                    if (s > f1) { second = first; f2 = f1; first = k; f1 = s; }
                    else if (s > f2) { second = k; f2 = s; }
                }
                if (first == null || second == null) return;
                if (f1 < f2 * 1.6f) return;                                // 未形成霸权

                // 围堵: 非霸权国联合(含玩家在内也被 AI 视为潜在围堵者/被围堵者)
                int sanctioned = 0, plays = 0;
                foreach (var k in Kingdom.All)
                {
                    if (k == null || k.IsEliminated || ReferenceEquals(k, first)) continue;
                    bool sanctionedAlready = false;
                    try { sanctionedAlready = WarEconomy.SanctionedBy(first, k); } catch { }
                    if (sanctionedAlready) continue;
                    bool allyOfHegemon = false;
                    try { allyOfHegemon = Diplomacy.IsAlly(k, first); } catch { }
                    if (allyOfHegemon) continue;                            // 霸权国的盟友不参与围堵
                    int agg = AiPersonality.AggressionOf(k);
                    // 联合制裁(每 28 天最多 1 国加入, 形成滚雪球)
                    if (sanctioned < 1 && MBRandom.RandomFloat < 0.35f + agg / 300f)
                    {
                        try
                        {
                            WarEconomy.SetSanction(first, k, true);
                            sanctioned++;
                            DLog.Force("反霸权围堵: " + k.Name + " 制裁霸权国 " + first.Name);
                        }
                        catch { }
                    }
                    // 协调博弈: 关系好的一起向霸权国施压
                    if (plays < 1 && pk != null && ReferenceEquals(k, pk)) continue;   // 玩家自主
                    if (plays < 1 && MBRandom.RandomFloat < 0.15f + agg / 400f)
                    {
                        try
                        {
                            var play = DiploPlays.StartPlay(k, first, false);
                            if (play != null) { plays++; DLog.Force("反霸权围堵: " + k.Name + " 向霸权国 " + first.Name + " 发起博弈"); }
                        }
                        catch { }
                    }
                }
                if ((sanctioned > 0 || plays > 0) && day % 28 == 0)
                    DLog.Force("反霸权: 霸权国 " + first.Name + " 军力 " + (int)f1 + " vs 第二 " + (int)f2
                        + ", 本月围堵(制裁 " + sanctioned + " 博弈 " + plays + ")");
            }
            catch (Exception ex) { DLog.Force("反霸权围堵异常: " + ex.Message); }
        }

        // ================= 危机外交: 主动求和止损 =================
        //   破产/兵源枯竭/多线(>=3)/厌战(>=70) -> 立即向所有敌人求和(不等月度条件)
        internal static void EmergencyPeace(Kingdom k, int day)
        {
            try
            {
                if (k == null || k.IsEliminated) return;
                var pk = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                if (ReferenceEquals(k, pk)) return;                        // 玩家自己决定

                bool bankrupt = false, crisis = false;
                float wear = 0f;
                int wars = 0;
                try
                {
                    int def; bankrupt = WarEconomy.DeficitDaysOf(k) >= 14;
                    crisis = WarEconomy.IsCrisis(k);
                    wear = WarWeariness.MaxWearOf(k);
                    foreach (var x in Kingdom.All)
                    {
                        if (x == null || x.IsEliminated || ReferenceEquals(x, k)) continue;
                        if (k.IsAtWarWith(x)) wars++;
                    }
                }
                catch { }
                bool emergency = bankrupt || (crisis && wars >= 1) || wars >= 3 || wear >= 70f;
                if (!emergency) return;

                // 向每个敌人求和(优先和强者谈, 减少战线)
                foreach (var e in Kingdom.All)
                {
                    if (e == null || e.IsEliminated || ReferenceEquals(e, k)) continue;
                    bool war = false;
                    try { war = k.IsAtWarWith(e); } catch { }
                    if (!war) continue;
                    if (MBRandom.RandomFloat > 0.5f) continue;             // 不一次全停, 分月消化
                    int wd = AiDiplomacy.WarDays(k, e);
                    if (wd < 14) continue;                                 // 刚开战不谈
                    AiDiplomacy.MakeAiPeace(k, e, wd);
                    DLog.Force("危机外交: " + k.Name + " 因" + (bankrupt ? "破产" : (wars >= 3 ? "多线" : (wear >= 70f ? "厌战" : "危机")))
                        + "主动向 " + e.Name + " 求和(战争 " + wd + " 天)");
                }
            }
            catch { }
        }
    }
}
