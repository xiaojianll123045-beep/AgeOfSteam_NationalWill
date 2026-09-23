using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace FeudalInternalAffairs
{
    // 条约(文档 24.8 / P27; v4.188): 贸易协定 / 投资条约 / 同盟 / 互不侵犯
    //   期限按骑砍历法(84 日/年); 到期自动失效; 违约有权威/合法性/关系惩罚
    internal class Treaty
    {
        internal string Partner;      // 对方王国 StringId
        internal string Type;         // trade / invest / ally / nap
        internal int StartDay;
        internal int Days;
        internal bool Broken;
        internal int Level = 1;        // v4.203: 条款等级 1 标准 / 2 优厚 / 3 苛刻
        internal int Monthly;          // v4.203: 每月收益(按等级与类型)
        internal int Left(int today) { return Math.Max(0, StartDay + Days - today); }
    }

    internal static class Treaties
    {
        internal static readonly List<Treaty> All = new List<Treaty>();

        // v4.203: 信誉(0~100) —— 撕毁条约扣信誉, 信誉低则缔约更贵; 每月缓慢回升
        internal static float Credit = 60f;
        internal static int SignLevel = 1;      // v4.203: 缔约时使用的条款等级(UI 选择)
        internal static readonly string[] LevelNames = { "标准", "优厚", "苛刻" };
        internal static readonly float[] LevelCostMult = { 1f, 1.6f, 2.4f };
        internal static readonly float[] LevelGainMult = { 1f, 1.8f, 2.8f };
        internal static readonly float[] LevelDaysMult = { 1f, 1.3f, 0.8f };

        internal static readonly string[] TypeIds = { "trade", "invest", "ally", "nap" };
        internal static readonly string[] TypeNames = { "贸易协定", "投资条约", "同盟条约", "互不侵犯" };
        internal static readonly int[] TypeDays = { 168, 252, 336, 168 };        // 2 / 3 / 4 / 2 年
        internal static readonly int[] TypeCost = { 60, 90, 140, 40 };           // 权威成本
        internal static readonly string[] TypeEffect =
        {
            "贸易协定: 每月 +800 第纳尔(商路收益), 商帮满意 +6",
            "投资条约: 每月 +1,500 第纳尔(投资分红), 商帮/行会满意 +4",
            "同盟条约: 每月 +400 第纳尔, 军队满意 +8, 关系 +8(共同防御)",
            "互不侵犯: 双方不开战, 关系 +4"
        };

        internal static string NameOf(string type)
        {
            for (int i = 0; i < TypeIds.Length; i++) if (TypeIds[i] == type) return TypeNames[i];
            return type;
        }

        internal static bool Has(Kingdom k, string type)
        {
            if (k == null) return false;
            int today = Politics.Today();
            for (int i = 0; i < All.Count; i++)
            {
                var t = All[i];
                if (t == null || t.Broken || t.Type != type || t.Partner != k.StringId) continue;
                if (t.Left(today) <= 0) continue;
                return true;
            }
            return false;
        }

        internal static Treaty Find(Kingdom k, string type)
        {
            if (k == null) return null;
            int today = Politics.Today();
            for (int i = 0; i < All.Count; i++)
            {
                var t = All[i];
                if (t == null || t.Broken || t.Type != type || t.Partner != k.StringId) continue;
                if (t.Left(today) <= 0) continue;
                return t;
            }
            return null;
        }

        // 缔约(权威成本; 已存在同类条约则拒绝)
        internal static string Sign(Kingdom k, string type, int level = 1)
        {
            try
            {
                if (k == null) return "未选择对象。";
                int ti = Array.IndexOf(TypeIds, type);
                if (ti < 0) return "条约类型无效。";
                if (Has(k, type)) return "已与 " + k.Name + " 有生效中的" + TypeNames[ti] + "。";
                if (level < 1) level = 1; if (level > 3) level = 3;
                float creditMult = 1f + (60f - Credit) / 100f;      // 信誉低于 60 变贵
                int cost = (int)Math.Round(TypeCost[ti] * LevelCostMult[level - 1] * creditMult);
                if (Politics.Authority < cost) return "权威不足(需 " + cost + ", 当前 " + ((int)Politics.Authority) + ")。";
                Politics.Authority -= cost;
                int gain = type == "trade" ? 800 : (type == "invest" ? 1500 : (type == "ally" ? 400 : 0));
                All.Add(new Treaty
                {
                    Partner = k.StringId, Type = type, StartDay = Politics.Today(),
                    Days = (int)Math.Round(TypeDays[ti] * LevelDaysMult[level - 1]),
                    Level = level,
                    Monthly = (int)Math.Round(gain * LevelGainMult[level - 1])
                });
                try
                {
                    int g = type == "ally" ? 7 : 4;   // 军队 / 商帮
                    InterestGroups.AddEventMod(g, type == "ally" ? 8 : 6);
                }
                catch { }
                DLog.Force("条约: 与 " + k.Name + " 签订 " + TypeNames[ti] + "(" + LevelNames[level - 1] + ")");
                return "已与 " + k.Name + " 签订" + TypeNames[ti] + "·" + LevelNames[level - 1] + "(权威 -" + cost + ")。";
            }
            catch (Exception ex) { return "缔约失败: " + ex.Message; }
        }

        // 违约(权威/合法性/关系惩罚)
        internal static string Break(Kingdom k, string type)
        {
            try
            {
                var t = Find(k, type);
                if (t == null) return "没有生效中的该类条约。";
                t.Broken = true;
                Credit = Math.Max(0f, Credit - 12f);        // v4.203: 撕毁扣信誉
                Politics.Authority = Math.Max(0f, Politics.Authority - 40f);
                Politics.Legitimacy = Math.Max(0f, Politics.Legitimacy - 6f);
                try
                {
                    if (Hero.MainHero != null && k != null && k.Leader != null)
                        TaleWorlds.CampaignSystem.Actions.ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, k.Leader, -15, false);
                }
                catch { }
                DLog.Force("条约: 撕毁与 " + k.Name + " 的 " + NameOf(type));
                return "已撕毁与 " + k.Name + " 的" + NameOf(type) + "(权威 -40, 合法性 -6, 关系 -15)。";
            }
            catch (Exception ex) { return "违约失败: " + ex.Message; }
        }

        // 月结: 到期清理 + 收益结算
        internal static void Monthly()
        {
            try
            {
                Credit = Math.Min(100f, Credit + 0.5f);   // v4.203: 信誉每月缓慢回升
                int today = Politics.Today();
                for (int i = All.Count - 1; i >= 0; i--)
                {
                    var t = All[i];
                    if (t == null) { All.RemoveAt(i); continue; }
                    if (t.Broken || t.Left(today) <= 0) { All.RemoveAt(i); continue; }
                    int gain = t.Monthly > 0 ? t.Monthly : (t.Type == "trade" ? 800 : (t.Type == "invest" ? 1500 : (t.Type == "ally" ? 400 : 0)));
                    if (gain > 0) { try { EconomyWorld.TreasuryAdd(gain); } catch { } }
                }
            }
            catch { }
        }

        internal static string StatusText()
        {
            try
            {
                if (All.Count == 0) return "当前没有生效中的条约。";
                int today = Politics.Today();
                var sb = new System.Text.StringBuilder();
                sb.Append("C:").Append((int)Credit).Append('!');   // v4.203: 信誉
                for (int i = 0; i < All.Count; i++)
                {
                    var t = All[i];
                    if (t == null || t.Broken || t.Left(today) <= 0) continue;
                    var k = Kingdom.All != null ? FindKingdom(t.Partner) : null;
                    sb.Append(k != null ? k.Name.ToString() : t.Partner).Append(' ').Append(NameOf(t.Type))
                      .Append('·').Append(LevelNames[Math.Min(2, Math.Max(0, t.Level - 1))])
                      .Append("(剩 ").Append(t.Left(today)).Append(" 日, 月 ").Append(t.Monthly).Append(")\n");
                }
                return sb.Length > 0 ? sb.ToString() : "当前没有生效中的条约。";
            }
            catch { return ""; }
        }

        internal static Kingdom FindKingdom(string id)
        {
            try
            {
                foreach (var k in Kingdom.All) if (k != null && k.StringId == id) return k;
            }
            catch { }
            return null;
        }

        // 可缔约对象(非我方、未消灭、未交战)
        internal static List<Kingdom> Candidates()
        {
            var list = new List<Kingdom>();
            try
            {
                var pk = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                if (pk == null) return list;
                foreach (var k in Kingdom.All)
                {
                    if (k == null || k == pk || k.IsEliminated) continue;
                    list.Add(k);
                }
            }
            catch { }
            return list;
        }

        // ================= v4.21x: AI 条约(仅 AI 之间自动缔约; 与玩家的条约仍走玩家 UI) =================
        //   花费 AI 权威(Decrees.AiAuth*); 月收益结给签约国金库, 开战/到期即作废
        private static readonly Dictionary<string, List<Treaty>> AiAll = new Dictionary<string, List<Treaty>>();   // 王国 -> 条约表
        private static int _aiLastDay = -1;

        private static List<Treaty> AiListOf(string ownerId)
        {
            List<Treaty> list;
            if (!AiAll.TryGetValue(ownerId, out list)) { list = new List<Treaty>(); AiAll[ownerId] = list; }
            return list;
        }

        internal static void AiMonthly(int day)
        {
            try
            {
                if (day < _aiLastDay) AiAll.Clear();   // 新档/读档 -> 重置
                _aiLastDay = day;
                var pk = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                // 1) 到期/开战清理 + 月收益结给签约国
                var owners = new List<string>(AiAll.Keys);
                for (int oi = 0; oi < owners.Count; oi++)
                {
                    var list = AiAll[owners[oi]];
                    var ok = FindKingdom(owners[oi]);
                    if (ok == null) { list.Clear(); continue; }
                    for (int i = list.Count - 1; i >= 0; i--)
                    {
                        var t = list[i];
                        var partner = t != null ? FindKingdom(t.Partner) : null;
                        if (t == null || partner == null || t.Left(day) <= 0) { list.RemoveAt(i); continue; }
                        bool war = false;
                        try { war = ok.IsAtWarWith(partner); } catch { }
                        if (war) { list.RemoveAt(i); continue; }   // 开战 -> 条约作废
                        if (t.Monthly > 0) WarEconomy.AddPublic(ok, t.Monthly);
                    }
                }
                // 2) 各国按性格与需求提出/接受条约
                foreach (var k in Kingdom.All)
                {
                    if (k == null || k.IsEliminated) continue;
                    if (pk != null && ReferenceEquals(pk, k)) continue;
                    try { AiSign(k, day); } catch { }
                }
            }
            catch (Exception ex) { DLog.Force("AI 条约月结异常: " + ex.Message); }
        }

        private static void AiSign(Kingdom k, int day)
        {
            if (WarEconomy.IsCrisis(k)) return;
            if (Decrees.AiAuthOf(k) < 150f) return;   // v5.x: 信誉/权威不足先攒, 不缔约
            float dipReserve = 0f;
            try { dipReserve = AiBrain.ReserveGold(k); } catch { }
            if (dipReserve > 1f && WarEconomy.GoldOfPublic(k) < dipReserve) return;   // 军费储备未满 -> 外交让路
            try { AiAgenda.Ensure(k, day); } catch { }
            int wars = 0;
            try { foreach (var x in Kingdom.All) { if (x == null || x.IsEliminated || ReferenceEquals(x, k)) continue; if (k.IsAtWarWith(x)) wars++; } } catch { }
            if (wars >= 2) return;
            int com = AiPersonality.CommerceOf(k), agg = AiPersonality.AggressionOf(k);
            if (MBRandom.RandomFloat > 0.15f + com / 300f) return;
            // 候选: 接壤、未交战、关系不太差的 AI 邻国
            Kingdom best = null;
            int bestRel = int.MinValue;
            foreach (var b in Kingdom.All)
            {
                if (b == null || b.IsEliminated || ReferenceEquals(b, k)) continue;
                var pk2 = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                if (pk2 != null && ReferenceEquals(pk2, b)) continue;   // 对玩家的条约由玩家 UI 发起
                bool war = false; try { war = k.IsAtWarWith(b); } catch { }
                if (war) continue;
                if (!AiDiplomacy.Borders(k, b)) continue;
                int rel = 0; try { rel = Diplomacy.Get(k, b); } catch { }
                if (rel < -30) continue;
                if (rel > bestRel) { bestRel = rel; best = b; }
            }
            if (best == null) return;
            // v5.x: 按经济需求选条约(缺粮->粮食/贸易, 缺引擎->工业/投资), 其余按性格兜底
            string type = null;
            try { type = AiAgenda.TreatyTypeFor(k); } catch { }
            if (type == null || Array.IndexOf(TypeIds, type) < 0)
            {
                if (bestRel >= 40 && MBRandom.RandomFloat < 0.25f) type = "ally";
                else if (com >= 60) type = MBRandom.RandomFloat < 0.6f ? "trade" : "invest";
                else if (agg >= 60 && wars > 0) type = "nap";
                else type = MBRandom.RandomFloat < 0.7f ? "trade" : "nap";
            }
            if (type == "ally" && bestRel < 40) type = "nap";   // 关系不够不硬结盟
            int ti = Array.IndexOf(TypeIds, type);
            if (ti < 0) return;
            var list = AiListOf(k.StringId);
            for (int i = 0; i < list.Count; i++)
                if (list[i] != null && list[i].Type == type && list[i].Partner == best.StringId) return;
            int cost = TypeCost[ti];
            if (!Decrees.AiAuthSpend(k, cost)) return;
            list.Add(new Treaty
            {
                Partner = best.StringId, Type = type, StartDay = day, Days = TypeDays[ti], Level = 1,
                Monthly = type == "trade" ? 800 : (type == "invest" ? 1500 : (type == "ally" ? 400 : 0))
            });
            try { Diplomacy.Change(k, best, type == "ally" ? 8 : 4); } catch { }
            AiAgenda.LogDip(k, "与 " + best.Name + " 签订" + NameOf(type) + "(权威 -" + cost + ")");
        }

        internal static string Save()
        {
            try
            {
                var sb = new System.Text.StringBuilder();
                sb.Append("C:").Append((int)Credit).Append('!');   // v4.203: 信誉
                for (int i = 0; i < All.Count; i++)
                {
                    var t = All[i];
                    if (t == null) continue;
                    sb.Append(t.Partner).Append(',').Append(t.Type).Append(',').Append(t.StartDay).Append(',')
                      .Append(t.Days).Append(',').Append(t.Broken ? "1" : "0").Append(',').Append(t.Level).Append(',').Append(t.Monthly).Append(';');
                }
                // v4.21x: AI 条约段(旧档无此段)
                sb.Append('~');
                foreach (var kv in AiAll)
                    for (int i = 0; i < kv.Value.Count; i++)
                    {
                        var t = kv.Value[i];
                        if (t == null) continue;
                        sb.Append(kv.Key).Append(',').Append(t.Partner).Append(',').Append(t.Type).Append(',')
                          .Append(t.StartDay).Append(',').Append(t.Days).Append(',').Append(t.Level).Append(',').Append(t.Monthly).Append(';');
                    }
                return sb.ToString();
            }
            catch { return ""; }
        }

        // 条约 UI(外交页入口): 一屏列出"对象 × 条约类型", 已生效的显示剩期; 撕毁用同名选项二次确认
        internal static void ShowUi()
        {
            try
            {
                var opts = new List<InquiryElement>();
                // 条款等级选择(标准/优厚/苛刻)
                for (int lv = 1; lv <= 3; lv++)
                {
                    int li = lv;
                    opts.Add(new InquiryElement("LV" + lv,
                        "【条款等级】" + LevelNames[lv - 1] + (SignLevel == lv ? " (当前)" : ""),
                        null, true,
                        "成本 ×" + LevelCostMult[lv - 1].ToString("F1") + " / 月收益 ×" + LevelGainMult[lv - 1].ToString("F1") + " / 期限 ×" + LevelDaysMult[lv - 1].ToString("F1")));
                }
                foreach (var k in Candidates())
                {
                    if (k == null) continue;
                    string kn = k.Name != null ? k.Name.ToString() : k.StringId;
                    for (int ti = 0; ti < TypeIds.Length; ti++)
                    {
                        var t = Find(k, TypeIds[ti]);
                        string label = t != null
                            ? ("撕毁 " + kn + " 的" + TypeNames[ti] + "·" + LevelNames[Math.Min(2, Math.Max(0, t.Level - 1))] + "(剩 " + t.Left(Politics.Today()) + " 日, 月 " + t.Monthly + ")")
                            : ("与 " + kn + " 签" + TypeNames[ti] + "(" + ((int)Math.Round(TypeCost[ti] * LevelCostMult[SignLevel - 1] * (1f + (60f - Credit) / 100f))) + " 权威)");
                        opts.Add(new InquiryElement(k.StringId + "|" + TypeIds[ti], label, null, true,
                            t != null ? "违约: 权威 -40, 合法性 -6, 关系 -15" : TypeEffect[ti]));
                        if (opts.Count >= 60) break;
                    }
                    if (opts.Count >= 60) break;
                }
                PanelInputGuard.ShowPopup(new MultiSelectionInquiryData(
                    "外交条约",
                    "权威: " + ((int)Politics.Authority) + "  ·  信誉: " + ((int)Credit) + " (低于 60 缔约更贵)\n"
                    + "当前条款等级: " + LevelNames[SignLevel - 1] + "\n" + StatusText(),
                    opts, true, 1, 1, "执行", "取消",
                    delegate (List<InquiryElement> sel)
                    {
                        if (sel == null || sel.Count == 0) return;
                        string id = sel[0].Identifier as string;
                        if (string.IsNullOrEmpty(id)) return;
                        if (id.StartsWith("LV", StringComparison.Ordinal))
                        {
                            int lv;
                            if (int.TryParse(id.Substring(2), out lv)) { SignLevel = Math.Min(3, Math.Max(1, lv)); }
                            ShowUi();   // 重新打开以刷新
                            return;
                        }
                        var f = id.Split('|');
                        if (f.Length != 2) return;
                        var k = FindKingdom(f[0]);
                        string msg = Find(k, f[1]) != null ? Break(k, f[1]) : Sign(k, f[1], SignLevel);
                        try { MapSelection.Message(msg); } catch { }
                        DLog.Force("条约UI: " + msg);
                    }, null, null, false));
            }
            catch (Exception ex) { DLog.Force("条约界面失败: " + ex.Message); }
        }

        internal static void Load(string data)
        {
            try
            {
                All.Clear(); AiAll.Clear();
                if (string.IsNullOrEmpty(data)) return;
                int tilde = data.IndexOf('~');
                string aiPart = tilde >= 0 ? data.Substring(tilde + 1) : null;
                if (tilde >= 0) data = data.Substring(0, tilde);
                int bang = data.IndexOf('!');
                if (bang > 0)
                {
                    int cr;
                    if (int.TryParse(data.Substring(2, bang - 2), out cr)) Credit = cr;
                    data = data.Substring(bang + 1);
                }
                foreach (var seg in data.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var f = seg.Split(',');
                    if (f.Length < 5) continue;
                    int sd, days, br, lv = 1, mo = 0;
                    if (!int.TryParse(f[2], out sd) || !int.TryParse(f[3], out days)) continue;
                    int.TryParse(f[4], out br);
                    if (f.Length > 5) int.TryParse(f[5], out lv);
                    if (f.Length > 6) int.TryParse(f[6], out mo);
                    All.Add(new Treaty { Partner = f[0], Type = f[1], StartDay = sd, Days = days, Broken = br == 1, Level = lv, Monthly = mo });
                }
                if (!string.IsNullOrEmpty(aiPart))
                {
                    foreach (var seg in aiPart.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        var f = seg.Split(',');
                        if (f.Length < 7) continue;
                        int sd, days, lv2, mo2;
                        if (!int.TryParse(f[3], out sd) || !int.TryParse(f[4], out days)) continue;
                        int.TryParse(f[5], out lv2);
                        int.TryParse(f[6], out mo2);
                        AiListOf(f[0]).Add(new Treaty { Partner = f[1], Type = f[2], StartDay = sd, Days = days, Level = lv2, Monthly = mo2 });
                    }
                }
            }
            catch { }
        }
    }
}
