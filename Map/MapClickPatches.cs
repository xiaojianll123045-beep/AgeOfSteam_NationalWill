using System;
using HarmonyLib;
using SandBox.View.Map;
using SandBox.View.Map.Visuals;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameState;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace FeudalInternalAffairs
{
    // 接管单机地图的左键效果:
    //   左键地面     -> 让选中部队前往该点(玩家没有部队, 绝不移动玩家部队)
    //   左键我方部队 -> 选中
    //   左键敌军部队 -> 让选中部队攻击
    //   左键定居点   -> 让选中部队前往
    internal static class MapClickPatches
    {
        [HarmonyPatch(typeof(MapState), "ProcessTravel")]
        internal static class GroundClick
        {
            private static bool Prefix(CampaignVec2 moveTargetPoint)
            {
                try { PartyInfoHud.Hide(); } catch { }

                try
                {
                    if (Outliner.BlocksMapInput) return false;
                    // v4.144: 弹窗期间 / 鼠标在自建面板或导航栏上 -> 不算地图点击(修: 点弹窗被当成点地面)
                    if (PanelInputGuard.BlockMapClick()) return false;
                    if (PartyInfoHud.IsMouseOnButtons()) return false;   // 28.6: 点在部队小窗选项按钮上
                    PanelInputGuard.DiagClick("地面");
                    // v4.75: 新档选国模式: 点领地 -> 右侧国家栏(拦住原版点击)
                    if (NationPickMode.Active)
                    {
                        if (!PanelScreen.IsMouseOnPanel()) NationPickMode.HandleMapClick();
                        return false;
                    }
                    if (!NationalWillOrders.IsActive) return true;
                    DLog.Force("地面点击: 判定 旗=" + NationalPanel.IsMouseOnFlag() + " 面板=" + PanelScreen.IsMouseOnPanel()
                        + " 框=" + MapBoxSelect.BoxVisible + " 选中=" + MapSelection.Count);
                    // 点在左上角国旗上 -> 交给国家面板处理, 别当地图点击
                    if (NationalPanel.IsMouseOnFlag()) return false;
                    // 点在打开的侧边栏面板上 -> 不算地图点击(否则点面板按钮会同时给部队下令)
                    if (PanelScreen.IsMouseOnPanel()) return false;
                    if (MapBoxSelect.BoxVisible) return false;   // 框选拖动中, 不当点击
                    // 军务页/海军页"地图点选城市"模式: 地面点击不下部队命令, 改为命中检测(30 距离内视为点中该定居点)
                    // (地面点击会先于定居点点击触发, 所以这里不取消待选状态; 取消=再点一次[地图点选]按钮 / 右键 / Esc)
                    // v4.282: 判据从 `ArmyPanel.IsPickingSettlement` 改成公共注册表 —— 海军页的
                    //   "造船港/敌港/海运两端港/进驻城市"四个选城入口走的是同一份状态(见 Map\SettlementPickMode.cs)。
                    if (SettlementPickMode.Active)
                    {
                        try
                        {
                            var pt = MapBoxSelect.CaptureGroundPoint();
                            var at = TerritoryData.SettlementAt(pt.x, pt.y);
                            if (at != null && (at.Position.AsVec3() - pt).LengthSquared < 30f * 30f)
                                SettlementPickMode.Deliver(at);
                        }
                        catch { }
                        return false;
                    }
                    // 点在选中圈范围内 = 点在该部队上(圈比图标大)
                    var ringHit = MapBoxSelect.HitSelectedRing();
                    if (ringHit != null)
                    {
                        MapSelection.Toggle(ringHit);
                        return false;
                    }
                    var selected = MapSelection.Selected;
                    if (selected == null)
                    {
                        DLog.Info("地面点击: 没有选中部队, 忽略(玩家没有部队可移动)");
                        return false;
                    }
                    NationalWillOrders.MoveToPoint(moveTargetPoint);
                    return false;
                }
                catch (Exception ex) { DLog.Force("GroundClick 异常: " + ex.Message); return true; }
            }
        }

        [HarmonyPatch(typeof(MobilePartyVisual), "OnMapClick")]
        internal static class PartyClick
        {
            private static bool Prefix(MobilePartyVisual __instance, bool followModifierUsed, ref bool __result)
            {
                if (Outliner.BlocksMapInput) { __result = true; return false; }
                try { DLog.Force("部队小窗: 点击收到(补丁已触发)"); PartyInfoHud.Show(__instance != null && __instance.MapEntity != null ? __instance.MapEntity.MobileParty : null); } catch { }

                try
                {
                    // v4.144: 弹窗期间 / 鼠标在自建面板或导航栏上 -> 不当成点部队
                    if (PanelInputGuard.BlockMapClick()) { __result = true; return false; }
                    if (PartyInfoHud.IsMouseOnButtons()) { __result = true; return false; }   // 28.6: 小窗选项按钮
                    PanelInputGuard.DiagClick("部队");
                    // v4.75: 选国模式: 点部队不处理(只认领地)
                    if (NationPickMode.Active) { __result = true; return false; }
                    if (followModifierUsed || !NationalWillOrders.IsActive) return true;
                    if (PanelScreen.IsMouseOnPanel()) { __result = true; return false; }   // 点面板不当成点部队
                    if (MapBoxSelect.BoxVisible) { __result = true; return false; }   // 框选拖动中, 不当点击
                    var party = __instance != null && __instance.MapEntity != null ? __instance.MapEntity.MobileParty : null;
                    if (party == null) return true;
                    __result = true;
                    if (party.IsMainParty) return false;

                    if (NationalWillOrders.IsOurs(party))
                    {
                        if (!NationalWillOrders.IsCommandable(party))
                        {
                            MapSelection.Message(MapSelection.NameOf(party) + " 是农民/巡逻队伍, 不能指挥");
                            return false;
                        }
                        MapSelection.Toggle(party);   // 左键: 直接选中(军团长=整个军团)
                        return false;
                    }

                    var selected = MapSelection.Selected;
                    if (selected != null && NationalWillOrders.IsOurs(selected))
                    {
                        // v4.279(第 11 项): 目标是中立/友方 -> 明确说原因(原来这里一点输出都没有)
                        if (!IsHostileParty(party))
                        {
                            MapSelection.Message("「" + MapSelection.NameOf(party) + "」不是敌国部队(中立/友方), 不能攻击");
                            return false;
                        }
                        NationalWillOrders.Engage(party);
                    }
                    else
                    {
                        MapSelection.Message("先左键选中一支我方部队, 再点击目标下达命令");
                    }
                    return false;
                }
                catch (Exception ex) { DLog.Force("PartyClick 异常: " + ex.Message); return true; }
            }
        }

        // 选中领主/部队时的地图点击会被原版 HandleClickTimeChange 顺手改成"暂停/快进"
        // (按 MapDoubleClickBehavior 设置) —— 国家意志模式下不要动时间控制
        [HarmonyPatch(typeof(MapScreen), "HandleClickTimeChange")]
        internal static class NoClickTimeChange
        {
            private static bool Prefix()
            {
                return !NationalWillOrders.IsActive;
            }
        }

        [HarmonyPatch(typeof(MapCameraView), "OnBeforeTick")]
        internal static class BlockOutlinerWheelZoom
        {
            private static void Prefix(ref MapCameraView.InputInformation inputInformation)
            {
                try
                {
                    // v4.263b: 教程浮窗的拖动每帧驱动(挂在地图管线里, 不依赖引导是否开启/是否有弹窗)
                    try { TutorialWindow.Tick(); } catch { }
                    // v4.265: 鼠标在教程浮窗上 -> 滚轮用来翻看正文, 同时清掉相机缩放(滚轮不穿透)
                    if (TutorialWindow.IsMouseOn())
                    {
                        try { TutorialWindow.OnScroll(inputInformation.DeltaMouseScroll); } catch { }
                        inputInformation.DeltaMouseScroll = 0f;
                    }
                    // v4.263f: **引导心跳也挂这里** —— 原来只挂 CampaignEvents.TickEvent, 而选国阶段
                    //   游戏时间是锁定的(NationPickMode.SetTimeLock), 时间锁定后 TickEvent 根本不触发,
                    //   于是选国引导永远不弹、接管前整条链都停摆(日志实证: 启动后只跑过一次就再无输出)。
                    //   地图管线每帧都跑, 与游戏时间无关, 正好补上这段空窗。
                    try { Guide.Tick(0.05f); } catch { }
                    if (Outliner.BlocksMapInput)
                    {
                        inputInformation.MapZoomIn = 0f;
                        inputInformation.MapZoomOut = 0f;
                        inputInformation.DeltaMouseScroll = 0f;
                    }
                    // v4.213: 自建页面打开时, 鼠标不驱动地图相机(WASD 是键盘, 不受影响 -> 开页时镜头照常可动)
                    //   页外: 只清左/中键(维持"开页时地图左键框选/中键旋转暂停"的既有行为), 右键平移与滚轮缩放放行
                    //   页内: 左/右/中键 + 滚轮 + 位移全部清零 -> 页内点击与滚轮不穿透地图
                    if (PanelScreen.AnyOpen)
                    {
                        inputInformation.LeftMouseButtonPressed = false;
                        inputInformation.LeftMouseButtonDown = false;
                        inputInformation.LeftMouseButtonReleased = false;
                        inputInformation.MiddleMouseButtonDown = false;
                        if (PanelScreen.IsMouseOnPanel())
                        {
                            inputInformation.RightMouseButtonDown = false;
                            inputInformation.MouseMoveX = 0f;
                            inputInformation.MouseMoveY = 0f;
                            inputInformation.MapZoomIn = 0f;
                            inputInformation.MapZoomOut = 0f;
                            inputInformation.DeltaMouseScroll = 0f;
                        }
                    }
                }
                catch { }
            }
        }

        // 鼠标操作: 左键拖动=框选; 中键拖动=旋转视角; 右键拖动=平移地图; 右键单击=取消多选/军团菜单
        // 注意: 不能挂在 MapScreen.HandleMouse 上(实测那条路不跑), 改成由 FeudalMapView 每帧调用
        internal static class ArmyRightClickMenu
        {
            private static bool _btnLogged;

            internal static void Tick()
            {
                try
                {
                    // v4.75h: 选国阶段也要鼠标机制(右键平移/中键旋转), 但不做指挥/框选/菜单
                    bool pick = NationPickMode.Active && !NationalWillOrders.IsActive;
                    if (!NationalWillOrders.IsActive && !pick) return;
                    // v4.143: 弹窗输入抑制(含"退出弹窗未松手也不触发", 用户要求)
                    bool mouseHeld = TaleWorlds.InputSystem.Input.IsKeyDown(TaleWorlds.InputSystem.InputKey.LeftMouseButton)
                                  || TaleWorlds.InputSystem.Input.IsKeyDown(TaleWorlds.InputSystem.InputKey.RightMouseButton)
                                  || TaleWorlds.InputSystem.Input.IsKeyDown(TaleWorlds.InputSystem.InputKey.MiddleMouseButton);
                    if (PanelInputGuard.SuppressAfterInquiry(mouseHeld)) return;
                    var screen = MapScreen.Instance;
                    if (screen == null) return;

                    if (Outliner.BlocksMapInput) { HandleOutlinerBlocked(); return; }

                    // 建造模式快捷键: Ctrl+Z 撤销 / ESC 退出
                    if (BatchBuild.Active)
                    {
                        try
                        {
                            bool ctrl = TaleWorlds.InputSystem.Input.IsKeyDown(TaleWorlds.InputSystem.InputKey.LeftControl)
                                     || TaleWorlds.InputSystem.Input.IsKeyDown(TaleWorlds.InputSystem.InputKey.RightControl);
                            if (ctrl && TaleWorlds.InputSystem.Input.IsKeyPressed(TaleWorlds.InputSystem.InputKey.Z)) BatchBuild.Undo();
                            if (TaleWorlds.InputSystem.Input.IsKeyPressed(TaleWorlds.InputSystem.InputKey.Escape)) BatchBuild.Exit();
                        }
                        catch { }
                    }

                    var mouse = TaleWorlds.InputSystem.Input.MousePositionPixel;

                    // 兜底: 不在框选中时确保相机输入为开(否则原版会吞掉左键指挥移动)
                    if (!MapBoxSelect.IsDragging) MapBoxSelect.EnsureCameraInputOn();

                    bool lPressed = TaleWorlds.InputSystem.Input.IsKeyPressed(TaleWorlds.InputSystem.InputKey.LeftMouseButton);
                    bool lDown = TaleWorlds.InputSystem.Input.IsKeyDown(TaleWorlds.InputSystem.InputKey.LeftMouseButton);
                    bool lReleased = TaleWorlds.InputSystem.Input.IsKeyReleased(TaleWorlds.InputSystem.InputKey.LeftMouseButton);
                    bool mPressed = TaleWorlds.InputSystem.Input.IsKeyPressed(TaleWorlds.InputSystem.InputKey.MiddleMouseButton);
                    bool mDown = TaleWorlds.InputSystem.Input.IsKeyDown(TaleWorlds.InputSystem.InputKey.MiddleMouseButton);
                    bool mReleased = TaleWorlds.InputSystem.Input.IsKeyReleased(TaleWorlds.InputSystem.InputKey.MiddleMouseButton);
                    bool rPressed = TaleWorlds.InputSystem.Input.IsKeyPressed(TaleWorlds.InputSystem.InputKey.RightMouseButton);
                    bool rDown = TaleWorlds.InputSystem.Input.IsKeyDown(TaleWorlds.InputSystem.InputKey.RightMouseButton);
                    bool rReleased = TaleWorlds.InputSystem.Input.IsKeyReleased(TaleWorlds.InputSystem.InputKey.RightMouseButton);

                    if (!_btnLogged && (lPressed || mPressed || rPressed))
                    {
                        _btnLogged = true;
                        DLog.Force("鼠标处理: 运行中(左=" + lPressed + " 中=" + mPressed + " 右=" + rPressed + ")");
                    }

                    // v27.x: 选国"开始游戏"后的抑制窗口内吞掉点击(含单击标记), 防确认点击误开驻军页
                    bool clicksSuppressed = PanelInputGuard.ClicksSuppressed;

                    // v4.264: **鼠标在教程浮窗上 -> 三个键的手势一律不启动**(框选/旋转/平移都不做, 也不下命令)。
                    //   这是"左右键穿透"的主因: 原来的拦截只在"下命令"那一层, 而框选与地图平移是在
                    //   MapBoxSelect 里、先于命令链启动的, 于是在窗上按左键会拉出选择框、按右键会拖动地图。
                    //   注意仍要喂一次"释放", 否则在窗上按下、拖出窗外再松手会残留按下状态。
                    if (TutorialWindow.IsMouseOn())
                    {
                        if (lReleased) MapBoxSelect.HandleLeftButton(false, false, true, mouse);
                        if (mReleased) MapBoxSelect.HandleMiddleButton(false, false, true, mouse);
                        if (rReleased) MapBoxSelect.HandleRightButton(false, false, true, mouse);
                        return;
                    }

                    // 左键: 拖动判定/单击标记(两个阶段都要, 选国阶段靠它区分"点击选国"与"拖动"); 框选选中只在指挥阶段生效
                    if (lPressed || lDown || lReleased)
                        MapBoxSelect.HandleLeftButton(lPressed, lDown, lReleased, mouse);

                    // 左键单击地面 = 选中部队移动(自研: 原版点击链被 NoClickCameraMove 等补丁拦截, 不再依赖它)
                    if (!pick && lReleased)
                    {
                        if (clicksSuppressed) MapBoxSelect.ClearClickFlag();
                        else HandleLeftClickGround(screen, mouse);
                    }

                    // 中键: 拖动=旋转视角
                    if (mPressed || mDown || mReleased)
                        MapBoxSelect.HandleMiddleButton(mPressed, mDown, mReleased, mouse);

                    // 右键: 拖动=平移地图; 单击=取消多选/军团菜单
                    if (!rPressed && !rDown && !rReleased) return;

                    bool wasPan = MapBoxSelect.HandleRightButton(rPressed, rDown, rReleased, mouse);
                    if (!rReleased || wasPan) return;
                    if (clicksSuppressed) return;               // v27.x: 开始游戏抑制窗口内不弹右键菜单
                    if (PanelInputGuard.BlockMapClick()) return;   // v4.263: 统一总闸(含面板/导航栏/教程浮窗) —— 原来只查面板, 鼠标在教程窗上右键会穿透
                    if (Outliner.BlocksMapInput) return;
                    if (pick) return;   // v4.75h: 选国阶段右键单击不做任何菜单/命令

                    // 建造模式: 右键单击 = 把方案排队到光标下的领地(设计 10.3)
                    if (BatchBuild.Active)
                    {
                        BatchBuild.QueueAtCursor();
                        return;
                    }

                    // 没选中部队时: 右键他国领土 -> 打开外交面板并选中该国
                    if (MapSelection.Count == 0)
                    {
                        try
                        {
                            var pt = MapBoxSelect.CaptureGroundPoint();
                            var at = TerritoryData.SettlementAt(pt.x, pt.y);
                            var pk = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                            if (at != null && at.OwnerClan != null && at.OwnerClan.Kingdom != null && at.OwnerClan.Kingdom != pk)
                            {
                                DiplomacyPanel.Open(at.OwnerClan.Kingdom);
                                return;
                            }
                        }
                        catch { }
                    }

                    // 右键战场标记: 本国参战的地图战斗 -> 询问是否亲自指挥
                    var battle = BattleCommand.FindNearCursor(90f);
                    if (battle != null)
                    {
                        BattleCommand.ShowConfirm(battle);
                        return;
                    }

                    // 右键点在某个部队上?
                    // v4.246: 不再只依赖 CurrentVisualOfTooltip —— 它会滞后/被名板挡住, 导致"多选后右键"
                    //   经常命中不到部队, 于是菜单弹不出来反而把选中取消了(用户: 组建军团的弹窗怎么没了)。
                    //   现在先用"光标像素距离"找部队(与左键同一口径), 再退回悬停视觉。
                    var party = MapBoxSelect.FindPartyAtCursor(60f);
                    if (party == null)
                    {
                        var visual = screen.CurrentVisualOfTooltip as MobilePartyVisual;
                        party = visual != null && visual.MapEntity != null ? visual.MapEntity.MobileParty : null;
                    }
                    bool onOurParty = party != null && NationalWillOrders.IsOurs(party);

                    // v4.276: 已单选一支舰队 -> 右键**任何地方**都弹舰队菜单(不必把光标压在船模型上)。
                    //   与下面多选菜单同一思路(用户: 右键菜单要能稳定弹出来)。
                    if (MapSelection.Count == 1)
                    {
                        var selFleet = MapSelection.Selected;
                        if (selFleet != null && NavalTaskForce.IsTaskForce(selFleet))
                        {
                            FleetMenu.Show(selFleet);
                            return;
                        }
                    }

                    // v4.246: 多选(且选中的都是我方的)时, 右键一律弹小队菜单 ——
                    //   用户的意图本来就是"对选中的这几支下命令", 不该要求光标必须压在部队模型上
                    bool multiOurs = false;
                    try
                    {
                        if (MapSelection.Count > 1)
                        {
                            multiOurs = true;
                            var sl = MapSelection.SelectedList;
                            for (int i = 0; i < sl.Count; i++)
                            {
                                var q = sl[i];
                                if (q == null || !NationalWillOrders.IsOurs(q)) { multiOurs = false; break; }
                            }
                        }
                    }
                    catch { }

                    if (onOurParty || multiOurs)
                    {
                        var army = party != null ? party.Army : null;
                        bool armyMenu = army != null && army.Parties != null && army.Parties.Count > 1;
                        DLog.Force("右键: 选中=" + MapSelection.Count + " 命中部队=" + (party != null)
                            + " -> " + (armyMenu ? "军团成员菜单" : (MapSelection.Count > 1 ? "小队菜单(含建立军团)" : "国防军菜单/取消选中")));
                        // 在军团里 -> 军团成员菜单(可拆领主)
                        if (armyMenu)
                        {
                            ArmyPicker.Show(army);
                            return;
                        }
                        // 多选状态 -> 小队菜单(建立军团/合并/拆分)
                        if (MapSelection.Count > 1)
                        {
                            MultiSelectMenu.Show();
                            return;
                        }
                        // 单选国防军 -> 国防军菜单(拆分等)
                        if (MapSelection.Count == 1 && DefArmyMenu.CountSelectedDefLegions() > 0)
                        {
                            DefArmyMenu.Show();
                            return;
                        }
                        // 单选(或没选) -> 右键取消选中
                        if (MapSelection.Count >= 1)
                        {
                            MapSelection.Clear();
                            MapSelection.Message("已取消选中");
                        }
                        return;
                    }

                    // 右键空白/敌人: 取消选中(单选也能取消)
                    if (MapSelection.Count >= 1)
                    {
                        MapSelection.Clear();
                        MapSelection.Message("已取消选中");
                    }
                }
                catch (Exception ex) { DLog.Force("鼠标操作异常: " + ex.Message); }
            }

            private static void HandleOutlinerBlocked()
            {
                try
                {
                    var m = TaleWorlds.InputSystem.Input.MousePositionPixel;
                    if (TaleWorlds.InputSystem.Input.IsKeyReleased(TaleWorlds.InputSystem.InputKey.LeftMouseButton))
                        MapBoxSelect.HandleLeftButton(false, false, true, m);
                    if (TaleWorlds.InputSystem.Input.IsKeyReleased(TaleWorlds.InputSystem.InputKey.RightMouseButton))
                        MapBoxSelect.HandleRightButton(false, false, true, m);
                    if (TaleWorlds.InputSystem.Input.IsKeyReleased(TaleWorlds.InputSystem.InputKey.MiddleMouseButton))
                        MapBoxSelect.HandleMiddleButton(false, false, true, m);
                    if (!MapBoxSelect.IsDragging) MapBoxSelect.EnsureCameraInputOn();
                }
                catch { }
            }

            // 左键单击全处理(自研; 原版点击链被 NoClickCameraMove 等补丁拦截, 不再依赖它):
            //   点我方部队=选中 / 点敌方=攻击 / 点城市=前往围城或打开抽屉 / 点圈=取消 / 点地面=移动
            private static void HandleLeftClickGround(MapScreen screen, Vec2 mouse)
            {
                try
                {
                    if (PanelInputGuard.ClicksSuppressed) return;   // v27.x: 开始游戏确认后的抑制窗口(防误开驻军页)
                    // v4.258: 走统一总闸 —— 原来这条链只查 IsMouseOnPanel/Outliner/IsMouseOnButtons 三个条件,
                    //   漏任何一个就会把"点面板"当成"点地图"(给选中的部队误下命令)。
                    if (PanelInputGuard.BlockMapClick()) return;
                    if (Outliner.BlocksMapInput) return;
                    if (!NationalWillOrders.IsActive) return;
                    if (!MapBoxSelect.LastReleaseWasClick) return;
                    MapBoxSelect.ClearClickFlag();
                    if (PanelScreen.IsMouseOnPanel()) return;
                    if (PartyInfoHud.IsMouseOnButtons()) return;   // 28.6: 小窗选项按钮
                    if (NationalPanel.IsMouseOnFlag()) return;
                    if (mouse.X < PanelScreen.PanelX) return;    // 左侧导航栏区域
                    if (mouse.Y < 66f) return;                   // 顶部地图栏区域

                    var vis = screen.CurrentVisualOfTooltip;

                    // v4.165: 铁路页"地图点选起终点"模式: 点地面 = 反查领地城镇/城堡, 吞掉本次点击(必须最先判定)
                    if (RailwaysUi.IsPickingEndpoint)
                    {
                        try
                        {
                            Settlement railS = null;
                            var railSv = vis as SettlementVisual;
                            if (railSv != null && railSv.MapEntity != null) railS = railSv.MapEntity.Settlement;
                            if (railS == null)
                            {
                                var railPt = MapBoxSelect.CaptureGroundPoint();
                                if (railPt.z > -5000f) railS = TerritoryData.SettlementAt(railPt.x, railPt.y);
                            }
                            if (railS != null) RailwaysUi.PickEndpoint(railS);
                            else MapSelection.Message("这里不是城镇/城堡");
                        }
                        catch { }
                        return;
                    }

                    // v4.98: 军务页"地图点选城市"模式: 左键点地图 = 选中光标下的聚落
                    // (原版 ProcessTravel 点击链已被拦, 这里自研; 点图标/点附近地面都认)
                    // v4.282: 判据改成公共注册表(SettlementPickMode) —— 海军页复用同一条链;
                    //   右键在这里**只吞掉不处理**(取消由 SettlementPickMode.Poll 每帧自检, 见那边的 ①②)。
                    if (SettlementPickMode.Active)
                    {
                        try
                        {
                            if (TaleWorlds.InputSystem.Input.IsKeyPressed(TaleWorlds.InputSystem.InputKey.RightMouseButton))
                            {
                                SettlementPickMode.Cancel();
                                MapSelection.Message(SettlementPickMode.CancelHint);
                                return;
                            }
                            var sv0 = vis as SettlementVisual;
                            var st0 = sv0 != null && sv0.MapEntity != null ? sv0.MapEntity.Settlement : null;
                            if (st0 != null) { SettlementPickMode.Deliver(st0); return; }
                            var pt0 = MapBoxSelect.CaptureGroundPoint();
                            if (pt0.z > -5000f)
                            {
                                Settlement best = null;
                                float bd = 120f;
                                foreach (var x in Settlement.All)
                                {
                                    if (x == null) continue;
                                    float dx = x.Position.X - pt0.x, dy = x.Position.Y - pt0.y;
                                    float d = (float)Math.Sqrt(dx * dx + dy * dy);
                                    if (d < bd) { bd = d; best = x; }
                                }
                                if (best != null) { SettlementPickMode.Deliver(best); return; }
                            }
                            MapSelection.Message("地图点选: 未命中聚落, 请点击城市图标");
                        }
                        catch { }
                        return;
                    }

                    // v4.165: 铁路地图模式下点城镇/城堡(图标或近处) = 直接打开铁路运输(装车/运输)
                    if (MapDataMode.Current == MapData.Rail)
                    {
                        try
                        {
                            Settlement railS2 = null;
                            var railSv2 = vis as SettlementVisual;
                            if (railSv2 != null && railSv2.MapEntity != null) railS2 = railSv2.MapEntity.Settlement;
                            if (railS2 == null)
                            {
                                var railPt2 = MapBoxSelect.CaptureGroundPoint();
                                if (railPt2.z > -5000f)
                                {
                                    var railCand = TerritoryData.SettlementAt(railPt2.x, railPt2.y);
                                    if (railCand != null)
                                    {
                                        float rdx = railCand.Position.X - railPt2.x;
                                        float rdy = railCand.Position.Y - railPt2.y;
                                        if (rdx * rdx + rdy * rdy < 60f * 60f) railS2 = railCand;
                                    }
                                }
                            }
                            if (railS2 != null && RailwaysUi.RailModeClick(railS2)) return;
                        }
                        catch { }
                    }

                    // 1) 点部队(优先用光标距离检测, 不依赖原版悬停拾取)
                    var party = MapBoxSelect.FindPartyAtCursor(60f);
                    if (party == null)
                    {
                        var pv0 = vis as MobilePartyVisual;
                        if (pv0 != null && pv0.MapEntity != null) party = pv0.MapEntity.MobileParty;
                    }
                    if (party != null)
                    {
                        try { PartyInfoHud.Show(party); } catch { }
                        if (party.IsMainParty) return;
                        if (NationalWillOrders.IsOurs(party))
                        {
                            if (!NationalWillOrders.IsCommandable(party))
                            {
                                MapSelection.Message(MapSelection.NameOf(party) + " 是农民/巡逻队伍, 不能指挥");
                                return;
                            }
                            MapSelection.Toggle(party);   // 左键: 单击单选/取消(军团长=整个军团)
                            return;
                        }
                        var sel = MapSelection.Selected;
                        if (sel != null && NationalWillOrders.IsOurs(sel))
                        {
                            // v4.279(第 11 项): 与 PartyClick 同一口径 —— 中立/友方目标给中文提示, 不静默
                            if (!IsHostileParty(party))
                                MapSelection.Message("「" + MapSelection.NameOf(party) + "」不是敌国部队(中立/友方), 不能攻击");
                            else NationalWillOrders.Engage(party);
                        }
                        else MapSelection.Message("先左键选中一支我方部队, 再点击目标下达命令");
                        return;
                    }

                    // 2) 点定居点(必须"光标确实落在城镇图标/名板上"才算, 否则一律走点地面移动)
                    //    v4.246: 原来"悬停视觉命中"就直接前往, 但 CurrentVisualOfTooltip 会滞后(鼠标快速移开
                    //    仍指向上一个聚落), 加上 12 米坐标兜底太宽 —— 于是点空地经常被判成"前往该城镇"。
                    //    v4.248: 日志证明"点空地"时射线会打中城镇 3D 模型(返回的模型表面点离中心只有 5 米),
                    //    所以改为三级判定: ①聚落名板屏幕距离 ≤90px(最准) ②悬停视觉+世界坐标 ≤3 米 ③坐标兜底 ≤3 米。
                    var pt2 = MapBoxSelect.CaptureGroundPoint();
                    bool pt2ok = pt2.z > -5000f;
                    // v4.249: 名板判定 90 -> 45 像素(用户: 部队莫名其妙进城了 —— 90px 太宽, 点城镇旁边的空地
                    //   也会落进判定圈, 于是被判成"前往该城镇"并进城; 45px ≈ 名板本身的范围)
                    var st2 = MapBoxSelect.FindSettlementAtCursor(45f);
                    string st2How = st2 != null ? "名板" : "";
                    if (st2 == null)
                    {
                        var sv = vis as SettlementVisual;
                        var cand = sv != null && sv.MapEntity != null ? sv.MapEntity.Settlement : null;
                        if (cand != null && pt2ok)
                        {
                            float hx = cand.Position.X - pt2.x, hy = cand.Position.Y - pt2.y;
                            if (hx * hx + hy * hy <= 3f * 3f) { st2 = cand; st2How = "悬停+3米"; }
                        }
                    }
                    if (st2 == null && pt2ok)
                    {
                        float bd = 3f * 3f;
                        foreach (var x in Settlement.All)
                        {
                            if (x == null) continue;
                            float dx = x.Position.X - pt2.x, dy = x.Position.Y - pt2.y;
                            float d = dx * dx + dy * dy;
                            if (d < bd) { bd = d; st2 = x; }
                        }
                        if (st2 != null) st2How = "坐标3米";
                    }
                    // ==================== v4.279(第 2 项): 这里只需要知道"选择集里有没有舰队" ====================
                    //   具体的分流(陆军 -> NationalWillOrders, 舰队 -> NavalTaskForce 带封锁语义)交给
                    //   OrderSelectionToSettlement 统一做 —— 它自己会再快照一次, 而且那个方法是"点聚落图标"
                    //   那条路共用的, 保证两条入口语义一致。
                    bool anyFleet = false;
                    try
                    {
                        var sl0 = MapSelection.SelectedList;
                        for (int q = 0; q < sl0.Count; q++)
                        {
                            var t0 = sl0[q];
                            if (t0 == null || !t0.IsActive) continue;
                            bool isF0 = false;
                            try { isF0 = NavalTaskForce.IsTaskForce(t0); } catch { }
                            if (isF0) { anyFleet = true; break; }
                        }
                    }
                    catch { }

                    // v4.254: 若选中的部队**已经停在这个聚落**(军团常态就是停在城门外), 那么点它旁边的地面
                    //   只该是"移动过去", 不该判成"前往该城" —— 45px 名板判定 + 部队常年堆在城门口,
                    //   玩家点地面经常被判成前往该城(日志实证: 7 秒内连点 6 次"命令 2 支部队前往 厄毗诺萨堡")
                    //   v4.279(第 11 项): 这条抑制**只对纯陆军选择集成立**。原来它对舰队也生效 ⇒
                    //     舰队停在自己母港海面时点该港, st2 被置空 -> 静默落到"移动到海面点",
                    //     玩家点了没反应也不说原因。舰队点港口 = "停泊/封锁这个港", 意图明确且命令幂等,
                    //     所以只要有舰队参与就跳过这条抑制, 一律走下面的港口分支。
                    if (st2 != null && !anyFleet)
                    {
                        try
                        {
                            var sel0 = MapSelection.Selected;
                            if (sel0 != null)
                            {
                                if (sel0.CurrentSettlement == st2) st2 = null;
                                else if (sel0.Position.ToVec2().Distance(st2.Position.ToVec2()) < 30f) st2 = null;
                            }
                        }
                        catch { }
                    }
                    if (st2 != null)
                    {
                        // v4.279(第 2 项): 分流逻辑抽到 OrderSelectionToSettlement(两条点聚落的路共用),
                        //   这里只负责"点地面时先过一遍 v4.254 的抑制判定"(见上), 然后交给它。
                        OrderSelectionToSettlement(st2, "左键地面/" + st2How);
                        return;
                    }

                    // 3) 点在选中圈上 = 取消该部队
                    var ringHit = MapBoxSelect.HitSelectedRing();
                    if (ringHit != null) { MapSelection.Toggle(ringHit); return; }

                    // 4) 地面 -> 移动选中部队
                    if (MapSelection.Count == 0) return;
                    var pt = MapBoxSelect.CaptureGroundPoint();
                    if (pt.z < -5000f) return;                   // 无效地面点
                    NationalWillOrders.MoveToPoint(new CampaignVec2(new Vec2(pt.x, pt.y), true));
                }
                catch (Exception ex) { DLog.Force("左键点击处理异常: " + ex.Message); }
            }
        }

        // ==================== v4.279(第 11 项): 攻击前的"敌我预检" ====================
        //   【原缺陷】点中立/友方部队时:
        //     · 陆军分支 -> `NationalWillOrders.Engage` -> `SetMoveEngageParty(友军)`, 原版静默拒绝;
        //     · 舰队分支 -> 同一个 Engage 里调 `NavalTaskForce.OrderEngage(p, target)`, 它**会**返回
        //       "该目标不是敌国部队", 但这个返回值在 NationalWillOrders.Engage 里被**直接丢弃**
        //       (那一行只有 `NavalTaskForce.OrderEngage(p, target); fleets++;`)。
        //   ⇒ 玩家点了敌人以外的部队**完全没有任何输出**(既没命令也没提示)。
        //   【修法】在地图点击入口先做一次预检, 把原因直接说给玩家。NationalWillOrders.cs 不在本次
        //     改动范围内, 所以这里不动它 —— 预检放在它**之前**, 两条链都不会再收到注定失败的交战令。
        private static bool IsHostileParty(MobileParty p)
        {
            try
            {
                var k = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                var f = p != null ? p.MapFaction : null;
                if (k == null || f == null) return false;
                if (ReferenceEquals(f, k)) return false;
                return FactionManager.IsAtWarAgainstFaction(k, f);
            }
            catch { return false; }
        }

        // ==================== v4.279(第 2 项): 点定居点 = 对**全体选中**分流下令(两个入口共用) ====================
        //   为什么抽成一个方法: "点定居点"在本文件里有**两条独立的路**都能触发 ——
        //     ① 自研左键链 `HandleLeftClickGround`(光标距离 + 名板判定, 点地面也算);
        //     ② 原版 `SettlementVisual.OnMapClick` 的补丁 `SettlementClick`(点城镇图标/名板)。
        //   原来这两条路各自只处理"主选那一支", 而且行为还不一样(① 舰队 return 掉了陆军,
        //   ② 让舰队走 GoToSettlement 的 blockade:false 分支 = 不封锁)。现在两条路共用这一份,
        //   语义严格一致。
        //
        //   【分流规则】陆军的交给 NationalWillOrders(围城/进城/海运拦截那套一行不动);
        //   舰队的交给 NavalTaskForce 并**带上封锁语义**(敌港 = 开过去封锁, 其余 = 停泊)。
        //   ★ 顺序**必须**先陆军后舰队: NationalWillOrders.GoToSettlement 内部会遍历同一个选中集,
        //     它的舰队分支恒传 `blockade: false`, 会把我们先下的封锁令覆盖掉。
        //   ★ 返回值不丢: 每个舰队的命令结果(含"不能封锁自己的港口""未与该国交战"这类拒绝)
        //     都拼进消息 —— 原来这些字符串被直接丢弃, 玩家点了没反应也不知道为什么。
        internal static void OrderSelectionToSettlement(Settlement st, string how)
        {
            try
            {
                if (st == null) return;
                // 快照: 下面的命令会改选区(Toggle/Clear/Select), 不能直接遍历活列表
                var snap = new System.Collections.Generic.List<MobileParty>();
                try
                {
                    var sl = MapSelection.SelectedList;
                    for (int q = 0; q < sl.Count; q++) if (sl[q] != null) snap.Add(sl[q]);
                }
                catch { }
                int fleetN = 0, landN = 0;
                for (int q = 0; q < snap.Count; q++)
                {
                    var t0 = snap[q];
                    if (t0 == null || !t0.IsActive) continue;
                    bool isF0 = false;
                    try { isF0 = NavalTaskForce.IsTaskForce(t0); } catch { }
                    if (isF0) { fleetN++; continue; }
                    try { if (NationalWillOrders.IsOurs(t0)) landN++; } catch { }
                }
                bool hostilePort = NationalWillOrders.IsHostileSettlement(st);
                DLog.Force("点聚落: " + (st.Name != null ? st.Name.ToString() : st.StringId)
                    + "(来源=" + how + ") 选中 " + snap.Count + " 支(陆军 " + landN + " / 舰队 " + fleetN
                    + ") 敌对=" + hostilePort);

                // ① 陆军
                if (landN > 0) NationalWillOrders.GoToSettlement(st);

                // ② 舰队(带封锁语义)
                if (fleetN > 0)
                {
                    var fm = new System.Text.StringBuilder();
                    for (int q = 0; q < snap.Count; q++)
                    {
                        var t = snap[q];
                        if (t == null || !t.IsActive) continue;
                        bool isF = false;
                        try { isF = NavalTaskForce.IsTaskForce(t); } catch { }
                        if (!isF) continue;
                        string one = NavalTaskForce.OrderToPort(t, st, hostilePort);
                        if (hostilePort)
                        {
                            try { one += " · " + NavalFleet.SetBlockade(st, NavalTaskForce.PlayerKingdom()); }
                            catch { }
                        }
                        if (fm.Length > 0) fm.Append(" | ");
                        fm.Append(one);
                    }
                    if (fm.Length > 0) MapSelection.Message(fm.ToString());
                }

                // 选中的都不是我方部队(或没选中) -> 原行为: 弹驻军列表
                if (landN == 0 && fleetN == 0) GarrisonPanel.Open(st);
            }
            catch (Exception ex) { DLog.Force("点聚落分流异常: " + ex.Message); }
        }

        // ==================== v4.276: 舰队右键菜单 ====================
        //   与国防军菜单(DefArmyMenu)并列: 单选一支舰队时右键弹出。
        //   菜单里每一项都只调 NavalTaskForce 的海军入口 —— 舰队绝不走陆军那几条链
        //   (陆军入口会调 DefArmy.EnsureNavigation, 那会把海上部队直接拖回陆地, 见 NationalWillOrders.IsFleet 注释)。
        internal static class FleetMenu
        {
            internal static void Show(MobileParty fleet)
            {
                try
                {
                    if (fleet == null) return;
                    var options = new System.Collections.Generic.List<InquiryElement>
                    {
                        new InquiryElement("recall", "回母港", null, true, "返回母港海面驻泊"),
                        new InquiryElement("sethome", "指定母港…", null, true, "从自有港口里挑一处作为母港(母港未知/被转让后用它重新指定)"),
                        new InquiryElement("patrol", "巡航母港海域", null, true, "在母港附近海域来回巡逻(原版 PatrolAroundPoint 海上分支)"),
                        new InquiryElement("blockade", "封锁敌港…", null, true, "从敌国港口里挑一处, 舰队开过去实施封锁(500 第纳尔/次)"),
                        new InquiryElement("disband", "解散舰队", null, true, "舰船按原主归还氏族, 舰队司令退役")
                    };
                    string body = MapSelection.NameOf(fleet) + "\n" + NavalTaskForce.Summary(fleet);
                    PanelInputGuard.ShowPopup(new MultiSelectionInquiryData(
                        "舰队指挥", body, options, true, 1, 1, "执行", "取消",
                        delegate (System.Collections.Generic.List<InquiryElement> sel) { OnPicked(fleet, sel); },
                        null, null, false));
                }
                catch (Exception ex) { DLog.Force("舰队菜单异常: " + ex.Message); }
            }

            private static void OnPicked(MobileParty fleet, System.Collections.Generic.List<InquiryElement> sel)
            {
                try
                {
                    if (fleet == null || sel == null || sel.Count == 0) return;
                    string id = sel[0].Identifier as string;
                    if (id == "recall") { MapSelection.Message(NavalTaskForce.OrderRecall(fleet)); return; }
                    if (id == "sethome") { ShowHomePortPicker(fleet); return; }
                    if (id == "patrol")
                    {
                        var f = NavalTaskForce.ForceOf(fleet);
                        var home = NavalTaskForce.FindSettlement(f != null ? f.HomePortId : null);
                        // ==================== v4.279(第 11 项): 母港未知时给**可操作**的提示 ====================
                        //   【原缺陷】母港未知/失效时 home == null, OrderPatrol 返回的是"目标不是港口" ——
                        //   这句话既没说清原因(母港没了), 也没告诉玩家怎么办; 而且一旦返回值是空串,
                        //   `Message("")` 就等于**点了完全没反应**。
                        //   【修法】先判母港, 直接给"请先用[指定母港…]指定一处自有港口"这句可操作的提示;
                        //   正常路径下再对返回值做兜底 —— 任何时候都不 Message("")。
                        if (home == null || !home.HasPort)
                        {
                            MapSelection.Message("「" + MapSelection.NameOf(fleet) + "」母港未知(原母港已失效或被转让) —— "
                                + "请先用右键菜单[指定母港…]选一处自有港口, 再回来点[巡航母港海域]");
                            return;
                        }
                        string patMsg = NavalTaskForce.OrderPatrol(fleet, home);
                        MapSelection.Message(string.IsNullOrEmpty(patMsg)
                            ? "巡航命令未生效(舰队状态异常) —— 请刷新后重试, 详情见日志"
                            : patMsg);
                        return;
                    }
                    if (id == "blockade") { ShowBlockadePicker(fleet); return; }
                    if (id == "disband")
                    {
                        // 解散是不可逆操作 -> 二次确认(与国防军解散同一套口径)
                        PanelInputGuard.ShowPopup(new MultiSelectionInquiryData(
                            "解散舰队", "确定解散这支舰队吗?\n舰船会按原主归还, 司令退役, 编制注销。",
                            new System.Collections.Generic.List<InquiryElement>
                            {
                                new InquiryElement("yes", "确定解散", null, true, null),
                                new InquiryElement("no", "取消", null, true, null)
                            }, true, 1, 1, "确定", "取消",
                            delegate (System.Collections.Generic.List<InquiryElement> s2)
                            {
                                try
                                {
                                    if (s2 != null && s2.Count > 0 && (s2[0].Identifier as string) == "yes")
                                        MapSelection.Message(NavalTaskForce.Disband(fleet));
                                }
                                catch (Exception ex) { DLog.Force("舰队解散异常: " + ex.Message); }
                            }, null, null, false));
                        return;
                    }
                }
                catch (Exception ex) { DLog.Force("舰队菜单选择异常: " + ex.Message); }
            }

            // v4.277(第 3 项): 指定母港 —— 收养时推断失败(HomeSettlement 非港口/我方无港口)后,
            //   玩家能在这里一次性补救, 不必解散重建舰队。候选 = 我方自有港口(与组建舰队同一数据源)。
            private static void ShowHomePortPicker(MobileParty fleet)
            {
                try
                {
                    var k = NavalTaskForce.PlayerKingdom();
                    if (k == null) { MapSelection.Message("尚未建立国家意志"); return; }
                    var ports = NavalBridge.PortsOf(k);
                    var options = new System.Collections.Generic.List<InquiryElement>();
                    for (int i = 0; i < ports.Count; i++)
                    {
                        var p = ports[i];
                        if (p == null || !p.HasPort) continue;
                        options.Add(new InquiryElement(p, NavalBridge.NameOf(p), null, true, "设为这支舰队的母港"));
                    }
                    if (options.Count == 0) { MapSelection.Message("我方没有可用港口, 无法指定母港"); return; }
                    PanelInputGuard.ShowPopup(new MultiSelectionInquiryData(
                        "指定母港", "选一处自有港口作为这支舰队的母港", options, true, 1, 1, "指定", "取消",
                        delegate (System.Collections.Generic.List<InquiryElement> s4)
                        {
                            try
                            {
                                if (s4 == null || s4.Count == 0) return;
                                var port = s4[0].Identifier as Settlement;
                                if (port == null) return;
                                MapSelection.Message(NavalTaskForce.SetHomePort(fleet, port));
                            }
                            catch (Exception ex) { DLog.Force("舰队指定母港异常: " + ex.Message); }
                        }, null, null, false));
                }
                catch (Exception ex) { DLog.Force("舰队母港列表异常: " + ex.Message); }
            }

            // 敌港选择(与海军页封锁页同一数据源: NavalBridge.EnemyPorts)
            private static void ShowBlockadePicker(MobileParty fleet)
            {
                try
                {
                    var k = NavalTaskForce.PlayerKingdom();
                    if (k == null) { MapSelection.Message("尚未建立国家意志"); return; }
                    var ports = NavalBridge.EnemyPorts(k);
                    var options = new System.Collections.Generic.List<InquiryElement>();
                    for (int i = 0; i < ports.Count; i++)
                    {
                        var p = ports[i];
                        if (p == null || !p.HasPort) continue;
                        options.Add(new InquiryElement(p, NavalBridge.NameOf(p), null, true,
                            "该国: " + (NavalBridge.KingdomOf(p) != null && NavalBridge.KingdomOf(p).Name != null
                                ? NavalBridge.KingdomOf(p).Name.ToString() : "?")));
                    }
                    if (options.Count == 0) { MapSelection.Message("当前没有可封锁的敌国港口(需与有港口的国家交战)"); return; }
                    PanelInputGuard.ShowPopup(new MultiSelectionInquiryData(
                        "封锁敌港", "选一处敌国港口实施封锁", options, true, 1, 1, "封锁", "取消",
                        delegate (System.Collections.Generic.List<InquiryElement> s3)
                        {
                            try
                            {
                                if (s3 == null || s3.Count == 0) return;
                                var port = s3[0].Identifier as Settlement;
                                if (port == null) return;
                                string msg = NavalTaskForce.OrderToPort(fleet, port, true);
                                string bm = NavalFleet.SetBlockade(port, NavalTaskForce.PlayerKingdom());
                                MapSelection.Message(msg + " · " + bm);
                            }
                            catch (Exception ex) { DLog.Force("舰队封锁异常: " + ex.Message); }
                        }, null, null, false));
                }
                catch (Exception ex) { DLog.Force("舰队封锁列表异常: " + ex.Message); }
            }
        }

        // 多选状态下右键部队的小队菜单(目前只有"建立军团")
        internal static class MultiSelectMenu
        {
            internal static void Show()
            {
                try
                {
                    // ==================== v4.279(第 1 项): 多选里混了舰队要说明 ====================
                    //   舰队也是"我方部队"(`IsOurs` 为真 —— 司令挂在玩家氏族), 所以多选 舰队+陆军 时
                    //   菜单照常弹出。但下面两项对舰队**没有意义**(舰队不能建军团/不能与陆军合并),
                    //   于是这里先数一遍, 在标题里写清楚 —— 否则玩家会奇怪"我选了 3 支, 为什么菜单里
                    //   只有建立军团/合并部队, 而且合并还点不动"。
                    int mFleet = 0, mLand = 0;
                    try
                    {
                        var msl = MapSelection.SelectedList;
                        for (int i = 0; i < msl.Count; i++)
                        {
                            var q = msl[i];
                            if (q == null || !q.IsActive) continue;
                            bool f = false;
                            try { f = NavalTaskForce.IsTaskForce(q); } catch { }
                            if (f) mFleet++; else mLand++;
                        }
                    }
                    catch { }
                    var options = new System.Collections.Generic.List<InquiryElement>
                    {
                        new InquiryElement("form_army", "建立军团", null, true, "从选中的部队里挑一位领主建立军团")
                    };
                    if (ArmyManagePanel.CountMergeableSelected() >= 2)
                        options.Add(new InquiryElement("merge_army", "合并部队", null, true, "把选中的本国非军团部队逐人+装备并入兵力最多的一支(受编制上限约束)"));
                    if (DefArmyMenu.CountSelectedDefLegions() >= 2)
                        options.Add(new InquiryElement("merge_def", "合并国防军", null, true, "把选中的国防军军团合并为一支(多余将军卸任转为 1 名小兵, 免费)"));
                    if (DefArmyMenu.CountSelectedDefLegions() >= 1)
                        options.Add(new InquiryElement("split_def", "拆分国防军", null, true, "把兵力最多的一支一分为二(1 名小兵升任将军, 花费 500)"));
                    PanelInputGuard.ShowPopup(new MultiSelectionInquiryData(
                        "部队指挥", "已选中 " + MapSelection.Count + " 支部队"
                            + (mFleet > 0
                                ? ("(其中舰队 " + mFleet + " 支 —— 舰队**不参与**建立军团/合并部队, "
                                   + "请单选舰队后用右键菜单[舰队指挥]下命令)")
                                : ""),
                        options, true, 1, 1, "确定", "取消",
                        OnPicked, null, null, false));
                }
                catch (Exception ex) { DLog.Force("小队菜单异常: " + ex.Message); }
            }

            private static void OnPicked(System.Collections.Generic.List<InquiryElement> selected)
            {
                try
                {
                    if (selected == null || selected.Count == 0) return;
                    string id = selected[0].Identifier as string;
                    if (id == "merge_army") { ArmyManagePanel.MergeSelected(); return; }
                    if (id == "merge_def") { DefArmyMenu.DoMerge(); return; }
                    if (id == "split_def") { DefArmyMenu.DoSplit(); return; }
                    if (id != "form_army") return;
                    ShowLeaderPicker();
                }
                catch (Exception ex) { DLog.Force("小队菜单选择异常: " + ex.Message); }
            }

            // 原版询问框: 选择谁建立军团(列出本国所有带队的领主)
            private static void ShowLeaderPicker()
            {
                try
                {
                    var behavior = NationalWillOrders.Behavior;
                    var kingdom = behavior != null ? behavior.NationKingdom : null;
                    if (kingdom == null) return;

                    var options = new System.Collections.Generic.List<InquiryElement>();
                    // 人选只能是"当前选中的部队"里的家族领袖
                    // ==================== v4.279(第 1 项): 舰队司令**不能**当军团长 ====================
                    //   【原缺陷】舰队的司令是 DefArmy.MakeCaptainFor 生成、挂在玩家氏族的 Lord 英雄
                    //   (`ActualClan = Clan.PlayerClan`), 所以 `hero.Clan.Leader == hero` 对他成立 ——
                    //   于是**舰队会被列为合法军团长**, 玩家选中舰队点"建立军团"就能把它编进陆军军团。
                    //   后果不是"编不进去", 而是**整支舰队连同全部舰船被永久销毁**(见 OnLeaderPicked 的实证注释)。
                    //   【修法】这里直接不列出舰队, 并记下跳过了几支, 好在下面的说明里写清楚原因(不静默)。
                    var leaderSnap = MapSelection.SelectedList;
                    int skippedFleets = 0;
                    for (int li = 0; li < leaderSnap.Count; li++)
                    {
                        var p = leaderSnap[li];
                        if (p == null || !p.IsActive) continue;
                        bool isFleet0 = false;
                        try { isFleet0 = NavalTaskForce.IsTaskForce(p); } catch { }
                        if (isFleet0) { skippedFleets++; continue; }
                        var hero = p.LeaderHero;
                        if (hero == null || hero.IsDead) continue;
                        if (hero.Clan == null || !ReferenceEquals(hero.Clan.Leader, hero)) continue;
                        options.Add(new InquiryElement(hero, hero.Name.ToString(), null, true, null));
                    }
                    if (options.Count == 0)
                    {
                        // 没人能建立军团 -> 告诉玩家条件
                        PanelInputGuard.ShowPopup(new MultiSelectionInquiryData(
                            "无法建立军团",
                            "建立军团的条件:\n" +
                            "· 军团长必须是【选中的部队】里的【家族领袖】\n" +
                            "· 该领主的部队必须在野外、未被俘、未解散\n" +
                            "· 农民队伍和巡逻队不能作为军团长\n" +
                            "· 选中的部队里至少要有 1 位符合条件的家族领袖"
                            // v4.279(第 1 项): 被跳过的舰队要说出来, 否则"我明明选了一支领主部队"的玩家
                            //   会以为是 bug(舰队司令显示是领主, 却不给选)。
                            + (skippedFleets > 0
                                ? ("\n\n★ 已跳过 " + skippedFleets + " 支舰队: 舰队是海上部队, 不能当军团长, "
                                   + "也不能编入陆军军团(编入会让原版销毁整支舰队与全部舰船)。")
                                : ""),
                            new System.Collections.Generic.List<InquiryElement>
                            {
                                new InquiryElement("ok", "知道了", null, true, null)
                            }, true, 1, 1, "确定", "取消", null, null, null, false));
                        return;
                    }

                    PanelInputGuard.ShowPopup(new MultiSelectionInquiryData(
                        "建立军团", "选择由哪位领主建立军团(当前选中的部队会一起加入)",
                        options, true, 1, 1, "确定", "取消",
                        OnLeaderPicked, null, null, false));
                }
                catch (Exception ex) { DLog.Force("建立军团列表异常: " + ex.Message); }
            }

            private static void OnLeaderPicked(System.Collections.Generic.List<InquiryElement> selected)
            {
                try
                {
                    if (selected == null || selected.Count == 0) return;
                    var leader = selected[0].Identifier as Hero;
                    if (leader == null || leader.PartyBelongedTo == null) return;

                    var behavior = NationalWillOrders.Behavior;
                    var kingdom = behavior != null ? behavior.NationKingdom : null;
                    if (kingdom == null) return;

                    // 把当前选中的部队一起拉进军团(不含军团长自己)
                    // ==================== v4.279(第 1 项 ★★ 最关键的一处) ====================
                    //   【为什么必须在这里挡】`kingdom.CreateArmy(leader, target, type, toCall)` 会把这些部队
                    //   编入军团; 而军团成员一旦被解散/合并, 原版链是
                    //     ArmyMoves.Merge -> DestroyPartyAction.ApplyForDisbanding(src, home)
                    //       -> MobileParty.RemoveParty()
                    //       -> `for (i...) DestroyShipAction.Apply(Ships[i])`   (反编译实证)
                    //   ⇒ **整支舰队连同名下全部舰船被永久销毁**, 不可逆, 而且原版不给二次确认。
                    //   ShowLeaderPicker 已经不把舰队列为军团长, 但"选中的部队"是**弹窗打开时的选区**,
                    //   两次弹窗之间玩家还能改选区(框选/Toggle), 所以这里必须再挡一次(双保险)。
                    var toCall = new System.Collections.Generic.List<MobileParty>();
                    int skippedFleetCall = 0;
                    var callSnap = MapSelection.SelectedList;
                    for (int ci = 0; ci < callSnap.Count; ci++)
                    {
                        var p = callSnap[ci];
                        if (p == null || !p.IsActive) continue;
                        if (ReferenceEquals(p, leader.PartyBelongedTo)) continue;
                        bool isFleetC = false;
                        try { isFleetC = NavalTaskForce.IsTaskForce(p); } catch { }
                        if (isFleetC) { skippedFleetCall++; continue; }
                        toCall.Add(p);
                    }

                    var target = kingdom.InitialHomeSettlement;
                    NoAiControlPatches.PlayerFormingArmy = true;
                    try
                    {
                        kingdom.CreateArmy(leader, target, Army.ArmyTypes.Defender,
                            toCall.Count > 0 ? new TaleWorlds.Library.MBReadOnlyList<MobileParty>(toCall) : null);
                    }
                    finally { NoAiControlPatches.PlayerFormingArmy = false; }
                    MapSelection.Message(leader.Name + " 已建立军团");
                    // v4.279(第 1 项): 被挡下的舰队要给玩家一句中文说明(不静默跳过)
                    if (skippedFleetCall > 0)
                        MapSelection.Message("★ 已跳过 " + skippedFleetCall + " 支舰队: 舰队不能编入陆军军团"
                            + "(编入后一旦军团解散/合并, 原版会销毁整支舰队与全部舰船)。舰队请在地图上单独右键指挥。");
                    DLog.Force("建立军团: " + leader.Name + " 召集 " + toCall.Count + " 支部队, 跳过舰队 " + skippedFleetCall + " 支");

                    // 立刻把军团所有成员瞬移到军团长身边, 直接合并成军团
                    var leaderParty = leader.PartyBelongedTo;
                    TeleportArmyTogether(leaderParty != null ? leaderParty.Army : null, leader);

                    // 取消多选, 改为选中军团长(军团作为整体被指挥)
                    MapSelection.Clear();
                    if (leaderParty != null) MapSelection.Select(leaderParty);
                    MapSelection.Message(leader.Name + " 军团已集结");
                }
                catch (Exception ex) { DLog.Force("建立军团失败: " + ex.Message); }
            }

            // 军团刚建立: 所有成员受令走向军团长(不瞬移), 并在归队前保持受控(AI 不会半路把它们带走)
            private static void TeleportArmyTogether(Army army, Hero leader)
            {
                try
                {
                    if (army == null || leader == null) return;
                    var leaderParty = leader.PartyBelongedTo;
                    if (leaderParty == null) return;
                    int moved = 0;
                    foreach (var p in army.Parties)
                    {
                        if (p == null || !p.IsActive) continue;
                        if (ReferenceEquals(p, leaderParty)) continue;
                        // v4.279(第 1 项): 万一日后有人把舰队编进了军团, 这里也**不许**把它拖向军团长 ——
                        //   下面那句 SetMoveGoToPoint(..., NavigationType.Default) 是**陆地**导航, 对海上
                        //   部队会把它的导航类型改回陆地(舰队可能就此搁浅/被拖上岸)。舰队由玩家单独指挥。
                        bool isFleetHere = false;
                        try { isFleetHere = NavalTaskForce.IsTaskForce(p); } catch { }
                        if (isFleetHere)
                        {
                            DLog.Force("军团集结: 跳过舰队 " + MapSelection.NameOf(p) + "(海上部队不由军团调动)");
                            continue;
                        }
                        try
                        {
                            // 关键: 保持在国家意志指挥下(放开 AI 的话它们会各自跑去干别的)
                            // v4.258: 不再设 SetDoNotMakeNewDecisions(true) —— v4.90 已确认该标志会让 native
                            //   侧疑似冻结移动, 于是"组建军团后归队"的部队经常半路停死。原版 AI 决策本来就
                            //   被 DefArmyAi.AiGuard 屏蔽, 这里只需给驾驶目标 + 玩家接管标记。
                            p.Ai.SetDoNotMakeNewDecisions(false);
                            DefArmy.MarkPlayerOrder(p);
                            // v4.258: 给"卡住兜底"目标点(原来只 Touch 无点 -> native 不动就永远不动)
                            var lpt = new CampaignVec2(leaderParty.Position.ToVec2(), true);
                            CommandTimeout.Touch(p, lpt);
                            // 走过去: 到军团长身边, 靠近后由军团逻辑并入
                            p.SetMoveGoToPoint(lpt, MobileParty.NavigationType.Default);
                            moved++;
                        }
                        catch { }
                    }
                    DLog.Force("军团集结: " + moved + " 支部队已受令走向 " + leader.Name);
                }
                catch (Exception ex) { DLog.Force("军团集结异常: " + ex.Message); }
            }
        }

        // 军团指挥: 列出军团里的所有领主, 选中军团长=指挥整支军团, 选中成员=先脱离军团再指挥
        internal static class ArmyPicker
        {
            internal static void Show(TaleWorlds.CampaignSystem.Army army)
            {
                try
                {
                    var options = new System.Collections.Generic.List<InquiryElement>();
                    foreach (var p in army.Parties)
                    {
                        if (p == null || !p.IsActive) continue;
                        bool isLeader = ReferenceEquals(p, army.LeaderParty);
                        string name = MapSelection.NameOf(p) + (isLeader ? "（军团长：指挥整个军团）" : "（脱离军团，独立指挥）");
                        options.Add(new InquiryElement(p, name, null, true, null));
                    }
                    if (options.Count == 0) return;
                    PanelInputGuard.ShowPopup(new MultiSelectionInquiryData(
                        "军团指挥", "选择要指挥的领主。成员脱离军团后会变成可单独下令的小图标。",
                        options, true, 1, 1, "确定", "取消",
                        OnPick, null, null, false));
                }
                catch (Exception ex) { DLog.Force("军团列表异常: " + ex.Message); }
            }

            private static void OnPick(System.Collections.Generic.List<InquiryElement> selected)
            {
                try
                {
                    if (selected == null || selected.Count == 0) return;
                    var p = selected[0].Identifier as MobileParty;
                    if (p == null) return;
                    var army = p.Army;
                    if (army != null && !ReferenceEquals(p, army.LeaderParty))
                    {
                        p.Army = null;    // 脱离军团 -> 变成可单独指挥的部队
                        MapSelection.Message(MapSelection.NameOf(p) + " 已脱离军团");
                        DLog.Force("军团: " + MapSelection.NameOf(p) + " 已脱离军团");
                    }
                    MapSelection.Select(p);
                }
                catch (Exception ex) { DLog.Force("军团选择异常: " + ex.Message); }
            }
        }

        // v4.79g: 选国阶段右键城市名牌不再打开百科(名牌按钮走 Gauntlet 命令 AlternateClick, 地图点击拦截盖不到)
        // 军务页/海军页"地图点选": 点城市名牌 = 选中该城, 同样不弹百科
        [HarmonyPatch(typeof(SandBox.ViewModelCollection.Nameplate.SettlementNameplateVM), "ExecuteOpenEncyclopedia")]
        internal static class NoEncyclopediaDuringPick
        {
            private static bool Prefix()
            {
                try
                {
                    if (NationPickMode.Active && !NationalWillOrders.IsActive) return false;
                    if (SettlementPickMode.Active) return false;   // v4.282: 公共判据(军务页 + 海军页)
                    return true;
                }
                catch { return true; }
            }
        }

        [HarmonyPatch(typeof(SettlementVisual), "OnMapClick")]
        internal static class SettlementClick
        {
            private static bool Prefix(SettlementVisual __instance, bool followModifierUsed, ref bool __result)
            {
                try
                {
                    if (Outliner.BlocksMapInput) { __result = true; return false; }
                    // v4.144: 弹窗期间 / 鼠标在自建面板或导航栏上 -> 不当点定居点(修: 点弹窗结果打开驻军)
                    if (PanelInputGuard.BlockMapClick()) { __result = true; return false; }
                    if (PartyInfoHud.IsMouseOnButtons()) { __result = true; return false; }   // 28.6: 小窗选项按钮
                    PanelInputGuard.DiagClick("定居点");
                    // v4.165: 铁路页"地图点选起终点": 点定居点 = 交给铁路 UI, 吞掉本次点击
                    if (RailwaysUi.IsPickingEndpoint)
                    {
                        var railS = __instance != null && __instance.MapEntity != null ? __instance.MapEntity.Settlement : null;
                        if (railS != null) RailwaysUi.PickEndpoint(railS);
                        else MapSelection.Message("这里不是城镇/城堡");
                        __result = true;
                        return false;
                    }
                    // v4.75: 选国模式: 点定居点 = 点该国领地 -> 右侧国家栏
                    if (NationPickMode.Active)
                    {
                        if (!PanelScreen.IsMouseOnPanel()) NationPickMode.HandleMapClick();
                        __result = true;
                        return false;
                    }
                    if (followModifierUsed || !NationalWillOrders.IsActive) return true;
                    if (MapBoxSelect.BoxVisible) { __result = true; return false; }   // 框选拖动中, 不当点击
                    var settlement = __instance != null && __instance.MapEntity != null ? __instance.MapEntity.Settlement : null;
                    if (settlement == null) return true;
                    __result = true;

                    // 军务页/海军页"地图点选城市"待选状态: 点定居点 = 选中该聚落(v4.282: 公共注册表)
                    if (SettlementPickMode.Active)
                    {
                        SettlementPickMode.Deliver(settlement);
                        return false;
                    }

                    // 市场面板处于"本城"待选状态: 点城市 = 选中该城市场(不打开定居点抽屉)
                    if (MarketPanel.IsPickingCity && settlement.IsTown)
                    {
                        MarketPanel.PickCity(settlement);
                        return false;
                    }

                    // v4.165: 铁路地图模式下点城镇/城堡 = 直接打开铁路运输(装车/运输)
                    if (RailwaysUi.RailModeClick(settlement)) return false;

                    // ==================== v4.279(第 2 项): 与自研左键链共用同一份分流 ====================
                    //   这条路原来是 `if (选中第一支是我方) GoToSettlement(...) else 弹驻军` ——
                    //   舰队在这里会走 GoToSettlement 的舰队分支(= 前往但**不封锁**), 混选时同样漏掉非主选的部队。
                    //   现在一并交给 OrderSelectionToSettlement(陆军交给国家意志、舰队带封锁语义)。
                    OrderSelectionToSettlement(settlement, "点聚落图标");
                    return false;
                }
                catch (Exception ex) { DLog.Force("SettlementClick 异常: " + ex.Message); return true; }
            }
        }
    }
}
