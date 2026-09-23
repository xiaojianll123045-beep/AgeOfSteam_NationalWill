using System;
using System.Collections.Generic;
using TaleWorlds.Core;

namespace FeudalInternalAffairs
{
    // v4.152: 贸易路线 UI 重做(按 V3 官方: 贸易容量/贸易优势/禁运/垄断)
    internal static class TradeUi
    {
        internal static void Open()
        {
            try
            {
                var opts = new List<InquiryElement>();
                for (int i = 0; i < TradeRoutes.Routes.Count; i++)
                {
                    var r = TradeRoutes.Routes[i];
                    string name = (r.Export ? "出口 " : "进口 ") + FeudalGoods.NameOf(r.GoodId) + " @ " + r.PartnerName;
                    string hint = "Lv" + r.Level + " · 货量 " + (r.Level * TradeRoutes.VolumePerLevel) + "/日"
                        + " · 昨日 " + (r.LastProfit >= 0 ? "+" : "") + r.LastProfit
                        + " · 累计 " + r.TotalProfit;
                    opts.Add(new InquiryElement(i, name, null, true, hint));
                }
                opts.Add(new InquiryElement(-1, "建立新贸易路线...", null,
                    TradeRoutes.Routes.Count < TradeRoutes.MaxRoutes() && TradeRoutes.UsedCapacity() + TradeRoutes.VolumePerLevel <= TradeRoutes.TotalCapacity(),
                    "名额 " + TradeRoutes.Routes.Count + "/" + TradeRoutes.MaxRoutes()
                    + " · 容量 " + TradeRoutes.UsedCapacity() + "/" + TradeRoutes.TotalCapacity()));
                opts.Add(new InquiryElement(-2, "禁运管理...", null, true, "对某国禁运: 断其贸易(花 500 第纳尔, 优势 -60)"));

                string body = "贸易容量 " + TradeRoutes.UsedCapacity() + "/" + TradeRoutes.TotalCapacity()
                    + " · 关税政策 " + (int)(TradeRoutes.TariffRate() * 100) + "%"
                    + " · 自由贸易 +25% 优势"
                    + "\n贸易优势: 100% 优势 = 25% 更好价格; 利益/同盟/贸易法提升优势"
                    + "\n货量每周按利润自动调整: 赚则扩量, 亏则缩量直至关停; 交战/禁运强制切断";
                PanelInputGuard.ShowPopup(new MultiSelectionInquiryData(
                    "贸易路线", body, opts, true, 1, 1, "确定", "关闭", OnPick, null, null, false));
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
                if (idx == -2) { EmbargoMenu(); return; }
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
                float profit = TradeRoutes.EstimateDailyProfit(r, AiDiplomacy.Today());
                float advantage = TradeRoutes.TradeAdvantageOf(r.PartnerId);
                float monopoly = TradeRoutes.MonopolyMult(r.GoodId);
                string body = (r.Export ? "出口" : "进口") + " " + FeudalGoods.NameOf(r.GoodId) + " ↔ " + r.PartnerName + "\n"
                    + "等级 Lv" + r.Level + " · 货量 " + (r.Level * TradeRoutes.VolumePerLevel) + "/日 · 占用容量 " + (r.Level * TradeRoutes.VolumePerLevel) + "\n"
                    + "昨日收益 " + (r.LastProfit >= 0 ? "+" : "") + r.LastProfit + " · 累计 " + r.TotalProfit + "\n"
                    + "预计日利润 " + (profit >= 0 ? "+" : "") + profit.ToString("F0") + "(含关税按基础价)\n"
                    + "其中关税 " + TradeRoutes.EstimateDailyTariff(r).ToString("F0") + " 金/日\n"
                    + "贸易优势 " + advantage.ToString("F0") + "(价格 ×" + TradeRoutes.AdvantagePriceMult(r.PartnerId).ToString("F2") + ")"
                    + (monopoly > 1.001f ? " · 垄断加成 ×" + monopoly.ToString("F2") : "") + "\n\n"
                    + "货量每周按利润自动调整; 可手动升级(受容量限制)";

                var opts = new List<InquiryElement>
                {
                    new InquiryElement("up", "升级货量 -> Lv" + Math.Min(TradeRoutes.LevelCap(), r.Level + 1), null,
                        r.Level < TradeRoutes.LevelCap() && TradeRoutes.UsedCapacity() + TradeRoutes.VolumePerLevel <= TradeRoutes.TotalCapacity(),
                        "货量 " + ((r.Level + 1) * TradeRoutes.VolumePerLevel) + "/日"),
                    new InquiryElement("close", "切断路线", null, true, "取消该贸易路线"),
                    new InquiryElement("ok", "返回", null, true, null)
                };
                PanelInputGuard.ShowPopup(new MultiSelectionInquiryData(
                    "路线操作", body, opts, true, 1, 1, "确定", "取消",
                    delegate (List<InquiryElement> sel2)
                    {
                        try
                        {
                            if (sel2 == null || sel2.Count == 0) return;
                            string id = sel2[0].Identifier as string;
                            if (id == "up") MapSelection.Message(TradeRoutes.Upgrade(idx));
                            else if (id == "close") MapSelection.Message(TradeRoutes.Cancel(idx));
                            Open();
                        }
                        catch { }
                    }, null, null, false));
            }
            catch (Exception ex) { DLog.Force("贸易路线操作失败: " + ex.Message); }
        }

        private static void PickPartner()
        {
            try
            {
                var opts = new List<InquiryElement>();
                var list = TradeRoutes.PartnerKingdoms();
                for (int i = 0; i < list.Count; i++)
                {
                    var k = list[i];
                    bool war = false;
                    try { war = k.IsAtWarWith(NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null); } catch { }
                    bool embargo = TradeRoutes.IsEmbargoed(k.StringId);
                    float adv = TradeRoutes.TradeAdvantageOf(k.StringId);
                    opts.Add(new InquiryElement(k.StringId, k.Name != null ? k.Name.ToString() : k.StringId, null, !war && !embargo,
                        (war ? "交战中(不可贸易) · " : (embargo ? "已禁运 · " : "")) + "贸易优势 " + adv.ToString("F0")));
                }
                PanelInputGuard.ShowPopup(new MultiSelectionInquiryData(
                    "选择伙伴", "与哪个国家建立贸易路线? (交战/禁运国不可选)", opts, true, 1, 1, "确定", "取消",
                    delegate (List<InquiryElement> sel)
                    {
                        try
                        {
                            if (sel == null || sel.Count == 0) return;
                            string pid = sel[0].Identifier as string;
                            string pname = sel[0].Title != null ? sel[0].Title.ToString() : pid;
                            PickGood(pid, pname);
                        }
                        catch { }
                    }, null, null, false));
            }
            catch (Exception ex) { DLog.Force("选择伙伴失败: " + ex.Message); }
        }

        private static void PickGood(string partnerId, string partnerName)
        {
            try
            {
                var opts = new List<InquiryElement>();
                foreach (var g in FeudalGoods.Main)
                {
                    if (g == null || string.IsNullOrEmpty(g.Id)) continue;
                    var item = FeudalGoods.Item(g.Id);
                    if (item == null) continue;
                    float my = TradeRoutes.MyPriceOf(g.Id);
                    opts.Add(new InquiryElement(g.Id, item.Name.ToString(), null, true,
                        "本国价 " + my.ToString("F1") + " · 基础价 " + g.BasePrice));
                }
                PanelInputGuard.ShowPopup(new MultiSelectionInquiryData(
                    "选择商品 - " + partnerName, "进口(买入)还是出口(卖出)?", opts, true, 1, 1, "确定", "取消",
                    delegate (List<InquiryElement> sel)
                    {
                        try
                        {
                            if (sel == null || sel.Count == 0) return;
                            string gid = sel[0].Identifier as string;
                            string gname = sel[0].Title != null ? sel[0].Title.ToString() : gid;
                            DirectionMenu(partnerId, partnerName, gid, gname);
                        }
                        catch { }
                    }, null, null, false));
            }
            catch (Exception ex) { DLog.Force("选择商品失败: " + ex.Message); }
        }

        private static void DirectionMenu(string partnerId, string partnerName, string goodId, string goodName)
        {
            try
            {
                float my = TradeRoutes.MyPriceOf(goodId);
                var opts = new List<InquiryElement>
                {
                    new InquiryElement("exp", "出口(卖出)", null, true, "外国更贵时赚差价; 本国价 " + my.ToString("F1")),
                    new InquiryElement("imp", "进口(买入)", null, true, "外国更便宜时赚差价; 本国价 " + my.ToString("F1"))
                };
                PanelInputGuard.ShowPopup(new MultiSelectionInquiryData(
                    goodName + " @ " + partnerName, "选择贸易方向(差价每周自动调整货量)", opts, true, 1, 1, "确定", "取消",
                    delegate (List<InquiryElement> sel)
                    {
                        try
                        {
                            if (sel == null || sel.Count == 0) return;
                            bool export = (string)sel[0].Identifier == "exp";
                            MapSelection.Message(TradeRoutes.Establish(partnerId, partnerName, goodId, export));
                        }
                        catch { }
                    }, null, null, false));
            }
            catch (Exception ex) { DLog.Force("选择方向失败: " + ex.Message); }
        }

        // 禁运管理(V3 官方: 手动禁运; 交战自动)
        private static void EmbargoMenu()
        {
            try
            {
                var opts = new List<InquiryElement>();
                var list = TradeRoutes.PartnerKingdoms();
                for (int i = 0; i < list.Count; i++)
                {
                    var k = list[i];
                    bool embargo = TradeRoutes.IsEmbargoed(k.StringId);
                    float adv = TradeRoutes.TradeAdvantageOf(k.StringId);
                    opts.Add(new InquiryElement(k.StringId, (embargo ? "[已禁运] " : "") + (k.Name != null ? k.Name.ToString() : k.StringId), null, true,
                        "贸易优势 " + adv.ToString("F0") + " · 点击切换禁运状态"));
                }
                PanelInputGuard.ShowPopup(new MultiSelectionInquiryData(
                    "禁运管理", "对某国禁运: 立即切断所有路线, 贸易优势 -60(花 500 第纳尔); 再次点击解除\n交战国自动禁运(免费)", opts, true, 1, 1, "切换", "关闭",
                    delegate (List<InquiryElement> sel)
                    {
                        try
                        {
                            if (sel == null || sel.Count == 0) return;
                            string pid = sel[0].Identifier as string;
                            string pname = sel[0].Title != null ? sel[0].Title.ToString() : pid;
                            bool on = !TradeRoutes.IsEmbargoed(pid);
                            MapSelection.Message(TradeRoutes.SetEmbargo(pid, pname, on));
                        }
                        catch { }
                    }, null, null, false));
            }
            catch (Exception ex) { DLog.Force("禁运管理失败: " + ex.Message); }
        }
    }
}
