using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Library;

namespace FeudalInternalAffairs
{
    // 地图上的"当前选中部队"(支持多选: 框选/逐个点选)
    internal static class MapSelection
    {
        private static readonly List<MobileParty> SelectedParties = new List<MobileParty>();

        // 主选部队(第一个)
        internal static MobileParty Selected
        {
            get
            {
                Prune();
                return SelectedParties.Count > 0 ? SelectedParties[0] : null;
            }
        }

        // ==================== v4.279(第 2 项): SelectedList 改为**返回快照** ====================
        //   【原缺陷(审计点名的定时炸弹)】原来是 `get { Prune(); return SelectedParties; }` ——
        //   把内部 `List<MobileParty>` **原样交出去**, 所有调用点拿到的都是**活列表**:
        //     · 遍历过程中只要调了任何会改动选区的命令(Toggle / Select / SelectMany / Clear),
        //       就会抛 "集合已修改; 枚举操作可能无法执行"(InvalidOperationException),
        //       或者更糟 —— 不抛异常的静默跳过/重复处理;
        //     · 连 `MapSelection.Selected` 自己都会 Prune()(内部 RemoveAt), 所以
        //       "先取 Selected 再遍历 SelectedList" 这种再普通不过的写法本身就是雷。
        //   【修法】返回 `new List<MobileParty>(SelectedParties)` —— 一个**独立快照**:
        //     · 调用点可以在遍历里安全地下命令(选中/取消/清空);
        //     · 快照里可能含"遍历期间刚失效"的部队 —— 各调用点本来就有 `p == null || !p.IsActive` 判定;
        //     · 代价: 每次读取多一次 List 浅拷贝(选区通常 1~10 支, 可忽略)。
        //   【影响面】全部读取点(见报告清单)由"活列表"变为"快照": 它们此前都只做只读遍历,
        //     没有任何一处是**正当依赖**"活"语义的 —— 唯一真正"边遍历边改动选区"的写法
        //     (MapClickPatches 里点聚落/建军团的命令链)恰恰就是本次要修的那个 bug。
        internal static IReadOnlyList<MobileParty> SelectedList
        {
            get { Prune(); return new List<MobileParty>(SelectedParties); }
        }

        internal static int Count
        {
            get { Prune(); return SelectedParties.Count; }
        }

        private static void Prune()
        {
            for (int i = SelectedParties.Count - 1; i >= 0; i--)
            {
                var p = SelectedParties[i];
                if (p == null) { SelectedParties.RemoveAt(i); continue; }
                try { if (!p.IsActive) SelectedParties.RemoveAt(i); }
                catch { SelectedParties.RemoveAt(i); }
            }
        }

        internal static bool Is(MobileParty p)
        {
            Prune();
            return p != null && SelectedParties.Contains(p);
        }

        // v4.276: 选中提示按"陆军/海军"分开 —— 舰队的可用操作与军团不同(不能围城/进城),
        //   提示写错会让玩家以为点了没用。
        private static string HintOf(MobileParty p)
        {
            try
            {
                if (p != null && NavalTaskForce.IsTaskForce(p))
                    return " —— 左键海面=开过去, 左键港口=开往该港, 左键敌方舰队=追击, 右键=舰队菜单";
                if (p != null && NationalWillOrders.IsFleet(p))
                    return " —— 左键海面=开过去, 右键=舰队菜单";
            }
            catch { }
            return " —— 左键地面=移动, 左键定居点=前往, 左键敌方部队=攻击";
        }

        internal static void Select(MobileParty p)
        {
            SelectedParties.Clear();
            if (p != null) SelectedParties.Add(p);
            if (p != null) Message("已选中: " + NameOf(p) + HintOf(p));
            else Message("已取消选中");
        }

        internal static void Toggle(MobileParty p)
        {
            if (p == null) return;
            Prune();
            if (SelectedParties.Contains(p))
            {
                SelectedParties.Remove(p);
                Message(SelectedParties.Count == 0 ? "已取消选中" : "剩余选中 " + SelectedParties.Count + " 支部队");
                    return;
            }
            SelectedParties.Clear();
            SelectedParties.Add(p);
            Message("已选中: " + NameOf(p) + HintOf(p));
        }

        internal static void SelectMany(List<MobileParty> parties)
        {
            SelectedParties.Clear();
            if (parties != null)
            {
                foreach (var p in parties)
                {
                    if (p != null && !SelectedParties.Contains(p)) SelectedParties.Add(p);
                }
            }
            if (SelectedParties.Count == 0) Message("框选范围内没有我方部队");
            else Message("框选选中 " + SelectedParties.Count + " 支部队 —— 左键地面/定居点/敌人给全体下令");
        }

        internal static void Clear()
        {
            SelectedParties.Clear();
        }

        internal static string NameOf(MobileParty p)
        {
            try
            {
                if (p == null) return "?";
                if (p.LeaderHero != null && p.LeaderHero.Name != null) return p.LeaderHero.Name.ToString();
                if (p.Name != null) return p.Name.ToString();
            }
            catch { }
            return "?";
        }

        internal static void Message(string text)
        {
            try
            {
                // 消息照常发; 面板打开时 SPChatLog 会被 MessageListShift 整体右滑让开(原版动画)
                InformationManager.DisplayMessage(new InformationMessage(text, Color.FromUint(4294953344U)));
            }
            catch { }
        }
    }
}
