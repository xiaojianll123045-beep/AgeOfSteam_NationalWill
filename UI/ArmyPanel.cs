using System;
using TaleWorlds.CampaignSystem.Settlements;

namespace FeudalInternalAffairs
{
    // 军务页(第 23 章; 导航栏第 13 格); 卡片式三页签: 募兵与征兵 / 野战军团 / 守备营
    internal static class ArmyPanel
    {
        private const string Key = "army";
        private const string Movie = "FeudalArmy";
        private const float Width = 680f;
        private static ArmyPanelVM _vm;
        private static float _acc;
        private static bool _mapPicking;

        internal static bool IsPickingSettlement { get { return _mapPicking; } }

        internal static void SetMapPicking(bool on) { _mapPicking = on; }

        // 地图点击定居点 -> 转发给面板 VM(选择/取消)
        internal static void PickSettlement(Settlement s)
        {
            try
            {
                if (_vm != null) _vm.TryPickSettlement(s);
                else _mapPicking = false;
            }
            catch { _mapPicking = false; }
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
                        delegate { _mapPicking = false; },   // onClosed: 任何关闭路径(切页签/互斥/X)都清掉"地图点选"状态
                        delegate (float dt) { if (_vm != null) _vm.TickAnim(dt); OnTick(dt); },
                        delegate { if (_vm != null) _vm.ClosePanelAnim(Width); });
                    PanelScreen.SetScrollHandler(Key, delegate (int dir) { if (_vm != null) _vm.ScrollStep(dir); });
                    RegisterSpots();
                }
                else if (_vm != null) _vm.Refresh();
            }
            catch (Exception ex) { DLog.Force("打开军务页失败: " + ex.Message); }
        }

        internal static void Close()
        {
            try { _mapPicking = false; PanelScreen.ClosePanel(Key); }
            catch { }
        }

        internal static void Tick(float dt) { }

        private static void OnTick(float dt)
        {
            try
            {
                // 每帧: 键入模式下的数字键/退格/回车轮询(实时刷新需要/拥有)
                if (_vm != null && _vm.PlanEditing) _vm.PollPlanKeys();

                _acc += dt;
                if (_acc < 3f) return;
                _acc = 0f;
                if (_vm != null) { _vm.Refresh(); RegisterSpots(); }
            }
            catch { }
        }

        // 热区(与 FeudalArmy.xml 布局常量一致)
        private static void RegisterSpots()
        {
            try
            {
                if (_vm == null) return;
                PanelScreen.ClearSpots();
                float sh = PanelScreen.ScreenHeight();
                PanelScreen.AddSpot(Width - 60f, 20f, 60f, 58f, _vm.ExecuteClose);          // X
                PanelScreen.AddSpot(30f, sh - 26f - 42f, 122f, 42f, _vm.ExecuteClose);      // 关闭
                PanelScreen.AddSpot(170f, sh - 26f - 42f, 150f, 42f, _vm.Parade);            // 阅兵

                // 页签
                PanelScreen.AddSpot(26f, 200f, 196f, 40f, delegate { _vm.SetTab(0); });
                PanelScreen.AddSpot(238f, 200f, 196f, 40f, delegate { _vm.SetTab(1); });
                PanelScreen.AddSpot(450f, 200f, 196f, 40f, delegate { _vm.SetTab(2); });

                if (_vm.Tab == 0)
                {
                    // 地点名点击 -> 视野飞到该城(v4.58)
                    PanelScreen.AddSpot(26f, 288f, 390f, 100f, _vm.FlyToCurrent);
                    // 地点: 列表选择 / 地图点选
                    PanelScreen.AddSpot(430f, 294f, 110f, 34f, _vm.OpenSettlementPicker);
                    PanelScreen.AddSpot(548f, 294f, 110f, 34f, _vm.StartMapPick);
                    // 数量: 步进 / 键入 / 最大
                    PanelScreen.AddSpot(170f, 428f, 58f, 36f, delegate { _vm.PlanStep(-100); });
                    PanelScreen.AddSpot(232f, 428f, 48f, 36f, delegate { _vm.PlanStep(-10); });
                    PanelScreen.AddSpot(284f, 428f, 48f, 36f, delegate { _vm.PlanStep(10); });
                    PanelScreen.AddSpot(336f, 428f, 58f, 36f, delegate { _vm.PlanStep(100); });
                    PanelScreen.AddSpot(398f, 428f, 58f, 36f, _vm.TogglePlanEdit);
                    PanelScreen.AddSpot(460f, 428f, 48f, 36f, _vm.PlanMax);
                    // v4.121: 兵种档位
                    PanelScreen.AddSpot(146f, 470f, 96f, 30f, delegate { _vm.SetRecruitTier(0); });
                    PanelScreen.AddSpot(252f, 470f, 96f, 30f, delegate { _vm.SetRecruitTier(3); });
                    PanelScreen.AddSpot(358f, 470f, 106f, 30f, delegate { _vm.SetRecruitTier(4); });
                    // 执行: 募兵 / 征兵
                    PanelScreen.AddSpot(26f, 676f, 304f, 48f, _vm.RecruitPlan);
                    PanelScreen.AddSpot(342f, 676f, 312f, 48f, _vm.ConscriptPlan);
                    // 征兵管理(三枚等宽)
                    PanelScreen.AddSpot(26f, 736f, 196f, 38f, _vm.RenewConscripts);
                    PanelScreen.AddSpot(238f, 736f, 196f, 38f, _vm.ReleaseConscripts);
                    PanelScreen.AddSpot(450f, 736f, 196f, 38f, _vm.TransferConscripts);
                }
                else if (_vm.Tab == 1)
                {
                    // 军团行: 驻防/巡逻/补员/解散(行位 322 + i*46, 按钮 y+7)
                    for (int i = 0; i < _vm.ShownLegionCount; i++)
                    {
                        int idx = i;
                        float y = 322f + i * 46f + 7f;
                        PanelScreen.AddSpot(308f, 322f + i * 46f, 104f, 46f, delegate { _vm.FlyToLegionHome(idx); });   // 驻地城市名 -> 跳视角
                        PanelScreen.AddSpot(455f, y, 40f, 32f, delegate { _vm.LegionAction(idx, 0); });   // 驻防
                        PanelScreen.AddSpot(500f, y, 40f, 32f, delegate { _vm.LegionAction(idx, 2); });   // 巡逻
                        PanelScreen.AddSpot(545f, y, 40f, 32f, delegate { _vm.LegionAction(idx, 3); });   // 补员
                        PanelScreen.AddSpot(590f, y, 40f, 32f, delegate { _vm.LegionAction(idx, 4); });   // 解散
                    }
                    PanelScreen.AddSpot(26f, 580f, 150f, 40f, _vm.AllToHome);   // 全军回防
                    PanelScreen.AddSpot(190f, 580f, 150f, 40f, _vm.UpgradeElite);   // v4.118: 精锐整编
                }
                else
                {
                    // 守备营行: 成立军团(行位 322 + i*38, 按钮 y+3)
                    for (int i = 0; i < _vm.ShownGarrisonCount; i++)
                    {
                        int idx = i;
                        float y = 322f + i * 38f + 3f;
                        PanelScreen.AddSpot(26f, 322f + i * 38f, 196f, 38f, delegate { _vm.FlyToGarrisonHome(idx); });   // 地点城市名 -> 跳视角
                        PanelScreen.AddSpot(550f, y, 110f, 32f, delegate { _vm.GarrisonAction(idx); });
                    }
                }
            }
            catch (Exception ex) { DLog.Force("军务页热区失败: " + ex.Message); }
        }
    }
}
