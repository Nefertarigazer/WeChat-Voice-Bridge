# 微信语音桥 · 当前交接（2026-11-12，源码与线上均为 v1.8.0；GitHub 仓库已公开）

> **新模型第一入口是 [PROJECT-BRIEF.md](PROJECT-BRIEF.md)**：自包含的项目总览、环境命令、发布纪律、不可回退的红线、当前任务与准绳。本文是细节交接，逐版本流水见 [WORKLOG.md](WORKLOG.md)，当日执行记录在本机的 `.workbuddy/memory/`（2026-11-12 起不入库）。
> 旧的 `NEXT-AGENT-HANDOVER-1.4.md` 是 1.4.0 时期的历史快照，`PROJECT-HANDOVER.md` 更早（1.2），都不要当作现状。
> 用户可见的版本说明在 `src/VoiceBridge.App/ReleaseHistory.cs`。

## 00. 任务一 · 多厂商语音 API 融合（2026-11-11，已完成；1.6.0 已发布上线）

- **一句话现状**：源码与线上同为 **1.6.0**（源码 commit `f279d5e`，payload `1.6.0+f279d5e…`）。Muse 发布单 `codex_20261111_111550_686f2b` 先 `info` 后 `ok`（`publishedAt=2026-11-11T10:19:18Z`，哈希链三方一致，旧清单备份 `latest.json.bak-1.5.3`）。本助手已独立公网复核：清单 200、六字段齐全、notes 321 字中文正常；ZIP / Setup HEAD 200，长度 92,137,109 / 67,090,008；完整下载 ZIP 重算 SHA-256 与清单一致。桌面快捷方式未动（仍指向 1.4.7 r3）。
- **1.6.0 产物**：ZIP `artifacts\WeChat-Voice-Bridge-V1.6.0-win-x64.zip` = 92,137,109 字节 / `f58cdf57605d692e15619b87584c0da03ed92ed4b6ea1cd7e553e2d0c44a6d7d`；Setup `artifacts\WeChat-Voice-Bridge-V1.6.0-Setup.exe` = 67,090,008 字节 / `d96a0cff6b4d74202704723dbdf91555bd289ae13b8d7a3a6c5cf66eaf5f1378`；payload `artifacts\win-x64-v1.6.0\`（1993 文件）。
- **发布闭环已完成**：文档提交 `7dfa96c` 已推送，Muse 已回确认（`codex_20261111_113411_4a62b1`，`ok`，无进一步动作）。
- **任务二（隐私模式 / 提醒模式）：已随 1.7.0 上线**。准绳是 [privacy-reminder.md](privacy-reminder.md)（定稿需求 §1「补充三」「补充四」，界面 §8「第三版修订」），逐节记录在 WORKLOG「任务二 · 节 1–9」与「1.7.0 · 节 10–12」。
  - **1.7.0 上线**：Muse 发布单 `codex_20261111_211423_9ceb57`（21:14 发出，21:35 才被领走，21:37 回 `ok`，这次没有先回 `info`），`publishedAt=2026-11-11T20:37:01Z`，哈希链三方一致，旧清单备份 `latest.json.bak-1.6.0`。本助手独立公网复核：清单 200、六字段齐全、notes 177 字中文正常；ZIP / Setup HEAD 200，长度 92,186,434 / 67,123,954；完整下载 ZIP 重算 SHA-256 与清单一致，临时下载已删。桌面快捷方式未动（仍指向 1.4.7 r3）。
  - **发布闭环已完成**：文档提交 `fbb856e` 已推送，Muse 已回确认（`codex_20261111_213907_514b37`，`ok`，Muse 侧独立复核一致，无进一步动作）。
- **第三轮（2026-11-12 起，用户说这是最后一轮）**，按顺序：
  1. 节 13 **已完成**：更新说明底部渐隐、暗语留空也能完成、更新状态点回到绿色（详见 WORKLOG）。
  2. 节 14 **已完成**：使用说明重做。快速开始三步，附版本表（取自 `WeChatSession`）和服务表（取自 `ProviderCatalog.All`）；其余是常用功能卡片、四问四答、隐私说明和更新日志（默认 4 个版本）。`docs/user-guide.md` 同步。**改说明文字时，软件里的 `GuideContent` 和 `docs/user-guide.md` 一起改。**
  3. 节 15 **已完成**：「设置 → 软件更新」下面新增「外观」。第一层浅色 / 深色 / 跟随系统，第二层主题色默认 / 浅绿 / 浅粉 / 浅蓝；默认是浅色 + 默认（新的中性主题，苹果式浅灰底 + 系统蓝），浅绿是 1.8 之前的样子。存在 `settings.json` 的 `Appearance`，启动时在第一个窗口出现前应用。
     - **颜色约定（节 15 起）**：所有颜色都是 `Theme.cs` 生成的应用资源，XAML 一律用 `DynamicResource`；代码里上色用 `Theme.Brush/ColorOf` 或 `SetResourceReference`，长期存在的控件在 `Theme.Changed` 时重画。**不要再写死颜色**，也不要给模板里的渐变色标做绑定（找不到模板父级，色块会变空白）。警示色和隐私 / 提醒的颜色固定，不随主题色变。新加颜色就在 `ThemePalette.Build` 里加一个键。
     - `tools/DialogPreview` 必须先套主题（`--theme Dark/Pink` 这样指定，默认浅色 + 默认），因为新键不在 `App.xaml` 里。
  4. 节 16 **已完成**：`README.md` 重写（定位、功能、下载、三步开始、语音服务、工作原理、隐私与安全、构建、免责声明、致谢、许可、English）。截图 `docs/images/` 用的是演示数据，不含用户的联系人。README 里的下载链接写的是 1.8.0，**以后每次发版要跟着改**。
  5. 升版本（1.8.0）、打包、经 Muse 发布；清理历史后公开 GitHub 仓库；推送 README。**全部已完成**（节 17–23）。
  - 节 17 **已完成**：源码版本 **1.8.0**（`Directory.Build.props`），`ReleaseHistory.cs` 有 1.8.0 条目。
  - 节 18 **已完成**：1.8.0 已打包（源码 `5c3ad2e`，payload `1.8.0+5c3ad2e896f063c2cdc09c9617232f0ecbc97caa`）。ZIP `artifacts\WeChat-Voice-Bridge-V1.8.0-win-x64.zip` = 92,188,901 字节 / `3548a75646c6fc05bef293426e43b3cfc444681e1eb0a0e5422ac8776d55bb10`；Setup `artifacts\WeChat-Voice-Bridge-V1.8.0-Setup.exe` = 67,120,906 字节 / `de8532a06f1015e94ea6f1d7255694b8ffa922abc5cd3b19278d003c857cb551`；payload `artifacts\win-x64-v1.8.0\`（1993 文件）。
  - 节 19 **已完成**：**1.8.0 已上线**。Muse 发布单 `codex_20261112_020446_0bf7a9` 先 `info` 后 `ok`，`publishedAt=2026-11-12T01:07:29Z`，旧清单备份 `latest.json.bak-1.7.0`。本助手独立公网复核：清单 200、六字段齐全、notes 164 字正常；ZIP / Setup HEAD 200 且长度一致；完整下载 ZIP 重算 SHA-256 一致。桌面快捷方式未动。
  - **1.8.0 发布闭环已完成**：回 Muse 的确认 `codex_20261112_021009_019491`（带记录提交 `b93682f`）收到 `ok`，无进一步动作。
  - 节 20 **已完成**：改公开前的历史审计（详见 WORKLOG 节 20）。真实 Key 在整个历史里 0 处；但历史里有用户和一位联系人的 wxid / 微信号、一张真实名单截图、本机路径、服务器 IP 和内部文件。
  - **用户已拍板**：A（清理历史后公开）+ Apache-2.0。节 21 **已完成**：`LICENSE`、README 许可一节、内部文件移出仓库（文件仍在本机，已加进 `.gitignore`）。
  - 节 22 **已完成**：历史已在 OneDrive 外的干净克隆里改写并复查（详见 WORKLOG 节 22）。**这份历史就是公开仓库的历史**，提交号和 2026-11-12 之前的记录对不上：要查旧提交，去私有备份仓库或 bundle。
    - 原始历史完整备份：`D:\USERNAME\CodexScratch\wvb-rewrite\original-history.bundle`（`git bundle --all`），新旧提交号对照 `wvb-rewrite\commit-map.txt`；GitHub 上另有改名后的私有仓库（见下）。
    - 改写用的克隆：`D:\USERNAME\CodexScratch\wvb-rewrite\repo`，工具 git-filter-repo 2.47.0 装在 `wvb-rewrite\gfr`。替换清单 `wvb-rewrite\replacements.txt` 含个人信息，**不要提交进仓库**。
    - 文档里的 `USERNAME` 是本机用户名的占位，`服务器IP`、`wxid_本人账号`、`wxid_联系人`、`（联系人昵称）`、`（联系人微信号）` 也是占位。**公开仓库里不要再写真实的用户名、wxid、微信号或服务器 IP。**
    - 提交作者已统一成 `Nefertarigazer <97020488+Nefertarigazer@users.noreply.github.com>`。Muse 已把本机项目的 git 身份（项目级 `user.name` / `user.email`）改成这个匿名地址，新提交不会再带真实姓名和邮箱；在别的机器或别的克隆里提交时，也要用这个身份。
  - 节 23 **已完成：仓库已公开**（详见 WORKLOG 节 23）。
    - 公开仓库：<https://github.com/Nefertarigazer/WeChat-Voice-Bridge>（公开，默认分支 `master`，许可证识别为 Apache-2.0）。原始历史在私有仓库 `Nefertarigazer/WeChat-Voice-Bridge-private-history`（114 个提交，另有 `clean-history` 分支），**不要公开、不要删除**。
    - 本机这个仓库已切到新历史，`origin` 地址不变，`master` 与 `origin/master` 同点。已忽略的内部文件留在本机。本机还有别的工具留下的 `refs/codex/…`、`refs/t3/…` 检查点引用，指向旧历史，**只推 `master` 和标签，不要用 `--mirror` 或 `--all` 推送**。
    - 有旧历史的克隆（改写之前的任何副本）都不要再往公开仓库推，否则清理掉的内容会被带回来；要用就重新克隆。
  - **公开闭环已完成**：回 Muse 的单 `codex_20261112_041602_92c33e`（带记录提交 `acb0f7f`）收到 `ok`，Muse 独立公网复核全部通过；GitHub 话题已补上（9 个）。第三轮完成。
- **1.8.1（2026-11-12，进行中）**：用户反馈深色模式下联系人卡片的昵称看不清。
  - 节 24 **已完成**：`ContactName` 样式加了正文色（显式样式不叠加隐式 TextBlock 样式，原来退回了黑色）；README 的 `home.png`、`themes.png` 用演示数据重截。**以后新建 TextBlock 的显式样式，一定要写 `Foreground`，或者 `BasedOn` 隐式样式。**
  - 节 25 **已完成**：源码版本 **1.8.1**，`ReleaseHistory.cs` 有 1.8.1 条目（含对 1.8.0 外观的简要提示，因为弹窗只显示当前版本）。
  - **续跑点**：节 26 门禁打包（`wvb-package.ps1 -Version 1.8.1`）；节 27 发 Muse（`wvb-muse-publish.ps1 -Version 1.8.1`）、公网复核；之后把 README 的版本徽章和两个下载链接改成 1.8.1 并推送。
  - **以后发版**：除了原来的流程，还要把 README 里两个下载链接的版本号改成新版本。
  - **用户自己那份**是 `artifacts\win-x64-v1.5.1`，已在 2026-11-11 23:40 用软件内更新升到 1.7.0。开发版目录（`bin\Release\net8.0-windows`）现在没人占用，可以编译和实机巡检；巡检前照 WORKLOG 节 15 的做法备份并在结束后复原 5 个设置文件。
- **界面检查**：`tools/DialogPreview` 加了 `--realclick`（真实鼠标点击，鼠标不动，确认最上层是自己的窗口才点）。
  - **还要用户实测的**：开始监听后，对方发消息时「✓ 已读」与角标出现、提醒按时播报（先响提醒音）；手机上回一条后提醒停下、角标消失；开隐私后朗读与记录都不出现内容；「选择本机音频」打开文件框（离屏没测）。
  - **1.7.0 产物**（源码 commit `dd263df`，payload ProductVersion `1.7.0+dd263dfdf21e0e153709129a886289f74a2aa6b8`）：ZIP `artifacts\WeChat-Voice-Bridge-V1.7.0-win-x64.zip` = 92,186,434 字节 / `2a0940984d95bc72fb05ca4f69ad3fa9e9a422fe75121832db171b8af166029a`；Setup `artifacts\WeChat-Voice-Bridge-V1.7.0-Setup.exe` = 67,123,954 字节 / `763909e1c9814277112b945a627b042f19fbd31dbc09ed7c9979ca1b7dac5114`；payload `artifacts\win-x64-v1.7.0\`（1993 文件，Clean-payload PASS）。
  - **界面检查工具** `tools/DialogPreview`（Debug 编译）：屏幕外打开真实的更新说明与设置卡片，自动检查半行、滚动条、试听/取消并存截图。**不要在工具里构造 `App`**，它会触发 `OnStartup`、敲正在运行的那份应用。
  - **更新说明约定**：底部不要再加渐隐（会洗掉露出的半行）；滚动条透明度只由代码控制，不要在模板里加回 `IsMouseOver` 触发器。
  - **开发版目录被占用时**（用户在跑 `bin\Release\net8.0-windows` 那份）：不要结束它，应用改用 `-r win-x64` 编译，`Test.ps1` 第一步会编不过，用同样步骤、应用加 `-r win-x64` 代替；实机巡检做不了（单实例，第二份会直接唤醒用户那份）。2026-11-12 起没有被占用。
  - worker：`wechat_adapter.py` 的回复游标与 `take_replies()`，`main.py` 在 `message` / `heartbeat` 里带 `replies`。
  - Core：`Attention.cs`（设置与播报句）、`ReplyTracker.cs`（待回复与计时），以及 `SessionController` 的 `Replied` / `Received` / `Finished` / `Merge` / `Announce`。
  - App：`MainWindow.Attention.cs`（接线、定时器、提醒音）、`ModeCapsule.cs`（多人胶囊）、`ModeCard.cs`（单人宽卡）、`MainWindow.Morph.cs`（单人 ↔ 多人变形）、`ModeSettingsDialog.cs`（设置卡片）、使用说明第 10 节。
  - 工具：`tools/probes/outgoing_probe.py`（只读探针）、`tools/CapsulePreview`（离屏渲染胶囊 / 宽卡）、`tools/UiSmokeTest` 的 `print` 与 `task2` 模式（`task2` 会改设置，必须先备份）。
- **用户实测清单**：
  - 开始监听后，让对方发消息，看「✓ 已读」与角标出现，提醒按 3/6/9… 分钟播报（先响提醒音）。
  - 在手机上回一条，提醒应停下、角标消失。
  - 开隐私后，朗读与记录都不出现内容。
  - 二变一、一变二时，看变形动画。
- **实机截图守则**：开发版第一次启动会弹「已更新到 v1.6.0」。用 `tools/UiSmokeTest print 0 <png>` 截主窗口（PrintWindow），截完 `Stop-Process`，**不要点掉弹窗**。如果 `%LOCALAPPDATA%\WeChatVoiceBridge\release-notes-seen.json` 被写成 1.6.0，改回 `"1.5.3"`，用户自己打开时才看得到更新说明。
- **跑适配器测试**：`Test.ps1` 会跳过需要 SQLCipher 的 42 个 Python 测试；改了 `wechat_adapter.py` 时，用 `src\VoiceBridge.App\bin\Release\net8.0-windows\runtime\python\python.exe -B -m unittest discover -s tests\python -p "test_live_adapter.py"`（以及 `test_multi_contact.py`）单独跑。
- **工具坑**：本机 PowerShell 工具对长等待偶尔提前返回 exit -1 且无输出，后台进程仍会跑完。等 Muse 回执时不要重发，看 `muse-bridge\outbox\<req_id>.reply.json`。
- **准绳文档**：[tts-integration.md](tts-integration.md)（调研结论、设计、8 节实施计划）。逐节进度看 WORKLOG 顶部「任务一」段。开发者说明见 [providers.md](providers.md)。
- **存档点**：标签 `v1.5.3`（`67a74f0`）与 `pre-multi-provider`（`d608aca`）已推送；任何时候都能回到动工前。
- **已完成**：节 1（调研 + 设计文档）；节 2（厂商身份改为服务身份 `ServiceIdentity`、音色固定模型、模型切换推广到所有协议、迁移与测试，.NET 216/216）。
- **关键约定（节 2 起）**：判断「是不是同一家」一律用 `ServiceIdentity.Of/Same`，**不要再用 `Kind ==`**；读写当前模型一律用 `SpeechModels.Current/Use/Listed`（豆包的模型是资源 ID）。`SpeechProviderKind` 只能在末尾追加。
- **节 3 已完成**：`src/VoiceBridge.Core/ProviderCatalog.cs`（19 个预设：13 主流 + 2 通用 + 4 本地），.NET 227/227。界面还没接上目录（节 5）。
- **节 4 已完成**：工厂支持 ElevenLabs / MiniMax / 阿里百炼 / Gemini / 自定义 HTTP，Fish 扩到 4 个模型并允许本机 fish-speech，.NET 250/250。
- **节 5 已完成**：Edge 免费朗读复用 edge-tts 7.3.1（常驻子进程 `worker/edge_speak.py` + `EdgeSpeechHost`），`Publish.ps1` 锁定 14 个轮子到 `worker\tts-deps`；真实联网测试通过。.NET 253/253。
- **开发版前提**：`bin\Release\net8.0-windows\` 下除了 `runtime\`、`worker\deps\`，现在还需要 `worker\tts-deps\`（已放好）。
- **节 6 已完成**：添加服务对话框按目录分三组、字段按预设生成；添加音色给常用音色与「固定模型」。截图见 WORKLOG 节 6。
- **界面约定（节 6 起）**：对话框里的下拉框模板不支持 `IsEditable`（`App.xaml` 的 ComboBox 模板没有编辑框），需要「可选可填」时用「文本框 + 常用项下拉」的组合，不要直接设 `IsEditable`。
- **节 7 已完成**：使用说明 03/04 重写，新增 05 主流厂商逐家、06 通用协议、07 本地模型（后续顺延 08–16）；`docs/providers.md` 重写为开发者说明（含「加一家新厂商」步骤）。**改目录（`ProviderCatalog`）时同步改教程第 05–07 节。**
- **用户检查后的打磨（已完成）**：对话框去滚动条改用边缘渐隐、分组改为「通用协议 / 主流厂商 / 自建模型」、「高级」改用 `Disclosure`（平滑开合 + 对话框锁上沿生长）、需海外网络的厂商带琥珀色地球胶囊（`ProviderPreset.Overseas`：OpenAI、Gemini、ElevenLabs、Fish Audio）。页面过长时底部让下一行「露头」（`PeekNextRow`，48%）；首页空记录时清空按钮显示禁止光标（窗口级 `QueryCursor` 的 `OverIdleClear`）。**不要再用 WPF 默认 Expander 做对话框里的折叠区**，也不要给对话框滚动区加回滚动条。
- **节 8 已完成**：全量回归通过；开发版用真实 Fish Key 试听成功（迁移后 `CredentialId` 与 `FishKeyName` 不变）；`Publish.ps1` 发到临时目录验证通过（edge-tts 冒烟检查 + 出厂门禁 PASS，验证后已删除临时目录）。
- **约束**：不打包、不上线、不改桌面快捷方式；保留三条红线；任务二（隐私模式 / 提醒模式）等任务一稳定后再做。
- **环境**：SDK 实际在 `%LOCALAPPDATA%\Temp\wvb-sdk\sdk-tmp\dotnet.exe`（文档里旧写法 `wvb-sdk\sdk\dotnet.exe` 已不存在）。

## 0. 当前状态（2026-11-10 · v1.5.2 已打包并发布上线）

**当前一节（v1.5.2，源码完成、已发布）**：
- 提示行不再挤动布局：四个提示位（首页 `NoticeText` / 音效 `VoiceLibraryNotice` / 高级 `ApiNotice` / 词库 `ReadingNotice`）改为**固定 20 DIP 高、常驻可见**的留白行，文字填入不改高度；各页标题下边距等量减免，所以无提示时排版与之前一致。**不要再对这四个槽切 `Visibility`**，否则预留高度丢失、页面又会被推一下。实测 shift：修前 39 px → 现在 0 px。
- 提示本身：只在该页显示、5 秒自动消失、切页即清；删掉启动时的「已自动找到微信数据目录。」。
- 版本 **1.5.2**（`Directory.Build.props`），`ReleaseHistory` 已有 1.5.2 条目（状态点颜色语义、呼吸灯、字号层级与基线、右键菜单、提示行行为、去噪音）。
- 验证：`Test.ps1` **.NET 209/209 PASS + Python 54 PASS（41 skipped 基线）**、`Verify-Source` PASS、Clean-payload PASS（1733 文件）。

**工作区未发布改动（1.5.2 之后，未升版本号）**：
- 提示是**窗口内底部居中的字幕条** `HintToast`（`Grid.ColumnSpan=2`、`VerticalAlignment=Bottom`、圆角 12、`#F8FFFFFF` 底）：同一时刻只有一条，5 秒淡出，切页即关。**不要改回 Popup**——独立透明窗口会露出圆角外的矩形，而且不跟随窗口移动/最小化。四个页级提示 TextBlock 已删除，边距已还原；词库提示走同一个字幕条。
- 音色下拉框：`VoiceChoiceItemStyle` 补 `BasedOn="{StaticResource {x:Type ComboBoxItem}}"`（显式 ItemContainerStyle 会覆盖隐式样式，这就是它“和别人不一样”的原因）；`ComboBoxItem` 样式加 `FocusVisualStyle=null` 去掉虚线框。分厂商分组与失效音色语义未改。
- 右键菜单：新增 `src/VoiceBridge.App/FieldMenu.cs`，`App.OnStartup` 用 `EventManager.RegisterClassHandler` 挂住 `TextBoxBase`/`PasswordBox` 的 `ContextMenuOpening`，自建中文菜单（剪切/复制/粘贴/全选 + 快捷键，`RowMenu` 样式）。**不要在 XAML 里再给单个输入框设 ContextMenu**，否则会绕过这套统一逻辑。
- 词库编辑行的输入框与行悬停底色曾同色（都是 `Fill`），导致未聚焦的「读作」框看起来消失；行悬停已改 `FillStrong`。**不要把输入框改白底**，白底在白色卡片上同样会隐形。
- 右键菜单定位改为 `PlacementMode.MousePoint`（原来按输入框底边定位，宽字段下离指针很远）。
- 字幕条配色：半透明偏灰绿（`#CCEDEDEB` 底 + `#6B7B75` 文字），更低调。
- 右键菜单改为 **XAML 样式直挂**：App.xaml 定义 `x:Key="FieldMenu"`（ContextMenu，复用 RowMenu/RowMenuItem 原样），在隐式 TextBox/PasswordBox 样式里 `Setter ContextMenu`。**不要**再走 `EventManager.RegisterClassHandler` + 运行时建菜单那条路（`RowMenu` 的 `Width=112` 会压窄，`Dispatcher.BeginInvoke` 有吞弹出风险）。`FieldMenu.cs` 已删除。
- **清缓存只清数据库副本**：`CleanClick` 不得再动 `avatars` 或联系人 `Avatar`（曾因此清完头像全没）；头像目录 `avatars\` 与索引 `contact-index\` 不属于缓存目录。
- 朗读记录清空是卡片右上角的 `IconDangerButton`（30×30、圆角 9、背景 `#FBF1EE` 与「移除」同色；图标是用户提供的 SVG 垃圾桶，`#D81E06`，两个 `PathGeometry` 组成，**不要再换成别的图形**），`IsEnabled = Results.Items.Count > 0`，点击清 `Results`（不是 `controller.ClearQueue()`）。设置页底部只剩淡红「清理缓存」，`SaveButton`/`SaveClick` 已删（设置全程自动保存）。
- 朗读记录区最终 **138 DIP**（空状态 130）、首页底部边距 **16**、记录标题上边距 **10**、通讯录标题上边距 **16**；卡片底边到窗口底实测约 **54 px ≈ 原间距的 41%**，UIA 复测**不可滚动**（再高就会滚，这是硬边界）。下沿渐隐 **56 DIP 四段渐变，且只在 `ContentScroller.ScrollableHeight > 0` 时显示**（`BottomFade`），否则会把首页卡片底边洗掉——那正是之前“看起来贴着底”的原因。调高度务必用标定脚本（改高度→构建→启动→`wvb-scrollcheck` + `wvb-measure`）。
- 气泡：`#6EE4E7E5` 底、`#5C6B64` 字、`Padding 16,9`、`MaxWidth 720`、距底 **50**（上移自身高度 3/5）、右栏居中。
- **平滑滚动**：`src/VoiceBridge.App/SmoothScroll.cs` 用类级 `PreviewMouseWheel` + `CompositionTarget.Rendering` 累积目标偏移、ease-out-quint（160–380ms），覆盖全应用所有 ScrollViewer。**不要再在页面里自绘滚动**（旧的 `PageMouseWheel/ScrollFrame/StopScrolling` 已删）。
- **无悬停提示**：全应用 `ToolTip` 已清空（含 `restrictionTip` 那套禁用控件弹字机制）。禁用态只保留变淡 + No 光标 + `AutomationProperties.HelpText`。不要再加回 ToolTip。
- **词库**：表头（原文/读作/出现方式/操作）贴卡片上沿、与六列逐一安对齐；行内是 **编辑/完成 + 删除** 三按钮（`EditReadingClick` 旧逻辑：点编辑**不**自动聚焦，点哪个框才聚焦；删除是纯文字淡红药丸 `RowDangerButton`，编辑/完成是 `RowSecondaryButton`）。**不要再改回“点行即编辑”**。点「编辑」后点到别的行或表格外会自动提交（`ClearReadingSelectionOutside` → `CommitReading`），等价于点「完成」。表头四列居中、操作对准两按钮中点；**编辑态内容靠左、完成态居中**（别再把编辑态的 TextBox/ComboBox 设成居中）。单元格只读区可直接点：原文→原文框、读作→读作框、出现方式→展开下拉（`ReadingFieldClick` / `FocusScope`）；下拉弹出列表内的点击不算“行外”（`IsInsideDropDown`），否则选不了值。动作列宽 70+70。列表与加号之间的 Hairline 已删，加号按钮就是 `AccentButton`（与「添加音色」同款）。
- 左栏「高级」已改名「设置」（`NavAdvanced` 名不变）；朗读记录只保留「清空」（`SkipButton` 已删）。首页按钮统一「＋ 添加」，首页那句「依次朗读每个人的消息…」已删（`PrefixHint` 置空）。
- 词库视觉：`AddReadingButton` 用 `AccentButton`（与「添加音色」同款）；表头是 `#F4F8F6` 横带 + `TableHead` 样式；删除是带 × 的淡红药丸 `RowDangerButton`。
- **A1 故障（Muse 已定位）**：INPUT 链 REJECT 在 443 ACCEPT 之前，新入站 443 全被丢弃；Caddy/证书/本机回环均正常。需用户批准后由 Muse 调防火墙顺序。
- **更新服务故障（2026-11-10 晚）**：`iceland.de5.net` 的 443 不完成 TLS 握手；TCP 可连、DNS 公网解析到 服务器IP、ICMP 通，但 `r.jina.ai`（外部）报 `ERR_ADDRESS_UNREACHABLE`、`allorigins` 返回 522 → **A1 服务器侧问题**，与用户 VPN 无关（早前误判为本机代理，已更正）。已用 muse-call 通知 Muse。应用内文案改为「服务器没有完成握手，稍后再试」。
- 朗读记录空状态根因：`Render()` 无记录时折叠 `ResultsBox`，页面矮卡其实是 `ResultsEmpty`；已给 `ResultsEmpty` 设 `Height=512 + VerticalAlignment=Center`，空/有记录卡片同高。列表 `Results` `Height=520`。
- 字幕条：底色 `#80E8EAE8`（更透偏灰绿）、距底 26px、右栏内居中（`Grid.Column=1`）、文字 `TrimEnd('。')`。
- 字幕条距底 26px（上移）；文字尾部 `TrimEnd('。')` 只去句号，其他标点保留。
- 验证：build 0/0；`Test.ps1` .NET 209/209 + Python 54；`Verify-Source` PASS。

**1.5.3 发布台账（2026-11-11，Muse 发布单 `codex_20261111_010936_89c978`）**：
- 源码 commit `67a74f0`；payload `1.5.3+67a74f0`。
- ZIP `artifacts\WeChat-Voice-Bridge-V1.5.3-win-x64.zip` = 90,826,648 字节 / `0c11285164f002717cf00fa03c85d721487ce466b57d4ccbfb398b1d2a7b264f`。
- Setup `artifacts\WeChat-Voice-Bridge-V1.5.3-Setup.exe` = 66,213,417 字节 / `1f7dd8bc988ef99dbaa41484670647339bc1fda98e3df6aa041241322a8d4452`。
- 门禁：Clean-payload PASS（1733 文件）、`Test.ps1` PASS、`Verify-Source` PASS、1.5.3 > 线上 1.5.2。
- Muse 回执 `status=ok`（`publishedAt=2026-11-11T00:13:22Z`），称哈希链三方一致、旧清单备份 `latest.json.bak-1.5.2`。
- **独立公网复核**：清单 200（六字段齐全、notes 234 字中文正常）、ZIP 与 Setup HEAD 200 且长度一致、**完整下载 ZIP 重算 SHA-256 一致**。
- **下一步**：无待办。桌面快捷方式仍指向 1.4.7 r3，不要动。

**1.5.2 发布台账（2026-11-10，Muse 发布单 `codex_20261110_174906_4f5a16`）**：
- 源码 commit：`4ae22b4`；payload `1.5.2+4ae22b4be0acef8cdcfb733fe610f9e2bfd2641b`（先提交再打包）。
- ZIP `artifacts\WeChat-Voice-Bridge-V1.5.2-win-x64.zip` = 90,820,155 字节 / `dad840a1ab28cd5ffb6cc086fcf3769900df95b491ff56b0fd6d84ee9813fb9b`。
- Setup `artifacts\WeChat-Voice-Bridge-V1.5.2-Setup.exe` = 66,215,244 字节 / `a9689e16fb1719ce234ccab9f55d71329d87cc354f680ef18a7952fc25791f88`。
- Muse 回执 `status=ok`（`publishedAt=2026-11-10T16:52:12Z`），称哈希链三方一致、旧清单备份 `latest.json.bak-1.5.1`。
- **独立公网复核**：清单 200 / 六字段齐全（version `1.5.2`、size 90,820,155、sha256 与本地一致、notes 166 字中文正常）；ZIP 与 Setup HEAD 均 200 且长度一致；**完整下载 ZIP 重算 SHA-256 一致**。
- **下一步**：无待办。桌面快捷方式仍指向 1.4.7 r3（用户要求保留，便于亲自点一次更新实测），不要动。

**工作区未发布改动（2026-11-10 傍晚，未升版本号、未打包）**：
- 「软件更新」状态圆点改色，按用户要求把三色互换：**绿 `#1D9A5B` = 已是最新（常态）**、**红 `#D33B3B` = 发现新版本**、**橙 `#B2760A` = 下载完待重启安装**（重启安装后仍回到绿）。
- 实现只有一行：`MainWindow.Updates.cs` 的 `RenderUpdate()` 里 `UpdateStateDot.Fill = manifest is null ? LiveDot : verifiedUpdate is not null ? WarnDot : ErrorDot;`，直接复用现有画刷、未新定义颜色，所以左侧状态胶囊（灰/绿/橙/红）与按钮配色完全不受影响。XAML 里那句颜色注释同步改成 green / red / orange。
- 验证：`dotnet build` 0/0；真实应用三态截图（`artifacts/screenshots/update-dot-green.png`、`-red-available.png`、`-orange-ready.png`）均符合预期；临时假 pending 包已删、`updates` 目录回到空、未点安装、线上清单未动。
- **待拍板**：版本号仍为 1.5.1（等于线上），要发布需先升到 1.5.2 + 补 `ReleaseHistory`，再走打包 → Muse → 公网复核；等用户发话。
- 待办：`tools/UpdatePanelPreview`（开发工具，不入发布物）仍按旧三色与旧字号渲染，下次用它量测前需同步。

**工作区未发布改动（第二项，2026-11-10 傍晚）**：更新卡片字号层级。
- 「当前版本」`UpdateVersion` 15 → **17 SemiBold**；新版本标题 `UpdateNoteTitle` 从 `SectionTitle`（16）改为 **13 SemiBold**，体积小字 12 → **11**；状态圆点 9px → **10px**。
- 目的：用户反馈「当前版本」比下面的新版本标题还小；现在三级阶梯 = 17（当前版本）/ 13（待装版本）/ 11（体积），三态都验证过（`artifacts/screenshots/font-state-{uptodate,red,orange}(-crop).png`）。卡片高度与按钮居中结构未变。
- 呼吸灯（两个点，定义在 `src/VoiceBridge.App/BreathingLight.cs`）：周期 3.2 s 的不对称节拍——慢暗 1.25 s → 底部停一拍到 1.7 s → 慢亮回 3.2 s；`Opacity` 1→**0.42**、`Scale` 1→**0.85**，都 `SineEase`，另加一层同色柔光晕（`Fill` 绑定到点的 `Fill`、`BlurEffect`、**0.30**→0、1→**2.05** 倍）。只动透明度/缩放，**不碰 Fill**，所以颜色怎么变都不影响呼吸；两个点外各包一层同尺寸 `Grid`，占位尺寸未变。
  - **软件更新点**：三态（绿/红/橙）都呼吸，构造函数里常驻启动。
  - **左下角连接胶囊点**：**只有绿色（正在监听）呼吸**；灰（未开始）、黄（准备中）、红（已停止）全静止，由 `MainWindow.SetPillBreathing(state.State == BridgeState.Running)` 开/停。
  - 旧记录里的对称淡入淡出（`AutoReverse`、1.6 s、To 0.35/0.88）已作废，不要再改回去。
- 右键菜单：`App.xaml` 新增隐式 `ContextMenu`/`MenuItem` 样式（`BasedOn` 现有的 `RowMenu`/`RowMenuItem`），输入框的剪切/复制/粘贴不再露出 WPF 默认灰渐变方角菜单，改用「⋮」那套白底圆角面板；`RowMenuItem` 模板新增一列快捷键提示（`InputGestureText` 为空时折叠，所以「⋮」菜单外观不变）。未改任何命令逻辑。
- **开发版运行前提（重要）**：`dotnet build` 不会往 `bin\Release\net8.0-windows` 放 Python 运行时，也不会拷 `worker\deps`；直接跑 bin 里的 exe 点「开始」会报「缺少 Python 3.12 Worker」。需要先拷一次：`artifacts\win-x64-v1.5.1\runtime` → `bin\...\runtime`、`artifacts\win-x64-v1.5.1\worker\deps` → `bin\...\worker\deps`（2026-11-10 已补，`artifacts\win-x64\runtime` 也一并补回给 `Test.ps1` 用）。
- 页面提示小字（`notice`）：改成**页内归属 + 5 秒自动消失**的属性，切页即清。原来同一份字符串同时写进首页/音效/高级三个 TextBlock，任何页的提示都会在三个页出现（「试听完成」跑到音效页的根因）；现在三个提示位都挪到各页标题下方、统一 Footnote 12px、空时折叠不占位；词库的 `ReadingNotice` 也加了同样 5 秒过期；删掉了启动时的「已自动找到微信数据目录。」。`uiSmokeTest` 相应改为在当前页三个槽里找文案。更新状态行与首页状态描述属于状态，**不**自动消失。
- 提示行不挤动布局：四个提示位（首页/音效/高级/词库）改为**固定 20 DIP 高度、常驻可见**的留白行，文字填入不改高度；上方标题下边距等量减免，无提示时排版与修前一致。实测 shift = 0 px（修前 39 px）。改这四个槽时**不要再切 Visibility**，否则预留高度会丢、页面又会被推一下。
- 基线对齐：标题「v1.5.2」与体积「0.0 MB」改成**同一个 TextBlock 的两个内联 Run**（原来是两个 TextBlock 只能底边对齐，小字号基线会高 1.5 DIP）。以后要改这两处的字色/字号，改 Run 的属性；名字 `UpdateNoteTitle` / `UpdateNoteSize` 不变，code-behind 的 `.Text` 赋值照旧。

**当前一节（v1.5.1，源码完成、已发布）**：
- 「软件更新」状态行只留一行：9px 圆点 + `当前版本 vX.Y.Z`，水平 `StackPanel`、两者 `VerticalAlignment=Center`，灰/琥珀/绿三态只换 `Fill`；第二行说明（「已是最新版本」「发现新版本」「更新包已就绪」）已删除，状态由卡片下方状态行与按钮文案承载。
- 按钮整块居中：两列 Grid（左列 = 状态行 + 说明标题行的 `StackPanel`，右列 = 按钮 `VerticalAlignment=Center`），因此按钮居中范围是「卡片上沿 → 说明框上沿」；无说明时左列只剩状态行，按钮即居卡片。**不要改回 `RowSpan` 方案**：跨行子元素会把空的第二行撑到 18px，卡片变高、状态文字看着偏高（已实测差 9 DIP）。
- 更新说明上方改为**标题行**：`UpdateNoteTitle` = `v{版本}`（`SectionTitle`，16 Semibold）+ `UpdateNoteSize` = `{大小} MB`（12 灰、底线对齐）；原先「v1.5.0 · 90.8 MB」小字不再出现。
- 版本 **1.5.1**（`Directory.Build.props`），`ReleaseHistory` 已加条目。
- 联系人「移除」按钮**不再有悬停提示**：`ContactRemoveAction` 样式里的 `ToolTip` Setter 已删除（两处使用点共用该样式，按钮自身未写 ToolTip），悬停只保留配色变化。
- 「高级 → 试听」改为**字段标题在控件上方**：`FieldLabel`（12 灰）标「音色」「文本」，块内边距 16/14，块间距 16；文本行下方那条 `Hairline` 已删除，按钮直接在卡片内右对齐（`Row` + `Margin 0,2,0,6`）。
- 「语音服务」标题与首个分组头之间加 `Margin="0,12,0,0"`（`ProvidersList`）。
- 「⋮」更多菜单改为**向右下展开、接近方形**：`HorizontalOffset = 79.5 - ActualWidth`（右缘固定距窗口 16 DIP，加宽部分向左吃，绝不顶边），`VerticalOffset = -4`（与按钮留约 6px，别压着阴影）；`RowMenu` 用 `MinWidth 0 + Width 112`、圆角 12、菜单项 `Padding 16`，实测 112×102 DIP。**注意 DPI 是 2.0**，早期记录里按 1.5 换成 DIP 的旧数字（106.7 / 23 / 127）都已作废。79.5 里的 95.5 是「⋮ 左缘→窗口右缘」的布局常数，改外边距要同步。**不要再改回「右缘对齐、向左展开」**，用户明确说那样「往左歪」。
- 验证：`dotnet build` 0/0、`Test.ps1` **.NET + Python 全 PASS**（直接 `dotnet test` 会因缺 `WVB_PYTHON` 有 7 项环境性失败，必须走 `Test.ps1`）、`Verify-Source` PASS、58 项 PASS；真实应用截图（见 WORKLOG 最新节）；`UiSmokeTest nohover` 实测「移除」按钮悬停 4 秒无 tooltip。

**1.5.1 发布台账（2026-11-10，Muse 发布单 `codex_20261110_141758_3cb004`）**：
- 源码 commit：`dc2a9fd`；payload 产品版本 `1.5.1+dc2a9fd903f1d64ffb3e5597c63f1ac55a00e715`。
- ZIP `artifacts\WeChat-Voice-Bridge-V1.5.1-win-x64.zip` = 90,815,896 字节 / `6c43b41dddd0764b7084bb0ebc567818578d2cfae97e48f8e0ab01b0b5e65b1b`。
- Setup `artifacts\WeChat-Voice-Bridge-V1.5.1-Setup.exe` = 66,213,170 字节 / `cfd0482045045267b5bef2b3526a08c377f7e45517157529a8f3368f66fc4a3b`。
- 门禁：Clean-payload PASS（1733 文件）、`Test.ps1` PASS、`Verify-Source` PASS、58 项等价性 PASS、产物 1.5.1 > 线上 1.5.0。
- Muse 回执 `status=ok`（`publishedAt=2026-11-10T13:21:18Z`），称哈希链三方一致、旧清单备份为 `latest.json.bak-1.5.0`。
- **独立公网复核**：`latest.json` 200 / 六字段齐全（version `1.5.1`、size 90,815,896、sha256 与本地一致、notes 294 字中文正常）；ZIP 与 Setup HEAD 均 200 且长度一致；**完整下载 ZIP 重算 SHA-256 = `6c43b41d…65b1b`，与清单一致**；本地 Setup 重算 = `cfd04820…4a3b`，与回执一致。
- **下一步**：无待办。桌面快捷方式仍指向 1.4.7（用户要求保留，便于亲自点一次更新实测），不要动。

## 0b. 上一节（v1.5.0 周期，已上线）

**A–E 全部完成并已发布上线**（下方 A–E 为细节记录）。发布单 `codex_20261110_130657_f72a7b`，Muse 回执 `status=ok`（`publishedAt=2026-11-10T12:09:47Z`），公网已复核：清单 200 / 六字段齐全 / ZIP 与 Setup HEAD 200 且长度一致 / 完整下载 ZIP 重算 SHA-256 一致。

**发布产物**：ZIP `artifacts\WeChat-Voice-Bridge-V1.5.0-win-x64.zip` = 90,814,448 字节 / `48E2ADDAF9A6FFB7AF19CC2DFE1943A2176B037F42F7D95397A892BE6EE6CC33`；Setup `artifacts\WeChat-Voice-Bridge-V1.5.0-Setup.exe` = 66,213,216 字节 / `462980BDE3133CF37C11EDD926707D95B5ECADDC44EB1E17148A818F38B11734`。payload 目录 `artifacts\win-x64-v1.5.0\`（1733 文件，Analyze-CleanPayload PASS）。

**注意**：桌面快捷方式**仍指向 1.4.7**（用户要求保留，便于亲自点一次更新实测）；不主动改。

---

**A. API 配置界面精简**（完成）：同厂商多 Key 去掉「未启用/待用/已启用/使用中」括号小字；右侧只留一个白色「启用」按钮（启用后绿色「使用中」+ 行首绿点，未启用灰点）；「编辑/删除」并入「⋮」菜单；删光面向开发者的解释文案。

**B. 音色失效语义**（完成）：API 改动不影响所选音色时一律不动、不消失、不提示；仅当音色所属厂商 Key 全部删除才变灰色斜体 +「音色已失效，请重新选择」，下拉框名字保留但不可点选。根因是 `RefreshLibrary` 里 `voiceChoices.Clear()` 重置 WPF 选中态，已改就地同步（`SyncVoiceChoice`）。

**C. 凭据指针事故修复**（完成）：当日构建覆写 `settings.json`/`fish-keys.dpapi`、服务指针被 32 位空壳占位值顶掉致 401。根因两处已修（`ProfileEditors.Save` 铸新名顶掉旧名、`KeyStore.Load` 读失败落 legacy 整库覆写）；指针已恢复到真钥匙条目 `speech-d1ee2ca3106c44d1af5b91b674944451`，自验（重启指针不变 + 真实试听成功）通过。**修复指针类操作必须在应用未运行时做**（旧构建退出会写回内存里的坏值）。

**D. 凭据库自动清扫**（完成）：`fish-keys.dpapi` 只保留被引用条目（任何服务 `CredentialId` + 顶层 `FishKeyName` + 联系人/试听凭据覆盖）。时机：①启动且全部迁移完成后；②删除服务时。本次会话新建未挂载条目豁免当次、下次启动再判；只删无引用条目，条数记 `app.log`。实现：`src/VoiceBridge.Core/CredentialSweep.cs` + `MainWindow.SweepCredentials`；自验（塞两条无引用条目进库、重启后被清 + 试听照常）通过。

**E. 「⋮」菜单重做**（完成，用户复查截图后追加）：默认 WPF 菜单是灰渐变边+方角，与卡片不同画风。新增 `RowMenu`/`RowMenuItem`/`RowMenuItemDanger`（App.xaml）：白底圆角 10、`#E9EEEC` 细边、柔和阴影、高 34 行、hover 铺底色（删除项淡红 + 文字警示红）；`MinWidth=116`。定位右对齐到「⋮」，见 `ProviderMoreClick`。验证工具：`tools/MenuPreview`（三态静态图）、`tools/UiSmokeTest menu|menuclick`（抓真实弹窗截图 + 真鼠标点击验证整行可命中）。

**流程红线（本轮已由用户授权发布，红线照旧对后续生效）**：1.5.0 已打包并经 Muse 上线，**桌面快捷方式仍未更新**；今后任何推线上仍须用户本人明确发话。

> **验证基线（1.5.0 全部通过）**：`dotnet build` 0/0；`Test.ps1` PASS；`dotnet test` 211/211（含 6 项清扫新测试）；`Verify-Source.ps1` PASS；58 项等价性 PASS；清扫自验 = 钥匙库 16→1（仅存被引用条目）、app.log 记「删除 2 条」；UIA 真实窗口试听「试听完成。」。

## 1. 一句话现状

源码与线上同为 **1.5.2**（`Directory.Build.props` 是版本唯一来源，程序集 1.5.2.0、`1.5.2+4ae22b4`）。1.5.2 已由 Muse 流水线上线（发布单 `codex_20261110_174906_4f5a16`，`status=ok`，`publishedAt=2026-11-10T16:52:12Z`），本助手已独立公网复核（清单六字段、ZIP/Setup HEAD 长度、完整下载重算 SHA-256 全部一致）。1.5.1 在 1.5.0 之上做的是界面打磨（更新页标题化与整块居中、高级页布局、⋮ 菜单比例、「移除」去悬停提示）。1.5.0 在 1.4.9（wx-multi 多版本适配 + 更新页精简）之上新增：API 配置界面精简（单启用按钮 + 「⋮」菜单重做、无开发文案）、音色不无故消失（厂商 Key 全删才失效）、凭据指针修复（迁移/加载不再覆写「服务→凭据」映射）、凭据库自动清扫（只留被引用条目）。**不回退 1.4.7 / 1.4.8 / 1.4.9 任一改动**。

⚠️ **上线闸门状态：已解除**。用户 2026-11-09 明确说「检查一遍没有问题之后，推到线上去就好。仔细测试一遍，没有问题之后，推到线上去就好了。要去联系 muse推新版本哈」，并补充「你跟 muse 沟通就好」。但**发布仍必须走 Muse 通道**：不自行上传、不索要凭据、超时只用 `-Check`、不重复发单、不重发 1.4.7 旧包。早前「别上线啊，我还没测试呢」的闸门已被上述明确授权取代。

⚠️ **桌面快捷方式刻意保持 1.4.7 r3**（`artifacts\win-x64-v1.4.7-r3\WeChatVoiceBridge.exe`），不再改成 1.4.8：线上 1.4.8 严格大于桌面版本，客户端才会出现更新按钮，用户可亲自点一次更新并目视确认新的引导窗口行为。

✅ **线上清单已由 1.4.8 正式覆盖**：`version=1.4.8`、`publishedAt=2026-11-09T22:59:27Z`、`sha256=a4ca75b3…`、`size=89464301`，旧版备份 `latest.json.bak-1.4.7`。此前那份流水线之外的手工翻版 1.4.7（`publishedAt=2026-11-09T22:20:00Z`）已被取代，不再是客户端可读的清单。公网 `GET latest.json` 的 403（Caddy）已恢复 200，Muse 独立两次实测确认。

ℹ️ **手工翻版来源（Muse 最终结论）**：A1 `auth.log` 显示 22:13:01 UTC 有人 `sudo cp` 清单 → `latest.json.bak-1.4.6`，随后 `sudo mv /tmp/latest-147.json` → `latest.json`；文件名不是流水线用的 `/tmp/latest-new.json`，流水线日志无此笔。我没有 A1 登录/sudo 能力，否认已记录；现有日志无法指认操作者，Muse 不臆测并已报给用户。

## 2.0d 最新一轮（1.4.8）：更新完成后由用户决定何时打开新版

用户指令：更新安装引导进度到 100% 后，**不点「完成」或叉，新版应用界面不能自己弹出来**；点确定/叉之后，安装引导界面消失、新版界面才打开。核对确认原行为不符合：`UpdateInstaller.Apply()` 在 95% 无条件 `restart(exe)`，`Restart()` 以 `MainWindowHandle != IntPtr.Zero` 判定启动成功，所以主窗口提前显示。

- **设计取舍**：保留 `restart(exe)` 承担的「新版加载成功才提交更新、失败自动回滚」，只把「启动确认」和「显示窗口」解耦。新增 `src/VoiceBridge.Core/UpdateHandoff.cs`：`--await-handoff <ticket>`、固定目录 `%LOCALAPPDATA%\WeChatVoiceBridge\updates` 下的 `ready` / `go` 原子信号、`ShouldShowMainWindow() => IsGo()`。新版进程仍启动但只写 `ready`、主窗口隐藏；安装窗口 `OnClosing`（「完成」按钮与叉都汇入）写 `go`；新版进程轮询到 `go`（或 helper 消失兜底）才 Show。
- **改动文件**：`UpdateProcess.cs`（`Apply` 加握手参数、`Restart` 改用 `IsReady()`、`ReadRestartNotice` 兼容 5/7 参数、新增 `HelperStillRunning`）、`App.xaml.cs`（`AwaitInstallationConfirmation` + `OnExplicitShutdown` + 120ms 轮询 + 单实例激活回调在等待期 return + 异常兜底显示）、`UpdateInstallationWindow.cs`（握手字段、`OnClosing` 写 `go`、文案与「完成」按钮）、`tests/VoiceBridge.Tests/UpdateHandoffTests.cs`（18 例）、`Directory.Build.props`（1.4.8）、`ReleaseHistory.cs`（1.4.8 条目）。回滚路径与老客户端 5 参数路径保持原行为。
- **产物**：`artifacts\win-x64-v1.4.8`，ZIP **89,464,301 字节 / `A4CA75B3C4F9EECB2D4BADB291161165FE6A7B9107F5343F678736D3E6CCB1C0`**，Setup **65,302,086 字节 / `A9FB702F06EC212C17E80B5F86FBE042734345769D92B6B68F11AB786FA3128C`**。完整交代见 WORKLOG「1.4.8」段。
- **已上线**：发布单 `codex_20261109_235238_857190` 正式 `status=ok`，ZIP 哈希链三方一致、Setup 一并发布、`latest.json` 翻到 1.4.8 并备份 `latest.json.bak-1.4.7`；我方独立公网友核全部通过（详见 WORKLOG「1.4.8 · 上线结果」）。
- **未验证**：跨进程 WPF 窗口隐藏的实机目视确认，需用户点一次更新实测；单元测试与静态行为链复核已覆盖协议与参数 round-trip。

## 2.0c 上一轮（1.4.7 r3）：厂商名保留 + 同厂商多 Key 自选

用户指令：删掉 API 后该厂商音色仍保留厂商名、只换显示样式，编辑与删除照常可用；同一厂商有多个 Key 时多一个「启用」按钮选择用哪个，不同厂商各可启用一个，只有同厂商 ≥2 个 Key 才出现该按钮；添加第二/第三个 Key **只添加不自动应用**。用户同时澄清事实：**Fish 音色 ID 是平台公共的，任何有效 Key 都能跑**——所以音色不应绑死在某一个 Key 上。

- 数据层：`VoiceProfile` 新增可空 `ProviderKind`（删连接后仍知道厂商）；`VoiceLibraryMigration` 增 `VendorOf()` / `CacheVendor()` / `UseActiveKey()`，`BindOrphanVoices()` 加厂商保护；新增 `src/VoiceBridge.Core/ProviderActivation.cs` 承载 `StaysOnStandby()` / `NeedsActivation()` / `Activate()` 三条规则（跨厂商零影响、单测锁定）。`LocalSettings.Load()` 趁连接还在给老音色回填厂商并 `Save()` 一次。
- 显示层：`LibraryRow` 加 `ProviderMissing`/`ProviderHint`，XAML 用 `DataTemplate.Triggers` 把缺失音色名称灰化斜体并显示警示标签，编辑/删除按钮不变；`ProviderRow` 加 `ShowActivate`/`CanActivate`/`ActivateLabel`，「启用」按钮只在同厂商 ≥2 个 Key 时出现，已启用的显示「使用中」并禁用。
- 行为层：`ProviderEditor.Save()` 不再强制 `Enabled=true`；`EditProvider()` 新增 Key 在同厂商已有启用 Key 时置为待用；`DeleteProviderClick()` 删除前缓存厂商、同厂商还有 Key 时音色立即跟随；`ActivateProviderClick()` 只切同厂商并让该厂商音色跟随。`SpeechFor()` / `ChooseContactVoice()` / `RefreshContactSpeech()` 按厂商回退到启用的 Key（`voice with { ProviderId = provider.Id }` 满足 `Validate` 归属校验）。
- 编辑可用性：`VoiceEditor` 在原服务已删时插入「（原服务已删除）」占位选项，弹窗可正常打开、保存且保留原绑定；有其它服务时把服务选择框移到详情页，可真正改绑。
- 产物在 **`artifacts\win-x64-v1.4.7-r3`**，ZIP / Setup 用标准文件名；r2 产物改名留档。哈希与完整交代见 WORKLOG「1.4.7 r3」段。

## 2.0 上一轮（1.4.7 修订）：不预置服务与音色

用户改主意：内置音色也清空、无内置音色、只保留用户新添加的；并要求「api 添加进来，对应的音色就不要显示无 api 了，而是变成可用的状态」。

**「无 API」的由来**：首轮 1.4.7 摘掉了出厂 `fish-default` 服务，却按当时要求保留了 4 个预置音色 +「此前使用的音色」的播种，它们的 `ProviderId` 仍指向已不存在的服务，于是音色库显示「服务已删除 / 服务不可用」、首页选音色提示「尚未配置的 API」。

**改动**：删掉 `InitializeLibrary()` 的整段预置音色播种、`LegacyPresetProviderId` 常量、`MainWindow.xaml.cs` 的 `VoicePresets` 四个内置音色。新增 `src/VoiceBridge.Core/VoiceLibraryMigration.cs`：`IsFactoryVoice()` 双条件精确识别（`ProviderId` 精确为出厂 Id **且** `Id` 以 `fish-` 开头，用户改绑过的保留）、`RemoveFactoryVoices()`、`CleanContactReferences()` / `CleanTestVoiceReference()`、`BindOrphanVoices()`。`LocalSettings.Load()` 接入音色迁移并清理悬空引用；`EditProvider()` 新增服务后把孤儿音色自动关联到新服务；文案统一为「未关联服务 / 尚未关联可用的 API」。DPAPI 凭据库仍不读不写不删。

**产物目录注意**：用户当时正在运行 `artifacts\win-x64-v1.4.7\WeChatVoiceBridge.exe`，DLL 被锁，覆盖发布失败且未强杀进程。本轮发布在 **`artifacts\win-x64-v1.4.7-r2`**，ZIP / Setup 仍用标准文件名；旧 Setup 改名保留为 `...-r1-Setup.exe`。要测新版请用 r2 目录或重新解压的新 ZIP，**不要继续用 `win-x64-v1.4.7` 旧目录**。完整交代与哈希见 WORKLOG「1.4.7 修订」段。

## 2.0b 同一版首轮（1.4.7）：取消出厂自带默认 API

用户当时要求：全新安装一条 API 服务都不种、任何条目都可删；老用户自建服务与钥匙原样保留、内置默认 API 自动删掉；出厂包不得含真实 API 钥匙；**预置音色本次不动**（该条已被上一节的修订推翻）。

**根因**：不是源码里硬编码了钥匙（源码/git 历史/1.4.6 的 ZIP 与 Setup.exe 都没有明文真实 Key），而是 `MainWindow.Library.cs` 每次启动补种固定 `Id="fish-default"` 的连接，`SyncDefaultFish()` 把本机 DPAPI 凭据库里当前选中的凭据名写回它，`DeleteProviderClick()` 又禁止删它。开发机 `settings.json` 实测即 `fish-default → CredentialId=speech-fish-default`。

**摘除**：删掉 `InitializeLibrary()` 的 `providers.Insert(0, ...)` 播种、删掉 `SyncDefaultFish()`、删掉删除特判与「默认连接」标签；`StoreProvider()` 去掉联动隐藏控件的 `applyingProvider`；XAML 的「默认 Fish 服务/密钥」改中性文案并说明「软件不预置任何服务或密钥」。预置音色播种当时保留、常量改名 `LegacyPresetProviderId`（**该状态已在 2.0 修订中删除**）。

**迁移**：新增 `src/VoiceBridge.Core/ProviderMigration.cs`，`LocalSettings.Load()` 读盘后无条件 `RemoveFactoryDefault()`，只按 Ordinal 全等摘 `fish-default`，用户服务连同 `CredentialId` 原样保留、顺序不变；发现过出厂条目就立刻 `Save()` 把 `settings.json` 写干净；**不读不写不删 DPAPI 凭据库**。`InitializeLibrary()` 再过滤一次做双保险。

**出厂包门禁**：新增 `scripts/Assert-CleanPayload.ps1`，ZIP 与 Setup 打包前强制跑——拒绝 `settings.json`/`.dpapi`/`.key`/`.env`/数据库；全文件分块扫明文密钥；读取本机 DPAPI 凭据库（只在内存里，绝不打印）按明文与 UTF-16 两种形式精确比对，命中即拒绝打包。`Verify-Source.ps1` 加了三条防复发断言（禁止再播种、`Load()` 必须调迁移、`fish-default` 只允许出现在 `ProviderMigration.cs`）。完整交代见 WORKLOG 1.4.7 段。

## 2.1 上一轮（1.4.6）：更新按钮三态配色

用户要求「软件更新」区有升级时的按钮更美观、上下居中，且三种状态颜色各不相同。根因：`Row` 内高 36 DIP（`MinHeight 52 − Padding 16`），而 `SecondaryButton` 是 `MinHeight 34`、`PrimaryButton` 是 `MinHeight 38 / MinWidth 96 / Padding 24,8`——状态切换时按钮高度与宽度都跳变，把整行撑高、左右失配。

修复：`App.xaml` 新增浅绿描边 `UpdateReadyButton` 与实心绿 `UpdateInstallButton`，二者与灰色 `SecondaryButton` 共用 36 DIP 高度、`MinWidth 120`、`Padding 18,7`、圆角 9、内容居中；`MainWindow.xaml` 给 `UpdateAction` 设本地尺寸（本地值优先于样式）；`MainWindow.Updates.cs` 的 `RenderUpdate` 按三态选样式与字色，按钮文字禁换行并居中。隔离工程 `D:\USERNAME\CodexScratch\Temp\wvb-update-row-20261109` 截图确认三态三色、尺寸一致、上下居中。详见 WORKLOG 1.4.6 段。

## 2. 上一轮（1.4.5）：自定义提示词选中状态根治

用户第三次反馈同一问题，1.4.3 的修复在真实主窗口里无效。**真实原因**：提示词 ComboBox 在 `DataTemplate` 内由 `ContentControl`/`ListBox` 生成，用户点「自定义」时 `SelectedItem`（以及收起框的 `SelectionBoxItem`）变成该项，而 `SelectedValue` 的 TwoWay 绑定会**立刻把值改回源值**——于是「值已经是旧的、显示还是『自定义』」。1.4.3 只在对话框关闭后 `Notify`/`UpdateTarget()`，既拉不回 `SelectedItem`，`ContextIdle` 优先级又被主窗口重复定时器饿死。当时的验证工程用的是裸 ComboBox + 直接赋值 `SelectedValue`，没有复现真实点击行为，所以误判为通过。

现在的做法让「自定义」永远不成为选中值：

- `ContactCard.AnnouncementChoiceId` setter 遇到 `custom` 直接 return；删除不再使用的 `CustomRequested` 事件 / `customRequestPending` / `FinishCustomRequest()`。
- `MainWindow.Library.AnnouncementSelectionChanged` 用 **`e.AddedItems`** 判断动作项（不看已被绑定改回的 `SelectedValue`），随即用 `RestoreAnnouncementItem` 以 `SetCurrentValue` 同时把 `SelectedItem` 与 `SelectedValue` 改回真实选项——不写入 TwoWay 源。
- 对话框用默认 `Normal` 优先级异步打开；`SyncAnnouncementSelection` 按 `ItemsSource` 匹配 ComboBox（不依赖模板里的 `x:Name`/`DataContext`），添加成功后同样恢复两个属性。

验证：隔离工程 `D:\USERNAME\CodexScratch\Temp\wvb-announcement-rootfix-20261109` 用 `ContentControl`+`DataTemplate` 渲染、链接真实源码、逐字复制产品处理器、以 `SelectedItem` 模拟真实点击；7 组场景（取消/点叉/添加 × 初始无提示词/昵称说/消息+留言）全部通过，`shows 自定义=False`。详见 [WORKLOG.md](WORKLOG.md) 的 1.4.5 段。

## 2b. 上一轮（1.4.4）做了什么

| 问题 | 根因 | 修复位置 |
| --- | --- | --- |
| 旧版本升级后已选联系人不显示头像 | `ContactProfile.AvatarUrl` 是 1.4 新增字段，`LocalSettings.Load` 无迁移；老数据为 null 时 `AvatarCache.GetAsync` 直接短路。启动索引虽构建，但结果从不回填卡片 | `ContactSearchCache.TryFind`（新增，按会话 ID 精确查、不受搜索 60 条上限影响）、`MainWindow.ContactIndexing.BuildContactIndexAsync` 索引就绪后 `RestoreMissingAvatars`、`ContactCard.UpdateAvatar`（只更新 URL 不改名字，借既有 `changed` 落盘） |
| 重启更新时任务栏多一个图标 | 更新助手从 staging 启动、新版从安装目录启动，Windows 视作不同应用；助手窗口 `ShowInTaskbar=true` | `App.xaml.cs` 在窗口创建前调用 `SetCurrentProcessExplicitAppUserModelID("WeChatVoiceBridge")`，三个进程归到同一任务栏按钮；保留任务栏进度与「更新完成」窗口 |
| 词库点「编辑」不出现光标 | `FocusReading` 用 `presenter.ContentTemplate.FindName`，但模板由 `ListBox.ItemTemplate` 提供，该属性常为 null → 静默 return；且未等 `Collapsed→Visible` 布局完成 | `FocusReading` 改在容器可视化树按名字找 TextBox，用 `Keyboard.Focus` + `IsVisibleChanged` 等待可见，最多重试 3 次；编辑仍聚焦「读作」并把光标放在末尾 |

## 3. 前几轮成果（1.4.3 及更早，详见 WORKLOG）

- 弹窗外壳统一：透明窗口 + 暖白圆角卡片 + 中性阴影，去掉四周浅绿色底（`DialogShell.cs`、`ReleaseNotesDialog.xaml`）。透明窗口没有原生标题栏，拖动改由 `DragMove()`、补了 Alt+F4。
- 自定义提示词（1.4.3 的初步修复**未根治**，1.4.5 已重做，见第 2 节）：每次打开空白；旧数据自定义文字无预设时生成真实菜单项，不再退回显示"自定义"字样。
- 提示词显示：句首 `提示词+消息`、句尾 `消息+提示词`；下拉菜单最后「自定义」用绿色 ＋ / 加粗 / 分隔线区分。
- 移除联系人热更新：放开 UI 只读限制，`SessionController.Start` 新增可选 `Func<ChatMessage,bool>? admitFilter`，在 `Admit` 中按最新 `routing` 快照过滤，被移除者立即静音。**Worker 采集白名单仍是启动时固定的**，只是静音；换整批名单仍需结束再开始。
- 词库替换覆盖提示词：新增 `Core/ReadingText.cs`（复刻 worker `_apply_rules` 的 both/inside/standalone、数字护栏、长词优先），`ContactProfile.Prepare` 加可选 `renderPrefix`，`RoutedSpeech.Resolve` 与试听都对提示词应用 `activeReadings`，且提示词用 `ignoreScope: true`（一律包含匹配，避免"单独出现时"规则在拼出的短语里漏掉）。
- `Publish.ps1` 缓存复用：已解压的 Python 运行时不再 `Expand-Archive -Force` 清空，`worker/deps` 不再整目录删除，可重复发布到同一目录。

## 4. 不可破坏的边界

- 先读 `AGENTS.md`。所有真实 DB 探针必须 `tempfile.TemporaryDirectory()`，放在本地 scratch、OneDrive 之外；同批复用一个快照；离开上下文前关闭 SQLite/子进程；DB/WAL/SHM 不进项目、报告、发布物。
- 不修改微信进程内存、不注入/Hook/UIA 热激活、不发微信消息、**不重写密钥扫描算法**。`scripts/Verify-Source.ps1` 会强制 `ReadProcessMemory` 只出现在 `worker/audited_wechat_keys.py`。
- 微信受审计支持版本仅 `4.2.0.13`（`WeChatSession.SupportedVersion`），不要擅自放宽。只读访问也不保证符合微信平台规范或账号安全。
- 始终手动开始、初始 IDLE，结束取消整条链路；不自动恢复监听。
- 用户配置与 DPAPI 密钥在 `%LOCALAPPDATA%\WeChatVoiceBridge`，不进发布包；不删用户数据库、录音、配置。
- **不得重新引入任何出厂预置 API 服务、出厂音色、不可删条目或内置密钥**：全新安装的「语音服务」与「我的音色」两个列表都必须为空。`scripts/Verify-Source.ps1` 会拦截再次播种（`providers.Insert(` / `SyncDefaultFish` / `DefaultFishId` / `VoicePresets` / `LegacyPresetProviderId`）、迁移或孤儿绑定缺失、以及 `fish-default` 字面量扩散；`scripts/Assert-CleanPayload.ps1` 会在 ZIP / Setup 打包前拦下个人配置与真实 API Key。
- 发布上线需用户授权；固定 feed `https://iceland.de5.net/voice-bridge/latest.json`，六字段契约不变。本地打包 ≠ 上线。
- 用户重视先交付可用版本，别为了完善程度做无关扩展或反复跑长测试。

## 5. 命令速查

```powershell
# 本机 `C:\Program Files\dotnet` 一度只剩运行时、没有 SDK，已从微软官方
# 下载 .NET SDK 8.0.427 解压到临时目录（不改系统 PATH、不装系统组件）。
# 若临时目录被清理：重新下载 dotnet-sdk-8.0.427-win-x64.zip 并解压即可。
$sdk = 'C:\Users\USERNAME\AppData\Local\Temp\wvb-sdk\sdk\dotnet.exe'
$py  = 'D:\OneDrive\文档\ChatGPT\微信语音\artifacts\win-x64\runtime\python\python.exe'

# 构建 / 全量测试 / 静态边界
& $sdk build 'D:\OneDrive\文档\ChatGPT\微信语音\WeChatVoiceBridge.sln' -c Release
& 'D:\OneDrive\文档\ChatGPT\微信语音\scripts\Test.ps1' -Dotnet $sdk -Python $py
& 'D:\OneDrive\文档\ChatGPT\微信语音\scripts\Verify-Source.ps1'

# 发布到独立目录（ZIP 与 Setup 都会先跑出厂包门禁）
# 每轮用新的 -vX.Y.Z 目录：用户可能正运行旧目录里的 exe，DLL 被锁会令覆盖发布失败。
& 'D:\OneDrive\文档\ChatGPT\微信语音\scripts\Publish.ps1' -Dotnet $sdk `
    -OutputDirectory 'D:\OneDrive\文档\ChatGPT\微信语音\artifacts\win-x64-v1.4.8' `
    -ZipPath 'D:\OneDrive\文档\ChatGPT\微信语音\artifacts\WeChat-Voice-Bridge-V1.4.8-win-x64.zip'
& 'D:\OneDrive\文档\ChatGPT\微信语音\scripts\Build-Installer.ps1' `
    -PayloadDirectory 'D:\OneDrive\文档\ChatGPT\微信语音\artifacts\win-x64-v1.4.8'
# Setup 输出名由 Directory.Build.props 的 <Version> 自动决定：
# artifacts\WeChat-Voice-Bridge-V1.4.8-Setup.exe
# 桌面快捷方式刻意保持 1.4.7 r3 不动，让用户能亲自体验一次真实升级；若要改，用：
# & 'D:\OneDrive\文档\ChatGPT\微信语音\scripts\Install-DesktopShortcut.ps1' -InstallDirectory '<新目录>'
# 若目标 ZIP 已存在且位于 OneDrive，覆盖可能触发安全删除拦截：先把旧包改名留档，再生成新包。
```

`Publish.ps1` 会复用 `artifacts/build-tools` 里已校验的 Python 3.12.10 NuGet 包与 sqlcipher3/zstandard wheel；本轮用 `-ZipPath` 指定标准文件名。

## 6. 验证基线（1.4.8 后重跑）

- Release 全量构建：**0 警告 0 错误**。
- `Test.ps1` 全量：**197/197 C# PASS**（179 基线 + `UpdateHandoffTests` 18）+ **54/54 Python PASS**（41 个需 SQLCipher 运行时的跳过属既有基线；worker 未改，仍复跑确认）。
- `Verify-Source.ps1`：**PASS**（含禁止服务/音色播种、迁移与孤儿绑定必须存在的全套防复发断言）。
- **出厂包门禁**：`Publish.ps1` 与 `Build-Installer.ps1` 各跑一次 `Assert-CleanPayload`，均 `PASS (1619 files, no per-user data, no live API key; 2 local credential(s) compared)`——本机 2 个真实 Key 按明文与 UTF-16 比对零命中。
- 发布物（**以这一组为准**，1.4.7 各组哈希均已作废且不得再用于发布单）：ZIP `WeChat-Voice-Bridge-V1.4.8-win-x64.zip` **89,464,301 字节** / SHA-256 `A4CA75B3C4F9EECB2D4BADB291161165FE6A7B9107F5343F678736D3E6CCB1C0`，1621 项；Setup.exe **65,302,086 字节** / SHA-256 `A9FB702F06EC212C17E80B5F86FBE042734345769D92B6B68F11AB786FA3128C`；无 `.db`/WAL/SHM/`settings.json`/密钥残留。
- **产物内容核验**：ZIP 内 `WeChatVoiceBridge.dll` / `VoiceBridge.Core.dll` / `WeChatVoiceBridge.exe` 与发布目录逐字节一致；EXE `ProductVersion=1.4.8+<commit>`、`FileVersion=1.4.8.0`，Setup `ProductVersion=1.4.8`；出厂 DLL 含 `--await-handoff` / `UpdateHandoff` / `ShouldShowMainWindow` / `SignalGo` / `AwaitInstallationConfirmation` 与 1.4.8 版本说明，旧的「新版本已启动，可以关闭此窗口」文案已消失，`VoicePresets`/`LegacyPresetProviderId`/`SyncDefaultFish`/`DefaultFishId` 仍未回流。
- **静态行为链复核**：握手目录 `%LOCALAPPDATA%\WeChatVoiceBridge\updates` 在 helper 与新版进程间一致；7 参数 `--update-result … --await-handoff <ticket>` 可 round-trip；`FindProcess` 用 PID + 启动时间 ticks 校验；安装窗口「完成」按钮 `Click => Close()` 与叉都汇入 `OnClosing`。⚠️ **跨进程 WPF 窗口隐藏的实机目视验证未做**，需等 1.4.8 上线后由用户点更新实测。
- 历史专项：1.4.5 隔离 WPF 工程 7 组自定义提示词场景通过；1.4.6 三态按钮截图确认三色/尺寸/居中；1.4.7 r3 的 XAML 绑定与 `DataTemplate.Triggers` 静态结构核查通过。
- 未做：实机全新安装目视确认、真实跨机器升级（含 `fish-default` 的旧 `settings.json`）、同厂商多 Key 的实机点击；以及此前遗留的头像 CDN 实测、任务栏分组目视、词库焦点实机点击。都需要用户在桌面确认。
- **上线后独立公网友核**：`GET latest.json` 200 / 1093 字节，六字段齐全，`version=1.4.8`、`publishedAt=2026-11-09T22:59:27Z`、`url=…/WeChat-Voice-Bridge-V1.4.8-win-x64.zip`、`sha256=a4ca75b3…`、`size=89464301`，与本地实算一致，中文 notes 无乱码；ZIP / Setup 均 `HEAD 200`，`Content-Length` = 89,464,301 / 65,302,086。**下载级终验**：两个产物完整下载后重算 SHA-256 与字节数，与本地产物逐项一致。Muse 正式 `ok` 回执亦确认 ZIP 哈希链「申报=PC 实测=A1 落地」三方一致。
- 本机 `C:\Program Files\dotnet` 曾只剩运行时、无 SDK，已从微软下载 8.0.427 解压到 `%LOCALAPPDATA%\Temp\wvb-sdk\sdk` 用于构建，详见第 5 节。系统 `dotnet` 无 SDK 时，跑 `Test.ps1` / `Publish.ps1` 必须传 `-Dotnet $sdk`。

## 7. 下一步（按优先级）

1. ✅ **发 1.4.8 发布单（已完成）**：用户明确授权上线并指定联系 Muse → Muse `codex_20261109_234635_779950` 正式 `ok` 放行 → 发出唯一一条发布单 `codex_20261109_235238_857190`（`-Version 1.4.8`、`sha256=a4ca75b3…`、`package_size=89464301`、中文 notes）→ 流水线先 `info` 后正式 `ok`。未重发 1.4.7、未自行上传、未绕过通道。
2. ✅ **上线后公网复核（已完成）**：见第 6 节末条，`version=1.4.8`、六字段、`sha256`/`size` 与本地一致，ZIP/Setup 均 200 且长度一致。
3. **用户实机验证新引导窗口（唯一剩余重点）**：桌面快捷方式保持 1.4.7 r3，线上 1.4.8 严格大于桌面版 → 客户端应出现更新按钮 → 点更新，观察进度到 100% 后**新版主窗口不自动出现**；点「完成」或叉后安装引导消失、新版窗口打开。这是本轮唯一未做实机目视验证的部分，需用户本人确认。
4. `README.md` 仍偏旧（1.2 文案），需同步 1.4.7/1.4.8 的多人/多 API/多 Key/指南/受审计微信版本说明。
5. 其余遗留实机确认：头像升级恢复、词库编辑光标、更新窗口任务栏合并、同厂商多 Key 的实机点击。
6. ✅ 手工翻版来源：Muse 已给最终结论（A1 `auth.log` 的 `sudo cp`/`sudo mv`、文件名非流水线所用、现有日志无法指认操作者），已记入 WORKLOG 与第 1 节；不再追问，不来回翻清单。若用户想继续追查操作者，需另行授权。

> 1.4.6 **已上线**（发布单 `codex_20261109_023128_ac2ff2` 的更正回执 `status=ok`）。1.4.7 发布单 `codex_20261109_230425_438f4b` 因公网 `public_verify` 403 被拒并回滚，旧包**不再使用**；那份手工翻的 1.4.7 清单也已被 1.4.8 取代。**当前线上正式版 = 1.4.8**（发布单 `codex_20261109_235238_857190`，`status=ok`）。完整流水见 `WORKLOG.md`。

## 8. 关键文件地图

| 主题 | 路径 |
| --- | --- |
| 版本唯一来源 | `Directory.Build.props` |
| 用户可见更新日志 | `src/VoiceBridge.App/ReleaseHistory.cs` |
| 弹窗外壳 | `src/VoiceBridge.App/DialogShell.cs`、`ReleaseNotesDialog.xaml(.cs)` |
| 提示词卡片/选项 | `src/VoiceBridge.App/ContactCard.cs`、`AnnouncementDialog.cs` |
| 词库行/焦点 | `src/VoiceBridge.App/ReadingItem.cs`、`MainWindow.xaml`(ReadingRow)、`MainWindow.xaml.cs`(FocusReading/CommitReading) |
| 词库文本替换 | `src/VoiceBridge.Core/ReadingText.cs`、`worker/wechat_adapter.py`(`_apply_rules`) |
| 前缀拼接 | `src/VoiceBridge.Core/ContactPreferences.cs`、`MainWindow.Library.cs`(RoutedSpeech) |
| 会话准入/热移除 | `src/VoiceBridge.Core/SessionController.cs`、`MainWindow.Library.cs`(PublishRouting/RemoveContactClick) |
| 联系人索引/头像 | `src/VoiceBridge.WeChat/ContactSearchCache.cs`、`src/VoiceBridge.App/AvatarCache.cs`、`MainWindow.ContactIndexing.cs`、`MainWindow.Library.cs` |
| 微信进程只读 | `worker/audited_wechat_keys.py`、`src/VoiceBridge.WeChat/WeChatSession.cs`、`PythonFixtureSource.cs` |
| 更新助手 | `src/VoiceBridge.App/UpdateInstallationWindow.cs`、`UpdateProcess.cs`、`App.xaml.cs`(AwaitInstallationConfirmation)、`MainWindow.Updates.cs`、`src/VoiceBridge.Core/UpdateInstaller.cs` |
| 更新完成握手（1.4.8） | `src/VoiceBridge.Core/UpdateHandoff.cs`、`tests/VoiceBridge.Tests/UpdateHandoffTests.cs`；`--await-handoff <ticket>`、`ready`/`go` 信号固定写在 `%LOCALAPPDATA%\WeChatVoiceBridge\updates` |
| 发布/快捷方式 | `scripts/Publish.ps1`、`scripts/Install-DesktopShortcut.ps1` |
| 出厂服务/音色迁移与门禁 | `src/VoiceBridge.Core/ProviderMigration.cs`、`VoiceLibraryMigration.cs`、`src/VoiceBridge.App/LocalSettings.cs`(Load)、`MainWindow.Library.cs`(InitializeLibrary/EditProvider/RefreshLibrary)、`tests/VoiceBridge.Tests/ProviderMigrationTests.cs`、`VoiceLibraryMigrationTests.cs`、`scripts/Assert-CleanPayload.ps1`、`scripts/Verify-Source.ps1` |
| 厂商缓存与多 Key 启用 | `src/VoiceBridge.Core/ProviderActivation.cs`、`VoiceLibraryMigration.cs`(CacheVendor/UseActiveKey)、`SpeechProfiles.cs`(ProviderKind)、`MainWindow.Library.cs`(ActivateProviderClick/ActiveProviderFor)、`ProfileEditors.cs`(VoiceEditor 占位编辑)、`tests/VoiceBridge.Tests/ProviderActivationTests.cs` |
