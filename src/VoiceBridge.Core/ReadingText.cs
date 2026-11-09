using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace VoiceBridge.Core;

/// <summary>
/// Applies the word-book readings to text composed on the app side. The Python
/// worker already does this for the message body (see _apply_rules in
/// worker/wechat_adapter.py); the announcement prefix is added afterwards, in
/// C#, so it needs the same treatment here. Keep the two implementations in sync.
/// </summary>
public static class ReadingText
{
    /// <param name="ignoreScope">
    /// The announcement is a phrase composed by the app, not a line of chat. A rule
    /// marked "standalone" or "inside" would otherwise miss it (e.g. "face说" is
    /// never a standalone line), so announcements always use substring matching.
    /// </param>
    public static string Apply(string text, IReadOnlyList<ReadingRule>? rules, bool ignoreScope = false)
    {
        if (string.IsNullOrEmpty(text) || rules is null || rules.Count == 0) return text;
        var active = new List<(string Match, string Say, string Scope)>();
        foreach (var rule in rules)
        {
            var match = rule.Match?.Trim() ?? "";
            var say = rule.Say ?? "";
            if (match.Length == 0 || say.Length == 0) continue;
            var scope = (rule.Scope ?? "").Trim().ToLowerInvariant();
            if (scope is not ("standalone" or "inside" or "both"))
                scope = rule.Anywhere ? "both" : "standalone";
            active.Add((match, say, ignoreScope ? "both" : scope));
        }
        // Longest match first, exactly like configure() in the worker.
        foreach (var rule in active.OrderByDescending(item => item.Match.Length))
            text = rule.Scope switch
            {
                "both" => ReplaceGuarded(text, rule.Match, rule.Say),
                "inside" => ReplaceInside(text, rule.Match, rule.Say),
                _ => ReplaceStandalone(text, rule.Match, rule.Say)
            };
        return text;
    }

    private static string ReplaceGuarded(string text, string match, string say)
    {
        // A number that belongs to a longer number keeps its digits.
        if (match.All(char.IsAsciiDigit))
            return Regex.Replace(text, @"(?<![0-9])" + Regex.Escape(match) + @"(?![0-9])", _ => say);
        return text.Replace(match, say, StringComparison.Ordinal);
    }

    private static string ReplaceStandalone(string text, string match, string say)
    {
        var lines = text.Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            if (line.Trim() != match) continue;
            var indent = line.Length - line.TrimStart().Length;
            lines[index] = line[..indent] + say;
        }
        return string.Join("\n", lines);
    }

    private static string ReplaceInside(string text, string match, string say)
    {
        var lines = text.Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            if (!line.Contains(match, StringComparison.Ordinal) || line.Trim() == match) continue;
            lines[index] = ReplaceGuarded(line, match, say);
        }
        return string.Join("\n", lines);
    }
}
