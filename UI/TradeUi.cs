using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace FeudalInternalAffairs
{
    // 贸易路线 UI(文档 24.7; 多级弹窗: 路线列表 -> 建立 -> 选伙伴 -> 选商品 -> 选方向)
    internal static class TradeUi
    {
        internal static void Show()
        {
            try
            {
                if (PanelInputGuard.AnyPopupActive()) return;
                var opts = new List<InquiryElement>();
                for (int i = 0; i < TradeRoutes.Routes.Count; i++)
                {
                    var r = TradeRoutes.Routes[i];
                    string label = (r.Export ? "出口 " : "进口 ") + FeudalGoods.NameOf(r.GoodId)
                        + " ↔ " + r.PartnerName + " (Lv" + r.Level + ")";
                    opts.Add(new InquiryElement(i, label, null, true,
                        "昨日净收益 " + r.LastProfit.ToString("N0") + " · 累计 " + r.TotalProfit.ToString("N0")
                        + "\n关税率 " + (int)(TradeRoutes.TariffRate() * 100) + "% · 货量 " + (r.Level * TradeRoutes.VolumePerLevel) + "/日"));
                }
                opts.Add(new InquiryElement(-1, "建立新贸易路线...", null, TradeRoutes.Routes.Count < TradeRoutes.MaxRoutes(),
                    "名额 " + TradeRoutes.Routes.Count + "/" + TradeRoutes.MaxRoutes() + "(关税政策与贸易行会增加名额)"));
                PanelInputGuard.ShowPopup(new MultiSelectionInquiryData(
                    "贸易路线",
                    "跨市场贸易: 低价买入/高价卖出赚取差价并按政策缴纳关税 · 差价过小或交战会自动关停",
                    opts, true, 1, 1, "确定", "关闭", OnPick, null, null, false));
            }
            catch (Exception ex) { DLog.Force("贸易 UI 失败: " + ex.Message); }
        }

        private static void OnPick(List<InquiryElement> sel)
        {
            try
            {
                if (sel == null || sel.Count == 0) return;
                int idx = (int)sel[0].Identifier;
                if (idx == -1) { PickPartner(); return; }
                RouteActions(idx);
            }
            catch { }
        }

        private static void RouteActions(int idx)
        {
            try
            {
                if (idx < 0 || idx >= TradeRoutes.Routes.Count) return;
                var r = TradeRoutes.Routes[idx];
                var opts = new List<InquiryElement>();
                opts.Add(new InquiryElement("up", "升级路线 -> Lv" + Math.Min(TradeRoutes.LevelCap(), r.Level + 1),
                    null, r.Level < TradeRoutes.LevelCap(), "提升货量(当前 " + (r.Level * TradeRoutes.VolumePerLevel) + "/日)"));
                opts.Add(new InquiryElement("close", "切断路线", null, true, "取消该贸易路线"));
                PanelInputGuard.ShowPopup(new MultiSelectionInquiryData(
                    "路线: " + (r.Export ? "出口 " : "进口 ") + FeudalGoods.NameOf(r.GoodId) + " ↔ " + r.PartnerName,
                    "我方价 " + (int)TradeRoutes.MyPriceOf(r.GoodId) + " · 对方价 " + (int)TradeRoutes.PartnerPriceOf(r, Politics.Today()),
                    opts, true, 1, 1, "执行", "返回", delegate (List<InquiryElement> s2)
                    {
                        try
                        {
                            if (s2 == null || s2.Count == 0) return;
                            string act = s2[0].Identifier as string;
                            if (act == "up") MapSelection.Message(TradeRoutes.Upgrade(idx));
                            else MapSelection.Message(TradeRoutes.Cancel(idx));
                        }
                        catch { }
                    }, delegate (List<InquiryElement> c) { Show(); }, null, false));
            }
            catch { }
        }

        private static void PickPartner()
        {
            try
            {
                if (PanelInputGuard.AnyPopupActive()) return;
                var list = TradeRoutes.PartnerKingdoms();
                var opts = new List<InquiryElement>();
                for (int i = 0; i < list.Count; i++)
                {
                    var k = list[i];
                    string label = (k.Name != null ? k.Name.ToString() : k.StringId)
                        + (k.IsAtWarWith(GetMyKingdom()) ? " [交战]" : "");
                    opts.Add(new InquiryElement(k.StringId, label, null, !k.IsAtWarWith(GetMyKingdom()),
                        "与我国关系: " + (k.IsAtWarWith(GetMyKingdom()) ? "交战(不可通商)" : "和平")));
                }
                if (opts.Count == 0) { MapSelection.Message("没有可通商的王国"); return; }
                PanelInputGuard.ShowPopup(new MultiSelectionInquiryData(
                    "建立贸易路线 · 选择伙伴",
                    "交战中的王国不可通商(路线会被强制切断)", opts, true, 1, 1, "下一步", "返回",
                    delegate (List<InquiryElement> sel)
                    {
                        try
                        {
                            if (sel == null || sel.Count == 0) return;
                            string kid = sel[0].Identifier as string;
                            Kingdom pick = null;
                            foreach (var k in list) if (k.StringId == kid) { pick = k; break; }
                            if (pick != null) PickGood(pick);
                        }
                        catch { }
                    }, delegate (List<InquiryElement> c) { Show(); }, null, false));
            }
            catch { }
        }

        private static Kingdom GetMyKingdom()
        {
            try { return NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null; } catch { return null; }
        }

        private static void PickGood(Kingdom partner)
        {
            try
            {
                if (PanelInputGuard.AnyPopupActive()) return;
                var opts = new List<InquiryElement>();
                int n = Math.Min(12, FeudalGoods.Main.Count);
                for (int i = 0; i < n; i++)
                {
                    var g = FeudalGoods.Main[i];
                    float my = TradeRoutes.MyPriceOf(g.Id);
                    float pp = 0f;
                    var tmp = new TradeRoute { GoodId = g.Id, PartnerId = partner.StringId };
                    pp = TradeRoutes.PartnerPriceOf(tmp, Politics.Today());
                    float diffPct = my > 0f ? (pp - my) / my * 100f : 0f;
                    opts.Add(new InquiryElement(g.Id,
                        FeudalGoods.NameOf(g.Id) + "  (我方 " + (int)my + " / 对方 " + (int)pp + ", " + (diffPct >= 0 ? "+" : "") + (int)diffPct + "%)",
                        null, true, diffPct >= 0 ? "出口有利(对方价高)" : "进口有利(我方价高)"));
                }
                if (opts.Count == 0) { MapSelection.Message("没有可贸易的商品"); return; }
                PanelInputGuard.ShowPopup(new MultiSelectionInquiryData(
                    "建立贸易路线 · 选择商品(与 " + (partner.Name != null ? partner.Name.ToString() : partner.StringId) + ")",
                    "差价 >10% 才有利润; 关税率 " + (int)(TradeRoutes.TariffRate() * 100) + "%", opts, true, 1, 1, "下一步", "返回",
                    delegate (List<InquiryElement> sel)
                    {
                        try
                        {
                            if (sel == null || sel.Count == 0) return;
                            string goodId = sel[0].Identifier as string;
                            PickDirection(partner, goodId);
                        }
                        catch { }
                    }, delegate (List<InquiryElement> c) { PickPartner(); }, null, false));
            }
            catch { }
        }

        private static void PickDirection(Kingdom partner, string goodId)
        {
            try
            {
                if (PanelInputGuard.AnyPopupActive()) return;
                var opts = new List<InquiryElement>();
                opts.Add(new InquiryElement("exp", "出口 " + FeudalGoods.NameOf(goodId), null, true, "卖出本国货物, 收差价 + 关税"));
                opts.Add(new InquiryElement("imp", "进口 " + FeudalGoods.NameOf(goodId), null, true, "买入外国货物, 压低本国物价"));
                PanelInputGuard.ShowPopup(new MultiSelectionInquiryData(
                    "建立贸易路线 · 方向", "路线建立后每日结算, 差价过小或交战自动关停", opts, true, 1, 1, "建立", "返回",
                    delegate (List<InquiryElement> sel)
                    {
                        try
                        {
                            if (sel == null || sel.Count == 0) return;
                            bool export = (sel[0].Identifier as string) == "exp";
                            MapSelection.Message(TradeRoutes.Establish(partner.StringId,
                                partner.Name != null ? partner.Name.ToString() : partner.StringId, goodId, export));
                        }
                        catch { }
                    }, delegate (List<InquiryElement> c) { PickGood(partner); }, null, false));
            }
            catch { }
        }
    }
}
