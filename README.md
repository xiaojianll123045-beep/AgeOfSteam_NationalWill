# 内政扩展（国家意志） FeudalInternalAffairs

《骑马与砍杀 2：霸主》(Bannerlord) v1.4.8 的独立 mod。

核心设定：**你是国家意志，而不是某个领主。** 玩家没有个人部队，取而代之的是在地图上直接指挥整个国家的领主、军团与战争。

> 与旧 mod `_FeudalCalradia` 互斥，请勿同时启用（两者都改动了地图名牌预制体）。

## 主要功能

- **国家意志开局**：跳过捏脸/家族/旗帜等阶段，从 9 个国家里选一个接管（自动生成文化姓名、成为统治家族）。
- **地图指挥**
  - 左键：选中我方部队 / 点地面下达移动 / 点敌国定居点直接围城 / 点敌军发起攻击
  - 左键拖动：框选多支部队
  - 右键拖动：平移地图（1:1 跟手）
  - 中键拖动：旋转视角
- **建立军团**：多选部队 → 右键 → 建立军团 → 选军团长；成员走向军团长并保持受控，归队后并入军团
- **政治地图**：拉远视角自动显示各国领土填色与国名
- **视野**：远处敌军、本国定居点视野修复（不会"撞上空气"）
- **右下角信息栏**：跟随当前选择显示金钱/影响力/兵力等
- **国策树**：HOI4 风格，4 层结构 + 逐日推进已实现（当前 17 个国策为**临时占位内容**，后续会调整）
- **国家文化特性**：巴旦尼亚/诺德/斯特吉亚/库塞特/瓦兰迪亚/阿塞莱 各具特性
- **外交**：宣战可直接发起；和谈在敌国发起领主投票
- **取缔个人事务**：背包/部队/家族/任务/角色/捏脸/旗帜等快捷键被屏蔽（国家意志不需要处理这些）
- **禁止 AI 擅自行动**：AI 不能自行建立军团、宣战、和谈

## 安装

1. 需要游戏版本 **v1.4.8**，以及前置 mod：**Bannerlord.Harmony**、**Bannerlord.UIExtenderEx**
2. 把 `FeudalInternalAffairs` 模块文件夹放进
   `Mount & Blade II Bannerlord\Modules\`（**只需这一个文件夹**，RTS Camera 已内置）
3. 启动器里勾选 **内政扩展（国家意志）**

进战场后按 **F10** 切换上帝视角：WASD 平移、鼠标转视角、Q/E（或滚轮）升降、Shift 加速。
点"亲自指挥"进入的战斗会在开场后自动切到该视角。

## 从源码编译

```bash
dotnet build FeudalInternalAffairs.csproj -c Release
```

产物在 `bin\Win64_Shipping_Client\FeudalInternalAffairs.dll`。

若游戏不在默认路径，改 `FeudalInternalAffairs.csproj` 里的 `GameModulesDir`，或命令行覆盖：

```bash
dotnet build FeudalInternalAffairs.csproj -c Release -p:GameModulesDir="你的路径\Modules"
```

## 分支说明

- `main`：主线，由作者维护
- `yinghui`：给「赢回」个人使用的分支（其他人请勿提交到此分支）

## 贡献

欢迎提交 Pull Request：fork 本仓库 → 新建分支开发 → 发起 PR。

代码按职责分目录（`Map/` `Will/` `World/` `Focus/` `Creation/` `Core/`），Harmony 补丁逐类注册，不使用 `PatchAll`。

## 联系

- QQ：**1099155831**
- 邮箱：**qw123045@qq.com**

## 致谢

- 战场自由相机与命令系统来自 [RTS Camera](https://github.com/lzh-mb-mod/RTSCamera) 与 [MissionLibrary](https://github.com/lzh-mb-mod/MissionLibrary)（MIT，Copyright (c) 2020 Li Zhenhuan / lizhenhuan1019@qq.com）。其**源码已并入本项目** `ThirdParty/RTSCamera/`（随附 MIT LICENSE），仅做了两处适配：改为编译进本模块程序集、补了一个缺失的反射扩展方法。
- 依赖 Bannerlord.Harmony、Bannerlord.UIExtenderEx。
- 如原作者对署名方式有额外要求，或希望调整、停止再分发，请联系我们（见上方「联系」），我们会立即配合修改或下架。

## 许可证

[MIT](LICENSE)（`_bundled` 下的 RTS Camera 同样为 MIT，见其自带 LICENSE）
