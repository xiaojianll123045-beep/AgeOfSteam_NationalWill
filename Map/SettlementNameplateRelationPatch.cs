using System;
using System.Reflection;
using HarmonyLib;
using SandBox.ViewModelCollection.Nameplate;

namespace FeudalInternalAffairs
{
    // 城市/城堡/村庄名牌的"敌我表现"同步(纯原版逻辑, 我们自己不画任何东西)
    //
    // 原版: SettlementNameplateVM.RefreshRelationStatus() 用 Hero.MainHero.MapFaction 算关系
    //   (0=中立 1=我方 2=交战 3=盟友), 但只写私有字段 _bindRelation;
    //   界面靠公开属性 Relation 才会更新原版名牌自己的敌我表现(背景图/明暗)。
    // 这里只做"重算 + 写回 Relation", 名字字体等全部保持原版。
    internal static class SettlementNameplateRelationPatch
    {
        private static readonly FieldInfo BindRelationField = typeof(SettlementNameplateVM)
            .GetField("_bindRelation", BindingFlags.Instance | BindingFlags.NonPublic);

        private static bool _loggedOnce;

        private static void Sync(SettlementNameplateVM vm)
        {
            try
            {
                var behavior = NationalWillOrders.Behavior;
                if (behavior == null || !behavior.IsNationalWill) return;
                if (vm == null || vm.Settlement == null || vm.Settlement.OwnerClan == null) return;

                vm.RefreshRelationStatus();
                if (BindRelationField == null) return;
                int live = (int)BindRelationField.GetValue(vm);
                if (vm.Relation != live)
                {
                    vm.Relation = live;
                    if (!_loggedOnce)
                    {
                        _loggedOnce = true;
                        DLog.Force("城市名牌关系已同步(关系值 " + live + ")");
                    }
                }
            }
            catch { }
        }

        [HarmonyPatch(typeof(SettlementNameplateVM), "RefreshValues")]
        internal static class SyncOnRefreshValues
        {
            private static void Postfix(SettlementNameplateVM __instance)
            {
                Sync(__instance);
            }
        }

        [HarmonyPatch(typeof(SettlementNameplateVM), "RefreshDynamicProperties")]
        internal static class SyncOnDynamicProperties
        {
            private static void Postfix(SettlementNameplateVM __instance)
            {
                Sync(__instance);
            }
        }
    }
}
