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
                EndDay = day + DurationDays;
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
        }
    }

}

