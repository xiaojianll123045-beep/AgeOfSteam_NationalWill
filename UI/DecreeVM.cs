using System;
using TaleWorlds.Library;

namespace FeudalInternalAffairs
{
    // 法令卡片(v4.210: 点一下即颁布; 生效中的置灰)
    public class DecreeCardVM : ViewModel
    {
        internal readonly string DecreeId;
        private readonly string _name, _cost, _body, _nameColor, _plate;
        internal DecreeCardVM(DecreeDef d, bool on)
        {
            DecreeId = d.Id;
            _name = d.Name + (on ? "  (生效中 剩 " + Decrees.DaysLeft(d.Id) + " 日)" : "");
            _cost = on ? "已颁布" : (d.Cost + " 权威 / " + d.Days + " 日");
            _body = d.Desc + "\n" + d.Effect;
            _nameColor = on ? "#8FD8E8FF" : "#E9CD74FF";
            _plate = on ? "#12414DE6" : "#1E1912E6";
        }
        [DataSourceProperty] public string Name { get { return _name; } }
        [DataSourceProperty] public string Cost { get { return _cost; } }
        [DataSourceProperty] public string Body { get { return _body; } }
        [DataSourceProperty] public string NameColor { get { return _nameColor; } }
        [DataSourceProperty] public string PlateColor { get { return _plate; } }
    }

    // 法令页 VM(自建页: 11 条法令 + 同时生效上限与状态)
    public class DecreeVM : PanelVMBase
    {
        private readonly Action _onClose;
        public DecreeVM(Action onClose)
        {
            _onClose = onClose;
            Cards = new MBBindingList<DecreeCardVM>();
            Refresh();
        }

        public MBBindingList<DecreeCardVM> Cards { get; private set; }

        [DataSourceProperty] public string Summary
        {
            get
            {
                try
                {
                    return "权威 " + ((int)Politics.Authority) + " · 同时生效 " + Decrees.Active.Count + "/" + Decrees.MaxActive()
                         + " 条(上限 = 2 + 官僚制度档位) · " + Decrees.StatusText();
                }
                catch { return ""; }
            }
        }

        public void ExecuteClose() { if (_onClose != null) _onClose(); }

        internal void EnactClick(int idx)
        {
            try
            {
                if (idx < 0 || idx >= Cards.Count) return;
                string msg = Decrees.Enact(Cards[idx].DecreeId);
                try { MapSelection.Message(msg); } catch { }
                DLog.Force("法令页: " + msg);
                Refresh();
            }
            catch (Exception ex) { DLog.Force("法令颁布失败: " + ex.Message); }
        }

        internal void Refresh()
        {
            try
            {
                Cards.Clear();
                for (int i = 0; i < Decrees.All.Count; i++)
                {
                    var d = Decrees.All[i];
                    Cards.Add(new DecreeCardVM(d, Decrees.IsActive(d.Id)));
                }
                OnPropertyChangedWithValue(Cards.Count, "Cards");
                OnPropertyChangedWithValue(Summary, "Summary");
            }
            catch (Exception ex) { DLog.Force("法令页刷新失败: " + ex.Message); }
        }
    }
}
