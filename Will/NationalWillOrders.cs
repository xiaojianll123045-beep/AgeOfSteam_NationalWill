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

        // 相机补丁的开关: 只要玩家已经选好国家就生效。
        internal static bool ShouldControlCamera
        {
            get
            {
                try
                {
                    if (IsActive) return true;
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
            foreach (var p in list)
            {
                if (p == null) continue;
                TakeOver(p);
                try { p.SetMoveGoToPoint(point, MobileParty.NavigationType.Default); n++; }
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
                    if (hostile)
                        p.SetMoveBesiegeSettlement(settlement, MobileParty.NavigationType.Default);   // 敌国定居点: 直接围城
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
                p.Ai.SetDoNotMakeNewDecisions(true);
                p.SetMoveModeHold();
                CommandTimeout.Touch(p);   // 10 秒内没新命令就放它自由
            }
            catch (Exception ex) { DLog.Force("接管部队失败: " + ex.Message); }
        }
    }
}
