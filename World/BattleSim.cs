using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace FeudalInternalAffairs
{
    // v4.201: 坐镇指挥战斗内核(照 V3 官方战斗模型重写, 不敷衍)
    //   V3 每轮: ①统计可战兵力 ②双方造成伤亡 ③收治伤员 ④按伤亡扣士气 ⑤一方士气归零/被歼则结束
    //   关键机制: 战斗宽度 = ceil((5 + 0.5 × 基建) × 地形系数); 战斗条件(突袭/堑壕/泥泞/遭遇/暴风);
    //             组织度 0~100%(影响输出); 兵种克制(骑克弓/弓克步/步克骑); 军事科技; 补给不足惩罚
    internal static class BattleSim
    {
        // ---------------- 战斗条件(V3: 每场战斗随机一个, 双方共享) ----------------
        internal class Condition
        {
            internal string Name, Desc;
            internal float CasTaken = 1f;      // 承受伤亡倍率
            internal float MoraleDmg = 1f;     // 士气伤害倍率
            internal float Kill = 1f;          // 杀伤倍率
            internal float Width = 1f;         // 战斗宽度倍率
        }

        internal static readonly Condition[] Conditions =
        {
            new Condition { Name = "遭遇战", Desc = "双方在开阔地遭遇, 无额外修正" },
            new Condition { Name = "突袭", Desc = "攻方抢先展开: 攻方 +25% 杀伤, 守方 +25% 承受伤亡", Kill = 1.25f },
            new Condition { Name = "堑壕战", Desc = "守方据壕而守: 承受伤亡 -35%, 士气伤害 +30%", CasTaken = 0.65f, MoraleDmg = 1.3f },
            new Condition { Name = "泥泞", Desc = "道路泥泞: 战斗宽度 -30%, 承受伤亡 +15%", Width = 0.7f, CasTaken = 1.15f },
            new Condition { Name = "暴风", Desc = "风雨交加: 杀伤 -20%, 士气伤害 +20%", Kill = 0.8f, MoraleDmg = 1.2f }
        };

        // ---------------- 一场战斗的聚合状态 ----------------
        internal class State
        {
            internal string BattleId;
            internal int CondIdx;
            internal int Day;
            internal bool AttackerIsPlayer;      // 我方是否为攻方
            internal float OurDmg, TheirDmg;     // 累计伤害(我方造成 / 我方承受)
            internal int Hits, OurMen, TheirMen;
            internal float OurTierSum, TheirTierSum;
            internal int OurHits, TheirHits;
            internal float Width;
            internal string Terrain = "平原";
            internal List<string> Detail = new List<string>();   // 逐条伤害修正(战报明细)
            internal MapEvent Ref;   // 归档战报用
            // v27: 分阶段坐镇指挥(27.6)
            internal SideSnap SnapA, SnapB;                      // 攻方 / 守方 开战快照
            internal List<string> Stages = new List<string>();   // 逐阶段明细
            internal string Verdict = "";
            internal bool Staged;
        }

        // v27: 单侧快照(27.6 分阶段结算输入)
        internal class SideSnap
        {
            internal bool IsAttacker;
            internal string Name = "?";
            internal int Inf, Shoot, Cav, HCav;                  // 步兵/射手/骑兵/骑射
            internal int Cannons;                                // 参战炮数
            internal int MGs;                                    // v4.241: 机枪挺数(1 挺/百人, 27.2)
            internal float CannonDmg = 90f, CannonPen = 60f;     // 火炮伤害/破甲(兜底=前装6磅; 参战炮数就绪后细化)
            internal int Hussars;                                // 骠骑兵(缴获 +20%)
            internal int InitialMen;
            internal int Lost;
            internal int[] TierMen = new int[7];
            internal int[] TierFireMen = new int[7];
            internal float Dmg = 45f, Pierce = 20f, Reload = 1.5f, Accuracy = 1f, Range = 4f;
            internal float FirearmShare;                         // 射手中火器占比
            internal float Armor = 10f, Training = 1f;
            internal float Morale = 70f, Org = 100f, Speed = 30f;
            internal float EquipMult = 1f;                       // 缺装乘子(27.4)
            internal float MeleeTrait = 1f;                      // v4.241: 兵种特性(掷弹兵近战 +20% / 胸甲骑兵冲击 +40%, 按占比)
            internal float SiegeTrait = 1f;                      // v4.241: 攻城时额外(攻城炮兵 +50% / 掷弹兵攻坚 +25%, 按占比)
            internal bool Sieging;                               // v4.241: 该方正在围城
            // v4.243: 军团状态 / 将军特质 / 训练度
            internal float StanceMult = 1f;                      // 戒备 +5% / 休整 -10%
            internal float TraitMult = 1f;                       // 政治将军 -5%
            internal float BombardMult = 1f;                     // 炮兵专家 +20%
            internal float MeleeMult = 1f;                       // 步兵操典家 +10%
            internal float LossTakenMult = 1f;                   // 堑壕老兵 0.85
            internal float LootMult = 1f;                        // 骑兵将领 1.25
            internal float TrainMult = 1f;                       // 训练度系数(0.8~1.2)
            internal bool FoodHalf;                              // 后勤天才: 缺粮惩罚减半
            internal float LegacyMult = 1f;                      // 既有单次因子合并(科技/老兵/军官/伤员/缺粮/宽度/战术)
            internal float CondKill = 1f;                        // 战斗条件 杀伤倍率
            internal float CasTaken = 1f;                        // 战斗条件 承受伤亡倍率
            internal bool TerrainAdapt;                          // 轻步兵地形适应
            internal int AmmoRounds = 15;
            internal bool AmmoTracked;
            internal MobileParty AmmoParty;
            internal MobileParty LeaderParty;
            internal Kingdom Kingdom;
            internal IFaction Faction;
            internal bool Crashed;

            internal int Total { get { return Inf + Shoot + Cav + HCav; } }
            internal int RangedMen { get { return Shoot + HCav; } }
        }

        private static readonly Dictionary<string, State> _states = new Dictionary<string, State>();
        internal static readonly List<string> Recent = new List<string>();   // 最近战报(倒序, 最多 8 条)
        private static string _lastReport = "";
        internal static string LastDetail = "";   // 最近一场战斗的明细

        internal static string LastReport { get { return _lastReport; } }
        internal static Condition CondOf(int idx) { return (idx >= 0 && idx < Conditions.Length) ? Conditions[idx] : Conditions[0]; }

        private static State GetOrCreate(MapEvent battle, PartyBase strikerParty)
        {
            string id = BattleIdOf(battle);
            State st;
            if (_states.TryGetValue(id, out st) && st != null) return st;
            // 归档上一批已结束的战斗(超过 1 天没更新的)
            try
            {
                var stale = new List<string>();
                foreach (var kv in _states) if (kv.Value != null && Politics.Today() - kv.Value.Day >= 1) stale.Add(kv.Key);
                for (int i = 0; i < stale.Count; i++)
                {
                    State old;
                    if (_states.TryGetValue(stale[i], out old) && old != null && old.Ref != null) Archive(old.Ref);
                    else _states.Remove(stale[i]);
                }
            }
            catch { }
            st = new State { BattleId = id, Day = Politics.Today(), Ref = battle };
            var rnd = new Random((battle != null && battle.Id != null ? battle.Id.GetHashCode() : 12345) + st.Day * 31);
            st.CondIdx = rnd.Next(Conditions.Length);
            // 我方是否为攻方(玩家所在方)
            try
            {
                var pk = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                if (pk != null && battle != null)
                {
                    bool playerAttacker = false;
                    foreach (var s in battle.AttackerSide.Parties)
                        if (s != null && s.Party != null && s.Party.MapEventSide == battle.AttackerSide
                            && s.Party.LeaderHero != null && s.Party.LeaderHero.Clan != null && s.Party.LeaderHero.Clan.Kingdom == pk)
                            playerAttacker = true;
                    st.AttackerIsPlayer = playerAttacker;
                }
            }
            catch { }
            // 战斗宽度: ceil((5 + 0.5 × 基建) × 地形系数)
            float infra = 0f;
            try
            {
                var s = battle != null && battle.MapEventSettlement != null ? battle.MapEventSettlement : null;
                if (s != null) { infra = Infrastructure.BaseOf(s.StringId); st.Terrain = s.IsTown ? "城镇" : (s.IsCastle ? "城堡" : (s.IsVillage ? "村庄" : "平原")); }
            }
            catch { }
            st.Width = (float)Math.Ceiling((5f + 0.5f * infra) * (st.Terrain == "城镇" ? 1.3f : (st.Terrain == "城堡" ? 1.5f : (st.Terrain == "村庄" ? 0.9f : 1f))));
            // v27: 开战快照(分阶段结算的输入; 战斗结束时一次性算完)
            try
            {
                st.SnapA = SnapshotSide(battle != null ? battle.AttackerSide : null, true, st);
                st.SnapB = SnapshotSide(battle != null ? battle.DefenderSide : null, false, st);
            }
            catch { }
            _states[id] = st;
            EnsureEndHook();
            DLog.Force("战斗: " + st.Terrain + " / 条件=" + CondOf(st.CondIdx).Name + " / 战斗宽度=" + st.Width
                + " / 兵力 " + (st.SnapA != null ? st.SnapA.Total : 0) + " vs " + (st.SnapB != null ? st.SnapB.Total : 0));
            return st;
        }

        private static string BattleIdOf(MapEvent battle)
        {
            try { return battle != null && battle.Id != null ? battle.Id.ToString() : "?"; } catch { return "?"; }
        }

        // ---------------- 兵种克制(骑克弓 / 弓克步 / 步克骑) ----------------
        private static float CounterMult(CharacterObject striker, CharacterObject struck)
        {
            try
            {
                int a = TypeOf(striker), b = TypeOf(struck);
                if (a == b) return 1f;
                if ((a == 2 && b == 1) || (a == 1 && b == 0) || (a == 0 && b == 2)) return 1.5f;   // 克制
                return 0.85f;                                                                     // 被克制
            }
            catch { return 1f; }
        }

        // 0 步兵 / 1 弓手 / 2 骑兵 / 3 骑射
        private static int TypeOf(CharacterObject c)
        {
            if (c == null) return 0;
            if (c.IsMounted && c.IsRanged) return 3;
            if (c.IsMounted) return 2;
            if (c.IsRanged) return 1;
            return 0;
        }

        // ---------------- 伤害倍率(SimulateHit 调用) ----------------
        //   输出 = 兵种阶级(热武器) × 克制 × 组织度/士气 × 军事科技 × 战斗条件 × 战斗宽度参与率
        internal static float DamageMult(CharacterObject striker, CharacterObject struck, PartyBase sp, PartyBase tp, MapEvent battle, out string why)
        {
            why = "";
            try
            {
                var st = GetOrCreate(battle, sp);
                var cond = CondOf(st.CondIdx);
                float mult = 1f;
                var notes = new List<string>();

                // ① 兵种阶级: 高级热武器高伤害
                int tier = striker != null ? striker.Tier : 0;
                if (tier >= 4)
                {
                    float t = 1.35f + (tier - 4) * 0.35f;
                    mult *= t;
                    notes.Add("T" + tier + " ×" + t.ToString("F2"));
                }

                // ② 兵种克制
                float ct = CounterMult(striker, struck);
                if (Math.Abs(ct - 1f) > 0.01f)
                {
                    mult *= ct;
                    notes.Add((ct > 1f ? "克制" : "被克") + " ×" + ct.ToString("F2"));
                }

                // ③ 军事科技(每项 +0.5%) —— v4.241: 按"开火方所属国"算, 原来读玩家国 Done,
                //    等于给敌我双方同时加成(相对战力毫无差别)
                int techs = MilitaryTechCountOf(sp);
                if (techs > 0) { mult *= 1f + techs * 0.005f; }

                // ④ 我方军团的组织度与士气(我方部队才吃这个加成/惩罚)
                bool ours = IsOurParty(sp);
                if (ours)
                {
                    float org = DefArmyStats.OrgOf(sp);
                    float mor = DefArmyStats.MoraleOf(sp);
                    float om = 0.5f + org / 200f + mor / 400f;      // 组织度 0~100 -> 0.5~1.0; 士气再叠 0~0.25
                    mult *= om;
                    notes.Add("组织" + ((int)org) + "/士气" + ((int)mor) + " ×" + om.ToString("F2"));
                    // 补给不足: 粮秣 < 20 -> 输出 -25%
                    try
                    {
                        if (sp != null && sp.MobileParty != null && sp.MobileParty.Food < 20f)
                        {
                            bool half = false;
                            try { half = DefArmy.HasTrait(sp.MobileParty, DefArmy.TraitLogistics); } catch { }
                            float fm = half ? 0.875f : 0.75f;   // v4.243: 后勤天才缺粮惩罚减半
                            mult *= fm;
                            notes.Add("缺粮 ×" + fm.ToString("F2"));
                        }
                    }
                    catch { }
                }

                // ④b v4.202: 老兵度 / 军官统率 / 编制上限 / 兵种构成战术
                try
                {
                    var mp = ours ? (sp != null ? sp.MobileParty : null) : (tp != null ? tp.MobileParty : null);
                    bool strikerOurs = ours;
                    var tacticOwner = ours ? (sp != null ? sp.MobileParty : null) : (tp != null ? tp.MobileParty : null);
                    float outM, takeM, morM;
                    string tac = ArmyDoctrine.TacticOf(tacticOwner, out outM, out takeM, out morM);
                    if (strikerOurs) mult *= outM;
                    else mult *= takeM;                      // 我方被打时按战术减伤
                    if (mp != null)
                    {
                        float vet = ArmyDoctrine.VetOfParty(mp);
                        if (vet > 0.5f) { mult *= ArmyDoctrine.VetDamageMult(vet); notes.Add("老兵" + ((int)vet) + " ×" + ArmyDoctrine.VetDamageMult(vet).ToString("F2")); }
                        mult *= ArmyDoctrine.OfficerMult(mp);
                        if (mp != null)
                        {
                            float mm = 1f;
                            try
                            {
                                for (int li = 0; li < DefArmy.Legions.Count; li++)
                                {
                                    var lp4 = DefArmy.LegionParty(DefArmy.Legions[li]);
                                    if (lp4 != null && lp4 == mp) { mm = ArmyDoctrine.MoraleMult(ArmyDoctrine.LegionKey(DefArmy.Legions[li])); break; }
                                }
                            }
                            catch { }
                            if (mm < 0.999f) { mult *= mm; notes.Add("士气 ×" + mm.ToString("F2")); }
                        }
                        float wm = ArmyDoctrine.WoundedMult(mp);
                        if (wm < 0.999f) { mult *= wm; notes.Add("伤员 ×" + wm.ToString("F2")); }
                        if (tac != "标准阵型") notes.Add(tac);
                    }
                }
                catch { }

                // ④c v27: 装备因子(缺装乘子 0.7+0.3×满足率) + 弹药断供(火器 ×0.5) 作为乘子并入
                try
                {
                    float em = DefArmyStats.EquipMultOf(sp);
                    if (Math.Abs(em - 1f) > 0.001f) { mult *= em; notes.Add("缺装 ×" + em.ToString("F2")); }
                    if (striker != null)
                    {
                        float wd, wp, wf;
                        DefArmyStats.WeaponOfTroop(striker, out wd, out wp, out wf);
                        if (wf >= 0.5f)
                        {
                            var smp = sp != null ? sp.MobileParty : null;
                            float am = ArmyDoctrine.AmmoMultOf(smp);
                            if (am < 0.999f) { mult *= am; notes.Add("弹药断供 ×0.50"); }
                        }
                    }
                }
                catch { }

                // ⑤ 战斗条件
                if (ours)
                {
                    if (cond.Kill != 1f) { mult *= cond.Kill; notes.Add(cond.Name + "杀伤 ×" + cond.Kill.ToString("F2")); }
                }

                // ⑥ 战斗宽度参与率(兵力超过宽度 -> 后排无法输出)
                try
                {
                    int men = sp != null ? sp.NumberOfRegularMembers : 0;
                    if (men > 0 && st.Width > 0f)
                    {
                        float engage = Math.Min(1f, st.Width * 100f / men);   // 宽度按"百人单位"折算
                        if (engage < 0.999f)
                        {
                            mult *= 0.6f + 0.4f * engage;
                            notes.Add("宽度 " + ((int)(engage * 100f)) + "%");
                        }
                    }
                }
                catch { }

                // ⑦ 统计(战报用)
                try
                {
                    float baseDmg = 10f * mult;      // 伤害量级仅用于战报估算
                    if (ours) { st.OurDmg += baseDmg; st.OurHits++; st.OurTierSum += tier; }
                    else { st.TheirDmg += baseDmg; st.TheirHits++; st.TheirTierSum += tier; }
                }
                catch { }

                // ⑦b 君主亲率(实际数值由 FeudalCombatModel 应用, 此处仅登记明细)
                try
                {
                    if (sp != null && sp.LeaderHero != null && Hero.MainHero != null && sp.LeaderHero == Hero.MainHero)
                        notes.Insert(0, "君主亲率 ×1.05");
                }
                catch { }

                why = string.Join(" · ", notes);
                try
                {
                    for (int i = 0; i < notes.Count && st.Detail.Count < 6; i++)
                        if (!st.Detail.Contains(notes[i])) st.Detail.Add(notes[i]);
                }
                catch { }
                return mult;
            }
            catch { return 1f; }
        }

        private static bool IsOurParty(PartyBase p)
        {
            try
            {
                if (p == null) return false;
                var pk = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                if (pk == null) return false;
                if (p.LeaderHero != null && p.LeaderHero.Clan != null && p.LeaderHero.Clan.Kingdom == pk) return true;
                if (p.Owner != null && p.Owner.Clan != null && p.Owner.Clan.Kingdom == pk) return true;
            }
            catch { }
            return false;
        }

        internal static int MilitaryTechCount()
        {
            try
            {
                int n = 0;
                for (int i = 0; i < Research.All.Count; i++)
                {
                    var t = Research.All[i];
                    if (t == null || t.Tree != 1) continue;
                    if (Research.IsDone(t.Id)) n++;
                }
                return n;
            }
            catch { return 0; }
        }

        // v4.241: 按当事国算军事科技数(玩家国 = MilitaryTechCount 同值)
        internal static int MilitaryTechCountOf(PartyBase p)
        {
            try
            {
                Kingdom k = null;
                try { if (p != null) k = p.MapFaction as Kingdom; } catch { }
                if (k == null) return MilitaryTechCount();
                return Research.MilitaryTechCountOf(k);
            }
            catch { return 0; }
        }

        // ---------------- v27: 分阶段坐镇指挥(27.6) ----------------
        //   遭遇 -> 炮击 -> 远程齐射 -> 近战 -> 追击; 战斗结束时一次性算完(不每帧)
        internal static void StagedResolve(MapEvent battle, State st)
        {
            try
            {
                if (st == null || st.Staged || st.SnapA == null || st.SnapB == null) return;
                st.Staged = true;
                var a = st.SnapA;
                var b = st.SnapB;
                if (a.Total <= 0 || b.Total <= 0) return;

                // 27.6.3 地形与天气(乘法叠加, 结果 clamp 0.5~1.5)
                string terrain = "平原";
                try { terrain = battle.EventTerrainType.ToString(); } catch { }
                bool rain = false, mud = false, forest = false, mountain = false;
                var cond = CondOf(st.CondIdx);
                if (cond.Name == "暴风") rain = true;
                if (cond.Name == "泥泞") mud = true;
                forest = terrain.IndexOf("Forest", StringComparison.OrdinalIgnoreCase) >= 0;
                mountain = terrain.IndexOf("Mountain", StringComparison.OrdinalIgnoreCase) >= 0;
                if (mud) { a.Speed *= 0.8f; b.Speed *= 0.8f; }   // 泥泞: 机动 ×0.8
                // 既有战斗条件作为乘子并入(突袭攻方 +25% 杀伤; 堑壕守方承受 -35%; 泥泞双方承受 +15%; 暴风杀伤 -20%)
                if (cond.Name == "突袭") { a.CondKill = cond.Kill; b.CondKill = 1f; }
                else if (cond.Name == "暴风") { a.CondKill = cond.Kill; b.CondKill = cond.Kill; }
                if (cond.Name == "堑壕战") b.CasTaken = cond.CasTaken;
                else if (cond.Name == "泥泞") { a.CasTaken = cond.CasTaken; b.CasTaken = cond.CasTaken; }

                st.Stages.Add("遭遇: 地形 " + terrain + " · 天气 " + (rain ? "雨天" : (mud ? "泥泞" : "晴"))
                    + " · " + a.Name + "(" + a.Total + "人) vs " + b.Name + "(" + b.Total + "人)");
                st.Stages.Add("装备: 攻 伤害" + ((int)a.Dmg) + "/破甲" + ((int)a.Pierce) + "/甲" + ((int)a.Armor)
                    + " ×" + a.EquipMult.ToString("F2") + "(缺装) ×" + a.LegacyMult.ToString("F2") + "(既有)"
                    + " · 守 伤害" + ((int)b.Dmg) + "/破甲" + ((int)b.Pierce) + "/甲" + ((int)b.Armor)
                    + " ×" + b.EquipMult.ToString("F2") + "(缺装) ×" + b.LegacyMult.ToString("F2") + "(既有)"
                    + " · 炮 " + a.Cannons + ":" + b.Cannons);

                // ① 炮击 2 轮: 敌组织度 -8/轮
                for (int r = 1; r <= 2; r++)
                {
                    if (a.Crashed || b.Crashed) break;
                    int ka = BombardKills(a, b, rain, forest, mountain);
                    int kb = BombardKills(b, a, rain, forest, mountain);
                    ApplyLoss(b, ka);
                    ApplyLoss(a, kb);
                    a.Org = ClampF(a.Org - 8f);
                    b.Org = ClampF(b.Org - 8f);
                    MoraleOrgTick(a, kb);
                    MoraleOrgTick(b, ka);
                    if (ka + kb > 0)
                        st.Stages.Add("炮击第" + r + "/2轮: " + a.Name + " 杀 " + ka + " · " + b.Name + " 杀 " + kb);
                    if (CrashCheck(a, st) | CrashCheck(b, st)) break;
                }

                // ② 远程齐射
                int rounds = RangedRounds(a, b);
                for (int r = 1; r <= rounds; r++)
                {
                    if (a.Crashed || b.Crashed) break;
                    bool aHasFire = a.AmmoTracked && a.AmmoParty != null && a.RangedMen > 0 && a.FirearmShare > 0.001f;
                    bool bHasFire = b.AmmoTracked && b.AmmoParty != null && b.RangedMen > 0 && b.FirearmShare > 0.001f;
                    bool aBroken = aHasFire && ArmyDoctrine.AmmoBroken(a.AmmoParty);
                    bool bBroken = bHasFire && ArmyDoctrine.AmmoBroken(b.AmmoParty);
                    int ka = RangedKills(a, b, rain, forest, mountain, aBroken);
                    int kb = RangedKills(b, a, rain, forest, mountain, bBroken);
                    ApplyLoss(b, ka);
                    ApplyLoss(a, kb);
                    if (aHasFire)
                        ArmyDoctrine.ConsumeVolley(a.AmmoParty, a.RangedMen * a.FirearmShare);
                    if (bHasFire)
                        ArmyDoctrine.ConsumeVolley(b.AmmoParty, b.RangedMen * b.FirearmShare);
                    if (aBroken) a.Morale = ClampF(a.Morale - 2f);   // 断供每轮士气 -2
                    if (bBroken) b.Morale = ClampF(b.Morale - 2f);
                    MoraleOrgTick(a, kb);
                    MoraleOrgTick(b, ka);
                    string mgNote = "";
                    try
                    {
                        if (a.MGs > 0 || b.MGs > 0)
                            mgNote = " (机枪 " + a.MGs + "/" + b.MGs + " 挺 ×" + MachineGunMult(a).ToString("F2") + "/" + MachineGunMult(b).ToString("F2") + ")";
                    }
                    catch { }
                    st.Stages.Add("远程第" + r + "/" + rounds + "轮: " + a.Name + " 杀 " + ka + " · " + b.Name + " 杀 " + kb
                        + mgNote + ((aBroken || bBroken) ? " (断供方火器×0.5, 士气-2)" : ""));
                    if (CrashCheck(a, st) | CrashCheck(b, st)) break;
                }

                // ③ 近战(打到崩溃/全灭, 上限 8 轮)
                for (int r = 1; r <= 8; r++)
                {
                    if (a.Crashed || b.Crashed) break;
                    int ka = MeleeKills(a, b, mountain);
                    int kb = MeleeKills(b, a, mountain);
                    ApplyLoss(b, ka);
                    ApplyLoss(a, kb);
                    MoraleOrgTick(a, kb);
                    MoraleOrgTick(b, ka);
                    st.Stages.Add("近战第" + r + "轮: " + a.Name + " 杀 " + ka + " · " + b.Name + " 杀 " + kb);
                    if (CrashCheck(a, st) | CrashCheck(b, st)) break;
                    if (a.Total <= 0 || b.Total <= 0) break;
                }

                // ④ 追击 / 撤退 / 全歼 + 缴获
                ResolvePursuit(battle, st, a, b);
            }
            catch (Exception ex) { DLog.Force("分阶段结算异常: " + ex.Message); }
        }

        // 破甲系数(27.6.1): clamp(1 − 目标甲 × (1 − 破甲÷150) ÷ 200, 0.25, 1.0)
        private static float PierceCoef(float targetArmor, float pierce)
        {
            return ClampF2(1f - targetArmor * (1f - pierce / 150f) / 200f, 0.25f, 1f);
        }

        private static int BombardKills(SideSnap shooter, SideSnap target, bool rain, bool forest, bool mountain)
        {
            try
            {
                if (shooter.Cannons <= 0 || target.Total <= 0) return 0;
                float cd = shooter.CannonDmg > 0f ? shooter.CannonDmg : shooter.Dmg;
                float cp = shooter.CannonPen > 0f ? shooter.CannonPen : shooter.Pierce;
                float k = 150f * shooter.Cannons * cd * PierceCoef(target.Armor, cp) / 10000f;
                k *= shooter.EquipMult * shooter.LegacyMult * shooter.CondKill * target.CasTaken
                    * RangedTerrainMult(shooter, rain, forest, mountain);
                if (shooter.Sieging) k *= shooter.SiegeTrait;   // v4.241: 攻城炮兵 攻城 +50% / 掷弹兵 攻坚 +25%
                k *= shooter.StanceMult * shooter.TraitMult * shooter.BombardMult;   // v4.243: 状态/特质/炮兵专家
                int ki = (int)Math.Round(k);
                return ki < 0 ? 0 : (ki > target.Total ? target.Total : ki);
            }
            catch { return 0; }
        }

        private static int RangedKills(SideSnap shooter, SideSnap target, bool rain, bool forest, bool mountain, bool ammoBroken)
        {
            try
            {
                if (shooter.RangedMen <= 0 || target.Total <= 0) return 0;
                float dmg = shooter.Dmg;
                if (ammoBroken) dmg *= 0.5f;   // 27.5 断供
                float k = 17f * shooter.RangedMen * dmg
                    * ClampF2(shooter.Reload / 1.5f, 0.4f, 1.8f)
                    * shooter.Accuracy * shooter.Training
                    * PierceCoef(target.Armor, shooter.Pierce) / 10000f;
                k *= MachineGunMult(shooter);   // v4.241: 机枪挺数加成(27.2)
                k *= shooter.StanceMult * shooter.TraitMult;   // v4.243: 状态/特质
                k *= shooter.EquipMult * shooter.LegacyMult * shooter.CondKill * target.CasTaken
                    * RangedTerrainMult(shooter, rain, forest, mountain);
                int ki = (int)Math.Round(k);
                return ki < 0 ? 0 : (ki > target.Total ? target.Total : ki);
            }
            catch { return 0; }
        }

        // v4.241: 机枪加成(27.2 "机枪 1 挺/百人, 计入远程阶段, 防御时守方 ×1.3")
        //   每挺 +2%, 上限 +30%; 守方 ×1.3
        internal static float MachineGunMult(SideSnap sd)
        {
            try
            {
                if (sd == null || sd.MGs <= 0) return 1f;
                float b = sd.MGs * 0.02f;
                if (b > 0.30f) b = 0.30f;
                if (!sd.IsAttacker) b *= 1.3f;
                return 1f + b;
            }
            catch { return 1f; }
        }

        private static int MeleeKills(SideSnap shooter, SideSnap target, bool mountain)        {
            try
            {
                if (shooter.Total <= 0 || target.Total <= 0) return 0;
                float k = 40f * shooter.Total * shooter.Dmg * PierceCoef(target.Armor, shooter.Pierce)
                    * shooter.Training * (0.4f + 0.6f * shooter.Org / 100f) / 10000f;
                k *= shooter.MeleeTrait;   // v4.241: 掷弹兵 近战 +20% / 胸甲骑兵 冲击 +40%(按占比)
                k *= shooter.MeleeMult * shooter.StanceMult * shooter.TraitMult;   // v4.243: 步兵操典家/状态/特质
                if (shooter.Sieging) k *= shooter.SiegeTrait;
                k *= shooter.EquipMult * shooter.LegacyMult * shooter.CondKill * target.CasTaken;
                if (mountain)
                {
                    float cav = (shooter.Cav + shooter.HCav) / (float)Math.Max(1, shooter.Total);
                    k *= (1f - cav) + cav * 0.7f;   // 山地 骑兵 ×0.7
                }
                int ki = (int)Math.Round(k);
                return ki < 0 ? 0 : (ki > target.Total ? target.Total : ki);
            }
            catch { return 0; }
        }

        // 远程轮数(27.6.1): 双方有远程 clamp(2 + 射程差×1.5, 1, 5); 一方无远程 clamp(敌射程×1.2, 1, 5)
        private static int RangedRounds(SideSnap a, SideSnap b)
        {
            try
            {
                bool ar = a.RangedMen > 0, br = b.RangedMen > 0;
                float rounds;
                if (ar && br) rounds = 2f + Math.Abs(a.Range - b.Range) * 1.5f;
                else if (ar) rounds = a.Range * 1.2f;
                else if (br) rounds = b.Range * 1.2f;
                else return 0;
                return (int)Math.Round(ClampF2(rounds, 1f, 5f));
            }
            catch { return 2; }
        }

        // 27.6.3 远程地形/天气乘子(乘法叠加, clamp 0.5~1.5; 轻步兵惩罚减半)
        private static float RangedTerrainMult(SideSnap sd, bool rain, bool forest, bool mountain)
        {
            try
            {
                float m = 1f;
                float bow = 1f - sd.FirearmShare;
                if (rain) m *= sd.FirearmShare * 0.85f + bow * 0.95f;
                if (forest) m *= 0.8f;
                if (mountain) m *= sd.FirearmShare * 1f + bow * 1.1f;
                if (sd.TerrainAdapt) m = 1f - (1f - m) * 0.5f;
                return ClampF2(m, 0.5f, 1.5f);
            }
            catch { return 1f; }
        }

        // 27.6.2 每轮: 士气 -= 伤亡率×120; 组织度 -= 伤亡率×80(伤亡率按开战兵力)
        private static void MoraleOrgTick(SideSnap s, int killed)
        {
            try
            {
                if (s == null || s.InitialMen <= 0 || killed <= 0) return;
                float rate = killed / (float)s.InitialMen;
                s.Morale = ClampF(s.Morale - rate * 120f);
                s.Org = ClampF(s.Org - rate * 80f);
            }
            catch { }
        }

        // 崩溃阈值(27.6.2): 组织度 ≤15 或 士气 ≤20
        private static bool CrashCheck(SideSnap s, State st)
        {
            try
            {
                if (s == null) return false;
                if (s.Crashed) return true;
                if (s.Org <= 15f || s.Morale <= 20f || s.Total <= 0)
                {
                    s.Crashed = true;
                    if (st != null)
                        st.Stages.Add((s.IsAttacker ? "攻方" : "守方") + " " + s.Name + " 崩溃(组织度 "
                            + ((int)s.Org) + " / 士气 " + ((int)s.Morale) + ")");
                    return true;
                }
            }
            catch { }
            return false;
        }

        private static void ApplyLoss(SideSnap s, int n)
        {
            try
            {
                if (s == null || n <= 0 || s.Total <= 0) return;
                // v4.243: 堑壕老兵(承受伤亡 -15%)在唯一入口统一折算
                if (s.LossTakenMult < 0.999f) n = (int)Math.Round(n * s.LossTakenMult);
                if (n <= 0) return;
                if (n > s.Total) n = s.Total;
                float f = n / (float)s.Total;
                int ci = (int)Math.Floor(s.Inf * f);
                int cs = (int)Math.Floor(s.Shoot * f);
                int cc = (int)Math.Floor(s.Cav * f);
                int ch = (int)Math.Floor(s.HCav * f);
                int rest = n - (ci + cs + cc + ch);
                int guard = 0;
                while (rest > 0 && guard++ < 8)
                {
                    int before = rest;
                    if (rest > 0 && s.Inf - ci > 0) { ci++; rest--; }
                    if (rest > 0 && s.Shoot - cs > 0) { cs++; rest--; }
                    if (rest > 0 && s.Cav - cc > 0) { cc++; rest--; }
                    if (rest > 0 && s.HCav - ch > 0) { ch++; rest--; }
                    if (rest == before) break;
                }
                s.Inf -= ci; s.Shoot -= cs; s.Cav -= cc; s.HCav -= ch;
                s.Lost += ci + cs + cc + ch;
            }
            catch { }
        }

        // 27.6.2 撤退/全歼 + 27.7 缴获
        private static void ResolvePursuit(MapEvent battle, State st, SideSnap a, SideSnap b)
        {
            SideSnap winner = null, loser = null;
            if (a.Crashed && !b.Crashed) { winner = b; loser = a; }
            else if (b.Crashed && !a.Crashed) { winner = a; loser = b; }
            else if (a.Total <= 0 && b.Total > 0) { winner = b; loser = a; }
            else if (b.Total <= 0 && a.Total > 0) { winner = a; loser = b; }
            else
            {
                float ra = a.InitialMen > 0 ? a.Total / (float)a.InitialMen : 0f;
                float rb = b.InitialMen > 0 ? b.Total / (float)b.InitialMen : 0f;
                if (Math.Abs(ra - rb) < 0.05f) { st.Verdict = "胶着: 双方脱离接触"; return; }
                winner = ra > rb ? a : b;
                loser = ra > rb ? b : a;
            }
            if (winner == null || loser == null) return;
            if (loser.Total <= 0)
            {
                st.Verdict = "全歼: " + loser.Name + " 全军覆没";
                CaptureGear(st, winner, loser, 1f);
                return;
            }

            float cavShare = (winner.Cav + winner.HCav) / (float)Math.Max(1, winner.Total);
            float mob = (winner.Speed - loser.Speed) / Math.Max(10f, loser.Speed);
            if (mob < 0f) mob = 0f;
            if (mob > 1f) mob = 1f;
            float pursuit = Math.Min(0.8f, cavShare * 1.2f + mob * 0.5f);   // 27.6.1 追击公式

            string retreatName;
            bool hasRoute = HasRetreat(loser, battle, out retreatName);
            int lost;
            if (!hasRoute)
            {
                lost = loser.Total;
                ApplyLoss(loser, lost);
                st.Verdict = "全歼: " + loser.Name + " 无退路(含被围), 残部 " + lost + " 人被歼";
            }
            else
            {
                lost = (int)Math.Round(loser.Total * pursuit * 0.5f);   // 二次伤亡 = 追击公式×50%
                if (lost > loser.Total) lost = loser.Total;
                ApplyLoss(loser, lost);
                st.Verdict = "撤退: " + loser.Name + " 退往 " + retreatName + ", 追击伤亡 " + lost + " 人";
            }
            st.Stages.Add("追击: 骑兵比例 " + ((int)(cavShare * 100f)) + "% · 机动差 " + ((int)(mob * 100f))
                + "% · 追击系数 " + pursuit.ToString("F2") + (hasRoute ? " ×50%(有序撤退)" : " (无退路)"));
            CaptureGear(st, winner, loser, pursuit);
        }

        // 退路(27.6.2): 存在距离 ≤ 当前行军×1.5 的本国/同盟城; 被围 -> 无
        private static bool HasRetreat(SideSnap loser, MapEvent battle, out string townName)
        {
            townName = "";
            try
            {
                if (battle != null && battle.MapEventSettlement != null) return false;
                if (loser == null || loser.Faction == null) return false;
                MobileParty from = loser.LeaderParty != null ? loser.LeaderParty : loser.AmmoParty;
                if (from == null) return false;
                CampaignVec2 pos = from.Position;
                float limit = 1.5f * Math.Max(10f, loser.Speed);
                float best = float.MaxValue;
                Settlement bestS = null;
                foreach (var s in Settlement.All)
                {
                    if (s == null || !(s.IsTown || s.IsCastle)) continue;
                    var owner = s.MapFaction;
                    if (owner == null) continue;
                    bool friendly = owner == loser.Faction;
                    if (!friendly)
                    {
                        var kk = loser.Faction as Kingdom;
                        var ok = owner as Kingdom;
                        if (kk != null && ok != null) { try { friendly = kk.IsAllyWith(ok); } catch { } }
                    }
                    if (!friendly) continue;
                    float d = s.Position.Distance(pos);
                    if (d < best) { best = d; bestS = s; }
                }
                if (bestS == null || best > limit) return false;
                townName = bestS.Name != null ? bestS.Name.ToString() : bestS.StringId;
                return true;
            }
            catch { }
            return false;
        }

        // 27.7 缴获: 可用率 = clamp(0.30 + 0.06×(军工等级−装备层级) + 0.20×追击系数 − 0.10×精密系数, 0.10, 1.00)
        private static void CaptureGear(State st, SideSnap winner, SideSnap loser, float pursuit)
        {
            try
            {
                string ownerKey = Armory.NationalOwner(winner.Kingdom);
                if (string.IsNullOrEmpty(ownerKey)) return;
                int milLevel = MilitaryIndustryLevel(winner.Kingdom);
                float killedShare = loser.InitialMen > 0 ? loser.Lost / (float)loser.InitialMen : 0f;
                if (killedShare > 1f) killedShare = 1f;
                bool wiped = loser.Total <= 0;
                float hussar = winner.Hussars > 0 ? 1.2f : 1f;   // 骠骑兵缴获 +20%
            hussar *= winner.LootMult;                       // v4.243: 骑兵将领 +25%
                int lines0 = st.Stages.Count;
                for (int tier = 1; tier <= 6; tier++)
                {
                    CaptureBucket(st, ownerKey, milLevel, pursuit, hussar, tier, false, loser.TierMen[tier], wiped, killedShare);
                    CaptureBucket(st, ownerKey, milLevel, pursuit, hussar, tier, true, loser.TierFireMen[tier], wiped, killedShare);
                }
                if (st.Stages.Count > lines0)
                    st.Stages.Add("缴获入库: " + Armory.OwnerName(ownerKey)
                        + " · 军工等级 " + milLevel + (winner.Hussars > 0 ? " · 骠骑兵 +20%" : ""));
            }
            catch { }
        }

        private static void CaptureBucket(State st, string ownerKey, int milLevel, float pursuit, float hussar,
            int tier, bool firearm, int men, bool wiped, float killedShare)
        {
            try
            {
                if (men <= 0) return;
                int pool = wiped ? men : (int)Math.Floor(men * killedShare);
                if (pool <= 0) return;
                float rate = 0.30f + 0.06f * (milLevel - tier) + 0.20f * pursuit + (firearm ? -0.10f : 0f);
                rate = ClampF2(rate, 0.10f, 1.00f);
                int qty = (int)Math.Floor(pool * rate * hussar);
                if (qty <= 0) return;
                string model = Armory.PickModelFor(tier, firearm);
                // TODO(装备目录): 目录未就绪时用层级桶占位型号; PickModelFor 就绪后自动换真实型号
                if (string.IsNullOrEmpty(model)) model = "T" + tier + (firearm ? "_火器" : "_冷兵器");
                Armory.AddKey(ownerKey, model, qty);
                st.Stages.Add("缴获: " + model + " ×" + qty + " (可用率 " + ((int)(rate * 100f)) + "%)");
            }
            catch { }
        }

        // 军工等级 1~6(与科技树层级对齐): 已完成军事科技占比映射
        private static int MilitaryIndustryLevel(Kingdom k)
        {
            try
            {
                int done = 0, total = 0;
                for (int i = 0; i < Research.All.Count; i++)
                {
                    var t = Research.All[i];
                    if (t == null || t.Tree != 1) continue;
                    total++;
                    if (Research.IsDone(k, t.Id)) done++;   // v4.241: 按当事国算(原来读玩家国)
                }
                if (total <= 0) return 1;
                int lvl = 1 + (done * 5) / total;
                return lvl < 1 ? 1 : (lvl > 6 ? 6 : lvl);
            }
            catch { return 1; }
        }

        // 27.8 战力分(陆): Σ(武器数×伤害×(1+破甲÷200)) × (1+平均甲÷100) × 训练 × (0.5+0.5×组织度÷100)
        internal static float PowerScoreOf(PartyBase p)
        {
            try
            {
                if (p == null || p.MemberRoster == null) return 0f;
                float weapons = 0f, armorSum = 0f, trainSum = 0f, w = 0f;
                foreach (var e in p.MemberRoster.GetTroopRoster())
                {
                    var c = e.Character;
                    int n = e.Number;
                    if (c == null || c.IsHero || n <= 0) continue;
                    float d, pr, f;
                    DefArmyStats.WeaponOfTroop(c, out d, out pr, out f);
                    weapons += n * d * (1f + pr / 200f);
                    armorSum += DefArmyStats.ArmorOfTroop(c) * n;
                    trainSum += DefArmyStats.TrainingOfTroop(c) * n;
                    w += n;
                }
                if (w <= 0f) return 0f;
                float org = DefArmyStats.OrgOf(p);
                return weapons * (1f + (armorSum / w) / 100f) * (trainSum / w) * (0.5f + 0.5f * org / 100f);
            }
            catch { return 0f; }
        }

        internal static float PowerScoreOf(MobileParty p)
        {
            try { return p != null ? PowerScoreOf(p.Party) : 0f; }
            catch { return 0f; }
        }

        internal static float PowerScoreOf(MapEventSide side)
        {
            try
            {
                if (side == null) return 0f;
                float s = 0f;
                for (int i = 0; i < side.Parties.Count; i++)
                {
                    var mep = side.Parties[i];
                    if (mep != null && mep.Party != null) s += PowerScoreOf(mep.Party);
                }
                return s;
            }
            catch { return 0f; }
        }

        // 27.8 战力分(海): 舷炮数 × 舰炮伤害 × (1 + 装甲÷100) × 提督系数
        internal static float FleetPowerScoreOf(int guns, float damage, float armor, float admiral)
        {
            try
            {
                if (guns <= 0 || damage <= 0f) return 0f;
                if (admiral <= 0f) admiral = 0.90f;
                return guns * damage * (1f + armor / 100f) * admiral;
            }
            catch { return 0f; }
        }

        // 既有单次因子合并(27.6 要求作为乘子并入): 科技/老兵/军官/伤员/缺粮/宽度/战术
        private static float LegacyFactorOf(MobileParty mp, int men, State st)
        {
            float m = 1f;
            try { m *= 1f + MilitaryTechCount() * 0.005f; } catch { }
            try
            {
                if (mp != null)
                {
                    float vet = ArmyDoctrine.VetOfParty(mp);
                    if (vet > 0.5f) m *= ArmyDoctrine.VetDamageMult(vet);
                    m *= ArmyDoctrine.OfficerMult(mp);
                    float wm = ArmyDoctrine.WoundedMult(mp);
                    if (wm < 0.999f) m *= wm;
                    try { if (mp.Food < 20f) m *= 0.75f; } catch { }      // 缺粮
                    float o, t, mo;
                    ArmyDoctrine.TacticOf(mp, out o, out t, out mo);      // 战术输出倍率
                    m *= o;
                }
            }
            catch { }
            try
            {
                if (st != null && st.Width > 0f && men > 0)
                {
                    float engage = Math.Min(1f, st.Width * 100f / men);   // 战斗宽度参与率
                    m *= 0.6f + 0.4f * engage;
                }
            }
            catch { }
            return m;
        }

        private static SideSnap SnapshotSide(MapEventSide side, bool attacker, State st)
        {
            var s = new SideSnap();
            s.IsAttacker = attacker;
            try
            {
                if (side == null) return s;
                s.Faction = side.MapFaction;
                s.Kingdom = side.MapFaction as Kingdom;
                if (side.LeaderParty != null)
                {
                    s.LeaderParty = side.LeaderParty.MobileParty;
                    if (side.LeaderParty.Name != null) s.Name = side.LeaderParty.Name.ToString();
                }
                float dmgSum = 0f, pierceSum = 0f, armorSum = 0f, trainSum = 0f, wAll = 0f;
                float rlSum = 0f, acSum = 0f, rgSum = 0f, fireSum = 0f, wRanged = 0f;
                float morSum = 0f, orgSum = 0f, spdSum = 0f, wParty = 0f, legSum = 0f;
                for (int i = 0; i < side.Parties.Count; i++)
                {
                    var mep = side.Parties[i];
                    var p = mep != null ? mep.Party : null;
                    if (p == null) continue;
                    var roster = p.MemberRoster;
                    int partyMen = 0;
                    // v4.241: 我军编制走"军械库实际列装的甲胄+钢盔"(换甲真加防护), 其它部队仍按兵种等级估算
                    float legionArmor = -1f;
                    try
                    {
                        if (p.MobileParty != null && DefArmy.LegionOf(p.MobileParty) != null)
                            legionArmor = DefArmy.ArmorOf(p.MobileParty);
                    }
                    catch { }
                    if (roster != null)
                    {
                        foreach (var e in roster.GetTroopRoster())
                        {
                            var c = e.Character;
                            int n = e.Number;
                            if (c == null || c.IsHero || n <= 0) continue;
                            partyMen += n;
                            if (c.IsMounted && c.IsRanged) s.HCav += n;
                            else if (c.IsMounted) s.Cav += n;
                            else if (c.IsRanged) s.Shoot += n;
                            else s.Inf += n;

                            float d, pr, fire;
                            DefArmyStats.WeaponOfTroop(c, out d, out pr, out fire);
                            dmgSum += d * n;
                            pierceSum += pr * n;
                            armorSum += (legionArmor >= 0f ? legionArmor : DefArmyStats.ArmorOfTroop(c)) * n;
                            trainSum += DefArmyStats.TrainingOfTroop(c) * n;
                            wAll += n;
                            int ti = c.Tier;
                            if (ti < 0) ti = 0;
                            if (ti > 6) ti = 6;
                            if (c.IsRanged)
                            {
                                s.TierFireMen[ti] += n;
                                float r1, a1, g1;
                                DefArmyStats.RangedParamsOfTroop(c, out r1, out a1, out g1);
                                rlSum += r1 * n;
                                acSum += a1 * n;
                                rgSum += g1 * n;
                                fireSum += fire * n;
                                wRanged += n;
                            }
                            else s.TierMen[ti] += n;
                        }
                    }
                    if (partyMen > 0)
                    {
                        float pm = 60f;
                        try { pm = p.MobileParty != null ? p.MobileParty.Morale : 60f; } catch { }
                        float po = DefArmyStats.OrgOf(p);
                        float ps = 30f;
                        try { ps = p.MobileParty != null ? p.MobileParty.Speed : 30f; } catch { }
                        morSum += pm * partyMen;
                        orgSum += po * partyMen;
                        spdSum += ps * partyMen;
                        legSum += LegacyFactorOf(p.MobileParty, partyMen, st) * partyMen;
                        s.Cannons += DefArmyStats.CannonsOf(p);
                        s.Hussars += DefArmyStats.HussarCount(p);
                        // v4.241: 火炮型号(伤害/破甲) + 兵种特性(掷弹兵/胸甲骑兵/攻城炮兵, 按占比) + 围城判定
                        try
                        {
                            var mp0 = p.MobileParty;
                            if (mp0 != null)
                            {
                                float cd, cp;
                                DefArmy.CannonStatsOf(mp0, out cd, out cp);
                                if (cd > s.CannonDmg) s.CannonDmg = cd;
                                if (cp > s.CannonPen) s.CannonPen = cp;
                                float gren = DefArmy.ShareOf(mp0, Equipment.UGrenadier);
                                float cui = DefArmy.ShareOf(mp0, Equipment.UCuirassier);
                                float sie = DefArmy.ShareOf(mp0, Equipment.USiegeArtillery);
                                s.MeleeTrait *= 1f + 0.20f * gren + 0.40f * cui;
                                s.SiegeTrait *= 1f + 0.25f * gren + 0.50f * sie;
                                if (mp0.BesiegedSettlement != null) s.Sieging = true;
                                // v4.243: 军团状态 / 将军特质 / 训练度
                                var lg0 = DefArmy.LegionOf(mp0);
                                if (lg0 != null)
                                {
                                    s.TrainMult *= DefArmy.TrainMultOf(lg0);
                                    int st0 = DefArmy.StanceOf(lg0);
                                    if (st0 == DefArmy.StanceStandby) s.StanceMult *= 1.05f;
                                    else if (st0 == DefArmy.StanceRest) s.StanceMult *= 0.90f;
                                    if (DefArmy.HasTrait(lg0, DefArmy.TraitPolitical)) s.TraitMult *= 0.95f;
                                    if (DefArmy.HasTrait(lg0, DefArmy.TraitArtillery)) s.BombardMult *= 1.20f;
                                    if (DefArmy.HasTrait(lg0, DefArmy.TraitInfantry)) s.MeleeMult *= 1.10f;
                                    if (DefArmy.HasTrait(lg0, DefArmy.TraitTrench)) s.LossTakenMult *= 0.85f;
                                    if (DefArmy.HasTrait(lg0, DefArmy.TraitCavalry)) s.LootMult *= 1.25f;
                                    if (DefArmy.HasTrait(lg0, DefArmy.TraitLogistics)) s.FoodHalf = true;
                                }
                                // v4.241: 轻步兵/骠骑兵原来按原版兵种名认(我军逐人表里的兵种名对不上) -> 补逐人表口径
                                if (DefArmy.ShareOf(mp0, Equipment.ULightInf) >= 0.05f) s.TerrainAdapt = true;
                                if (DefArmy.ShareOf(mp0, Equipment.UHussar) >= 0.05f) s.Hussars += Math.Max(1, (int)(partyMen * 0.05f));
                            }
                        }
                        catch { }
                        try { s.MGs += DefArmy.MachineGunsOf(p.MobileParty); } catch { }   // v4.241: 机枪(远程阶段加成)
                        try { if (DefArmyStats.HasLightInfantry(p)) s.TerrainAdapt = true; } catch { }
                        float em = DefArmyStats.EquipMultOf(p);
                        if (em < s.EquipMult) s.EquipMult = em;
                        var mp = p.MobileParty;
                        if (mp != null && DefArmy.IsDefArmyParty(mp))
                        {
                            s.AmmoTracked = true;
                            s.AmmoParty = mp;
                            int roundsLeft = ArmyDoctrine.AmmoRoundsOf(mp);
                            if (roundsLeft < s.AmmoRounds) s.AmmoRounds = roundsLeft;
                        }
                        wParty += partyMen;
                    }
                }
                if (wAll > 0f)
                {
                    s.Dmg = dmgSum / wAll;
                    s.Pierce = pierceSum / wAll;
                    s.Armor = armorSum / wAll;
                    s.Training = trainSum / wAll * s.TrainMult;   // v4.243: 训练度系数(0.8~1.2)
                }
                if (wRanged > 0f)
                {
                    s.Reload = rlSum / wRanged;
                    s.Accuracy = acSum / wRanged;
                    s.Range = rgSum / wRanged;
                    s.FirearmShare = fireSum / wRanged;
                }
                if (wParty > 0f)
                {
                    s.Morale = morSum / wParty;
                    s.Org = orgSum / wParty;
                    s.Speed = spdSum / wParty;
                    s.LegacyMult = legSum / wParty;
                }
                s.InitialMen = s.Total;
            }
            catch { }
            return s;
        }

        private static float ClampF(float v)
        {
            if (v < 0f) return 0f;
            if (v > 100f) return 100f;
            return v;
        }

        private static float ClampF2(float v, float lo, float hi)
        {
            if (v < lo) return lo;
            if (v > hi) return hi;
            return v;
        }

        // 战斗结束钩子: 一次性分阶段结算(事件监听弱引用, 静态字段保活)
        private sealed class BattleEndHook
        {
            internal void OnEnded(MapEvent me)
            {
                try { Archive(me); } catch { }
            }
        }

        private static BattleEndHook _endHook;

        private static void EnsureEndHook()
        {
            if (_endHook != null) return;
            try
            {
                _endHook = new BattleEndHook();
                CampaignEvents.MapEventEnded.AddNonSerializedListener(_endHook, _endHook.OnEnded);
                DLog.Force("坐镇指挥: 战斗结束钩子已注册(一次性分阶段结算)");
            }
            catch (Exception ex) { DLog.Force("战斗结束钩子注册失败: " + ex.Message); }
        }

        // ---------------- 战报(V3 式: 地形/条件/宽度/双方输出/胜负判读) ----------------
        internal static string BuildReport(MapEvent battle)
        {
            try
            {
                string id = BattleIdOf(battle);
                State st;
                if (!_states.TryGetValue(id, out st) || st == null) return "";
                var cond = CondOf(st.CondIdx);
                var sb = new System.Text.StringBuilder();
                sb.Append("【战报】").Append(st.Terrain).Append(" · 战斗条件: ").Append(cond.Name).Append('\n');
                sb.Append(cond.Desc).Append('\n');
                sb.Append("战斗宽度 ").Append((int)st.Width).Append("  ·  我方输出 ").Append((int)st.OurDmg)
                  .Append("  ·  敌方输出 ").Append((int)st.TheirDmg).Append('\n');
                float ratio = st.TheirDmg > 1f ? st.OurDmg / st.TheirDmg : (st.OurDmg > 1f ? 9.99f : 1f);
                sb.Append("交换比 ").Append(ratio.ToString("F2")).Append("  ·  ");
                if (ratio >= 1.5f) sb.Append("大胜(我军火力占优)");
                else if (ratio >= 1.1f) sb.Append("小胜");
                else if (ratio > 0.9f) sb.Append("胶着");
                else if (ratio > 0.6f) sb.Append("不利");
                else sb.Append("惨败");
                sb.Append('\n');
                // v27: 分阶段结算明细(27.6) + 判定/缴获(27.6.2 / 27.7)
                if (st.Stages.Count > 0)
                {
                    sb.Append("—— 分阶段结算(遭遇→炮击→远程→近战→追击) ——\n");
                    for (int i = 0; i < st.Stages.Count; i++) sb.Append(st.Stages[i]).Append('\n');
                }
                if (!string.IsNullOrEmpty(st.Verdict)) sb.Append("判定: ").Append(st.Verdict).Append('\n');
                sb.Append("老兵度与编制: ");
                try
                {
                    for (int i = 0; i < DefArmy.Legions.Count && i < 4; i++)
                    {
                        var lg2 = DefArmy.Legions[i];
                        var lp2 = DefArmy.LegionParty(lg2);
                        if (lp2 == null) continue;
                        float outM2, takeM2, morM2;
                        string tac2 = ArmyDoctrine.TacticOf(lp2, out outM2, out takeM2, out morM2);
                        sb.Append(ArmyDoctrine.LegionKey(lg2)).Append("(老兵").Append((int)ArmyDoctrine.VetOfParty(lp2))
                          .Append("/上限").Append(ArmyDoctrine.CommandLimitOf(lp2)).Append("/").Append(tac2).Append(") ");
                    }
                }
                catch { }
                sb.Append("\n主要因素: 兵种阶级(火器) · 兵种克制(骑克弓/弓克步/步克骑) · 军事科技 ")
                  .Append(MilitaryTechCount()).Append(" 项 · 组织度与士气 · 补给 · 战斗宽度");
                return sb.ToString();
            }
            catch { return ""; }
        }

        // 战斗结束时登记(由 FeudalCombatModel 在战斗对象失活后调用)
        internal static void Archive(MapEvent battle)
        {
            try
            {
                string id = BattleIdOf(battle);
                State st;
                if (!_states.TryGetValue(id, out st) || st == null) return;
                // v4.202: 结算老兵度(按交换比判定胜负)
                try
                {
                    bool win = st.OurDmg >= st.TheirDmg;
                    ArmyDoctrine.OnBattle(win);
                    // v4.243: 战斗经验(熟练度成长) + 个人击杀累计 + 训练度因伤亡下滑
                    try
                    {
                        SideSnap mine = null;
                        try { mine = st.AttackerIsPlayer ? st.SnapA : st.SnapB; } catch { }
                        float ourLossRatio = 0f;
                        if (mine != null && mine.InitialMen > 0)
                            ourLossRatio = Math.Min(0.5f, (float)st.TheirDmg / mine.InitialMen);
                        for (int li = 0; li < DefArmy.Legions.Count; li++)
                        {
                            var lgv = DefArmy.Legions[li];
                            var lpv = DefArmy.LegionParty(lgv);
                            if (lgv == null || lpv == null || !lpv.IsActive) continue;
                            if (!PartyInBattle(battle, lpv)) continue;   // 只有参战军团吃经验
                            Soldiers.BattleExp(lpv, win ? 1.2f : 0.8f);
                            Soldiers.AddKills(lpv, (int)st.TheirDmg);
                            if (ourLossRatio > 0.01f && lgv.Train > 0f)
                                lgv.Train = Math.Max(0f, lgv.Train - ourLossRatio * 40f);
                        }
                    }
                    catch { }
                    // v4.207: 把估计伤亡按文化比例摊到 pop 上(V3)
                    try
                    {
                        ArmyDoctrine.BeginBattleLosses();
                        for (int li = 0; li < DefArmy.Legions.Count; li++)
                        {
                            var lg = DefArmy.Legions[li];
                            var lp = DefArmy.LegionParty(lg);
                            if (lg == null || lp == null) continue;
                            int men = 0; try { men = lp.Party.NumberOfRegularMembers; } catch { }
                            if (men <= 0) continue;
                            float ratio = win ? 0.05f : 0.12f;
                            ArmyDoctrine.ApplyPopLoss(lg, men * ratio, Politics.Today());
                        }
                    }
                    catch { }
                }
                catch { }
                // v27: 战斗结束时一次性跑完 遭遇->炮击->远程->近战->追击(缴获同时入库)
                try { StagedResolve(battle, st); } catch { }
                string rep = BuildReport(battle);
                try { LastDetail = st.Detail.Count > 0 ? string.Join(" · ", st.Detail.ToArray()) : ""; } catch { }
                if (!string.IsNullOrEmpty(rep))
                {
                    _lastReport = rep;
                    Recent.Insert(0, rep);
                    while (Recent.Count > 8) Recent.RemoveAt(Recent.Count - 1);
                    DLog.Force(rep.Replace('\n', ' '));
                }
                _states.Remove(id);
            }
            catch { }
        }

        // v4.243: 该部队是否参加了这场战斗(只有参战军团吃战斗经验)
        private static bool PartyInBattle(MapEvent battle, MobileParty p)
        {
            try
            {
                if (battle == null || p == null || p.Party == null) return false;
                var atk = battle.AttackerSide;
                if (atk != null && atk.Parties != null)
                    foreach (var s in atk.Parties) if (s != null && s.Party == p.Party) return true;
                var def = battle.DefenderSide;
                if (def != null && def.Parties != null)
                    foreach (var s in def.Parties) if (s != null && s.Party == p.Party) return true;
            }
            catch { }
            return false;
        }

        // 战报界面: 列出最近 8 场战斗的战报(点开看详情)
        internal static void ShowUi()
        {
            try
            {
                var opts = new List<InquiryElement>();
                for (int i = 0; i < Recent.Count; i++)
                {
                    string r = Recent[i];
                    string first = r;
                    int nl = first.IndexOf('\n');
                    if (nl > 0) first = first.Substring(0, nl);
                    opts.Add(new InquiryElement(i.ToString(), first, null, true, r));
                }
                if (opts.Count == 0) opts.Add(new InquiryElement("none", "暂无战报", null, false, "还没有坐镇指挥过的战斗"));
                PanelInputGuard.ShowPopup(new MultiSelectionInquiryData(
                    "战报",
                    "最近 " + Recent.Count + " 场战斗(坐镇指挥)\n当前模型: 战斗宽度 = ceil((5 + 0.5 × 基建) × 地形系数) · 兵种克制 骑>弓>步>骑 · 高级火器高伤害",
                    opts, true, 1, 1, "知道了", "关闭", null, null, null, false));
            }
            catch (Exception ex) { DLog.Force("战报界面失败: " + ex.Message); }
        }

        internal static string Save()
        {
            try
            {
                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < Recent.Count; i++) sb.Append(Recent[i].Replace('\n', (char)1)).Append((char)2);
                return sb.ToString();
            }
            catch { return ""; }
        }

        internal static void Load(string data)
        {
            try
            {
                Recent.Clear();
                LastDetail = "";
                if (string.IsNullOrEmpty(data)) return;
                foreach (var seg in data.Split((char)2))
                {
                    if (string.IsNullOrEmpty(seg)) continue;
                    Recent.Add(seg.Replace((char)1, '\n'));
                    if (Recent.Count >= 8) break;
                }
                if (Recent.Count > 0) _lastReport = Recent[0];
            }
            catch { }
        }
    }
}
