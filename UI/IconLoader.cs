using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using TaleWorlds.Engine.GauntletUI;

namespace FeudalInternalAffairs
{
    // 图标加载: 游戏不读散装贴图(精灵表必须打进 tpac, 我们没有打包工具),
    // 所以改为运行时注册精灵:
    //   ModuleData/FeudalIcons.bin(原始 RGBA)
    //   -> TaleWorlds.Engine.Texture.CreateFromByteArray
    //   -> EngineTexture -> TaleWorlds.TwoDimension.Texture
    //   -> SpriteFromTexture(internal, 用反射创建)
    //   -> 塞进 UIResourceManager.SpriteData.Sprites
    // 之后 prefab 里就能直接写 Sprite="fia_goods_grain"
    internal static class IconLoader
    {
        private static bool _done;
        private static int _attempts;

        internal static void Tick()
        {
            if (_done) return;
            _attempts++;
            if (_attempts > 900) { _done = true; return; }
            try
            {
                var spriteData = UIResourceManager.SpriteData;
                if (spriteData == null) return;          // UI 还没初始化, 下帧再试
                var sprites = spriteData.Sprites;
                if (sprites == null) return;
                _done = true;
                RegisterAll(sprites);
            }
            catch (Exception ex)
            {
                DLog.Force("图标加载异常: " + ex.Message);
                _done = true;
            }
        }

        private static void RegisterAll(Dictionary<string, TaleWorlds.TwoDimension.Sprite> sprites)
        {
            try
            {
                var spriteType = AccessTools.TypeByName("TaleWorlds.GauntletUI.BaseTypes.SpriteFromTexture");
                if (spriteType == null) { DLog.Force("图标: 找不到 SpriteFromTexture 类型"); return; }
                var nameField = AccessTools.Field(typeof(TaleWorlds.TwoDimension.Sprite), "<Name>k__BackingField");
                if (nameField == null) { DLog.Force("图标: 找不到 Sprite.Name 字段"); return; }

                string basePath = TaleWorlds.Library.BasePath.Name;
                string path = System.IO.Path.Combine(basePath, "Modules", "_FeudalInternalAffairs", "ModuleData", "FeudalIcons.bin");
                if (!File.Exists(path)) { DLog.Force("图标: 找不到 " + path); return; }

                int ok = 0, fail = 0;
                using (var fs = File.OpenRead(path))
                using (var br = new BinaryReader(fs))
                {
                    string magic = new string(br.ReadChars(4));
                    if (magic != "FIAI") { DLog.Force("图标: 文件头错误(" + magic + ")"); return; }
                    br.ReadInt32();                       // 版本
                    int count = br.ReadInt32();
                    for (int i = 0; i < count; i++)
                    {
                        int nameLen = br.ReadInt32();
                        string name = System.Text.Encoding.UTF8.GetString(br.ReadBytes(nameLen));
                        int w = br.ReadInt32();
                        int h = br.ReadInt32();
                        int len = br.ReadInt32();
                        byte[] rgba = br.ReadBytes(len);
                        try
                        {
                            var engTex = TaleWorlds.Engine.Texture.CreateFromByteArray(rgba, w, h);
                            if (engTex == null) { fail++; continue; }
                            engTex.Name = "fia_tex_" + name;   // 关键: 唯一命名, 否则原生层可能复用同一张纹理
                            engTex.SetTextureAsAlwaysValid();
                            engTex.PreloadTexture(true);       // 立即上传, 避免延迟上传时共用缓冲
                            var tdTex = new TaleWorlds.TwoDimension.Texture(new EngineTexture(engTex));
                            var sprite = Activator.CreateInstance(spriteType, new object[] { tdTex, w, h }) as TaleWorlds.TwoDimension.Sprite;
                            if (sprite == null) { fail++; continue; }
                            nameField.SetValue(sprite, name);
                            sprites[name] = sprite;
                            ok++;
                        }
                        catch { fail++; }
                    }
                }
                DLog.Force("图标: 已注册 " + ok + " 个精灵" + (fail > 0 ? ("(失败 " + fail + ")") : ""));
            }
            catch (Exception ex)
            {
                DLog.Force("图标注册失败: " + ex.Message);
            }
        }
    }
}
