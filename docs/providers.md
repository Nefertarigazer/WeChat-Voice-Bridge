# 语音服务与音色接入（开发者说明）

> 用户看的逐家接入教程在应用内「使用说明」第 03–07 节（`src/VoiceBridge.App/UserGuide.xaml.cs`）。调研过程、取舍与实施记录见 [tts-integration.md](tts-integration.md)。本文说明代码怎么组织、各协议怎么发请求，以及加一家新厂商要改哪里。核对日期：2026-11-11。

## 1. 概念

| 概念 | 代码 | 说明 |
| --- | --- | --- |
| 服务（厂商） | `ServiceIdentity.Of(profile)` | 主流厂商 = 目录预设 Id；通用协议 / 本地模型 = 「协议@规范化地址」。**判断是不是同一家一律用它，不要用 `Kind ==`。** |
| Key | `SpeechProviderProfile` | 一条服务连接：协议 `Kind`、预设 `Preset`、身份 `Vendor`、地址、模型列表、凭据引用 `CredentialId`、是否启用。同一服务同一时间只启用一个（`ProviderActivation`）。 |
| 模型 | `SpeechModels.Current/Use/Listed` | 服务勾选的模型。豆包的「模型」是资源 ID（`ResourceId`），其他协议是 `Model`。 |
| 音色 | `VoiceProfile` | 挂在服务上（`Vendor`），任一启用的 Key 都能合成。可选 `Model` = 固定模型。 |
| 预设 | `ProviderCatalog` / `ProviderPreset` | 添加服务对话框的条目：分组、协议、地址/地域、Key 需求、模型、常用音色（可限定模型）、注意事项、链接。 |

**模型优先级**：音色固定的模型 > 联系人卡片选的模型（只接受服务勾选过的）> 服务默认模型。实现在 `MainWindow.Library.cs` 的 `SpeechFor` 与 `ContactProfile.ApplySpeechPreferences`。

**音色失效**：只有这个服务一个 Key 都不剩时（`VoiceLibraryMigration.ActiveKeyFor` 返回 null）。

## 2. 协议适配（`src/VoiceBridge.FishAudio/SpeechProviderFactory.cs`）

| 协议 `Kind` | 预设 | 请求 | 返回 |
| --- | --- | --- | --- |
| `Fish` | fish、fish-speech（本机） | `POST /v1/tts`，Bearer；官方地址必发 `model` 头；JSON `text / reference_id / format=mp3 / latency` | MP3 |
| `OpenAiCompatible` | openai、siliconflow、stepfun、zhipu、openai-compatible、kokoro、localai | `POST …/audio/speech`，Bearer（本机可无）；`input / model / voice / response_format / speed / instructions` | MP3 或 WAV |
| `AzureSpeech` | azure | `Ocp-Apim-Subscription-Key`，SSML，`audio-24khz-96kbitrate-mono-mp3` | MP3 |
| `Doubao` | volcengine | HTTP V3 单向流式；`X-Api-Key`（旧版 `X-Api-App-Id + X-Api-Access-Key`）+ `X-Api-Resource-Id` | 分段 JSON，base64，结束码 20000000 |
| `GptSoVits` | gpt-sovits | `POST /tts`，`ref_audio_path / prompt_text / prompt_lang / text_lang / media_type=wav` | WAV |
| `ElevenLabs` | elevenlabs | `POST /v1/text-to-speech/{voice}?output_format=mp3_44100_128`，`xi-api-key`；`text / model_id / voice_settings.speed` | MP3 |
| `MiniMax` | minimax-cn、minimax-global | `POST /v1/t2a_v2`，Bearer；`model / text / stream=false / voice_setting / audio_setting / output_format=hex` | JSON：`data.audio` 十六进制；`base_resp.status_code` 判错 |
| `DashScope` | aliyun | CosyVoice：`/api/v1/services/audio/tts/SpeechSynthesizer`；千问：`/api/v1/services/aigc/multimodal-generation/generation`；Bearer | JSON：`output.audio.url`，再下载（仅 `*.aliyuncs.com`，不带认证头，http 升 https） |
| `Gemini` | gemini | `POST /v1beta/models/{model}:generateContent`，`x-goog-api-key`；`responseModalities=["AUDIO"]` + `prebuiltVoiceConfig.voiceName`，语气放 `speech_metadata.style` | JSON base64：WAV 直接用，裸 PCM 按 mimeType 采样率包成 WAV |
| `EdgeTts` | edge | 不走 HTTP：交给 `EdgeSpeechHost`（常驻 `worker/edge_speak.py` + edge-tts） | MP3 |
| `CustomHttp` | custom-http | 用户模板：请求头逐行、`{{key}}` 只在头里；请求体占位符 JSON 转义 | 直接音频，或 JSON 点路径 + base64 / hex / url |

共同约束：远程必须 HTTPS（只有 loopback 可 HTTP）；地址不许带用户信息、查询参数、`{{`；不跟随重定向；每个请求单独带认证头；401/403 暂停同一配置、429 按 Retry-After 暂停；只有 Fish 免费模型会自动重试；音频 ≤ 16 MiB、JSON 包装 ≤ 24 MiB；错误信息不回显服务端原文和密钥。

## 3. 加一家新厂商

1. **能走现有协议的**（大多数「OpenAI 兼容」厂商）：只在 `ProviderCatalog.All` 加一个预设——分组、`Kind`、地址、Key 页面、模型、常用音色（模型相关的写 `Models`）、注意事项。`ProviderCatalogTests` 会检查地址、链接、模型引用的一致性。
2. **需要新协议的**：在 `SpeechProviderKind` **末尾**追加一个值（设置文件存的是数字，不能重排）；在工厂里补 `Label`、`DefaultEndpoint`、`Validate`、`RequiresCredential`、`BuildRequest`、`ReadAudioAsync` 的分支；在 `VendorAdapterTests` 用模拟 HTTP 写请求形状与返回解码的测试；再按第 1 步加预设。
3. 在 `UserGuide.xaml.cs` 第 05 节（主流）或 06/07 节补一段用户说明：去哪拿 Key、填什么、音色在哪找、费用与注意事项。

## 4. 复用的外部项目

- **edge-tts 7.3.1**（LGPLv3，未修改，独立包）：`scripts/Publish.ps1` 锁定 14 个轮子的下载地址与 SHA-256，解到 `worker\tts-deps`，许可证随 `dist-info/licenses` 一起发布。升级时改版本号与哈希，并跑 `Test.ps1 -Network`（需要 `WVB_TTS_DEPS`）。
- **OpenAI 兼容协议**：让用户自建的 LiteLLM proxy、LocalAI、Kokoro-FastAPI、vLLM-Omni 等成熟服务端直接可用。
- **Dify 模型插件的厂商描述结构**：`ProviderPreset` 的字段设计参照它（只借鉴结构，没有复制代码）。

## 官方参考

- [Fish TTS](https://docs.fish.audio/api-reference/endpoint/openapi-v1/text-to-speech) · [Fish 模型](https://docs.fish.audio/developer-guide/models-pricing/models-overview.md)
- [OpenAI TTS](https://developers.openai.com/api/docs/guides/text-to-speech) · [OpenAI 弃用公告](https://developers.openai.com/api/docs/deprecations)
- [ElevenLabs TTS](https://elevenlabs.io/docs/api-reference/text-to-speech/convert)
- [Azure Speech REST](https://learn.microsoft.com/azure/ai-services/speech-service/rest-text-to-speech)
- [Gemini 语音生成](https://aistudio.google.com/docs/speech-generation)
- [豆包 HTTP V3](https://www.volcengine.com/docs/6561/1598757)
- [百炼 CosyVoice HTTP](https://help.aliyun.com/zh/model-studio/cosyvoice-tts-http-api) · [千问 TTS](https://help.aliyun.com/zh/model-studio/qwen-tts-api)
- [MiniMax 国内](https://platform.minimax.cn/docs/api-reference/speech-t2a-http) · [MiniMax 国际](https://platform.minimax.io/docs/api-reference/speech-t2a-http)
- [硅基流动](https://docs.siliconflow.com/en/userguide/capabilities/text-to-speech) · [阶跃星辰](https://platform.stepfun.com/docs/api-reference/audio/create-audio) · [智谱 GLM-TTS](https://docs.bigmodel.cn/cn/guide/models/sound-and-video/glm-tts)
- [GPT-SoVITS api_v2](https://github.com/RVC-Boss/GPT-SoVITS/blob/main/api_v2.py) · [fish-speech 本机服务](https://speech.fish.audio/inference/) · [Kokoro-FastAPI](https://github.com/remsky/Kokoro-FastAPI) · [LocalAI](https://localai.io/features/text-to-audio/) · [edge-tts](https://github.com/rany2/edge-tts)
