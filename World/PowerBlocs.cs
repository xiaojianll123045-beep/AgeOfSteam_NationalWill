using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace FeudalInternalAffairs
{
    // v5.x: AI 权力集团(AI 侧独立数据, 不占用玩家的单集团流程/UI)
    internal class AiBloc
    {
        internal string LeaderId;
        internal string Name;
        internal int Identity;
        internal readonly List<string> Members = new List<string>();
        internal float Cohesion = 30f;
        internal float MandateProgress;
        internal int Mandates;
        internal readonly int[][] Principle = { new int[2], new int[2], new int[2] };   // [身份][原则]
    }

    // 权力集团(文档 24.8; v4.138 照 V3 官方 Power_bloc 重做, 骑砍化):
    //   创建花 5000 第纳尔(v4.139: 国家意志无影响力概念, 用户要求走国库); 3 种身份(骑砍化自官方六身份);
    //   凝聚力 0-100(V3 Cohesion: 基础 30 - 成员×3 + 领袖实力/关系修正), 每周向目标 ±1;
    //   授权进度(V3 Mandate: 2000 进度 = 1 授权, 按成员数与集团排名 +), 授权消费于"原则"(3 档);
    //   杠杆邀请(V3 Leverage: 需比对方自主杠杆多 200): 我们简化为 关系 + 实力比 判定
    internal class BlocMember
    {
        internal string KingdomId;
        internal string Name;
        internal int JoinDay;
    }

    internal static class PowerBlocs
    {
        // 身份(骑砍化自官方: 贸易联盟/军事条约/主权帝国)
        internal const int IdTrade = 0, IdMilitary = 1, IdEmpire = 2;
        internal static readonly string[] IdentityNames = { "贸易联盟", "军事同盟", "主权帝国" };
        internal static readonly string[] IdentityDesc =
        {
            "贸易联盟: 领袖贸易容量 +25%, 成员关税 -50%(Trade League)",
            "军事同盟: 全体训练速度 +20%, 领袖威望 +10%(Military Treaty)",
            "主权帝国: 成员每月上贡, 附庸自由渴望 -5%(Sovereign Empire)"
        };
        // 原则(每身份 2 个, 3 档; 简化自官方列表)
        internal static readonly string[][] PrincipleNames =
        {
            new[] { "对外贸易", "内部贸易" },
            new[] { "防御协作", "进攻协作" },
            new[] { "附庸化", "榨取成员" }
        };
        internal static readonly string[][][] PrincipleEffects =
        {
            new[]
            {
                new[] { "贸易容量 +1/+2/+3", "贸易优势/关税优惠递进", "无贸易利益惩罚" },
                new[] { "成员关税再 -10%/档", "成员商队损耗 -", "港口/商路保护" }
            },
            new[]
            {
                new[] { "不得互相开战", "拉拢成员成本 -50%", "可召唤成员参战" },
                new[] { "进攻战争目标成本 -30%", "战争疲惫 -15%", "威望 +15%(凝聚力 -10)" }
            },
            new[]
            {
                new[] { "每附庸 +10 权威", "+15 权威, 附庸贡金 +25%", "+20 权威, 可直接在其领土征兵" },
                new[] { "凝聚力 -5, 领袖权威 +20%", "-10, 领袖权威 +20%", "-20, 成员税收 5% 上贡" }
            }
        };

        internal static bool Founded;
        internal static int Identity = IdTrade;
        internal static string LeaderId;
        internal static string Name = "";
        internal static readonly List<BlocMember> Members = new List<BlocMember>();
        internal static float Cohesion = 30f;
        internal static float MandateProgress;      // 0-2000(V3 官方)
        internal static int Mandates;               // 可用授权(0-3)
        internal static readonly int[][] PrincipleTier = { new int[2], new int[2], new int[2] };   // [身份][原则]
        private static int _lastWeekDay = -9999;

        internal const int CreateCost = 5000;       // v4.139: 用户要求移除影响力概念 -> 只花国库(5000 第纳尔)
        internal const float MandateNeed = 2000f;   // V3: 2000 进度 = 1 授权
        internal const int MaxMandates = 3;         // V3: 最多存 3

        // ================= 创建 =================
        internal static string Create(int identity)
        {
            try
            {
                if (Founded) return "已存在权力集团: " + Name;
                if (identity < 0 || identity > 2) return "无效身份";
                var k = MyKingdom();
                if (k == null) return "没有国家";
                if (Treasury() < CreateCost) return "国库不足(需 " + CreateCost.ToString("N0") + " 第纳尔)";
                TreasurySpend(CreateCost);
                Founded = true;
                Identity = identity;
                LeaderId = k.StringId;
                Name = k.Name != null ? (k.Name.ToString() + " 的" + IdentityNames[identity]) : IdentityNames[identity];
                Members.Clear();
                Members.Add(new BlocMember { KingdomId = k.StringId, Name = k.Name != null ? k.Name.ToString() : k.StringId, JoinDay = Politics.Today() });
                Cohesion = 30f;
                MandateProgress = 0f;
                DLog.Force("权力集团: 成立 " + Name + "(" + IdentityNames[identity] + ")");
                InterestGroups.NotifyPlayer("🏛 权力集团成立: " + Name + "(国库 -" + CreateCost.ToString("N0") + ")", true);
                return "已创建 " + Name + " · " + IdentityNames[identity];
            }
            catch { return "创建失败"; }
        }

        internal static string SetIdentity(int identity)
        {
            try
            {
                if (!Founded) return "尚未创建权力集团";
                if (identity < 0 || identity > 2) return "";
                if (identity == Identity) return "当前已是 " + IdentityNames[identity];
                // V3: 更换身份需授权
                if (Mandates < 1) return "需要 1 个授权才能更换身份(当前 " + Mandates + ")";
                Mandates--;
                Identity = identity;
                DLog.Force("权力集团: 身份改为 " + IdentityNames[identity]);
                return "集团身份已改为 " + IdentityNames[identity];
            }
            catch { return "更换失败"; }
        }

        // ================= 成员 =================
        internal static bool IsMember(string kingdomId)
        {
            for (int i = 0; i < Members.Count; i++) if (Members[i].KingdomId == kingdomId) return true;
            return false;
        }

        internal static bool IsLeader()
        {
            try { var k = MyKingdom(); return Founded && k != null && k.StringId == LeaderId; } catch { return false; }
        }

        // 邀请(简化自 V3 Leverage: 实力比 + 关系; 花国库)
        internal static string Invite(Kingdom target)
        {
            try
            {
                if (!Founded || !IsLeader()) return "只有集团领袖才能邀请成员";
                if (target == null) return "无效目标";
                if (IsMember(target.StringId)) return target.Name + " 已是成员";
                var k = MyKingdom();
                if (k == null) return "没有国家";
                if (target.IsAtWarWith(k)) return "与 " + target.Name + " 处于战争状态, 无法邀请";
                if (Treasury() < 1000) return "国库不足(需 1,000 第纳尔)";
                // 接受判定: 关系 + 实力比(V3: 需杠杆优势 200)
                int rel = 0;
                try { if (k.Leader != null && target.Leader != null) rel = target.Leader.GetRelation(k.Leader); } catch { }
                float myPow = k.CurrentTotalStrength + k.Fiefs.Count * 50f;
                float theirPow = target.CurrentTotalStrength + target.Fiefs.Count * 50f;
                bool stronger = myPow > theirPow * 1.3f;
                bool accept = rel >= 0 || stronger;
                if (!accept)
                    return target.Name + " 拒绝了邀请(关系 " + rel + ", 实力不足)";
                TreasurySpend(1000);
                Members.Add(new BlocMember { KingdomId = target.StringId, Name = target.Name != null ? target.Name.ToString() : target.StringId, JoinDay = Politics.Today() });
                RecomputeCohesion();
                InterestGroups.NotifyPlayer("🏛 " + target.Name + " 加入了 " + Name, false);
                DLog.Force("权力集团: 邀请 " + target.StringId + " 加入(关系=" + rel + ")");
                return target.Name + " 加入了集团(国库 -1,000)";
            }
            catch { return "邀请失败"; }
        }

        internal static string Kick(int idx)
        {
            try
            {
                if (!Founded || !IsLeader()) return "只有集团领袖才能开除成员";
                if (idx < 0 || idx >= Members.Count) return "";
                var m = Members[idx];
                if (m.KingdomId == LeaderId) return "不能开除自己(领袖)";
                Members.RemoveAt(idx);
                // V3: 踢出 -> 关系 -50 / 12 个月停战 / 凝聚力 -10
                Cohesion = Math.Max(0f, Cohesion - 10f);
                try
                {
                    foreach (var kk in Kingdom.All)
                    {
                        if (kk != null && kk.StringId == m.KingdomId)
                        {
                            var k = MyKingdom();
                            if (k != null && kk.Leader != null && k.Leader != null)
                                kk.Leader.SetPersonalRelation(k.Leader, m.KingdomId == null ? -50 : kk.Leader.GetRelation(k.Leader) - 50);
                            break;
                        }
                    }
                }
                catch { }
                DLog.Force("权力集团: 开除 " + m.Name);
                return "已开除 " + m.Name + "(凝聚力 -10, 关系恶化)";
            }
            catch { return "开除失败"; }
        }

        // ================= 原则与授权 =================
        internal static string UpgradePrinciple(int pi)
        {
            try
            {
                if (!Founded) return "尚未创建权力集团";
                if (pi < 0 || pi > 1) return "";
                int cur = PrincipleTier[Identity][pi];
                if (cur >= 3) return PrincipleNames[Identity][pi] + " 已满级";
                int cost = cur + 1;   // V3: 档位 = 授权花费
                if (Mandates < cost) return "授权不足(需 " + cost + ", 当前 " + Mandates + "); 等待成员与排名累积授权";
                Mandates -= cost;
                PrincipleTier[Identity][pi] = cur + 1;
                DLog.Force("权力集团: 原则 " + PrincipleNames[Identity][pi] + " -> " + (cur + 1));
                return PrincipleNames[Identity][pi] + " 提升到 " + (cur + 1) + " 档";
            }
            catch { return "提升失败"; }
        }

        // 授权进度与凝聚力(每周, V3: 每周最多 ±1 凝聚力)
        internal static void Weekly(int day)
        {
            try
            {
                if (!Founded) return;
                if (day - _lastWeekDay < 7) return;
                _lastWeekDay = day;
                // 授权进度: 非领袖成员按人数(V3: 按成员等级; 我们: 每成员 +4) + 集团排名 +2; × 凝聚力
                int others = Math.Max(0, Members.Count - 1);
                float gain = (2f + others * 4f) * (Cohesion / 100f);
                MandateProgress += gain;
                while (MandateProgress >= MandateNeed)
                {
                    MandateProgress -= MandateNeed;
                    if (Mandates < MaxMandates) Mandates++;
                    InterestGroups.NotifyPlayer("🏛 集团获得 1 个授权(可用授权 " + Mandates + "/" + MaxMandates + ")", false);
                }
                // 凝聚力向目标靠拢(每周 ±1)
                float target = TargetCohesion();
                if (Cohesion < target) Cohesion = Math.Min(target, Cohesion + 1f);
                else if (Cohesion > target) Cohesion = Math.Max(target, Cohesion - 1f);
            }
            catch { }
        }

        // V3 官方 Cohesion 因子(简化): 基础 30 - 独立成员×3 + 领袖实力份额 + 最差关系
        internal static float TargetCohesion()
        {
            try
            {
                float v = 30f;
                int others = Math.Max(0, Members.Count - 1);
                v -= others * 3f;
                try
                {
                    float my = 0f, all = 0f;
                    var k = MyKingdom();
                    foreach (var kv in Members)
                    {
                        float pow = 0f;
                        foreach (var kk in Kingdom.All)
                            if (kk != null && kk.StringId == kv.KingdomId) { pow = kk.CurrentTotalStrength; break; }
                        all += pow;
                        if (k != null && kv.KingdomId == k.StringId) my = pow;
                    }
                    if (all > 0f) v += (my / all) * 20f;   // 领袖实力份额(V3: GDP/威望份额)
                }
                catch { }
                if (!IsLeader()) v -= 10f;
                return LawSystem.ClampF(v, 0f, 100f);
            }
            catch { return 30f; }
        }

        internal static string CohesionLevel()
        {
            try
            {
                if (Cohesion < 20f) return "分裂";
                if (Cohesion < 40f) return "分离";
                if (Cohesion < 60f) return "稳定";
                if (Cohesion < 80f) return "受控";
                return "协调";
            }
            catch { return "稳定"; }
        }

        internal static float CohesionMult()
        {
            try
            {
                if (Cohesion < 20f) return 0.5f;
                if (Cohesion < 40f) return 0.7f;
                if (Cohesion < 60f) return 0.9f;
                if (Cohesion < 80f) return 1.1f;
                return 1.25f;
            }
            catch { return 1f; }
        }

        internal static void RecomputeCohesion()
        {
            try { Cohesion = TargetCohesion(); } catch { }
        }

        // ================= 效果(接既有系统) =================
        internal static int TradeCapacityBonus()
        {
            try
            {
                if (!Founded) return 0;
                int n = 0;
                if (Identity == IdTrade) n += 1;                                          // 官方: 领袖贸易容量 +25%
                n += PrincipleTier[IdTrade][0] + PrincipleTier[IdTrade][1];               // 贸易原则
                return n;
            }
            catch { return 0; }
        }

        internal static float TariffMult()
        {
            try
            {
                if (!Founded) return 1f;
                float m = 1f;
                if (Identity == IdTrade) m -= 0.5f;                                        // 关税同盟: 关税 -50%
                if (PrincipleTier[IdTrade][1] > 0) m -= 0.10f * PrincipleTier[IdTrade][1];
                return LawSystem.ClampF(m, 0.1f, 1f);
            }
            catch { return 1f; }
        }

        internal static float MilitaryMult()
        {
            try
            {
                if (!Founded) return 1f;
                float m = 1f;
                if (Identity == IdMilitary) m += 0.10f;                                    // 训练/军力(V3 +20% 训练率)
                m += 0.05f * PrincipleTier[IdMilitary][0];
                return m;
            }
            catch { return 1f; }
        }

        // 主权帝国: 成员月贡金(V3: 附庸贡金/收入转移)
        internal static int EmpireTribute()
        {
            try
            {
                if (!Founded || Identity != IdEmpire) return 0;
                int n = 0;
                float per = 300f * (1f + 0.25f * PrincipleTier[IdEmpire][0] + 0.05f * PrincipleTier[IdEmpire][1]);
                for (int i = 0; i < Members.Count; i++)
                {
                    if (Members[i].KingdomId == LeaderId) continue;
                    n += (int)per;
                }
                return n;
            }
            catch { return 0; }
        }

        internal static string StatusText()
        {
            try
            {
                if (!Founded) return "权力集团: 未创建(需 5000 第纳尔)";
                return "🏛 " + Name + " · " + CohesionLevel() + "(" + (int)Cohesion + ") · 成员 " + Members.Count
                    + " · 授权 " + Mandates + "(" + (int)MandateProgress + "/2000)";
            }
            catch { return ""; }
        }

        internal static string PrincipleText()
        {
            try
            {
                if (!Founded) return "";
                string s = "原则: ";
                for (int i = 0; i < 2; i++)
                    s += PrincipleNames[Identity][i] + " " + PrincipleTier[Identity][i] + "/3" + (i == 0 ? " · " : "");
                return s;
            }
            catch { return ""; }
        }

        // v4.139: 国家意志无"影响力"概念(用户要求) -> 集团开销一律走国库第纳尔
        internal static int Treasury()
        {
            try { return (int)EconomyWorld.Treasury.Gold; } catch { return 0; }
        }

        internal static void TreasurySpend(int v)
        {
            try { EconomyWorld.TreasurySpend(v); Fiscal.AddCourt(v); } catch { }
        }

        private static Kingdom MyKingdom()
        {
            try { return NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null; } catch { return null; }
        }

        // ================= v5.x: AI 集团(AI 段; 创建/加入/升级/协作) =================
        //   玩家单集团流程不变; AI 集团单独存续, 用于均势外交与战争协同。
        private static readonly Dictionary<string, AiBloc> AiBlocs = new Dictionary<string, AiBloc>();
        private static int _aiWeekDay = -9999;

        private static Kingdom FindKingdomById(string id)
        {
            try
            {
                if (string.IsNullOrEmpty(id)) return null;
                foreach (var k in Kingdom.All) if (k != null && k.StringId == id) return k;
            }
            catch { }
            return null;
        }

        private static AiBloc AiBlocOf(string kingdomId)
        {
            try
            {
                if (string.IsNullOrEmpty(kingdomId)) return null;
                foreach (var kv in AiBlocs)
                    if (kv.Value != null && kv.Value.Members.Contains(kingdomId)) return kv.Value;
            }
            catch { }
            return null;
        }

        internal static bool AiSameBloc(Kingdom a, Kingdom b)
        {
            try
            {
                if (a == null || b == null) return false;
                var ba = AiBlocOf(a.StringId);
                return ba != null && ba.Members.Contains(b.StringId);
            }
            catch { return false; }
        }

        // 同集团成员正与 b 交战 -> 协同参战(供 AiDiplomacy 宣战权重)
        internal static bool AiMateAtWarWith(Kingdom a, Kingdom b)
        {
            try
            {
                if (a == null || b == null) return false;
                var ba = AiBlocOf(a.StringId);
                if (ba == null) return false;
                for (int i = 0; i < ba.Members.Count; i++)
                {
                    if (ba.Members[i] == b.StringId) continue;
                    var m = FindKingdomById(ba.Members[i]);
                    if (m == null) continue;
                    bool war = false;
                    try { war = m.IsAtWarWith(b); } catch { }
                    if (war) return true;
                }
            }
            catch { }
            return false;
        }

        // 集团成员仍在交战且凝聚力足够 -> 不单独媾和
        internal static bool AiShouldNotPeace(Kingdom a, Kingdom b)
        {
            try
            {
                if (a == null || b == null) return false;
                var ba = AiBlocOf(a.StringId);
                if (ba == null || ba.Cohesion < 40f) return false;
                for (int i = 0; i < ba.Members.Count; i++)
                {
                    if (ba.Members[i] == a.StringId) continue;
                    var m = FindKingdomById(ba.Members[i]);
                    if (m == null) continue;
                    bool war = false;
                    try { war = m.IsAtWarWith(b); } catch { }
                    if (war) return true;
                }
            }
            catch { }
            return false;
        }

        private static int AiRankOf(Kingdom k)
        {
            try
            {
                int rank = 1;
                float mine = k.CurrentTotalStrength;
                foreach (var x in Kingdom.All)
                {
                    if (x == null || x.IsEliminated || ReferenceEquals(x, k)) continue;
                    if (x.CurrentTotalStrength > mine) rank++;
                }
                return rank;
            }
            catch { return 9; }
        }

        private static void RecomputeAiCohesion(AiBloc b)
        {
            try
            {
                float v = 30f;
                v -= Math.Max(0, b.Members.Count - 1) * 3f;
                var leader = FindKingdomById(b.LeaderId);
                float all = 0f, my = 0f;
                for (int i = 0; i < b.Members.Count; i++)
                {
                    var m = FindKingdomById(b.Members[i]);
                    if (m == null) continue;
                    float pow = m.CurrentTotalStrength;
                    all += pow;
                    if (ReferenceEquals(m, leader)) my = pow;
                }
                if (all > 0f) v += my / all * 20f;
                b.Cohesion = LawSystem.ClampF(v, 0f, 100f);
            }
            catch { }
        }

        // 月度: AI 创建/加入/升级集团(由 AiAgenda 调用)
        internal static void AiMonthly(Kingdom k, int day)
        {
            try
            {
                if (k == null || k.IsEliminated) return;
                var mine = AiBlocOf(k.StringId);
                if (mine == null)
                {
                    // 1) 加入现有 AI 集团
                    AiBloc join = null;
                    foreach (var kv in AiBlocs)
                    {
                        var b = kv.Value;
                        if (b == null || b.Members.Count >= 5) continue;
                        if (b.Members.Contains(k.StringId)) continue;
                        var leader = FindKingdomById(b.LeaderId);
                        if (leader == null) continue;
                        bool war = false;
                        try { war = k.IsAtWarWith(leader); } catch { }
                        if (war) continue;
                        if (Diplomacy.Get(k, leader) < 0) continue;
                        join = b;
                        break;
                    }
                    if (join != null && MBRandom.RandomFloat < 0.3f)
                    {
                        join.Members.Add(k.StringId);
                        RecomputeAiCohesion(join);
                        AiAgenda.LogDip(k, "加入 " + join.Name + "(成员 " + join.Members.Count + ")");
                        return;
                    }
                    // 2) 创建(强国 + 目标明确; 全球同时只存一个 AI 集团)
                    AiDirector.Goal goal = AiAgenda.GoalOf(k);
                    if (AiBlocs.Count == 0 && AiRankOf(k) <= 3 && Decrees.AiAuthOf(k) >= 120f && MBRandom.RandomFloat < 0.2f)
                    {
                        int ident = goal == AiDirector.Goal.Military ? IdMilitary : (goal == AiDirector.Goal.Economy ? IdTrade : IdEmpire);
                        var b = new AiBloc
                        {
                            LeaderId = k.StringId,
                            Identity = ident,
                            Name = (k.Name != null ? k.Name.ToString() : k.StringId) + " 的" + IdentityNames[ident]
                        };
                        b.Members.Add(k.StringId);
                        Decrees.AiAuthSpend(k, 60f);
                        AiBlocs[k.StringId] = b;
                        AiAgenda.LogDip(k, "创建" + IdentityNames[ident]);
                    }
                    return;
                }
                // 3) 领袖升级原则(按 Goal: 军事->进攻协作, 经济->对外贸易, 主权->附庸化/榨取)
                if (mine.LeaderId == k.StringId)
                {
                    AiDirector.Goal goal = AiAgenda.GoalOf(k);
                    int pi = goal == AiDirector.Goal.Military ? 1 : 0;
                    int cur = mine.Principle[mine.Identity][pi];
                    if (cur < 3 && mine.Mandates >= cur + 1)
                    {
                        mine.Mandates -= cur + 1;
                        mine.Principle[mine.Identity][pi] = cur + 1;
                        AiAgenda.LogDip(k, "集团原则 " + PrincipleNames[mine.Identity][pi] + " -> " + (cur + 1) + " 档");
                    }
                }
            }
            catch { }
        }

        // 周结: 凝聚力/授权/内部关系/贡金(由 AiDiplomacy.Month 调用, 内部按 7 天节流)
        internal static void AiWeekly(int day)
        {
            try
            {
                if (day < _aiWeekDay) AiBlocs.Clear();   // 新档/读档 -> 重置
                if (day - _aiWeekDay < 7) return;
                _aiWeekDay = day;
                if (AiBlocs.Count == 0) return;
                var dead = new List<string>();
                foreach (var kv in AiBlocs)
                {
                    var b = kv.Value;
                    if (b == null) continue;
                    for (int i = b.Members.Count - 1; i >= 0; i--)
                    {
                        var mm = FindKingdomById(b.Members[i]);
                        if (mm == null || mm.IsEliminated) b.Members.RemoveAt(i);
                    }
                    var ld = FindKingdomById(b.LeaderId);
                    if (b.Members.Count == 0 || ld == null || ld.IsEliminated) { dead.Add(kv.Key); continue; }
                    int others = Math.Max(0, b.Members.Count - 1);
                    b.MandateProgress += (2f + others * 4f) * (b.Cohesion / 100f);
                    while (b.MandateProgress >= MandateNeed)
                    {
                        b.MandateProgress -= MandateNeed;
                        if (b.Mandates < MaxMandates) b.Mandates++;
                    }
                    float target = AiCohesionTarget(b);
                    if (b.Cohesion < target) b.Cohesion = Math.Min(target, b.Cohesion + 1f);
                    else if (b.Cohesion > target) b.Cohesion = Math.Max(target, b.Cohesion - 1f);
                    var leader = FindKingdomById(b.LeaderId);
                    for (int i = 0; i < b.Members.Count; i++)
                    {
                        var m = FindKingdomById(b.Members[i]);
                        if (m == null || leader == null || ReferenceEquals(m, leader)) continue;
                        try { Diplomacy.Change(m, leader, 2); } catch { }
                        if (b.Identity == IdEmpire)
                        {
                            int trib = 200 + 50 * b.Principle[IdEmpire][0];
                            try
                            {
                                if (WarEconomy.GoldOfPublic(m) > trib * 2f)
                                {
                                    WarEconomy.SpendPublic(m, trib);
                                    WarEconomy.AddPublic(leader, trib);
                                }
                            }
                            catch { }
                        }
                    }
                }
                for (int i = 0; i < dead.Count; i++) AiBlocs.Remove(dead[i]);
            }
            catch { }
        }

        private static float AiCohesionTarget(AiBloc b)
        {
            try
            {
                float v = 30f;
                v -= Math.Max(0, b.Members.Count - 1) * 3f;
                var leader = FindKingdomById(b.LeaderId);
                float all = 0f, my = 0f;
                for (int i = 0; i < b.Members.Count; i++)
                {
                    var m = FindKingdomById(b.Members[i]);
                    if (m == null) continue;
                    float pow = m.CurrentTotalStrength;
                    all += pow;
                    if (ReferenceEquals(m, leader)) my = pow;
                }
                if (all > 0f) v += my / all * 20f;
                return LawSystem.ClampF(v, 0f, 100f);
            }
            catch { return 30f; }
        }

        // AI 集团存档段(附在玩家 FIA_Bloc 之后, '~' 分隔)
        internal static string SaveAi()
        {
            try
            {
                var sb = new StringBuilder();
                foreach (var kv in AiBlocs)
                {
                    var b = kv.Value;
                    if (b == null) continue;
                    sb.Append('L').Append(b.LeaderId).Append(',').Append(b.Identity).Append(',')
                      .Append((int)b.Cohesion).Append(',').Append((int)b.MandateProgress).Append(',').Append(b.Mandates).Append(';');
                    for (int i = 0; i < b.Members.Count; i++)
                        sb.Append('M').Append(b.LeaderId).Append(',').Append(b.Members[i]).Append(';');
                    for (int id = 0; id < 3; id++)
                        sb.Append('P').Append(b.LeaderId).Append(',').Append(id).Append(',').Append(b.Principle[id][0]).Append(',').Append(b.Principle[id][1]).Append(';');
                }
                return sb.ToString();
            }
            catch { return ""; }
        }

        internal static void LoadAi(string data)
        {
            try
            {
                AiBlocs.Clear();
                if (string.IsNullOrEmpty(data)) return;
                foreach (var seg in data.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (seg.Length < 2) continue;
                    char tag = seg[0];
                    var f = seg.Substring(1).Split(',');
                    if (tag == 'L' && f.Length >= 5)
                    {
                        var b = new AiBloc();
                        int v;
                        b.LeaderId = f[0];
                        if (int.TryParse(f[1], out v)) b.Identity = Math.Max(0, Math.Min(2, v));
                        if (int.TryParse(f[2], out v)) b.Cohesion = v;
                        if (int.TryParse(f[3], out v)) b.MandateProgress = v;
                        if (int.TryParse(f[4], out v)) b.Mandates = v;
                        b.Name = NameOfKingdom(b.LeaderId) + " 的" + IdentityNames[b.Identity];
                        AiBlocs[b.LeaderId] = b;
                    }
                    else if (tag == 'M' && f.Length >= 2)
                    {
                        AiBloc b;
                        if (AiBlocs.TryGetValue(f[0], out b) && !b.Members.Contains(f[1])) b.Members.Add(f[1]);
                    }
                    else if (tag == 'P' && f.Length >= 4)
                    {
                        AiBloc b;
                        int id, a, c;
                        if (AiBlocs.TryGetValue(f[0], out b)
                            && int.TryParse(f[1], out id) && id >= 0 && id < 3
                            && int.TryParse(f[2], out a) && int.TryParse(f[3], out c))
                        {
                            b.Principle[id][0] = Math.Max(0, Math.Min(3, a));
                            b.Principle[id][1] = Math.Max(0, Math.Min(3, c));
                        }
                    }
                }
                DLog.Force("权力集团: AI 读档 " + AiBlocs.Count + " 个");
            }
            catch { }
        }

        // ================= 存档 FIA_Bloc =================
        internal static string Save()
        {
            try
            {
                var sb = new StringBuilder();
                sb.Append("v1;");
                sb.Append(Founded ? "1" : "0").Append(',').Append(Identity).Append(',').Append(LeaderId ?? "").Append(',')
                  .Append(Uri.EscapeDataString(Name ?? "")).Append(',')
                  .Append(Cohesion.ToString("F1", CultureInfo.InvariantCulture)).Append(',')
                  .Append(MandateProgress.ToString("F1", CultureInfo.InvariantCulture)).Append(',').Append(Mandates);
                sb.Append(';');
                for (int i = 0; i < 3; i++)
                    sb.Append(PrincipleTier[i][0]).Append(',').Append(PrincipleTier[i][1]).Append(';');
                for (int i = 0; i < Members.Count; i++)
                {
                    if (i > 0) sb.Append('|');
                    sb.Append(Members[i].KingdomId).Append(',').Append(Members[i].JoinDay);
                }
                sb.Append(';');
                sb.Append('~');           // v5.x: AI 集团段(旧档无此段, Load 兼容)
                sb.Append(SaveAi());
                return sb.ToString();
            }
            catch { return "v1;"; }
        }

        internal static void Load(string data)
        {
            try
            {
                string aiPart = null;
                int tilde = data != null ? data.IndexOf('~') : -1;
                if (tilde >= 0) { aiPart = data.Substring(tilde + 1); data = data.Substring(0, tilde); }
                LoadAi(aiPart);
                if (string.IsNullOrEmpty(data)) return;
                var seg = data.Split(';');
                if (seg.Length > 1 && !string.IsNullOrEmpty(seg[1]))
                {
                    var f = seg[1].Split(',');
                    Founded = f.Length > 0 && f[0] == "1";
                    int x;
                    if (f.Length > 1 && int.TryParse(f[1], out x)) Identity = Math.Max(0, Math.Min(2, x));
                    if (f.Length > 2) LeaderId = f[2];
                    if (f.Length > 3) Name = Uri.UnescapeDataString(f[3]);
                    float p;
                    if (f.Length > 4 && float.TryParse(f[4], NumberStyles.Float, CultureInfo.InvariantCulture, out p)) Cohesion = p;
                    if (f.Length > 5 && float.TryParse(f[5], NumberStyles.Float, CultureInfo.InvariantCulture, out p)) MandateProgress = p;
                    if (f.Length > 6 && int.TryParse(f[6], out x)) Mandates = x;
                }
                for (int i = 0; i < 3 && i + 2 < seg.Length; i++)
                {
                    var f = seg[i + 2].Split(',');
                    int x;
                    if (f.Length > 0 && int.TryParse(f[0], out x)) PrincipleTier[i][0] = Math.Max(0, Math.Min(3, x));
                    if (f.Length > 1 && int.TryParse(f[1], out x)) PrincipleTier[i][1] = Math.Max(0, Math.Min(3, x));
                }
                Members.Clear();
                if (seg.Length > 5 && !string.IsNullOrEmpty(seg[5]))
                {
                    foreach (var line in seg[5].Split('|'))
                    {
                        if (string.IsNullOrEmpty(line)) continue;
                        var f = line.Split(',');
                        if (f.Length < 2) continue;
                        int x;
                        int.TryParse(f[1], out x);
                        Members.Add(new BlocMember { KingdomId = f[0], Name = NameOfKingdom(f[0]), JoinDay = x });
                    }
                }
                DLog.Force("权力集团: 读档 " + StatusText());
            }
            catch { }
        }

        private static string NameOfKingdom(string id)
        {
            try
            {
                foreach (var k in Kingdom.All) if (k != null && k.StringId == id) return k.Name != null ? k.Name.ToString() : id;
            }
            catch { }
            return id;
        }

        internal static void Reset()
        {
            Founded = false;
            Identity = IdTrade;
            LeaderId = null;
            Name = "";
            Members.Clear();
            Cohesion = 30f;
            MandateProgress = 0f;
            Mandates = 0;
            for (int i = 0; i < 3; i++) { PrincipleTier[i][0] = 0; PrincipleTier[i][1] = 0; }
            _lastWeekDay = -9999;
            AiBlocs.Clear();      // v5.x: AI 集团
            _aiWeekDay = -9999;
        }

        // 成员列表(UI)
        internal static Kingdom KingdomByIndex(int i)
        {
            try
            {
                if (i < 0 || i >= Members.Count) return null;
                foreach (var k in Kingdom.All) if (k != null && k.StringId == Members[i].KingdomId) return k;
            }
            catch { }
            return null;
        }
    }

    // 利益宣示(文档 24.8; V3 Interests: 无利益区域不能介入战争/博弈)
    internal static class Interests
    {
        internal static readonly HashSet<string> Declared = new HashSet<string>();
        internal static int ChangeDay = -9999;

        // 外交容量(V3: 受外交容量限制; v4.139: 国家规模驱动, 不再用影响力)
        internal static int Capacity()
        {
            try
            {
                var k = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                if (k == null) return 2;
                return 2 + k.Fiefs.Count / 5 + (k.Leader != null ? k.Leader.Clan.Tier : 0);
            }
            catch { return 2; }
        }

        internal static bool Has(string kingdomId)
        {
            return kingdomId != null && (Declared.Contains(kingdomId) || IsNeighbor(kingdomId));
        }

        private static bool IsNeighbor(string kingdomId)
        {
            try
            {
                var k = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                if (k == null) return false;
                foreach (var other in Kingdom.All)
                {
                    if (other == null || other.StringId != kingdomId) continue;
                    foreach (var f in k.Fiefs)
                        foreach (var f2 in other.Fiefs)
                            if (f != null && f2 != null && f.Settlement.Position.Distance(f2.Settlement.Position) < 60f) return true;
                }
            }
            catch { }
            return false;
        }

        internal static string Declare(Kingdom target)
        {
            try
            {
                if (target == null) return "无效目标";
                var k = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                if (k == null) return "没有国家";
                if (target == k) return "不能对自己宣示利益";
                if (Declared.Contains(target.StringId)) return "已对 " + target.Name + " 宣示利益";
                if (IsNeighbor(target.StringId)) return target.Name + " 是邻国, 无需宣示(自动有利益)";
                if (Declared.Count >= Capacity()) return "外交容量已满(" + Declared.Count + "/" + Capacity() + "); 扩张领地或提升家族等级可增加容量";
                if (PowerBlocs.Treasury() < 500) return "国库不足(需 500 第纳尔)";
                PowerBlocs.TreasurySpend(500);
                Declared.Add(target.StringId);
                ChangeDay = Politics.Today();
                DLog.Force("利益: 宣示 -> " + target.StringId);
                return "已对 " + target.Name + " 宣示利益(国库 -500)";
            }
            catch { return "宣示失败"; }
        }

        internal static string StatusText()
        {
            try
            {
                return "利益宣示 " + Declared.Count + "/" + Capacity() + "(未宣示的远方国家不可介入)";
            }
            catch { return ""; }
        }

        internal static string Save()
        {
            var sb = new StringBuilder();
            sb.Append("v1;");
            foreach (var id in Declared) sb.Append(id).Append('|');
            sb.Append(';').Append(ChangeDay).Append(';');
            return sb.ToString();
        }

        internal static void Load(string data)
        {
            try
            {
                Declared.Clear();
                if (string.IsNullOrEmpty(data)) return;
                var seg = data.Split(';');
                if (seg.Length > 1 && !string.IsNullOrEmpty(seg[1]))
                    foreach (var id in seg[1].Split('|')) if (!string.IsNullOrEmpty(id)) Declared.Add(id);
                int x;
                if (seg.Length > 2 && int.TryParse(seg[2], out x)) ChangeDay = x;
                DLog.Force("利益: 读档 " + StatusText());
            }
            catch { }
        }

        internal static void Reset()
        {
            Declared.Clear();
            ChangeDay = -9999;
        }
    }
}
