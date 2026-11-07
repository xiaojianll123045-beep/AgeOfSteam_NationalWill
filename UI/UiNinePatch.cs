using System;
using System.Collections.Generic;
using TaleWorlds.TwoDimension;

namespace FeudalInternalAffairs
{
    // ==================== v4.291: UI 贴图九宫格(九宫拉伸) ====================
    //
    // 【问题】整个 UI 的边框/按钮/行底/横幅放大时是**整张贴图拉伸**, 四角被一起拉变形,
    //   而不是"只拉伸中间、四边保持原样"。
    //
    // 【根因(已实证)】
    //   1. prefab 里约 250 处用的是裸 `Sprite="fia_ui_xxx"`(直接挂在 Widget 上), 不是 Brush。
    //   2. Gauntlet 渲染裸 Sprite 时走 Widget.cs:
    //          simpleMaterial.NinePatchParameters = _sprite.NinePatchParameters;
    //          if (NinePatchLeft != 0 || ... ) simpleMaterial.NinePatchParameters = new(...);
    //      —— 也就是说裸 Sprite **只能**从 Sprite 自身取九宫格参数。
    //   3. 而喷图包 ModuleData\FeudalIcons.bin 里 16 个 fia_ui_* 的九宫格边距**全是 0**
    //      (逐条解析确认: fia_ui_frame 128x128  L=0 R=0 T=0 B=0), 于是
    //      SpriteNinePatchParameters.Empty -> 整张拉伸。
    //   (GUI\Brushes\FeudalUI.xml 里的 ExtendLeft/Top/... 只对 BrushLayer 生效,
    //    裸 Sprite 根本读不到 —— 那是另一套, 不能解决本问题。)
    //
    // 【修法】不改 prefab、不改喷图包, 而是在图标挂载时给这些 Sprite 补上九宫格参数
    //   (IconLoader 本来就用 SpriteGeneric(name, part, nine) 构造, 已支持)。
    //   边距不是猜的: 从 FeudalIcons.bin 里取每个 128x128 贴图的**实际像素**,
    //   沿中心行/列扫亮度, 找出"金边装饰带"的厚度作为边距。
    //
    // 【调参】如果某些贴图的四角仍被拉伸得不对, 直接改下表的数字后重新编译即可;
    //   日志里会打一行 "九宫格: 应用 N 个", 说明本机制已生效。
    internal static class UiNinePatch
    {
        // 名称 -> { 左, 右, 上, 下 }(像素, 基于 128x128 原图实测)
        //   frame   : 金边 + 内衬高光线, 亮带在 x=1, 内衬线在 x=9~10, 其后进入平整内面 -> 14
        //   button  : 亮边 x=0..4, 平整内面自 x≈9 起 -> 10 / 上 8
        //   row     : 亮带 x=0..6, 渐变到 x≈13 才平 -> 13 / 上 12
        //   banner  : 左右窄边 12/10; 上方有装饰横条(亮带延伸到 y≈14) -> 上 14
        //   divider : 细横线图, 只保留两端(左右 8), 上下不切(整条本来就是线)
        //   slot_ring: 圆环图标, **不能切**(切了圆环会变形), 故意不登记
        private static readonly Dictionary<string, int[]> Map = new Dictionary<string, int[]>
        {
            { "fia_ui_frame",        new[] { 14, 14, 14, 14 } },
            { "fia_ui_frame_lite",   new[] { 14, 14, 14, 14 } },
            { "fia_ui_tooltip",      new[] { 14, 14, 12, 12 } },

            { "fia_ui_button",       new[] { 10, 10,  8,  8 } },
            { "fia_ui_button_hover", new[] { 10, 10,  8,  8 } },
            { "fia_ui_button_press", new[] { 10, 10,  8,  8 } },

            { "fia_ui_row",          new[] { 13, 13, 12, 12 } },
            { "fia_ui_row_sel",      new[] { 13, 13, 12, 12 } },

            { "fia_ui_banner",       new[] { 12, 10, 14, 10 } },
            { "fia_ui_banner_red",   new[] { 12, 10, 14, 10 } },

            { "fia_ui_tab_on",       new[] { 12, 10, 12, 10 } },
            { "fia_ui_tab_off",      new[] { 12, 10, 12, 10 } },

            { "fia_ui_bar_bg",       new[] { 12, 12, 10, 10 } },
            { "fia_ui_bar_fill",     new[] { 10, 10,  8,  8 } },

            // 细横线: 只固定两端, 上下不固定(否则线会随高度变粗)
            { "fia_ui_divider",      new[] {  8,  8,  0,  0 } },
        };

        internal static int Applied;

        // 供 IconLoader 调用: 优先用喷图包自带边距(非 0 时), 否则用上表。
        internal static SpriteNinePatchParameters Resolve(string name, int w, int h,
            int ml, int mr, int mt, int mb)
        {
            try
            {
                // ① 喷图包自带边距(将来重新打包时若填了就用它, 尊重美术)
                if (ml > 0 || mr > 0 || mt > 0 || mb > 0)
                {
                    var p0 = Build(ml, mr, mt, mb, w, h);
                    if (p0.IsValid) return p0;
                }
                // ② 内置表
                int[] v;
                if (name != null && Map.TryGetValue(name, out v))
                {
                    var p1 = Build(v[0], v[1], v[2], v[3], w, h);
                    if (p1.IsValid) { Applied++; return p1; }
                }
            }
            catch { }
            return SpriteNinePatchParameters.Empty;
        }

        private static SpriteNinePatchParameters Build(int l, int r, int t, int b, int w, int h)
        {
            try
            {
                // 夹紧: 任一侧不得超过 (尺寸-1), 且左右/上下之和必须小于尺寸, 否则不切。
                int ll = Math.Max(0, Math.Min(l, w - 1));
                int rr = Math.Max(0, Math.Min(r, w - 1));
                int tt = Math.Max(0, Math.Min(t, h - 1));
                int bb = Math.Max(0, Math.Min(b, h - 1));
                if (ll + rr >= w) return SpriteNinePatchParameters.Empty;
                if (tt + bb >= h) return SpriteNinePatchParameters.Empty;
                if (ll + rr == 0 && tt + bb == 0) return SpriteNinePatchParameters.Empty;
                return new SpriteNinePatchParameters(ll, rr, tt, bb);
            }
            catch { return SpriteNinePatchParameters.Empty; }
        }
    }
}
