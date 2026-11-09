# 微信语音桥 · 当前交接（2026-11-09，v1.4.6）

> 本文是**最新接手入口**。旧的 `NEXT-AGENT-HANDOVER-1.4.md` 是 1.4.0 时期的历史快照，`PROJECT-HANDOVER.md` 更早（1.2），都不要当作现状。
> 完整改动流水见 [WORKLOG.md](WORKLOG.md)，用户可见的版本说明在 `src/VoiceBridge.App/ReleaseHistory.cs`。

## 1. 一句话现状

源码停在 **1.4.6**（`Directory.Build.props` 是版本唯一来源）。发布产物 `artifacts/WeChat-Voice-Bridge-V1.4.6-win-x64.zip` 与 `artifacts/WeChat-Voice-Bridge-V1.4.6-Setup.exe` 已生成，按用户指令通过 Muse 通道推到线上（本代理不自行上传）。**线上发布前的版本是 1.4.3**。

⚠️ **桌面快捷方式刻意保持在 `artifacts/win-x64-v1.4.5`，不要改**：用户要在 Muse 上线后亲自从桌面端点「检查更新 → 下载 → 重启安装」体验完整自升级流程。任何后续构建都不要调用 `Install-DesktopShortcut.ps1`，除非用户明确要求。

## 2.0 最新一轮（1.4.6）：更新按钮三态配色

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
- 发布上线需用户授权；固定 feed `https://iceland.de5.net/voice-bridge/latest.json`，六字段契约不变。本地打包 ≠ 上线。
- 用户重视先交付可用版本，别为了完善程度做无关扩展或反复跑长测试。

## 5. 命令速查

```powershell
$sdk = 'C:\Users\USERNAME\AppData\Local\Temp\wvb-sdk-builds\dotnet.exe'
$py  = 'D:\OneDrive\文档\ChatGPT\微信语音\artifacts\win-x64-live\runtime\python\python.exe'

# 构建 / 全量测试 / 静态边界
& $sdk build 'D:\OneDrive\文档\ChatGPT\微信语音\WeChatVoiceBridge.sln' -c Release
& 'D:\OneDrive\文档\ChatGPT\微信语音\scripts\Test.ps1' -Dotnet $sdk -Python $py
& 'D:\OneDrive\文档\ChatGPT\微信语音\scripts\Verify-Source.ps1'

# 发布到独立目录并更新桌面快捷方式（不会强关运行中的程序）
& 'D:\OneDrive\文档\ChatGPT\微信语音\scripts\Publish.ps1' -Dotnet $sdk `
    -OutputDirectory 'D:\OneDrive\文档\ChatGPT\微信语音\artifacts\win-x64-v1.4.6' `
    -ZipPath 'D:\OneDrive\文档\ChatGPT\微信语音\artifacts\WeChat-Voice-Bridge-V1.4.6-win-x64.zip'
& 'D:\OneDrive\文档\ChatGPT\微信语音\scripts\Build-Installer.ps1' `
    -PayloadDirectory 'D:\OneDrive\文档\ChatGPT\微信语音\artifacts\win-x64-v1.4.6'
# 不要运行 Install-DesktopShortcut.ps1：用户要保留桌面 1.4.5 以体验自升级。
```

`Publish.ps1` 会复用 `artifacts/build-tools` 里已校验的 Python 3.12.10 NuGet 包与 sqlcipher3/zstandard wheel；`-ZipPath` 可选，本轮未用。

## 6. 验证基线（本轮）

- Release 全量构建：**0 警告 0 错误**。
- `Test.ps1` 全量：**141/141 C# PASS + 54/54 Python PASS**（worker 本轮未改，仍复跑确认）。
- `Verify-Source.ps1`：**PASS**。
- **1.4.5 专项 UI 验证**：隔离 WPF 工程 `D:\USERNAME\CodexScratch\Temp\wvb-announcement-rootfix-20261109`，`ContentControl` + `DataTemplate` 渲染、链接真实源码、模拟真实 `SelectedItem` 点击，7 组场景全部通过（含 `shows 自定义=False`）。
- **1.4.6 专项 UI 验证**：`D:\USERNAME\CodexScratch\Temp\wvb-update-row-20261109` 截图确认三态三种颜色、尺寸一致、上下居中、无文字换行。
- 发布物检查：ZIP 内 exe/程序集 `1.4.6`、1621 项、Python 运行时与两个 wheel 齐全、无 `.db`/WAL/SHM/`settings.json`/密钥残留、程序集含本轮全部修复符号；ZIP 实算 SHA-256 与字节数见 WORKLOG 1.4.6 段。
- 未做：实机真实微信头像 CDN 实测、更新流程真实跨进程任务栏分组的目视确认、词库焦点修复的实机点击验证。这些都需要用户在实际桌面确认。

## 7. 下一步（按优先级）

2. 用户实机确认头像升级恢复、词库编辑光标、更新窗口任务栏合并三项。
3. 若用户授权发布：按 `docs/update-release.md` / `NEXT-AGENT-HANDOVER-1.4.md` 第 6 节的 Muse 通道，出新 ZIP/Setup.exe，核验 hash 后再推 feed。
4. `README.md` 仍偏旧（1.2 文案），需同步 1.4.5 的多人/多 API/指南/受审计微信版本说明。

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
| 更新助手 | `src/VoiceBridge.App/UpdateInstallationWindow.cs`、`UpdateProcess.cs`、`MainWindow.Updates.cs` |
| 发布/快捷方式 | `scripts/Publish.ps1`、`scripts/Install-DesktopShortcut.ps1` |
