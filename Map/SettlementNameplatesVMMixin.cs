using System;
using Bannerlord.UIExtenderEx.Attributes;
using Bannerlord.UIExtenderEx.ViewModels;
using SandBox.ViewModelCollection.Nameplate;
using TaleWorlds.Library;

namespace FeudalInternalAffairs
{
    // 抓住定居点名板管理器: ①加入国家后需要立刻刷新名板颜色 ②承载"国家名/数据标签"在最顶层渲染
    // v4.75m: hook 从 RefreshValues 改成 Update(每帧) —— 标签数据必须每帧同步, RefreshValues 是低频的,
    //          迁层后曾导致国名一直不同步而不显示
    [ViewModelMixin("Update")]
    public class SettlementNameplatesVMMixin : BaseViewModelMixin<SettlementNameplatesVM>
    {
        internal static SettlementNameplatesVM Instance;
        internal static SettlementNameplatesVMMixin Mixin;   // 自身实例(供外部每帧驱动同步)

        public SettlementNameplatesVMMixin(SettlementNameplatesVM vm) : base(vm)
        {
            Instance = vm;
            Mixin = this;
            DLog.Force("定居点名板 mixin 已注册");
        }

        // 供外部(TerritoryColorMode)每帧驱动: 把国名/数值标签同步进 TintLabels(双保险)
        internal static void SyncFromTerritory()
        {
            try { if (Mixin != null) Mixin.SyncLabels(); }
            catch { }
        }

        // v4.69: 供地图数据模式读取每个定居点名板(屏幕坐标/定居点)
        internal SettlementNameplatesVM Manager { get { return ViewModel; } }

        // v4.75k: 国名/数值标签(从 TerritoryColorMode 同步; 渲染在定居点名板 prefab 最顶部)
        [DataSourceProperty]
        public MBBindingList<TintLabelVM> TintLabels { get; } = new MBBindingList<TintLabelVM>();

        public override void OnRefresh()
        {
            try
            {
                Instance = ViewModel;
                SyncLabels();

                // v4.79i: 在"城市名牌每帧更新"的同一时机重算并同步国名标签(与名牌同帧同序, 根治移动抖动)
                TerritoryColorMode.RefreshLabelsNow();
                FeudalMapLabels.SyncNow();
            }
            catch { }
        }

        private bool _loggedSync;

        private void SyncLabels()
        {
            try
            {
                var src = TerritoryColorMode.Labels;
                while (TintLabels.Count < src.Count) TintLabels.Add(new TintLabelVM());
                while (TintLabels.Count > src.Count) TintLabels.RemoveAt(TintLabels.Count - 1);
                if (!_loggedSync && TintLabels.Count > 0)
                {
                    _loggedSync = true;
                    DLog.Force("顶层标签: 已同步 " + TintLabels.Count + " 个国名/数值到定居点名板层");
                }
                for (int i = 0; i < src.Count; i++)
                {
                    var s = src[i];
                    var t = TintLabels[i];
                    if (Math.Abs(t.LabelX - s.X) > 0.5f) t.LabelX = s.X;
                    if (Math.Abs(t.LabelY - s.Y) > 0.5f) t.LabelY = s.Y;
                    if (t.LabelText != s.Text) t.LabelText = s.Text;
                    if (t.LabelColor != s.Color) t.LabelColor = s.Color;
                    if (t.LabelFontSize != s.FontSize) t.LabelFontSize = s.FontSize;
                }
            }
            catch { }
        }
    }
}
