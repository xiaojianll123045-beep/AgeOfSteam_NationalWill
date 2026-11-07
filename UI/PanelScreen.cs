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
        // 面板停靠位置: 左侧导航栏(宽 60)右侧
        internal const float PanelX = 64f;

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

        // v5.0-P22b: 政治页改全屏(宽度 = 屏幕宽 - 导航栏), 内容居中设计宽 1700
        internal static float FullWidth()
        {
            try { return Math.Max(700f, ScreenWidth() - PanelX); }
            catch { return 1856f; }
        }

        internal static float WidthOf(string key)
        {
            switch (key)
            {
                case "market": return 780f;
                case "pop": return FullWidth();   // v4.147: 人口页改全屏
                case "fiscal": return 680f;
                case "breg": return 680f;
                case "soc": return 680f;
                case "guild": return 620f;
                case "stats": return 680f;
                case "pol": return FullWidth();
                case "focus": return FullWidth();   // v4.146: 国策页(全屏面板)
                case "tech": return FullWidth();    // v4.151: 科技页(全屏面板)
                case "contracts": return 720f;
                case "play": return 680f;
                case "army": return 860f;     // 军务三页: 加宽(原 680)
                case "troops": return 860f;   // 军队页: 加宽登记(原缺省 500)
                case "military": return 860f; // 军务总览页: 加宽(原缺省 500)
                case "amg": return 760f;      // 28.6 部队管理页
                case "legion": return 980f;   // 27.18 建军设计器页
                case "bld": return 680f;
                case "drawer": return 560f;
                case "diplomacy": return 540f;
                case "build": return 480f;
                case "national": return 520f;   // v4.150: 国家总页加宽
                case "navy": return 860f;       // v4.268: 海军页(与军务页同宽)
                default: return 500f;
            }
        }

        // 鼠标是否落在"当前打开面板"的区域上(面板贴左、满高) —— 用于阻止输入透传到地图
        internal static bool IsMouseOnPanel()
        {
            try
            {
                var m = TaleWorlds.InputSystem.Input.MousePositionPixel;
                // v4.75: 右侧选国侧栏也算"在面板上"
                if (NationPickPanel.IsOpen && m.X >= ScreenWidth() - NationPickPanel.Width) return true;
                if (Open.Count == 0) return false;
                float w = CurrentPanelWidth();
                return m.X >= PanelX - 6f && m.X <= PanelX + w;
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
                    // v5.0-P22b: 关闭延迟按面板宽度算(全屏面板滑出距离大, 0.3s 不够会"闪现消失")
                    e.CloseDelay = Math.Max(0.30f, WidthOf(key) / 3000f + 0.06f);
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
            // (就是这个 bug 导致"批量建造面板点不动"); v4.75: 右侧选国侧栏开着时也不能清
            if (Open.Count == 0 && !NationPickPanel.IsOpen) ClearSpots();
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
            if (Open.ContainsKey("pop")) return FullWidth();   // v4.147: 人口页改全屏
            if (Open.ContainsKey("fiscal")) return 680f;
            if (Open.ContainsKey("breg")) return 680f;
            if (Open.ContainsKey("soc")) return 680f;
            if (Open.ContainsKey("guild")) return 620f;
            if (Open.ContainsKey("stats")) return 680f;
            if (Open.ContainsKey("pol")) return FullWidth();
            if (Open.ContainsKey("focus")) return FullWidth();   // v4.146: 国策页
            if (Open.ContainsKey("tech")) return FullWidth();    // v4.151: 科技页
            if (Open.ContainsKey("culture")) return FullWidth();  // v4.186: 文化页
            if (Open.ContainsKey("event")) return 560f;           // v4.197: 事件面板
            if (Open.ContainsKey("military")) return 860f;        // v4.199: 军务页(加宽)
            if (Open.ContainsKey("treaty")) return 680f;          // v4.204: 条约页
            if (Open.ContainsKey("breport")) return 680f;         // v4.209: 战报页
            if (Open.ContainsKey("decree")) return 680f;          // v4.210: 法令页
            if (Open.ContainsKey("lobby")) return 680f;           // v4.211: 游说页
            if (Open.ContainsKey("parly")) return 680f;           // v4.211: 议会页
            if (Open.ContainsKey("chest")) return 680f;           // v4.211: 钱箱页
                if (Open.ContainsKey("contracts")) return 720f;
            if (Open.ContainsKey("play")) return 680f;
            if (Open.ContainsKey("army")) return 860f;          // 军务三页: 加宽(原 680)
            if (Open.ContainsKey("troops")) return 860f;        // 军队页: 加宽登记(原缺省 500)
            if (Open.ContainsKey("amg")) return 760f;          // 28.6 部队管理页
            if (Open.ContainsKey("legion")) return 760f;       // 27.18 建军设计器页
            if (Open.ContainsKey("bld")) return 680f;
                if (Open.ContainsKey("drawer")) return 560f;
                if (Open.ContainsKey("diplomacy")) return 540f;
                if (Open.ContainsKey("build")) return 480f;
                if (Open.ContainsKey("national")) return 520f;   // v4.150: 国家总页加宽
                if (Open.ContainsKey("navy")) return 860f;       // v4.268: 海军页(消息流避让按真实宽度算)
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

                var layer = new GauntletLayer(movie, 345, false);   // v4.75o: 高于国名标签(335)
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

        // v4.263: 持久热区 —— 教程浮窗这类"不属于任何侧边栏"的常驻窗口把按钮注册在这里,
        //   各面板的 ClearSpots() 不会清掉它(否则教程窗按钮会被面板刷新顺手清空 -> 点了没反应)
        private static readonly List<Spot> Persistent = new List<Spot>();
        internal static void ClearPersistentSpots() { Persistent.Clear(); }
        internal static void AddSpotPersistent(float x, float y, float w, float h, Action act)
        {
            try { Persistent.Add(new Spot { X = x, Y = y, W = w, H = h, Act = act }); }
            catch { }
        }

        internal static void AddSpot(float x, float y, float w, float h, Action act)
        {
            // 热区坐标全部按"面板内相对坐标"登记, 这里统一加上停靠偏移(面板在导航栏右侧)
            try { Spots.Add(new Spot { X = x + PanelX, Y = y, W = w, H = h, Act = act }); }
            catch { }
        }

        // v4.75: 右侧面板用(坐标已是屏幕坐标, 不加左侧停靠偏移)
        internal static void AddSpotRaw(float x, float y, float w, float h, Action act)
        {
            try { Spots.Add(new Spot { X = x, Y = y, W = w, H = h, Act = act }); }
            catch { }
        }

        // 每帧检测(不受刷新节流影响): 只登记待执行动作, 不在这里执行
        internal static void PollSpots()
        {
            try
            {
                if (Spots.Count == 0) { _leftWasDown = false; return; }
                // v4.85: 弹窗(Inquiry)打开时热区不响应, 避免点击弹窗按钮被面板热区重复吃掉
                if (PanelInputGuard.AnyPopupActive()) { _leftWasDown = true; return; }
                // v27.x: "开始游戏"确认后的抑制窗口内热区一律忽略(同步按下状态, 窗口结束不误触发)
                if (PanelInputGuard.ClicksSuppressed)
                {
                    _leftWasDown = TaleWorlds.InputSystem.Input.IsKeyDown(TaleWorlds.InputSystem.InputKey.LeftMouseButton);
                    return;
                }
                bool down = TaleWorlds.InputSystem.Input.IsKeyDown(TaleWorlds.InputSystem.InputKey.LeftMouseButton);
                if (!down) { _leftWasDown = false; return; }
                if (_leftWasDown) return;   // 只在按下那一瞬触发一次
                _leftWasDown = true;
                var m = TaleWorlds.InputSystem.Input.MousePositionPixel;
                // v4.263: 先判持久热区(教程浮窗等最上层窗口), 再判当前面板的热区
                for (int i = Persistent.Count - 1; i >= 0; i--)
                {
                    var ps = Persistent[i];
                    if (m.X >= ps.X && m.X <= ps.X + ps.W && m.Y >= ps.Y && m.Y <= ps.Y + ps.H)
                    {
                        DLog.Info("热区点击: 命中(浮窗) (" + (int)m.X + "," + (int)m.Y + ")");   // v4.267: 每次点击都记 -> 降噪, 不进正式日志
                        _pendingAct = ps.Act;
                        return;
                    }
                }
                // v4.257: 命中判定改成"**后登记的热区优先**" —— 面板互斥切换时, 正在播关闭动画的旧面板
                //   仍会每帧重注册热区(它比新面板更早进入 Open), 而原来是先入者优先 -> 点新面板的按钮
                //   会被旧面板的热区吃掉(典型: 点刚打开的[成立军团]命中了上一页的[关闭])。
                //   后登记优先后, 新面板的热区天然压过旧面板, 无需改动各页面板。
                for (int i = Spots.Count - 1; i >= 0; i--)
                {
                    var s = Spots[i];
                    if (m.X >= s.X && m.X <= s.X + s.W && m.Y >= s.Y && m.Y <= s.Y + s.H)
                    {
                        DLog.Info("热区点击: 命中 (" + (int)m.X + "," + (int)m.Y + ")");   // v4.267: 每次点击都记 -> 降噪, 不进正式日志
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

        // v4.58: 城市名点击 -> 相机平滑飞到该定居点(用户需求: 所有城市名可点)
        internal static void JumpToSettlement(TaleWorlds.CampaignSystem.Settlements.Settlement s)
        {
            try
            {
                if (s == null) return;
                NationalWillCamera.FlyTo(s);
                DLog.Force("视角跳转: " + (s.Name != null ? s.Name.ToString() : s.StringId));
            }
            catch (Exception ex) { DLog.Force("视角跳转失败: " + ex.Message); }
        }

        // 屏幕高度(像素, 用于底部锚定的按钮)
        internal static float ScreenHeight()
        {
            try { return TaleWorlds.Engine.Screen.RealScreenResolution.Y; }
            catch { return 1080f; }
        }

        // 屏幕宽度(像素)
        internal static float ScreenWidth()
        {
            try { return TaleWorlds.Engine.Screen.RealScreenResolution.X; }
            catch { return 1920f; }
        }

        // ==================== v4.290: 设计单位 -> 像素 ====================
        // 问题(实机探针确证, 1920×1200: UsableArea=(1,1) CustomScale=1.020408):
        //   Gauntlet 的 prefab 里 SuggestedWidth/Height 与 Margin* 都是**设计单位**, 进布局时要乘
        //   UIContext.CustomScale(见 GauntletUI: ScaledSuggestedWidth/ScaledMargin* = _scaleToUse × 原值)。
        //   而热区一直用 `ScreenHeight() - d`(d = 距底边的设计距离), 把设计单位当像素用了。
        //   偏移量 = d × (CustomScale − 1), 所以:
        //     事件面板 d=72  -> 1.5px  (看得出来但不明显)
        //     军团小窗 d=576 -> 11.8px (按钮高仅 46, 于是"中间能点、边缘点不动")
        //   1440p 下 CustomScale≈1.333, d=576 会偏 192px —— 屏幕下半部热区基本报废。
        // 下面用与 UIContext.UpdateScale() 相同的算法复刻 CustomScale(不需要 GauntletLayer 引用):
        //   Scale = 画布高 / 1080; 画布宽高比 < 16:9×0.98 时再乘 (宽高比 / 该参考比)
        //   本机复刻值 = (1200/1080) × (1.6/1.7422) = 1.02041 —— 与探针实测逐位吻合。
        private static float _uiScale = -1f;

        internal static float UiScale()
        {
            if (_uiScale > 0f) return _uiScale;
            try
            {
                float h = ScreenHeight(), w = ScreenWidth();
                if (h <= 1f) { _uiScale = 1f; return _uiScale; }
                float s = h / 1080f;
                try
                {
                    float ar = w / h;
                    float refAr = (1920f / 1080f) * 0.98f;
                    if (ar < refAr) s *= ar / refAr;
                }
                catch { }
                _uiScale = s > 0.05f ? s : 1f;
            }
            catch { _uiScale = 1f; }
            return _uiScale;
        }

        // 把"距屏幕底边 d 个设计单位"换算成真实屏幕 y。
        // 用法: 原来写 `sh - 660 + 84` 的, 现在写 `PanelScreen.FromBottom(660f - 84f)`。
        internal static float FromBottom(float d)
        {
            try { return ScreenHeight() - d * UiScale(); }
            catch { return ScreenHeight() - d; }
        }

        // 把"距屏幕右边 d 个设计单位"换算成真实屏幕 x。
        internal static float FromRight(float d)
        {
            try { return ScreenWidth() - d * UiScale(); }
            catch { return ScreenWidth() - d; }
        }

        // 把"距屏幕左边 d 个设计单位"换算成真实屏幕 x。
        internal static float FromLeft(float d)
        {
            try { return d * UiScale(); }
            catch { return d; }
        }

        // 把"距屏幕顶边 d 个设计单位"换算成真实屏幕 y。
        internal static float FromTop(float d)
        {
            try { return d * UiScale(); }
            catch { return d; }
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
                    try { ChatLogAvoid.SetOffset(PanelX + CurrentPanelWidth()); } catch { }
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
