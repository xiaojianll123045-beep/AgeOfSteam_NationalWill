using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;

namespace FeudalInternalAffairs
{
    // 商队与商路安全(文档 20.10): 贸易容量 -> 商队; 盗匪损耗; 巡逻安保
    internal static class Caravans
    {
        internal struct Info { public int Count; public int Lost; public float Security; }

        internal static readonly Dictionary<string, Info> ByTown = new Dictionary<string, Info>();

        // 贸易容量(与 BuildTrade 同口径: @trade_cap 输出)
        internal static int CapacityOf(string sid)
        {
            try
            {
                var sb = EconomyWorld.Find(sid);
                if (sb == null) return 0;
                int trade = 0;
                for (int i = 0; i < sb.Groups.Count; i++)
                {
                    var g = sb.Groups[i];
                    if (g == null || g.Count <= 0) continue;
                    var def = BuildDefs.Get(g.DefId);
                    if (def == null || !def.IsEffect) continue;
                    for (int j = 0; j < def.Outputs.Count; j++)
                    {
                        var o = def.Outputs[j];
                        if (o != null && o.Good == BuildDefs.EffTradeCap) trade += (int)Math.Round(o.Value(g.Mode) * g.Count);
                    }
                }
                return trade;
            }
            catch { return 0; }
        }

        private static float SecurityOf(string sid)
        {
            try
            {
                var sb = EconomyWorld.Find(sid);
                if (sb == null) return 0f;
                float s = 0f;
                for (int i = 0; i < sb.Groups.Count; i++)
                {
                    var g = sb.Groups[i];
                    if (g == null || g.Count <= 0) continue;
                    if (g.DefId == "patrol") s += 0.2f * g.Count;
                    else if (g.DefId == "watchtower") s += 0.1f * g.Count;
                    else if (g.DefId == "militia_camp") s += 0.1f * g.Count;
                }
                return s > 1f ? 1f : s;
            }
            catch { return 0f; }
        }

        // 日结算: 对玩家王国各城, 按商队数掷损耗; 损耗从仓库扣货
        internal static void Daily()
        {
            ByTown.Clear();
            try
            {
                var pk = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                if (pk == null) return;
                foreach (var t in pk.Fiefs)
                {
                    if (t == null || !t.IsTown || t.Settlement == null) continue;
                    string sid = t.Settlement.StringId;
                    int cap = CapacityOf(sid);
                    int count = cap / 100;
                    float sec = SecurityOf(sid);
                    var info = new Info { Count = count, Security = sec, Lost = 0 };
                    if (count > 0)
                    {
                        float chance = 0.03f * (1f - sec);
                        int lost = 0;
                        var roster = t.Settlement.ItemRoster;
                        for (int i = 0; i < count; i++)
                        {
                            if (MBRandom.RandomFloat >= chance) continue;
                            // 一支商队被劫: 从仓库随机扣 10 件货物
                            int take = 10;
                            bool taken = false;
                            for (int gi = 0; gi < FeudalGoods.Main.Count && take > 0; gi++)
                            {
                                var g = FeudalGoods.Main[gi];
                                var item = FeudalGoods.Item(g.Id);
                                if (item == null || roster == null) continue;
                                int have = roster.GetItemNumber(item);
                                if (have <= 0) continue;
                                int del = Math.Min(have, take);
                                roster.AddToCounts(item, -del);
                                take -= del;
                                taken = true;
                            }
                            if (taken) { lost++; }
                        }
                        if (lost > 0)
                        {
                            info.Lost = lost;
                            DLog.Force("商队: " + t.Name + " 被劫 " + lost + " 支(安保 " + (int)(sec * 100) + "%)");
                        }
                    }
                    ByTown[sid] = info;
                }
            }
            catch (Exception ex) { DLog.Force("商队结算异常: " + ex.Message); }
        }

        internal static string LineOf(string sid)
        {
            Info i;
            if (!ByTown.TryGetValue(sid, out i)) return "—";
            return i.Count + " 支 · 损 " + i.Lost + " · 安 " + (int)(i.Security * 100f) + "%";
        }
    }
}
