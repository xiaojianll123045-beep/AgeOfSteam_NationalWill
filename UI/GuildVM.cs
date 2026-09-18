using System;
using TaleWorlds.Library;

namespace FeudalInternalAffairs
{
    // 行会行(文档 19.14 扩展; 对应 V3 公司窗口)
    public class GuildRowVM : ViewModel
    {
        private readonly string _name, _icon, _desc, _cost, _state, _stateColor;
        internal readonly string Id;

        internal GuildRowVM(GuildDef def)
        {
            Id = def.Id;
            bool f = Guilds.IsFounded(def.Id);
            bool c = Guilds.IsChartered(def.Id);
            _name = def.Name;
            _icon = def.Sprite;
            _desc = def.Desc;
            _cost = f ? "—" : "费用 " + def.Cost.ToString("N0");
            // 按钮语义: 未成立 -> 成立; 已成立 -> 申请特许; 已特许 -> 撤销
            _state = f ? (c ? "已授·撤销" : "申请特许") : "成立";
            _stateColor = c ? "#39FF14FF" : (f ? "#E8C33AFF" : "#E8C33AFF");
        }

        [DataSourceProperty] public string Name { get { return _name; } }
        [DataSourceProperty] public string Icon { get { return _icon; } }
        [DataSourceProperty] public string Desc { get { return _desc; } }
        [DataSourceProperty] public string Cost { get { return _cost; } }
        [DataSourceProperty] public string State { get { return _state; } }
        [DataSourceProperty] public string StateColor { get { return _stateColor; } }
    }

    // 行会页 VM
    public class GuildPanelVM : PanelVMBase
    {
        private readonly Action _onClose;
        private string _hint = "";

        public GuildPanelVM(Action onClose)
        {
            _onClose = onClose;
            Rows = new MBBindingList<GuildRowVM>();
            Refresh();
        }

        public MBBindingList<GuildRowVM> Rows { get; private set; }

        [DataSourceProperty] public string Hint { get { return _hint; } }

        public void ExecuteClose() { if (_onClose != null) _onClose(); }

        internal void Found(int idx)
        {
            try
            {
                if (idx < 0 || idx >= Guilds.All.Count) return;
                var id = Guilds.All[idx].Id;
                int today = 0;
                try { today = (int)TaleWorlds.CampaignSystem.CampaignTime.Now.ToDays; } catch { }
                if (!Guilds.IsFounded(id)) _hint = Guilds.Found(id);
                else if (!Guilds.IsChartered(id)) _hint = Guilds.ApplyCharter(id, today);
                else _hint = Guilds.RevokeCharter(id, today);
                Refresh();
            }
            catch (Exception ex) { DLog.Force("行会操作失败: " + ex.Message); }
        }

        internal void Refresh()
        {
            try
            {
                Rows.Clear();
                for (int i = 0; i < Guilds.All.Count; i++) Rows.Add(new GuildRowVM(Guilds.All[i]));
                int n = 0, c = 0;
                foreach (var g in Guilds.All) { if (Guilds.IsFounded(g.Id)) n++; if (Guilds.IsChartered(g.Id)) c++; }
                int today = 0;
                try { today = (int)TaleWorlds.CampaignSystem.CampaignTime.Now.ToDays; } catch { }
                _hint = "已成立 " + n + " / " + Guilds.All.Count + " · 已授特许 " + c
                    + " · 年金/月 " + Guilds.MonthlyFeePreview().ToString("N0")
                    + "\n成立→申请王室特许状(垄断加成 +18%, 每年上缴年金); 已授可撤销(加成暂停 30 天)";
                OnPropertyChangedWithValue(_hint, "Hint");
            }
            catch (Exception ex) { DLog.Force("行会页刷新失败: " + ex.Message); }
        }
    }
}
