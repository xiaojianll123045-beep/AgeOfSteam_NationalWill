# AGENTS.md — 内政与经济扩展 (FeudalInternalAffairs)

## 用户长期约定（必须遵守）
1. **版本号固定为 `0.2.0`，永远不要改**：SubModule.xml 的 `<Version value="v0.2.0"/>` 保持不变；提交/发布说明里也沿用 0.2.0，不再递增。
2. 文档唯一依据：`E:\编程\Ai\真实的骑砍mod\建筑与经济系统设计文档.md`（持续回填变更记录 v4.x）。不要在本仓库创建设计/说明类文档，除非用户明确要求。
3. 只做用户要求的事；**不要主动 git 提交/推送**，用户说"发一版/提交"才操作。
4. 文本文件一律用 UTF-8 无 BOM 写入（优先 edit/write 工具或 .NET `UTF8Encoding(false)`），禁止用 PowerShell `Set-Content`。
5. UI 约定：文字颜色必须用 `Brush.FontColor="#RRGGBBAA"`（控件 `Color=` 对文字无效）；自建面板按钮点击靠 `PanelScreen.AddSpot` 热区轮询，`Command.Click` 收不到。

## 修改后必做
- 构建：`dotnet build FeudalInternalAffairs.csproj -c Release`
- 部署：拷贝 DLL 到 `E:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\_FeudalInternalAffairs\bin\Win64_Shipping_Client\`，prefab 拷到模块 `GUI\Prefabs\`（prefab 需重启游戏才加载）。
- 模块 Id / 文件夹名保持 `_FeudalInternalAffairs` 不变（改 Id 会废存档）。
