using System;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.Engine.GauntletUI;

namespace FeudalInternalAffairs
{
    // 通用面板滑入/滑出动画(不动 prefab):
    // 面板层加载后, 从 UIContext.RootWidget 的 Children[0] 拿到面板控件, 每帧改它的 MarginLeft。
    // (国家面板有自己的 VM 动画, 不用这个; 其余四个面板用它, 于是五个面板开关动画统一)
    internal static class PanelAnim
    {
        private class Anim
        {
            internal object Widget;
            internal PropertyInfo MarginProp;
            internal FieldInfo MarginField;
            internal float From, To, T, Duration;
            internal bool Running;
        }

        private static readonly System.Collections.Generic.Dictionary<string, Anim> Anims =
            new System.Collections.Generic.Dictionary<string, Anim>();

        internal static bool TryBind(string key, GauntletLayer layer)
        {
            try
            {
                if (layer == null) return false;
                var ctxProp = AccessTools.Property(layer.GetType(), "UIContext");
                var ctx = ctxProp != null ? ctxProp.GetValue(layer, null) : null;
                if (ctx == null) return false;   // 层还没激活, 下次再试
                var rootProp = AccessTools.Property(ctx.GetType(), "RootWidget");
                var root = rootProp != null ? rootProp.GetValue(ctx, null) : null;
                if (root == null) return false;

                var childrenProp = AccessTools.Property(root.GetType(), "Children");
                var children = childrenProp != null ? childrenProp.GetValue(root, null) as System.Collections.IEnumerable : null;
                if (children == null) return false;
                object panel = null;
                foreach (var c in children) { panel = c; break; }   // 面板 = 根的第一个子控件
                if (panel == null)
                {
                    DLog.Force("面板动画: " + key + " 根控件没有子控件, 无法绑定");
                    return true;   // 结构不对, 不再重试(避免日志刷屏)
                }

                var a = new Anim { Widget = panel };
                var t = panel.GetType();
                a.MarginProp = AccessTools.Property(t, "MarginLeft");
                if (a.MarginProp == null || !a.MarginProp.CanWrite)
                    a.MarginField = AccessTools.Field(t, "MarginLeft");
                if (a.MarginProp == null && a.MarginField == null)
                {
                    DLog.Force("面板动画: " + key + " 控件无 MarginLeft(" + t.Name + "), 跳过");
                    return true;
                }
                Anims[key] = a;
                DLog.Force("面板动画: 已绑定 " + key + "(控件=" + t.Name + ")");
                return true;
            }
            catch (Exception ex) { DLog.Info("面板动画绑定失败 " + key + ": " + ex.Message); return false; }
        }

        internal static void Bind(string key, GauntletLayer layer) { TryBind(key, layer); }

        // 从 from 滑到 to
        internal static void Play(string key, float from, float to, float duration)
        {
            try
            {
                Anim a;
                if (!Anims.TryGetValue(key, out a)) return;
                a.From = from; a.To = to; a.T = 0f; a.Duration = Math.Max(0.01f, duration); a.Running = true;
                Apply(a, from);
            }
            catch { }
        }

        internal static void Unbind(string key) { try { Anims.Remove(key); } catch { } }

        internal static void Tick(float dt)
        {
            try
            {
                foreach (var kv in Anims)
                {
                    var a = kv.Value;
                    if (!a.Running) continue;
                    a.T += dt;
                    float p = Math.Min(1f, a.T / a.Duration);
                    // ease-out
                    float e = 1f - (1f - p) * (1f - p);
                    Apply(a, a.From + (a.To - a.From) * e);
                    if (p >= 1f) a.Running = false;
                }
            }
            catch { }
        }

        private static void Apply(Anim a, float v)
        {
            try
            {
                if (a.MarginProp != null && a.MarginProp.CanWrite) a.MarginProp.SetValue(a.Widget, v, null);
                else if (a.MarginField != null) a.MarginField.SetValue(a.Widget, v);
            }
            catch { }
        }
    }
}
