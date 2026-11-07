using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;

namespace FeudalInternalAffairs
{
    // ==================== v4.273(B-4): 沿海渔获 ====================
    //
    // 【DLC 有什么、我们接什么】DLC 的 `NavalDLC.CampaignBehaviors.NavalFishingCampaignBehaviour` /
    //   `FishingPartyCampaignBehavior` + `FishingPartyComponent` 会自己生成渔船队并把渔获投进港口市场,
    //   那是**原版侧已经在跑的逻辑, 我们不去碰**(碰了就是双份产出/双份状态)。
    //   这一层只补**玩家王国缺的那一块**: "治下的沿海城镇每天给国库带来多少渔获收益" —— 一条
    //   纯只读统计 + 一条收入, 让"拥有海岸线"在财政上看得见。
    //
    // 【口径】每处我方沿海城镇每日渔获 = 基准 40 第纳尔 × 船坞系数 × 繁荣度系数
    //     · 船坞系数: 1 + 0.25 × 船坞等级(0~3 级 -> 1.00 / 1.25 / 1.50 / 1.75; 有造船厂 = 有渔业加工能力)
    //     · 繁荣度系数: 0.5 + 繁荣度 / 6000(繁荣 3000 -> 1.0; 上限 2.0)
    //   并且要求该港**有渔业资源**(原版 Village 里有鱼村 / 或港口本身) —— 判据取"该城镇是否有
    //   绑定村庄产出鱼", 没有就只算基础的一半(近海捕捞)。
    //
    // 【记账】走 Fiscal.AddTax(税收科目)而不是新开科目 —— 我们不想为了这条收入改动财政报表结构
    //   (那会影响所有财政界面)。日志与面板都会明确写"渔获", 玩家看得到来源。
    internal static class NavalFishing
    {
        private const int BasePerPort = 40;

        // 昨日渔获总额(面板显示; 新战役/读档后由日结重算, 老档没有该字段 -> 0, 零副作用)
        internal static int LastGain = 0;
        internal static int LastPorts = 0;

        // 日结: 结算我方沿海城镇的渔获收益。返回实际入账的第纳尔。
        internal static int Daily(Kingdom k)
        {
            LastGain = 0; LastPorts = 0;
            try
            {
                if (k == null) return 0;
                if (!NavalBridge.Usable) return 0;        // 没装 DLC / 没港口 -> 没有渔业
                int total = 0;
                int ports = 0;
                var list = NavalBridge.PortsOf(k);
                for (int i = 0; i < list.Count; i++)
                {
                    var s = list[i];
                    if (s == null || !s.IsTown || s.Town == null) continue;
                    int lvl = NavalBridge.ShipyardLevel(s);
                    float prosp = 0f;
                    try { prosp = s.Town.Prosperity; } catch { }
                    float yardMul = 1f + 0.25f * (lvl > 3 ? 3 : lvl);
                    float prospMul = 0.5f + prosp / 6000f;
                    if (prospMul < 0.5f) prospMul = 0.5f;
                    if (prospMul > 2f) prospMul = 2f;
                    float gain = BasePerPort * yardMul * prospMul;
                    if (!HasFishery(s)) gain *= 0.5f;     // 没有鱼村 = 只有近海捕捞, 减半
                    total += (int)Math.Round(gain);
                    ports++;
                }
                if (total > 0)
                {
                    EconomyWorld.TreasuryAdd(total);
                    try { Fiscal.AddTax(total); } catch { }
                }
                LastGain = total;
                LastPorts = ports;
                return total;
            }
            catch (Exception ex) { DLog.Force("海军: 渔获结算失败 " + ex.Message); return 0; }
        }

        // 该港口有没有渔业资源: 绑定村庄里有没有产鱼的(原版鱼村 VillageType.StringId 含 "fishing")
        private static bool HasFishery(Settlement port)
        {
            try
            {
                if (port == null) return false;
                var vs = port.BoundVillages;
                if (vs == null) return false;
                for (int i = 0; i < vs.Count; i++)
                {
                    var v = vs[i];
                    if (v == null || v.VillageType == null) continue;
                    string id = v.VillageType.StringId;
                    if (!string.IsNullOrEmpty(id) &&
                        (id.IndexOf("fish", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         id.IndexOf("whal", StringComparison.OrdinalIgnoreCase) >= 0))
                        return true;
                }
            }
            catch { }
            return false;
        }

        // 面板一行摘要
        internal static string Summary(Kingdom k)
        {
            try
            {
                if (!NavalBridge.Usable) return "";
                if (LastPorts <= 0) return "沿海渔获: 我方暂无沿海城镇(占领港口后每日有渔获收入)";
                return "沿海渔获: " + LastPorts + " 处港口 · 昨日 +" + LastGain + " 第纳尔(计入税收)";
            }
            catch { return ""; }
        }

        // 鲸油/海象牙这两种 DLC 商品能不能拿到(市场用它决定要不要显示; 没装 DLC 时查不到)
        internal static bool DlcGoodsAvailable()
        {
            try
            {
                return FeudalGoods.Item(FeudalGoods.WhaleOil) != null
                    || FeudalGoods.Item(FeudalGoods.WalrusTusk) != null;
            }
            catch { return false; }
        }
    }
}
