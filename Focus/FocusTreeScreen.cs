using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.Library;
using TaleWorlds.ScreenSystem;

namespace FeudalInternalAffairs
{
    // 国策树屏幕(自建 Gauntlet 屏幕)
    internal class FocusTreeScreen : ScreenBase
    {
        private static FocusTreeScreen _current;
        private GauntletLayer _layer;
        private FocusTreeVM _vm;
        private bool _paused;
        private CampaignTimeControlMode _prevTimeControl;

        internal static bool IsOpen { get { return _current != null; } }

        // 我们自己在轮询输入时置 true, 让拦截补丁放行
        internal static bool PollingInput;

        internal static FocusTreeScreen Current { get { return _current; } }

        private Vec2 _lastMouse;
        private bool _hasLastMouse;

        // 每帧轮询输入(由 FocusTreeMapGuardPatch 的 prefix 调用):
        //   滚轮=缩放, WASD=平移面板, 鼠标左右键拖动=移动面板
        internal static void PollInput()
        {
            var scr = _current;
            if (scr == null || scr._vm == null) return;
            PollingInput = true;
            try
            {
                var vm = scr._vm;

                float scroll = TaleWorlds.InputSystem.Input.DeltaMouseScroll;
                if (Math.Abs(scroll) > 0.01f)
                {
                    if (scroll > 1f) scroll = 1f;
                    if (scroll < -1f) scroll = -1f;
                    vm.ZoomBy(scroll * 0.04f);
                }

                float step = 9f;
                float dx = 0f, dy = 0f;
                if (TaleWorlds.InputSystem.Input.IsKeyDown(TaleWorlds.InputSystem.InputKey.A)) dx += step;
                if (TaleWorlds.InputSystem.Input.IsKeyDown(TaleWorlds.InputSystem.InputKey.D)) dx -= step;
                if (TaleWorlds.InputSystem.Input.IsKeyDown(TaleWorlds.InputSystem.InputKey.W)) dy += step;
                if (TaleWorlds.InputSystem.Input.IsKeyDown(TaleWorlds.InputSystem.InputKey.S)) dy -= step;

                var mouse = TaleWorlds.InputSystem.Input.MousePositionPixel;
                bool dragging = TaleWorlds.InputSystem.Input.IsKeyDown(TaleWorlds.InputSystem.InputKey.LeftMouseButton)
                                || TaleWorlds.InputSystem.Input.IsKeyDown(TaleWorlds.InputSystem.InputKey.RightMouseButton);
                if (dragging)
                {
                    if (scr._hasLastMouse)
                    {
                        dx += mouse.X - scr._lastMouse.X;
                        dy += mouse.Y - scr._lastMouse.Y;
                    }
                    scr._lastMouse = mouse;
                    scr._hasLastMouse = true;
                }
                else
                {
                    scr._hasLastMouse = false;
                }

                if (Math.Abs(dx) > 0.01f || Math.Abs(dy) > 0.01f) vm.Pan(dx, dy);
            }
            catch { }
            finally { PollingInput = false; }
        }

        internal static void Open()
        {
            try
            {
                if (_current != null) return;
                ScreenManager.PushScreen(new FocusTreeScreen());
            }
            catch (Exception ex) { DLog.Force("打开国策树失败: " + ex.Message); }
        }

        internal static void Close()
        {
            try { if (_current != null) ScreenManager.PopScreen(); }
            catch (Exception ex) { DLog.Force("关闭国策树失败: " + ex.Message); }
        }

        protected override void OnInitialize()
        {
            base.OnInitialize();
            try
            {
                MouseVisible = true;
                _vm = new FocusTreeVM();
                _layer = new GauntletLayer("FeudalFocusTree", 345, false);   // v4.75o: 高于国名标签(335)
                _layer.LoadMovie("FeudalFocusTree", _vm);
                try { _layer.InputRestrictions.SetInputRestrictions(); } catch { }
                AddLayer(_layer);
                // 打开时暂停战役(和原版屏幕一致, 否则地图继续 tick, 海战可视化会崩)
                try
                {
                    if (Campaign.Current != null)
                    {
                        _prevTimeControl = Campaign.Current.TimeControlMode;
                        Campaign.Current.TimeControlMode = CampaignTimeControlMode.Stop;
                        _paused = true;
                    }
                }
                catch { }
                _current = this;
                DLog.Force("国策树已打开");
            }
            catch (Exception ex)
            {
                // 初始化失败必须回滚, 否则 _current 非空 -> IsOpen=true -> 地图视觉 tick 被跳过 -> 整个游戏卡死
                DLog.Force("国策树初始化失败: " + ex.Message);
                try { if (_layer != null) { RemoveLayer(_layer); _layer = null; } } catch { }
                try
                {
                    if (_paused && Campaign.Current != null)
                    {
                        Campaign.Current.TimeControlMode = _prevTimeControl;
                        _paused = false;
                    }
                }
                catch { }
                _current = null;
                try { ScreenManager.PopScreen(); } catch { }
            }
        }

        protected override void OnActivate()
        {
            base.OnActivate();
            MouseVisible = true;
        }

        protected override void OnFinalize()
        {
            try
            {
                if (_paused && Campaign.Current != null)
                {
                    Campaign.Current.TimeControlMode = _prevTimeControl;
                    _paused = false;
                }
            }
            catch { }
            try
            {
                if (_layer != null) { RemoveLayer(_layer); _layer = null; }
            }
            catch { }
            _current = null;
            DLog.Force("国策树已关闭");
            base.OnFinalize();
        }

        // 点击国策 -> 原版询问框显示详情 + 开始
        internal static void ShowStartConfirm(FocusDefinition def, Action onDone)
        {
            try
            {
                MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                    def.Name,
                    BuildBody(def) + "\n\n点击「开始」后将开始推进该政策。",
                    new List<InquiryElement> { new InquiryElement("start", "开始", null, true, null) },
                    true, 1, 1, "确定", "取消",
                    delegate (List<InquiryElement> sel)
                    {
                        if (sel != null && sel.Count > 0)
                        {
                            FocusTreeData.Start(def);
                            MapSelection.Message("已开始政策: " + def.Name);
                            DLog.Force("国策开始: " + def.Name);
                            if (onDone != null) onDone();
                        }
                    }, null, null, false));
            }
            catch (Exception ex) { DLog.Force("国策弹窗失败: " + ex.Message); }
        }

        internal static void ShowInfo(FocusDefinition def, string extra)
        {
            try
            {
                MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                    def.Name,
                    BuildBody(def) + (string.IsNullOrEmpty(extra) ? "" : "\n\n" + extra),
                    new List<InquiryElement> { new InquiryElement("ok", "知道了", null, true, null) },
                    true, 1, 1, "确定", "取消", null, null, null, false));
            }
            catch (Exception ex) { DLog.Force("国策信息弹窗失败: " + ex.Message); }
        }

        private static string BuildBody(FocusDefinition def)
        {
            return "所需时间: " + def.Days + " 日\n"
                 + "前置: " + FocusTreeData.RequirementText(def) + "\n\n"
                 + def.Description + "\n\n"
                 + "效果:\n" + def.Effects;
        }
    }
}
