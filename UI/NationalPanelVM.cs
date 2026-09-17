using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core.ViewModelCollection.ImageIdentifiers;
using TaleWorlds.Library;

namespace FeudalInternalAffairs
{
    // 民族精神槽位(未来填充内容; 现在只显示空框)
    public class SpiritSlotVM : ViewModel
    {
        private string _icon;
        private string _name;
        private bool _filled;

        internal SpiritSlotVM(string icon, string name, bool filled)
        {
            _icon = icon; _name = name; _filled = filled;
        }

        [DataSourceProperty]
        public string Icon { get { return _icon; } set { if (_icon != value) { _icon = value; OnPropertyChangedWithValue(value, "Icon"); } } }

        [DataSourceProperty]
        public string Name { get { return _name; } set { if (_name != value) { _name = value; OnPropertyChangedWithValue(value, "Name"); } } }

        [DataSourceProperty]
        public bool IsFilled { get { return _filled; } set { if (_filled != value) { _filled = value; OnPropertyChangedWithValue(value, "IsFilled"); } } }
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
        private const float PanelWidth = 430f;
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
                _target = 0f;
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

        // ================= 刷新 =================
        internal void Refresh()
        {
            try
            {
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

                // 民族精神(暂为 6 个空槽, 未来填充)
                Spirits.Clear();
                for (int i = 0; i < 6; i++)
                    Spirits.Add(new SpiritSlotVM(null, "民族精神(未开放)", false));
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
