using System;
using System.Globalization;
using System.Text;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace FeudalInternalAffairs
{
    // 法律定义(文档 24.3; 对照 V3 官方 26 个法律组移植为封建语境)
    internal class LawDef
    {
        internal string Id;
        internal string Name;
        internal string Cat;              // 类别: 权力/经济/民权
        internal string[] Tiers = new string[4];
        internal string[] Effect = new string[4];
        internal int[][] Att = new int[4][];        // [档][集团 8]
        internal int Legacy = -1;                   // 旧 6 部法映射
        internal string Scope;                      // v4.186: 空 = 通用(任意地图/国家可用); "c:文化id" / "k:王国id" = 特有(硬编码)
    }

    // 法律体系(文档 24.3, v4.133 按 V3 官方重写): 19 部法 × 3-4 档, 3 大类(权力/经济/民权)
    // 档位效果与 IG 立场抄自 vic3.paradoxwikis.com 的 Power_structure_laws / Economy_laws / Human_rights_laws
    internal static class LawSystem
    {
        // 集团索引: 0 王室 1 大贵族 2 地方贵族 3 教会 4 商帮 5 行会 6 农民 7 军队
        internal static readonly string[] CatOrder = { "权力", "经济", "民权" };

        internal const int LawCount = 29;   // v4.186: 23 通用 + 6 特有(按文化)

        // 索引
        internal const int LPolity = 0, LFranchise = 1, LSuccession = 2, LJustice = 3, LBureaucracy = 4, LChurch = 5;
        internal const int LSecurity = 6;   // 国内安全(官方 Internal Security)
        internal const int LTax = 7, LLand = 8, LEconomy = 9, LTrade = 10, LCharter = 11, LCoinage = 12;
        internal const int LPolicing = 13;  // 治安(官方 Policing)
        internal const int LRights = 14, LLevy = 15, LSpeech = 16, LWomen = 17, LWelfare = 18, LEducation = 19;
        internal const int LMigration = 20, LLabor = 21;    // 劳工权利(官方 Labor Rights)
        internal const int LAssoc = 22;                      // 劳工组织(官方 Labor Associations)

        private static LawDef[] _defs;
        private static readonly int[] _level = new int[LawCount];
        private static bool _loaded;
        private static bool _migrated;

        internal static int BillLaw = -1;
        internal static float BillProgress;
        internal static int BillStartDay = -9999;
        internal static int BillStallDays;
        internal static bool BillForced;
        internal static bool BillPaused;
        internal static int BillTarget = -1;         // v4.142: 立法目标档(可任意档, 含倒退; V3 形式=更换法律)
        internal static int LastCheckDay = -9999;    // v4.142: 上次检查点
        internal const int CheckpointDays = 14;      // v4.142: 检查点间隔(V3 官方默认 100 天 -> 骑砍 14 天)
        internal static string LastOutcome = "";     // v4.142: 上次检查点结果(成功推进/稳步推进/僵局/辩论)

        private static int[] A(int crown, int mag, int gent, int church, int merch, int guild, int peas, int army)
        {
            return new int[] { crown, mag, gent, church, merch, guild, peas, army };
        }

        private static void EnsureDefs()
        {
            if (_defs != null) return;
            _defs = new LawDef[]
            {
                // ================= 权力 =================
                new LawDef
                {
                    Id = "polity", Name = "政体", Cat = "权力",
                    Tiers = new[] { "封建契约", "王室集权", "专制君权", "天授神权" },
                    Effect = new[]
                    {
                        "分封自治: 贵族力量 +15%, 无额外集权",
                        "王室集权: 权威 +2/月, 税收 +3%, 对立惩罚 ×1.1",
                        "专制君权: 权威 +4/月, 税收 +6%, 对立 ×1.25, 内阁位 -1, 贵族/军官力量 +30%",
                        "天授神权: 权威 +6/月, 税收 +9%, 对立 ×1.5, 内阁位 -1, 贵族/军官力量 +50%"
                    },
                    Att = new[]
                    {
                        A(0, 0, 0, 0, 0, 0, 0, 0),
                        A(10, -10, -5, 0, 5, 3, -5, 8),
                        A(16, -20, -12, -5, 6, 3, -10, 14),
                        A(18, -24, -14, -10, 4, 0, -14, 16)
                    }
                },
                new LawDef
                {
                    Id = "franchise", Name = "权力分配", Cat = "权力",
                    Tiers = new[] { "贵族会议", "财产选举", "等级议会", "平民大会" },
                    Effect = new[]
                    {
                        "贵族会议(寡头): 内阁 5 位, 对立 ×0.9, 上层力量 ×1.6",
                        "财产选举: 内阁 4 位, 对立 ×1.1, 上层 ×1.35/中层 ×1.15",
                        "等级议会: 内阁 3 位, 上层 ×1.15/中层 ×1.2/下层 ×1.05",
                        "平民大会: 内阁 4 位, 对立 ×0.9, 下层力量 ×1.5, 上层 ×1.0"
                    },
                    Att = new[]
                    {
                        A(4, 16, 10, 4, 0, 0, -16, 10),
                        A(8, 8, 6, 2, 10, 8, -8, 6),
                        A(10, -6, 4, 2, 12, 14, 12, 0),
                        A(6, -18, -4, -4, 8, 18, 24, -8)
                    }
                },
                new LawDef
                {
                    Id = "succession", Name = "王位继承", Cat = "权力", Legacy = 0,
                    Tiers = new[] { "推选制", "长子继承", "钦定储君", "神圣血统" },
                    Effect = new[]
                    {
                        "合法性 +0 (领主推举, 贵族满意)",
                        "合法性 +2.5 (长子继承, 有法可依)",
                        "合法性 +4 (钦定储君, 王权巩固)",
                        "合法性 +6 (神圣血统, 不容置疑)"
                    },
                    Att = new[]
                    {
                        A(0, 6, 4, 2, 0, 0, 0, 0),
                        A(6, -2, -2, 0, 0, 0, 0, 0),
                        A(10, -8, -6, -2, 0, 0, -2, 0),
                        A(14, -14, -10, -6, -2, -2, -4, -4)
                    }
                },
                new LawDef
                {
                    Id = "justice", Name = "王室司法", Cat = "权力", Legacy = 5,
                    Tiers = new[] { "领主裁判", "王室巡回", "统一律令", "铁面律法" },
                    Effect = new[]
                    {
                        "激进 -0%/日 (领主自理讼狱, 贵族满意)",
                        "激进 -0.05%/日, 贵族态度 -4",
                        "激进 -0.10%/日, 贵族态度 -8, 民众拥护",
                        "激进 -0.15%/日, 贵族态度 -12, 律法森严"
                    },
                    Att = new[]
                    {
                        A(0, 8, 6, 2, 2, 2, -4, 0),
                        A(6, -4, -2, 2, 3, 4, 6, 2),
                        A(10, -8, -4, 4, 5, 6, 10, 4),
                        A(14, -12, -8, 6, 7, 8, 14, 6)
                    }
                },
                new LawDef
                {
                    Id = "bureaucracy", Name = "官僚制度", Cat = "权力",
                    Tiers = new[] { "世袭官僚", "任命官僚", "科举取士" },
                    Effect = new[]
                    {
                        "世袭官僚: 税收效率 -5%, 大贵族力量 +15%",
                        "任命官僚: 税收效率 +5%, 王室力量 +25%",
                        "科举取士: 税收效率 +10%, 识字增长 +0.1%/月, 地方贵族力量 +25%"
                    },
                    Att = new[]
                    {
                        A(0, 14, 6, 2, -6, -4, -6, 0),
                        A(10, -6, -4, 0, 6, 4, 6, 2),
                        A(8, -4, 14, -2, 8, 8, 10, 0)
                    }
                },
                new LawDef
                {
                    Id = "church", Name = "教会地位", Cat = "权力", Legacy = 3,
                    Tiers = new[] { "教产充公", "教会自治", "国教特权", "神权至上" },
                    Effect = new[]
                    {
                        "教产充公: 教会力量 -50%, 教会愤怒, 合法性 -2, 教会池归公",
                        "教会自治: 相安无事, 什一税照常",
                        "国教特权: 权威 +2/月, 合法性 +2, 税收 -3%, 教会力量 +30%",
                        "神权至上: 权威 +4/月, 合法性 -2, 税收 -6%, 教会力量 +50%, 贵族不满"
                    },
                    Att = new[]
                    {
                        A(6, 4, 2, -22, 4, 2, 2, 0),
                        A(0, 0, 0, 0, 0, 0, 0, 0),
                        A(2, -2, -2, 14, -4, -2, -4, 4),
                        A(-4, -6, -4, 20, -8, -6, -8, 2)
                    }
                },
                new LawDef
                {
                    Id = "security", Name = "国内安全", Cat = "权力",
                    Tiers = new[] { "无巡查", "民兵治安", "秘密警察", "保障自由" },
                    Effect = new[]
                    {
                        "无巡查: 无内务机构; 军队镇压 (+10% 军事)",
                        "民兵治安: 启用内务机构(每级 -3% 运动激进度, +0.3% 征兵率)",
                        "秘密警察: 启用内务机构(-5% 镇压影响, 革命进度 -10%/级, 军官力量 +25%)",
                        "保障自由: 启用内务机构(-5% 运动激进, +5% 忠诚, 革命进度 -10%/级)"
                    },
                    Att = new[]
                    {
                        A(0, 0, 0, 0, 0, -10, 0, -10),
                        A(0, 0, 0, 0, 0, 10, 0, 10),
                        A(0, 0, -20, 0, 0, 10, 0, 10),
                        A(0, 0, 20, 0, 0, -20, 0, -20)
                    }
                },

                // ================= 经济 =================
                new LawDef
                {
                    Id = "tax", Name = "税赋特权", Cat = "经济", Legacy = 1,
                    Tiers = new[] { "一体纳赋", "贵族减税", "贵族免税", "贵族优免" },
                    Effect = new[]
                    {
                        "税收效率 +6%; 贵族怨声载道",
                        "税收效率 +3%, 贵族态度 +5",
                        "税收效率 0%, 贵族态度 +5/级",
                        "税收效率 -3%, 贵族态度 +5/级"
                    },
                    Att = new[]
                    {
                        A(4, -12, -8, 0, 2, 3, 4, 0),
                        A(2, -4, -2, 0, 0, 0, 0, 0),
                        A(0, 6, 4, 0, -2, -2, -4, 0),
                        A(-2, 10, 6, 0, -4, -3, -8, -2)
                    }
                },
                new LawDef
                {
                    Id = "land", Name = "土地制度", Cat = "经济",
                    Tiers = new[] { "农奴制", "佃农制", "自耕农", "商业化农业" },
                    Effect = new[]
                    {
                        "农奴制: 贵族力量 +25%/农民 -30%, 农业产出 +10%, 农民激进 +",
                        "佃农制: 贵族 +15%/农民 -10%, 税收 +5%, 农民不可迁移",
                        "自耕农: 农民力量 +25%, 农业产出 +15%, 迁移吸引力 +10%",
                        "商业化农业: 商帮力量 +25%, 农业产出 +20%, 农民不满"
                    },
                    Att = new[]
                    {
                        A(2, 20, 12, 4, -4, -6, -24, 4),
                        A(2, 10, 6, 2, 0, -2, -8, 0),
                        A(4, -12, 4, -2, -4, 2, 18, -2),
                        A(2, 0, -2, -2, 16, 4, -14, 0)
                    }
                },
                new LawDef
                {
                    Id = "economy", Name = "经济体制", Cat = "经济",
                    Tiers = new[] { "传统经济", "重商主义", "自由放任", "国家专营" },
                    Effect = new[]
                    {
                        "传统经济: 税收 -25%, 私人建造 +25%, 国有化补偿 +100%",
                        "重商主义: 私人建造 +50%, 国有化补偿 +25%, 商帮壮大",
                        "自由放任: 私人建造 +75%, 贷款利率 -25%, 行会特许被削弱",
                        "国家专营: 权威 +25%, 私人建造 -50%, 国有化补偿 -50%, 商帮强烈不满"
                    },
                    Att = new[]
                    {
                        A(0, 14, 6, 4, -10, 2, 2, 0),
                        A(2, 4, 0, 0, 10, 6, 0, 2),
                        A(0, -6, 6, -2, 20, -10, 0, -2),
                        A(14, -8, -4, -4, -22, -6, -6, 4)
                    }
                },
                new LawDef
                {
                    Id = "trade_policy", Name = "关税政策", Cat = "经济",
                    Tiers = new[] { "孤立自守", "重商保护", "通商互惠", "自由贸易" },
                    Effect = new[]
                    {
                        "市场税 -6%, 商帮失望 (闭关锁国), 权威 +",
                        "重商保护: 市场税基准",
                        "通商互惠: 市场税 +6%, 商帮满意",
                        "自由贸易: 市场税 +12%, 商帮鼎盛、行会反对"
                    },
                    Att = new[]
                    {
                        A(0, 6, 4, 2, -10, 2, 4, 0),
                        A(0, 2, 2, 0, 0, 0, 0, 0),
                        A(2, -2, 0, 0, 6, 0, 2, 0),
                        A(2, -4, -2, 0, 12, -6, 6, 0)
                    }
                },
                new LawDef
                {
                    Id = "charter", Name = "商贸特许", Cat = "经济", Legacy = 4,
                    Tiers = new[] { "特许状", "专利状", "自由买卖", "万商云集" },
                    Effect = new[]
                    {
                        "行会特许费 -500/档, 行会加成 +2%/档",
                        "行会特许费 -500/档, 行会加成 +2%/档",
                        "行会特许费 -500/档, 行会加成 +2%/档, 商帮壮大",
                        "行会特许费 -500/档, 行会加成 +2%/档, 商帮鼎盛"
                    },
                    Att = new[]
                    {
                        A(0, 2, 0, 0, 4, 8, 0, 0),
                        A(0, 0, 0, 0, 2, 4, -2, 0),
                        A(2, -2, 0, 0, 6, -6, 4, 0),
                        A(2, -4, -2, 0, 10, -10, 8, 0)
                    }
                },
                new LawDef
                {
                    Id = "coinage", Name = "铸币权", Cat = "经济",
                    Tiers = new[] { "足银", "九成", "七成" },
                    Effect = new[]
                    {
                        "足银: 铸币收入基准, 物价稳定, 民众信服",
                        "九成: 铸币收入 +40%, 物价 +2%/月, 商帮不满",
                        "七成: 铸币收入 +90%, 物价 +5%/月, 教会/商帮/农民愤怒"
                    },
                    Att = new[]
                    {
                        A(-2, 2, 2, 6, 12, 8, 12, 0),
                        A(6, 2, 0, -6, -4, -2, -8, 0),
                        A(12, 2, -4, -14, -14, -8, -20, 0)
                    }
                },
                new LawDef
                {
                    Id = "policing", Name = "治安", Cat = "经济",
                    Tiers = new[] { "无警察", "地方治安", "专职巡捕", "军事化治安" },
                    Effect = new[]
                    {
                        "无警察: 无治安机构; 农民满意",
                        "地方治安: 启用治安机构(每级 -5% 动乱惩罚, 大贵族力量 +5%)",
                        "专职巡捕: 启用治安机构(+2 最大等级, -5% 激进)",
                        "军事化治安: 启用治安机构(+2 最大等级, 军官力量 +10%, 激进 -5%, 动乱死亡率 +0.4%)"
                    },
                    Att = new[]
                    {
                        A(0, -10, 0, 0, 0, -10, 10, -10),
                        A(0, 10, -10, 0, 0, 0, -10, 0),
                        A(0, 0, 0, 0, 0, 10, 0, 10),
                        A(0, 0, -20, 0, 0, 20, -20, 20)
                    }
                },

                // ================= 民权 =================
                new LawDef
                {
                    Id = "rights", Name = "平民权利", Cat = "民权",
                    Tiers = new[] { "无权利", "有限权利", "法律平等", "天赋人权" },
                    Effect = new[]
                    {
                        "下层无法可依; 迁移吸引力 0%, 激进 +",
                        "有限权利: 激进 -0.02%/日",
                        "法律平等: 激进 -0.04%/日, 识字 +0.05%/月, 贵族反对",
                        "天赋人权: 激进 -0.06%/日, 识字 +0.1%/月, 贵族强烈反对"
                    },
                    Att = new[]
                    {
                        A(4, 6, 4, 0, 0, -4, -12, 0),
                        A(2, 2, 2, 2, 2, 2, 4, 0),
                        A(0, -4, -2, 4, 4, 8, 12, 0),
                        A(-2, -10, -6, 8, 6, 12, 20, 2)
                    }
                },
                new LawDef
                {
                    Id = "levy", Name = "军制", Cat = "民权", Legacy = 2,
                    Tiers = new[] { "领主私兵", "领主征召", "举国征召", "全民皆兵" },
                    Effect = new[]
                    {
                        "领主私兵: 军事 +0%, 征召池 ×0.6, 军队满意",
                        "领主征召: 军事 +4%, 征召池 ×1.0, 领主愤怒 +2/月",
                        "举国征召: 军事 +8%, 征召池 ×1.5, 领主愤怒 +4/月, 农民负担重",
                        "全民皆兵: 军事 +12%, 征召池 ×2.0, 领主愤怒 +8/月, 民怨沸腾"
                    },
                    Att = new[]
                    {
                        A(0, 8, 4, 0, 0, 0, 2, 10),
                        A(2, 0, 0, 0, 0, 0, -4, 6),
                        A(6, -6, -2, -2, 0, -2, -12, 10),
                        A(8, -10, -4, -4, -2, -4, -20, 14)
                    }
                },
                new LawDef
                {
                    Id = "speech", Name = "言论集会", Cat = "民权",
                    Tiers = new[] { "禁言", "有限集会", "言论自由" },
                    Effect = new[]
                    {
                        "禁言: 权威 +2/月, 镇压效果 +50%, 识字增长 -0.1%/月",
                        "有限集会: 权威 +1/月, 镇压效果 +25%",
                        "言论自由: 激烈 -0.02%/日, 识字 +0.1%/月, 镇压效果 -25%"
                    },
                    Att = new[]
                    {
                        A(10, 4, -6, 4, -4, -6, -10, 4),
                        A(4, 2, 0, 0, 0, 0, 0, 0),
                        A(-4, -6, 12, -6, 8, 10, 14, -4)
                    }
                },
                new LawDef
                {
                    Id = "women", Name = "妇女权利", Cat = "民权",
                    Tiers = new[] { "家长制", "财产权", "劳动参与" },
                    Effect = new[]
                    {
                        "家长制: 人口增长 +5%, 劳动力比例基准",
                        "财产权: 劳动力比例 +5%",
                        "劳动参与: 劳动力比例 +10%, 人口增长 -3%"
                    },
                    Att = new[]
                    {
                        A(0, 12, 6, 14, 0, -4, -4, 2),
                        A(2, 0, 4, -4, 4, 4, 4, 0),
                        A(2, -8, 6, -12, 8, 10, 10, 0)
                    }
                },
                new LawDef
                {
                    Id = "welfare", Name = "济贫制度", Cat = "民权",
                    Tiers = new[] { "无救济", "济贫院", "养老抚恤" },
                    Effect = new[]
                    {
                        "无救济: 国库无支出, 下层怨声",
                        "济贫院: 国库 -1.5/百人/月, 下层满意度 +, 激进 -",
                        "养老抚恤: 国库 -3/百人/月, 下层满意度 ++, 激进 --"
                    },
                    Att = new[]
                    {
                        A(2, 10, 0, -6, 4, -4, -12, 0),
                        A(0, -4, 2, 2, 0, 6, 8, 0),
                        A(-2, -10, 4, 6, -6, 12, 16, 0)
                    }
                },
                new LawDef
                {
                    Id = "education", Name = "教育制度", Cat = "民权",
                    Tiers = new[] { "无学", "教会学堂", "公立学堂" },
                    Effect = new[]
                    {
                        "无学: 教育机会无加成, 学者/商帮不满",
                        "教会学堂: 教育机会 +10%, 教会力量 +10%",
                        "公立学堂: 教育机会 +12.5%, 地方贵族/商帮满意, 教会不满"
                    },
                    Att = new[]
                    {
                        A(2, 8, -4, 0, -6, -4, -8, 0),
                        A(2, 2, 0, 14, 2, 2, 4, 0),
                        A(6, -4, 12, -10, 8, 8, 12, 2)
                    }
                },
                new LawDef
                {
                    Id = "migration", Name = "移居自由", Cat = "民权",
                    Tiers = new[] { "封闭", "控制", "自由" },
                    Effect = new[]
                    {
                        "封闭: 迁移吸引力 ×0.5, 城市人口增长慢",
                        "控制: 迁移吸引力 ×1.0",
                        "自由: 迁移吸引力 ×1.3, 商帮/农民满意"
                    },
                    Att = new[]
                    {
                        A(6, 6, 2, 4, -10, 4, -6, 2),
                        A(0, 0, 0, 0, 0, 0, 0, 0),
                        A(-2, -4, 0, -2, 14, 2, 10, 0)
                    }
                },
                new LawDef
                {
                    Id = "labor", Name = "劳工权利", Cat = "民权",
                    Tiers = new[] { "无劳工权利", "监管机构", "工人保护" },
                    Effect = new[]
                    {
                        "无劳工权利: 无工作安全机构; 商帮满意",
                        "监管机构: 启用工作安全机构(每级 -20% 危险工作)",
                        "工人保护: 启用工作安全机构(+10% 工资目标/级, 商帮强烈反对)"
                    },
                    Att = new[]
                    {
                        A(0, 0, 0, 0, 0, 0, 0, 0),
                        A(0, 0, 0, 10, -10, 10, 4, 0),
                        A(0, 0, 0, 0, -20, 20, 12, 0)
                    }
                },
                new LawDef
                {
                    Id = "assoc", Name = "劳工组织", Cat = "民权",
                    Tiers = new[] { "行会制度", "禁止结社", "结社自由", "国有化行会" },
                    Effect = new[]
                    {
                        "行会制度: 行会力量 +20%, 商帮强烈反对, 手工业者受益",
                        "禁止结社: 行会力量 -25%, 运动激进 +25%",
                        "结社自由: 行会力量 +10%, 运动激进 -10%, 商帮不满",
                        "国有化行会: 行会并入国家, 激进/忠诚波动 +15%, 教会支持"
                    },
                    Att = new[]
                    {
                        A(0, 0, 0, 10, -20, 20, -10, 0),
                        A(0, 0, 0, 0, 10, 10, -20, 0),
                        A(0, 0, 0, -10, -20, -10, 20, 0),
                        A(0, 0, 0, 20, 0, 0, 0, 0)
                    }
                },

                // ================= 特有法律(v4.186, 文档 24.12 用户要求) =================
                // 硬编码: 按文化归属生效(Scope="c:文化id"); 未编码的国家/地图只用上面的通用 23 部
                new LawDef
                {
                    Id = "senate", Name = "元老院传统(特有)", Cat = "权力", Scope = "c:empire",
                    Tiers = new[] { "废弃元老院", "恢复元老院", "大元老院" },
                    Effect = new[]
                    {
                        "废弃: 权威 +2/日, 权贵与地方不满",
                        "恢复: 合法性 +8, 权贵满意, 权威 -1/日",
                        "大元老院: 合法性 +15, 立法成功 +10%, 王权不满"
                    },
                    Att = new[]
                    {
                        A(10, -12, -8, 0, 0, 0, 0, 0),
                        A(-4, 12, 8, 4, 4, 4, 4, -2),
                        A(-10, 16, 12, 6, 6, 6, 8, -6)
                    }
                },
                new LawDef
                {
                    Id = "chivalry", Name = "采邑骑士(特有)", Cat = "权力", Scope = "c:vlandia",
                    Tiers = new[] { "解散骑士团", "册封骑士", "大采邑制" },
                    Effect = new[]
                    {
                        "解散: 军队不满, 税收 +3%",
                        "册封: 军队满意 +12, 贵族忠诚 +5, 维持费 +10%",
                        "大采邑: 骑兵战力 +15%, 王权 -1/日, 税收 +5%"
                    },
                    Att = new[]
                    {
                        A(4, -8, -4, 0, 6, 0, 4, -16),
                        A(-2, 10, 8, 4, 0, 0, -4, 14),
                        A(-10, 16, 12, 6, -4, -4, -8, 18)
                    }
                },
                new LawDef
                {
                    Id = "veche", Name = "村社大会(特有)", Cat = "权力", Scope = "c:sturgia",
                    Tiers = new[] { "取缔村社", "承认村社", "大村社" },
                    Effect = new[]
                    {
                        "取缔: 权威 +2/日, 农民强烈不满",
                        "承认: 农民满意 +14, 地方自治, 税收 -3%",
                        "大村社: 农民/地方满意 +20, 治安 +10%, 王权 -2/日"
                    },
                    Att = new[]
                    {
                        A(12, 0, -6, 0, 0, 0, -18, 0),
                        A(-4, 0, 10, 2, 0, 4, 14, 0),
                        A(-12, -4, 16, 4, -2, 6, 20, -2)
                    }
                },
                new LawDef
                {
                    Id = "druids", Name = "德鲁伊教团(特有)", Cat = "权力", Scope = "c:battania",
                    Tiers = new[] { "禁止教团", "容忍教团", "国教教团" },
                    Effect = new[]
                    {
                        "禁止: 教会不满, 同化 +20%",
                        "容忍: 教会满意 +10, 满意度 +5",
                        "国教: 教会满意 +18, 同化 +40%, 异文化 -12 满意度"
                    },
                    Att = new[]
                    {
                        A(6, 0, 0, -18, 0, 0, -6, 0),
                        A(0, 0, 4, 10, 0, 0, 6, 0),
                        A(-2, 0, 8, 18, -4, -2, 4, 2)
                    }
                },
                new LawDef
                {
                    Id = "tribal", Name = "部族联盟(特有)", Cat = "权力", Scope = "c:khuzait",
                    Tiers = new[] { "拆分部族", "部族联盟", "大汗集权" },
                    Effect = new[]
                    {
                        "拆分: 骑兵战力 -10%, 权威 +2/日",
                        "联盟: 骑兵战力 +10%, 军队满意 +10, 税收 -2%",
                        "集权: 骑兵战力 +20%, 权威 +3/日, 部族不满"
                    },
                    Att = new[]
                    {
                        A(10, -10, 0, 0, 0, 0, 0, -14),
                        A(0, 8, 4, 2, 4, 4, 6, 10),
                        A(14, -14, -8, 0, 0, 0, -8, -6)
                    }
                },
                new LawDef
                {
                    Id = "caravan_law", Name = "沙漠商队法(特有)", Cat = "权力", Scope = "c:aserai",
                    Tiers = new[] { "封闭商道", "保护商队", "商队帝国" },
                    Effect = new[]
                    {
                        "封闭: 商帮强烈不满, 治安 +10%",
                        "保护: 商帮满意 +14, 贸易容量 +10%",
                        "商队帝国: 商帮满意 +20, 贸易容量 +25%, 关税 +5%"
                    },
                    Att = new[]
                    {
                        A(4, 0, 0, 4, -18, -6, 0, 2),
                        A(0, 0, 0, 0, 14, 8, 2, 0),
                        A(-4, -4, 0, -2, 20, 10, 4, 0)
                    }
                }
            };
            DLog.Force("法律: 定义表就绪 共 " + _defs.Length + " 部(3 大类, 对经典)");
        }

        internal static LawDef Def(int i)
        {
            EnsureDefs();
            return (i >= 0 && i < _defs.Length) ? _defs[i] : null;
        }

        // v4.186: 法律是否适用于当前国家 —— 空 Scope = 通用; "c:xxx" = 该文化特有; "k:xxx" = 该王国特有
        internal static bool IsAvailable(int i)
        {
            try
            {
                var d = Def(i);
                if (d == null) return false;
                if (string.IsNullOrEmpty(d.Scope)) return true;
                var pk = NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null;
                if (pk == null) return false;
                if (d.Scope.StartsWith("c:", StringComparison.Ordinal))
                {
                    string cid = d.Scope.Substring(2);
                    return pk.Culture != null && pk.Culture.StringId == cid;
                }
                if (d.Scope.StartsWith("k:", StringComparison.Ordinal))
                    return pk.StringId == d.Scope.Substring(2);
                return false;
            }
            catch { return false; }
        }

        // 该法律是否特有(UI 标记用)
        internal static bool IsSpecial(int i)
        {
            var d = Def(i);
            return d != null && !string.IsNullOrEmpty(d.Scope);
        }

        internal static int IndexOf(string id)
        {
            EnsureDefs();
            for (int i = 0; i < _defs.Length; i++) if (_defs[i].Id == id) return i;
            return -1;
        }

        internal static int LegacyToLaw(int legacyIdx)
        {
            EnsureDefs();
            for (int i = 0; i < _defs.Length; i++) if (_defs[i].Legacy == legacyIdx) return i;
            return -1;
        }

        internal static string NameOf(int i) { var d = Def(i); return d != null ? d.Name : "?"; }
        internal static string CatOf(int i) { var d = Def(i); return d != null ? d.Cat : ""; }
        internal static string TierName(int i, int tier)
        {
            var d = Def(i);
            if (d == null) return "?";
            if (tier < 0) tier = 0;
            if (tier > d.Tiers.Length - 1) tier = d.Tiers.Length - 1;
            return d.Tiers[tier];
        }

        internal static int TierCount(int i) { var d = Def(i); return d != null ? d.Tiers.Length : 4; }
        internal static int Level(int i) { return (i >= 0 && i < LawCount) ? _level[i] : 0; }
        internal static bool Maxed(int i) { return Level(i) >= TierCount(i) - 1; }
        internal static int NextTier(int i) { int c = Level(i); return c >= TierCount(i) - 1 ? TierCount(i) - 1 : c + 1; }

        internal static string EffectNow(int i) { return EffectOfTier(i, Level(i)); }
        internal static string EffectOfTier(int i, int tier)
        {
            var d = Def(i);
            if (d == null) return "";
            if (tier < 0) tier = 0;
            if (tier > d.Tiers.Length - 1) tier = d.Tiers.Length - 1;
            return d.Effect[tier];
        }

        internal static int AttOf(int law, int tier, int group)
        {
            var d = Def(law);
            if (d == null) return 0;
            if (tier < 0) tier = 0;
            if (tier > d.Tiers.Length - 1) tier = d.Tiers.Length - 1;
            if (group < 0 || group > 7) return 0;
            return d.Att[tier][group];
        }

        internal static int AdvanceStance(int law, int group)
        {
            int cur = Level(law);
            if (cur >= TierCount(law) - 1) return 0;
            return AttOf(law, cur + 1, group) - AttOf(law, cur, group);
        }

        // ================= 立法过程 =================
        internal static int Cost(int i) { return 60 + (Level(i) + 1) * 40; }
        internal static bool InBill { get { return BillLaw >= 0; } }

        // V3 形式: 支持 = 对"要更换成的目标法律"的立场(方向无关: 目标比当前更符合其立场则支持)
        internal static float SupportPctFor(int i, int target)
        {
            try
            {
                var d = Def(i);
                if (d == null) return 0.5f;
                int cur = Level(i);
                if (target < 0) target = 0;
                if (target > TierCount(i) - 1) target = TierCount(i) - 1;
                float yes = 0f, all = 0f;
                for (int g = 0; g < 8; g++)
                {
                    float w = InterestGroups.CloutOf(g);
                    all += w;
                    int diff = d.Att[target][g] - d.Att[cur][g];
                    if (diff > 0) yes += w * Math.Min(1f, diff / 10f);
                }
                return all > 0f ? ClampF(yes / all, 0f, 1f) : 0.5f;
            }
            catch { return 0.5f; }
        }

        internal static float OpposePctFor(int i, int target)
        {
            try
            {
                var d = Def(i);
                if (d == null) return 0.2f;
                int cur = Level(i);
                if (target < 0) target = 0;
                if (target > TierCount(i) - 1) target = TierCount(i) - 1;
                float no = 0f, all = 0f;
                for (int g = 0; g < 8; g++)
                {
                    float w = InterestGroups.CloutOf(g);
                    all += w;
                    int diff = d.Att[target][g] - d.Att[cur][g];
                    if (diff < 0) no += w * Math.Min(1f, -diff / 10f);
                }
                return all > 0f ? ClampF(no / all, 0f, 1f) : 0.2f;
            }
            catch { return 0.2f; }
        }

        // 兼容: 无目标 = 推进到下一档
        internal static float SupportPct(int i) { return SupportPctFor(i, NextTier(i)); }
        internal static float OpposePct(int i) { return OpposePctFor(i, NextTier(i)); }

        // V3 官方: 检查点两种主概率(支持者 -> 成功几率; 反对者 -> 僵局几率)
        internal static int SuccessPctFor(int i, int target)
        {
            float net = SupportPctFor(i, target) - OpposePctFor(i, target);
            return Clamp((int)Math.Round(35f + net * 50f), 5, 95);
        }

        internal static int StallPctFor(int i, int target)
        {
            float net = SupportPctFor(i, target) - OpposePctFor(i, target);
            return Clamp((int)Math.Round(10f - net * 40f), 0, 60);
        }

        internal static int SuccessPct(int i) { return SuccessPctFor(i, NextTier(i)); }
        internal static int StallPct(int i) { return StallPctFor(i, NextTier(i)); }

        // 立案(旧接口: 推进到下一档)
        internal static string StartBill(int law, bool force) { return StartBillTo(law, NextTier(law), force); }

        // V3 形式立案: 选择组内任意"法律"(档位, 可倒退) -> 立案 -> 检查点推进 -> 达成则更换
        internal static string StartBillTo(int law, int target, bool force)
        {
            try
            {
                var d = Def(law);
                if (d == null) return "法律不存在";
                if (!IsAvailable(law)) return d.Name + " 不适用于当前国家(特有法律)";   // v4.186
                if (target < 0) target = 0;
                if (target > TierCount(law) - 1) target = TierCount(law) - 1;
                if (target == Level(law)) return d.Name + " 当前已是 " + TierName(law, target);
                if (InBill) return "正在审议《" + NameOf(BillLaw) + "》, 请先等其落定或搁置";
                if (Politics.Legitimacy < 25f && !InterestGroups.HasMovementFor(law))
                    return "合法性不足(需 25, 当前 " + ((int)Politics.Legitimacy) + "); 除非有集团运动推动";
                int dist = Math.Abs(target - Level(law));
                int cost = 40 + dist * 30;      // V3 形式: 换得越远越贵
                if (force) cost = (int)(cost * 1.5f);
                if (Politics.Authority < cost) return "权威不足(需 " + cost + ", 当前 " + ((int)Politics.Authority) + ")";
                Politics.Authority -= cost;
                BillLaw = law; BillTarget = target; BillProgress = force ? 25f : 0f;
                BillStartDay = Politics.Today(); LastCheckDay = Politics.Today();
                BillStallDays = 0; BillForced = force; BillPaused = false; LastOutcome = force ? "强推立案" : "立案";
                if (force)
                {
                    Politics.Tyranny += 1.5f;
                    Politics.Legitimacy = Politics.ClampF(Politics.Legitimacy - 3f, 0f, 100f);
                    for (int g = 0; g < 8; g++) InterestGroups.AddEventMod(g, -6);
                }
                string head = force ? "已强推立案: 《" : "已立案: 《";
                DLog.Force("法律: 立案 -> " + d.Name + " → " + d.Tiers[target] + (force ? " [强推]" : "")
                    + " 成本=" + cost + " 支持=" + (int)(SupportPctFor(law, target) * 100) + "% 反对=" + (int)(OpposePctFor(law, target) * 100) + "%");
                return head + d.Name + "》→ " + d.Tiers[target] + " (权威 -" + cost + ")"
                    + (force ? " [强推: 合法性 -3, 各集团不满]" : "");
            }
            catch (Exception ex) { DLog.Force("立案失败: " + ex.Message); return "立案失败"; }
        }

        internal static string CancelBill()
        {
            try
            {
                if (!InBill) return "当前没有在审议的法案";
                string nm = NameOf(BillLaw);
                ClearBill();
                DLog.Force("法律: 搁置法案 -> " + nm);
                return "已搁置《" + nm + "》(已付权威不退)";
            }
            catch { return "搁置失败"; }
        }

        // 强制推进一档(v4.134: 革命胜利 -> 诉求法直接生效, V3 官方革命机制)
        internal static void ForceAdvance(int law)
        {
            try
            {
                if (law < 0 || law >= LawCount || Maxed(law)) return;
                int oldTier = _level[law];
                _level[law] = NextTier(law);
                SyncLegacy();
                Notify("强制颁布: 《" + NameOf(law) + "》→ " + TierName(law, _level[law]), true);
                DLog.Force("法律: 革命强制推进 -> " + NameOf(law));
            }
            catch { }
        }

        internal static string PhaseText()
        {
            if (BillProgress < 30f) return "拟稿";
            if (BillProgress < 75f) return "辩论";
            return "表决";
        }

        internal static void TickDay(int day)
        {
            try
            {
                if (!InBill) return;
                var d = Def(BillLaw);
                if (d == null) { BillLaw = -1; return; }
                if (BillTarget < 0 || BillTarget > TierCount(BillLaw) - 1) BillTarget = NextTier(BillLaw);
                // V3 官方: 政府非法(<25)时完全不能立法(运动支持的除外)
                if (InterestGroups.LegitLevel() == 0 && !InterestGroups.HasMovementFor(BillLaw))
                {
                    if (!BillPaused)
                    {
                        BillPaused = true;
                        Notify("立法停滞: 政府合法性不足(需 25), 《" + d.Name + "》无法推进", true);
                        DLog.Force("法律: 合法性不足, 法案暂停 -> " + d.Name);
                    }
                    return;
                }
                if (BillPaused)
                {
                    BillPaused = false;
                    Notify("合法性恢复, 《" + d.Name + "》恢复审议", false);
                }
                // V3 官方形式: 检查点机制(每 14 天一次, 四种结果之一: 成功推进/稳步推进/僵局/辩论)
                if (day - LastCheckDay < CheckpointDays) return;
                LastCheckDay = day;
                float sup = SupportPctFor(BillLaw, BillTarget), opp = OpposePctFor(BillLaw, BillTarget);
                float net = sup - opp;
                float succ = ClampF(0.35f + net * 0.5f, 0.05f, 0.95f) * InterestGroups.LawSpeedMult();
                if (succ > 0.97f) succ = 0.97f;
                float stall = ClampF(0.10f - net * 0.4f, 0f, 0.6f);
                float r = MBRandom.RandomFloat;
                if (r < succ) { BillProgress += 25f; BillStallDays = 0; LastOutcome = "成功推进"; }
                else if (r < succ + 0.15f) { BillProgress += 12f; BillStallDays = 0; LastOutcome = "稳步推进"; }
                else if (r < succ + 0.15f + stall) { BillProgress -= 8f; BillStallDays++; LastOutcome = "僵局"; }
                else { BillProgress += 3f; LastOutcome = "继续辩论"; }
                if (BillProgress < 0f) BillProgress = 0f;
                InterestGroups.NotifyPlayer("立法检查点: 《" + d.Name + "》" + LastOutcome + " · 进度 " + (int)BillProgress + "/100 · 阶段 " + PhaseText(), LastOutcome == "僵局");
                DLog.Force("法律: 检查点 -> " + d.Name + " " + LastOutcome + " 进度=" + (int)BillProgress
                    + " 支持=" + (int)(sup * 100) + "% 反对=" + (int)(opp * 100) + "% 成功p=" + (int)(succ * 100) + "% 僵局p=" + (int)(stall * 100) + "%");
                if (BillProgress >= 100f)
                {
                    int oldTier = _level[BillLaw];
                    _level[BillLaw] = BillTarget;      // V3 形式: 直接更换为目标法律(支持任意档位)
                    int newTier = _level[BillLaw];
                    SyncLegacy();
                    Notify("法令颁布: 《" + d.Name + "》→ " + TierName(BillLaw, newTier), false);
                    DLog.Force("法律: 通过 -> " + d.Name + " = " + TierName(BillLaw, newTier));
                    // V3 官方: 法律变更对 approval 的冲击(1 档 ±5 / 2 档 ±10 / 3+ 档 ±20, ±20 标度 -> 我们 ±10/±20/±40)
                    for (int g = 0; g < 8; g++)
                    {
                        int diff = d.Att[newTier][g] - d.Att[oldTier][g];
                        if (diff == 0) continue;
                        int abs = Math.Abs(diff);
                        int shift = abs >= 15 ? 40 : (abs >= 8 ? 20 : 10);
                        InterestGroups.AddEventMod(g, diff > 0 ? shift : -shift);
                    }
                    InterestGroups.OnLawPassed(BillLaw);
                    OnLawChanged(BillLaw);
                    ClearBill();
                }
                else if (BillStallDays >= 5)
                {
                    Notify("法案搁浅: 《" + d.Name + "》遭抵制, 退回重议(合法性 -1)", true);
                    DLog.Force("法律: 法案搁浅 -> " + d.Name + " (支持 " + (int)(sup * 100) + "% 反对 " + (int)(opp * 100) + "%)");
                    Politics.Legitimacy = Politics.ClampF(Politics.Legitimacy - 1f, 0f, 100f);
                    ClearBill();
                }
            }
            catch (Exception ex) { DLog.Force("立法日结异常: " + ex.Message); }
        }

        private static void ClearBill()
        {
            BillLaw = -1; BillTarget = -1; BillProgress = 0f; BillStallDays = 0; BillForced = false; BillPaused = false;
        }

        internal static void Reset()
        {
            try
            {
                EnsureDefs();
                for (int i = 0; i < LawCount; i++) _level[i] = 0;
                BillLaw = -1; BillTarget = -1; BillProgress = 0f; BillStartDay = -9999; LastCheckDay = -9999; BillStallDays = 0; BillForced = false; BillPaused = false; LastOutcome = "";
                _loaded = false; _migrated = false;
                SyncLegacy();
                DLog.Force("法律: 初始化(全 0 档)");
            }
            catch { }
        }

        // 旧 6 法令(第 21 章)迁移
        internal static void MigrateLegacy(int[] oldLaws)
        {
            try
            {
                if (_migrated || _loaded || oldLaws == null) return;
                _migrated = true;
                bool any = false;
                for (int i = 0; i < 6; i++) if (oldLaws[i] != 0) any = true;
                if (!any) return;
                EnsureDefs();
                for (int i = 0; i < _defs.Length; i++)
                {
                    int li = _defs[i].Legacy;
                    if (li >= 0 && li < 6) _level[i] = Clamp(oldLaws[li], 0, TierCount(i) - 1);
                }
                DLog.Force("法律: 旧 6 法令已并入新法律体系");
            }
            catch { }
        }

        // 旧 11 部法存档 -> 19 部法迁移(索引映射)
        private static readonly int[] OldToNew =
        {
            LPolity,        // 0 polity
            LSuccession,    // 1 succession
            LJustice,       // 2 justice
            LTax,           // 3 noble_tax
            LLevy,          // 4 levy
            LChurch,        // 5 church_land -> church
            LChurch,        // 6 church_state -> church
            LCharter,       // 7 charter
            LTrade,         // 8 trade_policy
            LRights,        // 9 civil_rights
            LLevy           // 10 conscription -> levy(合并)
        };

        internal static void SyncLegacy()
        {
            try
            {
                EnsureDefs();
                for (int i = 0; i < _defs.Length; i++)
                {
                    int li = _defs[i].Legacy;
                    if (li >= 0 && li < 6) Politics.Laws[li] = _level[i];
                }
                TaxPolicy.Recompute();
            }
            catch { }
        }

        // 法律变化后的系统联动
        private static void OnLawChanged(int law)
        {
            try
            {
                // 铸币权 -> 成色档
                if (law == LCoinage)
                {
                    int lv = Level(LCoinage);
                    MintRight.SetPurity(lv == 0 ? 0 : (lv == 1 ? 1 : 2));
                }
                // 土地制度 -> 农民迁移
                DLog.Force("法律: 联动刷新 -> " + NameOf(law));
            }
            catch { }
        }

        // ================= 系统乘数(接既有系统) =================
        internal static float ExtraTaxMult()      // 政体 + 官僚 + 土地(教会/税赋由旧公式经 Laws[] 处理)
        {
            try
            {
                float m = 1f;
                m += 0.03f * Level(LPolity);                 // 集权税收
                m += 0.05f * (Level(LBureaucracy) - 1);      // 官僚制
                m += 0.05f * (Level(LLand) == 1 ? 1f : 0f);  // 佃农制
                return m;
            }
            catch { return 1f; }
        }

        internal static float ExtraMonthRegen()   // 权威恢复: 政体 + 教会 + 言论
        {
            try
            {
                float r = 2f * Level(LPolity);
                if (Level(LChurch) >= 2) r += 2f;
                if (Level(LChurch) >= 3) r += 2f;
                if (Level(LSpeech) == 0) r += 2f;
                else if (Level(LSpeech) == 1) r += 1f;
                return r;
            }
            catch { return 0f; }
        }

        internal static float IdeologyMult()      // 对立惩罚乘数: 政体 + 权力分配
        {
            try
            {
                float m = 1f;
                switch (Level(LPolity))
                {
                    case 2: m *= 1.25f; break;
                    case 3: m *= 1.5f; break;
                }
                switch (Level(LFranchise))
                {
                    case 0: m *= 0.9f; break;
                    case 1: m *= 1.1f; break;
                    case 2: m *= 1.1f; break;
                    case 3: m *= 0.9f; break;
                }
                return m;
            }
            catch { return 1f; }
        }

        internal static int CabinetLimit()        // 内阁位: 权力分配 + 政体
        {
            try
            {
                int n = 4;
                switch (Level(LFranchise))
                {
                    case 0: n = 5; break;
                    case 1: n = 4; break;
                    case 2: n = 3; break;
                    case 3: n = 4; break;
                }
                if (Level(LPolity) >= 2) n -= 1;
                if (n < 2) n = 2;
                return n;
            }
            catch { return 4; }
        }

        internal static void StratumMults(out float up, out float mid, out float low)   // 阶层政治力量(权力分配)
        {
            try
            {
                switch (Level(LFranchise))
                {
                    case 0: up = 1.6f; mid = 1.1f; low = 0.6f; break;
                    case 1: up = 1.35f; mid = 1.15f; low = 0.85f; break;
                    case 2: up = 1.15f; mid = 1.2f; low = 1.05f; break;
                    default: up = 1.0f; mid = 1.1f; low = 1.5f; break;
                }
                // 政体加成: 集权抬高上层
                if (Level(LPolity) >= 2) { up *= 1.3f; }
                else if (Level(LPolity) == 1) { up *= 1.15f; }
                // 土地制度: 农民(下层)力量
                if (Level(LLand) == 0) low *= 0.7f;
                else if (Level(LLand) == 3) low *= 0.85f;
                // 官僚制
                if (Level(LBureaucracy) == 0) up *= 1.1f;
                // 言论自由抬高下层
                if (Level(LSpeech) >= 2) low *= 1.1f;
            }
            catch { up = 1f; mid = 1f; low = 1f; }
        }

        internal static float MarketTaxMult()     // 关税政策
        {
            try { return 1f + 0.06f * (Level(LTrade) - 1); } catch { return 1f; }
        }

        internal static float AttractMult()       // 移居自由
        {
            try
            {
                switch (Level(LMigration))
                {
                    case 0: return 0.5f;
                    case 2: return 1.3f;
                    default: return 1f;
                }
            }
            catch { return 1f; }
        }

        internal static float CivilRadicalDaily() // 平民权利 + 言论 + 福利
        {
            try
            {
                float d = -0.02f * Level(LRights);
                if (Level(LSpeech) >= 2) d -= 0.02f;
                if (Level(LWelfare) >= 1) d -= 0.01f;
                if (Level(LWelfare) >= 2) d -= 0.01f;
                if (Level(LCoinage) >= 2) d += 0.01f;
                if (Level(LLand) == 0) d += 0.01f;
                return d;
            }
            catch { return 0f; }
        }

        internal static float ConscriptMult()     // 军制兵源系数
        {
            try
            {
                switch (Level(LLevy))
                {
                    case 0: return 0.6f;
                    case 1: return 1.0f;
                    case 2: return 1.5f;
                    default: return 2.0f;
                }
            }
            catch { return 1f; }
        }

        internal static float MilitaryMult()      // 军制军事加成
        {
            try { return 1f + 0.04f * Level(LLevy); } catch { return 1f; }
        }

        // v4.148 V3 官方: 学校法不再直接加识字率, 而是提升"教育机会"(宗教学校 +10% / 公立学校 +12.5%)
        //   识字率只通过 Pops.EducationWeekly 向教育机会动态靠拢(长期演化)
        internal static float EducationAccessMult()
        {
            try
            {
                float m = 1f;
                switch (Level(LEducation))
                {
                    case 1: m += 0.10f; break;         // 教会学堂(宗教学校): +10%
                    case 2: m += 0.125f; break;        // 公立学堂(公立学校): +12.5%
                }
                m += 0.02f * Level(LRights);           // 平民权利: 教育普及
                if (Level(LSpeech) >= 2) m += 0.05f;   // 言论自由: 知识传播
                if (Level(LBureaucracy) >= 2) m += 0.05f;  // 官僚(科举): 教育机会
                return m;
            }
            catch { return 1f; }
        }

        internal static float WorkforceBonus()    // 妇女权利: 劳动力比例加成
        {
            try
            {
                switch (Level(LWomen))
                {
                    case 1: return 0.05f;
                    case 2: return 0.10f;
                    default: return 0f;
                }
            }
            catch { return 0f; }
        }

        internal static int WelfareMonthlyCost()  // 福利月支出(按全国人口/100)
        {
            try
            {
                if (Level(LWelfare) == 0) return 0;
                float pop = Pops.TotalPopulation();
                float per = Level(LWelfare) == 1 ? 1.5f : 3.0f;
                return (int)(pop / 100f * per * Institutions.WelfarePayMult());   // V3 官方: 社保机构 +20%/级
            }
            catch { return 0; }
        }

        internal static float SuppressMult()      // 国内安全/言论: 镇压效果乘数
        {
            try
            {
                float m = 1f;
                if (Level(LSpeech) == 0) m += 0.5f;
                else if (Level(LSpeech) == 1) m += 0.25f;
                else m -= 0.25f;
                return m;
            }
            catch { return 1f; }
        }

        internal static float AgriOutputMult()    // 土地制度: 农业产出
        {
            try
            {
                switch (Level(LLand))
                {
                    case 0: return 1.10f;
                    case 2: return 1.15f;
                    case 3: return 1.20f;
                    default: return 1f;
                }
            }
            catch { return 1f; }
        }

        internal static float PrivateBuildMult()  // 经济体制: 私人建造
        {
            try
            {
                switch (Level(LEconomy))
                {
                    case 0: return 1.25f;
                    case 1: return 1.5f;
                    case 2: return 1.75f;
                    default: return 0.5f;
                }
            }
            catch { return 1f; }
        }

        internal static float InterestRateMult()  // 经济体制: 贷款利率
        {
            try { return Level(LEconomy) == 2 ? 0.75f : 1f; } catch { return 1f; }
        }

        internal static float WelfareNeedMult()   // 福利 -> 下层需求满足(激进已在 CivilRadical)
        {
            try { return 1f + 0.05f * Level(LWelfare); } catch { return 1f; }
        }

        // 每月人口月度联动(由 InterestGroups.Monthly 调用)
        internal static void Monthly()
        {
            try
            {
                // v4.148: 识字率不再每月直加, 改由"教育机会"驱动(Pops.EducationWeekly 向教育机会靠拢)
                int cost = WelfareMonthlyCost();
                if (cost > 0)
                {
                    try { EconomyWorld.TreasurySpend(cost); Fiscal.AddCourt(cost); } catch { }
                }
                DLog.Force("法律月结: 教育机会 ×" + EducationAccessMult().ToString("F3") + " 福利支出 " + cost);
            }
            catch { }
        }

        // ================= 存档 FIA_Laws =================
        internal static string Save()
        {
            try
            {
                var sb = new StringBuilder();
                sb.Append("v1;");
                for (int i = 0; i < LawCount; i++) { if (i > 0) sb.Append(','); sb.Append(_level[i]); }
                sb.Append(';');
                sb.Append(BillLaw).Append(',').Append(BillProgress.ToString("F1", CultureInfo.InvariantCulture))
                  .Append(',').Append(BillStartDay).Append(',').Append(BillStallDays).Append(',').Append(BillForced ? "1" : "0")
                  .Append(',').Append(BillTarget).Append(',').Append(LastCheckDay);   // v4.142: 目标档/检查点
                sb.Append(';');
                return sb.ToString();
            }
            catch { return ""; }
        }

        internal static void Load(string data)
        {
            try
            {
                EnsureDefs();
                if (string.IsNullOrEmpty(data)) return;
                var seg = data.Split(';');
                if (seg.Length > 1)
                {
                    var lv = seg[1].Split(',');
                    if (lv.Length >= 19 && lv.Length <= LawCount)
                    {
                        // v4.186: 兼容 19/23 部旧存档(新增的 6 部特有法从 0 档起)
                        for (int i = 0; i < lv.Length && i < LawCount; i++)
                        {
                            int x;
                            if (int.TryParse(lv[i], out x)) _level[i] = Clamp(x, 0, TierCount(i) - 1);
                        }
                        if (lv.Length != LawCount) DLog.Force("法律: " + lv.Length + " 部法存档已迁移到 " + LawCount + " 部");
                    }
                    else if (lv.Length == 11)
                    {
                        // 旧 11 部法存档 -> 新体系(索引映射; 合并项取较大值)
                        var old = new int[11];
                        for (int i = 0; i < 11 && i < lv.Length; i++)
                        {
                            int x;
                            if (int.TryParse(lv[i], out x)) old[i] = Clamp(x, 0, 3);
                        }
                        for (int i = 0; i < 11; i++)
                        {
                            int ni = OldToNew[i];
                            if (ni < 0 || ni >= LawCount) continue;
                            int v = Clamp(old[i], 0, TierCount(ni) - 1);
                            if (v > _level[ni]) _level[ni] = v;
                        }
                        DLog.Force("法律: 旧 11 部法存档已迁移");
                    }
                    else if (lv.Length == 19)
                    {
                        // v4.133 的 19 部法存档 -> 23 部(v4.135 补 4 部); 前 19 位索引一致, 直读
                        for (int i = 0; i < 19 && i < lv.Length; i++)
                        {
                            int x;
                            if (int.TryParse(lv[i], out x)) _level[i] = Clamp(x, 0, TierCount(i) - 1);
                        }
                        DLog.Force("法律: 19 部法存档已迁移到 23 部法体系");
                    }
                }
                if (seg.Length > 2 && !string.IsNullOrEmpty(seg[2]))
                {
                    var f = seg[2].Split(',');
                    int x;
                    if (f.Length > 0 && int.TryParse(f[0], out x)) BillLaw = x;
                    float p;
                    if (f.Length > 1 && float.TryParse(f[1], NumberStyles.Float, CultureInfo.InvariantCulture, out p)) BillProgress = p;
                    if (f.Length > 2 && int.TryParse(f[2], out x)) BillStartDay = x;
                    if (f.Length > 3 && int.TryParse(f[3], out x)) BillStallDays = x;
                    if (f.Length > 4) BillForced = f[4] == "1";
                    if (f.Length > 5 && int.TryParse(f[5], out x)) BillTarget = x;
                    if (f.Length > 6 && int.TryParse(f[6], out x)) LastCheckDay = x;
                    if (BillLaw >= LawCount) BillLaw = -1;
                    if (BillTarget >= 4) BillTarget = -1;
                }
                _loaded = true;
                SyncLegacy();
                string all = "";
                for (int i = 0; i < LawCount; i++) all += (i > 0 ? " " : "") + TierName(i, _level[i]);
                DLog.Force("法律: 读档 " + all + (InBill ? " [审议中: " + NameOf(BillLaw) + "]" : ""));
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

        internal static int Clamp(int v, int lo, int hi) { return v < lo ? lo : (v > hi ? hi : v); }
        internal static float ClampF(float v, float lo, float hi) { return v < lo ? lo : (v > hi ? hi : v); }
    }
}
