using System;
using System.IO;

namespace FeudalInternalAffairs
{
    // 日志: Documents\Mount and Blade II Bannerlord\Configs\FeudalInternalAffairs_log.txt
    // 开关: Documents\Mount and Blade II Bannerlord\Configs\FeudalInternalAffairs_flags.txt
    //       可用: nostageskip / nonation / nohide / noruler / showall / realjoin / keepcreation / keepvideo
    //             econ(经济归因日志) / memo(缓存自检) / nothreads / nocolor
    //             colorstart=N / colorfull=N / hidecity=N(距离阈值)
    //       值开关: colorstart=90 / colorfull=20 / hidecity=700
    //       v4.277 新增(海军诊断):
    //             navydiag           打开"舰队移动诊断"(默认关闭; 每隔一段时间为每支舰队打一行日志)
    //             navydiagsec=3600   诊断间隔(游戏内秒; 缺省 3600 = 1 游戏日, 下限 60)
    //         日志里搜 "舰队诊断:" 即可看到 舰队id/任务/PlayerHold/IsCurrentlyAtSea/
    //         **PartyMoveMode(Hold 还是 Point)**/距目标距离与是否在靠近/最近一次命令。
    internal static class DLog
    {
        private static readonly object Sync = new object();
        private static string _filePath;
        private static string _flagsText;
        private static bool _flagsRead;

        private static string ConfigDir
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "Mount and Blade II Bannerlord", "Configs");
            }
        }

        private static string FilePath
        {
            get
            {
                if (_filePath == null)
                {
                    Directory.CreateDirectory(ConfigDir);
                    _filePath = Path.Combine(ConfigDir, "FeudalInternalAffairs_log.txt");
                }
                return _filePath;
            }
        }

        internal static bool Flag(string name)
        {
            try
            {
                if (!_flagsRead)
                {
                    _flagsRead = true;
                    string f = Path.Combine(ConfigDir, "FeudalInternalAffairs_flags.txt");
                    _flagsText = File.Exists(f) ? File.ReadAllText(f) : "";
                }
            }
            catch { _flagsText = ""; }
            return _flagsText != null && _flagsText.Contains(name);
        }

        // v4.283: 无缓存读取 —— 给"临时调试开关"用: 改完 flags 文件**不必重启游戏**就能生效。
        //   带 5 秒节流(Environment.TickCount), 避免被每帧/每小时路径反复读盘。
        //   Flag() 仍保留原缓存语义(正式开关照旧快), 两者互不影响。
        private static string _freshText;
        private static int _freshAt = int.MinValue;

        internal static bool FlagFresh(string name)
        {
            try
            {
                int now = Environment.TickCount;
                if (_freshText == null || unchecked(now - _freshAt) > 5000 || unchecked(now - _freshAt) < 0)
                {
                    _freshAt = now;
                    string f = Path.Combine(ConfigDir, "FeudalInternalAffairs_flags.txt");
                    _freshText = File.Exists(f) ? File.ReadAllText(f) : "";
                }
            }
            catch { _freshText = ""; }
            return _freshText != null && _freshText.Contains(name);
        }

        // 从 flags 文件读数值, 如 colorstart=90 / colorfull=20
        internal static float FlagFloat(string key, float def)
        {
            try
            {
                Flag("");
                if (string.IsNullOrEmpty(_flagsText)) return def;
                var m = System.Text.RegularExpressions.Regex.Match(_flagsText,
                    key + @"\s*=\s*([0-9]+(?:\.[0-9]+)?)");
                if (m.Success)
                {
                    float v;
                    if (float.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out v) && v > 0.1f)
                        return v;
                }
            }
            catch { }
            return def;
        }

        internal static void Info(string msg)
        {
            Write("    " + msg);
        }

        internal static void Force(string msg)
        {
            Write("[!] " + msg);
        }

        private static void Write(string msg)
        {
            try
            {
                lock (Sync)
                {
                    File.AppendAllText(FilePath, DateTime.Now.ToString("HH:mm:ss") + " " + msg + Environment.NewLine);
                }
            }
            catch { }
        }
    }
}
