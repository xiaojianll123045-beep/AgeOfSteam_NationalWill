using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Naval;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;

namespace FeudalInternalAffairs
{
    // v4.268: 海军层的战役行为 —— 唯一挂到游戏的入口。
    //   · 日结: 收海军维护费(进 Fiscal 支出) / 推进造船队列 / 结算封锁惩罚与战果
    //   · 存档: 键 FIA_Navy(老档没有该键 -> NavalFleet.Load("") -> 按"没有舰队"处理, 不报错)
    //   · 软依赖: 没装海战 DLC 时 NavalBridge.Usable 为 false, 整个日结直接 return(零副作用)
    internal class NavalBehavior : CampaignBehaviorBase
    {
        private string _navy = "";
        private static int _lastPatrolDay = -1;
        // v4.278(追加 4): 日结**防重**用(同一天被触发两次 -> 整组结算只跑一次)
        private static int _lastNavyDay = -1;

        public override void RegisterEvents()
        {
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
            // v4.270: 海运要"到了就下船", 日结粒度太粗(军团会在海上多漂一整天) -> 加小时结推进运输队
            try { CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, OnHourlyTick); } catch { }
            // v4.277(第 2 项): 原版地图事件结束回调 -> 把**地图上真实打的海战**补进海军战报
            //   (订阅/判定/格式化全在 Navy\NavalMapBattle.cs, 那里有 file:line 级的反编译依据)
            try { NavalMapBattle.Register(this); } catch (Exception exB) { DLog.Force("海军: 真实海战订阅异常 " + exB.Message); }
            // v4.272(审计 A5): 补 OnNewGameCreatedEvent —— 原实现只有日结/小时结两个监听,
            //   于是"返回主菜单 -> 不重启进程 -> 新开战役"时, 上一局的**静态状态**会被继承:
            //   NavalFleet.Orders(造船队列) / Battles(海战战报) / Blockade*(封锁目标) /
            //   LastUpkeepPaid / LastBlockadeGain/Loss / NavalTransport.Active(海运运输队)。
            //   对照 DefArmyBehavior.cs:24 早就有 OnNewGameCreatedEvent -> DefArmy.Reset()。
            try { CampaignEvents.OnNewGameCreatedEvent.AddNonSerializedListener(this, OnNewGameCreated); } catch { }
            // v4.273(C 级): 沉船 -> 从海运账目里划掉(否则"还船数"会永远对不上真实船数)
            //   签名: OnShipDestroyedEvent(PartyBase 前主人, Ship 船, ShipDestroyDetail 原因)
            try
            {
                CampaignEvents.OnShipDestroyedEvent.AddNonSerializedListener(this,
                    (PartyBase owner, Ship ship, DestroyShipAction.ShipDestroyDetail detail)
                        =>
                    {
                        NavalTransport.OnShipDestroyed(owner, ship, detail);
                        // v4.276: 沉的是舰队名下的船 -> 也要从舰队的还船账目里划掉
                        NavalTaskForce.OnShipDestroyed(owner, ship, detail);
                    });
            }
            catch (Exception ex) { DLog.Force("海军: 沉船事件订阅失败(只影响还船账目显示) " + ex.Message); }
            // v4.273(A-1): 装/卸保养后维修进度会变, 顺手让舰队页下次刷新拿到新值(纯只读, 不改任何状态)
            try
            {
                CampaignEvents.OnShipRepairedEvent.AddNonSerializedListener(this,
                    (Ship ship, Settlement st) => { try { NavalSeaRisk.Reset(); } catch { } });
            }
            catch { }
        }

        public override void SyncData(IDataStore dataStore)
        {
            try
            {
                if (dataStore.IsSaving) _navy = NavalFleet.Save();
                dataStore.SyncData("FIA_Navy", ref _navy);   // v4.268: 新增存档键(缺省容错见 NavalFleet.Load)
                if (dataStore.IsLoading) NavalFleet.Load(_navy);
            }
            catch (Exception ex) { DLog.Force("海军: 存档同步失败 " + ex.Message); }
        }

        // v4.272(审计 A5): 新战役创建 -> 把海军全部静态状态归零
        //   NavalFleet.Reset() 内部已经级联清空造船队列/封锁/战报/统计, 并调 NavalTransport.Reset()
        //   清空海运运输队(军团还没建, 留着运输队只会在第一次 Tick 时报"军团已不存在")。
        private void OnNewGameCreated(CampaignGameStarter starter)
        {
            try
            {
                NavalFleet.Reset();
                NavalPatrol.Reset();
                NavalSeaRisk.Reset();            // v4.273(B-3): 丢掉上一局缓存的 DLC 风暴实例
                NavalMapBattle.Reset();          // v4.277(第 2 项): 真实海战去重表也要清(否则新局前几场可能被误判重复)
                NavalBridge.InvalidateCache();   // 新地图的港口要重新探测(旧缓存可能来自上一局的战役地图)
                _lastPatrolDay = -1;
                _lastNavyDay = -1;   // v4.278(追加 4): 新战役必须复位, 否则"上一局的第 N 天"会挡掉本局同一天的日结
                DLog.Force("海军: 新战役, 海军静态状态已清空(舰队/订单/封锁/战报/海运)");
            }
            catch (Exception ex) { DLog.Force("海军: 新战役清理失败 " + ex.Message); }
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

        private void OnDailyTick()
        {
            try
            {
                if (!NavalBridge.Usable) return;
                var k = PlayerKingdom();
                if (k == null) return;
                // v4.278(追加 4): **同日防重** —— 本 behavior 的其它日结分支都有 _lastDay 判等
                //   (PatrolDaily:240 就是), 唯独这里是裸的。正常流程下 DailyTickEvent 一天只发一次,
                //   但"同一天内存读档 / 其它 mod 手动触发日结"会让整组结算**重复跑一遍**:
                //   水手军饷/船维护/封锁收益/造船进度全都会重复推进(造船进度一次扣 2 天最直观)。
                //   这里与其它五个 behavior(EconomyBehavior:451 / DefArmyBehavior:98 / DefArmy:2084 /
                //   DiplomacyBehavior:210)统一口径; 新战役/读档时由 Reset 侧把 -1 复位。
                if (Politics.Today() == _lastNavyDay) return;
                _lastNavyDay = Politics.Today();

                UpkeepDaily(k);
                NavalFleet.DailyShipbuilding(k);
                BlockadeDaily(k);
                PatrolDaily(k);
                // v4.273(B-4): 沿海渔获 -> 国库(计入税收科目); 只读统计我方港口, 不改任何原版渔业逻辑
                try { NavalFishing.Daily(k); } catch (Exception exF) { DLog.Force("海军: 渔获异常 " + exF.Message); }
                // v4.270: 海运兜底推进(小时间隔万一被跳过, 日结至少跑一次, 不让军团漂在海上跨天)
                NavalTransport.Tick();
                // v4.276: 舰队存续校验(部队被歼灭/船被击沉/母港易主) + "AI 不接管"标志自愈
                try { NavalTaskForce.Tick(); } catch (Exception exF) { DLog.Force("海军: 舰队日结异常 " + exF.Message); }
            }
            catch (Exception ex) { DLog.Force("海军: 日结异常 " + ex.Message); }
        }

        // v4.270: 海运推进 —— 到达目的港就下船并还船(3×ETA+3 天超时则就近靠港)
        private void OnHourlyTick()
        {
            try
            {
                // v4.278(第 6 项): 舰队任务终态对账 —— **不受 navydiag 影响**(那是诊断开关, 这是功能):
                //   到达/超时/中断/召回都要让面板上的"任务"一栏跟实际一致, 否则停在母港的舰队
                //   会一直显示"远征/封锁"。★ 放在诊断**之前**: 诊断会读 Task 打日志,
                //   先对账才能保证日志里那一行也是刚收敛过的真值。
                try { NavalTaskForce.HourlySync(); } catch { }
                // v4.277(第 4 项 ★): 舰队移动诊断 —— 放在最前面且不受 NavalBridge.Usable 影响:
                //   它要回答的恰恰是"DLC 在不在/舰队到底走没走", 若跟着 Usable 一起 early-return,
                //   "DLC 没认出来"这种最需要诊断的情况反而一行日志都没有。
                //   默认关闭(flags 里加 navydiag 才生效), 关闭时是一次 DLog.Flag 缓存查表(零开销)。
                try { NavalTaskForce.DiagTick(); } catch { }
                if (!NavalBridge.Usable) return;
                if (NavalTransport.Active.Count == 0) return;
                NavalTransport.Tick();
            }
            catch (Exception ex) { DLog.Force("海军: 海运小时结异常 " + ex.Message); }
        }

        // ---- 1) 海军维护费: 进 Fiscal 的 Navy 支出项(与军费/建筑维护并列) ----
        private void UpkeepDaily(Kingdom k)
        {
            try
            {
                var ships = NavalFleet.MyShips();   // v4.273(审计 #2): 维护费只算我方口径
                if (ships.Count == 0 && NavalFleet.Orders.Count == 0) { NavalFleet.LastUpkeepPaid = 0; return; }

                int want = (int)Math.Round(NavalFleet.DailyUpkeep(k));
                if (want <= 0) { NavalFleet.LastUpkeepPaid = 0; return; }

                int gold = EconomyWorld.Treasury.Gold;
                int pay = Math.Min(want, Math.Max(0, gold));
                if (pay > 0)
                {
                    EconomyWorld.TreasurySpend(pay);
                    Fiscal.AddNavy(pay);
                }
                NavalFleet.LastUpkeepPaid = pay;

                // 欠费: 拖欠部分转化为厌战(水兵拿不到钱) —— 与"财政破产 -> 厌战"的既有口径一致
                if (pay < want)
                {
                    try
                    {
                        // v4.272(审计 A16): 删掉死代码 `string key = ...` / `_ = key` —— 建了从没用过的键
                        // 用"对当前最强敌国"的方向记厌战; 无敌国则跳过
                        var enemy = StrongestEnemy(k);
                        if (enemy != null)
                        {
                            string ek = k.StringId + ">" + enemy.StringId;
                            float cur;
                            if (!WarWeariness.Wear.TryGetValue(ek, out cur)) cur = 0f;
                            cur = Math.Min(100f, cur + (want - pay) / 200f);
                            WarWeariness.Wear[ek] = cur;
                        }
                    }
                    catch { }
                    if (Politics.Today() % 5 == 0)
                        MapSelection.Message("海军欠费 " + (want - pay) + " 第纳尔: 水兵不满, 厌战上升");
                }
            }
            catch (Exception ex) { DLog.Force("海军: 维护费结算失败 " + ex.Message); }
        }

        private static Kingdom StrongestEnemy(Kingdom k)
        {
            try
            {
                Kingdom best = null; float bp = -1f;
                foreach (var kk in Kingdom.All)
                {
                    if (kk == null || kk == k) continue;
                    if (!k.IsAtWarWith(kk)) continue;
                    float p = kk.CurrentTotalStrength;
                    if (p > bp) { bp = p; best = kk; }
                }
                return best;
            }
            catch { return null; }
        }

        // ---- 2) 封锁: 敌方港口繁荣度/关税下降 + 贸易禁运(原版口径), 我方获得战果 ----
        //   v4.272: 真实结算全部搬到 NavalBlockade.Daily(与面板的 EstimateBlockadeDaily **共用同一段算法**),
        //   这里只负责"该不该结算 / 什么时候自动解除"。
        private void BlockadeDaily(Kingdom k)
        {
            try
            {
                if (!NavalFleet.IsBlockading) { NavalFleet.LastBlockadeGain = 0f; NavalFleet.LastBlockadeLoss = 0f; return; }

                var port = NavalFleet.BlockadeTarget();
                if (port == null)
                {
                    // 港口没了(被烧/改名) -> 自动解除
                    MapSelection.Message("封锁自动解除: 目标港口已不存在");
                    NavalFleet.ClearBlockade();
                    return;
                }
                var owner = NavalBridge.KingdomOf(port);
                if (owner == null || owner == k || !k.IsAtWarWith(owner) || NavalFleet.MyShips().Count == 0)
                {
                    MapSelection.Message("封锁自动解除: " + (owner == null ? "港口已无主" : (!k.IsAtWarWith(owner) ? "已与该国停战" : "我方已无舰船")));
                    NavalFleet.ClearBlockade();
                    return;
                }
                if (!port.IsTown || port.Town == null) { NavalFleet.ClearBlockade(); return; }

                string err = NavalBlockade.Daily(port, k);
                if (!string.IsNullOrEmpty(err))
                {
                    MapSelection.Message("封锁自动解除: " + err);
                    NavalFleet.ClearBlockade();
                }
            }
            catch (Exception ex) { DLog.Force("海军: 封锁结算失败 " + ex.Message); }
        }

        // ---- 3) v4.272(审计 A10): 海上巡逻 —— 把 NavalTech.Sum.Patrol 接到真实落点 ----
        //   落点见 NavalPatrol: DLC 的 SettlementPatrolModel.GetPatrolPartySpawnDuration(玩家王国港口)
        //   被除以 Patrol 系数 -> 巡逻队生成更快 = 同时间内在航的巡逻队更多(巡逻力量 ×N)。
        //   这里做两件事: ①每日刷新一次系数(供补丁读取) ②统计玩家王国在航的海上巡逻队, 供面板显示。
        private void PatrolDaily(Kingdom k)
        {
            try
            {
                if (Politics.Today() == _lastPatrolDay) return;   // 一天只算一次
                _lastPatrolDay = Politics.Today();

                float mul = NavalPatrol.Refresh(k);
                NavalFleet.PatrolMul = mul;

                int parties, ships, crew;
                NavalPatrol.Strength(out parties, out ships, out crew);
                NavalFleet.PatrolParties = parties;
                NavalFleet.PatrolShips = ships;
                NavalFleet.PatrolCrew = crew;
            }
            catch (Exception ex) { DLog.Force("海军: 巡逻统计失败 " + ex.Message); }
        }
    }
}
