using System;
using System.Collections.Generic;
using TaleWorlds.Library;

namespace FeudalInternalAffairs
{
    // v4.262: 教程系统重做 —— 旧的"每页长文案弹窗"(18 页静态说明书)与"开局分步教程"(接管后连弹 8 步)
    //   **已全部移除**。现在各面板的[教程]按钮 = 打开「挑战式引导」面板(当前挑战 + 怎么做 + 为什么 +
    //   已完成列表 + 本页对应哪一条挑战), 教程内容全部由 UI\Guide.cs 的 12 条挑战链提供,
    //   不再有静态长文案墙, 也不再有任何强制弹窗(正常开始的游戏全程静默)。
    //
    //   本类只保留三件事:
    //     ① 页面访问记录 —— Guide 的第 1 条挑战("看懂账本")要检测玩家是否真的打开过[财政]页
    //     ② 教程局的选国引导 —— 唯一保留的静态文案, 只在主菜单「蒸汽时代教程」开局时弹一次
    //     ③ 把[教程]弹窗转交给 Guide
    internal static class Tutorials
    {
        // 本会话里"被打开过的页面"清单(在 Page(id) 最前面登记, 不受任何开关影响)
        private static readonly HashSet<string> _visited = new HashSet<string>();

        internal static bool Visited(string id)
        {
            try { return !string.IsNullOrEmpty(id) && _visited.Contains(id); }
            catch { return false; }
        }

        // 旧接口保留(Guide 用它判断"原版分步教程是否在弹"): 分步教程已移除, 恒为 false
        internal static bool IntroActive { get { return false; } }

        internal static void ResetForNewCampaign()
        {
            try
            {
                _visited.Clear();
                Guide.ResetForNewCampaign();
            }
            catch { }
        }

        // 各面板[教程]按钮的统一入口 -> v4.263 改用自定义可拖动浮窗
        internal static void Page(string id)
        {
            try
            {
                if (!string.IsNullOrEmpty(id)) _visited.Add(id);
                string title = "教程 · " + TitleOf(id);
                string body = (id == "tip_picknation")
                    ? PickNationText()
                    : (Guide.SummaryText() + Guide.PageHint(id));
                if (string.IsNullOrEmpty(body)) body = "本页暂无对应挑战, 点[知道了]继续。";
                bool on = Guide.Enabled;
                TutorialWindow.Show(title, body, "挑战式引导",
                    "知道了", on ? "关闭引导" : "开启引导", "刷新",
                    delegate { TutorialWindow.HideNow(); },
                    delegate { Guide.SetEnabled(!on); Page(id); },          // 切开关后刷新同一页
                    delegate { Page(id); });                                 // 刷新
            }
            catch (Exception ex) { DLog.Info("教程失败: " + ex.Message); }
        }

        // 教程局的"选国"引导(由 Guide 在 NationPickMode 阶段弹出; 也是唯一保留的静态文案)
        // v4.265: 重排为"怎么做 / 为什么 / 接下来"三段短句, 配合浮窗的自动折行更好读
        private static string PickNationText()
        {
            return
                "你正在「蒸汽时代教程」里, 教程从选国这一步开始。\n" +
                "\n" +
                "【怎么做】\n" +
                "· 点左侧的国家卡片, 挑一个你要接管的王国\n" +
                "· 推荐体量中等、周边敌人少的国家, 上手更稳\n" +
                "· 选好后点[开始游戏]\n" +
                "\n" +
                "【为什么】\n" +
                "· 你扮演的是「国家意志」: 接管一个现成王国\n" +
                "· 你会继承它的国库、领土、军队, 以及它的麻烦\n" +
                "· 不同国家的开局难度差别很大, 建议先易后难\n" +
                "\n" +
                "【接下来】\n" +
                "· 接管完成后教程自动继续, 共 " + Guide.TotalCount + " 个挑战\n" +
                "· 挑战按顺序解锁, 完成一个才出现下一个\n" +
                "· 每个挑战都带[怎么做]与[为什么值得做]\n" +
                "· 随时可在弹窗里[关闭引导]; 之后任意页面点[教程]都能看进度";
        }

        // 弹窗标题(仅用于标题栏)
        private static string TitleOf(string id)
        {
            switch (id)
            {
                case "page_army": return "军务";
                case "page_troops": return "军队总览";
                case "page_garrison": return "驻军列表";
                case "page_armycmd": return "军团操作";
                case "page_fiscal": return "财政";
                case "page_diplo": return "外交";
                case "page_diploplay": return "外交博弈";
                case "page_guild": return "行会";
                case "page_politics": return "政治";
                case "page_pop": return "人口";
                case "page_society": return "社会";
                case "page_stats": return "统计";
                case "page_build": return "建筑";
                case "page_market": return "市场";
                case "page_nation": return "国家";
                case "page_drawer": return "定居点";
                case "page_focus": return "国策树";
                case "page_tech": return "科技";
                case "page_navy": return "海军";   // v4.289: 海上篇章(6 条 g_navy_*)所在页
                case "tip_picknation": return "第一步 · 选择你的国家";
                default: return "指引";
            }
        }
    }
}
