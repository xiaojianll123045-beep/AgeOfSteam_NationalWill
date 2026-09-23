using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.ViewModelCollection;
using TaleWorlds.Core.ViewModelCollection.Information;

namespace FeudalInternalAffairs
{
    // 28.7 悬停劫持: 地图上悬停部队时, 把原版 tooltip 里的"兵种列表"换成我们的列表
    //   挂点(1.4.7 实测):
    //     SandBox.View.Map.Visuals.MobilePartyVisual.OnHover() -> InformationManager.ShowTooltip(typeof(MobileParty), {party,false,true})
    //     SandBox.View.SandBoxViewSubModule.RegisterTooltipTypes() 注册的刷新器 =
    //       TooltipRefresherCollection.RefreshMobilePartyTooltip(PropertyBasedTooltipVM, object[])
    //     原版逐兵种行由 TooltipRefresherCollection.AddPartyTroopProperties(...) 添加 -> Prefix 替换;
    //     Refresh 的 Postfix 负责"多选合计"与反射失败时的兜底摘要。
    internal static class PartyTooltipPatch
    {
        private const int ThrottleMs = 200;   // 28.13: 悬停 tooltip 按需构建, 节流 200ms

        // ==================== 聚合缓存(悬停时才做一次 O(n)) ====================
        private sealed class UnitRow
        {
            internal string Id = "";
            internal string Name = "";
            internal string Icon = "";
            internal int Men;
            internal int Prof;
        }

        private sealed class Agg
        {
            internal readonly List<UnitRow> Rows = new List<UnitRow>();
            internal int Total;
            internal int FillPct = 100;
            internal int Org = 100;
            internal int Morale = 50;
            internal DateTime Stamp;
        }

        private static readonly Dictionary<string, Agg> Cache =
            new Dictionary<string, Agg>(StringComparer.Ordinal);
        private static readonly Dictionary<Type, FieldInfo> PartyFieldOfClosure =
            new Dictionary<Type, FieldInfo>();
        private static string _handledPartyId;

        private static string IdOf(MobileParty p)
        {
            try { return p != null ? p.StringId : null; } catch { return null; }
        }

        // 200ms 内复用同一部队的聚合结果(逐兵种人数/熟练 + 满足率/士气/组织度)
        private static Agg GetAgg(MobileParty p)
        {
            string id = IdOf(p);
            Agg a;
            if (!string.IsNullOrEmpty(id) && Cache.TryGetValue(id, out a) && a != null
                && (DateTime.UtcNow - a.Stamp).TotalMilliseconds < ThrottleMs)
                return a;
            a = Build(p);
            if (!string.IsNullOrEmpty(id))
            {
                if (Cache.Count > 512) Cache.Clear();   // 有界, 防长局泄漏
                Cache[id] = a;
            }
            return a;
        }

        private static Agg Build(MobileParty p)
        {
            var a = new Agg { Stamp = DateTime.UtcNow };
            try
            {
                if (p == null) return a;
                Dictionary<string, int> men;
                Dictionary<string, float> prof;
                Soldiers.Aggregate(p, out men, out prof);
                if (men != null)
                {
                    foreach (var kv in men)
                    {
                        if (kv.Value <= 0) continue;
                        var row = new UnitRow();
                        row.Id = kv.Key;
                        var u = Equipment.UnitOf(kv.Key);
                        row.Name = u != null && !string.IsNullOrEmpty(u.Name)
                            ? u.Name
                            : (kv.Key == "unknown" ? "未知兵种" : kv.Key);
                        row.Icon = MilIcons.IconOf(kv.Key);
                        row.Men = kv.Value;
                        float avg;
                        row.Prof = prof != null && prof.TryGetValue(kv.Key, out avg)
                            ? (int)Math.Round(avg) : 0;
                        a.Rows.Add(row);
                        a.Total += kv.Value;
                    }
                    a.Rows.Sort(CompareRow);
                }
                var lg = DefArmy.LegionOf(p);
                if (lg != null)
                {
                    try { a.FillPct = (int)Math.Round(Math.Max(0f, Math.Min(1f, DefArmy.EquipFillOf(lg))) * 100f); } catch { }
                    try { a.Morale = (int)Math.Round(ArmyDoctrine.MoraleOf2(ArmyDoctrine.LegionKey(lg))); } catch { }
                }
                else
                {
                    // 原版/领主部队: 满足率视为 100%(27.15), 士气取队伍士气
                    try { a.Morale = (int)Math.Round(DefArmyStats.MoraleOf(p.Party)); } catch { }
                }
                try { a.Org = (int)Math.Round(DefArmyStats.OrgOf(p.Party)); } catch { }
            }
            catch { }
            return a;
        }

        private static int CompareRow(UnitRow x, UnitRow y)
        {
            int ix = OrderOf(x.Id), iy = OrderOf(y.Id);
            if (ix != iy) return ix - iy;
            if (x.Men != y.Men) return y.Men - x.Men;
            return string.CompareOrdinal(x.Name, y.Name);
        }

        // 按 Equipment.Units(14 兵种)顺序; 未识别兵种排最后
        private static int OrderOf(string id)
        {
            try
            {
                for (int i = 0; i < Equipment.Units.Count; i++)
                {
                    var u = Equipment.Units[i];
                    if (u != null && u.Id == id) return i;
                }
                return int.MaxValue;
            }
            catch { return int.MaxValue; }
        }

        private static string DefOf(string icon, string name)
        {
            // 原版 tooltip 行是 RichTextWidget, 支持 <img src="精灵名"/> 内联图标
            return string.IsNullOrEmpty(icon) ? name : "<img src=\"" + icon + "\"/>" + name;
        }

        // 显示口径 = 名册人数 − 将军数(英雄): 与内核编制/军饷口径解耦, 仅用于 tooltip
        private static int MenOf(MobileParty p)
        {
            try
            {
                int n = DefArmy.RegularsOf(p);
                return n > 0 ? n : 0;
            }
            catch { return 0; }
        }

        // 首帧悬停也要走我们的列表: 逐人表未建时对领主/家族/驻军/民兵按名册懒折算(不含将军)
        private static bool EnsureOurList(MobileParty p)
        {
            try
            {
                if (p == null || !p.IsActive) return false;
                if (p.IsGarrison || p.IsMilitia || p.IsMainParty) return true;
                return p.PartyComponent is LordPartyComponent;
            }
            catch { return false; }
        }

        // ==================== tooltip 行输出 ====================
        private static void AddSep(PropertyBasedTooltipVM vm)
        {
            vm.AddProperty("", "", -1, TooltipProperty.TooltipPropertyFlags.None);
        }

        private static void AddLine(PropertyBasedTooltipVM vm, string def, string val)
        {
            vm.AddProperty(def, val, 0, TooltipProperty.TooltipPropertyFlags.None);
        }

        // 陆军: 部队名(原版行保留) + 逐兵种(图标/人数/熟练均值) + 装备满足率 + 士气/组织度
        private static void AddOurUnitList(PropertyBasedTooltipVM vm, MobileParty p)
        {
            var a = GetAgg(p);
            if (a == null || a.Total <= 0) return;
            AddSep(vm);
            AddLine(vm, "兵种构成 (FIA)", MenOf(p).ToString("N0") + " 人");
            for (int i = 0; i < a.Rows.Count; i++)
            {
                var r = a.Rows[i];
                AddLine(vm, DefOf(r.Icon, r.Name), r.Men.ToString("N0") + " 人 · 熟练 " + r.Prof);
            }
            AddLine(vm, "装备满足率", a.FillPct + "%");
            AddLine(vm, "士气 / 组织度", a.Morale + " / " + a.Org);
            try
            {
                int ships = p.Ships != null ? p.Ships.Count : 0;
                if (ships > 0) AddLine(vm, "舰船", ships + " 艘 (状态见原版舰船行)");
            }
            catch { }
        }

        // 多选(框选): 照 27.11 小窗口径 -> 合计兵种构成 + 逐队列一行
        private static void AddSelectionSummary(PropertyBasedTooltipVM vm)
        {
            try
            {
                if (MapSelection.Count <= 1) return;
                var sel = MapSelection.SelectedList;
                if (sel == null) return;
                var totals = new Dictionary<string, int>(StringComparer.Ordinal);
                var order = new List<string>();
                var names = new List<string>();
                var vals = new List<string>();
                int used = 0, men = 0;
                for (int i = 0; i < sel.Count; i++)
                {
                    var p = sel[i];
                    if (p == null) continue;
                    if (Soldiers.Get(p) == null && !EnsureOurList(p)) continue;
                    var a = GetAgg(p);
                    if (a == null || a.Total <= 0) continue;
                    used++;
                    men += MenOf(p);
                    for (int k = 0; k < a.Rows.Count; k++)
                    {
                        var r = a.Rows[k];
                        int old;
                        if (!totals.TryGetValue(r.Id, out old)) order.Add(r.Id);
                        totals[r.Id] = old + r.Men;
                    }
                    names.Add(MapSelection.NameOf(p));
                    vals.Add(MenOf(p).ToString("N0") + " 人 · 满足 " + a.FillPct + "% · 士气 " + a.Morale
                        + " · 组织 " + a.Org);
                }
                if (used <= 0) return;
                AddSep(vm);
                AddLine(vm, "多选合计 (FIA)", used + " 队 · " + men.ToString("N0") + " 人");
                order.Sort(delegate (string x, string y) { return OrderOf(x) - OrderOf(y); });
                for (int i = 0; i < order.Count; i++)
                {
                    string uid = order[i];
                    var u = Equipment.UnitOf(uid);
                    string name = u != null && !string.IsNullOrEmpty(u.Name) ? u.Name : uid;
                    AddLine(vm, DefOf(MilIcons.IconOf(uid), name), "×" + totals[uid].ToString("N0"));
                }
                int cap = names.Count < 12 ? names.Count : 12;
                for (int i = 0; i < cap; i++) AddLine(vm, names[i], vals[i]);
                if (names.Count > cap) AddLine(vm, "…", "共 " + names.Count + " 队");
            }
            catch { }
        }

        // 从 lambda 闭包里取 MobileParty(RefreshMobilePartyTooltip 的 <>c__DisplayClass18_0.mobileParty)
        //   军团 tooltip(闭包只含 Army)/遭遇战(闭包只含 List<MobileParty>)取不到 -> 返回 null, 走原版
        private static MobileParty PartyOf(Func<TroopRoster> f)
        {
            try
            {
                var t = f != null ? f.Target : null;
                if (t == null) return null;
                var ty = t.GetType();
                FieldInfo fi;
                if (!PartyFieldOfClosure.TryGetValue(ty, out fi))
                {
                    fi = null;
                    var fs = ty.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    for (int i = 0; i < fs.Length; i++)
                    {
                        if (fs[i].FieldType == typeof(MobileParty)) { fi = fs[i]; break; }
                    }
                    PartyFieldOfClosure[ty] = fi;
                }
                return fi != null ? fi.GetValue(t) as MobileParty : null;
            }
            catch { return null; }
        }

        // ==================== ① 原版逐兵种列表 -> 我们的(真替换) ====================
        [HarmonyPatch(typeof(TooltipRefresherCollection), "AddPartyTroopProperties")]
        internal static class TroopListReplace
        {
            private static bool Prefix(PropertyBasedTooltipVM __0, TroopRoster __1, Func<TroopRoster> __4)
            {
                try
                {
                    var p = PartyOf(__4);
                    if (p == null) return true;                 // 军团/遭遇战 tooltip: 原版
                    if (p.IsInfoHidden) return true;            // 迷雾/隐藏信息: 原版
                    if (Soldiers.Get(p) == null && !EnsureOurList(p)) return true;   // 商队/村民等未转化部队: 原版
                    var a = GetAgg(p);
                    if (a == null || a.Total <= 0) return true; // 无逐人数据: 原版
                    try { if (__1 != null && ReferenceEquals(__1, p.PrisonRoster)) return true; } catch { return true; }
                    AddOurUnitList(__0, p);
                    _handledPartyId = IdOf(p);
                    return false;                               // 跳过原版兵种列表
                }
                catch { return true; }
            }
        }

        // ==================== ② 兜底摘要 + 多选合计 ====================
        [HarmonyPatch(typeof(TooltipRefresherCollection), "RefreshMobilePartyTooltip")]
        internal static class RefreshPartyTooltip
        {
            private static void Postfix(PropertyBasedTooltipVM __0, object[] __1)
            {
                try
                {
                    var p = __1 != null && __1.Length > 0 ? __1[0] as MobileParty : null;
                    if (p != null && !p.IsInfoHidden && Soldiers.Get(p) != null
                        && !string.Equals(IdOf(p), _handledPartyId, StringComparison.Ordinal))
                    {
                        // 退化路径: 直接改写点失效时至少追加一行摘要(报告已说明)
                        var a = GetAgg(p);
                        if (a != null && a.Total > 0)
                        {
                            AddSep(__0);
                            AddLine(__0, "FIA 军情", "兵力 " + MenOf(p).ToString("N0") + " · 装备满足率 " + a.FillPct
                                + "% · 士气 " + a.Morale + " · 组织度 " + a.Org);
                        }
                    }
                    _handledPartyId = null;
                    AddSelectionSummary(__0);
                }
                catch { _handledPartyId = null; }
            }
        }
    }
}
