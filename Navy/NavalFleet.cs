using System;
using System.Collections.Generic;
using System.Text;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Naval;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.ObjectSystem;

namespace FeudalInternalAffairs
{
    // v4.268: 海军舰队状态 —— 玩家王国的舰队、造船队列、封锁目标、海战记录。
    //   存档键 FIA_Navy(在 NavalBehavior.SyncData 里读写; 老档没有该键 -> Load 收到空串 -> 按"没有舰队"处理, 不报错)。
    //   设计原则: 复用现有系统 —— 钱走 EconomyWorld.Treasury, 账走 Fiscal, 贸易封禁口径对齐 TradeRoutes.SetEmbargo。
    internal static class NavalFleet
    {
        // ==================== 造船队列 ====================
        internal class Order
        {
            internal string PortId = "";      // 港口 Settlement.StringId
            internal string PortName = "";    // 港口名(存档冗余一份, 港口丢了也能显示)
            internal string HullId = "";      // 船体 id(ship_hulls.xml)
            internal string HullName = "";    // 船体名
            internal float Paid = 0f;         // 已付造价
            internal int DaysLeft = 0;        // 剩余天数
            internal int DaysTotal = 0;       // 总天数
            internal int Tier = 0;            // 船体档位 0/1/2
            // v4.278(追加 3): 订单**真实推进过的天数** —— 进度不能再从 DaysLeft 反推(见 Progress 注释)
            internal int DaysDone = 0;
            // v4.278(追加 3): 接收方缺失 -> 进入"无限等待"状态(不推进、不收维护费、可随时全款取消)
            internal bool WaitingReceiver = false;
            // v4.272(UI 需求): 在建船也要能在舰队页显示 船员/耐久/战力/维护 四列 —— 原来只有
            //   工期信息, 这四列只能留空。下单时把该船体的静态属性一起记下来(完工后与真实 Ship 一致)。
            internal int Crew = 0;            // 载员(ShipHull.TotalCrewCapacity)
            internal float HitPoints = 0f;    // 耐久(ShipHull.MaxHitPoints)
            internal int HullValue = 0;       // 船价(ShipHull.Value) —— 战力/维护/退款的折算基准

            internal float Progress
            {
                get
                {
                    if (DaysTotal <= 0) return 1f;
                    // ==================== v4.278(追加 3 ★): 进度必须用"真实已过天数"算 ====================
                    //   【原 bug】`1 - DaysLeft / DaysTotal`, 而"接收方缺失 -> 卡住"那条路径把
                    //   `DaysLeft = 1` 写死(见 DailyShipbuilding)。于是卡单时 DaysLeft 恒 = 1:
                    //     Progress 恒 = 1 - 1/DaysTotal > 0  (只要 DaysTotal > 1)
                    //   两个后果: ①取消订单**永远退不到全款**(退款 = Paid × Progress, 被卡单时反而越卡退越少);
                    //   ②那个订单**永久占用** Orders 槽(上限 4 条), 还每天照收 3×折扣维护费。
                    //   【修法】改用 DaysDone(只在**真的推进了一天**时 +1):
                    //     · 正常订单: DaysDone 与 (DaysTotal - DaysLeft) 完全同步, 数值不变;
                    //     · 卡单(WaitingReceiver): DaysDone 不再增长 -> 进度停在卡住前的那一刻,
                    //       面板显示也如实反映"船厂停工了"; 维护费同时停收(见 DailyUpkeep)。
                    int done = DaysDone;
                    if (done < 0) done = 0;
                    if (done > DaysTotal) done = DaysTotal;
                    return MBMath.ClampFloat((float)done / DaysTotal, 0f, 1f);
                }
            }
        }

        internal static readonly List<Order> Orders = new List<Order>();

        // ==================== 封锁 ====================
        internal static string BlockadePortId = "";
        internal static string BlockadePortName = "";
        internal static string BlockadeKingdomId = "";
        internal static string BlockadeKingdomName = "";
        internal static int BlockadeSinceDay = 0;
        internal static float BlockadeDamage = 0f;    // 累计给对方造成的损失(第纳尔)

        // ==================== 海战记录(倒序, 最多 12 条) ====================
        internal static readonly List<string> Battles = new List<string>();
        internal const int BattleLogMax = 12;
        // v4.290 修复: Battles 是上限 12 的滚动表, Count 到顶后恒为 12。
        // 教程挑战 g_navy_battle 原先用 "Battles.Count > 基线" 判定, 一旦进挑战时已有 12 条,
        // 判定式永远为 false -> 作为最后一条会卡死"全部完成"。改用单调递增的累计计数。
        internal static int TotalBattles = 0;

        // ==================== 统计(供面板显示) ====================
        internal static int LastUpkeepPaid = 0;         // 昨日海军维护费
        internal static float LastBlockadeGain = 0f;    // 昨日封锁战果(我方收益)
        internal static float LastBlockadeLoss = 0f;    // 昨日给对方造成的损失
        // v4.272(A11): 封锁附带的那条贸易禁运是不是我们挂的(解除封锁时要一并撤销; 玩家手动挂的禁运不动)
        internal static bool BlockadeEmbargoOwned = false;
        // v4.272(A10): 海上巡逻的真实统计(每个日结刷新一次) —— 巡逻队 = 玩家王国在航的 DLC 海上巡逻队
        internal static float PatrolMul = 1f;           // 当前巡逻收益系数(= NavalTech.Sum.Patrol)
        internal static int PatrolParties = 0;          // 在航巡逻队数量
        internal static int PatrolShips = 0;            // 巡逻队船只总数
        internal static int PatrolCrew = 0;             // 巡逻队载员总数

        internal static void AddBattle(string report)
        {
            try
            {
                if (string.IsNullOrEmpty(report)) return;
                TotalBattles++;
                Battles.Insert(0, report);
                while (Battles.Count > BattleLogMax) Battles.RemoveAt(Battles.Count - 1);
            }
            catch { }
        }

        // ==================== 舰队规模 / 维护费 ====================

        // 舰队上限: 基础 4 条 + 科技 + 每处自有港口 +1 + **船坞建筑容量**(v4.273 A-2)
        //   v4.273(A-2): 原来**完全没算船坞等级** —— 升船坞只解锁造船档位, 对"能养多少船"毫无帮助,
        //   建筑投资价值缺了一半。现在把 BuildingEffectEnum.ShipProduction(1/2/3 级) 的容量加进来
        //   (数值走原版 Building.AddEffectOfBuilding 读, 不写死), 于是"在自有港口盖船坞"同时提升:
        //     ①能造多大的船(MaxHullTier) ②能养多少船(本函数)。
        //
        // ==================== v4.275(第 4 项): 这是**自建口径**, 与原版 PartyShipLimitModel 的关系 ====================
        //   反编译实证(本机 v1.4.x 游戏 DLL):
        //     · TaleWorlds.CampaignSystem.ComponentInterfaces.PartyShipLimitModel 只有三个抽象方法:
        //         int GetIdealShipNumber(MobileParty) / int GetIdealShipNumber(Clan) / float GetShipPriority(...)
        //     · 无 DLC 时注册的是 DefaultPartyShipLimitModel —— 三个方法**一律返回 0**(纯占位)。
        //     · 装了海战 DLC 后由 NavalDLC.GameComponents.NavalDLCShipLimitModel 覆盖:
        //         GetIdealShipNumber(MobileParty): 商队/领主部队/匪徒部队**都返回常数 3**;
        //           其余类型走 Debug.FailedAssert("Unhandled case") 再回落 BaseModel(=0)。
        //           ★ 玩家主队 IsLordParty == false -> 落到 FailedAssert 分支: 原版**根本没为玩家主队
        //             定义船数上限**(这也解释了为什么这个重载在 DLC 全库只被"商队/匪徒/海盗"消费)。
        //         GetIdealShipNumber(Clan): 只被 PartyShipLimitModel 自身声明, DLC 主库**无任何调用点**。
        //     · 真正管"一国能有多少船"的是另一个模型 ClanShipOwnershipModel.GetIdealShipNumberForClan(Clan)
        //       (DLC 实现 NavalDLCClanShipOwnershipModel = ClanTierModel.GetPartyLimitForTier(clan, clan.Tier) × 3,
        //        即"该氏族按等级能带几支部队 × 3"); 而它唯一的消费点 ShipLimitCampaignBehavior.DailyTickClan 里
        //       写的是 `!clan.IsBanditFaction && !clan.IsEliminated && clan != Clan.PlayerClan` —— **玩家氏族被显式排除**。
        //     · 买船的硬校验 ShipDistributionModel.CanPartyTakeShip(PartyBase, Ship) 只拦"匪徒买 2 级船",
        //       与舰队条数无关; 我们的购船(NavalFleet.BuyShip)走的是自己的入口 + ChangeShipOwnerAction.ApplyByTrade,
        //       原版不会因为条数拒绝。
        //   ⇒ 结论: **不冲突, 也不该对齐**(这是**有意**保留的差异):
        //     ① 原版对玩家舰队没有硬上限(玩家氏族的氏族级上限被 DailyTickClan 跳过), 所以不存在
        //        "我们的面板说还能买、原版却不让"或反之的情况;
        //     ② 原版 GetIdealShipNumber(MobileParty) 的常数 3 是给 AI 行为的软目标值, 不是"上限", 更不适合
        //        玩家主队(调它还会触发 FailedAssert); 原版氏族口径 (部队数 × 3) 与"海洋扩张"(港口/船坞/科技)
        //        是两套完全不同的设计意图 —— 我们的玩法就是"占港口、盖船坞、点科技才能养更多船";
        //     ③ 若将来真要显示原版参考, 正确做法是反射读 ClanShipOwnershipModel.GetIdealShipNumberForClan
        //        (DLC 类型, 按软依赖铁律必须反射), **不要**用 GetIdealShipNumber(MobileParty) ——
        //        那个重载在玩家主队上会走 FailedAssert。当前面板不显示该参考值。
        //     ④ 补充一条边界: 原版那条"氏族上限(部队数 × 3)"对**本国 AI 氏族**仍然生效(只排除玩家氏族),
        //        即 AI 领主能带几条船是原版在管; 我们这条 FleetCap 管的是**玩家自己**的舰队
        //        —— 两边管的是不同对象, 更谈不上冲突。
        //   ★ 本函数是"我国(玩家方)能养多少船"的**唯一权威口径**(仅当 k 是玩家王国时有意义; AI 王国不需要)。
        internal static int FleetCap(Kingdom k)
        {
            try
            {
                var s = NavalTech.Of(k);
                return 4 + (s != null ? s.FleetCap : 0) + NavalBridge.PortsOf(k).Count
                     + NavalBridge.ShipyardShipCapacity(k);
            }
            catch { return 4; }
        }

        // v4.273(A-2): 舰队上限的来源拆解(面板要"说明来源", 否则玩家只看到一个会变大的数字)
        internal static void FleetCapParts(Kingdom k, out int baseCap, out int techCap, out int portCap, out int yardCap)
        {
            baseCap = 4; techCap = 0; portCap = 0; yardCap = 0;
            try
            {
                var s = NavalTech.Of(k);
                techCap = s != null ? s.FleetCap : 0;
                portCap = NavalBridge.PortsOf(k).Count;
                yardCap = NavalBridge.ShipyardShipCapacity(k);
            }
            catch { }
        }

        // ==================== v4.273(审计 #2): 舰队查询的**两个语义入口** ====================
        //   【原 bug】v4.272 把本函数写成无条件 `return NavalBridge.MyShips();` —— 参数 k 被**完全忽略**。
        //   于是 NavalBattle 里的 `Ships(enemy)` / NavyPanelVM 里的 `Ships(owner)` 拿到的都是**我方舰队**:
        //     · NavalBattle.cs:82 `FleetPower(enemy)` -> 敌方战力 = 我方战力
        //     · NavalBattle.cs:106 `enShips` -> "敌方沉没/敌方阵亡/战利品"全按我方船算
        //     · NavyPanelVM.cs:339/1144 面板"敌方舰队 N 艘"显示的是自家数量
        //   而 v4.272 的注释还写着"敌国舰队仍然走 ShipsOf(Kingdom)", 代码根本没做到 —— 注释与实现脱节。
        //
        //   【修法】把"我方"和"某国"拆成两个**名字自己会说话**的入口, 调用点必须显式选边:
        //     · MyShips()          = 玩家主队 + 玩家氏族战帮 + 国防军 + 部队管理分裂队(fia_amg_)
        //     · ShipsOf(Kingdom k) = k 旗下**所有氏族战帮**的船(全国口径, 这才是"某国海军"该有的样子)
        //   Ships(k) 保留为**兼容壳**, 按"k 是不是玩家王国"分流(下面有明确判据), 避免漏改的调用点
        //   继续拿到错的集合。新代码请直接用 MyShips() / ShipsOf(k)。
        internal static List<Ship> MyShips()
        {
            return NavalBridge.MyShips();
        }

        // 某国的舰队(全国口径: 旗下所有氏族的战帮船)。这就是"敌方舰队"的正确定义。
        internal static List<Ship> ShipsOf(Kingdom k)
        {
            try { return NavalBridge.ShipsOf(k); }
            catch { return new List<Ship>(); }
        }

        // (v4.273 说明: 这里曾短暂加过一个 IsPlayerKingdom 辅助判据, 但全仓已有 NavalBehavior / DiploPlays /
        //  AiDirector 各自的同名实现, 且两个语义入口落地后本文件不再需要它 -> 已删除, 避免留死代码。)

        // 日维护费总额(第纳尔/日): 各舰单价之和 × 科技折扣 × 海军技能折扣
        internal static float DailyUpkeep(Kingdom k)
        {
            try
            {
                var ships = NavalBridge.MyShips();
                float sum = 0f;
                // v4.282: 单船维护走 `NavalTaskForce.ShipUpkeepOf` —— **含驻泊折扣**(进驻自有沿海城市的
                //   舰队, 其名下的船维护 -35%)。这是"进驻有意义"的第 1 项, 落点必须是日结真正扣钱的地方
                //   (NavalBehavior.UpkeepDaily 读的就是本函数), 否则折扣只是面板上的一行字。
                //   口径一致性: 面板/舰队小窗的"维护/日"也走同一个函数(见 NavalTaskForce.Stats 的 v4.282 注释)。
                for (int i = 0; i < ships.Count; i++) sum += NavalTaskForce.ShipUpkeepOf(ships[i]);
                var s = NavalTech.Of(k);
                if (s != null) sum *= s.Upkeep;
                // v4.270: 海战 DLC 的海军技能/特长 -> 维护费折扣(水手长系为主, 见 NavalAdmiral)
                //   没装 DLC 或没点海军技能时该乘数恒为 1.00, 老数值不变
                sum *= NavalAdmiral.UpkeepMul(NavalAdmiral.Of(Hero.MainHero));
                // 在建船也吃一半维护(船台占用/材料)
                //   v4.272(审计 A15): 原来这 3/日 是**光秃秃的常数**, 不吃科技折扣也不吃海军技能折扣
                //   (上面两行只乘在已有舰船上) -> 造船科技/技能对在建部分完全无效, 口径不一致。
                //   现在把同一个乘数也套到在建船头上。
                float orderMul = (s != null ? s.Upkeep : 1f) * NavalAdmiral.UpkeepMul(NavalAdmiral.Of(Hero.MainHero));
                for (int i = 0; i < Orders.Count; i++)
                {
                    var o = Orders[i];
                    // v4.278(追加 3): **等待接收方**的订单不收维护费 —— 船厂已经停工(工期冻结),
                    //   再每天收 3×折扣就是"卡住还要收钱"。同时它也不占"在建产能"的语义了。
                    if (o != null && o.WaitingReceiver) continue;
                    sum += 3f * orderMul;
                }
                return sum;
            }
            catch { return 0f; }
        }

        // 舰队总战力(海战抽象结算用): Σ 单舰战力 × 科技战力乘数
        internal static float FleetPower(Kingdom k)
        {
            try
            {
                var ships = NavalBridge.MyShips();
                float sum = 0f;
                for (int i = 0; i < ships.Count; i++) sum += NavalBridge.ShipPowerScore(ships[i]);
                var s = NavalTech.Of(k);
                if (s != null) sum *= s.Power;
                return sum;
            }
            catch { return 0f; }
        }

        // 海运容量(载重总和, 接 Caravans/MarketSim 口径用)
        internal static float CargoCapacity(Kingdom k)
        {
            try
            {
                var ships = NavalBridge.MyShips();
                float sum = 0f;
                for (int i = 0; i < ships.Count; i++) sum += NavalBridge.Cargo(ships[i]);
                var s = NavalTech.Of(k);
                if (s != null) sum *= s.Cargo;
                // v4.270: 海战 DLC 的海军技能/特长 -> 海运容量加成(船长系为主, 见 NavalAdmiral)
                sum *= NavalAdmiral.CargoMul(NavalAdmiral.Of(Hero.MainHero));
                return sum;
            }
            catch { return 0f; }
        }

        // v4.270: 运兵能力(能一次运多少**人**) —— 与"载重"是两个量纲:
        //   载重(CargoCapacity)算货物, 这里算人: Σ 各舰主甲板载员 × 科技海运乘数 × 海军技能运兵乘数。
        //   海运(NavalTransport)判定"舰队载不载得下这支军团"用这个口径。
        internal static int TroopCapacity(Kingdom k)
        {
            try
            {
                var ships = NavalBridge.MyShips();
                int crew = 0;
                for (int i = 0; i < ships.Count; i++) crew += NavalBridge.Crew(ships[i]);
                var s = NavalTech.Of(k);
                float mul = (s != null ? s.Cargo : 1f) * NavalAdmiral.TroopMul(NavalAdmiral.Of(Hero.MainHero));
                return (int)Math.Round(crew * mul);
            }
            catch { return 0; }
        }

        // ==================== 造船 ====================

        // 可造的船体清单(某港口): 该港船坞在售的船 + 我们的船坞等级/科技门槛过滤
        //   返回 (hullId, hullName, tier, price) —— price 已含科技折扣
        internal class Offer
        {
            internal string HullId = "";
            internal string HullName = "";
            internal int Tier = 0;
            internal int Price = 0;
            internal int Days = 1;
            internal int Value = 0;
            internal int Crew = 0;
            internal float HitPoints = 0f;
            internal float Speed = 0f;
            internal bool Buildable = false;
            internal string Why = "";
            internal Ship Sample = null;   // 港口待售的同型船(用于文案: 直接买现船)
        }

        internal static List<Offer> Offers(Settlement port, Kingdom k)
        {
            var list = new List<Offer>();
            try
            {
                if (port == null || k == null) return list;
                var sum = NavalTech.Of(k);
                int lvl = NavalBridge.ShipyardLevel(port);
                int maxTier = NavalTech.MaxHullTier(lvl, sum);

                // 1) 港口船坞现有的待售船(可直接买走)
                var avail = NavalBridge.AvailableShips(port.Town);
                for (int i = 0; i < avail.Count; i++)
                {
                    var sh = avail[i];
                    if (sh == null) continue;
                    int tier = NavalBridge.HullType(sh);
                    var o = new Offer
                    {
                        HullId = NavalBridge.HullId(sh),
                        HullName = NavalBridge.HullName(sh),
                        Tier = tier,
                        Value = NavalBridge.HullValue(sh),
                        Crew = NavalBridge.Crew(sh),
                        HitPoints = NavalBridge.MaxHitPoints(sh),
                        Speed = NavalBridge.Speed(sh),
                        Sample = sh
                    };
                    float basePrice = NavalBridge.ShipBuyPrice(sh);
                    if (sum != null) basePrice *= sum.BuildCost;
                    o.Price = (int)Math.Round(basePrice);
                    o.Days = 0;   // 0 = 现货, 立刻能开走
                    o.Buildable = true;
                    o.Why = "现货";
                    list.Add(o);
                }

                // 2) 该文化能造、但港口暂时没有现船的型号 -> 我们的"下单建造"(占队列, 比现货便宜 10%)
                var culture = port.Culture;
                if (culture != null)
                {
                    var hulls = culture.AvailableShipHulls;
                    if (hulls != null)
                    {
                        for (int i = 0; i < hulls.Count; i++)
                        {
                            var h = hulls[i];
                            if (h == null) continue;
                            if (h.StringId != null && (h.StringId.Contains("storyline") || h.StringId.Contains("burning"))) continue;  // 剧情/燃烧专用船不开放
                            int tier = (int)h.Type;
                            if (tier > maxTier)
                            {
                                // 门槛不够: 也列出, 但标注原因(玩家能看到"要几级船坞/哪项科技")
                                list.Add(new Offer
                                {
                                    HullId = h.StringId,
                                    HullName = h.Name != null ? h.Name.ToString() : h.StringId,
                                    Tier = tier,
                                    Value = h.Value,
                                    Crew = h.TotalCrewCapacity,
                                    HitPoints = h.MaxHitPoints,
                                    Speed = h.BaseSpeed,
                                    Buildable = false,
                                    Why = (lvl < tier + 1)
                                        ? ("需 " + (tier + 1) + " 级造船厂(当前 " + lvl + " 级)")
                                        : "需海军科技解锁(铁甲舰/前无畏舰/无畏舰/战列舰)"
                                });
                                continue;
                            }
                            // 已有同型现船就不重复列"下单建造"
                            bool dup = false;
                            for (int j = 0; j < list.Count; j++) if (list[j].HullId == h.StringId) { dup = true; break; }
                            if (dup) continue;

                            float bp = h.Value * 1.35f;    // 下单建造 = 1.35 倍船价(比现货 1.5 倍便宜 10%)
                            if (sum != null) { bp *= sum.BuildCost; }
                            float spd = (sum != null && sum.BuildSpeed > 0f) ? sum.BuildSpeed : 1f;
                            int days = Math.Max(1, (int)Math.Round((6 + tier * 8) / spd));
                            list.Add(new Offer
                            {
                                HullId = h.StringId,
                                HullName = h.Name != null ? h.Name.ToString() : h.StringId,
                                Tier = tier,
                                Value = h.Value,
                                Crew = h.TotalCrewCapacity,
                                HitPoints = h.MaxHitPoints,
                                Speed = h.BaseSpeed,
                                Price = (int)Math.Round(bp),
                                Days = days,
                                Buildable = true,
                                Why = "下单建造 " + days + " 天"
                            });
                        }
                    }
                }
                // v4.268: 稳定排序 —— List.Sort 不稳定, 同样输入可能给出不同顺序, 而面板的点击靠"行下标"
                //   反查 Offers(), 顺序一变就会点 A 行买到 B 船。这里用 (可建造, 价格, 船体 id) 三级键保证确定性。
                list.Sort(delegate (Offer a, Offer b)
                {
                    if (a.Buildable != b.Buildable) return a.Buildable ? -1 : 1;
                    int c = a.Price.CompareTo(b.Price);
                    if (c != 0) return c;
                    return string.CompareOrdinal(a.HullId ?? "", b.HullId ?? "");
                });
            }
            catch (Exception ex) { DLog.Force("海军: 生成船型清单失败 " + ex.Message); }
            return list;
        }

        // 买现船: 直接扣钱 + 转移所有权
        internal static string BuyShip(Settlement port, Ship ship, Kingdom k)
        {
            try
            {
                if (!NavalBridge.Usable) return "未检测到海战 DLC";
                if (port == null || ship == null || k == null) return "参数错误";
                var sum = NavalTech.Of(k);
                if (MyShips().Count >= FleetCap(k)) return "已达舰队上限 " + FleetCap(k) + " 条(研究海军科技或占领更多港口可提升)";

                float price = NavalBridge.ShipBuyPrice(ship);
                if (sum != null) price *= sum.BuildCost;
                int cost = (int)Math.Round(price);
                if (EconomyWorld.Treasury.Gold < cost) return "国库不足(需 " + cost + " 第纳尔)";

                var newOwner = OwnerPartyFor(port, k);
                if (newOwner == null) return "找不到接收舰船的部队";

                EconomyWorld.TreasurySpend(cost);
                Fiscal.AddCourt(cost);      // 计入财政"宫廷与外交"支出(购船 = 一次性开销)
                ChangeShipOwnerAction.ApplyByTrade(newOwner, ship);
                DLog.Force("海军: 购入 " + NavalBridge.HullName(ship) + " @ " + NavalBridge.NameOf(port) + " 花费 " + cost);
                return "已购入「" + NavalBridge.HullName(ship) + "」(花费 " + cost + " 第纳尔)";
            }
            catch (Exception ex) { return "购船失败: " + ex.Message; }
        }

        // 下单建造: 扣钱 + 入队(日结推进)
        internal static string OrderShip(Settlement port, Offer o, Kingdom k)
        {
            try
            {
                if (!NavalBridge.Usable) return "未检测到海战 DLC";
                if (port == null || o == null || k == null) return "参数错误";
                if (!o.Buildable) return "不可建造: " + o.Why;
                if (MyShips().Count + Orders.Count >= FleetCap(k)) return "已达舰队上限 " + FleetCap(k) + " 条(含在建)";
                if (Orders.Count >= 4) return "同时最多 4 艘在建";
                if (EconomyWorld.Treasury.Gold < o.Price) return "国库不足(需 " + o.Price + " 第纳尔)";

                EconomyWorld.TreasurySpend(o.Price);
                Fiscal.AddCourt(o.Price);
                Orders.Add(new Order
                {
                    PortId = port.StringId,
                    PortName = NavalBridge.NameOf(port),
                    HullId = o.HullId,
                    HullName = o.HullName,
                    Paid = o.Price,
                    DaysLeft = o.Days,
                    DaysTotal = o.Days,
                    Tier = o.Tier,
                    // v4.272(UI 需求): 记下船体静态属性, 让舰队页的"在建"行能显示 船员/耐久/战力/维护
                    Crew = o.Crew,
                    HitPoints = o.HitPoints,
                    HullValue = o.Value
                });
                DLog.Force("海军: 下单建造 " + o.HullName + " @ " + NavalBridge.NameOf(port) + " 花费 " + o.Price + " 工期 " + o.Days + " 天");
                return "已下单「" + o.HullName + "」: " + o.Price + " 第纳尔, " + o.Days + " 天完工";
            }
            catch (Exception ex) { return "下单失败: " + ex.Message; }
        }

        // 日结: 推进造船队列, 完工的船交给玩家
        internal static void DailyShipbuilding(Kingdom k)
        {
            try
            {
                if (Orders.Count == 0) return;
                for (int i = Orders.Count - 1; i >= 0; i--)
                {
                    var o = Orders[i];
                    if (o == null) { Orders.RemoveAt(i); continue; }

                    // ==================== v4.278(追加 3 ★): "接收方缺失"不再卡 DaysLeft ====================
                    //   【原实现】`o.DaysLeft = 1; continue;` —— 意图是"卡一天, 明天再试"(注释也这么写),
                    //   但配合 `Progress = 1 - DaysLeft/DaysTotal` 就成了**永久卡死**:
                    //     · DaysLeft 被摁在 1 -> Progress 永远 > 0 -> 取消订单永远退不到全款;
                    //     · 订单永久占用 Orders 槽(上限 4), 每天还照收 3×折扣维护费;
                    //     · 面板上它显示成"进度 90% 快完工了"(其实是永远完不了)。
                    //   【修法】显式进入"无限等待"状态: WaitingReceiver = true —— **不推进工期、不收维护费、
                    //   进度冻结在卡住前那一刻**, 并且只要接收方一恢复就自动继续(下一天照常交付)。
                    //   为什么不直接 `RefundOrder(o, 1f)` 全款退并移除: 触发条件是"我方一处港口/战帮都找不到"
                    //   (OwnerPartyFor 全链兜底失败), 那通常是**读档中的过渡态**, 不是玩家的意愿;
                    //   直接退单会让"读档时刚好没找到接收方"变成订单凭空消失。而"无限等待 + 全款可退"
                    //   既保住了玩家的钱, 也把占用与收费都停掉了(玩家要拿回钱只需点一次[取消])。
                    Settlement port = null;
                    try { port = Settlement.Find(o.PortId); } catch { }
                    var owner = port != null ? OwnerPartyFor(port, k) : OwnerPartyFor(null, k);
                    if (owner == null)
                    {
                        if (!o.WaitingReceiver)
                        {
                            o.WaitingReceiver = true;
                            DLog.Force("海军: 造船订单「" + o.HullName + "」暂时找不到接收方 —— 已转入等待"
                                + "(工期冻结 · 不再收维护费 · 可[取消]全款退回 " + (int)o.Paid + ")");
                        }
                        continue;
                    }
                    if (o.WaitingReceiver)
                    {
                        o.WaitingReceiver = false;   // 接收方回来了 -> 恢复推进
                        DLog.Force("海军: 造船订单「" + o.HullName + "」接收方已恢复, 继续建造");
                    }

                    o.DaysLeft--;
                    if (o.DaysLeft < 0) o.DaysLeft = 0;
                    o.DaysDone++;                              // v4.278: 真实推进天数(进度口径, 见 Order.Progress)
                    if (o.DaysLeft > 0) continue;

                    // 完工: 造船体
                    var hull = MBObjectManager.Instance.GetObject<ShipHull>(o.HullId);
                    if (hull == null)
                    {
                        // v4.272(审计 A12): 船体在数据里不存在(改版/删船体)导致订单作废 -> **必须退款**,
                        //   否则玩家的钱凭空蒸发。这是"非玩家意愿的取消", 所以按**全额**退。
                        int back = RefundOrder(o, 1f);
                        Orders.RemoveAt(i);
                        DLog.Force("海军: 船体 " + o.HullId + " 不存在, 订单取消并全额退款 " + back);
                        if (back > 0) { try { MapSelection.Message("造船订单作废(" + o.HullName + " 型号已不存在), 已全额退款 " + back + " 第纳尔"); } catch { } }
                        continue;
                    }

                    var ship = new Ship(hull);
                    ChangeShipOwnerAction.ApplyByProduction(owner, ship);
                    Orders.RemoveAt(i);
                    DLog.Force("海军: " + o.HullName + " 完工交付 @ " + (port != null ? NavalBridge.NameOf(port) : "?"));
                }
            }
            catch (Exception ex) { DLog.Force("海军: 造船日结异常 " + ex.Message); }
        }

        // ==================== v4.272(审计 A12): 取消订单 + 退款 ====================
        //   原来两条 RemoveAt 路径(o==null / hull==null)**都不退款**, 而且全仓没有任何"取消造船"入口
        //   -> 玩家的钱进了队列就出不来。
        //   口径(可调, 二选一, 这里用"按已推进工期比例退"):
        //     退款 = Paid × (已完工比例) = Paid × Progress
        //   即"船厂已经干了多少活, 就付多少钱", 没开工全退、快完工退得多。
        //   若想改"全额退"(玩家体验更宽), 把 ratio 传 1f 即可 —— 作废路径就是这么调的。
        //   返回值 = 实际退回国库的第纳尔。
        internal static int RefundOrder(Order o, float ratio)
        {
            try
            {
                if (o == null) return 0;
                if (ratio < 0f) ratio = 0f;
                if (ratio > 1f) ratio = 1f;
                int back = (int)Math.Round(o.Paid * ratio);
                if (back <= 0) return 0;
                EconomyWorld.TreasuryAdd(back);
                Fiscal.AddCourt(-back);   // 负值 = 收入(与购船/下单的 AddCourt(正) 同一科目, 对冲干净)
                DLog.Force("海军: 取消订单 " + o.HullName + " 退款 " + back + "(已付 " + (int)o.Paid + " × " + ratio.ToString("0.00") + ")");
                return back;
            }
            catch { return 0; }
        }

        // 玩家取消在建船(i = Orders 下标)。UI 侧接法见文件末尾说明。
        internal static string CancelOrder(int i)
        {
            try
            {
                if (i < 0 || i >= Orders.Count) return "没有这条造船订单";
                var o = Orders[i];
                if (o == null) { Orders.RemoveAt(i); return "订单已清理"; }
                int back = RefundOrder(o, o.Progress);   // 按已推进的工期比例退
                Orders.RemoveAt(i);
                return "已取消「" + o.HullName + "」的建造订单, 退回 " + back + " 第纳尔(按已完工 "
                    + (int)Math.Round(o.Progress * 100f) + "% 折算)";
            }
            catch (Exception ex) { return "取消订单失败: " + ex.Message; }
        }

        // 接收舰船的部队。v4.272(审计 A3): 增加"优先还给原主"参数 + 修掉永不生效的 fallback 链。
        //   · preferPartyId 非空(海运还船) -> 优先还给这条船**原来的主人**;
        //   · 原主没了 -> 按 玩家主队 / 玩家氏族战帮 / 港口 / 王国任意氏族部队 依次兜底。
        //   v4.270: 改为 internal —— 海运(NavalTransport)到达后"还船"复用同一套接收方选取逻辑。
        internal static PartyBase ReceiverParty(Kingdom k, Settlement port)
        {
            return OwnerPartyFor(port, k, null);
        }

        internal static PartyBase ReceiverParty(Kingdom k, Settlement port, string preferPartyId)
        {
            return OwnerPartyFor(port, k, preferPartyId);
        }

        // 按 id 找一支"船还在名下"的部队(海运记的是 PartyBase.Id = MobileParty.StringId)。
        //   海运途中"还船"必须按 id 找, 不能按对象引用 —— 存档往返后引用会换新实例。
        //   v4.272: **不要求 IsActive** —— 部队被歼灭时 IsActive=false 但 Party/船可能还在,
        //   这时更应该把船还给原主(而不是漏到 fallback 的玩家主队, 那会重新打开"洗船"的口子)。
        //   只有 Party == null(真被销毁, 船也随之 DestroyShipAction 掉了)才算找不到。
        internal static MobileParty FindMyPartyById(string id)
        {
            try
            {
                if (string.IsNullOrEmpty(id)) return null;
                var all = MobileParty.All;
                if (all == null) return null;
                for (int i = 0; i < all.Count; i++)
                {
                    var p = all[i];
                    if (p == null || p.Party == null) continue;
                    if (p.StringId == id) return p;
                }
            }
            catch { }
            return null;
        }

        private static PartyBase OwnerPartyFor(Settlement port, Kingdom k)
        {
            return OwnerPartyFor(port, k, null);
        }

        private static PartyBase OwnerPartyFor(Settlement port, Kingdom k, string preferPartyId)
        {
            try
            {
                // 0) v4.272(A3): 还给原主(海运还船)。注意这里**必须是原主**,
                //    否则"海运 -> 召回"反复操作会把全国 AI 领主的船搬进玩家部队(审计 A3 的洗船漏洞)。
                if (!string.IsNullOrEmpty(preferPartyId))
                {
                    var origin = FindMyPartyById(preferPartyId);
                    if (origin != null && origin.Party != null) return origin.Party;
                }

                var main = MobileParty.MainParty;
                if (main != null && main.Party != null) return main.Party;

                // v4.272(A3): 下面这段 fallback 原来**从不执行**(第一行就 return main.Party 了),
                //   注释里承诺的"自动分散到船最少的氏族部队/港口现货"等于空头支票。现在它真的会跑到。
                var pc = Clan.PlayerClan;
                if (pc != null)
                {
                    PartyBase best = null; int bestN = int.MaxValue;
                    var wpc = pc.WarPartyComponents;
                    if (wpc != null)
                    {
                        for (int i = 0; i < wpc.Count; i++)
                        {
                            var w = wpc[i];
                            if (w == null || w.MobileParty == null || w.MobileParty.Party == null) continue;
                            int n = NavalBridge.ShipsOf(w.MobileParty).Count;
                            if (n < bestN) { bestN = n; best = w.MobileParty.Party; }
                        }
                    }
                    if (best != null) return best;
                }
                if (port != null && port.Party != null) return port.Party;
                if (k != null)
                {
                    var clans = k.Clans;
                    if (clans != null)
                    {
                        for (int i = 0; i < clans.Count; i++)
                        {
                            var c = clans[i];
                            if (c == null) continue;
                            var w2 = c.WarPartyComponents;
                            if (w2 == null) continue;
                            for (int j = 0; j < w2.Count; j++)
                                if (w2[j] != null && w2[j].MobileParty != null && w2[j].MobileParty.Party != null)
                                    return w2[j].MobileParty.Party;
                        }
                    }
                }
            }
            catch { }
            return null;
        }

        // ==================== 封锁 ====================

        // v4.272(UI 需求 + 审计 A11): 封锁收益估算 —— **只读**, 与日结共用同一段算法(NavalBlockade.Estimate)。
        //   面板拿它显示"昨日我收 / 敌损"两列(而不是只能显示繁荣度)。
        //   口径与真实结算完全一致(同一个 Estimate), A4 的 eff² 修复也一起生效。
        //   Ok=false 时 Why 是"现在打不了/结算不了"的原因(已停战/没船/港口无主...)。
        internal static NavalBlockade.EstimateResult EstimateBlockadeDaily(Settlement enemyPort)
        {
            try
            {
                Kingdom k = null;
                try { var b = NationalWillOrders.Behavior; k = b != null ? b.NationKingdom : null; } catch { }
                return NavalBlockade.Estimate(enemyPort, k);
            }
            catch { return new NavalBlockade.EstimateResult(); }
        }

        // ==================== v4.278(第 9 项): 出击冷却/补给费的**转发层** ====================
        //   口径与实现全在 `NavalTaskForce`(那里有冷却时长与费用的完整依据), 这里只是给 UI 一个
        //   "海军页那一侧"的入口名 —— 面板代码全在 NavalFleet 这一族里读数据, 保持同一习惯。
        //   【UI 接线(两行, 交给负责 UI 的那位)】在 `UI\NavyPanelVM.Sortie()` 的
        //     `if (NavalFleet.MyShips().Count == 0) {...}` 之后插入:
        //         string se = NavalFleet.SortieBlockedReason();
        //         if (!string.IsNullOrEmpty(se)) { _status = se; MapSelection.Message("海军: " + se); Refresh(); return; }
        //    并在 `var r = NavalBattle.Resolve(k, enemy, target);` 之后插一行抹掉保底:
        //         NavalFleet.SortieFloorAdjust(r != null ? r.Loot : 0);
        //    按钮提示/灰化可接 `NavalFleet.SortieCooldownHoursLeft()`(>0 时显示"补给中 N 小时")。
        internal static double SortieCooldownHoursLeft() { return NavalTaskForce.SortieCooldownLeftHours(); }

        internal static int SortieSupply() { return NavalTaskForce.SortieSupplyCost(); }

        // "" = 可以出击(此时**已经扣了补给费并开始冷却**); 否则 = 中文原因(不可出击)
        internal static string SortieBlockedReason() { return NavalTaskForce.TryConsumeSortie(); }

        // 出击结算后调用一次: 把 NavalBattle 那个"保底 300"里"凭空来的部分"从国库收回去 ⇒ 净收益 = 真实战果。
        //   返回实际回冲的金额(供 UI 拼状态串, 0 = 没回冲)。
        internal static int SortieFloorAdjust(int actualLoot) { return NavalTaskForce.SortieFloorAdjust(actualLoot); }

        // 对敌国港口实施封锁。费用: 一次调动费(500) + 每日维持(按我方舰队规模)
        //
        // ==================== v4.278(第 5 项 ★): 封锁旗标必须与**地图上的舰队实体**挂钩 ====================
        //   【原缺陷】v4.272 的校验只有"我方舰船 ≥1"(MyShips().Count)—— 那是**氏族船坞里的船**,
        //   与"有没有一支舰队在那个港口外面"完全无关。于是: 面板点[封锁] -> 旗标立刻置上 ->
        //   日结天天按 NavalBlockade.Daily 产战果, 而地图上的舰队可能还停在母港(出击里
        //   `UI\NavyPanelVM.cs:1884-1889` 对 OrderToPort 的失败串是**静默丢弃**的, 玩家看不到)。
        //   ⇒ 面板显示"封锁中", 实际没人去封锁。这是"旗标与实体解耦"。
        //   【修法】加两道校验, 缺一不可:
        //     ① 必须存在**在编舰队**(Forces 非空) —— 没有舰队就没有封锁能力的载体;
        //     ② 必须有一支舰队"以该港为目标"(Force.OrderPortId == 该港) 或**已经离得够近**
        //        (直线 ≤ BlockadeNearDist)。判据用的是 Force 上已有的信息, 不新增任何状态。
        //     失败时: 能自动调度就自动调度(见下), 调度不了才返回**明确的中文原因**(面板 _status 会显示)。
        //   【为什么"自动派最近的舰队过去"而不是直接拒绝】海军页封锁按钮(NavyPanelVM.ActionBlockade
        //     -> SetBlockade)本身**不下达舰队移动命令**, 若直接拒绝, 玩家点封锁就永远是失败;
        //     而"封锁"这个战略决定本来就该产生实体后果。所以这里补上那一半:
        //     挑离目标最近的**在编舰队**, 用与右键菜单/出击完全相同的 `NavalTaskForce.OrderToPort(..., true)`
        //     命令它开过去 —— 玩家在地图上能看见舰队起航, 封锁旗标与实体从此一致。
        //     若一支舰队都没有 -> 返回可操作的提示(去舰队页组建), 不产生任何扣费。
        //   【重设封锁】(第 5 项后半): 原实现无条件覆盖 BlockadePortId/Name/Kingdom/SinceDay/Damage,
        //     旧目标的 500 第纳尔**不退也不提示**, 累计战果还被清零。现在:
        //     · 目标没变 -> 直接返回"已在封锁中"(**不重复扣 500**);
        //     · 目标变了 -> 返回值里如实写明"原封锁目标 X(累计战果 N)已被替换", 玩家看得见这笔账的去向。
        internal const float BlockadeNearDist = 60f;   // 视作"已经在封锁海域"的直线距离(与 DiagLogOne 的 20 口径同量级, 留出海域范围)

        internal static string SetBlockade(Settlement port, Kingdom k)
        {
            try
            {
                if (!NavalBridge.Usable) return "未检测到海战 DLC";
                if (port == null || k == null) return "参数错误";
                if (!port.HasPort || !port.IsTown) return "该聚落不是港口";
                var owner = NavalBridge.KingdomOf(port);
                if (owner == null) return "该港口无主";
                if (owner == k) return "不能封锁自己的港口";
                if (!k.IsAtWarWith(owner)) return "未与该国交战, 不能封锁其港口";
                if (MyShips().Count == 0) return "我方没有舰船, 无法实施封锁(先造船)";

                // v4.278: 同一目标重复点 -> 直接说明, 不再扣一次 500(原实现会静默重复收费)
                if (IsBlockading && BlockadePortId == port.StringId)
                    return "已在封锁 " + BlockadePortName + "(累计战果 " + (int)BlockadeDamage + " 第纳尔)—— 无需重复下令";

                // v4.278: 换目标要如实告知旧账(见上面【重设封锁】)
                string switchNote = "";
                if (IsBlockading && BlockadePortId != port.StringId)
                    switchNote = "原封锁目标「" + BlockadePortName + "」(累计战果 " + (int)BlockadeDamage + " 第纳尔)已被替换; ";

                // ---- v4.278(第 5 项 校验 ①②): 至少一支在编舰队以该港为目标, 或已经在附近 ----
                string why = EnsureFleetOnStation(port);
                if (!string.IsNullOrEmpty(why)) return switchNote + why;

                int cost = 500;
                if (EconomyWorld.Treasury.Gold < cost) return switchNote + "国库不足(需 " + cost + " 第纳尔)";
                EconomyWorld.TreasurySpend(cost);
                Fiscal.AddCourtPaid(cost);   // v4.278(追加 1): 显式记"支出侧"(原来靠 AddCourt 正数推断)

                BlockadePortId = port.StringId;
                BlockadePortName = NavalBridge.NameOf(port);
                BlockadeKingdomId = owner.StringId;
                BlockadeKingdomName = owner.Name != null ? owner.Name.ToString() : owner.StringId;
                try { BlockadeSinceDay = Politics.Today(); } catch { BlockadeSinceDay = 0; }
                BlockadeDamage = 0f;
                DLog.Force("海军: 对 " + BlockadeKingdomName + " 的 " + BlockadePortName + " 实施封锁");
                return switchNote + "已封锁 " + BlockadeKingdomName + " 的 " + BlockadePortName + "(500 第纳尔/次)";
            }
            catch (Exception ex) { return "封锁失败: " + ex.Message; }
        }

        // v4.278(第 5 项): "封锁旗标必须有舰队实体支撑"的唯一判据实现。
        //   返回 "" = 已有一支舰队在位(或刚刚被派过去); 否则返回**给玩家看的**中文原因。
        private static string EnsureFleetOnStation(Settlement port)
        {
            try
            {
                var list = NavalTaskForce.Alive();
                if (list.Count == 0) return "没有在编舰队 —— 请先到海军页[舰队]页签组建一支(封锁只能由地图上的实体舰队执行)";

                bool onStation = false;
                bool anyUsable = false;
                for (int i = 0; i < list.Count; i++)
                {
                    var p = NavalTaskForce.PartyOf(list[i]);
                    if (p == null) continue;
                    anyUsable = true;
                    if (list[i].OrderPortId == port.StringId) { onStation = true; break; }
                    try
                    {
                        if (p.Position.Distance(port.PortPosition) <= BlockadeNearDist) { onStation = true; break; }
                    }
                    catch { }
                }
                if (onStation) return "";
                if (!anyUsable) return "在编舰队都不在场(可能已被歼灭), 无法实施封锁";

                // 没有舰队在目标海域 -> 派最近的一支过去(与右键菜单/出击同一条命令入口)
                NavalTaskForce.Force best = null;
                float bestD = float.MaxValue;
                for (int i = 0; i < list.Count; i++)
                {
                    var p = NavalTaskForce.PartyOf(list[i]);
                    if (p == null) continue;
                    float d;
                    try { d = p.Position.Distance(port.PortPosition); }
                    catch { d = float.MaxValue; }
                    if (d < bestD) { bestD = d; best = list[i]; }
                }
                if (best == null) return "找不到可调动的舰队";
                var bp = NavalTaskForce.PartyOf(best);
                if (bp == null) return "找不到可调动的舰队";
                string r = NavalTaskForce.OrderToPort(bp, port, true);
                // OrderToPort 失败 -> 把它的原因原样上报(它已经写明"未与该国交战/不是港口"等)
                if (!string.IsNullOrEmpty(r) && r.IndexOf("已奉命", StringComparison.Ordinal) < 0) return r;
                try
                {
                    MapSelection.Message("海军: 已命令舰队「" + MapSelection.NameOf(bp) + "」开往 "
                        + NavalBridge.NameOf(port) + " 海域执行封锁(距离约 " + (int)bestD + ")");
                }
                catch { }
                return "";
            }
            catch (Exception ex) { return "封锁校验异常: " + ex.Message; }
        }

        internal static string ClearBlockade()
        {
            try
            {
                if (string.IsNullOrEmpty(BlockadePortId)) return "当前没有封锁目标";
                // v4.272(A11): 解除封锁 -> 撤销"由封锁带来"的那条贸易禁运(玩家手动挂的禁运不动)
                NavalBlockade.EmbargoOff();
                string s = "已解除对 " + BlockadePortName + " 的封锁(累计战果 " + (int)BlockadeDamage + " 第纳尔)";
                DLog.Force("海军: 解除封锁 " + BlockadePortName);
                BlockadePortId = "";
                BlockadePortName = "";
                BlockadeKingdomId = "";
                BlockadeKingdomName = "";
                BlockadeSinceDay = 0;
                // v4.278(追加 5): 原来**漏了这一行** —— SetBlockade 会重置 BlockadeDamage 而 ClearBlockade 不会,
                //   于是面板上"累计战果"是跨多次封锁的混合值, 与同样被清 0 的 BlockadeSinceDay(封锁天数)不自洽
                //   (会出现"已封锁 0 天 · 累计战果 12000"这种自相矛盾的一行)。
                BlockadeDamage = 0f;
                return s;
            }
            catch { return "解除封锁失败"; }
        }

        internal static bool IsBlockading { get { return !string.IsNullOrEmpty(BlockadePortId); } }

        internal static Settlement BlockadeTarget()
        {
            try { return string.IsNullOrEmpty(BlockadePortId) ? null : Settlement.Find(BlockadePortId); }
            catch { return null; }
        }

        // ==================== 存档 ====================
        // v4.275: 键结构 = **按段拼接**(段间 ';', 段内 '|')。每个段前缀 ↔ 解析函数 ↔ 坏段降级行为:
        //   V2 / V1   版本标记      Load 内联              不解析数据; 只决定 O 段是否含 3 个新字段
        //   upkeep=   ParseInt      Load 内联              昨日维护费回 0(Reset 已清), 其余段照常
        //   bgain=    ParseInt      Load 内联              昨日封锁收益回 0
        //   bloss=    ParseInt      Load 内联              昨日对方损失回 0
        //   bem=      ParseInt      Load 内联              禁运归属标记回 false
        //   B=<6 字段> ParseSegmentBlockade 该段整段丢弃 -> 无封锁(其余段照常; 船/战报/海运不受影响)
        //   O=<11 字段> ParseSegmentOrder   该条订单丢弃 -> 该船丢失(不退款, 只能靠日志追溯)
        //   L=<转义文本> ParseSegmentBattle 该条战报丢弃 -> 只少一条记录
        //   T=<15 字段> NavalTransport.LoadSegment 该条运输队丢弃 + 明确日志(船归属随之丢失)
        //   未知前缀    —                      原样跳过(新版本新增段落到老版本时不炸档)
        //   ★ 整键**完全不可识别**时才会 Reset()(并保留一条 Force 日志) —— 见 Load 末尾。
        // 段内字段形状:
        //   B=<portId>|<portName>|<kingdomId>|<kingdomName>|<sinceDay>|<damage>
        //   O=<portId>|<portName>|<hullId>|<hullName>|<paid>|<daysLeft>|<daysTotal>|<tier>|<crew>|<hitPoints>|<hullValue>
        //   L=<一行战报, 换行替换为 %0A 转义>
        // 版本说明: v4.272 之前是 V1 + 订单段 8 字段; V2 的订单段追加了 crew/HitPoints/HullValue 三个
        //   (UI 的"在建"行要显示 船员/耐久/战力/维护)。**老档(V1 / 无版本标记)按 0 容错, 并从 hullId
        //   反查 ShipHull 回填这三项** —— 所以老档读进来也不会出现空列。
        private const string SaveVerMarker = "V2";
        private const string LegacyVerMarker = "V1";

        internal static string Save()
        {
            try
            {
                var sb = new StringBuilder();
                sb.Append(SaveVerMarker);
                sb.Append(";upkeep=").Append(LastUpkeepPaid);
                sb.Append(";bgain=").Append(((int)LastBlockadeGain));
                sb.Append(";bloss=").Append(((int)LastBlockadeLoss));
                sb.Append(";bemb=").Append(BlockadeEmbargoOwned ? 1 : 0);
                // v4.290: 出击冷却的绝对时间戳(游戏秒)。原先不落盘 -> 读档后 Reset 清成 -1
                //   -> "出击→存档→读档→出击"无限刷钱。存绝对值, 读回时若已过期自然就绪。
                sb.Append(";srt=").Append((long)NavalTaskForce.SortieReadySecRaw);
                if (IsBlockading)
                {
                    sb.Append(";B=").Append(Escape(BlockadePortId)).Append('|').Append(Escape(BlockadePortName))
                      .Append('|').Append(Escape(BlockadeKingdomId)).Append('|').Append(Escape(BlockadeKingdomName))
                      .Append('|').Append(BlockadeSinceDay).Append('|').Append(((int)BlockadeDamage));
                }
                for (int i = 0; i < Orders.Count; i++)
                {
                    var o = Orders[i];
                    if (o == null) continue;
                    sb.Append(";O=").Append(Escape(o.PortId)).Append('|').Append(Escape(o.PortName))
                      .Append('|').Append(Escape(o.HullId)).Append('|').Append(Escape(o.HullName))
                      .Append('|').Append(((int)o.Paid)).Append('|').Append(o.DaysLeft)
                      .Append('|').Append(o.DaysTotal).Append('|').Append(o.Tier)
                      // v4.272: 追加 3 个字段(老档读入时按 0 容错并回填)
                      .Append('|').Append(o.Crew).Append('|').Append(((int)o.HitPoints)).Append('|').Append(o.HullValue)
                      // v4.278(追加 3): 再追加 2 个(进度口径 + 等待接收方标记)
                      .Append('|').Append(o.DaysDone).Append('|').Append(o.WaitingReceiver ? 1 : 0);
                }
                for (int i = 0; i < Battles.Count; i++)
                {
                    string b = Battles[i];
                    if (string.IsNullOrEmpty(b)) continue;
                    sb.Append(";L=").Append(Escape(b));
                }
                // v4.270: 海运运输队(段前缀 T=) —— 与老档兼容: 没有 T 段 = 没有运输队
                sb.Append(NavalTransport.Serialize());
                // v4.276: 舰队实体(段前缀 F=) —— 与老档兼容: 没有 F 段 = 没有舰队
                sb.Append(NavalTaskForce.Serialize());
                return sb.ToString();
            }
            catch (Exception ex) { DLog.Force("海军: 存档序列化失败 " + ex.Message); return ""; }
        }

        // 从船体 id 反查船体静态属性(老档回填订单的 crew/耐久/船价用)
        private static void FillHullStats(Order o)
        {
            try
            {
                if (o == null || string.IsNullOrEmpty(o.HullId)) return;
                var h = MBObjectManager.Instance.GetObject<ShipHull>(o.HullId);
                if (h == null) return;
                if (o.Crew <= 0) o.Crew = h.TotalCrewCapacity;
                if (o.HitPoints <= 0f) o.HitPoints = h.MaxHitPoints;
                if (o.HullValue <= 0) o.HullValue = h.Value;
            }
            catch { }
        }

        internal static void Load(string data)
        {
            // v4.268: 缺省容错 —— 老档没有 FIA_Navy 键时 data 为空 -> 直接按"没有舰队"处理, 不抛异常
            // v4.275(按段容错 ★): **先 Reset 再逐段覆盖**。原来只要任何一段抛异常, 整个 catch 就把
            //   造船队列 / 封锁 / 海战战报 / 海运运输队**全部清空** —— 一段坏、全盘丢, 玩家读档后
            //   舰队凭空消失且只有一条日志。现在与海运段的做法一致: **逐段独立 try/catch**,
            //   坏段跳过并 Force 说明"哪一段坏了 / 影响什么", 好段照常加载;
            //   只有整键**完全不可识别**(没有任何已知段前缀)时才 Reset()。
            Reset();
            int known = 0;   // 认出来的段数(版本标记不算)
            int bad = 0;     // 解析抛异常的段数
            try
            {
                if (string.IsNullOrEmpty(data)) return;
                var parts = data.Split(';');
                bool v2 = false;   // v4.272: 订单段是否含新字段
                for (int i = 0; i < parts.Length; i++)
                {
                    string p = parts[i];
                    if (string.IsNullOrEmpty(p)) continue;
                    string seg = p;   // v4.275: 坏段日志要报出"是哪一段", 所以每段先留一份原文
                    try
                    {
                        if (p == SaveVerMarker) { v2 = true; continue; }
                        if (p == LegacyVerMarker) continue;

                        if (HasKnownPrefix(p)) known++;
                        else DLog.Force("海军: 存档段无法识别(已跳过, 可能是更新版本写入的新段): "
                            + Clip(seg) + " —— 影响: 该段承载的功能本次读档不生效, 其余段照常");

                        if (p.StartsWith("upkeep=", StringComparison.Ordinal)) { LastUpkeepPaid = ParseInt(p.Substring(7)); continue; }
                        if (p.StartsWith("bgain=", StringComparison.Ordinal)) { LastBlockadeGain = ParseInt(p.Substring(6)); continue; }
                        if (p.StartsWith("bloss=", StringComparison.Ordinal)) { LastBlockadeLoss = ParseInt(p.Substring(6)); continue; }
                        // v4.272(A11): 封锁附带禁运的归属标记(老档没有 -> false)
                        if (p.StartsWith("bemb=", StringComparison.Ordinal)) { BlockadeEmbargoOwned = ParseInt(p.Substring(5)) != 0; continue; }
                        // v4.290: 出击冷却时间戳。老档没有这个键 -> 保持 Reset 后的 -1(等于"随时可出击"),
                        //   与旧行为一致, 不报错、不算坏段。
                        if (p.StartsWith("srt=", StringComparison.Ordinal)) { NavalTaskForce.SortieReadySecRaw = ParseInt(p.Substring(4)); continue; }

                        if (p.StartsWith("B=", StringComparison.Ordinal)) { ParseSegmentBlockade(p.Substring(2)); continue; }
                        if (p.StartsWith("O=", StringComparison.Ordinal)) { ParseSegmentOrder(p.Substring(2), v2); continue; }
                        if (p.StartsWith("L=", StringComparison.Ordinal)) { ParseSegmentBattle(p.Substring(2)); continue; }
                        // v4.270: 海运运输队(老档没有 T 段 -> 不进来 -> 没有运输队, 不报错)
                        // v4.275: 返回值 = 该条是否真的读进来了(false -> 计一个坏段, 但**不中断**其余段)
                        if (p.StartsWith("T=", StringComparison.Ordinal))
                        {
                            if (!NavalTransport.LoadSegment(p.Substring(2))) { bad++; LogBadSegment(seg); }
                            continue;
                        }
                        // v4.276: 舰队实体(段前缀 F=) —— 老档没有 F 段 -> 不进来 -> 没有舰队, 不报错
                        if (p.StartsWith("F=", StringComparison.Ordinal))
                        {
                            if (!NavalTaskForce.LoadSegment(p.Substring(2))) { bad++; LogBadSegment(seg); }
                            continue;
                        }
                    }
                    catch (Exception exSeg)
                    {
                        // ★ 逐段容错: 只丢这一段, 绝不 Reset 整键
                        bad++;
                        LogBadSegment(seg);
                        DLog.Force("海军: 存档段解析异常(该段已跳过): " + exSeg.Message);
                    }
                }
                if (known == 0 && !AnyKnownSegment(data))
                {
                    // 整键彻底不可识别: 认不出任何已知段 -> 只能按"没有舰队"处理(保留日志便于排查)
                    DLog.Force("海军: FIA_Navy 整键不可识别(没有任何已知段前缀), 按空舰队处理; 原文=" + Clip(data));
                    Reset();
                    return;
                }
                if (bad > 0)
                    DLog.Force("海军: 读档完成(按段容错) —— 已加载 " + known + " 段, 跳过 " + bad + " 个坏段");
            }
            catch (Exception ex)
            {
                // v4.275: 走到这里只剩"循环本身炸了"(极端情况, 比如 Split 内存不足)。
                //   **不再 Reset** —— 已经按段读进来的好段比整键清空更有价值; 数据在 Reset 后本就干净。
                DLog.Force("海军: 存档反序列化失败(按段保留已读入的部分): " + ex.Message);
            }
        }

        // v4.275: 整键判据 —— 串里只要出现**任意**一个已知段前缀, 就认为"这确实是我们的 FIA_Navy"。
        //   刻意用 IndexOf(纯文本扫描, 与解析是否成功无关): 哪怕所有段都解析失败, 只要格式认得出来
        //   就**不清空**已有状态(坏段已经各自打过降级日志), 避免"一段坏 -> 整键 Reset -> 造船/封锁/战报全没"。
        private static bool AnyKnownSegment(string data)
        {
            if (string.IsNullOrEmpty(data)) return false;
            return data.IndexOf("upkeep=", StringComparison.Ordinal) >= 0
                || data.IndexOf("bgain=", StringComparison.Ordinal) >= 0
                || data.IndexOf("bloss=", StringComparison.Ordinal) >= 0
                || data.IndexOf("bemb=", StringComparison.Ordinal) >= 0
                || data.IndexOf("B=", StringComparison.Ordinal) >= 0
                || data.IndexOf("O=", StringComparison.Ordinal) >= 0
                || data.IndexOf("L=", StringComparison.Ordinal) >= 0
                || data.IndexOf("T=", StringComparison.Ordinal) >= 0
                || data.IndexOf("F=", StringComparison.Ordinal) >= 0;
        }

        // v4.275: 段前缀白名单 —— 用来判定"整键是否完全不可识别"(见 Load 末尾)
        private static bool HasKnownPrefix(string p)
        {
            if (string.IsNullOrEmpty(p)) return false;
            return p.StartsWith("upkeep=", StringComparison.Ordinal) || p.StartsWith("bgain=", StringComparison.Ordinal)
                || p.StartsWith("bloss=", StringComparison.Ordinal) || p.StartsWith("bemb=", StringComparison.Ordinal)
                || p.StartsWith("B=", StringComparison.Ordinal) || p.StartsWith("O=", StringComparison.Ordinal)
                || p.StartsWith("L=", StringComparison.Ordinal) || p.StartsWith("T=", StringComparison.Ordinal)
                || p.StartsWith("F=", StringComparison.Ordinal);
        }

        // v4.275: 坏段降级说明 —— 日志里必须写清"这一段坏了会影响什么", 否则玩家/作者只看到"跳过一段"
        private static string SegmentImpact(string seg)
        {
            if (seg == null) return "未知段";
            if (seg.StartsWith("B=", StringComparison.Ordinal)) return "封锁状态丢失(需重新[封锁])";
            if (seg.StartsWith("O=", StringComparison.Ordinal)) return "该条造船订单丢失(船不会完工交付)";
            if (seg.StartsWith("L=", StringComparison.Ordinal)) return "该条海战战报丢失";
            if (seg.StartsWith("T=", StringComparison.Ordinal)) return "该支海运运输队丢失(其船只归属随之丢失)";
            if (seg.StartsWith("F=", StringComparison.Ordinal)) return "该支地图舰队丢失(其船只变成无主 —— 再到[舰队]页重建即可)";
            if (seg.StartsWith("upkeep=", StringComparison.Ordinal)) return "昨日海军维护费统计归 0(仅显示口径)";
            if (seg.StartsWith("bgain=", StringComparison.Ordinal) || seg.StartsWith("bloss=", StringComparison.Ordinal))
                return "昨日封锁战果统计归 0(仅显示口径)";
            if (seg.StartsWith("bemb=", StringComparison.Ordinal)) return "封锁附带的禁运归属标记丢失(解除封锁时不会自动撤销该禁运)";
            return "未知字段段(不影响其它功能)";
        }

        private static void LogBadSegment(string seg)
        {
            DLog.Force("海军: 存档坏段(已跳过, 其余段照常加载) —— 段=["
                + Clip(seg) + "] 影响: " + SegmentImpact(seg));
        }

        // v4.275: 日志里截断超长段(存档段可能几百字符), 只留能定位问题的头部
        private static string Clip(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length > 120 ? s.Substring(0, 120) + "…" : s;
        }

        // v4.275: 封锁段(B=)。字段不足/数字乱 -> 抛异常给 Load 的逐段 catch 记账
        private static void ParseSegmentBlockade(string body)
        {
            var f = body.Split('|');
            if (f.Length < 6) throw new FormatException("封锁段字段数不足(" + f.Length + " < 6)");
            BlockadePortId = Unescape(f[0]);
            BlockadePortName = Unescape(f[1]);
            BlockadeKingdomId = Unescape(f[2]);
            BlockadeKingdomName = Unescape(f[3]);
            BlockadeSinceDay = ParseInt(f[4]);
            BlockadeDamage = ParseInt(f[5]);
        }

        // v4.275: 造船订单段(O=)。v2 = 该段是否含 v4.272 追加的 crew/耐久/船价 三个字段
        //   v4.278(追加 3): 字段 11/12 再追加 DaysDone / WaitingReceiver(老档没有 -> 从 DaysLeft 反推 DaysDone,
        //     保证老档读进来后**进度显示与旧版完全一致**, 不会因为口径切换而跳到 0)。
        private static void ParseSegmentOrder(string body, bool v2)
        {
            var f = body.Split('|');
            if (f.Length < 8) throw new FormatException("造船订单段字段数不足(" + f.Length + " < 8)");
            var o = new Order
            {
                PortId = Unescape(f[0]),
                PortName = Unescape(f[1]),
                HullId = Unescape(f[2]),
                HullName = Unescape(f[3]),
                Paid = ParseInt(f[4]),
                DaysLeft = ParseInt(f[5]),
                DaysTotal = ParseInt(f[6]),
                Tier = ParseInt(f[7])
            };
            // v4.272: 新字段(老档 f.Length < 11 -> 保持 0, 下面回填)
            if (v2 && f.Length >= 11)
            {
                o.Crew = ParseInt(f[8]);
                o.HitPoints = ParseInt(f[9]);
                o.HullValue = ParseInt(f[10]);
            }
            // v4.278(追加 3): 老档(< 13 字段)按"已推进 = 总工期 - 剩余"回填, 数值与旧口径一致
            if (f.Length >= 13) o.DaysDone = ParseInt(f[11]);
            else o.DaysDone = Math.Max(0, o.DaysTotal - o.DaysLeft);
            o.WaitingReceiver = f.Length >= 13 && ParseInt(f[12]) != 0;
            FillHullStats(o);   // 缺哪项补哪项(老档全缺 -> 三项都从船体回填)
            Orders.Add(o);
        }

        // v4.275: 海战战报段(L=)
        private static void ParseSegmentBattle(string body)
        {
            string b = Unescape(body);
            if (!string.IsNullOrEmpty(b)) Battles.Add(b);
            // v4.290: 战报段不带累计计数(不改存档格式), 读档后对齐到已有条数即可 ——
            // 教程基线与判定都取 Max(TotalBattles, Battles.Count), 因此不会因读档而假死。
            if (Battles.Count > TotalBattles) TotalBattles = Battles.Count;
        }

        internal static void Reset()
        {
            try
            {
                Orders.Clear();
                Battles.Clear();
                TotalBattles = 0;
                BlockadePortId = ""; BlockadePortName = "";
                BlockadeKingdomId = ""; BlockadeKingdomName = "";
                BlockadeSinceDay = 0; BlockadeDamage = 0f;
                LastUpkeepPaid = 0; LastBlockadeGain = 0f; LastBlockadeLoss = 0f;
                BlockadeEmbargoOwned = false;   // v4.272(A11)
                PatrolMul = 1f; PatrolParties = 0; PatrolShips = 0; PatrolCrew = 0;   // v4.272(A10)
                NavalTransport.Reset();   // v4.270: 海运运输队一并清空
                NavalTaskForce.Reset();   // v4.276: 地图舰队一并清空
                NavalMapBattle.Reset();   // v4.277(第 2 项): 真实海战去重表(否则上一局的指纹会挡住新局的同型海战)
                // v4.273: 新增的静态统计一并归零 —— 否则"返回主菜单 -> 新开战役"(不重启进程) 会继承上一局的数字
                NavalFishing.LastGain = 0; NavalFishing.LastPorts = 0;   // (B-4) 渔获
                NavalSeaRisk.Reset();                                     // (B-3) DLC 风暴实例缓存
            }
            catch { }
        }

        internal static int ParseInt(string s)
        {
            int v;
            return int.TryParse(s, out v) ? v : 0;
        }

        // 存档转义: ';' '|' '=' 换行 都是分隔符, 统一换成 %XX 形式
        //   v4.270: 改为 internal —— 海运(NavalTransport)的 T= 段复用同一套转义
        internal static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length + 8);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                switch (c)
                {
                    case ';': sb.Append("%3B"); break;
                    case '|': sb.Append("%7C"); break;
                    case '=': sb.Append("%3D"); break;
                    case '%': sb.Append("%25"); break;
                    case '\n': sb.Append("%0A"); break;
                    case '\r': break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }

        internal static string Unescape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            try
            {
                return s.Replace("%0A", "\n").Replace("%3B", ";").Replace("%7C", "|")
                        .Replace("%3D", "=").Replace("%25", "%");
            }
            catch { return s; }
        }
    }
}
