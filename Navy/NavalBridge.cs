using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Naval;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Settlements.Buildings;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace FeudalInternalAffairs
{
    // v4.268: 海战 DLC(NavalDLC / War Sails)接入层。
    //
    // 【软依赖的关键事实】舰船的**战役层数据模型**并不在 NavalDLC.dll 里, 而在基础游戏程序集:
    //   · TaleWorlds.CampaignSystem.Naval.Ship            -> TaleWorlds.CampaignSystem.dll
    //   · TaleWorlds.Core.ShipHull / ShipSlot / ShipUpgradePiece -> TaleWorlds.Core.dll
    //   · Settlement.HasPort / PortPosition / Town.AvailableShips / MobileParty.Ships -> 同上
    //   · GameModels.ShipCostModel / ShipStatModel / PartyShipLimitModel ... -> 同上
    // 所以本文件可以**编译期直接使用**这些类型, **一个 NavalDLC.* 类型都不引用**,
    // 于是 DLC 缺失时既不会 TypeLoadException, 也不需要 SubModule.xml 硬依赖、不需要 <Reference> NavalDLC.dll。
    //
    // DLC 缺失时的自然降级链路(实测数据): 港口坐标写在 **DLC 的** ModuleData\settlements.xml 的
    //   port_posX / port_posY 上(该文件 101 处); 原版 SandBox\ModuleData\settlements.xml 里一个都没有。
    //   -> 没装 DLC 时 Settlement.HasPort 恒为 false、Culture.AvailableShipHulls 为空、Town.AvailableShips 为空,
    //      本层所有查询自然返回"没有舰队 / 没有港口", 面板显示"未检测到海战 DLC"。
    internal static class NavalBridge
    {
        // ==================== DLC 探测 ====================

        private static int _dlcState;   // 0=未探测 1=有 2=无
        private static bool _dlcLoaded;

        // DLC 模块是否装载(只探测 NavalDLC.dll 里的类型是否存在, 不引用其类型)
        internal static bool DlcLoaded
        {
            get
            {
                try
                {
                    if (_dlcState == 0)
                    {
                        _dlcState = 2;
                        // NavalDLCManager 是 DLC 的 GameHandler, 存在于 NavalDLC.dll
                        var t = Type.GetType("NavalDLC.NavalDLCManager, NavalDLC", false);
                        if (t == null)
                        {
                            try
                            {
                                var asms = AppDomain.CurrentDomain.GetAssemblies();
                                for (int i = 0; i < asms.Length; i++)
                                {
                                    var a = asms[i];
                                    if (a == null) continue;
                                    if (a.GetName().Name == "NavalDLC") { t = a.GetType("NavalDLC.NavalDLCManager", false); break; }
                                }
                            }
                            catch { }
                        }
                        if (t != null) { _dlcState = 1; _dlcLoaded = true; }
                    }
                }
                catch { _dlcState = 2; }
                return _dlcLoaded;
            }
        }

        // 海军功能是否可用 = DLC 在 + 地图上真有港口(双保险: 老档/自定义地图没有 port_posX 时不显示海军内容)
        //   v4.268: 只对"可用"做正缓存 —— "不可用"每次重算。原因: 模块 OnSubModuleLoad 阶段 Settlement.All
        //   还没填数据, 若那时把结果缓存成 false, 整局游戏海军都会是灰的。
        private static int _usable = -1;
        internal static bool Usable
        {
            get
            {
                try
                {
                    if (_usable == 1) return true;
                    if (!DlcLoaded) { _usable = 0; return false; }
                    bool ok = AllPorts().Count > 0;
                    _usable = ok ? 1 : -1;
                    return ok;
                }
                catch { return false; }
            }
        }

        internal static void InvalidateCache() { _usable = -1; }

        // 面板顶部一行状态文案
        internal static string StatusText()
        {
            try
            {
                if (!DlcLoaded) return "未检测到海战 DLC(NavalDLC) —— 海军功能已整体停用, 不影响其它系统";
                if (AllPorts().Count == 0) return "已装海战 DLC, 但当前地图没有任何港口(settlements.xml 无 port_posX) —— 海军功能停用";
                return "海战 DLC 已接入 · 港口 " + AllPorts().Count + " 处";
            }
            catch { return "海军状态未知"; }
        }

        // ==================== 港口 ====================

        // 全部沿海聚落(城镇; 港口坐标由 DLC settlements.xml 的 port_posX 决定)
        internal static List<Settlement> AllPorts()
        {
            var list = new List<Settlement>();
            try
            {
                foreach (var s in Settlement.All)
                {
                    if (s == null) continue;
                    if (!s.HasPort) continue;
                    if (!s.IsTown) continue;      // 港口菜单只挂在城镇上(DLC 的造船/修船都要求 IsTown)
                    list.Add(s);
                }
            }
            catch { }
            return list;
        }

        // 某王国的沿海城镇
        internal static List<Settlement> PortsOf(Kingdom k)
        {
            var list = new List<Settlement>();
            try
            {
                if (k == null) return list;
                foreach (var s in Settlement.All)
                {
                    if (s == null || !s.HasPort || !s.IsTown) continue;
                    if (s.OwnerClan != null && s.OwnerClan.Kingdom == k) list.Add(s);
                }
            }
            catch { }
            return list;
        }

        // 敌对王国的沿海城镇(封锁候选): 与我方交战中
        internal static List<Settlement> EnemyPorts(Kingdom mine)
        {
            var list = new List<Settlement>();
            try
            {
                if (mine == null) return list;
                foreach (var s in Settlement.All)
                {
                    if (s == null || !s.HasPort || !s.IsTown) continue;
                    var owner = s.OwnerClan != null ? s.OwnerClan.Kingdom : null;
                    if (owner == null || owner == mine) continue;
                    if (mine.IsAtWarWith(owner)) list.Add(s);
                }
            }
            catch { }
            return list;
        }

        // 港口名(容错)
        internal static string NameOf(Settlement s)
        {
            try { return (s != null && s.Name != null) ? s.Name.ToString() : "?"; }
            catch { return "?"; }
        }

        // 港口所属王国
        internal static Kingdom KingdomOf(Settlement s)
        {
            try { return (s != null && s.OwnerClan != null) ? s.OwnerClan.Kingdom : null; }
            catch { return null; }
        }

        // ==================== 船坞建筑 ====================
        // DLC 用扩展方法 Town.GetShipyard() 找 building_shipyard(NavalDLCExtensions.cs:41),
        // 那是 NavalDLC 自己的扩展方法, 我们不能调 -> 这里按同一个 BuildingType 的 StringId 自己找。

        private const string ShipyardBuildingId = "building_shipyard";

        internal static Building ShipyardOf(Town t)
        {
            try
            {
                if (t == null) return null;
                var bs = t.Buildings;
                if (bs == null) return null;
                for (int i = 0; i < bs.Count; i++)
                {
                    var b = bs[i];
                    if (b == null || b.BuildingType == null) continue;
                    if (b.BuildingType.StringId == ShipyardBuildingId) return b;
                }
            }
            catch { }
            return null;
        }

        // 船坞等级 0-3。0 = 不能造船; 1 = 轻型; 2 = 中型; 3 = 重型
        // (对应 ShipProductionCampaignBehavior.CanTownCreateShipFromHull 的门槛)
        internal static int ShipyardLevel(Town t)
        {
            try { var b = ShipyardOf(t); return b != null ? b.CurrentLevel : 0; }
            catch { return 0; }
        }

        internal static int ShipyardLevel(Settlement s)
        {
            try { return (s != null && s.IsTown) ? ShipyardLevel(s.Town) : 0; }
            catch { return 0; }
        }

        // ==================== v4.273(A-2): 船坞建筑的"舰队容量"效果 ====================
        //   反编译实证(DLC)：`BuildingEffectEnum.ShipProduction(28)` 的等级值 = 1/2/3,
        //   即"这座港口最多同时养活几条船"的**建筑侧**容量(DLC 自己用
        //   ShipProductionCampaignBehavior.GetMaxShipCountForTown 消费 29=MaximumShipCount=9/12/15 那条,
        //   那是"该港在售船数上限"; 28 才是等级化的产出/容量效果)。
        //   我们这边的舰队上限(FleetCap)原来只算 `4 + 科技 + 港口数`, **完全没算船坞等级** ——
        //   于是"升船坞"只影响造船档位, 对"能养多少船"毫无帮助。这里接上, 让建筑投资有完整回报。
        //
        //   实现方式: 走基础游戏的 `Building.AddEffectOfBuilding(enum, ref ExplainedNumber)` 拿真实数值
        //   (不写死 1/2/3 —— 原版/其它 mod 若改了这栋建筑的效果数值, 我们自动跟随)。
        internal static int ShipyardShipCapacity(Building shipyard)
        {
            try
            {
                if (shipyard == null) return 0;
                var en = new ExplainedNumber(0f, false, null);
                shipyard.AddEffectOfBuilding(BuildingEffectEnum.ShipProduction, ref en);
                int v = (int)Math.Round(en.ResultNumber);
                return v > 0 ? v : 0;
            }
            catch { return 0; }
        }

        // 我方所有港口的船坞容量之和(舰队上限的建筑侧加成)
        internal static int ShipyardShipCapacity(Kingdom k)
        {
            try
            {
                int n = 0;
                var ports = PortsOf(k);
                for (int i = 0; i < ports.Count; i++)
                {
                    var b = ShipyardOf(ports[i] != null ? ports[i].Town : null);
                    n += ShipyardShipCapacity(b);
                }
                return n;
            }
            catch { return 0; }
        }

        // ==================== v4.273(A-3): 修船(只读 DLC 的维修费口径, 不重复扣钱) ====================
        //   反编译实证(DLC)：`ShipRepairCampaignBehavior` 已经在港口自动修船
        //   (DailyTickPartyEvent + AfterSettlementEntered), 费用走 `ShipCostModel.GetShipRepairCost`。
        //   ⇒ 我们**绝不能**再扣一次钱(那就是重复收费)。这里只做"读维修费 + 告诉玩家", 让舰队页能显示
        //   "需维修 N 艘 · 约 X 第纳尔"(原来面板只有一句按耐久差估的"修好需约 N 第纳尔", 口径与 DLC 不一致)。
        internal static float RepairCost(Ship s, PartyBase payer)
        {
            try
            {
                if (s == null) return 0f;
                var m = Campaign.Current != null ? Campaign.Current.Models.ShipCostModel : null;
                if (m == null) return 0f;
                float v = m.GetShipRepairCost(s, payer);
                if (float.IsNaN(v) || float.IsInfinity(v) || v < 0f) return 0f;
                return v;
            }
            catch { return 0f; }
        }

        // 我方舰队里"需要维修"(完整度 < 99%)的船数与总维修费
        internal static void RepairSummary(out int needCount, out float needCost)
        {
            needCount = 0; needCost = 0f;
            try
            {
                var ships = MyShips();
                PartyBase payer = null;
                try { payer = MobileParty.MainParty != null ? MobileParty.MainParty.Party : null; } catch { }
                for (int i = 0; i < ships.Count; i++)
                {
                    var s = ships[i];
                    if (s == null) continue;
                    if (HealthRatio(s) >= 0.99f) continue;
                    needCount++;
                    needCost += RepairCost(s, payer);
                }
            }
            catch { }
        }

        // ==================== 舰船查询 ====================

        private static void Collect(PartyBase p, List<Ship> into)
        {
            try
            {
                if (p == null || into == null) return;
                var ships = p.Ships;
                if (ships == null) return;
                for (int i = 0; i < ships.Count; i++) if (ships[i] != null) into.Add(ships[i]);
            }
            catch { }
        }

        internal static List<Ship> ShipsOf(PartyBase p)
        {
            var list = new List<Ship>();
            Collect(p, list);
            return list;
        }

        internal static List<Ship> ShipsOf(MobileParty p)
        {
            var list = new List<Ship>();
            try { if (p != null) Collect(p.Party, list); } catch { }
            return list;
        }

        // 氏族舰队 = 各 WarParty 名下的船。
        //   v4.268 修正: **不能**把氏族的封地(Town)Party 算进来 —— 那是 DLC 造船厂的"待售库存"
        //   (Town.AvailableShips 就是 Settlement.Party.Ships), 算进来会让玩家"白得"全国港口库存的船。
        //   港口库存要用 AvailableShips(Town) 单独查。
        internal static List<Ship> ShipsOf(Clan c)
        {
            var list = new List<Ship>();
            try
            {
                if (c == null) return list;
                var wpc = c.WarPartyComponents;
                if (wpc != null)
                {
                    for (int i = 0; i < wpc.Count; i++)
                    {
                        var w = wpc[i];
                        if (w == null || w.MobileParty == null) continue;
                        Collect(w.MobileParty.Party, list);
                    }
                }
            }
            catch { }
            return list;
        }

        // 王国 = 旗下所有氏族(Clan.Kingdom == k)的船
        internal static List<Ship> ShipsOf(Kingdom k)
        {
            var list = new List<Ship>();
            try
            {
                if (k == null) return list;
                var clans = k.Clans;
                if (clans == null) return list;
                for (int i = 0; i < clans.Count; i++)
                {
                    var c = clans[i];
                    if (c == null) continue;
                    list.AddRange(ShipsOf(c));
                }
            }
            catch { }
            return list;
        }

        // ==================== v4.272(审计 A14): "我方舰队"的**明确口径** ====================
        //   问题: NavalFleet 原来用 ShipsOf(Kingdom) 当"我方舰队", 那是**全国所有氏族战帮的船** ——
        //     于是 ①舰队上限被 AI 领主的船占满(买船直接报"已达上限")
        //          ②维护费替 AI 领主付(NavalFleet.DailyUpkeep 对它们求和)
        //          ③海战击沉判定把自家领主的船当"我方损失"沉掉(NavalBattle.SinkShips(myShips,...))。
        //   口径(与 Military\Soldiers.cs 的 IsPlayerSideParty 对齐, 但**不收**玩家王国的 AI 领主):
        //     · 玩家主队
        //     · 玩家氏族的所有战帮(家族成员的部队 —— 它们是玩家自己的战力)
        //     · 国防军 fia_def_*(mod 自建的军团部队)
        //   敌国的舰队仍然用 ShipsOf(Kingdom)(NavalBattle 里对 enemy 就是这么调的), 语义不变。
        //   v4.276(舰队实体化): **地图舰队(fia_navy_*)也在这个口径里** —— 舰队的 PartyComponent 是
        //   LordPartyComponent 且 owner = 玩家氏族的司令, 所以它天然出现在 Clan.PlayerClan.WarPartyComponents
        //   里, 名下的船会被这里收到。这是**有意为之**: 舰队是玩家自己的海上力量, 维护费/上限/海战
        //   本就该算它。要区分"在某支舰队手里"的船请用 NavalTaskForce.ShipBlockedByFleet / ShipInFleet。
        internal static List<Ship> MyShips()
        {
            // ==================== v4.279(第 5 项): "同一调用批"缓存 ====================
            //   【为什么需要】本函数是 O(部队数 × 军团数) + 双堆分配(List + HashSet)。原来
            //   NavyPanelVM.BuildBlockadeRows 把它放在**敌港循环里**(每港 2 次) —— 20 敌港 + 300 部队时
            //   每次刷新约 12 万次内层比较 + 84 次分配, 每 3 秒一次。循环外提取(本文件调用点已改)解决了
            //   那一处, 但 NavalBattle/NavalFleet/小窗还会各自调它, 所以这里再加一层批内复用。
            //   【失效判据为什么选"实时 10ms 窗口"而不是"同一游戏日"】见下面 CacheHitWindowMs 的注释。
            try
            {
                if (_myShipsCache != null)
                {
                    int age = unchecked(Environment.TickCount - _myShipsStamp);
                    if (age >= 0 && age < CacheHitWindowMs) return new List<Ship>(_myShipsCache);
                }
            }
            catch { }
            var list = new List<Ship>();
            var seen = new HashSet<int>();
            try
            {
                CollectParty(MobileParty.MainParty, list, seen);

                var pc = Clan.PlayerClan;
                if (pc != null)
                {
                    var wpc = pc.WarPartyComponents;
                    if (wpc != null)
                    {
                        for (int i = 0; i < wpc.Count; i++)
                        {
                            var w = wpc[i];
                            if (w == null || w.MobileParty == null) continue;
                            CollectParty(w.MobileParty, list, seen);
                        }
                    }
                }

                // 国防军军团(可能在玩家氏族之外, 也可能被临时抽调进军团)
                //   v4.273(审计 #5): 补上**部队管理"分裂"出的新队前缀 fia_amg_**。                //   那些部队是 UI\ArmyManageVM 用 MobileParty.CreateParty(pid, null) 建的 —— PartyComponent
                //   为 null ⇒ 既没有 WarPartyComponent(不在 Clan.PlayerClan.WarPartyComponents 里),
                //   也不在 DefArmy 的军团登记表(IsDefArmyParty 只认 fia_def_ 前缀 + LegionOf)
                //   ⇒ 它们的船**不算我方舰队**: 维护费漏算、舰队上限漏算、海战不参与。
                //   口径与 Military\Soldiers.cs:69 的 IsPlayerSideParty(按 fia_ 前缀兜底)对齐。
                //   用 "fia_def_" 与 "fia_amg_" 两个**精确前缀**而不是笼统的 "fia_"(将来若新增其它
                //   fia_ 前缀部队, 不至于被静默算进舰队口径 —— 那时应显式加进来)。
                var all = MobileParty.All;
                if (all != null)
                {
                    for (int i = 0; i < all.Count; i++)
                    {
                        var p = all[i];
                        if (p == null || !p.IsActive) continue;
                        // v4.279(第 5 项): **前缀快路径 + 无船快路径** —— 本函数只收船, 一支部队名下
                        //   一条船都没有时, 无论它是不是国防军, 对它调用 IsDefArmyParty(内部 O(军团数)
                        //   遍历登记表)都**必然一无所获**。所以先做两次 O(1) 判断:
                        //     ① 没船 -> 直接跳过(绝大多数部队在这里就被滤掉, 内层比较次数降一个数量级);
                        //     ② 有船 -> 只有 fia_def_/fia_amg_ 前缀才可能是我方编制,
                        //        其余仍走 IsDefArmyParty 兜底(不假设它只认前缀: 它还有 LegionOf 登记表分支)。
                        bool hasShips = false;
                        try { hasShips = p.Party != null && p.Party.Ships != null && p.Party.Ships.Count > 0; }
                        catch { }
                        if (!hasShips) continue;
                        bool mineSquad = false;
                        try
                        {
                            string sid = p.StringId;
                            mineSquad = sid != null &&
                                (sid.StartsWith("fia_def_", StringComparison.Ordinal) ||
                                 sid.StartsWith("fia_amg_", StringComparison.Ordinal));
                        }
                        catch { }
                        if (!mineSquad)
                        {
                            try { mineSquad = DefArmy.IsDefArmyParty(p); } catch { }
                        }
                        if (!mineSquad) continue;
                        CollectParty(p, list, seen);
                    }
                }
            }
            catch { }
            // 落缓存: 存**独立副本**, 对外一律返回新 List —— 调用方可以自由 Sort/Add 而不污染缓存。
            try { _myShipsCache = new List<Ship>(list); _myShipsStamp = Environment.TickCount; } catch { }
            return list;
        }

        // ==================== v4.279(第 5 项): MyShips 批内缓存 ====================
        //   【失效判据为什么是"实时 10ms 窗口"而不是"同一游戏日"】
        //   · 游戏日(或游戏小时)判据在**地图暂停时永不失效**: 玩家打开海军页时地图通常是暂停的,
        //     CampaignTime.Now 不动, 于是"买了一条船"之后面板仍旧显示旧列表 —— 玩家会以为没买到。
        //   · 反过来, 缓存要解决的问题**只发生在同一个同步调用栈内**(一次面板刷新的敌港循环里
        //     连续 40 次调用)。跨帧/跨点击之间必须重算。
        //   => 取一个**远小于一帧**的实时窗口(10ms): 同一循环里的连续调用必然命中(单次 <1ms),
        //      而两次 UI 刷新(3 秒节流)或一次点击(买船/组建舰队)之间**必然过期**而重算。
        //      10ms 也覆盖不了"同一帧内的两次独立刷新", 但那正是我们希望重算的情形。
        private const int CacheHitWindowMs = 10;
        private static List<Ship> _myShipsCache;
        private static int _myShipsStamp;

        // 显式失效(改船之后立刻要看到新结果的场合调用; 例如面板买船/组建舰队后的刷新)
        internal static void InvalidateMyShips() { try { _myShipsCache = null; } catch { } }

        // 收一支我方部队的船(按 Ship.RandomValue 去重: 同一支部队可能被上面两条路径重复命中)
        private static void CollectParty(MobileParty p, List<Ship> into, HashSet<int> seen)
        {
            try
            {
                if (p == null || p.Party == null) return;
                var ships = p.Party.Ships;
                if (ships == null) return;
                for (int i = 0; i < ships.Count; i++)
                {
                    var s = ships[i];
                    if (s == null) continue;
                    int key = 0;
                    try { key = s.RandomValue; } catch { }
                    if (key != 0 && !seen.Add(key)) continue;
                    into.Add(s);
                }
            }
            catch { }
        }

        // 港口船坞当前在售的船(Town.AvailableShips 就是 Settlement.Party.Ships)
        internal static List<Ship> AvailableShips(Town t)
        {
            var list = new List<Ship>();
            try
            {
                if (t == null) return list;
                var ships = t.AvailableShips;
                if (ships == null) return list;
                for (int i = 0; i < ships.Count; i++) if (ships[i] != null) list.Add(ships[i]);
            }
            catch { }
            return list;
        }

        // ==================== 舰船属性 ====================
        // 全部走基础游戏的 Ship 属性, DLC 的 perks/政策加成已在属性里体现(Ship.MaxHitPoints 等会累加升级件)

        internal static string HullName(Ship s)
        {
            try { return (s != null && s.ShipHull != null && s.ShipHull.Name != null) ? s.ShipHull.Name.ToString() : "?"; }
            catch { return "?"; }
        }

        internal static string HullId(Ship s)
        {
            try { return (s != null && s.ShipHull != null) ? s.ShipHull.StringId : ""; }
            catch { return ""; }
        }

        // 舰种: 0=轻型 1=中型 2=重型
        internal static int HullType(Ship s)
        {
            try { return s != null && s.ShipHull != null ? (int)s.ShipHull.Type : 0; }
            catch { return 0; }
        }

        internal static string HullTypeName(int t)
        {
            switch (t)
            {
                case 2: return "重型";
                case 1: return "中型";
                default: return "轻型";
            }
        }

        internal static float HitPoints(Ship s) { try { return s != null ? s.HitPoints : 0f; } catch { return 0f; } }
        internal static float MaxHitPoints(Ship s) { try { return s != null ? s.MaxHitPoints : 0f; } catch { return 0f; } }

        // ==================== v4.279(第 6 项): "载员"统一到 DLC 的权威口径 TotalCrewCapacity ====================
        //   【反编译实证】(本机 TaleWorlds.CampaignSystem.dll -> TaleWorlds.CampaignSystem.Naval.Ship)
        //     Ship.TotalCrewCapacity    = ShipHull.TotalCrewCapacity    × (1 + Σ升级件 CrewCapacityBonusMultiplier)
        //     Ship.MainDeckCrewCapacity = ShipHull.MainDeckCrewCapacity × (1 + Σ升级件 CrewCapacityBonusMultiplier)
        //     Ship.SkeletalCrewCapacity = ShipHull.SkeletalCrewCapacity  (骨架船员, 不累加升级件)
        //   两者的关系由 DLC 自己的代码写死(NavalDLC.dll 反编译):
        //     `CrewSizeOnLowerDeck = ShipOrigin.TotalCrewCapacity - CrewSizeOnMainDeck`
        //     => **Total = 主甲板 + 下层甲板**, MainDeck 只是"上层战斗位"。
        //   而 DLC 的**战斗部署 / 运力上限 / 超载判定全部用 Total**(逐条来自 NavalDLC 反编译):
        //     · NavalShipAgents.DesiredTroopCount = Ship.TotalCrewCapacity        (一条船派多少兵上场)
        //     · NavalAgentsLogic: SetDesiredTroopCount(ship.TotalCrewCapacity)    (编队分配兵员)
        //     · GetMaximumTroopCountForShipList: Σ ship.TotalCrewCapacity         (海战可投入兵力上限)
        //     · party.NumberOfAllMembers > Σ TotalCrewCapacity -> 超载惩罚        (载员上限)
        //     · maximumCrewLimit += ship.TotalCrewCapacity                        (船队人数上限)
        //   原实现取 MainDeckCrewCapacity => 面板"载员"与海运运力判断**系统性低估**(把下层甲板的人丢了),
        //   而且与本项目自己的 NavalFleet.Offers(:329/:354/:772 用 `h.TotalCrewCapacity`)自相矛盾 ——
        //   同一时刻造船页显示总载员、舰队页显示主甲板。所以这里统一到 Total。
        //   ★ 战力分 ShipPowerScore 仍取 MainDeckCrew("舷侧火力"= 主甲板战斗位), 于是**战力数值改动前后不变**,
        //     本次修正只影响"载员/运力"这类口径, 不顺手改海战平衡。
        internal static int Crew(Ship s) { try { return s != null ? s.TotalCrewCapacity : 0; } catch { return 0; } }

        // 主甲板战斗位(仅用于战力分与"上层/下层"拆解说明, 不要拿它当运力)
        internal static int MainDeckCrew(Ship s) { try { return s != null ? s.MainDeckCrewCapacity : 0; } catch { return 0; } }

        // 骨架船员(操船最低人数)
        internal static int SkeletalCrew(Ship s) { try { return s != null ? s.SkeletalCrewCapacity : 0; } catch { return 0; } }
        internal static float Cargo(Ship s) { try { return s != null ? s.InventoryCapacity : 0f; } catch { return 0f; } }
        internal static float Speed(Ship s) { try { return s != null && s.ShipHull != null ? s.ShipHull.BaseSpeed : 0f; } catch { return 0f; } }
        internal static int SeaWorthiness(Ship s) { try { return s != null ? s.SeaWorthiness : 0; } catch { return 0; } }
        internal static int HullValue(Ship s) { try { return s != null && s.ShipHull != null ? s.ShipHull.Value : 0; } catch { return 0; } }

        // 完整度 0..1
        internal static float HealthRatio(Ship s)
        {
            try
            {
                float m = MaxHitPoints(s);
                if (m <= 0f) return 0f;
                return MBMath.ClampFloat(HitPoints(s) / m, 0f, 1f);
            }
            catch { return 0f; }
        }

        // 单舰战力分: 舷侧火力(船员) × 舰炮伤害(船体价/1000) × 装甲(耐久/1000) × 舰种系数
        // 与 BattleSim.FleetPowerScoreOf(guns, damage, armor, admiral) 同口径(那边是场景战的旧口径)
        internal static float ShipPowerScore(Ship s)
        {
            try
            {
                if (s == null) return 0f;
                // v4.279(第 6 项): 舷侧火力取**主甲板战斗位** —— 与 v4.279 之前 Crew() 的取值完全相同,
                //   所以"载员口径修正"不会顺手改动战力分/海战结算的数值。改用 NavalBridge.Crew(总载员)
                //   会让战力整体变大(下层甲板的人也算进火力), 那是**平衡改动**, 不属本次修复范围。
                float guns = MainDeckCrew(s);
                float dmg = HullValue(s) / 1000f;
                float armor = MaxHitPoints(s) / 1000f;
                float typeK = 1f + HullType(s) * 0.25f;
                return guns * dmg * (1f + armor / 100f) * typeK * HealthRatio(s);
            }
            catch { return 0f; }
        }

        // 单舰日维护费(第纳尔/日): 船体价 / 1200 × (0.6 + 完整度), 轻型船约 5/日, 重型约 60/日
        internal static float ShipUpkeep(Ship s)
        {
            try
            {
                if (s == null) return 0f;
                float v = HullValue(s);
                if (v <= 0f) return 0f;
                return v / 1200f * (0.6f + HealthRatio(s));
            }
            catch { return 0f; }
        }

        // 购船基价(与 DLC NavalDLCShipCostModel.GetShipTradeValue 的 1.5 倍买价同口径;
        // 我们自己在面板里算"我方实付价", 不去改 DLC 的模型)
        internal static float ShipBuyPrice(Ship s)
        {
            try
            {
                if (s == null) return 0f;
                float v = HullValue(s);
                return v * 1.5f;
            }
            catch { return 0f; }
        }

        // ==================== 图标(图集 ModuleData\FeudalIcons.bin, 已在 bin 内) ====================
        // v4.268: 海军图标 24 张(fia_ship_*) + 港口/船坞(fia_bld_port / fia_bld_shipyard / fia_bld_naval_port)
        //   按船体 id 映射; 未知船体按舰种档位兜底。

        internal static string IconOf(Ship s)
        {
            try { return s != null ? IconOfHullId(HullId(s), HullType(s)) : IconShipFallback(0); }
            catch { return IconShipFallback(0); }
        }

        internal static string IconOfHullId(string hullId, int tier)
        {
            try
            {
                switch (hullId)
                {
                    // 商用/运输
                    case "northern_trade_ship":
                    case "eastern_trade_ship":
                    case "western_trade_ship": return "fia_ship_merchantman";
                    case "empire_trade_ship": return "fia_ship_transport";
                    // 渔船
                    case "fishing_ship":
                    case "southern_fishing_ship": return "fia_ship_boat";
                    // 重型战列线
                    case "vlandia_heavy_ship":
                    case "aserai_heavy_ship": return "fia_ship_first_rate";
                    case "sturgia_heavy_ship":
                    case "eastern_heavy_ship":
                    case "empire_heavy_ship": return "fia_ship_ship_of_line";
                    case "khuzait_heavy_ship": return "fia_ship_armored_cruiser";
                    // 中型
                    case "western_medium_ship": return "fia_ship_frigate";
                    case "nord_mediumballista_ship": return "fia_ship_gunboat";
                    case "empire_medium_ship":
                    case "eastern_medium_ship":
                    case "battanian_medium_ship":
                    case "northern_medium_ship":
                    case "nord_medium_ship": return "fia_ship_corvette";
                    // 轻型
                    case "northern_light_ship":
                    case "western_light_ship":
                    case "central_light_ship": return "fia_ship_cutter";
                    case "battanian_light_ship": return "fia_ship_boat";
                    default: return IconShipFallback(tier);
                }
            }
            catch { return IconShipFallback(tier); }
        }

        private static string IconShipFallback(int tier)
        {
            switch (tier)
            {
                case 2: return "fia_ship_ship_of_line";
                case 1: return "fia_ship_corvette";
                default: return "fia_ship_cutter";
            }
        }

        internal const string IconPort = "fia_bld_port";
        internal const string IconNavalPort = "fia_bld_naval_port";
        internal const string IconShipyard = "fia_bld_shipyard";
        internal const string IconBlockade = "fia_ship_fireship";
    }
}
