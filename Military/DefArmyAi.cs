using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;

namespace FeudalInternalAffairs
{
    // v4.96: 国防军 AI 抑制(用户: 没领主部队听话, 走着走着去打劫匪)
    //   v4.95 起有"手动驾驶"兜底(CommandTimeout 0.5 秒未动即直接推进位置),
    //   因此可以安全屏蔽原版 AI 决策: 军团不再自主选目标(不会跑去打劫匪/追商队), 移动由命令+兜底驱动。
    //   注意: 只屏蔽决策(TickInternal), 战斗/地图事件等原版机制不受影响。
    internal static class DefArmyAi
    {
        private static readonly FieldInfo PartyField = AccessTools.Field(typeof(MobilePartyAi), "_mobileParty");

        // v4.97: AI 抑制 —— 只屏蔽"决策"(TickInternal), 不能屏蔽 Tick 整体
        //   (实测屏蔽 Tick 后连移动都不执行了: Tick 里有驱动移动所需的环节)
        [HarmonyPatch(typeof(MobilePartyAi), "TickInternal")]
        internal static class AiGuard
        {
            private static bool Prefix(MobilePartyAi __instance)
            {
                try
                {
                    var p = PartyField != null ? PartyField.GetValue(__instance) as MobileParty : null;
                    if (p != null && DefArmy.IsDefArmyParty(p)) return false;   // 国防军不参与原版 AI 决策
                }
                catch { }
                return true;
            }
        }

        // v4.97: 国防军不参与原版自动招募(用户: 他竟然会征兵)
        //   原版 RecruitmentCampaignBehavior 每日对 AI 领主部队在定居点自动招募志愿兵;
        //   我们的军团名义上是"玩家家族的领主部队", 会被原版当 AI 部队招募 -> 屏蔽。
        [HarmonyPatch(typeof(RecruitmentCampaignBehavior), "CheckRecruiting")]
        internal static class NoAutoRecruit
        {
            private static bool Prefix(MobileParty mobileParty)
            {
                try
                {
                    if (mobileParty != null && DefArmy.IsDefArmyParty(mobileParty)) return false;
                }
                catch { }
                return true;
            }
        }

        // ================= v4.21x: AI 部队铁路运输 =================
        //   战时长途行军 -> ArmyDoctrine.SetRailTransport(party, true); 和平/任务结束 -> 关闭;
        //   欠费由 ArmyDoctrine.DailyRailTransport 自动停用(不重复处理)。只碰 AI 王国, 玩家部队由玩家 UI 控制。
        internal static class RailTransportAi
        {
            private static int _lastDay = -1;

            internal static void EnableForMarch(MobileParty p, Vec2 targetPos)
            {
                try
                {
                    if (p == null || !p.IsActive || p.IsMainParty) return;
                    if (ArmyDoctrine.IsRailTransport(p)) return;
                    var k = p.MapFaction as Kingdom;
                    if (k == null || k.IsEliminated) return;
                    var pk = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                    if (pk != null && ReferenceEquals(pk, k)) return;                 // 玩家部队走 UI
                    float d = p.Position.ToVec2().Distance(targetPos);
                    if (d < 150f) return;                                             // 长途才值得开
                    bool war = false;
                    try { foreach (var x in Kingdom.All) { if (x == null || x.IsEliminated || ReferenceEquals(x, k)) continue; if (k.IsAtWarWith(x)) { war = true; break; } } } catch { }
                    if (!war) return;
                    if (WarEconomy.GoldOfPublic(k) < 1500f) return;                   // 国库太薄先不开
                    ArmyDoctrine.SetRailTransport(p, true);
                }
                catch { }
            }

            internal static void Disable(MobileParty p)
            {
                try { if (p != null && ArmyDoctrine.IsRailTransport(p)) ArmyDoctrine.SetRailTransport(p, false); }
                catch { }
            }

            // 月度扫尾: 和平的 AI 王国关闭全部铁路运输(欠费停用由 ArmyDoctrine 日结负责)
            internal static void Sweep(int day)
            {
                try
                {
                    if (day < _lastDay) _lastDay = -1;   // 新档/读档 -> 重置
                    if (day == _lastDay) return;
                    _lastDay = day;
                    var pk = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                    foreach (var p in MobileParty.All)
                    {
                        if (p == null || !p.IsActive || p.IsMainParty) continue;
                        if (!ArmyDoctrine.IsRailTransport(p)) continue;
                        var k = p.MapFaction as Kingdom;
                        if (k == null || (pk != null && ReferenceEquals(pk, k))) continue;   // 玩家部队不动
                        bool war = false;
                        try { foreach (var x in Kingdom.All) { if (x == null || x.IsEliminated || ReferenceEquals(x, k)) continue; if (k.IsAtWarWith(x)) { war = true; break; } } } catch { }
                        if (!war) Disable(p);
                    }
                }
                catch { }
            }
        }
    }

    // ================= v5.x: 军事总监(统一大脑接线) =================
    //   把"玩家级"打法接到 AiDirector(统一大脑):
    //   ① 军力评估: 本国机动兵力(军团按组织度/士气加权) vs 目标国机动+驻军; ≥1.2 才主动进攻
    //   ② 轮换: 组织度<50 / 兵力<编制60% / 士气<40 / 粮秣<20 -> 撤回最近本国城休整补员, 健康军团接替
    //   ③ 围城拖垮: 围城 ≥21 天且敌守军未被消耗 -> 撤围
    //   ④ 铁路投送: Railways.LoadArmyMilitary(运力/冷却由它校验, 失败跳过), 投送后开铁路运输
    //   ⑤ 和谈: 厌战/劣势/目标达成 -> 玩家王国走 DiplomacyBehavior.PlayerMakePeace(领主表决), AI 王国走 AiDiplomacy.MakeAiPeace
    //   ⑥ 动员: WarMobilization.AutoDecide 按 Goal/Threat 自动动员复员
    //   周结决策(骑砍 1 月 = 7 天); 每国每月最多 2 条 "AI 军事" 日志; 全部 try/catch; 玩家正在指挥/选中的部队不碰。
    internal static class AiWarDirector
    {
        internal const float AttackRatio = 1.2f;      // 主动进攻阈值(兵力优势)
        internal const float RetreatRatio = 0.83f;    // 劣势收缩阈值(≈1/1.2)
        internal const float RestOrg = 50f;           // 组织度低于此 -> 休整
        internal const float RestMorale = 40f;        // 士气低于此 -> 休整
        internal const float RestMenFrac = 0.6f;      // 兵力低于编制 60% -> 休整
        internal const float LowFood = 20f;           // 粮秣低于此 -> 回城
        internal const int SiegeDays = 21;            // 围城无进展天数上限
        internal const int DispatchCooldown = 28;     // 同一部队军列投送冷却(天)
        private const int LegionMinAttack = 100;      // 低于此兵力的军团不进攻

        private static int _lastWeek = -1;
        private static int _logBucket = -1;
        private static int _powerDay = -1;
        private static readonly Dictionary<string, int> _logCount = new Dictionary<string, int>();
        private static readonly Dictionary<string, float> _mobilePower = new Dictionary<string, float>();
        private static readonly Dictionary<string, int> _railDay = new Dictionary<string, int>();
        private static readonly Dictionary<string, int> _siegeStart = new Dictionary<string, int>();
        private static readonly Dictionary<string, float> _siegeEnemy = new Dictionary<string, float>();
        private static readonly Dictionary<string, int> _peaceAsked = new Dictionary<string, int>();

        internal static Kingdom PlayerKingdom()
        {
            try { return NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null; }
            catch { return null; }
        }

        // 周结入口(DefArmy.Daily 调用)
        internal static void Daily(int day)
        {
            try
            {
                if (CampaignTime.Now.GetDayOfWeek != 0) return;   // 周结(骑砍 1 月 = 7 天)
                if (day == _lastWeek) return;
                _lastWeek = day;
                int bucket = day / 7;   // 骑砍 1 月 = 7 天(与 AiAgenda 月键一致)
                if (bucket != _logBucket) { _logBucket = bucket; _logCount.Clear(); }   // 每国每月 2 条日志配额
                _powerDay = -1;
                var pk = PlayerKingdom();
                try { LegionWeek(pk, day); } catch { }
                try { PeaceWeek(pk, day); } catch { }
                try { RailWeek(day); } catch { }
            }
            catch (Exception ex) { try { DLog.Force("AI 军事: 周结异常 " + ex.Message); } catch { } }
        }

        internal static void Reset()
        {
            _lastWeek = -1; _logBucket = -1; _powerDay = -1;
            _logCount.Clear(); _mobilePower.Clear();
            _railDay.Clear(); _siegeStart.Clear(); _siegeEnemy.Clear(); _peaceAsked.Clear();
        }

        // 每国每月最多两条 "AI 军事: ..." 日志
        internal static void Log(Kingdom k, string msg)
        {
            try
            {
                if (k == null) return;
                int n;
                _logCount.TryGetValue(k.StringId, out n);
                if (n >= 2) return;
                _logCount[k.StringId] = n + 1;
                DLog.Force("AI 军事: " + (k.Name != null ? k.Name.ToString() : k.StringId) + " " + msg);
            }
            catch { }
        }

        private static float Clamp01(float v) { return v < 0f ? 0f : (v > 1f ? 1f : v); }

        internal static float GoldOf(Kingdom k)
        {
            try
            {
                if (k == null) return 0f;
                var pk = PlayerKingdom();
                if (pk != null && ReferenceEquals(k, pk)) return EconomyWorld.Treasury.Gold;
                return WarEconomy.GoldOfPublic(k);
            }
            catch { return 0f; }
        }

        // ================= 军力评估 =================
        // 机动兵力(质量加权): 兵力 × (0.55 + 0.30×组织度 + 0.15×士气); 国防军军团单独按真实组织度/士气算
        internal static float MobilePower(Kingdom k)
        {
            try
            {
                if (k == null) return 0f;
                int day = (int)CampaignTime.Now.ToDays;
                if (_powerDay != day) { _powerDay = day; _mobilePower.Clear(); }
                float v;
                if (_mobilePower.TryGetValue(k.StringId, out v)) return v;
                v = 0f;
                var pk = PlayerKingdom();
                foreach (var p in MobileParty.All)
                {
                    if (p == null || !p.IsActive) continue;
                    if (p.IsGarrison || p.IsMilitia || p.IsCaravan || p.IsVillager) continue;
                    if (!ReferenceEquals(p.MapFaction, k)) continue;
                    if (pk != null && ReferenceEquals(k, pk) && DefArmy.IsDefArmyParty(p)) continue;   // 军团单独算
                    int men = 0;
                    try { men = p.MemberRoster != null ? p.MemberRoster.TotalManCount : 0; } catch { }
                    if (men <= 0) continue;
                    float mor = 50f;
                    try { mor = p.Morale; } catch { }
                    v += men * (0.55f + 0.30f * Clamp01(mor / 100f) + 0.15f * 0.8f);
                }
                if (pk != null && ReferenceEquals(k, pk))
                {
                    for (int i = 0; i < DefArmy.Legions.Count; i++)
                    {
                        var lg = DefArmy.Legions[i];
                        var p = DefArmy.LegionParty(lg);
                        if (p == null || !p.IsActive) continue;
                        int men = DefArmy.RegularsOf(p);
                        if (men <= 0) continue;
                        string key = ArmyDoctrine.LegionKey(lg);
                        v += men * (0.55f + 0.30f * Clamp01(ArmyDoctrine.OrgOf2(key) / 100f)
                                          + 0.15f * Clamp01(ArmyDoctrine.MoraleOf2(key) / 100f));
                    }
                }
                _mobilePower[k.StringId] = v;
                return v;
            }
            catch { return 0f; }
        }

        // 驻军(含民兵折算 0.8); only != null 时只算该城
        internal static float GarrisonPower(Kingdom k, Settlement only)
        {
            try
            {
                if (k == null) return 0f;
                float v = 0f;
                foreach (var s in k.Settlements)
                {
                    if (s == null) continue;
                    if (only != null && !ReferenceEquals(s, only)) continue;
                    if (!(s.IsTown || s.IsCastle)) continue;
                    try { if (s.Town != null && s.Town.GarrisonParty != null) v += s.Town.GarrisonParty.MemberRoster.TotalManCount; } catch { }
                    try { if (s.Town != null) v += s.Town.Militia * 0.8f; } catch { }
                }
                return v;
            }
            catch { return 0f; }
        }

        // 国力对比(含敌方驻军): 我方机动 / (敌机动 + 敌驻军)
        internal static float ForceRatioOf(Kingdom k, Kingdom e)
        {
            try
            {
                if (k == null || e == null) return 1f;
                float theirs = MobilePower(e) + GarrisonPower(e, null);
                if (theirs < 1f) return 2f;
                return MobilePower(k) / theirs;
            }
            catch { return 1f; }
        }

        // 野战对比(只看机动兵力)
        internal static float FieldRatioOf(Kingdom k, Kingdom e)
        {
            try
            {
                if (k == null || e == null) return 1f;
                float theirs = MobilePower(e);
                if (theirs < 1f) return 2f;
                return MobilePower(k) / theirs;
            }
            catch { return 1f; }
        }

        // 统一大脑指定方向: 有指定目标时必须打指定国(集中兵力)
        internal static bool OnBrainTarget(Kingdom k, Kingdom e)
        {
            try
            {
                if (k == null || e == null) return false;
                string tid = AiBrain.WarTargetId(k);
                return string.IsNullOrEmpty(tid) || tid == e.StringId;
            }
            catch { return true; }
        }

        // 主动进攻条件: 统一大脑指定方向 且 我方机动 ≥1.2×(敌机动+目标驻军)
        internal static bool CanPushAttack(Kingdom k, Kingdom e, Settlement target)
        {
            try
            {
                if (k == null || e == null) return false;
                if (!OnBrainTarget(k, e)) return false;
                float need = MobilePower(e) + GarrisonPower(e, target);
                if (need < 1f) need = 1f;
                return MobilePower(k) >= AttackRatio * need;
            }
            catch { return false; }
        }

        internal static int WarCount(Kingdom k)
        {
            try
            {
                int n = 0;
                foreach (var x in Kingdom.All)
                {
                    if (x == null || x.IsEliminated || ReferenceEquals(x, k)) continue;
                    if (k.IsAtWarWith(x)) n++;
                }
                return n;
            }
            catch { return 0; }
        }

        // ================= 军团轮换/集中(玩家王国) =================
        private static void LegionWeek(Kingdom pk, int day)
        {
            if (pk == null || pk.IsEliminated) return;
            if (DefArmy.Legions.Count == 0) return;
            Kingdom enemy = WarEnemyOf(pk, day);
            bool atWar = enemy != null;
            bool onTarget = atWar && OnBrainTarget(pk, enemy);
            float ratio = atWar ? ForceRatioOf(pk, enemy) : 1f;
            Settlement target = atWar ? WarTargetOf(pk, enemy, day) : null;
            bool push = target != null && CanPushAttack(pk, enemy, target);
            Settlement besieged = BesiegedOwnCity(pk);
            for (int i = 0; i < DefArmy.Legions.Count; i++)
            {
                try { LegionTick(pk, enemy, day, atWar, onTarget, ratio, target, push, besieged, DefArmy.Legions[i]); }
                catch { }
            }
        }

        private static void LegionTick(Kingdom pk, Kingdom enemy, int day, bool atWar, bool onTarget, float ratio,
            Settlement target, bool push, Settlement besieged, DefLegion lg)
        {
            if (lg == null) return;
            var p = DefArmy.LegionParty(lg);
            if (p == null || !p.IsActive) return;
            if (CommandTimeout.IsCommanded(p) || MapSelection.Is(p)) return;   // 玩家正在指挥/选中 -> 不碰
            if (PlayerTasked(lg)) return;                                       // 玩家指定的驻防/巡逻 -> 不碰
            if (DefArmy.IsPlayerHeld(lg)) return;                               // v4.245: 玩家接管中的军团一律不碰
            bool inArmy = false;
            try { inArmy = p.Army != null; } catch { }
            if (inArmy) return;                                                 // 已加入玩家军团 -> 不碰
            bool inBattle = false;
            try { inBattle = p.MapEvent != null; } catch { }
            if (inBattle) return;                                               // 交战中不插指令

            string key = ArmyDoctrine.LegionKey(lg);
            float org = 100f, mor = 100f;
            try { org = ArmyDoctrine.OrgOf2(key); } catch { }
            try { mor = ArmyDoctrine.MoraleOf2(key); } catch { }
            int men = DefArmy.RegularsOf(p);
            int expected = lg.Expected > 0 ? lg.Expected : men;
            float food = 100f;
            try { food = p.Food; } catch { }
            bool hurt = org < RestOrg || mor < RestMorale || (expected > 0 && men < expected * RestMenFrac) || men < LegionMinAttack;
            bool starving = food < LowFood;
            Settlement restCity = NearestOwnCity(pk, p);

            if (hurt || starving)
            {
                SetTask(lg, "AI休整");
                RailOff(p);                                                   // 休整关铁路
                if (restCity != null && Dist(p, restCity) > 25f)
                {
                    MoveTo(p, restCity);
                    Log(pk, "军团 " + NameOf(p) + " 撤回 " + NameOf(restCity) + " 休整(" + RestReason(org, mor, men, expected, food) + ")");
                }
                else if (restCity != null)
                {
                    int keep = MinGarrison(pk, restCity, enemy);               // 驻军按威胁留最小守备
                    int avail = DefArmy.GarrisonMenOf(restCity.StringId) - keep;
                    int want = expected - men;
                    if (avail > 0 && want > 0) DefArmy.ReplenishLegionAt(p, restCity, Math.Min(want, avail));
                }
                return;
            }

            if (!atWar)
            {
                SetTask(lg, "AI驻守");
                RailOff(p);   // 和平关铁路; 不动位置(玩家可能在前线集结)
                return;
            }

            if (!onTarget || ratio < RetreatRatio)
            {
                var def = besieged != null ? besieged : (restCity != null ? restCity : DefArmy.FindSettlement(lg.HomeId));
                SetTask(lg, "AI防守");
                RailOff(p);
                if (def != null && Dist(p, def) > 25f) MoveTo(p, def);
                return;
            }

            if (!push || target == null)
            {
                SetTask(lg, "AI集结");
                var rally = target != null ? NearestOwnCityTo(pk, target.Position) : restCity;
                if (rally != null && Dist(p, rally) > 25f) MoveTo(p, rally);
                return;
            }

            // ---- 进攻 ----
            // 单军团不足目标所需兵力 90% -> 先向目标方向集结(避免一支打光)
            float needed = 0f;
            try { var plan = WarPlans.Get(pk, enemy); if (plan != null) needed = plan.NeededTroops; } catch { }
            if (needed > 0f && men < needed * 0.9f)
            {
                SetTask(lg, "AI集结");
                var rally = NearestOwnCityTo(pk, target.Position);
                if (rally != null && Dist(p, rally) > 25f) MoveTo(p, rally);
                return;
            }

            SetTask(lg, "AI进攻");
            // 围城拖垮: N 天且守军未被消耗 -> 撤围休整
            Settlement bs = null;
            try { bs = p.BesiegedSettlement; } catch { }
            if (bs != null && ReferenceEquals(bs.MapFaction, enemy))
            {
                int since;
                if (!_siegeStart.TryGetValue(p.StringId, out since))
                {
                    since = day; _siegeStart[p.StringId] = day; _siegeEnemy[p.StringId] = GarrisonPower(enemy, bs);
                }
                float g0;
                _siegeEnemy.TryGetValue(p.StringId, out g0);
                bool progress = GarrisonPower(enemy, bs) < g0 * 0.98f;
                if (day - since >= SiegeDays && !progress)
                {
                    _siegeStart.Remove(p.StringId); _siegeEnemy.Remove(p.StringId);
                    SetTask(lg, "AI休整");
                    RailOff(p);
                    if (restCity != null) MoveTo(p, restCity);
                    Log(pk, "军团 " + NameOf(p) + " 围城 " + (day - since) + " 天无进展, 撤围休整");
                    return;
                }
            }
            else { _siegeStart.Remove(p.StringId); _siegeEnemy.Remove(p.StringId); }

            // 铁路战略投送(失败跳过); 投送后立即开铁路运输提高机动
            if (Dist(p, target) > 200f && TryRailDispatch(pk, p, target, day))
            {
                try { ArmyDoctrine.SetRailTransport(p, true); } catch { }
                Log(pk, "军团 " + NameOf(p) + " 军列投送 -> " + NameOf(target));
                return;
            }
            RailForMarch(pk, p, target);

            try
            {
                if (target.IsVillage) p.SetMoveRaidSettlement(target, MobileParty.NavigationType.Default, false);
                else p.SetMoveBesiegeSettlement(target, MobileParty.NavigationType.Default);
                CommandTimeout.Touch(p, target.IsVillage ? target.Position : target.GatePosition);
            }
            catch { }
        }

        // ================= 和谈(玩家王国, 走现有表决入口) =================
        private static void PeaceWeek(Kingdom pk, int day)
        {
            if (pk == null || pk.IsEliminated) return;
            int wars = WarCount(pk);
            Kingdom main = WarEnemyOf(pk, day);
            foreach (var e in Kingdom.All)
            {
                if (e == null || e.IsEliminated || ReferenceEquals(e, pk)) continue;
                bool war = false;
                try { war = pk.IsAtWarWith(e); } catch { }
                if (!war) continue;
                int last;
                if (_peaceAsked.TryGetValue(e.StringId, out last) && day - last < 28) continue;

                float wear = WarWeariness.WearOf(pk, e);
                float ratio = ForceRatioOf(pk, e);
                bool achieved = false;
                var plan = WarPlans.Get(pk, e);
                if (plan != null)
                {
                    var mt = WarPlans.Find(plan.MainTargetId);
                    achieved = mt != null && ReferenceEquals(mt.MapFaction, pk);   // 拿下目标城 = 目标达成
                }
                bool multiSecondary = wars >= 2 && !ReferenceEquals(e, main) && wear >= 45f;
                if (!(wear >= 50f || ratio < 0.7f || achieved || multiSecondary)) continue;
                if (AiDiplomacy.WarDays(pk, e) < 28) continue;
                // 统一大脑还想打(开战条件)且非大劣 -> 不急谈
                if (AiBrain.WantsWar(pk) && !achieved && wear < 70f && ratio >= 0.6f) continue;

                _peaceAsked[e.StringId] = day;
                bool ok = false;
                try { ok = DiplomacyBehavior.PlayerMakePeace(e); } catch { }
                Log(pk, "厌战/劣势, 推动与 " + (e.Name != null ? e.Name.ToString() : e.StringId)
                    + " 和谈" + (ok ? "(成功)" : "(表决未过)"));
            }
        }

        // ================= AI 王国军列投送 =================
        //   战时把后方主力经军列送到统一大脑目标方向; 玩家王国军团由 Railways.AiLegionRailMonthly 负责
        private static void RailWeek(int day)
        {
            var pk = PlayerKingdom();
            foreach (var k in Kingdom.All)
            {
                if (k == null || k.IsEliminated) continue;
                if (pk != null && ReferenceEquals(k, pk)) continue;
                if (WarCount(k) == 0) continue;
                if (WarEconomy.IsCrisis(k)) continue;
                Kingdom enemy = WarEnemyOf(k, day);
                if (enemy == null) continue;
                Settlement target = WarTargetOf(k, enemy, day);
                if (target == null) continue;
                if (!CanPushAttack(k, enemy, target)) continue;

                MobileParty best = null;
                float bs = 0f;
                foreach (var p in MobileParty.All)
                {
                    if (p == null || !p.IsActive) continue;
                    if (p.IsMainParty || p.IsGarrison || p.IsMilitia || p.IsCaravan || p.IsVillager) continue;
                    if (p.LeaderHero == null) continue;
                    if (!ReferenceEquals(p.MapFaction, k)) continue;
                    bool inArmy = false;
                    try { inArmy = p.Army != null; } catch { }
                    if (inArmy) continue;
                    if (p.Position.Distance(target.Position) < 200f) continue;   // 已在战区
                    float men = 0f;
                    try { men = p.MemberRoster != null ? p.MemberRoster.TotalManCount : 0f; } catch { }
                    if (men > bs) { bs = men; best = p; }
                }
                if (best == null || bs < LegionMinAttack) continue;
                if (!TryRailDispatch(k, best, target, day)) continue;
                try { ArmyDoctrine.SetRailTransport(best, true); } catch { }
                Log(k, "军列投送 " + NameOf(best) + " -> " + NameOf(target));
            }
        }

        // 战略投送: 部队在本国铁路城旁(≤30) -> 军列直达最靠近战区目标的本国铁路城
        //   运力/冷却/付款由 Railways.LoadArmyMilitary 校验, 失败跳过
        private static bool TryRailDispatch(Kingdom k, MobileParty p, Settlement target, int day)
        {
            try
            {
                if (k == null || p == null || target == null) return false;
                int last;
                if (_railDay.TryGetValue(p.StringId, out last) && day - last < DispatchCooldown) return false;

                Settlement to = null;
                float bd = float.MaxValue;
                foreach (var s in k.Settlements)
                {
                    if (s == null || !(s.IsTown || s.IsCastle)) continue;
                    float d = s.Position.Distance(target.Position);
                    if (d > 250f) continue;
                    if (Railways.MilitaryCapacity(s.StringId) <= 0) continue;
                    if (d < bd) { bd = d; to = s; }
                }
                if (to == null) return false;

                Settlement from = null;
                bd = float.MaxValue;
                foreach (var s in k.Settlements)
                {
                    if (s == null || !(s.IsTown || s.IsCastle)) continue;
                    if (s.Position.Distance(p.Position) > 30f) continue;
                    if (!Railways.HasLine(s.StringId, to.StringId)) continue;
                    float d = s.Position.Distance(p.Position);
                    if (d < bd) { bd = d; from = s; }
                }
                if (from == null || ReferenceEquals(from, to)) return false;

                int men = DefArmy.RegularsOf(p);
                if (men < LegionMinAttack) return false;
                int load = (men + 99) / 100;
                if (Railways.MilitaryCapacity(from.StringId) - Railways.MilitaryUsedToday(from.StringId) < load) return false;

                float gold = GoldOf(k);
                float budget = AiBrain.BudgetFor(k, "army", gold);   // 统一大脑军费额度(已扣储备)
                if (budget <= 0f) return false;
                int cost = Railways.MilitaryCost(from.StringId, to.StringId, p);
                if (budget < cost) return false;

                string err = Railways.LoadArmyMilitary(p, from, to.StringId);
                if (err != "") return false;
                _railDay[p.StringId] = day;
                DLog.Info("AI 军列: " + (k.Name != null ? k.Name.ToString() : k.StringId) + " " + NameOf(p)
                    + " " + from.StringId + " -> " + to.StringId + " (费 " + cost + ")");
                return true;
            }
            catch { return false; }
        }

        // ================= 目标/城市选择 =================
        private static Kingdom WarEnemyOf(Kingdom k, int day)
        {
            try
            {
                if (k == null) return null;
                var bt = FindKingdom(AiBrain.WarTargetId(k));   // 统一大脑目标 = 敌国 id
                if (bt != null && !ReferenceEquals(bt, k) && k.IsAtWarWith(bt)) return bt;
                var plan = WarPlans.MainOf(k, day);
                if (plan != null)
                {
                    var e = FindKingdom(plan.EnemyId);
                    if (e != null && k.IsAtWarWith(e)) return e;
                }
                Kingdom best = null;
                float bs = 0f;
                foreach (var x in Kingdom.All)
                {
                    if (x == null || x.IsEliminated || ReferenceEquals(x, k)) continue;
                    if (!k.IsAtWarWith(x)) continue;
                    float s = WarPlans.StrengthOf(x);
                    if (s > bs) { bs = s; best = x; }
                }
                return best;
            }
            catch { return null; }
        }

        private static Settlement WarTargetOf(Kingdom k, Kingdom enemy, int day)
        {
            try
            {
                if (k == null || enemy == null) return null;
                var plan = WarPlans.GetOrCreate(k, enemy, day);
                if (plan != null)
                {
                    var mt = WarPlans.Find(plan.MainTargetId);
                    if (mt != null && ReferenceEquals(mt.MapFaction, enemy)) return mt;
                }
                return null;
            }
            catch { return null; }
        }

        private static Settlement BesiegedOwnCity(Kingdom k)
        {
            try
            {
                foreach (var s in k.Settlements)
                {
                    if (s == null) continue;
                    bool under = false;
                    try { under = s.IsUnderSiege; } catch { }
                    if (under) return s;
                }
            }
            catch { }
            return null;
        }

        private static Settlement NearestOwnCity(Kingdom k, MobileParty p)
        {
            try { return p != null ? NearestOwnCityTo(k, p.Position) : null; }
            catch { return null; }
        }

        private static Settlement NearestOwnCityTo(Kingdom k, CampaignVec2 pos)
        {
            try
            {
                Settlement best = null;
                float bd = float.MaxValue;
                foreach (var s in k.Settlements)
                {
                    if (s == null || !(s.IsTown || s.IsCastle)) continue;
                    float d = s.Position.Distance(pos);
                    if (d < bd) { bd = d; best = s; }
                }
                return best;
            }
            catch { return null; }
        }

        // 驻军最小守备: 40 + 威胁(0~1)×150 + 前线(离敌城 120 内) 60, 上限 300
        private static int MinGarrison(Kingdom k, Settlement s, Kingdom enemy)
        {
            int min = 40;
            try { min += (int)(AiBrain.ThreatOf(k) * 150f); } catch { }
            try
            {
                if (s != null && enemy != null)
                {
                    foreach (var es in enemy.Settlements)
                    {
                        if (es == null) continue;
                        if (s.Position.Distance(es.Position) < 120f) { min += 60; break; }
                    }
                }
            }
            catch { }
            return min > 300 ? 300 : min;
        }

        // ================= 铁路运输开关 =================
        // 战时长途开(速度 +20%), 短途/国库薄关; 休整/和平由 RailOff 关
        private static void RailForMarch(Kingdom k, MobileParty p, Settlement target)
        {
            try
            {
                if (p == null || target == null) return;
                bool on = false;
                try { on = ArmyDoctrine.IsRailTransport(p); } catch { }
                if (Dist(p, target) < 150f || GoldOf(k) < 1500f)
                {
                    if (on) ArmyDoctrine.SetRailTransport(p, false);
                    return;
                }
                if (!on) ArmyDoctrine.SetRailTransport(p, true);
            }
            catch { }
        }

        private static void RailOff(MobileParty p)
        {
            try { if (p != null && ArmyDoctrine.IsRailTransport(p)) ArmyDoctrine.SetRailTransport(p, false); }
            catch { }
        }

        // ================= 小工具 =================
        private static bool PlayerTasked(DefLegion lg)
        {
            try
            {
                if (lg == null || string.IsNullOrEmpty(lg.Task)) return false;
                return lg.Task.Contains("驻防") || lg.Task.Contains("巡逻");
            }
            catch { return false; }
        }

        private static void SetTask(DefLegion lg, string task)
        {
            try { if (lg != null && lg.Task != task) lg.Task = task; } catch { }
        }

        private static string RestReason(float org, float mor, int men, int expected, float food)
        {
            if (food < LowFood) return "缺粮 " + (int)food;
            if (org < RestOrg) return "组织度 " + (int)org;
            if (mor < RestMorale) return "士气 " + (int)mor;
            if (men < LegionMinAttack) return "兵力 " + men;
            return "兵力 " + men + "/编制 " + expected;
        }

        // v4.249: 军事总监调度一律"到城门外"(不再让军团进城)
        //   用户反馈"部队莫名其妙进城了" —— 军事总监每周的休整/集结/防守会让军团直接开进城;
        //   玩家自己点城镇(GoToSettlement)仍保留原版入城行为, 只是 AI 不再替玩家决定入城。
        private static void MoveTo(MobileParty p, Settlement s)
        {
            try
            {
                if (p == null || s == null) return;
                var gp = new CampaignVec2(s.GatePosition.ToVec2(), true);
                p.SetMoveGoToPoint(gp, MobileParty.NavigationType.Default);
                CommandTimeout.Touch(p, gp);
            }
            catch { }
        }

        private static float Dist(MobileParty p, Settlement s)
        {
            try { return p.Position.Distance(s.Position); } catch { return float.MaxValue; }
        }

        private static string NameOf(MobileParty p)
        {
            try { return MapSelection.NameOf(p); } catch { return "?"; }
        }

        private static string NameOf(Settlement s)
        {
            try { return s != null && s.Name != null ? s.Name.ToString() : "?"; } catch { return "?"; }
        }

        private static Kingdom FindKingdom(string id)
        {
            try
            {
                if (string.IsNullOrEmpty(id)) return null;
                foreach (var k in Kingdom.All) if (k != null && k.StringId == id) return k;
            }
            catch { }
            return null;
        }
    }

    // ================= v5.x: 领主 AI(设计 28.4) —— 装备下单 / 征兵填编 / 降档 / 省钱 =================
    //   ① 下单: 按性格编成(好战 掷弹+骑兵 / 重商 线列+炮兵 / 防御 线列+工兵)算兵种缺口 ->
    //      装备需求(最便宜的已解锁型号) − 私库现有 − 在途, 经 Armory.Order 花领主(家族)自己的金;
    //      优先级 0 前线(交战中/在 WarTargetId 方向) > 1 缺编 > 2 储备; 领主金不足只补最缺的 1~2 项
    //   ② 征兵: 装备到货(Armory.Count 足够) -> 最近的 SlotLeft>0 本国城市/村庄 -> LordArmy.Recruit 补编
    //   ③ 降档: 目标兵种未解锁/无货 -> 沿降档链改招低档(线列→民兵 等), AvailableKinds 决定实际可招兵种
    //   ④ 省钱: 和平期领主金 < 30 天军饷 -> 裁最低价值兵种(最多两档), 装备按 28.9 回领主私库
    //   性能: 全月结(不日结); 每国每月最多 2 条 "AI 领主:" 日志; 全部 try/catch。
    internal static class LordArmyAi
    {
        private const int LogMax = 2;
        private const int MaxOrderItems = 6;        // 金够时每家族每月最多下单项数
        private const int MaxOrderItemsTight = 2;   // 金不足时只补最缺的 1~2 项
        private static int _lastMonth = -1;
        private static int _logBucket = -1;
        private static readonly Dictionary<string, int> _logCount = new Dictionary<string, int>(StringComparer.Ordinal);
        // 在途订单估算: ownerKey -> equipId -> 已下单未到货件数; _seenStock 记录下单时私库库存
        private static readonly Dictionary<string, Dictionary<string, int>> _ordered = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
        private static readonly Dictionary<string, Dictionary<string, int>> _seenStock = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);

        private sealed class PartyPlan
        {
            internal MobileParty Party;
            internal bool Frontline;
            internal int Expected;
            internal readonly Dictionary<int, int> Current = new Dictionary<int, int>();
            internal readonly Dictionary<int, int> Gap = new Dictionary<int, int>();
        }

        private sealed class ClanPlan
        {
            internal Clan Clan;
            internal int Purse;
            internal float Wage30;
            internal int Men;
            internal bool Frontline;
            internal readonly List<PartyPlan> Parties = new List<PartyPlan>();
            internal readonly Dictionary<string, int> NeedMain = new Dictionary<string, int>(StringComparer.Ordinal);
            internal readonly Dictionary<string, int> NeedRes = new Dictionary<string, int>(StringComparer.Ordinal);
        }

        private sealed class OrderItem
        {
            internal string Id = "";
            internal int Qty;
            internal int Priority;
            internal int Deficit;
        }

        private sealed class Profile
        {
            internal readonly List<int> Kinds = new List<int>();
            internal readonly List<int> Weights = new List<int>();
            internal int Total;
        }

        // 降档链(28.4: 缺火器/甲 -> 改招低档): 线列→民兵 / 掷弹→线列→民兵 / 龙骑→骠骑→民兵 …
        private static readonly int[][] Fallback = new int[][]
        {
            new int[] { 0 },              // 民兵
            new int[] { 1, 0 },           // 线列 → 民兵
            new int[] { 2, 1, 0 },        // 轻步兵 → 线列 → 民兵
            new int[] { 3, 1, 0 },        // 掷弹兵 → 线列 → 民兵
            new int[] { 4, 5, 0 },        // 龙骑兵 → 骠骑兵 → 民兵
            new int[] { 5, 0 },           // 骠骑兵 → 民兵
            new int[] { 6, 5, 0 },        // 胸甲骑兵 → 骠骑兵 → 民兵
            new int[] { 7, 0 },           // 弓兵 → 民兵
            new int[] { 8, 7, 0 },        // 弩兵 → 弓兵 → 民兵
            new int[] { 9, 5, 0 },        // 骑射手 → 骠骑兵 → 民兵
            new int[] { 10, 1, 0 },       // 野战炮兵 → 线列 → 民兵
            new int[] { 11, 10, 1 },      // 攻城炮兵 → 野战炮兵 → 线列
            new int[] { 12, 10, 1 },      // 骑炮兵 → 野战炮兵 → 线列
            new int[] { 13, 0 }           // 工兵 → 民兵
        };

        // ---------------- 月结入口(由 AiEconomyDeep.Monthly 调用) ----------------
        internal static void Monthly(int day)
        {
            try
            {
                if (day < _lastMonth) { _lastMonth = -1; _logBucket = -1; _logCount.Clear(); _ordered.Clear(); _seenStock.Clear(); }
                if (day == _lastMonth) return;
                _lastMonth = day;
                int bucket = day / 7;
                if (bucket != _logBucket) { _logBucket = bucket; _logCount.Clear(); }

                var pk = AiWarDirector.PlayerKingdom();
                var byKingdom = new Dictionary<string, List<MobileParty>>(StringComparer.Ordinal);
                foreach (var p in MobileParty.All)
                {
                    if (p == null || !p.IsActive || p.LeaderHero == null) continue;
                    if (p.IsMainParty || p.IsGarrison || p.IsMilitia || p.IsCaravan || p.IsVillager) continue;
                    if (DefArmy.IsDefArmyParty(p)) continue;   // 国防军有自己的 AI(军事总监)
                    var k = p.MapFaction as Kingdom;
                    if (k == null || k.IsEliminated) continue;
                    if (pk != null && ReferenceEquals(k, pk)) continue;   // 玩家王国由玩家管理
                    List<MobileParty> list;
                    if (!byKingdom.TryGetValue(k.StringId, out list)) { list = new List<MobileParty>(); byKingdom[k.StringId] = list; }
                    list.Add(p);
                }

                foreach (var k in Kingdom.All)
                {
                    if (k == null || k.IsEliminated) continue;
                    if (pk != null && ReferenceEquals(k, pk)) continue;
                    List<MobileParty> list;
                    if (!byKingdom.TryGetValue(k.StringId, out list) || list.Count == 0) continue;
                    try { KingdomMonth(k, day, list); } catch { }
                }
            }
            catch (Exception ex) { try { DLog.Force("AI 领主: 月结异常 " + ex.Message); } catch { } }
        }

        private static void KingdomMonth(Kingdom k, int day, List<MobileParty> parties)
        {
            bool atWar = AiWarDirector.WarCount(k) > 0;
            Settlement target = TargetSettlement(k, day);
            Profile pr = ProfileOf(k);
            var clans = new Dictionary<string, ClanPlan>(StringComparer.Ordinal);
            var plans = new List<PartyPlan>();

            for (int i = 0; i < parties.Count; i++)
            {
                var p = parties[i];
                try
                {
                    var plan = BuildPartyPlan(p, pr, target);
                    if (plan == null) continue;
                    plans.Add(plan);
                    var clan = p.LeaderHero != null ? p.LeaderHero.Clan : null;
                    if (clan == null) continue;
                    string key = Armory.LordOwner(clan);
                    if (string.IsNullOrEmpty(key)) continue;
                    ClanPlan cp;
                    if (!clans.TryGetValue(key, out cp)) { cp = new ClanPlan(); cp.Clan = clan; clans[key] = cp; }
                    cp.Parties.Add(plan);
                    cp.Frontline |= plan.Frontline;
                    cp.Men += TotalOf(plan.Current);
                    try { if (clan.Leader != null) cp.Purse = clan.Leader.Gold; } catch { }
                    try { cp.Wage30 += LordArmy.DailyWageOf(p) * 30f; } catch { }
                    foreach (var gap in plan.Gap) AddKindNeed(cp.NeedMain, gap.Key, gap.Value);
                }
                catch { }
            }
            if (clans.Count == 0) return;

            int orderLines = 0, orderQty = 0, recruited = 0, disbanded = 0;
            string recruitNote = null, saveNote = null;

            foreach (var kv in clans)
            {
                try
                {
                    var cp = kv.Value;
                    // 和平 + 金宽裕 -> 攒 10% 储备装备(优先级 2)
                    if (!atWar && cp.Purse > cp.Wage30 * 2f && pr.Kinds.Count > 0)
                        AddKindNeed(cp.NeedRes, pr.Kinds[0], Math.Max(2, cp.Men / 10));
                    OrderClan(cp, ref orderLines, ref orderQty);
                }
                catch { }
            }

            // 战时优先补前线部队
            plans.Sort(delegate (PartyPlan a, PartyPlan b) { return b.Frontline.CompareTo(a.Frontline); });
            for (int i = 0; i < plans.Count; i++)
            {
                try { RecruitParty(k, plans[i], pr, ref recruited, ref recruitNote); } catch { }
            }

            if (!atWar)
            {
                foreach (var kv in clans)
                {
                    try { SaveClan(kv.Value, ref disbanded, ref saveNote); } catch { }
                }
            }

            if (orderQty > 0) Log(k, "下单 " + orderLines + " 项/" + orderQty + " 件" + (atWar ? "(战时优先)" : ""));
            string tail = "";
            if (recruited > 0) tail += "征兵 +" + recruited + " 人" + (string.IsNullOrEmpty(recruitNote) ? "" : "(" + recruitNote + ")");
            if (disbanded > 0) tail += (tail.Length > 0 ? " · " : "") + "省钱裁 " + disbanded + " 人" + (string.IsNullOrEmpty(saveNote) ? "" : "(" + saveNote + ")");
            if (tail.Length > 0) Log(k, tail);
        }

        // ---------------- 编成: 性格 → 目标兵种/权重(已按科技降档折叠) ----------------
        private static Profile ProfileOf(Kingdom k)
        {
            var w = ProfileShares(k);
            var pr = new Profile();
            for (int idx = 0; idx < w.Length && idx < UnitCount; idx++)
            {
                if (w[idx] <= 0) continue;
                int pick = FirstUnlocked(idx);
                if (pick < 0) continue;
                int at = pr.Kinds.IndexOf(pick);
                if (at < 0) { pr.Kinds.Add(pick); pr.Weights.Add(w[idx]); }
                else pr.Weights[at] += w[idx];
            }
            if (pr.Kinds.Count == 0) { pr.Kinds.Add(0); pr.Weights.Add(1); }
            for (int i = 0; i < pr.Weights.Count; i++) pr.Total += pr.Weights[i];
            return pr;
        }

        // 好战→掷弹兵+骑兵; 重商→线列+炮兵; 防御→线列+工兵(27.14 / 28.4)
        private static int[] ProfileShares(Kingdom k)
        {
            int agg = 50, dev = 50, com = 50;
            AiDirector.Goal goal = AiDirector.Goal.Economy;
            try { agg = AiPersonality.AggressionOf(k); } catch { }
            try { dev = AiPersonality.DevelopmentOf(k); } catch { }
            try { com = AiPersonality.CommerceOf(k); } catch { }
            try { goal = AiBrain.GoalOf(k); } catch { }
            if (goal == AiDirector.Goal.Military || agg >= 60)
                return new int[] { 15, 25, 0, 20, 15, 10, 5, 0, 0, 0, 10, 0, 0, 0 };
            if (goal == AiDirector.Goal.Economy || (dev >= 60 && com >= 60))
                return new int[] { 20, 40, 0, 0, 5, 0, 0, 0, 10, 0, 15, 0, 5, 5 };
            if (goal == AiDirector.Goal.Survival)
                return new int[] { 30, 30, 0, 0, 0, 10, 0, 10, 5, 0, 0, 0, 0, 15 };
            return new int[] { 25, 35, 0, 5, 10, 5, 0, 10, 0, 0, 10, 0, 0, 0 };
        }

        private static PartyPlan BuildPartyPlan(MobileParty p, Profile pr, Settlement target)
        {
            var pp = new PartyPlan();
            pp.Party = p;
            CurrentKinds(p, pp.Current);
            int current = TotalOf(pp.Current);
            pp.Expected = ExpectedOf(p, current);
            bool battle = false, siege = false, near = false;
            try { battle = p.MapEvent != null; } catch { }
            try { siege = p.BesiegedSettlement != null; } catch { }
            try { near = target != null && p.Position.Distance(target.Position) < 150f; } catch { }
            pp.Frontline = battle || siege || near;
            if (pr.Total <= 0) return pp;
            for (int i = 0; i < pr.Kinds.Count; i++)
            {
                int idx = pr.Kinds[i];
                int want = (int)Math.Round(pp.Expected * (double)pr.Weights[i] / pr.Total);
                int have;
                pp.Current.TryGetValue(idx, out have);
                int gap = want - have;
                if (gap > 0) pp.Gap[idx] = gap;
            }
            return pp;
        }

        // ---------------- ① 装备下单(家族为单位, 花领主自己的金) ----------------
        private static void OrderClan(ClanPlan cp, ref int orderLines, ref int orderQty)
        {
            if (cp.Clan == null) return;
            string owner = Armory.LordOwner(cp.Clan);
            if (string.IsNullOrEmpty(owner)) return;

            var items = new List<OrderItem>();
            var ids = new Dictionary<string, bool>(StringComparer.Ordinal);
            foreach (var e in cp.NeedMain.Keys) ids[e] = true;
            foreach (var e in cp.NeedRes.Keys) ids[e] = true;
            foreach (var kv in ids)
            {
                string id = kv.Key;
                int main;
                cp.NeedMain.TryGetValue(id, out main);
                int res;
                cp.NeedRes.TryGetValue(id, out res);
                int qty = main + res - Armory.CountKey(owner, id) - PendingOf(owner, id);
                if (qty <= 0) continue;
                var it = new OrderItem();
                it.Id = id;
                it.Qty = qty;
                it.Deficit = main > 0 ? main : res;
                it.Priority = main > 0 ? (cp.Frontline ? 0 : 1) : 2;   // 0 前线 > 1 缺编 > 2 储备
                items.Add(it);
            }
            if (items.Count == 0) return;
            items.Sort(delegate (OrderItem a, OrderItem b)
            {
                int c = a.Priority.CompareTo(b.Priority);
                if (c != 0) return c;
                return b.Deficit.CompareTo(a.Deficit);
            });

            int totalCost = 0;
            for (int i = 0; i < items.Count; i++) totalCost += items[i].Qty * Armory.GoldCostOf(items[i].Id);
            // 预算: 先留 30 天军饷; 领主金不足时只补最缺的 1~2 项(应急 ≤ 现金 1/4)
            int budget = cp.Purse - (int)Math.Ceiling(cp.Wage30) - 50;
            int limit;
            if (budget > 0)
            {
                limit = totalCost > budget ? MaxOrderItemsTight : MaxOrderItems;
            }
            else
            {
                if (cp.Purse <= 0) return;
                budget = cp.Purse / 4;
                if (budget <= 0) return;
                limit = 1;
            }
            int spent = 0, placed = 0;
            for (int i = 0; i < items.Count && placed < limit; i++)
            {
                var it = items[i];
                int cost = it.Qty * Armory.GoldCostOf(it.Id);
                if (cost > budget - spent) continue;   // 该项买不起 -> 试下一项
                Armory.Order(cp.Clan, it.Id, it.Qty, it.Priority);
                NoteOrder(owner, it.Id, it.Qty);
                spent += cost;
                placed++;
                orderLines++;
                orderQty += it.Qty;
            }
        }

        // ---------------- ②③ 征兵填编 + 降档 ----------------
        private static void RecruitParty(Kingdom k, PartyPlan pp, Profile pr, ref int recruited, ref string note)
        {
            if (pp == null || pp.Party == null || pp.Gap.Count == 0) return;
            var want = new List<int>();
            for (int i = 0; i < pr.Kinds.Count; i++)
                if (pp.Gap.ContainsKey(pr.Kinds[i])) want.Add(pr.Kinds[i]);
            foreach (var kv in pp.Gap)
                if (!want.Contains(kv.Key)) want.Add(kv.Key);
            if (want.Count == 0) return;

            // 找最近的、有征兵名额且私库可支撑首选兵种的本国城市/村庄
            Settlement best = null;
            var bestAvail = new List<int>();
            var avail = new List<int>();
            float bestDist = float.MaxValue;
            foreach (var s in k.Settlements)
            {
                if (s == null || !(s.IsTown || s.IsVillage)) continue;
                float slotLeft = 0f;
                try { slotLeft = LordArmy.SlotLeft(s); } catch { }
                if (slotLeft < 1f) continue;
                avail.Clear();
                CollectAvailable(pp.Party, s, avail);
                bool any = false;
                for (int i = 0; i < want.Count && !any; i++) any = avail.Contains(want[i]);
                if (!any) continue;
                float d = Dist(pp.Party, s);
                if (d < bestDist) { bestDist = d; best = s; bestAvail.Clear(); bestAvail.AddRange(avail); }
            }
            if (best == null || bestAvail.Count == 0) return;

            float slotF = 0f;
            try { slotF = LordArmy.SlotLeft(best); } catch { }
            int slot = (int)Math.Floor(slotF);
            if (slot <= 0) return;

            for (int i = 0; i < want.Count && slot > 0; i++)
            {
                int idx = want[i];
                if (!bestAvail.Contains(idx)) continue;   // 降档: 取 AvailableKinds 里偏好序最高的可招兵种
                int gap;
                pp.Gap.TryGetValue(idx, out gap);
                int n = Math.Min(gap, slot);              // 每次最多补到编制上限
                n = MaxMenByStock(pp.Party, idx, n);      // 装备到货(Armory.Count 足够)才招
                if (n <= 0) continue;
                LordArmy.Recruit(pp.Party, best, idx, n);
                recruited += n;
                note = idx != want[0]
                    ? "缺 " + UnitName(want[0]) + " 降档→" + UnitName(idx) + "@" + NameOf(best)
                    : UnitName(idx) + "@" + NameOf(best);
                slot -= n;
            }
        }

        // ---------------- ④ 和平期省钱: 领主金 < 30 天军饷 -> 裁最低价值兵种 ----------------
        private static void SaveClan(ClanPlan cp, ref int disbanded, ref string note)
        {
            if (cp.Clan == null || cp.Parties.Count == 0) return;
            if (cp.Wage30 <= 0f) return;
            if (cp.Purse >= cp.Wage30) return;                     // 金 >= 30 天军饷 -> 不裁
            float perManMonth = cp.Men > 0 ? cp.Wage30 / cp.Men : 0f;
            if (perManMonth <= 0.001f) return;
            int cutNeed = (int)Math.Ceiling((cp.Wage30 - cp.Purse) / perManMonth);
            if (cutNeed <= 0) return;

            var kinds = new List<int>();
            for (int i = 0; i < cp.Parties.Count; i++)
                foreach (var kv in cp.Parties[i].Current)
                    if (!kinds.Contains(kv.Key)) kinds.Add(kv.Key);
            kinds.Sort(delegate (int a, int b) { return UnitValue(a).CompareTo(UnitValue(b)); });

            var returned = new Dictionary<string, int>(StringComparer.Ordinal);
            int cutTotal = 0;
            int kindMax = kinds.Count > 1 ? 2 : 1;                 // 只动最低价值的 1~2 档
            for (int vi = 0; vi < kinds.Count && vi < kindMax && cutTotal < cutNeed; vi++)
            {
                int idx = kinds[vi];
                for (int pi = 0; pi < cp.Parties.Count && cutTotal < cutNeed; pi++)
                {
                    var pp = cp.Parties[pi];
                    int have;
                    if (!pp.Current.TryGetValue(idx, out have) || have <= 0) continue;
                    int cut = CutFromParty(pp.Party, idx, Math.Min(have, cutNeed - cutTotal));
                    if (cut <= 0) continue;
                    cutTotal += cut;
                    pp.Current[idx] = have - cut;
                    AddKindNeed(returned, idx, cut);               // 装备按 28.9 回领主私库
                }
            }
            if (cutTotal <= 0) return;
            foreach (var kv in returned)
                if (kv.Value > 0) Armory.Add(cp.Clan, kv.Key, kv.Value);
            disbanded += cutTotal;
            note = "裁 " + UnitName(kinds.Count > 0 ? kinds[0] : 0);
        }

        // 从逐人表剔除指定兵种(单部队最多 40%), 同步扣原版名册
        private static int CutFromParty(MobileParty p, int idx, int want)
        {
            int cut = 0;
            try
            {
                if (p == null || want <= 0) return 0;
                var rec = Soldiers.Ensure(p);
                if (rec == null) return 0;
                int maxCut = (int)Math.Floor(rec.List.Count * 0.4f);
                if (want > maxCut) want = maxCut;
                for (int i = rec.List.Count - 1; i >= 0 && cut < want; i--)
                {
                    if (rec.List[i].Unit != idx) continue;
                    rec.List.RemoveAt(i);
                    cut++;
                }
                if (cut > 0)
                {
                    var roster = p.MemberRoster;
                    if (roster != null)
                    {
                        int left = cut;
                        var list = new List<TroopRosterElement>(roster.GetTroopRoster());
                        for (int i = 0; i < list.Count && left > 0; i++)
                        {
                            var e = list[i];
                            if (e.Character == null || e.Character.IsHero || e.Number <= 0) continue;
                            int take = Math.Min(e.Number, left);
                            roster.AddToCounts(e.Character, -take);
                            left -= take;
                        }
                    }
                }
            }
            catch { }
            return cut;
        }

        // ---------------- 在途订单估算(防重复下单) ----------------
        private static int PendingOf(string owner, string id)
        {
            try
            {
                int stock = Armory.CountKey(owner, id);
                var ordered = Bucket(_ordered, owner);
                var seen = Bucket(_seenStock, owner);
                int have;
                ordered.TryGetValue(id, out have);
                int was;
                seen.TryGetValue(id, out was);
                if (stock > was) { have -= (stock - was); if (have < 0) have = 0; }   // 到货核销在途
                seen[id] = stock;
                ordered[id] = have;
                return have;
            }
            catch { return 0; }
        }

        private static void NoteOrder(string owner, string id, int qty)
        {
            try
            {
                var d = Bucket(_ordered, owner);
                int old;
                d.TryGetValue(id, out old);
                d[id] = old + qty;
                Bucket(_seenStock, owner)[id] = Armory.CountKey(owner, id);
            }
            catch { }
        }

        private static Dictionary<string, int> Bucket(Dictionary<string, Dictionary<string, int>> map, string key)
        {
            Dictionary<string, int> d;
            if (!map.TryGetValue(key, out d)) { d = new Dictionary<string, int>(StringComparer.Ordinal); map[key] = d; }
            return d;
        }

        // ---------------- 兵种/装备规则 ----------------
        private static int UnitCount { get { return Equipment.Units.Count; } }

        private static float UnitValue(int idx)
        {
            var u = Equipment.Unit(idx);
            return u != null ? u.Training : 1f;
        }

        private static string UnitName(int idx)
        {
            try
            {
                var u = Equipment.Unit(idx);
                if (u != null && !string.IsNullOrEmpty(u.Name)) return u.Name;
            }
            catch { }
            return "兵种" + idx;
        }

        private static bool IsUnlockedModel(EquipDef e)
        {
            try { return e != null && Equipment.IsUnlocked(e); } catch { return false; }
        }

        // 某标签下最便宜的已解锁型号(AI 省钱; 火器/甲自然取低档)
        private static EquipDef CheapPick(EquipTag tag)
        {
            EquipDef best = null;
            int bestCost = int.MaxValue;
            try
            {
                for (int i = 0; i < Equipment.All.Count; i++)
                {
                    var e = Equipment.All[i];
                    if (e == null || !Equipment.MatchesTag(e, tag) || !IsUnlockedModel(e)) continue;
                    int c = Armory.GoldCostOf(e.Id);
                    if (c < bestCost) { bestCost = c; best = e; }
                }
            }
            catch { }
            return best;
        }

        private static bool KindUnlocked(int idx)
        {
            try
            {
                var u = Equipment.Unit(idx);
                if (u == null || u.Req == null || u.Req.Length == 0) return false;
                for (int r = 0; r < u.Req.Length; r++)
                    if (CheapPick(u.Req[r].Tag) == null) return false;
                return true;
            }
            catch { return false; }
        }

        private static int FirstUnlocked(int idx)
        {
            try
            {
                int[] chain = idx >= 0 && idx < Fallback.Length ? Fallback[idx] : null;
                if (chain == null) return KindUnlocked(idx) ? idx : -1;
                for (int i = 0; i < chain.Length; i++)
                    if (KindUnlocked(chain[i])) return chain[i];
            }
            catch { }
            return -1;
        }

        private static void AddKindNeed(Dictionary<string, int> need, int idx, int men)
        {
            try
            {
                if (need == null || men <= 0) return;
                var u = Equipment.Unit(idx);
                if (u == null || u.Req == null) return;
                for (int r = 0; r < u.Req.Length; r++)
                {
                    var pick = CheapPick(u.Req[r].Tag);
                    if (pick == null) continue;
                    int q = (int)Math.Ceiling(u.Req[r].Per100 * (double)men / 100.0);
                    if (q <= 0) continue;
                    int old;
                    need.TryGetValue(pick.Id, out old);
                    need[pick.Id] = old + q;
                }
            }
            catch { }
        }

        // 私库现有(按标签汇总全部已解锁型号)可支撑的兵员数
        private static int MaxMenByStock(MobileParty p, int idx, int want)
        {
            try
            {
                if (p == null || want <= 0) return 0;
                var u = Equipment.Unit(idx);
                if (u == null || u.Req == null) return 0;
                int cap = want;
                for (int r = 0; r < u.Req.Length; r++)
                {
                    int per = u.Req[r].Per100;
                    if (per <= 0) continue;
                    int stock = 0;
                    for (int i = 0; i < Equipment.All.Count; i++)
                    {
                        var e = Equipment.All[i];
                        if (e == null || !Equipment.MatchesTag(e, u.Req[r].Tag) || !IsUnlockedModel(e)) continue;
                        stock += Armory.Count(p, e.Id);
                    }
                    int m = stock * 100 / per;
                    if (m < cap) cap = m;
                }
                return cap < 0 ? 0 : cap;
            }
            catch { return 0; }
        }

        // 另一智能体接口的宽口径消费: AvailableKinds 返回可负担兵种集合(元素兼容 int/UnitDef/兵种 id)
        private static void CollectAvailable(MobileParty p, Settlement s, List<int> into)
        {
            try
            {
                object raw = LordArmy.AvailableKinds(p, s);
                var seq = raw as IEnumerable;
                if (seq == null) return;
                foreach (object item in seq)
                {
                    int idx = ToKindIdx(item);
                    if (idx >= 0 && idx < UnitCount && !into.Contains(idx)) into.Add(idx);
                }
            }
            catch { }
        }

        private static int ToKindIdx(object item)
        {
            try
            {
                if (item == null) return -1;
                if (item is int) return (int)item;
                if (item is byte) return (int)(byte)item;
                if (item is short) return (int)(short)item;
                var s = item as string;
                if (s != null) return Soldiers.UnitIndexOf(s);
                var ud = item as UnitDef;
                if (ud != null) return Equipment.Units.IndexOf(ud);
                return Convert.ToInt32(item);
            }
            catch { return -1; }
        }

        // ---------------- 小工具 ----------------
        private static void CurrentKinds(MobileParty p, Dictionary<int, int> into)
        {
            try
            {
                var rec = Soldiers.Ensure(p);
                if (rec == null) return;
                for (int i = 0; i < rec.List.Count; i++)
                {
                    int u = rec.List[i].Unit;
                    if (u < 0 || u >= UnitCount) continue;
                    int n;
                    into.TryGetValue(u, out n);
                    into[u] = n + 1;
                }
            }
            catch { }
        }

        private static int TotalOf(Dictionary<int, int> d)
        {
            int n = 0;
            try { foreach (var kv in d) n += kv.Value; } catch { }
            return n;
        }

        private static int ExpectedOf(MobileParty p, int current)
        {
            int lim = 0;
            try { lim = p.Party != null ? p.Party.PartySizeLimit : 0; } catch { }
            if (lim <= 0) lim = current + 20;
            if (lim < 10) lim = 10;
            return lim;
        }

        // 前线方向: 统一大脑 WarTargetId 优先(取其与本国最近的定居点), 否则战争计划主目标
        private static Settlement TargetSettlement(Kingdom k, int day)
        {
            try
            {
                Kingdom enemy = FindEnemyByBrain(k);
                if (enemy != null)
                {
                    Settlement best = null;
                    float bd = float.MaxValue;
                    foreach (var es in enemy.Settlements)
                    {
                        if (es == null) continue;
                        foreach (var os in k.Settlements)
                        {
                            if (os == null) continue;
                            float d = os.Position.Distance(es.Position);
                            if (d < bd) { bd = d; best = es; }
                        }
                    }
                    if (best != null) return best;
                }
                var plan = WarPlans.MainOf(k, day);
                if (plan == null) return null;
                return WarPlans.Find(plan.MainTargetId);
            }
            catch { return null; }
        }

        private static Kingdom FindEnemyByBrain(Kingdom k)
        {
            try
            {
                string tid = AiBrain.WarTargetId(k);
                if (string.IsNullOrEmpty(tid)) return null;
                foreach (var x in Kingdom.All)
                    if (x != null && !x.IsEliminated && x.StringId == tid) return x;
            }
            catch { }
            return null;
        }

        private static float Dist(MobileParty p, Settlement s)
        {
            try { return p.Position.Distance(s.Position); } catch { return float.MaxValue; }
        }

        private static string NameOf(Settlement s)
        {
            try { return s != null && s.Name != null ? s.Name.ToString() : "?"; } catch { return "?"; }
        }

        // 每国每月最多 2 条 "AI 领主:" 日志
        private static void Log(Kingdom k, string msg)
        {
            try
            {
                if (k == null || string.IsNullOrEmpty(msg)) return;
                int n;
                _logCount.TryGetValue(k.StringId, out n);
                if (n >= LogMax) return;
                _logCount[k.StringId] = n + 1;
                DLog.Force("AI 领主: " + (k.Name != null ? k.Name.ToString() : k.StringId) + " " + msg);
            }
            catch { }
        }
    }
}
