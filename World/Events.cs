using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace FeudalInternalAffairs
{
    // 事件选项
    internal class EventChoice
    {
        internal string Text;        // 选项文案
        internal string Effect;      // 效果说明(界面显示)
        internal int Gold;           // 立即金币(负 = 支出)
        internal float Authority, Legitimacy, Tyranny;
        internal float Prosperity;   // 立即城镇繁荣(我方全部城镇)
        internal int[] Att;          // 8 集团态度
        internal float LowSatis;     // 下层(农民+行会)满意度
        internal float ArmySatis;
    }

    // 事件定义
    internal class EventDef
    {
        internal string Id, Name, Icon, Desc;
        internal EventChoice[] Choices;
    }

    // 随机事件(文档 24.x; v4.197): 日结按概率触发 -> 弹出带图抉择面板 -> 选项结算
    internal static class Events
    {
        internal const int CooldownDays = 21;      // 两次事件最小间隔
        internal const float DailyChance = 0.03f;  // 每日触发概率

        internal static readonly List<EventDef> All = new List<EventDef>
        {
            new EventDef
            {
                Id = "fire", Name = "城市大火", Icon = "fia_event_fire",
                Desc = "夜里一处作坊走水, 火借风势烧了半条街。灾民挤在城外, 商会与行会都在看你如何处置。",
                Choices = new[]
                {
                    new EventChoice { Text = "出钱赈灾", Effect = "金币 -5000, 下层 +8, 城镇繁荣 -2", Gold = -5000, LowSatis = 8, Prosperity = -2f },
                    new EventChoice { Text = "令市民自救", Effect = "下层 -8, 城镇繁荣 -4, 权威 +1", LowSatis = -8, Prosperity = -4f, Authority = 1f }
                }
            },
            new EventDef
            {
                Id = "protest", Name = "街头抗议", Icon = "fia_event_protest",
                Desc = "粮价上涨, 城里聚起数千人, 有人举着牌子喊你的名字。军队在等命令。",
                Choices = new[]
                {
                    new EventChoice { Text = "出动军队弹压", Effect = "下层 -10, 军队 +6, 暴政 +1, 合法性 +1", LowSatis = -10, ArmySatis = 6, Tyranny = 1f, Legitimacy = 1f },
                    new EventChoice { Text = "与领头人谈判", Effect = "下层 +8, 权威 -20, 合法性 +2", LowSatis = 8, Authority = -20f, Legitimacy = 2f },
                    new EventChoice { Text = "置之不理", Effect = "下层 -6, 权威 +0", LowSatis = -6 }
                }
            },
            new EventDef
            {
                Id = "industry", Name = "工业博览会", Icon = "fia_event_industry",
                Desc = "行会与商帮联合筹办博览会, 请你出面赞助。会场会挂上你的徽记。",
                Choices = new[]
                {
                    new EventChoice { Text = "出资赞助", Effect = "金币 -3000, 行会/商帮 +8, 城镇繁荣 +3", Gold = -3000, Att = new[] { 0, 0, 0, 0, 8, 8, 0, 0 }, Prosperity = 3f },
                    new EventChoice { Text = "婉言推辞", Effect = "行会 -4, 商帮 -2", Att = new[] { 0, 0, 0, 0, -2, -4, 0, 0 } }
                }
            },
            new EventDef
            {
                Id = "newspaper", Name = "报纸风波", Icon = "fia_event_newspaper",
                Desc = "一家报馆刊文批评你的税制, 抄本一夜之间传遍全城。",
                Choices = new[]
                {
                    new EventChoice { Text = "收买报馆", Effect = "金币 -2000, 下层 -4, 权威 +2", Gold = -2000, LowSatis = -4, Authority = 2f },
                    new EventChoice { Text = "允许自由报道", Effect = "下层 +6, 合法性 +2, 权威 -1", LowSatis = 6, Legitimacy = 2f, Authority = -1f }
                }
            },
            new EventDef
            {
                Id = "trade", Name = "商路机遇", Icon = "fia_event_trade",
                Desc = "一支远方商队带来新的货源, 商帮希望官府出资护送商路。",
                Choices = new[]
                {
                    new EventChoice { Text = "投资商路", Effect = "金币 -4000, 商帮 +10, 城镇繁荣 +2", Gold = -4000, Att = new[] { 0, 0, 0, 0, 10, 4, 0, 0 }, Prosperity = 2f },
                    new EventChoice { Text = "维持现状", Effect = "商帮 -4", Att = new[] { 0, 0, 0, 0, -4, 0, 0, 0 } }
                }
            },
            new EventDef
            {
                Id = "election", Name = "选举集会", Icon = "fia_event_election",
                Desc = "选举临近, 各派在广场搭台演说, 都在争取你的背书。",
                Choices = new[]
                {
                    new EventChoice { Text = "公开支持一派", Effect = "合法性 +3, 权威 -15, 该派集团 +6", Legitimacy = 3f, Authority = -15f, Att = new[] { 4, 0, 0, 0, 0, 0, 6, 0 } },
                    new EventChoice { Text = "保持中立", Effect = "合法性 -2, 权威 +1", Legitimacy = -2f, Authority = 1f }
                }
            },
            new EventDef
            {
                Id = "portrait", Name = "御容画像", Icon = "fia_event_portrait",
                Desc = "教会提议为你绘制巨幅御容, 悬挂于大教堂与市政厅。",
                Choices = new[]
                {
                    new EventChoice { Text = "重金绘制", Effect = "金币 -2500, 合法性 +4, 教会 +4", Gold = -2500, Legitimacy = 4f, Att = new[] { 0, 0, 0, 4, 0, 0, 0, 0 } },
                    new EventChoice { Text = "一切从简", Effect = "合法性 -1, 教会 -2", Legitimacy = -1f, Att = new[] { 0, 0, 0, -2, 0, 0, 0, 0 } }
                }
            },
            new EventDef
            {
                Id = "scales", Name = "司法争议", Icon = "fia_event_scales",
                Desc = "一桩命案牵涉权贵子弟, 民间都在看你的判决是否公道。",
                Choices = new[]
                {
                    new EventChoice { Text = "依法严惩", Effect = "金币 -1500, 合法性 +3, 权贵 -8, 下层 +4", Gold = -1500, Legitimacy = 3f, Att = new[] { 0, -8, 0, 0, 0, 0, 4, 0 } },
                    new EventChoice { Text = "网开一面", Effect = "权贵 +8, 下层 -6, 合法性 -2", Att = new[] { 0, 8, 4, 0, 0, 0, -6, 0 }, Legitimacy = -2f }
                }
            },
            new EventDef
            {
                Id = "skull", Name = "瘟疫", Icon = "fia_event_skull",
                Desc = "港口城市出现恶疾, 死者日增。医师说必须封锁城门, 商帮坚决反对。",
                Choices = new[]
                {
                    new EventChoice { Text = "封锁并施药", Effect = "金币 -6000, 下层 +4, 商帮 -8, 城镇繁荣 -2", Gold = -6000, LowSatis = 4, Att = new[] { 0, 0, 0, 4, -8, 0, 4, 0 }, Prosperity = -2f },
                    new EventChoice { Text = "维持商路", Effect = "金币 -2000, 下层 -12, 城镇繁荣 -5, 商帮 +6", Gold = -2000, LowSatis = -12, Att = new[] { 0, 0, 0, -4, 6, 0, -6, 0 }, Prosperity = -5f }
                }
            },
            new EventDef
            {
                Id = "flag", Name = "边境摩擦", Icon = "fia_event_flag",
                Desc = "邻国巡逻队越界骚扰村庄, 地方绅士请求增兵。",
                Choices = new[]
                {
                    new EventChoice { Text = "增兵边境", Effect = "金币 -3000, 军队 +8, 权威 +3", Gold = -3000, ArmySatis = 8, Authority = 3f },
                    new EventChoice { Text = "外交交涉", Effect = "合法性 +2, 权威 -10, 地方 +4", Legitimacy = 2f, Authority = -10f, Att = new[] { 0, 0, 4, 0, 0, 0, 0, 0 } }
                }
            },
            new EventDef
            {
                Id = "default", Name = "地方请愿", Icon = "fia_event_default",
                Desc = "几个村庄的乡老进城请愿, 说水利失修、赋税过重。",
                Choices = new[]
                {
                    new EventChoice { Text = "拨款修水利", Effect = "金币 -2000, 下层 +6, 地方 +4", Gold = -2000, LowSatis = 6, Att = new[] { 0, 0, 4, 0, 0, 0, 4, 0 } },
                    new EventChoice { Text = "驳回请愿", Effect = "下层 -5, 权威 +2", LowSatis = -5, Authority = 2f }
                }
            }
        };

        // 当前待处理事件(面板关闭后清空)
        internal static EventDef Pending;
        private static int _lastDay = -9999;

        internal static EventDef Def(string id)
        {
            for (int i = 0; i < All.Count; i++) if (All[i].Id == id) return All[i];
            return null;
        }

        // 日结: 概率触发(带冷却); 触发后由 EventPanel 打开面板
        internal static void Daily(int day)
        {
            try
            {
                if (Pending != null) return;                       // 上一个还没处理
                if (day - _lastDay < CooldownDays) return;
                var rnd = new Random(day * 7919 + 13);
                if (rnd.NextDouble() > DailyChance) return;
                Pending = PickByState(rnd);
                if (Pending == null) return;
                _lastDay = day;
                DLog.Force("事件: 触发 " + Pending.Name);
                try { EventPanel.Open(); } catch { }
            }
            catch { }
        }

        // 按国情加权(低满意 -> 抗议/瘟疫; 高繁荣 -> 博览会/商路; 选举期 -> 选举集会)
        private static EventDef PickByState(Random rnd)
        {
            try
            {
                var pool = new List<EventDef>();
                float worst = 100f;
                for (int g = 0; g < InterestGroups.GroupCount; g++) { float s = InterestGroups.SatOf(g); if (s < worst) worst = s; }
                bool electing = false;
                try { electing = Elections.CampaignActive; } catch { }
                if (worst < 35f) { pool.Add(Def("protest")); pool.Add(Def("skull")); }
                if (electing) pool.Add(Def("election"));
                pool.Add(Def("fire")); pool.Add(Def("trade")); pool.Add(Def("industry"));
                pool.Add(Def("newspaper")); pool.Add(Def("scales")); pool.Add(Def("portrait"));
                pool.Add(Def("flag")); pool.Add(Def("default"));
                var ok = new List<EventDef>();
                for (int i = 0; i < pool.Count; i++) if (pool[i] != null) ok.Add(pool[i]);
                if (ok.Count == 0) return Def("default");
                return ok[rnd.Next(ok.Count)];
            }
            catch { return Def("default"); }
        }

        // 选择结算
        internal static string Apply(int choiceIdx)
        {
            try
            {
                var e = Pending;
                if (e == null || e.Choices == null) return "";
                if (choiceIdx < 0 || choiceIdx >= e.Choices.Length) return "";
                var c = e.Choices[choiceIdx];
                if (c.Gold != 0)
                {
                    try { if (c.Gold < 0) EconomyWorld.TreasurySpend(-c.Gold); else EconomyWorld.TreasuryAdd(c.Gold); } catch { }
                }
                if (Math.Abs(c.Authority) > 0.01f) Politics.Authority = Math.Max(0f, Politics.Authority + c.Authority);
                if (Math.Abs(c.Legitimacy) > 0.01f) Politics.Legitimacy = Politics.ClampF(Politics.Legitimacy + c.Legitimacy, 0f, 100f);
                if (Math.Abs(c.Tyranny) > 0.01f) Politics.Tyranny += c.Tyranny;
                if (Math.Abs(c.LowSatis) > 0.01f) { InterestGroups.AddEventMod(6, (int)Math.Round(c.LowSatis)); InterestGroups.AddEventMod(5, (int)Math.Round(c.LowSatis * 0.5f)); }
                if (Math.Abs(c.ArmySatis) > 0.01f) InterestGroups.AddEventMod(7, (int)Math.Round(c.ArmySatis));
                if (c.Att != null)
                    for (int g = 0; g < c.Att.Length && g < InterestGroups.GroupCount; g++)
                        if (c.Att[g] != 0) InterestGroups.AddEventMod(g, c.Att[g]);
                if (Math.Abs(c.Prosperity) > 0.01f)
                {
                    var pk = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                    if (pk != null)
                        foreach (var s in pk.Settlements)
                            if (s != null && s.IsTown && s.Town != null) s.Town.Prosperity += c.Prosperity;
                }
                string msg = "事件「" + e.Name + "」-> " + c.Text + ": " + c.Effect;
                DLog.Force(msg);
                Pending = null;
                return msg;
            }
            catch (Exception ex) { Pending = null; return "事件结算失败: " + ex.Message; }
        }

        internal static void Dismiss()
        {
            if (Pending != null) DLog.Force("事件: 暂不处理 " + Pending.Name);
            Pending = null;
        }

        internal static string Save()
        {
            try { return _lastDay + ";" + (Pending != null ? Pending.Id : ""); } catch { return ""; }
        }

        internal static void Load(string data)
        {
            try
            {
                if (string.IsNullOrEmpty(data)) return;
                var f = data.Split(';');
                int d;
                if (f.Length > 0 && int.TryParse(f[0], out d)) _lastDay = d;
                if (f.Length > 1 && !string.IsNullOrEmpty(f[1])) Pending = Def(f[1]);
            }
            catch { }
        }
    }
}
