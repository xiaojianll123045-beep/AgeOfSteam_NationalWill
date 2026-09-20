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
        internal const float FamineDays = 3f;       // 粮储低于 3 天 = 饥荒
        internal const int BankruptDays = 7;        // 连续赤字 7 天 = 破产
        internal const float EquipRatio = 0.1f;     // 军需库存/部队 < 0.1 = 装备危机
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

                    // ---- 饥荒 ----
                    float foodDays;
                    bool famine = FoodDaysOf(k, out foodDays) < FamineDays;

                    // ---- 装备 / 人力 ----
                    bool equip = EquipRatioOf(k, troops) < EquipRatio;
                    bool levy = LevyRatioOf(k) < LevyRatio;

                    bool bankrupt = deficit >= BankruptDays;
                    bool crisis = famine || bankrupt || equip || levy;
                    _crisis[k.StringId] = crisis;

                    // ---- 惩罚 ----
                    ApplyPenalties(k, day, famine, bankrupt, equip, levy, isPlayer);

                    // ---- 通知 ----
                    Notice(k, day, isPlayer, famine, bankrupt, equip, levy, foodDays, gold);
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

        // 国家粮食可支撑天数(库存 / 日耗, 只看食物); v4.75p: 公开给选国侧栏展示
        internal static float FoodDaysOf(Kingdom k, out float days)
        {
            days = 99f;
            try
            {
                float stock = 0f, need = 0f;
                foreach (var s in k.Settlements)
                {
                    if (s == null || s.Town == null) continue;
                    var roster = s.ItemRoster;
                    var m = EconomyWorld.FindMarket(s.StringId);
                    for (int i = 0; i < FeudalGoods.Main.Count; i++)
                    {
                        var g = FeudalGoods.Main[i];
                        if (g == null || !g.IsFood) continue;
                        var it = FeudalGoods.Item(g.Id);
                        if (it != null && roster != null) stock += roster.GetItemNumber(it);
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
                // v4.93: 删除"装备枯竭"减员(用户: 装备不足减员个锤子啊)
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
                if (famine) sb.Append("饥荒(粮储 ").Append(foodDays.ToString("F1")).Append(" 天) ");
                if (bankrupt) sb.Append("国库破产 ");
                if (equip) sb.Append("军需耗尽 ");
                if (levy) sb.Append("兵源枯竭 ");
                string msg = (k.Name != null ? k.Name.ToString() : k.StringId) + ": " + sb.ToString().Trim();

                if (isPlayer || AtWarWithPlayer(k))
                    MapSelection.Message("经济危机 — " + msg + " (部队正在流失)");
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
        }
    }
}
