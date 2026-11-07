using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;

namespace FeudalInternalAffairs
{
    // v4.272: 海上巡逻收益(审计 A10)的真实落点。
    //
    // 【审计说的问题】NavalTech 里 Patrol 字段被 4 项科技(floating_harbor / jeune_ecole /
    //   aircraft_carrier / battlefleet_tactics)赋值汇总, 但**全仓没有任何地方读它** —— 面板上
    //   写着"海上巡逻收益 +20%", 实际什么也没发生。
    //
    // 【为什么不能直接调 DLC 的巡逻接口】DLC 的巡逻入口是
    //   NavalDLC.CampaignBehaviors.INavalPatrolPartiesCampaignBehavior(只有 GetSettlementPatrolStatus /
    //   GetNavalPatrolParty 两个**只读**方法, 没有任何"提高巡逻收益"的入口), 而且它定义的
    //   TaleWorlds.CampaignSystem.CampaignBehaviors.NavalPatrolPartiesCampaignBehavior 虽然名字是
    //   基础游戏命名空间, 其实**住在 NavalDLC.dll 里** —— 本项目不能静态引用 NavalDLC.dll
    //   (软依赖铁律: 没装 DLC 时不能 TypeLoadException)。
    //
    // 【真正的落点: 巡逻队数量】DLC 的海军巡逻队由 SettlementPatrolModel 驱动:
    //   · 每个沿海城镇(有 building_shipyard 的港口 + 王国通过"海岸卫队"法令)排队生成一支;
    //   · 等待时长 = GetPatrolPartySpawnDuration(settlement, naval:true);
    //   · DLC 的 NavalSettlementPatrolModel 覆盖它并返回随机 5~7 天。
    //   把这段等待时长除以 Patrol 系数 = **巡逻队数量与刷新频率同比例提升**,
    //   这正是"海上巡逻收益 ×N"该有的物理含义(巡逻力量 = 巡逻队密度), 且:
    //     · 不新增存档字段(改的是生成队列的时长, 原版自己会存);
    //     · 不碰 DLC 的修船/补船逻辑(不与它打架);
    //     · DLC 缺失时整段不生效(类型找不到 -> 直接跳过)。
    //   补丁只在**玩家王国**的定居点上生效(其他国家的巡逻节奏一字不动)。
    //
    // ==================== ★★ 硬前提(面板必须提示玩家) ★★ ====================
    //   DLC 的海军巡逻队**不是无条件生成的**(反编译 NavalDLC.dll 实证):
    //     · NavalSettlementPatrolModel.CanSettlementHavePatrolParties(settlement, naval:true) 要求:
    //         定居点 IsTown && HasPort && OwnerClan != null && !OwnerClan.IsRebelClan
    //         && OwnerClan.Kingdom != null && **HasCoastalEdict(OwnerClan.Kingdom)**
    //       —— HasCoastalEdict 就是 `kingdom.HasPolicy(NavalPolicies.CoastalGuardEdict)`
    //          = 王国**通过了「海岸卫队」法令**(DLC 的王国政策, 需要玩家在王国决策里推)。
    //     · NavalPatrolPartiesCampaignBehavior.OnNewGameCreated / DailyTickSettlement 按这个判定排队生成
    //       (生成周期 = GetPatrolPartySpawnDuration, DLC 返回随机 5~7 天)。
    //   ⇒ **玩家王国如果没有「海岸卫队」法令, 海上巡逻队数量恒为 0, 这 4 项科技
    //      (floating_harbor / jeune_ecole / aircraft_carrier / battlefleet_tactics) 的巡逻加成就是零效果。**
    //      这不是我们的 bug, 是 DLC 的原生前置条件(我们只是把生成周期除以系数, 不能凭空造出巡逻队)。
    //   ⇒ UI 侧请在舰队页/科技汇总处给一句提示, 例如:
    //      "海上巡逻加成需王国法令「海岸卫队」; 当前状态: 已通过/未通过(巡逻队 N 支)"
    //      可用 NavalFleet.PatrolParties(在航巡逻队数)判断: == 0 且已研究巡逻类科技时提示玩家去推法令。
    //
    //   另: 我们把"巡逻收益"实现为**生成周期 ÷ 系数**(即巡逻队密度 ×N), 而不是"给巡逻队加船/加人" ——
    //   后者会与 DLC 的"巡逻队回港时把船补到模板上限"(NavalPatrolPartiesCampaignBehavior.SettlementEntered
    //   里的 `if (num < item.MaxValue)`)持续打架(我们加、它削, 或者无限膨胀), 所以选了不打架的那条。
    internal static class NavalPatrol
    {
        private static bool _applied;

        // 当前生效的巡逻系数(= NavalTech.Sum.Patrol, 1 = 无加成)。0 表示"还没刷新过"。
        private static float _mul = 0f;

        // DLC 的巡逻模型类型(反射拿, 不静态引用 DLC 程序集)
        internal static Type ModelType
        {
            get
            {
                try { return AccessTools.TypeByName("NavalDLC.GameComponents.NavalSettlementPatrolModel"); }
                catch { return null; }
            }
        }

        internal static void Apply(Harmony harmony)
        {
            try
            {
                if (_applied) return;
                _applied = true;   // 只试一次(不论成败, 避免每次日结都反射找类型)
                if (harmony == null) return;

                var t = ModelType;
                if (t == null) { DLog.Info("海军巡逻: 未检测到 DLC 巡逻模型, 巡逻科技加成不生效(不影响其它系统)"); return; }

                var m = AccessTools.Method(t, "GetPatrolPartySpawnDuration");
                if (m == null) { DLog.Force("海军巡逻: 未找到 GetPatrolPartySpawnDuration, 跳过"); return; }

                harmony.Patch(m, postfix: new HarmonyMethod(AccessTools.Method(typeof(NavalPatrol), "Postfix")));
                DLog.Force("海军巡逻: 已挂生成周期补丁(玩家王国港口巡逻队刷新加速)");
            }
            catch (Exception ex) { DLog.Force("海军巡逻: 挂补丁失败 " + ex.Message); }
        }

        // 刷新系数(每个游戏日一次)。返回当前值。
        internal static float Refresh(Kingdom k)
        {
            try
            {
                float v = 1f;
                var s = NavalTech.Of(k);
                if (s != null && s.Patrol > 0f) v = s.Patrol;
                _mul = v;
                return v;
            }
            catch { _mul = 1f; return 1f; }
        }

        internal static float CurrentMul { get { return _mul <= 0f ? 1f : _mul; } }

        // v4.272(审计 A5): 新战役 —— 系数归零(下一交易日会由 PatrolDaily 重算)
        internal static void Reset()
        {
            try { _mul = 0f; } catch { }
        }

        // 目标定居点是不是"玩家王国的" —— 只有自己国家的巡逻节奏才吃科技加成
        private static bool Mine(Settlement st)
        {
            try
            {
                if (st == null || st.OwnerClan == null) return false;
                var k = PlayerKingdom();
                return k != null && ReferenceEquals(st.OwnerClan.Kingdom, k);
            }
            catch { return false; }
        }

        private static Kingdom PlayerKingdom()
        {
            try
            {
                var b = NationalWillOrders.Behavior;
                return b != null ? b.NationKingdom : null;
            }
            catch { return null; }
        }

        // Postfix 参数名 __0 = 原方法的第 1 个参数(settlement)。
        //   用位置名而不是参数名, 是为了不受 DLC 内部参数命名变化的影响。
        //
        // CampaignTime 的语义(反编译确认): 内部 _numTicks 是**绝对时刻**(CurrentTicks 基准),
        //   所以不能直接改 NumTicks; 正确做法是按"距离现在还有多久"来缩短:
        //     原时长 = __result.ToMilliseconds - 现在.ToMilliseconds
        //     新时刻 = 现在 + 原时长 / mul
        private static void Postfix(ref CampaignTime __result, Settlement __0)
        {
            try
            {
                if (__0 == null) return;
                float mul = CurrentMul;
                if (mul <= 1.0001f) return;          // 没科技 -> 一字不改
                if (!Mine(__0)) return;              // 只有玩家王国的港口

                double now = CampaignTime.Now.ToMilliseconds;
                double end = __result.ToMilliseconds;
                double full = end - now;             // 原方法返回的是"从现在起 N 毫秒后"
                if (full <= 0.0) return;             // 已经过期(排到了) -> 不动
                double shortened = full / mul;
                if (shortened < 1.0) shortened = 1.0;
                if (shortened >= full) return;
                __result = CampaignTime.MillisecondsFromNow((long)Math.Round(shortened));
            }
            catch { }
        }

        // ==================== 只读统计(供面板显示"巡逻队 N 支") ====================
        //   用基础游戏的 MobileParty 属性遍历: IsPatrolParty = 组件是 PatrolPartyComponent,
        //   PatrolPartyComponent.IsNaval / HomeSettlement 都在基础游戏程序集里。
        internal static int CountMine()
        {
            int n = 0;
            try
            {
                var k = PlayerKingdom();
                if (k == null) return 0;
                var all = MobileParty.All;
                if (all == null) return 0;
                for (int i = 0; i < all.Count; i++)
                {
                    var p = all[i];
                    if (p == null || !p.IsActive || !p.IsPatrolParty) continue;
                    var pc = p.PatrolPartyComponent;
                    if (pc == null || !pc.IsNaval) continue;       // 只看海上巡逻队
                    var home = p.HomeSettlement;
                    if (home == null || home.OwnerClan == null) continue;
                    if (!ReferenceEquals(home.OwnerClan.Kingdom, k)) continue;
                    n++;
                }
            }
            catch { }
            return n;
        }

        // 我方海上巡逻队的总船只数 / 总载员(巡逻强度的直观量)
        internal static void Strength(out int parties, out int ships, out int crew)
        {
            parties = 0; ships = 0; crew = 0;
            try
            {
                var k = PlayerKingdom();
                if (k == null) return;
                var all = MobileParty.All;
                if (all == null) return;
                for (int i = 0; i < all.Count; i++)
                {
                    var p = all[i];
                    if (p == null || !p.IsActive || !p.IsPatrolParty) continue;
                    var pc = p.PatrolPartyComponent;
                    if (pc == null || !pc.IsNaval) continue;
                    var home = p.HomeSettlement;
                    if (home == null || home.OwnerClan == null) continue;
                    if (!ReferenceEquals(home.OwnerClan.Kingdom, k)) continue;
                    parties++;
                    var ss = NavalBridge.ShipsOf(p);
                    ships += ss.Count;
                    for (int j = 0; j < ss.Count; j++) crew += NavalBridge.Crew(ss[j]);
                }
            }
            catch { }
        }
    }
}
