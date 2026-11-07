using System;
using TaleWorlds.CampaignSystem.Settlements;

namespace FeudalInternalAffairs
{
    // v4.268: 海军页(导航栏第 17 格) —— 五页签: 舰队 / 造船 / 封锁 / 战报 / 海运。
    //   完全走现有面板体系: PanelScreen 开层 + AddSpot 热区轮询(本文件) + FeudalNavy.xml 提供布局。
    //   软依赖: DLC 缺失时面板照常打开, 顶部显示"未检测到海战 DLC", 所有操作按钮给出提示而不崩。
    //
    // ==================== v4.282 重构(用户三条要求) ====================
    //   ① 城市详情页扩充 + 海军可进驻沿海城市 -> 见 舰队页的 [进驻城市]/[离港] 与 UI\DrawerVMs.cs
    //   ② 城市选择改成点击式 -> 本页五个入口(造船港/敌港/海运出发港/目的港/进驻城市)全部有
    //      [选择城市] 按钮, 走 Map\SettlementPickMode.cs 的**地图点选**(◀▶ 仍保留作快速切换)
    //   ③ 重构版面 -> 控制条从"页签上方"(旧 y240)搬到"页签下方"(y356), 层级变成
    //      页签 -> 控制条 -> 状态行 -> 列头 -> 数据区(见 FeudalNavy.xml 顶部对照表)。
    //
    // ==================== 热区(prefab 元素 ↔ 面板坐标 ↔ 本文件 AddSpot) ====================
    //   ★ 下列坐标 = FeudalNavy.xml 里写的面板内相对坐标(AddSpot 会统一加 PanelX=64)。
    //   ★ 行内坐标 ↔ 面板坐标: 面板 x = 26(ListPanel MarginLeft) + 行内 x; 面板 y = 496 + i*112 + 行内 y。
    //
    //   元素                     面板坐标(x,y,w,h)          动作
    //   -----------------------  -------------------------  ----------------------------------
    //   标题栏 X                 780,26,56,52               _vm.ExecuteClose
    //   (底部[关闭] 已删: v4.286 用户要求去掉重复的关闭入口, 热区同步删除)
    //   页签 舰队/造船/封锁/战报/海运  28|196|364|532|700,300,156,44   SwitchTab(0..4)
    //   ---- 舰队页控制条(ShowFleetBar; y356 h48, 按钮 h40 y360) ----
    //   ◀                        28,356,30,48               _vm.PrevFleet
    //   舰队名(点=下一支)        60,356,246,48              _vm.NextFleet
    //   ▶                        312,356,30,48              _vm.NextFleet
    //   [组建舰队]               350,360,122,40             _vm.FoundFleet
    //   [进驻城市]/[取消选点]    480,360,112,40             _vm.MoorSelected
    //   [离港]                   600,360,76,40              _vm.UnmoorSelected
    //   [回港]                   684,360,72,40              _vm.RecallFleet
    //   [解散]                   764,360,74,40              _vm.DisbandFleet
    //   ---- 造船页控制条(ShowPortPicker) ----
    //   ◀                        28,356,30,48               _vm.PrevPort
    //   港口名(点=下一个)        60,356,280,48              _vm.NextPort
    //   ▶                        348,356,30,48              _vm.NextPort
    //   [选择城市]/[取消选点]    392,360,150,40             _vm.StartPick(1) 造船港
    //   [跳到该城]               556,360,120,40             _vm.FlyToPickerPort
    //   ---- 封锁页控制条(ShowEnemyPicker; 比造船页多一枚[出击]) ----
    //   ◀                        28,356,30,48               _vm.PrevEnemyPort
    //   敌港名(点=下一个)        60,356,280,48              _vm.NextEnemyPort
    //   ▶                        348,356,30,48              _vm.NextEnemyPort
    //   [选择城市]/[取消选点]    392,360,150,40             _vm.StartPick(2) 敌港
    //   [跳到敌港]               556,360,120,40             _vm.FlyToPickerPort
    //   [出击·舰队交战]          684,360,154,40             _vm.Sortie
    //   底部[出击·舰队交战]      190,sh-70,190,44           _vm.Sortie(同一动作, 大按钮更显眼)
    //   ---- 海运页控制条(ShowTransport) ----
    //   ◀ 军团                   48,356,28,48               _vm.PrevLegion
    //   军团值(点=下一个)        78,356,150,48              _vm.NextLegion
    //   ▶ 军团                   230,356,28,48              _vm.NextLegion
    //   ◀ 出发港                 300,356,28,48              _vm.PrevFromPort
    //   出发港值(点=下一个)      330,356,140,48             _vm.NextFromPort
    //   ▶ 出发港                 472,356,28,48              _vm.NextFromPort
    //   [选城]                   504,360,64,40              _vm.StartPick(3) 出发港
    //   ◀ 目的港                 608,356,28,48              _vm.PrevToPort
    //   目的港值(点=下一个)      638,356,120,48             _vm.NextToPort
    //   ▶ 目的港                 760,356,28,48              _vm.NextToPort
    //   [选城]                   792,360,46,40              _vm.StartPick(4) 目的港
    //   底部[装船出发]           190,sh-70,190,44           _vm.DepartTransport
    //   ---- 数据区行内操作键(造船/封锁/海运三页; 行内 x 由 VM 的 ActX 绑定) ----
    //   操作键(三页统一)        772, 496+i*96+28, 64,40  _vm.RowActionByKey(key)(行内 746 w64)
    //     ★ v4.286: 行高 96 -> 120, 每页 4 -> 3 行; 操作键 y 26 -> 38(与行内数值行同一水平线)。
    //   ---- v4.284(第 1 项) 资源总览条 84,26..834 (y84 h44, 六个数字) ---- 纯显示, 无热区
    //   ---- v4.284(第 2 项) 舰队构成条 26,858,524,52 (三段)          ---- 纯显示, 无热区
    //   ---- v4.284(第 3 项) 舰队页批量操作三枚(仅 Tab==0 注册; 底部行 底距 26 h44) ----
    //   全部召回                 190, sh-70, 190, 44         _vm.RecallAllFleets
    //   全部交还                 394, sh-70, 190, 44         _vm.ReleaseAllFleets
    //   全部进驻母港             598, sh-70, 236, 44         _vm.MoorAllFleets
    //   ---- v4.284(第 4 项) 舰队页花钱换属性两枚(仅 Tab==0 注册; y840 h40; v4.286 由 866 上移, 让开构成条) ----
    //   海上演训 <费用>          590, 884, 122, 28           _vm.SeaDrill
    //   整备 <费用>              718, 884, 116, 28           _vm.OverhaulAll
    //   ★ 列头/状态行/页脚没有热区(纯显示)。
    //
    // ==================== v4.284(用户: "海军面板设计上比陆军少东西, 参考陆军怎么显示") ====================
    //   四项一次补齐, 每一项都对齐陆军页的既有做法:
    //     ① 顶部资源总览条(y84 h44, 全页签可见): 国库/舰队/舰船/水手/日耗/在航巡逻队六个数字,
    //        对应陆军页顶部 @TreasuryText/@MenText/@LegionText/@GarrisonText/@MoneyText/@QualityText;
    //        每个数都取自**已有只读函数**(见 NavyPanelVM 里 Ov* 属性的注释), 不另算口径。**纯显示, 无热区**。
    //     ② 舰队页构成条(y858 h52, 三段): 选中舰队的舰种构成/水手/提督与特长/升级件 ——
    //        对应陆军"选中军团看兵种构成", 但海军一屏还要放逐船列表, 所以做成"选中即常显"的窄条, 纯显示无热区。
    //     ③ 舰队页批量操作三枚(底部行 底距 26): [全部召回]/[全部交还]/[全部进驻母港] ——
    //        对应陆军 [全军回防]/[全军交还总监]。
    //     ④ 舰队页花钱换属性两枚(y866): [海上演训 费用]/[整备 费用] —— 对应陆军 [阅兵 1500]。
    //   ★ 页头 @Header 由 y88 h56(两行)压成 y130 h18(一行), 把 y84 那块让给总览条(文本不变, 见 VM.FlatHeader)。
    // ============================================================================
    internal static class NavyPanel
    {
        private const string Key = "navy";
        private const string Movie = "FeudalNavy";
        internal const float Width = 860f;

        // v4.282: 列表几何(必须与 FeudalNavy.xml 一致 —— 改一处必须同时改另一处)
        private const float ListTop = 496f;      // ListPanel MarginTop(v4.286: 472->496, 让开列头 458..490)
        // ==================== v4.286(用户反馈第 2 条): 行高 96 -> 128 ====================
        //   【原缺陷】字号放大后, 一行里"名称(20 号) + 四列数值(18/20 号) + 说明(17 号) + 完整度条"
        //   在 96 像素里装不下: 三段文字的字面高合计 24+24+20 = 68, 再加行距与 8 像素的条, 必然互相压。
        //   【现在】每页 3 行 × 行高 112, 数据区 y496..832(v4.286b 下移让开列头 458..490; 构成条 836、判语带 896
        //   的相对位置完全不变), 所以只有本文件与 NavyPanelVM.RowH 两个常量需要跟着改。
        private const float RowH = 96f;         // 行高(prefab 行模板 SuggestedHeight, **必须**与 VM.RowH 相同)
        private const float ListX = 26f;         // ListPanel MarginLeft
        private const int RowsPerPage = 4;       // 每页行数(496 + 4*96 = 880 < 构成条 y884)

        // v4.282: 行内操作键列(与 NavyPanelVM.ActX/ActW **同源**: VM 渲染时的 x/宽 = 这里的热区)
        //   v4.286: 三页统一到行内 730..810(面板 756..836), 不再分"造船/海运 640 w136"与"封锁 632 w64"两套。
        private const float ActY = 28f, ActH = 40f;

        // 控制条几何(y356 h48; 条内按钮 h40 y360)
        private const float BarY = 356f, BarH = 48f, BtnY = 360f, BtnH = 40f;

        private static NavyPanelVM _vm;
        private static float _acc;

        internal static bool IsOpen { get { return PanelScreen.IsOpen(Key); } }

        internal static void Open()
        {
            // v4.289: 与其它 17 个面板同源 —— 打开本页即登记"访问过 page_navy", 并把[教程]浮窗转交给
            //   挑战式引导(Tutorials.Page -> Guide.SummaryText + Guide.PageHint)。于是海上篇章
            //   (Guide.cs 的 6 条 g_navy_*)在海军页也能看到进度与"本页就是当前挑战"。
            //   ★ 只加这一行: 浮窗样式与输入拦截一行都没动(仍是 TutorialWindow 那一套)。
            Tutorials.Page("page_navy");
            try
            {
                if (!IsOpen)
                {
                    // v4.271: 出击成功后 VM 会自己跳到战报页 -> 必须立刻重登记热区
                    _vm = new NavyPanelVM(Close, RegisterSpots);
                    _vm.OpenPanelAnim(Width);
                    // v4.286: 建绑定之前先跑一次契约自检(见 ContractSelfCheck 的长注释)
                    ContractSelfCheck(_vm);
                    PanelScreen.OpenPanel(Key, Movie, _vm,
                        delegate { CancelPickOnClose(); },   // v4.282: 任何关闭路径(互斥/X)都要退出地图点选
                        delegate (float dt) { if (_vm != null) _vm.TickAnim(dt); OnTick(dt); },
                        delegate { if (_vm != null) _vm.ClosePanelAnim(Width); });
                    // 滚轮翻页后必须重登记热区(否则窗口换了行而热区还指旧行)
                    PanelScreen.SetScrollHandler(Key, delegate (int dir) { if (_vm != null) { _vm.ScrollStep(dir); RegisterSpots(); } });
                    RegisterSpots();
                }
                else if (_vm != null) { _vm.Refresh(); RegisterSpots(); }
            }
            catch (Exception ex) { DLog.Force("打开海军页失败: " + ex.Message); }
        }

        // ==================== v4.286: [DataSourceProperty] 契约自检(用户报"海军页点不出来"之后补的) ====================
        //   【为什么需要】PanelScreen.OpenPanel 里的 `layer.LoadMovie(movie, vm)` 会**读取 VM 上所有
        //   [DataSourceProperty]**(不只是 prefab 用到的那些)来建立绑定。任何一个 getter 抛异常,
        //   整页都打不开, 而日志里只有一句 `打开面板失败(navy): Object reference not set to an instance of an object.`
        //   —— 没有属性名、没有堆栈, 排查全靠猜(这次就绕了很久, 真因其实是 prefab 里一个游离文本节点)。
        //   【现在】开面板前把每个带 [DataSourceProperty] 的属性 getter 都试调一次(含 Rows/Logs/TabNames
        //   里的每个行对象), 把**抛异常的那个属性名 + 异常类型 + 堆栈首行**写进日志。
        //   只读不写, 代价是几十次属性访问; 自身失败也不影响开面板(整体 try/catch)。
        //   ★ 用"属性名比较"而不是引用 TaleWorlds 的特性类型, 避免多一个依赖面。
        private static void ContractSelfCheck(object vm)
        {
            try
            {
                if (vm == null) return;
                int total = 0, bad = 0;
                total += CheckProps(vm, "VM", ref bad);
                foreach (string col in new[] { "Rows", "Logs", "TabNames" })
                {
                    try
                    {
                        var pi = vm.GetType().GetProperty(col);
                        var items = pi != null ? pi.GetValue(vm, null) as System.Collections.IEnumerable : null;
                        if (items == null) continue;
                        int idx = 0;
                        foreach (var item in items)
                        {
                            if (item != null) total += CheckProps(item, col + "[" + idx + "]", ref bad);
                            idx++;
                            if (idx >= 80) break;   // 只看前 80 行, 不拖慢开面板
                        }
                    }
                    catch (Exception ex) { DLog.Force("海军页契约自检(集合 " + col + ")失败: " + ex.Message); }
                }
                DLog.Force("海军页契约自检: 共检查 " + total + " 个 [DataSourceProperty], 抛异常的 " + bad + " 个");
            }
            catch (Exception ex) { try { DLog.Force("海军页契约自检失败: " + ex.Message); } catch { } }
        }

        private static int CheckProps(object o, string where, ref int bad)
        {
            int n = 0;
            try
            {
                var props = o.GetType().GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                for (int i = 0; i < props.Length; i++)
                {
                    var p = props[i];
                    try
                    {
                        if (p.GetIndexParameters().Length != 0) continue;
                        bool marked = false;
                        var attrs = p.GetCustomAttributes(false);
                        for (int a = 0; a < attrs.Length; a++)
                        {
                            if (attrs[a] != null && attrs[a].GetType().Name == "DataSourceProperty") { marked = true; break; }
                        }
                        if (!marked) continue;
                        n++;
                        try { var v = p.GetValue(o, null); }
                        catch (Exception ex)
                        {
                            bad++;
                            string st = ex.StackTrace ?? "";
                            int nl = st.IndexOf('\n');
                            DLog.Force("海军页契约自检: " + where + "." + p.Name + " 抛异常 -> "
                                + ex.GetType().Name + ": " + ex.Message + " @ " + (nl > 0 ? st.Substring(0, nl).Trim() : st.Trim()));
                        }
                    }
                    catch { }
                }
            }
            catch { }
            return n;
        }

        // v4.282: 城市详情页的[查看] -> 打开海军页并选中指定舰队(见 UI\DrawerVMs.cs 的 NavyRow 操作)
        internal static void OpenAtFleet(string partyId)
        {
            try
            {
                Open();
                if (_vm == null) return;
                _vm.SetTab(0);
                _vm.SelectFleetByKey(partyId);
                RegisterSpots();
            }
            catch (Exception ex) { DLog.Force("海军页定位舰队失败: " + ex.Message); }
        }

        internal static void Close()
        {
            try { CancelPickOnClose(); PanelScreen.ClosePanel(Key); }
            catch { }
        }

        // 退出"地图点选"(面板关闭/被互斥顶掉时都必须调, 否则点选态会漏到别的页面上)
        private static void CancelPickOnClose()
        {
            try
            {
                if (SettlementPickMode.Is(SettlementPickMode.OwnerNavy)) SettlementPickMode.Cancel();
                if (_vm != null) _vm.CancelPick("", false);
            }
            catch { }
        }

        internal static void Tick(float dt) { }

        private static void OnTick(float dt)
        {
            try
            {
                // v4.282: 地图点选轮询(**每帧**, 与军务页同源): 面板打开时原版 SettlementVisual.OnMapClick
                //   的点击链被输入屏蔽, 必须自研; 命中/取消(Esc/右键)全在公共机制里(见 SettlementPickMode)。
                try { SettlementPickMode.Poll(); } catch { }
                _acc += dt;
                if (_acc < 3f) return;   // 与军务页一致: 3 秒节流刷新
                _acc = 0f;
                if (_vm != null) { _vm.Refresh(); RegisterSpots(); }
            }
            catch { }
        }

        private static void SwitchTab(int t)
        {
            try
            {
                if (_vm == null) return;
                _vm.SetTab(t);
                _vm.ClearStatus();
                RegisterSpots();
            }
            catch { }
        }

        // 热区坐标 = FeudalNavy.xml 里的面板内相对坐标(AddSpot 会统一加 PanelX)
        private static void RegisterSpots()
        {
            try
            {
                if (_vm == null) return;
                PanelScreen.ClearSpots();
                float sh = PanelScreen.ScreenHeight();
                // v4.290: 本面板所有 x/y/宽/高 都是 prefab 的**设计单位**, 必须乘 UIContext.CustomScale
                //   才是真实像素(本机 1920×1200 实测 CustomScale=1.020408)。不换算的偏移 = 设计值 × 0.0204:
                // 按钮 x 在 590~792 -> 偏 12~16px; BtnY=360 -> 偏 7.4px(按钮高仅 40~46)。
                // 变量在此统一声明, 供下面所有 Tab 分支使用。
                float ns = PanelScreen.UiScale();
                float nby = BtnY * ns, nbh = BtnH * ns;

                // ---- 标题栏 X(x=780 y=26 w=56 h=52) ----
                //   ★ v4.286(用户反馈②): 左下角那枚 [关闭] 按钮已从 prefab 删除 -> 这里的热区**必须一起删**,
                //     否则会出现"点一块看不见的地方也会关面板"的幽灵热区。关闭面板只剩右上角的 X。
                PanelScreen.AddSpot(780f, 26f, 56f, 52f, _vm.ExecuteClose);

                // ---- 五个页签(y=300 h=44; x=28/196/364/532/700 w=156, 间距 12) ----
                PanelScreen.AddSpot(28f, 300f, 156f, 44f, delegate { SwitchTab(0); });
                PanelScreen.AddSpot(196f, 300f, 156f, 44f, delegate { SwitchTab(1); });
                PanelScreen.AddSpot(364f, 300f, 156f, 44f, delegate { SwitchTab(2); });
                PanelScreen.AddSpot(532f, 300f, 156f, 44f, delegate { SwitchTab(3); });
                PanelScreen.AddSpot(700f, 300f, 156f, 44f, delegate { SwitchTab(4); });

                if (_vm.Tab == 0)
                {
                    // ---- 舰队页控制条(y=356 h=48; 与 FeudalNavy.xml 的 ShowFleetBar 组逐一对应) ----
                    PanelScreen.AddSpot(28f, BarY, 30f, BarH, _vm.PrevFleet);            // ◀
                    PanelScreen.AddSpot(60f, BarY, 246f, BarH, _vm.NextFleet);           // 舰队名(点=下一支)
                    PanelScreen.AddSpot(312f, BarY, 30f, BarH, _vm.NextFleet);           // ▶
                    // v4.290: x / 宽 与 BtnY / BtnH 全是**设计单位**, 必须换算成像素(见函数开头的 ns/nby/nbh)
                    PanelScreen.AddSpot(350f * ns, nby, 122f * ns, nbh, _vm.FoundFleet);         // [组建舰队]
                    PanelScreen.AddSpot(480f * ns, nby, 112f * ns, nbh, _vm.MoorSelected);       // [进驻城市]/[取消选点]
                    PanelScreen.AddSpot(600f * ns, nby, 76f * ns, nbh, _vm.UnmoorSelected);      // [离港]
                    PanelScreen.AddSpot(684f * ns, nby, 72f * ns, nbh, _vm.RecallFleet);         // [回港]
                    PanelScreen.AddSpot(764f * ns, nby, 74f * ns, nbh, _vm.DisbandFleet);        // [解散]
                    // ---- v4.284(第 3 项): 批量操作三枚(底部行 底距 26 h44; 仿陆军页[全军回防]/[全军交还总监]) ----
                    //   ★ 与封锁页[出击]/海运页[装船出发]共用 x190 这个槽位, 但三者的 IsVisible 互斥
                    //     (@ShowFleetBar / @ShowSortie / @ShowTransport), 且本函数按 Tab 分支注册 —— 不会打架。
                    float nby2 = PanelScreen.FromBottom(70f);   // 底距 26 + 高 44
                    PanelScreen.AddSpot(190f * ns, nby2, 190f * ns, nbh, _vm.RecallAllFleets);    // [全部召回]
                    PanelScreen.AddSpot(394f * ns, nby2, 190f * ns, nbh, _vm.ReleaseAllFleets);   // [全部交还]
                    PanelScreen.AddSpot(598f * ns, nby2, 236f * ns, nbh, _vm.MoorAllFleets);      // [全部进驻母港]
                    // ---- v4.284(第 4 项): 花钱换属性两枚(y866 h40; 构成条右侧, 费用写在按钮文字里) ----
                    PanelScreen.AddSpot(590f * ns, PanelScreen.FromTop(884f), 122f * ns, 28f * ns, _vm.SeaDrill);    // [海上演训 N]
                    PanelScreen.AddSpot(718f * ns, PanelScreen.FromTop(884f), 116f * ns, 28f * ns, _vm.OverhaulAll); // [整备 N]
                    // ★ 顶部资源总览条(y84)与选中舰队的构成条(y858)是纯显示, **没有热区**。
                }
                else if (_vm.Tab == 1)
                {
                    // ---- 造船页控制条: ◀ / 港口名 / ▶ / [选择城市] / [跳到该城] ----
                    PanelScreen.AddSpot(28f, BarY, 30f, BarH, _vm.PrevPort);
                    PanelScreen.AddSpot(60f, BarY, 280f, BarH, _vm.NextPort);
                    PanelScreen.AddSpot(348f, BarY, 30f, BarH, _vm.NextPort);
                    PanelScreen.AddSpot(392f, BtnY, 150f, BtnH, _vm.PickShipyardPort);
                    PanelScreen.AddSpot(556f, BtnY, 120f, BtnH, _vm.FlyToPickerPort);
                }
                else if (_vm.Tab == 2)
                {
                    // ---- 封锁页控制条(比造船页多一枚[出击]) ----
                    PanelScreen.AddSpot(28f, BarY, 30f, BarH, _vm.PrevEnemyPort);
                    PanelScreen.AddSpot(60f, BarY, 280f, BarH, _vm.NextEnemyPort);
                    PanelScreen.AddSpot(348f, BarY, 30f, BarH, _vm.NextEnemyPort);
                    PanelScreen.AddSpot(392f, BtnY, 150f, BtnH, _vm.PickEnemyPort);
                    PanelScreen.AddSpot(556f, BtnY, 120f, BtnH, _vm.FlyToPickerPort);
                    // 出击键: 控制条内(x=684 w=154) + 底部(x=190 底距 26 w=190 h=44), 两处同一动作
                    PanelScreen.AddSpot(684f * ns, nby, 154f * ns, nbh, _vm.Sortie);
                    PanelScreen.AddSpot(190f, sh - 26f - 44f, 190f, 44f, _vm.Sortie);
                }
                else if (_vm.Tab == 4)
                {
                    // ---- 海运页控制条(三步: ①军团 ②出发港 ③目的港; 两个港口各有[选城] -> 地图点选) ----
                    PanelScreen.AddSpot(48f, BarY, 28f, BarH, _vm.PrevLegion);
                    PanelScreen.AddSpot(78f, BarY, 150f, BarH, _vm.NextLegion);
                    PanelScreen.AddSpot(230f, BarY, 28f, BarH, _vm.NextLegion);
                    PanelScreen.AddSpot(300f, BarY, 28f, BarH, _vm.PrevFromPort);
                    PanelScreen.AddSpot(330f, BarY, 140f, BarH, _vm.NextFromPort);
                    PanelScreen.AddSpot(472f, BarY, 28f, BarH, _vm.NextFromPort);
                    PanelScreen.AddSpot(504f, BtnY, 64f, BtnH, _vm.PickFromPort);
                    PanelScreen.AddSpot(608f, BarY, 28f, BarH, _vm.PrevToPort);
                    PanelScreen.AddSpot(638f, BarY, 120f, BarH, _vm.NextToPort);
                    PanelScreen.AddSpot(760f, BarY, 28f, BarH, _vm.NextToPort);
                    PanelScreen.AddSpot(792f * ns, nby, 46f * ns, nbh, _vm.PickToPort);
                    // [装船出发](底部 x=190 底距 26 w=190 h=44)
                    PanelScreen.AddSpot(190f, sh - 26f - 44f, 190f, 44f, _vm.DepartTransport);
                }

                // ---- 行内操作键(第 5 列): 造船页 = 购买/下单, 封锁页 = 封锁/解除, 海运页 = 召回 ----
                //   行 y = ListTop + i * RowH; 行内 x/宽由 VM 的 ActX/ActW 绑定进 prefab, 这里读同一份值
                //   (**渲染与热区同源**: 旧版 prefab 写死 640 而封锁页热区按 632 算, 两边差 8 像素)。
                if (_vm.Tab == 1 || _vm.Tab == 2 || _vm.Tab == 4)
                {
                    int n = _vm.ShownRowCount;
                    for (int i = 0; i < n && i < RowsPerPage; i++)
                    {
                        // ==================== v4.279(第 3 项): 热区捕获**稳定 key**, 不再捕获行号 ====================
                        //   【原缺陷】这里原来捕获 `idx = i`(行号), 点击时由 VM 用行号去**重新生成**的
                        //   Offers()/EnemyPorts()/Active 里索引 —— 而三者的行序随时会变(船被买走/重新排序、
                        //   停战易主、运输队到达), 面板却 3 秒才刷新一次(见本文件 OnTick), 于是
                        //   "点第 3 行买到第 4 行的船型/封锁错港口/召回错运输队"。
                        //   【现在】捕获渲染这一行时的稳定 key(船 = Ship.RandomValue, 港口 = StringId,
                        //   运输队 = PartyId); 热区总是在 Refresh 之后立刻重登记(OnTick / SwitchTab /
                        //   滚轮翻页), 所以 key 与"玩家看到的这一行"严格对应。数据在窗口内变化时,
                        //   按 key 反查会命中同一对象; 对象真的没了就给中文状态提示(不静默、不误操作)。
                        //   ★ v4.282 重构后**继续沿用这一模式**(没有退回按下标索引)。
                        string key = _vm.RowKeyAt(i);
                        float w = _vm.RowActionWidth;     // 136(造船/海运) 或 64(封锁)
                        float x = _vm.RowActionX;         // 640(造船/海运) 或 632(封锁)
                        float y = ListTop + i * RowH + ActY;
                        PanelScreen.AddSpot(ListX + x, y, w, ActH,
                            delegate { _vm.RowActionByKey(key); RegisterSpots(); });
                    }
                }
            }
            catch (Exception ex) { DLog.Force("海军页热区失败: " + ex.Message); }
        }
    }
}
