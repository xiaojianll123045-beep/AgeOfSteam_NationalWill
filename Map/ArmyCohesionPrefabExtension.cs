using Bannerlord.UIExtenderEx.Attributes;
using Bannerlord.UIExtenderEx.Prefabs2;

namespace FeudalInternalAffairs
{
    // 军团凝聚度: 在军团部队名板上方显示"凝聚度 N%"
    [PrefabExtension("PartyNameplate", "descendant::Widget[@DataSource='{Nameplates}']")]
    internal class ArmyCohesionPrefabExtension : PrefabExtensionInsertPatch
    {
        public override InsertType Type => InsertType.Append;

        [PrefabExtensionText]
        public string GetPrefabExtension()
        {
            return "<Widget DataSource=\"{ArmyLabels}\" DoNotAcceptEvents=\"true\" IsDisabled=\"true\" " +
                   "WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"StretchToParent\">" +
                   "<ItemTemplate>" +
                   "<TextWidget WidthSizePolicy=\"CoverChildren\" HeightSizePolicy=\"CoverChildren\" " +
                   "PositionXOffset=\"@LabelX\" PositionYOffset=\"@LabelY\" " +
                   "Brush=\"MapTextBrushGal\" Brush.FontSize=\"18\" Brush.FontColor=\"#FFFFFFFF\" " +
                   "Text=\"@LabelText\" />" +
                   "</ItemTemplate>" +
                   "</Widget>";
        }
    }
}
