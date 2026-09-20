using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using TaleWorlds.Library;

namespace FeudalInternalAffairs
{
    // 革命阵营(文档 24.5; 对照 V3 Political_movement / Revolution 官方机制)
    // 运动激进度 >=75 -> 革命性 -> 组织革命(进度条, 8 天一次检查) -> 100 -> 内战/革命胜利
    internal class RevFaction
    {
        internal int Movement = -1;    // 运动索引
        internal int Group = -1;       // 集团
        internal int Law = -1;         // 诉求法律
        internal float Progress;       // 革命进度 0-100
        internal int StartDay = -9999;
        internal bool Active;          // 已爆发
        internal int LastCheckDay = -9999;
    }

    internal static class Revolution
    {
        internal static RevFaction Current;
        internal static int LastCivilWarEndDay = -9999;
        internal static bool Won;      // 上次内战结局: true=革命胜利
        internal static readonly HashSet<string> BrokenGroups = new HashSet<string>();   // 战败集团(政治力量 -100%, V3 官方)

        // 每日检查: 革命进度推进(V3: 基础间隔 8 周 -> 我们 8 天; 变化向激进度靠拢 cap -10~+25)
        internal static void TickDay(int day)
        {
            try
            {
                if (Current == null) return;
                if (InterestGroups.Movements.Count == 0) { Current = null; return; }
                if (Current.Movement < 0 || Current.Movement >= InterestGroups.Movements.Count) { Current = null; return; }
                var m = InterestGroups.Movements[Current.Movement];
                // 运动解散或激进度 <50 -> 革命停止组织(V3 官方)
                if (m.Radical < 50f)
                {
                    DLog.Force("革命: 运动激进度回落(" + (int)m.Radical + "), 革命解散");
                    InterestGroups.NotifyPlayer("革命组织已解散: 民怨回落", false);
                    Current = null;
                    return;
                }
                if (Current.Active) return;
                if (day - Current.LastCheckDay < 8) return;
                Current.LastCheckDay = day;
                float delta = (m.Radical - Current.Progress) * 0.25f * Institutions.RevolutionSlowMult();   // V3 官方: 内务机构 -10%/级
                if (delta > 25f) delta = 25f;
                if (delta < -10f) delta = -10f;
                Current.Progress += delta;
                if (Current.Progress < 0f) Current.Progress = 0f;
                if (Current.Progress >= 100f)
                {
                    Current.Progress = 100f;
                    BreakOut(day);
                }
            }
            catch (Exception ex) { DLog.Force("革命日结异常: " + ex.Message); }
        }

        // 运动达到 75 激进度 -> 组织革命(V3: >75 activism 成为 revolutionary)
        internal static void TryOrganize(int movementIdx, int day)
        {
            try
            {
                if (Current != null) return;
                if (movementIdx < 0 || movementIdx >= InterestGroups.Movements.Count) return;
                var m = InterestGroups.Movements[movementIdx];
                if (m.Radical < 75f) return;
                if (day - LastCivilWarEndDay < 84) return;   // 战后 1 年喘息
                Current = new RevFaction
                {
                    Movement = movementIdx,
                    Group = m.Group,
                    Law = m.Law,
                    Progress = 10f,
                    StartDay = day,
                    LastCheckDay = day
                };
                InterestGroups.NotifyPlayer("⚠ 革命组织出现: " + InterestGroups.NameOf(m.Group) + " 正在酝酿暴动(激进度 " + (int)m.Radical + "%)", true);
                DLog.Force("革命: 开始组织 -> " + InterestGroups.NameOf(m.Group) + " 诉求=" + (m.Law >= 0 ? LawSystem.NameOf(m.Law) : "让步"));
            }
            catch { }
        }

        // 每月: 高激进度运动使支持者激进化(V3: >50 activism 每月 0.5%~2.5% 支持者变激进)
        internal static void Monthly()
        {
            try
            {
                for (int i = 0; i < InterestGroups.Movements.Count; i++)
                {
                    var m = InterestGroups.Movements[i];
                    if (m.Radical < 50f) continue;
                    float pct = 0.005f + (m.Radical - 50f) / 50f * 0.02f;
                    InterestGroups.ShiftRadicalsOfGroup(m.Group, pct);
                }
            }
            catch { }
        }

        // 革命爆发: 内战(V3: 100% 进度 -> 革命国成立/内战)
        private static void BreakOut(int day)
        {
            try
            {
                Current.Active = true;
                Politics.CivilWar = true;
                Politics.CivilWarDay = day;
                Politics.Legitimacy = LawSystem.ClampF(Politics.Legitimacy - 20f, 0f, 100f);
                InterestGroups.NotifyPlayer("💥 革命爆发! " + InterestGroups.NameOf(Current.Group) + " 举旗造反(税收 -40%, 全境产出 -15%)", true);
                DLog.Force("革命: 爆发! 集团=" + InterestGroups.NameOf(Current.Group) + " 诉求=" + (Current.Law >= 0 ? LawSystem.NameOf(Current.Law) : "让步"));
            }
            catch { }
        }

        // 强力镇压革命(200 权威 + 军队满意; V3: 花费权威镇压运动)
        internal static string Suppress()
        {
            try
            {
                if (Current == null) return "当前没有革命组织";
                if (Politics.Authority < 200f) return "权威不足(需 200)";
                Politics.Authority -= 200f;
                Politics.Tyranny += 4f;
                InterestGroups.AddEventMod(Current.Group, -20);
                // 运动激进度大幅回落
                if (Current.Movement >= 0 && Current.Movement < InterestGroups.Movements.Count)
                {
                    var m = InterestGroups.Movements[Current.Movement];
                    m.Radical = Math.Max(0f, m.Radical - 45f);
                    m.SuppressDay = Politics.Today();
                }
                Politics.Legitimacy = LawSystem.ClampF(Politics.Legitimacy - 3f, 0f, 100f);
                InterestGroups.AddEventMod(7, 6);   // 军队支持
                bool wasActive = Current.Active;
                Current = null;
                if (wasActive)
                {
                    Politics.CivilWar = false;
                    LastCivilWarEndDay = Politics.Today();
                    Won = false;
                    foreach (var kv in Politics.Lords) kv.Value.Fear = Politics.Clamp(kv.Value.Fear + 20, 0, 100);
                    InterestGroups.NotifyPlayer("已血腥镇压革命: 暴政 +4, 恐惧 +20, 运动激进度 -45", true);
                }
                DLog.Force("革命: 镇压 (内战=" + wasActive + ")");
                return wasActive ? "革命已镇压(暴政 +4, 全国恐惧 +20)" : "革命组织已被驱散(激进度 -45)";
            }
            catch { return "镇压失败"; }
        }

        // 妥协: 接受运动诉求(V3: 让步)
        internal static string Concede()
        {
            try
            {
                if (Current == null) return "当前没有革命组织";
                if (EconomyWorld.Treasury.Gold < 3000) return "国库不足(需 3000 第纳尔)";
                EconomyWorld.TreasurySpend(3000);
                Fiscal.AddCourt(3000);
                InterestGroups.AddEventMod(Current.Group, 18);
                // 诉求法律直接推进一档(V3: 革命国直接生效诉求法)
                string lawTxt = "";
                if (Current.Law >= 0 && !LawSystem.Maxed(Current.Law))
                {
                    lawTxt = " 并特颁《" + LawSystem.NameOf(Current.Law) + "》";
                }
                bool wasActive = Current.Active;
                if (wasActive)
                {
                    Politics.CivilWar = false;
                    LastCivilWarEndDay = Politics.Today();
                    Won = false;
                    foreach (var kv in Politics.Lords) kv.Value.Anger = Politics.Clamp(kv.Value.Anger - 25, 0, 100);
                    Politics.Legitimacy = LawSystem.ClampF(Politics.Legitimacy + 6f, 0f, 100f);
                }
                Politics.Authority = Math.Max(0f, Politics.Authority - 60f);
                Current = null;
                InterestGroups.NotifyPlayer("已妥协平息革命" + lawTxt, false);
                DLog.Force("革命: 妥协 (内战=" + wasActive + ")");
                return "已对革命让步: 运动平息, 权威 -60" + lawTxt;
            }
            catch { return "妥协失败"; }
        }

        // 内战/革命拖满一年(V3: 革命胜利场景)
        internal static void OnCivilWarTimeout(int day)
        {
            try
            {
                LastCivilWarEndDay = day;
                Won = Current != null;
                if (Current != null)
                {
                    // 革命胜利: 诉求法强制推进 + 执政集团洗牌 + 合法性重建(V3: 革命国法律生效)
                    if (Current.Law >= 0 && !LawSystem.Maxed(Current.Law))
                    {
                        LawSystem.ForceAdvance(Current.Law);
                        InterestGroups.NotifyPlayer("革命胜利: 《" + LawSystem.NameOf(Current.Law) + "》被强制颁布", true);
                    }
                    if (Current.Group > 0)
                    {
                        InterestGroups.G[Current.Group].InGov = true;
                        InterestGroups.AddEventMod(Current.Group, 20);
                        // 旧执政集团中满意度最低者出阁
                        int worst = -1;
                        for (int g = 1; g < InterestGroups.GroupCount; g++)
                            if (InterestGroups.G[g].InGov && g != Current.Group &&
                                (worst < 0 || InterestGroups.G[g].Satisfaction < InterestGroups.G[worst].Satisfaction)) worst = g;
                        if (worst > 0 && InterestGroups.CabinetCount() > InterestGroups.CabinetLimit())
                        {
                            InterestGroups.G[worst].InGov = false;
                            InterestGroups.AddEventMod(worst, -25);
                        }
                    }
                    Politics.Legitimacy = 45f;
                    Pops.ShiftRadicals(-0.15f);   // V3: 内战后激进者转中立
                    foreach (var kv in Politics.Lords)
                    {
                        kv.Value.Anger = Politics.Clamp(kv.Value.Anger - 20, 0, 100);
                        kv.Value.Fear = Politics.Clamp(kv.Value.Fear + 10, 0, 100);
                    }
                    InterestGroups.NotifyPlayer("革命结束: 政府改组, 合法性重建至 45", true);
                }
                else
                {
                    Pops.ShiftRadicals(-0.15f);
                }
                Current = null;
                Politics.CivilWar = false;
                DLog.Force("革命: 内战结算 -> " + (Won ? "革命胜利" : "政府幸存"));
            }
            catch (Exception ex) { DLog.Force("革命结算异常: " + ex.Message); }
        }

        internal static string StatusText()
        {
            try
            {
                if (Current == null) return "";
                string s = "革命: " + InterestGroups.NameOf(Current.Group)
                    + (Current.Law >= 0 ? " 诉求《" + LawSystem.NameOf(Current.Law) + "》" : " 要求让步")
                    + " · 进度 " + (int)Current.Progress + "%";
                if (Current.Active) s += " [已爆发]";
                return s;
            }
            catch { return ""; }
        }

        // ================= 存档 FIA_Rev =================
        internal static string Save()
        {
            try
            {
                if (Current == null) return "v1;";
                var sb = new StringBuilder();
                sb.Append("v1;");
                sb.Append(Current.Movement).Append(',').Append(Current.Group).Append(',').Append(Current.Law).Append(',')
                  .Append(Current.Progress.ToString("F1", CultureInfo.InvariantCulture)).Append(',')
                  .Append(Current.StartDay).Append(',').Append(Current.Active ? "1" : "0").Append(',')
                  .Append(Current.LastCheckDay).Append(';');
                sb.Append(Won ? "1" : "0").Append(',').Append(LastCivilWarEndDay).Append(';');
                return sb.ToString();
            }
            catch { return "v1;"; }
        }

        internal static void Load(string data)
        {
            try
            {
                if (string.IsNullOrEmpty(data)) return;
                var seg = data.Split(';');
                if (seg.Length > 1 && !string.IsNullOrEmpty(seg[1]))
                {
                    var f = seg[1].Split(',');
                    if (f.Length >= 7)
                    {
                        var r = new RevFaction();
                        int x;
                        float p;
                        if (int.TryParse(f[0], out x)) r.Movement = x;
                        if (int.TryParse(f[1], out x)) r.Group = x;
                        if (int.TryParse(f[2], out x)) r.Law = x;
                        if (float.TryParse(f[3], NumberStyles.Float, CultureInfo.InvariantCulture, out p)) r.Progress = p;
                        if (int.TryParse(f[4], out x)) r.StartDay = x;
                        r.Active = f[5] == "1";
                        if (int.TryParse(f[6], out x)) r.LastCheckDay = x;
                        Current = r;
                    }
                }
                if (seg.Length > 2 && !string.IsNullOrEmpty(seg[2]))
                {
                    var f = seg[2].Split(',');
                    if (f.Length > 0) Won = f[0] == "1";
                    int x;
                    if (f.Length > 1 && int.TryParse(f[1], out x)) LastCivilWarEndDay = x;
                }
                DLog.Force("革命: 读档 " + (Current != null ? StatusText() : "无"));
            }
            catch { }
        }

        internal static void Reset()
        {
            Current = null;
            Won = false;
            LastCivilWarEndDay = -9999;
            BrokenGroups.Clear();
        }
    }
}
