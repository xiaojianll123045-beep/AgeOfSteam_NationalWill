using System;
using HarmonyLib;
using SandBox.View.Map;

namespace FeudalInternalAffairs
{
    // 国家意志身份下, 取缔"个人事务"页面的快捷键
    //   原版: I=物品栏 P=队伍 V=捏脸 L=家族 J=任务 C=角色 B=旗帜编辑
    //   保留: K=王国, N=百科, Esc=系统菜单
    internal static class NationalWillHotkeyPatches
    {
        private static bool Block(string what)
        {
            try
            {
                if (!NationalWillOrders.IsActive) return false;
                MapSelection.Message("国家意志不需要处理" + what + "事务");
                DLog.Info("已拦截快捷键: " + what);
                return true;
            }
            catch { return false; }
        }

        [HarmonyPatch(typeof(MapScreen), "OpenInventory")]
        internal static class BlockInventory
        {
            private static bool Prefix() { return !Block("物品栏"); }
        }

        [HarmonyPatch(typeof(MapScreen), "OpenParty")]
        internal static class BlockParty
        {
            private static bool Prefix() { return !Block("队伍"); }
        }

        [HarmonyPatch(typeof(MapScreen), "OpenClanScreen")]
        internal static class BlockClan
        {
            private static bool Prefix() { return !Block("家族"); }
        }

        [HarmonyPatch(typeof(MapScreen), "OpenQuestsScreen")]
        internal static class BlockQuests
        {
            private static bool Prefix() { return !Block("任务"); }
        }

        [HarmonyPatch(typeof(MapScreen), "OpenCharacterDevelopmentScreen")]
        internal static class BlockCharacter
        {
            private static bool Prefix() { return !Block("角色"); }
        }

        [HarmonyPatch(typeof(MapScreen), "OpenFaceGeneratorScreen")]
        internal static class BlockFaceGen
        {
            private static bool Prefix() { return !Block("捏脸"); }
        }

        [HarmonyPatch(typeof(MapScreen), "OpenBannerEditorScreen")]
        internal static class BlockBannerEditor
        {
            private static bool Prefix() { return !Block("旗帜编辑"); }
        }
    }
}
