using Bannerlord.UIExtenderEx.Attributes;
using Bannerlord.UIExtenderEx.Prefabs2;

namespace FeudalInternalAffairs
{
    // 往"部队名字板容器"里注入选中圈列表(用原版 tracked_ring 贴图, 位置来自 SelectionRings)
    // 插到 {Nameplates} 列表的"最前面" -> 先绘制 = 在名称/人数/旗帜的下层
    [PrefabExtension("PartyNameplate", "descendant::Widget[@DataSource='{Nameplates}']")]
    internal class NameplateSelectionRingPrefabExtension : PrefabExtensionInsertPatch
    {
        private static bool _logged;

        public override InsertType Type => InsertType.Prepend;

        [PrefabExtensionText]
        public string GetPrefabExtension()
        {
            if (!_logged) { _logged = true; DLog.Force("选中圈: 预制体扩展已请求"); }
            // 用普通 Widget 容器(绝对定位), 不要用 ListPanel(会被堆叠布局吃掉偏移)
            return "<Widget DataSource=\"{SelectionRings}\" DoNotAcceptEvents=\"true\" IsDisabled=\"true\" " +
                   "WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"StretchToParent\">" +
                   "<ItemTemplate>" +
                   "<Widget WidthSizePolicy=\"Fixed\" HeightSizePolicy=\"Fixed\" SuggestedWidth=\"48\" SuggestedHeight=\"48\" " +
                   "PositionXOffset=\"@RingX\" PositionYOffset=\"@RingY\" " +
                   "Sprite=\"SPGeneral\\Nameplates\\tracked_ring\" Color=\"#CCFFFFFF\" />" +
                   "</ItemTemplate>" +
                   "</Widget>";
        }
    }
}
