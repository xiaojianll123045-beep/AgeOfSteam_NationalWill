using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;

namespace FeudalInternalAffairs
{
    // 建造流程(设计 3.1~3.6): 每日按队列投入建造点与材料, 达标完工
    internal static class Construction
    {
        // 返回 [推进项数, 完工数, 停滞项数]
        internal static int[] TickSettlement(Settlement s, SettlementBuildings sb, ItemRoster roster)
        {
            if (sb == null || sb.Queue.Count == 0) return new int[3];
            if (DailySettlement.IsHalted(s)) return new int[3];

            // 1) 建造点池 = Σ(本地部门数量 × 档位点数); 每城独立(3.1/3.2)
            float pool = 0f;
            foreach (var g in sb.Groups)
            {
                if (g == null || g.Count <= 0) continue;
                var def = g.Def;
                if (def == null || !def.IsEffect) continue;
                foreach (var outp in def.Outputs)
                    if (outp != null && outp.Good == BuildDefs.EffConstruction)
                        pool += outp.Value(g.Mode) * g.Count;
            }
            if (pool <= 0.01f) return new int[] { 0, 0, sb.Queue.Count };   // 没有部门不能开工

            float eff = Efficiency(s);
            int advanced = 0, done = 0, stalled = 0;

            foreach (var q in sb.Queue)
            {
                if (q == null || q.Paused) continue;
                var def = q.Def;
                if (def == null) continue;
                if (pool <= 0.01f) break;

                // 每项每日最多投入 = 工时 × 20%(3.2)
                float cap = q.TotalWork * 0.2f;
                float points = Math.Min(pool, cap);
                if (points <= 0.01f) break;

                // 当日材料 = 一次性材料 × (投入 / 总工时) (3.4a + 12.4)
                float ratio = points / Math.Max(1f, q.TotalWork);
                float mWood, mStone, mIron;
                BuildDefs.BuildMaterials(q.Mode, out mWood, out mStone, out mIron);

                int w = MBRandom.RoundRandomized(mWood * ratio);
                int st = MBRandom.RoundRandomized(mStone * ratio);
                int ir = MBRandom.RoundRandomized(mIron * ratio);
                var woodItem = FeudalGoods.Item(FeudalGoods.Hardwood);
                var stoneItem = FeudalGoods.Item(FeudalGoods.Stone);
                var ironItem = FeudalGoods.Item(FeudalGoods.Iron);

                // 三种材料齐了才扣(避免扣一半又停滞)
                bool ok = true;
                if (w > 0 && (woodItem == null || roster.GetItemNumber(woodItem) < w)) ok = false;
                if (ok && st > 0 && (stoneItem == null || roster.GetItemNumber(stoneItem) < st)) ok = false;
                if (ok && ir > 0 && (ironItem == null || roster.GetItemNumber(ironItem) < ir)) ok = false;
                if (!ok)
                {
                    stalled++;   // 缺料停滞, 进度不倒退(3.3)
                    continue;
                }
                if (w > 0) roster.AddToCounts(woodItem, -w);
                if (st > 0) roster.AddToCounts(stoneItem, -st);
                if (ir > 0) roster.AddToCounts(ironItem, -ir);

                pool -= points;
                q.Progress += (int)Math.Round(points * eff);
                advanced++;

                if (q.Progress >= q.TotalWork)
                {
                    q.Progress = q.TotalWork;
                    var g = sb.GetOrCreate(q.DefId, q.Mode);
                    g.Mode = q.Mode;
                    g.Count += 1;
                    done++;
                    NotifyDone(s, q);
                }
            }

            // 移除完工项
            for (int i = sb.Queue.Count - 1; i >= 0; i--)
                if (sb.Queue[i] != null && sb.Queue[i].Progress >= sb.Queue[i].TotalWork)
                    sb.Queue.RemoveAt(i);

            return new int[] { advanced, done, stalled };
        }

        // 建造效率(3.6)
        internal static float Efficiency(Settlement s)
        {
            float e = 1f;
            try
            {
                if (DailySettlement.IsHalted(s)) e -= 0.5f;      // 围城/掠夺 −50%
                var t = s != null ? s.Town : null;
                if (t != null && (t.Loyalty < 50f || t.Security < 50f)) e -= 0.2f;   // 动乱 −20%
                if (EconomyWorld.Treasury.PenaltyDaysLeft > 0) e -= 0.75f;          // 破产 30 天内 −75%
            }
            catch { }
            return Math.Max(0.05f, e);   // 最低 5%
        }

        private static void NotifyDone(Settlement s, QueuedBuild q)
        {
            try
            {
                if (s == null || q == null) return;
                var b = NationalWillOrders.Behavior;
                var k = b != null ? b.NationKingdom : null;
                bool ours = k != null && s.OwnerClan != null && s.OwnerClan.Kingdom == k;
                var def = q.Def;
                string name = def != null ? def.Name : q.DefId;
                if (ours)
                    MapSelection.Message("建造完成: " + s.Name + " · " + name + "(" + BuildDefs.ModeName(q.Mode) + ")");
                if (DLog.Flag("econ")) DLog.Force("建造完成: " + s.StringId + " " + q.DefId + " " + BuildDefs.ModeName(q.Mode));
            }
            catch { }
        }
    }
}
