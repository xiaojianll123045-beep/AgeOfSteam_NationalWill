using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;

namespace FeudalInternalAffairs
{
    // 28.4 领主装备 → 征兵闭环 + 领主军饷
    //   · 兵种选项 = 领主私库(L:clan)装备能支撑的最高档 + 本国科技已解锁(Research.UnlockedFor)
    //   · 装备需求照 27.2 兵种表(Equipment.Units, 每 100 人), 型号照 27.1.2 目录(高 tier 优先)
    //   · 招募费按兵种训练等级定价(照 27 章口径); 装备从私库扣, 费用从领主金扣
    //   · 军饷每日从领主金扣: 不足 → 家族借债(利率复用现有 Credit.MonthlyRate, 额度仿 Credit 口径)
    //     → 仍不足 → 逐人逃兵(士气越低流失越快); 装备视作被带走, 不回库
    //   · 每领主每天最多一条日志; 只服务领主/家族部队(含玩家家族), 国防军军团不在此列
    internal static class LordArmy
    {
        internal struct KindOption
        {
            public int KindIdx;
            public string Name;
            public int MaxN;
            public string Need;
            public int Fee;
            public bool TechOk;
        }

        // 招募费(金/人): 按 27.2 训练等级定价(27 章"按兵种训练等级定价"口径):
        //   民兵(0.85)=20 / 线列(1.00)=45 / 轻步兵(1.05)=60 / 掷弹兵(1.15)=80 /
        //   龙骑兵(1.00, 需枪+马+甲)=70 / 骠骑兵(1.05)=60 / 胸甲骑兵(1.10, 重装)=90 /
        //   弓兵(0.95)=30 / 弩兵(0.95)=30(与弓同档) / 骑射手(0.95, 带马)=60(同骠骑档) /
        //   炮兵(1.10, 三类同价)=120 / 工兵(1.00)=50
        private static readonly int[] RecruitFee = { 20, 45, 60, 80, 70, 60, 90, 30, 30, 60, 120, 120, 120, 50 };

        // 同族只提供"装备能支撑的最高档"(28.4); 每族按高→低排列:
        //   步兵族: 掷弹兵 > 轻步兵 > 线列 > 民兵
        //   骑兵族: 胸甲骑兵 > 龙骑兵 > 骠骑兵 > 骑射手
        //   远程族: 弩兵 > 弓兵
        //   炮兵族: 骑炮兵 > 攻城炮兵 > 野战炮兵
        //   工兵:   单独一族
        private static readonly int[][] Families =
        {
            new[] { Equipment.UGrenadier, Equipment.ULightInf, Equipment.ULine, Equipment.UMilita },
            new[] { Equipment.UCuirassier, Equipment.UDragoon, Equipment.UHussar, Equipment.UHorseArcher },
            new[] { Equipment.UCrossbow, Equipment.UArcher },
            new[] { Equipment.UHorseArtillery, Equipment.USiegeArtillery, Equipment.UFieldArtillery },
            new[] { Equipment.UEngineer }
        };

        // 家族欠饷借债: clanId -> 债务(浮点累计); 读档段 FIA_LordArmy
        private static readonly Dictionary<string, float> Debt = new Dictionary<string, float>(StringComparer.Ordinal);
        // 每领主每天最多一条日志: clanId -> 最近日志日
        private static readonly Dictionary<string, int> LastLogDay = new Dictionary<string, int>(StringComparer.Ordinal);

        // ==================== 公开 API(签名固定, 28.4) ====================

        // 兵种 = 私库装备能支撑的最高档 + 本国科技已解锁; MaxN 受原版名额与本队编制上限
        internal static List<KindOption> AvailableKinds(MobileParty p, Settlement s)
        {
            var list = new List<KindOption>();
            try
            {
                if (p == null || !p.IsActive || !IsLordArmy(p)) return list;
                var clan = p.ActualClan;
                if (clan == null) return list;
                string lordKey = Armory.LordOwner(clan);
                if (string.IsNullOrEmpty(lordKey)) return list;
                var kingdom = KingdomOf(p, clan);
                int slots = s != null ? SlotLeft(s) : int.MaxValue;
                int head = PartyHeadroom(p);

                for (int f = 0; f < Families.Length; f++)
                {
                    var fam = Families[f];
                    for (int i = 0; i < fam.Length; i++)
                    {
                        int idx = fam[i];
                        var u = Equipment.Unit(idx);
                        if (u == null) continue;
                        bool techOk = Research.UnlockedFor(kingdom, UnitGate(u.Id));
                        string need;
                        int eqN = SupportN(lordKey, kingdom, idx, out need);
                        if (!techOk || eqN <= 0) continue;               // 降档找下一档
                        long maxN = Math.Min((long)eqN, Math.Min((long)slots, (long)head));
                        if (maxN < 0) maxN = 0;
                        list.Add(new KindOption
                        {
                            KindIdx = idx,
                            Name = u.Name,
                            MaxN = (int)Math.Min(int.MaxValue, maxN),
                            Need = need,
                            Fee = FeeOf(idx),
                            TechOk = true
                        });
                        break;   // 每族只给最高可用档
                    }
                }
            }
            catch (Exception ex) { DLog.Force("领主征兵: AvailableKinds 异常 " + ex.Message); }
            return list;
        }

        // 原版征兵名额剩余(村庄/城镇志愿兵槽位, 28.4 条件 1)
        internal static int SlotLeft(Settlement s)
        {
            int n = 0;
            try
            {
                if (s == null) return 0;
                var notables = s.Notables;
                if (notables == null) return 0;
                for (int i = 0; i < notables.Count; i++)
                {
                    var h = notables[i];
                    if (h == null) continue;
                    var vt = h.VolunteerTypes;
                    if (vt == null) continue;
                    for (int j = 0; j < vt.Length; j++) if (vt[j] != null) n++;
                }
            }
            catch { }
            return n;
        }

        // 校验名额/装备(从领主私库 L:clan 扣)/费用(领主金), 逐人生成(Soldiers.Add), 返回提示串
        internal static string Recruit(MobileParty p, Settlement s, int kindIdx, int n)
        {
            try
            {
                if (p == null || !p.IsActive) return "征募失败: 部队无效";
                if (s == null) return "征募失败: 请选择征兵地";
                if (n <= 0) return "征募失败: 人数必须为正";
                if (kindIdx < 0 || kindIdx >= Equipment.Units.Count) return "征募失败: 未知兵种";
                if (!IsLordArmy(p)) return "征募失败: 仅领主/家族部队可征募(国防军军团除外)";
                var clan = p.ActualClan;
                if (clan == null) return "征募失败: 未识别家族";
                var u = Equipment.Unit(kindIdx);
                if (u == null) return "征募失败: 未知兵种";
                string lordKey = Armory.LordOwner(clan);
                if (string.IsNullOrEmpty(lordKey)) return "征募失败: 未识别领主私库";
                var kingdom = KingdomOf(p, clan);

                string gate = UnitGate(u.Id);
                if (!Research.UnlockedFor(kingdom, gate))
                    return "征募失败: 本国未解锁「" + u.Name + "」(需 " + TechName(gate) + ")";

                int slots = SlotLeft(s);
                if (slots < n) return "征募失败: 「" + SetName(s) + "」征兵名额不足(剩 " + slots + " 人)";
                int head = PartyHeadroom(p);
                if (head < n) return "征募失败: 本队编制已满(还可容纳 " + head + " 人)";

                Dictionary<string, int> plan;
                string needText;
                string err = EquipPlan(lordKey, kingdom, kindIdx, n, out plan, out needText);
                if (!string.IsNullOrEmpty(err))
                    return "征募失败: " + err + "(" + needText + ")";

                Hero payer = PayerOf(p, clan);
                if (payer == null) return "征募失败: 未识别领主";
                int fee = FeeOf(kindIdx) * n;
                if (payer.Gold < fee)
                    return "征募失败: 领主金不足(需 " + fee.ToString("N0") + ", 现有 " + payer.Gold.ToString("N0") + ")";

                // ---- 应用(失败回滚) ----
                var taken = new Dictionary<string, int>(StringComparer.Ordinal);
                bool goldPaid = false;
                bool rosterAdded = false;
                try
                {
                    foreach (var kv in plan)
                    {
                        int got = Armory.TakeKey(lordKey, kv.Key, kv.Value);
                        if (got > 0) taken[kv.Key] = got;
                        if (got < kv.Value)
                        {
                            Refund(lordKey, taken);
                            return "征募失败: 领主私库该型号库存不足";
                        }
                    }
                    // 先固化逐人表(按征募前的名单), 再往原版名册加人, 避免懒初始化重复计数
                    if (Soldiers.Ensure(p) == null)
                    {
                        Refund(lordKey, taken);
                        return "征募失败: 无法建立逐人表";
                    }
                    payer.ChangeHeroGold(-fee);
                    goldPaid = true;
                    ConsumeSlots(s, n);
                    AddRoster(p, kindIdx, n);
                    rosterAdded = true;
                    for (int i = 0; i < n; i++)
                        Soldiers.Add(p, u.Id, 1, MBRandom.RandomInt(20, 31));   // 新兵熟练 20~30(28.5)

                    var sb = new StringBuilder();
                    sb.Append("征募成功: ").Append(u.Name).Append(" ×").Append(n)
                      .Append(" → 「").Append(MapSelection.NameOf(p)).Append("」")
                      .Append(" · 费 ").Append(fee.ToString("N0")).Append(" 金")
                      .Append(" · 私库扣装 ");
                    foreach (var kv in plan)
                    {
                        var def = Equipment.Get(kv.Key);
                        sb.Append(def != null ? def.Name : kv.Key).Append(kv.Value).Append(' ');
                    }
                    sb.Append("· 名额剩 ").Append(SlotLeft(s));
                    string msg = sb.ToString();
                    DLog.Force("领主征兵: " + msg);
                    return msg;
                }
                catch (Exception ex)
                {
                    // 回滚: 装备入私库, 费用退回领主金
                    Refund(lordKey, taken);
                    if (goldPaid) payer.ChangeHeroGold(fee);
                    if (rosterAdded) RemoveRoster(p, BranchOf(kindIdx), n);
                    DLog.Force("领主征兵: 应用异常已回滚 " + ex.Message);
                    return "征募异常, 已回滚(见日志)";
                }
            }
            catch (Exception ex)
            {
                DLog.Force("领主征兵: 异常 " + ex.Message);
                return "征募异常, 见日志";
            }
        }

        // 领主军饷/日(按兵种与人数): 基准 0.5 金/兵/日(第 23 章口径, 对应民兵训练 0.85),
        //   按兵种训练度折算 = 0.5 × 训练 ÷ 0.85, 四舍五入到 0.01 金
        internal static int DailyWageOf(MobileParty p)
        {
            try
            {
                if (p == null) return 0;
                Dictionary<string, int> men;
                Soldiers.Aggregate(p, out men, out _);
                long milli = 0;
                foreach (var kv in men)
                {
                    if (kv.Value <= 0) continue;
                    milli += (long)WageMilliOf(kv.Key) * kv.Value;
                }
                return (int)Math.Ceiling(milli / 1000.0);
            }
            catch { return 0; }
        }

        // 领主金扣饷; 不足 → 家族借债(走现有 Credit 利率口径) → 再不足逐人逃兵
        internal static void DailyWages()
        {
            try
            {
                var c = Campaign.Current;
                if (c == null || c.MobileParties == null) return;
                int day = 0;
                try { day = (int)CampaignTime.Now.ToDays; } catch { }
                AccrueInterest();
                for (int i = 0; i < c.MobileParties.Count; i++)
                {
                    var p = c.MobileParties[i];
                    if (!IsLordArmy(p)) continue;
                    try { PayOne(p, day); }
                    catch (Exception ex) { DLog.Force("领主军饷: 「" + MapSelection.NameOf(p) + "」结算异常 " + ex.Message); }
                }
            }
            catch (Exception ex) { DLog.Force("领主军饷: 每日结算异常 " + ex.Message); }
        }

        // ==================== 军饷/逃兵 ====================

        private static void PayOne(MobileParty p, int day)
        {
            var clan = p.ActualClan;
            if (clan == null) return;
            string key = ClanKey(clan);
            if (string.IsNullOrEmpty(key)) return;
            Hero payer = PayerOf(p, clan);
            if (payer == null) return;

            int wage = DailyWageOf(p);
            int paid = 0, borrow = 0, cut = 0, shortfall = 0;
            float debt;
            Debt.TryGetValue(key, out debt);
            if (debt < 0f) debt = 0f;

            if (wage > 0)
            {
                paid = Math.Min(payer.Gold, wage);
                if (paid > 0) payer.ChangeHeroGold(-paid);
                shortfall = wage - paid;
                if (shortfall > 0)
                {
                    float room = DebtLimit(clan) - debt;                       // 家族借债(额度仿 Credit)
                    if (room > 0f)
                    {
                        borrow = (int)Math.Min(shortfall, Math.Floor(room));
                        if (borrow > 0) { debt += borrow; shortfall -= borrow; }
                    }
                }
            }

            if (shortfall > 0)
            {
                int men = Soldiers.CountOf(p);
                if (men > 0)
                {
                    double avgWage = (double)wage / men;
                    int needCut = (int)Math.Ceiling(shortfall / Math.Max(0.1, avgWage));   // 裁到收支平衡
                    float morale = 50f;
                    try { morale = p.Morale; } catch { }
                    if (morale < 0f) morale = 0f;
                    if (morale > 100f) morale = 100f;
                    // 士气越低流失越快: 0.2%/日(士气100) ~ 3.2%/日(士气0)
                    int lowCut = (int)Math.Ceiling(men * (0.002 + (1f - morale / 100f) * 0.03));
                    cut = Math.Max(needCut, lowCut);
                    int capCut = Math.Max(1, (int)(men * 0.2f));               // 单日单队至多 20%
                    if (cut > capCut) cut = capCut;
                    cut = Desert(p, cut);
                }
            }
            else
            {
                // 无欠饷: 领主金余额先还家族债
                int repay = (int)Math.Min(debt, payer.Gold);
                if (repay > 0) { payer.ChangeHeroGold(-repay); debt -= repay; }
            }

            if (debt < 0f) debt = 0f;
            if (debt > 0f) Debt[key] = debt;
            else Debt.Remove(key);

            if ((borrow > 0 || cut > 0) && LastLogDayOf(key) != day)
            {
                MarkLogDay(key, day);
                var sb = new StringBuilder();
                sb.Append("军饷[").Append(ClanName(clan)).Append("]: 日饷 ").Append(wage).Append(" 金");
                if (borrow > 0)
                    sb.Append(" · 领主金不足, 家族借债 +").Append(borrow)
                      .Append("(现欠 ").Append((int)Math.Round(debt)).Append(')');
                if (cut > 0)
                    sb.Append(" · 欠饷逃兵 ").Append(cut).Append(" 人(装备带走不回库)");
                string msg = sb.ToString();
                DLog.Force(msg);
                if (ReferenceEquals(clan, Clan.PlayerClan)) MapSelection.Message(msg);
            }
        }

        // 逐人逃兵(与 RemoveKilled 同口径按比例); 装备在私库, 随人带走不回库(28.4)
        private static int Desert(MobileParty p, int cut)
        {
            int removed = 0;
            try
            {
                if (p == null || cut <= 0) return 0;
                var rec = Soldiers.Get(p);
                if (rec == null) rec = Soldiers.Ensure(p);
                if (rec == null || rec.List.Count == 0) return 0;
                int men = rec.List.Count;
                if (cut > men) cut = men;
                float ratio = (float)cut / men;

                var counts = new Dictionary<byte, int>();
                for (int i = 0; i < rec.List.Count; i++)
                {
                    var s = rec.List[i];
                    int old;
                    counts.TryGetValue(s.Unit, out old);
                    counts[s.Unit] = old + 1;
                }
                var map = new Dictionary<string, float>(StringComparer.Ordinal);
                var branchLoss = new Dictionary<int, int>();
                foreach (var kv in counts)
                {
                    int loss = (int)Math.Round(kv.Value * (double)ratio);
                    if (loss <= 0 && kv.Value > 0) loss = 1;
                    if (loss > kv.Value) loss = kv.Value;
                    string id = Soldiers.UnitIdOf(kv.Key);
                    map[id] = ratio;
                    int b = BranchOfUnitId(id);
                    int old;
                    branchLoss.TryGetValue(b, out old);
                    branchLoss[b] = old + loss;
                }
                removed = Soldiers.RemoveKilled(p, map);
                foreach (var kv in branchLoss) RemoveRoster(p, kv.Key, kv.Value);   // 同步原版名单
            }
            catch { }
            return removed;
        }

        private static void AccrueInterest()
        {
            try
            {
                if (Debt.Count == 0) return;
                float rate;
                try { rate = Credit.MonthlyRate; } catch { rate = 0.06f; }
                var keys = new List<string>(Debt.Keys);
                for (int i = 0; i < keys.Count; i++)
                {
                    float d;
                    if (!Debt.TryGetValue(keys[i], out d) || d <= 0f) continue;
                    float interest = d * rate / 84f;      // 月利率 → 日息(84 天/年 × 12)
                    if (interest > 0f) Debt[keys[i]] = d + interest;
                }
            }
            catch { }
        }

        // 家族债上限: 仿 Credit.Limit 口径(基准 500 + 领地/声望折算), 20,000 封顶
        private static float DebtLimit(Clan c)
        {
            try
            {
                if (c == null) return 500f;
                int fiefs = c.Fiefs != null ? c.Fiefs.Count : 0;
                float limit = 500f + fiefs * 400f + (int)c.Renown * 8f;
                if (limit > 20000f) limit = 20000f;
                return limit;
            }
            catch { return 500f; }
        }

        // 军饷(千分之一金/兵/日): 0.5 × 训练 ÷ 0.85, 取整到 0.01
        private static int WageMilliOf(string unitId)
        {
            try
            {
                var u = Equipment.UnitOf(unitId);
                if (u == null) return 500;
                int m = (int)Math.Round(500.0 * u.Training / 0.85 / 10.0) * 10;
                return m > 0 ? m : 500;
            }
            catch { return 500; }
        }

        // ==================== 装备 ====================

        // 私库中匹配该需求、且本国已解锁的型号, 按 tier 降序(同 tier 价高优先)
        private static List<KeyValuePair<EquipDef, int>> Candidates(string lordKey, Kingdom k, EquipTag tag)
        {
            var list = new List<KeyValuePair<EquipDef, int>>();
            try
            {
                foreach (var kv in Armory.EntriesOf(lordKey))
                {
                    if (kv.Value <= 0) continue;
                    var def = Equipment.Get(kv.Key);
                    if (def == null || !Equipment.MatchesTag(def, tag)) continue;
                    if (!Research.UnlockedFor(k, def.TechId)) continue;
                    list.Add(new KeyValuePair<EquipDef, int>(def, kv.Value));
                }
                list.Sort(delegate (KeyValuePair<EquipDef, int> a, KeyValuePair<EquipDef, int> b)
                {
                    if (a.Key.Tier != b.Key.Tier) return b.Key.Tier - a.Key.Tier;
                    return b.Key.Price - a.Key.Price;
                });
            }
            catch { }
            return list;
        }

        // 私库最多支撑该兵种多少人(每行需求取 min), 并生成需求说明
        private static int SupportN(string lordKey, Kingdom k, int kindIdx, out string needText)
        {
            needText = "";
            int best = int.MaxValue;
            try
            {
                var u = Equipment.Unit(kindIdx);
                if (u == null || u.Req == null) return 0;
                var sb = new StringBuilder();
                for (int r = 0; r < u.Req.Length; r++)
                {
                    var req = u.Req[r];
                    var cands = Candidates(lordKey, k, req.Tag);
                    long total = 0;
                    string model = TagName(req.Tag);
                    for (int i = 0; i < cands.Count; i++)
                    {
                        total += cands[i].Value;
                        if (i == 0) model = cands[i].Key.Name;
                    }
                    if (sb.Length > 0) sb.Append(" + ");
                    sb.Append(model).Append(req.Per100);
                    long n = req.Per100 > 0 ? total * 100L / req.Per100 : 0;
                    if (n < best) best = (int)Math.Min(int.MaxValue, n);
                }
                needText = sb.Append(" /百人").ToString();
            }
            catch { best = 0; }
            return best == int.MaxValue ? 0 : best;
        }

        // 招募 n 人的逐型号扣装计划(每行需求从高 tier 型号依次取); 返回 "" 或错误
        private static string EquipPlan(string lordKey, Kingdom k, int kindIdx, int n,
            out Dictionary<string, int> plan, out string needText)
        {
            plan = new Dictionary<string, int>(StringComparer.Ordinal);
            needText = "";
            try
            {
                var u = Equipment.Unit(kindIdx);
                if (u == null || u.Req == null) return "未知兵种装备";
                var sb = new StringBuilder();
                for (int r = 0; r < u.Req.Length; r++)
                {
                    var req = u.Req[r];
                    int qty = (int)Math.Ceiling(req.Per100 * (double)n / 100.0);
                    var cands = Candidates(lordKey, k, req.Tag);
                    if (cands.Count > 0)
                    {
                        if (sb.Length > 0) sb.Append(" + ");
                        sb.Append(cands[0].Key.Name).Append(qty);
                    }
                    for (int i = 0; i < cands.Count && qty > 0; i++)
                    {
                        string id = cands[i].Key.Id;
                        int already;
                        plan.TryGetValue(id, out already);
                        int avail = cands[i].Value - already;
                        if (avail <= 0) continue;
                        int take = Math.Min(avail, qty);
                        plan[id] = already + take;
                        qty -= take;
                    }
                    if (qty > 0)
                    {
                        needText = sb.ToString();
                        return "私库缺 " + TagName(req.Tag);
                    }
                }
                needText = sb.Append(" /百人").ToString();
                return "";
            }
            catch (Exception ex) { return "装备清点异常: " + ex.Message; }
        }

        private static void Refund(string lordKey, Dictionary<string, int> taken)
        {
            try
            {
                if (taken == null || string.IsNullOrEmpty(lordKey)) return;
                foreach (var kv in taken)
                    if (kv.Value > 0) Armory.AddKey(lordKey, kv.Key, kv.Value);
            }
            catch { }
        }

        // ==================== 名单/槽位 ====================

        // v4.238: 供国防军扩容共用(征兵地招募同样吃原版志愿兵名额, 两条路共用同一个名额池)
        internal static int ConsumeSlots(Settlement s, int n)
        {
            int used = 0;
            try
            {
                if (s == null || n <= 0) return 0;
                var notables = s.Notables;
                if (notables == null) return 0;
                for (int i = 0; i < notables.Count && used < n; i++)
                {
                    var h = notables[i];
                    if (h == null) continue;
                    var vt = h.VolunteerTypes;
                    if (vt == null) continue;
                    for (int j = 0; j < vt.Length && used < n; j++)
                    {
                        if (vt[j] == null) continue;
                        vt[j] = null;
                        used++;
                    }
                }
            }
            catch { }
            return used;
        }

        private static int PartyHeadroom(MobileParty p)
        {
            try
            {
                if (p == null || p.Party == null) return 0;
                int cap = p.Party.PartySizeLimit - p.Party.MemberRoster.TotalManCount;
                return cap > 0 ? cap : 0;
            }
            catch { return 0; }
        }

        // 兵种 → 原版名单分支: 0 步 / 1 弓 / 2 骑(用于把征募/逃兵同步进原版名册)
        private static int BranchOf(int kindIdx)
        {
            switch (kindIdx)
            {
                case Equipment.UDragoon:
                case Equipment.UHussar:
                case Equipment.UCuirassier:
                case Equipment.UHorseArcher:
                    return 2;
                case Equipment.UArcher:
                case Equipment.UCrossbow:
                    return 1;
                default:
                    return 0;   // 步兵 / 炮兵 / 工兵
            }
        }

        private static int BranchOfUnitId(string unitId)
        {
            int idx = Soldiers.UnitIndexOf(unitId);
            return idx >= 0 ? BranchOf(idx) : 0;
        }

        private static void AddRoster(MobileParty p, int kindIdx, int n)
        {
            try
            {
                if (p == null || p.MemberRoster == null || n <= 0) return;
                var t = TroopFor(p, BranchOf(kindIdx));
                if (t != null) p.MemberRoster.AddToCounts(t, n, false, 0, 0, true, -1);
            }
            catch { }
        }

        private static void RemoveRoster(MobileParty p, int branch, int n)
        {
            try
            {
                if (p == null || p.MemberRoster == null || n <= 0) return;
                var t = TroopFor(p, branch);
                if (t == null) return;
                int have = p.MemberRoster.GetTroopCount(t);
                int take = Math.Min(have, n);
                if (take > 0) p.MemberRoster.AddToCounts(t, -take, false, 0, 0, true, -1);
            }
            catch { }
        }

        private static CharacterObject TroopFor(MobileParty p, int branch)
        {
            try
            {
                if (branch < 0) branch = 0;
                if (branch > 2) branch = 2;
                CultureObject culture = null;
                try { if (p != null && p.Party != null) culture = p.Party.Culture; } catch { }
                if (culture == null && p != null && p.ActualClan != null) culture = p.ActualClan.Culture;
                if (culture == null && p != null && p.LeaderHero != null) culture = p.LeaderHero.Culture;
                var t = DefArmy.TroopsOf(culture);
                return t != null ? t[branch] : null;
            }
            catch { return null; }
        }

        // ==================== 判定/命名 ====================

        // 只服务领主/家族部队(含玩家家族); 国防军军团/商队/村民/驻军/民兵不在此列
        private static bool IsLordArmy(MobileParty p)
        {
            try
            {
                if (p == null || !p.IsActive) return false;
                if (p.IsCaravan || p.IsVillager || p.IsGarrison || p.IsMilitia) return false;
                if (DefArmy.IsDefArmyParty(p)) return false;
                if (p.ActualClan == null) return false;
                return p.IsMainParty || p.PartyComponent is LordPartyComponent;
            }
            catch { return false; }
        }

        private static Hero PayerOf(MobileParty p, Clan clan)
        {
            try
            {
                if (p != null && p.LeaderHero != null) return p.LeaderHero;
                if (clan != null && clan.Leader != null) return clan.Leader;
            }
            catch { }
            return null;
        }

        private static Kingdom KingdomOf(MobileParty p, Clan clan)
        {
            try
            {
                if (clan != null && clan.Kingdom != null) return clan.Kingdom;
                if (p != null) return p.MapFaction as Kingdom;
            }
            catch { }
            return null;
        }

        // 兵种科技门槛(28.11 / v4.241): 一律读 Equipment.Units 里登记的 TechId(空 = 开局可用),
        //   原来硬编码的 flintlock/caplock/grenadiers/carbine/warhorse 在科技表里都不存在 -> 等于不设门槛
        internal static string UnitGate(string unitId)
        {
            try { return Equipment.GateOf(unitId); } catch { }
            return "";
        }

        internal static string TechName(string techId)
        {
            if (string.IsNullOrEmpty(techId)) return "无";
            try
            {
                var t = Research.Find(techId);
                if (t != null && !string.IsNullOrEmpty(t.Name)) return t.Name;
            }
            catch { }
            return techId;
        }

        private static int FeeOf(int kindIdx)
        {
            if (kindIdx >= 0 && kindIdx < RecruitFee.Length) return RecruitFee[kindIdx];
            return 20;
        }

        private static string TagName(EquipTag tag)
        {
            if (tag == EquipTag.AnyWeapon) return "任意装备";
            if (tag == EquipTag.BowOrCrossbow) return "弓/弩";
            if (tag == EquipTag.Firearm) return "火器";
            if (tag == EquipTag.Rifle) return "来复枪/步枪";
            if (tag == EquipTag.Bow) return "弓";
            if (tag == EquipTag.Crossbow) return "弩";
            if (tag == EquipTag.Saber) return "马刀";
            if (tag == EquipTag.Lance) return "骑枪";
            if (tag == EquipTag.Carbine) return "卡宾枪";
            if (tag == EquipTag.AnyHorse) return "马匹";
            if (tag == EquipTag.HeavyHorse) return "重装战马";
            if (tag == EquipTag.LightArmor) return "轻甲";
            if (tag == EquipTag.HeavyArmor) return "重甲";
            if (tag == EquipTag.Grenade) return "手榴弹";
            if (tag == EquipTag.Gun) return "火炮";
            if (tag == EquipTag.HeavyGun) return "重炮";
            if (tag == EquipTag.LightGun) return "轻炮";
            if (tag == EquipTag.Carriage) return "炮组";
            if (tag == EquipTag.Tool) return "工具";
            if (tag == EquipTag.SupportGear) return "支援装备";
            return "装备";
        }

        private static string SetName(Settlement s)
        {
            try { return s != null && s.Name != null ? s.Name.ToString() : "?"; } catch { return "?"; }
        }

        private static string ClanKey(Clan c)
        {
            try
            {
                if (c == null) return "";
                return !string.IsNullOrEmpty(c.StringId) ? c.StringId
                    : (c.Name != null ? c.Name.ToString() : "");
            }
            catch { return ""; }
        }

        private static string ClanName(Clan c)
        {
            try { return c != null && c.Name != null ? c.Name.ToString() : "?"; } catch { return "?"; }
        }

        private static int LastLogDayOf(string key)
        {
            try
            {
                int d;
                return (!string.IsNullOrEmpty(key) && LastLogDay.TryGetValue(key, out d)) ? d : -1;
            }
            catch { return -1; }
        }

        private static void MarkLogDay(string key, int day)
        {
            try { if (!string.IsNullOrEmpty(key)) LastLogDay[key] = day; } catch { }
        }

        // ==================== 存档 FIA_LordArmy ====================
        internal static string Save()
        {
            try
            {
                var sb = new StringBuilder("v1");
                foreach (var kv in Debt)
                {
                    if (kv.Value <= 0f || string.IsNullOrEmpty(kv.Key)) continue;
                    sb.Append(';').Append(kv.Key).Append('=')
                      .Append(((int)Math.Round(kv.Value)).ToString(CultureInfo.InvariantCulture));
                }
                return sb.ToString();
            }
            catch { return "v1"; }
        }

        internal static void Load(string data)
        {
            try
            {
                Debt.Clear();
                if (string.IsNullOrEmpty(data)) return;
                var parts = data.Split(';');
                if (parts.Length < 2 || parts[0] != "v1") return;
                for (int i = 1; i < parts.Length; i++)
                {
                    string line = parts[i];
                    if (string.IsNullOrEmpty(line)) continue;
                    int at = line.IndexOf('=');
                    if (at <= 0 || at >= line.Length - 1) continue;
                    float v;
                    if (float.TryParse(line.Substring(at + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out v) && v > 0f)
                        Debt[line.Substring(0, at)] = v;
                }
                DLog.Force("领主军饷: 读档 家族债=" + Debt.Count);
            }
            catch (Exception ex) { DLog.Force("领主军饷读档异常: " + ex.Message); }
        }

        internal static void Reset()
        {
            try { Debt.Clear(); LastLogDay.Clear(); } catch { }
        }
    }
}
