# 工作日志（按版本）

记录本次会话中实际完成、构建与验证过的改动。用户可见的精简版在 `src/VoiceBridge.App/ReleaseHistory.cs`；接手说明见 [HANDOVER-CURRENT.md](HANDOVER-CURRENT.md)。

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
| **1.4.6** | **更新按钮三态配色与居中** | **发布产物 ZIP + Setup.exe，由 Muse 推到线上；线上原版本为 1.4.3** |

**发布状态更正（1.4.6 已上线）**：本会话确实生成了 `WeChat-Voice-Bridge-V1.4.6-win-x64.zip` 与 `...-Setup.exe`，并经 Muse 通道把 1.4.6 推到线上（详见 1.4.6 段）。此处的旧表述是 1.4.5 及更早留下的、已作废。线上正式版自 2026-11-09 起为 **1.4.6**（此前为 1.4.3）。
