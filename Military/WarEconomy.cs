using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;

namespace FeudalInternalAffairs
{
    // v4.72 经济-军事联动(用户设计确认): 三个断点(粮/钱/装备) + 战争疲劳
    //   粮: 国家粮储 < 3 天 -> 部队逃兵/停补/守军逃亡/繁荣下滑, AI 不再开战并倾向和谈
    //   钱: AI 国家金库(税入 - 军费 0.5/兵, 境外×1.5/围城×2); 连续 7 天赤字 -> 欠饷(士气-3/日/减员)
    //   装备: 军需(武器+盔甲)库存/部队 < 0.1 -> 部队损耗; 人力: 平民池 < 2% -> 无法补充
    //   焦土: 被掠夺村庄产出 -50% 持续 30 天
    internal static class WarEconomy
    {
        internal const float SoldierPay = 0.5f;      // 每兵日军费(与国防军一致)
        // v4.248: 3 天 -> 7 天(开局市场刚建立, 食物库存天然偏低, 3 天阈值会让开局就判饥荒并天天逃兵)
        internal const float FamineDays = 7f;       // 粮储低于 7 天 = 饥荒
        internal const int BankruptDays = 7;        // 连续赤字 7 天 = 破产
        internal const float EquipRatio = 0.1f;     // 军需库存/部队 < 0.1 = 装备危机(仅在交战时判定, v4.248)
        internal const float LevyRatio = 0.02f;     // 平民池 < 2% = 人力危机
        internal const int ScorchDays = 30;         // 焦土恢复天数

        // 存档
        internal static readonly Dictionary<string, float> Gold = new Dictionary<string, float>();
        internal static readonly Dictionary<string, int> DeficitDays = new Dictionary<string, int>();
        internal static readonly Dictionary<string, int> Scorched = new Dictionary<string, int>();
        internal static readonly HashSet<string> Sanctioned = new HashSet<string>();   // v4.114: 被制裁国(任一发起者)
        private static readonly Dictionary<string, HashSet<string>> SanctionBy = new Dictionary<string, HashSet<string>>();   // v4.115: 谁制裁了谁

        // 当日缓存(不存档)
        private static readonly Dictionary<string, bool> _crisis = new Dictionary<string, bool>();
        private static readonly Dictionary<string, int> _lastNotice = new Dictionary<string, int>();
        private static int _today;
        private static int _lastDay = -1;   // 去重: 读档补结算与跨日 tick 不会双算

        internal static bool IsCrisis(Kingdom k)
        {
            try
            {
                if (k == null) return false;
                bool b;
                return _crisis.TryGetValue(k.StringId, out b) && b;
            }
            catch { return false; }
        }

        internal static bool AtWarWithPlayer(Kingdom k)
        {
            try
            {
                var pk = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                return pk != null && k != null && k.IsAtWarWith(pk);
            }
            catch { return false; }
        }

        // 焦土产出乘子(供 DailySettlement 用)
        internal static float OutputMultOf(string settlementId)
        {
            try
            {
                if (string.IsNullOrEmpty(settlementId)) return 1f;
                int until;
                if (Scorched.TryGetValue(settlementId, out until))
                {
                    if (_today <= until) return 0.5f;
                    Scorched.Remove(settlementId);
                }
            }
            catch { }
            return 1f;
        }

        // ================= 每日 =================
        internal static void Daily(int day)
        {
            try
            {
                if (day == _lastDay) return;
                _lastDay = day;
                _today = day;
                var pk = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                foreach (var k in Kingdom.All)
                {
                    if (k == null || k.IsEliminated) continue;
                    bool isPlayer = pk != null && k == pk;

                    // ---- 收入 / 支出 ----
                    float income = isPlayer ? 0f : EstimateIncomeOf(k);   // 玩家由现有财政系统管
                    int troops;
                    float expense = ExpenseOf(k, out troops);

                    float gold = isPlayer ? EconomyWorld.Treasury.Gold : GoldOf(k);
                    int deficit = isPlayer ? Math.Max(0, EconomyWorld.Treasury.NegativeDays) : DeficitsOf(k);
                    if (isPlayer)
                    {
                        // v4.115: 被制裁 -> 贸易受损(按 AI 口径折算 25% 国库损失)
                        try
                        {
                            if (IsSanctioned(k))
                            {
                                float baseInc = 0f;
                                foreach (var s in k.Settlements)
                                {
                                    if (s == null) continue;
                                    if (s.Town != null) baseInc += s.Town.Prosperity * 0.05f;
                                    if (s.Village != null) baseInc += s.Village.Hearth * 0.02f;
                                }
                                int loss = (int)(baseInc * 0.7f * 0.25f);
                                if (loss > 0)
                                {
                                    EconomyWorld.TreasurySpend(loss);
                                    DLog.Force("经济制裁: 本国被制裁, 今日损失 " + loss + " 第纳尔");
                                }
                            }
                        }
                        catch { }
                    }
                    else
                    {
                        gold = gold + income - expense;
                        Gold[k.StringId] = gold;
                        if (gold < 0f) deficit++;
                        else deficit = 0;
                        DeficitDays[k.StringId] = deficit;
                    }

                    // ---- 饥荒(军队口径) ----
                    // v4.250: 饥荒 = 军队粮食储备不足(原来用平民市场消费算, 所有国家长期判饥荒 -> 部队每天 -2%)
                    float foodDays;
                    float civilianFoodDays = FoodDaysOf(k, out foodDays);   // 平民粮食安全(民生口径, 仍用于日志/提示)
                    float armyFoodDays = ArmyFoodDaysOf(k);
                    bool famine = armyFoodDays < FamineDays;
                    // v4.152: 玩家战略储备充足 -> 饥荒免疫
                    if (famine && isPlayer)
                    {
                        try { if (StrategicReserve.FamineImmune()) famine = false; } catch { }
                    }

                    // ---- 装备 / 人力 ----
                    // v4.248: 军需危机只在**交战状态**下判定 —— 和平时期储备偏低不算危机
                    //   (原来开局无战事也判"军需耗尽", 玩家一进游戏就看到军需物资危机)
                    bool atWarNow = false;
                    try
                    {
                        foreach (var other in Kingdom.All)
                        {
                            if (other == null || other == k || other.IsEliminated) continue;
                            if (k.IsAtWarWith(other)) { atWarNow = true; break; }
                        }
                    }
                    catch { }
                    float equipRatio = EquipRatioOf(k, troops);
                    // v4.250: 军需危机只对**玩家国**判定 —— 只有玩家国有本 mod 的装备体系(国家军械库/领主私库);
                    //   AI 国家的这类库永远是空的(日志实测全部 0.0%), 于是所有 AI 常年挂"军需耗尽"并被算作危机,
                    //   拖累它们的战略层决策。AI 的战争经济压力走"军费 + 破产 + 兵源"三条, 不靠这条。
                    bool equip = atWarNow && isPlayer && equipRatio < EquipRatio;
                    bool levy = LevyRatioOf(k) < LevyRatio;

                    bool bankrupt = deficit >= BankruptDays;
                    bool crisis = famine || bankrupt || equip || levy;
                    _crisis[k.StringId] = crisis;

                    // ---- 惩罚 ----
                    ApplyPenalties(k, day, famine, bankrupt, equip, levy, isPlayer);

                    // ---- 通知 ----
                    Notice(k, day, isPlayer, famine, bankrupt, equip, levy, armyFoodDays, gold);
                    // v4.248 诊断: 玩家国(或判危机的国家)每天记一行军需/粮储数值, 便于复查"开局就危机"
                    try
                    {
                        if (isPlayer || equip || famine)
                            DLog.Force("战争经济: " + (k.Name != null ? k.Name.ToString() : k.StringId)
                                + " 军需储备=" + (equipRatio * 100f).ToString("F1") + "%(阈值 " + (EquipRatio * 100f).ToString("F0")
                                + "%, 仅在交战时判定) 交战=" + atWarNow + " 判定=" + (equip ? "军需危机" : "正常")
                                + " · 军粮=" + armyFoodDays.ToString("F1") + "天(阈值 " + FamineDays.ToString("F0") + ")"
                                + " 判定=" + (famine ? "饥荒" : "正常")
                                + " · 民粮=" + civilianFoodDays.ToString("F1") + "天(民生口径, 不触发军队减员)");
                    }
                    catch { }
                }

                // ---- 焦土标记: 被掠夺的村庄 ----
                foreach (var s in Settlement.All)
                {
                    try
                    {
                        if (s == null || s.Village == null) continue;
                        if (s.Village.VillageState == Village.VillageStates.Looted)
                        {
                            int until;
                            if (!Scorched.TryGetValue(s.StringId, out until) || until < day)
                                Scorched[s.StringId] = day + ScorchDays;
                        }
                    }
                    catch { }
                }

                // 清理过期焦土
                if (Scorched.Count > 0)
                {
                    var del = new List<string>();
                    foreach (var kv in Scorched) if (day > kv.Value) del.Add(kv.Key);
                    for (int i = 0; i < del.Count; i++) Scorched.Remove(del[i]);
                }

                // v27: 军械库工厂订单推进(材料已在 Armory 内经 TryConsumeMaterials 从市场扣)
                try { Armory.Daily(day); } catch { }
            }
            catch (Exception ex) { DLog.Force("战争经济异常: " + ex.Message); }
        }

        // AI 国家单日税收(简化口径: 人头税 5%/繁荣 + 土地税 2%/户数, 实收 70%)
        private static float EstimateIncomeOf(Kingdom k)
        {
            float inc = 0f;
            try
            {
                foreach (var s in k.Settlements)
                {
                    if (s == null) continue;
                    if (s.Town != null) inc += s.Town.Prosperity * 0.05f;
                    if (s.Village != null) inc += s.Village.Hearth * 0.02f;
                }
            }
            catch { }
            float total = inc * 0.7f;
            try { if (IsSanctioned(k)) total *= 0.75f; } catch { }   // v4.114: 经济制裁 -> 收入 -25%
            try { total = AiEconomyDeep.IncomeAdjust(k, total); } catch { }   // v4.149: AI 税制/行会/铸币/信贷
            try { total *= NationalSpirits.IncomeMult(k); } catch { }         // v4.150: 民族精神(重商主义/霸权等)
            return total;
        }

        // v4.114/4.115: 经济制裁(玩家与 AI 通用)
        internal static bool IsSanctioned(Kingdom k)
        {
            try { return k != null && Sanctioned.Contains(k.StringId); }
            catch { return false; }
        }

        internal static bool SanctionedBy(Kingdom target, Kingdom by)
        {
            try
            {
                if (target == null || by == null) return false;
                HashSet<string> set;
                return SanctionBy.TryGetValue(target.StringId, out set) && set.Contains(by.StringId);
            }
            catch { return false; }
        }

        internal static void SetSanction(Kingdom target, Kingdom by, bool on)
        {
            try
            {
                if (target == null || by == null) return;
                HashSet<string> set;
                if (!SanctionBy.TryGetValue(target.StringId, out set))
                {
                    set = new HashSet<string>();
                    SanctionBy[target.StringId] = set;
                }
                if (on) set.Add(by.StringId);
                else set.Remove(by.StringId);
                if (set.Count == 0) Sanctioned.Remove(target.StringId);
                else Sanctioned.Add(target.StringId);
                DLog.Force("制裁: " + (by.Name != null ? by.Name.ToString() : by.StringId)
                    + (on ? " -> " : " 解除 -> ") + (target.Name != null ? target.Name.ToString() : target.StringId));
            }
            catch { }
        }

        // v4.115: 清除某国发起的全部制裁(AI 每月重建名单用)
        internal static void ClearSanctionsBy(Kingdom by)
        {
            try
            {
                if (by == null) return;
                var targets = new List<string>(SanctionBy.Keys);
                for (int i = 0; i < targets.Count; i++)
                {
                    HashSet<string> set;
                    if (!SanctionBy.TryGetValue(targets[i], out set)) continue;
                    set.Remove(by.StringId);
                    if (set.Count == 0) Sanctioned.Remove(targets[i]);
                }
            }
            catch { }
        }

        // 军事支出: 每兵 0.5/日; 境外 ×1.5; 围城 ×2
        private static float ExpenseOf(Kingdom k, out int troops)
        {
            troops = 0;
            float exp = 0f;
            try
            {
                foreach (var p in MobileParty.All)
                {
                    if (p == null || !p.IsActive || p.MapFaction != k) continue;
                    if (p.IsGarrison || p.IsMilitia || p.IsCaravan) continue;
                    int n = 0;
                    try { if (p.MemberRoster != null) n = p.MemberRoster.TotalManCount; } catch { }
                    troops += n;
                    float cost = n * SoldierPay;
                    try
                    {
                        float x = p.Position.X, y = p.Position.Y;
                        var owner = TerritoryData.OwnerAt(x, y);
                        if (owner != null && owner != k) cost *= 1.5f;
                        if (p.BesiegedSettlement != null) cost *= 2f;
                    }
                    catch { }
                    exp += cost;
                }
            }
            catch { }
            return exp;
        }

        // 军队粮食可支撑天数 = 全国粮仓 / (在野部队+驻军 日耗)
        // v4.250: 饥荒判定改用**军队口径**。原来用"平民市场粮食库存 ÷ 市场日消费", 而平民消费量远大于
        //   军粮需求, 于是所有国家(含玩家)长期显示"粮储 3~7 天"-> 每天判饥荒 -> 每支非国防军部队
        //   每天逃兵 2%(一周 -14%), 部队被慢慢蒸发(用户看到的"刚开局就不安稳")。
        //   平民缺粮属于民生问题(繁荣/激进), 不该让军队按这个数字掉人。
        internal static float ArmyFoodDaysOf(Kingdom k)
        {
            try
            {
                if (k == null) return 99f;
                float stock = 0f;
                foreach (var s in k.Settlements)
                {
                    if (s == null || s.ItemRoster == null) continue;
                    for (int i = 0; i < FeudalGoods.Main.Count; i++)
                    {
                        var g = FeudalGoods.Main[i];
                        if (g == null || !g.IsFood) continue;
                        var it = FeudalGoods.Item(g.Id);
                        if (it != null) stock += s.ItemRoster.GetItemNumber(it);
                    }
                }
                float need = 0f;
                foreach (var p in MobileParty.All)
                {
                    if (p == null || !p.IsActive) continue;
                    if (p.MapFaction != k) continue;
                    if (p.IsCaravan || p.IsMilitia) continue;
                    int men = 0;
                    try { men = p.MemberRoster != null ? p.MemberRoster.TotalManCount : 0; } catch { }
                    need += men * DefArmy.FoodPerManPerDay;
                }
                if (need <= 0.01f) return 99f;
                return stock / need;
            }
            catch { return 99f; }
        }

        // 国家粮食可支撑天数(库存 / 日耗, 只看食物); v4.75p: 公开给选国侧栏展示
        // v4.248: 库存改为"城镇市场 + 村庄粮仓"都算 —— 原来只算城镇市场, 开局市场刚建立时库存天然偏低,
        //   于是刚开局就判饥荒(每支部队每天逃兵 2%, 用户看到的"开局军需/物资危机")
        internal static float FoodDaysOf(Kingdom k, out float days)
        {
            days = 99f;
            try
            {
                float stock = 0f, need = 0f;
                foreach (var s in k.Settlements)
                {
                    if (s == null) continue;
                    var roster = s.ItemRoster;
                    for (int i = 0; i < FeudalGoods.Main.Count; i++)
                    {
                        var g = FeudalGoods.Main[i];
                        if (g == null || !g.IsFood) continue;
                        var it = FeudalGoods.Item(g.Id);
                        if (it != null && roster != null) stock += roster.GetItemNumber(it);   // 城镇市场 + 村庄粮仓
                        if (s.Town == null) continue;
                        var m = EconomyWorld.FindMarket(s.StringId);
                        if (m != null)
                        {
                            var e = m.Get(g.Id);
                            if (e != null) need += e.DailyConsumption;
                        }
                    }
                }
                if (need > 0.01f) days = stock / need;
                else days = stock > 0f ? 99f : 0f;
            }
            catch { }
            return days;
        }

        // 军需库存(武器+盔甲) / 部队人数
        // v4.248: ①把**国家军械库**(Armory —— 本 mod 装备体系的实际库存)计入军需库存:
        //   原来只看城镇市场的「武器/盔甲」货架, 于是"军械库里堆着几万件装备"也照样判"军需耗尽"
        //   (用户: 刚开局就出现军需物资危机); ②只有部队规模够看时才判(小部队不吃这个口径)
        private static float EquipRatioOf(Kingdom k, int troops)
        {
            try
            {
                if (troops <= 0) return 99f;
                int stock = 0;
                foreach (var s in k.Settlements)
                {
                    if (s == null) continue;
                    var roster = s.ItemRoster;
                    if (roster == null) continue;
                    var w = FeudalGoods.Item(FeudalGoods.Weapons);
                    var a = FeudalGoods.Item(FeudalGoods.Armor);
                    if (w != null) stock += roster.GetItemNumber(w);
                    if (a != null) stock += roster.GetItemNumber(a);
                }
                // v4.248: 国家军械库(装备体系库存)
                try
                {
                    string key = Armory.NationalOwner(k);
                    if (!string.IsNullOrEmpty(key)) stock += Armory.TotalCount(key);
                }
                catch { }
                // v4.248: 各领主私库里的武器/护甲(领主军的装备储备也算国家可动员军需)
                try
                {
                    foreach (var c in Clan.All)
                    {
                        if (c == null || c.Kingdom != k) continue;
                        string lk = Armory.LordOwner(c);
                        if (string.IsNullOrEmpty(lk)) continue;
                        var entries = Armory.EntriesOf(lk);
                        for (int i = 0; i < entries.Count; i++)
                        {
                            var def = Equipment.Get(entries[i].Key);
                            if (def == null) continue;
                            if (def.Cat == EquipCat.Melee || def.Cat == EquipCat.Ranged
                                || def.Cat == EquipCat.Artillery || def.Cat == EquipCat.Armor)
                                stock += entries[i].Value;
                        }
                    }
                }
                catch { }
                return stock / (float)troops;
            }
            catch { return 99f; }
        }

        // 平民池 / 总人口
        private static float LevyRatioOf(Kingdom k)
        {
            try
            {
                float commoners = 0f, total = 0f;
                foreach (var s in k.Settlements)
                {
                    if (s == null) continue;
                    var list = Pops.Of(s.StringId);
                    if (list == null) continue;
                    for (int i = 0; i < list.Count; i++)
                    {
                        var r = list[i];
                        if (r == null) continue;
                        total += r.Size;
                    }
                    commoners += DefArmy.CommonersOf(s);
                }
                if (total <= 0.5f) return 99f;
                return commoners / total;
            }
            catch { return 99f; }
        }

        // ================= 惩罚 =================
        private static void ApplyPenalties(Kingdom k, int day, bool famine, bool bankrupt, bool equip, bool levy, bool isPlayer)
        {
            try
            {
                float lose = 0f;
                if (famine) lose += 0.02f;      // 饥荒: 逃兵 2%/日
                if (bankrupt) lose += 0.01f;    // 欠饷: 1%/日
                // v4.93: 删除"装备枯竭"减员(用户明确反对该机制; 缺装统一走满足率乘子)
                if (levy) lose += 0.01f;        // 无人可补: 1%/日
                if (lose > 0.29f) lose = 0.29f;

                // 部队减员 + 欠饷士气
                int lostTotal = 0, lostParties = 0;
                foreach (var p in MobileParty.All)
                {
                    if (p == null || !p.IsActive || p.MapFaction != k) continue;
                    if (p.IsGarrison || p.IsMilitia || p.IsCaravan) continue;
                    if (p == MobileParty.MainParty) continue;   // 玩家本人部队不自动减员
                    if (DefArmy.IsDefArmyParty(p)) continue;    // v4.91: 国防军有独立补给(喂粮/军饷), 不吃战争经济惩罚减员
                    if (lose > 0f)
                    {
                        int got = CutTroops(p, lose);
                        if (got > 0) { lostTotal += got; lostParties++; }
                    }
                    if (bankrupt)
                    {
                        try { p.RecentEventsMorale = Math.Max(-100f, p.RecentEventsMorale - 3f); } catch { }
                    }
                }
                // v4.92: 减员给玩家发消息(说明原因), 用户要求"给人减少的时候加上消息"
                if (isPlayer && lostTotal > 0)
                {
                    string reason = "";
                    if (famine) reason += "饥荒 ";
                    if (bankrupt) reason += "欠饷 ";
                    if (equip) reason += "装备枯竭 ";
                    if (levy) reason += "兵源枯竭 ";
                    try { MapSelection.Message("战争经济减员(" + reason.Trim() + "): " + lostParties + " 支部队共 -" + lostTotal + " 人(国防军有独立补给, 不受此项)"); } catch { }
                    DLog.Force("战争经济: 玩家王国惩罚减员 -" + lostTotal + " 人(" + lostParties + " 支部队) 原因: " + reason);
                }

                // 饥荒: 守军逃亡 + 繁荣下滑
                if (famine)
                {
                    foreach (var s in k.Settlements)
                    {
                        if (s == null || s.Town == null) continue;
                        try
                        {
                            var gp = s.Town.GarrisonParty;
                            if (gp != null && gp.MemberRoster != null && gp.MemberRoster.TotalManCount > 0)
                                CutTroops(gp, 0.005f);
                        }
                        catch { }
                        try
                        {
                            float pr = s.Town.Prosperity;
                            if (pr > 50f) s.Town.Prosperity = pr * 0.995f;
                        }
                        catch { }
                    }
                }
            }
            catch { }
        }

        // 按比例裁减部队(从低阶兵开始; 跳过英雄; 供厌战系统共用); v4.92: 返回实际裁减人数
        internal static int CutTroops(MobileParty p, float ratio)
        {
            int removed = 0;
            try
            {
                var roster = p.MemberRoster;
                if (roster == null) return 0;
                int total = roster.TotalManCount;
                int n = (int)Math.Ceiling(total * ratio);
                if (n < 1) n = 1;
                int guard = 0;
                for (int i = 0; i < roster.Count && n > 0 && guard < 400; i++, guard++)
                {
                    var el = roster.GetElementCopyAtIndex(i);
                    var c = el.Character;
                    if (c == null || c.IsHero) continue;
                    int take = Math.Min(n, el.Number);
                    if (take <= 0) continue;
                    roster.AddToCounts(c, -take);
                    n -= take;
                    removed += take;
                }
            }
            catch { }
            return removed;
        }

        // ================= 查询/通知 =================
        // v4.106: AI 建设/扩军用的公开金库访问
        internal static float GoldOfPublic(Kingdom k) { return k != null ? GoldOf(k) : 0f; }

        internal static void SpendPublic(Kingdom k, float v)
        {
            try
            {
                if (k == null || v <= 0f) return;
                float g = GoldOf(k) - v;
                Gold[k.StringId] = g < 0f ? 0f : g;
            }
            catch { }
        }

        // ================= 军械库/工厂接线(第 27 章) =================
        // 国家资金口径: 玩家国 = 国库, AI 国 = 战经金库
        internal static float KingdomGold(Kingdom k)
        {
            try
            {
                if (k == null) return 0f;
                var pk = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                if (pk != null && k == pk) return EconomyWorld.Treasury.Gold;
                return GoldOf(k);
            }
            catch { return 0f; }
        }

        internal static bool TryPayKingdom(Kingdom k, int v)
        {
            try
            {
                if (k == null) return false;
                if (v <= 0) return true;
                var pk = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                if (pk != null && k == pk)
                {
                    if (EconomyWorld.Treasury.Gold < v) return false;
                    EconomyWorld.TreasurySpend(v);
                    return true;
                }
                if (GoldOf(k) < v) return false;
                SpendPublic(k, v);
                return true;
            }
            catch { return false; }
        }

        internal static void RefundKingdom(Kingdom k, int v)
        {
            try
            {
                if (k == null || v <= 0) return;
                var pk = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                if (pk != null && k == pk) { EconomyWorld.TreasuryAdd(v); return; }
                AddPublic(k, v);
            }
            catch { }
        }

        // 工厂用料: 先在市场上验量(不扣), 再由 TryConsumeMaterials 实扣
        internal static bool HasMaterials(Kingdom k, List<string> goods, List<int> amounts)
        {
            try
            {
                if (k == null) return false;
                if (goods == null || amounts == null || goods.Count == 0) return true;
                for (int i = 0; i < goods.Count && i < amounts.Count; i++)
                    if (AmountAvailable(k, goods[i]) < amounts[i]) return false;
                return true;
            }
            catch { return false; }
        }

        internal static bool TryConsumeMaterials(Kingdom k, List<string> goods, List<int> amounts)
        {
            try
            {
                if (k == null) return false;
                if (goods == null || amounts == null || goods.Count == 0) return true;
                for (int i = 0; i < goods.Count && i < amounts.Count; i++)
                    if (AmountAvailable(k, goods[i]) < amounts[i]) return false;

                for (int i = 0; i < goods.Count && i < amounts.Count; i++)
                {
                    int left = amounts[i];
                    if (left <= 0) continue;
                    var item = FeudalGoods.Item(goods[i]);
                    if (item == null) return false;
                    foreach (var s in k.Settlements)
                    {
                        if (s == null || left <= 0) continue;
                        var roster = s.ItemRoster;
                        if (roster == null) continue;
                        int have = roster.GetItemNumber(item);
                        if (have <= 0) continue;
                        int take = Math.Min(have, left);
                        roster.AddToCounts(item, -take);
                        left -= take;
                        // 物理扣减同步到市场(军用采购; 武器/盔甲/皮革从此不上市的口径)
                        try
                        {
                            var m = EconomyWorld.FindMarket(s.StringId);
                            if (m != null)
                            {
                                var e = m.GetOrCreate(goods[i]);
                                e.DailyConsumption += take;
                                if (e.Stock > take) e.Stock -= take; else e.Stock = 0f;
                            }
                        }
                        catch { }
                    }
                    if (left > 0) return false;
                }
                return true;
            }
            catch { return false; }
        }

        private static int AmountAvailable(Kingdom k, string goodId)
        {
            try
            {
                var item = FeudalGoods.Item(goodId);
                if (item == null) return 0;
                int have = 0;
                foreach (var s in k.Settlements)
                {
                    if (s == null) continue;
                    var roster = s.ItemRoster;
                    if (roster != null) have += roster.GetItemNumber(item);
                }
                return have;
            }
            catch { return 0; }
        }

        // v4.149: AI 信贷借入/危机查询
        internal static void AddPublic(Kingdom k, float v)
        {
            try
            {
                if (k == null || v <= 0f) return;
                Gold[k.StringId] = GoldOf(k) + v;
            }
            catch { }
        }

        internal static int DeficitDaysOf(Kingdom k)
        {
            try
            {
                if (k == null) return 0;
                int d;
                return DeficitsOf(k) >= 0 && DeficitDays.TryGetValue(k.StringId, out d) ? d : 0;
            }
            catch { return 0; }
        }

        internal static bool IsScorched(string settlementId)
        {
            try { return !string.IsNullOrEmpty(settlementId) && Scorched.ContainsKey(settlementId); }
            catch { return false; }
        }

        private static float GoldOf(Kingdom k)
        {
            try
            {
                float g;
                if (Gold.TryGetValue(k.StringId, out g)) return g;
                // 首见: 按规模初始化
                float init = 2000f;
                try
                {
                    foreach (var s in k.Settlements)
                    {
                        if (s == null) continue;
                        if (s.Town != null) init += s.Town.Prosperity * 0.3f;
                        if (s.Village != null) init += s.Village.Hearth * 0.1f;
                    }
                }
                catch { }
                Gold[k.StringId] = init;
                return init;
            }
            catch { return 2000f; }
        }

        private static int DeficitsOf(Kingdom k)
        {
            try
            {
                int d;
                return DeficitDays.TryGetValue(k.StringId, out d) ? d : 0;
            }
            catch { return 0; }
        }

        private static void Notice(Kingdom k, int day, bool isPlayer, bool famine, bool bankrupt, bool equip, bool levy,
            float foodDays, float gold)
        {
            try
            {
                if (!famine && !bankrupt && !equip && !levy) return;
                int last;
                if (_lastNotice.TryGetValue(k.StringId, out last) && day - last < 7) return;
                _lastNotice[k.StringId] = day;

                var sb = new StringBuilder();
                if (famine) sb.Append("军粮告急(军队储备 ").Append(foodDays.ToString("F1")).Append(" 天) ");
                if (bankrupt) sb.Append("国库破产 ");
                if (equip) sb.Append("军需耗尽(交战中储备不足) ");
                if (levy) sb.Append("兵源枯竭 ");
                string msg = (k.Name != null ? k.Name.ToString() : k.StringId) + ": " + sb.ToString().Trim();

                if (isPlayer || AtWarWithPlayer(k))
                    MapSelection.Message("经济危机 — " + msg + (famine ? " (部队正在流失)" : ""));
                DLog.Force("战争经济: " + msg + " | 金库=" + ((int)gold));
            }
            catch { }
        }

        // ================= 存档 =================
        internal static string Save()
        {
            try
            {
                var sb = new StringBuilder();
                sb.Append("1");
                foreach (var kv in Gold)
                    sb.Append('|').Append('G').Append(':').Append(kv.Key).Append(':').Append(kv.Value.ToString("F0", CultureInfo.InvariantCulture));
                foreach (var kv in DeficitDays)
                    sb.Append('|').Append('D').Append(':').Append(kv.Key).Append(':').Append(kv.Value);
            foreach (var kv in Scorched)
                sb.Append('|').Append('S').Append(':').Append(kv.Key).Append(':').Append(kv.Value);
            foreach (var kv in SanctionBy)
            {
                if (kv.Value == null) continue;
                foreach (var by in kv.Value)
                    sb.Append('|').Append('N').Append(':').Append(kv.Key).Append(':').Append(by);   // v4.115: N:target:by
            }
            // 注: 军械库/订单已独立为 FIA_Armory / FIA_Orders(见 DefArmyBehavior / World\Armory.cs)
            return sb.ToString();
            }
            catch { return "1"; }
        }

        internal static void Load(string data)
        {
            try
            {
                Gold.Clear();
                DeficitDays.Clear();
                Scorched.Clear();
                Sanctioned.Clear();   // v4.114
                SanctionBy.Clear();   // v4.115
                if (string.IsNullOrEmpty(data)) return;
                var parts = data.Split('|');
                for (int i = 1; i < parts.Length; i++)
                {
                    var seg = parts[i];
                    if (string.IsNullOrEmpty(seg)) continue;
                    var f = seg.Split(':');
                    if (f.Length < 3) continue;
                    string kind = f[0];
                    string id = f[1];
                    if (kind == "G")
                    {
                        float g;
                        if (float.TryParse(f[2], NumberStyles.Float, CultureInfo.InvariantCulture, out g)) Gold[id] = g;
                    }
                    else if (kind == "D")
                    {
                        int d;
                        if (int.TryParse(f[2], out d)) DeficitDays[id] = d;
                    }
                    else if (kind == "S")
                    {
                        int s;
                        if (int.TryParse(f[2], out s)) Scorched[id] = s;
                    }
                    else if (kind == "N")
                    {
                        // v4.115: N:target:by
                        string by = f.Length >= 3 ? f[2] : "1";
                        if (by == "1") { Sanctioned.Add(id); continue; }   // 旧格式(只知被制裁, 无发起者)
                        HashSet<string> set;
                        if (!SanctionBy.TryGetValue(id, out set)) { set = new HashSet<string>(); SanctionBy[id] = set; }
                        set.Add(by);
                        Sanctioned.Add(id);
                    }
                }
                DLog.Force("战争经济: 读档 金库=" + Gold.Count + " 焦土=" + Scorched.Count);
            }
            catch (Exception ex) { DLog.Force("战争经济读档异常: " + ex.Message); }
        }

        internal static void Reset()
        {
            Gold.Clear();
            DeficitDays.Clear();
            Scorched.Clear();
            Sanctioned.Clear();   // v4.114
            SanctionBy.Clear();   // v4.115
            _crisis.Clear();
            _lastNotice.Clear();
            _lastDay = -1;
            Armory.Reset();       // v27: 军械库/订单
        }
    }
}
