using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace FeudalInternalAffairs
{
    // 领主政治档案(文档 21 章): 政治系统的原子单位
    internal class LordProfile
    {
        internal string ClanId;
        internal string Name = "?";
        internal int Clout;        // 势力(表决权重/派系力量)
        internal int Attitude;     // 态度 -100..100
        internal int Anger;        // 愤怒 0..100
        internal int Fear;         // 威慑 0..100
        internal int Seat = -1;    // 御前会议席位 -1 无 / 0..4
        internal int Tendency = 1; // 倾向 0 王室 1 贵族 2 教权 3 市民
        internal int TitleBonus;   // 授勋带来的永久势力加成
    }

    // 请愿(文档 21.5)
    internal class Petition
    {
        internal string Kind;      // tax_cut / compensation / price_control / charter / tithe_raise / title
        internal int Estate;       // 0 领主 1 教会 2 市民
        internal string ClanId;
        internal string Text;
        internal int Expire;       // 战役日
    }

    // 封建政治系统(文档第 21 章): 领主为首, 权威/合法性/势力; 与税制/所有权/行会/铸币/激进度双向接入
    internal static class Politics
    {
        // ---- 常量表 ----
        internal static readonly string[] EstateNames = { "领主", "教会", "行会市民" };
        internal static readonly string[] LawNames = { "王位继承", "税赋特权", "军务征召", "教产保护", "商贸特许", "王室司法" };
        internal static readonly string[] LawLevels = { "未立", "初立", "通行", "钦定" };
        internal static readonly string[] LawShort =
        {
            "合法性 +2.5/级",
            "税收-6%/级 · 领主态度+5",
            "军事+4%/级 · 领主怒+2",
            "教会态度+6/级 · 税收-3%",
            "特许费-500/级 · 加成+2%",
            "激进-0.1%/日 · 领主态度-4"
        };
        internal static readonly string[] SeatNames = { "首相", "掌玺", "财政", "军务", "司法" };
        internal static readonly string[] SeatBonusText = { "权威+2/月", "合法性+2", "税收+3%", "军事+4%", "激进-0.05%/日" };
        internal static readonly string[] TendencyNames = { "王室", "贵族", "教权", "市民" };

        // ---- 状态 ----
        internal static float Authority = 200f;   // 0..1000
        internal static float Legitimacy = 50f;   // 0..100
        internal static float Tyranny;            // 暴政值
        internal static bool CivilWar;
        internal static int CivilWarDay = -9999;
        internal static readonly int[] Laws = new int[6];
        internal static readonly string[] Council = new string[5];   // clanId
        internal static readonly Dictionary<string, LordProfile> Lords = new Dictionary<string, LordProfile>();
        internal static readonly List<Petition> Petitions = new List<Petition>();

        // 最后通牒(文档 21.6 阶梯)
        internal static bool UltimatumActive;
        internal static int UltimatumDay = -9999;
        internal static int UltimatumKind;   // 0 减税 1 赔偿 2 授勋
        internal static int LastRebellionDay = -9999;

        // 阶层愤怒偏移(拒绝请愿等事件的持久影响; 月结衰减)
        internal static int ChurchAngerOffset, BurgerAngerOffset;

        // 阶层聚合(UI/判定用)
        internal static int NobleClout, NobleAttitude, NobleAnger;
        internal static int ChurchClout, ChurchAttitude, ChurchAnger;
        internal static int BurgerClout, BurgerAttitude, BurgerAnger;

        private static int _dividendAccum;
        private static int _lastMonthDay = -1;

        // ================= 初始化 =================
        internal static void Reset()
        {
            try
            {
                Authority = 200f; Legitimacy = 50f; Tyranny = 0f;
                CivilWar = false; CivilWarDay = -9999;
                UltimatumActive = false; UltimatumDay = -9999; UltimatumKind = 0;
                LastRebellionDay = -9999;
                ChurchAngerOffset = 0; BurgerAngerOffset = 0;
                for (int i = 0; i < 6; i++) Laws[i] = 0;
                for (int i = 0; i < 5; i++) Council[i] = "";
                Lords.Clear();
                Petitions.Clear();
                _dividendAccum = 0;
                _lastMonthDay = -1;
                LawSystem.Reset();            // v5.0-P22: 法律体系(文档 24.3)
                InterestGroups.Reset();       // v5.0-P22: 利益集团(文档 24.2)
                Revolution.Reset();           // v5.0-P23: 革命(文档 24.5)
                TradeRoutes.Reset();          // v5.0-P24: 贸易路线(文档 24.7)
                WarMobilization.Reset();      // v5.0-P25: 动员
                Elections.Reset();            // v5.0-P25: 选举
                Research.Reset();             // v5.0-P26: 研究
                Institutions.Reset();         // v5.0-P26: 机构/文化
                PowerBlocs.Reset();           // v5.0-P27: 权力集团
                Interests.Reset();            // v5.0-P27: 利益宣示
                RefreshLords();
                Aggregate();
                DLog.Force("政治: 初始化 领主=" + Lords.Count + " 权威=" + (int)Authority + " 合法性=" + (int)Legitimacy);
            }
            catch (Exception ex) { DLog.Force("政治初始化失败: " + ex.Message); }
        }

        internal static int LawLevel(int i)
        {
            try { return (i >= 0 && i < 6) ? Laws[i] : 0; } catch { return 0; }
        }

        // 领主名册刷新: 从玩家王国家族重建/补充(势力/姓名实时)
        internal static void RefreshLords()
        {
            try
            {
                var k = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                if (k == null) return;
                var me = Clan.PlayerClan;
                var seen = new HashSet<string>();
                foreach (var c in k.Clans)
                {
                    if (c == null || string.IsNullOrEmpty(c.StringId)) continue;
                    if (me != null && ReferenceEquals(c, me)) continue;
                    seen.Add(c.StringId);
                    LordProfile lp;
                    if (!Lords.TryGetValue(c.StringId, out lp))
                    {
                        lp = new LordProfile { ClanId = c.StringId };
                        int rel = 0;
                        try { if (c.Leader != null && Hero.MainHero != null) rel = c.Leader.GetRelation(Hero.MainHero); } catch { }
                        lp.Attitude = Clamp(rel, -40, 40) + Ownership.FavorOf(c.StringId);
                        lp.Attitude = Clamp(lp.Attitude, -100, 100);
                        lp.Tendency = GuessTendency(c);
                        Lords[c.StringId] = lp;
                    }
                    lp.Name = c.Name != null ? c.Name.ToString()
                        : (c.Leader != null ? c.Leader.Name.ToString() : c.StringId);
                    if (lp.Name.Length > 10) lp.Name = lp.Name.Substring(0, 10);
                    int fiefs = 0;
                    try { if (c.Settlements != null) fiefs = c.Settlements.Count; } catch { }
                    int tier = 0;
                    try { tier = c.Tier; } catch { }
                    float inf = 0f;
                    try { inf = c.Influence; } catch { }
                    int gold = 0;
                    try { if (c.Leader != null) gold = c.Leader.Gold; } catch { }
                    lp.Clout = (int)(fiefs * 25f + tier * 12f + inf * 0.1f + gold / 4000f) + lp.TitleBonus;
                    if (lp.Clout < 5) lp.Clout = 5;
                    if (lp.Seat >= 0 && lp.Seat < 5 && Council[lp.Seat] != lp.ClanId) lp.Seat = -1;
                }
                var gone = new List<string>();
                foreach (var kv in Lords) if (!seen.Contains(kv.Key)) gone.Add(kv.Key);
                for (int i = 0; i < gone.Count; i++)
                {
                    var lp = Lords[gone[i]];
                    if (lp.Seat >= 0 && lp.Seat < 5 && Council[lp.Seat] == lp.ClanId) Council[lp.Seat] = "";
                    Lords.Remove(gone[i]);
                }
            }
            catch { }
        }

        // 倾向(文档 21.1): 优先看领主性格, 再看领地/哈希
        private static int GuessTendency(Clan c)
        {
            try
            {
                int h = 0;
                string s = c.StringId ?? "";
                for (int i = 0; i < s.Length; i++) h = (h * 31 + s[i]) & 0x7fffffff;
                var l = c.Leader;
                if (l != null)
                {
                    int mercy = SafeTrait(l, DefaultTraits.Mercy);
                    int gen = SafeTrait(l, DefaultTraits.Generosity);
                    int calc = SafeTrait(l, DefaultTraits.Calculating);
                    int honor = SafeTrait(l, DefaultTraits.Honor);
                    if (mercy + gen >= 1) return 2;              // 仁慈/慷慨 -> 教权
                    if (calc >= 1) return 1;                     // 精于算计 -> 贵族
                    if (honor >= 1) return 1;
                    if (mercy + gen + calc + honor == 0)
                    {
                        if (h % 5 <= 1) return 2;
                        if (h % 5 <= 3) return 3;
                        return 1;
                    }
                }
                else
                {
                    if (h % 5 <= 1) return 2;
                    if (h % 5 <= 3) return 3;
                }
                return 1;
            }
            catch { return 1; }
        }

        private static int SafeTrait(Hero h, TaleWorlds.CampaignSystem.CharacterDevelopment.TraitObject t)
        {
            try { return h.GetTraitLevel(t); } catch { return 0; }
        }

        // ================= 每日 =================
        internal static void TickDay(int day)
        {
            try
            {
                if (Lords.Count == 0) RefreshLords();

                // 最后通牒到期(文档 21.6): 领主转入抗命
                if (UltimatumActive && day >= UltimatumDay && !CivilWar)
                {
                    UltimatumActive = false;
                    foreach (var kv in Lords) kv.Value.Anger = Clamp(kv.Value.Anger + 10, 0, 100);
                    Legitimacy = ClampF(Legitimacy - 5f, 0f, 100f);
                    Pops.ShiftRadicals(0.02f);
                    Notify("最后通牒到期: 领主转入抗命(部分领地减产、拒税)", true);
                    DLog.Force("政治: 最后通牒到期 -> 抗命阶段");
                }

                if (CivilWar)
                {
                    Authority = Math.Max(0f, Authority - 0.5f);
                    if (day - CivilWarDay > 84)   // 拖满一年: 结算(V4.134: 革命阵营 -> 革命胜利; 否则疲惫停战)
                    {
                        if (Revolution.Current != null && Revolution.Current.Active)
                        {
                            Revolution.OnCivilWarTimeout(day);
                        }
                        else
                        {
                            CivilWar = false;
                            Legitimacy = Math.Max(5f, Legitimacy - 10f);
                            for (int i = 0; i < 3; i++) Pops.ShiftRadicals(0.01f);
                            Notify("内战拖满一年: 双方疲惫停战(合法性重创)", false);
                            DLog.Force("政治: 内战拖满一年, 停战");
                        }
                    }
                }
            }
            catch { }
        }

        // ================= 月度结算 =================
        internal static void Month(int day)
        {
            try
            {
                if (day == _lastMonthDay) return;
                _lastMonthDay = day;
                RefreshLords();

                // 1) 权威恢复 + 暴政衰减
                float regen = MonthRegen();
                Authority = ClampF(Authority + regen, 0f, 1000f);
                if (Tyranny > 0f) Tyranny = Math.Max(0f, Tyranny - 0.5f);

                // 2) 领主态度/愤怒: 税负 + 法令 + 私产池 + 自然衰减 + 威慑压制
                float taxOver = Math.Max(0f, TaxPolicy.Level[0] - 2f) + Math.Max(0f, TaxPolicy.Level[1] - 2f);
                bool lordDividend = Ownership.LordPool > 3000f;
                bool lordPoor = Ownership.LordPool < 400f;
                foreach (var kv in Lords)
                {
                    var lp = kv.Value;
                    lp.Anger += (int)Math.Round(taxOver * 2f);
                    lp.Anger += Laws[2] * 2;                 // 军务征召
                    lp.Anger += FeudalContracts.MonthlyAngerOf(kv.Key);   // v4.126: 封建契约(税档/兵役档)每月影响
                    lp.Attitude += Laws[1] * 5;              // 税赋特权
                    lp.Attitude -= Laws[5] * 4;              // 王室司法
                    if (lordDividend) { lp.Attitude += 1; lp.Anger -= 2; }
                    if (lordPoor) lp.Anger += 2;
                    if (lp.Fear >= 40) lp.Anger -= 1;        // 威慑压制
                    lp.Anger = Clamp(lp.Anger - 5, 0, 100);  // 自然衰减
                    lp.Attitude = Clamp(lp.Attitude, -100, 100);
                    if (lp.Seat >= 0) { lp.Attitude += 3; lp.Anger -= 3; }
                }
                _dividendAccum = 0;

                // 3) 阶层愤怒偏移衰减 + 聚合
                if (ChurchAngerOffset > 0) ChurchAngerOffset = Math.Max(0, ChurchAngerOffset - 2);
                if (BurgerAngerOffset > 0) BurgerAngerOffset = Math.Max(0, BurgerAngerOffset - 2);
                Aggregate();

                // 4) 合法性重算(v5.0-P22: 主干 = 执政集团(组阁)影响力, 文档 24.2; v4.140: 抽出为公共方法即时刷新)
                RecomputeLegitimacy();

                // 5) 请愿: 过期处理 + 生成
                for (int i = Petitions.Count - 1; i >= 0; i--)
                {
                    if (day >= Petitions[i].Expire) { RefuseEffect(Petitions[i], true); Petitions.RemoveAt(i); }
                }
                GeneratePetitions(day);

                // 6) 最后通牒(阶梯第 3 级)
                if (!CivilWar && !UltimatumActive && NobleAnger >= 75)
                {
                    UltimatumActive = true;
                    UltimatumDay = day + 7;
                    UltimatumKind = (TaxPolicy.Level[0] >= 3 || TaxPolicy.Level[1] >= 3) ? 0 : 2;
                    Notify("领主最后通牒: " + UltimatumText() + ", 限 7 天, 否则抗命", true);
                    DLog.Force("政治: 领主发出最后通牒 -> " + UltimatumText());
                }
                else if (UltimatumActive && NobleAnger < 65)
                {
                    UltimatumActive = false;
                    Notify("领主收回了最后通牒", false);
                    DLog.Force("政治: 最后通牒已收回");
                }

                // 7) 民众叛乱(原版叛乱接口; 文档 21.9 简化)
                if ((BurgerAnger >= 75 && Legitimacy < 45f) || (CivilWar && Legitimacy < 30f))
                    TryNativeRebellion(day);

                // 8) 内战判定
                if (!CivilWar)
                {
                    float crownClout = CrownClout();
                    if (NobleAnger >= 90 && NobleClout >= crownClout && Legitimacy < 25f)
                    {
                        CivilWar = true; CivilWarDay = day;
                        Notify("内战爆发! 领主们举起了叛旗(税收 -40% / 全境产出 -15%)", true);
                        DLog.Force("政治: 内战爆发! 领主愤怒=" + NobleAnger + " 势力=" + NobleClout + " 王室=" + (int)crownClout + " 合法性=" + (int)Legitimacy);
                    }
                }
                else if (NobleAnger < 45 && Legitimacy > 60f)
                {
                    CivilWar = false;
                    Notify("局势平息, 内战结束", false);
                    DLog.Force("政治: 局势平息, 内战结束");
                }

                DLog.Force("政治月结: 权威=" + (int)Authority + " 合法性=" + (int)Legitimacy + " 暴政=" + Tyranny.ToString("F1")
                    + " 领主愤怒=" + NobleAnger + " 教会=" + ChurchAnger + " 市民=" + BurgerAnger + " 请愿=" + Petitions.Count
                    + (CivilWar ? " [内战]" : (UltimatumActive ? " [最后通牒]" : "")));
            }
            catch (Exception ex) { DLog.Force("政治月结异常: " + ex.Message); }
        }

        // 阶段文本(文档 21.6)
        internal static string StageText()
        {
            try
            {
                if (CivilWar) return "内战";
                if (NobleAnger >= 85) return "抗命(产出-30%/拒税)";
                if (UltimatumActive) return "最后通牒(剩 " + Math.Max(0, UltimatumDay - Today()) + " 天)";
                if (NobleAnger >= 60) return "联名请愿";
                if (NobleAnger >= 30) return "不满";
                return "平稳";
            }
            catch { return "平稳"; }
        }

        internal static string UltimatumText()
        {
            switch (UltimatumKind)
            {
                case 0: return "要求全面减税";
                case 1: return "要求赔偿没收家产";
                default: return "要求授予头衔";
            }
        }

        // ---- 民众叛乱: 直接调用原版 RebellionsCampaignBehavior(用户指定) ----
        internal static string TryNativeRebellion(int day)
        {
            try
            {
                if (day - LastRebellionDay < 56) return "";
                var k = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                if (k == null) return "";
                Settlement worst = null;
                float worstLoy = 101f;
                foreach (var s in k.Settlements)
                {
                    if (s == null || !s.IsTown || s.Town == null) continue;
                    if (s.Town.Loyalty < worstLoy) { worstLoy = s.Town.Loyalty; worst = s; }
                }
                if (worst == null || worstLoy > 65f) return "";
                var behavior = Campaign.Current != null ? Campaign.Current.GetCampaignBehavior<RebellionsCampaignBehavior>() : null;
                if (behavior == null) { DLog.Force("政治: 找不到原版叛乱行为, 跳过"); return ""; }
                behavior.StartRebellionEvent(worst);
                LastRebellionDay = day;
                Notify("民变! " + worst.Name + " 的市民揭竿而起(原版叛乱)", true);
                DLog.Force("政治: 调用原版叛乱接口 -> " + worst.Name + " (忠诚 " + (int)worstLoy + ")");
                return worst.Name.ToString();
            }
            catch (Exception ex) { DLog.Force("政治: 触发原版叛乱失败: " + ex.Message); return ""; }
        }

        // ================= 抗命/内战的经济惩罚(接入税制与产出) =================
        internal static float SettlementOutputMult(Settlement s)
        {
            try
            {
                if (s == null) return 1f;
                var clan = s.OwnerClan;
                if (clan == null || string.IsNullOrEmpty(clan.StringId)) return 1f;
                if (CivilWar && Lords.ContainsKey(clan.StringId)) return 0.7f;   // 内战: 领主领地减产
                LordProfile lp;
                if (Lords.TryGetValue(clan.StringId, out lp) && lp.Anger >= 80) return 0.7f;   // 抗命
            }
            catch { }
            return 1f;
        }

        internal static float SettlementTaxFactor(Settlement s)
        {
            try
            {
                if (s == null) return 1f;
                var clan = s.OwnerClan;
                if (clan == null || string.IsNullOrEmpty(clan.StringId)) return 1f;
                LordProfile lp;
                if (Lords.TryGetValue(clan.StringId, out lp) && lp.Anger >= 80) return 0.5f;   // 抗命: 拒税
            }
            catch { }
            return 1f;
        }

        private static void GeneratePetitions(int day)
        {
            try
            {
                int before = Petitions.Count;
                if (Petitions.Count >= 3) return;
                // 领主: 减税
                if ((TaxPolicy.Level[0] >= 3 || TaxPolicy.Level[1] >= 3) && CanAdd("tax_cut"))
                    AddPetition(new Petition { Kind = "tax_cut", Estate = 0, Text = "领主们联名请愿: 税负过重, 请求减税", Expire = day + 14 });
                // 领主: 赔偿(近期没收)
                if (Petitions.Count < 3)
                {
                    string cid = null;
                    foreach (var kv in Ownership.ConfiscateDay)
                    {
                        if (day - kv.Value <= 21) { cid = kv.Key; break; }
                    }
                    if (cid != null && CanAdd("compensation"))
                        AddPetition(new Petition { Kind = "compensation", Estate = 0, ClanId = cid, Text = "被没收产业的领主请求赔偿(3000)", Expire = day + 14 });
                }
                // 市民: 物价
                if (Petitions.Count < 3 && (MintRight.Purity > 0 || PriceIndex() > 1.08f) && CanAdd("price_control"))
                    AddPetition(new Petition { Kind = "price_control", Estate = 2, Text = "市民请愿: 物价飞涨, 请求货币足银", Expire = day + 14 });
                // 市民: 特许
                if (Petitions.Count < 3 && Guilds.Founded.Count > 0 && CountChartered() == 0 && CanAdd("charter"))
                    AddPetition(new Petition { Kind = "charter", Estate = 2, Text = "行会请愿: 请求王室授予首份特许状(免费)", Expire = day + 14 });
                // 教会: 什一
                if (Petitions.Count < 3 && Ownership.ChurchPool < 1000f && TaxPolicy.Level[2] < 2 && CanAdd("tithe_raise"))
                    AddPetition(new Petition { Kind = "tithe_raise", Estate = 1, Text = "教会请愿: 教产入不敷出, 请求提高什一税", Expire = day + 14 });
                // 领主: 授勋(愤怒最高者)
                if (Petitions.Count < 3)
                {
                    LordProfile worst = null;
                    foreach (var kv in Lords) if (kv.Value.Anger >= 40 && (worst == null || kv.Value.Anger > worst.Anger)) worst = kv.Value;
                    if (worst != null && CanAdd("title"))
                        AddPetition(new Petition { Kind = "title", Estate = 0, ClanId = worst.ClanId, Text = worst.Name + " 请求授予头衔", Expire = day + 14 });
                }
                int added = Petitions.Count - before;
                if (added > 0) ShowPetitionPopup(added);   // v4.59: 新请愿弹窗通知玩家(用户需求)
            }
            catch { }
        }

        // v4.59: 新请愿 -> 弹窗(用户需求)
        private static void ShowPetitionPopup(int count)
        {
            try
            {
                string last = Petitions.Count > 0 ? Petitions[Petitions.Count - 1].Text : "";
                string text = (count == 1 ? last : ("新增 " + count + " 条请愿, 最近一条: " + last))
                    + "\n前往政治页处理: 同意可安抚对应阶层; 14 天内不回应将激化民怨。";
                InformationManager.ShowInquiry(new InquiryData(
                    "民众请愿", text, true, true, "前往政治页", "稍后处理",
                    delegate { try { PoliticsPanel.Open(); } catch { } }, null,
                    "", 0f, null, null, null), true, false);
            }
            catch (Exception ex) { DLog.Force("请愿弹窗异常: " + ex.Message); }
        }

        private static bool CanAdd(string kind)
        {
            for (int i = 0; i < Petitions.Count; i++) if (Petitions[i].Kind == kind) return false;
            return true;
        }

        private static void AddPetition(Petition p)
        {
            Petitions.Add(p);
            Notify("新请愿: " + p.Text, false);
            DLog.Force("政治: 收到请愿 -> " + p.Text);
        }

        private static int CountChartered()
        {
            int n = 0;
            foreach (var g in Guilds.All) if (Guilds.IsChartered(g.Id)) n++;
            return n;
        }

        private static float PriceIndex()
        {
            try { return Stats.Ring.Count > 0 ? Stats.Ring[Stats.Ring.Count - 1].PriceIndex : 1f; }
            catch { return 1f; }
        }

        // ================= 阶层聚合 =================
        internal static void Aggregate()
        {
            try
            {
                long wa = 0, wg = 0;
                int sum = 0;
                foreach (var kv in Lords)
                {
                    var lp = kv.Value;
                    int w = Math.Max(1, lp.Clout);
                    wa += lp.Attitude * w; wg += lp.Anger * w; sum += w;
                }
                NobleClout = sum;
                NobleAttitude = sum > 0 ? (int)(wa / sum) : 0;
                NobleAnger = sum > 0 ? (int)(wg / sum) : 0;

                float rad = AvgRadicalism();
                int charters = CountChartered();
                int marketTaxOver = Math.Max(0, TaxPolicy.Level[3] - 2);

                ChurchClout = (int)(Ownership.ChurchPool / 20f) + 30;
                ChurchAttitude = Clamp((int)(50f + Laws[3] * 6f + Ownership.ChurchPool / 2000f
                    - MintRight.Purity * 12f - marketTaxOver * 5f), -100, 100);
                ChurchAnger = Clamp((int)Math.Round(20f + MintRight.Purity * 10f + marketTaxOver * 6f
                    + (Ownership.ChurchPool < 1000f ? 10f : 0f) - Laws[3] * 5f) + ChurchAngerOffset, 0, 100);

                BurgerClout = (int)(Ownership.GuildFund / 20f) + 20 + charters * 10;
                BurgerAttitude = Clamp((int)(50f + Laws[4] * 5f + charters * 5f - marketTaxOver * 8f - rad * 40f), -100, 100);
                BurgerAnger = Clamp((int)Math.Round(15f + rad * 60f + marketTaxOver * 8f
                    + (charters == 0 ? 5f : 0f) + MintRight.Purity * 4f - Laws[4] * 4f) + BurgerAngerOffset, 0, 100);
            }
            catch { }
        }

        internal static float AvgRadicalism()
        {
            try
            {
                float num = 0f, den = 0f;
                foreach (var kv in Pops.BySettlement)
                {
                    var l = kv.Value;
                    if (l == null) continue;
                    for (int i = 0; i < l.Count; i++)
                    {
                        var p = l[i];
                        if (p == null || p.Size < 0.5f) continue;
                        num += p.Radicalism * p.Size; den += p.Size;
                    }
                }
                return den > 0f ? num / den : 0f;
            }
            catch { return 0f; }
        }

        // ================= 输出给经济系统的乘数 =================
        internal static float TaxIncomeMult()
        {
            try
            {
                float m = 1f - 0.06f * Laws[1] - 0.03f * Laws[3] + (SeatHeld(2) ? 0.03f : 0f);
                m *= LawSystem.ExtraTaxMult();   // v5.0-P22: 权力结构(集权)提升税收效率
                m *= WarMobilization.TaxMult();  // v5.0-P25: 动员期间税收 -10%
                m *= Research.TaxMult() * Institutions.TaxMult();   // v5.0-P26: 研究/官僚机构
                if (CivilWar) m *= 0.6f;
                return ClampF(m, 0.3f, 1.3f);
            }
            catch { return 1f; }
        }

        internal static float RadicalDaily()
        {
            try
            {
                float d = -0.001f * Laws[1] - 0.001f * Laws[5] - (SeatHeld(4) ? 0.0005f : 0f);
                d += LawSystem.CivilRadicalDaily();   // v5.0-P22: 平民权利压制激进
                d += Research.RadicalDelta();         // v5.0-P26: 学识研究
                if (CivilWar) d += 0.004f;
                if (UltimatumActive) d += 0.001f;
                return d;
            }
            catch { return 0f; }
        }

        internal static float OutputMult(string categoryName)
        {
            try
            {
                float m;
                if (categoryName == "军事")
                    m = (LawSystem.MilitaryMult() + (SeatHeld(3) ? 0.04f : 0f)) * WarMobilization.MilitaryMult() * PowerBlocs.MilitaryMult();   // v4.133/v5.0-P25/P27
                else if (categoryName == "资源")
                    m = (CivilWar ? 0.85f : 1f) * LawSystem.AgriOutputMult();    // v4.133: 土地制度
                else
                    m = CivilWar ? 0.85f : 1f;
                return m * Research.OutputMult();   // v5.0-P26: 工程研究
            }
            catch { return 1f; }
        }

        // v4.140: 合法性重算(月度结算 + 组阁/选举变更时即时调用; 用户反馈: 出阁后合法性要立即变化)
        internal static void RecomputeLegitimacy()
        {
            try
            {
                float taxOverAll = Math.Max(0f, TaxPolicy.Level[0] + TaxPolicy.Level[1] + TaxPolicy.Level[2] + TaxPolicy.Level[3] - 8f);
                float legit = InterestGroups.LegitimacyTerms() + Laws[0] * 2.5f + ChurchAttitude / 5f - Tyranny - taxOverAll * 2f;
                if (CivilWar) legit -= 10f;
                if (Treasury() > 10000) legit += 3f;
                if (Treasury() < 0) legit -= 5f;
                if (SeatHeld(1)) legit += 2f;   // 掌玺大臣(文档 21.3)
                Legitimacy = ClampF(legit, 0f, 100f);
            }
            catch { }
        }

        internal static float MonthRegen()
        {
            try
            {
                float r = 10f + Legitimacy / 10f - Tyranny * 0.5f;
                if (Legitimacy < 30f) r *= 0.5f;
                if (SeatHeld(0)) r += 2f;
                r += Laws[5];
                r += LawSystem.ExtraMonthRegen();   // v5.0-P22: 权力结构(集权)权威恢复
                r += Research.RegenBonus();         // v5.0-P26: 行政研究
                return r;
            }
            catch { return 10f; }
        }

        internal static bool SeatHeld(int seat)
        {
            try { return seat >= 0 && seat < 5 && !string.IsNullOrEmpty(Council[seat]) && Lords.ContainsKey(Council[seat]); }
            catch { return false; }
        }

        internal static float CrownClout()
        {
            try { return 80f + Authority * 0.3f + Math.Max(0f, (float)Treasury()) / 5000f; }
            catch { return 100f; }
        }

        // ================= 领主操作 =================
        internal static string Feast()
        {
            try
            {
                if (EconomyWorld.Treasury.Gold < 1500) return "国库不足(需 1500 第纳尔)";
                EconomyWorld.TreasurySpend(1500);
                Fiscal.AddCourt(1500);
                foreach (var kv in Lords) { kv.Value.Attitude = Clamp(kv.Value.Attitude + 6, -100, 100); kv.Value.Anger = Clamp(kv.Value.Anger - 5, 0, 100); }
                Aggregate();
                DLog.Force("政治: 举办宴会(全体领主态度 +6)");
                return "宴会已举办: 全体领主态度 +6, 愤怒 -5";
            }
            catch { return "宴会失败"; }
        }

        internal static string Patrol()
        {
            try
            {
                if (EconomyWorld.Treasury.Gold < 800) return "国库不足(需 800 第纳尔)";
                EconomyWorld.TreasurySpend(800);
                Fiscal.AddCourt(800);
                Legitimacy = ClampF(Legitimacy + 1f, 0f, 100f);
                Pops.ShiftRadicals(-0.005f);
                DLog.Force("政治: 王室巡游(合法性 +1, 激进 -0.5%)");
                return "王室巡游: 合法性 +1, 全国激进 -0.5%";
            }
            catch { return "巡游失败"; }
        }

        internal static string Grant(string clanId)
        {
            try
            {
                LordProfile lp;
                if (clanId == null || !Lords.TryGetValue(clanId, out lp)) return "领主不存在";
                if (EconomyWorld.Treasury.Gold < 500) return "国库不足(需 500 第纳尔)";
                EconomyWorld.TreasurySpend(500);
                Fiscal.AddCourt(500);
                Ownership.LordPool += 500f;
                lp.Attitude = Clamp(lp.Attitude + 8, -100, 100);
                lp.Anger = Clamp(lp.Anger - 6, 0, 100);
                Aggregate();
                return "已赏赐 " + lp.Name + ": 态度 +8, 愤怒 -6";
            }
            catch { return "赏赐失败"; }
        }

        internal static string Honor(string clanId)
        {
            try
            {
                LordProfile lp;
                if (clanId == null || !Lords.TryGetValue(clanId, out lp)) return "领主不存在";
                if (Authority < 30f) return "权威不足(需 30)";
                Authority -= 30f;
                lp.Attitude = Clamp(lp.Attitude + 15, -100, 100);
                lp.Anger = Clamp(lp.Anger - 10, 0, 100);
                lp.TitleBonus += 10;
                lp.Clout += 10;
                Aggregate();
                return "已授予 " + lp.Name + " 头衔: 态度 +15, 势力 +10";
            }
            catch { return "授勋失败"; }
        }

        internal static string Appoint(string clanId)
        {
            try
            {
                LordProfile lp;
                if (clanId == null || !Lords.TryGetValue(clanId, out lp)) return "领主不存在";
                if (lp.Seat >= 0) return lp.Name + " 已在 " + SeatNames[lp.Seat] + " 任上";
                int seat = -1;
                for (int i = 0; i < 5; i++) if (string.IsNullOrEmpty(Council[i])) { seat = i; break; }
                if (seat < 0) return "御前会议无空缺(先罢免他人)";
                if (Authority < 30f) return "权威不足(需 30)";
                Authority -= 30f;
                Council[seat] = lp.ClanId;
                lp.Seat = seat;
                lp.Attitude = Clamp(lp.Attitude + 20, -100, 100);
                lp.Anger = Clamp(lp.Anger - 10, 0, 100);
                Aggregate();
                return "已任命 " + lp.Name + " 为 " + SeatNames[seat] + "(" + SeatBonusText[seat] + ")";
            }
            catch { return "任命失败"; }
        }

        internal static string Dismiss(string clanId)
        {
            try
            {
                LordProfile lp;
                if (clanId == null || !Lords.TryGetValue(clanId, out lp)) return "领主不存在";
                if (lp.Seat < 0) return lp.Name + " 未在御前会议任职";
                string seatName = SeatNames[lp.Seat];
                Council[lp.Seat] = "";
                lp.Seat = -1;
                lp.Attitude = Clamp(lp.Attitude - 20, -100, 100);
                lp.Anger = Clamp(lp.Anger + 10, 0, 100);
                foreach (var kv in Lords) kv.Value.Fear = Clamp(kv.Value.Fear + 3, 0, 100);
                Aggregate();
                return "已罢免 " + lp.Name + " 的" + seatName + "之职(态度 -20)";
            }
            catch { return "罢免失败"; }
        }

        // 处决(文档 21.8): 威慑剧增, 暴政/合法性重创; 走原版处决接口
        internal static string Execute(string clanId)
        {
            try
            {
                LordProfile lp;
                if (clanId == null || !Lords.TryGetValue(clanId, out lp)) return "领主不存在";
                var k = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                if (k == null) return "没有国家";
                Hero victim = null;
                foreach (var c in k.Clans)
                {
                    if (c != null && c.StringId == clanId) { victim = c.Leader; break; }
                }
                string name = lp.Name;
                if (victim != null)
                {
                    try { KillCharacterAction.ApplyByExecution(victim, Hero.MainHero, true); }
                    catch (Exception ex) { DLog.Force("政治: 原版处决失败(" + ex.Message + "), 仅结算政治影响"); }
                }
                lp.Attitude = Clamp(lp.Attitude - 50, -100, 100);
                lp.Anger = Clamp(lp.Anger + 30, 0, 100);
                foreach (var kv in Lords) kv.Value.Fear = Clamp(kv.Value.Fear + 15, 0, 100);
                Tyranny += 8f;
                Legitimacy = ClampF(Legitimacy - 5f, 0f, 100f);
                InterestGroups.AddEventMod(1, -18);   // v5.0-P22: 大贵族震怒
                InterestGroups.AddEventMod(7, 6);     // 军官团叫好
                Notify("已处决 " + name + "(暴政 +8, 全体领主恐惧 +15)", true);
                DLog.Force("政治: 处决 " + name);
                return "已处决 " + name + ": 暴政 +8, 恐惧 +15, 合法性 -5";
            }
            catch { return "处决失败"; }
        }

        // ================= 请愿处理 =================
        internal static string Respond(int idx, bool accept, int day)
        {
            try
            {
                if (idx < 0 || idx >= Petitions.Count) return "";
                var p = Petitions[idx];
                Petitions.RemoveAt(idx);
                string head = accept ? "已同意: " : "已拒绝: ";
                if (accept) AcceptEffect(p, day);
                else RefuseEffect(p, false);
                Aggregate();
                return head + p.Text;
            }
            catch { return "处理失败"; }
        }

        private static void AcceptEffect(Petition p, int day)
        {
            try
            {
                switch (p.Kind)
                {
                    case "tax_cut":
                        for (int i = 0; i < 4; i++) if (TaxPolicy.Level[i] > 0) { TaxPolicy.Level[i]--; }
                        TaxPolicy.Recompute();
                        Legitimacy = ClampF(Legitimacy + 2f, 0f, 100f);
                        DLog.Force("政治: 同意减税请愿 -> 四税各降一档");
                        break;
                    case "compensation":
                        if (EconomyWorld.Treasury.Gold >= 3000) { EconomyWorld.TreasurySpend(3000); Fiscal.AddCourt(3000); }
                        Ownership.LordPool += 3000f;
                        if (p.ClanId != null) TouchLord(p.ClanId, 15, -15);
                        DLog.Force("政治: 同意赔偿请愿(3000)");
                        break;
                    case "price_control":
                        MintRight.SetPurity(0);
                        BurgerAngerOffset = Clamp(BurgerAngerOffset - 15, -40, 100);
                        DLog.Force("政治: 同意物价请愿 -> 铸币成色回到足银");
                        break;
                    case "charter":
                        { string r = Guilds.GrantCharterFree(); DLog.Force("政治: 同意特许请愿 -> " + r); }
                        break;
                    case "tithe_raise":
                        TaxPolicy.SetLevel(2, Math.Min(4, TaxPolicy.Level[2] + 1), day);
                        DLog.Force("政治: 同意提高什一税");
                        break;
                    case "title":
                        if (Authority >= 40f) Authority -= 40f; else Authority = 0f;
                        if (p.ClanId != null) TouchLord(p.ClanId, 20, -15);
                        DLog.Force("政治: 同意授勋请愿(-40 权威)");
                        break;
                }
            }
            catch { }
        }

        private static void RefuseEffect(Petition p, bool expired)
        {
            try
            {
                int d = expired ? 4 : 8;
                switch (p.Estate)
                {
                    case 0: NobleAnger = Clamp(NobleAnger + d, 0, 100); break;
                    case 1: ChurchAngerOffset += d; break;                  // 持久偏移(月结衰减; 修: 不再被重算覆盖)
                    default: BurgerAngerOffset += d; break;
                }
                if (p.ClanId != null) TouchLord(p.ClanId, -8, d);
                Legitimacy = ClampF(Legitimacy - (expired ? 1f : 2f), 0f, 100f);
                DLog.Force("政治: " + (expired ? "请愿逾期作废" : "拒绝请愿") + " -> " + p.Text);
            }
            catch { }
        }

        private static void TouchLord(string clanId, int att, int anger)
        {
            try
            {
                LordProfile lp;
                if (clanId != null && Lords.TryGetValue(clanId, out lp))
                {
                    lp.Attitude = Clamp(lp.Attitude + att, -100, 100);
                    lp.Anger = Clamp(lp.Anger + anger, 0, 100);
                }
            }
            catch { }
        }

        // ================= 法令 =================
        internal static int LawCost(int cat)
        {
            try { return 60 + (Laws[cat] + 1) * 40; } catch { return 100; }
        }

        internal static int SupportPct(int cat)
        {
            try
            {
                long yes = 0, all = 0;
                foreach (var kv in Lords)
                {
                    var w = Math.Max(1, kv.Value.Clout);
                    all += w;
                    if (VoteYes(cat, kv.Value)) yes += w;
                }
                return all > 0 ? (int)(100L * yes / all) : 0;
            }
            catch { return 0; }
        }

        // 表决名单(修: 未知投谁)
        internal static string VoteSummary(int cat)
        {
            try
            {
                var yes = new List<string>();
                var no = new List<string>();
                foreach (var kv in Lords)
                {
                    if (VoteYes(cat, kv.Value)) yes.Add(kv.Value.Name);
                    else no.Add(kv.Value.Name);
                }
                string ys = JoinNames(yes);
                string ns = JoinNames(no);
                string s = "赞成 " + SupportPct(cat) + "%: " + (ys.Length > 0 ? ys : "无");
                if (ns.Length > 0) s += " · 反对: " + ns;
                return s;
            }
            catch { return ""; }
        }

        private static string JoinNames(List<string> l)
        {
            if (l == null || l.Count == 0) return "";
            var sb = new StringBuilder();
            for (int i = 0; i < l.Count && i < 4; i++)
            {
                if (i > 0) sb.Append('、');
                sb.Append(l[i]);
            }
            if (l.Count > 4) sb.Append(" 等" + l.Count + "家");
            return sb.ToString();
        }

        private static bool VoteYes(int cat, LordProfile lp)
        {
            int score = lp.Attitude + (lp.Seat >= 0 ? 25 : 0);
            switch (lp.Tendency)
            {
                case 1:   // 贵族
                    if (cat == 1) score += 20;
                    if (cat == 2) score -= 15;
                    if (cat == 4) score -= 10;
                    if (cat == 5) score -= 25;
                    if (cat == 0) score += 5;
                    break;
                case 2:   // 教权
                    if (cat == 3) score += 30;
                    if (cat == 1) score += 10;
                    if (cat == 5) score += 10;
                    break;
                case 3:   // 市民
                    if (cat == 4) score += 25;
                    if (cat == 5) score += 5;
                    if (cat == 1) score -= 15;
                    break;
                default:  // 王室派
                    score += 15;
                    break;
            }
            if (lp.Anger >= 75) score -= 40;   // 愤怒领主阻挠一切
            return score >= 10;
        }

        // v5.0-P22: 旧 6 法令表决 -> 新法律体系立案(文档 24.3); cat 为旧索引(0..5)
        internal static string ProposeLaw(int cat, bool force, int day)
        {
            try
            {
                if (cat < 0 || cat > 5) return "";
                int law = LawSystem.LegacyToLaw(cat);
                if (law < 0) return LawNames[cat] + " 无法案通道";
                return LawSystem.StartBill(law, force);
            }
            catch { return "立法失败"; }
        }

        // ================= 内战处理 =================
        internal static string Suppress()
        {
            try
            {
                if (!CivilWar) return "当前没有内战";
                if (EconomyWorld.Treasury.Gold < 5000) return "国库不足(需 5000 第纳尔)";
                if (Authority < 100f) return "权威不足(需 100)";
                EconomyWorld.TreasurySpend(5000);
                Fiscal.AddCourt(5000);
                Authority -= 100f;
                CivilWar = false;
                foreach (var kv in Lords) { kv.Value.Anger = Clamp(kv.Value.Anger + 10, 0, 100); kv.Value.Fear = Clamp(kv.Value.Fear + 20, 0, 100); }
                Pops.ShiftRadicals(-0.05f);
                Legitimacy = ClampF(Legitimacy + 2f, 0f, 100f);
                for (int g = 0; g < 8; g++) InterestGroups.AddEventMod(g, -6);   // v5.0-P22: 血腥镇压
                InterestGroups.AddEventMod(7, 8);
                Aggregate();
                Notify("已镇压内战: 领主恐惧 +20", true);
                DLog.Force("政治: 镇压内战(5000 第纳尔 + 100 权威)");
                return "已镇压叛乱: 领主恐惧 +20(态度与合法性将随恐惧重建)";
            }
            catch { return "镇压失败"; }
        }

        internal static string Concede()
        {
            try
            {
                if (!CivilWar) return "当前没有内战";
                if (EconomyWorld.Treasury.Gold < 3000) return "国库不足(需 3000 第纳尔)";
                EconomyWorld.TreasurySpend(3000);
                Fiscal.AddCourt(3000);
                CivilWar = false;
                foreach (var kv in Lords) { kv.Value.Anger = Clamp(kv.Value.Anger - 30, 0, 100); kv.Value.Attitude = Clamp(kv.Value.Attitude + 8, -100, 100); }
                Authority = Math.Max(0f, Authority - 40f);
                Legitimacy = ClampF(Legitimacy + 5f, 0f, 100f);
                for (int g = 0; g < 8; g++) InterestGroups.AddEventMod(g, 4);   // v5.0-P22: 王室让步
                InterestGroups.AddEventMod(1, 8);
                Aggregate();
                Notify("已妥协: 内战结束", false);
                DLog.Force("政治: 妥协结束内战(3000 第纳尔, 领主愤怒 -30)");
                return "已妥协: 内战结束(权威 -40, 领主愤怒 -30)";
            }
            catch { return "妥协失败"; }
        }

        // ================= 外部事件钩子 =================
        internal static void OnConfiscate(string clanId)
        {
            try
            {
                LordProfile lp;
                if (clanId != null && Lords.TryGetValue(clanId, out lp))
                {
                    lp.Anger = Clamp(lp.Anger + 25, 0, 100);
                    lp.Attitude = Clamp(lp.Attitude - 25, -100, 100);
                }
                Tyranny += 3f;
                foreach (var kv in Lords) kv.Value.Fear = Clamp(kv.Value.Fear + 5, 0, 100);
                InterestGroups.AddEventMod(1, -10);   // v5.0-P22: 没收触动大贵族
                DLog.Force("政治: 没收引发领主愤怒(暴政 +3)");
            }
            catch { }
        }

        internal static void OnBankruptcy()
        {
            try
            {
                foreach (var kv in Lords) kv.Value.Anger = Clamp(kv.Value.Anger + 8, 0, 100);
                Legitimacy = ClampF(Legitimacy - 3f, 0f, 100f);
                DLog.Force("政治: 财政破产(全体领主愤怒 +8, 合法性 -3)");
            }
            catch { }
        }

        internal static void OnLordDividend(int amount)
        {
            try { _dividendAccum += Math.Max(0, amount); } catch { }
        }

        // ================= 通知 =================
        private static void Notify(string msg, bool alert)
        {
            try
            {
                var c = TaleWorlds.Library.Color.FromUint(alert ? 4294901760U : 4294953344U);
                InformationManager.DisplayMessage(new InformationMessage(msg, c));
            }
            catch { }
        }

        // ================= 存档 FIA_Politics =================
        internal static string Save()
        {
            try
            {
                var sb = new StringBuilder();
                sb.Append("v2;");
                sb.Append(Authority.ToString("F1", CultureInfo.InvariantCulture)).Append(',')
                  .Append(Legitimacy.ToString("F1", CultureInfo.InvariantCulture)).Append(',')
                  .Append(Tyranny.ToString("F1", CultureInfo.InvariantCulture)).Append(',')
                  .Append(CivilWar ? "1" : "0").Append(',')
                  .Append(CivilWarDay).Append(',')
                  .Append(ChurchAngerOffset).Append(',')
                  .Append(BurgerAngerOffset).Append(',')
                  .Append(UltimatumActive ? "1" : "0").Append(',')
                  .Append(UltimatumDay).Append(',')
                  .Append(UltimatumKind).Append(',')
                  .Append(LastRebellionDay).Append(';');
                for (int i = 0; i < 6; i++) { if (i > 0) sb.Append(','); sb.Append(Laws[i]); }
                sb.Append(';');
                for (int i = 0; i < 5; i++) { if (i > 0) sb.Append(','); sb.Append(Council[i] ?? ""); }
                sb.Append(';');
                for (int i = 0; i < Petitions.Count; i++)
                {
                    var p = Petitions[i];
                    if (i > 0) sb.Append('|');
                    sb.Append(p.Kind).Append(',').Append(p.Estate).Append(',').Append(p.ClanId ?? "").Append(',').Append(p.Expire);
                }
                sb.Append(';');
                foreach (var kv in Lords)
                {
                    var lp = kv.Value;
                    sb.Append(lp.ClanId).Append(',').Append(lp.Attitude).Append(',').Append(lp.Anger).Append(',')
                      .Append(lp.Fear).Append(',').Append(lp.Seat).Append(',').Append(lp.Tendency).Append(',').Append(lp.TitleBonus).Append(';');
                }
                return sb.ToString();
            }
            catch { return ""; }
        }

        internal static void Load(string data)
        {
            try
            {
                if (string.IsNullOrEmpty(data)) return;
                var seg = data.Split(';');
                if (seg.Length > 1)
                {
                    var f = seg[1].Split(',');
                    float v;
                    int x;
                    if (f.Length > 0 && float.TryParse(f[0], NumberStyles.Float, CultureInfo.InvariantCulture, out v)) Authority = v;
                    if (f.Length > 1 && float.TryParse(f[1], NumberStyles.Float, CultureInfo.InvariantCulture, out v)) Legitimacy = v;
                    if (f.Length > 2 && float.TryParse(f[2], NumberStyles.Float, CultureInfo.InvariantCulture, out v)) Tyranny = v;
                    if (f.Length > 3) CivilWar = f[3] == "1";
                    if (f.Length > 4 && int.TryParse(f[4], out x)) CivilWarDay = x;
                    if (f.Length > 5 && int.TryParse(f[5], out x)) ChurchAngerOffset = x;
                    if (f.Length > 6 && int.TryParse(f[6], out x)) BurgerAngerOffset = x;
                    if (f.Length > 7) UltimatumActive = f[7] == "1";
                    if (f.Length > 8 && int.TryParse(f[8], out x)) UltimatumDay = x;
                    if (f.Length > 9 && int.TryParse(f[9], out x)) UltimatumKind = x;
                    if (f.Length > 10 && int.TryParse(f[10], out x)) LastRebellionDay = x;
                }
                if (seg.Length > 2)
                {
                    var lv = seg[2].Split(',');
                    for (int i = 0; i < 6 && i < lv.Length; i++)
                    {
                        int x;
                        if (int.TryParse(lv[i], out x)) Laws[i] = Clamp(x, 0, 3);
                    }
                }
                if (seg.Length > 3)
                {
                    var cv = seg[3].Split(',');
                    for (int i = 0; i < 5 && i < cv.Length; i++) Council[i] = cv[i];
                }
                Petitions.Clear();
                if (seg.Length > 4 && !string.IsNullOrEmpty(seg[4]))
                {
                    foreach (var line in seg[4].Split('|'))
                    {
                        if (string.IsNullOrEmpty(line)) continue;
                        var f = line.Split(',');
                        if (f.Length < 4) continue;
                        int est, exp;
                        int.TryParse(f[1], out est);
                        int.TryParse(f[3], out exp);
                        Petitions.Add(new Petition { Kind = f[0], Estate = est, ClanId = string.IsNullOrEmpty(f[2]) ? null : f[2], Expire = exp });
                    }
                }
                if (seg.Length > 5)
                {
                    for (int i = 5; i < seg.Length; i++)
                    {
                        if (string.IsNullOrEmpty(seg[i])) continue;
                        var f = seg[i].Split(',');
                        if (f.Length < 7) continue;
                        var lp = new LordProfile { ClanId = f[0] };
                        int x;
                        if (int.TryParse(f[1], out x)) lp.Attitude = x;
                        if (int.TryParse(f[2], out x)) lp.Anger = x;
                        if (int.TryParse(f[3], out x)) lp.Fear = x;
                        if (int.TryParse(f[4], out x)) lp.Seat = x;
                        if (int.TryParse(f[5], out x)) lp.Tendency = x;
                        if (int.TryParse(f[6], out x)) lp.TitleBonus = x;
                        Lords[f[0]] = lp;
                    }
                }
                // 补请愿文本(存档只存 kind)
                for (int i = 0; i < Petitions.Count; i++) Petitions[i].Text = PetitionText(Petitions[i]);
                // v5.0-P22: 旧 6 法令并入法律体系(FIA_Laws 未读档时迁移, 之后同步回 Laws[])
                LawSystem.MigrateLegacy(Laws);
                LawSystem.SyncLegacy();
                DLog.Force("政治: 读档 权威=" + (int)Authority + " 合法性=" + (int)Legitimacy + " 领主=" + Lords.Count + " 请愿=" + Petitions.Count);
            }
            catch { }
        }

        private static string PetitionText(Petition p)
        {
            switch (p.Kind)
            {
                case "tax_cut": return "领主们联名请愿: 税负过重, 请求减税";
                case "compensation": return "被没收产业的领主请求赔偿(3000)";
                case "price_control": return "市民请愿: 物价飞涨, 请求货币足银";
                case "charter": return "行会请愿: 请求王室授予首份特许状(免费)";
                case "tithe_raise": return "教会请愿: 教产入不敷出, 请求提高什一税";
                case "title":
                    {
                        LordProfile lp;
                        if (p.ClanId != null && Lords.TryGetValue(p.ClanId, out lp)) return lp.Name + " 请求授予头衔";
                        return "一名领主请求授予头衔";
                    }
            }
            return "请愿";
        }

        // ================= 工具 =================
        private static int Treasury()
        {
            try { return (int)EconomyWorld.Treasury.Gold; } catch { return 0; }
        }

        internal static int Today()
        {
            try { return (int)CampaignTime.Now.ToDays; } catch { return 0; }
        }

        internal static int Clamp(int v, int lo, int hi) { return v < lo ? lo : (v > hi ? hi : v); }
        internal static float ClampF(float v, float lo, float hi) { return v < lo ? lo : (v > hi ? hi : v); }
    }
}
