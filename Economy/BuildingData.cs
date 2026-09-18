using System;
using System.Collections.Generic;

namespace FeudalInternalAffairs
{
    // 在建队列项(每个元素 = 1 个单位; 每定居点一条队列, 最多 5 项)
    internal class QueuedBuild
    {
        internal string DefId;
        internal int Progress;    // 已投入建造点
        internal int TotalWork;   // 总工时(由类别与模式决定)
        internal bool Paused;
        internal BuildMode Mode;  // 建造时选的模式(完工后按此模式入库)

        internal QueuedBuild() { }

        internal QueuedBuild(string defId, int totalWork, BuildMode mode)
        {
            DefId = defId;
            TotalWork = totalWork;
            Mode = mode;
        }

        internal BuildDef Def { get { return BuildDefs.Get(DefId); } }

        internal int Percent
        {
            get { return TotalWork > 0 ? (int)Math.Min(100, Progress * 100L / TotalWork) : 0; }
        }
    }

    // 建筑组: 一个定居点里已建的一种建筑(数量堆叠, 模式对该组全部数量生效)
    internal class BuildingGroup
    {
        internal string DefId;
        internal int Count;       // 已建数量
        internal BuildMode Mode;

        // 当日状态(每日重算, 不入存档)
        internal bool Stalled;
        internal string StallReason;

        // v3.0 建筑经营状态(文档 19.7; 存档随建筑组序列化)
        internal float Cash;          // 现金储备(第纳尔, 可负)
        internal float WageMult = 1f; // 工资出价系数 0.6~2.0
        internal float Fill = 1f;     // 雇佣到岗率 0~1(默认 1: 尚未结算前按满产)
        internal float Margin;        // 上期利润率(用于下期工资出价)
        internal int BankruptDays;    // 连续资不抵债天数(>=90 停业)
        internal int Owner = -1;      // v4.0 所有权(文档 20.4): -1=按默认推导 0王/1领/2教/3行/4私

        internal BuildingGroup() { }

        internal BuildingGroup(string defId, int count, BuildMode mode)
        {
            DefId = defId;
            Count = count;
            Mode = mode;
        }

        internal BuildDef Def { get { return BuildDefs.Get(DefId); } }

        internal int Slots { get { return Count; } }
    }

    // 一个定居点的全部建筑(已建组 + 在建队列)
    internal class SettlementBuildings
    {
        internal string SettlementId;
        internal List<BuildingGroup> Groups = new List<BuildingGroup>();
        internal List<QueuedBuild> Queue = new List<QueuedBuild>();

        internal SettlementBuildings() { }

        internal SettlementBuildings(string id)
        {
            SettlementId = id;
        }

        internal BuildingGroup Find(string defId)
        {
            for (int i = 0; i < Groups.Count; i++) if (Groups[i].DefId == defId) return Groups[i];
            return null;
        }

        internal BuildingGroup GetOrCreate(string defId, BuildMode mode)
        {
            var g = Find(defId);
            if (g == null)
            {
                g = new BuildingGroup(defId, 0, mode);
                Groups.Add(g);
            }
            return g;
        }

        // 已建 + 在建 都占槽位
        internal int UsedSlots { get { return BuiltCount + QueuedCount; } }

        internal int BuiltCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < Groups.Count; i++) n += Groups[i].Count;
                return n;
            }
        }

        internal int QueuedCount { get { return Queue.Count; } }

        internal int PausedCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < Queue.Count; i++) if (Queue[i].Paused) n++;
                return n;
            }
        }

        // 某定义的在建数量
        internal int QueuedOf(string defId)
        {
            int n = 0;
            for (int i = 0; i < Queue.Count; i++) if (Queue[i].DefId == defId) n++;
            return n;
        }

        // 清理空组(数量 0)
        internal void Prune()
        {
            for (int i = Groups.Count - 1; i >= 0; i--)
                if (Groups[i].Count <= 0) Groups.RemoveAt(i);
        }
    }

    // 建筑通用规则
    internal static class BuildingRules
    {
        // 槽位上限(2.3 / 12.1)
        internal static int SlotLimit(bool isTown, bool isCastle, int prosperity, int hearth)
        {
            if (isTown) return Math.Min(12, 4 + prosperity / 1000);
            if (isCastle) return Math.Min(8, 3 + prosperity / 1500);
            return Math.Min(6, 2 + hearth / 400);
        }

        internal const int MaxQueuePerSettlement = 5;   // 每定居点最多 5 项在建(3.3)

        internal static int SlotLimitOf(TaleWorlds.CampaignSystem.Settlements.Settlement s)
        {
            try
            {
                if (s == null) return 0;
                if (s.IsVillage)
                {
                    var v = s.Village;
                    return SlotLimit(false, false, 0, v != null ? (int)v.Hearth : 0);
                }
                var t = s.Town;
                return SlotLimit(s.IsTown, s.IsCastle, t != null ? (int)t.Prosperity : 0, 0);
            }
            catch { return 0; }
        }
    }
}
