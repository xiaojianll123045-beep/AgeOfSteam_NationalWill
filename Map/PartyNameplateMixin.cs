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
    // v4.75d: 主队被冻结后游戏不再刷新这个名牌 -> 加 RefreshNow() 供每帧主动发通知(否则隐藏不生效)。
    [ViewModelMixin("RefreshBinding")]
    public class PartyPlayerNameplateVMMixin : BaseViewModelMixin<PartyPlayerNameplateVM>
    {
        internal static PartyPlayerNameplateVMMixin Instance;
        private static bool _loggedCtor;
        private static bool _loggedHide;

        public PartyPlayerNameplateVMMixin(PartyPlayerNameplateVM vm) : base(vm)
        {
            Instance = this;
            if (!_loggedCtor)
            {
                _loggedCtor = true;
                DLog.Force("玩家名字板 mixin 已注册");
            }
        }

        // 每帧主动刷新(由 NationalWillParty.HideNameplate 调用): 冻结的主队不会自己触发刷新
        internal static void RefreshNow()
        {
            try { if (Instance != null) Instance.OnRefresh(); } catch { }
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
                    if (behavior == null) return true;
                    // v4.75e: 选国阶段(还没接管)也要隐藏玩家头像名牌
                    if (!behavior.IsNationalWill && !NationPickMode.Active) return true;
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
