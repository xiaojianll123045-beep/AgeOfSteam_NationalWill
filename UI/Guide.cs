using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;              // v4.289: Kingdom(海上篇章的适用性/舰队查询要用)
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace FeudalInternalAffairs
{
    // 挑战式引导(V3 式三层教程的落地):
    //   ① 反应式挑战链: 陆上 12 条 + 海上 6 条(v4.289), 前一条为后一条铺前提, 完成一条才解锁下一条
    //   ② 每条挑战自带 Tell me How(点哪里) / Tell me Why(为什么值得做), 玩家自己触发、可随时回看
    //   ③ 概念提示(ConceptTip): 首次碰到某个机制时就地讲一段, 不限于引导模式
    // 设计原则: 融入正常玩法(不是独立模式) · 一次只激活一条 · 可跳过可回看 · 加机制只需加一条挑战
    internal class GuideGoal
    {
        internal string Id = "";
        internal string Title = "";      // 挑战名
        internal string How = "";        // 怎么做(指到具体页面与按钮)
        internal string Why = "";        // 为什么值得做(后果与坑)
        internal string Hint = "";       // 一句话提示
        internal string Prereq = null;   // 前置挑战 id(可空)
        internal Func<bool> Check;       // 完成检测(必须廉价, 每次检测只跑当前这一条)

        internal GuideGoal() { }

        internal GuideGoal(string id, string title, string how, string why, string hint, string prereq, Func<bool> check)
        {
            Id = id; Title = title; How = how; Why = why; Hint = hint; Prereq = prereq; Check = check;
        }
    }

    // 挑战起点快照(用于"比开局多了多少"这类检测; 只在切换挑战时取一次)
    internal struct GuideSnap
    {
        internal int Gold;
        internal int Bld;
        internal int Garrison;
        internal int Legions;
        internal int Held;
        internal int Tech;
        internal int Laws;
        internal int TaxDay;
        internal int TaxPack;   // 四税档位打包(土×1000 + 人×100 + 什×10 + 市)
        internal string Report;
        internal int NavalBattle;   // v4.289: 海战记录条数(NavalFleet.Battles.Count) —— 海上篇章"打第一场海战"的基线
    }

    internal static class Guide
    {
        private const float TickInterval = 1.5f;   // 完成检测节流(秒)
        // v4.289: 海上篇章的挑战条数(与 Goals 里实际追加的条数必须一致 —— 改一处要同时改另一处)
        internal const int NavyGoalCount = 6;

        private static float _acc;
        private static List<GuideGoal> _goals;

        private static readonly List<string> _done = new List<string>();
        private static readonly HashSet<string> _doneSet = new HashSet<string>(StringComparer.Ordinal);
        private static readonly HashSet<string> _tipsSeen = new HashSet<string>(StringComparer.Ordinal);

        private static string _current = "";
        private static bool _enabled = false;   // v4.261: 默认关闭 —— 只有从主菜单「蒸汽时代教程」进入的新战役才开启(用户要求: 教程不强制, 正常开始游戏没有教程)
        // v4.261: 教程模式标记(仅由主菜单「蒸汽时代教程」入口置位, 不存档 —— 读档时引导状态从存档恢复, 不依赖它)
        internal static bool TutorialMode;
        private static bool _pickHinted;        // 教程局: "请选择你要接管的王国"只弹一次
        private static bool _started;           // 是否已弹过"引导开始"
        private static bool _legacy;            // 旧存档没有 FIA_Guide 键
        private static GuideSnap _base;
        private static bool _hasBase;           // 起点快照是否已取(没取过就不做"比开局多了多少"判断)
        private static bool _heldSeen;          // 曾经处于"归玩家指挥"(第 12 条检测用)

        // ==================== v4.289: 海上篇章(海军/海战教程)的适用性 ====================
        //   挑战链原来 12 条(经济 → 军事 → 政治 → 外交), 完全没有海军内容, 而 Navy\ 是本轮的重头戏。
        //   这里补 6 条海军挑战, **整条挂在 g_release 之后**(国家已经立起来之后的进阶内容)。
        //   ★ 适用性必须**全局判一次**: 内陆国家 / 没装海战 DLC 时, 这 6 条一条都不进 Goals
        //     (否则玩家会卡在一个永远做不完的挑战上 —— 见 RefreshCurrent 里的调用点)。
        private static bool _navyChecked;        // 适用性是否已判过(判过就不再重复判定: 打完沿海城也不中途解锁)
        private static bool _navyApplicable;     // 判定结果: true = 海军链进 Goals
        private static bool _navySkipLogged;     // 跳过是否已写日志 + 已弹提示(只做一次)

        // 弹窗被占用时挂起的提示(下一次 tick 补弹)
        private static string _pendKey, _pendTitle, _pendBody;

        internal static bool Enabled { get { return _enabled; } }
        internal static bool Started { get { return _started; } }
        internal static int DoneCount { get { return _done.Count; } }
        internal static int TotalCount { get { return Goals.Count; } }

        // ==================== 对外入口 ====================

        // 当前激活的挑战(没有剩余 = null)。已完成的不再返回。
        internal static GuideGoal Current()
        {
            try { return RefreshCurrent(); }
            catch { return null; }
        }

        internal static GuideGoal Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            var l = Goals;
            for (int i = 0; i < l.Count; i++) if (l[i].Id == id) return l[i];
            return null;
        }

        // 记账: 标记某条挑战完成(重复调用无害)
        internal static void Complete(string id)
        {
            try
            {
                if (string.IsNullOrEmpty(id)) return;
                if (!_doneSet.Add(id)) return;
                _done.Add(id);
                if (_current == id) _current = "";
                DLog.Force("引导: 挑战完成 " + id + " (" + _done.Count + "/" + Goals.Count + ")");
            }
            catch { }
        }

        // 心跳: 由 NationalWillBehavior.OnTick 调用(内部 1.5 秒节流)
        internal static void Tick(float dt)
        {
            try { TutorialWindow.Tick(); } catch { }   // v4.263b: 教程窗拖动(每帧, 与引导开关无关)
            try
            {
                _acc += dt;
                _acc2 += dt;

                // v4.263e: **选国块改用独立累加器 `_acc2`** —— 原来它和下面的挑战链共用 `_acc`,
                //   而选国块只要 `TutorialMode && !_pickHinted` 成立就每 1.5 秒把 `_acc` 清零,
                //   于是下面 `if (_acc < TickInterval) return;` **永远成立**, ShowStart/StepCurrent 一次都没跑过
                //   (日志实证: 连我加的"引导闸门"诊断都没有输出)。这是本次"引导怎么都不弹"的真凶。
                if (_acc2 >= TickInterval && TutorialMode && !_pickHinted)
                {
                    _acc2 = 0f;
                    if (!PanelInputGuard.AnyPopupActive() && NationPickMode.Active)
                    {
                        Tutorials.Page("tip_picknation");
                        if (TutorialWindow.IsShown)
                        {
                            _pickHinted = true;
                            DLog.Force("引导: 选国引导已弹出");
                        }
                        else DLog.Force("引导: 选国引导未能弹出(地图/浮窗未就绪), 稍后重试");
                    }
                    else if (!_pickProbeLogged)
                    {
                        // v4.263d: 选国引导的等待诊断(只记一次, 便于确诊是"弹窗占用"还是"不在选国模式")
                        _pickProbeLogged = true;
                        DLog.Force("引导: 选国引导等待中(弹窗占用=" + PanelInputGuard.AnyPopupActive()
                            + " 选国模式=" + NationPickMode.Active + ")");
                    }
                }

                // v4.263d: 原来这里是 `if (!NationalWillOrders.IsActive) return;` —— 日志实证: 教程局里
                //   连一条"弹出开始说明"都没有, 整条挑战链被这个闸门挡住了(选国未完成/沙盒下判定异常时
                //   IsActive 一直为假)。挑战链本身不依赖"接管完成", 是否跑引导只由 _enabled(教程局开关)决定,
                //   所以这里改成只记一次诊断, 不再据此 return。
                if (!NationalWillOrders.IsActive && !_inactiveLogged)
                {
                    _inactiveLogged = true;
                    DLog.Force("引导: 国家意志尚未激活(选国未完成?) —— 引导仍按教程局开关继续跑");
                }
                if (!_enabled) return;

                if (_acc < TickInterval) return;
                _acc = 0f;

                // v4.263d: 闸门诊断(只记一次) —— 把每个可能挡住引导链的条件一次性写进日志,
                //   以后再出问题不用再猜是哪一道闸门。
                if (!_gateLogged)
                {
                    _gateLogged = true;
                    try
                    {
                        DLog.Force("引导闸门: enabled=" + _enabled + " started=" + _started
                            + " 弹窗占用=" + PanelInputGuard.AnyPopupActive()
                            + " 国家意志激活=" + NationalWillOrders.IsActive
                            + " 教程局=" + TutorialMode + " 挑战数=" + Goals.Count
                            + " 已完成=" + _done.Count
                            + " 当前=" + (Current() != null ? Current().Id : "无"));
                    }
                    catch { }
                }

                // 玩家正在读某个弹窗时: 不弹新的, 也不推进(避免连弹)
                if (PanelInputGuard.AnyPopupActive()) return;

                // 挂起的首次提示(前一次被弹窗挡下)
                if (!string.IsNullOrEmpty(_pendKey)) { FlushPendingTip(); return; }

                // 先让原版分步教程走完, 再弹引导说明(两套弹窗不同时抢屏)
                if (!_started)
                {
                    if (Tutorials.IntroActive) return;
                    ShowStart();
                    return;
                }

                ConceptChecks();
                // v4.263c: 当前挑战变化时记一行(便于事后确认挑战链有没有在推进)
                try
                {
                    var cg = Current();
                    if (cg != null && cg.Id != _loggedCur)
                    {
                        _loggedCur = cg.Id;
                        DLog.Force("引导: 当前挑战 -> " + cg.Id + " (" + cg.Title + ") 已完成 " + _done.Count + "/" + Goals.Count);
                    }
                    else if (cg == null && _loggedCur != "none")
                    {
                        _loggedCur = "none";
                        DLog.Force("引导: 已无待办挑战(Goals=" + Goals.Count + " 已完成=" + _done.Count + ")");
                    }
                }
                catch { }
                StepCurrent();
            }
            catch (Exception ex) { DLog.Force("引导 tick 异常: " + ex.Message); }   // v4.263f: 用 Force(原来 Info 不写正式日志, 异常被静默吞掉)
        }

        private static string _loggedCur = "";
        private static bool _inactiveLogged;      // v4.263d: "国家意志未激活"只记一次
        private static bool _pickProbeLogged;     // v4.263d: 选国引导等待诊断只记一次
        private static bool _gateLogged;          // v4.263d: 闸门诊断只记一次
        private static float _acc2;               // v4.263e: 选国块独立节流累加器(与挑战链的 _acc 分开)

        // 概念提示: 同一个 key 只弹一次(已看过的 key 随存档)
        internal static void ConceptTip(string key, string title, string body)
        {
            try
            {
                if (string.IsNullOrEmpty(key)) return;
                if (_tipsSeen.Contains(key)) return;
                if (PanelInputGuard.AnyPopupActive())
                {
                    if (string.IsNullOrEmpty(_pendKey)) { _pendKey = key; _pendTitle = title; _pendBody = body; }
                    return;
                }
                _tipsSeen.Add(key);
                DLog.Force("引导: 概念提示 " + key);
                ShowText(title, body, "知道了");
            }
            catch (Exception ex) { DLog.Info("概念提示失败: " + ex.Message); }
        }

        // 开关(存档记住; 重新开启不重置进度)
        internal static void SetEnabled(bool on)
        {
            try
            {
                if (_enabled == on) return;
                _enabled = on;
                _acc = 0f;
                DLog.Force("引导: " + (on ? "已开启" : "已关闭") + "(进度 " + _done.Count + "/" + Goals.Count + ")");
                if (!on) { _pendKey = null; return; }
                _started = true;   // 中途开启不再弹"引导开始"长文, 只报当前进度
                var g = RefreshCurrent();
                string body = g != null
                    ? ("挑战式引导已开启。\n· 进度: " + (_done.Count + 1) + "/" + Goals.Count + " · 当前挑战: " + g.Title
                        + "\n· 提示: " + g.Hint + "\n· 已完成的 " + _done.Count + " 项不会重来, 直接从这里继续。")
                    : "挑战式引导已开启。" + Goals.Count + " 项挑战此前已全部完成。";
                if (g != null) ShowChallenge("引导已开启 · 挑战 " + (_done.Count + 1) + "/" + Goals.Count, body, g, "查看");
                else ShowText("引导已开启", body, "知道了");
            }
            catch { }
        }

        // 教程页脚: 当前挑战 + 进度(任意页面的[教程]里都能看到)
        internal static string SummaryText()
        {
            try
            {
                if (!_enabled)
                    return "\n\n【挑战式引导】未开启(共 " + Goals.Count + " 项)"
                        + "\n· 点本弹窗下方的[开启引导]可随时开始; 已有进度不会重置, 已看过的说明也不会重复弹。";
                if (!_started)
                    return "\n\n【挑战式引导】接管国家后会自动开始, 共 " + Goals.Count + " 项挑战(经济 → 军事 → 政治 → 外交 → 海上)。";
                var g = RefreshCurrent();
                if (g == null)
                    return "\n\n【挑战式引导】" + Goals.Count + " 项挑战已全部完成 —— 引导结束, 之后随你发挥。";
                var sb = new System.Text.StringBuilder();
                sb.Append("\n\n【挑战式引导】挑战 ").Append(_done.Count + 1).Append('/').Append(Goals.Count)
                  .Append(" · ").Append(g.Title);
                sb.Append("\n· 怎么做: ").Append(g.How);
                sb.Append("\n· 为什么: ").Append(g.Why);
                sb.Append("\n· 提示: ").Append(g.Hint);
                if (_done.Count > 0) sb.Append("\n· 已完成 ").Append(_done.Count).Append(" 项(").Append(DoneTitles()).Append(")");
                return sb.ToString();
            }
            catch { return ""; }
        }

        // 该页正是当前挑战所在页时, 追加一句提示
        internal static string PageHint(string pageId)
        {
            try
            {
                if (string.IsNullOrEmpty(pageId)) return "";
                if (!_enabled || !_started) return "";
                var g = RefreshCurrent();
                if (g == null) return "";
                string pages = PagesOf(g.Id);
                if (string.IsNullOrEmpty(pages)) return "";
                if (("," + pages + ",").IndexOf("," + pageId + ",", StringComparison.Ordinal) < 0) return "";
                return "\n· 本页就是当前挑战(第 " + (_done.Count + 1) + " 项「" + g.Title + "」)要用的页面。";
            }
            catch { return ""; }
        }

        // ==================== 存档(纯文本) ====================
        // 格式: v1;开启;已弹开始;旧档标记;当前挑战id;完成id列表;已看概念key列表;
        internal static string Save()
        {
            try
            {
                var tips = new string[_tipsSeen.Count];
                _tipsSeen.CopyTo(tips);
                var sb = new System.Text.StringBuilder();
                sb.Append("v1;");
                sb.Append(_enabled ? "1" : "0").Append(';');
                sb.Append(_started ? "1" : "0").Append(';');
                sb.Append(_legacy ? "1" : "0").Append(';');
                sb.Append(string.IsNullOrEmpty(_current) ? "" : _current).Append(';');
                sb.Append(string.Join(",", _done.ToArray())).Append(';');
                sb.Append(string.Join(",", tips)).Append(';');
                return sb.ToString();
            }
            catch { return ""; }
        }

        internal static void Load(string data)
        {
            try
            {
                _done.Clear(); _doneSet.Clear(); _tipsSeen.Clear();
                _current = ""; _pendKey = null; _pendTitle = null; _pendBody = null; _acc = 0f;
                _hasBase = false;
                // v4.289: 海上篇章的适用性必须跟着新战役一起重置(否则上一局的判定会被沿用)
                _navyChecked = false; _navyApplicable = false; _navySkipLogged = false;
                if (string.IsNullOrEmpty(data))
                {
                    // v4.261: 旧存档一律"不开启引导"(用户要求: 教程不强制; 想看就在任意页面[教程]里手动开)
                    _legacy = true; _enabled = false; _started = true;
                    DLog.Force("引导: 存档无 FIA_Guide 键 -> 保持静默(可在任意页面[教程]里开启)");
                    return;
                }
                var p = data.Split(';');
                if (p.Length < 6 || p[0] != "v1")
                {
                    _legacy = true; _enabled = false; _started = true;   // v4.261: 同"键缺失"分支, 一律不强制
                    DLog.Force("引导: 存档段格式不认识 -> 保持静默");
                    return;
                }
                _enabled = p[1] == "1";
                _started = p[2] == "1";
                _legacy = p[3] == "1";
                string savedCurrent = p[4] ?? "";
                AddIds(_done, _doneSet, p[5]);
                if (p.Length > 6) AddIds(null, _tipsSeen, p[6]);
                // 容错: 存档里的 id 若已不在表里(改过内容), 直接忽略
                for (int i = _done.Count - 1; i >= 0; i--) if (Find(_done[i]) == null) { _doneSet.Remove(_done[i]); _done.RemoveAt(i); }
                // 当前挑战按"已完成列表 + 顺序 + 前置"重新推导(存档里的 id 只作日志),
                // 顺便强制重取一次起点快照 —— 否则"比开局多了多少"这类检测会因为基线全 0 而误判完成
                _current = "";
                _hasBase = false;
                DLog.Force("引导: 读档 开启=" + _enabled + " 已完成=" + _done.Count + "/" + Goals.Count
                    + " 当前=" + (savedCurrent.Length > 0 ? savedCurrent : "空") + " 已看概念=" + _tipsSeen.Count);
            }
            catch (Exception ex) { DLog.Force("引导读档失败: " + ex.Message); }
        }

        private static void AddIds(List<string> list, HashSet<string> set, string csv)
        {
            try
            {
                if (string.IsNullOrEmpty(csv)) return;
                foreach (var s in csv.Split(','))
                {
                    var t = s != null ? s.Trim() : "";
                    if (t.Length == 0) continue;
                    if (set != null && !set.Add(t)) continue;
                    if (list != null) list.Add(t);
                }
            }
            catch { }
        }

        // 新战役: 清空并按"是否教程局"决定开不开(用户要求: 教程不强制)
        internal static void ResetForNewCampaign()
        {
            try
            {
                _done.Clear(); _doneSet.Clear(); _tipsSeen.Clear();
                _current = ""; _pendKey = null; _pendTitle = null; _pendBody = null; _acc = 0f;
                // v4.261: 正常"战役"按钮进的局 -> 引导全程静默(零弹窗); 只有主菜单「蒸汽时代教程」才开启
                _enabled = TutorialMode;
                _started = !TutorialMode;
                _legacy = false; _heldSeen = false; _pickHinted = false;
                // v4.289: 海上篇章适用性跟着新战役重置(读档后要按当前局势重新判一次)
                _navyChecked = false; _navyApplicable = false; _navySkipLogged = false;
                // v4.290 修复: _goals 是惰性缓存且原先没有任何清空点 —— 不重置会让上一个战役
                // (甚至上一个教程局)构建的 12 条残留到新局, 海军链永远追加不进去。
                _goals = null;
                _base = new GuideSnap();
                _hasBase = false;
                DLog.Force(TutorialMode
                    ? "引导: 教程局 -> 挑战式引导已启用(选国后开始)"
                    : "引导: 普通新战役 -> 挑战式引导静默(可在任意页面[教程]里手动开启)");
            }
            catch { }
        }

        // ==================== 推进 ====================

        private static GuideGoal RefreshCurrent()
        {
            // v4.289: 先把"海上篇章适不适用"定下来(只判一次; 判完 Goals 的内容才是最终形态)
            CheckNavyApplicable();
            var l = Goals;
            for (int i = 0; i < l.Count; i++)
            {
                var x = l[i];
                if (_doneSet.Contains(x.Id)) continue;
                if (!string.IsNullOrEmpty(x.Prereq) && !_doneSet.Contains(x.Prereq)) continue;
                if (_current != x.Id || !_hasBase)
                {
                    _current = x.Id;
                    CaptureBaseline();
                    DLog.Force("引导: 当前挑战 -> " + x.Id + " · " + x.Title);
                }
                return x;
            }
            _current = "";
            return null;
        }

        // ==================== v4.289: 海上篇章能否挂进挑战链 ====================
        //   【为什么必须判】海军 6 条挑战全是"必须有自有沿海港口"才做得到的事: 内陆国家(全图无港口 /
        //     本国没占到沿海城镇)或没装海战 DLC 的局, 这 6 条会变成**永远做不完**的挑战, 把玩家卡死。
        //   【判据】(全部用已有的只读查询, 不新造口径)
        //     ① !TutorialMode           -> 普通局: 本来就不跑引导, 保持静默(沿用现有机制)
        //     ② !NavalBridge.Usable     -> 没装海战 DLC(或地图上没有任何港口) —— 海军功能整体停用
        //     ③ 全图有港口但 NavalBridge.PortsOf(k).Count == 0 -> 本国没有一处沿海城镇(内陆国家)
        //     ④ NavalFleet.MyShips().Count > 0    -> 例外: 玩家手上已经有船(买过/缴获过), 说明能玩, 照挂上
        //   其中 ③④ 需要"已接管王国"才能判 —— 王国还没定时**不判**(等下一 tick), 不写 _navyChecked;
        //   全图一处港口都没有时也**不判**(可能是聚落数据还没填完), 同样留到后面的 tick。
        //   【跳过是什么样】安静但可见:
        //     · 日志: DLog.Force("引导: 海上篇章跳过 ...") 一行, 写清是哪一条判据 + 当前的港口/船只数
        //     · 玩家: 弹一次引导提示(ConceptTip 同一套机制, 每个 key 只弹一次), 明说"没有出海口/未接入
        //       海战 DLC, 海军篇章跳过", 而不是让 6 条挑战默默消失
        //   ★ 判定只做一次: 万一玩家以后打下沿海城市, 本局中途也不会"补上"这 6 条 —— 挑战链的条数
        //     中途变化会让"已完成 x/y"与存档里的进度对不上。提示里也如实告诉玩家这一点。
        private static void CheckNavyApplicable()
        {
            try
            {
                if (_navyChecked) return;
                if (!TutorialMode) { _navyChecked = true; _navyApplicable = false; return; }

                // ① 没装海战 DLC / 地图上没有任何港口 -> 直接跳过(这条不需要王国)
                if (!NavalBridge.Usable)
                {
                    _navyChecked = true; _navyApplicable = false;
                    SkipNavyLog("未接入海战 DLC(NavalBridge.Usable=false)", 0, 0);
                    return;
                }

                // ② 需要"已接管王国"才能判本国有没有出海口; 王国未定 -> 不判, 下一 tick 再来
                var k = PlayerKingdom();
                if (k == null) return;

                // ②c v4.290: 王国刚建立但还没有任何聚落 -> 不判, 留到拿到第一座城/堡时再判。
                //     否则 PortsOf(k) 必为 0, 会被下面 ③ 当成"内陆国家"永久跳过(_navyChecked 一设就不再判)。
                int fiefs = 0;
                try { fiefs = k.Fiefs.Count; } catch { }
                if (fiefs <= 0) return;

                // ②b 全图一处港口都没有 -> 要么是没装 DLC, 要么是 **聚落数据还没填完**(加载期).
                //   这一条**不设 _navyChecked**, 留到后面的 tick 重判: 判早了会把沿海国家误判成内陆,
                //   而 NavalBridge.Usable 的正缓存本身也是"数据就绪后才转真"的设计(见那边的注释).
                int allPorts = 0;
                try { allPorts = NavalBridge.AllPorts().Count; } catch { }
                if (allPorts <= 0) return;

                int ports = 0, ships = 0;
                try { ports = NavalBridge.PortsOf(k).Count; } catch { }
                try { ships = NavalFleet.MyShips().Count; } catch { }

                // ③ 内陆国家(全图有港口, 但本国一处都没有) -> 跳过。
                // v4.290 修复: 去掉原先的 "&& ships <= 0" 例外(手上有船就照挂)。那条推理不成立 ——
                // 组建舰队硬要求 port.HasPort 且 KingdomOf(port)==k(NavalTaskForce.Found 的判据),
                // 没有自有港口时手上有船也开不出舰队, 挂上去会让 g_navy_fleet 及其后 4 条永久卡死。
                if (ports <= 0)
                {
                    _navyChecked = true; _navyApplicable = false;
                    SkipNavyLog("本国没有一处沿海城镇(内陆国家)", ports, ships);
                    return;
                }

                _navyChecked = true; _navyApplicable = true;
                // v4.290 修复: 判定是"一次定终身"(_navyChecked), 若首次判定 true 时 _goals 已经被
                // 别的路径建成 12 条, 海军链就永远追加不上。结果翻转为适用时丢弃旧缓存强制重建。
                _goals = null;
                DLog.Force("引导: 海上篇章适用(全图港口 " + allPorts + " 处 · 自有沿海城镇 " + ports
                    + " 处 · 我方船只 " + ships + " 艘 · 海战 DLC=" + NavalBridge.DlcLoaded + ") -> 追加 "
                    + NavyGoalCount + " 条海军挑战");
            }
            catch (Exception ex) { DLog.Force("引导: 海上篇章适用性判定异常 " + ex.Message); }
        }

        // 跳过: 日志留痕 + 玩家可见的一句说明(key 固定, 只弹一次)
        private static void SkipNavyLog(string why, int ports, int ships)
        {
            try
            {
                if (_navySkipLogged) return;
                _navySkipLogged = true;
                bool noDlc = !NavalBridge.DlcLoaded;
                DLog.Force("引导: 海上篇章跳过(本局不出现在挑战链里) —— 原因: " + why
                    + " · 自有沿海城镇 " + ports + " 处 · 我方船只 " + ships + " 艘 · 海战 DLC=" + NavalBridge.DlcLoaded
                    + " · 全图港口 " + AllPortCount() + " 处 · 共跳过 " + NavyGoalCount + " 条海军挑战");
                ConceptTip("concept_navy_skip", "海上篇章 · 本局跳过",
                    "· 原因: " + why + "。\n" +
                    (noDlc
                        ? "· 你的游戏没装海战 DLC(NavalDLC / War Sails), 所以海军整套功能都不存在 —— 面板不会出现。\n"
                        : "· 你的国家没有出海口(一处自有沿海城镇都没有), 所以海军那一套你一时半会儿用不上。\n") +
                    "· 因此这次不安排海军/海战教程: 你手上的挑战链只有陆上那些条, 不会多出做不完的任务。\n" +
                    "· 如果你的国家后来打下了沿海城镇, 本局中途也不会补上这 "
                    + NavyGoalCount + " 条(挑战条数中途变化会让进度对不上存档)。\n" +
                    "· 想看海军全套流程: 重新开一局「蒸汽时代教程」, 选一个沿海国家。");
            }
            catch { }
        }

        private static int AllPortCount()
        {
            try { return NavalBridge.AllPorts().Count; } catch { return 0; }
        }

        private static void StepCurrent()
        {
            var g = RefreshCurrent();
            if (g == null) return;
            // 记录"曾经接管过军团"(第 12 条要用)
            try { if (DefArmy.PlayerHeldCount() >= 1) _heldSeen = true; } catch { }
            if (g.Check == null) return;
            bool ok = false;
            try { ok = g.Check(); } catch { ok = false; }
            if (!ok) return;

            string spoils = SpoilsOf(g.Id);
            Complete(g.Id);
            var next = RefreshCurrent();
            string title = "挑战完成: " + g.Title;
            string body = "· 战果: " + spoils
                + "\n· 进度: " + _done.Count + "/" + Goals.Count + " 项已完成"
                + (next != null
                    ? "\n\n下一个挑战" + (_done.Count + 1) + "/" + Goals.Count + ": " + next.Title
                      + "\n· 提示: " + next.Hint
                    : "\n\n" + Goals.Count + " 项挑战全部完成 —— 引导到此结束, 剩下的交给你。");
            DLog.Force("引导: 挑战完成反馈 " + g.Id + " -> 下一个=" + (next != null ? next.Id : "无"));
            if (next != null) ShowChallenge(title, body, next, "查看");
            else ShowText(title, body, "知道了");
        }

        private static void CaptureBaseline()
        {
            try
            {
                var s = new GuideSnap();
                s.Gold = EconomyWorld.Treasury.Gold;
                s.Bld = BuildingCount();
                s.Garrison = GarrisonMen();
                s.Legions = DefArmy.Legions.Count;
                s.Held = DefArmy.PlayerHeldCount();
                s.Tech = Research.Done.Count;
                s.Laws = LawSum();
                s.TaxDay = TaxPolicy.ChangeDay;
                s.TaxPack = TaxPack();
                s.Report = BattleSim.LastReport ?? "";
                // v4.289: 海战记录的基线(海上篇章"打第一场海战"用)
                // v4.290: 基线用累计计数(滚动表 Count 会封顶在 12, 不能当基线)
                try { s.NavalBattle = Math.Max(NavalFleet.TotalBattles, NavalFleet.Battles.Count); } catch { s.NavalBattle = 0; }
                _base = s;
                _hasBase = true;
                if (s.Held >= 1) _heldSeen = true;
            }
            catch { }
        }

        // ==================== 检测用的廉价统计 ====================
        // 注意: 只在"当前挑战"的检测里用到, 且每 1.5 秒最多跑一次;
        //       建筑数只遍历本国定居点(不碰全图 Settlement.All)。

        private static int BuildingCount()
        {
            try
            {
                var b = NationalWillOrders.Behavior;
                var k = b != null ? b.NationKingdom : null;
                if (k == null) return 0;
                int n = 0;
                foreach (Settlement s in k.Settlements)
                {
                    if (s == null || string.IsNullOrEmpty(s.StringId)) continue;
                    SettlementBuildings sb;
                    if (EconomyWorld.Buildings.TryGetValue(s.StringId, out sb) && sb != null) n += sb.BuiltCount;
                }
                return n;
            }
            catch { return 0; }
        }

        private static int GarrisonMen()
        {
            try
            {
                int n = 0;
                for (int i = 0; i < DefArmy.Garrisons.Count; i++)
                {
                    var g = DefArmy.Garrisons[i];
                    if (g != null && g.Placed > 0) n += g.Placed;
                }
                return n;
            }
            catch { return 0; }
        }

        private static int LawSum()
        {
            try
            {
                int n = 0;
                for (int i = 0; i < LawSystem.LawCount; i++) n += LawSystem.Level(i);
                return n;
            }
            catch { return 0; }
        }

        // 各项检测(全部廉价; 只比大小/看标志, 不做全图遍历)
        private static bool LedgerSeen()
        {
            try
            {
                if (Tutorials.Visited("page_fiscal")) return true;
                if (PanelScreen.IsOpen("fiscal")) return true;
                return EconomyWorld.Treasury.Gold != _base.Gold;
            }
            catch { return false; }
        }

        // 四税档位打包成一个 int(便宜且能与快照直接比)
        private static int TaxPack()
        {
            try
            {
                return TaxPolicy.Level[0] * 1000 + TaxPolicy.Level[1] * 100
                     + TaxPolicy.Level[2] * 10 + TaxPolicy.Level[3];
            }
            catch { return 0; }
        }

        private static bool TaxTouched()
        {
            try
            {
                if (TaxPack() != _base.TaxPack) return true;        // 任一档位变了(最主要的口径)
                if (TaxPolicy.ChangeDay != _base.TaxDay) return true;  // 同档位再点一次也算"动手了"
                return TaxPolicy.ChangeDay != -9999;                 // 本战役里确实调整过税制
            }
            catch { return false; }
        }

        private static bool BuildingGrew()
        {
            try { return BuildingCount() > _base.Bld; } catch { return false; }
        }

        private static bool GarrisonReady()
        {
            try { return GarrisonMen() >= 100; } catch { return false; }
        }

        private static bool LegionExists()
        {
            try { return DefArmy.Legions.Count >= 1; } catch { return false; }
        }

        private static bool PlayerHolds()
        {
            try { return DefArmy.PlayerHeldCount() >= 1; } catch { return false; }
        }

        private static bool Drilling()
        {
            try
            {
                for (int i = 0; i < DefArmy.Legions.Count; i++)
                {
                    var lg = DefArmy.Legions[i];
                    if (lg == null) continue;
                    if (DefArmy.StanceOf(lg) != DefArmy.StanceDrill) continue;
                    if (DefArmy.TrainLevelOf(lg) >= 60f) return true;
                }
                return false;
            }
            catch { return false; }
        }

        private static bool TechDone()
        {
            try { return Research.Done.Count >= 1; } catch { return false; }
        }

        private static bool LawPassed()
        {
            try { return LawSum() >= 1; } catch { return false; }
        }

        private static bool BattleReported()
        {
            try
            {
                string rep = BattleSim.LastReport;
                if (string.IsNullOrEmpty(rep)) return false;
                if (rep == _base.Report) return false;
                // 只有"我方参战"的战报才算(其它国家互殴不推进引导)
                return rep.IndexOf("本方判定", StringComparison.Ordinal) >= 0;
            }
            catch { return false; }
        }

        private static bool TreasuryRich()
        {
            try { return EconomyWorld.Treasury.Gold >= 50000; } catch { return false; }
        }

        private static bool ReleasedBack()
        {
            try
            {
                if (DefArmy.PlayerHeldCount() != 0) return false;
                return _heldSeen || _base.Held >= 1;
            }
            catch { return false; }
        }

        // ==================== v4.289: 海上篇章的检测(全部走 Navy\ 里已有的只读函数) ====================
        //   与上面 12 条同一个纪律: 每条检测都必须廉价(每 1.5 秒最多跑一次, 只跑当前这一条),
        //   并且**必须**由真实游戏状态判定 —— 不看"点了哪个按钮", 只看游戏里真的发生了什么。
        //   ★ 每一步都用的是"玩家真要拿到这个状态, 就必须真的做完那件事"的口径:
        //     · 有船 = NavalBridge.MyShips().Count(买断/建造完工/缴获都会进这个口径)
        //     · 有舰队 = NavalTaskForce.Alive().Count(编制真的落地在地图上才计入)
        //     · 出航 = 舰队与母港的直线距离(只有真的被开出港口才成立)
        //     · 进驻 = NavalTaskForce.InBerth(f)(到位判定与业务里的到位口径同源)
        //     · 海运 = 军团在目的港 / 仍在海上航行(装船出发后才有)
        //     · 海战 = NavalFleet.Battles.Count 超过基线(战报页那一份记录)
        private static Kingdom PlayerKingdom()
        {
            try
            {
                var b = NationalWillOrders.Behavior;
                return b != null ? b.NationKingdom : null;
            }
            catch { return null; }
        }

        // ① 名下 ≥1 艘船
        private static bool NavyHasShip()
        {
            try { return NavalBridge.MyShips().Count >= 1; } catch { return false; }
        }

        // ② ≥1 支在编舰队
        private static bool NavyHasFleet()
        {
            try { return NavalTaskForce.Alive().Count >= 1; } catch { return false; }
        }

        // ③ 至少一支舰队真的被开出了母港(与母港海面直线距离 > 60 地图单位)
        //   60 = 港口海域之外的量级: 泊位/原地绕圈都在 40 以内(见 NavalTaskForce.MoorSnapRadius),
        //   而"左键点海面 / 点别的港口 / 点敌港封锁"下达后舰队会真的驶离母港海域。
        private const float NavySailAwayDist = 60f;

        private static bool NavySailed()
        {
            try
            {
                var list = NavalTaskForce.Alive();
                for (int i = 0; i < list.Count; i++)
                {
                    var f = list[i];
                    if (f == null) continue;
                    var home = NavalTaskForce.FindSettlement(f.HomePortId);
                    if (home == null) continue;
                    var p = NavalTaskForce.PartyOf(f);
                    if (p == null) continue;
                    if (p.Position.Distance(home.PortPosition) > NavySailAwayDist) return true;
                }
                return false;
            }
            catch { return false; }
        }

        // ④ ≥1 支舰队已进驻自有沿海城市并**到位**(InBerth 与业务里的到位口径同源)
        private static bool NavyMoored()
        {
            try
            {
                var k = PlayerKingdom();
                if (k == null) return false;
                var list = NavalTaskForce.Alive();
                for (int i = 0; i < list.Count; i++)
                {
                    var f = list[i];
                    if (f == null || !f.HasMoor) continue;
                    var port = NavalTaskForce.FindSettlement(f.MooredPortId);
                    if (port == null || !port.IsTown || !port.HasPort) continue;
                    if (!ReferenceEquals(NavalBridge.KingdomOf(port), k)) continue;
                    if (NavalTaskForce.InBerth(f)) return true;
                }
                return false;
            }
            catch { return false; }
        }

        // ⑤ 海运真的发生了: 军团在目的港, 或者此刻仍在海上航行
        private static bool NavyConvoySeen()
        {
            try
            {
                var list = NavalTransport.Active;
                for (int i = 0; i < list.Count; i++)
                {
                    var c = list[i];
                    if (c == null) continue;
                    // 运输队登记的 PartyId 就是那支陆军军团的部队 id(NavalTransport.Depart 里
                    //   `PartyId = p.StringId`, p = DefArmy.LegionParty(lg)), 所以按 id 直接取部队即可。
                    var p = DefArmy.FindParty(c.PartyId);
                    if (p == null) continue;
                    var to = NavalTaskForce.FindSettlement(c.ToPortId);
                    if (to == null) continue;
                    if (p.CurrentSettlement == to) return true;   // 已经到目的港下船
                    if (p.IsCurrentlyAtSea) return true;          // 还在海上航行(装船出发已成功)
                }
                return false;
            }
            catch { return false; }
        }

        // ⑥ 海战记录多了一条(本挑战基线之后新增)
        private static bool NavyBattleReported()
        {
            // v4.290 修复: 原先比 Battles.Count, 而它是上限 12 的滚动表 —— 进挑战时已有 12 条则恒 false。
            try
            {
                int t = NavalFleet.TotalBattles;
                if (NavalFleet.Battles.Count > t) t = NavalFleet.Battles.Count;
                return t > _base.NavalBattle;
            }
            catch { return false; }
        }

        // ---- 战果一行用到的小统计(全部容错, 取不到就回 "—") ----
        private static int ShipCountText()
        {
            try { return NavalBridge.MyShips().Count; } catch { return 0; }
        }

        private static string FleetCapText()
        {
            try
            {
                var k = PlayerKingdom();
                return k != null ? (NavalFleet.FleetCap(k) + " 艘") : "—";
            }
            catch { return "—"; }
        }

        private static int NavyFleetCount()
        {
            try { return NavalTaskForce.Alive().Count; } catch { return 0; }
        }

        private static int FleetShipCount()
        {
            try
            {
                int n = 0;
                var list = NavalTaskForce.Alive();
                for (int i = 0; i < list.Count; i++)
                {
                    var p = NavalTaskForce.PartyOf(list[i]);
                    if (p == null) continue;
                    n += NavalBridge.ShipsOf(p).Count;
                }
                return n;
            }
            catch { return 0; }
        }

        // 最远那支舰队离母港多少距离(战果文案用)
        private static string NavyFarthestText()
        {
            try
            {
                float best = 0f;
                var list = NavalTaskForce.Alive();
                for (int i = 0; i < list.Count; i++)
                {
                    var f = list[i];
                    if (f == null) continue;
                    var home = NavalTaskForce.FindSettlement(f.HomePortId);
                    if (home == null) continue;
                    var p = NavalTaskForce.PartyOf(f);
                    if (p == null) continue;
                    float d = p.Position.Distance(home.PortPosition);
                    if (d > best) best = d;
                }
                return ((int)best).ToString();
            }
            catch { return "—"; }
        }

        private static string MooredPortNameText()
        {
            try
            {
                var list = NavalTaskForce.Alive();
                for (int i = 0; i < list.Count; i++)
                {
                    var f = list[i];
                    if (f == null || !f.HasMoor) continue;
                    if (NavalTaskForce.InBerth(f)) return f.MooredPortName ?? "?";
                }
                return "?";
            }
            catch { return "?"; }
        }

        private static int NavalBattleCount()
        {
            try { return NavalFleet.Battles.Count; } catch { return 0; }
        }

        // 战果一行(完成反馈用)
        private static string SpoilsOf(string id)
        {
            try
            {
                switch (id)
                {
                    case "g_ledger":
                        return "已经会看国库与今日净额了(国库 " + EconomyWorld.Treasury.Gold.ToString("N0") + " 第纳尔)";
                    case "g_tax":
                        return "四税现在: " + TaxPolicy.Names[0] + " " + TaxPolicy.LevelNames[TaxPolicy.Level[0]]
                            + " / " + TaxPolicy.Names[1] + " " + TaxPolicy.LevelNames[TaxPolicy.Level[1]]
                            + " / " + TaxPolicy.Names[2] + " " + TaxPolicy.LevelNames[TaxPolicy.Level[2]]
                            + " / " + TaxPolicy.Names[3] + " " + TaxPolicy.LevelNames[TaxPolicy.Level[3]];
                    case "g_build":
                        return "本国已建成建筑 " + BuildingCount() + " 栋";
                    case "g_recruit":
                        return "守备营合计 " + GarrisonMen() + " 兵";
                    case "g_legion":
                        return "野战军团 " + DefArmy.Legions.Count + " 支 · 国防军总兵力 " + DefArmy.TotalMen() + " 兵";
                    case "g_command":
                        return "归玩家指挥 " + DefArmy.PlayerHeldCount() + " 支(军事总监不会再调走它们)";
                    case "g_drill":
                        return "训练度已到 " + ((int)Math.Round(MaxTrain())).ToString() + "/100";
                    case "g_tech":
                        return "已研究 " + Research.Done.Count + " 项科技";
                    case "g_law":
                        return "法律已推进 " + LawSum() + " 档";
                    case "g_war":
                        return "已有 " + BattleSim.Recent.Count + " 条战报可复盘";
                    case "g_treasury":
                        return "国库 " + EconomyWorld.Treasury.Gold.ToString("N0") + " 第纳尔";
                    case "g_release":
                        return "归玩家指挥 " + DefArmy.PlayerHeldCount() + " 支(已交还的军团恢复自动调度)";
                    // ==================== v4.289: 海上篇章(6 条) ====================
                    case "g_navy_ship":
                        return "名下已有 " + ShipCountText() + " 艘船(舰队上限 " + FleetCapText() + ")";
                    case "g_navy_fleet":
                        return "在编舰队 " + NavyFleetCount() + " 支 · 在编舰船 " + FleetShipCount() + " 艘";
                    case "g_navy_sail":
                        return "舰队已离开母港海域 " + NavyFarthestText() + " 距离";
                    case "g_navy_moor":
                        return "舰队已进驻「" + MooredPortNameText() + "」(船维护 -35% + 每日在港修理)";
                    case "g_navy_transport":
                        return NavyConvoySeen() ? "陆军军团已由海路运送(目的港下船)" : "海运运输队已出发";
                    case "g_navy_battle":
                        return "海战记录已有 " + NavalBattleCount() + " 条(见海军页[战报]页签)";
                }
            }
            catch { }
            return "挑战达成";
        }

        private static float MaxTrain()
        {
            try
            {
                float m = 0f;
                for (int i = 0; i < DefArmy.Legions.Count; i++)
                {
                    var lg = DefArmy.Legions[i];
                    if (lg == null) continue;
                    float t = DefArmy.TrainLevelOf(lg);
                    if (t > m) m = t;
                }
                return m;
            }
            catch { return 0f; }
        }

        // ==================== 概念提示触发点(首触) ====================
        private static void ConceptChecks()
        {
            try
            {
                // ① 第一次打开[市场]页: MAPI 与价差
                if (Tutorials.Visited("page_market"))
                    ConceptTip("concept_market", "概念 · 市场与价差",
                        "· 全图 20 种商品各城一个本地价, 由本地供需驱动、每日浮动(±75% 钳制)。\n" +
                        "· 跨城价差就是钱: 某城便宜、别城贵 -> 商队与贸易路线套利(注意关税与路上战损)。\n" +
                        "· 极端价差会被市场接入度慢慢拉平: 差价越离谱, 回落压力越大 —— 别指望价差永远在。\n" +
                        "· 战时囤粮(财政页[战略储备])会抽走市场供给、推高粮价; 围城与掠夺会打断当地供给 30 天。");

                // ② 第一次出现赤字(今日支出 > 今日收入): 固定支出与四税
                if (Fiscal.TodayExpense > Fiscal.TodayIncome && Fiscal.TodayExpense > 0)
                    ConceptTip("concept_deficit", "概念 · 赤字: 钱是怎么漏掉的",
                        "· 两条固定支出: 军饷 0.5/兵/日 · 建筑维护 1.5 金/栋/日(军团切[操练]时军饷 ×1.3)。\n" +
                        "· 收入主力是四税([财政]页右栏调档) + 铸币 + 出口 + 国有分红; 什一税进教会池, 不入国库。\n" +
                        "· 四税总和超过 4.0(全中档)开始反噬: 全国激进 +0.004/人/日 · 工商产出 -5%/点 · 迁移吸引力 -6%/点。\n" +
                        "· 国库连续 7 天见底 = 破产 30 天(建造效率 -75%), 同时领主讨债、欠饷哗变。");

                // ③ 第一次拥有野战军团: 军饷/军粮/编制上限
                if (DefArmy.Legions.Count >= 1)
                    ConceptTip("concept_legion", "概念 · 养一支军团要花什么",
                        "· 军饷 0.5/兵/日 + 将军月薪 30, 从国库直接扣([财政]页[国防军军费]一行)。\n" +
                        "· 军粮 0.006/兵/日, 断粮会逃散; [行军]状态军粮 ×1.4, [征粮]就地取粮但掉当地忠诚、涨民怨。\n" +
                        "· 编制上限 = 600 + 统率×12 + 战术×6 + 军事科技×12; 补员从驻地守备营抽兵, 不会自动补。\n" +
                        "· 训练度 0~100 = 战斗力 ×0.8~1.2; 老兵占比 = 熟练度 ≥70 的人; 将军特质决定炮击/缴获/伤亡/后勤。");

                // ④ 第一次欠饷: 哗变
                if (DefArmy.UnpaidDays > 0)
                    ConceptTip("concept_unpaid", "概念 · 欠饷会哗变",
                        "· 国库付不出军饷的那天起累积[欠饷天数], 到 7 天全军士气 -30%, 到 14 天直接哗变逃散。\n" +
                        "· 三条止血路: 提税([财政]页) · 解散或缩编军团([军务]页) · 把军团从[操练]切回[戒备](军饷 ×1.3 -> ×1.0)。\n" +
                        "· 强制征兵 5/兵比募兵 20/兵便宜, 但涨民怨、掉忠诚; 民怨 ≥50 乡民会递[反征兵请愿]。");
            }
            catch { }
        }

        // ==================== 弹窗 ====================

        private static void FlushPendingTip()
        {
            try
            {
                string k = _pendKey, t = _pendTitle, b = _pendBody;
                _pendKey = null; _pendTitle = null; _pendBody = null;
                if (string.IsNullOrEmpty(k) || _tipsSeen.Contains(k)) return;
                _tipsSeen.Add(k);
                DLog.Force("引导: 概念提示(补弹) " + k);
                ShowText(t, b, "知道了");
            }
            catch { }
        }

        private static void ShowStart()
        {
            try
            {
                _started = true;
                var g = RefreshCurrent();
                string body =
                    "从现在起, 你的统治会以「挑战」的方式一步步展开: 完成一项, 才解锁下一项。\n" +
                    "· 每条挑战都配两条说明: [怎么做]指到具体页面与按钮, [为什么]讲清代价与后果。\n" +
                    "· 引导不影响沙盒: 你随时可以做别的事, 挑战只回答「接下来该学什么」。\n" +
                    "· 随时可关: 弹窗里选[关闭引导]; 之后在任意页面点[教程]都能看到引导进度, 也能在那里重新开启。\n" +
                    "· 进度自动存档: 关掉再开不会重置, 已看过的说明也不会重复弹。\n\n"
                    + (g != null
                        ? "第一个挑战: " + g.Title + "\n· 提示: " + g.Hint
                        : Goals.Count + " 项挑战此前已全部完成。");
                DLog.Force("引导: 弹出开始说明(第一个挑战=" + (g != null ? g.Id : "无") + ")");
                if (g != null) ShowChallenge("引导开始 · 挑战 1/" + Goals.Count, body, g, "开始");
                else ShowText("引导开始", body, "知道了");
            }
            catch (Exception ex) { DLog.Force("引导开始弹窗失败: " + ex.Message); }
        }

        // 带 [怎么做] / [为什么] 两个入口的挑战弹窗
        // v4.263: 改用自定义可拖动浮窗(原来走原版 Inquiry) —— 鼠标在窗上时左右键都被吞掉, 不会穿透到地图
        private static void ShowChallenge(string title, string body, GuideGoal g, string okLabel)
        {
            try
            {
                string gid = g != null ? g.Id : "";
                string prog = "挑战 " + Math.Min(_done.Count + 1, Goals.Count) + "/" + Goals.Count;
                // v4.263c: 每条挑战弹窗自动附上"怎么做" —— 原来正文只给目标与战果, 玩家看完不知道下一步点哪里,
                //   观感就是"引导只弹了一条就没了"。现在把 How 直接铺在正文里(仍保留[怎么做]按钮看全文)。
                string full = body;
                try
                {
                    var cur0 = Find(string.IsNullOrEmpty(gid) ? (Current() != null ? Current().Id : "") : gid);
                    if (cur0 != null && !string.IsNullOrEmpty(cur0.How) && (full == null || full.IndexOf("怎么做", StringComparison.Ordinal) < 0))
                        full = (full ?? "") + "\n\n【怎么做】" + cur0.How;
                }
                catch { }
                TutorialWindow.Show(title, full, prog,
                    okLabel, "怎么做", "为什么值得做",
                    delegate { TutorialWindow.HideNow(); },
                    delegate
                    {
                        var cur = Find(string.IsNullOrEmpty(gid) ? (Current() != null ? Current().Id : "") : gid);
                        ShowText("怎么做 · " + (cur != null ? cur.Title : ""), cur != null ? cur.How : "", "知道了");
                    },
                    delegate
                    {
                        var cur = Find(string.IsNullOrEmpty(gid) ? (Current() != null ? Current().Id : "") : gid);
                        ShowText("为什么 · " + (cur != null ? cur.Title : ""), cur != null ? cur.Why : "", "知道了");
                    });
            }
            catch (Exception ex) { DLog.Force("挑战弹窗失败: " + ex.Message); }
        }

        // v4.263: 文本页(怎么做/为什么)也走同一个自定义浮窗, 按钮统一为 [知道了]/[怎么做]/[为什么]
        private static void ShowText(string title, string body, string okLabel)
        {
            try
            {
                string gid = Current() != null ? Current().Id : "";
                TutorialWindow.Show(title, body ?? "", "挑战式引导",
                    okLabel ?? "知道了", "怎么做", "为什么值得做",
                    delegate { TutorialWindow.HideNow(); },
                    delegate
                    {
                        var cur = Find(gid);
                        ShowText("怎么做 · " + (cur != null ? cur.Title : ""), cur != null ? cur.How : "", "知道了");
                    },
                    delegate
                    {
                        var cur = Find(gid);
                        ShowText("为什么 · " + (cur != null ? cur.Title : ""), cur != null ? cur.Why : "", "知道了");
                    });
            }
            catch (Exception ex) { DLog.Force("引导文本弹窗失败: " + ex.Message); }
        }

        // 挑战 -> 该挑战要用的页面 id(只用于"本页就是当前挑战"提示)
        private static string PagesOf(string goalId)
        {
            switch (goalId)
            {
                case "g_ledger": return "page_fiscal";
                case "g_tax": return "page_fiscal";
                case "g_build": return "page_build,page_drawer,page_garrison";
                case "g_recruit": return "page_army,page_garrison";
                case "g_legion": return "page_army,page_garrison";
                case "g_command": return "page_troops,page_army";
                case "g_drill": return "page_army";
                case "g_tech": return "page_tech";
                case "g_law": return "page_politics";
                case "g_war": return "page_diplo,page_diploplay,page_troops";
                case "g_treasury": return "page_fiscal,page_stats,page_market";
                case "g_release": return "page_army";
                // v4.289: 海上篇章 —— 六条全部落在海军页(海军页本身没有[教程]按钮,
                //   这一行是给"面板里其它入口打开的教程页"与将来接入口用的; 不影响挑战判定)
                case "g_navy_ship": return "page_navy";
                case "g_navy_fleet": return "page_navy";
                case "g_navy_sail": return "page_navy,page_troops";
                case "g_navy_moor": return "page_navy,page_drawer";
                case "g_navy_transport": return "page_navy";
                case "g_navy_battle": return "page_navy";
            }
            return "";
        }

        private static string DoneTitles()
        {
            try
            {
                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < _done.Count; i++)
                {
                    var g = Find(_done[i]);
                    if (g == null) continue;
                    if (sb.Length > 0) sb.Append(" · ");
                    sb.Append(g.Title);
                }
                return sb.ToString();
            }
            catch { return ""; }
        }

        // ==================== 12 条挑战(经济 → 军事 → 政治 → 外交) ====================
        private static List<GuideGoal> Goals
        {
            get
            {
                if (_goals != null) return _goals;
                // v4.290 修复: 惰性构建前必须先跑适用性判定。
                // 原先只有 RefreshCurrent():417 调 CheckNavyApplicable, 而 Tick():180 的诊断日志
                // 会在那之前读 Goals.Count —— 教程局第一个 1.5 秒 Tick 就把它固化成 12 条(当时王国未定,
                // CheckNavyApplicable 直接 return 不判), 此后 _navyApplicable 变 true 也追加不上那 6 条。
                // 放在属性入口后, 无论谁先读 Goals 都先判定, 从根上消除这个竞态。
                CheckNavyApplicable();
                var l = new List<GuideGoal>();

                l.Add(new GuideGoal("g_ledger", "看懂账本",
                    "① 点左侧导航栏第 7 格[财政]打开账本。\n" +
                    "② 看顶部两行: [国库] = 现在有多少钱; [今日净额] = 今天国库的实际变动(绿加红减)。\n" +
                    "③ 左列是收入(税收/铸币/出口/分红), 右列是支出(军饷/建筑维护/关税/利息/行会年金/宫廷)。\n" +
                    "④ 右栏四行是税制(土地/人头/什一/市场), 每行最右边五个小格就是档位选择器。",
                    "国库是唯一的硬约束: 军饷 0.5/兵/日、建筑维护 1.5 金/栋/日, 天天都要付; 税只随繁荣缓慢上涨。\n" +
                    "今日净额连续为负 -> 7 天后破产 30 天(建造效率 -75%), 同时领主讨债、欠饷哗变。\n" +
                    "先搞清钱从哪来、往哪去, 后面每一步(募兵/建军/立法)才知道自己付不付得起。",
                    "左侧图标列第 7 格就是[财政]。", null,
                    delegate { return LedgerSeen(); }));

                l.Add(new GuideGoal("g_tax", "调一次税",
                    "在[财政]页右栏的税制四行里动手: 点某一行最右边的五个小方块 = 直接选档; 点整行 = 循环切档。\n" +
                    "五档是 免征 / 低 / 中 / 高 / 苛征, 对应倍率 0 / 0.5 / 1 / 1.5 / 2。\n" +
                    "改完立刻生效, 没有冷却; 页面下方的[税负效果]一行会显示当前的副作用。",
                    "四税是国库的主收入, 但四个档位加起来超过 4.0(全部中档)就开始反噬: 每超 1 点, 全国激进 +0.004/人/日、\n" +
                    "工商产出 -5%、迁移吸引力 -6%。什一税进教会池, 不进国库 —— 别指望提它救急。\n" +
                    "常见坑: 一缺钱就把四税全拉满, 结果激进爆掉、人口外流, 税基反而缩水。缺钱先看[统计]页找原因, 再决定提哪一档。",
                    "点小格直接选档, 点整行循环切档。", "g_ledger",
                    delegate { return TaxTouched(); }));

                l.Add(new GuideGoal("g_build", "盖起第一栋楼",
                    "① 在地图上左键点一座本国城镇/城堡 -> 弹出[驻军列表]。\n" +
                    "② 点底部的[查看建筑] -> 打开该城的建筑列表。\n" +
                    "③ 找到想盖的建筑, 点那一行右侧的[+]排队建造(点[−]是取消在建/拆除已建; 每城最多 5 项在建)。\n" +
                    "④ 建造要三样: 建造点(当地[行政部门]产出) + 材料(扣该城市场的木材/石料/铁) + 时间\n" +
                    "   (每项每日最多推进总工时的 20%; 材料不齐会停滞, 进度不倒退)。\n" +
                    "⑤ 队列推完 1 项就算完成; 想查已建建筑的经营情况, 到[建筑]页(左栏第 3 格)点该行看详情。",
                    "建筑产出 -> 市场供给 -> 物价 -> 人口生活水平 -> 税收与民心, 这是整条经济链的源头。\n" +
                    "代价也不能忘: 每栋建筑每天 1.5 金维护费, 摊子铺太大而税收跟不上就是慢性破产。\n" +
                    "常见坑: 一上来就盖最贵的。第一栋优先选能出建造点的行政部门或本地缺的资源类, 让后面滚起来。",
                    "点地图上的本国城镇 -> [查看建筑] -> 找到建筑那一行, 点[+]排队。", "g_tax",
                    delegate { return BuildingGrew(); }));

                l.Add(new GuideGoal("g_recruit", "募兵 100",
                    "① [军务]页(左栏第 13 格) -> 默认页签[募兵与征兵]。\n" +
                    "② [列表选择]或[地图点选]选一个本国城镇/城堡(选村庄会并入它所属的城镇守备营)。\n" +
                    "③ 用 −100 / −10 / +10 / +100 或[键入]调数量。\n" +
                    "④ 点[募兵]执行: 20 金/兵, 装备从国家军械库领装(领不满按满足率结算, 领装不另扣金)。",
                    "守备营是兵员池: 只有攒到 100 兵才能成立野战军团, 也是日后补员的来源。\n" +
                    "兵是 1:1 从当地平民抽的(总人口不变), 抽不出人就募不满。\n" +
                    "另一条路是[强制征兵](5/兵, 需先在[政治]页立法[军务征召]): 便宜但涨民怨、掉忠诚, 民怨 ≥50 会递反征兵请愿。\n" +
                    "常见坑: 只看单价忘了装备 —— 军械库没货时, 兵是募到了, 但战斗力会打折。",
                    "[军务]页 -> 选地点 -> 调数量 -> [募兵]。", "g_build",
                    delegate { return GarrisonReady(); }));

                l.Add(new GuideGoal("g_legion", "成立第一支军团",
                    "① [军务]页 -> 页签[守备营]。\n" +
                    "② 找到兵力 ≥100 的那一行, 点最右的[成立军团]。\n" +
                    "③ 在建军设计器里拖四条编制条(步兵/射击/骑兵/骑射), 右下可选[工具/野战医院/电报机]支援。\n" +
                    "④ 点左下[成立军团(装备/花费)]确认: 费用 200 + 10/兵, 从守备营全额抽兵, 将军免费附送。",
                    "守备营只能守城; 有了军团才能上地图打仗、围城、护送。军团出现在城门外, 行军速度恒定为 3.0。\n" +
                    "养它的成本是持续的: 军饷 0.5/兵/日 + 将军月薪 30, 从国库直接扣 —— 建军前先看[财政]页的今日净额撑不撑得住。\n" +
                    "将军是随机生成的, 带 1~3 个特质(炮兵专家/后勤天才/严酷军纪…), 点行内特质文字可看说明。\n" +
                    "常见坑: 建了一堆小军团。军团越少越好管, 军饷也更好控。",
                    "[军务] -> [守备营] -> 行末[成立军团]。", "g_recruit",
                    delegate { return LegionExists(); }));

                l.Add(new GuideGoal("g_command", "指挥它走一趟",
                    "在地图上左键点那支绿圈国防军 = 选中(按住左键拖框可多选)。\n" +
                    "选中后: 左键点地面 = 移动 · 左键点本国城市 = 进城 · 左键点敌国城市 = 围城 · 左键点敌军 = 攻击。\n" +
                    "右键选中的国防军 = 小队菜单(驻防/巡逻/补员/解散/拆分/归玩家指挥/交还军事总监)。",
                    "你下过命令的军团会被标记「归玩家指挥」, 军事总监、铁路 AI、外交 AI 都不会再把它调走 —— 这就是国家意志的手。\n" +
                    "代价: 它也不再自动回防、不会自己补员, 得你自己盯。\n" +
                    "常见坑: 下完命令就不管了。国防军速度恒定 3.0, 追不上轻装敌军; 长途奔袭要留出时间, 或者用铁路运兵。",
                    "左键点绿圈国防军 = 选中, 再左键点地图 = 下命令。", "g_legion",
                    delegate { return PlayerHolds(); }));

                l.Add(new GuideGoal("g_drill", "让它操练起来",
                    "① [军务]页 -> 页签[野战军团]。\n" +
                    "② 在军团行里点[状态]按钮循环切换: 戒备 -> 操练 -> 休整 -> 行军 -> 征粮。\n" +
                    "③ 切到「操练」后等训练度涨到 60 以上(操练 +0.35/日); 点行内[将军特质]文字可看当前状态与训练度。",
                    "训练度 0~100 直接决定战斗力系数 ×0.8~1.2(60 = ×1.04), 平时练、战时省命。\n" +
                    "代价是军饷 ×1.3 —— 和平时期练精兵很划算, 战时别把国库练空。\n" +
                    "其它状态各有取舍: 休整恢复士气与组织度、伤员快愈 · 行军军粮 ×1.4 · 征粮就地取粮但掉当地忠诚、涨民怨。\n" +
                    "常见坑: 一直挂在[操练]忘了切回来, 军饷悄悄多出三成。",
                    "军团行里的[状态]按钮点一下换一档, 切到「操练」。", "g_command",
                    delegate { return Drilling(); }));

                l.Add(new GuideGoal("g_tech", "研究出第一项科技",
                    "① 点[科技]页(左栏第 15 格), 顶上是[生产/军事/社会]三条树的页签。\n" +
                    "② 左键点一个当前可研究的节点开始研究(会显示创新成本); 三条线可以同时各研究一项。\n" +
                    "③ 创新点逐日累积, 上限受识字率限制 —— 想快就先办教育(大学、教育类法律)。",
                    "科技是「能不能造出好东西」的总开关: 军事科技解锁装备型号(军械库只发已解锁的), \n" +
                    "社会科技解锁法律与机构, 生产科技提高建筑产出。\n" +
                    "时代惩罚: 低时代还有没做完的科技时, 高时代的研究成本会明显上涨 —— 先补旧账更省。\n" +
                    "常见坑: 三条线都堆在高时代, 结果每条都慢。先把一条线的低时代清干净。",
                    "[科技]页点一个节点就开始研究, 三线并行。", "g_drill",
                    delegate { return TechDone(); }));

                l.Add(new GuideGoal("g_law", "通过第一项法律",
                    "① 点[政治]页(左栏第 11 格) -> 页签[法律]。\n" +
                    "② 左侧点一部法(如[税赋]、[教育]); 右侧四张档位卡点一张作为要换成的目标。\n" +
                    "③ 点[立案]付权威 -> 之后每 14 天一个检查点(成功推进/稳步推进/僵局/辩论) -> 进度满 100 换成新法。\n" +
                    "④ 支持者提高成功率、反对者提高僵局率; 合法性 <25 时只有集团运动推动的法案能立案。",
                    "法律是长期国策: 税赋/土地/经济体制影响税收与产出, 平民权利/教育/言论影响激进与识字率。\n" +
                    "立法要花权威(每月恢复), 走完通常要几十天 —— 越早开始越好。急用可以[强推](立即 +25% 进度, 但合法性 -3、各集团不满)。\n" +
                    "常见坑: 一上来就立最激进的法。先立能提高收入或降低激进的法, 后面才有本钱推大改革。",
                    "[政治] -> [法律] -> 选法 -> 选档 -> [立案]。", "g_tech",
                    delegate { return LawPassed(); }));

                l.Add(new GuideGoal("g_war", "打第一场仗并读战报",
                    "① 选中你的军团 -> 左键点敌国城镇 = 围城, 左键点敌军部队 = 攻击。\n" +
                    "② 战斗结束后左下角消息流会提示; 战报明细在[军队]页(左栏第 14 格) -> 底部[军务] -> [战报](最近 8 条)。\n" +
                    "③ 战报里有地形、战斗条件、双方输出、交换比、分阶段结算(遭遇→炮击→远程→近战→追击)与缴获。",
                    "战报是唯一的复盘材料: 兵力差不多却打输, 通常是兵种构成、训练度、组织度或补给的问题, 都能在战报里看出来。\n" +
                    "国防军不会自动补员 —— 打完硬仗记得回[军务]页点[补员](从驻地守备营抽兵, 受编制上限约束)。\n" +
                    "常见坑: 一路打下去不补员, 军团越打越薄, 最后被反推。\n" +
                    "要开战前先去[外交]页走一遍[外交博弈], 别直接硬宣(主动宣战会积累恶名、压低所有国家对你的关系)。",
                    "打完一场(我方参战)就会生成战报。", "g_law",
                    delegate { return BattleReported(); }));

                l.Add(new GuideGoal("g_treasury", "给国库攒到 5 万",
                    "在[财政]页看[国库]数字; 攒钱的四条路:\n" +
                    "① 提税: 右栏四税档位(注意别超过总和 4.0)。\n" +
                    "② 铸币: 右栏[铸币权]三格(足银/九成/七成), 成色越低收入越高、通胀越大。\n" +
                    "③ 出口: [财政]页底部[贸易路线], 把本国便宜的商品卖到贵的国家。\n" +
                    "④ 节流: 解散养不起的军团、卖掉不赚钱的建筑(建筑详情页[出售])。",
                    "5 万不是目的, 是安全垫: 破产要 7 天见底 + 30 天惩罚, 一场大战的军饷与补员也能吃掉几千。\n" +
                    "有钱才能在关键节点动手 —— 宣战、立法、铺铁路、囤粮, 全都要现钱。\n" +
                    "常见坑: 为了攒钱把税拉满, 激进爆掉反而更贵。看[统计]页的 30 天滚动账本找真正的漏点。",
                    "国库数字在[财政]页顶部。", "g_war",
                    delegate { return TreasuryRich(); }));

                l.Add(new GuideGoal("g_release", "交还一次指挥权",
                    "① [军务]页 -> 页签[野战军团] -> 底部[全军交还军事总监](一次交还全部)。\n" +
                    "② 或者在地图上右键单支国防军 -> [交还军事总监]。\n" +
                    "注意[全军回防]是另一件事: 它把全军叫回驻地, 但仍然归你指挥。",
                    "这是「国家意志」的放手阀: 交还后军事总监、铁路 AI、外交 AI 会重新自动调度这些军团\n" +
                    "(自己休整、集结、驰援、进攻), 你不用再手操每一支。\n" +
                    "两种玩法都对: 战时自己全权掌控, 战后交还给 AI 省心 —— 引导只是要让你知道开关在哪。\n" +
                    "常见坑: 以为交还就失控了。想再拿回来, 下一条命令就够(命令会让它重新标记「归玩家指挥」)。",
                    "[军务] -> [野战军团] -> 底部[全军交还军事总监]。", "g_treasury",
                    delegate { return ReleasedBack(); }));

                // ==================== v4.289: 海上篇章(6 条, 挂在 g_release 之后) ====================
                //   【为什么挂在这里】这 6 条**全部**要在自有沿海港口 + 舰船 + 造船厂上花钱与花时间:
                //     · 买一条现货轻型船约 1.5 倍船价, 造船厂还要先建/先升(1 级轻 / 2 级中 / 3 级重);
                //     · 组建舰队要 2 艘以上(至少留 1 艘在氏族手里), 海运要付 2/人 + 20/船的补给费,
                //       封锁 500/次, 出击还要按参战舰数收补给费。
                //   而 g_treasury("给国库攒到 5 万")正是整条链的"钱够了"里程碑, g_release 是"陆上交还
                //   指挥权"的收尾。把海军放在这两条之后 = 玩家已经会看账、会建军、会打仗、也有钱了,
                //   这时候再展开一个**全新的兵种域**最自然(也最不容易因为缺钱而卡住)。
                //   ★ 位置选择上**不插进**现有 12 条中间: 那会把 g_release 的 Prereq 从 g_treasury 改掉,
                //     等于重构既有链条; 追加在末尾则现有 12 条的 Prereq 与判定逻辑一字不动。
                //   ★ 整段用 if (_navyApplicable) 包住: 内陆国家/未装海战 DLC 时这 6 条一条都不进 Goals
                //     (适用性判定与跳过提示见 CheckNavyApplicable / SkipNavyLog)。
                if (_navyApplicable)
                {
                    l.Add(new GuideGoal("g_navy_ship", "买下第一条船",
                        "① 左侧导航栏最下面一格[海军]打开海军页, 点页签[造船]。\n" +
                        "② 顶上那一行就是当前造船港口(显示「我方自有沿海城镇」); 换城用 ◀ ▶, 或者点[选择城市]\n" +
                        "   然后在地图上直接左键点那座沿海城镇(再点一次[取消选点] / 右键 / Esc 都能退出点选)。\n" +
                        "③ 数据区每一行是一种舰型, 点那一行最右边的金色行内键:\n" +
                        "   [购买] = 买现货, 立刻过户; [下单建造] = 便宜一成但要等几天(占该港造船队列)。\n" +
                        "④ 灰色[未解锁]的行不能买, 说明里写着差什么 —— 要 2/3 级造船厂就去[建筑]页升该城的造船厂。",
                        "船是海军的唯一硬门槛: 没有船, [组建舰队]/[封锁]/[海运]/[出击] 全部点不动。\n" +
                        "现货价 = 1.5 倍船价, 下单建造 = 1.35 倍(便宜 10%)—— 不急就下单, 急用才买现货。\n" +
                        "买了就要养: 每条船按船体价与完整度收维护费(轻型约 5/日, 重型约 60/日), 从国库直接扣;\n" +
                        "舰队总上限 = 4 + 海军科技 + 自有港口数 + 各港造船厂的容量, 满了要研究海军科技或占更多港口。\n" +
                        "常见坑: 只买最便宜的船去封锁。封锁与海战按战力算, 一整队轻型船打不过人家一条重型。",
                        "[海军] -> [造船] -> 选自有沿海城镇 -> 行末金色键[购买]/[下单建造]。", "g_release",
                        delegate { return NavyHasShip(); }));

                    l.Add(new GuideGoal("g_navy_fleet", "组建第一支舰队",
                        "① 先确保名下至少有 2 艘船(见上一条): [海军]页 -> 页签[造船]。\n" +
                        "② 切到页签[舰队], 点[组建舰队]。\n" +
                        "③ 第一步弹窗选母港: 只能选【自有 + 有港口 + 还没驻扎舰队】的沿海城镇, 选完点[下一步]。\n" +
                        "④ 第二步弹窗勾选要编入的舰船(每条后面写着舰种/船员/完整度/战力), 点[组建]。\n" +
                        "   必须至少留 1 艘船在氏族手里, 所以自由船不足时这一步会被拒绝并弹出原因。",
                        "编成后舰队会真的出现在母港海面上, 成为地图上一支可以点、可以下令的部队(名字前缀是舰队编制);\n" +
                        "水手自动从该港守备营抽调(最多抽到所选舰船的载员上限与守备营现有兵力), 所以母港最好先有兵。\n" +
                        "代价: 船维护 + 水手军饷(与陆军同单价)每天从国库扣; 一支舰队只能有一个母港, 一个港口也只能驻一支舰队。\n" +
                        "常见坑: 把船全编进舰队 —— 舰队本身与海运、买船共用同一批船源, 系统强制留 1 艘就是为了这个。",
                        "[海军] -> [舰队] -> [组建舰队] -> 选母港 -> 勾船 -> [组建]。", "g_navy_ship",
                        delegate { return NavyHasFleet(); }));

                    l.Add(new GuideGoal("g_navy_sail", "把舰队开出去",
                        "① 在地图上左键点那支舰队 = 选中它(名板/图标都能点中)。\n" +
                        "② 选中后左键点海面 = 开过去; 左键点己方沿海城镇 = 开往该港; 左键点敌国沿海城镇 = 开往并封锁。\n" +
                        "③ 右键选中的舰队 = 舰队菜单, 五项:\n" +
                        "   [回母港] · [指定母港…] · [巡航母港海域] · [封锁敌港…](500 第纳尔/次) · [解散舰队]。\n" +
                        "④ 面板上也有同一批入口: [舰队]页 -> [回港] / [解散]; 底部 [全部召回] / [全部交还] / [全部进驻母港]。",
                        "舰队与国防军是两套命令链: 它只走海军入口(不会被陆军那套拖上岸), 在海上按原版航海路径推进。\n" +
                        "没有命令时它就在母港海面待命; 下过命令的舰队归你指挥, 海军部不会自己把它调走。\n" +
                        "[交还](底部[全部交还])后才由原版 AI 自主巡弋 —— 想再拿回来, 下一条命令就够。\n" +
                        "常见坑: 忘了给母港。母港未知/被转让后 [回港] 会提示你右键选[指定母港…]重新指定, 否则它无处可回。",
                        "左键点舰队选中 -> 左键点地图上的目标(海面/己方港/敌港); 右键 = 舰队菜单。", "g_navy_fleet",
                        delegate { return NavySailed(); }));

                    l.Add(new GuideGoal("g_navy_moor", "把舰队进驻沿海城市",
                        "① 地图上左键选中一支舰队(或 [海军]页 -> [舰队] 用 ◀ ▶ 选中它)。\n" +
                        "② 点[进驻城市], 然后在地图上左键点一座你自己的沿海城镇; 再点一次同一按钮 / 右键 / Esc = 取消选点。\n" +
                        "③ 到位后该舰队行内会显示「驻泊于「城市名」」; 城市详情页也能看到驻泊的舰队。\n" +
                        "④ 解除: 点[离港](母港保留) —— 或者直接把它开走(任何移动/巡航命令都会自动解除驻泊)。",
                        "泊位是纯收益: 进驻到位后船维护 -35%, 而且每天按 DLC 的修船价从国库付费把船修回满耐久。\n" +
                        "所以打完一场硬仗之后, 正确的做法不是把破船扔在海上, 而是开回自有港口进驻几天。\n" +
                        "注意进驻会把该城同时设为母港(最直白的语义), 而且只能进驻自己的沿海城市: 别人的港口会被拒绝并说明原因。\n" +
                        "常见坑: 把舰队驻在离战区很远的港口图省钱, 战时来不及赶到封锁海域。",
                        "[海军] -> [舰队] -> [进驻城市] -> 地图上点自有沿海城镇。", "g_navy_sail",
                        delegate { return NavyMoored(); }));

                    l.Add(new GuideGoal("g_navy_transport", "用海运把陆军送过海",
                        "① 先有一支野战军团([军务]页 -> [守备营] -> [成立军团]), 而且它此刻不在交战、不在海上。\n" +
                        "② [海军]页 -> 页签[海运], 控制条上依次选三步:\n" +
                        "   (1) 军团(用 ◀ ▶ 切换) (2) 出发港(点[选城]在地图上点, 或用 ◀ ▶) (3) 目的港(点[选城]或 ◀ ▶)。\n" +
                        "③ 点底部[装船出发]。军团会从出发港出海, 目的港自动下船, 船按原主还回去。\n" +
                        "④ 途中要改主意就点该行行内键[召回](在途上限 3 支运输队)。",
                        "海运让陆军绕过海峡与群山, 也能在你没有陆路通道时投送兵力 —— 港口对港口的直线海路。\n" +
                        "门槛与代价: 氏族手里要有没编进舰队的船(海运按载员挑大船, 所以至少要留 1~2 艘);\n" +
                        "军团与出发港的距离要在 20 以内; 补给费 = 2 第纳尔/人 + 20/船, 从国库扣。\n" +
                        "出发港必须自有, 目的港可以是本国或中立(交战国港口不能当目的港 —— 那是登陆战, 是另一套系统)。\n" +
                        "风险: 海上有损耗(船耐久会被风浪吃掉), 船撑不住会提前靠港; 运输途中的军团不接受[交还军事总监]。\n" +
                        "常见坑: 把军团留在内陆就点[装船出发], 或者把船全编进了舰队 —— 两者都会被拒绝, 面板会写明原因。",
                        "[海军] -> [海运] -> ①军团 ②出发港 ③目的港 -> 底部[装船出发]。", "g_navy_moor",
                        delegate { return NavyConvoySeen(); }));

                    l.Add(new GuideGoal("g_navy_battle", "打第一场海战并看战报",
                        "① [海军]页 -> 页签[封锁], 用 ◀ ▶ 或[选择城市]在地图上点一座敌国沿海城镇(必须先与它交战)。\n" +
                        "② 点[出击 · 舰队交战](控制条右侧那枚, 或底部同一枚大按钮)。\n" +
                        "③ 结算完面板自动跳到页签[战报], 最近 12 场都在那里(也可点行内键[封锁]/[解除]先做常规封锁)。\n" +
                        "④ 地图上真打的海战(DLC 自己的遭遇战)也会自动记进同一份[战报], 标为【实战】。",
                        "门槛: 我方至少 1 艘船 · 与目标国处于交战状态 · 目标必须是沿海城镇。没有敌国港口时按钮会告诉你原因。\n" +
                        "有 6 游戏小时冷却, 并按参战舰数收补给费(100/艘) —— 反复点[出击]刷战利品这条路是堵死的。\n" +
                        "结算口径 = 双方舰队战力(舰船数/舰种/完整度/海军科技)× 提督统率(你的战术与统御 + DLC 海军技能),\n" +
                        "所以「船多」不如「船好 + 提督强」: 打之前先看[舰队]页的构成条与行内战力。\n" +
                        "常见坑: 拿一队轻型船去撞重型舰队。先把船整备(舰队页 [整备 费用])、进驻港口修满再出击。",
                        "[海军] -> [封锁] -> 选敌国沿海城镇 -> [出击 · 舰队交战] -> 自动跳[战报]。", "g_navy_transport",
                        delegate { return NavyBattleReported(); }));
                }

                _goals = l;
                return _goals;
            }
        }
    }
}
