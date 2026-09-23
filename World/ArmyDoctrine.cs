using System;
using System.Collections.Generic;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Localization;

namespace FeudalInternalAffairs
{
    // v4.202: 军队编制与军官(照 V3 Formation / Battalion / Veterancy / Command Limit / Combat Tactics)
    //   ① 军团老兵度(0~100): 打胜仗 +6, 驻扎 +0.15/日, 败仗 -4 -> 输出与组织度加成
    //   ② 编制上限 = 600 + 军官统率×12 + 军事科技×12 -> 超编按比例扣组织度(V3: over command limit)
    //   ③ 兵种构成战术(V3 Combat Tactics, 按营构成自动判定):
    //        骑兵>=30% -> 快速推进(输出+10%, 承受+15%)
    //        弓手>=40% -> 火力压制(杀伤+15%, 士气伤害+10%)
    //        步兵>=60% -> 稳固阵线(承受-15%)
    //   ④ 军官统率/战术技能 -> 编制上限与全军团输出
    internal static class ArmyDoctrine
    {
        internal static readonly Dictionary<string, float> Vet = new Dictionary<string, float>();   // legionId -> 老兵度
        internal static readonly Dictionary<string, int> LastVetDay = new Dictionary<string, int>();
        // v4.205: V3 组织度(0~100, 战斗 -25, 日恢复 1%, 缺粮打折) 与 伤员池
        internal static readonly Dictionary<string, float> Org = new Dictionary<string, float>();
        internal static readonly Dictionary<string, float> Wounded = new Dictionary<string, float>();
        // v4.208: V3 军团士气(0~100): 战斗 -20, 非战斗每日 +3%, 补给不足减半恢复
        internal static readonly Dictionary<string, float> Morale2 = new Dictionary<string, float>();
        internal const float MoraleMax = 100f, MoraleDailyGain = 3f, MoralePerBattle = 20f;
        internal const float OrgMax = 100f, OrgDailyGain = 1f, OrgPerBattle = 25f, WoundHealDaily = 0.05f;

        internal const float VetMax = 100f;

        // v4.21x: V3 Rail Transport 动员选项(部队大地图 +20% 速度 / 组织度 -50% / 每日耗引擎)
        internal static readonly Dictionary<string, bool> RailTransport = new Dictionary<string, bool>();   // PartyId -> 开启
        internal const float RailSpeedBonus = 0.20f;      // 行军 +20%
        internal const float RailOrgFactor = 0.5f;        // 组织度上限与当前值 ×0.5
        internal const float RailEnginePer1000Daily = 0.5f;   // 每 1000 兵力每日 0.5 引擎
        private static int _lastRailDay = -1;
        private static bool _railPatchApplied;

        // v27: 弹药基数(27.5): 军团自带 3 基数(1 基数 = 每 100 件火器 30 火药), 每轮齐射耗 1/5 基数 = 6 火药
        //      3 基数 = 15 轮齐射; 断供 -> 火器伤害 ×0.5 且每轮士气 -2
        internal static readonly Dictionary<string, float> Ammo = new Dictionary<string, float>();   // legionKey -> 基数额
        internal const float AmmoMaxBases = 3f;
        internal const int VolleysPerBase = 5;
        internal const int AmmoPowderPerBasePer100 = 30;
        private static int _lastAmmoDay = -1;

        // v4.209: 战损可读字段(战报页显示)
        internal static readonly List<string> PopLossLog = new List<string>();
        internal static readonly List<string> WeeklyLossLog = new List<string>();
        private static int _lastWeekClear = -1;

        internal static void BeginBattleLosses() { try { PopLossLog.Clear(); } catch { } }

        internal static string LossSummary()
        {
            try
            {
                var sb = new System.Text.StringBuilder();
                sb.Append("本周损耗: ");
                if (WeeklyLossLog.Count == 0) sb.Append("无");
                else for (int i = 0; i < WeeklyLossLog.Count; i++) { if (i > 0) sb.Append(" · "); sb.Append(WeeklyLossLog[i]); }
                sb.Append('\n').Append("文化战损: ");
                if (PopLossLog.Count == 0) sb.Append("无");
                else for (int i = 0; i < PopLossLog.Count; i++) { if (i > 0) sb.Append(" · "); sb.Append(PopLossLog[i]); }
                return sb.ToString();
            }
            catch { return ""; }
        }

        internal static string LegionKey(DefLegion lg)
        {
            try { return lg != null && lg.PartyId != null ? lg.PartyId : "?"; } catch { return "?"; }
        }

        internal static float MoraleOf2(string key){ float v; return (!string.IsNullOrEmpty(key) && Morale2.TryGetValue(key, out v)) ? v : MoraleMax; }
        internal static void MoraleLoss(string key, float amount){ try { if(string.IsNullOrEmpty(key)) return; float v = MoraleOf2(key) - amount; Morale2[key] = v < 0f ? 0f : v; } catch { } }
        // v4.243: 士气恢复(休整/戒备状态每日回)
        internal static void MoraleGain(string key, float amount){ try { if (string.IsNullOrEmpty(key)) return; float v = MoraleOf2(key) + amount; Morale2[key] = v > MoraleMax ? MoraleMax : v; } catch { } }
        internal static float MoraleMult(string key){ try { return 0.90f + 0.10f * (MoraleOf2(key) / MoraleMax); } catch { return 1f; } }
        internal static float OrgOf2(string key)
        {
            try
            {
                if (string.IsNullOrEmpty(key)) return OrgMax;
                float v;
                if (!Org.TryGetValue(key, out v)) v = OrgMax;
                if (IsRailKey(key))   // v4.21x: 铁路运输期间组织度上限 ×0.5
                {
                    float cap = OrgMax * RailOrgFactor;
                    if (v > cap) v = cap;
                }
                return v;
            }
            catch { return OrgMax; }
        }
        // v4.243: 组织度恢复(休整/操练/戒备状态每日回)
        internal static void OrgGain(string key, float amount)
        {
            try
            {
                if (string.IsNullOrEmpty(key)) return;
                float v = OrgOf2(key) + amount;
                Org[key] = v > OrgMax ? OrgMax : v;
            }
            catch { }
        }
        // v4.243: 伤员治疗(休整状态/爱兵如子/野战医院)
        internal static void WoundedHeal(string key, float amount)
        {
            try
            {
                if (string.IsNullOrEmpty(key) || amount <= 0f) return;
                float v;
                if (!Wounded.TryGetValue(key, out v) || v <= 0f) return;
                v -= amount;
                Wounded[key] = v < 0f ? 0f : v;
            }
            catch { }
        }
        internal static float WoundedOf(string key){ float v; return (!string.IsNullOrEmpty(key) && Wounded.TryGetValue(key, out v)) ? v : 0f; }
        internal static void OrgLoss(string key, float amount){ try { if(string.IsNullOrEmpty(key)) return; float v = OrgOf2(key) - amount; Org[key] = v < 0f ? 0f : v; } catch { } }
        internal static float VetOf(string key)
        {
            float v;
            return (!string.IsNullOrEmpty(key) && Vet.TryGetValue(key, out v)) ? v : 0f;
        }

        // ---- v27: 弹药(27.5) ----
        internal static float AmmoOf(string key)
        {
            float v;
            return (!string.IsNullOrEmpty(key) && Ammo.TryGetValue(key, out v)) ? v : AmmoMaxBases;
        }

        internal static void AmmoSet(string key, float bases)
        {
            try
            {
                if (string.IsNullOrEmpty(key)) return;
                if (bases < 0f) bases = 0f;
                if (bases > AmmoMaxBases) bases = AmmoMaxBases;
                Ammo[key] = bases;
            }
            catch { }
        }

        internal static int AmmoRoundsOf(MobileParty p)
        {
            try
            {
                var lg = DefArmy.LegionOf(p);
                if (lg == null) return int.MaxValue;   // 非我军编制不追弹药
                return (int)Math.Floor(AmmoOf(LegionKey(lg)) * VolleysPerBase);
            }
            catch { return int.MaxValue; }
        }

        internal static bool AmmoBroken(MobileParty p)
        {
            try
            {
                var lg = DefArmy.LegionOf(p);
                if (lg == null) return false;
                return AmmoOf(LegionKey(lg)) <= 0.001f;
            }
            catch { return false; }
        }

        // 断供火器伤害 ×0.5(27.5)
        internal static float AmmoMultOf(MobileParty p)
        {
            return AmmoBroken(p) ? 0.5f : 1f;
        }

        // 每轮齐射: 扣 1/5 基数 × 火器数/100
        internal static void ConsumeVolley(MobileParty p, float firearms)
        {
            try
            {
                var lg = DefArmy.LegionOf(p);
                if (lg == null || firearms <= 0f) return;
                string key = LegionKey(lg);
                float bases = AmmoOf(key) - 0.2f * (firearms / 100f);
                AmmoSet(key, bases);
            }
            catch { }
        }

        internal static float VetOfParty(MobileParty p)
        {
            try
            {
                if (p == null) return 0f;
                for (int i = 0; i < DefArmy.Legions.Count; i++)
                {
                    var lp = DefArmy.LegionParty(DefArmy.Legions[i]);
                    if (lp != null && lp == p) return VetOf(LegionKey(DefArmy.Legions[i]));
                }
            }
            catch { }
            return 0f;
        }

        // 老兵度加成: 输出 ×(1 + vet/300), 组织度 +vet/5
        internal static float VetDamageMult(float vet) { return 1f + vet / 300f; }
        // v4.205: 伤员惩罚(0.5~1) = 1 - 伤员/兵力的一半
        // v4.241: 野战医院(支援槽 1, 效果"伤员回流 +5%") -> 惩罚减轻 5%
        internal static float WoundedMult(MobileParty p)
        {
            try
            {
                if (p == null) return 1f;
                for (int i = 0; i < DefArmy.Legions.Count; i++)
                {
                    var lp = DefArmy.LegionParty(DefArmy.Legions[i]);
                    if (lp != null && lp == p)
                    {
                        string k = LegionKey(DefArmy.Legions[i]);
                        int men = 0; try { men = p.Party.NumberOfRegularMembers; } catch { }
                        float wd = WoundedOf(k);
                        if (men <= 0 || wd <= 0.5f) return 1f;
                        float m = 1f - (wd / men) * 0.5f;
                        if (m < 0.5f) m = 0.5f;
                        try { if (DefArmy.HasSupport(p, 1)) m = Math.Min(1f, m * 1.05f); } catch { }
                        return m;
                    }
                }
            }
            catch { }
            return 1f;
        }

        // 编制上限(V3: 由军官与科技决定)
        internal static int CommandLimitOf(MobileParty p)
        {
            int lim = 600;
            try
            {
                if (p != null && p.LeaderHero != null)
                {
                    lim += p.LeaderHero.GetSkillValue(DefaultSkills.Leadership) * 12;
                    lim += p.LeaderHero.GetSkillValue(DefaultSkills.Tactics) * 6;
                }
            }
            catch { }
            lim += BattleSim.MilitaryTechCount() * 12;
            return lim;
        }

        // 超编惩罚(0.5~1): 兵力超过编制上限时按比例扣
        internal static float OverLimitMult(MobileParty p)
        {
            try
            {
                if (p == null) return 1f;
                int men = 0;
                try { men = p.Party.NumberOfRegularMembers; } catch { }
                int lim = CommandLimitOf(p);
                if (lim <= 0 || men <= lim) return 1f;
                float m = lim / (float)men;
                return Math.Max(0.5f, 0.6f + 0.4f * m);
            }
            catch { return 1f; }
        }

        // 军官统率加成(全军团输出): ×(0.9 + 统率/100)
        internal static float OfficerMult(MobileParty p)
        {
            try
            {
                if (p == null || p.LeaderHero == null) return 0.9f;
                int lead = p.LeaderHero.GetSkillValue(DefaultSkills.Leadership);
                int tac = p.LeaderHero.GetSkillValue(DefaultSkills.Tactics);
                float m = 0.9f + (lead * 0.6f + tac * 0.4f) / 100f;
                return m < 0.9f ? 0.9f : (m > 1.6f ? 1.6f : m);
            }
            catch { return 1f; }
        }

        // 兵种构成(0 步兵/1 弓手/2 骑兵/3 骑射 的占比)
        internal static void CompositionOf(MobileParty p, float[] share)
        {
            try
            {
                if (share == null) return;
                for (int i = 0; i < 4; i++) share[i] = 0f;
                if (p == null || p.MemberRoster == null) return;
                int inf = 0, arch = 0, cav = 0, hcav = 0, tot = 0;
                foreach (var e in p.MemberRoster.GetTroopRoster())
                {
                    var c = e.Character;
                    if (c == null || c.IsHero || e.Number <= 0) continue;
                    tot += e.Number;
                    if (c.IsMounted && c.IsRanged) hcav += e.Number;
                    else if (c.IsMounted) cav += e.Number;
                    else if (c.IsRanged) arch += e.Number;
                    else inf += e.Number;
                }
                if (tot <= 0) return;
                share[0] = inf / (float)tot;
                share[1] = arch / (float)tot;
                share[2] = (cav + hcav) / (float)tot;
                share[3] = hcav / (float)tot;
            }
            catch { }
        }

        // 战术判定(V3 Combat Tactics): 返回战术名/说明/输出倍率/承受倍率/士气伤害倍率
        internal static string TacticOf(MobileParty p, out float outMult, out float takeMult, out float moraleMult)
        {
            outMult = 1f; takeMult = 1f; moraleMult = 1f;
            try
            {
                var sh = new float[4];
                CompositionOf(p, sh);
                if (sh[2] >= 0.30f) { outMult = 1.10f; takeMult = 1.15f; return "快速推进(骑兵≥30%)"; }
                if (sh[1] >= 0.40f) { outMult = 1.15f; moraleMult = 1.10f; return "火力压制(弓手≥40%)"; }
                if (sh[0] >= 0.60f) { takeMult = 0.85f; return "稳固阵线(步兵≥60%)"; }
            }
            catch { }
            return "标准阵型";
        }

        // 战报归档时结算老兵度(胜 +6 / 败 -4)
        internal static void OnBattle(bool ourWin)
        {
            try
            {
                for (int i = 0; i < DefArmy.Legions.Count; i++)
                {
                    var lg = DefArmy.Legions[i];
                    if (lg == null) continue;
                    string k = LegionKey(lg);
                    float v = VetOf(k);
                    v += ourWin ? 6f : -4f;
                    if (v < 0f) v = 0f;
                    if (v > VetMax) v = VetMax;
                    Vet[k] = v;
                }
                for (int i = 0; i < DefArmy.Legions.Count; i++)
                {
                    var lg2 = DefArmy.Legions[i];
                    if (lg2 == null) continue;
                    string k2 = LegionKey(lg2);
                    OrgLoss(k2, OrgPerBattle);                              // V3: 战斗损耗组织度
                    MoraleLoss(k2, MoralePerBattle);                        // V3: 战斗损耗士气
                    var lp2 = DefArmy.LegionParty(lg2);
                    if (lp2 != null)
                    {
                        int men = 0; try { men = lp2.Party.NumberOfRegularMembers; } catch { }
                        float add = men * (ourWin ? 0.06f : 0.14f);         // 胜方伤员少 / 败方伤员多
                        Wounded[k2] = Math.Min(men * 0.5f, WoundedOf(k2) + add);
                    }
                }
                DLog.Force("军团老兵度: " + (ourWin ? "胜利 +6" : "战败 -4") + ", 组织度 -25, 伤员已入池");
            }
            catch { }
        }

        // v4.207: V3 伤亡按文化比例分摊 —— 把损耗落到该军团招募地聚落对应文化的 pop 上
        internal static void ApplyPopLoss(DefLegion lg, float men, int day)
        {
            try
            {
                if (lg == null || men < 1f) return;
                string sid = lg.HomeId;
                if (string.IsNullOrEmpty(sid)) return;
                var l = Pops.Of(sid);
                if (l == null || l.Count == 0) return;
                float total = 0f;
                for (int i = 0; i < l.Count; i++) if (l[i] != null) total += l[i].Size;
                if (total < 1f) return;
                var byCul = new System.Collections.Generic.Dictionary<string, float>();
                for (int i = 0; i < l.Count; i++)
                {
                    var rec = l[i];
                    if (rec == null) continue;
                    string c = string.IsNullOrEmpty(rec.Culture) ? "?" : rec.Culture;
                    float v;
                    byCul.TryGetValue(c, out v);
                    byCul[c] = v + rec.Size;
                }
                foreach (var kv in byCul)
                {
                    float share = kv.Value / total;
                    float cut = men * share;
                    if (cut < 1f) continue;
                    string cul = kv.Key;
                    float left = cut;
                    for (int i = 0; i < l.Count && left > 0.5f; i++)
                    {
                        var rec = l[i];
                        if (rec == null) continue;
                        string c = string.IsNullOrEmpty(rec.Culture) ? "?" : rec.Culture;
                        if (c != cul) continue;
                        float take = Math.Min(rec.Size * 0.5f, left);
                        if (take < 0.5f) continue;
                        rec.Size -= take;
                        left -= take;
                    }
                    string line = Railways.NameOf(sid) + " " + CultureSystem.NameOf(cul) + " -" + ((int)cut) + " 人";
                    DLog.Force("伤亡分摊: " + sid + " " + CultureSystem.NameOf(cul) + " -" + ((int)cut) + " 人");
                    if (PopLossLog.Count < 10) PopLossLog.Add(line);
                }
            }
            catch { }
        }

        // ================= v4.21x: 军队铁路运输(V3 Rail Transport) =================
        internal static bool IsRailTransport(MobileParty p)
        {
            try
            {
                if (p == null || string.IsNullOrEmpty(p.StringId)) return false;
                return IsRailKey(p.StringId);
            }
            catch { return false; }
        }

        private static bool IsRailKey(string key)
        {
            bool on;
            return !string.IsNullOrEmpty(key) && RailTransport.TryGetValue(key, out on) && on;
        }

        // 开启: 当前组织度 ×0.5(上限同步 ×0.5, 见 OrgOf2); 关闭: 保留当前值, 上限恢复
        internal static string SetRailTransport(MobileParty p, bool on)
        {
            try
            {
                if (p == null || string.IsNullOrEmpty(p.StringId)) return "";
                EnsureRailSpeedPatch();
                if (on)
                {
                    float v;
                    if (!Org.TryGetValue(p.StringId, out v)) v = OrgMax;
                    RailTransport[p.StringId] = true;
                    Org[p.StringId] = v * RailOrgFactor;
                }
                else
                {
                    RailTransport[p.StringId] = false;
                }
                DLog.Force("铁路运输: " + (p.Name != null ? p.Name.ToString() : p.StringId)
                    + (on ? " 开启(速度 +20%, 组织度 -50%, 每日耗引擎)" : " 关闭"));
                return on ? "铁路运输 已开启: 行军 +20% · 组织度 -50% · 每日耗引擎" : "铁路运输 已关闭";
            }
            catch { }
            return "";
        }

        // 每日引擎消耗: 兵力/1000 × 0.5; 部队无引擎库存则按当前价折金扣
        internal static void DailyRailTransport(int day)
        {
            if (day == _lastRailDay) return;
            _lastRailDay = day;
            if (RailTransport.Count == 0) return;
            var active = new List<MobileParty>();
            var stale = new List<string>();
            foreach (var kv in RailTransport)
            {
                var pp = Railways.FindParty(kv.Key);
                if (!kv.Value || pp == null || !pp.IsActive) { stale.Add(kv.Key); continue; }   // v4.21x: 清理已停用/不存在/非活跃部队的 key
                active.Add(pp);
            }
            for (int i = 0; i < stale.Count; i++) RailTransport.Remove(stale[i]);
            for (int i = 0; i < active.Count; i++)
            {
                var p = active[i];
                int men = 0;
                try { men = p.Party.NumberOfRegularMembers; }
                catch { try { men = p.MemberRoster != null ? p.MemberRoster.TotalManCount : 0; } catch { } }
                float need = men / 1000f * RailEnginePer1000Daily;
                if (need <= 0.001f) continue;
                float left = ConsumeEngines(p, need);
                if (left <= 0.001f) continue;
                float price = 0f;
                try { price = EconomyWorld.National.PriceOf(FeudalGoods.Engines); } catch { price = FeudalGoods.BasePrice(FeudalGoods.Engines); }
                int gold = (int)Math.Ceiling(left * price);
                var k = p.MapFaction as Kingdom;
                if (gold > 0 && !Railways.TryPayKingdom(k, gold))
                {
                    RailTransport[p.StringId] = false;   // 付不起引擎费 -> 自动停用(避免白拿加成)
                    DLog.Force("铁路运输: " + p.StringId + " 欠引擎费 " + gold + " 金, 已自动停用");
                }
            }
        }

        // 优先扣部队随军引擎商品; 返回仍缺的量(不足部分由金币折算)
        private static float ConsumeEngines(MobileParty p, float need)
        {
            try
            {
                var item = FeudalGoods.Item(FeudalGoods.Engines);
                if (item == null) return need;
                var roster = p.ItemRoster;
                if (roster == null) return need;
                int have = roster.GetItemNumber(item);
                int take = (int)Math.Floor(need);
                if (take > have) take = have;
                if (take > 0) roster.AddToCounts(item, -take);
                return need - take;
            }
            catch { return need; }
        }

        // 速度补丁自行注册(不改 SubModule 补丁表): 部队开启铁路运输时大地图 +20%
        internal static void EnsureRailSpeedPatch()
        {
            if (_railPatchApplied) return;
            try
            {
                var h = new Harmony("FeudalInternalAffairs.RailTransportSpeed");
                h.CreateClassProcessor(typeof(RailTransportSpeedPatch)).Patch();
                _railPatchApplied = true;   // v4.21x: 注册成功才置位, 失败可重试
                DLog.Force("铁路运输: 速度补丁已注册");
            }
            catch (Exception ex) { DLog.Force("铁路运输速度补丁注册失败: " + ex.Message); }
        }

        [HarmonyPatch(typeof(DefaultPartySpeedCalculatingModel), "CalculateFinalSpeed")]
        internal static class RailTransportSpeedPatch
        {
            private static void Postfix(MobileParty mobileParty, ref ExplainedNumber finalSpeed)
            {
                try
                {
                    if (mobileParty == null) return;
                    // v4.245: 国防军速度恒锁 3.0(用户: 无论人数多少都要锁死) —— 铁路运输不再给它提速,
                    //   否则两个 Postfix 的执行顺序会让 3.0 被再乘一次 1.2(=3.6)
                    if (DefArmy.IsDefArmyParty(mobileParty)) return;
                    if (IsRailTransport(mobileParty))
                        finalSpeed.AddFactor(RailSpeedBonus, new TextObject("铁路运输"));
                }
                catch { }
            }
        }

        // 弹药补给(27.5): 耗完从最近本国城补给(耗火药); 铁路运输补给半径 ×2(提速)
        internal static void DailyAmmoResupply(int day)
        {
            if (day == _lastAmmoDay) return;
            _lastAmmoDay = day;
            try
            {
                for (int i = 0; i < DefArmy.Legions.Count; i++)
                {
                    var lg = DefArmy.Legions[i];
                    var p = DefArmy.LegionParty(lg);
                    if (p == null || !p.IsActive) continue;
                    string key = LegionKey(lg);
                    float bases = AmmoOf(key);
                    if (bases >= AmmoMaxBases - 0.001f) continue;
                    int firearms = 0;
                    try { firearms = DefArmyStats.FirearmCountOf(p.Party); } catch { }
                    if (firearms <= 0) { AmmoSet(key, AmmoMaxBases); continue; }   // 无火器 -> 弹药无意义
                    int needPowder = (int)Math.Ceiling((AmmoMaxBases - bases) * AmmoPowderPerBasePer100 * firearms / 100f);
                    if (needPowder <= 0) continue;

                    bool rail = IsRailTransport(p);
                    float range = rail ? 120f : 60f;   // 口径: 60 地图单位 / 铁路 120
                    var k = p.MapFaction as Kingdom;
                    Settlement best = null;
                    float bestD = float.MaxValue;
                    if (k != null)
                    {
                        foreach (var s in k.Settlements)
                        {
                            if (s == null || !s.IsTown) continue;
                            float d = p.Position.Distance(s.Position);
                            if (d < bestD) { bestD = d; best = s; }
                        }
                    }
                    if (best == null || bestD > range) continue;
                    var item = FeudalGoods.Item(FeudalGoods.Explosives);
                    if (item == null || best.ItemRoster == null) continue;
                    int have = best.ItemRoster.GetItemNumber(item);
                    if (have < needPowder) continue;
                    best.ItemRoster.AddToCounts(item, -needPowder);
                    AmmoSet(key, AmmoMaxBases);
                    try
                    {
                        var m = EconomyWorld.FindMarket(best.StringId);
                        if (m != null)
                        {
                            var e = m.GetOrCreate(FeudalGoods.Explosives);
                            e.DailyConsumption += needPowder;
                            if (e.Stock > needPowder) e.Stock -= needPowder; else e.Stock = 0f;
                        }
                    }
                    catch { }
                    DLog.Force("弹药补给: " + key + " 自 " + (best.Name != null ? best.Name.ToString() : best.StringId)
                        + " 领火药 " + needPowder + " -> 3 基数" + (rail ? "(铁路提速)" : ""));
                }
            }
            catch { }
        }

        // 日结: 驻扎军团老兵度缓涨
        internal static void Daily(int day)
        {
            try
            {
                try { EnsureRailSpeedPatch(); DailyRailTransport(day); } catch { }   // v4.21x: 铁路运输日耗
                try { DailyAmmoResupply(day); } catch { }                            // v27: 弹药补给
                if (day % 7 == 0 && day != _lastWeekClear) { WeeklyLossLog.Clear(); _lastWeekClear = day; }
                for (int i = 0; i < DefArmy.Legions.Count; i++)
                {
                    var lg = DefArmy.Legions[i];
                    if (lg == null) continue;
                    string k = LegionKey(lg);
                    int last;
                    if (LastVetDay.TryGetValue(k, out last) && day - last < 7) continue;
                    LastVetDay[k] = day;
                    float v = VetOf(k) + 0.15f * Math.Max(1, day - (last == 0 ? day : last));
                    // v4.205: 组织度 +1%/日(V3), 伤员每日 5% 归队
                    // v4.208: V3 士气每日 +3%(非战斗; 缺粮恢复减半)
                    try
                    {
                        var lpm = DefArmy.LegionParty(DefArmy.Legions[i]);
                        bool inBattle = false;
                        float food2 = 100f;
                        if (lpm != null)
                        {
                            try { inBattle = lpm.MapEvent != null; } catch { }
                            try { food2 = lpm.Food; } catch { }
                        }
                        if (!inBattle)
                        {
                            float gain = MoraleDailyGain * (food2 < 20f ? 0.5f : 1f);
                            float mv = MoraleOf2(k) + gain;
                            if (mv > MoraleMax) mv = MoraleMax;
                            Morale2[k] = mv;
                        }
                    }
                    catch { }
                    float orgCap = IsRailKey(k) ? OrgMax * RailOrgFactor : OrgMax;   // v4.21x: 铁路运输组织度上限 ×0.5
                    float org = OrgOf2(k) + OrgDailyGain;
                    if (org > orgCap) org = orgCap;
                    Org[k] = org;
                    float wd = WoundedOf(k) * (1f - WoundHealDaily);
                    if (wd < 0.5f) wd = 0f;
                    Wounded[k] = wd;
                    // v4.206: V3 每周损耗(4~12%): 围城/缺粮/士气崩溃时才发生; 本土补给正常不损耗
                    if (day % 7 == 0)
                    {
                        try
                        {
                            var lp3 = DefArmy.LegionParty(DefArmy.Legions[i]);
                            if (lp3 != null && lp3.Party != null)
                            {
                                float loss = 0f; string why = null;
                                bool besieging = false;
                                try { besieging = lp3.BesiegedSettlement != null; } catch { }
                                float food = 100f; try { food = lp3.Food; } catch { }
                                float mor = 100f; try { mor = lp3.Morale; } catch { }
                                if (besieging) { loss = 0.08f; why = "围城"; }
                                if (food < 20f) { loss = Math.Max(loss, 0.12f); why = "缺粮"; }
                                if (mor < 40f) { loss = Math.Max(loss, 0.06f); if (why == null) why = "士气崩溃"; }
                                if (loss > 0.001f)
                                {
                                    int removed = 0;
                                    var roster = lp3.MemberRoster;
                                    if (roster != null)
                                    {
                                        var list = new List<TaleWorlds.CampaignSystem.Roster.TroopRosterElement>(roster.GetTroopRoster());
                                        for (int ri = 0; ri < list.Count; ri++)
                                        {
                                            var e = list[ri];
                                            if (e.Character == null || e.Character.IsHero || e.Number <= 0) continue;
                                            int cut = (int)Math.Floor(e.Number * loss);
                                            if (cut < 1) continue;
                                            roster.AddToCounts(e.Character, -cut);
                                            removed += cut;
                                        }
                                    }
                                    if (removed > 0)
                                    {
                                        DLog.Force("军团损耗: " + k + " 每周 -" + removed + " 人 (" + why + ", " + ((int)(loss * 100)) + "%)");
                                        if (WeeklyLossLog.Count < 8)
                                            WeeklyLossLog.Add((lp3.Name != null ? lp3.Name.ToString() : k) + " -" + removed + " 人(" + why + ")");
                                        Wounded[k] = Math.Min(9999f, WoundedOf(k) + removed * 0.4f);
                                    }
                                }
                            }
                        }
                        catch { }
                    }
                    if (v > VetMax) v = VetMax;
                    Vet[k] = v;
                }
            }
            catch { }
        }

        internal static string Save()
        {
            try
            {
                var sb = new System.Text.StringBuilder();
                foreach (var kv in Vet) sb.Append('V').Append(kv.Key).Append(',').Append(((int)kv.Value)).Append(';');
                foreach (var kv in Org) sb.Append('O').Append(kv.Key).Append(',').Append(((int)kv.Value)).Append(';');
                foreach (var kv in Wounded) sb.Append('W').Append(kv.Key).Append(',').Append(((int)kv.Value)).Append(';');
                foreach (var kv in Morale2) sb.Append('M').Append(kv.Key).Append(',').Append(((int)kv.Value)).Append(';');
                foreach (var kv in RailTransport) if (kv.Value) sb.Append('R').Append(kv.Key).Append(",1;");   // v4.21x: 铁路运输开关
                foreach (var kv in Ammo) sb.Append('A').Append(kv.Key).Append(',').Append((int)Math.Round(kv.Value * 100f)).Append(';');   // v27: 弹药基数(×100 存)
                return sb.ToString();
            }
            catch { return ""; }
        }

        internal static void Load(string data)
        {
            try
            {
                Vet.Clear(); LastVetDay.Clear();
                RailTransport.Clear();   // v4.21x: 旧档无 R 段 -> 默认全部关闭
                Ammo.Clear();            // v27: 旧档无 A 段 -> 默认满基数(3)
                if (string.IsNullOrEmpty(data)) return;
                foreach (var seg in data.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var f = seg.Split(',');
                    int v;
                    if (f.Length != 2 || !int.TryParse(f[1], out v)) continue;
                    string key = f[0];
                    if (key.StartsWith("V")) Vet[key.Substring(1)] = v;
                    else if (key.StartsWith("O")) Org[key.Substring(1)] = v;
                    else if (key.StartsWith("W")) Wounded[key.Substring(1)] = v;
                    else if (key.StartsWith("M")) Morale2[key.Substring(1)] = v;
                    else if (key.StartsWith("R")) RailTransport[key.Substring(1)] = (v != 0);
                    else if (key.StartsWith("A")) Ammo[key.Substring(1)] = v / 100f;   // v27: 弹药基数(旧档无 -> 默认 3)
                }
                if (RailTransport.Count > 0) EnsureRailSpeedPatch();
            }
            catch { }
        }
    }
}
