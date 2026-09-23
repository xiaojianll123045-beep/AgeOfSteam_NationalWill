using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;

namespace FeudalInternalAffairs
{
    // 科技定义(文档 24.10; v4.136 照 V3 官方重做: 三树 × 时代 × 前置 × 创新成本)
    internal class TechDef
    {
        internal string Id;
        internal string Name;
        internal string Effect;
        internal int Tree;        // 0 生产 / 1 军事 / 2 社会
        internal int Era;         // 1-5
        internal string Req;      // 前置科技 id(空=无)
        internal float Tax, Output, Mil, Radical, Lit, Authority;   // 效果(接既有系统)
        internal float Conscript; // v4.241: 征召上限加成(征召 +N%); 与 Effect 文案一致
        internal float Trade;     // v4.241: 贸易优势(+N 点, 接 TradeRoutes.TradeAdvantageOf)
        internal string Icon;     // v4.154: 图标 sprite 名(空=默认)
    }

    // 每国科技数据(文档 28.10 A): 已完成 + 各科技进度 + 每树当前研究队列 + 扩散来源
    internal class NationTech
    {
        internal readonly HashSet<string> Done = new HashSet<string>();
        internal readonly Dictionary<string, float> Progress = new Dictionary<string, float>();
        internal readonly string[] Current = new string[Research.TreeCount];   // 每树当前研究
        internal readonly string[] Spread = new string[Research.TreeCount];    // 每树传播中的科技(玩家)
        internal readonly Dictionary<string, string> SpreadFrom = new Dictionary<string, string>();   // techId -> 来源王国 StringId
    }

    // 科技研究(V3 官方口径): 三树并行研究; 创新 = 基础 + 大学; 
    // 投入上限 = 50 + 1.5×识字率(V3 公式, 缩放 1/10); 超上限 -> 技术传播; 成本 = 时代 + 未研究惩罚
    internal static class Research
    {
        internal const int TreeCount = 3;
        internal static readonly string[] TreeNames = { "生产", "军事", "社会" };

        // 时代基础成本(V3 官方 7500/10000/12500/15000/17500 -> 缩放 1/100; v4.241 补第 5 档)
        private static readonly float[] EraCost = { 75f, 100f, 125f, 150f, 175f };

        // v4.173: 科技表补齐到 V3 官方**全量**(生产 57 / 军事 58 / 社会 64, 共 179; 名称/时代/前置均照 V3,
        //   多前置用逗号分隔); 效果映射到本 mod 系统: Output 产出 / Tax 税收 / Mil 军事 / Radical 每日激进 / Lit 教育机会 / Authority 权威
        internal static readonly List<TechDef> All = new List<TechDef>
        {
            // ================= 生产 57(V3 Production technology) =================
            // T1
            new TechDef { Id="enclosure", Name="圈地运动", Tree=0, Era=1, Effect="农业产出 +6%", Output=0.06f },
            new TechDef { Id="manufacturies", Name="工场手工业", Tree=0, Era=1, Effect="加工产出 +6%", Output=0.06f },
            new TechDef { Id="cotton_gin", Name="轧棉机", Tree=0, Era=1, Req="manufacturies", Effect="加工产出 +5%", Output=0.05f },
            new TechDef { Id="distillation", Name="蒸馏法", Tree=0, Era=1, Req="manufacturies", Effect="税收 +4%", Tax=0.04f },
            new TechDef { Id="shaft_mining", Name="竖井采矿", Tree=0, Era=1, Req="enclosure", Effect="产出 +6%", Output=0.06f },
            new TechDef { Id="lathe", Name="车床", Tree=0, Era=1, Req="cotton_gin", Effect="加工产出 +6%", Output=0.06f },
            new TechDef { Id="prospecting", Name="勘探", Tree=0, Era=1, Req="shaft_mining", Effect="税收 +5%", Tax=0.05f },
            new TechDef { Id="steelworking", Name="钢铁冶炼", Tree=0, Era=1, Req="shaft_mining", Effect="产出 +7%, 军事 +4%", Output=0.07f, Mil=0.04f },
            new TechDef { Id="sericulture", Name="养蚕术", Tree=0, Era=1, Req="manufacturies", Effect="税收 +5%", Tax=0.05f },
            // T2
            new TechDef { Id="atmospheric_engine", Name="大气引擎", Tree=0, Era=2, Req="shaft_mining", Effect="产出 +9%", Output=0.09f },
            new TechDef { Id="bessemer", Name="贝塞麦炼钢法", Tree=0, Era=2, Req="steelworking", Effect="产出 +9%, 军事 +5%", Output=0.09f, Mil=0.05f },
            new TechDef { Id="canneries", Name="罐头业", Tree=0, Era=2, Req="lathe", Effect="激进 -0.02%/日", Radical=-0.0002f },
            new TechDef { Id="crystal_glass", Name="水晶玻璃", Tree=0, Era=2, Req="lathe", Effect="税收 +6%", Tax=0.06f },
            new TechDef { Id="fractional_dist", Name="分馏法", Tree=0, Era=2, Req="distillation", Effect="税收 +6%", Tax=0.06f },
            new TechDef { Id="intensive_agri", Name="集约农业", Tree=0, Era=2, Req="enclosure", Effect="农业产出 +10%", Output=0.10f },
            new TechDef { Id="mechanical_tools", Name="机械工具", Tree=0, Era=2, Req="lathe", Effect="加工产出 +8%", Output=0.08f },
            new TechDef { Id="railways", Name="铁路", Tree=0, Era=2, Req="atmospheric_engine", Effect="税收 +8%, 贸易优势 +5", Tax=0.08f, Trade=5f },
            new TechDef { Id="watertube_boiler", Name="水管锅炉", Tree=0, Era=2, Req="atmospheric_engine", Effect="产出 +9%", Output=0.09f },
            new TechDef { Id="baking_powder", Name="发酵粉", Tree=0, Era=2, Req="fractional_dist", Effect="激进 -0.02%/日", Radical=-0.0002f },
            // T3
            new TechDef { Id="dynamite", Name="炸药", Tree=0, Era=3, Req="nitroglycerin", Effect="军事 +8%, 产出 +5%", Mil=0.08f, Output=0.05f },
            new TechDef { Id="nitroglycerin", Name="硝化甘油", Tree=0, Era=2, Req="intensive_agri", Effect="军事 +6%, 产出 +4%", Mil=0.06f, Output=0.04f },
            new TechDef { Id="improved_fert", Name="改良肥料", Tree=0, Era=3, Req="intensive_agri", Effect="农业产出 +12%", Output=0.12f },
            new TechDef { Id="open_hearth", Name="平炉炼钢法", Tree=0, Era=3, Req="bessemer", Effect="产出 +11%, 军事 +5%", Output=0.11f, Mil=0.05f },
            new TechDef { Id="reinforced_concrete", Name="钢筋混凝土", Tree=0, Era=3, Req="bessemer", Effect="产出 +10%", Output=0.10f },
            new TechDef { Id="rotary_valve", Name="旋转阀引擎", Tree=0, Era=3, Req="watertube_boiler", Effect="加工产出 +12%", Output=0.12f },
            new TechDef { Id="rubber_mastication", Name="橡胶塑炼", Tree=0, Era=3, Req="fractional_dist", Effect="税收 +8%", Tax=0.08f },
            new TechDef { Id="shift_work", Name="轮班制", Tree=0, Era=3, Req="mechanized_workshops", Effect="产出 +10%, 激进 +0.02%/日", Output=0.10f, Radical=0.0002f },
            new TechDef { Id="steam_donkey", Name="蒸汽绞盘", Tree=0, Era=3, Req="intensive_agri", Effect="产出 +9%", Output=0.09f },
            new TechDef { Id="mechanized_workshops", Name="机械化工坊", Tree=0, Era=2, Req="mechanical_tools", Effect="加工产出 +11%", Output=0.11f },
            // T4
            new TechDef { Id="combustion", Name="内燃机", Tree=0, Era=4, Req="rotary_valve", Effect="产出 +16%, 军事 +6%", Output=0.16f, Mil=0.06f },
            new TechDef { Id="conveyors", Name="传送带", Tree=0, Era=4, Req="shift_work", Effect="加工产出 +15%", Output=0.15f },
            new TechDef { Id="mechanized_farming", Name="机械化农业", Tree=0, Era=4, Req="steam_donkey", Effect="农业产出 +18%", Output=0.18f },
            new TechDef { Id="nitrogen_fix", Name="氮固定法", Tree=0, Era=4, Req="improved_fert", Effect="农业产出 +16%", Output=0.16f },
            new TechDef { Id="steam_turbine", Name="蒸汽轮机", Tree=0, Era=4, Req="electrical_gen", Effect="产出 +15%", Output=0.15f },
            new TechDef { Id="telephone", Name="电话", Tree=0, Era=4, Req="electrical_gen", Effect="税收 +12%, 贸易优势 +10", Tax=0.12f, Trade=10f },
            new TechDef { Id="electrical_gen", Name="发电", Tree=0, Era=3, Req="rotary_valve", Effect="产出 +15%, 税收 +6%", Output=0.15f, Tax=0.06f },

            // v4.173 补齐 V3 官方全量(+21; 生产树官方 57)
            new TechDef { Id="chemical_bleaching", Name="化学漂白", Tree=0, Era=2, Req="crystal_glass", Effect="税收 +5%", Tax=0.05f },
            new TechDef { Id="aniline", Name="苯胺", Tree=0, Era=3, Req="rubber_mastication", Effect="税收 +6%", Tax=0.06f },
            new TechDef { Id="vulcanization", Name="硫化", Tree=0, Era=3, Req="rubber_mastication", Effect="税收 +7%", Tax=0.07f },
            new TechDef { Id="pumpjacks", Name="抽油机", Tree=0, Era=3, Req="steam_donkey,dynamite", Effect="税收 +6%, 产出 +4%", Tax=0.06f, Output=0.04f },
            new TechDef { Id="steel_railway_cars", Name="钢制铁路车厢", Tree=0, Era=3, Req="railways", Effect="税收 +7%", Tax=0.07f },
            new TechDef { Id="threshing_machines", Name="脱粒机", Tree=0, Era=3, Req="steam_donkey", Effect="产出 +9%", Output=0.09f },
            new TechDef { Id="vacuum_canning", Name="真空罐装", Tree=0, Era=3, Req="mechanized_workshops", Effect="产出 +7%", Output=0.07f },
            new TechDef { Id="art_silk", Name="人造丝", Tree=0, Era=4, Req="aniline", Effect="税收 +8%", Tax=0.08f },
            new TechDef { Id="automatic_bottle_blowers", Name="自动吹瓶机", Tree=0, Era=4, Req="vulcanization", Effect="产出 +8%", Output=0.08f },
            new TechDef { Id="electric_arc_process", Name="电弧炼钢法", Tree=0, Era=4, Req="open_hearth", Effect="产出 +10%, 军事 +5%", Output=0.10f, Mil=0.05f },
            new TechDef { Id="electrical_capacitors", Name="电容器", Tree=0, Era=4, Req="electrical_gen", Effect="产出 +8%, 税收 +5%", Output=0.08f, Tax=0.05f },
            new TechDef { Id="pasteurization", Name="巴氏杀菌", Tree=0, Era=4, Req="vacuum_canning,electrical_capacitors", Effect="激进 -0.04%/日", Radical=-0.0004f },
            new TechDef { Id="plastics", Name="塑料", Tree=0, Era=4, Req="reinforced_concrete", Effect="税收 +8%", Tax=0.08f },
            new TechDef { Id="pneumatic_tools", Name="气动工具", Tree=0, Era=4, Req="rotary_valve,reinforced_concrete", Effect="产出 +10%", Output=0.10f },
            new TechDef { Id="radio", Name="无线电", Tree=0, Era=4, Req="telephone", Effect="税收 +10%", Tax=0.10f },
            new TechDef { Id="electric_railways", Name="电气化铁路", Tree=0, Era=4, Req="electrical_capacitors,steel_railway_cars", Effect="税收 +10%", Tax=0.10f },
            new TechDef { Id="arc_welding", Name="电弧焊", Tree=0, Era=5, Req="electric_arc_process,pneumatic_tools", Effect="产出 +12%, 军事 +5%", Output=0.12f, Mil=0.05f },
            new TechDef { Id="compression_ignition", Name="压燃发动机", Tree=0, Era=5, Req="combustion", Effect="产出 +15%, 军事 +6%", Output=0.15f, Mil=0.06f },
            new TechDef { Id="dough_rollers", Name="压面辊", Tree=0, Era=5, Req="conveyors", Effect="产出 +12%", Output=0.12f },
            new TechDef { Id="flash_freezing", Name="速冻", Tree=0, Era=5, Req="pasteurization", Effect="激进 -0.03%/日", Radical=-0.0003f },
            new TechDef { Id="oil_turbine", Name="燃油轮机", Tree=0, Era=5, Req="steam_turbine", Effect="产出 +14%", Output=0.14f },

            // ================= 军事 58(V3 Military technology) =================
            // T1
            new TechDef { Id="navigation", Name="航海术", Tree=1, Era=1, Effect="税收 +4%, 贸易优势 +5", Tax=0.04f, Trade=5f },
            new TechDef { Id="standing_army", Name="常备军", Tree=1, Era=1, Effect="军事 +7%", Mil=0.07f },
            new TechDef { Id="admiralty", Name="海军部", Tree=1, Era=1, Req="navigation", Effect="军事 +5%, 税收 +3%", Mil=0.05f, Tax=0.03f },
            new TechDef { Id="army_reserves", Name="预备役", Tree=1, Era=1, Req="line_infantry", Effect="军事 +5%, 征召 +10%", Mil=0.05f, Conscript=0.10f },
            new TechDef { Id="drydocks", Name="干船坞", Tree=1, Era=1, Req="navigation", Effect="税收 +5%", Tax=0.05f },
            new TechDef { Id="gunsmithing", Name="枪械制造", Tree=1, Era=1, Req="standing_army", Effect="军事 +6%", Mil=0.06f },
            new TechDef { Id="mandatory_service", Name="义务兵役", Tree=1, Era=1, Req="standing_army", Effect="征召 +12%", Mil=0.04f, Conscript=0.12f },
            new TechDef { Id="military_drill", Name="军事操典", Tree=1, Era=1, Req="standing_army", Effect="军事 +7%", Mil=0.07f },
            new TechDef { Id="artillery", Name="火炮", Tree=1, Era=1, Req="gunsmithing", Effect="军事 +8%", Mil=0.08f },
            // T2
            new TechDef { Id="line_infantry", Name="线列步兵", Tree=1, Era=1, Req="mandatory_service", Effect="军事 +9%", Mil=0.09f },
            new TechDef { Id="napoleonic", Name="拿破仑战术", Tree=1, Era=1, Req="artillery", Effect="军事 +9%", Mil=0.09f },
            new TechDef { Id="field_works", Name="野战工事", Tree=1, Era=2, Req="napoleonic", Effect="军事 +9%", Mil=0.09f },
            new TechDef { Id="general_staff", Name="总参谋部", Tree=1, Era=2, Req="army_reserves", Effect="军事 +9%", Mil=0.09f },
            new TechDef { Id="logistics", Name="后勤学", Tree=1, Era=2, Req="army_reserves", Effect="征召 +12%, 军事 +4%", Mil=0.04f, Conscript=0.12f },
            new TechDef { Id="percussion_cap", Name="击发雷管", Tree=1, Era=2, Req="gunsmithing", Effect="军事 +9%", Mil=0.09f },
            new TechDef { Id="rifling", Name="膛线", Tree=1, Era=2, Req="percussion_cap", Effect="军事 +10%", Mil=0.10f },
            new TechDef { Id="shell_gun", Name="炮弹炮", Tree=1, Era=2, Req="artillery", Effect="军事 +10%", Mil=0.10f },
            new TechDef { Id="hydraulic_cranes", Name="液压起重机", Tree=1, Era=2, Req="drydocks", Effect="税收 +6%, 产出 +4%", Tax=0.06f, Output=0.04f },
            // T3
            new TechDef { Id="breech_artillery", Name="后装炮", Tree=1, Era=3, Req="rifling", Effect="军事 +12%", Mil=0.12f },
            new TechDef { Id="electric_telegraph", Name="电报", Tree=1, Era=3, Req="logistics", Effect="军事 +8%, 税收 +5%", Mil=0.08f, Tax=0.05f },
            new TechDef { Id="enlistment", Name="征兵办公室", Tree=1, Era=3, Req="logistics", Effect="征召 +15%", Mil=0.05f, Conscript=0.15f },
            new TechDef { Id="floating_harbor", Name="浮式港口", Tree=1, Era=3, Req="gantry_cranes", Effect="税收 +7%, 贸易优势 +5", Tax=0.07f, Trade=5f },
            new TechDef { Id="gantry_cranes", Name="龙门吊", Tree=1, Era=3, Req="hydraulic_cranes", Effect="产出 +8%, 税收 +4%", Output=0.08f, Tax=0.04f },
            new TechDef { Id="ironclad", Name="铁甲舰", Tree=1, Era=3, Req="admiralty", Effect="军事 +11%", Mil=0.11f },
            new TechDef { Id="jeune_ecole", Name="青年学派", Tree=1, Era=3, Req="self_torpedoes", Effect="军事 +10%", Mil=0.10f },
            new TechDef { Id="self_torpedoes", Name="自航鱼雷", Tree=1, Era=3, Req="logistics", Effect="军事 +9%", Mil=0.09f },
            new TechDef { Id="modern_nursing", Name="现代护理", Tree=1, Era=3, Req="general_staff", Effect="激进 -0.03%/日", Radical=-0.0003f },
            new TechDef { Id="repeaters", Name="连发枪", Tree=1, Era=3, Req="rifling", Effect="军事 +12%", Mil=0.12f },
            new TechDef { Id="military_stats", Name="军事统计", Tree=1, Era=3, Req="electric_telegraph", Effect="军事 +10%, 权威 +1/月", Mil=0.10f, Authority=1f },
            // T4
            new TechDef { Id="bolt_action", Name="栓动步枪", Tree=1, Era=4, Req="repeaters", Effect="军事 +15%", Mil=0.15f },
            new TechDef { Id="trench_works", Name="战壕工事", Tree=1, Era=4, Req="general_staff", Effect="军事 +13%", Mil=0.13f },
            new TechDef { Id="war_propaganda", Name="战争宣传", Tree=1, Era=4, Req="enlistment", Effect="征召 +15%, 激进 +0.02%/日", Conscript=0.15f, Radical=0.0002f },
            new TechDef { Id="wargaming", Name="兵棋推演", Tree=1, Era=4, Req="military_stats", Effect="军事 +14%", Mil=0.14f },
            new TechDef { Id="auto_mg", Name="自动机枪", Tree=1, Era=4, Req="bolt_action", Effect="军事 +16%", Mil=0.16f },
            new TechDef { Id="defense_depth", Name="纵深防御", Tree=1, Era=4, Req="trench_works", Effect="军事 +14%", Mil=0.14f },
            new TechDef { Id="submarine", Name="潜艇", Tree=1, Era=4, Req="self_torpedoes", Effect="军事 +13%", Mil=0.13f },
            new TechDef { Id="military_aviation", Name="军事航空", Tree=1, Era=4, Req="auto_mg", Effect="军事 +15%", Mil=0.15f },

            // v4.173 补齐 V3 官方全量(+21; 军事树官方 58)
            new TechDef { Id="paddle_steamer", Name="明轮船", Tree=1, Era=1, Req="admiralty", Effect="军事 +5%", Mil=0.05f },
            new TechDef { Id="triage", Name="急救分诊", Tree=1, Era=2, Req="logistics", Effect="军事 +4%", Mil=0.04f },
            new TechDef { Id="power_of_the_purse", Name="财权", Tree=1, Era=2, Req="admiralty", Effect="军事 +5%, 税收 +3%", Mil=0.05f, Tax=0.03f },
            new TechDef { Id="screw_frigate", Name="螺旋桨护卫舰", Tree=1, Era=2, Req="paddle_steamer", Effect="军事 +6%", Mil=0.06f },
            new TechDef { Id="handcranked_mg", Name="手摇机枪", Tree=1, Era=3, Req="repeaters,breech_artillery", Effect="军事 +10%", Mil=0.10f },
            new TechDef { Id="monitor", Name="浅水炮舰", Tree=1, Era=3, Req="ironclad,breech_artillery", Effect="军事 +9%", Mil=0.09f },
            new TechDef { Id="concrete_dockyards", Name="混凝土船坞", Tree=1, Era=4, Req="floating_harbor", Effect="税收 +6%", Tax=0.06f },
            new TechDef { Id="pre_dreadnought", Name="前无畏舰", Tree=1, Era=4, Req="ironclad", Effect="军事 +11%", Mil=0.11f },
            new TechDef { Id="sea_lane_strategies", Name="海路战略", Tree=1, Era=4, Req="jeune_ecole", Effect="军事 +10%, 贸易优势 +5", Mil=0.10f, Trade=5f },
            new TechDef { Id="landing_craft", Name="登陆艇", Tree=1, Era=4, Req="jeune_ecole,monitor", Effect="军事 +8%", Mil=0.08f },
            new TechDef { Id="dreadnought", Name="无畏舰", Tree=1, Era=4, Req="pre_dreadnought,sea_lane_strategies,concrete_dockyards,military_aviation", Effect="军事 +14%", Mil=0.14f },
            new TechDef { Id="aircraft_carrier", Name="航空母舰", Tree=1, Era=5, Req="dreadnought", Effect="军事 +15%", Mil=0.15f },
            new TechDef { Id="battleship", Name="战列舰", Tree=1, Era=5, Req="dreadnought", Effect="军事 +15%", Mil=0.15f },
            new TechDef { Id="destroyer", Name="驱逐舰", Tree=1, Era=5, Req="monitor", Effect="军事 +13%", Mil=0.13f },
            new TechDef { Id="chemical_warfare", Name="化学战", Tree=1, Era=5, Req="auto_mg", Effect="军事 +12%, 激进 +0.02%/日", Mil=0.12f, Radical=0.0002f },
            new TechDef { Id="concrete_fortifications", Name="混凝土要塞", Tree=1, Era=5, Req="defense_depth", Effect="军事 +12%", Mil=0.12f },
            new TechDef { Id="flamethrowers", Name="喷火器", Tree=1, Era=5, Req="trench_works,auto_mg", Effect="军事 +12%", Mil=0.12f },
            new TechDef { Id="mobile_armor", Name="装甲部队", Tree=1, Era=5, Req="concrete_fortifications,nco_training", Effect="军事 +16%", Mil=0.16f },
            new TechDef { Id="nco_training", Name="士官训练", Tree=1, Era=5, Req="trench_works,wargaming", Effect="军事 +13%", Mil=0.13f },
            new TechDef { Id="stormtroopers", Name="突击队", Tree=1, Era=5, Req="wargaming,trench_works", Effect="军事 +14%", Mil=0.14f },
            new TechDef { Id="battlefleet_tactics", Name="现代舰队战术", Tree=1, Era=5, Req="battleship,sea_lane_strategies", Effect="军事 +14%", Mil=0.14f },

            // ================= 社会 64(V3 Society technology) =================
            // T1
            new TechDef { Id="rationalism", Name="理性主义", Tree=2, Era=1, Effect="教育机会 +2%", Lit=0.02f },
            new TechDef { Id="urbanization", Name="城市化", Tree=2, Era=1, Effect="税收 +5%", Tax=0.05f },
            new TechDef { Id="academia", Name="学院制度", Tree=2, Era=1, Req="rationalism", Effect="教育机会 +3%", Lit=0.03f },
            new TechDef { Id="bureaucracy", Name="官僚制", Tree=2, Era=1, Req="urbanization", Effect="权威 +1/月", Authority=1f },
            new TechDef { Id="democracy", Name="民主", Tree=2, Era=1, Req="rationalism", Effect="激进 -0.02%/日", Radical=-0.0002f },
            new TechDef { Id="centralization", Name="中央集权", Tree=2, Era=1, Req="bureaucracy", Effect="税收 +6%, 权威 +1/月", Tax=0.06f, Authority=1f },
            new TechDef { Id="empiricism", Name="经验主义", Tree=2, Era=1, Req="academia", Effect="教育机会 +3%, 贸易优势 +5", Lit=0.03f, Trade=5f },
            new TechDef { Id="intl_relations", Name="国际关系", Tree=2, Era=1, Req="bureaucracy", Effect="权威 +1/月, 贸易优势 +5", Authority=1f, Trade=5f },
            new TechDef { Id="intl_trade", Name="国际贸易", Tree=2, Era=1, Req="bureaucracy", Effect="税收 +6%", Tax=0.06f },
            // T2
            new TechDef { Id="medical_degrees", Name="医学学位", Tree=2, Era=1, Req="academia", Effect="激进 -0.03%/日", Radical=-0.0003f },
            new TechDef { Id="law_enforcement", Name="执法", Tree=2, Era=1, Req="bureaucracy", Effect="激进 -0.03%/日", Radical=-0.0003f },
            new TechDef { Id="mass_communication", Name="大众传播", Tree=2, Era=1, Req="democracy", Effect="权威 +2/月", Authority=2f },
            new TechDef { Id="stock_exchange", Name="证券交易所", Tree=2, Era=1, Req="intl_trade", Effect="税收 +8%", Tax=0.08f },
            new TechDef { Id="banking", Name="银行业", Tree=2, Era=1, Req="currency_standards", Effect="税收 +8%", Tax=0.08f },
            new TechDef { Id="currency_standards", Name="货币标准", Tree=2, Era=1, Req="intl_trade", Effect="税收 +7%", Tax=0.07f },
            new TechDef { Id="central_archives", Name="中央档案", Tree=2, Era=2, Req="centralization", Effect="权威 +2/月, 税收 +5%", Authority=2f, Tax=0.05f },
            new TechDef { Id="corporate_charters", Name="公司特许状", Tree=2, Era=2, Req="stock_exchange", Effect="税收 +8%", Tax=0.08f },
            new TechDef { Id="egalitarianism", Name="平等主义", Tree=2, Era=2, Req="democracy", Effect="激进 -0.03%/日", Radical=-0.0003f },
            new TechDef { Id="nationalism", Name="民族主义", Tree=2, Era=2, Req="mass_communication", Effect="权威 +2/月, 激进 +0.02%/日", Authority=2f, Radical=0.0002f },
            // T3
            new TechDef { Id="labor_movement", Name="劳工运动", Tree=2, Era=2, Req="egalitarianism", Effect="激进 -0.03%/日, 税收 -2%", Radical=-0.0003f, Tax=-0.02f },
            new TechDef { Id="socialism", Name="社会主义", Tree=2, Era=3, Req="labor_movement", Effect="激进 -0.04%/日, 税收 -3%", Radical=-0.0004f, Tax=-0.03f },
            new TechDef { Id="human_rights", Name="人权", Tree=2, Era=3, Req="egalitarianism", Effect="激进 -0.04%/日", Radical=-0.0004f },
            new TechDef { Id="feminism", Name="女权主义", Tree=2, Era=3, Req="human_rights", Effect="激进 -0.03%/日, 税收 +4%", Radical=-0.0003f, Tax=0.04f },
            new TechDef { Id="corporatism", Name="法团主义", Tree=2, Era=3, Req="nationalism", Effect="权威 +3/月, 激进 -0.02%/日", Authority=3f, Radical=-0.0002f },
            new TechDef { Id="pharmaceuticals", Name="制药学", Tree=2, Era=2, Req="medical_degrees", Effect="激进 -0.04%/日", Radical=-0.0004f },
            new TechDef { Id="joint_stock", Name="股份公司", Tree=2, Era=2, Req="corporate_charters", Effect="税收 +9%", Tax=0.09f },
            new TechDef { Id="central_banking", Name="中央银行", Tree=2, Era=2, Req="banking", Effect="税收 +10%", Tax=0.10f },
            new TechDef { Id="id_documents", Name="身份证件", Tree=2, Era=3, Req="central_archives", Effect="权威 +2/月, 税收 +6%", Authority=2f, Tax=0.06f },
            // T4
            new TechDef { Id="investment_banks", Name="投资银行", Tree=2, Era=3, Req="joint_stock", Effect="税收 +12%", Tax=0.12f },
            new TechDef { Id="political_agitation", Name="政治鼓动", Tree=2, Era=4, Req="socialism", Effect="权威 +3/月, 激进 +0.03%/日", Authority=3f, Radical=0.0003f },
            new TechDef { Id="mass_propaganda", Name="大众宣传", Tree=2, Era=5, Req="political_agitation", Effect="权威 +3/月, 激进 +0.03%/日", Authority=3f, Radical=0.0003f },
            new TechDef { Id="central_planning", Name="中央计划", Tree=2, Era=4, Req="id_documents", Effect="权威 +3/月, 税收 +10%", Authority=3f, Tax=0.10f },
            new TechDef { Id="multilateral", Name="多边联盟", Tree=2, Era=4, Req="nationalism", Effect="权威 +2/月, 贸易优势 +10", Authority=2f, Trade=10f },
            new TechDef { Id="psychoanalysis", Name="精神分析", Tree=2, Era=4, Req="pharmaceuticals", Effect="教育机会 +6%, 激进 -0.03%/日", Lit=0.06f, Radical=-0.0003f },
            new TechDef { Id="steel_frame", Name="钢结构建筑", Tree=2, Era=3, Req="urbanization", Effect="产出 +10%, 税收 +5%", Output=0.10f, Tax=0.05f },
            new TechDef { Id="film", Name="电影", Tree=2, Era=4, Req="psychoanalysis", Effect="权威 +2/月, 激进 -0.02%/日", Authority=2f, Radical=-0.0002f },
            new TechDef { Id="malaria_prevention", Name="疟疾防治", Tree=2, Era=4, Req="pharmaceuticals", Effect="激进 -0.05%/日", Radical=-0.0005f },

            // v4.173 补齐 V3 官方全量(+27; 社会树官方 64)
            new TechDef { Id="urban_planning", Name="城市规划", Tree=2, Era=1, Req="urbanization", Effect="税收 +5%", Tax=0.05f },
            new TechDef { Id="romanticism", Name="浪漫主义", Tree=2, Era=1, Req="academia", Effect="权威 +1/月", Authority=1f },
            new TechDef { Id="colonization", Name="殖民", Tree=2, Era=1, Req="intl_relations", Effect="税收 +4%", Tax=0.04f },
            new TechDef { Id="realism", Name="现实主义", Tree=2, Era=2, Req="romanticism", Effect="权威 +1/月, 教育机会 +2%", Authority=1f, Lit=0.02f },
            new TechDef { Id="quinine", Name="奎宁", Tree=2, Era=2, Req="colonization,pharmaceuticals", Effect="激进 -0.04%/日", Radical=-0.0004f },
            new TechDef { Id="dialectics", Name="辩证法", Tree=2, Era=2, Req="empiricism", Effect="教育机会 +3%", Lit=0.03f },
            new TechDef { Id="psychiatry", Name="精神病学", Tree=2, Era=2, Req="empiricism", Effect="税收 +5%", Tax=0.05f },
            new TechDef { Id="modern_sewerage", Name="现代下水道", Tree=2, Era=2, Req="urban_planning", Effect="激进 -0.03%/日", Radical=-0.0003f },
            new TechDef { Id="postal_savings", Name="邮政储蓄", Tree=2, Era=2, Req="stock_exchange", Effect="税收 +6%", Tax=0.06f },
            new TechDef { Id="organized_sports", Name="体育运动", Tree=2, Era=2, Req="nationalism", Effect="权威 +1/月", Authority=1f },
            new TechDef { Id="camera", Name="摄影", Tree=2, Era=3, Req="realism", Effect="权威 +1/月", Authority=1f },
            new TechDef { Id="mutual_funds", Name="共同基金", Tree=2, Era=3, Req="central_banking,postal_savings", Effect="税收 +7%", Tax=0.07f },
            new TechDef { Id="pan_nationalism", Name="泛民族主义", Tree=2, Era=3, Req="nationalism", Effect="权威 +2/月, 激进 +0.02%/日", Authority=2f, Radical=0.0002f },
            new TechDef { Id="anarchism", Name="无政府主义", Tree=2, Era=3, Req="egalitarianism", Effect="激进 -0.03%/日", Radical=-0.0003f },
            new TechDef { Id="civilizing_mission", Name="文明使命", Tree=2, Era=3, Req="quinine,nationalism", Effect="税收 +6%, 权威 +1/月", Tax=0.06f, Authority=1f },
            new TechDef { Id="philosophical_pragmatism", Name="实用主义", Tree=2, Era=3, Req="psychiatry", Effect="教育机会 +3%, 权威 +1/月", Lit=0.03f, Authority=1f },
            new TechDef { Id="corporate_governance", Name="公司治理", Tree=2, Era=4, Req="investment_banks", Effect="税收 +8%", Tax=0.08f },
            new TechDef { Id="elevators", Name="电梯", Tree=2, Era=4, Req="steel_frame", Effect="产出 +8%", Output=0.08f },
            new TechDef { Id="zeppelins", Name="飞艇", Tree=2, Era=4, Req="steel_frame", Effect="税收 +7%", Tax=0.07f },
            new TechDef { Id="intl_exchange_standards", Name="国际汇兑标准", Tree=2, Era=4, Req="mutual_funds", Effect="税收 +8%, 贸易优势 +5", Tax=0.08f, Trade=5f },
            new TechDef { Id="analytical_philosophy", Name="分析哲学", Tree=2, Era=5, Req="psychoanalysis", Effect="教育机会 +5%", Lit=0.05f },
            new TechDef { Id="behaviorism", Name="行为主义", Tree=2, Era=5, Req="psychoanalysis", Effect="权威 +2/月", Authority=2f },
            new TechDef { Id="antibiotics", Name="抗生素", Tree=2, Era=5, Req="malaria_prevention", Effect="激进 -0.05%/日", Radical=-0.0005f },
            new TechDef { Id="macroeconomics", Name="宏观经济学", Tree=2, Era=5, Req="corporate_governance,intl_exchange_standards", Effect="税收 +10%", Tax=0.10f },
            new TechDef { Id="mass_surveillance", Name="大规模监控", Tree=2, Era=5, Req="central_planning", Effect="权威 +3/月", Authority=3f },
            new TechDef { Id="modern_financial", Name="现代金融工具", Tree=2, Era=5, Req="intl_exchange_standards", Effect="税收 +10%", Tax=0.10f },
            new TechDef { Id="paved_roads", Name="铺装道路", Tree=2, Era=5, Req="elevators", Effect="产出 +8%, 税收 +4%", Output=0.08f, Tax=0.04f }
        };

        // v6.0: 按国科技(文档 28.10): key = Kingdom.StringId, 玩家国用 PlayerKey(); 空档期 fallback ""
        internal static readonly Dictionary<string, NationTech> Nations = new Dictionary<string, NationTech>();
        private static int _lastDay = -1;
        private static bool _nationsLoaded;   // FIA_Tech 已按国载入(旧档缺段时走全局迁移)

        internal static Kingdom PlayerKingdom()
        {
            try { return NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null; }
            catch { return null; }
        }

        internal static string PlayerKey()
        {
            try
            {
                var pk = PlayerKingdom();
                return pk != null && pk.StringId != null ? pk.StringId : "";
            }
            catch { return ""; }
        }

        internal static string KeyOf(Kingdom k)
        {
            try { return k != null && k.StringId != null ? k.StringId : PlayerKey(); }
            catch { return PlayerKey(); }
        }

        internal static NationTech Of(Kingdom k) { return OfKey(KeyOf(k)); }

        internal static NationTech OfKey(string key)
        {
            if (key == null) key = "";
            NationTech n;
            if (!Nations.TryGetValue(key, out n)) { n = new NationTech(); Nations[key] = n; }
            return n;
        }

        // 兼容包装: 旧调用点(玩家 UI / 经济)默认读玩家王国
        internal static HashSet<string> Done { get { return Of(PlayerKingdom()).Done; } }
        internal static Dictionary<string, float> Progress { get { return Of(PlayerKingdom()).Progress; } }
        internal static string[] CurrentId { get { return Of(PlayerKingdom()).Current; } }
        internal static string[] SpreadId { get { return Of(PlayerKingdom()).Spread; } }

        internal static TechDef Find(string id)
        {
            for (int i = 0; i < All.Count; i++) if (All[i].Id == id) return All[i];
            return null;
        }

        internal static bool IsDone(string id) { return id != null && Done.Contains(id); }   // 兼容: 玩家国
        internal static bool IsDone(Kingdom k, string id) { return id != null && Of(k).Done.Contains(id); }

        // ================= 按国 API(文档 28.10 A) =================
        internal static float ProgressOf(Kingdom k, string id)
        {
            float p;
            Of(k).Progress.TryGetValue(id, out p);
            return p;
        }

        // 加进度(达到成本自动完成; 返回是否完成) —— 玩家/AI/扩散/一次性加速共用
        internal static bool AddProgress(Kingdom k, string id, float n)
        {
            try
            {
                var t = Find(id);
                if (t == null || n <= 0f) return false;
                var nat = Of(k);
                if (nat.Done.Contains(id)) return false;
                float p; nat.Progress.TryGetValue(id, out p);
                p += n;
                if (p >= CostOf(k, t)) { Complete(k, id, null); return true; }
                nat.Progress[id] = p;
                return false;
            }
            catch { return false; }
        }

        // 完成科技(清进度与队列); sourceKingdomId 非空 = 扩散来源(科技页展示)
        internal static void Complete(Kingdom k, string id, string sourceKingdomId)
        {
            try
            {
                var t = Find(id);
                if (t == null) return;
                var nat = Of(k);
                nat.Done.Add(id);
                nat.Progress.Remove(id);
                for (int i = 0; i < TreeCount; i++)
                {
                    if (nat.Current[i] == id) nat.Current[i] = null;
                    if (nat.Spread[i] == id) nat.Spread[i] = null;
                }
                if (!string.IsNullOrEmpty(sourceKingdomId)) nat.SpreadFrom[id] = sourceKingdomId;
                else nat.SpreadFrom.Remove(id);   // 自行完成 -> 清掉旧的扩散来源标记
                DLog.Force("科技: " + KeyOf(k) + " 完成 " + id + " " + t.Name);
            }
            catch (Exception ex) { DLog.Force("科技完成异常: " + ex.Message); }
        }
        internal static void Complete(Kingdom k, string id) { Complete(k, id, null); }

        internal static string[] QueueOf(Kingdom k)
        {
            var nat = Of(k);
            var copy = new string[TreeCount];
            for (int i = 0; i < TreeCount; i++) copy[i] = nat.Current[i];
            return copy;
        }

        // 兵种/装备门槛(文档 28.10 A / 28.11): 本国是否已解锁(无要求/未知科技 -> 不设限)
        internal static bool UnlockedFor(Kingdom k, string techId)
        {
            try
            {
                if (string.IsNullOrEmpty(techId)) return true;
                return Find(techId) == null || IsDone(k, techId);
            }
            catch { return true; }
        }

        // v4.173: 前置支持多个(V3 官方很多科技是双前置), 逗号分隔
        internal static string[] Reqs(TechDef t)
        {
            try
            {
                if (t == null || string.IsNullOrEmpty(t.Req)) return EmptyReqs;
                var parts = t.Req.Split(',');
                var list = new List<string>();
                for (int i = 0; i < parts.Length; i++)
                {
                    string id = parts[i].Trim();
                    if (id.Length > 0) list.Add(id);
                }
                return list.Count > 0 ? list.ToArray() : EmptyReqs;
            }
            catch { return EmptyReqs; }
        }
        private static readonly string[] EmptyReqs = new string[0];

        internal static bool ReqOk(Kingdom k, TechDef t)
        {
            try
            {
                if (t == null) return false;
                var reqs = Reqs(t);
                for (int i = 0; i < reqs.Length; i++) if (!IsDone(k, reqs[i])) return false;
                return true;
            }
            catch { return false; }
        }
        internal static bool ReqOk(TechDef t) { return ReqOk(PlayerKingdom(), t); }   // 兼容: 玩家国

        // 未完成的前置名(用于提示)
        internal static string MissingReqText(Kingdom k, TechDef t)
        {
            try
            {
                var reqs = Reqs(t);
                var sb = new StringBuilder();
                for (int i = 0; i < reqs.Length; i++)
                {
                    if (IsDone(k, reqs[i])) continue;
                    if (sb.Length > 0) sb.Append(", ");
                    var p = Find(reqs[i]);
                    sb.Append(p != null ? p.Name : reqs[i]);
                }
                return sb.Length > 0 ? sb.ToString() : "-";
            }
            catch { return "-"; }
        }
        internal static string MissingReqText(TechDef t) { return MissingReqText(PlayerKingdom(), t); }

        // 时代成本 + 未研究惩罚(V3 公式: Era cost + 未研究数 × (时代差 × 0.25 × Era cost); 按本国未完成算)
        internal static float CostOf(Kingdom k, TechDef t)
        {
            try
            {
                if (t == null) return 100f;
                float baseCost = EraCost[Math.Min(EraCost.Length - 1, Math.Max(0, t.Era - 1))];
                int un = 0;
                for (int i = 0; i < All.Count; i++)
                {
                    var o = All[i];
                    if (o.Tree != t.Tree || IsDone(k, o.Id)) continue;
                    if (o.Era < t.Era) un++;
                }
                return baseCost + un * ((t.Era - 1) * 0.25f * baseCost);
            }
            catch { return 100f; }
        }
        internal static float CostOf(TechDef t) { return CostOf(PlayerKingdom(), t); }   // 兼容: 玩家国

        internal static float CostOf(string id) { return CostOf(Find(id)); }

        // 识字率(百分比)
        internal static float LiteracyPct()
        {
            try
            {
                float num = 0f, den = 0f;
                foreach (var kv in Pops.BySettlement)
                {
                    var l = kv.Value;
                    if (l == null) continue;
                    for (int i = 0; i < l.Count; i++)
                    {
                        var p = l[i];
                        if (p == null || p.Size < 0.5f) continue;
                        num += p.Literacy * p.Size; den += p.Size;
                    }
                }
                if (den > 0f) return num / den * 100f;
            }
            catch { }
            return 5f;
        }

        // v5.0: 本国定居点 id 集(替代"建筑×全定居点"嵌套扫描; InnovationDaily/Capacity 共用, O(S+B))
        internal static HashSet<string> OwnSettlementIds()
        {
            var ids = new HashSet<string>();
            try
            {
                var pk = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                if (pk == null) return ids;
                foreach (var s in TaleWorlds.CampaignSystem.Settlements.Settlement.All)
                    if (s != null && ReferenceEquals(s.MapFaction, pk)) ids.Add(s.StringId);
            }
            catch { }
            return ids;
        }

        // 每日创新产出: 基础 + 大学建筑(V3: 基础 50, 大学每级 1-2; 缩放 1/10)
        internal static float InnovationDaily()
        {
            try
            {
                float v = 0.5f;
                var mineIds = Research.OwnSettlementIds();
                foreach (var kv in EconomyWorld.Buildings)
                {
                    var sb = kv.Value;
                    if (sb == null || !mineIds.Contains(kv.Key)) continue;
                    for (int i = 0; i < sb.Groups.Count; i++)
                    {
                        var g = sb.Groups[i];
                        if (g == null || g.Count <= 0) continue;
                        string id = g.DefId ?? "";
                        if (id.Contains("university") || id.Contains("academy")) v += 0.5f * g.Count;
                    }
                }
                return v;
            }
            catch { return 0.5f; }
        }

        // 投入上限(V3: 50 + 1.5×识字率%; 缩放 1/10 -> 5 + 0.15×识字率)
        internal static float InnovationCap()
        {
            try { return 5f + 0.15f * LiteracyPct(); } catch { return 6f; }
        }

        internal static void Daily(int day)
        {
            try
            {
                if (day == _lastDay) return;
                _lastDay = day;
                var pk = PlayerKingdom();
                var nat = Of(pk);
                float gain = InnovationDaily() * ResearchSpeedMult();
                float cap = InnovationCap();
                float spent = Math.Min(gain, cap);
                float unspent = Math.Max(0f, gain - cap);
                // 玩家国: 每树当前研究 + 创新; 无当前则自动选成本最低的可研项
                for (int t = 0; t < TreeCount; t++)
                {
                    if (string.IsNullOrEmpty(nat.Current[t]) || IsDone(pk, nat.Current[t]))
                        nat.Current[t] = AutoPick(t);
                    if (string.IsNullOrEmpty(nat.Current[t])) continue;
                    var td = Find(nat.Current[t]);
                    if (td == null) { nat.Current[t] = null; continue; }
                    float p;
                    nat.Progress.TryGetValue(td.Id, out p);
                    p += spent;
                    if (p >= CostOf(pk, td))
                    {
                        Complete(pk, td.Id);
                        InterestGroups.NotifyPlayer("科技完成: " + td.Name + "(" + td.Effect + ")", false);
                    }
                    else nat.Progress[td.Id] = p;
                }
                // 技术传播(V3 官方公式, 缩放 1/10: Spread = (25 + 0.2×未使用创新 + 0.75×识字率%) × rand(0.5,1.5))
                if (unspent > 0.01f)
                {
                    float spread = (2.5f + 0.2f * unspent + 0.075f * LiteracyPct())
                                 * (0.5f + (float)TaleWorlds.Core.MBRandom.RandomFloat);
                    for (int t = 0; t < TreeCount; t++)
                    {
                        if (string.IsNullOrEmpty(nat.Spread[t]) || IsDone(pk, nat.Spread[t]) || nat.Current[t] == nat.Spread[t])
                            nat.Spread[t] = AutoPick(t);
                        if (string.IsNullOrEmpty(nat.Spread[t])) continue;
                        var sd = Find(nat.Spread[t]);
                        if (sd == null) continue;
                        float p;
                        nat.Progress.TryGetValue(sd.Id, out p);
                        p += spread;
                        if (p >= CostOf(pk, sd))
                        {
                            Complete(pk, sd.Id);
                            InterestGroups.NotifyPlayer("技术传播: " + sd.Name + " 传入我国(" + sd.Effect + ")", false);
                        }
                        else nat.Progress[sd.Id] = p;
                    }
                }
                // 低频扩散(文档 28.10 B): 每 42 天一次
                try { TechDiffusion.Daily(day); } catch { }
                // AI 国研究(按各国独立科技树推进)
                AiMonthly(day);
            }
            catch (Exception ex) { DLog.Force("研究日结异常: " + ex.Message); }
        }

        // ================= AI 国研究(偏好 + 月结推进; v6.0 按各国独立科技树) =================
        //   AI 国按统一大脑目标(AiBrain/AiDirector.GoalOf)每月向本国 Progress 投点,
        //   复用原偏好/效果匹配逻辑; 完成只影响本国 Done, 玩家队列不受影响。
        private static int _lastAiMonth = -9999;

        // 目标 -> 三树偏好(生产/军事/社会; 用现有 TechDef.Tree, 不新增科技条目)
        private static float AiTreePref(AiDirector.Goal g, int tree)
        {
            switch (g)
            {
                case AiDirector.Goal.Survival:      return tree == 1 ? 1.00f : (tree == 0 ? 0.55f : 0.35f);
                case AiDirector.Goal.Military:      return tree == 1 ? 1.00f : (tree == 0 ? 0.35f : 0.20f);
                case AiDirector.Goal.Politics:      return tree == 2 ? 1.00f : (tree == 0 ? 0.25f : 0.30f);
                case AiDirector.Goal.Consolidation: return tree == 0 ? 0.90f : (tree == 2 ? 0.90f : 0.25f);
                default:                            return tree == 0 ? 1.00f : (tree == 2 ? 0.45f : 0.25f);   // Economy
            }
        }

        // 目标 -> 现有科技效果字段匹配(Output/Tax/Mil/Radical/Lit/Authority; 不加新字段)
        private static float AiTechFit(AiDirector.Goal g, TechDef t)
        {
            switch (g)
            {
                case AiDirector.Goal.Military:      return t.Mil;
                case AiDirector.Goal.Survival:      return t.Mil * 1.2f + t.Tax * 0.3f;
                case AiDirector.Goal.Politics:      return t.Authority * 0.03f + t.Lit + (t.Radical < 0f ? -t.Radical * 0.5f : 0f);
                case AiDirector.Goal.Consolidation: return t.Output + t.Lit + t.Authority * 0.02f;
                default:                            return t.Output + t.Tax;   // Economy
            }
        }

        // AI 选一项: 前置满足且未完成, 偏好树 × 效果匹配 / 成本(按本国已完成)
        private static string AiPick(Kingdom k, AiDirector.Goal g)
        {
            try
            {
                TechDef best = null;
                float bestScore = 0f;
                for (int i = 0; i < All.Count; i++)
                {
                    var t = All[i];
                    if (IsDone(k, t.Id) || !ReqOk(k, t)) continue;
                    float pref = AiTreePref(g, t.Tree);
                    float score = pref * (1f + AiTechFit(g, t) * 5f) / Math.Max(1f, CostOf(k, t));
                    if (score > bestScore) { bestScore = score; best = t; }
                }
                return best != null ? best.Id : null;
            }
            catch { return null; }
        }

        // 单国推进: 研究速度 = 基础 + 领土 + 工业倾向; 返回是否完成一项
        private static bool AiAdvance(Kingdom k)
        {
            try
            {
                string id = AiPick(k, AiBrain.GoalOf(k));
                if (id == null) return false;
                var t = Find(id);
                if (t == null) return false;
                int terr = k.Settlements != null ? k.Settlements.Count : 0;
                float gain = (0.9f + 0.07f * terr) * (0.70f + AiPersonality.IndustryOf(k) / 200f);
                var nat = Of(k);
                if (nat.Current[t.Tree] != id) nat.Current[t.Tree] = id;   // AI 当前研究队列(供 QueueOf/外交页)
                bool done = AddProgress(k, id, gain);
                if (done)
                {
                    if (nat.Current[t.Tree] == id) nat.Current[t.Tree] = null;
                    DLog.Info("AI 研究: " + k.Name + " 完成 " + t.Name + "(" + t.Effect + ")");
                }
                return done;
            }
            catch { return false; }
        }

        // 月结: 骑砍 1 月 = 7 天; 每国每月一次(各按本国科技树; 跳过玩家王国)
        internal static void AiMonthly(int day)
        {
            try
            {
                int month = day / 7;
                if (month == _lastAiMonth) return;
                _lastAiMonth = month;
                var pk = PlayerKingdom();
                foreach (var k in Kingdom.All)
                {
                    if (k == null || k.IsEliminated) continue;
                    if (pk != null && ReferenceEquals(pk, k)) continue;   // 玩家自己研究
                    try { AiAdvance(k); } catch { }
                }
            }
            catch (Exception ex) { DLog.Force("AI 研究月结异常: " + ex.Message); }
        }

        // 自动选择(玩家国): 前置满足且未完成, 成本最低
        private static string AutoPick(int tree) { return AutoPick(PlayerKingdom(), tree); }

        private static string AutoPick(Kingdom k, int tree)
        {
            try
            {
                TechDef best = null;
                float bestCost = float.MaxValue;
                for (int i = 0; i < All.Count; i++)
                {
                    var t = All[i];
                    if (t.Tree != tree || IsDone(k, t.Id) || !ReqOk(k, t)) continue;
                    float c = CostOf(k, t);
                    float p; Of(k).Progress.TryGetValue(t.Id, out p);
                    c -= p;   // 考虑已有进度
                    if (c < bestCost) { bestCost = c; best = t; }
                }
                return best != null ? best.Id : null;
            }
            catch { return null; }
        }

        internal static string StartResearch(int tree)
        {
            try
            {
                if (tree < 0 || tree >= TreeCount) return "";
                if (!string.IsNullOrEmpty(CurrentId[tree]) && !IsDone(CurrentId[tree]))
                {
                    var cur = Find(CurrentId[tree]);
                    if (cur != null)
                    {
                        CurrentId[tree] = null;   // 取消(进度保留, V3 官方)
                        return "已暂停研究 " + cur.Name + "(进度保留)";
                    }
                }
                string id = AutoPick(tree);
                if (id == null) return TreeNames[tree] + " 树已无可研究项目";
                CurrentId[tree] = id;
                var t = Find(id);
                DLog.Force("科技: 开始研究 " + id);
                return "开始研究 " + t.Name + "(成本 " + (int)CostOf(t) + ")";
            }
            catch { return "研究操作失败"; }
        }

        internal static string SetResearch(int tree, string techId)
        {
            try
            {
                if (tree < 0 || tree >= TreeCount) return "";
                var t = Find(techId);
                if (t == null || t.Tree != tree) return "无效科技";
                if (IsDone(techId)) return t.Name + " 已完成";
                if (!ReqOk(t)) return "前置未满足(需 " + MissingReqText(t) + ")";
                CurrentId[tree] = techId;
                DLog.Force("科技: 开始研究 " + techId);
                return "开始研究 " + t.Name + "(成本 " + (int)CostOf(t) + ")";
            }
            catch { return "研究操作失败"; }
        }

        // 研究速度: V3 官方 IG happy traits(+10%); 我们: 满意/忠诚集团加成 + 民族精神
        internal static float ResearchSpeedMult()
        {
            try
            {
                float m = 1f;
                if (InterestGroups.SatOf(4) >= 25) m += 0.10f;   // 商帮(生产)
                if (InterestGroups.SatOf(7) >= 25) m += 0.10f;   // 军队(军事)
                if (InterestGroups.SatOf(2) >= 25) m += 0.10f;   // 地方贵族/学者(社会)
                // v4.150: 民族精神(文教昌明等)
                try
                {
                    var pk = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                    if (pk != null) m *= NationalSpirits.ResearchMult(pk);
                }
                catch { }
                return m;
            }
            catch { return 1f; }
        }

        internal static string ProgressText(int tree)
        {
            try
            {
                if (tree < 0 || tree >= TreeCount) return "";
                string curId = CurrentId[tree];
                if (string.IsNullOrEmpty(curId)) curId = AutoPick(tree);
                if (string.IsNullOrEmpty(curId)) return TreeNames[tree] + " 树: 已完成";
                var t = Find(curId);
                if (t == null) return "";
                float p; Progress.TryGetValue(t.Id, out p);
                float c = CostOf(t);
                string s = TreeNames[tree] + ": " + t.Name + " " + (int)p + "/" + (int)c + " (时代 " + t.Era + ")";
                if (!string.IsNullOrEmpty(SpreadId[tree]))
                {
                    var sp = Find(SpreadId[tree]);
                    if (sp != null) s += " · 传播: " + sp.Name;
                }
                return s;
            }
            catch { return ""; }
        }

        internal static string BonusText(int tree)
        {
            try
            {
                int n = 0;
                for (int i = 0; i < All.Count; i++) if (All[i].Tree == tree && IsDone(All[i].Id)) n++;
                return "已完成 " + n + "/" + (All.Count / TreeCount) + " 项 · 创新 +" + InnovationDaily().ToString("F1")
                    + "/日 · 上限 " + InnovationCap().ToString("F0") + " · 识字 " + LiteracyPct().ToString("F0") + "%"
                    + (ResearchSpeedMult() > 1f ? " · 速度 ×" + ResearchSpeedMult().ToString("F2") : "");
            }
            catch { return ""; }
        }

        // ===== 效果合计(接既有系统) =====
        internal static float TaxMult()
        {
            try { float v = 0f; for (int i = 0; i < All.Count; i++) if (IsDone(All[i].Id)) v += All[i].Tax; return 1f + v; } catch { return 1f; }
        }
        internal static float OutputMult()
        {
            try
            {
                float v = 0f;
                for (int i = 0; i < All.Count; i++) if (IsDone(All[i].Id)) v += All[i].Output;
                float m = 1f + v;
                // v4.150: 民族精神(营造之风/太平盛世/劳工尊严等)
                var pk = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                if (pk != null) m *= NationalSpirits.OutputMult(pk);
                return m;
            }
            catch { return 1f; }
        }
        internal static float MilitaryMult()
        {
            try
            {
                float v = 0f;
                for (int i = 0; i < All.Count; i++) if (IsDone(All[i].Id)) v += All[i].Mil;
                float m = 1f + v;
                // v4.150: 民族精神(尚武传统/凯歌高奏/众矢之的)
                var pk = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                if (pk != null) m *= NationalSpirits.MilitaryMult(pk);
                return m;
            }
            catch { return 1f; }
        }
        internal static float RegenBonus()
        {
            try { float v = 0f; for (int i = 0; i < All.Count; i++) if (IsDone(All[i].Id)) v += All[i].Authority; return v; } catch { return 0f; }
        }
        internal static float RadicalDelta()
        {
            try { float v = 0f; for (int i = 0; i < All.Count; i++) if (IsDone(All[i].Id)) v += All[i].Radical; return v; } catch { return 0f; }
        }
        internal static float LiteracyBonus()
        {
            try { float v = 0f; for (int i = 0; i < All.Count; i++) if (IsDone(All[i].Id)) v += All[i].Lit; return v; } catch { return 0f; }
        }

        // v4.241: 征召上限加成("征召 +N%"; 原来只有文案没有字段, 征兵上限从不吃科技)
        internal static float ConscriptMult()
        {
            try
            {
                float v = 0f;
                for (int i = 0; i < All.Count; i++) if (IsDone(All[i].Id)) v += All[i].Conscript;
                return 1f + v;
            }
            catch { return 1f; }
        }

        // v4.241: 贸易优势点数("贸易优势 +N"; 接 TradeRoutes.TradeAdvantageOf, 100 点 = 25% 更好价格)
        internal static float TradeBonus()
        {
            try
            {
                float v = 0f;
                for (int i = 0; i < All.Count; i++) if (IsDone(All[i].Id)) v += All[i].Trade;
                return v;
            }
            catch { return 0f; }
        }

        internal static TechDef ByName(string name)
        {
            try
            {
                if (string.IsNullOrEmpty(name)) return null;
                for (int i = 0; i < All.Count; i++)
                {
                    var t = All[i];
                    if (t != null && t.Name == name) return t;
                }
            }
            catch { }
            return null;
        }

        // 某国已完成的军事树科技数(0..58) —— 战斗/编制上限按"当事国"算, 不再一律读玩家国
        internal static int MilitaryTechCountOf(Kingdom k)
        {
            try
            {
                int n = 0;
                for (int i = 0; i < All.Count; i++)
                {
                    var t = All[i];
                    if (t == null || t.Tree != 1) continue;
                    if (IsDone(k, t.Id)) n++;
                }
                return n;
            }
            catch { return 0; }
        }

        // ================= 存档(文档 28.10 A): FIA_Tech = 按国已完成位图 + 有进度的项 =================
        // 格式: v3;<扩散判定日>;<王国id>~<位图Base64>~<进度 id:val|...>~<cur0,cur1,cur2>~<sp0,sp1,sp2>~<来源 tech:src|...>;...
        internal static string SaveNations()
        {
            try
            {
                var sb = new StringBuilder();
                sb.Append("v3;").Append(TechDiffusion.LastCheckDay).Append(';');
                foreach (var kv in Nations)
                {
                    var nat = kv.Value;
                    if (nat == null) continue;
                    if (nat.Done.Count == 0 && nat.Progress.Count == 0 && nat.SpreadFrom.Count == 0
                        && IsEmptySlots(nat.Current) && IsEmptySlots(nat.Spread)) continue;
                    sb.Append(kv.Key).Append('~');
                    sb.Append(DoneBitmap(nat)).Append('~');
                    bool first = true;
                    foreach (var p in nat.Progress)
                    {
                        if (p.Value <= 0f) continue;
                        if (!first) sb.Append('|');
                        first = false;
                        sb.Append(p.Key).Append(':').Append(p.Value.ToString("F1", CultureInfo.InvariantCulture));
                    }
                    sb.Append('~');
                    for (int t = 0; t < TreeCount; t++) { if (t > 0) sb.Append(','); sb.Append(nat.Current[t] ?? ""); }
                    sb.Append('~');
                    for (int t = 0; t < TreeCount; t++) { if (t > 0) sb.Append(','); sb.Append(nat.Spread[t] ?? ""); }
                    sb.Append('~');
                    first = true;
                    foreach (var s in nat.SpreadFrom)
                    {
                        if (!first) sb.Append('|');
                        first = false;
                        sb.Append(s.Key).Append(':').Append(s.Value ?? "");
                    }
                    sb.Append(';');
                }
                return sb.ToString();
            }
            catch (Exception ex) { DLog.Force("科技存档异常: " + ex.Message); return ""; }
        }

        internal static string Save() { return SaveNations(); }   // FIA_Research 同步存按国数据

        internal static void LoadNations(string data)
        {
            try
            {
                if (string.IsNullOrEmpty(data))
                {
                    // 旧档缺 FIA_Tech: 清空后由 Load(旧全局段) 迁移
                    Nations.Clear();
                    _nationsLoaded = false;
                    return;
                }
                if (!data.StartsWith("v3;", StringComparison.Ordinal)) { Load(data); return; }
                var seg = data.Split(';');
                Nations.Clear();
                int lc;
                if (seg.Length > 1 && int.TryParse(seg[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out lc))
                    TechDiffusion.LastCheckDay = lc;
                for (int s = 2; s < seg.Length; s++)
                {
                    var rec = seg[s];
                    if (string.IsNullOrEmpty(rec)) continue;
                    var f = rec.Split('~');
                    if (f.Length < 2) continue;
                    var nat = OfKey(f[0]);
                    try { LoadBitmap(nat, f[1]); } catch { }
                    if (f.Length > 2 && !string.IsNullOrEmpty(f[2]))
                    {
                        foreach (var line in f[2].Split('|'))
                        {
                            var kv = line.Split(':');
                            if (kv.Length < 2) continue;
                            float p;
                            if (float.TryParse(kv[1], NumberStyles.Float, CultureInfo.InvariantCulture, out p)) nat.Progress[kv[0]] = p;
                        }
                    }
                    if (f.Length > 3) FillSlots(nat.Current, f[3]);
                    if (f.Length > 4) FillSlots(nat.Spread, f[4]);
                    if (f.Length > 5 && !string.IsNullOrEmpty(f[5]))
                    {
                        foreach (var line in f[5].Split('|'))
                        {
                            var kv = line.Split(':');
                            if (kv.Length < 2 || string.IsNullOrEmpty(kv[0])) continue;
                            nat.SpreadFrom[kv[0]] = kv[1];
                        }
                    }
                }
                _nationsLoaded = true;
                _lastAiMonth = -9999;
                DLog.Force("科技: 按国读档 国家 " + Nations.Count + " 玩家国已完成 " + Done.Count + " 项");
            }
            catch (Exception ex) { DLog.Force("科技按国读档异常: " + ex.Message); }
        }

        // 旧 v2 全局格式: 完成度拷贝给所有国家作为起点(文档 28.10 A 兼容)
        internal static void Load(string data)
        {
            try
            {
                if (string.IsNullOrEmpty(data)) return;
                if (data.StartsWith("v3;", StringComparison.Ordinal)) { LoadNations(data); return; }
                if (_nationsLoaded) return;   // FIA_Tech 已按国载入 -> 忽略旧段
                var tmpDone = new HashSet<string>();
                var tmpProg = new Dictionary<string, float>();
                var cur = new string[TreeCount];
                var seg = data.Split(';');
                if (seg.Length > 1 && !string.IsNullOrEmpty(seg[1]))
                    foreach (var id in seg[1].Split('|')) if (!string.IsNullOrEmpty(id) && Find(id) != null) tmpDone.Add(id);
                if (seg.Length > 2 && !string.IsNullOrEmpty(seg[2]))
                {
                    foreach (var line in seg[2].Split('|'))
                    {
                        if (string.IsNullOrEmpty(line)) continue;
                        var kv = line.Split(':');
                        if (kv.Length < 2) continue;
                        float p;
                        if (float.TryParse(kv[1], NumberStyles.Float, CultureInfo.InvariantCulture, out p)) tmpProg[kv[0]] = p;
                    }
                }
                if (seg.Length > 3)
                {
                    var f = seg[3].Split(',');
                    for (int t = 0; t < TreeCount && t < f.Length; t++) cur[t] = string.IsNullOrEmpty(f[t]) ? null : f[t];
                }
                // 完成度拷贝给所有国家作为起点
                foreach (var k in Kingdom.All)
                {
                    if (k == null || k.IsEliminated) continue;
                    var nat = OfKey(k.StringId);
                    foreach (var id in tmpDone) nat.Done.Add(id);
                }
                var pn = Of(PlayerKingdom());
                foreach (var id in tmpDone) pn.Done.Add(id);
                foreach (var kv in tmpProg) pn.Progress[kv.Key] = kv.Value;   // 玩家进行中的进度保留
                for (int t = 0; t < TreeCount; t++) if (!string.IsNullOrEmpty(cur[t])) pn.Current[t] = cur[t];
                _lastAiMonth = -9999;
                DLog.Force("科技: 读旧档(全局迁移到各国) 已完成 " + tmpDone.Count + " 项");
            }
            catch (Exception ex) { DLog.Force("科技读档异常: " + ex.Message); }
        }

        private static bool IsEmptySlots(string[] slots)
        {
            if (slots == null) return true;
            for (int i = 0; i < slots.Length; i++) if (!string.IsNullOrEmpty(slots[i])) return false;
            return true;
        }

        private static void FillSlots(string[] slots, string csv)
        {
            if (slots == null || string.IsNullOrEmpty(csv)) return;
            var f = csv.Split(',');
            for (int t = 0; t < slots.Length && t < f.Length; t++) slots[t] = string.IsNullOrEmpty(f[t]) ? null : f[t];
        }

        private static string DoneBitmap(NationTech nat)
        {
            try
            {
                var bytes = new byte[(All.Count + 7) / 8];
                for (int i = 0; i < All.Count; i++)
                {
                    if (!nat.Done.Contains(All[i].Id)) continue;
                    bytes[i >> 3] |= (byte)(1 << (i & 7));
                }
                return Convert.ToBase64String(bytes);
            }
            catch { return ""; }
        }

        private static void LoadBitmap(NationTech nat, string b64)
        {
            if (string.IsNullOrEmpty(b64)) return;
            var bytes = Convert.FromBase64String(b64);
            int max = Math.Min(All.Count, bytes.Length * 8);
            for (int i = 0; i < max; i++)
                if ((bytes[i >> 3] & (1 << (i & 7))) != 0) nat.Done.Add(All[i].Id);
        }

        internal static void Reset()
        {
            Nations.Clear();
            _nationsLoaded = false;
            _lastDay = -1;
            _lastAiMonth = -9999;
            TechDiffusion.LastCheckDay = -1;
            TechDiffusion.ClearCache();
        }
    }

    // 国家机构(文档 24.11; v4.135 照 V3 官方重做): 6 机构由法律启用, 每级效果与成本倍增,
    // 成本 = 人口驱动的官僚负荷, 升级需 1 年/级(V3 官方: 1 bureaucracy per 100k pop, 每级 1 年)
    internal static class Institutions
    {
        internal const int Count = 6;
        internal static readonly string[] Names = { "教育", "医疗", "内务", "治安", "社会保障", "工作安全" };
        internal static readonly string[] Effects = { "识字增长", "民生安抚", "革命压制", "动乱压制", "福利支付", "劳动保护" };
        internal static readonly int[] Level = new int[Count];
        internal static readonly int[] PendingLevel = new int[Count];     // 正在实施的等级
        internal static readonly float[] PendProgress = new float[Count]; // 实施进度(天; 84 天/级)
        internal const int UpgradeDays = 84;    // V3: 每级 1 年
        internal const int MinLoadPerLevel = 10;

        // 文化接纳政策(文档 24.13; 下一轮做成正式法律)
        internal static readonly string[] CultureNames = { "歧视", "隔离", "同化", "多元" };
        internal static int CulturePolicy;
        internal static int CultureChangeDay = -9999;

        // 机构是否被法律启用(V3 官方: institutions are established by laws)
        internal static bool Enabled(int i)
        {
            try
            {
                switch (i)
                {
                    case 0: return LawSystem.Level(LawSystem.LEducation) >= 1;
                    case 1: return LawSystem.Level(LawSystem.LWelfare) >= 1;
                    case 2: return LawSystem.Level(LawSystem.LSecurity) >= 1;
                    case 3: return LawSystem.Level(LawSystem.LPolicing) >= 1;
                    case 4: return LawSystem.Level(LawSystem.LWelfare) >= 1;
                    case 5: return LawSystem.Level(LawSystem.LLabor) >= 1;
                }
            }
            catch { }
            return false;
        }

        internal static string EnablingLawName(int i)
        {
            switch (i)
            {
                case 0: return "教育制度 ≥ 教会学堂";
                case 1: return "济贫制度 ≥ 济贫院";
                case 2: return "国内安全 ≥ 民兵治安";
                case 3: return "治安 ≥ 地方治安";
                case 4: return "济贫制度 ≥ 济贫院";
                default: return "劳工权利 ≥ 监管机构";
            }
        }

        // 最大等级(V3: 由法律与技术决定; 我们: 由对应法律档位决定)
        internal static int MaxLevel(int i)
        {
            try
            {
                switch (i)
                {
                    case 2:
                        return LawSystem.Level(LawSystem.LSecurity) >= 2 ? 5 : 3;
                    case 3:
                        return LawSystem.Level(LawSystem.LPolicing) >= 2 ? 5 : 3;
                    case 0:
                        return LawSystem.Level(LawSystem.LEducation) >= 2 ? 5 : 3;
                    default: return 5;
                }
            }
            catch { return 3; }
        }

        // 官僚容量(V3: Bureaucracy; 我们: 市政/税务类建筑 + 官僚制度法律)
        internal static int Capacity()
        {
            try
            {
                int n = 30;
                var mineIds = Research.OwnSettlementIds();
                foreach (var kv in EconomyWorld.Buildings)
                {
                    var sb = kv.Value;
                    if (sb == null || !mineIds.Contains(kv.Key)) continue;
                    for (int i = 0; i < sb.Groups.Count; i++)
                    {
                        var g = sb.Groups[i];
                        if (g == null || g.Count <= 0) continue;
                        string id = g.DefId ?? "";
                        if (id.Contains("townhall") || id.Contains("tax") || id.Contains("courier")) n += 15 * g.Count;
                    }
                }
                n += LawSystem.Level(LawSystem.LBureaucracy) * 10;
                return n;
            }
            catch { return 30; }
        }

        // 官僚负荷(V3: 每级 1/10 万人口, 最低 10)
        internal static int Load()
        {
            try
            {
                float pop = Pops.TotalPopulation();
                int per = Math.Max(MinLoadPerLevel, (int)(pop / 100000f));
                int sum = 0;
                for (int i = 0; i < Count; i++) sum += Level[i];
                return sum * per;
            }
            catch { return 0; }
        }

        internal static int PerLevelLoad()
        {
            try { return Math.Max(MinLoadPerLevel, (int)(Pops.TotalPopulation() / 100000f)); } catch { return MinLoadPerLevel; }
        }

        // 机构维护费(月度; 财政页展示用; 公式: 官僚负荷 × 20 第纳尔)
        internal static int MaintenanceFee
        {
            get { try { return Load() * 20; } catch { return 0; } }
        }

        internal static bool Upgrading(int i)
        {
            return i >= 0 && i < Count && PendProgress[i] > 0f;
        }

        internal static string StartUpgrade(int i)
        {
            try
            {
                if (i < 0 || i >= Count) return "";
                if (!Enabled(i)) return Names[i] + " 机构尚未启用(需立法: " + EnablingLawName(i) + ")";
                if (Upgrading(i)) return Names[i] + " 正在实施中(还需 " + (int)(UpgradeDays - PendProgress[i]) + " 天)";
                if (Level[i] >= MaxLevel(i)) return Names[i] + " 已达当前法律下的最大等级(" + MaxLevel(i) + ")";
                int cost = PerLevelLoad();
                if (Load() + cost > Capacity())
                    return "官僚负荷不足(需 " + (Load() + cost) + " / 容量 " + Capacity() + "); 建市政厅/税务局或提升官僚制度";
                PendingLevel[i] = Level[i] + 1;
                PendProgress[i] = 0.1f;
                DLog.Force("机构: 开始升级 " + Names[i] + " -> " + PendingLevel[i] + "(需 " + UpgradeDays + " 天)");
                return Names[i] + " 开始扩充到 " + PendingLevel[i] + " 级(需 " + UpgradeDays + " 天, 每年 1 级)";
            }
            catch { return "升级失败"; }
        }

        internal static void Daily(int day)
        {
            try
            {
                // 实施推进(V3: 每级 1 年; 官僚不足则暂停)
                for (int i = 0; i < Count; i++)
                {
                    if (PendProgress[i] <= 0f) continue;
                    if (Load() >= Capacity()) continue;   // 容量不足暂停
                    PendProgress[i] += 1f;
                    if (PendProgress[i] >= UpgradeDays)
                    {
                        Level[i] = PendingLevel[i];
                        PendProgress[i] = 0f;
                        InterestGroups.NotifyPlayer("机构建成: " + Names[i] + " 达到 " + Level[i] + " 级", false);
                        DLog.Force("机构: " + Names[i] + " -> " + Level[i]);
                    }
                }
                // 效果: 医疗/治安/工作安全 -> 激进
                float rad = 0f;
                rad -= Level[1] * 0.0001f;    // 医疗
                rad -= Level[3] * 0.0002f;    // 治安
                rad -= Level[5] * 0.0001f;    // 工作安全
                rad += CultureRadicalDelta();
                if (Math.Abs(rad) > 0.00001f) Pops.ShiftRadicals(rad);
            }
            catch (Exception ex) { DLog.Force("机构日结异常: " + ex.Message); }
        }

        internal static void Monthly()
        {
            try
            {
                // v4.148 V3 官方: 教育机构与科技改为提升"教育机会"(见 Pops.EducationAccessOf), 不再直加识字率
                if (Level[5] > 0) InterestGroups.AddEventMod(6, Level[5]);
                // 维护费(官僚负荷的财政面: 每点负荷 20 第纳尔)
                int cost = MaintenanceFee;
                if (cost > 0)
                {
                    try { EconomyWorld.TreasurySpend(cost); Fiscal.AddCourt(cost); } catch { }
                }
                DLog.Force("机构月结: 负荷 " + Load() + "/" + Capacity() + " 支出 " + cost
                    + " 教育=" + Level[0] + " 医疗=" + Level[1] + " 内务=" + Level[2] + " 治安=" + Level[3]
                    + " 社保=" + Level[4] + " 工安=" + Level[5]);
            }
            catch { }
        }

        // 效果乘数
        internal static float WelfarePayMult() { return 1f + Level[4] * 0.2f; }        // V3 官方: +20%/级
        internal static float RevolutionSlowMult() { return 1f - Level[2] * 0.10f; }   // V3 官方: -10%/级
        internal static float TurmoilReduceMult() { return 1f - Level[3] * 0.10f; }    // V3 官方: -10%/级
        internal static float DangerousWorkReduce() { return 1f - Level[5] * 0.20f; }  // V3 官方: -20%/级
        internal static float TaxMult() { return 1f + LawSystem.Level(LawSystem.LBureaucracy) * 0.02f; }
        internal static float AttractMult()
        {
            switch (CulturePolicy)
            {
                case 0: return 0.6f;
                case 1: return 0.85f;
                case 3: return 1.2f;
                default: return 1f;
            }
        }

        internal static float CultureRadicalDelta()
        {
            switch (CulturePolicy)
            {
                case 0: return 0.0003f;
                case 1: return 0.00015f;
                case 3: return -0.0003f;
                default: return 0f;
            }
        }

        internal static string CultureStatus()
        {
            try
            {
                string s = "文化接纳: " + CultureNames[CulturePolicy] + "(迁移 ×" + AttractMult().ToString("F2") + ")";
                int day = Politics.Today();
                if (day - CultureChangeDay < 84) s += " · 冷却 " + (84 - (day - CultureChangeDay)) + " 天";
                return s;
            }
            catch { return ""; }
        }

        internal static string StatusText()
        {
            try
            {
                return "官僚 " + Load() + "/" + Capacity() + " · 教育 " + Level[0] + " 医疗 " + Level[1] + " 内务 " + Level[2]
                    + " 治安 " + Level[3] + " 社保 " + Level[4] + " 工安 " + Level[5];
            }
            catch { return ""; }
        }

        internal static string SetCulturePolicy(int lv)
        {
            try
            {
                if (lv < 0 || lv > 3) return "";
                if (lv == CulturePolicy) return "当前已是「" + CultureNames[lv] + "」政策";
                int day = Politics.Today();
                if (day - CultureChangeDay < 84) return "政策刚调整过(冷却 " + (84 - (day - CultureChangeDay)) + " 天)";
                if (Politics.Authority < 100f) return "权威不足(需 100)";
                Politics.Authority -= 100f;
                CulturePolicy = lv;
                CultureChangeDay = day;
                InterestGroups.AddEventMod(3, lv >= 3 ? 10 : (lv <= 0 ? -10 : 0));
                InterestGroups.AddEventMod(6, lv >= 2 ? 8 : -6);
                InterestGroups.AddEventMod(2, lv >= 1 ? 6 : -8);
                DLog.Force("文化: 政策 -> " + CultureNames[lv]);
                return "文化接纳政策: " + CultureNames[lv] + "(权威 -100)";
            }
            catch { return "政策调整失败"; }
        }

        internal static string Save()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("v2;");
            for (int i = 0; i < Count; i++) { if (i > 0) sb.Append(','); sb.Append(Level[i]); }
            sb.Append(';');
            for (int i = 0; i < Count; i++) { if (i > 0) sb.Append(','); sb.Append(PendingLevel[i]).Append(':').Append(((int)PendProgress[i])); }
            sb.Append(';');
            sb.Append(CulturePolicy).Append(',').Append(CultureChangeDay).Append(';');
            return sb.ToString();
        }

        internal static void Load(string data)
        {
            try
            {
                if (string.IsNullOrEmpty(data)) return;
                var seg = data.Split(';');
                if (seg.Length > 1)
                {
                    var f = seg[1].Split(',');
                    for (int i = 0; i < Count && i < f.Length; i++)
                    {
                        int x;
                        if (int.TryParse(f[i], out x)) Level[i] = Math.Max(0, Math.Min(5, x));
                    }
                }
                if (seg.Length > 2 && !string.IsNullOrEmpty(seg[2]))
                {
                    var f = seg[2].Split(',');
                    for (int i = 0; i < Count && i < f.Length; i++)
                    {
                        var kv = f[i].Split(':');
                        int x;
                        if (kv.Length > 0 && int.TryParse(kv[0], out x)) PendingLevel[i] = x;
                        if (kv.Length > 1 && int.TryParse(kv[1], out x)) PendProgress[i] = x;
                    }
                }
                if (seg.Length > 3)
                {
                    var f = seg[3].Split(',');
                    int x;
                    if (f.Length > 0 && int.TryParse(f[0], out x)) CulturePolicy = Math.Max(0, Math.Min(3, x));
                    if (f.Length > 1 && int.TryParse(f[1], out x)) CultureChangeDay = x;
                }
                DLog.Force("机构: 读档 " + StatusText() + " " + CultureStatus());
            }
            catch { }
        }

        internal static void Reset()
        {
            for (int i = 0; i < Count; i++) { Level[i] = 0; PendingLevel[i] = 0; PendProgress[i] = 0f; }
            CulturePolicy = 2;
            CultureChangeDay = -9999;
        }
    }
}