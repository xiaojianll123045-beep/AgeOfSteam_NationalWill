using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace FeudalInternalAffairs
{
    // ================== 外交博弈(文档 22 章; 参照维多利亚 3 Diplomatic Play) ==================
    // 所有战争都要先开展"博弈": 发起方提诉求 -> 对方屈服或拒绝 -> 倒计时内双方加诉求/拉拢第三国 -> 开战
    // 战争结束后按诉求清单执行(割地/赔款/纳贡/羞辱/通商)

    internal enum GoalKind { Conquer = 0, Tribute = 1, Vassalize = 2, Humiliate = 3, OpenMarket = 4 }

    internal class WarGoal
    {
        internal GoalKind Kind;
        internal string SettlementId;   // 仅 Conquer
        internal bool Secondary;
        internal string OwnerId;        // 诉求归属(提出方)
        internal bool Enforced;
        internal int Value;             // 赔款额/恶名基数
    }

    internal class PlayMember
    {
        internal string KingdomId;
        internal int Side;      // 0 发起方 1 防御方 2 未定
        internal int Stance;    // 0 中立 1 倾向发起 2 倾向防御 3 已加入发起 4 已加入防御 5 拒绝
        internal int SwayBy;    // 0 无 1 被发起方拉拢 2 被防御方拉拢
    }

    internal class DiploPlay
    {
        internal int Id;
        internal string InitiatorId, TargetId;
        internal int StartDay;
        internal float Escalation;      // 0..100
        internal int Pause;             // 升级暂停天数(每次动作 +5, 上限 20)
        internal float ManeuverA, ManeuverB;
        internal bool WarDeclared;
        internal bool Notified;
        internal int AiActions;         // AI 已做动作数(超过上限后不再拖延激化度)
        internal readonly List<WarGoal> Demands = new List<WarGoal>();
        internal readonly List<PlayMember> Members = new List<PlayMember>();
        internal string RegionName = "";

        internal bool Involves(string id)
        {
            return InitiatorId == id || TargetId == id || FindMember(id) != null;
        }

        internal PlayMember FindMember(string id)
        {
            for (int i = 0; i < Members.Count; i++) if (Members[i].KingdomId == id) return Members[i];
            return null;
        }

        internal PlayMember GetOrCreate(string id)
        {
            var m = FindMember(id);
            if (m == null) { m = new PlayMember { KingdomId = id, Side = 2 }; Members.Add(m); }
            return m;
        }
    }

    internal class WarTrack
    {
        internal string KingdomId;
        internal float Support = 100f;
        internal int StartDay;
    }

    internal class Vassalage
    {
        internal string VassalId, OverlordId;
        internal int UntilDay;
        internal int Monthly;
    }

    internal class MarketAccess
    {
        internal string A, B;
        internal int UntilDay;
    }

    // 拉拢条件(参照 V3 "拉拢" 界面: 以未来利益换取第三国支持)
    internal enum SwayKind { Obligation = 0, TributeShare = 1, OpenMarket = 2, Land = 3, Puppet = 4, Gold = 5 }

    internal class SwayPromise
    {
        internal string ById;      // 承诺方(我)
        internal string ToId;      // 得到好处的第三国
        internal string EnemyId;   // 战争对手
        internal SwayKind Kind;
        internal int Day;
        internal bool Settled;
    }

    internal static class DiploPlays
    {
        internal static readonly List<DiploPlay> Active = new List<DiploPlay>();
        internal static readonly Dictionary<string, float> Infamy = new Dictionary<string, float>();
        internal static readonly Dictionary<string, WarTrack> War = new Dictionary<string, WarTrack>();
        internal static readonly List<Vassalage> Vassals = new List<Vassalage>();
        internal static readonly List<MarketAccess> Markets = new List<MarketAccess>();
        internal static readonly List<SwayPromise> Promises = new List<SwayPromise>();
        internal static readonly Dictionary<string, int> Humiliated = new Dictionary<string, int>();  // "victim|aggressor" -> untilDay
        // 战后待执行诉求: "胜方id|败方id" -> goals
        private static readonly Dictionary<string, List<WarGoal>> Pending = new Dictionary<string, List<WarGoal>>();

        private static int _nextId = 1;

        internal static int Today()
        {
            try { return (int)CampaignTime.Now.ToDays; } catch { return 0; }
        }

        internal static Kingdom K(string id)
        {
            try
            {
                if (string.IsNullOrEmpty(id)) return null;
                foreach (var k in Kingdom.All) if (k != null && k.StringId == id) return k;
            }
            catch { }
            return null;
        }

        internal static string NameOf(string id)
        {
            var k = K(id);
            return k != null && k.Name != null ? k.Name.ToString() : (id ?? "?");
        }

        internal static bool IsPlayerKingdom(Kingdom k)
        {
            try
            {
                var pk = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                return k != null && ReferenceEquals(k, pk);
            }
            catch { return false; }
        }

        internal static DiploPlay PlayOf(string kingdomId)
        {
            try
            {
                for (int i = 0; i < Active.Count; i++)
                {
                    var p = Active[i];
                    if (!p.WarDeclared && p.Involves(kingdomId)) return p;
                }
            }
            catch { }
            return null;
        }

        internal static DiploPlay PlayerPlay()
        {
            try
            {
                // UI 手动选中的世界博弈优先(可查看/介入任意一场)
                if (UiSelectedPlayId >= 0)
                {
                    for (int i = 0; i < Active.Count; i++)
                    {
                        var sel = Active[i];
                        if (sel.Id == UiSelectedPlayId && !sel.WarDeclared) return sel;
                    }
                    UiSelectedPlayId = -1;   // 已结束, 清除
                }
                var pk = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                if (pk == null) return null;
                for (int i = 0; i < Active.Count; i++)
                {
                    var p = Active[i];
                    if (!p.WarDeclared && (p.InitiatorId == pk.StringId || p.TargetId == pk.StringId || p.FindMember(pk.StringId) != null))
                        return p;
                }
                // 玩家未卷入时也展示世界上的博弈(可主动介入)
                for (int i = 0; i < Active.Count; i++)
                    if (!Active[i].WarDeclared) return Active[i];
            }
            catch { }
            return null;
        }

        // UI 选中的世界博弈(供 VM 切换查看)
        internal static int UiSelectedPlayId = -1;

        internal static DiploPlay FindPlay(int id)
        {
            try
            {
                for (int i = 0; i < Active.Count; i++) if (Active[i].Id == id) return Active[i];
            }
            catch { }
            return null;
        }

        internal static float InfamyOf(string kingdomId)
        {
            float v;
            return kingdomId != null && Infamy.TryGetValue(kingdomId, out v) ? v : 0f;
        }

        internal static bool IsVassalOf(string vassalId, string overlordId)
        {
            for (int i = 0; i < Vassals.Count; i++)
            {
                var v = Vassals[i];
                if (v.VassalId == vassalId && v.OverlordId == overlordId && Today() < v.UntilDay) return true;
            }
            return false;
        }

        internal static bool IsHumiliatedBy(string victimId, string aggressorId)
        {
            int until;
            return !string.IsNullOrEmpty(victimId) && !string.IsNullOrEmpty(aggressorId)
                && Humiliated.TryGetValue(victimId + "|" + aggressorId, out until) && Today() < until;
        }

        // ================== 诉求定义 ==================
        internal static string GoalName(GoalKind k)
        {
            switch (k)
            {
                case GoalKind.Conquer: return "割让领地";
                case GoalKind.Tribute: return "战争赔款";
                case GoalKind.Vassalize: return "称臣纳贡";
                case GoalKind.Humiliate: return "羞辱";
                default: return "开放商路";
            }
        }

        internal static int GoalCost(GoalKind k)
        {
            switch (k)
            {
                case GoalKind.Conquer: return 10;
                case GoalKind.Tribute: return 10;
                case GoalKind.Vassalize: return 20;
                case GoalKind.Humiliate: return 20;
                default: return 15;
            }
        }

        internal static int GoalInfamy(GoalKind k)
        {
            switch (k)
            {
                case GoalKind.Conquer: return 20;
                case GoalKind.Tribute: return 10;
                case GoalKind.Vassalize: return 30;
                case GoalKind.Humiliate: return 8;
                default: return 12;
            }
        }

        // 有效目标定居点: 与发起方接壤的敌国定居点
        internal static Settlement FindConquerTarget(Kingdom by, Kingdom on)
        {
            try
            {
                Settlement best = null;
                float bd = float.MaxValue;
                if (on.Settlements == null) return null;
                for (int i = 0; i < on.Settlements.Count; i++)
                {
                    var s = on.Settlements[i];
                    if (s == null) continue;
                    float d = NearestDistance(by, s);
                    if (d < bd) { bd = d; best = s; }
                }
                return bd <= 70f ? best : null;
            }
            catch { return null; }
        }

        private static float NearestDistance(Kingdom k, Settlement s)
        {
            float best = float.MaxValue;
            try
            {
                if (k.Settlements == null) return best;
                for (int i = 0; i < k.Settlements.Count; i++)
                {
                    var a = k.Settlements[i];
                    if (a == null) continue;
                    float d = a.Position.Distance(s.Position);
                    if (d < best) best = d;
                }
            }
            catch { }
            return best;
        }

        internal static int TributeAmount(Kingdom from, Kingdom to)
        {
            try
            {
                float s = from.CurrentTotalStrength;
                int v = (int)(s * 60f) + 1000;
                if (v < 1500) v = 1500;
                if (v > 30000) v = 30000;
                return v;
            }
            catch { return 3000; }
        }

        // ================== 启动博弈 ==================
        internal static DiploPlay StartPlay(Kingdom a, Kingdom b, bool playerInitiated)
        {
            try
            {
                if (a == null || b == null || a == b) return null;
                if (a.IsAtWarWith(b) || Diplomacy.IsAlly(a, b)) return null;
                if (PlayOf(a.StringId) != null || PlayOf(b.StringId) != null) return null;
                if (IsVassalOf(a.StringId, b.StringId)) return null;
                if (IsHumiliatedBy(a.StringId, b.StringId)) return null;   // 被羞辱方 5 年内不能主动发

                var p = new DiploPlay
                {
                    Id = _nextId++,
                    InitiatorId = a.StringId,
                    TargetId = b.StringId,
                    StartDay = Today(),
                    ManeuverA = ManeuverOf(a),
                    ManeuverB = ManeuverOf(b)
                };
                var conquerSid = FindConquerTarget(a, b);
                if (!playerInitiated && conquerSid != null)
                    AddGoal(p, a, GoalKind.Conquer, conquerSid.StringId, false, 1);   // AI 自动提第一条; 玩家自己选
                p.GetOrCreate(a.StringId).Side = 0;
                p.GetOrCreate(b.StringId).Side = 1;
                p.GetOrCreate(a.StringId).Stance = 3;
                p.GetOrCreate(b.StringId).Stance = 4;
                Active.Add(p);

                var pk0 = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                bool involvesPlayer = pk0 != null && (a == pk0 || b == pk0);
                Notify("外交博弈开始: " + NameOf(a.StringId) + " 向 " + NameOf(b.StringId) + " 提出诉求(" + GoalName(GoalKind.Conquer) + ")"
                    + (involvesPlayer ? " — 打开左侧『博弈』页应对" : ""), involvesPlayer);
                DLog.Force("外交博弈: " + a.StringId + " -> " + b.StringId + " 博弈#" + p.Id);
                return p;
            }
            catch (Exception ex) { DLog.Force("启动博弈失败: " + ex.Message); return null; }
        }

        internal static float ManeuverOf(Kingdom k)
        {
            try
            {
                float s = k.CurrentTotalStrength;
                if (s >= 8000f) return 100f;
                if (s >= 4500f) return 75f;
                if (s >= 2200f) return 60f;
                return 50f;
            }
            catch { return 50f; }
        }

        private static void AddGoal(DiploPlay p, Kingdom by, GoalKind kind, string sid, bool secondary, int sign, int valueOverride = 0)
        {
            try
            {
                var g = new WarGoal { Kind = kind, SettlementId = sid, Secondary = secondary, OwnerId = by.StringId };
                if (valueOverride > 0) g.Value = valueOverride;
                else if (kind == GoalKind.Tribute || kind == GoalKind.Vassalize)
                {
                    var other = sign > 0 ? K(p.TargetId) : K(p.InitiatorId);
                    g.Value = TributeAmount(other, by);
                }
                p.Demands.Add(g);
                float inf = GoalInfamy(kind);
                if (secondary) inf *= 0.5f;
                AddInfamy(by.StringId, inf);
                SpreadInfamy(by, inf);
            }
            catch { }
        }

        private static void AddInfamy(string id, float v)
        {
            try { Infamy[id] = InfamyOf(id) + v; } catch { }
        }

        // 恶名当即降低所有其他国家与该国的关系
        private static void SpreadInfamy(Kingdom by, float inf)
        {
            try
            {
                int d = -(int)Math.Round(inf / 4f);
                if (d == 0) return;
                foreach (var k in Kingdom.All)
                {
                    if (k == null || k == by || k.IsEliminated) continue;
                    Diplomacy.Change(by, k, d);
                }
            }
            catch { }
        }

        // ================== 加诉求(玩家/AI) ==================
        internal static string AddDemandBy(DiploPlay p, Kingdom by, GoalKind kind)
        {
            string sid = null;
            if (kind == GoalKind.Conquer)
            {
                bool isInit = p.InitiatorId == by.StringId;
                var other = isInit ? K(p.TargetId) : K(p.InitiatorId);
                var cand = FindConquerTarget(by, other);
                if (cand == null) return "没有接壤的敌方领地可以索取";
                sid = cand.StringId;
            }
            return AddDemandAt(p, by, kind, sid);
        }

        // 指定目标的加诉求(割地由 UI 选点; 赔款/纳贡由 UI 输入金额)
        internal static string AddDemandAt(DiploPlay p, Kingdom by, GoalKind kind, string sid, int valueOverride = 0)
        {
            try
            {
                if (p == null || by == null || p.WarDeclared) return "博弈已结束";
                bool isInit = p.InitiatorId == by.StringId;
                bool isTarget = p.TargetId == by.StringId;
                if (!isInit && !isTarget) return "你不是博弈主角";
                if (p.Escalation >= 80f) return "已进入开战倒计时, 不能再加诉求";
                if (kind == GoalKind.Vassalize && !isInit) return "防御方不能索取称臣";
                float maneuvers = isInit ? p.ManeuverA : p.ManeuverB;
                int cost = GoalCost(kind);
                // 双方第一条诉求免费(参照 V3)
                bool firstFree = CountGoals(p, by) == 0;
                if (!firstFree)
                {
                    if (maneuvers < cost) return "机动点不足(需 " + cost + " 当前 " + (int)maneuvers + ")";
                    if (isInit) p.ManeuverA -= cost; else p.ManeuverB -= cost;
                }
                if (kind == GoalKind.Conquer)
                {
                    if (string.IsNullOrEmpty(sid)) return "请选择要割让的领地";
                    var st = FindSettlement(sid);
                    var other = isInit ? K(p.TargetId) : K(p.InitiatorId);
                    if (st == null || other == null || st.MapFaction != other) return "该领地不属于对方";
                }
                AddGoal(p, by, kind, sid, false, isInit ? 1 : -1, valueOverride);
                p.Pause = Math.Min(20, p.Pause + 5);
                return (firstFree ? "已提出诉求(第一条免费): " : "已加入诉求: ") + GoalName(kind)
                    + ((kind == GoalKind.Tribute || kind == GoalKind.Vassalize) && valueOverride > 0 ? " · " + valueOverride.ToString("N0") : "");
            }
            catch { return "加诉求失败"; }
        }

        // 可索取的领地表(接壤范围内的敌国定居点, 按距离排序)
        internal static List<Settlement> ConquerCandidates(Kingdom by, Kingdom on, int max)
        {
            var list = new List<Settlement>();
            try
            {
                if (by == null || on == null || on.Settlements == null) return list;
                var cands = new List<Settlement>();
                for (int i = 0; i < on.Settlements.Count; i++)
                {
                    var s = on.Settlements[i];
                    if (s == null) continue;
                    if (NearestDistance(by, s) <= 110f) cands.Add(s);
                }
                cands.Sort(delegate (Settlement x, Settlement y) { return NearestDistance(by, x).CompareTo(NearestDistance(by, y)); });
                for (int i = 0; i < cands.Count && i < max; i++) list.Add(cands[i]);
            }
            catch { }
            return list;
        }

        private static int CountGoals(DiploPlay p, Kingdom by)
        {
            int n = 0;
            for (int i = 0; i < p.Demands.Count; i++) if (p.Demands[i].OwnerId == by.StringId) n++;
            return n;
        }

        private static bool HasGoalBy(DiploPlay p, Kingdom by)
        {
            for (int i = 0; i < p.Demands.Count; i++) if (p.Demands[i].OwnerId == by.StringId) return true;
            return false;
        }

        private static void MarkStanceAgainst(DiploPlay p, Kingdom by) { }

        // ================== 拉拢 ==================
        internal static int Preference(DiploPlay p, Kingdom who, int side)
        {
            try
            {
                var pk = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                var init = K(p.InitiatorId);
                var target = K(p.TargetId);
                if (who == null || init == null || target == null) return 0;
                if (IsPlayerKingdom(who)) return 0;   // 玩家自己决定
                int relA = Diplomacy.Get(who, init);
                int relB = Diplomacy.Get(who, target);
                int baseScore = (relA - relB);

                // 盟友直接大量加分
                try
                {
                    if (Diplomacy.IsAlly(who, init)) baseScore += 60;
                    if (Diplomacy.IsAlly(who, target)) baseScore -= 60;
                }
                catch { }

                // 投机: 靠向更强的一方
                float sa = SafeStrength(init), sb = SafeStrength(target);
                float total = sa + sb;
                float bias = total > 1f ? (sa - sb) / total * 40f : 0f;
                baseScore += (int)bias;

                // 恶名: 发起方恶名高 -> 别人更愿意帮防御方
                baseScore -= (int)(InfamyOf(p.InitiatorId) * 0.5f);

                // 已有倾向/拉拢/加入
                var m = p.FindMember(who.StringId);
                if (m != null)
                {
                    if (m.Stance == 3) return 100;
                    if (m.Stance == 4) return -100;
                    if (m.SwayBy == 1 && side == 0) baseScore += 15;
                    if (m.SwayBy == 2 && side == 1) baseScore -= 0;
                    if (m.SwayBy == 2) baseScore -= 15;
                }
                return baseScore;
            }
            catch { return 0; }
        }

        private static float SafeStrength(Kingdom k)
        {
            try { return Math.Max(1f, k.CurrentTotalStrength); } catch { return 1f; }
        }

        internal static string SwayBy(DiploPlay p, Kingdom by)
        {
            try
            {
                if (p == null || by == null || p.WarDeclared) return "博弈已结束";
                bool isInit = p.InitiatorId == by.StringId;
                if (!isInit && p.TargetId != by.StringId) return "你不是博弈主角";
                if (p.Escalation >= 80f) return "已进入开战倒计时, 不能再拉拢";
                float maneuvers = isInit ? p.ManeuverA : p.ManeuverB;
                if (maneuvers < 20f) return "机动点不足(需 20)";
                var pk = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                // 找最值得拉拢的目标: 倾向接近自己但还没加入
                Kingdom bestK = null;
                int bestScore = 0;
                foreach (var k in Kingdom.All)
                {
                    if (k == null || k.IsEliminated || k == by || k == K(p.InitiatorId) || k == K(p.TargetId)) continue;
                    if (IsPlayerKingdom(k)) continue;   // 玩家不AI拉拢
                    var m = p.FindMember(k.StringId);
                    if (m != null && (m.Stance == 3 || m.Stance == 4)) continue;
                    int pref = Preference(p, k, isInit ? 0 : 1);
                    if (isInit && pref <= 0) continue;
                    if (!isInit && pref >= 0) continue;
                    bool atWarWithUs = false;
                    try { atWarWithUs = k.IsAtWarWith(by); } catch { }
                    if (atWarWithUs) continue;
                    int score = Math.Abs(pref);
                    if (score > bestScore) { bestScore = score; bestK = k; }
                }
                if (bestK == null) return "没有可以拉拢的国家";
                if (isInit) p.ManeuverA -= 20f; else p.ManeuverB -= 20f;
                p.Pause = Math.Min(20, p.Pause + 5);
                var mm = p.GetOrCreate(bestK.StringId);
                if (isInit) { mm.SwayBy = 1; mm.Stance = 1; }
                else { mm.SwayBy = 2; mm.Stance = 2; }
                var pkk = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                if (by == pkk || bestK == pkk)
                    Notify("外交博弈: " + NameOf(by.StringId) + " 拉拢 " + NameOf(bestK.StringId) + " 倾向自己一方", false);
                DLog.Force("博弈拉拢: " + by.StringId + " -> " + bestK.StringId);
                return "已拉拢 " + NameOf(bestK.StringId) + " 站到这边";
            }
            catch { return "拉拢失败"; }
        }

        // 玩家拉拢指定国家(按王国 id)
        internal static string PlayerSway(string kingdomId)
        {
            try
            {
                var p = PlayerPlay();
                if (p == null) return "没有你参与的博弈";
                var pk = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                if (pk == null) return "没有国家";
                bool isInit = p.InitiatorId == pk.StringId;
                if (!isInit && p.TargetId != pk.StringId) return "你不是博弈主角";
                if (string.IsNullOrEmpty(kingdomId)) return "无效目标";
                var m = p.FindMember(kingdomId);
                if (m == null) return "目标不存在";
                if (m.KingdomId == pk.StringId || m.KingdomId == p.InitiatorId || m.KingdomId == p.TargetId) return "对方是博弈主角";
                if (m.Stance == 3 || m.Stance == 4) return "对方已参战";
                if (p.Escalation >= 80f) return "已进入开战倒计时, 不能再拉拢";
                float maneuvers = isInit ? p.ManeuverA : p.ManeuverB;
                if (maneuvers < 20f) return "机动点不足(需 20)";
                var who = K(m.KingdomId);
                if (who == null) return "国家不存在";
                int pref = Preference(p, who, isInit ? 0 : 1);
                int need = isInit ? 15 : -15;
                if (isInit && pref < need) return NameOf(m.KingdomId) + " 不愿站过来(倾向 " + pref + ")";
                if (!isInit && pref > need) return NameOf(m.KingdomId) + " 不愿站过来(倾向 " + pref + ")";
                if (isInit) p.ManeuverA -= 20f; else p.ManeuverB -= 20f;
                p.Pause = Math.Min(20, p.Pause + 5);
                m.SwayBy = isInit ? 1 : 2;
                m.Stance = isInit ? 1 : 2;
                Notify("你拉拢了 " + NameOf(m.KingdomId), false);
                return "已拉拢 " + NameOf(m.KingdomId);
            }
            catch { return "拉拢失败"; }
        }

        // ================== 退让/屈服 ==================
        internal static string BackDown(DiploPlay p, Kingdom by)
        {
            try
            {
                if (p == null || by == null || p.WarDeclared) return "博弈已结束";
                if (p.Escalation < 20f) return "落子阶段还不能退让(等升级到 20)";
                if (p.InitiatorId != by.StringId) return "只有发起方可以退让";
                Active.Remove(p);
                // 恶名部分退还(参照 V3: 75%)
                RefundInfamy(p, by);
                Notify("外交博弈: " + NameOf(by.StringId) + " 退让, 诉求落空(恶名部分退还)", false);
                DLog.Force("博弈退让: " + by.StringId + " 博弈#" + p.Id);
                return "已退让, 博弈结束";
            }
            catch { return "退让失败"; }
        }

        internal static string GiveIn(DiploPlay p, Kingdom by)
        {
            try
            {
                if (p == null || by == null || p.WarDeclared) return "博弈已结束";
                if (p.Escalation < 20f) return "落子阶段还不能屈服(等升级到 20)";
                if (p.TargetId != by.StringId) return "只有防御方可以屈服";
                // 主要诉求直接执行
                EnforceFrom(p, K(p.InitiatorId), by, true);
                Active.Remove(p);
                Notify("外交博弈: " + NameOf(by.StringId) + " 屈服, 接受全部主要诉求", false);
                DLog.Force("博弈屈服: " + by.StringId + " 博弈#" + p.Id);
                return "已屈服, 诉求生效";
            }
            catch { return "屈服失败"; }
        }

        private static void RefundInfamy(DiploPlay p, Kingdom by)
        {
            try
            {
                float total = 0f;
                for (int i = 0; i < p.Demands.Count; i++) total += GoalInfamy(p.Demands[i].Kind) * (p.Demands[i].Secondary ? 0.5f : 1f);
                AddInfamy(by.StringId, -total * 0.75f);
                if (InfamyOf(by.StringId) < 0f) Infamy[by.StringId] = 0f;
            }
            catch { }
        }

        // ================== 第三国加入 ==================
        internal static string JoinSide(Kingdom who, DiploPlay p, int side)
        {
            try
            {
                if (p == null || who == null || p.WarDeclared) return "博弈已结束";
                if (who.StringId == p.InitiatorId || who.StringId == p.TargetId) return "你已是博弈主角";
                if (p.Escalation >= 80f) return "已进入开战倒计时, 不能再站队";
                var m = p.GetOrCreate(who.StringId);
                if (m.Stance == 3 || m.Stance == 4) return "已参战";
                // 介入条件: 与任一主角接壤或结盟(参照 V3 利益区)
                var init = K(p.InitiatorId);
                var target = K(p.TargetId);
                bool canReach = (init != null && (AiDiplomacy.Borders(who, init) || Diplomacy.IsAlly(who, init)))
                             || (target != null && (AiDiplomacy.Borders(who, target) || Diplomacy.IsAlly(who, target)));
                if (!canReach) return "距离太远(不接壤且无同盟), 无法介入";
                int pref = Preference(p, who, side);
                if (side == 0 && pref < 0) return "国内反对帮助发起方(倾向 " + pref + ")";
                if (side == 1 && pref > 0) return "国内反对帮助防御方(倾向 " + pref + ")";
                m.Stance = side == 0 ? 3 : 4;
                m.Side = side;
                p.Pause = Math.Min(20, p.Pause + 5);
                Notify("外交博弈: " + NameOf(who.StringId) + " 加入" + (side == 0 ? "发起方" : "防御方"), false);
                DLog.Force("博弈加入: " + who.StringId + " side=" + side);
                return "已加入" + (side == 0 ? "发起方" : "防御方");
            }
            catch { return "加入失败"; }
        }

        // ================== 每日结算 ==================
        internal static void Daily(int day)
        {
            try
            {
                // 1) 博弈推进
                for (int i = Active.Count - 1; i >= 0; i--)
                {
                    var p = Active[i];
                    if (p.WarDeclared) { Active.RemoveAt(i); continue; }
                    // 暂停期间也保留一半速度(保证一定能推进到开战, 不再"永远不涨")
                    if (p.Pause > 0) { p.Pause--; p.Escalation += 0.55f; }
                    else p.Escalation += 1.1f;
                    DLog.Force("博弈日结: #" + p.Id + " 升级=" + ((int)p.Escalation) + " 暂停=" + p.Pause + " AI动作=" + p.AiActions);

                    // 阶段通知
                    if (!p.Notified && p.Escalation >= 20f)
                    {
                        p.Notified = true;
                        var pk = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                        if (pk != null && p.Involves(pk.StringId))
                            Notify("外交博弈进入拉拢阶段: 你可以在「博弈」页加诉求/拉拢/屈服或退让", true);
                    }
                    // AI 决策(每 4 天)
                    if (day % 4 == 0) AiDecide(p, day);
                    // 倒计时结束 -> 开战
                    if (p.Escalation >= 100f) DeclareWarFor(p);
                }

                // 2) 战时支持度
                WarSupportDaily(day);

                // 3) 附庸纳贡(月度)
                if (CampaignTime.Now.GetDayOfWeek == 0) VassalMonthly();

                // 4) 恶名衰减
                var keys = new List<string>(Infamy.Keys);
                for (int i = 0; i < keys.Count; i++)
                {
                    float v = InfamyOf(keys[i]) - 0.5f;
                    Infamy[keys[i]] = v < 0f ? 0f : v;
                }
            }
            catch (Exception ex) { DLog.Force("博弈日结异常: " + ex.Message); }
        }

        // ---- AI 决策: 加诉求 / 拉拢 / 站队 / 屈服 / 退让 (每次最多一个动作, 防止激化度被无限暂停) ----
        private static void AiDecide(DiploPlay p, int day)
        {
            try
            {
                var init = K(p.InitiatorId);
                var target = K(p.TargetId);
                if (init == null || target == null) { Active.Remove(p); return; }
                var pk = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;

                // 防御方第一条诉求(免费, 只做一次)
                if (!HasGoalBy(p, target) && p.ManeuverB >= 10f && !IsPlayerKingdom(target))
                {
                    p.ManeuverB -= 10f;
                    AddGoal(p, target, GoalKind.Tribute, null, false, -1);
                    p.Pause = Math.Min(20, p.Pause + 5);
                    p.AiActions++;
                    return;
                }

                // 退让/屈服判断(随时)
                float sa = SideStrength(p, 0), sb = SideStrength(p, 1);
                if (!IsPlayerKingdom(init) && p.Escalation > 55f && sa < sb * 0.7f && MBRandom.RandomFloat < 0.25f)
                {
                    BackDown(p, init);
                    return;
                }
                if (!IsPlayerKingdom(target) && p.Escalation > 45f && sb < sa * 0.6f && MBRandom.RandomFloat < 0.3f)
                {
                    GiveIn(p, target);
                    return;
                }

                // 只有完全没有暂停时才允许 AI 动作(保证金至少每 5 天推进一次激化度)
                if (p.Pause > 0) return;
                if (p.AiActions >= 8) return;   // 每个博弈 AI 最多拖延 8 次, 防止无限续暂停

                // 发起方: 加诉求
                if (!IsPlayerKingdom(init) && p.ManeuverA >= 20f && p.Demands.Count < 4 && MBRandom.RandomFloat < 0.4f)
                {
                    var kind = MBRandom.RandomFloat < 0.5f ? GoalKind.Humiliate : (MBRandom.RandomFloat < 0.5f ? GoalKind.Tribute : GoalKind.Vassalize);
                    AddDemandBy(p, init, kind);
                    p.AiActions++;
                    return;
                }
                // 拉拢
                if (!IsPlayerKingdom(init) && p.ManeuverA >= 20f && MBRandom.RandomFloat < 0.5f)
                {
                    string r = SwayBy(p, init);
                    if (r != null && r.Length > 0 && r.IndexOf("不足") < 0 && r.IndexOf("没有") < 0) { p.AiActions++; return; }
                }
                if (!IsPlayerKingdom(target) && p.ManeuverB >= 20f && MBRandom.RandomFloat < 0.5f)
                {
                    string r2 = SwayBy(p, target);
                    if (r2 != null && r2.Length > 0 && r2.IndexOf("不足") < 0 && r2.IndexOf("没有") < 0) { p.AiActions++; return; }
                }
                // 第三国自动站队(一次一个): 我方阵营只有盟友自动参战, 其他国家必须由玩家手动拉拢
                var pk2 = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                int playerSide = pk2 == null ? -1 : (p.InitiatorId == pk2.StringId ? 0 : (p.TargetId == pk2.StringId ? 1 : -1));
                foreach (var k in Kingdom.All)
                {
                    if (k == null || k.IsEliminated || k == init || k == target) continue;
                    if (IsPlayerKingdom(k)) continue;
                    var m = p.FindMember(k.StringId);
                    if (m != null && (m.Stance == 3 || m.Stance == 4)) continue;
                    // 正在与玩家阵营(玩家或盟友)交战的国家不参与拉拢/站队
                    if (pk2 != null)
                    {
                        bool warWithPlayer = AtWarWithOurSide(pk2, k);
                        if (warWithPlayer) continue;
                    }
                    int prefA = Preference(p, k, 0);
                    int prefB = Preference(p, k, 1);
                    bool joinsA = prefA >= 55, joinsB = prefB <= -55;
                    // 玩家所在阵营: 非盟友不自动加入, 留给玩家拉拢
                    if (playerSide == 0 && joinsA && !Diplomacy.IsAlly(pk2, k)) continue;
                    if (playerSide == 1 && joinsB && !Diplomacy.IsAlly(pk2, k)) continue;
                    if (joinsA && MBRandom.RandomFloat < 0.5f) { JoinSide(k, p, 0); p.AiActions++; return; }
                    if (joinsB && MBRandom.RandomFloat < 0.5f) { JoinSide(k, p, 1); p.AiActions++; return; }
                }
            }
            catch { }
        }

        private static float SideStrength(DiploPlay p, int side)
        {
            float s = 0f;
            try
            {
                var a = K(side == 0 ? p.InitiatorId : p.TargetId);
                if (a != null) s += SafeStrength(a);
                for (int i = 0; i < p.Members.Count; i++)
                {
                    var m = p.Members[i];
                    int ms = m.Stance == 3 ? 0 : (m.Stance == 4 ? 1 : (m.Stance == 1 ? 0 : (m.Stance == 2 ? 1 : -1)));
                    if (ms != side) continue;
                    var k = K(m.KingdomId);
                    if (k != null) s += SafeStrength(k) * (m.Stance == 3 || m.Stance == 4 ? 1f : 0.4f);
                }
                // 潜在盟友(同盟国)
                foreach (var k in Kingdom.All)
                {
                    if (k == null || k.IsEliminated) continue;
                    var main = K(side == 0 ? p.InitiatorId : p.TargetId);
                    if (main == null || k == main) continue;
                    if (Diplomacy.IsAlly(k, main)) s += SafeStrength(k) * 0.7f;
                }
            }
            catch { }
            return s;
        }

        // ---- 开战 ----
        private static void DeclareWarFor(DiploPlay p)
        {
            try
            {
                p.WarDeclared = true;
                var init = K(p.InitiatorId);
                var target = K(p.TargetId);
                if (init == null || target == null) { Active.Remove(p); return; }
                var pk = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;

                // 记录待执行诉求(双向)
                RecordPending(p, init, target);
                RecordPending(p, target, init);

                // 各自阵营的国家全部两两开战
                var sideA = new List<Kingdom>();
                var sideB = new List<Kingdom>();
                sideA.Add(init); sideB.Add(target);
                for (int i = 0; i < p.Members.Count; i++)
                {
                    var m = p.Members[i];
                    var k = K(m.KingdomId);
                    if (k == null) continue;
                    if (m.Stance == 3) sideA.Add(k);
                    else if (m.Stance == 4) sideB.Add(k);
                }
                AiDiplomacy.AiActing = true;
                try
                {
                    for (int i = 0; i < sideA.Count; i++)
                    {
                        for (int j = 0; j < sideB.Count; j++)
                        {
                            try
                            {
                                if (sideA[i] != null && sideB[j] != null && !sideA[i].IsAtWarWith(sideB[j]))
                                    DeclareWarAction.ApplyByDefault(sideA[i], sideB[j]);
                            }
                            catch { }
                        }
                    }
                }
                finally { AiDiplomacy.AiActing = false; }

                if (pk != null && (init == pk || target == pk))
                    Notify("外交博弈破裂, 战争爆发! " + NameOf(init.StringId) + " vs " + NameOf(target.StringId), true);
                DLog.Force("博弈开战: " + init.StringId + " vs " + target.StringId + " 诉求=" + p.Demands.Count);
                Active.Remove(p);
            }
            catch (Exception ex) { DLog.Force("博弈开战异常: " + ex.Message); }
        }

        private static void RecordPending(DiploPlay p, Kingdom side, Kingdom enemy)
        {
            try
            {
                var key = side.StringId + "|" + enemy.StringId;
                List<WarGoal> list;
                if (!Pending.TryGetValue(key, out list)) { list = new List<WarGoal>(); Pending[key] = list; }
                for (int i = 0; i < p.Demands.Count; i++)
                {
                    var g = p.Demands[i];
                    // 诉求归属明确: 只记录由 side 提出的
                    if (g.OwnerId == side.StringId) list.Add(g);
                }
            }
            catch { }
        }

        // ---- 执行诉求 ----
        internal static void EnforceFrom(DiploPlay p, Kingdom winner, Kingdom loser, bool primaryOnly)
        {
            try
            {
                for (int i = 0; i < p.Demands.Count; i++)
                {
                    var g = p.Demands[i];
                    if (primaryOnly && g.Secondary) continue;
                    if (g.Enforced) continue;
                    if (winner == null || loser == null) continue;
                    if (g.OwnerId != winner.StringId) continue;
                    EnforceGoal(g, winner, loser);
                    g.Enforced = true;
                }
            }
            catch { }
        }

        internal static string EnforceGoal(WarGoal g, Kingdom winner, Kingdom loser)
        {
            try
            {
                if (g == null || winner == null || loser == null) return "";
                switch (g.Kind)
                {
                    case GoalKind.Conquer:
                        {
                            var s = FindSettlement(g.SettlementId);
                            if (s != null && s.MapFaction == loser)
                            {
                                var hero = winner.Leader;
                                if (hero == null && winner.Clans != null && winner.Clans.Count > 0) hero = winner.Clans[0].Leader;
                                if (hero != null)
                                {
                                    ChangeOwnerOfSettlementAction.ApplyByKingDecision(hero, s);
                                    DLog.Force("诉求执行: " + winner.StringId + " 割得 " + s.StringId);
                                    if (IsPlayerKingdom(winner) || IsPlayerKingdom(loser))
                                        Notify((IsPlayerKingdom(winner) ? "你" : NameOf(winner.StringId)) + " 割得 " + s.Name + "(条约执行)", false);
                                    return "割地";
                                }
                            }
                        }
                        break;
                    case GoalKind.Tribute:
                        {
                            int paid = PayTribute(loser, winner, g.Value > 0 ? g.Value : TributeAmount(loser, winner));
                            DLog.Force("诉求执行: " + loser.StringId + " 赔款 " + paid + " -> " + winner.StringId);
                            return "赔款 " + paid;
                        }
                    case GoalKind.Vassalize:
                        {
                            int monthly = Math.Max(500, TributeAmount(loser, winner) / 12);
                            Vassals.Add(new Vassalage { VassalId = loser.StringId, OverlordId = winner.StringId, UntilDay = Today() + 12 * 84, Monthly = monthly });
                            Diplomacy.Set(loser, winner, Math.Max(Diplomacy.Get(loser, winner), 40));
                            DLog.Force("诉求执行: " + loser.StringId + " 向 " + winner.StringId + " 称臣(月贡 " + monthly + ")");
                            return "称臣";
                        }
                    case GoalKind.Humiliate:
                        {
                            Humiliated[loser.StringId + "|" + winner.StringId] = Today() + 5 * 84;
                            Diplomacy.Change(loser, winner, -25);
                            DLog.Force("诉求执行: " + loser.StringId + " 被 " + winner.StringId + " 羞辱(5 年)");
                            return "羞辱";
                        }
                    case GoalKind.OpenMarket:
                        {
                            Markets.Add(new MarketAccess { A = winner.StringId, B = loser.StringId, UntilDay = Today() + 2 * 84 });
                            Diplomacy.Change(loser, winner, 10);
                            DLog.Force("诉求执行: " + loser.StringId + " 向 " + winner.StringId + " 开放商路(2 年)");
                            return "通商";
                        }
                }
            }
            catch (Exception ex) { DLog.Force("诉求执行失败: " + ex.Message); }
            return "";
        }

        internal static int PayTribute(Kingdom from, Kingdom to, int amount)
        {
            int paid = 0;
            try
            {
                if (IsPlayerKingdom(from))
                {
                    paid = (int)Math.Min(amount, Math.Max(0f, (float)EconomyWorld.Treasury.Gold));
                    if (paid > 0) EconomyWorld.TreasurySpend(paid);
                }
                else
                {
                    float pool = InvestmentPool.Of(from.StringId);
                    paid = (int)Math.Min(amount, Math.Max(0f, pool));
                    if (paid > 0) InvestmentPool.Add(from.StringId, -paid);
                }
                if (IsPlayerKingdom(to)) EconomyWorld.TreasuryAdd(paid);
                else if (paid > 0) InvestmentPool.Add(to.StringId, paid);
            }
            catch { }
            return paid;
        }

        // ================== 战争支持度 ==================
        private static void WarSupportDaily(int day)
        {
            try
            {
                var list = new List<string>(War.Keys);
                for (int i = 0; i < list.Count; i++)
                {
                    var w = War[list[i]];
                    var k = K(w.KingdomId);
                    if (k == null || k.IsEliminated)
                    {
                        War.Remove(list[i]);
                        continue;
                    }
                    bool atWar = false;
                    float enemyStrength = 0f, mine = SafeStrength(k);
                    int fronts = 0;
                    foreach (var x in Kingdom.All)
                    {
                        if (x == null || x == k || x.IsEliminated) continue;
                        bool w2 = false;
                        try { w2 = k.IsAtWarWith(x); } catch { }
                        if (!w2) continue;
                        atWar = true; fronts++;
                        enemyStrength += SafeStrength(x);
                    }
                    if (!atWar) { War.Remove(list[i]); continue; }
                    float drop = 0.35f;
                    if (fronts >= 3) drop += 0.2f;
                    if (enemyStrength > mine * 1.5f) drop += 0.15f;
                    w.Support -= drop;
                    if (w.Support < 0f)
                    {
                        w.Support = 0f;
                        Capitulate(k);
                        War.Remove(list[i]);
                    }
                }
            }
            catch { }
        }

        // 省份易主: 战争支持度变化
        internal static void OnSettlementChanged(Settlement s, Hero oldOwner, Hero newOwner)
        {
            try
            {
                if (s == null) return;
                string oldK = oldOwner != null && oldOwner.Clan != null && oldOwner.Clan.Kingdom != null ? oldOwner.Clan.Kingdom.StringId : null;
                string newK = newOwner != null && newOwner.Clan != null && newOwner.Clan.Kingdom != null ? newOwner.Clan.Kingdom.StringId : null;
                if (oldK != null)
                {
                    WarTrack w;
                    if (War.TryGetValue(oldK, out w)) { w.Support = Math.Max(0f, w.Support - 10f); }
                }
                if (newK != null)
                {
                    WarTrack w2;
                    if (War.TryGetValue(newK, out w2)) { w2.Support = Math.Min(100f, w2.Support + 5f); }
                }
            }
            catch { }
        }

        internal static float SupportOf(string kingdomId)
        {
            WarTrack w;
            return kingdomId != null && War.TryGetValue(kingdomId, out w) ? w.Support : 100f;
        }

        // 战争开始: 建立支持度轨道
        internal static void OnWarDeclared(Kingdom a, Kingdom b)
        {
            try
            {
                if (a != null)
                {
                    WarTrack w;
                    if (!War.TryGetValue(a.StringId, out w)) War[a.StringId] = new WarTrack { KingdomId = a.StringId, Support = 100f, StartDay = Today() };
                }
                if (b != null)
                {
                    WarTrack w;
                    if (!War.TryGetValue(b.StringId, out w)) War[b.StringId] = new WarTrack { KingdomId = b.StringId, Support = 100f, StartDay = Today() };
                }
            }
            catch { }
        }

        // 投降: 接受所有敌对诉求并停战
        internal static void Capitulate(Kingdom k)
        {
            try
            {
                if (k == null) return;
                DLog.Force("战争: " + k.StringId + " 支持度归零 -> 投降");
                AiDiplomacy.AiActing = true;
                try
                {
                    foreach (var x in Kingdom.All)
                    {
                        if (x == null || x == k || x.IsEliminated) continue;
                        bool w = false;
                        try { w = k.IsAtWarWith(x); } catch { }
                        if (!w) continue;
                        EnforcePending(x, k, true);
                        try { MakePeaceAction.Apply(k, x); } catch { }
                    }
                }
                finally { AiDiplomacy.AiActing = false; }
                if (IsPlayerKingdom(k)) Notify("你的国家崩溃投降: 敌对诉求全部生效", true);
            }
            catch { }
        }

        // ================== 停战: 执行待结算诉求 ==================
        internal static void OnPeace(Kingdom a, Kingdom b)
        {
            try
            {
                if (a == null || b == null) return;
                // 比较双方战时状态, 强者执行自己的诉求
                float sa = SafeStrength(a) * (SupportOf(a.StringId) / 100f);
                float sb = SafeStrength(b) * (SupportOf(b.StringId) / 100f);
                var winner = sa >= sb ? a : b;
                var loser = sa >= sb ? b : a;
                EnforcePending(winner, loser, false);
                SettlePromises(winner, loser);   // 拉拢承诺战后兑现
                // 停战提升支持度
                WarTrack w;
                if (War.TryGetValue(a.StringId, out w)) { w.Support = Math.Min(100f, w.Support + 15f); }
                if (War.TryGetValue(b.StringId, out w)) { w.Support = Math.Min(100f, w.Support + 15f); }
            }
            catch { }
        }

        // winner 对 loser 的待执行诉求
        private static void EnforcePending(Kingdom winner, Kingdom loser, bool all)
        {
            try
            {
                string key = winner.StringId + "|" + loser.StringId;
                List<WarGoal> list;
                if (!Pending.TryGetValue(key, out list)) return;
                for (int i = 0; i < list.Count; i++)
                {
                    var g = list[i];
                    if (g.Enforced) continue;
                    if (g.Secondary && !all) continue;   // 非投降时只执行主要诉求
                    EnforceGoal(g, winner, loser);
                    g.Enforced = true;
                }
                Pending.Remove(key);
            }
            catch { }
        }

        // ================== 附庸/通商 月结 ==================
        private static void VassalMonthly()
        {
            try
            {
                for (int i = Vassals.Count - 1; i >= 0; i--)
                {
                    var v = Vassals[i];
                    if (Today() >= v.UntilDay) { Vassals.RemoveAt(i); continue; }
                    var vk = K(v.VassalId);
                    var ok = K(v.OverlordId);
                    if (vk == null || ok == null || vk.IsEliminated) { Vassals.RemoveAt(i); continue; }
                    PayTribute(vk, ok, v.Monthly);
                }
                for (int i = Markets.Count - 1; i >= 0; i--)
                    if (Today() >= Markets[i].UntilDay) Markets.RemoveAt(i);
            }
            catch { }
        }

        // 通商加成(供贸易容量用; 与目标国签有通商: +25%)
        internal static float MarketBonusOf(string kingdomId)
        {
            try
            {
                float b = 0f;
                for (int i = 0; i < Markets.Count; i++)
                    if (Markets[i].A == kingdomId || Markets[i].B == kingdomId) b += 0.25f;
                return b;
            }
            catch { return 0f; }
        }

        // 附庸国不能对宗主宣战
        internal static bool CannotDeclareWarOn(string fromId, string toId)
        {
            try
            {
                return IsVassalOf(fromId, toId) || IsHumiliatedBy(fromId, toId);
            }
            catch { return false; }
        }

        // ================= 拉拢条件(参照 V3) =================
        internal const float SwayCost = 20f;

        internal static string SwayOfferName(SwayKind k)
        {
            switch (k)
            {
                case SwayKind.Obligation: return "提供义务";
                case SwayKind.TributeShare: return "承诺战后赔款分成";
                case SwayKind.OpenMarket: return "承诺开放商路";
                case SwayKind.Land: return "承诺割让征服地";
                case SwayKind.Puppet: return "承诺扶植其为附庸";
                default: return "当场支付佣金";
            }
        }

        internal static int SwayOfferValue(SwayKind k)
        {
            switch (k)
            {
                case SwayKind.Obligation: return 15;
                case SwayKind.TributeShare: return 25;
                case SwayKind.OpenMarket: return 20;
                case SwayKind.Land: return 40;
                case SwayKind.Puppet: return 50;
                default: return 30;
            }
        }

        internal static string SwayOfferDesc(SwayKind k)
        {
            switch (k)
            {
                case SwayKind.Obligation: return "欠他们一次人情(关系+10)";
                case SwayKind.TributeShare: return "战胜后把三成赔款分给他们";
                case SwayKind.OpenMarket: return "战后双方通商 2 年";
                case SwayKind.Land: return "若征服到 2 块地, 分他们一块";
                case SwayKind.Puppet: return "打败对方后让他当他们的附庸(仅发起方可许)";
                default: return "立刻支付 1500 第纳尔佣金";
            }
        }

        internal static bool SwayOfferValid(DiploPlay p, Kingdom by, SwayKind k, out string why)
        {
            why = "";
            try
            {
                if (p == null || by == null) { return false; }
                if (k == SwayKind.Puppet && p.InitiatorId != by.StringId) { why = "只有发起方能把战败国许给别人"; return false; }
                if (k == SwayKind.Gold && !IsPlayerKingdom(by))
                {
                    if (InvestmentPool.Of(by.StringId) < 1500f) { why = "国库不足 1500"; return false; }
                }
                else if (k == SwayKind.Gold && EconomyWorld.Treasury.Gold < 1500) { why = "国库不足 1500"; return false; }
                if (k == SwayKind.Land)
                {
                    bool hasConquer = false;
                    for (int i = 0; i < p.Demands.Count; i++)
                        if (p.Demands[i].Kind == GoalKind.Conquer && p.Demands[i].OwnerId == by.StringId) { hasConquer = true; break; }
                    if (!hasConquer) { why = "你还没有割地诉求可许诺"; return false; }
                }
                return true;
            }
            catch { return false; }
        }

        // 玩家向第三国提出拉拢条件
        internal static string PlayerOfferSway(string kingdomId, SwayKind kind)
        {
            try
            {
                var p = PlayerPlay();
                if (p == null) return "没有你参与的博弈";
                var pk = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                if (pk == null) return "没有国家";
                bool isInit = p.InitiatorId == pk.StringId;
                if (!isInit && p.TargetId != pk.StringId) return "你不是博弈主角";
                if (p.Escalation >= 80f) return "已进入开战倒计时, 不能再拉拢";
                var m = p.FindMember(kingdomId);
                if (m == null) m = p.GetOrCreate(kingdomId);
                if (m.KingdomId == pk.StringId || m.KingdomId == p.InitiatorId || m.KingdomId == p.TargetId) return "对方是博弈主角";
                if (m.Stance == 3 || m.Stance == 4) return "对方已参战";
                float maneuvers = isInit ? p.ManeuverA : p.ManeuverB;
                if (maneuvers < SwayCost) return "机动点不足(需 20)";
                string why;
                if (!SwayOfferValid(p, pk, kind, out why)) return why;
                var who = K(kingdomId);
                if (who == null) return "国家不存在";
                if (AtWarWithOurSide(pk, who)) return NameOf(kingdomId) + " 正在与我国或我国盟友交战, 无法拉拢";

                if (isInit) p.ManeuverA -= SwayCost; else p.ManeuverB -= SwayCost;
                p.Pause = Math.Min(20, p.Pause + 5);

                int pref = Preference(p, who, isInit ? 0 : 1);
                int acceptance = pref + SwayOfferValue(kind);
                if (acceptance < 30)
                {
                    Diplomacy.Change(pk, who, 3);
                    return NameOf(kingdomId) + " 拒绝了(好感 " + acceptance + "/30)";
                }

                var enemy = K(isInit ? p.TargetId : p.InitiatorId);
                m.SwayBy = isInit ? 1 : 2;
                m.Stance = isInit ? 3 : 4;
                m.Side = isInit ? 0 : 1;
                if (kind == SwayKind.Gold) PayTribute(pk, who, 1500);
                Promises.Add(new SwayPromise { ById = pk.StringId, ToId = kingdomId, EnemyId = enemy != null ? enemy.StringId : null, Kind = kind, Day = Today() });
                Notify("拉拢成功: " + NameOf(kingdomId) + " 加入我们(" + SwayOfferName(kind) + ")", false);
                DLog.Force("博弈拉拢: " + pk.StringId + " 用 " + kind + " 换 " + kingdomId);
                return "已拉拢 " + NameOf(kingdomId) + "(条件: " + SwayOfferName(kind) + ")";
            }
            catch { return "拉拢失败"; }
        }

        // 战后兑现承诺(胜方对败方停战时调用)
        private static void SettlePromises(Kingdom winner, Kingdom loser)
        {
            try
            {
                if (winner == null || loser == null) return;
                int tributeSum = 0;
                string pk2 = winner.StringId + "|" + loser.StringId;
                List<WarGoal> gl;
                if (Pending.TryGetValue(pk2, out gl))
                    for (int i = 0; i < gl.Count; i++)
                    {
                        var g = gl[i];
                        if (g.Enforced && (g.Kind == GoalKind.Tribute || g.Kind == GoalKind.Vassalize)) tributeSum += Math.Max(0, g.Value);
                    }

                for (int i = 0; i < Promises.Count; i++)
                {
                    var pr = Promises[i];
                    if (pr.Settled || pr.ById != winner.StringId || pr.EnemyId != loser.StringId) continue;
                    var helper = K(pr.ToId);
                    pr.Settled = true;
                    if (helper == null || helper.IsEliminated) continue;
                    switch (pr.Kind)
                    {
                        case SwayKind.Obligation:
                            Diplomacy.Change(winner, helper, 10);
                            break;
                        case SwayKind.TributeShare:
                            {
                                int share = (int)(tributeSum * 0.3f);
                                if (share > 0) PayTribute(loser, helper, share);
                            }
                            break;
                        case SwayKind.OpenMarket:
                            Markets.Add(new MarketAccess { A = winner.StringId, B = helper.StringId, UntilDay = Today() + 2 * 84 });
                            break;
                        case SwayKind.Land:
                            {
                                Settlement give = null;
                                if (gl != null)
                                {
                                    int cnt = 0;
                                    for (int j = 0; j < gl.Count; j++)
                                        if (gl[j].Enforced && gl[j].Kind == GoalKind.Conquer)
                                        {
                                            cnt++;
                                            var st = FindSettlement(gl[j].SettlementId);
                                            if (st != null && st.MapFaction == winner) give = st;
                                        }
                                    if (cnt < 2) give = null;
                                }
                                if (give != null)
                                {
                                    var hero = helper.Leader;
                                    if (hero == null && helper.Clans != null && helper.Clans.Count > 0) hero = helper.Clans[0].Leader;
                                    if (hero != null)
                                    {
                                        try { ChangeOwnerOfSettlementAction.ApplyByKingDecision(hero, give); } catch { }
                                        Notify("承诺兑现: " + NameOf(helper.StringId) + " 获得 " + give.Name, false);
                                    }
                                }
                                else Diplomacy.Change(winner, helper, -15);
                            }
                            break;
                        case SwayKind.Puppet:
                            {
                                bool vassalized = false;
                                if (gl != null)
                                    for (int j = 0; j < gl.Count; j++)
                                        if (gl[j].Enforced && gl[j].Kind == GoalKind.Vassalize) { vassalized = true; break; }
                                if (vassalized)
                                {
                                    int monthly = Math.Max(500, TributeAmount(loser, helper) / 12);
                                    Vassals.Add(new Vassalage { VassalId = loser.StringId, OverlordId = helper.StringId, UntilDay = Today() + 12 * 84, Monthly = monthly });
                                    Notify("承诺兑现: " + NameOf(loser.StringId) + " 成为 " + NameOf(helper.StringId) + " 的附庸", false);
                                }
                                else Diplomacy.Change(winner, helper, -15);
                            }
                            break;
                    }
                }
            }
            catch (Exception ex) { DLog.Force("承诺兑现异常: " + ex.Message); }
        }
        // 是否与"我方阵营"(玩家+盟友)处于交战——这类国家不能被玩家拉拢
        internal static bool AtWarWithOurSide(Kingdom pk, Kingdom who)
        {
            try
            {
                if (pk == null || who == null) return false;
                if (who.IsAtWarWith(pk)) return true;
                foreach (var a in Kingdom.All)
                {
                    if (a == null || a == pk || a.IsEliminated) continue;
                    if (!Diplomacy.IsAlly(pk, a)) continue;
                    bool w = false;
                    try { w = who.IsAtWarWith(a); } catch { }
                    if (w) return true;
                }
            }
            catch { }
            return false;
        }

        // 通知
        private static void Notify(string msg, bool alert)
        {
            try
            {
                var c = TaleWorlds.Library.Color.FromUint(alert ? 4294901760U : 4294953344U);
                InformationManager.DisplayMessage(new InformationMessage(msg, c));
            }
            catch { }
        }

        // ================= 玩家发起: 统一入口(按用户规则) =================
        // ① 已在交战 -> 无事; ② 目标正与盟友开战 -> 跳过博弈直接参战;
        // ③ 目标正处博弈且我方盟友是主角之一 -> 自动加入盟友一方; ④ 否则开新博弈(玩家自选诉求)
        internal static DiploPlay PlayerDeclareOn(Kingdom target, out string message)
        {
            message = "";
            try
            {
                var pk = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                if (pk == null || target == null || pk == target) { message = "无法操作"; return null; }
                if (pk.IsAtWarWith(target)) { message = "你们已经在交战"; return null; }

                // 盟友正在与目标交战 -> 直接开战(绕过博弈)
                bool allyAtWar = false;
                foreach (var k in Kingdom.All)
                {
                    if (k == null || k == pk || k.IsEliminated) continue;
                    if (!Diplomacy.IsAlly(pk, k)) continue;
                    bool w = false;
                    try { w = k.IsAtWarWith(target); } catch { }
                    if (w) { allyAtWar = true; break; }
                }
                if (allyAtWar)
                {
                    DiplomacyBehavior.AllyForcingWar = true;
                    try { DeclareWarAction.ApplyByDefault(pk, target); }
                    finally { DiplomacyBehavior.AllyForcingWar = false; }
                    message = "盟友正在与 " + NameOf(target.StringId) + " 交战: 已直接参战(跳过博弈)";
                    return null;
                }

                // 目标已在博弈中
                var existing = PlayOf(target.StringId);
                if (existing != null)
                {
                    if (existing.Involves(pk.StringId)) { message = "你已在这场博弈中"; return null; }
                    int side = -1;
                    if (Diplomacy.IsAlly(pk, K(existing.InitiatorId))) side = 0;
                    else if (Diplomacy.IsAlly(pk, K(existing.TargetId))) side = 1;
                    if (side >= 0)
                    {
                        message = JoinSide(pk, existing, side);
                        return null;
                    }
                    message = "对方正处于另一场博弈中, 无法另开新博弈";
                    return null;
                }

                var play = StartPlay(pk, target, true);
                if (play != null) { message = ""; return play; }
                message = "无法发起博弈(状态不允许)";
                return null;
            }
            catch (Exception ex) { message = "发起失败: " + ex.Message; return null; }
        }

        // ================== 玩家操作入口(UI) ==================
        internal static string PlayerAddGoal(GoalKind kind)
        {
            try
            {
                var p = PlayerPlay();
                if (p == null) return "没有你参与的博弈";
                var pk = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                return AddDemandBy(p, pk, kind);
            }
            catch { return "加诉求失败"; }
        }

        // 指定目标的玩家加诉求(割地选点)
        internal static string PlayerAddGoalAt(GoalKind kind, string sid)
        {
            try
            {
                var p = PlayerPlay();
                if (p == null) return "没有你参与的博弈";
                var pk = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                return AddDemandAt(p, pk, kind, sid);
            }
            catch { return "加诉求失败"; }
        }

        // 玩家自定义金额(赔款/纳贡)
        internal static string PlayerAddGoalAmount(GoalKind kind, int amount)
        {
            try
            {
                var p = PlayerPlay();
                if (p == null) return "没有你参与的博弈";
                var pk = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                if (pk == null) return "没有国家";
                if (amount < 500) amount = 500;
                if (amount > 30000) amount = 30000;
                return AddDemandAt(p, pk, kind, null, amount);
            }
            catch { return "加诉求失败"; }
        }

        // 默认金额(供 UI 初始化)
        internal static int DefaultAmount(GoalKind kind)
        {
            try
            {
                var p = PlayerPlay();
                if (p == null) return 3000;
                var pk = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                var enemy = pk != null && p.InitiatorId == pk.StringId ? K(p.TargetId) : K(p.InitiatorId);
                return TributeAmount(enemy, pk);
            }
            catch { return 3000; }
        }

        internal static string PlayerBackDown()
        {
            try
            {
                var p = PlayerPlay();
                var pk = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                return BackDown(p, pk);
            }
            catch { return "退让失败"; }
        }

        internal static string PlayerGiveIn()
        {
            try
            {
                var p = PlayerPlay();
                var pk = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                return GiveIn(p, pk);
            }
            catch { return "屈服失败"; }
        }

        internal static string PlayerJoin(int side)
        {
            try
            {
                var p = PlayerPlay();
                var pk = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                return JoinSide(pk, p, side);
            }
            catch { return "加入失败"; }
        }

        internal static string PlayerDecline()
        {
            try
            {
                var p = PlayerPlay();
                var pk = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                if (p == null || pk == null) return "没有你参与的博弈";
                var m = p.GetOrCreate(pk.StringId);
                m.Stance = 5;
                Notify("你拒绝了这次的站队请求", false);
                return "已保持中立退出博弈";
            }
            catch { return "操作失败"; }
        }

        // ================== 存档 FIA_DiploPlay ==================
        internal static string Save()
        {
            try
            {
                var sb = new StringBuilder("v1;");
                for (int i = 0; i < Active.Count; i++)
                {
                    var p = Active[i];
                    if (i > 0) sb.Append('|');
                    sb.Append(p.Id).Append(',').Append(p.InitiatorId).Append(',').Append(p.TargetId).Append(',')
                      .Append(p.StartDay).Append(',').Append(p.Escalation.ToString("F1", CultureInfo.InvariantCulture)).Append(',')
                      .Append(p.Pause).Append(',').Append(p.ManeuverA.ToString("F0")).Append(',').Append(p.ManeuverB.ToString("F0")).Append(',')
                      .Append(p.WarDeclared ? 1 : 0).Append(',')
                      .Append(GoalsToText(p)).Append(',').Append(MembersToText(p));
                }
                sb.Append(';');
                foreach (var kv in Infamy) sb.Append(kv.Key).Append(',').Append(kv.Value.ToString("F1", CultureInfo.InvariantCulture)).Append(';');
                sb.Append('#');
                foreach (var kv in War) sb.Append(kv.Key).Append(',').Append(kv.Value.Support.ToString("F1", CultureInfo.InvariantCulture)).Append(',').Append(kv.Value.StartDay).Append(';');
                sb.Append('#');
                for (int i = 0; i < Vassals.Count; i++)
                {
                    var v = Vassals[i];
                    sb.Append(v.VassalId).Append(',').Append(v.OverlordId).Append(',').Append(v.UntilDay).Append(',').Append(v.Monthly).Append(';');
                }
                sb.Append('#');
                for (int i = 0; i < Markets.Count; i++)
                {
                    var m = Markets[i];
                    sb.Append(m.A).Append(',').Append(m.B).Append(',').Append(m.UntilDay).Append(';');
                }
                sb.Append('#');
                foreach (var kv in Humiliated) sb.Append(kv.Key).Append(',').Append(kv.Value).Append(';');
                sb.Append('#');
                foreach (var kv in Pending)
                {
                    sb.Append(kv.Key).Append(',');
                    for (int i = 0; i < kv.Value.Count; i++)
                    {
                        var g = kv.Value[i];
                        if (i > 0) sb.Append('+');
                        sb.Append((int)g.Kind).Append('~').Append(g.SettlementId ?? "").Append('~').Append(g.Secondary ? 1 : 0).Append('~').Append(g.Value).Append('~').Append(g.Enforced ? 1 : 0).Append('~').Append(g.OwnerId ?? "");
                    }
                    sb.Append(';');
                }
                sb.Append('#');
                for (int i = 0; i < Promises.Count; i++)
                {
                    var pr = Promises[i];
                    sb.Append(pr.ById ?? "").Append(',').Append(pr.ToId ?? "").Append(',').Append(pr.EnemyId ?? "").Append(',')
                      .Append((int)pr.Kind).Append(',').Append(pr.Day).Append(',').Append(pr.Settled ? 1 : 0).Append(';');
                }
                return sb.ToString();
            }
            catch { return "v1;"; }
        }

        private static string GoalsToText(DiploPlay p)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < p.Demands.Count; i++)
            {
                var g = p.Demands[i];
                if (i > 0) sb.Append('+');
                sb.Append((int)g.Kind).Append('~').Append(g.SettlementId ?? "").Append('~').Append(g.Secondary ? 1 : 0).Append('~').Append(g.Value).Append('~').Append(g.Enforced ? 1 : 0).Append('~').Append(g.OwnerId ?? "");
            }
            return sb.ToString();
        }

        private static string MembersToText(DiploPlay p)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < p.Members.Count; i++)
            {
                var m = p.Members[i];
                if (i > 0) sb.Append('+');
                sb.Append(m.KingdomId).Append('~').Append(m.Side).Append('~').Append(m.Stance).Append('~').Append(m.SwayBy);
            }
            return sb.ToString();
        }

        internal static void Load(string data)
        {
            try
            {
                Active.Clear();
                Infamy.Clear();
                War.Clear();
                Vassals.Clear();
                Markets.Clear();
                Humiliated.Clear();
                Pending.Clear();
                if (string.IsNullOrEmpty(data)) return;
                var seg = data.Split('#');
                // 段0: v1;plays
                var head = seg[0].Split(';');
                if (head.Length > 1 && !string.IsNullOrEmpty(head[1]))
                {
                    foreach (var ptxt in head[1].Split('|'))
                    {
                        if (string.IsNullOrEmpty(ptxt)) continue;
                        var f = ptxt.Split(',');
                        if (f.Length < 11) continue;
                        var p = new DiploPlay { Id = PI(f[0]), InitiatorId = f[1], TargetId = f[2], StartDay = PI(f[3]) };
                        float esc; float.TryParse(f[4], NumberStyles.Float, CultureInfo.InvariantCulture, out esc);
                        p.Escalation = esc;
                        p.Pause = PI(f[5]);
                        p.ManeuverA = PF(f[6]); p.ManeuverB = PF(f[7]);
                        p.WarDeclared = f[8] == "1";
                        ParseGoals(p, f[9]);
                        ParseMembers(p, f[10]);
                        Active.Add(p);
                        if (p.Id >= _nextId) _nextId = p.Id + 1;
                    }
                    // 段0 后半: 恶名
                    if (head.Length > 2)
                    {
                        foreach (var it in head[2].Split(';'))
                        {
                            if (string.IsNullOrEmpty(it)) continue;
                            var f = it.Split(',');
                            if (f.Length < 2) continue;
                            Infamy[f[0]] = PF(f[1]);
                        }
                    }
                }
                if (seg.Length > 1)
                {
                    foreach (var it in seg[1].Split(';'))
                    {
                        if (string.IsNullOrEmpty(it)) continue;
                        var f = it.Split(',');
                        if (f.Length < 3) continue;
                        War[f[0]] = new WarTrack { KingdomId = f[0], Support = PF(f[1]), StartDay = PI(f[2]) };
                    }
                }
                if (seg.Length > 2)
                {
                    foreach (var it in seg[2].Split(';'))
                    {
                        if (string.IsNullOrEmpty(it)) continue;
                        var f = it.Split(',');
                        if (f.Length < 4) continue;
                        Vassals.Add(new Vassalage { VassalId = f[0], OverlordId = f[1], UntilDay = PI(f[2]), Monthly = PI(f[3]) });
                    }
                }
                if (seg.Length > 3)
                {
                    foreach (var it in seg[3].Split(';'))
                    {
                        if (string.IsNullOrEmpty(it)) continue;
                        var f = it.Split(',');
                        if (f.Length < 3) continue;
                        Markets.Add(new MarketAccess { A = f[0], B = f[1], UntilDay = PI(f[2]) });
                    }
                }
                if (seg.Length > 4)
                {
                    foreach (var it in seg[4].Split(';'))
                    {
                        if (string.IsNullOrEmpty(it)) continue;
                        var f = it.Split(',');
                        if (f.Length < 2) continue;
                        Humiliated[f[0]] = PI(f[1]);
                    }
                }
                if (seg.Length > 5)
                {
                    foreach (var it in seg[5].Split(';'))
                    {
                        if (string.IsNullOrEmpty(it)) continue;
                        int idx = it.IndexOf(',');
                        if (idx <= 0) continue;
                        string key = it.Substring(0, idx);
                        var list = new List<WarGoal>();
                        foreach (var gt in it.Substring(idx + 1).Split('+'))
                        {
                            if (string.IsNullOrEmpty(gt)) continue;
                            var g = ParseGoal(gt);
                            if (g != null) list.Add(g);
                        }
                        Pending[key] = list;
                    }
                }
                if (seg.Length > 6)
                {
                    foreach (var it in seg[6].Split(';'))
                    {
                        if (string.IsNullOrEmpty(it)) continue;
                        var f = it.Split(',');
                        if (f.Length < 6) continue;
                        Promises.Add(new SwayPromise
                        {
                            ById = f[0],
                            ToId = f[1],
                            EnemyId = f[2],
                            Kind = (SwayKind)PI(f[3]),
                            Day = PI(f[4]),
                            Settled = f[5] == "1"
                        });
                    }
                }
                DLog.Force("外交博弈: 读档 博弈=" + Active.Count + " 恶名国=" + Infamy.Count + " 战时支持=" + War.Count + " 附庸=" + Vassals.Count);
            }
            catch (Exception ex) { DLog.Force("博弈读档失败: " + ex.Message); }
        }

        private static void ParseGoals(DiploPlay p, string txt)
        {
            if (string.IsNullOrEmpty(txt)) return;
            foreach (var gt in txt.Split('+'))
            {
                var g = ParseGoal(gt);
                if (g != null) p.Demands.Add(g);
            }
        }

        private static WarGoal ParseGoal(string gt)
        {
            try
            {
                var f = gt.Split('~');
                if (f.Length < 4) return null;
                var g = new WarGoal { Kind = (GoalKind)PI(f[0]), SettlementId = string.IsNullOrEmpty(f[1]) ? null : f[1], Secondary = f[2] == "1", Value = PI(f[3]) };
                if (f.Length > 4) g.Enforced = f[4] == "1";
                if (f.Length > 5) g.OwnerId = string.IsNullOrEmpty(f[5]) ? null : f[5];
                return g;
            }
            catch { return null; }
        }

        private static void ParseMembers(DiploPlay p, string txt)
        {
            if (string.IsNullOrEmpty(txt)) return;
            foreach (var mt in txt.Split('+'))
            {
                var f = mt.Split('~');
                if (f.Length < 4) continue;
                p.Members.Add(new PlayMember { KingdomId = f[0], Side = PI(f[1]), Stance = PI(f[2]), SwayBy = PI(f[3]) });
            }
        }

        internal static Settlement FindSettlement(string id)
        {
            try
            {
                if (string.IsNullOrEmpty(id)) return null;
                foreach (var s in Settlement.All) if (s != null && s.StringId == id) return s;
            }
            catch { }
            return null;
        }

        private static int PI(string s) { int v; return int.TryParse(s, out v) ? v : 0; }
        private static float PF(string s) { float v; return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : 0f; }

        // ================== UI 辅助 ==================
        internal static string PhaseText(DiploPlay p)
        {
            try
            {
                if (p == null) return "";
                if (p.Escalation < 20f) return "落子阶段";
                if (p.Escalation < 80f) return "拉拢阶段";
                return "开战倒计时";
            }
            catch { return ""; }
        }

        internal static string DemandText(WarGoal g)
        {
            try
            {
                string who = NameOf(g.OwnerId);
                string s = (g.Secondary ? "[次] " : "[主] ") + who + ": ";
                switch (g.Kind)
                {
                    case GoalKind.Conquer:
                        {
                            var st = FindSettlement(g.SettlementId);
                            s += "要求割让 " + (st != null && st.Name != null ? st.Name.ToString() : "?") + " (给" + who + ")";
                        }
                        break;
                    case GoalKind.Tribute:
                        s += "要求赔款 " + g.Value.ToString("N0");
                        break;
                    case GoalKind.Vassalize:
                        s += "要求称臣纳贡(月贡 " + g.Value.ToString("N0") + ")";
                        break;
                    case GoalKind.Humiliate:
                        s += "要求羞辱对方(5 年不得报复)";
                        break;
                    default:
                        s += "要求开放商路(2 年, 贸易 +25%)";
                        break;
                }
                if (g.Enforced) s += " ✓已执行";
                return s;
            }
            catch { return ""; }
        }

        internal static string MemberStatus(PlayMember m)
        {
            switch (m.Stance)
            {
                case 3: return "已加入发起方";
                case 4: return "已加入防御方";
                case 1: return "倾向发起方";
                case 2: return "倾向防御方";
                case 5: return "拒绝";
                default: return "中立";
            }
        }
    }
}
