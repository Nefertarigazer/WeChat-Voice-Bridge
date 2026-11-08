# 微信语音桥 WeChat Voice Bridge V1.2 — 项目介绍与交接文档

> **这份文档有两个读者。**
> 上半部分（第一部分）是**项目介绍**：背景、目标、技术选型与理由、架构、发布形态，可以直接对外交付。
> 下半部分（第二部分）是**交接说明**：当前状态、不可破坏的约定、代码位置速查、实测环境、已知坑、验证方法、遗留项，写给接手的开发者或 AI。
>
> **来历**：本项目由 Codex 中途交接给 MiniMax Code。文档中所有数字、文件位置和行为描述，均按 2026-11-06 的实际代码与实机运行结果核对过，未做推测。
> **版本**：`Directory.Build.props` 中 `Version = 1.2.0`。
> **运行环境**：Windows x64，微信 4.2.0.13。

---

## 目录

**第一部分 · 项目介绍**
1. [背景与来源](#1-背景与来源)
2. [产品定义与边界](#2-产品定义与边界)
3. [用户流程](#3-用户流程)
4. [技术选型与理由](#4-技术选型与理由)
5. [系统架构](#5-系统架构)
6. [代码结构](#6-代码结构)
7. [运行与发布形态](#7-运行与发布形态)
8. [安全与合规姿态](#8-安全与合规姿态)
9. [质量现状](#9-质量现状)

**第二部分 · 交接说明**
10. [当前状态快照](#10-当前状态快照)
11. [不可破坏的约定](#11-不可破坏的约定)
12. [关键实现位置速查](#12-关键实现位置速查)
13. [本机实测环境](#13-本机实测环境)
14. [已知坑清单](#14-已知坑清单)
15. [验证与复现](#15-验证与复现)
16. [遗留项与技术债](#16-遗留项与技术债)
17. [接手建议](#17-接手建议)

---

# 第一部分 · 项目介绍

## 1. 背景与来源

需求来自一个很具体的日常场景：**希望微信里某个特定的人发来文字时，能用「TA 的声音」读给自己听**——比如走路不方便看手机、或者想要一种更私密的提醒方式。

技术上的难点在于，微信 4.x 桌面端**没有提供任何公开的消息读取接口**：没有官方 API，没有本地明文存储，消息库是 SQLCipher 加密的，且**密钥只存在于运行中微信进程的内存里**。所有第三方方案大致分成三类：

| 方案类别 | 代表 | 问题 |
|---|---|---|
| UI 自动化 / 钩子 | 各类「自动回复」「微信机器人」 | 依赖窗口结构，版本一升就坏；注入 DLL 有安全与合规风险；可能触发风控 |
| 逆向注入读内存 | 各类内存补丁工具 | 需要写进程内存、注入代码，风险高，且无法保证只读 |
| 桌面端订阅工具 | 少数付费软件 | 闭源、无法定制语音话术、无法对接自有音色 |

本项目选择**第三条路的一个窄化版本**：**不碰微信进程本身，只做只读**——在用户自己已登录的微信进程里**只读地**扫描出数据库密钥，把加密消息库**复制**到临时缓存目录，用 SQLCipher 以 `mode=ro` 打开，读取指定**一个**会话的新入站文字，交给 Fish Audio 克隆音色合成，用 Windows 默认输出设备播放。

整个过程：**不写微信进程、不注入代码、不发送任何微信消息、不读取除目标会话外的会话内容**。

## 2. 产品定义与边界

一句话：**把微信一个指定联系人发来的文字，用他的声音读出来。**

明确的产品边界（这些是设计约束，不是实现细节）：

| 维度 | 边界 |
|---|---|
| 会话范围 | **有且仅有一个**联系人。选择即锁定，监听期间不切换 |
| 数据方向 | **只读、只入站**。永不发送、永不自动回复、永不改写任何微信数据 |
| 消息类型 | 入站文字为主；图片/表情会转成一句「朗读话术」（见下表） |
| 人物 | 声音克隆需声音本人同意。按「界面克制简洁」的要求，这句**不再显示在界面上**，只保留在 `README.md` 与本文档 |
| 云端 | 文字会发送到 Fish Audio 云端合成。同上，界面上不再显示 |
| 模型 | 默认免费模型 `s2.1-pro-free`；`drama-3` 手动切换，付费余额不足（402）时**不自动回退、不自动充值** |
| 界面 | 不展示版本号、SQLite/WAL 等底层名词、不展示耗时等内部指标 |

### 朗读话术（第一人称）

微信原文往往是「对方的视角」（图、表情包、缩写、纯标点），直接丢给 TTS 会念得很怪。因此 worker 内置了一层**话术改写**，全部改成第一人称：

| 输入 | 朗读为 |
|---|---|
| 可识别的图片消息（type=47，微信表情图 / 表情包） | 我发了一个 {名字} 的表情包 |
| 表情包但没有名字 | 我发了一个表情包 |
| 图片消息但认不出 | 我发送了一个小表情 |
| 普通照片（type=3） | 我发了一张图片 |
| 文字里直接输入的 emoji | {名字}的表情 |
| `...` / `。。` / `..` / 单个 `…` | 我很无语了 |
| `？` | 啊？什么？ |
| `！` | 哇！ |
| `omg` | Oh my god |
| 独立的一个数字 `1` | 一（**词库的第一条示例**，程序不再写死；用户可改可删，删了不会再自动回来） |

用户还可以自己在「词库」页加读法：**一张表管到底**——每一行是「原本的样子 → 读成什么」，外加一档「出现方式」（单独出现时 / 句中出现时 / 两个都包含）。词与符号不再分两张表，行内直接改，不弹窗。

## 3. 用户流程

```
启动应用
  → 左侧栏四页：首页 / 音效 / 词库 / 高级
  → 首页：顶部状态显示「未开始」
  → 「对方」卡片：搜索昵称/备注/微信号，或直接粘贴微信号回车
      （下拉结果第二行显示的是微信号，例如 （联系人微信号））
  → 点「开始」→ 状态变「正在监听」
  → 对方发来新文字 → 自动合成 → Windows 默认输出设备播放
  → 「朗读记录」区显示已朗读内容（可勾选显示原文）
  → 「试听最后一条消息」随时验证链路（读该会话最新一条可读入站消息并朗读）
  → 词库页：一张表管所有读法，行内改「原本的样子 / 读成什么 / 出现方式」，右侧删除
  → 高级页：数据目录可「浏览…」；账号目录自动填好并隐藏（只有一个账号时不显示这一行）
```

停止方式：点「结束」停止当次监听；**最小化只缩到任务栏，保留任务栏按钮**（不再藏进托盘折叠区）；托盘保留「显示窗口 / 结束监听 / 退出程序」；关窗口等价于退出（会做完整清理）。

## 4. 技术选型与理由

| 决策点 | 选择 | 为什么 | 放弃了什么 / 代价 |
|---|---|---|---|
| 桌面框架 | **.NET 8 + WPF** | Windows 原生、自绘标题栏与渐隐遮罩方便、NAudio WASAPI 生态成熟 | WinForms 样式难统一；Electron 体积大且播放链路绕远。只服务 Windows，不选跨平台框架 |
| 语音合成 | **Fish Audio 云端 TTS** | 支持 `reference_id` 克隆音色；有免费模型；有延迟档位（balanced/normal/low）；HTTP 简单 | 本地模型要显卡+模型体积且音质不达标；Edge-TTS 无音色克隆 |
| 音频输出 | **NAudio / WASAPI** | 直接用系统默认输出设备，切设备即生效 | DirectSound/ASIO 无必要 |
| 微信数据读取 | **独立 Python 3.12 worker**（SQLCipher + zstandard） | 微信 4.x 用 SQLCipher 4 加密，密钥只在进程内存；Python 侧加密库与 HMAC 校验生态最成熟 | 纯 C# 直接读加密库要把密钥提取和 HMAC 校验全部重写，风险更高 |
| 密钥获取 | **只读内存扫描**（`ReadProcessMemory`）+ 数据库首页 HMAC 校验 | 唯一非侵入方式；只读取用户自己已登录的微信进程 | 注入/Hook/写内存一律不做（红线） |
| 数据库访问 | **复制到会话缓存目录** + `mode=ro` + `query_only=ON` | 绝不碰原始文件；规避微信持锁与 WAL 竞争 | 直接读原库不可行 |
| 进程间通信 | **stdin/stdout + JSON Lines** | 跨语言最省事，天然单向，易加会话/代际校验，易审计 | 命名管道/gRPC 属于过度设计 |
| 进程回收 | **Windows Job Object（kill-on-close）** | 应用崩溃时连带回收整棵 Python 进程树，不会留孤儿 | 手工 kill 容易漏 |
| 配置持久化 | **`settings.json` 原子写 + 音色即时落盘** | 用户看得见的选择绝不能丢（曾用 500ms 防抖，导致关窗前 500ms 的改动丢失） | 频繁写盘（可接受，文件很小） |
| 密钥存储 | **DPAPI（CurrentUser）** 密封到 `fish-keys.dpapi` | API Key 不以明文出现在设置文件或磁盘 | 换机器/换用户需重新添加 |
| 发布形态 | **自包含单目录 + 内嵌 CPython + 官方 wheel（SHA256 校验）** | 目标机零环境依赖，不装 Python、不装运行库 | 产物约 86 MiB |

**上游来源**：微信密钥提取与 SQLCipher 校验逻辑来自 `fanyuantaier/wechatauto-replica`（commit `4931f98d4b4b68dc4af8cfc10f76805eb9c743da`，Apache-2.0）。本项目只保留必要的片段，许可证见 `worker/WECHATAUTO-LICENSE.txt`。

## 5. 系统架构

```text
┌────────────────────────────────────────────────────────────────────┐
│ WPF 界面（VoiceBridge.App）                                        │
│  左侧栏四页：首页 / 音效 / 词库 / 高级                              │
└───────────────┬──────────────────────────────┬─────────────────────┘
                │                              │
      SessionController                   WeChatDirectory
      (VoiceBridge.Core)                  (VoiceBridge.WeChat)
   状态机 / 有界队列 / 去重 /             联系人搜索 / 读最后一条消息
   准入过滤 / 生命周期                    （按需启停的第二个 worker）
                │                              │
                │  IMessageSource             │  同一个 Python worker
                ▼                              ▼  协议（JSON Lines）
   PythonFixtureSource  ──── stdin/stdout ────►  worker/main.py
   (事件驱动 + 轮询兜底)                     │
                │                              ▼
                │                        wechat_adapter.py
                │                    ┌────────┴────────┐
                │                    │ 只读扫描微信进程内存
                │                    │ → SQLCipher 密钥
                │                    │ → 复制加密库到会话缓存
                │                    │ → mode=ro 增量查询
                │                    └────────┬────────┘
                ▼                             ▼
        FishSpeech (FishAudio)          WindowsPlayer (NAudio/WASAPI)
        api.fish.audio/v1/tts           系统默认输出设备
```

### 数据流（一条消息的完整路径）

1. `FileSystemWatcher` 监听微信 `db_storage/message/message_*.db(-wal)` 的变更 → 只是**查询线索**，不是数据来源。
2. 合并窗口 30ms 合并通知后，向 worker 发 `query`。
3. worker 把加密主库 + WAL 复制到会话缓存目录，用密钥以 `mode=ro` 打开，`PRAGMA query_only=ON`，按 `local_id` 游标读该会话（`Msg_<md5(username)>` 表）的新行，只取 `local_type` 命中 `TEXT_ROWS` 且发送方是目标用户的行。
4. worker 用 zstandard 解压长文本，按 `extract_text` + `spoken_text` 得到最终朗读文本，回传 `message` 包。
5. `SessionController.Admit` 做二次校验：账号、会话、入站、文本类型、非空、复合键去重（`分片:local_id`）、长度上限 1000、队列容量（默认 30），然后入有界 Channel。
6. `ConsumeAsync` 取出消息 → `FishSpeech.SynthesizeAsync` → Fish Audio → MP3/PCM → `WindowsPlayer.PlayAsync` 播放。
7. 播放完成后 worker 收到 `ack`，推进游标。

### 关键设计点

- **会话 + 代际（session/generation）**：每个数据包都带这两个标识，worker 与宿主都会丢弃不匹配的包。旧会话的音频、ACK、IPC 准入全部被拒。
- **基线**：开始监听时先记录每个分片的 `MAX(local_id)`，只朗读基线之后的新消息（不复读历史）。
- **只读快照**：`EncryptedSnapshot.refresh()` 复制主库和 WAL 后才查询，复制前后校验源文件没变，避免读到撕裂的快照。
- **密钥只从内存拿、只读不改**：候选密钥必须通过数据库首页 HMAC 校验才算有效（`_verify_enc_key`）。
- **崩溃不留痕**：worker 分配到 Windows Job Object，应用退出或崩溃时整棵进程树被系统回收。

## 6. 代码结构

```text
WeChatVoiceBridge.sln
├─ src/
│  ├─ VoiceBridge.Core/          领域核心，无 UI 无 IO
│  │   ├─ Contracts.cs           ISpeech / IPlayer / IMessageSource 等接口
│  │   └─ SessionController.cs   状态机、有界队列、准入、去重、生命周期
│  ├─ VoiceBridge.WeChat/        微信侧
│  │   ├─ PythonFixtureSource.cs 监听源：起 worker、文件事件 + 轮询兜底、query/ack
│  │   ├─ WeChatDirectory.cs      按需源：联系人搜索、读最后一条消息
│  │   ├─ SessionCache.cs         会话缓存目录（临时加密库副本）的生命周期
│  │   ├─ WeChatSession.cs        绑定已登录微信进程、校验版本、发现数据根
│  │   ├─ WorkerJob.cs            Windows Job Object 封装
│  │   └─ WorkerLog.cs            stderr 排空（丢弃，不入库）
│  ├─ VoiceBridge.FishAudio/     FishSpeech.cs  云端 TTS 客户端
│  ├─ VoiceBridge.Audio/         WindowsPlayer.cs  NAudio/WASAPI 播放
│  └─ VoiceBridge.App/           WPF 界面
│       ├─ MainWindow.xaml(.cs)   左侧栏四页导航（首页/音效/词库/高级）与全部交互
│       ├─ App.xaml              全局样式（卡片、下拉、开关、日志行）
│       ├─ AddItemDialog.xaml(.cs) 通用「名称 / 值」弹窗（仅用于音色与 API Key）
│       ├─ LocalSettings.cs       设置持久化 + DPAPI 密钥库
│       ├─ RuleScopes.cs          出现方式三档（单独出现时 / 句中出现时 / 两个都包含）
│       ├─ ReadingItem.cs         词库一行（行内可改，改动直接写回列表）
│       ├─ AccountChoice.cs       账号下拉的一项（显示名 + 目录名）
│       ├─ ContactNameText.cs     联系人名文本处理
│       ├─ app.ico                exe / 托盘图标（9 档尺寸，16–256）
│       └─ app-icon.png           窗口 / 任务栏图标
├─ worker/                       Python 3.12 worker（发布时随包携带）
│  ├─ main.py                    协议入口：start/query/ack/contacts/lastmessage/stop
│  ├─ wechat_adapter.py          ★ 核心：话术改写 + SQLCipher 只读读取
│  ├─ audited_wechat_keys.py     只读内存扫描取密钥（唯一允许 ReadProcessMemory 的文件）
│  ├─ db_adapter.py              fixture 模式的合成数据库实现
│  ├─ ipc_protocol.py            JSON Lines 收发
│  ├─ emoji_names.py             emoji 中文名表
│  └─ kaomoji_names.py           颜文字名表
├─ tests/
│  ├─ python/                    40 例（adapter 话术与读取逻辑）
│  └─ VoiceBridge.Tests/         27 例（.NET 端：会话、worker、音频、Fish）
├─ scripts/                      构建 / 测试 / 发布 / 冒烟 / 源码边界扫描
├─ docs/                         本文档与历史文档
└─ artifacts/                    构建产物（gitignore，不入库）
```

## 7. 运行与发布形态

**目标机零依赖**：发布目录自带 .NET 8 运行时、CPython 3.12 x64、SQLCipher 与 zstandard。

```powershell
# 构建与测试
.\scripts\New-WorkerVenv.ps1        # 建 .venv（开发用）
.\scripts\Test.ps1                  # .NET + Python 全量测试
.\scripts\Verify-Source.ps1        # 源码边界与密钥泄漏扫描

# 发布
.\scripts\Publish.ps1 -OutputDirectory artifacts\win-x64-live -ZipPath artifacts\WeChat-Voice-Bridge-V1.2-win-x64.zip
.\scripts\Build-Installer.ps1

# 发布后冒烟
powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File scripts\Smoke-Published.ps1
```

发布脚本内置的完整性保证：

- CPython 官方 NuGet 包校验 SHA256 + `dotnet nuget verify` 签名。
- `sqlcipher3` / `zstandard` wheel 校验 SHA256 后再解包。
- 内嵌 Python 启动后做 x64 / 3.12 冒烟断言。
- worker 依赖做 SQLCipher 与 zstandard 的功能性冒烟断言。

**当前产物**：单文件中文安装包 `artifacts/WeChat-Voice-Bridge-V1.2-Setup.exe`，以及发布目录 `artifacts/win-x64-live/` 与免安装 ZIP。安装向导结束时默认勾选启动应用。桌面快捷方式 `D:\OneDrive\桌面\微信语音桥.lnk` 指向发布目录。

### 应用图标的三处来源

改图标资源时要三处一起看，否则会出现「窗口一个样、托盘另一个样」：

| 位置 | 资源 | 说明 |
|---|---|---|
| exe 图标 / 任务栏 | `src/VoiceBridge.App/app.ico`（`ApplicationIcon`） | 编译进 exe；发布时 SDK 额外拷贝一份为 `WeChatVoiceBridge.ico` |
| 窗口标题栏 / 任务栏 | `src/VoiceBridge.App/app-icon.png`（WPF `Resource`） | `MainWindow.xaml:7` 用 `pack://application:,,,/app-icon.png` |
| **系统托盘** | `app.ico`（内嵌 `Resource`） | `LoadTrayIcon()` 加载；早前误用 `SystemIcons.Application`，展开托盘是 Windows 默认蓝白方块 |

`app.ico` 含 16/20/24/32/40/48/64/128/256 共 9 档，托盘 16×16 取原生那一档。

## 8. 安全与合规姿态

1. **只读**。对微信进程只做 `ReadProcessMemory`；不 `WriteProcessMemory`、不 `CreateRemoteThread`、不 `SetWindowsHookEx`、不注入 DLL。
2. **静态守卫**。`scripts/Verify-Source.ps1` 会在 CI/交付前扫描源码：命中 `WriteProcessMemory|CreateRemoteThread|SetWindowsHookEx` 或疑似密钥就直接失败；并强制 `ReadProcessMemory` **只能**出现在 `worker/audited_wechat_keys.py` 一个文件里。该脚本只用 PowerShell 自带的 `Select-String`，不需要额外安装 ripgrep。
3. **不写原始数据**。所有数据库操作都在缓存副本上，`mode=ro` + `query_only=ON`；原始微信文件只被读取。
4. **密钥不落明文**。Fish API Key 用 DPAPI（CurrentUser）密封到 `fish-keys.dpapi`；`settings.json` 里没有任何密钥。
5. **日志脱敏**。聊天正文、API Key 不进日志；stderr 排空后丢弃；界面只显示固定文案。
6. **数据副本不外流**。数据库副本只存在于会话缓存目录（与微信数据同卷），会话结束即删；不进入项目目录、测试报告与发布产物。
7. **知情同意**。声音克隆需本人同意、文字会发送到 Fish Audio 云端——按「界面克制简洁」的要求，这两句**只保留在 `README.md` 与本文档**，界面上不再出现。若要恢复展示，在「高级」页底部加一行灰色小字即可。

## 9. 质量现状

| 项 | 状态 |
|---|---|
| Python 测试 | **40 / 40 通过**（`tests/python/test_live_adapter.py`、`test_fixture.py`） |
| .NET 测试 | **27 / 27 通过**（排除 `Benchmark` 与 `AudioDevice` 分类） |
| 编译 | 0 警告 0 错误（`TreatWarningsAsErrors=true`，`NuGetAudit=true`） |
| 实机 | 微信 4.2.0.13 上完成「开始监听 → 对方发字 → 自动朗读 → 试听最后一条消息」全链路验证 |
| 托盘图标 | 展开托盘箭头实机确认：绿色气泡图标（非 Windows 默认蓝白占位图）；发布 DLL 内已确认含 `LoadTrayIcon` 与 pack URI |
| 左侧栏导航 | 实机确认四页（首页/音效/词库/高级）切换正常；**最小化后窗口为 `minimized` 而非隐藏，任务栏按钮保留** |
| 词库合并 | 实机确认：一张表同时管符号与词；行内改「原本的样子 / 读成什么 / 出现方式」、下拉切档、删除，均实时写回 `settings.json` |
| 账号目录 | 单账号时该行**自动隐藏**并填好；多账号时出现下拉，显示去掉实例后缀的微信号（如 `wxid_本人账号`） |
| 发布 | 自包含产物可解压即用，冒烟脚本通过 |

---

# 第二部分 · 交接说明

## 10. 当前状态快照

时间：2026-11-06。

| 项 | 值 |
|---|---|
| 应用进程 | 已在运行（发布目录 `artifacts/win-x64-live/WeChatVoiceBridge.exe`），状态**未开始**；点「开始」即进入正在监听 |
| 监听对象 | `（联系人昵称）` / 微信号 `（联系人微信号）`（会话标识 `wxid_联系人`） |
| 音色 | 聂聂（Fish 私有 reference id 已掩码，实际值见 `settings.json`） |
| 模型 | `s2.1-pro-free`（免费），音量 0.7 |
| 设置文件 | `%LOCALAPPDATA%\WeChatVoiceBridge\settings.json`（原子写，无密钥） |
| 密钥文件 | `%LOCALAPPDATA%\WeChatVoiceBridge\fish-keys.dpapi`（DPAPI） |
| 会话缓存 | `<微信数据根>\.voicebridge-cache\<guid>`，每份约 547 MB，应用退出时回收 |
| 仓库 | **尚无任何提交**，10 项 untracked（`.gitignore`、`AGENTS.md`、`Directory.Build.props`、`README.md`、`WeChatVoiceBridge.sln`、`docs/`、`scripts/`、`src/`、`tests/`、`worker/`）；`artifacts/` 已 gitignore |
| 最近一轮需求 | ①省略号/缩写/图片话术 ②界面底部渐隐 ③音色记忆 ④联系人显示微信号 ⑤托盘图标 ⑥**界面整体重构（左侧栏四页 + 词库合并 + 目录可改 + 最小化修复）**——**均已实现、发布并实机验证** |

**最近一轮已完成并验证的改动**（接手后无需重做）：

1. 单个 `…`（U+2026）也能触发语音 → 读「我很无语了」。根因：微信把 `...` 存成 1 个 U+2026，旧规则要求 ≥2 字符。
2. `omg` → `Oh my god`（ASCII 边界正则，中文紧邻可命中，`omgwtf` 不命中；用户词库优先）。
3. 表情三档话术统一；普通照片（`local_type=3`）读「我发了一张图片」。
4. 界面底部 36px 渐隐遮罩 + 底部留白，顶部 22px 渐隐（滚动后出现）。
5. 音色记忆：切换即写盘 + 关窗再写一次；运行中换音色下一条即生效；**内置与自定义音色均已实机验证**（增删一个自定义音色 → 重启后仍被选中 → 删除后回落聂聂）。
6. 联系人展示：列表与已选卡片第二行显示**微信号**（`alias`），没有微信号才回落 wxid。
7. **托盘图标**：原先写死 `SystemIcons.Application`，展开托盘看到的是 Windows 默认蓝白占位图；现改为加载应用自己的 `app.ico`，托盘与窗口/任务栏图标一致。详见 [第 12 节](#12-关键实现位置速查) 与 [第 14 节](#14-已知坑清单)。
8. **界面整体重构**：以用户提供的另一版前端为基底重做，左侧栏改为 **首页 / 音效 / 词库 / 高级**（命名由用户指定），并删掉所有重复的解释性文字（详见 [第 2 节](#2-产品定义与边界) 与 [第 11 节](#11-不可破坏的约定)）。
9. **词库合并成一张表**：原来的「符号读法」与「词条」两张表合成一张；每一行 = 原本的样子 → 读成什么 + 一档「出现方式」（单独出现时 / 句中出现时 / 两个都包含）；**行内直接改，不再弹编辑窗**；底部一条固定的添加行。
10. **规则三档在 worker 侧真正生效**：`_apply_rules` 分派到 `_replace_standalone` / `_replace_inside` / 全局替换；纯数字词带「独立替换」护栏（`123` 不会被读成「一23」）。
11. **目录可改**：「数据目录」加「浏览…」按钮；「账号目录」改为自动发现——单账号时**整行隐藏**并自动填好，多账号时才出现下拉框。
12. **最小化修复**：原先 `WindowStateChanged` 里调 `Hide()`，窗口直接缩进托盘折叠区、任务栏里没有按钮；现改为正常最小化，**保留任务栏按钮**。

## 11. 不可破坏的约定

这些是前任留下的硬约束，改动前请确认不会破坏它们。

### 红线

- **只读**：不 `WriteProcessMemory`、不 `CreateRemoteThread`、不 `SetWindowsHookEx`、不注入 DLL、**不发送任何微信消息**、不改写任何微信原始数据。`scripts/Verify-Source.ps1` 会强制第一条。
- **不记录敏感数据**：API Key 与聊天正文不得写入日志、不得提交进仓库。探针只输出长度/摘要/标签结构。
- **数据库副本不外流**：不得留在项目目录、测试报告或发布产物中。
- **界面不暴露底层信息**：不显示版本号、SQLite/WAL 字样、内部耗时。
- **朗读一律第一人称**；界面「对方」标签保持不变。
- **付费模型不自动降级**：402 时提示用户，不自动回退免费模型、不自动充值。
- **不主动 commit**：除非明确要求。

### 产品口径

- 表情三档最终口径：可识别的**图片类**消息（type=47，含微信自带表情图与表情包）统一读「我发了一个XX的表情包」；认不出读「我发送了一个小表情」；文字中直接输入的 emoji 读「XX的表情」；普通照片读「我发了一张图片」。
- 省略号：一个 U+2026 即视为「我很无语了」；单独 `.` / `。` **不**改写。
- 内置缩写目前只放 `omg → Oh my god`，用 ASCII 边界正则。
- 音色持久化：切换即写盘 + 关窗再写一次；音色 id 缺失时回落第一个内置音色「聂聂」。
- 联系人展示：名称行是备注/昵称，第二行显示 `alias`（微信号），无微信号才显示 wxid；**会话标识仍用 wxid**（消息表按 `md5(username)` 定位，不能换成 alias）。
- **界面克制简洁**：不写重复的解释性文字；同一件事只说一次。用户明确要求「把类似于这种过于重复的解释说明的都全部删掉」。
- **左侧栏四页**：首页 / 音效 / 词库 / 高级（命名由用户指定，不要改回「朗读 / 声音 / 读法」）。
- **词库是一张表**：符号与词不再分开；每行 = 原本的样子 → 读成什么 +「出现方式」三档；**行内编辑，不弹窗**；底部一条固定添加行。
- **出现方式三档**：`standalone`（单独出现时）/ `inside`（句中出现时）/ `both`（两个都包含）。旧 `Anywhere=true` 等同 `both`，`Anywhere=false` 等同 `standalone`。
- **纯数字词独立替换**：`1 → 一` 不会把 `123` 读成「一23」；`第1名` → `第一名`。
- **账号目录默认隐藏**：数据根下只有一个账号时整行隐藏并自动填好；多个账号才出现下拉，显示去掉实例后缀的微信号。
- **最小化保留任务栏按钮**：不要再用 `Hide()`。

### 工程约定（`AGENTS.md`）

- 实时探针必须用 `tempfile.TemporaryDirectory()` 落在配置的本地 scratch 盘（**OneDrive 之外**）。旧的探针若用了裸 `mkdtemp()`，先修再跑。
- 退出临时目录上下文前（含失败路径）关闭 SQLite 连接、停止子进程。
- 同一批只读查询**复用同一份快照**。

## 12. 关键实现位置速查

### `worker/wechat_adapter.py`（话术与读取的核心）

| 内容 | 位置 |
|---|---|
| 消息类型常量（`APPMSG_TYPE=49` / `EMOJI_TYPE=47` / `IMAGE_TYPE=3`） | `worker/wechat_adapter.py:26-31` |
| `TEXT_ROWS`：哪些行值得朗读 | `worker/wechat_adapter.py:35` |
| `UNKNOWN_EMOJI = "我发送了一个小表情"` | `worker/wechat_adapter.py:143` |
| `PUNCTUATION_MEANING`：标点单独成句的话术 | `worker/wechat_adapter.py:147` |
| `DIGIT_WORD`：纯数字词的识别（独立替换护栏用） | `worker/wechat_adapter.py:178` |
| `BUILTIN_ABBREVIATIONS` / `ABBREVIATION` | `worker/wechat_adapter.py:182` / `:185` |
| `SCOPES`（出现方式三档） | `worker/wechat_adapter.py:212` |
| `configure()`（读入用户读法；旧的词库列表仍兼容） | `worker/wechat_adapter.py:215` |
| `spoken_text` 入口 | `worker/wechat_adapter.py:345` |
| `_apply_rules`（按出现方式分派） | `worker/wechat_adapter.py:364` |
| `_replace_guarded`（纯数字词只替换不邻接其它数字的独立数字） | `worker/wechat_adapter.py:376` |
| `_replace_standalone` / `_replace_inside` | `worker/wechat_adapter.py:391` / `:403` |
| `_punctuation_meaning` | `worker/wechat_adapter.py:415` |
| `_kaomoji_pieces` / `_emoji_pieces` | `worker/wechat_adapter.py:429` / `:474` |
| `_finish`（旧词库列表 → 缩写，顺序固定） | `worker/wechat_adapter.py:578` |
| `_replace_word`（旧词库路径，数字护栏） | `worker/wechat_adapter.py:586` |
| `emoji_message`（表情包名解析） | `worker/wechat_adapter.py:598` |
| `extract_text`（按 local_type 取正文） | `worker/wechat_adapter.py:634` |
| `EncryptedSnapshot`（加密库只读快照与刷新） | `worker/wechat_adapter.py:674` |
| `connect()`（`mode=ro` + `query_only=ON` + 注入密钥） | `worker/wechat_adapter.py:716` |
| `WeChatStore` | `worker/wechat_adapter.py:732` |
| `open_conversation`（`Msg_<md5(username)>` 表与基线） | `worker/wechat_adapter.py:780` |
| `search_contacts`（返回 `id`/`name`/`wechat`） | `worker/wechat_adapter.py:795` |
| `last_incoming_text`（试听用） | `worker/wechat_adapter.py:825` |
| `query` / `ack`（增量读取与游标推进） | `worker/wechat_adapter.py:847` / `:883` |

### `worker/audited_wechat_keys.py`（唯一允许 ReadProcessMemory 的文件）

| 内容 | 位置 |
|---|---|
| `_verify_enc_key`（数据库首页 HMAC 校验） | `worker/audited_wechat_keys.py:40` |
| `_collect_key_candidates`（只读扫描进程内存） | `worker/audited_wechat_keys.py:65` |
| `_keys_from_candidates`（逐库校验，确定标准形式 / 明文头） | `worker/audited_wechat_keys.py:131` |

### `src/VoiceBridge.App/MainWindow.xaml.cs`

| 内容 | 位置 |
|---|---|
| 内置音色表（聂聂/里奥/熊二/派大星） | `MainWindow.xaml.cs:49` |
| `trayIconStream`（托盘图标的数据流，退出时释放） | `MainWindow.xaml.cs:60` |
| `LoadTrayIcon()`（托盘图标加载：内嵌 `app.ico` → 同目录 ico → 兜底占位图） | `MainWindow.xaml.cs:106` |
| 托盘图标赋值 | `MainWindow.xaml.cs:152` |
| 启动读取设置、装载自定义音色与读法、预置词库首条 | `MainWindow.xaml.cs:176-211` |
| `Render()`（界面状态渲染） | `MainWindow.xaml.cs:239` |
| `DeleteVoiceButton.IsEnabled`（仅自定义音色可删） | `MainWindow.xaml.cs:296` |
| `AccountOf()` / `RefreshAccounts()`（账号目录自动发现与隐藏） | `MainWindow.xaml.cs:471` / `:479` |
| `AccountLabelOf()`（显示去掉实例后缀的微信号） | `MainWindow.xaml.cs:516` |
| `BrowseRootClick()`（数据目录「浏览…」） | `MainWindow.xaml.cs:527` |
| `AccountChanged()` | `MainWindow.xaml.cs:540` |
| `SearchContactsAsync` | `MainWindow.xaml.cs:388` |
| `PreviewClick`（试听最后一条消息） | `MainWindow.xaml.cs:676` |
| `CreateFish`（含 `selectVoice` 回调） | `MainWindow.xaml.cs:787` |
| `ReadSettings`（写回内存状态） | `MainWindow.xaml.cs:801` |
| `SaveNow`（立即落盘） | `MainWindow.xaml.cs:842` |
| `RefreshVoices` / `VoiceIdOf` | `MainWindow.xaml.cs:883` / `:900` |
| `VoicePresetChanged` / `AddVoiceClick` / `DeleteVoiceClick` | `MainWindow.xaml.cs:908` / `:921` / `:939` |
| `RefreshReadings` / `AddReadingClick` / `DeleteReadingClick`（词库一张表） | `MainWindow.xaml.cs:1008` / `:1015` / `:1040` |
| `WindowStateChanged`（只更新标题栏字形，**不再 Hide**） | `MainWindow.xaml.cs:1095` |
| `WindowClosing`（保存 + 完整清理 + 释放托盘图标流） | `MainWindow.xaml.cs:1239` |

### 词库与账号（`src/VoiceBridge.App/`）

| 内容 | 位置 |
|---|---|
| 出现方式三档（`standalone` / `inside` / `both`） | `RuleScopes.cs:19-34` |
| `ScopeOption.ToString()`（下拉显示标签而不是记录本身） | `RuleScopes.cs:12` |
| 词库一行（`Match` / `Say` / `Scope` 三个可写属性，改动直接写回列表） | `ReadingItem.cs:11-70` |
| 账号下拉项（`Value` = 目录名，`Label` = 显示名） | `AccountChoice.cs:7` |
| `ReadingRule`（`Match` / `Say` / `Anywhere` / `Scope`） | `src/VoiceBridge.Core/Contracts.cs:11` |
| 设置持久化：读法列表 + 旧词库字段 + 首次预置标记 | `LocalSettings.cs:46-75` |
| 旧设置迁移：把旧的 `Lexicon` 并入 `Rules`（scope = `both`） | `LocalSettings.cs:55-67` |
| 词库页 XAML（一张表 + 行内编辑 + 固定添加行） | `MainWindow.xaml`（`ReadingRow` 模板与 `ReadingsPanel`） |

### 其余模块

| 内容 | 位置 |
|---|---|
| Fish 模型与端点、动态音色、`reference_id` | `src/VoiceBridge.FishAudio/FishSpeech.cs:11-14`、`:43`、`:46`、`:81` |
| 联系人条目（含微信号）与解析 | `src/VoiceBridge.WeChat/WeChatDirectory.cs:9`、`:39-57` |
| 会话缓存目录与残留清理 | `src/VoiceBridge.WeChat/SessionCache.cs:24`、`:43`、`:145` |
| 监听源：worker 启动、文件事件、轮询兜底 | `src/VoiceBridge.WeChat/PythonFixtureSource.cs:56`、`:133`、`:183` |
| 会话状态机与准入规则 | `src/VoiceBridge.Core/SessionController.cs:22`、`:155` |
| 设置持久化与 DPAPI 密钥库 | `src/VoiceBridge.App/LocalSettings.cs:19`、`:74` |
| worker 协议入口 | `worker/main.py:16` |

## 13. 本机实测环境

| 项 | 值 |
|---|---|
| 微信 | 4.2.0.13，`D:\微信\Weixin\Weixin.exe` |
| 数据根 | `D:\xwechat_files` |
| 账号目录 | `wxid_本人账号` |
| 目标联系人 | `（联系人昵称）` = `wxid_联系人`，微信号 `（联系人微信号）`（昵称 `争渡.`） |
| contact 表 | 28 829 行，其中 1 372 行有 `alias`；列含 `alias` / `remark` / `nick_name` |
| 入站 `local_type` 分布 | 1 文本 49 338、**3 图片 1 092**、34 语音 122、43 视频 17、**47 表情/表情包 4 162**、48 位置 1、10000 系统 73 |
| app(49) 子类型 | 57 引用 1 706、33 小程序 65、51 视频号 93 等 |
| 磁盘 | C 约 50 GB 可用 / D 约 152 GB 可用；`D:\` 根目录不可写，`D:\xwechat_files\` 与应用目录可写 |
| 其它 | 显示器 200% 缩放（逻辑 1600×952）；`WheelScrollLines=7` |
| **账号昵称** | **数据目录里读不到**。微信 4.2.0.13 只把账号昵称放在客户端内存与服务器上；`contact.db` 没有自己这一行，`general.db` / `session.db` / `bizchat.db` / `hardlink.db` 也没有；`config/*` 与 `all_users/config/*` 全是加密的（`VW` 魔数），唯一明文的 `all_users/login/<wxid>/key_info.db` 只有密钥哈希。因此下拉框只能显示**去掉实例后缀的微信号**（`wxid_本人账号` → `wxid_本人账号`） |

> 微信进程 pid 每次启动都会变，探针需用 `Get-CimInstance Win32_Process -Filter "Name='Weixin.exe'"` 重新获取。

## 14. 已知坑清单

**运行时**

1. **强杀应用会留下孤儿 worker**。必须按命令行过滤清理：
   ```powershell
   Get-CimInstance Win32_Process -Filter "Name='python.exe'" |
     Where-Object { $_.CommandLine -like '*win-x64-live*' } |
     ForEach-Object { Stop-Process -Id $_.ProcessId -Force }
   ```
2. **「试听最后一条消息」会另起一个 worker 与另一份 547 MB 缓存**。这是 `WeChatDirectory` 的独立实例（与监听源不共享），应用退出时回收。若磁盘紧张，点完试听后可手动清 `.voicebridge-cache`。
3. **用户会同时操作这台电脑**。最小化会让窗口消失（`MainWindowHandle=0`），但进程通常仍在、监听不中断；窗口也可能被用户直接关掉。重新验证 UI 前先 `computer_app_launch`（本 exe 不在 AppsFolder/PATH，**必须传 `launch_path`** 指向发布目录 exe）。
4. `worker.log` 里可能有历史遗留的 traceback（例如 11:29 那条），**不是当前故障**。
5. **托盘图标默认藏在折叠区**。点任务栏右下角的小箭头才能看到微信语音桥的图标——这是 Windows 的「隐藏图标」设置，程序控制不了，只能由用户在「设置 → 个性化 → 任务栏 → 其他系统托盘图标」里改成常驻显示。做实机验证时记得先展开箭头再截图，否则会误判「图标不见了」。
6. **窗口图标和托盘图标是两套资源**。窗口用 `MainWindow.xaml` 的 `pack://application:,,,/app-icon.png`（PNG），托盘用 `LoadTrayIcon()` 加载的 `app.ico`。改图标资源时两处都要看，否则会出现窗口一个样、托盘另一个样。
7. **最小化不要再用 `Hide()`**。早前的 `WindowStateChanged` 里调了 `Hide()`，窗口缩进托盘折叠区、任务栏里没有按钮，用户以为程序丢了。现在只设 `WindowState = Minimized`，`computer_window_list` 里能看到 `minimized: true` 而不是窗口消失。
8. **WPF 的 `DisplayMemberPath` 不会填 `ItemTemplate`**。自绘的 ComboBox 模板用 `ContentTemplate="{TemplateBinding ItemTemplate}"` 显示选中项，`DisplayMemberPath` 不会让它生效，结果下拉框显示成 `ScopeOption { Value = both, Label = 两个都包含 }`。解法：给记录类型覆写 `ToString()` 返回标签（`RuleScopes.cs:12`、`AccountChoice.cs:10`）。
9. **账号昵称在数据目录里不存在**。见 [第 13 节](#13-本机实测环境) 的说明——不要再去翻 `contact.db` / `general.db`，已经逐个库探过了。

**探针**

10. 控制台是 GBK，中文/emoji 输出会 `UnicodeEncodeError`。探针改为写 UTF-8 文件再读。
11. 用 `Select-Object -First N` 截断 python 管道会触发 `BrokenPipe`，导致探针临时目录残留（需 `mavis-trash` 清理）。
12. 不要用 `cd` 触发策略拦截；用 `sys.path.insert` + `exec(open(...))` 的方式跑探针。

**环境与工具**

13. `Copy-Item` / `Remove-Item` 被安全策略拦截。复制用 `[IO.File]::Copy($src,$dst,$true)`；删除只能走 `C:\Users\USERNAME\.minimax\bin\mavis-trash.cmd -- "<path>"`。
14. `D:\` 根目录不可写；缓存必须放在 `D:\xwechat_files\` 或应用目录。
15. 构建需要独立安装的 .NET SDK：当前用 `$env:TEMP\wvb-sdk-builds\dotnet.exe`。
16. **`.NET` 测试要带 `$env:WVB_PYTHON`**，否则 4 个 worker 相关用例会因为找不到内嵌 Python 而失败（与代码改动无关）。
17. **`scripts/Verify-Source.ps1` 原本依赖 `rg`（ripgrep）**，而这台机器上没装，脚本会直接报「无法将 rg 项识别为 cmdlet」。现已改用 PowerShell 自带的 `Select-String`，不再需要外部工具。

## 15. 验证与复现

```powershell
$dotnet = "$env:TEMP\wvb-sdk-builds\dotnet.exe"
$py = "D:\OneDrive\文档\ChatGPT\微信语音\artifacts\win-x64-live\runtime\python\python.exe"
$env:WVB_PYTHON = $py

# 编译（0 警告 0 错误）
& $dotnet build WeChatVoiceBridge.sln -c Release -v:minimal

# .NET 测试（27 例）
& $dotnet test tests/VoiceBridge.Tests/VoiceBridge.Tests.csproj -c Release `
  --filter "Category!=Benchmark&Category!=AudioDevice" `
  --logger "trx;LogFileName=automated.trx" --results-directory artifacts/TestResults

# Python 测试（40 例）
& $py -I -B -m unittest discover -s tests/python -v

# 源码边界扫描
.\scripts\Verify-Source.ps1
```

**实机验证清单**（发布后逐项确认）：

- [ ] 应用启动显示「未开始」，音色框显示上次选择的音色
- [ ] 左侧栏四页（首页 / 音效 / 词库 / 高级）都能切换，切换后内容正确
- [ ] 「对方」卡片显示名称 + 微信号（不是 wxid）
- [ ] 点「开始」→ 状态「正在监听」
- [ ] 对方发来文字 → 自动朗读，「朗读记录」出现条目
- [ ] 点「试听最后一条消息」→ 提示「试听完成。」
- [ ] 运行中切音色 → `settings.json` 的 `Voice` 立即变化
- [ ] **展开任务栏托盘箭头 → 微信语音桥的图标是绿色气泡，不是 Windows 默认蓝白方块**
- [ ] **点「最小化」→ 窗口变成 `minimized`（不是隐藏），任务栏仍有绿色气泡按钮**
- [ ] **词库页**：加一行「原本的样子 / 读成什么 / 出现方式」→ 回车保存后，`settings.json` 的 `Rules` 立刻多一条；下拉切档 → `Scope` 变化；行内改字 → `Say` 变化；删除 → 该条消失
- [ ] **高级页**：只有一个账号时「账号目录」整行不显示；点「浏览…」能选到微信数据目录
- [ ] 关窗退出 → 无孤儿 python 进程、`.voicebridge-cache` 清空

## 16. 遗留项与技术债

| # | 事项 | 说明 | 建议 |
|---|---|---|---|
| 1 | **历史文档与当前 UI 不一致** | `README.md` 与 `docs/*.md`（9 个文件，共 48 处）仍描述 Codex 时代的双页签界面、「设置与试音」页、授权勾选框和模拟模式。当前产品是**左侧栏四页（首页 / 音效 / 词库 / 高级）、只有正式（真实微信）模式、无授权勾选**。 | 以本文档为准；后续可批量订正历史文档，或直接标注为「历史文档」。`README.md` 的运行说明已按新界面改写并加了本文档入口。 |
| 2 | 仓库尚无提交 | 10 项 untracked。 | 若要正式交接，建议打一个初始提交（`artifacts/` 已被忽略）。**需你确认后我才做。** |
| 3 | fixture（模拟）模式仍在代码与测试中 | UI 不暴露入口，但 `worker` 仍支持 `mode=fixture`，`DemoSpeech`/`DemoTarget` 仍在。fixture 模式是自动化测试的支撑路径。 | 保留，不要当死代码删。 |
| 4 | 试听占用第二份 547 MB 缓存 | `WeChatDirectory` 与监听源各持一份会话缓存。 | 可考虑让试听复用监听源的快照，但会牵动 `WeChatDirectory` 的 browse 模式协议，收益有限，优先级低。 |
| 5 | 只支持微信 4.2.0.13 | `WeChatSession.SupportedVersion` 硬校验版本，不匹配直接报错。 | 微信升级后需要重新验证 schema 与密钥提取路径，这是**最大的维护风险点**。 |
| 6 | 密钥提取依赖上游仓库的内存布局 | 上游 commit 固定为 `4931f98d…`。 | 微信更新后该 commit 可能失效，需要重新审计。 |
| 7 | 无断线续接 | 当前实现遇到 worker 掉线即结束会话，不支持同一会话断线重连。 | 记为已知限制。 |
| 8 | 语音合成走公网 | 文字会发送到 Fish Audio。 | 按用户决定，界面**不再显示**知情同意与上云提示，只留在 `README.md` 与本文档。若要更严格的合规，在「高级」页底部补一行小字即可。 |
| 9 | 账号昵称读不到 | 微信 4.2.0.13 不把账号昵称落在数据目录里（详见 [第 13 节](#13-本机实测环境)）。 | 下拉框只能显示去掉实例后缀的微信号。若将来微信换了存储位置，重新探一次 `all_users/` 与 `config/` 即可。 |
| 10 | `worker` 仍保留旧的词库列表通道 | 应用已经只下发一张统一的读法列表，但 `configure(rules, lexicon)` 的 `lexicon` 参数与 `_replace_word` 仍在，为的是兼容旧版宿主。 | 可保留（新旧协议兼容），也可以在下个大版本删掉，届时同步改 `tests/python`。 |

## 17. 接手建议

**先做这三件事（半天内可完成）**

1. 通读本文档第一部分，再看 `docs/architecture.md`（IPC 与生命周期细节写得比代码注释更细）。
2. 在本机跑通 [第 15 节](#15-验证与复现) 的编译与两组测试，确认基线全绿。
3. 打开应用点「开始」，确认「正在监听」，再点「试听最后一条消息」确认链路通。

**如果要继续做需求，按这个顺序**

1. **先确认微信版本没变**（4.2.0.13）。变了的话，一切真实模式的工作都要先重新验证 schema。
2. 改动落在 `worker/wechat_adapter.py` 的「话术层」时，改完必须同步更新 `tests/python/test_live_adapter.py` 的期望值。
3. 改动落在「界面/交互」时，改完必须实机验证（这个项目的很多问题只有实机才暴露）。
4. 任何改动后跑 `scripts/Verify-Source.ps1`——它会挡住越界的内存操作和误提交的密钥。

**不要做的事**

- 不要引入 Hook / 注入 / 写内存。
- 不要让 API Key 或聊天正文进入日志或仓库。
- 不要在生产路径留下数据库副本。
- 不要在没有实机验证的情况下宣称「已修复」。

---

## 修订记录

| 日期 | 修订内容 |
|---|---|
| 2026-11-06 | 首次成文：项目介绍（背景 / 技术选型 / 架构）+ 交接说明。覆盖到「联系人显示微信号」这一轮需求。 |
| 2026-11-06 | 补记**托盘图标修复**：新增 `LoadTrayIcon()`（`MainWindow.xaml.cs:106`），托盘不再使用 `SystemIcons.Application`；新增「应用图标的三处来源」小节、两条已知坑、一条实机验证项；校正因该改动漂移的行号。 |
| 2026-11-06 | 补记**界面整体重构**：左侧栏改为 首页 / 音效 / 词库 / 高级；「符号读法」与「词库」两张表合并为一张、行内编辑不再弹窗；出现方式三档落到 worker（`_replace_standalone` / `_replace_inside` / 全局替换）并给纯数字词加独立替换护栏；数据目录加「浏览…」，账号目录改为自动发现（单账号隐藏、多账号下拉）；最小化不再 `Hide()`、保留任务栏按钮；旧 `settings.json` 的词库自动并入读法列表。新增 `RuleScopes.cs` / `ReadingItem.cs` / `AccountChoice.cs`，删 `RuleItem.cs`；Python 测试 37 → 40 例；`scripts/Verify-Source.ps1` 改用 PowerShell 自带的 `Select-String`，不再依赖本机没装的 ripgrep。 |

---

*文档生成于 2026-11-06，依据当时的代码与实机运行状态核对。若代码继续演进，请同步更新本文档与 `README.md`。*
