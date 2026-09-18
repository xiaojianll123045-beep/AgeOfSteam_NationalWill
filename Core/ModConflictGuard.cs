using System;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.ModuleManager;

namespace FeudalInternalAffairs
{
    // 旧 MOD 共存守卫: 检测到【卡拉迪亚封建军政】也在加载时, 停用本 MOD 的所有初始化并弹窗
    internal static class ModConflictGuard
    {
        private const string RivalModuleId = "_FeudalCalradia";
        private const string RivalAssemblyName = "FeudalCalradia";

        private static bool _checked;
        private static bool _popupShown;

        internal static bool Blocked { get; private set; }

        internal static bool Check()
        {
            if (_checked) return Blocked;
            _checked = true;
            try
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    string n = null;
                    try { n = asm.GetName().Name; } catch { }
                    if (string.Equals(n, RivalAssemblyName, StringComparison.OrdinalIgnoreCase))
                    {
                        Blocked = true;
                        break;
                    }
                }
            }
            catch (Exception ex) { DLog.Force("冲突检测异常: " + ex.Message); }
            if (Blocked) DLog.Force("检测到旧 MOD [" + RivalName() + "]: 国家意志初始化已全部停用");
            return Blocked;
        }

        internal static string RivalName()
        {
            try
            {
                var info = ModuleHelper.GetModuleInfo(RivalModuleId);
                if (info != null && !string.IsNullOrEmpty(info.Name)) return info.Name;
            }
            catch { }
            return "卡拉迪亚封建军政";
        }

        internal static void TryPopup()
        {
            if (!Check() || _popupShown) return;
            _popupShown = true;
            try
            {
                string nm = RivalName();
                string body = "有【" + nm + "】没我，有我没【" + nm + "】，选一个！！！";
                InformationManager.ShowInquiry(new InquiryData("内政与经济扩展", body, true, false, "知道了", null, null, null, "", 0f, null, null, null), true, false);
                DLog.Force("已弹出冲突提示: " + body);
            }
            catch (Exception ex) { DLog.Force("弹窗失败: " + ex.Message); }
        }
    }
}
