using Bannerlord.UIExtenderEx.Attributes;
using Bannerlord.UIExtenderEx.Prefabs2;

namespace FeudalInternalAffairs
{
    // 政治地图大标题: 屏幕正中显示当前所在国家名(只在最大放大时出现)
    [PrefabExtension("PartyNameplate", "descendant::Widget[@DataSource='{Nameplates}']")]
    internal class BigKingdomNamePrefabExtension : PrefabExtensionInsertPatch
    {
        public override InsertType Type => InsertType.Append;

        [PrefabExtensionText]
        public string GetPrefabExtension()
        {
            return "<TextWidget WidthSizePolicy=\"CoverChildren\" HeightSizePolicy=\"CoverChildren\" " +
                   "HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\" " +
                   "Brush=\"MapTextBrushGal\" Brush.FontSize=\"110\" Brush.FontColor=\"@BigKingdomColor\" " +
                   "IsVisible=\"@ShowBigKingdomName\" Text=\"@BigKingdomName\" />";
        }
    }
}
