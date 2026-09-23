using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;

namespace FeudalInternalAffairs
{
    // v4.150: 民族精神(National Spirits) —— 国家总页的 6 个精神槽真实化
    //   来源: ①国家性格(鹰派/建设/商贸) ②关键法律 ③当前局势(霸权/围堵/危机/和平/战争/动荡)
    //   效果真实接入: 军事 / 建筑产出 / 收入 / 研究 / 合法性 / 激进 / 征召
    internal class SpiritDef
    {
        internal string Id;
        internal string Name;
        internal string Icon;
        internal string Desc;          // 效果描述(UI)
        internal float Military;       // 军事加成
        internal float Output;         // 建筑产出加成
        internal float Income;         // 收入加成
        internal float Research;       // 研究速度加成
        internal float Legit;          // 合法性加成(点)
        internal float Radical;        // 每日激进修正(正=加剧)
        internal float Conscript;      // 征召/动员加成
        internal int Priority;
    }

    internal static class NationalSpirits
    {
        private static readonly Dictionary<string, List<SpiritDef>> Cache = new Dictionary<string, List<SpiritDef>>();
        private static readonly Dictionary<string, int> CacheDay = new Dictionary<string, int>();

        private static SpiritDef Def(string id, string name, string icon, string desc,
            float mil = 0f, float outp = 0f, float inc = 0f, float res = 0f, float legit = 0f, float rad = 0f, float cons = 0f, int pri = 50)
        {
            return new SpiritDef { Id = id, Name = name, Icon = icon, Desc = desc, Military = mil, Output = outp, Income = inc, Research = res, Legit = legit, Radical = rad, Conscript = cons, Priority = pri };
        }

        // 计算某国当前的精神(缓存 7 天)
        internal static List<SpiritDef> Of(Kingdom k, int day)
        {
            try
            {
                if (k == null) return new List<SpiritDef>();
                int d;
                if (CacheDay.TryGetValue(k.StringId, out d) && day - d < 7)
                {
                    List<SpiritDef> c;
                    if (Cache.TryGetValue(k.StringId, out c)) return c;
                }
                var list = Build(k, day);
                Cache[k.StringId] = list;
                CacheDay[k.StringId] = day;
                return list;
            }
            catch { return new List<SpiritDef>(); }
        }

        private static List<SpiritDef> Build(Kingdom k, int day)
        {
            var list = new List<SpiritDef>();
            try
            {
                // ---- ① 国家性格 ----
                int agg = AiPersonality.AggressionOf(k);
                int dev = AiPersonality.DevelopmentOf(k);
                int com = AiPersonality.CommerceOf(k);
                if (agg >= 65) list.Add(Def("sp_martial", "尚武传统", "General\\Icons\\Militia", "军事 +8% · 征召 +10%", mil: 0.08f, cons: 0.10f, pri: 10));
                if (dev >= 65) list.Add(Def("sp_builder", "营造之风", "fia_cat_industry", "建筑产出 +6% · 建造更快", outp: 0.06f, pri: 20));
                if (com >= 65) list.Add(Def("sp_mercantile", "重商主义", "fia_cat_trade", "收入 +8% · 关税更优", inc: 0.08f, pri: 30));
                if (agg <= 35 && dev <= 35 && com <= 35)
                    list.Add(Def("sp_quiet", "守成之邦", "fia_cat_admin", "激进 -15% · 收入 -3%", inc: -0.03f, rad: -0.0002f, pri: 90));

                // ---- ② 关键法律 ----
                try
                {
                    if (LawSystem.Level(LawSystem.LEducation) >= 2)
                        list.Add(Def("sp_learning", "文教昌明", "fia_cat_admin", "研究 +10% · 识字增长更快", res: 0.10f, pri: 15));
                    if (LawSystem.Level(LawSystem.LRights) >= 2)
                        list.Add(Def("sp_rights", "民权勃兴", "fia_cat_admin", "合法性 +3 · 激进 -10%", legit: 3f, rad: -0.0001f, pri: 25));
                    if (LawSystem.Level(LawSystem.LWelfare) >= 2)
                        list.Add(Def("sp_welfare", "福利国家", "fia_cat_living", "激进 -20% · 收入 -4%", inc: -0.04f, rad: -0.0003f, pri: 35));
                    if (LawSystem.Level(LawSystem.LLevy) >= 2)
                        list.Add(Def("sp_levy", "全民皆兵", "General\\Icons\\Militia", "征召 +15% · 动员更快", cons: 0.15f, pri: 40));
                    if (LawSystem.Level(LawSystem.LLabor) >= 2)
                        list.Add(Def("sp_labor", "劳工尊严", "fia_cat_industry", "产出 +4% · 激进 -10%", outp: 0.04f, rad: -0.0001f, pri: 60));
                }
                catch { }

                // ---- ③ 当前局势 ----
                try
                {
                    // 霸权: 军力 > 第二 ×1.6
                    Kingdom first = null, second = null;
                    float f1 = 0f, f2 = 0f;
                    foreach (var x in Kingdom.All)
                    {
                        if (x == null || x.IsEliminated) continue;
                        float s = WarPlans.StrengthOf(x);
                        if (s > f1) { second = first; f2 = f1; first = x; f1 = s; }
                        else if (s > f2) { second = x; f2 = s; }
                    }
                    if (ReferenceEquals(first, k) && f1 > f2 * 1.6f)
                        list.Add(Def("sp_hegemon", "霸权", "Clan\\party_leader_crown", "收入 +10% · 外交关系 -2/月", inc: 0.10f, pri: 5));

                    // 被围堵: 3+ 国制裁
                    int sanctionedBy = 0;
                    foreach (var x in Kingdom.All)
                    {
                        if (x == null || x.IsEliminated || ReferenceEquals(x, k)) continue;
                        bool s = false;
                        try { s = WarEconomy.SanctionedBy(k, x); } catch { }
                        if (s) sanctionedBy++;
                    }
                    if (sanctionedBy >= 3)
                        list.Add(Def("sp_embattled", "众矢之的", "fia_cat_military", "收入 -10% · 军事 +5%(背水一战)", inc: -0.10f, mil: 0.05f, pri: 45));

                    // 危机
                    bool crisis = false;
                    try { crisis = WarEconomy.IsCrisis(k); } catch { }
                    if (crisis)
                        list.Add(Def("sp_troubled", "国困民穷", "fia_cat_resource", "激进 +50% · 收入 -6%", inc: -0.06f, rad: 0.0004f, pri: 8));

                    // 太平盛世: 无战争且金库充裕
                    bool atWar = false;
                    try { foreach (var x in Kingdom.All) { if (x != null && !x.IsEliminated && k.IsAtWarWith(x)) { atWar = true; break; } } } catch { }
                    float gold = WarEconomy.GoldOfPublic(k);
                    if (!atWar && gold > 6000f)
                        list.Add(Def("sp_prosperity", "太平盛世", "Barter\\Gold", "建筑产出 +8% · 激进 -10%", outp: 0.08f, rad: -0.0001f, pri: 12));

                    // 凯歌高奏: 战争中且战况优势
                    if (atWar)
                    {
                        try
                        {
                            var plan = WarPlans.MainOf(k, day);
                            if (plan != null && plan.Progress >= 1.5f)
                                list.Add(Def("sp_triumph", "凯歌高奏", "Clan\\moving_icon", "军事 +6% · 合法性 +2", mil: 0.06f, legit: 2f, pri: 18));
                        }
                        catch { }
                    }
                }
                catch { }

                // 按优先级排序, 取前 6
                list.Sort(delegate (SpiritDef a, SpiritDef b) { return a.Priority.CompareTo(b.Priority); });
                if (list.Count > 6) list.RemoveRange(6, list.Count - 6);
            }
            catch { }
            return list;
        }

        // ================= 效果聚合(供各系统接入) =================
        private static float Sum(Kingdom k, Func<SpiritDef, float> sel)
        {
            try
            {
                var list = Of(k, AiDiplomacy.Today());
                float v = 0f;
                for (int i = 0; i < list.Count; i++) v += sel(list[i]);
                return v;
            }
            catch { return 0f; }
        }

        internal static float MilitaryMult(Kingdom k) { return 1f + Sum(k, delegate (SpiritDef d) { return d.Military; }); }
        internal static float OutputMult(Kingdom k) { return 1f + Sum(k, delegate (SpiritDef d) { return d.Output; }); }
        internal static float IncomeMult(Kingdom k) { return 1f + Sum(k, delegate (SpiritDef d) { return d.Income; }); }
        internal static float ResearchMult(Kingdom k) { return 1f + Sum(k, delegate (SpiritDef d) { return d.Research; }); }
        internal static float LegitBonus(Kingdom k) { return Sum(k, delegate (SpiritDef d) { return d.Legit; }); }
        internal static float RadicalDelta(Kingdom k) { return Sum(k, delegate (SpiritDef d) { return d.Radical; }); }
        internal static float ConscriptMult(Kingdom k) { return 1f + Sum(k, delegate (SpiritDef d) { return d.Conscript; }); }

        internal static void Reset()
        {
            Cache.Clear();
            CacheDay.Clear();
        }
    }
}
