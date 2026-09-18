using System;

namespace FeudalInternalAffairs
{
    // 建筑总表(左侧边栏; 文档 19.14 / 参照 V3 建筑窗口)
    internal static class BuildRegPanel
    {
        private const string Key = "breg";
        private const string Movie = "FeudalBuildReg";
        private const float Width = 680f;
        private static BuildRegVM _vm;
        private static float _acc;

        internal static bool IsOpen { get { return PanelScreen.IsOpen(Key); } }

        internal static void Open()
        {
            try
            {
                if (!IsOpen)
                {
                    _vm = new BuildRegVM(Close);
                    _vm.OpenPanelAnim(Width);
                    PanelScreen.OpenPanel(Key, Movie, _vm, null,
                        delegate (float dt) { if (_vm != null) _vm.TickAnim(dt); OnTick(dt); },
                        delegate { if (_vm != null) _vm.ClosePanelAnim(Width); });
                    PanelScreen.SetScrollHandler(Key, delegate (int dir) { if (_vm != null) _vm.ScrollStep(dir); });
                    RegisterSpots();
                }
                else if (_vm != null) _vm.Refresh();
            }
            catch (Exception ex) { DLog.Force("打开建筑总表失败: " + ex.Message); }
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
                if (_vm != null) _vm.Refresh();
            }
            catch { }
        }

        // 热区(与 FeudalBuildReg.xml 布局常量一致)
        private static void RegisterSpots()
        {
            try
            {
                if (_vm == null) return;
                PanelScreen.ClearSpots();
                float sh = PanelScreen.ScreenHeight();
                PanelScreen.AddSpot(Width - 60f, 20f, 60f, 58f, _vm.ExecuteClose);       // X
                PanelScreen.AddSpot(26f, sh - 26f - 42f, 122f, 42f, _vm.ExecuteClose);  // 关闭
                // v4.0: 点击行 -> 建筑详情(文档 20.8; 行起点 280, 行高 40)
                for (int i = 0; i < _vm.ShownCount; i++)
                {
                    int idx = i;
                    PanelScreen.AddSpot(14f, 280f + i * 40f, 650f, 40f, delegate
                    {
                        var r = _vm.RowAt(idx);
                        if (r != null && !string.IsNullOrEmpty(r.Sid)) BldDetailPanel.Open(r.Sid, r.DefId);
                    });
                }
                // 位置筛选
                float fy = 150f;
                PanelScreen.AddSpot(26f, fy, 90f, 36f, _vm.ExecuteLocAll);
                PanelScreen.AddSpot(122f, fy, 90f, 36f, _vm.ExecuteLocTown);
                PanelScreen.AddSpot(218f, fy, 90f, 36f, _vm.ExecuteLocCastle);
                PanelScreen.AddSpot(314f, fy, 90f, 36f, _vm.ExecuteLocVillage);
                // 分类筛选(8 个: 全部 + 7 类)
                float cy = 196f;
                string[] cats = { null, "资源", "加工", "军事", "行政", "贸易", "物流", "生活" };
                for (int i = 0; i < cats.Length; i++)
                {
                    string c = cats[i];
                    if (c == null) PanelScreen.AddSpot(26f + i * 80f, cy, 74f, 36f, _vm.ExecuteCatAll);
                    else PanelScreen.AddSpot(26f + i * 80f, cy, 74f, 36f, delegate { _vm.ExecuteCat(c); });
                }
            }
            catch (Exception ex) { DLog.Force("建筑总表热区失败: " + ex.Message); }
        }
    }
}
