using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;

namespace FeudalInternalAffairs
{
    // 军务统计卡(v4.199; 内核口径: 组织度/士气/装备满足率/军饷)
    public class MilStatVM : ViewModel
    {
        private readonly string _icon, _name, _value, _color;
        internal MilStatVM(string icon, string name, string value, string color)
        {
            _icon = icon; _name = name; _value = value; _color = color;
        }
        [DataSourceProperty] public string Icon { get { return _icon; } }
        [DataSourceProperty] public string Name { get { return _name; } }
        [DataSourceProperty] public string Value { get { return _value; } }
        [DataSourceProperty] public string ValueColor { get { return _color; } }
    }

    // 兵种图鉴卡(内核 14 兵种 Equipment.Units; 解锁按装备目录可领用判定)
    public class MilUnitVM : ViewModel
    {
        private readonly string _icon, _name, _status, _color, _plate;
        internal MilUnitVM(string icon, string name, bool unlocked, string need)
        {
            _icon = icon; _name = name;
            string needShort = MilIcons.Clip(need, 8);
            _status = unlocked ? "已解锁" : ("需 " + (string.IsNullOrEmpty(needShort) ? "后续科技" : needShort));
            _color = unlocked ? "#7FBF6AFF" : "#8A8070FF";
            _plate = unlocked ? "#1E1912E6" : "#141210D9";
        }
        [DataSourceProperty] public string Icon { get { return _icon; } }
        [DataSourceProperty] public string Name { get { return _name; } }
        [DataSourceProperty] public string Status { get { return _status; } }
        [DataSourceProperty] public string StatusColor { get { return _color; } }
        [DataSourceProperty] public string Plate { get { return _plate; } }
    }

    // 军务总览页 VM(加宽 860; 12 项军务统计 + 兵种图鉴 3 列)
    public class MilitaryVM : PanelVMBase
    {
        private readonly Action _onClose;
        private string _title = "军务";
        private string _sub = "";
        private string _status = "";

        public MilitaryVM(Action onClose)
        {
            _onClose = onClose;
            StatsCol0 = new MBBindingList<MilStatVM>();
            StatsCol1 = new MBBindingList<MilStatVM>();
            StatsCol2 = new MBBindingList<MilStatVM>();
            UnitsLeft = new MBBindingList<MilUnitVM>();
            UnitsMid = new MBBindingList<MilUnitVM>();
            UnitsRight = new MBBindingList<MilUnitVM>();
            Refresh();
        }

        public MBBindingList<MilStatVM> StatsCol0 { get; private set; }
        public MBBindingList<MilStatVM> StatsCol1 { get; private set; }
        public MBBindingList<MilStatVM> StatsCol2 { get; private set; }
        public MBBindingList<MilUnitVM> UnitsLeft { get; private set; }
        public MBBindingList<MilUnitVM> UnitsMid { get; private set; }
        public MBBindingList<MilUnitVM> UnitsRight { get; private set; }

        private void AddStat(MilStatVM vm)
        {
            if (StatsCol0.Count < 4) StatsCol0.Add(vm);
            else if (StatsCol1.Count < 4) StatsCol1.Add(vm);
            else StatsCol2.Add(vm);
        }

        [DataSourceProperty] public string Title { get { return _title; } }
        [DataSourceProperty] public string Sub { get { return _sub; } }
        [DataSourceProperty] public string StatusText { get { return _status; } }

        public void ExecuteClose() { if (_onClose != null) _onClose(); }

        internal void Refresh()
        {
            try
            {
                StatsCol0.Clear();
                StatsCol1.Clear();
                StatsCol2.Clear();
                UnitsLeft.Clear();
                UnitsMid.Clear();
                UnitsRight.Clear();
                var pk = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                int legionMen = 0, legionCnt = 0;
                float orgSum = 0f, morSum = 0f, fillSum = 0f;
                long profSum = 0; int profCnt = 0;
                int garrisonMen = 0, garrisonCnt = 0;
                try
                {
                    for (int i = 0; i < DefArmy.Legions.Count; i++)
                    {
                        var lg = DefArmy.Legions[i];
                        var p = DefArmy.LegionParty(lg);
                        if (lg == null) continue;
                        if (p != null)
                        {
                            legionCnt++;
                            legionMen += DefArmy.RegularsOf(p);
                        }
                        try
                        {
                            string key = ArmyDoctrine.LegionKey(lg);
                            orgSum += ArmyDoctrine.OrgOf2(key);
                            morSum += ArmyDoctrine.MoraleOf2(key);
                            fillSum += DefArmy.EquipFillOf(lg);
                        }
                        catch { }
                        if (p != null)
                        {
                            try
                            {
                                Dictionary<string, int> men; Dictionary<string, float> profs;
                                Soldiers.Aggregate(p, out men, out profs);
                                foreach (var kv in men)
                                {
                                    float pf;
                                    if (!profs.TryGetValue(kv.Key, out pf)) pf = 0f;
                                    profSum += (long)(pf * kv.Value);
                                    profCnt += kv.Value;
                                }
                            }
                            catch { }
                        }
                    }
                    try
                    {
                        for (int i = 0; i < DefArmy.Garrisons.Count; i++)
                        {
                            var g = DefArmy.Garrisons[i];
                            if (g == null) continue;
                            garrisonCnt++;
                            garrisonMen += g.Placed;   // DefGarrison.Placed = 已布防人数
                        }
                    }
                    catch { }
                }
                catch { }
                int org = legionCnt > 0 ? (int)Math.Round(orgSum / legionCnt) : 0;
                int morale = legionCnt > 0 ? (int)Math.Round(morSum / legionCnt) : 0;
                int equipFill = legionCnt > 0 ? (int)Math.Round(fillSum / legionCnt * 100f) : 0;
                int prof = profCnt > 0 ? (int)(profSum / profCnt) : 0;
                float attrition = Math.Max(0f, 100f - morale);
                int wars = 0;
                try { if (pk != null) foreach (var k in Kingdom.All) if (k != null && !k.IsEliminated && pk.IsAtWarWith(k)) wars++; } catch { }
                float food = 0f;
                try { if (pk != null) foreach (var s in pk.Settlements) if (s != null && s.IsTown && s.Town != null) food += s.Town.FoodStocks; } catch { }
                int pop = 0;
                try { pop = (int)Pops.TotalPopulation(); } catch { }
                float mobPct = pop > 0 ? (legionMen + garrisonMen) * 100f / pop : 0f;

                AddStat(new MilStatVM(MilIcons.ByBranch(0), "组织度", org + " / 100", org >= 70 ? "#7FBF6AFF" : (org >= 40 ? "#E8C33AFF" : "#D96A5AFF")));
                AddStat(new MilStatVM(MilIcons.ByBranch(3), "士气", morale + " / 100", morale >= 60 ? "#7FBF6AFF" : (morale >= 35 ? "#E8C33AFF" : "#D96A5AFF")));
                AddStat(new MilStatVM("fia_unit_militia", "人力(现役)", legionMen.ToString("N0") + " 人", "#E8DCC0FF"));
                AddStat(new MilStatVM("fia_unit_skirmisher", "损耗", ((int)attrition) + "%", attrition <= 30f ? "#7FBF6AFF" : (attrition <= 55f ? "#E8C33AFF" : "#D96A5AFF")));
                AddStat(new MilStatVM("fia_unit_cuirassier", "战争支持", wars > 0 ? (wars + " 国交战") : "无战事", wars > 0 ? "#E8A33AFF" : "#7FBF6AFF"));
                AddStat(new MilStatVM("fia_settle_town", "占领/驻防", garrisonCnt + " 处", "#D8C9A0FF"));
                AddStat(new MilStatVM(MilIcons.ByBranch(2), "动员率", mobPct.ToString("F1") + "%", mobPct <= 3f ? "#7FBF6AFF" : (mobPct <= 6f ? "#E8C33AFF" : "#D96A5AFF")));
                AddStat(new MilStatVM("fia_unit_dragoon", "可复员", ((int)(garrisonMen * 0.2f)).ToString("N0") + " 人", "#8A8070FF"));
                AddStat(new MilStatVM("fia_goods_grain", "补给(粮秣)", ((int)food).ToString("N0"), food >= 500f ? "#7FBF6AFF" : "#E8C33AFF"));
                AddStat(new MilStatVM(MilIcons.EquipIconOf("chainmail"), "装备满足率", equipFill + "%", equipFill >= 90 ? "#7FBF6AFF" : (equipFill >= 60 ? "#E8C33AFF" : "#D96A5AFF")));
                AddStat(new MilStatVM("fia_goods_gold", "军饷 欠" + DefArmy.UnpaidDays + "天", DefArmy.DailyWageCost().ToString("N0") + "/日", DefArmy.UnpaidDays > 0 ? "#D96A5AFF" : "#E8C33AFF"));
                AddStat(new MilStatVM("fia_unit_engineer", "平均熟练", profCnt > 0 ? (prof + " / 100") : "—", prof >= 60 ? "#7FBF6AFF" : "#D8C9A0FF"));

                // 军事科技完成数 -> 兵种解锁门槛
                int milDone = 0, milTotal = 0;
                try
                {
                    for (int i = 0; i < Research.All.Count; i++)
                    {
                        var t = Research.All[i];
                        if (t == null || t.Tree != 1) continue;
                        milTotal++;
                        if (Research.IsDone(t.Id)) milDone++;
                    }
                }
                catch { }
                _sub = "军事科技 " + milDone + " / " + milTotal + "  ·  兵力 " + (legionMen + garrisonMen).ToString("N0") + " 人";
                OnPropertyChangedWithValue(_sub, "Sub");
                OnPropertyChangedWithValue(_title, "Title");
                OnPropertyChangedWithValue(_status, "StatusText");

                // 内核 14 兵种: 图标 + 解锁(按装备目录可领用判定)
                for (int i = 0; i < Equipment.Units.Count; i++)
                {
                    var u = Equipment.Units[i];
                    if (u == null) continue;
                    bool ok; string need;
                    UnitUnlock(i, out ok, out need);
                    var vm = new MilUnitVM(MilIcons.ByUnit(i), u.Name, ok, need);
                    if (UnitsLeft.Count <= UnitsMid.Count && UnitsLeft.Count <= UnitsRight.Count) UnitsLeft.Add(vm);
                    else if (UnitsMid.Count <= UnitsRight.Count) UnitsMid.Add(vm);
                    else UnitsRight.Add(vm);
                }
            }
            catch (Exception ex) { DLog.Force("军务总览页刷新失败: " + ex.Message); }
        }

        // 解锁判定: 该兵种每条装备需求都能从目录取到且科技已解锁
        private static void UnitUnlock(int idx, out bool ok, out string need)
        {
            ok = true; need = "";
            try
            {
                var u = Equipment.Unit(idx);
                if (u == null || u.Req == null) return;
                for (int i = 0; i < u.Req.Length; i++)
                {
                    var pick = Equipment.PickForTag(u.Req[i].Tag, 6);
                    if (pick == null) { ok = false; if (need.Length == 0) need = "无可用装备"; continue; }
                    if (!Equipment.IsUnlocked(pick)) { ok = false; if (need.Length == 0) need = pick.Tech; }
                }
            }
            catch { ok = false; need = "数据异常"; }
        }
    }
}
