using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;

namespace FeudalInternalAffairs
{
    // 国家意志下命令: 只对本国部队生效, 下过命令的部队禁止 AI 自行改主意。
    // 命令作用于"当前所有选中部队"(支持框选多选)。
    internal static class NationalWillOrders
    {
        internal static bool IsActive
        {
            get
            {
                var b = Behavior;
                return b != null && b.IsNationalWill;
            }
        }

        // 相机补丁的开关: 玩家已经选好国家(或正在地图选国)就生效。
        internal static bool ShouldControlCamera
        {
            get
            {
                try
                {
                    if (IsActive) return true;
                    if (NationPickMode.Active) return true;   // v4.75e: 选国阶段也要禁"镜头回主角"等原版行为
                    return !string.IsNullOrEmpty(NationChoice.ChosenKingdomId);
                }
                catch { return false; }
            }
        }

        internal static NationalWillBehavior Behavior
        {
            get
            {
                try { return Campaign.Current != null ? Campaign.Current.GetCampaignBehavior<NationalWillBehavior>() : null; }
                catch { return null; }
            }
        }

        internal static bool IsOurs(MobileParty p)
        {
            try
            {
                if (p == null || p.IsMainParty) return false;
                var b = Behavior;
                if (b == null || !b.IsNationalWill) return false;
                var kingdom = b.NationKingdom;
                if (kingdom == null) return false;
                var faction = p.MapFaction;
                if (faction != null && ReferenceEquals(faction, kingdom)) return true;
                var clan = p.ActualClan;
                if (clan != null && ReferenceEquals(clan.Kingdom, kingdom)) return true;
                var hero = p.LeaderHero;
                if (hero != null && hero.Clan != null && ReferenceEquals(hero.Clan.Kingdom, kingdom)) return true;
            }
            catch { }
            return false;
        }

        // 可指挥的部队: 本国部队, 但农民队伍和巡逻队不允许玩家操作
        internal static bool IsCommandable(MobileParty p)
        {
            try
            {
                if (!IsOurs(p)) return false;
                if (p.IsVillager) return false;
                if (p.IsPatrolParty) return false;
                return true;
            }
            catch { return false; }
        }

        // ==================== v4.273(审计 #3): 海运中的军团不许被玩家命令改道 ====================
        //   【问题】下面三个命令入口(MoveToPoint / GoToSettlement / Engage)直接调
        //     SetMoveGoToPoint / SetMoveGoToSettlement(...,Default,...) / SetMoveEngageParty /
        //     SetMoveBesiegeSettlement, 这些方法内部会 ResetAllMovementParameters() 重写
        //     **DefaultBehavior** 与 **DesiredAiNavigationType** —— 而海运正是靠
        //     `SetMoveGoToSettlement(目的港, NavigationType.Naval, true)` 设的这两个字段在推进
        //     (见 NavalTransport.Depart 的长注释)。一次点击就把它们改回 Default/陆地行为 ⇒
        //     海运当场失去推进链路。这比 CommandTimeout 的 1 秒守卫致命得多(那个至少还会自愈)。
        //   【修法】入口先问 NavalTransport.InConvoy, 是海运中的军团就**整个动作不执行**,
        //     并给玩家一句中文提示。玩家想改道时先到海军页[召回], 或等它到港。
        private const string ConvoyBusyMsg = "该军团正在海运途中, 请先召回再下令";

        // 这支被选中的部队是不是"海运中, 不能接受新命令"? 是则累计 skipped 并返回 true
        private static bool SkipIfInConvoy(MobileParty p, ref int skipped)
        {
            try
            {
                if (p == null) return true;                       // null 本来就要 continue
                if (!NavalTransport.InConvoy(p.StringId)) return false;
                skipped++;
                DLog.Force("命令拦截(海运中): " + MapSelection.NameOf(p) + " —— " + ConvoyBusyMsg);
                return true;
            }
            catch { return false; }
        }

        // ==================== v4.276: 海军舰队的命令分流 ====================
        //   【为什么必须分流, 而不是让舰队复用下面三个入口】
        //     ① TakeOver -> DefArmy.EnsureNavigation 会把"在海上的部队"直接 `IsCurrentlyAtSea = false`
        //        + `MovePartyToTheClosestLand()` **拖回陆地** —— 对舰队是致命的(它只在海上才有意义);
        //     ② 陆军入口用的是 `NavigationType.Default`, 舰队没有陆路可言, 会试着上岸;
        //     ③ 海运守卫(InConvoy)管的是"陆军军团被船运到目的港", 舰队自己不是那个语义,
        //        混在一起会让"舰队正在执行命令"被误判成"军团在运输途中"。
        //   所以按 `fia_navy_` 前缀把舰队命令**整体**交给 NavalTaskForce(海军专用入口),
        //   两套语义一行都不交叉。
        internal static bool IsFleet(MobileParty p)
        {
            try { return NavalTaskForce.IsTaskForce(p); } catch { return false; }
        }

        internal static void MoveToPoint(CampaignVec2 point)
        {
            var list = MapSelection.SelectedList;
            int n = 0;
            int skipped = 0;   // v4.273(#3): 被"海运中"拦下的部队数
            int fleets = 0;    // v4.276: 走海军入口的舰队数
            var pt = point.ToVec2();
            foreach (var p in list)
            {
                if (p == null) continue;
                if (IsFleet(p))
                {
                    // v4.276: 舰队 = 海面点 + Naval 导航(见 IsFleet 注释)
                    try { NavalTaskForce.OrderToPoint(p, point); fleets++; }
                    catch (Exception ex) { DLog.Force("舰队移动命令失败: " + ex.Message); }
                    continue;
                }
                if (SkipIfInConvoy(p, ref skipped)) continue;   // v4.273(#3): 海运中的军团直接不改目标
                TakeOver(p);
                try
                {
                    p.SetMoveGoToPoint(point, MobileParty.NavigationType.Default);
                    CommandTimeout.Touch(p, point);   // v4.88: 保存目标点, 超时前每秒重发(防原地不动)
                    n++;
                    DLog.Force("移动命令: " + MapSelection.NameOf(p) + " 位置(" + (int)p.Position.X + "," + (int)p.Position.Y
                        + ") 目标(" + (int)pt.X + "," + (int)pt.Y + ")");
                    try
                    {
                        DLog.Force("移动状态: " + MapSelection.NameOf(p) + " 模式=" + p.PartyMoveMode
                            + " 行为=" + p.DefaultBehavior + "/" + p.ShortTermBehavior
                            + " 交互=" + p.Ai.AiBehaviorInteractable
                            + " 定居=" + (p.CurrentSettlement != null ? "是" : "否")
                            + " 地航=" + p.HasLandNavigationCapability
                            + " 海=" + p.IsCurrentlyAtSea
                            + " 距离=" + (int)p.Position.ToVec2().Distance(pt));
                    }
                    catch { }
                }
                catch (Exception ex) { DLog.Force("移动命令失败: " + ex.Message); }
            }
            if (n > 0) { MapSelection.Message("已命令 " + n + " 支部队前往指定位置"); DLog.Force("命令 " + n + " 支部队移动"); }
            // v4.276: 舰队走的是另一条链, 结果单独报一次(两类混选时两条消息都出, 语义不混)
            if (fleets > 0) MapSelection.Message("已命令 " + fleets + " 支舰队前往指定海域");
            // v4.273(#3): 提示只发一次(多选时避免刷屏); 有成功命令时不抢结果行的位置
            if (n == 0 && fleets == 0 && skipped > 0) MapSelection.Message(ConvoyBusyMsg + "(" + skipped + " 支)");
        }

        internal static void GoToSettlement(Settlement settlement)
        {
            if (settlement == null) return;
            var list = MapSelection.SelectedList;
            bool hostile = IsHostileSettlement(settlement);
            int n = 0;
            int skipped = 0;   // v4.273(#3): 被"海运中"拦下的部队数
            int fleets = 0;    // v4.276: 走海军入口的舰队数
            foreach (var p in list)
            {
                if (p == null) continue;
                if (IsFleet(p))
                {
                    // v4.276: 舰队 = 开去该港的海面(isTargetingThePort: true)。**不围城/不攻城** ——
                    //   舰队是海上力量, 登陆攻城是海运那套(把陆军军团运过去)的活。
                    // v4.280: 舰队分支原来恒传 false(=不封锁) —— 于是"点敌港"经这条路径时舰队只是开过去、不封锁,
                    //   而 MapClickPatches 的左键链是在外面用"先陆军后舰队"的顺序把结果纠正回来的。
                    //   这里改成按 hostile 传(该变量本方法内已有), 让**任何**调 GoToSettlement 的路径都拿到正确语义。
                    try { NavalTaskForce.OrderToPort(p, settlement, hostile); fleets++; }
                    catch (Exception ex) { DLog.Force("舰队前往命令失败: " + ex.Message); }
                    continue;
                }
                if (SkipIfInConvoy(p, ref skipped)) continue;   // v4.273(#3): 海运中的军团直接不改目标
                TakeOver(p);
                try
                {
                    // v4.100: 恢复原行为(允许进城); 敌国仍直接围城
                    // v4.249: 保持"点城镇 = 进城"(用户明确要求不要改成城外驻扎); 但要保证进城只发生在
                    //   "玩家真的点了这个城镇"时 —— 误判来源已另行收紧(名板判定 90->45 像素 + 世界坐标 3 米)。
                    if (hostile)
                    {
                        p.SetMoveBesiegeSettlement(settlement, MobileParty.NavigationType.Default);
                        // v4.254: 围城也给一个"卡住兜底"目标(原来只有 TakeOver 的 10 秒计时, 没有目标点)
                        // v4.290: 改用 TouchSettlement —— 让 1 秒重发走 SetMoveBesiegeSettlement 而不是
                        //   SetMoveGoToPoint(后者会清掉围城接线, 导致永远起不了攻城战)。
                        CommandTimeout.TouchSettlement(p, settlement, true,
                            new CampaignVec2(settlement.Position.ToVec2(), true));
                    }
                    else
                    {
                        p.SetMoveGoToSettlement(settlement, MobileParty.NavigationType.Default, false);
                        // v4.254: 点城镇前往也要有"手动驾驶兜底"。原来只调 TakeOver(=> Touch 无目标点),
                        //   native 一旦不驱动(实测会, 日志有"移动兜底: 0.5秒未动, 已接管驾驶")部队就永远停在半路;
                        //   目标取城门: 走到城门后原版入城逻辑会把它带进城。
                        // v4.290: 改用 TouchSettlement —— 重发必须继续调 SetMoveGoToSettlement,
                        //   否则 1 秒后接线被 SetMoveGoToPoint 清掉, 部队推到城门就停住、永远进不了城。
                        CommandTimeout.TouchSettlement(p, settlement, false,
                            new CampaignVec2(settlement.GatePosition.ToVec2(), true));
                    }
                    n++;
                }
                catch (Exception ex) { DLog.Force("前往命令失败: " + ex.Message); }
            }
            if (n > 0)
            {
                MapSelection.Message(hostile
                    ? ("已命令 " + n + " 支部队围困 " + settlement.Name)
                    : ("已命令 " + n + " 支部队前往 " + settlement.Name));
                DLog.Force((hostile ? "命令 " + n + " 支部队围城 " : "命令 " + n + " 支部队前往 ") + settlement.Name);
            }
            // v4.276: 舰队另有语义(开往该港海面, 不围城), 单独报一次
            if (fleets > 0) MapSelection.Message("已命令 " + fleets + " 支舰队前往 " + settlement.Name + " 港口");
            if (n == 0 && fleets == 0 && skipped > 0) MapSelection.Message(ConvoyBusyMsg + "(" + skipped + " 支)");
        }

        // 敌国(交战状态)的定居点: 左键点击 = 直接围城
        internal static bool IsHostileSettlement(Settlement settlement)
        {
            try
            {
                var ours = Behavior != null ? Behavior.NationKingdom : null;
                var owner = settlement != null ? settlement.MapFaction : null;
                if (ours == null || owner == null) return false;
                if (ReferenceEquals(owner, ours)) return false;
                return FactionManager.IsAtWarAgainstFaction(ours, owner);
            }
            catch { return false; }
        }

        internal static void Engage(MobileParty target)
        {
            if (target == null) return;
            var list = MapSelection.SelectedList;
            int n = 0;
            int skipped = 0;   // v4.273(#3): 被"海运中"拦下的部队数
            int fleets = 0;    // v4.276: 走海军入口的舰队数
            foreach (var p in list)
            {
                if (p == null || ReferenceEquals(p, target)) continue;
                if (IsFleet(p))
                {
                    // v4.276: 舰队 -> 海上追击(Naval 导航)。原版遭遇系统会在距离内触发 MapEvent。
                    // v4.290: 先退掉残留的点命令 —— 否则 1 秒重发会把交战接线改回 GoToPoint。
                    CommandTimeout.ReleasePoint(p);
                    try { NavalTaskForce.OrderEngage(p, target); fleets++; }
                    catch (Exception ex) { DLog.Force("舰队攻击命令失败: " + ex.Message); }
                    continue;
                }
                if (SkipIfInConvoy(p, ref skipped)) continue;   // v4.273(#3): 海运中的军团不接交战令
                TakeOver(p);
                // v4.290 修复: 交战没有"目标点", 但若这支军团此前收过"前往/驻防/巡逻", Points 里
                //   还留着旧目标 —— 1 秒重发(CommandTimeout 的重发段)会把刚设好的 EngageParty 行为
                //   改回 GoToPoint 并把部队拉向旧目标。先清掉点命令, 让驾驶段不再介入。
                CommandTimeout.ReleasePoint(p);
                try { p.SetMoveEngageParty(target, MobileParty.NavigationType.Default); n++; }
                catch (Exception ex) { DLog.Force("攻击命令失败: " + ex.Message); }
            }
            if (n > 0) { MapSelection.Message("已命令 " + n + " 支部队攻击 " + MapSelection.NameOf(target)); DLog.Force("命令 " + n + " 支部队攻击 " + MapSelection.NameOf(target)); }
            if (fleets > 0) MapSelection.Message("已命令 " + fleets + " 支舰队追击 " + MapSelection.NameOf(target));
            if (n == 0 && fleets == 0 && skipped > 0) MapSelection.Message(ConvoyBusyMsg + "(" + skipped + " 支)");
        }

        private static void TakeOver(MobileParty p)
        {
            try
            {
                // v4.90: 不再设 SetDoNotMakeNewDecisions(true)/SetMoveModeHold()
                // (1.4.8 实测该标志疑似让 native 侧冻结部队移动, 是"指挥不动"的根因)
                p.Ai.SetDoNotMakeNewDecisions(false);
                DefArmy.EnsureNavigation(p);   // v4.91: 确保 native 陆地导航可用
                CommandTimeout.Touch(p);   // 10 秒内没新命令就放它自由
                DefArmy.MarkPlayerOrder(p);   // v4.245: 玩家意志优先(军事总监/AI 不许再改道)
            }
            catch (Exception ex) { DLog.Force("接管部队失败: " + ex.Message); }
        }
    }
}
