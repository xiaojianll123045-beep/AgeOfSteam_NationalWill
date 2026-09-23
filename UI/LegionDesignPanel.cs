using System;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.InputSystem;

namespace FeudalInternalAffairs
{
    // 建军设计器页(PanelScreen 页): 左栏 14 兵种编制, 右栏实时预览
    //   入口 = 军务页守备营行 [成立军团](ArmyVM.AskLegionCount)
    //   拖动条/按钮 = 自建热区轮询 + 每帧轮询鼠标(该寄生层收不到 Command.Click);
    //   布局常量与 FeudalLegionDesign.xml 严格一致。
    internal static class LegionDesignPanel
    {
        internal const string Key = "legion";
        private const string Movie = "FeudalLegionDesign";
        internal const float Width = 980f;

        private const float RowTop = 232f;               // 首行顶(ListPanel MarginTop)
        private const float RowH = 184f;                 // 行高(与 xml SuggestedHeight / VM.RowHeight 一致)
        private const float ListX = 26f;                 // ListPanel 左边距
        private const float RowW = 460f;                 // 行宽(与 xml 一致)
        private const float TrackX = ListX + 100f;       // 轨道列(面板内绝对坐标, 不含 PanelX)
        private const float TrackW = 240f;               // 轨道宽(与 xml 一致)
        private const float TrackTop = 48f;              // 行内轨道容器顶(与 xml 一致)
        private const float BtnTop = 46f;                // 行内 ± 顶(与 xml 一致)
        private const float MinusX = ListX + 336f;       // − 按钮(xml 36x36)
        private const float PlusX = ListX + 376f;        // + 按钮(xml 36x36)
        private const float StepBtn = 36f;
        private const int StepMen = 10;

        private static LegionDesignVM _vm;
        private static float _acc;
        private static float _regRetry;
        private static bool _closing;

        internal static bool IsOpen { get { return PanelScreen.IsOpen(Key); } }

        internal static void Open(Settlement home)
        {
            try
            {
                if (home == null) { MapSelection.Message("请先选择驻地(本国城镇/城堡)"); return; }
                if (home.IsVillage && home.Village != null && home.Village.Bound != null) home = home.Village.Bound;
                _armed = false;
                _dragKind = -1;
                if (IsOpen) { if (_vm != null) { _vm.Retarget(home); RegisterSpots(); } return; }
                _closing = false;
                _regRetry = 0.8f;
                _vm = new LegionDesignVM(Close, home);
                _vm.OpenPanelAnim(Width);
                PanelScreen.OpenPanel(Key, Movie, _vm, null,
                    OnTick,
                    delegate { if (_vm != null) _vm.ClosePanelAnim(Width); });
                RegisterSpots();
                // 滚轮: 由 PanelScreen 轮询转发 -> VM 换可见窗口(拖动期间不响应, 防止行位移)
                PanelScreen.SetScrollHandler(Key, delegate (int dir)
                {
                    if (_vm != null && _dragKind < 0) { _vm.ScrollStep(dir); RegisterSpots(); }
                });
                DLog.Force("建军设计器已打开: " + (home.Name != null ? home.Name.ToString() : home.StringId));
            }
            catch (Exception ex) { DLog.Force("打开建军设计器失败: " + ex.Message); }
        }

        internal static void Close()
        {
            try { _closing = true; _dragKind = -1; PanelScreen.ClosePanel(Key); }
            catch { }
        }

        private static void OnTick(float dt)
        {
            try
            {
                if (_vm == null) return;
                _vm.TickAnim(dt);
                if (_closing) return;
                if (_regRetry > 0f)
                {
                    // 开页 0.8s 内高频重登记: 旧面板(军务页)关闭动画期间可能回写热区
                    _regRetry -= dt;
                    RegisterSpots();
                }
                DragTick();
                _acc += dt;
                if (_acc >= 2f)
                {
                    _acc = 0f;
                    _vm.Refresh();
                    RegisterSpots();
                }
            }
            catch { }
        }

        // ==================== 拖动条(按下 -> 移动 -> 松开 状态机) ====================
        // 热区轮询每帧在 OnTick 里做(该寄生层收不到 Gauntlet 拖动事件):
        //   ① 按下: 鼠标落在轨道热区 -> 记录 _dragKind 并立刻按 X 换算一次
        //   ② 移动: 每帧读 MousePositionPixel.X -> (X-轨道左)/轨道宽 -> VM 夹到 0..上限 并刷预览
        //   ③ 松开: Input.IsKeyReleased(左键) -> 结束拖动
        private static int _dragKind = -1;
        private static bool _armed;             // 开页那一下的按住不放不算拖动: 松手后才允许起拖

        private static void BeginDrag(int kind)
        {
            try
            {
                if (_vm == null || !_armed) return;
                if (kind < 0 || kind >= _vm.UnitCount) return;
                _dragKind = kind;
                _vm.SetHoverRow(kind);
                _vm.SetRowFromPx(kind, MouseX(), TrackX + PanelScreen.PanelX, TrackW);
                RegisterSpots();   // 拖动期间清空全部热区: 锁死 ±/支援/确认/关闭
                DLog.Force("建军设计器: 开始拖动第 " + kind + " 行");
            }
            catch { }
        }

        private static void EndDrag()
        {
            try
            {
                _dragKind = -1;
                if (_vm != null) _vm.SetHoverRow(-1);
                RegisterSpots();   // 恢复热区
            }
            catch { }
        }

        private static void DragTick()
        {
            try
            {
                if (_vm == null) return;
                _vm.TickVisuals();   // 每帧通知 @FillWidth/@HandleX(滑块随值)
                if (PanelInputGuard.AnyPopupActive()) { if (_dragKind >= 0) EndDrag(); return; }
                if (!_armed)
                {
                    if (Input.IsKeyDown(InputKey.LeftMouseButton)) { _vm.SetHoverRow(-1); return; }
                    _armed = true;   // 鼠标已松开: 之后按下才允许起拖
                }

                if (_dragKind < 0)
                {
                    int vi = HitRow(MouseX(), MouseY());
                    int hover = vi >= 0 ? _vm.KindAt(vi) : -1;   // 可见行号 -> 兵种号
                    _vm.SetHoverRow(hover);
                    if (hover >= 0 && HitTrack(MouseX()) && Input.IsKeyPressed(InputKey.LeftMouseButton))
                        BeginDrag(hover);
                    return;
                }

                if (Input.IsKeyReleased(InputKey.LeftMouseButton)) { EndDrag(); return; }
                if (!Input.IsKeyDown(InputKey.LeftMouseButton)) { EndDrag(); return; }
                _vm.SetRowFromPx(_dragKind, MouseX(), TrackX + PanelScreen.PanelX, TrackW);
            }
            catch { }
        }

        // 行命中: 鼠标在面板行带内(x 限行宽, y 按行高换算)
        private static int HitRow(float mx, float my)
        {
            try
            {
                if (_vm == null || _vm.Rows == null) return -1;
                float x = mx - PanelScreen.PanelX;
                if (x < ListX || x > ListX + RowW) return -1;
                if (my < RowTop) return -1;
                int i = (int)((my - RowTop) / RowH);
                return (i >= 0 && i < _vm.Rows.Count) ? i : -1;
            }
            catch { return -1; }
        }

        // 轨道命中: x 落在轨道热区(配合 HitRow 得到行号)
        private static bool HitTrack(float mx)
        {
            try
            {
                float x = mx - PanelScreen.PanelX;
                return x >= TrackX && x <= TrackX + TrackW;
            }
            catch { return false; }
        }

        private static float MouseX()
        {
            try { return Input.MousePositionPixel.X; } catch { return 0f; }
        }

        private static float MouseY()
        {
            try { return Input.MousePositionPixel.Y; } catch { return 0f; }
        }

        // ==================== 热区(与 FeudalLegionDesign.xml 布局一致) ====================
        private static void RegisterSpots()
        {
            try
            {
                if (_vm == null) return;
                PanelScreen.ClearSpots();
                if (_dragKind >= 0) return;   // 拖动期间不登记任何热区: 防误触其它按钮
                float sh = PanelScreen.ScreenHeight();
                PanelScreen.AddSpot(922f, 16f, 44f, 44f, _vm.ExecuteClose);
                // 行内轨道: 热区坐标与 xml 轨道容器一致(126, y+48, 240x28); ± 在 (362/402, y+46)
                //   只登记当前可见窗口的行; 按下沿由 DragTick 的 IsKeyPressed 进拖动状态, 这里再兜底一份
                int rows = _vm.Rows != null ? _vm.Rows.Count : 0;
                for (int i = 0; i < rows; i++)
                {
                    int kind = _vm.KindAt(i);
                    if (kind < 0) continue;
                    float y = RowTop + i * RowH;
                    PanelScreen.AddSpot(TrackX, y + TrackTop, TrackW, 28f, delegate { BeginDrag(kind); });  // 拖动条(按下)
                    PanelScreen.AddSpot(MinusX, y + BtnTop, StepBtn, StepBtn, delegate { _vm.StepRow(kind, -StepMen); });
                    PanelScreen.AddSpot(PlusX, y + BtnTop, StepBtn, StepBtn, delegate { _vm.StepRow(kind, StepMen); });
                }
                float by = sh - 26f - 42f;
                PanelScreen.AddSpot(26f, by, 420f, 42f, _vm.ExecuteConfirm);                   // 成立军团(装备/花费)
                PanelScreen.AddSpot(458f, by, 86f, 42f, delegate { _vm.ToggleSupport(0); });   // 工具
                PanelScreen.AddSpot(550f, by, 86f, 42f, delegate { _vm.ToggleSupport(1); });   // 野战医院
                PanelScreen.AddSpot(642f, by, 86f, 42f, delegate { _vm.ToggleSupport(2); });   // 电报机
                PanelScreen.AddSpot(740f, by, 90f, 42f, _vm.ExecuteClose);                     // 关闭
            }
            catch (Exception ex) { DLog.Force("建军设计器热区失败: " + ex.Message); }
        }
    }
}
