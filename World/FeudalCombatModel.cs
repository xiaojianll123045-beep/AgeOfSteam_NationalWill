using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Naval;      // v4.290: Ship(海战转发用)
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Localization;
using TaleWorlds.Library;

namespace FeudalInternalAffairs
{
    // v4.200: 接管坐镇指挥(自动结算)的伤害计算 —— 高级热武器兵种高伤害
    //   规则: ①兵种阶级 Tier>=4 起按档放大(T4 ×1.35 / T5 ×1.70 / T6 ×2.05 / T7 ×2.40)
    //        ②每完成一项军事树科技 +0.5%(火力/组织度提升)
    //        ③我方君主亲率部队 +5%(指挥加成)
    internal class FeudalCombatModel : DefaultCombatSimulationModel
    {
        private static readonly TextObject _exp = new TextObject("蒸汽时代火力");

        public override ExplainedNumber SimulateHit(CharacterObject strikerTroop, CharacterObject struckTroop, PartyBase strikerParty,
            PartyBase struckParty, float strikerAdvantage, MapEvent battle, float strikerSideMorale, float struckSideMorale)
        {
            var r = base.SimulateHit(strikerTroop, struckTroop, strikerParty, struckParty, strikerAdvantage, battle, strikerSideMorale, struckSideMorale);
            try
            {
                // v4.201: 走 BattleSim 的深度模型(兵种阶级/克制/组织度/科技/条件/宽度)
                string why;
                float mult = BattleSim.DamageMult(strikerTroop, struckTroop, strikerParty, struckParty, battle, out why);
                // 君主亲率加成
                try
                {
                    if (strikerParty != null && strikerParty.LeaderHero != null && Hero.MainHero != null
                        && strikerParty.LeaderHero == Hero.MainHero) mult *= 1.05f;
                }
                catch { }
                if (Math.Abs(mult - 1f) > 0.001f)
                {
                    float extra = r.ResultNumber * (mult - 1f);
                    if (Math.Abs(extra) > 0.001f) r.Add(extra, _exp);
                }
            }
            catch { }
            return r;
        }

        // ==================== v4.290: 转发其余 12 个抽象成员 ====================
        // 背景(已反编译实证): 本类继承的是 DefaultCombatSimulationModel, 而 C# 的 base. 是静态绑定
        //   (IL 里是 call 而非 callvirt)。活动模型链是 [Default, NavalDLC, FeudalCombatModel],
        //   于是 NavalDLCCombatSimulationModel 的 13 个 override 一个都不会被调用 —— 它的 BaseModel
        //   虽然指向 DLC 实例, 但没有任何代码去走它。
        //   海战后果: GetShipSiegeEngineHitChance 落到 Default 的 return 0f
        //     -> RandomFloat < 0f 恒 false -> 船战分支永不进入;
        //   更狠的是 GetSimulationShips 落到 Default 的空列表
        //     -> NumRemainingSimulationShips 恒 0
        //     -> CalculateWinner 在任何 tick 之前就判 AttackerVictory
        //     -> 海战坐镇 = 攻方白赢 + 双方零伤亡 + 无船战。
        // 修法: 全部直通转发给 BaseModel。无 DLC 时 BaseModel 就是 Default, 行为与原版一致;
        //   有 DLC 时链路完整、mod 因子继续在兵打兵生效。不需要引用 NavalDLC
        //   (Ship / SiegeEngineType / MapEventParty 都在 TaleWorlds.CampaignSystem 里)。
        public override ExplainedNumber SimulateHit(Ship strikerShip, Ship struckShip, PartyBase strikerParty,
            PartyBase struckParty, SiegeEngineType siegeEngine, float strikerAdvantage, MapEvent battle, out int troopCasualties)
            => BaseModel.SimulateHit(strikerShip, struckShip, strikerParty, struckParty, siegeEngine, strikerAdvantage, battle, out troopCasualties);

        public override (int defenderRounds, int attackerRounds) GetSimulationTicksForBattleRound(MapEvent mapEvent)
            => BaseModel.GetSimulationTicksForBattleRound(mapEvent);

        public override int GetNumberOfEquipmentsBuilt(Settlement settlement)
            => BaseModel.GetNumberOfEquipmentsBuilt(settlement);

        public override float GetMaximumSiegeEquipmentProgress(Settlement settlement)
            => BaseModel.GetMaximumSiegeEquipmentProgress(settlement);

        public override float GetSettlementAdvantage(Settlement settlement)
            => BaseModel.GetSettlementAdvantage(settlement);

        public override void GetBattleAdvantage(MapEvent mapEvent, out ExplainedNumber defenderAdvantage, out ExplainedNumber attackerAdvantage)
            => BaseModel.GetBattleAdvantage(mapEvent, out defenderAdvantage, out attackerAdvantage);

        public override float GetShipSiegeEngineHitChance(Ship ship, SiegeEngineType siegeEngineType, BattleSideEnum battleSide)
            => BaseModel.GetShipSiegeEngineHitChance(ship, siegeEngineType, battleSide);

        public override int GetPursuitRoundCount(MapEvent mapEvent)
            => BaseModel.GetPursuitRoundCount(mapEvent);

        public override float GetBluntDamageChance(CharacterObject strikerTroop, CharacterObject strikedTroop,
            PartyBase strikerParty, PartyBase strikedParty, MapEvent battle)
            => BaseModel.GetBluntDamageChance(strikerTroop, strikedTroop, strikerParty, strikedParty, battle);

        public override CampaignTime GetSimulationTickInterval(MapEvent mapEvent)
            => BaseModel.GetSimulationTickInterval(mapEvent);

        public override MBList<(Ship, MapEventParty)> GetSimulationShips(MapEvent mapEvent, MBList<MapEventParty> battleParties)
            => BaseModel.GetSimulationShips(mapEvent, battleParties);

        public override int GetParticipatingTroopCount(MapEventSide side)
            => BaseModel.GetParticipatingTroopCount(side);

        // 已完成的军事树科技数(0..58)
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
    }
}
