"""Session-scoped read-only adapter for the verified Windows WeChat schema."""
from contextlib import closing
import base64
import hashlib
import heapq
import io
from pathlib import Path
import re
import shutil
import time

from audited_wechat_keys import WeChatDB
from db_adapter import BATCH_PER_SHARD, TEXT_LIMIT, IdentityChanged
from contact_index import ContactIndex
from emoji_names import EMOJI_NAMES_ZH
from kaomoji_names import KAOMOJI_MARKERS, KAOMOJI_NAMES
from sqlcipher3 import dbapi2 as sql
import zstandard

SHARD = re.compile(r"message_\d{1,3}\.db")
MAX_WAL_BYTES = 64 * 1024 * 1024
MAX_TEXT_BYTES = 1024 * 1024
ZSTD_MAGIC = b"\x28\xb5\x2f\xfd"
CONTAINER_MARKER = b"\x01\x00"

# WeChat 4 packs a message type into one 64 bit value: the low half keeps the
# classic type and the high half carries the subtype of an app message.
TYPE_BITS = 0xFFFFFFFF
SUBTYPE_BITS = 0xFFFF
APPMSG_TYPE = 49
REPLY_SUBTYPE = 57
EMOJI_TYPE = 47
IMAGE_TYPE = 3

# Rows that carry something a listener should hear: plain text, a picture, an
# emoji and quoted replies. Every other app message is a link, a file or a card.
TEXT_ROWS = ("msg.local_type = 1 OR msg.local_type = 3 OR msg.local_type = 47 "
             "OR ((msg.local_type & 4294967295) = 49 "
             "AND ((msg.local_type >> 32) & 65535) = 57)")
# System notices (tips, recalls) may carry either side's sender id.
SYSTEM_TYPES = "10000, 10002"

APPMSG_BLOCK = re.compile(r"<appmsg\b[^>]*>(.*?)</appmsg>", re.S)
TITLE_TAG = re.compile(r"<title>(.*?)</title>", re.S)
REFER_BLOCK = re.compile(r"<refermsg\b[^>]*>(.*?)</refermsg>", re.S)
TYPE_TAG = re.compile(r"<type>(.*?)</type>", re.S)
CONTENT_TAG = re.compile(r"<content>(.*?)</content>", re.S)
MSG_SOURCE_MARK = "<msgsource"
ENTITIES = (("&lt;", "<"), ("&gt;", ">"), ("&quot;", '"'), ("&apos;", "'"), ("&amp;", "&"))

EMOJI_TAG = re.compile(r"<emoji\b([^>]*)/?>", re.S)
XML_ATTRIBUTE = re.compile(r"([A-Za-z_][A-Za-z0-9_:\-]*)\s*=\s*\"([^\"]*)\"")
CHINESE_RUN = re.compile(r"[一-鿿]{1,6}")
ASCII_RUN = re.compile(rb"[\x20-\x7e]{2,}")
EMOJI_CODE = re.compile(r"\[([^\[\]\r\n]{1,12})\]")

# A sticker pack image carries type 2, the emoji that WeChat itself draws
# carries type 1. The rarer kinds are still emoji the client ships with.
STICKER_TYPE = "2"

# Emoji that the system keyboard types into a message are plain characters.
# They are read as the name of an emoji, the same way a picture of one is.
EMOJI_CHARACTER = re.compile(
    "([\U0001F300-\U0001F5FF\U0001F600-\U0001F64F\U0001F680-\U0001F6FF"
    "\U0001F900-\U0001F9FF\U0001FA70-\U0001FAFF\U0001F1E6-\U0001F1FF"
    "\u2600-\u26FF\u2B00-\u2BFF])")
SKIN_TONE = range(0x1F3FB, 0x1F400)

EMOJI_CHARACTER_NAMES = {
    0x1F600: "开心", 0x1F601: "开心", 0x1F602: "开心", 0x1F603: "开心", 0x1F604: "开心",
    0x1F605: "苦笑", 0x1F606: "笑哭", 0x1F607: "捂脸", 0x1F608: "得意", 0x1F609: "眨眼",
    0x1F60A: "开心", 0x1F60B: "调皮", 0x1F60C: "放松", 0x1F60D: "喜欢", 0x1F60E: "生气",
    0x1F60F: "得意", 0x1F610: "无语", 0x1F611: "无语", 0x1F612: "生气", 0x1F613: "捂脸",
    0x1F614: "开心", 0x1F615: "委屈", 0x1F616: "疑惑", 0x1F617: "亲亲", 0x1F618: "飞吻",
    0x1F619: "飞吻", 0x1F61A: "亲亲", 0x1F61B: "得意", 0x1F61C: "调皮", 0x1F61D: "得意",
    0x1F61E: "不满", 0x1F61F: "难过", 0x1F620: "生气", 0x1F621: "生气", 0x1F622: "大哭",
    0x1F623: "开心", 0x1F624: "开心", 0x1F625: "难过", 0x1F626: "惊恐", 0x1F627: "难过",
    0x1F628: "害怕", 0x1F629: "难过", 0x1F62A: "困", 0x1F62B: "累", 0x1F62C: "惊讶",
    0x1F62D: "害怕", 0x1F62E: "惊讶", 0x1F62F: "难过", 0x1F630: "难过", 0x1F631: "尖叫",
    0x1F632: "害怕", 0x1F633: "脸红", 0x1F634: "睡着", 0x1F635: "晕", 0x1F636: "无表情",
    0x1F637: "戴口罩", 0x1F638: "得意", 0x1F639: "哭", 0x1F63A: "开心", 0x1F63B: "开心",
    0x1F63C: "开心", 0x1F63D: "开心", 0x1F63E: "开心", 0x1F63F: "难过", 0x1F640: "大哭",
    0x1F641: "苦笑", 0x1F642: "微笑", 0x1F643: "无奈", 0x1F644: "得意", 0x1F645: "不理",
    0x1F646: "疑惑", 0x1F647: "委屈", 0x1F648: "瞪眼", 0x1F649: "害怕", 0x1F64A: "疲惫",
    0x1F64B: "举手", 0x1F64C: "点赞", 0x1F64D: "疑惑", 0x1F64E: "不满", 0x1F64F: "捂脸",
    0x1F910: "疑惑", 0x1F911: "疑惑", 0x1F912: "晕", 0x1F913: "思考", 0x1F914: "思考",
    0x1F915: "皱眉", 0x1F916: "疑惑", 0x1F917: "拥抱", 0x1F918: "摇滚", 0x1F919: "打电话",
    0x1F91A: "举手", 0x1F91B: "鼓掌", 0x1F91C: "握手", 0x1F91D: "握手", 0x1F91E: "祈求",
    0x1F91F: "胜利", 0x1F920: " cowboy", 0x1F921: "皇冠", 0x1F922: "累", 0x1F923: "开心",
    0x1F924: "流口水", 0x1F925: "疑惑", 0x1F926: "捂脸", 0x1F927: "打喷嚏", 0x1F928: "皱眉",
    0x1F929: "震惊", 0x1F92A: "开心", 0x1F92B: "安静", 0x1F92C: "骂人", 0x1F92D: "开心",
    0x1F92E: "吐舌", 0x1F92F: "吐舌", 0x1F937: "举手", 0x1F970: "可爱", 0x1F973: "庆祝",
    0x1F975: "冷汗", 0x1F976: "迷糊", 0x1F977: "忍者", 0x1F978: "大拇指", 0x1F97A: "拜托",
    0x1F9D1: "点头", 0x1F9D6: "祈祷", 0x1F9D7: "招手", 0x1F44B: "挥手", 0x1F44C: "好",
    0x1F44D: "点赞", 0x1F44E: "爱你", 0x1F44F: "鼓掌", 0x1F450: "开心", 0x1F440: "眼睛",
    0x1F441: "眼睛", 0x1F442: "耳朵", 0x1F443: "鼻子", 0x1F444: "嘴", 0x1F445: "舌头",
    0x1F447: "招手", 0x1F448: "左边", 0x1F449: "右边", 0x1F44A: "拳头", 0x1F595: "中指",
    0x1F596: "好的", 0x1F4AA: "加油", 0x1F4AB: "禁止", 0x1F4A4: "睡觉", 0x1F4A9: "炸弹",
    0x1F4AF: "满分", 0x1F494: "心碎", 0x1F495: "爱", 0x1F496: "爱心", 0x1F497: "爱心",
    0x1F498: "爱心", 0x1F49B: "爱心", 0x1F49C: "爱心", 0x1F49D: "爱心", 0x1F49F: "爱心",
    0x1F4A0: "钻石", 0x1F4A1: "灯泡", 0x1F4A5: "撞", 0x1F4A6: "汗", 0x1F4A7: "工具",
    0x1F4A8: "加速", 0x1F4C8: "上升", 0x1F4C9: "下降",
    0x1F4BB: "电脑", 0x1F4F1: "手机", 0x1F4F7: "相机", 0x1F525: "火", 0x1F52B: "抽烟",
    0x1F389: "派对", 0x1F388: "气球", 0x1F381: "礼物", 0x1F382: "蛋糕", 0x1F37E: "酒杯",
    0x1F377: "红酒", 0x1F37B: "啤酒", 0x2615: "咖啡", 0x1F375: "奶茶", 0x1F35E: "面包",
    0x1F354: "汉堡", 0x1F355: "披萨", 0x1F34E: "苹果", 0x1F34C: "香蕉", 0x1F34D: "西瓜",
    0x1F347: "葡萄", 0x1F353: "草莓", 0x1F352: "樱桃", 0x1F349: "橙子", 0x1F34A: "柠檬",
    0x1F95A: "鸡蛋", 0x1F95C: "坚果", 0x1F95D: "面包", 0x1F308: "彩虹", 0x1F31E: "星星",
    0x1F31F: "星星", 0x1F680: "火箭", 0x1F697: "汽车", 0x1F68B: "火车", 0x1F6B2: "自行车",
    0x1F3AF: "目标", 0x1F3C6: "奖杯", 0x1F3C0: "篮球", 0x1F3B2: "骰子", 0x1F3AE: "游戏",
    0x1F3B8: "吉他", 0x1F3A4: "麦克风", 0x1F3A7: "耳机", 0x1F57A: "跳舞",
    0x270A: "举手", 0x270B: "举手", 0x270C: "胜利", 0x270D: "拳头", 0x270F: "胜利",
    0x2714: "对", 0x2764: "红心", 0x2699: "工具", 0x26A0: "警告", 0x26A1: "灯泡",
    0x2B50: "星星", 0x2B55: "空心红心",
}

# Markers that the emoji descriptor always carries besides the real name.
DESCRIPTOR_MARKERS = ("default", "zh_cn", "zh_tw", "zh_hk", "en_us")

# The names WeChat gives to the emoji that ship with the client. A text
# message carries them in square brackets, and only these are turned into a
# spoken name so that ordinary brackets stay untouched.
EMOJI_NAMES = frozenset((
    "微笑", "撇嘴", "色", "发呆", "得意", "流泪", "害羞", "生气", "抓狂", "吐舌",
    "偷笑", "愉快", "白眼", "困惑", "累", "惊讶", "冷漠", "亲亲", "破涕为笑", "憨笑",
    "疑问", "嘘", "晕", "衰", "骷髅", "敲打", "再见", "擦汗", "抠鼻", "鼓掌",
    "坏笑", "左哼哼", "右哼哼", "哈欠", "鄙视", "委屈", "快哭了", "阴险", "可怜",
    "菜刀", "啤酒", "篮球", "太阳", "礼物", "红包", "灯笼", "蛋糕", "咖啡", "猪头",
    "玫瑰", "凋谢", "菜花", "爱心", "心碎", "拥抱", "强", "弱", "握手", "胜利",
    "抱拳", "勾引", "拳头", "OK", "ok", "拍手", "飘扬", "敬礼", "烟花", "鞭炮",
    "天啊", "大哭", "困", "飞吻", "苦涩", "庆祝", "捂脸", "恐惧", "玫瑰", "哇",
    "笑脸", "嘴唇", "咒骂", "月亮", "吃瓜", "让我看看", "嘿哈", "奋斗", "皱眉",
    "叹气", "呲牙", "卖萌", "悠闲", "汗", "佛系", "笑哭", "好的", "哎呀", "打脸",
    "咦", "呵呵", "赞", "比心", "摸摸", "抱抱", "哼", "哼哼", "嗯嗯", "呜呜",
    "么么哒", "晚安", "早安", "祝好", "加油", "666", "翻白眼",
    "旺柴", "机智", "社会社会", "裂开", "福", "合十", "耶", "Emm", "转圈", "难过",
    "爆竹", "跳跳", "發", "奸笑", "炸弹", "发怒", "脸红", "惊恐", "对勾", "调皮",
    "尴尬", "囧", "惊喜掉落", "吐", "火", "发抖", "闭嘴", "睡", "向右", "怄火",
    "失望", "打叉", "便便", "Scowl", "爱意", "号外", "鲜花", "加油干", "残花",
    "乱舞", "跳绳", "投降", "折磨", "转账", "傲慢", "送花花", "拒绝", "气泡",
    "惊喜抽奖", "财神", "红火", "团队", "哟", "哦哟", "敲敲打打", "打工人", "牛啊",
    "区域", "我酸了", "起飞", "各位", "小确幸", "捣蛋鬼", "好的", "机智", "乖巧",
    "撒花", "偷看", "疑问", "期待", "吃瓜", "拳头", "敲打", "左哼哼", "右哼哼",
))

# A face that nothing knows is still worth announcing.
UNKNOWN_EMOJI = "我发送了一个小表情"

# A message that only holds punctuation still carries a meaning. The three
# dots that people type reach us as one ellipsis sign, so it stands on its own.
PUNCTUATION_MEANING = (
    (frozenset("。.．…"), 2, "我很无语了"),
    (frozenset("…"), 1, "我很无语了"),
    (frozenset("？?"), 1, "啊？什么？"),
    (frozenset("！!"), 1, "哇！"),
)

# A few characters are used as plain signs in text, not as an emoji.
TEXT_SIGNS = frozenset("★☆✓✔✗✘✚✜✝✞✟✠✡✂✃✄✅✆✇")

# The dingbat block holds emoji of its own, such as a heart or a victory hand.
EMOJI_DINGBATS = "".join(sorted(key for key in EMOJI_NAMES_ZH
                                if len(key) == 1 and 0x2700 <= ord(key) <= 0x27BF
                                and key not in TEXT_SIGNS))

EMOJI_ANY = re.compile(
    r"\[([^\[\]\r\n]{1,12})\]|([\U0001F300-\U0001F5FF\U0001F600-\U0001F64F"
    r"\U0001F680-\U0001F6FF\U0001F900-\U0001F9FF\U0001FA70-\U0001FAFF"
    r"\U0001F1E6-\U0001F1FF\u2600-\u26FF\u2B00-\u2BFF" + re.escape(EMOJI_DINGBATS) + "])")

# A run of symbols that is not a word. The emoji keep their own place.
SYMBOL_RUN = re.compile(
    r"[^0-9A-Za-z\u4e00-\u9fff\U0001F300-\U0001F5FF\U0001F600-\U0001F64F"
    r"\U0001F680-\U0001F6FF\U0001F900-\U0001F9FF\U0001FA70-\U0001FAFF"
    r"\U0001F1E6-\U0001F1FF\u2600-\u26FF\u2B00-\u2BFF" + re.escape(EMOJI_DINGBATS) + "]+")

VARIATION_SELECTORS = "\ufe0f\ufe0e"


# A word of the word book that holds only digits is read on its own, so that a
# longer number keeps its digits.
DIGIT_WORD = re.compile(r"^[0-9]+$")

# Abbreviations that people write in messages. The voice model cannot read the
# short form, so it is spelled out before the text is spoken.
BUILTIN_ABBREVIATIONS = {
    "omg": "Oh my god",
}
ABBREVIATION = re.compile(
    r"(?<![A-Za-z0-9_])(%s)(?![A-Za-z0-9_])" % "|".join(
        sorted(BUILTIN_ABBREVIATIONS, key=len, reverse=True)),
    re.IGNORECASE)


def _index_by_first(table):
    """Group the entries of a table by their first character, longest first."""
    index = {}
    for key, value in table.items():
        index.setdefault(key[0], []).append((key, value))
    for first in index:
        index[first].sort(key=lambda item: -len(item[0]))
    return index


# The emoji that are made of several characters, such as a family or a flag.
EMOJI_SEQUENCES = _index_by_first(
    {key: name for key, name in EMOJI_NAMES_ZH.items() if len(key) > 1})
# The kaomoji that people type as text.
KAOMOJI_INDEX = _index_by_first(KAOMOJI_NAMES)

# The readings that the user defined in the application.
CUSTOM_RULES = []
LEXICON = []

# Where a symbol may be read: on its own, inside a sentence, or both.
SCOPES = ("standalone", "inside", "both")


def configure(rules=None, lexicon=None):
    """Load the readings that the user defined in the application."""
    global CUSTOM_RULES, LEXICON
    CUSTOM_RULES = []
    for item in rules or []:
        match = str(item.get("match") or "")
        say = str(item.get("say") or "")
        if not match or not say:
            continue
        scope = str(item.get("scope") or "").strip().lower()
        if scope not in SCOPES:
            # An older build only knew "anywhere", which replaced everywhere.
            scope = "both" if bool(item.get("anywhere")) else "standalone"
        CUSTOM_RULES.append((match, say, scope))
    CUSTOM_RULES.sort(key=lambda item: -len(item[0]))
    LEXICON = []
    for item in lexicon or []:
        word = str(item.get("word") or "")
        say = str(item.get("say") or "")
        if word and say:
            LEXICON.append((word, say))
    LEXICON.sort(key=lambda item: -len(item[0]))


class SnapshotBusy(Exception):
    pass


DECOMPRESSOR = zstandard.ZstdDecompressor()


def _inflate(blob):
    """Expand a zstd frame. WeChat stores long text messages compressed."""
    try:
        with DECOMPRESSOR.stream_reader(io.BytesIO(blob)) as reader:
            return reader.read(MAX_TEXT_BYTES)
    except zstandard.ZstdError:
        return None


def payload_bytes(blob):
    """Return the decompressed bytes of a stored message payload."""
    if blob is None:
        return b""
    if isinstance(blob, str):
        blob = blob.encode("utf-8", "ignore")
    if not blob:
        return b""
    if blob[:4] == ZSTD_MAGIC:
        return _inflate(blob) or b""
    return blob


def decode_content(blob):
    """Return plain text from a WeChat text payload, or an empty string."""
    blob = payload_bytes(blob)
    if not blob:
        return ""
    # WeChat wraps the visible text in a binary container.
    marker = blob.find(CONTAINER_MARKER)
    if marker >= 0:
        blob = blob[:marker]
    try:
        return blob.decode("utf-8").strip()
    except UnicodeDecodeError:
        return ""


def is_reply(local_type):
    """True when a row is a quoted reply, that is an app message of subtype 57."""
    if not isinstance(local_type, int):
        return False
    return (local_type & TYPE_BITS) == APPMSG_TYPE and \
        ((local_type >> 32) & SUBTYPE_BITS) == REPLY_SUBTYPE


def readable(value):
    """Return a plain fragment, or an empty string when it carries markup."""
    if not value:
        return ""
    text = value.strip()
    if not text or "<" in text:
        return ""
    for entity, character in ENTITIES:
        text = text.replace(entity, character)
    return text.strip()


def reply_text(blob):
    """Return the spoken text of a quoted reply."""
    return render_speech(reply_parts(blob))


def reply_parts(blob):
    """Return the new sentence and the quoted plain text separately."""
    raw = payload_bytes(blob)
    if not raw:
        return []
    start = raw.find(b"<appmsg")
    if start < 0:
        return []
    try:
        xml = raw[start:].decode("utf-8")
    except UnicodeDecodeError:
        return []
    block = APPMSG_BLOCK.search(xml)
    if not block:
        return []
    inner = block.group(1)
    head, separator, _ = inner.partition("<refermsg")
    title = ""
    found = TITLE_TAG.search(head)
    if found:
        title = readable(found.group(1))
    quoted = ""
    if separator:
        reference = REFER_BLOCK.search(inner)
        if reference:
            body = reference.group(1)
            kind = TYPE_TAG.search(body)
            # Only a quoted plain text message carries readable content.
            if kind and kind.group(1).strip() == "1":
                found = CONTENT_TAG.search(body.split(MSG_SOURCE_MARK)[0])
                if found:
                    quoted = readable(found.group(1))
    return [part for part in (title, quoted) if part]


def render_speech(parts, announcement=False):
    if announcement:
        return _finish(_apply_rules(parts[0])) if parts else ""
    return "，来自引用，".join(filter(None, map(spoken_text, parts)))


def spoken_text(text):
    """Turn the symbols of a message into the words that should be heard.

    WeChat writes its own emoji as a code in square brackets and the system
    keyboard writes emoji as plain characters. A kaomoji is a face that people
    type as text. Each of them is read as the name it stands for, and a face
    that nothing knows is announced as a small emoji. Codes that do not belong
    to an emoji stay as they are.
    """
    if not text:
        return text
    text = _apply_rules(text)
    text = _punctuation_meaning(text)
    pieces = _kaomoji_pieces(text)
    pieces = _plain_kaomoji(pieces)
    pieces = _emoji_pieces(pieces)
    return _finish(_join(_merge(pieces)))


def _apply_rules(text):
    """Replace the symbols that the user gave a reading of their own."""
    for match, say, scope in CUSTOM_RULES:
        if scope == "both":
            text = _replace_guarded(text, match, say)
        elif scope == "inside":
            text = _replace_inside(text, match, say)
        else:
            text = _replace_standalone(text, match, say)
    return text


def _replace_guarded(text, match, say):
    """Replace the match, but keep it out of a longer number.

    A number that is part of a longer number is read as one number, so a
    digit only replaces itself when it stands on its own.
    """
    target = match.strip()
    if not target:
        return text
    if DIGIT_WORD.match(target):
        pattern = r"(?<![0-9])" + re.escape(target) + r"(?![0-9])"
        return re.sub(pattern, lambda found: say, text)
    return text.replace(target, say)


def _replace_standalone(text, match, say):
    """Replace the match only where it stands on its own."""
    target = match.strip()
    if not target:
        return text
    lines = text.split("\n")
    for index, line in enumerate(lines):
        if line.strip() == target:
            lines[index] = line[: len(line) - len(line.lstrip())] + say
    return "\n".join(lines)


def _replace_inside(text, match, say):
    """Replace the match where it stands inside a line, not as the whole line."""
    target = match.strip()
    if not target:
        return text
    lines = text.split("\n")
    for index, line in enumerate(lines):
        if target in line and line.strip() != target:
            lines[index] = _replace_guarded(line, target, say)
    return "\n".join(lines)


def _punctuation_meaning(text):
    """Give a meaning to a message that only holds punctuation."""
    lines = text.split("\n")
    for index, line in enumerate(lines):
        stripped = line.strip()
        if not stripped:
            continue
        for characters, minimum, meaning in PUNCTUATION_MEANING:
            if len(stripped) >= minimum and all(char in characters for char in stripped):
                lines[index] = line[: len(line) - len(line.lstrip())] + meaning
                break
    return "\n".join(lines)


def _kaomoji_pieces(text):
    """Split the text into plain words and the kaomoji faces it holds."""
    pieces = []
    buffer = []
    index = 0
    length = len(text)
    while index < length:
        matched = False
        for face, reading in KAOMOJI_INDEX.get(text[index], ()):
            if text.startswith(face, index):
                if buffer:
                    pieces.append(("text", "".join(buffer)))
                    buffer = []
                pieces.append(("kaomoji", reading))
                index += len(face)
                matched = True
                break
        if not matched:
            buffer.append(text[index])
            index += 1
    if buffer:
        pieces.append(("text", "".join(buffer)))
    return pieces


def _plain_kaomoji(pieces):
    """Announce a face that the table of names does not know."""
    result = []
    for kind, value in pieces:
        if kind != "text":
            result.append((kind, value))
            continue
        index = 0
        for match in SYMBOL_RUN.finditer(value):
            if sum(1 for char in match.group(0) if char in KAOMOJI_MARKERS) < 2:
                continue
            if match.start() > index:
                result.append(("text", value[index:match.start()]))
            result.append(("kaomoji", "一个颜文字"))
            index = match.end()
        if index < len(value):
            result.append(("text", value[index:]))
    return result


def _emoji_pieces(pieces):
    """Turn the emoji of a message into the name they stand for."""
    result = []
    for kind, value in pieces:
        if kind != "text":
            result.append((kind, value))
            continue
        index = 0
        length = len(value)
        buffer = []
        while index < length:
            match = EMOJI_ANY.match(value, index)
            if match:
                code, symbol = match.group(1), match.group(2)
                if code is not None:
                    if code in EMOJI_NAMES:
                        if buffer:
                            result.append(("text", "".join(buffer)))
                            buffer = []
                        result.append(("emoji", "%s的表情" % code))
                    else:
                        buffer.append(match.group(0))
                    index = match.end()
                    continue
                size, name = _emoji_name(value, index)
                if size == 0:
                    buffer.append(value[index])
                    index += 1
                    continue
                if symbol in TEXT_SIGNS:
                    # A star that rates a shop is a sign, not an emoji.
                    buffer.append(symbol)
                    index += size
                    continue
                if buffer:
                    result.append(("text", "".join(buffer)))
                    buffer = []
                result.append(("emoji", _phrase(name) if name else UNKNOWN_EMOJI))
                index += size
                if index < length and value[index] in VARIATION_SELECTORS:
                    index += 1
                continue
            buffer.append(value[index])
            index += 1
        if buffer:
            result.append(("text", "".join(buffer)))
    return result


def _phrase(name):
    """Name the emoji after the thing it stands for."""
    # A name that already holds several parts reads better on its own.
    return name if "，" in name else f"{name}的表情"


def _emoji_name(text, index):
    """Return the size and the name of the emoji that starts at the index."""
    symbol = text[index]
    point = ord(symbol)
    for key, name in EMOJI_SEQUENCES.get(symbol, ()):
        if text.startswith(key, index):
            # The short name of the first character is the one that reads best.
            return len(key), EMOJI_CHARACTER_NAMES.get(point) or name
    if point in SKIN_TONE:
        # A skin tone belongs to the emoji in front of it.
        return 0, ""
    name = EMOJI_CHARACTER_NAMES.get(point)
    if name:
        return 1, name
    name = EMOJI_NAMES_ZH.get(symbol)
    if name:
        return 1, name
    return 1, ""


def _merge(pieces):
    """Join the plain words that follow each other."""
    merged = []
    for kind, value in pieces:
        if kind == "text" and merged and merged[-1][0] == "text":
            merged[-1] = ("text", merged[-1][1] + value)
        else:
            merged.append((kind, value))
    return merged


def _join(pieces):
    """Separate every face from the words around it."""
    result = []
    for position, (kind, value) in enumerate(pieces):
        if kind == "text":
            result.append(value)
            continue
        if result and not result[-1].endswith(("，", ",", " ", "\n", "\r", "。", "！", "？",
                                                "!", "?", "、", "：", ":", "；", ";")):
            result.append("，")
        result.append(value)
        following = "".join(other for other_kind, other in pieces[position + 1:] if other_kind == "text")
        # A line break already separates the words, a comma would be noise.
        if following.strip() and not following.lstrip(" \t").startswith(("\n", "\r")):
            result.append("，")
    return "".join(result)


def _finish(text):
    """Apply the word book and spell out the abbreviations."""
    for word, say in LEXICON:
        text = _replace_word(text, word, say)
    # The short form of an abbreviation is not a word the voice model can say.
    return ABBREVIATION.sub(lambda match: BUILTIN_ABBREVIATIONS[match.group(1).lower()], text)


def _replace_word(text, word, say):
    """Replace a word of the word book.

    A word that holds only digits is read on its own, so a longer number keeps
    its digits and the voice model reads it as one number.
    """
    if DIGIT_WORD.match(word):
        pattern = r"(?<![0-9])" + re.escape(word) + r"(?![0-9])"
        return re.sub(pattern, lambda match: say, text)
    return text.replace(word, say)


def emoji_message(blob):
    """Return whether a picture is a sticker and the name it carries.

    The name comes from the descriptor that WeChat stores beside the picture.
    It is empty when the sender used a picture without a name.
    """
    raw = payload_bytes(blob)
    if not raw:
        return (False, "")
    try:
        xml = raw.decode("utf-8")
    except UnicodeDecodeError:
        return (False, "")
    found = EMOJI_TAG.search(xml)
    if not found:
        return (False, "")
    attrs = dict(XML_ATTRIBUTE.findall(found.group(1)))
    sticker = attrs.get("type") == STICKER_TYPE
    desc = attrs.get("desc") or ""
    if not desc:
        return (sticker, "")
    try:
        data = base64.b64decode(desc + "=" * (-len(desc) % 4))
    except (ValueError, TypeError):
        return (sticker, "")
    for match in CHINESE_RUN.finditer(data.decode("utf-8", "ignore")):
        name = match.group().strip()
        if name and name not in DESCRIPTOR_MARKERS:
            return (sticker, name)
    for match in ASCII_RUN.finditer(data):
        name = match.group().decode("ascii", "ignore").strip()
        if len(name) >= 2 and name not in DESCRIPTOR_MARKERS:
            return (sticker, name)
    return (sticker, "")


def extract_text(blob, local_type):
    """Return the text a listener should hear for one stored message row."""
    parts, announcement = speech_parts(blob, local_type)
    return render_speech(parts, announcement)


def speech_parts(blob, local_type):
    if local_type == 1:
        return [decode_content(blob)], False
    if is_reply(local_type):
        return reply_parts(blob), False
    if local_type == EMOJI_TYPE:
        sticker, name = emoji_message(blob)
        if name:
            # A picture that carries a name is a sticker, not a written face.
            phrase = f"我发了一个{name}的表情包"
        elif sticker:
            phrase = "我发了一个表情包"
        else:
            phrase = UNKNOWN_EMOJI
        return [phrase], True
    if local_type == IMAGE_TYPE:
        # A photograph has no name to read out.
        return ["我发了一张图片"], True
    return [], False


def main_stamp(path):
    info = path.stat()
    with path.open("rb") as stream:
        header = stream.read(100)
    return info.st_ino, info.st_size, info.st_mtime_ns, header


def read_wal(path):
    try:
        with path.open("rb") as stream:
            data = stream.read(MAX_WAL_BYTES + 1)
    except FileNotFoundError:
        return b""
    if len(data) > MAX_WAL_BYTES:
        raise ValueError("WAL too large")
    return data


class EncryptedSnapshot:
    def __init__(self, original, cache, key):
        self.original = original
        self.wal = original.with_name(original.name + "-wal")
        self.copy = cache / original.with_suffix(".sqlite").name
        self.copy_wal = self.copy.with_name(self.copy.name + "-wal")
        self.key = key
        self.identity = original.stat().st_ino
        self.base_stamp = None
        self.wal_hash = None

    def refresh(self):
        for _ in range(3):
            before = main_stamp(self.original)
            if before[0] != self.identity:
                raise IdentityChanged()
            wal = read_wal(self.wal)
            # WAL salt changes on a reset. Refresh the base even if Windows has
            # not yet reported a changed mtime for the open database handle.
            base_stamp = before + (wal[:24],)
            wal_hash = hashlib.sha256(wal).digest()
            if base_stamp == self.base_stamp and wal_hash == self.wal_hash:
                return
            main_next = self.copy.with_suffix(".sqlite.next")
            copy_base = self.base_stamp != base_stamp
            if copy_base:
                shutil.copyfile(self.original, main_next)
            if before != main_stamp(self.original) or wal != read_wal(self.wal):
                continue
            if copy_base:
                main_next.replace(self.copy)
            self.copy.with_name(self.copy.name + "-shm").unlink(missing_ok=True)
            if wal:
                wal_next = self.copy.with_name(self.copy.name + "-wal.next")
                wal_next.write_bytes(wal)
                wal_next.replace(self.copy_wal)
            else:
                self.copy_wal.unlink(missing_ok=True)
            self.base_stamp, self.wal_hash = base_stamp, wal_hash
            return
        raise SnapshotBusy()

    def connect(self):
        self.refresh()
        conn = sql.connect(self.copy.as_uri() + "?mode=ro", uri=True, timeout=0.05)
        try:
            conn.execute("PRAGMA key = \"x'%s'\"" % self.key.hex())
            if len(self.key) == 48:
                conn.execute("PRAGMA cipher_plaintext_header_size=16")
            conn.execute("PRAGMA query_only=ON")
            conn.execute("PRAGMA trusted_schema=OFF")
            conn.execute("PRAGMA temp_store=MEMORY")
            return conn
        except BaseException:
            conn.close()
            raise


class WeChatStore:
    """Read-only access to one account's contact list and message shards."""

    def __init__(self, cache, root, account, pids, peer=None, include_speech=False,
                 version=None):
        # `version` is the client file version validated by WeChatSession.Bind
        # (4.1.11 <= v <= 4.2.0.52); it is handed to the key reader, which refuses
        # to scan otherwise. Unsupported/unparsable versions raise there before
        # any process handle or database file is touched.
        if not account or Path(account).name != account or account in (".", ".."):
            raise IdentityChanged()
        if not pids:
            raise IdentityChanged()
        self.account = account
        self.include_speech = include_speech
        self.storage = (Path(root) / account / "db_storage").resolve(strict=True)
        self.message_dir = self.storage / "message"
        self.contact_path = self.storage / "contact" / "contact.db"
        if not self.message_dir.is_dir() or not self.contact_path.is_file():
            raise IdentityChanged()
        self.shards = self.list_shards()
        if not 1 <= len(self.shards) <= 20:
            raise IdentityChanged()
        # One memory scan authenticates the contact database and every shard.
        # `version` drives the strict 4.1.11 <= v <= 4.2.0.52 profile dispatch;
        # an unsupported version raises before any scan starts.
        key_reader = WeChatDB(version=version)
        targets = [(self.contact_path.name, str(self.contact_path), 0)]
        targets += [(name, str(self.message_dir / name), 0) for name in self.shards]
        key_reader._db_files = targets
        keys, seen = {}, set()
        for pid in pids:
            keys.update(key_reader._keys_from_candidates(key_reader._collect_key_candidates(pid, seen)))
            key_reader._db_files = [entry for entry in key_reader._db_files if entry[0] not in keys]
            if not key_reader._db_files:
                break
        if self.contact_path.name not in keys or set(keys) != {name for name, _, _ in targets}:
            raise IdentityChanged()
        self.contact = EncryptedSnapshot(self.contact_path, cache, keys[self.contact_path.name])
        self.snapshots = {name: EncryptedSnapshot(self.message_dir / name, cache, keys[name])
                          for name in self.shards}
        self.peer = peer
        self.peers = ()
        self.tables = {}
        self.table = None
        self.baseline = {}
        self.outstanding = None
        self.reply_cursor = {}
        self.replies = {}
        if peer is not None:
            self.open_conversation(peer)

    def list_shards(self):
        return sorted(path.name for path in self.message_dir.iterdir()
                      if path.is_file() and SHARD.fullmatch(path.name))

    def has_table(self, conn, table):
        return conn.execute("SELECT 1 FROM sqlite_schema WHERE type='table' AND name=?",
                            (table,)).fetchone() is not None

    def open_conversation(self, peer):
        self.open_conversations([peer])
        return {name: bound for (conversation, name), bound in self.baseline.items() if conversation == peer}

    def open_conversations(self, peers):
        """Bind an immutable explicit whitelist before returning the startup baseline.

        One copy/connection per shard serves the whole batch. No other conversation's
        body is read. Cursors belong to a (conversation, shard), since local ids do
        not identify messages outside their own table.
        """
        if (not isinstance(peers, (list, tuple)) or not 1 <= len(peers) <= 16
                or any(not isinstance(peer, str) or not peer or len(peer) > 256
                       or peer.endswith("@chatroom") or peer == "filehelper" for peer in peers)
                or len(set(peers)) != len(peers)):
            raise IdentityChanged()
        tables = {peer: "Msg_" + hashlib.md5(peer.encode("utf-8"), usedforsecurity=False).hexdigest()
                  for peer in peers}
        baseline = {}
        for name, snapshot in self.snapshots.items():
            with closing(snapshot.connect()) as conn:
                for peer, table in tables.items():
                    if self.has_table(conn, table):
                        baseline[(peer, name)] = conn.execute(
                            f'SELECT COALESCE(MAX(local_id), 0) FROM "{table}"').fetchone()[0]
        if any(not any(conversation == peer for conversation, _ in baseline) for peer in peers):
            raise IdentityChanged()
        self.peers = tuple(peers)
        self.peer = peers[0]
        self.tables = tables
        self.table = tables[self.peer]
        self.baseline = baseline
        self.outstanding = None
        # The user's own messages since Start, tracked apart from the incoming cursor.
        self.reply_cursor = dict(baseline)
        self.replies = {}
        return self.baseline

    def search_contacts(self, keyword, limit=60):
        """Build one in-memory metadata index after explicit browsing; never cache chat bodies."""
        now = time.monotonic()
        index = getattr(self, '_contact_index', None)
        file_stamp = []
        for path in (self.contact.original, self.contact.wal):
            try:
                info = path.stat()
                file_stamp.append((info.st_ino, info.st_size, info.st_mtime_ns))
            except FileNotFoundError:
                file_stamp.append(None)
        if index is not None and file_stamp == self._contact_file_stamp and now - self._contact_checked < 1:
            return index.search(keyword, limit)
        with closing(self.contact.connect()) as conn:
            if not self.has_table(conn, "contact"):
                return []
            stamp = (self.contact.base_stamp, self.contact.wal_hash)
            if index is not None and stamp == self._contact_index_stamp:
                self._contact_checked, self._contact_file_stamp = now, file_stamp
                return index.search(keyword, limit)
            # Versions may not expose a head-image URL. Only project a known column
            # when it is really present, never guess a disk cache or fetch a URL.
            columns = {row[1] for row in conn.execute('PRAGMA table_info("contact")')}
            avatar_column = next((column for column in (
                "small_head_url", "small_head_img_url", "head_img_url", "big_head_url")
                if column in columns), None)
            avatar = '"' + avatar_column + '"' if avatar_column else "NULL"
            modified_column = next((column for column in ('update_time', 'modify_time', 'last_modify_time') if column in columns), None)
            modified = '"' + modified_column + '"' if modified_column else '0'
            rows = conn.execute(f"SELECT username, nick_name, remark, alias, {avatar}, {modified} FROM contact "
                                "WHERE username <> '' AND username NOT LIKE '%@chatroom' AND username <> 'filehelper'").fetchall()
        self._contact_index = ContactIndex(rows)
        self._contact_index_stamp, self._contact_checked = stamp, now
        self._contact_file_stamp = file_stamp
        return self._contact_index.search(keyword, limit)

    def selected_contact_info(self, peers):
        """Optional portraits for the already authorized whitelist, without browsing other contacts."""
        if not peers:
            return []
        with closing(self.contact.connect()) as conn:
            columns = {row[1] for row in conn.execute('PRAGMA table_info("contact")')}
            if not {'username', 'nick_name', 'remark', 'alias'}.issubset(columns):
                return []
            avatar = next((column for column in ('small_head_url', 'small_head_img_url', 'head_img_url', 'big_head_url') if column in columns), None)
            projection = '"' + avatar + '"' if avatar else 'NULL'
            placeholders = ','.join('?' for _ in peers)
            rows = conn.execute(f'SELECT username, nick_name, remark, alias, {projection}, 0 FROM contact '
                                f'WHERE username IN ({placeholders})', tuple(peers)).fetchall()
        return ContactIndex(rows).search('', len(peers))

    def last_incoming_text(self, peer):
        """Return the newest incoming readable message of a conversation."""
        table = "Msg_" + hashlib.md5(peer.encode("utf-8"), usedforsecurity=False).hexdigest()
        newest = None
        for name, snapshot in self.snapshots.items():
            with closing(snapshot.connect()) as conn:
                if not self.has_table(conn, table):
                    continue
                # sort_seq is a shared timestamp; local_id only counts inside one shard.
                rows = conn.execute(f"""
                    SELECT msg.sort_seq, msg.message_content, msg.local_type
                    FROM "{table}" AS msg
                    WHERE ({TEXT_ROWS})
                      AND msg.real_sender_id IN (SELECT rowid FROM Name2Id WHERE user_name = ?)
                    ORDER BY msg.sort_seq DESC LIMIT 20
                """, (peer,)).fetchall()
            for sequence, body, local_type in rows:
                text = extract_text(body, local_type)
                if text and (newest is None or sequence > newest[0]):
                    newest = (sequence, text)
        return newest[1] if newest else ""

    def query(self):
        if self.outstanding is not None:
            raise ValueError("unacknowledged batch")
        if self.list_shards() != self.shards:
            raise IdentityChanged()
        started = time.perf_counter_ns()
        cursor = self.baseline.copy()
        streams = []
        for name, snapshot in self.snapshots.items():
            with closing(snapshot.connect()) as conn:
                for peer, table in self.tables.items():
                    binding = (peer, name)
                    if not self.has_table(conn, table):
                        if binding in cursor:
                            raise IdentityChanged()
                        continue
                    bound = cursor.get(binding, 0)
                    max_id = conn.execute(f'SELECT COALESCE(MAX(local_id), 0) FROM "{table}"').fetchone()[0]
                    if max_id < bound:
                        raise IdentityChanged()
                    records = conn.execute(f"""
                        SELECT msg.local_id, msg.sort_seq, msg.message_content, msg.local_type
                        FROM "{table}" AS msg
                        WHERE msg.local_id > ? AND ({TEXT_ROWS})
                          AND msg.real_sender_id IN (SELECT rowid FROM Name2Id WHERE user_name = ?)
                        ORDER BY msg.local_id LIMIT ?
                    """, (bound, peer, BATCH_PER_SHARD)).fetchall()
                    if records:
                        streams.append((peer, name, records))
                    self.note_replies(conn, table, peer, binding)
        # Merge stream heads and only advance consumed prefixes. In particular, a
        # sort_seq decrease cannot cause a lower local_id to disappear at a page
        # boundary. Future commits cannot be given a guaranteed global wall-clock
        # order; currently available heads use a deterministic tie breaker.
        heads = []
        for index, (peer, name, records) in enumerate(streams):
            local_id, sequence, _, _ = records[0]
            heapq.heappush(heads, (sequence or 0, peer, name, local_id, index, 0))
        rows = []
        while heads and len(rows) < BATCH_PER_SHARD:
            _, peer, name, local_id, index, position = heapq.heappop(heads)
            records = streams[index][2]
            _, sequence, body, local_type = records[position]
            parts, announcement = speech_parts(body, local_type)
            text = render_speech(parts, announcement)
            row = dict(key=f"{peer}:{name}:{local_id}", account=self.account, conversation=peer,
                       direction="incoming", isText=bool(text), sequence=sequence or 0,
                       text=text[:TEXT_LIMIT], fixtureDetectionMs=None)
            if (self.include_speech and text and len(text) < TEXT_LIMIT
                    and sum(map(len, parts)) <= 4096):
                row["speech"] = dict(parts=parts, announcement=announcement)
            rows.append(row)
            cursor[(peer, name)] = local_id
            position += 1
            if position < len(records):
                next_id, next_sequence, _, _ = records[position]
                heapq.heappush(heads, (next_sequence or 0, peer, name, next_id, index, position))
        if rows:
            self.outstanding = (cursor, [row["key"] for row in rows])
        return rows, (time.perf_counter_ns() - started) / 1e6

    def note_replies(self, conn, table, peer, binding):
        """Remember the newest message the user sent in this chat, from any device.

        Only metadata is read: no body. Rows from the peer and system notices
        (type 10000 / 10002, which either side may carry) do not count as a reply.
        sort_seq is a millisecond timestamp shared by both directions, so the
        parent compares it with the incoming messages it is still waiting on.
        """
        newest_id, newest_sequence = conn.execute(f"""
            SELECT MAX(msg.local_id), MAX(msg.sort_seq) FROM "{table}" AS msg
            WHERE msg.local_id > ? AND msg.real_sender_id <> 0
              AND (msg.local_type & 4294967295) NOT IN ({SYSTEM_TYPES})
              AND msg.real_sender_id NOT IN (SELECT rowid FROM Name2Id WHERE user_name = ?)
        """, (self.reply_cursor.get(binding, 0), peer)).fetchone()
        if newest_id is None:
            return
        self.reply_cursor[binding] = newest_id
        self.replies[peer] = max(self.replies.get(peer, 0), newest_sequence or 0)

    def take_replies(self):
        """Hand over and forget the replies found since the previous call."""
        replies = [dict(account=self.account, conversation=peer, sequence=sequence)
                   for peer, sequence in sorted(self.replies.items())]
        self.replies = {}
        return replies

    def ack(self, keys):
        if self.outstanding is None or keys != self.outstanding[1]:
            raise ValueError("invalid acknowledgment")
        self.baseline, self.outstanding = self.outstanding[0], None

    def close(self):
        self.snapshots.clear()
        self.contact = None
        self._contact_index = None
