using System;
using System.Collections.Generic;
using TaleWorlds.Library;

namespace FeudalInternalAffairs
{
    // 左侧文化行(v4.186 文化页, 文档 24.13)
    public class CulRowVM : ViewModel
    {
        private string _name, _pop, _share, _law, _lawColor, _color, _plate;
        private bool _sel;
        internal int Idx;

        internal CulRowVM(int idx, CultureSystem.CultureRow r, bool sel)
        {
            Idx = idx;
            _name = r.Name + (r.IsRuling ? " (主体)" : "");
            _pop = ((int)Math.Round(r.Pop)).ToString() + " 人";
            _share = (r.Share * 100f).ToString("F1") + "%";
            _law = r.LawName;
            _lawColor = r.LawId == "assimilation" ? "#FFC46BFF" : (r.LawId == "multicultural" ? "#8FD8E8FF" : (r.LawId == "state_church" ? "#E8C33AFF" : "#D8C9A0FF"));
            _color = r.Color;
            _sel = sel;
            _plate = sel ? "fia_ui_row_sel" : "fia_ui_row";
        }

        [DataSourceProperty] public string Name { get { return _name; } }
        [DataSourceProperty] public string PopText { get { return _pop; } }
        [DataSourceProperty] public string ShareText { get { return _share; } }
        [DataSourceProperty] public string LawText { get { return _law; } }
        [DataSourceProperty] public string LawColor { get { return _lawColor; } }
        [DataSourceProperty] public string Swatch { get { return _color; } }
        [DataSourceProperty] public string Plate { get { return _plate; } }
        [DataSourceProperty] public bool IsSel { get { return _sel; } }
        [DataSourceProperty] public float IdxOffset { get { return Idx * 48f; } }
    }

    // 右侧法律选项
    public class CultureLawOptionVM : ViewModel
    {
        internal int LawIdx;
        private string _name, _effect, _desc, _plate, _frame;
        private bool _cur;

        internal CultureLawOptionVM(int lawIdx, CultureLawDef d, bool cur)
        {
            LawIdx = lawIdx;
            _name = d.Name + (cur ? "  (当前)" : "");
            _desc = d.Desc;
            _effect = d.Effect;
            _cur = cur;
            _plate = cur ? "#12414DE6" : "#1E1912E6";
            _frame = cur ? "#4FD8E8AA" : "#C9A22766";
        }

        [DataSourceProperty] public string Name { get { return _name; } }
        [DataSourceProperty] public string Desc { get { return _desc; } }
        [DataSourceProperty] public string Effect { get { return _effect; } }
        [DataSourceProperty] public string PlateColor { get { return _plate; } }
        [DataSourceProperty] public string FrameColor { get { return _frame; } }
        [DataSourceProperty] public bool IsCurrent { get { return _cur; } }
        [DataSourceProperty] public float IdxOffset { get { return LawIdx * 104f; } }
    }

    // 文化页 VM(全屏面板, 与科技页/国策页同一套外观)
    public class CultureVM : PanelVMBase
    {
        private readonly Action _onClose;
        internal int Sel;
        private string _selName = "未选择", _selLine1 = "", _selLine2 = "", _selLine3 = "", _selLaw = "", _selLawEffect = "", _cdText = "";
        private string _top1 = "", _top2 = "", _top3 = "", _tip = "";

        public CultureVM(Action onClose)
        {
            _onClose = onClose;
            Rows = new MBBindingList<CulRowVM>();
            Laws = new MBBindingList<CultureLawOptionVM>();
            Refresh();
        }

        public MBBindingList<CulRowVM> Rows { get; private set; }
        public MBBindingList<CultureLawOptionVM> Laws { get; private set; }

        [DataSourceProperty] public float PanelWidth { get { try { return PanelScreen.FullWidth(); } catch { return 1856f; } } }
        [DataSourceProperty] public float ContentPad { get { try { return Math.Max(0f, (PanelScreen.FullWidth() - 1700f) / 2f); } catch { return 0f; } } }
        [DataSourceProperty] public string SelName { get { return _selName; } }
        [DataSourceProperty] public string SelLine1 { get { return _selLine1; } }
        [DataSourceProperty] public string SelLine2 { get { return _selLine2; } }
        [DataSourceProperty] public string SelLine3 { get { return _selLine3; } }
        [DataSourceProperty] public string SelLaw { get { return _selLaw; } }
        [DataSourceProperty] public string SelLawEffect { get { return _selLawEffect; } }
        [DataSourceProperty] public string CooldownText { get { return _cdText; } }
        [DataSourceProperty] public string Top1 { get { return _top1; } }
        [DataSourceProperty] public string Top2 { get { return _top2; } }
        [DataSourceProperty] public string Top3 { get { return _top3; } }
        [DataSourceProperty] public string TipText { get { return _tip; } }

        public void ExecuteClose() { if (_onClose != null) _onClose(); }

        internal void Select(int idx)
        {
            Sel = idx;
            Refresh();
        }

        internal void PickLaw(int lawIdx)
        {
            try
            {
                var rows = CultureSystem.Snapshot();
                if (Sel < 0 || Sel >= rows.Count) return;
                if (lawIdx < 0 || lawIdx >= CultureSystem.Laws.Count) return;
                string msg = CultureSystem.SetLaw(rows[Sel].Id, CultureSystem.Laws[lawIdx].Id);
                try { MapSelection.Message(msg); } catch { }
                Refresh();
            }
            catch (Exception ex) { DLog.Force("文化改法失败: " + ex.Message); }
        }

        internal void Refresh()
        {
            try
            {
                var rows = CultureSystem.Snapshot();
                Rows.Clear();
                for (int i = 0; i < rows.Count; i++) Rows.Add(new CulRowVM(i, rows[i], i == Sel));

                var pk = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                string ruling = pk != null && pk.Culture != null ? CultureSystem.NameOf(pk.Culture.StringId) : "?";
                _top1 = "主体文化: " + ruling;
                _top2 = "境内文化: " + rows.Count + " 种";
                float church = 0f;
                try { church = InterestGroups.SatOf(3); } catch { }
                _top3 = "教会态度: " + ((int)church);
                OnPropertyChangedWithValue(_top1, "Top1");
                OnPropertyChangedWithValue(_top2, "Top2");
                OnPropertyChangedWithValue(_top3, "Top3");

                if (rows.Count > 0)
                {
                    if (Sel >= rows.Count) Sel = 0;
                    var r = rows[Sel];
                    _selName = r.Name + (r.IsRuling ? "  ·  主体文化" : "");
                    _selLine1 = "人口 " + ((int)Math.Round(r.Pop)) + " 人  ·  占比 " + (r.Share * 100f).ToString("F1") + "%";
                    _selLine2 = "生活水平 " + r.Sol.ToString("F1") + "  ·  忠诚 " + (r.Loyalty * 100f).ToString("F0") + "%  ·  激进 " + (r.Radical * 100f).ToString("F0") + "%";
                    _selLine3 = "同化倍率 ×" + CultureSystem.AssimOf(r.Id).ToString("F2") + "  ·  迁移吸引 " + (CultureSystem.AttractOf(r.Id) >= 0f ? "+" : "") + CultureSystem.AttractOf(r.Id).ToString("F0") + "  ·  税收 ×" + (1f + CultureSystem.TaxOf(r.Id)).ToString("F2");
                    var def = CultureSystem.Def(r.LawId);
                    _selLaw = "当前文化法律: " + def.Name;
                    _selLawEffect = def.Effect;
                    _cdText = CultureSystem.CooldownTextOf(r.Id);
                    Laws.Clear();
                    for (int i = 0; i < CultureSystem.Laws.Count; i++)
                        Laws.Add(new CultureLawOptionVM(i, CultureSystem.Laws[i], CultureSystem.Laws[i].Id == r.LawId));
                }
                else
                {
                    _selName = "境内暂无人口数据";
                    _selLine1 = ""; _selLine2 = ""; _selLine3 = "";
                    _selLaw = ""; _selLawEffect = ""; _cdText = "";
                    Laws.Clear();
                }
                OnPropertyChangedWithValue(_selName, "SelName");
                OnPropertyChangedWithValue(_selLine1, "SelLine1");
                OnPropertyChangedWithValue(_selLine2, "SelLine2");
                OnPropertyChangedWithValue(_selLine3, "SelLine3");
                OnPropertyChangedWithValue(_selLaw, "SelLaw");
                OnPropertyChangedWithValue(_selLawEffect, "SelLawEffect");
                OnPropertyChangedWithValue(_cdText, "CooldownText");
                _tip = "左键选文化 · 右侧点法律立即生效(每文化 90 日冷却) · 效果作用于同化/迁移/满意度/税收/忠诚";
                OnPropertyChangedWithValue(_tip, "TipText");
            }
            catch (Exception ex) { DLog.Force("文化页刷新失败: " + ex.Message); }
        }
    }
}
