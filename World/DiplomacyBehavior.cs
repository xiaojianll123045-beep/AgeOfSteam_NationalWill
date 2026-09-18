using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.SaveSystem;

namespace FeudalInternalAffairs
{
    // 国家关系 + 同盟参战的存档与事件
    internal class DiplomacyBehavior : CampaignBehaviorBase
    {
        internal static DiplomacyBehavior Current;
        private string _relations = "";
        private string _warDairy = "";   // AI 外交: 战争开始日/停战日(第 21 章后的外交补全)
        private string _diploPlay = "";  // 第 22 章: 外交博弈存档
        private bool _empireAllianceDone;
        private static bool _joiningWar;

        // 同盟参战/同步宣战时置 true(绕过"NoAiControlPatches 禁止 AI 宣战"的拦截)
        internal static bool AllyForcingWar;

        // 双方表决通过后执行停战时置 true(绕过"NoAiControlPatches 禁止 AI 和谈"的拦截)
        internal static bool ForcingPeace;

        internal DiplomacyBehavior() { Current = this; }

        public override void RegisterEvents()
        {
            Current = this;
            CampaignEvents.WarDeclared.AddNonSerializedListener(this, OnWarDeclared);
            CampaignEvents.MakePeace.AddNonSerializedListener(this, OnMakePeace);
            CampaignEvents.OnAllianceStartedEvent.AddNonSerializedListener(this, OnAllianceStarted);
            CampaignEvents.OnAllianceEndedEvent.AddNonSerializedListener(this, OnAllianceEnded);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
            CampaignEvents.OnNewGameCreatedEvent.AddNonSerializedListener(this, OnNewGameCreated);
            CampaignEvents.OnGameLoadedEvent.AddNonSerializedListener(this, OnGameLoaded);
        }

        public override void SyncData(IDataStore dataStore)
        {
            try
            {
                if (dataStore.IsSaving)
                {
                    _relations = Diplomacy.Save();
                    _warDairy = AiDiplomacy.Save();
                    _diploPlay = DiploPlays.Save();
                }
                dataStore.SyncData("FIA_Diplomacy", ref _relations);
                dataStore.SyncData("FIA_WarDairy", ref _warDairy);
                dataStore.SyncData("FIA_DiploPlay", ref _diploPlay);
                dataStore.SyncData("FIA_EmpireAlliance", ref _empireAllianceDone);
                if (dataStore.IsLoading)
                {
                    Diplomacy.Load(_relations);
                    AiDiplomacy.Load(_warDairy);
                    DiploPlays.Load(_diploPlay);
                    DLog.Force("读档: 国家关系 " + (Diplomacy.Save().Length > 3 ? "已载入" : "空"));
                }
            }
            catch (Exception ex) { DLog.Force("外交 SyncData 异常: " + ex.Message); }
        }

        private void OnNewGameCreated(CampaignGameStarter starter)
        {
            try { EnsureEmpireAlliance(); } catch { }
        }

        private void OnGameLoaded(CampaignGameStarter starter)
        {
            try
            {
                EnsureEmpireAlliance();
                AiDiplomacy.CleanNativeWarPeaceDecisions();   // 清掉旧存档里的原版战争/和平决议
            }
            catch { }
        }

        // 帝国三国(西/南/北)互相缔结同盟 —— 开局设定, 只做一次
        internal void EnsureEmpireAlliance()
        {
            try
            {
                if (_empireAllianceDone) return;
                var list = new System.Collections.Generic.List<Kingdom>();
                foreach (var k in Kingdom.All)
                {
                    if (k == null || k.IsEliminated) continue;
                    if (k.Culture == null || k.Culture.StringId != "empire") continue;
                    list.Add(k);
                }
                if (list.Count < 2) return;
                int made = 0;
                for (int i = 0; i < list.Count; i++)
                {
                    for (int j = i + 1; j < list.Count; j++)
                    {
                        var a = list[i];
                        var b = list[j];
                        if (a.IsAtWarWith(b)) continue;
                        if (Diplomacy.IsAlly(a, b)) { Diplomacy.Set(a, b, Math.Max(Diplomacy.Get(a, b), 75)); continue; }
                        if (Diplomacy.StartAlliance(a, b))
                        {
                            Diplomacy.Set(a, b, 75);   // 帝国兄弟: 关系设为友好
                            made++;
                            DLog.Force("帝国同盟: " + a.Name + " <-> " + b.Name);
                        }
                    }
                }
                _empireAllianceDone = true;
                if (made > 0)
                {
                    MapSelection.Message("帝国三国(西/南/北)已缔结同盟");
                    DLog.Force("帝国同盟: 共缔结 " + made + " 对");
                }
            }
            catch (Exception ex) { DLog.Force("帝国同盟失败: " + ex.Message); }
        }

        // ---- 宣战: 关系 -40; 被入侵方的盟友一起宣战 ----
        private void OnWarDeclared(IFaction f1, IFaction f2, DeclareWarAction.DeclareWarDetail detail)
        {
            try
            {
                var attacker = f1 as Kingdom;
                var defender = f2 as Kingdom;
                if (attacker == null || defender == null) return;

                int before = Diplomacy.Get(attacker, defender);
                Diplomacy.Change(attacker, defender, Diplomacy.WarPenalty);
                AiDiplomacy.NoteWar(attacker, defender);
                DiploPlays.OnWarDeclared(attacker, defender);   // 第 22 章: 建立战时支持度
                DLog.Force("外交: " + attacker.Name + " 对 " + defender.Name + " 宣战, 关系 "
                    + before + " -> " + Diplomacy.Get(attacker, defender));

                CallAlliesToWar(attacker, defender);
            }
            catch (Exception ex) { DLog.Force("宣战处理异常: " + ex.Message); }
        }

        // 同盟参战: 被宣战方的盟友一起对宣战方宣战(设计需求)
        private void CallAlliesToWar(Kingdom attacker, Kingdom defender)
        {
            if (_joiningWar) return;
            try
            {
                _joiningWar = true;
                var playerKingdom = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                foreach (var ally in Kingdom.All)
                {
                    if (ally == null || ally == attacker || ally == defender) continue;
                    if (!Diplomacy.IsAlly(ally, defender)) continue;
                    if (ally.IsAtWarWith(attacker)) continue;
                    try
                    {
                        AllyForcingWar = true;
                        try { DeclareWarAction.ApplyByDefault(ally, attacker); }
                        finally { AllyForcingWar = false; }
                        DLog.Force("同盟参战: " + ally.Name + " 因同盟 " + defender.Name + " 被入侵, 对 " + attacker.Name + " 宣战");
                        if (ally == playerKingdom || defender == playerKingdom)
                        {
                            MapSelection.Message("同盟参战: " + ally.Name + " 加入了对 " + attacker.Name + " 的战争");
                        }
                    }
                    catch (Exception ex) { DLog.Info("盟友参战失败 " + ally.Name + ": " + ex.Message); }
                }
            }
            catch { }
            finally { _joiningWar = false; }
        }

        // ---- 和谈: 关系 +25 ----
        private void OnMakePeace(IFaction f1, IFaction f2, MakePeaceAction.MakePeaceDetail detail)
        {
            try
            {
                var a = f1 as Kingdom;
                var b = f2 as Kingdom;
                if (a == null || b == null) return;
                int before = Diplomacy.Get(a, b);
                Diplomacy.Change(a, b, Diplomacy.PeaceBonus);
                AiDiplomacy.NotePeace(a, b);
                DiploPlays.OnPeace(a, b);   // 第 22 章: 按战争表现执行诉求
                DLog.Force("外交: " + a.Name + " 与 " + b.Name + " 停战, 关系 " + before + " -> " + Diplomacy.Get(a, b));
            }
            catch (Exception ex) { DLog.Force("和谈处理异常: " + ex.Message); }
        }

        private void OnAllianceStarted(Kingdom a, Kingdom b)
        {
            try
            {
                if (a == null || b == null) return;
                Diplomacy.Set(a, b, Math.Max(Diplomacy.Get(a, b), Diplomacy.AllyThreshold));
                DLog.Force("外交: " + a.Name + " 与 " + b.Name + " 缔结同盟");
                var pk = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                if (a == pk || b == pk)
                    MapSelection.Message("已与 " + (a == pk ? b.Name : a.Name) + " 缔结同盟");
            }
            catch { }
        }

        private void OnAllianceEnded(Kingdom a, Kingdom b)
        {
            try
            {
                if (a == null || b == null) return;
                Diplomacy.Change(a, b, Diplomacy.BreakAllyPenalty);
                DLog.Force("外交: " + a.Name + " 与 " + b.Name + " 同盟结束");
            }
            catch { }
        }

        private void OnDailyTick()
        {
            try
            {
                Diplomacy.DailyDrift();
                SyncAllyWars();
                AiDiplomacy.CleanNativeWarPeaceDecisions();   // 兜底: 拦住的原版决议若还挂在队列里, 每天清一次
                DiploPlays.Daily((int)TaleWorlds.CampaignSystem.CampaignTime.Now.ToDays);   // 第 22 章: 博弈推进/战时支持度
                if (TaleWorlds.CampaignSystem.CampaignTime.Now.GetDayOfWeek == 0)
                    AiDiplomacy.Month((int)TaleWorlds.CampaignSystem.CampaignTime.Now.ToDays);
            }
            catch { }
        }

        // 同盟国家每天同步宣战状态(设计需求): 盟友正在交战的对手, 我也要对其宣战
        private static void SyncAllyWars()
        {
            if (_joiningWar) return;
            try
            {
                _joiningWar = true;
                var pk = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                foreach (var a in Kingdom.All)
                {
                    if (a == null || a.IsEliminated) continue;
                    foreach (var b in Kingdom.All)
                    {
                        if (b == null || b == a || b.IsEliminated) continue;
                        if (string.CompareOrdinal(a.StringId, b.StringId) >= 0) continue;   // 每对只处理一次
                        if (!Diplomacy.IsAlly(a, b)) continue;
                        foreach (var x in Kingdom.All)
                        {
                            if (x == null || x == a || x == b || x.IsEliminated) continue;
                            bool aw = false, bw = false;
                            try { aw = a.IsAtWarWith(x); bw = b.IsAtWarWith(x); } catch { continue; }
                            if (aw == bw) continue;
                            var joiner = aw ? b : a;
                            var source = aw ? a : b;
                            try
                            {
                                AllyForcingWar = true;
                                try { DeclareWarAction.ApplyByDefault(joiner, x); }
                                finally { AllyForcingWar = false; }
                                DLog.Force("同盟同步: " + joiner.Name + " 对 " + x.Name + " 宣战(因盟友 " + source.Name + " 正在交战)");
                                if (joiner == pk || source == pk)
                                    MapSelection.Message("同盟同步: " + joiner.Name + " 加入了对 " + x.Name + " 的战争");
                            }
                            catch (Exception ex) { DLog.Info("同盟同步失败: " + ex.Message); }
                        }
                    }
                }
            }
            catch { }
            finally { _joiningWar = false; }
        }

        // ================= 供 UI 调用的操作 =================

        // 宣战(玩家) —— 需要绕过"NoAiControlPatches 禁止 AI 宣战"
        internal static bool PlayerDeclareWar(Kingdom target)
        {
            try
            {
                var pk = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                if (pk == null || target == null || pk == target) return false;
                if (pk.IsAtWarWith(target)) return false;
                DiplomacyPatches.PlayerDeclaringWar = true;
                try { DeclareWarAction.ApplyByDefault(pk, target); }
                finally { DiplomacyPatches.PlayerDeclaringWar = false; }
                return true;
            }
            catch (Exception ex) { DLog.Force("宣战失败: " + ex.Message); return false; }
        }

        // 求和(玩家) —— 双方联盟领主表决: 我方多数通过 且 对方多数通过 才停战(用户需求)
        internal static bool PlayerMakePeace(Kingdom target)
        {
            try
            {
                var pk = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                if (pk == null || target == null) return false;
                if (!pk.IsAtWarWith(target)) return false;

                int cd;
                if (Diplomacy.PeaceOnCooldown(target.StringId, out cd))
                {
                    MapSelection.Message("和谈冷却中: 还需 " + cd + " 天才能再次发起");
                    return false;
                }

                var ourSide = Diplomacy.SideOf(pk);
                var enemySide = Diplomacy.SideOf(target);

                // 第一关: 我方联盟(本国+盟国)领主表决, 严格多数 > 50%
                int y1, t1, d1;
                bool ok1 = Diplomacy.VotePeace(ourSide, target, out y1, out t1, out d1);
                if (!ok1)
                {
                    Diplomacy.SetPeaceCooldown(target.StringId, 7);
                    MapSelection.Message("和谈被本国联盟否决(" + y1 + "/" + t1 + " 赞成), 7 天内不能再次发起");
                    DLog.Force("和谈否决(我方): " + y1 + "/" + t1);
                    return false;
                }

                // 第二关: 对方(或其联盟)领主表决
                int y2, t2, d2;
                bool ok2 = Diplomacy.VotePeace(enemySide, pk, out y2, out t2, out d2);
                if (!ok2)
                {
                    Diplomacy.SetPeaceCooldown(target.StringId, 7);
                    MapSelection.Message("和谈被对方联盟否决(我方 " + y1 + "/" + t1 + " 通过, 对方 " + y2 + "/" + t2 + "), 7 天内不能再次发起");
                    DLog.Force("和谈否决(对方): " + y2 + "/" + t2);
                    return false;
                }

                // 双方阵营两两停战
                ForcingPeace = true;
                try
                {
                    foreach (var a in ourSide)
                    {
                        if (a == null) continue;
                        foreach (var b in enemySide)
                        {
                            if (b == null || a == b) continue;
                            try { if (a.IsAtWarWith(b)) MakePeaceAction.Apply(a, b); }
                            catch (Exception ex) { DLog.Info("停战失败 " + a.StringId + "/" + b.StringId + ": " + ex.Message); }
                        }
                    }
                }
                finally { ForcingPeace = false; }

                MapSelection.Message("和谈成功: 我方 " + y1 + "/" + t1 + " · 对方 " + y2 + "/" + t2 + " 赞成");
                DLog.Force("和谈成功: 我方 " + y1 + "/" + t1 + " 对方 " + y2 + "/" + t2);
                return true;
            }
            catch (Exception ex) { DLog.Force("求和失败: " + ex.Message); return false; }
        }

        // 缔结同盟(玩家) —— 需要关系 >= 50
        internal static bool PlayerStartAlliance(Kingdom target)
        {
            try
            {
                var pk = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                if (pk == null || target == null || pk == target) return false;
                if (Diplomacy.IsAlly(pk, target)) return false;
                int rel = Diplomacy.Get(pk, target);
                if (rel < Diplomacy.AllyThreshold)
                {
                    MapSelection.Message("关系不足(" + rel + "/" + Diplomacy.AllyThreshold + "), 无法缔结同盟");
                    return false;
                }
                if (pk.IsAtWarWith(target))
                {
                    MapSelection.Message("正在交战, 无法缔结同盟");
                    return false;
                }
                return Diplomacy.StartAlliance(pk, target);
            }
            catch (Exception ex) { DLog.Force("缔结同盟失败: " + ex.Message); return false; }
        }

        internal static bool PlayerBreakAlliance(Kingdom target)
        {
            try
            {
                var pk = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                if (pk == null || target == null) return false;
                if (!Diplomacy.IsAlly(pk, target)) return false;
                Diplomacy.EndAlliance(pk, target);
                return true;
            }
            catch (Exception ex) { DLog.Force("解除同盟失败: " + ex.Message); return false; }
        }
    }
}
