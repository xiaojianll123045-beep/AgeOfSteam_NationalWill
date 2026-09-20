using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace FeudalInternalAffairs
{
    // v4.73 厌战度(用户需求):
    //   战斗伤亡 + 每日自然累积 -> 每国对每敌国的厌战度(0~100, 方向性)
    //   高厌战: 士气/逃兵/繁荣惩罚; 持续忽略(高厌战 30 天且 30 天无和谈动作)加重惩罚
    //   和谈互动:
    //     · 玩家向对方求和被拒 -> 玩家厌战减免(努力过了), 拒绝方(AI)厌战上升
    //     · AI 高厌战会主动向玩家弹窗求和; 玩家拒绝 -> 玩家厌战上升
    internal static class WarWeariness
    {
        // key = "我方id>敌方id"(方向性)
        internal static readonly Dictionary<string, float> Wear = new Dictionary<string, float>();
        internal static readonly Dictionary<string, int> LastAsk = new Dictionary<string, int>();      // key -> 最近和谈动作日
        internal static readonly Dictionary<string, int> PlayerAsked = new Dictionary<string, int>();  // 玩家>敌 -> 求和被拒日(14 天成长减免)
        internal static readonly Dictionary<string, int> HighSince = new Dictionary<string, int>();    // 我方id -> 厌战>=70 起始日
        internal static readonly Dictionary<string, int> Noticed = new Dictionary<string, int>();      // 我方id -> 忽略提醒日

        private static readonly List<string> _pendingAiAsk = new List<string>();        // 待弹窗: AI 国 id
        private static readonly List<string> _pendingPlayerPeace = new List<string>();  // 待执行: 玩家已接受的 AI 停战
        private static bool _asking;
        private static int _today;
        private static int _lastDay = -1;

        // ================= 查询 =================
        private static string Key(string mineId, string enemyId) { return mineId + ">" + enemyId; }

        internal static float WearOf(Kingdom mine, Kingdom enemy)
        {
            try
            {
                if (mine == null || enemy == null) return 0f;
                float w;
                return Wear.TryGetValue(Key(mine.StringId, enemy.StringId), out w) ? w : 0f;
            }
            catch { return 0f; }
        }

        private static void SetWear(string mineId, string enemyId, float v)
        {
            try
            {
                if (string.IsNullOrEmpty(mineId) || string.IsNullOrEmpty(enemyId)) return;
                if (v < 0f) v = 0f;
                if (v > 100f) v = 100f;
                string k = Key(mineId, enemyId);
                if (v <= 0.01f) { Wear.Remove(k); return; }
                Wear[k] = v;
            }
            catch { }
        }

        // 该国在所有战争中的最大厌战(国家级惩罚用)
        internal static float MaxWearOf(Kingdom k)
        {
            try
            {
                if (k == null) return 0f;
                string prefix = k.StringId + ">";
                float max = 0f;
                foreach (var kv in Wear)
                {
                    if (kv.Key.StartsWith(prefix, StringComparison.Ordinal) && kv.Value > max) max = kv.Value;
                }
                return max;
            }
            catch { return 0f; }
        }

        // ================= 战斗伤亡 =================
        internal static void OnBattleEnded(MapEvent me)
        {
            try
            {
                if (me == null) return;
                var atk = me.AttackerSide != null ? me.AttackerSide.MapFaction as Kingdom : null;
                var def = me.DefenderSide != null ? me.DefenderSide.MapFaction as Kingdom : null;
                int ca = 0, cd = 0;
                try { if (me.AttackerSide != null) ca = (int)me.AttackerSide.CasualtyStrength; } catch { }
                try { if (me.DefenderSide != null) cd = (int)me.DefenderSide.CasualtyStrength; } catch { }
                if (atk != null && def != null && atk != def)
                {
                    if (ca > 0) AddBattleWear(atk, def, ca);
                    if (cd > 0) AddBattleWear(def, atk, cd);
                    if (ca + cd > 20)
                        DLog.Force("厌战: " + atk.Name + "×" + def.Name + " 伤亡 " + ca + "/" + cd
                            + " -> 厌战 " + (int)WearOf(atk, def) + "/" + (int)WearOf(def, atk));
                }
            }
            catch { }
        }

        private static void AddBattleWear(Kingdom who, Kingdom vs, int casualties)
        {
            try
            {
                string k = Key(who.StringId, vs.StringId);
                float cur;
                Wear.TryGetValue(k, out cur);
                float add = casualties * 0.05f;
                if (add > 25f) add = 25f;
                SetWear(who.StringId, vs.StringId, cur + add);
            }
            catch { }
        }

        // ================= 每日 =================
        internal static void Daily(int day)
        {
            try
            {
                if (day == _lastDay) return;
                _lastDay = day;
                _today = day;

                var lookup = new Dictionary<string, Kingdom>();
                foreach (var k in Kingdom.All) if (k != null && !k.IsEliminated) lookup[k.StringId] = k;
                var pk = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;

                // ---- 1) 厌战演化 ----
                var keys = new List<string>(Wear.Keys);
                for (int i = 0; i < keys.Count; i++)
                {
                    var kk = keys[i];
                    int gt = kk.IndexOf('>');
                    if (gt <= 0) continue;
                    string mineId = kk.Substring(0, gt);
                    string enemyId = kk.Substring(gt + 1);
                    Kingdom mine, enemy;
                    if (!lookup.TryGetValue(mineId, out mine) || !lookup.TryGetValue(enemyId, out enemy))
                    {
                        Wear.Remove(kk);
                        continue;
                    }
                    bool atWar = false;
                    try { atWar = mine.IsAtWarWith(enemy); } catch { }
                    float cur = Wear[kk];
                    if (!atWar)
                    {
                        cur -= 2f;   // 停战 -> 降温
                    }
                    else
                    {
                        cur += 0.35f;   // 战时自然累积
                        // 玩家主动求和被拒 -> 14 天成长减免(甚至回落)
                        if (pk != null && mine == pk)
                        {
                            int asked;
                            if (PlayerAsked.TryGetValue(kk, out asked) && day - asked <= 14) cur -= 0.5f;
                        }
                    }
                    if (cur <= 0.01f) { Wear.Remove(kk); continue; }
                    if (cur > 100f) cur = 100f;
                    Wear[kk] = cur;
                }

                // ---- 2) 国家级惩罚 ----
                foreach (var k in Kingdom.All)
                {
                    if (k == null || k.IsEliminated) continue;
                    float w = MaxWearOf(k);
                    if (w < 60f)
                    {
                        HighSince.Remove(k.StringId);
                        continue;
                    }
                    // 士气
                    bool isPlayerK = false;
                    try { var nk = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null; isPlayerK = nk != null && ReferenceEquals(nk, k); } catch { }
                    int wearLost = 0, wearParties = 0;
                    foreach (var p in MobileParty.All)
                    {
                        if (p == null || !p.IsActive || p.MapFaction != k) continue;
                        if (p.IsGarrison || p.IsMilitia || p.IsCaravan) continue;
                        if (p == MobileParty.MainParty) continue;
                        try { p.RecentEventsMorale = Math.Max(-100f, p.RecentEventsMorale - 1f); } catch { }
                        if (w >= 80f)
                        {
                            int got = WarEconomy.CutTroops(p, 0.005f);   // 重度厌战: 逃兵 0.5%/日
                            if (got > 0) { wearLost += got; wearParties++; }
                        }
                    }
                    if (isPlayerK && wearLost > 0)
                    {
                        // v4.92: 减员给消息(说明原因)
                        try { MapSelection.Message("厌战减员(厌战度 " + (int)w + "): " + wearParties + " 支部队逃兵 -" + wearLost + " 人; 尽快和谈可停止"); } catch { }
                        DLog.Force("厌战: 玩家王国重度厌战逃兵 -" + wearLost + " 人(" + wearParties + " 支部队)");
                    }
                    // 持续忽略(>=70 且 30 天内无和谈动作) -> 繁荣与民怨
                    if (w >= 70f)
                    {
                        int since;
                        if (!HighSince.TryGetValue(k.StringId, out since)) { HighSince[k.StringId] = day; since = day; }
                        bool ignored = true;
                        foreach (var kv in LastAsk)
                        {
                            if (kv.Key.StartsWith(k.StringId + ">", StringComparison.Ordinal) && day - kv.Value < 30) { ignored = false; break; }
                        }
                        if (ignored && day - since >= 30)
                        {
                            foreach (var s in k.Settlements)
                            {
                                if (s == null || s.Town == null) continue;
                                try { if (s.Town.Prosperity > 50f) s.Town.Prosperity *= 0.998f; } catch { }
                            }
                            int last;
                            if (!Noticed.TryGetValue(k.StringId, out last) || day - last >= 14)
                            {
                                Noticed[k.StringId] = day;
                                bool playerSide = pk != null && k == pk;
                                if (playerSide)
                                    MapSelection.Message("国内厌战严重(厌战 " + (int)w + ")且无人推动和谈 — 城镇繁荣与部队士气持续流失");
                                DLog.Force("厌战: " + k.Name + " 持续忽略战争(厌战 " + (int)w + " 已 " + (day - since) + " 天)");
                            }
                        }
                    }
                    else HighSince.Remove(k.StringId);
                }

                // ---- 3) AI 主动向玩家求和(排队弹窗; 30 天冷却) ----
                if (pk != null)
                {
                    foreach (var k in Kingdom.All)
                    {
                        if (k == null || k.IsEliminated || k == pk) continue;
                        bool atWar = false;
                        try { atWar = k.IsAtWarWith(pk); } catch { }
                        if (!atWar) continue;
                        float w = WearOf(k, pk);
                        if (w < 65f) continue;
                        string kk = Key(k.StringId, pk.StringId);
                        int last;
                        if (LastAsk.TryGetValue(kk, out last) && day - last < 30) continue;
                        if (!_pendingAiAsk.Contains(k.StringId)) _pendingAiAsk.Add(k.StringId);
                    }
                }
            }
            catch (Exception ex) { DLog.Force("厌战日常异常: " + ex.Message); }
        }

        // ================= 每帧(弹窗 + 执行停战) =================
        internal static void Tick()
        {
            try
            {
                if (_pendingAiAsk.Count > 0 && !_asking)
                {
                    var ai = FindKingdom(_pendingAiAsk[0]);
                    _pendingAiAsk.RemoveAt(0);
                    if (ai != null) { _asking = true; ShowAskPopup(ai); }
                }
                while (_pendingPlayerPeace.Count > 0)
                {
                    var ai = FindKingdom(_pendingPlayerPeace[0]);
                    _pendingPlayerPeace.RemoveAt(0);
                    if (ai != null)
                    {
                        try
                        {
                            if (DiplomacyBehavior.PlayerMakePeace(ai))
                                MapSelection.Message("已接受 " + ai.Name + " 的停战请求");
                        }
                        catch (Exception ex) { DLog.Force("接受停战失败: " + ex.Message); }
                    }
                }
            }
            catch { }
        }

        private static Kingdom FindKingdom(string id)
        {
            try
            {
                foreach (var k in Kingdom.All) if (k != null && k.StringId == id) return k;
            }
            catch { }
            return null;
        }

        private static void ShowAskPopup(Kingdom ai)
        {
            try
            {
                var pk = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                if (pk == null) { _asking = false; return; }
                float w = WearOf(ai, pk);
                float ourW = WearOf(pk, ai);
                string name = ai.Name != null ? ai.Name.ToString() : ai.StringId;
                InformationManager.ShowInquiry(new InquiryData(
                    name + " 请求停战",
                    name + " 的厌战度已达 " + (int)w + "(我方 " + (int)ourW + ")。\n"
                    + "接受将立即推动双方停战; 拒绝会让我国厌战上升。",
                    true, true, "接受停战", "拒绝",
                    delegate
                    {
                        _asking = false;
                        _pendingPlayerPeace.Add(ai.StringId);
                    },
                    delegate
                    {
                        _asking = false;
                        NotePlayerRefusedAsk(ai);
                    },
                    "", 0f, null, null, null), true, false);
            }
            catch (Exception ex) { DLog.Force("停战申请弹窗异常: " + ex.Message); _asking = false; }
        }

        // 玩家拒绝 AI 的和谈申请 -> 玩家厌战上升(拒绝方惩罚)
        private static void NotePlayerRefusedAsk(Kingdom ai)
        {
            try
            {
                var pk = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                if (pk == null || ai == null) return;
                SetWear(pk.StringId, ai.StringId, WearOf(pk, ai) + 10f);
                string kk = Key(pk.StringId, ai.StringId);
                LastAsk[kk] = _today;
                MapSelection.Message("已拒绝 " + ai.Name + " 的停战请求 — 厌战度 +10");
                DLog.Force("厌战: 玩家拒绝 " + ai.Name + " 停战请求, 玩家厌战=" + (int)WearOf(pk, ai));
            }
            catch { }
        }

        // 玩家向对方求和被拒 -> 玩家减免(努力过了) + 拒绝方惩罚
        internal static void NotePlayerPeaceRefused(Kingdom enemy)
        {
            try
            {
                var pk = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                if (pk == null || enemy == null) return;
                string kk = Key(pk.StringId, enemy.StringId);
                float our = WearOf(pk, enemy);
                SetWear(pk.StringId, enemy.StringId, our - 6f);   // 我方减免
                PlayerAsked[kk] = _today;                          // 14 天成长减免
                LastAsk[kk] = _today;
                SetWear(enemy.StringId, pk.StringId, WearOf(enemy, pk) + 8f);   // 拒绝方惩罚
                DLog.Force("厌战: 玩家求和被 " + enemy.Name + " 拒绝 -> 我方 " + (int)WearOf(pk, enemy)
                    + " / 对方 " + (int)WearOf(enemy, pk));
            }
            catch { }
        }

        // 玩家主动发起和谈(无论结果) -> 记录, 供"持续忽略"判定
        internal static void NotePlayerTriedPeace(Kingdom enemy)
        {
            try
            {
                var pk = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                if (pk == null || enemy == null) return;
                LastAsk[Key(pk.StringId, enemy.StringId)] = _today;
            }
            catch { }
        }

        // 和谈成功 -> 双方厌战大降
        internal static void OnPeace(Kingdom a, Kingdom b)
        {
            try
            {
                if (a == null || b == null) return;
                SetWear(a.StringId, b.StringId, WearOf(a, b) - 30f);
                SetWear(b.StringId, a.StringId, WearOf(b, a) - 30f);
            }
            catch { }
        }

        // ================= 存档 =================
        internal static string Save()
        {
            try
            {
                var sb = new StringBuilder();
                sb.Append('1');
                foreach (var kv in Wear)
                    sb.Append("|W:").Append(kv.Key).Append(':').Append(kv.Value.ToString("F1", CultureInfo.InvariantCulture));
                foreach (var kv in LastAsk)
                    sb.Append("|A:").Append(kv.Key).Append(':').Append(kv.Value);
                foreach (var kv in PlayerAsked)
                    sb.Append("|P:").Append(kv.Key).Append(':').Append(kv.Value);
                foreach (var kv in HighSince)
                    sb.Append("|H:").Append(kv.Key).Append(':').Append(kv.Value);
                return sb.ToString();
            }
            catch { return "1"; }
        }

        internal static void Load(string data)
        {
            try
            {
                Wear.Clear(); LastAsk.Clear(); PlayerAsked.Clear(); HighSince.Clear();
                if (string.IsNullOrEmpty(data)) return;
                var parts = data.Split('|');
                for (int i = 1; i < parts.Length; i++)
                {
                    var seg = parts[i];
                    if (string.IsNullOrEmpty(seg)) continue;
                    int c1 = seg.IndexOf(':');
                    int c2 = seg.LastIndexOf(':');
                    if (c1 <= 0 || c2 <= c1) continue;
                    string kind = seg.Substring(0, c1);
                    string key = seg.Substring(c1 + 1, c2 - c1 - 1);
                    string val = seg.Substring(c2 + 1);
                    if (kind == "W")
                    {
                        float f;
                        if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out f)) Wear[key] = f;
                    }
                    else if (kind == "A")
                    {
                        int v;
                        if (int.TryParse(val, out v)) LastAsk[key] = v;
                    }
                    else if (kind == "P")
                    {
                        int v;
                        if (int.TryParse(val, out v)) PlayerAsked[key] = v;
                    }
                    else if (kind == "H")
                    {
                        int v;
                        if (int.TryParse(val, out v)) HighSince[key] = v;
                    }
                }
                DLog.Force("厌战: 读档 记录=" + Wear.Count);
            }
            catch (Exception ex) { DLog.Force("厌战读档异常: " + ex.Message); }
        }

        internal static void Reset()
        {
            Wear.Clear(); LastAsk.Clear(); PlayerAsked.Clear(); HighSince.Clear();
            _pendingAiAsk.Clear(); _pendingPlayerPeace.Clear();
            _asking = false; _lastDay = -1;
        }
    }
}
