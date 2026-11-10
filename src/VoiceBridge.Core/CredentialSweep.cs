using System;
using System.Collections.Generic;
using System.Linq;

namespace VoiceBridge.Core;

/// <summary>
/// Decides which secure-store entries may be deleted. An entry survives only
/// while something still points at it: any service's CredentialId, the
/// top-level FishKeyName, or a contact / test-voice credential override. An
/// entry created during the current session and not yet attached to anything
/// is exempt from the sweep and judged again at the next start, so a key the
/// user just added can never be yanked away mid-flow.
/// </summary>
public static class CredentialSweep
{
    /// <summary>
    /// Every credential name that a settings snapshot still references. Both
    /// enabled and standby services count: their pointer must survive editing
    /// and switching keys later.
    /// </summary>
    public static IReadOnlyCollection<string> ReferencedNames(
        IEnumerable<SpeechProviderProfile?>? providers,
        string? fishKeyName,
        IEnumerable<string?>? contactOrTestOverrides = null)
    {
        var keep = new HashSet<string>(StringComparer.Ordinal);
        foreach (var provider in providers ?? Array.Empty<SpeechProviderProfile?>())
            if (provider is not null && !string.IsNullOrWhiteSpace(provider.CredentialId))
                keep.Add(provider.CredentialId);
        if (!string.IsNullOrWhiteSpace(fishKeyName))
            keep.Add(fishKeyName);
        foreach (var name in contactOrTestOverrides ?? Array.Empty<string?>())
            if (!string.IsNullOrWhiteSpace(name))
                keep.Add(name);
        return keep;
    }

    /// <summary>
    /// The entries that may be removed right now: nothing references them and
    /// they were not created during this session. Entries in the referenced or
    /// session-new sets are never returned, whatever else happens.
    /// </summary>
    public static IReadOnlyList<string> UnreferencedEntries(
        IEnumerable<string>? entryNames,
        IEnumerable<string?>? referencedNames,
        IEnumerable<string>? sessionNewNames = null)
    {
        var keep = new HashSet<string>(
            (referencedNames ?? Array.Empty<string?>())
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name!),
            StringComparer.Ordinal);
        var fresh = new HashSet<string>(
            (sessionNewNames ?? Array.Empty<string>())
                .Where(name => !string.IsNullOrWhiteSpace(name)),
            StringComparer.Ordinal);
        return (entryNames ?? Array.Empty<string>())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Where(name => !keep.Contains(name) && !fresh.Contains(name))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }
}
