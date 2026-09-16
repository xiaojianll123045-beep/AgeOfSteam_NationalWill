using System;
using System.Collections.Generic;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;

namespace FeudalInternalAffairs
{
    // 玩家=国家意志: 视野 = 本国所有部队视野的总和
    // 例如选了北帝国, 就能看见北帝国任何一支部队看见的东西
    internal static class MapVisionPatches
    {
        private static CampaignTime _cacheTime = CampaignTime.Zero;
        private static Kingdom _cacheKingdom;
        private static readonly List<CampaignVec2> Positions = new List<CampaignVec2>();
        private static readonly List<float> Ranges = new List<float>();

        [HarmonyPatch(typeof(PartyBase), "UpdateVisibilityAndInspected")]
        internal static class NationalWillVision
        {
            private static void Postfix(PartyBase __instance)
            {
                try
                {
                    var behavior = NationalWillOrders.Behavior;
                    if (behavior == null || !behavior.IsNationalWill) return;
                    var kingdom = behavior.NationKingdom;
                    if (kingdom == null || __instance == null) return;

                    // 玩家部队由 NationalWillParty 负责隐藏, 绝对不要在这里把它拉回可见
                    if (__instance.MobileParty != null && __instance.MobileParty.IsMainParty) return;

                    // 本国部队永远可见(含本国军团的全部成员)
                    if (__instance.MapFaction != null && ReferenceEquals(__instance.MapFaction, kingdom))
                    {
                        SetVisible(__instance);
                        SetArmyVisible(__instance);
                        return;
                    }
                    if (IsVisible(__instance)) return;

                    // 敌方/中立: 本国任何一支部队看得到就算看得到
                    RefreshCache(kingdom);
                    var position = __instance.Position;
                    for (int i = 0; i < Positions.Count; i++)
                    {
                        float range = Ranges[i];
                        if (range <= 0f) continue;
                        var delta = Positions[i] - position;
                        if (delta.Length <= range + 2f)
                        {
                            // 敌军军团成员原版是合并隐藏的 -> 整支军团一起显示, 否则会"撞上空气"
                            SetArmyVisible(__instance);
                            return;
                        }
                    }
                }
                catch { }
            }
        }

        // 把该部队所在军团的所有成员都设为可见(敌军军团成员原版是隐藏的)
        private static void SetArmyVisible(PartyBase party)
        {
            try
            {
                var mobile = party.MobileParty;
                if (mobile == null) { SetVisible(party); return; }
                var army = mobile.Army;
                if (army == null || army.Parties == null) { SetVisible(party); return; }
                foreach (var m in army.Parties)
                {
                    if (m == null) continue;
                    if (m.IsMainParty) continue;   // 玩家部队由 NationalWillParty 隐藏
                    SetVisible(m.Party);
                }
            }
            catch { }
        }

        // 原版只在"玩家部队附近"更新可见性(CampaignTickCacheDataStore.UpdateVisibilitiesBasedOnPoint),
        // 而我们的玩家部队是藏起来的 -> 远处敌人的可见性永远不更新(一直隐身, 撞上就是"空气")。
        // 所以这里自己做全场刷新(每 0.25 秒一次): 本国部队永远可见, 其余按视野总和决定。
        private static float _tickTimer;

        internal static void TickVisibility(float dt)
        {
            try
            {
                var behavior = NationalWillOrders.Behavior;
                if (behavior == null || !behavior.IsNationalWill) return;
                var kingdom = behavior.NationKingdom;
                if (kingdom == null) return;

                _tickTimer -= dt;
                if (_tickTimer > 0f) return;
                _tickTimer = 0.25f;

                RefreshCache(kingdom);

                foreach (var k in Kingdom.All)
                {
                    if (k == null) continue;
                    foreach (var clan in k.Clans)
                    {
                        if (clan == null) continue;
                        foreach (var wpc in clan.WarPartyComponents)
                        {
                            var mp = wpc != null ? wpc.MobileParty : null;
                            if (mp == null) continue;
                            ApplyVisibility(mp, kingdom);
                        }
                    }
                }
            }
            catch { }
        }

        private static void ApplyVisibility(MobileParty p, Kingdom kingdom)
        {
            try
            {
                if (p == null || !p.IsActive) return;
                if (p.IsMainParty) return;   // 玩家部队由 NationalWillParty 隐藏
                if (IsOurs(p, kingdom))
                {
                    if (!p.IsVisible) p.IsVisible = true;
                    return;
                }
                bool visible = InVision(p.Position);
                if (p.IsVisible != visible) p.IsVisible = visible;
            }
            catch { }
        }

        private static bool InVision(CampaignVec2 pos)
        {
            for (int i = 0; i < Positions.Count; i++)
            {
                float range = Ranges[i];
                if (range <= 0f) continue;
                var delta = Positions[i] - pos;
                if (delta.Length <= range + 2f) return true;
            }
            return false;
        }

        private static bool IsVisible(PartyBase party)        {
            try
            {
                var mobile = party.MobileParty;
                if (mobile != null) return mobile.IsVisible;
                var settlement = party.Settlement;
                return settlement == null || settlement.IsVisible;
            }
            catch { return true; }
        }

        private static void SetVisible(PartyBase party)
        {
            try
            {
                var mobile = party.MobileParty;
                if (mobile != null)
                {
                    if (!mobile.IsVisible) mobile.IsVisible = true;
                    return;
                }
                var settlement = party.Settlement;
                if (settlement != null && !settlement.IsVisible) settlement.IsVisible = true;
            }
            catch { }
        }

        // 每帧只算一次本国部队的位置/视野半径
        private static void RefreshCache(Kingdom kingdom)
        {
            try
            {
                if (ReferenceEquals(_cacheKingdom, kingdom) && _cacheTime == CampaignTime.Now) return;
                _cacheKingdom = kingdom;
                _cacheTime = CampaignTime.Now;
                Positions.Clear();
                Ranges.Clear();
                foreach (var p in MobileParty.All)
                {
                    if (p == null || !p.IsActive || p.IsMainParty) continue;
                    if (IsOurs(p, kingdom)) AddParty(p);

                    // 军团成员可能不在 MobileParty.All 里, 单独把整支军团的部队补上
                    var army = p.Army;
                    if (army != null && army.Kingdom != null && ReferenceEquals(army.Kingdom, kingdom) && army.Parties != null)
                    {
                        foreach (var m in army.Parties)
                        {
                            if (m != null && m.IsActive) AddParty(m);
                        }
                    }
                }

                // 关键: 军团的部队可能整个都不在 MobileParty.All 里 -> 直接遍历本国所有家族的战争部队
                foreach (var clan in kingdom.Clans)
                {
                    if (clan == null) continue;
                    foreach (var wpc in clan.WarPartyComponents)
                    {
                        var mp = wpc != null ? wpc.MobileParty : null;
                        if (mp != null && mp.IsActive) AddParty(mp);
                    }
                }

                // 本国的定居点也提供视野(城镇/城堡/村庄周围都能看见敌人)
                try
                {
                    foreach (var s in kingdom.Settlements)
                    {
                        if (s == null) continue;
                        Positions.Add(s.Position);
                        Ranges.Add(30f);   // 定居点视野半径
                    }
                }
                catch { }
                if (!_loggedOnce && Positions.Count > 0)
                {
                    _loggedOnce = true;
                    DLog.Force("视野: 已纳入本国 " + Positions.Count + " 支部队(含军团成员)");
                }
            }
            catch { }
        }

        private static void AddParty(MobileParty p)
        {
            try
            {
                Positions.Add(p.Position);
                Ranges.Add(p.SeeingRange);
                // 本国部队(含军团成员)在地图上要看得见(军团成员原版是合并隐藏的)
                if (!p.IsMainParty && !p.IsVisible) p.IsVisible = true;
            }
            catch { }
        }

        private static bool _loggedOnce;

        // 属于所选国家的部队: 领主/军团(看家族)、商队村民(看实际家族)、王国直属(看阵营)
        private static bool IsOurs(MobileParty p, Kingdom kingdom)
        {
            try
            {
                if (p.MapFaction != null && ReferenceEquals(p.MapFaction, kingdom)) return true;

                var clan = p.ActualClan;
                if (clan != null && ReferenceEquals(clan.Kingdom, kingdom)) return true;

                var hero = p.LeaderHero;
                if (hero != null && hero.Clan != null && ReferenceEquals(hero.Clan.Kingdom, kingdom)) return true;

                var army = p.Army;
                if (army != null && army.Kingdom != null && ReferenceEquals(army.Kingdom, kingdom)) return true;
            }
            catch { }
            return false;
        }
    }
}
