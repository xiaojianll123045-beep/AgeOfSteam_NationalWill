using System;

namespace FeudalInternalAffairs
{
    // 政治面板(v5.0-P22b: 全屏 + 大字号; 导航栏第 11 格; 4 页签 = 集团/法律/运动/派系)
    // 布局: BrushWidget 宽 = 屏幕宽-导航栏; 内容容器设计宽 1700 居中(ContentPad);
    //       热区用 AddSpotRaw(屏幕绝对坐标) = PanelScreen.PanelX + ContentPad + 内容坐标
    internal static class PoliticsPanel
    {
        private const string Key = "pol";
        private const string Movie = "FeudalPolitics";
        private const float DesignW = 1700f;
        private static PoliticsPanelVM _vm;
        private static float _acc;

        internal static bool IsOpen { get { return PanelScreen.IsOpen(Key); } }

        internal static void Open()
        {
            Tutorials.Page("page_politics");
            try
            {
                if (!IsOpen)
                {
                    _vm = new PoliticsPanelVM(Close);
                    _vm.OpenPanelAnim(PanelScreen.FullWidth());
                    PanelScreen.OpenPanel(Key, Movie, _vm, null,
                        delegate (float dt) { if (_vm != null) _vm.TickAnim(dt); OnTick(dt); },
                        delegate { if (_vm != null) _vm.ClosePanelAnim(PanelScreen.FullWidth()); });
                    PanelScreen.SetScrollHandler(Key, delegate (int dir) { if (_vm != null) { _vm.ScrollStep(dir); RegisterSpots(); } });
                    RegisterSpots();
                }
                else if (_vm != null) _vm.Refresh();
            }
            catch (Exception ex) { DLog.Force("打开政治页失败: " + ex.Message); }
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

        // 动作包装: 执行后立即重注册热区(页签切换/列表增删即时生效)
        private static Action W(Action a)
        {
            return delegate { try { a(); } catch (Exception ex) { DLog.Force("政治热区动作异常: " + ex.Message); } RegisterSpots(); };
        }

        private static void Spot(float ox, float x, float y, float w, float h, Action act)
        {
            PanelScreen.AddSpotRaw(ox + x, y, w, h, act);
        }

        // 热区(与 FeudalPolitics.xml 布局常量严格一致)
        private static void RegisterSpots()
        {
            try
            {
                if (_vm == null) return;
                PanelScreen.ClearSpots();
                float ox = Origin();
                float sh = PanelScreen.ScreenHeight();

                // 标题栏 X / 底部栏
                Spot(ox, 1616f, 37f, 64f, 56f, _vm.ExecuteClose);
                Spot(ox, 30f, sh - 30f - 56f, 160f, 56f, _vm.ExecuteClose);
                Spot(ox, 210f, sh - 30f - 56f, 200f, 56f, ContractsPanel.Open);   // v4.126/v4.141: 封建契约(独立侧栏)

                // 页签
                Spot(ox, 0f, 280f, 400f, 68f, W(delegate { _vm.SetTab(0); }));
                Spot(ox, 408f, 280f, 400f, 68f, W(delegate { _vm.SetTab(1); }));
                Spot(ox, 816f, 280f, 400f, 68f, W(delegate { _vm.SetTab(2); }));
                Spot(ox, 1224f, 280f, 400f, 68f, W(delegate { _vm.SetTab(3); }));

                switch (_vm.Tab)
                {
                    case 0:
                        // 集团: 内阁(左列) / 在野(右列) 竖排列表; 行距 70, 起点 420; 按钮 (600,10,130,44)
                        int cn = _vm.CabinetRows.Count;
                        for (int i = 0; i < cn && i < 8; i++)
                        {
                            int idx = i;
                            float y = 420f + i * 70f + 10f;
                            Spot(ox, 600f, y, 130f, 44f, W(delegate { _vm.CabinetAction(idx); }));
                        }
                        int on = _vm.OppositionRows.Count;
                        for (int i = 0; i < on && i < 8; i++)
                        {
                            int idx = i;
                            float y = 420f + i * 70f + 10f;
                            Spot(ox, 1550f, y, 130f, 44f, W(delegate { _vm.OppositionAction(idx); }));
                        }
                        // 一键优化内阁(V3 quick reform)
                        Spot(ox, 1380f, 372f, 240f, 44f, W(_vm.OptimizeCabinet));
                        break;

                    case 1:
                        // 法律列表: 13 行窗口 × 46, 起点 376
                        for (int i = 0; i < _vm.LawItems.Count; i++)
                        {
                            var item = _vm.LawItemAt(i);
                            if (item == null || item.IsHeader) continue;
                            int lawIdx = item.Law;
                            float y = 376f + i * 46f;
                            Spot(ox, 0f, y, 460f, 46f, W(delegate { _vm.SelectLaw(lawIdx); }));
                        }
                        // 档位块 4 块(285 宽 + 8 间距)
                        // 法律卡 4 行(每行 58, 起点 464): 点击选为目标法律(v4.142 V3 形式)
                        for (int t = 0; t < 4; t++)
                        {
                            int tier = t;
                            Spot(ox, 520f, 464f + t * 58f, 1180f, 52f, W(delegate { _vm.SelectTier(tier); }));
                        }
                        // 立案/搁置 + 强推
                        Spot(ox, 520f, 852f, 220f, 64f, W(delegate
                        {
                            if (LawSystem.InBill && LawSystem.BillLaw == _vm.SelectedLaw) _vm.CancelBill();
                            else _vm.LawAction(false);
                        }));
                        Spot(ox, 760f, 852f, 220f, 64f, W(delegate { _vm.LawAction(true); }));
                        break;

                    case 2:
                        // 运动卡: 行距 188, 起点 420; 镇压 (360,90,130,32) / 妥协 (498,90,120,32)
                        int n = _vm.MovementRows.Count;
                        for (int i = 0; i < n && i < 3; i++)
                        {
                            int idx = i;
                            float y = 420f + i * 188f + 88f;   // 与 XML MarginTop="88" 对齐
                            Spot(ox, 1200f, y, 220f, 56f, W(delegate { _vm.MovementAction(idx, true); }));
                            Spot(ox, 1440f, y, 220f, 56f, W(delegate { _vm.MovementAction(idx, false); }));
                        }
                        // v5.0-P23: 革命操作(镇压/妥协革命, 仅革命存在时可见)
                        if (_vm.RevVisible)
                        {
                            Spot(ox, 1000f, 414f, 220f, 40f, W(_vm.RevSuppress));
                            Spot(ox, 1230f, 414f, 220f, 40f, W(_vm.RevConcede));
                        }
                        break;

                    default:
                        // 派系: 全局操作 4 钮 + 动员(v5.0-P25)
                        Spot(ox, 0f, 476f, 240f, 56f, W(_vm.Feast));
                        Spot(ox, 250f, 476f, 240f, 56f, W(_vm.Patrol));
                        Spot(ox, 500f, 476f, 240f, 56f, W(_vm.Suppress));
                        Spot(ox, 750f, 476f, 240f, 56f, W(_vm.Concede));
                        Spot(ox, 1000f, 476f, 240f, 56f, W(_vm.Mobilize));
                        // 领主名册: 5 行 × 52, 起点 620; 5 钮 x 790 起(步进 106) 宽 96 高 40 (y+6)
                        for (int i = 0; i < _vm.ShownCount; i++)
                        {
                            int idx = i;
                            float y = 620f + i * 52f + 6f;
                            Spot(ox, 790f, y, 96f, 40f, W(delegate { _vm.LordAction(idx, 0); }));
                            Spot(ox, 896f, y, 96f, 40f, W(delegate { _vm.LordAction(idx, 1); }));
                            Spot(ox, 1002f, y, 96f, 40f, W(delegate { _vm.LordAction(idx, 2); }));
                            Spot(ox, 1108f, y, 96f, 40f, W(delegate { _vm.LordAction(idx, 3); }));
                            Spot(ox, 1214f, y, 96f, 40f, W(delegate { _vm.LordAction(idx, 4); }));
                        }
                        // 请愿: 1 行, 起点 920; 同意 1400 / 拒绝 1540 (y+6)
                        int pn = _vm.PetitionRows.Count;
                        for (int i = 0; i < pn && i < 1; i++)
                        {
                            int idx = i;
                            float y = 920f + i * 56f + 6f;
                            Spot(ox, 1400f, y, 120f, 44f, W(delegate { _vm.PetitionAction(idx, true); }));
                            Spot(ox, 1540f, y, 120f, 44f, W(delegate { _vm.PetitionAction(idx, false); }));
                        }
                        break;
                }
            }
            catch (Exception ex) { DLog.Force("政治页热区失败: " + ex.Message); }
        }
    }
}
