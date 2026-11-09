# 微信语音桥 1.4.0 开发交接（2026-11-07）

> **本文是 1.4.0 时期的历史快照，不再是接手入口。最新状态请读 `docs/HANDOVER-CURRENT.md`，改动流水见 `docs/WORKLOG.md`。** 旧的 PROJECT-HANDOVER.md 停留在 1.2。
> 用户最新要求：马上写清已做/未做/怎么接手，避免 Token 用尽导致工作丢失。
> 本文写入时 1.4.0 仍未发布，不要把半成品标成最终成功。当前正式线上与桌面快捷方式仍是 1.3.3。

## 给下一位的可直接使用提示词

你接手 Windows WPF 项目 D:\OneDrive\文档\ChatGPT\微信语音。先读本文件，再读 AGENTS.md。当前源码已改到 1.4.0（Directory.Build.props 是版本唯一来源），多人/多 API/音色库/指南大部分源码已完成，必要模拟冒烟已通过，但仍有明确界面缺陷和一个最优先的更新安装故障待修。

请延续已授权开发，别重新做架构、别要求用户重新提供现有 Fish Key。不要打印或写入源码/文档任何真实密钥。用户强调“先把事情做完，不要过度测试”，只补关键启动、取消、下载恢复、真实隔离安装助手路径验证，修复后构建打包供用户试用。

最优先修复：
1. 朋友下载更新后点“重启安装更新”无反应，装不上。
2. 退出重开后部分下载与完整下载都丢失，要求断点续传、已校验包复用、重开恢复；离线也应能安装此前已完整校验包。
3. 安装按钮改醒目颜色、点击立刻反馈，失败有脱敏原因。
现有 speech_providers 子代理正在负责更新器底层，先查其状态/消息和 docs/updater-handover-1.4.md，避免与它冲突。主窗口更新 UI 还没接这些新接口。

然后修已观察到的 ComboBox 显示 record.ToString() 技术信息的问题，完成其余集成、必要验证、发布 ZIP/EXE。不要把真实微信多人监听或真实各家 API 标成已实测。微信多版本自动适配用户明确放在后续一轮，本轮不要启用不受审计版本。

## 1. 用户本轮最终需求（含追加）

- 更新说明弹窗要与首页分开：独立底色、边框、阴影；顶部仅右侧叉号，保留拖动和 Esc/知道了关闭。
- 同一微信账号可同时监听多名明确选择的一对一联系人：首页“＋”添加卡片，每人绑定自己的音色。当前实现上限 8 人，底层保护上限 16。
- 原“音效”页变为“音色库”，按 API 服务分组，可增删改音色。
- 多个 API 可同时启用。音色严格属于某个具体 API 配置，不能悄悄把 Fish 的音色交给豆包；服务未启用时应解释并拒绝该选择。
- 每人开头有记忆，按真实账号+联系人 ID 保存，与昵称无关。
  - 自动：单人不加，多人“昵称说”。
  - 不加开头。
  - 固定“昵称 + 说”。
  - 自定义开头，切到“不加”后也保留此前自定义内容。
  - 移除再添加联系人、重开软件后保留绑定和开头。
- 最近联系人最多 12 条、搜索词最多 8 条，按账号分开显示，单项删除/清空；不因清空历史删除联系人偏好。
- 头像尽量读取真实联系人表实际存在的 URL 字段；没有就文字头像，不伪造头像或字段。
- 多人消息共用一个有界播放队列，不重叠；结束丢弃迟到结果。
- 使用说明写详细到电脑新手能用，最后放逐版本更新日志。弹窗和日志共用 ReleaseHistory。
- 首次没有完成配置的用户默认打开使用说明，已有配置的更新用户进首页；任何启动都 IDLE。
- API 先做好通用兼容接口，尽量广泛覆盖常见云/本地项目；异构接口独立适配，不吹“所有项目都通用”。
- 用户补充：容易稳定热切换的都允许。现实现：
  - 音量即时作用当前播放。
  - 输出设备下一段生效。
  - 每人音色/开头、API 配置/凭据/模型、词库下一条开始合成生效。
  - 原文预览开关可热切，关闭会清空当前日志并隐藏迟到的预览。
  - 账号/数据目录/监听名单需要重新建基线，要求先结束，并显示只读光标和提示。
- 左下角仅有更新时出现的“更新”按钮更靠右，字号与状态 13 一致，加轻边框；已写样式但还有下述模板显示细节需检查。
- 最后追加的安装/下载恢复故障是现在最高优先级。
- 微信最新多版本识别/适配：仅列后续工作，本轮保留原受审计支持范围。

## 2. 已完成的源码和验证

### 多联系人 Worker（子代理 multi_source 已完成）

文件：
- worker/main.py
- worker/wechat_adapter.py
- 新 worker/multi_fixture_adapter.py
- src/VoiceBridge.WeChat/PythonFixtureSource.cs
- src/VoiceBridge.WeChat/WeChatDirectory.cs
- 新 tests/python/test_multi_contact.py
- 新 tests/VoiceBridge.Tests/MultiWorkerTests.cs
- 新 tests/VoiceBridge.Tests/MultiSessionTests.cs

接口：
- PythonFixtureSource 构造末尾 conversations: IReadOnlyList<string>?。
- PrepareAsync 的 Target 必须对应 conversations 首人。
- start/ready IPC 精确回显白名单，多人遇到旧 Worker 没有数组会拒绝运行。
- 同 Worker、同账号、共享缓存与数据库快照，每 shard 一次连接。
- 各 (peer, shard) 独立水印，ACK 后才推进；消息 Key 包含 peer 防跨人碰撞。
- 每批最多 32 条，稳定堆合并，保留每张表 local_id 已消费前缀，不静默跳行。
- 不承诺未来才提交的消息有严格全局时间顺序。
- ContactEntry(string Id,string Name,string Wechat,string? AvatarUrl=null)。
- AvatarUrl 只从实际存在的 small_head_url / small_head_img_url / head_img_url / big_head_url 列读取，现 worker 和 C# helper 只接受 HTTPS。
- 模拟身份为 demo-local / demo-peer、demo-peer-2 等 demo-*；RequestDemo() 轮流注入这些联系人。

验证：
- Python 51 项（原 41+新增10）PASS。
- Worker 相关 C# 11 项 PASS。
- 新 MultiSessionTests 7 项 PASS：跨人同 Key 不互去重、重复/白名单/方向过滤、串行播放、取消和迟到结果、新会话、账号/8人限制。
- 此部分未实测真实微信多人/真实头像列。

### 多 API 后端（speech_providers 原任务已完成，后正在修更新器）

文件：
- 新 Core/SpeechProfiles.cs
- 新 FishAudio/SpeechProviderFactory.cs、AudioSignature.cs
- 新 tests/VoiceBridge.Tests/ProviderTests.cs
- 新 docs/providers.md（官方来源与精确配置说明）

类型：
- SpeechProviderKind: Fish / OpenAiCompatible / AzureSpeech / Doubao / GptSoVits。
- SpeechProviderProfile 的 init 字段：
  Id, Name, Kind, Endpoint, Model, CredentialId, Enabled, ResourceId, AppId,
  Latency, TimeoutSeconds, AudioFormat。
- VoiceProfile：
  Id, Name, ProviderId, VoiceId, Language, ReferenceAudioPath, ReferenceText,
  PromptLanguage, Instructions, Speed, IsCustomVoice。
- API 密钥不进以上对象，只用 CredentialId 引用现有 DPAPI 凭据库。
- SpeechProviderFactory 长生命周期，Create(profile,voice,Func<string>readKey) -> ISpeech，返回值不单独 Dispose；窗口关掉才 Dispose 工厂。
- Validate / ValidateCredential / RequiresCredential / DefaultEndpoint / ResolveEndpoint / Label / ResetFailures。
- 每条开始时捕获配置和凭据快照。401/403 同配置暂停，改密钥或显式重试/保存可恢复。
- 429 遵循 Retry-After，Fish 免费有界重试，付费不自动重试，无跨服务或付费 fallback。
- 禁重定向，远端 HTTPS，仅 loopback 允许 HTTP。Fish 类型只可官方地址。
- OpenAI-compatible 支持 origin、结尾 /v1、完整 /audio/speech 路径；必须配置 Model。
- Azure 用官方 SSML REST；豆包官方 HTTP v3 JSON/base64 分段响应；GPT-SoVITS API v2 需要服务端可读参考录音路径+原文。
- 先完整 MP3/WAV 播放；不把 HTTP 块当独立 MP3。
- 新 39 个 ProviderTests PASS，未调用真实云 API。
- 子代理正确配置 WVB_PYTHON 后曾跑全常规 .NET 110/110 PASS（是后面 root 新增5项前的计数）。

### 说明、日志、弹窗（guide_dialog 已完成）

文件：
- ReleaseNotesDialog.xaml/.cs
- 新 UserGuide.xaml/.cs
- 新 ReleaseHistory.cs
- 新 docs/user-guide.md（约9千字，13章节）
- 新 docs/guide-ui-validation.md

- UserGuide 无参数、无外层 ScrollViewer，已替换 MainWindow 旧说明页。
- 3分钟上手、13折叠章节、快捷定位、最下面6版日志（1.2/1.3.0/1.3.1/1.3.2/1.3.3/1.4.0）。
- ReleaseHistory.Through(currentVersion) 过滤高于本版的条目。
- 弹窗暖白卡片、细边框、真实 DropShadow + DWM 窗影；不启用透明窗口。
- 旧“CaptionHeight=32”的测试不能照搬，新值 50（留白+标题区域）。
- 隔离窗口 920/600 DIP、跳转展开、拖动命中、叉号/知道了/Esc 关闭 PASS。
- 隔离工程/截图：
  D:\USERNAME\CodexScratch\Temp\wvb-guide-ui-a609640d9ee14e00b7c61056bc796bbe

### Root 已整合

Core：
- Contracts.cs 新 IMessageSpeech，按原始已验证 ChatMessage 选择音色；ProcessingEvent 加可选 ConversationId。
- SessionController.cs 保留单 Target overload，新增 IReadOnlyList<Target> overload：
  同账号 1–8 不重复一对一联系人，防调用者修改数组；
  Account+Conversation+MessageKey 去重，单有界 Channel、单消费者。
- SetPreview(bool) 运行中变更预览。
- 新 ContactPreferences.cs：
  ContactProfile、AnnouncementMode、ContactHistory、SearchHistory、RecentContacts。
- 新 ContactPreferencesTests.cs 5 项已写，首次全套构建被 xUnit2031 拦过，已改用 Assert.Single(collection,predicate)，尚未再次跑全套。

App：
- MainWindow.xaml 主窗口已接多人卡片、分组音色库、多 API 列表、新指南、测试音色选择器。
- 新 MainWindow.Library.cs 管模型迁移、每人偏好/历史、头像、API/音色编辑、RoutingSnapshot。
- 新 ContactCard.cs、ProfileEditors.cs、AvatarCache.cs。
- LocalSettings.cs 扩字段，旧 Voices/Voice/FishKeyName 等字段保留，KeyStore 继续 fish-keys.dpapi 兼容原文件。
- 新字段：
  VoiceLibrary, Providers, ContactProfiles, SelectedContacts,
  ContactHistory, SearchHistory, SetupCompleted, TestVoiceProfileId。
- 旧单联系人和音色自动迁到默认 Fish profile，已有可用配置进首页，缺配置进说明页。
- profile 以账号+ConversationId 找回，移除联系人只是 Selected=false，不丢偏好。
- 统一 RoutedSpeech 在出队时读取不可变 RoutingSnapshot，按该消息联系人取 voice/provider/key/prefix，不读后台 WPF 控件。
- ProviderEditor 可添加/编辑 endpoint/model/key/resource/appid/timeout/audioformat/latency。
- VoiceEditor 可添加/编辑 provider/voice/lang/reference/instructions/speed/custom-voice。
- 不允许有绑定音色的 API 直接改 Kind；换音色所属 API 时清空音色 ID，要求重新填写。
- 默认 Fish profile Id=fish-default，类型固定，可停用，保留原免费/Drama 3开关。
- 音色仍绑定 disabled API 时可以看出未启用；选择未启用服务音色会提示并退回。
- 同名密钥“添加/替换”支持替换值热更；启用的 API 正在引用的密钥不能直接删。
- MainWindow.Interaction.cs 加新控件只读提示，监听时仍允许改音色和开头。
- 新 MainWindow.Browsing.cs 序列化联系人搜索/试听读取，按 root/account/pids 绑定，取消时重置 helper，开始监听前在 SessionSource.PrepareAsync 关闭浏览 Worker。
- 试听从数据库读取阶段就受 manualCancel 跟踪，不用 CancellationToken.None；结束会等待并关闭 helper，避免迟到试听播放。
- WindowsPlayer.cs 新 SetVolume/SelectDevice，当前音量可热调，设备下一段生效。
- 左下角 SidebarUpdate 独立 SidebarUpdateButton 样式：右对齐、16间距、13号、轻绿边框。
- 注意新 Legacy SelectedCard/VoicePresetBox 控件仍保留但隐藏，用于兼容旧代码，不再是主流程。

头像：
- 搜索/添加后才可下载，启动仅读自己已缓存的头像。
- 限官方 qlogo.cn/qpic.cn/*.weixin.qq.com，禁重定向，不带 API Key；HTTP 官方头像可升级 HTTPS（但 worker 目前只返回 HTTPS，可能导致 HTTP 原字段无头像）。
- 256KiB、3并发、6秒整段超时，小图解码96像素，存自己 %LOCALAPPDATA%\WeChatVoiceBridge\avatars。
- 无真实 URL/下载失败则文字头像。
- “清理缓存”会同时清头像缓存，但还有下述并发清理细节可顺手检查。

## 3. 必须接着做的事项（按优先级）

### P0 更新安装/下载恢复（底层代理仍在做）

已找到确定问题：
- UpdateClient.DownloadAsync 原实现每次随机 GUID.part，finally 无条件删 partial，无法续传。
- MainWindow.CloseUpdatesAsync 退出时删 verified ZIP 和 staging，完整下载也丢。
- UpdateProcess.Start 原实现 fire-and-forget，不知道 helper 是否真启动。
- Apply 有多个静默 return1，失败不留原因；canRestart 前失败甚至不重开旧版，所以可能表现“无反应”。
- 本机 updates 暂无朋友现场失败日志，因此不能宣称已定位朋友唯一触发条件。

代理 ownership：
- Core/UpdateClient.cs、UpdateInstaller.cs、新 UpdateDownloadState.cs（如需要）
- App/UpdateProcess.cs
- 新 UpdaterRecoveryTests.cs，可适配旧 UpdateTests 预期
- docs/updater-handover-1.4.md（已要求代理马上补进展）

约定接口（此交接写入时仍需核实已落地与否）：
- DownloadAsync 签名保持，改为稳定 sha/size 标识文件与安全 Range+If-Range。
- GetCachedAsync(manifest,updates,token) -> Task<string?>：重新校验后复用完整 ZIP。
- ReadPendingAsync(updates,token) -> Task<UpdateManifest?>：从自己持久元数据恢复待下/已下 manifest，过滤 <= 当前版本；支持离线已有包安装。
- UpdateProcess.StartAsync(stage,token)：等 helper-ready 握手、检查提前退出。
- helper 脱敏 ready/error/result 状态持久化；失败原因可见；仍等旧进程退出、备份/回滚。
- 不触碰用户 settings.json/密钥/原数据库；不改正式 feed。

Root 后续必须改 MainWindow.Updates.cs（目前还是旧实现，未接新接口）：
1. 启动/检查先恢复 ReadPendingAsync + GetCachedAsync，完整包变“重启安装”，部分包变“继续下载”；网络坏也保留可安装的已验包。
2. fresh manifest 到来时按版本+sha选择，不将旧缓存冒充新包。
3. CloseUpdatesAsync 停止删除 verified ZIP；取消改“暂停”并保留 partial，临时 staging 可安全清理。
4. 安装前 SaveNow，等 StopAsync，await StartAsync 成功后才 handoff Close。
5. 按钮 busy 时不可被 RenderUpdate 重新启用；立即显示“正在准备/正在退出/正在安装”。
6. 安装按钮颜色改明显 Accent/Primary，文字白色清晰；确认停止监听的二次点击要显眼，避免用户以为没响应。
7. 非预期异常也要脱敏给出提示并恢复按钮，别 async void 未捕获崩溃。
8. 增补 ReleaseHistory 1.4.0 这次 updater 修复条目和指南下载恢复说明。

### P1 已看见的界面缺陷必须修（不是猜测）

实际截图：
D:\USERNAME\CodexScratch\Temp\wvb-ui-140-smoke-e9e5dcace49842d0b4987479292d2993\fresh\home.png
D:\USERNAME\CodexScratch\Temp\wvb-ui-140-smoke-e9e5dcace49842d0b4987479292d2993\fresh\voices.png

首页联系人音色下拉框现在显示：
“VoiceChoice { Id=..., Name=..., ProviderName=..., Label=... }”
即 record.ToString()，而不是友好 Label。功能选择和路由已正常，但这个外观绝不能发。

原因几乎确定：
App.xaml 自定义 ComboBox Template 的选中项 ContentPresenter 目前是
Content="{TemplateBinding SelectionBoxItem}"
ContentTemplate="{TemplateBinding ItemTemplate}"
它忽略 DisplayMemberPath 生成的 SelectionBoxItemTemplate。
应修共享模板使用 SelectionBoxItemTemplate（并保留 ItemTemplateSelector/字符串格式的正常语义），或者为新 ComboBox 显式 ItemTemplate（VoiceChoice.Label、SpeechProviderProfile.Name、ProviderKindChoice.Label）。
不要只修 VoiceChoice.ToString 而漏掉 ProviderEditor 的两个 ComboBox。

额外看见的样式细节：
- PrimaryButton 的字符串 Content 在绿色背景上仍显示黑字，虽然 Button.Foreground=White。这是全局 TextBlock 隐式样式设置 Foreground=Label 的优先级问题。
- 新“安装”按钮建议显式 TextBlock 白字，或修合适的按钮内容模板；不要全局粗暴把任何复杂 Content 都转换字符串。
- SidebarUpdate 目前只改了 Button.FontSize=13，但自生成 TextBlock 的隐式 FontSize=14 可能盖过继承，需在它自己的字符串专用模板中显式绑定字号/前景，确保用户最新要求实际生效。
- 首页 Search/History 全隐藏时仍有一个空的 Group/footer 白框。可当没有搜索/历史/ContactHint时折叠整个该区域，不要留空框。
- 音色库实际分组已显示正常，但每行重复 ProviderName；可保留或精简，非阻塞。

### P2 收尾，不要再过度拓展

- 新 LocalSettings.Load 可补 null 列表规范化，确保旧/缺字段 settings 稳定；不要修改用户实际配置来做测试。
- App 新窗口 WindowClosing 的末尾还存在原来的 direct directory.DisposeAsync 区块，建议统一用 CloseDirectoryAsync（StopAsync 已做，不要再竞态 Dispose）。
- 头像 Clear 与尚未完成 GET 可能竞争写回，可用 cache generation 防清理后迟到结果重写；路径 reparse 检查可参考 UpdateInstaller，但别扩安全工程耽搁功能。
- Root.RenderUpdate 接安装颜色后记得截图只检查一次，不用大批 DPI/压力/benchmark。
- 不要重复已经通过的几十轮测试。必要代码变化后统一跑一次 scripts/Test.ps1 与 Verify-Source。
- README 仍是 1.2 单人/音效/旧安装包文案，须改 1.4.0，链接新指南/providers，明确实际兼容微信版本 4.2.0.13，其他适配下轮。
- docs/architecture.md/security-review.md 更新多人白名单和多API外发范围；保留原只读不保证微信平台合规/账号安全说明。
- 写 docs/release-1.4.0.md：本轮完成项、关键测试、真实未测项、包SHA/大小、待办。
- Publish.ps1 输出全新 artifacts/win-x64-v1.4.0，生成 WeChat-Voice-Bridge-V1.4.0-win-x64.zip；Build-Installer 用该 PayloadDirectory，产单 EXE。
- 检查发布物不含私密设置/数据库/WAL/SHM；DPAPI 文件不会被打入包。
- 仅更新桌面快捷方式到完成后的 1.4.0，不要强关用户正在用的旧版或自动启动监听。
- 本轮 1.4.0 还未交 Muse 发布，不要混称线上成功。先把本地包做完供用户试用；若用户授权/确认发布，再按原通道交接。上一轮“直接联系 Muse 推上线”已经完成的是 1.3.3，不宜当无限期自动发布授权。

## 4. 当前测试证据与限制（不要混淆）

通过：
- 子代理 multi_source：51 Python、11 Worker C#、7 MultiSession C#。
- 子代理 providers：39 Provider 测试；在正确 Python 环境曾完整常规 .NET 110/110。
- Root 两个隔离 WPF 冒烟用同样 1.4.0 UI 源码：
  1. fresh：首次进入说明、IDLE无Worker、偏好保存/移除重加、每人不同voice/prefix、
     实际 Python fixture IPC -> Core单队列 -> 模拟HTTP TTS -> 模拟player，
     热改模型+前缀下一条生效、无重叠、生成中停止不迟到发声、Worker释放。
  2. legacy：旧配置/音色/DPAPI凭据迁移、进首页、保持IDLE。
- 两个结果均 PASS，运行约3秒。
- App/solution 在 Root 后端集成时曾 Release build 0警告0错误。
- root新增 ContactPreferencesTests 的 xUnit2031 已修，但修后尚没再跑完整 Test.ps1。
- Verify-Source 之前 PASS；最终改完再跑一次。

未测试/不要伪造：
- 真正两位微信联系人同时发消息的实机验收。
- 微信真实头像列和真实CDN图片。
- 新增各家云API真实账户、音色质量、费用、延迟。
- 本轮真实 Windows 音频热调与蓝牙切换；隔离冒烟使用模拟player。
- 真实朋友电脑更新安装；需用自建隔离安装目录真实 helper 验证解决机制。
- 大规模性能/多微信版本兼容，均后续。

## 5. 可复用的关键路径和命令

工作目录：
D:\OneDrive\文档\ChatGPT\微信语音

.NET SDK（系统 dotnet 没SDK，用这个）：
C:\Users\USERNAME\AppData\Local\Temp\wvb-sdk-builds\dotnet.exe

Python 3.12 x64（带SQLCipher/zstd）：
D:\OneDrive\文档\ChatGPT\微信语音\artifacts\win-x64-live\runtime\python\python.exe

测试：
& .\scripts\Test.ps1 -Dotnet 'C:\Users\USERNAME\AppData\Local\Temp\wvb-sdk-builds\dotnet.exe' -Python 'D:\OneDrive\文档\ChatGPT\微信语音\artifacts\win-x64-live\runtime\python\python.exe'
& .\scripts\Verify-Source.ps1

直接 dotnet test 时必须设置 WVB_PYTHON，普通验证排除 Category=Benchmark/AudioDevice；不要因没设Python制造假失败。

发布：
& .\scripts\Publish.ps1 -Dotnet '<上方SDK>' -OutputDirectory 'D:\OneDrive\文档\ChatGPT\微信语音\artifacts\win-x64-v1.4.0' -ZipPath 'D:\OneDrive\文档\ChatGPT\微信语音\artifacts\WeChat-Voice-Bridge-V1.4.0-win-x64.zip'
& .\scripts\Build-Installer.ps1 -PayloadDirectory 'D:\OneDrive\文档\ChatGPT\微信语音\artifacts\win-x64-v1.4.0'
& .\scripts\Install-DesktopShortcut.ps1 -InstallDirectory 'D:\OneDrive\文档\ChatGPT\微信语音\artifacts\win-x64-v1.4.0'

Inno Setup：
C:\Users\USERNAME\AppData\Local\Programs\Inno Setup 6\ISCC.exe
使用 /D 定义，不支持 --define；PS5.1 中文脚本保留 UTF8 BOM。
构建/发布脚本已有缓存运行时，不需重装工具。

Root UI 冒烟（只用于隔离，不是产品源码）：
D:\USERNAME\CodexScratch\Temp\wvb-140-prepare-smoke.py
D:\USERNAME\CodexScratch\Temp\wvb-ui-140-smoke-e9e5dcace49842d0b4987479292d2993\
  App.UiFixture.cs / FakePlayer.cs / fixture\ / publish\ / fresh\ / legacy\
- 准备脚本会复制 src/worker，排除 bin/obj/deps，将 LocalSettings/KeyStore/AvatarCache/UpdateProcess 定位到 WVB_UI_ROOT\data，禁自动更新、禁微信路径发现和真实cache清理。
- 仅 fixture 中注入模拟 HttpMessageHandler，替换 WindowsPlayer 为假播放；真实源码没有这些测试后门。
- StartClick 的 WeChatSession.Bind 仅在 fixture 改 null，实际走 demo-local/deemo-peer fixture Worker。
- WVB_PYTHON 必须设上方真实runtime。
- WVB_UI_CASE=fresh 或 legacy，WVB_UI_ROOT 指各自隔离目录。
- 准备脚本 copytree 不能对已存在 fixture 再跑；新建 scratch 工程，或者只同步本次修的文件并重新做相同隔离替换。
- 新 updater 代码落地后若接口改变，先同步并适配 fixture，不要把fixture代码拷回产品。

其他 scratch 修改脚本已执行，勿盲目再次运行（大多有 once 断言）：
wvb-140-home.py / wvb-140-library-ui.py / wvb-140-main.py / wvb-140-preview.py。

## 6. 正式更新通道与 Muse

固定地址：
https://iceland.de5.net/voice-bridge/latest.json
不得换成 aoliao。六字段契约不变：
version,publishedAt,url,sha256,size,notes。
更新 ZIP 名：
WeChat-Voice-Bridge-V<version>-win-x64.zip
分发 EXE 名：
WeChat-Voice-Bridge-V<version>-Setup.exe

1.3.3 上线已由 Muse 与本机实测共同确认：
- ZIP 91925310 字节
- sha256 8d61dbba54269d9e3eddb90d0063809ce0a93186d2d5856790f2f2d0821fb702
- EXE 65210018 字节
- sha256 e2566c14fe88b31eb5040569a7f8468d95bdcea0684541de3b7fac2575b88035
- 客户端新旧版本检查、完整公网 ZIP 下载并复核hash PASS。
这不是本轮1.4的结果。

Muse 本机直连命令存在：
C:\Users\USERNAME\es-tool\muse-call.cmd
长中文说明写 UTF8 文件，用 -TextFile；不要放真实Key/密码。
发布用 -Type publish-voicebridge -Version ... -PackagePath ... -Sha256 ... -Notes ...
同一请求要前台等命令结果，info 只是进度，ok 才是正式回执；超时用 -Check 原 req_id，不重复发布。
需用户授权后才发新版本。交接ZIP/EXE完整路径、大小与hash，让Muse独立核验上传。

注意当前工具 shell 是 PowerShell 7，.cmd 调的是 Windows PS5.1，可能出现 Get-FileHash 找不到。
已验证解决方式是仅对调用进程临时设置：
$savedModulePath=$env:PSModulePath
try {
  $env:PSModulePath="$env:SystemRoot/System32/WindowsPowerShell/v1.0/Modules;$env:ProgramFiles/WindowsPowerShell/Modules"
  & 'C:\Users\USERNAME\es-tool\muse-call.cmd' ...
} finally {$env:PSModulePath=$savedModulePath}
不要为此改 Muse 通道脚本、凭据或另搭通道。

## 7. 不可破坏的边界

- 先读 AGENTS.md。所有真实 DB probe 必须 tempfile.TemporaryDirectory()，在本地 scratch、OneDrive 之外；同批复用快照；关闭 SQLite/子进程后才退出上下文。
- 不把任何 DB/WAL/SHM、密钥缓存、用户配置打进项目/报告/发布包。
- 不修改微信进程内存、注入/Hook/UIA热激活、不发微信消息、不重新写密钥扫描算法。
- 始终手动开始、初始 IDLE，结束取消整条链路；不自动恢复监听。
- 不把无凭据mock当真实云测试，不把模拟多人当实机成功。
- API Key 仅DPAPI，不能提交/回显。用户以前提供过真实Key，不要复制到交接文档。
- Git 仓库大量文件未跟踪/无可靠commit，git status --untracked-files=no 空白不代表没改；保留现有工作，别 reset/clean。
- 不删原数据库/用户录音/用户配置；下载及staging清理只动本程序拥有且验证过的路径。
- 当前用户更重视先交付可用版本，别为了完善程度继续扩展无关功能或反复跑长测试。

