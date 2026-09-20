using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FeudalInternalAffairs
{
    // v4.85: 讲解弹窗"不再提示"标记; 文件持久化于 Configs 目录, 每个 id 一行
    internal static class TipState
    {
        private static readonly object Sync = new object();
        private static HashSet<string> _off;
        private static string _path;

        private static string FilePath
        {
            get
            {
                if (_path == null)
                {
                    string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                        "Mount and Blade II Bannerlord", "Configs");
                    Directory.CreateDirectory(dir);
                    _path = Path.Combine(dir, "FeudalInternalAffairs_tips.txt");
                }
                return _path;
            }
        }

        private static HashSet<string> Off
        {
            get
            {
                if (_off == null)
                {
                    _off = new HashSet<string>();
                    try
                    {
                        if (File.Exists(FilePath))
                            foreach (var line in File.ReadAllLines(FilePath))
                                if (!string.IsNullOrEmpty(line)) _off.Add(line.Trim());
                    }
                    catch { }
                }
                return _off;
            }
        }

        internal static bool Disabled(string id)
        {
            try { lock (Sync) { return Off.Contains(id); } }
            catch { return false; }
        }

        internal static void Disable(string id)
        {
            try
            {
                lock (Sync)
                {
                    if (!Off.Add(id)) return;
                    File.WriteAllLines(FilePath, new List<string>(Off), new UTF8Encoding(false));
                    DLog.Force("讲解提示已关闭: " + id);
                }
            }
            catch (Exception ex) { DLog.Force("讲解提示关闭失败: " + ex.Message); }
        }
    }
}
