using System;
using SandBox.View.Map;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.Library;

namespace FeudalInternalAffairs
{
    // v4.75b: "开始游戏"黑屏过渡 —— 点按钮后全屏黑幕(锁输入) -> 接管在幕后完成 -> 淡出
    public class StartFadeVM : ViewModel
    {
        private readonly string _text;
        private float _alpha = 1f;

        internal StartFadeVM(string text) { _text = text; }

        [DataSourceProperty] public string Text { get { return _text; } }

        [DataSourceProperty]
        public float Alpha
        {
            get { return _alpha; }
            set
            {
                if (Math.Abs(_alpha - value) > 0.002f)
                {
                    _alpha = value;
                    OnPropertyChangedWithValue(value, "Alpha");
                    OnPropertyChangedWithValue(FadeColor, "FadeColor");
                }
            }
        }

        // 8 位 RRGGBBAA(全部由 Alpha 控制)
        [DataSourceProperty]
        public string FadeColor
        {
            get
            {
                int a = (int)Math.Round(Math.Max(0f, Math.Min(1f, _alpha)) * 255f);
                return "#000000" + a.ToString("X2");
            }
        }
    }

    internal static class StartGameFade
    {
        private static GauntletLayer _layer;
        private static StartFadeVM _vm;
        private static MapScreen _map;
        private static float _hold;

        internal static void Show(string text)
        {
            try
            {
                var map = MapScreen.Instance;
                if (map == null) return;
                CloseImmediate();
                _vm = new StartFadeVM(text);
                _vm.Alpha = 1f;
                _layer = new GauntletLayer("FeudalStartFade", 350, false);   // v4.75o: 全局最高(盖住国名/面板/导航)
                _layer.LoadMovie("FeudalStartFade", _vm);
                try
                {
                    _layer.InputRestrictions.SetInputRestrictions(true, InputUsageMask.BlockEverythingWithoutHitTest);   // 黑屏期间锁输入
                }
                catch { }
                map.AddLayer(_layer);
                _map = map;
                _hold = 0.7f;   // 全黑保持, 让接管完成
                DLog.Force("开始过渡: 黑幕已显示(" + text + ")");
            }
            catch (Exception ex) { DLog.Force("开始过渡失败: " + ex.Message); }
        }

        // 由 FeudalMapView 每帧调用
        internal static void Tick(float dt)
        {
            try
            {
                if (_vm == null) return;
                if (_hold > 0f) { _hold -= dt; return; }
                float a = _vm.Alpha - dt / 0.8f;
                if (a <= 0f) { DLog.Force("开始过渡: 黑幕已淡出"); CloseImmediate(); return; }
                _vm.Alpha = a;
            }
            catch { }
        }

        private static void CloseImmediate()
        {
            try { if (_layer != null && _map != null) _map.RemoveLayer(_layer); } catch { }
            _layer = null; _vm = null; _map = null;
        }
    }
}
