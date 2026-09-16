using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;

namespace FeudalInternalAffairs
{
    // 一个国策的定义
    internal class FocusDefinition
    {
        internal string Id;
        internal string Name;
        internal string Icon;        // 游戏内已有的 sprite 名
        internal string Description;
        internal string Effects;
        internal float PosX, PosY;   // 基础坐标(未缩放)
        internal string[] Requires;  // 前置国策 id
        internal int Days = 30;
    }

    // 国策树数据与状态(完整版示例树; 状态暂不存档)
    internal static class FocusTreeData
    {
        internal const float NodeWidth = 200f;
        internal const float NodeHeight = 58f;

        internal static readonly List<FocusDefinition> All = new List<FocusDefinition>
        {
            // ===== 根 =====
            new FocusDefinition { Id = "f_root", Name = "国家意志", Icon = "General\\Icons\\Morale",
                Description = "确立国家意志对全国的统治根基, 群臣归心。",
                Effects = "影响力 +50, 全国领主关系 +2", PosX = 560, PosY = 30, Requires = null, Days = 10 },

            // ===== 第一层 =====
            new FocusDefinition { Id = "f_econ", Name = "恢复生产", Icon = "General\\Icons\\Prosperity",
                Description = "修复战乱破坏的农庄与作坊, 让百姓重新耕作。",
                Effects = "金币 +5000", PosX = 200, PosY = 170, Requires = new[] { "f_root" }, Days = 20 },
            new FocusDefinition { Id = "f_army", Name = "扩充常备军", Icon = "General\\Icons\\Militia",
                Description = "扩编常备军, 加强边境守备与巡逻。",
                Effects = "影响力 +50", PosX = 560, PosY = 170, Requires = new[] { "f_root" }, Days = 20 },
            new FocusDefinition { Id = "f_diplo", Name = "遣使四方", Icon = "General\\Icons\\parley_icon",
                Description = "向四方邻邦遣使, 缓和紧张局势。",
                Effects = "影响力 +30", PosX = 920, PosY = 170, Requires = new[] { "f_root" }, Days = 20 },

            // ===== 第二层: 经济线 =====
            new FocusDefinition { Id = "f_farm", Name = "兴修水利", Icon = "General\\Icons\\Food",
                Description = "在河谷兴修水利, 保证粮产稳定。",
                Effects = "金币 +8000", PosX = 60, PosY = 310, Requires = new[] { "f_econ" }, Days = 25 },
            new FocusDefinition { Id = "f_trade", Name = "重开商路", Icon = "General\\Icons\\ShopIcons\\smithy_large",
                Description = "与邻邦重开商路, 互通有无。",
                Effects = "金币 +8000", PosX = 300, PosY = 310, Requires = new[] { "f_econ" }, Days = 25 },
            // 军事线
            new FocusDefinition { Id = "f_drill", Name = "操练新兵", Icon = "General\\EquipmentIcons\\equipment_type_one_handed",
                Description = "严加操练, 提升部队素质与士气。",
                Effects = "全军士气 +5", PosX = 540, PosY = 310, Requires = new[] { "f_army" }, Days = 30 },
            new FocusDefinition { Id = "f_fort", Name = "加固城防", Icon = "General\\Icons\\Walls",
                Description = "加固各处城墙与堡垒, 稳固防守。",
                Effects = "全国城镇忠诚度 +5", PosX = 780, PosY = 310, Requires = new[] { "f_army" }, Days = 30 },
            // 外交线
            new FocusDefinition { Id = "f_marry", Name = "联姻结好", Icon = "General\\EquipmentIcons\\equipment_type_banner",
                Description = "以联姻结好邻邦, 争取外援。",
                Effects = "影响力 +40", PosX = 1020, PosY = 310, Requires = new[] { "f_diplo" }, Days = 25 },

            // ===== 第三层 =====
            new FocusDefinition { Id = "f_granary", Name = "广建粮仓", Icon = "General\\Icons\\Garrison",
                Description = "广建粮仓, 以备战荒。",
                Effects = "全国城镇繁荣度 +1", PosX = 60, PosY = 450, Requires = new[] { "f_farm" }, Days = 30 },
            new FocusDefinition { Id = "f_market", Name = "市集兴旺", Icon = "General\\Icons\\hideout_militia",
                Description = "整顿市集, 鼓励工商。",
                Effects = "金币 +12000", PosX = 300, PosY = 450, Requires = new[] { "f_trade" }, Days = 30 },
            new FocusDefinition { Id = "f_veteran", Name = "老兵归营", Icon = "General\\TroopTypeIcons\\icon_troop_type_cavalry",
                Description = "召回流散老兵, 充实军伍。",
                Effects = "影响力 +80", PosX = 540, PosY = 450, Requires = new[] { "f_drill" }, Days = 35 },
            new FocusDefinition { Id = "f_wall", Name = "城垣重修", Icon = "General\\Icons\\icon_issue_available_square",
                Description = "重修城垣, 使敌骑不得轻犯。",
                Effects = "全国城镇忠诚度 +5", PosX = 780, PosY = 450, Requires = new[] { "f_fort" }, Days = 35 },
            new FocusDefinition { Id = "f_envoy", Name = "常驻使节", Icon = "Encyclopedia\\icon_search",
                Description = "在邻邦常驻使节, 维系邦交。",
                Effects = "影响力 +40", PosX = 1020, PosY = 450, Requires = new[] { "f_marry" }, Days = 30 },

            // ===== 终局 =====
            new FocusDefinition { Id = "f_prosper", Name = "太平盛世", Icon = "General\\Icons\\bounty_icon_Card",
                Description = "仓廪实而知礼节, 国家进入太平盛世。",
                Effects = "全国城镇繁荣度 +2", PosX = 180, PosY = 590, Requires = new[] { "f_granary", "f_market" }, Days = 45 },
            new FocusDefinition { Id = "f_empire", Name = "帝国军威", Icon = "General\\Icons\\Morale",
                Description = "军威远播, 四邻不敢窥伺。",
                Effects = "影响力 +150", PosX = 660, PosY = 590, Requires = new[] { "f_veteran", "f_wall" }, Days = 45 },
            new FocusDefinition { Id = "f_hegemony", Name = "霸业初成", Icon = "Clan\\clan_header",
                Description = "内政修明、军威远播, 霸业初成。",
                Effects = "全国城镇繁荣度 +2, 忠诚度 +5", PosX = 920, PosY = 590, Requires = new[] { "f_envoy", "f_empire" }, Days = 60 },
        };

        internal static readonly HashSet<string> Completed = new HashSet<string>();
        internal static readonly Dictionary<string, int> InProgress = new Dictionary<string, int>();

        internal static FocusDefinition Get(string id)
        {
            foreach (var f in All) if (f.Id == id) return f;
            return null;
        }

        internal static bool IsCompleted(string id) { return Completed.Contains(id); }
        internal static bool IsInProgress(string id) { return InProgress.ContainsKey(id); }

        internal static bool PrerequisitesMet(FocusDefinition f)
        {
            if (f.Requires == null || f.Requires.Length == 0) return true;
            foreach (var r in f.Requires) if (!Completed.Contains(r)) return false;
            return true;
        }

        internal static string RequirementText(FocusDefinition f)
        {
            if (f.Requires == null || f.Requires.Length == 0) return "没有前置国策";
            var parts = new List<string>();
            foreach (var r in f.Requires)
            {
                var d = Get(r);
                parts.Add((d != null ? d.Name : r) + (Completed.Contains(r) ? "(已完成)" : "(未完成)"));
            }
            return string.Join("、", parts);
        }

        internal static void Start(FocusDefinition f)
        {
            if (f == null || Completed.Contains(f.Id) || InProgress.ContainsKey(f.Id)) return;
            InProgress[f.Id] = Math.Max(1, f.Days);
        }

        internal static FocusDefinition TickDay()
        {
            if (InProgress.Count == 0) return null;
            string doneId = null;
            var keys = new List<string>(InProgress.Keys);
            foreach (var id in keys)
            {
                int left = InProgress[id] - 1;
                if (left <= 0) { InProgress.Remove(id); doneId = id; break; }
                InProgress[id] = left;
            }
            if (doneId == null) return null;
            Completed.Add(doneId);
            var def = Get(doneId);
            if (def != null) ApplyEffects(def);
            return def;
        }

        // ===== 效果 =====
        private static void ApplyEffects(FocusDefinition f)
        {
            try
            {
                switch (f.Id)
                {
                    case "f_root":
                        Influence(50);
                        foreach (var h in OurLords()) ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, h, 2, false);
                        break;
                    case "f_econ": Gold(5000); break;
                    case "f_army": Influence(50); break;
                    case "f_diplo": Influence(30); break;
                    case "f_farm": Gold(8000); break;
                    case "f_trade": Gold(8000); break;
                    case "f_drill": Influence(60); break;
                    case "f_fort": Loyalty(5f); break;
                    case "f_marry": Influence(40); break;
                    case "f_granary": Prosperity(1f); break;
                    case "f_market": Gold(12000); break;
                    case "f_veteran": Influence(80); break;
                    case "f_wall": Loyalty(5f); break;
                    case "f_envoy": Influence(40); break;
                    case "f_prosper": Prosperity(2f); break;
                    case "f_empire": Influence(150); break;
                    case "f_hegemony": Prosperity(2f); Loyalty(5f); break;
                }
            }
            catch (Exception ex) { DLog.Force("国策效果失败: " + ex.Message); }
        }

        private static void Influence(float amount)
        {
            if (Clan.PlayerClan != null) ChangeClanInfluenceAction.Apply(Clan.PlayerClan, amount);
        }

        private static void Gold(int amount)
        {
            if (Hero.MainHero != null) GiveGoldAction.ApplyBetweenCharacters(null, Hero.MainHero, amount, true);
        }

        private static void Prosperity(float amount)
        {
            foreach (var t in Town.AllTowns) if (t != null && t.OwnerClan == Clan.PlayerClan) t.Prosperity += amount;
        }

        private static void Loyalty(float amount)
        {
            foreach (var t in Town.AllTowns) if (t != null && t.OwnerClan == Clan.PlayerClan) t.Loyalty += amount;
        }

        private static List<Hero> OurLords()
        {
            var list = new List<Hero>();
            try
            {
                var b = NationalWillOrders.Behavior;
                var k = b != null ? b.NationKingdom : null;
                if (k == null) return list;
                foreach (var h in k.AliveLords) if (h != null && !h.IsDead) list.Add(h);
            }
            catch { }
            return list;
        }
    }
}
