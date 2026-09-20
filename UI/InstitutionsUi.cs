using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace FeudalInternalAffairs
{
    // 国家机构 + 研究 + 文化政策 UI(文档 24.10/24.11/24.13; 弹窗式)
    internal static class InstitutionsUi
    {
        internal static void Show()
        {
            try
            {
                if (PanelInputGuard.AnyPopupActive()) return;
                var opts = new List<InquiryElement>();
                // 六机构(V3 官方口径: 由法律启用, 升级需官僚负荷与 1 年/级)
                for (int i = 0; i < Institutions.Count; i++)
                {
                    int lv = Institutions.Level[i];
                    bool enabled = Institutions.Enabled(i);
                    bool upgrading = Institutions.Upgrading(i);
                    bool can = enabled && !upgrading && lv < Institutions.MaxLevel(i);
                    string label = Institutions.Names[i] + "  " + lv + "/" + Institutions.MaxLevel(i);
                    if (!enabled) label += "  [未启用]";
                    else if (upgrading) label += "  [实施中]";
                    else if (lv < Institutions.MaxLevel(i)) label += "  [扩充 +2 负荷]";
                    else label += "  [满级]";
                    opts.Add(new InquiryElement("inst" + i, label, null, can,
                        enabled ? ("效果: " + Institutions.Effects[i] + " · 每级负担 " + Institutions.PerLevelLoad()
                            + " · 需 " + Institutions.UpgradeDays + " 天")
                        : ("未启用: 需立法 " + Institutions.EnablingLawName(i))));
                }
                // 科技三树(生产/军事/社会; 点击切换自动研究项)
                for (int s = 0; s < Research.TreeCount; s++)
                {
                    int tree = s;
                    string line = "【科技·" + Research.TreeNames[tree] + "】" + Research.ProgressText(tree);
                    opts.Add(new InquiryElement("res" + s, line, null, true, Research.BonusText(tree) + "\n点击暂停/继续该树研究"));
                }
                // 文化政策(四档切换)
                opts.Add(new InquiryElement("cul", "文化接纳: " + Institutions.CultureStatus(), null, true,
                    "切换政策(100 权威, 84 天冷却): 歧视/隔离/同化/多元\n影响: 迁移吸引力与异文化激进"));
                PanelInputGuard.ShowPopup(new MultiSelectionInquiryData(
                    "国家机构 · 研究与文化",
                    Institutions.StatusText(),
                    opts, true, 1, 1, "执行", "关闭", OnPick, null, null, false));
            }
            catch (Exception ex) { DLog.Force("机构 UI 失败: " + ex.Message); }
        }

        private static void OnPick(List<InquiryElement> sel)
        {
            try
            {
                if (sel == null || sel.Count == 0) return;
                string id = sel[0].Identifier as string;
                if (id == null) return;
                if (id.StartsWith("inst"))
                {
                    int idx = id[4] - '0';
                    MapSelection.Message(Institutions.StartUpgrade(idx));
                }
                else if (id.StartsWith("res"))
                {
                    int tree = id[3] - '0';
                    MapSelection.Message(Research.StartResearch(tree));
                }
                else if (id == "cul")
                {
                    PickCulture();
                }
            }
            catch { }
        }

        private static void PickCulture()
        {
            try
            {
                if (PanelInputGuard.AnyPopupActive()) return;
                var opts = new List<InquiryElement>();
                for (int i = 0; i < 4; i++)
                {
                    opts.Add(new InquiryElement(i, Institutions.CultureNames[i] + (i == Institutions.CulturePolicy ? " (当前)" : ""),
                        null, i != Institutions.CulturePolicy,
                        i == 0 ? "异文化受歧视: 迁移 ×0.6, 异文化激进 +" : (i == 3 ? "多元共存: 迁移 ×1.2, 异文化激进 -" : "中间政策")));
                }
                PanelInputGuard.ShowPopup(new MultiSelectionInquiryData(
                    "文化接纳政策", "调整花费 100 权威 · 同族 84 天冷却 · 影响迁移与异文化稳定",
                    opts, true, 1, 1, "确认", "返回", delegate (List<InquiryElement> s2)
                    {
                        try
                        {
                            if (s2 == null || s2.Count == 0) return;
                            MapSelection.Message(Institutions.SetCulturePolicy((int)s2[0].Identifier));
                        }
                        catch { }
                    }, delegate (List<InquiryElement> c) { Show(); }, null, false));
            }
            catch { }
        }
    }
}
