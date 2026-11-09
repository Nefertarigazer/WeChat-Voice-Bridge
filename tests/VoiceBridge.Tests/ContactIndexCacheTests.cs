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

    [Fact]
    public void Wal_churn_keeps_the_index_and_a_saved_avatar_is_findable_by_id()
    {
        var temporary = Directory.CreateTempSubdirectory("wvb-contact-index-avatar-");
        try
        {
            var folder = Path.Combine(temporary.FullName, "account", "db_storage", "contact");
            Directory.CreateDirectory(folder);
            var database = Path.Combine(folder, "contact.db");
            File.WriteAllText(database, "simulated encrypted database, no real data");
            var session = new WeChatSession(temporary.FullName, "account", "unused", [], DateTime.UtcNow);
            var cachePath = Path.Combine(temporary.FullName, "index-cache");
            var cache = new ContactSearchCache(cachePath);
            // Row 79 is beyond TrySearch's 60-result cap but must still be recoverable.
            var rows = Enumerable.Range(0, 80).Select(index => new ContactSearchCache.Row(
                new ContactEntry("peer-" + index, "name" + index, "alias" + index,
                    index == 79 ? "https://qlogo.cn/last.png" : null),
                ["peer-" + index, "name" + index], index)).ToArray();
            cache.Save(session, ContactSearchCache.Fingerprint(session)!, rows);
            var restarted = new ContactSearchCache(cachePath);
            // WeChat rewrites the WAL constantly, especially during a call. It must not
            // invalidate the index, or every write would launch a new key-scanning Worker.
            File.WriteAllText(database + "-wal", "wal bytes");
            File.SetLastWriteTimeUtc(database + "-wal", DateTime.UtcNow.AddMinutes(1));
            Assert.True(restarted.TryFind(session, "peer-79", out var last));
            Assert.Equal("https://qlogo.cn/last.png", last!.AvatarUrl);
            Assert.True(restarted.TrySearch(session, "", out var visible));
            Assert.Equal(60, visible.Count);
            Assert.False(restarted.TryFind(session, "absent", out _));
            // A real change to the main database must still invalidate the cache.
            File.AppendAllText(database, "changed");
            Assert.False(restarted.TryFind(session, "peer-79", out _));
        }
        finally { temporary.Delete(recursive: true); }
    }
}
