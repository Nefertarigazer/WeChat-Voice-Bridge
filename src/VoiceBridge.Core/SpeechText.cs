namespace VoiceBridge.Core;

public static class SpeechText
{
    public const int MaximumLength = 20_000;
    public static IEnumerable<string> Split(string text, int limit = 240)
    {
        if (limit < 2) throw new ArgumentOutOfRangeException(nameof(limit));
        var offset = 0;
        while (offset < text.Length)
        {
            var end = Math.Min(text.Length, offset + limit);
            if (end < text.Length)
            {
                var boundaryFound = false;
                for (var index = end - 1; index >= offset + limit / 2; index--)
                    if ("。！？!?；;".Contains(text[index])) { end = index + 1; boundaryFound = true; break; }
                if (!boundaryFound)
                    for (var index = end - 1; index >= offset; index--)
                        if (char.IsWhiteSpace(text[index])) { end = index + 1; break; }
                if (char.IsHighSurrogate(text[end - 1]) && char.IsLowSurrogate(text[end])) end--;
            }
            yield return text[offset..end];
            offset = end;
        }
    }
}
