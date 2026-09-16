using Bannerlord.UIExtenderEx.Attributes;
using Bannerlord.UIExtenderEx.Prefabs2;

namespace FeudalInternalAffairs
{
    // 政治地图: 往"部队名字板容器"最底层注入领土色块(实心方块, 纯色填充)
    [PrefabExtension("PartyNameplate", "descendant::Widget[@DataSource='{Nameplates}']")]
    internal class TerritoryTintPrefabExtension : PrefabExtensionInsertPatch
    {
        public override InsertType Type => InsertType.Prepend;

        [PrefabExtensionText]
        public string GetPrefabExtension()
        {
            // 注意: 必须用普通 Widget 容器(绝对定位); 用 ListPanel 会被堆叠布局吃掉偏移,
            // 结果就是色块像贴在屏幕上一样不跟着地图走
            return "<Widget DataSource=\"{TintTiles}\" DoNotAcceptEvents=\"true\" IsDisabled=\"true\" " +
                   "WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"StretchToParent\">" +
                   "<ItemTemplate>" +
                   "<Widget WidthSizePolicy=\"Fixed\" HeightSizePolicy=\"Fixed\" " +
                   "SuggestedWidth=\"@TileSize\" SuggestedHeight=\"@TileSize\" " +
                   "PositionXOffset=\"@TileX\" PositionYOffset=\"@TileY\" " +
                   "Sprite=\"BlankWhiteSquare\" Color=\"@TileColor\" />" +
                   "</ItemTemplate>" +
                   "</Widget>";
        }
    }
}
