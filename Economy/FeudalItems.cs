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
            foreach (var id in new[] { StoneId, WeaponsId, ArmorId, HerbsId })
            {
                var it = Get(id);
                if (it == null) { sb.Append(id + "=失败 "); continue; }
                sb.Append(it.StringId + "=" + it.Name + "(" + it.Value + ") ");
            }
            DLog.Force("商品: 4 种新商品注册完成 -> " + sb.ToString().TrimEnd());
        }
    }
}
