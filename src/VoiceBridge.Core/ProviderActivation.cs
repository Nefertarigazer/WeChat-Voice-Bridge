using System;
using System.Collections.Generic;
using System.Linq;

namespace VoiceBridge.Core;

/// <summary>
/// One key per vendor is in use at a time. Keys of different vendors are
/// independent: each vendor may have its own active key. These rules live in
/// the data layer so they can be tested without a WPF window.
/// </summary>
public static class ProviderActivation
{
    /// <summary>
    /// Adding a key is not applying it. When this vendor already has an active
    /// key, the new one stays on standby and nothing switches over — including
    /// when it is the third or fourth key being added.
    /// </summary>
    public static bool StaysOnStandby(
        IEnumerable<SpeechProviderProfile>? existing, SpeechProviderProfile candidate) =>
        (existing ?? Array.Empty<SpeechProviderProfile>())
            .Any(item => item is not null && item.Kind == candidate.Kind && item.Enabled);

    /// <summary>
    /// Show the "启用" button only when one vendor has more than one key. A
    /// vendor with a single key needs no choice, and other vendors are ignored.
    /// </summary>
    public static bool NeedsActivation(
        IEnumerable<SpeechProviderProfile>? providers, SpeechProviderProfile candidate) =>
        (providers ?? Array.Empty<SpeechProviderProfile>())
            .Count(item => item is not null && item.Kind == candidate.Kind) >= 2;

    /// <summary>
    /// Make one key of a vendor active and put its other keys on standby. Keys of
    /// other vendors are returned untouched, so each keeps its own active key.
    /// </summary>
    public static List<SpeechProviderProfile> Activate(
        IEnumerable<SpeechProviderProfile>? providers, string id)
    {
        var list = (providers ?? Array.Empty<SpeechProviderProfile>())
            .Where(item => item is not null).ToList();
        var target = list.FirstOrDefault(item => item.Id == id);
        if (target is null || !NeedsActivation(list, target))
            return list;
        return list.Select(item => item.Kind == target.Kind
            ? item with { Enabled = item.Id == id } : item).ToList();
    }
}
