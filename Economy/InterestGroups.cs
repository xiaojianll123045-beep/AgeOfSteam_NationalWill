using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;

namespace FeudalInternalAffairs
{
    // 利益集团(文档 24.2): 8 大集团, 影响力/满意度/意识形态(对法律态度)/领袖/组阁
    internal class InterestGroup
    {
        internal int Index;
        internal string Id;
        internal string Name;
        internal string Desc;
        internal string Icon;
        internal float Clout;        // 影响力(绝对)
        internal float Share;        // 影响力占比 0..1
        internal int Satisfaction;   // 满意度 -100..100
        internal bool InGov;         // 是否执政
        internal int EventMod;       // 事件修正(月衰减)
        internal int Members;        // 成员人口(估)
        internal float MemberShare;  // 成员占全国人口比
        internal string Leader = "—";
        internal string StanceText = "";   // 支持/反对(按法律态度)
    }

    // 政治运动(文档 24.4; P22 基础版, 革命留给 P23)
    internal class Movement
    {
        internal int Group;
        internal int Law = -1;       // 诉求法律(推进)
        internal float Support;      // 支持人口占比 0..1
        internal float Radical;      // 激进度 0..100
        internal int StartDay;
        internal int SuppressDay = -9999;
    }

    // 集团系统(文档 24.2): 影响力聚合 + 满意度(法律/经济/组阁) + 组阁合法性 + 政治运动
    internal static class InterestGroups
    {
        internal const int GroupCount = 8;
        // 集团索引: 0 王室 1 大贵族 2 地方贵族 3 教会 4 商帮 5 行会 6 农民 7 军队

        internal static readonly InterestGroup[] G = new InterestGroup[GroupCount];
        internal static readonly List<Movement> Movements = new List<Movement>();
        internal static bool Inited;

        private static readonly Dictionary<string, bool> _mineCache = new Dictionary<string, bool>();
        private static int _lastMonthDay = -1;

        // 意识形态对立对(组阁冲突)
        private static readonly int[][] Conflicts =
        {
            new[] { 1, 6 },   // 大贵族 vs 农民(税赋特权/平民权利)
            new[] { 1, 4 },   // 大贵族 vs 商帮(关税/特许)
            new[] { 3, 4 },   // 教会 vs 商帮(教会地位/自由贸易)
            new[] { 7, 6 },   // 军队 vs 农民(抽丁)
            new[] { 5, 4 }    // 行会 vs 商帮(特许垄断/自由买卖)
        };

        internal static void Reset()
        {
            try
            {
                if (G[0] == null) Build();
                for (int i = 0; i < GroupCount; i++)
                {
                    G[i].Clout = 0f; G[i].Share = 0f; G[i].Satisfaction = 0;
                    G[i].EventMod = 0; G[i].Members = 0; G[i].MemberShare = 0f;
                }
                // 默认内阁: 王室 + 大贵族 + 教会 + 军队(开局稳态: 合法性约 54)
                bool[] def = { true, true, false, true, false, false, false, true };
                for (int i = 0; i < GroupCount; i++) G[i].InGov = def[i];
                Movements.Clear();
                _mineCache.Clear();
                _lastMonthDay = -1;
                Inited = true;
                Recompute();
                DLog.Force("集团: 初始化 8 集团, 默认内阁=王室/大贵族/教会/军队");
            }
            catch (Exception ex) { DLog.Force("集团初始化失败: " + ex.Message); }
        }

        private static void Build()
        {
            G[0] = new InterestGroup { Index = 0, Id = "crown", Name = "王室", Icon = "fia_ig_crown", Desc = "王室与宫廷: 国家意志的化身" };
            G[1] = new InterestGroup { Index = 1, Id = "magnates", Name = "大贵族", Icon = "fia_ig_magnates", Desc = "拥有城堡与城镇的大领主" };
            G[2] = new InterestGroup { Index = 2, Id = "gentry", Name = "地方贵族", Icon = "fia_ig_gentry", Desc = "乡绅、小领主与退役军官" };
            G[3] = new InterestGroup { Index = 3, Id = "church", Name = "教会", Icon = "fia_ig_church", Desc = "神职与教产" };
            G[4] = new InterestGroup { Index = 4, Id = "merchants", Name = "商帮", Icon = "fia_ig_merchants", Desc = "富商、店主与资本家" };
            G[5] = new InterestGroup { Index = 5, Id = "guilds", Name = "行会", Icon = "fia_ig_guilds", Desc = "工匠、劳工与作坊主" };
            G[6] = new InterestGroup { Index = 6, Id = "peasants", Name = "农民", Icon = "fia_ig_peasants", Desc = "占人口绝大多数的耕作者" };
            G[7] = new InterestGroup { Index = 7, Id = "army", Name = "军队", Icon = "fia_ig_army", Desc = "军官团与国防军" };
        }

        internal static string NameOf(int g) { return (g >= 0 && g < GroupCount && G[g] != null) ? G[g].Name : "?"; }

        internal static float CloutOf(int g)
        {
            float c = (g >= 0 && g < GroupCount && G[g] != null && G[g].Clout > 1f) ? G[g].Clout : 1f;
            // v4.131: 边缘化集团(势弱)在立法加权中声音很小(V3: 边缘化 IG 不参与立法)
            if (g >= 0 && g < GroupCount && G[g] != null && IsMarginalized(g)) c *= 0.25f;
            return c;
        }

        // 边缘化(V3 1.13 官方): 影响力 <4% 且不在政府中(在政府/元首支持的 IG 永不边缘化)
        internal static bool IsMarginalized(int g)
        {
            try
            {
                if (g <= 0 || g >= GroupCount || G[g] == null) return false;
                if (G[g].InGov) return false;
                return G[g].Share < 0.04f;
            }
            catch { return false; }
        }

        // 强权(V3: clout >20% = powerful)
        internal static bool IsPowerful(int g)
        {
            try
            {
                return g > 0 && g < GroupCount && G[g] != null && G[g].Share > 0.20f;
            }
            catch { return false; }
        }

        // 满意度档位(V3 approval 档位, 映射到 ±100 标度): 0 愤怒 / 1 不满 / 2 中立 / 3 满意 / 4 忠诚
        internal static int SatLevel(int g)
        {
            int s = SatOf(g);
            if (s <= -50) return 0;
            if (s <= -25) return 1;
            if (s < 25) return 2;
            if (s < 50) return 3;
            return 4;
        }

        internal static string SatLevelName(int g)
        {
            switch (SatLevel(g))
            {
                case 0: return "愤怒";
                case 1: return "不满";
                case 3: return "满意";
                case 4: return "忠诚";
                default: return "中立";
            }
        }

        internal static int SatOf(int g) { return (g >= 0 && g < GroupCount && G[g] != null) ? G[g].Satisfaction : 0; }

        internal static bool InGovOf(int g) { return g >= 0 && g < GroupCount && G[g] != null && G[g].InGov; }

        internal static void AddEventMod(int g, int v)
        {
            try
            {
                if (g < 0 || g >= GroupCount || G[g] == null) return;
                G[g].EventMod = LawSystem.Clamp(G[g].EventMod + v, -60, 60);
            }
            catch { }
        }

        // ================= 影响力聚合 =================
        internal static void Recompute()
        {
            try
            {
                if (G[0] == null) Build();
                var pk = NationKingdom();
                // 只统计本国定居点的人口(归属缓存每日重建, 见 TickDay)
                float[] pop = new float[GroupCount];
                int[] mem = new int[GroupCount];
                if (pk != null)
                {
                    float up, mid, low;
                    StratumFactors(out up, out mid, out low);
                    foreach (var kv in Pops.BySettlement)
                    {
                        if (!IsMine(kv.Key, pk)) continue;
                        var list = kv.Value;
                        if (list == null) continue;
                        for (int i = 0; i < list.Count; i++)
                        {
                            var r = list[i];
                            if (r == null || r.Size < 0.5f) continue;
                            var def = PopDefs.Get(r.Profession);
                            float sf = def.Stratum == 2 ? up : (def.Stratum == 1 ? mid : low);
                            float wf = 0.5f + r.Wealth / 40f;   // 财富系数: SoL4 -> 0.6, SoL18 -> 0.95
                            float p = r.Workforce * def.Political * wf * sf;
                            if (p <= 0f) continue;
                            AddPop(pop, mem, r, p, def);
                        }
                    }
                }

                // 特殊项
                float crown = 260f + Politics.Authority * 0.3f + Math.Max(0f, (float)Treasury()) / 1500f;
                float magnates = pop[1] + Math.Max(0f, Politics.NobleClout) * 1.2f;
                float church = pop[3] + 30f + Ownership.ChurchPool / 20f;
                float guilds = pop[5] + 20f + Ownership.GuildFund / 20f;
                float army = pop[7] + Math.Max(0, SafeTotalMen()) * 0.15f;
                float merchants = pop[4];
                float gentry = pop[2];
                float peasants = pop[6] * 0.5f;   // 农民分散无组织: 打折
                float[] vals = { crown, magnates, gentry, church, merchants, guilds, peasants, army };
                float sum = 0f;
                for (int i = 0; i < GroupCount; i++) { vals[i] = Math.Max(1f, vals[i]); sum += vals[i]; }
                for (int i = 0; i < GroupCount; i++)
                {
                    G[i].Clout = vals[i];
                    G[i].Share = vals[i] / sum;
                    G[i].Members = mem[i];
                }
                float totalPop = 0f;
                for (int i = 0; i < GroupCount; i++) totalPop += mem[i];
                for (int i = 0; i < GroupCount; i++)
                    G[i].MemberShare = totalPop > 0f ? mem[i] / totalPop : 0f;

                RefreshLeaders();
                for (int i = 0; i < GroupCount; i++) { G[i].Satisfaction = ComputeSatisfaction(i); G[i].StanceText = StanceOf(i); }
                Inited = true;
            }
            catch (Exception ex) { DLog.Force("集团影响力聚合失败: " + ex.Message); }
        }

        // 职业 -> 集团(含权重分摊)
        private static void AddPop(float[] pop, int[] mem, PopRecord r, float p, ProfessionDef def)
        {
            string id = r.Profession;
            if (id == PopDefs.Aristocrats) { pop[1] += p; mem[1] += (int)r.Size; }
            else if (id == PopDefs.Farmers) { pop[2] += p; mem[2] += (int)r.Size; }
            else if (id == PopDefs.Officers) { pop[7] += p * 0.5f; pop[2] += p * 0.5f; mem[7] += (int)(r.Size * 0.5f); mem[2] += (int)(r.Size * 0.5f); }
            else if (id == PopDefs.Clergymen) { pop[3] += p; mem[3] += (int)r.Size; }
            else if (id == PopDefs.Academics) { pop[3] += p * 0.3f; pop[4] += p * 0.7f; mem[3] += (int)(r.Size * 0.3f); mem[4] += (int)(r.Size * 0.7f); }
            else if (id == PopDefs.Capitalists) { pop[4] += p; mem[4] += (int)r.Size; }
            else if (id == PopDefs.Shopkeepers) { pop[4] += p; mem[4] += (int)r.Size; }
            else if (id == PopDefs.Clerks) { pop[4] += p * 0.5f; pop[5] += p * 0.5f; mem[4] += (int)(r.Size * 0.5f); mem[5] += (int)(r.Size * 0.5f); }
            else if (id == PopDefs.Laborers) { pop[5] += p; mem[5] += (int)r.Size; }
            else if (id == PopDefs.Machinists) { pop[5] += p; mem[5] += (int)r.Size; }
            else if (id == PopDefs.Engineers) { pop[5] += p; mem[5] += (int)r.Size; }
            else if (id == PopDefs.Miners) { pop[5] += p; mem[5] += (int)r.Size; }
            else if (id == PopDefs.Bureaucrats) { pop[0] += p * 0.5f; pop[2] += p * 0.5f; mem[0] += (int)(r.Size * 0.5f); mem[2] += (int)(r.Size * 0.5f); }
            else if (id == PopDefs.Soldiers) { pop[7] += p; mem[7] += (int)r.Size; }
            else if (id == PopDefs.Peasants) { pop[6] += p; mem[6] += (int)r.Size; }
            else if (id == PopDefs.Unemployed) { pop[6] += p * 0.5f; pop[5] += p * 0.5f; mem[6] += (int)(r.Size * 0.5f); mem[5] += (int)(r.Size * 0.5f); }
            else { pop[5] += p; mem[5] += (int)r.Size; }
        }

        // 制度系数(文档 24.1, v4.133 委托法律体系): 权力分配决定各阶层政治力量; 政体/土地/官僚/言论做修正
        private static void StratumFactors(out float up, out float mid, out float low)
        {
            LawSystem.StratumMults(out up, out mid, out low);
            int cr = LawSystem.Level(LawSystem.LRights);
            low *= 1f + 0.15f * cr;
            up *= 1f - 0.05f * cr;
        }

        private static bool IsMine(string settlementId, Kingdom pk)
        {
            try
            {
                bool v;
                if (_mineCache.TryGetValue(settlementId, out v)) return v;
                Settlement s = FindSettlement(settlementId);
                v = s != null && s.MapFaction == pk;
                _mineCache[settlementId] = v;
                return v;
            }
            catch { return false; }
        }

        private static Settlement FindSettlement(string id)
        {
            try
            {
                foreach (var s in Settlement.All) if (s != null && s.StringId == id) return s;
            }
            catch { }
            return null;
        }

        private static Kingdom NationKingdom()
        {
            try { return NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null; }
            catch { return null; }
        }

        private static int Treasury()
        {
            try { return (int)EconomyWorld.Treasury.Gold; } catch { return 0; }
        }

        private static int SafeTotalMen()
        {
            try { return DefArmy.TotalMen(); } catch { return 0; }
        }

        // ================= 满意度 =================
        internal static int ComputeSatisfaction(int g)
        {
            try
            {
                if (G[g] == null) return 50;
                float s = 50f;
                // 1) 法律态度(V3 官方: 法律立场合计 cap ±5 于 ±20 标度 -> 我们 ±40)
                float lawSum = 0f;
                for (int i = 0; i < LawSystem.LawCount; i++)
                    lawSum += LawSystem.AttOf(i, LawSystem.Level(i), g);
                if (lawSum > 40f) lawSum = 40f;
                if (lawSum < -40f) lawSum = -40f;
                s += lawSum;
                // 2) 组阁(V3: 强权 IG(clout>20%) 在野额外不满)
                if (G[g].InGov) s += 8f;
                else if (IsPowerful(g)) s -= 18f;
                else if (G[g].Share >= 0.15f) s -= 12f;
                else s -= 3f;
                // 3) 经济/形势项
                switch (g)
                {
                    case 0:   // 王室: 权威与暴政
                        s += Politics.Authority / 25f - Politics.Tyranny * 0.5f;
                        break;
                    case 1:   // 大贵族: 领主态度/愤怒 + 分红 + 契约税档
                        s += Politics.NobleAttitude / 5f - Politics.NobleAnger / 6f;
                        if (Ownership.LordPool > 3000f) s += 5f;
                        s += (1f - FeudalContracts.AvgTaxMult()) * 20f;
                        break;
                    case 2:   // 地方贵族: 粮价与治安
                        s += PriceIndex() < 1.12f ? 4f : -6f;
                        s += Politics.CivilWar ? -8f : 2f;
                        break;
                    case 3:   // 教会: 教会态度 + 铸币成色
                        s += Politics.ChurchAttitude / 4f - MintRight.Purity * 8f;
                        break;
                    case 4:   // 商帮: 特许 + 市场税
                    {
                        int charters = CountChartered();
                        int marketTaxOver = Math.Max(0, TaxPolicy.Level[3] - 2);
                        s += charters * 3f - marketTaxOver * 4f + Guilds.Founded.Count * 1f;
                        break;
                    }
                    case 5:   // 行会: 行会基金 + 激进
                        s += Math.Max(-10f, Math.Min(10f, Ownership.GuildFund / 2000f)) + CountChartered() * 2f;
                        s -= Politics.AvgRadicalism() * 20f;
                        break;
                    case 6:   // 农民: 税负 + 激进 + 粮价
                    {
                        float taxOver = Math.Max(0f, TaxPolicy.Level[0] + TaxPolicy.Level[1] - 3f);
                        s -= taxOver * 3f;
                        s -= Politics.AvgRadicalism() * 40f;
                        s += PriceIndex() < 1.15f ? 5f : -8f;
                        break;
                    }
                    case 7:   // 军队: 规模 + 欠饷(战争经济/内战)
                        s += Math.Max(0f, Math.Min(8f, SafeTotalMen() / 500f));
                        if (Politics.CivilWar) s -= 10f;
                        break;
                }
                s += G[g].EventMod;
                return LawSystem.Clamp((int)Math.Round(s), -100, 100);
            }
            catch { return 50; }
        }

        private static int CountChartered()
        {
            try
            {
                int n = 0;
                foreach (var gd in Guilds.All) if (Guilds.IsChartered(gd.Id)) n++;
                return n;
            }
            catch { return 0; }
        }

        private static float PriceIndex()
        {
            try { return Stats.Ring.Count > 0 ? Stats.Ring[Stats.Ring.Count - 1].PriceIndex : 1f; }
            catch { return 1f; }
        }

        // ================= 领袖与立场 =================
        internal static void RefreshLeaders()
        {
            try
            {
                if (G[0] == null) return;
                G[0].Leader = Hero.MainHero != null ? Hero.MainHero.Name.ToString() : "国王";
                G[1].Leader = TopLordName(0, "大领主");
                G[2].Leader = TopLordName(1, "乡绅议会");
                G[3].Leader = "大主教";
                G[4].Leader = "商会会长";
                G[5].Leader = TopGuildName();
                G[6].Leader = "村长联会";
                G[7].Leader = SafeTotalMen() > 0 ? "国防军统帅" : "军官团";
            }
            catch { }
        }

        private static string TopLordName(int rank, string fallback)
        {
            try
            {
                string best = null; int bestClout = -1; string second = null; int secondClout = -1;
                foreach (var kv in Politics.Lords)
                {
                    var lp = kv.Value;
                    if (lp == null) continue;
                    if (lp.Clout > bestClout) { second = best; secondClout = bestClout; best = lp.Name; bestClout = lp.Clout; }
                    else if (lp.Clout > secondClout) { second = lp.Name; secondClout = lp.Clout; }
                }
                if (rank == 0) return !string.IsNullOrEmpty(best) ? best : fallback;
                return !string.IsNullOrEmpty(second) ? second : fallback;
            }
            catch { return fallback; }
        }

        private static string TopGuildName()
        {
            try
            {
                foreach (var gd in Guilds.All) if (Guilds.IsFounded(gd.Id)) return gd.Name;
            }
            catch { }
            return "行会总会";
        }

        // "支持 X · 反对 Y" (按对下一档的立场; 只显示法律名, 档位详情见法律页)
        internal static string StanceOf(int g)
        {
            try
            {
                string best = null, worst = null;
                int bestV = 6, worstV = -6;
                for (int i = 0; i < LawSystem.LawCount; i++)
                {
                    int st = LawSystem.AdvanceStance(i, g);
                    if (st > bestV) { best = LawSystem.NameOf(i); bestV = st; }
                    if (st < worstV) { worst = LawSystem.NameOf(i); worstV = st; }
                }
                if (best == null && worst == null) return "暂无明确诉求";
                string s = "";
                if (best != null) s += "支持 " + best;
                if (worst != null) s += (s.Length > 0 ? " · " : "") + "反对 " + worst;
                return s;
            }
            catch { return ""; }
        }

        // ================= 组阁 =================
        internal static string ToggleCabinet(int g)
        {
            try
            {
                if (g < 0 || g >= GroupCount || G[g] == null) return "集团不存在";
                if (g == 0 && G[0].InGov) return "王室是元首集团, 不能出阁";
                if (!G[g].InGov && IsMarginalized(g))
                    return G[g].Name + " 势力微弱, 已被边缘化(无法入阁)";
                if (!G[g].InGov && SatLevel(g) == 0)
                    return G[g].Name + " 正处于愤怒状态, 拒绝入阁(先用宴会/赏赐/政策安抚)";
                if (!G[g].InGov && CabinetCount() >= CabinetLimit())
                    return "内阁已满(" + CabinetCount() + "/" + CabinetLimit() + " 位), 请先让其他集团出阁";
                bool to = !G[g].InGov;
                G[g].InGov = to;
                if (to)
                {
                    AddEventMod(g, 5);
                }
                else
                {
                    // V3 官方: 移除政府 IG -> 激化其 25% 成员;
                    // 但选举后 6 个月(我们 42 天)内的第一次改组免费, 不激化(官方 Election 规则)
                    bool freeReform = Elections.InFreeReformWindow();
                    if (!freeReform)
                    {
                        ShiftRadicalsOfGroup(g, 0.25f);
                        AddEventMod(g, -12);
                    }
                    else
                    {
                        AddEventMod(g, -3);
                    }
                }
                G[g].Satisfaction = ComputeSatisfaction(g);
                Politics.RecomputeLegitimacy();   // v4.140: 组阁即时刷新合法性(用户反馈)
                DLog.Force("集团: " + G[g].Name + (to ? " 入阁" : " 出阁(成员激进 +25%)") + " 执政占比=" + (int)(CabinetShare() * 100) + "% 合法性=" + (int)Politics.Legitimacy);
                return G[g].Name + (to ? " 已入阁" : " 已出阁(其成员 25% 激化)") + " (合法性 " + (int)CabinetBase() + ")";
            }
            catch { return "组阁操作失败"; }
        }

        // 内阁规模上限(委托法律体系: 权力分配 + 政体)
        internal static int CabinetLimit()
        {
            try { return LawSystem.CabinetLimit(); } catch { return 4; }
        }

        internal static float CabinetShare()
        {
            try
            {
                float all = 0f, ing = 0f;
                for (int i = 0; i < GroupCount; i++) { all += G[i].Clout; if (G[i].InGov) ing += G[i].Clout; }
                return all > 0f ? ing / all : 0f;
            }
            catch { return 0f; }
        }

        internal static int CabinetCount()
        {
            int n = 0;
            for (int i = 0; i < GroupCount; i++) if (G[i].InGov) n++;
            return n;
        }

        // 合法性主干(V3 口径): 30 + 65×执政影响力占比 × 元首加成 − 逐法意识形态对立 − 超编×12
        internal static float CabinetBase()
        {
            try
            {
                bool[] ing = new bool[GroupCount];
                for (int i = 0; i < GroupCount; i++) ing[i] = G[i] != null && G[i].InGov;
                return LegitFor(ing);
            }
            catch { return 45f; }
        }

        private static float LegitFor(bool[] ing)
        {
            try
            {
                float all = 0f, got = 0f;
                int n = 0;
                for (int i = 0; i < GroupCount; i++)
                {
                    if (G[i] == null) continue;
                    all += Math.Max(1f, G[i].Clout);
                    if (ing[i]) { got += Math.Max(1f, G[i].Clout); n++; }
                }
                float share = all > 0f ? got / all : 0f;
                float v = 30f + 65f * share;
                v *= ing[0] ? 1.08f : 0.85f;
                v -= IdeologyPenaltyFor(ing);
                int over = n - CabinetLimit();
                if (over > 0) v -= over * 12f;
                return LawSystem.ClampF(v, 5f, 95f);
            }
            catch { return 45f; }
        }

        // 逐法意识形态对立(V3: 每法取政府内最大立场差; 每法上限 8, 总上限 25; 法律类权重)
        internal static float IdeologyPenalty()
        {
            try
            {
                bool[] ing = new bool[GroupCount];
                for (int i = 0; i < GroupCount; i++) ing[i] = G[i] != null && G[i].InGov;
                return IdeologyPenaltyFor(ing);
            }
            catch { return 0f; }
        }

        private static float IdeologyPenaltyFor(bool[] ing)
        {
            try
            {
                float total = 0f;
                for (int i = 0; i < LawSystem.LawCount; i++)
                {
                    int lv = LawSystem.Level(i);
                    int maxDiff = 0;
                    for (int a = 0; a < GroupCount; a++)
                    {
                        if (!ing[a] || G[a] == null) continue;
                        for (int b = a + 1; b < GroupCount; b++)
                        {
                            if (!ing[b] || G[b] == null) continue;
                            int d = Math.Abs(LawSystem.AttOf(i, lv, a) - LawSystem.AttOf(i, lv, b));
                            if (d > maxDiff) maxDiff = d;
                        }
                    }
                    if (maxDiff > 8) maxDiff = 8;
                    total += maxDiff * 0.15f * LawWeight(i);
                }
                if (total > 25f) total = 25f;
                return total * LawSystem.IdeologyMult();   // v4.133: 政体/权力分配调节对立惩罚(V3)
            }
            catch { return 0f; }
        }

        private static float LawWeight(int law)
        {
            switch (LawSystem.CatOf(law))
            {
                case "权力": return 2.0f;
                case "贵族": return 1.5f;
                case "商贸": return 1.5f;
                case "信仰": return 1.0f;
                default: return 0.5f;
            }
        }

        // 合法性分解(UI)
        internal static string LegitBreakdownText()
        {
            try
            {
                float all = 0f, got = 0f;
                int n = 0;
                for (int i = 0; i < GroupCount; i++)
                {
                    if (G[i] == null) continue;
                    all += Math.Max(1f, G[i].Clout);
                    if (G[i].InGov) { got += Math.Max(1f, G[i].Clout); n++; }
                }
                float share = all > 0f ? got / all : 0f;
                float raw = (30f + 65f * share) * (G[0] != null && G[0].InGov ? 1.08f : 0.85f);
                float pen = IdeologyPenalty();
                int over = n - CabinetLimit();
                string s = "基础 " + (int)raw + " · 对立 -" + (int)pen + " · 超编 " + (over > 0 ? "-" + over * 12 : "0")
                    + " · 档位: " + LegitLevelName() + "(立法 ×" + LawSpeedMult().ToString("F2") + ")";
                return s;
            }
            catch { return ""; }
        }

        // ================= 合法性档位(V3 五档, 文档 24.2) =================
        internal static int LegitLevel()
        {
            try
            {
                float v = Politics.Legitimacy;
                if (v < 25f) return 0;   // 非法
                if (v < 50f) return 1;   // 虚弱
                if (v < 75f) return 2;   // 争议
                if (v < 90f) return 3;   // 合法
                return 4;                // 正统
            }
            catch { return 2; }
        }

        internal static string LegitLevelName()
        {
            switch (LegitLevel())
            {
                case 0: return "非法";
                case 1: return "虚弱";
                case 3: return "合法";
                case 4: return "正统";
                default: return "争议";
            }
        }

        internal static string LegitLevelColor()
        {
            switch (LegitLevel())
            {
                case 0: return "#FF4B4BFF";
                case 1: return "#D96A5AFF";
                case 3: return "#9FB08AFF";
                case 4: return "#7FBF6AFF";
                default: return "#E8C33AFF";
            }
        }

        internal static float LawSpeedMult()
        {
            switch (LegitLevel())
            {
                case 0: return 0.45f;
                case 1: return 0.66f;
                case 3: return 1.0f;
                case 4: return 1.25f;
                default: return 1.0f;
            }
        }

        // 档位每月对全国激进的影响(V3 官方: 非法 +3.0%~1.56%/月, 虚弱 +1.5%~0.06%, 合法/正统 -> 忠诚)
        internal static float MonthlyRadicalDelta()
        {
            switch (LegitLevel())
            {
                case 0: return 0.022f;
                case 1: return 0.008f;
                case 3: return -0.003f;
                case 4: return -0.007f;
                default: return 0f;
            }
        }

        // 供 Politics.Month 使用: 合法性主干 + 集团专属修正
        internal static float LegitimacyTerms()
        {
            try
            {
                float v = CabinetBase();
                int churchLv = LawSystem.Level(LawSystem.LChurch);   // v4.133: 教产充公 -2 / 国教 +2 / 神权 -2
                v += churchLv == 0 ? -2f : (churchLv == 2 ? 2f : (churchLv == 3 ? -2f : 0f));
                v += Elections.LegitFromVotes();   // v4.137: 选票合法性(V3 官方 Voting laws)
                for (int i = 0; i < Movements.Count; i++)
                    if (Movements[i].Radical >= 60f) v -= 2f;
                return v;
            }
            catch { return 45f; }
        }

        internal static string CabinetText()
        {
            try
            {
                string s = "";
                for (int i = 0; i < GroupCount; i++)
                    if (G[i].InGov) s += (s.Length > 0 ? "·" : "") + G[i].Name;
                return s.Length > 0 ? s : "无";
            }
            catch { return ""; }
        }

        internal static string OppositionText()
        {
            try
            {
                string s = "";
                int n = 0;
                for (int i = 0; i < GroupCount; i++)
                {
                    if (!G[i].InGov && G[i].Satisfaction < -15)
                    {
                        n++;
                        if (n <= 2) s += (s.Length > 0 ? "·" : "") + G[i].Name + " " + G[i].Satisfaction;
                    }
                }
                if (n > 2) s += " 等 " + n + " 个集团";
                return s.Length > 0 ? s : "暂无强烈不满";
            }
            catch { return ""; }
        }

        // ================= 政治运动 =================
        internal static bool HasMovementFor(int law)
        {
            for (int i = 0; i < Movements.Count; i++) if (Movements[i].Law == law) return true;
            return false;
        }

        // 一键优化内阁(V3 quick reform): 枚举组合取合法性最高; 出阁者照常激化其成员
        internal static string BestCabinet()
        {
            try
            {
                if (G[0] == null) return "集团未初始化";
                int n = GroupCount;
                bool[] best = null;
                float bestV = -1f;
                int combos = 1 << (n - 1);   // 王室固定在内阁
                for (int m = 0; m < combos; m++)
                {
                    var ing = new bool[n];
                    ing[0] = true;
                    int cnt = 1;
                    for (int b = 0; b < n - 1; b++)
                    {
                        if ((m & (1 << b)) == 0) continue;
                        int g = b + 1;
                        if (IsMarginalized(g)) continue;
                        ing[g] = true; cnt++;
                    }
                    if (cnt > CabinetLimit()) continue;
                    float v = LegitFor(ing);
                    if (v > bestV) { bestV = v; best = ing; }
                }
                if (best == null) return "无法计算推荐内阁";
                int changed = 0;
                for (int g = 1; g < n; g++)
                {
                    if (G[g].InGov == best[g]) continue;
                    changed++;
                    if (!best[g]) { ShiftRadicalsOfGroup(g, 0.25f); AddEventMod(g, -12); }
                    else AddEventMod(g, 5);
                    G[g].InGov = best[g];
                    G[g].Satisfaction = ComputeSatisfaction(g);
                }
                Politics.RecomputeLegitimacy();   // v4.140: 推荐内阁后即时刷新
                string txt = changed == 0
                    ? "当前内阁已是推荐组合(合法性 " + (int)CabinetBase() + ")"
                    : "已应用推荐内阁: 合法性 " + (int)CabinetBase() + " (调整 " + changed + " 个集团, 出阁者成员激化)";
                DLog.Force("集团: 一键优化内阁 -> " + txt + " [" + CabinetText() + "]");
                return txt;
            }
            catch { return "推荐内阁计算失败"; }
        }

        // 按集团调整其成员 pop 的激进度(V3: 出阁激化 25% 成员)
        internal static void ShiftRadicalsOfGroup(int g, float delta)
        {
            try
            {
                var pk = NationKingdom();
                foreach (var kv in Pops.BySettlement)
                {
                    if (!IsMine(kv.Key, pk)) continue;
                    var list = kv.Value;
                    if (list == null) continue;
                    for (int i = 0; i < list.Count; i++)
                    {
                        var r = list[i];
                        if (r == null || r.Size < 0.5f || !Belongs(r.Profession, g)) continue;
                        r.Radicalism = LawSystem.ClampF(r.Radicalism + delta, 0f, 1f);
                    }
                }
            }
            catch { }
        }

        // 职业 -> 集团主归属(用于按集团调整激进)
        private static bool Belongs(string prof, int g)
        {
            switch (g)
            {
                case 0: return prof == PopDefs.Bureaucrats;
                case 1: return prof == PopDefs.Aristocrats;
                case 2: return prof == PopDefs.Farmers;
                case 3: return prof == PopDefs.Clergymen || prof == PopDefs.Academics;
                case 4: return prof == PopDefs.Capitalists || prof == PopDefs.Shopkeepers || prof == PopDefs.Clerks;
                case 5: return prof == PopDefs.Laborers || prof == PopDefs.Machinists || prof == PopDefs.Engineers || prof == PopDefs.Miners;
                case 6: return prof == PopDefs.Peasants || prof == PopDefs.Unemployed;
                case 7: return prof == PopDefs.Soldiers || prof == PopDefs.Officers;
            }
            return false;
        }

        // 法案通过: 以该法为诉求的运动自然消散
        internal static void OnLawPassed(int law)
        {
            try
            {
                for (int i = Movements.Count - 1; i >= 0; i--)
                    if (Movements[i].Law == law) Movements.RemoveAt(i);
            }
            catch { }
        }

        internal static void TickDay(int day)
        {
            try
            {
                if (G[0] == null) return;
                _mineCache.Clear();   // 每天重建定居点归属缓存(UI 高频刷新走缓存)
                Recompute();          // 每日刷新影响力/满意度(立法推进读到当日数据)
                for (int i = Movements.Count - 1; i >= 0; i--)
                {
                    var m = Movements[i];
                    if (m.Group < 0 || m.Group >= GroupCount) { Movements.RemoveAt(i); continue; }
                    // v4.134(V3 官方): 激进度向"目标值"靠拢(每周 1% -> 每日 ~2%)
                    float target = ActivismTarget(m.Group, m.Law);
                    float step = (target - m.Radical) * 0.02f;
                    if (day - m.SuppressDay <= 3) step -= 1.5f;   // 镇压后 3 天回落
                    m.Radical += step;
                    if (m.Radical < 0f) m.Radical = 0f;
                    if (m.Radical > 100f) m.Radical = 100f;
                    if (m.Radical >= 100f)
                    {
                        Politics.Legitimacy = LawSystem.ClampF(Politics.Legitimacy - 6f, 0f, 100f);
                        Pops.ShiftRadicals(0.015f);
                        AddEventMod(m.Group, -8);
                        m.Radical = 70f;
                        NotifyPlayer("政治危机: " + NameOf(m.Group) + " 的运动激化(合法性 -6, 全国激进 +1.5%)", true);
                        DLog.Force("集团: 运动激化 -> " + NameOf(m.Group) + " 诉求=" + (m.Law >= 0 ? LawSystem.NameOf(m.Law) : "让步"));
                    }
                    // V3 官方: 激进度 >75 -> 成为革命性运动, 组织革命
                    if (m.Radical >= 75f) Revolution.TryOrganize(i, day);
                }
            }
            catch { }
        }

        // 运动激进度目标值(V3 官方: 每部现行法立场 ±5 / 近期通过法 ±25 / 激进支持者 +100 / 忠诚 -75)
        private static float ActivismTarget(int g, int law)
        {
            try
            {
                float t = 45f;
                // 诉求法律立场差
                if (law >= 0 && !LawSystem.Maxed(law))
                {
                    int cur = LawSystem.Level(law);
                    int nxt = LawSystem.NextTier(law);
                    int diff = LawSystem.AttOf(law, nxt, g) - LawSystem.AttOf(law, cur, g);
                    t += LawSystem.Clamp((int)(diff * 2f), -25, 25);
                }
                else
                {
                    // 无明确诉求: 由满意度驱动
                    int sat0 = G[g].Satisfaction;
                    t += sat0 < 0 ? Math.Min(35f, -sat0 * 0.5f) : Math.Max(-15f, -sat0 * 0.3f);
                }
                // 满意度修正(V3: 激进支持者 +100 / 忠诚支持者 -75)
                int sat = G[g].Satisfaction;
                if (sat < 0) t += Math.Min(25f, -sat * 0.4f);
                else t -= Math.Min(20f, sat * 0.3f);
                // 法律环境: 言论自由 - 镇压效果; 禁言 +
                t *= LawSystem.SuppressMult() > 1f ? 1.1f : 1f;
                if (LawSystem.Level(LawSystem.LSpeech) >= 2) t -= 8f;
                return LawSystem.ClampF(t, 5f, 100f);
            }
            catch { return 50f; }
        }

        // 公共通知(供 Revolution 等外部系统调用)
        internal static void NotifyPlayer(string msg, bool alert)
        {
            Notify(msg, alert);
        }

        internal static void Monthly(int day)
        {
            try
            {
                if (day == _lastMonthDay) return;
                _lastMonthDay = day;
                if (G[0] == null) { Reset(); return; }
                Recompute();
                // 事件修正衰减
                for (int i = 0; i < GroupCount; i++)
                {
                    if (G[i].EventMod > 0) G[i].EventMod = Math.Max(0, G[i].EventMod - 3);
                    else if (G[i].EventMod < 0) G[i].EventMod = Math.Min(0, G[i].EventMod + 3);
                }
                GenerateMovements(day);
                // V3 官方: 合法性档位 -> 所有在野集团不满(每月; ±20 标度 -3/-2/-1 -> 我们 -15/-10/-5)
                float oppMod = 0f;
                switch (LegitLevel())
                {
                    case 0: oppMod = -15f; break;
                    case 1: oppMod = -10f; break;
                    case 2: oppMod = -5f; break;
                }
                if (oppMod != 0f)
                    for (int i = 0; i < GroupCount; i++) if (!G[i].InGov) AddEventMod(i, (int)oppMod);
                // V3 官方: Happy(满意)及以上的集团停止支持运动
                for (int i = Movements.Count - 1; i >= 0; i--)
                    if (SatLevel(Movements[i].Group) >= 3) Movements.RemoveAt(i);
                // V3: 合法性档位每月影响激进(非法/虚弱 -> 激进; 合法/正统 -> 平息)
                float rad = MonthlyRadicalDelta();
                if (Math.Abs(rad) > 0.0001f) Pops.ShiftRadicals(rad);
                LawSystem.Monthly();   // v4.133: 法律月结(识字增长/福利支出)
            try { Treaties.Monthly(); } catch { }   // v4.188: 条约月结(收益/到期)
            try { Parties.Monthly(); } catch { }    // v4.195: 执政党效果 + 选举同步
                Revolution.Monthly();  // v4.134: 高激进度运动 -> 支持者激进化(V3)
                DLog.Force("集团月结: 执政=" + CabinetText() + " 基础合法性=" + (int)CabinetBase()
                    + " 运动=" + Movements.Count + " 满意度=" + SatLine());
            }
            catch (Exception ex) { DLog.Force("集团月结异常: " + ex.Message); }
        }

        private static string SatLine()
        {
            string s = "";
            for (int i = 0; i < GroupCount; i++) s += (i > 0 ? " " : "") + G[i].Name + ":" + G[i].Satisfaction;
            return s;
        }

        private static void GenerateMovements(int day)
        {
            try
            {
                for (int g = 0; g < GroupCount; g++)
                {
                    if (g == 0) continue;   // 王室不会反自己
                    if (G[g].Satisfaction >= -20) continue;
                    bool has = false;
                    for (int i = 0; i < Movements.Count; i++) if (Movements[i].Group == g) has = true;
                    if (has) continue;
                    if (Movements.Count >= 3) break;   // UI 一屏 3 张运动卡
                    // 诉求: 该集团最想推进且当前形势允许的法律
                    int bestLaw = -1, bestV = 8;
                    for (int i = 0; i < LawSystem.LawCount; i++)
                    {
                        if (LawSystem.Maxed(i)) continue;
                        int st = LawSystem.AdvanceStance(i, g);
                        if (st > bestV) { bestV = st; bestLaw = i; }
                    }
                    var m = new Movement
                    {
                        Group = g,
                        Law = bestLaw,
                        // V3 官方: 支持度 = 1/3 人口占比 + 1/3 军队占比 + 1/3 政治力量占比
                        Support = LawSystem.ClampF(G[g].MemberShare * 0.5f + G[g].Share * 0.5f, 0.02f, 0.75f),
                        Radical = Math.Min(100f, -G[g].Satisfaction),
                        StartDay = day
                    };
                    Movements.Add(m);
                    Notify("政治运动: " + G[g].Name + "发起请愿运动" + (bestLaw >= 0 ? " (诉求《" + LawSystem.NameOf(bestLaw) + "》)" : " (要求王室让步)"), true);
                    DLog.Force("集团: 新运动 -> " + G[g].Name + " 诉求=" + (bestLaw >= 0 ? LawSystem.NameOf(bestLaw) : "让步") + " 激进度=" + (int)m.Radical + "%");
                }
            }
            catch { }
        }

        internal static string SuppressMovement(int mi)
        {
            try
            {
                if (mi < 0 || mi >= Movements.Count) return "";
                var m = Movements[mi];
                if (Politics.Authority < 40f) return "权威不足(需 40)";
                Politics.Authority -= 40f;
                Politics.Tyranny += 2f;
                AddEventMod(m.Group, -12);
                m.Radical = Math.Max(0f, m.Radical - 35f);
                m.SuppressDay = Politics.Today();
                G[m.Group].Satisfaction = ComputeSatisfaction(m.Group);
                DLog.Force("集团: 镇压 " + NameOf(m.Group) + " 运动(激进度 " + (int)m.Radical + "%)");
                return "已镇压 " + NameOf(m.Group) + " 运动: 暴政 +2, 该集团满意度下降(激进度 -35)";
            }
            catch { return "镇压失败"; }
        }

        internal static string ConcedeMovement(int mi)
        {
            try
            {
                if (mi < 0 || mi >= Movements.Count) return "";
                var m = Movements[mi];
                if (EconomyWorld.Treasury.Gold < 1200) return "国库不足(需 1200 第纳尔)";
                EconomyWorld.TreasurySpend(1200);
                Fiscal.AddCourt(1200);
                AddEventMod(m.Group, 12);
                Politics.Legitimacy = LawSystem.ClampF(Politics.Legitimacy + 1f, 0f, 100f);
                string nm = NameOf(m.Group);
                Movements.RemoveAt(mi);
                G[m.Group].Satisfaction = ComputeSatisfaction(m.Group);
                Notify("已妥协: " + nm + " 运动平息(安抚费 1200)", false);
                DLog.Force("集团: 妥协安抚 " + nm + " 运动");
                return "已妥协: " + nm + " 运动平息(满意度 +12, 合法性 +1)";
            }
            catch { return "妥协失败"; }
        }

        // ================= 存档 FIA_IG =================
        internal static string Save()
        {
            try
            {
                var sb = new StringBuilder();
                sb.Append("v1;");
                for (int i = 0; i < GroupCount; i++) { if (i > 0) sb.Append(','); sb.Append(G[i] != null && G[i].InGov ? "1" : "0"); }
                sb.Append(';');
                for (int i = 0; i < GroupCount; i++) { if (i > 0) sb.Append(','); sb.Append(G[i] != null ? G[i].EventMod : 0); }
                sb.Append(';');
                for (int i = 0; i < Movements.Count; i++)
                {
                    var m = Movements[i];
                    if (i > 0) sb.Append('|');
                    sb.Append(m.Group).Append(',').Append(m.Law).Append(',')
                      .Append(m.Support.ToString("F3", CultureInfo.InvariantCulture)).Append(',')
                      .Append(m.Radical.ToString("F1", CultureInfo.InvariantCulture)).Append(',')
                      .Append(m.StartDay).Append(',').Append(m.SuppressDay);
                }
                sb.Append(';');
                return sb.ToString();
            }
            catch { return ""; }
        }

        internal static void Load(string data)
        {
            try
            {
                if (G[0] == null) Build();
                if (string.IsNullOrEmpty(data)) { Reset(); return; }   // 旧档无 FIA_IG: 用默认内阁与清零事件
                var seg = data.Split(';');
                if (seg.Length > 1)
                {
                    var gv = seg[1].Split(',');
                    for (int i = 0; i < GroupCount && i < gv.Length; i++) G[i].InGov = gv[i] == "1";
                }
                if (seg.Length > 2)
                {
                    var ev = seg[2].Split(',');
                    for (int i = 0; i < GroupCount && i < ev.Length; i++)
                    {
                        int x;
                        if (int.TryParse(ev[i], out x)) G[i].EventMod = x;
                    }
                }
                Movements.Clear();
                if (seg.Length > 3 && !string.IsNullOrEmpty(seg[3]))
                {
                    foreach (var line in seg[3].Split('|'))
                    {
                        if (string.IsNullOrEmpty(line)) continue;
                        var f = line.Split(',');
                        if (f.Length < 6) continue;
                        int g, law, sd, spd;
                        float sup, rad;
                        if (!int.TryParse(f[0], out g)) continue;
                        if (!int.TryParse(f[1], out law)) continue;
                        float.TryParse(f[2], NumberStyles.Float, CultureInfo.InvariantCulture, out sup);
                        float.TryParse(f[3], NumberStyles.Float, CultureInfo.InvariantCulture, out rad);
                        int.TryParse(f[4], out sd);
                        int.TryParse(f[5], out spd);
                        Movements.Add(new Movement { Group = g, Law = law, Support = sup, Radical = rad, StartDay = sd, SuppressDay = spd });
                    }
                }
                Inited = true;
                Recompute();
                DLog.Force("集团: 读档 执政=" + CabinetText() + " 运动=" + Movements.Count);
            }
            catch { }
        }

        private static void Notify(string msg, bool alert)
        {
            try
            {
                var c = Color.FromUint(alert ? 4294901760U : 4294953344U);
                InformationManager.DisplayMessage(new InformationMessage(msg, c));
            }
            catch { }
        }
    }
}
