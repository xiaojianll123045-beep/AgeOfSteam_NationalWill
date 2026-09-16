using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;

namespace FeudalInternalAffairs
{
    // 一条连接线(细长矩形)
    public class FocusLineVM : ViewModel
    {
        private float _x, _y, _w, _h;
        private string _color = "#FFFFFF66";

        internal FocusLineVM(float x, float y, float w, float h, bool done)
        {
            _x = x; _y = y; _w = w; _h = h;
            _color = done ? "#D6A24A99" : "#FFFFFF55";
        }

        [DataSourceProperty]
        public float X { get { return _x; } set { if (Math.Abs(_x - value) > 0.5f) { _x = value; OnPropertyChangedWithValue(value, "X"); } } }

        [DataSourceProperty]
        public float Y { get { return _y; } set { if (Math.Abs(_y - value) > 0.5f) { _y = value; OnPropertyChangedWithValue(value, "Y"); } } }

        [DataSourceProperty]
        public float W { get { return _w; } set { if (Math.Abs(_w - value) > 0.5f) { _w = value; OnPropertyChangedWithValue(value, "W"); } } }

        [DataSourceProperty]
        public float H { get { return _h; } set { if (Math.Abs(_h - value) > 0.5f) { _h = value; OnPropertyChangedWithValue(value, "H"); } } }

        [DataSourceProperty]
        public string Color { get { return _color; } set { if (_color != value) { _color = value; OnPropertyChangedWithValue(value, "Color"); } } }
    }

    // 单个国策节点
    public class FocusItemVM : ViewModel
    {
        private readonly FocusDefinition _def;
        private string _status = "";
        private bool _available = true;
        private bool _visible = true;
        private float _x, _y, _w, _h;
        private int _fontSize = 19;
        private float _iconSize = 44f;
        private float _iconMargin = 8f;
        private float _textMargin = 60f;

        internal FocusItemVM(FocusDefinition def, Action<FocusItemVM> onSelect)
        {
            _def = def;
            OnSelect = onSelect;
        }

        internal FocusDefinition Definition { get { return _def; } }
        internal Action<FocusItemVM> OnSelect { get; private set; }

        [DataSourceProperty]
        public string Name { get { return _def.Name; } }

        [DataSourceProperty]
        public string Icon { get { return _def.Icon; } }

        [DataSourceProperty]
        public float PosX { get { return _x; } set { if (Math.Abs(_x - value) > 0.5f) { _x = value; OnPropertyChangedWithValue(value, "PosX"); } } }

        [DataSourceProperty]
        public float PosY { get { return _y; } set { if (Math.Abs(_y - value) > 0.5f) { _y = value; OnPropertyChangedWithValue(value, "PosY"); } } }

        [DataSourceProperty]
        public float Width { get { return _w; } set { if (Math.Abs(_w - value) > 0.5f) { _w = value; OnPropertyChangedWithValue(value, "Width"); } } }

        [DataSourceProperty]
        public float Height { get { return _h; } set { if (Math.Abs(_h - value) > 0.5f) { _h = value; OnPropertyChangedWithValue(value, "Height"); } } }

        // 字体/图标也跟着缩放(否则节点框变大了字还是原大小)
        // 注意: Brush.FontSize 的数据类型是 Int32, 这里必须是 int, 否则 prefab 加载会抛转换异常
        [DataSourceProperty]
        public int FontSize { get { return _fontSize; } set { if (_fontSize != value) { _fontSize = value; OnPropertyChangedWithValue(value, "FontSize"); } } }

        [DataSourceProperty]
        public float IconSize { get { return _iconSize; } set { if (Math.Abs(_iconSize - value) > 0.1f) { _iconSize = value; OnPropertyChangedWithValue(value, "IconSize"); } } }

        [DataSourceProperty]
        public float IconMargin { get { return _iconMargin; } set { if (Math.Abs(_iconMargin - value) > 0.1f) { _iconMargin = value; OnPropertyChangedWithValue(value, "IconMargin"); } } }

        [DataSourceProperty]
        public float TextMargin { get { return _textMargin; } set { if (Math.Abs(_textMargin - value) > 0.1f) { _textMargin = value; OnPropertyChangedWithValue(value, "TextMargin"); } } }

        [DataSourceProperty]
        public string Status { get { return _status; } set { if (_status != value) { _status = value; OnPropertyChangedWithValue(value, "Status"); } } }

        // 图标亮度: 已解锁=正常, 未解锁=变暗(不再用文字写"未解锁")
        private string _iconColor = "#55FFFFFF";

        [DataSourceProperty]
        public string IconColor
        {
            get { return _iconColor; }
            set { if (_iconColor != value) { _iconColor = value; OnPropertyChangedWithValue(value, "IconColor"); } }
        }

        [DataSourceProperty]
        public bool IsAvailable { get { return _available; } set { if (_available != value) { _available = value; OnPropertyChangedWithValue(value, "IsAvailable"); } } }

        [DataSourceProperty]
        public bool IsVisible { get { return _visible; } set { if (_visible != value) { _visible = value; OnPropertyChangedWithValue(value, "IsVisible"); } } }

        public void ExecuteSelect()
        {
            try { if (OnSelect != null) OnSelect(this); }
            catch (Exception ex) { DLog.Force("国策点击异常: " + ex.Message); }
        }

        internal void Refresh()
        {
            if (FocusTreeData.IsCompleted(_def.Id)) { Status = "已完成"; IsAvailable = true; IconColor = "#FFD6A24A"; }
            else if (FocusTreeData.IsInProgress(_def.Id)) { Status = "进行中"; IsAvailable = true; IconColor = "#FFFFFFFF"; }
            else if (FocusTreeData.PrerequisitesMet(_def)) { Status = "可开始"; IsAvailable = true; IconColor = "#FFFFFFFF"; }
            else { Status = "未解锁"; IsAvailable = false; IconColor = "#55FFFFFF"; }
        }
    }

    // 国策树屏幕的VM
    public class FocusTreeVM : ViewModel
    {
        private string _title = "国策树";
        private string _subtitle = "";
        private string _counter = "0/0";
        private string _searchText = "";
        private float _scale = 0.8f;
        private float _offsetX;
        private float _offsetY;

        // ===== 平移/缩放(滚轮 / WASD / 鼠标拖动) =====
        public void Pan(float dx, float dy)
        {
            try
            {
                if (Math.Abs(dx) < 0.01f && Math.Abs(dy) < 0.01f) return;
                _offsetX += dx;
                _offsetY += dy;
                Layout();
            }
            catch { }
        }

        public void ZoomBy(float delta)
        {
            SetScale(_scale + delta);
        }

        public FocusTreeVM()
        {
            try
            {
                var b = NationalWillOrders.Behavior;
                var k = b != null ? b.NationKingdom : null;
                if (k != null && k.Name != null) _title = k.Name + " 国策树";
            }
            catch { }

            Items = new MBBindingList<FocusItemVM>();
            Lines = new MBBindingList<FocusLineVM>();
            foreach (var def in FocusTreeData.All)
                Items.Add(new FocusItemVM(def, OnItemSelected));

            Layout();
            Refresh();
        }

        [DataSourceProperty]
        public MBBindingList<FocusItemVM> Items { get; private set; }

        [DataSourceProperty]
        public MBBindingList<FocusLineVM> Lines { get; private set; }

        [DataSourceProperty]
        public string Title
        {
            get { return _title; }
            set { if (_title != value) { _title = value; OnPropertyChangedWithValue(value, "Title"); } }
        }

        [DataSourceProperty]
        public string Subtitle
        {
            get { return _subtitle; }
            set { if (_subtitle != value) { _subtitle = value; OnPropertyChangedWithValue(value, "Subtitle"); } }
        }

        [DataSourceProperty]
        public string Counter
        {
            get { return _counter; }
            set { if (_counter != value) { _counter = value; OnPropertyChangedWithValue(value, "Counter"); } }
        }

        // 搜索框: 输入后只显示匹配的节点
        [DataSourceProperty]
        public string SearchText
        {
            get { return _searchText; }
            set
            {
                if (_searchText == value) return;
                _searchText = value;
                OnPropertyChangedWithValue(value, "SearchText");
                ApplyFilter();
            }
        }

        // ===== 缩放 =====
        public void ExecuteZoomIn() { SetScale(_scale + 0.1f); }
        public void ExecuteZoomOut() { SetScale(_scale - 0.1f); }

        private void SetScale(float value)
        {
            try
            {
                value = Math.Max(0.5f, Math.Min(1.4f, value));
                if (Math.Abs(value - _scale) < 0.001f) return;
                _scale = value;
                OnPropertyChangedWithValue(value, "Scale");
                Layout();
            }
            catch (Exception ex) { DLog.Force("缩放异常: " + ex.Message); }
        }

        [DataSourceProperty]
        public float Scale
        {
            get { return _scale; }
        }

        // ===== 右侧面板: 正在解锁的国策与剩余天数 =====
        private string _currentName = "";
        private string _currentDays = "";
        private bool _hasCurrent;

        [DataSourceProperty]
        public string CurrentName
        {
            get { return _currentName; }
            set { if (_currentName != value) { _currentName = value; OnPropertyChangedWithValue(value, "CurrentName"); } }
        }

        [DataSourceProperty]
        public string CurrentDays
        {
            get { return _currentDays; }
            set { if (_currentDays != value) { _currentDays = value; OnPropertyChangedWithValue(value, "CurrentDays"); } }
        }

        [DataSourceProperty]
        public bool HasCurrent
        {
            get { return _hasCurrent; }
            set { if (_hasCurrent != value) { _hasCurrent = value; OnPropertyChangedWithValue(value, "HasCurrent"); } }
        }

        public void ExecuteClose()
        {
            try { FocusTreeScreen.Close(); }
            catch (Exception ex) { DLog.Force("关闭国策树失败: " + ex.Message); }
        }

        // ===== 布局: 节点位置/大小 + 连接线 =====
        private void Layout()
        {
            try
            {
                float nw = FocusTreeData.NodeWidth * _scale;
                float nh = FocusTreeData.NodeHeight * _scale;
                foreach (var it in Items)
                {
                    it.PosX = it.Definition.PosX * _scale + _offsetX;
                    it.PosY = it.Definition.PosY * _scale + _offsetY;
                    it.Width = nw;
                    it.Height = nh;
                    it.FontSize = (int)Math.Round(19f * _scale);
                    it.IconSize = 44f * _scale;
                    it.IconMargin = 8f * _scale;
                    it.TextMargin = 60f * _scale;
                }

                Lines.Clear();
                foreach (var f in FocusTreeData.All)
                {
                    if (f.Requires == null) continue;
                    foreach (var rid in f.Requires)
                    {
                        var p = FocusTreeData.Get(rid);
                        if (p == null) continue;
                        float x1 = (p.PosX + FocusTreeData.NodeWidth / 2f) * _scale + _offsetX;
                        float y1 = (p.PosY + FocusTreeData.NodeHeight) * _scale + _offsetY;
                        float x2 = (f.PosX + FocusTreeData.NodeWidth / 2f) * _scale + _offsetX;
                        float y2 = f.PosY * _scale + _offsetY;
                        bool done = FocusTreeData.IsCompleted(rid) && FocusTreeData.IsCompleted(f.Id);
                        float midY = (y1 + y2) / 2f;
                        if (Math.Abs(x1 - x2) < 1f)
                        {
                            Lines.Add(new FocusLineVM(x1 - 1f, y1, 2f, Math.Max(0f, y2 - y1), done));
                        }
                        else
                        {
                            Lines.Add(new FocusLineVM(x1 - 1f, y1, 2f, Math.Max(0f, midY - y1), done));
                            Lines.Add(new FocusLineVM(Math.Min(x1, x2), midY - 1f, Math.Abs(x2 - x1), 2f, done));
                            Lines.Add(new FocusLineVM(x2 - 1f, midY, 2f, Math.Max(0f, y2 - midY), done));
                        }
                    }
                }
            }
            catch (Exception ex) { DLog.Force("国策布局异常: " + ex.Message); }
        }

        private void ApplyFilter()
        {
            try
            {
                string key = _searchText != null ? _searchText.Trim() : "";
                foreach (var it in Items)
                {
                    bool show = key.Length == 0 || (it.Name != null && it.Name.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0);
                    it.IsVisible = show;
                }
            }
            catch { }
        }

        private void OnItemSelected(FocusItemVM item)
        {
            try
            {
                if (item == null || item.Definition == null) return;
                var def = item.Definition;

                if (FocusTreeData.IsCompleted(def.Id)) { FocusTreeScreen.ShowInfo(def, "该政策已经完成。"); return; }
                if (FocusTreeData.IsInProgress(def.Id)) { FocusTreeScreen.ShowInfo(def, "该政策正在进行中, 剩余 " + FocusTreeData.InProgress[def.Id] + " 天。"); return; }
                if (!FocusTreeData.PrerequisitesMet(def)) { FocusTreeScreen.ShowInfo(def, "前置国策尚未完成, 无法开始。"); return; }

                FocusTreeScreen.ShowStartConfirm(def, Refresh);
            }
            catch (Exception ex) { DLog.Force("国策选择异常: " + ex.Message); }
        }

        internal void Refresh()
        {
            try
            {
                foreach (var it in Items) it.Refresh();
                int inProg = FocusTreeData.InProgress.Count;
                Counter = FocusTreeData.Completed.Count + "/" + Items.Count;
                Subtitle = "已完成 " + FocusTreeData.Completed.Count + " / " + Items.Count
                           + (inProg > 0 ? "   ·   " + inProg + " 项进行中" : "");
                Layout();   // 完成状态会影响连线颜色
                // 右侧面板: 正在解锁的国策
                string curId = null;
                foreach (var kv in FocusTreeData.InProgress) { curId = kv.Key; break; }
                if (curId != null)
                {
                    var def = FocusTreeData.Get(curId);
                    HasCurrent = true;
                    CurrentName = def != null ? def.Name : curId;
                    CurrentDays = "剩余 " + FocusTreeData.InProgress[curId] + " 天";
                }
                else
                {
                    HasCurrent = false;
                    CurrentName = "";
                    CurrentDays = "";
                }
                if (!_logged)
                {
                    _logged = true;
                    DLog.Force("国策树刷新: 共 " + Items.Count + " 项, 首项状态=" + (Items.Count > 0 ? Items[0].Status : "?")
                        + ", 可开始=" + (Items.Count > 0 && Items[0].IsAvailable));
                }
            }
            catch { }
        }

        private static bool _logged;
    }
}
