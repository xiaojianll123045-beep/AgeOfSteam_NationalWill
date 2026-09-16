using System;
using System.Reflection;

namespace MissionSharedLibrary.Utilities
{
    // 兼容补齐: RTS Camera 的 master 分支里缺少这个反射扩展(他们的源码没提交全),
    // 但多处调用 type.Method("X") / type.Method("X", typeof(...))。这里按用法补上。
    public static class RtsPortTypeExtensions
    {
        public static MethodInfo Method(this Type type, string name, params Type[] parameterTypes)
        {
            if (type == null) return null;
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            try
            {
                if (parameterTypes != null && parameterTypes.Length > 0)
                {
                    var m = type.GetMethod(name, flags, null, parameterTypes, null);
                    if (m != null) return m;
                }
                var any = type.GetMethod(name, flags);
                if (any != null) return any;
                return HarmonyLib.AccessTools.Method(type, name, parameterTypes);
            }
            catch { return null; }
        }
    }
}
