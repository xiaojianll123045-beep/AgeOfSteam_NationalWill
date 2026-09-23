using System;

namespace FeudalInternalAffairs
{
    // 人口面板(v4.147: 全屏 + V3 Pops 视角: 概览/阶级/职业/文化)
    //   布局: 同政治页 —— BrushWidget 宽 = 屏幕宽-导航栏; 内容容器设计宽 1700 居中(ContentPad);
    //   热区用 AddSpotRaw(屏幕绝对坐标) = PanelScreen.PanelX + ContentPad + 内容坐标
    internal static class PopPanel
    {
        private const string Key = "pop";
        private const string Movie = "FeudalPop";
        private const float DesignW = 1700f;
        private static PopPanelVM _vm;
        private static float _acc;

        internal static bool IsOpen { get { return PanelScreen.IsOpen(Key); } }

        internal static void Open()
        {
            Tutorials.Page("page_pop");
            try
            {
                if (!IsOpen)
                {
                    _vm = new PopPanelVM(Close);
                    _vm.OpenPanelAnim(PanelScreen.FullWidth());
                    PanelScreen.OpenPanel(Key, Movie, _vm, null,
                        delegate (float dt) { if (_vm != null) _vm.TickAnim(dt); OnTick(dt); },
                        delegate { if (_vm != null) _vm.ClosePanelAnim(PanelScreen.FullWidth()); });
                    PanelScreen.SetScrollHandler(Key, delegate (int dir) { if (_vm != null) { _vm.ScrollStep(dir); RegisterSpots(); } });
                    RegisterSpots();
                }
                else if (_vm != null) { _vm.Refresh(); RegisterSpots(); }
            }
            catch (Exception ex) { DLog.Force("打开人口页失败: " + ex.Message); }
        }

        internal static void Close()
        {
            try { PanelScreen.ClosePanel(Key); }
            catch { }
        }

        internal static void Tick(float dt) { }

        private static void OnTick(float dt)
        {
            try
            {
                _acc += dt;
                if (_acc < 3f) return;
                _acc = 0f;
                if (_vm != null) { _vm.Refresh(); RegisterSpots(); }
            }
            catch { }
        }

        // 内容容器左边缘(屏幕绝对 x)
        private static float Origin()
        {
            try { return PanelScreen.PanelX + Math.Max(0f, (PanelScreen.FullWidth() - DesignW) / 2f); }
            catch { return 64f; }
        }

        private static void Spot(float ox, float x, float y, float w, float h, Action act)
        {
            PanelScreen.AddSpotRaw(ox + x, y, w, h, act);
        }

        // 热区(与 FeudalPop.xml 布局常量严格一致)
        private static void RegisterSpots()
        {
            try
            {
                if (_vm == null) return;
                PanelScreen.ClearSpots();
                float ox = Origin();

                // 关闭 X
                Spot(ox, 1616f, 37f, 64f, 56f, _vm.ExecuteClose);

                // 职业行(中栏; 行高 40, 起点 380) -> 详情弹窗
                int rn = _vm.Rows.Count;
                for (int i = 0; i < rn; i++)
                {
                    int idx = i;
                    float y = 380f + i * 40f;
                    Spot(ox, 430f, y, 830f, 40f, delegate { _vm.ProfessionAction(idx); RegisterSpots(); });
                }

                // 文化行(右栏; 行高 44, 起点 380) -> 接纳/同化详情
                int cn = _vm.CultureRows.Count;
                for (int i = 0; i < cn; i++)
                {
                    int idx = i;
                    float y = 380f + i * 44f;
                    Spot(ox, 1280f, y, 400f, 44f, delegate { _vm.CultureAction(idx); RegisterSpots(); });
                }
            }
            catch (Exception ex) { DLog.Force("人口页热区失败: " + ex.Message); }
        }
    }
}
