using System;
using System.Text;

namespace FeudalInternalAffairs
{
    // ==================== v4.290 修复探针 ====================
    // 目的: 本轮改了近 30 处(教程/建筑/指挥链/海战/军粮/军饷/税收/记账)。
    //   计数只放内存、不落盘, 每游戏日由 EconomyBehavior 的日结调一次 DailyReport(),
    //   往日志打一行 "[日诊断]"。跑几天后把这行抓出来, 就能一次性判断:
    //     · 每条补丁到底有没有生效(命中计数是否在涨; 若恒 0 说明补丁没挂上或判据不成立)
    //     · AiGuard 的 Prefix 有没有被 JIT 内联绕过(hit 是否为 0 —— 这是唯一还没定的方法论问题)
    //     · 双重结算的新判据命中的是哪一条(打标 vs 玩家在场上)
    //     · 军粮/舰队/税收/未付支出的实际量级
    // 检索用唯一前缀 "[日诊断]" 即可, 不会被别的日志淹没。
    internal static class FixProbe
    {
        // ---- 补丁命中计数(在各 patch 里 ++) ----
        internal static int HitSettlementIncomeSkip;   // 税收接管: 原版 AddSettlementIncome 被跳过
        internal static int HitPartyExpenseSkip;       // 军饷/补金库/双扣: AddPartyExpense 被跳过
        internal static int HitPartySizeWiden;         // 舰队: PartySizeLimit 被放宽
        internal static int HitAiGuard;                // AiGuard: Prefix 被执行(恒 0 = 补丁没生效)
        internal static int HitAiGuardBlock;           // AiGuard: 真的屏蔽了国防军
        internal static int HitRefireSettle;           // CommandTimeout: 1 秒重发走的"聚落"分支(进城的必要条件)
        internal static int HitRefirePoint;            // CommandTimeout: 重发走的"点"分支
        internal static int CmdByFlag, CmdByPresence;  // 双重结算: 判据①(显式打标) / 判据②(玩家挂在场上)

        // ---- 未付支出(被日初基线免除的那部分) ----
        internal static long UnpaidToday;
        internal static long UnpaidTotal;

        // 日初基线发现国库为负时调用
        internal static void NoteUnpaid(long v)
        {
            try { if (v > 0) { UnpaidToday += v; UnpaidTotal += v; } } catch { }
        }

        internal static void DailyReport(int day)
        {
            try
            {
                var sb = new StringBuilder();
                sb.Append("[日诊断] 第").Append(day).Append("天");

                try { sb.Append(" 国库=").Append(EconomyWorld.Treasury.Gold); } catch { }
                try { sb.Append(" 今日收=").Append(Fiscal.TodayIncome).Append(" 支=").Append(Fiscal.TodayExpense); } catch { }
                sb.Append(" 未付=").Append(UnpaidToday).Append("(累计").Append(UnpaidTotal).Append(')');

                sb.Append(" || 补丁: 税接管=").Append(HitSettlementIncomeSkip)
                  .Append(" 军饷跳过=").Append(HitPartyExpenseSkip)
                  .Append(" 舰队上限=").Append(HitPartySizeWiden);
                sb.Append(" AiGuard=").Append(HitAiGuard).Append('/').Append(HitAiGuardBlock);
                sb.Append(" 重发聚落=").Append(HitRefireSettle).Append(" 重发点=").Append(HitRefirePoint);
                sb.Append(" 双结算标/在场=").Append(CmdByFlag).Append('/').Append(CmdByPresence);

                // ---- 军团: 数量 / 饥饿数 / 最少的那个粮袋 ----
                try
                {
                    int n = DefArmy.Legions != null ? DefArmy.Legions.Count : 0;
                    int starve = 0, minFood = int.MaxValue, noFood = 0;
                    var grain = FeudalGoods.Item(FeudalGoods.Grain);
                    for (int i = 0; i < n; i++)
                    {
                        var lg = DefArmy.Legions[i];
                        if (lg == null) continue;
                        if (lg.Starve > 0) starve++;
                        var p = DefArmy.LegionParty(lg);
                        if (p == null || grain == null) continue;
                        int g = 0;
                        try { g = p.ItemRoster.GetItemNumber(grain); } catch { }
                        if (g <= 0) noFood++;
                        if (g < minFood) minFood = g;
                    }
                    sb.Append(" || 军团=").Append(n).Append(" 饥饿=").Append(starve)
                      .Append(" 断粮=").Append(noFood);
                    if (minFood != int.MaxValue) sb.Append(" 最少粮=").Append(minFood);
                }
                catch { }

                // ---- 舰队: 数量 / 水手 / 原版上限(判断会不会被 PartySizeLimit 卡住) ----
                try
                {
                    int f = NavalTaskForce.Forces != null ? NavalTaskForce.Forces.Count : 0;
                    sb.Append(" || 舰队=").Append(f);
                    int sailors = 0, capMin = int.MaxValue;
                    for (int i = 0; i < f; i++)
                    {
                        var fo = NavalTaskForce.Forces[i];
                        if (fo == null) continue;
                        var p = NavalTaskForce.PartyOf(fo);
                        if (p == null) continue;
                        try { sailors += p.MemberRoster.TotalManCount; } catch { }
                        try { int lim = p.Party.PartySizeLimit; if (lim > 0 && lim < capMin) capMin = lim; } catch { }
                    }
                    if (f > 0) sb.Append(" 水手=").Append(sailors);
                    if (capMin != int.MaxValue) sb.Append(" 队上限=").Append(capMin);
                }
                catch { }

                // ---- 教程: 挑战数(12 = 海军链没进去; 18 = 海上篇章已生效) ----
                try { sb.Append(" || 挑战数=").Append(Guide.TotalCount); } catch { }

                // ---- 坐镇模型: 活动模型的实际类型 + 它的 BaseModel ----
                //   确认链尾确实是 mod 的 FeudalCombatModel(它 override 了全部 13 个成员并转发给
                //   BaseModel)。装了 NavalDLC 时 BaseModel 应当显示 NavalDLCCombatSimulationModel ——
                //   这就是"海战坐镇转发已生效"的直接证据。
                try
                {
                    var m = TaleWorlds.CampaignSystem.Campaign.Current != null
                        ? TaleWorlds.CampaignSystem.Campaign.Current.Models.CombatSimulationModel : null;
                    sb.Append(" || 坐镇模型=").Append(m != null ? m.GetType().Name : "null");
                    try
                    {
                        var bf = m != null ? m.GetType().BaseType : null;
                        if (bf != null) sb.Append(" extends ").Append(bf.Name);
                    }
                    catch { }
                }
                catch { }

                DLog.Force(sb.ToString());
                UnpaidToday = 0;
            }
            catch { }
        }
    }
}
