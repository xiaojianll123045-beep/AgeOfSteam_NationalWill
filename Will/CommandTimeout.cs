using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Library;

namespace FeudalInternalAffairs
{
    // 指挥超时: 被玩家下过命令的部队会禁止 AI 自行决策; 如果 10 秒内没有新命令,
    // 就放它自由(AI 恢复自主行动), 免得整支军队一直呆在原地。
    // 计时用游戏时间(暂停时不走), 所以"10秒"是游戏内秒数。
    // v4.88: 移动类命令(点地面)保存目标点并每 1 秒重发一次(对抗 AI 覆盖/被重置导致原地不动),
    //        释放时输出"命令后已移动多少米"用于诊断。
    internal static class CommandTimeout
    {
        private const float TimeoutSeconds = 10f;
        private const float RefireSeconds = 1f;

        private static readonly Dictionary<MobileParty, float> Timers = new Dictionary<MobileParty, float>();
        private static readonly Dictionary<MobileParty, CampaignVec2> Points = new Dictionary<MobileParty, CampaignVec2>();
        private static readonly Dictionary<MobileParty, float> Refire = new Dictionary<MobileParty, float>();
        private static readonly Dictionary<MobileParty, CampaignVec2> StartPos = new Dictionary<MobileParty, CampaignVec2>();
        private static readonly Dictionary<MobileParty, float> Still = new Dictionary<MobileParty, float>();
        private static readonly Dictionary<MobileParty, CampaignVec2> LastPos = new Dictionary<MobileParty, CampaignVec2>();
        private static readonly HashSet<MobileParty> Piloting = new HashSet<MobileParty>();
        private static bool _pilotLogged;
        private static bool _pilotDiag;
        private static bool _arriveLogged;
        private static float _speedLogTimer;   // v4.246: 指挥诊断节流(每 3 秒)
        private static readonly List<MobileParty> Finished = new List<MobileParty>();

        // 每次下命令时刷新计时
        internal static void Touch(MobileParty p)
        {
            try
            {
                if (p == null || !p.IsActive) return;
                Timers[p] = TimeoutSeconds;
            }
            catch { }
        }

        // v4.88: 带目标点的移动命令 -> 超时前每秒重发, 防止部队被 AI/系统重置后原地不动
        internal static void Touch(MobileParty p, CampaignVec2 point)
        {
            Touch(p);
            try
            {
                if (p == null || !p.IsActive) return;
                Points[p] = point;
                Refire[p] = RefireSeconds;
                StartPos[p] = p.Position;
                _pilotDiag = false;   // 新命令 -> 下次驾驶检查输出一次诊断
            }
            catch { }
        }

        internal static void Tick(float dt)
        {
            if (Timers.Count == 0) return;
            try
            {
                // v4.246 诊断: 指挥期间每 3 秒打印一次各部队的"速度值 + 驱动方式 + 人数",
                //   用于确认国防军速度是否恒为 3.0(用户反馈"人多的快、人少的慢")
                try
                {
                    _speedLogTimer += dt;
                    if (_speedLogTimer >= 3f && Points.Count > 0)
                    {
                        _speedLogTimer = 0f;
                        var sb = new System.Text.StringBuilder("指挥诊断: ");
                        foreach (var kv in Points)
                        {
                            var pp = kv.Key;
                            if (pp == null || !pp.IsActive) continue;
                            int men = 0;
                            try { men = pp.MemberRoster != null ? pp.MemberRoster.TotalManCount : 0; } catch { }
                            float spd = 0f;
                            try { spd = pp.Speed; } catch { }
                            sb.Append(MapSelection.NameOf(pp)).Append(" 速度=").Append(spd.ToString("F2"))
                              .Append(Piloting.Contains(pp) ? "[手动驾驶]" : "[native]")
                              .Append(" 人数=").Append(men).Append("  |  ");
                        }
                        DLog.Force(sb.ToString());
                    }
                }
                catch { }
                Finished.Clear();
                foreach (var kv in Timers)
                {
                    float left = kv.Value - dt;
                    if (left <= 0f) Finished.Add(kv.Key);
                    else Timers[kv.Key] = left;
                }
                foreach (var p in Finished)
                {
                    // 还在选中状态 = 玩家还拿着它, 不放
                    if (MapSelection.Is(p))
                    {
                        Timers[p] = 1f;   // 1 秒后再看
                        continue;
                    }
                    // 军团成员: 军团在管着它(走向军团长/跟随军团), 别放给 AI 乱跑
                    // (离开军团后 p.Army 变 null, 下一秒就会正常放行)
                    try
                    {
                        if (p != null && p.IsActive && p.Army != null)
                        {
                            Timers[p] = 1f;
                            continue;
                        }
                    }
                    catch { }
                    Timers.Remove(p);
                    float moved = -1f;
                    CampaignVec2 s;
                    if (p != null && StartPos.TryGetValue(p, out s))
                    {
                        try { moved = p.Position.ToVec2().Distance(s.ToVec2()); } catch { }
                    }
                    StartPos.Remove(p);
                    // v4.96: 保留 Points/驾驶状态 -> 手动驾驶持续到"到达目标"才结束(不会被半路丢弃)
                    try
                    {
                        if (p != null && p.IsActive)
                        {
                            p.Ai.SetDoNotMakeNewDecisions(false);
                            DLog.Force("指挥超时: " + MapSelection.NameOf(p) + " 恢复自由行动"
                                + (moved >= 0f ? "(命令后已移动 " + (int)moved + " 米)" : "")
                                + (Points.ContainsKey(p) ? " [继续驾驶到目标]" : ""));
                        }
                    }
                    catch { }
                }

                // v4.88: 每 1 秒重发一次移动目标(对抗 AI 覆盖/被重置)
                if (Refire.Count > 0)
                {
                    var keys = new List<MobileParty>(Refire.Keys);
                    for (int i = 0; i < keys.Count; i++)
                    {
                        var p = keys[i];
                        float left;
                        if (!Refire.TryGetValue(p, out left)) continue;
                        left -= dt;
                        if (left > 0f) { Refire[p] = left; continue; }
                        Refire[p] = RefireSeconds;
                        CampaignVec2 pt;
                        if (p == null || !p.IsActive || !Points.TryGetValue(p, out pt))
                        {
                            Refire.Remove(p); Points.Remove(p); StartPos.Remove(p);
                            continue;
                        }
                        try
                        {
                            p.SetMoveGoToPoint(pt, MobileParty.NavigationType.Default);
                        }
                        catch { }
                    }
                }

                // v4.93: 手动驾驶兜底 —— native 不驱动时(0.5 秒不动), 直接朝目标推进位置
                if (Points.Count > 0)
                {
                    var pkeys = new List<MobileParty>(Points.Keys);
                    for (int i = 0; i < pkeys.Count; i++)
                    {
                        var p = pkeys[i];
                        if (p == null || !p.IsActive) { Still.Remove(p); LastPos.Remove(p); Piloting.Remove(p); continue; }
                        CampaignVec2 target;
                        if (!Points.TryGetValue(p, out target)) continue;
                        var cur = p.Position;
                        if (!_pilotDiag)
                        {
                            _pilotDiag = true;
                            DLog.Force("驾驶诊断: 点数=" + Points.Count + " 计时=" + Timers.Count + " 活跃=" + p.IsActive
                                + " 位置=(" + (int)cur.X + "," + (int)cur.Y + ") 目标=(" + (int)target.X + "," + (int)target.Y + ")");
                        }
                        float dist = cur.ToVec2().Distance(target.ToVec2());
                        if (dist < 1.5f)   // 已到达
                        {
                            Still[p] = 0f;
                            Piloting.Remove(p);
                            // v4.96: 巡逻任务 -> 到达后自动换一个新目标(绕驻地巡逻)
                            var lg = DefArmy.LegionOf(p);
                            if (lg != null && lg.Task != null && lg.Task.Contains("巡逻"))
                            {
                                CampaignVec2 np;
                                if (DefArmy.TryPatrolPoint(lg, out np)) { Points[p] = np; Refire[p] = 0.5f; continue; }
                            }
                            // 其它任务/点命令: 到达即完成, 清空驾驶状态
                            Points.Remove(p); Refire.Remove(p); StartPos.Remove(p); LastPos.Remove(p);
                            if (!_arriveLogged)
                            {
                                _arriveLogged = true;
                                DLog.Force("移动完成: 已到达目标 " + MapSelection.NameOf(p));
                            }
                            continue;
                        }
                        if (!Piloting.Contains(p))
                        {
                            float moved = -1f;
                            CampaignVec2 last;
                            if (LastPos.TryGetValue(p, out last)) moved = cur.ToVec2().Distance(last.ToVec2());
                            LastPos[p] = cur;
                            float st;
                            Still.TryGetValue(p, out st);
                            if (moved >= 0f && moved > 0.002f) { Still[p] = 0f; continue; }   // native 在驱动, 不插手
                            st += dt;
                            if (st < 0.5f) { Still[p] = st; continue; }   // v4.94: 0.5 秒未动即接管
                            Piloting.Add(p);
                            Still[p] = 0.5f;
                            if (!_pilotLogged)
                            {
                                _pilotLogged = true;
                                DLog.Force("移动兜底: 0.5秒未动, 已接管驾驶 " + MapSelection.NameOf(p));
                            }
                        }
                        else
                        {
                            // v4.246: 已被我们接管后每帧仍要检查 native 是否恢复驱动 ——
                            //   原来接管后不再检查, native 一恢复就变成"native + 手动"双倍速度
                            //   (用户: 人多的国防军部队速度快, 人少的又慢)
                            float moved2 = -1f;
                            CampaignVec2 last2;
                            if (LastPos.TryGetValue(p, out last2)) moved2 = cur.ToVec2().Distance(last2.ToVec2());
                            LastPos[p] = cur;
                            if (moved2 > 0.002f)
                            {
                                Piloting.Remove(p);   // native 已恢复 -> 交回驾驶权, 不叠加
                                Still[p] = 0f;
                                continue;
                            }
                        }
                        // 手动推进: 严格按 3.0/秒 × 帧时间
                        // (原来写成 Math.Max(0.5f, SpeedLock*dt) —— 每帧至少 0.5 米, 60fps 下等于 30 米/秒,
                        //  被接管的部队会"飞快", 没被接管的按 3.0/秒, 这就是速度看起来不一致的另一半原因)
                        float step = DefArmy.SpeedLock * dt;
                        if (step > 3f) step = 3f;      // dt 异常时也不许瞬移
                        if (step <= 0f) continue;
                        if (step > dist) step = dist;
                        var d = target.ToVec2() - cur.ToVec2();
                        float len = d.Length;
                        if (len > 0.01f)
                        {
                            var nv = cur.ToVec2() + d * (step / len);
                            try
                            {
                                p.Position = new CampaignVec2(nv, true);
                                LastPos[p] = p.Position;
                            }
                            catch { }
                        }
                    }
                }
            }
            catch { }
        }

        // v4.105: 是否处于指挥窗口(有待命命令/重发中)
        internal static bool IsCommanded(MobileParty p)
        {
            try { return p != null && (Timers.ContainsKey(p) || Points.ContainsKey(p)); }
            catch { return false; }
        }

        internal static void Clear()
        {
            Timers.Clear();
            Points.Clear();
            Refire.Clear();
            StartPos.Clear();
            Still.Clear();
            LastPos.Clear();
            Piloting.Clear();
        }
    }
}
