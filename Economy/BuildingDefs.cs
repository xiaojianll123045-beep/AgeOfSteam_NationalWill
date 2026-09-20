using System;
using System.Collections.Generic;

namespace FeudalInternalAffairs
{
    [Flags]
    internal enum BuildLoc
    {
        Village = 1,
        Castle = 2,
        Town = 4
    }

    internal enum BuildCat
    {
        Resource,   // 资源
        Industry,   // 加工
        Military,   // 军事
        Admin,      // 行政
        Trade,      // 贸易
        Logistics,  // 后勤
        Living      // 民生
    }

    internal enum BuildMode
    {
        Wood = 0,
        Stone = 1,
        Iron = 2
    }

    // 一个数值项: Good = 商品 id, 或 "@效果id"; V = [木档, 石档, 铁档]
    // 商品 id 里带 '|' 表示"任意一种"(如 "wool|cotton|flax")
    internal class Amount
    {
        internal string Good;
        internal float[] V;
        // v3.7 校准(19.12): 实测供给约为需求的 6 倍(粮食 产 1646 / 需 153), 价格被压向地板价(25%基础价) -> 建筑全亏
        // 统一缩放所有"实物产出/投入"(效果类 @xxx 数值不缩放), 让供需回到同一量级
        internal static float Scale = 1f / 6f;

        internal float Value(BuildMode m)
        {
            float v = V[(int)m];
            if (Good != null && Good.StartsWith("@")) return v;
            return v * Scale;
        }
    }

    internal class BuildDef
    {
        internal string Id;
        internal string Name;
        internal string Sprite;                        // 图标 sprite 名
        internal BuildLoc Loc;                         // 可建地点(可多个)
        internal BuildCat Cat;
        internal bool CanBePrivate;                    // 可私有(城堡建筑一律国有, 见 BuildDefs.CanBePrivate)
        internal List<Amount> Outputs = new List<Amount>();   // 产出 / 效果
        internal List<Amount> Inputs = new List<Amount>();    // 输入(不随档位变化)
        internal List<Amount>[] InputsByMode;                 // 输入随档位变化(建造部门); 非空则忽略 Inputs
        internal float[] Maintenance = new float[3];          // 维护费 / 日
        internal int[] Work = new int[3];                     // 建造工时
        internal float[] ExtraStoneOverride = null;           // 附加石料覆盖(默认全局规则)
        internal float[] ExtraIronOverride = null;            // 附加铁覆盖(默认全局规则)

        internal bool IsEffect
        {
            get { return Outputs.Count > 0 && Outputs[0].Good != null && Outputs[0].Good.StartsWith("@"); }
        }
    }

    // 建筑总表(见设计文档 2.4 / 12.3)
    internal static class BuildDefs
    {
        // 效果 id(产出里以 @ 开头)
        internal const string EffConstruction = "@construction";       // 建造点
        internal const string EffSpecialtyValue = "@specialty_value";  // 特产(按价值归一)
        internal const string EffStorage = "@storage";                 // 库存上限
        internal const string EffLoyalty = "@loyalty";                 // 忠诚
        internal const string EffMilitia = "@militia";                 // 民兵
        internal const string EffGarrison = "@garrison";               // 驻军上限
        internal const string EffRecruit = "@recruit";                 // 征兵速度 %
        internal const string EffSupply = "@supply";                   // 军需容量
        internal const string EffCavalry = "@cavalry";                 // 骑兵补充 %
        internal const string EffDefense = "@defense";                 // 防御 %
        internal const string EffVision = "@vision";                   // 视野 %
        internal const string EffSecurity = "@security";               // 治安
        internal const string EffBandit = "@bandit";                   // 盗匪 %(负值)
        internal const string EffTradeCap = "@trade_cap";              // 贸易容量
        internal const string EffPrice = "@price";                     // 本地价 %(负值=改善)
        internal const string EffImportCap = "@import_cap";            // 进出口容量
        internal const string EffCaravanSpeed = "@caravan_speed";      // 商队速度 %
        internal const string EffTax = "@tax";                         // 税收 %
        internal const string EffProsperity = "@prosperity";           // 繁荣度 / 日
        internal const string EffInterest = "@interest";               // 利息 %(负值=降息)
        internal const string EffFocusSpeed = "@focus_speed";          // 国策推进 %
        internal const string EffPopulation = "@population";           // 人口增长 %
        internal const string EffMarketPrice = "@market_price";        // 市场价改善(占位)

        private static Amount A(string good, float w, float s, float i)
        {
            return new Amount { Good = good, V = new[] { w, s, i } };
        }

        private static Amount AM(string good, float v)
        {
            return new Amount { Good = good, V = new[] { v, v, v } };
        }

        private static float[] F(float w, float s, float i)
        {
            return new[] { w, s, i };
        }

        private static readonly int[] WResource = { 30, 80, 200 };   // 12.4 建造工时
        private static readonly int[] WIndustry = { 40, 100, 240 };
        private static readonly int[] WMilitary = { 50, 120, 300 };
        private static readonly int[] WAdmin = { 40, 100, 240 };

        internal static readonly List<BuildDef> All = new List<BuildDef>
        {
            // ================= 村庄 · 资源类 =================
            new BuildDef { Id = "farm", Name = "农田", Sprite = "fia_bld_farm", Loc = BuildLoc.Village, Cat = BuildCat.Resource, CanBePrivate = true,
                Outputs = { A(FeudalGoods.Grain, 6f, 9f, 13.8f) }, Maintenance = F(2.0f, 1.4f, 0.9f), Work = WResource },
            new BuildDef { Id = "specialty_farm", Name = "特产农场", Sprite = "fia_bld_orchard", Loc = BuildLoc.Village, Cat = BuildCat.Resource, CanBePrivate = true,
                Outputs = { A(FeudalGoods.Grape + "|" + FeudalGoods.Olives + "|" + FeudalGoods.DateFruit + "|" + FeudalGoods.Cotton + "|" + FeudalGoods.Flax + "|" + FeudalGoods.Spice, 1.5f, 2.25f, 3.45f) }, Maintenance = F(2.0f, 1.4f, 0.9f), Work = WResource },
            new BuildDef { Id = "pasture", Name = "牧场", Sprite = "fia_bld_pasture", Loc = BuildLoc.Village, Cat = BuildCat.Resource, CanBePrivate = true,
                Outputs = { A(FeudalGoods.Meat, 3f, 4.5f, 6.9f), A(FeudalGoods.Hides, 0.5f, 0.75f, 1.15f), A(FeudalGoods.Wool, 1f, 1.5f, 2.3f), A(FeudalGoods.Horse, 0.1f, 0.15f, 0.23f) },
                Maintenance = F(2.5f, 1.75f, 1.1f), Work = WResource },
            new BuildDef { Id = "lumberjack", Name = "伐木场", Sprite = "fia_bld_lumber", Loc = BuildLoc.Village | BuildLoc.Castle, Cat = BuildCat.Resource, CanBePrivate = true,
                Outputs = { A(FeudalGoods.Hardwood, 4f, 6f, 9.2f) }, Maintenance = F(2.0f, 1.4f, 0.9f), Work = WResource },
            // v4.64: 炭窑(铁链命脉: 设计里一直有"煤", 但从未有建筑产出 -> 铁厂永远停摆, 全商品大面积 0 产出)
            new BuildDef { Id = "charcoal_kiln", Name = "炭窑", Sprite = "fia_bld_mine", Loc = BuildLoc.Village | BuildLoc.Castle, Cat = BuildCat.Industry, CanBePrivate = true,
                Inputs = { A(FeudalGoods.Hardwood, 2f, 2f, 2f) },
                Outputs = { A(FeudalGoods.Charcoal, 3f, 4.5f, 6.9f) }, Maintenance = F(2.0f, 1.4f, 0.9f), Work = WIndustry },
            new BuildDef { Id = "mine_iron", Name = "矿场(铁矿)", Sprite = "fia_bld_mine", Loc = BuildLoc.Village, Cat = BuildCat.Resource, CanBePrivate = true,
                Outputs = { A(FeudalGoods.IronOre, 3f, 4.5f, 6.9f) }, Maintenance = F(2.5f, 1.75f, 1.1f), Work = WResource },
            new BuildDef { Id = "mine_clay_salt", Name = "矿场(黏土/盐)", Sprite = "fia_bld_mine", Loc = BuildLoc.Village, Cat = BuildCat.Resource, CanBePrivate = true,
                Outputs = { A(FeudalGoods.Clay + "|" + FeudalGoods.Salt, 4f, 6f, 9.2f) }, Maintenance = F(2.0f, 1.4f, 0.9f), Work = WResource },
            new BuildDef { Id = "mine_silver", Name = "矿场(银)", Sprite = "fia_bld_mine", Loc = BuildLoc.Village, Cat = BuildCat.Resource, CanBePrivate = true,
                Outputs = { A(FeudalGoods.Silver, 1f, 1.5f, 2.3f) }, Maintenance = F(3.0f, 2.1f, 1.35f), Work = WResource },
            new BuildDef { Id = "quarry", Name = "采石场", Sprite = "fia_bld_mine", Loc = BuildLoc.Castle, Cat = BuildCat.Resource, CanBePrivate = true,
                Outputs = { A(FeudalGoods.Stone, 4f, 6f, 9.2f) }, Maintenance = F(2.0f, 1.4f, 0.9f), Work = WResource },
            new BuildDef { Id = "fishery", Name = "渔场", Sprite = "fia_bld_fishery", Loc = BuildLoc.Village, Cat = BuildCat.Resource, CanBePrivate = true,
                Outputs = { A(FeudalGoods.Fish, 6f, 9f, 13.8f) }, Maintenance = F(2.0f, 1.4f, 0.9f), Work = WResource },
            new BuildDef { Id = "herb_gatherer", Name = "草药采集地", Sprite = "fia_bld_herbs", Loc = BuildLoc.Village, Cat = BuildCat.Resource, CanBePrivate = true,
                Outputs = { A(FeudalGoods.Herbs, 1.5f, 2.25f, 3.45f) }, Maintenance = F(2.0f, 1.4f, 0.9f), Work = WResource },

            // ================= 村庄 · 加工 / 后勤 / 民生 / 军事 =================
            new BuildDef { Id = "weavery_shop", Name = "纺织作坊", Sprite = "fia_bld_weavery", Loc = BuildLoc.Village, Cat = BuildCat.Industry, CanBePrivate = true,
                Inputs = { A(FeudalGoods.Wool + "|" + FeudalGoods.Cotton + "|" + FeudalGoods.Flax, 3f, 3f, 3f) },
                Outputs = { A(FeudalGoods.Linen, 0.5f, 0.75f, 1.15f) }, Maintenance = F(2.0f, 1.4f, 0.9f), Work = WIndustry },
            new BuildDef { Id = "tannery_shop", Name = "皮革作坊", Sprite = "fia_bld_tannery", Loc = BuildLoc.Village, Cat = BuildCat.Industry, CanBePrivate = true,
                Inputs = { A(FeudalGoods.Hides, 2f, 2f, 2f) },
                Outputs = { A(FeudalGoods.Leather, 0.7f, 1.05f, 1.61f) }, Maintenance = F(2.0f, 1.4f, 0.9f), Work = WIndustry },
            new BuildDef { Id = "brewery_shop", Name = "酿酒厂", Sprite = "fia_bld_brewery", Loc = BuildLoc.Village, Cat = BuildCat.Industry, CanBePrivate = true,
                Inputs = { A(FeudalGoods.Grain + "|" + FeudalGoods.Grape, 4f, 4f, 4f) },
                Outputs = { A(FeudalGoods.Beer, 2f, 3f, 4.6f), A(FeudalGoods.Wine, 0.5f, 0.75f, 1.15f) }, Maintenance = F(2.5f, 1.75f, 1.1f), Work = WIndustry },
            new BuildDef { Id = "granary", Name = "粮仓", Sprite = "fia_bld_granary", Loc = BuildLoc.Village, Cat = BuildCat.Logistics, CanBePrivate = true,
                Outputs = { A(EffStorage, 100f, 150f, 230f) }, Maintenance = F(1.0f, 0.7f, 0.45f), Work = WAdmin },
            new BuildDef { Id = "temple_village", Name = "神庙", Sprite = "fia_bld_temple", Loc = BuildLoc.Village, Cat = BuildCat.Living, CanBePrivate = true,
                Outputs = { A(EffLoyalty, 3f, 5f, 8f) }, Maintenance = F(1.0f, 0.7f, 0.45f), Work = WAdmin },
            new BuildDef { Id = "militia_camp", Name = "民兵营", Sprite = "fia_bld_militia", Loc = BuildLoc.Village, Cat = BuildCat.Military, CanBePrivate = false,
                Outputs = { A(EffMilitia, 20f, 30f, 46f) }, Maintenance = F(1.5f, 1.05f, 0.68f), Work = WMilitary },

            // ================= 城镇 · 加工类 =================
            new BuildDef { Id = "ironworks", Name = "炼铁厂", Sprite = "fia_bld_ironworks", Loc = BuildLoc.Town, Cat = BuildCat.Industry, CanBePrivate = true,
                Inputs = { A(FeudalGoods.IronOre, 2f, 2f, 2f), A(FeudalGoods.Charcoal, 1f, 1f, 1f) },
                Outputs = { A(FeudalGoods.Iron, 3.5f, 5.25f, 8.05f) }, Maintenance = F(3.0f, 2.1f, 1.35f), Work = WIndustry },
            new BuildDef { Id = "toolshop", Name = "工具作坊", Sprite = "fia_bld_toolshop", Loc = BuildLoc.Town, Cat = BuildCat.Industry, CanBePrivate = true,
                Inputs = { A(FeudalGoods.Iron, 2f, 2f, 2f), A(FeudalGoods.Hardwood, 1f, 1f, 1f) },
                Outputs = { A(FeudalGoods.Tools, 0.8f, 1.2f, 1.84f) }, Maintenance = F(3.0f, 2.1f, 1.35f), Work = WIndustry },
            new BuildDef { Id = "weaponsmith", Name = "武器作坊", Sprite = "fia_bld_weaponsmith", Loc = BuildLoc.Town, Cat = BuildCat.Industry, CanBePrivate = true,
                Inputs = { A(FeudalGoods.Iron, 2f, 2f, 2f), A(FeudalGoods.Hardwood, 1f, 1f, 1f) },
                Outputs = { A(FeudalGoods.Weapons, 1.8f, 2.7f, 4.14f) }, Maintenance = F(3.5f, 2.45f, 1.6f), Work = WIndustry },
            new BuildDef { Id = "armorsmith", Name = "盔甲作坊", Sprite = "fia_bld_armorsmith", Loc = BuildLoc.Town, Cat = BuildCat.Industry, CanBePrivate = true,
                Inputs = { A(FeudalGoods.Iron, 1f, 1f, 1f), A(FeudalGoods.Leather, 0.5f, 0.5f, 0.5f) },
                Outputs = { A(FeudalGoods.Armor, 1.2f, 1.8f, 2.76f) }, Maintenance = F(3.5f, 2.45f, 1.6f), Work = WIndustry },
            new BuildDef { Id = "weavery", Name = "纺织厂", Sprite = "fia_bld_mill", Loc = BuildLoc.Town, Cat = BuildCat.Industry, CanBePrivate = true,
                Inputs = { A(FeudalGoods.Wool + "|" + FeudalGoods.Cotton, 3f, 3f, 3f) },
                Outputs = { A(FeudalGoods.Linen, 0.5f, 0.75f, 1.15f) }, Maintenance = F(2.5f, 1.75f, 1.1f), Work = WIndustry },
            new BuildDef { Id = "tannery", Name = "皮革厂", Sprite = "fia_bld_tannery", Loc = BuildLoc.Town, Cat = BuildCat.Industry, CanBePrivate = true,
                Inputs = { A(FeudalGoods.Hides, 2f, 2f, 2f) },
                Outputs = { A(FeudalGoods.Leather, 0.7f, 1.05f, 1.61f) }, Maintenance = F(2.5f, 1.75f, 1.1f), Work = WIndustry },
            new BuildDef { Id = "brewery", Name = "酿酒厂", Sprite = "fia_bld_brewery", Loc = BuildLoc.Town, Cat = BuildCat.Industry, CanBePrivate = true,
                Inputs = { A(FeudalGoods.Grain + "|" + FeudalGoods.Grape, 4f, 4f, 4f) },
                Outputs = { A(FeudalGoods.Beer, 2f, 3f, 4.6f), A(FeudalGoods.Wine, 0.5f, 0.75f, 1.15f) }, Maintenance = F(2.5f, 1.75f, 1.1f), Work = WIndustry },
            new BuildDef { Id = "pottery_works", Name = "陶器厂", Sprite = "fia_bld_pottery", Loc = BuildLoc.Town, Cat = BuildCat.Industry, CanBePrivate = true,
                Inputs = { A(FeudalGoods.Clay, 3f, 3f, 3f) },
                Outputs = { A(FeudalGoods.Pottery, 0.5f, 0.75f, 1.15f) }, Maintenance = F(2.0f, 1.4f, 0.9f), Work = WIndustry },

            // ================= 城镇 / 城堡 · 军事 =================
            new BuildDef { Id = "barracks", Name = "军营", Sprite = "fia_bld_barracks", Loc = BuildLoc.Town | BuildLoc.Castle, Cat = BuildCat.Military, CanBePrivate = false,
                Outputs = { A(EffGarrison, 20f, 35f, 55f), A(EffRecruit, 5f, 8f, 12f) }, Maintenance = F(2.0f, 1.4f, 0.9f), Work = WMilitary },
            new BuildDef { Id = "armory", Name = "军械库", Sprite = "fia_bld_armory", Loc = BuildLoc.Town | BuildLoc.Castle, Cat = BuildCat.Military, CanBePrivate = false,
                Outputs = { A(EffSupply, 50f, 90f, 140f) }, Maintenance = F(2.0f, 1.4f, 0.9f), Work = WMilitary },
            new BuildDef { Id = "stables", Name = "马厩", Sprite = "fia_bld_stables", Loc = BuildLoc.Town | BuildLoc.Castle, Cat = BuildCat.Military, CanBePrivate = false,
                Outputs = { A(EffCavalry, 10f, 15f, 23f) }, Maintenance = F(2.0f, 1.4f, 0.9f), Work = WMilitary },
            new BuildDef { Id = "walls", Name = "城墙加固", Sprite = "fia_bld_walls", Loc = BuildLoc.Town | BuildLoc.Castle, Cat = BuildCat.Military, CanBePrivate = false,
                Outputs = { A(EffDefense, 10f, 15f, 23f) }, Maintenance = F(2.5f, 1.75f, 1.1f), Work = WMilitary },
            new BuildDef { Id = "watchtower", Name = "瞭望塔", Sprite = "fia_bld_watchtower", Loc = BuildLoc.Town | BuildLoc.Castle, Cat = BuildCat.Military, CanBePrivate = false,
                Outputs = { A(EffVision, 15f, 23f, 35f) }, Maintenance = F(1.5f, 1.05f, 0.68f), Work = WMilitary },
            new BuildDef { Id = "patrol", Name = "巡逻营", Sprite = "fia_bld_patrol", Loc = BuildLoc.Town | BuildLoc.Castle, Cat = BuildCat.Military, CanBePrivate = false,
                Outputs = { A(EffSecurity, 3f, 5f, 8f), A(EffBandit, -5f, -8f, -12f) }, Maintenance = F(1.5f, 1.05f, 0.68f), Work = WMilitary },
            new BuildDef { Id = "supply", Name = "补给站", Sprite = "fia_bld_supply", Loc = BuildLoc.Town | BuildLoc.Castle, Cat = BuildCat.Military, CanBePrivate = false,
                Outputs = { A(EffSupply, 40f, 60f, 92f) }, Maintenance = F(1.5f, 1.05f, 0.68f), Work = WMilitary },

            // ================= 城镇 / 城堡 · 行政 / 贸易 / 后勤 / 民生 =================
            new BuildDef { Id = "builder", Name = "建造部门", Sprite = "fia_bld_builder", Loc = BuildLoc.Town | BuildLoc.Castle, Cat = BuildCat.Admin, CanBePrivate = false,
                Outputs = { A(EffConstruction, 5f, 9f, 14f) }, Maintenance = F(0f, 0f, 0f), Work = WAdmin,
                InputsByMode = new[]
                {
                    new List<Amount> { AM(FeudalGoods.Hardwood, 3f) },
                    new List<Amount> { AM(FeudalGoods.Hardwood, 3f), AM(FeudalGoods.Stone, 2f) },
                    new List<Amount> { AM(FeudalGoods.Hardwood, 3f), AM(FeudalGoods.Stone, 2f), AM(FeudalGoods.Iron, 1f) }
                } },
            new BuildDef { Id = "warehouse", Name = "仓库", Sprite = "fia_bld_warehouse", Loc = BuildLoc.Town | BuildLoc.Castle, Cat = BuildCat.Logistics, CanBePrivate = true,
                Outputs = { A(EffStorage, 200f, 350f, 550f) }, Maintenance = F(1.5f, 1.05f, 0.68f), Work = WAdmin },
            new BuildDef { Id = "market", Name = "市场", Sprite = "fia_bld_market", Loc = BuildLoc.Town, Cat = BuildCat.Trade, CanBePrivate = true,
                Outputs = { A(EffTradeCap, 20f, 35f, 55f), A(EffPrice, -5f, -8f, -12f) }, Maintenance = F(2.0f, 1.4f, 0.9f), Work = WAdmin },
            new BuildDef { Id = "trade_post", Name = "贸易站", Sprite = "fia_bld_tradepost", Loc = BuildLoc.Town, Cat = BuildCat.Trade, CanBePrivate = true,
                Outputs = { A(EffImportCap, 30f, 45f, 69f) }, Maintenance = F(2.0f, 1.4f, 0.9f), Work = WAdmin },
            new BuildDef { Id = "caravan_post", Name = "驿站", Sprite = "fia_bld_courier", Loc = BuildLoc.Town, Cat = BuildCat.Logistics, CanBePrivate = true,
                Outputs = { A(EffCaravanSpeed, 10f, 15f, 23f) }, Maintenance = F(1.5f, 1.05f, 0.68f), Work = WAdmin },
            new BuildDef { Id = "tax_office", Name = "税务局", Sprite = "fia_bld_tax", Loc = BuildLoc.Town | BuildLoc.Castle, Cat = BuildCat.Admin, CanBePrivate = false,
                Outputs = { A(EffTax, 5f, 8f, 12f) }, Maintenance = F(1.5f, 1.05f, 0.68f), Work = WAdmin },
            new BuildDef { Id = "town_hall", Name = "市政厅", Sprite = "fia_bld_townhall", Loc = BuildLoc.Town, Cat = BuildCat.Admin, CanBePrivate = false,
                Outputs = { A(EffProsperity, 0.5f, 0.8f, 1.2f) }, Maintenance = F(2.0f, 1.4f, 0.9f), Work = WAdmin },
            new BuildDef { Id = "bank", Name = "银行", Sprite = "fia_bld_bank", Loc = BuildLoc.Town, Cat = BuildCat.Admin, CanBePrivate = false,
                Outputs = { A(EffInterest, -1f, -2f, -3f) }, Maintenance = F(3.0f, 2.1f, 1.35f), Work = WAdmin },
            // v4.0 铸币厂(文档 20.3): 王室专属; 与银矿配合产生铸币收入
            new BuildDef { Id = "mint", Name = "铸币厂", Sprite = "fia_bld_bank", Loc = BuildLoc.Town, Cat = BuildCat.Admin, CanBePrivate = false,
                Outputs = { A(EffTax, 0f, 0f, 0f) }, Maintenance = F(2.0f, 1.4f, 0.9f), Work = WAdmin },
            new BuildDef { Id = "university", Name = "大学", Sprite = "fia_bld_university", Loc = BuildLoc.Town, Cat = BuildCat.Admin, CanBePrivate = false,
                Outputs = { A(EffFocusSpeed, 2f, 3f, 5f) }, Maintenance = F(3.0f, 2.1f, 1.35f), Work = WAdmin },
            new BuildDef { Id = "hospital", Name = "医院", Sprite = "fia_bld_hospital", Loc = BuildLoc.Town, Cat = BuildCat.Living, CanBePrivate = true,
                Inputs = { A(FeudalGoods.Herbs, 1f, 1.5f, 2.3f) },
                Outputs = { A(EffPopulation, 1f, 1.5f, 2.3f) }, Maintenance = F(2.0f, 1.4f, 0.9f), Work = WAdmin },
            new BuildDef { Id = "temple", Name = "神庙", Sprite = "fia_bld_temple", Loc = BuildLoc.Town | BuildLoc.Castle, Cat = BuildCat.Living, CanBePrivate = true,
                Outputs = { A(EffLoyalty, 5f, 8f, 12f) }, Maintenance = F(1.5f, 1.05f, 0.67f), Work = WAdmin },
            new BuildDef { Id = "well", Name = "水井", Sprite = "fia_bld_well", Loc = BuildLoc.Town, Cat = BuildCat.Living, CanBePrivate = true,
                Outputs = { A(EffProsperity, 0.3f, 0.45f, 0.7f) }, Maintenance = F(1.0f, 0.7f, 0.45f), Work = WAdmin }
        };

        private static readonly Dictionary<string, BuildDef> Index = BuildIndex();

        private static Dictionary<string, BuildDef> BuildIndex()
        {
            var d = new Dictionary<string, BuildDef>();
            foreach (var b in All) if (!d.ContainsKey(b.Id)) d.Add(b.Id, b);
            return d;
        }

        internal static BuildDef Get(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            BuildDef b;
            return Index.TryGetValue(id, out b) ? b : null;
        }

        // 该定居点能否建这个建筑
        internal static bool AllowedAt(BuildDef def, bool isVillage, bool isCastle, bool isTown)
        {
            if (def == null) return false;
            if (isVillage) return (def.Loc & BuildLoc.Village) != 0;
            if (isCastle) return (def.Loc & BuildLoc.Castle) != 0;
            if (isTown) return (def.Loc & BuildLoc.Town) != 0;
            return false;
        }

        // 城堡建筑一律国有; 其余按定义
        internal static bool CanBePrivate(BuildDef def, bool isCastle)
        {
            if (def == null || isCastle) return false;
            return def.CanBePrivate;
        }

        internal static bool IsVillageDef(BuildDef def) { return def != null && (def.Loc & BuildLoc.Village) != 0; }

        // ---- 全局模式规则(2.2 / 12.3) ----
        // 石档: 附加石料 1/日; 铁档: 附加石料 1 + 铁 0.6/日 (可被单建筑覆盖)
        internal static float ExtraStone(BuildDef def, BuildMode m)
        {
            if (def != null && def.ExtraStoneOverride != null) return def.ExtraStoneOverride[(int)m];
            return m == BuildMode.Wood ? 0f : 1f;
        }

        internal static float ExtraIron(BuildDef def, BuildMode m)
        {
            if (def != null && def.ExtraIronOverride != null) return def.ExtraIronOverride[(int)m];
            return m == BuildMode.Iron ? 0.6f : 0f;
        }

        // ---- 一次性建造材料(12.4) ----
        internal static void BuildMaterials(BuildMode m, out float hardwood, out float stone, out float iron)
        {
            switch (m)
            {
                case BuildMode.Stone: hardwood = 30f; stone = 20f; iron = 0f; break;
                case BuildMode.Iron: hardwood = 40f; stone = 30f; iron = 20f; break;
                default: hardwood = 20f; stone = 0f; iron = 0f; break;
            }
        }

        internal static int WorkHours(BuildDef def, BuildMode m)
        {
            if (def == null) return 0;
            return def.Work[(int)m];
        }

        // 输入列表(考虑建造部门的按档位输入)
        internal static List<Amount> InputsFor(BuildDef def, BuildMode m)
        {
            if (def == null) return new List<Amount>();
            if (def.InputsByMode != null) return def.InputsByMode[(int)m];
            return def.Inputs;
        }

        // "a|b|c" -> 备选商品列表
        internal static string[] Alternatives(string good)
        {
            if (string.IsNullOrEmpty(good)) return new string[0];
            return good.IndexOf('|') >= 0 ? good.Split('|') : new[] { good };
        }

        internal static string ModeName(BuildMode m)
        {
            switch (m)
            {
                case BuildMode.Stone: return "石制";
                case BuildMode.Iron: return "铁制";
                default: return "木制";
            }
        }

        internal static string ModeSprite(BuildMode m)
        {
            switch (m)
            {
                case BuildMode.Stone: return "fia_mode_stone";
                case BuildMode.Iron: return "fia_mode_iron";
                default: return "fia_mode_wood";
            }
        }

        internal static string CategoryName(BuildCat c)
        {
            switch (c)
            {
                case BuildCat.Resource: return "资源";
                case BuildCat.Industry: return "加工";
                case BuildCat.Military: return "军事";
                case BuildCat.Admin: return "行政";
                case BuildCat.Trade: return "贸易";
                case BuildCat.Logistics: return "后勤";
                default: return "民生";
            }
        }

        internal static string CategorySprite(BuildCat c)
        {
            switch (c)
            {
                case BuildCat.Resource: return "fia_cat_resource";
                case BuildCat.Industry: return "fia_cat_industry";
                case BuildCat.Military: return "fia_cat_military";
                case BuildCat.Admin: return "fia_cat_admin";
                case BuildCat.Trade: return "fia_cat_trade";
                case BuildCat.Logistics: return "fia_cat_logistics";
                default: return "fia_cat_living";
            }
        }
    }
}
