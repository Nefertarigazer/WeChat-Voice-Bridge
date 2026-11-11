namespace VoiceBridge.Core;

/// <summary>
/// The wire protocol a service speaks. New values are only ever appended:
/// settings store the number, so reordering would change saved services.
/// </summary>
public enum SpeechProviderKind
{
    Fish, OpenAiCompatible, AzureSpeech, Doubao, GptSoVits,
    ElevenLabs, MiniMax, DashScope, Gemini, EdgeTts, CustomHttp
}

/// <summary>Non-secret connection settings. CredentialId refers to the user's secure credential store.</summary>
public sealed record SpeechProviderProfile
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public SpeechProviderKind Kind { get; init; }
    /// <summary>The catalog entry the service was created from; empty on older settings.</summary>
    public string Preset { get; init; } = "";
    /// <summary>
    /// The service this key belongs to (see <see cref="ServiceIdentity"/>). Keys with
    /// the same vendor share voices and only one of them is active at a time.
    /// </summary>
    public string Vendor { get; init; } = "";
    public string Endpoint { get; init; } = "";
    public string Model { get; init; } = "";
    // Null preserves the provider's defaults when opening older settings.
    public string[]? AvailableModels { get; init; }
    public string CredentialId { get; init; } = "";
    public bool Enabled { get; init; } = true;
    public string ResourceId { get; init; } = "";
    public string AppId { get; init; } = "";
    public string Latency { get; init; } = "balanced";
    public int TimeoutSeconds { get; init; } = 45;
    public string AudioFormat { get; init; } = "mp3";
    /// <summary>Only for <see cref="SpeechProviderKind.CustomHttp"/>: how to build the request and read the answer.</summary>
    public CustomHttpTemplate? Template { get; init; }
}

/// <summary>
/// A user-described HTTP speech API for vendors without a dedicated entry.
/// Placeholders: {{text}}, {{voice}}, {{model}}, {{speed}} in the body and
/// {{key}} only in headers, so a key never ends up in a URL or a log line.
/// </summary>
public sealed record CustomHttpTemplate
{
    /// <summary>One "Name: value" per line.</summary>
    public string Headers { get; init; } = "Authorization: Bearer {{key}}";
    public string Body { get; init; } = "{\"text\": \"{{text}}\", \"voice\": \"{{voice}}\"}";
    /// <summary>"audio" when the body is the sound itself, "json" when it is wrapped.</summary>
    public string Response { get; init; } = "audio";
    /// <summary>Dotted path to the audio inside a JSON answer, e.g. "data.audio".</summary>
    public string JsonPath { get; init; } = "";
    /// <summary>How the JSON field holds the audio: "base64", "hex" or "url".</summary>
    public string Encoding { get; init; } = "base64";
}

/// <summary>A voice is tied to a vendor, not to one particular key of that vendor.</summary>
public sealed record VoiceProfile
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string ProviderId { get; init; } = "";
    /// <summary>
    /// Cached protocol so the library can still describe the voice after the
    /// connection that created it is deleted. Null on settings written by older builds.
    /// </summary>
    public SpeechProviderKind? ProviderKind { get; init; }
    /// <summary>
    /// Cached service identity. Any enabled key of this vendor can synthesize the
    /// voice; empty on settings written before services had an identity.
    /// </summary>
    public string Vendor { get; init; } = "";
    /// <summary>
    /// A model this voice only works with (cloned voices, Volcengine 2.0 voices…).
    /// Empty means it follows the contact's or the service's model.
    /// </summary>
    public string Model { get; init; } = "";
    public string VoiceId { get; init; } = "";
    public string Language { get; init; } = "zh-CN";
    public string ReferenceAudioPath { get; init; } = "";
    public string ReferenceText { get; init; } = "";
    public string PromptLanguage { get; init; } = "zh";
    public string Instructions { get; init; } = "";
    public double Speed { get; init; } = 1;
    public bool IsCustomVoice { get; init; }
}
