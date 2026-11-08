# 语音 API 与音色接入

本版将「API 连接」和「音色」分别保存。一条 API 连接表示一个服务商地址、模型、凭据引用和启用状态。一条音色必须绑定具体连接，不能把 Fish 的音色 ID 交给豆包，也不能因两条连接使用相同协议就自动混用它们的凭据。可以同时启用多条连接，为不同联系人选择不同服务商的音色；所有声音仍通过同一个播放队列依次播放。

## 支持范围

添加 API 与添加音色均分为两步：先选择服务，再填写该服务专用的字段。每个音色都必须填写辨识名称；Fish 音色还需 Voice ID。连接名称、已有密钥、延迟、语速等非必需项收纳在「更多选项」。豆包资源 ID 只在豆包连接中出现，GPT-SoVITS 的参考文件路径与语言只在本地音色中出现。各服务使用独立分类卡片，添加按钮位于卡片外。新增 API 会自动加入添加音色的服务选择，音色根据绑定的 API 归类。

2026-11-07 再次按本文底部官方链接核对字段：Fish 使用 reference_id 与 model header；Azure 使用资源区域/端点、资源密钥与 voice；豆包 HTTP v3 使用 API Key 和 Resource ID（旧控制台鉴权放在更多选项）；GPT-SoVITS v2 的 prompt_text 为可选字段。OpenAI Speech 的字段依据官方 OpenAI documentation 核对，普通聊天端点不属于语音兼容端点。

| 类型 | 接口地址填写方式 | 认证 | 音色标识 | 音频 |
| --- | --- | --- | --- | --- |
| Fish Audio | 固定 `https://api.fish.audio/v1/tts` | Fish API Key | 已获授权的 Voice ID | 完整 MP3 |
| OpenAI 兼容 | 完整合成 URL，或结尾 `/v1` 的基础地址，或仅主机地址 | Bearer API Key；本机服务可留空 | 服务端支持的 `voice` 值 | 完整 MP3 / WAV |
| Azure Speech | 完整 `https://<region>.tts.speech.microsoft.com/cognitiveservices/v1`，或官方提供的自定义完整端点 | Speech 资源密钥 | 对应资源支持的 voice 名称 | 完整 MP3 |
| 豆包 / 火山语音 | 默认 `https://openspeech.bytedance.com/api/v3/tts/unidirectional`，HTTP Chunked v3 | 新控制台 API Key + 资源 ID；旧控制台 App ID + Access Token + 资源 ID | 对应资源授权的 speaker | 完整组装 MP3 |
| GPT-SoVITS API v2 | 默认 `http://127.0.0.1:9880/tts`；需要先自行启动模型服务 | 本地服务通常无需密钥；已配置的 Bearer 密钥会随请求发送 | 服务端参考录音路径、参考原文、语言 | 完整 WAV |

这是对上述协议的适配。仅提供普通聊天 API、只有网页、采用其他专用鉴权或不同请求格式的服务，不能直接当成 OpenAI 兼容语音接口。具体模型、声音、权限、价格及服务可用性由对应服务商决定，软件不会自动开通服务或创建克隆音色。

## OpenAI 兼容接口

后端发送 `POST`，JSON 使用 `input`、`model`、`voice`、`response_format` 和 `speed`。可选 `instructions` 用于支持它的模型；自定义 OpenAI 音色可显式选择 `{ "id": "voice_..." }` 形式。

- 填 `https://example.com` 时，使用 `https://example.com/v1/audio/speech`。
- 填 `https://example.com/prefix/v1` 时，使用 `https://example.com/prefix/v1/audio/speech`。
- 填完整 `https://example.com/custom/synthesize` 时，原样使用该路径。
- 本机兼容服务可填 `http://127.0.0.1:8880/v1`，以该服务自己的模型和声音名称调用。

这使实现相同语音协议的云服务、网关和本地项目可以直接接入，无需每个服务商写一份硬编码。兼容协议不代表所有模型可互换；模型名称和音色 ID 要以所连接服务的说明为准。当前适配完整 MP3 和 WAV，不把 SSE、Opus、AAC、裸 PCM 或 JSON 下载链接当作音频文件。

OpenAI 官方在本次开发核对时仍提供 `/audio/speech`；官方文档同时提示部分旧 Speech 模型有停用计划。软件允许用户填写模型名称，因此需按官方通知维护模型。它未实现 OpenAI Realtime WebSocket 协议，不会把 Speech 请求自动切换为 Realtime。

## Fish Audio

保留 `s2.1-pro-free` 与 `drama-3` 的明确选择。每次都发送 model Header，无效模型会在本地拒绝，防止服务端将错误或遗漏模型回落为付费模型。免费模型对 429 / 5xx 最多三次请求且遵守 Retry-After；Drama 3 不自动重试。同一请求不会因错误改用其他模型或其他 API。延迟参数为 normal / balanced / low，默认 balanced。本次新工厂使用完整 MP3，已有 Fish 流式实现仍保留在原类中。

## Azure

密钥必须属于 Speech 资源，不是任意 Azure 或聊天模型密钥。音色名例如 `zh-CN-XiaoxiaoNeural`，可用性以资源实际返回为准。请求正文以 XML 构建 SSML，聊天中的 `<`、`&` 等字符会正确转义，避免被当成指令。请填写完整合成地址，软件不会猜测资源所属区域。

## 豆包

使用新版控制台时，App ID 留空，填写 API Key 和资源 ID。使用旧版时，另填 App ID，密钥位置填写该应用的 Access Token。资源 ID 要与音色和计费项目匹配，例如 `seed-tts-2.0` 与 `seed-icl-2.0` 的音色权限不同；具体可用资源与声音仍以控制台为准。

此适配使用 HTTP Chunked v3，不是 WebSocket，也不是 `/sse`。网络返回的是多段 JSON，其中音频在 base64 `data` 中。适配器会组装音频，并要求 `20000000` 完成码；缺少完成码、无效 JSON、错误状态或无效 base64 都会丢弃本条内容。不会将一个网络字节块误认为完整 MP3。

## GPT-SoVITS

需要用户自行安装并启动官方 API v2 服务，软件不安装、下载权重或启动它。填写的参考录音路径必须能被该模型服务读取：本机服务用本机路径，远程服务用远程服务器上的路径。语音桥只发送路径字符串和录音原文，不读取或上传录音文件。

常用语言值为 `zh`、`en`、`ja`、`ko` 等，以服务版本为准。为便于共用音色表单，后端会将常见 `zh-CN` / `en-US` / `ja-JP` 等转换为对应短代码；其他值原样传入。使用 `media_type=wav`、`streaming_mode=false`，不会触发模型切换、权重加载、服务重启或其他管理接口。

## 失败与隐私

- 远程地址必须使用 HTTPS；只有 loopback 本机地址允许 HTTP。拒绝地址中的账户密码、查询参数和 fragment。
- 每条请求单独设置认证头，不使用共享默认认证头，不跟随重定向，避免把密钥带往其他主机。
- API 密钥由桌面层 DPAPI 安全存储，普通 profile 只保存凭据引用。日志和界面错误不复述密钥或服务端原始错误正文。
- 401 / 403 会暂停相同配置的后续请求，直到更换密钥、音色或连接，或用户修改后重新测试。UI 保存/重试应调用 `ResetFailures`。
- 429 按服务端 Retry-After 暂停该配置，不阻断其他 API。除明确限定的 Fish 免费模型重试外，其他适配不自动重试付费请求。
- 402 会提示余额或额度问题，不充值、不替换接口、不升级到其他付费模型。
- 请求超时可配置为 5–300 秒，默认 45 秒。「结束」传入的 CancellationToken 会取消网络请求与响应读取。
- 音频限额 16 MiB；豆包封装响应限额 24 MiB。错误响应、未知格式或不完整 WAV 会被拒绝。WAV 按 RIFF chunk 读取，不假设文件头固定 44 字节。
- 将目标消息交给哪个 API，由联系人选定的音色决定。云服务会处理该消息和已在该服务建立的授权声音；本地模型会在本地端点处理。配置远程端点时应确认它的服务协议和数据处理条款。

## 实现与验证

公共配置位于 `SpeechProfiles.cs`，网络适配位于 `SpeechProviderFactory.cs`。工厂可长期复用，所有生成器共享禁用重定向的 HttpClient；窗口停止处理后释放工厂即可。每条消息开始时固定 provider 和 voice 快照，凭据回调在请求开始时读取。


官方参考（2026-11-07 核对）：

- [Fish TTS v1](https://docs.fish.audio/api-reference/endpoint/openapi-v1/text-to-speech)
- [OpenAI Speech API](https://developers.openai.com/api/reference/resources/audio/subresources/speech/methods/create)
- [Azure Speech REST](https://learn.microsoft.com/en-us/azure/ai-services/speech-service/rest-text-to-speech)
- [豆包 HTTP Chunked / SSE v3](https://docs.volcengine.com/docs/doubao-voice/http-chunked-sse-unidirectional-streaming-v3?lang=zh)
- [GPT-SoVITS 官方 API v2 源码](https://github.com/RVC-Boss/GPT-SoVITS/blob/main/api_v2.py)
