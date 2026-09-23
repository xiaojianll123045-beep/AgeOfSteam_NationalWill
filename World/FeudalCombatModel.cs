using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
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
