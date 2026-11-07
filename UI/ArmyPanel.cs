using System;
using SandBox.View.Map;
using SandBox.View.Map.Visuals;
using TaleWorlds.CampaignSystem.Settlements;

namespace FeudalInternalAffairs
{
    // 军务页(导航栏第 13 格); 三页签: 募兵与征兵 / 野战军团 / 守备营
    // 加宽到 860; 热区坐标与 FeudalArmy.xml 布局一一对应
    internal static class ArmyPanel
    {
        private const string Key = "army";
        private const string Movie = "FeudalArmy";
        private const float Width = 860f;
        private static ArmyPanelVM _vm;
        private static float _acc;

        // ==================== v4.282: 地图点选搬到公共机制 ====================
        //   原来这里的 `_mapPicking` + `PollMapPick()` + MapClickPatches 三条转发分支, 是**只有军务页**
        //   才有的私有实现; 海军页(造船港/敌港/海运两端港/进驻城市)需要同一能力, 于是抽成
        //   `Map\SettlementPickMode.cs`(公共注册表: 进入/命中/取消/每帧兜底轮询/命中判定)。
        //   本文件的对外签名(IsPickingSettlement / SetMapPicking / PickSettlement)一字不改 ——
        //   UI\ArmyVM.cs 与 Map\MapClickPatches.cs 都不用动, 只是内部改成转发。
        internal static bool IsPickingSettlement { get { return SettlementPickMode.Is(SettlementPickMode.OwnerArmy); } }

        // true = 进入地图点选(回调进 ArmyVM.TryPickSettlement); false = 退出(只退自己这一份)
        //   ★ prompt 传空: ArmyVM.StartMapPick 自己已经弹了地图消息(v4.98 的行为), 这里再弹一次就是两条。
        internal static void SetMapPicking(bool on)
        {
            try
            {
                if (on)
                {
                    if (_vm == null) return;
                    SettlementPickMode.Begin(SettlementPickMode.OwnerArmy, "", _vm.TryPickSettlement);
                }
                else if (SettlementPickMode.Is(SettlementPickMode.OwnerArmy))
                {
                    SettlementPickMode.Cancel();
                }
            }
            catch { }
        }

        // 地图点击定居点 -> 转发给面板 VM(选择/取消); 现在统一走公共注册表
        internal static void PickSettlement(Settlement s)
        {
            try
            {
                if (!SettlementPickMode.Is(SettlementPickMode.OwnerArmy)) return;   // 已被别的页顶掉 -> 不认
                SettlementPickMode.Deliver(s);
            }
            catch { SettlementPickMode.Cancel(); }
        }

        internal static bool IsOpen { get { return PanelScreen.IsOpen(Key); } }

        internal static void Open()
        {
            Tutorials.Page("page_army");
            try
            {
                if (!IsOpen)
                {
                    _vm = new ArmyPanelVM(Close);
                    _vm.OpenPanelAnim(Width);
                    PanelScreen.OpenPanel(Key, Movie, _vm,
                        delegate { SetMapPicking(false); },   // onClosed: 任何关闭路径(切页签/互斥/X)都清掉"地图点选"状态
                        delegate (float dt) { if (_vm != null) _vm.TickAnim(dt); OnTick(dt); },
                        delegate { if (_vm != null) _vm.ClosePanelAnim(Width); });
                    // v4.256: 滚轮翻页后必须重登记热区 —— 原来只调 ScrollStep, 窗口里换了 3 行而热区仍指旧 3 行,
                    //   最长 3 秒(下一次 OnTick 节流刷新前)内"点 A 行的按钮作用到 B 行", 解散类按钮尤其危险。
                    PanelScreen.SetScrollHandler(Key, delegate (int dir) { if (_vm != null) { _vm.ScrollStep(dir); RegisterSpots(); } });
                    RegisterSpots();
                }
                else if (_vm != null) _vm.Refresh();
            }
            catch (Exception ex) { DLog.Force("打开军务页失败: " + ex.Message); }
        }

        internal static void Close()
        {
            try { SetMapPicking(false); PanelScreen.ClosePanel(Key); }
            catch { }
        }

        // 建军设计器成功后回军务页守备营页签
        internal static void OpenAtGarrison()
        {
            try
            {
                Open();
                if (_vm != null) { _vm.SetTab(2); RegisterSpots(); }
            }
            catch { }
        }

        internal static void Tick(float dt) { }

        private static void OnTick(float dt)
        {
            try
            {
                // 每帧: 地图点选轮询(原版 SettlementVisual.OnMapClick 在面板打开时被输入屏蔽, 必须自研)
                PollMapPick();
                // 每帧: 键入模式下的数字键/退格/回车轮询(实时刷新需要/拥有)
                if (_vm != null && _vm.PlanEditing) _vm.PollPlanKeys();

                _acc += dt;
                if (_acc < 3f) return;
                _acc = 0f;
                if (_vm != null) { _vm.Refresh(); RegisterSpots(); }
            }
            catch { }
        }

        // 地图点选: 军务页打开 + 待选状态 -> 左键点地图 = 选中光标下的聚落(点图标或附近地面都认)。
        //   v4.282: 判定/取消(Esc/右键)/兜底轮询全部搬进公共机制, 这里只剩一行转发 —— 与海军页同一套。
        private static void PollMapPick()
        {
            try { SettlementPickMode.Poll(); RegisterSpots(); }
            catch { }
        }

        private static void SwitchTab(int t)
        {
            try
            {
                if (_vm == null) return;
                _vm.SetTab(t);
                _vm.ClearStatus();   // v4.258: 切页清掉上一页留下的操作结果(三个页签各有一份 @StatusText, 原来会串页显示)
                RegisterSpots();   // 立即换算出新页签的热区, 避免 3 秒窗口内旧热区误触
            }
            catch { }
        }

        // 热区(与 FeudalArmy.xml 布局常量一致; AddSpot 会统一加 PanelX 偏移)
        private static void RegisterSpots()
        {
            try
            {
                if (_vm == null) return;
                PanelScreen.ClearSpots();
                float sh = PanelScreen.ScreenHeight();

                // 标题栏 X / 底部栏
                PanelScreen.AddSpot(780f, 26f, 56f, 52f, _vm.ExecuteClose);                  // X
                PanelScreen.AddSpot(26f, sh - 70f, 150f, 44f, _vm.ExecuteClose);            // 关闭
                PanelScreen.AddSpot(190f, sh - 70f, 170f, 44f, _vm.Parade);                 // 阅兵

                // 页签
                PanelScreen.AddSpot(26f, 248f, 260f, 56f, delegate { SwitchTab(0); });
                PanelScreen.AddSpot(300f, 248f, 260f, 56f, delegate { SwitchTab(1); });
                PanelScreen.AddSpot(574f, 248f, 260f, 56f, delegate { SwitchTab(2); });

                if (_vm.Tab == 0)
                {
                    // 地点选择
                    PanelScreen.AddSpot(546f, 328f, 138f, 40f, _vm.OpenSettlementPicker);   // 列表选择
                    PanelScreen.AddSpot(694f, 328f, 140f, 40f, _vm.StartMapPick);           // 地图点选
                    PanelScreen.AddSpot(48f, 380f, 430f, 44f, _vm.FlyToCurrent);            // 地点名 -> 跳视角
                    // 数量: 步进 / 键入 / 最大
                    PanelScreen.AddSpot(228f, 534f, 88f, 44f, delegate { _vm.PlanStep(-100); });
                    PanelScreen.AddSpot(326f, 534f, 88f, 44f, delegate { _vm.PlanStep(-10); });
                    PanelScreen.AddSpot(424f, 534f, 88f, 44f, delegate { _vm.PlanStep(10); });
                    PanelScreen.AddSpot(522f, 534f, 88f, 44f, delegate { _vm.PlanStep(100); });
                    PanelScreen.AddSpot(620f, 534f, 88f, 44f, _vm.TogglePlanEdit);
                    PanelScreen.AddSpot(718f, 534f, 88f, 44f, _vm.PlanMax);
                    // 执行: 募兵 / 征兵
                    PanelScreen.AddSpot(26f, 864f, 394f, 52f, _vm.RecruitPlan);
                    PanelScreen.AddSpot(440f, 864f, 394f, 52f, _vm.ConscriptPlan);
                    // 征兵管理(v4.239: 强制征兵无时效, [续征]已删; 两枚等宽)
                    PanelScreen.AddSpot(26f, 924f, 390f, 42f, _vm.ReleaseConscripts);
                    PanelScreen.AddSpot(440f, 924f, 390f, 42f, _vm.TransferConscripts);
                }
                else if (_vm.Tab == 1)
                {
                    // 军团行(行高 110 + 行距 6 = 116): 驻地跳转 / 驻防·巡逻·补员·解散
                    //   行内坐标 + ListPanel 左缩进 26 = 面板坐标
                    //   v4.235: 删除行内[铁路 开/关](没有铁路也能开关, 用户要求删)
                    for (int i = 0; i < _vm.ShownLegionCount; i++)
                    {
                        int idx = i;
                        float ry = 432f + i * 116f;
                        PanelScreen.AddSpot(126f, ry + 60f, 150f, 24f, delegate { _vm.FlyToLegionHome(idx); });    // 驻地城市名 -> 跳视角
                        // v4.244: 按钮 74x26 -> 78x30(用户: 小窗按钮太小), 与 FeudalArmy.xml 同步
                        PanelScreen.AddSpot(280f, ry + 56f, 78f, 30f, delegate { _vm.LegionAction(idx, 0); });    // 驻防
                        PanelScreen.AddSpot(362f, ry + 56f, 78f, 30f, delegate { _vm.LegionAction(idx, 2); });    // 巡逻
                        PanelScreen.AddSpot(444f, ry + 56f, 78f, 30f, delegate { _vm.LegionAction(idx, 3); });    // 补员
                        PanelScreen.AddSpot(526f, ry + 56f, 78f, 30f, delegate { _vm.LegionStance(idx); });       // v4.243 状态(循环)
                        PanelScreen.AddSpot(608f, ry + 56f, 78f, 30f, delegate { _vm.LegionAction(idx, 4); });    // 解散
                        // v4.243: 将军特质文本 -> 悬停显示状态/训练度/特质说明
                        PanelScreen.AddSpot(694f, ry + 58f, 140f, 26f, delegate { _vm.ShowLegionTip(idx); });
                    }
                    PanelScreen.AddSpot(26f, 786f, 220f, 46f, _vm.AllToHome);    // 全军回防
                    PanelScreen.AddSpot(254f, 786f, 220f, 46f, _vm.ReleaseAllToAi);   // v4.245: 全军交还军事总监
                }
                else
                {
                    // 守备营行(行高 110 + 行距 6 = 116): 名称跳转 / 定位 / 成立军团
                    for (int i = 0; i < _vm.ShownGarrisonCount; i++)
                    {
                        int idx = i;
                        float y = 432f + i * 116f;
                        PanelScreen.AddSpot(126f, y + 40f, 300f, 28f, delegate { _vm.FlyToGarrisonHome(idx); });   // 地点名 -> 跳视角
                        PanelScreen.AddSpot(640f, y + 38f, 80f, 32f, delegate { _vm.FlyToGarrisonHome(idx); });    // 定位
                        PanelScreen.AddSpot(730f, y + 38f, 130f, 32f, delegate { _vm.GarrisonAction(idx); });      // 成立军团
                    }
                }
            }
            catch (Exception ex) { DLog.Force("军务页热区失败: " + ex.Message); }
        }
    }
}
