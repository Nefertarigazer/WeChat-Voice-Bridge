using System;
using System.Collections.Generic;
using System.Linq;

namespace VoiceBridge.Core;

/// <summary>
/// Which service (vendor) a key belongs to. Keys of one service share its voices,
/// and only one of them is in use at a time. A mainstream vendor is identified by
/// its catalog preset, so all of its keys group together wherever they point.
/// A generic protocol or a local model is identified by its own address, so two
/// services that merely speak the same protocol (for example SiliconFlow and a
/// local Kokoro server, both OpenAI-compatible) are never merged into one vendor.
/// </summary>
public static class ServiceIdentity
{
    public const string Fish = "fish";
    public const string OpenAi = "openai";
    public const string Azure = "azure";
    public const string Volcengine = "volcengine";
    public const string FishOfficialEndpoint = "https://api.fish.audio/v1/tts";
    public const string GptSoVitsDefaultEndpoint = "http://127.0.0.1:9880/tts";

    /// <summary>The stored identity, or the derived one for settings that predate it.</summary>
    public static string Of(SpeechProviderProfile provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        return string.IsNullOrWhiteSpace(provider.Vendor) ? Derive(provider) : provider.Vendor;
    }

    public static bool Same(SpeechProviderProfile a, SpeechProviderProfile b) =>
        string.Equals(Of(a), Of(b), StringComparison.Ordinal);

    /// <summary>
    /// Identity for a profile that carries no stored vendor: the four built-in
    /// vendors by name, every other service by protocol plus normalized address.
    /// </summary>
    public static string Derive(SpeechProviderProfile provider)
    {
        var address = NormalizeEndpoint(provider.Endpoint);
        return provider.Kind switch
        {
            SpeechProviderKind.Fish => address.Length == 0 || address == NormalizeEndpoint(FishOfficialEndpoint)
                ? Fish : "fish@" + address,
            SpeechProviderKind.OpenAiCompatible => address.Length == 0 || HostOf(address) == "api.openai.com"
                ? OpenAi : "openai-compatible@" + address,
            SpeechProviderKind.AzureSpeech => Azure,
            SpeechProviderKind.Doubao => Volcengine,
            SpeechProviderKind.GptSoVits => "gpt-sovits@" +
                (address.Length == 0 ? NormalizeEndpoint(GptSoVitsDefaultEndpoint) : address),
            _ => address.Length == 0 ? KindName(provider.Kind) : KindName(provider.Kind) + "@" + address
        };
    }

    /// <summary>
    /// The vendor a voice from an older build belongs to when all that survived is
    /// its protocol. It matches what <see cref="Derive"/> gives the default address
    /// of that protocol, so such a voice comes back once the user adds the service.
    /// </summary>
    public static string Legacy(SpeechProviderKind kind) => kind switch
    {
        SpeechProviderKind.Fish => Fish,
        SpeechProviderKind.OpenAiCompatible => OpenAi,
        SpeechProviderKind.AzureSpeech => Azure,
        SpeechProviderKind.Doubao => Volcengine,
        SpeechProviderKind.GptSoVits => "gpt-sovits@" + NormalizeEndpoint(GptSoVitsDefaultEndpoint),
        _ => KindName(kind)
    };

    /// <summary>
    /// One spelling per address: case, a trailing slash, the OpenAI
    /// "/audio/speech" path and a trailing "/v1" do not make another service.
    /// </summary>
    public static string NormalizeEndpoint(string? endpoint)
    {
        var value = (endpoint ?? "").Trim().TrimEnd('/').ToLowerInvariant();
        if (value.EndsWith("/audio/speech", StringComparison.Ordinal))
            value = value[..^"/audio/speech".Length];
        if (value.EndsWith("/v1", StringComparison.Ordinal))
            value = value[..^"/v1".Length];
        return value.TrimEnd('/');
    }

    /// <summary>Store the identity on settings written before it existed.</summary>
    public static List<SpeechProviderProfile> CacheVendor(
        IEnumerable<SpeechProviderProfile>? providers, out int filled)
    {
        filled = 0;
        var result = new List<SpeechProviderProfile>();
        foreach (var provider in providers ?? Array.Empty<SpeechProviderProfile>())
        {
            if (provider is null)
                continue;
            if (string.IsNullOrWhiteSpace(provider.Vendor))
            {
                result.Add(provider with { Vendor = Derive(provider) });
                filled++;
            }
            else result.Add(provider);
        }
        return result;
    }

    private static string KindName(SpeechProviderKind kind) => kind.ToString().ToLowerInvariant();

    private static string HostOf(string address) =>
        Uri.TryCreate(address, UriKind.Absolute, out var uri) ? uri.Host : "";
}

/// <summary>
/// The model a request uses. Most protocols carry it in <see cref="SpeechProviderProfile.Model"/>;
/// Volcengine selects its model generation through the resource id instead.
/// </summary>
public static class SpeechModels
{
    public const string FishFree = "s2.1-pro-free";
    public const string FishDrama = "drama-3";

    public static string Current(SpeechProviderProfile provider) =>
        provider.Kind == SpeechProviderKind.Doubao ? provider.ResourceId : provider.Model;

    public static SpeechProviderProfile Use(SpeechProviderProfile provider, string? model)
    {
        if (string.IsNullOrWhiteSpace(model))
            return provider;
        return provider.Kind == SpeechProviderKind.Doubao
            ? provider with { ResourceId = model }
            : provider with { Model = model };
    }

    /// <summary>
    /// The models this key may switch between. Old Fish settings carry no list and
    /// keep offering the two models they always offered.
    /// </summary>
    public static string[] Listed(SpeechProviderProfile provider)
    {
        if (provider.Kind == SpeechProviderKind.Fish && provider.AvailableModels is null)
            return [FishFree, FishDrama];
        return new[] { Current(provider) }
            .Concat(provider.AvailableModels ?? [])
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }
}
