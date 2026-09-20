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
                try
                {
                    // v4.144: 弹窗期间 / 鼠标在自建面板或导航栏上 -> 不算地图点击(修: 点弹窗被当成点地面)
                    if (PanelInputGuard.BlockMapClick()) return false;
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
                    // 军务页"地图点选城市"模式: 地面点击不下部队命令, 改为命中检测(30 距离内视为点中该定居点)
                    // (地面点击会先于定居点点击触发, 所以这里不取消待选状态; 取消=再点一次[地图点选]按钮)
                    if (ArmyPanel.IsOpen && ArmyPanel.IsPickingSettlement)
                    {
                        try
                        {
                            var pt = MapBoxSelect.CaptureGroundPoint();
                            var at = TerritoryData.SettlementAt(pt.x, pt.y);
                            if (at != null && (at.Position.AsVec3() - pt).LengthSquared < 30f * 30f)
                                ArmyPanel.PickSettlement(at);
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
                try
                {
                    // v4.144: 弹窗期间 / 鼠标在自建面板或导航栏上 -> 不当成点部队
                    if (PanelInputGuard.BlockMapClick()) { __result = true; return false; }
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

                    // 左键: 拖动判定/单击标记(两个阶段都要, 选国阶段靠它区分"点击选国"与"拖动"); 框选选中只在指挥阶段生效
                    if (lPressed || lDown || lReleased)
                        MapBoxSelect.HandleLeftButton(lPressed, lDown, lReleased, mouse);

                    // 左键单击地面 = 选中部队移动(自研: 原版点击链被 NoClickCameraMove 等补丁拦截, 不再依赖它)
                    if (!pick && lReleased) HandleLeftClickGround(screen, mouse);

                    // 中键: 拖动=旋转视角
                    if (mPressed || mDown || mReleased)
                        MapBoxSelect.HandleMiddleButton(mPressed, mDown, mReleased, mouse);

                    // 右键: 拖动=平移地图; 单击=取消多选/军团菜单
                    if (!rPressed && !rDown && !rReleased) return;

                    bool wasPan = MapBoxSelect.HandleRightButton(rPressed, rDown, rReleased, mouse);
                    if (!rReleased || wasPan) return;
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
                    var visual = screen.CurrentVisualOfTooltip as MobilePartyVisual;
                    var party = visual != null && visual.MapEntity != null ? visual.MapEntity.MobileParty : null;
                    bool onOurParty = party != null && NationalWillOrders.IsOurs(party);

                    if (onOurParty)
                    {
                        var army = party.Army;
                        // 在军团里 -> 军团成员菜单(可拆领主)
                        if (army != null && army.Parties != null && army.Parties.Count > 1)
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

            // 左键单击全处理(自研; 原版点击链被 NoClickCameraMove 等补丁拦截, 不再依赖它):
            //   点我方部队=选中 / 点敌方=攻击 / 点城市=前往围城或打开抽屉 / 点圈=取消 / 点地面=移动
            private static void HandleLeftClickGround(MapScreen screen, Vec2 mouse)
            {
                try
                {
                    if (!NationalWillOrders.IsActive) return;
                    if (!MapBoxSelect.LastReleaseWasClick) return;
                    MapBoxSelect.ClearClickFlag();
                    if (PanelScreen.IsMouseOnPanel()) return;
                    if (NationalPanel.IsMouseOnFlag()) return;
                    if (mouse.X < PanelScreen.PanelX) return;    // 左侧导航栏区域
                    if (mouse.Y < 66f) return;                   // 顶部地图栏区域

                    var vis = screen.CurrentVisualOfTooltip;

                    // v4.98: 军务页"地图点选城市"模式: 左键点地图 = 选中光标下的聚落
                    // (原版 ProcessTravel 点击链已被拦, 这里自研; 点图标/点附近地面都认)
                    if (ArmyPanel.IsOpen && ArmyPanel.IsPickingSettlement)
                    {
                        try
                        {
                            var sv0 = vis as SettlementVisual;
                            var st0 = sv0 != null && sv0.MapEntity != null ? sv0.MapEntity.Settlement : null;
                            if (st0 != null) { ArmyPanel.PickSettlement(st0); return; }
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
                                if (best != null) { ArmyPanel.PickSettlement(best); return; }
                            }
                            MapSelection.Message("地图点选: 未命中聚落, 请点击城市图标");
                        }
                        catch { }
                        return;
                    }

                    // 1) 点部队(优先用光标距离检测, 不依赖原版悬停拾取)
                    var party = MapBoxSelect.FindPartyAtCursor(60f);
                    if (party == null)
                    {
                        var pv0 = vis as MobilePartyVisual;
                        if (pv0 != null && pv0.MapEntity != null) party = pv0.MapEntity.MobileParty;
                    }
                    DLog.Force("单击: 部队=" + (party != null ? MapSelection.NameOf(party) : "无")
                        + " 我方=" + (party != null ? NationalWillOrders.IsOurs(party).ToString() : "-")
                        + " 选中=" + MapSelection.Count
                        + " tooltip=" + (vis != null ? vis.GetType().Name : "null"));
                    if (party != null)
                    {
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
                        if (sel != null && NationalWillOrders.IsOurs(sel)) NationalWillOrders.Engage(party);
                        else MapSelection.Message("先左键选中一支我方部队, 再点击目标下达命令");
                        return;
                    }

                    // 2) 点定居点(优先悬停视觉, 失败用地面坐标兜底 12 米内)
                    var sv = vis as SettlementVisual;
                    var st2 = sv != null && sv.MapEntity != null ? sv.MapEntity.Settlement : null;
                    if (st2 == null)
                    {
                        var pt2 = MapBoxSelect.CaptureGroundPoint();
                        if (pt2.z > -5000f)
                        {
                            float bd = 12f * 12f;
                            foreach (var x in Settlement.All)
                            {
                                if (x == null) continue;
                                float dx = x.Position.X - pt2.x, dy = x.Position.Y - pt2.y;
                                float d = dx * dx + dy * dy;
                                if (d < bd) { bd = d; st2 = x; }
                            }
                        }
                    }
                    if (st2 != null)
                    {
                        var sel2 = MapSelection.Selected;
                        if (sel2 != null && NationalWillOrders.IsOurs(sel2)) NationalWillOrders.GoToSettlement(st2);
                        else GarrisonPanel.Open(st2);   // v4.101: 弹驻军列表
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

        // 多选状态下右键部队的小队菜单(目前只有"建立军团")
        internal static class MultiSelectMenu
        {
            internal static void Show()
            {
                try
                {
                    var options = new System.Collections.Generic.List<InquiryElement>
                    {
                        new InquiryElement("form_army", "建立军团", null, true, "从选中的部队里挑一位领主建立军团")
                    };
                    if (DefArmyMenu.CountSelectedDefLegions() >= 2)
                        options.Add(new InquiryElement("merge_def", "合并国防军", null, true, "把选中的国防军军团合并为一支(多余将军卸任转为 1 名小兵, 免费)"));
                    if (DefArmyMenu.CountSelectedDefLegions() >= 1)
                        options.Add(new InquiryElement("split_def", "拆分国防军", null, true, "把兵力最多的一支一分为二(1 名小兵升任将军, 花费 500)"));
                    PanelInputGuard.ShowPopup(new MultiSelectionInquiryData(
                        "部队指挥", "已选中 " + MapSelection.Count + " 支部队",
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
                    foreach (var p in MapSelection.SelectedList)
                    {
                        if (p == null || !p.IsActive) continue;
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
                            "· 选中的部队里至少要有 1 位符合条件的家族领袖",
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
                    var toCall = new System.Collections.Generic.List<MobileParty>();
                    foreach (var p in MapSelection.SelectedList)
                    {
                        if (p == null || !p.IsActive) continue;
                        if (ReferenceEquals(p, leader.PartyBelongedTo)) continue;
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
                    DLog.Force("建立军团: " + leader.Name + " 召集 " + toCall.Count + " 支部队");

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
                        try
                        {
                            // 关键: 保持在国家意志指挥下(放开 AI 的话它们会各自跑去干别的)
                            p.Ai.SetDoNotMakeNewDecisions(true);
                            CommandTimeout.Touch(p);
                            // 走过去: 到军团长身边, 靠近后由军团逻辑并入
                            p.SetMoveGoToPoint(leaderParty.Position, MobileParty.NavigationType.Default);
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
        [HarmonyPatch(typeof(SandBox.ViewModelCollection.Nameplate.SettlementNameplateVM), "ExecuteOpenEncyclopedia")]
        internal static class NoEncyclopediaDuringPick
        {
            private static bool Prefix()
            {
                try { return !(NationPickMode.Active && !NationalWillOrders.IsActive); }
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
                    // v4.144: 弹窗期间 / 鼠标在自建面板或导航栏上 -> 不当点定居点(修: 点弹窗结果打开驻军)
                    if (PanelInputGuard.BlockMapClick()) { __result = true; return false; }
                    PanelInputGuard.DiagClick("定居点");
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

                    // 军务页"地图点选城市"待选状态: 点定居点 = 选中该聚落
                    if (ArmyPanel.IsOpen && ArmyPanel.IsPickingSettlement)
                    {
                        ArmyPanel.PickSettlement(settlement);
                        return false;
                    }

                    // 市场面板处于"本城"待选状态: 点城市 = 选中该城市场(不打开定居点抽屉)
                    if (MarketPanel.IsPickingCity && settlement.IsTown)
                    {
                        MarketPanel.PickCity(settlement);
                        return false;
                    }

                    var selected = MapSelection.Selected;
                    if (selected != null && NationalWillOrders.IsOurs(selected))
                    {
                        NationalWillOrders.GoToSettlement(settlement);
                    }
                    else
                    {
                        // 没选中部队 -> 弹驻军列表(v4.101; 里面可点[查看建筑]打开原抽屉)
                        GarrisonPanel.Open(settlement);
                    }
                    return false;
                }
                catch (Exception ex) { DLog.Force("SettlementClick 异常: " + ex.Message); return true; }
            }
        }
    }
}
