using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace FeudalInternalAffairs
{
    // 方案里的一行
    public class BuildPlanRowVM : ViewModel
    {
        private readonly Action<BuildPlanRowVM> _plus, _minus, _remove;

        internal BuildPlanRowVM(PlanItem item, Action<BuildPlanRowVM> plus, Action<BuildPlanRowVM> minus, Action<BuildPlanRowVM> remove)
        {
            Item = item;
            _plus = plus; _minus = minus; _remove = remove;
        }

        internal PlanItem Item { get; private set; }
        internal string DefId { get { return Item != null ? Item.DefId : null; } }

        [DataSourceProperty]
        public string Name { get { return Item != null && Item.Def != null ? Item.Def.Name : "?"; } }

        [DataSourceProperty]
        public string Icon { get { return Item != null && Item.Def != null ? Item.Def.Sprite : ""; } }

        [DataSourceProperty]
        public string CategoryText { get { return Item != null && Item.Def != null ? BuildDefs.CategoryName(Item.Def.Cat) : ""; } }

        [DataSourceProperty]
        public string CountText { get { return Item != null ? ("×" + Item.Count) : ""; } }

        public void ExecutePlus() { try { if (_plus != null) _plus(this); } catch { } }
        public void ExecuteMinus() { try { if (_minus != null) _minus(this); } catch { } }
        public void ExecuteRemove() { try { if (_remove != null) _remove(this); } catch { } }
    }

    // 批量建造面板 VM
    public class BuildPanelVM : PanelVMBase
    {
        private readonly Action _onClose;

        internal BuildPanelVM(Action onClose)
        {
            _onClose = onClose;
            Rows = new MBBindingList<BuildPlanRowVM>();
            Refresh();
        }

        [DataSourceProperty]
        public MBBindingList<BuildPlanRowVM> Rows { get; private set; }

        [DataSourceProperty]
        public string Title { get { return "批量建造"; } }

        [DataSourceProperty]
        public string Hint { get { return "右键点击领地 = 把方案排队到该定居点 · Ctrl+Z 撤销 · ESC 退出"; } }

        [DataSourceProperty]
        public string PlanSummary
        {
            get
            {
                return BatchBuild.PlanTotal > 0
                    ? ("方案: 共 " + BatchBuild.PlanTotal + " 项")
                    : "方案是空的 —— 点「添加建筑」开始";
            }
        }

        [DataSourceProperty]
        public string Preset1 { get { return BatchBuild.PresetText(0); } }

        [DataSourceProperty]
        public string Preset2 { get { return BatchBuild.PresetText(1); } }

        [DataSourceProperty]
        public string Preset3 { get { return BatchBuild.PresetText(2); } }

        public void ExecuteAdd()
        {
            try
            {
                var options = new List<InquiryElement>();
                foreach (var def in BuildDefs.All)
                {
                    if (def == null) continue;
                    options.Add(new InquiryElement(def.Id, BuildDefs.CategoryName(def.Cat) + " · " + def.Name, null, true, null));
                }
                MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                    "添加建筑到方案",
                    "可一次选多个(方案里的建筑会在右键领地时尽量排队; 地点不符的会自动跳过)",
                    options, true, 0, options.Count, "加入方案", "取消",
                    OnPicked, null, null, false));
            }
            catch (Exception ex) { DLog.Force("添加建筑失败: " + ex.Message); }
        }

        private void OnPicked(List<InquiryElement> picked)
        {
            try
            {
                if (picked == null) return;
                foreach (var e in picked)
                {
                    var id = e != null ? e.Identifier as string : null;
                    if (id != null) BatchBuild.Add(id);
                }
                Refresh();
            }
            catch (Exception ex) { DLog.Force("加入方案失败: " + ex.Message); }
        }

        public void ExecutePreset1() { try { BatchBuild.ClickPreset(0); } catch { } }
        public void ExecutePreset2() { try { BatchBuild.ClickPreset(1); } catch { } }
        public void ExecutePreset3() { try { BatchBuild.ClickPreset(2); } catch { } }
        public void ExecuteUndo() { try { BatchBuild.Undo(); } catch { } }
        public void ExecuteClear() { try { BatchBuild.Clear(); } catch { } }

        public void ExecuteClose()
        {
            try
            {
                if (_onClose != null) _onClose();
                else BatchBuild.Exit();
            }
            catch (Exception ex) { DLog.Force("关闭建造面板失败: " + ex.Message); }
        }

        internal void Refresh()
        {
            try
            {
                Rows.Clear();
                foreach (var p in BatchBuild.Plan)
                {
                    if (p == null) continue;
                    Rows.Add(new BuildPlanRowVM(p, OnPlus, OnMinus, OnRemove));
                }
                OnPropertyChangedWithValue(PlanSummary, "PlanSummary");
                OnPropertyChangedWithValue(Preset1, "Preset1");
                OnPropertyChangedWithValue(Preset2, "Preset2");
                OnPropertyChangedWithValue(Preset3, "Preset3");
            }
            catch { }
        }

        private void OnPlus(BuildPlanRowVM row) { try { if (row != null) BatchBuild.Change(row.DefId, 1); } catch { } }
        private void OnMinus(BuildPlanRowVM row) { try { if (row != null) BatchBuild.Change(row.DefId, -1); } catch { } }
        private void OnRemove(BuildPlanRowVM row) { try { if (row != null) BatchBuild.Remove(row.DefId); } catch { } }
    }
}
