using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem.Settlements;

namespace FeudalInternalAffairs
{
    // 方案里的一项: 建筑 + 数量
    internal class PlanItem
    {
        internal string DefId;
        internal int Count;

        internal BuildDef Def { get { return BuildDefs.Get(DefId); } }
    }

    // 批量建造模式(设计 10.3): 笔 = 建造方案(可带多个建筑)
    // 进入后: 右键点击领地 -> 把方案尽量塞进该定居点的队列; Ctrl+Z 撤销; ESC 退出
    // 注意: 类名不能叫 BuildMode(与 BuildingDefs.cs 里的 BuildMode 枚举冲突)
    internal static class BatchBuild
    {
        private static readonly List<PlanItem> _plan = new List<PlanItem>();
        private static readonly List<UndoEntry> _undo = new List<UndoEntry>();

        internal class UndoEntry
        {
            internal string SettlementId;
            internal int Count;
        }

        internal static bool Active { get; private set; }
        internal static List<PlanItem> Plan { get { return _plan; } }

        internal static int PlanTotal
        {
            get
            {
                int n = 0;
                foreach (var p in _plan) n += p.Count;
                return n;
            }
        }

        internal static void Toggle()
        {
            if (Active) Exit(); else Enter();
        }

        internal static void Enter()
        {
            try
            {
                if (Active) return;
                Active = true;
                BuildPanel.Open();
                MapSelection.Message("建造模式: 右键点击领地排队建造 (Ctrl+Z 撤销, ESC 退出)");
                DLog.Force("建造模式: 进入");
            }
            catch (Exception ex) { DLog.Force("进入建造模式失败: " + ex.Message); }
        }

        internal static void Exit()
        {
            try
            {
                if (!Active) return;
                Active = false;
                BuildPanel.Close();
                DLog.Force("建造模式: 退出");
            }
            catch { }
        }

        // ================= 方案编辑 =================
        internal static void Add(string defId)
        {
            try
            {
                if (string.IsNullOrEmpty(defId)) return;
                foreach (var p in _plan) if (p.DefId == defId) { p.Count++; BuildPanel.Refresh(); return; }
                _plan.Add(new PlanItem { DefId = defId, Count = 1 });
                BuildPanel.Refresh();
            }
            catch { }
        }

        internal static void Change(string defId, int delta)
        {
            try
            {
                for (int i = 0; i < _plan.Count; i++)
                {
                    if (_plan[i].DefId != defId) continue;
                    _plan[i].Count += delta;
                    if (_plan[i].Count <= 0) _plan.RemoveAt(i);
                    BuildPanel.Refresh();
                    return;
                }
            }
            catch { }
        }

        internal static void Remove(string defId)
        {
            try
            {
                for (int i = 0; i < _plan.Count; i++)
                    if (_plan[i].DefId == defId) { _plan.RemoveAt(i); BuildPanel.Refresh(); return; }
            }
            catch { }
        }

        internal static void Clear()
        {
            try { _plan.Clear(); BuildPanel.Refresh(); }
            catch { }
        }

        // ================= 预设(3 个槽位, 存 FIA_Brush) =================
        internal static string PresetText(int index)
        {
            try
            {
                var list = EconomyWorld.Brushes;
                if (index < 0 || index >= 3) return "预设" + (index + 1);
                if (index >= list.Count || list[index] == null || list[index].DefIds.Count == 0)
                    return "预设" + (index + 1) + "(空)";
                return "预设" + (index + 1) + "(" + list[index].DefIds.Count + ")";
            }
            catch { return "预设" + (index + 1); }
        }

        // 点击预设: 空则保存当前方案, 非空则载入
        internal static void ClickPreset(int index)
        {
            try
            {
                if (index < 0 || index >= 3) return;
                var list = EconomyWorld.Brushes;
                while (list.Count <= index) list.Add(new BrushPreset { Name = "预设" + (list.Count + 1) });
                var b = list[index];
                if (b.DefIds.Count == 0)
                {
                    b.DefIds.Clear();
                    foreach (var p in _plan)
                        for (int i = 0; i < p.Count; i++) b.DefIds.Add(p.DefId);
                    MapSelection.Message("已保存到 " + b.Name + " (" + b.DefIds.Count + " 项)");
                }
                else
                {
                    _plan.Clear();
                    var counts = new Dictionary<string, int>();
                    foreach (var d in b.DefIds)
                    {
                        int c;
                        counts.TryGetValue(d, out c);
                        counts[d] = c + 1;
                    }
                    foreach (var kv in counts) _plan.Add(new PlanItem { DefId = kv.Key, Count = kv.Value });
                    MapSelection.Message("已载入 " + b.Name);
                }
                BuildPanel.Refresh();
            }
            catch (Exception ex) { DLog.Force("预设操作失败: " + ex.Message); }
        }

        // ================= 排队 =================
        internal static void QueueAtCursor()
        {
            try
            {
                if (!Active) return;
                var pt = MapBoxSelect.CaptureGroundPoint();
                var s = TerritoryData.SettlementAt(pt.x, pt.y);
                if (s == null)
                {
                    // 光标没落在领地上 -> 试试光标下的定居点图标
                    var map = SandBox.View.Map.MapScreen.Instance;
                    var vis = map != null ? map.CurrentVisualOfTooltip as SandBox.View.Map.Visuals.SettlementVisual : null;
                    s = vis != null && vis.MapEntity != null ? vis.MapEntity.Settlement : null;
                }
                if (s == null) { MapSelection.Message("这里不是任何定居点的领地"); return; }
                QueueAt(s);
            }
            catch (Exception ex) { DLog.Force("批量排队失败: " + ex.Message); }
        }

        internal static void QueueAt(Settlement s)
        {
            try
            {
                if (s == null) return;
                if (_plan.Count == 0) { MapSelection.Message("方案是空的: 先在建造面板里加建筑"); return; }
                var sb = EconomyWorld.Of(s.StringId);
                int limit = BuildingRules.SlotLimitOf(s);
                int used = sb.UsedSlots;
                int queued = 0;
                foreach (var p in _plan)
                {
                    var def = p.Def;
                    if (def == null) continue;
                    if (!BuildDefs.AllowedAt(def, s.IsVillage, s.IsCastle, s.IsTown)) continue;   // 地点不符跳过
                    for (int i = 0; i < p.Count; i++)
                    {
                        if (used >= limit) break;
                        if (sb.Queue.Count >= BuildingRules.MaxQueuePerSettlement) break;
                        sb.Queue.Add(new QueuedBuild(p.DefId, BuildDefs.WorkHours(def, BuildMode.Wood), BuildMode.Wood));
                        used++;
                        queued++;
                    }
                }
                if (queued > 0)
                {
                    _undo.Add(new UndoEntry { SettlementId = s.StringId, Count = queued });
                    MapSelection.Message("已排队 " + queued + " 项到 " + s.Name + " (占用 " + used + "/" + limit + ")");
                    DLog.Force("批量建造: " + s.StringId + " 排队 " + queued + " 项");
                }
                else
                {
                    MapSelection.Message(s.Name + ": 无法排队(槽位已满 " + used + "/" + limit + " 或地点不符)");
                }
            }
            catch (Exception ex) { DLog.Force("排队失败: " + ex.Message); }
        }

        internal static void Undo()
        {
            try
            {
                if (_undo.Count == 0) { MapSelection.Message("没有可撤销的排队"); return; }
                var e = _undo[_undo.Count - 1];
                _undo.RemoveAt(_undo.Count - 1);
                var sb = EconomyWorld.Find(e.SettlementId);
                if (sb != null)
                {
                    for (int i = 0; i < e.Count && sb.Queue.Count > 0; i++)
                        sb.Queue.RemoveAt(sb.Queue.Count - 1);
                }
                MapSelection.Message("已撤销上一次排队");
                DLog.Force("批量建造: 撤销 " + e.SettlementId + " 的 " + e.Count + " 项");
            }
            catch (Exception ex) { DLog.Force("撤销失败: " + ex.Message); }
        }
    }
}
