using System;
using TaleWorlds.CampaignSystem.Settlements;

namespace FeudalInternalAffairs
{
    // 定居点抽屉: 态A 本国定居点树 / 态B 建筑列表
    // 用 PushScreen(带按钮的地图界面只有屏幕方案能收到点击)
    internal static class SettlementDrawer
    {
        private const string Key = "drawer";
        private const string Movie = "FeudalDrawer";
        private static SettlementDrawerVM _vm;
        private static float _acc;

        internal static bool IsOpen { get { return PanelScreen.IsOpen(Key); } }

        internal static void Open(Settlement s)
        {
            Tutorials.Page("page_drawer");
            try
            {
                if (IsOpen)
                {
                    if (_vm != null && s != null) _vm.ShowBuildings(s);
                    return;
                }
                _vm = new SettlementDrawerVM(Close);
                if (s != null) _vm.ShowBuildings(s);
                _vm.OpenPanelAnim(560f);
                PanelScreen.OpenPanel(Key, Movie, _vm, null,
                    delegate (float dt) { if (_vm != null) _vm.TickAnim(dt); OnTick(dt); },
                    delegate { if (_vm != null) _vm.ClosePanelAnim(560f); });
                PanelScreen.SetScrollHandler(Key, delegate (int dir) { if (_vm != null) { _vm.ScrollStep(dir); RegisterSpots(); } });
                RegisterSpots();
            }
            catch (Exception ex) { DLog.Force("打开抽屉失败: " + ex.Message); }
        }

        internal static void Close()
        {
            try { PanelScreen.ClosePanel(Key); }
            catch { }
        }

        // 低频刷新(2 秒一次)
        private static void OnTick(float dt)
        {
            try
            {
                _acc += dt;
                if (_acc < 2f) return;
                _acc = 0f;
                if (_vm != null) _vm.Refresh();
                RegisterSpots();   // 列表内容会变(切态/折叠/搜索), 定期重建热区
            }
            catch { }
        }

        internal static void Tick(float dt) { }

        // 热区(按 FeudalDrawer.xml 布局: 面板宽 560, 无 MarginTop)
        //   ==================== v4.282: 新增"驻扎的海军"区块(底锚定) ====================
        //   版面(prefab 与这里必须成对改; 数字同源于 SettlementDrawerVM.NavyReserve/NavyRowH):
        //     标题行     底距 230 (h20)  —— 无热区
        //     港口信息行 底距 210 (h18)  —— 无热区
        //     舰队行 i   行顶 = sh - 204 + i*44 (h40, 行距 4; 最多 3 行)
        //       行内 [查看] 面板 x 380 w64 h30(y = 行顶 + 5)  ·  [召回] 面板 x 452 w64 h30
        //       ★ 用 MarginLeft 左锚定而不是右锚定: 本页既有的右锚定热区(±按钮 448/506)与
        //         prefab 的 MarginRight 推算结果本身就差 ~10 像素(见下), 左锚定的行首是确定的(容器左缩进 14)。
        //   ★ 建筑列表的可用高度因此少了 258 像素: 下面的守卫与 DrawerVMs.RowPage() 用**同一个常量**,
        //     否则会出现"渲染了却点不动"的后几行。
        private static void RegisterSpots()
        {
            try
            {
                if (_vm == null) return;
                PanelScreen.ClearSpots();
                float sh = PanelScreen.ScreenHeight();
                PanelScreen.AddSpot(500f, 20f, 60f, 58f, _vm.ExecuteClose);                    // X
                if (_vm.IsBuildMode)
                {
                    PanelScreen.AddSpot(26f, sh - 26f - 42f, 124f, 42f, _vm.ExecuteBack);      // 返回树
                    PanelScreen.AddSpot(160f, sh - 26f - 42f, 150f, 42f, _vm.ExecuteCallOut);  // v4.100: 唤出军团
                    // 建筑列表行(底部让出驻扎海军区块: 258 = 76 + 174 + 8)
                    int i = 0;
                    foreach (var row in _vm.Rows)
                    {
                        if (row == null) continue;
                        float y = 224f + i * 54f;
                        if (y + 54f > sh - SettlementDrawerVM.NavyReserve) break;
                        var r = row;
                        PanelScreen.AddSpot(448f, y + 6f, 42f, 42f, r.ExecuteRemove);          // −
                        PanelScreen.AddSpot(506f, y + 6f, 42f, 42f, r.ExecuteAdd);             // +
                        i++;
                    }
                    // v4.282: 驻扎的海军(最多 3 行; key = "fleet:<PartyId>", 按 key 反查, 不按下标)
                    //   ★ 行顶 = sh - 208 + i*44(SettlementDrawerVM.NavyRowH), 按钮在行内垂直居中(h30 -> 行顶 + 5)。
                    //     208 = 76(区块底距) + 132(ListPanel 高 = 3 行 × (40+4)); 与 prefab 的
                    //     MarginBottom=76 / SuggestedHeight=132 / 行高 40 / 行距 4 **逐一对应**。
                    if (_vm.ShowNavy)
                    {
                        int n = _vm.NavyRows != null ? _vm.NavyRows.Count : 0;
                        for (int k = 0; k < n && k < SettlementDrawerVM.NavyRowMax; k++)
                        {
                            string key = _vm.NavyKeyAt(k);
                            if (string.IsNullOrEmpty(key)) continue;
                            float ry = sh - 203f + k * SettlementDrawerVM.NavyRowH;
                            string kView = key, kRecall = key;
                            PanelScreen.AddSpot(380f, ry, 64f, 30f, delegate { _vm.NavyActionByKey(kView, 0); RegisterSpots(); });    // [查看]
                            PanelScreen.AddSpot(452f, ry, 64f, 30f, delegate { _vm.NavyActionByKey(kRecall, 1); RegisterSpots(); });  // [召回]
                        }
                    }
                }
                else
                {
                    int i = 0;
                    foreach (var node in _vm.Nodes)
                    {
                        if (node == null || !node.IsVisible) continue;
                        float y = 92f + i * 52f;
                        if (y > sh - 60f) break;
                        var n = node;
                        PanelScreen.AddSpot(0f, y, 42f, 52f, n.ExecuteToggle);                // 展开/折叠
                        PanelScreen.AddSpot(42f, y, 480f, 52f, n.ExecuteSelect);               // 选中(飘相机+看建筑)
                        i++;
                    }
                }
            }
            catch (Exception ex) { DLog.Force("抽屉热区失败: " + ex.Message); }
        }
    }
}
