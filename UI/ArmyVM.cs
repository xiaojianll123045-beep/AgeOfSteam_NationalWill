using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace FeudalInternalAffairs
{
    // 军团一行(第 23 章 军务页)
    public class LegionRowVM : ViewModel
    {
        internal readonly string PartyId;
        internal readonly string HomeId;      // v4.58: 驻地(点城市名跳视角)
        private readonly string _name, _men, _menColor, _morale, _moraleColor, _home, _task;

        internal LegionRowVM(DefLegion lg, MobileParty p)
        {
            PartyId = lg != null ? lg.PartyId : "";
            HomeId = lg != null ? lg.HomeId : "";
            _name = DefArmy.OurKingdom != null && DefArmy.OurKingdom.Name != null
                ? DefArmy.OurKingdom.Name.ToString() + "国防军第" + lg.Number + "军团"
                : "国防军第" + lg.Number + "军团";
            int men = DefArmy.LegionMen(lg);
            _men = men.ToString("N0") + " 兵";
            _menColor = men >= 1000 ? "#E8C33AFF" : "#F0E4C8FF";
            _morale = DefArmy.MoraleText(p);
            int mor = 0;
            try { if (p != null) mor = (int)p.Morale; } catch { }
            _moraleColor = mor >= 60 ? "#7FBF6AFF" : (mor >= 30 ? "#E8C33AFF" : "#C96A5AFF");
            _home = lg != null && lg.HomeId != null ? ShortName(lg.HomeId) : "—";
            _task = lg != null ? (lg.Task ?? "驻守") : "—";
        }

        private static string ShortName(string sid)
        {
            try
            {
                var s = DefArmy.FindSettlement(sid);
                return s != null && s.Name != null ? s.Name.ToString() : sid;
            }
            catch { return sid; }
        }

        [DataSourceProperty] public string Name { get { return _name; } }
        [DataSourceProperty] public string Men { get { return _men; } }
        [DataSourceProperty] public string MenColor { get { return _menColor; } }
        [DataSourceProperty] public string Morale { get { return _morale; } }
        [DataSourceProperty] public string MoraleColor { get { return _moraleColor; } }
        [DataSourceProperty] public string Home { get { return _home; } }
        [DataSourceProperty] public string Task { get { return _task; } }
    }

    // 守备营一行
    public class GarrisonRowVM : ViewModel
    {
        internal readonly string SettlementId;
        private readonly string _name, _men, _equip, _equipColor;

        internal GarrisonRowVM(DefGarrison g)
        {
            SettlementId = g != null ? g.SettlementId : "";
            var s = DefArmy.FindSettlement(SettlementId);
            _name = s != null && s.Name != null ? s.Name.ToString() : SettlementId;
            _men = DefArmy.GarrisonMen(g).ToString("N0") + " 兵";   // v4.84: 显示实时驻军兵力(与建军可用数一致)
            _equip = g != null && g.LowEquip ? "低配(士气-10%)" : "装备齐整";
            _equipColor = g != null && g.LowEquip ? "#C96A5AFF" : "#7FBF6AFF";
        }

        [DataSourceProperty] public string Name { get { return _name; } }
        [DataSourceProperty] public string Men { get { return _men; } }
        [DataSourceProperty] public string Equip { get { return _equip; } }
        [DataSourceProperty] public string EquipColor { get { return _equipColor; } }
    }

    // 军务页 VM(导航栏第 13 格); 三页签: 募兵与征兵 / 野战军团 / 守备营
    public class ArmyPanelVM : PanelVMBase
    {
        private const int LegionPage = 5;
        private const int GarrisonPage = 8;
        private const int PlanMin = 1;
        private const int PlanMaxLim = 999999;   // v4.89: 募兵计划上限放宽

        private readonly Action _onClose;
        private readonly List<Settlement> _cands = new List<Settlement>();
        private readonly List<Settlement> _srcList = new List<Settlement>();
        private readonly List<DefLegion> _legionView = new List<DefLegion>();
        private int _candIdx;
        private int _srcIdx;
        private int _scroll;
        private int _plan = 100;
        private bool _planEdit;
        private bool _enterWasDown;   // v4.86: 回车确认的边沿检测
        private int _tab;   // 0 募兵/征兵 1 军团 2 守备营
        private bool _pickingSettle;

        private string _overview = "", _money = "", _settleName = "—", _srcName = "—", _settleInfo = "",
            _planText = "", _needRecruitText = "", _needConscriptText = "", _haveGoldPop = "", _haveEquip = "",
            _planVerdict = "", _planVerdictColor = "", _planVerdictBg = "#00000000",
            _menText = "0", _legionText = "0", _garrisonText = "0",
            _conscriptText = "", _recruitBtn = "", _conscriptBtn = "", _formLegionBtn = "",
            _status = "", _legionHelp = "", _garrisonHelp = "";
        private bool _showRecruit = true, _showLegion, _showGarrison;
        private string _tabRecruitColor = "#E8C33AFF", _tabLegionColor = "#8A8070FF", _tabGarrisonColor = "#8A8070FF";
        private bool _tabLineRecruit = true, _tabLineLegion, _tabLineGarrison;
        private string _recruitBtnColor = "#7FBF6AFF", _conscriptBtnColor = "#7FBF6AFF";
        private bool _legionEmpty, _garrisonEmpty;

        public ArmyPanelVM(Action onClose)
        {
            _onClose = onClose;
            LegionRows = new MBBindingList<LegionRowVM>();
            GarrisonRows = new MBBindingList<GarrisonRowVM>();
            Refresh();
        }

        public MBBindingList<LegionRowVM> LegionRows { get; private set; }
        public MBBindingList<GarrisonRowVM> GarrisonRows { get; private set; }

        [DataSourceProperty] public string OverviewText { get { return _overview; } }
        [DataSourceProperty] public string MoneyText { get { return _money; } }
        [DataSourceProperty] public string SettleName { get { return _settleName; } }
        [DataSourceProperty] public string SrcName { get { return _srcName; } }
        [DataSourceProperty] public string SettleInfo { get { return _settleInfo; } }
        [DataSourceProperty] public string PlanText { get { return _planText; } }
        [DataSourceProperty] public string NeedRecruitText { get { return _needRecruitText; } }
        [DataSourceProperty] public string NeedConscriptText { get { return _needConscriptText; } }
        [DataSourceProperty] public string HaveGoldPop { get { return _haveGoldPop; } }
        [DataSourceProperty] public string HaveEquip { get { return _haveEquip; } }
        [DataSourceProperty] public string PlanVerdictText { get { return _planVerdict; } }
        [DataSourceProperty] public string PlanVerdictColor { get { return _planVerdictColor; } }
        [DataSourceProperty] public string PlanVerdictBg { get { return _planVerdictBg; } }
        [DataSourceProperty] public string MenText { get { return _menText; } }
        [DataSourceProperty] public string LegionText { get { return _legionText; } }
        [DataSourceProperty] public string GarrisonText { get { return _garrisonText; } }
        [DataSourceProperty] public string ConscriptText { get { return _conscriptText; } }
        [DataSourceProperty] public string RecruitBtnText { get { return _recruitBtn; } }
        [DataSourceProperty] public string ConscriptBtnText { get { return _conscriptBtn; } }
        [DataSourceProperty] public string FormLegionBtnText { get { return _formLegionBtn; } }
        [DataSourceProperty] public string StatusText { get { return _status; } }
        [DataSourceProperty] public string LegionHelp { get { return _legionHelp; } }
        [DataSourceProperty] public string GarrisonHelp { get { return _garrisonHelp; } }

        [DataSourceProperty] public bool ShowRecruit { get { return _showRecruit; } }
        [DataSourceProperty] public bool ShowLegion { get { return _showLegion; } }
        [DataSourceProperty] public bool ShowGarrison { get { return _showGarrison; } }
        [DataSourceProperty] public string TabRecruitColor { get { return _tabRecruitColor; } }
        [DataSourceProperty] public string TabLegionColor { get { return _tabLegionColor; } }
        [DataSourceProperty] public string TabGarrisonColor { get { return _tabGarrisonColor; } }
        [DataSourceProperty] public bool TabLineRecruit { get { return _tabLineRecruit; } }
        [DataSourceProperty] public bool TabLineLegion { get { return _tabLineLegion; } }
        [DataSourceProperty] public bool TabLineGarrison { get { return _tabLineGarrison; } }
        [DataSourceProperty] public string RecruitBtnColor { get { return _recruitBtnColor; } }
        [DataSourceProperty] public string ConscriptBtnColor { get { return _conscriptBtnColor; } }
        [DataSourceProperty] public bool LegionEmptyVisible { get { return _legionEmpty; } }
        [DataSourceProperty] public bool GarrisonEmptyVisible { get { return _garrisonEmpty; } }

        public void ExecuteClose() { if (_onClose != null) _onClose(); }

        internal int ShownLegionCount { get { return LegionRows.Count; } }
        internal int ShownGarrisonCount { get { return GarrisonRows.Count; } }
        internal bool PlanEditing { get { return _planEdit; } }
        internal int Plan { get { return _plan; } }
        internal int Tab { get { return _tab; } }

        internal Settlement Current
        {
            get { return _candIdx >= 0 && _candIdx < _cands.Count ? _cands[_candIdx] : null; }
        }

        // 当前募兵/征兵来源(城镇=劳工, 附属村庄=农民)
        internal Settlement Source
        {
            get { return _srcIdx >= 0 && _srcIdx < _srcList.Count ? _srcList[_srcIdx] : Current; }
        }

        internal void SetTab(int t)
        {
            try
            {
                _tab = t < 0 ? 0 : (t > 2 ? 2 : t);
                _planEdit = false;
                if (_pickingSettle) { _pickingSettle = false; ArmyPanel.SetMapPicking(false); }
                Refresh();
            }
            catch { }
        }

        // ---- 计划数量(玩家输入) ----
        internal void PlanStep(int delta)
        {
            try
            {
                if (_planEdit) _planEdit = false;
                _plan += delta;
                if (_plan < PlanMin) _plan = PlanMin;
                if (_plan > PlanMaxLim) _plan = PlanMaxLim;
                Refresh();
            }
            catch { }
        }

        internal void PlanMax()
        {
            try
            {
                _planEdit = false;
                int pop = DefArmy.CommonersOf(Source);
                _plan = Math.Min(PlanMaxLim, Math.Max(PlanMin, pop));
                Refresh();
            }
            catch { }
        }

        internal void TogglePlanEdit()
        {
            try
            {
                if (_planEdit) { _planEdit = false; Refresh(); return; }
                _planEdit = true;
                _plan = 0;
                _status = "键入中: 数字键输入数量 · 退格删除 · 回车确认";
                Refresh();
            }
            catch { }
        }

        internal void PollPlanKeys()
        {
            try
            {
                if (!_planEdit) return;
                bool changed = false;
                int d = DigitPressed();
                if (d >= 0)
                {
                    long v = (long)_plan * 10 + d;
                    _plan = v > PlanMaxLim ? PlanMaxLim : (int)v;
                    changed = true;
                }
                if (TaleWorlds.InputSystem.Input.IsKeyPressed(TaleWorlds.InputSystem.InputKey.BackSpace))
                {
                    _plan /= 10;
                    changed = true;
                }
                // v4.86: 面板打开时原版读不到回车(PanelInputGuard 拦截), 这里改用按住状态自检按下边沿
                bool enterDown = TaleWorlds.InputSystem.Input.IsKeyDown(TaleWorlds.InputSystem.InputKey.Enter)
                    || TaleWorlds.InputSystem.Input.IsKeyDown(TaleWorlds.InputSystem.InputKey.NumpadEnter);
                if (enterDown && !_enterWasDown)
                {
                    if (_plan < PlanMin) _plan = 100;
                    _planEdit = false;
                    _status = "数量已设为 " + _plan.ToString("N0");
                    changed = true;
                }
                _enterWasDown = enterDown;
                if (changed) Refresh();
            }
            catch { }
        }

        private static int DigitPressed()
        {
            if (TaleWorlds.InputSystem.Input.IsKeyPressed(TaleWorlds.InputSystem.InputKey.D0)
                || TaleWorlds.InputSystem.Input.IsKeyPressed(TaleWorlds.InputSystem.InputKey.Numpad0)) return 0;
            if (TaleWorlds.InputSystem.Input.IsKeyPressed(TaleWorlds.InputSystem.InputKey.D1)
                || TaleWorlds.InputSystem.Input.IsKeyPressed(TaleWorlds.InputSystem.InputKey.Numpad1)) return 1;
            if (TaleWorlds.InputSystem.Input.IsKeyPressed(TaleWorlds.InputSystem.InputKey.D2)
                || TaleWorlds.InputSystem.Input.IsKeyPressed(TaleWorlds.InputSystem.InputKey.Numpad2)) return 2;
            if (TaleWorlds.InputSystem.Input.IsKeyPressed(TaleWorlds.InputSystem.InputKey.D3)
                || TaleWorlds.InputSystem.Input.IsKeyPressed(TaleWorlds.InputSystem.InputKey.Numpad3)) return 3;
            if (TaleWorlds.InputSystem.Input.IsKeyPressed(TaleWorlds.InputSystem.InputKey.D4)
                || TaleWorlds.InputSystem.Input.IsKeyPressed(TaleWorlds.InputSystem.InputKey.Numpad4)) return 4;
            if (TaleWorlds.InputSystem.Input.IsKeyPressed(TaleWorlds.InputSystem.InputKey.D5)
                || TaleWorlds.InputSystem.Input.IsKeyPressed(TaleWorlds.InputSystem.InputKey.Numpad5)) return 5;
            if (TaleWorlds.InputSystem.Input.IsKeyPressed(TaleWorlds.InputSystem.InputKey.D6)
                || TaleWorlds.InputSystem.Input.IsKeyPressed(TaleWorlds.InputSystem.InputKey.Numpad6)) return 6;
            if (TaleWorlds.InputSystem.Input.IsKeyPressed(TaleWorlds.InputSystem.InputKey.D7)
                || TaleWorlds.InputSystem.Input.IsKeyPressed(TaleWorlds.InputSystem.InputKey.Numpad7)) return 7;
            if (TaleWorlds.InputSystem.Input.IsKeyPressed(TaleWorlds.InputSystem.InputKey.D8)
                || TaleWorlds.InputSystem.Input.IsKeyPressed(TaleWorlds.InputSystem.InputKey.Numpad8)) return 8;
            if (TaleWorlds.InputSystem.Input.IsKeyPressed(TaleWorlds.InputSystem.InputKey.D9)
                || TaleWorlds.InputSystem.Input.IsKeyPressed(TaleWorlds.InputSystem.InputKey.Numpad9)) return 9;
            return -1;
        }

        // ---- 操作 ----
        // v4.58: 城市名点击 -> 视野飞到该城(用户需求)
        internal void FlyToCurrent()
        {
            try { PanelScreen.JumpToSettlement(Current); } catch { }
        }

        internal void FlyToLegionHome(int i)
        {
            try
            {
                if (i < 0 || i >= LegionRows.Count) return;
                PanelScreen.JumpToSettlement(DefArmy.FindSettlement(LegionRows[i].HomeId));
            }
            catch { }
        }

        internal void FlyToGarrisonHome(int i)
        {
            try
            {
                if (i < 0 || i >= GarrisonRows.Count) return;
                PanelScreen.JumpToSettlement(DefArmy.FindSettlement(GarrisonRows[i].SettlementId));
            }
            catch { }
        }

        internal void CycleSettlement(int dir)
        {
            try
            {
                if (_cands.Count == 0) return;
                _planEdit = false;
                _candIdx = (_candIdx + dir) % _cands.Count;
                if (_candIdx < 0) _candIdx += _cands.Count;
                _srcIdx = 0;
                Refresh();
            }
            catch { }
        }

        // 弹窗选择城市
        internal void OpenSettlementPicker()
        {
            try
            {
                _planEdit = false;
                if (_cands.Count == 0) { _status = "本国没有可选聚落(城镇/城堡)"; Refresh(); return; }
                var options = new List<InquiryElement>();
                for (int i = 0; i < _cands.Count; i++)
                {
                    var s = _cands[i];
                    if (s == null) continue;
                    string kind = s.IsTown ? "城镇" : "城堡";
                    int g = 0;
                    try { g = GarrisonAvailable(s); } catch { }
                    int pop = 0;
                    try { pop = DefArmy.CommonersOf(s); } catch { }
                    string txt = (s.Name != null ? s.Name.ToString() : s.StringId)
                        + "（" + kind + "）· 可征人口 " + pop + " · 守备营 " + g + " 兵";
                    options.Add(new InquiryElement(s.StringId, txt, null, true, null));
                }
                MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                    "选择征兵地点", "本国共有 " + options.Count + " 处城镇/城堡",
                    options, true, 1, 1, "确定", "取消",
                    OnSettlementPicked, null, null, false));
            }
            catch (Exception ex) { DLog.Force("选择城市弹窗异常: " + ex.Message); }
        }

        private void OnSettlementPicked(List<InquiryElement> selected)
        {
            try
            {
                if (selected == null || selected.Count == 0) return;
                string id = selected[0].Identifier as string;
                for (int i = 0; i < _cands.Count; i++)
                {
                    if (_cands[i] != null && _cands[i].StringId == id)
                    {
                        _candIdx = i;
                        _srcIdx = 0;
                        _status = "已选择 " + (_cands[i].Name != null ? _cands[i].Name.ToString() : id);
                        break;
                    }
                }
                Refresh();
            }
            catch (Exception ex) { DLog.Force("选择城市异常: " + ex.Message); }
        }

        // 地图点选: 进入待选模式, 下次点击地图上的定居点时选中
        internal bool PickingSettlement { get { return _pickingSettle; } }

        internal void StartMapPick()
        {
            try
            {
                _planEdit = false;
                if (_pickingSettle)   // 再点一次 = 取消
                {
                    _pickingSettle = false;
                    ArmyPanel.SetMapPicking(false);
                    _status = "已取消地图选点";
                    Refresh();
                    return;
                }
                _pickingSettle = true;
                ArmyPanel.SetMapPicking(true);
                MapSelection.Message("请在地图上左键点击本国的城镇/城堡（再点一次[地图点选]可取消）");
                _status = "地图点选中: 请点击地图上的本国城镇/城堡...";
                Refresh();
            }
            catch { }
        }

        // 地图点选回调(settlement 为 null 表示取消)
        internal void TryPickSettlement(Settlement s)
        {
            try
            {
                _pickingSettle = false;
                ArmyPanel.SetMapPicking(false);
                if (s == null) { _status = "已取消地图选点"; Refresh(); return; }
                for (int i = 0; i < _cands.Count; i++)
                {
                    if (_cands[i] != null && _cands[i].StringId == s.StringId)
                    {
                        _candIdx = i;
                        _srcIdx = 0;
                        _status = "已选择 " + (_cands[i].Name != null ? _cands[i].Name.ToString() : s.StringId);
                        Refresh();
                        return;
                    }
                }
                _status = "该聚落不属于本国(只能选本国的城镇/城堡)";
                Refresh();
            }
            catch { }
        }

        internal void CycleSource(int dir)
        {
            try
            {
                if (_srcList.Count == 0) return;
                _planEdit = false;
                _srcIdx = (_srcIdx + dir) % _srcList.Count;
                if (_srcIdx < 0) _srcIdx += _srcList.Count;
                Refresh();
            }
            catch { }
        }

        internal void RecruitPlan()
        {
            try
            {
                _planEdit = false;
                string msg = DefArmy.Recruit(Source, _plan);
                // v4.85: 募兵成功首次弹"讲解"(守备营/成立军团流程), 带"不再提示"与"关闭"按钮
                if (msg != null && msg.StartsWith("募兵") && !TipState.Disabled("recruit"))
                {
                    _status = msg;
                    ShowRecruitTip(msg);
                    Refresh();
                    return;
                }
                Notify(msg);
            }
            catch (Exception ex) { DLog.Force("军务页募兵异常: " + ex.Message); }
        }

        internal void ConscriptPlan()
        {
            try { _planEdit = false; Notify(DefArmy.Conscript(Source, _plan)); }
            catch (Exception ex) { DLog.Force("军务页征兵异常: " + ex.Message); }
        }

        // v4.85: 军务操作反馈改为弹窗(用户要求); 结果同时写面板底部状态栏
        private void Notify(string msg)
        {
            try
            {
                _status = msg;
                ShowInquiry("军务", msg);
            }
            catch { }
            Refresh();
        }

        internal static void ShowInquiry(string title, string body)
        {
            try
            {
                if (string.IsNullOrEmpty(body)) return;
                if (InformationManager.IsAnyInquiryActive()) return;   // 已有弹窗时不叠加
                InformationManager.ShowInquiry(new InquiryData(title, body, true, false,
                    "关闭", null, null, null, "", 0f, null, null, null), true, false);
            }
            catch (Exception ex) { DLog.Force("弹窗失败: " + ex.Message); }
        }

        private void ShowRecruitTip(string result)
        {
            try
            {
                if (InformationManager.IsAnyInquiryActive()) { _status = result; return; }
                string body = result + "\n\n所募兵员编入该地守备营(城镇/城堡驻军), 不会直接出现在世界地图上。\n"
                    + "让部队上地图: 军务页 → [守备营]页 → 点该行[成立军团], 会弹出数量输入框(键入数字, 至少 100 兵), "
                    + "费用: 建军 2,000 + 置装 500 = 2,500 第纳尔。\n"
                    + "成立后地图上出现「国防军第 N 军团」, 选中后左键点地图即可移动, 或用[驻防/巡逻/补员/解散]指挥。";
                InformationManager.ShowInquiry(new InquiryData("军务 · 募兵完成", body,
                    true, true, "不再提示", "关闭",
                    delegate { TipState.Disable("recruit"); }, null, "", 0f, null, null, null), true, false);
            }
            catch (Exception ex) { DLog.Force("募兵讲解弹窗失败: " + ex.Message); }
        }

        internal void FormLegion()
        {
            try
            {
                _planEdit = false;
                AskLegionCount(Current);
            }
            catch (Exception ex) { DLog.Force("军务页建军异常: " + ex.Message); }
        }

        // v4.86: 成立军团前先让玩家选择数量(默认=计划数量, 可直接键入数字; 面板打开时回车已被拦截)
        internal void AskLegionCount(Settlement s)
        {
            try
            {
                _planEdit = false;
                if (s == null) { Notify("请先选择驻地"); return; }
                int avail = GarrisonAvailable(s);
                int max = avail;   // v4.89: 取消单军上限(能多大多大)
                if (max < 100) { Notify("守备营兵力不足(现有 " + avail + ", 至少 100)"); return; }
                if (InformationManager.IsAnyInquiryActive()) return;
                int def = _plan < 100 ? 100 : (_plan > max ? max : _plan);
                string name = s.Name != null ? s.Name.ToString() : s.StringId;
                string body = name + " 守备营现有 " + avail.ToString("N0") + " 兵。\n"
                    + "请输入要成立军团的兵力(至少 100, 最多 " + max.ToString("N0") + "):\n"
                    + "费用固定: 建军 2,000 + 置装 500 = 2,500 第纳尔";
                InformationManager.ShowTextInquiry(new TextInquiryData("成立军团 · 选择数量", body,
                    true, true, "成立军团", "取消",
                    delegate (string input)
                    {
                        try
                        {
                            string t = (input ?? "").Trim();
                            int n = def;
                            if (t.Length == 0) n = def;
                            else if (!int.TryParse(t, out n) || n <= 0) { Notify("数量无效, 未成立军团"); return; }
                            if (n < 100) n = 100;
                            if (n > max) n = max;
                            _plan = n;
                            Notify(DefArmy.CreateLegion(s, n));
                        }
                        catch (Exception ex) { DLog.Force("成立军团数量确认异常: " + ex.Message); }
                    },
                    null, false, delegate (string input)
                    {
                        if (input == null || input.Length <= 6) return new Tuple<bool, string>(true, "");
                        return new Tuple<bool, string>(false, "最多输入 6 位数字");
                    },
                    "", def.ToString()), true, false);
            }
            catch (Exception ex) { DLog.Force("成立军团数量弹窗失败: " + ex.Message); }
        }

        private static int GarrisonAvailable(Settlement s)
        {
            try
            {
                var target = s;
                if (s != null && s.IsVillage && s.Village != null) target = s.Village.Bound;
                if (target == null) return 0;
                return DefArmy.GarrisonMenOf(target.StringId);   // v4.90: 账面口径(不含原版驻军自带兵)
            }
            catch { return 0; }
        }

        internal void LegionAction(int i, int kind)
        {
            try
            {
                if (i < 0 || i >= LegionRows.Count) return;
                var p = DefArmy.FindParty(LegionRows[i].PartyId);
                if (p == null || !p.IsActive) { Notify("该军团已不存在"); return; }
                if (kind >= 4) Notify(DefArmy.DisbandLegion(p));
                else if (kind == 3) Notify(DefArmy.ReplenishLegion(p, _plan));
                else Notify(DefArmy.SetTask(p, kind));
            }
            catch (Exception ex) { DLog.Force("军务页军团操作异常: " + ex.Message); }
        }

        internal void GarrisonAction(int i)
        {
            try
            {
                if (i < 0 || i >= GarrisonRows.Count) return;
                var s = DefArmy.FindSettlement(GarrisonRows[i].SettlementId);
                AskLegionCount(s);
            }
            catch (Exception ex) { DLog.Force("军务页守备营操作异常: " + ex.Message); }
        }

        internal void AllToHome()
        {
            try { Notify(DefArmy.AllToHome()); }
            catch { }
        }

        internal void RenewConscripts()
        {
            try { Notify(DefArmy.RenewConscripts()); }
            catch { }
        }

        internal void ReleaseConscripts()
        {
            try { Notify(DefArmy.ReleaseConscripts()); }
            catch { }
        }

        internal void TransferConscripts()
        {
            try { Notify(DefArmy.TransferConscripts()); }
            catch { }
        }

        internal void Parade()
        {
            try { Notify(DefArmy.Parade()); }
            catch { }
        }

        internal void ScrollStep(int dir)
        {
            try
            {
                if (_tab != 1) return;
                int max = Math.Max(0, _legionView.Count - LegionPage);
                _scroll += dir;
                if (_scroll < 0) _scroll = 0;
                if (_scroll > max) _scroll = max;
                RebuildLegionRows();
            }
            catch { }
        }

        // ---- 刷新 ----
        internal void Refresh()
        {
            try
            {
                RefreshCandidates();
                int lmen = 0;
                for (int i = 0; i < DefArmy.Legions.Count; i++) lmen += DefArmy.LegionMen(DefArmy.Legions[i]);
                int gmen = 0;
                for (int i = 0; i < DefArmy.Garrisons.Count; i++) gmen += DefArmy.GarrisonMen(DefArmy.Garrisons[i]);
                int total = lmen + gmen;
                _overview = "现役 " + total.ToString("N0") + " 人 ｜ 军团 " + DefArmy.Legions.Count + " 支(" + lmen.ToString("N0") + ")"
                    + " ｜ 守备营 " + DefArmy.Garrisons.Count + " 处(" + gmen.ToString("N0") + ")";
                _menText = total.ToString("N0");
                _legionText = DefArmy.Legions.Count + " 支(" + lmen.ToString("N0") + ")";
                _garrisonText = DefArmy.Garrisons.Count + " 处(" + gmen.ToString("N0") + ")";
                _money = "军饷 " + DefArmy.DailyWageCost().ToString("N0") + "/日 · 可撑 " + DefArmy.DaysAffordable()
                    + " 天 · 欠饷 " + DefArmy.UnpaidDays + " 天 · 今日军损 " + DefArmy.TodayMilLoss + " 人";

                var s = Current;
                var src = Source;
                _srcName = src != null ? (src.Name != null ? src.Name.ToString() : src.StringId) : "—";

                int haveW = 0, haveA = 0, haveL = 0, havePop = 0;
                _settleName = s != null ? (s.Name != null ? s.Name.ToString() : s.StringId) : "—";
                if (src != null)
                {
                    havePop = DefArmy.CommonersOf(src);
                    haveW = Have(src, FeudalGoods.Weapons);
                    haveA = Have(src, FeudalGoods.Armor);
                    haveL = Have(src, FeudalGoods.Leather);
                    _settleInfo = (s != null && s.IsTown ? "城镇" : "城堡") + " · 本城守备营 " + GarrisonAvailable(s) + " 兵"
                        + " · 人口来源: " + (src.IsVillage ? "农民" : "劳工");
                }
                else
                {
                    _settleInfo = "本国暂无城镇/城堡";
                }

                // 计划数量 + 实时"需要 / 拥有"
                int n2 = _plan;
                NeedEquip(n2, out int needW, out int needA, out int needL);
                int needGold = NeedGoldFor(n2);
                int needGoldConscript = n2 * 5;
                _planText = _planEdit ? (_plan + "_") : _plan.ToString("N0");
                _needRecruitText = "国库 " + needGold.ToString("N0") + " · 人口 " + n2.ToString("N0")
                    + " · 武器 " + needW + " · 盔甲 " + needA + " · 皮革 " + needL;
                _needConscriptText = "国库 " + needGoldConscript.ToString("N0") + " · 人口 " + n2.ToString("N0");
                int haveGold = 0;
                try { haveGold = EconomyWorld.Treasury.Gold; } catch { }
                _haveGoldPop = "国库 " + haveGold.ToString("N0") + " · 源人口 " + havePop.ToString("N0") + " 人";
                _haveEquip = "武器 " + haveW + " · 盔甲 " + haveA + " · 皮革 " + haveL;

                if (havePop < n2) { _planVerdict = "⚠ 人口不足 " + (n2 - havePop).ToString("N0") + " 人"; _planVerdictColor = "#FF4B4BFF"; _planVerdictBg = "#C96A5A26"; }
                else if (haveGold < needGold) { _planVerdict = "⚠ 国库不足 " + (needGold - haveGold).ToString("N0"); _planVerdictColor = "#FF4B4BFF"; _planVerdictBg = "#C96A5A26"; }
                else if (haveW < needW || haveA < needA || haveL < needL) { _planVerdict = "装备不足 → 低配入列(士气-10%)"; _planVerdictColor = "#E8C33AFF"; _planVerdictBg = "#E8C33A26"; }
                else { _planVerdict = "✓ 资源充足, 可执行"; _planVerdictColor = "#7FBF6AFF"; _planVerdictBg = "#7FBF6A26"; }

                _recruitBtn = "募兵 " + n2.ToString("N0") + " · " + needGold.ToString("N0") + "金";
                _conscriptBtn = "征兵 " + n2.ToString("N0") + " · " + needGoldConscript.ToString("N0") + "金";

                int lv = Politics.LawLevel(2);
                int pool = DefArmy.ConscriptPool(src);
                int left = DefArmy.NearestConscriptExpiry();
                _conscriptText = "征兵法令: " + (lv > 0 ? Politics.LawLevels[lv] : "未立(需立法)")
                    + " · 征召池 " + pool.ToString("N0") + " 人 · 已征 " + DefArmy.ConscriptedMen().ToString("N0")
                    + " / 上限 " + DefArmy.ConscriptCap().ToString("N0")
                    + (left >= 0 ? " · 最近到期 " + left + " 天" : "");

                int garrAvail = GarrisonAvailable(s);
                _formLegionBtn = "成立军团 " + Math.Min(_plan, Math.Max(0, garrAvail)).ToString("N0") + " 兵";

                _legionHelp = "野战军团=可移动的国防军。任务: 驻防(回驻地) · 巡逻 · 补员(从驻地守备营补兵) · 解散";
                _garrisonHelp = "守备营=驻扎在城镇驻军里的国防军。点[成立军团]会弹出数量输入框(键入数字, 至少 100 兵), 成立费 2000+袍服 500";

                // 页签显示与高亮
                _showRecruit = _tab == 0;
                _showLegion = _tab == 1;
                _showGarrison = _tab == 2;
                _tabRecruitColor = _tab == 0 ? "#E8C33AFF" : "#8A8070FF";
                _tabLegionColor = _tab == 1 ? "#E8C33AFF" : "#8A8070FF";
                _tabGarrisonColor = _tab == 2 ? "#E8C33AFF" : "#8A8070FF";
                _tabLineRecruit = _tab == 0;
                _tabLineLegion = _tab == 1;
                _tabLineGarrison = _tab == 2;
                // 执行按钮状态色: 资源不足时变红
                bool canRecruit = havePop >= n2 && haveGold >= needGold;
                bool canConscript = havePop >= n2 && haveGold >= needGoldConscript;
                _recruitBtnColor = canRecruit ? "#7FBF6AFF" : "#C96A5AFF";
                _conscriptBtnColor = canConscript ? "#7FBF6AFF" : "#C96A5AFF";

                RebuildLegionRows();
                RebuildGarrisonRows();
                _legionEmpty = LegionRows.Count == 0;
                _garrisonEmpty = GarrisonRows.Count == 0;

                OnPropertyChangedWithValue(_overview, "OverviewText");
                OnPropertyChangedWithValue(_money, "MoneyText");
                OnPropertyChangedWithValue(_settleName, "SettleName");
                OnPropertyChangedWithValue(_srcName, "SrcName");
                OnPropertyChangedWithValue(_settleInfo, "SettleInfo");
                OnPropertyChangedWithValue(_planText, "PlanText");
                OnPropertyChangedWithValue(_needRecruitText, "NeedRecruitText");
                OnPropertyChangedWithValue(_needConscriptText, "NeedConscriptText");
                OnPropertyChangedWithValue(_haveGoldPop, "HaveGoldPop");
                OnPropertyChangedWithValue(_haveEquip, "HaveEquip");
                OnPropertyChangedWithValue(_planVerdict, "PlanVerdictText");
                OnPropertyChangedWithValue(_planVerdictColor, "PlanVerdictColor");
                OnPropertyChangedWithValue(_planVerdictBg, "PlanVerdictBg");
                OnPropertyChangedWithValue(_menText, "MenText");
                OnPropertyChangedWithValue(_legionText, "LegionText");
                OnPropertyChangedWithValue(_garrisonText, "GarrisonText");
                OnPropertyChangedWithValue(_conscriptText, "ConscriptText");
                OnPropertyChangedWithValue(_recruitBtn, "RecruitBtnText");
                OnPropertyChangedWithValue(_conscriptBtn, "ConscriptBtnText");
                OnPropertyChangedWithValue(_formLegionBtn, "FormLegionBtnText");
                OnPropertyChangedWithValue(_status, "StatusText");
                OnPropertyChangedWithValue(_legionHelp, "LegionHelp");
                OnPropertyChangedWithValue(_garrisonHelp, "GarrisonHelp");
                OnPropertyChangedWithValue(_showRecruit, "ShowRecruit");
                OnPropertyChangedWithValue(_showLegion, "ShowLegion");
                OnPropertyChangedWithValue(_showGarrison, "ShowGarrison");
                OnPropertyChangedWithValue(_tabRecruitColor, "TabRecruitColor");
                OnPropertyChangedWithValue(_tabLegionColor, "TabLegionColor");
                OnPropertyChangedWithValue(_tabGarrisonColor, "TabGarrisonColor");
                OnPropertyChangedWithValue(_tabLineRecruit, "TabLineRecruit");
                OnPropertyChangedWithValue(_tabLineLegion, "TabLineLegion");
                OnPropertyChangedWithValue(_tabLineGarrison, "TabLineGarrison");
                OnPropertyChangedWithValue(_recruitBtnColor, "RecruitBtnColor");
                OnPropertyChangedWithValue(_conscriptBtnColor, "ConscriptBtnColor");
                OnPropertyChangedWithValue(_legionEmpty, "LegionEmptyVisible");
                OnPropertyChangedWithValue(_garrisonEmpty, "GarrisonEmptyVisible");
            }
            catch (Exception ex) { DLog.Force("军务页刷新异常: " + ex.Message); }
        }

        private static void NeedEquip(int n, out int weapons, out int armor, out int leather)
        {
            weapons = Math.Max(1, (int)Math.Round(n / 100f));
            armor = Math.Max(1, (int)Math.Round(n / 200f));
            leather = armor;
        }

        internal static int NeedGoldFor(int n)
        {
            int lordSeat = Politics.SeatHeld(3) ? 1 : 0;
            return (int)Math.Round(n * 20 * (lordSeat == 1 ? 0.9f : 1f));
        }

        private void RefreshCandidates()
        {
            try
            {
                string keep = Current != null ? Current.StringId : null;
                _cands.Clear();
                _cands.AddRange(DefArmy.CandidateSettlements());
                _candIdx = 0;
                if (keep != null)
                {
                    for (int i = 0; i < _cands.Count; i++)
                        if (_cands[i].StringId == keep) { _candIdx = i; break; }
                }
                string keepSrc = Source != null ? Source.StringId : null;
                _srcList.Clear();
                _srcList.AddRange(DefArmy.SourcesOf(Current));
                _srcIdx = 0;
                if (keepSrc != null)
                {
                    for (int i = 0; i < _srcList.Count; i++)
                        if (_srcList[i].StringId == keepSrc) { _srcIdx = i; break; }
                }
                // 诊断: 可选聚落数量变化时记录(排查"只有一座城")
                if (_cands.Count != _lastCandCount)
                {
                    _lastCandCount = _cands.Count;
                    var names = new System.Text.StringBuilder();
                    for (int i = 0; i < _cands.Count && i < 12; i++) names.Append(_cands[i].Name).Append(' ');
                    DLog.Force("军务: 可选聚落=" + _cands.Count + " [" + names.ToString().TrimEnd() + "] 王国="
                        + (DefArmy.OurKingdom != null ? DefArmy.OurKingdom.Name.ToString() : "?"));
                }
            }
            catch { }
        }

        private int _lastCandCount = -1;

        private static int Have(Settlement s, string goodId)
        {
            try
            {
                var item = FeudalGoods.Item(goodId);
                if (s == null || item == null || s.ItemRoster == null) return 0;
                return s.ItemRoster.GetItemNumber(item);
            }
            catch { return 0; }
        }

        private void RebuildLegionRows()
        {
            try
            {
                LegionRows.Clear();
                _legionView.Clear();
                for (int i = 0; i < DefArmy.Legions.Count; i++) _legionView.Add(DefArmy.Legions[i]);
                int max = Math.Max(0, _legionView.Count - LegionPage);
                if (_scroll > max) _scroll = max;
                for (int i = _scroll; i < _legionView.Count && LegionRows.Count < LegionPage; i++)
                {
                    var lg = _legionView[i];
                    LegionRows.Add(new LegionRowVM(lg, DefArmy.LegionParty(lg)));
                }
            }
            catch { }
        }

        private void RebuildGarrisonRows()
        {
            try
            {
                DefArmy.NormalizeGarrisons();   // v4.86: 同一城市只显示一行守备营(重复募兵合并)
                GarrisonRows.Clear();
                for (int i = 0; i < DefArmy.Garrisons.Count && GarrisonRows.Count < GarrisonPage; i++)
                    GarrisonRows.Add(new GarrisonRowVM(DefArmy.Garrisons[i]));
            }
            catch { }
        }
    }
}
