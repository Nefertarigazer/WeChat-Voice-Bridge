using System;
using System.Collections.Generic;

namespace VoiceBridge.Core;

/// <summary>
/// Builds before 1.4.7 seeded one factory Fish connection ("fish-default") into every
/// install. It could not be deleted, and it silently pointed at whichever API key
/// happened to be in the machine's credential store, so a shipped install could end
/// up spending the developer's quota. New installs start with no services at all.
///
/// This migration removes only that one fixed id. Every service the user added uses
/// a generated id and is kept verbatim, in its original order. Credentials live in a
/// separate encrypted store and are never read, written, or deleted here.
/// </summary>
public static class ProviderMigration
{
    /// <summary>The id of the factory connection in builds before 1.4.7.</summary>
    public const string FactoryDefaultId = "fish-default";

    /// <summary>True only for the factory connection id, matched exactly.</summary>
    public static bool IsFactoryDefault(string? id) =>
        string.Equals(id, FactoryDefaultId, StringComparison.Ordinal);

    /// <summary>
    /// Returns the services the user owns: blank ids, duplicates and the factory
    /// connection are dropped. <paramref name="removed"/> reports whether a stale
    /// factory entry was found, so the caller can persist the cleanup once.
    /// </summary>
    public static List<SpeechProviderProfile> RemoveFactoryDefault(
        IEnumerable<SpeechProviderProfile>? providers, out bool removed)
    {
        removed = false;
        var result = new List<SpeechProviderProfile>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var provider in providers ?? Array.Empty<SpeechProviderProfile>())
        {
            if (provider is null || string.IsNullOrWhiteSpace(provider.Id))
                continue;
            if (IsFactoryDefault(provider.Id))
            {
                removed = true;
                continue;
            }
            if (seen.Add(provider.Id))
                result.Add(provider);
        }
        return result;
    }
}
