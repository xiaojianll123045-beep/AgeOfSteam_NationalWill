using System;
using System.Collections.Generic;
using System.Text;
using TaleWorlds.CampaignSystem;

namespace FeudalInternalAffairs
{
    // v4.268: 军事树里 23 项海军科技中, 除 navigation(港口建筑门槛) 与 drydocks(造船厂门槛) 之外的
    //   21 项, 原来只有面板上的一行数值加成(Mil=军事% / Tax=税收%), 没有任何海军侧效果。
    //   这里给每一项挂一个**结构化**的海军效果, 汇总后由 NavalFleet / NavalBehavior / NavyPanel 消费:
    //     BuildCost  造船(购船)成本乘数(1 = 不打折)
    //     BuildSpeed 造船速度乘数(1 = 基准; 影响我们自己的造船队列天数)
    //     Upkeep     舰船维护费乘数(1 = 不打折)
    //     FleetCap   舰队规模上限加成(条)
    //     Power      海战战力乘数(1 = 无加成)
    //     Blockade   封锁效率乘数(1 = 基准)
    //     Cargo      海运容量乘数(1 = 基准)
    //     Patrol     海上巡逻收益乘数(1 = 基准) —— v4.272: 真实落点 = 玩家王国港口的
    //                DLC 海上巡逻队生成周期 ÷ Patrol(见 Navy\NavalPatrol.cs), 不再只是文案
    //     Hull       解锁的最高船体档位(0=轻型 1=中型 2=重型; -1 = 不额外解锁)
    //                v4.272b: 与船坞等级的关系 = "船坞 3 级时才兑现解锁"(原来是 Math.Min -> 这 4 项是 no-op),
    //                详见 MaxHullTier 上方注释
    //   注意: 这 21 项**全部**有实际落点, 没有"落不上"的项(见报告第二部分)。
    //   汇总(Sum.Of)末尾会统一套一条压缩曲线, 见该函数上方注释。
    internal class NavalEffect
    {
        internal float BuildCost = 1f;
        internal float BuildSpeed = 1f;
        internal float Upkeep = 1f;
        internal int FleetCap = 0;
        internal float Power = 1f;
        internal float Blockade = 1f;
        internal float Cargo = 1f;
        internal float Patrol = 1f;
        internal int Hull = -1;
        internal string Text = "";      // 中文效果说明(面板显示)
    }

    internal static class NavalTech
    {
        // id -> 效果(顺序与 Research.cs 军事树一致)
        private static readonly Dictionary<string, NavalEffect> Table = new Dictionary<string, NavalEffect>();

        private static void P(string id, float buildCost, float buildSpeed, float upkeep, int fleetCap,
                              float power, float blockade, float cargo, float patrol, int hull, string text)
        {
            Table[id] = new NavalEffect
            {
                BuildCost = buildCost,
                BuildSpeed = buildSpeed,
                Upkeep = upkeep,
                FleetCap = fleetCap,
                Power = power,
                Blockade = blockade,
                Cargo = cargo,
                Patrol = patrol,
                Hull = hull,
                Text = text
            };
        }

        static NavalTech()
        {
            // ---- Era 1 ----
            P("admiralty", 1f, 1f, 0.95f, 2, 1f, 1f, 1f, 1f, -1,
              "海军部: 舰队上限 +2, 舰船维护费 -5%");
            P("paddle_steamer", 1f, 1.10f, 1f, 0, 1.05f, 1f, 1.10f, 1f, -1,
              "明轮船: 海战战力 +5%, 海运容量 +10%, 造船速度 +10%");
            // ---- Era 2 ----
            P("hydraulic_cranes", 0.95f, 1.15f, 1f, 0, 1f, 1f, 1f, 1f, -1,
              "液压起重机: 造船成本 -5%, 造船速度 +15%");
            P("power_of_the_purse", 1f, 1f, 0.90f, 0, 1f, 1.10f, 1f, 1f, -1,
              "财权: 舰船维护费 -10%, 封锁效率 +10%");
            P("screw_frigate", 1f, 1.05f, 1f, 0, 1.06f, 1f, 1.15f, 1f, -1,
              "螺旋桨护卫舰: 海战战力 +6%, 海运容量 +15%, 造船速度 +5%");
            // ---- Era 3 ----
            P("gantry_cranes", 0.90f, 1.20f, 1f, 0, 1f, 1f, 1f, 1f, -1,
              "龙门吊: 造船成本 -10%, 造船速度 +20%");
            P("floating_harbor", 1f, 1f, 0.97f, 1, 1f, 1.05f, 1.20f, 1.20f, -1,
              "浮式港口: 海运容量 +20%, 海上巡逻收益 +20%, 封锁效率 +5%, 舰队上限 +1, 维护费 -3%");
            P("ironclad", 0.97f, 1f, 1f, 0, 1.18f, 1.10f, 1f, 1f, 2,
              "铁甲舰: 海战战力 +18%, 封锁效率 +10%, 解锁重型船体(需 3 级造船厂)");
            P("monitor", 1f, 1f, 1f, 0, 1.09f, 1.25f, 1f, 1f, -1,
              "浅水炮舰: 海战战力 +9%, 封锁效率 +25%");
            P("self_torpedoes", 1f, 1f, 1f, 0, 1.09f, 1.15f, 1f, 1f, -1,
              "自航鱼雷: 海战战力 +9%, 封锁效率 +15%");
            P("jeune_ecole", 1f, 1f, 1f, 0, 1.10f, 1f, 1f, 1.15f, -1,
              "青年学派: 海战战力 +10%, 海上巡逻收益 +15%");
            // ---- Era 4 ----
            P("submarine", 1f, 1f, 1.05f, 0, 1.13f, 1.30f, 1f, 1f, -1,
              "潜艇: 海战战力 +13%, 封锁效率 +30%, 维护费 +5%");
            P("concrete_dockyards", 0.85f, 1.15f, 0.95f, 1, 1f, 1f, 1f, 1f, -1,
              "混凝土船坞: 造船成本 -15%, 造船速度 +15%, 维护费 -5%, 舰队上限 +1");
            P("pre_dreadnought", 0.95f, 1f, 1f, 0, 1.11f, 1.10f, 1f, 1f, 2,
              "前无畏舰: 海战战力 +11%, 封锁效率 +10%, 造船成本 -5%, 解锁重型船体");
            P("sea_lane_strategies", 1f, 1f, 0.95f, 1, 1.05f, 1.20f, 1.30f, 1.10f, -1,
              "海路战略: 海运容量 +30%, 封锁效率 +20%, 维护费 -5%, 舰队上限 +1, 战力 +5%");
            P("landing_craft", 1f, 1.10f, 1f, 0, 1.08f, 1.05f, 1.10f, 1f, -1,
              "登陆艇: 海战战力 +8%, 海运容量 +10%, 造船速度 +10%");
            P("dreadnought", 0.92f, 1.10f, 1f, 0, 1.14f, 1.15f, 1f, 1f, 2,
              "无畏舰: 海战战力 +14%, 封锁效率 +15%, 造船成本 -8%, 造船速度 +10%");
            // ---- Era 5 ----
            P("aircraft_carrier", 1f, 1f, 1.05f, 0, 1.15f, 1.20f, 1f, 1.25f, -1,
              "航空母舰: 海战战力 +15%, 封锁效率 +20%, 海上巡逻收益 +25%, 维护费 +5%");
            P("battleship", 0.95f, 1f, 1f, 0, 1.15f, 1.15f, 1f, 1f, 2,
              "战列舰: 海战战力 +15%, 封锁效率 +15%, 造船成本 -5%, 解锁重型船体");
            P("destroyer", 1f, 1.05f, 1f, 0, 1.13f, 1.20f, 1f, 1f, -1,
              "驱逐舰: 海战战力 +13%, 封锁效率 +20%, 造船速度 +5%");
            P("battlefleet_tactics", 0.95f, 1f, 0.95f, 2, 1.14f, 1.20f, 1.10f, 1.10f, -1,
              "现代舰队战术: 海战战力 +14%, 封锁效率 +20%, 海运容量 +10%, 舰队上限 +2, 维护费 -5%");
        }

        internal static NavalEffect Of(string id)
        {
            NavalEffect e;
            return (id != null && Table.TryGetValue(id, out e)) ? e : null;
        }

        internal static IEnumerable<string> Ids { get { return Table.Keys; } }

        // 已研究项的中文效果说明(面板"海军科技加成汇总"用)
        internal static List<string> DoneTexts(Kingdom k)
        {
            var list = new List<string>();
            try
            {
                foreach (var kv in Table)
                {
                    if (kv.Key == null) continue;
                    bool done = (k != null) ? Research.IsDone(k, kv.Key) : Research.IsDone(kv.Key);
                    if (!done) continue;
                    var def = Research.Find(kv.Key);
                    string nm = (def != null && !string.IsNullOrEmpty(def.Name)) ? def.Name : kv.Key;
                    list.Add(nm + " —— " + kv.Value.Text);
                }
                list.Sort(StringComparer.Ordinal);
            }
            catch { }
            return list;
        }

        // ==================== 汇总(某王国的海军科技总加成) ====================
        // 返回一个乘数包, 供 NavalFleet 计算成本/上限/战力/封锁/海运
        internal class Sum
        {
            internal float BuildCost = 1f;
            internal float BuildSpeed = 1f;
            internal float Upkeep = 1f;
            internal int FleetCap = 0;
            internal float Power = 1f;
            internal float Blockade = 1f;
            internal float Cargo = 1f;
            internal float Patrol = 1f;
            internal int Hull = -1;
            internal int Count = 0;
        }

        // v4.268: 不做缓存 —— 只有 21 项, 每项一次 HashSet 查找, 面板 3 秒刷新一次的开销可忽略;
        //   而缓存会让"当天研究完 ironclad 后造船页仍按旧上限"这种错一直持续到跨日。
        //
        // v4.272(数值压缩): 21 项全叠后原来肥得离谱 —— 战力 ×4.74 / 封锁 ×7.52 / 载货 ×2.39 /
        //   巡逻 ×2.09 / 造价 ×0.556 / 工期 ×2.33(−57%) / 维护 ×0.784 / 舰队上限 +7(陆军那条线
        //   刚因为同类问题整体打了 6 折)。这里在汇总**之后统一套一条压缩曲线**, 好处是:
        //     · 一处可调(只改下面那三个系数), 21 项之间的**相对强弱完全不变**;
        //     · 不需要逐项改表(改表会让强弱关系走样, 以后也没法整体调);
        //     · 面板显示的就是压缩后的真实值(Summary 走同一个 Of())。
        //   曲线(乘数类 v>1 偏强, 向 1 收缩):
        //     乘数类 (Power/Blockade/Cargo/Patrol/BuildSpeed): m' = 1 + (m − 1) × 0.45
        //     成本类 (BuildCost/Upkeep, 都 ≤1):              m' = 1 − (1 − m) × 0.50
        //     上限类 (FleetCap):                             n' = round(n × 0.5)
        //   即"超额部分只保留 45%/50%"。
        internal const float CompressGain = 0.45f;
        internal const float CompressCost = 0.50f;
        private static float CompressUp(float m) { return 1f + (m - 1f) * CompressGain; }
        private static float CompressDown(float m) { return 1f - (1f - m) * CompressCost; }

        internal static Sum Of(Kingdom k)
        {
            var s = new Sum();
            try
            {
                foreach (var kv in Table)
                {
                    if (kv.Value == null) continue;
                    bool done = (k != null) ? Research.IsDone(k, kv.Key) : Research.IsDone(kv.Key);
                    if (!done) continue;
                    var e = kv.Value;
                    s.Count++;
                    s.BuildCost *= e.BuildCost;
                    s.BuildSpeed *= e.BuildSpeed;
                    s.Upkeep *= e.Upkeep;
                    s.FleetCap += e.FleetCap;
                    s.Power *= e.Power;
                    s.Blockade *= e.Blockade;
                    s.Cargo *= e.Cargo;
                    s.Patrol *= e.Patrol;
                    if (e.Hull > s.Hull) s.Hull = e.Hull;
                }

                // v4.272: 统一压缩(曲线说明见函数上方注释)
                s.Power = CompressUp(s.Power);
                s.Blockade = CompressUp(s.Blockade);
                s.Cargo = CompressUp(s.Cargo);
                s.Patrol = CompressUp(s.Patrol);
                s.BuildSpeed = CompressUp(s.BuildSpeed);
                s.BuildCost = CompressDown(s.BuildCost);
                s.Upkeep = CompressDown(s.Upkeep);
                s.FleetCap = (int)Math.Round(s.FleetCap * CompressCost);
            }
            catch { }
            return s;
        }

        // 允许船坞建造的最高船体档位。
        //   DLC 原生规则: 船坞 1 级=轻型, 2 级=中型, 3 级=重型 (ShipProductionCampaignBehavior.CanTownCreateShipFromHull)
        //   v4.272 修审计 A9: 原实现用 Math.Min(byBuilding, sum.Hull) -> sum.Hull 永远被 byBuilding 压回,
        //     那 4 项科技(ironclad / pre_dreadnought / dreadnought / battleship)的"解锁重型船体"**全是 no-op**。
        //   v4.272b 保守版(用户决策 2): 科技只在"船坞已 3 级"时把重型档位兑现。
        //     · byBuilding < 0 (没船坞)      -> -1: 什么都造不了(建筑硬门槛)
        //     · byBuilding < 2 (船坞 1/2 级) -> 直接返回 byBuilding: 低等级**仍受建筑限制**,
        //       "3 级造船厂"这个建筑的投资价值完整保留(1 级船坞就算研究了铁甲舰也造不出重型);
        //     · byBuilding == 2 (船坞 3 级)  -> 取较大值: 有解锁科技 = 重型(2), 没科技 = 中型(1)。
        //   结果与 4 项科技的文案"解锁重型船体(需 3 级造船厂)"完全一致。
        internal static int MaxHullTier(int shipyardLevel, Sum sum)
        {
            try
            {
                int byBuilding = shipyardLevel >= 3 ? 2 : (shipyardLevel >= 2 ? 1 : (shipyardLevel >= 1 ? 0 : -1));
                if (byBuilding < 0) return -1;                      // 没有船坞 -> 什么都造不了(建筑硬门槛)
                if (byBuilding < 2) return byBuilding;              // 船坞不到 3 级 -> 科技解锁不外溢, 仍受建筑限制
                if (sum == null || sum.Hull < 0) return byBuilding;  // 3 级船坞 + 无解锁科技 -> 中型
                return Math.Max(byBuilding, sum.Hull);              // 3 级船坞 + 有解锁科技 -> 重型
            }
            catch { return -1; }
        }

        // 汇总面板文本
        internal static string Summary(Kingdom k)
        {
            try
            {
                var s = Of(k);
                if (s.Count == 0) return "海军科技: 尚未研究任何海军科技(军事树 · navigation 起)";
                var sb = new StringBuilder();
                sb.Append("已研究海军科技 ").Append(s.Count).Append("/21 项 · ");
                sb.Append("战力 ×").Append(s.Power.ToString("0.00"));
                sb.Append(" · 造船成本 ×").Append(s.BuildCost.ToString("0.00"));
                sb.Append(" · 造船速度 ×").Append(s.BuildSpeed.ToString("0.00"));
                sb.Append(" · 维护费 ×").Append(s.Upkeep.ToString("0.00"));
                sb.Append(" · 封锁效率 ×").Append(s.Blockade.ToString("0.00"));
                sb.Append(" · 海运 ×").Append(s.Cargo.ToString("0.00"));
                sb.Append(" · 巡逻 ×").Append(s.Patrol.ToString("0.00"));
                sb.Append(" · 舰队上限 +").Append(s.FleetCap);
                return sb.ToString();
            }
            catch { return "海军科技汇总不可用"; }
        }
    }
}
