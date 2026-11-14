using System.Text.Json;
using System.Text.RegularExpressions;

namespace VoiceBridge.Core;

/// <param name="WeChatSupport">
/// Optional <c>wechatSupport</c> band (for example <c>4.1.11-4.2.0.52</c>) that the
/// published build states it covers. Absent for every release before 1.8.3, so the
/// version gate treats it as "unknown" and falls back to offering the update.
/// </param>
public sealed record UpdateManifest(string Version, DateTimeOffset PublishedAt, Uri Url, string Sha256, long Size, string Notes,
                                    string? WeChatSupport = null)
{
    public const string Feed = "https://iceland.de5.net/voice-bridge/latest.json";
    internal const long MaxArchiveBytes = 1024L * 1024 * 1024;

    public static UpdateManifest Parse(ReadOnlyMemory<byte> json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var item = document.RootElement;
            // The six-field contract stays required and stays closed: the only extra
            // field tolerated is the optional wechatSupport band published from 1.8.3 on.
            var fields = item.EnumerateObject().Select(property => property.Name).ToArray();
            if (item.ValueKind != JsonValueKind.Object ||
                fields.Distinct().Count() != fields.Length ||
                fields.Length is not (6 or 7) ||
                (fields.Length == 7 && !fields.Contains("wechatSupport")))
                throw new InvalidDataException("更新清单字段不正确。");

            var version = item.GetProperty("version").GetString() ?? "";
            var publishedAt = item.GetProperty("publishedAt").GetDateTimeOffset();
            var hash = item.GetProperty("sha256").GetString() ?? "";
            var size = item.GetProperty("size").GetInt64();
            var notes = item.GetProperty("notes").GetString();
            var link = item.GetProperty("url").GetString();
            // A malformed or non-string band degrades to "absent" instead of failing
            // the whole check: the update must stay installable even then.
            var support = item.TryGetProperty("wechatSupport", out var supportItem) &&
                supportItem.ValueKind == JsonValueKind.String ? supportItem.GetString() : null;
            var feed = new Uri(Feed);
            CompareVersions(version, version);
            if (!Uri.TryCreate(link, UriKind.Absolute, out var url) ||
                url.Scheme != Uri.UriSchemeHttps || url.Host != feed.Host || !url.IsDefaultPort ||
                url.UserInfo.Length != 0 || url.Fragment.Length != 0 ||
                !url.AbsolutePath.StartsWith("/voice-bridge/", StringComparison.Ordinal) ||
                !url.AbsolutePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("更新包地址不在受信任的 HTTPS 发布目录。");
            if (notes is null || !Regex.IsMatch(hash, @"\A[0-9a-f]{64}\z") || size is <= 0 or > MaxArchiveBytes)
                throw new InvalidDataException("更新清单中的大小、校验值或说明无效。");
            return new(version, publishedAt, url, hash, size, notes, support);
        }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw new InvalidDataException("更新清单缺少必需字段或格式不正确。", error);
        }
    }

    private static readonly Regex SemVer = new(
        @"\A(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-([A-Za-z0-9-]+(?:\.[A-Za-z0-9-]+)*))?(?:\+[A-Za-z0-9-]+(?:\.[A-Za-z0-9-]+)*)?\z",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    public static int CompareVersions(string left, string right)
    {
        var a = ParseVersion(left);
        var b = ParseVersion(right);
        for (var i = 1; i <= 3; i++)
        {
            var order = CompareNumber(a.Groups[i].Value, b.Groups[i].Value);
            if (order != 0)
                return order;
        }
        var aTags = a.Groups[4].Value;
        var bTags = b.Groups[4].Value;
        if (aTags.Length == 0 || bTags.Length == 0)
            return (bTags.Length == 0 ? 0 : 1) - (aTags.Length == 0 ? 0 : 1);

        var aParts = aTags.Split('.');
        var bParts = bTags.Split('.');
        for (var i = 0; i < Math.Min(aParts.Length, bParts.Length); i++)
        {
            var aNumeric = aParts[i].All(char.IsAsciiDigit);
            var bNumeric = bParts[i].All(char.IsAsciiDigit);
            var order = aNumeric && bNumeric ? CompareNumber(aParts[i], bParts[i])
                : aNumeric != bNumeric ? (aNumeric ? -1 : 1)
                : string.CompareOrdinal(aParts[i], bParts[i]);
            if (order != 0)
                return order;
        }
        return aParts.Length.CompareTo(bParts.Length);
    }

    private static Match ParseVersion(string version)
    {
        var match = SemVer.Match(version);
        if (!match.Success || version.Length > 100 ||
            match.Groups[4].Value.Split('.').Any(part => part.Length > 1 && part[0] == '0' && part.All(char.IsAsciiDigit)))
            throw new InvalidDataException("版本号不符合语义化版本格式。");
        return match;
    }

    private static int CompareNumber(string a, string b) =>
        a.Length == b.Length ? string.CompareOrdinal(a, b) : a.Length.CompareTo(b.Length);
}
