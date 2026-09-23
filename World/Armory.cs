using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;

namespace FeudalInternalAffairs
{
    // 第 27 章: 两级军械库 + 军用工厂订单(27.3 / 27.4 / 27.17)
    //   · 库存: 国家军械库(K:<王国id>) + 领主私库(L:<家族id>), 逐型号计数
    //   · 订单: 型号 × 数量 × 优先级, 按工厂日产推进; 优先级 0=前线缺装(最高) 1=补编 2=储备
    //   · 付款: 国家订单花国库 / 领主订单花领主金(家族领袖)
    //   · 材料: 从市场扣(WarEconomy.TryConsumeMaterials), 铁/木/皮革/布/煤
    //   · 工厂: 等级 1~3 日产 10/20/35 点(1 点 = 1 件)
    //     建筑 def: military_factory(建筑智能体接入后自动生效) / arms_industry(现网等价物)
    //   · 存档段: FIA_Armory / FIA_Orders; 本轮白名单未含 DefArmyBehavior,
    //     故随 FIA_WarEco 内嵌落盘(见 WarEconomy.Save/Load), Save()/Load() 同时导出供后续接线
    internal static class Armory
    {
        internal const string SaveKeyArmory = "FIA_Armory";
        internal const string SaveKeyOrders = "FIA_Orders";
        internal const int MaxFactoryLevel = 3;
        private static readonly int[] FactoryOutput = { 10, 20, 35 };
        private const int MaxLog = 12;

        // 库存: ownerKey -> (equipId -> count)
        private static readonly Dictionary<string, Dictionary<string, int>> _stock =
            new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
        private static readonly List<OrderRec> _orders = new List<OrderRec>();
        private static readonly List<string> _log = new List<string>();
        private static int _lastDay = -1;

        internal sealed class OrderRec
        {
            internal string Owner = "";
            internal string EquipId = "";
            internal int Remaining;
            internal int Priority;
            internal int Done;
        }

        internal static List<string> RecentLog { get { return _log; } }
        internal static int OrderCount { get { return _orders.Count; } }

        // ==================== 所有者归一化 ====================
        internal static string NationalOwner(Kingdom k)
        {
            try { return k != null && !string.IsNullOrEmpty(k.StringId) ? "K:" + k.StringId : ""; }
            catch { return ""; }
        }

        internal static string LordOwner(Clan c)
        {
            try { return c != null && !string.IsNullOrEmpty(c.StringId) ? "L:" + c.StringId : ""; }
            catch { return ""; }
        }

        internal static string KeyOf(object owner)
        {
            try
            {
                if (owner == null) return "";
                var s = owner as string;
                if (s != null)
                {
                    if (s.StartsWith("K:", StringComparison.Ordinal) || s.StartsWith("L:", StringComparison.Ordinal)
                        || s.StartsWith("R:", StringComparison.Ordinal)) return s;
                    var k = FindKingdom(s);
                    if (k != null) return NationalOwner(k);
                    var c = FindClan(s);
                    if (c != null) return LordOwner(c);
                    return "";
                }
                var kk = owner as Kingdom;
                if (kk != null) return NationalOwner(kk);
                var cc = owner as Clan;
                if (cc != null) return LordOwner(cc);
                var h = owner as Hero;
                if (h != null) return h.Clan != null ? LordOwner(h.Clan) : "";
                var mp = owner as MobileParty;
                if (mp != null)
                {
                    if (DefArmy.IsDefArmyParty(mp)) return NationalOwner(mp.MapFaction as Kingdom);
                    var mpClan = mp.LeaderHero != null ? mp.LeaderHero.Clan : (mp.Party != null && mp.Party.Owner != null ? mp.Party.Owner.Clan : null);
                    return LordOwner(mpClan);
                }
                var pb = owner as PartyBase;
                if (pb != null)
                {
                    if (pb.MobileParty != null) return KeyOf(pb.MobileParty);
                    var pbClan = pb.Owner != null ? pb.Owner.Clan : null;
                    return LordOwner(pbClan);
                }
            }
            catch { }
            return "";
        }

        internal static string OwnerName(string key)
        {
            try
            {
                if (string.IsNullOrEmpty(key)) return "?";
                if (key.StartsWith("K:", StringComparison.Ordinal))
                {
                    var k = FindKingdom(key.Substring(2));
                    return k != null && k.Name != null ? k.Name.ToString() : key;
                }
                if (key.StartsWith("L:", StringComparison.Ordinal))
                {
                    var c = FindClan(key.Substring(2));
                    return c != null && c.Name != null ? c.Name.ToString() : key;
                }
            }
            catch { }
            return key;
        }

        internal static Kingdom KingdomOfKey(string key)
        {
            try
            {
                if (string.IsNullOrEmpty(key)) return null;
                if (key.StartsWith("K:", StringComparison.Ordinal)) return FindKingdom(key.Substring(2));
                if (key.StartsWith("L:", StringComparison.Ordinal))
                {
                    var c = FindClan(key.Substring(2));
                    return c != null ? c.Kingdom : null;
                }
            }
            catch { }
            return null;
        }

        private static Kingdom FindKingdom(string id)
        {
            try
            {
                if (string.IsNullOrEmpty(id)) return null;
                foreach (var k in Kingdom.All) if (k != null && k.StringId == id) return k;
            }
            catch { }
            return null;
        }

        private static Clan FindClan(string id)
        {
            try
            {
                if (string.IsNullOrEmpty(id)) return null;
                foreach (var c in Clan.All) if (c != null && c.StringId == id) return c;
            }
            catch { }
            return null;
        }

        // ==================== 库存 API (27.4) ====================
        internal static void Add(object owner, string equipId, int n)
        {
            AddKey(KeyOf(owner), equipId, n);
        }

        internal static void AddKey(string key, string equipId, int n)
        {
            try
            {
                if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(equipId) || n <= 0) return;
                Dictionary<string, int> d;
                if (!_stock.TryGetValue(key, out d))
                {
                    d = new Dictionary<string, int>(StringComparer.Ordinal);
                    _stock[key] = d;
                }
                int have;
                d.TryGetValue(equipId, out have);
                d[equipId] = have + n;
            }
            catch { }
        }

        internal static int Take(object owner, string equipId, int n)
        {
            return TakeKey(KeyOf(owner), equipId, n);
        }

        internal static int TakeKey(string key, string equipId, int n)
        {
            try
            {
                if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(equipId) || n <= 0) return 0;
                Dictionary<string, int> d;
                if (!_stock.TryGetValue(key, out d) || d == null) return 0;
                int have;
                if (!d.TryGetValue(equipId, out have) || have <= 0) return 0;
                int take = n < have ? n : have;
                int left = have - take;
                if (left <= 0) d.Remove(equipId);
                else d[equipId] = left;
                return take;
            }
            catch { return 0; }
        }

        internal static int Count(object owner, string equipId)
        {
            return CountKey(KeyOf(owner), equipId);
        }

        internal static int CountKey(string key, string equipId)
        {
            try
            {
                if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(equipId)) return 0;
                Dictionary<string, int> d;
                if (!_stock.TryGetValue(key, out d) || d == null) return 0;
                int have;
                return d.TryGetValue(equipId, out have) ? have : 0;
            }
            catch { return 0; }
        }

        internal static int TotalCount(string key)
        {
            try
            {
                Dictionary<string, int> d;
                if (string.IsNullOrEmpty(key) || !_stock.TryGetValue(key, out d) || d == null) return 0;
                int n = 0;
                foreach (var kv in d) n += kv.Value;
                return n;
            }
            catch { return 0; }
        }

        // 逐型号列出(军械库页用; UI 由其他智能体接入)
        internal static List<KeyValuePair<string, int>> EntriesOf(object owner)
        {
            var list = new List<KeyValuePair<string, int>>();
            try
            {
                Dictionary<string, int> d;
                var key = KeyOf(owner);
                if (string.IsNullOrEmpty(key) || !_stock.TryGetValue(key, out d) || d == null) return list;
                foreach (var kv in d) if (kv.Value > 0) list.Add(kv);
                list.Sort(delegate (KeyValuePair<string, int> x, KeyValuePair<string, int> y)
                { return string.CompareOrdinal(x.Key, y.Key); });
            }
            catch { }
            return list;
        }

        // ==================== 订单 API (27.3) ====================
        internal static string Order(object owner, string equipId, int n, int priority)
        {
            try
            {
                string key = KeyOf(owner);
                if (string.IsNullOrEmpty(key)) return "军械库: 无法识别订单所有者";
                if (string.IsNullOrEmpty(equipId)) return "军械库: 型号为空";
                if (n <= 0) return "军械库: 数量必须为正";
                if (priority < 0) priority = 0;
                if (priority > 2) priority = 2;

                OrderRec found = null;
                for (int i = 0; i < _orders.Count; i++)
                {
                    var o = _orders[i];
                    if (o != null && o.Owner == key && o.EquipId == equipId) { found = o; break; }
                }
                if (found != null)
                {
                    found.Remaining += n;
                    if (priority < found.Priority) found.Priority = priority;   // 数值小 = 更高优先
                }
                else
                {
                    if (_orders.Count >= 256) return "军械库: 订单已满";
                    _orders.Add(new OrderRec { Owner = key, EquipId = equipId, Remaining = n, Priority = priority });
                }
                string msg = "订单: " + OwnerName(key) + " × " + n + " " + equipId
                    + " (优先级 " + priority + ")";
                PushLog(msg);
                DLog.Force("军械库: " + msg);
                return msg;
            }
            catch { return "军械库: 下单失败"; }
        }

        internal static string Order(object owner, string equipId, int n)
        {
            return Order(owner, equipId, n, 1);
        }

        internal static string Cancel(object owner, string equipId)
        {
            try
            {
                string key = KeyOf(owner);
                for (int i = _orders.Count - 1; i >= 0; i--)
                {
                    var o = _orders[i];
                    if (o != null && o.Owner == key && o.EquipId == equipId)
                    {
                        _orders.RemoveAt(i);
                        return "已取消订单: " + equipId;
                    }
                }
            }
            catch { }
            return "没有该订单";
        }

        // ==================== 每日: 工厂推进(27.3) ====================
        internal static void Daily()
        {
            try { Daily((int)CampaignTime.Now.ToDays); }
            catch { Daily(0); }
        }

        internal static void Daily(int day)
        {
            try
            {
                if (day == _lastDay) return;
                _lastDay = day;
                if (_orders.Count == 0) return;

                // 按所有者聚合, 每国独立产能
                var byOwner = new Dictionary<string, List<int>>(StringComparer.Ordinal);
                for (int i = 0; i < _orders.Count; i++)
                {
                    var o = _orders[i];
                    if (o == null || o.Remaining <= 0) continue;
                    List<int> idx;
                    if (!byOwner.TryGetValue(o.Owner, out idx))
                    {
                        idx = new List<int>();
                        byOwner[o.Owner] = idx;
                    }
                    idx.Add(i);
                }

                foreach (var kv in byOwner)
                {
                    var kingdom = KingdomOfKey(kv.Key);
                    if (kingdom == null) continue;
                    int points = FactoryPointsOf(kingdom);
                    if (points <= 0)
                    {
                        PushLog("无军用工厂: " + OwnerName(kv.Key) + " 订单停滞");
                        continue;
                    }
                    // 优先级升序(0 最高), 同优先按下单顺序
                    kv.Value.Sort(delegate (int x, int y)
                    {
                        int c = _orders[x].Priority.CompareTo(_orders[y].Priority);
                        return c != 0 ? c : x.CompareTo(y);
                    });
                    for (int i = 0; i < kv.Value.Count && points > 0; i++)
                    {
                        var o = _orders[kv.Value[i]];
                        while (o.Remaining > 0 && points > 0)
                        {
                            string why;
                            if (!ProduceOne(kv.Key, kingdom, o.EquipId, out why))
                            {
                                if (!string.IsNullOrEmpty(why)) PushLog("订单停滞(" + OwnerName(kv.Key) + "): " + why);
                                break;
                            }
                            points--;
                            o.Remaining--;
                            o.Done++;
                            AddKey(kv.Key, o.EquipId, 1);
                        }
                    }
                }

                // 清理已完成订单
                for (int i = _orders.Count - 1; i >= 0; i--)
                    if (_orders[i] == null || _orders[i].Remaining <= 0) _orders.RemoveAt(i);
            }
            catch (Exception ex) { DLog.Force("军械库日结异常: " + ex.Message); }
        }

        // 工厂日产点 = Σ min(3, 建筑数) 档位产出(10/20/35)
        internal static int FactoryPointsOf(Kingdom k)
        {
            try
            {
                if (k == null) return 0;
                int pts = 0;
                foreach (var s in k.Settlements)
                {
                    if (s == null || string.IsNullOrEmpty(s.StringId)) continue;
                    var sb = EconomyWorld.Find(s.StringId);
                    if (sb == null || sb.Groups == null) continue;
                    for (int i = 0; i < sb.Groups.Count; i++)
                    {
                        var g = sb.Groups[i];
                        if (g == null || g.Count <= 0 || !IsFactory(g.DefId)) continue;
                        int lvl = g.Count;
                        if (lvl > MaxFactoryLevel) lvl = MaxFactoryLevel;
                        pts += FactoryOutput[lvl - 1];
                    }
                }
                return pts;
            }
            catch { return 0; }
        }

        private static bool IsFactory(string defId)
        {
            if (string.IsNullOrEmpty(defId)) return false;
            return defId == "military_factory" || defId == "arms_industry";
        }

        // 生产 1 件: 先验材料 -> 付款 -> 扣材料
        private static bool ProduceOne(string ownerKey, Kingdom kingdom, string equipId, out string why)
        {
            why = "";
            try
            {
                int gold = GoldCostOf(equipId);
                List<string> goods; List<int> amounts;
                MaterialsOf(equipId, out goods, out amounts);

                if (!WarEconomy.HasMaterials(kingdom, goods, amounts)) { why = "缺材料"; return false; }
                if (!PayGold(ownerKey, kingdom, gold)) { why = "缺资金"; return false; }
                if (!WarEconomy.TryConsumeMaterials(kingdom, goods, amounts))
                {
                    // 材料在验后被别处扣走(理论上单线程不会发生): 退款
                    RefundGold(ownerKey, kingdom, gold);
                    why = "材料扣减失败";
                    return false;
                }
                return true;
            }
            catch (Exception ex) { why = ex.Message; return false; }
        }

        private static bool PayGold(string ownerKey, Kingdom kingdom, int gold)
        {
            if (gold <= 0) return true;
            if (ownerKey.StartsWith("L:", StringComparison.Ordinal))
            {
                var c = FindClan(ownerKey.Substring(2));
                var hero = c != null ? c.Leader : null;
                if (hero == null || hero.Gold < gold) return false;
                hero.ChangeHeroGold(-gold);
                return true;
            }
            return WarEconomy.TryPayKingdom(kingdom, gold);
        }

        private static void RefundGold(string ownerKey, Kingdom kingdom, int gold)
        {
            try
            {
                if (gold <= 0) return;
                if (ownerKey.StartsWith("L:", StringComparison.Ordinal))
                {
                    var c = FindClan(ownerKey.Substring(2));
                    if (c != null && c.Leader != null) c.Leader.ChangeHeroGold(gold);
                    return;
                }
                WarEconomy.RefundKingdom(kingdom, gold);
            }
            catch { }
        }

        private static void PushLog(string s)
        {
            try
            {
                if (string.IsNullOrEmpty(s)) return;
                _log.Insert(0, s);
                while (_log.Count > MaxLog) _log.RemoveAt(_log.Count - 1);
            }
            catch { }
        }

        // ==================== 装备目录适配(另一智能体的 Military\Equipment.cs) ====================
        // 说明: 本类不实现装备目录; 目录未就绪时全部走兜底估算并标 TODO。
        private static Type _catalog;
        private static bool _catalogProbed;

        private static Type Catalog()
        {
            if (_catalogProbed) return _catalog;
            _catalogProbed = true;
            try
            {
                var asm = typeof(Armory).Assembly;
                string[] names =
                {
                    "FeudalInternalAffairs.Equipment", "FeudalInternalAffairs.EquipmentCatalog",
                    "FeudalInternalAffairs.Military.Equipment", "FeudalInternalAffairs.EquipCatalog"
                };
                for (int i = 0; i < names.Length && _catalog == null; i++)
                    _catalog = asm.GetType(names[i], false);
                if (_catalog == null)
                {
                    Type[] types;
                    try { types = asm.GetTypes(); } catch (ReflectionTypeLoadException ex) { types = ex.Types; }
                    if (types != null)
                    {
                        for (int i = 0; i < types.Length; i++)
                        {
                            var t = types[i];
                            if (t == null || !t.Name.Contains("Equipment")) continue;
                            if (t.GetMethod("DefOf", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static) != null
                                || t.GetMethod("Of", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static) != null)
                            { _catalog = t; break; }
                        }
                    }
                }
            }
            catch { }
            return _catalog;
        }

        private static object DefOf(string equipId)
        {
            try
            {
                var t = Catalog();
                if (t == null || string.IsNullOrEmpty(equipId)) return null;
                string[] names = { "DefOf", "Get", "Find", "FindDef", "Of" };
                for (int i = 0; i < names.Length; i++)
                {
                    var m = t.GetMethod(names[i], BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                        null, new[] { typeof(string) }, null);
                    if (m == null || m.ReturnType == typeof(void)) continue;
                    object v = null;
                    try { v = m.Invoke(null, new object[] { equipId }); } catch { continue; }
                    if (v != null) return v;
                }
            }
            catch { }
            return null;
        }

        private static bool TryNum(object obj, string[] names, out float v)
        {
            v = 0f;
            try
            {
                if (obj == null) return false;
                var t = obj.GetType();
                for (int i = 0; i < names.Length; i++)
                {
                    var p = t.GetProperty(names[i], BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (p != null && p.CanRead)
                    {
                        object raw = p.GetValue(obj, null);
                        if (raw != null) { v = Convert.ToSingle(raw, CultureInfo.InvariantCulture); return true; }
                    }
                    var f = t.GetField(names[i], BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (f != null)
                    {
                        object raw = f.GetValue(obj);
                        if (raw != null) { v = Convert.ToSingle(raw, CultureInfo.InvariantCulture); return true; }
                    }
                }
            }
            catch { }
            return false;
        }

        private static bool TryInt(object obj, string[] names, out int v)
        {
            v = 0;
            try
            {
                if (obj == null) return false;
                var t = obj.GetType();
                for (int i = 0; i < names.Length; i++)
                {
                    var p = t.GetProperty(names[i], BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (p != null && p.CanRead)
                    {
                        object raw = p.GetValue(obj, null);
                        if (raw != null) { v = Convert.ToInt32(raw, CultureInfo.InvariantCulture); return true; }
                    }
                    var f = t.GetField(names[i], BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (f != null)
                    {
                        object raw = f.GetValue(obj);
                        if (raw != null) { v = Convert.ToInt32(raw, CultureInfo.InvariantCulture); return true; }
                    }
                }
            }
            catch { }
            return false;
        }

        private static bool TryBool(object obj, string[] names, out bool v)
        {
            v = false;
            try
            {
                if (obj == null) return false;
                var t = obj.GetType();
                for (int i = 0; i < names.Length; i++)
                {
                    var p = t.GetProperty(names[i], BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (p != null && p.CanRead && p.PropertyType == typeof(bool))
                    { v = (bool)p.GetValue(obj, null); return true; }
                    var f = t.GetField(names[i], BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (f != null && f.FieldType == typeof(bool)) { v = (bool)f.GetValue(obj); return true; }
                }
            }
            catch { }
            return false;
        }

        private static string TryStr(object obj, string[] names)
        {
            try
            {
                if (obj == null) return null;
                var t = obj.GetType();
                for (int i = 0; i < names.Length; i++)
                {
                    var p = t.GetProperty(names[i], BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (p != null && p.CanRead && p.PropertyType == typeof(string))
                    { object raw = p.GetValue(obj, null); if (raw != null) return (string)raw; }
                    var f = t.GetField(names[i], BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (f != null && f.FieldType == typeof(string))
                    { object raw = f.GetValue(obj); if (raw != null) return (string)raw; }
                }
            }
            catch { }
            return null;
        }

        internal static int TierOf(string equipId)
        {
            try
            {
                var def = DefOf(equipId);
                float v;
                if (TryNum(def, new[] { "Tier", "Level", "Rank" }, out v))
                {
                    int t = (int)Math.Round(v);
                    return t < 1 ? 1 : (t > 6 ? 6 : t);
                }
            }
            catch { }
            return 1;
        }

        internal static int GoldCostOf(string equipId)
        {
            try
            {
                var def = DefOf(equipId);
                float v;
                if (TryNum(def, new[] { "Price", "Cost", "GoldCost", "Gold", "Value" }, out v) && v > 0f)
                    return (int)Math.Ceiling(v);
            }
            catch { }
            // TODO(装备目录): Military\Equipment.cs 就绪后应直接给单价; 此为占位估算
            return 40 * TierOf(equipId);
        }

        // 目录可读时取其材料表; 否则兜底: 铁 ×层级(+ 木 ×1)
        internal static void MaterialsOf(string equipId, out List<string> goods, out List<int> amounts)
        {
            goods = new List<string>();
            amounts = new List<int>();
            int tier = TierOf(equipId);
            try
            {
                var def = DefOf(equipId);
                string spec = TryStr(def, new[] { "Materials", "MaterialSpec", "Recipe", "Costs", "Inputs" });
                if (!string.IsNullOrEmpty(spec)) ParseMaterialSpec(spec, goods, amounts);
            }
            catch { }
            if (goods.Count == 0)
            {
                // TODO(装备目录): 目录就绪后删除本兜底
                goods.Add(FeudalGoods.Iron);
                amounts.Add(Math.Max(1, tier));
                if (tier >= 3) { goods.Add(FeudalGoods.Hardwood); amounts.Add(1); }
            }
        }

        private static void ParseMaterialSpec(string spec, List<string> goods, List<int> amounts)
        {
            try
            {
                var tokens = spec.Split(new[] { ' ', ',', ';', '、', '+', '/', '，' }, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < tokens.Length; i++)
                {
                    string tok = tokens[i];
                    if (string.IsNullOrEmpty(tok)) continue;
                    int amt = 0;
                    var sbName = new StringBuilder();
                    for (int c = 0; c < tok.Length; c++)
                    {
                        char ch = tok[c];
                        if (ch >= '0' && ch <= '9') { amt = amt * 10 + (ch - '0'); continue; }
                        sbName.Append(ch);
                    }
                    if (amt <= 0) amt = 1;
                    string gid = GoodIdOfName(sbName.ToString());
                    if (string.IsNullOrEmpty(gid)) continue;
                    int at = goods.IndexOf(gid);
                    if (at >= 0) amounts[at] += amt;
                    else { goods.Add(gid); amounts.Add(amt); }
                }
            }
            catch { }
        }

        private static string GoodIdOfName(string name)
        {
            try
            {
                if (string.IsNullOrEmpty(name)) return null;
                string n = name.Trim().ToLowerInvariant();
                if (n.Contains("火药") || n.Contains("explosive")) return FeudalGoods.Explosives;
                if (n.Contains("钢") || n.Contains("steel")) return FeudalGoods.Steel;
                if (n.Contains("铁") || n.Contains("iron")) return FeudalGoods.Iron;
                if (n.Contains("木") || n.Contains("wood") || n.Contains("timber")) return FeudalGoods.Hardwood;
                if (n.Contains("皮") || n.Contains("leather")) return FeudalGoods.Leather;
                if (n.Contains("布") || n.Contains("linen") || n.Contains("cloth")) return FeudalGoods.Linen;
                if (n.Contains("煤") || n.Contains("charcoal") || n.Contains("coal")) return FeudalGoods.Charcoal;
                if (n.Contains("粮") || n.Contains("grain")) return FeudalGoods.Grain;
                if (n.Contains("马") || n.Contains("horse")) return FeudalGoods.Horse;
            }
            catch { }
            return null;
        }

        // 缴获/建军按 (层级, 火器与否) 挑一个目录型号; 目录未就绪返回 null
        internal static string PickModelFor(int tier, bool firearm)
        {
            try
            {
                var t = Catalog();
                if (t == null) return null;
                string[] listNames = { "All", "List", "Defs", "Models", "Catalog", "DefsList" };
                IEnumerable seq = null;
                for (int i = 0; i < listNames.Length && seq == null; i++)
                {
                    var p = t.GetProperty(listNames[i], BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    if (p != null) { seq = p.GetValue(null, null) as IEnumerable; continue; }
                    var f = t.GetField(listNames[i], BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    if (f != null) seq = f.GetValue(null) as IEnumerable;
                }
                if (seq == null) return null;
                int guard = 0;
                string lockedPick = null;      // v4.241: 全锁时才退回(缴获不该凭空出未来枪械, 但也不能空手)
                foreach (var def in seq)
                {
                    if (guard++ > 512) break;
                    if (def == null) continue;
                    float tv;
                    if (!TryNum(def, new[] { "Tier", "Level", "Rank" }, out tv)) continue;
                    if ((int)Math.Round(tv) != tier) continue;
                    bool isF;
                    if (!TryBool(def, new[] { "IsFirearm", "Firearm", "IsGun" }, out isF)) continue;
                    if (isF != firearm) continue;
                    int cat;
                    if (TryInt(def, new[] { "Cat", "Category", "Kind" }, out cat))
                    {
                        // EquipCat: 0=Melee 1=Ranged 2=Artillery 3=Armor
                        bool ok = firearm ? (cat == 1 || cat == 2) : (cat == 0 || cat == 3);
                        if (!ok) continue;
                    }
                    string id = TryStr(def, new[] { "Id", "StringId", "Name" });
                    if (string.IsNullOrEmpty(id)) continue;
                    bool unlocked = true;
                    try { unlocked = Equipment.IsUnlocked(id); } catch { }
                    if (unlocked) return id;
                    if (lockedPick == null) lockedPick = id;
                }
                return lockedPick;
            }
            catch { }
            return null;
        }

        // ==================== 存档 (27.17) ====================
        internal static string SaveArmory()
        {
            try
            {
                var sb = new StringBuilder();
                sb.Append("v1");
                foreach (var kv in _stock)
                {
                    if (kv.Value == null) continue;
                    foreach (var e in kv.Value)
                    {
                        if (e.Value <= 0 || string.IsNullOrEmpty(e.Key)) continue;
                        sb.Append(';').Append(kv.Key).Append('|').Append(e.Key).Append('|').Append(e.Value);
                    }
                }
                return sb.ToString();
            }
            catch { return "v1"; }
        }

        internal static void LoadArmory(string data)
        {
            try
            {
                _stock.Clear();
                if (string.IsNullOrEmpty(data)) return;
                var parts = data.Split(';');
                for (int i = 1; i < parts.Length; i++)
                {
                    var f = parts[i].Split('|');
                    if (f.Length < 3) continue;
                    int n;
                    if (!int.TryParse(f[2], out n) || n <= 0) continue;
                    AddKey(f[0], f[1], n);
                }
            }
            catch { }
        }

        internal static string SaveOrders()
        {
            try
            {
                var sb = new StringBuilder();
                sb.Append("v1");
                for (int i = 0; i < _orders.Count; i++)
                {
                    var o = _orders[i];
                    if (o == null || o.Remaining <= 0) continue;
                    sb.Append(';').Append(o.Owner).Append('|').Append(o.EquipId).Append('|')
                      .Append(o.Remaining).Append('|').Append(o.Priority).Append('|').Append(o.Done);
                }
                return sb.ToString();
            }
            catch { return "v1"; }
        }

        internal static void LoadOrders(string data)
        {
            try
            {
                _orders.Clear();
                if (string.IsNullOrEmpty(data)) return;
                var parts = data.Split(';');
                for (int i = 1; i < parts.Length; i++)
                {
                    var f = parts[i].Split('|');
                    if (f.Length < 4) continue;
                    if (string.IsNullOrEmpty(f[0]) || string.IsNullOrEmpty(f[1])) continue;
                    int rem, pri, done = 0;
                    if (!int.TryParse(f[2], out rem) || rem <= 0) continue;
                    if (!int.TryParse(f[3], out pri)) continue;
                    if (f.Length >= 5) int.TryParse(f[4], out done);
                    _orders.Add(new OrderRec { Owner = f[0], EquipId = f[1], Remaining = rem, Priority = pri, Done = done });
                }
            }
            catch { }
        }

        // 合并 payload: Save()/Load() 供 behavior 接线; WarEconomy 内嵌时用 SaveArmory/SaveOrders
        internal static string Save()
        {
            return SaveArmory() + (char)3 + SaveOrders();
        }

        internal static void Load(string data)
        {
            try
            {
                string armory = data;
                string orders = "";
                if (!string.IsNullOrEmpty(data))
                {
                    int at = data.IndexOf((char)3);
                    if (at >= 0) { armory = data.Substring(0, at); orders = data.Substring(at + 1); }
                }
                LoadArmory(armory);
                LoadOrders(orders);
            }
            catch { }
        }

        internal static void Reset()
        {
            try
            {
                _stock.Clear();
                _orders.Clear();
                _log.Clear();
                _lastDay = -1;
            }
            catch { }
        }
    }
}
