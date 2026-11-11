using System;
using System.Collections.Generic;
using System.Linq;

namespace VoiceBridge.Core;

/// <summary>
/// Builds before 1.4.7 seeded a handful of Fish voices into every install, all
/// pointing at the factory connection. New installs ship no voices at all: the
/// library contains only voices the user added, and an upgrade must delete the
/// seeded ones without touching anything the user owns.
/// </summary>
public static class VoiceLibraryMigration
{
    /// <summary>Prefix older builds used for the ids of the voices they seeded.</summary>
    public const string FactoryVoicePrefix = "fish-";

    /// <summary>
    /// A seeded voice carries BOTH the old factory provider id and the old seeding
    /// prefix. A voice the user re-pointed at their own service keeps its id but is
    /// no longer a factory voice, so it is preserved rather than deleted.
    /// </summary>
    public static bool IsFactoryVoice(VoiceProfile? voice) =>
        voice is not null
        && ProviderMigration.IsFactoryDefault(voice.ProviderId)
        && voice.Id.StartsWith(FactoryVoicePrefix, StringComparison.Ordinal);

    /// <summary>
    /// Returns only the voices the user owns. Blank ids and duplicates are dropped,
    /// and <paramref name="removedIds"/> lists the ids that disappeared so the caller
    /// can clear references to them and persist the cleanup once.
    /// </summary>
    public static List<VoiceProfile> RemoveFactoryVoices(
        IEnumerable<VoiceProfile>? voices, out List<string> removedIds)
    {
        removedIds = new List<string>();
        var result = new List<VoiceProfile>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var voice in voices ?? Array.Empty<VoiceProfile>())
        {
            if (voice is null || string.IsNullOrWhiteSpace(voice.Id))
                continue;
            if (IsFactoryVoice(voice))
            {
                removedIds.Add(voice.Id);
                continue;
            }
            if (seen.Add(voice.Id))
                result.Add(voice);
        }
        return result;
    }

    /// <summary>Contacts that selected a removed voice fall back to no selection.</summary>
    public static List<ContactProfile> CleanContactReferences(
        IEnumerable<ContactProfile>? contacts, IEnumerable<string>? removedIds)
    {
        var gone = new HashSet<string>(removedIds ?? Array.Empty<string>(), StringComparer.Ordinal);
        return (contacts ?? Array.Empty<ContactProfile>())
            .Where(profile => profile is not null)
            .Select(profile => gone.Contains(profile.VoiceProfileId)
                ? profile with { VoiceProfileId = "" }
                : profile)
            .ToList();
    }

    /// <summary>The test-voice dropdown must not keep pointing at a removed voice.</summary>
    public static string CleanTestVoiceReference(string? id, IEnumerable<string>? removedIds) =>
        !string.IsNullOrEmpty(id) && (removedIds ?? Array.Empty<string>()).Contains(id, StringComparer.Ordinal)
            ? "" : id ?? "";

    /// <summary>
    /// The service a voice belongs to: the cached identity when present, otherwise
    /// the identity of the connection it points at, otherwise the vendor implied by
    /// its cached protocol. Null only when none of the three survived, which
    /// happens for settings written by a build that knew no vendor at all.
    /// </summary>
    public static string? VendorOf(
        VoiceProfile? voice, IEnumerable<SpeechProviderProfile>? providers)
    {
        if (voice is null)
            return null;
        if (!string.IsNullOrWhiteSpace(voice.Vendor))
            return voice.Vendor;
        var own = (providers ?? Array.Empty<SpeechProviderProfile>())
            .FirstOrDefault(item => item is not null && item.Id == voice.ProviderId);
        if (own is not null)
            return ServiceIdentity.Of(own);
        return voice.ProviderKind is { } kind ? ServiceIdentity.Legacy(kind) : null;
    }

    /// <summary>
    /// Fill in the vendor on voices saved by older builds. Called once at load
    /// time, while their connections still exist, so deleting a connection later
    /// cannot lose the vendor the voice belongs to.
    /// </summary>
    public static List<VoiceProfile> CacheVendor(
        IEnumerable<VoiceProfile>? voices,
        IEnumerable<SpeechProviderProfile>? providers,
        out int cached)
    {
        cached = 0;
        var connections = (providers ?? Array.Empty<SpeechProviderProfile>())
            .Where(item => item is not null && !string.IsNullOrEmpty(item.Id))
            .ToList();
        var result = new List<VoiceProfile>();
        foreach (var voice in voices ?? Array.Empty<VoiceProfile>())
        {
            if (voice is null)
                continue;
            var own = connections.FirstOrDefault(item => item.Id == voice.ProviderId);
            var updated = voice;
            if (updated.ProviderKind is null && own is not null)
                updated = updated with { ProviderKind = own.Kind };
            if (string.IsNullOrWhiteSpace(updated.Vendor) && VendorOf(updated, connections) is { } vendor)
                updated = updated with { Vendor = vendor };
            if (updated != voice)
                cached++;
            result.Add(updated);
        }
        return result;
    }

    /// <summary>
    /// The key that will actually synthesize this voice. A public voice id works
    /// with any valid key of the same vendor, so the voice survives losing the
    /// particular key that created it: the vendor's active key is used, and if
    /// none is marked active the vendor's remaining key is used as-is.
    /// Null only when the vendor has no key left at all — that is the single
    /// condition that makes a voice unavailable.
    /// </summary>
    public static SpeechProviderProfile? ActiveKeyFor(
        VoiceProfile? voice, IEnumerable<SpeechProviderProfile>? providers)
    {
        if (voice is null)
            return null;
        var all = (providers ?? Array.Empty<SpeechProviderProfile>())
            .Where(item => item is not null && !string.IsNullOrEmpty(item.Id)).ToList();
        var own = all.FirstOrDefault(item => item.Id == voice.ProviderId);
        if (own is { Enabled: true })
            return own;
        if (VendorOf(voice, all) is not { } vendor)
            return null;
        return all.FirstOrDefault(item => item.Enabled && ServiceIdentity.Of(item) == vendor)
            ?? all.FirstOrDefault(item => ServiceIdentity.Of(item) == vendor);
    }

    /// <summary>
    /// A voice is unavailable only when every key of its vendor is gone. Adding a
    /// key, switching which key is active, or deleting one of several keys never
    /// affects a voice the user already selected.
    /// </summary>
    public static bool IsUsable(VoiceProfile? voice, IEnumerable<SpeechProviderProfile>? providers) =>
        ActiveKeyFor(voice, providers) is not null;

    /// <summary>
    /// Once the user adds an API service, voices whose service is missing become
    /// usable again by pointing them at that service instead of showing a missing
    /// API. Voices already bound to a live service are never touched, and a voice
    /// known to belong to a DIFFERENT vendor is never re-pointed across vendors.
    /// </summary>
    public static List<VoiceProfile> BindOrphanVoices(
        IEnumerable<VoiceProfile>? voices,
        IEnumerable<SpeechProviderProfile>? providers,
        SpeechProviderProfile target,
        out int bound)
    {
        bound = 0;
        var all = (providers ?? Array.Empty<SpeechProviderProfile>())
            .Where(item => item is not null && !string.IsNullOrEmpty(item.Id)).ToList();
        var live = new HashSet<string>(all.Select(item => item.Id), StringComparer.Ordinal);
        var result = new List<VoiceProfile>();
        foreach (var voice in voices ?? Array.Empty<VoiceProfile>())
        {
            if (voice is null)
                continue;
            if (live.Contains(voice.ProviderId))
            {
                result.Add(voice);
            }
            else if (VendorOf(voice, all) is { } vendor && vendor != ServiceIdentity.Of(target))
            {
                // A different vendor's voice ids cannot be used by this service.
                result.Add(voice);
            }
            else
            {
                result.Add(voice with
                {
                    ProviderId = target.Id, ProviderKind = target.Kind, Vendor = ServiceIdentity.Of(target)
                });
                bound++;
            }
        }
        return result;
    }

    /// <summary>
    /// When the user picks which key of a vendor is active, every voice of that
    /// vendor follows the active key. A public voice id works with any valid key
    /// of the same vendor, so the voice itself never needs to be re-created.
    /// Other vendors are left completely alone.
    /// </summary>
    public static List<VoiceProfile> UseActiveKey(
        IEnumerable<VoiceProfile>? voices,
        IEnumerable<SpeechProviderProfile>? providers,
        SpeechProviderProfile target,
        out int rebound)
    {
        rebound = 0;
        var all = (providers ?? Array.Empty<SpeechProviderProfile>())
            .Where(item => item is not null && !string.IsNullOrEmpty(item.Id)).ToList();
        var result = new List<VoiceProfile>();
        foreach (var voice in voices ?? Array.Empty<VoiceProfile>())
        {
            if (voice is null)
                continue;
            var vendor = ServiceIdentity.Of(target);
            if (VendorOf(voice, all) != vendor)
            {
                result.Add(voice);
                continue;
            }
            var updated = voice with { ProviderId = target.Id, ProviderKind = target.Kind, Vendor = vendor };
            if (updated != voice)
                rebound++;
            result.Add(updated);
        }
        return result;
    }
}
