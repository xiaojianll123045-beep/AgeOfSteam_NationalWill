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

        // ==================== v4.267: 逐人表只服务"我方"(存档体积) ====================
        //   问题: BattleSim.SnapshotSide 对**每一支**参战部队调 DefArmy.ShareOf -> Soldiers.Ensure,
        //         于是每一支 AI 部队都被建了一份逐人表(1000 人 ≈ 4KB), 长期游玩 FIA_Soldiers 无限膨胀。
        //   做法: "我方"(玩家家族部队 + 国防军 fia_def_* + 玩家王国领主军)照旧建表/存表/熟练度成长;
        //         其余(敌国/中立 AI)改为"按原版兵种折算"的轻量口径(每次现算, 不建表、不入档), 折算规则
        //         与 FillFromRoster 完全一致, 所以它们拿到的兵种构成/占比/熟练 与本改动之前**逐人表首次
        //         生成的结果一致** -> 战斗力不掉。膨胀主要来自"全世界部队", 玩家国部队数量有限。
        internal static bool IsPlayerSideParty(MobileParty p)
        {
            try
            {
                if (p == null) return false;
                if (p.IsMainParty) return true;                                  // 玩家主队
                var owner = p.Owner;
                if (owner != null && owner.Clan != null && owner.Clan == Clan.PlayerClan) return true;   // 玩家家族部队
                // 国防军(fia_def_)与部队管理分裂出的新队(fia_amg_): 由 mod 用 CreateParty 建的玩家方部队。
                //   注意: 它们在 InitializeMobilePartyAtPosition 之前 Owner 还是空, 只靠家族判定会漏掉 ->
                //   所以这里按前缀兜底(原版 AI 部队的 StringId 不会以 fia_ 开头, 不会误收)。
                if (p.StringId != null && p.StringId.StartsWith("fia_", StringComparison.Ordinal)) return true;
                if (DefArmy.LegionOf(p) != null) return true;
                // v4.267: 玩家王国的领主军 —— 玩家看得到、能临时指挥, 逐人明细值得保留。两种口径任一命中即可:
                //   ① MapFaction(战斗中/军团里都稳)  ② ActualClan.Kingdom(军队里被临时抽调时 MapFaction 可能变)
                var pk = PlayerKingdom();
                if (pk != null)
                {
                    if (ReferenceEquals(p.MapFaction, pk)) return true;
                    var clan = p.ActualClan;
                    if (clan != null && ReferenceEquals(clan.Kingdom, pk)) return true;
                }
                // 正在跟玩家王国交战的敌国部队: 明确不建表(全世界部队里数量最多的一类)。
                return !IsEnemyOfPlayer(p);
            }
            catch { return false; }
        }

        // 玩家王国(内政侧缓存的 NationKingdom; 没有则回退玩家家族所属王国)
        private static Kingdom PlayerKingdom()
        {
            try
            {
                var b = NationalWillOrders.Behavior;
                if (b != null && b.NationKingdom != null) return b.NationKingdom;
                var c = Clan.PlayerClan;
                if (c != null && c.Kingdom != null) return c.Kingdom;
            }
            catch { }
            return null;
        }

        // v4.267: 玩家王国的交战国集合(按 "王国数 + 游戏日" 指纹缓存, 避免每支部队都遍历一遍全世界王国)。
        //   只用于"明确排除敌国", 所以指纹窗口内(同一天)新宣战的敌国最多晚一天被排除, 无正确性风险。
        private static List<Kingdom> _enemyKingdoms;
        private static int _enemyStamp = -1;

        private static bool IsEnemyOfPlayer(MobileParty p)
        {
            try
            {
                var pk = PlayerKingdom();
                if (pk == null) return false;
                int now = Kingdom.All != null ? Kingdom.All.Count : 0;
                int day = 0;
                try { day = (int)TaleWorlds.CampaignSystem.CampaignTime.Now.ToDays; } catch { }
                int stamp = now * 100000 + (day & 0xFFFF);   // 王国数变了 / 换天了 -> 重建
                if (_enemyKingdoms == null || _enemyStamp != stamp)
                {
                    var list = new List<Kingdom>();
                    for (int i = 0; i < now; i++)
                    {
                        var k = Kingdom.All[i];
                        if (k == null || k.IsEliminated || ReferenceEquals(k, pk)) continue;
                        bool war = false;
                        try { war = pk.IsAtWarWith(k); } catch { }
                        if (war) list.Add(k);
                    }
                    _enemyKingdoms = list;
                    _enemyStamp = stamp;
                }
                var kp = p.MapFaction as Kingdom;
                if (kp == null) return false;
                for (int i = 0; i < _enemyKingdoms.Count; i++)
                    if (ReferenceEquals(_enemyKingdoms[i], kp)) return true;
            }
            catch { }
            return false;
        }

        // 武器/骑乘分支 -> 兵种下标: 与 FillFromRoster 同一套规则(不重复实现, 避免两条口径漂移)
        internal static int UnitIndexOfTroop(CharacterObject c)
        {
            try
            {
                if (c == null) return -1;
                int branch = c.IsMounted ? (c.IsRanged ? 3 : 2) : (c.IsRanged ? 1 : 0);
                int idx = Equipment.UnitOfBranch(branch, c.Tier);
                return (idx >= 0 && idx < UnknownUnit) ? idx : 0;
            }
            catch { return -1; }
        }

        // 按名册现算一份**临时**逐人表(绝不进 Data/存档): AI 部队的轻量口径
        internal static PartySoldiers DerivedFromRoster(MobileParty p)
        {
            try
            {
                var rec = new PartySoldiers();
                FillFromRoster(rec, p != null ? p.MemberRoster : null);
                return rec;
            }
            catch { return null; }
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
        //   v4.267: **只给玩家方建表** —— 非玩家方(含全部 AI 部队)一律返回 null, 所以 FIA_Soldiers 不会再被
        //   AI 部队撑大(原来 BattleSim 快照对每支参战部队调 ShareOf -> Ensure, 几十上百支部队各建一份)。
        //   注意: 老存档里已存在的表由 Load 统一丢弃(不载入, 见 Load), 因此这里不会误用到旧 AI 表。
        internal static PartySoldiers Ensure(MobileParty p)
        {
            try
            {
                string id = IdOf(p);
                if (string.IsNullOrEmpty(id)) return null;
                if (!IsPlayerSideParty(p)) return null;   // v4.267: AI 不建表(改走按原版兵种折算的轻量口径)
                PartySoldiers rec;
                if (Data.TryGetValue(id, out rec) && rec != null) return rec;
                rec = new PartySoldiers();
                try { FillFromRoster(rec, p.MemberRoster); }
                catch (Exception ex) { DLog.Force("逐人表折算异常 " + id + ": " + ex.Message); }   // v4.267: 建表是关键路径, 不再静默
                Data[id] = rec;
                return rec;
            }
            catch (Exception ex) { DLog.Force("逐人表建表异常: " + ex.Message); return null; }   // v4.267
        }

        // v4.267: 读取用入口 —— 玩家方 = 数据库里的真表(懒初始化); 其它 = 按名册现算的**临时**表(不入档)。
        //   用它替代"读路径"上直接调 Ensure 的写法: 语义不变(折算规则与首次建表完全一致), 但 AI 不再产生记录。
        internal static PartySoldiers EnsureLight(MobileParty p)
        {
            try
            {
                string id = IdOf(p);
                if (string.IsNullOrEmpty(id)) return null;
                if (IsPlayerSideParty(p)) return Ensure(p);
                return DerivedFromRoster(p);   // AI: 现算, 不缓存
            }
            catch (Exception ex) { DLog.Force("逐人表折算异常: " + ex.Message); return null; }   // v4.267
        }

        // AI 部队的"兵种人数"轻量口径: 按原版名册 + 分支/等级折算(不建表、不入档)。
        //   BattleSim 的快照里每支部队会被查 5~6 次占比, 用调用方传入的 cache 摊掉重复折算。
        internal static Dictionary<int, int> LightUnitMen(MobileParty p, Dictionary<string, Dictionary<int, int>> cache)
        {
            try
            {
                var rec = EnsureLight(p);
                if (rec == null) return null;
                string id = IdOf(p);
                Dictionary<int, int> r = null;
                if (cache != null && !string.IsNullOrEmpty(id) && cache.TryGetValue(id, out r) && r != null) return r;
                r = new Dictionary<int, int>();
                for (int i = 0; i < rec.List.Count; i++)
                {
                    int u = rec.List[i].Unit;
                    int old;
                    r.TryGetValue(u, out old);
                    r[u] = old + 1;
                }
                if (cache != null && !string.IsNullOrEmpty(id)) cache[id] = r;
                return r;
            }
            catch (Exception ex) { DLog.Force("逐人表轻量折算异常: " + ex.Message); return null; }   // v4.267
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
            try
            {
                foreach (var e in roster.GetTroopRoster())
                {
                    var c = e.Character;
                    if (c == null || c.IsHero || e.Number <= 0) continue;
                    int idx = UnitIndexOfTroop(c);
                    if (idx < 0 || idx >= UnknownUnit) idx = 0;
                    int tier = c.Tier;
                    if (tier < 1) tier = 1;
                    if (tier > 6) tier = 6;
                    byte prof = InitProf(tier);
                    for (int i = 0; i < e.Number; i++)
                        rec.List.Add(new SoldierRec { Unit = (byte)idx, Prof = prof });
                }
            }
            // v4.267: 折算失败原来会静默留下"空表"(看起来就是 0 兵) -> 进正式日志
            catch (Exception ex) { DLog.Force("逐人表名册折算异常: " + ex.Message); }
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
                if (!IsPlayerSideParty(p)) return false;   // v4.267: 建军设计器只对玩家方(国防军)成立军团
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
                if (!IsPlayerSideParty(p)) return 0;   // v4.267: AI 已不持有逐人表 -> 不需要(也不允许)对账扩表
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
                // v4.267: 玩家方 = 库里的真表(先与名册对账, v4.234); AI = 按名册现算的临时表
                //   (折算规则与旧版首日建表完全一致 -> 兵种人数/平均熟练不变, 只是不产生存档记录)
                var rec = EnsureLight(p);
                if (rec == null) return;
                if (IsPlayerSideParty(p)) Reconcile(p);
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
                // v4.267: 玩家方先懒初始化 + 与名册对账(分裂/合并上限都读这里); AI 没有逐人表 ->
                //   直接用名册非英雄人数(与旧版 AI 空表首次折算的结果同值), 且不会因为调用而长出表来。
                if (!IsPlayerSideParty(p)) return Math.Max(0, RosterMenOf(p));
                var rec = Ensure(p);
                if (rec == null) return Math.Max(0, RosterMenOf(p));
                Reconcile(p);
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
                    // v4.256: g 通常 ≤0.15(日常操练 0.15/人/日), 而 Math.Round(p+0.15)==p 恒成立 ->
                    //   取整把增长全吞了(审计: 熟练度实际硬上限=85, "精锐期 85~100"整段是死区)。
                    //   改成小数部分按概率进位: 期望增长仍等于 g, 但不再被取整吃掉。
                    int inc = (int)g;
                    float frac = g - inc;
                    if (frac > 0f && TaleWorlds.Core.MBRandom.RandomFloat < frac) inc++;
                    if (inc <= 0) continue;
                    s.Prof = ProfClamp(s.Prof + inc);
                    rec.List[i] = s;      // v4.255(P0-4): SoldierRec 是结构体, 必须写回 —— 原来只改副本, 熟练度永远不涨
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
                    rec.List[i] = s;      // v4.255(P0-4): 结构体写回, 原来个人击杀永远不累计
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
                DropCaches();   // v4.267: 读档时部队索引/敌国集合必须重建
                if (string.IsNullOrEmpty(data)) return;
                var parts = data.Split(';');
                if (parts.Length < 2 || parts[0] != "v1") return;
                // v4.267: 先全部解析出来。老档里 AI 部队的逐人表**跳过加载**(它们现在走"按原版兵种折算"的
                //   轻量口径; 载入只会在下次存档时把几十上百支 AI 部队的逐人表原样写回去 = 存档继续膨胀)。
                //   读档兼容: 找不到部队/判不出玩家方的只是不加载, 不报错、不改动原档(下次存档自然瘦身);
                //   玩家家族部队与国防军(fia_def_/fia_amg_)一律照旧载入, 熟练度/老兵/击杀行为完全不变。
                var ids = new List<string>();
                var recs = new List<PartySoldiers>();
                for (int i = 1; i < parts.Length; i++)
                {
                    string line = parts[i];
                    if (string.IsNullOrEmpty(line)) continue;
                    int at = line.IndexOf('|');
                    if (at <= 0 || at >= line.Length - 1) continue;
                    var rec = Decode(line.Substring(at + 1));
                    if (rec == null) continue;
                    ids.Add(line.Substring(0, at));
                    recs.Add(rec);
                }
                // v4.267 安全阀: 存档系统调用 SyncData 的时机可能早于部队枚举就绪 —— 若**一条都匹配不上**
                //   (说明当下根本查不到部队, 而不是"这些都是 AI 部队"), 就退回"全量载入", 宁可多留一点体积,
                //   也绝不误删玩家方的熟练度/老兵/击杀。
                int matched = 0;
                for (int i = 0; i < ids.Count; i++)
                    if (PartyOf(ids[i]) != null) { matched++; break; }
                if (matched == 0)
                {
                    for (int i = 0; i < ids.Count; i++) Data[ids[i]] = recs[i];
                    if (ids.Count > 0)
                        DLog.Force("逐人熟练度: 读档时查不到部队(枚举未就绪?) -> 全量载入 " + ids.Count + " 条, 稍后由 PruneNonPlayer 过滤");
                    return;
                }
                for (int i = 0; i < ids.Count; i++)
                {
                    var mp = PartyOf(ids[i]);
                    if (mp == null || !IsPlayerSideParty(mp)) continue;
                    Data[ids[i]] = recs[i];
                }
                DLog.Force("逐人熟练度: 读档 部队=" + Data.Count);
            }
            catch (Exception ex) { DLog.Force("逐人熟练度读档异常: " + ex.Message); }
        }

        // v4.267: 存档 id -> 现存部队(找不到返回 null, 由调用方跳过)。
        //   读档时一次要查几十上百条, 这里用"部队枚举指纹"做一次 O(n) 建索引, 避免每次线性扫描变成 O(n²)。
        private static Dictionary<string, MobileParty> _partyIndex;
        private static int _partyAllCount = -1;

        internal static MobileParty PartyOf(string partyId)
        {
            try
            {
                if (string.IsNullOrEmpty(partyId)) return null;
                var all = MobileParty.All;
                if (all == null) return null;
                if (_partyIndex == null || _partyAllCount != all.Count)
                {
                    _partyIndex = new Dictionary<string, MobileParty>(StringComparer.Ordinal);
                    for (int i = 0; i < all.Count; i++)
                    {
                        var p = all[i];
                        if (p == null || string.IsNullOrEmpty(p.StringId)) continue;
                        _partyIndex[p.StringId] = p;
                    }
                    _partyAllCount = all.Count;
                }
                MobileParty hit;
                return _partyIndex.TryGetValue(partyId, out hit) ? hit : null;
            }
            catch (Exception ex) { DLog.Force("逐人熟练度部队查找异常: " + ex.Message); }   // v4.267: 读档路径
            return null;
        }

        // v4.267: 兜底清理 —— 读档过滤(Load)已经在源头跳过非玩家方记录; 这里在进地图后再扫一次, 处理
        //   "读档时部队还没建出来"这类极端情况。只删**确实存在且不是玩家方**的记录(找不到的保留, 不误删);
        //   玩家家族部队与国防军一律保留(不动既有熟练度成长/老兵占比/击杀统计)。
        internal static int PruneNonPlayer()
        {
            int n = 0;
            try
            {
                var drop = new List<string>();
                foreach (var kv in Data)
                {
                    var mp = PartyOf(kv.Key);
                    if (mp == null) continue;              // 暂时找不到的(如队伍已解散/未重建)不动, 免得误删
                    if (IsPlayerSideParty(mp)) continue;
                    drop.Add(kv.Key);
                }
                for (int i = 0; i < drop.Count; i++) { Data.Remove(drop[i]); n++; }
                if (n > 0) DLog.Force("逐人熟练度: 清理非玩家方(旧档 AI 部队)记录 " + n + " 条");
            }
            catch (Exception ex) { DLog.Force("逐人熟练度清理异常: " + ex.Message); }
            return n;
        }

        internal static void Reset()
        {
            try { Data.Clear(); DropCaches(); } catch { }   // v4.267: 连部队/敌国索引一起失效
        }

        // v4.267: 战役级缓存(部队 id 索引 / 敌国集合)统一失效
        private static void DropCaches()
        {
            _partyIndex = null; _partyAllCount = -1;
            _enemyKingdoms = null; _enemyStamp = -1;
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
                    // v4.255(P0-6): 亲自指挥期间"暂时移出"的玩家部队兵员落盘(不然战斗中存档/退出 = 永久丢兵)
                    SyncChunks.Save(dataStore, "FIA_CmdRoster", BattleCommand.SavePendingRoster());
                    bool converted = DefArmy.Converted;
                    dataStore.SyncData("FIA_Converted", ref converted);
                }
                if (dataStore.IsLoading)
                {
                    Soldiers.Load(SyncChunks.Load(dataStore, "FIA_Soldiers"));
                    LordArmy.Load(SyncChunks.Load(dataStore, "FIA_LordArmy"));
                    BattleCommand.LoadPendingRoster(SyncChunks.Load(dataStore, "FIA_CmdRoster"));   // v4.255(P0-6)
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
            // v4.267: 建档(逐人表复位)是关键路径, 出问题不再静默
            catch (Exception ex) { DLog.Force("逐人熟练度新档复位异常: " + ex.Message); }
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
            try { BattleCommand.OnGameLoadedRestore(); } catch { }   // v4.255(P0-6): 补放回上次"参战暂存"的兵员
            // v4.267: 读档后清理老档遗留的 AI 部队逐人表(玩家方/国防军一律保留)
            try { Soldiers.PruneNonPlayer(); }
            catch (Exception ex) { DLog.Force("逐人熟练度读档清理异常: " + ex.Message); }
        }

        private void OnDailyTick()
        {
            if (!DefArmy.Converted) DefArmy.EnsureConverted();
            try { DefArmy.TempFillIfReady(); } catch { }   // TEMP: 玩家王国确定后补给一次(新档选国完成)
            LordArmy.DailyWages();   // 28.4 领主军饷: 扣饷 → 家族借债 → 欠饷逃兵
        }
    }
}
