using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem.Settlements;

namespace FeudalInternalAffairs
{
    // 地图着色模式(v4.69)
    internal enum MapData
    {
        PoliticalConditional = 0,   // 有条件政治(默认): 拉远才显示各国领土色
        PoliticalAlways = 1,        // 无条件政治: 任何缩放都显示
        Prosperity = 2,             // 繁荣度
        Garrison = 3,               // 驻军
        Population = 4,             // 人口
        Buildings = 5,              // 建筑数
        BuildingPick = 6,           // 自选建筑
        FoodRate = 7,               // 粮食自给率
        FoodTotal = 8,              // 粮食总数
        ItemPick = 9,               // 自选物品
        Radicals = 10,              // 激进人口(越少越绿)
        Weariness = 11              // 厌战度(v4.74: 取该国最高的一条战线)
    }

    // 地图数据模式: 数据模式统一用"白色 -> 深绿色"渐变(越高越绿), 定居点名板上方显示数值
    internal static class MapDataMode
    {
        internal static MapData Current = MapData.PoliticalConditional;
        internal static string PickBuildingId = "farm";
        internal static string PickItemId = "grain";

        private static int _ver;

        internal static int Version { get { return _ver + (int)Current * 1000; } }

        internal static void MarkDirty() { _ver++; }

        internal static bool IsPolitical
        {
            get { return Current == MapData.PoliticalConditional || Current == MapData.PoliticalAlways; }
        }

        // 非默认模式: 无条件显示(不受缩放影响)
        internal static bool AlwaysOn { get { return Current != MapData.PoliticalConditional; } }

        internal static void Set(MapData m, string pickB = null, string pickI = null)
        {
            try
            {
                Current = m;
                if (!string.IsNullOrEmpty(pickB)) PickBuildingId = pickB;
                if (!string.IsNullOrEmpty(pickI)) PickItemId = pickI;
                MarkDirty();
                TerritoryColorMode.MarkDirty();
            }
            catch { }
        }

        internal static string NameOf(MapData m)
        {
            switch (m)
            {
                case MapData.PoliticalAlways: return "无条件政治地图";
                case MapData.Prosperity: return "繁荣度地图";
                case MapData.Garrison: return "驻军地图";
                case MapData.Population: return "人口地图";
                case MapData.Buildings: return "建筑地图";
                case MapData.BuildingPick: return "自选建筑地图";
                case MapData.FoodRate: return "粮食自给率地图";
                case MapData.FoodTotal: return "粮食总数地图";
                case MapData.ItemPick: return "自选物品地图";
                case MapData.Radicals: return "激进人口地图";
                case MapData.Weariness: return "厌战度地图";
                default: return "有条件政治地图";
            }
        }

        internal static string DescOf(MapData m)
        {
            switch (m)
            {
                case MapData.PoliticalAlways: return "任何缩放都显示各国领土色(不再随缩放淡入)";
                case MapData.Prosperity: return "越绿越繁荣(城镇=繁荣度, 村庄=户数)";
                case MapData.Garrison: return "越绿驻军越多(含国防军守备营)";
                case MapData.Population: return "越绿人口越多";
                case MapData.Buildings: return "越绿建筑越多";
                case MapData.BuildingPick: return "先选一种建筑, 越绿该建筑越多";
                case MapData.FoodRate: return "越绿粮食自给率越高(100% = 自给自足)";
                case MapData.FoodTotal: return "越绿粮食库存越多";
                case MapData.ItemPick: return "先选一种物品, 越绿库存越多";
                case MapData.Radicals: return "显示激进人口; 越绿社会越安定";
                case MapData.Weariness: return "越绿厌战越高(取该国最高的一条战线; 高厌战会削弱军队并倾向和谈)";
                default: return "拉远地图时显示各国领土色(默认)";
            }
        }

        internal static string CurrentPickText()
        {
            try
            {
                if (Current == MapData.BuildingPick)
                {
                    var d = BuildDefs.Get(PickBuildingId);
                    return d != null ? d.Name : PickBuildingId;
                }
                if (Current == MapData.ItemPick) return FeudalGoods.NameOf(PickItemId);
            }
            catch { }
            return "";
        }

        // 数值(<0 = 无数据); text = 名牌上方显示文本
        internal static int ValueOf(Settlement s, out string text)
        {
            text = "";
            try
            {
                if (s == null) return -1;
                switch (Current)
                {
                    case MapData.Prosperity:
                        {
                            if (s.IsVillage)
                            {
                                var v = s.Village;
                                int h = v != null ? (int)v.Hearth : 0;
                                text = "户 " + h;
                                return h;
                            }
                            var t = s.Town;
                            int p = t != null ? (int)t.Prosperity : 0;
                            text = "繁荣 " + p;
                            return p;
                        }
                    case MapData.Garrison:
                        {
                            int g = 0;
                            try { if (s.Town != null && s.Town.GarrisonParty != null && s.Town.GarrisonParty.MemberRoster != null) g = s.Town.GarrisonParty.MemberRoster.TotalManCount; } catch { }
                            try { var gp = DefArmy.GarrisonPartyOf(s); if (gp != null && gp.MemberRoster != null) g += gp.MemberRoster.TotalManCount; } catch { }
                            text = "驻军 " + g;
                            return g;
                        }
                    case MapData.Population:
                        {
                            int n = 0;
                            var list = Pops.Of(s.StringId);
                            if (list != null)
                                for (int i = 0; i < list.Count; i++)
                                {
                                    var r = list[i];
                                    if (r != null) n += (int)r.Size;
                                }
                            text = "人口 " + n.ToString("N0");
                            return n;
                        }
                    case MapData.Buildings:
                        {
                            var sb = EconomyWorld.Find(s.StringId);
                            int n = sb != null ? sb.BuiltCount : 0;
                            text = "建筑 " + n;
                            return n;
                        }
                    case MapData.BuildingPick:
                        {
                            var sb = EconomyWorld.Find(s.StringId);
                            int n = 0;
                            if (sb != null && !string.IsNullOrEmpty(PickBuildingId))
                            {
                                var g = sb.Find(PickBuildingId);
                                if (g != null) n = g.Count;
                            }
                            var def = BuildDefs.Get(PickBuildingId);
                            text = (def != null ? def.Name : PickBuildingId) + " " + n;
                            return n;
                        }
                    case MapData.FoodRate:
                        {
                            float need = 0f, prod = 0f;
                            var m = EconomyWorld.FindMarket(s.StringId);
                            if (m != null)
                                for (int i = 0; i < FeudalGoods.Main.Count; i++)
                                {
                                    var g = FeudalGoods.Main[i];
                                    if (g == null || !g.IsFood) continue;
                                    var e = m.Get(g.Id);
                                    if (e != null) { need += e.DailyConsumption; prod += e.DailyProduction; }
                                }
                            int pct = need > 0.01f ? (int)Math.Round(prod / need * 100f) : (prod > 0f ? 150 : 0);
                            text = "自给 " + pct + "%";
                            return pct;
                        }
                    case MapData.FoodTotal:
                        {
                            int n = FoodStock(s);
                            text = "粮 " + n;
                            return n;
                        }
                    case MapData.ItemPick:
                        {
                            int n = 0;
                            try
                            {
                                var it = FeudalGoods.Item(PickItemId);
                                if (it != null && s.ItemRoster != null) n = s.ItemRoster.GetItemNumber(it);
                            }
                            catch { }
                            text = FeudalGoods.NameOf(PickItemId) + " " + n;
                            return n;
                        }
                    case MapData.Radicals:
                        {
                            int n = 0;
                            var list = Pops.Of(s.StringId);
                            if (list != null)
                                for (int i = 0; i < list.Count; i++)
                                {
                                    var r = list[i];
                                    if (r != null && r.Radicalism >= 0.5f) n += (int)r.Size;
                                }
                            text = "激进 " + n;
                            return n;
                        }
                    case MapData.Weariness:
                        {
                            var k = s.MapFaction as TaleWorlds.CampaignSystem.Kingdom;
                            int w = k != null ? (int)WarWeariness.MaxWearOf(k) : 0;
                            text = "厌战 " + w;
                            return w;
                        }
                }
            }
            catch { }
            return -1;
        }

        private static int FoodStock(Settlement s)
        {
            int n = 0;
            try
            {
                var roster = s.ItemRoster;
                if (roster == null) return 0;
                for (int i = 0; i < FeudalGoods.Main.Count; i++)
                {
                    var g = FeudalGoods.Main[i];
                    if (g == null || !g.IsFood) continue;
                    var it = FeudalGoods.Item(g.Id);
                    if (it != null) n += roster.GetItemNumber(it);
                }
            }
            catch { }
            return n;
        }

        // 全局最大值(归一化用)
        internal static float MaxValue()
        {
            int mx = 1;
            try
            {
                foreach (var s in Settlement.All)
                {
                    if (s == null) continue;
                    string dummy;
                    int v = ValueOf(s, out dummy);
                    if (v > mx) mx = v;
                }
            }
            catch { }
            return mx;
        }

        // 白 -> 深绿 渐变
        internal static string Gradient(float t)
        {
            byte r, g, b;
            GradientRgb(t, out r, out g, out b);
            return "#" + r.ToString("X2") + g.ToString("X2") + b.ToString("X2");
        }

        internal static void GradientRgb(float t, out byte r, out byte g, out byte b)
        {
            if (t < 0f) t = 0f;
            if (t > 1f) t = 1f;
            r = (byte)Math.Round(255f - (255f - 18f) * t);
            g = (byte)Math.Round(255f - (255f - 95f) * t);
            b = (byte)Math.Round(255f - (255f - 18f) * t);
        }
    }
}
