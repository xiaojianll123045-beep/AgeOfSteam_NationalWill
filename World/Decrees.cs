using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace FeudalInternalAffairs
{
    // 一条法令(V3 decrees 式: 花权威颁布, 限时生效, 可同时存在若干条)
    internal class DecreeDef
    {
        internal string Id, Name, Icon, Desc, Effect;
        internal int Cost;          // 权威成本
        internal int Days;          // 有效期(骑砍历法 84 日/年)
        internal int[] Att;         // 颁布时 IG 态度[8]
        internal int Gold;          // 立即金币(负 = 支出)
        internal float ProsperityDaily;   // 每日城镇繁荣(我方)
        internal float GoldDaily;         // 每日金币
        internal float AuthorityDaily;
        internal float TyrannyOnce;       // 立即暴政
    }

    internal static class Decrees
    {
        internal class ActiveDecree
        {
            internal string Id;
            internal int EndDay;
        }

        internal static readonly List<ActiveDecree> Active = new List<ActiveDecree>();

        internal static readonly List<DecreeDef> All = new List<DecreeDef>
        {
            new DecreeDef { Id = "emergency_relief", Name = "紧急救济", Icon = "fia_decree_emergency_relief", Desc = "开仓放粮, 安抚饥民", Effect = "下层满意度 +10, 立即支出 3000 金币",
                Cost = 40, Days = 60, Gold = -3000, Att = new[] { 0, -2, 0, 4, 0, 0, 10, 0 } },
            new DecreeDef { Id = "encourage_agriculture", Name = "鼓励农业", Icon = "fia_decree_encourage_agriculture", Desc = "免税劝农, 兴修水利", Effect = "60 日内 我方城镇繁荣 +0.3/日",
                Cost = 50, Days = 60, ProsperityDaily = 0.3f, Att = new[] { 0, -4, 4, 0, -4, 0, 8, 0 } },
            new DecreeDef { Id = "encourage_manufacturing", Name = "鼓励制造业", Icon = "fia_decree_encourage_manufacturing", Desc = "奖励工坊与订货", Effect = "60 日内 城镇繁荣 +0.2/日, 行会/商帮 +6",
                Cost = 50, Days = 60, ProsperityDaily = 0.2f, Att = new[] { 0, 4, 0, 0, 6, 6, 0, 0 } },
            new DecreeDef { Id = "encourage_resource", Name = "鼓励资源", Icon = "fia_decree_encourage_resource", Desc = "开矿伐木, 保障原料", Effect = "60 日内 城镇繁荣 +0.3/日, 商帮 +4",
                Cost = 50, Days = 60, ProsperityDaily = 0.3f, Att = new[] { 0, 4, 0, 0, 4, 4, 0, 0 } },
            new DecreeDef { Id = "enlistment_efforts", Name = "征兵努力", Icon = "fia_decree_enlistment_efforts", Desc = "张贴募兵告示, 提高饷银", Effect = "军队 +10, 合法性 -1, 立即支出 2000 金币",
                Cost = 45, Days = 60, Gold = -2000, Att = new[] { 2, 0, 0, 0, 0, 0, -6, 10 } },
            new DecreeDef { Id = "establish_missions", Name = "建立传教", Icon = "fia_decree_establish_missions", Desc = "拨款建立传教所", Effect = "教会 +12, 90 日有效",
                Cost = 40, Days = 90, Att = new[] { 0, 0, 0, 12, -4, 0, 0, 0 } },
            new DecreeDef { Id = "greener_grass", Name = "移民鼓励", Icon = "fia_decree_greener_grass", Desc = "招徕流民与移民", Effect = "下层满意度 +6, 商帮 +4, 90 日",
                Cost = 40, Days = 90, Att = new[] { 0, 0, 0, 0, 4, 0, 6, 0 } },
            new DecreeDef { Id = "promote_national_values", Name = "推广民族价值观", Icon = "fia_decree_promote_national_values", Desc = "兴办学校与宣讲", Effect = "教会 +6, 军队 +6, 权威 +0.5/日, 90 日",
                Cost = 60, Days = 90, AuthorityDaily = 0.5f, Att = new[] { 4, 0, 0, 6, 0, 0, -4, 6 } },
            new DecreeDef { Id = "promote_social_mobility", Name = "促进社会流动", Icon = "fia_decree_promote_social_mobility", Desc = "开科举、兴学堂", Effect = "下层满意度 +8, 地方 +4, 权威 -0.5/日, 90 日",
                Cost = 60, Days = 90, AuthorityDaily = -0.5f, Att = new[] { -4, -4, 4, 0, 0, 0, 8, 0 } },
            new DecreeDef { Id = "road_maintenance", Name = "道路维护", Icon = "fia_decree_road_maintenance", Desc = "修桥补路, 疏通驿道", Effect = "90 日内 城镇繁荣 +0.2/日, 金币 -10/日",
                Cost = 45, Days = 90, ProsperityDaily = 0.2f, GoldDaily = -10f, Att = new[] { 0, 0, 4, 0, 4, 2, 4, 0 } },
            new DecreeDef { Id = "violent_suppression", Name = "暴力镇压", Icon = "fia_decree_violent_suppression", Desc = "出动军队弹压", Effect = "下层满意度 -12, 军队 +8, 合法性 +2, 暴政 +1.5, 30 日",
                Cost = 70, Days = 30, TyrannyOnce = 1.5f, Att = new[] { 8, 4, -4, 0, 0, -4, -12, 8 } }
        };

        internal static DecreeDef Def(string id)
        {
            for (int i = 0; i < All.Count; i++) if (All[i].Id == id) return All[i];
            return null;
        }

        // 可同时生效的法令数 = 2 + 官僚制度档位
        internal static int MaxActive()
        {
            try { return 2 + LawSystem.Level(LawSystem.LBureaucracy); } catch { return 2; }
        }

        internal static bool IsActive(string id)
        {
            for (int i = 0; i < Active.Count; i++) if (Active[i].Id == id) return true;
            return false;
        }

        internal static int DaysLeft(string id)
        {
            try
            {
                int today = Politics.Today();
                for (int i = 0; i < Active.Count; i++) if (Active[i].Id == id) return Math.Max(0, Active[i].EndDay - today);
            }
            catch { }
            return 0;
        }

        internal static string Enact(string id)
        {
            try
            {
                var d = Def(id);
                if (d == null) return "法令无效。";
                if (IsActive(id)) return "「" + d.Name + "」已在生效中。";
                if (Active.Count >= MaxActive()) return "同时生效的法令已达上限(" + MaxActive() + " 条), 等旧的到期。";
                if (Politics.Authority < d.Cost) return "权威不足(需 " + d.Cost + ", 当前 " + ((int)Politics.Authority) + ")。";
                Politics.Authority -= d.Cost;
                Active.Add(new ActiveDecree { Id = id, EndDay = Politics.Today() + d.Days });
                if (d.Att != null)
                    for (int g = 0; g < d.Att.Length && g < InterestGroups.GroupCount; g++)
                        if (d.Att[g] != 0) InterestGroups.AddEventMod(g, d.Att[g]);
                if (d.Gold != 0)
                {
                    try
                    {
                        if (d.Gold < 0) EconomyWorld.TreasurySpend(-d.Gold);
                        else EconomyWorld.TreasuryAdd(d.Gold);
                    }
                    catch { }
                }
                if (Math.Abs(d.TyrannyOnce) > 0.01f) { try { Politics.Tyranny += d.TyrannyOnce; } catch { } }
                DLog.Force("法令: 颁布 " + d.Name + "(权威 -" + d.Cost + ", " + d.Days + " 日)");
                return "已颁布「" + d.Name + "」(" + d.Days + " 日, 权威 -" + d.Cost + ")。";
            }
            catch (Exception ex) { return "颁布失败: " + ex.Message; }
        }

        // 每日: 到期清理 + 每日效果
        internal static void Daily(int day)
        {
            try
            {
                for (int i = Active.Count - 1; i >= 0; i--)
                {
                    if (Active[i].EndDay <= day)
                    {
                        var d0 = Def(Active[i].Id);
                        Active.RemoveAt(i);
                        DLog.Force("法令: 「" + (d0 != null ? d0.Name : "?") + "」已到期");
                    }
                }
                if (Active.Count == 0) return;
                var pk = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                if (pk == null) return;
                float pros = 0f, gold = 0f, auth = 0f;
                for (int i = 0; i < Active.Count; i++)
                {
                    var d = Def(Active[i].Id);
                    if (d == null) continue;
                    pros += d.ProsperityDaily; gold += d.GoldDaily; auth += d.AuthorityDaily;
                }
                if (Math.Abs(pros) > 0.001f)
                {
                    foreach (var s in pk.Settlements)
                        if (s != null && s.IsTown && s.Town != null) s.Town.Prosperity += pros;
                }
                if (Math.Abs(gold) > 0.001f)
                {
                    try { if (gold < 0) EconomyWorld.TreasurySpend((int)Math.Round(-gold)); else EconomyWorld.TreasuryAdd((int)Math.Round(gold)); } catch { }
                }
                if (Math.Abs(auth) > 0.001f) Politics.Authority = Math.Max(0f, Politics.Authority + auth);
            }
            catch { }
        }

        // ================= v4.21x: AI 法令(AI 国家自行颁布) =================
        //   权威与效果只作用于该国; 成本表/效果沿用同一 DecreeDef, 不新增数值
        private static readonly Dictionary<string, float> AiAuth = new Dictionary<string, float>();
        private static readonly Dictionary<string, List<ActiveDecree>> AiActive = new Dictionary<string, List<ActiveDecree>>();
        private static int _aiLastDay = -1;

        internal static float AiAuthOf(Kingdom k)
        {
            try
            {
                if (k == null || string.IsNullOrEmpty(k.StringId)) return 0f;
                float v;
                if (AiAuth.TryGetValue(k.StringId, out v)) return v;
                AiAuth[k.StringId] = 200f;   // 与玩家开局一致
                return 200f;
            }
            catch { return 0f; }
        }

        internal static void AiAuthAdd(Kingdom k, float v)
        {
            try
            {
                if (k == null || string.IsNullOrEmpty(k.StringId) || Math.Abs(v) < 0.001f) return;
                float n = AiAuthOf(k) + v;
                AiAuth[k.StringId] = n < 0f ? 0f : (n > 1000f ? 1000f : n);
            }
            catch { }
        }

        // 供 AI 游说/条约消耗权威; 不足返回 false
        internal static bool AiAuthSpend(Kingdom k, float cost)
        {
            try
            {
                if (k == null) return false;
                if (cost <= 0f) return true;
                float v = AiAuthOf(k);
                if (v < cost) return false;
                AiAuth[k.StringId] = v - cost;
                return true;
            }
            catch { return false; }
        }

        private static List<ActiveDecree> AiListOf(Kingdom k) { return k != null ? AiListOf(k.StringId) : null; }

        private static List<ActiveDecree> AiListOf(string id)
        {
            List<ActiveDecree> list;
            if (!AiActive.TryGetValue(id, out list)) { list = new List<ActiveDecree>(); AiActive[id] = list; }
            return list;
        }

        private static bool AiActiveHas(List<ActiveDecree> list, string id)
        {
            for (int i = 0; i < list.Count; i++) if (list[i].Id == id) return true;
            return false;
        }

        internal static void AiMonthly(int day)
        {
            try
            {
                if (day < _aiLastDay) { AiAuth.Clear(); AiActive.Clear(); }   // 新档/读档 -> 重置
                _aiLastDay = day;
                var pk = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                foreach (var k in Kingdom.All)
                {
                    if (k == null || k.IsEliminated) continue;
                    if (pk != null && ReferenceEquals(pk, k)) continue;
                    try { AiStep(k, day); } catch { }
                }
            }
            catch (Exception ex) { DLog.Force("AI 法令月结异常: " + ex.Message); }
        }

        private static void AiStep(Kingdom k, int day)
        {
            AiAuthAdd(k, 10f + Math.Min(10f, k.Settlements.Count * 0.5f));   // 权威恢复(与政治月结同基础)
            var list = AiListOf(k);
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (list[i].EndDay <= day) { list.RemoveAt(i); continue; }
                var d0 = Def(list[i].Id);
                if (d0 != null) ApplyMonthlyEffects(k, d0);
            }
            try { AiAgenda.Ensure(k, day); } catch { }
            if (MBRandom.RandomFloat > 0.5f) return;
            var pick = PickDecree(k);
            if (pick == null || AiActiveHas(list, pick.Id)) return;
            if (AiEnact(k, pick.Id, day))
                AiAgenda.LogPol(k, "颁布「" + pick.Name + "」(权威 -" + pick.Cost + ", " + pick.Days + " 日)");
        }

        // v5.x: 指定法令(AI 议程用于镇压/妥协/安抚; 与 AiStep 同一成本与效果)
        internal static bool AiEnact(Kingdom k, string id, int day)
        {
            try
            {
                var d = Def(id);
                if (k == null || d == null) return false;
                var list = AiListOf(k);
                if (AiActiveHas(list, id)) return true;                      // 已在生效
                if (list.Count >= 2) return false;                           // 同时生效上限
                if (AiAuthOf(k) < d.Cost) return false;
                if (d.Gold < 0 && WarEconomy.GoldOfPublic(k) < -d.Gold + 500f) return false;
                if (!AiAuthSpend(k, d.Cost)) return false;
                list.Add(new ActiveDecree { Id = id, EndDay = day + d.Days });
                if (d.Gold < 0) WarEconomy.SpendPublic(k, -d.Gold);
                else if (d.Gold > 0) WarEconomy.AddPublic(k, d.Gold);
                try { if (d.Att != null) AiEconomyDeep.ShiftPublicStance(k, d.Att); } catch { }
                return true;
            }
            catch { return false; }
        }

        private static void ApplyMonthlyEffects(Kingdom k, DecreeDef d)
        {
            try
            {
                float pros = d.ProsperityDaily * 30f;
                if (Math.Abs(pros) > 0.001f)
                    foreach (var s in k.Settlements)
                        if (s != null && s.IsTown && s.Town != null) s.Town.Prosperity += pros;
                float gold = d.GoldDaily * 30f;
                if (gold > 0.001f) WarEconomy.AddPublic(k, gold);
                else if (gold < -0.001f) WarEconomy.SpendPublic(k, -gold);
                if (Math.Abs(d.AuthorityDaily) > 0.001f) AiAuthAdd(k, d.AuthorityDaily * 30f);
            }
            catch { }
        }

        // 选择依据: 议程(Goal)优先; 缺粮/危机 -> 救济; 好战 -> 征兵/镇压; 重商 -> 制造业/资源; 建设 -> 农业/道路
        private static DecreeDef PickDecree(Kingdom k)
        {
            try
            {
                var want = Def(AiAgenda.DecreeIdFor(k));
                if (want != null) return want;   // v5.x: AI 议程目标
                int agg = AiPersonality.AggressionOf(k);
                int dev = AiPersonality.DevelopmentOf(k);
                int com = AiPersonality.CommerceOf(k);
                int wars = 0;
                try { foreach (var x in Kingdom.All) { if (x == null || x.IsEliminated || ReferenceEquals(x, k)) continue; if (k.IsAtWarWith(x)) wars++; } } catch { }
                float days = 99f;
                try { WarEconomy.FoodDaysOf(k, out days); } catch { }
                if (days < 10f) return Def("emergency_relief");
                if (wars > 0 && agg >= 60) return MBRandom.RandomFloat < 0.6f ? Def("enlistment_efforts") : Def("violent_suppression");
                if (com >= 60) return MBRandom.RandomFloat < 0.5f ? Def("encourage_manufacturing") : Def("encourage_resource");
                if (dev >= 60) return MBRandom.RandomFloat < 0.5f ? Def("encourage_agriculture") : Def("road_maintenance");
                if (agg >= 60) return Def("violent_suppression");
                return MBRandom.RandomFloat < 0.5f ? Def("encourage_agriculture") : Def("road_maintenance");
            }
            catch { return null; }
        }

        internal static string StatusText()
        {
            try
            {
                if (Active.Count == 0) return "当前没有生效中的法令(上限 " + MaxActive() + " 条)。";
                var sb = new System.Text.StringBuilder();
                sb.Append("生效中(").Append(Active.Count).Append('/').Append(MaxActive()).Append("): ");
                for (int i = 0; i < Active.Count; i++)
                {
                    var d = Def(Active[i].Id);
                    sb.Append(d != null ? d.Name : Active[i].Id).Append("(剩 ").Append(DaysLeft(Active[i].Id)).Append(" 日)");
                    if (i < Active.Count - 1) sb.Append(" · ");
                }
                return sb.ToString();
            }
            catch { return ""; }
        }

        // UI: 选一条法令颁布(显示成本/期限/效果; 生效中的不可再选)
        internal static void ShowUi()
        {
            try
            {
                var opts = new List<InquiryElement>();
                for (int i = 0; i < All.Count; i++)
                {
                    var d = All[i];
                    bool on = IsActive(d.Id);
                    string label = d.Name + (on ? ("(生效中 剩 " + DaysLeft(d.Id) + " 日)") : ("(" + d.Cost + " 权威 / " + d.Days + " 日)"));
                    opts.Add(new InquiryElement(d.Id, label, null, !on, d.Desc + "\n" + d.Effect));
                }
                PanelInputGuard.ShowPopup(new MultiSelectionInquiryData(
                    "颁布法令",
                    "权威: " + ((int)Politics.Authority) + "  ·  同时生效上限: " + MaxActive() + " 条(官僚制度提升)\n" + StatusText(),
                    opts, true, 1, 1, "颁布", "取消",
                    delegate (List<InquiryElement> sel)
                    {
                        if (sel == null || sel.Count == 0) return;
                        string msg = Enact(sel[0].Identifier as string);
                        try { MapSelection.Message(msg); } catch { }
                    }, null, null, false));
            }
            catch (Exception ex) { DLog.Force("法令界面失败: " + ex.Message); }
        }

        internal static string Save()
        {
            try
            {
                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < Active.Count; i++) sb.Append(Active[i].Id).Append(',').Append(Active[i].EndDay).Append(';');
                // v4.21x: AI 段(旧档无此段, Load 兼容)
                sb.Append('~');
                foreach (var kv in AiAuth) sb.Append('A').Append(kv.Key).Append(',').Append(((int)kv.Value)).Append(';');
                foreach (var kv in AiActive)
                    for (int i = 0; i < kv.Value.Count; i++)
                        sb.Append('D').Append(kv.Key).Append(',').Append(kv.Value[i].Id).Append(',').Append(kv.Value[i].EndDay).Append(';');
                return sb.ToString();
            }
            catch { return ""; }
        }

        internal static void Load(string data)
        {
            try
            {
                Active.Clear();
                AiAuth.Clear(); AiActive.Clear();
                if (string.IsNullOrEmpty(data)) return;
                int tilde = data.IndexOf('~');
                string core = tilde >= 0 ? data.Substring(0, tilde) : data;
                foreach (var seg in core.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var f = seg.Split(',');
                    int end;
                    if (f.Length != 2 || !int.TryParse(f[1], out end)) continue;
                    if (Def(f[0]) == null) continue;
                    Active.Add(new ActiveDecree { Id = f[0], EndDay = end });
                }
                if (tilde >= 0) LoadAi(data.Substring(tilde + 1));
            }
            catch { }
        }

        private static void LoadAi(string ai)
        {
            try
            {
                if (string.IsNullOrEmpty(ai)) return;
                foreach (var seg in ai.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (seg.Length < 2) continue;
                    var f = seg.Substring(1).Split(',');
                    char tag = seg[0];
                    if (tag == 'A')
                    {
                        int v;
                        if (f.Length == 2 && int.TryParse(f[1], out v)) AiAuth[f[0]] = v;
                    }
                    else if (tag == 'D')
                    {
                        int end;
                        if (f.Length != 3 || Def(f[1]) == null || !int.TryParse(f[2], out end)) continue;
                        AiListOf(f[0]).Add(new ActiveDecree { Id = f[1], EndDay = end });
                    }
                }
            }
            catch { }
        }
    }
}
