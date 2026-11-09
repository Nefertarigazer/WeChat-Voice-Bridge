"""Self-check for the merged multi-version key reader.

Run (from the repository root, after the wx-multi merge):
    python tests/wx-multi/test_audited_wechat_keys.py

The original slice is frozen at tests/wx-multi/slice/audited_wechat_keys.py and
MUST NOT be edited. IMPL/MAIN point at the real shipped worker files, so this
58-check equivalence gate keeps guarding the actual production code.

What this test does (read-only, no WeChat, no Windows):
  * patches ctypes.WinDLL so the module imports on Linux;
  * loads the frozen slice (original) and worker/audited_wechat_keys.py
    (adapted) side by side and asserts constant-level identity (anchor string,
    XOR mask, regex, page params) and function-level identity (_pbkdf2 /
    _verify_enc_key) over many random pages;
  * builds a synthetic WCDB Config.Cipher memory image (needle, (ptr,len) pair,
    std::string node, config object at config_ptr+0x88, XOR-masked hex-literal
    blob) in a fake VirtualQueryEx/ReadProcessMemory backend and runs BOTH the
    original and the adapted scanner over it: candidate sets (including the
    (cand, salt) explicit-salt variant) must be identical and keys_from_candidates
    must authenticate the expected keys through page1 HMAC (standard form and
    plaintext-header form);
  * checks strict version dispatch for every boundary value (4.1.11 /
    4.1.10.x / 4.2.0.13 / 4.2.0.14 / 4.0.x / unknown) and that refused versions
    never reach OpenProcess (no guessing, no force-fitting).
"""

import ctypes
import hashlib
import hmac
import importlib.util
import os
import sys
import struct
import tempfile
import types
from pathlib import Path
from unittest import mock

sys.dont_write_bytecode = True   # keep the frozen slice free of __pycache__
ROOT = Path(__file__).resolve().parents[2]          # repository root
SLICE = ROOT / 'tests' / 'wx-multi' / 'slice' / 'audited_wechat_keys.py'   # frozen original slice
IMPL = ROOT / 'worker' / 'audited_wechat_keys.py'   # merged implementation
MAIN = ROOT / 'worker' / 'main.py'                  # merged worker entry point

PASS = 0


def check(name, cond):
    global PASS
    assert cond, 'FAIL: ' + name
    PASS += 1
    print('ok  - ' + name)


def load(name, path):
    spec = importlib.util.spec_from_file_location(name, str(path))
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


ctypes.WinDLL = mock.MagicMock(return_value=mock.MagicMock())
orig = load('ak_orig', SLICE)
adapted = load('ak_impl', IMPL)

# ---------------------------------------------------------------- constants
check('PAGE_SZ identical', orig.PAGE_SZ == adapted.PAGE_SZ == 4096)
check('RESERVE_SZ identical', orig.RESERVE_SZ == adapted.RESERVE_SZ == 80)
check('CONFIG_CIPHER_NAME identical', orig.CONFIG_CIPHER_NAME == adapted.CONFIG_CIPHER_NAME)
# The slice file is the canonical baseline; the impl value was additionally
# verified byte-identical against the upstream clones (see impl-selfcheck.md).
EXPECTED_MASK = orig.CONFIG_XOR_MASK
check('mask sanity: 32B, prefix/suffix spot checks',
      len(EXPECTED_MASK) == 32
      and EXPECTED_MASK.startswith(bytes.fromhex('d2c74424'))
      and EXPECTED_MASK.endswith(bytes.fromhex('584c24')))
check('CONFIG_XOR_MASK byte-identical (impl == slice)',
      orig.CONFIG_XOR_MASK == adapted.CONFIG_XOR_MASK)
check('HEX_LITERAL_RE identical', orig.HEX_LITERAL_RE.pattern == adapted.HEX_LITERAL_RE.pattern)
check('MASTER_DLL_PATTERN is 87 bytes with b8 movabs tail 0f110648b8',
      len(adapted.WeChatDB.MASTER_DLL_PATTERN) == 87
      and adapted.WeChatDB.MASTER_DLL_PATTERN.endswith(bytes.fromhex('0f110648b8')))
check('MASTER_DLL_VERIFY matches upstream',
      adapted.WeChatDB.MASTER_DLL_VERIFY == (b'488944242048b8', b'488944242848b8',
                                            b'488944243048b8'))
check('no legacy scan branch left',
      not hasattr(adapted, 'LegacyProfile')
      and not hasattr(adapted.WeChatDB, '_collect_legacy_candidates'))
check('support range is exactly 4.1.11 - 4.2.0.13',
      adapted.PROFILES[0].min_version == (4, 1, 11, 0)
      and adapted.PROFILES[0].max_version == (4, 2, 0, 13)
      and len(adapted.PROFILES) == 1)

# ---------------------------------------------------------------- version dispatch
for good in ['4.1.11', '4.1.11.0', '4.1.12', '4.1.13.12', '4.1.15.99', '4.1.15.13',
             '4.2.0.11', '4.2.0.13']:
    check('profile_for accepts %s' % good,
          adapted.profile_for(good).name == 'wcdb-config-cipher-4.1.11+')
for bad in ['4.1.10', '4.1.10.24', '4.1.10.31', '4.0.3', '4.1.9.57', '4.2.0.14',
            '4.2.1.0', '5.0.0.1', '4.1', '', 'garbage', '4.1.11 alpha']:
    try:
        adapted.profile_for(bad)
        check('profile_for rejects %r' % bad, False)
    except adapted.UnsupportedVersionError as exc:
        check('profile_for rejects %r' % bad, '不支持此版本' in str(exc))
try:
    adapted.profile_for(None)
    check('profile_for rejects None', False)
except (adapted.UnsupportedVersionError, TypeError):
    check('profile_for rejects None', True)

_q = adapted.profile_for('4.2.0.13').to_xcipher_params()
check('profile offsets == canonical layout',
      _q.needle == adapted.CONFIG_CIPHER_NAME and _q.node_back == 0x10
      and _q.config_ptr_offset == 0x28 and _q.blob_object_offset == 0x88
      and _q.blob_ptr_offset == 0x08 and _q.blob_len_offset == 0x10
      and _q.blob_max == 1024)
check('master-cfg band exists (4.1.12-4.2.0.13)',
      adapted.master_cfg_profile_for('4.1.12') is not None
      and adapted.master_cfg_profile_for('4.2.0.13') is not None)

# R2: the candidate mask is exactly the slice baseline (no empty-mask candidate)
check('R2: masks == slice CONFIG_XOR_MASK (no empty-mask branch)',
      adapted.PROFILES[0].masks == (adapted.CONFIG_XOR_MASK,)
      and _q.masks == (adapted.CONFIG_XOR_MASK,))
# R3: an empty version is not "supported" (aligns with MasterCfgProfile)
check('R3: ConfigCipherProfile.supports(()) is False',
      adapted.PROFILES[0].supports(()) is False
      and adapted.PROFILES[0].supports((4, 1, 11)) is True)
# R4: strict parse_version, matching System.Version (max 4 integer segments)
check('R4: parse_version strict',
      adapted.parse_version('4.1.11.0.99') == ()
      and adapted.parse_version('4.1.11 alpha') == ()
      and adapted.parse_version('') == ()
      and adapted.parse_version(None) == ()
      and adapted.parse_version('4.1.11') == (4, 1, 11))
try:
    adapted.profile_for('4.1.11.0.99')
    check('R4: profile_for rejects 5-segment version (Python == C# System.Version)', False)
except adapted.UnsupportedVersionError as exc:
    check('R4: profile_for rejects 5-segment version (Python == C# System.Version)',
          '不支持此版本' in str(exc))

# ---------------------------------------------------------------- HMAC floor
def make_page(key, salt):
    page = bytearray(4096)
    page[:16] = salt
    mac_salt = bytes(b ^ 58 for b in salt)
    mac_key = hashlib.pbkdf2_hmac('sha512', key, mac_salt, 2, dklen=32)
    hm = hmac.new(mac_key, bytes(page[16:4032]), hashlib.sha512)
    hm.update(struct.pack('<I', 1))
    page[4032:] = hm.digest()
    return bytes(page)


floor_ok = True
for _ in range(200):
    key, salt = os.urandom(32), os.urandom(16)
    page = make_page(key, salt)
    if not (orig._verify_enc_key(key, page) and adapted._verify_enc_key(key, page)):
        floor_ok = False
    if orig._verify_enc_key(os.urandom(32), page) != adapted._verify_enc_key(os.urandom(32), page):
        floor_ok = False
    if orig.WeChatDB._probable_key(key) != adapted.WeChatDB._probable_key(key):
        floor_ok = False
check('_verify_enc_key / _probable_key equivalence over 200 random pages', floor_ok)

# ---------------------------------------------------------------- fake memory backend
REA = 0x10000
RS_SZ = 0xFA000
NEEDLE_AT = REA + 0x100
PAIR_AT = REA + 0x300
NODE_AT = PAIR_AT - 0x10                 # read(qaddr - node_back=0x10, 80)
OBJ_BASE_AT = REA + 0x8000               # config_ptr; object struct lives at +0x88
BLOB_AT = REA + 0x9000
MEM = bytearray(RS_SZ)


def vq_side(h, addr, mbi_ptr, size):
    if (addr.value or 0) != 0:
        return 0
    mbi = adapted._MBI()
    mbi.BaseAddress = REA
    mbi.RegionSize = RS_SZ
    mbi.State = 4096
    mbi.Protect = 0x40                    # PAGE_EXECUTE_READWRITE passes 0xE6 filter
    ctypes.memmove(mbi_ptr, ctypes.cast(ctypes.byref(mbi), ctypes.c_void_p),
                   ctypes.sizeof(mbi))
    return ctypes.sizeof(mbi)


def rpm_side(h, addr, buf_ptr, size, br_ptr):
    base = addr.value if isinstance(addr, ctypes.c_void_p) else addr
    data = MEM[base - REA:base - REA + size]
    ctypes.memmove(buf_ptr, bytes(data), len(data))
    wrote = ctypes.c_size_t(len(data))
    ctypes.memmove(br_ptr, ctypes.cast(ctypes.byref(wrote), ctypes.c_void_p),
                   ctypes.sizeof(wrote))
    return True


def install_backend(module):
    m = module._k32
    m.VirtualQueryEx.side_effect = vq_side
    m.ReadProcessMemory.side_effect = rpm_side
    m.OpenProcess.side_effect = lambda *a, **k: 1
    m.CloseHandle.side_effect = lambda *a, **k: 1


install_backend(orig)
install_backend(adapted)


def build_scene(run_hex: bytes, extra: bytes = b''):
    """Place needle / (ptr,len) pair / node / obj / masked blob into MEM."""
    MEM[:] = b'\x00' * RS_SZ          # in-place clear (del MEM[:] would shrink it)
    needle = adapted.CONFIG_CIPHER_NAME
    MEM[NEEDLE_AT - REA:NEEDLE_AT - REA + len(needle)] = needle
    MEM[PAIR_AT - REA:PAIR_AT - REA + 16] = struct.pack('<Q', NEEDLE_AT) + struct.pack('<Q', len(needle))
    off = NODE_AT - REA
    MEM[off + 0x10:off + 0x18] = struct.pack('<Q', NEEDLE_AT)
    MEM[off + 0x18:off + 0x20] = struct.pack('<Q', len(needle))
    MEM[off + 0x28:off + 0x30] = struct.pack('<Q', OBJ_BASE_AT)
    o = OBJ_BASE_AT + adapted.PROFILES[0].blob_object_offset - REA
    payload = b"x'" + (run_hex if isinstance(run_hex, bytes) else run_hex.encode()) + b"'" + extra
    MEM[o + 0x08:o + 0x10] = struct.pack('<Q', BLOB_AT)
    MEM[o + 0x10:o + 0x18] = struct.pack('<Q', len(payload))
    blob = bytes(b ^ adapted.CONFIG_XOR_MASK[i % len(adapted.CONFIG_XOR_MASK)]
                 for i, b in enumerate(payload))
    MEM[BLOB_AT - REA:BLOB_AT - REA + len(blob)] = blob


def scan(module, version, db_rel, db_path):
    reader = module.WeChatDB(version=version) if module is adapted else module.WeChatDB()
    reader._db_files = [(db_rel, str(db_path), 0)]
    return reader._keys_from_candidates(reader._collect_key_candidates(4242, set()))


with tempfile.TemporaryDirectory() as tmp:
    tmpd = Path(tmp)
    key1, saltA = os.urandom(32), os.urandom(16)
    (tmpd / 'a.db').write_bytes(make_page(key1, saltA))           # standard form
    key2, salt2, wrong = os.urandom(32), os.urandom(16), os.urandom(16)
    page2 = bytearray(make_page(key2, salt2))
    page2[0:16] = wrong                                           # plaintext-header form
    (tmpd / 'b.db').write_bytes(bytes(page2))

    # --- scene A: key1 at run offset 0 (two candidates: None + explicit salt)
    build_scene((key1 + saltA).hex(), extra=b'garbage tail !\n')
    cands = adapted.WeChatDB(version='4.2.0.13')._collect_key_candidates(4242, set())
    pairs = [c for c in cands if isinstance(c, tuple) and len(c) == 2]
    check('scene A: exactly two records for the scanned key (None + explicit salt)',
          (key1, None) in cands
          and sum(1 for c in pairs if c[0] == key1) == 2)
    check('scene A: ad-hoc 32-byte candidate still present',
          key1 in cands or any(c[0] == key1 for c in pairs))
    keys = scan(adapted, '4.2.0.13', 'a.db', tmpd / 'a.db')
    check('scene A: standard-form DB authenticated, bare 32B key stored', keys == {'a.db': key1})
    keys_o = scan(orig, None, 'a.db', tmpd / 'a.db')
    check('scene A: original scanner agrees with adapted', keys_o == keys)

    # --- scene B: key1 at run offset 32 (step-32 split path), standard-form DB
    build_scene(os.urandom(16).hex() + (key1 + saltA).hex())
    cands_b = adapted.WeChatDB(version='4.1.13.12')._collect_key_candidates(4242, set())
    hits = [c for c in cands_b if isinstance(c, tuple) and c[0] == key1]
    check('scene B: key found at step-32 offset with its explicit salt',
          (key1, saltA) in hits and (key1, None) in hits)
    check('scene B: scan authenticates key for standard-form DB',
          scan(adapted, '4.1.13.12', 'a.db', tmpd / 'a.db') == {'a.db': key1})

    # --- scene C: plaintext-header DB, key2+salt2 at offsets 32/96 of a 128-hex run
    decoy = os.urandom(16).hex()
    build_scene(decoy.encode() + (key2 + salt2).hex().encode())
    keys_c = scan(adapted, '4.1.15.99', 'b.db', tmpd / 'b.db')
    check('scene C: plaintext-header DB stores 48B key+salt via explicit salt branch',
          keys_c == {'b.db': key2 + salt2})
    keys_c_orig = scan(orig, None, 'b.db', tmpd / 'b.db')
    check('scene C: original scanner agrees on plaintext-header acceptance', keys_c_orig == keys_c)

# ---------------------------------------------------------------- refusal discipline
adapt_k32 = adapted._k32
adapt_k32.OpenProcess.reset_mock()
for bad_version in ['4.0.3', '4.1.10.24', '4.1.10.31', '4.2.0.14', '5.0.0.1']:
    reader = adapted.WeChatDB(version=bad_version)
    try:
        reader._collect_key_candidates(4242, set())
        check('refused version %s raised before scanning' % bad_version, False)
    except adapted.UnsupportedVersionError:
        check('refused version %s raised before scanning' % bad_version,
              adapt_k32.OpenProcess.call_count == 0)
try:
    adapted.WeChatDB()._collect_key_candidates(4242, set())
    check('missing version refused without guessing', False)
except adapted.UnsupportedVersionError:
    check('missing version refused without guessing', True)
check('refusal never opened a process handle', adapt_k32.OpenProcess.call_count == 0)

# ---------------------------------------------------------------- R1: main.py error mapping
_sends = []
_start = {
    "type": "start",
    "session": "11111111-2222-3333-4444-555555555555",
    "generation": 1,
    "account": "wxid_test",
    "conversation": "peer",
    "mode": "wechat",
    "root": "/tmp",
    "pids": [4242],
    "version": "9.9.9.9",
    "rules": None,
    "lexicon": None,
}
_received = []


def _fake_receive():
    if not _received:
        _received.append(_start)
        return _start
    return None


def _fake_send(kind, session, generation, **fields):
    _sends.append({"kind": kind, "code": fields.get("code"),
                   "message": fields.get("message")})


class _IdentityChanged(Exception):
    pass


class _FixtureStore:
    def __init__(self, *args, **kwargs):
        self.shards = []


class _SnapshotBusy(Exception):
    pass


_OpErr = type("OperationalError", (Exception,), {})
_sql_ns = types.SimpleNamespace(OperationalError=_OpErr)


def _configure(*args, **kwargs):
    pass


def _render_speech(*args, **kwargs):
    return ""


class _WeChatStoreStub:
    def __init__(self, *args, **kwargs):
        version = kwargs.get("version", "<unknown>")
        raise adapted.UnsupportedVersionError(
            version, "不支持此版本：检测到 %s。" % version)


_stubs = {
    "db_adapter": types.SimpleNamespace(
        ACCOUNT="ACCOUNT", CONVERSATION="CONVERSATION", SHARDS=[],
        TEXT_LIMIT=1024, FixtureStore=_FixtureStore,
        IdentityChanged=_IdentityChanged),
    "ipc_protocol": types.SimpleNamespace(receive=_fake_receive, send=_fake_send),
    "wechat_adapter": types.SimpleNamespace(
        SnapshotBusy=_SnapshotBusy, WeChatStore=_WeChatStoreStub,
        configure=_configure, render_speech=_render_speech, sql=_sql_ns),
    "audited_wechat_keys": adapted,
}

_saved_modules = {name: sys.modules.get(name) for name in _stubs}
sys.modules.update(_stubs)
_saved_argv = list(sys.argv)
rc = None
main_mod = None
try:
    with tempfile.TemporaryDirectory() as fixture_dir:
        sys.argv = ["main.py", "--fixture", fixture_dir]
        main_mod = load("impl_main", MAIN)
        check('R1: main.py imports the same UnsupportedVersionError class',
              getattr(main_mod, "UnsupportedVersionError", None) is adapted.UnsupportedVersionError)
        rc = main_mod.main()
finally:
    sys.argv = _saved_argv
    for name, module in _saved_modules.items():
        if module is None:
            sys.modules.pop(name, None)
        else:
            sys.modules[name] = module

_errs = [s for s in _sends if s["kind"] == "error"]
check('R1: main.py maps UnsupportedVersionError to code "unsupported_version"',
      any(s["code"] == "unsupported_version" for s in _errs))
check('R1: error message keeps the Chinese "不支持此版本" reason',
      any(s["code"] == "unsupported_version" and s.get("message")
          and "不支持此版本" in s["message"] for s in _errs))
check('R1: unsupported version no longer falls through to generic "worker"',
      not any(s["code"] == "worker" for s in _errs))
check('R1: main.py returns a non-zero exit code on refusal', rc not in (0, None))

print('\nALL SELF-CHECKS PASSED (' + str(PASS) + ' checks)')
