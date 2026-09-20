using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.Core;

namespace FeudalInternalAffairs
{
    // AI 宣战/和谈系统(用户需求): 所有国家的战争外交都走这一套, 不走原版决议。
    //   宣战: AI 之间与 AI 对玩家由本系统判定; 玩家的战争由玩家在外交页决定。
    //   和谈: 只有满足我们的多数条件(3 选 2)才可能停战; 涉及玩家王国的和谈走玩家表决流程。
    internal static class AiDiplomacy
    {
        // 本系统执行动作时置 true(放行 NoAiControlPatches 的拦截)
        internal static bool AiActing;

        private static readonly Dictionary<string, int> _warStart = new Dictionary<string, int>();
        private static readonly Dictionary<string, int> _peaceDay = new Dictionary<string, int>();
        private static int _lastWarDay = -9999;

        internal static int Today()
        {
            try { return (int)CampaignTime.Now.ToDays; } catch { return 0; }
        }

        internal static void NoteWar(Kingdom a, Kingdom b)
        {
            try
            {
                var k = Diplomacy.Key(a, b);
                if (k != null) _warStart[k] = Today();
            }
            catch { }
        }

        internal static void NotePeace(Kingdom a, Kingdom b)
        {
            try
            {
                var k = Diplomacy.Key(a, b);
                if (k == null) return;
                _peaceDay[k] = Today();
                _warStart.Remove(k);
            }
            catch { }
        }

        internal static int WarDays(Kingdom a, Kingdom b)
        {
            try
            {
                int d;
                var k = Diplomacy.Key(a, b);
                if (k != null && _warStart.TryGetValue(k, out d)) return Math.Max(0, Today() - d);
                var st = a.GetStanceWith(b);
                if (st != null && st.IsAtWar && st.WarStartDate != CampaignTime.Never)
                    return (int)(CampaignTime.Now - st.WarStartDate).ToDays;
            }
            catch { }
            return 0;
        }

        private static int PeaceAge(Kingdom a, Kingdom b)
        {
            try
            {
                int d;
                var k = Diplomacy.Key(a, b);
                if (k != null && _peaceDay.TryGetValue(k, out d)) return Math.Max(0, Today() - d);
            }
            catch { }
            return 9999;
        }

        private static int WarsOf(Kingdom k)
        {
            try
            {
                int n = 0;
                var war = k.FactionsAtWarWith;
                if (war == null) return 0;
                for (int i = 0; i < war.Count; i++)
                {
                    var kk = war[i] as Kingdom;
                    if (kk != null && !kk.IsEliminated) n++;
                }
                return n;
            }
            catch { return 0; }
        }

        // 领袖野心(好战程度): 勇气/算计 +, 仁慈 -; 0.6~1.6 倍率
        private static float Aggression(Kingdom a)
        {
            try
            {
                var l = a.Leader;
                if (l == null) return 1f;
                int valor = 0, calc = 0, mercy = 0;
                try { valor = l.GetTraitLevel(TaleWorlds.CampaignSystem.CharacterDevelopment.DefaultTraits.Valor); } catch { }
                try { calc = l.GetTraitLevel(TaleWorlds.CampaignSystem.CharacterDevelopment.DefaultTraits.Calculating); } catch { }
                try { mercy = l.GetTraitLevel(TaleWorlds.CampaignSystem.CharacterDevelopment.DefaultTraits.Mercy); } catch { }
                float g = 1f + valor * 0.15f + calc * 0.1f - mercy * 0.1f;
                if (g < 0.6f) g = 0.6f;
                if (g > 1.6f) g = 1.6f;
                return g;
            }
            catch { return 1f; }
        }

        // 边境摩擦(用户: 解决实力接近不打仗): 接壤且非盟友的国家关系保底 -25(冷淡/敌对)
        private static void BorderFriction()
        {
            try
            {
                var all = new List<Kingdom>();
                foreach (var k in Kingdom.All) if (k != null && !k.IsEliminated) all.Add(k);
                for (int i = 0; i < all.Count; i++)
                {
                    for (int j = i + 1; j < all.Count; j++)
                    {
                        var a = all[i];
                        var b = all[j];
                        bool atWar = false;
                        try { atWar = a.IsAtWarWith(b); } catch { }
                        if (atWar || Diplomacy.IsAlly(a, b)) continue;
                        if (!Borders(a, b)) continue;
                        if (Diplomacy.Get(a, b) > -25) Diplomacy.Set(a, b, -25);
                    }
                }
            }
            catch { }
        }

        private static float StrengthOf(Kingdom k)
        {
            try { return Math.Max(1f, k.CurrentTotalStrength); } catch { return 1f; }
        }

        // 接壤判定(用户要求): 两国王国最近两个定居点距离 <= 阈值 视为接壤
        private const float BorderDistance = 70f;

        internal static bool Borders(Kingdom a, Kingdom b)
        {
            try
            {
                var sa = a.Settlements;
                var sb = b.Settlements;
                if (sa == null || sb == null || sa.Count == 0 || sb.Count == 0) return false;
                for (int i = 0; i < sa.Count; i++)
                {
                    var s1 = sa[i];
                    if (s1 == null) continue;
                    for (int j = 0; j < sb.Count; j++)
                    {
                        var s2 = sb[j];
                        if (s2 == null) continue;
                        if (s1.Position.Distance(s2.Position) <= BorderDistance) return true;
                    }
                }
            }
            catch { }
            return false;
        }

        // ================= 月度: AI 和谈 + AI 宣战 =================
        internal static void Month(int day)
        {
            try
            {
                var pk = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                var all = new List<Kingdom>();
                foreach (var k in Kingdom.All) if (k != null && !k.IsEliminated) all.Add(k);

                BorderFriction();   // 边境摩擦: 接壤国家关系保底 -25, 给宣战提供理由

                // ---- 1) AI-AI 和谈: 按"联盟阵营"整体停战 ----
                var done = new HashSet<string>();
                for (int i = 0; i < all.Count; i++)
                {
                    for (int j = i + 1; j < all.Count; j++)
                    {
                        var a = all[i];
                        var b = all[j];
                        if (a == pk || b == pk) continue;
                        bool atWar = false;
                        try { atWar = a.IsAtWarWith(b); } catch { }
                        if (!atWar) continue;
                        var pairKey = Diplomacy.Key(a, b);
                        if (pairKey == null || done.Contains(pairKey)) continue;

                        int wd = WarDays(a, b);
                        if (wd < 28) continue;                       // 条件 1: 战争至少 28 天

                        // 阵营(传递闭包): 同盟一起谈, 一起停
                        var sideA = SideOf(a);
                        var sideB = SideOf(b);
                        if (ContainsPlayer(sideA, pk) || ContainsPlayer(sideB, pk)) continue;   // 涉及玩家的由玩家主导

                        if (!PeaceConditionsMetSides(sideA, sideB, wd)) continue;   // 条件 2: 多数条件成立
                        if (MBRandom.RandomFloat > 0.45f) continue;
                        MakeSidePeace(sideA, sideB, wd);
                        foreach (var x in sideA) foreach (var y in sideB) { var k2 = Diplomacy.Key(x, y); if (k2 != null) done.Add(k2); }
                    }
                }

                // ---- 2) AI 宣战(全局每 21 天最多一场; 实力接近也能打, 看关系/野心) ----
                if (day - _lastWarDay < 21) return;
                Kingdom bestA = null, bestB = null;
                float bestChance = 0f;
                int bestRel = 1;
                for (int i = 0; i < all.Count; i++)
                {
                    var a = all[i];
                    if (a == pk) continue;                            // 玩家王国的战争由玩家决定
                    if (WarEconomy.IsCrisis(a)) continue;             // v4.72: 经济危机国不发动新战争
                    if (WarsOf(a) >= 2) continue;                     // 已经在两线作战, 不再开战
                    for (int j = 0; j < all.Count; j++)
                    {
                        var b = all[j];
                        if (b == a) continue;
                        if (WarEconomy.IsCrisis(b)) continue;         // v4.72: 不打经济崩溃的国家(除非已在战)
                        bool peace = true;
                        try { peace = !a.IsAtWarWith(b); } catch { }
                        if (!peace) continue;
                        if (Diplomacy.IsAlly(a, b)) continue;
                        if (PeaceAge(a, b) < 56) continue;            // 停战不足 56 天
                        if (!Borders(a, b)) continue;                 // 用户要求: 不接壤不宣战
                        int rel = Diplomacy.Get(a, b);
                        // v4.111: 军力碾压(≥2倍)+高鹰派(≥75) -> 允许对关系>0的弱邻"背刺"
                        bool crush = AiPersonality.AggressionOf(a) >= 75 && StrengthOf(a) >= StrengthOf(b) * 2f;
                        if (rel > 0 && !crush) continue;              // 默认只打关系 <=0 的

                        // 宣战欲望: 优势型/均势型/劣势型 + 领袖野心 + 关系恶化
                        float sa = StrengthOf(a), sb = StrengthOf(b);
                        float ratio = sa / Math.Max(1f, sb);
                        float chance = ratio >= 1.25f ? 0.50f : (ratio >= 0.75f ? 0.28f : 0.08f);
                        chance *= Aggression(a);
                        // v4.106/4.107: 国家性格 —— 双方鹰派越高越易开战, 重商国家更不愿开战
                        int agA = AiPersonality.AggressionOf(a), agB = AiPersonality.AggressionOf(b);
                        chance *= (0.5f + (agA + agB) / 200f);
                        chance *= (1.1f - AiPersonality.CommerceOf(a) / 250f);
                        if (rel <= -40) chance *= 1.3f;
                        if (rel > 0) chance *= 0.55f;   // v4.111: 背刺概率低
                        // v4.117: 若目标是玩家且玩家明显更强, AI 敌意更高
                        if (pk != null && ReferenceEquals(b, pk) && StrengthOf(b) > StrengthOf(a) * 1.3f)
                            chance *= 1.25f;
                        if (chance > bestChance || (Math.Abs(chance - bestChance) < 0.001f && rel < bestRel))
                        {
                            bestChance = chance; bestRel = rel; bestA = a; bestB = b;
                        }
                    }
                }
                if (bestA != null && MBRandom.RandomFloat < bestChance)
                {
                    // 第 22 章: 先开外交博弈, 谈崩了才打; 无法开博弈时兜底直接宣战
                    if (DiploPlays.StartPlay(bestA, bestB, false) == null)
                        MakeAiWar(bestA, bestB);
                }
            }
            catch (Exception ex) { DLog.Force("AI 外交月结异常: " + ex.Message); }
        }

        // 阵营(传递闭包; 与 Diplomacy.SideOf 单跳不同, 这里把盟友的盟友也并进来)
        internal static List<Kingdom> SideOf(Kingdom k)
        {
            var side = new List<Kingdom>();
            try
            {
                if (k == null) return side;
                var seen = new HashSet<string>();
                var queue = new Queue<Kingdom>();
                queue.Enqueue(k); seen.Add(k.StringId); side.Add(k);
                int guard = 0;
                while (queue.Count > 0 && guard++ < 64)
                {
                    var cur = queue.Dequeue();
                    foreach (var x in Kingdom.All)
                    {
                        if (x == null || x.IsEliminated || seen.Contains(x.StringId)) continue;
                        if (!Diplomacy.IsAlly(cur, x)) continue;
                        seen.Add(x.StringId); side.Add(x); queue.Enqueue(x);
                    }
                }
            }
            catch { }
            return side;
        }

        private static bool ContainsPlayer(List<Kingdom> side, Kingdom pk)
        {
            if (pk == null) return false;
            for (int i = 0; i < side.Count; i++) if (ReferenceEquals(side[i], pk)) return true;
            return false;
        }

        // 阵营的敌人数量(不重复)
        private static int EnemyCount(List<Kingdom> side)
        {
            try
            {
                var set = new HashSet<string>();
                for (int i = 0; i < side.Count; i++)
                {
                    var war = side[i].FactionsAtWarWith;
                    if (war == null) continue;
                    for (int j = 0; j < war.Count; j++)
                    {
                        var f = war[j] as Kingdom;
                        if (f == null || f.IsEliminated) continue;
                        set.Add(f.StringId);
                    }
                }
                return set.Count;
            }
            catch { return 0; }
        }

        private static float SideStrength(List<Kingdom> side)
        {
            float s = 0f;
            for (int i = 0; i < side.Count; i++) s += StrengthOf(side[i]);
            return Math.Max(1f, s);
        }

        // 和谈多数条件(5 选 2, 按阵营整体评估): 军力差距>=1.6 / 任一阵营敌人>=3 / 打满 84 天 / 任一国有经济危机 / 任一国有厌战>=60 (v4.72/4.73)
        private static bool PeaceConditionsMetSides(List<Kingdom> sideA, List<Kingdom> sideB, int warDays)
        {
            try
            {
                float sa = SideStrength(sideA), sb = SideStrength(sideB);
                float ratio = sa > sb ? sa / sb : sb / sa;
                int ea = EnemyCount(sideA), eb = EnemyCount(sideB);
                int conds = 0;
                if (ratio >= 1.6f) conds++;
                if (ea >= 3 || eb >= 3) conds++;
                if (warDays >= 84) conds++;
                if (AnyCrisis(sideA) || AnyCrisis(sideB)) conds++;   // v4.72: 经济崩了就想谈
                if (MaxWearSide(sideA) >= 60f || MaxWearSide(sideB) >= 60f) conds++;   // v4.73: 厌战了就想谈
                // v4.108: 性格影响和谈门槛 —— 鹰派阵营更顽固(需 3 条), 重商阵营更务实(1 条即谈)
                int need = 2;
                try
                {
                    int aggSum = 0, comSum = 0, cnt = 0;
                    for (int i = 0; i < sideA.Count; i++) { if (sideA[i] == null) continue; aggSum += AiPersonality.AggressionOf(sideA[i]); comSum += AiPersonality.CommerceOf(sideA[i]); cnt++; }
                    for (int i = 0; i < sideB.Count; i++) { if (sideB[i] == null) continue; aggSum += AiPersonality.AggressionOf(sideB[i]); comSum += AiPersonality.CommerceOf(sideB[i]); cnt++; }
                    if (cnt > 0)
                    {
                        int avgAgg = aggSum / cnt, avgCom = comSum / cnt;
                        if (avgAgg >= 70) need = 3;
                        else if (avgCom >= 70) need = 1;
                    }
                }
                catch { }
                return conds >= need;
            }
            catch { return false; }
        }

        private static float MaxWearSide(List<Kingdom> side)
        {
            float m = 0f;
            try
            {
                for (int i = 0; i < side.Count; i++)
                {
                    float w = WarWeariness.MaxWearOf(side[i]);
                    if (w > m) m = w;
                }
            }
            catch { }
            return m;
        }

        private static bool AnyCrisis(List<Kingdom> side)
        {
            try
            {
                for (int i = 0; i < side.Count; i++)
                    if (side[i] != null && WarEconomy.IsCrisis(side[i])) return true;
            }
            catch { }
            return false;
        }

        // 整条阵营一起停战(所有交战对)
        internal static void MakeSidePeace(List<Kingdom> sideA, List<Kingdom> sideB, int warDays)
        {
            try
            {
                int pairs = 0;
                AiActing = true;
                try
                {
                    for (int i = 0; i < sideA.Count; i++)
                    {
                        for (int j = 0; j < sideB.Count; j++)
                        {
                            var x = sideA[i];
                            var y = sideB[j];
                            if (x == null || y == null || x == y) continue;
                            bool atWar = false;
                            try { atWar = x.IsAtWarWith(y); } catch { }
                            if (!atWar) continue;
                            try
                            {
                                MakePeaceAction.Apply(x, y);
                                NotePeace(x, y);
                                pairs++;
                            }
                            catch (Exception ex) { DLog.Info("阵营停战失败 " + x.StringId + "/" + y.StringId + ": " + ex.Message); }
                        }
                    }
                }
                finally { AiActing = false; }
                string na = sideA.Count > 0 ? sideA[0].Name.ToString() : "?";
                string nb = sideB.Count > 0 ? sideB[0].Name.ToString() : "?";
                DLog.Force("AI 阵营和谈: [" + na + " 阵营 " + sideA.Count + "国] <-> [" + nb + " 阵营 " + sideB.Count + "国] 停战 " + pairs + " 对 (战争 " + warDays + " 天)");
            }
            catch (Exception ex) { DLog.Force("阵营和谈失败: " + ex.Message); }
        }

        internal static void MakeAiPeace(Kingdom a, Kingdom b, int warDays)
        {
            try
            {
                AiActing = true;
                try { if (a.IsAtWarWith(b)) MakePeaceAction.Apply(a, b); }
                finally { AiActing = false; }
                NotePeace(a, b);
                DLog.Force("AI 和谈: " + a.Name + " <-> " + b.Name + " (战争 " + warDays + " 天, 多数条件达成)");
            }
            catch (Exception ex) { DLog.Force("AI 和谈失败: " + ex.Message); }
        }

        internal static void MakeAiWar(Kingdom a, Kingdom b)
        {
            try
            {
                AiActing = true;
                try { DeclareWarAction.ApplyByDefault(a, b); }
                finally { AiActing = false; }
                _lastWarDay = Today();
                NoteWar(a, b);
                var pk = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                if (a == pk || b == pk)
                    MapSelection.Message("宣战: " + a.Name + " 进攻 " + b.Name + "(AI 判定)");
                DLog.Force("AI 宣战: " + a.Name + " -> " + b.Name);
            }
            catch (Exception ex) { DLog.Force("AI 宣战失败: " + ex.Message); }
        }

        // ================= 清除原版战争/和平决议(已停用!) =================
        // 原因: 从原版 KingdomDecisionManager 里 RemoveDecision 会让决策系统状态不一致,
        //       原版随后处理"被取消的决策"时原生崩溃(0xC0000005, 日志 "has been cancelled" 后崩)。
        //       战争/和平实际由动作层 BlockDeclareWar / BlockMakePeace 拦截, 不需要动决策系统。
        internal static void CleanNativeWarPeaceDecisions()
        {
            return;
#pragma warning disable CS0162
            try
            {
                foreach (var k in Kingdom.All)
                {
                    if (k == null || k.IsEliminated) continue;
                    var list = k.UnresolvedDecisions;
                    if (list == null || list.Count == 0) continue;
                    var arr = new List<TaleWorlds.CampaignSystem.Election.KingdomDecision>(list);
                    for (int i = 0; i < arr.Count; i++)
                    {
                        var d = arr[i];
                        if (d is TaleWorlds.CampaignSystem.Election.DeclareWarDecision
                            || d is TaleWorlds.CampaignSystem.Election.MakePeaceKingdomDecision)
                        {
                            try
                            {
                                k.RemoveDecision(d);
                                DLog.Force("清除原版决议: " + k.Name + " " + d.GetType().Name);
                            }
                            catch { }
                        }
                    }
                }
            }
            catch { }
#pragma warning restore CS0162
        }

        // ================= 存档 FIA_WarDairy =================
        internal static string Save()
        {
            try
            {
                var sb = new StringBuilder("v1;");
                foreach (var kv in _warStart) sb.Append("W").Append(kv.Key).Append(',').Append(kv.Value).Append(';');
                foreach (var kv in _peaceDay) sb.Append("P").Append(kv.Key).Append(',').Append(kv.Value).Append(';');
                sb.Append("L").Append(_lastWarDay).Append(';');
                return sb.ToString();
            }
            catch { return "v1;"; }
        }

        internal static void Load(string data)
        {
            try
            {
                _warStart.Clear();
                _peaceDay.Clear();
                if (string.IsNullOrEmpty(data)) return;
                var parts = data.Split(';');
                for (int i = 1; i < parts.Length; i++)
                {
                    var s = parts[i];
                    if (string.IsNullOrEmpty(s)) continue;
                    char tag = s[0];
                    var body = s.Substring(1);
                    if (tag == 'L')
                    {
                        int lv;
                        if (int.TryParse(body, out lv)) _lastWarDay = lv;
                        continue;
                    }
                    int idx = body.LastIndexOf(',');
                    if (idx <= 0) continue;
                    string key = body.Substring(0, idx);
                    int day;
                    if (!int.TryParse(body.Substring(idx + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out day)) continue;
                    if (tag == 'W') _warStart[key] = day;
                    else if (tag == 'P') _peaceDay[key] = day;
                }
                DLog.Force("AI 外交: 读档 战争记录=" + _warStart.Count + " 停战记录=" + _peaceDay.Count);
            }
            catch { }
        }
    }
}
