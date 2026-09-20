using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
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
        internal int Era;         // 1-4
        internal string Req;      // 前置科技 id(空=无)
        internal float Tax, Output, Mil, Radical, Lit, Authority;   // 效果(接既有系统)
    }

    // 科技研究(V3 官方口径): 三树并行研究; 创新 = 基础 + 大学; 
    // 投入上限 = 50 + 1.5×识字率(V3 公式, 缩放 1/10); 超上限 -> 技术传播; 成本 = 时代 + 未研究惩罚
    internal static class Research
    {
        internal const int TreeCount = 3;
        internal static readonly string[] TreeNames = { "生产", "军事", "社会" };

        // 时代基础成本(V3: 7500/10000/12500/15000/17500 -> 缩放 1/100)
        private static readonly float[] EraCost = { 75f, 100f, 125f, 150f };

        internal static readonly List<TechDef> All = new List<TechDef>
        {
            // ===== 生产 =====
            new TechDef { Id="heavy_plow", Name="重犁", Tree=0, Era=1, Effect="农业产出 +8%", Output=0.08f },
            new TechDef { Id="rotation", Name="三圃轮作", Tree=0, Era=1, Req="heavy_plow", Effect="农业产出 +8%", Output=0.08f },
            new TechDef { Id="watermill", Name="水车磨坊", Tree=0, Era=2, Req="rotation", Effect="加工产出 +10%", Output=0.10f },
            new TechDef { Id="blast", Name="高炉", Tree=0, Era=2, Req="watermill", Effect="加工产出 +10%, 军事 +5%", Output=0.10f, Mil=0.05f },
            new TechDef { Id="sawmill", Name="风力锯木", Tree=0, Era=3, Req="blast", Effect="产出 +10%", Output=0.10f },
            new TechDef { Id="ledger", Name="复式记账", Tree=0, Era=3, Req="blast", Effect="税收 +8%", Tax=0.08f },
            // ===== 军事 =====
            new TechDef { Id="chainmail", Name="锁甲", Tree=1, Era=1, Effect="军事 +8%", Mil=0.08f },
            new TechDef { Id="crossbow", Name="十字弩", Tree=1, Era=2, Req="chainmail", Effect="军事 +10%", Mil=0.10f },
            new TechDef { Id="plate", Name="板甲", Tree=1, Era=2, Req="chainmail", Effect="军事 +10%", Mil=0.10f },
            new TechDef { Id="castle", Name="城堡工事", Tree=1, Era=3, Req="plate", Effect="军事 +12%", Mil=0.12f },
            new TechDef { Id="gunpowder", Name="火药", Tree=1, Era=3, Req="crossbow", Effect="军事 +15%", Mil=0.15f },
            new TechDef { Id="standing", Name="常备军制", Tree=1, Era=4, Req="gunpowder", Effect="军事 +15%, 征召池 +10%", Mil=0.15f },
            // ===== 社会 =====
            new TechDef { Id="roman_law", Name="罗马法", Tree=2, Era=1, Effect="权威恢复 +1/月", Authority=1f },
            new TechDef { Id="university", Name="大学", Tree=2, Era=1, Effect="识字 +0.1%/月", Lit=0.001f },
            new TechDef { Id="printing", Name="印刷术", Tree=2, Era=2, Req="university", Effect="识字 +0.2%/月, 激进 -0.01%/日", Lit=0.002f, Radical=-0.0001f },
            new TechDef { Id="guild_charter", Name="行会特许学", Tree=2, Era=2, Req="roman_law", Effect="税收 +6%", Tax=0.06f },
            new TechDef { Id="bureaucracy_sci", Name="官僚文书", Tree=2, Era=3, Req="printing", Effect="权威 +2/月, 税收 +6%", Authority=2f, Tax=0.06f },
            new TechDef { Id="public_edu", Name="公共教育", Tree=2, Era=3, Req="printing", Effect="识字 +0.3%/月", Lit=0.003f }
        };

        internal static readonly HashSet<string> Done = new HashSet<string>();
        internal static readonly Dictionary<string, float> Progress = new Dictionary<string, float>();
        internal static readonly string[] CurrentId = new string[TreeCount];   // 每树当前研究
        internal static readonly string[] SpreadId = new string[TreeCount];    // 每树传播中的科技
        private static int _lastDay = -1;

        internal static TechDef Find(string id)
        {
            for (int i = 0; i < All.Count; i++) if (All[i].Id == id) return All[i];
            return null;
        }

        internal static bool IsDone(string id) { return id != null && Done.Contains(id); }

        internal static bool ReqOk(TechDef t)
        {
            return t != null && (string.IsNullOrEmpty(t.Req) || IsDone(t.Req));
        }

        // 时代成本 + 未研究惩罚(V3 公式: Era cost + 未研究数 × (时代差 × 0.25 × Era cost))
        internal static float CostOf(TechDef t)
        {
            try
            {
                if (t == null) return 100f;
                float baseCost = EraCost[Math.Min(EraCost.Length - 1, Math.Max(0, t.Era - 1))];
                int un = 0;
                for (int i = 0; i < All.Count; i++)
                {
                    var o = All[i];
                    if (o.Tree != t.Tree || IsDone(o.Id)) continue;
                    if (o.Era < t.Era) un++;
                }
                return baseCost + un * ((t.Era - 1) * 0.25f * baseCost);
            }
            catch { return 100f; }
        }

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

        // 每日创新产出: 基础 + 大学建筑(V3: 基础 50, 大学每级 1-2; 缩放 1/10)
        internal static float InnovationDaily()
        {
            try
            {
                float v = 0.5f;
                var pk = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                foreach (var kv in EconomyWorld.Buildings)
                {
                    var sb = kv.Value;
                    if (sb == null) continue;
                    bool mine = false;
                    try
                    {
                        foreach (var s in TaleWorlds.CampaignSystem.Settlements.Settlement.All)
                            if (s != null && s.StringId == kv.Key) { mine = s.MapFaction == pk; break; }
                    }
                    catch { }
                    if (!mine) continue;
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
                float gain = InnovationDaily() * ResearchSpeedMult();
                float cap = InnovationCap();
                float spent = Math.Min(gain, cap);
                float unspent = Math.Max(0f, gain - cap);
                // 每树: 当前研究 + 创新; 无当前则自动选成本最低的可研项
                for (int t = 0; t < TreeCount; t++)
                {
                    if (string.IsNullOrEmpty(CurrentId[t]) || IsDone(CurrentId[t]))
                        CurrentId[t] = AutoPick(t);
                    if (string.IsNullOrEmpty(CurrentId[t])) continue;
                    var td = Find(CurrentId[t]);
                    if (td == null) { CurrentId[t] = null; continue; }
                    float p;
                    Progress.TryGetValue(td.Id, out p);
                    p += spent;
                    Progress[td.Id] = p;
                    if (p >= CostOf(td))
                    {
                        Done.Add(td.Id);
                        Progress.Remove(td.Id);
                        CurrentId[t] = null;
                        InterestGroups.NotifyPlayer("科技完成: " + td.Name + "(" + td.Effect + ")", false);
                        DLog.Force("科技: 完成 " + td.Id + " " + td.Name);
                    }
                }
                // 技术传播(V3: 超出上限的创新 -> 传播; 识字驱动)
                if (unspent > 0.01f)
                {
                    for (int t = 0; t < TreeCount; t++)
                    {
                        if (string.IsNullOrEmpty(SpreadId[t]) || IsDone(SpreadId[t]) || CurrentId[t] == SpreadId[t])
                            SpreadId[t] = AutoPick(t);
                        if (string.IsNullOrEmpty(SpreadId[t])) continue;
                        var sd = Find(SpreadId[t]);
                        if (sd == null) continue;
                        float p;
                        Progress.TryGetValue(sd.Id, out p);
                        p += unspent * 0.2f;
                        Progress[sd.Id] = p;
                        if (p >= CostOf(sd))
                        {
                            Done.Add(sd.Id);
                            Progress.Remove(sd.Id);
                            SpreadId[t] = null;
                            InterestGroups.NotifyPlayer("技术传播: " + sd.Name + " 传入我国(" + sd.Effect + ")", false);
                            DLog.Force("科技: 传播传入 " + sd.Id);
                        }
                    }
                }
            }
            catch (Exception ex) { DLog.Force("研究日结异常: " + ex.Message); }
        }

        // 自动选择: 前置满足且未完成, 成本最低
        private static string AutoPick(int tree)
        {
            try
            {
                TechDef best = null;
                float bestCost = float.MaxValue;
                for (int i = 0; i < All.Count; i++)
                {
                    var t = All[i];
                    if (t.Tree != tree || IsDone(t.Id) || !ReqOk(t)) continue;
                    float c = CostOf(t);
                    float p; Progress.TryGetValue(t.Id, out p);
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
                if (!ReqOk(t)) return "前置未满足(需 " + (Find(t.Req) != null ? Find(t.Req).Name : "?") + ")";
                CurrentId[tree] = techId;
                DLog.Force("科技: 开始研究 " + techId);
                return "开始研究 " + t.Name + "(成本 " + (int)CostOf(t) + ")";
            }
            catch { return "研究操作失败"; }
        }

        // 研究速度: V3 官方 IG happy traits(+10%); 我们: 满意/忠诚集团加成
        internal static float ResearchSpeedMult()
        {
            try
            {
                float m = 1f;
                if (InterestGroups.SatOf(4) >= 25) m += 0.10f;   // 商帮(生产)
                if (InterestGroups.SatOf(7) >= 25) m += 0.10f;   // 军队(军事)
                if (InterestGroups.SatOf(2) >= 25) m += 0.10f;   // 地方贵族/学者(社会)
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
            try { float v = 0f; for (int i = 0; i < All.Count; i++) if (IsDone(All[i].Id)) v += All[i].Output; return 1f + v; } catch { return 1f; }
        }
        internal static float MilitaryMult()
        {
            try { float v = 0f; for (int i = 0; i < All.Count; i++) if (IsDone(All[i].Id)) v += All[i].Mil; return 1f + v; } catch { return 1f; }
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

        internal static string Save()
        {
            var sb = new StringBuilder();
            sb.Append("v2;");
            foreach (var id in Done) sb.Append(id).Append('|');
            sb.Append(';');
            foreach (var kv in Progress) sb.Append(kv.Key).Append(':').Append(kv.Value.ToString("F1", CultureInfo.InvariantCulture)).Append('|');
            sb.Append(';');
            for (int t = 0; t < TreeCount; t++) { if (t > 0) sb.Append(','); sb.Append(CurrentId[t] ?? ""); }
            sb.Append(';');
            return sb.ToString();
        }

        internal static void Load(string data)
        {
            try
            {
                Done.Clear(); Progress.Clear();
                for (int t = 0; t < TreeCount; t++) { CurrentId[t] = null; SpreadId[t] = null; }
                if (string.IsNullOrEmpty(data)) return;
                var seg = data.Split(';');
                if (seg.Length > 1 && !string.IsNullOrEmpty(seg[1]))
                    foreach (var id in seg[1].Split('|')) if (!string.IsNullOrEmpty(id)) Done.Add(id);
                if (seg.Length > 2 && !string.IsNullOrEmpty(seg[2]))
                {
                    foreach (var line in seg[2].Split('|'))
                    {
                        if (string.IsNullOrEmpty(line)) continue;
                        var kv = line.Split(':');
                        if (kv.Length < 2) continue;
                        float p;
                        if (float.TryParse(kv[1], NumberStyles.Float, CultureInfo.InvariantCulture, out p)) Progress[kv[0]] = p;
                    }
                }
                if (seg.Length > 3)
                {
                    var f = seg[3].Split(',');
                    for (int t = 0; t < TreeCount && t < f.Length; t++) CurrentId[t] = string.IsNullOrEmpty(f[t]) ? null : f[t];
                }
                DLog.Force("科技: 读档 已完成 " + Done.Count + " 项 " + ProgressText(0) + " / " + ProgressText(1) + " / " + ProgressText(2));
            }
            catch { }
        }

        internal static void Reset()
        {
            Done.Clear(); Progress.Clear();
            for (int t = 0; t < TreeCount; t++) { CurrentId[t] = null; SpreadId[t] = null; }
            _lastDay = -1;
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
                var pk = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                foreach (var kv in EconomyWorld.Buildings)
                {
                    var sb = kv.Value;
                    if (sb == null) continue;
                    bool mine = false;
                    try
                    {
                        foreach (var s in TaleWorlds.CampaignSystem.Settlements.Settlement.All)
                            if (s != null && s.StringId == kv.Key) { mine = s.MapFaction == pk; break; }
                    }
                    catch { }
                    if (!mine) continue;
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
                // 教育 -> 识字; 工作安全 -> 农民满意
                float lit = Level[0] * 0.0005f + Research.LiteracyBonus();
                if (lit > 0f) Pops.ShiftLiteracy(lit);
                if (Level[5] > 0) InterestGroups.AddEventMod(6, Level[5]);
                // 维护费(官僚负荷的财政面: 每点负荷 20 第纳尔)
                int cost = Load() * 20;
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