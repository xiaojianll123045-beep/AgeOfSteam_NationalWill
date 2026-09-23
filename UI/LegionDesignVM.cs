using System;
using System.Collections.Generic;
using System.Text;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;

namespace FeudalInternalAffairs
{
    // 建军设计器: 单个兵种行(图标/名称/已选·上限/拖动条/±/武器需求)
    public class LegionUnitRowVM : ViewModel
    {
        internal const float HandleW = 28f;   // 滑块尺寸(与 prefab 严格一致: 28x28)

        private readonly int _kind;
        private readonly string _icon, _name;
        private string _selText = "—", _reqText = "—", _barColor = "#7FBF6AFF";
        private string _nameColor = "#E8DCC0FF", _reqColor = "#9A8F78FF", _selColor = "#E8C33AFF";
        private string _rowColor = "#FFFFFF08", _trackColor = "#FFFFFF22";
        private float _fillW, _handleX;
        private bool _hover;

        internal LegionUnitRowVM(int kind)
        {
            _kind = kind;
            _icon = ArmyUnitRowVM.IconOf(kind);
            var u = Equipment.Unit(kind);
            _name = u != null ? u.Name : ("兵种 " + kind);
        }

        internal int Kind { get { return _kind; } }

        [DataSourceProperty] public string Icon { get { return _icon; } }
        [DataSourceProperty] public string Name { get { return _name; } }
        [DataSourceProperty] public string SelText { get { return _selText; } }
        [DataSourceProperty] public string SelColor { get { return _selColor; } }
        [DataSourceProperty] public string ReqText { get { return _reqText; } }
        [DataSourceProperty] public string NameColor { get { return _nameColor; } }
        [DataSourceProperty] public string ReqColor { get { return _reqColor; } }
        [DataSourceProperty] public string BarColor { get { return _barColor; } }
        [DataSourceProperty] public string RowColor { get { return _rowColor; } }
        [DataSourceProperty] public string TrackColor { get { return _trackColor; } }
        [DataSourceProperty] public float FillWidth { get { return _fillW; } }
        [DataSourceProperty] public float HandleX { get { return _handleX; } }

        internal bool IsHover { get { return _hover; } }

        internal void SetLive(int sel, int max, string req, bool locked, float trackW)
        {
            if (locked) _selText = "未解锁";
            else if (max <= 0) _selText = "暂不可招";
            else _selText = MilFmt.N(sel) + " / " + (max >= int.MaxValue - 1 ? "∞" : MilFmt.N(max));
            float ratio = max > 0 ? Math.Min(1f, sel / (float)max) : 0f;
            _fillW = ratio * trackW;
            _handleX = ratio * Math.Max(0f, trackW - HandleW);
            _reqText = req;
            _nameColor = locked ? "#6F6858FF" : "#E8DCC0FF";
            _reqColor = locked ? "#6F6858FF" : "#9A8F78FF";
            _barColor = locked ? "#4A4438FF" : (max <= 0 ? "#5A5448FF" : (sel > 0 ? "#E8C33AFF" : "#7FBF6AFF"));
            _selColor = locked ? "#6F6858FF" : (max <= 0 ? "#8A8070FF" : (sel > 0 ? "#7FBF6AFF" : "#E8C33AFF"));
            OnPropertyChangedWithValue(_selText, "SelText");
            OnPropertyChangedWithValue(_selColor, "SelColor");
            OnPropertyChangedWithValue(_reqText, "ReqText");
            OnPropertyChangedWithValue(_nameColor, "NameColor");
            OnPropertyChangedWithValue(_reqColor, "ReqColor");
            OnPropertyChangedWithValue(_barColor, "BarColor");
            OnPropertyChangedWithValue(_fillW, "FillWidth");
            OnPropertyChangedWithValue(_handleX, "HandleX");
        }

        // 悬停高亮: 整行底色 + 轨道底
        internal void SetHover(bool on)
        {
            if (_hover == on) return;
            _hover = on;
            _rowColor = on ? "#FFFFFF1A" : "#FFFFFF08";
            _trackColor = on ? "#FFFFFF38" : "#FFFFFF22";
            OnPropertyChangedWithValue(_rowColor, "RowColor");
            OnPropertyChangedWithValue(_trackColor, "TrackColor");
        }

        // 每帧通知: 滑块位置随值实时刷新(面板 OnTick 调用)
        internal void TickVisual()
        {
            OnPropertyChangedWithValue(_fillW, "FillWidth");
            OnPropertyChangedWithValue(_handleX, "HandleX");
        }
    }

    // 建军设计器右栏: 装备清单行(仅列有库存型号; 缺装统一用"满足率 %"表达)
    public class LegionGapRowVM : ViewModel
    {
        private readonly string _name, _have, _need, _fill, _fillColor;

        internal LegionGapRowVM(string name, int have, int need)
        {
            _name = ClipName(string.IsNullOrEmpty(name) ? "?" : name, 14);
            _have = MilFmt.N(have);
            _need = MilFmt.N(need);
            int pct = need > 0 ? (int)Math.Round(100.0 * have / need) : 100;
            if (pct > 100) pct = 100;
            if (pct < 0) pct = 0;
            _fill = pct + "%";
            _fillColor = pct >= 90 ? "#7FBF6AFF" : (pct >= 50 ? "#E8C33AFF" : "#D96A5AFF");
        }

        private LegionGapRowVM(string text)
        {
            _name = text;
            _have = ""; _need = ""; _fill = "";
            _fillColor = "#8A8070FF";
        }

        internal static LegionGapRowVM Placeholder(string text) { return new LegionGapRowVM(text); }

        // 型号列宽约 140px(font 20): 超宽截断加省略号(CJK 2 单位, ASCII 1 单位)
        private static string ClipName(string s, int units)
        {
            if (string.IsNullOrEmpty(s)) return s;
            int used = 0, i = 0;
            for (; i < s.Length; i++)
            {
                int w = s[i] > (char)0x2E7F ? 2 : 1;
                if (used + w + 2 > units) break;
                used += w;
            }
            return i >= s.Length ? s : s.Substring(0, i) + "…";
        }

        [DataSourceProperty] public string Name { get { return _name; } }
        [DataSourceProperty] public string Have { get { return _have; } }
        [DataSourceProperty] public string Need { get { return _need; } }
        [DataSourceProperty] public string Fill { get { return _fill; } }
        [DataSourceProperty] public string FillColor { get { return _fillColor; } }
    }

    // 建军设计器页 VM(军务页守备营 [成立军团] 入口)
    //   左栏 14 兵种编制(滚轮换窗口), 右栏实时预览(总人数/逐兵种上限/装备清单满足率/花费/编制上限)
    //   上限规则: 每兵种 = min(军械库该兵种武器实际可用数(Armory.CountKey), 守备营可抽人数, 编制上限, 资金可支撑人数)
    //   资金口径: 军械库领装不扣金(CreateLegionEx 只花 建军费+训练费/人), 故资金上限 = (国库-建军费) ÷ 训练费/人
    //   总人数上限 = 各兵种上限之和, 再受守备营/编制/资金总池按比例夹取; 全 0 或资金成瓶颈时给原因行
    public class LegionDesignVM : PanelVMBase
    {
        internal const float TrackW = 240f;      // 与 FeudalLegionDesign.xml 拖动条同宽
        internal const float RowHeight = 184f;   // 行高(与 xml SuggestedHeight 一致; 图标 88 需 ≥180)
        internal const float FirstRowTop = 232f; // 列表首行顶(与 xml ListPanel MarginTop 一致)
        private const float ListBottomReserve = 104f;   // 底部状态行 + 操作栏预留
        private const int ReqUnitsPerLine = 48;  // 需求摘要每行宽度单位(CJK=2, ASCII=1)

        private int _hoverKind = -1;         // 鼠标悬停/拖动中的行(高亮用)
        private int _scroll;                 // 可见窗口第一行的兵种号

        private readonly Action _onClose;
        private Settlement _home;
        private readonly int[] _sel;
        private readonly int[] _wcap;
        private readonly int[] _cpm;
        private readonly bool[] _locked;
        private readonly string[] _reqText;
        private readonly bool[] _supports = new bool[3];
        private readonly List<LegionUnitRowVM> _allRows = new List<LegionUnitRowVM>();

        private int _garrison, _formationCap, _treasury, _formCost, _moneyUsable, _tier = 1;
        private int _armoryWeapons;
        private string _homeName = "—";
        private string _subText = "", _totalText = "", _capText = "", _costText = "";
        private string _treasuryText = "国库：— 金", _treasuryColor = "#E8C33AFF", _costColor = "#C8B98FCC";
        private string _previewBody = "", _previewColor = "#D8C9A0FF";
        private string _status = "滚轮滚动 · 拖动条/± 调人数 · 上限 = min(军械库可用, 守备营, 编制, 资金)";
        private string _statusColor = "#E8DCC0FF";
        private string _reasonText = "", _reasonColor = "#E8C33AFF";
        private bool _hasReason;
        private string _confirmText = "确认建军", _confirmColor = "#C96A5AFF";
        private string _sup0Color = "#8A8070FF", _sup1Color = "#8A8070FF", _sup2Color = "#8A8070FF";

        public LegionDesignVM(Action onClose, Settlement home)
        {
            _onClose = onClose;
            _home = home;
            int n = Equipment.Units.Count;
            _sel = new int[n];
            _wcap = new int[n];
            _cpm = new int[n];
            _locked = new bool[n];
            _reqText = new string[n];
            Rows = new MBBindingList<LegionUnitRowVM>();
            GapRows = new MBBindingList<LegionGapRowVM>();
            for (int i = 0; i < n; i++) _allRows.Add(new LegionUnitRowVM(i));
            Refresh();
        }

        public MBBindingList<LegionUnitRowVM> Rows { get; private set; }
        public MBBindingList<LegionGapRowVM> GapRows { get; private set; }

        [DataSourceProperty] public string SubText { get { return _subText; } }
        [DataSourceProperty] public string TotalText { get { return _totalText; } }
        [DataSourceProperty] public string CapText { get { return _capText; } }
        [DataSourceProperty] public string CostText { get { return _costText; } }
        [DataSourceProperty] public string TreasuryText { get { return _treasuryText; } }
        [DataSourceProperty] public string TreasuryColor { get { return _treasuryColor; } }
        [DataSourceProperty] public string CostColor { get { return _costColor; } }
        [DataSourceProperty] public string PreviewBody { get { return _previewBody; } }
        [DataSourceProperty] public string PreviewColor { get { return _previewColor; } }
        [DataSourceProperty] public string StatusText { get { return _status; } }
        [DataSourceProperty] public string StatusColor { get { return _statusColor; } }
        [DataSourceProperty] public string ReasonText { get { return _reasonText; } }
        [DataSourceProperty] public string ReasonColor { get { return _reasonColor; } }
        [DataSourceProperty] public bool HasReason { get { return _hasReason; } }
        [DataSourceProperty] public string ConfirmText { get { return _confirmText; } }
        [DataSourceProperty] public string ConfirmColor { get { return _confirmColor; } }
        [DataSourceProperty] public string Sup0Color { get { return _sup0Color; } }
        [DataSourceProperty] public string Sup1Color { get { return _sup1Color; } }
        [DataSourceProperty] public string Sup2Color { get { return _sup2Color; } }

        public void ExecuteClose() { if (_onClose != null) _onClose(); }

        internal Settlement Home { get { return _home; } }
        internal int SelectedTotal { get { return Sum(); } }
        internal int UnitCount { get { return _allRows.Count; } }

        // 可见行号 -> 兵种号(滚动窗口映射; 越界返回 -1)
        internal int KindAt(int visibleIndex)
        {
            try
            {
                if (visibleIndex < 0 || visibleIndex >= Rows.Count) return -1;
                return Rows[visibleIndex].Kind;
            }
            catch { return -1; }
        }

        // 一屏可见行数: 列表从顶部一直排到底部预留线
        private int VisibleRowCount()
        {
            try
            {
                float h = 0f;
                try { h = TaleWorlds.Engine.Screen.RealScreenResolutionHeight; } catch { }
                if (h <= 100f) h = 1080f;
                int n = (int)((h - FirstRowTop - ListBottomReserve) / RowHeight);
                if (n < 1) n = 1;
                if (n > _allRows.Count) n = _allRows.Count;
                return n;
            }
            catch { return 6; }
        }

        // 滚轮: 上下滚动可见窗口(与其它页 ScrollStep 同口径)
        internal void ScrollStep(int dir)
        {
            try
            {
                if (dir == 0) return;
                int max = Math.Max(0, _allRows.Count - VisibleRowCount());
                int next = _scroll + dir;
                if (next < 0) next = 0;
                if (next > max) next = max;
                if (next == _scroll) return;
                _scroll = next;
                _hoverKind = -1;
                for (int i = 0; i < _allRows.Count; i++) _allRows[i].SetHover(false);
                SyncRows();
            }
            catch { }
        }

        internal void Retarget(Settlement home)
        {
            try
            {
                if (home == null || _home == home) return;
                _home = home;
                for (int i = 0; i < _sel.Length; i++) _sel[i] = 0;
                _status = "已切换驻地: " + (home.Name != null ? home.Name.ToString() : home.StringId);
                _statusColor = "#E8DCC0FF";
                Refresh();
                OnPropertyChangedWithValue(_status, "StatusText");
                OnPropertyChangedWithValue(_statusColor, "StatusColor");
            }
            catch { }
        }

        // ==================== 上限计算 ====================
        private static string NationalKey()
        {
            try { return Armory.NationalOwner(DefArmy.OurKingdom); } catch { return ""; }
        }

        private static bool IsWeaponTag(EquipTag t)
        {
            return t == EquipTag.AnyWeapon || t == EquipTag.Firearm || t == EquipTag.Rifle || t == EquipTag.Bow
                || t == EquipTag.Crossbow || t == EquipTag.BowOrCrossbow || t == EquipTag.Carbine
                || t == EquipTag.Saber || t == EquipTag.Lance || t == EquipTag.Gun
                || t == EquipTag.HeavyGun || t == EquipTag.LightGun || t == EquipTag.MachineGun;
        }

        private static string TagName(EquipTag t)
        {
            if (t == EquipTag.AnyWeapon) return "任意装备";
            if (t == EquipTag.BowOrCrossbow) return "弓/弩";
            if (t == EquipTag.Firearm) return "火器";
            if (t == EquipTag.Rifle) return "来复枪";
            if (t == EquipTag.Bow) return "弓";
            if (t == EquipTag.Crossbow) return "弩";
            if (t == EquipTag.Carbine) return "卡宾枪";
            if (t == EquipTag.Saber) return "马刀";
            if (t == EquipTag.Lance) return "骑枪";
            if (t == EquipTag.Gun) return "火炮";
            if (t == EquipTag.HeavyGun) return "重炮";
            if (t == EquipTag.LightGun) return "轻炮";
            if (t == EquipTag.MachineGun) return "机枪";
            if (t == EquipTag.AnyHorse) return "马匹";
            if (t == EquipTag.HeavyHorse) return "重装战马";
            if (t == EquipTag.LightArmor) return "轻甲";
            if (t == EquipTag.HeavyArmor) return "重甲";
            if (t == EquipTag.Helmet) return "钢盔";
            if (t == EquipTag.Grenade) return "手榴弹";
            if (t == EquipTag.Carriage) return "炮组";
            if (t == EquipTag.Tool) return "工具";
            if (t == EquipTag.SupportGear) return "支援器材";
            return "装备";
        }

        // v4.236: 统一走内核 Equipment.BranchOf(按主武器定: 火器步兵算远程, 龙骑兵算骑射)
        private static int BranchOf(int kind)
        {
            return Equipment.BranchOf(kind);
        }

        private static string UnitName(int kind)
        {
            try
            {
                var u = Equipment.Unit(kind);
                return u != null ? u.Name : ("兵种" + kind);
            }
            catch { return "兵种" + kind; }
        }

        // 本国"火器/火炮"能到的最高装备层级 = 建军用的装备档(equipTier)
        //   v4.241: 只认火器/火炮(以前把冷兵器/甲胄也算进来, 于是开局就是 T4 档); 没有火器时退回弓弩档 2
        private static int EraTierOf(Kingdom k)
        {
            try
            {
                for (int t = 6; t >= 3; t--)
                    for (int i = 0; i < Equipment.All.Count; i++)
                    {
                        var e = Equipment.All[i];
                        if (e == null || e.Tier != t) continue;
                        if (!e.IsFirearm && e.Cat != EquipCat.Artillery) continue;
                        if (Research.UnlockedFor(k, e.TechId)) return t;
                    }
            }
            catch { }
            return 2;
        }

        // 军械库里该标签实际有库存的型号: 只认已解锁(与内核 BestStockFor 同口径), 档内优先、档外作后备
        private EquipDef StockPick(string key, EquipTag tag)
        {
            try
            {
                if (string.IsNullOrEmpty(key)) return null;
                EquipDef best = null, fallback = null;
                var entries = Armory.EntriesOf(key);
                for (int i = 0; i < entries.Count; i++)
                {
                    var kv = entries[i];
                    if (kv.Value <= 0) continue;
                    var def = Equipment.Get(kv.Key);
                    if (def == null || !Equipment.MatchesTag(def, tag)) continue;
                    bool unlocked = false;
                    try { unlocked = Equipment.IsUnlocked(def); } catch { }
                    if (!unlocked) continue;                       // v4.241: 未解锁型号不计入可领用
                    if (def.Tier <= _tier)
                    {
                        if (best == null || def.Tier > best.Tier || (def.Tier == best.Tier && def.Price > best.Price)) best = def;
                    }
                    else if (fallback == null || def.Tier > fallback.Tier || (def.Tier == fallback.Tier && def.Price > fallback.Price))
                    {
                        fallback = def;
                    }
                }
                return best != null ? best : fallback;
            }
            catch { return null; }
        }

        // 逐兵种上限(武器口径/资金) + 需求文案 + 领装估价
        private void ComputeUnits(Kingdom kingdom)
        {
            string key = NationalKey();
            for (int k = 0; k < _sel.Length; k++)
            {
                var u = Equipment.Unit(k);
                if (u == null) { _wcap[k] = 0; _cpm[k] = 0; _locked[k] = true; _reqText[k] = "—"; continue; }
                string gate = "";
                try { gate = LordArmy.UnitGate(u.Id); } catch { }
                _locked[k] = !Research.UnlockedFor(kingdom, gate);
                if (_locked[k])
                {
                    _wcap[k] = 0; _cpm[k] = 0;
                    _reqText[k] = "未解锁: 需 " + LordArmy.TechName(gate) + "(科技页研究)";
                    continue;
                }
                long wcap = int.MaxValue;
                double cost = 0;
                float fill = 1f;
                var req = new StringBuilder();
                var models = new StringBuilder();
                if (u.Req != null)
                {
                    for (int r = 0; r < u.Req.Length; r++)
                    {
                        var one = u.Req[r];
                        var model = StockPick(key, one.Tag);
                        int have = model != null ? Armory.CountKey(key, model.Id) : 0;
                        if (IsWeaponTag(one.Tag))
                        {
                            long c = one.Per100 > 0 ? have * 100L / one.Per100 : long.MaxValue;
                            if (c < wcap) wcap = c;
                        }
                        if (one.Per100 > 0)
                        {
                            float rate = have >= one.Per100 ? 1f : have / (float)one.Per100;
                            if (rate < fill) fill = rate;
                        }
                        if (model == null) model = Equipment.PickForTag(one.Tag, _tier);
                        if (model != null) cost += model.Price * (one.Per100 / 100.0);
                        if (req.Length > 0) req.Append(' ');
                        req.Append(TagName(one.Tag)).Append('×').Append(one.Per100);
                        if (model != null)
                        {
                            if (models.Length > 0) models.Append('/');
                            models.Append(model.Name);
                        }
                    }
                }
                _wcap[k] = wcap >= int.MaxValue - 1 ? int.MaxValue : (int)wcap;
                _cpm[k] = (int)Math.Min(int.MaxValue - 1, Math.Ceiling(cost));
                _reqText[k] = BuildReqText(req, models, fill);
            }
        }

        // 行内需求摘要: 每百人需求 + 现用型号 + 满足率; 主动断行最多 3 行
        private static string BuildReqText(StringBuilder req, StringBuilder models, float fill)
        {
            var sb = new StringBuilder();
            if (req.Length > 0) sb.Append("每百人 ").Append(req.ToString());
            else sb.Append("无装备需求");
            if (models.Length > 0) sb.Append(" · 现用 ").Append(models.ToString());
            sb.Append(" · 满足率 ").Append((int)Math.Round(fill * 100f)).Append('%');
            return Wrap3(sb.ToString(), ReqUnitsPerLine, 3);
        }

        // 粗估宽度单位: CJK 2, ASCII 1
        private static int UnitsOf(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            int n = 0;
            for (int i = 0; i < s.Length; i++) n += s[i] > (char)0x2E7F ? 2 : 1;
            return n;
        }

        // 主动断行(按空格分词, 超长词硬断): 每行 unitsPerLine 单位, 最多 maxLines 行, 溢出补 …
        private static string Wrap3(string s, int unitsPerLine, int maxLines)
        {
            if (string.IsNullOrEmpty(s) || unitsPerLine <= 0 || maxLines <= 0) return s;
            try
            {
                var lines = new List<string>();
                var cur = new StringBuilder();
                int curW = 0;
                var parts = s.Split(' ');
                for (int i = 0; i < parts.Length; i++)
                {
                    string token = parts[i];
                    if (token.Length == 0) continue;
                    int w = UnitsOf(token);
                    if (curW > 0 && curW + 1 + w > unitsPerLine)
                    {
                        if (lines.Count >= maxLines) break;
                        lines.Add(cur.ToString());
                        cur.Length = 0; curW = 0;
                    }
                    if (w > unitsPerLine)
                    {
                        int pos = 0;
                        while (pos < token.Length)
                        {
                            int avail = unitsPerLine - curW - (cur.Length > 0 ? 1 : 0);
                            int take = 0, tw = 0;
                            while (pos + take < token.Length)
                            {
                                int cw = token[pos + take] > (char)0x2E7F ? 2 : 1;
                                if (tw + cw > avail) break;
                                tw += cw; take++;
                            }
                            if (take <= 0)
                            {
                                if (lines.Count >= maxLines) break;
                                lines.Add(cur.ToString());
                                cur.Length = 0; curW = 0;
                                continue;
                            }
                            if (cur.Length > 0) { cur.Append(' '); curW += 1; }
                            cur.Append(token, pos, take);
                            curW += tw;
                            pos += take;
                        }
                    }
                    else
                    {
                        if (curW > 0) { cur.Append(' '); curW += 1; }
                        cur.Append(token);
                        curW += w;
                    }
                }
                if (cur.Length > 0 && lines.Count < maxLines) lines.Add(cur.ToString());
                if (lines.Count == 0) return s;
                // 还有剩余内容被截断: 最后一行补 …
                bool truncated = false;
                if (lines.Count >= maxLines)
                {
                    int used = 0;
                    for (int i = 0; i < lines.Count; i++) used += UnitsOf(lines[i]) + 2;
                    if (used < UnitsOf(s)) truncated = true;
                }
                if (truncated)
                {
                    string last = lines[lines.Count - 1];
                    while (last.Length > 0 && UnitsOf(last) + 2 > unitsPerLine) last = last.Substring(0, last.Length - 1);
                    lines[lines.Count - 1] = last + "…";
                }
                return string.Join("\n", lines.ToArray());
            }
            catch { return s; }
        }

        internal int MaxOf(int kind)
        {
            try
            {
                if (kind < 0 || kind >= _sel.Length || _home == null) return 0;
                if (_locked[kind]) return 0;
                if (_moneyUsable <= 0) return 0;
                long cap = int.MaxValue;
                if (_wcap[kind] >= 0 && _wcap[kind] < cap) cap = _wcap[kind];
                cap = Math.Min(cap, (long)MoneyMenCap());   // v27.x: 领装不扣金, 资金只按训练费/人折算
                cap = Math.Min(cap, (long)_garrison);
                cap = Math.Min(cap, (long)_formationCap);
                cap = Math.Min(cap, (long)DefArmy.MaxLegionMen);
                if (cap < 0) cap = 0;
                if (cap > int.MaxValue - 1) cap = int.MaxValue - 1;
                return (int)cap;
            }
            catch { return 0; }
        }

        // 资金可支撑人数 = (国库 - 建军费) ÷ 训练费/人; 军械库领装不计价(装备费用旧口径已废)
        private int MoneyMenCap()
        {
            try
            {
                if (_moneyUsable <= 0) return 0;
                if (DefArmy.LegionTrainCostPerMan <= 0) return int.MaxValue - 1;
                long n = _moneyUsable / DefArmy.LegionTrainCostPerMan;
                return n > int.MaxValue - 1 ? int.MaxValue - 1 : (int)n;
            }
            catch { return 0; }
        }

        private int TotalPool()
        {
            try
            {
                // 总人数上限 = 各兵种上限之和(上限已含逐兵种武器口径), 再受守备营/编制/资金总池约束
                long sumCaps = 0;
                for (int k = 0; k < _sel.Length; k++) sumCaps += MaxOf(k);
                long pool = Math.Min(sumCaps, Math.Min((long)_garrison, (long)_formationCap));
                pool = Math.Min(pool, DefArmy.MaxLegionMen);
                pool = Math.Min(pool, MoneyMenCap());
                if (pool < 0) pool = 0;
                return (int)Math.Min(pool, int.MaxValue - 1);
            }
            catch { return 0; }
        }

        private int Sum()
        {
            int n = 0;
            for (int i = 0; i < _sel.Length; i++) n += _sel[i];
            return n;
        }

        private long EqCost()
        {
            long c = 0;
            for (int i = 0; i < _sel.Length; i++) if (_sel[i] > 0) c += (long)_sel[i] * _cpm[i];
            return c;
        }

        // 超限自动夹取: 逐兵种上限 -> 总池(守备营/编制/资金)按比例
        // (旧口径把军械库装备全价当钱扣 -> 上限只剩个位数; 现装备领用不扣金, 资金总池已含在 TotalPool 内)
        internal void ClampSelections(bool announce)
        {
            try
            {
                bool hit = false;
                for (int k = 0; k < _sel.Length; k++)
                {
                    int cap = MaxOf(k);
                    if (_sel[k] > cap) { _sel[k] = cap; hit = true; }
                    if (_sel[k] < 0) { _sel[k] = 0; hit = true; }
                }
                int pool = TotalPool();
                int sum = Sum();
                if (sum > pool && sum > 0)
                {
                    for (int k = 0; k < _sel.Length; k++)
                        if (_sel[k] > 0) _sel[k] = (int)Math.Floor(_sel[k] * (double)pool / sum);
                    hit = true;
                }
                if (hit && announce)
                {
                    _status = "已超限, 自动夹到上限(军械库装备/守备营/编制/资金)";
                    _statusColor = "#E8C33AFF";
                    OnPropertyChangedWithValue(_status, "StatusText");
                    OnPropertyChangedWithValue(_statusColor, "StatusColor");
                }
            }
            catch { }
        }

        // ==================== 刷新 ====================
        internal void Refresh()
        {
            try
            {
                var kingdom = DefArmy.OurKingdom;
                _homeName = _home != null && _home.Name != null ? _home.Name.ToString() : "—";
                _garrison = 0;
                try { if (_home != null) _garrison = DefArmy.GarrisonMenOf(_home.StringId); } catch { }
                _treasury = 0;
                try { _treasury = MilTreasury.Gold(); } catch { }
                _formCost = DefArmy.LegionFormCost;
                _moneyUsable = Math.Max(0, _treasury - _formCost);
                _formationCap = 600;
                try { _formationCap = Math.Max(DefArmy.LegionMinMen, ArmyDoctrine.CommandLimitOf(null) + 200); } catch { }
                if (_formationCap > DefArmy.MaxLegionMen) _formationCap = DefArmy.MaxLegionMen;
                _tier = EraTierOf(kingdom);
                _armoryWeapons = CountArmoryWeapons(NationalKey());

                ComputeUnits(kingdom);
                ClampSelections(false);
                SyncRows();
                UpdateTexts();
                BuildGapRows(kingdom);
            }
            catch (Exception ex) { DLog.Force("建军设计器刷新失败: " + ex.Message); }
        }

        // 国家军械库实际可用武器数(CountKey 口径): 上限/原因行/预览共用
        private static int CountArmoryWeapons(string key)
        {
            int n = 0;
            try
            {
                if (string.IsNullOrEmpty(key)) return 0;
                var entries = Armory.EntriesOf(key);
                for (int i = 0; i < entries.Count; i++)
                {
                    var def = Equipment.Get(entries[i].Key);
                    if (def == null || !IsWeaponDef(def)) continue;
                    n += Armory.CountKey(key, entries[i].Key);
                }
            }
            catch { }
            return n;
        }

        private static bool IsWeaponDef(EquipDef def)
        {
            try
            {
                return Equipment.MatchesTag(def, EquipTag.AnyWeapon)
                    || Equipment.MatchesTag(def, EquipTag.Gun) || Equipment.MatchesTag(def, EquipTag.HeavyGun)
                    || Equipment.MatchesTag(def, EquipTag.LightGun) || Equipment.MatchesTag(def, EquipTag.MachineGun);
            }
            catch { return false; }
        }

        // 重建可见窗口(列表从顶部排到底部; 滚轮换窗口)
        private void SyncRows()
        {
            try
            {
                int visible = VisibleRowCount();
                int max = Math.Max(0, _allRows.Count - visible);
                if (_scroll > max) _scroll = max;
                if (_scroll < 0) _scroll = 0;
                Rows.Clear();
                for (int i = _scroll; i < _allRows.Count && Rows.Count < visible; i++) Rows.Add(_allRows[i]);
                for (int k = 0; k < _allRows.Count; k++)
                    _allRows[k].SetLive(_sel[k], MaxOf(k), _reqText[k], _locked[k], TrackW);
                OnPropertyChangedWithValue(Rows.Count, "Rows");
            }
            catch { }
        }

        // 悬停/拖动行高亮(面板每帧调用; 参数 = 兵种号, -1 = 无)
        internal void SetHoverRow(int kind)
        {
            try
            {
                if (kind >= _allRows.Count) kind = _allRows.Count - 1;
                if (kind < -1) kind = -1;
                if (_hoverKind == kind) return;
                _hoverKind = kind;
                for (int i = 0; i < Rows.Count; i++) Rows[i].SetHover(Rows[i].Kind == kind);
            }
            catch { }
        }

        // 每帧通知所有行的 FillWidth/HandleX(滑块实时跟随)
        internal void TickVisuals()
        {
            try { for (int i = 0; i < Rows.Count; i++) Rows[i].TickVisual(); }
            catch { }
        }

        private void UpdateTexts()
        {
            try
            {
                int total = Sum();
                _formCost = DefArmy.LegionFormCost + DefArmy.LegionTrainCostPerMan * total;
                int pool = TotalPool();
                int moneyCap = MoneyMenCap();
                long equipValue = EqCost();   // 仅展示: 军械库领装估价, 不从国库扣
                int wage = (int)Math.Ceiling(total * (double)DefArmy.WagePerManPerDay);

                _subText = "「" + _homeName + "」守备营可抽 " + MilFmt.N(_garrison) + " 兵 · 建军费 "
                    + MilFmt.N(_formCost) + " 金";
                _totalText = "总人数 " + MilFmt.N(total) + " / 全项上限 " + MilFmt.N(pool) + (total > pool ? "  [超限]" : "");
                _capText = "编制上限 " + MilFmt.N(_formationCap) + " · 军饷 " + MilFmt.N(wage)
                    + " 金/日 · 资金可招 " + (moneyCap >= int.MaxValue - 1 ? "∞" : MilFmt.N(moneyCap)) + " 兵 · 国库可撑 "
                    + MilFmt.N(wage > 0 ? (_treasury / Math.Max(1, wage)) : 0) + " 天";
                _costText = "花费: 建军 " + MilFmt.N(_formCost) + " 金(" + MilFmt.N(DefArmy.LegionFormCost)
                    + " + " + MilFmt.N(DefArmy.LegionTrainCostPerMan) + "/兵; 军械库领装不扣金) · 领装估价 "
                    + MilFmt.N(equipValue) + " 金";
                _treasuryText = "国库：" + MilFmt.N(_treasury) + " 金";
                _treasuryColor = _treasury >= _formCost ? "#7FBF6AFF" : "#D96A5AFF";
                _costColor = _formCost <= _treasury ? "#C8B98FCC" : "#D96A5AFF";

                var sb = new StringBuilder();
                sb.Append("总人数 ").Append(MilFmt.N(total)).Append(" · 上限 ").Append(MilFmt.N(pool)).Append('\n');
                sb.Append("军械库 ").Append(MilFmt.N(Armory.TotalCount(NationalKey()))).Append(" 件(可用装备 ")
                  .Append(MilFmt.N(_armoryWeapons)).Append(")\n");
                sb.Append("逐兵种上限(军械库实际可用口径):\n");
                int shown = 0, hidden = 0;
                for (int k = 0; k < _sel.Length; k++)
                {
                    int cap = MaxOf(k);
                    bool show = _sel[k] > 0 || (cap > 0 && !_locked[k] && _wcap[k] > 0);
                    if (!show || shown >= 6) { hidden++; continue; }
                    sb.Append("· ").Append(UnitName(k)).Append(' ');
                    sb.Append(_locked[k] ? "未解锁" : (cap >= int.MaxValue - 1 ? "∞" : MilFmt.N(cap)));
                    if (_sel[k] > 0) sb.Append("(选 ").Append(MilFmt.N(_sel[k])).Append(')');
                    sb.Append('\n');
                    shown++;
                }
                if (hidden > 0) sb.Append("· …其余 ").Append(hidden).Append(" 项无装备/未解锁\n");
                sb.Append("支援: ");
                bool anySup = false;
                if (_supports[0]) { sb.Append("工具 "); anySup = true; }
                if (_supports[1]) { sb.Append("医院 "); anySup = true; }
                if (_supports[2]) { sb.Append("电报 "); anySup = true; }
                if (!anySup) sb.Append("无");
                _previewBody = sb.ToString();
                _previewColor = total >= DefArmy.LegionMinMen ? "#7FBF6AFF" : "#D8C9A0FF";
                _confirmText = total > 0
                    ? ("成立军团 " + MilFmt.N(total) + " 人（建军 " + MilFmt.N(_formCost) + "）")
                    : "成立军团";
                _confirmColor = total >= DefArmy.LegionMinMen
                    ? (_formCost <= _treasury ? "#7FBF6AFF" : "#C96A5AFF")
                    : "#C96A5AFF";
                _sup0Color = _supports[0] ? "#7FBF6AFF" : "#8A8070FF";
                _sup1Color = _supports[1] ? "#7FBF6AFF" : "#8A8070FF";
                _sup2Color = _supports[2] ? "#7FBF6AFF" : "#8A8070FF";
                UpdateReason();

                OnPropertyChangedWithValue(_subText, "SubText");
                OnPropertyChangedWithValue(_totalText, "TotalText");
                OnPropertyChangedWithValue(_capText, "CapText");
                OnPropertyChangedWithValue(_costText, "CostText");
                OnPropertyChangedWithValue(_treasuryText, "TreasuryText");
                OnPropertyChangedWithValue(_treasuryColor, "TreasuryColor");
                OnPropertyChangedWithValue(_costColor, "CostColor");
                OnPropertyChangedWithValue(_previewBody, "PreviewBody");
                OnPropertyChangedWithValue(_previewColor, "PreviewColor");
                OnPropertyChangedWithValue(_reasonText, "ReasonText");
                OnPropertyChangedWithValue(_reasonColor, "ReasonColor");
                OnPropertyChangedWithValue(_hasReason, "HasReason");
                OnPropertyChangedWithValue(_confirmText, "ConfirmText");
                OnPropertyChangedWithValue(_confirmColor, "ConfirmColor");
                OnPropertyChangedWithValue(_sup0Color, "Sup0Color");
                OnPropertyChangedWithValue(_sup1Color, "Sup1Color");
                OnPropertyChangedWithValue(_sup2Color, "Sup2Color");
            }
            catch { }
        }

        // 不含资金口径的总池(用于判断资金是否成为瓶颈, 给"国库不足"原因行而不是悄悄缩上限)
        private int NoMoneyPool()
        {
            try
            {
                long sum = 0;
                for (int k = 0; k < _sel.Length; k++)
                {
                    if (_locked[k]) continue;
                    long c = _wcap[k] >= 0 ? _wcap[k] : int.MaxValue;
                    c = Math.Min(c, (long)_garrison);
                    c = Math.Min(c, (long)_formationCap);
                    c = Math.Min(c, (long)DefArmy.MaxLegionMen);
                    if (c > 0) sum += c;
                }
                long pool = Math.Min(sum, Math.Min((long)_garrison, (long)_formationCap));
                pool = Math.Min(pool, DefArmy.MaxLegionMen);
                if (pool < 0) pool = 0;
                return (int)Math.Min(pool, int.MaxValue - 1);
            }
            catch { return 0; }
        }

        // 全项上限为 0 或资金成瓶颈时给出明确原因行(不再只显示一堆 0 / 悄悄缩到个位数)
        private void UpdateReason()
        {
            try
            {
                bool allZero = true;
                for (int k = 0; k < _sel.Length; k++)
                    if (MaxOf(k) > 0) { allZero = false; break; }
                if (!allZero)
                {
                    int pool = TotalPool();
                    int noMoney = NoMoneyPool();
                    if (_moneyUsable > 0 && pool > 0 && pool < noMoney)
                    {
                        _hasReason = true;
                        _reasonText = Wrap3("国库不足: 建军费 " + MilFmt.N(DefArmy.LegionFormCost) + " + 训练费 "
                            + MilFmt.N(DefArmy.LegionTrainCostPerMan) + "/兵 只够 " + MilFmt.N(pool)
                            + " 兵(国库 " + MilFmt.N(_treasury) + " 金; 军械库领装不扣金)", 48, 1);
                        _reasonColor = "#E8C33AFF";
                        return;
                    }
                    _hasReason = false; _reasonText = "";
                    return;
                }
                var sb = new StringBuilder();
                string key = NationalKey();
                if (_home == null) sb.Append("未指定驻地");
                if (string.IsNullOrEmpty(key))
                {
                    if (sb.Length > 0) sb.Append(" · ");
                    sb.Append("尚未建立国家军械库(先建国/定都)");
                }
                else if (_armoryWeapons <= 0)
                {
                    if (sb.Length > 0) sb.Append(" · ");
                    sb.Append("军械库无可用装备");
                }
                if (_garrison <= 0)
                {
                    if (sb.Length > 0) sb.Append(" · ");
                    sb.Append("守备营无可抽兵力");
                }
                if (_moneyUsable <= 0 && _formCost > 0)
                {
                    if (sb.Length > 0) sb.Append(" · ");
                    sb.Append("国库不足以支付建军费 ").Append(MilFmt.N(_formCost));
                }
                if (sb.Length == 0) sb.Append("暂无可招募兵种(检查兵种科技与军械库库存)");
                _hasReason = true;
                _reasonText = Wrap3(sb.ToString(), 48, 1);   // 限宽单行, 超出补 …(不与右栏叠字)
                _reasonColor = (_garrison <= 0 || (string.IsNullOrEmpty(key) || _armoryWeapons <= 0)) ? "#D96A5AFF" : "#E8C33AFF";
            }
            catch { _hasReason = false; _reasonText = ""; }
        }

        private void BuildGapRows(Kingdom kingdom)
        {
            try
            {
                GapRows.Clear();
                string key = NationalKey();
                var need = new Dictionary<string, int>(StringComparer.Ordinal);
                var have = new Dictionary<string, int>(StringComparer.Ordinal);
                for (int k = 0; k < _sel.Length; k++)
                {
                    if (_sel[k] <= 0) continue;
                    var u = Equipment.Unit(k);
                    if (u == null || u.Req == null) continue;
                    for (int r = 0; r < u.Req.Length; r++)
                    {
                        var one = u.Req[r];
                        var model = StockPick(key, one.Tag);
                        if (model == null) model = Equipment.PickForTag(one.Tag, _tier);
                        if (model == null) continue;
                        int q = (int)Math.Ceiling(one.Per100 * (double)_sel[k] / 100.0);
                        if (q <= 0) continue;
                        int old;
                        need.TryGetValue(model.Id, out old);
                        need[model.Id] = old + q;
                    }
                }
                for (int s = 0; s < 3 && s < _supports.Length; s++)
                {
                    if (!_supports[s]) continue;
                    string id = Equipment.SupportIds[s];
                    if (string.IsNullOrEmpty(id)) continue;
                    int old;
                    need.TryGetValue(id, out old);
                    need[id] = old + 1;
                }
                foreach (var kv in need) have[kv.Key] = Armory.CountKey(key, kv.Key);

                var ids = new List<string>(need.Keys);
                ids.Sort(delegate (string a, string b)
                {
                    int ga = Val(need, a) - Val(have, a);
                    int gb = Val(need, b) - Val(have, b);
                    if (ga != gb) return gb.CompareTo(ga);
                    return string.CompareOrdinal(a, b);
                });
                int shown = 0;
                for (int i = 0; i < ids.Count && shown < 10; i++)
                {
                    string id = ids[i];
                    int haveN = Armory.CountKey(key, id);
                    if (haveN <= 0) continue;                 // 库存/可用为 0 的型号不显示
                    int needN = Val(need, id);
                    if (needN <= 0) continue;
                    var def = Equipment.Get(id);
                    GapRows.Add(new LegionGapRowVM(def != null ? def.Name : id, haveN, needN));
                    shown++;
                }
                if (shown == 0)
                    GapRows.Add(LegionGapRowVM.Placeholder(Sum() > 0 ? "所选装备均无库存" : "未选择兵种 / 无需求"));
                OnPropertyChangedWithValue(GapRows.Count, "GapRows");
            }
            catch (Exception ex) { DLog.Force("建军设计器: 装备满足率刷新失败 " + ex.Message); }
        }

        private static int Val(Dictionary<string, int> d, string id)
        {
            int v;
            return (d != null && id != null && d.TryGetValue(id, out v)) ? v : 0;
        }

        // ==================== 操作 ====================
        internal int ValueOf(int kind)
        {
            return kind >= 0 && kind < _sel.Length ? _sel[kind] : 0;
        }

        internal void StepRow(int kind, int delta)
        {
            try { SetValue(kind, ValueOf(kind) + delta); }
            catch { }
        }

        internal void SetRowFromPx(int kind, float mouseX, float trackX, float trackW)
        {
            try
            {
                int max = MaxOf(kind);
                if (max <= 0) return;
                float rel = (mouseX - trackX) / Math.Max(1f, trackW);
                if (rel < 0f) rel = 0f;
                if (rel > 1f) rel = 1f;
                SetValue(kind, (int)Math.Round(rel * max));
            }
            catch { }
        }

        private void SetValue(int kind, int v)
        {
            try
            {
                if (kind < 0 || kind >= _sel.Length) return;
                int max = MaxOf(kind);
                if (v < 0) v = 0;
                if (v > max) v = max;
                if (_sel[kind] == v) return;   // 拖动中值未变: 不重复刷新预览
                _sel[kind] = v;
                ClampSelections(true);
                SyncRows();
                UpdateTexts();
                BuildGapRows(DefArmy.OurKingdom);
            }
            catch { }
        }

        internal void ToggleSupport(int i)
        {
            try
            {
                if (i < 0 || i > 2) return;
                _supports[i] = !_supports[i];
                Refresh();
            }
            catch { }
        }

        internal void ExecuteConfirm()
        {
            try
            {
                Refresh();   // 用最新库存/国库校验
                if (_home == null) { Fail("未指定驻地"); return; }
                int total = Sum();
                _formCost = DefArmy.LegionFormCost + DefArmy.LegionTrainCostPerMan * total;
                if (total < DefArmy.LegionMinMen)
                { Fail("成立军团至少需要 " + MilFmt.N(DefArmy.LegionMinMen) + " 兵(当前 " + MilFmt.N(total) + ")"); return; }
                if (total > _garrison) { Fail("守备营兵力不足: 只有 " + MilFmt.N(_garrison) + " 兵(计划 " + MilFmt.N(total) + ")"); return; }
                if (_treasury < _formCost)
                { Fail("国库不足: 建军需要 " + MilFmt.N(_formCost) + " 第纳尔(现有 " + MilFmt.N(_treasury) + ")"); return; }
                for (int k = 0; k < _sel.Length; k++)
                {
                    if (_sel[k] <= 0) continue;
                    if (_locked[k]) { Fail("「" + UnitName(k) + "」科技未解锁"); return; }
                    if (_wcap[k] < int.MaxValue && _sel[k] > _wcap[k])
                    { Fail("「" + UnitName(k) + "」超出军械库装备上限 " + MilFmt.N(_wcap[k])); return; }
                }
                if (DefArmy.OurKingdom == null) { Fail("尚未建立国家意志"); return; }
                var comp = new int[4];
                for (int k = 0; k < _sel.Length; k++) if (_sel[k] > 0) comp[BranchOf(k)] += _sel[k];
                // v4.236: 把 14 兵种逐项选择一并交给内核(花名册与逐人表都按设计编成)
                string err = DefArmy.CreateLegionEx(_home, total, comp, _tier, _supports, _sel);
                if (!string.IsNullOrEmpty(err)) { Fail(err); return; }
                string msg = "已成立军团: " + MilFmt.N(total) + " 兵 · 建军费 " + MilFmt.N(_formCost) + " 第纳尔";
                DLog.Force("建军设计器: " + msg);
                try { MapSelection.Message(msg); } catch { }
                CloseAndReturn();
            }
            catch (Exception ex) { DLog.Force("建军设计器确认异常: " + ex.Message); Fail("建军异常, 见日志"); }
        }

        private void Fail(string msg)
        {
            try
            {
                _status = msg;
                _statusColor = "#C96A5AFF";
                OnPropertyChangedWithValue(_status, "StatusText");
                OnPropertyChangedWithValue(_statusColor, "StatusColor");
                ArmyPanelVM.ShowInquiry("建军设计器", msg);
            }
            catch { }
        }

        // 成功: 关设计器 -> 回军务页守备营页签
        private void CloseAndReturn()
        {
            try
            {
                if (_onClose != null) _onClose();
                ArmyPanel.OpenAtGarrison();
            }
            catch { }
        }
    }
}
