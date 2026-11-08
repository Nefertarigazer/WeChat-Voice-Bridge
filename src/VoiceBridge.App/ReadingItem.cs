using System.ComponentModel;
using VoiceBridge.Core;

namespace VoiceBridge.App;

/// <summary>
/// An editable row with a separate snapshot for playback and persistence.
/// </summary>
internal sealed class ReadingItem : INotifyPropertyChanged
{
    private bool editing;

    public ReadingItem(ReadingRule rule, bool isDraft = false)
    {
        Rule = rule;
        Committed = isDraft ? null : rule;
        editing = isDraft;
    }

    public ReadingRule Rule { get; private set; }

    public ReadingRule? Committed { get; private set; }

    /// <summary>
    /// True while the row is being edited. Only then the boxes are shown and
    /// the text can be changed.
    /// </summary>
    public bool IsEditing
    {
        get => editing;
        set
        {
            if (editing == value)
                return;
            editing = value;
            Notify();
        }
    }

    /// <summary>How the symbol may be read.</summary>
    public string Match
    {
        get => Rule.Match;
        set
        {
            var text = value ?? "";
            if (Rule.Match == text)
                return;
            Apply(Rule with { Match = text });
        }
    }

    /// <summary>What should be heard instead.</summary>
    public string Say
    {
        get => Rule.Say;
        set
        {
            var text = value ?? "";
            if (Rule.Say == text)
                return;
            Apply(Rule with { Say = text });
        }
    }

    /// <summary>
    /// Where the symbol may be read: on its own, inside a sentence, or
    /// both. The old flag only knew "anywhere", so it follows the scope.
    /// </summary>
    public string Scope
    {
        get => Rule.Scope;
        set
        {
            if (Rule.Scope == value)
                return;
            Apply(Rule with { Scope = value, Anywhere = value != "standalone" });
        }
    }

    /// <summary>The scope as it is shown while the row is not edited.</summary>
    public string ScopeLabel => RuleScopes.Label(Rule.Scope);

    public event PropertyChangedEventHandler? PropertyChanged;

    public void Commit(ReadingRule rule)
    {
        Rule = Committed = rule;
        IsEditing = false;
    }

    private void Apply(ReadingRule rule)
    {
        Rule = rule;
        Notify();
    }

    private void Notify() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
}
