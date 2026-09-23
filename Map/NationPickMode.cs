using System;
using System.Reflection;
using HarmonyLib;
using SandBox.View.Map;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Library;

namespace FeudalInternalAffairs
{
    // v4.75: 地图选国模式 —— 新档开局直接进地图:
    //   · 视角拉到地图中心并锁死最高(滚轮无效, 平移/旋转可用)
    //   · 领土恒显(国名+国家颜色正常填充, 不受缩放阈值影响)
    //   · 单击任意王国领地 -> 右侧弹出该国侧滑栏(国旗/君主/实力/专属加成)
    //   · 侧栏"开始游戏" -> 写入所选国家, 交给 NationalWillBehavior.TrySetup 正式接管
    internal static class NationPickMode
    {
        internal static bool Active { get; private set; }
        internal static Kingdom PendingKingdom;   // 当前侧栏展示的国家

        private static bool _mapCentered;
        private static MethodInfo _maxHeightGetter;

        internal static void Start()
        {
            Active = true;
            _mapCentered = false;
            PendingKingdom = null;
            DLog.Force("选国模式: 开启(新档在地图上选择国家)");
        }

        internal static void Stop()
        {
            if (!Active) return;
            Active = false;
            // 解锁时间: 回到正常可暂停的流逝
            try
            {
                if (Campaign.Current != null)
                {
                    SetTimeLock(false);
                    Campaign.Current.TimeControlMode = CampaignTimeControlMode.StoppablePlay;
                }
            }
            catch { }
            DLog.Force("选国模式: 结束(正式接管国家, 相机与时间解锁)");
        }

        internal static void Reset()
        {
            Active = false;
            PendingKingdom = null;
            _mapCentered = false;
        }

        // 由 FeudalMapView 每帧调用
        internal static void Tick(float dt)
        {
            if (!Active) return;
            try
            {
                // 锁屏(时间)不依赖相机就绪(进图最初几帧也生效)
                LockTime();
                HandlePickClick();

                var view = NationalWillCamera.View;
                if (view == null) return;

                // 锁死最高(每帧重置: 玩家滚轮缩放会被立刻拉回);
                // 居中走既有机制: MapCameraPatches.AfterSceneReady(场景就绪后再瞬移一次)
                NationalWillCamera.SetCameraDistance(view, MaxHeight());
            }
            catch (Exception ex) { DLog.Force("选国模式 tick 异常: " + ex.Message); }
        }

        // 进图/场景就绪后把相机拉到地图中心(只做一次; 由 MapCameraPatches.AfterSceneReady 按既有"就绪后再瞬移"机制调用)
        internal static void CenterOnMapCenterOnce()
        {
            if (_mapCentered) return;
            try
            {
                var view = NationalWillCamera.View;
                if (view == null) return;
                Vec2 min = Campaign.MapMinimumPosition;
                Vec2 max = Campaign.MapMaximumPosition;
                var target = new Vec3((min.x + max.x) * 0.5f, (min.y + max.y) * 0.5f, 0f, 1f) + Vec3.Up;
                NationalWillCamera.SetIdealTarget(view, target);
                NationalWillCamera.SetCameraTarget(view, target);   // 瞬移, 不滑
                NationalWillCamera.SetLastUsedTarget(view, target);
                _mapCentered = true;
                DLog.Force("选国模式: 相机已居中地图中心(锁最高)");
            }
            catch { }
        }

        // 锁屏: 世界时间强制静止(时间锁 + 模式锁, 玩家按键也无法恢复)
        private static void LockTime()
        {
            try
            {
                if (Campaign.Current != null)
                {
                    SetTimeLock(true);
                    if (Campaign.Current.TimeControlMode != CampaignTimeControlMode.Stop)
                        Campaign.Current.TimeControlMode = CampaignTimeControlMode.Stop;
                }
            }
            catch { }
        }

        // TimeControlModeLock 的 setter 非公开 -> 反射写
        private static PropertyInfo _timeLockProp;
        private static bool _timeLockSearched;

        internal static void SetTimeLock(bool locked)
        {
            try
            {
                if (!_timeLockSearched)
                {
                    _timeLockSearched = true;
                    _timeLockProp = AccessTools.Property(typeof(Campaign), "TimeControlModeLock");
                }
                var setter = _timeLockProp != null ? _timeLockProp.GetSetMethod(true) : null;
                if (setter != null) setter.Invoke(Campaign.Current, new object[] { locked });
            }
            catch { }
        }

        internal static float MaxHeight()
        {
            try
            {
                var view = NationalWillCamera.View;
                if (view != null)
                {
                    if (_maxHeightGetter == null)
                        _maxHeightGetter = AccessTools.PropertyGetter(typeof(MapCameraView), "MaximumCameraHeight");
                    if (_maxHeightGetter != null)
                    {
                        float h = (float)_maxHeightGetter.Invoke(view, null);
                        if (h > 60f) return h;
                    }
                }
            }
            catch { }
            return 700f;
        }

        // 自管点击(每帧轮询): 左键"单击"(松开且没有拖动过) 落在面板外的地图上 -> 按领地选国
        // 注意: 必须等松开+非拖动, 否则"按住左键拖动平移地图"会一按就弹出选国侧栏
        private static void HandlePickClick()
        {
            try
            {
                if (PanelInputGuard.ClicksSuppressed) return;   // v27.x: 开始游戏确认后的抑制窗口
                if (PanelInputGuard.SuppressAfterInquiry(TaleWorlds.InputSystem.Input.IsKeyDown(TaleWorlds.InputSystem.InputKey.LeftMouseButton))) return;   // v4.143: 弹窗/未松手抑制
                if (!TaleWorlds.InputSystem.Input.IsKeyReleased(TaleWorlds.InputSystem.InputKey.LeftMouseButton)) return;
                if (!MapBoxSelect.LastReleaseWasClick) return;   // 拖动(平移/框选)不算点击
                MapBoxSelect.ClearClickFlag();
                var m = TaleWorlds.InputSystem.Input.MousePositionPixel;
                if (m.Y < 66f) return;                      // 原版顶部地图栏
                if (m.X < 60f) return;                      // 左侧导航栏区域
                if (PanelScreen.IsMouseOnPanel()) return;   // 右侧选国侧栏
                if (NationalPanel.IsMouseOnFlag()) return;
                var pt = MapBoxSelect.CaptureGroundPoint();
                if (pt.Length < 0.001f) return;
                var k = TerritoryData.OwnerAt(pt.x, pt.y);
                if (k != null) NationPickPanel.Open(k);
                else MapSelection.Message("这里是无主荒地, 请点击有国家颜色的领地");
            }
            catch { }
        }

        // 地图点击领地: 命中哪个国家就开哪个侧栏; 恒返回 true(拦住原版点击链)
        internal static bool HandleMapClick()
        {
            try
            {
                var pt = MapBoxSelect.CaptureGroundPoint();
                if (pt.Length < 0.001f) return true;
                var k = TerritoryData.OwnerAt(pt.x, pt.y);
                if (k != null) NationPickPanel.Open(k);
                else MapSelection.Message("这里是无主荒地, 请点击有国家颜色的领地");
                return true;
            }
            catch (Exception ex)
            {
                DLog.Force("选国点击异常: " + ex.Message);
                return true;
            }
        }
    }
}
