using System;
using System.Collections.Generic;
using TaleWorlds.Library;

namespace FeudalInternalAffairs
{
    // 意识形态(文档 24.2 / 美术清单 3.6; v4.195)
    internal class IdeologyDef
    {
        internal string Id, Name, Icon;
    }

    // 政党(12 个)
    internal class PartyDef
    {
        internal string Id, Name, Icon, Desc;
        internal string[] Ideologies;      // 该党的意识形态构成
        internal float AuthorityMonthly;   // 执政时 权威/月
        internal float LegitMonthly;       // 执政时 合法性/月
        internal int ArmySatis;            // 执政时 军队满意度事件修正
        internal int LowSatis;             // 执政时 下层满意度事件修正
    }

    // 政党与意识形态系统: IG 有意识形态, 政党由意识形态聚合, 支持度 = Σ(对齐 IG 的政治力量)
    internal static class Parties
    {
        internal static readonly List<IdeologyDef> Ideologies = new List<IdeologyDef>
        {
            new IdeologyDef { Id = "agrarian", Name = "农业主义", Icon = "fia_ideo_agrarian" },
            new IdeologyDef { Id = "anti_clerical", Name = "反教权", Icon = "fia_ideo_anti_clerical" },
            new IdeologyDef { Id = "anti_slavery", Name = "废奴", Icon = "fia_ideo_anti_slavery" },
            new IdeologyDef { Id = "egalitarian", Name = "平等", Icon = "fia_ideo_egalitarian" },
            new IdeologyDef { Id = "hierarchic", Name = "等级", Icon = "fia_ideo_hierarchic" },
            new IdeologyDef { Id = "individualist", Name = "个人主义", Icon = "fia_ideo_individualist" },
            new IdeologyDef { Id = "isolationist", Name = "孤立主义", Icon = "fia_ideo_isolationist" },
            new IdeologyDef { Id = "jingoist", Name = "主战", Icon = "fia_ideo_jingoist" },
            new IdeologyDef { Id = "laissez_faire", Name = "自由放任", Icon = "fia_ideo_laissez_faire" },
            new IdeologyDef { Id = "liberal", Name = "自由主义", Icon = "fia_ideo_liberal" },
            new IdeologyDef { Id = "loyalist", Name = "保皇", Icon = "fia_ideo_loyalist" },
            new IdeologyDef { Id = "meritocratic", Name = "贤能", Icon = "fia_ideo_meritocratic" },
            new IdeologyDef { Id = "moralist", Name = "道德主义", Icon = "fia_ideo_moralist" },
            new IdeologyDef { Id = "particularist", Name = "特殊主义", Icon = "fia_ideo_particularist" },
            new IdeologyDef { Id = "paternalistic", Name = "家长制", Icon = "fia_ideo_paternalistic" },
            new IdeologyDef { Id = "patriarchal", Name = "父权", Icon = "fia_ideo_patriarchal" },
            new IdeologyDef { Id = "patriotic", Name = "爱国", Icon = "fia_ideo_patriotic" },
            new IdeologyDef { Id = "pious", Name = "虔信", Icon = "fia_ideo_pious" },
            new IdeologyDef { Id = "plutocratic", Name = "财阀", Icon = "fia_ideo_plutocratic" },
            new IdeologyDef { Id = "populist", Name = "民粹", Icon = "fia_ideo_populist" },
            new IdeologyDef { Id = "proletarian", Name = "无产阶级", Icon = "fia_ideo_proletarian" },
            new IdeologyDef { Id = "reactionary", Name = "反动", Icon = "fia_ideo_reactionary" },
            new IdeologyDef { Id = "republican", Name = "共和", Icon = "fia_ideo_republican" }
        };

        // 8 个利益集团 -> 主导意识形态(与美术清单一一对应)
        internal static readonly string[] GroupIdeology =
        {
            "loyalist",        // 0 王权
            "plutocratic",     // 1 权贵
            "paternalistic",   // 2 地方绅士
            "pious",           // 3 教会
            "laissez_faire",   // 4 商帮
            "proletarian",     // 5 行会
            "agrarian",        // 6 农民
            "jingoist"         // 7 军队
        };

        internal static readonly List<PartyDef> All = new List<PartyDef>
        {
            new PartyDef { Id = "agrarian", Name = "农业党", Icon = "fia_party_agrarian", Desc = "护农、重乡土",
                Ideologies = new[] { "agrarian", "paternalistic", "particularist" }, AuthorityMonthly = 0f, LegitMonthly = 0.5f, LowSatis = 4 },
            new PartyDef { Id = "anarchist", Name = "无政府派", Icon = "fia_party_anarchist", Desc = "取消国家、行会自治",
                Ideologies = new[] { "egalitarian", "proletarian", "individualist" }, AuthorityMonthly = -2f, LegitMonthly = -0.5f, LowSatis = 8 },
            new PartyDef { Id = "communist", Name = "共产派", Icon = "fia_party_communist", Desc = "工人国有化、平均分配",
                Ideologies = new[] { "proletarian", "egalitarian", "anti_slavery" }, AuthorityMonthly = 1f, LegitMonthly = 0f, LowSatis = 10 },
            new PartyDef { Id = "conservative", Name = "保守党", Icon = "fia_party_conservative", Desc = "维持秩序与等级",
                Ideologies = new[] { "loyalist", "hierarchic", "plutocratic", "reactionary" }, AuthorityMonthly = 2f, LegitMonthly = 1f, ArmySatis = 4 },
            new PartyDef { Id = "fascist", Name = "民族阵线", Icon = "fia_party_fascist", Desc = "尚武、集权、扩张",
                Ideologies = new[] { "jingoist", "reactionary", "patriotic", "hierarchic" }, AuthorityMonthly = 3f, LegitMonthly = -0.5f, ArmySatis = 10, LowSatis = -4 },
            new PartyDef { Id = "faith", Name = "信仰党", Icon = "fia_party_faith", Desc = "国教与道德秩序",
                Ideologies = new[] { "pious", "moralist", "patriarchal", "anti_clerical" }, AuthorityMonthly = 1f, LegitMonthly = 1.5f, LowSatis = 2 },
            new PartyDef { Id = "free_trade", Name = "自由贸易党", Icon = "fia_party_free_trade", Desc = "开放市场、低关税",
                Ideologies = new[] { "laissez_faire", "individualist", "plutocratic" }, AuthorityMonthly = 0f, LegitMonthly = 0.5f },
            new PartyDef { Id = "liberal", Name = "自由党", Icon = "fia_party_liberal", Desc = "宪政、教育与贤能",
                Ideologies = new[] { "liberal", "meritocratic", "individualist" }, AuthorityMonthly = 0f, LegitMonthly = 1f, LowSatis = 3 },
            new PartyDef { Id = "patriotic", Name = "爱国党", Icon = "fia_party_patriotic", Desc = "国家至上、强力外交",
                Ideologies = new[] { "patriotic", "loyalist", "jingoist" }, AuthorityMonthly = 1f, LegitMonthly = 1f, ArmySatis = 6 },
            new PartyDef { Id = "radical", Name = "激进党", Icon = "fia_party_radical", Desc = "彻底改革、扩大选举",
                Ideologies = new[] { "egalitarian", "republican", "liberal" }, AuthorityMonthly = -1f, LegitMonthly = 0.5f, LowSatis = 6 },
            new PartyDef { Id = "revolutionary", Name = "革命党", Icon = "fia_party_revolutionary", Desc = "推翻旧制",
                Ideologies = new[] { "republican", "proletarian", "populist" }, AuthorityMonthly = -1.5f, LegitMonthly = -1f, LowSatis = 8 },
            new PartyDef { Id = "social_democrat", Name = "社会民主党", Icon = "fia_party_social_democrat", Desc = "劳工保护与渐进改良",
                Ideologies = new[] { "paternalistic", "egalitarian", "liberal", "populist" }, AuthorityMonthly = 0f, LegitMonthly = 0.5f, LowSatis = 6 }
        };

        private static string _ruling = "conservative";
        private static int _lastElectionHandled = -9999;

        internal static PartyDef Def(string id)
        {
            for (int i = 0; i < All.Count; i++) if (All[i].Id == id) return All[i];
            return null;
        }

        internal static string RulingId { get { return _ruling; } }
        internal static string RulingName { get { var d = Def(_ruling); return d != null ? d.Name : "无"; } }
        internal static string RulingIcon { get { var d = Def(_ruling); return d != null ? d.Icon : "fia_party_conservative"; } }

        internal static string IdeologyNameOf(int group)
        {
            try
            {
                if (group < 0 || group >= GroupIdeology.Length) return "?";
                string id = GroupIdeology[group];
                for (int i = 0; i < Ideologies.Count; i++) if (Ideologies[i].Id == id) return Ideologies[i].Name;
            }
            catch { }
            return "?";
        }

        internal static string IdeologyIconOf(int group)
        {
            try
            {
                if (group < 0 || group >= GroupIdeology.Length) return "fia_ideo_loyalist";
                string id = GroupIdeology[group];
                for (int i = 0; i < Ideologies.Count; i++) if (Ideologies[i].Id == id) return Ideologies[i].Icon;
            }
            catch { }
            return "fia_ideo_loyalist";
        }

        // 支持度: 对齐 IG 的政治力量占比(0..1, 全部政党归一化)
        internal static float SupportOf(string partyId)
        {
            try
            {
                var d = Def(partyId);
                if (d == null) return 0f;
                float sum = 0f, mine = 0f;
                for (int g = 0; g < InterestGroups.GroupCount; g++)
                {
                    float clout = Math.Max(0f, InterestGroups.CloutOf(g));
                    sum += clout;
                    if (Array.IndexOf(d.Ideologies, GroupIdeology[g]) >= 0) mine += clout;
                }
                return sum > 0.01f ? mine / sum : 0f;
            }
            catch { return 0f; }
        }

        internal static int SeatsOf(string partyId)
        {
            try { return (int)Math.Round(SupportOf(partyId) * 100f); } catch { return 0; }
        }

        internal static string TopPartyId()
        {
            try
            {
                string best = null; float bv = -1f;
                for (int i = 0; i < All.Count; i++)
                {
                    float v = SupportOf(All[i].Id);
                    if (v > bv) { bv = v; best = All[i].Id; }
                }
                return best;
            }
            catch { return "conservative"; }
        }

        // 组阁: 花权威改执政党(IG 反应按意识形态冲突)
        internal static string SetRuling(string partyId)
        {
            try
            {
                var d = Def(partyId);
                if (d == null) return "政党无效。";
                if (_ruling == d.Id) return "该党已经在朝。";
                int cost = 60;
                if (Politics.Authority < cost) return "权威不足(需 " + cost + ", 当前 " + ((int)Politics.Authority) + ")。";
                Politics.Authority -= cost;
                _ruling = d.Id;
                for (int g = 0; g < InterestGroups.GroupCount; g++)
                {
                    bool align = Array.IndexOf(d.Ideologies, GroupIdeology[g]) >= 0;
                    InterestGroups.AddEventMod(g, align ? 8 : -5);
                }
                DLog.Force("政党: 组阁 -> " + d.Name + "(权威 -" + cost + ")");
                return "已由「" + d.Name + "」组阁(权威 -" + cost + ", 对齐集团满意, 其他不满)。";
            }
            catch (Exception ex) { return "组阁失败: " + ex.Message; }
        }

        // 月结: 执政党效果 + 选举结果同步(胜选集团所属政党上台)
        internal static void Monthly()
        {
            try
            {
                var d = Def(_ruling);
                if (d != null)
                {
                    if (Math.Abs(d.AuthorityMonthly) > 0.01f) Politics.Authority = Math.Max(0f, Politics.Authority + d.AuthorityMonthly);
                    if (Math.Abs(d.LegitMonthly) > 0.01f) Politics.Legitimacy = Politics.ClampF(Politics.Legitimacy + d.LegitMonthly, 0f, 100f);
                    if (d.ArmySatis != 0) InterestGroups.AddEventMod(7, d.ArmySatis);
                    if (d.LowSatis != 0) { InterestGroups.AddEventMod(6, d.LowSatis); InterestGroups.AddEventMod(5, d.LowSatis / 2); }
                }
                // 选举结果: 最近一次胜选集团 -> 其意识形态对齐度最高的政党上台
                int last = Elections.LastElectionDay;
                if (last > _lastElectionHandled && Elections.LastWinnerGroup >= 0)
                {
                    _lastElectionHandled = last;
                    string ideo = GroupIdeology[Math.Min(Elections.LastWinnerGroup, GroupIdeology.Length - 1)];
                    string best = null; int bestHit = 0;
                    for (int i = 0; i < All.Count; i++)
                    {
                        int hit = 0;
                        foreach (var x in All[i].Ideologies) if (x == ideo) hit++;
                        // 同分取支持度更高的
                        if (hit > bestHit || (hit == bestHit && best != null && SupportOf(All[i].Id) > SupportOf(best))) { bestHit = hit; best = All[i].Id; }
                    }
                    if (best != null && bestHit > 0 && best != _ruling)
                    {
                        _ruling = best;
                        DLog.Force("政党: 选举后组阁 -> " + RulingName);
                    }
                }
            }
            catch { }
        }

        internal static string Save()
        {
            try { return _ruling + ";" + _lastElectionHandled; } catch { return ""; }
        }

        internal static void Load(string data)
        {
            try
            {
                if (string.IsNullOrEmpty(data)) return;
                var f = data.Split(';');
                if (f.Length > 0 && !string.IsNullOrEmpty(f[0]) && Def(f[0]) != null) _ruling = f[0];
                int x;
                if (f.Length > 1 && int.TryParse(f[1], out x)) _lastElectionHandled = x;
            }
            catch { }
        }
    }
}
