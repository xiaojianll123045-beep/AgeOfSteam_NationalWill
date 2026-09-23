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

        internal static void MoveToPoint(CampaignVec2 point)
        {
            var list = MapSelection.SelectedList;
            int n = 0;
            var pt = point.ToVec2();
            foreach (var p in list)
            {
                if (p == null) continue;
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
        }

        internal static void GoToSettlement(Settlement settlement)
        {
            if (settlement == null) return;
            var list = MapSelection.SelectedList;
            bool hostile = IsHostileSettlement(settlement);
            int n = 0;
            foreach (var p in list)
            {
                if (p == null) continue;
                TakeOver(p);
                try
                {
                    // v4.100: 恢复原行为(允许进城); 敌国仍直接围城
                    if (hostile)
                        p.SetMoveBesiegeSettlement(settlement, MobileParty.NavigationType.Default);
                    else
                        p.SetMoveGoToSettlement(settlement, MobileParty.NavigationType.Default, false);
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
            foreach (var p in list)
            {
                if (p == null || ReferenceEquals(p, target)) continue;
                TakeOver(p);
                try { p.SetMoveEngageParty(target, MobileParty.NavigationType.Default); n++; }
                catch (Exception ex) { DLog.Force("攻击命令失败: " + ex.Message); }
            }
            if (n > 0) { MapSelection.Message("已命令 " + n + " 支部队攻击 " + MapSelection.NameOf(target)); DLog.Force("命令 " + n + " 支部队攻击 " + MapSelection.NameOf(target)); }
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
