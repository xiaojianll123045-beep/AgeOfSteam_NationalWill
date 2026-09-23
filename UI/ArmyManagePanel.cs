using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;

namespace FeudalInternalAffairs
{
    // 28.6 部队管理页(PanelScreen 页):
    //   入口 = 地图单选非军团部队 -> 左下角小窗 [管理/扩容/分裂/合并/解散]
    //   拖动条 = 自建热区 + 每帧轮询鼠标(该寄生层收不到 Gauntlet 输入/Command.Click);
    //   每行另有 −/+ 按钮兜底。布局常量与 FeudalArmyManage.xml 严格一致。
    internal static class ArmyManagePanel
    {
        internal const string Key = "amg";
        private const string Movie = "FeudalArmyManage";
        internal const float Width = 760f;

        private const float RowX = 26f;         // 行左边(ListPanel MarginLeft)
        private const float RowW = 460f;        // 行宽(与 xml 一致)
        private const float RowTop = 268f;      // 首行顶(ListPanel MarginTop)
        private const float RowH = 124f;        // 行高(与 xml SuggestedHeight 一致; 图标 80 + 两行摘要)
        private const float TrackX = 118f;      // 轨道列(面板内绝对坐标 = RowX + 92)
        private const float TrackW = 264f;      // 轨道宽(与 xml 一致)
        private const float TrackTop = 44f;     // 行内轨道容器顶(与 xml 一致)
        private const float BtnTop = 40f;       // 行内 ± 顶(与 xml 一致)
        private const float MinusX = 390f;      // − 按钮(xml 36x36; RowX + 364)
        private const float PlusX = 430f;       // + 按钮(xml 36x36; RowX + 404)
        private const float StepBtn = 36f;

        private static ArmyManageVM _vm;
        private static float _acc;

        internal static bool IsOpen { get { return PanelScreen.IsOpen(Key); } }

        internal static void Open(MobileParty p) { Open(p, -1); }

        internal static void Open(MobileParty p, int mode)
        {
            try
            {
                if (p == null || !p.IsActive) { MapSelection.Message("该部队无效(可能已解散)"); return; }
                _armed = false;
                _dragKind = -1;
                if (IsOpen)
                {
                    if (_vm != null) { _vm.Retarget(p, mode); RegisterSpots(); }
                    return;
                }
                _vm = new ArmyManageVM(Close, p, mode);
                _vm.OpenPanelAnim(Width);
                PanelScreen.OpenPanel(Key, Movie, _vm, null,
                    OnTick,
                    delegate { if (_vm != null) _vm.ClosePanelAnim(Width); });
                RegisterSpots();
                PanelScreen.SetScrollHandler(Key, delegate (int dir)
                {
                    if (_vm != null && _dragKind < 0) { _vm.ScrollStep(dir); RegisterSpots(); }
                });
                DLog.Force("部队管理页已打开: " + MapSelection.NameOf(p) + " 模式=" + mode);
            }
            catch (Exception ex) { DLog.Force("打开部队管理页失败: " + ex.Message); }
        }

        internal static void Close()
        {
            try { _dragKind = -1; PanelScreen.ClosePanel(Key); }
            catch { }
        }

        // 小窗[合并]: 打开本页并直接弹出目标部队列表
        internal static void OpenWithMerge(MobileParty p)
        {
            try
            {
                Open(p, ArmyManageVM.ModeAdd);
                if (_vm != null) _vm.ExecuteMerge();
            }
            catch (Exception ex) { DLog.Force("打开合并列表失败: " + ex.Message); }
        }

        private static void OnTick(float dt)
        {
            try
            {
                if (_vm == null) return;
                _vm.TickAnim(dt);
                DragTick();
                _acc += dt;
                if (_acc >= 2.5f)
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
        private static float _dragX, _dragW;
        private static bool _armed;             // 开页那一下的按住不放不算拖动: 松手后才允许起拖

        private static void BeginDrag(int kind)
        {
            try
            {
                if (_vm == null || _vm.IsArmy || !_armed) return;
                if (kind < 0 || kind >= _vm.UnitCount) return;
                _dragKind = kind;
                _dragX = TrackX + PanelScreen.PanelX;
                _dragW = TrackW;
                _vm.SetHoverRow(kind);
                _vm.SetRowDrag(kind, true);   // 按下态: 滑块亮金
                _vm.NoteCapReason(kind);      // v4.234: 上限为 0 时把原因写到状态行(否则拖了像坏掉)
                _vm.SetRowFromPx(kind, MouseX(), _dragX, _dragW);
                RegisterSpots();   // 拖动期间清空全部热区: 锁死 ±/页签/底部按钮
                DLog.Force("部队管理: 开始拖动第 " + kind + " 行 上限=" + _vm.MaxOf(kind));
            }
            catch { }
        }

        private static void EndDrag()
        {
            try
            {
                int k = _dragKind;
                _dragKind = -1;
                if (_vm != null) { _vm.SetRowDrag(k, false); _vm.SetHoverRow(-1); }
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
                if (_vm.IsArmy)
                {
                    if (_dragKind >= 0) { _vm.SetRowDrag(_dragKind, false); _dragKind = -1; }
                    return;
                }
                if (PanelInputGuard.AnyPopupActive()) { if (_dragKind >= 0) EndDrag(); return; }
                if (!_armed)
                {
                    if (Input.IsKeyDown(InputKey.LeftMouseButton)) { _vm.SetHoverRow(-1); return; }
                    _armed = true;   // 鼠标已松开: 之后按下才允许起拖
                }

                if (_dragKind < 0)
                {
                    int hover = HitRow(MouseX(), MouseY());
                    _vm.SetHoverRow(hover);
                    if (hover >= 0 && HitTrack(MouseX()) && Input.IsKeyPressed(InputKey.LeftMouseButton))
                        BeginDrag(hover);
                    return;
                }

                if (Input.IsKeyReleased(InputKey.LeftMouseButton)) { EndDrag(); return; }
                if (!Input.IsKeyDown(InputKey.LeftMouseButton)) { EndDrag(); return; }
                _vm.SetRowFromPx(_dragKind, MouseX(), _dragX, _dragW);
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
                if (x < RowX || x > RowX + RowW) return -1;
                if (my < RowTop) return -1;
                int i = (int)((my - RowTop) / RowH);
                return (i >= 0 && i < _vm.Rows.Count) ? _vm.KindAt(i) : -1;
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

        // ==================== 热区(与 prefab 布局一致) ====================
        private static void RegisterSpots()
        {
            try
            {
                if (_vm == null) return;
                PanelScreen.ClearSpots();
                if (_dragKind >= 0) return;   // 拖动期间不登记任何热区: 防误触其它按钮/页签
                float sh = PanelScreen.ScreenHeight();
                if (_vm.IsArmy)
                {
                    PanelScreen.AddSpot(700f, 16f, 44f, 44f, _vm.ExecuteClose);
                    PanelScreen.AddSpot(30f, 300f, 180f, 44f, _vm.ExecuteGoMilitary);   // 去军务页
                    PanelScreen.AddSpot(30f, 352f, 180f, 44f, _vm.ExecuteClose);
                    return;
                }

                PanelScreen.AddSpot(700f, 16f, 44f, 44f, _vm.ExecuteClose);             // X
                PanelScreen.AddSpot(26f, 184f, 130f, 42f, delegate { _vm.SetMode(0); RegisterSpots(); });  // 扩容页签
                PanelScreen.AddSpot(162f, 184f, 130f, 42f, delegate { _vm.SetMode(1); RegisterSpots(); }); // 分裂页签

                // 行内轨道: 热区坐标与 xml 轨道容器一致(RowX+92, y+44, 264x28); ± 在 (RowX+364/404, y+40)
                //   按下沿由 DragTick 的 IsKeyPressed 直接进拖动状态; 这里再登记一份兜底
                int rows = _vm.Rows != null ? _vm.Rows.Count : 0;
                for (int i = 0; i < rows; i++)
                {
                    int kind = _vm.KindAt(i);
                    if (kind < 0) continue;
                    float y = RowTop + i * RowH;
                    PanelScreen.AddSpot(TrackX, y + TrackTop, TrackW, 28f, delegate { BeginDrag(kind); });  // 拖动条(按下)
                    PanelScreen.AddSpot(MinusX, y + BtnTop, StepBtn, StepBtn, delegate { _vm.StepRow(kind, -1); });  // −
                    PanelScreen.AddSpot(PlusX, y + BtnTop, StepBtn, StepBtn, delegate { _vm.StepRow(kind, 1); });   // +
                }

                float by = sh - 26f - 42f;
                PanelScreen.AddSpot(26f, by, 180f, 42f, _vm.ExecuteApply);               // 确认扩容/分裂
                PanelScreen.AddSpot(214f, by, 110f, 42f, _vm.ExecuteMerge);              // 合并
                PanelScreen.AddSpot(332f, by, 110f, 42f, _vm.ExecuteDisband);            // 解散
                if (_vm.Mode == ArmyManageVM.ModeAdd)
                    PanelScreen.AddSpot(450f, by, 140f, 42f, _vm.PickSettlement);        // 选征兵地
                PanelScreen.AddSpot(606f, by, 128f, 42f, _vm.ExecuteClose);              // 关闭
            }
            catch (Exception ex) { DLog.Force("部队管理页热区失败: " + ex.Message); }
        }

        // ==================== 地图多选右键: 合并部队 ====================
        internal static int CountMergeableSelected()
        {
            int n = 0;
            try
            {
                var list = MapSelection.SelectedList;
                for (int i = 0; i < list.Count; i++)
                {
                    var q = list[i];
                    if (q == null || !q.IsActive || q.Army != null) continue;
                    if (q.IsMainParty || q.IsCaravan || q.IsVillager || q.IsMilitia || q.IsGarrison) continue;
                    if (!NationalWillOrders.IsOurs(q)) continue;
                    n++;
                }
            }
            catch { }
            return n;
        }

        internal static void MergeSelected()
        {
            try
            {
                var list = new List<MobileParty>();
                var sel = MapSelection.SelectedList;
                for (int i = 0; i < sel.Count; i++)
                {
                    var q = sel[i];
                    if (q == null || !q.IsActive || q.Army != null) continue;
                    if (q.IsMainParty || q.IsCaravan || q.IsVillager || q.IsMilitia || q.IsGarrison) continue;
                    if (!NationalWillOrders.IsOurs(q)) continue;
                    if (!list.Contains(q)) list.Add(q);
                }
                if (list.Count < 2) { MapSelection.Message("至少选中 2 支本国非军团部队"); return; }

                MobileParty main = list[0];
                for (int i = 1; i < list.Count; i++)
                {
                    int cur = Soldiers.CountOf(main), next = Soldiers.CountOf(list[i]);
                    if (next > cur) main = list[i];
                }
                int ok = 0;
                var fails = new System.Text.StringBuilder();
                for (int i = 0; i < list.Count; i++)
                {
                    var q = list[i];
                    if (ReferenceEquals(q, main)) continue;
                    string msg;
                    if (ArmyMoves.Merge(main, q, out msg)) ok++;
                    else
                    {
                        if (fails.Length > 0) fails.Append(" | ");
                        fails.Append(msg);
                    }
                }
                string text = "合并部队: " + ok + " 支并入「" + MapSelection.NameOf(main) + "」"
                    + (fails.Length > 0 ? " ; " + fails.ToString() : "");
                MapSelection.Message(text);
                MapSelection.Select(main);
                if (IsOpen && _vm != null) { _vm.Refresh(); RegisterSpots(); }
            }
            catch (Exception ex) { DLog.Force("多选合并异常: " + ex.Message); }
        }
    }
}
