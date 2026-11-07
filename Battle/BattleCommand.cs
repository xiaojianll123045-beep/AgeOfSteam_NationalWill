using System;
using System.Collections.Generic;
using HarmonyLib;
using SandBox.View.Map;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace FeudalInternalAffairs
{
    // 亲自指挥地图战斗(阶段1: 加入并进入真正的战场)
    //   地图上本国参战的战斗 -> 右键战场标记 -> 弹窗询问 -> 确认后玩家部队并入该战斗的一侧,
    //   再走原版遭遇流程(PlayerEncounter)进入战场任务。
    //   战场结束后把玩家部队送回原处并重新冻结隐藏。
    internal static class BattleCommand
    {
        // 正在"亲自指挥"一场战斗(此时不冻结玩家部队)
        internal static bool InCommandBattle;

        private static MapEvent _pending;
        private static MapEvent _commandingBattle;   // 正在指挥的那场战斗(只认它结束)
        private static bool _savedValid;
        private static CampaignVec2 _savedPos;
        private static TroopRoster _savedRoster;     // 参战期间暂存的玩家部队兵员
        // v4.255(P0-6): 上次存档时未归位的暂存兵员(存档字符串), 读档后补放回
        private static string _pendingRestore;

        // 找光标附近的战斗(只认本国参战、未结束的)
        internal static MapEvent FindNearCursor(float maxPixels)
        {
            try
            {
                var kingdom = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                var mgr = Campaign.Current != null ? Campaign.Current.MapEventManager : null;
                if (kingdom == null || mgr == null) { DLog.Force("战斗诊断: 没有国家或没有战斗管理器"); return null; }
                var cam = TerritoryColorMode.GetCamera();
                if (cam == null) { DLog.Force("战斗诊断: 拿不到相机"); return null; }
                var mouse = TaleWorlds.InputSystem.Input.MousePositionPixel;

                // 光标下的地面点(世界坐标): 用它再做一次"世界距离"判定, 双保险
                Vec3 ground = Vec3.Zero;
                bool haveGround = false;
                try
                {
                    var screen = MapScreen.Instance;
                    var sv = screen != null && screen.SceneLayer != null ? screen.SceneLayer.SceneView : null;
                    if (sv != null)
                    {
                        Vec3 gn;
                        haveGround = sv.ProjectedMousePositionOnGround(out ground, out gn, false,
                            (TaleWorlds.Engine.BodyFlags)544323529, false);
                    }
                }
                catch { }

                MapEvent best = null;
                float bestD = float.MaxValue;
                int count = 0;
                foreach (var me in mgr.MapEvents)
                {
                    if (me == null) continue;
                    count++;
                    bool invol = InvolvesKingdom(me, kingdom);
                    bool proj = TryProject(me, cam, out float sx, out float sy);
                    float sd = proj
                        ? (float)Math.Sqrt((mouse.X - sx) * (mouse.X - sx) + (mouse.Y - sy) * (mouse.Y - sy))
                        : -1f;
                    float wd = -1f;
                    if (haveGround)
                    {
                        var p = me.Position;
                        wd = (float)Math.Sqrt((ground.x - p.X) * (ground.x - p.X) + (ground.y - p.Y) * (ground.y - p.Y));
                    }
                    DLog.Force("战斗诊断: 结束=" + me.IsFinalized + " 含本国=" + invol
                        + " 屏幕距=" + (proj ? sd.ToString("F0") : "投影失败")
                        + " 世界距=" + (haveGround ? wd.ToString("F0") : "无地面点"));

                    if (!invol || me.IsFinalized) continue;
                    // 屏幕距离或世界距离任一在范围内都算点到了
                    float d = float.MaxValue;
                    if (proj && sd >= 0f) d = sd;
                    if (wd >= 0f && wd * 8f < d) d = wd * 8f;   // 世界单位按 ~8 像素/单位折算
                    if (d < bestD) { bestD = d; best = me; }
                }
                if (count == 0) DLog.Force("战斗诊断: 地图上目前没有战斗");
                if (best == null) return null;
                if (bestD > maxPixels) { DLog.Force("战斗诊断: 最近的战斗距离 " + bestD.ToString("F0") + " 像素, 超出阈值"); return null; }
                return best;
            }
            catch (Exception ex) { DLog.Force("战斗诊断异常: " + ex.Message); return null; }
        }

        private static bool TryProject(MapEvent me, TaleWorlds.Engine.Camera cam, out float sx, out float sy)
        {
            sx = 0f; sy = 0f;
            try
            {
                var pos = me.Position;
                float h = 0f;
                Vec3 n = Vec3.Up;
                try
                {
                    var scene = MapScreen.Instance != null ? MapScreen.Instance.MapScene : null;
                    if (scene != null) scene.GetTerrainHeightAndNormal(new Vec2(pos.X, pos.Y), out h, out n);
                }
                catch { }
                if (h < 0.05f) h = 20f;
                float w = 0f;
                TaleWorlds.MountAndBlade.MBWindowManager.WorldToScreenInsideUsableArea(
                    cam, new Vec3(pos.X, pos.Y, h, 1f), ref sx, ref sy, ref w);
                return w > 0f;
            }
            catch { return false; }
        }

        // 这场战斗是否有本国一方参与
        internal static bool InvolvesKingdom(MapEvent me, Kingdom kingdom)
        {
            try
            {
                if (me == null || kingdom == null) return false;
                return SideHasKingdom(me.AttackerSide, kingdom) || SideHasKingdom(me.DefenderSide, kingdom);
            }
            catch { return false; }
        }

        private static bool SideHasKingdom(MapEventSide side, Kingdom kingdom)
        {
            try
            {
                if (side == null) return false;
                if (side.MapFaction == kingdom) return true;
                foreach (var mep in side.Parties)
                {
                    var p = mep != null ? mep.Party : null;
                    if (p == null) continue;
                    if (p.MapFaction == kingdom) return true;
                    var hero = p.LeaderHero;
                    if (hero != null && hero.Clan != null && hero.Clan.Kingdom == kingdom) return true;
                }
            }
            catch { }
            return false;
        }

        // 本国是攻方还是守方
        internal static BattleSideEnum OurSide(MapEvent me, Kingdom kingdom)
        {
            try
            {
                if (SideHasKingdom(me.AttackerSide, kingdom)) return BattleSideEnum.Attacker;
            }
            catch { }
            return BattleSideEnum.Defender;
        }

        // 右键战场 -> 询问
        internal static void ShowConfirm(MapEvent me)
        {
            try
            {
                _pending = me;
                var kingdom = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                string desc = Describe(me, kingdom);
                var options = new List<InquiryElement>
                {
                    new InquiryElement("cmd", "亲自指挥（进入战场）", null, true, "以统帅身份进入战场，亲自指挥"),
                    new InquiryElement("no", "交给将领们", null, true, "照常交给 AI 打完")
                };
                PanelInputGuard.ShowPopup(new MultiSelectionInquiryData(
                    "发现战斗", desc, options, true, 1, 1, "确定", "取消",
                    OnPicked, null, null, false), false);
            }
            catch (Exception ex) { DLog.Force("战斗询问异常: " + ex.Message); }
        }

        private static string Describe(MapEvent me, Kingdom kingdom)
        {
            try
            {
                string us = SideText(me.AttackerSide, kingdom) + "  VS  " + SideText(me.DefenderSide, kingdom);
                string type = me.IsSiegeAssault ? "攻城" : (me.IsSallyOut ? "突围" : (me.IsRaid ? "劫掠" : "野战"));
                return type + "：" + us + "\n是否亲自指挥这场战斗？";
            }
            catch { return "是否亲自指挥这场战斗？"; }
        }

        private static string SideText(MapEventSide side, Kingdom kingdom)
        {
            try
            {
                if (side == null) return "?";
                var leader = side.LeaderParty;
                string name = leader != null && leader.Name != null ? leader.Name.ToString() : "?";
                bool ours = SideHasKingdom(side, kingdom);
                int men = side.TroopCount;
                return (ours ? "【我方】" : "") + name + "(" + men + "人)";
            }
            catch { return "?"; }
        }

        private static void OnPicked(List<InquiryElement> selected)
        {
            try
            {
                if (selected == null || selected.Count == 0) return;
                if ((selected[0].Identifier as string) != "cmd") return;
                TakeCommand(_pending);
            }
            catch (Exception ex) { DLog.Force("战斗询问选择异常: " + ex.Message); }
            finally { _pending = null; }
        }

        // 加入战斗并进入战场
        // 严格按原版顺序: 到场 -> SetupFields -> JoinBattle(设 PlayerSide/OpponentSide)
        //                 -> StartAttackMission(真正打开战场任务)
        internal static void TakeCommand(MapEvent me)
        {
            try
            {
                var kingdom = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                var main = MobileParty.MainParty;
                if (me == null || kingdom == null || main == null) return;
                if (me.IsFinalized) { MapSelection.Message("战斗已经结束了"); return; }

                // v4.255(P0-6): 去重 —— 指挥中再次右键确认会用"只剩英雄"的名册覆盖 _savedRoster,
                //   第一次暂存的兵就永久丢了
                if (InCommandBattle)
                {
                    bool stale = false;
                    try { stale = _commandingBattle == null || _commandingBattle.IsFinalized; } catch { stale = true; }
                    if (!stale)
                    {
                        MapSelection.Message("已在亲自指挥中，无需重复进入");
                        DLog.Force("亲自指挥: 已在指挥中, 忽略重复进入请求");
                        return;
                    }
                    DLog.Force("亲自指挥: 上一场指挥状态已失效, 先归位再重新进入");
                    RestoreParty();
                }

                var attacker = me.AttackerSide != null ? me.AttackerSide.LeaderParty : null;
                var defender = me.DefenderSide != null ? me.DefenderSide.LeaderParty : null;
                if (attacker == null || defender == null)
                {
                    MapSelection.Message("战斗数据不完整，无法加入");
                    return;
                }

                var side = OurSide(me, kingdom);
                _savedPos = main.Position;
                _savedValid = true;
                InCommandBattle = true;
                _commandingBattle = me;
                try { BattleSim.MarkCommanded(me); } catch { }   // v4.255(P0-7): 告诉 BattleSim 别再结算这场

                // 1) 到场: 挪到战场, 并解除"忽略"和隐藏
                //    (国家意志模式下玩家部队平时被 IgnoreForHours 忽略, 不解开的话遭遇系统根本不认它)
                try { main.Position = me.Position; } catch { }
                try { main.IgnoreForHours(0f); } catch { }
                try { main.IsVisible = true; } catch { }

                // 国家意志没有私人部队: 参战期间把玩家部队的普通兵暂时移出(只留英雄), 结束后恢复
                //   v4.255(P0-6): 只暂存普通兵 —— 原来整份名册(含英雄)都存, 归位时会把英雄再加一遍
                //   并且带伤兵数量, 归位后伤员不丢
                try
                {
                    var roster = main.MemberRoster;
                    if (roster != null)
                    {
                        _savedRoster = TroopRoster.CreateDummyTroopRoster();
                        foreach (var e in roster.GetTroopRoster())
                        {
                            if (e.Character == null || e.Character.IsHero || e.Number <= 0) continue;
                            _savedRoster.AddToCounts(e.Character, e.Number, false, e.WoundedNumber, 0, false, -1);
                        }
                        roster.RemoveIf(e => !e.Character.IsHero);
                        DLog.Force("亲自指挥: 已暂时移出玩家部队的普通兵(" + _savedRoster.TotalManCount + "人)");
                    }
                }
                catch (Exception ex) { DLog.Force("参战前移出玩家部队失败: " + ex.Message); }

                // 2) 原版加入战斗
                main.Party.MapEventSide = me.GetMapEventSide(side);
                PlayerEncounter.Start();
                var enc = PlayerEncounter.Current;
                if (enc != null) enc.SetupFields(attacker, defender);
                PlayerEncounter.JoinBattle(side);

                // 3) 打开战场任务。
                //    原版真正开战场的是菜单项"攻击"的处理函数 MenuHelper.EncounterAttackConsequence,
                //    它内部按战斗类型选择场景并 OpenBattleMission。这里反射调用它(传一个自造 args);
                //    万一失败, 玩家仍会看到原版遭遇菜单, 点"攻击"即可进入。
                PlayerEncounter.StartAttackMission();
                if (!TryOpenBattleMission())
                    MapSelection.Message("已加入战斗，请在出现的菜单里点『攻击』进入战场");

                DLog.Force("亲自指挥: 已加入战斗(" + side + ") " + Describe(me, kingdom));
                MapSelection.Message("已进入战场：" + Describe(me, kingdom));
                BattleRtsCamera.Activate();   // 进入 RTS 上帝视角
            }
            catch (Exception ex)
            {
                DLog.Force("亲自指挥失败: " + ex.Message);
                RestoreParty();
            }
        }

        // 收尾: 玩家部队归位 + 重新冻结隐藏 + 关闭 RTS 相机
        private static void RestoreParty()
        {
            try
            {
                var main = MobileParty.MainParty;
                if (main != null && _savedValid)
                {
                    try { main.Position = _savedPos; } catch { }
                    try { main.IsVisible = false; } catch { }
                }
            }
            catch { }
                _savedValid = false;
                _commandingBattle = null;
                InCommandBattle = false;
                // v4.255(P0-7): 这里**不**解除 BattleSim 的"亲自指挥"打标 —— MapEventEnded 的监听顺序上
                //   本行为(NationalWillBehavior, 开局注册) 早于 BattleSim 的归档钩子(战斗中懒注册),
                //   过早解除会让 Archive 错过跳过判定。打标由 BattleSim.Archive 消费后自行清除。
                BattleRtsCamera.Reset();
                _postBattleLeaveTries = 10;   // 战后把玩家部队从战斗记录里摘掉
            // 把暂时移出的兵还回去
            //   v4.255(P0-6): 只有真的放回去了才清空暂存名单 —— 原来 MobileParty.MainParty == null
            //   时直接把 _savedRoster 置 null, 兵就永久消失了
            if (_savedRoster != null)
            {
                bool back = false;
                try
                {
                    var main = MobileParty.MainParty;
                    if (main != null && main.MemberRoster != null)
                    {
                        main.MemberRoster.Add(_savedRoster);
                        back = true;
                    }
                }
                catch { }
                if (back)
                {
                    DLog.Force("亲自指挥: 暂存兵员已归位 " + _savedRoster.TotalManCount + " 人");
                    _savedRoster = null;
                    _pendingRestore = null;
                }
                else
                {
                    // 放回失败: 保留名单(存档时落盘), 读档后由 OnGameLoadedRestore 补放回
                    _pendingRestore = SerializeRoster(_savedRoster);
                    DLog.Force("亲自指挥: 玩家部队暂不可用, 暂存兵员转为待归位(" + (_pendingRestore != null ? _pendingRestore.Length : 0) + " 字节)");
                }
            }
            try { NationalWillParty.Freeze(true); } catch { }
        }

        // ==================== v4.255(P0-6): 暂存兵员的存档接线 ====================
        //   战斗中存档/退出/崩溃时, 名册已经删进存档而静态暂存名单随进程消失 -> 兵永久丢失。
        //   这里把暂存名单写成字符串交给 SoldiersBehavior 落盘, 读档后补放回。
        private static string SerializeRoster(TroopRoster r)
        {
            try
            {
                if (r == null) return "";
                var sb = new System.Text.StringBuilder();
                foreach (var e in r.GetTroopRoster())
                {
                    if (e.Character == null || e.Number <= 0) continue;
                    string tid = e.Character.StringId;
                    if (string.IsNullOrEmpty(tid)) continue;
                    sb.Append(tid).Append(':').Append(e.Number).Append(':').Append(e.WoundedNumber).Append(';');
                }
                return sb.ToString();
            }
            catch { return ""; }
        }

        // 存档时调用: 返回"未归位的暂存兵员"
        internal static string SavePendingRoster()
        {
            try
            {
                if (_savedRoster != null) return SerializeRoster(_savedRoster);
                return _pendingRestore != null ? _pendingRestore : "";
            }
            catch { return ""; }
        }

        // 读档时调用: 记下待归位名单(真正放回要等游戏加载完, 见 OnGameLoadedRestore)
        internal static void LoadPendingRoster(string data)
        {
            try
            {
                _savedRoster = null;
                InCommandBattle = false;
                _commandingBattle = null;
                _savedValid = false;
                try { BattleSim.ClearCommanded(null); } catch { }
                _pendingRestore = string.IsNullOrEmpty(data) ? null : data;
                if (!string.IsNullOrEmpty(_pendingRestore))
                    DLog.Force("亲自指挥: 读档发现未归位的暂存兵员, 稍后补放回");
            }
            catch { }
        }

        // 游戏加载完成: 把上次没来得及放回的兵还给玩家部队
        internal static void OnGameLoadedRestore()
        {
            try
            {
                InCommandBattle = false;
                _commandingBattle = null;
                _savedValid = false;
                try { BattleSim.ClearCommanded(null); } catch { }
                TryFinishPendingRestore();
            }
            catch (Exception ex) { DLog.Force("亲自指挥读档归位异常: " + ex.Message); }
        }

        // 真正执行"补放回"; 成功返回 true。读档时 MobileParty.MainParty 可能还没就绪,
        //   所以 OnGameLoaded 之外还由 TickPostBattleLeave 每帧兜底重试一次。
        private static bool TryFinishPendingRestore()
        {
            try
            {
                if (string.IsNullOrEmpty(_pendingRestore)) return true;
                var main = MobileParty.MainParty;
                if (main == null || main.MemberRoster == null) return false;   // 还没就绪 -> 留着下次机会
                int back = 0;
                foreach (var seg in _pendingRestore.Split(';'))
                {
                    if (string.IsNullOrEmpty(seg)) continue;
                    var f = seg.Split(':');
                    if (f.Length < 2) continue;
                    int n, w = 0;
                    if (!int.TryParse(f[1], out n) || n <= 0) continue;
                    if (f.Length > 2) int.TryParse(f[2], out w);
                    CharacterObject c = null;
                    try { c = Game.Current != null && Game.Current.ObjectManager != null ? Game.Current.ObjectManager.GetObject<CharacterObject>(f[0]) : null; } catch { c = null; }
                    if (c == null) continue;
                    try { main.MemberRoster.AddToCounts(c, n, false, w, 0, false, -1); back += n; } catch { }
                }
                _pendingRestore = null;
                if (back > 0)
                {
                    DLog.Force("亲自指挥: 读档补放回暂存兵员 " + back + " 人");
                    MapSelection.Message("亲自指挥中断的部队已归位: " + back + " 人");
                }
                return true;
            }
            catch (Exception ex) { DLog.Force("亲自指挥补归位异常: " + ex.Message); return false; }
        }

        // 反射调用原版"攻击"处理函数, 真正打开战场任务
        private static bool TryOpenBattleMission()
        {
            try
            {
                var helperType = AccessTools.TypeByName("Helpers.MenuHelper");
                if (helperType == null) { DLog.Force("开战场: 找不到 Helpers.MenuHelper"); return false; }
                var mi = AccessTools.Method(helperType, "EncounterAttackConsequence");
                if (mi == null) { DLog.Force("开战场: 找不到 EncounterAttackConsequence"); return false; }

                var argsType = AccessTools.TypeByName("TaleWorlds.CampaignSystem.GameMenus.MenuCallbackArgs");
                object args = null;
                if (argsType != null)
                {
                    var mapState = TaleWorlds.Core.GameStateManager.Current != null
                        ? TaleWorlds.Core.GameStateManager.Current.ActiveState
                        : null;
                    var ctor = AccessTools.Constructor(argsType, new[] { mapState != null ? mapState.GetType() : typeof(object), typeof(TaleWorlds.Localization.TextObject) })
                               ?? AccessTools.Constructor(argsType, new[] { typeof(TaleWorlds.CampaignSystem.GameState.MapState), typeof(TaleWorlds.Localization.TextObject) });
                    if (ctor != null)
                        args = ctor.Invoke(new object[] { mapState as TaleWorlds.CampaignSystem.GameState.MapState, new TaleWorlds.Localization.TextObject("") });
                }
                if (args == null) { DLog.Force("开战场: 构造 MenuCallbackArgs 失败"); return false; }

                mi.Invoke(null, new[] { args });
                DLog.Force("开战场: 已调用原版攻击流程");
                return true;
            }
            catch (Exception ex)
            {
                DLog.Force("开战场失败: " + (ex.InnerException != null ? ex.InnerException.Message : ex.Message));
                return false;
            }
        }

        // 战后处理: 不拦截结算页面(玩家想留就留), 只把玩家自己的部队从这场战斗里摘掉,
        // 这样结算页/战斗总结里不会出现"玩家的部队"。
        private static int _postBattleLeaveTries;

        internal static void TickPostBattleLeave(float dt)
        {
            // v4.255(P0-6): 读档补放回的兜底重试(OnGameLoaded 时 MainParty 可能还没就绪)
            if (!string.IsNullOrEmpty(_pendingRestore)) { try { TryFinishPendingRestore(); } catch { } }
            if (_postBattleLeaveTries <= 0) return;
            _postBattleLeaveTries--;
            try
            {
                var main = MobileParty.MainParty;
                if (main == null || main.Party == null) { _postBattleLeaveTries = 0; return; }
                // 把玩家部队从战斗一方里摘掉(战后安全, 只影响显示)
                if (main.Party.MapEventSide != null)
                {
                    main.Party.MapEventSide = null;
                    DLog.Force("战后: 已把玩家部队从战斗记录中摘除(不再出现在结算页面)");
                }
                else
                {
                    _postBattleLeaveTries = 0;
                }
            }
            catch (Exception ex)
            {
                DLog.Force("战后摘除玩家部队失败: " + ex.Message);
                _postBattleLeaveTries = 0;
            }
        }

        // 战斗结束: 玩家部队回原位并重新冻结隐藏
        internal static void OnBattleEnded(MapEvent me)
        {
            try
            {
                if (!InCommandBattle) return;
                // 只认正在指挥的这场(否则别的战斗结束会把状态误清)
                if (me != null && _commandingBattle != null && !ReferenceEquals(me, _commandingBattle)) return;
                RestoreParty();
                DLog.Force("亲自指挥: 战斗结束, 玩家部队已归位");
            }
            catch (Exception ex) { DLog.Force("战斗收尾异常: " + ex.Message); }
        }
    }
}
