using Bannerlord.UIExtenderEx.Attributes;
using Bannerlord.UIExtenderEx.Prefabs2;

namespace FeudalInternalAffairs
{
    // 政治地图: 各国领土中心的大字国家名(画在名牌之上)
    [PrefabExtension("PartyNameplate", "descendant::Widget[@DataSource='{Nameplates}']")]
    internal class TerritoryLabelPrefabExtension : PrefabExtensionInsertPatch
    {
        public override InsertType Type => InsertType.Append;

        [PrefabExtensionText]
        public string GetPrefabExtension()
        {
            return "<Widget DataSource=\"{TintLabels}\" DoNotAcceptEvents=\"true\" IsDisabled=\"true\" " +
                   "WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"StretchToParent\">" +
                   "<ItemTemplate>" +
                   "<TextWidget WidthSizePolicy=\"CoverChildren\" HeightSizePolicy=\"CoverChildren\" " +
                   "PositionXOffset=\"@LabelX\" PositionYOffset=\"@LabelY\" " +
                   "Brush=\"MapTextBrushGal\" Brush.FontSize=\"@LabelFontSize\" Brush.FontColor=\"@LabelColor\" " +
                   "Text=\"@LabelText\" />" +
                   "</ItemTemplate>" +
                   "</Widget>";
        }
    }
}
