using System;
using System.Collections.Generic;
using System.Globalization;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace FeudalInternalAffairs
{
    // 军务三页/军队页/军务总览页共用: 内核 14 兵种 -> 现役图标名(fia_unit_*)
    internal static class MilIcons
    {
        internal static string ByUnit(int kind)
        {
            switch (kind)
            {
                case Equipment.UMilita: return "fia_unit_militia";
                case Equipment.ULine: return "fia_unit_line_infantry";
                case Equipment.ULightInf: return "fia_unit_skirmisher";
                case Equipment.UGrenadier: return "fia_unit_grenadier";
                case Equipment.UDragoon: return "fia_unit_dragoon";
                case Equipment.UHussar: return "fia_unit_hussar";
                case Equipment.UCuirassier: return "fia_unit_cuirassier";
                case Equipment.UArcher: return "fia_unit_archer";
                case Equipment.UCrossbow: return "fia_unit_crossbowman";
                case Equipment.UHorseArcher: return "fia_unit_horse_archer";
                case Equipment.UFieldArtillery: return "fia_unit_field_artillery";
                case Equipment.USiegeArtillery: return "fia_unit_siege_artillery";
                case Equipment.UHorseArtillery: return "fia_unit_horse_artillery";
                case Equipment.UEngineer: return "fia_unit_engineer";
                default: return "fia_unit_line_infantry";
            }
        }

        // 分支: 0 步 1 弓 2 骑 3 骑射(与 Equipment.UnitOfBranch 分支号一致)
        internal static string ByBranch(int branch)
        {
            switch (branch)
            {
                case 1: return "fia_unit_archer";
                case 2: return "fia_unit_hussar";
                case 3: return "fia_unit_horse_archer";
                default: return "fia_unit_line_infantry";
            }
        }

        // 兵种 id -> 现役图标(库中实际存在的 fia_unit_*)
        internal static string IconOf(string unitId)
        {
            try
            {
                if (!string.IsNullOrEmpty(unitId))
                    for (int i = 0; i < Equipment.Units.Count; i++)
                        if (Equipment.Units[i] != null && Equipment.Units[i].Id == unitId)
                            return ByUnit(i);
            }
            catch { }
            return "fia_tech_ph";
        }

        // 装备 id -> 现役图标(库中实际存在的 fia_equip_*; 缺图退占位)
        internal static string EquipIconOf(string equipId)
        {
            if (string.IsNullOrEmpty(equipId)) return "fia_tech_ph";
            switch (equipId)
            {
                case "caplock": return "fia_equip_percussion";
                case "minie_rifle": return "fia_equip_minie";
                case "breech_rifle": return "fia_equip_breechloader";
                case "repeater_rifle": return "fia_equip_repeater";
                case "short_spear": return "fia_equip_spear";
                case "long_sword": return "fia_equip_sword";
                case "twohanded_sword": return "fia_equip_greatsword";
                case "half_plate": return "fia_equip_plate_armor";
                case "steel_helmet": return "fia_equip_helmet";
                case "gun_carriage": return "fia_equip_gun_crew";
            }
            string s = "fia_equip_" + equipId;
            return EquipIcons.Contains(s) ? s : "fia_tech_ph";
        }

        private static readonly HashSet<string> EquipIcons = new HashSet<string>(StringComparer.Ordinal)
        {
            "fia_equip_ammo_kit", "fia_equip_breech_cannon", "fia_equip_breechloader", "fia_equip_cannon12",
            "fia_equip_cannon6", "fia_equip_carbine", "fia_equip_chainmail", "fia_equip_congreve",
            "fia_equip_crossbow", "fia_equip_cuirass", "fia_equip_field_hospital", "fia_equip_flintlock",
            "fia_equip_gatling", "fia_equip_greatsword", "fia_equip_grenade", "fia_equip_gun_crew",
            "fia_equip_halberd", "fia_equip_heavy_warhorse", "fia_equip_helmet", "fia_equip_howitzer",
            "fia_equip_hunting_bow", "fia_equip_lance", "fia_equip_leather_armor", "fia_equip_light_cannon",
            "fia_equip_longbow", "fia_equip_matchlock", "fia_equip_maxim", "fia_equip_minie", "fia_equip_nag",
            "fia_equip_percussion", "fia_equip_plate_armor", "fia_equip_repeater", "fia_equip_rifled_cannon",
            "fia_equip_saber", "fia_equip_spear", "fia_equip_sword", "fia_equip_telegraph", "fia_equip_tools",
            "fia_equip_warhorse"
        };

        // 兵种 id -> 构成类别(0 步 1 弓 2 骑 3 骑射 4 其它)
        internal static int CategoryOfUnitId(string unitId)
        {
            try
            {
                for (int i = 0; i < Equipment.Units.Count; i++)
                    if (Equipment.Units[i] != null && Equipment.Units[i].Id == unitId) return CategoryOfUnit(i);
            }
            catch { }
            return 4;
        }

        internal static int CategoryOfUnit(int kind)
        {
            switch (kind)
            {
                case Equipment.UMilita:
                case Equipment.ULine:
                case Equipment.ULightInf:
                case Equipment.UGrenadier: return 0;
                case Equipment.UArcher:
                case Equipment.UCrossbow: return 1;
                case Equipment.UHussar:
                case Equipment.UCuirassier: return 2;
                case Equipment.UDragoon:
                case Equipment.UHorseArcher: return 3;
                default: return 4;
            }
        }

        // 超长文本截断加省略号(定宽列防出框)
        internal static string Clip(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.Length <= max) return s;
            return s.Substring(0, max) + "…";
        }
    }

    // 不依赖区域设置的数字格式(系统区域可能把千分位显示成 '.'): <1 万走 N0+不变文化, ≥1 万折成 "X[.X] 万"
    internal static class MilFmt
    {
        internal static string N(long v)
        {
            try
            {
                if (v >= 10000L || v <= -10000L)
                    return (v / 10000.0).ToString("0.#", CultureInfo.InvariantCulture) + " 万";
                return v.ToString("N0", CultureInfo.InvariantCulture);
            }
            catch { return v.ToString(CultureInfo.InvariantCulture); }
        }
    }

    // 国库余额: 玩家王国优先走 WarEconomy(玩家国 = EconomyWorld.Treasury, 兼容未建国), 取不到再退 EconomyWorld
    internal static class MilTreasury
    {
        internal static int Gold()
        {
            // 玩家王国 = Clan.PlayerClan.Kingdom(即 DefArmy.OurKingdom/NationKingdom)
            try
            {
                var k = Clan.PlayerClan != null ? Clan.PlayerClan.Kingdom : null;
                if (k == null) k = DefArmy.OurKingdom;
                // 只有玩家国才走 WarEconomy 口径(玩家国映射 = EconomyWorld.Treasury; AI 国缓存对本 UI 无意义)
                if (k != null && k == DefArmy.OurKingdom)
                {
                    try
                    {
                        float g = WarEconomy.KingdomGold(k);
                        if (g > 0.5f) return (int)g;
                    }
                    catch { }
                }
            }
            catch { }
            try { return (int)EconomyWorld.Treasury.Gold; } catch { return 0; }
        }
    }

    // 军团一行(军务页): 图标 + 番号 + 兵力/士气/组织度/装备满足率/老兵度(内核口径)
    public class LegionRowVM : ViewModel
    {
        internal readonly string PartyId;
        internal readonly string HomeId;      // v4.58: 驻地(点城市名跳视角)
        private readonly string _name, _men, _menColor, _morale, _moraleColor, _org, _orgColor,
            _equip, _equipColor, _vet, _vetColor, _home, _task, _icon;
        // v4.243: 军团状态 / 训练度 / 将军特质
        private readonly string _train, _trainColor, _trait, _stanceText, _stanceColor;

        internal LegionRowVM(DefLegion lg, MobileParty p)
        {
            PartyId = lg != null ? lg.PartyId : "";
            HomeId = lg != null ? lg.HomeId : "";
            string kname = DefArmy.OurKingdom != null && DefArmy.OurKingdom.Name != null
                ? DefArmy.OurKingdom.Name.ToString()
                : "王国";
            string full = lg != null
                ? kname + "国防军第" + lg.Number + "军团"
                : "国防军·未编成";
            _name = MilIcons.Clip(full, 13);
            int men = DefArmy.LegionMen(lg);
            _men = MilFmt.N(men) + " 兵";
            _menColor = men >= 1000 ? "#E8C33AFF" : "#F0E4C8FF";

            string key = "";
            try { key = ArmyDoctrine.LegionKey(lg); } catch { }
            int mor = 100;
            try { mor = (int)Math.Round(ArmyDoctrine.MoraleOf2(key)); } catch { }
            if (mor > 100) mor = 100;
            if (mor < 0) mor = 0;
            _morale = mor.ToString();
            _moraleColor = mor >= 60 ? "#7FBF6AFF" : (mor >= 30 ? "#E8C33AFF" : "#C96A5AFF");
            int org = 100;
            try { org = (int)Math.Round(ArmyDoctrine.OrgOf2(key)); } catch { }
            if (org > 100) org = 100;
            if (org < 0) org = 0;
            _org = org.ToString();
            _orgColor = org >= 70 ? "#7FBF6AFF" : (org >= 40 ? "#E8C33AFF" : "#C96A5AFF");
            int fill = 100;
            try { fill = (int)Math.Round(DefArmy.EquipFillOf(lg) * 100f); } catch { }
            if (fill > 100) fill = 100;
            if (fill < 0) fill = 0;
            _equip = fill + "%";
            _equipColor = fill >= 90 ? "#7FBF6AFF" : (fill >= 60 ? "#E8C33AFF" : "#C96A5AFF");
            int vet = 0;
            try { vet = (int)Math.Round(ArmyDoctrine.VetOf(key)); } catch { }
            if (vet > 100) vet = 100;
            if (vet < 0) vet = 0;

            // v4.243: 老兵占比(熟练度 ≥70 的人数比例)取代行内"老兵度"(老兵度挪进质量行)
            try
            {
                float avgP;
                float share = p != null ? Soldiers.VeteranShare(p, 70, out avgP) : 0f;
                _vet = "老兵 " + (int)Math.Round(share * 100f) + "%";
                _vetColor = share >= 0.5f ? "#E8A33AFF" : (share >= 0.2f ? "#D8C9A0FF" : "#8A8070FF");
            }
            catch { _vet = "老兵 —"; _vetColor = "#8A8070FF"; }

            // v4.243: 训练度 + 状态 + 将军特质
            float train = 0f;
            try { train = DefArmy.TrainLevelOf(lg); } catch { }
            _train = "训练 " + (int)Math.Round(train);
            _trainColor = train >= 80f ? "#E8C33AFF" : (train >= 50f ? "#7FBF6AFF" : "#C96A5AFF");
            int st = 0;
            try { st = DefArmy.StanceOf(lg); } catch { }
            _stanceText = DefArmy.StanceNames[st];
            _stanceColor = st == DefArmy.StanceDrill ? "#7FBF6AFF"
                : (st == DefArmy.StanceRest ? "#8FD8E8FF"
                : (st == DefArmy.StanceMarch ? "#E8C33AFF"
                : (st == DefArmy.StanceForage ? "#C96A5AFF" : "#D8C9A0FF")));
            string tr = "";
            try { tr = lg != null ? DefArmy.TraitText(lg.GeneralTraits) : ""; } catch { }
            _trait = MilIcons.Clip(tr, 11);

            _home = lg != null && lg.HomeId != null ? ShortName(lg.HomeId) : "—";
            _task = lg != null ? (lg.Task ?? "驻守") : "—";
            if (lg != null) _home = MilIcons.Clip(_home + " · " + _task, 11);   // v4.243: 驻地 + 当前任务(驻防/巡逻)

            int[] comp = new int[4];
            int best = -1;
            try
            {
                comp = DefArmy.CompOf(lg);
                for (int i = 0; i < 4; i++) if (comp[i] > 0 && (best < 0 || comp[i] > comp[best])) best = i;
            }
            catch { best = -1; }
            _icon = best >= 0 ? MilIcons.ByBranch(best) : "fia_unit_militia";
        }

        private static string ShortName(string sid)
        {
            try
            {
                var s = DefArmy.FindSettlement(sid);
                return s != null && s.Name != null ? s.Name.ToString() : sid;
            }
            catch { return sid; }
        }

        [DataSourceProperty] public string Icon { get { return _icon; } }
        [DataSourceProperty] public string Name { get { return _name; } }
        [DataSourceProperty] public string Men { get { return _men; } }
        [DataSourceProperty] public string MenColor { get { return _menColor; } }
        [DataSourceProperty] public string Morale { get { return _morale; } }
        [DataSourceProperty] public string MoraleColor { get { return _moraleColor; } }
        [DataSourceProperty] public string Org { get { return _org; } }
        [DataSourceProperty] public string OrgColor { get { return _orgColor; } }
        [DataSourceProperty] public string Equip { get { return _equip; } }
        [DataSourceProperty] public string EquipColor { get { return _equipColor; } }
        [DataSourceProperty] public string Vet { get { return _vet; } }
        [DataSourceProperty] public string VetColor { get { return _vetColor; } }
        // v4.243: 训练度 / 状态 / 将军特质
        [DataSourceProperty] public string Train { get { return _train; } }
        [DataSourceProperty] public string TrainColor { get { return _trainColor; } }
        [DataSourceProperty] public string Trait { get { return _trait; } }
        [DataSourceProperty] public string StanceText { get { return _stanceText; } }
        [DataSourceProperty] public string StanceColor { get { return _stanceColor; } }
        [DataSourceProperty] public string Home { get { return _home; } }
        [DataSourceProperty] public string Task { get { return _task; } }
    }

    // v4.2xx: 铁路运输引擎开销文案(军团开关与部队小窗共用)
    internal static class RailEngineInfo
    {
        internal static float PerDayOf(MobileParty p)
        {
            try
            {
                int men = 0;
                try { if (p != null && p.Party != null) men = p.Party.NumberOfRegularMembers; } catch { }
                if (men <= 0) { try { men = p != null && p.MemberRoster != null ? p.MemberRoster.TotalManCount : 0; } catch { } }
                return men / 1000f * ArmyDoctrine.RailEnginePer1000Daily;
            }
            catch { return 0f; }
        }

        // 日耗引擎 X（兵力/1000×0.5）; 能取到随军引擎库存时补 余额/可维持天数
        internal static string TextOf(MobileParty p)
        {
            float per = PerDayOf(p);
            string s = "日耗引擎 " + per.ToString("0.###", CultureInfo.InvariantCulture) + "（兵力/1000×0.5）";
            try
            {
                var item = FeudalGoods.Item(FeudalGoods.Engines);
                var roster = p != null ? p.ItemRoster : null;
                if (item == null || roster == null) return s;
                int have = roster.GetItemNumber(item);
                int days = per > 0.001f ? (int)(have / per) : 0;
                s += " · 余额 " + have + " · 可维持 " + days + " 天";
            }
            catch { }
            return s;
        }
    }

    // v4.193: 兵种构成行(内核 14 兵种图标 fia_unit_*; 熟练度取 Soldiers.Aggregate)
    public class UnitRowVM : ViewModel
    {
        private readonly string _icon, _name, _count, _share, _color, _prof;
        private readonly float _barW;
        internal UnitRowVM(string icon, string name, int count, float share, string color, string prof)
        {
            _icon = icon; _name = name; _count = MilFmt.N(count); _share = (share * 100f).ToString("F0", CultureInfo.InvariantCulture) + "%"; _color = color;
            _prof = prof;
            _barW = 148f * Math.Max(0f, Math.Min(1f, share));
        }
        [DataSourceProperty] public string Icon { get { return _icon; } }
        [DataSourceProperty] public string Name { get { return _name; } }
        [DataSourceProperty] public string Count { get { return _count; } }
        [DataSourceProperty] public string Share { get { return _share; } }
        [DataSourceProperty] public string Color { get { return _color; } }
        [DataSourceProperty] public string Prof { get { return _prof; } }
        [DataSourceProperty] public float BarW { get { return _barW; } }
    }

    public class GarrisonRowVM : ViewModel
    {
        internal readonly string SettlementId;
        private readonly string _name, _men, _icon;

        internal GarrisonRowVM(DefGarrison g)
        {
            SettlementId = g != null ? g.SettlementId : "";
            var s = DefArmy.FindSettlement(SettlementId);
            string nm = s != null && s.Name != null ? s.Name.ToString() : SettlementId;
            _name = MilIcons.Clip(nm, 13);
            _men = MilFmt.N(DefArmy.GarrisonMen(g)) + " 兵";   // v4.84: 显示实时驻军兵力(与建军可用数一致)
            _icon = s != null && s.IsTown ? "fia_settle_town" : "fia_settle_castle";
        }

        [DataSourceProperty] public string Icon { get { return _icon; } }
        [DataSourceProperty] public string Name { get { return _name; } }
        [DataSourceProperty] public string Men { get { return _men; } }
    }

    // 军务页 VM(导航栏第 13 格); 三页签: 募兵与征兵 / 野战军团 / 守备营
    public class ArmyPanelVM : PanelVMBase
    {
        private const int LegionPage = 3;
        private const int GarrisonPage = 4;
        private const int PlanMin = 1;
        private const int PlanMaxLim = 999999;   // v4.89: 募兵计划上限放宽

        private readonly Action _onClose;
        private readonly List<Settlement> _cands = new List<Settlement>();
        private readonly List<Settlement> _srcList = new List<Settlement>();
        private readonly List<DefLegion> _legionView = new List<DefLegion>();
        private int _candIdx;
        private int _srcIdx;
        private int _scroll;
        private int _gScroll;
        private int _plan = 100;
        private bool _planEdit;
        private bool _enterWasDown;   // v4.86: 回车确认的边沿检测
        private int _tab;   // 0 募兵/征兵 1 军团 2 守备营
        private bool _pickingSettle;

        private string _overview = "", _money = "", _quality = "", _settleName = "—", _srcName = "—", _settleInfo = "",
            _planText = "", _needRecruitText = "", _needEquipText = "", _needConscriptText = "", _haveGoldPop = "", _haveEquip = "",
            _planVerdict = "", _planVerdictColor = "", _planVerdictBg = "#00000000",
            _menText = "0", _legionText = "0", _garrisonText = "0",
            _conscriptText = "", _recruitBtn = "", _recruitCost = "", _conscriptBtn = "", _conscriptCost = "", _formLegionBtn = "",
            _status = "", _legionHelp = "", _garrisonHelp = "", _recruitHelp = "";
        private string _treasuryText = "国库：— 金", _treasuryColor = "#E8C33AFF";
        private string _recruitCostColor = "#D8C9A0FF", _conscriptCostColor = "#D8C9A0FF";
        private bool _showRecruit = true, _showLegion, _showGarrison;
        private string _tabRecruitColor = "#E8C33AFF", _tabLegionColor = "#8A8070FF", _tabGarrisonColor = "#8A8070FF";

        private string _unitsTitle = "兵种构成";
        private bool _tabLineRecruit = true, _tabLineLegion, _tabLineGarrison;
        private string _recruitBtnColor = "#7FBF6AFF", _conscriptBtnColor = "#7FBF6AFF";
        private bool _legionEmpty, _garrisonEmpty;

        public ArmyPanelVM(Action onClose)
        {
            _onClose = onClose;
            LegionRows = new MBBindingList<LegionRowVM>();
            GarrisonRows = new MBBindingList<GarrisonRowVM>();
            Units = new MBBindingList<UnitRowVM>();
            Refresh();
        }

        public MBBindingList<LegionRowVM> LegionRows { get; private set; }
        public MBBindingList<GarrisonRowVM> GarrisonRows { get; private set; }
        public MBBindingList<UnitRowVM> Units { get; private set; }        // v4.193: 兵种构成
        [DataSourceProperty] public string UnitsTitle { get { return _unitsTitle; } }

        [DataSourceProperty] public string OverviewText { get { return _overview; } }
        [DataSourceProperty] public string MoneyText { get { return _money; } }
        [DataSourceProperty] public string QualityText { get { return _quality; } }
        [DataSourceProperty] public string SettleName { get { return _settleName; } }
        [DataSourceProperty] public string SrcName { get { return _srcName; } }
        [DataSourceProperty] public string SettleInfo { get { return _settleInfo; } }
        [DataSourceProperty] public string PlanText { get { return _planText; } }
        [DataSourceProperty] public string NeedRecruitText { get { return _needRecruitText; } }
        [DataSourceProperty] public string NeedEquipText { get { return _needEquipText; } }
        [DataSourceProperty] public string NeedConscriptText { get { return _needConscriptText; } }
        [DataSourceProperty] public string HaveGoldPop { get { return _haveGoldPop; } }
        [DataSourceProperty] public string TreasuryText { get { return _treasuryText; } }
        [DataSourceProperty] public string TreasuryColor { get { return _treasuryColor; } }
        [DataSourceProperty] public string RecruitCostColor { get { return _recruitCostColor; } }
        [DataSourceProperty] public string ConscriptCostColor { get { return _conscriptCostColor; } }
        [DataSourceProperty] public string HaveEquip { get { return _haveEquip; } }
        [DataSourceProperty] public string PlanVerdictText { get { return _planVerdict; } }
        [DataSourceProperty] public string PlanVerdictColor { get { return _planVerdictColor; } }
        [DataSourceProperty] public string PlanVerdictBg { get { return _planVerdictBg; } }
        [DataSourceProperty] public string MenText { get { return _menText; } }
        [DataSourceProperty] public string LegionText { get { return _legionText; } }
        [DataSourceProperty] public string GarrisonText { get { return _garrisonText; } }
        [DataSourceProperty] public string ConscriptText { get { return _conscriptText; } }
        [DataSourceProperty] public string RecruitBtnText { get { return _recruitBtn; } }
        [DataSourceProperty] public string RecruitBtnCost { get { return _recruitCost; } }
        [DataSourceProperty] public string ConscriptBtnText { get { return _conscriptBtn; } }
        [DataSourceProperty] public string ConscriptBtnCost { get { return _conscriptCost; } }
        [DataSourceProperty] public string FormLegionBtnText { get { return _formLegionBtn; } }
        [DataSourceProperty] public string StatusText { get { return _status; } }
        [DataSourceProperty] public string LegionHelp { get { return _legionHelp; } }
        [DataSourceProperty] public string GarrisonHelp { get { return _garrisonHelp; } }
        [DataSourceProperty] public string RecruitHelp { get { return _recruitHelp; } }

        [DataSourceProperty] public bool ShowRecruit { get { return _showRecruit; } }
        [DataSourceProperty] public bool ShowLegion { get { return _showLegion; } }
        [DataSourceProperty] public bool ShowGarrison { get { return _showGarrison; } }
        [DataSourceProperty] public string TabRecruitColor { get { return _tabRecruitColor; } }
        [DataSourceProperty] public string TabLegionColor { get { return _tabLegionColor; } }
        [DataSourceProperty] public string TabGarrisonColor { get { return _tabGarrisonColor; } }
        [DataSourceProperty] public bool TabLineRecruit { get { return _tabLineRecruit; } }
        [DataSourceProperty] public bool TabLineLegion { get { return _tabLineLegion; } }
        [DataSourceProperty] public bool TabLineGarrison { get { return _tabLineGarrison; } }
        [DataSourceProperty] public string RecruitBtnColor { get { return _recruitBtnColor; } }
        [DataSourceProperty] public string ConscriptBtnColor { get { return _conscriptBtnColor; } }
        [DataSourceProperty] public bool LegionEmptyVisible { get { return _legionEmpty; } }
        [DataSourceProperty] public bool GarrisonEmptyVisible { get { return _garrisonEmpty; } }

        public void ExecuteClose() { if (_onClose != null) _onClose(); }

        internal int ShownLegionCount { get { return LegionRows.Count; } }
        internal int ShownGarrisonCount { get { return GarrisonRows.Count; } }
        internal bool PlanEditing { get { return _planEdit; } }
        internal int Plan { get { return _plan; } }
        internal int Tab { get { return _tab; } }

        internal Settlement Current
        {
            get { return _candIdx >= 0 && _candIdx < _cands.Count ? _cands[_candIdx] : null; }
        }

        // 当前募兵/征兵来源(城镇=劳工, 附属村庄=农民)
        internal Settlement Source
        {
            get { return _srcIdx >= 0 && _srcIdx < _srcList.Count ? _srcList[_srcIdx] : Current; }
        }

        internal void SetTab(int t)
        {
            try
            {
                _tab = t < 0 ? 0 : (t > 2 ? 2 : t);
                _planEdit = false;
                if (_pickingSettle) { _pickingSettle = false; ArmyPanel.SetMapPicking(false); }
                Refresh();
            }
            catch { }
        }

        // ---- 计划数量(玩家输入) ----
        internal void PlanStep(int delta)
        {
            try
            {
                if (_planEdit) _planEdit = false;
                _plan += delta;
                if (_plan < PlanMin) _plan = PlanMin;
                if (_plan > PlanMaxLim) _plan = PlanMaxLim;
                Refresh();
            }
            catch { }
        }

        internal void PlanMax()
        {
            try
            {
                _planEdit = false;
                int pop = DefArmy.CommonersOf(Source);
                _plan = Math.Min(PlanMaxLim, Math.Max(PlanMin, pop));
                if (pop <= 0) _status = "该来源当前没有可征人口";
                Refresh();
            }
            catch { }
        }

        internal void TogglePlanEdit()
        {
            try
            {
                if (_planEdit) { _planEdit = false; Refresh(); return; }
                _planEdit = true;
                _plan = 0;
                _status = "键入中: 数字键输入数量 · 退格删除 · 回车确认";
                Refresh();
            }
            catch { }
        }

        internal void PollPlanKeys()
        {
            try
            {
                if (!_planEdit) return;
                bool changed = false;
                int d = DigitPressed();
                if (d >= 0)
                {
                    long v = (long)_plan * 10 + d;
                    _plan = v > PlanMaxLim ? PlanMaxLim : (int)v;
                    changed = true;
                }
                if (TaleWorlds.InputSystem.Input.IsKeyPressed(TaleWorlds.InputSystem.InputKey.BackSpace))
                {
                    _plan /= 10;
                    changed = true;
                }
                // v4.86: 面板打开时原版读不到回车(PanelInputGuard 拦截), 这里改用按住状态自检按下边沿
                bool enterDown = TaleWorlds.InputSystem.Input.IsKeyDown(TaleWorlds.InputSystem.InputKey.Enter)
                    || TaleWorlds.InputSystem.Input.IsKeyDown(TaleWorlds.InputSystem.InputKey.NumpadEnter);
                if (enterDown && !_enterWasDown)
                {
                    if (_plan < PlanMin) _plan = 100;
                    _planEdit = false;
                    _status = "数量已设为 " + MilFmt.N(_plan);
                    changed = true;
                }
                _enterWasDown = enterDown;
                if (changed) Refresh();
            }
            catch { }
        }

        private static int DigitPressed()
        {
            if (TaleWorlds.InputSystem.Input.IsKeyPressed(TaleWorlds.InputSystem.InputKey.D0)
                || TaleWorlds.InputSystem.Input.IsKeyPressed(TaleWorlds.InputSystem.InputKey.Numpad0)) return 0;
            if (TaleWorlds.InputSystem.Input.IsKeyPressed(TaleWorlds.InputSystem.InputKey.D1)
                || TaleWorlds.InputSystem.Input.IsKeyPressed(TaleWorlds.InputSystem.InputKey.Numpad1)) return 1;
            if (TaleWorlds.InputSystem.Input.IsKeyPressed(TaleWorlds.InputSystem.InputKey.D2)
                || TaleWorlds.InputSystem.Input.IsKeyPressed(TaleWorlds.InputSystem.InputKey.Numpad2)) return 2;
            if (TaleWorlds.InputSystem.Input.IsKeyPressed(TaleWorlds.InputSystem.InputKey.D3)
                || TaleWorlds.InputSystem.Input.IsKeyPressed(TaleWorlds.InputSystem.InputKey.Numpad3)) return 3;
            if (TaleWorlds.InputSystem.Input.IsKeyPressed(TaleWorlds.InputSystem.InputKey.D4)
                || TaleWorlds.InputSystem.Input.IsKeyPressed(TaleWorlds.InputSystem.InputKey.Numpad4)) return 4;
            if (TaleWorlds.InputSystem.Input.IsKeyPressed(TaleWorlds.InputSystem.InputKey.D5)
                || TaleWorlds.InputSystem.Input.IsKeyPressed(TaleWorlds.InputSystem.InputKey.Numpad5)) return 5;
            if (TaleWorlds.InputSystem.Input.IsKeyPressed(TaleWorlds.InputSystem.InputKey.D6)
                || TaleWorlds.InputSystem.Input.IsKeyPressed(TaleWorlds.InputSystem.InputKey.Numpad6)) return 6;
            if (TaleWorlds.InputSystem.Input.IsKeyPressed(TaleWorlds.InputSystem.InputKey.D7)
                || TaleWorlds.InputSystem.Input.IsKeyPressed(TaleWorlds.InputSystem.InputKey.Numpad7)) return 7;
            if (TaleWorlds.InputSystem.Input.IsKeyPressed(TaleWorlds.InputSystem.InputKey.D8)
                || TaleWorlds.InputSystem.Input.IsKeyPressed(TaleWorlds.InputSystem.InputKey.Numpad8)) return 8;
            if (TaleWorlds.InputSystem.Input.IsKeyPressed(TaleWorlds.InputSystem.InputKey.D9)
                || TaleWorlds.InputSystem.Input.IsKeyPressed(TaleWorlds.InputSystem.InputKey.Numpad9)) return 9;
            return -1;
        }

        // ---- 操作 ----
        // v4.58: 城市名点击 -> 视野飞到该城(用户需求)
        internal void FlyToCurrent()
        {
            try
            {
                var s = Current;
                if (s == null) { _status = "尚无可选聚落(本国没有城镇/城堡)"; Refresh(); return; }
                PanelScreen.JumpToSettlement(s);
            }
            catch { }
        }

        internal void FlyToLegionHome(int i)
        {
            try
            {
                if (i < 0 || i >= LegionRows.Count) return;
                var s = DefArmy.FindSettlement(LegionRows[i].HomeId);
                if (s == null) { _status = "该军团驻地聚落不存在"; Refresh(); return; }
                PanelScreen.JumpToSettlement(s);
            }
            catch { }
        }

        internal void FlyToGarrisonHome(int i)
        {
            try
            {
                if (i < 0 || i >= GarrisonRows.Count) return;
                var s = DefArmy.FindSettlement(GarrisonRows[i].SettlementId);
                if (s == null) { _status = "该守备营聚落不存在"; Refresh(); return; }
                PanelScreen.JumpToSettlement(s);
            }
            catch { }
        }

        internal void CycleSettlement(int dir)
        {
            try
            {
                if (_cands.Count == 0) { _status = "尚无可选聚落"; Refresh(); return; }
                _planEdit = false;
                _candIdx = (_candIdx + dir) % _cands.Count;
                if (_candIdx < 0) _candIdx += _cands.Count;
                _srcIdx = 0;
                Refresh();
            }
            catch { }
        }

        // 弹窗选择城市
        internal void OpenSettlementPicker()
        {
            try
            {
                _planEdit = false;
                if (_cands.Count == 0) { _status = "本国没有可选聚落(城镇/城堡)"; Notify("本国没有可选聚落(城镇/城堡)"); return; }
                var options = new List<InquiryElement>();
                for (int i = 0; i < _cands.Count; i++)
                {
                    var s = _cands[i];
                    if (s == null) continue;
                    string kind = s.IsTown ? "城镇" : "城堡";
                    int g = 0;
                    try { g = GarrisonAvailable(s); } catch { }
                    int pop = 0;
                    try { pop = DefArmy.CommonersOf(s); } catch { }
                    string txt = (s.Name != null ? s.Name.ToString() : s.StringId)
                        + "（" + kind + "）· 可征人口 " + MilFmt.N(pop) + " · 守备营 " + MilFmt.N(g) + " 兵";
                    options.Add(new InquiryElement(s.StringId, txt, null, true, null));
                }
                PanelInputGuard.ShowPopup(new MultiSelectionInquiryData(
                    "选择征兵地点", "本国共有 " + options.Count + " 处城镇/城堡",
                    options, true, 1, 1, "确定", "取消",
                    OnSettlementPicked, null, null, false));
            }
            catch (Exception ex) { DLog.Force("选择城市弹窗异常: " + ex.Message); Notify("打开选择弹窗失败: " + ex.Message); }
        }

        private void OnSettlementPicked(List<InquiryElement> selected)
        {
            try
            {
                if (selected == null || selected.Count == 0) return;
                string id = selected[0].Identifier as string;
                for (int i = 0; i < _cands.Count; i++)
                {
                    if (_cands[i] != null && _cands[i].StringId == id)
                    {
                        _candIdx = i;
                        _srcIdx = 0;
                        _status = "已选择 " + (_cands[i].Name != null ? _cands[i].Name.ToString() : id);
                        break;
                    }
                }
                Refresh();
            }
            catch (Exception ex) { DLog.Force("选择城市异常: " + ex.Message); }
        }

        // 地图点选: 进入待选模式, 下次点击地图上的定居点时选中
        internal bool PickingSettlement { get { return _pickingSettle; } }

        internal void StartMapPick()
        {
            try
            {
                _planEdit = false;
                if (_pickingSettle)   // 再点一次 = 取消
                {
                    _pickingSettle = false;
                    ArmyPanel.SetMapPicking(false);
                    _status = "已取消地图选点";
                    Refresh();
                    return;
                }
                _pickingSettle = true;
                ArmyPanel.SetMapPicking(true);
                MapSelection.Message("请在地图上左键点击本国的城镇/城堡（再点一次[地图点选]可取消）");
                _status = "地图点选中: 请点击地图上的本国城镇/城堡...";
                Refresh();
            }
            catch { }
        }

        // 地图点选回调(settlement 为 null 表示取消)
        internal void TryPickSettlement(Settlement s)
        {
            try
            {
                _pickingSettle = false;
                ArmyPanel.SetMapPicking(false);
                if (s == null) { _status = "已取消地图选点"; Refresh(); return; }
                for (int i = 0; i < _cands.Count; i++)
                {
                    if (_cands[i] != null && _cands[i].StringId == s.StringId)
                    {
                        _candIdx = i;
                        _srcIdx = 0;
                        _status = "已选择 " + (_cands[i].Name != null ? _cands[i].Name.ToString() : s.StringId);
                        Refresh();
                        return;
                    }
                }
                _status = "该聚落不属于本国(只能选本国的城镇/城堡)";
                Refresh();
            }
            catch { }
        }

        internal void CycleSource(int dir)
        {
            try
            {
                if (_srcList.Count == 0) { _status = "该地点没有可选人口来源"; Refresh(); return; }
                _planEdit = false;
                _srcIdx = (_srcIdx + dir) % _srcList.Count;
                if (_srcIdx < 0) _srcIdx += _srcList.Count;
                Refresh();
            }
            catch { }
        }

        internal void RecruitPlan()
        {
            try
            {
                _planEdit = false;
                string msg = DefArmy.Recruit(Source, _plan);
                // v4.85: 募兵成功首次弹"讲解"
                if (msg != null && msg.StartsWith("募兵") && !TipState.Disabled("recruit"))
                {
                    _status = msg;
                    ShowRecruitTip(msg);
                    Refresh();
                    return;
                }
                Notify(msg);
            }
            catch (Exception ex) { DLog.Force("军务页募兵异常: " + ex.Message); Notify("募兵失败: " + ex.Message); }
        }

        internal void ConscriptPlan()
        {
            try { _planEdit = false; Notify(DefArmy.Conscript(Source, _plan)); }
            catch (Exception ex) { DLog.Force("军务页征兵异常: " + ex.Message); Notify("征兵失败: " + ex.Message); }
        }

        // v4.85: 军务操作反馈改为弹窗(用户要求); 结果同时写面板底部状态栏
        private void Notify(string msg)
        {
            try
            {
                _status = string.IsNullOrEmpty(msg) ? "操作失败(无返回信息)" : msg;
                ShowInquiry("军务", _status);
            }
            catch { }
            Refresh();
        }

        internal static void ShowInquiry(string title, string body)
        {
            try
            {
                if (string.IsNullOrEmpty(body)) return;
                if (PanelInputGuard.AnyPopupActive()) return;   // 已有弹窗时不叠加
                InformationManager.ShowInquiry(new InquiryData(title, body, true, false,
                    "关闭", null, null, null, "", 0f, null, null, null), true, false);
            }
            catch (Exception ex) { DLog.Force("弹窗失败: " + ex.Message); }
        }

        private void ShowRecruitTip(string result)
        {
            try
            {
                if (PanelInputGuard.AnyPopupActive()) { _status = result; return; }
                string body = result + "\n\n所募兵员编入该地守备营(城镇/城堡驻军), 不会直接出现在世界地图上。\n"
                    + "装备从国家军械库领装(按兵种配装), 库存不足按满足率结算; 领装不扣国库金。\n"
                    + "让部队上地图: 军务页 → [守备营]页 → 点该行[成立军团], 打开建军设计器: 14 兵种逐行选人"
                    + "(逐兵种上限看军械库装备可用数, 至少 100 兵), 费用: 建军 200 + 10/兵。\n"
                    + "成立后地图上出现「国防军第 N 军团」, 选中后左键点地图即可移动, 或用[驻防/巡逻/补员/解散]指挥。";
                InformationManager.ShowInquiry(new InquiryData("军务 · 募兵完成", body,
                    true, true, "不再提示", "关闭",
                    delegate { TipState.Disable("recruit"); }, null, "", 0f, null, null, null), true, false);
            }
            catch (Exception ex) { DLog.Force("募兵讲解弹窗失败: " + ex.Message); }
        }

        internal void FormLegion()
        {
            try
            {
                _planEdit = false;
                AskLegionCount(Current);
            }
            catch (Exception ex) { DLog.Force("军务页建军异常: " + ex.Message); }
        }

        // 成立军团改走建军设计器页(14 兵种编制 + 军械库上限); 旧"只填人数"TextInquiry 路径已移除
        internal void AskLegionCount(Settlement s)
        {
            try
            {
                _planEdit = false;
                if (s == null) { Notify("请先选择驻地"); return; }
                if (PanelInputGuard.AnyPopupActive()) return;
                var target = s;
                if (s.IsVillage && s.Village != null && s.Village.Bound != null) target = s.Village.Bound;
                LegionDesignPanel.Open(target);
            }
            catch (Exception ex) { DLog.Force("打开建军设计器失败: " + ex.Message); Notify("打开建军设计器失败: " + ex.Message); }
        }

        private static int GarrisonAvailable(Settlement s)
        {
            try
            {
                var target = s;
                if (s != null && s.IsVillage && s.Village != null) target = s.Village.Bound;
                if (target == null) return 0;
                return DefArmy.GarrisonMenOf(target.StringId);   // v4.90: 账面口径(不含原版驻军自带兵)
            }
            catch { return 0; }
        }

        internal void LegionAction(int i, int kind)
        {
            try
            {
                if (i < 0 || i >= LegionRows.Count) return;
                var p = DefArmy.FindParty(LegionRows[i].PartyId);
                if (p == null || !p.IsActive) { Notify("该军团已不存在"); return; }
                if (kind >= 4) Notify(DefArmy.DisbandLegion(p));
                else if (kind == 3) Notify(DefArmy.ReplenishLegion(p, _plan));
                else Notify(DefArmy.SetTask(p, kind));
            }
            catch (Exception ex) { DLog.Force("军务页军团操作异常: " + ex.Message); Notify("军团操作失败: " + ex.Message); }
        }

        // v4.235: 原"军团行[铁路 开/关]"已删除(没有铁路也能开关, 用户要求删)
        //   铁路运输仍由内核保留: AI 长途行军自动开、军列运兵投送后开、日耗引擎付不起自动停

        // v4.243: 军团状态按钮(循环切换 戒备/操练/休整/行军/征粮)
        internal void LegionStance(int i)
        {
            try
            {
                if (i < 0 || i >= LegionRows.Count) return;
                var p = DefArmy.FindParty(LegionRows[i].PartyId);
                if (p == null || !p.IsActive) { Notify("该军团已不存在"); return; }
                Notify(DefArmy.CycleStance(p));
                RebuildLegionRows();
                Refresh();
            }
            catch (Exception ex) { DLog.Force("军团状态切换异常: " + ex.Message); Notify("状态切换失败"); }
        }

        // 军团行悬停: 状态/训练度/将军特质的完整说明
        internal void ShowLegionTip(int i)
        {
            try { Notify(LegionTipOf(i)); }
            catch { }
        }

        internal string LegionTipOf(int i)
        {
            try
            {
                if (i < 0 || i >= LegionRows.Count) return "";
                var lg = DefArmy.LegionOf(DefArmy.FindParty(LegionRows[i].PartyId));
                if (lg == null) return "";
                var sb = new System.Text.StringBuilder();
                sb.Append("状态「").Append(DefArmy.StanceNames[DefArmy.StanceOf(lg)]).Append("」: ")
                  .Append(DefArmy.StanceHelp(DefArmy.StanceOf(lg)));
                sb.Append("\n训练度 ").Append((int)Math.Round(DefArmy.TrainLevelOf(lg)))
                  .Append("/100 -> 战斗系数 ×").Append(DefArmy.TrainMultOf(lg).ToString("F2"));
                sb.Append("\n将军特质: ");
                int mask = lg.GeneralTraits;
                if (mask == 0) sb.Append("无");
                else
                {
                    for (int k = 0; k < DefArmy.TraitBits.Length; k++)
                    {
                        if ((mask & DefArmy.TraitBits[k]) == 0) continue;
                        sb.Append("\n  ").Append(DefArmy.TraitNames[k]).Append(": ").Append(DefArmy.TraitHelp(DefArmy.TraitBits[k]));
                    }
                }
                return sb.ToString();
            }
            catch { return ""; }
        }

        internal void GarrisonAction(int i)
        {
            try
            {
                if (i < 0 || i >= GarrisonRows.Count) return;
                var s = DefArmy.FindSettlement(GarrisonRows[i].SettlementId);
                AskLegionCount(s);
            }
            catch (Exception ex) { DLog.Force("军务页守备营操作异常: " + ex.Message); Notify("守备营操作失败: " + ex.Message); }
        }

        internal void AllToHome()
        {
            try { Notify(DefArmy.AllToHome()); }
            catch (Exception ex) { DLog.Force("全军回防异常: " + ex.Message); Notify("全军回防失败: " + ex.Message); }
        }

        // v4.245: 交还军事总监(恢复了 AI 自动调度, 军团会自己休整/集结/进攻)
        internal void ReleaseAllToAi()
        {
            try { Notify(DefArmy.ReleaseAllToAi()); RebuildLegionRows(); Refresh(); }
            catch (Exception ex) { DLog.Force("全军交还异常: " + ex.Message); Notify("交还失败: " + ex.Message); }
        }

        // v4.239: 强制征兵无时效 -> [续征 +7 天] 按钮与入口删除(要减员用[解甲归田])

        internal void ReleaseConscripts()
        {
            try { Notify(DefArmy.ReleaseConscripts()); }
            catch (Exception ex) { DLog.Force("解甲归田异常: " + ex.Message); Notify("解甲归田失败: " + ex.Message); }
        }

        internal void TransferConscripts()
        {
            try { Notify(DefArmy.TransferConscripts()); }
            catch (Exception ex) { DLog.Force("转常备异常: " + ex.Message); Notify("转常备失败: " + ex.Message); }
        }

        internal void Parade()
        {
            try { Notify(DefArmy.Parade()); }
            catch (Exception ex) { DLog.Force("阅兵异常: " + ex.Message); Notify("阅兵失败: " + ex.Message); }
        }

        internal void ScrollStep(int dir)
        {
            try
            {
                if (_tab == 1)
                {
                    int max = Math.Max(0, _legionView.Count - LegionPage);
                    _scroll += dir;
                    if (_scroll < 0) _scroll = 0;
                    if (_scroll > max) _scroll = max;
                    RebuildLegionRows();
                    return;
                }
                if (_tab == 2)
                {
                    int max = Math.Max(0, DefArmy.Garrisons.Count - GarrisonPage);
                    int next = _gScroll + dir;
                    if (next < 0) next = 0;
                    if (next > max) next = max;
                    if (next == _gScroll) return;
                    _gScroll = next;
                    RebuildGarrisonRows();
                }
            }
            catch { }
        }

        // ---- 刷新 ----
        internal void Refresh()
        {
            try
            {
                RefreshCandidates();
                int lmen = 0;
                for (int i = 0; i < DefArmy.Legions.Count; i++) lmen += DefArmy.LegionMen(DefArmy.Legions[i]);
                int gmen = 0;
                for (int i = 0; i < DefArmy.Garrisons.Count; i++) gmen += DefArmy.GarrisonMen(DefArmy.Garrisons[i]);
                int total = lmen + gmen;
                _overview = "现役 " + MilFmt.N(total) + " 人 ｜ 军团 " + DefArmy.Legions.Count + " 支 ｜ 守备营 " + DefArmy.Garrisons.Count + " 处";
                _menText = MilFmt.N(total);
                _legionText = DefArmy.Legions.Count + " 支 · " + MilFmt.N(lmen);
                _garrisonText = DefArmy.Garrisons.Count + " 处 · " + MilFmt.N(gmen);
                _money = "军饷 " + MilFmt.N(DefArmy.DailyWageCost()) + "/日 · 可撑 " + DefArmy.DaysAffordable()
                    + " 天 · 欠饷 " + DefArmy.UnpaidDays + " 天 · 今日军损 " + DefArmy.TodayMilLoss + " 人";
                _quality = BuildQualityText();

                var s = Current;
                var src = Source;
                _srcName = src != null ? (src.Name != null ? src.Name.ToString() : src.StringId) : "—";

                int havePop = 0;
                _settleName = s != null ? (s.Name != null ? s.Name.ToString() : s.StringId) : "—";
                if (src != null)
                {
                    havePop = DefArmy.CommonersOf(src);
                    _settleInfo = (s != null && s.IsTown ? "城镇" : "城堡") + " · 守备营 " + MilFmt.N(GarrisonAvailable(s)) + " 兵"
                        + " · 人口来源: " + (src.IsVillage ? "农民" : "劳工");
                }
                else
                {
                    _settleInfo = "本国暂无城镇/城堡";
                }

                // 计划数量 + 实时"需要 / 拥有"
                int n2 = _plan;
                int needEquipTotal, haveEquipTotal;
                RecruitEquip(n2, src, out needEquipTotal, out haveEquipTotal);
                int equipFill = needEquipTotal > 0 ? (int)Math.Round(100.0 * haveEquipTotal / needEquipTotal) : 100;
                if (equipFill > 100) equipFill = 100;
                int needGold = NeedGoldFor(n2);
                int needGoldConscript = n2 * 5;
                _planText = _planEdit ? (_plan.ToString("N0", CultureInfo.InvariantCulture) + "_") : MilFmt.N(_plan);
                // 拆两行(定宽列防出框): 资金/人口 + 军械库领装
                _needRecruitText = "金 " + MilFmt.N(needGold) + " · 人 " + MilFmt.N(n2);
                _needEquipText = "· 满足率 " + equipFill + "%";
                _needConscriptText = "国库 " + MilFmt.N(needGoldConscript) + " · 人口 " + MilFmt.N(n2);
                int haveGold = MilTreasury.Gold();
                _treasuryText = "国库：" + MilFmt.N(haveGold) + " 金";
                _treasuryColor = haveGold >= 0 ? "#E8C33AFF" : "#D96A5AFF";
                _haveGoldPop = "国库：" + MilFmt.N(haveGold) + " 金 · 源人口 " + MilFmt.N(havePop) + " 人";
                _haveEquip = "满足率 " + equipFill + "% (国家军械库)";

                if (havePop < n2) { _planVerdict = "⚠ 人口不足 " + MilFmt.N(n2 - havePop) + " 人"; _planVerdictColor = "#FF4B4BFF"; _planVerdictBg = "#C96A5A26"; }
                else if (haveGold < needGold) { _planVerdict = "⚠ 国库不足 " + MilFmt.N(needGold - haveGold); _planVerdictColor = "#FF4B4BFF"; _planVerdictBg = "#C96A5A26"; }
                else if (equipFill < 100) { _planVerdict = "军械库缺装 · 按满足率 " + equipFill + "% 领装"; _planVerdictColor = "#E8C33AFF"; _planVerdictBg = "#E8C33A26"; }
                else if (Politics.ConscriptGrievance >= 80f) { _planVerdict = "⚠ 民怨沸腾 " + Politics.ConscriptGrievance.ToString("0.#") + "/100 · 再强制征兵会引来请愿与征丁逃亡"; _planVerdictColor = "#FFB84BFF"; _planVerdictBg = "#E8C33A26"; }
                else { _planVerdict = "✓ 国库/人口/军械库充足, 可执行"; _planVerdictColor = "#7FBF6AFF"; _planVerdictBg = "#7FBF6A26"; }

                _recruitBtn = "募兵 " + MilFmt.N(n2) + " 人";
                _recruitCost = "20金/兵 · 花费 " + MilFmt.N(needGold) + " 金 · 国库 " + MilFmt.N(haveGold);
                _conscriptBtn = "强制征兵 " + MilFmt.N(n2) + " 人";
                int gAdd = 0;
                string gAddS = "0";
                try
                {
                    float totalPop = Math.Max(1f, Pops.TotalPopulation());
                    float add = Math.Min(100f, n2 / totalPop * 4000f);   // 与内核同系数(1% 人口 = 40 民怨)
                    gAddS = add.ToString("0.#");
                    gAdd = (int)Math.Round(add);
                }
                catch { }
                _conscriptCost = "5金/兵 · 花费 " + MilFmt.N(needGoldConscript) + " 金 · 国库 " + MilFmt.N(haveGold)
                    + " · 民怨 +" + gAddS;
                _recruitCostColor = haveGold >= needGold ? "#7FBF6AFF" : "#C96A5AFF";
                _conscriptCostColor = haveGold >= needGoldConscript ? "#7FBF6AFF" : "#C96A5AFF";

                int lv = Politics.LawLevel(2);
                int pool = DefArmy.ConscriptPool(src);
                float gr = Politics.ConscriptGrievance;
                _conscriptText = "强制征兵(无期限) · " + (lv > 0 ? LawSystem.TierName(LawSystem.LLevy, lv) : "未立")
                    + " · 池 " + MilFmt.N(pool) + " · 已征 " + MilFmt.N(DefArmy.ConscriptedMen())
                    + "/" + MilFmt.N(DefArmy.ConscriptCap())
                    + " · 民怨 " + gr.ToString("0.#") + "/100"
                    + (gr >= 70f ? " (征丁会逃亡)" : (gr >= 50f ? " (乡民在请愿)" : ""));

                int garrAvail = GarrisonAvailable(s);
                _formLegionBtn = "成立军团 · 打开设计器(守备营 " + MilFmt.N(Math.Max(0, garrAvail)) + " 兵)";

                _legionHelp = "野战军团=可移动的国防军。行内指挥: 驻防(回驻地) · 巡逻 · 补员(从驻地守备营补兵) · 解散";
                _garrisonHelp = "守备营=驻扎在城镇驻军里的国防军。点[成立军团]打开建军设计器: 14 兵种逐行选人, "
                    + "逐兵种上限=min(军械库装备可用, 守备营, 编制, 资金可支撑), 成立费 200 + 10/兵(军械库领装不扣金)";
                // v4.240: 本页说明(用户反馈看不懂各按钮)
                _recruitHelp = "募兵 = 20 金/兵招志愿兵(装备从国家军械库领, 不扣金)\n"
                    + "强制征兵 = 按法令 5 金/兵抽平民, 无期限, 会涨民怨\n"
                    + "放归征丁 = 送回乡下(降民怨) · 转常备 = 20 金/兵把全部征丁转正(不占名额)\n"
                    + "民怨 ≥50 乡民递[反征兵请愿], ≥70 每月征丁逃亡; 停征每月自然平息 4 点";

                // 页签显示与高亮
                RefreshUnits();   // v4.193: 兵种构成(全军)
                _showRecruit = _tab == 0;
                _showLegion = _tab == 1;
                _showGarrison = _tab == 2;
                _tabRecruitColor = _tab == 0 ? "#E8C33AFF" : "#8A8070FF";
                _tabLegionColor = _tab == 1 ? "#E8C33AFF" : "#8A8070FF";
                _tabGarrisonColor = _tab == 2 ? "#E8C33AFF" : "#8A8070FF";
                _tabLineRecruit = _tab == 0;
                _tabLineLegion = _tab == 1;
                _tabLineGarrison = _tab == 2;
                // 执行按钮状态色: 资源不足时变红
                bool canRecruit = havePop >= n2 && haveGold >= needGold;
                bool canConscript = havePop >= n2 && haveGold >= needGoldConscript;
                _recruitBtnColor = canRecruit ? "#7FBF6AFF" : "#C96A5AFF";
                _conscriptBtnColor = canConscript ? "#7FBF6AFF" : "#C96A5AFF";

                RebuildLegionRows();
                RebuildGarrisonRows();
                _legionEmpty = LegionRows.Count == 0;
                _garrisonEmpty = GarrisonRows.Count == 0;

                OnPropertyChangedWithValue(_overview, "OverviewText");
                OnPropertyChangedWithValue(_money, "MoneyText");
                OnPropertyChangedWithValue(_quality, "QualityText");
                OnPropertyChangedWithValue(_settleName, "SettleName");
                OnPropertyChangedWithValue(_srcName, "SrcName");
                OnPropertyChangedWithValue(_settleInfo, "SettleInfo");
                OnPropertyChangedWithValue(_planText, "PlanText");
                OnPropertyChangedWithValue(_needRecruitText, "NeedRecruitText");
                OnPropertyChangedWithValue(_needEquipText, "NeedEquipText");
                OnPropertyChangedWithValue(_needConscriptText, "NeedConscriptText");
                OnPropertyChangedWithValue(_haveGoldPop, "HaveGoldPop");
                OnPropertyChangedWithValue(_treasuryText, "TreasuryText");
                OnPropertyChangedWithValue(_treasuryColor, "TreasuryColor");
                OnPropertyChangedWithValue(_recruitCostColor, "RecruitCostColor");
                OnPropertyChangedWithValue(_conscriptCostColor, "ConscriptCostColor");
                OnPropertyChangedWithValue(_haveEquip, "HaveEquip");
                OnPropertyChangedWithValue(_planVerdict, "PlanVerdictText");
                OnPropertyChangedWithValue(_planVerdictColor, "PlanVerdictColor");
                OnPropertyChangedWithValue(_planVerdictBg, "PlanVerdictBg");
                OnPropertyChangedWithValue(_menText, "MenText");
                OnPropertyChangedWithValue(_legionText, "LegionText");
                OnPropertyChangedWithValue(_garrisonText, "GarrisonText");
                OnPropertyChangedWithValue(_conscriptText, "ConscriptText");
                OnPropertyChangedWithValue(_recruitBtn, "RecruitBtnText");
                OnPropertyChangedWithValue(_recruitCost, "RecruitBtnCost");
                OnPropertyChangedWithValue(_conscriptBtn, "ConscriptBtnText");
                OnPropertyChangedWithValue(_conscriptCost, "ConscriptBtnCost");
                OnPropertyChangedWithValue(_formLegionBtn, "FormLegionBtnText");
                OnPropertyChangedWithValue(_status, "StatusText");
                OnPropertyChangedWithValue(_legionHelp, "LegionHelp");
                OnPropertyChangedWithValue(_garrisonHelp, "GarrisonHelp");
                OnPropertyChangedWithValue(_recruitHelp, "RecruitHelp");
                OnPropertyChangedWithValue(_showRecruit, "ShowRecruit");
                OnPropertyChangedWithValue(_showLegion, "ShowLegion");
                OnPropertyChangedWithValue(_showGarrison, "ShowGarrison");
                OnPropertyChangedWithValue(_tabRecruitColor, "TabRecruitColor");
                OnPropertyChangedWithValue(_tabLegionColor, "TabLegionColor");
                OnPropertyChangedWithValue(_tabGarrisonColor, "TabGarrisonColor");
                OnPropertyChangedWithValue(_tabLineRecruit, "TabLineRecruit");
                OnPropertyChangedWithValue(_tabLineLegion, "TabLineLegion");
                OnPropertyChangedWithValue(_tabLineGarrison, "TabLineGarrison");
                OnPropertyChangedWithValue(_recruitBtnColor, "RecruitBtnColor");
                OnPropertyChangedWithValue(_conscriptBtnColor, "ConscriptBtnColor");
                OnPropertyChangedWithValue(_legionEmpty, "LegionEmptyVisible");
                OnPropertyChangedWithValue(_garrisonEmpty, "GarrisonEmptyVisible");
            }
            catch (Exception ex) { DLog.Force("军务页刷新异常: " + ex.Message); }
        }

        // 概览质量行: 组织度/士气/装备满足率/平均熟练(全部内核口径)
        private string BuildQualityText()
        {
            try
            {
                int cnt = 0, orgSum = 0, morSum = 0, fillSum = 0;
                long profSum = 0; int profCnt = 0;
                for (int i = 0; i < DefArmy.Legions.Count; i++)
                {
                    var lg = DefArmy.Legions[i];
                    var p = DefArmy.LegionParty(lg);
                    if (lg == null) continue;
                    string key = ArmyDoctrine.LegionKey(lg);
                    orgSum += (int)Math.Round(ArmyDoctrine.OrgOf2(key));
                    morSum += (int)Math.Round(ArmyDoctrine.MoraleOf2(key));
                    fillSum += (int)Math.Round(DefArmy.EquipFillOf(lg) * 100f);
                    cnt++;
                    if (p != null)
                    {
                        try
                        {
                            Dictionary<string, int> men; Dictionary<string, float> profs;
                            Soldiers.Aggregate(p, out men, out profs);
                            foreach (var kv in men)
                            {
                                float pf;
                                if (!profs.TryGetValue(kv.Key, out pf)) pf = 0f;
                                profSum += (long)(pf * kv.Value);
                                profCnt += kv.Value;
                            }
                        }
                        catch { }
                    }
                }
                int org = cnt > 0 ? orgSum / cnt : 0;
                int mor = cnt > 0 ? morSum / cnt : 0;
                int fill = cnt > 0 ? fillSum / cnt : 0;
                int prof = profCnt > 0 ? (int)(profSum / profCnt) : 0;
                // v4.243: 训练度均值 / 老兵占比 / 战力分 / 老兵度
                float trainSum = 0f, vetShareSum = 0f;
                long vetKills = 0;
                int vc = 0;
                for (int i = 0; i < DefArmy.Legions.Count; i++)
                {
                    var lg = DefArmy.Legions[i];
                    if (lg == null) continue;
                    trainSum += DefArmy.TrainLevelOf(lg);
                    var p = DefArmy.LegionParty(lg);
                    if (p != null)
                    {
                        try
                        {
                            float avg;
                            vetShareSum += Soldiers.VeteranShare(p, 70, out avg);
                            vetKills += Soldiers.TotalKills(p);
                            vc++;
                        }
                        catch { }
                    }
                }
                int train = cnt > 0 ? (int)Math.Round(trainSum / cnt) : 0;
                int vetShare = vc > 0 ? (int)Math.Round(vetShareSum / vc * 100f) : 0;
                return "均值: 组织度 " + org + " · 士气 " + mor + " · 训练度 " + train + " · 老兵占比 " + vetShare
                    + "% · 装备 " + fill + "% · 熟练 " + prof + " · 累计击杀 " + MilFmt.N((int)Math.Min(int.MaxValue, vetKills));
            }
            catch { return "均值: —"; }
        }

        // 募兵领装(新体系口径): 逐人 1 武器 + 1 护甲, 型号按兵种需求从国家军械库现货挑
        // needTotal = 计划需要件数; haveTotal = 军械库当前可领(按型号截断到需求)
        private static void RecruitEquip(int n, Settlement src, out int needTotal, out int haveTotal)
        {
            needTotal = 0; haveTotal = 0;
            try
            {
                var need = DefArmy.RecruitEquipNeed(n, src != null ? src.Culture : null);
                string key = Armory.NationalOwner(DefArmy.OurKingdom);
                foreach (var kv in need)
                {
                    needTotal += kv.Value;
                    int have = Armory.CountKey(key, kv.Key);
                    haveTotal += Math.Min(have, kv.Value);
                }
            }
            catch { }
        }

        internal static int NeedGoldFor(int n)
        {
            int lordSeat = Politics.SeatHeld(3) ? 1 : 0;
            return (int)Math.Round(n * 20 * (lordSeat == 1 ? 0.9f : 1f));
        }

        private void RefreshCandidates()
        {
            try
            {
                string keep = Current != null ? Current.StringId : null;
                _cands.Clear();
                _cands.AddRange(DefArmy.CandidateSettlements());
                _candIdx = 0;
                if (keep != null)
                {
                    for (int i = 0; i < _cands.Count; i++)
                        if (_cands[i].StringId == keep) { _candIdx = i; break; }
                }
                string keepSrc = Source != null ? Source.StringId : null;
                _srcList.Clear();
                _srcList.AddRange(DefArmy.SourcesOf(Current));
                _srcIdx = 0;
                if (keepSrc != null)
                {
                    for (int i = 0; i < _srcList.Count; i++)
                        if (_srcList[i].StringId == keepSrc) { _srcIdx = i; break; }
                }
                // 诊断: 可选聚落数量变化时记录(排查"只有一座城")
                if (_cands.Count != _lastCandCount)
                {
                    _lastCandCount = _cands.Count;
                    var names = new System.Text.StringBuilder();
                    for (int i = 0; i < _cands.Count && i < 12; i++) names.Append(_cands[i].Name).Append(' ');
                    DLog.Force("军务: 可选聚落=" + _cands.Count + " [" + names.ToString().TrimEnd() + "] 王国="
                        + (DefArmy.OurKingdom != null ? DefArmy.OurKingdom.Name.ToString() : "?"));
                }
            }
            catch { }
        }

        private int _lastCandCount = -1;



        // v4.193: 全军兵种构成(内核 14 兵种图标; 熟练度取 Soldiers.Aggregate)
        private void RefreshUnits()
        {
            try
            {
                Units.Clear();
                int inf = 0, arch = 0, cav = 0, hcav = 0, elite = 0, total = 0;
                var catSum = new long[5];
                var catCnt = new int[5];
                long profSum = 0; int profCnt = 0;
                for (int i = 0; i < DefArmy.Legions.Count; i++)
                {
                    var p = DefArmy.LegionParty(DefArmy.Legions[i]);
                    if (p == null) continue;
                    // v4.236: 构成按"我们的 14 兵种逐人表"算, 不再读原版兵种的近战/远程旗标
                    //   (原来 100 线列步兵会被原版花名册拆成 65 近战 + 35 远程)
                    try
                    {
                        Dictionary<string, int> men; Dictionary<string, float> profs;
                        Soldiers.Aggregate(p, out men, out profs);
                        foreach (var kv in men)
                        {
                            int n = kv.Value;
                            if (n <= 0) continue;
                            total += n;
                            int idx = Soldiers.UnitIndexOf(kv.Key);
                            int b = Equipment.BranchOf(idx);
                            if (b == 0) inf += n;
                            else if (b == 1) arch += n;
                            else if (b == 2) cav += n;
                            else hcav += n;
                            var u = Equipment.Unit(idx);
                            if (u != null && u.Training >= 1.10f) elite += n;
                            float pf;
                            if (!profs.TryGetValue(kv.Key, out pf)) pf = 0f;
                            profSum += (long)(pf * n);
                            profCnt += n;
                            int cat = MilIcons.CategoryOfUnitId(kv.Key);
                            if (cat < 5) { catSum[cat] += (long)(pf * n); catCnt[cat] += n; }
                        }
                    }
                    catch { }
                }
                if (total <= 0) { _unitsTitle = "兵种构成(暂无野战军团)"; OnPropertyChangedWithValue(_unitsTitle, "UnitsTitle"); return; }
                _unitsTitle = "兵种构成(全军 " + MilFmt.N(total) + " 人)";
                OnPropertyChangedWithValue(_unitsTitle, "UnitsTitle");
                AddUnit(MilIcons.ByBranch(0), "近战", inf, total, "#D8C9A0FF", ProfText(catSum[0], catCnt[0]));
                AddUnit(MilIcons.ByBranch(1), "远程", arch, total, "#8FD8E8FF", ProfText(catSum[1], catCnt[1]));
                AddUnit(MilIcons.ByBranch(2), "骑兵", cav, total, "#E8C33AFF", ProfText(catSum[2], catCnt[2]));
                AddUnit(MilIcons.ByBranch(3), "骑射", hcav, total, "#9FB08AFF", ProfText(catSum[3], catCnt[3]));
                AddUnit("fia_unit_grenadier", "精锐", elite, total, "#E8A33AFF",
                    profCnt > 0 ? ("熟练 " + (int)(profSum / profCnt)) : "熟练 —");
            }
            catch (Exception ex) { DLog.Force("兵种构成刷新失败: " + ex.Message); }
        }

        private static string ProfText(long sum, int cnt)
        {
            if (cnt <= 0) return "熟练 —";
            return "熟练 " + (int)(sum / cnt);
        }

        private void AddUnit(string icon, string name, int count, int total, string color, string prof)
        {
            if (count <= 0) return;
            Units.Add(new UnitRowVM(icon, name, count, total > 0 ? count / (float)total : 0f, color, prof));
        }

        private void RebuildLegionRows()
        {
            try
            {
                LegionRows.Clear();
                _legionView.Clear();
                for (int i = 0; i < DefArmy.Legions.Count; i++) _legionView.Add(DefArmy.Legions[i]);
                int max = Math.Max(0, _legionView.Count - LegionPage);
                if (_scroll > max) _scroll = max;
                for (int i = _scroll; i < _legionView.Count && LegionRows.Count < LegionPage; i++)
                {
                    var lg = _legionView[i];
                    LegionRows.Add(new LegionRowVM(lg, DefArmy.LegionParty(lg)));
                }
            }
            catch { }
        }

        private void RebuildGarrisonRows()
        {
            try
            {
                DefArmy.NormalizeGarrisons();   // v4.86: 同一城市只显示一行守备营(重复募兵合并)
                GarrisonRows.Clear();
                int max = Math.Max(0, DefArmy.Garrisons.Count - GarrisonPage);
                if (_gScroll > max) _gScroll = max;
                if (_gScroll < 0) _gScroll = 0;
                for (int i = _gScroll; i < DefArmy.Garrisons.Count && GarrisonRows.Count < GarrisonPage; i++)
                    GarrisonRows.Add(new GarrisonRowVM(DefArmy.Garrisons[i]));
            }
            catch { }
        }
    }
}
