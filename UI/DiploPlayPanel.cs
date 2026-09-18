using System;

namespace FeudalInternalAffairs
{
    // 外交博弈面板(文档 22 章; 导航栏第 12 格)
    internal static class DiploPlayPanel
    {
        private const string Key = "play";
        private const string Movie = "FeudalDiploPlay";
        private const float Width = 680f;
        private static DiploPlayVM _vm;
        private static float _acc;

        internal static bool IsOpen { get { return PanelScreen.IsOpen(Key); } }

        internal static void Open()
        {
            try
            {
                if (!IsOpen)
                {
                    _vm = new DiploPlayVM(Close);
                    _vm.OpenPanelAnim(Width);
                    PanelScreen.OpenPanel(Key, Movie, _vm, null,
                        delegate (float dt) { if (_vm != null) _vm.TickAnim(dt); OnTick(dt); },
                        delegate { if (_vm != null) _vm.ClosePanelAnim(Width); });
                    RegisterSpots();
                }
                else if (_vm != null) _vm.Refresh();
            }
            catch (Exception ex) { DLog.Force("打开博弈页失败: " + ex.Message); }
        }

        internal static void Close()
        {
            try { PanelScreen.ClosePanel(Key); }
            catch { }
        }

        internal static void Tick(float dt) { }

        // 供 VM 在操作后立刻重建热区(不等 3 秒刷新)
        internal static void RefreshSpotsNow()
        {
            try { if (IsOpen) RegisterSpots(); } catch { }
        }

        private static void OnTick(float dt)
        {
            try
            {
                _acc += dt;
                if (_acc < 3f) return;
                _acc = 0f;
                if (_vm != null) { _vm.Refresh(); RegisterSpots(); }
            }
            catch { }
        }

        // 热区(与 FeudalDiploPlay.xml 布局常量一致; 按角色显示/注册)
        private static void RegisterSpots()
        {
            try
            {
                if (_vm == null) return;
                PanelScreen.ClearSpots();
                float sh = PanelScreen.ScreenHeight();
                PanelScreen.AddSpot(Width - 60f, 20f, 60f, 58f, _vm.ExecuteClose);       // X
                PanelScreen.AddSpot(30f, sh - 26f - 42f, 122f, 42f, _vm.ExecuteClose); // 关闭

                // 页签
                PanelScreen.AddSpot(26f, 86f, 120f, 38f, _vm.TabOverview);
                PanelScreen.AddSpot(156f, 86f, 150f, 38f, _vm.TabInvolved);

                // 卷入的国家(两栏): 拉拢按钮(行位 190 + i*44; 仅可拉拢的行注册热区)
                if (_vm.ShowInvolvedActions)
                {
                    for (int i = 0; i < 4; i++)
                    {
                        int idx = i;
                        float y = 190f + i * 44f;
                        if (_vm.RowCanSwaySide(0, idx)) PanelScreen.AddSpot(250f, y + 7f, 80f, 30f, delegate { _vm.SwaySide(0, idx); });
                        if (_vm.RowCanSwaySide(1, idx)) PanelScreen.AddSpot(574f, y + 7f, 80f, 30f, delegate { _vm.SwaySide(1, idx); });
                    }
                }
                // 自定义金额子页: -1000/-500/+500/+1000 + 确定/取消(行位 290/346)
                if (_vm.IsAmountPicking)
                {
                    PanelScreen.AddSpot(26f, 290f, 150f, 40f, delegate { _vm.AmountDelta(-1000); });
                    PanelScreen.AddSpot(186f, 290f, 150f, 40f, delegate { _vm.AmountDelta(-500); });
                    PanelScreen.AddSpot(346f, 290f, 150f, 40f, delegate { _vm.AmountDelta(500); });
                    PanelScreen.AddSpot(506f, 290f, 150f, 40f, delegate { _vm.AmountDelta(1000); });
                    PanelScreen.AddSpot(26f, 346f, 220f, 44f, _vm.ConfirmAmount);
                    PanelScreen.AddSpot(256f, 346f, 220f, 44f, _vm.CancelAmount);
                }
                // 拉拢对象选择页(总览"拉拢国家"按钮): 选择按钮(行位 206 + i*44, 取消 138 右上)
                if (_vm.IsSwayPicking)
                {
                    for (int i = 0; i < _vm.SwayCandCount && i < 6; i++)
                    {
                        int idx = i;
                        float y = 206f + i * 44f;
                        PanelScreen.AddSpot(550f, y + 6f, 104f, 32f, delegate { _vm.PickSwayTarget(idx); });
                    }
                    PanelScreen.AddSpot(546f, 138f, 104f, 34f, _vm.CancelSwayPick);
                }
                // 拉拢条件子页: 提供按钮(行位 206 + i*46, 取消 138 右上)
                if (_vm.IsSwaying)
                {
                    for (int i = 0; i < _vm.SwayCount && i < 6; i++)
                    {
                        int idx = i;
                        float y = 206f + i * 46f;
                        PanelScreen.AddSpot(550f, y + 7f, 104f, 32f, delegate { _vm.OfferSwayIdx(idx); });
                    }
                    PanelScreen.AddSpot(546f, 138f, 104f, 34f, _vm.CancelSway);
                }
                // 世界博弈: [查看] 切换(仅"卷入的国家"页; 行位 406 + i*30)
                if (_vm.ShowWorld)
                {
                    for (int i = 0; i < _vm.PlayCount && i < 4; i++)
                    {
                        int idx = i;
                        float y = 406f + i * 30f;
                        PanelScreen.AddSpot(560f, y + 2f, 96f, 26f, delegate { _vm.SelectPlay(idx); });
                    }
                }
                // 倾向区"拉拢国家"按钮(行位 604) + 倾向列表拉拢(行位 644 + i*30); 仅总览页
                if (_vm.ShowOverviewActions)
                {
                    PanelScreen.AddSpot(518f, 604f, 130f, 34f, _vm.BeginSwayPick);
                    for (int i = 0; i < _vm.LeanerCount && i < 4; i++)
                    {
                        int idx = i;
                        float y = 644f + i * 30f;
                        if (_vm.RowCanSwayLeaner(idx)) PanelScreen.AddSpot(560f, y + 2f, 96f, 26f, delegate { _vm.SwayLeaner(idx); });
                    }
                    // 加诉求(行位 566)
                    for (int i = 0; i < 5; i++)
                    {
                        int kind = i;
                        PanelScreen.AddSpot(26f + i * 126f, 566f, 118f, 34f, delegate { _vm.AddGoal(kind); });
                    }
                    if (_vm.IsInitiator) PanelScreen.AddSpot(26f, 806f, 220f, 40f, _vm.BackDown);
                    if (_vm.IsTarget) PanelScreen.AddSpot(26f, 806f, 220f, 40f, _vm.GiveIn);
                }
                // 未参战: 加入/中立(行位 806)
                if (_vm.IsThird)
                {
                    PanelScreen.AddSpot(26f, 806f, 200f, 40f, delegate { _vm.Join(0); });
                    PanelScreen.AddSpot(236f, 806f, 200f, 40f, delegate { _vm.Join(1); });
                    PanelScreen.AddSpot(446f, 806f, 200f, 40f, _vm.Decline);
                }
                // 割让选地(候选行 178 + i*34, 取消 138 右上)
                if (_vm.IsPicking)
                {
                    for (int i = 0; i < _vm.CandidateCount && i < 5; i++)
                    {
                        int idx = i;
                        float y = 178f + i * 34f;
                        PanelScreen.AddSpot(550f, y, 100f, 34f, delegate { _vm.PickCandidate(idx); });
                    }
                    PanelScreen.AddSpot(546f, 138f, 104f, 34f, _vm.CancelPick);
                }
            }
            catch (Exception ex) { DLog.Force("博弈页热区失败: " + ex.Message); }
        }
    }
}
