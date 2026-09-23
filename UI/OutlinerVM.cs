using System;
using System.Collections.Generic;
using TaleWorlds.Library;

namespace FeudalInternalAffairs
{
    public class OutlinerItemVM : ViewModel
    {
        internal readonly int Section;
        internal readonly Action Click;
        internal float RelY;

        private readonly float _height;
        private readonly bool _header, _plain, _hasIcon;
        private readonly string _icon, _name, _value, _valueColor, _extra, _extraColor, _arrow, _count, _bg;
        private bool _hover;

        internal OutlinerItemVM(bool header, bool plain, int section, string icon, string name, string value, string valueColor,
            string extra, string extraColor, string arrow, string count, string bg, float height, Action click)
        {
            _header = header;
            _plain = plain;
            Section = section;
            _icon = icon != null ? icon : "";
            _hasIcon = !plain && _icon.Length > 0;
            _name = Fit(name, header ? 230f : 200f);
            _value = Fit(value, 76f);
            _valueColor = !string.IsNullOrEmpty(valueColor) ? valueColor : "#C8B98FCC";
            _extra = Fit(extra, 80f);
            _extraColor = !string.IsNullOrEmpty(extraColor) ? extraColor : "#8A8070FF";
            _arrow = arrow != null ? arrow : "";
            _count = count != null ? count : "";
            _bg = !string.IsNullOrEmpty(bg) ? bg : "#00000000";
            _height = height;
            Click = click;
        }

        [DataSourceProperty] public float Height { get { return _height; } }
        [DataSourceProperty] public bool IsHeader { get { return _header; } }
        [DataSourceProperty] public bool IsRow { get { return !_header && !_plain; } }
        [DataSourceProperty] public bool IsPlain { get { return _plain; } }
        [DataSourceProperty] public bool ShowHeaderIcon { get { return _header && _hasIcon; } }
        [DataSourceProperty] public bool ShowRowIcon { get { return !_header && _hasIcon; } }
        [DataSourceProperty] public string Icon { get { return _icon; } }
        [DataSourceProperty] public string Name { get { return _name; } }
        [DataSourceProperty] public string Value { get { return _value; } }
        [DataSourceProperty] public string ValueColor { get { return _valueColor; } }
        [DataSourceProperty] public string Extra { get { return _extra; } }
        [DataSourceProperty] public string ExtraColor { get { return _extraColor; } }
        [DataSourceProperty] public string Arrow { get { return _arrow; } }
        [DataSourceProperty] public string Count { get { return _count; } }
        [DataSourceProperty] public string Bg { get { return _hover ? "#FFFFFF14" : _bg; } }

        internal void SetHover(bool v)
        {
            if (_hover == v) return;
            _hover = v;
            OnPropertyChangedWithValue(Bg, "Bg");
        }

        // 按估算像素宽截断(中文15px/字, 其余9px/字), 超长加省略号, 防止文字出框
        private static string Fit(string s, float maxPx)
        {
            if (string.IsNullOrEmpty(s)) return s;
            float w = 0f;
            for (int i = 0; i < s.Length; i++) w += s[i] >= 0x2E80 ? 15f : 9f;
            if (w <= maxPx) return s;
            float budget = maxPx - 15f;
            w = 0f;
            for (int i = 0; i < s.Length; i++)
            {
                float cw = s[i] >= 0x2E80 ? 15f : 9f;
                if (w + cw > budget) return s.Substring(0, i) + "…";
                w += cw;
            }
            return s;
        }
    }

    public class OutlinerVM : ViewModel
    {
        internal const int SectionCount = 7;
        internal static readonly bool[] Collapsed = new bool[SectionCount];
        internal static bool CollapsedAll = true;

        internal const float PanelW = 400f;
        internal const float StripW = 44f;
        internal const float TopMargin = 0f;
        internal const float BottomMargin = 0f;
        internal const float ListMargin = 46f;
        internal const float ListTopAbs = TopMargin + ListMargin;
        internal const float RightMargin = 0f;

        private const int MaxRows = 8;
        private const float HeaderH = 34f;
        private const float RowH = 32f;
        private const float PlainH = 28f;
        private const float GapH = 0f;

        private const string Green = "#7BC96FFF";
        private const string Red = "#D96A5AFF";
        private const string Grey = "#8A8070FF";
        private const string Pale = "#C8B98FCC";
        private const string Gold = "#E8C33AFF";

        private readonly List<OutlinerItemVM> _all = new List<OutlinerItemVM>();
        private readonly List<OutlinerItemVM> _visible = new List<OutlinerItemVM>();
        private int _scroll;
        private int _hover = -1;
        private DateTime _lastRefresh = DateTime.MinValue;
        private bool _isExpanded;

        public OutlinerVM()
        {
            Items = new MBBindingList<OutlinerItemVM>();
            _isExpanded = !CollapsedAll;
            Rebuild();
            _lastRefresh = DateTime.UtcNow;
        }

        [DataSourceProperty] public MBBindingList<OutlinerItemVM> Items { get; private set; }
        [DataSourceProperty] public bool IsExpanded { get { return _isExpanded; } }
        [DataSourceProperty] public bool IsCollapsed { get { return !_isExpanded; } }
        [DataSourceProperty] public float PanelWidth { get { return _isExpanded ? PanelW : StripW; } }
        [DataSourceProperty] public float HandleRight { get { return RightMargin + PanelWidth + 2f; } }
        [DataSourceProperty] public string HandleArrow { get { return _isExpanded ? "▶" : "◀"; } }
        [DataSourceProperty] public string StripSummary { get { return "概\n览"; } }

        private class RowData
        {
            internal string Icon, Name, Value, ValueColor, Extra, ExtraColor;
            internal float Sort;
            internal Action Click;
        }

        private class SectionData
        {
            internal string Name, Icon;
            internal readonly List<RowData> Rows = new List<RowData>();
        }

        internal bool Expanded { get { return _isExpanded; } }

        internal void TickRefresh()
        {
            try
            {
                if (!_isExpanded) return;
                if ((DateTime.UtcNow - _lastRefresh).TotalSeconds < 1.0) return;
                _lastRefresh = DateTime.UtcNow;
                Rebuild();
            }
            catch { }
        }

        internal void ToggleAll()
        {
            try
            {
                _isExpanded = !_isExpanded;
                CollapsedAll = !_isExpanded;
                OnPropertyChangedWithValue(_isExpanded, "IsExpanded");
                OnPropertyChangedWithValue(IsCollapsed, "IsCollapsed");
                OnPropertyChangedWithValue(PanelWidth, "PanelWidth");
                OnPropertyChangedWithValue(HandleRight, "HandleRight");
                OnPropertyChangedWithValue(HandleArrow, "HandleArrow");
                if (_isExpanded) Rebuild();
            }
            catch { }
        }

        private void ToggleSection(int s)
        {
            try
            {
                if (s < 0 || s >= SectionCount) return;
                Collapsed[s] = !Collapsed[s];
                Rebuild();
            }
            catch { }
        }

        internal void Activate(int i)
        {
            try
            {
                var it = ItemAt(i);
                if (it == null) return;
                if (it.IsHeader) { ToggleSection(it.Section); return; }
                if (it.Click != null) it.Click();
            }
            catch (Exception ex) { DLog.Force("概览栏点击失败: " + ex.Message); }
        }

        internal void Scroll(int dir)
        {
            try
            {
                if (_all.Count == 0) return;
                int max = Math.Max(0, Math.Min(_all.Count - 1, MaxStartIndex()));
                int next = _scroll + dir * 2;
                if (next < 0) next = 0;
                if (next > max) next = max;
                if (next == _scroll) return;
                _scroll = next;
                ApplyWindow();
            }
            catch { }
        }

        internal void SetHover(int i)
        {
            try
            {
                if (i == _hover) return;
                if (_hover >= 0 && _hover < _visible.Count) _visible[_hover].SetHover(false);
                _hover = i;
                if (i >= 0 && i < _visible.Count) _visible[i].SetHover(true);
            }
            catch { }
        }

        internal OutlinerItemVM ItemAt(int i)
        {
            return (i >= 0 && i < _visible.Count) ? _visible[i] : null;
        }

        internal int HitTest(float relY)
        {
            try
            {
                for (int i = 0; i < _visible.Count; i++)
                {
                    var it = _visible[i];
                    if (relY >= it.RelY && relY < it.RelY + it.Height + GapH) return i;
                }
            }
            catch { }
            return -1;
        }

        internal static float VisibleArea()
        {
            float h = 1080f;
            try { h = TaleWorlds.Engine.Screen.RealScreenResolutionHeight; } catch { }
            if (h <= 100f) h = 1080f;
            float area = h - ListTopAbs - BottomMargin - 14f;
            return area < 140f ? 140f : area;
        }

        // 从尾往前按行高累计, 求最后一屏的起始索引; 内容不足一屏时返回 0
        private int MaxStartIndex()
        {
            try
            {
                int n = _all.Count;
                if (n <= 1) return 0;
                float area = VisibleArea();
                float y = 0f;
                for (int i = n - 1; i >= 0; i--)
                {
                    float h = _all[i].Height + GapH;
                    if (y + h > area && i < n - 1) return i + 1;
                    y += h;
                }
                return 0;
            }
            catch { return 0; }
        }

        private void Rebuild()
        {
            try
            {
                _all.Clear();
                var secs = new List<SectionData>();
                secs.Add(BuildMarket());
                secs.Add(BuildGroups());
                secs.Add(BuildMovements());
                secs.Add(BuildLobbying());
                secs.Add(BuildTreaties());
                secs.Add(BuildArmy());
                secs.Add(BuildRailways());
                for (int s = 0; s < secs.Count; s++)
                {
                    var sec = secs[s];
                    bool collapsed = s < SectionCount && Collapsed[s];
                    _all.Add(new OutlinerItemVM(true, false, s, sec.Icon, sec.Name, "", "", "", "", collapsed ? "▸" : "▾",
                        sec.Rows.Count.ToString(), "#00000000", HeaderH, MakeToggle(s)));
                    if (collapsed) continue;
                    if (sec.Rows.Count == 0)
                    {
                        _all.Add(new OutlinerItemVM(false, true, s, "", "（无）", "", "", "", "", "", "", "#00000000", PlainH, null));
                        continue;
                    }
                    int n = sec.Rows.Count < MaxRows ? sec.Rows.Count : MaxRows;
                    for (int i = 0; i < n; i++)
                    {
                        var r = sec.Rows[i];
                        _all.Add(new OutlinerItemVM(false, false, s, r.Icon, r.Name, r.Value, r.ValueColor, r.Extra, r.ExtraColor,
                            "", "", "#00000000", RowH, r.Click));
                    }
                    if (sec.Rows.Count > MaxRows)
                        _all.Add(new OutlinerItemVM(false, true, s, "", "…共 " + sec.Rows.Count + " 条", "", "", "", "", "", "", "#00000000", PlainH, null));
                }
                if (_all.Count == 0) _scroll = 0;
                int maxStart = Math.Max(0, Math.Min(_all.Count - 1, MaxStartIndex()));
                if (_scroll > maxStart) _scroll = maxStart;
                if (_scroll < 0) _scroll = 0;
                ApplyWindow();
            }
            catch (Exception ex) { DLog.Force("概览栏刷新失败: " + ex.Message); }
        }

        private Action MakeToggle(int s)
        {
            return delegate { ToggleSection(s); };
        }

        // 紧凑数值: <1万 用千分位, >=1万 用"万", 保证数值列(60px)不出框
        private static string Num(int v)
        {
            int a = v < 0 ? -v : v;
            if (a >= 10000) return (v / 10000.0).ToString("0.#") + "万";
            return v.ToString("N0");
        }

        private void ApplyWindow()
        {
            try
            {
                _visible.Clear();
                Items.Clear();
                _hover = -1;
                float area = VisibleArea();
                float y = 0f;
                for (int i = _scroll; i < _all.Count; i++)
                {
                    var it = _all[i];
                    float h = it.Height + GapH;
                    if (y > 0f && y + h > area) break;
                    it.RelY = y;
                    _visible.Add(it);
                    Items.Add(it);
                    y += h;
                }
            }
            catch { }
        }

        private SectionData BuildMarket()
        {
            var sec = new SectionData { Name = "市场", Icon = "fia_cat_trade" };
            try
            {
                var nat = EconomyWorld.National;
                for (int i = 0; i < FeudalGoods.Main.Count; i++)
                {
                    var g = FeudalGoods.Main[i];
                    if (g == null) continue;
                    float baseP = g.BasePrice > 0.01f ? g.BasePrice : FeudalGoods.BasePrice(g.Id);
                    if (baseP <= 0.01f) continue;
                    float price = nat != null ? nat.PriceOf(g.Id) : baseP;
                    float dev = price / baseP - 1f;
                    float buy = 0f, sell = 0f;
                    try { if (nat != null) nat.BuyVolume.TryGetValue(g.Id, out buy); } catch { }
                    try { if (nat != null) nat.SellVolume.TryGetValue(g.Id, out sell); } catch { }
                    float gap = buy - sell;
                    float refv = Math.Max(1f, Math.Max(buy, sell));
                    bool shortage = gap > 0.05f * refv;
                    bool anomaly = Math.Abs(dev) >= 0.15f;
                    if (!shortage && !anomaly) continue;
                    int pct = (int)Math.Round(price / baseP * 100f);
                    string gname = g.Name;
                    float cp = price, cb = baseP, cbuy = buy, csell = sell;
                    bool cShort = shortage;
                    sec.Rows.Add(new RowData
                    {
                        Icon = g.Sprite,
                        Name = gname,
                        Value = pct + "%",
                        ValueColor = dev > 0.02f ? Red : (dev < -0.02f ? Green : Pale),
                        Extra = cShort ? "短缺" : "过剩",
                        ExtraColor = cShort ? Red : Green,
                        Sort = Math.Abs(dev),
                        Click = delegate
                        {
                            MapSelection.Message(gname + " · 现价 " + cp.ToString("F1") + " / 基础价 " + cb.ToString("F1")
                                + " (" + pct + "%) · 买单 " + cbuy.ToString("F1") + " · 卖单 " + csell.ToString("F1")
                                + (cShort ? " · 供不应求" : " · 供大于求"));
                        }
                    });
                }
                sec.Rows.Sort(delegate (RowData a, RowData b) { return b.Sort.CompareTo(a.Sort); });
            }
            catch { }
            return sec;
        }

        private SectionData BuildGroups()
        {
            var sec = new SectionData { Name = "利益集团", Icon = "fia_ig_crown" };
            try
            {
                for (int i = 0; i < InterestGroups.GroupCount; i++)
                {
                    var g = InterestGroups.G[i];
                    if (g == null) continue;
                    int idx = i;
                    int sat = g.Satisfaction;
                    int share = (int)Math.Round(g.Share * 100f);
                    string gname = g.Name;
                    string leader = g.Leader;
                    bool inGov = g.InGov;
                    sec.Rows.Add(new RowData
                    {
                        Icon = g.Icon,
                        Name = gname,
                        Value = (sat > 0 ? "+" : "") + sat,
                        ValueColor = sat >= 5 ? Green : (sat <= -5 ? Red : Pale),
                        Extra = share + "%",
                        ExtraColor = Grey,
                        Sort = g.Share,
                        Click = delegate
                        {
                            string st = "";
                            try { st = InterestGroups.StanceOf(idx); } catch { }
                            MapSelection.Message(gname + " · " + InterestGroups.SatLevelName(idx) + " " + sat
                                + " · 影响力占比 " + share + "% · 领袖 " + leader + " · " + (inGov ? "执政" : "在野")
                                + (string.IsNullOrEmpty(st) ? "" : "\n" + st));
                        }
                    });
                }
                sec.Rows.Sort(delegate (RowData a, RowData b) { return b.Sort.CompareTo(a.Sort); });
            }
            catch { }
            return sec;
        }

        private SectionData BuildMovements()
        {
            var sec = new SectionData { Name = "政治运动", Icon = "fia_lobby_anti_country" };
            try
            {
                for (int i = 0; i < InterestGroups.Movements.Count; i++)
                {
                    var m = InterestGroups.Movements[i];
                    if (m == null) continue;
                    int sup = (int)Math.Round(m.Support * 100f);
                    int rad = (int)m.Radical;
                    string gname = InterestGroups.NameOf(m.Group);
                    string demand = m.Law >= 0 ? ("《" + LawSystem.NameOf(m.Law) + "》") : "要求王室让步";
                    int startDay = m.StartDay;
                    sec.Rows.Add(new RowData
                    {
                        Icon = "fia_lobby_anti_country",
                        Name = gname + " · " + demand,
                        Value = sup + "%",
                        ValueColor = Pale,
                        Extra = "激进" + rad + "%",
                        ExtraColor = rad >= 50 ? Red : (rad >= 25 ? Gold : Green),
                        Sort = m.Support,
                        Click = delegate
                        {
                            MapSelection.Message(gname + " · 诉求 " + demand + "\n支持人口 " + sup + "% · 激进度 " + rad
                                + "% · 始于第 " + startDay + " 天");
                        }
                    });
                }
                sec.Rows.Sort(delegate (RowData a, RowData b) { return b.Sort.CompareTo(a.Sort); });
            }
            catch { }
            return sec;
        }

        private SectionData BuildLobbying()
        {
            var sec = new SectionData { Name = "政治游说团", Icon = "fia_lobby_fund_lobbies" };
            try
            {
                for (int i = 0; i < Lobbying.All.Count; i++)
                {
                    var d = Lobbying.All[i];
                    if (d == null) continue;
                    int cd = Lobbying.CooldownLeft(d.Id);
                    string lname = d.Name, desc = d.Desc, effect = d.Effect;
                    int gold = d.Gold;
                    sec.Rows.Add(new RowData
                    {
                        Icon = d.Icon,
                        Name = lname,
                        Value = cd > 0 ? (cd + "日") : (gold + "金"),
                        ValueColor = cd > 0 ? Grey : Gold,
                        Extra = cd > 0 ? "冷却" : "可发动",
                        ExtraColor = cd > 0 ? Grey : Green,
                        Sort = cd > 0 ? 1f : 0f,
                        Click = delegate
                        {
                            MapSelection.Message(lname + ": " + desc + "\n" + effect + " · 花费 " + gold + " 金"
                                + (cd > 0 ? (" · 冷却 " + cd + " 日") : ""));
                        }
                    });
                }
                sec.Rows.Sort(delegate (RowData a, RowData b) { return a.Sort.CompareTo(b.Sort); });
            }
            catch { }
            return sec;
        }

        private SectionData BuildTreaties()
        {
            var sec = new SectionData { Name = "条约", Icon = "fia_dip_alliance" };
            try
            {
                int today = Politics.Today();
                for (int i = 0; i < Treaties.All.Count; i++)
                {
                    var t = Treaties.All[i];
                    if (t == null || t.Broken || t.Left(today) <= 0) continue;
                    var k = Treaties.FindKingdom(t.Partner);
                    string kname = k != null && k.Name != null ? k.Name.ToString() : t.Partner;
                    string tname = Treaties.NameOf(t.Type);
                    string level = Treaties.LevelNames[Math.Min(2, Math.Max(0, t.Level - 1))];
                    int left = t.Left(today);
                    int monthly = t.Monthly;
                    string icon = "fia_dip_neutral";
                    try { icon = TreatyCandVM.IconOf(t.Type); } catch { }
                    sec.Rows.Add(new RowData
                    {
                        Icon = icon,
                        Name = kname + " · " + tname,
                        Value = "剩" + left + "日",
                        ValueColor = left <= 30 ? Red : Pale,
                        Extra = level,
                        ExtraColor = Grey,
                        Sort = left,
                        Click = delegate
                        {
                            MapSelection.Message(kname + " · " + tname + "·" + level + " · 剩 " + left + " 日 · 月收益 " + monthly);
                        }
                    });
                }
                sec.Rows.Sort(delegate (RowData a, RowData b) { return a.Sort.CompareTo(b.Sort); });
            }
            catch { }
            return sec;
        }

        private SectionData BuildArmy()
        {
            var sec = new SectionData { Name = "陆军", Icon = "fia_cat_military" };
            try
            {
                for (int i = 0; i < DefArmy.Legions.Count; i++)
                {
                    var lg = DefArmy.Legions[i];
                    if (lg == null) continue;
                    var p = DefArmy.LegionParty(lg);
                    if (p == null) continue;
                    string key = ArmyDoctrine.LegionKey(lg);
                    float org = ArmyDoctrine.OrgOf2(key);
                    int men = DefArmy.LegionMen(lg);
                    int exp = lg.Expected;
                    string name = MapSelection.NameOf(p);
                    var cp = p;
                    var cl = lg;
                    sec.Rows.Add(new RowData
                    {
                        Icon = MilIcons.IconOf("line_infantry"),
                        Name = name,
                        Value = Num(men),
                        ValueColor = exp > 0 && men < exp * 0.6f ? Red : Pale,
                        Extra = "组织" + ((int)org) + "%",
                        ExtraColor = org >= 60f ? Green : (org >= 30f ? Gold : Red),
                        Sort = men,
                        Click = delegate
                        {
                            var sh = new float[4];
                            string tactic = "—";
                            try { ArmyDoctrine.CompositionOf(cp, sh); } catch { }
                            try { float o, t2, m2; tactic = ArmyDoctrine.TacticOf(cp, out o, out t2, out m2); } catch { }
                            float mor = 0f;
                            try { mor = DefArmyStats.MoraleOf(cp.Party); } catch { }
                            string task = cl != null && !string.IsNullOrEmpty(cl.Task) ? cl.Task : "—";
                            MapSelection.Message(name + " · 兵力 " + men + "/" + exp + " · 组织 " + ((int)org) + "% · 士气 " + ((int)mor)
                                + "\n兵种 步 " + (int)(sh[0] * 100f) + "% 弓 " + (int)(sh[1] * 100f) + "% 骑 " + (int)(sh[2] * 100f)
                                + "% 骑射 " + (int)(sh[3] * 100f) + "% · 战术 " + tactic + " · 任务 " + task);
                        }
                    });
                }
                sec.Rows.Sort(delegate (RowData a, RowData b) { return b.Sort.CompareTo(a.Sort); });
            }
            catch { }
            return sec;
        }

        private SectionData BuildRailways()
        {
            var sec = new SectionData { Name = "铁路", Icon = "fia_mode_iron" };
            try
            {
                var pk = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                var lines = new List<RailLine>();
                if (pk != null)
                {
                    for (int i = 0; i < Railways.All.Count; i++)
                    {
                        var l = Railways.All[i];
                        if (l == null || l.Broken || l.OwnerId != pk.StringId) continue;
                        lines.Add(l);
                    }
                }
                int netTotal = 0;
                for (int i = 0; i < lines.Count; i++)
                {
                    int inc, up;
                    netTotal += Railways.DailyNetOf(lines[i], out inc, out up);
                }
                int lineCount = lines.Count;
                int netSum = netTotal;
                sec.Rows.Add(new RowData
                {
                    Icon = "fia_mode_iron",
                    Name = "线路 " + lineCount + " 条",
                    Value = (netSum >= 0 ? "+" : "") + Num(netSum),
                    ValueColor = netSum >= 0 ? Green : Red,
                    Extra = "日净利",
                    ExtraColor = Grey,
                    Sort = 0f,
                    Click = delegate
                    {
                        MapSelection.Message("铁路 " + lineCount + " 条 · 今日净利 " + (netSum >= 0 ? "+" : "") + netSum.ToString("N0"));
                    }
                });
                for (int i = 0; i < lines.Count; i++)
                {
                    var l = lines[i];
                    int inc, up;
                    int net = Railways.DailyNetOf(l, out inc, out up);
                    string lname = Railways.NameOf(l.FromId) + "—" + Railways.NameOf(l.ToId);
                    string summary = Railways.LineSummary(l);
                    string msg = lname + " · " + summary + "\n今日净利 " + (net >= 0 ? "+" : "") + net
                        + " (收入 " + inc + " / 维护 " + up + ")";
                    sec.Rows.Add(new RowData
                    {
                        Icon = "fia_settle_town",
                        Name = lname,
                        Value = (net >= 0 ? "+" : "") + Num(net),
                        ValueColor = net >= 0 ? Green : Red,
                        Extra = "等级" + Railways.TierOf(l),
                        ExtraColor = Grey,
                        Sort = net,
                        Click = delegate { MapSelection.Message(msg); }
                    });
                }
                int capSum = 0, usedSum = 0;
                if (pk != null)
                {
                    try
                    {
                        foreach (var t in pk.Fiefs)
                        {
                            if (t == null || t.Settlement == null) continue;
                            int u, c, cd;
                            Railways.MilStatusOf(t.Settlement.StringId, out u, out c, out cd);
                            capSum += c;
                            usedSum += u;
                        }
                    }
                    catch { }
                }
                int remain = capSum - usedSum;
                if (remain < 0) remain = 0;
                int cCap = capSum, cUsed = usedSum, cRemain = remain;
                int cRemainPct = cCap > 0 ? (int)Math.Round(cRemain * 100f / cCap) : 0;
                sec.Rows.Add(new RowData
                {
                    Icon = MilIcons.IconOf("militia"),
                    Name = "军列今日余力",
                    Value = cCap > 0 ? (cRemainPct + "%") : "—",
                    ValueColor = cCap <= 0 ? Grey : (cRemain > 0 ? Green : Red),
                    Extra = "已用" + cUsed,
                    ExtraColor = Grey,
                    Sort = 0f,
                    Click = delegate
                    {
                        MapSelection.Message("军列运力: 今日已用 " + cUsed + " / " + cCap + " (1 运力 = 100 兵力, 每日重置)");
                    }
                });
            }
            catch { }
            return sec;
        }
    }
}
