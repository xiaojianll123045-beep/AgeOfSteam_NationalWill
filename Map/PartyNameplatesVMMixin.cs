using System;
using Bannerlord.UIExtenderEx.Attributes;
using Bannerlord.UIExtenderEx.ViewModels;
using SandBox.ViewModelCollection.Nameplate;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Library;

namespace FeudalInternalAffairs
{
    // 选中圈: 一个位置VM, 供预制体里的 ItemTemplate 使用
    public class SelectionRingVM : ViewModel
    {
        private float _x;
        private float _y;

        [DataSourceProperty]
        public float RingX
        {
            get { return _x; }
            set { if (Math.Abs(_x - value) > 0.5f) { _x = value; OnPropertyChangedWithValue(value, "RingX"); } }
        }

        [DataSourceProperty]
        public float RingY
        {
            get { return _y; }
            set { if (Math.Abs(_y - value) > 0.5f) { _y = value; OnPropertyChangedWithValue(value, "RingY"); } }
        }
    }

    // 领土色块: 一个屏幕实心方块(位置/大小/颜色由 TerritoryColorMode 计算)
    public class TintTileVM : ViewModel
    {
        private float _x, _y, _size;
        private string _color = "#FFFFFF00";

        [DataSourceProperty]
        public float TileX
        {
            get { return _x; }
            set { if (Math.Abs(_x - value) > 0.5f) { _x = value; OnPropertyChangedWithValue(value, "TileX"); } }
        }

        [DataSourceProperty]
        public float TileY
        {
            get { return _y; }
            set { if (Math.Abs(_y - value) > 0.5f) { _y = value; OnPropertyChangedWithValue(value, "TileY"); } }
        }

        [DataSourceProperty]
        public float TileSize
        {
            get { return _size; }
            set { if (Math.Abs(_size - value) > 0.5f) { _size = value; OnPropertyChangedWithValue(value, "TileSize"); } }
        }

        [DataSourceProperty]
        public string TileColor
        {
            get { return _color; }
            set { if (_color != value) { _color = value; OnPropertyChangedWithValue(value, "TileColor"); } }
        }
    }

    // 地图上的大字国家名(领土中心)
    public class TintLabelVM : ViewModel
    {
        private float _x, _y;
        private string _text = "";
        private string _color = "#FFFFFFFF";
        private int _fontSize = 60;

        [DataSourceProperty]
        public float LabelX
        {
            get { return _x; }
            set { if (Math.Abs(_x - value) > 0.5f) { _x = value; OnPropertyChangedWithValue(value, "LabelX"); } }
        }

        [DataSourceProperty]
        public float LabelY
        {
            get { return _y; }
            set { if (Math.Abs(_y - value) > 0.5f) { _y = value; OnPropertyChangedWithValue(value, "LabelY"); } }
        }

        [DataSourceProperty]
        public string LabelText
        {
            get { return _text; }
            set { if (_text != value) { _text = value; OnPropertyChangedWithValue(value, "LabelText"); } }
        }

        [DataSourceProperty]
        public string LabelColor
        {
            get { return _color; }
            set { if (_color != value) { _color = value; OnPropertyChangedWithValue(value, "LabelColor"); } }
        }

        [DataSourceProperty]
        public int LabelFontSize
        {
            get { return _fontSize; }
            set { if (_fontSize != value) { _fontSize = value; OnPropertyChangedWithValue(value, "LabelFontSize"); } }
        }
    }

    // 军团凝聚度标签(显示在军团部队名板上方)
    public class ArmyLabelVM : ViewModel
    {
        private float _x, _y;
        private string _text = "";

        [DataSourceProperty]
        public float LabelX
        {
            get { return _x; }
            set { if (Math.Abs(_x - value) > 0.5f) { _x = value; OnPropertyChangedWithValue(value, "LabelX"); } }
        }

        [DataSourceProperty]
        public float LabelY
        {
            get { return _y; }
            set { if (Math.Abs(_y - value) > 0.5f) { _y = value; OnPropertyChangedWithValue(value, "LabelY"); } }
        }

        [DataSourceProperty]
        public string LabelText
        {
            get { return _text; }
            set { if (_text != value) { _text = value; OnPropertyChangedWithValue(value, "LabelText"); } }
        }
    }

    // 部队名字板管理器: ①暴露给框选 ②框选矩形数据 ③选中圈列表 ④政治地图色块
    [ViewModelMixin("Update")]
    public class PartyNameplatesVMMixin : BaseViewModelMixin<PartyNameplatesVM>
    {
        internal static PartyNameplatesVMMixin Instance;

        // 名字板管理器本体(供框选/刷新使用)
        internal PartyNameplatesVM Manager
        {
            get { return ViewModel; }
        }

        private bool _visible;
        private float _left, _top, _width, _height, _right, _bottom;
        private string _bigName = "";
        private bool _showBigName;
        private string _bigColor = "#FFFFFFFF";

        public PartyNameplatesVMMixin(PartyNameplatesVM vm) : base(vm)
        {
            Instance = this;
        }

        [DataSourceProperty]
        public MBBindingList<SelectionRingVM> SelectionRings { get; } = new MBBindingList<SelectionRingVM>();

        // 政治地图色块列表
        [DataSourceProperty]
        public MBBindingList<TintTileVM> TintTiles { get; } = new MBBindingList<TintTileVM>();

        // 政治地图大字国名列表
        [DataSourceProperty]
        public MBBindingList<TintLabelVM> TintLabels { get; } = new MBBindingList<TintLabelVM>();

        // 军团凝聚度标签
        [DataSourceProperty]
        public MBBindingList<ArmyLabelVM> ArmyLabels { get; } = new MBBindingList<ArmyLabelVM>();

        // ===== 政治地图大标题(最大放大时显示当前所在国家名) =====
        [DataSourceProperty]
        public string BigKingdomName
        {
            get { return _bigName; }
            set { if (_bigName != value) { _bigName = value; OnPropertyChangedWithValue(value, "BigKingdomName"); } }
        }

        [DataSourceProperty]
        public bool ShowBigKingdomName
        {
            get { return _showBigName; }
            set { if (_showBigName != value) { _showBigName = value; OnPropertyChangedWithValue(value, "ShowBigKingdomName"); } }
        }

        [DataSourceProperty]
        public string BigKingdomColor
        {
            get { return _bigColor; }
            set { if (_bigColor != value) { _bigColor = value; OnPropertyChangedWithValue(value, "BigKingdomColor"); } }
        }

        public override void OnRefresh()
        {
            try
            {
                var v = ViewModel;
                if (v == null) return;

                // 框选矩形
                bool vis = MapBoxSelect.BoxVisible;
                float l = MapBoxSelect.BoxLeft, t = MapBoxSelect.BoxTop, w = MapBoxSelect.BoxWidth, h = MapBoxSelect.BoxHeight;
                if (vis != _visible) { _visible = vis; OnPropertyChangedWithValue(vis, "SelectionBoxVisible"); }
                if (Math.Abs(l - _left) > 0.5f) { _left = l; OnPropertyChangedWithValue(l, "SelectionBoxLeft"); }
                if (Math.Abs(t - _top) > 0.5f) { _top = t; OnPropertyChangedWithValue(t, "SelectionBoxTop"); }
                if (Math.Abs(w - _width) > 0.5f) { _width = w; OnPropertyChangedWithValue(w, "SelectionBoxWidth"); }
                if (Math.Abs(h - _height) > 0.5f) { _height = h; OnPropertyChangedWithValue(h, "SelectionBoxHeight"); }
                float r = l + w, b = t + h;
                if (Math.Abs(r - _right) > 0.5f) { _right = r; OnPropertyChangedWithValue(r, "SelectionBoxRight"); }
                if (Math.Abs(b - _bottom) > 0.5f) { _bottom = b; OnPropertyChangedWithValue(b, "SelectionBoxBottom"); }

                // 选中圈: 跟着选中部队的名字板位置走
                SyncRings(v);

                // 屏幕中央的大字国名: 已按需求移除(只在各国领土中心显示国名)
                if (_showBigName) ShowBigKingdomName = false;

                // 政治地图色块
                SyncTiles();
                SyncLabels();

                // 军团凝聚度
                SyncArmyLabels(v);
            }
            catch { }
        }

        private void SyncTiles()
        {
            try
            {
                var src = TerritoryColorMode.Tiles;
                while (TintTiles.Count < src.Count) TintTiles.Add(new TintTileVM());
                while (TintTiles.Count > src.Count) TintTiles.RemoveAt(TintTiles.Count - 1);
                for (int i = 0; i < src.Count; i++)
                {
                    var s = src[i];
                    var t = TintTiles[i];
                    if (Math.Abs(t.TileX - s.X) > 0.5f) t.TileX = s.X;
                    if (Math.Abs(t.TileY - s.Y) > 0.5f) t.TileY = s.Y;
                    if (Math.Abs(t.TileSize - s.Size) > 0.5f) t.TileSize = s.Size;
                    if (t.TileColor != s.Color) t.TileColor = s.Color;
                }
            }
            catch { }
        }

        private void SyncLabels()
        {
            try
            {
                var src = TerritoryColorMode.Labels;
                while (TintLabels.Count < src.Count) TintLabels.Add(new TintLabelVM());
                while (TintLabels.Count > src.Count) TintLabels.RemoveAt(TintLabels.Count - 1);
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

        private void SyncArmyLabels(PartyNameplatesVM manager)
        {
            try
            {
                if (manager.Nameplates == null) return;
                bool show = !TerritoryColorMode.HideNameplates;
                int used = 0;
                foreach (var np in manager.Nameplates)
                {
                    if (np == null || np.Party == null) continue;
                    var mp = np.Party;   // PartyNameplateVM.Party 本身就是 MobileParty
                    if (mp == null) continue;
                    var army = mp.Army;
                    if (army == null) continue;
                    if (!show) break;
                    if (!NationalWillOrders.IsOurs(mp)) continue;   // 敌方/中立方不显示凝聚力
                    var pos = np.Position;
                    string txt = "凝聚度 " + ((int)army.Cohesion) + "%";
                    if (used < ArmyLabels.Count)
                    {
                        var it = ArmyLabels[used];
                        it.LabelX = pos.X;
                        it.LabelY = pos.Y - 34f;
                        it.LabelText = txt;
                    }
                    else
                    {
                        var it = new ArmyLabelVM();
                        it.LabelX = pos.X;
                        it.LabelY = pos.Y - 34f;
                        it.LabelText = txt;
                        ArmyLabels.Add(it);
                    }
                    used++;
                }
                while (ArmyLabels.Count > used) ArmyLabels.RemoveAt(ArmyLabels.Count - 1);
            }
            catch { }
        }

        private void SyncRings(PartyNameplatesVM manager)
        {
            try
            {
                if (manager.Nameplates == null) return;
                int used = 0;
                foreach (var np in manager.Nameplates)
                {
                    if (np == null || np.Party == null) continue;
                    if (!MapSelection.Is(np.Party)) continue;

                    var pos = np.Position;
                    // 环以名板为中心(名板坐标是左上角, 环 48x48)
                    float rx = pos.X - 24f;
                    float ry = pos.Y - 24f;
                    if (used < SelectionRings.Count)
                    {
                        SelectionRings[used].RingX = rx;
                        SelectionRings[used].RingY = ry;
                    }
                    else
                    {
                        var item = new SelectionRingVM();
                        item.RingX = rx;
                        item.RingY = ry;
                        SelectionRings.Add(item);
                    }
                    used++;
                }
                while (SelectionRings.Count > used) SelectionRings.RemoveAt(SelectionRings.Count - 1);
            }
            catch { }
        }
    }
}
