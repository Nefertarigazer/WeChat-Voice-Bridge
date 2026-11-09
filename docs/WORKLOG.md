# 工作日志（按版本）

记录本次会话中实际完成、构建与验证过的改动。用户可见的精简版在 `src/VoiceBridge.App/ReleaseHistory.cs`；接手说明见 [HANDOVER-CURRENT.md](HANDOVER-CURRENT.md)。

---

## 1.4.5 · 2026-11-09（自定义提示词选中状态根治）

用户第三次反馈同一个问题：点「自定义」弹出窗口后，取消或点叉应恢复打开前的提示词，确定并添加后应直接选中新内容，但收起框一直显示「自定义」三个字。

**为什么 1.4.3 的修复没生效（如实记录）**：当时的隔离验证工程用的是**一个直接放在 StackPanel 里的裸 ComboBox**，并模拟 `combo.SelectedValue = "custom"`；真实主窗口里 ComboBox 在 `DataTemplate` 内、由 `ContentControl`/`ListBox` 生成，且用户点击会先设置 `SelectedItem`。两者行为不同：真实场景下 `SelectedValue` 的 TwoWay 绑定会把值**立即改回源值**，但 `SelectedItem`（以及收起框的 `SelectionBoxItem`）仍停在「自定义」项上。因此只靠事后 `Notify` / `UpdateTarget()` 永远拉不回显示。另外 1.4.3 的 `finally` 用了 `DispatcherPriority.ContextIdle`，会被主窗口的重复定时器饿死。

**本轮改法（让「自定义」永远不成为选中值）**

| 文件 | 改动 |
| --- | --- |
| `src/VoiceBridge.App/ContactCard.cs` | `AnnouncementChoiceId` setter 遇到 `custom` 直接 return（不再触发 `CustomRequested`）；删除因此不再使用的 `CustomRequested` 事件、`customRequestPending` 字段、`FinishCustomRequest()` |
| `src/VoiceBridge.App/MainWindow.Library.cs` | `CardFor` 移除该事件订阅；新增 `AnnouncementSelectionChanged`（用 `e.AddedItems` 判断动作项，而不是看已被绑定改回的 `SelectedValue`）；新增 `RestoreAnnouncementItem`，用 `SetCurrentValue` 同时把 `SelectedItem` 与 `SelectedValue` 改回真实选项；对话框改为 `Dispatcher.BeginInvoke` 默认 Normal 优先级；`SyncAnnouncementSelection` 改为按 `ItemsSource` 匹配并同样恢复两个属性 |
| `src/VoiceBridge.App/MainWindow.xaml` | 两个提示词 ComboBox 加 `SelectionChanged="AnnouncementSelectionChanged"` |

**验证（这次用贴近真实的工程）**：`D:\USERNAME\CodexScratch\Temp\wvb-announcement-rootfix-20261109` 链接真实 `ContactCard.cs` / `AnnouncementDialog.cs` / `DialogShell.cs`，用 `ContentControl` + `DataTemplate`（含 `x:Name="AnnouncementBox"` 与 TwoWay `SelectedValue`）渲染，逐字复制产品的两个处理器；模拟真实点选是设置 `SelectedItem`。跑了 7 组场景（取消/点叉/添加 × 初始无提示词/昵称说/消息+留言），日志全部通过：

- 点「自定义」的同时收起框已是打开前的选项（`无提示词` / `“小林”说+消息` / `消息+“小林”留言`）；
- 取消、点叉后 `shows 自定义=False`，`card` 与 `combo` 都保持原值；
- 添加并使用后 `combo` 与 `card` 都等于新 preset id，显示 `✧₍^˶- ˕ -˵^₎✧说+消息`；
- 输入框每次空白，对话框正常关闭。

`Test.ps1` 全量构建 0 警告 0 错误、141/141 C# + 54/54 Python PASS；`Verify-Source.ps1` PASS。发布到 `artifacts/win-x64-v1.4.5` 并更新桌面快捷方式。

---

## 1.4.4 · 2026-11-09（头像恢复、任务栏合并、词库光标）

用户一次提出：旧版本升级后已选联系人头像不自动显示；更新时任务栏多一个图标；词库编辑不自动出现光标；并要求把工作日志与交接文档写清楚。

**代码改动**

| 文件 | 改动 |
| --- | --- |
| `src/VoiceBridge.WeChat/ContactSearchCache.cs` | `Fingerprint` 只用 `contact.db` 主库，不再含 `-wal`；抽出 `TryLoad`；新增 `TryFind(session, peerId, ...)`，按会话 ID 精确查、不受 `TrySearch` 的 60 条上限影响 |
| `src/VoiceBridge.App/MainWindow.ContactIndexing.cs` | watcher `Filter` 由 `contact.db*` 改为 `contact.db`；重建间隔 15 秒 → 5 分钟；索引就绪后 `Dispatcher.InvokeAsync(RestoreMissingAvatars)` |
| `src/VoiceBridge.App/MainWindow.Library.cs` | 新增 `contactIndexCache` 字段与 `RestoreMissingAvatars`，对 `AvatarUrl` 为空且无磁盘缓存的卡片补 URL 并触发 `FetchAvatarAsync` |
| `src/VoiceBridge.App/ContactCard.cs` | 新增 `UpdateAvatar`，只更新 URL 不改姓名，复用 `changed` 落盘 |
| `src/VoiceBridge.WeChat/PythonFixtureSource.cs` | 新增 `CheckWeChatOccasionally()`，`QueryAsync` 里的微信进程存活检查由每轮（约 1 秒）降到最多每 10 秒一次 |
| `src/VoiceBridge.App/App.xaml.cs` | 窗口创建前 `SetCurrentProcessExplicitAppUserModelID("WeChatVoiceBridge")`，主程序 / 更新助手 / 重启后的新版共用一个任务栏按钮 |
| `src/VoiceBridge.App/MainWindow.xaml.cs` | `FocusReading` 改为在容器可视化树按名字找输入框、`Keyboard.Focus` + `IsVisibleChanged` 等待可见、最多重试 3 次；编辑光标仍在「读作」末尾 |
| `Directory.Build.props` | `1.4.3` → `1.4.4` |
| `src/VoiceBridge.App/ReleaseHistory.cs` | 新增 1.4.4 条目（4 条） |
| `tests/VoiceBridge.Tests/ContactIndexCacheTests.cs` | 新增用例：WAL 变动不使索引失效、第 80 行仍可 `TryFind` 找回头像 URL、主库变更仍失效 |
| `docs/user-guide.md`、`src/VoiceBridge.App/UserGuide.xaml.cs` | 头像自动恢复、词库编辑光标、更新窗口任务栏 |
| `docs/HANDOVER-CURRENT.md`、`docs/WORKLOG.md` | 新建（本文件与交接文档） |

**验证**：`Test.ps1` 全量 Release 构建 0 警告 0 错误、141/141 C# + 54/54 Python PASS；`Verify-Source.ps1` PASS；发布物 `1.4.4.0`、Python 运行时与两个 wheel 齐全、无 `.db`/WAL/SHM/`settings.json`/密钥残留、程序集含本轮新增符号；桌面快捷方式回读确认指向 `artifacts/win-x64-v1.4.4`，旧目标消失。

**未验证 / 需用户确认**：头像恢复、词库光标、任务栏分组需实际桌面确认。

---

## 1.4.3 · 2026-11-08（两轮）

### 第二轮：提示词、下拉菜单、热移除、词库覆盖提示词

| 文件 | 改动 |
| --- | --- |
| `src/VoiceBridge.App/AnnouncementDialog.cs` | 输入框每次空白 |
| `src/VoiceBridge.App/ContactCard.cs` | getter 不再返回 `custom` 哨兵；旧数据用 `AnnouncementChoice.CurrentId` 生成真实选项；预设 label 改为 `提示词+消息` / `消息+提示词`；新增 `IsAddNew`；`RefreshAnnouncements` 按 ID 增删而非只截尾部 |
| `src/VoiceBridge.App/MainWindow.Library.cs` | `EditCustomAnnouncement` 同步 `FinishCustomRequest` + 新增 `SyncAnnouncementSelection`（`BindingOperations...UpdateTarget()` 强制回读，修复下拉框卡在"自定义"）；`RemoveContactClick` 放开监听中限制；`RoutedSpeech.Resolve` 对提示词应用词库规则 |
| `src/VoiceBridge.App/MainWindow.xaml` | 两个提示词 ComboBox 加 `x:Name="AnnouncementBox"`；`AnnouncementMenuTemplate` 给「自定义」加绿色 ＋ / 加粗 / 分隔线 |
| `src/VoiceBridge.App/MainWindow.Interaction.cs` | `RemovePeerButton` 不再被监听态只读限制 |
| `src/VoiceBridge.Core/SessionController.cs` | `Start` 新增可选 `admitFilter`，`Admit` 中应用；`Session.AdmitFilter` |
| `src/VoiceBridge.Core/ContactPreferences.cs` | `Prepare` 加可选 `Func<string,string>? renderPrefix` |
| `src/VoiceBridge.Core/ReadingText.cs` | **新建**：复刻 worker `_apply_rules` 的 both/inside/standalone、数字护栏、长词优先；`ignoreScope` 供提示词使用 |
| `src/VoiceBridge.App/MainWindow.xaml.cs` | `StartClick` 传按路由快照判断的准入过滤器；`PreviewOperationAsync` 对正文与提示词同样应用词库规则 |
| `tests/VoiceBridge.Tests/ReadingTextTests.cs` | **新建**，7 项（后增至 8 项） |
| `docs/user-guide.md`、`UserGuide.xaml.cs`、`ReleaseHistory.cs` | 同步说明与 1.4.3 条目 |

验证：构建 0/0；`Test.ps1` 139 C# + 54 Python PASS；`Verify-Source.ps1` PASS；隔离 WPF 工程 `D:\USERNAME\CodexScratch\Temp\wvb-announcement-fix-20261108` 链接真实 `ContactCard.cs`/`AnnouncementDialog.cs`，日志确认输入框为空、取消回到 `“小林”说+消息`、添加后显示 `✧...✧说+消息`、旧数据返回 `current`；下拉菜单截图确认绿色 ＋ 与分隔线。

### 第一轮：更新弹窗浅绿色外框

`DialogShell.cs`、`ReleaseNotesDialog.xaml(.cs)` 改为透明窗口（`AllowsTransparency=True`、`Background=Transparent`），外层 16 DIP 透明，卡片 `#FFFEFB` + `#DCE3DF` 细边框 + 中性阴影；移除 WindowChrome，拖动改 `DragMove()`，补 Alt+F4。`docs/guide-ui-validation.md` 追加后续变更说明。

验证：构建 0/0；隔离工程 `D:\USERNAME\CodexScratch\Temp\wvb-dialog-clean-20261108` 截屏对比（含 `before_after.png`），拖动实测位移 80/45 DIP。

### 发布与快捷方式（同日）

- `scripts/Publish.ps1`：修复 `Expand-Archive -Force` 清空已缓存 Python 目录触发宿主 safe-delete 拦截；`worker/deps` 不再整目录删除。现在可重复发布到同一目录。
- 发布 `artifacts/win-x64-v1.4.3`（旧快捷方式原指向 `preview-1.4.2-ui2`，导致用户测到的仍是旧行为），用 `Install-DesktopShortcut.ps1` 更新桌面快捷方式。
- 顺手加固：词典「单独出现时」/「句中出现时」规则在拼出来的提示词短语里会漏替换，`ReadingText.Apply` 新增 `ignoreScope`，提示词一律包含匹配，正文保留原三档语义。

---

## 版本与发布状态对照

| 版本 | 内容 | 状态 |
| --- | --- | --- |
| 1.3.3 | 线上正式版 | 已上线（早于本次会话） |
| 1.4.0–1.4.2 | 历史开发快照，见 `NEXT-AGENT-HANDOVER-1.4.md` | 本地 |
| 1.4.3 | 弹窗外观 + 提示词/词库/热移除 + 快捷方式 | 本地试用，`artifacts/win-x64-v1.4.3` |
| 1.4.4 | 头像恢复、任务栏合并、词库光标、文档 | 本地试用，`artifacts/win-x64-v1.4.4` |
| **1.4.5** | **自定义提示词选中状态根治** | **本地试用，`artifacts/win-x64-v1.4.5`；桌面快捷方式指向此处** |

本次会话**没有**向 Muse 发布任何版本，也没有生成新的 Setup.exe / ZIP。
