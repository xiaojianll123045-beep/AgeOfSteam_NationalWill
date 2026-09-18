using System;
using SandBox.View.Map;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.Library;

namespace FeudalInternalAffairs
{
    public class NavRailVM : ViewModel
    {
        private bool _tipVisible;
        private string _tipText = "";
        private float _tipY;

        [DataSourceProperty] public bool TipVisible { get { return _tipVisible; } }
        [DataSourceProperty] public string TipText { get { return _tipText; } }
        [DataSourceProperty] public float TipY { get { return _tipY; } }

        internal void SetTip(int idx)
        {
            try
            {
                bool vis = idx >= 0;
                string txt = vis ? NavRail.NameOf(idx) : _tipText;
                float y = vis ? NavRail.TipYOf(idx) : _tipY;
                bool textChanged = vis != _tipVisible || txt != _tipText;
                _tipVisible = vis; _tipText = txt;
                if (textChanged)
                {
                    OnPropertyChanged("TipVisible");
                    OnPropertyChanged("TipText");
                }
                if (Math.Abs(_tipY - y) > 0.5f) { _tipY = y; OnPropertyChanged("TipY"); }
            }
            catch { }
        }
    }

    // 左侧导航栏(参照维多利亚3左侧图标栏; 文档 19.14)
    // 与国旗同款: 层收不到点击 -> 自己轮询鼠标左键
    internal static class NavRail
    {
        private const string Movie = "FeudalNavRail";
        private const float X = 6f;        // 图标左边界
        private const float Top = 150f;    // 第一个图标 y
        private const float Size = 44f;    // 图标尺寸
        private const float Step = 54f;    // 图标间距

        private static GauntletLayer _layer;
        private static NavRailVM _vm;
        private static MapScreen _map;

        // 槽位顺序需与 FeudalNavRail.xml 中的 y 一致(150 + 54*i)
        private const int SlotNation = 0;
        private const int SlotPop = 1;
        private const int SlotBuild = 2;
        private const int SlotMarket = 3;
        private const int SlotDiplo = 4;
        private const int SlotFocus = 5;
        private const int SlotFiscal = 6;
        private const int SlotSociety = 7;
        private const int SlotGuild = 8;
        private const int SlotStats = 9;
        private const int SlotPolitics = 10;
        private const int SlotPlay = 11;
        private const int SlotCount = 12;

        // 悬停名称(顺序与槽位一致)
        internal static readonly string[] Names = { "国家", "人口", "建筑", "市场", "外交", "国策", "财政", "社会", "行会", "统计", "政治", "博弈" };

        internal static string NameOf(int idx) { return idx >= 0 && idx < Names.Length ? Names[idx] : ""; }

        internal static float TipYOf(int idx) { return Top + idx * Step + 2f; }

        internal static void Tick(float dt)
        {
            try
            {
                EnsureLayer();
                if (_layer == null) return;
                var m = TaleWorlds.InputSystem.Input.MousePositionPixel;
                int hover = -1;
                if (m.X >= X - 4f && m.X <= X + Size + 4f && m.Y >= Top - 2f)
                {
                    int idx = (int)((m.Y - Top) / Step);
                    if (idx >= 0 && idx < SlotCount && (m.Y - Top) - idx * Step <= Size + 6f) hover = idx;
                }
                if (_vm != null) _vm.SetTip(hover);
                if (hover < 0) return;
                if (!TaleWorlds.InputSystem.Input.IsKeyPressed(TaleWorlds.InputSystem.InputKey.LeftMouseButton)) return;
                Open(hover);
            }
            catch { }
        }

        private static void Open(int idx)
        {
            try
            {
                // 再点一次同一个图标 = 关闭当前页面
                if (IsSlotOpen(idx)) { CloseSlot(idx); return; }
                // 国策树是独立全屏页: 打开别的页面前先把它收掉
                if (idx != SlotFocus && FocusTreeScreen.IsOpen) FocusTreeScreen.Close();
                switch (idx)
                {
                    case SlotNation: NationalPanel.OpenSidebar(); break;
                    case SlotPop: PopPanel.Open(); break;
                    case SlotBuild: BuildRegPanel.Open(); break;
                    case SlotMarket: MarketPanel.Open(null); break;
                    case SlotDiplo: DiplomacyPanel.Open(null); break;
                    case SlotFocus:
                        {
                            var ck = PanelScreen.CurrentKey();
                            if (ck != null) PanelScreen.ClosePanel(ck);   // 侧边栏 -> 全屏国策树
                            FocusTreeScreen.Open();
                        }
                        break;
                    case SlotFiscal: FiscalPanel.Open(); break;
                    case SlotSociety: SocietyPanel.Open(); break;
                    case SlotGuild: GuildPanel.Open(); break;
                    case SlotStats: StatsPanel.Open(); break;
                    case SlotPolitics: PoliticsPanel.Open(); break;
                    case SlotPlay: DiploPlayPanel.Open(); break;
                }
            }
            catch (Exception ex) { DLog.Force("导航栏打开页面失败(" + idx + "): " + ex.Message); }
        }

        private static bool IsSlotOpen(int idx)
        {
            switch (idx)
            {
                case SlotNation: return NationalPanel.IsOpen;
                case SlotPop: return PopPanel.IsOpen;
                case SlotBuild: return BuildRegPanel.IsOpen;
                case SlotMarket: return MarketPanel.IsOpen;
                case SlotDiplo: return DiplomacyPanel.IsOpen;
                case SlotFocus: return FocusTreeScreen.IsOpen;
                case SlotFiscal: return FiscalPanel.IsOpen;
                case SlotSociety: return SocietyPanel.IsOpen;
                case SlotGuild: return GuildPanel.IsOpen;
                case SlotStats: return StatsPanel.IsOpen;
                case SlotPolitics: return PoliticsPanel.IsOpen;
                case SlotPlay: return DiploPlayPanel.IsOpen;
            }
            return false;
        }

        private static void CloseSlot(int idx)
        {
            try
            {
                switch (idx)
                {
                    case SlotNation: NationalPanel.CloseSidebar(); break;
                    case SlotPop: PopPanel.Close(); break;
                    case SlotBuild: BuildRegPanel.Close(); break;
                    case SlotMarket: MarketPanel.Close(); break;
                    case SlotDiplo: DiplomacyPanel.Close(); break;
                    case SlotFocus: FocusTreeScreen.Close(); break;
                    case SlotFiscal: FiscalPanel.Close(); break;
                    case SlotSociety: SocietyPanel.Close(); break;
                    case SlotGuild: GuildPanel.Close(); break;
                    case SlotStats: StatsPanel.Close(); break;
                    case SlotPolitics: PoliticsPanel.Close(); break;
                    case SlotPlay: DiploPlayPanel.Close(); break;
                }
            }
            catch (Exception ex) { DLog.Force("导航栏关闭页面失败(" + idx + "): " + ex.Message); }
        }

        private static void EnsureLayer()
        {
            try
            {
                var map = MapScreen.Instance;
                if (map == null)
                {
                    if (_layer != null) CloseLayer();
                    return;
                }
                if (_layer != null) return;
                if (!NationalWillOrders.ShouldControlCamera) return;
                _vm = new NavRailVM();
                _layer = new GauntletLayer("FeudalNavRail", 330, false);   // 深度需高于侧边栏面板(320), 悬停名称才能盖在面板上
                _layer.LoadMovie(Movie, _vm);
                map.AddLayer(_layer);
                _map = map;
                DLog.Force("导航栏: 已挂到地图(左侧)");
            }
            catch (Exception ex)
            {
                DLog.Force("导航栏挂载失败: " + ex.Message);
                try { if (_layer != null && _map != null) _map.RemoveLayer(_layer); } catch { }
                _layer = null; _vm = null; _map = null;
            }
        }

        private static void CloseLayer()
        {
            try
            {
                if (_layer != null && _map != null) _map.RemoveLayer(_layer);
            }
            catch { }
            _layer = null; _vm = null; _map = null;
        }
    }
}
