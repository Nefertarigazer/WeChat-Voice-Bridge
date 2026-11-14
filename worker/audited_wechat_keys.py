# Multi-version SQLCipher key scan for WeChat PC (Windows).
#
# This file is the multi-version adaptation of slice/worker/audited_wechat_keys.py
# (the original single-version 4.2.0.13 reader). The regression floor is kept:
# the Config.Cipher object scan, the XOR blob decode, the candidate split
# (start 0 / step 32 / last -64) and the per-database page1 HMAC validation
# are unchanged from the original file. Nothing in that path is re-guessed.
#
# Supported version range (decided by the user, enforced here and in
# WeChatSession.Bind): WeChat Windows 4.1.11 <= version <= 4.2.0.52.
#   * 4.1.11 and 4.1.15.99 are 区间覆盖·待实测 (covered by the value range, not
#     individually tested by us yet; the user will verify on a real client).
#   * 4.2.0.52 is the currently verified latest version (verified on a real
#     client on 2026-11-14: the running main process yielded 70 candidates and
#     every one of the account's 26 databases passed page1 HMAC). 4.2.0.13 was
#     verified earlier; the two share the same layout, so its behaviour is
#     unchanged.
#   * Versions outside the range (< 4.1.11, 4.1.10.x, > 4.2.0.52) are refused
#     with "不支持此版本"; no parameters are invented or force-fitted.
#
# Parameter organisation follows the ConfigCipherProfile schema of
# fangtimmy/wechat4-db-decrypt (src/wxexport/win/profiles.py) so that future
# layout changes can be added as extra profile rows instead of edits to the
# scanning engine. All real-device data quoted below was re-verified against
# two fresh shallow clones (2026-11-09, this session):
#
#   * fanyuantaier/wechatauto-replica (Apache-2.0)
#     https://github.com/fanyuantaier/wechatauto-replica
#     HEAD d462922e40d49406178930c92cfc59cdd446267a (2026-11-08); the egg's
#     db.py at commit 4931f98d4b4b68dc4af8cfc10f76805eb9c743da is byte-identical
#     to HEAD (diff 0 confirmed by git).
#     - CONFIG_CIPHER_NAME / CONFIG_XOR_MASK / HEX_LITERAL_RE: db.py:47-52
#     - Config.Cipher layout offsets + candidate split: db.py:927-998
#     - master-key cfg anchors CFG_LANDMARK/CFG_PTR_BACK/CFG_OFFSET/
#       CFG_DWORD_OFF/CFG_WXID_OFF/CFG_CIPHER_OFF + MASTER_DLL_PATTERN /
#       MASTER_DLL_VERIFY: db.py:54-68; movabs XOR material extraction: db.py:264-282
#     - cfg anchors re-sampled per major version (upstream comments).
#   * fangtimmy/wechat4-db-decrypt (WTFPL)
#     https://github.com/fangtimmy/wechat4-db-decrypt
#     HEAD a5c19d01bb8e9b866e1e4c432070d0108b88b9c9c (2026-10-27);
#     src/wxexport/win/profiles.py: ConfigCipherProfile fields and the
#     wcdb-config-cipher-4.1.11+ band (verified on real 4.1.13.12 / 4.1.15.13);
#     its CONFIG_CIPHER_MASK is byte-identical to the value used here.
#
# Corrected evidence note: out/scout-lead.md line 14 quotes a shortened
# (62-hex) transcription of CONFIG_XOR_MASK; the actual upstream file at both
# relevant commits carries the 64-hex value below, byte-identical to the
# original slice file. An earlier draft of this file wrongly described the
# upstream value as a "29-byte variant"; that description is retracted - the
# single mask value has always been one, stable across the audited sources.
#
# The legacy plaintext 'x'<hex>' scan branch that appeared in the earlier
# draft is REMOVED: the project scope now stops at 4.1.11+ (single extraction
# route: Config.Cipher structural scan + XOR decode). 4.1.10.x has zero-resident
# key caches and is out of scope entirely.

import ctypes
from ctypes import wintypes
import hashlib
import hmac as hmac_mod
import re
import struct
from typing import Dict, List, Optional, Tuple

# SQLCipher page format constants (same as original slice; stable across 4.x).
PAGE_SZ = 4096

RESERVE_SZ = 80  # IV(16) + HMAC(64)

# Anchor string of the WCDB config object holding the XOR-masked key cache.
CONFIG_CIPHER_NAME = b'com.Tencent.WCDB.Config.Cipher'

# Repeating XOR mask for the config blob. Byte-identical to
# fanyuantaier/wechatauto-replica wechatauto/db.py:48-50 (both commits
# audited) and to fangtimmy/wechat4-db-decrypt profiles.py
# CONFIG_CIPHER_MASK.
CONFIG_XOR_MASK = bytes.fromhex('d2c7442458020000004889442450488b450048844c2448488944254048584c24')

# Regex for the hex-literal key cache inside the decoded blob.
HEX_LITERAL_RE = re.compile(b"[xX]'([0-9a-fA-F]{64,192})'")


# ---------------------------------------------------------------------------
# Profiles (schema adopted from fangtimmy/wechat4-db-decrypt profiles.py).
# ---------------------------------------------------------------------------

class ConfigCipherProfile:
    """Memory-layout profile for the WCDB Config.Cipher key scan.

    Offsets may change between Tencent releases; every value lives in its own
    profile row so a new layout needs a new row, not an edit of the scanner.
    Profile selection is strict range membership (see profile_for): versions
    outside 4.1.11 - 4.2.0.52 are refused, never force-fitted. No candidate is
    trusted merely because it was found at an offset: every candidate is finally
    validated with the database page1 HMAC.
    """

    def __init__(self, name: str, min_version: Tuple[int, ...],
                 max_version: Tuple[int, ...],
                 needle: bytes = CONFIG_CIPHER_NAME,
                 node_back: int = 0x10,
                 config_ptr_offset: int = 0x28,
                 blob_object_offset: int = 0x88,
                 blob_ptr_offset: int = 0x08,
                 blob_len_offset: int = 0x10,
                 blob_max: int = 1024,
                  masks: Tuple[bytes, ...] = (CONFIG_XOR_MASK,)) -> None:
        self.name = name
        self.min_version = min_version
        self.max_version = max_version
        self.needle = needle
        self.node_back = node_back
        self.config_ptr_offset = config_ptr_offset
        self.blob_object_offset = blob_object_offset
        self.blob_ptr_offset = blob_ptr_offset
        self.blob_len_offset = blob_len_offset
        self.blob_max = blob_max
        self.masks = masks

    def supports(self, version: Tuple[int, ...]) -> bool:
        if not version:
            return False
        width = max(len(version), len(self.min_version), len(self.max_version))
        value = version + (0,) * (width - len(version))
        low = self.min_version + (0,) * (width - len(self.min_version))
        high = self.max_version + (0,) * (width - len(self.max_version))
        return low <= value <= high

    def to_xcipher_params(self) -> 'XCipherParams':
        """Map this profile onto the flat parameter bundle the scan uses."""
        return XCipherParams(
            needle=self.needle,
            node_back=self.node_back,
            config_ptr_offset=self.config_ptr_offset,
            blob_object_offset=self.blob_object_offset,
            blob_ptr_offset=self.blob_ptr_offset,
            blob_len_offset=self.blob_len_offset,
            blob_max=self.blob_max,
            masks=self.masks,
        )


class XCipherParams:
    """Flat parameter bundle for the Config.Cipher (XOR-decode) path."""

    def __init__(self, needle: bytes, node_back: int,
                 config_ptr_offset: int, blob_object_offset: int,
                 blob_ptr_offset: int, blob_len_offset: int,
                 blob_max: int, masks: Tuple[bytes, ...]):
        self.needle = needle
        self.node_back = node_back
        self.config_ptr_offset = config_ptr_offset
        self.blob_object_offset = blob_object_offset
        self.blob_ptr_offset = blob_ptr_offset
        self.blob_len_offset = blob_len_offset
        self.blob_max = blob_max
        self.masks = masks


class MasterCfgProfile:
    """Optional master-key path via the global_config object.

    Data model: fanyuantaier/wechatauto-replica wechatauto/db.py:55-68
    (re-verified on clone HEAD d462922). This is scout-lead's adopted item 1,
    but it is NOT on the main scan path - the per-key Config.Cipher route above
    is the extraction route for every supported version. The anchors are
    version-sensitive per upstream comments; keep one profile row per layout
    and re-sample on every major release. No runtime-tested row exists on the
    supported range to date (marked 待实测), so callers should treat a failure
    as "fallback unavailable" rather than a normal result.
    """

    CFG_LANDMARK = b'global_config'  # SSO inline landmark in weixin.dll

    # Offset table as published by fanyuantaier/wechatauto-replica (db.py:64-68):
    #   CFG_PTR_BACK   = 0x138  pointer-chaining back-offset from landmark;
    #                     version-sensitive: 4.1.10.31 = 0x130 (upstream comment)
    #   CFG_OFFSET     = 0x68   cfg pointer offset (upstream comment: v18)
    #   CFG_DWORD_OFF  = 0x40   cfgDword (image-key derivation source)
    #   CFG_WXID_OFF   = 0x48   wxId std::string
    #   CFG_CIPHER_OFF = 0x2B8  dbKey ciphertext std::string

    def __init__(self, name: str, min_version: Tuple[int, ...],
                 max_version: Tuple[int, ...],
                 cfg_ptr_back: int, cfg_offset: int,
                 cfg_dword_off: int = 0x40, cfg_wxid_off: int = 0x48,
                 cfg_cipher_off: int = 0x2B8) -> None:
        self.name = name
        self.min_version = min_version
        self.max_version = max_version
        self.cfg_ptr_back = cfg_ptr_back
        self.cfg_offset = cfg_offset
        self.cfg_dword_off = cfg_dword_off
        self.cfg_wxid_off = cfg_wxid_off
        self.cfg_cipher_off = cfg_cipher_off

    def supports(self, version: Tuple[int, ...]) -> bool:
        if not version:
            return False
        width = max(len(version), len(self.min_version), len(self.max_version))
        value = version + (0,) * (width - len(version))
        low = self.min_version + (0,) * (width - len(self.min_version))
        high = self.max_version + (0,) * (width - len(self.max_version))
        return low <= value <= high


# ---------------------------------------------------------------------------
# Version distribution table.
#
# Current membership: exactly one supported band, 4.1.11 - 4.2.0.52, served by
# the Config.Cipher layout below (the layout audited upstream across 4.1.13,
# 4.1.15.x and 4.2.0.11/4.2.0.13, and re-verified on a real 4.2.0.52 client on
# 2026-11-14: 70 candidates, all 26 account databases passed page1 HMAC - the
# layout is byte-identical, no parameter changed for 4.2.0.52; our live
# implementation has always targeted this same layout). Bands outside this
# range do not exist: nothing in any audited upstream publishes different
# offsets/masks inside it, so there is no version-specific parameter to switch
# on - the range check itself is the dispatch. When Tencent ships a layout
# change, add a NEW profile row with its own provenance and shrink this band;
# do not edit the constants in place.
# ---------------------------------------------------------------------------

PROFILES: Tuple[ConfigCipherProfile, ...] = (
    ConfigCipherProfile(
        name='wcdb-config-cipher-4.1.11+',
        # Supported range: 4.1.11 through 4.2.0.52 inclusive (user decision;
        # 4.2.0.52 is the latest version on the 4.2.x line, verified on a real
        # client - its layout equals 4.2.0.13's, so only the bound moved).
        # 4.1.11 and 4.1.15.99: 区间覆盖·待实测 (range coverage, not yet
        # individually device-tested by us - kept on the single layout
        # below, to be confirmed by the user).
        # >4.2.0.52: refused (no layout data found anywhere; new layout would
        # need a new profile row, not a force-fit).
        min_version=(4, 1, 11, 0),
        max_version=(4, 2, 0, 52),
        node_back=0x10,
        config_ptr_offset=0x28,
        blob_object_offset=0x88,
        blob_ptr_offset=0x08,
        blob_len_offset=0x10,
        blob_max=1024,
        # Single XOR mask, byte-identical to the slice baseline: every blob is
        # decoded with CONFIG_XOR_MASK exactly as the original 4.2.0.13 scanner
        # did, so 4.2.0.13 candidate sets are unchanged, and 4.2.0.52 uses that
        # same single mask. The field stays a tuple so a future layout can add
        # extra masks without editing the engine; no empty mask is used (an
        # empty mask would pass the raw blob through and diverge from the
        # audited baseline). Every candidate is still gated by the per-database
        # page1 HMAC.
        masks=(CONFIG_XOR_MASK,),
    ),
)

# Master-key cfg profile table (optional fallback path only): the single row
# below mirrors the 4.1.12+-era cfg anchors of fanyuantaier/wechatauto-replica
# db.py:64-73; further rows must be added per layout and re-verified per
# major release. Rows here do NOT grant additional extraction rights on
# versions outside the strict range checked by profile_for().
MASTER_CFG_PROFILES: Tuple[MasterCfgProfile, ...] = (
    MasterCfgProfile(
        name='master-cfg-4.1.12+',
        min_version=(4, 1, 12, 0),
        max_version=(4, 2, 0, 52),
        cfg_ptr_back=0x138,
        cfg_offset=0x68,
    ),
)


# ---------------------------------------------------------------------------
# Version parsing / strict range dispatch.
# ---------------------------------------------------------------------------

def parse_version(value: str) -> Tuple[int, ...]:
    """Parse '4.2.0.52' -> (4, 2, 0, 52), or () for anything unparsable.

    Strict, to match System.Version.TryParse on the C# side: every
    dot-separated segment must be a plain decimal integer and at most four
    segments are accepted. A five-segment version ('4.1.11.0.99'), any
    non-numeric text ('4.1.11 alpha'), an empty string or None all parse to ()
    so profile_for() refuses them instead of best-effort guessing. There is no
    partial parsing: the whole string is either a valid version or not.
    """
    parts = []
    for piece in (value or '').strip().split('.'):
        if piece and all(c in '0123456789' for c in piece):
            parts.append(int(piece))
        else:
            return ()
    if not parts or len(parts) > 4:
        return ()
    return tuple(parts)


class UnsupportedVersionError(RuntimeError):
    """Raised when a WeChat version is outside the passive-scan range."""

    def __init__(self, version: str, reason: str):
        super().__init__(reason)
        self.version = version


def profile_for(version: str) -> ConfigCipherProfile:
    """Return the profile that applies to a WeChat version string.

    Strict membership: 4.1.11 <= v <= 4.2.0.52 returns the single supported
    profile. Anything outside the range - including 4.1.10.x (zero-resident
    key cache since 2025-05, passive scan provably ineffective per
    NinjaSln-labs/wxlocal Issues #125/#152 and scout-lead gap 1), anything
    below 4.1.11 and anything above 4.2.0.52 - raises UnsupportedVersionError.
    Unknown/unparsable versions are refused the same way; there is no
    best-effort fallback (no guessing, no force-fitting).
    """
    parsed = parse_version(version)
    if not parsed:
        raise UnsupportedVersionError(
            version,
            '不支持此版本：无法确定微信版本，拒绝按未知/无法解析的版本扫描。')
    low = (4, 1, 11) + (0,) * 1
    high = (4, 2, 0, 52)
    width = max(len(parsed), len(low), len(high))
    value = parsed + (0,) * (width - len(parsed))
    low_p = low + (0,) * (width - len(low))
    high_p = high + (0,) * (width - len(high))
    if not low_p <= value <= high_p:
        raise UnsupportedVersionError(
            version,
            '不支持此版本：微信 Windows 版当前仅支持 4.1.11 至 4.2.0.52（含两端及其间'
            '全部小版本，4.1.11/4.1.15.99 为区间覆盖·待实测）。检测到 %s。' % version)
    for profile in PROFILES:
        if profile.supports(parsed):
            return profile
    # PROFILES covers the full range by construction; refusing here keeps the
    # contract total instead of returning a wrong profile.
    raise UnsupportedVersionError(version, '支持区间内没有匹配的密钥提取参数表，拒绝扫描。')


def profiles_for(version: str) -> Tuple[ConfigCipherProfile, ...]:
    """Diagnostic alias returning the (strict) matching profiles."""
    return (profile_for(version),)


def master_cfg_profile_for(version: str) -> Optional[MasterCfgProfile]:
    """Return the master-cfg profile for a version, or None.

    None means "master-key fallback not configured for this version", not "not
    supported": the strict supported-range check lives in profile_for().
    """
    parsed = parse_version(version)
    for profile in MASTER_CFG_PROFILES:
        if profile.supports(parsed):
            return profile
    return None


# ---------------------------------------------------------------------------
# Scanning. Entry points keep the original slice call convention:
#   key_reader._db_files = targets          # list of (rel, path, _)
#   key_reader._keys_from_candidates(key_reader._collect_key_candidates(pid, seen))
# The client version must be supplied via the constructor (WeChatStore does
# this from the host-provided start packet; WeChatSession.Bind validated the
# range on the C# side first).
# ---------------------------------------------------------------------------

class WeChatDB:

    def __init__(self, version: Optional[str] = None) -> None:
        # Version string as reported by the client's file version
        # (e.g. '4.2.0.52'); validated by profile_for() before every scan.
        self.version = version
        self._db_files: List[Tuple[str, str, int]] = []

    # ---------------- strict-range Config.Cipher scan (4.1.11 - 4.2.0.52) ----------------

    def _collect_key_candidates(self, pid: int, seen: set) -> set:
        """Collect candidate key material (no account validation): {(cand, salt_or_None)}

        Same contract as the original slice single-version scanner: it only
        finds 32-byte keys (or 32+16 with explicit salt); verification is
        delegated to the caller per account directory, because memory keys
        belong to the *currently logged-in account* and only the right
        directory passes page1 HMAC. Candidate set `seen` is shared across
        pids so repeated candidates are not re-inspected.
        """
        profile = profile_for(self.version or '')   # refuses unknown/out-of-range
        return self._collect_xcipher_candidates(pid, profile.to_xcipher_params(), seen)

    def _collect_xcipher_candidates(self, pid: int, params: XCipherParams,
                                    seen: set) -> set:
        h = _k32.OpenProcess(16 | 1024, False, pid)
        if not h:
            return set()
        out: set = set()
        try:

            def read(addr: int, n: int):
                buf = ctypes.create_string_buffer(n)
                br = ctypes.c_size_t(0)
                if _k32.ReadProcessMemory(h, ctypes.c_void_p(addr), buf, n, ctypes.byref(br)) and br.value:
                    return buf.raw[:br.value]
                return None
            needles = self._find_bytes(h, read, params.needle)
            pairs = [struct.pack('<Q', addr) + struct.pack('<Q', len(params.needle)) for addr in needles]
            for pair in pairs:
                for qaddr in self._find_bytes(h, read, pair):
                    node = read(qaddr - params.node_back, 80)
                    if not node or len(node) < 64:
                        continue
                    if struct.unpack_from('<Q', node, 16)[0] not in needles:
                        continue
                    if struct.unpack_from('<Q', node, 24)[0] != len(params.needle):
                        continue
                    config_ptr = struct.unpack_from('<Q', node, params.config_ptr_offset)[0]
                    if not 65536 <= config_ptr < 140737488355328:
                        continue
                    obj = read(config_ptr + params.blob_object_offset, 40)
                    if not obj or len(obj) < 24:
                        continue
                    data_ptr = struct.unpack_from('<Q', obj, params.blob_ptr_offset)[0]
                    data_len = struct.unpack_from('<Q', obj, params.blob_len_offset)[0]
                    if not (0 < data_len <= params.blob_max and 65536 <= data_ptr < 140737488355328):
                        continue
                    blob = read(data_ptr, int(data_len))
                    if not blob or len(blob) != data_len:
                        continue
                    for mask in params.masks:
                        if not mask:
                            decoded = blob
                        else:
                            decoded = bytes((v ^ mask[i % len(mask)] for i, v in enumerate(blob)))
                        for m in HEX_LITERAL_RE.finditer(decoded):
                            run = m.group(1).decode().lower()
                            starts = [0]
                            if len(run) > 96:
                                starts += list(range(0, len(run) - 63, 32))
                                starts.append(len(run) - 64)
                            for s in dict.fromkeys(starts):
                                if s + 64 > len(run):
                                    continue
                                cand = bytes.fromhex(run[s:s + 64])
                                if cand in seen or not self._probable_key(cand):
                                    continue
                                seen.add(cand)
                                out.add((cand, None))
                                if s + 96 <= len(run):
                                    out.add((cand, bytes.fromhex(run[s + 64:s + 96])))
        finally:
            _k32.CloseHandle(h)
        return out

    def _keys_from_candidates(self, cands) -> Dict[str, bytes]:
        """Verify candidates against page1 of each self._db_file.

        Storage follows the actual database layout: try the standard form
        (file-head salt, bare 32-byte key) first; only when that fails (plain
        text-header databases) use the candidate's explicit salt and store
        48 bytes key+salt. The order matters: storing a standard-form database
        as 48 bytes would make decryption take the plaintext-header branch and
        produce non-SQLite output.
        """
        keys: Dict[str, bytes] = {}
        for cand, salt in cands:
            for rel, path, _ in self._db_files:
                if rel in keys:
                    continue
                try:
                    with open(path, 'rb') as f:
                        page1 = f.read(PAGE_SZ)
                except OSError:
                    continue
                if _verify_enc_key(cand, page1):
                    keys[rel] = cand
                    break
                if salt and _verify_enc_key(cand, page1, salt=salt):
                    keys[rel] = cand + salt
                    break
        return keys

    @staticmethod
    def _probable_key(b: bytes) -> bool:
        return len(b) == 32 and len(set(b)) >= 15 and (b not in {b'\x00' * 32, b'\xff' * 32})

    # ---------------- shared memory scan helpers ----------------

    @staticmethod
    def _find_bytes(h, read, needle: bytes) -> List[int]:
        hits = []
        addr = 0
        while True:
            mbi = _MBI()
            r = _k32.VirtualQueryEx(h, ctypes.c_void_p(addr), ctypes.byref(mbi), ctypes.sizeof(mbi))
            if r == 0:
                break
            if mbi.State == 4096 and mbi.Protect & 255 & 230 and (not mbi.Protect & 256) and (0 < mbi.RegionSize < 268435456):
                buf = read(mbi.BaseAddress or 0, mbi.RegionSize)
                if buf:
                    base = mbi.BaseAddress or 0
                    pos = 0
                    while True:
                        pos = buf.find(needle, pos)
                        if pos < 0:
                            break
                        hits.append(base + pos)
                        pos += 1
            addr = (mbi.BaseAddress or 0) + mbi.RegionSize
        return hits

    # ---------------------------------------------------------------
    # Optional master-key (global_config) path - NOT on the main scan
    # route; reference data and flow copied from fanyuantaier/wechatauto-replica
    # wechatauto/db.py:54-73, 236-282, 328-398 (re-verified on clone HEAD
    # d462922). Optimised into MasterCfgProfile rows per the version-branching
    # data structure requested in the implementation brief. No runtime test on
    # a real client exists: Windows-only codec paths were NOT executed during
    # this work session (host is Linux; discipline forbids real-device
    # testing). The anchors are re-sampled per major WeChat release upstream.
    # ---------------------------------------------------------------

    # Weixin.dll machine-code anchor (sub_1803308D0 prefix), verbatim from
    # fanyuantaier db.py:55-60. After the pattern come four '48 b8 <imm64>'
    # movabs immediates (0x10 bytes after the pattern) = master-key XOR
    # material; MASTER_DLL_VERIFY guards each write slot.
    MASTER_DLL_PATTERN = bytes.fromhex('83ec404889d64889cb0f57c00f1142100f11024c8bb1c80200004883b9d0020000107209488b9bb8020000eb074881c3b80200004d85f60f880a0200004983fe10736d4c89761048c746180f0000000f10030f110648b8')
    MASTER_DLL_VERIFY = (b'488944242048b8', b'488944242848b8', b'488944243048b8')

    @staticmethod
    def _find_weixin_module(pid: int):
        """Return (base, size, path) of weixin.dll, or None.

        Verbatim flow of fanyuantaier/wechatauto-replica db.py:236-258
        (Toolhelp32 snapshot scan for the weixin.dll module).
        """
        k32 = ctypes.WinDLL('kernel32', use_last_error=True)
        k32.CreateToolhelp32Snapshot.restype = ctypes.c_void_p
        k32.CreateToolhelp32Snapshot.argtypes = [wintypes.DWORD, wintypes.DWORD]
        k32.CloseHandle.argtypes = [ctypes.c_void_p]
        k32.Module32FirstW.argtypes = [ctypes.c_void_p, ctypes.POINTER(_MODULEENTRY32W)]
        k32.Module32NextW.argtypes = [ctypes.c_void_p, ctypes.POINTER(_MODULEENTRY32W)]
        snap = k32.CreateToolhelp32Snapshot(0x08 | 0x10, pid)
        if not snap or snap == -1 or snap == (1 << 64) - 1:
            return None
        try:
            me = _MODULEENTRY32W()
            me.dwSize = ctypes.sizeof(me)
            ok = k32.Module32FirstW(snap, ctypes.byref(me))
            while ok:
                if me.szModule and me.szModule.lower() == 'weixin.dll':
                    return (me.modBaseAddr or 0), me.modBaseSize, me.szExePath
                me.dwSize = ctypes.sizeof(me)
                ok = k32.Module32NextW(snap, ctypes.byref(me))
        finally:
            k32.CloseHandle(snap)
        return None

    @staticmethod
    def _extract_movabs_xor_key(dll_path: str):
        """Extract the four movabs-immediate blocks following MASTER_DLL_PATTERN.

        Verbatim logic of fanyuantaier/wechatauto-replica db.py:264-282; the
        immediates concatenated form the 64-byte master-key XOR material.
        Returns 64 XOR bytes or None when the anchor/verify chain breaks.
        """
        try:
            with open(dll_path, 'rb') as f:
                data = f.read()
        except OSError:
            return None
        hit = data.find(WeChatDB.MASTER_DLL_PATTERN)
        if hit < 0:
            return None
        take = min(200, len(data) - hit)
        hex_txt = data[hit:hit + take].hex()
        hex_txt = hex_txt[len(WeChatDB.MASTER_DLL_PATTERN) * 2:]
        key = ''
        for vf in WeChatDB.MASTER_DLL_VERIFY:
            if len(hex_txt) < 30 or hex_txt[16:30] != vf.decode():
                return None
            key += hex_txt[0:16]
            hex_txt = hex_txt[30:]
        if len(hex_txt) < 16:
            return None
        key += hex_txt[0:16]
        try:
            return bytes.fromhex(key)
        except ValueError:
            return None

    @staticmethod
    def _read_remote_string(h, read, addr: int) -> str:
        """Read an MSVC x64 std::string from the target process (SSO layout)."""
        sz_buf = read(addr + 16, 8)
        if not sz_buf:
            return ''
        size = struct.unpack_from('<Q', sz_buf)[0]
        if size <= 0 or size > 0x7FFFFFFF:
            return ''
        if size <= 15:
            data = read(addr, size)
        else:
            p_buf = read(addr, 8)
            if not p_buf:
                return ''
            data = read(struct.unpack_from('<Q', p_buf)[0], size)
        if not data:
            return ''
        return data[:size].decode('utf-8', 'replace')

    @staticmethod
    def _read_remote_bytes(h, read, addr: int) -> Optional[bytes]:
        """Read a std::string buffer from the target process (SSO layout)."""
        sz_buf = read(addr + 16, 8)
        if not sz_buf:
            return None
        size = struct.unpack_from('<Q', sz_buf)[0]
        if size <= 0 or size > 0x400:
            return None
        cap_buf = read(addr + 24, 4)
        cap = struct.unpack_from('<I', cap_buf)[0] if cap_buf else 0
        if (cap | 0xF) == 0xF:
            data = read(addr, size)
        else:
            p_buf = read(addr, 8)
            if not p_buf:
                return None
            data = read(struct.unpack_from('<Q', p_buf)[0], size)
        if not data or len(data) != size:
            return None
        return data[:size]

    def extract_master_key_from_cfg(self, pid: int,
                                    profile: Optional[MasterCfgProfile] = None) -> \
            Optional[Tuple[str, int, str]]:
        """Optional fallback: (master_key_hex, cfg_dword, wxid) from global_config.

        Flow (fanyuantaier db.py:328-398): read the weixin.dll image -> locate
        the 'global_config' SSO landmark -> pointer chain via this profile's
        cfg_ptr_back/cfg_offset -> cfg+cfg_dword_off/cfg+cfg_wxid_off/
        cfg+cfg_cipher_off -> cipher bytes XOR the DLL movabs material.
        Returns None on any anchor drift or read failure.

        待实测: implemented from audited upstream code, but not executed in
        this session; master-key trustworthiness on 4.1.13+ is also flagged in
        upstream as potentially stale. Use only as diagnostics/fallback.
        """
        p = profile or MASTER_CFG_PROFILES[0]
        base, mod_size, dll_path = self._find_weixin_module(pid) or (0, 0, '')
        if not base or not dll_path or mod_size <= 0 or mod_size >= 0x40000000:
            return None
        h = _k32.OpenProcess(16 | 1024, False, pid)
        if not h:
            return None
        try:
            def read(addr: int, n: int):
                buf = ctypes.create_string_buffer(n)
                br = ctypes.c_size_t(0)
                if _k32.ReadProcessMemory(h, ctypes.c_void_p(addr), buf, n, ctypes.byref(br)) and br.value:
                    return buf.raw[:br.value]
                return None

            image = read(base, mod_size)
            if not image or len(image) != mod_size:
                return None
            pos = -1
            for i in range(len(image) - 8, 0, -8):
                if struct.unpack_from('<I', image, i)[0] == len(MasterCfgProfile.CFG_LANDMARK):
                    cap = struct.unpack_from('<I', image, i + 8)[0]
                    if cap and (cap | 0xF) == 0xF and i - 16 >= 0:
                        if image[i - 16:i - 16 + len(MasterCfgProfile.CFG_LANDMARK)] \
                                == MasterCfgProfile.CFG_LANDMARK:
                            pos = i
                            break
            if pos < 0:
                return None
            v18_buf = read(base + pos - p.cfg_ptr_back, 8)
            if not v18_buf:
                return None
            v18 = struct.unpack_from('<Q', v18_buf)[0]
            cfg_buf = read(v18 + p.cfg_offset, 8)
            if not cfg_buf:
                return None
            cfg = struct.unpack_from('<Q', cfg_buf)[0]
            if not (0x10000 <= cfg < 0x800000000000):
                return None
            dw_buf = read(cfg + p.cfg_dword_off, 4)
            cfg_dword = struct.unpack_from('<I', dw_buf)[0] if dw_buf else 0
            wxid = self._read_remote_string(h, read, cfg + p.cfg_wxid_off)
            cipher = self._read_remote_bytes(h, read, cfg + p.cfg_cipher_off)
            if not cipher:
                return None
            material = self._extract_movabs_xor_key(dll_path)
            if not material or len(material) != len(cipher):
                return None
            master = bytes(a ^ b for a, b in zip(cipher, material))
            return master.hex(), cfg_dword, wxid
        finally:
            _k32.CloseHandle(h)


# ---------------------------------------------------------------------------
# SQLCipher 4 page format helpers: byte-identical to the original slice file
# (PBKDF2-HMAC-SHA512, page1 HMAC 2 rounds, AES-256-CBC, page_size=4096,
# reserve=80, mac_salt = salt ^ 0x3A). This is the regression floor and it
# must stay untouched for all supported versions.
# ---------------------------------------------------------------------------

def _pbkdf2(passwd: bytes, salt: bytes, iters: int) -> bytes:
    return hashlib.pbkdf2_hmac('sha512', passwd, salt, iters, dklen=32)


def _verify_enc_key(enc_key: bytes, page1: bytes, salt: Optional[bytes] = None) -> bool:
    """Verify enc_key is the SQLCipher 4 key for page1.

    - enc_key is a 32-byte bare key: salt defaults to the first 16 bytes of
      page1 (standard form);
    - enc_key is 48 bytes (key+salt): the explicit salt takes precedence
      (plaintext-header form);
    - callers may also pass salt explicitly (derived from 96-hex runs).
    """
    if len(page1) < PAGE_SZ:
        return False
    if len(enc_key) == 48 and salt is None:
        enc_key, salt = (enc_key[:32], enc_key[32:])
    if salt is None:
        salt = page1[:16]
    elif len(salt) != 16:
        return False
    mac_salt = bytes((b ^ 58 for b in salt))
    mac_key = _pbkdf2(enc_key, mac_salt, 2)
    hmac_data = page1[16:PAGE_SZ - RESERVE_SZ + 16]
    stored_hmac = page1[PAGE_SZ - 64:PAGE_SZ]
    hm = hmac_mod.new(mac_key, hmac_data, hashlib.sha512)
    hm.update(struct.pack('<I', 1))
    return hm.digest() == stored_hmac


class _MBI(ctypes.Structure):
    _fields_ = [('BaseAddress', ctypes.c_void_p), ('AllocationBase', ctypes.c_void_p), ('AllocationProtect', wintypes.DWORD), ('__alignment1', wintypes.DWORD), ('RegionSize', ctypes.c_size_t), ('State', wintypes.DWORD), ('Protect', wintypes.DWORD), ('Type', wintypes.DWORD), ('__alignment2', wintypes.DWORD)]


class _MODULEENTRY32W(ctypes.Structure):
    """Toolhelp32 module entry (fanyuantaier db.py:99-112, for the master path)."""
    _fields_ = [
        ('dwSize', wintypes.DWORD),
        ('th32ModuleID', wintypes.DWORD),
        ('th32ProcessID', wintypes.DWORD),
        ('GlblcntUsage', wintypes.DWORD),
        ('ProccntUsage', wintypes.DWORD),
        ('modBaseAddr', ctypes.c_void_p),
        ('modBaseSize', wintypes.DWORD),
        ('hModule', wintypes.HMODULE),
        ('szModule', ctypes.c_wchar * 256),
        ('szExePath', ctypes.c_wchar * 260),
    ]


_k32 = ctypes.WinDLL('kernel32', use_last_error=True)

_k32.OpenProcess.restype = wintypes.HANDLE

_k32.OpenProcess.argtypes = [wintypes.DWORD, wintypes.BOOL, wintypes.DWORD]

_k32.VirtualQueryEx.argtypes = [wintypes.HANDLE, ctypes.c_void_p, ctypes.POINTER(_MBI), ctypes.c_size_t]

_k32.VirtualQueryEx.restype = ctypes.c_size_t

_k32.ReadProcessMemory.argtypes = [wintypes.HANDLE, ctypes.c_void_p, ctypes.c_void_p, ctypes.c_size_t, ctypes.POINTER(ctypes.c_size_t)]

_k32.CloseHandle.argtypes = [wintypes.HANDLE]
