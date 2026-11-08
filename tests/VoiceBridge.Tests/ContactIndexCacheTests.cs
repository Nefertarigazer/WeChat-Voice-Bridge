using VoiceBridge.WeChat;

namespace VoiceBridge.Tests;

public sealed class ContactIndexCacheTests
{
    [Fact]
    public void PersistedIndexIsEncryptedAccountScopedAndInvalidatedWhenDatabaseChanges()
    {
        var temporary = Directory.CreateTempSubdirectory("wvb-contact-index-");
        try
        {
            WeChatSession Session(string account)
            {
                var folder = Path.Combine(temporary.FullName, account, "db_storage", "contact");
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "contact.db"), "simulated encrypted database, no real data");
                return new(temporary.FullName, account, "unused", [], DateTime.UtcNow);
            }
            var one = Session("account-one"); var two = Session("account-two");
            var cachePath = Path.Combine(temporary.FullName, "index-cache");
            var cache = new ContactSearchCache(cachePath);
            cache.Save(one, ContactSearchCache.Fingerprint(one)!, [
                new(new("peer-one", "特殊备注😀", "alias"), ["peer-one", "OriginalNickname", "特殊备注😀", "alias"], 1),
                new(new("peer-two", "特殊备注2", "alias2"), ["peer-two", "OriginalNickname", "特殊备注2", "alias2"], 9)]);
            Assert.Single(Directory.GetFiles(cachePath, "*.dpapi"));
            File.SetLastWriteTimeUtc(Directory.GetFiles(cachePath)[0], new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            Assert.DoesNotContain("OriginalNickname", System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(Directory.GetFiles(cachePath)[0])));
            var restarted = new ContactSearchCache(cachePath);
            Assert.True(restarted.TrySearch(one, "NICK", out var matches));
            Assert.Equal(new[] { "peer-two", "peer-one" }, matches.Select(item => item.Id));
            Assert.False(restarted.TrySearch(two, "NICK", out _));
            Assert.True(restarted.TrySearch(one, "特殊备注", out _));
            Assert.True(restarted.TrySearch(one, "😀", out var emoji));
            Assert.Equal("peer-one", Assert.Single(emoji).Id);
            File.AppendAllText(Path.Combine(one.Root, one.Account, "db_storage", "contact", "contact.db"), "changed");
            Assert.False(restarted.TrySearch(one, "NICK", out _));
        }
        finally { temporary.Delete(recursive: true); }
    }
}
