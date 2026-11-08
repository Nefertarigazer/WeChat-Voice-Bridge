using System.Text.Json;

namespace VoiceBridge.Core;

/// <summary>Tracks acknowledged release notes independently of user settings.</summary>
public sealed class ReleaseNotesState : IDisposable
{
    private readonly string directory;
    private readonly FileStream lease;
    private string SeenFile => Path.Combine(directory, "release-notes-seen.json");

    private ReleaseNotesState(string directory, FileStream lease)
    {
        this.directory = directory;
        this.lease = lease;
    }

    public static ReleaseNotesState? TryAcquire(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var lease = new FileStream(Path.Combine(directory, "release-notes.lock"),
                FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            return new(directory, lease);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public bool ShouldShow(string version, bool existingUser)
    {
        string? seen = null;
        try
        {
            if (File.Exists(SeenFile))
                seen = JsonSerializer.Deserialize<string>(File.ReadAllText(SeenFile));
            if (seen is not null)
                return UpdateManifest.CompareVersions(version, seen) > 0;
        }
        catch (Exception error) when (error is JsonException or InvalidDataException)
        {
            // An interrupted or invalid record is treated as unacknowledged.
        }

        if (existingUser)
            return true;

        // A clean first installation establishes a baseline for later upgrades.
        Acknowledge(version);
        return false;
    }

    public void Acknowledge(string version)
    {
        var temporary = Path.Combine(directory, $"release-notes-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(version));
            File.Move(temporary, SeenFile, overwrite: true);
        }
        finally { File.Delete(temporary); }
    }

    public void Dispose() => lease.Dispose();
}
