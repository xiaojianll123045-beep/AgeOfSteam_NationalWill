using System;
using HarmonyLib;
using TaleWorlds.Library;

namespace FeudalInternalAffairs
{
    // v4.92: 原版不重要消息过滤(用户要求; 反编译确认原版消息流入口为 InformationManager.DisplayMessage)。
    //   保留: 我们自己的消息(军务/财政/外交等关键词) 与 战争/和平/领地/任务类原版消息。
    //   屏蔽: 婚姻/出生/关系/繁荣/宴会等琐事刷屏(中英双语关键词, 可在此清单调整)。
    internal static class NotificationFilter
    {
        private static readonly string[] Keep =
        {
            "国防军", "内政", "财政", "国库", "外交", "军务", "募兵", "军团", "守备营",
            "减员", "欠饷", "断粮", "厌战", "接管", "立法", "行会", "税收", "货币", "征兵"
        };

        private static readonly string[] Mute =
        {
            "婚礼", "结婚", "迎娶", "订婚", "marriage", "wedding", "betroth",
            "出生", "诞生", "喜得", "gave birth", "newborn",
            "你们的关系", "关系变化", "relation",
            "繁荣", "prosperity",
            "宴会", "feast",
            "生日", "birthday"
        };

        [HarmonyPatch(typeof(InformationManager), "DisplayMessage")]
        internal static class DisplayMessageFilter
        {
            private static bool Prefix(InformationMessage message)
            {
                try
                {
                    string t = message != null ? message.Information : null;
                    if (string.IsNullOrEmpty(t)) return true;
                    for (int i = 0; i < Keep.Length; i++) if (t.Contains(Keep[i])) return true;
                    for (int i = 0; i < Mute.Length; i++)
                    {
                        if (t.IndexOf(Mute[i], StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            DLog.Info("已屏蔽原版消息: " + t);
                            return false;
                        }
                    }
                }
                catch { }
                return true;
            }
        }
    }
}
