using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;

namespace FeudalInternalAffairs
{
    // 27.1 装备体系: 槽位/甲类/目录(39 件)
    internal enum EquipCat { Melee = 0, Ranged = 1, Artillery = 2, Armor = 3, Mount = 4, Support = 5 }
    internal enum EquipSlot { Main = 0, Side = 1, Armor = 2, Helmet = 3, Mount = 4, Support = 5 }
    internal enum ArmorClass { None = 0, Light = 1, Heavy = 2, Addon = 3 }

    [Flags]
    internal enum EquipTag
    {
        AnyWeapon = 0,                       // 需求专用: 任意武器(近战/远程)
        BowOrCrossbow = 1 << 22,             // 需求专用: 弓或弩
        Melee = 1 << 0,
        Ranged = 1 << 1,
        Firearm = 1 << 2,
        Rifle = 1 << 3,                      // 来复枪/步枪
        Bow = 1 << 4,
        Crossbow = 1 << 5,
        Saber = 1 << 6,
        Lance = 1 << 7,
        Carbine = 1 << 8,
        AnyHorse = 1 << 9,
        HeavyHorse = 1 << 10,
        LightArmor = 1 << 11,
        HeavyArmor = 1 << 12,
        Helmet = 1 << 13,
        Gun = 1 << 14,
        HeavyGun = 1 << 15,
        LightGun = 1 << 16,
        MachineGun = 1 << 17,
        Carriage = 1 << 18,
        Grenade = 1 << 19,
        Tool = 1 << 20,
        SupportGear = 1 << 21
    }

    // 27.1.2 装备目录(39 件, 数值照抄设计文档)
    internal class EquipDef
    {
        internal string Id = "";
        internal string Name = "";
        internal EquipCat Cat;
        internal int Tier;
        internal int Dmg, Pen, Range;            // 伤害 / 破甲 / 射程
        internal float Reload, Acc;              // 装填(发/分) / 精度
        internal int Prot, Weight;               // 防护 / 负重
        internal float Speed;                    // 速度加成(+15% = 0.15)
        internal int Impact;                     // 冲击
        internal int Price;
        internal string Materials = "";
        internal string Tech = "";               // 解锁科技(27.9 中文名)
        internal string TechId = "";             // 解锁科技 id(与军事科技树对齐)
        internal EquipTag Tags;
        internal ArmorClass ArmorClass;
        internal string Effect = "";             // 支援类数值说明(照抄)
        internal bool IsFirearm { get { return (Tags & EquipTag.Firearm) != 0; } }   // 供军械库/战斗按(层级,火器)选型
        internal bool IsGun { get { return Cat == EquipCat.Artillery; } }
    }

    internal struct UnitReq
    {
        internal EquipTag Tag;
        internal int Per100;
        internal UnitReq(EquipTag t, int n) { Tag = t; Per100 = n; }
    }

    // 27.2 兵种表(14 种, 每 100 人)
    internal class UnitDef
    {
        internal string Id = "";
        internal string Name = "";
        internal int Branch;                     // v4.236: 0 近战步兵 / 1 远程 / 2 骑兵 / 3 骑射手(按主武器定)
        internal float Training;
        internal int Morale;
        internal float Speed = 1f;               // 1.0 = 100%
        internal UnitReq[] Req;
        internal string Trait = "";
        internal string TechId = "";             // v4.241: 解锁科技 id(空 = 开局可用); 与 Research.All 对齐
    }

    // 27.12 原版兵种折算结果
    internal class VanillaApproxInfo
    {
        internal int Total;
        internal readonly int[] Men = new int[4];      // 步兵/弓手/骑兵/骑射手
        internal readonly int[] Unit = new int[4];     // 折算兵种(Units 下标, -1=无)
        internal readonly int[] Tier = new int[4];     // 装备档(按原版兵种等级)
        internal readonly string[][] Gear = new string[4][];
        internal float Training = 1f;
        internal float Fill = 1f;                      // 27.15: 原版部队满足率 100%
    }

    internal static class Equipment
    {
        // ==================== 27.1.2 装备目录(39) ====================
        // v4.241: Tech/TechId 全部改为与 World\Research.cs 军事树真实对齐(空 = 开局可用)
        internal static readonly List<EquipDef> All = new List<EquipDef>
        {
            // ---- 远程武器(10) ----
            new EquipDef { Id="hunting_bow", Name="猎弓", Cat=EquipCat.Ranged, Tier=1, Dmg=30, Pen=15, Range=3, Reload=4.0f, Acc=0.80f, Price=40, Materials="木3", Tech="", TechId="", Tags=EquipTag.Bow|EquipTag.Ranged },
            new EquipDef { Id="longbow", Name="长弓", Cat=EquipCat.Ranged, Tier=2, Dmg=45, Pen=20, Range=4, Reload=3.0f, Acc=0.80f, Price=70, Materials="木4 皮1", Tech="", TechId="", Tags=EquipTag.Bow|EquipTag.Ranged },
            new EquipDef { Id="crossbow", Name="十字弩", Cat=EquipCat.Ranged, Tier=2, Dmg=50, Pen=35, Range=3, Reload=1.5f, Acc=0.85f, Price=90, Materials="木3 铁2", Tech="", TechId="", Tags=EquipTag.Crossbow|EquipTag.Ranged },
            new EquipDef { Id="matchlock", Name="火绳枪", Cat=EquipCat.Ranged, Tier=3, Dmg=55, Pen=40, Range=4, Reload=0.8f, Acc=0.90f, Price=120, Materials="铁4 木2", Tech="常备军", TechId="standing_army", Tags=EquipTag.Firearm|EquipTag.Ranged },
            new EquipDef { Id="flintlock", Name="燧发枪", Cat=EquipCat.Ranged, Tier=3, Dmg=70, Pen=50, Range=4, Reload=1.5f, Acc=1.00f, Price=150, Materials="铁5 木2", Tech="军事操典", TechId="military_drill", Tags=EquipTag.Firearm|EquipTag.Ranged },
            new EquipDef { Id="caplock", Name="击发枪", Cat=EquipCat.Ranged, Tier=4, Dmg=80, Pen=60, Range=4, Reload=2.0f, Acc=1.02f, Price=190, Materials="铁6 木2", Tech="击发雷管", TechId="percussion_cap", Tags=EquipTag.Firearm|EquipTag.Ranged },
            new EquipDef { Id="minie_rifle", Name="米涅来复枪", Cat=EquipCat.Ranged, Tier=4, Dmg=95, Pen=75, Range=5, Reload=1.2f, Acc=1.05f, Price=240, Materials="铁7 木2", Tech="膛线", TechId="rifling", Tags=EquipTag.Firearm|EquipTag.Rifle|EquipTag.Ranged },
            new EquipDef { Id="carbine", Name="卡宾枪", Cat=EquipCat.Ranged, Tier=4, Dmg=85, Pen=55, Range=4, Reload=2.0f, Acc=1.00f, Price=210, Materials="铁6 木2", Tech="膛线", TechId="rifling", Tags=EquipTag.Firearm|EquipTag.Carbine|EquipTag.Ranged },
            new EquipDef { Id="breech_rifle", Name="后装步枪", Cat=EquipCat.Ranged, Tier=5, Dmg=110, Pen=85, Range=5, Reload=3.0f, Acc=1.10f, Price=300, Materials="铁8 木2", Tech="连发枪", TechId="repeaters", Tags=EquipTag.Firearm|EquipTag.Rifle|EquipTag.Ranged },
            new EquipDef { Id="repeater_rifle", Name="连发步枪", Cat=EquipCat.Ranged, Tier=6, Dmg=100, Pen=90, Range=5, Reload=6.0f, Acc=1.15f, Price=420, Materials="铁9 木2", Tech="栓动步枪", TechId="bolt_action", Tags=EquipTag.Firearm|EquipTag.Rifle|EquipTag.Ranged },

            // ---- 近战武器(6): 前工业冷兵器, 开局即可造 ----
            new EquipDef { Id="short_spear", Name="短矛", Cat=EquipCat.Melee, Tier=1, Dmg=25, Pen=10, Price=20, Materials="木2 铁1", Tech="", TechId="", Tags=EquipTag.Melee },
            new EquipDef { Id="long_sword", Name="长剑", Cat=EquipCat.Melee, Tier=2, Dmg=35, Pen=20, Price=45, Materials="铁3", Tech="", TechId="", Tags=EquipTag.Melee },
            new EquipDef { Id="saber", Name="马刀", Cat=EquipCat.Melee, Tier=2, Dmg=32, Pen=22, Price=50, Materials="铁3", Tech="", TechId="", Tags=EquipTag.Melee|EquipTag.Saber },
            new EquipDef { Id="twohanded_sword", Name="双手剑", Cat=EquipCat.Melee, Tier=3, Dmg=50, Pen=30, Price=80, Materials="铁5", Tech="", TechId="", Tags=EquipTag.Melee },
            new EquipDef { Id="lance", Name="骑枪", Cat=EquipCat.Melee, Tier=3, Dmg=55, Pen=40, Price=110, Materials="铁5 木1", Tech="", TechId="", Tags=EquipTag.Melee|EquipTag.Lance },
            new EquipDef { Id="halberd", Name="戟", Cat=EquipCat.Melee, Tier=4, Dmg=60, Pen=45, Price=120, Materials="铁6 木1", Tech="", TechId="", Tags=EquipTag.Melee },

            // ---- 火炮与机枪(9) ----
            new EquipDef { Id="cannon6", Name="前装 6 磅炮", Cat=EquipCat.Artillery, Tier=3, Dmg=90, Pen=60, Range=5, Reload=1.0f, Acc=1.00f, Price=500, Materials="铁14", Tech="火炮", TechId="artillery", Tags=EquipTag.Gun|EquipTag.LightGun },
            new EquipDef { Id="light_cannon", Name="轻炮", Cat=EquipCat.Artillery, Tier=4, Dmg=100, Pen=70, Range=5, Reload=1.2f, Acc=1.00f, Price=600, Materials="铁16", Tech="炮弹炮", TechId="shell_gun", Tags=EquipTag.Gun|EquipTag.LightGun },
            new EquipDef { Id="cannon12", Name="前装 12 磅炮", Cat=EquipCat.Artillery, Tier=4, Dmg=130, Pen=80, Range=5, Reload=0.8f, Acc=1.00f, Price=800, Materials="铁20", Tech="炮弹炮", TechId="shell_gun", Tags=EquipTag.Gun|EquipTag.HeavyGun },
            new EquipDef { Id="rifled_cannon", Name="线膛炮", Cat=EquipCat.Artillery, Tier=5, Dmg=170, Pen=110, Range=6, Reload=0.8f, Acc=1.05f, Price=1300, Materials="铁26", Tech="后装炮", TechId="breech_artillery", Tags=EquipTag.Gun|EquipTag.HeavyGun },
            new EquipDef { Id="congreve", Name="康格里夫火箭", Cat=EquipCat.Artillery, Tier=5, Dmg=150, Pen=90, Range=5, Reload=1.0f, Acc=0.90f, Price=1100, Materials="铁22", Tech="炮弹炮", TechId="shell_gun", Tags=EquipTag.Gun },
            new EquipDef { Id="breech_cannon", Name="后装线膛炮", Cat=EquipCat.Artillery, Tier=6, Dmg=210, Pen=140, Range=6, Reload=1.5f, Acc=1.10f, Price=1900, Materials="铁34", Tech="后装炮", TechId="breech_artillery", Tags=EquipTag.Gun|EquipTag.HeavyGun },
            new EquipDef { Id="howitzer", Name="榴弹炮", Cat=EquipCat.Artillery, Tier=6, Dmg=230, Pen=120, Range=5, Reload=1.0f, Acc=1.05f, Price=2200, Materials="铁36", Tech="战壕工事", TechId="trench_works", Tags=EquipTag.Gun|EquipTag.HeavyGun },
            new EquipDef { Id="gatling", Name="加特林", Cat=EquipCat.Artillery, Tier=5, Dmg=100, Pen=70, Range=5, Reload=8.0f, Acc=0.90f, Price=900, Materials="铁20", Tech="手摇机枪", TechId="handcranked_mg", Tags=EquipTag.MachineGun },
            new EquipDef { Id="maxim", Name="马克沁", Cat=EquipCat.Artillery, Tier=6, Dmg=130, Pen=95, Range=5, Reload=12.0f, Acc=1.00f, Price=1600, Materials="铁28", Tech="自动机枪", TechId="auto_mg", Tags=EquipTag.MachineGun },

            // ---- 护甲 / 马匹 / 支援(14) ----
            new EquipDef { Id="leather_armor", Name="皮甲", Cat=EquipCat.Armor, Tier=1, Prot=10, Weight=2, Price=30, Materials="皮3", Tech="", TechId="", Tags=EquipTag.LightArmor, ArmorClass=ArmorClass.Light },
            new EquipDef { Id="chainmail", Name="锁子甲", Cat=EquipCat.Armor, Tier=2, Prot=25, Weight=5, Price=90, Materials="铁4 皮2", Tech="", TechId="", Tags=EquipTag.LightArmor, ArmorClass=ArmorClass.Light },
            new EquipDef { Id="cuirass", Name="胸甲", Cat=EquipCat.Armor, Tier=3, Prot=40, Weight=8, Price=180, Materials="铁7", Tech="拿破仑战术", TechId="napoleonic", Tags=EquipTag.HeavyArmor, ArmorClass=ArmorClass.Heavy },
            new EquipDef { Id="half_plate", Name="半身板甲", Cat=EquipCat.Armor, Tier=4, Prot=55, Weight=12, Price=300, Materials="铁10", Tech="", TechId="", Tags=EquipTag.HeavyArmor, ArmorClass=ArmorClass.Heavy },
            new EquipDef { Id="steel_helmet", Name="钢盔", Cat=EquipCat.Armor, Tier=5, Prot=20, Weight=1, Price=60, Materials="铁3", Tech="战壕工事", TechId="trench_works", Tags=EquipTag.Helmet, ArmorClass=ArmorClass.Addon },
            new EquipDef { Id="nag", Name="驽马", Cat=EquipCat.Mount, Tier=1, Speed=0.15f, Impact=5, Price=60, Materials="粮2", Tech="", TechId="", Tags=EquipTag.AnyHorse },
            new EquipDef { Id="warhorse", Name="战马", Cat=EquipCat.Mount, Tier=3, Speed=0.25f, Impact=15, Price=150, Materials="粮4", Tech="", TechId="", Tags=EquipTag.AnyHorse },
            new EquipDef { Id="heavy_warhorse", Name="重装战马", Cat=EquipCat.Mount, Tier=4, Speed=0.20f, Impact=25, Price=260, Materials="粮6 铁1", Tech="拿破仑战术", TechId="napoleonic", Tags=EquipTag.AnyHorse|EquipTag.HeavyHorse },
            new EquipDef { Id="gun_carriage", Name="炮组器材", Cat=EquipCat.Support, Tier=3, Price=80, Materials="铁4 木2", Tech="火炮", TechId="artillery", Tags=EquipTag.Carriage, Effect="每门炮 1 套" },
            new EquipDef { Id="tools", Name="工具", Cat=EquipCat.Support, Tier=3, Price=50, Materials="铁3 木2", Tech="野战工事", TechId="field_works", Tags=EquipTag.Tool, Effect="工兵必备" },
            new EquipDef { Id="grenade", Name="手榴弹", Cat=EquipCat.Support, Tier=4, Price=40, Materials="铁2 皮1", Tech="拿破仑战术", TechId="napoleonic", Tags=EquipTag.Grenade, Effect="掷弹兵 20 件/百人" },
            new EquipDef { Id="ammo_kit", Name="弹药组", Cat=EquipCat.Support, Tier=3, Price=30, Materials="铁1 皮1", Tech="后勤学", TechId="logistics", Tags=EquipTag.SupportGear, Effect="机枪 1 套/挺" },
            new EquipDef { Id="field_hospital", Name="野战医院", Cat=EquipCat.Support, Tier=4, Price=400, Materials="皮8 布6", Tech="现代护理", TechId="modern_nursing", Tags=EquipTag.SupportGear, Effect="舰队/军团伤员回流 +5%" },
            new EquipDef { Id="telegraph", Name="电报机", Cat=EquipCat.Support, Tier=5, Price=600, Materials="铁12", Tech="电报", TechId="electric_telegraph", Tags=EquipTag.SupportGear, Effect="动员 +15% / 组织度 +5" }
        };

        private static readonly Dictionary<string, EquipDef> Index = BuildIndex();

        // 军团支援槽(DefLegion.Supports[3]): 工具 / 野战医院 / 电报机
        internal static readonly string[] SupportIds = { "tools", "field_hospital", "telegraph" };

        // ==================== 27.2 兵种表(14) ====================
        internal const int UMilita = 0, ULine = 1, ULightInf = 2, UGrenadier = 3, UDragoon = 4,
            UHussar = 5, UCuirassier = 6, UArcher = 7, UCrossbow = 8, UHorseArcher = 9,
            UFieldArtillery = 10, USiegeArtillery = 11, UHorseArtillery = 12, UEngineer = 13;

        // v4.236: Branch = 该兵种在分阶段战斗里算哪一支(按主武器定, 不是按原版兵种旗标)
        //   火器/来复枪/弓弩 -> 远程(1); 马+火器 -> 骑射(3); 冷兵器马队 -> 骑兵(2); 其余 -> 近战步兵(0)
        // v4.241: TechId = 兵种解锁科技(空 = 开局可用); 军制类科技链 常备军 -> 火炮/枪械制造/义务兵役 -> ...
        internal static readonly List<UnitDef> Units = new List<UnitDef>
        {
            new UnitDef { Id="militia", Name="民兵", Branch=0, Training=0.85f, Morale=70, Speed=1.00f, Trait="便宜、士气与训练低", TechId="", Req=new[]{ new UnitReq(EquipTag.AnyWeapon,100) } },
            new UnitDef { Id="line_infantry", Name="线列步兵", Branch=1, Training=1.00f, Morale=90, Speed=1.00f, Trait="齐射纪律", TechId="line_infantry", Req=new[]{ new UnitReq(EquipTag.Firearm,100), new UnitReq(EquipTag.LightArmor,50) } },
            new UnitDef { Id="light_infantry", Name="轻步兵", Branch=1, Training=1.05f, Morale=95, Speed=1.05f, Trait="地形适应（惩罚减半）", TechId="rifling", Req=new[]{ new UnitReq(EquipTag.Rifle,100), new UnitReq(EquipTag.LightArmor,50) } },
            new UnitDef { Id="grenadier", Name="掷弹兵", Branch=0, Training=1.15f, Morale=110, Speed=0.95f, Trait="攻坚 +25%、近战 +20%", TechId="napoleonic", Req=new[]{ new UnitReq(EquipTag.Firearm,100), new UnitReq(EquipTag.Grenade,20), new UnitReq(EquipTag.HeavyArmor,100) } },
            new UnitDef { Id="dragoon", Name="龙骑兵", Branch=3, Training=1.00f, Morale=95, Speed=1.60f, Trait="可骑可步、机动射击", TechId="rifling", Req=new[]{ new UnitReq(EquipTag.Carbine,100), new UnitReq(EquipTag.Saber,100), new UnitReq(EquipTag.AnyHorse,100), new UnitReq(EquipTag.LightArmor,50) } },
            new UnitDef { Id="hussar", Name="骠骑兵", Branch=2, Training=1.05f, Morale=100, Speed=1.85f, Trait="追击/侦察，缴获 +20%", TechId="", Req=new[]{ new UnitReq(EquipTag.Saber,100), new UnitReq(EquipTag.AnyHorse,100) } },
            new UnitDef { Id="cuirassier", Name="胸甲骑兵", Branch=2, Training=1.10f, Morale=105, Speed=1.50f, Trait="冲击 +40%", TechId="napoleonic", Req=new[]{ new UnitReq(EquipTag.Lance,100), new UnitReq(EquipTag.Saber,100), new UnitReq(EquipTag.HeavyHorse,100), new UnitReq(EquipTag.HeavyArmor,100) } },
            new UnitDef { Id="archer", Name="弓兵", Branch=1, Training=0.95f, Morale=85, Speed=1.00f, Trait="早期远程", TechId="", Req=new[]{ new UnitReq(EquipTag.Bow,100), new UnitReq(EquipTag.LightArmor,50) } },
            new UnitDef { Id="crossbowman", Name="弩兵", Branch=1, Training=0.95f, Morale=85, Speed=0.95f, Trait="破甲高、射速低", TechId="", Req=new[]{ new UnitReq(EquipTag.Crossbow,100), new UnitReq(EquipTag.LightArmor,50) } },
            new UnitDef { Id="horse_archer", Name="骑射手", Branch=3, Training=0.95f, Morale=90, Speed=1.70f, Trait="马上射击、扰袭", TechId="", Req=new[]{ new UnitReq(EquipTag.BowOrCrossbow,100), new UnitReq(EquipTag.AnyHorse,100), new UnitReq(EquipTag.LightArmor,50) } },
            new UnitDef { Id="field_artillery", Name="野战炮兵", Branch=0, Training=1.10f, Morale=95, Speed=0.60f, Trait="参战炮 4 门", TechId="artillery", Req=new[]{ new UnitReq(EquipTag.Gun,20), new UnitReq(EquipTag.Carriage,4) } },
            new UnitDef { Id="siege_artillery", Name="攻城炮兵", Branch=0, Training=1.10f, Morale=95, Speed=0.40f, Trait="参战炮 2 门、攻城 +50%", TechId="shell_gun", Req=new[]{ new UnitReq(EquipTag.HeavyGun,10), new UnitReq(EquipTag.Carriage,2) } },
            new UnitDef { Id="horse_artillery", Name="骑炮兵", Branch=0, Training=1.10f, Morale=95, Speed=1.20f, Trait="参战炮 3 门", TechId="artillery", Req=new[]{ new UnitReq(EquipTag.LightGun,15), new UnitReq(EquipTag.Carriage,3), new UnitReq(EquipTag.AnyHorse,15) } },
            new UnitDef { Id="engineer", Name="工兵", Branch=0, Training=1.00f, Morale=95, Speed=0.90f, Trait="攻城/筑垒/修桥", TechId="field_works", Req=new[]{ new UnitReq(EquipTag.Tool,50), new UnitReq(EquipTag.SupportGear,30) } }
        };

        // 兵种解锁科技 id(空 = 开局可用); 越界/未知兵种返回空
        internal static string GateOf(int unitIdx)
        {
            try
            {
                var u = Unit(unitIdx);
                return u != null && u.TechId != null ? u.TechId : "";
            }
            catch { return ""; }
        }

        internal static string GateOf(string unitId)
        {
            try
            {
                var u = UnitOf(unitId);
                return u != null && u.TechId != null ? u.TechId : "";
            }
            catch { return ""; }
        }

        // 兵种所属分支(0 近战 / 1 远程 / 2 骑兵 / 3 骑射); 越界回 0
        internal static int BranchOf(int unitIdx)
        {
            try
            {
                var u = Unit(unitIdx);
                if (u == null) return 0;
                int b = u.Branch;
                return (b >= 0 && b <= 3) ? b : 0;
            }
            catch { return 0; }
        }

        private static Dictionary<string, EquipDef> BuildIndex()
        {
            var d = new Dictionary<string, EquipDef>();
            for (int i = 0; i < All.Count; i++)
            {
                var e = All[i];
                if (e != null && !string.IsNullOrEmpty(e.Id)) d[e.Id] = e;
            }
            return d;
        }

        // ==================== 查询 API ====================
        internal static EquipDef Get(string id)
        {
            try
            {
                EquipDef e;
                return (!string.IsNullOrEmpty(id) && Index.TryGetValue(id, out e)) ? e : null;
            }
            catch { return null; }
        }

        internal static List<EquipDef> ByCat(EquipCat cat)
        {
            var r = new List<EquipDef>();
            try { for (int i = 0; i < All.Count; i++) if (All[i] != null && All[i].Cat == cat) r.Add(All[i]); } catch { }
            return r;
        }

        internal static List<EquipDef> ByTier(int tier)
        {
            var r = new List<EquipDef>();
            try { for (int i = 0; i < All.Count; i++) if (All[i] != null && All[i].Tier == tier) r.Add(All[i]); } catch { }
            return r;
        }

        internal static List<EquipDef> ByTech(string tech)
        {
            var r = new List<EquipDef>();
            try
            {
                if (string.IsNullOrEmpty(tech)) return r;
                for (int i = 0; i < All.Count; i++)
                {
                    var e = All[i];
                    if (e == null) continue;
                    if (e.Tech == tech || e.TechId == tech) r.Add(e);
                }
            }
            catch { }
            return r;
        }

        // 27.9 装备解锁(v4.241 修正): 空科技 = 开局可用; 填了科技就必须本国研究完成。
        //   旧版逻辑是"科技表里找不到该 id 就不阻塞", 而 38/39 件装备写的是不存在的 id,
        //   于是全部装备开局即解锁、军事科技树对装备毫无影响(电报机是唯一真门控)。
        internal static bool IsUnlocked(EquipDef e)
        {
            try
            {
                if (e == null) return false;
                string techId = e.TechId ?? "";
                string techName = e.Tech ?? "";
                if (techId.Length == 0 && techName.Length == 0) return true;
                if (techId.Length > 0 && Research.IsDone(techId)) return true;
                if (techId.Length > 0 && Research.Find(techId) == null) return false;   // 未知 id: 不放行(启动日志点名)
                if (techName.Length > 0)
                {
                    var t = Research.ByName(techName);
                    if (t != null) return Research.IsDone(t.Id);
                }
                return false;
            }
            catch { return false; }
        }

        internal static bool IsUnlocked(string id) { return IsUnlocked(Get(id)); }

        // v4.241: 附加件(钢盔) —— 解锁后按需配发, 但不计入满足率分母(27.1.1 "附加件、不占槽、可叠加")
        internal static bool IsOptional(string id)
        {
            var e = Get(id);
            return e != null && (e.Tags & EquipTag.Helmet) != 0;
        }

        internal static List<EquipDef> Unlocked()
        {
            var r = new List<EquipDef>();
            try { for (int i = 0; i < All.Count; i++) if (IsUnlocked(All[i])) r.Add(All[i]); } catch { }
            return r;
        }

        // v4.241 启动自检: 装备/兵种填的科技 id 必须在科技表里存在; 返回 "型号[科技id]" 列表(空串 = 全对)
        internal static string Validate()
        {
            var bad = new System.Text.StringBuilder();
            try
            {
                for (int i = 0; i < All.Count; i++)
                {
                    var e = All[i];
                    if (e == null || string.IsNullOrEmpty(e.TechId)) continue;
                    if (Research.Find(e.TechId) != null) continue;
                    if (bad.Length > 0) bad.Append(", ");
                    bad.Append(e.Name).Append('[').Append(e.TechId).Append(']');
                }
                for (int i = 0; i < Units.Count; i++)
                {
                    var u = Units[i];
                    if (u == null || string.IsNullOrEmpty(u.TechId)) continue;
                    if (Research.Find(u.TechId) != null) continue;
                    if (bad.Length > 0) bad.Append(", ");
                    bad.Append(u.Name).Append('[').Append(u.TechId).Append(']');
                }
            }
            catch { }
            return bad.ToString();
        }

        // 某科技解锁的装备 / 兵种名(科技页详情显示; 空 = 该科技不解锁装备)
        internal static string UnlockedByTech(string techId)
        {
            if (string.IsNullOrEmpty(techId)) return "";
            var sb = new System.Text.StringBuilder();
            try
            {
                for (int i = 0; i < Units.Count; i++)
                {
                    var u = Units[i];
                    if (u == null || u.TechId != techId) continue;
                    if (sb.Length > 0) sb.Append(" · ");
                    sb.Append(u.Name);
                }
                for (int i = 0; i < All.Count; i++)
                {
                    var e = All[i];
                    if (e == null || e.TechId != techId) continue;
                    if (sb.Length > 0) sb.Append(" · ");
                    sb.Append(e.Name).Append("(T").Append(e.Tier).Append(')');
                }
            }
            catch { }
            return sb.ToString();
        }

        internal static UnitDef Unit(int idx)
        {
            try { return (idx >= 0 && idx < Units.Count) ? Units[idx] : null; } catch { return null; }
        }

        internal static UnitDef UnitOf(string unitId)
        {
            try
            {
                if (string.IsNullOrEmpty(unitId)) return null;
                for (int i = 0; i < Units.Count; i++) if (Units[i] != null && Units[i].Id == unitId) return Units[i];
            }
            catch { }
            return null;
        }

        // 27.12 同款等级折算: 分支(步/弓/骑/骑射) + 装备档 -> 兵种
        internal static int UnitOfBranch(int branch, int tier)
        {
            if (tier < 1) tier = 1;
            if (tier > 6) tier = 6;
            switch (branch)
            {
                case 1:
                    if (tier <= 2) return UArcher;
                    if (tier <= 4) return UCrossbow;
                    return ULine;                       // T5~T6 火绳枪/燧发枪兵
                case 2:
                    if (tier <= 2) return UHussar;
                    return UCuirassier;                 // T5~T6 重装骑兵
                case 3:
                    return UHorseArcher;
                default:
                    if (tier <= 2) return UMilita;
                    if (tier <= 4) return ULine;
                    return UGrenadier;
            }
        }

        // ==================== 槽位 / 甲类校验(27.1.1 / 27.1.4) ====================
        internal static EquipSlot SlotOf(EquipDef e)
        {
            if (e == null) return EquipSlot.Support;
            if (e.Cat == EquipCat.Armor && e.ArmorClass == ArmorClass.Addon) return EquipSlot.Helmet;
            switch (e.Cat)
            {
                case EquipCat.Melee: case EquipCat.Ranged: case EquipCat.Artillery: return EquipSlot.Main;
                case EquipCat.Armor: return EquipSlot.Armor;
                case EquipCat.Mount: return EquipSlot.Mount;
                default: return EquipSlot.Support;
            }
        }

        internal static EquipSlot SlotOf(string id) { return SlotOf(Get(id)); }

        internal static ArmorClass ArmorClassOf(string id)
        {
            var e = Get(id);
            return e != null ? e.ArmorClass : ArmorClass.None;
        }

        internal static bool HasTag(EquipDef e, EquipTag tag)
        {
            if (e == null) return false;
            return (e.Tags & tag) != 0;
        }

        internal static bool MatchesTag(EquipDef e, EquipTag tag)
        {
            if (e == null) return false;
            if (tag == EquipTag.AnyWeapon) return e.Cat == EquipCat.Melee || e.Cat == EquipCat.Ranged;
            if (tag == EquipTag.BowOrCrossbow) return (e.Tags & (EquipTag.Bow | EquipTag.Crossbow)) != 0;
            return (e.Tags & tag) != 0;
        }

        internal static bool MatchesTag(string id, EquipTag tag) { return MatchesTag(Get(id), tag); }

        // 副武器槽(27.1.1): 掷弹兵手榴弹、龙骑兵/胸甲骑兵马刀
        internal static bool CanSide(EquipDef e)
        {
            if (e == null) return false;
            return (e.Tags & (EquipTag.Grenade | EquipTag.Saber)) != 0;
        }

        internal static int MinArmorClassOf(int unitIdx)
        {
            var u = Unit(unitIdx);
            if (u == null || u.Req == null) return 0;
            int c = 0;
            for (int i = 0; i < u.Req.Length; i++)
            {
                if (u.Req[i].Tag == EquipTag.LightArmor) c = Math.Max(c, 1);
                if (u.Req[i].Tag == EquipTag.HeavyArmor) c = Math.Max(c, 2);
            }
            return c;
        }

        internal static bool UnitNeedsMount(int unitIdx, out bool heavyOnly)
        {
            heavyOnly = false;
            var u = Unit(unitIdx);
            if (u == null || u.Req == null) return false;
            bool need = false;
            for (int i = 0; i < u.Req.Length; i++)
            {
                if (u.Req[i].Tag == EquipTag.AnyHorse) need = true;
                if (u.Req[i].Tag == EquipTag.HeavyHorse) { need = true; heavyOnly = true; }
            }
            return need;
        }

        // 按兵种槽位校验(返回 "" 或错误说明)
        internal static string ValidateLoadout(int unitIdx, string mainId, string sideId, string armorId, string helmetId, string mountId)
        {
            try
            {
                var u = Unit(unitIdx);
                if (u == null) return "未知兵种";
                EquipTag weaponReq = EquipTag.AnyWeapon;
                bool hasWeaponReq = false;
                bool sideReq = false, sideGrenadeReq = false;
                if (u.Req != null)
                {
                    for (int i = 0; i < u.Req.Length; i++)
                    {
                        var t = u.Req[i].Tag;
                        if (t == EquipTag.AnyWeapon || t == EquipTag.Firearm || t == EquipTag.Rifle || t == EquipTag.Bow
                            || t == EquipTag.Crossbow || t == EquipTag.BowOrCrossbow || t == EquipTag.Carbine
                            || t == EquipTag.Gun || t == EquipTag.HeavyGun || t == EquipTag.LightGun)
                        { weaponReq = t; hasWeaponReq = true; }
                        if (t == EquipTag.Grenade) { sideReq = true; sideGrenadeReq = true; }
                        else if (t == EquipTag.Saber) sideReq = true;
                    }
                }
                if (hasWeaponReq)
                {
                    if (string.IsNullOrEmpty(mainId)) return "缺少主武器";
                    if (!MatchesTag(mainId, weaponReq)) return "主武器不符合兵种要求";
                }

                if (sideReq)
                {
                    var sd = Get(sideId);
                    if (!CanSide(sd)) return "缺少/不合法副武器";
                    if (sideGrenadeReq && !MatchesTag(sd, EquipTag.Grenade)) return "副武器应为手榴弹";
                }
                else if (!string.IsNullOrEmpty(sideId) && !CanSide(Get(sideId)))
                    return "副武器槽不合法";

                int minArmor = MinArmorClassOf(unitIdx);
                if (minArmor > 0)
                {
                    var ac = Get(armorId);
                    if (ac == null) return "缺少护甲";
                    if (ac.Cat != EquipCat.Armor || ac.ArmorClass == ArmorClass.Addon) return "护甲槽不合法";
                    if ((int)ac.ArmorClass < minArmor) return "护甲低于兵种最低甲类";
                }
                else if (!string.IsNullOrEmpty(armorId))
                {
                    var ac2 = Get(armorId);
                    if (ac2 == null || ac2.Cat != EquipCat.Armor || ac2.ArmorClass == ArmorClass.Addon) return "护甲槽不合法";
                }

                if (!string.IsNullOrEmpty(helmetId))
                {
                    var h = Get(helmetId);
                    if (h == null || h.ArmorClass != ArmorClass.Addon) return "附加件槽不合法(钢盔)";
                }

                bool heavyOnly;
                bool needMount = UnitNeedsMount(unitIdx, out heavyOnly);
                if (needMount)
                {
                    var m = Get(mountId);
                    if (m == null || m.Cat != EquipCat.Mount) return "缺少马匹";
                    if (heavyOnly && !MatchesTag(m, EquipTag.HeavyHorse)) return "该兵种需重装战马";
                }
                else if (!string.IsNullOrEmpty(mountId) && SlotOf(mountId) == EquipSlot.Mount)
                    return "该兵种不需要马匹";
                return "";
            }
            catch (Exception ex) { return "校验异常: " + ex.Message; }
        }

        // ==================== 需求换算(27.2 每 100 人 -> 实际兵力, 向上取整) ====================
        // 同标签取"已解锁"的最高可用型号(档内优先); 供满足率/补装共用
        // v4.241: ①档内没有已解锁型号时回退到已解锁的最高型号(旧军团 EquipTier 是建军时定死的,
        //   研究出新枪后不该因为档位上限而算不出需求); ②**未解锁的型号一律不返回** ——
        //   否则会给军团算出一笔永远补不上的需求(国家库只发已解锁型号), 满足率被永久压低
        internal static EquipDef PickForTag(EquipTag tag, int maxTier)
        {
            try
            {
                EquipDef best = null, anyUnlocked = null;
                int bestTier = -1, anyTier = -1;
                for (int i = 0; i < All.Count; i++)
                {
                    var e = All[i];
                    if (e == null || !MatchesTag(e, tag)) continue;
                    if (!IsUnlocked(e)) continue;
                    if (anyUnlocked == null || e.Tier > anyTier) { anyUnlocked = e; anyTier = e.Tier; }
                    if (maxTier >= 1 && e.Tier > maxTier) continue;
                    if (best == null || e.Tier > bestTier) { best = e; bestTier = e.Tier; }
                }
                return best != null ? best : anyUnlocked;
            }
            catch { return null; }
        }

        internal static string PickId(EquipTag tag, int maxTier)
        {
            var e = PickForTag(tag, maxTier);
            return e != null ? e.Id : null;
        }

        internal static Dictionary<string, int> NeedOf(int[] comp, int equipTier, bool[] supports)
        {
            var need = new Dictionary<string, int>();
            try
            {
                if (comp != null)
                {
                    for (int b = 0; b < 4 && b < comp.Length; b++)
                    {
                        int men = comp[b];
                        if (men <= 0) continue;
                        var u = Unit(UnitOfBranch(b, equipTier));
                        if (u == null || u.Req == null) continue;
                        for (int r = 0; r < u.Req.Length; r++)
                        {
                            var pick = PickForTag(u.Req[r].Tag, equipTier);
                            if (pick == null) continue;
                            int q = (int)Math.Ceiling(u.Req[r].Per100 * (double)men / 100.0);
                            if (q <= 0) continue;
                            int old;
                            need.TryGetValue(pick.Id, out old);
                            need[pick.Id] = old + q;
                        }
                    }
                }
                if (supports != null)
                {
                    for (int s = 0; s < 3 && s < supports.Length; s++)
                    {
                        if (!supports[s]) continue;
                        string id = SupportIds[s];
                        if (string.IsNullOrEmpty(id)) continue;
                        int old;
                        need.TryGetValue(id, out old);
                        need[id] = old + 1;               // 军团级支援器材 1 件
                    }
                }
            }
            catch { }
            return need;
        }

        // ==================== 27.12 原版兵种折算 ====================
        internal static string[] VanillaGear(int branch, int tier)
        {
            if (tier < 1) tier = 1;
            if (tier > 6) tier = 6;
            switch (branch)
            {
                case 1:
                    if (tier <= 2) return new[] { "hunting_bow", "leather_armor" };
                    if (tier <= 4) return new[] { "crossbow", "chainmail" };
                    return new[] { (tier == 5 ? "matchlock" : "flintlock"), "chainmail" };
                case 2:
                    if (tier <= 2) return new[] { "saber", "nag", "leather_armor" };
                    if (tier <= 4) return new[] { "lance", "saber", "warhorse", "cuirass" };
                    return new[] { "lance", "saber", "heavy_warhorse", "cuirass" };
                case 3:
                    if (tier <= 2) return new[] { "hunting_bow", "nag", "leather_armor" };
                    if (tier <= 4) return new[] { "crossbow", "warhorse", "chainmail" };
                    return new[] { "crossbow", "warhorse", "chainmail" };
                default:
                    if (tier <= 2) return new[] { "short_spear", "leather_armor" };
                    if (tier <= 4) return new[] { "long_sword", "chainmail" };
                    return new[] { "twohanded_sword", "cuirass" };
            }
        }

        internal static VanillaApproxInfo VanillaApprox(MobileParty p)
        {
            var info = new VanillaApproxInfo();
            try
            {
                for (int i = 0; i < 4; i++) { info.Unit[i] = -1; info.Tier[i] = 0; }
                if (p == null || p.MemberRoster == null) return info;
                var tierSum = new long[4];
                foreach (var e in p.MemberRoster.GetTroopRoster())
                {
                    var c = e.Character;
                    if (c == null || c.IsHero || e.Number <= 0) continue;
                    int b = c.IsMounted ? (c.IsRanged ? 3 : 2) : (c.IsRanged ? 1 : 0);
                    int t = c.Tier;
                    if (t < 1) t = 1;
                    if (t > 6) t = 6;
                    info.Men[b] += e.Number;
                    tierSum[b] += (long)t * e.Number;
                    info.Total += e.Number;
                }
                float wsum = 0f;
                int wmen = 0;
                for (int b = 0; b < 4; b++)
                {
                    if (info.Men[b] <= 0) continue;
                    int avg = (int)Math.Round(tierSum[b] / (double)info.Men[b]);
                    if (avg < 1) avg = 1;
                    if (avg > 6) avg = 6;
                    info.Tier[b] = avg;
                    info.Unit[b] = UnitOfBranch(b, avg);
                    info.Gear[b] = VanillaGear(b, avg);
                    var u = Unit(info.Unit[b]);
                    float tr = u != null ? u.Training : 1f;
                    wsum += tr * info.Men[b];
                    wmen += info.Men[b];
                }
                info.Training = wmen > 0 ? wsum / wmen : 1f;
                info.Fill = 1f;   // 27.15: 旧领主军满足率 100%
            }
            catch { }
            return info;
        }

        // 27.12 折算完成
    }
}
