using System;
using SandBox.View.Map;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.Library;

namespace FeudalInternalAffairs
{
    // v4.77: 地图国名/数值标签独立顶层 —— 9 个固定标签直绑属性(不走 ItemTemplate)
    //   自建 GauntletLayer 里"模板 items 的 PositionXOffset"不生效(全部堆容器原点),
    //   改为与国旗层同款的"直绑属性 + Margin 定位"方案(已验证可靠)。
    public class FeudalMapLabelsVM : ViewModel
    {
        private readonly TintLabelVM[] _lines = new TintLabelVM[9];

        public FeudalMapLabelsVM()
        {
            for (int i = 0; i < _lines.Length; i++) _lines[i] = new TintLabelVM();
            RecalcPanelWidth();
        }

        [DataSourceProperty] public TintLabelVM L0 { get { return _lines[0]; } }
        [DataSourceProperty] public TintLabelVM L1 { get { return _lines[1]; } }
        [DataSourceProperty] public TintLabelVM L2 { get { return _lines[2]; } }
        [DataSourceProperty] public TintLabelVM L3 { get { return _lines[3]; } }
        [DataSourceProperty] public TintLabelVM L4 { get { return _lines[4]; } }
        [DataSourceProperty] public TintLabelVM L5 { get { return _lines[5]; } }
        [DataSourceProperty] public TintLabelVM L6 { get { return _lines[6]; } }
        [DataSourceProperty] public TintLabelVM L7 { get { return _lines[7]; } }
        [DataSourceProperty] public TintLabelVM L8 { get { return _lines[8]; } }

        // v4.79: 选国阶段用"遮挡块"隐藏原版时间盘(纯自建层方案: 不碰原版 VM/widget)
        internal const string PickHintText = "点击地图上的领地查看国家 · 选好后点下方按钮正式开始";
        private bool _hideTimePanel;
        [DataSourceProperty]
        public bool HideTimePanel
        {
            get { return _hideTimePanel; }
            set { if (_hideTimePanel != value) { _hideTimePanel = value; OnPropertyChangedWithValue(value, "HideTimePanel"); } }
        }

        // v4.79c: 横幅宽度按文字动态计算(中文字号 19 -> 每字约 19px, 左右内边距 60)
        private float _panelWidth = 520f;
        [DataSourceProperty]
        public float PanelWidth
        {
            get { return _panelWidth; }
            set { if (Math.Abs(_panelWidth - value) > 0.5f) { _panelWidth = value; OnPropertyChangedWithValue(value, "PanelWidth"); } }
        }

        internal void RecalcPanelWidth()
        {
            try
            {
                float w = PickHintText.Length * 19f + 88f;   // v4.82: 内边距 64->88, 覆盖时间盘两侧按钮的悬停区
                if (w < 460f) w = 460f;
                PanelWidth = w;
            }
            catch { }
        }

        private bool _loggedSample;
        private float _diagTimer;
        private int _diagLines;

        // v4.80: 按"国家 Id"固定槽位映射 —— 标签列表顺序/内容变动时不再按索引错位(根治"标签换国"式大跳)
        private readonly System.Collections.Generic.Dictionary<string, int> _slotById = new System.Collections.Generic.Dictionary<string, int>();
        private readonly System.Collections.Generic.HashSet<string> _alive = new System.Collections.Generic.HashSet<string>();

        internal void SyncAll(float dt)
        {
            try
            {
                var src = TerritoryColorMode.Labels;

                // 1) 把每个标签按其 Id 分配固定槽位(最多 9 个)
                _alive.Clear();
                for (int i = 0; i < src.Count; i++)
                {
                    string id = src[i].Id != null ? src[i].Id : ("idx" + i);
                    int slot;
                    if (!_slotById.TryGetValue(id, out slot))
                    {
                        slot = -1;
                        for (int s = 0; s < _lines.Length; s++)
                        {
                            bool used = false;
                            foreach (var kv in _slotById) { if (kv.Value == s) { used = true; break; } }
                            if (!used) { slot = s; break; }
                        }
                        if (slot < 0) slot = 0;   // 超出 9 个时的兜底
                        _slotById[id] = slot;
                    }
                    _alive.Add(id);
                    var t = _lines[slot];
                    if (t == null) continue;
                    var s2 = src[i];
                    bool valid = !(Math.Abs(s2.X) < 0.5f && Math.Abs(s2.Y) < 0.5f);
                    SetLine(t, valid, valid ? s2.Text : "", valid ? s2.X : 0f, valid ? s2.Y : 0f, s2.Color, s2.FontSize);
                }

                // 2) 清空不再出现的槽位
                for (int s = 0; s < _lines.Length; s++)
                {
                    bool used = false;
                    foreach (var kv in _slotById) { if (kv.Value == s && _alive.Contains(kv.Key)) { used = true; break; } }
                    if (!used) SetLine(_lines[s], false, "", 0f, 0f, null, 0);
                }
                // 清理已消失 Id 的槽位记录
                {
                    var dead = new System.Collections.Generic.List<string>();
                    foreach (var kv in _slotById) if (!_alive.Contains(kv.Key)) dead.Add(kv.Key);
                    for (int i = 0; i < dead.Count; i++) _slotById.Remove(dead[i]);
                }

                // v4.79l: 诊断采样(最多 20 行, 每行 1 帧; v4.79m 输出全部标签与相机位置, 定位"标签换国"问题)
                if (_diagLines < 20 && src.Count > 0 && (NationPickMode.Active || NationalWillOrders.IsActive))
                {
                    try
                    {
                        var v = NationalWillCamera.View;
                        float cx = 0f;
                        if (v != null)
                        {
                            var t = NationalWillCamera.GetIdealTarget(v);
                            cx = t.x;
                        }
                        var sb = new System.Text.StringBuilder();
                        sb.Append("相机X=").Append(cx.ToString("F0")).Append(" | ");
                        for (int i = 0; i < src.Count; i++)
                            sb.Append(src[i].Text).Append(':').Append(src[i].X.ToString("F0")).Append("  ");
                        _diagLines++;
                        DLog.Force("标签诊断: " + sb.ToString());
                    }
                    catch { }
                }

                // 诊断(数据就绪后延迟打点一次)
                if (!_loggedSample)
                {
                    _diagTimer += dt;
                    if (_diagTimer >= 4f)
                    {
                        _loggedSample = true;
                        string labelInfo = "无";
                        if (src.Count > 0)
                            labelInfo = "共" + src.Count + "个 首个(x=" + src[0].X.ToString("F0")
                                + " y=" + src[0].Y.ToString("F0") + " 文本=" + src[0].Text + ")";
                        DLog.Force("地图标签诊断(v4.79): " + labelInfo);
                    }
                }

                // 选国阶段: 遮挡原版时间盘(接管后自动露出)
                bool hideTime = NationPickMode.Active && !NationalWillOrders.IsActive;
                if (HideTimePanel != hideTime) HideTimePanel = hideTime;
            }
            catch { }
        }

        private static void SetLine(TintLabelVM t, bool show, string text, float x, float y, string color, int fs)
        {
            try
            {
                if (t == null) return;
                if (t.LabelText != text) t.LabelText = text;
                if (show)
                {
                    // v4.79h: 回退滤波, 改为整数像素对齐(消除亚像素抖动; 位置序列变为稳定的整像素步进)
                    x = (float)Math.Round(x);
                    y = (float)Math.Round(y);
                    if (Math.Abs(t.LabelX - x) > 0.05f) t.LabelX = x;
                    if (Math.Abs(t.LabelY - y) > 0.05f) t.LabelY = y;
                }
                else
                {
                    if (Math.Abs(t.LabelX) > 0.05f) t.LabelX = 0f;
                    if (Math.Abs(t.LabelY) > 0.05f) t.LabelY = 0f;
                }
                if (show && color != null && t.LabelColor != color) t.LabelColor = color;
                if (show && fs > 0 && t.LabelFontSize != fs) t.LabelFontSize = fs;
            }
            catch { }
        }
    }

    internal static class FeudalMapLabels
    {
        private static GauntletLayer _layer;
        private static FeudalMapLabelsVM _vm;
        private static MapScreen _map;

        // v4.79i: 供名牌 VM 每帧时机调用(与城市名牌同帧同序同步, 根治移动抖动)
        internal static void SyncNow()
        {
            try { if (_vm != null) _vm.SyncAll(0f); }
            catch { }
        }

        // 由 FeudalMapView 每帧调用
        internal static void Tick(float dt)
        {
            try
            {
                var map = MapScreen.Instance;
                if (map == null)
                {
                    if (_layer != null) Close();
                    return;
                }
                if (_layer != null && !ReferenceEquals(_map, map)) Close();   // 地图屏重建(游戏内读档等)
                if (_layer == null)
                {
                    if (!NationalWillOrders.ShouldControlCamera) return;   // 未接管且非选国: 不挂
                    _vm = new FeudalMapLabelsVM();
                    _layer = new GauntletLayer("FeudalMapLabels", 335, false);
                    _layer.LoadMovie("FeudalMapLabels", _vm);
                    try
                    {
                        // v4.80: 让横幅参与命中测试(挡住原版时间盘的鼠标悬停), 其他区域仍穿透
                        _layer.InputRestrictions.SetInputRestrictions(true, InputUsageMask.BlockEverythingWithoutHitTest);
                    }
                    catch { }
                    map.AddLayer(_layer);
                    _map = map;
                    DLog.Force("地图标签层: 已挂到地图(深度335, 直绑模式)");
                }
                if (_vm != null) _vm.SyncAll(dt);
            }
            catch { }
        }

        private static void Close()
        {
            try { if (_layer != null && _map != null) _map.RemoveLayer(_layer); } catch { }
            _layer = null; _vm = null; _map = null;
        }
    }
}
