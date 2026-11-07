using System;
using System.Collections.Generic;
using System.Text;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Naval;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace FeudalInternalAffairs
{
    // v4.268: 海战抽象结算层。
    //
    // 【为什么不调 DLC 的战斗模拟】调研结论: DLC **没有**独立的"海战自动结算"入口 ——
    //   它的自动结算就是原版坐镇系统本身(`CombatSimulationModel.SimulateHit(Ship, Ship, ...)` /
    //   `GetSimulationShips` / `MapEvent.IsNavalMapEvent`), 需要先有一场真实 `MapEvent`(海面位置)才跑得起来。
    //   外部 mod 无法凭空造一场位置在海上的 MapEvent 而不承担与 DLC  Missions/存档的耦合风险。
    //   -> 因此本层**自己做抽象结算**(舰船战力 + 海军科技 + 将领技能 -> 胜负/损失/战果),
    //      产出本 mod 格式的战报(NavalFleet.Battles, 由海军页展示), 并真实击沉敌舰(走官方 DestroyShipAction)。
    //      玩家真正需要的"进场景打一场"仍然完全由 DLC 原版提供(港口出海 -> 遭遇 -> 海战 Mission), 我们不去碰。
    internal static class NavalBattle
    {
        internal class Result
        {
            internal bool Won;
            internal float MyPower, EnemyPower;
            internal int MyShipsLost, EnemyShipsLost;
            internal int MyCrewLost, EnemyCrewLost;
            internal int Loot;
            internal float MyWear, EnemyWear;
            internal string Report = "";
            internal string Headline = "";
        }

        // 指挥官统率值: 玩家本人的战术/统御(海军技能由 DLC 提供, 这里不硬引用其类型)
        internal static int CommanderSkill()
        {
            try
            {
                var h = Hero.MainHero;
                if (h == null) return 60;
                int t = 0, l = 0;
                try { t = h.GetSkillValue(DefaultSkills.Tactics); } catch { }
                try { l = h.GetSkillValue(DefaultSkills.Leadership); } catch { }
                int v = (t * 2 + l) / 3;
                return v > 0 ? v : 60;
            }
            catch { return 60; }
        }

        private static string CommanderName()
        {
            try { return Hero.MainHero != null && Hero.MainHero.Name != null ? Hero.MainHero.Name.ToString() : "国家意志"; }
            catch { return "国家意志"; }
        }

        // 对某敌国港口发起一场舰队交战(抽象)。我方需要至少 1 条船。
        internal static Result Resolve(Kingdom mine, Kingdom enemy, Settlement targetPort)
        {
            var r = new Result();
            try
            {
                if (!NavalBridge.Usable) { r.Report = "未检测到海战 DLC, 无法进行海战结算"; return r; }
                if (mine == null || enemy == null) { r.Report = "参数错误"; return r; }

                // v4.275(第 3 项 ★): 交战状态前置校验。
                //   【原 bug】Resolve 直接开打 —— 玩家在"点出击"与"结算"之间若发生停战
                //   (和平决议/外交 AI/玩家自己议和), 就会对**非交战国**打一场海战:
                //   沉敌船、发战利品、加厌战全都落在一个已经和平的国家头上。
                //   现在入口先验交战国, 不满足就返回**明确的失败结果 + 中文原因**(Headline/Report 都填),
                //   绝不静默不执行(原来 UI 只能拿到一句"我方没有舰船"式的空结果, 看不出为什么没打)。
                if (mine == enemy) { r.Headline = "无法出击"; r.Report = "不能对自己宣战 —— 目标国就是本国"; return r; }
                if (!IsAtWarWith(mine, enemy))
                {
                    r.Headline = "无法出击";
                    r.Report = "已与该国停战, 无法出击(封锁页的国家关系已变, 请重新选敌港或再次宣战)";
                    DLog.Force("海军: 出击被拒 —— 与 " + (enemy.Name != null ? enemy.Name.ToString() : enemy.StringId)
                        + " 已停战(交战校验未通过), 未结算海战");
                    return r;
                }

                // v4.273(审计 #2): 我方舰队 = MyShips()(玩家主队/氏族/国防军/分裂队), **显式语义**, 不再靠参数猜
                var myShips = NavalFleet.MyShips();
                if (myShips.Count == 0) { r.Report = "我方没有舰船, 无法出海交战"; return r; }

                int skill = CommanderSkill();
                float admiral = 0.80f + skill / 250f;   // 技能 0 -> 0.80; 技能 100 -> 1.20
                // v4.270: 叠加海战 DLC 的**海军技能**(水手/水手长/船长)+ 海军特长的战力乘数。
                //   没装 DLC、或提督一项海军技能/特长都没有时 NavalAdmiral 返回 1.00 -> 原有数值一字不变。
                var admiralProf = NavalAdmiral.Of(Hero.MainHero);
                float navalMul = NavalAdmiral.PowerMul(admiralProf);
                admiral *= navalMul;

                // 我方战力: 舰队战力 × 提督系数(含海军技能) × 随机扰动
                r.MyPower = NavalFleet.FleetPower(mine) * admiral * Rand(0.90f, 1.10f);

                // 敌方战力: 该国舰队 + 目标港口守备(船坞等级越高守备越强)
                //   v4.273(审计 #2): 这里原来调 NavalFleet.FleetPower(enemy), 而 FleetPower 内部走 MyShips()
                //   -> "敌方战力"其实是我方战力(而且是**再乘一次**我方科技乘数), 海战变成打自己。
                //   现在直接对 ShipsOf(enemy)(全国口径)逐船算战力, 科技乘数用**敌方**自己的 NavalTech。
                var enShips = NavalFleet.ShipsOf(enemy);
                float eFleet = 0f;
                for (int i = 0; i < enShips.Count; i++) eFleet += NavalBridge.ShipPowerScore(enShips[i]);
                try { var es = NavalTech.Of(enemy); if (es != null) eFleet *= es.Power; } catch { }
                float ePort = 0f;
                if (targetPort != null)
                {
                    int lvl = NavalBridge.ShipyardLevel(targetPort);
                    ePort = (12f + lvl * 18f) * (1f + lvl * 0.2f);
                }
                r.EnemyPower = (eFleet + ePort) * Rand(0.90f, 1.10f);
                if (r.EnemyPower <= 0.01f) r.EnemyPower = 1f;

                // 胜负判定(边界给 5% 容差, 避免每次都变成纯数值比较)
                r.Won = r.MyPower >= r.EnemyPower * Rand(0.95f, 1.05f);

                // 损失比例
                float total = r.MyPower + r.EnemyPower;
                float myLossRatio = MBMath.ClampFloat(r.EnemyPower / total * 0.55f, 0.02f, 0.55f);
                float enLossRatio = MBMath.ClampFloat(r.MyPower / total * 0.55f, 0.05f, 0.75f);
                if (r.Won) { myLossRatio *= 0.6f; enLossRatio *= 1.3f; }
                else { myLossRatio *= 1.35f; enLossRatio *= 0.6f; }
                myLossRatio = MBMath.ClampFloat(myLossRatio, 0.02f, 0.85f);
                enLossRatio = MBMath.ClampFloat(enLossRatio, 0.02f, 0.90f);

                // 船员损失
                int myCrew = 0; for (int i = 0; i < myShips.Count; i++) myCrew += NavalBridge.Crew(myShips[i]);
                // v4.273(审计 #2): enShips 已在上面按 ShipsOf(enemy) 取好(原来这里再一次 Ships(enemy) = 我方舰队)
                int enCrew = 0; for (int i = 0; i < enShips.Count; i++) enCrew += NavalBridge.Crew(enShips[i]);
                r.MyCrewLost = (int)Math.Round(myCrew * myLossRatio);
                r.EnemyCrewLost = (int)Math.Round(enCrew * enLossRatio);

                // 真实击沉: 按损失比例从尾部(战力最低)开始沉船, 每边最多沉 3 条, 走官方 DestroyShipAction
                // v4.272(审计 A14): 两边都必须**排除我方舰队的船**, 否则一旦口径有漏(比如某支我方部队被
                //   临时算进敌国氏族列表), 就会出现"海战把自己的船击沉"。用 Ship.RandomValue 建白名单。
                var mineKeys = new HashSet<int>();
                for (int i = 0; i < myShips.Count; i++)
                {
                    try { int kk = myShips[i].RandomValue; if (kk != 0) mineKeys.Add(kk); } catch { }
                }
                r.MyShipsLost = SinkShips(myShips, myLossRatio, 3, true, null);
                // v4.273(审计 #2): 敌方沉没的必须是**敌方**的船(enShips = ShipsOf(enemy)),
                //   原来 enShips 就是我方舰队 —— 靠 excludeKeys 白名单勉强没自沉, 但"敌方损失"是假的。
                r.EnemyShipsLost = SinkShips(enShips, enLossRatio, 3, false, mineKeys);

                // 战果: 战胜按敌方损失折算战利品(进国库); 战败无收益
                if (r.Won)
                {
                    float worth = 0f;
                    for (int i = 0; i < enShips.Count; i++) worth += NavalBridge.HullValue(enShips[i]);
                    r.Loot = (int)Math.Round(worth * enLossRatio * 0.25f + 300f + r.EnemyCrewLost * 6f);
                    if (r.Loot > 0)
                    {
                        EconomyWorld.TreasuryAdd(r.Loot);
                        Fiscal.AddCourt(-r.Loot);   // 负值 = 收入(宫廷与外交口径: 正=支出, 负=收入)
                    }
                }

                // 厌战: 我方按损失上升(战败更多); 敌方按损失上升(战胜时更多)
                r.MyWear = 1.0f + myLossRatio * (r.Won ? 6f : 14f);
                r.EnemyWear = 1.0f + enLossRatio * (r.Won ? 14f : 6f);
                AddWear(mine, enemy, r.MyWear);
                AddWear(enemy, mine, r.EnemyWear);

                r.Headline = (r.Won ? "海战胜利" : "海战失利") + " · " + NavalBridge.NameOf(targetPort) + " 海域";
                r.Report = BuildReport(r, mine, enemy, targetPort, admiral, skill, navalMul, admiralProf);
                // v4.275(第 2 项): 海战战报带"第 N 天"前缀 —— 原来只存文本, 玩家看不出这是第几天打的。
                //   日期口径与全仓一致: Politics.Today() = (int)CampaignTime.Now.ToDays
                //   (World\BattleSim.cs 自己的 State.Day / 战报归档同样用它), 不另发明一种日期格式。
                //   注意前缀必须加在**整条战报的最前面**, 因为战报页(UI\NavyPanelVM.BuildLogRows)
                //   与 BattleSim 战报页(UI\BattleReportVM)都拿首行当标题。
                // v4.277(第 2 项): 再插入**来源标签**【推演】 —— 战报里同时存在两种来源:
                //   面板[出击]的抽象结算(本条)与地图上真实打的海战(Navy\NavalMapBattle.cs, 标【实战】)。
                //   不标清楚的话, 玩家会以为在面板上点一下就等于真打过了。
                r.Report = DayPrefix() + SourceTag() + r.Report;
                NavalFleet.AddBattle(r.Report);
                // v4.270: 海战战报**并入 BattleSim 的统一"近期战报"列表**(玩家不用在两处找战报)。
                //   约束: 只往它的 Recent(List<string>, 倒序, 上限 8)里插一条, **不碰 BattleSim 任何其它逻辑**。
                //   字段形状与它自己的战报完全一致: 单条 = 多行字符串, 首行是列表标题, 换行 \n;
                //   BattleSim.Save/Load 用 (char)1/(char)2 转义换行, 所以内容里不能出现这两个字符 ——
                //   本战报只由中文/数字/换行组成, 天然安全(下面仍做一次过滤兜底)。
                //   v4.275: 插入的**同一条字符串**(已含日期前缀), 两处列表口径完全一致, 不用各自补日期。
                try
                {
                    string forRecent = r.Report.Replace((char)1, ' ').Replace((char)2, ' ');
                    BattleSim.Recent.Insert(0, forRecent);
                    while (BattleSim.Recent.Count > 8) BattleSim.Recent.RemoveAt(BattleSim.Recent.Count - 1);
                }
                catch (Exception exIn) { DLog.Force("海军: 海战战报并入 BattleSim 失败(不影响海战本身) " + exIn.Message); }
                DLog.Force("海军: " + r.Headline + " 我战力 " + (int)r.MyPower + " vs 敌 " + (int)r.EnemyPower
                    + " 沉没 " + r.MyShipsLost + "/" + r.EnemyShipsLost + " 战利品 " + r.Loot);
            }
            catch (Exception ex)
            {
                r.Report = "海战结算异常: " + ex.Message;
                DLog.Force("海军: " + r.Report);
            }
            return r;
        }

        // 按损失比例击沉(从战力最低的开始); 返回实际击沉数
        //   excludeKeys: v4.272(A14) —— 这些 ShipKey 是"我方舰队", 绝对不沉(防误伤自家部队的船)
        private static int SinkShips(List<Ship> ships, float ratio, int cap, bool mine, HashSet<int> excludeKeys)
        {
            int n = 0;
            try
            {
                if (ships == null || ships.Count == 0) return 0;
                int want = (int)Math.Round(ships.Count * ratio);
                if (want <= 0) return 0;
                if (want > cap) want = cap;

                var sorted = new List<Ship>(ships);
                sorted.Sort(delegate (Ship a, Ship b)
                {
                    return NavalBridge.ShipPowerScore(a).CompareTo(NavalBridge.ShipPowerScore(b));
                });
                for (int i = 0; i < sorted.Count && n < want; i++)
                {
                    var s = sorted[i];
                    if (s == null) continue;
                    try
                    {
                        if (s.IsUsedByQuest) continue;      // 剧情船不动
                        if (excludeKeys != null)
                        {
                            int key = 0;
                            try { key = s.RandomValue; } catch { }
                            if (key != 0 && excludeKeys.Contains(key)) continue;   // 我方舰队: 不沉
                        }
                        DestroyShipAction.Apply(s);
                        n++;
                    }
                    catch { }
                }
            }
            catch { }
            return n;
        }

        private static void AddWear(Kingdom mine, Kingdom enemy, float v)
        {
            try
            {
                if (mine == null || enemy == null) return;
                string key = mine.StringId + ">" + enemy.StringId;
                float cur;
                if (!WarWeariness.Wear.TryGetValue(key, out cur)) cur = 0f;
                cur += v;
                if (cur > 100f) cur = 100f;
                WarWeariness.Wear[key] = cur;
            }
            catch { }
        }

        private static float Rand(float a, float b)
        {
            try { return a + (b - a) * MBRandom.RandomFloat; }
            catch { return (a + b) * 0.5f; }
        }

        // v4.275(第 3 项): 交战校验。包一层 try 是必须的 —— 王国对象可能处于"刚被灭国/刚成立"的
        //   半初始化状态, 直接 IsAtWarWith 有可能抛; 这时**按"没交战"处理**(宁可不打, 也不打错)。
        private static bool IsAtWarWith(Kingdom a, Kingdom b)
        {
            try
            {
                if (a == null || b == null) return false;
                return a.IsAtWarWith(b);
            }
            catch { return false; }
        }

        // v4.275(第 2 项): 战报日期前缀 —— 口径 = Politics.Today() = (int)CampaignTime.Now.ToDays,
        //   与 World\BattleSim.cs 的 State.Day / 战报归档用的同一天数口径(不另发明格式)。
        //   取不到时间时退化为"第 0 天", 绝不让前缀把整条战报搞没。
        private static string DayPrefix()
        {
            try { return "第 " + Politics.Today() + " 天 · "; }
            catch { return ""; }
        }

        // v4.277(第 2 项): 来源标签 —— 本入口是**面板抽象结算**, 一律标【推演】。
        //   与 NavalMapBattle 的【实战】成对; 玩家因此永远分得清"我在地图上真打过的"和"我在面板上推演的"。
        private static string SourceTag()
        {
            try { return "【" + NavalTaskForce.SourceSim + "】"; }
            catch { return "【推演】"; }
        }

        private static string BuildReport(Result r, Kingdom mine, Kingdom enemy, Settlement port, float admiral, int skill,
                                          float navalMul, NavalAdmiral.Profile prof)
        {
            try
            {
                var sb = new StringBuilder();
                sb.Append(r.Headline).Append('\n');
                sb.Append("我方: ").Append(mine.Name != null ? mine.Name.ToString() : mine.StringId);
                sb.Append(" · 提督 ").Append(CommanderName()).Append("(统率 ").Append(skill);
                // v4.270: 无 DLC / 无海军技能时不显示这一截, 免得战报里凭空多出"海军 ×1.00"的噪音
                if (navalMul > 1.0001f) sb.Append(", 海军 ×").Append(navalMul.ToString("0.00"));
                sb.Append(", 系数 ").Append(admiral.ToString("0.00")).Append(")\n");
                sb.Append("敌方: ").Append(enemy.Name != null ? enemy.Name.ToString() : enemy.StringId);
                if (port != null) sb.Append(" · 目标港 ").Append(NavalBridge.NameOf(port)).Append("(船坞 ").Append(NavalBridge.ShipyardLevel(port)).Append(" 级)");
                sb.Append('\n');
                sb.Append("战力: 我方 ").Append((int)r.MyPower).Append(" vs 敌方 ").Append((int)r.EnemyPower);
                sb.Append(r.Won ? "  →  我方占优\n" : "  →  敌方占优\n");
                sb.Append("损失: 我方沉没 ").Append(r.MyShipsLost).Append(" 艘 / 阵亡 ").Append(r.MyCrewLost).Append(" 人");
                sb.Append(" · 敌方沉没 ").Append(r.EnemyShipsLost).Append(" 艘 / 阵亡 ").Append(r.EnemyCrewLost).Append(" 人\n");
                if (r.Loot > 0) sb.Append("战利品: +").Append(r.Loot).Append(" 第纳尔(已入国库)\n");
                // v4.270: 提督的海军技能详情(有水手/水手长/船长或特长时才加这一行)
                if (prof != null && prof.Available)
                    sb.Append("海军: 水手 ").Append(prof.Mariner).Append(" · 水手长 ").Append(prof.Boatswain)
                      .Append(" · 船长 ").Append(prof.Shipmaster).Append(" · 特长 ").Append(prof.PerkTotal).Append(" 项\n");
                sb.Append("厌战: 我方 +").Append(r.MyWear.ToString("0.0")).Append(" · 敌方 +").Append(r.EnemyWear.ToString("0.0"));
                return sb.ToString();
            }
            catch { return r.Headline; }
        }
    }
}
