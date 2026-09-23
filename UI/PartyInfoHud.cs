using System;
using System;
using System.Collections.Generic;
using SandBox.View.Map;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.Library;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Library;

namespace FeudalInternalAffairs
{
    // 部队详情小窗 VM(v4.212): 点部队时在左下角显示全部 V3 数值
    public class PartyInfoVM : ViewModel
    {
        private string _title = "", _body = "", _railStatus = "";
        private bool _visible, _showRail, _showActions, _showEditActions;
        internal MobileParty Party;

        [DataSourceProperty] public string Title { get { return _title; } }
        [DataSourceProperty] public string Body { get { return _body; } }
        [DataSourceProperty] public string RailStatus { get { return _railStatus; } }
        [DataSourceProperty] public bool ShowRailStatus { get { return _showRail; } }
        [DataSourceProperty] public bool IsVisible { get { return _visible; } }
        // 28.6: 我们的部队显示 [管理/解散/扩容/分裂/合并]; 军团部队只显示[管理]
        [DataSourceProperty] public bool ShowActions { get { return _showActions; } }
        [DataSourceProperty] public bool ShowEditActions { get { return _showEditActions; } }

        internal void Hide()
        {
            _visible = false;
            _showActions = false;
            _showEditActions = false;
            Party = null;
            OnPropertyChangedWithValue(_visible, "IsVisible");
            OnPropertyChangedWithValue(_showActions, "ShowActions");
            OnPropertyChangedWithValue(_showEditActions, "ShowEditActions");
        }

        internal void Show(MobileParty p)
        {
            try
            {
                if (p == null) { Hide(); return; }
                Party = p;
                int men = 0;
                try { men = DefArmy.RegularsOf(p); } catch { }   // 显示口径 = 名册 − 将军
                DefLegion mine = null;
                try
                {
                    for (int i = 0; i < DefArmy.Legions.Count; i++)
                    {
                        var lp = DefArmy.LegionParty(DefArmy.Legions[i]);
                        if (lp != null && lp == p) { mine = DefArmy.Legions[i]; break; }
                    }
                }
                catch { }
                bool ours = false;
                try { ours = p.IsMainParty || NationalWillOrders.IsOurs(p); } catch { }
                bool railOn = false;
                try { railOn = ArmyDoctrine.IsRailTransport(p); } catch { }

                string name = MapSelection.NameOf(p);
                string cmd = "—";
                int lead = 0, tac = 0;
                try
                {
                    if (p.LeaderHero != null)
                    {
                        cmd = p.LeaderHero.Name != null ? p.LeaderHero.Name.ToString() : "—";
                        lead = p.LeaderHero.GetSkillValue(TaleWorlds.Core.DefaultSkills.Leadership);
                        tac = p.LeaderHero.GetSkillValue(TaleWorlds.Core.DefaultSkills.Tactics);
                    }
                }
                catch { }

                _title = name + (mine != null ? "  ·  野战军团 · " + DefArmy.StanceNames[DefArmy.StanceOf(mine)] : "");

                var sb = new System.Text.StringBuilder();
                // v4.245: 窗口收小到 660x470 -> 正文压到 9 行(合并多项; 去掉"行军速度 3.0"这种恒定值)
                sb.Append("指挥官 ").Append(cmd).Append("  (统率 ").Append(lead).Append(" · 战术 ").Append(tac).Append(")\n");
                sb.Append("兵力 ").Append(men.ToString("N0"));
                if (mine != null)
                {
                    int lim = ArmyDoctrine.CommandLimitOf(p);
                    sb.Append(" / 编制上限 ").Append(lim.ToString("N0")).Append(men > lim ? "  [超编!]" : "");
                }
                float food = 100f; try { food = p.Food; } catch { }
                sb.Append(" · 粮秣 ").Append((int)food).Append(food < 20f ? " [缺粮!]" : "").Append('\n');

                if (ours || mine != null)
                {
                    float org = 100f, mor = 50f;
                    try { org = DefArmyStats.OrgOf(p.Party); } catch { }
                    try { mor = DefArmyStats.MoraleOf(p.Party); } catch { }
                    string key = mine != null ? ArmyDoctrine.LegionKey(mine) : null;
                    float wd = !string.IsNullOrEmpty(key) ? ArmyDoctrine.WoundedOf(key) : 0f;
                    float vet = ArmyDoctrine.VetOfParty(p);
                    float outM, takeM, morM;
                    string tactic = ArmyDoctrine.TacticOf(p, out outM, out takeM, out morM);
                    var sh = new float[4];
                    ArmyDoctrine.CompositionOf(p, sh);
                    sb.Append("——— 军团状态 ———\n");
                    sb.Append("组织 ").Append((int)org).Append(org < 25f ? "(崩溃)" : (org < 50f ? "(下降)" : ""))
                      .Append(" · 士气 ").Append((int)mor).Append(" · 伤员 ").Append((int)wd)
                      .Append(" · 老兵度 ").Append((int)vet).Append('\n');
                    sb.Append("战术 ").Append(tactic).Append(" (输出 ×").Append(outM.ToString("F2"))
                      .Append(" · 承受 ×").Append(takeM.ToString("F2")).Append(" · 士气 ×").Append(morM.ToString("F2")).Append(")\n");
                    sb.Append("兵种 步 ").Append((int)(sh[0] * 100f)).Append("% · 弓 ").Append((int)(sh[1] * 100f))
                      .Append("% · 骑 ").Append((int)(sh[2] * 100f)).Append("% · 骑射 ").Append((int)(sh[3] * 100f)).Append("%\n");
                    if (mine != null)
                    {
                        // v4.243/245: 状态 / 训练度 / 将军特质 / 老兵占比(与军务页同口径)
                        int st = DefArmy.StanceOf(mine);
                        sb.Append("状态「").Append(DefArmy.StanceNames[st]).Append("」 训练 ")
                          .Append((int)Math.Round(DefArmy.TrainLevelOf(mine))).Append("/100 (战斗 ×")
                          .Append(DefArmy.TrainMultOf(mine).ToString("F2")).Append(") · 任务 ")
                          .Append(string.IsNullOrEmpty(mine.Task) ? "—" : mine.Task).Append('\n');
                        sb.Append("特质 ").Append(DefArmy.TraitText(mine.GeneralTraits)).Append('\n');
                        try
                        {
                            float avgP;
                            float share = Soldiers.VeteranShare(p, 70, out avgP);
                            sb.Append("老兵 ").Append((int)Math.Round(share * 100f)).Append("% · 熟练 ").Append((int)avgP)
                              .Append(" · 累计击杀 ").Append(Soldiers.TotalKills(p));
                        }
                        catch { }
                    }
                    string loss;
                    if (p.BesiegedSettlement != null) loss = "围城 8%/周";
                    else if (food < 20f) loss = "缺粮 12%/周";
                    else if (mor < 40f) loss = "士气崩溃 6%/周";
                    else loss = "无损耗";
                    sb.Append((mine != null ? " · " : "")).Append("损耗 ").Append(loss).Append('\n');
                }
                else
                {
                    string fac = "—";
                    try { if (p.MapFaction != null && p.MapFaction.Name != null) fac = p.MapFaction.Name.ToString(); } catch { }
                    sb.Append("所属阵营 ").Append(fac).Append('\n');
                    sb.Append("敌方/中立部队, 无本国编制数据");
                }
                _body = sb.ToString();
                _showRail = ours || mine != null;
                // 28.6: 非军团我方部队给 [管理/解散/扩容/分裂/合并]; 军团部队只给[管理](页内提示去军务页)
                bool main = false; try { main = p.IsMainParty; } catch { }
                bool inArmy = false; try { inArmy = p.Army != null; } catch { }
                bool commandable = false; try { commandable = NationalWillOrders.IsCommandable(p); } catch { }
                _showActions = (ours || mine != null) && !main && (commandable || mine != null);
                _showEditActions = _showActions && !inArmy;
                _railStatus = "铁路运输 " + (railOn ? "开 · 速度+20% 组织度-50% · " : "关 · 开启时 ")
                    + RailEngineInfo.TextOf(p);
                _visible = true;
                OnPropertyChangedWithValue(_title, "Title");
                OnPropertyChangedWithValue(_body, "Body");
                OnPropertyChangedWithValue(_railStatus, "RailStatus");
                OnPropertyChangedWithValue(_showRail, "ShowRailStatus");
                OnPropertyChangedWithValue(_showActions, "ShowActions");
                OnPropertyChangedWithValue(_showEditActions, "ShowEditActions");
                OnPropertyChangedWithValue(_visible, "IsVisible");
            }
            catch (Exception ex) { DLog.Force("部队小窗失败: " + ex.Message); Hide(); }
        }
    }

    // 小窗 HUD(独立 Gauntlet 层, 左下角, 不暂停游戏)
    internal static class PartyInfoHud
    {
        private static GauntletLayer _layer;
        private static PartyInfoVM _vm;
        private static MapScreen _map;
        internal static bool Enabled = true;

        internal static void Tick()
        {
            try
            {
                if (!Enabled) return;
                var cur = MapScreen.Instance;
                if (cur == null) return;
                if (!ReferenceEquals(cur, _map))
                {
                    if (_layer != null)
                    {
                        try { if (_map != null) _map.RemoveLayer(_layer); } catch { }
                        _layer = null;
                        _vm = null;
                        _lastPolled = null;
                    }
                    _map = cur;
                }
                if (_layer == null)
                {
                    _vm = new PartyInfoVM();
                    _layer = new GauntletLayer("FeudalPartyInfo", 341, false);
                    _layer.LoadMovie("FeudalPartyInfo", _vm);
                    _map.AddLayer(_layer);
                    DLog.Info("部队小窗: 层已建立(341), movie=FeudalPartyInfo");
                }
            }
            catch (Exception ex) { DLog.Force("部队小窗层失败: " + ex.Message); }
            PollSelection();
            RegisterActions();
        }

        private static MobileParty _lastPolled;
        private static DateTime _lastRefresh = DateTime.MinValue;

        private static void PollSelection()
        {
            try
            {
                if (_layer == null || _vm == null) return;
                var list = MapSelection.SelectedList;
                int n = list != null ? list.Count : 0;
                if (n != 1)
                {
                    if (_lastPolled != null) { _lastPolled = null; Hide(); }
                    return;
                }
                var p = list[0];
                if (p == null) return;
                if (ReferenceEquals(p, _lastPolled))
                {
                    if (!_vm.IsVisible) return;
                    if ((DateTime.UtcNow - _lastRefresh).TotalSeconds < 1.0) return;
                }
                _lastPolled = p;
                _lastRefresh = DateTime.UtcNow;
                Show(p);
            }
            catch { }
        }

        internal static void Show(MobileParty p)
        {
            try
            {
                Tick();
                if (_layer == null)
                {
                    DLog.Info("部队小窗: 点击收到但层未建立(MapScreen=" + (MapScreen.Instance != null) + ")");
                    return;
                }
                if (_vm == null)
                {
                    DLog.Info("部队小窗: 层在但 VM 为空");
                    return;
                }
                _vm.Show(p);
                _lastRefresh = DateTime.UtcNow;
                DLog.Info("部队小窗: 已显示 " + (p != null && p.Name != null ? p.Name.ToString() : "?") + " / 可见=" + _vm.IsVisible);
            }
            catch (Exception ex) { DLog.Force("部队小窗显示异常: " + ex.Message); }
        }

        internal static void Hide()
        {
            try { if (_vm != null) _vm.Hide(); } catch { }
        }

        // ==================== 28.6: 小窗选项热区 ====================
        // 布局与 FeudalPartyInfo.xml 一致: 窗口 660x470 @ (96, 屏高-660), 按钮行 y=84(112x46, 间距 12)
        // v4.244: 按钮从 108x30 放大到 140x46; v4.245: 窗口收小到 660x470(右边 756 < 消息避让位 760), 按钮 112x46
        private const float BtnRowX0 = 122f;      // 96 + 26
        private const float BtnY0 = 84f;
        private const float WinTop = 660f;        // 屏高 - 190(底距) - 470(高)
        private const float BtnW = 112f;
        private const float BtnStep = 124f;       // 112 宽 + 12 间距
        private const float BtnH = 46f;

        // v4.245: 小窗是否正在显示(消息列表据此自动避让)
        internal static bool IsShown
        {
            get { try { return _vm != null && _vm.IsVisible; } catch { return false; } }
        }

        // 鼠标是否落在五个选项按钮上(地图点击要避开; 否则点按会同时给小窗和地图)
        internal static bool IsMouseOnButtons()
        {
            try
            {
                if (_vm == null || !_vm.IsVisible || !_vm.ShowActions) return false;
                var m = TaleWorlds.InputSystem.Input.MousePositionPixel;
                float sh = PanelScreen.ScreenHeight();
                float y = sh - WinTop + BtnY0;
                return m.X >= BtnRowX0 && m.X <= BtnRowX0 + BtnStep * 4f + BtnW && m.Y >= y && m.Y <= y + BtnH;
            }
            catch { return false; }
        }

        // 每帧登记选项热区(面板/弹窗打开时让位给它们)
        private static void RegisterActions()
        {
            try
            {
                if (_vm == null || !_vm.IsVisible || !_vm.ShowActions) return;
                if (PanelScreen.AnyOpen || NationPickPanel.IsOpen) return;
                if (PanelInputGuard.AnyPopupActive()) return;
                PanelScreen.ClearSpots();
                float sh = PanelScreen.ScreenHeight();
                float y = sh - WinTop + BtnY0;
                PanelScreen.AddSpotRaw(BtnRowX0, y, BtnW, BtnH, delegate { ArmyManagePanel.Open(Cap()); });
                if (_vm.ShowEditActions)
                {
                    PanelScreen.AddSpotRaw(BtnRowX0 + BtnStep, y, BtnW, BtnH, delegate { Disband(Cap()); });
                    PanelScreen.AddSpotRaw(BtnRowX0 + 2f * BtnStep, y, BtnW, BtnH, delegate { ArmyManagePanel.Open(Cap(), ArmyManageVM.ModeAdd); });
                    PanelScreen.AddSpotRaw(BtnRowX0 + 3f * BtnStep, y, BtnW, BtnH, delegate { ArmyManagePanel.Open(Cap(), ArmyManageVM.ModeSplit); });
                    PanelScreen.AddSpotRaw(BtnRowX0 + 4f * BtnStep, y, BtnW, BtnH, delegate { ArmyManagePanel.OpenWithMerge(Cap()); });
                }
            }
            catch { }
        }

        private static MobileParty Cap()
        {
            try { return _vm != null ? _vm.Party : null; } catch { return null; }
        }

        private static void Disband(MobileParty p)
        {
            try
            {
                if (p == null) return;
                string msg;
                bool ok = VanillaArmySuppress.OurDisband(p, out msg);
                if (!string.IsNullOrEmpty(msg)) { try { MapSelection.Message(msg); } catch { } }
                DLog.Force("小窗解散(28.9): " + msg + " / ok=" + ok);
                bool still = false;
                try { still = p.IsActive && !p.IsDisbanding; } catch { still = false; }
                if (!still) Hide();
            }
            catch (Exception ex) { DLog.Force("小窗解散异常: " + ex.Message); }
        }

        // 部队状态变化(如切换铁路运输)时立即刷新小窗
        internal static void Refresh()
        {
            try { if (_vm != null && _vm.IsVisible && _vm.Party != null) _vm.Show(_vm.Party); } catch { }
        }
    }
}
