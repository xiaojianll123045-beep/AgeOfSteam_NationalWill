using System;
using Bannerlord.UIExtenderEx.Attributes;
using Bannerlord.UIExtenderEx.ViewModels;
using SandBox.ViewModelCollection.Nameplate;

namespace FeudalInternalAffairs
{
    // 抓住定居点名板管理器: 加入国家后需要立刻刷新名板颜色(否则要等时间流逝才变)
    [ViewModelMixin("RefreshValues")]
    public class SettlementNameplatesVMMixin : BaseViewModelMixin<SettlementNameplatesVM>
    {
        internal static SettlementNameplatesVM Instance;

        public SettlementNameplatesVMMixin(SettlementNameplatesVM vm) : base(vm)
        {
            Instance = vm;
            DLog.Force("定居点名板 mixin 已注册");
        }

        public override void OnRefresh()
        {
            try { Instance = ViewModel; }
            catch { }
        }
    }
}
