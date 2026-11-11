using System;
using System.Collections.Generic;
using System.Linq;

namespace VoiceBridge.Core;

public enum PresetCategory { Mainstream, Generic, Local }

/// <summary>What a voice of this service is made of.</summary>
public enum VoiceForm
{
    /// <summary>A voice id the vendor knows (built-in, cloned or designed).</summary>
    VoiceId,
    /// <summary>A reference recording plus its transcript, for local zero-shot models.</summary>
    Reference
}

public enum KeyNeed { Required, Optional, None }

/// <summary>A model the service offers. <see cref="Checked"/> models are offered on the home page by default.</summary>
public sealed record CatalogModel(string Id, string Label, string Note = "", bool Checked = true);

/// <summary>
/// A known voice. <see cref="Models"/> lists the models it works with; null means
/// any model of the service.
/// </summary>
public sealed record CatalogVoice(string Id, string Label, string[]? Models = null)
{
    public bool WorksWith(string model) =>
        Models is null || string.IsNullOrWhiteSpace(model) || Models.Contains(model, StringComparer.Ordinal);
}

public sealed record CatalogEndpoint(string Url, string Label);

/// <summary>
/// One entry of the "add a service" dialog: which protocol it speaks, what the
/// user has to fill in, which models and voices it offers, and where to get a key.
/// Modelled on the provider descriptors of mature multi-vendor apps (Dify's model
/// plugins): fields, defaults, per-model voices and help links in one place.
/// </summary>
public sealed record ProviderPreset
{
    public required string Id { get; init; }
    public required string Label { get; init; }
    public required PresetCategory Category { get; init; }
    public required SpeechProviderKind Kind { get; init; }
    /// <summary>One line shown under the name in the picker.</summary>
    public string Summary { get; init; } = "";
    /// <summary>The first entry is the default. Empty means the user types the address.</summary>
    public CatalogEndpoint[] Endpoints { get; init; } = [];
    /// <summary>Whether the user may type an address that is not in <see cref="Endpoints"/>.</summary>
    public bool CustomEndpoint { get; init; }
    public string EndpointHint { get; init; } = "";
    public KeyNeed Key { get; init; } = KeyNeed.Required;
    public string KeyLabel { get; init; } = "API Key";
    public string KeyUrl { get; init; } = "";
    public string DocsUrl { get; init; } = "";
    public string VoicesUrl { get; init; } = "";
    public CatalogModel[] Models { get; init; } = [];
    /// <summary>Whether models outside <see cref="Models"/> may be typed in.</summary>
    public bool CustomModels { get; init; }
    public string ModelLabel { get; init; } = "模型";
    public VoiceForm Voice { get; init; } = VoiceForm.VoiceId;
    public string VoiceLabel { get; init; } = "音色 ID";
    public string VoiceHint { get; init; } = "";
    public CatalogVoice[] Voices { get; init; } = [];
    /// <summary>Things the user must know: free or paid, retirements, regions.</summary>
    public string[] Notes { get; init; } = [];
    public string AudioFormat { get; init; } = "mp3";
    /// <summary>Generic and local services carry the user's own name; mainstream ones may keep the vendor name.</summary>
    public bool NameRequired => Category != PresetCategory.Mainstream;
    /// <summary>
    /// From mainland China the vendor's site, sign-up or API is not reachable without
    /// an overseas network. Shown as a badge so nobody signs up only to hit a wall.
    /// </summary>
    public bool Overseas { get; init; }

    public string DefaultEndpoint => Endpoints.Length > 0 ? Endpoints[0].Url : "";
    public string DefaultModel => Models.Length > 0 ? Models[0].Id : "";

    /// <summary>
    /// The service identity a new key of this preset gets: a mainstream vendor is
    /// one service wherever it is called from; a generic or local service is
    /// identified by its own address (see <see cref="ServiceIdentity"/>).
    /// </summary>
    public string VendorFor(SpeechProviderProfile key) =>
        Category == PresetCategory.Mainstream ? Id : ServiceIdentity.Derive(key with { Vendor = "" });

    public string ModelLabelOf(string model) =>
        Models.FirstOrDefault(item => item.Id == model)?.Label ?? model;

    public IEnumerable<CatalogVoice> VoicesFor(string model) => Voices.Where(item => item.WorksWith(model));
}

/// <summary>Every service the app offers a dedicated entry for, grouped as the dialog shows them.</summary>
public static class ProviderCatalog
{
    public const string OpenAiCompatibleId = "openai-compatible";
    public const string CustomHttpId = "custom-http";

    private static CatalogVoice[] SiliconFlowVoices(params string[] models) =>
        models.SelectMany(model => new[]
        {
            ("alex", "沉稳男声"), ("benjamin", "低沉男声"), ("charles", "磁性男声"), ("david", "欢快男声"),
            ("anna", "沉稳女声"), ("bella", "激情女声"), ("claire", "温柔女声"), ("diana", "欢快女声")
        }.Select(voice => new CatalogVoice(model + ":" + voice.Item1,
            voice.Item2 + " · " + voice.Item1 + " · " + model.Split('/').Last(), [model]))).ToArray();

    public static IReadOnlyList<ProviderPreset> All { get; } = Array.AsReadOnly(new ProviderPreset[]
    {
        // ------------------------------------------------------------ mainstream
        new()
        {
            Id = "fish", Label = "Fish Audio", Category = PresetCategory.Mainstream, Kind = SpeechProviderKind.Fish,
            Summary = "声音克隆效果好，有免费模型", Overseas = true,
            Endpoints = [new(ServiceIdentity.FishOfficialEndpoint, "官方接口")],
            KeyUrl = "https://fish.audio/app/api-keys/",
            DocsUrl = "https://docs.fish.audio/api-reference/endpoint/openapi-v1/text-to-speech",
            VoicesUrl = "https://fish.audio/",
            Models =
            [
                new(SpeechModels.FishFree, "S2.1 Pro · 免费", "免费，公平使用限额，不保证延迟"),
                new("s2.1-pro", "S2.1 Pro · 付费", "官方推荐的付费模型", false),
                new("s2-pro", "S2 Pro · 付费", "上一代付费模型", false),
                new(SpeechModels.FishDrama, "Drama 3 · 付费", "表现力更强的付费模型")
            ],
            VoiceLabel = "Voice ID", VoiceHint = "在 Fish 音色页面复制 32 位 ID（reference_id）",
            Notes = ["每次请求都明确发送所选模型，免费模型失败时不会改用付费模型。", "s1 模型将于 2026-12-31 退役，因此不再提供。"]
        },
        new()
        {
            Id = "openai", Label = "OpenAI", Category = PresetCategory.Mainstream, Kind = SpeechProviderKind.OpenAiCompatible,
            Summary = "gpt-4o-mini-tts，可用文字指令控制语气", Overseas = true,
            Endpoints = [new("https://api.openai.com/v1/audio/speech", "官方接口")],
            KeyUrl = "https://platform.openai.com/settings/organization/api-keys",
            DocsUrl = "https://developers.openai.com/api/docs/guides/text-to-speech",
            VoicesUrl = "https://developers.openai.com/api/docs/guides/text-to-speech",
            Models =
            [
                new("gpt-4o-mini-tts", "gpt-4o-mini-tts", "支持语气指令"),
                new("gpt-4o-mini-tts-2025-12-15", "gpt-4o-mini-tts（2025-12-15）", "固定快照", false),
                new("tts-1", "tts-1", "旧模型，延迟低", false),
                new("tts-1-hd", "tts-1-hd", "旧模型，音质高", false)
            ],
            VoiceLabel = "音色",
            Voices =
            [
                new("marin", "marin（官方推荐）", ["gpt-4o-mini-tts", "gpt-4o-mini-tts-2025-12-15"]),
                new("cedar", "cedar（官方推荐）", ["gpt-4o-mini-tts", "gpt-4o-mini-tts-2025-12-15"]),
                new("alloy", "alloy"), new("ash", "ash"),
                new("ballad", "ballad", ["gpt-4o-mini-tts", "gpt-4o-mini-tts-2025-12-15"]),
                new("coral", "coral"), new("echo", "echo"), new("fable", "fable"), new("nova", "nova"),
                new("onyx", "onyx"), new("sage", "sage"), new("shimmer", "shimmer"),
                new("verse", "verse", ["gpt-4o-mini-tts", "gpt-4o-mini-tts-2025-12-15"])
            ],
            Notes = ["OpenAI 已宣布：这里的全部语音模型将于 2027-01-06 停用，官方替代只提供实时语音接口。到期前可正常使用。"]
        },
        new()
        {
            Id = "elevenlabs", Label = "ElevenLabs", Category = PresetCategory.Mainstream, Kind = SpeechProviderKind.ElevenLabs,
            Summary = "多语种表现力强，声音库丰富", Overseas = true,
            Endpoints =
            [
                new("https://api.elevenlabs.io", "全球"), new("https://api.us.elevenlabs.io", "美国"),
                new("https://api.eu.residency.elevenlabs.io", "欧盟数据驻留")
            ],
            KeyUrl = "https://elevenlabs.io/app/settings/api-keys",
            DocsUrl = "https://elevenlabs.io/docs/api-reference/text-to-speech/convert",
            VoicesUrl = "https://elevenlabs.io/app/voice-library",
            Models =
            [
                new("eleven_multilingual_v2", "Multilingual v2", "稳定，中文效果好"),
                new("eleven_v3", "Eleven v3", "表现力最强", false),
                new("eleven_flash_v2_5", "Flash v2.5", "低延迟、价格低", false),
                new("eleven_v4", "Eleven v4", "最新一代", false)
            ],
            CustomModels = true,
            VoiceLabel = "Voice ID", VoiceHint = "在声音库或「My Voices」里复制 Voice ID",
            Voices = [new("JBFqnCBsd6RMkjVDRZzb", "George（官方示例）")]
        },
        new()
        {
            Id = "azure", Label = "Azure 语音服务", Category = PresetCategory.Mainstream, Kind = SpeechProviderKind.AzureSpeech,
            Summary = "微软官方，中文音色多，每月有免费额度",
            Endpoints =
            [
                new("https://eastasia.tts.speech.microsoft.com/cognitiveservices/v1", "东亚（香港）"),
                new("https://southeastasia.tts.speech.microsoft.com/cognitiveservices/v1", "东南亚（新加坡）"),
                new("https://japaneast.tts.speech.microsoft.com/cognitiveservices/v1", "日本东部"),
                new("https://eastus.tts.speech.microsoft.com/cognitiveservices/v1", "美国东部"),
                new("https://westeurope.tts.speech.microsoft.com/cognitiveservices/v1", "西欧")
            ],
            CustomEndpoint = true, EndpointHint = "选择语音资源所在区域；也可直接填区域名，例如 eastasia",
            KeyLabel = "语音资源密钥",
            KeyUrl = "https://portal.azure.com/#create/Microsoft.CognitiveServicesSpeechServices",
            DocsUrl = "https://learn.microsoft.com/azure/ai-services/speech-service/rest-text-to-speech",
            VoicesUrl = "https://learn.microsoft.com/azure/ai-services/speech-service/language-support?tabs=tts",
            VoiceLabel = "音色名称",
            Voices =
            [
                new("zh-CN-XiaoxiaoNeural", "晓晓（女）"), new("zh-CN-YunxiNeural", "云希（男）"),
                new("zh-CN-XiaoyiNeural", "晓伊（女）"), new("zh-CN-YunjianNeural", "云健（男）"),
                new("zh-CN-YunyangNeural", "云扬（男，新闻）"), new("zh-CN-XiaochenNeural", "晓辰（女）")
            ],
            Notes = ["密钥必须来自「语音服务」资源，并与所选区域一致。免费 F0 层每月含 50 万字符神经网络语音。"]
        },
        new()
        {
            Id = "gemini", Label = "Google Gemini", Category = PresetCategory.Mainstream, Kind = SpeechProviderKind.Gemini,
            Summary = "可用文字描述语气，有免费层", Overseas = true,
            Endpoints = [new("https://generativelanguage.googleapis.com", "官方接口")],
            KeyUrl = "https://aistudio.google.com/apikey",
            DocsUrl = "https://aistudio.google.com/docs/speech-generation",
            VoicesUrl = "https://aistudio.google.com/docs/speech-generation",
            Models =
            [
                new("gemini-3.8-flash-tts", "Gemini 3.8 Flash TTS"),
                new("gemini-3.8-flash-lite-tts", "Gemini 3.8 Flash-Lite TTS", "更便宜", false)
            ],
            CustomModels = true, VoiceLabel = "音色",
            Voices =
            [
                new("Kore", "Kore"), new("Puck", "Puck"), new("Zephyr", "Zephyr"), new("Charon", "Charon"),
                new("Fenrir", "Fenrir"), new("Leda", "Leda"), new("Orus", "Orus"), new("Aoede", "Aoede"), new("Sulafat", "Sulafat")
            ],
            AudioFormat = "wav",
            Notes = ["gemini-2.5 的预览版语音模型已于 2026-11-17 停用，请用 3.8 系列。"]
        },
        new()
        {
            Id = "volcengine", Label = "火山引擎 · 豆包语音", Category = PresetCategory.Mainstream, Kind = SpeechProviderKind.Doubao,
            Summary = "字节跳动豆包大模型语音，中文自然",
            Endpoints = [new("https://openspeech.bytedance.com/api/v3/tts/unidirectional", "HTTP 接口（V3）")],
            KeyLabel = "API Key（新版控制台）",
            KeyUrl = "https://console.volcengine.com/",
            DocsUrl = "https://www.volcengine.com/docs/6561/1598757",
            VoicesUrl = "https://www.volcengine.com/docs/6561/1598757",
            ModelLabel = "资源 ID",
            Models =
            [
                new("seed-tts-2.0", "豆包语音 2.0", "只能用 2.0 音色"),
                new("seed-tts-1.0", "豆包语音 1.0", "只能用 1.0 音色", false),
                new("seed-icl-2.0", "声音复刻 2.0", "复刻音色（S_ 开头）", false),
                new("seed-icl-1.0", "声音复刻 1.0", "复刻音色", false)
            ],
            CustomModels = true, VoiceLabel = "音色 ID（speaker）",
            VoiceHint = "音色和资源 ID 必须是同一代：2.0 音色配 seed-tts-2.0，复刻音色配 seed-icl。",
            Voices =
            [
                new("zh_female_vv_uranus_bigtts", "vv（示例）"),
                new("zh_female_shuangkuaisisi_moon_bigtts", "爽快思思（示例）")
            ],
            Notes = ["旧版控制台用户在「高级」里填 App ID，密钥位置填 Access Token。"]
        },
        new()
        {
            Id = "aliyun", Label = "阿里云百炼", Category = PresetCategory.Mainstream, Kind = SpeechProviderKind.DashScope,
            Summary = "CosyVoice 与千问语音合成，同一个 Key",
            Endpoints = [new("https://dashscope.aliyuncs.com", "华北 2（北京）")],
            CustomEndpoint = true,
            EndpointHint = "CosyVoice 若提示地址不对，换成业务空间域名 https://<业务空间ID>.cn-beijing.maas.aliyuncs.com",
            KeyUrl = "https://help.aliyun.com/zh/model-studio/get-api-key",
            DocsUrl = "https://help.aliyun.com/zh/model-studio/cosyvoice-tts-http-api",
            VoicesUrl = "https://help.aliyun.com/zh/model-studio/cosyvoice-tts-http-api",
            Models =
            [
                new("cosyvoice-v3-flash", "CosyVoice v3 Flash"),
                new("cosyvoice-v3-plus", "CosyVoice v3 Plus", "", false),
                new("cosyvoice-v3.5-flash", "CosyVoice v3.5 Flash", "", false),
                new("cosyvoice-v3.5-plus", "CosyVoice v3.5 Plus", "", false),
                new("cosyvoice-v2", "CosyVoice v2", "", false),
                new("qwen3-tts-flash", "千问 3 TTS Flash", "每次最多 600 字", false)
            ],
            CustomModels = true, VoiceLabel = "音色",
            VoiceHint = "复刻或设计出来的音色只能用创建它的那个模型合成，请在音色里固定该模型。",
            Voices =
            [
                new("longanyang", "龙安洋（CosyVoice）", ["cosyvoice-v3-flash", "cosyvoice-v3-plus"]),
                new("Cherry", "Cherry（千问）", ["qwen3-tts-flash"]),
                new("Ethan", "Ethan（千问）", ["qwen3-tts-flash"])
            ],
            Notes = ["新加坡地域与北京地域的 API Key 不通用。"]
        },
        new()
        {
            Id = "minimax-cn", Label = "MiniMax（国内）", Category = PresetCategory.Mainstream, Kind = SpeechProviderKind.MiniMax,
            Summary = "海螺语音，情绪丰富",
            Endpoints =
            [
                new("https://api.minimax.cn/v1/t2a_v2", "默认地址"),
                new("https://api-bj.minimaxi.com/v1/t2a_v2", "华北就近（首包更快）")
            ],
            KeyUrl = "https://platform.minimax.cn/user-center/basic-information/interface-key",
            DocsUrl = "https://platform.minimax.cn/docs/api-reference/speech-t2a-http",
            VoicesUrl = "https://platform.minimax.cn/docs/api-reference/speech-t2a-http",
            Models =
            [
                new("speech-2.8-hd", "speech-2.8-hd", "音质优先"),
                new("speech-2.8-turbo", "speech-2.8-turbo", "速度优先", false),
                new("speech-2.6-hd", "speech-2.6-hd", "", false),
                new("speech-2.6-turbo", "speech-2.6-turbo", "", false),
                new("speech-02-hd", "speech-02-hd", "", false),
                new("speech-02-turbo", "speech-02-turbo", "", false)
            ],
            CustomModels = true, VoiceLabel = "音色 ID（voice_id）",
            Voices =
            [
                new("male-qn-qingse", "青涩青年"), new("male-qn-jingying", "精英青年"),
                new("female-shaonv", "少女"), new("female-yujie", "御姐"), new("female-tianmei", "甜美女声")
            ]
        },
        new()
        {
            Id = "minimax-global", Label = "MiniMax（国际）", Category = PresetCategory.Mainstream, Kind = SpeechProviderKind.MiniMax,
            Summary = "MiniMax 海外站，Key 与国内站不通用",
            Endpoints =
            [
                new("https://api.minimax.io/v1/t2a_v2", "默认地址"),
                new("https://api-uw.minimax.io/v1/t2a_v2", "美国西部")
            ],
            KeyUrl = "https://platform.minimax.io/user-center/basic-information/interface-key",
            DocsUrl = "https://platform.minimax.io/docs/api-reference/speech-t2a-http",
            VoicesUrl = "https://platform.minimax.io/docs/api-reference/speech-t2a-http",
            Models =
            [
                new("speech-2.8-hd", "speech-2.8-hd", "音质优先"),
                new("speech-2.8-turbo", "speech-2.8-turbo", "速度优先", false),
                new("speech-2.6-hd", "speech-2.6-hd", "", false),
                new("speech-02-hd", "speech-02-hd", "", false)
            ],
            CustomModels = true, VoiceLabel = "音色 ID（voice_id）",
            Voices = [new("English_Graceful_Lady", "Graceful Lady"), new("male-qn-qingse", "青涩青年")]
        },
        new()
        {
            Id = "siliconflow", Label = "硅基流动 SiliconFlow", Category = PresetCategory.Mainstream, Kind = SpeechProviderKind.OpenAiCompatible,
            Summary = "CosyVoice2 / Fish Speech 开源模型托管，价格低",
            Endpoints =
            [
                new("https://api.siliconflow.cn/v1/audio/speech", "国内站"),
                new("https://api.siliconflow.com/v1/audio/speech", "国际站")
            ],
            KeyUrl = "https://cloud.siliconflow.cn/account/ak",
            DocsUrl = "https://docs.siliconflow.com/en/userguide/capabilities/text-to-speech",
            VoicesUrl = "https://docs.siliconflow.com/en/userguide/capabilities/text-to-speech",
            Models =
            [
                new("FunAudioLLM/CosyVoice2-0.5B", "CosyVoice2 0.5B"),
                new("fishaudio/fish-speech-1.5", "Fish Speech 1.5", "", false)
            ],
            CustomModels = true, VoiceLabel = "音色",
            VoiceHint = "系统音色写成「模型:音色名」；自己上传的音色是 speech: 开头的地址，只能配上传时选的模型。",
            Voices = SiliconFlowVoices("FunAudioLLM/CosyVoice2-0.5B", "fishaudio/fish-speech-1.5"),
            Notes = ["使用自己上传的音色需要先在硅基流动完成实名认证。"]
        },
        new()
        {
            Id = "stepfun", Label = "阶跃星辰", Category = PresetCategory.Mainstream, Kind = SpeechProviderKind.OpenAiCompatible,
            Summary = "Step 语音合成，支持括号里的语气指令",
            Endpoints = [new("https://api.stepfun.com/v1/audio/speech", "官方接口")],
            KeyUrl = "https://platform.stepfun.com/interface-key",
            DocsUrl = "https://platform.stepfun.com/docs/api-reference/audio/create-audio",
            VoicesUrl = "https://platform.stepfun.com/docs/zh/guides/developer/tts.md",
            Models =
            [
                new("step-tts-mini", "step-tts-mini"),
                new("step-tts-2", "step-tts-2", "", false),
                new("stepaudio-2.5-tts", "StepAudio 2.5 TTS", "括号内文字当作语气指令、不朗读", false),
                new("stepaudio-3-tts", "StepAudio 3 TTS", "括号内文字当作语气指令、不朗读", false)
            ],
            CustomModels = true, VoiceLabel = "音色",
            Voices = [new("cixingnansheng", "磁性男声")]
        },
        new()
        {
            Id = "zhipu", Label = "智谱 GLM-TTS", Category = PresetCategory.Mainstream, Kind = SpeechProviderKind.OpenAiCompatible,
            Summary = "超拟人情感语音",
            Endpoints = [new("https://open.bigmodel.cn/api/paas/v4/audio/speech", "官方接口")],
            KeyUrl = "https://open.bigmodel.cn/usercenter/apikeys",
            DocsUrl = "https://docs.bigmodel.cn/cn/guide/models/sound-and-video/glm-tts",
            VoicesUrl = "https://docs.bigmodel.cn/cn/guide/models/sound-and-video/glm-tts",
            Models = [new("glm-tts", "GLM-TTS")],
            CustomModels = true, VoiceLabel = "音色",
            Voices =
            [
                new("tongtong", "彤彤（默认）"), new("xiaochen", "小陈"), new("chuichui", "锤锤"),
                new("jam", "jam"), new("kazi", "kazi"), new("douji", "douji"), new("luodo", "luodo")
            ],
            AudioFormat = "wav",
            Notes = ["智谱只返回 WAV 或 PCM，这里固定用 WAV。音色 ID 以官方页面为准。"]
        },
        new()
        {
            Id = "edge", Label = "微软 Edge 朗读（免费）", Category = PresetCategory.Mainstream, Kind = SpeechProviderKind.EdgeTts,
            Summary = "免费、无需 Key；非官方接口，可能随时失效",
            Key = KeyNeed.None,
            DocsUrl = "https://github.com/rany2/edge-tts", VoicesUrl = "https://github.com/rany2/edge-tts",
            VoiceLabel = "音色名称",
            Voices =
            [
                new("zh-CN-XiaoxiaoNeural", "晓晓（女）"), new("zh-CN-YunxiNeural", "云希（男）"),
                new("zh-CN-XiaoyiNeural", "晓伊（女）"), new("zh-CN-YunjianNeural", "云健（男）"),
                new("zh-CN-YunyangNeural", "云扬（男，新闻）"), new("zh-CN-liaoning-XiaobeiNeural", "晓北（东北话）"),
                new("zh-CN-shaanxi-XiaoniNeural", "晓妮（陕西话）"), new("zh-HK-HiuMaanNeural", "曉曼（粤语）"),
                new("zh-TW-HsiaoChenNeural", "曉臻（台湾）")
            ],
            Notes =
            [
                "通过开源库 edge-tts 调用微软 Edge 浏览器的「大声朗读」服务，不是微软正式开放的接口，可能限流或失效。",
                "朗读的文字会发送到微软的服务器。"
            ]
        },

        // ------------------------------------------------------------ generic protocols
        new()
        {
            Id = OpenAiCompatibleId, Label = "OpenAI 兼容接口", Category = PresetCategory.Generic, Kind = SpeechProviderKind.OpenAiCompatible,
            Summary = "任何提供 /v1/audio/speech 的服务或网关",
            CustomEndpoint = true,
            EndpointHint = "填完整地址、以 /v1 结尾的地址或只填主机，例如 https://example.com/v1",
            Key = KeyNeed.Optional, CustomModels = true, VoiceLabel = "音色",
            VoiceHint = "填写这项服务认识的 voice 值",
            Notes = ["LiteLLM 网关、LocalAI、vLLM-Omni 以及很多云服务都用这个协议。模型名和音色名以该服务说明为准。"]
        },
        new()
        {
            Id = CustomHttpId, Label = "自定义 HTTP 接口", Category = PresetCategory.Generic, Kind = SpeechProviderKind.CustomHttp,
            Summary = "按模板拼请求，适配没有专属入口的小众厂商",
            CustomEndpoint = true, EndpointHint = "接收 POST 请求的完整地址",
            Key = KeyNeed.Optional, CustomModels = true, VoiceLabel = "音色",
            Notes = ["出于安全考虑，{{key}} 只能写在请求头里，不能写进请求体或地址。"]
        },

        // ------------------------------------------------------------ local models
        new()
        {
            Id = "gpt-sovits", Label = "GPT-SoVITS", Category = PresetCategory.Local, Kind = SpeechProviderKind.GptSoVits,
            Summary = "本机声音克隆，用一段参考录音",
            Endpoints = [new(ServiceIdentity.GptSoVitsDefaultEndpoint, "本机默认端口 9880")],
            CustomEndpoint = true, EndpointHint = "先运行 GPT-SoVITS 的 api_v2.py",
            Key = KeyNeed.Optional,
            DocsUrl = "https://github.com/RVC-Boss/GPT-SoVITS", Voice = VoiceForm.Reference
        },
        new()
        {
            Id = "fish-speech", Label = "fish-speech / OpenAudio", Category = PresetCategory.Local, Kind = SpeechProviderKind.Fish,
            Summary = "Fish 开源模型的本机服务",
            Endpoints = [new("http://127.0.0.1:8080/v1/tts", "本机默认端口 8080")],
            CustomEndpoint = true, EndpointHint = "先运行 tools/api_server.py；启动时加了 --api-key 才需要填 Key",
            Key = KeyNeed.Optional,
            DocsUrl = "https://speech.fish.audio/inference/",
            VoiceLabel = "参考音色 ID（reference_id）",
            VoiceHint = "先用服务的 /v1/references/add 保存参考音频，再填它的 ID"
        },
        new()
        {
            Id = "kokoro", Label = "Kokoro-FastAPI", Category = PresetCategory.Local, Kind = SpeechProviderKind.OpenAiCompatible,
            Summary = "轻量本机模型，CPU 也能跑",
            Endpoints = [new("http://127.0.0.1:8880/v1/audio/speech", "本机默认端口 8880")],
            CustomEndpoint = true, Key = KeyNeed.None,
            DocsUrl = "https://github.com/remsky/Kokoro-FastAPI",
            Models = [new("kokoro", "kokoro")], CustomModels = true, VoiceLabel = "音色",
            Voices =
            [
                new("zf_xiaoxiao", "晓晓（女）"), new("zf_xiaobei", "晓北（女）"), new("zf_xiaoni", "晓妮（女）"),
                new("zf_xiaoyi", "晓伊（女）"), new("zm_yunxi", "云希（男）"), new("zm_yunjian", "云健（男）"),
                new("zm_yunxia", "云夏（男）"), new("zm_yunyang", "云扬（男）")
            ],
            Notes = ["Kokoro 的中文音色质量一般，很短的句子可能读得不自然。"]
        },
        new()
        {
            Id = "localai", Label = "LocalAI", Category = PresetCategory.Local, Kind = SpeechProviderKind.OpenAiCompatible,
            Summary = "本机多模型服务，OpenAI 兼容",
            Endpoints = [new("http://127.0.0.1:8080/v1/audio/speech", "本机默认端口 8080")],
            CustomEndpoint = true, Key = KeyNeed.Optional,
            DocsUrl = "https://localai.io/features/text-to-audio/",
            CustomModels = true, VoiceLabel = "音色",
            VoiceHint = "填 LocalAI 里该模型的音色名，或保存过的 localai://voice-profiles/… 地址"
        }
    });

    public static ProviderPreset? Find(string? id) =>
        string.IsNullOrWhiteSpace(id) ? null : All.FirstOrDefault(item => item.Id == id);

    /// <summary>
    /// The entry a saved key belongs to. Settings from before the catalog have no
    /// preset id, so the protocol and address decide.
    /// </summary>
    public static ProviderPreset For(SpeechProviderProfile provider)
    {
        if (Find(provider.Preset) is { } stored)
            return stored;
        var vendor = ServiceIdentity.Of(provider);
        if (Find(vendor) is { } byVendor && byVendor.Kind == provider.Kind)
            return byVendor;
        return provider.Kind switch
        {
            SpeechProviderKind.Fish => Find(vendor == ServiceIdentity.Fish ? "fish" : "fish-speech")!,
            SpeechProviderKind.AzureSpeech => Find("azure")!,
            SpeechProviderKind.Doubao => Find("volcengine")!,
            SpeechProviderKind.GptSoVits => Find("gpt-sovits")!,
            SpeechProviderKind.ElevenLabs => Find("elevenlabs")!,
            SpeechProviderKind.Gemini => Find("gemini")!,
            SpeechProviderKind.DashScope => Find("aliyun")!,
            SpeechProviderKind.EdgeTts => Find("edge")!,
            SpeechProviderKind.CustomHttp => Find(CustomHttpId)!,
            SpeechProviderKind.MiniMax => Find(ServiceIdentity.NormalizeEndpoint(provider.Endpoint).Contains("minimax.io")
                ? "minimax-global" : "minimax-cn")!,
            _ => Find(OpenAiCompatibleId)!
        };
    }

    /// <summary>Known Fish model ids; any other value must never reach the model header.</summary>
    public static IReadOnlyList<string> FishModelIds { get; } =
        Find("fish")!.Models.Select(item => item.Id).ToArray();
}
