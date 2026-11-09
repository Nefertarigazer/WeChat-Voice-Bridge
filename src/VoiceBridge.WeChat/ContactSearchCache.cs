using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace VoiceBridge.WeChat;

/// <summary>Account-scoped contact metadata only. Never contains database keys or chat text.</summary>
public sealed class ContactSearchCache
{
    public sealed record Row(ContactEntry Contact, string[] Fields, double Modified);
    public sealed record Snapshot(int Schema, string Binding, string Fingerprint, Row[] Rows, Dictionary<string, int[]> Postings);
    private static string CacheRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WeChatVoiceBridge", "contact-index");
    private readonly string directory;
    public ContactSearchCache() : this(CacheRoot) { }
    internal ContactSearchCache(string directory) { this.directory = directory; }
    private Snapshot? current;
    private static string Binding(WeChatSession session) => Path.GetFullPath(Path.Combine(session.Root, session.Account)).TrimEnd(Path.DirectorySeparatorChar).ToUpperInvariant();
    private string FileFor(string binding) => Path.Combine(directory, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(binding))) + ".dpapi");

    public static string? Fingerprint(WeChatSession session)
    {
        try
        {
            var database = Path.Combine(session.Root, session.Account, "db_storage", "contact", "contact.db");
            if (!File.Exists(database)) return null;
            // Only the main database invalidates the index. WeChat rewrites contact.db-wal
            // constantly (and heavily during a voice/video call); including it here made
            // every write look like a schema change and forced a new browse Worker, which
            // re-ran the audited full-address-space key scan on every WeChat process.
            var file = new FileInfo(database);
            return string.Concat(file.Length, ":", file.CreationTimeUtc.Ticks, ":", file.LastWriteTimeUtc.Ticks);
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    /// <summary>Find one saved contact by its stable conversation id, without the 60-result search cap.</summary>
    public bool TryFind(WeChatSession session, string peerId, out ContactEntry? contact)
    {
        contact = null;
        if (!TryLoad(session)) return false;
        var row = current!.Rows.FirstOrDefault(item =>
            string.Equals(item.Contact.Id, peerId, StringComparison.Ordinal));
        contact = row?.Contact;
        return contact is not null;
    }

    private bool TryLoad(WeChatSession session)
    {
        var binding = Binding(session);
        var stamp = Fingerprint(session);
        if (stamp == null) return false;
        try
        {
            if (current?.Binding != binding || current.Fingerprint != stamp)
            {
                current = null;
                var path = FileFor(binding);
                if (!File.Exists(path) || new FileInfo(path).Length > 32 * 1024 * 1024) return false;
                var data = ProtectedData.Unprotect(File.ReadAllBytes(path), Encoding.UTF8.GetBytes(binding), DataProtectionScope.CurrentUser);
                var saved = JsonSerializer.Deserialize<Snapshot>(data);
                if (saved is not { Schema: 1 } || saved.Binding != binding || saved.Fingerprint != stamp || saved.Rows.Length > 50_000) return false;
                current = saved;
            }
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or CryptographicException or JsonException or ArgumentException or NullReferenceException or IndexOutOfRangeException)
        { current = null; return false; }
    }

    public bool TrySearch(WeChatSession session, string query, out IReadOnlyList<ContactEntry> results)
    {
        results = Array.Empty<ContactEntry>();
        try
        {
            if (!TryLoad(session)) return false;
            var snapshot = current!;
            var normalized = query.Trim().ToUpperInvariant();
            IEnumerable<int> candidates = Enumerable.Range(0, snapshot.Rows.Length);
            if (normalized.Length > 0)
            {
                HashSet<int>? matches = null;
                foreach (var gram in Grams(normalized).Distinct(StringComparer.Ordinal))
                {
                    if (!snapshot.Postings.TryGetValue(gram, out var ids)) { results = []; return true; }
                    if (matches == null) matches = ids.ToHashSet(); else matches.IntersectWith(ids);
                }
                candidates = matches ?? [];
            }
            results = candidates.Select(index => snapshot.Rows[index])
                .Where(row => row.Fields.Any(field => field.Contains(normalized, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(row => row.Fields.Any(field => field.Equals(normalized, StringComparison.OrdinalIgnoreCase)) ? 0
                    : row.Fields.Any(field => field.StartsWith(normalized, StringComparison.OrdinalIgnoreCase)) ? 1 : 2)
                .ThenByDescending(row => row.Modified).ThenBy(row => row.Contact.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(row => row.Contact.Id, StringComparer.Ordinal).Take(60).Select(row => row.Contact).ToArray();
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or CryptographicException or JsonException or ArgumentException or NullReferenceException or IndexOutOfRangeException)
        { current = null; return false; }
    }

    public void Save(WeChatSession session, string fingerprint, Row[] rows)
    {
        if (rows.Length > 50_000 || Fingerprint(session) != fingerprint) return;
        var binding = Binding(session);
        var postings = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);
        for (var index = 0; index < rows.Length; index++)
            foreach (var gram in rows[index].Fields.SelectMany(field => Grams(field.ToUpperInvariant())).Distinct(StringComparer.Ordinal))
            {
                if (!postings.TryGetValue(gram, out var ids)) postings[gram] = ids = [];
                ids.Add(index);
            }
        var snapshot = new Snapshot(1, binding, fingerprint, rows, postings.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray(), StringComparer.Ordinal));
        string? temporary = null;
        try
        {
            var bytes = ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(snapshot), Encoding.UTF8.GetBytes(binding), DataProtectionScope.CurrentUser);
            if (bytes.Length > 32 * 1024 * 1024) return;
            Directory.CreateDirectory(directory);
            var path = FileFor(binding);
            temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, path, true);
            current = snapshot;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or CryptographicException) { }
        finally { if (temporary != null && File.Exists(temporary)) { try { File.Delete(temporary); } catch (Exception error) when (error is IOException or UnauthorizedAccessException) { } } }
    }

    private static IEnumerable<string> Grams(string value)
    {
        var runes = value.EnumerateRunes().Select(rune => rune.ToString()).ToArray();
        for (var index = 0; index < runes.Length; index++)
        {
            yield return runes[index];
            if (index + 1 < runes.Length) yield return runes[index] + runes[index + 1];
        }
    }
}
