using System;
using HarmonyLib;
using TaleWorlds.Core;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade.GauntletUI;

namespace FeudalInternalAffairs
{
    // v4.86: 自建侧边栏(内政面板/选国侧栏)打开时拦截回车键。
    //   原版回车 = 打开聊天日志(消息日志), 在面板里输入数字后按回车会误触发(用户要求拦截)。
    //   双保险: ①拦聊天视图的输入处理 ②面板打开时让 Input.IsKeyPressed 读不到回车。
    //   注意: 自建面板自己的回车处理(ArmyVM 数量键入选定)用 Input.IsKeyDown 边沿自检, 不受影响。
    internal static class PanelInputGuard
    {
        internal static bool Blocked
        {
            get
            {
                try { return PanelScreen.AnyOpen || NationPickPanel.IsOpen; }
                catch { return false; }
            }
        }

        // v4.143: 系统弹窗(Inquiry)鼠标抑制(用户要求: 退出弹窗时若没松手也不触发地图点击)
        //   弹窗打开期间一律抑制; 若弹窗期间鼠标按下过, 关闭后仍未松手则继续抑制, 直到松手才恢复
        private static bool _heldDuringInquiry;
        private static int _swallowAtMs;       // v4.145: 吞掉"弹窗期间按下后的松开"的短窗口(同一次点击会触发多个原版入口)

        // v4.144: MultiSelectionInquiry 是独立系统, InformationManager.IsAnyInquiryActive() 检测不到!
        //   我们的弹窗全部走 ShowPopup 包装 -> 用计数精确追踪开关; 地图点击在弹窗期间全部屏蔽
        private static int _popupOpen;
        private static int _popupShownMs;      // 本次弹窗显示时刻(防回调丢失卡死: 15 分钟兜底)
        private static int _lastAnyShownMs;    // 任意多选弹窗(含原版)最近显示时刻: 覆盖"点击打开弹窗那一下"的松开

        // v27.x: 选国"开始游戏"后的点击抑制窗口(修: 确认点击被地图点击链/新面板热区吃到, 开局误开驻军页)
        //   StartGame 时置窗, 窗口内所有地图点击(BlockMapClick/HandleLeftClickGround)与面板热区(PollSpots)统一忽略
        private const int StartGameSuppressMs = 800;
        private static int _clickSuppressAtMs;

        internal static void SuppressClicksAfterStartGame()
        {
            try { _clickSuppressAtMs = Environment.TickCount; } catch { }
        }

        internal static bool ClicksSuppressed
        {
            get
            {
                try { return !Elapsed(_clickSuppressAtMs, StartGameSuppressMs); }
                catch { return false; }
            }
        }

        private static bool Elapsed(int sinceMs, int ms)
        {
            return unchecked(Environment.TickCount - sinceMs) > ms;
        }

        internal static bool AnyPopupActive()
        {
            try
            {
                if (_popupOpen > 0)
                {
                    if (Elapsed(_popupShownMs, 900000)) _popupOpen = 0;   // 兜底: 回调丢失(弹窗被顶掉等)时不永久卡死
                    else return true;
                }
                if (!Elapsed(_lastAnyShownMs, 350)) return true;          // 刚弹出的那一瞬间
                if (InformationManager.IsAnyInquiryActive()) return true;
                if (QueryManagerActive()) return true;                    // v4.145: 原版弹窗静态字段直读(含全部原版/其他 mod 弹窗)
                return false;
            }
            catch { return _popupOpen > 0; }
        }

        // v4.145: GauntletQueryManager._activeDataSource(private static) != null 即任意弹窗打开中
        //   (原版就是用它实现 InformationManager.IsAnyInquiryActive 的, 这里直读更稳, 不依赖委托链)
        private static System.Reflection.FieldInfo _activeDsField;
        internal static bool QueryManagerActive()
        {
            try
            {
                if (_activeDsField == null)
                {
                    var t = AccessTools.TypeByName("TaleWorlds.MountAndBlade.GauntletUI.GauntletQueryManager");
                    if (t == null) return false;
                    _activeDsField = AccessTools.Field(t, "_activeDataSource");
                    if (_activeDsField == null) return false;
                }
                return _activeDsField.GetValue(null) != null;
            }
            catch { return false; }
        }

        // 原版/其他 mod 的多选弹窗: 显示瞬间记录(无法追踪其关闭, 只覆盖打开那一下)
        [HarmonyPatch(typeof(MBInformationManager), "ShowMultiSelectionInquiry")]
        internal static class AnyMultiInquiryShownPatch
        {
            private static void Prefix()
            {
                try { _lastAnyShownMs = Environment.TickCount; } catch { }
            }
        }

        // 我们的多选弹窗统一入口(签名与 MBInformationManager.ShowMultiSelectionInquiry 一致: data, pauseGameActiveState=false, prioritize=false)
        internal static void ShowPopup(MultiSelectionInquiryData data, bool pauseGameActiveState = false, bool prioritize = false)
        {
            try
            {
                if (data == null) return;
                _popupOpen = 1;
                _popupShownMs = Environment.TickCount;
                var wrapped = new MultiSelectionInquiryData(
                    data.TitleText, data.DescriptionText, data.InquiryElements, data.IsExitShown,
                    data.MinSelectableOptionCount, data.MaxSelectableOptionCount,
                    data.AffirmativeText, data.NegativeText,
                    delegate (System.Collections.Generic.List<InquiryElement> s)
                    {
                        _popupOpen = 0;
                        try { if (data.AffirmativeAction != null) data.AffirmativeAction(s); } catch (Exception ex) { DLog.Force("弹窗确认回调异常: " + ex.Message); }
                    },
                    delegate (System.Collections.Generic.List<InquiryElement> c)
                    {
                        _popupOpen = 0;
                        try { if (data.NegativeAction != null) data.NegativeAction(c); } catch (Exception ex) { DLog.Force("弹窗取消回调异常: " + ex.Message); }
                    },
                    data.SoundEventPath, data.IsSeachAvailable);
                MBInformationManager.ShowMultiSelectionInquiry(wrapped, pauseGameActiveState, prioritize);
            }
            catch (Exception ex) { DLog.Force("显示弹窗失败: " + ex.Message); _popupOpen = 0; }
        }

        internal static bool SuppressAfterInquiry(bool mouseDown)
        {
            try
            {
                if (AnyPopupActive())
                {
                    if (mouseDown) _heldDuringInquiry = true;
                    return true;
                }
                if (_heldDuringInquiry)
                {
                    if (mouseDown) return true;              // 弹窗已关但仍按着 -> 继续抑制
                    _heldDuringInquiry = false;              // 检测到松开: 这次松开就是"点弹窗的松开" -> 吞掉
                    _swallowAtMs = Environment.TickCount;    // 并给同帧的其他原版入口开一个短窗口
                    return true;
                }
                if (unchecked(Environment.TickCount - _swallowAtMs) < 150) return true;   // 同一次点击的其他入口
            }
            catch { }
            return false;
        }

        // v4.144: 地图点击总闸(原版点击链用): 弹窗期间 / 未松手抑制 / 鼠标在自建面板或最左导航栏竖条上 -> 一律不当地图点击
        // v27.x: "开始游戏"确认后的抑制窗口内同样一律忽略(防误开驻军页)
        internal static bool BlockMapClick()
        {
            try
            {
                if (ClicksSuppressed) return true;
                if (SuppressAfterInquiry(TaleWorlds.InputSystem.Input.IsKeyDown(TaleWorlds.InputSystem.InputKey.LeftMouseButton))) return true;
                if (PanelScreen.IsMouseOnPanel()) return true;
                if (TaleWorlds.InputSystem.Input.MousePositionPixel.X < PanelScreen.PanelX) return true;
            }
            catch { }
            return false;
        }

        // v4.145: 诊断(用户复现穿透时看日志): 未拦住的点击且最近 10 秒内显示过弹窗 -> 记录现场
        internal static void DiagClick(string where)
        {
            try
            {
                int dt = unchecked(Environment.TickCount - _lastAnyShownMs);
                if (dt < 10000)
                {
                    var m = TaleWorlds.InputSystem.Input.MousePositionPixel;
                    DLog.Force("点击诊断[" + where + "]: open=" + _popupOpen + " 最近弹窗=" + dt + "ms前 按着=" + _heldDuringInquiry
                        + " 鼠标=(" + (int)m.X + "," + (int)m.Y + ") inquiry=" + InformationManager.IsAnyInquiryActive()
                        + " 面板=" + PanelScreen.IsMouseOnPanel());
                }
            }
            catch { }
        }

        private static bool EnterKey(InputKey key)
        {
            return key == InputKey.Enter || key == InputKey.NumpadEnter;
        }

        // v4.144: 弹窗期间鼠标键屏蔽 -> 已撤销!
        //   实测原版 Gauntlet 弹窗按钮的点击检测也经过 Input.IsKeyPressed(左键), 全局屏蔽会导致弹窗按钮点不动。
        //   改为: 所有地图轮询点显式检查 AnyPopupActive()/SuppressAfterInquiry(), 原版点击链走 BlockMapClick() 总闸。
        [HarmonyPatch(typeof(Input), "IsKeyPressed")]
        internal static class InputMousePressedPatch
        {
            private static bool Prefix(InputKey key, ref bool __result)
            {
                try
                {
                    if (EnterKey(key) && Blocked) { __result = false; return false; }
                }
                catch { }
                return true;
            }
        }

        [HarmonyPatch(typeof(Input), "IsKeyReleased")]
        internal static class InputMouseReleasedPatch
        {
            private static bool Prefix(InputKey key, ref bool __result)
            {
                try
                {
                    if (EnterKey(key) && Blocked) { __result = false; return false; }
                }
                catch { }
                return true;
            }
        }

        // ① 聊天日志视图的输入处理: 面板打开时把回车这一帧吞掉
        [HarmonyPatch(typeof(GauntletChatLogView), "HandleInput")]
        internal static class ChatLogHandleInputPatch
        {
            private static bool Prefix(ref bool chatOpened, ref bool chatClosed)
            {
                try
                {
                    if (!Blocked) return true;
                    if (Input.IsKeyPressed(InputKey.Enter) || Input.IsKeyPressed(InputKey.NumpadEnter))
                    {
                        chatOpened = false;
                        chatClosed = false;
                        return false;
                    }
                }
                catch { }
                return true;
            }
        }

        // ② 面板打开时原版读不到回车(见上 InputMousePressedPatch/ReleasedPatch 内的 Enter 分支)
    }
}
