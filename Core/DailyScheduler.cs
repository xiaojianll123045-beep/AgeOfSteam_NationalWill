using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using TaleWorlds.Library;

namespace FeudalInternalAffairs
{
    // v4.245: 每日结算调度器(用户: 每日结算搞多线程异步结算)
    //   问题: 跨日那一帧原本在主线程一口气跑完 20+ 个子系统(建筑产出/市场/税收/AI/政治/研究/军事...),
    //         整帧被占住 -> 明显卡顿(骑砍主线程一卡, 地图/相机/UI 全都停)。
    //   做法: ①把日结拆成**有序步骤**入队; ②每帧只花 FrameBudgetMs 毫秒推进队列, 剩下的留到下一帧
    //         (顺序严格不变, 所以结算语义与"一口气跑完"完全一致, 只是摊到若干帧);
    //         ③每步记录耗时, 日结完成后输出最慢的几步(方便下一轮把纯计算步骤搬到线程池);
    //         ④AddAsync: 纯计算步骤交给线程池算, 算完回主线程应用(绝不在后台线程碰游戏对象)。
    internal static class DailyScheduler
    {
        internal const float FrameBudgetMs = 3.0f;   // 每帧最多花多少毫秒做日结
        private const float LogThresholdMs = 8.0f;   // 总耗时超过这个值才写日志(免得每天刷屏)
        private const int LogTopN = 5;

        private sealed class Step
        {
            internal string Name = "";
            internal Action Job;
            internal float Ms;
            internal Task Async;          // AddAsync 用: 后台计算任务
        }

        private static readonly List<Step> _queue = new List<Step>();
        private static readonly Stopwatch _watch = new Stopwatch();
        private static int _idx;
        private static int _day = -1;
        private static bool _running;
        private static int _frames;

        internal static bool Running { get { return _running; } }
        internal static int Day { get { return _day; } }
        internal static int PendingCount { get { return _running ? Math.Max(0, _queue.Count - _idx) : 0; } }

        // 开始一天的结算(清空上次队列)
        internal static void Begin(int day)
        {
            try
            {
                _queue.Clear();
                _idx = 0;
                _frames = 0;
                _day = day;
                _running = true;
            }
            catch { }
        }

        // 登记一个步骤(严格按登记顺序执行)
        // 注: 若当前没有在跑的队列(经济侧已跑完/军事侧先到), 自动开一个新队列 —— 多个 Behavior 共用一个调度器
        internal static void Add(string name, Action job)
        {
            try
            {
                if (!_running) Begin(-1);
                _queue.Add(new Step { Name = name ?? "?", Job = job });
            }
            catch { }
        }

        // 纯计算步骤: compute 在线程池跑(禁止读写游戏对象), apply 回主线程执行(用计算结果写状态)
        internal static void AddAsync<T>(string name, Func<T> compute, Action<T> apply)
        {
            try
            {
                var step = new Step { Name = name ?? "?" };
                bool applied = false;
                T result = default(T);
                step.Job = delegate
                {
                    if (step.Async == null)
                    {
                        try
                        {
                            step.Async = Task.Run(delegate
                            {
                                try { result = compute != null ? compute() : default(T); } catch { }
                            });
                        }
                        catch { step.Async = null; }
                        if (step.Async == null)
                        {
                            // 线程池不可用(极端情况): 退回主线程同步算
                            T r0 = compute != null ? compute() : default(T);
                            if (apply != null) apply(r0);
                            return;
                        }
                        _idx--;          // 本帧先不应用, 下帧看结果
                        return;
                    }
                    if (!step.Async.IsCompleted) { _idx--; return; }   // 还没算完 -> 下帧再看(不阻塞主线程)
                    if (applied) return;
                    applied = true;
                    try { if (apply != null) apply(result); }
                    catch (Exception ex) { DLog.Force("日结异步步骤应用失败 " + step.Name + ": " + ex.Message); }
                };
                _queue.Add(step);
            }
            catch { }
        }

        // 每帧推进(主线程); 返回是否已全部完成
        internal static bool Tick()
        {
            if (!_running) return true;
            try
            {
                _frames++;
                _watch.Restart();
                while (_idx < _queue.Count)
                {
                    var s = _queue[_idx];
                    _idx++;
                    long t0 = _watch.ElapsedTicks;
                    try { if (s.Job != null) s.Job(); }
                    catch (Exception ex) { DLog.Force("日结步骤失败 " + s.Name + ": " + ex.Message); }
                    float ms = (float)((_watch.ElapsedTicks - t0) * 1000.0 / Stopwatch.Frequency);
                    if (ms > s.Ms) s.Ms = ms;   // AddAsync 会重复进入同一步, 取最大值
                    if (_watch.Elapsed.TotalMilliseconds >= FrameBudgetMs) return false;
                }
                _running = false;
                LogSummary();
                return true;
            }
            catch (Exception ex)
            {
                DLog.Force("日结调度异常: " + ex.Message);
                _running = false;
                return true;
            }
        }

        // 立刻把剩余步骤全部跑完(存档/读档/切换场景前调用, 避免半结算被写进档)
        internal static void Flush()
        {
            if (!_running) return;
            try
            {
                int guard = 0;
                while (_idx < _queue.Count && guard++ < 4096)
                {
                    var s = _queue[_idx];
                    _idx++;
                    if (s.Async != null)
                    {
                        try { s.Async.Wait(2000); } catch { }
                        if (!s.Async.IsCompleted) continue;   // 还没算完就放弃这一步(下次日结会重算)
                    }
                    try { if (s.Job != null) s.Job(); } catch { }
                }
                _running = false;
                DLog.Force("日结调度: Flush 强制跑完剩余 " + Math.Max(0, _queue.Count - _idx) + " 步");
            }
            catch { _running = false; }
        }

        internal static void Reset()
        {
            try { _queue.Clear(); _idx = 0; _running = false; _day = -1; } catch { }
        }

        private static void LogSummary()
        {
            try
            {
                float total = 0f;
                for (int i = 0; i < _queue.Count; i++) total += _queue[i].Ms;
                if (total < LogThresholdMs) return;
                var order = new List<Step>(_queue);
                order.Sort(delegate (Step a, Step b) { return b.Ms.CompareTo(a.Ms); });
                var sb = new System.Text.StringBuilder();
                sb.Append("日结: 第 ").Append(_day).Append(" 天 共 ").Append(_queue.Count)
                  .Append(" 步 ").Append((int)total).Append("ms, 分 ").Append(_frames).Append(" 帧跑完(每帧≤")
                  .Append((int)FrameBudgetMs).Append("ms) · 最慢: ");
                int n = Math.Min(LogTopN, order.Count);
                for (int i = 0; i < n; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(order[i].Name).Append(' ').Append((int)order[i].Ms).Append("ms");
                }
                DLog.Force(sb.ToString());
            }
            catch { }
        }
    }
}
