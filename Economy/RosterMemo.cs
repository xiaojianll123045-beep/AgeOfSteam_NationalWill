using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem.Roster;   // ItemRoster
using TaleWorlds.Core;                    // ItemObject / ItemRosterElement

namespace FeudalInternalAffairs
{
    // v4.269: 市场/人口结算的“物品数量整表快照 + 写路径增量同步”(多 roster 版)
    //   为什么要有这个类: v4.267 已在 DailySettlement.TickSettlement 里对**单个**市场 roster 做过同样的事,
    //   实测把原版 ItemRoster.GetItemNumber 的线性查找压掉 85%。但 MarketSim/PopSim 这一段要同时读写
    //   **很多**个 roster(每个城市场的 roster + 跨城调运的买卖双方), 没法钉一个“当前权威 roster”,
    //   所以这里做一份以 ItemRoster 为键的多表快照。
    //
    //   ---- 原版语义(ilspycmd 反编译 TaleWorlds.CampaignSystem.dll / TaleWorlds.Core.dll 逐行核对, 1.4.x) ----
    //     GetItemNumber(item)  = FindIndexOfItem(item): 线性找**第一个** EquipmentElement.Item == item(引用相等)的堆叠,
    //                            返回它的 Amount; 找不到返回 0。**不看 ItemModifier**。
    //     AddToCounts(item, n) : n==0 直接返回 -1(不变); 否则转 AddToCounts(new EquipmentElement(item), n)
    //                            —— EquipmentElement 的 ct 默认 itemModifier = null。
    //     AddToCounts(ee, n)   : i = FindIndexOfElement(ee), 而 IsEqualTo 是 Item==Item 且 ItemModifier==ItemModifier;
    //                            ① i<0 且 n<0 -> Debug.FailedAssert 且**什么都不做**(返回 -1);
    //                            ② i<0 且 n>0 -> 在**末尾**追加一个新堆叠(Amount 从 0 开始, 再加 n);
    //                            ③ i>=0 -> _data[i].Amount += n; 若结果 <=0 -> 用**最后一个元素**顶替 i 位置, _count--。
    //
    //   ---- 快照成立的两个前提(首次接触该 roster 时**全表扫一遍验证**, 有一条不满足就整个 roster 退回原版调用) ----
    //     ① 该 roster 内没有带 ItemModifier 的堆叠。否则 GetItemNumber 取到的“第一个堆叠”可能**不是**
    //        AddToCounts(ItemObject,int) 真正改的那个(modifier==null)堆叠 —— 读取口径与写入口径分叉;
    //     ② 同一种 ItemObject 在表里只有一个堆叠(无重复项)。否则“删堆叠时用最后一个元素顶替”可能把某物品的
    //        第二个堆叠搬到前面, 从而在**没人写这个物品**的情况下改变它的 GetItemNumber 返回值。
    //     两条前提由本 mod 的写入方式天然满足(写入一律走 AddToCounts(ItemObject,int), modifier==null,
    //     原版会把它合并进同一堆叠), 扫描只是**验证**, 不是假设。
    //
    //   ---- 第二层保险: 外部写入检测 ----
    //     ItemRoster.VersionNo 在原版每次 AddToCounts/Clear 后自增(UpdateVersion)。我们每次写完记录当时的值;
    //     读取前若发现版本与记录不符, 说明有**别的代码**直接写过这个 roster(商队损耗/原版/其它 mod/事件回调),
    //     立刻放掉该 roster 的快照并回退原版调用, 且本日不再重建 —— 宁慢勿错。
    //
    //   ---- 等价性结论 ----
    //     对被缓存的 roster, Count/Take/Give 与直接调用 GetItemNumber/AddToCounts 得到**逐次相同**的返回值与
    //     相同的 roster 结果。真实写入一律照原样执行(所以原版的 Amount 下溢检查、TotalFood/TotalValue 缓存统计、
    //     FoodVariety、RosterUpdatedEvent、VersionNo 全部与改前一致), 快照只是**额外镜像**一份数量;
    //     本类绝不代替原版写入, 只镜像它的效果。
    //     残余边界(无法在此证明, 但可用 flags 里的 memo 开自检):
    //       ⅰ 【已查明, 基本排除】"AddToCounts 内部被事件回调重入后再写同一个 roster":
    //         反编译整个 TaleWorlds.CampaignSystem.dll 后全仓只有一个订阅点 —— SettlementComponent.Owner 的
    //         `_owner.ItemRoster.RosterUpdatedEvent += OnInventoryUpdated`(Settlement.ItemRoster 就是
    //         Party.ItemRoster, 即我们写的那些市场 roster)。该回调: 村庄/城堡用基类空实现;
    //         城镇 Town.OnInventoryUpdated -> TownMarketData.OnTownInventoryUpdated, 只更新
    //         TownMarketData 的分类在库/供需聚合(SetItemData/AddNumberInStore), **不写回 ItemRoster**。
    //         因为本类从不代替原版写入(AddToCounts 一律照原样执行), 这些原版副作用与改前完全一致。
    //         剩下的可能只有"第三方 mod 在 RosterUpdatedEvent 里回写同一个 roster", 那种情况下
    //         ⅰ 的面板自检(memo)会立刻报"快照自检不一致"。
    //       ⅱ 若别的代码把 _data 里的 modifier 就地改掉而不动 VersionNo —— 原版也没有这种写法。
    internal static class RosterMemo
    {
        private sealed class Slot
        {
            internal Dictionary<ItemObject, int> Map;   // 物品 -> 数量(= GetItemNumber 口径); null = 该 roster 不可缓存
            internal int Ver;                           // 期待的原版 VersionNo(我们自己的写入也算在内)
        }

        private static readonly Dictionary<ItemRoster, Slot> _slots = new Dictionary<ItemRoster, Slot>();
        private static readonly Dictionary<ItemObject, byte> _seen = new Dictionary<ItemObject, byte>();
        private static bool _active;

        // 归因统计(只给日志用)
        internal static long Hits, Miss;
        internal static int Rosters, Entries, Skipped;

        // 自检(flags 里含 "memo" 时, 每次读取都与真实 GetItemNumber 比对)
        private static bool _verifyChecked, _verify;
        private static long _mismatch;

        internal static void Begin()
        {
            _slots.Clear();
            _active = true;
            Hits = 0; Miss = 0; Rosters = 0; Entries = 0; Skipped = 0;
        }

        internal static void End()
        {
            _slots.Clear();
            _active = false;
        }

        // 首次接触: 一次全表扫描建"物品 -> 数量"表, 并同时验证两个前提 + 记录版本号
        private static Slot SlotFor(ItemRoster r)
        {
            Slot s;
            if (_slots.TryGetValue(r, out s))
            {
                if (s.Map == null) return null;                       // 已判定不可缓存
                if (r.VersionNo != s.Ver)                             // 中间被外部代码写过 -> 放掉(本日不重建)
                {
                    s.Map = null;
                    Skipped++;
                    return null;
                }
                return s;
            }

            s = new Slot();
            var map = new Dictionary<ItemObject, int>();
            bool pure = true;
            int n = r.Count;
            _seen.Clear();
            for (int i = 0; i < n; i++)
            {
                var el = r[i];
                var it = el.EquipmentElement.Item;
                if (it == null) continue;                             // 空槽(_count 之内理论上不会有)
                if (el.EquipmentElement.ItemModifier != null) { pure = false; break; }   // 前提①
                if (_seen.ContainsKey(it)) { pure = false; break; }                        // 前提②
                _seen[it] = 1;
                int a = el.Amount;
                if (a > 0) map[it] = a;                               // 原版口径: 数量 <=0 的堆叠已经不存在
            }
            if (!pure) { s.Map = null; Skipped++; }
            else { s.Map = map; Rosters++; Entries += map.Count; }
            s.Ver = r.VersionNo;
            _slots[r] = s;
            return s.Map != null ? s : null;
        }

        // 读取: 等价于 roster.GetItemNumber(item)
        internal static int Count(ItemRoster roster, ItemObject item)
        {
            if (item == null) return 0;
            if (roster == null) { Miss++; return 0; }
            Slot s = _active ? SlotFor(roster) : null;
            if (s == null) { Miss++; return roster.GetItemNumber(item); }
            int v;
            if (!s.Map.TryGetValue(item, out v)) v = 0;               // 表是全表建的 -> 没有条目就等于原版返回 0
            Hits++;
            Verify(roster, item, v);
            return v;
        }

        // 写入(扣减): 等价于 roster.AddToCounts(item, -n)
        internal static void Take(ItemRoster roster, ItemObject item, int n)
        {
            if (item == null || n == 0) return;
            if (n < 0) { Give(roster, item, -n); return; }
            if (roster == null) return;
            Slot s = _active ? SlotFor(roster) : null;
            roster.AddToCounts(item, -n);                             // 真实写入原样执行(原版所有副作用保持不变)
            if (s == null || s.Map == null) return;                   // 写入过程中被判废(事件回调重入) -> 不镜像
            int v;
            if (s.Map.TryGetValue(item, out v) && v > n) s.Map[item] = v - n;
            else s.Map.Remove(item);                                  // 扣光/扣穿 -> 原版会把这个堆叠删掉 -> 数量 0
            s.Ver = roster.VersionNo;                                 // 写入后再取版本(含事件回调里的写入)
            Verify(roster, item, v > n ? v - n : 0);
        }

        // 写入(入库): 等价于 roster.AddToCounts(item, n)
        internal static void Give(ItemRoster roster, ItemObject item, int n)
        {
            if (item == null || n == 0) return;
            if (n < 0) { Take(roster, item, -n); return; }
            if (roster == null) return;
            Slot s = _active ? SlotFor(roster) : null;
            roster.AddToCounts(item, n);
            if (s == null || s.Map == null) return;                   // 写入过程中被判废 -> 不镜像
            int v;
            if (!s.Map.TryGetValue(item, out v)) v = 0;               // 没有堆叠 -> 原版追加新堆叠, 数量就是 n
            s.Map[item] = v + n;
            s.Ver = roster.VersionNo;
            Verify(roster, item, v + n);
        }

        // flags 含 memo 时逐次自检(默认关闭; 只读比对, 不影响结算)
        private static void Verify(ItemRoster r, ItemObject it, int mine)
        {
            if (!_verifyChecked) { _verifyChecked = true; try { _verify = DLog.Flag("memo"); } catch { _verify = false; } }
            if (!_verify) return;
            int live = r.GetItemNumber(it);
            if (live != mine)
            {
                _mismatch++;
                if (_mismatch <= 20)
                    DLog.Force("市场roster快照自检不一致: 快照=" + mine + " 实际=" + live
                        + " 差=" + (mine - live) + " 物品=" + (it.StringId ?? "?"));
            }
        }
    }
}
