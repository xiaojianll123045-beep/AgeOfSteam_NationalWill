using System;
using System.Collections.Generic;
using System.Text;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Naval;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;

namespace FeudalInternalAffairs
{
    // v4.270: 海运 —— 用舰队把国防军军团运过海(海军系统的核心价值)。
    //
    // ==================== 为什么是"自己写移动"而不是"注入 DLC 港口菜单" ====================
    // 反编译实证(NavalDLC.dll / TaleWorlds.CampaignSystem.dll):
    //   · DLC 的 NavalTransitionCampaignBehavior 全是**港口菜单**(town 菜单里插 "Go to the port" ->
    //     port_menu -> "Set sail"), 它的出海动作就是 `MainParty.SetSailAtPosition(Settlement.CurrentSettlement.PortPosition)`,
    //     而且**只作用于玩家自己的部队**(MainParty); 它没有任何"运别人的军团"的 public 入口。
    //   · 真正缺的那块能力其实在**基础游戏**里, 而且全是 public:
    //       MobileParty.SetSailAtPosition(CampaignVec2)      把一支部队放到海上(IsCurrentlyAtSea = true)
    //       MobileParty.DisembarkToPosition(CampaignVec2)    让它下船(IsCurrentlyAtSea = false)
    //       MobileParty.SetTargetSettlement(S, true)         目标 = 该港的 PortPosition(isTargetingPort)
    //       MobileParty.Anchor / AnchorPoint.CallFleet(S)    舰队锚点(public)
    //       ChangeShipOwnerAction.ApplyByTransferring(p, s)  移船(基础游戏 Action, DLC 自己也在用)
    //       MapDistanceModel.GetDistance(S, S, true, true, NavigationType.Naval)   海上距离
    //       PartyTransitionModel.GetFleetTravelTimeToSettlement / GetTransitionTimeForEmbarking(基础游戏接口)
    //   · DLC 覆盖的 NavalPartyNavigationModel.HasNavalNavigationCapability(party) 的判定是
    //     **`party.Ships.Count > 0`**(或挂靠在有船的部队上) —— 也就是说:
    //     "一支 AI 部队只要名下有船, 就在原版眼里是**合法的海上部队**"(DLC 的领主舰队/巡逻队/渔船全是这个状态)。
    //     => 我军团的出海不需要发明任何非法状态: 把船划给它, 它就真的是一支运兵船队, 走原版导航。
    //   · 因此本层**不注入任何菜单、不覆盖任何 GameModel、不 patch 原版 Tick**, 只用上面的 public 原语,
    //     所以不存在与 DLC 的 PartyNavigationModel/PartyTransitionModel 覆盖冲突的问题(我们只读不写)。
    //
    // ==================== 与"原版海战流程"的边界 ====================
    //   · 玩家自己进场景打海战的原版流程(港口出海 -> 遭遇 -> 海战 Mission)**完全不碰**。
    //   · 我们只把**军团部队**(mod 自己的 MobileParty)变成海上部队; 它在海上被敌方舰队截住会触发原版海战,
    //     这是原版支持的状态(和 AI 领主出海一模一样), 也正是"运兵船队会被拦截"该有的玩法。
    //   · 唯一需要我们自己兜的是: 军团在海上"到不了/船沉了/超时"的情况 -> 见 Tick() 的超时强制靠港。
    internal static class NavalTransport
    {
        // ==================== 运输队状态 ====================
        internal class Convoy
        {
            internal string PartyId = "";
            internal string LegionName = "";
            internal int LegionMen = 0;
            internal string FromPortId = "";
            internal string FromPortName = "";
            internal string ToPortId = "";
            internal string ToPortName = "";
            internal int DepartDay = 0;
            internal int EtaDays = 1;
            internal int ShipsGiven = 0;
            internal int TroopCap = 0;          // 出发时这批船的运兵能力(人)
            internal float CargoUsed = 0f;      // 占用的货舱(载重)
            internal int Cost = 0;
            internal bool PrevPlayerHold = false;   // 出发前军团的"玩家命令锁定"状态(到达后还原)
            internal readonly List<int> ShipIds = new List<int>();   // 划出去的船的 ShipKey(见 ShipKey)
            // v4.272(审计 A3): 每条船**原来的主人**(PartyBase.Id = MobileParty.StringId)。
            //   还船时必须按原主还, 否则"海运 -> 召回"反复操作可以把全国 AI 领主的船搬进玩家部队
            //   (原来 ReturnShips 一律走 ReceiverParty -> 永远命中玩家主队 = 零成本洗船漏洞)。
            internal readonly Dictionary<int, string> ShipOwner = new Dictionary<int, string>();
            // v4.273(A-1 海上损耗): 因"划出去的船耐久见底"而提前靠港的截止日(0 = 未触发)。
            //   月/日粒度只用于面板说明与日志, 真正判定在 Tick 里按 Ship.HitPoints 实时算。
            internal int AbortDay = 0;

            internal string Line
            {
                get
                {
                    return LegionName + " " + FromPortName + " → " + ToPortName
                        + " · 载 " + LegionMen + " 人/" + ShipsGiven + " 船 · 出发第 " + DepartDay + " 天 · 约 " + EtaDays + " 天";
                }
            }
        }

        internal static readonly List<Convoy> Active = new List<Convoy>();
        internal const int MaxConvoys = 3;                  // 同时最多 3 支运输队
        internal const float LoadRadius = 20f;              // 军团与出发港的最大装载距离
        internal const int CostPerMan = 2;                  // 海运补给费/人
        internal const int CostPerShip = 20;                // 水手与补给/船

        // ==================== v4.273(致命修复 #1): 存档字段顺序的**唯一真源** ====================
        //   【原 bug】Serialize 写 15 个字段, LoadSegment 按 13 字段读(且只判 f.Length < 14),
        //   而实际记录里只有 13 个 '|' ⇒ 索引整体错位 + 字段数判定不成立 ⇒ **每一条**记录都被丢弃,
        //   表现为"读档后海运静默消失", 且船列表/原主映射全丢。
        //   【修法】写入与读取**共用下面这套索引常量**(顺序即存档格式), 两边都不许再写字面量下标。
        //   字段顺序(写入即读取, 索引 0..14):
        //     CS_PartyId(0) | CS_LegionName(1) | CS_LegionMen(2) | CS_FromPortId(3) | CS_FromPortName(4)
        //     | CS_ToPortId(5) | CS_ToPortName(6) | CS_DepartDay(7) | CS_EtaDays(8) | CS_TroopCap(9)
        //     | CS_CargoUsed(10) | CS_Cost(11) | CS_PrevPlayerHold(12) | CS_Ships(13) | CS_AbortDay(14)
        //   ★ 改这个顺序 = 改存档格式: 只能**在末尾追加**, 且 LoadSegment 必须同时放宽最小字段数判定。
        private const int CS_PartyId = 0;
        private const int CS_LegionName = 1;
        private const int CS_LegionMen = 2;
        private const int CS_FromPortId = 3;
        private const int CS_FromPortName = 4;
        private const int CS_ToPortId = 5;
        private const int CS_ToPortName = 6;
        private const int CS_DepartDay = 7;
        private const int CS_EtaDays = 8;
        private const int CS_TroopCap = 9;
        private const int CS_CargoUsed = 10;
        private const int CS_Cost = 11;
        private const int CS_PrevPlayerHold = 12;
        private const int CS_Ships = 13;
        private const int CS_AbortDay = 14;                  // v4.273(A-1): 提前靠港的截止日(0 = 未触发)
        private const int ConvoyFieldCountWrite = 15;        // 写侧字段总数(必须 == 上面最大索引 + 1)
        // 读侧最小可接受字段数: 低于它就是**残缺记录**, 只能丢弃(并把字段数打进日志便于排查老档)
        private const int ConvoyFieldCountMin = 13;

        // ==================== 可选目标(供面板) ====================

        // 可海运的军团: 我方军团 + 部队健在 + 在陆上 + 没在打仗 + 不在运输中
        internal static List<DefLegion> ReadyLegions()
        {
            var list = new List<DefLegion>();
            try
            {
                for (int i = 0; i < DefArmy.Legions.Count; i++)
                {
                    var lg = DefArmy.Legions[i];
                    if (lg == null) continue;
                    var p = DefArmy.LegionParty(lg);
                    if (p == null || !p.IsActive) continue;
                    if (p.IsCurrentlyAtSea) continue;                  // 已经在海上
                    if (p.MapEvent != null) continue;                  // 交战中
                    if (InConvoy(lg.PartyId)) continue;                // 已在运输中
                    list.Add(lg);
                }
            }
            catch { }
            return list;
        }

        // v4.272: 海运中的判定(公开查询) —— DefArmyAi.AiGuard(放行海运军团) 与 DefArmy.EnsureNavigation
        //   (海运中不改导航) 都调它, 所以对 null/空 id 必须稳(宁可不放行, 也不能放行一支无关部队)。
        internal static bool InConvoy(string partyId)
        {
            try
            {
                if (string.IsNullOrEmpty(partyId)) return false;
                for (int i = 0; i < Active.Count; i++)
                {
                    var c = Active[i];
                    if (c != null && c.PartyId == partyId) return true;
                }
            }
            catch { }
            return false;
        }

        // 舰船的稳定标识: Ship 是 sealed class 且**没有 StringId**, 但它的 RandomValue 是
        //   [SaveableProperty(11)] 的进程内唯一随机值(存档持久), 用它标记"这次划出去的那几条船"最稳。
        //   (不能用 ShipHull.StringId —— 同型号的船会重复)
        internal static int ShipKey(Ship s)
        {
            try { return s != null ? s.RandomValue : 0; } catch { return 0; }
        }

        internal static Convoy ConvoyOf(string partyId)
        {
            try
            {
                for (int i = 0; i < Active.Count; i++) if (Active[i].PartyId == partyId) return Active[i];
            }
            catch { }
            return null;
        }

        // 目的港候选: 本国或中立(未交战)的沿海城镇; 出发港必须是本国港口
        internal static List<Settlement> DestinationPorts(Kingdom k)
        {
            var list = new List<Settlement>();
            try
            {
                foreach (var s in NavalBridge.AllPorts())
                {
                    if (s == null) continue;
                    var owner = NavalBridge.KingdomOf(s);
                    if (owner == null) continue;
                    if (owner != k && k.IsAtWarWith(owner)) continue;   // 敌港不能作为海运目的地(那是登陆战, 另有系统)
                    list.Add(s);
                }
            }
            catch { }
            return list;
        }

        // ==================== 估算 ====================

        internal static int LegionMenOf(DefLegion lg)
        {
            try { return DefArmy.LegionMen(lg); } catch { return 0; }
        }

        internal static string LegionLabel(DefLegion lg)
        {
            try
            {
                if (lg == null) return "?";
                string s = "第 " + lg.Number + " 军团";
                var p = DefArmy.LegionParty(lg);
                try
                {
                    if (p != null && p.LeaderHero != null && p.LeaderHero.Name != null)
                        s += "(" + p.LeaderHero.Name.ToString() + ")";
                }
                catch { }
                return s;
            }
            catch { return "?"; }
        }

        // 海上行程(天): 与 DLC NavalDLCPartyTransitionModel.GetAnchorReachDurationInHours 同口径
        //   distance^0.95 / 35 小时, 下限 3 小时, 上限 48 小时/段 —— 这里按"距离/35/24"折成天。
        //   v4.273(B-5): 加 lg 参数 —— 诺德船速特性(+10%)在我们的海运里也要生效(见 NavalCulture)。
        internal static int EtaDays(Settlement from, Settlement to) { return EtaDays(from, to, null); }

        internal static int EtaDays(Settlement from, Settlement to, DefLegion lg)
        {
            try
            {
                if (from == null || to == null) return 1;
                float dist = 0f;
                try
                {
                    dist = Campaign.Current.Models.MapDistanceModel.GetDistance(
                        from, to, true, true, MobileParty.NavigationType.Naval);
                }
                catch { }
                if (dist <= 0f)
                {
                    // 兜底: 港口坐标直线距离(没有海上路径数据时也不至于算出 0)
                    try { dist = from.PortPosition.Distance(to.PortPosition) * 1.6f; } catch { dist = 60f; }
                }
                float hours = (float)Math.Pow(Math.Max(1f, dist), 0.95) / 35f;
                // v4.273(B-5): 诺德"河流与沿海船速 +10%" -> 同一段距离用时 ÷1.1
                float cultureMul = 1f;
                try
                {
                    var cp = lg != null ? DefArmy.LegionParty(lg) : null;
                    cultureMul = NavalCulture.SailTimeMul(cp);
                    if (cultureMul <= 0f || cultureMul > 1f) cultureMul = 1f;   // 只接受"更快", 不被放大
                }
                catch { cultureMul = 1f; }
                hours *= cultureMul;
                int days = (int)Math.Round(hours / 24f);
                if (days < 1) days = 1;
                if (days > 30) days = 30;
                // v4.273(B-3 海上风暴): 航线穿过在航风暴 -> 航程按比例拉长。
                //   这是**我们自己的估算口径**(用于面板提示与超时兜底), 不额外造扣血/扣钱效果 ——
                //   风暴区内的真实损伤由 DLC 自己按小时结算(见 NavalSeaRisk 的长注释)。
                days += NavalSeaRisk.StormDelayDays(from.PortPosition.ToVec2(), to.PortPosition.ToVec2(), days);
                if (days < 1) days = 1;
                if (days > 40) days = 40;   // 含风暴在内仍设上限(原来 30)
                return days;
            }
            catch { return 2; }
        }

        // 军团离出发港多远(用港口城门坐标, 军团在陆上)
        internal static float DistanceToPort(MobileParty p, Settlement port)
        {
            try
            {
                if (p == null || port == null) return 9999f;
                return p.Position.Distance(port.GatePosition);
            }
            catch { return 9999f; }
        }

        // 出发预检: 返回 "" = 可以出发; 否则是拒绝原因
        //   cap/need 为运力与需求(人都按"人"算), cost 为补给费
        //
        // v4.272(追加需求 C): **能算出来的就先算** —— 原来函数一进来就把 need/cap/eta/cost/ships 全置 0,
        //   只有走到最后一步才赋值, 于是"军团距港口太远"/"已在海运途中"/"军团已在海上"这些**失败分支**
        //   返回时, UI 拿到的 cap/need 都是 0, 判语带只能显示一句干巴巴的原因(看不到"要多少运力/多少天/多少钱")。
        //   现在顺序是: 基础检查 -> 立刻算 需求/运力/用船数 -> 立刻算 ETA/费用 -> 再做"位置/状态"类检查。
        //   注: 军团/港口/目的港成立时这些值就一定算得出来; 早期 return(没 DLC / 没立国 / 没港口)时
        //   保持 0 是**正确**的(那时确实无从计算)。
        internal static string Precheck(DefLegion lg, Settlement from, Settlement to, Kingdom k,
                                        out int need, out int cap, out int eta, out int cost, out int ships)
        {
            float ignoredSafe;
            return Precheck(lg, from, to, k, out need, out cap, out eta, out cost, out ships, out ignoredSafe);
        }

        // v4.273(A-1): 带"安全航行天数"的重载 —— 面板需要 N 来显示"主力舰预计可安全航行 N 天 / 本航程约 M 天"。
        //   safeDays < 0 = 读不到原版模型(没装 DLC / 模型缺失) -> 不显示也不拦人。
        internal static string Precheck(DefLegion lg, Settlement from, Settlement to, Kingdom k,
                                        out int need, out int cap, out int eta, out int cost, out int ships,
                                        out float safeDays)
        {
            need = 0; cap = 0; eta = 0; cost = 0; ships = 0; safeDays = -1f;
            try
            {
                if (!NavalBridge.Usable) return "未检测到海战 DLC, 海运不可用";
                if (k == null) return "尚未立国";
                if (lg == null) return "没有可选的军团";
                if (from == null) return "没有可用的出发港(需要一处我方沿海城镇)";
                if (to == null) return "没有可选的目的港";
                if (from == to) return "出发港与目的港相同";

                var fromOwner = NavalBridge.KingdomOf(from);
                if (fromOwner != k) return "出发港「" + NavalBridge.NameOf(from) + "」不是我国港口";

                var p = DefArmy.LegionParty(lg);
                if (p == null || !p.IsActive) return "该军团部队不存在(可能已被歼灭)";

                // ---- ① 先算: 运力需求 + 我方可用舰船 + 用船数 + 运兵能力 ----
                need = LegionMenOf(lg);
                var pool = CargoShips(k);
                int acc = 0, n = 0;
                float cargo = 0f;
                for (int i = 0; i < pool.Count; i++)
                {
                    acc += NavalBridge.Crew(pool[i]);
                    cargo += NavalBridge.Cargo(pool[i]);
                    n++;
                    if (need > 0 && acc >= need) break;
                }
                float mul = 1f;
                try { var s = NavalTech.Of(k); if (s != null) mul *= s.Cargo; } catch { }
                mul *= NavalAdmiral.TroopMul(NavalAdmiral.Of(Hero.MainHero));
                cap = (int)Math.Round(acc * mul);
                ships = n;

                // ---- ② 再算: 航程(天) 与 补给费 ----
                eta = EtaDays(from, to, lg);   // v4.273(B-5): 传入军团, 让诺德船速特性计入航程
                if (need > 0) cost = need * CostPerMan + ships * CostPerShip;

                // ---- ③ 最后做"位置/状态"类检查(它们的失败也要带上上面算好的数字) ----
                if (p.IsCurrentlyAtSea) return "该军团已经在海上";
                if (p.MapEvent != null) return "该军团正在交战, 无法装船";
                if (InConvoy(lg.PartyId)) return "该军团已在海运途中";
                if (Active.Count >= MaxConvoys) return "同时最多 " + MaxConvoys + " 支运输队";
                if (need <= 0) return "该军团没有可运输的兵员";
                if (cap < need)
                    return "运力不足: 舰队可载 " + cap + " 人, 该军团 " + need + " 人(需更多舰船或研究海军科技)";

                float d = DistanceToPort(p, from);
                if (d > LoadRadius)
                    return "该军团距「" + NavalBridge.NameOf(from) + "」太远(先把军团调往该港附近再装船)";

                // ---- ④ v4.273(A-1 海上损耗): 最后校验"这批船撑不撑得到目的港" ----
                //   只读原版 CampaignShipDamageModel.GetEstimatedSafeSailDuration, 不自己算损耗公式。
                //   拿不到值时 safeDays = -1 且本函数返回 ""(绝不误拦)。
                string risk = NavalSeaRisk.CheckSafe(p, eta, out safeDays);
                if (!string.IsNullOrEmpty(risk)) return risk;

                return "";
            }
            catch (Exception ex) { return "预检异常: " + ex.Message; }
        }

        // 可用于海运的船: **我方舰队**里非剧情船, 按载员降序(大船优先, 减少占用船只数)
        //   v4.272(审计 A3): 原来用 NavalBridge.ShipsOf(k) = **全国所有氏族战帮的船**(含 AI 领主),
        //   而还船只还到玩家部队 -> 反复"海运→召回"可零成本把全国 AI 领主的船搬进玩家部队("洗船")。
        //   改成我方口径(NavalFleet.MyShips = 玩家主队 + 玩家氏族 + 国防军 + 部队管理分裂队, 见 NavalBridge.MyShips)。
        //   v4.273(审计 #2): 这里从 `NavalFleet.Ships(k)` 改成显式的 `NavalFleet.MyShips()` ——
        //   原 Ships(k) 忽略参数、无条件返回我方舰队, 语义全靠猜; 现在调用点自己说清要哪一边。
        private static List<Ship> CargoShips(Kingdom k)
        {
            var list = new List<Ship>();
            try
            {
                var all = NavalFleet.MyShips();   // v4.273(审计 #2): 我方口径显式化(原来 Ships(k) 靠参数猜)
                for (int i = 0; i < all.Count; i++)
                {
                    var s = all[i];
                    if (s == null) continue;
                    bool quest = false;
                    try { quest = s.IsUsedByQuest; } catch { }
                    if (quest) continue;
                    // v4.276(船源互斥): **已经被舰队占用的船不能再借给陆军军团**。
                    //   舰队(NavalTaskForce)与海运都会把船过户到自己名下, 同一艘船被两套系统同时
                    //   记账 -> 到达后还船错乱/船数凭空翻倍。判据 = 该船是否登记在某支舰队的
                    //   ShipOwner 表里(见 NavalTaskForce.ShipBlockedByFleet)。
                    if (NavalTaskForce.ShipBlockedByFleet(ShipKey(s))) continue;
                    list.Add(s);
                }
                list.Sort(delegate (Ship a, Ship b)
                {
                    int c = NavalBridge.Crew(b).CompareTo(NavalBridge.Crew(a));
                    if (c != 0) return c;
                    // 稳定排序: List.Sort 不稳定, 同载员时按 id 定序, 免得每次选中的船不同
                    return string.CompareOrdinal(NavalBridge.HullId(a), NavalBridge.HullId(b));
                });
            }
            catch { }
            return list;
        }

        // ==================== 出发 ====================
        internal static string Depart(DefLegion lg, Settlement from, Settlement to, Kingdom k)
        {
            try
            {
                int need, cap, eta, cost, ships;
                string why = Precheck(lg, from, to, k, out need, out cap, out eta, out cost, out ships);
                if (!string.IsNullOrEmpty(why)) return why;

                var p = DefArmy.LegionParty(lg);
                if (p == null) return "军团部队不存在";
                if (EconomyWorld.Treasury.Gold < cost) return "国库不足(需 " + cost + " 第纳尔海运补给费)";

                // 1) 选船 + 过户给军团(过户后 HasNavalNavigationCapability = true, 原版就认它是海上部队)
                var pool = CargoShips(k);
                var given = new List<Ship>();
                int acc = 0;
                float cargo = 0f;
                for (int i = 0; i < pool.Count && acc < need; i++)
                {
                    var s = pool[i];
                    if (s == null) continue;
                    given.Add(s);
                    acc += NavalBridge.Crew(s);
                    cargo += NavalBridge.Cargo(s);
                }
                if (given.Count == 0) return "没有可用的船(舰队为空?)";

                var convoy = new Convoy
                {
                    PartyId = p.StringId,
                    LegionName = LegionLabel(lg),
                    LegionMen = need,
                    FromPortId = from.StringId,
                    FromPortName = NavalBridge.NameOf(from),
                    ToPortId = to.StringId,
                    ToPortName = NavalBridge.NameOf(to),
                    DepartDay = SafeToday(),
                    EtaDays = eta,
                    ShipsGiven = given.Count,
                    TroopCap = cap,
                    CargoUsed = cargo,
                    Cost = cost,
                    PrevPlayerHold = lg.PlayerHold
                };

                int moved = 0;
                for (int i = 0; i < given.Count; i++)
                {
                    try
                    {
                        // v4.272(审计 A3): 过户**之前**先记下这条船原来的主人(PartyBase.Id = MobileParty.StringId),
                        //   到达/中断/召回时按原主还回去 —— 这是"洗船"漏洞的根治点。
                        string prevOwnerId = "";
                        try { prevOwnerId = given[i].Owner != null ? given[i].Owner.Id : ""; } catch { }
                        ChangeShipOwnerAction.ApplyByTransferring(p.Party, given[i]);
                        int key = ShipKey(given[i]);
                        convoy.ShipIds.Add(key);
                        if (key != 0) convoy.ShipOwner[key] = prevOwnerId ?? "";
                        moved++;
                    }
                    catch (Exception ex) { DLog.Force("海运: 移船失败 " + NavalBridge.HullName(given[i]) + " " + ex.Message); }
                }
                if (moved == 0)
                {
                    // 一条船都没过户成功 -> 不能出海(否则就是"海上无船"的非法状态), 直接失败返回
                    return "装船失败: 没有任何舰船完成过户(已全部退回)";
                }

                // 2) 出海 + 设定目的港(全走基础游戏 public 原语)
                //
                // v4.272(审计 A1 的**关键修复**): 原实现只调
                //     p.SetSailAtPosition(from.PortPosition);   // 把部队放到海上
                //     p.SetTargetSettlement(to, true);          // 只设"目标 = 该港海面"
                //   而 SetTargetSettlement **不设 PartyMoveMode**(反编译实证: 它只改 _targetSettlement /
                //   MoveTargetPoint / IsTargetingPort / Ai.DefaultBehaviorNeedsUpdate, 不动 MoveModeType)。
                //   MoveModeType 默认是 Hold, 全游戏唯一把它置成 Point 的入口是
                //     MobilePartyAi.TickInternal -> GetBehaviors -> SetAiBehavior -> UpdateBehavior
                //     -> MobileParty.SetNavigationModePoint(...)   (UpdateBehavior/SetNavigationMode* 都是 internal)
                //   而 TickInternal 被 DefArmyAi.AiGuard 的 Prefix 挡掉(国防军不参与原版 AI 决策)
                //   => 国防军的 PartyMoveMode 永远是 Hold => MobileParty.GetTargetCampaignPosition 里
                //      `if (PartyMoveMode == MoveModeType.Point)` 不成立 => 推进目标 = 自己当前位置 => **海上零推进**,
                //      表现就是"漂 3×ETA+3 天后由 Arrive() 瞬移下船"。
                //
                //   修法: 改用 SetMoveGoToSettlement(目的港, NavigationType.Naval, true)。它(反编译实证)做的是
                //     ResetAllMovementParameters(); SetTargetSettlement(to, isTargetingPort: true);
                //     TargetPosition = MoveTargetPoint = to.PortPosition;
                //     DesiredAiNavigationType = NavigationType.Naval;
                //     DefaultBehavior = AiBehavior.GoToSettlement;
                //   于是当 AiGuard 放行(见 DefArmyAi 的 InConvoy 判据)时, 下一次 Ai.Tick ->
                //   TickInternal -> GetBehaviors(从 DefaultBehavior=GoToSettlement 起步, 目标港未到达时
                //   行为保持 GoToSettlement) -> SetAiBehavior -> UpdateBehavior 看到
                //   ShortTermBehavior == GoToSettlement 且 AiBehaviorPartyBase(= 目的港 Party)有效
                //   -> **SetNavigationModePoint(BehaviorTarget)** -> PartyMoveMode = Point
                //   -> GetTargetCampaignPosition 返回 MoveTargetPoint(= 目的港海面) -> 原版导航开始真正推进。
                //   另外 DesiredAiNavigationType = Naval 让 IsComputePathCacheDirty 认可"海上走海路"
                //   (海上部队 + Naval 匹配, 不会被打回 NavigationType.All)。
                try
                {
                    p.SetSailAtPosition(from.PortPosition);
                    p.SetMoveGoToSettlement(to, MobileParty.NavigationType.Naval, true);
                }
                catch (Exception ex)
                {
                    // 出海失败 -> 立刻把军团放回陆地并把船退回, 绝不留下"卡在海上的军团"
                    try { p.DisembarkToPosition(from.GatePosition); } catch { }
                    ReturnShips(convoy, p, k);
                    return "出海失败(已撤回): " + ex.Message;
                }

                // 3) 玩家命令锁定: 军团归玩家指挥, DefArmyAi 当天不许改它的道(否则会被 AI 重新指回陆地目标)
                try { lg.PlayerHold = true; lg.PlayerOrderDay = SafeToday(); } catch { }

                EconomyWorld.TreasurySpend(cost);
                // v4.272(审计 A13): 原来记的是 Fiscal.AddNavy(经常性"海军维护"科目), 而购船/下单建造/封锁
                //   都记 AddCourt(宫廷与外交, 一次性开销) -> 月度"海军维护"被海运补给费虚高, 口径也不一致。
                //   海运补给费本质是**一次性调动开销**(和封锁的 500 调动费同类), 统一到 AddCourt。
                try { Fiscal.AddCourt(cost); } catch { }

                Active.Add(convoy);
                MapSelection.Message("海运: " + convoy.LegionName + " 已从「" + convoy.FromPortName + "」装船出海, 预计 "
                    + eta + " 天抵达「" + convoy.ToPortName + "」(载 " + need + " 人 / " + moved + " 船)");
                DLog.Force("海军: 海运出发 " + convoy.Line + " 花费 " + cost);
                return "已出发: " + convoy.LegionName + " → " + convoy.ToPortName + "(" + eta + " 天, " + moved + " 船, " + cost + " 第纳尔)";
            }
            catch (Exception ex)
            {
                DLog.Force("海军: 海运出发异常 " + ex.Message);
                return "海运出发失败: " + ex.Message;
            }
        }

        // ==================== 推进(小时结 + 日结兜底) ====================
        //   到达判定三选一: 已被原版带上岸 / 已在该港 / 已到该港锚地附近。
        //   另有超时兜底: 超过 3×ETA+3 天还没到 -> 强制在目的港下船(防"永久漂在海上")。
        internal static void Tick()
        {
            try
            {
                if (Active.Count == 0) return;
                if (!NavalBridge.Usable) { return; }

                Kingdom k = PlayerKingdom();
                for (int i = Active.Count - 1; i >= 0; i--)
                {
                    var c = Active[i];
                    if (c == null) { Active.RemoveAt(i); continue; }

                    var p = DefArmy.FindParty(c.PartyId);
                    if (p == null || !p.IsActive || p.Party == null)
                    {
                        // v4.272(审计 A6): 军团被歼灭/解散 —— 原实现**直接清账**(不还船、不还原 PlayerHold)。
                        //   但原版 MobileParty.RemoveParty() 会 DestroyShipAction.Apply 掉该部队**名下所有船**,
                        //   而"部队"这个对象此时可能已经不可用 -> 船已经连同部队一起蒸发, 这里能做的正确收尾是:
                        //     ① 按记录的原主还船(船若还在军团名下, 还回原主; 已经蒸发的自然还不了, 计数会体现);
                        //     ② 还原 DefLegion.PlayerHold —— Legion 记录独立于部队存活, 不还原就会**永久残留**,
                        //        把"玩家接管"状态永远挂在一个已经不存在的军团上(以后重建同号军团会莫名不受 AI 控制)。
                        //   注意: FindParty 依赖 Legion.PartyId, 部队没了它返回 null, 但 LegionOf(c.PartyId) 仍能
                        //   找到军团记录 -> PlayerHold 还得了。
                        int back = ReturnShipsOnLoss(c, p, k);
                        try
                        {
                            var lg = LegionOf(c.PartyId);
                            if (lg != null) lg.PlayerHold = c.PrevPlayerHold;
                            else DLog.Force("海军: 海运中止但找不到军团记录, PlayerHold 无法还原 " + c.PartyId);
                        }
                        catch { }

                        MapSelection.Message("海运: " + c.LegionName + " 已不存在, 运输队解散" + (back > 0 ? ("(船已归还原主 " + back + " 艘)") : ""));
                        DLog.Force("海军: 海运中止(军团不存在) " + c.Line + " 回收船 " + back + "/" + c.ShipIds.Count);
                        Active.RemoveAt(i);
                        continue;
                    }

                    Settlement to = null;
                    try { to = Settlement.Find(c.ToPortId); } catch { }
                    bool arrived = false;
                    bool timeout = false;
                    bool aborted = false;
                    bool keepGoing = false;   // v4.279(第 10 项): Arrive 回填"运输队继续在途"(目的港不可用)
                    // v4.273(A-1 海上损耗): 划出去的那批船里若有耐久见底的 -> 提前就近靠港。
                    //   走的是**已有的中断/靠港收尾路径**(aborted=true -> Arrive() 下船 + 按原主还船 +
                    //   还原 PlayerHold + 把目标从"港口海面"改成"陆上城门"), 不新增任何收尾分支。
                    bool emergency = false;
                    string emergencyWhy = "";
                    try
                    {
                        // 判定到达之前先看船: 船都要沉了就不该继续往目的港开
                        if (to != null && p.IsCurrentlyAtSea)
                        {
                            emergency = NavalSeaRisk.ShippedShipsFailing(c.ShipIds, p, out emergencyWhy);
                        }
                    }
                    catch { }
                    try
                    {
                        if (!p.IsCurrentlyAtSea)
                        {
                            // 已不在海上: 在目的港附近 = 正常抵达; 否则 = 被原版/AI 带上岸(或船全沉了) -> 中断结算
                            bool atDest = to != null &&
                                (p.CurrentSettlement == to || p.Position.Distance(to.GatePosition) < 20f);
                            if (atDest) arrived = true; else aborted = true;
                        }
                        else if (to != null && p.CurrentSettlement == to) arrived = true;
                        // v4.273(审计 #8): 到达阈值 3f -> 与 DLC 同口径 min(10f, 视野)。
                        //   原来是硬编码 3f, 贴着港口却判不到 -> 只能靠 3×ETA+3 的超时兜底,
                        //   表现就是"瞬移靠港"(玩家在港口外等了几天, 然后一夜之间出现在城里)。
                        else if (to != null && p.Position.Distance(to.PortPosition) <= ArriveRadius(p)) arrived = true;
                        else if (SafeToday() - c.DepartDay > c.EtaDays * 3 + 3) timeout = true;
                        if (emergency) aborted = true;   // 提前靠港优先于"到达"(已经到港了也无所谓, 目的港就是下船点)
                    }
                    catch { }

                    if (!arrived && !timeout && !aborted) continue;

                    if (emergency && !string.IsNullOrEmpty(emergencyWhy))
                    {
                        c.AbortDay = SafeToday();
                        DLog.Force("海军: 海运提前靠港(海上损耗) " + c.Line + " 原因=" + emergencyWhy);
                    }
                    string msg = Arrive(c, p, to, k, timeout, aborted, out keepGoing);
                    // v4.279(第 10 项): keepGoing = 目的港不可用 -> 已改道/原地待命, **保留记录**让下个结算日重试
                    if (!keepGoing) Active.RemoveAt(i);
                    if (!string.IsNullOrEmpty(msg)) { try { MapSelection.Message(msg); } catch { } }
                }
            }
            catch (Exception ex) { DLog.Force("海军: 海运推进异常 " + ex.Message); }
        }

        // v4.273(审计 #8): 海上"已到目的港"的判定半径 —— 与 DLC 同口径 `min(10f, SeeingRange)`。
        //   原实现硬编码 3f, 而 DLC 自己用 min(10, 视野): 贴着港口(3~10 之间)的船判不到,
        //   只能等 3×ETA+3 天超时兜底 —— 玩家看到的就是"在港外漂了几天然后瞬移进城"。
        //   放宽到 10f 不会"还没到就提前下船": 下船点仍然是 to.GatePosition(见 Arrive 第 1 步),
        //   而且到达后立刻 SetTargetSettlement(to, false) 把目标从"港口海面"改成"陆上城门",
        //   所以船不会在港外就被扔下海 —— 它是**到港才下船**, 只是"算到达"的容差变宽了。
        private static float ArriveRadius(MobileParty p)
        {
            try
            {
                float see = 10f;
                if (p != null) { float sr = p.SeeingRange; if (sr > 0f) see = sr; }
                return Math.Min(10f, see);
            }
            catch { return 10f; }
        }

        // v4.279(第 10 项): "目的港/出发港已消失"时的改道目标 —— 离军团最近的**可用港口**。
        //   选择规则: ①自有港口优先(即使稍远) —— 军团最终要在那里下船, 往敌港开会触发原版拦截/攻城;
        //             ②同类港口之间取海上直线距离最近的一处。
        //   取不到(全图没港口 / 坐标异常)返回 null, 调用方按"原地待命"处理。
        private static Settlement NearestUsablePort(MobileParty p)
        {
            try
            {
                if (p == null) return null;
                var k = PlayerKingdom();
                Settlement best = null;
                float bestD = float.MaxValue;
                bool bestMine = false;
                var all = NavalBridge.AllPorts();
                for (int i = 0; i < all.Count; i++)
                {
                    var s = all[i];
                    if (s == null || !s.HasPort) continue;
                    bool mine = k != null && ReferenceEquals(NavalBridge.KingdomOf(s), k);
                    float d;
                    try { d = p.Position.Distance(s.PortPosition); } catch { continue; }
                    if (best == null) { best = s; bestD = d; bestMine = mine; continue; }
                    if (mine && !bestMine) { best = s; bestD = d; bestMine = true; continue; }
                    if (mine == bestMine && d < bestD) { best = s; bestD = d; }
                }
                return best;
            }
            catch { return null; }
        }

        // 到达(或超时/中断): 下船 + 还船 + 还原玩家命令锁
        //   v4.279(第 10 项): 新增 out keepGoing —— 目的港已消失(to == null)且军团仍在海上时**不结束运输队**,
        //     改为改道最近的可用港口后继续在途(见下面长注释)。
        private static string Arrive(Convoy c, MobileParty p, Settlement to, Kingdom k, bool timeout, bool aborted, out bool keepGoing)
        {
            keepGoing = false;
            try
            {
                // ==================== v4.279(第 10 项): 目的港消失时**绝不能用海面坐标下船** ====================
                //   【原缺陷】`to == null`(目的港被移除 / 存档丢段 / 港口被摧毁·易主)时走
                //     `p.DisembarkToPosition(p.Position)` —— 而军团此刻在**海上**, `p.Position` 是**海面坐标**;
                //     `DisembarkToPosition` 只把 IsCurrentlyAtSea 置 false ⇒ 得到一支
                //     "陆上标记 + 海面坐标"的**非法部队**: 它按陆地规则在海上移动(可能永远走不出来),
                //     原版后续的上岸/入城/围城判定也全部按陆上处理 —— 表现就是"军团卡在海里/站在水上"。
                //   【修法】三重降级, **任何一档都不调用 DisembarkToPosition(p.Position)**:
                //     ① 还找得到可用港口 -> 把在途目标改成**最近的可用港口**(自有港优先, 免得往敌港开),
                //        保持海上状态继续航行(keepGoing=true, 记录留在 Active, 下一个结算日再判到达);
                //     ② 一处港口都找不到 -> **原地保持海上**待命(keepGoing=true), 只记日志、不结束运输队;
                //     ③ 军团已经不在海上(被原版带上岸/被拦截) -> 那本来就是合法陆上状态,
                //        照常走下面的收尾(还船 + 还原 PlayerHold), 无需下船动作。
                //   ★ "留在原地重试"不会造成永久漂着: Tick 里的 3×ETA+3 天超时兜底仍会在必要时把它
                //     送进能用的港口; 而 keepGoing=true 时 Tick **不**移除 Active 记录。
                string where = to != null ? NavalBridge.NameOf(to) : c.ToPortName;
                if (to == null)
                {
                    if (p.IsCurrentlyAtSea)
                    {
                        var alt = NearestUsablePort(p);
                        if (alt != null)
                        {
                            c.ToPortId = alt.StringId;
                            c.ToPortName = NavalBridge.NameOf(alt);
                            try { p.SetMoveGoToSettlement(alt, MobileParty.NavigationType.Naval, true); } catch { }
                            keepGoing = true;
                            DLog.Force("海军: 海运目的港「" + where + "」已不可用 -> 改道最近可用港口「"
                                + c.ToPortName + "」继续航行(不下船, 运输队不结束)");
                            return "海运: 目的港「" + where + "」已不可用(被摧毁/易主/存档丢失) —— "
                                 + c.LegionName + " 已改道最近可用港口「" + c.ToPortName + "」继续在途";
                        }
                        keepGoing = true;
                        DLog.Force("海军: 海运目的港「" + where + "」已不可用, 全图又找不到其它港口 -> "
                            + c.LegionName + " 保持海上待命, 下一个结算日重试(不下船, 运输队不结束)");
                        return "海运: 目的港「" + where + "」已不可用且找不到其它港口 —— " + c.LegionName
                             + " 暂留海上待命(**不会在海上非法下船**), 将在下一个结算日重试";
                    }
                    DLog.Force("海军: 海运目的港「" + where + "」已不可用, 但军团已在陆上 -> 直接进入收尾");
                }

                // 1) 下船: 目的港有城门坐标就用它, 否则退回军团当前位置(不移动到非法点)
                try
                {
                    if (p.IsCurrentlyAtSea)
                    {
                        if (to != null) p.DisembarkToPosition(to.GatePosition);
                        // v4.279(第 10 项): 原来的 else 分支(用 p.Position 海面坐标下船)已删除 ——
                        //   to == null 的情形全部在上面处理完(要么改道继续走, 要么原地待命)。
                    }
                }
                catch (Exception ex) { DLog.Force("海运: 下船失败 " + ex.Message); }

                // 1b) **关键**: 把"目标 = 该港海面(isTargetingPort)"改成"目标 = 该港陆上城门"。
                //     否则 MoveTargetPoint 仍指着港口海面, 原版导航会立刻又让它出海 —— 表现就是在港口
                //     反复上岸/下海抖动。SetTargetSettlement(to, false) 正好做这件事且不会踩空
                //     (传 null 会走 settlement.GatePosition 而 NRE, 所以这里必须传具体港口)。
                try { if (to != null) p.SetTargetSettlement(to, false); } catch { }

                // 2) 还船: 运输队的船**还回原主**(v4.272 审计 A3: 原来一律还给玩家主队 = 洗船漏洞)
                int back = ReturnShips(c, p, k);

                // 3) 还原玩家命令锁(出发前是什么就还原成什么)
                try
                {
                    var lg = LegionOf(c.PartyId);
                    if (lg != null) lg.PlayerHold = c.PrevPlayerHold;
                }
                catch { }

                string head = aborted ? "海运中断" : (timeout ? "海运超时靠港" : "海运抵达");
                DLog.Force("海军: " + head + " " + c.Line + " 还船 " + back + "/" + c.ShipIds.Count);
                if (aborted)
                {
                    // v4.273(A-1): 因"海上损耗"提前靠港的单独特判 —— 玩家必须知道是船撑不住了,
                    //   而不是"莫名其妙在目的港外下船了"。
                    if (c.AbortDay > 0 && c.AbortDay == SafeToday())
                        return "海运: " + c.LegionName + " 因海上损耗船体告急, 已在「" + where
                             + "」就近靠港下船(还船 " + back + " 艘) —— 请回港修船后再出航";
                    return "海运: " + c.LegionName + " 途中已上岸(或被拦截/船只损失), 运输队结束, 还船 " + back + " 艘";
                }
                return "海运: " + c.LegionName + (timeout ? " 已超时, 就近在「" : " 已抵达「") + where
                    + "」下船(还船 " + back + " 艘)";
            }
            catch (Exception ex)
            {
                DLog.Force("海军: 海运到达处理异常 " + ex.Message);
                return null;
            }
        }

        // 把运输队名下的船**还回原主**(v4.272 审计 A3), 原主没了才走 fallback 接收方。
        private static int ReturnShips(Convoy c, MobileParty p, Kingdom k)
        {
            try
            {
                if (c == null || p == null || p.Party == null) return 0;
                return ReturnShipsCore(c, p, k);
            }
            catch { return 0; }
        }

        // 军团已被歼灭时的回收: 部队对象可能已经无效(Party == null), 但** Legion 记录与船的原主 id 还在**。
        //   先尽力按 id 找一次部队(有的"歼灭"只是 IsActive=false 而对象还在), 找不到就只用原主 id 兜底还船。
        private static int ReturnShipsOnLoss(Convoy c, MobileParty p, Kingdom k)
        {
            try
            {
                if (c == null || c.ShipIds.Count == 0) return 0;
                var live = p;
                if (live == null || live.Party == null) live = NavalFleet.FindMyPartyById(c.PartyId);
                return ReturnShipsCore(c, live, k);
            }
            catch { return 0; }
        }

        // 公共实现: live 可以为 null(部队已消失) —— 那时只按"原主 id"还船
        private static int ReturnShipsCore(Convoy c, MobileParty live, Kingdom k)
        {
            int n = 0;
            try
            {
                // 1) 按原主分组: ShipOwner[ShipKey] = 出发前那条船的 PartyBase.Id
                //    没登记原主的老档条目(ownerId == "")单独走 fallback。
                var groups = new Dictionary<string, List<int>>();
                var noOwner = new List<int>();
                for (int i = 0; i < c.ShipIds.Count; i++)
                {
                    int key = c.ShipIds[i];
                    string ownerId;
                    if (c.ShipOwner.TryGetValue(key, out ownerId) && !string.IsNullOrEmpty(ownerId))
                    {
                        List<int> g;
                        if (!groups.TryGetValue(ownerId, out g)) { g = new List<int>(); groups[ownerId] = g; }
                        g.Add(key);
                    }
                    else noOwner.Add(key);
                }

                Settlement port = null;
                try { port = live != null ? live.CurrentSettlement : null; } catch { }
                if (port == null) { try { port = Settlement.Find(c.ToPortId); } catch { } }
                if (port == null) { try { port = Settlement.Find(c.FromPortId); } catch { } }
                PartyBase fallback = null;   // 惰性取一次(原主还在时不需要)

                foreach (var kv in groups)
                {
                    var origin = NavalFleet.FindMyPartyById(kv.Key);
                    if (origin != null && origin.Party != null)
                    {
                        n += ReturnShipKeys(live, origin.Party, kv.Value);
                    }
                    else
                    {
                        // 原主没了(被歼灭/氏族解散/存档丢失) -> 走 fallback(玩家主队优先)
                        if (fallback == null) fallback = NavalFleet.ReceiverParty(k, port);
                        if (fallback != null) n += ReturnShipKeys(live, fallback, kv.Value);
                    }
                }
                if (noOwner.Count > 0)
                {
                    if (fallback == null) fallback = NavalFleet.ReceiverParty(k, port);
                    if (fallback != null) n += ReturnShipKeys(live, fallback, noOwner);
                }
            }
            catch { }
            return n;
        }

        // 从 live 名下把 keys 里的船过户给 target(只还"这次划出去的船", 军团原有的船不动)
        private static int ReturnShipKeys(MobileParty live, PartyBase target, List<int> keys)
        {
            int n = 0;
            try
            {
                if (live == null || live.Party == null || target == null || keys == null) return 0;
                var ships = NavalBridge.ShipsOf(live);
                for (int i = 0; i < ships.Count; i++)
                {
                    var s = ships[i];
                    if (s == null) continue;
                    if (!keys.Contains(ShipKey(s))) continue;
                    try { ChangeShipOwnerAction.ApplyByTransferring(target, s); n++; }
                    catch { }
                }
            }
            catch { }
            return n;
        }

        private static DefLegion LegionOf(string partyId)
        {
            try
            {
                for (int i = 0; i < DefArmy.Legions.Count; i++)
                    if (DefArmy.Legions[i] != null && DefArmy.Legions[i].PartyId == partyId) return DefArmy.Legions[i];
            }
            catch { }
            return null;
        }

        // 召回: 立刻在出发港下船并还船(玩家改主意时用)
        internal static string Recall(int index)
        {
            try
            {
                if (index < 0 || index >= Active.Count) return "没有这支运输队";
                var c = Active[index];
                var p = DefArmy.FindParty(c.PartyId);
                if (p == null || !p.IsActive || p.Party == null)
                {
                    // v4.272(审计 A6): 与 Tick 的"军团已不存在"路径同口径 —— 还船(按原主) + 还原 PlayerHold,
                    //   再清账。原来这里只清账, 会把 PlayerHold 永久残留在 Legion 记录上。
                    int back0 = ReturnShipsOnLoss(c, p, PlayerKingdom());
                    try
                    {
                        var lg0 = LegionOf(c.PartyId);
                        if (lg0 != null) lg0.PlayerHold = c.PrevPlayerHold;
                    }
                    catch { }
                    Active.RemoveAt(index);
                    return "军团已不存在, 运输队已清账" + (back0 > 0 ? ("(船已归还原主 " + back0 + " 艘)") : "");
                }

                Settlement from = null;
                try { from = Settlement.Find(c.FromPortId); } catch { }
                // ==================== v4.279(第 10 项): 出发港消失时同样不许用海面坐标下船 ====================
                //   原实现 `else p.DisembarkToPosition(p.Position)` 与 Arrive 里那个是同一个缺陷
                //   (海面坐标 + 陆上标记 = 非法部队)。这里改成"改道最近的可用港口", 实在找不到港口就
                //   **拒绝召回**并把原因告诉玩家(运输队保持原状, 下一个结算日还有机会), 绝不下船到海面。
                if (from == null)
                {
                    from = NearestUsablePort(p);
                    if (from == null)
                    {
                        return "出发港「" + c.FromPortName + "」已不可用, 且全图找不到其它可用港口 —— "
                             + "无法召回(运输队保持原状; 军团仍在海上, 不会被丢在海面)";
                    }
                    DLog.Force("海军: 出发港「" + c.FromPortName + "」已不可用 -> 召回改为就近靠泊「"
                        + NavalBridge.NameOf(from) + "」");
                    c.FromPortId = from.StringId;
                    c.FromPortName = NavalBridge.NameOf(from);
                }
                try
                {
                    if (p.IsCurrentlyAtSea) p.DisembarkToPosition(from.GatePosition);
                }
                catch { }
                // 同上: 清掉海上目标, 免得它在出发港又自己下海
                try { p.SetTargetSettlement(from, false); } catch { }
                int back = ReturnShips(c, p, PlayerKingdom());
                try
                {
                    var lg = LegionOf(c.PartyId);
                    if (lg != null) lg.PlayerHold = c.PrevPlayerHold;
                }
                catch { }
                Active.RemoveAt(index);
                DLog.Force("海军: 海运召回 " + c.Line + " 还船 " + back);
                return "已召回 " + c.LegionName + "(返回「" + c.FromPortName + "」, 还船 " + back + " 艘)";
            }
            catch (Exception ex) { return "召回失败: " + ex.Message; }
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

        private static int SafeToday()
        {
            try { return Politics.Today(); } catch { return 0; }
        }

        // ==================== 存档(并入 NavalFleet 的 FIA_Navy 键, 段前缀 T=) ====================
        // 格式(v4.273 起, 字段顺序见上面 CS_* 索引表 —— 以索引表为准, 这行只是速览):
        //   T=<partyId>|<legionName>|<men>|<fromId>|<fromName>|<toId>|<toName>|<departDay>|<eta>|<cap>
        //     |<cargo>|<cost>|<prevHold 0/1>|<ships>|<abortDay>
        //   共 15 字段(14 个 '|')。读侧最小接受 13 字段(再少就只能丢弃并打日志)。
        //   v4.272(审计 A3): <ships> 从"逗号分隔的 ShipKey"升级为"逗号分隔的 key^原主PartyId":
        //     · 老档兼容: 没有 '^' 的条目 -> 原主按空串处理(还船时走 fallback 接收方, 不会崩也不会丢字段);
        //     · 原主 id 里出现 '^' 的概率为 0(原版部队 StringId 是字母数字下划线),
        //       万一真出现, 最坏情况是**还船给 fallback**(功能降级, 不会毁档)。
        //   v4.273(致命修复 #1): 原写入 15 字段 / 读取 13 字段且判定 `f.Length < 14` -> **每条都丢**。
        //     现在两侧共用 CS_* 索引常量, 并在写入后按索引自检(见 Serialize 末尾的 DebugSelfCheck)。
        internal static string Serialize()
        {
            try
            {
                if (Active.Count == 0) return "";
                var sb = new StringBuilder();
                for (int i = 0; i < Active.Count; i++)
                {
                    var c = Active[i];
                    if (c == null) continue;
                    // ShipIds 是 int 列表: 手工拼(避免 string.Join 在 netstandard2.0 的重载歧义)
                    //   v4.273: 顺带把 key==0 的无效项**不写入**(ShipKey 读不到 RandomValue 时才是 0),
                    //     并给 ownerId 做转义(万一某个部队 StringId 含 ';'/'|', 不转义会撕烂整个存档行)。
                    var ids = new StringBuilder();
                    for (int j = 0; j < c.ShipIds.Count; j++)
                    {
                        if (c.ShipIds[j] == 0) continue;
                        if (ids.Length > 0) ids.Append(',');
                        ids.Append(c.ShipIds[j]);
                        string ownerId;
                        if (c.ShipOwner.TryGetValue(c.ShipIds[j], out ownerId) && !string.IsNullOrEmpty(ownerId))
                            ids.Append('^').Append(NavalFleet.Escape(ownerId));
                    }
                    // v4.273(致命修复 #1): 字段顺序改成"按 CS_* 索引常量显式排布" —— 原来是一串
                    //   .Append(...).Append(...) 链式调用, 与读侧的裸下标毫无对应关系, 一旦有人插字段
                    //   就会再次错位。现在用一个定长数组按索引填, **写入顺序 == 索引表顺序**, 肉眼可核对。
                    var fv = new string[ConvoyFieldCountWrite];
                    fv[CS_PartyId] = NavalFleet.Escape(c.PartyId);
                    fv[CS_LegionName] = NavalFleet.Escape(c.LegionName);
                    fv[CS_LegionMen] = c.LegionMen.ToString();
                    fv[CS_FromPortId] = NavalFleet.Escape(c.FromPortId);
                    fv[CS_FromPortName] = NavalFleet.Escape(c.FromPortName);
                    fv[CS_ToPortId] = NavalFleet.Escape(c.ToPortId);
                    fv[CS_ToPortName] = NavalFleet.Escape(c.ToPortName);
                    fv[CS_DepartDay] = c.DepartDay.ToString();
                    fv[CS_EtaDays] = c.EtaDays.ToString();
                    fv[CS_TroopCap] = c.TroopCap.ToString();
                    fv[CS_CargoUsed] = ((int)c.CargoUsed).ToString();
                    fv[CS_Cost] = c.Cost.ToString();
                    fv[CS_PrevPlayerHold] = c.PrevPlayerHold ? "1" : "0";
                    fv[CS_Ships] = ids.ToString();
                    fv[CS_AbortDay] = c.AbortDay.ToString();
                    sb.Append(";T=").Append(string.Join("|", fv));
                    // v4.273(致命修复 #1 自检): 每条记录写完立刻按 '|' 数反查一遍字段数。
                    //   不匹配就是"写读错位"重演的前兆 —— 当场打 DLog.Force, 而不是等到玩家读档丢数据。
                    if (fv.Length != ConvoyFieldCountWrite || CountPipes(string.Join("|", fv)) != ConvoyFieldCountWrite - 1)
                        DLog.Force("海军: 海运存档字段数自检失败! 期望 " + ConvoyFieldCountWrite + " 字段/ "
                            + (ConvoyFieldCountWrite - 1) + " 个 '|', 实得 " + fv.Length + " 字段/ "
                            + CountPipes(string.Join("|", fv)) + " 个 '|' —— 写读索引表已不一致, 请立刻检查 CS_* 常量");
                }
                return sb.ToString();
            }
            catch (Exception ex) { DLog.Force("海军: 海运存档失败 " + ex.Message); return ""; }
        }

        // v4.273(修复 #1 配套): 数一段文本里的 '|' 个数(自检用)
        private static int CountPipes(string s)
        {
            int n = 0;
            try
            {
                if (string.IsNullOrEmpty(s)) return 0;
                for (int i = 0; i < s.Length; i++) if (s[i] == '|') n++;
            }
            catch { }
            return n;
        }

        // v4.275: 返回值 = 这一条 T= 记录**是否真的读进来了**。
        //   false 的三种情形(字段数不足 / 缺 PartyId·ToPortId / 解析抛异常)由调用方
        //   (NavalFleet.Load) 计入"坏段"并出统一日志 —— 这样"海运静默消失"在整键层面也可追溯。
        internal static bool LoadSegment(string body)
        {
            try
            {
                if (string.IsNullOrEmpty(body)) return false;
                var f = body.Split('|');
                // v4.273(致命修复 #1): 字段数不足 = 残缺/老格式记录。
                //   ① 必须**明确打日志**(DLog.Force)而不是静默 return —— 原来静默丢弃是"海运静默消失"的元凶;
                //   ② 判定门槛用 ConvoyFieldCountMin(13): 少于它连 PartyId/目的港都不全, 无法安全还原;
                //   ③ 13/14 字段的历史记录也能读进来(缺的字段按默认值), 不再"整条丢"。
                if (f.Length < ConvoyFieldCountMin)
                {
                    DLog.Force("海军: 海运记录字段数不足(" + f.Length + " < " + ConvoyFieldCountMin
                        + "), 该条已丢弃 —— 老档格式不兼容, 影响: 该运输队及其船只归属丢失; 记录="
                        + (body.Length > 120 ? body.Substring(0, 120) + "…" : body));
                    return false;
                }
                var c = new Convoy
                {
                    PartyId = NavalFleet.Unescape(f[CS_PartyId]),
                    LegionName = NavalFleet.Unescape(f[CS_LegionName]),
                    LegionMen = NavalFleet.ParseInt(f[CS_LegionMen]),
                    FromPortId = NavalFleet.Unescape(f[CS_FromPortId]),
                    FromPortName = NavalFleet.Unescape(f[CS_FromPortName]),
                    ToPortId = NavalFleet.Unescape(f[CS_ToPortId]),
                    ToPortName = NavalFleet.Unescape(f[CS_ToPortName]),
                    DepartDay = NavalFleet.ParseInt(f[CS_DepartDay]),
                    EtaDays = NavalFleet.ParseInt(f[CS_EtaDays]),
                    TroopCap = NavalFleet.ParseInt(f[CS_TroopCap]),
                    CargoUsed = NavalFleet.ParseInt(f[CS_CargoUsed]),
                    Cost = NavalFleet.ParseInt(f[CS_Cost]),
                    PrevPlayerHold = NavalFleet.ParseInt(f[CS_PrevPlayerHold]) != 0,
                    // v4.273(A-1): 老档没有第 15 字段 -> ParseInt 越界? 不 —— 这里显式判长度, 缺则 0
                    AbortDay = f.Length > CS_AbortDay ? NavalFleet.ParseInt(f[CS_AbortDay]) : 0
                };
                string ids = f[CS_Ships];
                if (!string.IsNullOrEmpty(ids))
                {
                    var arr = ids.Split(',');
                    for (int i = 0; i < arr.Length; i++)
                    {
                        string item = arr[i];
                        if (string.IsNullOrEmpty(item)) continue;
                        // v4.272: key 或 key^原主PartyId(老档只有 key)
                        string ownerId = "";
                        int hat = item.IndexOf('^');
                        if (hat >= 0)
                        {
                            ownerId = item.Substring(hat + 1);
                            item = item.Substring(0, hat);
                        }
                        int v;
                        if (int.TryParse(item, out v) && v != 0)
                        {
                            c.ShipIds.Add(v);
                            // v4.273: ownerId 与写入侧 Escape 对称
                            if (!string.IsNullOrEmpty(ownerId)) c.ShipOwner[v] = NavalFleet.Unescape(ownerId);
                        }
                        else
                        {
                            // v4.273: 坏条目不再静默 —— 它意味着"少还一条船", 必须留痕
                            DLog.Force("海军: 海运船条目无法解析(已跳过): " + arr[i]);
                        }
                    }
                }
                // v4.273(修复 #1 收尾自检): 关键字段缺失的记录读进来也没用(军团找不回、目的港找不到),
                //   与其让它在下一次 Tick 里被当"军团已不存在"清账, 不如这里就说清楚。
                if (string.IsNullOrEmpty(c.PartyId) || string.IsNullOrEmpty(c.ToPortId))
                {
                    DLog.Force("海军: 海运记录缺少 PartyId/ToPortId(已丢弃) —— 存档段=" + body);
                    return false;
                }
                Active.Add(c);
                return true;
            }
            // v4.275: 抛异常也算"这一条没读进来" -> 交给调用方计坏段(不再自己吞掉当成功)
            catch (Exception ex) { DLog.Force("海军: 海运读档失败(跳过该条) " + ex.Message); return false; }
        }

        internal static void Reset() { try { Active.Clear(); } catch { } }

        // ==================== v4.273(C 级): 沉船事件 —— 把已沉的船从运输队账上划掉 ====================
        //   【为什么需要】Convoy.ShipIds 记的是"这次海运划出去的那几条船"的 ShipKey。若某条船在途中
        //   沉没(被拦截/海上损耗/原版 DestroyShipAction), 这个 key 会一直留在账上:
        //     · ReturnShipsCore 会为它建一个"要还"的分组, 但实际已经找不到那条船 -> 白记一笔;
        //     · 玩家看到"还船 N 艘"少于出发时的船数, 却不知道为什么。
        //   订阅官方 CampaignEvents.OnShipDestroyedEvent(PartyBase 前主人, Ship 船, ShipDestroyDetail)
        //   来即时清理 —— 账目与真实船只数永远一致, 顺带修掉 A3 遗留的"船已在别人手里"的边界。
        //   注意: 事件参数里的 ship 对象在销毁后不可再访问其属性, 所以**只读 RandomValue**(纯字段)。
        internal static void OnShipDestroyed(PartyBase owner, Ship ship, DestroyShipAction.ShipDestroyDetail detail)
        {
            try
            {
                if (ship == null) return;
                int key = 0;
                try { key = ship.RandomValue; } catch { }
                if (key == 0) return;
                for (int i = 0; i < Active.Count; i++)
                {
                    var c = Active[i];
                    if (c == null) continue;
                    if (!c.ShipIds.Contains(key)) continue;
                    c.ShipIds.Remove(key);
                    c.ShipOwner.Remove(key);
                    DLog.Force("海军: 海运账目清理 —— 一条船在途中沉没(ShipKey " + key + "), 运输队 "
                        + c.LegionName + " 剩余 " + c.ShipIds.Count + " 艘待还");
                }
            }
            catch { }
        }
    }
}
