using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace FeudalInternalAffairs
{
    // 地图模式选择器(v4.69): 统计页[地图模式]按钮 -> 弹窗; 自选建筑/物品走二级弹窗
    internal static class MapDataModeUi
    {
        internal static void Open()
        {
            try
            {
                var opts = new List<InquiryElement>();
                foreach (MapData m in Enum.GetValues(typeof(MapData)))
                {
                    string mark = m == MapDataMode.Current ? "● " : "○ ";
                    string extra = "";
                    if (m == MapData.BuildingPick)
                    {
                        var d = BuildDefs.Get(MapDataMode.PickBuildingId);
                        extra = "（已选: " + (d != null ? d.Name : MapDataMode.PickBuildingId) + "）";
                    }
                    else if (m == MapData.ItemPick)
                        extra = "（已选: " + FeudalGoods.NameOf(MapDataMode.PickItemId) + "）";
                    opts.Add(new InquiryElement(m, mark + MapDataMode.NameOf(m) + extra, null, true, MapDataMode.DescOf(m)));
                }
                PanelInputGuard.ShowPopup(new MultiSelectionInquiryData(
                    "地图模式",
                    "选择地图着色方式; 数据模式为「白色→深绿」渐变(越高越绿), 定居点名板上方显示数值",
                    opts, true, 1, 1, "应用", "取消", OnPicked, null, null, false));
            }
            catch (Exception ex) { DLog.Force("地图模式弹窗异常: " + ex.Message); }
        }

        private static void OnPicked(List<InquiryElement> sel)
        {
            try
            {
                if (sel == null || sel.Count == 0) return;
                var m = (MapData)sel[0].Identifier;
                if (m == MapData.BuildingPick) { OpenBuildingPick(); return; }
                if (m == MapData.ItemPick) { OpenItemPick(); return; }
                MapDataMode.Set(m);
                MapSelection.Message("地图模式: " + MapDataMode.NameOf(m));
            }
            catch (Exception ex) { DLog.Force("地图模式选择异常: " + ex.Message); }
        }

        private static void OpenBuildingPick()
        {
            try
            {
                var opts = new List<InquiryElement>();
                for (int i = 0; i < BuildDefs.All.Count; i++)
                {
                    var d = BuildDefs.All[i];
                    if (d == null) continue;
                    string mark = d.Id == MapDataMode.PickBuildingId ? "● " : "○ ";
                    opts.Add(new InquiryElement(d.Id, mark + d.Name, null, true,
                        BuildDefs.CategoryName(d.Cat) + " · " + (d.Loc == BuildLoc.Village ? "村庄" : (d.Loc == BuildLoc.Town ? "城镇" : "多地点"))));
                }
                PanelInputGuard.ShowPopup(new MultiSelectionInquiryData(
                    "自选建筑地图",
                    "选择要显示的建筑(地图越绿=该建筑越多, 名牌显示数量)",
                    opts, true, 1, 1, "应用", "取消", OnBuildingPicked, null, null, false));
            }
            catch (Exception ex) { DLog.Force("自选建筑弹窗异常: " + ex.Message); }
        }

        private static void OnBuildingPicked(List<InquiryElement> sel)
        {
            try
            {
                if (sel == null || sel.Count == 0) return;
                string id = sel[0].Identifier as string;
                MapDataMode.Set(MapData.BuildingPick, id, null);
                var d = BuildDefs.Get(id);
                MapSelection.Message("地图模式: 自选建筑[" + (d != null ? d.Name : id) + "]");
            }
            catch (Exception ex) { DLog.Force("自选建筑选择异常: " + ex.Message); }
        }

        private static void OpenItemPick()
        {
            try
            {
                var opts = new List<InquiryElement>();
                for (int i = 0; i < FeudalGoods.All.Count; i++)
                {
                    var g = FeudalGoods.All[i];
                    if (g == null) continue;
                    string mark = g.Id == MapDataMode.PickItemId ? "● " : "○ ";
                    opts.Add(new InquiryElement(g.Id, mark + g.Name, null, true, "基础价 " + g.BasePrice));
                }
                PanelInputGuard.ShowPopup(new MultiSelectionInquiryData(
                    "自选物品地图",
                    "选择要显示的物品(地图越绿=该物品库存越多, 名牌显示数量)",
                    opts, true, 1, 1, "应用", "取消", OnItemPicked, null, null, false));
            }
            catch (Exception ex) { DLog.Force("自选物品弹窗异常: " + ex.Message); }
        }

        private static void OnItemPicked(List<InquiryElement> sel)
        {
            try
            {
                if (sel == null || sel.Count == 0) return;
                string id = sel[0].Identifier as string;
                MapDataMode.Set(MapData.ItemPick, null, id);
                MapSelection.Message("地图模式: 自选物品[" + FeudalGoods.NameOf(id) + "]");
            }
            catch (Exception ex) { DLog.Force("自选物品选择异常: " + ex.Message); }
        }
    }
}
