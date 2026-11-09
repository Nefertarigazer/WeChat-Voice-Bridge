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
    /// Once the user adds an API service, voices whose service is missing become
    /// usable again by pointing them at that service instead of showing a missing
    /// API. Voices already bound to a live service are never touched.
    /// </summary>
    public static List<VoiceProfile> BindOrphanVoices(
        IEnumerable<VoiceProfile>? voices,
        IEnumerable<SpeechProviderProfile>? providers,
        SpeechProviderProfile target,
        out int bound)
    {
        bound = 0;
        var live = new HashSet<string>((providers ?? Array.Empty<SpeechProviderProfile>())
            .Where(item => item is not null && !string.IsNullOrEmpty(item.Id))
            .Select(item => item.Id), StringComparer.Ordinal);
        var result = new List<VoiceProfile>();
        foreach (var voice in voices ?? Array.Empty<VoiceProfile>())
        {
            if (voice is null)
                continue;
            if (live.Contains(voice.ProviderId))
            {
                result.Add(voice);
            }
            else
            {
                result.Add(voice with { ProviderId = target.Id });
                bound++;
            }
        }
        return result;
    }
}
