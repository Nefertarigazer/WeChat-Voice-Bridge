using System.Linq;

namespace VoiceBridge.App;

/// <summary>One of the ways a symbol may be read.</summary>
public sealed record ScopeOption(string Value, string Label)
{
    /// <summary>
    /// The drop down shows the label, not the record. The pick box of the
    /// combo box falls back to this text when it has no template.
    /// </summary>
    public override string ToString() => Label;
}

/// <summary>
/// The three ways a symbol may be read: on its own, inside a sentence, or
/// both. The list and the add form share this table.
/// </summary>
public static class RuleScopes
{
    public static readonly ScopeOption[] Options =
    [
        new("standalone", "单独出现时"),
        new("inside", "句中出现时"),
        new("both", "单独 + 句中")
    ];

    /// <summary>The name of a scope, for the case that it is unknown.</summary>
    public static string Label(string scope) =>
        Options.FirstOrDefault(option => option.Value == scope)?.Label ?? Options[0].Label;

    /// <summary>The scope of a rule, when an older build left it out.</summary>
    public static string ScopeOf(string? scope, bool anywhere) =>
        scope is "standalone" or "inside" or "both" ? scope : anywhere ? "both" : "standalone";
}
