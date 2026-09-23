using System;
using System.Collections.Generic;
using TaleWorlds.Core;

namespace FeudalInternalAffairs
{
    // v4.152: 战略储备面板(重做) —— 4 类资源: 存量/容量/天数, 采购/释放/禁运入口
    internal static class StrategicReserveUi
    {
        internal static void Open()
        {
            try
            {
                var opts = new List<InquiryElement>();
                for (int i = 0; i < StrategicReserve.KindCount; i++)
                {
                    string name = StrategicReserve.KindNames[i];
                    float stock = StrategicReserve.Stock[i];
                    float days = StrategicReserve.DaysOf(i);
                    string hint = "存量 " + (int)stock + " · 可支撑 " + days.ToString("F0") + " 天 · 日耗 " + StrategicReserve.DailyDemand(i).ToString("F1");
                    opts.Add(new InquiryElement(i, name + ": " + (int)stock, null, true, hint));
                }
                string body = "国家储备库(容量 " + StrategicReserve.TotalStock() + "/" + StrategicReserve.Capacity() + ")\n"
                    + "建仓库/粮仓/贸易站可扩容 · 采购会抽走市场库存并推高价格 · 粮食储备 ≥10 天可免疫饥荒\n"
                    + "战时粮食每日消耗 · 短缺时自动释放平抑价格";
                PanelInputGuard.ShowPopup(new MultiSelectionInquiryData(
                    "战略储备", body, opts, true, 1, 1, "管理", "关闭",
                    delegate (List<InquiryElement> sel)
                    {
                        try
                        {
                            if (sel == null || sel.Count == 0) return;
                            int kind = (int)sel[0].Identifier;
                            Manage(kind);
                        }
                        catch { }
                    }, null, null, false));
            }
            catch (Exception ex) { DLog.Force("战略储备面板失败: " + ex.Message); }
        }

        private static void Manage(int kind)
        {
            try
            {
                float stock = StrategicReserve.Stock[kind];
                float days = StrategicReserve.DaysOf(kind);
                float price = TradeRoutes.MyPriceOf(StrategicReserve.KindGoods[kind]);
                var opts = new List<InquiryElement>
                {
                    new InquiryElement("buy10", "采购 10", null, true, "约 " + (int)(price * 10 * 1.05f) + " 第纳尔"),
                    new InquiryElement("buy50", "采购 50", null, true, "约 " + (int)(price * 50 * 1.05f) + " 第纳尔"),
                    new InquiryElement("buy100", "采购 100", null, true, "约 " + (int)(price * 100 * 1.05f) + " 第纳尔"),
                    new InquiryElement("rel25", "释放 25%", null, stock >= 4f, "注入市场平抑价格"),
                    new InquiryElement("rel50", "释放 50%", null, stock >= 4f, "注入市场平抑价格"),
                    new InquiryElement("ok", "返回", null, true, null)
                };
                string body = StrategicReserve.KindNames[kind] + "\n"
                    + "存量 " + (int)stock + " / 容量 " + StrategicReserve.Capacity() + "\n"
                    + "可支撑 " + days.ToString("F0") + " 天 · 市价 " + price.ToString("F1") + " 第纳尔\n"
                    + "国库 " + (int)EconomyWorld.Treasury.Gold + " 第纳尔";
                PanelInputGuard.ShowPopup(new MultiSelectionInquiryData(
                    "储备管理 - " + StrategicReserve.KindNames[kind], body, opts, true, 1, 1, "确定", "取消",
                    delegate (List<InquiryElement> sel)
                    {
                        try
                        {
                            if (sel == null || sel.Count == 0) return;
                            string id = sel[0].Identifier as string;
                            if (id == "ok") return;
                            string msg = "";
                            if (id == "buy10") msg = StrategicReserve.Buy(kind, 10);
                            else if (id == "buy50") msg = StrategicReserve.Buy(kind, 50);
                            else if (id == "buy100") msg = StrategicReserve.Buy(kind, 100);
                            else if (id == "rel25") msg = StrategicReserve.Release(kind, (int)(stock * 0.25f));
                            else if (id == "rel50") msg = StrategicReserve.Release(kind, (int)(stock * 0.5f));
                            MapSelection.Message(msg);
                            DLog.Force("战略储备: " + msg);
                        }
                        catch { }
                    }, null, null, false));
            }
            catch (Exception ex) { DLog.Force("储备管理失败: " + ex.Message); }
        }
    }
}
