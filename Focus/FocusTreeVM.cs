using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;

namespace FeudalInternalAffairs
{
    // 一条连接线(细长矩形; v4.168 支持 Rotation 旋转 = 真斜线)
    public class FocusLineVM : ViewModel
    {
        private float _x, _y, _w, _h;
        private float _rot;
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
        public float Rotation { get { return _rot; } set { if (Math.Abs(_rot - value) > 0.01f) { _rot = value; OnPropertyChangedWithValue(value, "Rotation"); } } }

        [DataSourceProperty]
        public string Color { get { return _color; } set { if (_color != value) { _color = value; OnPropertyChangedWithValue(value, "Color"); } } }
    }

    // 国策节点卡(v4.182 照科技页: 横卡 —— 左圆徽章 + 名称/效果两行 + 状态角标 + 底部进度条)
    public class FocusItemVM : ViewModel
    {
        private readonly FocusDefinition _def;
        private float _x, _y, _w, _h, _s = 1f;
        private int _fontSize = 17, _effectSize = 12, _numSize = 12, _badgeSize = 18;
        private string _stateText = "";
        private float _barPct;
        private string _barColor = "#C9A227FF";
        private string _iconColor = "#FFFFFFFF";
        private string _plateColor = "#241B12E6";
        private string _nameColor = "#D9C08AFF";
        private string _effectColor = "#A08F6CFF";
        private string _frameColor = "#C9A22766";
        private string _badgeText = "";
        private string _badgeColor = "#E8C33AFF";

        internal FocusItemVM(FocusDefinition def, Action<FocusItemVM> onSelect)
        {
            _def = def;
            OnSelect = onSelect;
        }

        internal FocusDefinition Definition { get { return _def; } }
        internal Action<FocusItemVM> OnSelect { get; private set; }

        [DataSourceProperty] public string Name { get { return _def.Name; } }
        [DataSourceProperty] public string EffectText { get { return _def.Effects; } }
        // v4.192: 国策图标映射到已产出的新标准图标(圆角方形); 未映射的回退占位徽章
        [DataSourceProperty]
        public string Icon
        {
            get
            {
                switch (_def.Id)
                {
                    case "f_root": return "fia_ig_crown";
                    case "f_econ": return "fia_bld_farm";
                    case "f_army": return MilIcons.IconOf("line_infantry");
                    case "f_diplo": return "fia_dip_improve_relations";
                    case "f_farm": return "fia_goods_grain";
                    case "f_trade": return "fia_goods_groceries";
                    case "f_drill": return MilIcons.IconOf("light_infantry");
                    case "f_fort": return "fia_bld_walls";
                    case "f_marry": return "fia_dip_alliance";
                    case "f_granary": return "fia_bld_granary";
                    case "f_market": return "fia_bld_market";
                    case "f_veteran": return "fia_unit_hussar";
                    case "f_wall": return "fia_bld_watchtower";
                    case "f_envoy": return "fia_dip_amicable";
                    case "f_prosper": return "fia_bld_urban_center";
                    case "f_empire": return "fia_ig_army";
                    case "f_hegemony": return "fia_dip_great_power";
                }
                return "fia_tech_ph";
            }
        }
        [DataSourceProperty] public string DaysText { get { return _def.Days + " 日"; } }
        [DataSourceProperty] public float PosX { get { return _x; } set { if (Math.Abs(_x - value) > 0.5f) { _x = value; OnPropertyChangedWithValue(value, "PosX"); } } }
        [DataSourceProperty] public float PosY { get { return _y; } set { if (Math.Abs(_y - value) > 0.5f) { _y = value; OnPropertyChangedWithValue(value, "PosY"); } } }
        [DataSourceProperty]
        public float Width
        {
            get { return _w; }
            set
            {
                if (Math.Abs(_w - value) > 0.5f)
                {
                    _w = value;
                    OnPropertyChangedWithValue(value, "Width");
                    OnPropertyChangedWithValue(BarW, "BarW");
                }
            }
        }
        [DataSourceProperty] public float Height { get { return _h; } set { if (Math.Abs(_h - value) > 0.5f) { _h = value; OnPropertyChangedWithValue(value, "Height"); } } }
        [DataSourceProperty] public int FontSize { get { return _fontSize; } set { if (_fontSize != value) { _fontSize = value; OnPropertyChangedWithValue(value, "FontSize"); } } }
        [DataSourceProperty] public int EffectSize { get { return _effectSize; } set { if (_effectSize != value) { _effectSize = value; OnPropertyChangedWithValue(value, "EffectSize"); } } }
        [DataSourceProperty] public int NumFontSize { get { return _numSize; } set { if (_numSize != value) { _numSize = value; OnPropertyChangedWithValue(value, "NumFontSize"); } } }
        [DataSourceProperty] public int BadgeSize { get { return _badgeSize; } set { if (_badgeSize != value) { _badgeSize = value; OnPropertyChangedWithValue(value, "BadgeSize"); } } }
        [DataSourceProperty] public string StateText { get { return _stateText; } set { if (_stateText != value) { _stateText = value; OnPropertyChangedWithValue(value, "StateText"); } } }
        [DataSourceProperty] public float BarW { get { return Math.Max(0f, (_w - BarSide - 10f * _s)) * _barPct; } }
        [DataSourceProperty] public string BarColor { get { return _barColor; } set { if (_barColor != value) { _barColor = value; OnPropertyChangedWithValue(value, "BarColor"); } } }
        [DataSourceProperty] public string IconColor { get { return _iconColor; } set { if (_iconColor != value) { _iconColor = value; OnPropertyChangedWithValue(value, "IconColor"); } } }
        [DataSourceProperty] public string PlateColor { get { return _plateColor; } set { if (_plateColor != value) { _plateColor = value; OnPropertyChangedWithValue(value, "PlateColor"); } } }
        [DataSourceProperty] public string NameColor { get { return _nameColor; } set { if (_nameColor != value) { _nameColor = value; OnPropertyChangedWithValue(value, "NameColor"); } } }
        [DataSourceProperty] public string EffectColor { get { return _effectColor; } set { if (_effectColor != value) { _effectColor = value; OnPropertyChangedWithValue(value, "EffectColor"); } } }
        [DataSourceProperty] public string FrameColor { get { return _frameColor; } set { if (_frameColor != value) { _frameColor = value; OnPropertyChangedWithValue(value, "FrameColor"); } } }
        [DataSourceProperty] public string BadgeText { get { return _badgeText; } set { if (_badgeText != value) { _badgeText = value; OnPropertyChangedWithValue(value, "BadgeText"); } } }
        [DataSourceProperty] public string BadgeColor { get { return _badgeColor; } set { if (_badgeColor != value) { _badgeColor = value; OnPropertyChangedWithValue(value, "BadgeColor"); } } }

        // 内部尺寸(设计值 × _s)
        [DataSourceProperty] public float IconSize { get { return 52f * _s; } }
        [DataSourceProperty] public float MedLeft { get { return 6f * _s; } }
        [DataSourceProperty] public float MedTop { get { return 6f * _s; } }
        [DataSourceProperty] public float NumLeft { get { return 12f * _s; } }
        [DataSourceProperty] public float NumTop { get { return 44f * _s; } }
        [DataSourceProperty] public float NameLeft { get { return 66f * _s; } }
        [DataSourceProperty] public float NameTop { get { return 8f * _s; } }
        [DataSourceProperty] public float EffectLeft { get { return 66f * _s; } }
        [DataSourceProperty] public float EffectTop { get { return 30f * _s; } }
        [DataSourceProperty] public float BadgeRight { get { return 10f * _s; } }
        [DataSourceProperty] public float BadgeTop { get { return 20f * _s; } }
        [DataSourceProperty] public float BarSide { get { return 66f * _s; } }
        [DataSourceProperty] public float BarBottom { get { return 1f * _s; } }
        [DataSourceProperty] public float StateTop { get { return 45f * _s; } }

        internal void SetScale(float s)
        {
            if (Math.Abs(_s - s) < 0.002f) return;
            _s = s;
            OnPropertyChangedWithValue(IconSize, "IconSize");
            OnPropertyChangedWithValue(MedLeft, "MedLeft");
            OnPropertyChangedWithValue(MedTop, "MedTop");
            OnPropertyChangedWithValue(NumLeft, "NumLeft");
            OnPropertyChangedWithValue(NumTop, "NumTop");
            OnPropertyChangedWithValue(NameLeft, "NameLeft");
            OnPropertyChangedWithValue(NameTop, "NameTop");
            OnPropertyChangedWithValue(EffectLeft, "EffectLeft");
            OnPropertyChangedWithValue(EffectTop, "EffectTop");
            OnPropertyChangedWithValue(BadgeRight, "BadgeRight");
            OnPropertyChangedWithValue(BadgeTop, "BadgeTop");
            OnPropertyChangedWithValue(BarSide, "BarSide");
            OnPropertyChangedWithValue(BarBottom, "BarBottom");
            OnPropertyChangedWithValue(StateTop, "StateTop");
            OnPropertyChangedWithValue(BarW, "BarW");
        }

        public void ExecuteSelect()
        {
            try { if (OnSelect != null) OnSelect(this); }
            catch (Exception ex) { DLog.Force("国策点击异常: " + ex.Message); }
        }

        internal void RefreshState()
        {
            try
            {
                if (FocusTreeData.IsCompleted(_def.Id))
                {
                    // 已完成 = 橄榄褐牌 + 金框 + ✓
                    StateText = "已完成";
                    PlateColor = "#2B2717E6"; NameColor = "#E8D9A0FF"; EffectColor = "#A89A70FF";
                    FrameColor = "#C9A22788"; BadgeText = "✓"; BadgeColor = "#C9A227FF";
                    _barPct = 1f; BarColor = "#C9A227FF"; IconColor = "#FFFFFFFF";
                }
                else if (FocusTreeData.IsInProgress(_def.Id))
                {
                    // 推进中 = 青蓝高亮牌 + 进度
                    int left = 0;
                    try { FocusTreeData.InProgress.TryGetValue(_def.Id, out left); } catch { }
                    int total = Math.Max(1, _def.Days);
                    StateText = "推进中 剩 " + left + " 日";
                    PlateColor = "#12414DE6"; NameColor = "#D8F6FFFF"; EffectColor = "#8FD8E8FF";
                    FrameColor = "#4FD8E8AA"; BadgeText = "◉"; BadgeColor = "#6FE8F8FF";
                    _barPct = Math.Min(1f, Math.Max(0f, 1f - left / (float)total));
                    BarColor = "#5FE0F0FF"; IconColor = "#FFFFFFFF";
                }
                else if (FocusTreeData.PrerequisitesMet(_def))
                {
                    // 可开始 = 深色牌 + 金框 + "+"
                    StateText = "可开始 (" + _def.Days + " 日)";
                    PlateColor = "#1E1912E6"; NameColor = "#E4D3A8FF"; EffectColor = "#A08F6CFF";
                    FrameColor = "#C9A22766"; BadgeText = "+"; BadgeColor = "#E8C33AFF";
                    _barPct = 0f; BarColor = "#C9A227FF"; IconColor = "#FFFFFFFF";
                }
                else
                {
                    StateText = "前置未完成";
                    PlateColor = "#141210D9"; NameColor = "#7A7264FF"; EffectColor = "#5E574BFF";
                    FrameColor = "#FFFFFF14"; BadgeText = ""; BadgeColor = "#FFFFFF00";
                    _barPct = 0f; IconColor = "#FFFFFF40";
                }
                OnPropertyChangedWithValue(BarW, "BarW");
            }
            catch { }
        }
    }

    // 国策页 VM(v4.182 照科技页重做: 横卡节点 / 粗棕曲线 + 当前路径青色高亮 / 顶栏三栏状态)
    public class FocusTreeVM : PanelVMBase
    {
        private readonly Action _onClose;
        private float _scale = 1f;
        private float _offsetX = 18f, _offsetY = 2f;
        private bool _fit;
        private static bool _logged;
        private float _contentW = 1400f, _contentH = 800f, _treeW = 1400f, _treeH = 800f;
        internal const float TreeTopY = 210f;      // 与 FeudalFocusTree.xml 树区域 MarginTop 一致

        // 布局常量(设计像素; 乘 _scale) —— 与科技页同一套横卡
        private const float NodeW = 204f, NodeH = 64f;

        // 顶栏三栏状态
        private string _curIcon = "fia_tech_ph", _curName = "未选择国策", _curProg = "", _curEta = "";
        private float _curBarW;
        private string _curBarColor = "#5FE0F0FF";
        private string _midLine1 = "未选择国策", _midLine2 = "(点击节点开始推进, 同时只推进一项)";
        private string _doneText = "0 / 0", _progText = "进行中: 0 项";
        private string _tip = "左键点节点开始国策 · 滚轮缩放 · 右键/中键拖动";

        public FocusTreeVM(Action onClose)
        {
            _onClose = onClose;
            Items = new MBBindingList<FocusItemVM>();
            Lines = new MBBindingList<FocusLineVM>();
            foreach (var def in FocusTreeData.All)
                if (def != null) Items.Add(new FocusItemVM(def, OnItemSelected));
            Refresh();
        }

        public MBBindingList<FocusItemVM> Items { get; private set; }
        public MBBindingList<FocusLineVM> Lines { get; private set; }

        [DataSourceProperty]
        public float PanelWidth
        {
            get { try { return PanelScreen.FullWidth(); } catch { return 1856f; } }
        }
        [DataSourceProperty]
        public float ContentPad
        {
            get { try { return Math.Max(0f, (PanelScreen.FullWidth() - 1700f) / 2f); } catch { return 0f; } }
        }
        [DataSourceProperty] public float TreeWidth { get { return _treeW; } }
        [DataSourceProperty] public float TreeHeight { get { return _treeH; } }

        // 顶栏三栏
        [DataSourceProperty] public string CurIcon { get { return _curIcon; } }
        [DataSourceProperty] public string CurName { get { return _curName; } }
        [DataSourceProperty] public string CurProg { get { return _curProg; } }
        [DataSourceProperty] public string CurEta { get { return _curEta; } }
        [DataSourceProperty] public float CurBarW { get { return _curBarW; } }
        [DataSourceProperty] public string CurBarColor { get { return _curBarColor; } }
        [DataSourceProperty] public string MidLine1 { get { return _midLine1; } }
        [DataSourceProperty] public string MidLine2 { get { return _midLine2; } }
        [DataSourceProperty] public string DoneText { get { return _doneText; } }
        [DataSourceProperty] public string ProgText { get { return _progText; } }
        [DataSourceProperty] public string TipText { get { return _tip; } }

        public void ExecuteClose() { if (_onClose != null) _onClose(); }

        public void ExecuteZoomIn() { ZoomBy(0.1f); }
        public void ExecuteZoomOut() { ZoomBy(-0.1f); }

        internal void Pan(float dx, float dy)
        {
            try
            {
                if (Math.Abs(dx) < 0.01f && Math.Abs(dy) < 0.01f) return;
                _offsetX += dx; _offsetY += dy;
                Layout();
            }
            catch { }
        }

        internal void ZoomBy(float delta) { SetScale(_scale + delta); }

        private void SetScale(float v)
        {
            try
            {
                v = Math.Max(0.8f, Math.Min(1.6f, v));
                if (Math.Abs(v - _scale) < 0.001f) return;
                _scale = v;
                Layout();
            }
            catch { }
        }

        internal void Refresh()
        {
            try
            {
                for (int i = 0; i < Items.Count; i++) Items[i].RefreshState();
                int total = Items.Count;
                int done = 0, prog = 0;
                string curId = null;
                for (int i = 0; i < Items.Count; i++)
                {
                    var id = Items[i].Definition.Id;
                    if (FocusTreeData.IsCompleted(id)) done++;
                    else if (FocusTreeData.IsInProgress(id)) { prog++; if (curId == null) curId = id; }
                }
                _doneText = done + " / " + total;
                _progText = "进行中: " + prog + " 项";

                var cur = curId != null ? FocusTreeData.Get(curId) : null;
                if (cur != null)
                {
                    int left = 0;
                    try { FocusTreeData.InProgress.TryGetValue(curId, out left); } catch { }
                    int days = Math.Max(1, cur.Days);
                    _curIcon = "fia_tech_ph";
                    _curName = cur.Name;
                    _curProg = (days - left) + " / " + days + " 日";
                    _curEta = "剩余 " + left + " 日";
                    _curBarW = 540f * Math.Min(1f, Math.Max(0f, 1f - left / (float)days));
                    _curBarColor = "#5FE0F0FF";
                    _midLine1 = "正在推进: " + cur.Name;
                    _midLine2 = "国策同时只推进一项, 完成后自动结算效果";
                }
                else
                {
                    _curIcon = "fia_tech_ph";
                    _curName = "未选择国策";
                    _curProg = "";
                    _curEta = "";
                    _curBarW = 0f;
                    _curBarColor = "#5FE0F0FF";
                    _midLine1 = "未选择国策";
                    _midLine2 = "(点击节点开始推进, 同时只推进一项)";
                }

                OnPropertyChangedWithValue(_curIcon, "CurIcon");
                OnPropertyChangedWithValue(_curName, "CurName");
                OnPropertyChangedWithValue(_curProg, "CurProg");
                OnPropertyChangedWithValue(_curEta, "CurEta");
                OnPropertyChangedWithValue(_curBarW, "CurBarW");
                OnPropertyChangedWithValue(_curBarColor, "CurBarColor");
                OnPropertyChangedWithValue(_midLine1, "MidLine1");
                OnPropertyChangedWithValue(_midLine2, "MidLine2");
                OnPropertyChangedWithValue(_doneText, "DoneText");
                OnPropertyChangedWithValue(_progText, "ProgText");
                OnPropertyChangedWithValue(_tip, "TipText");

                Layout();
                if (!_logged)
                {
                    _logged = true;
                    DLog.Force("国策页(照科技页重做): 共 " + total + " 项, 已完成 " + done);
                }
            }
            catch (Exception ex) { DLog.Force("国策页刷新失败: " + ex.Message); }
        }

        // ===== 布局: 节点位置/大小 + 连接线(沿用国策表里的手工坐标) =====
        private void Layout()
        {
            try
            {
                if (!_fit)
                {
                    _fit = true;
                    _scale = 1f;
                    _offsetX = 18f;
                    _offsetY = 2f;
                }

                float maxX = 0f, maxY = 0f;
                for (int i = 0; i < Items.Count; i++)
                {
                    var n = Items[i];
                    n.PosX = n.Definition.PosX * _scale + _offsetX;
                    n.PosY = n.Definition.PosY * _scale + _offsetY;
                    n.Width = NodeW * _scale;
                    n.Height = NodeH * _scale;
                    n.FontSize = Math.Max(12, (int)Math.Round(17f * _scale));
                    n.EffectSize = Math.Max(10, (int)Math.Round(12f * _scale));
                    n.NumFontSize = Math.Max(9, (int)Math.Round(12f * _scale));
                    n.BadgeSize = Math.Max(12, (int)Math.Round(18f * _scale));
                    n.SetScale(_scale);
                    float rx = n.Definition.PosX + 200f, ry = n.Definition.PosY + 58f;
                    if (rx > maxX) maxX = rx;
                    if (ry > maxY) maxY = ry;
                }
                _contentW = (maxX + 40f) * _scale;
                _contentH = (maxY + 40f) * _scale;
                _treeW = _contentW + _offsetX;
                _treeH = _contentH + _offsetY;
                OnPropertyChangedWithValue(_treeW, "TreeWidth");
                OnPropertyChangedWithValue(_treeH, "TreeHeight");

                // 高亮链: 正在推进的国策 + 其全部前置祖先(青色高亮路径)
                var hi = new HashSet<string>();
                try
                {
                    string cid = null;
                    foreach (var kv in FocusTreeData.InProgress) { cid = kv.Key; break; }
                    if (!string.IsNullOrEmpty(cid))
                    {
                        var stack = new Stack<string>();
                        stack.Push(cid);
                        for (int guard = 0; guard < 512 && stack.Count > 0; guard++)
                        {
                            string id = stack.Pop();
                            if (!hi.Add(id)) continue;
                            var d = FocusTreeData.Get(id);
                            if (d == null || d.Requires == null) continue;
                            for (int r = 0; r < d.Requires.Length; r++) stack.Push(d.Requires[r]);
                        }
                    }
                }
                catch { }

                Lines.Clear();
                for (int i = 0; i < Items.Count; i++)
                {
                    var n = Items[i];
                    var reqs = n.Definition.Requires;
                    if (reqs == null) continue;
                    for (int j = 0; j < reqs.Length; j++)
                    {
                        var pd = FocusTreeData.Get(reqs[j]);
                        if (pd == null) continue;
                        FocusItemVM p = null;
                        for (int k = 0; k < Items.Count; k++)
                            if (Items[k].Definition.Id == pd.Id) { p = Items[k]; break; }
                        if (p == null) continue;
                        bool hl = hi.Contains(n.Definition.Id) && hi.Contains(p.Definition.Id);
                        AddLink(p, n, hl);
                    }
                }
            }
            catch (Exception ex) { DLog.Force("国策布局异常: " + ex.Message); }
        }

        // 连线(照科技页): 粗棕曲线(3px) + 当前路径青色高亮(8px 半透明)
        private void AddLink(FocusItemVM p, FocusItemVM n, bool highlight)
        {
            try
            {
                float x1 = p.PosX + p.IconSize * 0.5f + p.MedLeft, y1 = p.PosY + p.Height;
                float x2 = n.PosX + n.IconSize * 0.5f + n.MedLeft, y2 = n.PosY;
                if (y2 - y1 < 2f && Math.Abs(x2 - x1) < 2f) return;
                bool done = FocusTreeData.IsCompleted(p.Definition.Id) && FocusTreeData.IsCompleted(n.Definition.Id);
                string color = highlight ? "#4FD8E8AA" : (done ? "#C9A227AA" : "#9A7A4A88");
                float w = highlight ? 8f * _scale : 3f * _scale;

                float dy = y2 - y1;
                float c1x = x1, c1y = y1 + dy * 0.45f;
                float c2x = x2, c2y = y2 - dy * 0.45f;
                float chord = (float)Math.Sqrt((x2 - x1) * (x2 - x1) + dy * dy);
                int N = Math.Max(8, Math.Min(24, (int)(chord / 10f) + 2));
                float px = x1, py = y1;
                for (int i = 1; i <= N; i++)
                {
                    float t = i / (float)N, mt = 1f - t;
                    float bx = mt * mt * mt * x1 + 3f * mt * mt * t * c1x + 3f * mt * t * t * c2x + t * t * t * x2;
                    float by = mt * mt * mt * y1 + 3f * mt * mt * t * c1y + 3f * mt * t * t * c2y + t * t * t * y2;
                    float dx = bx - px, dyy = by - py;
                    float len = (float)Math.Sqrt(dx * dx + dyy * dyy);
                    if (len > 0.6f)
                    {
                        float ang = (float)(Math.Atan2(dyy, dx) * 180.0 / Math.PI);
                        var seg = new FocusLineVM((px + bx) / 2f - len / 2f, (py + by) / 2f - w / 2f, len, w, done);
                        seg.Rotation = ang;
                        seg.Color = color;
                        Lines.Add(seg);
                    }
                    px = bx; py = by;
                }
            }
            catch { }
        }

        // 点击节点: 详情 + 开始
        internal void NodeAction(int idx)
        {
            try
            {
                if (idx < 0 || idx >= Items.Count) return;
                OnItemSelected(Items[idx]);
            }
            catch (Exception ex) { DLog.Force("国策节点点击失败: " + ex.Message); }
        }

        private void OnItemSelected(FocusItemVM item)
        {
            try
            {
                if (item == null || item.Definition == null) return;
                var def = item.Definition;
                if (FocusTreeData.IsCompleted(def.Id)) { FocusTreeScreen.ShowInfo(def, "该政策已经完成。"); return; }
                if (FocusTreeData.IsInProgress(def.Id))
                {
                    int left = 0;
                    try { FocusTreeData.InProgress.TryGetValue(def.Id, out left); } catch { }
                    FocusTreeScreen.ShowInfo(def, "该政策正在推进中, 剩余 " + left + " 天。");
                    return;
                }
                if (!FocusTreeData.PrerequisitesMet(def)) { FocusTreeScreen.ShowInfo(def, "前置国策尚未完成, 无法开始。"); return; }
                FocusTreeScreen.ShowStartConfirm(def, Refresh);
            }
            catch (Exception ex) { DLog.Force("国策选择异常: " + ex.Message); }
        }
    }
}
