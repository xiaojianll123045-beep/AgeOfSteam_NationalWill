using System;
using TaleWorlds.Core;
using TaleWorlds.Localization;

namespace FeudalInternalAffairs
{
    // 本 mod 新建的 4 种商品(原版没有): 石料 / 武器 / 盔甲 / 草药
    // 沿用原版 DefaultItems 的代码创建模式(不走 XML):
    //   1) 先建 ItemCategory(贸易品类别) 2) 再建 ItemObject 并 InitializeTradeGood
    // 时机: 必须在 XML 商品加载完成之后(Game 初始化结束)执行, 见 SubModule.OnGameInitializationFinished
    internal static class FeudalItems
    {
        internal const string StoneId = "fia_stone";
        internal const string WeaponsId = "fia_weapons";
        internal const string ArmorId = "fia_armor";
        internal const string HerbsId = "fia_herbs";
        // v3.0 新增(文档 19.3): 葡萄酒 / 马匹 / 服务
        internal const string WineId = "fia_wine";
        internal const string HorseId = "fia_horse";
        internal const string ServiceId = "fia_service";

        // v4.167: V3 化商品扩展(第 25 章, 33 种; 借原版网格)
        internal const string LeadId = "fia_lead";
        internal const string SulfurId = "fia_sulfur";
        internal const string OilId = "fia_oil";
        internal const string GoldId = "fia_gold";
        internal const string CoffeeId = "fia_coffee";
        internal const string SugarId = "fia_sugar";
        internal const string TeaId = "fia_tea";
        internal const string TobaccoId = "fia_tobacco";
        internal const string OpiumId = "fia_opium";
        internal const string SilkId = "fia_silk";
        internal const string RubberId = "fia_rubber";
        internal const string DyeId = "fia_dye";
        internal const string ClothesId = "fia_clothes";
        internal const string FurnitureId = "fia_furniture";
        internal const string GroceriesId = "fia_groceries";
        internal const string PaperId = "fia_paper";
        internal const string SteelId = "fia_steel";
        internal const string GlassId = "fia_glass";
        internal const string FertilizerId = "fia_fertilizer";
        internal const string ExplosivesId = "fia_explosives";
        internal const string EnginesId = "fia_engines";
        internal const string ClippersId = "fia_clippers";
        internal const string SteamersId = "fia_steamers";
        internal const string MerchantMarineId = "fia_merchant_marine";
        internal const string AmmunitionId = "fia_ammunition";
        internal const string ArtilleryId = "fia_artillery";
        internal const string ElectricityId = "fia_electricity";
        internal const string TransportationId = "fia_transportation";
        internal const string RadiosId = "fia_radios";
        internal const string TelephonesId = "fia_telephones";
        internal const string AutomobilesId = "fia_automobiles";
        internal const string TanksId = "fia_tanks";
        internal const string AeroplanesId = "fia_aeroplanes";

        private static Game _game;
        private static bool _registered;

        internal static bool IsRegistered { get { return _registered; } }

        internal static ItemObject Get(string id)
        {
            try
            {
                var om = Game.Current != null ? Game.Current.ObjectManager : null;
                return om != null ? om.GetObject<ItemObject>(id) : null;
            }
            catch { return null; }
        }

        internal static ItemObject Stone { get { return Get(StoneId); } }
        internal static ItemObject Weapons { get { return Get(WeaponsId); } }
        internal static ItemObject Armor { get { return Get(ArmorId); } }
        internal static ItemObject Herbs { get { return Get(HerbsId); } }

        // 幂等: 同一局只注册一次; 必须在存档数据加载之前完成(否则存档里的商品引用会解析失败)
        // 因此 OnGameStart 与 OnGameInitializationFinished 都会调用, 谁先到谁注册
        internal static void Register(Game game, string source)
        {
            try
            {
                if (game == null || game.ObjectManager == null) return;
                var om = game.ObjectManager;
                // 已存在(重复调用 / 读档)则跳过 —— 注意先查存在性, 防止对象管理器被重置后不再注册
                if (om.GetObject<ItemObject>(StoneId) != null)
                {
                    if (!_registered) { _registered = true; _game = game; DLog.Info("商品: 已存在, 跳过注册(" + source + ")"); }
                    return;
                }
                // 石料: 借用原版石堆模型
                CreateTradeGood(om, StoneId, "fia_item_stone", "Stone", "loads of stone", "merchandise_stones", 18);
                // 武器: 借用原版铁器捆
                CreateTradeGood(om, WeaponsId, "fia_item_weapons", "Weapons", "bundles of weapons", "merchandise_ironware", 120);
                // 盔甲: 借用原版铁器(马蹄铁)模型
                CreateTradeGood(om, ArmorId, "fia_item_armor", "Armor", "sets of armor", "merchandise_ironware_horseshoe", 200);
                // 草药: 借用原版亚麻束(形似草药)
                CreateTradeGood(om, HerbsId, "fia_item_herbs", "Herbs", "bundles of herbs", "merchandise_flax", 45);
                // v3.0: 葡萄酒(借用亚麻束外观) / 马匹(马蹄铁) / 服务(石堆=集市摊位)
                CreateTradeGood(om, WineId, "fia_item_wine", "Wine", "jars of wine", "merchandise_flax", 50);
                CreateTradeGood(om, HorseId, "fia_item_horse", "Horses", "herds of horses", "merchandise_ironware_horseshoe", 150);
                CreateTradeGood(om, ServiceId, "fia_item_service", "Services", "market services", "merchandise_stones", 30);
                // v4.167: V3 化商品扩展(第 25 章)
                CreateTradeGood(om, LeadId, "fia_item_lead", "Lead", "ingots of lead", "merchandise_ironware", 40);
                CreateTradeGood(om, SulfurId, "fia_item_sulfur", "Sulfur", "lumps of sulfur", "merchandise_stones", 50);
                CreateTradeGood(om, OilId, "fia_item_oil", "Oil", "barrels of oil", "merchandise_ironware_horseshoe", 40);
                CreateTradeGood(om, GoldId, "fia_item_gold", "Gold", "ingots of gold", "merchandise_ironware", 100);
                CreateTradeGood(om, CoffeeId, "fia_item_coffee", "Coffee", "sacks of coffee", "merchandise_flax", 50);
                CreateTradeGood(om, SugarId, "fia_item_sugar", "Sugar", "sacks of sugar", "merchandise_stones", 30);
                CreateTradeGood(om, TeaId, "fia_item_tea", "Tea", "chests of tea", "merchandise_flax", 50);
                CreateTradeGood(om, TobaccoId, "fia_item_tobacco", "Tobacco", "bales of tobacco", "merchandise_flax", 40);
                CreateTradeGood(om, OpiumId, "fia_item_opium", "Opium", "chests of opium", "merchandise_flax", 50);
                CreateTradeGood(om, SilkId, "fia_item_silk", "Silk", "bolts of silk", "merchandise_flax", 40);
                CreateTradeGood(om, RubberId, "fia_item_rubber", "Rubber", "blocks of rubber", "merchandise_ironware_horseshoe", 40);
                CreateTradeGood(om, DyeId, "fia_item_dye", "Dye", "barrels of dye", "merchandise_ironware_horseshoe", 40);
                CreateTradeGood(om, ClothesId, "fia_item_clothes", "Clothes", "bundles of clothes", "merchandise_flax", 30);
                CreateTradeGood(om, FurnitureId, "fia_item_furniture", "Furniture", "sets of furniture", "merchandise_flax", 30);
                CreateTradeGood(om, GroceriesId, "fia_item_groceries", "Groceries", "crates of groceries", "merchandise_flax", 30);
                CreateTradeGood(om, PaperId, "fia_item_paper", "Paper", "reams of paper", "merchandise_flax", 30);
                CreateTradeGood(om, SteelId, "fia_item_steel", "Steel", "ingots of steel", "merchandise_ironware", 50);
                CreateTradeGood(om, GlassId, "fia_item_glass", "Glass", "crates of glass", "merchandise_stones", 40);
                CreateTradeGood(om, FertilizerId, "fia_item_fertilizer", "Fertilizer", "sacks of fertilizer", "merchandise_stones", 30);
                CreateTradeGood(om, ExplosivesId, "fia_item_explosives", "Explosives", "kegs of explosives", "merchandise_stones", 50);
                CreateTradeGood(om, EnginesId, "fia_item_engines", "Engines", "crated engines", "merchandise_ironware", 60);
                CreateTradeGood(om, ClippersId, "fia_item_clippers", "Clippers", "clipper ships", "merchandise_ironware", 60);
                CreateTradeGood(om, SteamersId, "fia_item_steamers", "Steamers", "steam ships", "merchandise_ironware", 70);
                CreateTradeGood(om, MerchantMarineId, "fia_item_merchant_marine", "Merchant Marine", "merchant vessels", "merchandise_ironware", 50);
                CreateTradeGood(om, AmmunitionId, "fia_item_ammunition", "Ammunition", "crates of ammunition", "merchandise_ironware", 50);
                CreateTradeGood(om, ArtilleryId, "fia_item_artillery", "Artillery", "field guns", "merchandise_ironware", 70);
                CreateTradeGood(om, ElectricityId, "fia_item_electricity", "Electricity", "units of electricity", "merchandise_stones", 30);
                CreateTradeGood(om, TransportationId, "fia_item_transportation", "Transportation", "units of transport", "merchandise_stones", 30);
                CreateTradeGood(om, RadiosId, "fia_item_radios", "Radios", "crated radios", "merchandise_ironware", 80);
                CreateTradeGood(om, TelephonesId, "fia_item_telephones", "Telephones", "crated telephones", "merchandise_ironware", 70);
                CreateTradeGood(om, AutomobilesId, "fia_item_automobiles", "Automobiles", "motor cars", "merchandise_ironware", 100);
                CreateTradeGood(om, TanksId, "fia_item_tanks", "Tanks", "armored vehicles", "merchandise_stones", 80);
                CreateTradeGood(om, AeroplanesId, "fia_item_aeroplanes", "Aeroplanes", "aeroplanes", "merchandise_stones", 80);
                _registered = true;
                _game = game;
                DLog.Info("商品: 注册来源=" + source);
                Dump();
            }
            catch (Exception ex) { DLog.Force("商品注册失败(" + source + "): " + ex); }
        }

        private static void CreateTradeGood(TaleWorlds.ObjectSystem.MBObjectManager om, string itemId,
            string textKey, string englishName, string pluralName, string mesh, int value)
        {
            // 1) 贸易品类别(每个新商品一个独立类别)
            var catId = itemId;
            var cat = om.RegisterPresumedObject(new ItemCategory(catId));
            cat.InitializeObject(true, 10, 0, ItemCategory.Property.None, null, 0f, false, true);
            // 2) 物品本体(名称用本模块语言文件里的 key)
            var item = om.RegisterPresumedObject(new ItemObject(itemId));
            var name = new TextObject("{=" + textKey + "}" + englishName + "{@Plural}" + pluralName + "{\\@}");
            ItemObject.InitializeTradeGood(item, name, mesh, cat, value, 10f, ItemObject.ItemTypeEnum.Goods, false);
            DLog.Info("商品: 新建 " + itemId + " 名称=" + item.Name + " 基础价=" + item.Value
                + " 类别=" + (item.ItemCategory != null ? item.ItemCategory.StringId : "?")
                + " 网格=" + item.MultiMeshName + " 类型=" + item.ItemType);
        }

        // 注册结果自检(写日志)
        private static void Dump()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var id in new[] { StoneId, WeaponsId, ArmorId, HerbsId, WineId, HorseId, ServiceId,
                LeadId, SulfurId, OilId, GoldId, CoffeeId, SugarId, TeaId, TobaccoId, OpiumId, SilkId, RubberId, DyeId,
                ClothesId, FurnitureId, GroceriesId, PaperId, SteelId, GlassId, FertilizerId, ExplosivesId, EnginesId,
                ClippersId, SteamersId, MerchantMarineId, AmmunitionId, ArtilleryId, ElectricityId, TransportationId,
                RadiosId, TelephonesId, AutomobilesId, TanksId, AeroplanesId })
            {
                var it = Get(id);
                if (it == null) { sb.Append(id + "=失败 "); continue; }
                sb.Append(it.StringId + "=" + it.Name + "(" + it.Value + ") ");
            }
            DLog.Force("商品: 4 种新商品注册完成 -> " + sb.ToString().TrimEnd());
        }
    }
}
