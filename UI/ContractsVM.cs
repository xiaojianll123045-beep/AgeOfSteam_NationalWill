using System;
using System.Collections.Generic;
using TaleWorlds.Library;

namespace FeudalInternalAffairs
{
    // 封建契约侧栏 · 领主行(v4.141: 独立侧边栏 UI, 用户要求)
    public class ContractRowVM : ViewModel
    {
        internal readonly string ClanId;
        private readonly string _name, _clout, _angerText, _angerColor, _stateText, _stateColor;
        private readonly string[] _taxBg = new string[4];
        private readonly string[] _taxFg = new string[4];
        private readonly string[] _levyBg = new string[4];
        private readonly string[] _levyFg = new string[4];

        internal ContractRowVM(string clanId, LordProfile lp)
        {
            ClanId = clanId;
            _name = lp != null && !string.IsNullOrEmpty(lp.Name) ? lp.Name : clanId;
            _clout = lp != null ? ("势力 " + lp.Clout) : "";
            int anger = lp != null ? lp.Anger : 0;
            _angerText = "愤怒 " + anger;
            _angerColor = anger >= 75 ? "#FF4B4BFF" : (anger >= 40 ? "#E8C33AFF" : "#7FBF6AFF");
            var c = FeudalContracts.Of(clanId);
            for (int i = 0; i < 4; i++)
            {
                bool curTax = i == c.Tax;
                _taxBg[i] = curTax ? "#C9A227FF" : (i < c.Tax ? "#5A5142FF" : "#FFFFFF10");
                _taxFg[i] = curTax ? "#1A1408FF" : (i < c.Tax ? "#B8AC90FF" : "#8A8070FF");
                bool curLevy = i == c.Levy;
                _levyBg[i] = curLevy ? "#C9A227FF" : (i < c.Levy ? "#5A5142FF" : "#FFFFFF10");
                _levyFg[i] = curLevy ? "#1A1408FF" : (i < c.Levy ? "#B8AC90FF" : "#8A8070FF");
            }
            int cd = FeudalContracts.CooldownLeft(clanId);
            if (cd > 0)
            {
                _stateText = "冷却 " + cd + " 天";
                _stateColor = "#8A8070FF";
            }
            else
            {
                _stateText = "就绪";
                _stateColor = "#7FBF6AFF";
            }
        }

        [DataSourceProperty] public string Name { get { return _name; } }
        [DataSourceProperty] public string CloutText { get { return _clout; } }
        [DataSourceProperty] public string AngerText { get { return _angerText; } }
        [DataSourceProperty] public string AngerColor { get { return _angerColor; } }
        [DataSourceProperty] public string StateText { get { return _stateText; } }
        [DataSourceProperty] public string StateColor { get { return _stateColor; } }
        [DataSourceProperty] public string Tax0Bg { get { return _taxBg[0]; } }
        [DataSourceProperty] public string Tax0Fg { get { return _taxFg[0]; } }
        [DataSourceProperty] public string Tax1Bg { get { return _taxBg[1]; } }
        [DataSourceProperty] public string Tax1Fg { get { return _taxFg[1]; } }
        [DataSourceProperty] public string Tax2Bg { get { return _taxBg[2]; } }
        [DataSourceProperty] public string Tax2Fg { get { return _taxFg[2]; } }
        [DataSourceProperty] public string Tax3Bg { get { return _taxBg[3]; } }
        [DataSourceProperty] public string Tax3Fg { get { return _taxFg[3]; } }
        [DataSourceProperty] public string Levy0Bg { get { return _levyBg[0]; } }
        [DataSourceProperty] public string Levy0Fg { get { return _levyFg[0]; } }
        [DataSourceProperty] public string Levy1Bg { get { return _levyBg[1]; } }
        [DataSourceProperty] public string Levy1Fg { get { return _levyFg[1]; } }
        [DataSourceProperty] public string Levy2Bg { get { return _levyBg[2]; } }
        [DataSourceProperty] public string Levy2Fg { get { return _levyFg[2]; } }
        [DataSourceProperty] public string Levy3Bg { get { return _levyBg[3]; } }
        [DataSourceProperty] public string Levy3Fg { get { return _levyFg[3]; } }
    }

    // 封建契约侧栏 VM(v4.141)
    public class ContractsPanelVM : PanelVMBase
    {
        private readonly Action _onClose;
        private const int MaxRows = 8;
        private readonly List<ContractRowVM> _all = new List<ContractRowVM>();
        private int _scroll;
        private string _authText = "", _taxAvgText = "", _levyAvgText = "", _lordCountText = "", _statusText = "", _hintText = "";

        public ContractsPanelVM(Action onClose)
        {
            _onClose = onClose;
            Rows = new MBBindingList<ContractRowVM>();
            Refresh();
        }

        public MBBindingList<ContractRowVM> Rows { get; private set; }

        [DataSourceProperty] public string AuthText { get { return _authText; } }
        [DataSourceProperty] public string TaxAvgText { get { return _taxAvgText; } }
        [DataSourceProperty] public string LevyAvgText { get { return _levyAvgText; } }
        [DataSourceProperty] public string LordCountText { get { return _lordCountText; } }
        [DataSourceProperty] public string StatusText { get { return _statusText; } }
        [DataSourceProperty] public string HintText { get { return _hintText; } }
        [DataSourceProperty] public bool EmptyVisible { get { return _all.Count == 0; } }

        public void ExecuteClose() { if (_onClose != null) _onClose(); }

        internal int RowCount { get { return Rows.Count; } }

        internal string ClanAt(int row)
        {
            try { return (row >= 0 && row < Rows.Count) ? Rows[row].ClanId : null; } catch { return null; }
        }

        internal void ScrollStep(int dir)
        {
            try
            {
                int max = Math.Max(0, _all.Count - MaxRows);
                _scroll += dir;
                if (_scroll < 0) _scroll = 0;
                if (_scroll > max) _scroll = max;
                Rebuild();
            }
            catch { }
        }

        // 点档位格: 高于当前 -> 提一档; 低于当前 -> 降一档(一次一格, 受冷却限制)
        internal void SetTaxTier(int row, int tier)
        {
            try
            {
                string id = ClanAt(row);
                if (id == null) return;
                var c = FeudalContracts.Of(id);
                if (tier == c.Tax) { _statusText = "已是当前档位"; Refresh(); return; }
                bool up = tier > c.Tax;
                _statusText = FeudalContracts.Adjust(id, true, up);
                Refresh();
            }
            catch (Exception ex) { DLog.Force("契约税档调整失败: " + ex.Message); }
        }

        internal void SetLevyTier(int row, int tier)
        {
            try
            {
                string id = ClanAt(row);
                if (id == null) return;
                var c = FeudalContracts.Of(id);
                if (tier == c.Levy) { _statusText = "已是当前档位"; Refresh(); return; }
                bool up = tier > c.Levy;
                _statusText = FeudalContracts.Adjust(id, false, up);
                Refresh();
            }
            catch (Exception ex) { DLog.Force("契约兵役档调整失败: " + ex.Message); }
        }

        internal void Refresh()
        {
            try
            {
                Politics.RefreshLords();
                _authText = ((int)Politics.Authority).ToString();
                _taxAvgText = "×" + FeudalContracts.AvgTaxMult().ToString("F2");
                _levyAvgText = "×" + FeudalContracts.AvgLevyMult().ToString("F2");
                _lordCountText = Politics.Lords.Count.ToString();
                _all.Clear();
                var list = new List<LordProfile>(Politics.Lords.Values);
                list.Sort(delegate (LordProfile a, LordProfile b) { return b.Clout.CompareTo(a.Clout); });
                for (int i = 0; i < list.Count; i++)
                    _all.Add(new ContractRowVM(list[i].ClanId, list[i]));
                Rebuild();
                _hintText = "点档位格调整(每次 20 权威, 同族 84 天一次): 税赋档影响国库实收(×0.5~2), 兵役档影响征兵上限(×0.5~2); "
                    + "加档领主不满 +12/+9 并每月累积, 减档 -10/-8 可安抚; 滚轮翻看全部领主。";
                OnPropertyChangedWithValue(_authText, "AuthText");
                OnPropertyChangedWithValue(_taxAvgText, "TaxAvgText");
                OnPropertyChangedWithValue(_levyAvgText, "LevyAvgText");
                OnPropertyChangedWithValue(_lordCountText, "LordCountText");
                OnPropertyChangedWithValue(_statusText, "StatusText");
                OnPropertyChangedWithValue(_hintText, "HintText");
                OnPropertyChangedWithValue(EmptyVisible, "EmptyVisible");
            }
            catch (Exception ex) { DLog.Force("契约侧栏刷新失败: " + ex.Message); }
        }

        private void Rebuild()
        {
            try
            {
                Rows.Clear();
                for (int i = 0; i < MaxRows && _scroll + i < _all.Count; i++)
                    Rows.Add(_all[_scroll + i]);
            }
            catch { }
        }
    }
}
