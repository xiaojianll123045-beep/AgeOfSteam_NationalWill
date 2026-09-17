using System;
using System.Reflection;
using HarmonyLib;

namespace FeudalInternalAffairs
{
    // 消息流避让(不用 VMMixin!): UIExtenderEx 的 ViewModelMixin 在注册阶段(OnSubModuleLoad)会因为
    // 目标 VM 类型所在程序集尚未加载而抛 NRE, 导致整个 UIExtenderEx 初始化失败
    // (连累名牌/选中圈等既有扩展) —— 实测踩过, 所以改成"运行时直接改控件"。
    //
    // 做法: 面板打开时, 反射找到聊天记录 View 的层/控件, 给它加一个左侧留白(等价于右移);
    //       面板关闭/宽度变化时更新。找不到就静默跳过(不影响其它功能)。
    internal static class ChatLogAvoid
    {
        private static bool _searched;
        private static object _target;      // 找到的控件(或其外层)
        private static PropertyInfo _marginProp;
        private static FieldInfo _marginField;
        private static float _lastSet = -1f;

        internal static void SetOffset(float width)
        {
            try
            {
                if (_target == null && !_searched) Find();
                if (_target == null) return;
                if (Math.Abs(_lastSet - width) < 0.5f) return;
                _lastSet = width;
                ApplyOffset(width);
            }
            catch { }
        }

        // 找聊天记录控件: 遍历候选层(地图屏各层 + GlobalLayer), 打印名字并挑匹配的
        private static void Find()
        {
            _searched = true;
            try
            {
                var cands = new System.Collections.Generic.List<object>();
                var map = SandBox.View.Map.MapScreen.Instance;
                if (map != null) { try { foreach (var l in map.Layers) if (l != null) cands.Add(l); } catch { } }
                try
                {
                    var glType = AccessTools.TypeByName("TaleWorlds.ScreenSystem.GlobalLayer");
                    var glProp = glType != null ? AccessTools.Property(glType, "Layer") : null;
                    var gl = glProp != null ? (glProp.GetGetMethod(true) != null && glProp.GetGetMethod(true).IsStatic
                        ? glProp.GetValue(null, null) : null) : null;
                    if (gl != null) cands.Add(gl);
                }
                catch { }

                var names = new System.Text.StringBuilder();
                foreach (var layer in cands)
                {
                    string name = "";
                    try { var p = AccessTools.Property(layer.GetType(), "Name"); name = p != null ? (p.GetValue(layer, null) as string) : ""; } catch { }
                    if (names.Length > 0) names.Append(", ");
                    names.Append(name ?? "?");

                    if (name == null) continue;
                    if (name.IndexOf("chat", StringComparison.OrdinalIgnoreCase) < 0
                        && name.IndexOf("message", StringComparison.OrdinalIgnoreCase) < 0
                        && name.IndexOf("notification", StringComparison.OrdinalIgnoreCase) < 0) continue;

                    var uiCtxProp = AccessTools.Property(layer.GetType(), "UIContext");
                    var ctx = uiCtxProp != null ? uiCtxProp.GetValue(layer, null) : null;
                    if (ctx == null) continue;
                    var rootProp = AccessTools.Property(ctx.GetType(), "RootWidget");
                    var root = rootProp != null ? rootProp.GetValue(ctx, null) : null;
                    if (root == null) continue;

                    _target = root;
                    var t = root.GetType();
                    _marginProp = AccessTools.Property(t, "MarginLeft");
                    if (_marginProp == null || !_marginProp.CanWrite)
                        _marginField = AccessTools.Field(t, "MarginLeft");
                    DLog.Force("消息流避让: 找到聊天记录控件(层=" + name + ", 类型=" + t.Name + ")");
                    return;
                }

                var all = names.ToString();
                if (all.Length > 700) all = all.Substring(0, 700);
                DLog.Force("消息流避让: 未匹配到, 现有层名=[" + all + "]");
            }
            catch (Exception ex) { DLog.Force("消息流避让查找失败: " + ex.Message); }
        }

        private static void ApplyOffset(float width)
        {
            try
            {
                if (_marginProp != null && _marginProp.CanWrite) _marginProp.SetValue(_target, width, null);
                else if (_marginField != null) _marginField.SetValue(_target, width);
                else
                {
                    // 退而求其次: 改 PositionXOffset
                    var t = _target.GetType();
                    var p = AccessTools.Property(t, "PositionXOffset");
                    if (p != null && p.CanWrite) p.SetValue(_target, width, null);
                }
            }
            catch (Exception ex) { DLog.Info("消息流避让应用失败: " + ex.Message); }
        }
    }
}
