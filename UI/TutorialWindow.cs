using System;
using System.Collections.Generic;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using SandBox.View.Map;

namespace FeudalInternalAffairs
{
    // v4.265: 正文的一行(供 prefab 的 ListPanel 绑定)
    internal class TutLineVM : ViewModel
    {
        private string _text = "";
        [DataSourceProperty] public string Text { get { return _text; } }
        internal TutLineVM(string s) { _text = s ?? ""; }
    }

    // 教程浮窗的数据源(位置也在这里, 供 prefab 的 MarginLeft/MarginTop 绑定)
    internal class TutorialWindowVM : PanelVMBase
    {
        private string _title = "";
        private string _progress = "";
        private string _btnA = "";
        private string _btnB = "";
        private string _btnC = "";
        private float _x = 420f;
        private float _y = 200f;

        private readonly MBBindingList<TutLineVM> _lines = new MBBindingList<TutLineVM>();

        public TutorialWindowVM() : base() { }

        [DataSourceProperty] public string Title { get { return _title; } }
        [DataSourceProperty] public MBBindingList<TutLineVM> Lines { get { return _lines; } }
        [DataSourceProperty] public string Progress { get { return _progress; } }
        [DataSourceProperty] public string BtnAText { get { return _btnA; } }
        [DataSourceProperty] public string BtnBText { get { return _btnB; } }
        [DataSourceProperty] public string BtnCText { get { return _btnC; } }
        [DataSourceProperty] public float WinX { get { return _x; } }
        [DataSourceProperty] public float WinY { get { return _y; } }

        internal float X { get { return _x; } }
        internal float Y { get { return _y; } }

        internal void SetText(string title, string progress, string btnA, string btnB, string btnC)
        {
            try
            {
                _title = title ?? ""; _progress = progress ?? "";
                _btnA = btnA ?? ""; _btnB = btnB ?? ""; _btnC = btnC ?? "";
                OnPropertyChangedWithValue(_title, "Title");
                OnPropertyChangedWithValue(_progress, "Progress");
                OnPropertyChangedWithValue(_btnA, "BtnAText");
                OnPropertyChangedWithValue(_btnB, "BtnBText");
                OnPropertyChangedWithValue(_btnC, "BtnCText");
            }
            catch { }
        }

        internal void SetPos(float x, float y)
        {
            try
            {
                if (x < -100f) x = -100f;
                if (y < -40f) y = -40f;
                _x = x; _y = y;
                OnPropertyChangedWithValue(_x, "WinX");
                OnPropertyChangedWithValue(_y, "WinY");
            }
            catch { }
        }

        internal void FillLines(List<string> src, int from, int count)
        {
            try
            {
                _lines.Clear();
                if (src == null) return;
                for (int i = from; i < src.Count && i < from + count; i++)
                    _lines.Add(new TutLineVM(src[i]));
            }
            catch { }
        }
    }

    // ==================== 教程浮窗 ====================
    //   自定义 UI / 可拖动 / 鼠标在窗上时**三个键 + 滚轮**全部拦截(不穿透到地图)
    //   正文按行显示, 滚轮翻看(v4.265: 字体放大 + 滚动 + 滚轮拦截)
    internal static class TutorialWindow
    {
        private const string Movie = "FeudalTutorial";
        internal const float W = 570f, H = 360f;
        private const float BtnY = 304f, BtnH = 40f;
        private const float BtnAX = 26f, BtnBX = 206f, BtnCX = 386f, BtnW = 164f;
        private const float CloseX = 522f, CloseY = 17f, CloseW = 32f, CloseH = 30f;
        private const int VisibleLines = 7;      // 正文区 200 高 / 行高 26

        private static TutorialWindowVM _vm;
        private static GauntletLayer _layer;
        private static bool _wasDown, _dragging;
        private static float _grabX, _grabY;
        private static Action _actA, _actB, _actC;
        private static int _shownCount, _clickCount;
        private static readonly List<string> _allLines = new List<string>();
        private static int _scroll;

        internal static bool IsShown { get { return _layer != null && _vm != null; } }

        internal static void Show(string title, string body, string progress, string btnA, string btnB, string btnC,
            Action actA, Action actB, Action actC)
        {
            try
            {
                if (!IsShown)
                {
                    var map = MapScreen.Instance;
                    if (map == null) return;
                    _vm = new TutorialWindowVM();
                    _layer = new GauntletLayer(Movie, 360, false);
                    _layer.LoadMovie(Movie, _vm);
                    try
                    {
                        _layer.InputRestrictions.SetInputRestrictions(true,
                            TaleWorlds.Library.InputUsageMask.BlockEverythingWithoutHitTest);
                    }
                    catch { }
                    map.AddLayer(_layer);
                    float sw = 1920f;
                    try { sw = PanelScreen.ScreenWidth(); } catch { }
                    _vm.SetPos(Math.Max(40f, (sw - W) * 0.5f), 190f);
                    _shownCount++;
                    DLog.Force("教程窗: 创建 layer 成功 (第 " + _shownCount + " 次)");
                }
                _vm.SetText(title, progress, btnA, btnB, btnC);
                _actA = actA; _actB = actB; _actC = actC;
                WrapLines(body);
                RegisterSpots();
            }
            catch (Exception ex) { DLog.Force("教程窗打开失败: " + ex.Message); }
        }

        internal static void Hide()
        {
            try
            {
                if (_layer != null && MapScreen.Instance != null) MapScreen.Instance.RemoveLayer(_layer);
            }
            catch { }
            _layer = null; _vm = null; _dragging = false;
            _actA = _actB = _actC = null;
            _allLines.Clear(); _scroll = 0;
            try { PanelScreen.ClearPersistentSpots(); } catch { }
        }

        internal static void HideNow() { Hide(); }

        // 鼠标是否落在窗口矩形内(PanelInputGuard 与手势拦截都用它)
        internal static bool IsMouseOn()
        {
            try
            {
                if (!IsShown) return false;
                var m = Input.MousePositionPixel;
                float x = _vm.X, y = _vm.Y;
                return m.X >= x && m.X <= x + W && m.Y >= y && m.Y <= y + H;
            }
            catch { return false; }
        }

        // ==================== 正文排版与滚动 ====================
        // 按 \n 切行 + 超长行按 34 字硬折(中文按字符宽估算, 与 19 号字 / 510 宽匹配)
        private static void WrapLines(string body)
        {
            try
            {
                _allLines.Clear();
                _scroll = 0;
                if (string.IsNullOrEmpty(body)) { RefreshLines(); return; }
                string[] raw = body.Replace("\r", "").Split('\n');
                for (int i = 0; i < raw.Length; i++)
                {
                    string s = raw[i] != null ? raw[i].TrimEnd() : "";
                    if (s.Length == 0) { _allLines.Add(""); continue; }
                    while (s.Length > 34)
                    {
                        _allLines.Add(s.Substring(0, 34));
                        s = "   " + s.Substring(34);   // 续行缩进, 读起来像段落
                    }
                    _allLines.Add(s);
                }
                RefreshLines();
            }
            catch { }
        }

        private static int MaxScroll { get { return Math.Max(0, _allLines.Count - VisibleLines); } }

        private static void RefreshLines()
        {
            try { if (_vm != null) _vm.FillLines(_allLines, _scroll, VisibleLines); }
            catch { }
        }

        // 滚轮: 由地图管线把滚轮量喂进来(同时那边会把相机缩放清零 -> 不穿透)
        internal static void OnScroll(float delta)
        {
            if (!IsShown) return;
            try
            {
                if (Math.Abs(delta) < 0.01f) return;
                int step = delta > 0f ? 1 : -1;
                int ns = _scroll + step;
                if (ns < 0) ns = 0;
                if (ns > MaxScroll) ns = MaxScroll;
                if (ns != _scroll) { _scroll = ns; RefreshLines(); }
            }
            catch { }
        }

        // ==================== 按钮热区(走 PanelScreen 的持久热区) ====================
        private static void RegisterSpots()
        {
            try
            {
                if (!IsShown) return;
                PanelScreen.ClearPersistentSpots();
                float x = _vm.X, y = _vm.Y;
                PanelScreen.AddSpotPersistent(x + CloseX, y + CloseY, CloseW, CloseH, delegate { DoAct(null, true); });
                PanelScreen.AddSpotPersistent(x + BtnAX, y + BtnY, BtnW, BtnH, delegate { DoAct(_actA, false); });
                PanelScreen.AddSpotPersistent(x + BtnBX, y + BtnY, BtnW, BtnH, delegate { DoAct(_actB, false); });
                PanelScreen.AddSpotPersistent(x + BtnCX, y + BtnY, BtnW, BtnH, delegate { DoAct(_actC, false); });
            }
            catch { }
        }

        private static void DoAct(Action a, bool closeOnly)
        {
            try
            {
                _clickCount++;
                if (closeOnly || a == null) { DLog.Force("教程窗: 点击(关闭) 第 " + _clickCount + " 次"); Hide(); return; }
                DLog.Force("教程窗: 点击按钮 第 " + _clickCount + " 次");
                a();
            }
            catch (Exception ex) { DLog.Force("教程窗按钮异常: " + ex.Message); }
        }

        // 每帧: 拖动(整窗可拖) —— 由 MapClickPatches.OnBeforeTick 驱动
        internal static void Tick()
        {
            if (!IsShown) return;
            try
            {
                var m = Input.MousePositionPixel;
                bool down = Input.IsKeyDown(InputKey.LeftMouseButton);
                float x = _vm.X, y = _vm.Y;
                bool inWin = m.X >= x && m.X <= x + W && m.Y >= y && m.Y <= y + H;

                if (down && inWin)
                {
                    if (!_dragging) { _dragging = true; _grabX = m.X - x; _grabY = m.Y - y; }
                    else
                    {
                        _vm.SetPos(m.X - _grabX, m.Y - _grabY);
                        RegisterSpots();
                    }
                }
                else if (!down)
                {
                    if (_dragging)
                    {
                        _dragging = false;
                        DLog.Force("教程窗: 拖动结束 位置=(" + (int)_vm.X + "," + (int)_vm.Y + ")");
                    }
                }
                _wasDown = down;
            }
            catch (Exception ex) { DLog.Force("教程窗 tick 异常: " + ex.Message); }
        }
    }
}
