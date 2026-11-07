using System;
using System.Collections.Generic;
using System.Text;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace FeudalInternalAffairs
{
    // ==================== v4.277(第 2 项): 地图上**真实发生**的海战 -> 海军战报 ====================
    //
    // 【要解决的问题】海战战果原来有两套并行、各有入口、账不统一:
    //   · 地图上真实打的(原版 MapEvent 海上遭遇, 玩家在 Mission 里真打/坐镇) -> 只有**原版战报**;
    //   · 面板[出击](Navy\NavalBattle.cs 的抽象结算) -> 进 NavalFleet.Battles + BattleSim.Recent。
    //   于是玩家在海军页看不到自己舰队实际打过的仗 —— 而"我的舰队打过什么仗"恰恰是那个页面该回答的。
    //
    // 【为什么选"单向补充"而不是"合并成一套账"】
    //   原版那套战报属于原版 UI/结算(奖励、俘虏、影响力、领主关系都在原版那侧落地),
    //   若把它接管过来"合并", 就要在原版结算之上再算一遍战果 -> 必然重复记账(战利品/厌战/沉船)。
    //   所以本文件**只往 NavalFleet.Battles(与 BattleSim.Recent)里追加一条**,
    //   一个字节都不改原版的战果结算(NavalBattle 那条推演路径也保持原样)。
    //   => 取舍: 面板"战报"页从此是"我海军打过/推演过的全部海战"的**只读汇总**,
    //      而"真实战果到底算了什么"仍以原版战报为准(本文件的战报只是它的摘要+索引)。
    //
    // 【"是不是海战"怎么判 —— 反编译实证, 不猜】
    //   · `MapEvent.IsNavalMapEvent`(TaleWorlds.CampaignSystem.MapEvents.MapEvent.cs:247)的**实现**就是
    //       `=> !Position.IsOnLand`   —— 与任务书里的提示一致(已核对, 见下"证据"一节的字段读取)。
    //     本文件仍**双判**: `IsNavalMapEvent` 为真 **或** 双方任一方参战部队手里有船
    //     (MapEventParty.Ships = Party.Ships, MapEventParty.cs:131)。理由: 若将来某个版本把
    //     IsNavalMapEvent 改成别的口径(例如只看 BattleTypes), 有船这条仍然能把海战认出来。
    //   · `MapEvent.Position` 是 `CampaignVec2`(MapEvent.cs:194), `IsOnLand` 是它的 public 字段
    //     (CampaignVec2.cs:14)。港口海面点在反序列化时是 `new CampaignVec2(pos, isOnLand: false)`,
    //     所以港口海域的战斗天然 IsOnLand == false。
    //   · 胜负: `MapEvent.WinningSide`(MapEvent.cs:307)读 `BattleState`
    //     (AttackerVictory -> Attacker / DefenderVictory -> Defender / 否则 None)。
    //   · 伤亡: `MapEventSide.TroopCasualties`(MapEventSide.cs:55) / `.ShipCasualties`(:61)。
    //   · 回调时机: `CampaignEvents.MapEventEnded` 由 `MapEvent.FinalizeEventAux()` 发出
    //     (MapEvent.cs:2079), 发出前已经 `State = MapEventState.WaitingRemoval`(即 IsFinalized == true)
    //     并跑完 `ControlAndUpdateDefeatedPartiesAfterBattle()`, 而且 FinalizeEventAux 开头有
    //     `if (IsFinalized) return;` -> **同一场战斗只会发一次**。这里再加一道"内容指纹"去重做保险。
    //
    // 【软依赖】只碰 TaleWorlds.*(MapEvent / CampaignEvents / PartyBase 都是基础游戏类型),
    //   一个 NavalDLC.* 类型都不引用 —— DLC 缺失时本文件订阅后永远收不到海战事件, 静默无害。
    internal static class NavalMapBattle
    {
        private const int DedupMax = 8;
        private static readonly List<string> _recent = new List<string>();

        // 订阅(CampaignEvents 的 MapEventEnded)。由 NavalBehavior.RegisterEvents 调用。
        internal static void Register(CampaignBehaviorBase owner)
        {
            try
            {
                CampaignEvents.MapEventEnded.AddNonSerializedListener(owner, OnMapEventEnded);
                DLog.Force("海军: 已订阅原版地图事件结束回调(真实海战 -> 海军战报)");
            }
            catch (Exception ex) { DLog.Force("海军: 真实海战订阅失败(只影响海军战报) " + ex.Message); }
        }

        // ==================== 回调 ====================
        private static void OnMapEventEnded(MapEvent e)
        {
            try
            {
                if (e == null) return;
                if (!NavalBridge.Usable) return;                  // 没装海战 DLC -> 海战无从谈起
                if (!IsNaval(e)) return;                          // 不是海战

                int mySide = MySide(e);
                if (mySide == 0) return;                          // 我方没参战 -> 不进我方海军战报

                int enemySide = -mySide;
                string sig = Signature(e, mySide);
                if (IsDuplicate(sig)) return;
                Remember(sig);

                string report = BuildReport(e, mySide, enemySide);
                NavalFleet.AddBattle(report);

                // 与 NavalBattle 的推演战报同一处置: **同一条字符串**也插进 BattleSim 的统一"近期战报"
                //   (字段形状与它自己的战报一致: 单条 = 多行字符串, 首行是标题; 内容里不能出现 (char)1/2)。
                try
                {
                    string forRecent = report.Replace((char)1, ' ').Replace((char)2, ' ');
                    BattleSim.Recent.Insert(0, forRecent);
                    while (BattleSim.Recent.Count > 8) BattleSim.Recent.RemoveAt(BattleSim.Recent.Count - 1);
                }
                catch (Exception exIn) { DLog.Force("海军: 实战海战战报并入 BattleSim 失败(不影响战报本身) " + exIn.Message); }

                DLog.Force("海军: [实战] 海战记录入账(含你的舰队=" + (MyFleetInvolved(e, mySide) ? "是" : "否")
                    + ") —— " + HeadlineOf(e, mySide, enemySide));
            }
            catch (Exception ex) { DLog.Force("海军: 真实海战记账异常 " + ex.Message); }
        }

        // ==================== 判定 ====================
        // 海战判据(双判, 见文件头注释): 原版口径 IsNavalMapEvent 或"参战部队手里有船"
        private static bool IsNaval(MapEvent e)
        {
            try
            {
                if (e.IsNavalMapEvent) return true;
                if (!e.Position.IsOnLand) return true;    // 与 IsNavalMapEvent 同一判据, 显式写出便于将来对账
                return SideHasShips(e.AttackerSide) || SideHasShips(e.DefenderSide);
            }
            catch { return false; }
        }

        private static bool SideHasShips(MapEventSide s)
        {
            try
            {
                if (s == null) return false;
                var parties = s.Parties;
                if (parties == null) return false;
                for (int i = 0; i < parties.Count; i++)
                {
                    var mp = parties[i];
                    if (mp == null || mp.Party == null) continue;
                    var ships = mp.Party.Ships;
                    if (ships != null && ships.Count > 0) return true;
                }
            }
            catch { }
            return false;
        }

        // 我方在哪一侧: 1 = 进攻方 / -1 = 防守方 / 0 = 我方没参战
        //   判据 = 该侧有部队的 MapFaction == 玩家王国, **或**该侧有玩家氏族的部队
        //   (含玩家主队与 fia_navy_ 舰队 / fia_def_ 军团 / fia_amg_ 分裂队 —— 它们都挂 PlayerClan)。
        private static int MySide(MapEvent e)
        {
            bool mineAtk = SideIsMine(e.AttackerSide);
            bool mineDef = SideIsMine(e.DefenderSide);
            if (mineAtk && !mineDef) return 1;
            if (mineDef && !mineAtk) return -1;
            if (mineAtk && mineDef) return 1;   // 两侧都是我方(极罕见: 内战/误伤) -> 按进攻方记, 不静默丢弃
            return 0;
        }

        private static bool SideIsMine(MapEventSide s)
        {
            try
            {
                if (s == null) return false;
                var k = NavalTaskForce.PlayerKingdom();
                try { if (k != null && ReferenceEquals(s.MapFaction, k)) return true; } catch { }
                var parties = s.Parties;
                if (parties == null) return false;
                for (int i = 0; i < parties.Count; i++)
                {
                    var mp = parties[i];
                    if (mp == null || mp.Party == null) continue;
                    // 判据一: 部队归属氏族(PartyBase.Owner = _customOwner ?? MobileParty.Owner,
                    //   见 PartyBase.cs:189-204; PartyBase 上没有 OwnerClan 这个成员, 必须走 Owner.Clan)
                    try
                    {
                        var owner = mp.Party.Owner;
                        if (owner != null && owner.Clan == Clan.PlayerClan) return true;
                    }
                    catch { }
                    // 判据二: 部队的 ActualClan(舰队/军团/分裂队都被 FIA 设成 Clan.PlayerClan)
                    try
                    {
                        var mob = mp.Party.MobileParty;
                        if (mob != null && mob.ActualClan == Clan.PlayerClan) return true;
                    }
                    catch { }
                }
            }
            catch { }
            return false;
        }

        // ==================== 去重(保险) ====================
        //   同一场战斗本该只发一次(见文件头: FinalizeEventAux 的 IsFinalized 早退),
        //   但若将来原版改了时机/别的 mod 重复触发, 这条"内容指纹"能挡住重复入账。
        private static string Signature(MapEvent e, int mySide)
        {
            try
            {
                int myTroop = 0, enTroop = 0, myShip = 0, enShip = 0;
                var mine = mySide == 1 ? e.AttackerSide : e.DefenderSide;
                var enemy = mySide == 1 ? e.DefenderSide : e.AttackerSide;
                try { myTroop = mine.TroopCasualties; myShip = mine.ShipCasualties; } catch { }
                try { enTroop = enemy.TroopCasualties; enShip = enemy.ShipCasualties; } catch { }
                return NavalTaskForce.SafeToday() + "/" + (int)e.Position.X + "/" + (int)e.Position.Y
                    + "/" + myTroop + "/" + enTroop + "/" + myShip + "/" + enShip;
            }
            catch { return ""; }
        }

        private static bool IsDuplicate(string sig)
        {
            try
            {
                if (string.IsNullOrEmpty(sig)) return false;
                return _recent.Contains(sig);
            }
            catch { return false; }
        }

        private static void Remember(string sig)
        {
            try
            {
                if (string.IsNullOrEmpty(sig)) return;
                _recent.Add(sig);
                while (_recent.Count > DedupMax) _recent.RemoveAt(0);
            }
            catch { }
        }

        // v4.277: 新战役/新存档时清空(由 NavalFleet.Reset 调用, 与 Battles 一起清)
        internal static void Reset()
        {
            try { _recent.Clear(); } catch { }
        }

        // ==================== 战报文本 ====================
        //   格式与推演战报**逐字对齐**(同一天数口径 + 同样的字段行), 只在来源标签上区分:
        //     推演: "第 N 天 · 【推演】海战胜利 · …"   (NavalBattle.cs)
        //     实战: "第 N 天 · 【实战】海战胜利 · …"   (本文件)
        //   首行是标题(NavyPanelVM.BuildLogRows / BattleReportVM 都拿首行当标题)。
        private static string BuildReport(MapEvent e, int mySide, int enemySide)
        {
            try
            {
                var sb = new StringBuilder();
                string head = HeadlineOf(e, mySide, enemySide);
                sb.Append("第 ").Append(NavalTaskForce.SafeToday()).Append(" 天 · ")
                  .Append("【").Append(NavalTaskForce.SourceReal).Append("】")
                  .Append(head).Append('\n');

                string myName = FactionNameOf(mySide == 1 ? e.AttackerSide : e.DefenderSide);
                string enName = FactionNameOf(enemySide == 1 ? e.AttackerSide : e.DefenderSide);
                sb.Append("我方: ").Append(myName).Append(SideDetail(mySide == 1 ? e.AttackerSide : e.DefenderSide)).Append('\n');
                sb.Append("敌方: ").Append(enName).Append(SideDetail(enemySide == 1 ? e.AttackerSide : e.DefenderSide)).Append('\n');

                int myLoss, enLoss, myShipLoss, enShipLoss;
                SideLoss(mySide, e, out myLoss, out myShipLoss);
                SideLoss(enemySide, e, out enLoss, out enShipLoss);
                sb.Append("损失: 我方沉没 ").Append(myShipLoss).Append(" 艘 / 阵亡 ").Append(myLoss).Append(" 人");
                sb.Append(" · 敌方沉没 ").Append(enShipLoss).Append(" 艘 / 阵亡 ").Append(enLoss).Append(" 人\n");
                // 参战方是不是**你的舰队**(而不是王国的 AI 领主部队)—— 战报口径见文件头, 这里如实说明
                sb.Append(MyFleetInvolved(e, mySide)
                    ? "参战: 含你的舰队(地图上这支舰队真的打了这一仗)\n"
                    : "参战: 我方王国部队(其中没有你的舰队 —— 这是王国的海战, 你的舰队并未参战)\n");
                sb.Append("说明: 本条是**地图上真实发生**的海战摘要(战果已由原版结算, 本页只做记录与索引)");
                return sb.ToString();
            }
            catch (Exception ex) { return "第 " + NavalTaskForce.SafeToday() + " 天 · 【实战】海战(战报生成异常: " + ex.Message + ")"; }
        }

        private static string HeadlineOf(MapEvent e, int mySide, int enemySide)
        {
            try
            {
                var win = e.WinningSide;
                bool decided = win != BattleSideEnum.None;
                bool mineWon = (mySide == 1 && win == BattleSideEnum.Attacker)
                            || (mySide == -1 && win == BattleSideEnum.Defender);
                string where = WhereText(e);
                string verb;
                if (!decided) verb = "海战结束(未分胜负)";
                else verb = mineWon ? "海战胜利" : "海战失利";
                return verb + " · " + where;
            }
            catch { return "海战结束"; }
        }

        // 交战地点: 海战的事件位置就在海面上, 直接给坐标 + (若沾着某个聚落)港口名
        private static string WhereText(MapEvent e)
        {
            try
            {
                string port = "";
                try
                {
                    var s = e.MapEventSettlement;
                    if (s != null && s.HasPort) port = "「" + NavalBridge.NameOf(s) + "」海域";
                }
                catch { }
                if (!string.IsNullOrEmpty(port)) return port;
                return "海域(" + (int)e.Position.X + "," + (int)e.Position.Y + ")";
            }
            catch { return "海域"; }
        }

        private static string FactionNameOf(MapEventSide s)
        {
            try
            {
                var f = s != null ? s.MapFaction : null;
                if (f == null) return "?";
                if (f.Name != null && !string.IsNullOrEmpty(f.Name.ToString())) return f.Name.ToString();
                return f.StringId ?? "?";
            }
            catch { return "?"; }
        }

        // 该侧的一句话明细: 参战部队数 + 我方这边额外标出**哪几支是舰队**(玩家最关心的就是"我的舰队在不在里面")
        private static string SideDetail(MapEventSide s)
        {
            try
            {
                if (s == null) return "";
                var parties = s.Parties;
                if (parties == null) return "";
                int n = 0, fleets = 0, ships = 0;
                var names = new StringBuilder();
                for (int i = 0; i < parties.Count; i++)
                {
                    var mp = parties[i];
                    if (mp == null || mp.Party == null) continue;
                    n++;
                    try
                    {
                        var shipsList = mp.Party.Ships;
                        if (shipsList != null) ships += shipsList.Count;
                    }
                    catch { }
                    var mob = null as MobileParty;
                    try { mob = mp.Party.MobileParty; } catch { }
                    if (mob != null && NavalTaskForce.IsTaskForce(mob))
                    {
                        fleets++;
                        if (names.Length > 0) names.Append('、');
                        names.Append(MapSelection.NameOf(mob));
                    }
                }
                var sb = new StringBuilder();
                sb.Append("(参战 ").Append(n).Append(" 支部队 · 舰船 ").Append(ships).Append(" 艘");
                if (fleets > 0) sb.Append(" · 含舰队 ").Append(names.ToString());
                sb.Append(')');
                return sb.ToString();
            }
            catch { return ""; }
        }

        private static void SideLoss(int side, MapEvent e, out int troop, out int ship)
        {
            troop = 0; ship = 0;
            try
            {
                var s = side == 1 ? e.AttackerSide : e.DefenderSide;
                if (s == null) return;
                troop = s.TroopCasualties;
                ship = s.ShipCasualties;
            }
            catch { }
        }

        // v4.277: 我方这一侧的参战部队里**有没有玩家的舰队(fia_navy_)或玩家主队**。
        //   用途: 战报里如实区分"你的舰队真打了这一仗"与"这是王国的海战(你的舰队没参与)" ——
        //   两者都算我方海战、都进战报(口径见文件头), 但玩家必须看得出区别, 否则会以为自己派出去的
        //   舰队打了一仗, 实际上打的是隔壁 AI 领主的船。
        private static bool MyFleetInvolved(MapEvent e, int mySide)
        {
            try
            {
                var s = mySide == 1 ? e.AttackerSide : e.DefenderSide;
                var parties = s != null ? s.Parties : null;
                if (parties == null) return false;
                for (int i = 0; i < parties.Count; i++)
                {
                    var mp = parties[i];
                    if (mp == null || mp.Party == null) continue;
                    MobileParty mob = null;
                    try { mob = mp.Party.MobileParty; } catch { }
                    if (mob == null) continue;
                    if (NavalTaskForce.IsTaskForce(mob)) return true;      // 地图上的实体舰队
                    try { if (ReferenceEquals(mob, MobileParty.MainParty)) return true; } catch { }   // 玩家主队
                }
            }
            catch { }
            return false;
        }
    }
}
