# 工作日志（按版本）

记录本次会话中实际完成、构建与验证过的改动。用户可见的精简版在 `src/VoiceBridge.App/ReleaseHistory.cs`；接手说明见 [HANDOVER-CURRENT.md](HANDOVER-CURRENT.md)。

---

## 1.5.0 · 2026-11-10（API 配置简化 + 音色失效语义 + 凭据指针修复 + 凭据库自动清扫）

> 状态：**全部完成并通过自验，待用户检视后决定是否打包推新**。不打包、不推线上、不更新桌面快捷方式。应用位置：`src\VoiceBridge.App\bin\Release\net8.0-windows\WeChatVoiceBridge.exe`。

### 用户要求（四条）

1. **API 配置界面优化**：同厂商多 Key 取消「未启用/待用/已启用/使用中」括号小字；右侧「启用/编辑/删除」多按钮合并为最右一个白色「启用」按钮（启用后绿色「使用中」+ 行首小绿点，未启用灰点）；「编辑/删除」并入「⋮」竖排三点菜单；全面删除面向开发者的说明性文字（「已启用 Fish Audio 2」「其他 Key 保持待用」「每款软件不预置任何服务密钥」等）。
2. **音色消失 bug**：新增同厂商 API 后首页联系人音色消失。规则：API 改动不影响所选音色时（增/切换/删非关键 Key）一律不消失、不换样式、不弹任何提示；仅当音色所属厂商的 Key **全部**删除时，音色变灰色斜体、点「开始」提示「音色已失效，请重新选择」、下拉框里名字仍可见但不可点选，仅有效音色可选。
3. **凭据指针覆写事故修复**：当日新构建启动后重写 settings.json 与 fish-keys.dpapi，Fish 服务 `CredentialId` 指向 32 位空壳凭据（`speech-2466c4b1…`）→ 401 试听失败。修复要求：迁移/启动加载**原样保留**「服务→凭据」映射，禁止占位值覆写；指针恢复到真钥匙条目；自验 = 重启后指针不变 + 实际试听成功。
4. **凭据库自动清理规矩**：`fish-keys.dpapi` 只保留仍被引用条目（引用 = 任何服务 `CredentialId` + 顶层 `FishKeyName` + 联系人/试听凭据覆盖）；两个时机：①启动加载并完成全部配置迁移**之后**清扫（顺序不能反，防误删迁移目标）；②软件里删除服务时连带清掉因此无引用的条目；本次会话新建、未挂到服务的条目豁免当次清扫、下次启动再判；只删无引用条目，删几条记数到日志。

### 改动清单

- **UI（要求 1）**：`MainWindow.Library.cs` 的 `ProviderRow` 简化为 `Id/Name/Kind/IsActive`；行模板 = 左侧 9px 圆点（`#AEAEB2` 灰 / `#1D9A5B` 绿，`IsActive` 触发）+ 名称 + 白色「启用」按钮（`App.xaml` 新 `ProviderEnableButton`，ControlTemplate 内 Veil 覆盖层实现 hover，绿色态 hover 不褪色）+「⋮」按钮（`ProviderMoreClick` 动态 ContextMenu = 编辑/删除）；`ActivateProviderClick` 去掉 `NeedsActivation` 门槛与提示文案；删除「我的音色」「语音服务」脚注与 `VoiceHint` 块。
- **音色语义（要求 2）**：`VoiceLibraryMigration.ActiveKeyFor` 加同厂商 fallback（厂商还有任意 Key 即可用；全部删光才失效）+ `IsUsable`；`MainWindow.Library.cs` `RefreshLibrary` 重写为**就地同步** `SyncVoiceChoice`（不再 `voiceChoices.Clear()` —— 清空重建导致 WPF `Selector` 选中态重置，是音色消失的根因）；`ContactCard.cs` `VoiceChoice` 由 record 改为可变类 + `INotifyPropertyChanged`；`MainWindow.xaml` 新 `VoiceChoiceItemTemplate`（失效灰色斜体）与 `VoiceChoiceItemStyle`（`IsEnabled` 绑 `Usable`，名字保留在下拉但不可点选）；三处音色 ComboBox 切换到该模板。错误文案统一为「音色已失效，请重新选择。」；UserGuide 两处同步改写。
- **凭据指针（要求 3，根因两处）**：① `ProfileEditors.Save` 旧逻辑每次输入新密钥都铸 `"speech-" + Result.Id` 新名、把用户真钥匙名顶掉 → 改为仅在无既有名时铸名，否则保留 `Result.CredentialId`；② `KeyStore.Load` 在 `fish-keys.dpapi` 不可读时曾落入 legacy 导入、整库覆写 → 改为**已存在就绝不写**（读不出返回空、文件原样）。`KeyStore.Replace` 新增为唯一受护栏的写入口（空名/空密钥永不落盘）。`EditProvider` 改走 `KeyStore.Replace`。
- **凭据清扫（要求 4）**：新 `src/VoiceBridge.Core/CredentialSweep.cs` 纯函数（`ReferencedNames` 收集三类引用源；`UnreferencedEntries` 出删除名单，引用与会话新建永不入选）；`LocalSettings.KeyStore.Load(out createdThisLoad)` 上报 legacy 导入新建名；`MainWindow.xaml.cs` 新 `sessionNewKeys` 会话豁免集（`AddKeyClick`/`EditProvider` 新建名登记，`DeleteKeyClick` 注销）+ `SweepCredentials(when, providers, keyName, contacts)`（删无引用条目 → `KeyStore.Save` → `AppLog.Write` 记条数）；启动时在 `ReadStoredSettings()`（全部迁移）与 `KeyStore.Load()` **之后**清扫一次（`"启动"`）；`DeleteProviderById` 在摘除服务后清扫一次（`"删除服务"`）。新 `AppLog.cs` → `%LOCALAPPDATA%\WeChatVoiceBridge\app.log`（只记条数与事件名，128KB/200 行封顶，写失败不阻塞）。`DeleteKeyClick` 守卫从「被启用的服务使用」收紧为「被任何服务引用」。
- **版本**：`Directory.Build.props` → 1.5.0；`ReleaseHistory.cs` 新 1.5.0 条目（四项：界面精简、音色不无故消失、凭据指针修复、凭据库自动清理）。
- **工具**（`tools/`，不入发布物）：`KeyStoreInspector`（只读列条目名/长度/指纹）、`CredentialRepair`（`probe`/`repair <name>`/`verify`，本轮加 `inject <name>` 塞测试孤儿条目）、`UiSmokeTest`（UIA 驱动真实窗口：高级→试听这段文字→首页→读「试听完成。」）。

### 事故记录（凭据指针）

当日早间构建启动后把 `settings.json`/`fish-keys.dpapi` 重写、服务指针被占位值顶掉。用指纹分组 + 真实 Fish 调用辨认真钥匙：51 位 `fp=68BD1117` 组（`默认` 等 5 份拷贝）HTTP 200 为真钥匙；`fp=240975F2` 与 32 位空壳 401。`CredentialRepair repair speech-d1ee2ca3106c44d1af5b91b674944451` 恢复指针（备份 `settings.json.bak-20261110-112909`），并按上文修掉两处根因。教训：修复指针必须在应用**未运行**时做——旧构建退出时会把内存里的坏指针写回。

### 验证（全部通过）

- `dotnet build -c Release` 0 警告 0 错误；产物 `WeChatVoiceBridge.exe` 版本 **1.5.0.0**。
- `scripts/Test.ps1` PASS（.NET + Python）；直接 `dotnet test`（含 WVB_PYTHON）**211/211**（205 旧 + 6 新清扫测试）；`Verify-Source.ps1` PASS；58 项等价性自检 PASS。
- **清扫自验（用户指定）**：`CredentialRepair inject orphan-sweep-c/d` 塞两条无引用条目 → 重启应用 → 钥匙库 16 条只剩被引用的 `speech-d1ee2ca3…` 一条，两条孤儿被清；`app.log` 记「凭据清扫（启动）：删除 2 条无引用条目。」（首轮全库清扫记「删除 15 条」）。
- **试听自验**：`UiSmokeTest` 驱动真实窗口（高级→试听这段文字）→ 提示「**试听完成。**」exit 0；试听后 `settings.json` 指针不变（`CredentialId`/`FishKeyName` 均 `speech-d1ee2ca3106c44d1af5b91b674944451`）。
- 清扫删除服务路径由 `CredentialSweepTests.DeletingAServiceSweepsOnlyTheEntriesItOrphaned` 等 6 例覆盖（共享指针只清孤儿、待用 Key 不清、会话新建豁免、空白指针安全）。

### 待用户裁决

- 用户检视 `src\VoiceBridge.App\bin\Release\net8.0-windows\WeChatVoiceBridge.exe`；合格后再谈打包推新（届时走 Muse）。

---

## 1.4.9 · 2026-11-10（wx-multi 多版本适配合并 + 更新页 UI 优化）

> 本段是**断点续做的活文档**：每完成一部分就在这里追加，接手者先看这里与 `.workbuddy/memory/2026-11-10.md`。用户本轮指令原文要点见 `PROJECT-BRIEF.md` §7。

### 用户本轮要求（逐条）

1. 先完善文档型内容，保证没读过聊天记录的新模型仅凭文档+代码就能熟悉项目与要求；随时维护交接文档与工作执行记录，每完成一部分就及时留下文案记录。
2. 先查看 Git 是否上传好，没问题再继续。
3. 把 `wx-multi-handoff\package-v1\` 合并进 `WeChatVoiceBridge.sln`：先通读包内 `README.md`；**合并清单以 `docs/review-crouton.md` §4 为准绳**；修复说明看包内 `docs/fix-round1.md`。
   - 只支持微信 **4.1.11–4.2.0.13**，范围外必须走包内已实现的 `unsupported_version` 通道，不许静默失败或乱读数据。
   - 密钥掩码与原切片逐字节一致，不许改动原切片行为；**58 项等价性自检合并后仍要成立**。
   - 不许回退 1.4.7 / 1.4.8 已上线改动：无出厂默认 API、无内置音色、1.4.8 安装引导（装完不自动弹新版，点「完成」或关闭引导才打开）。
4. 更新进度条页面删掉「点确定才会进入更新应用」这类啰嗦解释文案；下载后绿色安装按钮上下不够居中、版本更新文字与按钮区域不够美观，继续优化布局；**做好保留截图**。
5. 合并完先 `dotnet build` 必须编译通过无报错；**先不用打包**，用户觉得合格后打包、再推新；告诉用户新版软件位置；**桌面快捷方式先不用更新**。

### 已完成

- **Git 核对**：`master` = `origin/master` = `git ls-remote` = `c917c6c26b7f9aa6d2198fa58d05de1d7ce073a5`，0 ahead / 0 behind，工作区干净。`c917c6c` 是 Muse 代提交的 release-state snapshot，把整个 `wx-multi-handoff/package-v1/` 纳入了仓库。
- **文档基线**：新建 `docs/PROJECT-BRIEF.md`（自包含项目总览：项目结构、环境命令、发布纪律、版本状态、三条不可回退红线、技术约定、当前任务准绳、接手检查清单）；`HANDOVER-CURRENT.md` 顶部加「§0 当前正在做的任务」并指向总览；建立当日 `.workbuddy/memory/2026-11-10.md`。
- **通读成果包**：`README.md`、`merge-notes-for-workbuddy.md`、`docs/review-crouton.md`（含 §2.2 合并配方与 §4 清单）、`docs/fix-round1.md`（R1–R8 修复对照）、`docs/impl-summary.md`（实现总结与 58 项构成）、自检脚本全文、项目与包内 `WeChatSession.cs`、`worker/audited_wechat_keys.py`。
- **差异核对（关键结论）**：包内 `impl/` 的 4 个文件相对当前项目**仅多出 version 透传与错误码映射**（`wechat_adapter.py` 11 行、`main.py` 9 行、`PythonFixtureSource.cs` 2 行、`WeChatDirectory.cs` 2 行），直接替换不会回退 1.4.x 改动；`worker/audited_wechat_keys.py` 当前是 8.7KB 的「原切片」，包内是 35KB 的多版本化重写。自检脚本硬编码 `ROOT/slice/worker/...` 与 `ROOT/out/impl/worker/...`，成果包里没有这两个目录，合并后必须把自检改造成指向项目真实文件的形式才能长期复跑。

### 合并主体已完成（本段第一小节）

- **落地文件**：`worker/audited_wechat_keys.py`（35KB 多版本化重写，替换原 8.7KB 切片）、`worker/wechat_adapter.py`（`WeChatStore(..., version=...)` → `WeChatDB(version=version)`）、`worker/main.py`（start 包透传 version + 显式捕获 `UnsupportedVersionError` → `code="unsupported_version"` 并保留中文 message）、`src/VoiceBridge.WeChat/PythonFixtureSource.cs` 与 `WeChatDirectory.cs`（start 包 `version` 字段 + code switch 新增中文「不支持此版本」文案）。
- **原切片冻结**：`tests/wx-multi/slice/audited_wechat_keys.py`（8,704 字节，逐字节复制自合并前的 `worker/audited_wechat_keys.py`，**禁止编辑**，作为 58 项等价性基线）。
- **自检落位**：`tests/wx-multi/test_audited_wechat_keys.py` 由包内自检改造，路径改为指向**真实生产文件**：`IMPL=worker/audited_wechat_keys.py`、`MAIN=worker/main.py`、`SLICE=tests/wx-multi/slice/...`，这样 58 项门禁长期守护实际 shipped 代码，而不是包内副本。
- **`WeChatSession.cs` 按 review §2.2 合并**：基底 impl 版（`MinSupportedVersion=4.1.11`、`MaxSupportedVersion=4.2.0.13`、`IsSupportedVersion`、`Version` 字段、`Bind` 区间判定与中文拒绝文案），并入 discovery 版 `RootAnchorName`/`ScanMaxDepth=6`/`ScanBudget=20s` 与 `DiscoverRootByScan`/`EnumerateFixedDrives`/`ScanDirectory`，`DiscoverRoot()` 末尾改 `return DiscoverRootByScan();`。**未使用 discovery 的单版本 `SupportedVersion` 锁**。另新增只读 `DetectClientVersion()`（不碰数据库/内存/账号目录）供宿主版本门控使用。
- **R5 编译期修复（包内未编译验证的两处 CS8602）**：`module` 可能为 null、`version.ToString()` 可能为 null。改为 `if (module is null || !IsSupportedVersion(parsed)) throw ...`，把「读不到版本」也归入明确拒绝；通过后用 `parsed!.ToString()`。行为不变，绝不静默乱读。
- **R6 破坏点同步**：`tests/VoiceBridge.Tests/ContactIndexCacheTests.cs` 两处 5 参数构造（含一处 target-typed `new(...)`）补 `"4.2.0.13"` 版本参数；全仓 grep 确认无其它 `new WeChatSession(` / `SupportedVersion` 引用。
- **复验结果**：
  - `tests/wx-multi/test_audited_wechat_keys.py` **58/58 PASS**（`ALL SELF-CHECKS PASSED (58 checks)`），含 R1 错误映射、R2 掩码单值、R3 空版本、R4 严格解析、49 项 slice 等价性（常量、HMAC 200 组、合成内存场景 A/B/C、拒绝版本零句柄）。
  - ⚠️ **必须用项目内置 Python 3.12.10 跑**：`artifacts/win-x64/runtime/python/python.exe tests/wx-multi/test_audited_wechat_keys.py`。系统 Python 3.13 会被 `main.py` 的 `sys.version_info[:2] != (3, 12)` 检查提前挡下，导致 R1 误报失败（第一次跑就是踩了这个坑，非代码问题）。
  - `python -m py_compile` 三个 worker 文件全过；`dotnet build WeChatVoiceBridge.sln -c Release` **0 警告 0 错误**。

### 版本门控弹窗与目录兜底（已落实）

- **范围外版本弹窗**：新增 `src/VoiceBridge.App/VersionGate.cs`（`ShowUnsupportedVersion(detected, min, max)`），用既有 `DialogShell` 风格弹窗，显示「检测到的微信版本：X」+ 支持区间 `4.1.11`–`4.2.0.13` + 两按钮「更新微信」（打开官网下载页）/「退出应用」。`App.xaml.cs` 在 `ContentRendered` 时若宿主不在更新交接模式下就先调 `WeChatSession.DetectClientVersion()`，范围外则弹窗，点「退出应用」设 `ShutdownMode` 并 `Environment.Exit(0)`。
- **目录兜底（已满足）**：MainWindow 数据目录行本就有「浏览…」+ `OpenFolderDialog` + 自动保存，`WeChatSession.DiscoverRoot()` 扫描失败时回退到手动选择，无需新增代码。

### 更新页 UI 优化（已落实 + 截图）

- **删啰嗦文案（只删文案、1.4.8 行为不动）**：`UpdateInstallationWindow.cs` 两处解释（进度中「安装完成后，点『完成』或关闭本窗口才会打开新版本」、完成态「点『完成』或关闭本窗口后，才会打开新版本」）改为中性短句；`UpdateProcess.cs` 失败回执与 100% 进度文案去掉「等待确认」「暂未确认启动成功」等重复说明。
- **绿色按钮 + 版本文字重新布局**：`MainWindow.xaml`「软件更新」行用 `DockPanel` 把状态点 + 两行文字（`UpdateVersion` 主 + `UpdateSubtext` 副）固定在左侧、按钮固定 `MinHeight=36` 钉在右侧，整行 `VerticalAlignment=Center`；三种状态（灰/薄荷描边/实心绿）共用同一套 36 高度、`Padding=20,0,20,0`、`FontSize=14`，文字垂直居中。修复了一处历史隐患：`App.xaml` 默认按钮模板的 `ContentPresenter` 显式绑定 `VerticalContentAlignment`（否则隐式 TextBlock 样式会把字压成深色、破坏白字）。
- **「版本说明」区块重设计（用户第二轮反馈：按钮下面那段文字不美观）**：原先 `UpdatePackage` 行就是「`v1.4.9 · 89.5 MB` + 一大段密排正文」直接堆在按钮下方，无分段、无层次。改为：
  - 尺寸行右侧加灰色小标签「更新内容」，让这块读起来是一个带标题的小节，而不是状态行的附属段落；
  - 说明放进浅色圆角内嵌面板（`#F6F9F7` 底 / `#E7EEE9` 边 / 圆角 10 / 内边距 12,10）；
  - 每条更新一行、左侧 5px 绿色圆点 + 12px 正文 + `LineHeight=19`，由 `MainWindow.Updates.cs` 新增的 `FillNotes()` 按 `\n` 拆分生成，长说明可扫读；
  - 去掉内层 `MaxHeight=132` 滚动框：整页本来就在 `ContentScroller` 里，硬切只会把最后一行截成半截。说明为空时连面板一起隐藏（`UpdateNotesBox`）。
  - 原 `UpdateNotes`（TextBlock）已由 `UpdateNotesPanel`（StackPanel）+ `FillNotes()` 取代，全仓无其它引用。
- **第三轮微调（用户看图后）**：① 删掉 `v1.4.9 · 89.5 MB` + 「更新内容」标签整行（`UpdateSize` 控件移除，体积信息改挂 `UpdatePackage.ToolTip`，不再占版面）；说明面板现在是该区块唯一内容、去掉上边距。② **「状态点全变绿」是预览工具的 bug，不是应用回归**——真机三色一直是对的：`MainWindow.xaml.cs` 里 `IdleDot=#AEAEB2`（灰，无更新）/ `WarnDot=#B2760A`（琥珀，发现新版）/ `LiveDot=#1D9A5B`（绿，可安装），`RenderUpdate()` 按 `manifest/verifiedUpdate` 三态赋值。之前截图三态都填了 `Positive` 绿色，已改为按真实色值渲染，并加画「已是最新」灰点态做对照。
- **截图（已生成，供用户检视）**：
  - `artifacts/screenshots/update-ui-states.png` —— 三态并排（已是最新 / 发现新版本 / 下载完成绿色按钮）。
  - `artifacts/screenshots/update-install-green.png` —— 绿色按钮特写：白字、垂直居中、行内点+两行文字对齐。
  - `artifacts/screenshots/update-panel-with-notes.png` —— **按钮 + 下方版本说明整块**，三态上下对照（灰点已是最新 / 琥珀点发现新版 / 绿点可安装），按真实 660 内容宽渲染，用于判断「按钮与下面文字」是否协调。
  - 预览工具：`tools/UpdatePanelPreview/`（复用 `App.xaml` 真实样式资源，按 660 宽离屏渲染；`tools/UpdateNotesPreview/` 是更早那版「更新区 + 版本介绍对话框」合成图工具）。截图属本地产物、被 `.gitignore` 忽略，不入库；工具源码入库。

### 收尾：版本推进到 1.4.9（未发布）

- `Directory.Build.props` 版本 `1.4.8` → `1.4.9`（版本唯一来源）；`ReleaseHistory.cs` 顶部新增 `1.4.9` 条目（多版本适配 + 更新页优化，并显式声明不回退 1.4.7/1.4.8 三条改动）。复跑 `dotnet build -c Release`：**0 警告 0 错误**，DLL 内嵌版本字符串已为 `1.4.9`。
- **新版软件位置（仅供检视，尚未打包/上线）**：`src/VoiceBridge.App/bin/Release/net8.0-windows/WeChatVoiceBridge.exe`（同目录含 `WeChatVoiceBridge.dll` 与全部依赖）。桌面快捷方式**保持 1.4.7 r3 不动**。

### 全量回归（合并 + UI 全部完成后）

- `scripts/Test.ps1`：构建 0 警告 0 错误；**C# 197/197 PASS**；Python **54 通过**（41 个需 Windows 内置 Python+SQLCipher 的 live 测试按约定跳过，属正常）。
- `scripts/Verify-Source.ps1`：**PASS**（静态边界 + 明显密钥扫描）。
- `tests/wx-multi/test_audited_wechat_keys.py`（用 `artifacts/win-x64/runtime/python/python.exe`，即内置 Python 3.12.10）：**58/58 PASS**，含 R1 错误映射、R2 掩码单值、R3 空版本、R4 严格解析、49 项 slice 等价性。
- 结论：**未回退 1.4.7/1.4.8** 任一改动（无出厂默认 API、无内置音色、1.4.8 安装引导需用户确认才显示新版）。

### 用户裁决后下一步（尚未做）

- [ ] 用户检视构建产物 + 截图，认为合格后 → 打包：`scripts/Build-Installer.ps1` / `Publish.ps1` 生成 1.4.9 的 ZIP + Setup，并跑出厂门禁 `Verify-Source.ps1`（PASS + 1619 files 无 per-user 数据/无 live API key）。
- [ ] 打包后联系 Muse 走发布单推新（`-Version 1.4.9` + 新 SHA256 + 中文 notes），不重发旧包、不自行上传。
- [ ] 用户实机点更新，目视确认 1.4.8 安装引导行为 + 1.4.9 更新页三态布局。

---

## 1.4.8 · 2026-11-09（更新完成后由用户决定何时打开新版）

用户先解除上线闸门：「检查一遍没有问题之后，推到线上去就好。仔细测试一遍，没有问题之后，推到线上去就好了。要去联系 muse推新版本哈」。随后补报一个更新引导窗口的 bug：**「更新有那个安装引导进度条然后到百分百就可以点确定，要改成就是如果点确定或者叉，才会开始弹出新更新的应用页面然后这个安装引导的界面消失……不点确定，不会自己弹出来新更新的应用界面」**，并要求「你看现在是不是这样 不是的话优化成这个样」；沟通由我代劳：「你跟 muse 沟通就好」。

**核对结论：原行为确实不符合要求**。`UpdateInstaller.Apply()` 在文件替换完成、进度 95% 时无条件调用 `restart(exe)`；`UpdateProcess.Restart()` 启动新版进程并以 `MainWindowHandle != IntPtr.Zero` 作为「启动确认成功」的判据，于是新版主窗口在用户点「完成」之前就自己弹出；窗口文案还写着「完成后会自动打开新版」「新版本已启动，可以关闭此窗口」。

### 设计取舍

不能直接把 `restart(exe)` 删掉：它同时承担「新版程序确实加载成功才提交更新，加载失败恢复旧版本并保留备份」的保护。因此把**启动确认**与**显示窗口**解耦：新版进程照常启动、但只写 `ready` 信号并保持主窗口隐藏；安装引导窗口在用户点「完成」或右上角叉触发 `OnClosing` 时写 `go`；新版进程轮询到 `go`（或 helper 进程异常消失的兜底）才显示主窗口并清理信号。回滚路径仍用不带 handshake 的旧启动方式，老客户端 5 参数 `--update-result` 行为不变。

### 改动清单

| 文件 | 改动 |
| --- | --- |
| `src/VoiceBridge.Core/UpdateHandoff.cs`（**新建**） | 跨进程握手协议：`--await-handoff <ticket>`、`LaunchArguments`、ticket 只允许 32 位 Guid `N` 格式（不能逃逸 updates 目录）、`SignalReady()` / `SignalGo()` / `IsReady()` / `IsGo()` / `ShouldShowMainWindow() => IsGo()` / `Cleanup()`；信号用 `.tmp` + `File.Move(overwrite)` 原子写，路径固定在 `%LOCALAPPDATA%\WeChatVoiceBridge\updates`，helper 与新版进程共用 |
| `src/VoiceBridge.App/UpdateProcess.cs` | `Apply(string[] args, UpdateHandoff handoff, ...)` 增加握手参数；`Restart(..., UpdateHandoff? handoff = null)` 成功安装时附加 `--await-handoff <ticket>`，改以 `handoff.IsReady()` 判定启动成功，失败回滚仍用 `MainWindowHandle` 老判据；`ReadRestartNotice` 兼容 5 与 7 参数；新增 `HelperStillRunning(args)` 供 helper 消失兜底；100% 文案改「更新完成，等待确认后打开新版本」，未确认路径不再 `SignalGo()` |
| `src/VoiceBridge.App/App.xaml.cs` | 主窗口创建后若发现 handshake 则走 `AwaitInstallationConfirmation()`：`ShutdownMode.OnExplicitShutdown`、`SignalReady()`、120ms `DispatcherTimer` 轮询 `go` 或 helper 退出，再 `RestoreAndShow()` 恢复 ShutdownMode / 清理信号 / `Show()` + `Activate()`；`SingleInstance` 二次启动激活回调在等待期直接 return，防止绕过隐藏；握手写失败等异常一律兜底显示，不让应用永久不可见 |
| `src/VoiceBridge.App/UpdateInstallationWindow.cs` | 新增 `handoff = UpdateHandoff.Create(UpdateProcess.DirectoryPath)`；`OnClosing` 在安装忙时照旧阻止关闭，完成后（点「完成」或叉）写 `go`；初始与成功文案改为「点「完成」或关闭本窗口后才会打开新版本」，成功时按钮显示「完成」；`InstallAsync` 把 handshake 传入 `UpdateProcess.Apply` |
| `tests/VoiceBridge.Tests/UpdateHandoffTests.cs`（**新建**） | 18 例：初始无 `ready`/`go`、`ready` 后仍 `ShouldShowMainWindow()==false`、`go` 后才允许显示、`LaunchArguments` round-trip、老 5 参数无握手、非法 ticket 不创建、ticket 唯一且不能逃逸、`Cleanup()` 只清自己的信号、信号幂等不留 `.tmp`、空目录抛 `ArgumentException` |
| `Directory.Build.props` | `1.4.7` → `1.4.8`（Muse 建议旧包不再复用、另起版本号，已采纳） |
| `src/VoiceBridge.App/ReleaseHistory.cs` | 新增 1.4.8 条目（4 条），说明 100% 后不自动弹出、必须由用户确认、保留启动确认与失败回滚 |

### 验证（版本改动后重跑）

- Release 全量构建 **0 警告 0 错误**；`Test.ps1` **197/197 C# PASS** + **54/54 Python PASS**（41 个需 SQLCipher 运行时的跳过属既有基线）。
- `Verify-Source.ps1` **PASS**；`Publish.ps1` 与 `Build-Installer.ps1` 出厂门禁各一次，均 `PASS (1619 files, no per-user data, no live API key; 2 local credential(s) compared)`。
- 产物内容：ZIP 1621 项，`WeChatVoiceBridge.dll` / `VoiceBridge.Core.dll` / `WeChatVoiceBridge.exe` 与发布目录逐字节一致；EXE `ProductVersion=1.4.8+<commit>`、`FileVersion=1.4.8.0`、Setup `ProductVersion=1.4.8`；出厂 DLL 含 `--await-handoff` / `UpdateHandoff` / `ShouldShowMainWindow` / `SignalGo` / 1.4.8 版本说明，旧的「新版本已启动，可以关闭此窗口」文案已消失，`VoicePresets` / `SyncDefaultFish` 等禁用符号仍未回流。
- 静态行为链复核：握手目录固定（helper 与新版进程一致）、7 参数命令行可 round-trip、`FindProcess` 用 PID + 启动时间 ticks 校验、安装窗口中「完成」按钮 `Click => Close()` 与叉都汇入 `OnClosing`。⚠️ **跨进程 WPF 实机窗口隐藏未做目视验证**：需要真实跑一次升级，等 1.4.8 上线后由用户点更新实测（桌面快捷方式刻意保持 1.4.7，使线上版本严格大于桌面版本）。

### 产物（本地实算，供 1.4.8 发布单）

| 文件 | 字节 | SHA-256 |
| --- | --- | --- |
| `artifacts\WeChat-Voice-Bridge-V1.4.8-win-x64.zip` | 89,464,301 | `A4CA75B3C4F9EECB2D4BADB291161165FE6A7B9107F5343F678736D3E6CCB1C0` |
| `artifacts\WeChat-Voice-Bridge-V1.4.8-Setup.exe` | 65,302,086 | `A9FB702F06EC212C17E80B5F86FBE042734345769D92B6B68F11AB786FA3128C` |

发布目录 `artifacts\win-x64-v1.4.8`，r3 的 1.4.7 产物保留不删。

### 发布状态与 Muse 沟通（工程记录）

- 1.4.7 发布单 `codex_20261109_230425_438f4b` 正式回执为 `error`：`publish-voicebridge 1.4.7 REFUSED at public_verify`，公网清单 `GET` 连续 3 次 403（Caddy），流水线把清单回滚到 1.4.6 并声明未发布任何文件。不盲目重发。
- 暂缓通知 `codex_20261109_231021_676940` 最终 `ok`：旧包不再上传、不再翻清单；同时揭示 A1 服务器上 22:13:01 UTC 有一次**流水线之外的手工翻版**，服务器 `latest.json` 当时是 1.4.7 旧包。已明确回复 Muse：手工翻版不是我做的，我没有 A1 登录或 sudo 能力，请他以 A1 侧日志与文件时间为准继续查。
- 策略消息 `codex_20261109_233510_8cda41` 回执 `info`：Muse 确认①查手工翻版来源 ②清单保持现状等 1.4.8 覆盖 ③采纳 1.4.8、桌面保持 1.4.7 ④正在修 Caddy 403 ⑤**修好并公网验证通过后通知我，再发全新 1.4.8 发布单**。
- 22:4x UTC 我本地实测 `GET/HEAD latest.json` 已恢复 **200**，但内容仍是那份手工翻版 1.4.7（version=1.4.7 / publishedAt=2026-11-09T22:20:00Z / sha256=f2f58514… / size=89462225）。已再发消息 `wvb-muse-148-ready.txt` 告知 403 恢复、附上 1.4.8 两个产物的实测哈希与字节数，请 Muse 放行；**未自行上传、未绕过 Muse 通道、未重发 1.4.7**。
- 桌面快捷方式保持 1.4.7 r3 不动，目的就是让线上 1.4.8 严格大于桌面版本，用户能亲自点一次更新、目视确认新的引导窗口行为。

### 上线结果（2026-11-09 22:59 UTC）

- 策略消息 `codex_20261109_234635_779950` 的**正式 `ok` 回执**：Muse 独立两次实测 `GET/HEAD latest.json` 均 200（22:48:14、22:51:00 UTC，760 字节内容一致），确认 403 已恢复，放行发全新 1.4.8 发布单；清单保持现状不动、不来回翻，旧 1.4.7 包不上。
- 发布单 **`codex_20261109_235238_857190`**（`-Type publish-voicebridge -Version 1.4.8`）：`package_path` 指向 1.4.8 ZIP、`sha256=a4ca75b3…`、`package_size=89464301`（脚本自动实算）、中文 `notes` 无乱码。先回 `info`（常驻流水线接手：哈希/版本核验 → 分块上传 → 拼装验哈希 → 翻清单 → 公网复核），随后**正式 `ok`**：1.4.8 已发布完成，ZIP 哈希链三方一致（申报=PC 实测=A1 落地），Setup.exe 一并发布，`latest.json` 更新为 1.4.8，旧版备份 `latest.json.bak-1.4.7`。
- **我方独立公网友核（不只信回执）**：`GET latest.json` → 200 / 1093 字节，六字段 `version, publishedAt, url, sha256, size, notes` 齐全；`version=1.4.8`、`publishedAt=2026-11-09T22:59:27Z`、`url=…/WeChat-Voice-Bridge-V1.4.8-win-x64.zip`、`sha256=a4ca75b3c4f9eecb2d4badb291161165fe6a7b9107f5343f678736d3e6ccb1c0`、`size=89464301`，与本地实算完全一致；中文 notes 正确无乱码。ZIP 与 Setup 均 `HEAD 200`，`Content-Length` 分别为 89,464,301 / 65,302,086。
- **下载级哈希终验**：把线上 ZIP 与 Setup 完整下载后逐字节计算，ZIP `89,464,301` 字节 / `a4ca75b3c4f9eecb2d4badb291161165fe6a7b9107f5343f678736d3e6ccb1c0`、Setup `65,302,086` 字节 / `a9fb702f06ec212c17e80b5f86fbe042734345769d92b6b68f11ab786fa3128c`，**size 与 hash 均与本地产物一致**（临时副本已删除）。
- **手工翻版来源（Muse 最终结论，工程记录）**：A1 `auth.log` 显示 22:13:01 UTC 有人执行 `sudo cp` 清单 → `latest.json.bak-1.4.6`，随后 `sudo mv /tmp/latest-147.json` → `latest.json`；该文件名不是流水线用的 `/tmp/latest-new.json`，流水线日志里也没有这一笔。我的否认（无 A1 登录/sudo 能力）已记录，现有日志无法指认操作者，Muse 不臆测并已报给用户。
- 至此**线上正式版为 1.4.8**。桌面仍是 1.4.7 r3，客户端应出现更新按钮；请用户点一次更新实测：进度到 100% 后新版主窗口不自动出现，点「完成」或叉后引导界面消失、新版窗口打开。这是本轮唯一未做实机目视验证的部分。

---

## 1.4.7 r3 · 2026-11-09（删 API 后保留厂商名换样式 + 同厂商多 Key 自选启用）

> 仍在同一未发布版本号 1.4.7 上迭代；**线上仍是 1.4.6，本轮未上线、未联系 Muse**。上一节的 r2 产物哈希作废，以本节为准。本轮按用户明确指令**更新了桌面快捷方式**（指向 r3），此前「留在 1.4.5 体验自升级」的约定就此结束。

用户指令三点：① 删除 API 后该厂商音色仍保留厂商名、但换成缺失样式，编辑与删除按钮照常可用；② 同一厂商有多个 Key 时多一个「启用」按钮选择用哪个，不同厂商各可启用一个，只有同厂商 ≥2 个 Key 才出现该按钮；③ 添加第二个/第三个 Key 只添加、不自动应用，原来在用的保持不变。并补充事实前提：**Fish 的音色 ID 是平台公共的，任何有效 Key 都能跑**。

### 改动清单

| 文件 | 改动 |
| --- | --- |
| `src/VoiceBridge.Core/SpeechProfiles.cs` | `VoiceProfile` 新增可空 `ProviderKind`（缓存厂商）。删掉连接后音色仍知道自己属于哪个厂商 |
| `src/VoiceBridge.Core/VoiceLibraryMigration.cs` | 新增 `VendorOf()` / `CacheVendor()` / `UseActiveKey()`；`BindOrphanVoices()` 增加厂商保护（已知厂商与目标不同绝不改绑）并在绑定时写入 `ProviderKind` |
| `src/VoiceBridge.Core/ProviderActivation.cs`（新增） | 三条规则下沉到数据层便于单测：`StaysOnStandby()`（同厂商已有启用 Key 时新 Key 待用）、`NeedsActivation()`（同厂商 ≥2 个 Key 才显示按钮）、`Activate()`（只切换同厂商 Enabled，其它厂商原样返回） |
| `src/VoiceBridge.App/LocalSettings.cs` | `Load()` 在 provider/音色迁移后调用 `CacheVendor()`，趁连接还在把厂商写进老音色，发生变化时 `Save()` 一次；DPAPI 凭据库仍不读不写不删 |
| `MainWindow.Library.cs` | `LibraryRow` 加 `ProviderMissing` / `ProviderHint`，`ProviderRow` 加 `ShowActivate` / `CanActivate` / `ActivateLabel`；`RefreshLibrary()` 按「厂商名 + 是否可用」生成分组与状态；新增 `VendorName()` / `ActiveProviderFor()`；`ChooseContactVoice()`、`SpeechFor()`、`RefreshContactSpeech()` 改为按厂商回退到启用的 Key（`SpeechFor` 用 `voice with { ProviderId = provider.Id }` 满足 `Validate` 的归属校验） |
| `MainWindow.Library.cs` `EditProvider()` | 新增服务若 `StaysOnStandby()` 为真则 `Enabled=false`（添加不应用）；孤儿音色只绑到该厂商**实际启用**的 Key |
| `MainWindow.Library.cs` `DeleteProviderClick()` / `ActivateProviderClick()`（新增） | 删除前把厂商缓存到音色；同厂商若还有 Key，音色立即跟随仍可用/自动启用的那个；点「启用」用 `ProviderActivation.Activate()` 只切同厂商，并 `UseActiveKey()` 让该厂商音色跟随 |
| `ProfileEditors.cs` | `ProviderEditor.Save()` 不再强制 `Enabled=true`（编辑待用 Key 不会偷偷启用）；`VoiceEditor` 在原服务已删时插入「（原服务已删除）」占位选项，编辑弹窗可正常打开、保存并保留原绑定；若有其它服务，把服务选择框移到详情页实现真正改绑 |
| `MainWindow.xaml` | 「我的音色」行用 `DataTemplate.Triggers` + `DataTrigger ProviderMissing`：名称灰化斜体、显示警示状态标签，编辑/删除按钮不变；「语音服务」行新增「启用」按钮（`ShowActivate` + `BoolToVis`，已启用显示「使用中」并禁用）；两处说明文案同步 |

### 三条规则的确切语义

- **同厂商互斥、跨厂商独立**：`Activate()` 先按 Id 找到目标，只改写 `Kind == target.Kind` 的条目；其它厂商的对象原样返回（`Assert.Same` 单测锁定）。只有一个 Key 的厂商没有按钮、也不需要选择。
- **添加不应用**：第一个 Key 默认启用；此后任何新增 Key 在同厂商已有启用 Key 时一律 `Enabled=false`，包括用户手动切到第二个后再加第三个。
- **任意 Key 都能跑音色**：音色只存 Voice ID（合成时是 `reference_id`），鉴权用启用 Key。切换启用 / 删除某 Key 后，只要同厂商还有启用的 Key，`SpeechFor()` 就自动回退过去，无需手工改绑；只有该厂商一个 Key 都不剩时才显示缺失样式。

### 验证与产物

- Release 构建 **0 警告 0 错误**；`Test.ps1` **179/179 C# PASS**（新增 `ProviderActivationTests` 12 个）+ **54/54 Python PASS**；`Verify-Source.ps1` **PASS**。
- 新增单测覆盖：第一个 Key 启用、加第二/第三个不切换、跨厂商各启用一个、激活幂等、未知 Id 无副作用、删除连接后厂商名仍在、老 JSON 无厂商字段时回填并可反序列化、同厂商音色跟随、跨厂商音色零损伤、孤儿音色不跨厂商误绑。
- 静态结构核查：从真实 `MainWindow.xaml` 抽取两个 `DataTemplate`，核对所有 `{Binding ...}` 路径都能在 `LibraryRow` / `ProviderRow` 字段中解析，`DataTemplate.Triggers` 的 `TargetName` 与 `x:Name`（`VoiceName` / `VoiceHint`）一一对应，启用按钮的事件与 `BoolToVis` 转换器齐全。⚠️ 本轮**未做运行时隔离 WPF 渲染验证**：验证脚本需要临时编译 .NET 代码，被环境安全策略拦下，未绕过；样式行为请在桌面端目视确认。
- ZIP / Setup 打包门禁各一次 `PASS (1619 files, no per-user data, no live API key; 2 local credential(s) compared)`。
- 发布物（**以这一组为准**）：`WeChat-Voice-Bridge-V1.4.7-win-x64.zip` **89,462,225 字节** / SHA-256 `F2F58514A93E63148166845E5CFA982F8056D9EE6ECC29BFDA51784DE408635C`，1621 项；`WeChat-Voice-Bridge-V1.4.7-Setup.exe` **65,299,128 字节** / SHA-256 `2139EDB3521D6C8A89D4284C891FEEC974A371BBB2C12747AF219A857A21B51E`。出厂 DLL 含 `ProviderActivation` / `StaysOnStandby` / `UseActiveKey` / `ActivateProviderClick` / `ProviderMissing` 等符号，且含本轮新增的版本日志文案（发布 DLL 与构建 DLL 哈希一致）；无个人配置与密钥残留。
- 旧 r2 产物改名留档为 `...-r2-win-x64.zip` / `...-r2-Setup.exe`；r1 的 Setup 仍在。`ReleaseHistory.cs` 在首轮打包后才补上本轮条目，因此首轮 r3 包改名留档为 `...-r3-prelog-win-x64.zip` / `...-r3-prelog-Setup.exe`，标准名包已按最终源码重出。打包时用户正运行 r2 的 exe（PID 11884），**未强杀进程、未覆盖其目录**，改发布到 `artifacts\win-x64-v1.4.7-r3`。
- **桌面快捷方式已按用户指令更新**：`D:\OneDrive\桌面\微信语音桥.lnk` → `artifacts\win-x64-v1.4.7-r3\WeChatVoiceBridge.exe`（已核对 .lnk 二进制只含 r3 路径、不含 1.4.5）。

---

## 1.4.7 修订 · 2026-11-09（清空内置音色 + 添加 API 后音色自动可用）

> 1.4.7 **从未上线**，本轮在同一版本号上迭代。下面旧的 1.4.7 段是首轮记录，其中「预置音色本次不动」的要求与该段产物哈希均已作废；**以本节的产物哈希和新包为准**。线上仍是 1.4.6，桌面快捷方式未动。

用户新指令：内置音色也清空、无内置音色、只保留用户新添加的；并明确「api 添加进来，对应的音色就不要显示无 api 了，而是变成可用的状态，请修复」。

### 问题定位：「无 API」从哪来

- 首轮 1.4.7 摘掉了出厂 `fish-default` **服务**，但按当时要求保留了 4 个预置音色（聂聂 / 里奥 / 熊二 / 派大星）+「此前使用的音色」的播种，这些音色的 `ProviderId` 仍指向已不存在的 `fish-default`。
- 于是 `RefreshLibrary()` 把它们归到「服务已删除 / 服务不可用」分组，`ChooseContactVoice()` 提示「尚未配置的 API」——即用户说的「无 API」。用户删掉自己添加的服务后留下的孤儿音色同样会卡在这个状态。

### 改动清单

| 文件 | 改动 |
| --- | --- |
| `src/VoiceBridge.App/MainWindow.Library.cs` | 删掉 `InitializeLibrary()` 里 `if (library.Count == 0)` 的整段预置音色播种；删掉 `LegacyPresetProviderId` 常量与两处按它查找音色的逻辑（联系人默认音色改为只按 `VoiceId` / 第一条可用音色）；加载 `VoiceLibrary` 时再按 `IsFactoryVoice()` 过滤一次做双保险 |
| `src/VoiceBridge.App/MainWindow.xaml.cs` | 删掉 `VoicePreset` 记录与 `VoicePresets` 四个内置音色；`RefreshVoices()` 只列 `userVoices`，空列表时 `SelectedIndex = -1` 防越界；`VoiceIdOf()` 只查用户命名音色 |
| `src/VoiceBridge.Core/VoiceLibraryMigration.cs`（新增） | `IsFactoryVoice()` 用**双条件**精确识别出厂音色（`ProviderId` 精确等于出厂 Id **且** `Id` 以 `fish-` 开头）——用户把内置音色改绑到自己服务后不再算出厂音色，会保留；`RemoveFactoryVoices()` 返回用户音色 + 被删 Id 并清理空/重复 Id；`CleanContactReferences()` / `CleanTestVoiceReference()` 清理悬空引用；`BindOrphanVoices()` 把没有有效服务的音色关联到指定服务 |
| `src/VoiceBridge.App/LocalSettings.cs` | `Load()` 在 provider 迁移之外再跑音色迁移，同步清理 `ContactProfiles[].VoiceProfileId` 与 `TestVoiceProfileId`，任一迁移发生变化就 `Save()` 一次；DPAPI 凭据库仍不读不写不删 |
| `MainWindow.Library.cs` `EditProvider()` | 新增服务保存后调用 `BindOrphanVoices(library, providers, updated, out bound)`，把孤儿音色自动关联到新服务并提示数量；已绑定现存服务的音色一律不动，编辑已有服务时不触发 |
| 文案 | `RefreshLibrary()` 的「服务已删除 / 服务不可用」→「未关联服务」；`ChooseContactVoice()` 的「尚未配置的 API」→「尚未关联可用的 API」并指引改绑；`MainWindow.xaml` 的「我的音色」补空态说明、语音服务说明补「不预置音色」 |
| `scripts/Verify-Source.ps1` | 新增防复发断言：禁止 `VoicePresets` / `LegacyPresetProviderId` 播种、`LocalSettings.Load()` 必须调用音色迁移、`MainWindow.Library.cs` 必须保留孤儿绑定；顺手修了 PowerShell 5.1 读 UTF-8 无 BOM 脚本的语法坑（补 BOM）与 `git diff --check` 的 stderr 噪声 |

### 验证与产物（本轮重做）

- Release 构建 **0 警告 0 错误**；`Test.ps1` **167/167 C# + 54/54 Python PASS**（新增 `VoiceLibraryMigrationTests.cs` 14 个用例：出厂音色全删、用户音色逐字段保留、改绑过的内置 Id 保留、相似 Id 不误删、幂等、联系人/试听引用清理、旧 JSON 反序列化、孤儿绑定与幂等）；`Verify-Source.ps1` **PASS**。
- **打包环境插曲**：用户当时正在运行 `artifacts\win-x64-v1.4.7\WeChatVoiceBridge.exe`（PID 36940），DLL 被锁导致覆盖发布失败。**没有强杀进程、没有动那个目录**；改为发布到并行目录 `artifacts\win-x64-v1.4.7-r2`，ZIP / Setup 仍用标准文件名输出新内容。旧 Setup 改名保留为 `WeChat-Voice-Bridge-V1.4.7-r1-Setup.exe`（未删除）；旧 ZIP 在覆盖尝试中已被安全删除机制移入回收站。
- 出厂门禁：ZIP 与 Setup 打包前各一次 `PASS (1619 files, no per-user data, no live API key; 2 local credential(s) compared)`。
- `WeChat-Voice-Bridge-V1.4.7-win-x64.zip`：**89,457,459 字节**，SHA-256 `116BB5C052852FBA961E4B2219EB9C73C56C62FC4CED81D7F347ACC00278C1E0`；1621 项、exe/dll `ProductVersion=1.4.7`、worker 与两个 wheel 齐全、无 `settings.json` / `.dpapi` / `.key` / 数据库残留。
- `WeChat-Voice-Bridge-V1.4.7-Setup.exe`：**65,298,145 字节**，SHA-256 `5D711DC6E9F043AFEE37A160CCE14ED4CFBA3D6C0D9B39C9546D034FF6C5B7AD`；Inno Setup `Successful compile`。
- 出厂二进制符号核查：`VoiceBridge.Core.dll` 含 `VoiceLibraryMigration` / `RemoveFactoryVoices` / `BindOrphanVoices`；`WeChatVoiceBridge.dll` 已**不含** `VoicePresets` / `LegacyPresetProviderId` /「此前使用的音色」，含「未关联服务」与 `BindOrphanVoices`。
- **未上线、未联系 Muse、桌面快捷方式未动**。上线闸门：必须等用户实测通过并明确发话。

---

## 1.4.7 · 2026-11-09（取消出厂自带「默认 API」）

用户要求：全新安装时 API 服务列表为空、一条不种；任何服务条目都可删，不留内置/不可删条目；老用户升级时自己添加过的服务与钥匙原样保留、所有人的内置默认 API 自动删掉；出厂包里不得再含任何真实 API 钥匙；**预置音色本次不动**。并要求交付说明「种子逻辑原来在哪、怎么摘的；升级迁移怎么处理的老配置」。

### 根因排查：「真实 API 钥匙」到底在哪

- 源码、git 全历史、1.4.6 的 ZIP 与 Setup.exe 中**没有明文硬编码的真实 API Key**；ZIP 里命中的 `sk-` 是 .NET 语言标签（`sk-SK`）和 SPDX 许可证 ID，不是密钥。
- 真正的链路是「默认服务 + 本机凭据库」：`MainWindow.Library.cs` 每次启动补种一条固定 `Id="fish-default"` 的连接，`SyncDefaultFish()` 把本机当前选中的凭据名（`CredentialId`）持续写回它，`DeleteProviderClick()` 又对固定 Id 做了不可删特判。开发机 `%LOCALAPPDATA%\WeChatVoiceBridge\fish-keys.dpapi` / `fish-key.dpapi` 用 DPAPI 存着开发者本人的真实钥匙，本机 `settings.json` 实测就是 `fish-default → CredentialId=speech-fish-default`。所以这条出厂连接在本机会直接读到开发者钥匙且删不掉；出厂包虽未带 `.dpapi`/`settings.json`，产品语义上依然不成立。

### 种子逻辑原来在哪、怎么摘的

| 位置（改动前） | 作用 | 本轮处理 |
| --- | --- | --- |
| `MainWindow.Library.cs` `InitializeLibrary()` | `if (!providers.Any(item => item.Id == DefaultFishId)) providers.Insert(0, new() { Id = "fish-default", ... CredentialId = selectedKeyName });` 每次启动补种 | 删除整段播种；改为只加载 `settings.Providers`，过滤固定出厂 Id、空 Id 与重复 Id |
| `MainWindow.Library.cs` `DeleteProviderClick()` | `row.Id == DefaultFishId` 直接拦下，提示「默认 Fish 连接请通过编辑修改」 | 删除特判；任何服务都可删，绑定音色保留并提示可改绑 |
| `MainWindow.Library.cs` `SyncDefaultFish()` | 把 `selectedKeyName` / `fishModel` / 延迟写回默认服务 | 整个方法删除，`PublishRouting()` 不再调用 |
| `MainWindow.Library.cs` `RefreshLibrary()` | `Name == "Fish Audio"` 时显示「默认连接」 | 改为空名时按服务类型显示标签，不再有「默认」概念 |
| `MainWindow.ConnectionSettings.cs` `StoreProvider()` | 编辑默认服务时联动隐藏控件与 `selectedKeyName` | 删除 `applyingProvider` 与整套联动，只做普通 upsert |
| `MainWindow.xaml.cs` | `applyingProvider` 抑制隐藏控件事件 | 删除该字段引用 |
| `MainWindow.xaml` | 隐藏区「默认 Fish 服务」「默认 Fish 密钥」文案 | 改为中性文案；「语音服务」说明补「软件不预置任何服务或密钥，首次使用请添加自己的 API」 |

预置音色按用户要求**本次不动**：播种逻辑保留，只把常量改名 `LegacyPresetProviderId = ProviderMigration.FactoryDefaultId`，明确它仅是旧音色条目的归属标记、不再对应任何会被创建的服务。删除服务后音色仍保留、可在「编辑音色」改绑，与放开删除保持一致。

### 升级迁移怎么处理的老配置

新增 `src/VoiceBridge.Core/ProviderMigration.cs`（放 Core 便于测试项目直接引用）：`FactoryDefaultId = "fish-default"`，`IsFactoryDefault()` 用 `StringComparison.Ordinal` 全等匹配，`RemoveFactoryDefault(providers, out removed)` 返回用户自己的服务列表。

`LocalSettings.Load()` 读 `settings.json` 后**无条件**调用它：

- 全新安装 / 空配置 → 返回空列表，什么都不种；
- 老配置 → 只精确摘掉 `Id == "fish-default"`，用户自建服务（GUID 等生成 Id）连同 `CredentialId`、`Endpoint`、`Model`、`Latency`、`TimeoutSeconds`、`AudioFormat` 原样保留、顺序不变；
- 顺手清理空 Id 与重复 Id；相似 Id（`fish-default-1`、`FISH-DEFAULT`、`fish_default`、带空格的）绝不误删；
- 确实摘掉过出厂条目时立刻 `Save()` 一次，把 `settings.json` 本身也写干净；写盘失败只影响落盘、内存结果已干净，下次启动重试；
- 迁移**不读、不写、不删** DPAPI 凭据库（`KeyStore`），用户自己的钥匙一条不动。

`MainWindow.Library.cs` 的 `InitializeLibrary()` 再做一次同样的 Id 过滤作为双保险，避免其它来源的配置把出厂服务带回列表。

### 出厂包不得含真实 API 钥匙

新增 `scripts/Assert-CleanPayload.ps1`；`Publish.ps1` 在写 ZIP 前、`Build-Installer.ps1` 在调 Inno Setup 前都强制调用：

1. **文件名门禁**：`settings.json` / `*.dpapi` / `*.key` / `.env*` / 数据库与 WAL/SHM/journal，命中即拒绝打包（沿用并收窄了旧检查；CPython 运行时自带的 `cacert.pem` 是合法文件，不在黑名单）。
2. **明文门禁**：对发布目录每一个文件（含 DLL/EXE）按 1 MB 分块扫描明显密钥形态，兼顾跨块边界。
3. **精确门禁**：读取本机 DPAPI 凭据库（只在内存里，绝不打印或写盘），把其中每个真实 Key 按明文与 UTF-16 两种形式作为模式去比对，出厂包里只要出现同一串就拒绝打包；干净 CI 机器没有凭据库时跳过该项并给出警告。

`Verify-Source.ps1` 扫描范围扩大到所有会随包发布的文本文件，并新增防复发断言：不允许再出现 `providers.Insert(` 播种 / `SyncDefaultFish` / `DefaultFishId`；`LocalSettings.Load()` 必须调用迁移；`fish-default` 字面量只允许存在于 `ProviderMigration.cs`。

### 验证与产物

**环境说明**：本轮开始时 `C:\Program Files\dotnet` 只剩运行时、SDK 目录已不存在，`dotnet build` 报 `No .NET SDKs were found`。从微软官方下载 `dotnet-sdk-8.0.427-win-x64.zip` 解压到 `%LOCALAPPDATA%\Temp\wvb-sdk\sdk`（不改系统 PATH、不装系统组件），用它完成全部构建与测试。该临时目录若被清理，按 HANDOVER 第 5 节重下即可。

- Release 全量构建：**0 警告 0 错误**。
- `Test.ps1`：**153/153 C# PASS**（141 原有 + `ProviderMigrationTests` 12 个新增）+ **54/54 Python PASS**（41 个因本机无 SQLCipher 环境按既有基线跳过）。新增用例覆盖：全新安装空列表、老配置只剩出厂条目时升级后为空、用户自建服务与 `CredentialId` 逐字段原样保留、幂等迁移、5 组相似 Id 不误删、空/重复 Id 清理、真实旧 JSON 反序列化后只丢出厂连接。
- `Verify-Source.ps1`：**PASS**（含本轮新增的三条防复发断言）。扫描范围扩大到所有会随包发布的文本文件，并排除了规则文件自身（它引用了被禁词字面量）。
- **打包门禁实测**：`Publish.ps1` 与 `Build-Installer.ps1` 各跑一次 `Assert-CleanPayload`，均 `PASS (1619 files, no per-user data, no live API key; 2 local credential(s) compared)`——即把开发机 DPAPI 凭据库里的 2 个真实 Key 按明文与 UTF-16 两种形式在全部出厂文件中比对，零命中。另对旧 `artifacts/win-x64`（1.4.6 发布目录）试跑也 `PASS (1593 files ...)`，说明旧包本来就没带明文钥匙，问题确实在默认服务的引用语义。
- **产物**（均在 `artifacts/`，未动桌面快捷方式）：
  - `WeChat-Voice-Bridge-V1.4.7-win-x64.zip`：89,456,803 字节，SHA-256 `908AC12B7767BC11930787F96C73886BB3C009EA0C992C92E246FFE05035F66A`；1621 项、exe/dll `ProductVersion=1.4.7`、Python 运行时与两个 wheel 齐全、无 `settings.json`/`.dpapi`/`.key`/`.db`/WAL/SHM 残留。
  - `WeChat-Voice-Bridge-V1.4.7-Setup.exe`：65,297,495 字节，SHA-256 `3F0927711AC2FE53F2174F7D7C63F905C1B23E1015FD7B1094ECE5F41DD76263`；Inno Setup `Successful compile`。
- **出厂二进制符号核查**：`VoiceBridge.Core.dll` 含 `ProviderMigration` / `RemoveFactoryDefault`；`WeChatVoiceBridge.dll` 不含 `SyncDefaultFish` / `DefaultFishId`。后者仍能搜到 `fish-default` 字符串，是 `LegacyPresetProviderId = ProviderMigration.FactoryDefaultId` 这个 const 被内联的结果，只用于给旧预置音色打归属标记，**不会创建任何服务**（全仓 `providers.Add/Insert` 只剩用户经 `ProviderEditor` 新建的 GUID 条目与从 settings 加载两处）。
- **未做**：实机全新安装后的界面目视确认、真实跨机器升级（把一份含 `fish-default` 的旧 `settings.json` 放到另一台 Windows 上跑）。这两项需要用户在桌面确认。
- **本轮未上线、未联系 Muse**：用户只要求改机制与交付报告；线上仍是 1.4.6。桌面快捷方式保持指向 `artifacts/win-x64-v1.4.5`。

---

## 1.4.6 · 2026-11-09（更新按钮三态配色 + 上线发布）

用户要求「软件更新」区有升级时的按钮做得更居中协调、且三种状态颜色各不相同，并明确**不要更新桌面快捷方式**（要在 Muse 推上线后自己从桌面端点更新升级）。

| 文件 | 改动 |
| --- | --- |
| `src/VoiceBridge.App/App.xaml` | 新增 `UpdateReadyText` / `UpdateReadyFill` / `UpdateReadyBorder` / `UpdateInstallFill` / `UpdateInstallBorder` / `WhiteText` 色板；新增 `UpdateReadyButton`（浅绿描边）与 `UpdateInstallButton`（实心绿）两个样式，二者与 `SecondaryButton` 共用 36 DIP 高度、`MinWidth 120`、`Padding 18,7`、圆角 9、内容居中 |
| `src/VoiceBridge.App/MainWindow.xaml` | `UpdateAction` 加本地 `MinHeight=36` / `MinWidth=120` / `Padding=18,7` / 双向居中对齐——本地值优先于样式，切换样式不再撑高整行 |
| `src/VoiceBridge.App/MainWindow.Updates.cs` | `RenderUpdate` 按三态选样式与字色：无更新＝灰色 `SecondaryButton`、发现新版＝浅绿描边 `UpdateReadyButton`、已校验可安装＝实心绿 `UpdateInstallButton`；按钮文字用 `TextBlock` 禁换行、居中 |
| `Directory.Build.props` / `ReleaseHistory.cs` | 版本 1.4.6，新增 1.4.6 更新说明 |

**发布前复验**：`Test.ps1` 全量 141/141 C# + 54/54 Python PASS、Release 构建 0 警告 0 错误；`Verify-Source.ps1` PASS；逐项静态核查前几轮全部修复点仍在；隔离 WPF 工程 11 个场景（取消/点叉/添加 × 3 种初始提示词 + 选普通提示词、直接点「编辑自定义…」、删除预设、旧版自定义数据）全部通过且 `shows 自定义=False`；三态按钮截图确认三种颜色不同、尺寸一致、上下居中。

**产物（并排在 artifacts/）**：`WeChat-Voice-Bridge-V1.4.6-win-x64.zip`（89,456,214 字节，实算 SHA-256 `047F899051D63016E309812AC893342D89A20C7AB80D7C461F8EF970FEE1A37B`）与 `WeChat-Voice-Bridge-V1.4.6-Setup.exe`（65,298,533 字节）。ZIP 含 1621 项、版本 1.4.6、Python 运行时与 wheel 齐全、无数据库/WAL/配置/密钥残留、程序集含本轮全部修复符号。

**按用户要求**：桌面快捷方式保持指向 `artifacts/win-x64-v1.4.5`，未调用 `Install-DesktopShortcut.ps1`，以便用户亲自体验「桌面端检测到线上 1.4.6 → 下载 → 重启安装」的完整升级流程。线上发布走 Muse 通道，本代理不自行上传。

**Muse 发布结果**：发布单 `codex_20261109_023128_ac2ff2` 先收到一条 `error`（`Remote end closed connection without response`，声称「未发布或已回滚」）。本代理未盲目重发，而是先独立实测：`latest.json` 已是 1.4.6、线上 ZIP HTTP 200 / 89,456,214 字节 / SHA-256 与本地完全一致、Setup.exe HEAD 200 / Content-Length 65,298,533；随后用允许范围内的简短问答向 Muse 核实，并取回同一发布单的第二封正式回执 **status = ok**：「publish-voicebridge 1.4.6 已发布完成（更正：上一条 error 回复是误报，请以本条为准，不要重发）」。原因：流水线 stage 8 公网复核首次抓清单遇到瞬时连接中断被判异常，但文件与清单当时已全部落位、异常分支未执行回滚。Muse 复核 ZIP 三方一致、Setup.exe sha256 `8f7251a5aa9f2083abac982b8260ff75b79b584acb3f371c7db4606c976b0c5d`（本地实算一致），旧清单备份为 `latest.json.bak-1.4.3`。**结论：1.4.6 已上线，不要重发本单。**

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
| 1.4.5 | 自定义提示词选中状态根治 | 本地试用，`artifacts/win-x64-v1.4.5`；**桌面快捷方式仍指向此处（用户要求保留，用于体验自升级）** |
| 1.4.6 | 更新按钮三态配色与居中 | 已上线（2026-11-09） |
| 1.4.7 | 取消出厂默认 API、删 API 保留厂商名、同厂商多 Key 自选 | 已上线（后被 1.4.8 覆盖） |
| 1.4.8 | 更新完成后由用户决定何时打开新版 | **线上正式版**（`codex_20261109_235238_857190`，2026-11-09 22:59 UTC） |
| 1.4.9 | wx-multi 多版本适配 + 更新页精简 | 本地，未打包 |
| **1.5.0** | **API 配置简化（含「⋮」菜单重做）+ 音色失效语义 + 凭据指针修复 + 凭据库自动清扫** | **线上正式版**（发布单 `codex_20261110_130657_f72a7b`，2026-11-10 12:09:47 UTC） |

**发布状态更正（1.4.6 已上线）**：本会话确实生成了 `WeChat-Voice-Bridge-V1.4.6-win-x64.zip` 与 `...-Setup.exe`，并经 Muse 通道把 1.4.6 推到线上（详见 1.4.6 段）。此处的旧表述是 1.4.5 及更早留下的、已作废。线上正式版自 2026-11-09 起为 **1.4.6**（此前为 1.4.3）。

---

## 2026-11-10 · v1.5.0 追加：「⋮」更多菜单重做

用户复查截图指出「点完这三个点，编辑和删除完全不是一个画风的产物」。原因是 `ProviderMoreClick` 用 `new ContextMenu()` + `new MenuItem()`，走的是 WPF 默认模板：灰渐变边框、方角、紧凑行高、无圆角面板。

**改法**（`App.xaml` 新增三套样式，`MainWindow.Library.cs` 接线）：
- `RowMenu`（TargetType=ContextMenu）：外层 10px 透明留白给阴影，内层白底 `CornerRadius=10` + `#E9EEEC` 1px 边 + `DropShadowEffect`（Blur 18 / Depth 3 / Opacity 0.16 / `#2B3A35`），`MinWidth=116`、内容 4px 内边距。
- `RowMenuItem`：字号 13、高 34、左右 12 内边距、`Margin 0,1`（两行悬停底色不相接）、圆角 7；悬停底色由 `Background` 承载（模板里 `IsHighlighted` 时 `TemplateBinding` 取它）。
- `RowMenuItemDanger`：`BasedOn` 上者，文字 `Danger`（#D33B3B），悬停底色 `#FCEDED`。
- 定位：`HorizontalOffset = -(ActualWidth - button.ActualWidth - 10)`，让**白面板**右缘而不是阴影圈对齐「⋮」；`VerticalOffset = -6`，扣掉阴影留白后仍是 4px 间隙。

**坑**：ControlTemplate 里用 `DataTrigger Binding=Tag` 区分危险项不生效（悬停仍是浅灰），改为「底色走 `Background`」后正常。

**验证**：新建 `tools/MenuPreview`（加载 `App.xaml` 资源、反射置 `IsHighlighted`）渲染静态/编辑悬停/删除悬停三态；`tools/UiSmokeTest` 增 `menu`（抓真实弹窗截图）与 `menuclick`（`user32.mouse_event` 真点，UIA `InvokePattern` 对 MenuItem 无效）两种模式，并刻意点击行右侧留白证明整行可命中 → 「编辑语音服务」正常弹出；点后未做删除，避免动用户配置。`dotnet build` 0/0，`dotnet test` **211/211**。

---

## 2026-11-10 · v1.5.0 上线（Muse 发布单 `codex_20261110_130657_f72a7b`）

用户授权「推到线上最新版本，去联系 muse」。

**打包**：
- `scripts/Publish.ps1` → `artifacts\win-x64-v1.5.0\` + `artifacts\WeChat-Voice-Bridge-V1.5.0-win-x64.zip`
  ZIP = 90,814,448 字节 / `48e2addaf9a6ffb7af19cc2dfe1943a2176b037f42f7d95397a892be6ee6cc33`
- `scripts/Build-Installer.ps1 -PayloadDirectory artifacts\win-x64-v1.5.0` → `artifacts\WeChat-Voice-Bridge-V1.5.0-Setup.exe`
  Setup = 66,213,216 字节 / `462980bde3133cf37c11edd926707d95b5ecaddc44eb1e17148a818f38b11734`
- 门禁：Clean-payload PASS（1733 文件，无 settings.json / dpapi / 明文 Key）；`Test.ps1` PASS；`Verify-Source.ps1` PASS；58 项等价性 PASS；payload 版本 1.5.0 > 线上 1.4.8。

**Muse 通道**：`muse-call -Type publish-voicebridge -Version 1.5.0 -PackagePath <zip> -Sha256 <hex> -Notes <中文>`。
第一次回执是 `status=info`（流水线接手，预计 10 分钟），要求不退出等待；最终 `status=ok`（`publishedAt=2026-11-10T12:09:47Z`），回执称哈希链三方一致（申报 = PC 实测 = A1 落地），并备份了 `latest.json.bak-1.4.8`。

**独立公网友核**（不只信回执）：
- `latest.json` HTTP 200，version `1.5.0`、publishedAt `2026-11-10T12:09:47Z`、size 90,814,448、sha256 与本地一致；notes 1307 字，按 UTF-8 解码是正常中文（命令行长串中文经 `.cmd` 传参未损坏）。
- ZIP HEAD 200 / 90,814,448；Setup HEAD 200 / 66,213,216。
- **完整下载 ZIP（90,814,448 字节）重算 SHA-256 = `48e2adda…cc33`，与清单一致**。

**坑**：`Build-Installer.ps1` 的 `-Output` 是「额外再复制一份到某处」，不是产物路径；第一次传成与脚本默认相同的 `artifacts\WeChat-Voice-Bridge-V1.5.0-Setup.exe`，编译完复制自己给自己触发 `IOException`（文件被占用）导致脚本非 0 退出，重跑只传 `-PayloadDirectory` 即正常。

**未动**：桌面快捷方式仍指向 `artifacts\win-x64-v1.4.7-r3`（用户要求保留，让线上版本严格大于桌面版，便于亲自点一次更新实测）。

---

## 2026-11-10 — 项目纪律：增量记录铁律写入 AGENTS.md（Muse 按用户指示）

用户定规：本项目以后「做一部分，就记录一部分」——Git、交接文档、工作记录、日志都随做随记、随记随存，以便任何接手者（包括限流中断后换上的新模型）只靠仓库就能续跑。

**动作**：在项目根目录 `AGENTS.md` 顶部新增《项目工作指令》，把上述要求落成可执行条目：一节的定义、每节必做的三件事（记 WORKLOG、更新 HANDOVER-CURRENT、单独 git 提交）、推送节奏与远端 HEAD 核对、中断自救（落盘优先于新活）、禁止事项、发版前四项自查与发布后回执确认（含 commit 号）。原有的 WeChat database probes 条目原样保留在文件后部。

**验证**：本次只改 `AGENTS.md` 与本条 WORKLOG 记录，单独成一个提交并推送，远端 HEAD 核对一致后才算完成（结果见本提交）。当时工作区另有 5 个文件的未提交改动（小版本细节优化进行中），不属于本节，未混入本提交——该节完成后须按新规矩自行记 WORKLOG 并单独提交。

---

## 2026-11-10 · v1.5.1：更新页标题化 + 状态点对齐 + 删掉第二行说明

用户复查 1.5.0 上线后的两处界面问题（截图对照）：
1. 版本说明前的「v1.5.0 · 90.8 MB」是一行小字，希望「更像标题一些」；
2. 「软件更新」一行的灰点与「当前版本」不在一条线上，要求三种状态（灰/琥珀/绿）都纵向居中与文字对齐；
3. 追加指令：删掉第二行的「已是最新版本」，格子里只保留点和一行字、上下居中。

**改动**（`MainWindow.xaml` / `MainWindow.Updates.cs`）：
- 状态行结构改为 `StackPanel Orientation=Horizontal`：9px 圆点 + 版本号，两者 `VerticalAlignment=Center`，整行只有一行字，因此圆点必然落在文字中心线上；三种状态只换 `Fill`，几何完全一致。删掉 `UpdateSubtext` 文本块与其三处赋值（状态由卡片下方的状态行与按钮文案承载）。
- 更新说明上方新增标题行：`UpdateNoteTitle`（`SectionTitle`，16 Semibold）+ `UpdateNoteSize`（`Footnote`，12 灰、底线对齐），内容为 `v{版本}` 与 `{大小} MB`；原来那行小字彻底不做了。
- `RenderUpdate` 同步填标题与体积；保留 `UpdatePackage.ToolTip`。
- **按钮改为整块居中**：说明与状态行之间没有分隔线、视觉上是一个整体，所以按钮的居中范围取「卡片上沿 → 说明框上沿」而不是状态行。结构是两列 Grid：左列 `StackPanel`（状态行 + 说明标题行），右列只有按钮、`VerticalAlignment=Center`，因此按钮天然在左列全高居中。
  - **坑（记下来）**：最初用「两行 Grid + 按钮 `RowSpan=2`」实现，WPF 会把跨行子元素的高度分摊到各行，空的第二行被撑成 18px，卡片因此从 52 长到 70，状态文字看着偏高（实测文字中心 27、卡片中心 36，差 9 DIP）。换成两列结构后中间层高度回到 52，文字中心 = 卡片中心 = 27。

**验证**：
- 预览工具 `tools/UpdatePanelPreview` 改为与真实布局同构，并新增**像素级测量**：按圆点自身几何范围找实心圆、按暗度找标题墨迹，比较两者中心；三态偏差 **-0.5 / -0.5 / 0.0 DIP（判定 OK）**，静态图 `artifacts/screenshots/update-panel-with-notes.png`。
- 真实应用截图（`tools/UiSmokeTest` 新增 `shot <pid> <png> [navId]` 模式）：`artifacts/update-live-uptodate.png`（灰点、单行、已是最新）、`artifacts/update-live-newer.png`（绿点、`v1.5.2  0.0 MB` 标题 + 说明面板）。后者用**本地暂存的 pending 更新包**触发（4096 字节假包 + 6 字段 state.json），截图后已删除，线上清单未动、未点击安装。
- 真实应用几何量测（`tools/UiSmokeTest` 新增 `probe <pid>`）：用 UIA 取「当前版本」文本与按钮的边界矩形比中心，无说明时为 **470.5 vs 471.0（差 -0.5 DIP，OK）**——修前会差约 9 DIP。
- `dotnet build` 0 警告 0 错误；`dotnet test` **211/211**；`Test.ps1` PASS；`Verify-Source` PASS；58 项等价性 PASS。
- 版本 **1.5.1**，`ReleaseHistory` 新增对应条目（含「按钮按整块居中」一条）。

**尚未做**：打包与 Muse 推送（等用户看过效果点头）。桌面快捷方式未动。

---

## 2026-11-10 · v1.5.1（续）：联系人「移除」按钮不再弹悬停提示

用户在实际界面悬停「移除」时看到浮动提示文字（「移出朗读名单，不会删除微信联系人」），判定没必要，要求去掉。

**改动**：`src/VoiceBridge.App/MainWindow.xaml` 的 `ContactRemoveAction` 样式删掉 `ToolTip` Setter。两处使用点（`SingleContactTemplate` / `ContactCardTemplate` 的「移除」按钮）都只靠这个样式，按钮本身没写 ToolTip，因此一处删除即全清；悬停配色不变。

**验证**：
- `tools/UiSmokeTest` 新增 `nohover <pid> <button> <png> [navId]` 模式：先 `ShowWindow(SW_RESTORE)` + 前置窗口，把光标两跳移到按钮中心，再用 `AutomationElement.FromPoint` **确认光标命中的就是这个按钮**（否则「没有提示」可能只是鼠标停在空处），随后 4 秒内轮询本进程所有顶层窗口里有没有 `ControlType.ToolTip` 元素。
- 实测：命中 `[移除]`、4 秒无 tooltip、**PASS**，截图 `artifacts/screenshots/contact-remove-nohover.png`。
- `dotnet build` 0/0；`Test.ps1` **.NET tests: PASS + Python tests: PASS**（注意：直接跑 `dotnet test` 会有 7 项因缺 `WVB_PYTHON` 而失败，必须走 `Test.ps1` 才有环境）。
- `ReleaseHistory` 1.5.1 增补该条。

**仍未做**：1.5.1 打包与 Muse 推送，等用户确认视觉后一起做（本次改动并入 1.5.1，不单独发版）。

---

## 2026-11-10 · v1.5.1（续）：语音服务间距 / 试听区加小标题 / ⋮ 菜单改向右展开

用户对着截图提了四条，全部并入 1.5.1。

**改动**：
1. **语音服务标题与首个分组太近**：`ProvidersList` 加 `Margin="0,12,0,0"`。分组头是绿色 `LibraryGroupHeader`，原来几乎贴着标题。
2. **试听卡片加字段标题**：原来两个输入框直接在白卡里，没有名字。现在每块用 `StackPanel`（`Margin=16,14,16,0` / `16,16,16,0`）+ `FieldLabel`（12 灰，与对话框字段一致）包住：「音色」在音色下拉上方、「文本」在文本框上方；块间距 16，标签到控件 6。
3. **删掉文本行下的横线**：`<Border Style="{StaticResource Hairline}"/>` 去掉；顺带删掉按钮行里那个空的占位 `TextBlock`，按钮改 `HorizontalAlignment=Right` 直接右对齐。
4. **「⋮」菜单方向**：原来 `HorizontalOffset = -(ActualWidth - buttonWidth - 10)` 是「菜单右缘对齐按钮右缘」→ 向左展开，用户觉得「往左歪」。改为从按钮**向右下**展开：`Placement=Bottom` 本身已左对齐，只需 `HorizontalOffset = -2`（-10 抵阴影留白、再右推 8），白面板左缘落在按钮左缘附近。宽度从 `MinWidth 116` 改为 `MinWidth 0 + Width 80`。
5. **菜单与按钮「粘在一起」**：`VerticalOffset` 从 -6 改到 -1，让白面板顶部与按钮底之间留出约 5px。原来白面板顶几乎压着按钮底边，阴影盖在按钮上，看着脏；实测间隙从 -1.3 DIP 变成 +5.4 DIP。

**验证**（用户已说明测试他自己看，所以只做最低限度）：
- `dotnet build` 0/0。
- 真实截图：`artifacts/screenshots/advanced-test-bottom.png`（试听卡片：音色/文本标题、无横线、按钮右下）、`artifacts/screenshots/menu-final.png`（菜单最终位置）。
- `UiSmokeTest menu` 现在会打印 ⋮ 与菜单的矩形、以及菜单距窗口右/下边界的余量。最终实测：菜单 popup **160×204 物理像素（106.7 DIP）**，右缘距窗口右边 **23 DIP**（收窄前 155 DIP 时几乎贴边、只有 7 DIP，更早的左向版本是 139 DIP），顶部间隙约 5 DIP。
- 宽度与「不出框」是耦合的：窗口固定、⋮ 距窗口右约 127 DIP，`Width` 每加 12 DIP，右缘余量就少 12 DIP——所以 92 时只剩 4 DIP，最后定在 80。以后要加第三个菜单项需重新算这个余量。
- `tools/UiSmokeTest` 顺带增强：`shot` 支持 `NavId#bottom`（自动把页面滚到底再截），`shot`/`menu`/`nohover` 截图或测量前都 `ShowWindow(SW_RESTORE)`——窗口最小化时 `BoundingRectangle` 会变成 -32000，菜单会量出 0,0。

**按钮下方留白**：用户先说「太近」，调到 `Margin="0,2,0,6"` 后回看又说「现在合适了，不用改」，保持现状。
