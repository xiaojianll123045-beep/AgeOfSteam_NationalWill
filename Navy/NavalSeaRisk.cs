using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Naval;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;

namespace FeudalInternalAffairs
{
    // ==================== v4.273(A-1 海上损耗 + B-3 海上风暴) ====================
    //
    // 【A-1 为什么必须做】DLC 的 `NavalDLCCampaignShipDamageModel.GetHourlyShipDamage`(DLC 反编译
    //   参考行 99280-99291) 在 `party.CurrentSettlement == null && party.MapEvent == null &&
    //   地形 == 19(公海)` 时**每个游戏小时扣船体耐久**。我们的海运最长 30 天
    //   (NavalTransport.EtaDays 的上限), 全程公海 —— 也就是说船会一路掉耐久, 原来我们完全不看这件事:
    //   可能出现"出发时运力刚好够, 半路船沉了/耐久见底, 军团被困在海上"的情况。
    //
    // 【接法: 只读原版模型, 不自己写损耗公式】
    //   基础游戏 `TaleWorlds.CampaignSystem.ComponentInterfaces.CampaignShipDamageModel` 是**public**,
    //   从 `Campaign.Current.Models.CampaignShipDamageModel` 取实例, 有两个我们要用的只读方法:
    //     · `float GetEstimatedSafeSailDuration(MobileParty)` —— 这支船队还能安全航行几天
    //     · `int   GetHourlyShipDamage(MobileParty, Ship)`     —— 该船此刻每游戏小时掉多少耐久
    //   我们**只调用、不覆盖、不另写公式**, 因此永远不会和 DLC 的模型打架(它算它的, 我们只是提前看一眼)。
    //
    // 【B-3 风暴同理】DLC 的 `NavalDLC.ComponentInterfaces.MapStormModel`(NavalDLCStormModel:
    //   MinimumWeatherStrengthInsideStorm=1 / MaximumNumberOfStorms=6) 与其 `NavalDLC.Map.StormManager`
    //   (public `MBReadOnlyList<Storm> SpawnedStorms`) 都在 NavalDLC.dll 里, 所以**全部走反射**
    //   (软依赖铁律: 一个 NavalDLC.* 编译期引用都不能有)。
    //   Storm 的位置/半径通过公开实例属性读: Position(Vec2) / GetEffectRadiusOfStorm(model, storm)。
    //   我们只做"航线是否穿过风暴"的几何判断 + 文案提示, **不自己造风暴、不改风暴、不叠加额外扣血**
    //   (DLC 自己已经在扣)。
    internal static class NavalSeaRisk
    {
        // ==================== 原版船损模型 ====================

        private static TaleWorlds.CampaignSystem.ComponentInterfaces.CampaignShipDamageModel Model
        {
            get
            {
                try { return Campaign.Current != null ? Campaign.Current.Models.CampaignShipDamageModel : null; }
                catch { return null; }
            }
        }

        // 这支船队"还能安全航行几天"(原版口径; 拿不到模型时返回 -1 = 未知)
        internal static float SafeSailDays(MobileParty p)
        {
            try
            {
                if (p == null) return -1f;
                var m = Model;
                if (m == null) return -1f;
                float v = m.GetEstimatedSafeSailDuration(p);
                if (float.IsNaN(v) || float.IsInfinity(v)) return -1f;
                return v;
            }
            catch { return -1f; }
        }

        // 某条船此刻每游戏小时掉多少耐久(原版口径; 0 = 不掉/拿不到)
        internal static int HourlyDamage(Ship s)
        {
            try
            {
                if (s == null) return 0;
                var m = Model;
                if (m == null) return 0;
                // PartyBase -> MobileParty: 走 PartyBase.MobileParty 属性(非移动部队时返回 null)
                var owner = s.Owner != null ? s.Owner.MobileParty : null;
                if (owner == null) return 0;
                return m.GetHourlyShipDamage(owner, s);
            }
            catch { return 0; }
        }

        // 军团船队里"最扛不住的那条船"还能撑几天(合并估算: 单船安全天数取所有船的**最小值**)
        //   返回 -1 = 未知(没装 DLC / 模型取不到 / 没船)
        internal static float ConvoySafeDays(MobileParty p)
        {
            try
            {
                if (p == null) return -1f;
                float clean = SafeSailDays(p);
                // 原版给的是"整队"的安全天数, 直接用它; 取不到才算单船
                if (clean > 0f) return clean;
                var ships = NavalBridge.ShipsOf(p);
                if (ships.Count == 0) return -1f;
                float worst = -1f;
                for (int i = 0; i < ships.Count; i++)
                {
                    var s = ships[i];
                    if (s == null) continue;
                    int dmg = HourlyDamage(s);
                    if (dmg <= 0) continue;                 // 不掉耐久 -> 不构成约束
                    float hp = NavalBridge.HitPoints(s);
                    float days = hp / dmg / 24f;            // 小时 -> 天
                    if (worst < 0f || days < worst) worst = days;
                }
                return worst;
            }
            catch { return -1f; }
        }

        // 出发前的"撑不撑得住"校验: 返回 "" = 可以走; 否则是中文拒绝原因。
        //   撑得住也返回 "" —— 调用方需要 N 时用 ConvoySafeDays 单独取(供面板显示)。
        internal static string CheckSafe(MobileParty p, int etaDays, out float safeDays)
        {
            safeDays = -1f;
            try
            {
                // v4.273: 只在装了 DLC 且模型给得出值时校验 —— 没数据时**绝不拦人**(宁可放过, 不可误伤)
                safeDays = ConvoySafeDays(p);
                if (safeDays < 0f) return "";
                if (etaDays <= 0) return "";
                // 留 1 天余量: 到港时耐久见底也算"撑住了", 但 0 余量太险(海况/绕路都会多花时间)
                if (safeDays + 1f >= etaDays) return "";
                return "主力舰仅可安全航行 " + safeDays.ToString("0.#") + " 天, 本航程约 " + etaDays
                     + " 天 —— 请先修船或换更近的目的港";
            }
            catch { safeDays = -1f; return ""; }
        }

        // 出航后每小时检查: 划出去的那批船里有没有"耐久见底"的(返回 true = 该提前靠港)
        //   ratio 阈值 0.25: 耐久低于上限 1/4 就收手, 留足余量让它能安全驶入港口
        internal static bool ShippedShipsFailing(List<int> shipKeys, MobileParty p, out string why)
        {
            why = "";
            try
            {
                if (p == null || shipKeys == null || shipKeys.Count == 0) return false;
                var ships = NavalBridge.ShipsOf(p);
                for (int i = 0; i < ships.Count; i++)
                {
                    var s = ships[i];
                    if (s == null) continue;
                    int key = 0;
                    try { key = s.RandomValue; } catch { }
                    if (key == 0 || !shipKeys.Contains(key)) continue;   // 只看这次海运划出去的船
                    float max = NavalBridge.MaxHitPoints(s);
                    if (max <= 0f) continue;
                    float hp = NavalBridge.HitPoints(s);
                    if (hp <= 0f)
                    {
                        why = NavalBridge.HullName(s) + " 已在航行中损毁";
                        return true;
                    }
                    if (hp / max < 0.25f)
                    {
                        why = NavalBridge.HullName(s) + " 船体仅剩 " + (int)Math.Round(hp / max * 100f)
                            + "%(海上损耗), 再走下去会沉";
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }

        // ==================== B-3: 风暴(全反射, 零 DLC 编译期引用) ====================

        private static bool _stormProbed;
        private static PropertyInfo _pSpawnedStorms;     // StormManager.SpawnedStorms
        private static PropertyInfo _pStormPosition;     // Storm.CurrentPosition (Vec2)
        private static PropertyInfo _pStormRadius;       // Storm.EffectRadius (float)
        private static PropertyInfo _pStormActive;       // Storm.IsActive (bool)
        // ==================== v4.279(第 4 项): 游戏在用的 StormManager 的**公开入口** ====================
        //   反编译实证(NavalDLC.dll, 本机):
        //     `public class NavalDLCManager : GameHandler`
        //       · `public static NavalDLCManager Instance;`                   <- public **静态字段**(不是属性)
        //       · `public StormManager StormManager { get; internal set; }`   <- public 实例属性
        //       · 初始化时机: OnGameStart -> `Campaign.Current.AddCustomManager<StormManager>()`;
        //                     OnGameLoad  -> `Campaign.Current.GetCustomManager<StormManager>()` + `OnAfterLoad()`
        //     DLC 自己内部 20+ 处就是这么用的(例如 `NavalDLCManager.Instance.StormManager.SpawnedStorms`)。
        private static FieldInfo _fDlcInstance;          // NavalDLCManager.Instance (静态字段)
        private static PropertyInfo _pDlcInstance;       // 兼容: 若将来改成静态属性
        private static PropertyInfo _pDlcStormManager;   // NavalDLCManager.StormManager (实例属性)

        private static void ProbeStorm()
        {
            if (_stormProbed) return;
            _stormProbed = true;
            try
            {
                var tMgr = AccessTools.TypeByName("NavalDLC.Map.StormManager");
                if (tMgr != null)
                {
                    _pSpawnedStorms = tMgr.GetProperty("SpawnedStorms", BindingFlags.Public | BindingFlags.Instance);
                }
                var tHost = AccessTools.TypeByName("NavalDLC.NavalDLCManager");
                if (tHost != null)
                {
                    _fDlcInstance = tHost.GetField("Instance", BindingFlags.Public | BindingFlags.Static);
                    if (_fDlcInstance == null)
                        _pDlcInstance = tHost.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                    _pDlcStormManager = tHost.GetProperty("StormManager", BindingFlags.Public | BindingFlags.Instance);
                }
                var tStorm = AccessTools.TypeByName("NavalDLC.Map.Storm");
                if (tStorm != null)
                {
                    // 注意: Storm.EffectRadius 是**计算属性**(内部走 NavalDLCManager.Instance.GameModels
                    //   .MapStormModel.GetEffectRadiusOfStorm(this)), 所以它同样依赖 DLC 已初始化 ——
                    //   拿不到时下面一律按 0/兜底半径处理, 不抛。
                    _pStormPosition = tStorm.GetProperty("CurrentPosition", BindingFlags.Public | BindingFlags.Instance);
                    _pStormRadius = tStorm.GetProperty("EffectRadius", BindingFlags.Public | BindingFlags.Instance);
                    _pStormActive = tStorm.GetProperty("IsActive", BindingFlags.Public | BindingFlags.Instance);
                }
                DLog.Force("海军风暴: DLC 接口探测 " + (StormAvailable
                    ? "成功(经 NavalDLCManager.Instance.StormManager 读在航风暴位置/半径)"
                    : "未命中 —— 取不到 NavalDLCManager.Instance.StormManager, 航线风暴提示停用(不影响其它系统)"));
            }
            catch (Exception ex) { DLog.Force("海军风暴: 探测异常(按无风暴处理) " + ex.Message); }
        }

        // ==================== v4.279(第 4 项): 改走 DLC 的公开入口(原实现 100% 静默失效) ====================
        //   【原实现的错误断言】原注释断言"StormManager 在 DLC 里不是挂 Campaign/CampaignBehaviorManager
        //   上的单例, 没有公开入口能拿到游戏正在用的那一个", 于是**自建一个实例**再调它的 OnAfterLoad(),
        //   声称"这样就能拿到存档里真实存在的那批风暴"。
        //   【反编译实证: 这个断言是错的, 而且后果是 100% 静默失效】
        //     · 公开入口存在且就是 DLC 自己用的那个: `NavalDLCManager.Instance.StormManager`
        //       (字段/属性签名见 ProbeStorm 注释; 它由 OnGameStart/OnGameLoad 里的
        //        Campaign.AddCustomManager<StormManager>/GetCustomManager<StormManager> 建立)。
        //     · `StormManager.OnAfterLoad()` **不是"从存档读风暴"** —— 它只对**实例里已有**的
        //       `_spawnedStorms` 做事(重建每场风暴的寻路/可见性状态)。风暴列表本身是 Saveable 字段,
        //       由存档系统回填到 `GetCustomManager<StormManager>()` 拿到的**那个实例**上。
        //     · 所以自建实例的 `SpawnedStorms` **恒为空** => RouteStorm 永远返回"没风暴" =>
        //       StormDelayDays 恒 0, 面板"航线上有风暴"永不出现 —— 而且**不抛异常**、日志还写着"探测成功"。
        //   【修法】改为从 NavalDLCManager.Instance.StormManager 取(仍然**纯反射**, 一个 NavalDLC.* 编译期
        //     引用都没有, 软依赖铁律不破)。取不到时(未装 DLC / 尚未初始化 / 正在读档)静默降级为"没有风暴",
        //     并把失效原因如实写进日志。**不再自建实例假装成功**。
        private static object StormManager()
        {
            try
            {
                ProbeStorm();
                if (_fDlcInstance == null && _pDlcInstance == null) return null;
                if (_pDlcStormManager == null) return null;
                object host = null;
                try
                {
                    host = _fDlcInstance != null ? _fDlcInstance.GetValue(null) : _pDlcInstance.GetValue(null, null);
                }
                catch { host = null; }
                if (host == null) return null;      // DLC GameHandler 还没建立(标题画面 / 读档进行中)
                return _pDlcStormManager.GetValue(host, null);
            }
            catch { return null; }
        }

        // 新战役/换地图时调用(保留原签名, 兼容既有 3 个调用点: NavalBehavior.OnGameLoad /
        //   NavalFleet.Reset / **OnShipRepairedEvent**)。
        //   v4.279(第 4 项): 本函数现在**故意是空操作** ——
        //     ① 不再缓存 StormManager 实例: 每次查询都从 `NavalDLCManager.Instance.StormManager`
        //        现取(见 StormManager 的长注释)。所以换战役时没有任何缓存需要清 —— DLC 重建
        //        GameHandler 之后 Instance 自己就指向新对象了(旧实现缓存自建实例, 才需要在这里清)。
        //     ② 刻意**不重置 `_stormProbed`**: 反射句柄(类型/成员签名)与战役无关, 重置只会让下一次
        //        查询重新探测并**再打一条探测日志** —— 而 `OnShipRepairedEvent`(每次修船)也会调本函数,
        //        那会把日志刷爆。保留原签名只为不动别人的调用点。
        internal static void Reset()
        {
        }

        internal static bool StormAvailable
        {
            get
            {
                ProbeStorm();
                // v4.279: 判据里也要求"公开入口的反射句柄齐备", 否则 StormAvailable 会在拿不到
                //   游戏实例的情况下报 true, 又回到"看起来能用、实际恒空"的老问题。
                return _pSpawnedStorms != null && _pStormPosition != null
                    && _pDlcStormManager != null && (_fDlcInstance != null || _pDlcInstance != null);
            }
        }

        internal class StormHit
        {
            internal bool Any;
            internal int Count;            // 命中航线的风暴数
            internal float MinDist = 0f;   // 离航线最近的圆心距离(诊断用)
        }

        // 航线(两点直线)是否穿过任一在航风暴的影响半径。
        //   注意: 这是**保守的几何近似** —— 用直线段近似海上航线, 用于"提示玩家航线上有风暴",
        //   不做任何数值惩罚(DLC 自己会在风暴区内按小时扣耐久, 我们只负责把这件事提前告诉玩家)。
        internal static StormHit RouteStorm(Vec2 from, Vec2 to)
        {
            var hit = new StormHit();
            try
            {
                ProbeStorm();
                if (!StormAvailable) return hit;
                var mgr = StormManager();
                if (mgr == null) return hit;
                var list = _pSpawnedStorms.GetValue(mgr, null) as System.Collections.IEnumerable;
                if (list == null) return hit;

                foreach (var st in list)
                {
                    if (st == null) continue;
                    // 只算"活着"的风暴(正在消散的旧风暴不再影响航线判断)
                    try { if (_pStormActive != null && !(bool)_pStormActive.GetValue(st, null)) continue; } catch { }
                    var posObj = _pStormPosition.GetValue(st, null);
                    if (!(posObj is Vec2)) continue;
                    var center = (Vec2)posObj;

                    float radius = 0f;
                    try { if (_pStormRadius != null) radius = Convert.ToSingle(_pStormRadius.GetValue(st, null)); } catch { radius = 0f; }
                    if (radius <= 0f) radius = 25f;   // 兜底半径(只在拿不到半径时用)

                    float d = PointSegmentDistance(center, from, to);
                    if (d <= radius)
                    {
                        hit.Any = true;
                        hit.Count++;
                        if (hit.MinDist <= 0f || d < hit.MinDist) hit.MinDist = d;
                    }
                }
            }
            catch { }
            return hit;
        }

        // 点到线段的距离 —— 直接用原版 `Vec2.DistanceToLineSegment`(自己写容易在退化线段上出错)
        private static float PointSegmentDistance(Vec2 p, Vec2 a, Vec2 b)
        {
            try
            {
                Vec2 closest;
                return p.DistanceToLineSegment(a, b, out closest);
            }
            catch { return 9999f; }
        }

        // 航线风暴造成的"预计延误天数"(只用于我们自己的 ETA 估算与面板文案)
        //   · 命中风暴 -> 按航程比例延误(短途 1 天起, 长途最多 +1/3 航程)
        //   · 季节系数: 冬季风暴更凶(12/1/2 月), 夏季(6/7/8 月)最轻
        internal static int StormDelayDays(Vec2 from, Vec2 to, int etaDays)
        {
            try
            {
                if (etaDays <= 0) return 0;
                var hit = RouteStorm(from, to);
                if (!hit.Any) return 0;
                float season = SeasonFactor();
                float d = etaDays * 0.15f * season * (1f + 0.10f * (hit.Count - 1));
                if (d < 1f) d = 1f;
                float cap = Math.Max(1f, etaDays / 3f);
                if (d > cap) d = cap;
                return (int)Math.Round(d);
            }
            catch { return 0; }
        }

        // 季节系数(用原版 CampaignTime.Seasons, 不需要额外数据)
        private static float SeasonFactor()
        {
            try
            {
                switch (CampaignTime.Now.GetSeasonOfYear)
                {
                    case CampaignTime.Seasons.Winter: return 1.5f;
                    case CampaignTime.Seasons.Autumn: return 1.2f;
                    case CampaignTime.Seasons.Spring: return 1.0f;
                    case CampaignTime.Seasons.Summer: return 0.8f;
                    default: return 1.0f;
                }
            }
            catch { return 1.0f; }
        }
    }
}
