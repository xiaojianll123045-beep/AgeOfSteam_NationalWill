using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;

namespace FeudalInternalAffairs
{
    // 战争动员(文档 24.9; 对照 V3 Army Model 动员: 战时兵力爆发 + 战后遣散 + 期间经济惩罚)
    internal static class WarMobilization
    {
        internal static bool Active;
        internal static int StartDay = -9999;
        internal static int EndDay = -9999;
        internal const int DurationDays = 60;      // 一次动员持续 60 天

        // v4.241: 电报机(军团支援槽 2, 效果"动员 +15%") -> 动员窗口 +15%
        private static int EquipDurationBonus()
        {
            try
            {
                for (int i = 0; i < DefArmy.Legions.Count; i++)
                {
                    var lg = DefArmy.Legions[i];
                    if (lg == null || lg.Supports == null || lg.Supports.Length < 3 || !lg.Supports[2]) continue;
                    var p = DefArmy.LegionParty(lg);
                    if (p != null && DefArmy.HasSupport(p, 2)) return (int)Math.Round(DurationDays * 0.15);
                }
            }
            catch { }
            return 0;
        }
        internal static int CallCount;             // 累计动员次数
        private static int _lastDay = -1;

        internal static string Call()
        {
            try
            {
                if (Active) return "已在动员状态(剩余 " + Math.Max(0, EndDay - Politics.Today()) + " 天)";
                var k = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                if (k == null) return "没有国家";
                if (!AtWar(k)) return "当前没有战争, 无需动员";
                int day = Politics.Today();
                Active = true;
                StartDay = day;
                EndDay = day + DurationDays + EquipDurationBonus();   // v4.241: 电报机"动员 +15%"
                CallCount++;
                // 动员冲击: 合法性 +2(同仇敌忾), 农民不满
                Politics.Legitimacy = LawSystem.ClampF(Politics.Legitimacy + 2f, 0f, 100f);
                InterestGroups.AddEventMod(6, -8);   // 农民被抽丁
                InterestGroups.AddEventMod(7, 10);   // 军队振奋
                InterestGroups.NotifyPlayer("⚔ 全国动员! 60 天内: 征召池 ×1.5 · 军事 +10% · 税收 -10%", true);
                DLog.Force("动员: 开始(第 " + day + " 天)");
                return "已发布动员令: 60 天(征召池 ×1.5, 军事 +10%, 税收 -10%)";
            }
            catch { return "动员失败"; }
        }

        internal static string Dismiss()
        {
            try
            {
                if (!Active) return "当前不在动员状态";
                Active = false;
                int day = Politics.Today();
                Pops.ShiftRadicals(0.015f);          // 返乡潮
                InterestGroups.AddEventMod(6, 6);    // 农民松口气
                InterestGroups.NotifyPlayer("遣散动员兵: 全国激进 +1.5%", false);
                DLog.Force("动员: 主动遣散(第 " + day + " 天)");
                return "已遣散动员兵(激进 +1.5%)";
            }
            catch { return "遣散失败"; }
        }

        internal static void Daily(int day)
        {
            try
            {
                if (day == _lastDay) return;
                _lastDay = day;
                try { AutoDecide(day); } catch { }   // v5.x AI: 按统一大脑 Goal/Threat 自动动员/复员
                if (!Active) return;
                var k = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                if (k != null && !AtWar(k))
                {
                    DLog.Force("动员: 战争结束, 自动遣散");
                    Dismiss();
                    return;
                }
                if (day >= EndDay)
                {
                    DLog.Force("动员: 期满, 自动遣散");
                    Dismiss();
                }
            }
            catch { }
        }

        private static bool AtWar(Kingdom k)
        {
            try
            {
                foreach (var other in Kingdom.All)
                {
                    if (other == null || other == k || other.IsEliminated) continue;
                    if (k.IsAtWarWith(other)) return true;
                }
            }
            catch { }
            return false;
        }

        // v5.x AI: 动员决策(周结) —— Goal=Survival/Military 且威胁高 -> 动员; 和平/战略转向 -> 复员
        private static int _lastAutoDay = -1;

        private static void AutoDecide(int day)
        {
            try
            {
                if (TaleWorlds.CampaignSystem.CampaignTime.Now.GetDayOfWeek != 0) return;   // 周结(骑砍 1 月 = 7 天)
                if (day == _lastAutoDay) return;
                _lastAutoDay = day;
                var k = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                if (k == null || k.IsEliminated) return;
                AiDirector.Goal goal = AiBrain.GoalOf(k);
                float threat = AiBrain.ThreatOf(k);   // 0~1
                if (!AtWar(k))
                {
                    if (Active) { DLog.Force("动员: 和平, 自动复员(AI)"); Dismiss(); }
                    return;
                }
                if ((goal == AiDirector.Goal.Survival || goal == AiDirector.Goal.Military) && threat >= 0.6f)
                {
                    if (!Active)
                    {
                        Call();
                        try { AiWarDirector.Log(k, "威胁 " + (int)(threat * 100f) + ", 发布动员令"); } catch { }
                    }
                    return;
                }
                // 战时但战略转向经济/政治/整合且威胁低 -> 复员
                if (Active && goal != AiDirector.Goal.Survival && goal != AiDirector.Goal.Military && threat < 0.4f)
                {
                    DLog.Force("动员: 战略转向(非军事), 自动复员(AI)");
                    Dismiss();
                    try { AiWarDirector.Log(k, "战略转向, 遣散动员兵"); } catch { }
                }
            }
            catch { }
        }

        // 效果乘数(接既有系统)
        internal static float TaxMult() { return Active ? 0.90f : 1f; }
        internal static float MilitaryMult() { return Active ? 1.10f : 1f; }
        internal static float ConscriptMult() { return Active ? 1.5f : 1f; }

        internal static string StatusText()
        {
            try
            {
                if (!Active) return "动员: 未动员";
                return "动员中: 剩余 " + Math.Max(0, EndDay - Politics.Today()) + " 天(征召池 ×1.5 · 税收 -10%)";
            }
            catch { return ""; }
        }

        internal static string Save()
        {
            return "v1;" + (Active ? "1" : "0") + "," + StartDay + "," + EndDay + "," + CallCount + ";";
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
                    if (f.Length >= 4)
                    {
                        Active = f[0] == "1";
                        int x;
                        if (int.TryParse(f[1], out x)) StartDay = x;
                        if (int.TryParse(f[2], out x)) EndDay = x;
                        if (int.TryParse(f[3], out x)) CallCount = x;
                    }
                }
                DLog.Force("动员: 读档 " + StatusText());
            }
            catch { }
        }

        internal static void Reset()
        {
            Active = false;
            StartDay = -9999;
            EndDay = -9999;
            CallCount = 0;
            _lastDay = -1;
            _lastAutoDay = -1;
        }
    }

}

