using System;
using TaleWorlds.Library;

namespace FeudalInternalAffairs
{
    // 侧边栏面板 VM 基类: 提供统一的"滑入/滑出"动画属性
    // (prefab 里面板控件绑定 MarginLeft="@PanelOffset"; OpenPanelAnim/ClosePanelAnim 由 PanelScreen 调用)
    public class PanelVMBase : ViewModel
    {
        private float _offset = -700f;   // 默认滑出屏幕外
        private float _target = -700f;

        [DataSourceProperty]
        public float PanelOffset
        {
            get { return _offset; }
            set { if (Math.Abs(_offset - value) > 0.5f) { _offset = value; OnPropertyChangedWithValue(value, "PanelOffset"); } }
        }

        // 面板打开: 从屏幕外滑到 0
        internal void OpenPanelAnim(float width)
        {
            _offset = -width;
            _target = 0f;
            PanelOffset = _offset;
        }

        // 面板关闭: 从 0 滑到屏幕外
        internal void ClosePanelAnim(float width)
        {
            _target = -width;
        }

        // 每帧推进(缓动)
        internal void TickAnim(float dt)
        {
            try
            {
                if (Math.Abs(_offset - _target) < 0.5f) return;
                float speed = 3000f * Math.Max(0.001f, Math.Min(dt, 0.1f));
                float next = _offset + (_target > _offset ? speed : -speed);
                if ((_target > _offset && next > _target) || (_target < _offset && next < _target)) next = _target;
                PanelOffset = next;
            }
            catch { }
        }
    }
}
