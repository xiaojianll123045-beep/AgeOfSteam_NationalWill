using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace FeudalInternalAffairs
{
    // 国防军右键菜单(第 23 章 23.9): 合并 / 拆分(成立军团沿用原版流程)
    internal static class DefArmyMenu
    {
        internal static int CountSelectedDefLegions()
        {
            int n = 0;
            try
            {
                var list = MapSelection.SelectedList;
                for (int i = 0; i < list.Count; i++)
                    if (list[i] != null && DefArmy.LegionOf(list[i]) != null) n++;
            }
            catch { }
            return n;
        }

        internal static void Show()
        {
            try
            {
                int n = CountSelectedDefLegions();
                var options = new List<InquiryElement>();
                if (n == 1)
                {
                    // v4.87: 单支军团 = 完整指挥菜单(用户反馈"无法指挥这支部队")
                    var one = BestSelectedLegion();
                    var olg = one != null ? DefArmy.LegionOf(one) : null;
                    string cur = olg != null && !string.IsNullOrEmpty(olg.Task) ? olg.Task : "待命";
                    options.Add(new InquiryElement("task_hold", "返回驻地驻防", null, true, "军团返回驻地并驻守 (当前任务: " + cur + ")"));
                    options.Add(new InquiryElement("task_patrol", "驻地巡逻", null, true, "军团在驻地一带巡逻"));
                    options.Add(new InquiryElement("replenish", "从守备营补员", null, true, "从驻地守备营抽调兵员补充该军团"));
                    options.Add(new InquiryElement("disband", "解散军团", null, true, "兵员转入驻地守备营, 视国库支付遣散费"));
                    options.Add(new InquiryElement("split_def", "拆分军团", null, true, "把该军团一分为二(1 名小兵升任将军, 花费 500)"));
                    // v4.245: 玩家接管 / 交还军事总监(接管后军事总监与外交 AI 都不再调走它)
                    bool held = one != null && DefArmy.IsPlayerHeld(one);
                    options.Add(new InquiryElement("hold_on", held ? "√ 归玩家指挥" : "归玩家指挥", null, true,
                        "接管该军团: 军事总监不再自动调它(你点过地图/驻防/巡逻/状态就自动接管)"));
                    options.Add(new InquiryElement("hold_off", held ? "交还军事总监" : "√ 由军事总监调度", null, true,
                        "交还给军事总监: 恢复自动休整/集结/进攻"));
                }
                else if (n >= 2)
                {
                    options.Add(new InquiryElement("merge_def", "合并国防军", null, true, "把选中的国防军军团合并为一支(多余将军卸任转为 1 名小兵, 免费)"));
                    options.Add(new InquiryElement("split_def", "拆分国防军", null, true, "把兵力最多的一支一分为二(1 名小兵升任将军, 花费 500)"));
                }
                if (options.Count == 0) return;
                PanelInputGuard.ShowPopup(new MultiSelectionInquiryData(
                    "国防军指挥", "已选中 " + n + " 支国防军军团(绿圈)",
                    options, true, 1, 1, "执行", "取消",
                    OnPicked, null, null, false));
            }
            catch (Exception ex) { DLog.Force("国防军菜单异常: " + ex.Message); }
        }

        // 选中的第一支国防军军团(多选时取兵力最多的一支用于展示指挥项)
        private static MobileParty BestSelectedLegion()
        {
            try
            {
                MobileParty best = null;
                var sel = MapSelection.SelectedList;
                for (int i = 0; i < sel.Count; i++)
                {
                    var p = sel[i];
                    if (p == null || !p.IsActive || DefArmy.LegionOf(p) == null) continue;
                    if (best == null) best = p;
                }
                return best;
            }
            catch { return null; }
        }

        private static void OnPicked(List<InquiryElement> selected)
        {
            try
            {
                if (selected == null || selected.Count == 0) return;
                string id = selected[0].Identifier as string;
                if (id == "merge_def") { DoMerge(); return; }
                if (id == "split_def") { DoSplit(); return; }
                if (id == "hold_on" || id == "hold_off")
                {
                    var hp = BestSelectedLegion();
                    if (hp == null) { MapSelection.Message("没有可指挥的国防军军团"); return; }
                    if (id == "hold_on") { DefArmy.MarkPlayerOrder(hp); MapSelection.Message(MapSelection.NameOf(hp) + " 已归玩家指挥(军事总监不再调它)"); }
                    else MapSelection.Message(DefArmy.ReleaseToAi(hp));
                    return;
                }
                if (id != "task_hold" && id != "task_patrol" && id != "replenish" && id != "disband") return;
                var p = BestSelectedLegion();
                if (p == null) { MapSelection.Message("没有可指挥的国防军军团"); return; }
                string msg;
                if (id == "task_hold") msg = DefArmy.SetTask(p, 0);
                else if (id == "task_patrol") msg = DefArmy.SetTask(p, 2);
                else if (id == "replenish") msg = DefArmy.ReplenishLegion(p, 99999);
                else msg = DefArmy.DisbandLegion(p);
                MapSelection.Message(msg);
            }
            catch (Exception ex) { DLog.Force("国防军菜单选择异常: " + ex.Message); }
        }

        internal static void DoMerge()
        {
            try
            {
                var list = new List<MobileParty>();
                var sel = MapSelection.SelectedList;
                for (int i = 0; i < sel.Count; i++)
                {
                    var p = sel[i];
                    if (p != null && p.IsActive && DefArmy.LegionOf(p) != null) list.Add(p);
                }
                var msg = DefArmy.MergeLegions(list);
                MapSelection.Message(msg);
            }
            catch (Exception ex) { DLog.Force("合并国防军异常: " + ex.Message); }
        }

        internal static void DoSplit()
        {
            try
            {
                MobileParty best = null;
                var sel = MapSelection.SelectedList;
                for (int i = 0; i < sel.Count; i++)
                {
                    var p = sel[i];
                    if (p == null || !p.IsActive || DefArmy.LegionOf(p) == null) continue;
                    if (best == null || p.MemberRoster.TotalManCount > best.MemberRoster.TotalManCount) best = p;
                }
                if (best == null) { MapSelection.Message("没有可拆分的国防军军团"); return; }
                var msg = DefArmy.SplitLegion(best);
                MapSelection.Message(msg);
            }
            catch (Exception ex) { DLog.Force("拆分国防军异常: " + ex.Message); }
        }
    }
}
