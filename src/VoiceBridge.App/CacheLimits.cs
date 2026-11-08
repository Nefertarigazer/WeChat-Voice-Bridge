using System.Linq;

namespace VoiceBridge.App;

/// <summary>One of the sizes that the scratch cache may keep.</summary>
public sealed record CacheLimit(long Bytes, string Label)
{
    /// <summary>
    /// The drop down shows the label. The pick box of the combo box falls
    /// back to this text when it has no template.
    /// </summary>
    public override string ToString() => Label;
}

/// <summary>
/// The sizes the user may choose for the scratch cache. The default is four
/// gigabytes, which holds a few sessions of the copied databases.
/// </summary>
public static class CacheLimits
{
    public const long DefaultBytes = 4L * 1024 * 1024 * 1024;

    public static readonly CacheLimit[] Options =
    [
        new(1L * 1024 * 1024 * 1024, "1 GB"),
        new(2L * 1024 * 1024 * 1024, "2 GB"),
        new(DefaultBytes, "4 GB"),
        new(8L * 1024 * 1024 * 1024, "8 GB")
    ];

    /// <summary>The choice that fits the given number of bytes.</summary>
    public static CacheLimit Of(long bytes) =>
        Options.FirstOrDefault(option => option.Bytes == bytes) ?? Options[2];
}
