using System;
using System.Collections.Generic;
using TaleWorlds.Library;

namespace FeudalInternalAffairs
{
    public class ActRowVM : ViewModel
    {
        internal readonly int Idx;
        private readonly string _name, _cost, _body, _nameColor;
        internal ActRowVM(int idx, string name, string cost, string body, string color)
        { Idx = idx; _name = name; _cost = cost; _body = body; _nameColor = color; }
        [DataSourceProperty] public string Name { get { return _name; } }
        [DataSourceProperty] public string Cost { get { return _cost; } }
        [DataSourceProperty] public string Body { get { return _body; } }
        [DataSourceProperty] public string NameColor { get { return _nameColor; } }
    }

    // 游说页 VM(v4.211)
    public class LobbyVM : PanelVMBase
    {
        private readonly Action _onClose;
        public LobbyVM(Action onClose) { _onClose = onClose; Rows = new MBBindingList<ActRowVM>(); Refresh(); }
        public MBBindingList<ActRowVM> Rows { get; private set; }
        [DataSourceProperty] public string Summary
        { get { try { int g = 0; try { g = TaleWorlds.CampaignSystem.Hero.MainHero != null ? TaleWorlds.CampaignSystem.Hero.MainHero.Gold : 0; } catch { } return "金币 " + g + " · 每条游说 14 日冷却 · " + Lobbying.StatusText(); } catch { return ""; } } }
        public void ExecuteClose() { if (_onClose != null) _onClose(); }
        internal void Click(int idx)
        {
            try { if (idx < 0 || idx >= Rows.Count) return; string m = Lobbying.Run(Lobbying.All[idx].Id); try { MapSelection.Message(m); } catch { } Refresh(); }
            catch (Exception ex) { DLog.Force("游说失败: " + ex.Message); }
        }
        internal void Refresh()
        {
            try
            {
                Rows.Clear();
                for (int i = 0; i < Lobbying.All.Count; i++)
                {
                    var d = Lobbying.All[i];
                    int cd = Lobbying.CooldownLeft(d.Id);
                    string cost = cd > 0 ? ("冷却 " + cd + " 日") : (d.Gold + " 金");
                    string col = cd > 0 ? "#8A8070FF" : "#E9CD74FF";
                    Rows.Add(new ActRowVM(i, d.Name, cost, d.Desc + "\n" + d.Effect, col));
                }
                OnPropertyChangedWithValue(Rows.Count, "Rows");
                OnPropertyChangedWithValue(Summary, "Summary");
            }
            catch (Exception ex) { DLog.Force("游说页刷新失败: " + ex.Message); }
        }
    }

    // 议会页 VM(v4.211)
    public class ParliamentVM : PanelVMBase
    {
        private readonly Action _onClose;
        private readonly List<int> _laws = new List<int>();
        public ParliamentVM(Action onClose) { _onClose = onClose; Rows = new MBBindingList<ActRowVM>(); Refresh(); }
        public MBBindingList<ActRowVM> Rows { get; private set; }
        [DataSourceProperty] public string Summary
        { get { try { return "席位: " + Parliament.SeatText(); } catch { return ""; } } }
        public void ExecuteClose() { if (_onClose != null) _onClose(); }
        internal void Click(int idx)
        {
            try
            {
                if (idx < 0 || idx >= _laws.Count) return;
                int law = _laws[idx];
                string m = Parliament.Vote(law, LawSystem.NextTier(law));
                try { MapSelection.Message(m); } catch { }
                Refresh();
            }
            catch (Exception ex) { DLog.Force("表决失败: " + ex.Message); }
        }
        internal void Refresh()
        {
            try
            {
                Rows.Clear(); _laws.Clear();
                for (int i = 0; i < LawSystem.LawCount; i++)
                {
                    if (!LawSystem.IsAvailable(i)) continue;
                    if (LawSystem.Maxed(i)) continue;
                    int t = LawSystem.NextTier(i);
                    int yes = Parliament.YesSeats(i, t);
                    string cost = "赞成 " + yes + " / 100 席";
                    string col = yes > 50 ? "#7FBF6AFF" : "#E8A33AFF";
                    _laws.Add(i);
                    Rows.Add(new ActRowVM(Rows.Count, LawSystem.NameOf(i) + " → " + LawSystem.TierName(i, t), cost, LawSystem.EffectOfTier(i, t), col));
                    if (_laws.Count >= 10) break;
                }
                OnPropertyChangedWithValue(Rows.Count, "Rows");
                OnPropertyChangedWithValue(Summary, "Summary");
            }
            catch (Exception ex) { DLog.Force("议会页刷新失败: " + ex.Message); }
        }
    }

    // 钱箱页 VM(v4.211)
    public class ChestVM : PanelVMBase
    {
        private readonly Action _onClose;
        public ChestVM(Action onClose) { _onClose = onClose; Rows = new MBBindingList<ActRowVM>(); Refresh(); }
        public MBBindingList<ActRowVM> Rows { get; private set; }
        [DataSourceProperty] public string Summary
        { get { try { int g = 0; try { g = TaleWorlds.CampaignSystem.Hero.MainHero != null ? TaleWorlds.CampaignSystem.Hero.MainHero.Gold : 0; } catch { } return "钱箱 " + ((int)CashChest.Chest) + " 第纳尔 · 日息 0.03% · 铸币分红 25% 入箱 · 君主金币 " + g; } catch { return ""; } } }
        public void ExecuteClose() { if (_onClose != null) _onClose(); }
        internal void Click(int idx)
        {
            try
            {
                string m = "";
                if (idx == 0) m = CashChest.Deposit(5000);
                else if (idx == 1) m = CashChest.Deposit(20000);
                else if (idx == 2) m = CashChest.Deposit(TaleWorlds.CampaignSystem.Hero.MainHero != null ? TaleWorlds.CampaignSystem.Hero.MainHero.Gold : 0);
                else if (idx == 3) m = CashChest.Withdraw(5000);
                else if (idx == 4) m = CashChest.Withdraw((int)CashChest.Chest);
                try { MapSelection.Message(m); } catch { }
                Refresh();
            }
            catch (Exception ex) { DLog.Force("钱箱操作失败: " + ex.Message); }
        }
        internal void Refresh()
        {
            try
            {
                try { CashChest.Accrue(Politics.Today()); } catch { }
                Rows.Clear();
                Rows.Add(new ActRowVM(0, "存入 5,000", "5,000 金", "从君主金币转入钱箱", "#E9CD74FF"));
                Rows.Add(new ActRowVM(1, "存入 20,000", "20,000 金", "从君主金币转入钱箱", "#E9CD74FF"));
                Rows.Add(new ActRowVM(2, "全部存入", "可支配金币", "把当前所有可支配金币转入钱箱", "#E9CD74FF"));
                Rows.Add(new ActRowVM(3, "取出 5,000", "钱箱余额", "从钱箱取回 5,000 第纳尔", "#8FD8E8FF"));
                Rows.Add(new ActRowVM(4, "全部取出", "钱箱余额", "取出钱箱全部余额", "#8FD8E8FF"));
                OnPropertyChangedWithValue(Rows.Count, "Rows");
                OnPropertyChangedWithValue(Summary, "Summary");
            }
            catch (Exception ex) { DLog.Force("钱箱页刷新失败: " + ex.Message); }
        }
    }
}