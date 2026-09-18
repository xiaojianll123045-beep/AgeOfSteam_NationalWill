using System;
using System.Collections.Generic;

namespace FeudalInternalAffairs
{
    // 职业定义(文档 19.2.1; 15 职业 + 失业)
    internal class ProfessionDef
    {
        internal string Id;
        internal string Name;
        internal int Stratum;         // 0=下层 1=中层 2=上层
        internal float WageFactor;    // 基础工资系数
        internal float Political;     // 政治力量
        internal float InvestShare;   // 投资池贡献率
        internal float ExpectedSol;   // 期望生活水平(基准, 19.6.4)
        internal float MinLiteracy;   // 转职识字门槛
        internal float ConsumeMult;   // 消费系数(农民 0.05)
        internal bool CanMigrate;     // 农民不可迁移
        internal bool CanChangeJob;   // 农民不可转职
    }

    // 职业总表 + 文化痴迷/禁忌(文档 19.2 / 19.4.4)
    internal static class PopDefs
    {
        internal const string Peasants = "peasants";
        internal const string Laborers = "laborers";
        internal const string Farmers = "farmers";
        internal const string Miners = "miners";
        internal const string Machinists = "machinists";
        internal const string Engineers = "engineers";
        internal const string Clerks = "clerks";
        internal const string Shopkeepers = "shopkeepers";
        internal const string Aristocrats = "aristocrats";
        internal const string Capitalists = "capitalists";
        internal const string Bureaucrats = "bureaucrats";
        internal const string Academics = "academics";
        internal const string Officers = "officers";
        internal const string Soldiers = "soldiers";
        internal const string Clergymen = "clergymen";
        internal const string Unemployed = "unemployed";

        // 常量(文档 19.15)
        internal const float BaseWage = 4.0f;        // 基准工资 第纳尔/日/人
        internal const float WorkforceRatio = 0.25f; // 工作比例
        internal const float DependentRatio = 0.50f; // 依赖人口消费比
        internal const float WealthGainRate = 0.20f; // 财富增长速率(周)
        internal const float WealthGate = 1.02f;     // 财富门槛系数
        // 实机标定(19.12): 曾试 ×6, 实测把下层需求满足打到 23%/-56% -> 撤回, 保持 1
        // (供需失衡与建筑盈亏仍需按 19.12 专项校准: 见 v3.6 变更记录)
        internal const float DemandScale = 1f;

        internal static readonly List<ProfessionDef> All = new List<ProfessionDef>
        {
            new ProfessionDef { Id = Peasants,    Name = "农民",   Stratum = 0, WageFactor = 0.2f, Political = 0.05f, InvestShare = 0.00f, ExpectedSol = 4f,  MinLiteracy = 0f,   ConsumeMult = 0.05f, CanMigrate = false, CanChangeJob = false },
            new ProfessionDef { Id = Laborers,    Name = "劳工",   Stratum = 0, WageFactor = 1.0f, Political = 0.10f, InvestShare = 0.00f, ExpectedSol = 5f,  MinLiteracy = 0f,   ConsumeMult = 1f,    CanMigrate = true,  CanChangeJob = true },
            new ProfessionDef { Id = Farmers,     Name = "农场主", Stratum = 1, WageFactor = 1.3f, Political = 0.15f, InvestShare = 0.00f, ExpectedSol = 8f,  MinLiteracy = 0.2f, ConsumeMult = 1f,    CanMigrate = true,  CanChangeJob = true },
            new ProfessionDef { Id = Miners,      Name = "矿工",   Stratum = 0, WageFactor = 1.2f, Political = 0.10f, InvestShare = 0.00f, ExpectedSol = 5f,  MinLiteracy = 0f,   ConsumeMult = 1f,    CanMigrate = true,  CanChangeJob = true },
            new ProfessionDef { Id = Machinists,  Name = "机械工", Stratum = 0, WageFactor = 1.5f, Political = 0.15f, InvestShare = 0.10f, ExpectedSol = 7f,  MinLiteracy = 0.2f, ConsumeMult = 1f,    CanMigrate = true,  CanChangeJob = true },
            new ProfessionDef { Id = Engineers,   Name = "工程师", Stratum = 1, WageFactor = 2.0f, Political = 0.20f, InvestShare = 0.00f, ExpectedSol = 10f, MinLiteracy = 0.5f, ConsumeMult = 1f,    CanMigrate = true,  CanChangeJob = true },
            new ProfessionDef { Id = Clerks,      Name = "职员",   Stratum = 1, WageFactor = 1.4f, Political = 0.15f, InvestShare = 0.00f, ExpectedSol = 8f,  MinLiteracy = 0.3f, ConsumeMult = 1f,    CanMigrate = true,  CanChangeJob = true },
            new ProfessionDef { Id = Shopkeepers, Name = "店主",   Stratum = 1, WageFactor = 1.6f, Political = 0.20f, InvestShare = 0.05f, ExpectedSol = 9f,  MinLiteracy = 0.3f, ConsumeMult = 1f,    CanMigrate = true,  CanChangeJob = true },
            new ProfessionDef { Id = Aristocrats, Name = "贵族",   Stratum = 2, WageFactor = 3.5f, Political = 1.00f, InvestShare = 0.40f, ExpectedSol = 15f, MinLiteracy = 0.4f, ConsumeMult = 1f,    CanMigrate = true,  CanChangeJob = true },
            new ProfessionDef { Id = Capitalists, Name = "资本家", Stratum = 2, WageFactor = 4.0f, Political = 0.80f, InvestShare = 0.50f, ExpectedSol = 18f, MinLiteracy = 0.6f, ConsumeMult = 1f,    CanMigrate = true,  CanChangeJob = true },
            new ProfessionDef { Id = Bureaucrats, Name = "官僚",   Stratum = 1, WageFactor = 2.2f, Political = 0.30f, InvestShare = 0.00f, ExpectedSol = 11f, MinLiteracy = 0.5f, ConsumeMult = 1f,    CanMigrate = true,  CanChangeJob = true },
            new ProfessionDef { Id = Academics,   Name = "学者",   Stratum = 1, WageFactor = 2.0f, Political = 0.25f, InvestShare = 0.00f, ExpectedSol = 12f, MinLiteracy = 0.6f, ConsumeMult = 1f,    CanMigrate = true,  CanChangeJob = true },
            new ProfessionDef { Id = Officers,    Name = "军官",   Stratum = 2, WageFactor = 3.0f, Political = 2.00f, InvestShare = 0.00f, ExpectedSol = 12f, MinLiteracy = 0.4f, ConsumeMult = 1f,    CanMigrate = true,  CanChangeJob = true },
            new ProfessionDef { Id = Soldiers,    Name = "士兵",   Stratum = 0, WageFactor = 1.1f, Political = 0.10f, InvestShare = 0.00f, ExpectedSol = 6f,  MinLiteracy = 0f,   ConsumeMult = 1f,    CanMigrate = true,  CanChangeJob = true },
            new ProfessionDef { Id = Clergymen,   Name = "神职",   Stratum = 2, WageFactor = 1.8f, Political = 0.50f, InvestShare = 0.00f, ExpectedSol = 10f, MinLiteracy = 0.4f, ConsumeMult = 1f,    CanMigrate = true,  CanChangeJob = true },
            new ProfessionDef { Id = Unemployed,  Name = "失业",   Stratum = 0, WageFactor = 0.0f, Political = 0.05f, InvestShare = 0.00f, ExpectedSol = 3f,  MinLiteracy = 0f,   ConsumeMult = 1f,    CanMigrate = true,  CanChangeJob = true }
        };

        private static readonly Dictionary<string, ProfessionDef> Index = BuildIndex();

        private static Dictionary<string, ProfessionDef> BuildIndex()
        {
            var d = new Dictionary<string, ProfessionDef>();
            foreach (var p in All) d[p.Id] = p;
            return d;
        }

        internal static ProfessionDef Get(string id)
        {
            ProfessionDef p;
            return (id != null && Index.TryGetValue(id, out p)) ? p : Index[Laborers];
        }

        internal static string NameOf(string id) { return Get(id).Name; }

        // ---- 文化痴迷(obsession): 权重 ×2 + 需求额 +25% ----
        internal static string ObsessionOf(string culture)
        {
            switch (culture)
            {
                case "empire": return FeudalGoods.Wine;      // 帝国迷恋葡萄酒
                case "aserai": return FeudalGoods.Spice;     // 阿塞莱迷恋香料
                case "khuzait": return "horse";              // 库塞特迷恋马匹
                case "sturgia": return FeudalGoods.Charcoal; // 斯特吉亚迷恋木炭
                case "battania": return FeudalGoods.Hardwood;// 巴旦尼亚迷恋木材
                case "vlandia": return FeudalGoods.Linen;    // 瓦兰迪亚迷恋亚麻布
                default: return null;
            }
        }

        // ---- 文化禁忌(taboo): 需求 ×0.5 且需求额 −25% ----
        internal static bool IsTaboo(string culture, string goodId)
        {
            if (string.IsNullOrEmpty(culture) || string.IsNullOrEmpty(goodId)) return false;
            // 帝国 ↔ 蛮族文化 互相禁忌对方特产; 库塞特 ↔ 帝国
            bool empire = culture == "empire";
            bool barbar = culture == "battania" || culture == "sturgia" || culture == "khuzait";
            if (empire && (goodId == FeudalGoods.Charcoal || goodId == FeudalGoods.Hardwood || goodId == "horse")) return true;
            if (barbar && (goodId == FeudalGoods.Wine || goodId == FeudalGoods.Linen || goodId == FeudalGoods.Pottery)) return true;
            if (culture == "khuzait" && goodId == FeudalGoods.Wine) return true;
            if (culture == "aserai" && goodId == FeudalGoods.Beer) return true;
            return false;
        }

        // 阵营(19.11.2 政治力量聚合 / 文化统计)
        internal static int StratumOf(string professionId) { return Get(professionId).Stratum; }
        internal static string StratumName(int s)
        {
            return s == 2 ? "上层" : (s == 1 ? "中层" : "下层");
        }
    }
}
