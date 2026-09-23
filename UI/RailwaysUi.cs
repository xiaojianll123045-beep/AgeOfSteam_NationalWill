using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;

namespace FeudalInternalAffairs
{
    // v4.162: 铁路管理 UI(第 26 章): 线路列表 / 新建 / 拆除
    internal static class RailwaysUi
    {
        // v4.165: 地图点选建线状态
        private static bool _picking;
        private static string _pickFromId;
        private static bool _pickReplaceNext;   // v4.2xx: 取消一次目的地确认后, 下一次点击 = 改起点

        internal static void Open()
        {
            try
            {
                var opts = new List<InquiryElement>();
                var mine = PlayerSettlements();
                for (int i = 0; i < Railways.All.Count; i++)
                {
                    var l = Railways.All[i];
                    if (l == null) continue;
                    string state = !l.Built ? ("在建 " + (int)(l.Progress * 100f) + "%") : (l.Broken ? "中断" : "运营中");
                    string netShort;
                    string netDetail = NetTextOf(l, out netShort);
                    string hint = state + " · 长度 " + (int)l.Length + " · 归属 " + KingdomName(l.OwnerId)
                        + (netDetail.Length > 0 ? "\n" + netDetail : "");
                    opts.Add(new InquiryElement("line:" + i,
                        Railways.NameOf(l.FromId) + " ↔ " + Railways.NameOf(l.ToId) + " · " + LineSummaryOf(l)
                        + " · 日净利 " + netShort, null, true, hint));
                }
                opts.Add(new InquiryElement("cargo", "货物运输...", null, true,
                    "选择出发城市装车, 沿铁路直达目的地"));
                opts.Add(new InquiryElement("military", "军列运兵...", null, true,
                    "从铁路城市整建制投送部队: 选出发城 → 选部队 → 选目的地"));
                opts.Add(new InquiryElement("upall", "批量升级全部线路", null, true,
                    "逐条升级所有未满级线路(由线路归属国国库支付); 一条消息汇总成功/失败条数与总花费"));
                opts.Add(new InquiryElement("ownall", "全部国有化", null, true,
                    "把所有线路收归国有(线路收入 100% 入国库); 一条消息汇总新收归条数"));
                opts.Add(new InquiryElement("new", "新建铁路...", null, true,
                    "名额 " + Railways.CountOf(PlayerKingdom()) + "/" + Railways.MaxLines(PlayerKingdom())
                    + " · 需「铁路」科技 · 花费 距离×2 金"));
                opts.Add(new InquiryElement("infra", "基建概况...", null, true,
                    "市场准入 = min(100%, 基建/用量); 铁路建筑 +20/级, 线路按等级 +20/25/30/40(1 级线 = 基建 20/运输 20)"));
                opts.Add(new InquiryElement("maprail", "地图模式：铁路", null, true,
                    "把地图着色切换为铁路模式; 该模式下点击已有铁路的城镇可直接装车/运输"));
                opts.Add(new InquiryElement("pick", IsPickingEndpoint ? "取消地图选线" : "在地图上选起终点", null, true,
                    "关闭本窗口, 在地图上依次点击起点与终点城镇/城堡(仅本国)"));
                opts.Add(new InquiryElement("close", "关闭", null, true, null));

                string body = "铁路名额 " + Railways.CountOf(PlayerKingdom()) + "/" + Railways.MaxLines(PlayerKingdom())
                    + " · 已有 " + Railways.All.Count + " 条 · 运输中 " + Railways.Transports.Count + " 支\n"
                    + "新建: 选择起点城市 → 终点城市(仅城镇/城堡); 建成后白线与蓝点出现\n"
                    + "点击线路: 升级列车 / 国有化或私有化 / 拆除\n"
                    + "货物运输 = 常规铁路运输; 军列运兵 = 独立投送部队(按运力与集结天数)";
                PanelInputGuard.ShowPopup(new MultiSelectionInquiryData(
                    "铁路管理", body, opts, true, 1, 1, "确定", "关闭",
                    delegate (List<InquiryElement> sel)
                    {
                        try
                        {
                            if (sel == null || sel.Count == 0) return;
                            string id = sel[0].Identifier as string;
                            if (id == "close") return;
                            if (id == "cargo") { CargoCityPicker(); return; }
                            if (id == "military") { MilitaryCityPicker(); return; }
                            if (id == "upall") { BatchUpgradeAll(); return; }
                            if (id == "ownall") { BatchNationalizeAll(); return; }
                            if (id == "new") { NewLineMenu(); return; }
                            if (id == "infra") { InfraMenu(); return; }
                            if (id == "maprail") { EnterRailMode(); MapSelection.Message("地图模式：铁路（点击已有铁路的城镇可装车/运输）"); return; }
                            if (id == "pick") { StartPickEndpoint(); return; }
                            if (id != null && id.StartsWith("line:"))
                            {
                                int idx = 0;
                                int.TryParse(id.Substring(5), out idx);
                                if (idx >= 0 && idx < Railways.All.Count)
                                {
                                    var l = Railways.All[idx];
                                    LineMenu(l);
                                }
                            }
                        }
                        catch { }
                    }, null, null, false));
            }
            catch (Exception ex) { DLog.Force("铁路管理 UI 失败: " + ex.Message); }
        }

        // 单条线路操作: 升级列车 / 国有化切换 / 拆除
        private static void LineMenu(RailLine l)
        {
            try
            {
                if (l == null) return;
                int tier = 0;
                int upGold = 0;
                try
                {
                    tier = (int)Railways.TierOf(l);
                    upGold = Railways.UpgradeGoldCost(l);   // 满级返回 0, 据此置灰升级项
                }
                catch { }
                bool maxed = upGold <= 0;
                int target = tier + 1;
                try { target = Railways.ClampTier(tier + 1); } catch { }
                int engGold = 0;
                try { engGold = (int)Math.Round(Railways.UpgradeEngineCount(target) * Railways.EngineUnitPrice()); } catch { }
                if (engGold > upGold) engGold = upGold;
                string upTitle = maxed
                    ? ("升级列车（已达最高等级 " + Railways.MaxTier + " 级）")
                    : ("升级列车（花费 " + upGold + " 金）");
                string upHint = maxed ? null
                    : ("基建 +" + Railways.TierInfra(tier) + "→+" + Railways.TierInfra(target)
                        + " · 运输 +" + Railways.TierTransport(tier) + "→+" + Railways.TierTransport(target)
                        + " · 花费 " + upGold + " 金（含引擎折算 " + engGold + " 金）");
                string refund = "";
                try { refund = "" + Railways.RemovalRefund(l); } catch { }
                bool stateOwned = false;
                try { stateOwned = Railways.IsStateOwned(l); } catch { }
                var opts = new List<InquiryElement>
                {
                    new InquiryElement("up", upTitle, null, !maxed, upHint),
                    new InquiryElement("own", stateOwned ? "私有化" : "国有化", null, true,
                        stateOwned ? "转为私人运营(自负盈亏)" : "转为国家运营(国库统一调度)"),
                    new InquiryElement("del", "拆除该铁路（返还 " + refund + " 金）", null, true,
                        "拆除后不可恢复; 返还 " + refund + " 金"),
                    new InquiryElement("back", "返回", null, true, null)
                };
                string state = !l.Built ? ("在建 " + (int)(l.Progress * 100f) + "%") : (l.Broken ? "中断" : "运营中");
                string netShort;
                string netDetail = NetTextOf(l, out netShort);
                string maint = "";
                try { maint = "" + Railways.MaintenanceOf(l); } catch { }
                PanelInputGuard.ShowPopup(new MultiSelectionInquiryData(
                    Railways.NameOf(l.FromId) + " ↔ " + Railways.NameOf(l.ToId),
                    LineSummaryOf(l) + "\n状态: " + state + " · 长度 " + (int)l.Length + " · 归属 " + KingdomName(l.OwnerId)
                    + (netDetail.Length > 0 ? "\n" + netDetail : "")
                    + (maint.Length > 0 ? " · 维护 " + maint + " 金/日" : ""),
                    opts, true, 1, 1, "确定", "取消",
                    delegate (List<InquiryElement> sel)
                    {
                        try
                        {
                            if (sel == null || sel.Count == 0) { Open(); return; }
                            string id = sel[0].Identifier as string;
                            if (id == "up")
                            {
                                string msg = "";
                                try { msg = Railways.Upgrade(l); } catch { }
                                MapSelection.Message(string.IsNullOrEmpty(msg) ? "列车已升级" : msg);
                                Open();
                            }
                            else if (id == "own")
                            {
                                string msg = "";
                                try { msg = Railways.SetOwnership(l, !stateOwned); } catch { }
                                MapSelection.Message(string.IsNullOrEmpty(msg)
                                    ? (stateOwned ? "已私有化" : "已国有化") : msg);
                                Open();
                            }
                            else if (id == "del") { RemoveConfirm(l); }
                            else Open();
                        }
                        catch { }
                    }, null, null, false));
            }
            catch (Exception ex) { DLog.Force("铁路线路菜单失败: " + ex.Message); }
        }

        // v4.2xx: 批量升级: 逐条 UpgradeLine, 成功/失败与总花费汇总为一条消息
        private static void BatchUpgradeAll()
        {
            try
            {
                if (Railways.All.Count == 0) { MapSelection.Message("当前没有铁路"); return; }
                int ok = 0, fail = 0, cost = 0;
                var reasons = new Dictionary<string, int>();
                for (int i = 0; i < Railways.All.Count; i++)
                {
                    var l = Railways.All[i];
                    if (l == null) continue;
                    int gold = 0;
                    try { gold = Railways.UpgradeGoldCost(l); } catch { }
                    string err = "";
                    try { err = Railways.UpgradeLine(l); } catch { }
                    if (string.IsNullOrEmpty(err)) { ok++; cost += gold; }
                    else { fail++; AddFailReason(reasons, err); }
                }
                string msg = "批量升级: 成功 " + ok + " 条 · 总花费 " + cost + " 金";
                if (fail > 0) msg += " · 失败 " + fail + " 条（" + FailReasonText(reasons) + "）";
                MapSelection.Message(msg);
            }
            catch (Exception ex) { DLog.Force("批量升级失败: " + ex.Message); }
        }

        // v4.2xx: 批量国有化: 逐条 SetOwnership(l,true), 汇总新收归/原有条数
        private static void BatchNationalizeAll()
        {
            try
            {
                if (Railways.All.Count == 0) { MapSelection.Message("当前没有铁路"); return; }
                int total = 0, changed = 0;
                for (int i = 0; i < Railways.All.Count; i++)
                {
                    var l = Railways.All[i];
                    if (l == null) continue;
                    bool was = true;
                    try { was = Railways.IsStateOwned(l); } catch { }
                    try { Railways.SetOwnership(l, true); } catch { }
                    total++;
                    if (!was) changed++;
                }
                MapSelection.Message("全部国有化: 共 " + total + " 条 · 新收归国有 " + changed
                    + " 条 · 原已国有 " + (total - changed) + " 条");
            }
            catch (Exception ex) { DLog.Force("批量国有化失败: " + ex.Message); }
        }

        // 失败原因归并(括号内的数字差异不算不同原因, 如"国库不足(需 X 金)")
        private static void AddFailReason(Dictionary<string, int> reasons, string err)
        {
            try
            {
                if (string.IsNullOrEmpty(err)) err = "未知原因";
                int cut = err.IndexOf('(');
                int cut2 = err.IndexOf('（');
                if (cut < 0 || (cut2 >= 0 && cut2 < cut)) cut = cut2;
                if (cut > 0) err = err.Substring(0, cut).Trim();
                int n;
                reasons.TryGetValue(err, out n);
                reasons[err] = n + 1;
            }
            catch { }
        }

        private static string FailReasonText(Dictionary<string, int> reasons)
        {
            string s = "";
            try
            {
                foreach (var kv in reasons)
                {
                    if (s.Length > 0) s += ", ";
                    s += kv.Key + "×" + kv.Value;
                }
            }
            catch { }
            return s.Length > 0 ? s : "未知原因";
        }

        private static string LineSummaryOf(RailLine l)
        {
            try
            {
                string s = Railways.LineSummary(l);
                return string.IsNullOrEmpty(s) ? "—" : s;
            }
            catch { return "—"; }
        }

        // 线路日净利: 详细式 "收入 X - 维护 Y = 净利 Z 金/日" + 短式 "+Z"/"-Z"
        private static string NetTextOf(RailLine l, out string shortText)
        {
            shortText = "0";
            try
            {
                int income = 0, upkeep = 0;
                var net = Railways.DailyNetOf(l, out income, out upkeep);
                shortText = net > 0 ? "+" + net : net.ToString();
                return "收入 " + income + " - 维护 " + upkeep + " = 净利 " + shortText + " 金/日";
            }
            catch { return ""; }
        }

        private static void InfraMenu()
        {
            try
            {
                var opts = new List<InquiryElement>();
                var cities = PlayerSettlements();
                for (int i = 0; i < cities.Count; i++)
                {
                    var s = cities[i];
                    opts.Add(new InquiryElement(s.StringId, (s.Name != null ? s.Name.ToString() : s.StringId), null, true,
                        Infrastructure.StatusText(s.StringId)));
                }
                PanelInputGuard.ShowPopup(new MultiSelectionInquiryData(
                    "基建概况",
                    "基建 = 3 + 人口/10万×0.1 + 铁路建筑×20 + 线路按等级 20/25/30/40 + 贸易站×5 + 市政厅×1\n"
                    + "用量 = 建筑等级×分类系数(铁路与市政厅不消耗)\n"
                    + "市场准入 = min(100%, 基建/用量) → 建筑产出乘数(经典)",
                    opts, true, 1, 1, "确定", "关闭", null, null, null, false));
            }
            catch (Exception ex) { DLog.Force("基建概况失败: " + ex.Message); }
        }

        private static void RemoveConfirm(RailLine l)
        {
            try
            {
                if (l == null) return;
                string refund = "";
                try { refund = "" + Railways.RemovalRefund(l); } catch { }
                var opts = new List<InquiryElement>
                {
                    new InquiryElement("del", "拆除该铁路（返还 " + refund + " 金）", null, true,
                        "拆除后不可恢复; 返还 " + refund + " 金"),
                    new InquiryElement("back", "返回", null, true, null)
                };
                PanelInputGuard.ShowPopup(new MultiSelectionInquiryData(
                    Railways.NameOf(l.FromId) + " ↔ " + Railways.NameOf(l.ToId),
                    "状态: " + (!l.Built ? ("在建 " + (int)(l.Progress * 100f) + "%") : (l.Broken ? "中断" : "运营中"))
                    + "\n长度: " + (int)l.Length + "\n归属: " + KingdomName(l.OwnerId)
                    + (refund.Length > 0 ? "\n拆除返还: " + refund + " 金" : ""),
                    opts, true, 1, 1, "确定", "取消",
                    delegate (List<InquiryElement> sel)
                    {
                        try
                        {
                            if (sel != null && sel.Count > 0 && (string)sel[0].Identifier == "del")
                                MapSelection.Message(Railways.Remove(l));
                            else Open();
                        }
                        catch { }
                    }, null, null, false));
            }
            catch { }
        }

        private static void NewLineMenu()
        {
            try
            {
                var list = PlayerSettlements();
                if (list.Count < 2) { MapSelection.Message("本国城镇/城堡不足 2 座"); return; }
                var opts = new List<InquiryElement>();
                for (int i = 0; i < list.Count; i++)
                {
                    var s = list[i];
                    opts.Add(new InquiryElement(s.StringId, s.Name != null ? s.Name.ToString() : s.StringId, null, true,
                        (s.IsTown ? "城镇" : "城堡")));
                }
                PanelInputGuard.ShowPopup(new MultiSelectionInquiryData(
                    "新建铁路 - 起点", "选择起点城市(需与终点不同; 两地之间不能已有铁路)", opts, true, 1, 1, "下一步", "取消",
                    delegate (List<InquiryElement> sel)
                    {
                        try
                        {
                            if (sel == null || sel.Count == 0) return;
                            string fromId = sel[0].Identifier as string;
                            PickEnd(fromId);
                        }
                        catch { }
                    }, null, null, false));
            }
            catch (Exception ex) { DLog.Force("新建铁路 UI 失败: " + ex.Message); }
        }

        private static void PickEnd(string fromId)
        {
            try
            {
                var list = PlayerSettlements();
                var opts = new List<InquiryElement>();
                for (int i = 0; i < list.Count; i++)
                {
                    var s = list[i];
                    if (s.StringId == fromId) continue;
                    string name = s.Name != null ? s.Name.ToString() : s.StringId;
                    if (Railways.HasLine(fromId, s.StringId))
                    {
                        opts.Add(new InquiryElement(s.StringId, "[已有铁路] " + name, null, false, "两地之间已有铁路"));
                        continue;
                    }
                    int cost = 0, days = 0;
                    bool ok = false;
                    string err = "";
                    try { ok = Railways.Estimate(fromId, s.StringId, out cost, out days, out err); } catch { }
                    if (ok)
                    {
                        opts.Add(new InquiryElement(s.StringId,
                            name + " · 距离 " + (cost / 2) + " · 预计花费 " + cost + " 金 · 工期 " + days + " 天",
                            null, true,
                            (s.IsTown ? "城镇" : "城堡") + " · 预计花费 " + cost + " 金 · 工期 " + days + " 天"));
                    }
                    else
                    {
                        string why = string.IsNullOrEmpty(err) ? "暂不可建" : err;
                        opts.Add(new InquiryElement(s.StringId, name + " · " + why, null, false, why));
                    }
                }
                PanelInputGuard.ShowPopup(new MultiSelectionInquiryData(
                    "新建铁路 - 终点", "起点: " + Railways.NameOf(fromId) + "（选项已标注距离/花费/工期）", opts, true, 1, 1, "开建", "取消",
                    delegate (List<InquiryElement> sel)
                    {
                        try
                        {
                            if (sel == null || sel.Count == 0) return;
                            string toId = sel[0].Identifier as string;
                            ConfirmLine(fromId, toId);
                        }
                        catch { }
                    }, null, null, false));
            }
            catch { }
        }

        private static void ConfirmLine(string fromId, string toId)
        {
            try
            {
                int cost, days; string err;
                if (!Railways.Estimate(fromId, toId, out cost, out days, out err))
                {
                    MapSelection.Message(err);
                    return;
                }
                var opts = new List<InquiryElement>
                {
                    new InquiryElement("go", "开建", null, true, "预计花费 " + cost + " 金 · 工期 " + days + " 天"),
                    new InquiryElement("back", "取消", null, true, null)
                };
                PanelInputGuard.ShowPopup(new MultiSelectionInquiryData(
                    "新建铁路 - 确认",
                    Railways.NameOf(fromId) + " ↔ " + Railways.NameOf(toId)
                    + "\n预计花费 " + cost + " 金 · 工期 " + days + " 天",
                    opts, true, 1, 1, "确定", "取消",
                    delegate (List<InquiryElement> sel)
                    {
                        try
                        {
                            if (sel == null || sel.Count == 0 || (string)sel[0].Identifier != "go") return;
                            string e = Railways.Establish(PlayerKingdom(), fromId, toId);
                            MapSelection.Message(e == "" ? "已开工: " + Railways.NameOf(fromId) + " ↔ " + Railways.NameOf(toId) : e);
                            DLog.Force("铁路建线: " + (e == "" ? "成功" : e));
                        }
                        catch { }
                    }, null, null, false));
            }
            catch (Exception ex) { DLog.Force("铁路预估 UI 失败: " + ex.Message); }
        }

        // ================= v4.165: 地图点选建线 =================
        internal static bool IsPickingEndpoint { get { return _picking; } }

        // 进入地图点选: 第一次点=起点, 第二次点=终点; 再点一次[地图选线] = 取消
        internal static void StartPickEndpoint()
        {
            try
            {
                if (_picking) { CancelPick(); return; }
                _picking = true;
                _pickFromId = null;
                _pickReplaceNext = false;
                EnterRailMode();
                MapSelection.Message("铁路模式：点击一个本国城镇/城堡作为起点（再点一次[地图选线]可取消）");
            }
            catch (Exception ex) { DLog.Force("铁路点选失败: " + ex.Message); }
        }

        // 进入铁路地图模式: 仅在从其他模式切进来时发一条图例(避免刷屏)
        private static void EnterRailMode()
        {
            try
            {
                bool wasRail = MapDataMode.Current == MapData.Rail;
                MapDataMode.Set(MapData.Rail);
                if (!wasRail)
                    MapSelection.Message("铁路地图图例: 灰=无铁路 · 浅绿=1 条 · 深绿=2 条以上 · 红色=中断");
            }
            catch (Exception ex) { DLog.Force("切换铁路地图模式失败: " + ex.Message); }
        }

        // 地图点击回调: 第一次=起点, 第二次=终点(同一城取消; 否则预览+确认)
        internal static void PickEndpoint(Settlement s)
        {
            try
            {
                if (!_picking) return;
                if (s == null || (!s.IsTown && !s.IsCastle))
                {
                    MapSelection.Message("这里不是城镇/城堡");
                    return;
                }
                if (!IsPlayerCity(s))
                {
                    MapSelection.Message("只能选本国的城镇/城堡（" + CityName(s) + " 不属于玩家王国）");
                    return;
                }
                if (_pickFromId == null)
                {
                    _pickFromId = s.StringId;
                    MapSelection.Message("已选起点：" + CityName(s) + "，请点击终点城镇/城堡（再点同一城取消；点其他城可改起点）");
                    return;
                }
                if (s.StringId == _pickFromId) { CancelPick(); return; }

                // v4.2xx: 上次目的地确认被取消 -> 本次点击视为"改起点"(点第三座城直接替换, 不用取消重来)
                if (_pickReplaceNext)
                {
                    RailSystem.ClearPreview();
                    _pickFromId = s.StringId;
                    _pickReplaceNext = false;
                    MapSelection.Message("起点已改为 " + CityName(s) + "，请点击终点城镇/城堡");
                    return;
                }

                string fromId = _pickFromId;
                string toId = s.StringId;
                int cost, days; string err;
                if (!Railways.Estimate(fromId, toId, out cost, out days, out err))
                {
                    // v4.2xx: 目的地不可建 -> 视为改起点意图, 直接替换起点(不用取消重来), 并透出具体原因
                    RailSystem.ClearPreview();
                    _pickFromId = s.StringId;
                    _pickReplaceNext = false;
                    MapSelection.Message("起点已改为 " + CityName(s)
                        + (string.IsNullOrEmpty(err) ? "" : "（" + err + "）")
                        + "，请点击终点城镇/城堡");
                    return;
                }
                RailSystem.ShowPreview(fromId, toId);
                var opts = new List<InquiryElement>
                {
                    new InquiryElement("go", "开建", null, true, "预计花费 " + cost + " 金 · 工期 " + days + " 天"),
                    new InquiryElement("back", "取消", null, true, null)
                };
                PanelInputGuard.ShowPopup(new MultiSelectionInquiryData(
                    "修建铁路",
                    "从 " + Railways.NameOf(fromId) + " 到 " + Railways.NameOf(toId)
                    + " · 预计花费 " + cost + " 金 · 工期 " + days + " 天",
                    opts, true, 1, 1, "确定", "取消",
                    delegate (List<InquiryElement> sel)
                    {
                        bool built = false;
                        try
                        {
                            if (sel != null && sel.Count > 0 && (string)sel[0].Identifier == "go")
                            {
                                string e = Railways.Establish(PlayerKingdom(), fromId, toId);
                                built = e == "";
                                MapSelection.Message(built ? "已开工：" + Railways.NameOf(fromId) + " ↔ " + Railways.NameOf(toId) : e);
                                DLog.Force("铁路点选建线: " + (built ? "成功" : e));
                            }
                            else
                            {
                                _pickReplaceNext = true;
                                MapSelection.Message("已取消修建（起点 " + Railways.NameOf(fromId) + " 保留；再点一座城=改起点，点[地图选线]结束）");
                            }
                        }
                        catch { }
                        finally
                        {
                            RailSystem.ClearPreview();
                            if (built) ClearPickState();   // 未建成: 保留起点, 下一次点击可改起点/改终点
                        }
                    },
                    delegate (List<InquiryElement> sel)
                    {
                        try
                        {
                            _pickReplaceNext = true;
                            RailSystem.ClearPreview();
                            MapSelection.Message("已取消修建（起点 " + Railways.NameOf(fromId) + " 保留；再点一座城=改起点，点[地图选线]结束）");
                        }
                        catch { }
                    }, null, false));
            }
            catch (Exception ex) { DLog.Force("铁路点选异常: " + ex.Message); }
        }

        // 取消地图点选: 清状态 + 清预览 + 提示
        internal static void CancelPick()
        {
            try
            {
                ClearPickState();
                RailSystem.ClearPreview();
                MapSelection.Message("已取消铁路选线");
            }
            catch (Exception ex) { DLog.Force("取消铁路选线异常: " + ex.Message); }
        }

        // 铁路地图模式下点城镇/城堡 = 消息显示基建/运输概况, 有铁路则打开运输菜单(返回 true = 已处理)
        internal static bool RailModeClick(Settlement s)
        {
            try
            {
                if (_picking || MapDataMode.Current != MapData.Rail) return false;
                if (s == null || (!s.IsTown && !s.IsCastle)) return false;
                string info = CityRailInfo(s.StringId);
                if (Railways.LinesOf(s.StringId).Count == 0)
                {
                    MapSelection.Message("该城暂无铁路" + (info.Length > 0 ? " · " + info : ""));
                    return true;
                }
                string brief = CityLinesBrief(s.StringId);
                if (brief.Length > 0) info = info.Length > 0 ? info + " · " + brief : brief;
                if (info.Length > 0) MapSelection.Message(info);
                TransportMenu(s);
                return true;
            }
            catch { return false; }
        }

        private static void ClearPickState() { _picking = false; _pickFromId = null; _pickReplaceNext = false; }

        private static bool IsPlayerCity(Settlement s)
        {
            try
            {
                var k = PlayerKingdom();
                return k != null && s != null && ReferenceEquals(s.MapFaction, k);
            }
            catch { return false; }
        }

        private static string CityName(Settlement s)
        {
            try { return s != null && s.Name != null ? s.Name.ToString() : (s != null ? s.StringId : "?"); }
            catch { return "?"; }
        }

        // ================= v4.164: 城市详情页 - 铁路运输 =================
        // 列出本城铁路连接与可装车部队, 装车后部队隐藏沿线移动, 到达敌城自动围城
        internal static void TransportMenu(Settlement city)
        {
            try
            {
                if (city == null) return;
                var lines = Railways.LinesOf(city.StringId);
                if (!HasUsableLine(city.StringId)) { MapSelection.Message("本城没有运营中的铁路"); return; }
                var parties = PartiesAt(city);
                if (parties.Count == 0) { MapSelection.Message("本城没有可装车的部队(领主军/国防军)"); return; }

                var opts = new List<InquiryElement>();
                for (int i = 0; i < parties.Count; i++)
                {
                    var p = parties[i];
                    string pname = p.Name != null ? p.Name.ToString() : p.StringId;
                    opts.Add(new InquiryElement("p:" + p.StringId, pname, null, true,
                        (p.LeaderHero != null && p.LeaderHero.Name != null ? ("领主: " + p.LeaderHero.Name.ToString() + " · ") : "")
                        + "兵力 " + (p.MemberRoster != null ? p.MemberRoster.TotalManCount : 0)));
                }
                string body = "本城铁路连接 " + lines.Count + " 条:\n";
                for (int i = 0; i < lines.Count && i < 6; i++)
                {
                    var l = lines[i];
                    string other = l.FromId == city.StringId ? l.ToId : l.FromId;
                    body += " · " + Railways.NameOf(other) + (IsEnemyCity(other, city) ? "(敌城: 到达后自动围城)" : "") + "\n";
                }
                body += "\n选择要装车的部队(费用 = 距离×2 金):";
                PanelInputGuard.ShowPopup(new MultiSelectionInquiryData(
                    "货物运输 - " + (city.Name != null ? city.Name.ToString() : city.StringId), body, opts, true, 1, 1, "下一步", "关闭",
                    delegate (List<InquiryElement> sel)
                    {
                        try
                        {
                            if (sel == null || sel.Count == 0) return;
                            string pid = sel[0].Identifier as string;
                            if (pid == null || !pid.StartsWith("p:")) return;
                            string partyId = pid.Substring(2);
                            PickTransportDest(city, partyId);
                        }
                        catch { }
                    }, null, null, false));
            }
            catch (Exception ex) { DLog.Force("铁路运输 UI 失败: " + ex.Message); }
        }

        private static void PickTransportDest(Settlement city, string partyId)
        {
            try
            {
                var lines = Railways.LinesOf(city.StringId);
                var opts = new List<InquiryElement>();
                for (int i = 0; i < lines.Count; i++)
                {
                    var l = lines[i];
                    if (l == null || l.Broken) continue;
                    string other = l.FromId == city.StringId ? l.ToId : l.FromId;
                    bool enemy = IsEnemyCity(other, city);
                    opts.Add(new InquiryElement(other,
                        Railways.NameOf(other) + (enemy ? "  [敌城: 到达后自动围城]" : ""), null, true,
                        "距离 " + (int)l.Length + " · 约 " + (int)(l.Length / 1000f * 1.5f) + " 天 · 费 " + (int)(l.Length * 2f) + " 金"));
                }
                PanelInputGuard.ShowPopup(new MultiSelectionInquiryData(
                    "货物运输 - 目的地", "选择目的地(仅铁路连接的城市)", opts, true, 1, 1, "装车", "取消",
                    delegate (List<InquiryElement> sel)
                    {
                        try
                        {
                            if (sel == null || sel.Count == 0) return;
                            string toId = sel[0].Identifier as string;
                            var p = Railways.FindParty(partyId);
                            if (p == null) { MapSelection.Message("部队不存在"); return; }
                            string err = Railways.LoadArmy(p, city, toId);
                            MapSelection.Message(err == "" ? ("已装车: 部队沿铁路前往 " + Railways.NameOf(toId)) : err);
                        }
                        catch { }
                    }, null, null, false));
            }
            catch { }
        }

        // ================= v4.2xx: 独立运兵(军列)与货物运输入口 =================
        // 货物运输: 选择装车城市(有铁路的本国城镇/城堡) -> 复用常规装车流程
        private static void CargoCityPicker()
        {
            try
            {
                var opts = new List<InquiryElement>();
                var list = PlayerSettlements();
                for (int i = 0; i < list.Count; i++)
                {
                    var s = list[i];
                    if (!HasUsableLine(s.StringId)) continue;
                    int prod = 0, cons = 0;
                    try { Railways.TransportOf(s.StringId, out prod, out cons); } catch { }
                    string hint = "运输余量 " + (prod - cons) + " · " + CityRailInfo(s.StringId);
                    opts.Add(new InquiryElement(s.StringId, s.Name != null ? s.Name.ToString() : s.StringId, null, true, hint));
                }
                if (opts.Count == 0) { MapSelection.Message("本国暂无已通铁路的城镇/城堡"); return; }
                PanelInputGuard.ShowPopup(new MultiSelectionInquiryData(
                    "货物运输 - 出发城市", "选择出发城市(仅铁路城市)", opts, true, 1, 1, "下一步", "取消",
                    delegate (List<InquiryElement> sel)
                    {
                        try
                        {
                            if (sel == null || sel.Count == 0) return;
                            var s = Railways.FindSettlement(sel[0].Identifier as string);
                            if (s == null) { MapSelection.Message("城市不存在"); return; }
                            TransportMenu(s);
                        }
                        catch { }
                    }, null, null, false));
            }
            catch (Exception ex) { DLog.Force("货物运输 UI 失败: " + ex.Message); }
        }

        // 军列运兵: 选择出发城市(有铁路的本国城镇/城堡), 显示运力与集结天数
        private static void MilitaryCityPicker()
        {
            try
            {
                var opts = new List<InquiryElement>();
                var list = PlayerSettlements();
                for (int i = 0; i < list.Count; i++)
                {
                    var s = list[i];
                    if (!HasUsableLine(s.StringId)) continue;
                    int used = 0, cap = 0, cd = 0, days = 0;
                    try { Railways.MilStatusOf(s.StringId, out used, out cap, out cd); } catch { }
                    try { days = (int)Railways.MobilizationDays(s.StringId); } catch { }
                    int remain = cap - used; if (remain < 0) remain = 0;
                    bool ok = cap > 0 && cd <= 0;
                    string hint = "军列运力 今日余 " + remain + "/" + cap + " · 冷却 " + cd + " 天 · 集结 " + days + " 天";
                    if (cap <= 0) hint += " · 本城无军列运力";
                    else if (cd > 0) hint += " · 调度冷却中, 暂不可发车";
                    opts.Add(new InquiryElement(s.StringId, s.Name != null ? s.Name.ToString() : s.StringId,
                        null, ok, hint));
                }
                if (opts.Count == 0) { MapSelection.Message("本国暂无已通铁路的城镇/城堡"); return; }
                PanelInputGuard.ShowPopup(new MultiSelectionInquiryData(
                    "军列运兵 - 出发城市", "选择出发城市(仅铁路城市); 运力决定一次可投送的兵力", opts, true, 1, 1, "下一步", "取消",
                    delegate (List<InquiryElement> sel)
                    {
                        try
                        {
                            if (sel == null || sel.Count == 0) return;
                            var s = Railways.FindSettlement(sel[0].Identifier as string);
                            if (s == null) { MapSelection.Message("城市不存在"); return; }
                            MilitaryPartyPicker(s);
                        }
                        catch { }
                    }, null, null, false));
            }
            catch (Exception ex) { DLog.Force("军列运兵 UI 失败: " + ex.Message); }
        }

        // 军列运兵: 选择投送部队(沿用装车选部队的列表方式)
        private static void MilitaryPartyPicker(Settlement city)
        {
            try
            {
                var parties = PartiesAt(city);
                if (parties.Count == 0) { MapSelection.Message("本城没有可装车的部队(领主军/国防军)"); return; }
                int used = 0, cap = 0, cd = 0, days = 0;
                try { Railways.MilStatusOf(city.StringId, out used, out cap, out cd); } catch { }
                try { days = (int)Railways.MobilizationDays(city.StringId); } catch { }
                int remain = cap - used; if (remain < 0) remain = 0;
                var lines = Railways.LinesOf(city.StringId);
                float minLen = 0f; int usable = 0;
                for (int i = 0; i < lines.Count; i++)
                {
                    var l = lines[i];
                    if (l == null || l.Broken) continue;
                    if (usable == 0 || l.Length < minLen) minLen = l.Length;
                    usable++;
                }
                string fareTag = usable > 1 ? "起" : "";
                var opts = new List<InquiryElement>();
                for (int i = 0; i < parties.Count; i++)
                {
                    var p = parties[i];
                    string pname = p.Name != null ? p.Name.ToString() : p.StringId;
                    int men = p.MemberRoster != null ? p.MemberRoster.TotalManCount : 0;
                    int load = (men + 99) / 100;                       // 每 100 兵力 = 1 运力单位
                    int fare = usable > 0 ? (int)(minLen * 2f) + men / 100 : 0;
                    int ride = usable > 0 ? (int)Math.Max(1f, minLen / 1000f * 1.125f) : 0;
                    bool ok = cd <= 0 && cap > 0 && load <= remain;
                    string why = cd > 0 ? "军列调度冷却中(还需 " + cd + " 天)"
                        : (cap <= 0 ? "本城无军列运力"
                        : (load > remain ? "运力不足(需 " + load + ", 今日余 " + remain + "/" + cap + ")" : null));
                    string hint = (p.LeaderHero != null && p.LeaderHero.Name != null ? ("领主: " + p.LeaderHero.Name.ToString() + " · ") : "")
                        + "兵力 " + men + " · 运费 " + fare + fareTag + " 金 · 车程 " + ride + fareTag + " 天 · 占用 " + load + " 单位";
                    if (why != null) hint += " · " + why;
                    opts.Add(new InquiryElement("p:" + p.StringId, pname, null, ok, hint));
                }
                string body = "军列运力 今日余 " + remain + "/" + cap + " · 冷却 " + cd + " 天 · 集结 " + days + " 天\n"
                    + (cd > 0 ? "军列调度冷却中(还需 " + cd + " 天), 暂不可发车\n" : "")
                    + "选择要投送的部队(占用 = 每 100 兵力 1 单位):";
                PanelInputGuard.ShowPopup(new MultiSelectionInquiryData(
                    "军列运兵 - " + (city.Name != null ? city.Name.ToString() : city.StringId), body, opts, true, 1, 1, "下一步", "关闭",
                    delegate (List<InquiryElement> sel)
                    {
                        try
                        {
                            if (sel == null || sel.Count == 0) return;
                            string pid = sel[0].Identifier as string;
                            if (pid == null || !pid.StartsWith("p:")) return;
                            MilitaryDestPicker(city, pid.Substring(2));
                        }
                        catch { }
                    }, null, null, false));
            }
            catch (Exception ex) { DLog.Force("军列运兵选部队失败: " + ex.Message); }
        }

        // 军列运兵: 选择目的地(铁路连接城市), 显示运费与车程, 确认后发车
        private static void MilitaryDestPicker(Settlement city, string partyId)
        {
            try
            {
                var p = Railways.FindParty(partyId);
                if (p == null) { MapSelection.Message("部队不存在"); return; }
                var lines = Railways.LinesOf(city.StringId);
                int used = 0, cap = 0, cd = 0;
                try { Railways.MilStatusOf(city.StringId, out used, out cap, out cd); } catch { }
                int remain = cap - used; if (remain < 0) remain = 0;
                int men = 0;
                try { men = p.MemberRoster != null ? p.MemberRoster.TotalManCount : 0; } catch { }
                int load = (men + 99) / 100;
                var opts = new List<InquiryElement>();
                for (int i = 0; i < lines.Count; i++)
                {
                    var l = lines[i];
                    if (l == null || l.Broken) continue;
                    string other = l.FromId == city.StringId ? l.ToId : l.FromId;
                    bool enemy = IsEnemyCity(other, city);
                    int gold = 0, days = 0;
                    try { gold = (int)Railways.MilitaryCost(city.StringId, other, p); } catch { }
                    try { days = (int)Railways.MilitaryDays(city.StringId, other); } catch { }
                    string why = cd > 0 ? "军列调度冷却中(还需 " + cd + " 天)"
                        : (cap <= 0 ? "本城无军列运力"
                        : (load > remain ? "运力不足(需 " + load + ", 今日余 " + remain + "/" + cap + ")" : ""));
                    bool ok = why.Length == 0;
                    opts.Add(new InquiryElement(other,
                        Railways.NameOf(other) + (enemy ? "  [敌城: 到达后自动围城]" : "")
                        + " · 距离 " + (int)l.Length + " · 运费 " + gold + " 金 · 车程 " + days + " 天"
                        + (why.Length > 0 ? " · " + why : ""), null, ok,
                        "运费 " + gold + " 金 · 车程 " + days + " 天 · 占用 " + load + " 单位 · 今日余 " + remain + "/" + cap
                        + (why.Length > 0 ? " · " + why : "")));
                }
                if (opts.Count == 0) { MapSelection.Message("该城没有运营中的铁路可发车"); return; }
                string pname = p.Name != null ? p.Name.ToString() : partyId;
                PanelInputGuard.ShowPopup(new MultiSelectionInquiryData(
                    "军列运兵 - 目的地", "部队: " + pname + " · 兵力 " + men + " · 占用 " + load + " 单位\n选择目的地(仅铁路连接的城市)", opts, true, 1, 1, "发车", "取消",
                    delegate (List<InquiryElement> sel)
                    {
                        try
                        {
                            if (sel == null || sel.Count == 0) return;
                            string toId = sel[0].Identifier as string;
                            string err = Railways.LoadArmyMilitary(p, city, toId);
                            MapSelection.Message(err == "" ? ("军列已发车: " + pname + " 前往 " + Railways.NameOf(toId)) : err);
                        }
                        catch { }
                    }, null, null, false));
            }
            catch (Exception ex) { DLog.Force("军列运兵目的地 UI 失败: " + ex.Message); }
        }

        // 城镇铁路/基建概况(选城与地图点击时显示)
        private static string CityRailInfo(string sid)
        {
            try
            {
                float baseV = Infrastructure.BaseOf(sid);
                float useV = Infrastructure.UsageOf(sid);
                float access = Infrastructure.AccessOf(sid);      // v4.2xx: 市场准入直接用基建口径, 不再自算
                int migCap = (int)Math.Round(5f * baseV);         // PopSim: 每周迁移上限 = 500 + 5×基建
                int prod = 0, cons = 0;
                Railways.TransportOf(sid, out prod, out cons);
                return "基建 " + (int)baseV + "/占用 " + (int)useV
                    + " · 市场准入 " + (int)Math.Round(access * 100f) + "%"
                    + " · 迁移上限 +" + migCap + "/周 · 产出 ×" + access.ToString("0.00")
                    + " · 运输 产" + prod + "/耗" + cons;
            }
            catch { return ""; }
        }

        // 地图点击补充信息: 线路条数/平均等级/国有私有 + 军列今日余量(MilStatusOf)
        private static string CityLinesBrief(string sid)
        {
            try
            {
                var lines = Railways.LinesOf(sid);
                int n = 0, tierSum = 0, own = 0;
                for (int i = 0; i < lines.Count; i++)
                {
                    var l = lines[i];
                    if (l == null) continue;
                    n++;
                    tierSum += Railways.TierOf(l);
                    bool st = false;
                    try { st = Railways.IsStateOwned(l); } catch { }
                    if (st) own++;
                }
                if (n == 0) return "";
                int used = 0, cap = 0, cd = 0;
                try { Railways.MilStatusOf(sid, out used, out cap, out cd); } catch { }
                int remain = cap - used;
                if (remain < 0) remain = 0;
                return n + " 条线路 · 平均等级 " + (tierSum / (float)n).ToString("0.#")
                    + " · 国有 " + own + "/私有 " + (n - own)
                    + " · 军列 今日余 " + remain + "/" + cap
                    + (cd > 0 ? "(冷却 " + cd + " 天)" : "");
            }
            catch { return ""; }
        }

        // 是否有非中断的运营线路(选城列表过滤用)
        private static bool HasUsableLine(string sid)
        {
            try
            {
                var lines = Railways.LinesOf(sid);
                for (int i = 0; i < lines.Count; i++)
                    if (lines[i] != null && !lines[i].Broken) return true;
            }
            catch { }
            return false;
        }

        private static List<TaleWorlds.CampaignSystem.Party.MobileParty> PartiesAt(Settlement city)
        {
            var list = new List<TaleWorlds.CampaignSystem.Party.MobileParty>();
            try
            {
                var pk = PlayerKingdom();
                if (pk == null) return list;
                foreach (var p in TaleWorlds.CampaignSystem.Party.MobileParty.All)
                {
                    if (p == null || !p.IsActive) continue;
                    if (p.IsMainParty || p.IsGarrison || p.IsMilitia || p.IsCaravan) continue;
                    if (p.LeaderHero == null) continue;
                    if (!ReferenceEquals(p.MapFaction, pk)) continue;
                    float dx = p.Position.X - city.Position.X;
                    float dy = p.Position.Y - city.Position.Y;
                    if (dx * dx + dy * dy > 30f * 30f) continue;
                    list.Add(p);
                }
                list.Sort(delegate (TaleWorlds.CampaignSystem.Party.MobileParty a, TaleWorlds.CampaignSystem.Party.MobileParty b)
                {
                    int na = a.MemberRoster != null ? a.MemberRoster.TotalManCount : 0;
                    int nb = b.MemberRoster != null ? b.MemberRoster.TotalManCount : 0;
                    return nb.CompareTo(na);
                });
            }
            catch { }
            return list;
        }

        private static bool IsEnemyCity(string settlementId, Settlement from)
        {
            try
            {
                var to = Railways.FindSettlement(settlementId);
                if (to == null || from == null) return false;
                var pk = PlayerKingdom();
                if (pk == null || to.MapFaction == null) return false;
                return pk.IsAtWarWith(to.MapFaction);
            }
            catch { return false; }
        }

        private static Kingdom PlayerKingdom()
        {
            try { return NationalWillOrders.Behavior != null ? NationalWillOrders.Behavior.NationKingdom : null; }
            catch { return null; }
        }

        private static List<Settlement> PlayerSettlements()
        {
            var list = new List<Settlement>();
            try
            {
                var k = PlayerKingdom();
                if (k == null) return list;
                foreach (var s in k.Settlements)
                {
                    if (s == null) continue;
                    if (!s.IsTown && !s.IsCastle) continue;
                    list.Add(s);
                }
                list.Sort(delegate (Settlement a, Settlement b)
                {
                    string na = a.Name != null ? a.Name.ToString() : a.StringId;
                    string nb = b.Name != null ? b.Name.ToString() : b.StringId;
                    return string.Compare(na, nb, StringComparison.Ordinal);
                });
            }
            catch { }
            return list;
        }

        private static string KingdomName(string id)
        {
            try
            {
                var k = Railways.FindKingdom(id);
                return k != null && k.Name != null ? k.Name.ToString() : "—";
            }
            catch { return "—"; }
        }
    }
}
