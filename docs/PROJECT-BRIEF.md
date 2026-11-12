# 微信语音桥 · 项目总览（给没有聊天记录的接手者）

> **这是新模型的第一入口。** 本文自包含：不读任何聊天记录，仅凭本文 + 仓库代码 + `docs/` 就能理解项目、约束、发布纪律与当前任务。
> 最新细节见 [HANDOVER-CURRENT.md](HANDOVER-CURRENT.md)，逐版本流水见 [WORKLOG.md](WORKLOG.md)，当日执行记录在本机的 `.workbuddy/memory/YYYY-MM-DD.md`（2026-11-12 起不入库）。
> 本文由 2026-11-10 的合并任务建立，之后每次完成一段工作都要回写这三处。

---

## 1. 项目是什么

Windows 桌面应用「微信语音桥」：读取本地微信加密数据库（只读），把联系人消息按用户配置的提示词/词库规则转成语音朗读。解决方案 `WeChatVoiceBridge.sln`，.NET 8 / WPF（C#）+ Python 3.12 worker，通过 UTF-8 JSON Lines 的 stdin/stdout 协议通信。

| 目录 | 职责 |
| --- | --- |
| `src/VoiceBridge.App` | WPF 界面、设置、更新流程、`MainWindow.*.cs` 分部类 |
| `src/VoiceBridge.Core` | 领域模型、更新清单/安装器/握手、词库规则、会话控制 |
| `src/VoiceBridge.WeChat` | 微信会话绑定、数据目录定位、fixture/真实模式桥接 |
| `src/VoiceBridge.Audio`、`VoiceBridge.FishAudio` | 音频播放；所有语音服务的协议适配（`SpeechProviderFactory.cs`，名字沿用早期的 Fish 项目） |
| `src/VoiceBridge.Core/ProviderCatalog.cs`、`ServiceIdentity.cs` | 厂商目录（19 个入口）与「同一家」的判断规则，见 `docs/providers.md` |
| `worker/` | Python 端：密钥提取 `audited_wechat_keys.py`、`wechat_adapter.py`、`main.py`、词库/联系人索引等；`edge_speak.py` 是 Edge 免费朗读助手（依赖在 `worker\tts-deps`） |
| `tests/python`、`tests/VoiceBridge.Tests` | Python unittest 与 C# xunit |
| `scripts/` | 构建/测试/门禁/打包/快捷方式/源码断言 |
| `artifacts/` | 本地发布产物（ZIP、Setup.exe、各版本目录） |
| `docs/` | 本文与交接、工作日志、用户指南、安全审查等 |

版本唯一来源是根目录 `Directory.Build.props` 的 `<Version>`；用户可见更新日志在 `src/VoiceBridge.App/ReleaseHistory.cs`。

## 2. 环境与常用命令（本机 Windows）

- 系统 `C:\Program Files\dotnet` **只有运行时、没有 SDK**。.NET SDK 8.0.427 解压在 `%LOCALAPPDATA%\Temp\wvb-sdk\sdk-tmp\dotnet.exe`（2026-11-11 核实；旧写法 `wvb-sdk\sdk\` 已不存在，临时目录被清理后需重新下载解压），不改系统 PATH。跑构建/测试脚本必须传 `-Dotnet $sdk`。
- Python 运行时在 `artifacts\win-x64\runtime\python\python.exe`（3.12 x64）。

```powershell
$sdk = Join-Path $env:TEMP 'wvb-sdk\sdk-tmp\dotnet.exe'
$root = 'D:\OneDrive\文档\ChatGPT\微信语音'
& $sdk build "$root\WeChatVoiceBridge.sln" -c Release                 # 编译
& "$root\scripts\Test.ps1" -Dotnet $sdk                               # 全量：Release build + C# xunit + Python unittest
& "$root\scripts\Verify-Source.ps1"                                  # 防复发静态断言
& "$root\scripts\Publish.ps1" -Dotnet $sdk -OutputDirectory "$root\artifacts\win-x64-vX.Y.Z" `
    -ZipPath "$root\artifacts\WeChat-Voice-Bridge-VX.Y.Z-win-x64.zip"   # 发布目录 + ZIP（内含出厂包门禁）
& "$root\scripts\Build-Installer.ps1" -PayloadDirectory "$root\artifacts\win-x64-vX.Y.Z"  # Setup.exe（含门禁）
# wx-multi 58 项等价性自检（必须用内置 3.12，系统 Python 3.13 会被 main.py 的版本检查挡下）
& "$root\artifacts\win-x64\runtime\python\python.exe" "$root\tests\wx-multi\test_audited_wechat_keys.py"
```

- `Publish.ps1` / `Build-Installer.ps1` 都会先跑 `scripts/Assert-CleanPayload.ps1`：禁止 `settings.json`、`.dpapi`、`.key`、`.env`、DB/WAL/SHM 混进出厂包，扫描明文 Key 形状，并用本机 DPAPI 凭据按明文与 UTF-16 比对。
- PowerShell 5.1 会把 native 命令的 stderr 当 ErrorRecord；`Test.ps1` 已对 `unittest -v` 放宽 `$ErrorActionPreference`。
- 从 Bash 调 PowerShell 会被安全策略拒绝；用 PowerShell 工具。
- Inno Setup 6.7.3 在 `C:\Users\USERNAME\AppData\Local\Programs\Inno Setup 6\ISCC.exe`。

## 3. 发布纪律（务必遵守）

1. **上线闸门**：任何推线上必须由用户本人明确发话。用户曾明确叫停「别上线啊，我还没测试呢」；后来明确授权「检查一遍没有问题之后，推到线上去就好……要去联系 muse推新版本哈」「你跟 muse 沟通就好」。以最新指令为准。
2. **只走 Muse 通道**：`C:\Users\USERNAME\es-tool\muse-call.cmd -Type publish-voicebridge -Version <v> -PackagePath <zip> -Sha256 <hex> -Notes <中文>`。不自行上传、不索要 A1 凭据、不绕过通道、不重复发单。等待回执用 `-Check <req_id>`（`-Type message` 用于沟通）。Muse 是用户的「好伙伴好朋友」。
3. 发布前：全量 `Test.ps1` + `Verify-Source.ps1` + ZIP/Setup 门禁 + 实算 SHA-256 与字节数 + 版本严格大于线上。
4. 线上清单 `https://iceland.de5.net/voice-bridge/latest.json` 六字段契约：`version, publishedAt, url, sha256, size, notes`。客户端 `UpdateManifest.cs` 校验 HTTPS、同 host、`/voice-bridge/` 前缀、`.zip` 结尾、sha256 为小写 64 hex、size ≤ 1 GiB。
5. 发布后**必须独立公网友核**（不能只信 Muse 回执）：GET 清单 200、六字段、版本与哈希与本地一致，ZIP/Setup HEAD 200 且长度一致，最好完整下载重算 SHA-256。历史出现过 Muse 误报 `error` / 流水线回滚，务必自己复核。
6. **干完活自己 `git commit`**（用户采纳的 Muse 习惯）。仓库有 remote `origin = https://github.com/Nefertarigazer/WeChat-Voice-Bridge.git`，`master` 跟踪 `origin/master`。
7. 用户偏好：中文交流；Muse 的恶作剧/吓唬内容要清掉，但**工作、发布、工程记录一律保留**。

## 4. 当前版本与线上状态（2026-11-12 02:10 GMT+1）

| 项 | 值 |
| --- | --- |
| 线上正式版 | **1.8.0**（发布单 `codex_20261112_020446_0bf7a9`，先 `info` 后 `ok`，`publishedAt=2026-11-12T01:07:29Z`，旧清单备份 `latest.json.bak-1.7.0`） |
| 源码版本 | `Directory.Build.props` = **1.8.0**（程序集 1.8.0.0，`1.8.0+5c3ad2e`） |
| 1.8.0 ZIP | 92,188,901 字节 / `3548a75646c6fc05bef293426e43b3cfc444681e1eb0a0e5422ac8776d55bb10` |
| 1.8.0 Setup | 67,120,906 字节 / `de8532a06f1015e94ea6f1d7255694b8ffa922abc5cd3b19278d003c857cb551` |
| 公网复核 | 清单 200（六字段齐全、notes 164 字正常 UTF-8 中文）、ZIP 与 Setup HEAD 200 且长度一致、**完整下载 ZIP 重算 SHA-256 一致** |
| 桌面快捷方式 | `D:\OneDrive\桌面\微信语音桥.lnk` → `artifacts\win-x64-v1.4.7-r3\WeChatVoiceBridge.exe`，**仍刻意保持 1.4.7**，让线上版本严格大于桌面版，用户能亲自点一次更新实测 |
| 上一版 1.7.0 ZIP | 92,186,434 字节 / `2a0940984d95bc72fb05ca4f69ad3fa9e9a422fe75121832db171b8af166029a`（发布单 `codex_20261111_211423_9ceb57`） |
| 上一版 1.7.0 Setup | 67,123,954 字节 / `763909e1c9814277112b945a627b042f19fbd31dbc09ed7c9979ca1b7dac5114` |
| Git | `master` 与 `origin/master` 同点；1.8.0 的包由 `5c3ad2e` 构建，1.7.0 的由 `dd263df`，1.6.0 的由 `f279d5e`；标签 `v1.5.3`、`pre-multi-provider` 是多厂商改动前的存档点 |

历史（1.5.2、1.5.3 的发布记录见 WORKLOG）：1.4.6 上线；1.4.7 发布单 `codex_20261109_230425_438f4b` 因公网 `public_verify` 403 被拒并回滚，旧包不再使用；公网 `latest.json` 曾 403（Caddy）已恢复；1.5.0 于 2026-11-10 12:09 UTC 由 Muse 流水线上线（发布单 `codex_20261110_130657_f72a7b`，旧清单备份 `latest.json.bak-1.4.8`）；**1.5.1 于 2026-11-10 13:21 UTC 由 Muse 流水线上线（发布单 `codex_20261110_141758_3cb004`，旧清单备份 `latest.json.bak-1.5.0`），本助手独立公网复核通过**；A1 服务器 22:13:01 UTC 有一次流水线之外的手工翻版，来源未指认（日志有 `sudo cp`/`sudo mv`，非流水线文件名），**不是本助手所为**，本助手无 A1 登录/sudo 能力。

## 5. 不可回退的已上线改动（红线）

1. **无出厂默认 API**：全新安装服务列表为空；老配置升级只按精确 Id 摘除 `fish-default`，用户自建服务/密钥保留；出厂包不得含真实 Key。数据层 `ProviderMigration.cs`、`VoiceLibraryMigration.cs`、`LocalSettings.Load()`。
2. **无内置音色**：两个列表全新安装都为空；孤儿音色在添加 API 后按厂商绑定可用；删 API 后音色保留厂商名、灰化缺失样式且仍可编辑删除；同厂商多 Key 用「启用」按钮自选，添加只添加不自动应用。核心 `ProviderActivation.cs`、`SpeechProfiles.cs` 的 `ProviderKind`、`MainWindow.Library.cs`。
3. **1.4.8 更新引导行为**：进度到 100% 后新版应用**不自动弹出**；必须点「完成」或关闭安装引导窗口，引导界面消失、新版窗口才打开。跨进程握手 `src/VoiceBridge.Core/UpdateHandoff.cs`（`--await-handoff <ticket>`、`ready`/`go` 信号固定写在 `%LOCALAPPDATA%\WeChatVoiceBridge\updates`），`App.xaml.cs` 的 `AwaitInstallationConfirmation`、`UpdateInstallationWindow.OnClosing`。保留「新版加载成功才提交、失败自动回滚」。

任何合并/重构都必须保持这三条，`Verify-Source.ps1` 有防复发断言。

## 6. 技术约定与红线

- **微信只读**：worker 端不得写微信内存或原始数据库；快照只拷到 `.sqlite[.next]` 缓存。密钥提取常量 `CONFIG_XOR_MASK` 必须与原切片逐字节一致。
- **凭据**：API Key 存本机 DPAPI 凭据库（`fish-keys.dpapi` 等），出厂包禁止携带；迁移不读写删该库。
- **单实例**：`SingleInstance` Mutex + `SetCurrentProcessExplicitAppUserModelID("WeChatVoiceBridge")` 合并任务栏图标。
- **更新握手**：见第 5 条第 3 点；老客户端 5 参数 `--update-result` 路径必须保持原行为。
- **IPC**：`worker/ipc_protocol.py` 的 `send(kind, session, generation, **fields)` 支持任意关键字字段（含 `code`、`message`），UTF-8 JSON Lines，stdout 专供协议。

## 7. 当前进行中的任务

> **2026-11-12 现状**：第三轮（外观主题、使用说明重写、三处修复、README）**随 1.8.0 上线**；剩下的只有「GitHub 仓库改公开」，要等用户看过历史隐私审计、选好开源许可证再做，见 HANDOVER 第 00 节。
>
> **2026-11-11 起的现行任务**：任务一「多厂商语音 API 融合」已完成，随 1.6.0 上线；任务二「隐私模式 / 提醒模式」（每位联系人各自设置）已完成两轮打磨，**随 1.7.0 上线**，监听中的已读、角标、提醒播报和手机回复后停下仍等用户实测。准绳是 [tts-integration.md](tts-integration.md)，现状与续跑点看 [HANDOVER-CURRENT.md](HANDOVER-CURRENT.md) 第 00 节。下面 A–C 是 2026-11-10 的旧任务，**已在 1.4.9–1.5.3 完成并上线**，仅作历史参考。

### 已完成的旧任务（2026-11-10）

### A. 合并微信多版本适配（wx-multi）

- 成果包：`wx-multi-handoff\package-v1\`（当时由 Muse 提交进仓库；2026-11-12 仓库公开前移出，只留在本机）。
- 合并入口：包内 `README.md`；**合并清单以 `docs/review-crouton.md` §4 为准绳**（R1–R8 + 复验）；修复说明看 `docs/fix-round1.md`（R1/R2/R3/R4 已修，自检 49+9=58 项）；`merge-notes-for-workbuddy.md` 是用户亲定的两条 UX 要求。
- 硬要求：
  1. 支持范围严格 **4.1.11 ≤ v ≤ 4.2.0.13**；范围外必须走包内已实现的 `unsupported_version` 通道（中文报错文案已备好），不许静默失败或乱读数据。
  2. 密钥掩码与原切片逐字节一致，不许改动原切片行为；**58 项等价性自检合并后仍要成立**。
  3. 不许回退第 5 节的三条已上线改动。
- 关键实现：`impl/worker/audited_wechat_keys.py`（多版本化重写，35KB）、`impl/worker/wechat_adapter.py` 与 `main.py`（version 透传）、`impl/cs/WeChatSession.cs`（区间判定 + `Version` 字段）、`impl/cs/PythonFixtureSource.cs` 与 `WeChatDirectory.cs`（start 包加 `version` + `unsupported_version` 文案）、`discovery/WeChatSession.cs`（按名扫描 `xwechat_files`，深度 ≤6、20 秒预算）。
- `WeChatSession.cs` 合并配方（review §2.2）：**基底取 impl 版**（区间门控），**并入 discovery 版的三常量三方法**（定位扫描）；禁止用 discovery 版的 `SupportedVersion` 单版本锁。
- **合并主体已完成**：worker 三件套 + C# 三文件已落地；原切片冻结在 `tests/wx-multi/slice/audited_wechat_keys.py`（禁止编辑）；自检落位 `tests/wx-multi/test_audited_wechat_keys.py` 并改为指向真实生产文件；58/58 PASS；`dotnet build` 0 警告 0 错误。R5 的两处 CS8602 与 R6 的两处测试构造点已修。详见 WORKLOG「进行中 · 2026-11-10」。
- 待落实 UX：范围外版本弹窗（检测到版本 + 支持范围，按钮「更新微信」/「退出应用」）；数据目录扫描失败时手动选目录兜底。

### B. 更新页面 UI 优化

- 删掉安装进度条页面「点确定才会进入更新应用」这类啰嗦解释文案（**注意：这只是删文案，不能改第 5 条第 3 点的行为**）。
- 下载完成后的绿色安装按钮上下不够居中；「版本更新文字 + 按钮」区域整体不够美观，继续优化布局。
- **保留相关截图**给用户看。

### C. 流程约束

- 合并完**先跑 `dotnet build`，必须编译通过、无报错**。
- **先不用打包**；用户检查效果觉得合格后，再打包，然后再推新。
- 告诉用户**新版软件位置**；**桌面快捷方式先不用更新**。
- 不打包、不上线、不改快捷方式，等用户确认。

## 8. 接手检查清单

1. `git status` 是否干净、`master` 是否与 `origin/master` 同步。
2. 读 `docs/HANDOVER-CURRENT.md` 第 1 节与第 7 节（当前状态与下一步）、`docs/WORKLOG.md` 最新段。
3. 本机有 `.workbuddy/memory/` 的话，读最近两天的日志（append-only，按日期；不入库）。
4. 按第 2 节命令跑一次 `Test.ps1` 建立基线；改动后重跑。
5. **每完成一部分立刻回写** `HANDOVER-CURRENT.md` / `WORKLOG.md` / 当日 memory，并 `git commit`，保证中断后新模型能接上。
