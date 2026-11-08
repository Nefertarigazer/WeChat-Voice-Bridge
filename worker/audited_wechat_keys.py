# Read-only excerpt from fanyuantaier/wechatauto-replica.
# Source commit 4931f98d4b4b68dc4af8cfc10f76805eb9c743da, wechatauto/db.py.
# Apache-2.0, see WECHATAUTO-LICENSE.txt. Only selected definitions are carried.
import ctypes
from ctypes import wintypes
import hashlib
import hmac as hmac_mod
import re
import struct
from typing import Dict, List, Optional

PAGE_SZ = 4096

RESERVE_SZ = 80

CONFIG_CIPHER_NAME = b'com.Tencent.WCDB.Config.Cipher'

CONFIG_XOR_MASK = bytes.fromhex('d2c7442458020000004889442450488b450048844c2448488944254048584c24')

HEX_LITERAL_RE = re.compile(b"[xX]'([0-9a-fA-F]{64,192})'")

class _MBI(ctypes.Structure):
    _fields_ = [('BaseAddress', ctypes.c_void_p), ('AllocationBase', ctypes.c_void_p), ('AllocationProtect', wintypes.DWORD), ('__alignment1', wintypes.DWORD), ('RegionSize', ctypes.c_size_t), ('State', wintypes.DWORD), ('Protect', wintypes.DWORD), ('Type', wintypes.DWORD), ('__alignment2', wintypes.DWORD)]

_k32 = ctypes.WinDLL('kernel32', use_last_error=True)

_k32.OpenProcess.restype = wintypes.HANDLE

_k32.OpenProcess.argtypes = [wintypes.DWORD, wintypes.BOOL, wintypes.DWORD]

_k32.VirtualQueryEx.argtypes = [wintypes.HANDLE, ctypes.c_void_p, ctypes.POINTER(_MBI), ctypes.c_size_t]

_k32.VirtualQueryEx.restype = ctypes.c_size_t

_k32.ReadProcessMemory.argtypes = [wintypes.HANDLE, ctypes.c_void_p, ctypes.c_void_p, ctypes.c_size_t, ctypes.POINTER(ctypes.c_size_t)]

def _pbkdf2(passwd: bytes, salt: bytes, iters: int) -> bytes:
    return hashlib.pbkdf2_hmac('sha512', passwd, salt, iters, dklen=32)

def _verify_enc_key(enc_key: bytes, page1: bytes, salt: Optional[bytes]=None) -> bool:
    """验证 enc_key 是否为 page1 的 SQLCipher 4 密钥。

    - enc_key 为 32 字节裸 key：默认用文件头前 16 字节作 salt（标准形式）；
    - enc_key 为 48 字节 key+salt：显式 salt 优先于文件头（明文头模式）；
    - 也允许单独传 salt 参数覆盖（供提取逻辑按 96hex 拆分尝试）。
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

class WeChatDB:

    def _collect_key_candidates(self, pid: int, seen: set) -> set:
        """收集候选密钥材料（**不做账号校验**）：{(cand, salt_or_None), ...}

        与 `_extract_keys_pid` 的区别：这里只负责“找出可能是密钥的 32 字节
        （或 32+16 带显式 salt）”，校验交给调用方对**每个账号目录**分别做。
        原因：内存里的密钥属于**当前登录账号**，只有对正确的账号目录才能通过
        页1 HMAC；这样账号选择由密码学校验决定，不再依赖“最近修改 .db”启发式
        （微信每次更新会重写 .db，mtime 全变 → 启发式会选错账号 → 0 密钥）。
        """
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
            needles = self._find_bytes(h, read, CONFIG_CIPHER_NAME)
            pairs = [struct.pack('<Q', addr) + struct.pack('<Q', len(CONFIG_CIPHER_NAME)) for addr in needles]
            for pair in pairs:
                for qaddr in self._find_bytes(h, read, pair):
                    node = read(qaddr - 16, 80)
                    if not node or len(node) < 64:
                        continue
                    if struct.unpack_from('<Q', node, 16)[0] not in needles:
                        continue
                    if struct.unpack_from('<Q', node, 24)[0] != len(CONFIG_CIPHER_NAME):
                        continue
                    config_ptr = struct.unpack_from('<Q', node, 40)[0]
                    if not 65536 <= config_ptr < 140737488355328:
                        continue
                    obj = read(config_ptr + 136, 40)
                    if not obj or len(obj) < 24:
                        continue
                    data_ptr = struct.unpack_from('<Q', obj, 8)[0]
                    data_len = struct.unpack_from('<Q', obj, 16)[0]
                    if not (0 < data_len <= 1024 and 65536 <= data_ptr < 140737488355328):
                        continue
                    blob = read(data_ptr, int(data_len))
                    if not blob or len(blob) != data_len:
                        continue
                    decoded = bytes((v ^ CONFIG_XOR_MASK[i % len(CONFIG_XOR_MASK)] for i, v in enumerate(blob)))
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
        """把候选材料对**当前 self._db_files** 逐个 HMAC 校验，返回可用密钥。

        存储形式按库的实际布局决定：**先按标准形式（文件头 salt）校验**，通过就
        存 32 字节裸密钥；只有标准形式验不过时（明文头库）才用候选自带的显式
        salt 并存成 48 字节 key+salt。
        顺序很关键：若先试显式 salt，标准库也会被存成 48 字节，解密时就会走
        「明文头」分支，产出非 SQLite 文件（实测踩过：file is not a database）。
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

_k32.CloseHandle.argtypes = [wintypes.HANDLE]
