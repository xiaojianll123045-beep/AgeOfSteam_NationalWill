using System;
using System.Collections.Generic;
using System.Reflection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;

namespace FeudalInternalAffairs
{
    // v4.201: 军团属性读取(坐镇指挥用: 士气/组织度/补给)
    // v27: 装备因子(27.4 缺装乘子 / 27.6 武器伤害与破甲加权平均)
    //   缺装乘子直连 DefArmy.EquipMultOf; 逐兵种武器/护甲按层级从 Equipment 目录(27.1)取实际型号,
    //   目录缺失/异常时退回阶级估算兜底(不在此实现装备目录)。
    internal static class DefArmyStats
    {
        internal static float MoraleOf(PartyBase p)
        {
            try
            {
                if (p == null || p.MobileParty == null) return 50f;
                return p.MobileParty.Morale;
            }
            catch { return 50f; }
        }

        // 组织度(0~100): 以士气为主, 我方军团编制再给加成
        internal static float OrgOf(PartyBase p)
        {
            try
            {
                // v4.205: 组织度改为独立状态(V3): 默认满值, 战斗/缺粮扣减, 日恢复 1%
                float org = 100f;
                try
                {
                    if (p != null && p.MobileParty != null)
                    {
                        for (int i = 0; i < DefArmy.Legions.Count; i++)
                        {
                            var lp = DefArmy.LegionParty(DefArmy.Legions[i]);
                            if (lp != null && lp == p.MobileParty) { org = ArmyDoctrine.OrgOf2(ArmyDoctrine.LegionKey(DefArmy.Legions[i])); break; }
                        }
                        if (p.MobileParty.Food < 20f) org = Math.Min(org, 70f);   // V3: 补给不足 -> 最大组织度惩罚
                    }
                }
                catch { }
                // 是否为我方编制军团(DefArmy.Legions)
                try
                {
                    if (p != null && p.MobileParty != null)
                    {
                        for (int i = 0; i < DefArmy.Legions.Count; i++)
                        {
                            var lp = DefArmy.LegionParty(DefArmy.Legions[i]);
                            if (lp != null && lp == p.MobileParty) { org += 10f; break; }
                        }
                    }
                }
                catch { }
                // v4.202: 超编扣组织度 + 老兵度加成
                try
                {
                    if (p != null && p.MobileParty != null)
                    {
                        org *= ArmyDoctrine.OverLimitMult(p.MobileParty);
                        org += ArmyDoctrine.VetOfParty(p.MobileParty) / 5f;
                        // v4.241: 电报机(支援槽 2, 效果"组织度 +5")
                        if (DefArmy.HasSupport(p.MobileParty, 2)) org += 5f;
                    }
                }
                catch { }
                if (org < 0f) org = 0f;
                if (org > 100f) org = 100f;
                return org;
            }
            catch { return 50f; }
        }

        // ==================== v27 装备因子 ====================
        // 缺装乘子 = 0.7 + 0.3 × 满足率(27.4); 由 DefArmy 维护(原版部队视为满足率 100%)
        internal static float EquipMultOf(PartyBase p)
        {
            try
            {
                var mp = p != null ? p.MobileParty : null;
                if (mp == null) return 1f;
                return DefArmy.EquipMultOf(mp);
            }
            catch { return 1f; }
        }

        // 参战炮数(27.6.1); 反射 DefArmy.CannonsOf/ArtilleryOf, 未就绪按 0(TODO)
        internal static int CannonsOf(PartyBase p)
        {
            try
            {
                var mp = p != null ? p.MobileParty : null;
                if (mp == null) return 0;
                var t = typeof(DefArmy);
                string[] names = { "CannonsOf", "ArtilleryOf", "GunsOf", "CannonCountOf" };
                for (int i = 0; i < names.Length; i++)
                {
                    var m = t.GetMethod(names[i], BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    if (m == null) continue;
                    var ps = m.GetParameters();
                    if (ps.Length != 1 || m.ReturnType == typeof(void)) continue;
                    object arg = null;
                    if (ps[0].ParameterType == typeof(MobileParty)) arg = mp;
                    else if (ps[0].ParameterType == typeof(PartyBase)) arg = mp.Party;
                    else if (ps[0].ParameterType == typeof(string)) arg = mp.StringId;
                    if (arg == null) continue;
                    object raw;
                    try { raw = m.Invoke(null, new[] { arg }); } catch { continue; }
                    if (raw == null) continue;
                    return Math.Max(0, (int)Math.Round(Convert.ToSingle(raw, System.Globalization.CultureInfo.InvariantCulture)));
                }
            }
            catch { }
            // TODO(装备目录): 炮兵编制就绪前按 0 门计
            return 0;
        }

        // 武器加权平均(27.6): 全体按人头加权 伤害/破甲; 远程参数只统计射手; 火器占比 = 火器射手/射手
        internal static void AvgWeaponOf(PartyBase p, out float dmg, out float pierce, out float reload,
            out float accuracy, out float range, out float firearmShare)
        {
            dmg = 45f; pierce = 20f; reload = 1.5f; accuracy = 1f; range = 4f; firearmShare = 0f;
            try
            {
                if (TryPartyWeaponProfile(p, out dmg, out pierce, out reload, out accuracy, out range, out firearmShare))
                    return;
                var roster = RosterOf(p);
                if (roster == null) return;
                float allW = 0f, rdW = 0f, fireW = 0f;
                float d = 0f, pr = 0f, rl = 0f, ac = 0f, rg = 0f;
                foreach (var e in roster.GetTroopRoster())
                {
                    var c = e.Character;
                    int n = e.Number;
                    if (c == null || c.IsHero || n <= 0) continue;
                    float fd, fp, ff;
                    WeaponOfTroop(c, out fd, out fp, out ff);
                    d += fd * n; pr += fp * n; allW += n;
                    if (c.IsRanged)
                    {
                        float r1, a1, g1;
                        RangedParamsOfTroop(c, out r1, out a1, out g1);
                        rl += r1 * n; ac += a1 * n; rg += g1 * n; rdW += n;
                        fireW += ff * n;
                    }
                }
                if (allW > 0f) { dmg = d / allW; pierce = pr / allW; }
                if (rdW > 0f) { reload = rl / rdW; accuracy = ac / rdW; range = rg / rdW; firearmShare = fireW / rdW; }
            }
            catch { }
        }

        // 反射 DefArmy.WeaponProfileOf(MobileParty/PartyBase/string) -> float[6] 或对象属性;
        // TODO(装备目录): 就绪后优先走真实逐型号加权(现为兜底按兵种阶级估算)
        private static bool TryPartyWeaponProfile(PartyBase p, out float dmg, out float pierce, out float reload,
            out float accuracy, out float range, out float firearmShare)
        {
            dmg = 45f; pierce = 20f; reload = 1.5f; accuracy = 1f; range = 4f; firearmShare = 0f;
            try
            {
                var mp = p != null ? p.MobileParty : null;
                if (mp == null) return false;
                var t = typeof(DefArmy);
                string[] names = { "WeaponProfileOf", "WeaponStatsOf", "AvgWeaponOf" };
                for (int i = 0; i < names.Length; i++)
                {
                    var m = t.GetMethod(names[i], BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    if (m == null) continue;
                    var ps = m.GetParameters();
                    if (ps.Length != 1 || m.ReturnType == typeof(void)) continue;
                    object arg = null;
                    if (ps[0].ParameterType == typeof(MobileParty)) arg = mp;
                    else if (ps[0].ParameterType == typeof(PartyBase)) arg = mp.Party;
                    else if (ps[0].ParameterType == typeof(string)) arg = mp.StringId;
                    if (arg == null) continue;
                    object raw;
                    try { raw = m.Invoke(null, new[] { arg }); } catch { continue; }
                    var arr = raw as float[];
                    if (arr != null && arr.Length >= 6)
                    {
                        dmg = arr[0]; pierce = arr[1]; reload = arr[2]; accuracy = arr[3]; range = arr[4]; firearmShare = arr[5];
                        return true;
                    }
                    if (raw != null)
                    {
                        float v;
                        if (TryProp(raw, new[] { "Damage", "Dmg" }, out v)) dmg = v;
                        if (TryProp(raw, new[] { "Pierce", "ArmorPierce", "Penetration" }, out v)) pierce = v;
                        if (TryProp(raw, new[] { "Reload", "ReloadPerMinute" }, out v)) reload = v;
                        if (TryProp(raw, new[] { "Accuracy", "Precision" }, out v)) accuracy = v;
                        if (TryProp(raw, new[] { "Range" }, out v)) range = v;
                        if (TryProp(raw, new[] { "FirearmShare", "GunShare" }, out v)) firearmShare = v;
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }

        private static bool TryProp(object obj, string[] names, out float v)
        {
            v = 0f;
            try
            {
                var t = obj.GetType();
                for (int i = 0; i < names.Length; i++)
                {
                    var p = t.GetProperty(names[i], BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (p != null && p.CanRead)
                    {
                        object raw = p.GetValue(obj, null);
                        if (raw != null) { v = Convert.ToSingle(raw, System.Globalization.CultureInfo.InvariantCulture); return true; }
                    }
                }
            }
            catch { }
            return false;
        }

        internal static float ArmorOfParty(PartyBase p)
        {
            try
            {
                var roster = RosterOf(p);
                if (roster == null) return 10f;
                float sum = 0f; float w = 0f;
                foreach (var e in roster.GetTroopRoster())
                {
                    var c = e.Character;
                    int n = e.Number;
                    if (c == null || c.IsHero || n <= 0) continue;
                    sum += ArmorOfTroop(c) * n;
                    w += n;
                }
                return w > 0f ? sum / w : 10f;
            }
            catch { return 10f; }
        }

        internal static float TrainingOfParty(PartyBase p)
        {
            try
            {
                var roster = RosterOf(p);
                if (roster == null) return 1f;
                float sum = 0f; float w = 0f;
                foreach (var e in roster.GetTroopRoster())
                {
                    var c = e.Character;
                    int n = e.Number;
                    if (c == null || c.IsHero || n <= 0) continue;
                    sum += TrainingOfTroop(c) * n;
                    w += n;
                }
                return w > 0f ? sum / w : 1f;
            }
            catch { return 1f; }
        }

        internal static int FirearmCountOf(PartyBase p)
        {
            try
            {
                var roster = RosterOf(p);
                if (roster == null) return 0;
                int n = 0;
                foreach (var e in roster.GetTroopRoster())
                {
                    var c = e.Character;
                    int num = e.Number;
                    if (c == null || c.IsHero || num <= 0) continue;
                    float d, pr, f;
                    WeaponOfTroop(c, out d, out pr, out f);
                    if (f >= 0.5f) n += num;
                }
                return n;
            }
            catch { return 0; }
        }

        internal static float FirearmShareOf(PartyBase p)
        {
            try
            {
                var roster = RosterOf(p);
                if (roster == null) return 0f;
                int total = 0, fire = 0;
                foreach (var e in roster.GetTroopRoster())
                {
                    var c = e.Character;
                    int num = e.Number;
                    if (c == null || c.IsHero || num <= 0) continue;
                    total += num;
                    float d, pr, f;
                    WeaponOfTroop(c, out d, out pr, out f);
                    if (f >= 0.5f) fire += num;
                }
                return total > 0 ? fire / (float)total : 0f;
            }
            catch { return 0f; }
        }

        // 骠骑兵(追击/缴获 +20%, 27.7): 按兵种名匹配; 兵种表就绪后可换 id 判定
        internal static int HussarCount(PartyBase p)
        {
            try
            {
                var roster = RosterOf(p);
                if (roster == null) return 0;
                int n = 0;
                foreach (var e in roster.GetTroopRoster())
                {
                    var c = e.Character;
                    int num = e.Number;
                    if (c == null || c.IsHero || num <= 0) continue;
                    if (!c.IsMounted) continue;
                    string nm = c.Name != null ? c.Name.ToString().ToLowerInvariant() : "";
                    if (nm.Contains("骠骑") || nm.Contains("hussar")) n += num;
                }
                return n;
            }
            catch { return 0; }
        }

        // 轻步兵地形适应(27.6.3 惩罚减半)
        internal static bool HasLightInfantry(PartyBase p)
        {
            try
            {
                var roster = RosterOf(p);
                if (roster == null) return false;
                foreach (var e in roster.GetTroopRoster())
                {
                    var c = e.Character;
                    if (c == null || c.IsHero || e.Number <= 0) continue;
                    string nm = c.Name != null ? c.Name.ToString().ToLowerInvariant() : "";
                    if (nm.Contains("轻步兵") || nm.Contains("light infantry")) return true;
                }
            }
            catch { }
            return false;
        }

        // ==================== 逐兵种装备取值(27.6 加权平均用) ====================
        // 优先按层级从 Equipment 目录取实际型号(27.1), 目录缺失时退回阶级估算
        private static readonly float[] DmgByTier = { 18f, 28f, 43f, 58f, 100f, 120f, 140f };
        private static readonly float[] PierceByTier = { 8f, 12f, 22f, 38f, 60f, 78f, 100f };
        private static readonly float[] ReloadByTier = { 2.5f, 3.0f, 2.2f, 1.4f, 1.8f, 3.0f, 6.0f };
        private static readonly float[] AccByTier = { 0.80f, 0.82f, 0.86f, 0.92f, 0.98f, 1.02f, 1.08f };
        private static readonly float[] RangeByTier = { 3f, 3f, 4f, 4f, 4f, 5f, 5f };
        private static readonly float[] ArmorByTier = { 5f, 10f, 25f, 40f, 55f, 70f, 78f };
        private static readonly float[] TrainByTier = { 0.85f, 0.85f, 0.90f, 0.97f, 1.05f, 1.10f, 1.15f };

        private static int TierIdx(CharacterObject c)
        {
            int t = 0;
            try { t = c != null ? c.Tier : 0; } catch { }
            if (t < 0) t = 0;
            if (t > 6) t = 6;
            return t;
        }

        // 目录内取档内最优型号(优先已解锁 -> 高层级 -> 高伤害), 不命中返回 null
        private static EquipDef BestWeaponOf(CharacterObject c)
        {
            try
            {
                if (c == null) return null;
                int tier = TierIdx(c);
                EquipDef best = null;
                int bestTier = -1;
                float bestDmg = -1f;
                bool bestUn = false;
                var all = Equipment.All;
                for (int i = 0; i < all.Count; i++)
                {
                    var e = all[i];
                    if (e == null || e.Tier < 1 || e.Tier > tier || e.Dmg <= 0) continue;
                    if (c.IsRanged)
                    {
                        if (e.Cat != EquipCat.Ranged) continue;
                    }
                    else
                    {
                        if (e.Cat != EquipCat.Melee) continue;
                        if (!c.IsMounted && Equipment.HasTag(e, EquipTag.Lance)) continue;   // 步兵不配骑枪
                    }
                    bool un = Equipment.IsUnlocked(e);
                    bool better;
                    if (best == null) better = true;
                    else if (un != bestUn) better = un;
                    else if (e.Tier != bestTier) better = e.Tier > bestTier;
                    else better = e.Dmg > bestDmg;
                    if (better) { best = e; bestTier = e.Tier; bestDmg = e.Dmg; bestUn = un; }
                }
                return best;
            }
            catch { return null; }
        }

        private static EquipDef BestArmorOf(int tier)
        {
            try
            {
                EquipDef best = null;
                int bestProt = -1;
                for (int i = 0; i < Equipment.All.Count; i++)
                {
                    var e = Equipment.All[i];
                    if (e == null || e.Cat != EquipCat.Armor || e.ArmorClass == ArmorClass.Addon) continue;
                    if (e.Tier < 1 || e.Tier > tier || e.Prot <= 0) continue;
                    if (e.Prot > bestProt) { best = e; bestProt = e.Prot; }
                }
                return best;
            }
            catch { return null; }
        }

        internal static void WeaponOfTroop(CharacterObject c, out float dmg, out float pierce, out float firearm)
        {
            var e = BestWeaponOf(c);
            if (e != null)
            {
                dmg = e.Dmg;
                pierce = e.Pen;
                firearm = e.IsFirearm ? 1f : 0f;
                return;
            }
            int t = TierIdx(c);
            dmg = DmgByTier[t];
            pierce = PierceByTier[t];
            firearm = t >= 3 ? 1f : 0f;   // 兜底: T3 起为火器档
        }

        internal static void RangedParamsOfTroop(CharacterObject c, out float reload, out float accuracy, out float range)
        {
            try
            {
                if (c != null && c.IsRanged)
                {
                    var e = BestWeaponOf(c);
                    if (e != null && e.Reload > 0f)
                    {
                        reload = e.Reload;
                        accuracy = e.Acc;
                        range = e.Range;
                        return;
                    }
                }
            }
            catch { }
            int t = TierIdx(c);
            reload = ReloadByTier[t];
            accuracy = AccByTier[t];
            range = RangeByTier[t];
        }

        internal static float ArmorOfTroop(CharacterObject c)
        {
            int t = TierIdx(c);
            try
            {
                var e = BestArmorOf(t);
                if (e != null) return e.Prot;
            }
            catch { }
            return ArmorByTier[t];
        }

        internal static float TrainingOfTroop(CharacterObject c)
        {
            return TrainByTier[TierIdx(c)];
        }

        private static TroopRoster RosterOf(PartyBase p)
        {
            try
            {
                if (p == null) return null;
                return p.MemberRoster;
            }
            catch { return null; }
        }

        private static float Clamp(float v, float lo, float hi)
        {
            if (v < lo) return lo;
            if (v > hi) return hi;
            return v;
        }
    }
}
