# 微信语音桥 · 当前交接（2026-11-10，v1.4.8 已上线）

> **新模型第一入口是 [PROJECT-BRIEF.md](PROJECT-BRIEF.md)**：自包含的项目总览、环境命令、发布纪律、不可回退的红线、当前任务与准绳。本文是细节交接，逐版本流水见 [WORKLOG.md](WORKLOG.md)，当日执行记录见 `.workbuddy/memory/2026-11-10.md`。
> 旧的 `NEXT-AGENT-HANDOVER-1.4.md` 是 1.4.0 时期的历史快照，`PROJECT-HANDOVER.md` 更早（1.2），都不要当作现状。
> 用户可见的版本说明在 `src/VoiceBridge.App/ReleaseHistory.cs`。

## 0. 当前正在做的任务（2026-11-10 00:45 GMT+1 用户新指令，进行中）

**A. 合并微信多版本适配（wx-multi）**：成果包 `wx-multi-handoff\package-v1\`（已由 Muse 在 `c917c6c` 提交进仓库）。合并入口是包内 `README.md`，**清单以 `docs/review-crouton.md` §4 为准绳**，修复说明看 `docs/fix-round1.md`（R1–R8 已修，自检 58 项）。硬要求：① 严格支持 4.1.11–4.2.0.13，范围外走 `unsupported_version` 通道，不静默失败/不乱读；② 密钥掩码与原切片逐字节一致、不改原切片行为，58 项等价性自检合并后仍成立；③ 不回退 1.4.7/1.4.8 三条已上线改动。`WeChatSession.cs` 按 review §2.2：基底 impl 版 + 并入 discovery 版三常量三方法。

**B. 更新页面 UI 优化**：删掉安装进度页「点确定才会进入更新应用」这类啰嗦解释文案（只删文案、不改 1.4.8 行为）；优化下载完成后绿色安装按钮的上下居中与「版本更新文字 + 按钮」区域美观；**保留截图**。

**C. 流程**：合并完先 `dotnet build` 必须 0 报错；**先不打包**，等用户检查效果合格后再打包、再推新；告诉用户新版软件位置；**桌面快捷方式先不用更新**。每完成一部分立刻回写本文、WORKLOG 与当日 memory 并 commit。

> 阶段进度与已完成项见 `WORKLOG.md` 最新段与 `.workbuddy/memory/2026-11-10.md`（中断后据此接上）。

## 1. 一句话现状

源码与**线上正式版均为 1.4.8**（`Directory.Build.props` 是版本唯一来源）。在 1.4.7 的「不预置服务/密钥/音色、删 API 保留厂商名、同厂商多 Key 自选启用」之上，新增**更新完成后由用户决定何时打开新版**：进度到 100% 不再自动弹出新版，必须点「完成」或关闭安装引导窗口。发布单 `codex_20261109_235238_857190` 已获 Muse 正式 `ok`，ZIP / Setup / `latest.json` 均已上线并经我方独立公网友核。

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
