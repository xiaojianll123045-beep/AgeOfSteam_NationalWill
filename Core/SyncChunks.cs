using System;
using System.Text;
using TaleWorlds.CampaignSystem;
using TaleWorlds.SaveSystem;

namespace FeudalInternalAffairs
{
    // 存档字符串分块存取(重要修复):
    // 原版 SaveSystem 对"单个 SyncData 字符串"有约 32KB 的隐性上限,
    // 超过后读档会在对象数据解析阶段直接报"数组维度超过了支持的范围"。
    // 本 mod 的 建筑/人口/市场 等数据很容易超过该上限 -> 统一切成 <= 24KB 的小块存取。
    // 用法: 保存与读取的调用序列必须一致(块数从存档读出后按相同顺序读写)。
    internal static class SyncChunks
    {
        internal const int ChunkSize = 24000;

        // 保存: 把 data 切块写入 key_0..key_{n-1}, 块数写入 key_n
        internal static void Save(IDataStore ds, string key, string data)
        {
            try
            {
                if (string.IsNullOrEmpty(data))
                {
                    int zero = 0;
                    ds.SyncData(key + "_n", ref zero);
                    return;
                }
                int n = (data.Length + ChunkSize - 1) / ChunkSize;
                int count = n;
                ds.SyncData(key + "_n", ref count);
                for (int i = 0; i < n; i++)
                {
                    string part = data.Substring(i * ChunkSize, Math.Min(ChunkSize, data.Length - i * ChunkSize));
                    ds.SyncData(key + "_" + i, ref part);
                }
            }
            catch (Exception ex) { DLog.Force("分块存档失败(" + key + "): " + ex.Message); }
        }

        // 读取: 按块数拼接
        internal static string Load(IDataStore ds, string key)
        {
            try
            {
                int n = 0;
                ds.SyncData(key + "_n", ref n);
                if (n <= 0) return "";
                if (n > 4096) n = 4096;   // 防御: 异常块数
                var sb = new StringBuilder(n * ChunkSize);
                for (int i = 0; i < n; i++)
                {
                    string part = "";
                    ds.SyncData(key + "_" + i, ref part);
                    if (!string.IsNullOrEmpty(part)) sb.Append(part);
                }
                return sb.ToString();
            }
            catch (Exception ex) { DLog.Force("分块读档失败(" + key + "): " + ex.Message); return ""; }
        }
    }
}
