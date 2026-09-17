using System;
using System.Collections.Generic;
using SandBox.View.Map;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.Library;

namespace FeudalInternalAffairs
{
    // 侧边栏面板管理器 —— 本质是"寄生在地图屏幕上的一个 Gauntlet 层":
    //   · 不 PushScreen(不顶掉地图屏): 地图照常渲染/更新, 时间/相机/快捷键全不受影响
    //   · 层只占左侧一条, 输入用命中掩码(BlockEverythingWithoutHitTest): 只有鼠标落在面板上才被拦, 其余点击照旧给地图
    //   · 关键: 层必须 IsFocusLayer = true, 否则原版地图各层(地图栏/名牌/信息栏)会把鼠标事件先吃掉, 控件收不到 Command.Click
    // 同一时间只允许一个面板(开新的自动关旧的)。
    internal static class PanelScreen
    {
        private class Entry
        {
            internal GauntletLayer Layer;
            internal MapScreen Map;
            internal string Movie;
            internal ViewModel VM;
            internal Action<float> OnTick;
            internal Action OnClosed;
            internal Action OnCloseAnim;    // 关面板前先播的滑出动画(可选)
            internal float CloseDelay;      // >0 表示正在播关闭动画, 倒计时到 0 才拆层
            internal bool UseCodeAnim;      // 用 PanelAnim 的通用代码动画
            internal bool Bound;            // 代码动画是否已绑定成功
        }

        internal static float WidthOf(string key)
        {
            switch (key)
            {
                case "market": return 780f;
                case "drawer": return 560f;
                case "diplomacy": return 540f;
                case "build": return 480f;
                case "national": return 440f;
                default: return 500f;
            }
        }

        // 鼠标是否落在"当前打开面板"的区域上(面板贴左、满高) —— 用于阻止输入透传到地图
        internal static bool IsMouseOnPanel()
        {
            try
            {
                if (Open.Count == 0) return false;
                var m = TaleWorlds.InputSystem.Input.MousePositionPixel;
                float w = CurrentPanelWidth();
                return m.X >= 0f && m.X <= w;
            }
            catch { return false; }
        }

        // 当前打开的面板 key
        internal static string CurrentKey()
        {
            foreach (var k in Open.Keys) return k;
            return null;
        }

        // 滚动回调(滚轮同样收不到 -> 自己轮询; 由各面板注册"滚动一行"的动作)
        private static readonly Dictionary<string, Action<int>> ScrollHandlers = new Dictionary<string, Action<int>>();

        internal static void SetScrollHandler(string key, Action<int> handler)
        {
            try
            {
                if (handler == null) ScrollHandlers.Remove(key);
                else ScrollHandlers[key] = handler;
            }
            catch { }
        }

        private static float _wheelAcc;
        private static void PollWheel(float dt)
        {
            try
            {
                if (!IsMouseOnPanel()) return;
                float wheel = TaleWorlds.InputSystem.Input.DeltaMouseScroll;
                if (Math.Abs(wheel) < 0.01f) return;
                _wheelAcc += wheel;
                if (Math.Abs(_wheelAcc) < 1f) return;      // 累计到 1 格再滚一行
                int dir = _wheelAcc > 0f ? -1 : 1;         // 滚轮上 -> 看前面的行
                _wheelAcc = 0f;
                Action<int> h;
                var k = CurrentKey();
                if (k != null && ScrollHandlers.TryGetValue(k, out h)) h(dir);
            }
            catch { }
        }

        // 关面板: 先播滑出动画(若提供), 动画结束再拆层; 没提供动画则立即拆
        internal static void ClosePanel(string key)
        {
            try
            {
                Entry e;
                if (!Open.TryGetValue(key, out e)) return;
                if (e.CloseDelay > 0f) return;                  // 已经在关了
                if (e.OnCloseAnim != null)
                {
                    e.CloseDelay = 0.18f;                       // 给动画留时间(原版过渡 0.14s)
                    try { e.OnCloseAnim(); } catch { }
                    DLog.Force("侧边栏正在关闭(播滑出动画): " + key);
                    return;
                }
                if (e.UseCodeAnim)
                {
                    e.CloseDelay = 0.18f;                       // 通用代码动画: 滑到左侧外面
                    PanelAnim.Play(key, 0f, -WidthOf(key), 0.16f);
                    DLog.Force("侧边栏正在关闭(代码动画): " + key);
                    return;
                }
                RemoveEntry(key, e);
            }
            catch (Exception ex) { DLog.Force("关闭面板失败(" + key + "): " + ex.Message); }
        }

        private static void RemoveEntry(string key, Entry e)
        {
            Open.Remove(key);
            // 注意: 只有"全部关完"才清热区。否则互斥时旧面板的延迟关闭会清掉新面板刚注册的热区
            // (就是这个 bug 导致"批量建造面板点不动")
            if (Open.Count == 0) ClearSpots();
            try { if (e.Layer != null && e.Map != null) e.Map.RemoveLayer(e.Layer); } catch { }
            if (e.UseCodeAnim) PanelAnim.Unbind(key);
            try { if (e.OnClosed != null) e.OnClosed(); } catch { }
            DLog.Force("侧边栏已关闭: " + key);
        }

        private static readonly Dictionary<string, Entry> Open = new Dictionary<string, Entry>();

        internal static bool AnyOpen { get { return Open.Count > 0; } }
        internal static bool IsOpen(string key) { return Open.ContainsKey(key); }

        // 当前打开面板的宽度(消息流避让用)
        internal static float CurrentPanelWidth()
        {
            try
            {
                if (Open.ContainsKey("market")) return 780f;
                if (Open.ContainsKey("drawer")) return 560f;
                if (Open.ContainsKey("diplomacy")) return 540f;
                if (Open.ContainsKey("build")) return 480f;
                if (Open.ContainsKey("national")) return 440f;
            }
            catch { }
            return 0f;
        }

        internal static void OpenPanel(string key, string movie, ViewModel vm, Action onClosed, Action<float> onTick = null, Action onCloseAnim = null)
        {
            try
            {
                // 侧边栏只能开一个
                if (Open.Count > 0)
                {
                    var keys = new List<string>(Open.Keys);
                    foreach (var k in keys)
                    {
                        if (k == key) continue;
                        DLog.Force("侧边栏互斥: 关闭 " + k + " -> 打开 " + key);
                        ClosePanel(k);
                    }
                }
                if (Open.ContainsKey(key)) return;

                var map = MapScreen.Instance;
                if (map == null) { DLog.Force("打开面板失败(" + key + "): MapScreen 为空"); return; }

                var layer = new GauntletLayer(movie, 320, false);
                layer.LoadMovie(movie, vm);
                // 点击已改由热区轮询接管(见 PollSpots), 这里不再设置 IsFocusLayer/光标/输入类型
                // —— 那些"焦点层"配置会干扰地图本身的输入与帧率(实测更卡)
                try
                {
                    layer.InputRestrictions.SetInputRestrictions(true,
                        TaleWorlds.Library.InputUsageMask.BlockEverythingWithoutHitTest);
                }
                catch { }
                map.AddLayer(layer);

                var e = new Entry { Layer = layer, Map = map, Movie = movie, VM = vm, OnTick = onTick, OnClosed = onClosed, OnCloseAnim = onCloseAnim };
                Open[key] = e;
                // 没自带动画的面板: 用通用代码动画(从左侧滑入)
                if (onCloseAnim == null)
                {
                    float w = WidthOf(key);
                    e.UseCodeAnim = true;   // 绑定+播动画放到 Tick 里重试(建层时 UIContext 可能还没就绪)
                }
                Tick(0f);   // 立刻刷一次
                DLog.Force("侧边栏已打开(地图层方式): " + key);
            }
            catch (Exception ex)
            {
                Open.Remove(key);
                DLog.Force("打开面板失败(" + key + "): " + ex.Message);
            }
        }

        internal static void ClosePanel_Old_Removed() { }

        // 原版地图各层(地图栏/信息栏)能收到点击, 我们照它们的层配置补齐:
        // ActiveCursor(光标类型) + _usedInputs(输入类型: 鼠标/键盘), 用反射容错设置
        private static void ForceLayerUsableInput(object layer)
        {
            if (layer == null) return;
            try
            {
                var t = layer.GetType();
                var curProp = t.GetProperty("ActiveCursor");
                if (curProp != null && curProp.CanWrite)
                {
                    var enumType = curProp.PropertyType;
                    object val = Enum.IsDefined(enumType, 0) ? Enum.ToObject(enumType, 0) : null;
                    if (val != null) curProp.SetValue(layer, val, null);
                }
                var inpProp = t.GetProperty("_usedInputs");
                if (inpProp != null && inpProp.CanWrite)
                {
                    var enumType = inpProp.PropertyType;
                    object val = Enum.IsDefined(enumType, 1) ? Enum.ToObject(enumType, 1) : null;
                    if (val != null) inpProp.SetValue(layer, val, null);
                }
            }
            catch { }
        }

        // ================= 热区点击(兜底) =================
        // 实测: 挂在 MapScreen 上的自建层收不到 Command.Click(原版层的输入路由吃掉了),
        // IsFocusLayer/ActiveCursor/_usedInputs/命中掩码/优先级都试过无效。
        // 所以面板按钮改由我们自己轮询(左上角国旗用同一招, 已验证有效):
        //   面板打开时由 VM 按 prefab 布局注册按钮矩形, 每帧检测鼠标+左键 -> 命中即执行动作。
        internal class Spot
        {
            internal float X, Y, W, H;
            internal Action Act;
        }

        private static readonly List<Spot> Spots = new List<Spot>();
        private static bool _leftWasDown;
        private static Action _pendingAct;   // 热区动作延迟到下一帧执行(避免在 tick/枚举中开层关层导致状态错乱或卡死)

        internal static void ClearSpots() { Spots.Clear(); }

        internal static void AddSpot(float x, float y, float w, float h, Action act)
        {
            try { Spots.Add(new Spot { X = x, Y = y, W = w, H = h, Act = act }); }
            catch { }
        }

        // 每帧检测(不受刷新节流影响): 只登记待执行动作, 不在这里执行
        internal static void PollSpots()
        {
            try
            {
                if (Open.Count == 0 || Spots.Count == 0) { _leftWasDown = false; return; }
                bool down = TaleWorlds.InputSystem.Input.IsKeyDown(TaleWorlds.InputSystem.InputKey.LeftMouseButton);
                if (!down) { _leftWasDown = false; return; }
                if (_leftWasDown) return;   // 只在按下那一瞬触发一次
                _leftWasDown = true;
                var m = TaleWorlds.InputSystem.Input.MousePositionPixel;
                for (int i = 0; i < Spots.Count; i++)
                {
                    var s = Spots[i];
                    if (m.X >= s.X && m.X <= s.X + s.W && m.Y >= s.Y && m.Y <= s.Y + s.H)
                    {
                        DLog.Force("热区点击: 命中 (" + (int)m.X + "," + (int)m.Y + ")");
                        _pendingAct = s.Act;   // 下一帧再执行
                        return;
                    }
                }
            }
            catch { }
        }

        // 执行上一帧登记的动作(在 tick 最开始, 与枚举/建层解耦)
        private static void RunPending()
        {
            var a = _pendingAct;
            _pendingAct = null;
            if (a == null) return;
            try { a(); }
            catch (Exception ex) { DLog.Force("热区动作异常: " + ex.Message); }
        }

        // 屏幕高度(像素, 用于底部锚定的按钮)
        internal static float ScreenHeight()
        {
            try { return TaleWorlds.Engine.Screen.RealScreenResolution.Y; }
            catch { return 1080f; }
        }

        // 每帧由 FeudalMapView 调用
        private static float _acc;
        private static float _hideAcc;
        internal static void Tick(float dt)
        {
            try
            {
                RunPending();  // 先执行上一帧登记的热区动作(与建层/关层解耦)
                PollWheel(dt); // 滚轮: 自己轮询(层收不到), 转成面板列表滚动
                PanelAnim.Tick(dt);   // 通用面板动画
                PollSpots();   // 热区点击: 每帧都测(不受刷新节流影响)
                if (Open.Count == 0) return;

                // 消息避让: 把左下角消息流(SPChatLog)往右推 = 当前面板宽度(面板关闭则归 0)
                _hideAcc += dt;
                if (_hideAcc >= 0.1f)
                {
                    _hideAcc = 0f;
                    try { ChatLogAvoid.SetOffset(CurrentPanelWidth()); } catch { }
                }

                // 每帧只跑"动画"(滑入/滑出, 极廉价); 数据刷新已改为按需, 不在这里做
                System.Collections.Generic.List<string> doneKeys = null;
                foreach (var kv in Open)
                {
                    try
                    {
                        // 代码动画面板: 绑定可能因为"层还没激活(UIContext 为空)"失败 -> 每帧重试, 成功即播滑入
                        if (kv.Value.UseCodeAnim && !kv.Value.Bound)
                        {
                            if (PanelAnim.TryBind(kv.Key, kv.Value.Layer))
                            {
                                kv.Value.Bound = true;
                                PanelAnim.Play(kv.Key, -WidthOf(kv.Key), 0f, 0.16f);
                            }
                        }
                        // 动画/刷新回调: 关闭动画期间也必须继续跑(否则滑出动画不会被驱动)
                        if (kv.Value.OnTick != null) kv.Value.OnTick(dt);
                        if (kv.Value.CloseDelay > 0f)
                        {
                            kv.Value.CloseDelay -= dt;
                            if (kv.Value.CloseDelay <= 0f)
                            {
                                if (doneKeys == null) doneKeys = new System.Collections.Generic.List<string>();
                                doneKeys.Add(kv.Key);
                            }
                        }
                    }
                    catch { }
                }
                if (doneKeys != null)
                {
                    foreach (var k in doneKeys)
                    {
                        Entry e2;
                        if (Open.TryGetValue(k, out e2)) RemoveEntry(k, e2);
                    }
                }
            }
            catch { }
        }
    }
}
