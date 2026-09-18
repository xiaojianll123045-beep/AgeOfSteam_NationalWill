using System;
using System.Collections.Generic;
using TaleWorlds.Library;

namespace FeudalInternalAffairs
{
    // 领主名册一行(文档 21.3)
    public class LordRowVM : ViewModel
    {
        internal readonly string ClanId;
        private readonly string _name, _clout, _attitude, _attitudeColor, _anger, _angerColor, _tendency, _seat;

        internal LordRowVM(LordProfile lp)
        {
            ClanId = lp.ClanId;
            string nm = lp.Name ?? "?";
            _name = nm.Length > 5 ? nm.Substring(0, 5) : nm;   // 列宽有限(大字号)
            _clout = lp.Clout.ToString("N0");
            _attitude = (lp.Attitude >= 0 ? "+" : "") + lp.Attitude;
            _attitudeColor = lp.Attitude >= 40 ? "#39FF14FF" : (lp.Attitude <= -30 ? "#D96A5AFF" : "#C8B98FFF");
            _anger = lp.Anger.ToString("N0");
            _angerColor = lp.Anger >= 75 ? "#FF4B4BFF" : (lp.Anger >= 40 ? "#E8C33AFF" : "#39FF14FF");
            _tendency = Politics.TendencyNames[lp.Tendency >= 0 && lp.Tendency < 4 ? lp.Tendency : 1];
            _seat = lp.Seat >= 0 && lp.Seat < 5 ? Politics.SeatNames[lp.Seat] : "—";
        }

        [DataSourceProperty] public string Name { get { return _name; } }
        [DataSourceProperty] public string Clout { get { return _clout; } }
        [DataSourceProperty] public string Attitude { get { return _attitude; } }
        [DataSourceProperty] public string AttitudeColor { get { return _attitudeColor; } }
        [DataSourceProperty] public string Anger { get { return _anger; } }
        [DataSourceProperty] public string AngerColor { get { return _angerColor; } }
        [DataSourceProperty] public string Tendency { get { return _tendency; } }
        [DataSourceProperty] public string Seat { get { return _seat; } }
    }

    public class EstateRowVM : ViewModel
    {
        private readonly string _name, _clout, _attitude, _attitudeColor, _anger, _angerColor;

        internal EstateRowVM(string name, int clout, int attitude, int anger)
        {
            _name = name;
            _clout = "势力 " + clout.ToString("N0");
            _attitude = "态度 " + (attitude >= 0 ? "+" : "") + attitude;
            _attitudeColor = attitude >= 40 ? "#39FF14FF" : (attitude <= -30 ? "#D96A5AFF" : "#C8B98FFF");
            _anger = "愤怒 " + anger;
            _angerColor = anger >= 75 ? "#FF4B4BFF" : (anger >= 40 ? "#E8C33AFF" : "#39FF14FF");
        }

        [DataSourceProperty] public string Name { get { return _name; } }
        [DataSourceProperty] public string Clout { get { return _clout; } }
        [DataSourceProperty] public string Attitude { get { return _attitude; } }
        [DataSourceProperty] public string AttitudeColor { get { return _attitudeColor; } }
        [DataSourceProperty] public string Anger { get { return _anger; } }
        [DataSourceProperty] public string AngerColor { get { return _angerColor; } }
    }

    public class LawRowVM : ViewModel
    {
        internal readonly int Cat;
        private readonly string _name, _now, _next, _cost, _support, _supportColor;

        internal LawRowVM(int cat)
        {
            Cat = cat;
            int lv = Politics.LawLevel(cat);
            int pct = Politics.SupportPct(cat);
            _name = Politics.LawNames[cat];
            _now = Politics.LawLevels[lv];
            _next = lv >= 3 ? "—" : Politics.LawLevels[lv + 1];
            _cost = lv >= 3 ? "—" : Politics.LawCost(cat).ToString("N0");
            _support = pct + "%";
            _supportColor = pct >= 50 ? "#39FF14FF" : "#D96A5AFF";
        }

        [DataSourceProperty] public string Name { get { return _name; } }
        [DataSourceProperty] public string Now { get { return _now; } }
        [DataSourceProperty] public string Next { get { return _next; } }
        [DataSourceProperty] public string Cost { get { return _cost; } }
        [DataSourceProperty] public string Support { get { return _support; } }
        [DataSourceProperty] public string SupportColor { get { return _supportColor; } }
    }

    public class PetitionRowVM : ViewModel
    {
        internal readonly int Index;
        private readonly string _text;

        internal PetitionRowVM(int index, string text)
        {
            Index = index;
            _text = text;
        }

        [DataSourceProperty] public string Text { get { return _text; } }
    }

    // 政治页 VM(文档 21.10; 导航栏第 11 格)
    public class PoliticsPanelVM : PanelVMBase
    {
        private readonly Action _onClose;
        private const int MaxRows = 10;
        private readonly List<LordRowVM> _allRows = new List<LordRowVM>();
        private int _scroll;
        private int _lawSelected = -1;

        private string _authText = "", _legitText = "", _tyrannyText = "", _warText = "", _warColor = "", _councilText = "", _status = "", _lawHint = "", _petitionHint = "";

        public PoliticsPanelVM(Action onClose)
        {
            _onClose = onClose;
            Rows = new MBBindingList<LordRowVM>();
            EstateRows = new MBBindingList<EstateRowVM>();
            LawRows = new MBBindingList<LawRowVM>();
            PetitionRows = new MBBindingList<PetitionRowVM>();
            Refresh();
        }

        public MBBindingList<LordRowVM> Rows { get; private set; }
        public MBBindingList<EstateRowVM> EstateRows { get; private set; }
        public MBBindingList<LawRowVM> LawRows { get; private set; }
        public MBBindingList<PetitionRowVM> PetitionRows { get; private set; }

        [DataSourceProperty] public string AuthText { get { return _authText; } }
        [DataSourceProperty] public string LegitText { get { return _legitText; } }
        [DataSourceProperty] public string TyrannyText { get { return _tyrannyText; } }
        [DataSourceProperty] public string WarText { get { return _warText; } }
        [DataSourceProperty] public string WarColor { get { return _warColor; } }
        [DataSourceProperty] public string CouncilText { get { return _councilText; } }
        [DataSourceProperty] public string StatusText { get { return _status; } }
        [DataSourceProperty] public string LawHint { get { return _lawHint; } }
        [DataSourceProperty] public string PetitionHint { get { return _petitionHint; } }

        public void ExecuteClose() { if (_onClose != null) _onClose(); }

        internal int ShownCount { get { return Rows.Count; } }

        internal LordRowVM RowAt(int i)
        {
            try { return i >= 0 && i < Rows.Count ? Rows[i] : null; } catch { return null; }
        }

        // 滚轮翻看领主名册
        internal void ScrollStep(int dir)
        {
            try
            {
                int max = Math.Max(0, _allRows.Count - MaxRows);
                _scroll += dir;
                if (_scroll < 0) _scroll = 0;
                if (_scroll > max) _scroll = max;
                RebuildRows();
            }
            catch { }
        }

        // ---- 操作 ----
        internal void LordAction(int i, int kind)
        {
            try
            {
                var r = RowAt(i);
                if (r == null) return;
                switch (kind)
                {
                    case 0: _status = Politics.Grant(r.ClanId); break;
                    case 1: _status = Politics.Honor(r.ClanId); break;
                    case 2: _status = Politics.Appoint(r.ClanId); break;
                    case 3: _status = Politics.Dismiss(r.ClanId); break;
                    default: _status = Politics.Execute(r.ClanId); break;
                }
                Refresh();
            }
            catch (Exception ex) { DLog.Force("政治操作失败: " + ex.Message); }
        }

        // 点击法令行: 底部显示"效果 + 表决名单"(文档 21.4)
        internal void SelectLaw(int cat)
        {
            try
            {
                _lawSelected = cat;
                _status = Politics.LawNames[cat] + ": " + Politics.LawShort[cat]
                    + " ｜ " + Politics.VoteSummary(cat);
                Refresh();
            }
            catch (Exception ex) { DLog.Force("法令查看失败: " + ex.Message); }
        }

        internal void LawAction(int cat, bool force)
        {
            try { _status = Politics.ProposeLaw(cat, force, Today()); Refresh(); }
            catch (Exception ex) { DLog.Force("立法操作失败: " + ex.Message); }
        }

        internal void PetitionAction(int index, bool accept)
        {
            try { _status = Politics.Respond(index, accept, Today()); Refresh(); }
            catch (Exception ex) { DLog.Force("请愿处理失败: " + ex.Message); }
        }

        internal void Feast()
        {
            try { _status = Politics.Feast(); Refresh(); }
            catch { }
        }

        internal void Patrol()
        {
            try { _status = Politics.Patrol(); Refresh(); }
            catch { }
        }

        internal void Suppress()
        {
            try { _status = Politics.Suppress(); Refresh(); }
            catch { }
        }

        internal void Concede()
        {
            try { _status = Politics.Concede(); Refresh(); }
            catch { }
        }

        private static int Today()
        {
            try { return (int)TaleWorlds.CampaignSystem.CampaignTime.Now.ToDays; } catch { return 0; }
        }

        // ---- 刷新 ----
        internal void Refresh()
        {
            try
            {
                Politics.RefreshLords();
                Politics.Aggregate();

                _authText = "权威 " + ((int)Politics.Authority) + " / 1000 (恢复 +" + ((int)Politics.MonthRegen()) + "/月)";
                _legitText = "合法性 " + ((int)Politics.Legitimacy) + "%";
                _tyrannyText = "暴政 " + Politics.Tyranny.ToString("F1");
                if (Politics.CivilWar)
                {
                    _warText = "【内战】税收 -40% · 全境产出 -15% · 权威每日流失(可镇压或妥协) ｜ 阶段: " + Politics.StageText();
                    _warColor = "#FF4B4BFF";
                }
                else if (Politics.NobleAnger >= 60)
                {
                    _warText = "局势: " + Politics.StageText() + " ｜ 领主愤怒 " + Politics.NobleAnger
                        + " (≥90 且合法性<25 将爆发内战) ｜ 王室势力 " + ((int)Politics.CrownClout()) + " vs 领主 " + Politics.NobleClout;
                    _warColor = Politics.NobleAnger >= 75 ? "#FF4B4BFF" : "#E8C33AFF";
                }
                else
                {
                    _warText = "局势: " + Politics.StageText() + " · 领主愤怒 " + Politics.NobleAnger + " · 大领主势力 " + Politics.NobleClout
                        + " vs 王室 " + ((int)Politics.CrownClout());
                    _warColor = "#7A7060FF";
                }

                _councilText = "御前会议: ";
                for (int i = 0; i < 5; i++)
                {
                    if (i > 0) _councilText += " · ";
                    string id = Politics.Council[i];
                    string who = "—";
                    LordProfile lp;
                    if (!string.IsNullOrEmpty(id) && Politics.Lords.TryGetValue(id, out lp)) who = lp.Name;
                    _councilText += Politics.SeatNames[i] + " " + who;
                }

                // 阶层
                EstateRows.Clear();
                EstateRows.Add(new EstateRowVM(Politics.EstateNames[0], Politics.NobleClout, Politics.NobleAttitude, Politics.NobleAnger));
                EstateRows.Add(new EstateRowVM(Politics.EstateNames[1], Politics.ChurchClout, Politics.ChurchAttitude, Politics.ChurchAnger));
                EstateRows.Add(new EstateRowVM(Politics.EstateNames[2], Politics.BurgerClout, Politics.BurgerAttitude, Politics.BurgerAnger));

                // 法令
                LawRows.Clear();
                for (int i = 0; i < 6; i++) LawRows.Add(new LawRowVM(i));

                // 请愿
                PetitionRows.Clear();
                for (int i = 0; i < Politics.Petitions.Count && i < 3; i++)
                    PetitionRows.Add(new PetitionRowVM(i, Politics.Petitions[i].Text));
                _petitionHint = Politics.Petitions.Count == 0 ? "暂无请愿(月结时视民情生成)" : "";

                // 领主名册(按势力降序)
                _allRows.Clear();
                var views = new List<LordProfile>(Politics.Lords.Values);
                views.Sort(delegate (LordProfile a, LordProfile b) { return b.Clout.CompareTo(a.Clout); });
                for (int i = 0; i < views.Count; i++) _allRows.Add(new LordRowVM(views[i]));
                int max = Math.Max(0, _allRows.Count - MaxRows);
                if (_scroll > max) _scroll = max;
                RebuildRows();

                _lawHint = _lawSelected >= 0
                    ? "已选: " + Politics.LawNames[_lawSelected] + " · 点『提案』表决 / 『强推』双倍权威"
                    : "成本=权威 · 点任意法令行可看效果与领主表决名单";

                OnPropertyChangedWithValue(_authText, "AuthText");
                OnPropertyChangedWithValue(_legitText, "LegitText");
                OnPropertyChangedWithValue(_tyrannyText, "TyrannyText");
                OnPropertyChangedWithValue(_warText, "WarText");
                OnPropertyChangedWithValue(_warColor, "WarColor");
                OnPropertyChangedWithValue(_councilText, "CouncilText");
                OnPropertyChangedWithValue(_status, "StatusText");
                OnPropertyChangedWithValue(_lawHint, "LawHint");
                OnPropertyChangedWithValue(_petitionHint, "PetitionHint");
            }
            catch (Exception ex) { DLog.Force("政治页刷新失败: " + ex.Message); }
        }

        private void RebuildRows()
        {
            try
            {
                Rows.Clear();
                for (int i = 0; i < MaxRows && _scroll + i < _allRows.Count; i++) Rows.Add(_allRows[_scroll + i]);
            }
            catch { }
        }
    }
}
