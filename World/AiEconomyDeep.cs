using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace FeudalInternalAffairs
{
    // v4.149: AI 经济精细化 —— AI 也会用玩家侧的经济工具(不简化)
    //   税制: 按财政/战争状况调档(影响 AI 金库收入 + 本国人口立场)
    //   铸币: 赤字时降成色换现金(全国物价通胀 -> 人口消费成本上升 -> 财富与立场恶化)
    //   行会: 有盈余时入会(金库月费 -> 收入加成)
    //   信贷: 赤字时借债(日息), 长期还不上 -> 债务危机
    //   建设: 利润导向评分(产出按全国价格 - 投入 - 维护 + 市场缺口 + 战争需求)
    //   粮食: 全局缺口采购(不只看单个城)
    internal static class AiEconomyDeep
    {
        internal static readonly Dictionary<string, int> TaxLevel = new Dictionary<string, int>();     // 0..4
        internal static readonly Dictionary<string, int> MintLevel = new Dictionary<string, int>();    // 0..3
        internal static readonly Dictionary<string, int> Guild = new Dictionary<string, int>();        // 0/1
        internal static readonly Dictionary<string, float> Debt = new Dictionary<string, float>();
        internal static readonly Dictionary<string, int> DebtDays = new Dictionary<string, int>();

        private static readonly float[] TaxMult = { 0.5f, 0.8f, 1f, 1.3f, 1.7f };   // 免征/轻/标准/重/苛
        private static readonly string[] TaxNames = { "免征", "轻税", "标准", "重税", "苛税" };
        private static readonly string[] MintNames = { "足色", "轻贬", "重贬", "恶铸" };

        private static int TaxOf(string id) { int v; return TaxLevel.TryGetValue(id, out v) ? v : 2; }
        private static int MintOf(string id) { int v; return MintLevel.TryGetValue(id, out v) ? v : 0; }
        private static float DebtOf(string id) { float v; return Debt.TryGetValue(id, out v) ? v : 0f; }

        internal static string TaxNameOf(Kingdom k) { return k != null ? TaxNames[TaxOf(k.StringId)] : "?"; }
        internal static string MintNameOf(Kingdom k) { return k != null ? MintNames[MintOf(k.StringId)] : "?"; }
        internal static float TaxMultOf(Kingdom k) { return k != null ? TaxMult[TaxOf(k.StringId)] : 1f; }

        // 铸币日收入(按人口与繁荣; 恶铸收益高但通胀重)
        internal static float MintIncomeOf(Kingdom k)
        {
            try
            {
                if (k == null) return 0f;
                int m = MintOf(k.StringId);
                if (m <= 0) return 0f;
                float baseInc = 0f;
                foreach (var s in k.Settlements)
                {
                    if (s == null) continue;
                    if (s.Town != null) baseInc += s.Town.Prosperity * 0.04f;
                    if (s.Village != null) baseInc += s.Village.Hearth * 0.015f;
                }
                return baseInc * (0.15f * m);
            }
            catch { return 0f; }
        }

        // 铸币通胀系数(该国人口消费成本乘数)
        internal static float InflationOf(Kingdom k)
        {
            try
            {
                if (k == null) return 1f;
                return 1f + 0.04f * MintOf(k.StringId);
            }
            catch { return 1f; }
        }

        // 按定居点查通胀(供 PopSim 消费成本用)
        internal static float InflationForSettlement(string settlementId)
        {
            try
            {
                if (string.IsNullOrEmpty(settlementId)) return 1f;
                var s = FindSettlement(settlementId);
                var k = s != null ? s.MapFaction as Kingdom : null;
                return InflationOf(k);
            }
            catch { return 1f; }
        }

        internal static float GuildBonusOf(Kingdom k)
        {
            try
            {
                if (k == null) return 0f;
                int g; return Guild.TryGetValue(k.StringId, out g) && g > 0 ? 0.08f : 0f;
            }
            catch { return 0f; }
        }

        internal static float InterestDaily(Kingdom k)
        {
            try
            {
                if (k == null) return 0f;
                return DebtOf(k.StringId) * 0.0008f;   // 约 2.4%/月
            }
            catch { return 0f; }
        }

        // 收入端修正(税制/行会/铸币/利息; 供 WarEconomy.EstimateIncomeOf 用)
        internal static float IncomeAdjust(Kingdom k, float baseIncome)
        {
            try
            {
                if (k == null) return baseIncome;
                float v = baseIncome * TaxMultOf(k);
                v *= 1f + GuildBonusOf(k);
                v += MintIncomeOf(k);
                v -= InterestDaily(k);
                return v;
            }
            catch { return baseIncome; }
        }

        // ================= 月度决策 =================
        internal static void Monthly(int day)
        {
            try
            {
                var pk = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                foreach (var k in Kingdom.All)
                {
                    if (k == null || k.IsEliminated) continue;
                    if (pk != null && ReferenceEquals(k, pk)) continue;   // 玩家自己管
                    try { Decide(k, day); } catch { }
                    try { BuyFoodGlobal(k, day); } catch { }              // v4.149: 全局粮食采购
                }
                // v5.x: 领主 AI(设计 28.4) —— 装备下单/征兵填编/降档/和平省钱(月结, 每国 ≤2 条日志)
                try { LordArmyAi.Monthly(day); } catch { }
                // v4.21x: AI 政治工具(法令/游说/议会/文化法律/条约; 各国每次决策至多一条日志)
                try { Decrees.AiMonthly(day); } catch { }
                try { Lobbying.AiMonthly(day); } catch { }
                try { Parliament.AiMonthly(day); } catch { }
                try { CultureSystem.AiMonthly(day); } catch { }
                try { Treaties.AiMonthly(day); } catch { }
            }
            catch (Exception ex) { DLog.Force("AI 经济月结异常: " + ex.Message); }
        }

        private static void Decide(Kingdom k, int day)
        {
            var id = k.StringId;
            float gold = WarEconomy.GoldOfPublic(k);
            int deficit = WarEconomy.DeficitDaysOf(k);
            bool atWar = false;
            int wars = 0;
            try
            {
                foreach (var x in Kingdom.All)
                {
                    if (x == null || x.IsEliminated || ReferenceEquals(x, k)) continue;
                    if (k.IsAtWarWith(x)) { atWar = true; wars++; }
                }
            }
            catch { }
            float agg = AiBrain.AggressionOf(k);
            AiDirector.Goal goal = AiBrain.GoalOf(k);
            float threat = AiBrain.ThreatOf(k);
            int tax = TaxOf(id), mint = MintOf(id);
            float debt = DebtOf(id);

            // ---- 税制: 战时/军事/生存 -> 加税; 经济/整合且和平 -> 减税 ----
            // v4.22x: 读激进/权威数据, 战时提税但不把激进推爆(激进≥45% 或权威折合法性<30 不再加税)
            float radical = RadicalOf(k);
            float legit = 100f;
            try { legit = Math.Min(100f, Decrees.AiAuthOf(k) / 4f); } catch { }   // AI 国无独立合法性 -> 以权威折算
            bool wantRaise = atWar || deficit >= 3
                || goal == AiDirector.Goal.Military || goal == AiDirector.Goal.Survival;
            bool wantCut = !atWar && deficit == 0
                && (goal == AiDirector.Goal.Economy || goal == AiDirector.Goal.Consolidation);
            bool safeToRaise = radical < 0.45f && legit >= 30f;
            if (wantRaise && tax < 4 && safeToRaise && MBRandom.RandomFloat < 0.35f + agg * 0.35f)
            {
                tax++;
                AiEconomyLog.Add(k, "税=" + TaxNames[tax]);
                // 加税 -> 本国人口不满(立场转激进)
                try { ShiftLocalStance(k, -0.05f); } catch { }
            }
            else if (wantCut && gold > 3000f && tax > 1 && MBRandom.RandomFloat < 0.35f)
            {
                tax--;
                AiEconomyLog.Add(k, "税=" + TaxNames[tax]);
                try { ShiftLocalStance(k, +0.03f); } catch { }
            }
            TaxLevel[id] = tax;
            // v4.149: 财政 -> 性格漂移(破产更保守重商 / 繁荣更大兴土木)
            if (deficit >= 5) { try { AiPersonality.Observe(k, "bankrupt", 1f); } catch { } }
            else if (deficit == 0 && gold > 6000f) { try { AiPersonality.Observe(k, "prosper", 1f); } catch { } }

            // ---- 铸币: 持续赤字(或生存目标缺钱) -> 降成色 ----
            if ((deficit >= 5 || (goal == AiDirector.Goal.Survival && gold < 1500f)) && mint < 3 && MBRandom.RandomFloat < 0.6f)
            {
                mint++;
                AiEconomyLog.Add(k, "铸币=" + MintNames[mint]);
            }
            else if (deficit == 0 && gold > 5000f && mint > 0 && MBRandom.RandomFloat < 0.4f)
            {
                mint--;   // 财政好转 -> 恢复成色
                AiEconomyLog.Add(k, "铸币=" + MintNames[mint]);
            }
            MintLevel[id] = mint;

            // ---- 行会: 金库充裕 -> 入会(一次性) ----
            int g;
            if (!Guild.TryGetValue(id, out g)) g = 0;
            if (g == 0 && gold > 2000f && MBRandom.RandomFloat < 0.25f)
            {
                Guild[id] = 1;
                AiEconomyLog.Add(k, "行会=成立");
            }
            // 行会月费
            if (g > 0 || Guild.TryGetValue(id, out g) && g > 0)
            {
                try { WarEconomy.SpendPublic(k, Guilds.MonthlyFee()); } catch { }
            }

            // ---- 信贷: 赤字 -> 借债; 盈余 -> 还债 ----
            if (deficit >= 5 && debt < 20000f)
            {
                float borrow = Math.Min(3000f, 20000f - debt);
                Debt[id] = debt + borrow;
                try { WarEconomy.AddPublic(k, borrow); } catch { }
                AiEconomyLog.Add(k, "借债=+" + (int)borrow);
            }
            else if (deficit == 0 && debt > 0f && gold > 4000f)
            {
                float repay = Math.Min(debt, 2000f);
                Debt[id] = debt - repay;
                try { WarEconomy.SpendPublic(k, repay); } catch { }
                AiEconomyLog.Add(k, "还债=-" + (int)repay);
            }
            // ---- v4.22x: 投资池扩产(按 Goal) ----
            //   经济/整合且和平、无赤字、无外部威胁 -> 国库注资投资池, 由私人建造扩产;
            //   生存/军事/战时/受威胁 -> 不注资(保国库优先)
            if (!atWar && deficit == 0 && threat <= 0f
                && (goal == AiDirector.Goal.Economy || goal == AiDirector.Goal.Consolidation)
                && gold > 6000f && MBRandom.RandomFloat < 0.5f)
            {
                float pool = 0f;
                try { pool = InvestmentPool.Of(id); } catch { }
                if (pool < 3000f)
                {
                    float seed = Math.Min(1200f, gold * 0.08f);
                    if (seed > 100f)
                    {
                        try
                        {
                            WarEconomy.SpendPublic(k, seed);
                            InvestmentPool.Add(id, seed);
                            AiEconomyLog.Add(k, "投资池=+" + (int)seed);
                        }
                        catch { }
                    }
                }
            }
            // 债务危机: 长期高债 -> 记录(供机会主义/危机外交读取)
            float d2 = DebtOf(id);
            if (d2 > 15000f)
            {
                int dd;
                DebtDays.TryGetValue(id, out dd);
                DebtDays[id] = dd + 1;
                if (dd + 1 == 3) AiEconomyLog.Add(k, "债务危机(" + (int)d2 + ")");
            }
            else DebtDays[id] = 0;
        }

        // 本国人口加权激进(0~1; 加税安全线用; 月结调用, 不做日结)
        private static float RadicalOf(Kingdom k)
        {
            try
            {
                if (k == null) return 0f;
                float num = 0f, den = 0f;
                foreach (var s in k.Settlements)
                {
                    if (s == null) continue;
                    var list = Pops.Of(s.StringId);
                    if (list == null) continue;
                    for (int i = 0; i < list.Count; i++)
                    {
                        var p = list[i];
                        if (p == null || p.Size < 0.5f) continue;
                        num += p.Radicalism * p.Size;
                        den += p.Size;
                    }
                }
                return den > 0f ? num / den : 0f;
            }
            catch { return 0f; }
        }

        // 本国人口立场调整(加税 -> 不满, 减税 -> 安抚)
        private static void ShiftLocalStance(Kingdom k, float delta)
        {
            try
            {
                if (k == null) return;
                foreach (var s in k.Settlements)
                {
                    if (s == null) continue;
                    var list = Pops.Of(s.StringId);
                    if (list == null) continue;
                    for (int i = 0; i < list.Count; i++)
                    {
                        var p = list[i];
                        if (p == null) continue;
                        Pops.ShiftStance(p, delta * (p.WealthLevel < 6 ? 1.5f : 0.8f));   // 穷人更敏感
                    }
                }
            }
            catch { }
        }

        // v4.21x: AI 政治事件(法令/游说/议会/文化法律)的 IG 态度 -> 本国人口立场(同 AiEconomyDeep 口径)
        internal static void ShiftPublicStance(Kingdom k, int[] att)
        {
            try
            {
                if (k == null || att == null) return;
                float d = 0f;
                if (att.Length > 1) d += att[1] * 0.0010f;   // 大贵族
                if (att.Length > 4) d += att[4] * 0.0010f;   // 商帮
                if (att.Length > 5) d += att[5] * 0.0008f;   // 行会
                if (att.Length > 6) d += att[6] * 0.0020f;   // 农民(占人口多数)
                if (att.Length > 7) d += att[7] * 0.0005f;   // 军队
                if (Math.Abs(d) < 0.0005f) return;
                ShiftLocalStance(k, d);
            }
            catch { }
        }

        internal static bool IsInDebtCrisis(Kingdom k)
        {
            try
            {
                if (k == null) return false;
                int dd;
                return DebtDays.TryGetValue(k.StringId, out dd) && dd >= 3;
            }
            catch { return false; }
        }

        // ================= 建设: 利润导向评分(供 AiDevelopment.ScoreBuild) =================
        //   收益 = Σ 产出商品 × 全国价格 × 日均产量 - Σ 投入商品 × 价格 - 维护费
        //   × 市场缺口加成(缺口越大越值得建) × 战争需求(战时军事建筑) × 性格
        internal static float BuildProfitScore(BuildDef def, Kingdom k)
        {
            try
            {
                if (def == null || k == null) return 0f;
                float revenue = 0f;
                for (int i = 0; i < def.Outputs.Count; i++)
                {
                    var o = def.Outputs[i];
                    if (o == null || string.IsNullOrEmpty(o.Good) || o.Good.StartsWith("@")) continue;
                    float price = PriceOf(o.Good);
                    float amount = o.V != null && o.V.Length > 0 ? o.V[0] : 1f;
                    revenue += price * amount * 0.1f;
                }
                float cost = 0f;
                if (def.Inputs != null)
                {
                    for (int i = 0; i < def.Inputs.Count; i++)
                    {
                        var inp = def.Inputs[i];
                        if (inp == null || string.IsNullOrEmpty(inp.Good) || inp.Good.StartsWith("@")) continue;
                        float price = PriceOf(inp.Good);
                        float amount = inp.V != null && inp.V.Length > 0 ? inp.V[0] : 1f;
                        cost += price * amount * 0.1f;
                    }
                }
                float maint = def.Maintenance != null && def.Maintenance.Length > 0 ? def.Maintenance[0] : 0f;
                float profit = revenue - cost - maint;

                // 市场缺口加成
                float gap = MarketGap(def);
                profit *= 1f + Math.Min(1.5f, gap);

                // 战争需求
                bool atWar = false;
                try { foreach (var x in Kingdom.All) { if (x != null && !x.IsEliminated && k.IsAtWarWith(x)) { atWar = true; break; } } } catch { }
                string cat = def.Cat.ToString();
                if (atWar && (cat.IndexOf("Military", StringComparison.OrdinalIgnoreCase) >= 0 || cat.IndexOf("军", StringComparison.Ordinal) >= 0))
                    profit *= 1.6f;

                // 性格(建设倾向) + 失业压力(劳动密集建筑 +)
                float dev = AiPersonality.DevelopmentOf(k) / 100f;
                profit *= 0.4f + dev;
                try
                {
                    if (def.Work != null && def.Work.Length > 0 && def.Work[0] >= 20)
                    {
                        float unemp = UnemploymentOf(k);
                        profit *= 1f + Math.Min(0.8f, unemp * 2f);
                    }
                }
                catch { }
                return profit;
            }
            catch { return 0f; }
        }

        private static float PriceOf(string good)
        {
            try { return EconomyWorld.National.PriceOf(good); } catch { return 0f; }
        }

        // 市场缺口: 产出商品中最大的 全国买单/卖单 比
        private static float MarketGap(BuildDef def)
        {
            try
            {
                float best = 0f;
                var nat = EconomyWorld.National;
                if (nat == null) return 0f;
                for (int i = 0; i < def.Outputs.Count; i++)
                {
                    var o = def.Outputs[i];
                    if (o == null || string.IsNullOrEmpty(o.Good) || o.Good.StartsWith("@")) continue;
                    float buy = 0f, sell = 0f;
                    nat.BuyVolume.TryGetValue(o.Good, out buy);
                    nat.SellVolume.TryGetValue(o.Good, out sell);
                    float gap = buy / Math.Max(1f, sell);
                    if (gap > best) best = gap;
                }
                return best;
            }
            catch { return 0f; }
        }

        private static float UnemploymentOf(Kingdom k)
        {
            try
            {
                float total = 0f, work = 0f;
                foreach (var s in k.Settlements)
                {
                    if (s == null) continue;
                    var list = Pops.Of(s.StringId);
                    if (list == null) continue;
                    for (int i = 0; i < list.Count; i++)
                    {
                        var p = list[i];
                        if (p == null) continue;
                        total += p.Size;
                        try { if (PopJob.IncomeOf(p) > 0.0001f) work += p.Workforce; } catch { }
                    }
                }
                float ratio = total * PopDefs.WorkforceRatio;
                return ratio > 1f ? Math.Max(0f, 1f - work / ratio) : 0f;
            }
            catch { return 0f; }
        }

        // ================= 粮食: 全局缺口采购 =================
        //   AI 各国: 全国粮食天数 < 阈值 -> 从富余国/中立市场集中买粮(不只看最缺的城)
        internal static void BuyFoodGlobal(Kingdom k, int day)
        {
            try
            {
                if (k == null || k.IsEliminated) return;
                float days;
                float have = WarEconomy.FoodDaysOf(k, out days);
                if (days >= 20f) return;                       // 够吃
                var pk = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                if (pk != null && ReferenceEquals(k, pk)) return;

                float gold = WarEconomy.GoldOfPublic(k);
                if (gold < 500f) return;
                float budget = Math.Min(gold * 0.4f, 2000f);
                // 找富余城镇(粮食库存最高的)
                Settlement best = null;
                float bestFood = 0f;
                foreach (var s in Settlement.All)
                {
                    if (s == null || s.Town == null) continue;
                    if (ReferenceEquals(s.MapFaction, k)) continue;
                    float f = s.Town.FoodStocks;
                    if (f > bestFood) { bestFood = f; best = s; }
                }
                if (best == null || bestFood < 100f) return;
                WarEconomy.SpendPublic(k, budget);
                try { best.Town.FoodStocks -= budget * 0.02f; } catch { }   // 采购真实抽走对方库存
                try
                {
                    foreach (var s in k.Settlements)
                    {
                        if (s == null || s.Town == null) continue;
                        s.Town.FoodStocks += budget * 0.02f / Math.Max(1, k.Settlements.Count);
                    }
                }
                catch { }
                AiEconomyLog.Add(k, "购粮=" + (int)budget + "@" + (best.Name != null ? best.Name.ToString() : best.StringId));
            }
            catch { }
        }

        private static Settlement FindSettlement(string id)
        {
            try
            {
                foreach (var s in Settlement.All) if (s != null && s.StringId == id) return s;
            }
            catch { }
            return null;
        }

        // ================= 存档 =================
        internal static string Save()
        {
            try
            {
                var sb = new StringBuilder("v1");
                foreach (var kv in TaxLevel) sb.Append(';').Append(kv.Key).Append(",T,").Append(kv.Value);
                foreach (var kv in MintLevel) sb.Append(';').Append(kv.Key).Append(",M,").Append(kv.Value);
                foreach (var kv in Guild) sb.Append(';').Append(kv.Key).Append(",G,").Append(kv.Value);
                foreach (var kv in Debt) sb.Append(';').Append(kv.Key).Append(",D,").Append(kv.Value.ToString("F0", CultureInfo.InvariantCulture));
                return sb.ToString();
            }
            catch { return "v1"; }
        }

        internal static void Load(string data)
        {
            try
            {
                TaxLevel.Clear(); MintLevel.Clear(); Guild.Clear(); Debt.Clear(); DebtDays.Clear();
                if (string.IsNullOrEmpty(data)) return;
                var seg = data.Split(';');
                if (seg.Length < 1 || seg[0] != "v1") return;
                for (int i = 1; i < seg.Length; i++)
                {
                    var f = seg[i].Split(',');
                    if (f.Length < 3) continue;
                    int v;
                    if (!int.TryParse(f[2], out v)) continue;
                    switch (f[1])
                    {
                        case "T": TaxLevel[f[0]] = v; break;
                        case "M": MintLevel[f[0]] = v; break;
                        case "G": Guild[f[0]] = v; break;
                        case "D": Debt[f[0]] = v; break;
                    }
                }
                DLog.Force("AI 经济: 读档 " + TaxLevel.Count + " 国");
            }
            catch { }
        }

        internal static void Reset()
        {
            TaxLevel.Clear(); MintLevel.Clear(); Guild.Clear(); Debt.Clear(); DebtDays.Clear();
        }
    }

    // ================= v4.22x: 统一大脑(AiDirector)访问包装 =================
    //   全模组只在此处直连 AiDirector: 统一类型/兜底口径; 大脑不可用时退回旧 AI 口径(不崩)
    internal static class AiBrain
    {
        internal static AiDirector.Goal GoalOf(Kingdom k)
        {
            try { return AiDirector.GoalOf(k); } catch { return AiDirector.Goal.Economy; }
        }
        internal static float ThreatOf(Kingdom k)
        {
            try { return (float)AiDirector.ThreatOf(k); } catch { return 0f; }
        }
        internal static float AggressionOf(Kingdom k)
        {
            try { return (float)AiDirector.Aggression(k); } catch { return AiPersonality.AggressionOf(k) / 100f; }
        }
        internal static bool WantsWar(Kingdom k)
        {
            try { return AiDirector.WantsWar(k); } catch { return false; }
        }
        internal static string WarTargetId(Kingdom k)
        {
            try { return AiDirector.WarTargetId(k); } catch { return null; }
        }
        internal static float ReserveGold(Kingdom k)
        {
            try { return (float)AiDirector.ReserveGold(k); } catch { return 0f; }
        }
        internal static float BudgetFor(Kingdom k, string kind, float gold)
        {
            try { return (float)AiDirector.BudgetFor(k, kind, (int)gold); } catch { return 0f; }
        }
        internal static string GoalName(Kingdom k)
        {
            try
            {
                switch (AiDirector.GoalOf(k))
                {
                    case AiDirector.Goal.Survival: return "生存";
                    case AiDirector.Goal.Military: return "军事";
                    case AiDirector.Goal.Politics: return "政治";
                    case AiDirector.Goal.Consolidation: return "整合";
                    default: return "经济";
                }
            }
            catch { return "?"; }
        }
    }

    // ================= v4.22x: 三国 AI 月结合并日志 =================
    //   铁路/建设/经济 AI 把动作写成片段(建线=城→城 / 建楼=名@城 / 税=...),
    //   由 Railways.AiMonthly 末尾统一 Flush: 每国每月一条 Force, 无动作则省略
    internal static class AiEconomyLog
    {
        private static readonly Dictionary<string, List<string>> Parts = new Dictionary<string, List<string>>();

        internal static void Add(Kingdom k, string part)
        {
            try
            {
                if (k == null || string.IsNullOrEmpty(part)) return;
                List<string> list;
                if (!Parts.TryGetValue(k.StringId, out list)) { list = new List<string>(); Parts[k.StringId] = list; }
                if (list.Count < 8) list.Add(part);
            }
            catch { }
        }

        internal static void Flush()
        {
            try
            {
                if (Parts.Count == 0) return;
                foreach (var kv in Parts)
                {
                    var k = Railways.FindKingdom(kv.Key);
                    if (k == null || kv.Value == null || kv.Value.Count == 0) continue;
                    DLog.Force("AI 经济: " + k.Name + " 目标=" + AiBrain.GoalName(k) + " " + string.Join(" ", kv.Value.ToArray()));
                }
                Parts.Clear();
            }
            catch { }
        }
    }
}
