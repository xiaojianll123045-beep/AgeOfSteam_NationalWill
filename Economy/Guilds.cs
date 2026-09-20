using System;
using System.Collections.Generic;
using System.Text;

namespace FeudalInternalAffairs
{
    // 行会(对应维多利亚3的"公司"; 文档 19.14 扩展)
    // 每个行会给一类建筑加成: 产出 +12% / 贸易容量 +30%; 成立需一次性费用
    internal class GuildDef
    {
        internal string Id;
        internal string Name;
        internal string Sprite;
        internal int Cost;         // 成立费用(第纳尔, 从国库扣)
        internal float OutputBonus;// 该类建筑产出加成
        internal string Cat;       // 作用建筑类别
        internal float CapacityBonus; // 贸易容量加成(仅商业行会)
        internal string Desc;
    }

    internal static class Guilds
    {
        internal static readonly List<GuildDef> All = new List<GuildDef>
        {
            new GuildDef { Id = "guild_industry", Name = "工业行会", Sprite = "fia_bld_ironworks", Cost = 8000, OutputBonus = 0.12f, Cat = "加工", Desc = "加工类建筑产出 +12%" },
            new GuildDef { Id = "guild_farm",     Name = "农业行会", Sprite = "fia_bld_farm",      Cost = 6000, OutputBonus = 0.12f, Cat = "资源", Desc = "资源类建筑产出 +12%" },
            new GuildDef { Id = "guild_trade",    Name = "商业行会", Sprite = "fia_bld_tradepost", Cost = 10000, OutputBonus = 0f,   Cat = null,   CapacityBonus = 0.30f, Desc = "贸易容量 +30%" },
            new GuildDef { Id = "guild_military", Name = "军工行会", Sprite = "fia_bld_weaponsmith", Cost = 9000, OutputBonus = 0.12f, Cat = "军事", Desc = "军事类建筑产出 +12%" },
            new GuildDef { Id = "guild_living",   Name = "民生行会", Sprite = "fia_bld_well",      Cost = 5000, OutputBonus = 0.12f, Cat = "生活", Desc = "生活类建筑产出 +12%" }
        };

        internal static readonly HashSet<string> Founded = new HashSet<string>();
        // v4.0 特许状(文档 20.5)
        internal static readonly HashSet<string> Chartered = new HashSet<string>();
        internal static readonly Dictionary<string, int> RevokedDay = new Dictionary<string, int>();
        internal const int CharterCost = 3000;

        internal static bool IsFounded(string id) { return id != null && Founded.Contains(id); }
        internal static bool IsChartered(string id) { return id != null && Chartered.Contains(id); }

        // 申请特许状(第 21 章: 商贸特许法令降低费用)
        internal static string ApplyCharter(string id, int today)
        {
            try
            {
                var def = Find(id);
                if (def == null) return "未知行会";
                if (!IsFounded(id)) return "行会尚未成立";
                if (IsChartered(id)) return "已有特许状";
                if (def.Cat != null && CategoryLevels(def.Cat) < 10) return "规模不足(该类建筑需 ≥10 级)";
                int cost = Math.Max(500, CharterCost - 500 * Politics.LawLevel(4));
                if (EconomyWorld.Treasury.Gold < cost) return "国库不足(需 " + cost.ToString("N0") + ")";
                EconomyWorld.TreasurySpend(cost);
                Fiscal.AddCourt(cost);
                Chartered.Add(id);
                DLog.Force("行会: 授予特许状 " + def.Name + " 花费 " + cost);
                return "已授予特许状: " + def.Name;
            }
            catch { return "申请失败"; }
        }

        // 免费授予首份特许状(政治请愿用; 第 21 章)
        internal static string GrantCharterFree()
        {
            try
            {
                for (int i = 0; i < All.Count; i++)
                {
                    var g = All[i];
                    if (!IsFounded(g.Id) || IsChartered(g.Id)) continue;
                    Chartered.Add(g.Id);
                    DLog.Force("行会: 政治请愿免费授予特许状 " + g.Name);
                    return g.Name;
                }
                return "无可授特许的行会";
            }
            catch { return "授予失败"; }
        }

        internal static string RevokeCharter(string id, int today)
        {
            try
            {
                var def = Find(id);
                if (def == null || !IsChartered(id)) return "没有特许状";
                Chartered.Remove(id);
                RevokedDay[id] = today;
                DLog.Force("行会: 撤销特许状 " + def.Name);
                return "已撤销特许状(加成暂停 30 天)";
            }
            catch { return "撤销失败"; }
        }

        internal static string StatusOf(string id, int today)
        {
            if (IsChartered(id)) return "已授";
            return "无";   // v4.4: 撤销后的 30 天加成暂停已删
        }

        // 行会年金预览(不扣钱; UI 用)
        internal static int MonthlyFeePreview()
        {
            try
            {
                int fee = 0;
                foreach (var id in Chartered)
                {
                    var def = Find(id);
                    if (def == null || def.Cat == null) continue;
                    fee += (int)(CategoryLevels(def.Cat) * 50f / 12f);
                }
                return fee;
            }
            catch { return 0; }
        }

        // 行会年金(月度支出; 文档 20.5)
        internal static int MonthlyFee()
        {
            try
            {
                int fee = 0;
                foreach (var id in Chartered)
                {
                    var def = Find(id);
                    if (def == null || def.Cat == null) continue;
                    fee += (int)(CategoryLevels(def.Cat) * 50f / 12f);
                }
                if (fee > 0) { EconomyWorld.TreasurySpend(fee); Fiscal.AddFee(fee); }
                return fee;
            }
            catch { return 0; }
        }

        // 玩家王国某类建筑的等级合计(特许条件/年金基数)
        private static int CategoryLevels(string cat)
        {
            int n = 0;
            try
            {
                var pk = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                if (pk == null) return 0;
                foreach (var s in pk.Settlements)
                {
                    if (s == null) continue;
                    var sb = EconomyWorld.Find(s.StringId);
                    if (sb == null) continue;
                    for (int i = 0; i < sb.Groups.Count; i++)
                    {
                        var g = sb.Groups[i];
                        if (g == null || g.Count <= 0) continue;
                        var def = BuildDefs.Get(g.DefId);
                        if (def != null && BuildDefs.CategoryName(def.Cat) == cat) n += g.Count;
                    }
                }
            }
            catch { }
            return n;
        }

        // 产出加成(按建筑类别; 有特许状 +18%, 被撤 30 天内不加成)
        internal static float OutputBonus(string categoryName)
        {
            if (string.IsNullOrEmpty(categoryName) || Founded.Count == 0) return 0f;
            float b = 0f;
            for (int i = 0; i < All.Count; i++)
            {
                var g = All[i];
                if (g.Cat != categoryName || !Founded.Contains(g.Id)) continue;
                if (Chartered.Contains(g.Id)) { b += 0.18f + 0.02f * Politics.LawLevel(4); continue; }
                b += g.OutputBonus;   // v4.4: 撤销后的加成暂停已删
            }
            return b;
        }

        private static int CurDay()
        {
            try { return (int)TaleWorlds.CampaignSystem.CampaignTime.Now.ToDays; } catch { return 0; }
        }

        // 贸易容量加成
        internal static float CapacityBonus()
        {
            float b = 0f;
            for (int i = 0; i < All.Count; i++)
            {
                var g = All[i];
                if (g.CapacityBonus > 0f && Founded.Contains(g.Id)) b += g.CapacityBonus;
            }
            return b;
        }

        internal static string Found(string id)   // 返回提示文本
        {
            try
            {
                var def = Find(id);
                if (def == null) return "未知行会";
                if (Founded.Contains(id)) return "已成立";
                if (EconomyWorld.Treasury.Gold < def.Cost) return "国库不足(需 " + def.Cost.ToString("N0") + ")";
                EconomyWorld.TreasurySpend(def.Cost);
                Fiscal.AddCourt(def.Cost);
                Founded.Add(id);
                DLog.Force("行会: 成立 " + def.Name + " 花费 " + def.Cost + " 效果: " + def.Desc);
                return "已成立: " + def.Name;
            }
            catch (Exception ex) { return "成立失败: " + ex.Message; }
        }

        internal static GuildDef Find(string id)
        {
            for (int i = 0; i < All.Count; i++) if (All[i].Id == id) return All[i];
            return null;
        }

        internal static string Save()
        {
            var sb = new StringBuilder();
            sb.Append("v2;");
            foreach (var id in Founded) sb.Append(id).Append(';');
            sb.Append('#');   // 分隔: 特许状段
            foreach (var id in Chartered) sb.Append(id).Append(';');
            sb.Append('#');   // 分隔: 撤销冷却段
            foreach (var kv in RevokedDay) sb.Append(kv.Key).Append(',').Append(kv.Value).Append(';');
            return sb.ToString();
        }

        internal static void Load(string data)
        {
            try
            {
                Founded.Clear();
                Chartered.Clear();
                RevokedDay.Clear();
                if (string.IsNullOrEmpty(data)) return;
                var seg = data.Split('#');
                var parts = seg[0].Split(';');
                for (int i = 1; i < parts.Length; i++)
                {
                    var id = parts[i];
                    if (!string.IsNullOrEmpty(id) && Find(id) != null) Founded.Add(id);
                }
                if (seg.Length > 1)
                {
                    var cp = seg[1].Split(';');
                    for (int i = 0; i < cp.Length; i++)
                    {
                        var id = cp[i];
                        if (!string.IsNullOrEmpty(id) && Find(id) != null) Chartered.Add(id);
                    }
                }
                if (seg.Length > 2)
                {
                    var rp = seg[2].Split(';');
                    for (int i = 0; i < rp.Length; i++)
                    {
                        var f = rp[i].Split(',');
                        int v;
                        if (f.Length >= 2 && int.TryParse(f[1], out v)) RevokedDay[f[0]] = v;
                    }
                }
                DLog.Force("行会: 读档 已成立=" + Founded.Count + " 特许=" + Chartered.Count);
            }
            catch (Exception ex) { DLog.Force("行会读档失败: " + ex.Message); }
        }
    }
}
