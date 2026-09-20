using System;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.ObjectSystem;

namespace FeudalInternalAffairs
{
    // 玩家部队的处理: 国家意志没有自己的部队
    // 冻结(不移动/不被 AI 理会) + 隐藏(名字板由 Map/PartyNameplateMixin 负责) + 补给
    internal static class NationalWillParty
    {
        internal static void Freeze(bool force)
        {
            if (DLog.Flag("nohide")) return;
            if (BattleCommand.InCommandBattle) return;   // 亲自指挥战斗期间: 不冻结、不隐藏
            var mp = MobileParty.MainParty;
            if (mp == null) return;
            try
            {
                mp.Ai.SetDoNotMakeNewDecisions(true);
                mp.SetMoveModeHold();
                mp.IgnoreForHours(20000f);
                // 地图上的部队标记看这个标志; 游戏会随时间把它改回去, 所以每帧维持
                if (mp.IsVisible) mp.IsVisible = false;
                HideNameplate();
                if (force) DLog.Force("已冻结并隐藏玩家部队");
            }
            catch (Exception ex) { DLog.Force("冻结玩家部队失败: " + ex.Message); }
        }

        // v4.75d: 隐藏玩家主队的地图名牌(头像/名字/人数) —— 每帧维持(光靠 mixin 返回值会被刷回)
        internal static void HideNameplate()
        {
            if (DLog.Flag("nohide")) return;
            if (BattleCommand.InCommandBattle) return;
            // 1) 玩家大头像名牌(PartyPlayerNameplateVM, 不在普通名牌列表里): 主动刷新 mixin 发通知
            try { PartyPlayerNameplateVMMixin.RefreshNow(); } catch { }
            // 2) 普通部队名牌列表里的主队(如果有)
            try
            {
                var mgr = PartyNameplatesVMMixin.Instance != null ? PartyNameplatesVMMixin.Instance.Manager : null;
                if (mgr == null || mgr.Nameplates == null) return;
                for (int i = 0; i < mgr.Nameplates.Count; i++)
                {
                    var np = mgr.Nameplates[i];
                    if (np == null || np.Party == null || !np.Party.IsMainParty) continue;
                    if (np.IsVisibleOnMap) np.IsVisibleOnMap = false;
                }
            }
            catch { }
        }

        // 冻结的部队也会吃粮, 定期补充避免饿死刷屏
        internal static void EnsureFood()
        {
            try
            {
                var mp = MobileParty.MainParty;
                if (mp == null || mp.ItemRoster == null) return;
                var grain = MBObjectManager.Instance.GetObject<TaleWorlds.Core.ItemObject>("grain");
                if (grain == null) return;
                if (mp.ItemRoster.GetItemNumber(grain) < 50) mp.ItemRoster.AddToCounts(grain, 200);
            }
            catch (Exception ex) { DLog.Force("补给异常: " + ex.Message); }
        }
    }
}
