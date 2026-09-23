using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace FeudalInternalAffairs
{
    // 一条游说(花金币影响集团态度; v4.198, 用上 6 张 fia_lobby_* 图标)
    internal class LobbyDef
    {
        internal string Id, Name, Icon, Desc, Effect;
        internal int Gold;
        internal int Days;          // 冷却
        internal int[] Att;         // 8 集团态度
        internal float Authority, Legitimacy;
    }

    internal static class Lobbying
    {
        internal static readonly List<LobbyDef> All = new List<LobbyDef>
        {
            new LobbyDef { Id = "fund_lobbies", Name = "资助说客", Icon = "fia_lobby_fund_lobbies", Desc = "出钱请说客在宫廷与地方走动",
                Effect = "王权 +8, 地方 +4", Gold = 3000, Days = 14, Att = new[] { 8, 0, 4, 0, 0, 0, 0, 0 } },
            new LobbyDef { Id = "pro_country", Name = "亲本国宣传", Icon = "fia_lobby_pro_country", Desc = "宣扬国家荣耀与秩序",
                Effect = "军队 +8, 教会 +6", Gold = 2500, Days = 14, Att = new[] { 2, 0, 0, 6, 0, 0, 0, 8 } },
            new LobbyDef { Id = "anti_country", Name = "反本国宣传", Icon = "fia_lobby_anti_country", Desc = "煽动对官府的不满(危险)",
                Effect = "下层 +8, 行会 +6, 合法性 -1", Gold = 2500, Days = 14, Att = new[] { -4, 0, 0, 0, 0, 6, 8, 0 }, Legitimacy = -1f },
            new LobbyDef { Id = "loyalist", Name = "保皇宣传", Icon = "fia_lobby_loyalist", Desc = "鼓吹王室正统与忠诚",
                Effect = "王权 +10, 权贵 +6", Gold = 3000, Days = 14, Att = new[] { 10, 6, 0, 0, 0, 0, 0, 2 } },
            new LobbyDef { Id = "independence", Name = "自治鼓动", Icon = "fia_lobby_independence", Desc = "支持地方自治与乡绅权力",
                Effect = "地方 +8, 下层 +6, 权威 -2", Gold = 2500, Days = 14, Att = new[] { -6, 0, 8, 0, 0, 0, 6, 0 }, Authority = -2f },
            new LobbyDef { Id = "appeasement", Name = "绥靖宣传", Icon = "fia_lobby_appeasement", Desc = "对外示好, 削减军备",
                Effect = "商帮 +6, 教会 +4, 军队 -6", Gold = 2500, Days = 14, Att = new[] { 0, 0, 0, 4, 6, 0, 0, -6 } }
        };

        private static readonly Dictionary<string, int> LastDay = new Dictionary<string, int>();

        internal static LobbyDef Def(string id)
        {
            for (int i = 0; i < All.Count; i++) if (All[i].Id == id) return All[i];
            return null;
        }

        internal static int CooldownLeft(string id)
        {
            try
            {
                int last;
                if (LastDay.TryGetValue(id, out last))
                {
                    var d = Def(id);
                    int cd = d != null ? d.Days : 14;
                    return Math.Max(0, cd - (Politics.Today() - last));
                }
            }
            catch { }
            return 0;
        }

        internal static string Run(string id)
        {
            try
            {
                var d = Def(id);
                if (d == null) return "游说无效。";
                int cd = CooldownLeft(id);
                if (cd > 0) return "「" + d.Name + "」还需 " + cd + " 日冷却。";
                var hero = TaleWorlds.CampaignSystem.Hero.MainHero;
                if (hero == null) return "找不到君主。";
                if (hero.Gold < d.Gold) return "金币不足(需 " + d.Gold + ", 现有 " + hero.Gold + ")。";
                hero.ChangeHeroGold(-d.Gold);
                LastDay[id] = Politics.Today();
                if (d.Att != null)
                    for (int g = 0; g < d.Att.Length && g < InterestGroups.GroupCount; g++)
                        if (d.Att[g] != 0) InterestGroups.AddEventMod(g, d.Att[g]);
                if (Math.Abs(d.Authority) > 0.01f) Politics.Authority = Math.Max(0f, Politics.Authority + d.Authority);
                if (Math.Abs(d.Legitimacy) > 0.01f) Politics.Legitimacy = Politics.ClampF(Politics.Legitimacy + d.Legitimacy, 0f, 100f);
                DLog.Force("游说: " + d.Name + "(金币 -" + d.Gold + ")");
                return "已发动「" + d.Name + "」(金币 -" + d.Gold + "): " + d.Effect;
            }
            catch (Exception ex) { return "游说失败: " + ex.Message; }
        }

        // ================= v4.21x: AI 游说(AI 花国库推进自己支持的法律) =================
        private static readonly Dictionary<string, int> AiLast = new Dictionary<string, int>();   // "王国|游说" -> 日
        private static int _aiLastDay = -1;

        internal static void AiMonthly(int day)
        {
            try
            {
                if (day < _aiLastDay) AiLast.Clear();   // 新档/读档 -> 重置
                _aiLastDay = day;
                var pk = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                foreach (var k in Kingdom.All)
                {
                    if (k == null || k.IsEliminated) continue;
                    if (pk != null && ReferenceEquals(pk, k)) continue;
                    try { AiStep(k, day); } catch { }
                }
            }
            catch (Exception ex) { DLog.Force("AI 游说月结异常: " + ex.Message); }
        }

        private static void AiStep(Kingdom k, int day)
        {
            if (WarEconomy.IsCrisis(k)) return;
            try { AiAgenda.Ensure(k, day); } catch { }
            if (MBRandom.RandomFloat > 0.4f) return;
            // v5.x: 议程优先(为要推的法案选对应游说), 无议程再按性格兜底
            LobbyDef d = null;
            try { d = Def(AiAgenda.LobbyIdFor(k)); } catch { }
            if (d == null)
            {
                int agg = AiPersonality.AggressionOf(k);
                int dev = AiPersonality.DevelopmentOf(k);
                int com = AiPersonality.CommerceOf(k);
                if (agg >= 65) d = MBRandom.RandomFloat < 0.6f ? Def("pro_country") : Def("loyalist");
                else if (com >= 65) d = MBRandom.RandomFloat < 0.5f ? Def("fund_lobbies") : Def("appeasement");
                else if (dev >= 60) d = Def("fund_lobbies");
                else d = MBRandom.RandomFloat < 0.5f ? Def("pro_country") : Def("fund_lobbies");
            }
            if (d == null) return;
            string key = k.StringId + "|" + d.Id;
            int last;
            if (AiLast.TryGetValue(key, out last) && day - last < d.Days) return;
            // v5.x: 统一大脑政治预算(预算紧且国库不宽裕 -> 不游说)
            float gold = WarEconomy.GoldOfPublic(k);
            float polBudget = 0f;
            try { polBudget = AiBrain.BudgetFor(k, "pol", gold); } catch { }
            if (polBudget > 1f && polBudget < d.Gold && gold < 12000f) return;
            if (gold < d.Gold + 800f) return;
            WarEconomy.SpendPublic(k, d.Gold);
            AiLast[key] = day;
            try { if (d.Att != null) AiEconomyDeep.ShiftPublicStance(k, d.Att); } catch { }
            if (Math.Abs(d.Authority) > 0.01f) Decrees.AiAuthAdd(k, d.Authority);
            // 游说推进议程法案: 给 AI 议会下次表决按类别加票
            string cat = null;
            try { cat = AiAgenda.LobbyCategoryFor(k); } catch { }
            if (string.IsNullOrEmpty(cat))
                cat = (d.Id == "fund_lobbies" || d.Id == "appeasement") ? "经济"
                    : ((d.Id == "anti_country" || d.Id == "independence") ? "民权" : "权力");
            try { Parliament.AiLobbyPush(k, cat); } catch { }
            AiAgenda.LogPol(k, "游说「" + d.Name + "」(金 " + d.Gold + ", 推" + cat + "类法案)");
        }

        internal static string StatusText()
        {
            try
            {
                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < All.Count; i++)
                {
                    int cd = CooldownLeft(All[i].Id);
                    sb.Append(All[i].Name);
                    sb.Append(cd > 0 ? ("(冷却 " + cd + " 日)") : ("(" + All[i].Gold + " 金)"));
                    if (i < All.Count - 1) sb.Append(" · ");
                }
                return sb.ToString();
            }
            catch { return ""; }
        }

        internal static void ShowUi()
        {
            try
            {
                var hero = TaleWorlds.CampaignSystem.Hero.MainHero;
                int gold = hero != null ? hero.Gold : 0;
                var opts = new List<InquiryElement>();
                for (int i = 0; i < All.Count; i++)
                {
                    var d = All[i];
                    int cd = CooldownLeft(d.Id);
                    string label = d.Name + (cd > 0 ? ("(冷却 " + cd + " 日)") : ("(" + d.Gold + " 金)"));
                    opts.Add(new InquiryElement(d.Id, label, null, cd <= 0 && gold >= d.Gold, d.Desc + "\n" + d.Effect));
                }
                PanelInputGuard.ShowPopup(new MultiSelectionInquiryData(
                    "游说",
                    "君主金币: " + gold + "\n游说可临时改变集团态度(每条 14 日冷却)\n" + StatusText(),
                    opts, true, 1, 1, "发动", "取消",
                    delegate (List<InquiryElement> sel)
                    {
                        if (sel == null || sel.Count == 0) return;
                        string msg = Run(sel[0].Identifier as string);
                        try { MapSelection.Message(msg); } catch { }
                    }, null, null, false));
            }
            catch (Exception ex) { DLog.Force("游说界面失败: " + ex.Message); }
        }

        internal static string Save()
        {
            try
            {
                var sb = new System.Text.StringBuilder();
                foreach (var kv in LastDay) sb.Append(kv.Key).Append(',').Append(kv.Value).Append(';');
                sb.Append('~');   // v4.21x: AI 冷却段(旧档无此段)
                foreach (var kv in AiLast) sb.Append(kv.Key).Append(',').Append(kv.Value).Append(';');
                return sb.ToString();
            }
            catch { return ""; }
        }

        internal static void Load(string data)
        {
            try
            {
                LastDay.Clear(); AiLast.Clear();
                if (string.IsNullOrEmpty(data)) return;
                int tilde = data.IndexOf('~');
                string core = tilde >= 0 ? data.Substring(0, tilde) : data;
                foreach (var seg in core.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var f = seg.Split(',');
                    int d;
                    if (f.Length == 2 && int.TryParse(f[1], out d)) LastDay[f[0]] = d;
                }
                if (tilde >= 0)
                {
                    foreach (var seg in data.Substring(tilde + 1).Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        var f = seg.Split(',');
                        int d2;
                        if (f.Length == 2 && int.TryParse(f[1], out d2)) AiLast[f[0]] = d2;
                    }
                }
            }
            catch { }
        }
    }
}
