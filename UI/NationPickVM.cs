using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core.ViewModelCollection.ImageIdentifiers;
using TaleWorlds.Library;

namespace FeudalInternalAffairs
{
    // 简介行(自适应高度: 行数由内容决定)
    public class PickLineVM : ViewModel
    {
        private readonly string _text;
        internal PickLineVM(string text) { _text = text; }
        [DataSourceProperty] public string Text { get { return _text; } }
    }

    // v4.75p: 信息行(左标签 + 右数值, 可着色; 段落标题行=金色标签+空值)
    public class PickInfoVM : ViewModel
    {
        private string _label = "";
        private string _value = "";
        private string _labelColor = "#C8B98FFF";
        private string _valueColor = "#F0E4C8FF";

        [DataSourceProperty]
        public string Label { get { return _label; } set { if (_label != value) { _label = value; OnPropertyChangedWithValue(value, "Label"); } } }
        [DataSourceProperty]
        public string Value { get { return _value; } set { if (_value != value) { _value = value; OnPropertyChangedWithValue(value, "Value"); } } }
        [DataSourceProperty]
        public string LabelColor { get { return _labelColor; } set { if (_labelColor != value) { _labelColor = value; OnPropertyChangedWithValue(value, "LabelColor"); } } }
        [DataSourceProperty]
        public string ValueColor { get { return _valueColor; } set { if (_valueColor != value) { _valueColor = value; OnPropertyChangedWithValue(value, "ValueColor"); } } }
    }

    // 国家加成行(绿=增益 / 红=减益)
    public class TraitLineVM : ViewModel
    {
        private readonly string _text;
        private readonly string _color;

        internal TraitLineVM(string text, bool good)
        {
            _text = text;
            _color = good ? "#6FD86FFF" : "#D96A5AFF";
        }

        [DataSourceProperty] public string Text { get { return _text; } }
        [DataSourceProperty] public string Color { get { return _color; } }
    }

    // v4.75p: 地图选国侧滑栏(右侧) —— 国旗/国名/君主 + 可滚动信息列表(该国全部信息) + 开始游戏
    public class NationPickVM : ViewModel
    {
        private bool _open;
        private string _kingdomName = "";
        private string _rulerName = "";
        private string _cultureText = "";
        private string _colorBar = "#3B2E1EFF";
        private ImageIdentifierVM _flag;
        private readonly List<PickInfoVM> _allLines = new List<PickInfoVM>();
        private int _scrollTop;
        private int _visibleCount = 16;

        internal NationPickVM()
        {
            RecalcVisibleRows();
            for (int i = 0; i < _visibleCount; i++) VisibleLines.Add(new PickInfoVM());
        }

        // v4.75q: 可见行数按屏幕高度铺满(消除下方空白): 列表顶 160 + 底部预留(提示96+按钮58+24+余量38)
        private void RecalcVisibleRows()
        {
            try
            {
                float sh = TaleWorlds.Engine.Screen.RealScreenResolution.Y;
                int rows = (int)((sh - 160f - 216f) / 30f);
                if (rows < 8) rows = 8;
                if (rows > 32) rows = 32;
                _visibleCount = rows;
            }
            catch { _visibleCount = 16; }
            while (VisibleLines.Count < _visibleCount) VisibleLines.Add(new PickInfoVM());
            while (VisibleLines.Count > _visibleCount) VisibleLines.RemoveAt(VisibleLines.Count - 1);
        }

        [DataSourceProperty] public MBBindingList<PickInfoVM> VisibleLines { get; } = new MBBindingList<PickInfoVM>();

        [DataSourceProperty]
        public ImageIdentifierVM Flag
        {
            get { return _flag; }
            set { if (_flag != value) { _flag = value; OnPropertyChangedWithValue(value, "Flag"); } }
        }

        [DataSourceProperty] public string Id { get { try { return _flag != null ? _flag.Id : ""; } catch { return ""; } } }
        [DataSourceProperty] public string AdditionalArgs { get { try { return _flag != null ? _flag.AdditionalArgs : ""; } catch { return ""; } } }
        [DataSourceProperty] public string TextureProviderName { get { try { return _flag != null ? _flag.TextureProviderName : ""; } catch { return ""; } } }

        [DataSourceProperty]
        public string KingdomName { get { return _kingdomName; } set { if (_kingdomName != value) { _kingdomName = value; OnPropertyChangedWithValue(value, "KingdomName"); } } }

        [DataSourceProperty]
        public string RulerName { get { return _rulerName; } set { if (_rulerName != value) { _rulerName = value; OnPropertyChangedWithValue(value, "RulerName"); } } }

        [DataSourceProperty]
        public string CultureText { get { return _cultureText; } set { if (_cultureText != value) { _cultureText = value; OnPropertyChangedWithValue(value, "CultureText"); } } }

        [DataSourceProperty]
        public string ColorBar { get { return _colorBar; } set { if (_colorBar != value) { _colorBar = value; OnPropertyChangedWithValue(value, "ColorBar"); } } }

        // ===== 滑入/滑出(右侧: 从屏外滑到贴右边缘) =====
        private float _offset = -550f;
        private float _target = -550f;
        private bool _visible;

        [DataSourceProperty]
        public float PanelOffset
        {
            get { return _offset; }
            set { if (Math.Abs(_offset - value) > 0.5f) { _offset = value; OnPropertyChangedWithValue(value, "PanelOffset"); } }
        }

        [DataSourceProperty]
        public bool IsPanelVisible
        {
            get { return _visible; }
            set { if (_visible != value) { _visible = value; OnPropertyChangedWithValue(value, "IsPanelVisible"); } }
        }

        [DataSourceProperty]
        public bool IsPanelOpen
        {
            get { return _open; }
            set { if (_open != value) { _open = value; OnPropertyChangedWithValue(value, "IsPanelOpen"); } }
        }

        internal void TickAnim(float dt)
        {
            try
            {
                if (Math.Abs(_offset - _target) < 1f)
                {
                    if (_offset != _target) PanelOffset = _target;
                }
                else
                {
                    float speed = 550f / 0.12f;
                    float step = speed * Math.Max(0.001f, Math.Min(dt, 0.1f));
                    float next = _offset + (_target > _offset ? step : -step);
                    if ((_target > _offset && next > _target) || (_target < _offset && next < _target)) next = _target;
                    PanelOffset = next;
                }
                if (_target > -550f + 1f) IsPanelVisible = true;
                else if (Math.Abs(_offset - _target) < 1f && _target <= -550f + 1f) IsPanelVisible = false;
            }
            catch { }
        }

        internal void Show(Kingdom k)
        {
            try
            {
                if (k == null) return;
                IsPanelOpen = true;
                _target = 0f;
                IsPanelVisible = true;
                Refresh(k);
            }
            catch { }
        }

        internal void Hide()
        {
            try
            {
                IsPanelOpen = false;
                _target = -550f;
            }
            catch { }
        }

        // 滚轮滚动(±1 行)
        internal void Scroll(int dir)
        {
            try
            {
                int max = Math.Max(0, _allLines.Count - _visibleCount);
                int next = _scrollTop + dir;
                if (next < 0) next = 0;
                if (next > max) next = max;
                if (next == _scrollTop) return;
                _scrollTop = next;
                ApplyVisible();
            }
            catch { }
        }

        private void ApplyVisible()
        {
            for (int i = 0; i < _visibleCount && i < VisibleLines.Count; i++)
            {
                var row = VisibleLines[i];
                int idx = _scrollTop + i;
                if (idx < _allLines.Count)
                {
                    var src = _allLines[idx];
                    if (row.Label != src.Label) row.Label = src.Label;
                    if (row.Value != src.Value) row.Value = src.Value;
                    if (row.LabelColor != src.LabelColor) row.LabelColor = src.LabelColor;
                    if (row.ValueColor != src.ValueColor) row.ValueColor = src.ValueColor;
                }
                else
                {
                    if (row.Label != "") row.Label = "";
                    if (row.Value != "") row.Value = "";
                }
            }
        }

        // ================= 填充 =================
        private void Refresh(Kingdom k)
        {
            try
            {
                _scrollTop = 0;
                RecalcVisibleRows();
                KingdomName = k.Name != null ? k.Name.ToString() : k.StringId;
                var ruler = k.Leader;
                RulerName = ruler != null && ruler.Name != null ? "君主: " + ruler.Name.ToString() : "君主: —";
                CultureText = k.Culture != null && k.Culture.Name != null ? "文化: " + k.Culture.Name.ToString() : "";

                try
                {
                    var c = Color.FromUint(k.Color);
                    ColorBar = "#" + ((int)Math.Round(Math.Max(0f, Math.Min(1f, c.Red)) * 255f)).ToString("X2")
                        + ((int)Math.Round(Math.Max(0f, Math.Min(1f, c.Green)) * 255f)).ToString("X2")
                        + ((int)Math.Round(Math.Max(0f, Math.Min(1f, c.Blue)) * 255f)).ToString("X2") + "FF";
                }
                catch { ColorBar = "#3B2E1EFF"; }

                try { if (k.Banner != null) Flag = new BannerImageIdentifierVM(k.Banner, false); } catch { }

                BuildLines(k);
                ApplyVisible();
            }
            catch (Exception ex) { DLog.Force("选国侧栏刷新异常: " + ex.Message); }
        }

        private const string Gold = "#C9A227FF";
        private const string Grey = "#C8B98FFF";
        private const string Light = "#F0E4C8FF";
        private const string Green = "#6FD86FFF";
        private const string Red = "#D96A5AFF";

        private void Header(string text) { _allLines.Add(new PickInfoVM { Label = "▍" + text, Value = "", LabelColor = Gold, ValueColor = Gold }); }
        private void Row(string label, string value, string lc = Grey, string vc = Light) { _allLines.Add(new PickInfoVM { Label = label, Value = value, LabelColor = lc, ValueColor = vc }); }

        private void BuildLines(Kingdom k)
        {
            _allLines.Clear();

            // ==== 国家简介(置顶) ====
            Header("国家简介");
            string desc = null;
            try { if (k.EncyclopediaText != null) desc = k.EncyclopediaText.ToString(); } catch { }
            if (string.IsNullOrEmpty(desc) && k.Culture != null && k.Culture.EncyclopediaText != null)
                desc = k.Culture.EncyclopediaText.ToString();
            if (string.IsNullOrEmpty(desc)) desc = "一个古老而强盛的王国, 正等待一位意志来执掌它的命运。";
            desc = desc.Replace("\n", " ").Replace("\r", " ");
            const int perLine = 22;
            while (desc.Length > 0)
            {
                if (desc.Length <= perLine) { Row(desc, "", Grey, Grey); desc = ""; }
                else { Row(desc.Substring(0, perLine), "", Grey, Grey); desc = desc.Substring(perLine); }
            }

            // ==== 国家专属加成(第二) ====
            Header("国家专属加成");
            try
            {
                var lines = KingdomTraits.GetTraitLines(k);
                for (int i = 0; i < lines.Count; i++)
                    Row((lines[i].Key ? "▲ " : "▼ ") + lines[i].Value, "", lines[i].Key ? Green : Red, Grey);
                if (lines.Count == 0) Row("(该国尚无专属特性)", "", Grey, Grey);
            }
            catch { }

            // ==== 军事 ====
            Header("军事");
            long troops = 0;
            int armies = 0, wars = 0;
            try
            {
                foreach (var p in MobileParty.All)
                {
                    if (p == null || !p.IsActive) continue;
                    if (p.MapFaction != k) continue;
                    try { troops += p.MemberRoster != null ? p.MemberRoster.TotalManCount : 0; } catch { }
                    if (p.Army != null && ReferenceEquals(p.Army.LeaderParty, p)) armies++;
                }
                foreach (var c in k.Clans)
                {
                    if (c == null) continue;
                    try { wars += c.WarPartyComponents != null ? c.WarPartyComponents.Count : 0; } catch { }
                }
            }
            catch { }
            Row("总兵力", troops.ToString("N0") + " 兵");
            Row("野战军团", armies + " 支");
            Row("领主部队", wars + " 支");

            // ==== 经济 ====
            Header("经济");
            string goldText = "—";
            try
            {
                float g;
                if (WarEconomy.Gold.TryGetValue(k.StringId, out g)) goldText = ((int)g).ToString("N0");
            }
            catch { }
            string foodText = "—";
            try
            {
                float days;
                WarEconomy.FoodDaysOf(k, out days);
                foodText = days >= 90f ? "充足" : days.ToString("F1") + " 天";
            }
            catch { }
            bool crisis = WarEconomy.IsCrisis(k);
            Row("国库", goldText + (goldText != "—" ? " 第纳尔" : ""));
            Row("粮食储备", foodText);
            Row("经济危机", crisis ? "是" : "否", Grey, crisis ? Red : Green);

            // ==== 领土 ====
            Header("领土");
            int towns = 0, castles = 0, villages = 0;
            float prosperity = 0f, hearth = 0f;
            long pop = 0;
            try
            {
                foreach (var s in k.Settlements)
                {
                    if (s == null) continue;
                    if (s.IsTown)
                    {
                        towns++;
                        if (s.Town != null) prosperity += s.Town.Prosperity;
                    }
                    else if (s.IsCastle) castles++;
                    else if (s.IsVillage)
                    {
                        villages++;
                        if (s.Village != null) hearth += s.Village.Hearth;
                    }
                    try
                    {
                        var list = Pops.Of(s.StringId);
                        if (list != null)
                            for (int i = 0; i < list.Count; i++)
                            {
                                var pp = list[i];
                                if (pp != null) pop += (long)pp.Size;
                            }
                    }
                    catch { }
                }
            }
            catch { }
            Row("城池", towns + " 城 · " + castles + " 堡 · " + villages + " 村");
            Row("总繁荣度", ((int)prosperity).ToString("N0"));
            Row("村庄户数", ((int)hearth).ToString("N0"));
            Row("总人口", pop.ToString("N0") + " 人");

            // ==== 外交 ====
            Header("外交");
            var warNames = new List<string>();
            var allyNames = new List<string>();
            try
            {
                foreach (var other in Kingdom.All)
                {
                    if (other == null || other == k) continue;
                    if (other.IsMinorFaction || other.IsBanditFaction) continue;
                    if (k.IsAtWarWith(other)) warNames.Add(other.Name != null ? other.Name.ToString() : other.StringId);
                    else if (Diplomacy.IsAlly(k, other)) allyNames.Add(other.Name != null ? other.Name.ToString() : other.StringId);
                }
            }
            catch { }
            Row("交战中", warNames.Count > 0 ? string.Join("、", warNames) : "无", Grey, warNames.Count > 0 ? Red : Green);
            Row("同盟", allyNames.Count > 0 ? string.Join("、", allyNames) : "无", Grey, allyNames.Count > 0 ? Green : Grey);
            Row("交战国家数", warNames.Count.ToString());

            // ==== 概况 ====
            Header("概况");
            int clans = 0;
            float influence = 0f;
            try { clans = k.Clans != null ? k.Clans.Count : 0; } catch { }
            try { influence = k.RulingClan != null ? k.RulingClan.Influence : 0f; } catch { }
            Row("统治家族", k.RulingClan != null && k.RulingClan.Name != null ? k.RulingClan.Name.ToString() : "—");
            Row("家族影响力", ((int)influence).ToString());
            Row("家族数量", clans.ToString());

            // ==== 国势 ====
            Header("国势");
            float wear = 0f;
            try { wear = WarWeariness.MaxWearOf(k); } catch { }
            Row("最高厌战", ((int)wear).ToString(), Grey, wear >= 70f ? Red : (wear >= 40f ? Light : Green));
        }

        // "开始游戏"回调(由 NationPickPanel 注入)
        internal Action OnStartGame;
    }
}
