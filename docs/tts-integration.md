# 多厂商语音 API 接入：调研结论与设计（2026-11-11）

> 本文是「任务一：把多厂商语音 API 融合进来」的准绳。调研结论、取舍理由、数据模型、分节实施计划都在这里；逐节进度见 [WORKLOG.md](WORKLOG.md)，当前状态见 [HANDOVER-CURRENT.md](HANDOVER-CURRENT.md)。
> 动工前的存档点：标签 `v1.5.3`（发布源码 `67a74f0`）与 `pre-multi-provider`（`d608aca`），均已推送到 GitHub。

## 1. 用户要求（整理后）

1. **通用协议**：小众、没单独适配的厂商，用通用协议接入；这种情况下服务名和音色名都由用户自己命名。
2. **主流厂商专属入口**：最常见的厂商一一对应，字段、默认地址、模型、音色示例都预先填好。
3. **本地模型**：支持用户在本机跑的模型服务，尤其要处理好「参考音频」这类音色。
4. **保留现有机制**（用户明确喜欢）：可以同时用多个厂商；每个厂商同一时间只启用一个 Key；同一 Key 能切换模型的就允许切换，能热切换就热切换；音色挂在厂商上并按厂商分类；添加 Key 只添加不自动应用；厂商的 Key 全删光音色才失效。
5. **修正过时或错误的选项**。
6. **教程页**写清每家怎么接。
7. **优先复用 GitHub 上成熟、高星、活跃维护的轮子**，少造轮子。

## 2. 调研了什么

| 类别 | 对象 | 用途 |
| --- | --- | --- |
| 多厂商 TTS 的成熟项目 | Dify 官方模型插件、LobeHub、Open WebUI、LibreChat、SillyTavern、Open-LLM-VTuber、AstrBot、pyvideotrans、LiteLLM、pipecat | 学它们怎么组织「服务 / 密钥 / 模型 / 音色」 |
| 可复用的轮子 | LiteLLM、edge-tts、RealtimeTTS、tetos、各厂商官方 SDK、Microsoft.Extensions.AI、Semantic Kernel、OpenAI .NET SDK | 判断哪些值得直接依赖 |
| 用户自建网关 | LiteLLM proxy、LocalAI、Kokoro-FastAPI、openai-edge-tts、vLLM-Omni、各类 OpenAI 兼容封装 | 判断「通用协议」能覆盖多少后端 |
| 厂商官方接口 | OpenAI、ElevenLabs、Azure、Gemini、Fish、MiniMax（国内/国际）、火山豆包 V3、阿里百炼（CosyVoice / 千问 TTS）、硅基流动、阶跃星辰、智谱、腾讯云、讯飞、百度、Cartesia、Deepgram、Inworld、Amazon Polly | 逐字段核对，作为适配器和教程依据 |
| 本地引擎 | GPT-SoVITS api_v2、CosyVoice、fish-speech、IndexTTS、Kokoro-FastAPI、LocalAI | 判断本地接入方式与音色形态 |

## 3. 关键调研结论

### 3.1 没有一个「万能轮子」能直接装进来

- **LiteLLM**（约 6.3 万星，MIT）是最成熟的统一网关，TTS 只覆盖 OpenAI、Azure、Polly、Vertex/Gemini、ElevenLabs、MiniMax、Mistral，国内厂商只有 MiniMax；Windows 轮子约 42 MB 且依赖 boto3、tokenizers 等，11 月 10–11 日几小时内连发 4 个版本。打进 90 MB 的安装包代价太大、变动太快。[文档](https://docs.litellm.ai/docs/text_to_speech) · [PyPI](https://pypi.org/project/litellm/)
- **pipecat、RealtimeTTS** 是实时语音框架，服务是流水线组件，一次性合成用不顺手。
- **AstrBot（AGPL）、pyvideotrans（GPL）** 国内厂商覆盖最好，但许可证不允许把代码抄进本项目，只能参考字段设计。
- **tetos**（Apache-2.0）覆盖火山、百度、MiniMax、讯飞，但只有约 270 星、单人维护，达不到「高星活跃」的门槛。
- **官方 SDK** 各管一家：`dashscope` 2.x 拖上 fastapi/uvicorn/opentelemetry；Azure Speech NuGet 55 MB；腾讯云 Python SDK 很小（Apache）。
- **.NET 的 `ITextToSpeechClient`**（Microsoft.Extensions.AI）仍是实验接口，实现只有 OpenAI 和一个 14 星的 ElevenLabs 包。

### 3.2 真正值得复用的三样东西

1. **OpenAI 兼容的 `/v1/audio/speech` 协议**——这是被复用最广的「轮子」。原生说这个协议的云厂商有 OpenAI、硅基流动、阶跃星辰、智谱 GLM-TTS；本地/自建的 LiteLLM proxy（6.3 万星）、LocalAI（5 万星）、Kokoro-FastAPI（5.7k 星）、vLLM-Omni（IndexTTS 2.5）、Chatterbox 封装等都暴露同一接口。项目已有这条适配器，把它做成「通用协议」入口 + 多个预设，就等于复用了这些成熟服务端。[硅基流动](https://docs.siliconflow.com/api-reference/audio/create-speech.md) · [阶跃星辰](https://platform.stepfun.ai/docs/en/api-reference/audio/create-audio) · [智谱](https://docs.bigmodel.cn/cn/guide/models/sound-and-video/glm-tts) · [LocalAI](https://github.com/mudler/LocalAI) · [Kokoro-FastAPI](https://github.com/remsky/Kokoro-FastAPI)
2. **edge-tts**（约 1.25 万星，LGPLv3，纯 Python 40 KB，2026-10-13 仍在发版）——免费、无需 Key、中文音色多。它的难点是微软会改校验令牌（2026 年 10 月有一波 403，维护者很快修了），这正是「别自己造」的典型：交给维护者跟进，我们只升级版本。按 LGPL 以未修改的独立包形式随附，附许可证文本，可被用户替换。[仓库](https://github.com/rany2/edge-tts) · [PyPI](https://pypi.org/project/edge-tts/)
3. **Dify 官方模型插件的「厂商描述」结构**——每个厂商一个声明：凭据字段（名称/类型/必填/默认值/帮助链接）、模型列表、**每个模型各自的音色列表**。这是本次「厂商目录」的蓝本（只借鉴结构，不复制代码）。[openai.yaml](https://github.com/langgenius/dify-official-plugins/blob/main/models/openai/provider/openai.yaml) · [gpt-4o-mini-tts 音色](https://github.com/langgenius/dify-official-plugins/blob/main/models/openai/models/tts/gpt-4o-mini-tts-2025-12-15.yaml)

### 3.3 成熟项目在「服务 / 模型 / 音色」上的共识

- **厂商身份 ≠ 协议**。LobeHub 自定义服务要填「服务 ID（不可改）+ 请求格式」；Dify 把 `openai` 与 `openai_api_compatible` 分成两个厂商，兼容服务每加一个都是独立条目；Open-LLM-VTuber 把 Kokoro 与硅基流动做成两个配置块，尽管都走 OpenAI 协议。
- **音色依附于模型**。Dify 把音色写在每个模型下面（`tts-1` 9 个、`gpt-4o-mini-tts` 13 个）；硅基流动的系统音色直接写成 `模型:音色名`；阿里百炼的复刻音色必须用创建时的模型合成；MiniMax 有专门错误码「该模型不支持此音色」。
- **参考音频是一种音色**。Open-LLM-VTuber、AstrBot 对 GPT-SoVITS 都用「参考音频路径 + 参考文本 + 参考语种」三件套。
- **反面教训**：SillyTavern 所有 OpenAI 兼容服务共用一个密钥槽；LibreChat 只允许一个 TTS 厂商；Open WebUI 拉音色列表失败时静默退回 6 个默认音色；音色/模型不兼容时直接隐藏而不说明。

### 3.4 本项目现状的问题

1. **厂商身份用的是协议类型 `Kind`**：`ProviderActivation`、`VoiceLibraryMigration`、删除/添加服务都按 `Kind` 分组。于是硅基流动和本机 Kokoro 都是 `OpenAiCompatible`，会被当成同一个厂商：只能启用其中一个 Key，音色还会互相串用。这是用户担心的「规则推不下去」的根源。
2. **模型只对 Fish 和 OpenAI 兼容开放切换**，其他厂商写死；Fish 只认 `s2.1-pro-free` 与 `drama-3` 两个。
3. **过时/错误选项**（逐项核对官方文档的结果）：
   - Fish：官方现有 `s2.1-pro`（推荐）、`s2.1-pro-free`、`s2-pro`、`drama-3`、`s1`（2026-12-31 退役）；漏了推荐的 `s2.1-pro` 与 `s2-pro`。不发 model 头时服务端默认走付费的 `s2.1-pro`——本项目一直显式发送，保持。[模型总览](https://docs.fish.audio/developer-guide/models-pricing/models-overview.md)
   - Fish 类型强制只能连官方地址，所以本机 fish-speech（同样是 `/v1/tts`）接不进来。
   - OpenAI：`tts-1`、`tts-1-hd`、`gpt-4o-mini-tts` 及其快照已于 2026-10-01 宣布弃用、**2027-01-06 停用**，官方替代只走 Realtime 接口。界面没有任何提示。[弃用公告](https://developers.openai.com/api/docs/deprecations)
   - OpenAI 音色与模型相关：`tts-1/hd` 只支持 9 个，`gpt-4o-mini-tts` 支持 13 个（含官方推荐的 marin、cedar）。[TTS 指南](https://developers.openai.com/api/docs/guides/text-to-speech)
   - 豆包 V3：资源 ID 决定模型代际与计费（`seed-tts-2.0` 只配 2.0 音色），现在只能手填、不能切换；`req_params.model` 只对声音复刻 2.0 有意义。[HTTP V3 文档](https://docs.volcengine.com/docs/6561/1598757)
   - 文案「豆包 / 火山语音」「GPT-SoVITS API v2」等对普通用户不直观；没有任何「去哪拿 Key」的指引。

## 4. 设计

### 4.1 一句话

**「厂商」从「协议类型」改成「服务身份」**：主流厂商的身份就是它在目录里的预设名（同一厂商的多个 Key 自动归到一起）；通用协议和本地模型每建一个就是一个独立服务（同一地址的多个 Key 归到一起）。现有的「一厂商一启用 Key、音色挂厂商、加 Key 不应用、Key 删光才失效」规则原封不动，只是换成按服务身份判断。

### 4.2 四层关系

```text
服务（Vendor，厂商/自建服务）
 ├─ Key（SpeechProviderProfile，可多个，同一时间只启用一个）
 ├─ 模型（目录给默认列表，可增删；联系人卡片可热切换，下一条生效）
 └─ 音色（VoiceProfile，挂在服务上，任一启用的 Key 都能合成）
      └─ 可选「固定模型」：只能用某个模型合成的音色（复刻音色、豆包 2.0 音色等）
```

- **服务身份 `Vendor`**（新字段，字符串）：
  - 主流预设：等于预设 Id，例如 `siliconflow`、`minimax-cn`。
  - 通用协议 / 本地模型：新建时生成 `svc-<guid>`；新加的 Key 若与已有同预设服务的接口地址相同，自动归入那个服务。
  - 老配置迁移：Fish→`fish`、Azure→`azure`、豆包→`volcengine`、OpenAI 兼容按地址（官方地址→`openai`，其他地址各成一个服务）、GPT-SoVITS 按地址。
- **预设 `Preset`**（新字段）：记录这条服务来自目录里的哪一项，决定界面字段、默认值、帮助链接。协议仍由原来的 `Kind` 表示。
- **模型优先级**：音色固定的模型 > 联系人卡片上选的模型 > 服务默认模型。音色固定了模型时，联系人卡片不再显示模型切换，避免出现「选了却不生效」。
- **音色有效性**：仍然只有「该服务一个 Key 都没有」才失效。模型与音色不兼容时（目录里已知的组合）在编辑音色时提示，但不隐藏、不偷偷改。

### 4.3 厂商目录（ProviderCatalog）

参照 Dify 的厂商描述，每个预设声明：

| 字段 | 含义 |
| --- | --- |
| `Id` / `Label` / `Category` | 预设名、显示名、分组（主流厂商 / 通用协议 / 本地模型） |
| `Kind` | 走哪条协议适配器 |
| `DefaultEndpoint` / 地址是否可改 / 地址选项（地域） | 例如 MiniMax 国内默认与华北就近地址 |
| Key 是否必填、Key 叫什么 | 例如「API Key」「Access Token」 |
| 额外字段 | 豆包旧版 App ID、百炼业务空间等 |
| 模型列表（含说明：免费/付费/即将停用） | 默认勾选与默认模型 |
| 音色形态 | 音色 ID / 参考音频 |
| 音色示例与按模型的音色清单 | 填音色时可直接选，也可手填 |
| 获取 Key 的链接、音色列表链接、说明 | 界面与教程共用 |

### 4.4 协议适配器（每个协议一条，预设复用协议）

| 协议 | 覆盖的预设 | 状态 |
| --- | --- | --- |
| Fish `/v1/tts` | Fish Audio（云）、fish-speech（本机，允许 loopback 地址、Key 可选） | 现有，扩模型、放开本机 |
| OpenAI 兼容 `/v1/audio/speech` | OpenAI、硅基流动、阶跃星辰、智谱、Kokoro-FastAPI、LocalAI、LiteLLM 网关、其他兼容服务（通用协议） | 现有，加预设 |
| Azure Speech REST | Azure | 现有，加音色示例 |
| 豆包 HTTP V3 | 火山引擎豆包语音 | 现有，资源 ID 变成可切换的「模型」 |
| GPT-SoVITS api_v2 | GPT-SoVITS（本机） | 现有 |
| ElevenLabs | ElevenLabs | 新增 |
| MiniMax T2A v2 | MiniMax 国内 / 国际 | 新增（JSON 返回十六进制音频） |
| 阿里百炼 | CosyVoice（HTTP 非实时）、千问 TTS | 新增（返回 24 小时有效的音频地址，再下载） |
| Gemini | Google Gemini TTS | 新增（返回 base64 WAV/PCM） |
| Edge 朗读（edge-tts） | 微软 Edge 免费音色 | 新增，复用 edge-tts 库，在内置 Python 里单次运行 |
| 自定义 HTTP 模板 | 任何小众厂商 | 新增（地址、请求头、请求体模板、响应取值方式） |

**本轮不做（写进教程，说明替代办法）**：腾讯云（TC3 签名）、讯飞（WebSocket + 签名）、百度（旧接口已停止维护）、Google Cloud TTS（OAuth）、Amazon Polly（SigV4）、小米 MiMo（聊天接口形态）、Cartesia/Deepgram/Inworld（无中文或中文弱）、CosyVoice 官方 FastAPI（表单上传 + 裸 PCM，建议走 OpenAI 兼容封装）。Hume TTS 将于 2026-11-13 关停，不接。

### 4.5 安全约束（沿用并扩展）

- 远程地址必须 HTTPS，只有本机 loopback 可用 HTTP；不跟随重定向；每个请求单独带认证头。
- 百炼返回的音频地址只允许 `*.aliyuncs.com`，下载时不带任何认证头，http 自动升级为 https。
- 自定义 HTTP 模板：Key 只能出现在请求头里（`{{key}}`），不能出现在地址里；请求体里的占位符按 JSON 字符串转义。
- edge-tts 子进程用内置 Python 的 `-I` 隔离模式，超时即结束；不读写微信数据。
- 出厂包门禁（`Assert-CleanPayload.ps1`）与静态断言照旧。

## 5. 分节实施计划

每节都按 AGENTS.md：记 WORKLOG → 更新 HANDOVER → 单独提交 → 推送。

1. 本文（调研与设计）。
2. 数据模型：`Vendor` / `Preset` / 音色固定模型 + 迁移 + 按服务身份的启用与音色规则 + 单元测试。
3. 厂商目录 `ProviderCatalog` + 一致性测试。
4. 协议适配器：ElevenLabs、MiniMax、百炼、Gemini、自定义 HTTP，Fish 扩模型与本机，豆包资源可切换。
5. 界面：添加服务改为分组目录、字段按预设生成、模型多选与热切换推广到所有厂商、音色编辑按形态生成并给出示例。
6. Edge 朗读：随包附带 edge-tts 及依赖（锁定哈希）、子进程调用、实测一次合成。
7. 教程页与 `docs/providers.md`：逐家写清在哪拿 Key、填什么、音色去哪找、费用与注意事项。
8. 全量回归、截图、交接文档、推送。不打包、不上线，等用户检查。
