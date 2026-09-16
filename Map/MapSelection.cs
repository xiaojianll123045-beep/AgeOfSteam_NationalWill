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

        internal static IReadOnlyList<MobileParty> SelectedList
        {
            get { Prune(); return SelectedParties; }
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

        internal static void Select(MobileParty p)
        {
            SelectedParties.Clear();
            if (p != null) SelectedParties.Add(p);
            if (p != null) Message("已选中: " + NameOf(p) + " —— 左键地面=移动, 左键定居点=前往, 左键敌方部队=攻击");
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
            Message("已选中: " + NameOf(p) + " —— 左键地面=移动, 左键定居点=前往, 左键敌方部队=攻击");
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
            try { InformationManager.DisplayMessage(new InformationMessage(text, Color.FromUint(4294953344U))); }
            catch { }
        }
    }
}
