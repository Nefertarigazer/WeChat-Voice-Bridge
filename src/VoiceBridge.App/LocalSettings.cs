using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VoiceBridge.Core;
using VoiceBridge.WeChat;

namespace VoiceBridge.App;

/// <summary>A voice that the user named for themselves.</summary>
internal sealed record NamedVoice(string Name, string Id);

/// <summary>An API key that the user named for themselves.</summary>
internal sealed record NamedKey(string Name, string Value);

internal sealed record LocalSettings
{
    private static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WeChatVoiceBridge");
    private static string SettingsFile => Path.Combine(Root, "settings.json");
    public string Voice { get; init; } = "";
    public string FishKeyName { get; init; } = "";
    public string Latency { get; init; } = "balanced";
    public string Device { get; init; } = "";
    public double Volume { get; init; } = 0.7;
    public bool Preview { get; init; } = true;
    public bool StreamPcm { get; init; }
    public string FishModel { get; init; } = "s2.1-pro-free";
    public WatchMode Watch { get; init; } = WatchMode.EventDriven;
    public int Poll { get; init; } = 1000;
    public int Coalesce { get; init; } = 30;
    public string Retry { get; init; } = "50,100,200";
    public int Capacity { get; init; } = 30;
    public bool RealWeChat { get; init; }
    public string WeChatRoot { get; init; } = "";
    public string WeChatAccount { get; init; } = "";
    public string WeChatConversation { get; init; } = "";
    public string WeChatLabel { get; init; } = "";
    /// <summary>The wechat id of the chosen contact, the one the user reads in WeChat.</summary>
    public string WeChatAlias { get; init; } = "";
    /// <summary>The voices that the user added and named.</summary>
    public List<NamedVoice> Voices { get; init; } = new();
    public List<VoiceProfile> VoiceLibrary { get; init; } = new();
    public List<SpeechProviderProfile> Providers { get; init; } = new();
    public List<ContactProfile> ContactProfiles { get; init; } = new();
    public List<string> SelectedContacts { get; init; } = new();
    public List<ContactHistory> ContactHistory { get; init; } = new();
    public List<SearchHistory> SearchHistory { get; init; } = new();
    public bool SetupCompleted { get; init; }
    public string TestVoiceProfileId { get; init; } = "";
    /// <summary>The readings that the user defined, one list for all of them.</summary>
    public List<ReadingRule> Rules { get; init; } = new();
    /// <summary>
    /// The word list of an older build. It is read once and merged into the
    /// rules, and it stays empty in every file that this build writes.
    /// </summary>
    public List<WordEntry> Lexicon { get; init; } = new();
    /// <summary>True once the first example of the word book was offered.</summary>
    public bool LexiconSeeded { get; init; }
    /// <summary>
    /// The largest amount of scratch space that the cache may keep. It is
    /// cleaned as soon as it grows past this size.
    /// </summary>
    public long CacheLimitBytes { get; init; } = CacheLimits.DefaultBytes;

    public static LocalSettings Load()
    {
        var settings = File.Exists(SettingsFile)
            ? JsonSerializer.Deserialize<LocalSettings>(File.ReadAllText(SettingsFile)) ?? new() : new();
        // A build from before the three ways of reading only knew "anywhere",
        // and an older build kept the words in a list of their own.
        var rules = (settings.Rules ?? new List<ReadingRule>()).Select(NormalizeRule)
            .Concat((settings.Lexicon ?? new List<WordEntry>())
                .Select(word => new ReadingRule(word.Word, word.Say, true) { Scope = "both" }))
            .ToList();
        // Builds before 1.4.7 seeded one undeletable factory Fish connection. Drop it
        // from upgraded settings at read time; every service the user added is kept.
        var providers = ProviderMigration.RemoveFactoryDefault(settings.Providers, out var removedFactoryDefault);
        var migrated = settings with
        {
            Rules = rules,
            Lexicon = new List<WordEntry>(),
            CacheLimitBytes = settings.CacheLimitBytes > 0 ? settings.CacheLimitBytes : CacheLimits.DefaultBytes,
            Providers = providers
        };
        // Persist the removal once, so an upgraded settings.json no longer carries the
        // factory entry. A failed write must never lose the user's other settings.
        if (removedFactoryDefault)
        {
            try { migrated.Save(); }
            catch (Exception) { /* The in-memory list is already clean; retry next start. */ }
        }
        return migrated;
    }

    private static ReadingRule NormalizeRule(ReadingRule rule)
    {
        var scope = rule.Scope;
        if (scope is not ("standalone" or "inside" or "both"))
            scope = rule.Anywhere ? "both" : "standalone";
        return rule with { Scope = scope, Anywhere = scope != "standalone" };
    }

    public void Save() => WriteAtomic(SettingsFile, JsonSerializer.SerializeToUtf8Bytes(this));

    internal static void WriteAtomic(string path, byte[] contents)
    {
        Directory.CreateDirectory(Root);
        var temp = Path.Combine(Root, Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = File.Open(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                stream.Write(contents);
            File.Move(temp, path, overwrite: true);
        }
        finally { File.Delete(temp); }
    }
}

/// <summary>
/// Keeps the API keys away from the screen. Every key is stored under a name
/// that the user chooses, and the file itself is sealed for the current
/// Windows user, so the key never appears in the settings file.
/// </summary>
internal static class KeyStore
{
    private static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WeChatVoiceBridge");
    private static string KeysFile => Path.Combine(Root, "fish-keys.dpapi");
    private static string LegacyFile => Path.Combine(Root, "fish-key.dpapi");

    public static List<NamedKey> Load()
    {
        if (File.Exists(KeysFile))
        {
            var stored = Unseal(File.ReadAllBytes(KeysFile));
            if (stored != null)
                return stored;
        }
        if (!File.Exists(LegacyFile))
            return new();
        // An older release kept one key under its own name. Move it over.
        var legacy = UnsealText(File.ReadAllBytes(LegacyFile));
        if (string.IsNullOrEmpty(legacy))
            return new();
        var keys = new List<NamedKey> { new("默认", legacy) };
        Save(keys);
        return keys;
    }

    public static void Save(List<NamedKey> keys)
    {
        var clear = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(keys));
        try
        {
            var sealedBytes = ProtectedData.Protect(clear, null, DataProtectionScope.CurrentUser);
            try { LocalSettings.WriteAtomic(KeysFile, sealedBytes); }
            finally { CryptographicOperations.ZeroMemory(sealedBytes); }
        }
        finally { CryptographicOperations.ZeroMemory(clear); }
    }

    public static void Clear()
    {
        File.Delete(KeysFile);
        File.Delete(LegacyFile);
    }

    public static string? Find(List<NamedKey> keys, string name)
    {
        var found = keys.FirstOrDefault(item => string.Equals(item.Name, name, StringComparison.Ordinal));
        return found?.Value;
    }

    private static List<NamedKey>? Unseal(byte[] blob)
    {
        var text = UnsealText(blob);
        if (text == null)
            return null;
        try
        {
            return JsonSerializer.Deserialize<List<NamedKey>>(text);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string? UnsealText(byte[] blob)
    {
        try
        {
            var clear = ProtectedData.Unprotect(blob, null, DataProtectionScope.CurrentUser);
            try { return Encoding.UTF8.GetString(clear); }
            finally { CryptographicOperations.ZeroMemory(clear); }
        }
        catch (Exception)
        {
            return null;
        }
    }
}
