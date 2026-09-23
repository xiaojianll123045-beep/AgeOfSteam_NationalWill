using System;
using System.Collections.Generic;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace FeudalInternalAffairs
{
    // 时代(照 V3: 名称 + 配色)
    internal static class TechEraStyle
    {
        internal static readonly string[] Names = { "传统", "蒸汽", "工业", "电气", "现代" };
        internal static readonly string[] Colors = { "#9A8F7CFF", "#C08A3EFF", "#C9A227FF", "#7FA8C8FF", "#A88FC8FF" };

        internal static int EraOf(TechDef d) { return Math.Max(1, Math.Min(5, d.Era)); }
        internal static string Name(int era) { return Names[Raw(era)]; }
        internal static string Color(int era) { return Colors[Raw(era)]; }
        private static int Raw(int era) { return Math.Max(0, Math.Min(4, era - 1)); }
    }

    // 科技节点(v4.179 照 V3: 横卡 —— 左圆徽章 + 名称/效果两行 + 右上角状态角标 + 底部进度条)
    public class TechNodeVM : ViewModel
    {
        private readonly TechDef _def;
        private float _x, _y, _w, _h, _s = 1f;
        private int _fontSize = 17, _effectSize = 12, _badgeSize = 18;
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

        internal TechNodeVM(TechDef def) { _def = def; }

        internal TechDef Definition { get { return _def; } }

        [DataSourceProperty] public string Name { get { return _def.Name; } }
        [DataSourceProperty] public string EffectText { get { return _def.Effect; } }
        private static string RomanOfEra(int n)
        {
            switch (n) { case 1: return "I"; case 2: return "II"; case 3: return "III"; case 4: return "IV"; default: return "V"; }
        }
        [DataSourceProperty] public string EraText { get { return RomanOfEra(TechEraStyle.EraOf(_def)); } }
        // 科技图标已产出(fia_tech_<id>); 缺省回退到程序生成的圆形占位徽章
        [DataSourceProperty] public string Icon { get { return string.IsNullOrEmpty(_def.Icon) ? "fia_tech_" + _def.Id : _def.Icon; } }
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

        internal void RefreshState()
        {
            try
            {
                bool done = Research.IsDone(_def.Id);
                bool cur = Research.CurrentId[_def.Tree] == _def.Id;
                bool spread = Research.SpreadId[_def.Tree] == _def.Id;
                float cost = Research.CostOf(_def.Id);
                float prog = 0f;
                try { Research.Progress.TryGetValue(_def.Id, out prog); } catch { }
                if (done)
                {
                    // V3: 已完成 = 橄榄褐牌 + 金框 + ✓
                    StateText = "已完成";
                    PlateColor = "#2B2717E6"; NameColor = "#E8D9A0FF"; EffectColor = "#A89A70FF";
                    FrameColor = "#C9A22788"; BadgeText = "✓"; BadgeColor = "#C9A227FF";
                    _barPct = 1f; BarColor = "#C9A227FF"; IconColor = "#FFFFFFFF";
                }
                else if (cur)
                {
                    // V3: 研究中 = 青蓝高亮牌 + 进度
                    StateText = "研究中 " + (int)prog + "/" + (int)cost;
                    PlateColor = "#12414DE6"; NameColor = "#D8F6FFFF"; EffectColor = "#8FD8E8FF";
                    FrameColor = "#4FD8E8AA"; BadgeText = "◉"; BadgeColor = "#6FE8F8FF";
                    _barPct = cost > 0f ? Math.Min(1f, prog / cost) : 0f;
                    BarColor = "#5FE0F0FF"; IconColor = "#FFFFFFFF";
                }
                else if (spread)
                {
                    StateText = "传播中 " + (int)prog + "/" + (int)cost;
                    PlateColor = "#1E2F3FE6"; NameColor = "#CFE0F0FF"; EffectColor = "#8FA8C8FF";
                    FrameColor = "#8FA8C899"; BadgeText = "◆"; BadgeColor = "#8FA8C8FF";
                    _barPct = cost > 0f ? Math.Min(1f, prog / cost) : 0f;
                    BarColor = "#8FA8C8FF"; IconColor = "#FFFFFFFF";
                }
                else if (!Research.ReqOk(_def))
                {
                    StateText = "前置未完成";
                    PlateColor = "#141210D9"; NameColor = "#7A7264FF"; EffectColor = "#5E574BFF";
                    FrameColor = "#FFFFFF14"; BadgeText = ""; BadgeColor = "#FFFFFF00";
                    _barPct = 0f; IconColor = "#FFFFFF40";
                }
                else
                {
                    // V3: 可研究 = 深色牌 + 金框 + "+"
                    StateText = "可研究 (" + (int)cost + ")";
                    PlateColor = "#1E1912E6"; NameColor = "#E4D3A8FF"; EffectColor = "#A08F6CFF";
                    FrameColor = "#C9A22766"; BadgeText = "+"; BadgeColor = "#E8C33AFF";
                    _barPct = 0f; IconColor = "#FFFFFFFF";
                }
                OnPropertyChangedWithValue(BarW, "BarW");
            }
            catch { }
        }
    }

    // 时代条带标签(传统 ...)
    public class TechHeadVM : ViewModel
    {
        private readonly string _text, _color;
        private readonly float _x, _y;
        private readonly int _fontSize;

        internal TechHeadVM(string text, string color, float x, float y, int fontSize)
        {
            _text = text; _color = color; _x = x; _y = y; _fontSize = fontSize;
        }

        [DataSourceProperty] public string Text { get { return _text; } }
        [DataSourceProperty] public string TextColor { get { return _color; } }
        [DataSourceProperty] public float PosX { get { return _x; } }
        [DataSourceProperty] public float PosY { get { return _y; } }
        [DataSourceProperty] public int FontSize { get { return _fontSize; } }
    }

    // 科技页 VM(v4.179 照 V3 官方科技树: 横卡节点 / 粗棕曲线 + 当前研究青色高亮 / 顶栏三栏状态)
    public class TechPanelVM : PanelVMBase
    {
        private readonly Action _onClose;
        internal int Tab;                       // 0 生产 / 1 军事 / 2 社会
        private float _scale = 1f;
        private float _offsetX = 18f, _offsetY = 2f;
        private bool _fit;
        private static bool _logged;
        private float _cols = 11f, _treeW = 1700f, _treeH = 900f, _contentW = 1700f, _contentH = 900f;
        internal const float TreeTopY = 266f;      // 与 FeudalTech.xml 树区域 MarginTop 一致

        private string _innov = "—", _cap = "—", _speed = "—", _lit = "—", _done = "—", _tip = "";
        private string _tab0Color = "#C9A227FF", _tab1Color = "#8A8070FF", _tab2Color = "#8A8070FF";
        private string _tab0Bg = "#FFFFFF14", _tab1Bg = "#00000066", _tab2Bg = "#00000066";
        // 顶栏三栏状态
        private string _curIcon = "fia_tech_ph", _curName = "未选择", _curProg = "", _curEta = "";
        private float _curBarW;
        private string _curBarColor = "#5FE0F0FF";
        private string _innovWeekly = "每周创新力: —", _spreadText = "科技扩散: —";

        // 布局常量(设计像素; 乘 _scale) —— V3 横卡
        private const float NodeW = 204f, NodeH = 64f, ColGap = 16f;
        private const float RowPitch = 92f, BandPadTop = 24f, BandGap = 8f, LeftPad = 18f;

        public TechPanelVM(Action onClose)
        {
            _onClose = onClose;
            Nodes = new MBBindingList<TechNodeVM>();
            Lines = new MBBindingList<FocusLineVM>();
            Heads = new MBBindingList<TechHeadVM>();
            Refresh();
        }

        public MBBindingList<TechNodeVM> Nodes { get; private set; }
        public MBBindingList<FocusLineVM> Lines { get; private set; }
        public MBBindingList<TechHeadVM> Heads { get; private set; }

        [DataSourceProperty] public string InnovText { get { return _innov; } }
        [DataSourceProperty] public string CapText { get { return _cap; } }
        [DataSourceProperty] public string SpeedText { get { return _speed; } }
        [DataSourceProperty] public string LitText { get { return _lit; } }
        [DataSourceProperty] public string DoneText { get { return _done; } }
        [DataSourceProperty] public string TipText { get { return _tip; } }
        [DataSourceProperty] public string Tab0Color { get { return _tab0Color; } }
        [DataSourceProperty] public string Tab1Color { get { return _tab1Color; } }
        [DataSourceProperty] public string Tab2Color { get { return _tab2Color; } }
        [DataSourceProperty] public string Tab0Bg { get { return _tab0Bg; } }
        [DataSourceProperty] public string Tab1Bg { get { return _tab1Bg; } }
        [DataSourceProperty] public string Tab2Bg { get { return _tab2Bg; } }
        [DataSourceProperty] public string Tab0Sprite { get { return Tab == 0 ? "fia_ui_tab_on" : "fia_ui_tab_off"; } }
        [DataSourceProperty] public string Tab0Brush { get { return Tab == 0 ? "Fia.TabOn" : "Fia.TabOff"; } }
        [DataSourceProperty] public string Tab1Brush { get { return Tab == 1 ? "Fia.TabOn" : "Fia.TabOff"; } }
        [DataSourceProperty] public string Tab2Brush { get { return Tab == 2 ? "Fia.TabOn" : "Fia.TabOff"; } }
        [DataSourceProperty] public string Tab1Sprite { get { return Tab == 1 ? "fia_ui_tab_on" : "fia_ui_tab_off"; } }
        [DataSourceProperty] public string Tab2Sprite { get { return Tab == 2 ? "fia_ui_tab_on" : "fia_ui_tab_off"; } }
        // 顶栏三栏
        [DataSourceProperty] public string CurIcon { get { return _curIcon; } }
        [DataSourceProperty] public string CurName { get { return _curName; } }
        [DataSourceProperty] public string CurProg { get { return _curProg; } }
        [DataSourceProperty] public string CurEta { get { return _curEta; } }
        [DataSourceProperty] public float CurBarW { get { return _curBarW; } }
        [DataSourceProperty] public string CurBarColor { get { return _curBarColor; } }
        [DataSourceProperty] public string InnovWeekly { get { return _innovWeekly; } }
        [DataSourceProperty] public string SpreadText { get { return _spreadText; } }
        [DataSourceProperty] public string CurText
        {
            get
            {
                try
                {
                    string id = Research.CurrentId[Tab];
                    if (string.IsNullOrEmpty(id)) return "当前研究: 未选择";
                    var t = Find(id);
                    return "当前研究: " + (t != null ? t.Name : id);
                }
                catch { return ""; }
            }
        }
        [DataSourceProperty]
        public bool HasCurrent
        {
            get { try { return !string.IsNullOrEmpty(Research.CurrentId[Tab]); } catch { return false; } }
        }
        [DataSourceProperty]
        public bool NoCurrent
        {
            get { try { return string.IsNullOrEmpty(Research.CurrentId[Tab]); } catch { return true; } }
        }

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
        // 树内容尺寸(容器用; 超出屏幕靠拖动)
        [DataSourceProperty] public float TreeWidth { get { return _treeW; } }
        [DataSourceProperty] public float TreeHeight { get { return _treeH; } }

        public void ExecuteClose() { if (_onClose != null) _onClose(); }

        internal void SetTab(int t)
        {
            if (t < 0 || t > 2 || t == Tab) return;
            Tab = t;
            _fit = false;
            Refresh();
        }

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
                Nodes.Clear();
                foreach (var t in Research.All)
                    if (t != null && t.Tree == Tab) Nodes.Add(new TechNodeVM(t));

                _innov = Research.InnovationDaily().ToString("F2") + " / 日";
                _cap = Research.InnovationCap().ToString("F1");
                _speed = "×" + Research.ResearchSpeedMult().ToString("F2");
                _lit = ((int)Math.Round(Research.LiteracyPct())) + "%";
                _done = Research.Done.Count + " / " + Research.All.Count;
                _tab0Color = Tab == 0 ? "#C9A227FF" : "#8A8070FF";
                _tab1Color = Tab == 1 ? "#C9A227FF" : "#8A8070FF";
                _tab2Color = Tab == 2 ? "#C9A227FF" : "#8A8070FF";
                _tab0Bg = Tab == 0 ? "#FFFFFF14" : "#00000066";
                _tab1Bg = Tab == 1 ? "#FFFFFF14" : "#00000066";
                _tab2Bg = Tab == 2 ? "#FFFFFF14" : "#00000066";
                OnPropertyChangedWithValue(_innov, "InnovText");
                OnPropertyChangedWithValue(_cap, "CapText");
                OnPropertyChangedWithValue(_speed, "SpeedText");
                OnPropertyChangedWithValue(_lit, "LitText");
                OnPropertyChangedWithValue(_done, "DoneText");
                OnPropertyChangedWithValue(_tab0Color, "Tab0Color");
                OnPropertyChangedWithValue(_tab1Color, "Tab1Color");
                OnPropertyChangedWithValue(_tab2Color, "Tab2Color");
                OnPropertyChangedWithValue(_tab0Bg, "Tab0Bg");
                OnPropertyChangedWithValue(_tab1Bg, "Tab1Bg");
                OnPropertyChangedWithValue(_tab2Bg, "Tab2Bg");
                OnPropertyChangedWithValue(Tab0Sprite, "Tab0Sprite");
                OnPropertyChangedWithValue(Tab0Brush, "Tab0Brush");
                OnPropertyChangedWithValue(Tab1Brush, "Tab1Brush");
                OnPropertyChangedWithValue(Tab2Brush, "Tab2Brush");
                OnPropertyChangedWithValue(Tab1Sprite, "Tab1Sprite");
                OnPropertyChangedWithValue(Tab2Sprite, "Tab2Sprite");
                OnPropertyChangedWithValue(CurText, "CurText");
                OnPropertyChangedWithValue(HasCurrent, "HasCurrent");
                OnPropertyChangedWithValue(NoCurrent, "NoCurrent");
                RefreshStatus();

                for (int i = 0; i < Nodes.Count; i++) Nodes[i].RefreshState();
                Layout();
                _tip = "左键点节点研究 · 滚轮缩放 · 右键/中键拖动 · 前置可跨类别";
                OnPropertyChangedWithValue(_tip, "TipText");
                if (!_logged)
                {
                    _logged = true;
                    DLog.Force("科技页(横卡): 当前树 " + Research.TreeNames[Tab] + " 节点 " + Nodes.Count);
                }
            }
            catch (Exception ex) { DLog.Force("科技页刷新失败: " + ex.Message); }
        }

        // 顶栏三栏: 当前研究(图标/名称/进度/剩余年) + 每周创新力/研究速度
        private void RefreshStatus()
        {
            try
            {
                string id = Research.CurrentId[Tab];
                var t = string.IsNullOrEmpty(id) ? null : Find(id);
                _curIcon = t != null ? (string.IsNullOrEmpty(t.Icon) ? "fia_tech_" + t.Id : t.Icon) : "fia_tech_ph";
                _curName = t != null ? t.Name : "未选择研究";
                float cost = t != null ? Research.CostOf(t.Id) : 0f;
                float prog = 0f;
                if (t != null) { try { Research.Progress.TryGetValue(t.Id, out prog); } catch { } }
                _curProg = t != null ? ((int)prog + " / " + (int)cost) : "";
                float pct = cost > 0f ? Math.Min(1f, prog / cost) : 0f;
                _curBarW = 540f * pct;
                _curBarColor = "#5FE0F0FF";
                string eta = "";
                if (t != null && prog < cost)
                {
                    float perDay = Research.InnovationDaily() * Research.ResearchSpeedMult();
                    if (perDay > 0.01f)
                    {
                        float days = (cost - prog) / perDay;
                        float years = days / 84f;
                        eta = years >= 1f ? ("剩余 " + years.ToString("F1") + " 年") : ("剩余 " + (int)days + " 天");
                    }
                }
                _curEta = eta;
                _innovWeekly = "每周创新力: " + (Research.InnovationDaily() * 7f).ToString("F0");
                _spreadText = DiffusionText();   // v6.0: 本国扩散来源列表(文档 28.10 C)
                OnPropertyChangedWithValue(_curIcon, "CurIcon");
                OnPropertyChangedWithValue(_curName, "CurName");
                OnPropertyChangedWithValue(_curProg, "CurProg");
                OnPropertyChangedWithValue(_curEta, "CurEta");
                OnPropertyChangedWithValue(_curBarW, "CurBarW");
                OnPropertyChangedWithValue(_curBarColor, "CurBarColor");
                OnPropertyChangedWithValue(_innovWeekly, "InnovWeekly");
                OnPropertyChangedWithValue(_spreadText, "SpreadText");
            }
            catch { }
        }

        // v6.0: 科技页显示本国王国的"扩散来源"(文档 28.10 C)
        private string DiffusionText()
        {
            try
            {
                var nat = Research.Of(Research.PlayerKingdom());
                if (nat.SpreadFrom.Count == 0) return "科技扩散: 无";
                var sb = new System.Text.StringBuilder("扩散来源: ");
                int n = 0;
                foreach (var kv in nat.SpreadFrom)
                {
                    var td = Find(kv.Key);
                    if (td == null) continue;
                    if (n >= 2) { sb.Append(" 等").Append(nat.SpreadFrom.Count).Append("项"); break; }
                    if (n > 0) sb.Append(" · ");
                    var kd = Treaties.FindKingdom(kv.Value);
                    sb.Append(td.Name).Append("←").Append(kd != null ? kd.Name.ToString() : (kv.Value ?? "?"));
                    n++;
                }
                return n > 0 ? sb.ToString() : "科技扩散: 无";
            }
            catch { return "科技扩散: 无"; }
        }

        // ===== 布局(照 V3: 行 = 依赖深度) =====
        private void Layout()
        {
            try
            {
                var byId = new Dictionary<string, TechNodeVM>();
                for (int i = 0; i < Nodes.Count; i++) byId[Nodes[i].Definition.Id] = Nodes[i];

                // ① 依赖深度 -> 分行
                var depth = new Dictionary<string, int>();
                int maxD = 0;
                for (int i = 0; i < Nodes.Count; i++)
                {
                    int d = DepthOf(Nodes[i], byId, depth);
                    if (d > maxD) maxD = d;
                }
                var rows = new List<TechNodeVM>[maxD + 1];
                for (int d = 0; d <= maxD; d++) rows[d] = new List<TechNodeVM>();
                for (int i = 0; i < Nodes.Count; i++) rows[depth[Nodes[i].Definition.Id]].Add(Nodes[i]);

                // ② 每层: 同父兄弟以父节点为中心左右展开(不足则右移), 子节点对齐父节点
                var x = new Dictionary<string, float>();
                float maxX = 0f;
                for (int d = 0; d <= maxD; d++)
                {
                    var row = rows[d];
                    row.Sort(delegate (TechNodeVM a, TechNodeVM b)
                    {
                        int c = Desired(a, byId, x).CompareTo(Desired(b, byId, x));
                        if (c != 0) return c;
                        return string.Compare(a.Definition.Id, b.Definition.Id, StringComparison.Ordinal);
                    });
                    float prev = -999f;
                    int i2 = 0;
                    while (i2 < row.Count)
                    {
                        float want = Desired(row[i2], byId, x);
                        if (want < 0f) want = 0f;
                        int j = i2;
                        while (j + 1 < row.Count && Math.Abs(Desired(row[j + 1], byId, x) - want) < 0.01f) j++;
                        int cnt = j - i2 + 1;
                        float start = (float)Math.Round(want - (cnt - 1) / 2.0);
                        if (start < prev + 1f) start = prev + 1f;
                        if (start < 0f) start = 0f;
                        for (int k = i2; k <= j; k++)
                        {
                            float v = start + (k - i2);
                            x[row[k].Definition.Id] = v;
                            prev = v;
                            if (v > maxX) maxX = v;
                        }
                        i2 = j + 1;
                    }
                }
                _cols = maxX + 1f;

                // ③ 行的 Y + 时代条带(相邻同代行合并成带)
                var rowEra = new int[maxD + 1];
                for (int d = 0; d <= maxD; d++)
                {
                    int best = 1, bestN = -1;
                    for (int e = 1; e <= 5; e++)
                    {
                        int n = 0;
                        for (int i = 0; i < rows[d].Count; i++)
                            if (TechEraStyle.EraOf(rows[d][i].Definition) == e) n++;
                        if (n > bestN) { bestN = n; best = e; }
                    }
                    rowEra[d] = best;
                }
                var bandTop = new float[maxD + 1];
                var bandRow = new int[maxD + 1];
                var bands = new List<int>();
                var bandEras = new List<int>();
                float totalH = 0f;
                int cur = 0;
                while (cur <= maxD)
                {
                    int e = rowEra[cur], end = cur;
                    while (end + 1 <= maxD && rowEra[end + 1] == e) end++;
                    bands.Add(cur); bandEras.Add(e);
                    for (int d = cur; d <= end; d++) { bandTop[d] = totalH; bandRow[d] = d - cur; }
                    totalH += BandPadTop + (end - cur + 1) * RowPitch + BandGap;
                    cur = end + 1;
                }
                if (totalH < 1f) totalH = 1f;

                // ④ 初始视图: 100% 尺寸(字号保证), 从左上开始
                if (!_fit)
                {
                    _fit = true;
                    _scale = 1f;
                    _offsetX = 18f;
                    _offsetY = 2f;
                }

                _contentW = (LeftPad + _cols * (NodeW + ColGap) + 20f) * _scale;
                _contentH = totalH * _scale + 20f;
                ClampOffsets();
                _treeW = _contentW + _offsetX;
                _treeH = _contentH + _offsetY;
                OnPropertyChangedWithValue(_treeW, "TreeWidth");
                OnPropertyChangedWithValue(_treeH, "TreeHeight");

                // ⑤ 节点位置
                for (int i = 0; i < Nodes.Count; i++)
                {
                    var n = Nodes[i];
                    int d = depth[n.Definition.Id];
                    float c = 0f; x.TryGetValue(n.Definition.Id, out c);
                    n.PosX = (LeftPad + c * (NodeW + ColGap)) * _scale + _offsetX;
                    n.PosY = (bandTop[d] + BandPadTop + bandRow[d] * RowPitch) * _scale + _offsetY;
                    n.Width = NodeW * _scale;
                    n.Height = NodeH * _scale;
                    n.FontSize = Math.Max(12, (int)Math.Round(17f * _scale));
                    n.EffectSize = Math.Max(10, (int)Math.Round(12f * _scale));
                    n.BadgeSize = Math.Max(12, (int)Math.Round(18f * _scale));
                    n.SetScale(_scale);
                }

                // ⑥ 时代条带 + 标签 + 连线
                Lines.Clear();
                Heads.Clear();
                for (int b = 0; b < bands.Count; b++)
                {
                    int e = bandEras[b], d0 = bands[b];
                    int d1 = (b + 1 < bands.Count) ? bands[b + 1] - 1 : maxD;
                    float bx = (LeftPad - 8f) * _scale + _offsetX;
                    float by = (bandTop[d0] + 2f) * _scale + _offsetY;
                    float bw = (_cols * (NodeW + ColGap) + 12f) * _scale;
                    float bh = (BandPadTop + (d1 - d0 + 1) * RowPitch - 8f) * _scale;
                    var band = new FocusLineVM(bx, by, bw, bh, false);
                    band.Color = (e % 2 == 1) ? "#FFFFFF0A" : "#FFFFFF05";
                    Lines.Add(band);

                    Heads.Add(new TechHeadVM(
                        TechEraStyle.Name(e),
                        TechEraStyle.Color(e),
                        (LeftPad + 2f) * _scale + _offsetX,
                        (bandTop[d0] + 4f) * _scale + _offsetY,
                        Math.Max(13, (int)Math.Round(17f * _scale))));
                }

                // 高亮链: 当前研究节点 + 其全部前置祖先(V3 用青色高亮路径)
                var hi = new HashSet<string>();
                try
                {
                    string cid = Research.CurrentId[Tab];
                    if (!string.IsNullOrEmpty(cid))
                    {
                        var stack = new Stack<string>();
                        stack.Push(cid);
                        for (int guard = 0; guard < 512 && stack.Count > 0; guard++)
                        {
                            string id = stack.Pop();
                            if (!hi.Add(id)) continue;
                            TechNodeVM nn;
                            if (!byId.TryGetValue(id, out nn)) continue;
                            var reqs = Research.Reqs(nn.Definition);
                            for (int r = 0; r < reqs.Length; r++) stack.Push(reqs[r]);
                        }
                    }
                }
                catch { }

                for (int i = 0; i < Nodes.Count; i++)
                {
                    var n = Nodes[i];
                    var reqs = Research.Reqs(n.Definition);
                    for (int j = 0; j < reqs.Length; j++)
                    {
                        TechNodeVM p;
                        if (!byId.TryGetValue(reqs[j], out p)) continue;
                        bool hl = hi.Contains(n.Definition.Id) && hi.Contains(p.Definition.Id);
                        AddLink(p, n, hl);
                    }
                }
            }
            catch (Exception ex) { DLog.Force("科技树布局异常: " + ex.Message); }
        }

        // 树不能拖出可视区域(否则会遮住上方 UI / 拖出面板)
        private void ClampOffsets()
        {
            try
            {
                float fw = PanelScreen.FullWidth();
                float sh = PanelScreen.ScreenHeight();
                float viewW = Math.Min(1700f, fw);
                float viewH = Math.Max(140f, sh - TreeTopY - 34f);
                float minX = Math.Min(18f, viewW - _contentW);
                float minY = Math.Min(2f, viewH - _contentH);
                if (_offsetX > 18f) _offsetX = 18f;
                if (_offsetX < minX) _offsetX = minX;
                if (_offsetY > 2f) _offsetY = 2f;
                if (_offsetY < minY) _offsetY = minY;
            }
            catch { }
        }

        // 依赖深度 = max(所有前置深度) + 1
        private static int DepthOf(TechNodeVM n, Dictionary<string, TechNodeVM> byId, Dictionary<string, int> memo)
        {
            try
            {
                int cached;
                if (memo.TryGetValue(n.Definition.Id, out cached)) return cached;
                memo[n.Definition.Id] = 0;                 // 防环
                int d = 0;
                var reqs = Research.Reqs(n.Definition);
                for (int i = 0; i < reqs.Length; i++)
                {
                    TechNodeVM p;
                    if (!byId.TryGetValue(reqs[i], out p)) continue;
                    int pd = DepthOf(p, byId, memo) + 1;
                    if (pd > d) d = pd;
                }
                memo[n.Definition.Id] = d;
                return d;
            }
            catch { return 0; }
        }

        // 期望列号 = 所有前置列号的平均; 无前置返回 -1(排最左)
        private static float Desired(TechNodeVM n, Dictionary<string, TechNodeVM> byId, Dictionary<string, float> x)
        {
            try
            {
                var reqs = Research.Reqs(n.Definition);
                float sum = 0f; int cnt = 0;
                for (int i = 0; i < reqs.Length; i++)
                {
                    float c;
                    if (x.TryGetValue(reqs[i], out c)) { sum += c; cnt++; }
                }
                return cnt > 0 ? sum / cnt : -1f;
            }
            catch { }
            return -1f;
        }

        // 连线(照 V3): 粗棕曲线(3px) + 当前研究路径青色高亮(8px 半透明)
        private void AddLink(TechNodeVM p, TechNodeVM n, bool highlight)
        {
            try
            {
                float x1 = p.PosX + p.IconSize * 0.5f + p.MedLeft, y1 = p.PosY + p.Height;
                float x2 = n.PosX + n.IconSize * 0.5f + n.MedLeft, y2 = n.PosY;
                if (y2 - y1 < 2f && Math.Abs(x2 - x1) < 2f) return;
                bool done = Research.IsDone(p.Definition.Id) && Research.IsDone(n.Definition.Id);
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

        // 点击节点: 详情 + 开始研究
        internal void NodeAction(int idx)
        {
            try
            {
                if (idx < 0 || idx >= Nodes.Count) return;
                var tech = Nodes[idx].Definition;
                bool done = Research.IsDone(tech.Id);
                bool cur = Research.CurrentId[tech.Tree] == tech.Id;
                float cost = Research.CostOf(tech.Id);
                float prog = 0f;
                try { Research.Progress.TryGetValue(tech.Id, out prog); } catch { }
                var reqs = Research.Reqs(tech);
                string reqText = "无";
                if (reqs.Length > 0)
                {
                    reqText = "";
                    for (int i = 0; i < reqs.Length; i++)
                    {
                        var p = Find(reqs[i]);
                        if (reqText.Length > 0) reqText += " + ";
                        reqText += (p != null ? p.Name : reqs[i]) + (Research.IsDone(reqs[i]) ? "(已完成)" : "(未完成)");
                    }
                }
                string srcText = "";
                try
                {
                    string sid;
                    if (Research.Of(Research.PlayerKingdom()).SpreadFrom.TryGetValue(tech.Id, out sid))
                    {
                        var sk = Treaties.FindKingdom(sid);
                        srcText = "扩散来源: " + (sk != null ? sk.Name.ToString() : sid) + "\n";
                    }
                }
                catch { }
                string unlockText = "";
                try
                {
                    string un = Equipment.UnlockedByTech(tech.Id);
                    if (!string.IsNullOrEmpty(un)) unlockText = "解锁: " + un + "\n";
                }
                catch { }
                string body = Research.TreeNames[tech.Tree] + "科技 · 时代 " + TechEraStyle.Name(tech.Era) + "\n"
                    + "效果: " + tech.Effect + "\n"
                    + unlockText
                    + "前置: " + reqText + "\n"
                    + srcText
                    + "创新成本: " + (int)cost + "\n"
                    + (done ? "状态: 已完成" : (cur ? ("进度: " + (int)prog + "/" + (int)cost) : "状态: " + (Research.ReqOk(tech) ? "可研究" : "前置未完成"))) + "\n\n"
                    + (done ? "该科技已研究完成。" : (cur ? "正在研究中。" : (Research.ReqOk(tech) ? "点击「开始研究」将其设为该树当前研究(进度保留)。" : "需要先完成前置科技。")));

                var opts = new List<InquiryElement>();
                if (!done && !cur && Research.ReqOk(tech))
                    opts.Add(new InquiryElement("start", "开始研究", null, true, null));
                opts.Add(new InquiryElement("ok", "关闭", null, true, null));
                PanelInputGuard.ShowPopup(new MultiSelectionInquiryData(
                    tech.Name, body, opts, true, 1, 1, "确定", "取消",
                    delegate (List<InquiryElement> sel)
                    {
                        try
                        {
                            if (sel != null && sel.Count > 0 && (string)sel[0].Identifier == "start")
                            {
                                MapSelection.Message(Research.SetResearch(tech.Tree, tech.Id));
                                Refresh();
                            }
                        }
                        catch (Exception ex) { DLog.Force("选择研究失败: " + ex.Message); }
                    }, null, null, false));
            }
            catch (Exception ex) { DLog.Force("科技详情失败: " + ex.Message); }
        }

        private static TechDef Find(string id)
        {
            try
            {
                foreach (var t in Research.All) if (t != null && t.Id == id) return t;
            }
            catch { }
            return null;
        }

        internal float Scale { get { return _scale; } }
    }
}
