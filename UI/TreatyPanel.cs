using System;

namespace FeudalInternalAffairs
{
    // v4.204: 外交条约页(自己实现的 680 宽侧栏页; 取代系统询问框)
    //   热区: 条款等级 x3(y116) / 缔约行 6 行 x 4 个图标(y194 起, 每行 46+4) / 生效条约 10 行(y526, 每行 42+4) / 关闭
    internal static class TreatyPanel
    {
        private const string Key = "treaty";
        private const string Movie = "FeudalTreaty";
        internal const float Width = 680f;
        private static TreatyVM _vm;

        internal static bool IsOpen { get { return PanelScreen.IsOpen(Key); } }
        internal static TreatyVM VM { get { return _vm; } }

        internal static void Open()
        {
            try
            {
                if (IsOpen)
                {
                    if (_vm != null) { _vm.Refresh(); RegisterSpots(); }
                    return;
                }
                _vm = new TreatyVM(Close);
                _vm.OpenPanelAnim(Width);
                PanelScreen.OpenPanel(Key, Movie, _vm, null,
                    delegate (float dt) { if (_vm != null) { _vm.TickAnim(dt); RegisterSpots(); } }, null);
                RegisterSpots();
                DLog.Force("条约页已打开");
            }
            catch (Exception ex) { DLog.Force("打开条约页失败: " + ex.Message); }
        }

        internal static void Close()
        {
            try { PanelScreen.ClosePanel(Key); }
            catch { }
        }

        private static void Spot(float x, float y, float w, float h, Action act)
        {
            PanelScreen.AddSpotRaw(PanelScreen.PanelX + x, y, w, h, act);
        }

        private static void RegisterSpots()
        {
            try
            {
                if (_vm == null) return;
                PanelScreen.ClearSpots();
                // 条款等级
                Spot(26f, 116f, 200f, 40f, delegate { _vm.SetLevel(1); });
                Spot(238f, 116f, 200f, 40f, delegate { _vm.SetLevel(2); });
                Spot(450f, 116f, 200f, 40f, delegate { _vm.SetLevel(3); });
                // 缔约: 每行 4 个图标按钮(与 prefab 中 x=250/356/462/556 对齐)
                int cn = _vm.Cands.Count;
                for (int i = 0; i < cn && i < 6; i++)
                {
                    int ci = i;
                    float y = 194f + i * 50f;
                    Spot(250f, y, 100f, 46f, delegate { _vm.SignClick(ci, 0); });
                    Spot(356f, y, 100f, 46f, delegate { _vm.SignClick(ci, 1); });
                    Spot(462f, y, 100f, 46f, delegate { _vm.SignClick(ci, 2); });
                    Spot(556f, y, 98f, 46f, delegate { _vm.SignClick(ci, 3); });
                }
                // 生效中条约: 点行撕毁
                int an = _vm.Actives.Count;
                for (int i = 0; i < an && i < 10; i++)
                {
                    int ai = i;
                    Spot(26f, 526f + i * 46f, 628f, 42f, delegate { _vm.BreakClick(ai); });
                }
                // 关闭
                Spot(30f, PanelScreen.ScreenHeight() - 26f - 42f, 122f, 42f, _vm.ExecuteClose);
            }
            catch (Exception ex) { DLog.Force("条约页热区失败: " + ex.Message); }
        }
    }
}
