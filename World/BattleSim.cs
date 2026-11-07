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
            internal bool OurSideWon;      // v4.255(P0-1): 本场我方(玩家国)一方的胜负(按分阶段真实减员判定)
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
            // v4.255(P0-1): 本方的参战部队(开战快照, 战斗结束后 MapEventSide.Parties 可能已被清空)
            internal List<MobileParty> Parties = new List<MobileParty>();
            internal bool IsOurs;                                // 本方是否含我方(玩家国)部队

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
                // v4.255(P0-10): 先取 key 快照再删 —— Archive 会改 _states, 若在遍历字典时改会抛
                //   InvalidOperationException(被 catch 吞掉 -> 每次只清一条, 陈旧状态越积越多)
                var stale = new List<string>();
                foreach (var key in new List<string>(_states.Keys))
                {
                    State sv;
                    if (_states.TryGetValue(key, out sv) && sv != null && Politics.Today() - sv.Day >= 1) stale.Add(key);
                }
                for (int i = 0; i < stale.Count; i++)
                {
                    try
                    {
                        State old;
                        if (_states.TryGetValue(stale[i], out old) && old != null && old.Ref != null) Archive(old.Ref);
                        else _states.Remove(stale[i]);
                    }
                    catch { }
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
            // v4.255(P0-12): 泥泞的"战斗宽度 -30%"落实(原来只写在战报文案里, 没进公式)
            try
            {
                float cw = CondOf(st.CondIdx).Width;
                if (cw < 0.999f) st.Width = Math.Max(1f, (float)Math.Ceiling(st.Width * cw));
            }
            catch { }
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
                //   v4.255(P0-8): 按"这一击是谁打的"判定 —— 原来只要 ours 就乘 cond.Kill,
                //   于是"突袭"(攻方 +25%)被我方当守方时也白吃, "暴风"(双方 -20%)又只罚我方
                if (cond.Kill != 1f)
                {
                    if (cond.Name == "突袭")
                    {
                        bool strikerIsAttacker = false;
                        try { strikerIsAttacker = battle != null && sp != null && sp.MapEventSide != null && ReferenceEquals(sp.MapEventSide, battle.AttackerSide); } catch { }
                        if (strikerIsAttacker)
                        {
                            mult *= cond.Kill;
                            notes.Add("突袭(攻方)杀伤 ×" + cond.Kill.ToString("F2"));
                        }
                    }
                    else
                    {
                        // 暴风等"双方同减"的条件: 敌我同口径
                        mult *= cond.Kill;
                        notes.Add(cond.Name + "杀伤 ×" + cond.Kill.ToString("F2"));
                    }
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
                //   v4.255(P0-1): 只统计"有我方参与"的攻防 —— 原来 AI 互殴时双方伤害全进 TheirDmg,
                //   而 OurDmg 恒 0, 战报与胜负判定一律变成"玩家惨败"
                try
                {
                    float baseDmg = 10f * mult;      // 伤害量级仅用于战报估算
                    if (ours) { st.OurDmg += baseDmg; st.OurHits++; st.OurTierSum += tier; }
                    else if (IsOurParty(tp)) { st.TheirDmg += baseDmg; st.TheirHits++; st.TheirTierSum += tier; }
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

        // v4.255(P0-1): 玩家自建军团(fia_def_)同样算"我方" —— 万一家族/王国归属判定异常,
        //   也不能把我方参战的战斗当成 AI 互殴而漏结算
        private static bool IsLegionParty(MobileParty mp)
        {
            try { return mp != null && DefArmy.LegionOf(mp) != null; } catch { return false; }
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
                // v4.255(P0-12): 补齐条件效果 —— 堑壕战守方"士气伤害 +30%" / 暴风双方 +20%
                //   (原来 Cond.MoraleDmg 全程未用, 战报却写着这两个数字)
                float morA = 1f, morB = 1f;
                if (cond.Name == "堑壕战") morB = cond.MoraleDmg;
                else if (cond.Name == "暴风") { morA = cond.MoraleDmg; morB = cond.MoraleDmg; }

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
                    // v4.255(P0-5②): 组织度惩罚以"真实炮数"为前提 —— 原来 0 门炮也每轮 -8(两轮共 -16),
                    //   直接把组织度打到 ≤15 触发崩溃判定
                    if (a.Cannons > 0) b.Org = ClampF(b.Org - 8f);
                    if (b.Cannons > 0) a.Org = ClampF(a.Org - 8f);
                    MoraleOrgTick(a, kb, morA);
                    MoraleOrgTick(b, ka, morB);
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
                    MoraleOrgTick(a, kb, morA);
                    MoraleOrgTick(b, ka, morB);
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
                    MoraleOrgTick(a, kb, morA);
                    MoraleOrgTick(b, ka, morB);
                    st.Stages.Add("近战第" + r + "轮: " + a.Name + " 杀 " + ka + " · " + b.Name + " 杀 " + kb);
                    if (CrashCheck(a, st) | CrashCheck(b, st)) break;
                    if (a.Total <= 0 || b.Total <= 0) break;
                }

                // ④ 追击 / 撤退 / 全歼 + 缴获
                ResolvePursuit(battle, st, a, b);

                // v4.255(P0-5④): 非军团(AI)部队的组织度写回持久值, 与军团用同一口径
                PersistSideOrg(a);
                PersistSideOrg(b);
            }
            catch (Exception ex) { DLog.Force("分阶段结算异常: " + ex.Message); }
        }

        // 破甲系数(27.6.1): clamp(1 − 目标甲 × (1 − 破甲÷150) ÷ 200, 0.25, 1.0)
        private static float PierceCoef(float targetArmor, float pierce)
        {
            return ClampF2(1f - targetArmor * (1f - pierce / 150f) / 200f, 0.25f, 1f);
        }

        // v4.255(P0-5④): 该方(非军团部队, 通常是 AI)战斗后的组织度写回持久值;
        //   我方军团的持久组织度由 ArmyDoctrine.OnBattle 统一结算(避免重复扣)
        private static void PersistSideOrg(SideSnap s)
        {
            try
            {
                if (s == null || s.Parties == null || s.Parties.Count == 0) return;
                if (s.Org >= 99.5f) return;                  // 没掉组织度就不落记录
                for (int i = 0; i < s.Parties.Count; i++)
                {
                    var mp = s.Parties[i];
                    if (mp == null) continue;
                    ArmyDoctrine.PersistBattleOrg(mp, s.Org);
                }
            }
            catch { }
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
        //   v4.255(P0-12): 增加 moraleMult —— 堑壕战守方 +30% / 暴风双方 +20%(条件 Cond.MoraleDmg)
        private static void MoraleOrgTick(SideSnap s, int killed, float moraleMult = 1f)
        {
            try
            {
                if (s == null || s.InitialMen <= 0 || killed <= 0) return;
                float rate = killed / (float)s.InitialMen;
                if (moraleMult < 0.1f) moraleMult = 0.1f;
                s.Morale = ClampF(s.Morale - rate * 120f * moraleMult);
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

        // 退路(27.6.2): 存在 3 天行程内的本国/同盟城; 真被围 -> 无
        //   v4.255(P0-9): 原 limit = 1.5×速度 ≈ 15 地图单位(远小于城镇间距), 且"发生在定居点"就直接判无退路,
        //   于是 196 场里 185 场判"全歼"、0 场"胶着"
        private static bool HasRetreat(SideSnap loser, MapEvent battle, out string townName)
        {
            townName = "";
            try
            {
                if (loser == null || loser.Faction == null) return false;
                MobileParty from = loser.LeaderParty != null ? loser.LeaderParty : loser.AmmoParty;
                if (from == null) return false;
                // 发生在定居点: 只有真被围(BesiegedSettlement != null)才算无退路
                if (battle != null && battle.MapEventSettlement != null)
                {
                    bool besieged = false;
                    try { besieged = from.BesiegedSettlement != null; } catch { }
                    if (besieged) return false;
                }
                CampaignVec2 pos = from.Position;
                // v4.255: 按行军尺度取 3 天行程(原版 Speed 为"地图单位/小时"口径)
                float limit = Math.Max(60f, Math.Max(1f, loser.Speed) * 72f);
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
                // v4.255(P0-3③): 目录里没有对应型号 -> 直接放弃这件缴获。
                //   原来写占位 id("T2_火器"/"T2_冷兵器"), 这类型号不存在、无法列装,
                //   却照样计入 Armory.TotalCount -> 抬高军需率(日志出现 31 次)
                if (string.IsNullOrEmpty(model)) return;
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
                // v4.255(P0-1): 记录本方参战部队与"是否含我方部队"
                //   (战斗结束时 MapEventSide.Parties 可能已被清空, 只能用开战快照做参战过滤)
                try
                {
                    for (int pi = 0; pi < side.Parties.Count; pi++)
                    {
                        var mepP = side.Parties[pi];
                        var pbP = mepP != null ? mepP.Party : null;
                        if (pbP == null) continue;
                        if (pbP.MobileParty != null) s.Parties.Add(pbP.MobileParty);
                        if (!s.IsOurs && (IsOurParty(pbP) || IsLegionParty(pbP.MobileParty))) s.IsOurs = true;
                    }
                }
                catch { }
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
                // v4.255(P0-7): 军团状态/训练度/特质改为"按兵力加权平均 + 每种特质只生效一次"的累加器
                float trainSum2 = 0f, trainW = 0f, stanceSum = 0f, meleeSum = 0f, siegeSum = 0f, traitW = 0f;
                bool tPol = false, tArt = false, tInf = false, tTre = false, tCav = false, tLog = false;
                // v4.267: 本场快照内共用一份"AI 部队兵种构成"折算缓存 —— AI 不再建逐人表(存档膨胀),
                //   但一次快照里每支部队会被查 5~6 次占比, 用 call-local 缓存摊掉重复折算(快照结束即释放)
                var aiUnitCache = new Dictionary<string, Dictionary<int, int>>(StringComparer.Ordinal);
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
                                float gren = DefArmy.ShareOfCached(mp0, Equipment.UGrenadier, aiUnitCache);
                                float cui = DefArmy.ShareOfCached(mp0, Equipment.UCuirassier, aiUnitCache);
                                float sie = DefArmy.ShareOfCached(mp0, Equipment.USiegeArtillery, aiUnitCache);
                                // v4.255(P0-7): 改为按兵力加权平均(下面统一结算), 不再逐军团连乘 ——
                                //   原来 10 支训练度 100 的军团 = 1.2^10 = 6.19 倍直接乘进杀伤公式,
                                //   拆成 N 支小军团就能指数放大输出
                                s.MeleeTrait = 1f; s.SiegeTrait = 1f;   // 占位(下面按兵力加权统一赋值)
                                meleeSum += (1f + 0.20f * gren + 0.40f * cui) * partyMen;
                                siegeSum += (1f + 0.25f * gren + 0.50f * sie) * partyMen;
                                traitW += partyMen;
                                if (mp0.BesiegedSettlement != null) s.Sieging = true;
                                // v4.243: 军团状态 / 将军特质 / 训练度
                                var lg0 = DefArmy.LegionOf(mp0);
                                if (lg0 != null)
                                {
                                    trainSum2 += DefArmy.TrainMultOf(lg0) * partyMen;
                                    trainW += partyMen;
                                    int st0 = DefArmy.StanceOf(lg0);
                                    stanceSum += (st0 == DefArmy.StanceStandby ? 1.05f : (st0 == DefArmy.StanceRest ? 0.90f : 1f)) * partyMen;
                                    // 每种特质全局只生效一次(原来每个军团都乘一遍)
                                    if (DefArmy.HasTrait(lg0, DefArmy.TraitPolitical)) tPol = true;
                                    if (DefArmy.HasTrait(lg0, DefArmy.TraitArtillery)) tArt = true;
                                    if (DefArmy.HasTrait(lg0, DefArmy.TraitInfantry)) tInf = true;
                                    if (DefArmy.HasTrait(lg0, DefArmy.TraitTrench)) tTre = true;
                                    if (DefArmy.HasTrait(lg0, DefArmy.TraitCavalry)) tCav = true;
                                    if (DefArmy.HasTrait(lg0, DefArmy.TraitLogistics)) tLog = true;
                                }
                                // v4.241: 轻步兵/骠骑兵原来按原版兵种名认(我军逐人表里的兵种名对不上) -> 补逐人表口径
                                if (DefArmy.ShareOfCached(mp0, Equipment.ULightInf, aiUnitCache) >= 0.05f) s.TerrainAdapt = true;
                                if (DefArmy.ShareOfCached(mp0, Equipment.UHussar, aiUnitCache) >= 0.05f) s.Hussars += Math.Max(1, (int)(partyMen * 0.05f));
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
                    // v4.255(P0-7): 训练度系数按兵力加权平均并 clamp(0.8~1.2), 不再逐军团连乘
                    if (trainW > 0f) s.TrainMult = ClampF2(trainSum2 / trainW, 0.8f, 1.2f);
                    s.Training = trainSum / wAll * s.TrainMult;   // v4.243: 训练度系数(0.8~1.2)
                }
                // v4.255(P0-7): 状态/兵种特性按兵力加权平均; 将军特质每种全局只生效一次
                if (traitW > 0f)
                {
                    s.MeleeTrait = ClampF2(meleeSum / traitW, 1f, 1.6f);
                    s.SiegeTrait = ClampF2(siegeSum / traitW, 1f, 1.75f);
                }
                if (trainW > 0f) s.StanceMult = ClampF2(stanceSum / trainW, 0.90f, 1.05f);
                if (tPol) s.TraitMult *= 0.95f;
                if (tArt) s.BombardMult *= 1.20f;
                if (tInf) s.MeleeMult *= 1.10f;
                if (tTre) s.LossTakenMult *= 0.85f;
                if (tCav) s.LootMult *= 1.25f;
                if (tLog) s.FoodHalf = true;
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
                // v4.267: 战斗归档是关键路径, 原来静默 -> 结算失败时战报/缴获/经验会凭空消失且无从查起
                try { Archive(me); }
                catch (Exception ex) { DLog.Force("战斗归档异常(结束钩子): " + ex.Message); }
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
                // v4.255(P0-11): 交换比改用"真实损失比"(SideSnap.Lost) —— 原来用伤害点数,
                //   与分阶段判定(全歼/撤退)口径不同, 战报会出现"惨败"与"全歼敌方"同行
                float ratio = -1f;
                SideSnap rMine = null, rFoe = null;
                if (st.SnapA != null && st.SnapA.IsOurs) { rMine = st.SnapA; rFoe = st.SnapB; }
                else if (st.SnapB != null && st.SnapB.IsOurs) { rMine = st.SnapB; rFoe = st.SnapA; }
                if (rMine != null && rFoe != null && rMine.InitialMen > 0 && rFoe.InitialMen > 0
                    && (rMine.Lost > 0 || rFoe.Lost > 0))
                {
                    float ourLost = rMine.Lost / (float)rMine.InitialMen;
                    float foeLost = rFoe.Lost / (float)rFoe.InitialMen;
                    ratio = ourLost > 0.0001f ? foeLost / ourLost : (foeLost > 0.0001f ? 9.99f : 1f);
                }
                if (ratio < 0f) ratio = st.TheirDmg > 1f ? st.OurDmg / st.TheirDmg : (st.OurDmg > 1f ? 9.99f : 1f);
                sb.Append("交换比 ").Append(ratio.ToString("F2")).Append("  ·  ");
                if (ratio >= 1.5f) sb.Append("大胜(我军火力占优)");
                else if (ratio >= 1.1f) sb.Append("小胜");
                else if (ratio > 0.9f) sb.Append("胶着");
                else if (ratio > 0.6f) sb.Append("不利");
                else sb.Append("惨败");
                sb.Append('\n');
                // v4.255(P0-1): 我方未参战时不结算军团(避免战报被误读成"玩家战败")
                if (OurSideInBattle(battle, st))
                    sb.Append("本方判定: ").Append(st.OurSideWon ? "胜" : "败").Append(" (老兵 ")
                      .Append(st.OurSideWon ? "+6" : "-4").Append(" / 组织度 -25)\n");
                else
                    sb.Append("(本场我方未参战: 不结算军团老兵/组织度/伤员)\n");
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

                // v4.255(P0-7): 玩家"亲自指挥"的战斗由原版战场结算, mod 侧坐镇结算整体跳过
                //   (原来两边同时结算: 一边打真实战场, 一边又拿参战前残留的 OurDmg/TheirDmg 判胜负/发缴获)
                if (IsCommandedBattle(battle))
                {
                    LogShortReport("玩家亲自指挥 · " + st.Terrain + " · mod 侧坐镇结算已跳过(采用原版战场结果)");
                    ClearCommanded(battle);   // 打标由本处消费后清除(不依赖 MapEventEnded 的监听顺序)
                    _states.Remove(id);
                    return;
                }

                // v4.255(P0-3①): 同一对部队在同一游戏小时内只结算一次
                //   (原来同一对部队几秒内被反复判"全歼", 每次各入库一次缴获)
                string duelKey = DuelKeyOf(st);
                if (!string.IsNullOrEmpty(duelKey))
                {
                    float hour = GameHourNow();
                    float last;
                    if (_duelSettled.TryGetValue(duelKey, out last) && hour - last < DuelDedupeHours)
                    {
                        LogShortReport(st.Terrain + " · 与同一对手的重复遭遇(同一游戏小时内已结算), 未重复结算/未重复缴获");
                        _states.Remove(id);
                        return;
                    }
                    _duelSettled[duelKey] = hour;
                    if (_duelSettled.Count > 256) PruneDuels(hour);
                }

                // v27: 战斗结束时一次性跑完 遭遇->炮击->远程->近战->追击(缴获同时入库)
                //   v4.255: 提前到老兵/经验结算之前 —— 胜负与个人击杀都要用分阶段算出的真实减员
                //   v4.267: 关键路径, 失败不再静默(否则战斗会"没有战报也没有缴获"却毫无线索)
                try { StagedResolve(battle, st); }
                catch (Exception ex) { DLog.Force("战斗分阶段结算异常: " + ex.Message); }

                // v4.255(P0-1): 只有"我方(玩家国)参战"的战斗才结算军团; 纯 AI 互殴直接跳过
                bool ours = OurSideInBattle(battle, st);
                bool win = ours && OurSideWon(st);
                st.OurSideWon = win;
                if (ours)
                {
                    // v4.202: 结算老兵度; v4.255(P0-1): 只对实际参战的部队
                    var participants = ParticipantsOf(battle, st);
                    try { ArmyDoctrine.OnBattle(win, participants); } catch { }
                    // v4.243: 战斗经验(熟练度成长) + 个人击杀累计 + 训练度因伤亡下滑
                    try
                    {
                        SideSnap mine = null, theirs = null;
                        if (st.SnapA != null && st.SnapA.IsOurs) { mine = st.SnapA; theirs = st.SnapB; }
                        else if (st.SnapB != null && st.SnapB.IsOurs) { mine = st.SnapB; theirs = st.SnapA; }
                        // v4.255(P0-4): 个人击杀用"我方造成的击杀"(= 敌方真实减员);
                        //   原来传 st.TheirDmg(敌方打出的伤害), 口径完全反了
                        int ourKills = theirs != null ? theirs.Lost : 0;
                        int ourMenTotal = mine != null ? mine.InitialMen : 0;
                        float ourLossRatio = 0f;
                        if (mine != null && mine.InitialMen > 0)
                            ourLossRatio = Math.Min(0.5f, mine.Lost / (float)mine.InitialMen);
                        for (int li = 0; li < DefArmy.Legions.Count; li++)
                        {
                            var lgv = DefArmy.Legions[li];
                            var lpv = DefArmy.LegionParty(lgv);
                            if (lgv == null || lpv == null || !lpv.IsActive) continue;
                            if (!PartyInBattle(battle, st, lpv)) continue;   // 只有参战军团吃经验
                            Soldiers.BattleExp(lpv, win ? 1.2f : 0.8f);
                            if (ourKills > 0)
                            {
                                int men = 0; try { men = lpv.Party.NumberOfRegularMembers; } catch { }
                                int k2 = ourMenTotal > 0 ? (int)Math.Round(ourKills * (men / (float)ourMenTotal)) : 0;
                                if (k2 < 1) k2 = 1;
                                if (k2 > ourKills) k2 = ourKills;
                                Soldiers.AddKills(lpv, k2);
                            }
                            if (ourLossRatio > 0.01f && lgv.Train > 0f)
                                lgv.Train = Math.Max(0f, lgv.Train - ourLossRatio * 40f);
                        }
                    }
                    catch { }
                    // v4.255(P0-2): 人口扣减只在真实减员处发生一次(DefArmy.RemoveSoldiersPop);
                    //   这里原来又对"全部军团"按当前兵力 5%/12% 调用 ApplyPopLoss —— 重复扣减且与参战无关
                }
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
            // v4.267: 战斗归档整体失败原来完全静默 -> 必须进正式日志(哪怕只留下"哪场战斗炸了")
            catch (Exception ex) { DLog.Force("战斗归档异常: " + ex.Message); }
        }

        // ==================== v4.255: P0-1/P0-3/P0-7 辅助 ====================
        private static MapEvent _commandedEvent;   // P0-7: 玩家亲自指挥中的战斗
        // P0-3①: 同一对部队的重复遭遇去重(键 = 双方主队 id 对, 值 = 上次结算时的游戏小时)
        private static readonly Dictionary<string, float> _duelSettled = new Dictionary<string, float>(StringComparer.Ordinal);
        private const float DuelDedupeHours = 1f;   // 同一游戏小时内同双方只结算一次

        // v4.255(P0-7): 由 BattleCommand 在"亲自指挥"开始时打标
        internal static void MarkCommanded(MapEvent me)
        {
            try { _commandedEvent = me; } catch { }
        }

        // v4.255(P0-7): 指挥结束/读档时清除打标
        internal static void ClearCommanded(MapEvent me)
        {
            try { if (me == null || IsCommandedBattle(me)) _commandedEvent = null; } catch { }
        }

        private static bool IsCommandedBattle(MapEvent me)
        {
            try
            {
                if (me == null) return false;
                // ① 显式打标(玩家走 mod 的"亲自指挥"入口)
                if (_commandedEvent != null)
                {
                    if (ReferenceEquals(me, _commandedEvent)) { FixProbe.CmdByFlag++; return true; }
                    if (me.Id != null && _commandedEvent.Id != null && me.Id.Equals(_commandedEvent.Id)) { FixProbe.CmdByFlag++; return true; }
                }
                // ② v4.290 兜底判据: 玩家亲自挂着这场真实战场, 也算"已由真实战场结算过"。
                //   背景: 打标只有一处(Battle\BattleCommand.cs:259, 即地图右键"亲自指挥"),
                //   而从原版遭遇菜单点"攻击"、加入进行中的战斗/围城、藏身处、村庄战斗、
                //   以及内嵌 RTSCamera 的 "(RTS Camera)Command the battle."(主英雄受伤时出现)
                //   全都不打标 —— 这些入口进的都是真实战场, 事件结束时 Archive 会再跑一遍
                //   StagedResolve, 造成缴获入库/个人击杀/老兵度·经验·组织度/战报判定重复。
                //   判据: 主队仍挂在这场事件的某一侧(PlayerEncounter.FinalizeBattle 是先
                //   FinalizeEvent() 再清 _mapEvent, 所以 MapEventEnded 触发时玩家还在),
                //   且它不是"送兵坐镇"模拟器 —— IsPlayerSimulation 只在 BattleSimulation 构造
                //   坐镇时置真, 那条路本来就该由 mod 结算, 不能被误杀。
                if (!me.IsPlayerSimulation)
                {
                    var mp = PartyBase.MainParty;
                    if (mp != null && ReferenceEquals(mp.MapEvent, me)) { FixProbe.CmdByPresence++; return true; }
                }
                return false;
            }
            catch { return false; }
        }

        private static float GameHourNow()
        {
            try { return (float)CampaignTime.Now.ToHours; } catch { return 0f; }
        }

        // P0-3①: 双方主队 id 对(与 MapEvent 实例无关, 因为同一对部队会被拆成多个 MapEvent)
        private static string DuelKeyOf(State st)
        {
            try
            {
                if (st == null) return null;
                string a = SideKeyOf(st.SnapA), b = SideKeyOf(st.SnapB);
                if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return null;
                return string.CompareOrdinal(a, b) <= 0 ? a + "|" + b : b + "|" + a;
            }
            catch { return null; }
        }

        private static string SideKeyOf(SideSnap s)
        {
            try
            {
                if (s == null) return null;
                if (s.LeaderParty != null && !string.IsNullOrEmpty(s.LeaderParty.StringId)) return s.LeaderParty.StringId;
                if (s.AmmoParty != null && !string.IsNullOrEmpty(s.AmmoParty.StringId)) return s.AmmoParty.StringId;
                if (s.Parties != null && s.Parties.Count > 0 && s.Parties[0] != null) return s.Parties[0].StringId;
            }
            catch { }
            return null;
        }

        private static void PruneDuels(float hour)
        {
            try
            {
                var stale = new List<string>();
                foreach (var kv in _duelSettled) if (hour - kv.Value > 24f) stale.Add(kv.Key);
                for (int i = 0; i < stale.Count; i++) _duelSettled.Remove(stale[i]);
            }
            catch { }
        }

        private static void LogShortReport(string text)
        {
            try
            {
                string rep = "【战报】" + text;
                _lastReport = rep;
                Recent.Insert(0, rep);
                while (Recent.Count > 8) Recent.RemoveAt(Recent.Count - 1);
                DLog.Force(rep);
            }
            catch { }
        }

        // v4.255(P0-1): 本场是否有我方(玩家国)部队参战
        //   开战快照为准, 补上"战斗结束时仍在场的双方"(增援部队可能没进快照)
        private static bool OurSideInBattle(MapEvent battle, State st)
        {
            try
            {
                if (st != null)
                {
                    if (st.SnapA != null && st.SnapA.IsOurs) return true;
                    if (st.SnapB != null && st.SnapB.IsOurs) return true;
                    // 快照缺失时兜底: 有"我方输出/我方承受"的统计即说明我方参战
                    if (st.SnapA == null && st.SnapB == null && (st.OurDmg > 0f || st.TheirDmg > 0f)) return true;
                }
                if (battle != null)
                {
                    if (LiveSideHasOurParty(battle.AttackerSide)) return true;
                    if (LiveSideHasOurParty(battle.DefenderSide)) return true;
                }
            }
            catch { }
            return false;
        }

        private static bool LiveSideHasOurParty(MapEventSide side)
        {
            try
            {
                if (side == null || side.Parties == null) return false;
                for (int i = 0; i < side.Parties.Count; i++)
                {
                    var mep = side.Parties[i];
                    var pb = mep != null ? mep.Party : null;
                    if (pb == null) continue;
                    if (IsOurParty(pb) || IsLegionParty(pb.MobileParty)) return true;
                }
            }
            catch { }
            return false;
        }

        // v4.255(P0-1): 胜负按"这场战斗里我们这一方"判定(不再拿全局 OurDmg/TheirDmg 硬比)
        private static bool OurSideWon(State st)
        {
            try
            {
                if (st == null) return false;
                SideSnap mine = null, foe = null;
                if (st.SnapA != null && st.SnapA.IsOurs) { mine = st.SnapA; foe = st.SnapB; }
                else if (st.SnapB != null && st.SnapB.IsOurs) { mine = st.SnapB; foe = st.SnapA; }
                if (mine != null && foe != null)
                {
                    if (mine.Crashed && !foe.Crashed) return false;
                    if (foe.Crashed && !mine.Crashed) return true;
                    if (mine.InitialMen > 0 && foe.InitialMen > 0)
                    {
                        float rm = mine.Total / (float)mine.InitialMen;
                        float rf = foe.Total / (float)foe.InitialMen;
                        if (Math.Abs(rm - rf) > 0.02f) return rm > rf;
                    }
                }
                return st.OurDmg >= st.TheirDmg;   // 兜底: 两个统计都只含"我方相关"的攻防
            }
            catch { return false; }
        }

        // v4.255(P0-1): 本场参战部队(开战快照 + 战斗结束时仍在场的双方)
        private static List<MobileParty> ParticipantsOf(MapEvent battle, State st)
        {
            var list = new List<MobileParty>();
            try
            {
                if (st != null && st.SnapA != null && st.SnapA.Parties != null) list.AddRange(st.SnapA.Parties);
                if (st != null && st.SnapB != null && st.SnapB.Parties != null) list.AddRange(st.SnapB.Parties);
                if (battle != null)
                {
                    AddLiveParties(battle.AttackerSide, list);
                    AddLiveParties(battle.DefenderSide, list);
                }
            }
            catch { }
            return list;
        }

        private static void AddLiveParties(MapEventSide side, List<MobileParty> list)
        {
            try
            {
                if (side == null || side.Parties == null || list == null) return;
                for (int i = 0; i < side.Parties.Count; i++)
                {
                    var mep = side.Parties[i];
                    var mp = mep != null && mep.Party != null ? mep.Party.MobileParty : null;
                    if (mp != null && !list.Contains(mp)) list.Add(mp);
                }
            }
            catch { }
        }

        // v4.255(P0-1): 该部队是否参加了这场战斗(只有参战军团吃战斗经验)
        private static bool PartyInBattle(MapEvent battle, State st, MobileParty p)
        {
            try
            {
                if (p == null) return false;
                if (SideHasParty(st != null ? st.SnapA : null, p)) return true;
                if (SideHasParty(st != null ? st.SnapB : null, p)) return true;
                if (battle != null)
                {
                    if (LiveSideHasParty(battle.AttackerSide, p)) return true;
                    if (LiveSideHasParty(battle.DefenderSide, p)) return true;
                }
            }
            catch { }
            return false;
        }

        private static bool LiveSideHasParty(MapEventSide side, MobileParty p)
        {
            try
            {
                if (side == null || side.Parties == null || p == null) return false;
                for (int i = 0; i < side.Parties.Count; i++)
                {
                    var mep = side.Parties[i];
                    var pb = mep != null ? mep.Party : null;
                    if (pb == null) continue;
                    if (pb.MobileParty == p) return true;
                    if (!string.IsNullOrEmpty(pb.MobileParty != null ? pb.MobileParty.StringId : null)
                        && pb.MobileParty.StringId == p.StringId) return true;
                }
            }
            catch { }
            return false;
        }

        private static bool SideHasParty(SideSnap s, MobileParty p)
        {
            try
            {
                if (s == null || s.Parties == null || p == null) return false;
                for (int i = 0; i < s.Parties.Count; i++)
                {
                    var q = s.Parties[i];
                    if (q == null) continue;
                    if (q == p || (!string.IsNullOrEmpty(q.StringId) && q.StringId == p.StringId)) return true;
                }
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
            // v4.267: 战斗归档落盘是关键路径, 原来静默返回空串(= 战报全丢)无从查起
            catch (Exception ex) { DLog.Force("战报落盘异常: " + ex.Message); return ""; }
        }

        internal static void Load(string data)
        {
            try
            {
                Recent.Clear();
                LastDetail = "";
                // v4.255: 读档清理会话级状态(重复遭遇去重表 / 亲自指挥打标)
                _duelSettled.Clear();
                _commandedEvent = null;
                if (string.IsNullOrEmpty(data)) return;
                foreach (var seg in data.Split((char)2))
                {
                    if (string.IsNullOrEmpty(seg)) continue;
                    Recent.Add(seg.Replace((char)1, '\n'));
                    if (Recent.Count >= 8) break;
                }
                if (Recent.Count > 0) _lastReport = Recent[0];
            }
            // v4.267: 战斗归档读档是关键路径, 原来静默
            catch (Exception ex) { DLog.Force("战报读档异常: " + ex.Message); }
        }
    }
}
