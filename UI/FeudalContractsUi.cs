using System;
using System.Collections.Generic;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace FeudalInternalAffairs
{
    // v4.126: 封建契约 UI(政治页[封建契约]按钮)
    //   两级弹窗: ①选领主家族(显示当前税档/兵役档) ②选操作(提/降 税赋或兵役), 执行后回到一级
    internal static class FeudalContractsUi
    {
        internal static void Show()
        {
            try
            {
                var options = new List<InquiryElement>();
                foreach (var kv in Politics.Lords)
                {
                    var lp = kv.Value;
                    if (lp == null) continue;
                    var c = FeudalContracts.Of(kv.Key);
                    string label = lp.Name + "  [税=" + FeudalContracts.TaxNames[c.Tax]
                        + " / 役=" + FeudalContracts.LevyNames[c.Levy] + "]";
                    options.Add(new InquiryElement(kv.Key, label, null, true,
                        "势力 " + lp.Clout + " · 愤怒 " + lp.Anger
                        + " · 契约每月影响不满 " + FeudalContracts.MonthlyAngerOf(kv.Key)
                        + "\n调整消耗 20 权威, 同族 1 年一次"));
                }
                if (options.Count == 0) { MapSelection.Message("本国没有领主家族"); return; }
                if (PanelInputGuard.AnyPopupActive()) return;
                PanelInputGuard.ShowPopup(new MultiSelectionInquiryData(
                    "封建契约",
                    "作用: 为每个领主家族设定「税赋档」与「兵役档」(各 4 档, 倍率 ×0.5 / ×1 / ×1.5 / ×2)。"
                    + "\n· 税赋档: 决定该家族领地税收上缴比例(全国税收 = 按势力加权的平均税档) — 高档国库多、领主留成少。"
                    + "\n· 兵役档: 决定可征兵上限(征兵池 × 平均兵役档) — 高档兵源多、领主负担重。"
                    + "\n· 提高档位: 领主立即不满 +12/+9(税/役) 且每月持续累积; 降低档位: 不满 -10/-8。"
                    + "\n· 调整消耗 20 权威, 同族 84 天(1 年)一次 ｜ 现有权威 " + (int)Politics.Authority,
                    options, true, 1, 1, "选择", "关闭", OnPick, null, null, false));
            }
            catch (Exception ex) { DLog.Force("契约 UI 失败: " + ex.Message); }
        }

        private static void OnPick(List<InquiryElement> sel)
        {
            try
            {
                if (sel == null || sel.Count == 0) return;
                string clanId = sel[0].Identifier as string;
                var c = FeudalContracts.Of(clanId);
                string name = clanId;
                LordProfile lp;
                if (Politics.Lords.TryGetValue(clanId, out lp) && lp != null) name = lp.Name;
                var opts = new List<InquiryElement>();
                opts.Add(new InquiryElement("tax_up", "提高税赋 -> " + FeudalContracts.TaxNames[Math.Min(3, c.Tax + 1)]
                    + " (税收 ×" + FeudalContracts.Mult[Math.Min(3, c.Tax + 1)].ToString("F1") + ")", null, c.Tax < 3,
                    "国库实收增加, 该领主不满 +12 并每月累积"));
                opts.Add(new InquiryElement("tax_dn", "降低税赋 -> " + FeudalContracts.TaxNames[Math.Max(0, c.Tax - 1)]
                    + " (税收 ×" + FeudalContracts.Mult[Math.Max(0, c.Tax - 1)].ToString("F1") + ")", null, c.Tax > 0,
                    "国库实收减少, 该领主不满 -10"));
                opts.Add(new InquiryElement("levy_up", "提高兵役 -> " + FeudalContracts.LevyNames[Math.Min(3, c.Levy + 1)]
                    + " (征兵 ×" + FeudalContracts.Mult[Math.Min(3, c.Levy + 1)].ToString("F1") + ")", null, c.Levy < 3,
                    "征兵上限增加, 该领主不满 +9 并每月累积"));
                opts.Add(new InquiryElement("levy_dn", "降低兵役 -> " + FeudalContracts.LevyNames[Math.Max(0, c.Levy - 1)]
                    + " (征兵 ×" + FeudalContracts.Mult[Math.Max(0, c.Levy - 1)].ToString("F1") + ")", null, c.Levy > 0,
                    "征兵上限减少, 该领主不满 -8"));
                if (PanelInputGuard.AnyPopupActive()) return;
                PanelInputGuard.ShowPopup(new MultiSelectionInquiryData(
                    "调整契约 · " + name,
                    "当前: 税=" + FeudalContracts.TaxNames[c.Tax] + "(×" + FeudalContracts.Mult[c.Tax].ToString("F1") + ")"
                    + " / 役=" + FeudalContracts.LevyNames[c.Levy] + "(×" + FeudalContracts.Mult[c.Levy].ToString("F1") + ")"
                    + " · 调整消耗 20 权威 · 同族 84 天一次",
                    opts, true, 1, 1, "执行", "返回",
                    delegate (List<InquiryElement> s2)
                    {
                        try
                        {
                            if (s2 == null || s2.Count == 0) return;
                            string id = s2[0].Identifier as string;
                            bool tax = id.StartsWith("tax");
                            bool up = id.EndsWith("up");
                            MapSelection.Message(FeudalContracts.Adjust(clanId, tax, up));
                            Show();   // 回到一级(刷新状态)
                        }
                        catch (Exception ex) { DLog.Force("契约执行失败: " + ex.Message); }
                    },
                    delegate { Show(); }, null, false));
            }
            catch (Exception ex) { DLog.Force("契约二级弹窗失败: " + ex.Message); }
        }
    }
}
