using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

// Repairs the service -> credential pointer that a migration placeholder broke.
// Secret values are never printed, logged or written anywhere outside the sealed
// store itself; only names, lengths and HTTP status codes are reported.
//
//   CredentialRepair probe              try every candidate key with a tiny TTS
//   CredentialRepair repair <name>      point the Fish service at that entry
//   CredentialRepair verify             synthesize once through the stored pointer

var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "WeChatVoiceBridge");
var keysFile = Path.Combine(root, "fish-keys.dpapi");
var settingsFile = Path.Combine(root, "settings.json");
var mode = args.Length > 0 ? args[0].ToLowerInvariant() : "probe";

var entries = ReadKeys();
Console.WriteLine($"entries: {entries.Count}");

var voiceId = ReadVoiceId();
Console.WriteLine($"reference voice id: {voiceId}");

switch (mode)
{
    case "probe":
        // One representative per distinct value keeps the probe cheap: duplicates
        // of the same key cannot answer differently.
        foreach (var (name, value) in entries
                     .Where(x => x.Value.Length >= 20)
                     .GroupBy(x => Fp(x.Value))
                     .Select(g => g.First()))
        {
            var (status, bytes, detail) = await Synthesize(value, voiceId, "好");
            var copies = entries.Count(x => Fp(x.Value) == Fp(value) && x.Value == value);
            Console.WriteLine($"   [{name}] len={value.Length} fp={Fp(value)} x{copies} -> HTTP {status}, {bytes} bytes {detail}");
        }
        break;

    case "repair":
        if (args.Length < 2) { Console.WriteLine("usage: repair <entry-name>"); return 1; }
        Repair(args[1]);
        break;

    case "verify":
        var pointer = ReadPointer();
        Console.WriteLine($"pointer: [{pointer}]");
        var entry = entries.FirstOrDefault(x => x.Name == pointer);
        if (entry.Value is null)
        {
            Console.WriteLine("FATAL: the pointer resolves to no entry in the store");
            return 2;
        }
        Console.WriteLine($"resolved entry len={entry.Value.Length} fp={Fp(entry.Value)}");
        if (entry.Value.Length < 20)
        {
            Console.WriteLine("FATAL: the resolved entry is too short to be a real key (placeholder)");
            return 3;
        }
        var (code, count, note) = await Synthesize(entry.Value, voiceId, "试听成功");
        Console.WriteLine($"synthesis: HTTP {code}, {count} bytes {note}");
        return code == 200 && count > 0 ? 0 : 4;

    case "inject":
        // Self-verification helper: plants one unreferenced garbage entry so a
        // later app start can prove the credential sweep removes exactly that.
        if (args.Length < 2) { Console.WriteLine("usage: inject <entry-name>"); return 1; }
        Inject(args[1]);
        break;

    default:
        Console.WriteLine("usage: probe | repair <entry-name> | verify | inject <entry-name>");
        return 1;
}

return 0;

// ---------------------------------------------------------------- helpers

List<(string Name, string Value)> ReadKeys()
{
    var list = new List<(string, string)>();
    if (!File.Exists(keysFile)) return list;
    var clear = ProtectedData.Unprotect(File.ReadAllBytes(keysFile), null, DataProtectionScope.CurrentUser);
    var text = Encoding.UTF8.GetString(clear);
    CryptographicOperations.ZeroMemory(clear);
    using var doc = JsonDocument.Parse(text);
    foreach (var item in doc.RootElement.EnumerateArray())
        list.Add((item.GetProperty("Name").GetString() ?? "", item.GetProperty("Value").GetString() ?? ""));
    return list;
}

string ReadPointer()
{
    using var settings = JsonDocument.Parse(File.ReadAllText(settingsFile));
    foreach (var p in settings.RootElement.GetProperty("Providers").EnumerateArray())
        if (p.TryGetProperty("CredentialId", out var c))
            return c.GetString() ?? "";
    return "";
}

string ReadVoiceId()
{
    using var settings = JsonDocument.Parse(File.ReadAllText(settingsFile));
    var testId = settings.RootElement.TryGetProperty("TestVoiceProfileId", out var t) ? t.GetString() ?? "" : "";
    var voices = settings.RootElement.GetProperty("VoiceLibrary");
    foreach (var v in voices.EnumerateArray())
        if (v.GetProperty("Id").GetString() == testId && v.TryGetProperty("VoiceId", out var id))
            return id.GetString() ?? "";
    foreach (var v in voices.EnumerateArray())
        if (v.TryGetProperty("VoiceId", out var id) && (id.GetString() ?? "").Length > 0)
            return id.GetString() ?? "";
    return "";
}

void Repair(string entryName)
{
    if (entries.All(x => x.Name != entryName))
    {
        Console.WriteLine($"FATAL: no entry named [{entryName}] in the store");
        Environment.Exit(5);
    }
    var backup = settingsFile + ".bak-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
    File.Copy(settingsFile, backup, overwrite: true);
    Console.WriteLine($"backup: {backup}");

    var node = JsonNode.Parse(File.ReadAllText(settingsFile))!;
    var providers = node["Providers"]!.AsArray();
    foreach (var p in providers)
    {
        var before = p!["CredentialId"]?.GetValue<string>() ?? "";
        p!["CredentialId"] = entryName;
        Console.WriteLine($"   provider {p!["Id"]?.GetValue<string>()}: [{before}] -> [{entryName}]");
    }
    // Keep the credential picker in sync, so the app resolves the same entry.
    if (node.AsObject().ContainsKey("FishKeyName"))
    {
        var before = node["FishKeyName"]?.GetValue<string>() ?? "";
        node["FishKeyName"] = entryName;
        Console.WriteLine($"   FishKeyName: [{before}] -> [{entryName}]");
    }
    var tmp = settingsFile + ".tmp";
    File.WriteAllText(tmp, node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
    File.Move(tmp, settingsFile, overwrite: true);

    var pointer = ReadPointer();
    Console.WriteLine(pointer == entryName
        ? $"re-read ok: pointer is [{pointer}]"
        : $"FATAL: re-read gave [{pointer}], expected [{entryName}]");
}

string Fp(string value) =>
    Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..8];

void Inject(string name)
{
    if (string.IsNullOrWhiteSpace(name) || entries.Any(x => x.Name == name))
    {
        Console.WriteLine($"FATAL: entry [{name}] already exists or is blank");
        Environment.Exit(5);
    }
    var list = entries
        .Select(x => new { x.Name, x.Value })
        .Append(new { Name = name, Value = "test-orphan-" + name })
        .ToList();
    var clear = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(list));
    try
    {
        var sealedBytes = ProtectedData.Protect(clear, null, DataProtectionScope.CurrentUser);
        try { File.WriteAllBytes(keysFile, sealedBytes); }
        finally { CryptographicOperations.ZeroMemory(sealedBytes); }
    }
    finally { CryptographicOperations.ZeroMemory(clear); }
    Console.WriteLine($"injected [{name}] with a garbage value; nothing references it");
}

async Task<(int Status, long Bytes, string Detail)> Synthesize(string apiKey, string voice, string text)
{
    try
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.fish.audio/v1/tts");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Headers.Add("model", "s2.1-pro-free");
        request.Content = JsonContent.Create(new
        {
            text,
            reference_id = voice,
            format = "mp3",
            sample_rate = 44100,
            latency = "balanced"
        });
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        var body = await response.Content.ReadAsByteArrayAsync();
        var code = (int)response.StatusCode;
        return (code, body.Length, code == 200 ? "OK" : Truncate(Encoding.UTF8.GetString(body)));
    }
    catch (Exception ex)
    {
        return (0, 0, ex.GetType().Name + ": " + ex.Message);
    }
}

static string Truncate(string text)
{
    var flat = text.Replace('\n', ' ').Trim();
    return flat.Length <= 160 ? flat : flat[..160] + "…";
}
