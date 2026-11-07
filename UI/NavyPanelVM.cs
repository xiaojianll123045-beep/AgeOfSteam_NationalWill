using System;
using System.Collections.Generic;
using System.Text;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Naval;      // v4.273: Ship(B-1 升级件展示用)
using TaleWorlds.CampaignSystem.Party;      // v4.279(第 7 项): MobileParty/PartyBase(修船价走 DLC 口径的 payer)
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Roster;      // v4.286c: TroopRoster(组建舰队时从其它守备营抽调水手)
using TaleWorlds.Core;                       // v4.276: InquiryElement(组建舰队的弹窗)
using TaleWorlds.Library;

namespace FeudalInternalAffairs
{
    // v4.271: 海军页行 —— 五列数字 + 两行文本, 与 FeudalNavy.xml 的列头严格对齐
    //   C1 = 第 1 列(船名/港口名/港口名/军团名), C2 = 第 2 列(舰种/势力·船坞/出发→目的)
    //   D1..D4 = 四个右对齐数值列(船员 / 耐久 / 战力 / 维护·造价·收益·航程)
    //   numX/numRight# = 四个数值列的 x 与右边缘(面板内坐标; 由 VM 传入, 与 prefab 注释表同源)
    //   Icon + HasAction/ActionText 与旧版一致; 退役的 C3/C4 保留属性只为兼容旧绑定, 面板已不使用。
    public class NavyRowVM : ViewModel
    {
        private readonly string _icon;
        private readonly string _c2, _c3, _c4, _act;                     // v4.286: C1/Note 要按列宽截断, 故去掉 readonly
        private string _c1;
        private string _d1 = "", _d2 = "", _d3 = "", _d4 = "";          // v4.271: 四列数值(v4.286: 纯文本, 由 prefab 定列)
        private readonly string _c1c, _c2c;
        private readonly string _d1c, _d2c, _d3c;
        private string _d4c;                                             // 海运页"航程"改青色, 故非 readonly
        private float _actW = 64f;                                       // v4.286: 三页统一 64(行内 746..810)
        private float _actX = 746f;                                      // v4.286: 三页统一 746(与热区同源)
        private string _note, _noteColor;                                // v4.286: 说明行按像素宽截断
        private readonly string _actColor;
        private readonly bool _bar;
        private readonly float _barRatio;
        private readonly string _barColor;

        // ==================== v4.279(第 3 项): 行的**稳定标识** ====================
        //   【为什么要它】行内操作键的热区下标是**上一次 Refresh 时**算好的(NavyPanel.RegisterSpots),
        //   而面板 3 秒才刷新一次(NavyPanel.OnTick), 数据源却随时可能变:
        //     · NavalFleet.Offers() 每次从 port.Town.AvailableShips 重新生成并 Sort  -> 船的行号会跳;
        //     · NavalBridge.EnemyPorts() 每次重算(停战/易主/新宣战)                -> 港口的行号会跳;
        //     · NavalTransport.Active 会被小时结/日结增删                            -> 运输队的行号会跳。
        //   原来点击时用 `i` 去**重新生成的列表**里索引 => 玩家点第 3 行可能买到第 4 行的船型、
        //   封锁错港口、召回错运输队。现在渲染时把稳定 key 写进行对象, 热区回调**捕获 key**,
        //   点击时按 key 反查(见 RowActionByKey) —— 命中的一定是玩家**看到的那一行**。
        //   key 取值: 船 = Ship.RandomValue(SaveableProperty, 进程内唯一且存档持久);
        //             港口 = Settlement.StringId; 运输队 = Convoy.PartyId(军团 PartyId, 同样稳定)。
        internal string Key = "";

        // 行内各文本控件的宽度/字号 —— **必须与 FeudalNavy.xml 的 SuggestedWidth / Brush.FontSize 逐一相同**,
        //   因为截断就是按这把尺子算的; 改列宽/列位置时必须同时改 prefab 与本组常量(两处一起改才不错位)。
        private const float WName = 126f, WType = 442f, WNote = 664f;
        private const float FName = 20f, FType = 18f, FNote = 17f;

        internal NavyRowVM(string icon, string c1, string c2,
                           string d1, string d2, string d3, string d4,
                           string note, string noteColor, bool bar, float barRatio,
                           string action, string actionColor)
        {
            _icon = string.IsNullOrEmpty(icon) ? "fia_ship_cutter" : icon;
            // v4.286: 名称列(行内 68..194 = 126 像素, 20 号)与舰种列(行内 194..358 = 164 像素, 18 号)
            //   都按控件宽做像素级截断 —— 否则"战列舰""第纳尔"这类字会直接画出面板右边界。
            _c1 = Clip(c1 ?? "", WName, FName);
            _c2 = Clip(c2 ?? "", WType, FType);
            _d2 = Clip(d2 ?? "", 92f, 17f);   // 耐久是"当前/上限"两段数字, 列宽 92、字号 17(不低于 17), 超长截断
            _c3 = ""; _c4 = "";                                   // v4.271: 旧三/四列已并入列头与数值列
            _d1 = Num(d1); _d3 = Num(d3); _d4 = Num(d4);
            // 说明行(行内 68..738 = 670 像素, 17 号): 只占一行, 超长截断(完整值的业务含义见 BuildFleetRows)
            _note = Clip(note ?? "", WNote, FNote);
            _noteColor = string.IsNullOrEmpty(noteColor) ? "#A89C7ACC" : noteColor;
            _bar = bar;
            _barRatio = barRatio < 0f ? 0f : (barRatio > 1f ? 1f : barRatio);
            _barColor = FleetStatus.ColorOfRatio(barRatio);
            _act = action ?? "";
            _actColor = string.IsNullOrEmpty(actionColor) ? "#E8C33AFF" : actionColor;
            _c1c = FleetStatus.NameColorOf(_c1);
            _c2c = FleetStatus.SecondColorOf(_c2);
            _d1c = "#E8DCC0FF";                                   // 船员/船数: 米白
            _d2c = "#D8C9A0FF";                                   // 耐久/收益: 浅卡其
            _d3c = "#E8C33AFF";                                   // 战力: 金
            _d4c = "#D98C4AFF";                                   // 维护/造价: 橙
        }

        // v4.271: 耐久/进度条的像素宽 = 比例 × 652(与 prefab 里底槽的 SuggestedWidth 一致)
        //   v4.286: 底槽 440 -> 664(说明行/条不再与数值行抢横向空间, 可以铺到行内 732)
        [DataSourceProperty] public float BarW { get { return _barRatio * 664f; } }

        // ==================== v4.286(用户反馈第 1 条): 删掉 `|x,w,文本|` 内联排版 ====================
        //   【原实现】SetNums(x, right1..right4) 把四个数值包成 "|68,232,45|" 这种内联标记, 指望
        //   渲染器按 x/宽摆位 —— 四个数值列的 MarginLeft 全是 68、SuggestedWidth 各不相同。
        //   字号放大后这套标记彻底失效: 四列全部右对齐到同一个 x, 于是"船员/耐久/战力/造价"
        //   叠成一坨, 而列头(独立控件)还在原位 —— 这正是用户截图里"数值与列头对不上"的根因。
        //   【现在】数值列改成 prefab 里**每列一个独立 TextWidget**(MarginLeft 写死 + 右对齐),
        //   D1..D4 只返回纯文本; 右边缘由 prefab 的 MarginLeft + SuggestedWidth 决定, 列头用同一组数。
        //   —— 与陆军页 FeudalArmy.xml 军团行的做法完全一致(那套写法已经在用, 是验证过的)。

        // 海运页的"航程"不是钱, 单独用青色, 避免与维护费混同
        internal void SetVoyageColor() { _d4c = "#8FD8E8FF"; }

        // ==================== v4.286(用户反馈第 4 条): 定宽列的像素级截断 ====================
        //   字号放大后"战列舰""第纳尔"这类文字会画到控件右边界之外(面板宽 860 也扛不住)。
        //   这里按**估算像素宽**截断(与项目既有口径一致: UI\OutlinerVM.cs 的 Fit 用 中文 15px/字、
        //   其余 9px/字 @15 号字; 本页字号是 17/18/20, 所以按 em 比例缩放):
        //     全角(CJK/中文标点, U+2E80 以上) = 1.0 em     ASCII 与半角 = 0.55 em
        //   em = 字号; 只有"真的超宽"才截, 截断后补 "…"(省略号本身占 1 em)。
        //   ★ 只影响显示, 不改任何业务字段(C1/Note 的原始值仍完整存在 VM 里)。
        private const float AsciiEm = 0.55f;

        // 估算像素宽(font = 字号)
        internal static float TextWidth(string s, float font)
        {
            try
            {
                if (string.IsNullOrEmpty(s)) return 0f;
                float w = 0f;
                for (int i = 0; i < s.Length; i++) w += (s[i] >= 0x2E80 ? 1.0f : AsciiEm) * font;
                return w;
            }
            catch { return 0f; }
        }

        // 按像素宽截断加省略号(maxPx = 该控件的 SuggestedWidth)
        internal static string Clip(string s, float maxPx, float font)
        {
            try
            {
                if (string.IsNullOrEmpty(s)) return "";
                if (TextWidth(s, font) <= maxPx) return s;
                float budget = maxPx - 1.0f * font;      // 给末尾的 "…" 留一格
                if (budget < font) budget = font;
                var sb = new StringBuilder();
                float w = 0f;
                for (int i = 0; i < s.Length; i++)
                {
                    float cw = (s[i] >= 0x2E80 ? 1.0f : AsciiEm) * font;
                    if (w + cw > budget) break;
                    sb.Append(s[i]); w += cw;
                }
                return sb.ToString() + "…";
            }
            catch { return s; }
        }

        // ==================== v4.286(用户反馈第 1 条后半): 大额数字的口径统一 ====================
        //   造价那一列会出现 4866107 这种 7 位数(17 号字下约 65 像素, 挤掉整列)。
        //   项目里大额数字的既有口径是 UI\ArmyVM.cs 的 MilFmt.N: <1 万走 "N0" 千分位,
        //   >=1 万折成 "X.X 万"。本页的数值列很窄(战力列 84 像素), 所以照抄这个口径,
        //   但只对"纯 ASCII 数字串 + 长度 > 4"生效 —— 否则 "1680/1680" 这种分数形态会被拆坏。
        internal static string Num(string s)
        {
            try
            {
                if (string.IsNullOrEmpty(s)) return "";
                if (s.Length <= 4) return s;
                for (int i = 0; i < s.Length; i++)
                    if (s[i] < '0' || s[i] > '9') return s;
                long v = long.Parse(s, System.Globalization.CultureInfo.InvariantCulture);
                return MilFmt.N(v);
            }
            catch { return s; }
        }

        // v4.268: 图标走图集(ModuleData\FeudalIcons.bin) —— fia_ship_* / fia_bld_port 等, 与陆军 UI 同款绑定(Sprite="@Icon")
        [DataSourceProperty] public string Icon { get { return _icon; } }
        [DataSourceProperty] public string C1 { get { return _c1; } }
        [DataSourceProperty] public string C2 { get { return _c2; } }
        [DataSourceProperty] public string C3 { get { return _c3; } }
        [DataSourceProperty] public string C4 { get { return _c4; } }
        [DataSourceProperty] public string ActionText { get { return _act; } }
        [DataSourceProperty] public bool HasAction { get { return !string.IsNullOrEmpty(_act); } }
        // v4.271: 行内操作键宽度 —— 造船/海运 = 136, 封锁 = 64(与热区宽度同源, 见 NavyPanel.RowActionWidth)
        //   v4.282: 改成 **float** 绑定(: 面板里所有 MarginLeft/SuggestedWidth 的既有绑定 @PanelOffset/@BarW
        //   都是 float, 字符串绑定在 MarginLeft 上没有先例 —— 用 float 才与已验证的写法一致, 不会出现
        //   "绑定转换失败 -> 按钮画在 x=0 而热区在 666"这种最要命的错位)。
        [DataSourceProperty] public float ActW { get { return _actW; } }
        // v4.282: 行内操作键的 x 也绑定给 prefab(MarginLeft="@ActX") —— 旧版 prefab 写死 640 而封锁页热区按 632 算,
        //   两边差 8 像素(点得中但边缘会漏); 现在**渲染与热区读同一个数**, 不可能再错位。
        //   v4.286: 三个页签共用一个几何(NavyPanelVM.RowActionXConst/RowActionWConst), setter 随之删除。
        [DataSourceProperty] public float ActX { get { return _actX; } }

        // v4.271: 四列数值 + 各自配色(列头统一在页面上方, 行内不再重复标签)
        [DataSourceProperty] public string D1 { get { return _d1; } }
        [DataSourceProperty] public string D2 { get { return _d2; } }
        [DataSourceProperty] public string D3 { get { return _d3; } }
        [DataSourceProperty] public string D4 { get { return _d4; } }
        [DataSourceProperty] public string D1Color { get { return _d1c; } }
        [DataSourceProperty] public string D2Color { get { return _d2c; } }
        [DataSourceProperty] public string D3Color { get { return _d3c; } }
        [DataSourceProperty] public string D4Color2 { get { return _d4c; } }
        [DataSourceProperty] public string C1Color { get { return _c1c; } }
        [DataSourceProperty] public string C2Color { get { return _c2c; } }
        [DataSourceProperty] public string ActionColor { get { return _actColor; } }

        // v4.271: 第二行说明文字(舰队页 = 完整度+修理费+载货; 其余页 = 补充信息)
        [DataSourceProperty] public string Note { get { return _note; } }
        [DataSourceProperty] public bool HasNote { get { return !string.IsNullOrEmpty(_note); } }
        [DataSourceProperty] public string NoteColor { get { return _noteColor; } }

        // v4.271: 耐久/进度条 —— 完整度一眼可见, 破损一眼看出
        [DataSourceProperty] public bool ShowBar { get { return _bar; } }
        [DataSourceProperty] public float BarRatio { get { return _barRatio; } }
        [DataSourceProperty] public string BarColor { get { return _barColor; } }
    }

    // 战报行(整段文本)
    public class NavyLogVM : ViewModel
    {
        private readonly string _title, _body, _color;
        internal NavyLogVM(string title, string body) : this(title, body, "#E9CD74FF") { }
        internal NavyLogVM(string title, string body, string color)
        {
            _title = title ?? ""; _body = body ?? ""; _color = string.IsNullOrEmpty(color) ? "#E9CD74FF" : color;
        }
        [DataSourceProperty] public string Title { get { return _title; } }
        [DataSourceProperty] public string Body { get { return _body; } }
        [DataSourceProperty] public string TitleColor { get { return _color; } }
    }

    // v4.271: 海军页配色/判定集中在这里, 保证"行内数值颜色"与"列头颜色"一处可控
    internal static class FleetStatus
    {
        internal static string HealthWord(float r)
        {
            if (r >= 0.99f) return "完好";
            if (r >= 0.85f) return "轻损";
            if (r >= 0.50f) return "受损";
            return "重创";
        }

        internal static string ColorOfRatio(float r)
        {
            if (r >= 0.99f) return "#7FD8A0FF";   // 完好: 绿
            if (r >= 0.85f) return "#D6D06AFF";   // 轻损: 黄
            if (r >= 0.50f) return "#E0A24AFF";   // 受损: 橙
            return "#D96A5AFF";                    // 重创: 红
        }

        // 舰种·状态 -> 文本左对齐列的配色(重创优先)
        internal static string SecondColorOf(string s)
        {
            if (string.IsNullOrEmpty(s)) return "#C8B98FCC";
            if (s.Contains("重创")) return "#D96A5AFF";
            if (s.Contains("受损")) return "#E0A24AFF";
            if (s.Contains("轻损")) return "#D6D06AFF";
            if (s.Contains("完好")) return "#9FC08AFF";
            if (s.Contains("正在封锁")) return "#7FD8A0FF";
            if (s.Contains("不可建造")) return "#C96A5AFF";
            return "#C8B98FCC";
        }

        internal static string NameColorOf(string s)
        {
            return "#F0E4C8FF";
        }

        // v4.271: 战力分/日维护费的估算口径 —— 与 NavalBridge.ShipPowerScore / ShipUpkeep 同一公式,
        //   只用于造船页"还没买、先看值不值"的试算, 不参与任何结算。
        internal static string PowerOf(float crew, float value, float hp, int tier, float ratio)
        {
            try
            {
                if (crew <= 0f || value <= 0f) return "—";
                float guns = crew;
                float dmg = value / 1000f;
                float armor = hp / 1000f;
                float typeK = 1f + tier * 0.25f;
                float p = guns * dmg * (1f + armor / 100f) * typeK * (ratio < 0f ? 0f : (ratio > 1f ? 1f : ratio));
                return ((int)Math.Round(p)).ToString();
            }
            catch { return "—"; }
        }

        internal static string UpkeepOf(float value, float ratio)
        {
            try
            {
                if (value <= 0f) return "—";
                return (value / 1200f * (0.6f + (ratio < 0f ? 0f : (ratio > 1f ? 1f : ratio)))).ToString("0.0");
            }
            catch { return "—"; }
        }
    }

    public class NavyPanelVM : PanelVMBase
    {
        private readonly Action _onClose;
        private int _tab;
        private int _scroll;

        // 造船页选中的港口索引(在 NavalBridge.PortsOf(我方) 列表里)
        private int _portIdx;
        // 封锁页选中的敌港索引
        private int _enemyPortIdx;
        // v4.270: 海运页选中的 军团 / 出发港 / 目的港 索引
        private int _legionIdx;
        private int _fromPortIdx;
        private int _toPortIdx;
        private string _status = "";

        // ==================== v4.286: 行高与每页行数(必须与 NavyPanel / FeudalNavy.xml 一致) ====================
        //   RowH 这个词在两个文件里各有一份, 两处**必须逐字节相同**, 否则热区 y 会与画出来的行错位:
        //     本文件 = NavyPanelVM.RowH(3 行 × 128 = 384 像素, 数据区 472..856)
        //     NavyPanel.cs = NavyPanel.RowH(热区用的同一把尺子)
        //     两者又都必须等于 FeudalNavy.xml 里行模板的 SuggestedHeight 与 ListPanel 的 MarginTop。
        internal const int RowsPerPage = 4;   // v4.286c: 3 -> 4(去掉行内完整度条 + 构成条压成一行后, 496..880)
        internal const float RowH = 96f;      // v4.286c: 112 -> 96(三层文字 32+26+24=82 < 96, 核对器逐条验过)

        // v4.271: 出击/运输操作会自己切页(原来只改 _tab, 面板热区要等最长 3 秒才跟上 -> 期间点哪都不对)
        private readonly Action _onTabChanged;

        // v4.271: 空状态引导文字(列表为空时显示在列头下方; 有内容时置空)
        private string _emptyFleet = "", _emptyYard = "", _emptyBlock = "", _emptyTrans = "", _emptyLogs = "";
        // v4.271: 海运页判语色带(可出发/不可出发 + 原因)
        private string _planBand = "", _planBandColor = "#C8B98FCC";

        public NavyPanelVM(Action onClose) : this(onClose, null) { }

        public NavyPanelVM(Action onClose, Action onTabChanged)
        {
            _onClose = onClose;
            _onTabChanged = onTabChanged;
            Rows = new MBBindingList<NavyRowVM>();
            Logs = new MBBindingList<NavyLogVM>();
            TabNames = new MBBindingList<NavyTabVM>();
            TabNames.Add(new NavyTabVM("舰队", 0));
            TabNames.Add(new NavyTabVM("造船", 1));
            TabNames.Add(new NavyTabVM("封锁", 2));
            TabNames.Add(new NavyTabVM("战报", 3));
            // v4.270: 海运 = 第 5 页签(index 4)。**故意排在战报之后**: 战报页的 index 3 已被
            //   ShowLogs / Sortie 跳转等多处引用, 把海运插在中间会牵动那些判定 —— 追加在末尾最安全。
            TabNames.Add(new NavyTabVM("海运", 4));
            Refresh();
        }

        public MBBindingList<NavyRowVM> Rows { get; private set; }
        public MBBindingList<NavyLogVM> Logs { get; private set; }
        public MBBindingList<NavyTabVM> TabNames { get; private set; }

        [DataSourceProperty] public int Tab { get { return _tab; } }
        [DataSourceProperty] public bool ShowRows { get { return _tab != 3; } }
        [DataSourceProperty] public bool ShowLogs { get { return _tab == 3; } }

        // v4.268: 与 FeudalNavy.xml 绑定 —— 港口切换条只在造船/封锁页显示; 出击按钮只在封锁页显示
        // v4.282: 造船页与封锁页的控制条拆成两个可见性(封锁页多一枚[出击]), 但几何完全同源
        [DataSourceProperty] public bool ShowPortPicker { get { return _tab == 1; } }
        [DataSourceProperty] public bool ShowEnemyPicker { get { return _tab == 2; } }
        [DataSourceProperty] public bool ShowSortie { get { return _tab == 2; } }

        // ==================== v4.276: 舰队页的"舰队指挥条" ====================
        //   与港口/封锁/海运三条"切换条"同一几何(y=240 h=52), 但只在舰队页(index 0)显示。
        //   用途: 组建 / 解散 / 召回 —— 组建完成后舰队就是地图上的实体部队(Navy\NavalTaskForce),
        //   日常指挥在地图上(左键点它 + 左键点目标 + 右键菜单), 不在这个面板里。
        private int _fleetIdx;
        [DataSourceProperty] public bool ShowFleetBar { get { return _tab == 0; } }

        // ==================== v4.282: 地图点选城市(用户: "不要强制列表选择, 要能点击城市") ====================
        //   五个入口共用一份状态:
        //     造船页港口 / 封锁页敌港 / 海运页出发港 / 海运页目的港 / 舰队页"进驻城市"。
        //   进入 = 点各页的[选择城市]按钮; 退出三条路(见 Map\SettlementPickMode.cs):
        //     ① 再点一次同一个按钮(StartPick 先判 _pick == t -> 取消) ② 右键 ③ Esc。
        //   ★ 合法性过滤在 `RejectReason()` 里**逐条给中文原因**, 不静默忽略(用户要求)。
        internal const int PickNone = 0;
        internal const int PickShipyard = 1;
        internal const int PickEnemy = 2;
        internal const int PickFrom = 3;
        internal const int PickTo = 4;
        internal const int PickMoor = 5;

        private int _pick = PickNone;
        internal bool IsPicking { get { return _pick != PickNone; } }
        internal int Picking { get { return _pick; } }

        // 按钮文案: 点选期间显示"取消…", 让"再点一次 = 取消"这件事在界面上看得见
        [DataSourceProperty] public string PickBtnText
        {
            get { return (_pick == PickShipyard || _pick == PickEnemy || _pick == PickFrom || _pick == PickTo) ? "取消选点" : "选择城市"; }
        }

        [DataSourceProperty] public string MoorBtnText
        {
            get { return _pick == PickMoor ? "取消选点" : "进驻城市"; }
        }

        internal void StartPick(int t) { StartPick(t, true); }

        private void StartPick(int t, bool toggle)
        {
            try
            {
                if (_pick == t && toggle) { CancelPick("已取消地图选点", false); return; }
                if (!NavalBridge.Usable) { _status = "未检测到海战 DLC —— 海军功能整体停用"; OnChanged(); return; }
                if (Kingdom() == null) { _status = "尚未接管王国 —— 海军功能在接管王国后可用"; OnChanged(); return; }
                _pick = t;
                string prompt = PickPrompt(t);
                _status = "地图点选中: " + prompt + "(右键 / Esc / 再点一次按钮 = 取消)";
                SettlementPickMode.Begin(SettlementPickMode.OwnerNavy, prompt, OnPickedSettlement);
                Touch();
            }
            catch (Exception ex) { _status = "进入地图点选失败: " + ex.Message; }
        }

        // 退出点选(不改任何业务选择)。notify=false 时不再弹地图消息(调用方自己已经说明了)
        internal void CancelPick(string why, bool notify = true)
        {
            try
            {
                _pick = PickNone;
                SettlementPickMode.Cancel();
                if (!string.IsNullOrEmpty(why)) _status = why;
                if (notify && !string.IsNullOrEmpty(why)) { try { MapSelection.Message("海军: " + why); } catch { } }
                Touch();
            }
            catch { }
        }

        private static string PickPrompt(int t)
        {
            switch (t)
            {
                case PickShipyard: return "请在地图上点击要造船的自有沿海城市";
                case PickEnemy: return "请在地图上点击要封锁的敌国沿海城市";
                case PickFrom: return "请在地图上点击海运的出发港(自有沿海城市)";
                case PickTo: return "请在地图上点击海运的目的港(本国或中立沿海城市)";
                case PickMoor: return "请在地图上点击要进驻的沿海城市(只能是你自己的沿海城市)";
                default: return "请在地图上点击目标城市";
            }
        }

        // 地图点选回调(公共注册表在玩家点中城市后调用; 命中即已退出点选态)
        internal void OnPickedSettlement(Settlement s)
        {
            int t = _pick;
            _pick = PickNone;
            try
            {
                string why = RejectReason(t, s);
                if (!string.IsNullOrEmpty(why))
                {
                    _status = why;
                    try { MapSelection.Message("海军: " + why); } catch { }
                    Touch();
                    return;
                }
                var k = Kingdom();
                string nm = NavalBridge.NameOf(s);
                switch (t)
                {
                    case PickShipyard:
                        {
                            int at = IndexOfPort(NavalBridge.PortsOf(k), s);
                            if (at < 0) { _status = "「" + nm + "」当前不在可选造船港口列表里"; break; }
                            _portIdx = at; _scroll = 0;
                            _status = "造船港口已选定: 「" + nm + "」(船坞 " + NavalBridge.ShipyardLevel(s) + " 级)";
                            break;
                        }
                    case PickEnemy:
                        {
                            int at = IndexOfPort(NavalBridge.EnemyPorts(k), s);
                            if (at < 0) { _status = "「" + nm + "」当前不在可封锁列表里"; break; }
                            _enemyPortIdx = at; _scroll = 0;
                            _status = "封锁目标已选定: 「" + nm + "」—— 点[出击]或行内[封锁]开始";
                            break;
                        }
                    case PickFrom:
                        {
                            int at = IndexOfPort(NavalBridge.PortsOf(k), s);
                            if (at < 0) { _status = "「" + nm + "」当前不在可选出发港列表里"; break; }
                            _fromPortIdx = at; _scroll = 0;
                            _status = "海运出发港已选定: 「" + nm + "」";
                            break;
                        }
                    case PickTo:
                        {
                            int at = IndexOfPort(NavalTransport.DestinationPorts(k), s);
                            if (at < 0) { _status = "「" + nm + "」当前不是可选目的港"; break; }
                            _toPortIdx = at; _scroll = 0;
                            _status = "海运目的港已选定: 「" + nm + "」—— 选好①②③后点底部[装船出发]";
                            break;
                        }
                    case PickMoor:
                        {
                            var list = Fleets();
                            if (list.Count == 0) { _status = "尚未组建舰队 —— 先点[组建舰队]"; break; }
                            if (_fleetIdx < 0 || _fleetIdx >= list.Count) _fleetIdx = 0;
                            var p = NavalTaskForce.PartyOf(list[_fleetIdx]);
                            if (p == null) { _status = "舰队不在场(可能已被歼灭)"; break; }
                            string ok;
                            string err = NavalTaskForce.Moor(p, s, out ok);
                            _status = string.IsNullOrEmpty(err) ? ok : err;
                            // 顺便把地图选中切到这支舰队, 玩家点完就能看到它在地图上的位置
                            try { MapSelection.Select(p); } catch { }
                            break;
                        }
                }
                // 无论选了哪一处, 都把镜头飞到该城 —— "我到底选了哪座城"用眼睛确认, 不用看文字
                try { PanelScreen.JumpToSettlement(s); } catch { }
                Touch();
            }
            catch (Exception ex) { _status = "地图点选处理失败: " + ex.Message; Touch(); }
        }

        // 合法性过滤(全部返回中文原因; "" = 通过)—— 面板点选与快速循环共用同一份判据
        private string RejectReason(int t, Settlement s)
        {
            try
            {
                if (s == null) return "没有选中任何城市";
                var k = Kingdom();
                if (k == null) return "尚未接管王国";
                string nm = NavalBridge.NameOf(s);
                if (!s.IsTown) return "「" + nm + "」不是城镇 —— 海军目标只能是城镇(城堡/村庄没有港口)";
                if (!s.HasPort) return "「" + nm + "」不是沿海城市(没有港口) —— 海军目标只能是沿海城镇";
                var owner = NavalBridge.KingdomOf(s);
                string on = owner != null && owner.Name != null ? owner.Name.ToString() : "?";
                if (t == PickEnemy)
                {
                    if (owner == null) return "「" + nm + "」已无主, 无法封锁";
                    if (ReferenceEquals(owner, k)) return "「" + nm + "」是你自己的港口 —— 封锁目标必须是敌国港口";
                    if (!k.IsAtWarWith(owner)) return "你与「" + on + "」并未交战 —— 不能封锁其港口「" + nm + "」";
                    return "";
                }
                if (t == PickTo)
                {
                    if (owner != null && !ReferenceEquals(owner, k) && k.IsAtWarWith(owner))
                        return "「" + nm + "」属于交战国「" + on + "」—— 目的港只能是本国或中立沿海城市";
                    return "";
                }
                // 造船港 / 出发港 / 进驻城市: 必须是自己王国的沿海城镇
                if (owner == null) return "「" + nm + "」已无主";
                if (!ReferenceEquals(owner, k)) return "「" + nm + "」属于「" + on + "」—— 只能选你自己的沿海城市";
                return "";
            }
            catch (Exception ex) { return "目标校验异常: " + ex.Message; }
        }

        private static int IndexOfPort(List<Settlement> list, Settlement s)
        {
            try
            {
                if (list == null || s == null) return -1;
                for (int i = 0; i < list.Count; i++)
                    if (list[i] != null && list[i].StringId == s.StringId) return i;
            }
            catch { }
            return -1;
        }

        // 把镜头飞到当前页签选中的那处港口(控制条上的[跳到该城])
        internal void FlyToPickerPort()
        {
            try
            {
                var k = Kingdom();
                if (k == null) return;
                Settlement s = null;
                if (_tab == 1) s = SelectedPort(k);
                else if (_tab == 2)
                {
                    var l = NavalBridge.EnemyPorts(k);
                    if (l.Count > 0)
                    {
                        if (_enemyPortIdx < 0 || _enemyPortIdx >= l.Count) _enemyPortIdx = 0;
                        s = l[_enemyPortIdx];
                    }
                }
                else if (_tab == 4) s = SelectedFromPort(k);
                else if (_tab == 0)
                {
                    // 舰队页: 没有"选中的港口", 就跳到它现在的驻泊港/母港(玩家一眼看到它停在哪)
                    var fl = Fleets();
                    if (fl.Count > 0)
                    {
                        if (_fleetIdx < 0 || _fleetIdx >= fl.Count) _fleetIdx = 0;
                        var f = fl[_fleetIdx];
                        var id = !string.IsNullOrEmpty(f.MooredPortId) ? f.MooredPortId : f.HomePortId;
                        s = NavalTaskForce.FindSettlement(id);
                    }
                }
                if (s == null) { _status = "当前没有可跳转的港口(舰队可能没有母港/驻泊港)"; Touch(); return; }
                PanelScreen.JumpToSettlement(s);
            }
            catch { }
        }

        // 选中的舰队部队(多个地方要用; 索引越界自愈)
        private MobileParty SelectedFleetParty()
        {
            try
            {
                var list = Fleets();
                if (list.Count == 0) return null;
                if (_fleetIdx < 0 || _fleetIdx >= list.Count) _fleetIdx = 0;
                return NavalTaskForce.PartyOf(list[_fleetIdx]);
            }
            catch { return null; }
        }

        // [进驻城市]: 进入地图点选(选中城市后由 OnPickedSettlement -> NavalTaskForce.Moor 落地)
        internal void MoorSelected()
        {
            try
            {
                if (!NavalBridge.Usable) { _status = "未检测到海战 DLC —— 舰队功能整体停用"; Touch(); return; }
                if (Fleets().Count == 0) { _status = "尚未组建舰队 —— 先点[组建舰队]"; Touch(); return; }
                StartPick(PickMoor);
            }
            catch (Exception ex) { _status = "进驻失败: " + ex.Message; Touch(); }
        }

        // [离港]: 解除驻泊(母港保留)
        internal void UnmoorSelected()
        {
            try
            {
                var p = SelectedFleetParty();
                if (p == null) { _status = Fleets().Count == 0 ? "尚未组建舰队" : "舰队不在场"; Touch(); return; }
                _status = NavalTaskForce.Unmoor(p);
                if (_pick == PickMoor) CancelPick("", false);
                Touch();
            }
            catch (Exception ex) { _status = "离港失败: " + ex.Message; Touch(); }
        }

        // 刷新 + 通知(所有"点了按钮立刻要看到结果"的路径统一走它, 免得漏通知某个绑定属性)
        private void Touch() { try { Refresh(); if (_onTabChanged != null) _onTabChanged(); } catch { } }

        private List<NavalTaskForce.Force> Fleets()
        {
            try { return NavalTaskForce.Alive(); } catch { return new List<NavalTaskForce.Force>(); }
        }

        // v4.277(第 1 项): 全部在编舰队的日耗拆成"船维护"与"水手军饷"两项(合计 = 两者相加)。
        //   与 NavalTaskForce.Stats(每舰队的 upkeep) / NavalTaskForce.NavalWageDaily(军饷合计) 同源,
        //   所以面板上的"合计"与日结真实从国库扣掉的数字是**同一口径**, 不是面板自己另算一套。
        private void AllFleetCost(out int upkeep, out int wage)
        {
            upkeep = 0; wage = 0;
            try
            {
                var list = Fleets();
                for (int i = 0; i < list.Count; i++)
                {
                    var p = NavalTaskForce.PartyOf(list[i]);
                    if (p == null) continue;
                    int ships, crew, sailors;
                    float power, ratio, morale, up, wg;
                    NavalTaskForce.Stats(p, out ships, out power, out ratio, out crew, out sailors, out morale, out up, out wg);
                    upkeep += (int)Math.Round(up);
                    wage += (int)Math.Round(wg);
                }
            }
            catch { }
        }

        [DataSourceProperty] public string FleetBarText
        {
            get
            {
                try
                {
                    if (!NavalBridge.Usable) return "海军未启用";
                    var list = Fleets();
                    if (list.Count == 0) return "尚未组建舰队";
                    if (_fleetIdx < 0 || _fleetIdx >= list.Count) _fleetIdx = 0;
                    var f = list[_fleetIdx];
                    var p = NavalTaskForce.PartyOf(f);
                    string nm = p != null ? MapSelection.NameOf(p) : f.HomePortName;
                    return (list.Count > 1 ? ("(" + (_fleetIdx + 1) + "/" + list.Count + ") ") : "") + nm;
                }
                catch { return "—"; }
            }
        }

        // 指挥条右侧的一句话说明(舰队属性, 与地图小窗同一口径)
        [DataSourceProperty] public string FleetBarHint
        {
            get
            {
                try
                {
                    if (!NavalBridge.Usable) return "未检测到海战 DLC";
                    var list = Fleets();
                    if (list.Count == 0)
                        return "点[组建舰队]选一处自有港口, 再从氏族可支配的船里挑船 —— 编成后它会出现在地图上";
                    if (_fleetIdx < 0 || _fleetIdx >= list.Count) _fleetIdx = 0;
                    var p = NavalTaskForce.PartyOf(list[_fleetIdx]);
                    if (p == null) return "舰队不在场";
                    // v4.277(第 1 项): 指挥条这句是把"舰队到底花多少钱"摆在明处的地方 ——
                    //   船维护与**水手军饷**分开写、再给合计(Summary 里已经是同一口径), 再补一行
                    //   全部舰队的日耗总额, 免得玩家只看单支舰队就以为那是全部海军开销。
                    int allUpkeep, allWage;
                    AllFleetCost(out allUpkeep, out allWage);
                    string total = list.Count > 1
                        ? " · 全部 " + list.Count + " 支合计 船维护 " + allUpkeep + " + 水手军饷 " + allWage
                          + " = " + (allUpkeep + allWage) + " 第纳尔/日"
                        : "";
                    return NavalTaskForce.Summary(p) + " · 上限 " + NavalTaskForce.MaxForces + " 支" + total;
                }
                catch { return "—"; }
            }
        }

        internal void NextFleet() { CycleFleet(1); }
        internal void PrevFleet() { CycleFleet(-1); }
        private void CycleFleet(int d)
        {
            try
            {
                var list = Fleets();
                if (list.Count == 0) { _fleetIdx = 0; return; }
                _fleetIdx = (_fleetIdx + d + list.Count) % list.Count;
                // v4.284(第 2 项): 换了一支舰队 -> **立刻**刷新。
                //   原来这里只改索引(靠 3 秒节流的 OnTick 才刷新), 玩家按下 ◀ ▶ 后最长 3 秒里
                //   控制条/状态行/构成条显示的还是上一支舰队 —— "选中谁看谁"的交互不能有这个窗口。
                //   Touch() = Refresh + 重登记热区(与其它页的 Cycle* 一致)。
                Touch();
            }
            catch { }
        }

        // [回港] —— 让选中的舰队回母港(等价于右键菜单里的[回母港])
        internal void RecallFleet()
        {
            try
            {
                var list = Fleets();
                if (list.Count == 0) { _status = "尚未组建舰队"; return; }
                if (_fleetIdx < 0 || _fleetIdx >= list.Count) _fleetIdx = 0;
                var p = NavalTaskForce.PartyOf(list[_fleetIdx]);
                if (p == null) { _status = "舰队不在场"; return; }
                _status = NavalTaskForce.OrderRecall(p);
                // 顺便把地图选中切到它 —— 玩家从面板下令后能立刻在地图上看到并接管
                try { MapSelection.Select(p); } catch { }
            }
            catch (Exception ex) { _status = "回港失败: " + ex.Message; }
        }

        // [解散舰队] —— 二次确认(与地图右键菜单同一口径)
        internal void DisbandFleet()
        {
            try
            {
                var list = Fleets();
                if (list.Count == 0) { _status = "尚未组建舰队"; return; }
                if (_fleetIdx < 0 || _fleetIdx >= list.Count) _fleetIdx = 0;
                var f = list[_fleetIdx];
                var p = NavalTaskForce.PartyOf(f);
                if (p == null) { _status = "舰队不在场"; return; }
                PanelInputGuard.ShowPopup(new MultiSelectionInquiryData(
                    "解散舰队", "确定解散「" + MapSelection.NameOf(p) + "」吗?\n舰船会按原主归还氏族, 司令退役, 编制注销。",
                    new List<InquiryElement>
                    {
                        new InquiryElement("yes", "确定解散", null, true, null),
                        new InquiryElement("no", "取消", null, true, null)
                    }, true, 1, 1, "确定", "取消",
                    delegate (List<InquiryElement> s)
                    {
                        try
                        {
                            if (s == null || s.Count == 0) return;
                            if ((s[0].Identifier as string) != "yes") return;
                            _status = NavalTaskForce.Disband(p);
                            _fleetIdx = 0;
                            Refresh();
                        }
                        catch (Exception ex) { DLog.Force("海军页解散舰队异常: " + ex.Message); }
                    }, null, null, false));
            }
            catch (Exception ex) { _status = "解散失败: " + ex.Message; }
        }

        // [组建舰队] 第一步: 选母港 -> 第二步: 选船 -> NavalTaskForce.Found
        internal void FoundFleet()
        {
            try
            {
                if (!NavalBridge.Usable) { _status = "未检测到海战 DLC —— 舰队功能整体停用"; OnChanged(); return; }
                if (NavalTaskForce.Forces.Count >= NavalTaskForce.MaxForces)
                {
                    _status = "舰队数量已达上限 " + NavalTaskForce.MaxForces + " 支 —— 先解散一支";
                    OnChanged();
                    return;
                }
                var ports = NavalTaskForce.CandidatePorts();
                if (ports.Count == 0)
                {
                    _status = "没有可用的自有港口 —— 需要一处【自有 + 有港口 + 尚未驻扎舰队】的沿海城镇";
                    OnChanged();
                    return;
                }
                // v4.285(用户反馈"选完城市之后就没有下文了"): 第二步(选船)在"自由船不足"时会直接 return,
                //   原来只写 _status(面板一行小字, 弹窗一关就看不见) ⇒ 玩家体验就是"没下文"。
                //   这里在**第一步弹窗之前**先查一次, 用弹窗把原因与出路讲清楚。
                int freeShips = 0;
                try
                {
                    var allMine = NavalFleet.MyShips();
                    for (int i = 0; i < allMine.Count; i++)
                    {
                        var s0 = allMine[i];
                        if (s0 == null) continue;
                        if (NavalTaskForce.ShipInFleet(s0)) continue;
                        freeShips++;
                    }
                }
                catch (Exception ex) { DLog.Force("组建舰队: 统计自由船失败 " + ex.Message); }
                if (freeShips <= NavalTaskForce.KeepShipsInClan)
                {
                    _status = "还没有可编入舰队的船(自由船 " + freeShips + " 艘, 至少需留 " + NavalTaskForce.KeepShipsInClan + " 艘在氏族手里)";
                    OnChanged();
                    PanelInputGuard.ShowPopup(new MultiSelectionInquiryData(
                        "组建舰队 · 还不能组建",
                        "你名下可自由编入舰队的舰船只有 " + freeShips + " 艘, 而必须至少保留 "
                        + NavalTaskForce.KeepShipsInClan + " 艘在氏族手里"
                        + "(否则海运、购船等其它海军功能会无船可用)。\n\n"
                        + "【怎么做】先到本页的 [造船] 页签, 在自有沿海城镇购买现货舰船或下单建造; 造船需要该城有【造船厂】建筑。\n"
                        + "【为什么】组建舰队会把船从氏族名下转给舰队, 所以不能把船全部编走。\n"
                        + "【接下来】备好船后回到本页点 [组建舰队]: 第一步选母港, 第二步勾选要编入的船。",
                        new List<InquiryElement> { new InquiryElement("ok", "知道了", null, true, null) },
                        true, 1, 1, "知道了", "关闭",
                        delegate (List<InquiryElement> _) { }, null, null, false));
                    return;
                }
                var options = new List<InquiryElement>();
                for (int i = 0; i < ports.Count; i++)
                {
                    var s = ports[i];
                    if (s == null) continue;
                    int men = DefArmy.GarrisonMen(s);
                    options.Add(new InquiryElement(s, NavalBridge.NameOf(s), null, true,
                        "守备营 " + men + " 人可抽作水手"));
                }
                PanelInputGuard.ShowPopup(new MultiSelectionInquiryData(
                    "组建舰队 · 第一步", "选择母港(舰队会在这里的海面生成并驻泊)",
                    options, true, 1, 1, "下一步", "取消",
                    delegate (List<InquiryElement> sel) { OnPickFleetPort(sel); }, null, null, false));
            }
            catch (Exception ex) { DLog.Force("海军页组建舰队异常: " + ex.Message); _status = "组建失败: " + ex.Message; }
        }

        private void OnPickFleetPort(List<InquiryElement> sel)
        {
            try
            {
                if (sel == null || sel.Count == 0) return;
                var port = sel[0].Identifier as Settlement;
                if (port == null) return;

                var all = NavalFleet.MyShips();
                var cand = new List<Ship>();
                for (int i = 0; i < all.Count; i++)
                {
                    var s = all[i];
                    if (s == null) continue;
                    if (NavalTaskForce.ShipInFleet(s)) continue;   // v4.276: 船源互斥 —— 已在舰队里的船不能再编
                    cand.Add(s);
                }
                // 至少要留 NavalTaskForce.KeepShipsInClan 艘在氏族手里
                //   (cand 已经排除了"已被舰队占用"的船, 所以这里的数量就是**自由船**的数量)
                if (cand.Count <= NavalTaskForce.KeepShipsInClan)
                {
                    _status = "可编入舰队的船不足 —— 氏族手里的自由船只有 " + cand.Count + " 艘, 必须至少留 "
                        + NavalTaskForce.KeepShipsInClan + " 艘(否则海运/买船等其它海军功能会被搬空)";
                    OnChanged();
                    // v4.285: 原来这里只写 _status 就 return, 弹窗一关玩家什么都看不到 = "没下文"。
                    //   现在补一个弹窗明说(即使在第一步之后船被用掉也能看到原因)。
                    PanelInputGuard.ShowPopup(new MultiSelectionInquiryData(
                        "组建舰队 · 船不够",
                        "刚选定的母港: " + NavalBridge.NameOf(port) + "\n\n"
                        + "可自由编入舰队的船只剩 " + cand.Count + " 艘, 而必须至少保留 "
                        + NavalTaskForce.KeepShipsInClan + " 艘在氏族手里。\n\n"
                        + "【怎么做】到本页 [造船] 页签继续购船/造船, 或先解散一支已有舰队把船还回氏族。",
                        new List<InquiryElement> { new InquiryElement("ok", "知道了", null, true, null) },
                        true, 1, 1, "知道了", "关闭",
                        delegate (List<InquiryElement> _) { }, null, null, false));
                    return;
                }

                var options = new List<InquiryElement>();
                for (int i = 0; i < cand.Count; i++)
                {
                    var s = cand[i];
                    string nm = NavalBridge.HullName(s);
                    try { if (s.Name != null && !string.IsNullOrEmpty(s.Name.ToString())) nm = s.Name.ToString(); } catch { }
                    int ratio = (int)Math.Round(NavalBridge.HealthRatio(s) * 100f);
                    options.Add(new InquiryElement(s, nm, null, true,
                        NavalBridge.HullTypeName(NavalBridge.HullType(s)) + "舰 · 船员 " + NavalBridge.Crew(s)
                        + " · 完整度 " + ratio + "% · 战力 " + (int)NavalBridge.ShipPowerScore(s)));
                }
                int maxPick = cand.Count - NavalTaskForce.KeepShipsInClan;
                PanelInputGuard.ShowPopup(new MultiSelectionInquiryData(
                    "组建舰队 · 第二步",
                    "母港「" + NavalBridge.NameOf(port) + "」· 勾选要编入舰队的舰船(最多 " + maxPick + " 艘, 至少留 "
                        + NavalTaskForce.KeepShipsInClan + " 艘在氏族手里)\n水手会自动从该港守备营抽调(按所选舰船的载员上限)",
                    options, true, 1, Math.Max(1, maxPick), "组建", "取消",
                    delegate (List<InquiryElement> sel2) { OnPickFleetShips(port, sel2); }, null, null, false));
            }
            catch (Exception ex) { DLog.Force("海军页选港口异常: " + ex.Message); _status = "组建失败: " + ex.Message; }
        }

        private void OnPickFleetShips(Settlement port, List<InquiryElement> sel)
        {
            try
            {
                if (sel == null || sel.Count == 0) return;
                var pick = new List<Ship>();
                for (int i = 0; i < sel.Count; i++)
                {
                    var s = sel[i] != null ? sel[i].Identifier as Ship : null;
                    if (s != null) pick.Add(s);
                }
                if (pick.Count == 0) { _status = "没有选中任何舰船"; OnChanged(); return; }

                // ==================== v4.286c(用户反馈③: "舰队怎么显示 0 人") ====================
                //   【原状】这里只从**母港守备营**抽水手: `sailors = min(船的载员上限, 该港守备营现有)`。
                //   开局守备营常常是空的 -> 抽到 0 人 -> 舰队只有司令。而查证下来(反编译 + 逐处读口径):
                //     · 军饷   = 水手数 × 费率(0 水手 = 0 军饷)                    <- 唯一受影响的
                //     · 战力   = Σ 单舰战力 × 科技 × 提督 PowerMul(不含水手/士气)   <- 不受影响
                //     · 海运运力 = Σ 船的载员上限 × 乘数(与舰队实际水手无关)          <- 不受影响
                //     · 航行 / 封锁 = 只看"有没有船 + 有没有舰队在港外"              <- 不受影响
                //   也就是"没人却照常能走能打" —— 这正是用户不接受的地方。
                //   【现在】① 先按**建议编制**(= 各船载员上限之和)把母港守备营补足, 人从**本国其它聚落
                //   守备营**如实抽调(每城至少留 1/4 或 20 人, 兵种原样搬运, 不是凭空变出来);
                //   ② 确实补不齐 -> **拒绝组建**(不让"0 人舰队"诞生), 并给出可操作指引。
                int cap = 0;
                for (int i = 0; i < pick.Count; i++) cap += NavalBridge.Crew(pick[i]);
                int need = cap;
                int avail = DefArmy.GarrisonMen(port);
                string ledger = "";
                if (avail < need)
                {
                    int topped = TopUpPortGarrison(port, need, out ledger);
                    if (topped > 0) avail = DefArmy.GarrisonMen(port);
                }
                if (avail < need)
                {
                    _status = "编成中止: 水手不足 —— 这 " + pick.Count + " 艘船需要 " + need + " 名水手(建议编制 = 各船载员上限之和), "
                            + "而母港「" + NavalBridge.NameOf(port) + "」守备营"
                            + (string.IsNullOrEmpty(ledger) ? "" : "(已从 " + ledger + " 抽调后)")
                            + "只有 " + avail + " 人, 还差 " + (need - avail) + " 人。\n"
                            + "【怎么做】先到「军务」页给本国守备营补兵(募兵/强制征兵), 或减少编入的舰船(每艘船的载员上限决定它要多少人); "
                            + "补兵后回到本页重新[组建舰队]。";
                    OnChanged();
                    return;
                }
                int sailors = Math.Min(cap, avail);

                string ok;
                string err = NavalTaskForce.Found(port, pick, sailors, out ok);
                _status = string.IsNullOrEmpty(err) ? ok : err;
                if (string.IsNullOrEmpty(err) && !string.IsNullOrEmpty(ledger))
                    _status += "\n(其中 " + ledger + " —— 从本国其它守备营如实抽调, 对应城池的在册守备数已同步扣减)";
                _fleetIdx = 0;
                Refresh();
            }
            catch (Exception ex) { DLog.Force("海军页组建舰队异常: " + ex.Message); _status = "组建失败: " + ex.Message; }
        }

        // ==================== v4.286c: 把母港守备营补到 need 人(用户反馈③"有几艘船就配多少人") ====================
        //   来源顺序: ① 玩家主队当前所在聚落 ② 本国其它沿海城镇。每座城**至少留 1/4 或 20 人**
        //   (不能把守备抽空 -> 那等于为了让舰队有人而让城市失守)。
        //   搬运方式是 `DefArmy.MoveGarrisonTroopsTo(源城, 临时名册, n)` 取人, 再原样 `AddToCounts` 进母港守备营
        //   —— **兵种完全不变**, 只是换了驻地; 两边的在册数(DefGarrison.Placed)分别扣/加, 账目自洽。
        //   返回实际补到的人数; ledger 是"从哪座城抽了多少"的中文账目(拼进玩家可见的状态串)。
        private static int TopUpPortGarrison(Settlement port, int need, out string ledger)
        {
            ledger = "";
            try
            {
                if (port == null || need <= 0) return 0;
                var gp = DefArmy.GarrisonPartyOf(port);
                if (gp == null || gp.Party == null || gp.MemberRoster == null) return 0;
                int have = DefArmy.GarrisonMen(port);
                if (have >= need) return 0;
                int shortfall = need - have;

                var sources = new List<Settlement>();
                try
                {
                    var mp = MobileParty.MainParty;
                    if (mp != null && mp.CurrentSettlement != null) sources.Add(mp.CurrentSettlement);
                }
                catch { }
                try
                {
                    var mine = NavalBridge.PortsOf(Kingdom());
                    for (int i = 0; i < mine.Count; i++) if (mine[i] != null && mine[i] != port) sources.Add(mine[i]);
                }
                catch { }

                var sb = new StringBuilder();
                int got = 0;
                for (int i = 0; i < sources.Count && got < shortfall; i++)
                {
                    var src = sources[i];
                    if (src == null || src == port) continue;
                    int avail = DefArmy.GarrisonMen(src);
                    int keep = Math.Max(20, avail / 4);            // 每城最低留守
                    int take = Math.Min(shortfall - got, avail - keep);
                    if (take <= 0) continue;
                    var tmp = new TroopRoster(gp.Party);
                    int moved = 0;
                    try { moved = DefArmy.MoveGarrisonTroopsTo(src, tmp, take); } catch { }
                    if (moved <= 0) continue;
                    int put = 0;
                    try
                    {
                        for (int k = 0; k < tmp.Count; k++)
                        {
                            var el = tmp.GetElementCopyAtIndex(k);
                            if (el.Character == null || el.Number <= 0) continue;
                            gp.MemberRoster.AddToCounts(el.Character, el.Number, false, 0, 0, true, -1);
                            put += el.Number;
                        }
                    }
                    catch { }
                    if (put <= 0) continue;
                    got += put;
                    // 母港守备营的在册数 += put(与 MoveGarrisonTroopsTo 对源城的扣减是同一套账)
                    try
                    {
                        var recs = DefArmy.Garrisons;
                        for (int r = 0; r < recs.Count; r++)
                            if (recs[r] != null && recs[r].SettlementId == port.StringId) { recs[r].Placed += put; break; }
                    }
                    catch { }
                    if (sb.Length > 0) sb.Append("、");
                    sb.Append(NavalBridge.NameOf(src)).Append(" ").Append(put).Append(" 人");
                    DLog.Force("海军: 为组建舰队从「" + NavalBridge.NameOf(src) + "」守备营抽调 " + put + " 人到母港「" + NavalBridge.NameOf(port) + "」");
                }
                ledger = sb.ToString();
                return got;
            }
            catch (Exception ex) { DLog.Force("海军: 补足母港守备营失败 " + ex.Message); return 0; }
        }

        // v4.270: 海运页(index 4) —— 独立的"军团 / 出发港 / 目的港"切换条 + [装船出发]按钮
        [DataSourceProperty] public bool ShowTransport { get { return _tab == 4; } }
        // v4.271: 海运页底部让位给"判语带"(可出发/不可出发+原因), 不与结果行叠在同一 y
        // v4.286: 海运页(4)不显示结果行(那里有判语带); 其余四页都显示 —— 舰队页的结果行
        //   现在承载"提督与特长"(构成条从三段压成两段后, 第 3 段搬到这里, 见 ResultText 的 case 0)。
        [DataSourceProperty] public bool ShowResult { get { return _tab != 4; } }

        // v4.271: 列表是否真的有内容(空则该块整段隐藏, 让"空状态引导"顶到列头下面, 不再留 500 像素空白)
        // v4.286 契约: getter 不得抛异常(prefab 加载会读全部属性) -> 集合先判 null
        [DataSourceProperty] public bool ShowRowList { get { try { return _tab != 3 && Rows != null && Rows.Count > 0; } catch { return false; } } }
        [DataSourceProperty] public bool ShowLogList { get { try { return _tab == 3 && Logs != null && Logs.Count > 0; } catch { return false; } } }

        // v4.271: 五页签选中态 —— 选中金 + 底部金线, 未选中灰; 与军务页页签同一套配色
        [DataSourceProperty] public string TabColor0 { get { return TabColor(0); } }
        [DataSourceProperty] public string TabColor1 { get { return TabColor(1); } }
        [DataSourceProperty] public string TabColor2 { get { return TabColor(2); } }
        [DataSourceProperty] public string TabColor3 { get { return TabColor(3); } }
        [DataSourceProperty] public string TabColor4 { get { return TabColor(4); } }
        [DataSourceProperty] public bool TabLine0 { get { return _tab == 0; } }
        [DataSourceProperty] public bool TabLine1 { get { return _tab == 1; } }
        [DataSourceProperty] public bool TabLine2 { get { return _tab == 2; } }
        [DataSourceProperty] public bool TabLine3 { get { return _tab == 3; } }
        [DataSourceProperty] public bool TabLine4 { get { return _tab == 4; } }
        // v4.286 契约: getter 不得抛异常 -> 这里也套一层(它只会比较 _tab, 但契约要求"一律不抛")
        private string TabColor(int t)
        {
            try { return _tab == t ? "#E8C33AFF" : "#8A8070FF"; }
            catch { return "#8A8070FF"; }
        }

        // v4.271: 每页自己的列头(列宽与行内数值右边缘一一对应, 见 FeudalNavy.xml 注释)
        [DataSourceProperty] public bool ShowHeadFleet { get { return _tab == 0; } }
        [DataSourceProperty] public bool ShowHeadYard { get { return _tab == 1; } }
        [DataSourceProperty] public bool ShowHeadBlock { get { return _tab == 2; } }
        [DataSourceProperty] public bool ShowHeadTrans { get { return _tab == 4; } }

        // v4.271: 空状态引导(列表为空时给一句"下一步该去哪")
        [DataSourceProperty] public string EmptyFleet { get { return _emptyFleet; } }
        [DataSourceProperty] public string EmptyYard { get { return _emptyYard; } }
        [DataSourceProperty] public string EmptyBlock { get { return _emptyBlock; } }
        [DataSourceProperty] public string EmptyTrans { get { return _emptyTrans; } }
        [DataSourceProperty] public string EmptyLogs { get { return _emptyLogs; } }
        [DataSourceProperty] public bool ShowEmptyFleet { get { return _tab == 0 && _emptyFleet.Length > 0; } }
        [DataSourceProperty] public bool ShowEmptyYard { get { return _tab == 1 && _emptyYard.Length > 0; } }
        [DataSourceProperty] public bool ShowEmptyBlock { get { return _tab == 2 && _emptyBlock.Length > 0; } }
        [DataSourceProperty] public bool ShowEmptyTrans { get { return _tab == 4 && _emptyTrans.Length > 0; } }
        [DataSourceProperty] public bool ShowEmptyLogs { get { return _tab == 3 && _emptyLogs.Length > 0; } }

        // 切换条上的当前目标名(造船页 = 造船港口; 封锁页 = 敌国港口)
        [DataSourceProperty] public string PickerText
        {
            get
            {
                try
                {
                    var k = Kingdom();
                    if (k == null || !NavalBridge.Usable) return "海军未启用";
                    if (_tab == 1)
                    {
                        var p = SelectedPort(k);
                        // v4.286: 控件宽 288 像素(右邻 ▶ 在 348) -> 港口名过长时截断
                        return NavyRowVM.Clip(p == null ? "我方没有可用港口" : ("造船港口: " + NavalBridge.NameOf(p)), 288f, 17f);
                    }
                    if (_tab == 2)
                    {
                        var ports = NavalBridge.EnemyPorts(k);
                        if (ports.Count == 0) return "没有可封锁的敌国港口";
                        if (_enemyPortIdx < 0 || _enemyPortIdx >= ports.Count) _enemyPortIdx = 0;
                        return NavyRowVM.Clip("封锁目标: " + NavalBridge.NameOf(ports[_enemyPortIdx]), 288f, 17f);
                    }
                    return "—";
                }
                catch { return "—"; }
            }
        }

        // v4.271: 切换条右侧的说明(一句话讲清"现在能干什么/为什么不能")
        [DataSourceProperty] public string PickerHint
        {
            get
            {
                try
                {
                    var k = Kingdom();
                    if (k == null || !NavalBridge.Usable) return "";
                    if (_tab == 1)
                    {
                        var p = SelectedPort(k);
                        if (p == null) return "先占领一处沿海城镇, 或研究海军科技解锁港口建筑";
                        return "造船厂 " + NavalBridge.ShipyardLevel(p) + " 级 · 1 级轻/2 级中/3 级重 · 金色=可造";
                    }
                    if (_tab == 2)
                    {
                        var ports = NavalBridge.EnemyPorts(k);
                        if (ports.Count == 0) return "需与拥有沿海城镇的国家交战";
                        var p = ports[_enemyPortIdx >= 0 && _enemyPortIdx < ports.Count ? _enemyPortIdx : 0];
                        var owner = NavalBridge.KingdomOf(p);
                        // v4.273(审计 #2): 这里要的是**敌国**舰队 -> ShipsOf(owner), 原来 Ships(owner) 拿到的是我方
                        return (owner != null && owner.Name != null ? owner.Name.ToString() : "?")
                             + " · 敌方舰队 " + NavalFleet.ShipsOf(owner).Count + " 艘 · 造船厂 "
                             + NavalBridge.ShipyardLevel(p) + " 级";
                    }
                    return "";
                }
                catch { return ""; }
            }
        }

        [DataSourceProperty] public string Title { get { return "海军"; } }

        // ==================== v4.282: 页签下方的"本页状态行"(两行, 16号字) ====================
        //   位置: 控制条(y356)之下、列头(y458)之上 —— 页签选"看哪一页", 控制条选"看哪一处",
        //   状态行回答"现在这一处是什么情况"。关键数值(战力/维护/载员/收益)都在这一行里**带色**显示,
        //   不再塞进控制条右侧的小字(FleetBarHint 那一栏原来只有 13 号字, 玩家看不清)。
        [DataSourceProperty] public string TabStatusText
        {
            get
            {
                try
                {
                    if (!NavalBridge.Usable) return "未检测到海战 DLC —— 海军功能整体停用(所有按钮给出提示, 不会崩)";
                    var k = Kingdom();
                    if (k == null) return "尚未接管王国 —— 海军功能在接管王国后可用";
                    switch (_tab)
                    {
                        case 0:
                            {
                                var list = Fleets();
                                if (list.Count == 0)
                                    return "还没有舰队 —— 点[组建舰队]选一处自有港口, 再从氏族可支配的船里挑船\n"
                                         + "编成后它是**地图上的实体部队**: 左键点它即可指挥, 也能停进沿海城市的泊位";
                                if (_fleetIdx < 0 || _fleetIdx >= list.Count) _fleetIdx = 0;
                                var f = list[_fleetIdx];
                                var p = NavalTaskForce.PartyOf(f);
                                if (p == null) return "舰队不在场(可能已被歼灭)";
                                int ships, crew, sailors;
                                float power, ratio, morale, upkeep, wage;
                                bool moored;
                                NavalTaskForce.Stats(p, out ships, out power, out ratio, out crew, out sailors,
                                    out morale, out upkeep, out wage, out moored);
                                string cap = "(" + (_fleetIdx + 1) + "/" + list.Count + ") ";
                                return cap + "本队舰 " + ships + " 艘 · 战力 " + (int)power + " · 完整度 "
                                     + (int)Math.Round(ratio * 100f) + "% · 水手 " + sailors + "/" + crew
                                     + " · 士气 " + (int)morale + " · 上限 " + NavalTaskForce.MaxForces + " 支\n"
                                     + "日耗 船维护 " + upkeep.ToString("0.#") + (moored ? "(驻泊 -35%)" : "")
                                     + " + 水手军饷 " + wage.ToString("0.#") + " = " + (upkeep + wage).ToString("0.#")
                                     + " 第纳尔/日 · " + NavalTaskForce.MoorStateText(f);
                            }
                        case 1:
                            {
                                var pc = NavalBridge.PortsOf(k);
                                var p = SelectedPort(k);
                                if (p == null) return "我方没有可用港口 —— 先占领一处沿海城镇, 或研究海军科技 navigation 解锁港口建筑\n"
                                    + "点[选择城市]可以在地图上直接点选目标港口(◀ ▶ 仍然可用)";
                                int yard = NavalBridge.ShipyardLevel(p);
                                int can = 0, locked = 0, stock = 0;
                                try
                                {
                                    var offers = NavalFleet.Offers(p, k);
                                    for (int i = 0; i < offers.Count; i++)
                                    {
                                        if (offers[i] == null) continue;
                                        if (offers[i].Buildable) { can++; if (offers[i].Days == 0) stock++; }
                                        else locked++;
                                    }
                                }
                                catch { }
                                return "造船厂 " + yard + " 级(1 级轻/2 级中/3 级重) · 本港在售 " + stock + " 型 · 本港可造 "
                                     + can + " 型 · 未解锁 " + locked + " 型 · 我方港口共 " + pc.Count + " 处\n"
                                     + "国库 " + EconomyWorld.Treasury.Gold + " 第纳尔 · 下单比现货便宜 10%, 但要等工期";
                            }
                        case 2:
                            {
                                string st = NavalFleet.IsBlockading
                                    ? "★正在封锁: 「" + NavalFleet.BlockadePortName + "」(第 "
                                      + Math.Max(0, (int)CampaignTime.Now.ToDays - NavalFleet.BlockadeSinceDay)
                                      + " 天) · 累计战果 " + (int)NavalFleet.BlockadeDamage + " 第纳尔"
                                    : "当前未实施封锁 —— 选一处敌国港口后点行内[封锁]开始";
                                return st + "\n昨日封锁: 我方收益 " + (int)NavalFleet.LastBlockadeGain + " / 对方损失 "
                                     + (int)NavalFleet.LastBlockadeLoss + " · " + NavalPolicy.OneLine(k);
                            }
                        case 4:
                            {
                                var lg = SelectedLegion();
                                var from = SelectedFromPort(k);
                                var to = SelectedToPort(k);
                                string l1 = "① 军团 " + (lg != null ? NavalTransport.LegionLabel(lg) + "(" + NavalTransport.LegionMenOf(lg) + "人)" : "无")
                                          + " · ② 出发 " + (from != null ? NavalBridge.NameOf(from) : "无")
                                          + " · ③ 目的 " + (to != null ? NavalBridge.NameOf(to) : "无");
                                if (lg == null || from == null || to == null)
                                    return l1 + "\n三步都要先选好 —— 军团用◀▶, 港口可以点[选城]在地图上直接点";
                                int need, cap, eta, cost, ships;
                                float safe;
                                string why = NavalTransport.Precheck(lg, from, to, k, out need, out cap, out eta, out cost, out ships, out safe);
                                return l1 + "\n我方运力 " + cap + "/" + need + " 人 · 我方用船 " + ships + " 艘 · 航程约 " + eta
                                     + " 天 · 补给费 " + cost + " 第纳尔" + (string.IsNullOrEmpty(why) ? " · 可以出发" : " · 尚不可出发: " + why);
                            }
                        default:
                            {
                                string head = "最近 " + NavalFleet.Battles.Count + " 场海战记录 · 滚轮翻页"
                                            + " · 【实战】=地图上真打的海战, 【推演】=面板[出击]的抽象结算";
                                if (!string.IsNullOrEmpty(_status)) return head;
                                return head + "\n" + NavalTech.Summary(k);
                            }
                    }
                }
                catch (Exception ex) { return "读取失败: " + ex.Message; }
            }
        }

        [DataSourceProperty] public string TabStatusColor
        {
            get
            {
                try
                {
                    switch (_tab)
                    {
                        case 0:
                            {
                                var list = Fleets();
                                if (list.Count == 0) return "#8A8070FF";
                                if (_fleetIdx < 0 || _fleetIdx >= list.Count) _fleetIdx = 0;
                                return NavalTaskForce.InBerth(list[_fleetIdx]) ? "#E9CD74FF" : "#D8C9A0FF";
                            }
                        case 1: return "#D8C9A0FF";
                        case 2: return NavalFleet.IsBlockading ? "#7FD8A0FF" : "#D8C9A0FF";
                        case 4:
                            {
                                var k = Kingdom();
                                if (k == null) return "#8A8070FF";
                                var lg = SelectedLegion(); var from = SelectedFromPort(k); var to = SelectedToPort(k);
                                if (lg == null || from == null || to == null) return "#C96A5AFF";
                                int need, cap, eta, cost, ships; float safe;
                                string why = NavalTransport.Precheck(lg, from, to, k, out need, out cap, out eta, out cost, out ships, out safe);
                                return string.IsNullOrEmpty(why) ? "#7FD8A0FF" : "#C96A5AFF";
                            }
                        default: return "#8FD8E8FF";
                    }
                }
                catch { return "#D8C9A0FF"; }
            }
        }

        // ==================== v4.286(用户质疑: "我整个国家不可能 29 处港口"): 口径显式化 ====================
        //   【原缺陷】标题栏那一行直接返回 NavalBridge.StatusText(), 而它写的是
        //     "海战 DLC 已接入 · 港口 " + NavalBridge.AllPorts().Count + " 处"
        //     —— AllPorts() 是**全图**所有有港口的城镇(见 NavalBridge.cs:106 的遍历 Settlement.All),
        //     本意是"系统已就绪(能认到港口)", 但文案没写"全图", 读起来就像在说玩家自己的国家,
        //     于是与造船页那句"自有港口 2 处"(NavalBridge.PortsOf(k).Count)自相矛盾, 玩家一眼看出不对。
        //   【现在】在后面补上**我方**口径。取数函数一个都不新写:
        //     全图 = NavalBridge.AllPorts().Count(就是 StatusText 里那一个, 保留它的原话)
        //     我方 = NavalBridge.PortsOf(k).Count(与造船页 HelpLine / 舰队页 Header / 海运页 ComputeSteps
        //            / NavalFleet.FleetCap 的 portCap 完全同一个函数, 不另算一套)
        //   未接管王国 / DLC 缺失时 k == null -> 只显示全图那半句(不硬编 0, 免得像"你有 0 个港口")。
        [DataSourceProperty] public string StatusLine
        {
            get
            {
                try
                {
                    if (!string.IsNullOrEmpty(_status)) return _status;
                    string baseText = NavalBridge.StatusText();
                    var k = Kingdom();
                    if (k == null) return baseText;
                    int mine = NavalBridge.PortsOf(k).Count;
                    return baseText + " · 我方 " + mine + " 处";
                }
                catch { return ""; }
            }
        }

        // ==================== v4.284(第 1 项): 页头压成一行, 顶部让位给"资源总览条" ====================
        //   原来 @Header 占 y88 h56(两行 16 号字); 现在同一块地方要放**六个关键数字**(国库/舰队/舰船/
        //   水手/日耗/巡逻队, 见 Ov*), 所以页头缩到 y130 h18 的单行(15 号), 文本一字不改 ——
        //   只在出口把两行之间的 "\n" 换成 " · ", 否则第二行会被控件裁掉(ClipContents)。
        [DataSourceProperty] public string Header
        {
            get
            {
                try { return OneLine(FlatHeader(HeaderRaw())); }   // v4.286: 控件 800 像素 -> 超长截断
                catch { return ""; }
            }
        }

        private static string FlatHeader(string s)
        {
            try { return string.IsNullOrEmpty(s) ? "" : s.Replace("\n", " · "); }
            catch { return s; }
        }

        private string HeaderRaw()
        {
            try
            {
                {
                    if (!NavalBridge.Usable) return "海军功能未启用 —— 需要《骑马与砍杀2》海战 DLC(War Sails / NavalDLC)";
                    var k = Kingdom();
                    if (k == null) return "尚未接管任何王国 —— 海军功能在接管王国后可用";
                    switch (_tab)
                    {
                        case 0:
                            {
                                int n = NavalFleet.MyShips().Count;   // v4.273(审计 #2): 我方口径显式化
                                var pc = NavalBridge.PortsOf(k);
                                int yard = 0;
                                for (int i = 0; i < pc.Count; i++) if (NavalBridge.ShipyardLevel(pc[i]) > 0) yard++;
                                return "我方舰队 " + n + "/" + NavalFleet.FleetCap(k) + " 艘 · 我方日维护 "
                                     + (int)Math.Round(NavalFleet.DailyUpkeep(k)) + " 第纳尔 · 我方总战力 "
                                     + (int)NavalFleet.FleetPower(k) + "\n我方港口 " + pc.Count + " 处(含造船厂 "
                                     + yard + " 处) · 海运容量 " + (int)NavalFleet.CargoCapacity(k) + " 人(我方)";
                            }
                        case 1:
                            {
                                var p = SelectedPort(k);
                                if (p == null) return "我方没有可用港口 —— 先占领沿海城镇, 或研究 navigation 解锁港口建筑\n(海军科技 · 军事树 navigation → admiralty → drydocks)";
                                return "造船港口(我方): " + NavalBridge.NameOf(p) + " · 造船厂 " + NavalBridge.ShipyardLevel(p)
                                     + " 级 · 我方在建 " + NavalFleet.Orders.Count + "/4 艘\n国库 "
                                     + EconomyWorld.Treasury.Gold + " 第纳尔 · 下单比现货便宜 10%, 但要等工期";
                            }
                        case 2:
                            {
                                if (!NavalFleet.IsBlockading)
                                    return "当前未实施封锁 · 选择一处敌国港口开始封锁\n需与该国交战, 且我方有舰船(先到「造船」页备船)";
                                return "正在封锁: " + NavalFleet.BlockadeKingdomName + " 的 " + NavalFleet.BlockadePortName
                                     + " · 累计战果 " + (int)NavalFleet.BlockadeDamage + " 第纳尔\n昨日: 我方收益 "
                                     + (int)NavalFleet.LastBlockadeGain + " / 对方损失 " + (int)NavalFleet.LastBlockadeLoss;
                            }
                        case 4:
                            {
                                // v4.270: 海运页头 —— 选中组合的预检结果(运力/航程/费用) + 在途运输队
                                var lg = SelectedLegion();
                                var from = SelectedFromPort(k);
                                var to = SelectedToPort(k);
                                if (lg == null || from == null || to == null)
                                    return "海运 —— 把国防军军团装船, 走海路运到另一处港口\n依次选: ① 军团 → ② 出发港 → ③ 目的港, 再点底部[装船出发]";
                                int need, cap, eta, cost, ships;
                                float safe;
                                string why = NavalTransport.Precheck(lg, from, to, k, out need, out cap, out eta, out cost, out ships, out safe);
                                string head = "海运: " + NavalTransport.LegionLabel(lg) + " 由 " + NavalBridge.NameOf(from)
                                    + " → " + NavalBridge.NameOf(to);
                                // v4.273(A-1/B-3): 头部也带上"可安全航行 N 天"与"风暴延误", 玩家不必先点出发才看到
                                string ext = "";
                                if (safe >= 0f) ext += " · 船队可安全航行 " + safe.ToString("0.#") + " 天";
                                if (eta > 0)
                                {
                                    int sd = NavalSeaRisk.StormDelayDays(from.PortPosition.ToVec2(), to.PortPosition.ToVec2(), eta);
                                    if (sd > 0) ext += " · 风暴延误 " + sd + " 天";
                                }
                                if (!string.IsNullOrEmpty(why)) return head + "\n尚不可出发: " + why + ext;
                                return head + " · 我方运力 " + cap + "/" + need + " 人 · 我方用船 " + ships + " 艘 · 航程约 " + eta
                                    + " 天 · 费用 " + cost + " 第纳尔\n我方在途 " + NavalTransport.Active.Count + "/" + NavalTransport.MaxConvoys
                                    + " 支 · 到达目的港自动下船并还船" + ext;
                            }
                        default:
                            {
                                var kk = Kingdom();
                                return NavalTech.Summary(kk) + "\n最近 " + NavalFleet.Battles.Count + " 场海战记录 · 滚轮翻页";
                            }
                    }
                }
            }
            catch (Exception ex) { return "读取失败: " + ex.Message; }
        }

        // ==================== v4.284(第 1 项 ★): 顶部"资源总览条"(仿陆军页的概览卡) ====================
        //   位置: 标题栏(14..76)之下、页头(y130)之上 —— 与原 @Header 抢同一块地方, 所以页头已压成一行。
        //   ★ 每个数字都取自**已有的只读函数**(不另算一套口径, 否则会与其它页面显示不一致):
        //     国库   = EconomyWorld.Treasury.Gold            (与陆军页 @TreasuryText / 造船页状态行同源)
        //     舰队   = NavalTaskForce.Alive().Count          (健在舰队; 与指挥条 (i/N) 同一份列表)
        //     舰船   = NavalFleet.MyShips().Count            (= NavalBridge.MyShips(), 我方口径)
        //     水手   = NavalTaskForce.NavalSailorsTotal()    (与"水手军饷"的计算口径同一函数)
        //     日耗   = AllFleetCost(out up, out wage)        (= Σ Stats 的 upkeep + wage, 与日结扣款同源)
        //     巡逻队 = NavalFleet.PatrolParties              (在航巡逻队; 由 NavalPatrol 每日写入)
        //   颜色沿用本页既有配色: 金=关键/绿=正向/米白=人/青=海(舰船蓝与日耗橙写在 prefab 的固定色里)。
        private const string OvGold = "#E8C33AFF", OvGreen = "#7FBF6AFF";
        private const string OvCream = "#E8DCC0FF", OvCyan = "#8FD8E8FF";

        [DataSourceProperty] public string OvTreasury
        {
            get { try { return EconomyWorld.Treasury.Gold.ToString(); } catch { return "—"; } }
        }

        // 国库染色与陆军页同一判据(够花=绿 / 偏紧=黄 / 见底=红) —— 只影响颜色, 不参与任何结算
        [DataSourceProperty] public string OvTreasuryColor
        {
            get
            {
                try
                {
                    int g = EconomyWorld.Treasury.Gold;
                    if (g >= 5000) return OvGreen;
                    if (g >= 1000) return "#D6D06AFF";
                    return "#D96A5AFF";
                }
                catch { return OvCream; }
            }
        }

        [DataSourceProperty] public string OvFleets
        {
            get { try { return Fleets().Count.ToString(); } catch { return "—"; } }
        }

        [DataSourceProperty] public string OvShips
        {
            get { try { return NavalFleet.MyShips().Count.ToString(); } catch { return "—"; } }
        }

        [DataSourceProperty] public string OvSailors
        {
            get { try { return NavalTaskForce.NavalSailorsTotal().ToString(); } catch { return "—"; } }
        }

        [DataSourceProperty] public string OvCost
        {
            get
            {
                try
                {
                    int up, wage;
                    AllFleetCost(out up, out wage);      // 与舰队页汇总行/HelpLine/日结**同一份数据**
                    return (up + wage).ToString();
                }
                catch { return "—"; }
            }
        }

        [DataSourceProperty] public string OvPatrol
        {
            get { try { return NavalFleet.PatrolParties.ToString(); } catch { return "—"; } }
        }

        // 巡逻队为 0 时给灰色(避免"0"看起来像故障); 有巡逻队在航时用青色
        [DataSourceProperty] public string OvPatrolColor
        {
            get { try { return NavalFleet.PatrolParties > 0 ? OvCyan : "#8A8070FF"; } catch { return "#8A8070FF"; } }
        }

        // ==================== v4.284(第 2 项 ★): 选中舰队的"构成"明细 ====================
        //   呈现方式(三选一里的哪一种, 以及为什么):
        //     选 **"选中某支舰队 -> 在数据区下方常显一条三段式构成条"**(y858 h52, 舰队页专属),
        //     不选"行内展开"也不选"第二个列表", 理由三条:
        //       ① 与陆军"选中军团看兵种构成"的交互对齐 —— 控制条 ◀▶ 选中谁, 这条就显示谁的构成;
        //       ② 舰队页的数据区(y472..856)已经被"逐船一行 + 日耗汇总行"占满(每页 4 行), 再插一个
        //          84 像素高的兵种卡片会把列表压到 2 行; 而行内展开会挤掉每行已有的两行说明
        //          (完整度/修理费/升级件, 见 BuildFleetRows 的 note);
        //       ③ 纯显示、**不新增任何热区** —— 本项目最易错的就是"prefab 元素 ↔ 面板坐标 ↔ AddSpot",
        //          能不动的坐标一律不动(本次只增 5 枚按钮热区, 见 NavyPanel.RegisterSpots)。
        //   三段的信息来源(全部走已有只读函数, 与状态行/日结同源):
        //     ① 舰种构成 = NavalTaskForce.HullBreakdown(按 NavalBridge.HullType 分轻/中/重统计)
        //     ② 水手/士气 = NavalTaskForce.Stats(crew/sailors/morale/ratio/wage, 与 TabStatusText 同一函数)
        //     ③ 提督/特长 = NavalAdmiral.Of(null) + ActivePerkNames(Hero.MainHero)
        //        (★ 海军乘数的**真实落点**全部是 Hero.MainHero —— 见 NavalFleet:210/255、NavalBattle:91、
        //         NavalTransport:326, 所以这里显示的"提督"就是实际生效的那一位, 不是摆设)
        //     ④ 升级件 = Ship.GetShipSlotAndPieceNames 逐船求和(与舰队页行内 PieceNames 同一 API)
        private NavalTaskForce.Force SelectedForce()
        {
            try
            {
                var list = Fleets();
                if (list.Count == 0) return null;
                if (_fleetIdx < 0 || _fleetIdx >= list.Count) _fleetIdx = 0;
                return list[_fleetIdx];
            }
            catch { return null; }
        }

        [DataSourceProperty] public bool ShowCompose
        {
            get { try { return _tab == 0 && Fleets().Count > 0; } catch { return false; } }
        }

        // 第 1 段: 舰种构成(轻/中/重各几艘) + 该舰队的载员/战力/日维护合计
        [DataSourceProperty] public string ComposeText1
        {
            get
            {
                try
                {
                    var f = SelectedForce();
                    if (f == null) return "";
                    var p = NavalTaskForce.PartyOf(f);
                    if (p == null) return "选中舰队不在场(可能已被歼灭) —— 用 ◀ ▶ 换一支";
                    var cnt = new int[3]; var crew = new int[3]; var pow = new float[3]; var upk = new float[3];
                    NavalTaskForce.HullBreakdown(p, cnt, crew, pow, upk);
                    int cap = crew[0] + crew[1] + crew[2];
                    int power = (int)(pow[0] + pow[1] + pow[2]);
                    float upkeep = upk[0] + upk[1] + upk[2];
                    bool moored = false;
                    try { moored = NavalTaskForce.InBerth(f); } catch { }
                    // v4.286: 控件宽 508 像素(构成条右端只到面板 x550, 让开右侧两枚按钮),
                    //   所以这段压成"构成 + 载员 + 战力"; 维护/日 在状态行与顶栏 @OvCost 都有, 不重复。
                    //   末尾再过一道 Clip: 万一船数变成两位数(轻 12/中 10/重 8)也不会画出控件。
                    // v4.286c: 构成条压成**一行**(控件 544 像素) -> 只留最要紧的四个数:
                    //   舰种构成 / 载员上限 / 战力 / 水手(缺员在这里明说)。士气·完整度·日耗在状态行与顶栏。
                    int sail;
                    try { sail = DefArmy.RegularsOf(p); } catch { sail = 0; }
                    int lack = cap - sail;
                    return NavyRowVM.Clip("构成 轻" + cnt[0] + " 中" + cnt[1] + " 重" + cnt[2]
                         + " · 载员 " + cap + " · 战力 " + power
                         + " · 水手 " + sail + (lack > 0 ? "(缺 " + lack + ")" : "")
                         + (moored ? " · 已驻泊" : ""), 544f, 17f);
                }
                catch { return ""; }
            }
        }

        // 第 2 段: 水手来源与状态(水手数 / 载员上限 / 缺员 / 日军饷 / 士气 / 完整度)
        [DataSourceProperty] public string ComposeText2
        {
            get
            {
                try
                {
                    var f = SelectedForce();
                    if (f == null) return "";
                    var p = NavalTaskForce.PartyOf(f);
                    if (p == null) return "";
                    int ships, crew, sailors;
                    float power, ratio, morale, upkeep, wage;
                    bool moored;
                    NavalTaskForce.Stats(p, out ships, out power, out ratio, out crew, out sailors,
                        out morale, out upkeep, out wage, out moored);
                    int missing = crew - sailors;
                    string mo = missing > 0 ? ("缺 " + missing + " 人") : (missing < 0 ? ("超载 " + (-missing) + " 人") : "满员");
                    return "水手 " + sailors + "/" + crew + "(" + mo + ") · 军饷 " + wage.ToString("0.#")
                         + "/日 · 士气 " + (int)morale + " · 完整度 " + (int)Math.Round(ratio * 100f) + "%";
                }
                catch { return ""; }
            }
        }

        // ==================== v4.286: 原"构成条第 3 段" -> 舰队页的**操作结果行** ====================
        //   17 号字一行 20.4 像素, 构成条只给得起两行(y836 h52), 所以"提督技能/特长/升级件"这一段
        //   搬到同页的结果行(y896, SuggestedHeight 26 = 单行)。
        //   它已不再是数据源属性(prefab 里已无 @ComposeText3 绑定 —— 属性被删掉后, 旧版 prefab 会绑定失败),
        private string ComposeText3OrHint()
        {
            try
            {
                var f = SelectedForce();
                if (f == null) return "提示: [进驻城市]把我方舰队停进自有沿海城市泊位 · [离港]恢复行动";
                var p = NavalTaskForce.PartyOf(f);
                if (p == null) return "";
                var prof = NavalAdmiral.Of(null);      // 提督 = 玩家本人(所有乘数的落点都是 MainHero)
                string nm = string.IsNullOrEmpty(prof.Source) ? "?" : prof.Source;
                string perk;
                if (!prof.Available) perk = "无海军技能/特长(×1.00)";
                else
                {
                    var names = NavalAdmiral.ActivePerkNames(Hero.MainHero);
                    if (names.Count == 0) perk = "特长未点";
                    else perk = names[0] + (names.Count > 1 ? "、" + names[1] : "")
                              + (names.Count > 2 ? "(共 " + names.Count + " 项)" : "");
                }
                int pieces = 0;
                var ships = NavalBridge.ShipsOf(p);
                for (int i = 0; i < ships.Count; i++) pieces += PieceCount(ships[i]);
                return "提督 " + nm + "(水手·长·船 " + prof.Mariner + "/" + prof.Boatswain + "/" + prof.Shipmaster
                     + ") · 特长 " + perk + " · 我方升级件 " + pieces + " 件";
            }
            catch { return ""; }
        }

        // ==================== v4.284(第 3 项 ★): 批量操作(底栏三枚, 仿陆军页[全军回防]/[全军交还总监]) ====================
        //   三枚都走 NavalTaskForce 里**已有的业务口径**(召回 OrderRecall / 交还 ReleaseAllToAi / 进驻 Moor),
        //   面板只负责"报结果 + 立刻重算热区"。颜色按"当前是否有可执行对象"灰化(灰 = 点了也只会给你原因),
        //   且**灰了也保留热区**: 点下去必定有中文提示, 不会"点了没反应"(用户明确要求)。
        internal void RecallAllFleets()
        {
            try { _status = NavalTaskForce.RecallAllToHome(); Touch(); }
            catch (Exception ex) { _status = "全部召回失败: " + ex.Message; Touch(); }
        }

        internal void ReleaseAllFleets()
        {
            try { _status = NavalTaskForce.ReleaseAllToAi(); Touch(); }
            catch (Exception ex) { _status = "全部交还失败: " + ex.Message; Touch(); }
        }

        internal void MoorAllFleets()
        {
            try { _status = NavalTaskForce.MoorAllHomePorts(); Touch(); }
            catch (Exception ex) { _status = "全部进驻失败: " + ex.Message; Touch(); }
        }

        // 有舰队可召回(在场)
        [DataSourceProperty] public string RecallAllColor
        {
            get { try { return Fleets().Count > 0 ? OvGreen : "#8A8070FF"; } catch { return "#8A8070FF"; } }
        }

        // 有"还在玩家手里"的舰队才亮(全都自主巡航了就灰)
        [DataSourceProperty] public string ReleaseAllColor
        {
            get
            {
                try
                {
                    var list = Fleets();
                    for (int i = 0; i < list.Count; i++) if (list[i] != null && list[i].PlayerHold) return OvCyan;
                    return "#8A8070FF";
                }
                catch { return "#8A8070FF"; }
            }
        }

        // 有"母港有效 + 尚未驻泊"的舰队才亮
        [DataSourceProperty] public string MoorAllColor
        {
            get
            {
                try
                {
                    var list = Fleets();
                    for (int i = 0; i < list.Count; i++)
                    {
                        var f = list[i];
                        if (f == null) continue;
                        if (NavalTaskForce.InBerth(f)) continue;
                        var home = NavalTaskForce.FindSettlement(f.HomePortId);
                        if (home != null && home.HasPort) return OvGold;
                    }
                    return "#8A8070FF";
                }
                catch { return "#8A8070FF"; }
            }
        }

        // ==================== v4.284(第 4 项 ★): 花钱换属性(舰队页两枚, 仿陆军[阅兵 1500]) ====================
        //   [海上演训]: 提升**舰队士气**(真实属性, 见 NavalTaskForce.SeaDrill 的长注释) —— 费用随水手数走,
        //     按钮上直接写出要花多少(用户要求"费用可见"), 钱不够时按钮变红且点击给中文原因。
        //   [整备]: 一次性把我方全部舰船修满, 报价 = 逐船 DLC 修船价(ShipCostModel.GetShipRepairCost)之和。
        internal void SeaDrill()
        {
            try { _status = NavalTaskForce.SeaDrill(); Touch(); }
            catch (Exception ex) { _status = "演训失败: " + ex.Message; Touch(); }
        }

        internal void OverhaulAll()
        {
            try { _status = NavalTaskForce.OverhaulAll(); Touch(); }
            catch (Exception ex) { _status = "整备失败: " + ex.Message; Touch(); }
        }

        [DataSourceProperty] public string DrillText
        {
            get
            {
                try
                {
                    int c = NavalTaskForce.SeaDrillCost();
                    return c > 0 ? ("海上演训 " + c) : "海上演训";
                }
                catch { return "海上演训"; }
            }
        }

        [DataSourceProperty] public string DrillColor
        {
            get
            {
                try
                {
                    int c = NavalTaskForce.SeaDrillCost();
                    if (c <= 0) return "#8A8070FF";                       // 没有水手 -> 灰
                    return EconomyWorld.Treasury.Gold >= c ? OvGold : "#C96A5AFF";   // 钱不够 -> 红
                }
                catch { return OvGold; }
            }
        }

        [DataSourceProperty] public string OverhaulText
        {
            get
            {
                try
                {
                    int n, cost;
                    NavalTaskForce.OverhaulQuote(out n, out cost);
                    if (n <= 0) return "整备 · 全好";
                    return "整备 " + cost;
                }
                catch { return "整备"; }
            }
        }

        [DataSourceProperty] public string OverhaulColor
        {
            get
            {
                try
                {
                    int n, cost;
                    NavalTaskForce.OverhaulQuote(out n, out cost);
                    if (n <= 0) return "#8A8070FF";                        // 无需整备 -> 灰
                    return EconomyWorld.Treasury.Gold >= cost ? OvCyan : "#C96A5AFF";
                }
                catch { return OvCyan; }
            }
        }

        // ==================== v4.273(审计 #7): A10 海上巡逻加成的"玩家可见性" ====================
        //   背景(Navy\NavalPatrol.cs:35-49 的硬前提): DLC 的海上巡逻队**不是无条件生成的** ——
        //   NavalSettlementPatrolModel.CanSettlementHavePatrolParties 要求
        //   `OwnerClan.Kingdom.HasPolicy(NavalPolicies.CoastalGuardEdict)`(王国通过「海岸卫队」法令)。
        //   玩家王国若没通过该法令, 在航巡逻队 **恒为 0** ⇒ 那 4 项科技
        //   (floating_harbor / jeune_ecole / aircraft_carrier / battlefleet_tactics)的"巡逻收益 +X%"
        //   完全是零效果。原实现只在 NavalPatrol 的代码注释里写了"面板必须提示", 面板上**一个字都没有**。
        //   这里补上: 已研究巡逻类科技却 0 支巡逻队 -> 明确告诉他差的是**法令**;
        //   正常有巡逻队 -> 报数量与当前系数。
        private static readonly string[] PatrolTechIds =
        {
            "floating_harbor", "jeune_ecole", "aircraft_carrier", "battlefleet_tactics"
        };

        // v4.286: @PatrolHint(两行版)已删除 —— 它原先是页脚行的一部分, 而页脚现在是**单行**控件
        //   (见 FootLine 的注释)。巡逻加成的完整说明移到 HelpLine(第 3 行, 见 PatrolHelpLine),
        //   页脚只留一句单行的 "我方在航巡逻队 N 支"(见 PatrolHintOneLine)。
        private const int PatrolLineMax = 46;   // 单行容量(800 像素 / 17 号约 47 个全角字, 留一格余量)

        // 已研究的科技里有没有巡逻类那 4 项(只读 Research 的既有接口, 不新增状态)
        private static bool HasPatrolTech(Kingdom k)
        {
            try
            {
                for (int i = 0; i < PatrolTechIds.Length; i++)
                    if (Research.IsDone(k, PatrolTechIds[i])) return true;   // 按 k 查(不是"玩家国"隐式口径)
            }
            catch { }
            return false;
        }

        // 巡逻系数: 取"日结已刷新值(NavalFleet.PatrolMul)"与"当前科技实时值"的较大者 ——
        //   日结每天才刷一次, 玩家刚研究完科技时前者还是 1.00, 显示实时值才不会让玩家以为没生效。
        private static string PatrolMulText()
        {
            try
            {
                float v = NavalFleet.PatrolMul;
                float live = NavalPatrol.CurrentMul;
                if (live > v) v = live;
                if (v < 1f) v = 1f;
                return v.ToString("0.00");
            }
            catch { return "1.00"; }
        }

        // ==================== v4.273: 舰队页第二行的三段信息(A-2/A-3/B-1) ====================

        // A-2: 舰队上限的来源拆解 —— "上限为什么会变大"必须让玩家看得见, 否则船坞投资是黑箱
        private static string FleetCapLine(Kingdom k)
        {
            try
            {
                int b, tech, port, yard;
                NavalFleet.FleetCapParts(k, out b, out tech, out port, out yard);
                return "舰队上限 " + NavalFleet.FleetCap(k) + " 艘 = 基础 " + b + " + 科技 " + tech
                     + " + 港口 " + port + " + 船坞建筑 " + yard;
            }
            catch { return ""; }
        }

        // ==================== v4.284(第 1/2/4 项): A-3/B-1 这几段文案的去向 ====================
        //   原来 FleetRepairLine(修船概况)/FleetCostLine(日耗拆解)/MoorRepairLine(进驻收益) 都挤在舰队页
        //   HelpLine 的第 2/3 行: 三段合计 150+ 字, 而 15 号字一行只放得下约 53 字 —— 尾部其实一直被
        //   ClipContents 裁掉(玩家看不到"船匠之手"/"昨日修复"这些句子)。本次把这批数字搬到**能完整显示**
        //   的地方之后, 三个方法已无调用者, 直接删除(不留死代码):
        //     · 日耗(船维护 + 水手军饷)     -> 顶部总览条 @OvCost / 舰队页状态行 / 构成条第 2 段
        //     · 修船概况(需修几艘·约多少钱) -> HelpLine 第 3 行(现算现拼, 与 NavalBridge.RepairSummary 同口径)
        //     · 进驻收益 + 昨日修复         -> HelpLine 第 3 行 + [整备] 按钮(费用直接写在按钮文字上)
        //   下面的 UpgradeLine 接回 HelpLine 第 3 行(它够短, 且正好补上"升级件装在几艘上"这个全局口径)。
        //   它只返回**以 " · " 开头的尾段**(由调用方拼), 不含前导分隔符时返回空串。

        // B-1: 单船升级件概览 —— 舰船页每行已有"完整度/载货", 这里再给一句全舰队的升级件合计,
        //   让"升级件确实装着、而且属性已经算进 耐久/载员/载重 三列"这件事看得见。
        private static string UpgradeLine()
        {
            try
            {
                var ships = NavalFleet.MyShips();
                if (ships.Count == 0) return "";
                int total = 0, shipsWith = 0;
                for (int i = 0; i < ships.Count; i++)
                {
                    int c = PieceCount(ships[i]);
                    if (c > 0) { shipsWith++; total += c; }
                }
                if (total == 0) return " · 我方尚无舰船装配升级件";
                return " · 我方升级件 " + total + " 件(装在 " + shipsWith + " 艘上, 属性已计入耐久/载员/载货)";
            }
            catch { return ""; }
        }

        // 一条船装了几个升级件(用基础游戏的 Ship.GetShipSlotAndPieceNames(), 不引用 DLC 类型)
        internal static int PieceCount(Ship s)
        {
            try
            {
                if (s == null) return 0;
                var list = s.GetShipSlotAndPieceNames();
                return list != null ? list.Count : 0;
            }
            catch { return 0; }
        }

        // 一条船装了哪些件(舰队页 note 行用; 逗号分隔的中文/原名)
        internal static string PieceNames(Ship s)
        {
            try
            {
                if (s == null) return "";
                var list = s.GetShipSlotAndPieceNames();
                if (list == null || list.Count == 0) return "";
                var sb = new StringBuilder();
                for (int i = 0; i < list.Count; i++)
                {
                    var it = list[i];
                    // ShipSlotAndPieceName 是值类型(struct), 不能与 null 比较
                    if (string.IsNullOrEmpty(it.PieceName)) continue;
                    if (sb.Length > 0) sb.Append('/');
                    sb.Append(it.PieceName);
                }
                return sb.ToString();
            }
            catch { return ""; }
        }

        // B-6: 正在生效的海军特长(逐条名字) —— 前两条拼成一行, 太多就截断
        private static string PerkLine()
        {
            try
            {
                var ns = NavalAdmiral.ActivePerkNames(Hero.MainHero);
                if (ns == null || ns.Count == 0) return "";
                var sb = new StringBuilder();
                for (int i = 0; i < ns.Count && i < 2; i++)
                {
                    sb.Append('\n').Append(ns[i]);
                }
                return sb.ToString();
            }
            catch { return ""; }
        }

        // B-5: 诺德海军文化特性(第 3 项"船速 +10%"已接进海运航程 —— 见 NavalTransport.EtaDays)
        private static string NavalCultureSummary()
        {
            try
            {
                CultureObject c = null;
                try { c = Clan.PlayerClan != null ? Clan.PlayerClan.Culture : null; } catch { }
                var s = NavalCulture.Summary(c);
                if (string.IsNullOrEmpty(s)) return "运力 = 舰队载员 × 海军科技(海运) × 提督统率, 不足就多造船或先研究科技";
                return s;
            }
            catch { return ""; }
        }

        // ==================== v4.286: 单行文案的容量闸门 ====================
        //   控件宽 800 / 17 号字 -> 约 47 个全角字(ASCII 按 0.55 em 算)。
        //   HelpLine 每行、FootLine、ResultText 都要过这道闸: 超了就截断加省略号,
        //   而不是指望 ClipContents 或"应该放得下" —— 那是这一轮 5 个症状的共同根因。
        private const float LineMax = 800f, LineFont = 17f;
        private static string OneLine(string s) { return NavyRowVM.Clip(s, LineMax, LineFont); }
        // HelpLine 每行按 800 像素独立裁剪(它可能有 3 行, 逐行处理, 不破坏换行)
        private static string PerLine(string s)
        {
            try
            {
                if (string.IsNullOrEmpty(s)) return "";
                var parts = s.Split('\n');
                var sb = new StringBuilder();
                for (int i = 0; i < parts.Length; i++)
                {
                    if (i > 0) sb.Append('\n');
                    sb.Append(OneLine(parts[i]));
                }
                return sb.ToString();
            }
            catch { return s; }
        }

        [DataSourceProperty] public string FootLine
        {
            get
            {
                try
                {
                    if (!NavalBridge.Usable) return "";
                    var k = Kingdom();
                    if (k == null) return "";
                    // ==================== v4.286: 页脚一律压成**一行**(控件 SuggestedHeight 28) ====================
                    //   原因: 17 号字的行高约 22 像素, 控件高度一旦小于两行, 多出来的行会以控件中心为基准
                    //   向上下溢出 —— 向上压 @ResultText(y896), 向下压底部按钮(顶边 = 屏高-70)。
                    //   容量: 控件宽 800 / 17 号 -> 约 47 个全角字。下面每一条都按这个上限裁过。
                    switch (_tab)
                    {
                        case 0:
                            // 22 + 25 = 47 全角单位 -> 800 像素 / 17 号刚好一行(留 1 格余量)
                            return OneLine("战力 = 舷侧火力×舰炮×装甲×舰种系数×科技 · 日耗 = 我方全部舰船维护 + 水手军饷");
                        case 1:
                            return OneLine("造船厂每升 1 级解锁更高船体档(1 级轻 / 2 级中 / 3 级重), 科技再解锁铁甲舰以上");
                        case 2:
                            return OneLine("封锁: 目标港口繁荣度与关税逐日下降, 我方按封锁效率得战果; 停战/易主/无船自动解除");
                        case 4:
                            return OneLine("补给费按我方军团人数计, 从国库扣 · 在途运输队上限 " + NavalTransport.MaxConvoys + " 支");
                        default:
                            // v4.277(第 2 项): 战报页现在同时有两种来源 -> 页脚把来源说清楚
                            return OneLine("战报来源: 【实战】地图上真实发生的海战 · 【推演】面板[出击]的抽象结算(战力×提督系数)");
                    }
                }
                catch { return ""; }
            }
        }

        // v4.286: 舰队页页脚的巡逻提示改为**单行**(原 @PatrolHint 是两行, 塞进单行页脚会溢出);
        //   完整的"为什么 0 支 / 要过什么法令"已经在 HelpLine 的第 3 行(见 PatrolHelpLine)。
        //   这里只保留一个兜底入口, 万一以后又有别处要拼单行提示可直接调。
        [DataSourceProperty] public string PatrolHintOneLine
        {
            get
            {
                try
                {
                    int parties = NavalFleet.PatrolParties;
                    if (parties > 0) return " · 我方在航巡逻队 " + parties + " 支(巡逻 ×" + PatrolMulText() + ")";
                    if (HasPatrolTech(Kingdom())) return " · 我方在航巡逻队 0 支(需王国法令「海岸卫队」)";
                    return " · 我方在航巡逻队 0 支";
                }
                catch { return ""; }
            }
        }

        // v4.271: 底部结果行 —— 有操作结果(买船/封锁/出击/召回)就报结果, 否则给本页的下一步引导
        //   (标题栏那一行 StatusLine 是全局状态"海战 DLC 已接入 · 港口 N 处", 两者不再抢同一块地方)
        //   v4.286: 控件是**单行**(SuggestedHeight 26) -> 这里也要过 OneLine 闸门, 否则换行会压页脚。
        [DataSourceProperty] public string ResultText
        {
            get
            {
                try
                {
                    if (!string.IsNullOrEmpty(_status)) return OneLine(_status);
                    if (!NavalBridge.Usable) return "";
                    switch (_tab)
                    {
                        case 0:
                            // v4.286: 舰队页这一行承载"选中舰队的提督与特长"(构成条第 3 段搬来的)。
                            //   没有可选舰队时退回原来的下一步引导。
                            return OneLine(ComposeText3OrHint());
                        case 1:
                            return "提示: 点行内金色键购买/下单 · 点[选择城市]在地图上直接点选港口(◀ ▶ 仍可快速切换)";
                        case 2:
                            return "提示: 点行内键开始/解除封锁, 再点[出击]打一场舰队交战 · [选择城市]在地图上点选敌港";
                        case 3:
                            return NavalFleet.Battles.Count > 0 ? "提示: 滚轮翻页查看更早的海战记录" : "";
                        case 4:
                            return "提示: 选好 ① ② ③ 后点底部[装船出发] · 两个港口都可以点[选城]在地图上点选";
                        default:
                            return "";
                    }
                }
                catch { return ""; }
            }
        }

        // v4.286: 巡逻加成的完整说明(原 @PatrolHint 的两行文本) —— 现在作为 HelpLine 的第 3 行。
        //   给 null 时返回 " · " 开头的一段, 由调用方决定拼在哪一行; 太长(> 46 全角字)时只留前半句。
        private string PatrolHelpLine()
        {
            try
            {
                if (!NavalBridge.Usable) return "";
                var k = Kingdom();
                if (k == null) return "";
                int parties = NavalFleet.PatrolParties;
                if (parties > 0) return "我方在航巡逻队 " + parties + " 支 · 巡逻 ×" + PatrolMulText();
                if (!HasPatrolTech(k)) return "";
                return "我方在航巡逻队 0 支 —— 海上巡逻需王国法令「海岸卫队」, 通过后巡逻队才会生成";
            }
            catch { return ""; }
        }

        // v4.271: 页面上方的说明行(**固定 3 行**; 控件 SuggestedHeight="72")
        // ==================== v4.286(用户反馈第 3 条): 4 行 -> 3 行 ====================
        //   【原缺陷】控件给 90 像素想装 4 行, 但 17 号 CJK 的字面高约 22 像素, 4 行 = 88 像素,
        //   加上行距后最后一行顶出控件下边界(y240), 视觉上就压到别的层上(用户报的"长提示横穿列表")。
        //   【现在】每条最多 3 行, 每行按 46 个全角字(800 像素 / 17 号)控制; 超出的信息一律下移到
        //   页脚(单行), 不再靠"会被 ClipContents 裁掉"来蒙混。
        [DataSourceProperty] public string HelpLine
        {
            get
            {
                try
                {
                    if (!NavalBridge.Usable) return "";
                    var k = Kingdom();
                    if (k == null) return "";
                    switch (_tab)
                    {
                        case 0:
                            // 第 1 行: 本页是什么; 第 2 行: 两个"口径"说明(舰队上限 / 我方修船需求)
                            // 第 3 行: 进驻折扣 + 升级件 + 巡逻现状(三段都很短, 拼起来仍在 46 全角字内)
                            {
                                int needN; float needCost;
                                NavalBridge.RepairSummary(out needN, out needCost);
                                string repair = needN <= 0
                                    ? "我方舰船全部完好"
                                    : ("我方需维修 " + needN + " 艘(约 " + (int)Math.Round(needCost) + " 第纳尔)");
                                // v4.286: 控件只装得下 3 行(72 像素 / 17 号 = 3 行), 所以把"巡逻现状"并进第 2 行
                                //   (第 2 行原本只有舰队上限 + 修船需求 = 约 640 像素, 加巡逻那 260 像素仍 < 800),
                                //   第 3 行留给"进驻收益 + 升级件"。这样 4 段信息一条不丢, 又不超出 3 行。
                                string patrol = PatrolHelpLine();
                                string line2 = FleetCapLine(k) + " · " + repair
                                             + (string.IsNullOrEmpty(patrol) ? "" : " · " + patrol);
                                string line3 = "进驻收益: 我方船维护 -35% + 每日付费修理(仅自有沿海港口)"
                                             + (NavalTaskForce.LastMoorRepairShips > 0
                                                ? " · 昨日修复 " + NavalTaskForce.LastMoorRepairShips + " 艘" : "")
                                             + UpgradeLine();
                                return PerLine("本页是我方全部舰船(逐船一行) · 数值列与列头一一对应 · 完整度条越红越该修\n"
                                     + line2 + "\n" + line3);
                            }
                        case 1:
                            return PerLine("现货 = 船坞已造好的船, 立即过户; 下单建造 = 便宜 10%, 需等到工期结束\n"
                                 + "金色行动键 = 可立刻执行; 红色 = 门槛不够(点它会给中文原因)");
                        case 2:
                            // v4.273(B-2): 加一行海军法令(海岸卫队是海上巡逻队的唯一开关)
                            return PerLine("选一处敌国港口 → 点[封锁]开始封锁; 再点同一行可解除 · 封锁可随时[出击]打一场舰队交战\n"
                                 + NavalPolicy.OneLine(k));
                        case 4:
                            // v4.273(B-5): 加一行诺德海军文化特性(船速已接进海运航程)
                            return PerLine("装船条件: 军团在陆上待命 · 距出发港不超过 20 · 舰队载员 ≥ 军团人数 · 国库付得起补给费\n"
                                 + NavalCultureSummary());
                        default:
                            // v4.273(B-6): 追加"正在生效的海军特长"(逐条名字) + 渔获概况(B-4)
                            // v4.286: 条目压成 3 行(原来 4 行, 第 4 行会顶出控件)
                            return PerLine("海战结果自动记在这里(最近 12 场) · 【实战】=地图上真实发生的海战, 【推演】=面板[出击]的抽象结算\n"
                                 + NavalAdmiral.Summary(NavalAdmiral.Of(null)) + PerkLine() + "\n"
                                 + NavalFishing.Summary(k));
                    }
                }
                catch { return ""; }
            }
        }

        [DataSourceProperty] public string TechText
        {
            get
            {
                try
                {
                    var k = Kingdom();
                    if (k == null || !NavalBridge.Usable) return "";
                    if (_tab != 3) return "";   // v4.271: 科技汇总已并入战报页头部, 这里只在战报页列已研究的条目
                    var list = NavalTech.DoneTexts(k);
                    if (list.Count == 0) return "";
                    var sb = new StringBuilder();
                    for (int i = 0; i < list.Count && i < 6; i++) { if (i > 0) sb.Append(" · "); sb.Append(list[i]); }
                    if (list.Count > 6) sb.Append(" · … 共 ").Append(list.Count).Append(" 项");
                    return "已研究: " + sb.ToString();
                }
                catch { return ""; }
            }
        }

        // ==================== v4.271: 海运页三步引导 ====================

        private int _stepLegion, _stepFrom, _stepTo;

        private void ComputeSteps(Kingdom k)
        {
            try
            {
                int lg = NavalTransport.ReadyLegions().Count;
                int fp = k != null ? NavalBridge.PortsOf(k).Count : 0;
                int tp = k != null ? NavalTransport.DestinationPorts(k).Count : 0;
                _stepLegion = lg > 0 ? 2 : 0;
                _stepFrom = fp > 0 ? 2 : 0;
                _stepTo = tp > 0 ? 2 : 0;
            }
            catch { _stepLegion = _stepFrom = _stepTo = 0; }
        }

        private static string StepColor(int state)
        {
            if (state >= 2) return "#7FD8A0FF";   // 已完成: 绿
            if (state == 1) return "#E8C33AFF";   // 进行中: 金
            return "#8A8070FF";                    // 不可选: 灰
        }

        [DataSourceProperty] public string StepLegionColor { get { return StepColor(_stepLegion); } }
        [DataSourceProperty] public string StepFromColor { get { return StepColor(_stepFrom); } }
        [DataSourceProperty] public string StepToColor { get { return StepColor(_stepTo); } }

        [DataSourceProperty] public string LegionText
        {
            get
            {
                try
                {
                    if (_tab != 4) return "";
                    var lg = SelectedLegion();
                    if (lg == null) return "无军团可运";
                    // v4.286: 控件宽 148 像素 -> 军团名过长时截断(后面 ▶ 与[选城]的坐标不能动)
                    return NavyRowVM.Clip(NavalTransport.LegionLabel(lg) + " · " + NavalTransport.LegionMenOf(lg) + " 人", 148f, 18f);
                }
                catch { return "?"; }
            }
        }

        [DataSourceProperty] public string FromPortText
        {
            get
            {
                try
                {
                    if (_tab != 4) return "";
                    var k = Kingdom();
                    var p = k != null ? SelectedFromPort(k) : null;
                    return p == null ? "我方无港口" : NavalBridge.NameOf(p);
                }
                catch { return "?"; }
            }
        }

        [DataSourceProperty] public string ToPortText
        {
            get
            {
                try
                {
                    if (_tab != 4) return "";
                    var k = Kingdom();
                    var p = k != null ? SelectedToPort(k) : null;
                    return p == null ? "无可选目的港" : NavalBridge.NameOf(p);
                }
                catch { return "?"; }
            }
        }

        // v4.271: 不可出发时, [装船出发]按钮变灰(变不了 disabled 状态, 只能靠颜色说明"不可点")
        [DataSourceProperty] public string DepartBtnColor
        {
            get { return string.IsNullOrEmpty(_planBand) || _planBandColor == "#7FD8A0FF" ? "#7FD8A0FF" : "#8A8070FF"; }
        }

        // v4.271: 判语色带 —— 可出发(绿)/不可出发(红)+原因
        [DataSourceProperty] public string PlanBandText { get { return _planBand; } }
        [DataSourceProperty] public string PlanBandColor { get { return _planBandColor; } }

        private void ComputePlanBand(Kingdom k)
        {
            _planBand = ""; _planBandColor = "#C8B98FCC";
            try
            {
                if (_tab != 4) return;
                if (k == null) { _planBand = "尚未接管王国 —— 海运需要先立国"; _planBandColor = "#C96A5AFF"; return; }
                var lg = SelectedLegion();
                var from = SelectedFromPort(k);
                var to = SelectedToPort(k);
                if (lg == null) { _planBand = "第 1 步未完成: 没有可海运的军团 —— 先到「军务」页组建国防军军团, 并让它在陆上待命"; _planBandColor = "#C96A5AFF"; return; }
                if (from == null) { _planBand = "第 2 步未完成: 我方没有可用港口 —— 先占领一处沿海城镇"; _planBandColor = "#C96A5AFF"; return; }
                if (to == null) { _planBand = "第 3 步未完成: 没有可选的目的港(本国或中立沿海城镇)"; _planBandColor = "#C96A5AFF"; return; }
                int need, cap, eta, cost, ships;
                float safeDays;
                // v4.273(A-1): 用带 safeDays 的重载 —— 面板必须告诉玩家"这些船能安全航行几天"
                string why = NavalTransport.Precheck(lg, from, to, k, out need, out cap, out eta, out cost, out ships, out safeDays);
                // v4.273(A-1/B-3): 单独一行给"保险/风暴"提示 —— 判语带宽度有限, 合并一行会被裁掉
                string risk = SafeSailLine(safeDays, eta);
                string storm = StormLine(from, to);
                // v4.275(第 5 项): riskLine 原来用 "\n" 拼成**第二行**, 而判语带控件(prefab y920 h26)
                //   是顶部锚定单行: 多余的行会被 ClipContents 直接裁掉(信息静默消失)。本控件**不能加高**
                //   —— 加高到 46 会伸到 y966, 与底部锚定的按钮(顶边 = 屏高-70, 屏高 1030 时 960)重叠。
                //   所以改为: 风险提示合并进**同一行**, 由 ClampBand() 统一"超长截断 + …",
                //   并把完整文本(判语 + 风险)写进日志与地图消息, 保证裁掉的部分仍可查。
                string riskLine = "";
                if (!string.IsNullOrEmpty(risk) || !string.IsNullOrEmpty(storm))
                {
                    riskLine = (risk.Length > 3 ? risk.Substring(3) : "")      // 去掉开头的 " · "
                             + (storm.Length > 3 ? (risk.Length > 3 ? " · " : "") + storm.Substring(3) : "");
                }
                if (!string.IsNullOrEmpty(why))
                {
                    // v4.271: 玩家最常卡在"运力不足", 这里把两个数都摆出来, 玩家一眼知道差多少
                    _planBand = ((cap > 0 || need > 0)
                        ? ("不可出发 —— 舰队载员 " + cap + " / 军团 " + need + " 人 · " + why)
                        : ("不可出发 —— " + why)) + riskLine;
                    _planBandColor = "#C96A5AFF";
                    ClampBand();
                    return;
                }
                int gold = 0;
                try { gold = EconomyWorld.Treasury.Gold; } catch { }
                _planBand = "可以出发 —— 舰队载员 " + cap + " / 军团 " + need + " 人 · 用船 " + ships + " 艘 · 航程约 "
                          + eta + " 天 · 补给费 " + cost + " 第纳尔(国库 " + gold + ")" + riskLine;
                _planBandColor = "#7FD8A0FF";
                ClampBand();
            }
            catch { _planBand = ""; }
        }

        // v4.275(第 5 项): 判语带单行截断 —— 控件宽 788 / 17 号字, 一行约放得下 46 个汉字。
        //   超长就在末尾补 "…" 并**把完整文本送进日志 + 地图消息**(截断不等于丢信息)。
        //   注意: 这里只处理"显示长度", 不改任何业务数值。
        private const int PlanBandMaxChars = 46;
        private void ClampBand()
        {
            try
            {
                if (string.IsNullOrEmpty(_planBand)) return;
                if (_planBand.IndexOf('\n') >= 0) _planBand = _planBand.Replace('\n', ' ');   // 万无一失: 判语带只吃单行
                if (_planBand.Length <= PlanBandMaxChars) return;
                string full = _planBand;
                _planBand = _planBand.Substring(0, PlanBandMaxChars - 1) + "…";
                // 只在水运页且内容真的被截断时提示一次(避免 3 秒节流刷新把日志刷屏)
                if (!_bandTruncatedNotified)
                {
                    _bandTruncatedNotified = true;
                    DLog.Force("海军: 海运判语带超长已截断显示 —— 完整文本: " + full);
                    try { MapSelection.Message("海军(完整判语): " + full); } catch { }
                }
            }
            catch { }
        }

        private bool _bandTruncatedNotified;

        // v4.273(A-1): "主力舰预计可安全航行 N 天 / 本航程约 M 天"
        private static string SafeSailLine(float safeDays, int eta)
        {
            try
            {
                if (safeDays < 0f) return "";                       // 读不到原版模型 -> 不显示
                return " · 主力舰预计可安全航行 " + safeDays.ToString("0.#") + " 天 / 本航程约 " + eta + " 天";
            }
            catch { return ""; }
        }

        // v4.273(B-3): 航线上有风暴 -> "预计延误 N 天"
        private static string StormLine(Settlement from, Settlement to)
        {
            try
            {
                if (from == null || to == null) return "";
                var hit = NavalSeaRisk.RouteStorm(from.PortPosition.ToVec2(), to.PortPosition.ToVec2());
                if (!hit.Any) return "";
                int d = NavalSeaRisk.StormDelayDays(from.PortPosition.ToVec2(), to.PortPosition.ToVec2(),
                                                    Math.Max(1, NavalTransport.EtaDays(from, to)));
                return " · 航线上有风暴(" + hit.Count + " 处), 预计延误 " + d + " 天";
            }
            catch { return ""; }
        }

        public void ExecuteClose() { if (_onClose != null) _onClose(); }

        internal void SetTab(int t)
        {
            if (_tab == t) return;
            // v4.282: 切页签 -> 退出地图点选(点选态只对发起它的那一页有意义; 否则会出现
            //   "在造船页点了选城, 切到封锁页又点了一下城市, 结果改了造船港"这种错位)
            if (_pick != PickNone) { _pick = PickNone; SettlementPickMode.Cancel(); }
            _tab = t;
            _scroll = 0;
            _status = "";
            _bandTruncatedNotified = false;   // v4.275: 换页后允许再提示一次"判语被截断"
            Refresh();
        }

        internal void ClearStatus() { _status = ""; }

        internal static Kingdom Kingdom()
        {
            try
            {
                var b = NationalWillOrders.Behavior;
                return b != null ? b.NationKingdom : null;
            }
            catch { return null; }
        }

        private Settlement SelectedPort(Kingdom k)
        {
            try
            {
                var ports = NavalBridge.PortsOf(k);
                if (ports.Count == 0) return null;
                if (_portIdx < 0 || _portIdx >= ports.Count) _portIdx = 0;
                return ports[_portIdx];
            }
            catch { return null; }
        }

        internal int PortCount() { try { var k = Kingdom(); return k != null ? NavalBridge.PortsOf(k).Count : 0; } catch { return 0; } }
        internal int EnemyPortCount() { try { var k = Kingdom(); return k != null ? NavalBridge.EnemyPorts(k).Count : 0; } catch { return 0; } }

        // 切换造船港口
        internal void NextPort()
        {
            try
            {
                int n = PortCount();
                if (n <= 0) { _status = "我方没有可用港口"; return; }
                _portIdx = (_portIdx + 1) % n;
                _scroll = 0;
                _status = "";
                Refresh();
            }
            catch { }
        }

        internal void PrevPort()
        {
            try
            {
                int n = PortCount();
                if (n <= 0) { _status = "我方没有可用港口"; return; }
                _portIdx = (_portIdx - 1 + n) % n;
                _scroll = 0;
                _status = "";
                Refresh();
            }
            catch { }
        }

        internal void NextEnemyPort()
        {
            try
            {
                int n = EnemyPortCount();
                if (n <= 0) { _status = "没有可封锁的敌国港口(需与拥有沿海城镇的国家交战)"; return; }
                _enemyPortIdx = (_enemyPortIdx + 1) % n;
                _scroll = 0;
                _status = "";
                Refresh();
            }
            catch { }
        }

        internal void PrevEnemyPort()
        {
            try
            {
                int n = EnemyPortCount();
                if (n <= 0) { _status = "没有可封锁的敌国港口(需与拥有沿海城镇的国家交战)"; return; }
                _enemyPortIdx = (_enemyPortIdx - 1 + n) % n;
                _scroll = 0;
                _status = "";
                Refresh();
            }
            catch { }
        }

        // ==================== v4.270: 海运页(军团 / 出发港 / 目的港) ====================

        private DefLegion SelectedLegion()
        {
            try
            {
                var list = NavalTransport.ReadyLegions();
                if (list.Count == 0) return null;
                if (_legionIdx < 0 || _legionIdx >= list.Count) _legionIdx = 0;
                return list[_legionIdx];
            }
            catch { return null; }
        }

        private Settlement SelectedFromPort(Kingdom k)
        {
            try
            {
                var ports = NavalBridge.PortsOf(k);
                if (ports.Count == 0) return null;
                if (_fromPortIdx < 0 || _fromPortIdx >= ports.Count) _fromPortIdx = 0;
                return ports[_fromPortIdx];
            }
            catch { return null; }
        }

        private Settlement SelectedToPort(Kingdom k)
        {
            try
            {
                var ports = NavalTransport.DestinationPorts(k);
                if (ports.Count == 0) return null;
                if (_toPortIdx < 0 || _toPortIdx >= ports.Count) _toPortIdx = 0;
                return ports[_toPortIdx];
            }
            catch { return null; }
        }

        private void CycleLegion(int d)
        {
            try
            {
                int n = NavalTransport.ReadyLegions().Count;
                if (n <= 0) { _status = "没有可海运的军团(需: 军团健在 · 在陆上 · 未交战 · 未在运输中)"; return; }
                _legionIdx = ((_legionIdx + d) % n + n) % n;
                _status = "";
                Refresh();
            }
            catch { }
        }

        private void CycleFromPort(int d)
        {
            try
            {
                var k = Kingdom();
                int n = k != null ? NavalBridge.PortsOf(k).Count : 0;
                if (n <= 0) { _status = "我方没有可用港口"; return; }
                _fromPortIdx = ((_fromPortIdx + d) % n + n) % n;
                _status = "";
                Refresh();
            }
            catch { }
        }

        private void CycleToPort(int d)
        {
            try
            {
                var k = Kingdom();
                int n = k != null ? NavalTransport.DestinationPorts(k).Count : 0;
                if (n <= 0) { _status = "没有可选的目的港(本国或中立沿海城镇)"; return; }
                _toPortIdx = ((_toPortIdx + d) % n + n) % n;
                _status = "";
                Refresh();
            }
            catch { }
        }

        internal void NextLegion() { CycleLegion(1); }
        internal void PrevLegion() { CycleLegion(-1); }
        internal void NextFromPort() { CycleFromPort(1); }
        internal void PrevFromPort() { CycleFromPort(-1); }
        internal void NextToPort() { CycleToPort(1); }
        internal void PrevToPort() { CycleToPort(-1); }

        // [装船出发]: 选中军团 -> 从出发港装船出海 -> 目的港下船(见 NavalTransport)
        internal void DepartTransport()
        {
            try
            {
                var k = Kingdom();
                if (k == null) { _status = "尚未接管王国"; return; }
                var lg = SelectedLegion();
                var from = SelectedFromPort(k);
                var to = SelectedToPort(k);
                _status = NavalTransport.Depart(lg, from, to, k);
                MapSelection.Message("海军: " + _status);
                Refresh();
            }
            catch (Exception ex) { _status = "海运操作失败: " + ex.Message; }
        }

        // 海运页行按钮 = 召回该运输队
        //   v4.279(第 3 项): 旧的 `ActionTransportRecall(int i)`(按行号索引 Active)已删除 ——
        //   Active 会被小时结/日结增删, 行号在 3 秒刷新窗口内就会指错运输队。现在统一走
        //   `ActionTransportRecall(string key)`: 热区捕获 Convoy.PartyId, 点击时按 PartyId 反查下标。

        // ==================== 列表滚动(v4.286 修好: 窗口在构建阶段生效) ====================
        private int _rowCount;   // 本页签的**总行数**(不受滚动窗口裁剪影响)
        private int _total;      // 构建过程中的绝对行下标计数器(每加一行 +1)

        // 每页行数: 舰船行 3 行(与 prefab 行高 112 + 数据区 y496..832 严格对应);
        //   战报行是可变高度, 一屏约 4 条。
        internal const int LogsPerPage = 4;
        private int PerPage { get { return _tab == 3 ? LogsPerPage : RowsPerPage; } }

        internal void ScrollStep(int dir)
        {
            try
            {
                if (dir == 0) return;
                int max = Math.Max(0, _rowCount - PerPage);
                int want = _scroll + dir;
                if (want < 0) want = 0;
                if (want > max) want = max;
                if (want == _scroll) return;   // 已经到顶/到底: 不做无意义的 Refresh
                _scroll = want;
                Refresh();                     // 重建 = 真的换一页(渲染跟着变)
            }
            catch { }
        }

        // 热区循环用它取"当前屏幕上实际有几行" —— 窗口生效后就是 Rows.Count 本身
        internal int ShownRowCount
        {
            get { try { return Rows != null ? Rows.Count : 0; } catch { return 0; } }
        }

        // v4.286(用户反馈③后半): 页码指示器。
        //   ★ 总行数 <= 每页行数时显示 "第 1/1 页"(**不显示 1/0** —— 那会让人以为能滚却没反应),
        //     一页装不下时才显示 "第 x/y 页 · 滚轮翻页"。
        [DataSourceProperty] public string PageText
        {
            get
            {
                try
                {
                    if (_rowCount <= 0) return "";
                    int per = PerPage;
                    if (per <= 0) return "";
                    int pages = (_rowCount + per - 1) / per;
                    if (pages <= 1) return "";   // v4.286c: 一屏装得下就不提示(避免"能滚"的错觉)
                    int cur = _scroll / per + 1;
                    if (cur < 1) cur = 1;
                    if (cur > pages) cur = pages;
                    return "滚轮 " + cur + "/" + pages + " 页";
                }
                catch { return ""; }
            }
        }

        // ==================== 刷新 ====================
        // ==================== v4.286(用户反馈③: 滚轮翻页没反应) 根因与修法 ====================
        //   【根因】**渲染没跟**: 原来 Refresh() 每次都是 `Rows.Clear()` 之后从**第 0 行**重新填满
        //   RowsPerPage 行, `_scroll` 只被 ShownRowCount(热区计数)读到 —— 也就是说滚轮事件其实接到了
        //   (PanelScreen.PollWheel -> SetScrollHandler -> ScrollStep), `_scroll` 也确实变了(还有上限夹取),
        //   但**列表内容永远是第 0..2 行**, 所以看起来"滚轮完全没反应"。
        //   【修法】把窗口下移到**构建阶段**: Add*Row 给每一行一个绝对下标 `_total`, 只有落在
        //   [_scroll, _scroll + 每页行数) 区间内的行才真的进 Rows(进 ListPanel); 总行数记在 _rowCount。
        //   这样 _scroll 一变, 下一次 Refresh 重建出来的就是**新的一页**, 与陆军页 ScrollStep 的行为一致。
        internal void Refresh() { RefreshWindow(false); }

        private void RefreshWindow(bool retryForWindow)
        {
            try
            {
                // v4.279(第 5 项 ★): 面板刷新 = 一次新的"数据快照", 先把 MyShips() 的批内缓存显式失效。
                //   这样"缓存"只服务于**同一次 Refresh 内部**的重复调用(敌港循环那种, 原来 20 港 ×2 次
                //   = 40 次全量遍历), 而每次刷新、每次操作后的 Refresh 都一定重算 —— 玩家永远不会看到
                //   过期数据(10ms 时间窗只是这一层的兜底, 见 NavalBridge.CacheHitWindowMs 的注释)。
                InvalidateShipCaches();
                // v4.286: 用**上一轮**的总行数先把 _scroll 夹到合法范围(数据变少/换页签时不至于窗口落空);
                //   真实总行数出来之后再校正一次(见函数末尾), 必要时只重建一层。
                int per = PerPage;
                int prevMax = _rowCount > per ? _rowCount - per : 0;
                if (_scroll > prevMax) _scroll = prevMax;
                if (_scroll < 0) _scroll = 0;
                Rows.Clear();
                Logs.Clear();
                _rowCount = 0;
                _total = 0;
                _emptyFleet = _emptyYard = _emptyBlock = _emptyTrans = _emptyLogs = "";
                _planBand = ""; _planBandColor = "#C8B98FCC";
                _stepLegion = _stepFrom = _stepTo = 0;
                // v4.276: 舰队选择索引越界自愈(舰队被解散/被歼灭后索引会指到空处)
                try
                {
                    int fn = NavalTaskForce.Alive().Count;
                    if (fn <= 0) _fleetIdx = 0;
                    else if (_fleetIdx < 0 || _fleetIdx >= fn) _fleetIdx = 0;
                }
                catch { }
                if (!NavalBridge.Usable)
                {
                    OnChanged();
                    return;
                }
                var k = Kingdom();
                if (k == null) { OnChanged(); return; }

                switch (_tab)
                {
                    case 0: BuildFleetRows(k); break;
                    case 1: BuildShipyardRows(k); break;
                    case 2: BuildBlockadeRows(k); break;
                    case 4: BuildTransportRows(k); break;   // v4.270: 海运
                    default: BuildLogRows(); break;
                }
                // v4.271: 海运页的三步状态与判语色带依赖上面刚生成的行(在途运输队)与选中项
                if (_tab == 4) { ComputeSteps(k); ComputePlanBand(k); }
                // v4.286: 真实总行数出来后再校正一次窗口 —— 若这一轮数据变少把 _scroll 顶出了范围,
                //   就只重建一层(把新的一页填上), 不会无限递归。
                int maxNow = _rowCount > per ? _rowCount - per : 0;
                if (!retryForWindow && _scroll > maxNow)
                {
                    _scroll = maxNow;
                    RefreshWindow(true);
                    return;
                }
                OnChanged();
            }
            catch (Exception ex) { DLog.Force("海军页刷新失败: " + ex.Message); }
        }

        private void OnChanged()
        {
            OnPropertyChangedWithValue(Rows.Count, "Rows");
            OnPropertyChangedWithValue(Logs.Count, "Logs");
            OnPropertyChangedWithValue(_tab, "Tab");
            OnPropertyChangedWithValue(ShowRows, "ShowRows");
            OnPropertyChangedWithValue(ShowLogs, "ShowLogs");
            // v4.282: 造船/封锁控制条拆开 + 选城按钮 + 状态行 + 进驻按钮文案
            OnPropertyChangedWithValue(ShowPortPicker, "ShowPortPicker");
            OnPropertyChangedWithValue(ShowEnemyPicker, "ShowEnemyPicker");
            OnPropertyChangedWithValue(TabStatusText, "TabStatusText");
            OnPropertyChangedWithValue(TabStatusColor, "TabStatusColor");
            OnPropertyChangedWithValue(PickBtnText, "PickBtnText");
            OnPropertyChangedWithValue(MoorBtnText, "MoorBtnText");
            OnPropertyChangedWithValue(ShowSortie, "ShowSortie");
            OnPropertyChangedWithValue(PickerText, "PickerText");
            OnPropertyChangedWithValue(PickerHint, "PickerHint");
            OnPropertyChangedWithValue(Header, "Header");
            OnPropertyChangedWithValue(FootLine, "FootLine");
            OnPropertyChangedWithValue(ResultText, "ResultText");
            OnPropertyChangedWithValue(HelpLine, "HelpLine");
            OnPropertyChangedWithValue(StatusLine, "StatusLine");
            OnPropertyChangedWithValue(TechText, "TechText");
            OnPropertyChangedWithValue(ShownRowCount, "ShownRowCount");
            OnPropertyChangedWithValue(PageText, "PageText");   // v4.286: 页码指示器(滚轮翻页的可见反馈)
            // v4.270: 海运页
            OnPropertyChangedWithValue(ShowTransport, "ShowTransport");
            OnPropertyChangedWithValue(ShowResult, "ShowResult");
            OnPropertyChangedWithValue(LegionText, "LegionText");
            OnPropertyChangedWithValue(FromPortText, "FromPortText");
            OnPropertyChangedWithValue(ToPortText, "ToPortText");
            // v4.271: 五页签选中态 / 每页列头 / 空状态 / 海运三步与判语
            OnPropertyChangedWithValue(TabColor0, "TabColor0");
            OnPropertyChangedWithValue(TabColor1, "TabColor1");
            OnPropertyChangedWithValue(TabColor2, "TabColor2");
            OnPropertyChangedWithValue(TabColor3, "TabColor3");
            OnPropertyChangedWithValue(TabColor4, "TabColor4");
            OnPropertyChangedWithValue(TabLine0, "TabLine0");
            OnPropertyChangedWithValue(TabLine1, "TabLine1");
            OnPropertyChangedWithValue(TabLine2, "TabLine2");
            OnPropertyChangedWithValue(TabLine3, "TabLine3");
            OnPropertyChangedWithValue(TabLine4, "TabLine4");
            OnPropertyChangedWithValue(ShowHeadFleet, "ShowHeadFleet");
            OnPropertyChangedWithValue(ShowHeadYard, "ShowHeadYard");
            OnPropertyChangedWithValue(ShowHeadBlock, "ShowHeadBlock");
            OnPropertyChangedWithValue(ShowHeadTrans, "ShowHeadTrans");
            OnPropertyChangedWithValue(ShowRowList, "ShowRowList");
            OnPropertyChangedWithValue(ShowLogList, "ShowLogList");
            OnPropertyChangedWithValue(EmptyFleet, "EmptyFleet");
            OnPropertyChangedWithValue(EmptyYard, "EmptyYard");
            OnPropertyChangedWithValue(EmptyBlock, "EmptyBlock");
            OnPropertyChangedWithValue(EmptyTrans, "EmptyTrans");
            OnPropertyChangedWithValue(EmptyLogs, "EmptyLogs");
            OnPropertyChangedWithValue(ShowEmptyFleet, "ShowEmptyFleet");
            OnPropertyChangedWithValue(ShowEmptyYard, "ShowEmptyYard");
            OnPropertyChangedWithValue(ShowEmptyBlock, "ShowEmptyBlock");
            OnPropertyChangedWithValue(ShowEmptyTrans, "ShowEmptyTrans");
            OnPropertyChangedWithValue(ShowEmptyLogs, "ShowEmptyLogs");
            OnPropertyChangedWithValue(StepLegionColor, "StepLegionColor");
            OnPropertyChangedWithValue(StepFromColor, "StepFromColor");
            OnPropertyChangedWithValue(StepToColor, "StepToColor");
            OnPropertyChangedWithValue(PlanBandText, "PlanBandText");
            OnPropertyChangedWithValue(PlanBandColor, "PlanBandColor");
            OnPropertyChangedWithValue(DepartBtnColor, "DepartBtnColor");
            // v4.276: 舰队指挥条
            OnPropertyChangedWithValue(ShowFleetBar, "ShowFleetBar");
            OnPropertyChangedWithValue(FleetBarText, "FleetBarText");
            OnPropertyChangedWithValue(FleetBarHint, "FleetBarHint");
            // v4.284(第 1 项): 顶部资源总览条(国库/舰队/舰船/水手/日耗/巡逻队)
            OnPropertyChangedWithValue(OvTreasury, "OvTreasury");
            OnPropertyChangedWithValue(OvTreasuryColor, "OvTreasuryColor");
            OnPropertyChangedWithValue(OvFleets, "OvFleets");
            OnPropertyChangedWithValue(OvShips, "OvShips");
            OnPropertyChangedWithValue(OvSailors, "OvSailors");
            OnPropertyChangedWithValue(OvCost, "OvCost");
            OnPropertyChangedWithValue(OvPatrol, "OvPatrol");
            OnPropertyChangedWithValue(OvPatrolColor, "OvPatrolColor");
            // v4.284(第 2 项): 选中舰队的构成条(三段)
            OnPropertyChangedWithValue(ShowCompose, "ShowCompose");
            OnPropertyChangedWithValue(ComposeText1, "ComposeText1");
            OnPropertyChangedWithValue(ComposeText2, "ComposeText2");
            // v4.286: @ComposeText3 的绑定已从 prefab 移除(构成条压成两段), 它现在由 ResultText 的 case 0
            //   调用 -> 只要通知 ResultText 即可。
            OnPropertyChangedWithValue(ResultText, "ResultText");
            // v4.284(第 3/4 项): 批量操作三枚 + 花钱换属性两枚(文字与颜色都绑定, 灰化/变红要立刻生效)
            OnPropertyChangedWithValue(RecallAllColor, "RecallAllColor");
            OnPropertyChangedWithValue(ReleaseAllColor, "ReleaseAllColor");
            OnPropertyChangedWithValue(MoorAllColor, "MoorAllColor");
            OnPropertyChangedWithValue(DrillText, "DrillText");
            OnPropertyChangedWithValue(DrillColor, "DrillColor");
            OnPropertyChangedWithValue(OverhaulText, "OverhaulText");
            OnPropertyChangedWithValue(OverhaulColor, "OverhaulColor");
        }

        // ==================== v4.271: 列对齐用的常量(v4.286 起只用于注释与自检) ====================
        //   数值列的**实际几何已经全部搬进 FeudalNavy.xml**(每列一个独立 TextWidget):
        //     列宽 282 / 82 / 72 / 120, 行内左边界 194 / 488 / 582 / 610
        //     行内右边缘 476 / 570 / 654 / 730(四个页签共用; 相邻列净空 >= 12 像素)
        //   列头(面板 坐标)= 行内 + 26 -> 右边缘 502 / 596 / 680 / 756, 与数据列逐像素相同。
        //   ★ 这里保留这组常量只为"改 prefab 时必须同步"的自检锚点, 不再参与渲染。
        internal const int NumRight1 = 476, NumRight2 = 570, NumRight3 = 654, NumRight4 = 730;

        // v4.279(第 3 项): 三个 Add*Row 改为**返回行对象** —— 调用方渲染完立刻写 row.Key(稳定标识),
        //   供 NavyPanel.RegisterSpots 把 key 捕获进热区闭包(见 NavyRowVM.Key 的长注释)。
        // v4.286(用户反馈③): 每一行先拿一个**绝对下标** _total; 只有落在当前滚动窗口
        //   [_scroll, _scroll + 每页行数) 里的行才真的进 Rows(也就是真的进 ListPanel)。
        //   这就是"滚轮翻页"能生效的关键 —— 窗口在**构建阶段**生效, 而不是只改一个没人看的索引。
        private NavyRowVM AddRow(string icon, string c1, string c2,
                            string n1, string n2, string n3, string n4,
                            string note, string noteColor, bool bar, float ratio,
                            string action, string actionColor)
        {
            var r = new NavyRowVM(icon, c1, c2, n1, n2, n3, n4, note, noteColor, bar, ratio, action, actionColor);
            int abs = _total++;
            if (abs >= _scroll && Rows.Count < RowsPerPage) Rows.Add(r);
            return r;
        }

        private NavyRowVM AddBlockRow(string icon, string c1, string c2,
                                 string n1, string n2, string n3, string n4,
                                 string note, string noteColor, string action, string actionColor)
        {
            var r = new NavyRowVM(icon, c1, c2, n1, n2, n3, n4, note, noteColor, false, 0f, action, actionColor);
            int abs = _total++;
            if (abs >= _scroll && Rows.Count < RowsPerPage) Rows.Add(r);
            return r;
        }

        private NavyRowVM AddTransRow(string icon, string c1, string c2,
                                 string n1, string n2, string n3, string n4,
                                 string note, string noteColor,
                                 string action, string actionColor)
        {
            var r = new NavyRowVM(icon, c1, c2, n1, n2, n3, n4, note, noteColor, false, 0f, action, actionColor);
            r.SetVoyageColor();   // 第 4 列是"航程(天)", 用青色区别于维护费
            int abs = _total++;
            if (abs >= _scroll && Rows.Count < RowsPerPage) Rows.Add(r);
            return r;
        }

        // ==================== 页签 0: 舰队 ====================
        private void BuildFleetRows(Kingdom k)
        {
            var ships = NavalFleet.MyShips();   // v4.273(审计 #2): 舰队页 = 我方口径显式化
            for (int i = 0; i < ships.Count; i++)
            {
                var s = ships[i];
                if (s == null) continue;
                string name = NavalBridge.HullName(s);
                try { if (s.Name != null && !string.IsNullOrEmpty(s.Name.ToString())) name = s.Name.ToString(); } catch { }
                float ratio = NavalBridge.HealthRatio(s);
                int hp = (int)NavalBridge.HitPoints(s), hpMax = (int)NavalBridge.MaxHitPoints(s);
                int power = (int)NavalBridge.ShipPowerScore(s);
                // v4.282: 维护列走 ShipUpkeepOf(**含驻泊折扣**) —— 与日结实际扣款同口径;
                //   驻泊的船在后面补一个 "泊" 标记, 免得玩家以为"这船怎么比别的便宜"。
                float upk = NavalTaskForce.ShipUpkeepOf(s);
                string upkeep = upk.ToString("0.0");
                bool shipMoored = false;
                try { shipMoored = NavalTaskForce.IsMooredShipKey(NavalTransport.ShipKey(s)); } catch { }

                // v4.271: 第二行说明 —— 完整度百分比 + 修理需求 + 载货, 修理费与 NavalFleet 的每日修理口径一致
                // ==================== v4.286(用户反馈第 2/4 条): 说明行的**优先级排序** ====================
                //   说明行只占一行(652 像素 = 约 38 个全角字), 超出部分由 NavyRowVM.Clip 截断加省略号。
                //   所以**最要紧的信息必须排在前面**: 完整度 → 修船报价 → 件数/载货 → 载员拆解。
                //   (旧版把"载员拆解"排在升级件之前, 一旦截断就先丢掉升级件 —— 那是玩家最想看的。)
                string note = "完整度 " + (int)Math.Round(ratio * 100f) + "%";
                // ==================== v4.279(第 7 项): 修船价改用 DLC 权威口径 ====================
                //   【原缺陷】这里行内硬编码 `(MaxHP-HP) × 2.5 第纳尔`, 而当时的页脚汇总
                //   (FleetRepairLine, v4.284 已删/其口径并入 HelpLine 与 [整备] 报价)用的是
                //   `NavalBridge.RepairSummary` -> `NavalBridge.RepairCost`
                //   -> DLC `ShipCostModel.GetShipRepairCost` —— **同一页两套价**: 玩家在行内看到的价
                //   与页脚汇总/实际扣费不一致(DLC 的口径含船体价/科技/技能因素, 不是线性的 2.5/点)。
                //   现在统一到 RepairCost, 与页脚那行、以及真正扣钱的 DLC 行为完全同源。
                TaleWorlds.CampaignSystem.Party.PartyBase payer = null;
                try { payer = MobileParty.MainParty != null ? MobileParty.MainParty.Party : null; } catch { }
                float repairCost = 0f;
                try { repairCost = NavalBridge.RepairCost(s, payer); } catch { }
                int repair = (int)Math.Round(repairCost);
                if (repair > 0) note += " · DLC 修船价约 " + repair + " · " + FleetStatus.HealthWord(ratio);
                else note += " · 船体完好";
                // v4.273(B-1): 装了哪些升级件(基础游戏 Ship.GetShipSlotAndPieceNames, 不引用 DLC 类型)
                string pieces = PieceNames(s);
                if (!string.IsNullOrEmpty(pieces)) note += " · 升级件: " + pieces;
                float cargo = NavalBridge.Cargo(s);
                if (cargo > 0f) note += " · 载货 " + (int)cargo;
                // v4.279(第 6 项): 第 1 列"载员"= **总载员**(DLC 战斗部署/超载判定/运力上限的权威口径,
                //   见 NavalBridge.Crew 的反编译长注释)。有下层甲板时在说明里把"主甲板/下层"拆出来 ——
                //   玩家既能看到"这条船能装多少人"(总), 也能看到"上层战斗位有多少"(主甲板)。
                int totalCrew = NavalBridge.Crew(s);
                int deckCrew = NavalBridge.MainDeckCrew(s);
                if (totalCrew > deckCrew && deckCrew > 0)
                    note += " · 载员拆解: 主甲板 " + deckCrew + " + 下层 " + (totalCrew - deckCrew);

                AddRow(NavalBridge.IconOf(s), name,
                    NavalBridge.HullTypeName(NavalBridge.HullType(s)) + (shipMoored ? " · 驻泊" : ""),
                    NavalBridge.Crew(s).ToString(), hp + "/" + hpMax, power.ToString(),
                    upkeep + (shipMoored ? "(泊)" : ""),
                    note, FleetStatus.ColorOfRatio(ratio), true, ratio, "", "");
            }
            // v4.277(第 1 项 ★): 汇总行 —— "船维护 X 第纳尔/日 + 水手军饷 Y 第纳尔/日 = 合计 Z"
            //   为什么单独一行而不是塞进页脚: 玩家问的是"养这支舰队到底花多少", 那是一个**数字**,
            //   必须落在舰队页的行区里(与舰船明细同表), 页脚那句话是解释口径用的。
            //   两个数字都来自权威口径: 船长维护 = Σ NavalBridge.ShipUpkeep(与 NavalFleet.DailyUpkeep 同源),
            //   水手军饷 = NavalTaskForce.NavalWageDaily()(与日结真实扣款同源)。
            try
            {
                int allUpkeep, allWage;
                AllFleetCost(out allUpkeep, out allWage);
                int sailors = NavalTaskForce.NavalSailorsTotal();
                if (allWage > 0 || allUpkeep > 0)
                {
                    AddRow(NavalBridge.IconOfHullId("", 1), "【舰队开销汇总】",
                        "船维护 + 水手军饷",
                        sailors.ToString(), "", "", (allUpkeep + allWage).ToString(),
                        "船维护 " + allUpkeep + " 第纳尔/日 + 水手军饷 " + allWage + " 第纳尔/日(水手 "
                            + sailors + " 人 × " + NavalTaskForce.WageRate().ToString("0.##") + "/人/日, 与陆军同口径)"
                            + " = 合计 " + (allUpkeep + allWage) + " 第纳尔/日 · 军饷每日从国库扣除并计入财政[军费]科目",
                        "#8FD8E8FF", false, 0f, "", "");
                }
            }
            catch { }
            for (int i = 0; i < NavalFleet.Orders.Count; i++)
            {
                var o = NavalFleet.Orders[i];
                if (o == null) continue;
                // v4.271: NavalFleet.Order 只带 PortName/HullName/Paid/DaysLeft/DaysTotal/Tier/Progress
                //   (载员·耐久·船价在订单里没有留存, 所以这四列留空 —— 需要的话要在 NavalFleet.Order 上加字段,
                //    那属于逻辑文件, 本次不改, 已列入"建议但未做"清单)
                AddRow(NavalBridge.IconShipyard, "【在建】" + o.HullName,
                    NavalBridge.HullTypeName(o.Tier) + "舰 · 建造中",
                    "", "", "", "",
                    "进度 " + (int)(o.Progress * 100f) + "% · 剩余 " + o.DaysLeft + "/" + o.DaysTotal + " 天 · 已付 "
                        + (int)o.Paid + " 第纳尔 · 在 " + o.PortName + " 建造, 完工自动入列",
                    "#8FD8E8FF", true, o.Progress, "", "");
            }
            _rowCount = _total;
            if (_rowCount == 0)
                _emptyFleet = "我方暂无舰船 —— 到[造船]页在我方港口下单, 或直接买现货; 我方舰队上限 "
                            + NavalFleet.FleetCap(k) + " 艘 · 维护与战力见「战报」页科技汇总";
        }

        // ==================== 页签 1: 造船 ====================
        private void BuildShipyardRows(Kingdom k)
        {
            var p = SelectedPort(k);
            if (p == null)
            {
                _rowCount = 0;
                _emptyYard = "我方没有可用港口 —— 先占领一处沿海城镇(港口城镇), 或研究海军科技 navigation 解锁港口建筑";
                return;
            }
            var offers = NavalFleet.Offers(p, k);
            for (int i = 0; i < offers.Count; i++)
            {
                var o = offers[i];
                if (o == null) continue;
                string note;
                string act, actColor;
                if (o.Buildable)
                {
                    if (o.Days == 0)
                    {
                        note = "现货 · 立刻过户 · 买价含 1.5 倍船价(科技已折)";
                        act = "购买"; actColor = "#7FBF6AFF";
                    }
                    else
                    {
                        note = "下单建造 · 比现货便宜 10%, 需等 " + o.Days + " 天 · 期间占造船队列";
                        act = "下单"; actColor = "#E8C33AFF";
                    }
                }
                else
                {
                    note = o.Why;
                    act = "未解锁"; actColor = "#8A8070FF";
                }
                string pay = o.Buildable ? (o.Price + " 第纳尔") : "—";
                var yrow = AddRow(NavalBridge.IconOfHullId(o.HullId, o.Tier), o.HullName,
                    NavalBridge.HullTypeName(o.Tier) + "舰",
                    o.Crew.ToString(), ((int)o.HitPoints).ToString(),
                    FleetStatus.PowerOf(o.Crew, o.Value, o.HitPoints, o.Tier, 1f),
                    pay,
                    note, o.Buildable ? "#A89C7ACC" : "#C96A5AFF", false, 0f, act, actColor);
                // v4.279(第 3 项): 记住这一行的稳定 key(现货 = Ship.RandomValue, 下单 = HullId)
                if (yrow != null) yrow.Key = OfferKey(o);
            }
            _rowCount = _total;
            if (_rowCount == 0)
                _emptyYard = "该港口暂无可造舰型 —— 提升造船厂等级(1 级轻 / 2 级中 / 3 级重)或研究海军科技; 也可换一个港口试试";
        }

        // ==================== 页签 2: 封锁 ====================
        private void BuildBlockadeRows(Kingdom k)
        {
            var ports = NavalBridge.EnemyPorts(k);
            // ==================== v4.279(第 5 项): MyShips() 提到循环外 ====================
            //   【原缺陷】原来在**每个敌港**的循环体里调 2 次 `NavalFleet.MyShips()`, 而 MyShips() 是
            //   O(部队数 × 军团数) + 双堆分配。20 敌港 + 300 部队时每次刷新约 12 万次内层比较 + 84 次分配,
            //   每 3 秒一遍。关键是: **这里两次调用的结果本来就是同一个值** —— 循环里没有任何东西会改船。
            //   所以按 "MyShips(); men = Σ Crew; count = .Count" 一次算好, 循环里只读这两个标量。
            List<Ship> myShips = null;
            int men = 0;
            int myShipCount = 0;
            try
            {
                myShips = NavalFleet.MyShips();   // v4.273(审计 #2): 我方口径显式化
                for (int j = 0; j < myShips.Count; j++) men += NavalBridge.Crew(myShips[j]);
                myShipCount = myShips.Count;
            }
            catch { myShips = null; }
            for (int i = 0; i < ports.Count; i++)
            {
                var p = ports[i];
                if (p == null) continue;
                var owner = NavalBridge.KingdomOf(p);
                bool cur = NavalFleet.IsBlockading && NavalFleet.BlockadePortId == p.StringId;
                // v4.271: 第二行给"为什么值得封锁/已经封出多少", 让玩家判断值不值
                string note = cur
                    ? "★ 已封锁 " + Math.Max(0, (int)CampaignTime.Now.ToDays - NavalFleet.BlockadeSinceDay) + " 天 · 累计战果 "
                      + (int)NavalFleet.BlockadeDamage + " 第纳尔 · 再点一次解除"
                    : "封锁逐日削弱该港繁荣度与关税; 停战 / 港口易主 / 我方无船会自动解除";
                var brow = AddBlockRow(cur ? NavalBridge.IconBlockade : NavalBridge.IconPort,
                    NavalBridge.NameOf(p),
                    (owner != null && owner.Name != null ? owner.Name.ToString() : "?") + " · 造船厂 "
                        + NavalBridge.ShipyardLevel(p) + " 级",
                    NavalFleet.ShipsOf(owner).Count.ToString(),
                    men.ToString(),
                    myShipCount.ToString(),
                    ((int)PortProsperity(p)).ToString(),
                    note, cur ? "#7FD8A0FF" : "#A89C7ACC",
                    cur ? "解除" : "封锁",
                    cur ? "#7FD8A0FF" : "#E8C33AFF");
                // v4.279(第 3 项): 封锁行的稳定 key = 港口 StringId(EnemyPorts 每次重算, 行号会跳)
                if (brow != null) brow.Key = "port:" + p.StringId;
            }
            _rowCount = _total;
            if (_rowCount == 0)
                _emptyBlock = "没有可封锁的敌国港口 —— 需要与一个拥有沿海城镇的国家处于战争状态\n封锁前提: 我方有舰船(先到[造船]页备船), 否则封锁会立刻自动解除";
        }

        private static float PortProsperity(Settlement p)
        {
            try { return p != null && p.Town != null ? p.Town.Prosperity : 0f; }
            catch { return 0f; }
        }

        // ==================== 页签 4: 海运(v4.270, v4.271 加三步引导) ====================
        private void BuildTransportRows(Kingdom k)
        {
            var list = NavalTransport.Active;
            int cap = 0;
            try { cap = (int)NavalFleet.CargoCapacity(k); } catch { }
            for (int i = 0; i < list.Count; i++)
            {
                var c = list[i];
                if (c == null) continue;
                var trow = AddTransRow(NavalBridge.IconOfHullId("empire_trade_ship", 0),   // 运输船图标
                    c.LegionName, c.FromPortName + " → " + c.ToPortName,
                    (c.LegionMen + " 人"), (c.ShipsGiven + " 艘"), (cap + " 人"),
                    "约 " + c.EtaDays + " 天",
                    "占用货舱 " + (int)c.CargoUsed + " · 出发第 " + c.DepartDay + " 天 · 海上航行中(到达自动下船并还船)",
                    "#8FD8E8FF", "召回", "#C96A5AFF");
                // v4.279(第 3 项): 运输队行的稳定 key = Convoy.PartyId(Active 会被小时结/日结增删)
                if (trow != null) trow.Key = "convoy:" + c.PartyId;
            }
            _rowCount = _total;
            if (_rowCount == 0)
                _emptyTrans = "我方暂无在途运输队 —— 按上面 ① ② ③ 选好军团与两个港口, 再点底部[装船出发]; 我方在途上限 "
                            + NavalTransport.MaxConvoys + " 支";
        }

        // ==================== 页签 3: 战报 ====================
        private void BuildLogRows()
        {
            var list = NavalFleet.Battles;
            for (int i = 0; i < list.Count; i++)
            {
                string r = list[i];
                if (string.IsNullOrEmpty(r)) continue;
                string title = "海战记录 " + (i + 1);
                string body = r;
                int nl = r.IndexOf('\n');
                if (nl > 0) { title = r.Substring(0, nl); body = r.Substring(nl + 1); }
                // v4.271: 战斗结果标题按胜负着色(胜=绿/败=红), 一眼看出战报成色
                string col = "#E9CD74FF";
                if (title.Contains("胜")) col = "#7FD8A0FF";
                else if (title.Contains("败") || title.Contains("沉")) col = "#D96A5AFF";
                // v4.286: 战报列表与舰船行同样走滚动窗口(否则滚轮在战报页也是"看着没反应")
                var log = new NavyLogVM(title, body, col);
                int abs = _total++;
                if (abs >= _scroll && Logs.Count < LogsPerPage) Logs.Add(log);
            }
            _rowCount = _total;
            if (_rowCount == 0)
                _emptyLogs = "暂无海战记录 —— 到[封锁]页选定敌港后点[出击]发起一场舰队交战, 结果会记在这里\n"
                           + "结算 = 双方舰队战力 × 提督统率 → 胜负 / 沉没 / 战利品, 并计入厌战与财政";
        }

        // ==================== 行按钮动作 ====================
        //   v4.279(第 3 项): 入口从"屏幕行号"改成"稳定 key"。
        //   旧路径 `RowAction(idx)` 保留(兼容可能的旧调用点), 但它内部也改为**先把行号换成 key**,
        //   于是"刷新窗口内行序变化"这个错位问题在两条路径上都不存在。
        internal void RowAction(int idx)
        {
            try
            {
                // v4.286: Rows 已是当前页 -> 下标直接用(旧版的 + _scroll 会双重偏移)
                if (idx < 0 || Rows == null || idx >= Rows.Count) { _status = "该行已不存在(列表已变化) —— 请重试"; return; }
                RowActionByKey(Rows[idx] != null ? Rows[idx].Key : null);
            }
            catch (Exception ex) { _status = "操作失败: " + ex.Message; }
        }

        // 屏幕第 screenRow 行(0 = 列表第一可见行)的稳定 key; 越界/无 key 时返回 null。
        //   NavyPanel.RegisterSpots 用它把 key **捕获进热区闭包**(而不是捕获行号)。
        // ==================== v4.282: 面板热区要读的"行内操作键几何" ====================
        //   渲染时用的是 NavyRowVM.ActX/ActW(绑定进 prefab), 热区这里读**同一把尺子** ——
        //   旧版 prefab 写死 640 而封锁页热区按 632 算(差 8 像素), 现在不可能再错位。
        //   v4.286: 三页(造船/封锁/海运)**统一**到行内 746 w64(面板 772..836)。
        //   旧版是 640 w136 与 632 w64 两套 —— 语义相同的"行内动作键"画在两个不同位置本身就不对,
        //   而且行尾那 4 个像素的偏差正是"点得中但边缘会漏"的来源。现在只剩一组数。
        internal const float RowActionXConst = 746f, RowActionWConst = 64f;
        internal float RowActionWidth { get { return RowActionWConst; } }
        internal float RowActionX { get { return RowActionXConst; } }

        // ==================== v4.282: 五个"点选城市"入口(热区直接接它们) ====================
        internal void PickShipyardPort() { StartPick(PickShipyard); }
        internal void PickEnemyPort() { StartPick(PickEnemy); }
        internal void PickFromPort() { StartPick(PickFrom); }
        internal void PickToPort() { StartPick(PickTo); }

        // 城市详情页的[查看] -> 海军页定位到这支舰队(按 PartyId 稳定反查, 不用行号)
        internal void SelectFleetByKey(string partyId)
        {
            try
            {
                if (string.IsNullOrEmpty(partyId)) return;
                var list = Fleets();
                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i] == null || list[i].PartyId != partyId) continue;
                    _fleetIdx = i;
                    _status = "已定位舰队: " + (list[i].HomePortName ?? "") ;
                    var p = NavalTaskForce.PartyOf(list[i]);
                    if (p != null) { try { MapSelection.Select(p); } catch { } }
                    Touch();
                    return;
                }
                _status = "该舰队已不在编(可能已解散/被歼灭)";
                Touch();
            }
            catch { }
        }

        // v4.286: Rows 里装的就是**当前页**的行(窗口已在构建阶段裁剪), 所以这里直接用 screenRow 索引
        //   —— 旧版写 `screenRow + _scroll` 是在"Rows 装全部行"的前提下才成立, 现在会双重偏移。
        internal string RowKeyAt(int screenRow)
        {
            try
            {
                if (screenRow < 0 || Rows == null || screenRow >= Rows.Count) return null;
                var r = Rows[screenRow];
                return r != null ? r.Key : null;
            }
            catch { return null; }
        }

        // 按稳定 key 执行行内动作(玩家**看到的那一行**的 key)。key 失配时给中文提示, 不静默。
        internal void RowActionByKey(string key)
        {
            try
            {
                if (string.IsNullOrEmpty(key)) { _status = "该行已失效(列表已变化) —— 请重试"; return; }
                var k = Kingdom();
                if (k == null) { _status = "尚未接管王国"; return; }
                if (_tab == 1) ActionShipyard(k, key);
                else if (_tab == 2) ActionBlockade(k, key);
                else if (_tab == 4) ActionTransportRecall(key);
            }
            catch (Exception ex) { _status = "操作失败: " + ex.Message; }
        }

        // ---- 造船页: key = "ship:<RandomValue>"(现货) 或 "hull:<HullId>"(下单建造) ----
        private void ActionShipyard(Kingdom k, string key)
        {
            var p = SelectedPort(k);
            if (p == null) { _status = "我方没有可用港口"; return; }
            var offers = NavalFleet.Offers(p, k);
            NavalFleet.Offer o = null;
            int at = -1;
            for (int i = 0; i < offers.Count; i++)
            {
                if (offers[i] != null && OfferKey(offers[i]) == key) { o = offers[i]; at = i; break; }
            }
            if (o == null)
            {
                // 玩家点的那一行已经不在列表里(船被人买走/港口易主/造船厂降级) -> 明确告知并刷新
                _status = "该舰型已不在当前港口在售列表中(可能已被买走或港口状态变化) —— 已刷新, 请重试";
                MapSelection.Message("海军: " + _status);
                Refresh();
                return;
            }
            if (!o.Buildable) { _status = "不可建造: " + (o != null ? o.Why : ""); return; }

            if (o.Days == 0 && o.Sample != null)
                _status = NavalFleet.BuyShip(p, o.Sample, k);
            else
                _status = NavalFleet.OrderShip(p, o, k);

            DLog.Force("海军页: 造船行 key=" + key + " 命中第 " + at + " 项 -> " + _status);
            InvalidateShipCaches();   // v4.279(第 5 项): 买船/下单后立刻让 MyShips 缓存失效
            MapSelection.Message("海军: " + _status);
            Refresh();
        }

        // 一行"可造船"的稳定 key(渲染与点击两侧共用同一个函数, 保证口径一致)
        private static string OfferKey(NavalFleet.Offer o)
        {
            try
            {
                if (o == null) return null;
                if (o.Days == 0 && o.Sample != null)
                {
                    int rv = 0;
                    try { rv = o.Sample.RandomValue; } catch { }
                    if (rv != 0) return "ship:" + rv;
                }
                return "hull:" + (o.HullId ?? "");
            }
            catch { return null; }
        }

        // ---- 封锁页: key = "port:<Settlement.StringId>" ----
        private void ActionBlockade(Kingdom k, string key)
        {
            string pid = key != null && key.StartsWith("port:", StringComparison.Ordinal) ? key.Substring(5) : null;
            Settlement p = null;
            try { p = string.IsNullOrEmpty(pid) ? null : Settlement.Find(pid); } catch { }
            if (p == null)
            {
                _status = "该港口已不存在 —— 已刷新, 请重试";
                MapSelection.Message("海军: " + _status);
                Refresh();
                return;
            }
            // 港口还在, 但可能已经不在"可封锁"名单里(停战/易主/被我方占领) -> 明确提示, 不误封
            bool stillEnemy = false;
            var ports = NavalBridge.EnemyPorts(k);
            for (int i = 0; i < ports.Count; i++)
                if (ports[i] != null && ports[i].StringId == p.StringId) { stillEnemy = true; break; }
            bool cur = NavalFleet.IsBlockading && NavalFleet.BlockadePortId == p.StringId;
            if (!stillEnemy && !cur)
            {
                _status = "「" + NavalBridge.NameOf(p) + "」已不在可封锁列表中(可能已停战或易主) —— 已刷新, 请重试";
                MapSelection.Message("海军: " + _status);
                Refresh();
                return;
            }
            if (cur) _status = NavalFleet.ClearBlockade();
            else _status = NavalFleet.SetBlockade(p, k);
            DLog.Force("海军页: 封锁行 key=" + key + " -> " + _status);
            MapSelection.Message("海军: " + _status);
            Refresh();
        }

        // ---- 海运页: key = "convoy:<Convoy.PartyId>" ----
        //   NavalTransport.Recall(int) 收的是 Active 的**下标**, 所以这里先按 PartyId 反查下标
        //   —— 反查与调用之间没有别的线程/结算插入(同一个同步点击栈), 不存在二次错位。
        internal void ActionTransportRecall(string key)
        {
            try
            {
                string cid = key != null && key.StartsWith("convoy:", StringComparison.Ordinal) ? key.Substring(7) : null;
                int at = -1;
                if (!string.IsNullOrEmpty(cid))
                {
                    var list = NavalTransport.Active;
                    for (int i = 0; i < list.Count; i++)
                        if (list[i] != null && list[i].PartyId == cid) { at = i; break; }
                }
                if (at < 0)
                {
                    _status = "该运输队已结束(可能已到港/已被召回) —— 已刷新, 请重试";
                    MapSelection.Message("海军: " + _status);
                    Refresh();
                    return;
                }
                DLog.Force("海军页: 海运行 key=" + key + " 命中 Active[" + at + "]");
                _status = NavalTransport.Recall(at);
                MapSelection.Message("海军: " + _status);
                Refresh();
            }
            catch (Exception ex) { _status = "召回失败: " + ex.Message; }
        }

        // v4.279(第 5 项): 改船/改编制之后立刻让 NavalBridge.MyShips() 的批内缓存失效
        //   (缓存窗口只有 10ms, 本来跨帧就会自然过期; 这里是"同一调用栈里改完立刻要读"的保险)
        private static void InvalidateShipCaches()
        {
            try { NavalBridge.InvalidateMyShips(); } catch { }
        }

        // 「出击」: 对当前封锁目标(或最近的一处敌港)发起一场舰队交战(抽象结算)
        internal void Sortie()
        {
            try
            {
                var k = Kingdom();
                if (k == null) { _status = "尚未接管王国"; return; }
                // v4.280(接线): 出击冷却(6 游戏小时) + 补给费(100×参战舰数) —— 堵掉"反复点出击刷战利品"的闭环。
                //   SortieBlockedReason 被挡时返回中文原因、通过时顺手扣补给费并记账, 这里只判非空。
                try
                {
                    string se = NavalFleet.SortieBlockedReason();
                    if (!string.IsNullOrEmpty(se))
                    {
                        _status = se;
                        MapSelection.Message("海军: " + se);
                        Refresh();
                        return;
                    }
                }
                catch (Exception ex) { DLog.Force("出击前置检查异常: " + ex.Message); }
                Settlement target = NavalFleet.BlockadeTarget();
                Kingdom enemy = null;
                if (target != null) enemy = NavalBridge.KingdomOf(target);
                if (enemy == null)
                {
                    var ports = NavalBridge.EnemyPorts(k);
                    if (ports.Count > 0)
                    {
                        int idx = _enemyPortIdx;
                        if (idx < 0 || idx >= ports.Count) idx = 0;
                        target = ports[idx];
                        enemy = NavalBridge.KingdomOf(target);
                    }
                }
                if (enemy == null) { _status = "没有可交战的敌国舰队(需与沿海国家交战)"; return; }
                if (NavalFleet.MyShips().Count == 0) { _status = "我方没有舰船, 先到「造船」页备船"; return; }

                // ==================== v4.276: [出击] 先指挥地图上的实体舰队 ====================
                //   舰队实体化之后, "出击"的第一层含义应该是**让地图上那几支舰队真的开过去** ——
                //   它们到达目标海域后会与敌方舰队/港口巡逻队按**原版遭遇流程**打起来(NavalDLCEncounterModel
                //   的海上遭遇判定距离 1.5), 玩家能在地图上看到战斗发生。
                //   抽象结算(NavalBattle.Resolve -> 战报)仍然保留在后面: 它是"战略层面的战果",
                //   也是没有舰队时唯一可用的路径 —— 两条路都给玩家即时反馈, 但**互不冒充**
                //   (下面的状态串会如实写明"已命令 N 支舰队开往 X")。
                string fleetMsg = "";
                try
                {
                    var fleets = NavalTaskForce.Alive();
                    int ordered = 0;
                    for (int i = 0; i < fleets.Count; i++)
                    {
                        var fp = NavalTaskForce.PartyOf(fleets[i]);
                        if (fp == null) continue;
                        string em = NavalTaskForce.OrderToPort(fp, target, true);
                        if (!string.IsNullOrEmpty(em)) ordered++;
                        DLog.Force("海军: 出击 -> 命令舰队 " + fp.StringId + " : " + em);
                    }
                    if (ordered > 0 && target != null)
                        fleetMsg = "已命令 " + ordered + " 支地图舰队开往「" + NavalBridge.NameOf(target) + "」封锁海域。";
                }
                catch (Exception exF) { DLog.Force("海军: 出击指挥舰队失败 " + exF.Message); }

                var r = NavalBattle.Resolve(k, enemy, target);
                _status = (string.IsNullOrEmpty(fleetMsg) ? "" : (fleetMsg + " "))
                    + (r != null ? r.Headline + "(详见「战报」页)" : "海战结算失败");
                MapSelection.Message("海军: " + _status);
                _tab = 3;    // 跳到战报页看结果
                _scroll = 0;
                Refresh();
                // v4.271: 切页后必须立刻重算热区, 否则最长 3 秒内点击还落在封锁页的旧按钮上
                if (_onTabChanged != null) _onTabChanged();
            }
            catch (Exception ex) { _status = "出击失败: " + ex.Message; }
        }
    }

    public class NavyTabVM : ViewModel
    {
        private readonly string _name;
        private readonly int _idx;
        internal NavyTabVM(string name, int idx) { _name = name; _idx = idx; }
        [DataSourceProperty] public string Name { get { return _name; } }
        [DataSourceProperty] public int Idx { get { return _idx; } }
    }
}
