using System;
using System.IO;

namespace FeudalInternalAffairs
{
    // 日志: Documents\Mount and Blade II Bannerlord\Configs\FeudalInternalAffairs_log.txt
    // 开关: Documents\Mount and Blade II Bannerlord\Configs\FeudalInternalAffairs_flags.txt
    //       可用: nostageskip / nonation / nohide / noruler / showall / realjoin
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
