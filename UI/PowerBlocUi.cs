using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace FeudalInternalAffairs
{
    // 权力集团 UI(文档 24.8; v4.138 照 V3 Power_bloc 官方机制)
    internal static class PowerBlocUi
    {
        internal static void Show()
        {
            try
            {
                if (PanelInputGuard.AnyPopupActive()) return;
                var opts = new List<InquiryElement>();
                if (!PowerBlocs.Founded)
                {
                    opts.Add(new InquiryElement(0, "创建贸易联盟(5000 第纳尔)", null, true, PowerBlocs.IdentityDesc[0]));
                    opts.Add(new InquiryElement(1, "创建军事同盟(5000 第纳尔)", null, true, PowerBlocs.IdentityDesc[1]));
                    opts.Add(new InquiryElement(2, "创建主权帝国(5000 第纳尔)", null, true, PowerBlocs.IdentityDesc[2]));
                    opts.Add(new InquiryElement("int", "利益宣示: " + Interests.StatusText(), null, true, "对远方国家宣示利益(500 第纳尔/国)后才能介入其战争与博弈"));
                    PanelInputGuard.ShowPopup(new MultiSelectionInquiryData(
                        "权力集团", "创建集团花费 5000 第纳尔(经典为 500 影响力, 本 mod 国家意志无影响力概念 -> 走国库); 成员共享集团效果",
                        opts, true, 1, 1, "创建", "关闭", OnPick, null, null, false));
                    return;
                }
                opts.Add(new InquiryElement("invite", "邀请成员国...", null, PowerBlocs.IsLeader(), "邀请花 1000 第纳尔; AI 按关系与实力决定是否接受(Leverage 简化)"));
                opts.Add(new InquiryElement("kick", "开除成员国...", null, PowerBlocs.IsLeader() && PowerBlocs.Members.Count > 1, "开除: 凝聚力 -10, 关系恶化(经典)"));
                opts.Add(new InquiryElement("princ", "提升原则: " + PowerBlocs.PrincipleText(), null, true, "原则共 3 档, 每档消耗对应数量授权(经典)"));
                opts.Add(new InquiryElement("ident", "更换集团身份(1 授权)", null, PowerBlocs.Mandates >= 1, "身份决定集团的核心效果(经典: 六身份骑砍化 3 种)"));
                opts.Add(new InquiryElement("int", "利益宣示: " + Interests.StatusText(), null, true, "对远方国家宣示利益(500 第纳尔/国)后才能介入其战争与博弈"));
                string desc = PowerBlocs.IdentityDesc[PowerBlocs.Identity] + "\n凝聚力 " + (int)PowerBlocs.Cohesion
                    + "(" + PowerBlocs.CohesionLevel() + ", 倍数 ×" + PowerBlocs.CohesionMult().ToString("F2") + ")"
                    + " · 授权 " + PowerBlocs.Mandates + "/3 (" + (int)PowerBlocs.MandateProgress + "/2000)"
                    + "\n" + PowerBlocs.PrincipleText();
                PanelInputGuard.ShowPopup(new MultiSelectionInquiryData(
                    PowerBlocs.Name, desc, opts, true, 1, 1, "执行", "关闭", OnPick, null, null, false));
            }
            catch (Exception ex) { DLog.Force("权力集团 UI 失败: " + ex.Message); }
        }

        private static void OnPick(List<InquiryElement> sel)
        {
            try
            {
                if (sel == null || sel.Count == 0) return;
                object id = sel[0].Identifier;
                if (id is int)
                {
                    MapSelection.Message(PowerBlocs.Create((int)id));
                    return;
                }
                string s = id as string;
                if (s == "invite") InviteMenu();
                else if (s == "kick") KickMenu();
                else if (s == "princ") PrincipleMenu();
                else if (s == "ident") IdentityMenu();
                else if (s == "int") InterestsMenu();
            }
            catch { }
        }

        private static void IdentityMenu()
        {
            try
            {
                if (PanelInputGuard.AnyPopupActive()) return;
                var opts = new List<InquiryElement>();
                for (int i = 0; i < 3; i++)
                {
                    int id = i;
                    opts.Add(new InquiryElement(id, PowerBlocs.IdentityNames[i] + (i == PowerBlocs.Identity ? " (当前)" : ""),
                        null, i != PowerBlocs.Identity, PowerBlocs.IdentityDesc[i]));
                }
                PanelInputGuard.ShowPopup(new MultiSelectionInquiryData(
                    "更换集团身份", "花费 1 授权(经典: 更换原则/身份需授权)", opts, true, 1, 1, "确认", "返回",
                    delegate (List<InquiryElement> s2)
                    {
                        try { if (s2 != null && s2.Count > 0) MapSelection.Message(PowerBlocs.SetIdentity((int)s2[0].Identifier)); }
                        catch { }
                    }, delegate (List<InquiryElement> c) { Show(); }, null, false));
            }
            catch { }
        }

        private static void InviteMenu()
        {
            try
            {
                if (PanelInputGuard.AnyPopupActive()) return;
                var opts = new List<InquiryElement>();
                var list = TradeRoutes.PartnerKingdoms();
                for (int i = 0; i < list.Count; i++)
                {
                    var k = list[i];
                    if (PowerBlocs.IsMember(k.StringId)) continue;
                    string id = k.StringId;
                    opts.Add(new InquiryElement(id, k.Name != null ? k.Name.ToString() : id, null, true,
                        "花 1000 第纳尔; 战争状态不可邀请"));
                }
                if (opts.Count == 0) { MapSelection.Message("没有可邀请的王国"); return; }
                PanelInputGuard.ShowPopup(new MultiSelectionInquiryData(
                    "邀请成员国", "经典: 邀请需杠杆优势 200; 我们简化为关系 + 实力比判定", opts, true, 1, 1, "邀请", "返回",
                    delegate (List<InquiryElement> s2)
                    {
                        try
                        {
                            if (s2 == null || s2.Count == 0) return;
                            string kid = s2[0].Identifier as string;
                            foreach (var k in list) if (k.StringId == kid) { MapSelection.Message(PowerBlocs.Invite(k)); break; }
                        }
                        catch { }
                    }, delegate (List<InquiryElement> c) { Show(); }, null, false));
            }
            catch { }
        }

        private static void KickMenu()
        {
            try
            {
                if (PanelInputGuard.AnyPopupActive()) return;
                var opts = new List<InquiryElement>();
                for (int i = 0; i < PowerBlocs.Members.Count; i++)
                {
                    var m = PowerBlocs.Members[i];
                    if (m.KingdomId == PowerBlocs.LeaderId) continue;
                    int idx = i;
                    opts.Add(new InquiryElement(idx, m.Name, null, true, "开除: 凝聚力 -10(经典)"));
                }
                if (opts.Count == 0) { MapSelection.Message("没有可开除的成员"); return; }
                PanelInputGuard.ShowPopup(new MultiSelectionInquiryData(
                    "开除成员国", "经典: 开除降低凝聚力并恶化关系", opts, true, 1, 1, "开除", "返回",
                    delegate (List<InquiryElement> s2)
                    {
                        try { if (s2 != null && s2.Count > 0) MapSelection.Message(PowerBlocs.Kick((int)s2[0].Identifier)); }
                        catch { }
                    }, delegate (List<InquiryElement> c) { Show(); }, null, false));
            }
            catch { }
        }

        private static void PrincipleMenu()
        {
            try
            {
                if (PanelInputGuard.AnyPopupActive()) return;
                var opts = new List<InquiryElement>();
                for (int i = 0; i < 2; i++)
                {
                    int pi = i;
                    int cur = PowerBlocs.PrincipleTier[PowerBlocs.Identity][i];
                    bool can = cur < 3;
                    string eff = PowerBlocs.PrincipleEffects[PowerBlocs.Identity][i][Math.Min(2, cur)];
                    opts.Add(new InquiryElement(pi, PowerBlocs.PrincipleNames[PowerBlocs.Identity][i] + "  " + cur + "/3"
                        + (can ? "  [下一档 " + (cur + 1) + " 授权]" : "  [满级]"), null, can, "下一档效果: " + eff));
                }
                PanelInputGuard.ShowPopup(new MultiSelectionInquiryData(
                    "集团原则", "经典: 原则 3 档, 每档花费等于档位的授权数; 授权由成员数与集团排名累积(2000 进度 = 1 授权)",
                    opts, true, 1, 1, "提升", "返回",
                    delegate (List<InquiryElement> s2)
                    {
                        try { if (s2 != null && s2.Count > 0) MapSelection.Message(PowerBlocs.UpgradePrinciple((int)s2[0].Identifier)); }
                        catch { }
                    }, delegate (List<InquiryElement> c) { Show(); }, null, false));
            }
            catch { }
        }

        private static void InterestsMenu()
        {
            try
            {
                if (PanelInputGuard.AnyPopupActive()) return;
                var opts = new List<InquiryElement>();
                var list = TradeRoutes.PartnerKingdoms();
                for (int i = 0; i < list.Count; i++)
                {
                    var k = list[i];
                    string kid = k.StringId;
                    bool has = Interests.Has(kid);
                    opts.Add(new InquiryElement(kid, (k.Name != null ? k.Name.ToString() : kid) + (has ? "  [已有利益]" : ""),
                        null, !has, has ? "已宣示(邻国自动)" : "宣示花 500 第纳尔"));
                }
                if (opts.Count == 0) { MapSelection.Message("没有可选国家"); return; }
                PanelInputGuard.ShowPopup(new MultiSelectionInquiryData(
                    "利益宣示", Interests.StatusText() + "\n经典: 无利益区域不能介入战争与外交博弈", opts, true, 1, 1, "宣示", "返回",
                    delegate (List<InquiryElement> s2)
                    {
                        try
                        {
                            if (s2 == null || s2.Count == 0) return;
                            string kid = s2[0].Identifier as string;
                            foreach (var k in list) if (k.StringId == kid) { MapSelection.Message(Interests.Declare(k)); break; }
                        }
                        catch { }
                    }, delegate (List<InquiryElement> c) { Show(); }, null, false));
            }
            catch { }
        }
    }
}
