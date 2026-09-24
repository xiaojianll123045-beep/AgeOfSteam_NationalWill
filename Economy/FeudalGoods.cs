using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;

namespace FeudalInternalAffairs
{
    // 商品定义: 市场 / 建筑配方 / UI 统一用这里的 id
    internal class GoodDef
    {
        internal string Id;          // 市场与配方 id
        internal string ItemId;      // 游戏内 ItemObject 的 StringId
        internal string Name;        // 中文名(UI / 日志)
        internal int BasePrice;      // 基础价(第纳尔)
        internal string Sprite;      // 图标 sprite 名
        internal string Category;    // 市场分类(过滤用): 原料/加工品/军需品/生活用品/特产
        internal bool IsNew;         // 本 mod 新建(需代码注册)
        internal bool IsSpecialty;   // 特产(仅特产农场产出)
        internal bool IsFood;        // 食物
        internal bool IsLiving;      // 生活用品(繁荣度循环: 布/酒/陶器)
        internal bool IsMilitary;    // 军需品(武器/盔甲/工具)
    }

    // 商品总表: 19 种主商品 + 8 种特产(见设计文档 4.1 / 12.8)
    internal static class FeudalGoods
    {
        // 按 id 找商品定义(主商品 + 特产)
        internal static GoodDef Def(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < Main.Count; i++) if (Main[i].Id == id) return Main[i];
            for (int i = 0; i < Specialty.Count; i++) if (Specialty[i].Id == id) return Specialty[i];
            return null;
        }

        // 商品简介(市场悬停提示用; 见设计文档 4.1)
        internal static string DescOf(string id)
        {
            switch (id)
            {
                case Grain: return "城镇与军队的口粮; 库存见底会掉繁荣度, 全靠农田产出";
                case Meat: return "牧场副产品; 城镇日常消耗的肉食, 也用来制革";
                case Fish: return "渔场产出; 沿海城镇和渔村的主要口粮";
                case Wool: return "牧场剪下的羊毛; 织布坊的原料";
                case Hides: return "牧场副产品; 制革坊鞣制皮革的原料";
                case Leather: return "制革坊产出; 打造皮甲和皮革制品";
                case Hardwood: return "森林村镇伐木产出; 建造主料, 也用来烧炭";
                case Stone: return "石矿产出; 石造建筑的主料(本 mod 新增)";
                case IronOre: return "铁矿产出; 铁坊冶炼生铁的原料";
                case Charcoal: return "炭窑烧制; 铁坊冶炼不可缺少的燃料";
                case Linen: return "织布坊产出; 民生必需品(繁荣度循环)";
                case Iron: return "铁坊冶炼; 打造工具, 武器和甲胄";
                case Tools: return "打造的工具; 军需品, 提高建筑与生产效率";
                case Weapons: return "兵工厂打造; 军团装备的来源(本 mod 新增)";
                case Armor: return "兵工厂打造; 重装部队的甲胄(本 mod 新增)";
                case Clay: return "碱滩产出; 烧制陶器的原料";
                case Beer: return "酿酒坊产出; 民生必需品(繁荣度循环)";
                case Pottery: return "陶窑烧制; 民生必需品(繁荣度循环)";
                case Herbs: return "采药棚产出; 军队疗伤的必需品(本 mod 新增)";
                case Grape: return "特产: 葡萄园产出; 酿造葡萄酒";
                case Olives: return "特产: 橄榄园产出; 榨油";
                case DateFruit: return "特产: 椰枣园产出; 沙漠地区的口粮";
                case Cotton: return "特产: 棉田产出; 纺织的原料";
                case Flax: return "特产: 亚麻田产出; 纺织的原料";
                case Salt: return "特产: 盐滩产出; 腌制与调味";
                case Silver: return "特产: 银矿产出; 贵金属, 高价商品";
                case Spice: return "特产: 香料园产出; 最贵的贸易品";
                default: return "—";
            }
        }

        // ---- 主商品 id(与原版物品 id 一致, 新建商品为 fia_*) ----
        internal const string Grain = "grain";
        internal const string Meat = "meat";
        internal const string Fish = "fish";
        internal const string Wool = "wool";
        internal const string Hides = "hides";
        internal const string Leather = "leather";
        internal const string Hardwood = "hardwood";
        internal const string Stone = "fia_stone";
        internal const string IronOre = "iron";
        internal const string Charcoal = "charcoal";
        internal const string Linen = "linen";
        internal const string Iron = "iron_a";
        internal const string Tools = "tools";
        internal const string Weapons = "fia_weapons";
        internal const string Armor = "fia_armor";
        internal const string Clay = "clay";
        internal const string Beer = "beer";
        internal const string Pottery = "pottery";
        internal const string Herbs = "fia_herbs";

        // ---- v3.0 新增商品(文档 19.3): 葡萄酒 / 马匹 / 服务 ----
        internal const string Wine = "fia_wine";
        internal const string Horse = "fia_horse";
        internal const string Service = "fia_service";

        // ---- 特产 id ----
        internal const string Grape = "grape";
        internal const string Olives = "olives";
        internal const string DateFruit = "date_fruit";
        internal const string Cotton = "cotton";
        internal const string Flax = "flax";
        internal const string Salt = "salt";
        internal const string Silver = "silver";
        internal const string Spice = "spice";

        // ---- v4.167: V3 化商品扩展(第 25 章, 33 种; 短名 Id, 物品 fia_ 前缀) ----
        internal const string Lead = "lead";
        internal const string Sulfur = "sulfur";
        internal const string Oil = "oil";
        internal const string Gold = "gold";
        internal const string Coffee = "coffee";
        internal const string Sugar = "sugar";
        internal const string Tea = "tea";
        internal const string Tobacco = "tobacco";
        internal const string Opium = "opium";
        internal const string Silk = "silk";
        internal const string Rubber = "rubber";
        internal const string Dye = "dye";
        internal const string Clothes = "clothes";
        internal const string Furniture = "furniture";
        internal const string Groceries = "groceries";
        internal const string Paper = "paper";
        internal const string Steel = "steel";
        internal const string Glass = "glass";
        internal const string Fertilizer = "fertilizer";
        internal const string Explosives = "explosives";
        internal const string Engines = "engines";
        internal const string Clippers = "clippers";
        internal const string Steamers = "steamers";
        internal const string MerchantMarine = "merchant_marine";
        internal const string Ammunition = "ammunition";
        internal const string Artillery = "artillery";
        internal const string Electricity = "electricity";
        internal const string Transportation = "transportation";
        internal const string Radios = "radios";
        internal const string Telephones = "telephones";
        internal const string Automobiles = "automobiles";
        internal const string Tanks = "tanks";
        internal const string Aeroplanes = "aeroplanes";

        // ---- 主商品(19) ----
        internal static readonly List<GoodDef> Main = new List<GoodDef>
        {
            new GoodDef { Id = Grain,     ItemId = Grain,     Name = "粮食", BasePrice = 13,  Sprite = "fia_goods_grain",     IsFood = true },
            new GoodDef { Id = Meat,      ItemId = Meat,      Name = "肉",   BasePrice = 30,  Sprite = "fia_goods_meat",      IsFood = true },
            new GoodDef { Id = Fish,      ItemId = Fish,      Name = "鱼",   BasePrice = 12,  Sprite = "fia_goods_fish",      IsFood = true },
            new GoodDef { Id = Wool,      ItemId = Wool,      Name = "羊毛", BasePrice = 22,  Sprite = "fia_goods_wool" },
            new GoodDef { Id = Hides,     ItemId = Hides,     Name = "生皮", BasePrice = 50,  Sprite = "fia_goods_hides" },
            new GoodDef { Id = Leather,   ItemId = Leather,   Name = "皮革", BasePrice = 230, Sprite = "fia_goods_leather" },
            new GoodDef { Id = Hardwood,  ItemId = Hardwood,  Name = "木材", BasePrice = 25,  Sprite = "fia_goods_hardwood" },
            new GoodDef { Id = Stone,     ItemId = Stone,     Name = "石料", BasePrice = 18,  Sprite = "fia_goods_stone",     IsNew = true },
            new GoodDef { Id = IronOre,   ItemId = IronOre,   Name = "铁矿", BasePrice = 50,  Sprite = "fia_goods_iron_ore" },
            new GoodDef { Id = Charcoal,  ItemId = Charcoal,  Name = "煤",   BasePrice = 50,  Sprite = "fia_goods_charcoal" },
            new GoodDef { Id = Linen,     ItemId = Linen,     Name = "布",   BasePrice = 245, Sprite = "fia_goods_linen",     IsLiving = true },
            new GoodDef { Id = Iron,      ItemId = Iron,      Name = "铁",   BasePrice = 60,  Sprite = "fia_goods_iron" },
            new GoodDef { Id = Tools,     ItemId = Tools,     Name = "工具", BasePrice = 250, Sprite = "fia_goods_tools",     IsMilitary = true },
            new GoodDef { Id = Weapons,   ItemId = Weapons,   Name = "武器", BasePrice = 120, Sprite = "fia_goods_weapons",   IsNew = true, IsMilitary = true },
            new GoodDef { Id = Armor,     ItemId = Armor,     Name = "盔甲", BasePrice = 200, Sprite = "fia_goods_armor",     IsNew = true, IsMilitary = true },
            new GoodDef { Id = Clay,      ItemId = Clay,      Name = "黏土", BasePrice = 18,  Sprite = "fia_goods_clay" },
            new GoodDef { Id = Beer,      ItemId = Beer,      Name = "酒",   BasePrice = 50,  Sprite = "fia_goods_beer",      IsLiving = true },
            new GoodDef { Id = Pottery,   ItemId = Pottery,   Name = "陶器", BasePrice = 210, Sprite = "fia_goods_pottery",   IsLiving = true },
            new GoodDef { Id = Herbs,     ItemId = Herbs,     Name = "草药", BasePrice = 45,  Sprite = "fia_goods_herbs",     IsNew = true },
            // v3.0 新增(文档 19.3 / 19.4): 葡萄酒(酿酒坊 葡萄2→1)、马匹(牧场副产)、服务(集市/神庙/大学/酒馆产出, 当日即销)
            new GoodDef { Id = Wine,      ItemId = Wine,      Name = "葡萄酒", BasePrice = 50,  Sprite = "fia_goods_wine",    Category = "加工品" },
            new GoodDef { Id = Horse,     ItemId = Horse,     Name = "马匹",   BasePrice = 150, Sprite = "fia_bld_pasture",    Category = "原料" },
            new GoodDef { Id = Service,   ItemId = Service,   Name = "服务",   BasePrice = 30,  Sprite = "fia_cat_admin",      Category = "生活用品" }
        };

        // ---- 特产(8): 只有特产农场产出, 不参与常规配方 ----
        internal static readonly List<GoodDef> Specialty = new List<GoodDef>
        {
            new GoodDef { Id = Grape,     ItemId = Grape,     Name = "葡萄", BasePrice = 20,  Sprite = "fia_goods_grain", IsSpecialty = true },
            new GoodDef { Id = Olives,    ItemId = Olives,    Name = "橄榄", BasePrice = 30,  Sprite = "fia_goods_grain", IsSpecialty = true, IsFood = true },
            new GoodDef { Id = DateFruit, ItemId = DateFruit, Name = "枣",   BasePrice = 50,  Sprite = "fia_goods_grain", IsSpecialty = true, IsFood = true },
            new GoodDef { Id = Cotton,    ItemId = Cotton,    Name = "棉花", BasePrice = 80,  Sprite = "fia_goods_wool",  IsSpecialty = true },
            new GoodDef { Id = Flax,      ItemId = Flax,      Name = "亚麻", BasePrice = 15,  Sprite = "fia_goods_wool",  IsSpecialty = true },
            new GoodDef { Id = Salt,      ItemId = Salt,      Name = "盐",   BasePrice = 40,  Sprite = "fia_goods_clay",  IsSpecialty = true },
            new GoodDef { Id = Silver,    ItemId = Silver,    Name = "银",   BasePrice = 100, Sprite = "fia_goods_iron",  IsSpecialty = true },
            new GoodDef { Id = Spice,     ItemId = Spice,     Name = "香料", BasePrice = 300, Sprite = "fia_goods_herbs", IsSpecialty = true },
            // ================= v4.167: V3 化商品扩展(第 25 章, 33 种; 图标暂用近似 sprite, 美术后补) =================
            // 工业原料
            new GoodDef { Id = Lead,      ItemId = "fia_lead",      Name = "铅",   BasePrice = 40,  Sprite = "fia_goods_iron",     IsNew = true },
            new GoodDef { Id = Sulfur,    ItemId = "fia_sulfur",    Name = "硫磺", BasePrice = 50,  Sprite = "fia_goods_stone",    IsNew = true },
            new GoodDef { Id = Oil,       ItemId = "fia_oil",       Name = "石油", BasePrice = 40,  Sprite = "fia_goods_charcoal", IsNew = true },
            new GoodDef { Id = Rubber,    ItemId = "fia_rubber",    Name = "橡胶", BasePrice = 40,  Sprite = "fia_goods_charcoal", IsNew = true },
            new GoodDef { Id = Dye,       ItemId = "fia_dye",       Name = "染料", BasePrice = 40,  Sprite = "fia_goods_herbs",    IsNew = true },
            new GoodDef { Id = Silk,      ItemId = "fia_silk",      Name = "丝绸", BasePrice = 40,  Sprite = "fia_goods_linen",    IsNew = true },
            // 加工品
            new GoodDef { Id = Steel,     ItemId = "fia_steel",     Name = "钢",   BasePrice = 50,  Sprite = "fia_goods_iron",     IsNew = true },
            new GoodDef { Id = Glass,     ItemId = "fia_glass",     Name = "玻璃", BasePrice = 40,  Sprite = "fia_goods_stone",    IsNew = true },
            new GoodDef { Id = Paper,     ItemId = "fia_paper",     Name = "纸",   BasePrice = 30,  Sprite = "fia_goods_linen",    IsNew = true },
            new GoodDef { Id = Furniture, ItemId = "fia_furniture", Name = "家具", BasePrice = 30,  Sprite = "fia_goods_hardwood", IsNew = true },
            new GoodDef { Id = Clothes,   ItemId = "fia_clothes",   Name = "衣服", BasePrice = 30,  Sprite = "fia_goods_linen",    IsNew = true },
            new GoodDef { Id = Groceries, ItemId = "fia_groceries", Name = "食品杂货", BasePrice = 30, Sprite = "fia_goods_grain", IsNew = true, IsFood = true },
            new GoodDef { Id = Fertilizer, ItemId = "fia_fertilizer", Name = "肥料", BasePrice = 30, Sprite = "fia_goods_stone", IsNew = true },
            new GoodDef { Id = Explosives, ItemId = "fia_explosives", Name = "炸药", BasePrice = 50, Sprite = "fia_goods_stone", IsNew = true },
            new GoodDef { Id = Engines,   ItemId = "fia_engines",   Name = "发动机", BasePrice = 60, Sprite = "fia_goods_iron",   IsNew = true },
            // 船舶
            new GoodDef { Id = Clippers,  ItemId = "fia_clippers",  Name = "帆船", BasePrice = 60,  Sprite = "fia_goods_iron",     IsNew = true },
            new GoodDef { Id = Steamers,  ItemId = "fia_steamers",  Name = "蒸汽船", BasePrice = 70, Sprite = "fia_goods_iron",    IsNew = true },
            new GoodDef { Id = MerchantMarine, ItemId = "fia_merchant_marine", Name = "商船", BasePrice = 50, Sprite = "fia_goods_iron", IsNew = true },
            // 种植园奢侈
            new GoodDef { Id = Coffee,    ItemId = "fia_coffee",    Name = "咖啡", BasePrice = 50,  Sprite = "fia_goods_herbs",    IsNew = true },
            new GoodDef { Id = Sugar,     ItemId = "fia_sugar",     Name = "糖",   BasePrice = 30,  Sprite = "fia_goods_stone",    IsNew = true },
            new GoodDef { Id = Tea,       ItemId = "fia_tea",       Name = "茶",   BasePrice = 50,  Sprite = "fia_goods_herbs",    IsNew = true },
            new GoodDef { Id = Tobacco,   ItemId = "fia_tobacco",   Name = "烟草", BasePrice = 40,  Sprite = "fia_goods_herbs",    IsNew = true },
            new GoodDef { Id = Opium,     ItemId = "fia_opium",     Name = "鸦片", BasePrice = 50,  Sprite = "fia_goods_herbs",    IsNew = true },
            new GoodDef { Id = Gold,      ItemId = "fia_gold",      Name = "黄金", BasePrice = 100, Sprite = "fia_goods_iron",     IsNew = true },
            // 后期
            new GoodDef { Id = Radios,    ItemId = "fia_radios",    Name = "收音机", BasePrice = 80, Sprite = "fia_goods_iron",    IsNew = true },
            new GoodDef { Id = Telephones, ItemId = "fia_telephones", Name = "电话", BasePrice = 70, Sprite = "fia_goods_iron",   IsNew = true },
            new GoodDef { Id = Automobiles, ItemId = "fia_automobiles", Name = "汽车", BasePrice = 100, Sprite = "fia_goods_iron", IsNew = true },
            // 军用
            new GoodDef { Id = Ammunition, ItemId = "fia_ammunition", Name = "弹药", BasePrice = 50, Sprite = "fia_goods_iron",   IsNew = true, IsMilitary = true },
            new GoodDef { Id = Artillery, ItemId = "fia_artillery", Name = "火炮", BasePrice = 70,  Sprite = "fia_goods_iron",     IsNew = true, IsMilitary = true },
            new GoodDef { Id = Tanks,     ItemId = "fia_tanks",     Name = "坦克", BasePrice = 80,  Sprite = "fia_goods_iron",     IsNew = true, IsMilitary = true },
            new GoodDef { Id = Aeroplanes, ItemId = "fia_aeroplanes", Name = "飞机", BasePrice = 80, Sprite = "fia_goods_iron",    IsNew = true, IsMilitary = true },
            // 本地(不可贸易)
            new GoodDef { Id = Electricity, ItemId = "fia_electricity", Name = "电力", BasePrice = 30, Sprite = "fia_goods_stone", IsNew = true },
            new GoodDef { Id = Transportation, ItemId = "fia_transportation", Name = "运输", BasePrice = 30, Sprite = "fia_goods_stone", IsNew = true }
        };

        internal static readonly List<GoodDef> All = BuildAll();

        private static List<GoodDef> BuildAll()
        {
            var list = new List<GoodDef>(Main);
            list.AddRange(Specialty);
            return list;
        }

        private static readonly Dictionary<string, GoodDef> Index = BuildIndex();

        private static Dictionary<string, GoodDef> BuildIndex()
        {
            var d = new Dictionary<string, GoodDef>();
            foreach (var g in All) if (!d.ContainsKey(g.Id)) d.Add(g.Id, g);
            return d;
        }

        internal static GoodDef Get(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            GoodDef g;
            return Index.TryGetValue(id, out g) ? g : null;
        }

        // ==================== 市场分类(维多利亚风格过滤器) ====================
        internal static readonly string[] Categories = { "原料", "加工品", "军需品", "生活用品", "特产" };

        internal static string CategoryOf(GoodDef g)
        {
            if (g == null) return "其他";
            if (g.IsSpecialty) return "特产";
            if (g.IsLiving) return "生活用品";
            if (g.IsMilitary) return "军需品";
            switch (g.Id)
            {
                case Leather:
                case Iron:
                    return "加工品";
                default:
                    return "原料";
            }
        }

        internal static string CategorySprite(string cat)
        {
            switch (cat)
            {
                case "原料": return "fia_cat_resource";
                case "加工品": return "fia_cat_industry";
                case "军需品": return "fia_cat_military";
                case "生活用品": return "fia_cat_living";
                case "特产": return "fia_cat_trade";
                default: return "fia_cat_admin";
            }
        }

        internal static int BasePrice(string id)
        {
            var g = Get(id);
            return g != null ? g.BasePrice : 0;
        }

        internal static string NameOf(string id)
        {
            var g = Get(id);
            return g != null ? g.Name : id;
        }

        // 商品 id -> 游戏内 ItemObject
        // 原版代码创建的 16 种基础商品走 DefaultItems 静态属性; XML 商品按 id 查; 新建商品查本 mod 注册的
        internal static ItemObject Item(string id)
        {
            try
            {
                if (string.IsNullOrEmpty(id)) return null;
                var def = Get(id);
                if (def == null) return null;
                if (def.IsNew) return FeudalItems.Get(def.ItemId);
                switch (def.Id)
                {
                    case Grain:    return DefaultItems.Grain;
                    case Meat:     return DefaultItems.Meat;
                    case Hides:    return DefaultItems.Hides;
                    case Hardwood: return DefaultItems.HardWood;
                    case IronOre:  return DefaultItems.IronOre;
                    case Charcoal: return DefaultItems.Charcoal;
                    case Iron:     return DefaultItems.IronIngot3;
                    case Tools:    return DefaultItems.Tools;
                }
                var om = Game.Current != null ? Game.Current.ObjectManager : null;
                return om != null ? om.GetObject<ItemObject>(def.ItemId) : null;
            }
            catch (Exception ex)
            {
                DLog.Info("取商品物品失败 " + id + ": " + ex.Message);
                return null;
            }
        }
    }
}
