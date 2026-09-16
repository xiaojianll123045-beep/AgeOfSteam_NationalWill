using Bannerlord.UIExtenderEx.Attributes;
using Bannerlord.UIExtenderEx.Prefabs2;

namespace FeudalInternalAffairs
{
    // 往"部队名字板容器"预制体里注入框选矩形 —— 只描边不填充(4条细边)。
    // 容器预制体 PartyNameplate 的全屏 Widget DataSource 就是 PartyNameplatesVM,
    // 挂它上面的 mixin 已确认能注册成功, 绑定必然解析。
    [PrefabExtension("PartyNameplate", "descendant::Widget[@DataSource='{Nameplates}']")]
    internal class NameplateSelectionBoxPrefabExtension : PrefabExtensionInsertPatch
    {
        private static bool _logged;

        public override InsertType Type => InsertType.Append;

        [PrefabExtensionText]
        public string GetPrefabExtension()
        {
            if (!_logged) { _logged = true; DLog.Force("框选UI: 预制体扩展已请求(PartyNameplate, 描边式)"); }
            const string edge = "<Widget Sprite=\"BlankWhiteSquare\" Color=\"#CCFFFFFF\" WidthSizePolicy=\"Fixed\" HeightSizePolicy=\"Fixed\" ";
            return "<Widget DoNotAcceptEvents=\"true\" IsDisabled=\"true\" IsVisible=\"@SelectionBoxVisible\" " +
                   "WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"StretchToParent\">" +
                   "<Children>" +
                   edge + "PositionXOffset=\"@SelectionBoxLeft\" PositionYOffset=\"@SelectionBoxTop\" SuggestedWidth=\"@SelectionBoxWidth\" SuggestedHeight=\"2\" />" +
                   edge + "PositionXOffset=\"@SelectionBoxLeft\" PositionYOffset=\"@SelectionBoxBottom\" SuggestedWidth=\"@SelectionBoxWidth\" SuggestedHeight=\"2\" />" +
                   edge + "PositionXOffset=\"@SelectionBoxLeft\" PositionYOffset=\"@SelectionBoxTop\" SuggestedWidth=\"2\" SuggestedHeight=\"@SelectionBoxHeight\" />" +
                   edge + "PositionXOffset=\"@SelectionBoxRight\" PositionYOffset=\"@SelectionBoxTop\" SuggestedWidth=\"2\" SuggestedHeight=\"@SelectionBoxHeight\" />" +
                   "</Children>" +
                   "</Widget>";
        }
    }
}
