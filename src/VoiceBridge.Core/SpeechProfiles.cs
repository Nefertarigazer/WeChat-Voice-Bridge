namespace VoiceBridge.Core;

public enum SpeechProviderKind { Fish, OpenAiCompatible, AzureSpeech, Doubao, GptSoVits }

/// <summary>Non-secret connection settings. CredentialId refers to the user's secure credential store.</summary>
public sealed record SpeechProviderProfile
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public SpeechProviderKind Kind { get; init; }
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
}

/// <summary>A voice is tied to a vendor, not to one particular key of that vendor.</summary>
public sealed record VoiceProfile
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string ProviderId { get; init; } = "";
    /// <summary>
    /// Cached vendor so the library can still show the vendor name after the
    /// connection that created the voice is deleted. Null on settings written by
    /// older builds; LocalSettings.Load() fills it in where the connection still
    /// exists. Any enabled key of this vendor can synthesize the same voice id.
    /// </summary>
    public SpeechProviderKind? ProviderKind { get; init; }
    public string VoiceId { get; init; } = "";
    public string Language { get; init; } = "zh-CN";
    public string ReferenceAudioPath { get; init; } = "";
    public string ReferenceText { get; init; } = "";
    public string PromptLanguage { get; init; } = "zh";
    public string Instructions { get; init; } = "";
    public double Speed { get; init; } = 1;
    public bool IsCustomVoice { get; init; }
}
