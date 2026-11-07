using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Party;

namespace FeudalInternalAffairs
{
    // ==================== v4.273(B-5): 4 项 Nord 文化特性 ====================
    //
    // 【反编译实证】DLC 的 `NavalDLC.CharacterDevelopment.NavalCulturalFeats`(DLC 反编译 108351-108369)
    //   注册了 4 项 `FeatObject`(**基础游戏类型**, 即"文化特性"), 全是 **Nord(诺德)** 文化专属:
    //     · NordHostileActionBonusFeat    +30% 劫掠战利品   (id 里写的是 hostile_action_bonus_loot)
    //     · NordHostileActionSpeedFeat    +20% 劫掠速度
    //     · NordShipMovementFeat          +10% 河流/沿海船速
    //     · NordArmyCohesionFeat          Nord 指挥官的军团凝聚力 -30% 流失
    //   注意第 3 项在 DLC 里的 **id 拼错了**(`nord_ship_movemenet_increase`), 这里按你(用户)的指示
    //   **照抄不改** —— 反射是按属性名取的, 所以拼写错不影响读取, 但文档/日志里保留原样以便比对。
    //
    // 【怎么"接进我们自研的文化/机构体系"】这 4 项的效果原版侧已经生效(DLC 自己读 FeatObject 算),
    //   我们能做的、也该做的是两件事:
    //     ① **可见**: 面板告诉玩家"你这个文化有哪些海军特性、当前生效几条";
    //     ② **落点**: 第 3 项(船速)接进我们**自己的**海运 ETA 计算 —— 因为海运是我们自建的运输队,
    //        原版那条船速加成不会作用到它, 必须我们主动算进去(见 NavalTransport.EtaDays)。
    //   第 1/2/4 项属于原版战斗/军团逻辑, 我们不重复实现, 只在面板如实列出(不塞假效果)。
    internal static class NavalCulture
    {
        internal class FeatInfo
        {
            internal string Prop = "";       // DLC 属性名
            internal string Id = "";         // DLC 里的 StringId(照抄, 含拼写错误)
            internal string Name = "";       // 中文名
            internal string Effect = "";     // 中文效果
            internal FeatObject Feat;        // 反射拿到的实例(可能为 null)
        }

        private static readonly FeatInfo[] Table =
        {
            new FeatInfo { Prop = "NordHostileActionBonusFeat", Id = "nord_hostile_action_bonus",
                Name = "北方劫掠者(战利品)", Effect = "劫掠战利品 +30%" },
            new FeatInfo { Prop = "NordHostileActionSpeedFeat", Id = "nord_hostile_action_speed",
                Name = "北方劫掠者(速度)", Effect = "劫掠速度 +20%" },
            new FeatInfo { Prop = "NordShipMovementFeat", Id = "nord_ship_movemenet_increase",
                Name = "北方航海者", Effect = "河流与沿海船速 +10%(本 mod 已接进海运航程)" },
            new FeatInfo { Prop = "NordArmyCohesionFeat", Id = "nord_decreased_cohesion_rate",
                Name = "北方军团", Effect = "Nord 指挥官的军团凝聚力流失 -30%" },
        };

        private static bool _probed;
        private static Dictionary<string, FeatObject> _feats;   // 属性名 -> FeatObject

        private static void Probe()
        {
            if (_probed) return;
            _probed = true;
            try
            {
                var t = AccessTools.TypeByName("NavalDLC.CharacterDevelopment.NavalCulturalFeats");
                if (t == null) { DLog.Info("海军文化: 未检测到 NavalDLC.NavalCulturalFeats(文化特性不显示)"); return; }
                var map = new Dictionary<string, FeatObject>();
                var all = t.GetProperties(BindingFlags.Public | BindingFlags.Static);
                for (int i = 0; i < all.Length; i++)
                {
                    var p = all[i];
                    if (p == null) continue;
                    if (!typeof(FeatObject).IsAssignableFrom(p.PropertyType)) continue;
                    FeatObject f = null;
                    try { f = p.GetValue(null, null) as FeatObject; } catch { }
                    if (f != null) map[p.Name] = f;
                }
                _feats = map;
                DLog.Force("海军文化: 已读取 DLC 诺德文化特性 " + map.Count + " 项");
            }
            catch (Exception ex) { DLog.Force("海军文化: 探测异常(按无特性处理) " + ex.Message); }
        }

        internal static bool Available { get { Probe(); return _feats != null && _feats.Count > 0; } }

        // 把所有特性(带当前文化是否拥有)列出来
        internal static List<FeatInfo> List(CultureObject culture)
        {
            var res = new List<FeatInfo>();
            try
            {
                Probe();
                for (int i = 0; i < Table.Length; i++)
                {
                    var info = new FeatInfo
                    {
                        Prop = Table[i].Prop, Id = Table[i].Id,
                        Name = Table[i].Name, Effect = Table[i].Effect
                    };
                    if (_feats != null)
                    {
                        FeatObject f;
                        if (_feats.TryGetValue(info.Prop, out f)) info.Feat = f;
                    }
                    res.Add(info);
                }
            }
            catch { }
            return res;
        }

        internal static bool Has(CultureObject c, FeatInfo info)
        {
            try
            {
                if (c == null || info == null || info.Feat == null) return false;
                return c.HasFeat(info.Feat);
            }
            catch { return false; }
        }

        // v4.273(B-5 落点): "船速 +10%" -> 海运航程乘数(0.909…)。
        //   只对**拥有该特性的文化**的部队生效(诺德船队走北方河流/沿海更快)。
        //   返回 1.0 = 无加成(没装 DLC / 非诺德 / 特性读不到)。
        internal static float SailTimeMul(MobileParty p)
        {
            try
            {
                Probe();
                if (_feats == null || _feats.Count == 0) return 1f;
                CultureObject c = null;
                try
                {
                    // MobileParty 没有直接的 Culture 属性 -> 走 家族文化 / 领袖英雄文化
                    if (p != null)
                    {
                        var clan = p.ActualClan;
                        if (clan != null) c = clan.Culture;
                        if (c == null && p.LeaderHero != null) c = p.LeaderHero.Culture;
                    }
                }
                catch { }
                if (c == null) return 1f;
                FeatObject f;
                if (!_feats.TryGetValue("NordShipMovementFeat", out f) || f == null) return 1f;
                if (!c.HasFeat(f)) return 1f;
                return 1f / 1.10f;   // +10% 船速 = 同样距离花 1/1.1 的时间
            }
            catch { return 1f; }
        }

        // 面板文案(1~2 行): 我方文化有没有海军特性
        internal static string Summary(CultureObject culture)
        {
            try
            {
                Probe();
                if (!Available) return "";
                var list = List(culture);
                var sb = new StringBuilder();
                int on = 0;
                for (int i = 0; i < list.Count; i++) if (Has(culture, list[i])) on++;
                sb.Append("诺德海军文化特性: ");
                if (on == 0)
                {
                    sb.Append("当前文化(").Append(CultureName(culture)).Append(")无海军特性");
                    sb.Append(" · 4 项分别为: 劫掠战利品 +30% / 劫掠速度 +20% / 船速 +10% / 军团凝聚力 -30% 流失");
                }
                else
                {
                    sb.Append("当前文化(").Append(CultureName(culture)).Append(")生效 ").Append(on).Append("/").Append(list.Count).Append(" 项");
                    int shown = 0;
                    for (int i = 0; i < list.Count && shown < 4; i++)
                    {
                        if (!Has(culture, list[i])) continue;
                        sb.Append(shown == 0 ? ": " : "、").Append(list[i].Name);
                        shown++;
                    }
                }
                return sb.ToString();
            }
            catch { return ""; }
        }

        private static string CultureName(CultureObject c)
        {
            try { return c != null && c.Name != null ? c.Name.ToString() : "?"; }
            catch { return "?"; }
        }
    }
}
