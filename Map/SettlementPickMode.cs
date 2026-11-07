using System;
using SandBox.View.Map;
using SandBox.View.Map.Visuals;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.InputSystem;

namespace FeudalInternalAffairs
{
    // ==================== v4.282: 地图"点选聚落"公共机制 ====================
    //
    // 【为什么要有这个文件 —— 原来只有军务页有, 海军页(以及以后的任何页)再抄一份就是两套】
    //   军务页(v4.98)的点选机制分散在三处:
    //     ① UI\ArmyPanel.cs  : `_mapPicking` 标志 + `PollMapPick()` 每帧轮询(面板打开时原版
    //        `SettlementVisual.OnMapClick` 的点击链被输入屏蔽吃掉, 必须自研);
    //     ② Map\MapClickPatches.cs: 三条转发分支(GroundClick / 右键链 / SettlementClick) +
    //        一条"点选期间不弹百科";
    //     ③ UI\ArmyVM.cs     : `StartMapPick/TryPickSettlement` 业务侧(选中/合法性/提示)。
    //   海军页需要同一能力(造船港/敌港/海运两端港/进驻城市), 所以把 ①② 抽成这一份**公共注册表**:
    //     · 同一时刻只允许一个"点选请求"(owner = 发起面板 key), 后发起的顶掉先发起的;
    //     · 取消方式三条(全部实现, 保证"进得去出得来"): 再点一次发起按钮 / 右键 / Esc;
    //     · 命中判定与军务页**逐行等价**(图标优先 -> 120 距离内最近聚落), 只是搬了个家;
    //     · 合法性过滤**不在这里** —— 那是各页自己的业务(例如海军的"是否沿海/是否自有/是否敌国"),
    //       本类只负责"把玩家点到的那个聚落送到回调里", 拒绝理由由回调写进它自己的状态行。
    //
    // 【与 MapClickPatches 的分工】
    //   MapClickPatches 负责"原版点击链"里的入口(它在原版 ProcessTravel/OnMapClick 上打了补丁,
    //   是**最先**收到点击的地方): 点选期间它只做一件事 —— 调 `Deliver(...)` 或 `Cancel()` 并吞掉点击。
    //   本类的 `Poll()` 是**兜底**(与军务页原来的 PollMapPick 同源): 万一某条原版点击链没被拦到,
    //   每帧的鼠标自检仍能命中。两条路径都以本类为唯一状态源, 不会出现两套状态。
    internal static class SettlementPickMode
    {
        // 发起方面板 key(诊断/互斥用; 目前只有 "army" 与 "navy")
        internal const string OwnerArmy = "army";
        internal const string OwnerNavy = "navy";

        private static Action<Settlement> _sink;   // 非空 = 正在点选
        private static string _owner = "";
        private static string _prompt = "";

        internal static bool Active { get { return _sink != null; } }
        internal static string Owner { get { return _owner ?? ""; } }
        internal static bool Is(string owner) { return _sink != null && string.Equals(_owner, owner, StringComparison.Ordinal); }

        // 进入点选模式。prompt = 给玩家的中文提示(会同时进地图消息流)。
        internal static void Begin(string owner, string prompt, Action<Settlement> sink)
        {
            try
            {
                if (sink == null) return;
                if (_sink != null && !Is(owner))
                    DLog.Force("地图点选: 「" + _owner + "」的待选被「" + owner + "」顶掉(同一时刻只允许一个)");
                _owner = owner ?? "";
                _prompt = prompt ?? "";
                _sink = sink;
                if (!string.IsNullOrEmpty(_prompt)) MapSelection.Message(_prompt);
            }
            catch (Exception ex) { DLog.Force("进入地图点选失败: " + ex.Message); }
        }

        // 退出点选模式(不改任何业务状态)。返回是否真的退出了一次进行中的点选。
        internal static bool Cancel()
        {
            try
            {
                if (_sink == null) return false;
                _sink = null; _owner = ""; _prompt = "";
                return true;
            }
            catch { return false; }
        }

        // 命中一个聚落 -> 交给发起方回调。s == null 视为"只结束, 不回调"。
        //   ★ 先摘掉回调再调用: 回调内部通常还会再调 Cancel()/Begin()(选中后退出/连选),
        //     重入时状态已经清空, 不会把新请求误清。
        internal static bool Deliver(Settlement s)
        {
            var act = _sink;
            if (act == null) return false;
            string who = _owner;
            _sink = null; _owner = ""; _prompt = "";
            if (s == null) return true;
            try { act(s); }
            catch (Exception ex) { DLog.Force("地图点选回调异常(" + who + "): " + ex.Message); }
            return true;
        }

        // 发起按钮的"再点一次 = 取消"由各页自己判(它们才知道当前选的是哪个目标);
        //   这里只提供一句统一的取消提示。
        internal const string CancelHint = "已取消地图选点";

        // ==================== 每帧轮询(兜底路径) ====================
        //   由发起面板的 OnTick 每帧调用(必须在 3 秒节流刷新**之前**, 见 NavyPanel.OnTick)。
        internal static void Poll()
        {
            try
            {
                if (_sink == null) return;

                // ① 取消: 右键(按下边沿) —— 与地图右键菜单互斥, 见 MapClickPatches 的右键链
                if (Input.IsKeyPressed(InputKey.RightMouseButton))
                {
                    if (Cancel()) { MapSelection.Message(CancelHint + "(右键)"); DLog.Force("地图点选: 右键取消(" + _owner + ")"); }
                    return;
                }
                // ② 取消: Esc(原版 Esc 会开菜单, 但至少"出得来"; 再点一次发起按钮是第三条路)
                if (Input.IsKeyPressed(InputKey.Escape))
                {
                    if (Cancel()) MapSelection.Message(CancelHint + "(Esc)");
                    return;
                }

                // ③ 左键点地图 = 选中光标下的聚落
                if (PanelScreen.IsMouseOnPanel()) return;
                if (PanelInputGuard.AnyPopupActive()) return;
                if (PanelInputGuard.SuppressAfterInquiry(Input.IsKeyDown(InputKey.LeftMouseButton))) return;
                if (!Input.IsKeyPressed(InputKey.LeftMouseButton)) return;
                var mouse = Input.MousePositionPixel;
                if (mouse.X < PanelScreen.PanelX || mouse.Y < 66f) return;   // 导航栏/顶部地图栏不算点地图

                var picked = UnderCursor();
                if (picked != null) { Deliver(picked); return; }
                MapSelection.Message("地图点选: 未命中聚落, 请点击城市图标(右键或 Esc 取消)");
            }
            catch (Exception ex) { DLog.Force("地图点选轮询异常: " + ex.Message); }
        }

        // 光标下的聚落: ① tooltip 指向的聚落图标 ② 否则 120 距离内最近的聚落(点图标附近地面也认)
        //   —— 与 UI\ArmyPanel.PollMapPick(v4.98) 的判定逐行一致。
        internal static Settlement UnderCursor()
        {
            try
            {
                Settlement picked = null;
                try
                {
                    var screen = MapScreen.Instance;
                    var vis = screen != null ? screen.CurrentVisualOfTooltip as SettlementVisual : null;
                    if (vis != null && vis.MapEntity != null) picked = vis.MapEntity.Settlement;
                }
                catch { }
                if (picked != null) return picked;

                var pt = MapBoxSelect.CaptureGroundPoint();
                if (pt.z > -5000f)
                {
                    float bd = 120f;
                    foreach (var x in Settlement.All)
                    {
                        if (x == null) continue;
                        float dx = x.Position.X - pt.x, dy = x.Position.Y - pt.y;
                        float d = (float)Math.Sqrt(dx * dx + dy * dy);
                        if (d < bd) { bd = d; picked = x; }
                    }
                }
                return picked;
            }
            catch { return null; }
        }
    }
}
