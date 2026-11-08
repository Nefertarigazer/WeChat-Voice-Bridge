namespace VoiceBridge.App;

/// <summary>
/// One account folder under the data root. The label is what the drop down
/// shows, the value is the folder name that the session is bound to.
/// </summary>
internal sealed record AccountChoice(string Value, string Label)
{
    /// <summary>The drop down shows the label, not the record.</summary>
    public override string ToString() => Label;
}
