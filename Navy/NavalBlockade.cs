using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;

namespace FeudalInternalAffairs
{
    // v4.272: 封锁的**唯一**结算口径(审计 A4/A11 的修复落点)。
    //
    // 为什么单独抽一个类:
    //   审计指出封锁页第 4 列只能显示"繁荣度"而无法显示"日收益", 因为收益公式藏在日结里。
    //   本次把"敌方日损失 / 我方日收益"的计算**收敛到 Estimate()** 一处:
    //     · NavalBehavior 的日结(真实扣繁荣度/加钱)调它;
    //     · 面板(NavyPanelVM)通过 NavalFleet.EstimateBlockadeDaily 调它。
    //   两边**共用同一段代码**, 所以显示值与实际结算值不会漂移(不会出现"面板说 +300, 实际 +280")。
    internal static class NavalBlockade
    {
        // 结算结果(纯数据, 不做任何写操作)
        internal class EstimateResult
        {
            internal bool Ok = false;
            internal string Why = "";         // Ok=false 时的原因
            internal float ProsperityLoss = 0f;   // 敌方港口今日繁荣度损失
            internal int TariffLoss = 0;          // 敌方港口今日被打掉的累计关税
            internal float EnemyLoss = 0f;        // 敌方今日总损失(繁荣度折算 + 关税)
            internal int MyGain = 0;              // 我方今日国库收益(第纳尔)
            internal float Eff = 1f;              // 当前封锁效率乘数(A4 修复后只在这里乘一次)
        }

        internal const float ProsperityToGold = 14f;   // 1 点繁荣度 ≈ 14 第纳尔(与原公式同口径)
        internal const float GainRatio = 0.35f;        // 我方战果 = 敌方损失 × 35%

        // 计算出"若今天结算, 会发生什么" —— 纯只读, 不改任何状态
        internal static EstimateResult Estimate(Settlement port, Kingdom k)
        {
            var r = new EstimateResult();
            try
            {
                if (!NavalBridge.Usable) { r.Why = "未检测到海战 DLC"; return r; }
                if (port == null || k == null) { r.Why = "没有封锁目标"; return r; }
                if (!port.HasPort || !port.IsTown || port.Town == null) { r.Why = "该聚落不是港口"; return r; }
                var owner = NavalBridge.KingdomOf(port);
                if (owner == null) { r.Why = "港口已无主"; return r; }
                if (owner == k) { r.Why = "不能封锁自己的港口"; return r; }
                if (!k.IsAtWarWith(owner)) { r.Why = "已与该国停战"; return r; }
                if (NavalFleet.MyShips().Count == 0) { r.Why = "我方已无舰船"; return r; }   // v4.273(#2): 显式我方口径

                var sum = NavalTech.Of(k);
                float eff = sum != null ? sum.Blockade : 1f;   // v4.272(A4): eff 只在此处乘一次
                r.Eff = eff;

                // (a) 敌方损失
                float prosperityLoss = (14f + NavalBridge.ShipyardLevel(port) * 6f) * eff;
                float before = 0f;
                try { before = port.Town.Prosperity; } catch { }
                if (before < prosperityLoss) prosperityLoss = Math.Max(0f, before);   // 不把繁荣度打成负数
                r.ProsperityLoss = prosperityLoss;

                int tariffLoss = 0;
                try
                {
                    int tax = port.Town.TradeTaxAccumulated;
                    if (tax > 0)
                    {
                        tariffLoss = (int)Math.Round(tax * Math.Min(0.5f, 0.15f * eff));
                        if (tariffLoss > tax) tariffLoss = tax;
                    }
                }
                catch { }
                r.TariffLoss = tariffLoss;

                r.EnemyLoss = prosperityLoss * ProsperityToGold + tariffLoss;

                // (b) 我方战果。
                //   v4.272(A4): 这里**不再**乘 eff —— 敌方损失(prosperityLoss/tariffLoss)已经乘过 eff 了,
                //   原来再乘一次使收益按 eff² 放大(全科技 eff=7.52 时 ×56.6), 是审计确认的致命放大。
                int gain = (int)Math.Round(r.EnemyLoss * GainRatio);
                if (gain < 0) gain = 0;
                r.MyGain = gain;
                r.Ok = true;
                return r;
            }
            catch (Exception ex)
            {
                r.Ok = false;
                r.Why = "估算异常: " + ex.Message;
                return r;
            }
        }

        // 日结: 真实执行(扣敌方繁荣度/关税, 给我方入账, 记厌战)。返回 null = 今天不结算(附原因)
        internal static string Daily(Settlement port, Kingdom k)
        {
            try
            {
                var r = Estimate(port, k);
                if (!r.Ok) return r.Why;

                // 1) 敌方惩罚: 繁荣度 + 累计关税(繁荣度是原版税收/关税/民兵/招募的总开关)
                try { port.Town.Prosperity = Math.Max(0f, port.Town.Prosperity - r.ProsperityLoss); } catch { }
                if (r.TariffLoss > 0)
                {
                    try { port.Town.TradeTaxAccumulated -= r.TariffLoss; } catch { }
                }

                // 2) 我方战果入账
                if (r.MyGain > 0)
                {
                    EconomyWorld.TreasuryAdd(r.MyGain);
                    Fiscal.AddCourt(-r.MyGain);   // 负值 = 收入
                }
                NavalFleet.BlockadeDamage += r.EnemyLoss;
                NavalFleet.LastBlockadeGain = r.MyGain;
                NavalFleet.LastBlockadeLoss = r.EnemyLoss;

                // 3) 敌方厌战(被封锁方对封锁方)
                try
                {
                    var owner = NavalBridge.KingdomOf(port);
                    if (owner != null)
                    {
                        string ek = owner.StringId + ">" + k.StringId;
                        float cur;
                        if (!WarWeariness.Wear.TryGetValue(ek, out cur)) cur = 0f;
                        WarWeariness.Wear[ek] = Math.Min(100f, cur + 0.8f * r.Eff);
                    }
                }
                catch { }

                // 4) v4.272(A11): 挂贸易禁运 —— 设计里封锁应当**切断敌方贸易**, 而原实现只掉繁荣度。
                //    TradeRoutes.SetEmbargo 自己的语义是"我方对某国禁运": 切断我方与该国的全部路线 +
                //    该国贸易优势 -60(会影响我方对它市场的价格)。所以封锁 = 双向贸易中断, 逻辑自洽。
                //    注意: SetEmbargo(on:true) 每次收 500 第纳尔, 所以**只在还没禁运时**调一次(不能每天扣)。
                try
                {
                    var owner = NavalBridge.KingdomOf(port);
                    if (owner != null && owner.StringId != k.StringId && !TradeRoutes.IsEmbargoed(owner.StringId))
                    {
                        string r2 = TradeRoutes.SetEmbargo(owner.StringId, NavalFleet.BlockadeKingdomName, true);
                        NavalFleet.BlockadeEmbargoOwned = true;   // 记下"这条禁运是封锁带来的", 解除封锁时一并撤销
                        DLog.Force("海军: 封锁附带禁运 -> " + r2);
                    }
                }
                catch (Exception ex) { DLog.Force("海军: 封锁挂禁运失败(不影响封锁本身) " + ex.Message); }

                return null;
            }
            catch (Exception ex) { return "封锁结算异常: " + ex.Message; }
        }

        // 解除封锁时的收尾: 撤销"由封锁带来"的那条禁运(玩家手动挂的禁运不动)
        internal static void EmbargoOff()
        {
            try
            {
                if (!NavalFleet.BlockadeEmbargoOwned) return;
                NavalFleet.BlockadeEmbargoOwned = false;
                string id = NavalFleet.BlockadeKingdomId;
                if (string.IsNullOrEmpty(id)) return;
                if (!TradeRoutes.IsEmbargoed(id)) return;   // 已经没了(停战/手动解除) -> 不用管
                string s = TradeRoutes.SetEmbargo(id, NavalFleet.BlockadeKingdomName, false);
                DLog.Force("海军: 解除封锁顺带撤销禁运 -> " + s);
            }
            catch { }
        }
    }
}
