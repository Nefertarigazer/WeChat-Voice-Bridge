namespace VoiceBridge.WeChat;

/// <summary>
/// Scratch space for one worker session. It holds only short-lived copies of the
/// encrypted WeChat databases (or the synthetic fixture files) and removes them
/// when the session ends.
///
/// The copies are large, so they live on the same volume as the WeChat data.
/// Keeping them on the system drive fills it up and makes the read fail.
/// </summary>
public sealed class SessionCache : IDisposable
{
    private const string Marker = "fixture.lease";
    private const string VolumeFolder = "WeChatVoiceBridgeCache";
    private const string DataFolder = ".voicebridge-cache";
    /// <summary>
    /// The system drive keeps a smaller budget. It is the last place for the
    /// copies, and it must never be filled up by them.
    /// </summary>
    private const long SystemLimitBytes = 1024L * 1024 * 1024;
    internal static readonly string[] Shards = ["message_0.sqlite", "message_1.sqlite"];
    private static readonly System.Text.RegularExpressions.Regex SessionFile =
        new(@"\A(?:contact|message_[0-9]{1,3})\.sqlite(?:-(?:wal|shm|journal))?(?:\.next)?\z");

    private readonly FileStream lease;
    private readonly string root;
    public string PathName { get; }

    public SessionCache(Guid id, string? preferredRoot = null)
    {
        root = ResolveRoot(preferredRoot);
        Directory.CreateDirectory(root);
        CheckDirectory(root);
        // Sessions that ended in a crash leave their copies behind.
        CleanRoot(root);
        PathName = Path.Combine(root, id.ToString("N"));
        Directory.CreateDirectory(PathName);
        CheckDirectory(PathName);
        lease = File.Open(Path.Combine(PathName, Marker), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
    }

    public void Dispose()
    {
        lease.Dispose();
        Clean(PathName);
    }

    public static int CleanResiduals(string? preferredRoot = null)
    {
        var cleaned = 0;
        foreach (var directory in Roots(preferredRoot))
        {
            if (!Directory.Exists(directory))
                continue;
            try { CheckDirectory(directory); }
            catch (IOException) { continue; }
            catch (UnauthorizedAccessException) { continue; }
            foreach (var session in Directory.EnumerateDirectories(directory))
            {
                if (!Guid.TryParseExact(Path.GetFileName(session), "N", out _))
                    continue;
                try { if (Clean(session)) cleaned++; }
                catch (IOException) { /* Another instance owns it or a handle is still open. */ }
                catch (UnauthorizedAccessException) { }
            }
        }
        return cleaned;
    }

    private static IEnumerable<string> Roots(string? preferredRoot) => Candidates(preferredRoot);

    /// <summary>
    /// The size of every cache folder together, in bytes. It tells the user
    /// how much room the copies take right now.
    /// </summary>
    public static long Measure(string? preferredRoot = null)
    {
        long total = 0;
        foreach (var directory in Roots(preferredRoot))
        {
            if (!Directory.Exists(directory))
                continue;
            try { total += Size(directory); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return total;
    }

    /// <summary>
    /// Removes the oldest sessions until the cache fits in the limit. A
    /// session that is still in use keeps its files, because the lease of a
    /// running worker cannot be opened.
    /// </summary>
    public static int Trim(long limitBytes, string? preferredRoot = null)
    {
        var removed = 0;
        foreach (var directory in Roots(preferredRoot))
        {
            if (!Directory.Exists(directory))
                continue;
            var budget = IsSystemDrive(directory) ? Math.Min(limitBytes, SystemLimitBytes) : limitBytes;
            removed += TrimRoot(directory, budget);
        }
        return removed;
    }

    private static int TrimRoot(string directory, long budget)
    {
        if (budget <= 0)
            return 0;
        try { CheckDirectory(directory); }
        catch (IOException) { return 0; }
        catch (UnauthorizedAccessException) { return 0; }
        var sessions = new List<(string Path, DateTime Stamp, long Size)>();
        long total = 0;
        foreach (var session in Directory.EnumerateDirectories(directory))
        {
            if (!Guid.TryParseExact(Path.GetFileName(session), "N", out _))
                continue;
            long size;
            DateTime stamp;
            try
            {
                size = Size(session);
                stamp = Directory.GetLastWriteTimeUtc(session);
            }
            catch (IOException) { continue; }
            catch (UnauthorizedAccessException) { continue; }
            sessions.Add((session, stamp, size));
            total += size;
        }
        var removed = 0;
        foreach (var session in sessions.OrderBy(item => item.Stamp))
        {
            if (total <= budget)
                break;
            try { if (Clean(session.Path)) { total -= session.Size; removed++; } }
            catch (IOException) { /* Another instance owns it or a handle is still open. */ }
            catch (UnauthorizedAccessException) { }
        }
        return removed;
    }

    private static long Size(string directory)
    {
        long total = 0;
        try
        {
            foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                try { total += new FileInfo(file).Length; }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return total;
    }

    /// <summary>True when the folder lives on the drive that Windows runs from.</summary>
    private static bool IsSystemDrive(string directory)
    {
        try
        {
            var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            var target = Path.GetPathRoot(Path.GetFullPath(directory));
            var system = Path.GetPathRoot(Path.GetFullPath(windows));
            return !string.IsNullOrEmpty(target) && !string.IsNullOrEmpty(system) &&
                string.Equals(target, system, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static string ResolveRoot(string? preferredRoot)
    {
        foreach (var candidate in Candidates(preferredRoot))
        {
            try
            {
                Directory.CreateDirectory(candidate);
                return candidate;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return LocalRoot;
    }

    /// <summary>
    /// The copies need hundreds of megabytes, so they stay on the volume that
    /// holds the WeChat data. The system drive is the last resort because
    /// filling it up breaks the read.
    /// </summary>
    private static IEnumerable<string> Candidates(string? preferredRoot)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in Ordered(preferredRoot))
            if (seen.Add(candidate))
                yield return candidate;
    }

    private static IEnumerable<string> Ordered(string? preferredRoot)
    {
        if (!string.IsNullOrWhiteSpace(preferredRoot))
        {
            string? full = null;
            try
            {
                full = Path.GetFullPath(preferredRoot);
            }
            catch (ArgumentException)
            {
            }
            catch (NotSupportedException)
            {
            }
            catch (PathTooLongException)
            {
            }
            if (full != null)
            {
                yield return Path.Combine(full, DataFolder);
                var volume = Path.GetPathRoot(full);
                if (!string.IsNullOrEmpty(volume))
                    yield return Path.Combine(volume, VolumeFolder);
            }
        }
        yield return Path.Combine(AppContext.BaseDirectory, "cache");
        yield return LocalRoot;
    }

    private static string LocalRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WeChatVoiceBridge", "fixtures");

    private static int CleanRoot(string directory)
    {
        if (!Directory.Exists(directory))
            return 0;
        var cleaned = 0;
        foreach (var session in Directory.EnumerateDirectories(directory))
        {
            if (!Guid.TryParseExact(Path.GetFileName(session), "N", out _))
                continue;
            try { if (Clean(session)) cleaned++; }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return cleaned;
    }

    private static bool Clean(string directory)
    {
        CheckDirectory(directory);
        var marker = Path.Combine(directory, Marker);
        if (!File.Exists(marker) || File.GetAttributes(marker).HasFlag(FileAttributes.ReparsePoint))
            return false;
        using (File.Open(marker, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            foreach (var file in Directory.EnumerateFiles(directory))
                if (SessionFile.IsMatch(Path.GetFileName(file)))
                    File.Delete(file);
        }
        File.Delete(marker);
        Directory.Delete(directory);
        return true;
    }

    private static void CheckDirectory(string directory)
    {
        if (File.GetAttributes(directory).HasFlag(FileAttributes.ReparsePoint) ||
            File.GetAttributes(Path.GetDirectoryName(directory)!).HasFlag(FileAttributes.ReparsePoint))
            throw new IOException("Reparse point in the session cache.");
    }
}
