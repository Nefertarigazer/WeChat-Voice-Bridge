using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

// Read-only inspector for the credential store. Prints entry names, value
// lengths and an 8-hex fingerprint of each value so identical values can be
// grouped — the secret itself is never printed or persisted anywhere.
var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "WeChatVoiceBridge");
var keysFile = Path.Combine(root, "fish-keys.dpapi");
var settingsFile = Path.Combine(root, "settings.json");

var entries = new List<(string Name, int Length, string Print)>();
if (File.Exists(keysFile))
{
    var clear = ProtectedData.Unprotect(File.ReadAllBytes(keysFile), null, DataProtectionScope.CurrentUser);
    var text = Encoding.UTF8.GetString(clear);
    using var doc = JsonDocument.Parse(text);
    foreach (var item in doc.RootElement.EnumerateArray())
    {
        var name = item.GetProperty("Name").GetString() ?? "";
        var value = item.GetProperty("Value").GetString() ?? "";
        var print = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..8];
        entries.Add((name, value.Length, print));
    }
}
Console.WriteLine($"entries: {entries.Count}");
foreach (var (name, length, print) in entries)
    Console.WriteLine($"   [{name}]  len={length}  fp={print}");

using var settings = JsonDocument.Parse(File.ReadAllText(settingsFile));
var providers = settings.RootElement.GetProperty("Providers");
Console.WriteLine("providers:");
foreach (var p in providers.EnumerateArray())
{
    var id = p.GetProperty("Id").GetString() ?? "";
    var cred = p.TryGetProperty("CredentialId", out var c) ? c.GetString() ?? "" : "";
    var match = entries.FirstOrDefault(x => x.Name == cred);
    var shell = cred.EndsWith(id, StringComparison.Ordinal)
        ? " (credential name derived from the provider id)" : "";
    Console.WriteLine($"   id={id}");
    Console.WriteLine($"   CredentialId=[{cred}]{shell} -> len={match.Length} fp={match.Print}");

    // Which other entries hold exactly the same value as this one?
    var same = entries.Where(x => x.Name != cred && x.Print == match.Print && x.Length == match.Length)
                      .Select(x => x.Name).ToList();
    Console.WriteLine(same.Count > 0
        ? "   same value also stored as: " + string.Join(", ", same)
        : "   no other entry holds this value");
}

Console.WriteLine("grouped by value fingerprint:");
foreach (var g in entries.GroupBy(x => (x.Length, x.Print)).OrderByDescending(g => g.Key.Length))
    Console.WriteLine($"   len={g.Key.Length} fp={g.Key.Print} -> {string.Join(", ", g.Select(x => x.Name))}");
