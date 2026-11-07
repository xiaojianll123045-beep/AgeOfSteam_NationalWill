using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;
using TaleWorlds.CampaignSystem;

namespace FeudalInternalAffairs
{
    // ==================== v4.273(B-2): 海战 DLC 的 11 条海军王国政策 ====================
    //
    // 【为什么需要这一层】DLC 的 `NavalDLC.NavalPolicies`(反编译 5878-5888) 注册了 11 条王国政策,
    //   它们全部是原版 `TaleWorlds.CampaignSystem.PolicyObject`, 走**原版**的王国决策界面提案/表决。
    //   其中 `CoastalGuardEdict`(海岸卫队)是**海上巡逻队的唯一开关**:
    //     `NavalSettlementPatrolModel.CanSettlementHavePatrolParties` 要求
    //     `kingdom.HasPolicy(NavalPolicies.CoastalGuardEdict)`(DLC 反编译 105780-105783)。
    //   ⇒ 没通过这条法令, 在航海上巡逻队恒为 0 ⇒ 我们那 4 项巡逻科技(floating_harbor / jeune_ecole /
    //     aircraft_carrier / battlefleet_tactics)的"巡逻收益 +X%"完全是零效果。
    //   玩家在原版界面里根本看不出"这 11 条政策分别干什么、我现在通过了哪几条" —— 这一层就是补这个。
    //
    // 【双份状态的红线】政策**本体与状态都在原版侧**(PolicyObject + Kingdom.HasPolicy), 我们:
    //     · 只**读** —— 反射读 DLC 的 11 个 PolicyObject, 再问 Kingdom.HasPolicy;
    //     · 只**引导** —— 面板告诉玩家"去王国决策界面提案", 绝不自己存一份"已通过"状态、
    //       也绝不自己 ApplyPolicy(那会与原版表决/存档打架)。
    internal static class NavalPolicy
    {
        internal class Info
        {
            internal string Prop = "";       // DLC 属性名(NavalPolicies 上的)
            internal string Name = "";       // 中文名(我们自己写的, 与 DLC 的中文本地化一致)
            internal string Effect = "";     // 中文效果说明
            internal bool Key;               // 是不是"关键政策"(面板要用醒目措辞)
            internal object Policy;          // 反射拿到的 PolicyObject 实例(可能为 null)
        }

        // 11 条政策一览(名字与 DLC 的 zho-CN 本地化对齐; 效果按反编译的行为归纳)
        private static readonly Info[] Table =
        {
            new Info { Prop = "CoastalGuardEdict", Name = "海岸卫队", Key = true,
                Effect = "沿海城镇可组建海上巡逻队(本 mod 的海上巡逻加成、4 项巡逻科技全靠它才有意义)" },
            new Info { Prop = "FraternalFleetDoctrine", Name = "兄弟舰队条令",
                Effect = "舰队协同: 王国舰队共同行动时战斗力提升" },
            new Info { Prop = "KingsTitheOnKeels", Name = "王室龙骨什一税",
                Effect = "按船只征收什一税: 王国从舰队获得额外收入" },
            new Info { Prop = "RoyalRansomClaim", Name = "王室赎金索取权",
                Effect = "海战俘获的赎金归王室(提高海战收益)" },
            new Info { Prop = "RoyalNavyPrerogative", Name = "皇家海军特权",
                Effect = "王室对舰队的征用权: 强化王国海军调度" },
            new Info { Prop = "MaritimeWealEdict", Name = "海事福利敕令",
                Effect = "港口民生: 沿海城镇繁荣与忠诚提升" },
            new Info { Prop = "KingsPardonForPirates", Name = "王室海盗赦令",
                Effect = "招安海盗: 海盗威胁下降, 可转为己用" },
            new Info { Prop = "RaidersSpoils", Name = "劫掠战利品",
                Effect = "鼓励劫掠: 沿海劫掠收益提升" },
            new Info { Prop = "BolsterTheFyrd", Name = "充实民兵",
                Effect = "沿海民兵加强: 港口守备与民兵规模提升" },
            new Info { Prop = "NavalConjoiningStatute", Name = "海军联合法令",
                Effect = "舰队编成: 多氏族舰队联合行动的组织度提升" },
            new Info { Prop = "ArsenalDepositoryAct", Name = "军械库储备法",
                Effect = "军械储备: 港口造船/维修与舰船补给改善" },
        };

        private static bool _probed;
        private static PropertyInfo[] _policies;   // NavalPolicies 上的 11 个 PolicyObject 属性

        private static void Probe()
        {
            if (_probed) return;
            _probed = true;
            try
            {
                var t = AccessTools.TypeByName("NavalDLC.NavalPolicies");
                if (t == null)
                {
                    DLog.Force("海军政策: 未检测到 NavalDLC.NavalPolicies(面板不显示政策清单)");
                    return;
                }
                var all = t.GetProperties(BindingFlags.Public | BindingFlags.Static);
                var list = new List<PropertyInfo>();
                for (int i = 0; i < all.Length; i++)
                {
                    var p = all[i];
                    if (p == null) continue;
                    if (typeof(PolicyObject).IsAssignableFrom(p.PropertyType)) list.Add(p);
                }
                _policies = list.ToArray();
                DLog.Force("海军政策: 已读取 DLC 海军政策 " + _policies.Length + " 条");
            }
            catch (Exception ex) { DLog.Force("海军政策: 探测异常(按无政策处理) " + ex.Message); }
        }

        internal static bool Available { get { Probe(); return _policies != null && _policies.Length > 0; } }

        // 取 11 条政策的当前状态(只读; 拿不到 PolicyObject 的条目 Policy 为 null)
        internal static List<Info> List(Kingdom k)
        {
            var res = new List<Info>();
            try
            {
                Probe();
                for (int i = 0; i < Table.Length; i++)
                {
                    // 每条都新建一个 Info(避免把反射出来的实例缓存进静态表 -> 换局后变成脏引用)
                    var info = new Info
                    {
                        Prop = Table[i].Prop, Name = Table[i].Name,
                        Effect = Table[i].Effect, Key = Table[i].Key
                    };
                    if (_policies != null)
                    {
                        for (int j = 0; j < _policies.Length; j++)
                        {
                            if (_policies[j].Name != info.Prop) continue;
                            try { info.Policy = _policies[j].GetValue(null, null); } catch { }
                            break;
                        }
                    }
                    res.Add(info);
                }
            }
            catch { }
            return res;
        }

        // 某条政策我方通过了没有(读原版状态; 拿不到政策对象时返回 false)
        internal static bool Has(Kingdom k, Info info)
        {
            try
            {
                if (k == null || info == null || info.Policy == null) return false;
                return k.HasPolicy((PolicyObject)info.Policy);
            }
            catch { return false; }
        }

        // 面板正文: 当前状态摘要 + 未通过的关键政策提示(2~3 行, 固定行高能放下)
        internal static string Summary(Kingdom k)
        {
            try
            {
                Probe();
                if (!Available) return "";
                var list = List(k);
                int on = 0;
                for (int i = 0; i < list.Count; i++) if (Has(k, list[i])) on++;

                var sb = new StringBuilder();
                sb.Append("海军法令(原版王国决策): 已通过 ").Append(on).Append("/").Append(list.Count).Append(" 条");
                // 已通过的列名字(最多 4 条, 免得撑破一行)
                int shown = 0;
                for (int i = 0; i < list.Count && shown < 4; i++)
                {
                    if (!Has(k, list[i])) continue;
                    sb.Append(shown == 0 ? " · " : "、").Append(list[i].Name);
                    shown++;
                }
                if (on > 4) sb.Append(" 等");

                // 海岸卫队没通过 -> 这是"巡逻科技无效"的根因, 必须单独讲清楚
                for (int i = 0; i < list.Count; i++)
                {
                    if (!list[i].Key) continue;
                    if (Has(k, list[i]))
                    {
                        sb.Append("\n关键法令「").Append(list[i].Name).Append("」已通过 —— 海上巡逻队可正常生成");
                    }
                    else
                    {
                        sb.Append("\n★ 关键法令「").Append(list[i].Name).Append("」未通过: ").Append(list[i].Effect)
                          .Append("\n   → 到王国决策界面提案并通过它, 海上巡逻队才会出现(4 项巡逻科技才生效)");
                    }
                    break;
                }
                return sb.ToString();
            }
            catch { return ""; }
        }

        // 全部 11 条的逐条清单(面板换行显示; 每行 "✓/— 名称: 效果")
        internal static string Detail(Kingdom k)
        {
            try
            {
                if (!Available) return "";
                var list = List(k);
                var sb = new StringBuilder();
                for (int i = 0; i < list.Count; i++)
                {
                    bool on = Has(k, list[i]);
                    if (i > 0) sb.Append('\n');
                    sb.Append(on ? "✓ " : "— ").Append(list[i].Name).Append(": ").Append(list[i].Effect);
                }
                return sb.ToString();
            }
            catch { return ""; }
        }

        // 一句话极简版(给空间紧张的地方用)
        internal static string OneLine(Kingdom k)
        {
            try
            {
                if (!Available) return "";
                var list = List(k);
                int on = 0;
                bool coastal = false;
                for (int i = 0; i < list.Count; i++)
                {
                    bool has = Has(k, list[i]);
                    if (has) on++;
                    if (list[i].Key && has) coastal = true;
                }
                return "海军法令 " + on + "/" + list.Count + " 条" + (coastal ? "(含海岸卫队)" : " · 未通过「海岸卫队」→ 无海上巡逻队");
            }
            catch { return ""; }
        }
    }
}
