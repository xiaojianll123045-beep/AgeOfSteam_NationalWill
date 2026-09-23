using System;
using TaleWorlds.Library;

namespace FeudalInternalAffairs
{
    // 事件选项行(v4.197)
    public class EventChoiceVM : ViewModel
    {
        internal int Idx;
        private readonly string _text, _effect;
        internal EventChoiceVM(int idx, EventChoice c)
        {
            Idx = idx;
            _text = c.Text;
            _effect = c.Effect;
        }
        [DataSourceProperty] public string Text { get { return _text; } }
        [DataSourceProperty] public string Effect { get { return _effect; } }
    }

    // 事件面板 VM(左侧 560 宽: 横幅 + 标题 + 描述 + 选项)
    public class EventVM : PanelVMBase
    {
        private readonly Action _onClose;
        private string _icon = "fia_event_default", _title = "事件", _desc = "";
        private float _descH = 80f;

        public EventVM(Action onClose)
        {
            _onClose = onClose;
            Choices = new MBBindingList<EventChoiceVM>();
            Refresh();
        }

        public MBBindingList<EventChoiceVM> Choices { get; private set; }

        [DataSourceProperty] public string Icon { get { return _icon; } }
        [DataSourceProperty] public string Title { get { return _title; } }
        [DataSourceProperty] public string Desc { get { return _desc; } }
        [DataSourceProperty] public float DescH { get { return _descH; } }

        public void ExecuteClose() { if (_onClose != null) _onClose(); }

        internal void ChoiceClick(int idx)
        {
            try
            {
                string msg = Events.Apply(idx);
                if (!string.IsNullOrEmpty(msg)) { try { MapSelection.Message(msg); } catch { } }
                if (_onClose != null) _onClose();
            }
            catch (Exception ex) { DLog.Force("事件选项失败: " + ex.Message); }
        }

        internal void Dismiss()
        {
            try { Events.Dismiss(); } catch { }
            if (_onClose != null) _onClose();
        }

        internal void Refresh()
        {
            try
            {
                var e = Events.Pending;
                if (e == null) { _title = "暂无事件"; _desc = ""; Choices.Clear(); return; }
                _icon = e.Icon; _title = e.Name; _desc = e.Desc;
                // 粗略估算描述高度(每行 ~28 字, 行高 24)
                int lines = 1 + (_desc.Length / 28);
                _descH = Math.Max(48f, lines * 24f);
                Choices.Clear();
                if (e.Choices != null)
                    for (int i = 0; i < e.Choices.Length; i++) Choices.Add(new EventChoiceVM(i, e.Choices[i]));
                OnPropertyChangedWithValue(_icon, "Icon");
                OnPropertyChangedWithValue(_title, "Title");
                OnPropertyChangedWithValue(_desc, "Desc");
                OnPropertyChangedWithValue(_descH, "DescH");
            }
            catch (Exception ex) { DLog.Force("事件面板刷新失败: " + ex.Message); }
        }
    }
}
