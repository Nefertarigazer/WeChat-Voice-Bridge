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
        // Builds before 1.4.7 seeded one undeletable factory Fish connection and a
        // handful of preset voices that pointed at it. Drop both kinds of factory
        // data at read time; every service and voice the user added is kept.
        var withoutFactory = ProviderMigration.RemoveFactoryDefault(settings.Providers, out var removedFactoryDefault);
        // Before 1.6 the vendor of a key was its protocol, which merged every
        // OpenAI-compatible service into one. Give each saved key its own service
        // identity once, while its address is still known.
        var providers = ServiceIdentity.CacheVendor(withoutFactory, out var cachedServices);
        var voices = VoiceLibraryMigration.RemoveFactoryVoices(settings.VoiceLibrary, out var removedVoiceIds);
        // Remember each voice's vendor while its connection still exists. Deleting
        // that connection later must not erase the vendor name from the library.
        var voicesWithVendor = VoiceLibraryMigration.CacheVendor(voices, providers, out var cachedVendors);
        var contacts = VoiceLibraryMigration.CleanContactReferences(settings.ContactProfiles, removedVoiceIds);
        var testVoice = VoiceLibraryMigration.CleanTestVoiceReference(settings.TestVoiceProfileId, removedVoiceIds);
        var migrated = settings with
        {
            Rules = rules,
            Lexicon = new List<WordEntry>(),
            CacheLimitBytes = settings.CacheLimitBytes > 0 ? settings.CacheLimitBytes : CacheLimits.DefaultBytes,
            Providers = providers,
            VoiceLibrary = voicesWithVendor,
            ContactProfiles = contacts,
            TestVoiceProfileId = testVoice
        };
        // Persist the cleanup once, so an upgraded settings.json no longer carries
        // factory entries or references to them. A failed write must never lose the
        // user's other settings.
        if (removedFactoryDefault || removedVoiceIds.Count > 0 || cachedVendors > 0 || cachedServices > 0)
        {
            try { migrated.Save(); }
            catch (Exception) { /* The in-memory lists are already clean; retry next start. */ }
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

    /// <summary>
    /// Reads the sealed store. <paramref name="createdThisLoad"/> lists the
    /// names that only now came into existence (the legacy one-key import), so
    /// the credential sweep can treat them as freshly added and judge them at
    /// the next start instead of the one that just created them.
    /// </summary>
    public static List<NamedKey> Load(out List<string> createdThisLoad)
    {
        createdThisLoad = new List<string>();
        if (File.Exists(KeysFile))
        {
            // The store already exists: never write to it here. An unreadable file
            // must be left exactly as it is — falling through to the legacy import
            // used to replace a whole multi-key store with one legacy key.
            var stored = Unseal(File.ReadAllBytes(KeysFile));
            return stored ?? new();
        }
        if (!File.Exists(LegacyFile))
            return new();
        // An older release kept one key under its own name. Move it over — this is
        // the only place that creates the multi-key store, and it runs solely when
        // there is no store yet.
        var legacy = UnsealText(File.ReadAllBytes(LegacyFile));
        if (string.IsNullOrEmpty(legacy))
            return new();
        var keys = new List<NamedKey> { new("默认", legacy) };
        Save(keys);
        createdThisLoad.Add("默认");
        return keys;
    }

    /// <summary>
    /// Stores a secret under an existing name (replacing that entry's value) or
    /// under a new name. A blank secret is never stored and never replaces a real
    /// one, so no code path can downgrade a working credential to a placeholder.
    /// </summary>
    public static List<NamedKey> Replace(List<NamedKey> keys, string name, string secret)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(secret))
            return keys;
        return keys.Where(item => !string.Equals(item.Name, name, StringComparison.Ordinal))
            .Append(new NamedKey(name, secret)).ToList();
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
