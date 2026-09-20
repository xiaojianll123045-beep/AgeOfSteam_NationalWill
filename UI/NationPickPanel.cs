using System;
using SandBox.View.Map;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Engine.GauntletUI;

namespace FeudalInternalAffairs
{
    // v4.75: 地图选国侧滑栏(右侧) —— 自管层 + 热区轮询(不占 PanelScreen 的左侧停靠位)
    internal static class NationPickPanel
    {
        internal const float Width = 500f;

        private static GauntletLayer _layer;
        private static NationPickVM _vm;
        private static MapScreen _map;
        private static Kingdom _kingdom;

        internal static bool IsOpen { get { return _layer != null && _vm != null && _vm.IsPanelOpen; } }

        internal static void Open(Kingdom k)
        {
            try
            {
                if (k == null) return;
                var map = MapScreen.Instance;
                if (map == null) return;
                _kingdom = k;

                if (_layer == null || !ReferenceEquals(_map, map))
                {
                    CloseImmediate();
                    _vm = new NationPickVM();
                    _vm.OnStartGame = StartGame;
                    _layer = new GauntletLayer("FeudalNationPick", 345, false);   // v4.75o: 高于国名标签(335)
                    _layer.LoadMovie("FeudalNationPick", _vm);
                    map.AddLayer(_layer);
                    _map = map;
                    RegisterSpots();
                }
                _vm.Show(k);
                DLog.Force("选国侧栏: 显示 " + (k.Name != null ? k.Name.ToString() : k.StringId));
            }
            catch (Exception ex) { DLog.Force("选国侧栏打开失败: " + ex.Message); }
        }

        internal static void Close()
        {
            try { if (_vm != null) _vm.Hide(); } catch { }
        }

        private static void CloseImmediate()
        {
            try { if (_layer != null && _map != null) _map.RemoveLayer(_layer); } catch { }
            _layer = null; _vm = null; _map = null;
        }

        // 每帧(FeudalMapView 调用): 推进滑入/滑出动画 + 滚轮滚动信息列表; 滑出到头后拆层
        internal static void Tick(float dt)
        {
            try
            {
                if (_vm == null) return;
                _vm.TickAnim(dt);

                // v4.75p: 滚轮滚动信息列表(鼠标在面板上时)
                if (_vm.IsPanelOpen && PanelScreen.IsMouseOnPanel())
                {
                    float wheel = TaleWorlds.InputSystem.Input.DeltaMouseScroll;
                    if (Math.Abs(wheel) > 0.01f)
                    {
                        _wheelAcc += wheel;
                        if (Math.Abs(_wheelAcc) >= 1f)
                        {
                            _vm.Scroll(_wheelAcc > 0f ? -1 : 1);   // 滚轮上 -> 看前面的信息
                            _wheelAcc = 0f;
                        }
                    }
                }

                if (!_vm.IsPanelOpen && !_vm.IsPanelVisible && _layer != null)
                {
                    CloseImmediate();
                    if (!PanelScreen.AnyOpen) PanelScreen.ClearSpots();
                }
            }
            catch { }
        }

        private static float _wheelAcc;

        // 热区: "开始游戏"按钮(prefab: 面板宽500, 按钮 左右24 高58 底边距24)
        private static void RegisterSpots()
        {
            try
            {
                PanelScreen.ClearSpots();
                float sw = PanelScreen.ScreenWidth();
                float sh = PanelScreen.ScreenHeight();
                PanelScreen.AddSpotRaw(sw - Width + 24f, sh - 24f - 58f, Width - 48f, 58f, StartGame);
                DLog.Force("选国侧栏: 已注册开始游戏热区(屏幕宽 " + (int)sw + ")");
            }
            catch (Exception ex) { DLog.Force("选国侧栏热区注册失败: " + ex.Message); }
        }

        // 玩家点击"开始游戏": 记录所选国家 -> 关闭侧栏 -> 退出选国模式(相机解锁),
        // 之后 NationalWillBehavior.TrySetup 会在下一帧读到 ChosenKingdomId 并正式接管
        internal static void StartGame()
        {
            try
            {
                if (_kingdom == null) return;
                NationChoice.ChosenKingdomId = _kingdom.StringId;
                NationPickMode.Stop();   // 立刻退出选国模式(即使后面的过渡动画/层出问题, 接管也能照常进行)
                string kname = _kingdom.Name != null ? _kingdom.Name.ToString() : "这个国家";
                DLog.Force("地图选国: 玩家选择 " + _kingdom.StringId + " (" + kname + ") -> 开始游戏");
                CloseImmediate();
                PanelScreen.ClearSpots();
                StartGameFade.Show("正在接管 " + kname + " ……");   // 黑屏过渡(锁输入)
                try
                {
                    MapSelection.Message("你选择了 " + kname + ", 即将成为它的国家意志……");
                }
                catch { }
            }
            catch (Exception ex) { DLog.Force("开始游戏失败: " + ex.Message); }
        }
    }
}
