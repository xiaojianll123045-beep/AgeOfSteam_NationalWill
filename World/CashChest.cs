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
                // v4.290 修复: 这 25% 铸币原先**凭空创造**(只加 Chest, 不冲减任何地方) ——
                //   而 MintRight 已经把铸币收入 100% 记进国库(TreasuryAdd + Fiscal.AddMint),
                //   于是整个体系凭空多出 25% 的货币。改为从国库**划转**: 先扣再进钱箱, 总量守恒。
                //   国库不够时**不分红**(而不是凭空造), 避免又开一个隐性的负国库入口。
                int mintTotal = (int)Math.Max(0f, mint * days);
                if (mintTotal > 0)
                {
                    try
                    {
                        if (EconomyWorld.Treasury.Gold >= mintTotal)
                        {
                            EconomyWorld.TreasurySpend(mintTotal);
                            Fiscal.AddCourtPaid(mintTotal);
                        }
                        else mintTotal = 0;
                    }
                    catch { mintTotal = 0; }
                }
                Chest = Chest * (float)Math.Pow(1.0 + DailyRate, days) + mintTotal;
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
                // v4.290 记账修复: 原先只调 hero.ChangeHeroGold, 既不动 Treasury.Gold 也不进任何科目 ——
                //   于是"存入当天国库数字不降、取出当天不升", 要等次日国库基线(EconomyBehavior.cs:487
                //   用 hero.Gold 覆盖 Treasury.Gold)才悄悄补平, 期间财政页的"今日净额"与科目明细对不上。
                //   改走统一入口: TreasurySpend/TreasuryAdd 本来就同时改两边, 并在这里补记账。
                EconomyWorld.TreasurySpend(amount);
                try { Fiscal.AddCourtPaid(amount); } catch { }
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
                // v4.290 记账修复: 同 Deposit —— 走统一入口, 并记进科目(与上面的支出侧配对)。
                if (hero != null) { EconomyWorld.TreasuryAdd(take); try { Fiscal.AddCourtGot(take); } catch { } }
                else { try { EconomyWorld.TreasuryAdd(take); } catch { } Fiscal.AddCourtGot(take); }
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
