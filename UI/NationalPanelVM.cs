using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core.ViewModelCollection.ImageIdentifiers;
using TaleWorlds.Library;

namespace FeudalInternalAffairs
{
    // 民族精神槽位(v4.150: 真实精神 —— 图标+名称+效果, 来自性格/法律/局势)
    public class SpiritSlotVM : ViewModel
    {
        private string _icon;
        private string _name;
        private bool _filled;
        private string _desc;
        private string _nameColor;

        internal SpiritSlotVM(string icon, string name, bool filled, string desc = null, string nameColor = "#F0E4C8FF")
        {
            _icon = icon; _name = name; _filled = filled; _desc = desc; _nameColor = nameColor;
        }

        [DataSourceProperty]
        public string Icon { get { return _icon; } set { if (_icon != value) { _icon = value; OnPropertyChangedWithValue(value, "Icon"); } } }

        [DataSourceProperty]
        public string Name { get { return _name; } set { if (_name != value) { _name = value; OnPropertyChangedWithValue(value, "Name"); } } }

        [DataSourceProperty]
        public bool IsFilled { get { return _filled; } set { if (_filled != value) { _filled = value; OnPropertyChangedWithValue(value, "IsFilled"); } } }

        [DataSourceProperty]
        public string Desc { get { return _desc; } set { if (_desc != value) { _desc = value; OnPropertyChangedWithValue(value, "Desc"); } } }

        [DataSourceProperty]
        public string NameColor { get { return _nameColor; } set { if (_nameColor != value) { _nameColor = value; OnPropertyChangedWithValue(value, "NameColor"); } } }
    }

    // 国家面板(左上角国旗 -> 侧边栏)
    // 布局参考 HOI4: 国旗+国名 / 民族精神 / 国策 / 国家概况 / 入口按钮
    public class NationalPanelVM : ViewModel
    {
        private bool _open;
        private string _kingdomName = "";
        private string _rulerName = "";
        private string _flagColor = "#3B2E1EFF";
        private string _focusName = "无";
        private string _focusDays = "";
        private string _gold = "0";
        private string _army = "0";
        private string _towns = "0";
        private string _influence = "0";
        private ImageIdentifierVM _flag;

        internal NationalPanelVM()
        {
            Spirits = new MBBindingList<SpiritSlotVM>();
            RebuildFlag();
            Refresh();
        }

        [DataSourceProperty]
        public MBBindingList<SpiritSlotVM> Spirits { get; private set; }

        [DataSourceProperty]
        public ImageIdentifierVM Flag
        {
            get { return _flag; }
            set { if (_flag != value) { _flag = value; OnPropertyChangedWithValue(value, "Flag"); } }
        }

        // ImageIdentifierWidget 需要这三个(原版 prefab 都是显式绑定的)
        [DataSourceProperty]
        public string Id { get { try { return _flag != null ? _flag.Id : ""; } catch { return ""; } } }

        [DataSourceProperty]
        public string AdditionalArgs { get { try { return _flag != null ? _flag.AdditionalArgs : ""; } catch { return ""; } } }

        [DataSourceProperty]
        public string TextureProviderName { get { try { return _flag != null ? _flag.TextureProviderName : ""; } catch { return ""; } } }

        // ===== 滑入/滑出动画 =====
        private const float PanelWidth = 520f;   // v4.150: 面板加宽(440 -> 520)
        private float _offset = -PanelWidth;
        private float _target = -PanelWidth;
        private bool _visible;
        private bool _animLogged;

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

        // 每帧推进动画(由 NationalPanel.Tick 调用)
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
                    float speed = PanelWidth / 0.12f;   // 0.12 秒滑完
                    float step = speed * Math.Max(0.001f, Math.Min(dt, 0.1f));
                    float next = _offset + (_target > _offset ? step : -step);
                    if ((_target > _offset && next > _target) || (_target < _offset && next < _target)) next = _target;
                    PanelOffset = next;
                }
                if (_target > -PanelWidth + 1f) IsPanelVisible = true;
                else if (Math.Abs(_offset - _target) < 1f) IsPanelVisible = false;
            }
            catch { }
        }

        [DataSourceProperty]
        public string FlagColor
        {
            get { return _flagColor; }
            set { if (_flagColor != value) { _flagColor = value; OnPropertyChangedWithValue(value, "FlagColor"); } }
        }

        [DataSourceProperty]
        public bool IsPanelOpen
        {
            get { return _open; }
            set { if (_open != value) { _open = value; OnPropertyChangedWithValue(value, "IsPanelOpen"); } }
        }

        [DataSourceProperty]
        public string KingdomName
        {
            get { return _kingdomName; }
            set { if (_kingdomName != value) { _kingdomName = value; OnPropertyChangedWithValue(value, "KingdomName"); } }
        }

        [DataSourceProperty]
        public string RulerName
        {
            get { return _rulerName; }
            set { if (_rulerName != value) { _rulerName = value; OnPropertyChangedWithValue(value, "RulerName"); } }
        }

        [DataSourceProperty]
        public string FocusName
        {
            get { return _focusName; }
            set { if (_focusName != value) { _focusName = value; OnPropertyChangedWithValue(value, "FocusName"); } }
        }

        [DataSourceProperty]
        public string FocusDays
        {
            get { return _focusDays; }
            set { if (_focusDays != value) { _focusDays = value; OnPropertyChangedWithValue(value, "FocusDays"); } }
        }

        [DataSourceProperty]
        public string Gold { get { return _gold; } set { if (_gold != value) { _gold = value; OnPropertyChangedWithValue(value, "Gold"); } } }

        [DataSourceProperty]
        public string Army { get { return _army; } set { if (_army != value) { _army = value; OnPropertyChangedWithValue(value, "Army"); } } }

        [DataSourceProperty]
        public string Towns { get { return _towns; } set { if (_towns != value) { _towns = value; OnPropertyChangedWithValue(value, "Towns"); } } }

        [DataSourceProperty]
        public string Influence { get { return _influence; } set { if (_influence != value) { _influence = value; OnPropertyChangedWithValue(value, "Influence"); } } }

        // v3.0: 人口 / 平均生活水平(文档 19.14.4)
        private string _popTotal = "—", _popSol = "—";
        [DataSourceProperty]
        public string PopTotal { get { return _popTotal; } set { if (_popTotal != value) { _popTotal = value; OnPropertyChangedWithValue(value, "PopTotal"); } } }
        [DataSourceProperty]
        public string PopSol { get { return _popSol; } set { if (_popSol != value) { _popSol = value; OnPropertyChangedWithValue(value, "PopSol"); } } }

        private string _poolText = "—";
        [DataSourceProperty]
        public string PoolText { get { return _poolText; } set { if (_poolText != value) { _poolText = value; OnPropertyChangedWithValue(value, "PoolText"); } } }

        // ===== v4.150: 国家总页扩展(政治/社会/军事/外交/研究) =====
        private string _authText = "—", _legitText = "—", _tyrannyText = "—";
        private string _literacyText = "—", _stanceText = "—";
        private string _wearText = "—", _defArmyText = "—";
        private string _budgetText = "—", _mintText = "—", _tradeText = "—";
        private string _diploText = "—", _blocText = "—";
        private string _researchText = "—", _researchProg = "—";
        private string _statusText = "—", _statusColor = "#C8B98FFF";

        [DataSourceProperty] public string AuthText { get { return _authText; } set { if (_authText != value) { _authText = value; OnPropertyChangedWithValue(value, "AuthText"); } } }
        [DataSourceProperty] public string LegitText { get { return _legitText; } set { if (_legitText != value) { _legitText = value; OnPropertyChangedWithValue(value, "LegitText"); } } }
        [DataSourceProperty] public string TyrannyText { get { return _tyrannyText; } set { if (_tyrannyText != value) { _tyrannyText = value; OnPropertyChangedWithValue(value, "TyrannyText"); } } }
        [DataSourceProperty] public string LiteracyText { get { return _literacyText; } set { if (_literacyText != value) { _literacyText = value; OnPropertyChangedWithValue(value, "LiteracyText"); } } }
        [DataSourceProperty] public string StanceText { get { return _stanceText; } set { if (_stanceText != value) { _stanceText = value; OnPropertyChangedWithValue(value, "StanceText"); } } }
        [DataSourceProperty] public string WearText { get { return _wearText; } set { if (_wearText != value) { _wearText = value; OnPropertyChangedWithValue(value, "WearText"); } } }
        [DataSourceProperty] public string DefArmyText { get { return _defArmyText; } set { if (_defArmyText != value) { _defArmyText = value; OnPropertyChangedWithValue(value, "DefArmyText"); } } }
        [DataSourceProperty] public string BudgetText { get { return _budgetText; } set { if (_budgetText != value) { _budgetText = value; OnPropertyChangedWithValue(value, "BudgetText"); } } }
        [DataSourceProperty] public string MintText { get { return _mintText; } set { if (_mintText != value) { _mintText = value; OnPropertyChangedWithValue(value, "MintText"); } } }
        [DataSourceProperty] public string TradeText { get { return _tradeText; } set { if (_tradeText != value) { _tradeText = value; OnPropertyChangedWithValue(value, "TradeText"); } } }
        [DataSourceProperty] public string DiploText { get { return _diploText; } set { if (_diploText != value) { _diploText = value; OnPropertyChangedWithValue(value, "DiploText"); } } }
        [DataSourceProperty] public string BlocText { get { return _blocText; } set { if (_blocText != value) { _blocText = value; OnPropertyChangedWithValue(value, "BlocText"); } } }
        [DataSourceProperty] public string ResearchText { get { return _researchText; } set { if (_researchText != value) { _researchText = value; OnPropertyChangedWithValue(value, "ResearchText"); } } }
        [DataSourceProperty] public string ResearchProg { get { return _researchProg; } set { if (_researchProg != value) { _researchProg = value; OnPropertyChangedWithValue(value, "ResearchProg"); } } }
        [DataSourceProperty] public string StatusText { get { return _statusText; } set { if (_statusText != value) { _statusText = value; OnPropertyChangedWithValue(value, "StatusText"); } } }
        [DataSourceProperty] public string StatusColor { get { return _statusColor; } set { if (_statusColor != value) { _statusColor = value; OnPropertyChangedWithValue(value, "StatusColor"); } } }

        // ================= 命令 =================
        public void ExecuteToggle()
        {
            try
            {
                if (IsPanelOpen)
                {
                    // 开着 -> 收起(屏幕方案: 要把整个屏幕关掉, 否则会出现"面板没了但屏幕还在"的诡异状态)
                    if (OnCloseRequested != null) OnCloseRequested();
                    else ClosePanel();
                }
                else
                {
                    if (OnOpenRequested != null) OnOpenRequested();
                    else OpenPanel();
                }
            }
            catch (Exception ex) { DLog.Force("国旗点击异常: " + ex.Message); }
        }

        // 屏幕方案: 开/关都交给 PanelScreen
        internal Action OnOpenRequested;

        internal void OpenPanel()
        {
            try
            {
                if (IsPanelOpen) return;
                IsPanelOpen = true;
                _target = 64f;   // 停靠在左侧导航栏右侧
                IsPanelVisible = true;
                Refresh();
                DLog.Force("国家面板: 展开");
            }
            catch { }
        }

        internal void ClosePanel()
        {
            try
            {
                if (!IsPanelOpen) return;
                IsPanelOpen = false;
                _target = -PanelWidth;
                DLog.Force("国家面板: 收起");
            }
            catch { }
        }

        public void ExecuteClose()
        {
            try
            {
                if (OnCloseRequested != null) OnCloseRequested();   // 由屏幕关闭(PushScreen 方案)
                else ClosePanel();
            }
            catch { }
        }

        // 屏幕方案: 点"收起"要求把整个面板屏幕关掉
        internal Action OnCloseRequested;

        public void ExecuteFocus()
        {
            try { FocusTreeScreen.Open(); }
            catch (Exception ex) { DLog.Force("打开国策树失败: " + ex.Message); }
        }

        public void ExecuteBuild()
        {
            try { BatchBuild.Enter(); }
            catch (Exception ex) { DLog.Force("进入建造模式失败: " + ex.Message); }
        }

        public void ExecuteDiplomacy()
        {
            try { DiplomacyPanel.Open(null); }
            catch (Exception ex) { DLog.Force("打开外交面板失败: " + ex.Message); }
        }

        public void ExecuteMarket()
        {
            try { MarketPanel.Open(null); }
            catch (Exception ex) { DLog.Force("打开市场面板失败: " + ex.Message); }
        }

        public void ExecutePopulation()
        {
            try { PopPanel.Open(); }
            catch (Exception ex) { DLog.Force("打开人口面板失败: " + ex.Message); }
        }

        // v4.150: 国家总页直达入口
        public void ExecutePolitics()
        {
            try { PoliticsPanel.Open(); }
            catch (Exception ex) { DLog.Force("打开政治页失败: " + ex.Message); }
        }

        public void ExecuteArmy()
        {
            try { ArmyPanel.Open(); }
            catch (Exception ex) { DLog.Force("打开军务页失败: " + ex.Message); }
        }

        public void ExecuteFiscal()
        {
            try { FiscalPanel.Open(); }
            catch (Exception ex) { DLog.Force("打开财政页失败: " + ex.Message); }
        }

        public void ExecuteSociety()
        {
            try { SocietyPanel.Open(); }
            catch (Exception ex) { DLog.Force("打开社会页失败: " + ex.Message); }
        }

        // ================= 刷新 =================
        internal void Refresh()
        {
            try
            {
                // v3.0: 人口 + 平均生活水平(文档 19.14.4)
                try
                {
                    float ttl = 0f, solNum = 0f;
                    foreach (var pv in Pops.BySettlement)
                    {
                        var pl = pv.Value;
                        if (pl == null) continue;
                        for (int i = 0; i < pl.Count; i++)
                        {
                            var pp = pl[i];
                            if (pp == null) continue;
                            ttl += pp.Size; solNum += pp.WealthLevel * pp.Size;
                        }
                    }
                    if (ttl > 0f)
                    {
                        PopTotal = ((int)ttl).ToString("N0") + " 人";
                        float sol = solNum / ttl;
                        PopSol = sol.ToString("F1") + " " + PopStrataVM.SolLabel(sol);
                    }
                    var pk = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                    PoolText = pk != null ? ((int)InvestmentPool.Of(pk.StringId)).ToString("N0") : "—";
                }
                catch { }

                var b = NationalWillOrders.Behavior;
                var k = b != null ? b.NationKingdom : null;
                if (k == null) return;

                KingdomName = k.Name != null ? k.Name.ToString() : "";
                // 领袖 = 开档时记录的原国家元首(主角已改名为他, 以后就用这个)
                Hero ruler = b != null ? b.OriginalRuler : null;
                RulerName = ruler != null && ruler.Name != null ? ruler.Name.ToString() : "";
                try { FlagColor = "#" + k.Color.ToString("X8"); } catch { }

                // 国旗(横幅)
                if (_flag == null) RebuildFlag();

                // 国策
                string curId = null;
                foreach (var kv in FocusTreeData.InProgress) { curId = kv.Key; break; }
                if (curId != null)
                {
                    var def = FocusTreeData.Get(curId);
                    FocusName = def != null ? def.Name : curId;
                    FocusDays = "剩余 " + FocusTreeData.InProgress[curId] + " 天";
                }
                else
                {
                    FocusName = "无进行中的国策";
                    FocusDays = "已完成 " + FocusTreeData.Completed.Count + " 项";
                }

                // 概况
                Gold = (Hero.MainHero != null ? Hero.MainHero.Gold : 0).ToString();
                int towns = 0, castles = 0, villages = 0;
                long troops = 0;
                try
                {
                    foreach (var s in k.Settlements)
                    {
                        if (s == null) continue;
                        if (s.IsTown) towns++;
                        else if (s.IsCastle) castles++;
                        else if (s.IsVillage) villages++;
                    }
                    foreach (var p in MobileParty.All)
                    {
                        if (p == null || !p.IsActive) continue;
                        if (p.MapFaction != k) continue;
                        try { troops += p.MemberRoster != null ? p.MemberRoster.TotalManCount : 0; } catch { }
                    }
                }
                catch { }
                Towns = towns + " 城 / " + castles + " 堡 / " + villages + " 村";
                Army = troops.ToString();
                try { Influence = Clan.PlayerClan != null ? ((int)Clan.PlayerClan.Influence).ToString() : "0"; } catch { }

                // 民族精神(v4.150: 真实精神 —— 性格/法律/局势派生, 效果真实生效)
                Spirits.Clear();
                try
                {
                    int today = AiDiplomacy.Today();
                    var sps = NationalSpirits.Of(k, today);
                    for (int i = 0; i < 6; i++)
                    {
                        if (i < sps.Count) Spirits.Add(new SpiritSlotVM(sps[i].Icon, sps[i].Name, true, sps[i].Desc));
                        else Spirits.Add(new SpiritSlotVM(null, "", false, null));
                    }
                }
                catch { }

                // ===== v4.150: 国家总页扩展(政治/社会/军事/经济/外交/研究/状态) =====
                try
                {
                    AuthText = ((int)Politics.Authority).ToString();
                    float lg = Politics.Legitimacy;
                    string lv = lg >= 90f ? "正统" : (lg >= 75f ? "合法" : (lg >= 50f ? "争议" : (lg >= 25f ? "虚弱" : "非法")));
                    LegitText = ((int)lg) + " " + lv;
                    TyrannyText = ((int)Politics.Tyranny).ToString();
                }
                catch { }
                try
                {
                    LiteracyText = ((int)Math.Round(Research.LiteracyPct())) + "%";
                    float loy = 0f, rad = 0f, tot = 0f;
                    foreach (var pv in Pops.BySettlement)
                    {
                        var pl = pv.Value;
                        if (pl == null) continue;
                        for (int i = 0; i < pl.Count; i++)
                        {
                            var pp = pl[i];
                            if (pp == null || pp.Size < 0.5f) continue;
                            tot += pp.Size; loy += pp.Loyalty * pp.Size; rad += pp.Radicalism * pp.Size;
                        }
                    }
                    if (tot > 0f)
                        StanceText = "忠 " + (int)Math.Round(loy / tot * 100f) + "% · 激 " + (int)Math.Round(rad / tot * 100f) + "%";
                }
                catch { }
                try
                {
                    WearText = ((int)WarWeariness.MaxWearOf(k)) + "%";
                    DefArmyText = DefArmy.TotalMen().ToString("N0");
                }
                catch { }
                try
                {
                    // v4.252: 关税(Fiscal.LastTariff)是**支出**(进口关税由国库承担, 见 MarketSim.SpendGold),
                    //   原来放在收入侧 -> 与国家面板/财政页的净额相差 2×关税。现在两边口径统一。
                    int inc = Fiscal.LastTax + Fiscal.LastMint + Fiscal.LastExport + Fiscal.LastDividend;
                    int exp = Fiscal.LastInterest + Fiscal.LastFee + Fiscal.LastMilitary + Fiscal.LastCourt + Fiscal.LastBurn + Fiscal.LastTariff + Fiscal.LastUpkeep;
                    int net = inc - exp;
                    BudgetText = (net >= 0 ? "+" : "") + net.ToString("N0") + " / 日";
                    int pu = MintRight.Purity < 0 ? 0 : (MintRight.Purity > 2 ? 2 : MintRight.Purity);
                    MintText = MintRight.PurityNames[pu];
                    TradeText = TradeRoutes.Routes.Count + " 条";
                }
                catch { }
                try
                {
                    int allies = 0, enemies = 0;
                    foreach (var x in Kingdom.All)
                    {
                        if (x == null || x.IsEliminated || ReferenceEquals(x, k)) continue;
                        try { if (Diplomacy.IsAlly(k, x)) allies++; } catch { }
                        try { if (k.IsAtWarWith(x)) enemies++; } catch { }
                    }
                    bool sanc = false;
                    try { sanc = WarEconomy.IsSanctioned(k); } catch { }
                    DiploText = "盟友 " + allies + " · 交战 " + enemies + (sanc ? " · 被制裁" : "");
                    try
                    {
                        BlocText = PowerBlocs.Founded
                            ? ((string.IsNullOrEmpty(PowerBlocs.Name) ? "权力集团" : PowerBlocs.Name) + "(" + (int)PowerBlocs.Cohesion + ")")
                            : "未创建";
                    }
                    catch { BlocText = "—"; }
                }
                catch { }
                try
                {
                    ResearchText = Research.ProgressText(0);
                    ResearchProg = Research.ProgressText(1);
                }
                catch { }
                try
                {
                    bool civil = Politics.CivilWar;
                    bool crisis = WarEconomy.IsCrisis(k);
                    int wars = 0;
                    foreach (var x in Kingdom.All)
                    {
                        if (x == null || x.IsEliminated || ReferenceEquals(x, k)) continue;
                        try { if (k.IsAtWarWith(x)) wars++; } catch { }
                    }
                    if (civil) { StatusText = "内战中"; StatusColor = "#FFC46BFF"; }
                    else if (crisis) { StatusText = "危机(饥荒/破产/兵源)"; StatusColor = "#FFC46BFF"; }
                    else if (wars > 0) { StatusText = "战争(" + wars + " 线)"; StatusColor = "#FFD98AFF"; }
                    else { StatusText = "和平"; StatusColor = "#9CE88AFF"; }
                }
                catch { }
            }
            catch (Exception ex) { DLog.Info("国家面板刷新异常: " + ex.Message); }
        }

        private void RebuildFlag()
        {
            try
            {
                var b = NationalWillOrders.Behavior;
                var k = b != null ? b.NationKingdom : null;
                if (k == null || k.Banner == null) return;
                Flag = new BannerImageIdentifierVM(k.Banner, false);
            }
            catch (Exception ex) { DLog.Info("国旗生成失败: " + ex.Message); }
        }
    }
}
