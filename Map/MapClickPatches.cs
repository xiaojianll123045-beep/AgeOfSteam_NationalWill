using System;
using HarmonyLib;
using SandBox.View.Map;
using SandBox.View.Map.Visuals;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameState;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;

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
                    if (!NationalWillOrders.IsActive) return true;
                    if (MapBoxSelect.BoxVisible) return false;   // 框选拖动中, 不当点击
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
                    if (followModifierUsed || !NationalWillOrders.IsActive) return true;
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
                    if (!NationalWillOrders.IsActive) return;
                    var screen = MapScreen.Instance;
                    if (screen == null) return;
                    var mouse = TaleWorlds.InputSystem.Input.MousePositionPixel;

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

                    // 左键: 拖动=框选
                    if (lPressed || lDown || lReleased)
                        MapBoxSelect.HandleLeftButton(lPressed, lDown, lReleased, mouse);

                    // 中键: 拖动=旋转视角
                    if (mPressed || mDown || mReleased)
                        MapBoxSelect.HandleMiddleButton(mPressed, mDown, mReleased, mouse);

                    // 右键: 拖动=平移地图; 单击=取消多选/军团菜单
                    if (!rPressed && !rDown && !rReleased) return;

                    bool wasPan = MapBoxSelect.HandleRightButton(rPressed, rDown, rReleased, mouse);
                    if (!rReleased || wasPan) return;

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
                        // 多选状态 -> 小队菜单(建立军团)
                        if (MapSelection.Count > 1)
                        {
                            MultiSelectMenu.Show();
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
                    MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
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
                    if ((selected[0].Identifier as string) != "form_army") return;
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
                        MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
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

                    MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
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
                    MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
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

        [HarmonyPatch(typeof(SettlementVisual), "OnMapClick")]
        internal static class SettlementClick
        {
            private static bool Prefix(SettlementVisual __instance, bool followModifierUsed, ref bool __result)
            {
                try
                {
                    if (followModifierUsed || !NationalWillOrders.IsActive) return true;
                    if (MapBoxSelect.BoxVisible) { __result = true; return false; }   // 框选拖动中, 不当点击
                    var settlement = __instance != null && __instance.MapEntity != null ? __instance.MapEntity.Settlement : null;
                    if (settlement == null) return true;
                    __result = true;

                    var selected = MapSelection.Selected;
                    if (selected != null && NationalWillOrders.IsOurs(selected))
                    {
                        NationalWillOrders.GoToSettlement(settlement);
                    }
                    else
                    {
                        MapSelection.Message("已看到 " + settlement.Name + " —— 先左键选中一支我方部队, 可命令其前往");
                    }
                    return false;
                }
                catch (Exception ex) { DLog.Force("SettlementClick 异常: " + ex.Message); return true; }
            }
        }
    }
}
