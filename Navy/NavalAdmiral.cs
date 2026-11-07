using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.Core;

namespace FeudalInternalAffairs
{
    // v4.270: 海战 DLC 的**海军技能/特长**接入层(纯反射, 零 NavalDLC 编译期引用)。
    //
    // 【调研到的 DLC 事实(ilspycmd 反编译 NavalDLC.dll 实证)】
    //   · NavalDLC.CharacterDevelopment.NavalSkills  三个 public static 属性返回 SkillObject:
    //       Mariner    —— "个人海战战力 + 在海上带兵与战术的效率"      -> 接我方海战战力
    //       Boatswain  —— "船员福祉纪律 + 船只战备(索具/补给)"        -> 接我方舰船维护费
    //       Shipmaster —— "航行能力 + 海军攻城器械 + 修船/升级速度"    -> 接我方海运容量
    //   · NavalDLC.CharacterDevelopment.NavalPerks   三个 public 嵌套类
    //       (Mariner 20 项 / Boatswain 21 项 / Shipmaster 15 项), 每项是 public static 属性返回 PerkObject。
    //   · 关键: SkillObject 与 PerkObject **都是基础游戏类型**
    //       (TaleWorlds.Core.SkillObject / TaleWorlds.CampaignSystem.CharacterDevelopment.PerkObject),
    //       取值也全走基础游戏 public API: Hero.GetSkillValue(SkillObject) / Hero.GetPerkValue(PerkObject)。
    //     -> 反射**只用来把 DLC 的那几个静态属性/嵌套类型"捞出来"**, 之后一切按基础游戏类型走,
    //        所以 DLC 缺失或改版时最坏情况只是"读不到", 不会有 TypeLoadException / MissingMethodException。
    //
    // 【特长为什么不写死名字】DLC 的特长共 56 项, 写死 56 个字符串会在 DLC 改版时静默失效。
    //   这里改为**枚举** NavalPerks 的三个嵌套类的全部 public static 属性, 凡返回 PerkObject 的都在册,
    //   再逐个问 Hero.GetPerkValue —— 于是"玩家点了几个海军特长"这件事永远读得对。
    internal static class NavalAdmiral
    {
        // ==================== 反射缓存 ====================
        private static bool _probed;

        private static PropertyInfo _pMarinerSkill, _pBoatswainSkill, _pShipmasterSkill;
        private static readonly List<PropertyInfo> _perkMariner = new List<PropertyInfo>();
        private static readonly List<PropertyInfo> _perkBoatswain = new List<PropertyInfo>();
        private static readonly List<PropertyInfo> _perkShipmaster = new List<PropertyInfo>();

        // DLC 装没装(只看类型在不在, 不看实例)
        internal static bool TypesPresent { get { Probe(); return _pMarinerSkill != null || _perkMariner.Count > 0; } }

        private static void Probe()
        {
            if (_probed) return;
            _probed = true;
            try
            {
                var tSkills = AccessTools.TypeByName("NavalDLC.CharacterDevelopment.NavalSkills");
                if (tSkills != null)
                {
                    _pMarinerSkill = SkillProp(tSkills, "Mariner");
                    _pBoatswainSkill = SkillProp(tSkills, "Boatswain");
                    _pShipmasterSkill = SkillProp(tSkills, "Shipmaster");
                }
                var tPerks = AccessTools.TypeByName("NavalDLC.CharacterDevelopment.NavalPerks");
                if (tPerks != null)
                {
                    CollectPerks(tPerks, "Mariner", _perkMariner);
                    CollectPerks(tPerks, "Boatswain", _perkBoatswain);
                    CollectPerks(tPerks, "Shipmaster", _perkShipmaster);
                }
                DLog.Force("海军技能: DLC 接口探测 " + (TypesPresent
                    ? ("成功(技能 3 项 · 特长 " + (_perkMariner.Count + _perkBoatswain.Count + _perkShipmaster.Count) + " 项)")
                    : "未命中(按无 DLC 处理, 海军计算不加成)"));
            }
            catch (Exception ex)
            {
                DLog.Force("海军技能: 探测异常(按无 DLC 处理) " + ex.Message);
            }
        }

        private static PropertyInfo SkillProp(Type t, string name)
        {
            try
            {
                var p = t.GetProperty(name, BindingFlags.Public | BindingFlags.Static);
                if (p == null) return null;
                // 只接受返回基础游戏 SkillObject 的属性(类型不符 = DLC 改版, 直接放弃, 不冒险取值)
                return typeof(SkillObject).IsAssignableFrom(p.PropertyType) ? p : null;
            }
            catch { return null; }
        }

        private static void CollectPerks(Type outer, string nestedName, List<PropertyInfo> into)
        {
            try
            {
                var nt = outer.GetNestedType(nestedName, BindingFlags.Public);
                if (nt == null) return;
                var ps = nt.GetProperties(BindingFlags.Public | BindingFlags.Static);
                for (int i = 0; i < ps.Length; i++)
                {
                    var p = ps[i];
                    if (p == null) continue;
                    // 只要 PerkObject 类型的静态属性(嵌套类里可能混有别的静态成员)
                    if (typeof(PerkObject).IsAssignableFrom(p.PropertyType)) into.Add(p);
                }
            }
            catch { }
        }

        // ==================== 读取某位提督的海军档案 ====================
        internal class Profile
        {
            internal bool Available;              // 是否读到了 DLC 的海军技能体系
            internal string Source = "";          // 提督名(面板显示)
            internal int Mariner, Boatswain, Shipmaster;   // 三项海军技能等级(0~300)
            internal int PerkMariner, PerkBoatswain, PerkShipmaster;   // 三项已点特长数
            internal int PerkTotal { get { return PerkMariner + PerkBoatswain + PerkShipmaster; } }
            internal int SkillSum { get { return Mariner + Boatswain + Shipmaster; } }
        }

        // 取提督档案: h 为空则用玩家本人(舰队提督默认 = 玩家)
        internal static Profile Of(Hero h)
        {
            var prof = new Profile();
            try
            {
                Probe();
                if (h == null) { try { h = Hero.MainHero; } catch { } }
                if (h == null) return prof;
                try { prof.Source = h.Name != null ? h.Name.ToString() : ""; } catch { }

                prof.Mariner = ReadSkill(h, _pMarinerSkill);
                prof.Boatswain = ReadSkill(h, _pBoatswainSkill);
                prof.Shipmaster = ReadSkill(h, _pShipmasterSkill);

                prof.PerkMariner = CountPerks(h, _perkMariner);
                prof.PerkBoatswain = CountPerks(h, _perkBoatswain);
                prof.PerkShipmaster = CountPerks(h, _perkShipmaster);

                // 三项技能全 0 且一个特长都没有 = 没装 DLC / 该英雄没走海军线 -> 视为"无加成"
                prof.Available = TypesPresent && (prof.SkillSum > 0 || prof.PerkTotal > 0);
            }
            catch { }
            return prof;
        }

        private static int ReadSkill(Hero h, PropertyInfo p)
        {
            try
            {
                if (h == null || p == null) return 0;
                var so = p.GetValue(null, null) as SkillObject;
                if (so == null) return 0;
                return h.GetSkillValue(so);
            }
            catch { return 0; }   // DLC 未初始化(NavalDLCManager.Instance 为 null)时属性会抛, 静默跳过
        }

        private static int CountPerks(Hero h, List<PropertyInfo> props)
        {
            int n = 0;
            try
            {
                if (h == null || props == null) return 0;
                for (int i = 0; i < props.Count; i++)
                {
                    try
                    {
                        var po = props[i].GetValue(null, null) as PerkObject;
                        if (po == null) continue;
                        if (h.GetPerkValue(po)) n++;
                    }
                    catch { }
                }
            }
            catch { }
            return n;
        }

        // ==================== 三处落点(全部返回乘数, 无 DLC 时恒为 1) ====================

        // 1) 海战战力乘数: Mariner 权重最高(它的说明就是"在海上带兵与战术"),
        //    三项全满(300/300/300) => +30%; 每点一个海军特长再 +0.4%; 上限 +45%
        internal static float PowerMul(Profile p)
        {
            try
            {
                if (p == null || !p.Available) return 1f;
                float weighted = p.Mariner * 0.5f + p.Boatswain * 0.2f + p.Shipmaster * 0.3f;   // 满 = 300
                float v = weighted / 300f * 0.30f + p.PerkTotal * 0.004f;
                return 1f + MBMathClamp(v, 0f, 0.45f);
            }
            catch { return 1f; }
        }

        // 2) 舰船维护费乘数: Boatswain 管"船只战备与补给" -> 主折扣; Shipmaster 次之
        //    满技能 -20%, 每个 Boatswain 特长再 -0.5%; 最多 -30%(不会白送舰队)
        internal static float UpkeepMul(Profile p)
        {
            try
            {
                if (p == null || !p.Available) return 1f;
                float v = p.Boatswain / 300f * 0.15f + p.Shipmaster / 300f * 0.05f + p.PerkBoatswain * 0.005f;
                return 1f - MBMathClamp(v, 0f, 0.30f);
            }
            catch { return 1f; }
        }

        // 3) 海运容量乘数: Shipmaster 管"航行能力" -> 主加成(一次能运更多)
        //    满技能 +20%, 每个 Shipmaster 特长再 +0.5%; 上限 +35%
        internal static float CargoMul(Profile p)
        {
            try
            {
                if (p == null || !p.Available) return 1f;
                float v = p.Shipmaster / 300f * 0.20f + p.Boatswain / 300f * 0.05f + p.PerkShipmaster * 0.005f;
                return 1f + MBMathClamp(v, 0f, 0.35f);
            }
            catch { return 1f; }
        }

        // 运兵能力乘数(海运时"这批船能运多少人"用): 同样走 Shipmaster/Boatswain, 幅度比货舱小
        internal static float TroopMul(Profile p)
        {
            try
            {
                if (p == null || !p.Available) return 1f;
                float v = p.Shipmaster / 300f * 0.12f + p.Boatswain / 300f * 0.08f + p.PerkTotal * 0.002f;
                return 1f + MBMathClamp(v, 0f, 0.25f);
            }
            catch { return 1f; }
        }

        private static float MBMathClamp(float v, float a, float b)
        {
            if (v < a) return a;
            if (v > b) return b;
            return v;
        }

        // ==================== 面板文案 ====================
        // 一行摘要: 无 DLC/无技能时给一句明确的"无加成", 不显示 0/0/0 这种噪音
        internal static string Summary(Profile p)
        {
            try
            {
                if (!TypesPresent) return "海军技能: 未检测到海战 DLC 的海军技能体系(不参与计算)";
                if (p == null || !p.Available)
                    return "海军技能: 提督尚未获得海军技能/特长(战力 ×1.00 · 维护 ×1.00 · 海运 ×1.00)";
                var sb = new StringBuilder();
                sb.Append("提督 ").Append(string.IsNullOrEmpty(p.Source) ? "?" : p.Source).Append(": ");
                sb.Append("水手 ").Append(p.Mariner).Append(" · 水手长 ").Append(p.Boatswain).Append(" · 船长 ").Append(p.Shipmaster);
                sb.Append(" · 海军特长 ").Append(p.PerkTotal).Append(" 项");
                sb.Append("  →  战力 ×").Append(PowerMul(p).ToString("0.00"));
                sb.Append(" · 维护 ×").Append(UpkeepMul(p).ToString("0.00"));
                sb.Append(" · 海运 ×").Append(CargoMul(p).ToString("0.00"));
                return sb.ToString();
            }
            catch { return "海军技能: 读取失败(不参与计算)"; }
        }

        // 面板多行详情(海军页"技能"行用)
        internal static List<string> Detail(Profile p)
        {
            var list = new List<string>();
            try
            {
                if (!TypesPresent) { list.Add("未装海战 DLC: 海军技能不参与任何计算"); return list; }
                if (p == null || !p.Available)
                {
                    list.Add("提督还没有海军技能或特长 —— 三项加成均为 ×1.00");
                    list.Add("(在 DLC 的海战/航海行为中提升 水手/水手长/船长 技能, 点出特长即生效)");
                    return list;
                }
                list.Add("水手 " + p.Mariner + "(海战战力) · 水手长 " + p.Boatswain + "(维护费) · 船长 " + p.Shipmaster + "(海运容量)");
                list.Add("特长: 水手系 " + p.PerkMariner + " · 水手长系 " + p.PerkBoatswain + " · 船长系 " + p.PerkShipmaster + "  (共 " + p.PerkTotal + " 项)");
                list.Add("落点: 海战战力 ×" + PowerMul(p).ToString("0.00")
                    + " · 舰船维护费 ×" + UpkeepMul(p).ToString("0.00")
                    + " · 海运容量 ×" + CargoMul(p).ToString("0.00")
                    + " · 单次运兵 ×" + TroopMul(p).ToString("0.00"));
                // v4.273(B-6): 逐条列出**正在生效**的特长名(原来只显示一个计数, 玩家不知道点了哪些)
                var names = ActivePerkNames(Hero.MainHero);
                if (names.Count > 0)
                {
                    var sb = new StringBuilder();
                    sb.Append("生效特长(").Append(names.Count).Append("): ");
                    for (int i = 0; i < names.Count; i++)
                    {
                        if (i > 0) sb.Append(" · ");
                        sb.Append(names[i]);
                    }
                    list.Add(sb.ToString());
                }
            }
            catch { }
            return list;
        }

        // ==================== v4.273(B-6): 正在生效的海军特长(逐条名字) ====================
        //   原来 Profile 只统计"点了几个"(PerkTotal), 玩家看不到具体是哪几项。
        //   这里按三条线分别枚举, 取 `PerkObject.Name`(DLC 的中文本地化会跟着游戏语言走),
        //   返回形如 "水手: 老练舵手/看风使舵" 的条目 —— 全部走基础游戏 public API, DLC 缺失时返回空表。
        internal static List<string> ActivePerkNames(Hero h)
        {
            var res = new List<string>();
            try
            {
                Probe();
                if (h == null) { try { h = Hero.MainHero; } catch { } }
                if (h == null) return res;
                AddNames(h, _perkMariner, "水手", res);
                AddNames(h, _perkBoatswain, "水手长", res);
                AddNames(h, _perkShipmaster, "船长", res);
            }
            catch { }
            return res;
        }

        private static void AddNames(Hero h, List<PropertyInfo> props, string group, List<string> into)
        {
            try
            {
                if (h == null || props == null || props.Count == 0) return;
                var sb = new StringBuilder();
                for (int i = 0; i < props.Count; i++)
                {
                    try
                    {
                        var po = props[i].GetValue(null, null) as PerkObject;
                        if (po == null) continue;
                        if (!h.GetPerkValue(po)) continue;
                        string nm = null;
                        try { nm = po.Name != null ? po.Name.ToString() : null; } catch { }
                        if (string.IsNullOrEmpty(nm)) nm = props[i].Name;   // 拿不到显示名就退回属性名(至少可辨认)
                        if (sb.Length > 0) sb.Append('/');
                        sb.Append(nm);
                    }
                    catch { }
                }
                if (sb.Length > 0) into.Add(group + ": " + sb.ToString());
            }
            catch { }
        }
    }
}
