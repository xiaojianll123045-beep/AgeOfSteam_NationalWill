using System;
using System.Collections.Generic;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace FeudalInternalAffairs
{
    // v4.146: 国策页门面(原独立 ScreenBase 已改造成全屏面板, 见 UI/FocusPanel.cs)
    //   保留 Open/Close/IsOpen 与两个弹窗方法, 所有旧调用点(NavRail/地图栏按钮/国家面板/VM)零改动。
    internal static class FocusTreeScreen
    {
        internal static bool IsOpen { get { return FocusPanel.IsOpen; } }

        internal static void Open()
        {
            Tutorials.Page("page_focus");
            FocusPanel.Open();
        }

        internal static void Close()
        {
            FocusPanel.Close();
        }

        // 点击国策 -> 原版询问框显示详情 + 开始
        internal static void ShowStartConfirm(FocusDefinition def, Action onDone)
        {
            try
            {
                PanelInputGuard.ShowPopup(new MultiSelectionInquiryData(
                    def.Name,
                    BuildBody(def) + "\n\n点击「开始」后将开始推进该政策。",
                    new List<InquiryElement> { new InquiryElement("start", "开始", null, true, null) },
                    true, 1, 1, "确定", "取消",
                    delegate (List<InquiryElement> sel)
                    {
                        if (sel != null && sel.Count > 0)
                        {
                            FocusTreeData.Start(def);
                            MapSelection.Message("已开始政策: " + def.Name);
                            DLog.Force("国策开始: " + def.Name);
                            if (onDone != null) onDone();
                        }
                    }, null, null, false));
            }
            catch (Exception ex) { DLog.Force("国策弹窗失败: " + ex.Message); }
        }

        internal static void ShowInfo(FocusDefinition def, string extra)
        {
            try
            {
                PanelInputGuard.ShowPopup(new MultiSelectionInquiryData(
                    def.Name,
                    BuildBody(def) + (string.IsNullOrEmpty(extra) ? "" : "\n\n" + extra),
                    new List<InquiryElement> { new InquiryElement("ok", "知道了", null, true, null) },
                    true, 1, 1, "确定", "取消", null, null, null, false));
            }
            catch (Exception ex) { DLog.Force("国策信息弹窗失败: " + ex.Message); }
        }

        private static string BuildBody(FocusDefinition def)
        {
            return "所需时间: " + def.Days + " 日\n"
                 + "前置: " + FocusTreeData.RequirementText(def) + "\n\n"
                 + def.Description + "\n\n"
                 + "效果:\n" + def.Effects;
        }
    }
}
