using System;

namespace FeudalInternalAffairs
{
    // 批量建造面板(方案编辑 + 预设 + 退出)
    internal static class BuildPanel
    {
        private const string Key = "build";
        private const string Movie = "FeudalBuild";
        private static BuildPanelVM _vm;

        internal static bool IsOpen { get { return PanelScreen.IsOpen(Key); } }

        internal static void Open()
        {
            try
            {
                if (IsOpen) { Refresh(); return; }
                _vm = new BuildPanelVM(BatchBuild.Exit);
                _vm.OpenPanelAnim(480f);
                PanelScreen.OpenPanel(Key, Movie, _vm, null,
                    delegate (float dt) { if (_vm != null) _vm.TickAnim(dt); },
                    delegate { if (_vm != null) _vm.ClosePanelAnim(480f); });
                RegisterSpots();
            }
            catch (Exception ex) { DLog.Force("打开建造面板失败: " + ex.Message); }
        }

        internal static void Close()
        {
            try { PanelScreen.ClosePanel(Key); }
            catch { }
        }

        internal static void Refresh()
        {
            try { if (_vm != null) { _vm.Refresh(); RegisterSpots(); } }   // 方案变化 -> 热区跟着重建
            catch { }
        }

        internal static void Tick() { }

        // 热区(按 FeudalBuild.xml: 面板宽 480, 无 MarginTop; 底部三行按钮)
        private static void RegisterSpots()
        {
            try
            {
                if (_vm == null) return;
                PanelScreen.ClearSpots();
                float sh = PanelScreen.ScreenHeight();
                PanelScreen.AddSpot(420f, 20f, 60f, 58f, _vm.ExecuteClose);                       // X
                float r3 = sh - 26f - 42f;
                float r2 = r3 - 12f - 42f;
                float r1 = r2 - 12f - 42f;
                PanelScreen.AddSpot(26f, r1, 150f, 42f, _vm.ExecuteAdd);                          // + 添加建筑
                PanelScreen.AddSpot(196f, r1, 92f, 42f, _vm.ExecuteClear);                        // 清空
                PanelScreen.AddSpot(26f, r2, 132f, 42f, _vm.ExecutePreset1);                      // 预设1
                PanelScreen.AddSpot(174f, r2, 132f, 42f, _vm.ExecutePreset2);                     // 预设2
                PanelScreen.AddSpot(322f, r2, 132f, 42f, _vm.ExecutePreset3);                     // 预设3
                PanelScreen.AddSpot(26f, r3, 172f, 42f, _vm.ExecuteUndo);                         // 撤销
                PanelScreen.AddSpot(218f, r3, 172f, 42f, _vm.ExecuteClose);                       // 退出建造模式
                // 方案行(动态): 每行 − + ×
                int i = 0;
                foreach (var row in _vm.Rows)
                {
                    if (row == null) continue;
                    float y = 176f + i * 54f;
                    if (y > sh - 270f) break;
                    var r = row;
                    PanelScreen.AddSpot(326f, y + 8f, 40f, 38f, r.ExecuteMinus);
                    PanelScreen.AddSpot(376f, y + 8f, 40f, 38f, r.ExecutePlus);
                    PanelScreen.AddSpot(426f, y + 8f, 40f, 38f, r.ExecuteRemove);
                    i++;
                }
            }
            catch (Exception ex) { DLog.Force("建造面板热区失败: " + ex.Message); }
        }
    }
}
