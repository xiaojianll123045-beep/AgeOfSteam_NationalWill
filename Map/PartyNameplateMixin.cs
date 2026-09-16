using System;
using Bannerlord.UIExtenderEx.Attributes;
using Bannerlord.UIExtenderEx.ViewModels;
using SandBox.ViewModelCollection.Nameplate;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;

namespace FeudalInternalAffairs
{
    // 隐藏玩家部队在地图上的旗子/人数/名字(旧项目"当兵"同款做法)
    // 配套: GUI/Prefabs/PartyPlayerNameplateItem.xml 里根控件 IsVisible="@ShowPlayerNameplate"
    // 注意: 不要用 Harmony 补 PartyNameplateVM 的虚方法, 会崩角色创建(旧项目实测)。
    [ViewModelMixin("RefreshBinding")]
    public class PartyPlayerNameplateVMMixin : BaseViewModelMixin<PartyPlayerNameplateVM>
    {
        private static bool _loggedCtor;
        private static bool _loggedHide;

        public PartyPlayerNameplateVMMixin(PartyPlayerNameplateVM vm) : base(vm)
        {
            if (!_loggedCtor)
            {
                _loggedCtor = true;
                DLog.Force("玩家名字板 mixin 已注册");
            }
        }

        [DataSourceProperty]
        public bool ShowPlayerNameplate
        {
            get
            {
                try
                {
                    if (ViewModel == null || !ViewModel.IsMainParty) return true;
                    var behavior = Campaign.Current != null ? Campaign.Current.GetCampaignBehavior<NationalWillBehavior>() : null;
                    if (behavior == null || !behavior.IsNationalWill) return true;
                    if (!_loggedHide)
                    {
                        _loggedHide = true;
                        DLog.Force("玩家部队名字板: 开始隐藏");
                    }
                    return false;
                }
                catch { return true; }
            }
        }

        public override void OnRefresh()
        {
            try
            {
                bool show = ShowPlayerNameplate;
                if (!show && ViewModel != null && ViewModel.IsVisibleOnMap)
                {
                    ViewModel.IsVisibleOnMap = false;
                }
                OnPropertyChangedWithValue(show, "ShowPlayerNameplate");
            }
            catch { }
        }
    }
}
