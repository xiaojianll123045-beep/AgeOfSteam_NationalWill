using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace FeudalInternalAffairs
{
    // 钱箱(文档 20.6; v4.187 实装): 国库之外的现金储备
    //   利息 0.03%/日(约 0.9%/月) + 铸币分红(铸币权日收入的 25% 入箱) + 存/取
    //   入口: 财政页「机构」菜单里的「钱箱」选项(与 InstitutionsUi 同一套询问框)
    internal static class CashChest
    {
        internal static float Chest;
        internal static int LastDay = -1;
        internal const float DailyRate = 0.0003f;

        internal static void Accrue(int today)
        {
            try
            {
                if (LastDay < 0) { LastDay = today; return; }
                int days = today - LastDay;
                if (days <= 0) return;
                if (days > 400) days = 400;   // 防读档跨度过大
                float mint = 0f;
                try { mint = MintRight.DailyIncome * 0.25f; } catch { }
                Chest = Chest * (float)Math.Pow(1.0 + DailyRate, days) + mint * days;
                LastDay = today;
            }
            catch { }
        }

        internal static string Deposit(int amount)
        {
            try
            {
                if (amount <= 0) return "金额无效。";
                var hero = Hero.MainHero;
                if (hero == null) return "找不到君主。";
                if (hero.Gold < amount) return "金币不足(需 " + amount + ", 现有 " + hero.Gold + ")。";
                hero.ChangeHeroGold(-amount);
                Chest += amount;
                return "已存入钱箱 " + amount + " 第纳尔, 现有 " + ((int)Chest) + "。";
            }
            catch (Exception ex) { return "存入失败: " + ex.Message; }
        }

        internal static string Withdraw(int amount)
        {
            try
            {
                if (amount <= 0) return "金额无效。";
                if (Chest < 1f) return "钱箱是空的。";
                int take = (int)Math.Min(amount, Chest);
                Chest -= take;
                var hero = Hero.MainHero;
                if (hero != null) hero.ChangeHeroGold(take);
                else { try { EconomyWorld.TreasuryAdd(take); } catch { } }
                return "已从钱箱取出 " + take + " 第纳尔, 剩余 " + ((int)Chest) + "。";
            }
            catch (Exception ex) { return "取出失败: " + ex.Message; }
        }

        // 询问框 UI(财政页「机构」菜单进入)
        internal static void Show()
        {
            try
            {
                int today = Politics.Today();
                Accrue(today);
                int chest = (int)Chest;
                var opts = new List<InquiryElement>
                {
                    new InquiryElement("d5", "存入 5,000", null, true, null),
                    new InquiryElement("d20", "存入 20,000", null, true, null),
                    new InquiryElement("da", "全部存入(可支配金币)", null, true, null),
                    new InquiryElement("w5", "取出 5,000", null, Chest >= 5000f, null),
                    new InquiryElement("wa", "全部取出", null, Chest >= 1f, null)
                };
                PanelInputGuard.ShowPopup(new MultiSelectionInquiryData(
                    "钱箱",
                    "钱箱: " + chest + " 第纳尔\n日息 0.03%(约 0.9%/月) + 铸币分红(铸币权日收入 25% 入箱)\n君主可支配金币: " + (Hero.MainHero != null ? Hero.MainHero.Gold : 0),
                    opts, true, 1, 1, "确定", "取消",
                    delegate (List<InquiryElement> sel)
                    {
                        if (sel == null || sel.Count == 0) return;
                        string id = sel[0].Identifier as string;
                        string msg = null;
                        if (id == "d5") msg = Deposit(5000);
                        else if (id == "d20") msg = Deposit(20000);
                        else if (id == "da") msg = Deposit(Hero.MainHero != null ? Hero.MainHero.Gold : 0);
                        else if (id == "w5") msg = Withdraw(5000);
                        else if (id == "wa") msg = Withdraw((int)Chest);
                        if (msg != null) { try { MapSelection.Message(msg); } catch { } DLog.Force("钱箱: " + msg); }
                    }, null, null, false));
            }
            catch (Exception ex) { DLog.Force("钱箱界面失败: " + ex.Message); }
        }

        internal static string Save()
        {
            try { return ((int)Chest) + ";" + LastDay; } catch { return ""; }
        }

        internal static void Load(string data)
        {
            try
            {
                Chest = 0f; LastDay = -1;
                if (string.IsNullOrEmpty(data)) return;
                var f = data.Split(';');
                int c;
                if (f.Length > 0 && int.TryParse(f[0], out c)) Chest = c;
                if (f.Length > 1 && int.TryParse(f[1], out c)) LastDay = c;
            }
            catch { }
        }
    }
}
