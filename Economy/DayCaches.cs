using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem.Settlements;

namespace FeudalInternalAffairs
{
    // v4.269: 建筑产出链上的两个**纯只读**缓存(按日清空, 不改变任何结算语义)
    //   动机: TickSettlement 里每个建筑、每条产出都要重算一遍“输出乘子”, 每个输入/产出都要拆一次
    //   “a|b|c”备选商品表。实测这两处加起来占“逐定居点”那段的可观比例, 而它们的结果在(定居点,类别)
    //   或(字符串)层面完全相同 —— 纯粹的重复计算。
    //
    //   无副作用确认(逐条读过实现, 见报告):
    //     Guilds.OutputBonus        : 只读 Founded/Chartered 集合 + All 表 + Politics.LawLevel(数组读)
    //     Politics.OutputMult       : 只读 CivilWar/Laws 数组 + LawSystem/WarMobilization/PowerBlocs/Research 的纯函数
    //     Politics.SettlementOutputMult: 只读 s.OwnerClan + Lords 字典
    //     WarEconomy.OutputMultOf   : 只读 Scorched 字典(含一行“发现已过期就删掉该条目”的惰性清理, 见下方注释)
    //     TaxPolicy.IndustryMult    : 静态字段读(当日由“税制”步骤写, 而“税制”排在“建筑产出”之后)
    //     Infrastructure.AccessOf   : 只读人口/建筑表/铁路表
    //   —— 六个都是纯读(或至多幂等清理), 不写任何影响结算的状态, 不消耗 MBRandom, 不依赖调用次数/顺序。
    internal static class OutputMultCache
    {
        private static readonly Dictionary<string, float> _m = new Dictionary<string, float>(StringComparer.Ordinal);
        private static int _hit, _miss;
        private static bool _verifyChecked, _verify;
        private static long _mismatch;
        internal static int Hits { get { return _hit; } }
        internal static int Misses { get { return _miss; } }
        internal static int Count { get { return _m.Count; } }

        internal static void Clear()
        {
            _m.Clear();
            _hit = 0; _miss = 0;
        }

        // 等价于原来的:
        //   float guildMult = (1f + Guilds.OutputBonus(cat)) * Politics.OutputMult(cat)
        //                     * Politics.SettlementOutputMult(s) * WarEconomy.OutputMultOf(s.StringId);
        //   guildMult *= TaxPolicy.IndustryMult;
        //   guildMult *= Infrastructure.AccessOf(s.StringId);
        // 乘法顺序逐字保持 -> 浮点结果**逐位相同**。
        // 缓存键 = (定居点 StringId, 建筑类别): 同一键在**同一次日结内**输入不会变(见各函数说明),
        // 而每个定居点每天只被处理一次(RunPart 按下标切片, 不重复), 所以也不存在“跨片读到旧值”。
        internal static float Of(Settlement s, string catName)
        {
            if (s == null || string.IsNullOrEmpty(s.StringId)) return Raw(s, catName);   // 边界: 与原式同解(含原样的异常)
            string key = s.StringId + "\u0001" + (catName ?? "");
            float v;
            if (_m.TryGetValue(key, out v))
            {
                _hit++;
                // 自检(默认关闭, flags 里含 memo 时打开): 用缓存值 vs 现算值比对。
                //   注意 WarEconomy.OutputMultOf 里有一行"发现记录已过期就从 Scorched 删除"的惰性清理, 自检会
                //   多重放一次该幂等清理 —— 返回值不受影响。
                if (!_verifyChecked) { _verifyChecked = true; try { _verify = DLog.Flag("memo"); } catch { _verify = false; } }
                if (_verify)
                {
                    float fresh = Raw(s, catName);
                    if (fresh != v)
                    {
                        _mismatch++;
                        if (_mismatch <= 20)
                            DLog.Force("输出乘子缓存自检不一致: 缓存=" + v + " 现算=" + fresh
                                + " 差=" + (v - fresh) + " 城=" + s.StringId + " 类别=" + catName);
                    }
                }
                return v;
            }
            _miss++;
            v = Raw(s, catName);
            _m[key] = v;
            return v;
        }

        // 原表达式原样搬运(供缓存未命中与边界情况使用)
        private static float Raw(Settlement s, string cat)
        {
            float m = (1f + Guilds.OutputBonus(cat)) * Politics.OutputMult(cat) * Politics.SettlementOutputMult(s) * WarEconomy.OutputMultOf(s.StringId);
            m *= TaxPolicy.IndustryMult;
            m *= Infrastructure.AccessOf(s.StringId);
            return m;
        }
    }

    // v4.269: BuildDefs.Alternatives("a|b|c") 的结果缓存。
    //   原实现每次调用都 `new[] { good }` 或 `good.Split('|')` —— 也就是每个建筑每个输入/产出都分配一个数组。
    //   它是**纯函数**(只取决于传入字符串), 现有调用点(DailySettlement L551/645/668)只读 Length/[0]/foreach、
    //   从不修改返回数组, 所以按字符串缓存同一个数组实例是等价的(唯一差别是数组标识, 无人依赖)。
    internal static class GoodAltCache
    {
        private static readonly Dictionary<string, string[]> _a = new Dictionary<string, string[]>(StringComparer.Ordinal);
        private static readonly string[] _empty = new string[0];
        internal static int Count { get { return _a.Count; } }

        internal static void Clear() { _a.Clear(); }

        internal static string[] Of(string spec)
        {
            if (string.IsNullOrEmpty(spec)) return _empty;      // 与 BuildDefs.Alternatives 的返回同解(长度 0)
            string[] v;
            if (_a.TryGetValue(spec, out v)) return v;
            v = BuildDefs.Alternatives(spec);
            _a[spec] = v;
            return v;
        }
    }
}
