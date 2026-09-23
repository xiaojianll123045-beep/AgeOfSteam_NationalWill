using System;
using System.Collections.Generic;
using System.Text;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.SaveSystem;

namespace FeudalInternalAffairs
{
    // 28.5 逐人熟练度: 每士兵 {兵种 id, 熟练 0~100, 个人击杀}
    //   紧凑字节: 每人 4 字节(兵种 1 + 熟练 1 + 击杀 2), 1000 人 ≈ 4KB(设计 28.5/28.13)
    internal struct SoldierRec
    {
        internal byte Unit;      // Equipment.Units 下标; UnknownUnit(255)=未识别
        internal byte Prof;      // 熟练 0~100
        internal ushort Kills;   // 个人击杀
    }

    // 每部队一条记录(FIA_Soldiers 存档单元)
    internal sealed class PartySoldiers
    {
        internal readonly List<SoldierRec> List = new List<SoldierRec>();
    }

    internal static class Soldiers
    {
        internal const byte UnknownUnit = 255;
        private static readonly Dictionary<string, PartySoldiers> Data =
            new Dictionary<string, PartySoldiers>(StringComparer.Ordinal);

        // 28.2 初始熟练度: 原版兵种等级 T1~T6 -> 20 / 35 / 50 / 65 / 80 / 92
        internal static byte InitProf(int tier)
        {
            switch (tier)
            {
                case 1: return 20;
                case 2: return 35;
                case 3: return 50;
                case 4: return 65;
                case 5: return 80;
                default: return 92;
            }
        }

        private static string IdOf(MobileParty p)
        {
            try { return p != null ? p.StringId : null; } catch { return null; }
        }

        internal static int UnitIndexOf(string unitId)
        {
            try
            {
                if (string.IsNullOrEmpty(unitId)) return -1;
                for (int i = 0; i < Equipment.Units.Count; i++)
                    if (Equipment.Units[i] != null && Equipment.Units[i].Id == unitId) return i;
            }
            catch { }
            return -1;
        }

        internal static string UnitIdOf(byte idx)
        {
            try
            {
                if (idx == UnknownUnit) return "unknown";
                var u = Equipment.Unit(idx);
                if (u != null && !string.IsNullOrEmpty(u.Id)) return u.Id;
            }
            catch { }
            return "unknown";
        }

        // 懒初始化(28.5): 无记录时按 27.12 从当前名单折算(兵种 + 等级初始熟练)
        internal static PartySoldiers Ensure(MobileParty p)
        {
            try
            {
                string id = IdOf(p);
                if (string.IsNullOrEmpty(id)) return null;
                PartySoldiers rec;
                if (Data.TryGetValue(id, out rec) && rec != null) return rec;
                rec = new PartySoldiers();
                try { FillFromRoster(rec, p.MemberRoster); } catch { }
                Data[id] = rec;
                return rec;
            }
            catch { return null; }
        }

        internal static PartySoldiers Get(MobileParty p)
        {
            try
            {
                string id = IdOf(p);
                if (string.IsNullOrEmpty(id)) return null;
                PartySoldiers rec;
                return Data.TryGetValue(id, out rec) ? rec : null;
            }
            catch { return null; }
        }

        // 只建空表(不按名单折算): 供 Split 移入逐人记录, 避免与懒初始化重复计数
        private static PartySoldiers EnsureEmpty(MobileParty p)
        {
            try
            {
                string id = IdOf(p);
                if (string.IsNullOrEmpty(id)) return null;
                PartySoldiers rec;
                if (Data.TryGetValue(id, out rec) && rec != null) return rec;
                rec = new PartySoldiers();
                Data[id] = rec;
                return rec;
            }
            catch { return null; }
        }

        // 27.12 折算: 步/弓/骑/骑射 + 原版等级 -> 兵种; 人数构成不变, 只换表示
        private static void FillFromRoster(PartySoldiers rec, TroopRoster roster)
        {
            if (rec == null || roster == null) return;
            foreach (var e in roster.GetTroopRoster())
            {
                var c = e.Character;
                if (c == null || c.IsHero || e.Number <= 0) continue;
                int branch = c.IsMounted ? (c.IsRanged ? 3 : 2) : (c.IsRanged ? 1 : 0);
                int tier = c.Tier;
                if (tier < 1) tier = 1;
                if (tier > 6) tier = 6;
                int idx = Equipment.UnitOfBranch(branch, tier);
                if (idx < 0 || idx >= UnknownUnit) idx = 0;
                byte prof = InitProf(tier);
                for (int i = 0; i < e.Number; i++)
                    rec.List.Add(new SoldierRec { Unit = (byte)idx, Prof = prof });
            }
        }

        // v4.236: 按"设计编成"直接写逐人表(不按花名册折算) —— 建军设计器成立军团时用,
        //   这样"选了 100 线列步兵"就是 100 条线列步兵记录, 不会被原版兵种折算成 民兵/弓兵
        internal static bool SetComposition(MobileParty p, int[] kindCounts)
        {
            try
            {
                if (p == null || kindCounts == null) return false;
                string id = IdOf(p);
                if (string.IsNullOrEmpty(id)) return false;
                var rec = new PartySoldiers();
                for (int k = 0; k < kindCounts.Length && k < Equipment.Units.Count; k++)
                {
                    int c = kindCounts[k];
                    if (c <= 0) continue;
                    byte pr = DefaultProfOf(k);
                    for (int i = 0; i < c; i++) rec.List.Add(new SoldierRec { Unit = (byte)k, Prof = pr });
                }
                Data[id] = rec;
                return true;
            }
            catch { return false; }
        }

        // 兵种默认熟练(27.2 兵种表按装备档折算: 低档 35 / 中档 65 / 高档 80)
        internal static byte DefaultProfOf(int unitIdx)
        {
            try
            {
                switch (unitIdx)
                {
                    case Equipment.UMilita: return 35;
                    case Equipment.UArcher: return 35;
                    case Equipment.UCrossbow: return 50;
                    case Equipment.UHorseArcher: return 50;
                    case Equipment.ULine: return 65;
                    case Equipment.ULightInf: return 65;
                    case Equipment.UDragoon: return 65;
                    case Equipment.UHussar: return 65;
                    case Equipment.UEngineer: return 65;
                    case Equipment.UGrenadier: return 80;
                    case Equipment.UCuirassier: return 80;
                    case Equipment.UFieldArtillery: return 80;
                    case Equipment.USiegeArtillery: return 80;
                    case Equipment.UHorseArtillery: return 80;
                    default: return 65;
                }
            }
            catch { return 65; }
        }

        // 名册非英雄合计(可供逐人表比对的唯一权威口径)
        internal static int RosterMenOf(MobileParty p)
        {
            try
            {
                if (p == null || p.MemberRoster == null) return -1;
                int n = 0;
                foreach (var e in p.MemberRoster.GetTroopRoster())
                {
                    var c = e.Character;
                    if (c == null || c.IsHero || e.Number <= 0) continue;
                    n += e.Number;
                }
                return n;
            }
            catch { return -1; }
        }

        private static byte AvgProfOf(Dictionary<byte, long> sum, Dictionary<byte, int> cnt, byte unit)
        {
            try
            {
                int c;
                if (!cnt.TryGetValue(unit, out c) || c <= 0) return 35;
                long s;
                sum.TryGetValue(unit, out s);
                return ProfClamp((int)Math.Round((double)s / c));
            }
            catch { return 35; }
        }

        // 名单对账(v4.234): 逐人表必须与名册(非英雄)人数一致
        //   原版途径(领主 AI 自行募兵/俘虏入队/守备营调拨/合并)会让名册增长而逐人表不动,
        //   于是"现有兵力"与分裂上限被低报(实测 100 人部队最多只能分出 65 人)。
        //   只对账总数、保持既有 14 兵种构成比例(不按名册分支重算, 免得把精细兵种抹平)。
        internal static int Reconcile(MobileParty p)
        {
            int changed = 0;
            try
            {
                if (p == null || p.MemberRoster == null) return 0;
                var rec = Get(p);
                if (rec == null) return 0;
                int rosterMen = RosterMenOf(p);
                if (rosterMen < 0) return 0;

                // ① 多退: 表比名册多 -> 从表尾削减
                while (rec.List.Count > rosterMen)
                {
                    rec.List.RemoveAt(rec.List.Count - 1);
                    changed++;
                }

                // ② 少补
                int need = rosterMen - rec.List.Count;
                if (need <= 0) return changed;

                if (rec.List.Count == 0)
                {
                    // 空表(旧档/新队): 按 27.12 从名册折算一次
                    FillFromRoster(rec, p.MemberRoster);
                    while (rec.List.Count > rosterMen) rec.List.RemoveAt(rec.List.Count - 1);
                    changed += rec.List.Count;
                    return changed;
                }

                // 按现有构成比例补齐, 余数给人数最多的兵种
                var cnt = new Dictionary<byte, int>();
                var sum = new Dictionary<byte, long>();
                for (int i = 0; i < rec.List.Count; i++)
                {
                    byte u = rec.List[i].Unit;
                    int o;
                    cnt.TryGetValue(u, out o);
                    cnt[u] = o + 1;
                    long s;
                    sum.TryGetValue(u, out s);
                    sum[u] = s + rec.List[i].Prof;
                }
                int total = rec.List.Count;
                byte biggest = 0;
                int bigN = -1;
                int added = 0;
                foreach (var kv in cnt)
                {
                    if (kv.Value > bigN) { bigN = kv.Value; biggest = kv.Key; }
                    int add = (int)Math.Round((double)need * kv.Value / total);
                    if (add <= 0) continue;
                    byte pr = AvgProfOf(sum, cnt, kv.Key);
                    for (int i = 0; i < add; i++) rec.List.Add(new SoldierRec { Unit = kv.Key, Prof = pr });
                    added += add;
                }
                byte bigProf = AvgProfOf(sum, cnt, biggest);
                for (int i = added; i < need; i++) rec.List.Add(new SoldierRec { Unit = biggest, Prof = bigProf });
                while (rec.List.Count > rosterMen)   // 四舍五入可能多补: 回削到与名册一致
                {
                    rec.List.RemoveAt(rec.List.Count - 1);
                }
                changed += need;
            }
            catch { }
            return changed;
        }

        // 聚合: 逐兵种人数 + 平均熟练; 战斗/UI 各一次 O(n)(28.5)
        internal static void Aggregate(MobileParty p, out Dictionary<string, int> men, out Dictionary<string, float> avgProf)
        {
            men = new Dictionary<string, int>(StringComparer.Ordinal);
            avgProf = new Dictionary<string, float>(StringComparer.Ordinal);
            try
            {
                var rec = Ensure(p);
                if (rec == null) return;
                Reconcile(p);   // v4.234: 读之前先与名册对账(与名册一致才不会被低报)
                if (rec.List.Count == 0) return;
                var sums = new Dictionary<string, long>(StringComparer.Ordinal);
                for (int i = 0; i < rec.List.Count; i++)
                {
                    string id = UnitIdOf(rec.List[i].Unit);
                    int n;
                    men.TryGetValue(id, out n);
                    men[id] = n + 1;
                    long s;
                    sums.TryGetValue(id, out s);
                    sums[id] = s + rec.List[i].Prof;
                }
                foreach (var kv in men)
                {
                    long s;
                    sums.TryGetValue(kv.Key, out s);
                    avgProf[kv.Key] = kv.Value > 0 ? (float)s / kv.Value : 0f;
                }
            }
            catch { }
        }

        // 逐人生成(新兵 20~30, 28.5)
        internal static bool Add(MobileParty p, string troopId, int n, int prof)
        {
            try
            {
                if (p == null || n <= 0) return false;
                int idx = UnitIndexOf(troopId);
                if (idx < 0) return false;
                var rec = Ensure(p);
                if (rec == null) return false;
                byte pr = ProfClamp(prof);
                for (int i = 0; i < n; i++)
                    rec.List.Add(new SoldierRec { Unit = (byte)idx, Prof = pr });
                return true;
            }
            catch { return false; }
        }

        // 伤亡: 逐人淘汰(按兵种比例), 返回实际淘汰数(28.5)
        internal static int RemoveKilled(MobileParty p, Dictionary<string, float> ratioByUnit)
        {
            int removed = 0;
            try
            {
                if (p == null || ratioByUnit == null) return 0;
                var rec = Ensure(p);
                if (rec == null || rec.List.Count == 0) return 0;
                foreach (var kv in ratioByUnit)
                {
                    if (kv.Value <= 0f) continue;
                    int idx = UnitIndexOf(kv.Key);
                    if (idx < 0) continue;
                    float ratio = kv.Value > 1f ? 1f : kv.Value;
                    var pos = new List<int>();
                    for (int i = 0; i < rec.List.Count; i++)
                        if (rec.List[i].Unit == idx) pos.Add(i);
                    int loss = (int)Math.Round(pos.Count * (double)ratio);
                    if (loss <= 0 && pos.Count > 0) loss = 1;
                    if (loss > pos.Count) loss = pos.Count;
                    for (int k = 0; k < loss; k++)
                    {
                        rec.List.RemoveAt(pos[pos.Count - 1 - k]);
                        removed++;
                    }
                }
            }
            catch { }
            return removed;
        }

        internal static int RemoveKilled(MobileParty p, float ratio)
        {
            var map = new Dictionary<string, float>(StringComparer.Ordinal);
            try
            {
                var rec = Get(p);
                if (rec == null) rec = Ensure(p);
                if (rec != null)
                    for (int i = 0; i < rec.List.Count; i++) map[UnitIdOf(rec.List[i].Unit)] = ratio;
            }
            catch { }
            return RemoveKilled(p, map);
        }

        // 分裂: 按比例把逐人记录移入新部队(新部队带独立逐人表, 28.6)
        internal static int Split(MobileParty src, MobileParty dst, float ratio)
        {
            int moved = 0;
            try
            {
                if (src == null || dst == null || ReferenceEquals(src, dst)) return 0;
                var a = Get(src);
                if (a == null) a = Ensure(src);
                if (a == null) return 0;
                var b = EnsureEmpty(dst);
                if (b == null) return 0;
                if (ratio < 0f) ratio = 0f;
                if (ratio > 1f) ratio = 1f;
                var counts = new Dictionary<byte, int>();
                for (int i = 0; i < a.List.Count; i++)
                {
                    int old;
                    counts.TryGetValue(a.List[i].Unit, out old);
                    counts[a.List[i].Unit] = old + 1;
                }
                foreach (var kv in counts)
                {
                    int take = (int)Math.Round(kv.Value * (double)ratio);
                    if (take <= 0) continue;
                    if (take > kv.Value) take = kv.Value;
                    var pos = new List<int>();
                    for (int i = 0; i < a.List.Count; i++)
                        if (a.List[i].Unit == kv.Key) pos.Add(i);
                    for (int k = 0; k < take; k++)
                    {
                        var s = a.List[pos[pos.Count - 1 - k]];
                        b.List.Add(new SoldierRec { Unit = s.Unit, Prof = s.Prof, Kills = s.Kills });
                        a.List.RemoveAt(pos[pos.Count - 1 - k]);
                        moved++;
                    }
                }
            }
            catch { }
            return moved;
        }

        // 合并: 逐人表并入(合并逐人 + 编成, 28.6)
        internal static int Merge(MobileParty dst, MobileParty src)
        {
            int moved = 0;
            try
            {
                if (dst == null || src == null || ReferenceEquals(dst, src)) return 0;
                var b = Get(src);
                if (b == null) return 0;
                var a = Ensure(dst);
                if (a == null) return 0;
                for (int i = 0; i < b.List.Count; i++)
                {
                    var s = b.List[i];
                    a.List.Add(new SoldierRec { Unit = s.Unit, Prof = s.Prof, Kills = s.Kills });
                    moved++;
                }
                Data.Remove(IdOf(src));
            }
            catch { }
            return moved;
        }

        internal static int CountOf(MobileParty p)
        {
            try
            {
                var rec = Ensure(p);
                if (rec == null) return 0;
                Reconcile(p);   // v4.234: 与名册对账后再报数(分裂上限/合并上限都读这里)
                return rec.List.Count;
            }
            catch { return 0; }
        }

        internal static void Remove(MobileParty p)
        {
            try
            {
                string id = IdOf(p);
                if (!string.IsNullOrEmpty(id)) Data.Remove(id);
            }
            catch { }
        }

        // 熟练度影响接口(28.5): 0.75 + 0.5 × (平均熟练 ÷ 100); 无记录 = 1(不惩罚)
        internal static float ProfMult(MobileParty p)
        {
            try
            {
                var rec = Ensure(p);
                if (rec == null) return 1f;
                Reconcile(p);
                if (rec.List.Count == 0) return 1f;
                long sum = 0;
                for (int i = 0; i < rec.List.Count; i++) sum += rec.List[i].Prof;
                float avg = (float)sum / rec.List.Count;
                return 0.75f + 0.5f * (avg / 100f);
            }
            catch { return 1f; }
        }

        private static byte ProfClamp(int prof)
        {
            if (prof < 0) return 0;
            if (prof > 100) return 100;
            return (byte)prof;
        }

        // ==================== v4.243: 熟练度成长 / 战斗经验 / 个人击杀 ====================
        //   原来逐人熟练度只在入伍时定一次(InitProf), 之后永远不变; 个人击杀 Kills 也从来没人加过。
        //   现在: 操练/驻防每日成长(越高越慢), 战斗按胜负与伤亡给经验, 击杀真实累计。
        private static float ProfGrowRate(int prof)
        {
            if (prof < 60) return 1.00f;    // 新兵期长得快
            if (prof < 85) return 0.50f;    // 老兵期减半
            return 0.25f;                   // 精锐期极慢(上限 100)
        }

        // 每日操练成长(amount = 基础点数, 按个人熟练度衰减)
        internal static int TrainUp(MobileParty p, float amount)
        {
            try
            {
                if (p == null || amount <= 0f) return 0;
                var rec = Ensure(p);
                if (rec == null || rec.List.Count == 0) return 0;
                Reconcile(p);
                int up = 0;
                for (int i = 0; i < rec.List.Count; i++)
                {
                    var s = rec.List[i];
                    if (s.Prof >= 100) continue;
                    float g = amount * ProfGrowRate(s.Prof);
                    if (g < 0.02f) continue;
                    s.Prof = ProfClamp((int)Math.Round(s.Prof + g));
                    up++;
                }
                return up;
            }
            catch { return 0; }
        }

        // 战斗经验(胜负 + 交换比): 幸存者按个人熟练度衰减获得
        internal static int BattleExp(MobileParty p, float amount)
        {
            return TrainUp(p, amount);
        }

        // 个人击杀累计(按本场我方总击杀摊到活着的士兵头上)
        internal static void AddKills(MobileParty p, int totalKills)
        {
            try
            {
                if (p == null || totalKills <= 0) return;
                var rec = Ensure(p);
                if (rec == null || rec.List.Count == 0) return;
                Reconcile(p);
                int per = Math.Max(1, totalKills / Math.Max(1, rec.List.Count));
                int left = totalKills;
                for (int i = 0; i < rec.List.Count && left > 0; i++)
                {
                    int add = Math.Min(per, left);
                    var s = rec.List[i];
                    int k = s.Kills + add;
                    s.Kills = (ushort)(k > 65535 ? 65535 : k);
                    left -= add;
                }
            }
            catch { }
        }

        // 老兵占比(熟练度 ≥ 阈值 的人数比例, 0~1) + 平均熟练
        internal static float VeteranShare(MobileParty p, int profThreshold, out float avgProf)
        {
            avgProf = 0f;
            try
            {
                var rec = Ensure(p);
                if (rec == null) return 0f;
                Reconcile(p);
                if (rec.List.Count == 0) return 0f;
                long sum = 0;
                int vet = 0;
                for (int i = 0; i < rec.List.Count; i++)
                {
                    sum += rec.List[i].Prof;
                    if (rec.List[i].Prof >= profThreshold) vet++;
                }
                avgProf = (float)sum / rec.List.Count;
                return vet / (float)rec.List.Count;
            }
            catch { return 0f; }
        }

        // 全军个人击杀合计(部队管理页/悬停展示)
        internal static long TotalKills(MobileParty p)
        {
            try
            {
                var rec = Ensure(p);
                if (rec == null || rec.List.Count == 0) return 0L;
                long k = 0;
                for (int i = 0; i < rec.List.Count; i++) k += rec.List[i].Kills;
                return k;
            }
            catch { return 0L; }
        }

        // ==================== 存档 FIA_Soldiers(28.5/28.12) ====================
        internal static string Save()
        {
            try
            {
                var sb = new StringBuilder("v1");
                foreach (var kv in Data)
                {
                    if (kv.Value == null || kv.Value.List.Count == 0 || string.IsNullOrEmpty(kv.Key)) continue;
                    sb.Append(';').Append(kv.Key).Append('|').Append(Encode(kv.Value.List));
                }
                return sb.ToString();
            }
            catch { return "v1"; }
        }

        internal static void Load(string data)
        {
            try
            {
                Data.Clear();
                if (string.IsNullOrEmpty(data)) return;
                var parts = data.Split(';');
                if (parts.Length < 2 || parts[0] != "v1") return;
                for (int i = 1; i < parts.Length; i++)
                {
                    string line = parts[i];
                    if (string.IsNullOrEmpty(line)) continue;
                    int at = line.IndexOf('|');
                    if (at <= 0 || at >= line.Length - 1) continue;
                    var rec = Decode(line.Substring(at + 1));
                    if (rec != null) Data[line.Substring(0, at)] = rec;
                }
                DLog.Force("逐人熟练度: 读档 部队=" + Data.Count);
            }
            catch (Exception ex) { DLog.Force("逐人熟练度读档异常: " + ex.Message); }
        }

        internal static void Reset()
        {
            try { Data.Clear(); } catch { }
        }

        // 紧凑字节: [人数 int32][每人: 兵种 1 + 熟练 1 + 击杀 2]
        private static string Encode(List<SoldierRec> list)
        {
            var bytes = new byte[4 + list.Count * 4];
            int n = list.Count;
            bytes[0] = (byte)(n & 0xFF);
            bytes[1] = (byte)((n >> 8) & 0xFF);
            bytes[2] = (byte)((n >> 16) & 0xFF);
            bytes[3] = (byte)((n >> 24) & 0xFF);
            int o = 4;
            for (int i = 0; i < list.Count; i++)
            {
                var s = list[i];
                bytes[o++] = s.Unit;
                bytes[o++] = s.Prof;
                bytes[o++] = (byte)(s.Kills & 0xFF);
                bytes[o++] = (byte)((s.Kills >> 8) & 0xFF);
            }
            return Convert.ToBase64String(bytes);
        }

        private static PartySoldiers Decode(string b64)
        {
            try
            {
                if (string.IsNullOrEmpty(b64)) return null;
                var bytes = Convert.FromBase64String(b64);
                if (bytes.Length < 4) return null;
                int n = bytes[0] | (bytes[1] << 8) | (bytes[2] << 16) | (bytes[3] << 24);
                long max = (bytes.Length - 4) / 4;
                if (n <= 0) return new PartySoldiers();
                if (n > max) n = (int)max;
                var rec = new PartySoldiers();
                int o = 4;
                for (int i = 0; i < n; i++)
                {
                    byte unit = bytes[o];
                    byte prof = bytes[o + 1];
                    if (prof > 100) prof = 100;
                    ushort kills = (ushort)(bytes[o + 2] | (bytes[o + 3] << 8));
                    rec.List.Add(new SoldierRec { Unit = unit, Prof = prof, Kills = kills });
                    o += 4;
                }
                return rec;
            }
            catch { return null; }
        }
    }

    // 28.2/28.12 存档接线: FIA_Soldiers(逐人表) + FIA_Converted(建档转化标记)
    //   旧档无此两段 -> 读档后宽容补跑一次转化(28.12)
    internal class SoldiersBehavior : CampaignBehaviorBase
    {
        internal static SoldiersBehavior Current;

        internal SoldiersBehavior()
        {
            Current = this;
        }

        public override void RegisterEvents()
        {
            Current = this;
            CampaignEvents.OnNewGameCreatedEvent.AddNonSerializedListener(this, OnNewGameCreated);
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.OnGameLoadedEvent.AddNonSerializedListener(this, OnGameLoaded);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
        }

        public override void SyncData(IDataStore dataStore)
        {
            try
            {
                if (dataStore.IsSaving)
                {
                    SyncChunks.Save(dataStore, "FIA_Soldiers", Soldiers.Save());
                    SyncChunks.Save(dataStore, "FIA_LordArmy", LordArmy.Save());   // 28.4 家族欠饷借债
                    bool converted = DefArmy.Converted;
                    dataStore.SyncData("FIA_Converted", ref converted);
                }
                if (dataStore.IsLoading)
                {
                    Soldiers.Load(SyncChunks.Load(dataStore, "FIA_Soldiers"));
                    LordArmy.Load(SyncChunks.Load(dataStore, "FIA_LordArmy"));
                    bool converted = false;
                    dataStore.SyncData("FIA_Converted", ref converted);
                    DefArmy.Converted = converted;
                }
            }
            catch (Exception ex) { DLog.Force("逐人熟练度 SyncData 异常: " + ex.Message); }
        }

        private void OnNewGameCreated(CampaignGameStarter starter)
        {
            try
            {
                Soldiers.Reset();
                LordArmy.Reset();
                DefArmy.Converted = false;
            }
            catch { }
        }

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            DefArmy.EnsureConverted();   // 新战役首次进入地图 -> 建档转化一次(28.2)
            try { DefArmy.TempFillIfReady(); } catch { }   // TEMP: 读档进入时玩家王国已确定 -> 补给一次
        }

        private void OnGameLoaded(CampaignGameStarter starter)
        {
            DefArmy.EnsureConverted();   // 旧档缺 FIA_Converted -> 宽容补跑一次(28.12)
            try { DefArmy.TempFillIfReady(); } catch { }   // TEMP: 读档后玩家王国已确定 -> 补给一次
        }

        private void OnDailyTick()
        {
            if (!DefArmy.Converted) DefArmy.EnsureConverted();
            try { DefArmy.TempFillIfReady(); } catch { }   // TEMP: 玩家王国确定后补给一次(新档选国完成)
            LordArmy.DailyWages();   // 28.4 领主军饷: 扣饷 → 家族借债 → 欠饷逃兵
        }
    }
}
