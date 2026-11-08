"""Encrypted fixture for the real adapter's database contract."""
from contextlib import closing
import base64
import hashlib
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "worker"))
sys.path.insert(0, str(Path(sys.executable).resolve().parents[2] / "worker" / "deps"))
try:
    import wechat_adapter as adapter
except (ModuleNotFoundError, AttributeError):
    adapter = None

LONG_TEXT = ("唔，哥哥不喜欢我这样吗(委屈到有些沙哑的小奶音，一双小杏眼眼眶红红的，"
             "两个小手指不安的戳戳戳，一颗珍珠泪滴落在粉色睡裙的镂空复古蕾丝花边上)")

# WeChat stores a quoted reply as an app message (type 49) of subtype 57.
REPLY_TYPE = (57 << 32) | 49
CARD_TYPE = (51 << 32) | 49
REPLY_XML = (
    '<?xml version="1.0"?><msg><appmsg appid="" sdkver="0">'
    "<title>hello</title><type>57</type>"
    "<refermsg><type>1</type><svrid>700</svrid><fromusr>peer</fromusr>"
    "<chatusr>peer</chatusr><displayname>friend</displayname>"
    "<content>123</content><msgsource>src</msgsource>"
    "<createtime>1700000000</createtime></refermsg></appmsg></msg>")
# Some rows keep the quoted sentence inside the chat user element.
REPLY_XML_NESTED = (
    '<?xml version="1.0"?><msg><appmsg appid="" sdkver="0">'
    "<title>hello</title><type>57</type>"
    "<refermsg><type>1</type><svrid>700</svrid><fromusr>peer</fromusr>"
    "<chatusr>peer</chatusr><displayname>friend</displayname><content>123</content>"
    "<msgsource>src</msgsource><createtime>1700000000</createtime>"
    "</refermsg></appmsg></msg>")
# A reply that quotes a picture carries the picture XML instead of text.
REPLY_XML_IMAGE = (
    '<?xml version="1.0"?><msg><appmsg appid="" sdkver="0">'
    "<title>hello</title><type>57</type>"
    "<refermsg><type>3</type><svrid>700</svrid><fromusr>peer</fromusr>"
    "<content>&lt;msg&gt;&lt;img/&gt;&lt;/msg&gt;</content>"
    "<msgsource>src</msgsource></refermsg></appmsg></msg>")
CARD_XML = (
    '<?xml version="1.0"?><msg><appmsg appid="" sdkver="0">'
    "<title>a shared link</title><des>link</des><type>5</type></appmsg></msg>")


def emoji_xml(kind, name=""):
    """Build an emoji payload the way WeChat stores it."""
    desc = ""
    if name:
        body = b"\x08\x45\x0bdefault\x12" + bytes([len(name.encode("utf-8"))]) \
            + name.encode("utf-8") + b"\x0c"
        desc = base64.b64encode(body).decode("ascii")
    return ('<?xml version="1.0"?><msg><emoji fromusername="peer" type="%s" md5="abc" '
            'desc="%s" width="240" height="240"/></msg>' % (kind, desc))


@unittest.skipIf(adapter is None, "Use the Windows release's bundled Python and SQLCipher")
class EncryptedAdapterTests(unittest.TestCase):
    def build(self, root, account, peer, shards=("message_0.db",)):
        """Create an encrypted account folder with message shards and a contact list."""
        table = "Msg_" + hashlib.md5(peer.encode(), usedforsecurity=False).hexdigest()
        storage = root / account / "db_storage"
        source = storage / "message"
        source.mkdir(parents=True)
        contact_path = storage / "contact" / "contact.db"
        contact_path.parent.mkdir(parents=True)
        cache = root / "cache"
        cache.mkdir()
        key = bytes(range(32))
        writers = []
        for name in shards:
            writer = adapter.sql.connect(source / name)
            writers.append(writer)
            writer.execute("PRAGMA key = \"x'%s'\"" % key.hex())
            writer.execute("PRAGMA journal_mode=WAL")
            writer.execute("PRAGMA wal_autocheckpoint=0")
            writer.execute("CREATE TABLE Name2Id(user_name TEXT)")
            writer.executemany("INSERT INTO Name2Id VALUES (?)", [(peer,), ("self",)])
            writer.execute(f'CREATE TABLE "{table}" (local_id INTEGER PRIMARY KEY, sort_seq INTEGER, '
                           "local_type INTEGER, real_sender_id INTEGER, message_content BLOB)")
            writer.commit()
        with closing(adapter.sql.connect(contact_path)) as contact:
            contact.execute("PRAGMA key = \"x'%s'\"" % key.hex())
            contact.execute("CREATE TABLE contact (username TEXT, nick_name TEXT, remark TEXT, alias TEXT)")
            # Rows with an empty username sort first and must not hide real contacts.
            contact.executemany("INSERT INTO contact VALUES (?, ?, ?, ?)", [
                ("", "", "", ""),
                ("", "", "", ""),
                (peer, "测试昵称", "测试备注", "test-alias"),
                ("room-id@chatroom", "群聊", "", ""),
            ])
            contact.commit()
        # One key material authenticates one database per pass, so pass one pid per file.
        pids = list(range(1, len(shards) + 2))
        with patch.object(adapter.WeChatDB, "_collect_key_candidates", return_value={(key, None)}):
            store = adapter.WeChatStore(cache, root, account, pids, peer)
        return writers, store, table

    def test_reads_new_target_text_once_without_writing_to_original(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            account, peer = "test-account", "test-peer"
            writers, store, table = self.build(root, account, peer)
            try:
                writer = writers[0]
                self.assertEqual([], store.query()[0])
                writer.executemany(f'INSERT INTO "{table}" VALUES(?, ?, ?, ?, ?)',
                                   [(2, 2, 1, 2, b"out"), (3, 3, 3, 1, b"image"),
                                    (4, 4, 1, 1, "新消息".encode())])
                writer.commit()
                source = root / account / "db_storage" / "message"
                original_before = [(item.name, item.read_bytes()) for item in sorted(source.iterdir())
                                   if not item.name.endswith("-shm")]
                messages, _ = store.query()
                self.assertEqual(["我发了一张图片", "新消息"], [message["text"] for message in messages])
                store.ack([message["key"] for message in messages])
                self.assertEqual([], store.query()[0])
                self.assertEqual(original_before, [(item.name, item.read_bytes()) for item in sorted(source.iterdir())
                                                   if not item.name.endswith("-shm")])
            finally:
                for writer in writers:
                    writer.close()
                store.close()

    def test_decompresses_long_zstd_messages(self):
        compressed = adapter.zstandard.ZstdCompressor().compress(LONG_TEXT.encode("utf-8"))
        self.assertLess(len(compressed), len(LONG_TEXT.encode("utf-8")))
        self.assertEqual(LONG_TEXT, adapter.decode_content(compressed))

    def test_reads_a_compressed_long_message_from_the_database(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            account, peer = "test-account", "test-peer"
            writers, store, table = self.build(root, account, peer)
            try:
                writer = writers[0]
                compressed = adapter.zstandard.ZstdCompressor().compress(LONG_TEXT.encode("utf-8"))
                writer.execute(f'INSERT INTO "{table}" VALUES(5, 5, 1, 1, ?)', (compressed,))
                writer.commit()
                messages, _ = store.query()
                self.assertEqual([LONG_TEXT], [message["text"] for message in messages])
                store.ack([message["key"] for message in messages])
            finally:
                for writer in writers:
                    writer.close()
                store.close()

    def test_drops_the_binary_container_marker(self):
        payload = LONG_TEXT.encode("utf-8") + b"\x01\x00" + b"\x7f\x00" * 8
        self.assertEqual(LONG_TEXT, adapter.decode_content(payload))

    def test_returns_empty_text_for_unreadable_payloads(self):
        self.assertEqual("", adapter.decode_content(None))
        self.assertEqual("", adapter.decode_content(b""))
        self.assertEqual("", adapter.decode_content(b"\x28\xb5\x2f\xfd" + b"\x00" * 8))
        self.assertEqual("", adapter.decode_content(b"\xff\xfe\xfd"))

    def test_recognises_a_quoted_reply_message_type(self):
        self.assertTrue(adapter.is_reply(REPLY_TYPE))
        self.assertFalse(adapter.is_reply(1))
        self.assertFalse(adapter.is_reply(CARD_TYPE))
        self.assertFalse(adapter.is_reply(None))

    def test_reads_new_sentence_before_the_quoted_text(self):
        self.assertEqual("hello，来自引用，123", adapter.reply_text(REPLY_XML.encode("utf-8")))
        self.assertEqual("hello，来自引用，123", adapter.reply_text(REPLY_XML_NESTED.encode("utf-8")))

    def test_reply_parts_keep_standalone_rules_and_the_fixed_connector(self):
        parts = adapter.reply_parts(REPLY_XML.encode("utf-8"))
        try:
            adapter.configure([{"match": "hello", "say": "你好", "scope": "standalone"},
                               {"match": "来自引用", "say": "不能替换的连接词", "scope": "both"}])
            self.assertEqual("你好，来自引用，123", adapter.render_speech(parts))
            adapter.configure([{"match": "123", "say": "一二三", "scope": "standalone"}])
            self.assertEqual("hello，来自引用，一二三", adapter.render_speech(parts))
        finally:
            adapter.configure()

    def test_reads_a_compressed_reply_message(self):
        compressed = adapter.zstandard.ZstdCompressor().compress(REPLY_XML.encode("utf-8"))
        self.assertEqual("hello，来自引用，123", adapter.reply_text(compressed))

    def test_skips_the_quoted_picture_and_keeps_the_new_sentence(self):
        self.assertEqual("hello", adapter.reply_text(REPLY_XML_IMAGE.encode("utf-8")))

    def test_ignores_app_messages_that_are_not_replies(self):
        self.assertEqual("", adapter.extract_text(CARD_XML.encode("utf-8"), CARD_TYPE))
        self.assertEqual("", adapter.extract_text(REPLY_XML.encode("utf-8"), CARD_TYPE))

    def test_reads_a_reply_message_from_the_database(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            account, peer = "test-account", "test-peer"
            writers, store, table = self.build(root, account, peer)
            try:
                writer = writers[0]
                writer.execute(f'INSERT INTO "{table}" VALUES(5, 5, ?, 1, ?)',
                               (REPLY_TYPE, REPLY_XML.encode("utf-8")))
                writer.execute(f'INSERT INTO "{table}" VALUES(6, 6, ?, 1, ?)',
                               (CARD_TYPE, CARD_XML.encode("utf-8")))
                writer.commit()
                messages, _ = store.query()
                self.assertEqual(["hello，来自引用，123"], [message["text"] for message in messages])
                store.ack([message["key"] for message in messages])
                self.assertEqual("hello，来自引用，123", store.last_incoming_text(peer))
            finally:
                for writer in writers:
                    writer.close()
                store.close()

    def test_reads_a_native_emoji_code_as_its_name(self):
        self.assertEqual("亲亲的表情", adapter.spoken_text("[亲亲]"))
        self.assertEqual("你好，亲亲的表情", adapter.spoken_text("你好[亲亲]"))
        self.assertEqual("抱抱的表情，晚安的表情", adapter.spoken_text("[抱抱][晚安]"))
        self.assertEqual("我好想你，爱心的表情", adapter.spoken_text("我好想你[爱心]"))
        self.assertEqual("亲亲的表情，先回消息", adapter.spoken_text("[亲亲]先回消息"))

    def test_keeps_brackets_that_are_not_emoji(self):
        self.assertEqual("[484]", adapter.spoken_text("[484]"))
        self.assertEqual("[学位论文页面]", adapter.spoken_text("[学位论文页面]"))
        self.assertEqual("plain", adapter.spoken_text("plain"))

    def test_announces_an_emoji_without_a_name(self):
        self.assertEqual("我发了一个表情包",
                         adapter.extract_text(emoji_xml("2").encode("utf-8"), 47))
        self.assertEqual("我发送了一个小表情",
                         adapter.extract_text(emoji_xml("1").encode("utf-8"), 47))

    def test_announces_an_emoji_that_nothing_knows(self):
        unknown = None
        for point in range(0x1F300, 0x1FAFF):
            symbol = chr(point)
            if symbol not in adapter.EMOJI_NAMES_ZH and point not in adapter.EMOJI_CHARACTER_NAMES:
                unknown = symbol
                break
        self.assertIsNotNone(unknown)
        self.assertEqual("我发送了一个小表情", adapter.spoken_text(unknown))

    def test_reads_the_meaning_of_a_message_of_punctuation(self):
        self.assertEqual("我很无语了", adapter.spoken_text("..."))
        self.assertEqual("我很无语了", adapter.spoken_text(".."))
        self.assertEqual("我很无语了", adapter.spoken_text("。。。"))
        self.assertEqual("我很无语了", adapter.spoken_text("。。"))
        self.assertEqual("我很无语了", adapter.spoken_text("...."))
        # The three dots that people type reach us as one ellipsis sign.
        self.assertEqual("我很无语了", adapter.spoken_text("…"))
        self.assertEqual("我很无语了", adapter.spoken_text("……"))
        self.assertEqual("啊？什么？", adapter.spoken_text("？"))
        self.assertEqual("啊？什么？", adapter.spoken_text("??"))
        self.assertEqual("哇！", adapter.spoken_text("！"))
        # The meaning only shows when the punctuation stands on its own.
        self.assertEqual("好吧...算了", adapter.spoken_text("好吧...算了"))
        self.assertEqual("你好\n我很无语了\n再见",
                         adapter.spoken_text("你好\n...\n再见"))

    def test_reads_the_digit_one_as_the_character(self):
        # The first entry of the word book reads a lone one as the character.
        try:
            adapter.configure([{"match": "1", "say": "一", "scope": "both"}], [])
            self.assertEqual("我有一个苹果", adapter.spoken_text("我有1个苹果"))
            self.assertEqual("第一名", adapter.spoken_text("第1名"))
            # A number that holds more digits keeps its digits.
            self.assertEqual("一共100元", adapter.spoken_text("一共100元"))
            self.assertEqual("2021年", adapter.spoken_text("2021年"))
        finally:
            adapter.configure([], [])

    def test_keeps_a_number_out_of_a_longer_one(self):
        # A digit that is part of a longer number is read as that number.
        try:
            adapter.configure([{"match": "1", "say": "一", "scope": "both"}], [])
            self.assertEqual("我买了123个", adapter.spoken_text("我买了123个"))
            self.assertEqual("一共12元", adapter.spoken_text("一共12元"))

            adapter.configure([{"match": "1", "say": "一", "scope": "inside"}], [])
            self.assertEqual("一共123元", adapter.spoken_text("一共123元"))
            self.assertEqual("一共一元", adapter.spoken_text("一共1元"))
        finally:
            adapter.configure([], [])

    def test_reads_a_kaomoji_as_its_name(self):
        face = next(iter(adapter.KAOMOJI_NAMES))
        self.assertEqual(adapter.KAOMOJI_NAMES[face], adapter.spoken_text(face))
        # A face that the table does not know is still announced.
        self.assertEqual("一个颜文字", adapter.spoken_text("(´ω｀*)"))
        # A plain sentence is left alone.
        self.assertEqual("hello (world)", adapter.spoken_text("hello (world)"))
        self.assertEqual("这个价格 199 元", adapter.spoken_text("这个价格 199 元"))

    def test_uses_the_readings_that_the_user_defined(self):
        try:
            adapter.configure([{"match": "※", "say": "重点", "anywhere": True}], [])
            self.assertEqual("重点的内容", adapter.spoken_text("※的内容"))
            adapter.configure([{"match": "？？", "say": "什么情况", "anywhere": False}], [])
            self.assertEqual("什么情况", adapter.spoken_text("？？"))
            self.assertEqual("你好？？再见", adapter.spoken_text("你好？？再见"))
        finally:
            adapter.configure([], [])

    def test_reads_a_symbol_only_where_the_user_wants_it(self):
        try:
            adapter.configure([{"match": "※", "say": "重点", "scope": "standalone"}], [])
            # On its own the symbol is read, inside a sentence it stays.
            self.assertEqual("重点", adapter.spoken_text("※"))
            self.assertEqual("你好※再见", adapter.spoken_text("你好※再见"))

            adapter.configure([{"match": "※", "say": "重点", "scope": "inside"}], [])
            # Inside a sentence the symbol is read, on its own it stays.
            self.assertEqual("※", adapter.spoken_text("※"))
            self.assertEqual("你好重点再见", adapter.spoken_text("你好※再见"))

            adapter.configure([{"match": "※", "say": "重点", "scope": "both"}], [])
            self.assertEqual("重点", adapter.spoken_text("※"))
            self.assertEqual("你好重点再见", adapter.spoken_text("你好※再见"))
        finally:
            adapter.configure([], [])

    def test_uses_the_word_book(self):
        # The word book of an older build still reaches the worker.
        try:
            adapter.configure([], [{"word": "yyds", "say": "永远的神"}])
            self.assertEqual("这也太永远的神了", adapter.spoken_text("这也太yyds了"))
        finally:
            adapter.configure([], [])

    def test_reads_a_word_like_any_other_reading(self):
        # The word book is one list now, so a word is read like a symbol.
        try:
            adapter.configure([{"match": "yyds", "say": "永远的神", "scope": "both"}], [])
            self.assertEqual("这也太永远的神了", adapter.spoken_text("这也太yyds了"))
        finally:
            adapter.configure([], [])

    def test_spells_out_an_abbreviation(self):
        self.assertEqual("Oh my god", adapter.spoken_text("omg"))
        self.assertEqual("Oh my god", adapter.spoken_text("OMG"))
        self.assertEqual("Oh my god", adapter.spoken_text("Omg"))
        self.assertEqual("天啊Oh my god", adapter.spoken_text("天啊omg"))
        # A word that merely starts with the letters is left alone.
        self.assertEqual("omgwtf", adapter.spoken_text("omgwtf"))
        # The word book of the user comes first.
        try:
            adapter.configure([], [{"word": "omg", "say": "天哪"}])
            self.assertEqual("天哪", adapter.spoken_text("omg"))
        finally:
            adapter.configure([], [])

    def test_announces_an_emoji_with_its_name(self):
        self.assertEqual("我发了一个亲亲的表情包",
                         adapter.extract_text(emoji_xml("2", "亲亲").encode("utf-8"), 47))
        # The picture of an emoji that WeChat draws itself is a sticker as well.
        self.assertEqual("我发了一个哭的表情包",
                         adapter.extract_text(emoji_xml("1", "哭").encode("utf-8"), 47))

    def test_announces_a_photograph(self):
        self.assertEqual("我发了一张图片", adapter.extract_text(b"image", 3))
        self.assertEqual("我发了一张图片", adapter.extract_text(b"", 3))
        # A voice note and a video are not read out.
        self.assertEqual("", adapter.extract_text(b"voice", 34))
        self.assertEqual("", adapter.extract_text(b"video", 43))

    def test_reads_an_emoji_that_the_keyboard_typed(self):
        self.assertEqual("点赞的表情", adapter.spoken_text("👍"))
        self.assertEqual("谢谢，点赞的表情", adapter.spoken_text("谢谢👍"))
        self.assertEqual("思考的表情，再见", adapter.spoken_text("🤔再见"))
        # A skin tone belongs to the emoji in front of it.
        self.assertEqual("点赞的表情", adapter.spoken_text("👍🏻"))

    def test_reads_an_emoji_message_from_the_database(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            account, peer = "test-account", "test-peer"
            writers, store, table = self.build(root, account, peer)
            try:
                writer = writers[0]
                writer.execute(f'INSERT INTO "{table}" VALUES(5, 5, 47, 1, ?)',
                               (emoji_xml("1", "亲亲").encode("utf-8"),))
                writer.execute(f'INSERT INTO "{table}" VALUES(6, 6, 47, 1, ?)',
                               (emoji_xml("2", "ok").encode("utf-8"),))
                writer.execute(f'INSERT INTO "{table}" VALUES(7, 7, 1, 1, ?)',
                               ("[微笑]".encode("utf-8"),))
                writer.commit()
                messages, _ = store.query()
                self.assertEqual(["我发了一个亲亲的表情包", "我发了一个ok的表情包", "微笑的表情"],
                                 [message["text"] for message in messages])
                store.ack([message["key"] for message in messages])
            finally:
                for writer in writers:
                    writer.close()
                store.close()

    def test_reads_a_photograph_from_the_database(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            account, peer = "test-account", "test-peer"
            writers, store, table = self.build(root, account, peer)
            try:
                writer = writers[0]
                writer.execute(f'INSERT INTO "{table}" VALUES(5, 5, 3, 1, ?)', (b"image",))
                writer.commit()
                messages, _ = store.query()
                self.assertEqual(["我发了一张图片"], [message["text"] for message in messages])
                store.ack([message["key"] for message in messages])
                self.assertEqual("我发了一张图片", store.last_incoming_text(peer))
            finally:
                for writer in writers:
                    writer.close()
                store.close()

    def test_searches_contacts_and_skips_rows_without_an_id(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            account, peer = "test-account", "test-peer"
            writers, store, _ = self.build(root, account, peer)
            try:
                # The wechat id of the contact is shown next to the name.
                contacts = store.search_contacts("测试")
                self.assertEqual([{"id": peer, "name": "测试备注", "wechat": "test-alias"}], contacts)
                self.assertEqual([{"id": peer, "name": "测试备注", "wechat": "test-alias"}],
                                 store.search_contacts(""))
                # A contact without a wechat id keeps the plain one.
                self.assertEqual([{"id": peer, "name": "测试备注", "wechat": "test-alias"}],
                                 store.search_contacts("test-alias"))
            finally:
                for writer in writers:
                    writer.close()
                store.close()

    def test_picks_the_newest_message_across_shards_by_sequence(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            account, peer = "test-account", "test-peer"
            writers, store, table = self.build(root, account, peer, shards=("message_0.db", "message_1.db"))
            try:
                # A larger local id in another shard must not win over a newer timestamp.
                writers[1].execute(f'INSERT INTO "{table}" VALUES(99, 100, 1, 1, ?)', ("较早的一条".encode(),))
                writers[1].commit()
                writers[0].execute(f'INSERT INTO "{table}" VALUES(5, 900, 1, 1, ?)', ("较晚的一条".encode(),))
                writers[0].commit()
                self.assertEqual("较晚的一条", store.last_incoming_text(peer))
            finally:
                for writer in writers:
                    writer.close()
                store.close()
