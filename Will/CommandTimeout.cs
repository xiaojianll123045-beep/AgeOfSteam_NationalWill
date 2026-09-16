using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem.Party;

namespace FeudalInternalAffairs
{
    // 指挥超时: 被玩家下过命令的部队会禁止 AI 自行决策; 如果 10 秒内没有新命令,
    // 就放它自由(AI 恢复自主行动), 免得整支军队一直呆在原地。
    // 计时用游戏时间(暂停时不走), 所以"10秒"是游戏内秒数。
    internal static class CommandTimeout
    {
        private const float TimeoutSeconds = 10f;

        private static readonly Dictionary<MobileParty, float> Timers = new Dictionary<MobileParty, float>();
        private static readonly List<MobileParty> Finished = new List<MobileParty>();

        // 每次下命令时刷新计时
        internal static void Touch(MobileParty p)
        {
            try
            {
                if (p == null || !p.IsActive) return;
                Timers[p] = TimeoutSeconds;
            }
            catch { }
        }

        internal static void Tick(float dt)
        {
            if (Timers.Count == 0) return;
            try
            {
                Finished.Clear();
                foreach (var kv in Timers)
                {
                    float left = kv.Value - dt;
                    if (left <= 0f) Finished.Add(kv.Key);
                    else Timers[kv.Key] = left;
                }
                foreach (var p in Finished)
                {
                    // 还在选中状态 = 玩家还拿着它, 不放
                    if (MapSelection.Is(p))
                    {
                        Timers[p] = 1f;   // 1 秒后再看
                        continue;
                    }
                    // 军团成员: 军团在管着它(走向军团长/跟随军团), 别放给 AI 乱跑
                    // (离开军团后 p.Army 变 null, 下一秒就会正常放行)
                    try
                    {
                        if (p != null && p.IsActive && p.Army != null)
                        {
                            Timers[p] = 1f;
                            continue;
                        }
                    }
                    catch { }
                    Timers.Remove(p);
                    try
                    {
                        if (p != null && p.IsActive)
                        {
                            p.Ai.SetDoNotMakeNewDecisions(false);
                            DLog.Force("指挥超时: " + MapSelection.NameOf(p) + " 恢复自由行动");
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        internal static void Clear()
        {
            Timers.Clear();
        }
    }
}
