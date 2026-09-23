using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.Library;
using TaleWorlds.TwoDimension;

namespace FeudalInternalAffairs
{
    // 图标加载(v4.169 运行时精灵表; v4.177 后台线程读包+拼图)
    //   ModuleData/FeudalIcons.bin(FIAI v2: 名称/宽高/九宫格边距/RGBA)
    //   -> 后台线程: 读包 + 按尺寸分组 + 拼成 1~2 张大图(纯 CPU, 不碰引擎 API)
    //   -> 主线程: 建纹理 + SpriteCategory/SpritePart/SpriteGeneric 挂进 UIResourceManager.SpriteData
    // 目的: 游戏启动(主菜单前)不再被 17.8MB 读包 + 22MB 拼图卡住
    internal static class IconLoader
    {
        private class IconData
        {
            internal string Name;
            internal int W, H;
            internal byte[] Rgba;
            internal int ML, MR, MT, MB;      // v2: 九宫格边距(左 右 上 下)
        }

        private class GroupPlan
        {
            internal int Cell;
            internal int AtlasH;
            internal byte[] Bytes;
            internal List<IconData> Icons = new List<IconData>();
            internal List<int> Xs = new List<int>();
            internal List<int> Ys = new List<int>();
        }

        private class LoadResult
        {
            internal List<GroupPlan> Groups = new List<GroupPlan>();
            internal List<IconData> Big = new List<IconData>();
            internal string Err;
            internal int ReadMs, BuildMs, Count;
        }

        private const int AtlasW = 4096;
        private const int MaxSmall = 256;
        private const int PerFrame = 8;

        private static Task<LoadResult> _task;
        private static LoadResult _result;
        private static bool _loaded;         // 读包+拼图 完成
        private static bool _attached;       // 纹理/精灵已挂载
        private static bool _atlasOk;
        private static int _attempts;
        private static int _next;
        private static object _cat;
        private static string _catName = "fia_runtime";
        private static readonly List<string> _names = new List<string>();
        private static readonly List<Sprite> _sprites = new List<Sprite>();
        private static readonly List<SpritePart> _parts = new List<SpritePart>();
        private static readonly List<KeyValuePair<string, Sprite>> _big = new List<KeyValuePair<string, Sprite>>();
        private static bool _rebuildLogged;
        private static List<IconData> _flat;

        internal static void Tick()
        {
            try
            {
                var sd = UIResourceManager.SpriteData;
                if (sd == null || sd.Sprites == null) return;      // UI 还没初始化

                // ① 后台线程: 读包 + 分组 + 拼图(纯 CPU)
                if (_task == null && !_loaded)
                {
                    _task = Task.Run(delegate { return LoadAndBuild(); });
                    return;
                }
                if (_task != null && !_loaded)
                {
                    if (!_task.IsCompleted) return;                // 还没算完, 下一帧再看
                    _loaded = true;
                    try { _result = _task.Result; } catch (Exception ex) { _result = new LoadResult { Err = ex.Message }; }
                    if (_result == null || _result.Err != null)
                        DLog.Force("图标: 后台加载失败 " + (_result != null ? _result.Err : "null"));
                }

                // ② 主线程: 建纹理 + 注册精灵(只做一次)
                if (!_attached)
                {
                    _attached = true;
                    if (_result != null && _result.Groups.Count > 0)
                    {
                        var sw = System.Diagnostics.Stopwatch.StartNew();
                        _atlasOk = AttachResult(sd, _result);
                        sw.Stop();
                        if (_atlasOk)
                            DLog.Force("图标: 就绪 " + _names.Count + " 个(后台读包 " + _result.ReadMs + "ms/拼图 "
                                + _result.BuildMs + "ms; 主线程建纹理 " + sw.ElapsedMilliseconds + "ms)");
                        else
                            DLog.Force("图标: 精灵表失败, 退回逐张注册");
                    }
                    else
                    {
                        _atlasOk = false;
                        DLog.Force("图标: 无可用数据, 退回逐张注册");
                    }
                }

                if (_atlasOk)
                {
                    // ③ 守护: SpriteData 被游戏重建后重新挂上(复用对象, 不新建纹理)
                    if (_names.Count > 0 && !Present(sd, _names[0]))
                    {
                        AttachAll(sd);
                        if (!_rebuildLogged)
                        {
                            _rebuildLogged = true;
                            DLog.Force("图标: SpriteData 重建, 已重新挂载 " + _names.Count + " 个");
                        }
                    }
                    return;
                }

                // ④ 兜底: 逐张 SpriteFromTexture(分批)
                if (_flat == null) return;
                if (_next < _flat.Count)
                {
                    int ok = 0;
                    for (int i = 0; i < PerFrame && _next < _flat.Count; i++, _next++)
                        if (RegisterOne(sd.Sprites, _flat[_next])) ok++;
                    if (_next >= _flat.Count)
                        DLog.Force("图标: 兜底注册完成 " + _flat.Count + " 个(本帧 " + ok + ")");
                    return;
                }
                if (!Present(sd, "fia_cat_admin")) { _next = 0; }
            }
            catch (Exception ex)
            {
                DLog.Force("图标加载异常: " + ex.Message);
                _loaded = true;
            }
        }

        // ===== 后台线程: 读包 + 分组 + 拼图(不调用任何引擎 API) =====
        private static LoadResult LoadAndBuild()
        {
            var res = new LoadResult();
            try
            {
                string basePath = TaleWorlds.Library.BasePath.Name;
                string path = Path.Combine(basePath, "Modules", "AgeOfSteam_NationalWill", "ModuleData", "FeudalIcons.bin");
                if (!File.Exists(path)) { res.Err = "找不到 " + path; return res; }

                var list = new List<IconData>();
                var swRead = System.Diagnostics.Stopwatch.StartNew();
                int ver = 0;
                using (var fs = File.OpenRead(path))
                using (var br = new System.IO.BinaryReader(fs))
                {
                    string magic = new string(br.ReadChars(4));
                    if (magic != "FIAI") { res.Err = "文件头错误(" + magic + ")"; return res; }
                    ver = br.ReadInt32();
                    int count = br.ReadInt32();
                    for (int i = 0; i < count; i++)
                    {
                        int nameLen = br.ReadInt32();
                        string name = System.Text.Encoding.UTF8.GetString(br.ReadBytes(nameLen));
                        int w = br.ReadInt32();
                        int h = br.ReadInt32();
                        int len = br.ReadInt32();
                        int ml = 0, mr = 0, mt = 0, mb = 0;
                        if (ver >= 2) { ml = br.ReadInt32(); mr = br.ReadInt32(); mt = br.ReadInt32(); mb = br.ReadInt32(); }
                        byte[] rgba = br.ReadBytes(len);
                        list.Add(new IconData { Name = name, W = w, H = h, Rgba = rgba, ML = ml, MR = mr, MT = mt, MB = mb });
                    }
                }
                swRead.Stop();
                res.ReadMs = (int)swRead.ElapsedMilliseconds;
                res.Count = list.Count;
                _flat = list;

                var swBuild = System.Diagnostics.Stopwatch.StartNew();
                var groups = new List<IconData>[2];
                for (int i = 0; i < 2; i++) groups[i] = new List<IconData>();
                for (int i = 0; i < list.Count; i++)
                {
                    var d = list[i];
                    int mx = Math.Max(d.W, d.H);
                    if (mx <= 128) groups[0].Add(d);
                    else if (mx <= MaxSmall) groups[1].Add(d);
                    else res.Big.Add(d);
                }
                groups[0].Add(MakeTechPlaceholder());     // 占位徽章(缺图兜底)

                for (int g = 0; g < 2; g++)
                {
                    if (groups[g].Count == 0) continue;
                    var plan = BuildGroupBytes(groups[g], g == 0 ? 128 : 256);
                    if (plan != null) res.Groups.Add(plan);
                }
                swBuild.Stop();
                res.BuildMs = (int)swBuild.ElapsedMilliseconds;
            }
            catch (Exception ex) { res.Err = ex.Message; }
            return res;
        }

        // 拼图(纯 CPU, 可在后台线程跑; 每张图写自己的矩形, 无竞争)
        private static GroupPlan BuildGroupBytes(List<IconData> list, int cell)
        {
            var plan = new GroupPlan();
            plan.Cell = cell;
            int grid = Math.Max(1, AtlasW / cell);
            int rows = (list.Count + grid - 1) / grid;
            plan.AtlasH = ((rows * cell + 63) / 64) * 64;
            plan.Bytes = new byte[AtlasW * plan.AtlasH * 4];
            plan.Icons.AddRange(list);
            for (int i = 0; i < list.Count; i++)
            {
                var d = list[i];
                plan.Xs.Add(i % grid * cell + (cell - d.W) / 2);
                plan.Ys.Add(i / grid * cell + (cell - d.H) / 2);
            }
            var bytes = plan.Bytes;
            try
            {
                Parallel.For(0, list.Count, delegate (int i)
                {
                    var d = list[i];
                    int ox = plan.Xs[i], oy = plan.Ys[i];
                    for (int y = 0; y < d.H; y++)
                    {
                        int src = y * d.W * 4;
                        int dst = ((oy + y) * AtlasW + ox) * 4;
                        Buffer.BlockCopy(d.Rgba, src, bytes, dst, d.W * 4);
                    }
                });
            }
            catch
            {
                for (int i = 0; i < list.Count; i++)      // 退化: 单线程
                {
                    var d = list[i];
                    int ox = plan.Xs[i], oy = plan.Ys[i];
                    for (int y = 0; y < d.H; y++)
                        Buffer.BlockCopy(d.Rgba, y * d.W * 4, bytes, ((oy + y) * AtlasW + ox) * 4, d.W * 4);
                }
            }
            return plan;
        }

        // 程序生成: 科技占位徽章(圆形金环 + 绿色圆盘), 128×128 RGBA
        private static IconData MakeTechPlaceholder()
        {
            const int S = 128;
            var px = new byte[S * S * 4];
            const float c = (S - 1) / 2f;
            const float rDisc = 50f, rDiscEdge = 52f, rRingEdge = 63f;
            for (int y = 0; y < S; y++)
            {
                for (int x = 0; x < S; x++)
                {
                    float dx = x - c, dy = y - c;
                    float d = (float)Math.Sqrt(dx * dx + dy * dy);
                    byte r = 0, g = 0, b = 0, a = 0;
                    if (d <= rDiscEdge)
                    {
                        float t = d / rDisc;
                        if (t > 1f) t = 1f;
                        r = (byte)(0x24 + (0x4C - 0x24) * (1f - t));
                        g = (byte)(0x63 + (0xAF - 0x63) * (1f - t));
                        b = (byte)(0x28 + (0x50 - 0x28) * (1f - t));
                        a = (byte)(255f * Math.Min(1f, (rDiscEdge - d) / 1.5f + 0.5f));
                    }
                    else if (d <= rRingEdge)
                    {
                        r = 0xC9; g = 0xA2; b = 0x27;
                        a = (byte)(255f * Math.Min(1f, (rRingEdge - d) / 1.5f + 0.5f));
                    }
                    if (a > 0)
                    {
                        int o = (y * S + x) * 4;
                        px[o] = r; px[o + 1] = g; px[o + 2] = b; px[o + 3] = a;
                    }
                }
            }
            return new IconData { Name = "fia_tech_ph", W = S, H = S, Rgba = px };
        }

        private static bool Present(SpriteData sd, string name)
        {
            try
            {
                Sprite s;
                return sd.Sprites.TryGetValue(name, out s) && s != null;
            }
            catch { return false; }
        }

        // ===== 主线程: 建纹理 + 注册精灵 =====
        private static bool AttachResult(SpriteData sd, LoadResult res)
        {
            try
            {
                for (int g = 0; g < res.Groups.Count; g++)
                {
                    if (!AttachGroup(res.Groups[g])) return false;
                }

                // 超大图: 单张纹理(内部类型, 用反射建)
                var spriteType = AccessTools.TypeByName("TaleWorlds.GauntletUI.BaseTypes.SpriteFromTexture");
                var nameField = AccessTools.Field(typeof(Sprite), "<Name>k__BackingField");
                for (int i = 0; i < res.Big.Count; i++)
                {
                    var d = res.Big[i];
                    var eng = TaleWorlds.Engine.Texture.CreateFromByteArray(d.Rgba, d.W, d.H);
                    if (eng == null || spriteType == null) continue;
                    eng.Name = "fia_tex_" + d.Name;
                    eng.SetTextureAsAlwaysValid();
                    eng.PreloadTexture(true);
                    var sp = Activator.CreateInstance(spriteType, new object[] { new Texture(new EngineTexture(eng)), d.W, d.H }) as Sprite;
                    if (sp == null) continue;
                    if (nameField != null) nameField.SetValue(sp, d.Name);
                    _big.Add(new KeyValuePair<string, Sprite>(d.Name, sp));
                }

                AttachAll(sd);
                return _names.Count > 0;
            }
            catch (Exception ex)
            {
                DLog.Force("图标: 精灵表构建异常: " + ex.Message);
                return false;
            }
        }

        private static bool AttachGroup(GroupPlan plan)
        {
            var engTex = TaleWorlds.Engine.Texture.CreateFromByteArray(plan.Bytes, AtlasW, plan.AtlasH);
            if (engTex == null) { DLog.Force("图标: 精灵表纹理创建失败(" + plan.Icons.Count + ")"); return false; }
            engTex.Name = "fia_atlas_tex_" + plan.Cell;
            engTex.SetTextureAsAlwaysValid();
            engTex.PreloadTexture(true);
            var tdTex = new Texture(new EngineTexture(engTex));

            var cat = new SpriteCategory(_catName + "_" + plan.Cell, 1, true);
            cat.SheetSizes = new Vec2i[] { new Vec2i(AtlasW, plan.AtlasH) };
            cat.SpriteSheets.Add(tdTex);
            try
            {
                var f = typeof(SpriteCategory).GetField("SpriteSheetSize", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (f != null) f.SetValue(cat, AtlasW);
                var fl = typeof(SpriteCategory).GetField("<IsLoaded>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
                if (fl != null) fl.SetValue(cat, true);
            }
            catch { }
            if (_cat == null) _cat = cat;

            for (int i = 0; i < plan.Icons.Count; i++)
            {
                var d = plan.Icons[i];
                var part = new SpritePart(d.Name, cat, d.W, d.H);
                part.SheetID = 1;
                part.SheetX = plan.Xs[i];
                part.SheetY = plan.Ys[i];
                part.UpdateInitValues();
                var nine = SpriteNinePatchParameters.Empty;
                if (d.ML > 0 || d.MR > 0 || d.MT > 0 || d.MB > 0)
                {
                    int ml = Math.Min(d.ML, d.W - 2), mr = Math.Min(d.MR, d.W - 2);
                    int mt = Math.Min(d.MT, d.H - 2), mb = Math.Min(d.MB, d.H - 2);
                    if (ml + mr < d.W && mt + mb < d.H)
                        nine = new SpriteNinePatchParameters(ml, mr, mt, mb);
                }
                var sp = new SpriteGeneric(d.Name, part, nine);
                _names.Add(d.Name);
                _sprites.Add(sp);
                _parts.Add(part);
            }
            DLog.Force("图标: 精灵表 " + plan.Cell + "px 组 " + plan.Icons.Count + " 个");
            return true;
        }

        private static void AttachAll(SpriteData sd)
        {
            try
            {
                var dict = sd.Sprites;
                for (int i = 0; i < _names.Count; i++) dict[_names[i]] = _sprites[i];
                try
                {
                    var pd = sd.SpriteParts;
                    if (pd != null) for (int i = 0; i < _names.Count; i++) pd[_names[i]] = _parts[i];
                }
                catch { }
                for (int i = 0; i < _big.Count; i++) dict[_big[i].Key] = _big[i].Value;
            }
            catch (Exception ex) { DLog.Force("图标挂载失败: " + ex.Message); }
        }

        private static bool RegisterOne(Dictionary<string, Sprite> sprites, IconData d)
        {
            try
            {
                var spriteType = AccessTools.TypeByName("TaleWorlds.GauntletUI.BaseTypes.SpriteFromTexture");
                if (spriteType == null) { DLog.Force("图标: 找不到 SpriteFromTexture 类型"); return false; }
                var nameField = AccessTools.Field(typeof(Sprite), "<Name>k__BackingField");
                if (nameField == null) { DLog.Force("图标: 找不到 Sprite.Name 字段"); return false; }

                var engTex = TaleWorlds.Engine.Texture.CreateFromByteArray(d.Rgba, d.W, d.H);
                if (engTex == null) return false;
                engTex.Name = "fia_tex_" + d.Name;
                engTex.SetTextureAsAlwaysValid();
                engTex.PreloadTexture(true);
                var tdTex = new Texture(new EngineTexture(engTex));
                var sprite = Activator.CreateInstance(spriteType, new object[] { tdTex, d.W, d.H }) as Sprite;
                if (sprite == null) return false;
                nameField.SetValue(sprite, d.Name);
                sprites[d.Name] = sprite;
                return true;
            }
            catch { return false; }
        }
    }
}
