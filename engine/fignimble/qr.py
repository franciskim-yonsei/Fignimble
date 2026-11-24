"""QR codes (ISO/IEC 18004), for the link on a poster: byte mode, versions 1-40, error correction L, M, Q or H, the
mask chosen by the standard's penalty rules. Written for Fignimble so a QR code needs no package; it follows
Project Nayuki's reference implementation (MIT License).

    modules("https://github.com/pachterlab/Fignimble")   ->  [[True, False, ...], ...]   (True: a dark module)
"""

from __future__ import annotations

LEVELS = {"L": 1, "M": 0, "Q": 3, "H": 2}            # the format bits of each error correction level

# per level (L, M, Q, H), indexed by version: error correction codewords per block, and the number of blocks
_ECC_PER_BLOCK = {
    "L": [-1, 7, 10, 15, 20, 26, 18, 20, 24, 30, 18, 20, 24, 26, 30, 22, 24, 28, 30, 28, 28, 28, 28, 30, 30, 26, 28,
          30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30],
    "M": [-1, 10, 16, 26, 18, 24, 16, 18, 22, 22, 26, 30, 22, 22, 24, 24, 28, 28, 26, 26, 26, 26, 28, 28, 28, 28, 28,
          28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28],
    "Q": [-1, 13, 22, 18, 26, 18, 24, 18, 22, 20, 24, 28, 26, 24, 20, 30, 24, 28, 28, 26, 30, 28, 30, 30, 30, 30, 28,
          30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30],
    "H": [-1, 17, 28, 22, 16, 22, 28, 26, 26, 24, 28, 24, 28, 22, 24, 24, 30, 28, 28, 26, 28, 30, 24, 30, 30, 30, 30,
          30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30],
}
_BLOCKS = {
    "L": [-1, 1, 1, 1, 1, 1, 2, 2, 2, 2, 4, 4, 4, 4, 4, 6, 6, 6, 6, 7, 8, 8, 9, 9, 10, 12, 12, 12, 13, 14, 15, 16, 17,
          18, 19, 19, 20, 21, 22, 24, 25],
    "M": [-1, 1, 1, 1, 2, 2, 4, 4, 4, 5, 5, 5, 8, 9, 9, 10, 10, 11, 13, 14, 16, 17, 17, 18, 20, 21, 23, 25, 26, 28, 29,
          31, 33, 35, 37, 38, 40, 43, 45, 47, 49],
    "Q": [-1, 1, 1, 2, 2, 4, 4, 6, 6, 8, 8, 8, 10, 12, 16, 12, 17, 16, 18, 21, 20, 23, 23, 25, 27, 29, 34, 34, 35, 38,
          40, 43, 45, 48, 51, 53, 56, 59, 62, 65, 68],
    "H": [-1, 1, 1, 2, 4, 4, 4, 5, 6, 8, 8, 11, 11, 16, 16, 18, 16, 19, 21, 25, 25, 25, 34, 30, 32, 35, 37, 40, 42, 45,
          48, 51, 54, 57, 60, 63, 66, 70, 74, 77, 81],
}


def _raw_modules(ver: int) -> int:
    """Data and error correction bits a version holds (everything but the function patterns)."""
    n = (16 * ver + 128) * ver + 64
    if ver >= 2:
        a = ver // 7 + 2
        n -= (25 * a - 10) * a - 55
        if ver >= 7:
            n -= 36
    return n


def _data_codewords(ver: int, level: str) -> int:
    return _raw_modules(ver) // 8 - _ECC_PER_BLOCK[level][ver] * _BLOCKS[level][ver]


def _gf_mul(x: int, y: int) -> int:
    z = 0
    for i in range(7, -1, -1):
        z = (z << 1) ^ ((z >> 7) * 0x11D)
        z ^= ((y >> i) & 1) * x
    return z


def _rs_divisor(degree: int) -> list[int]:
    out = [0] * (degree - 1) + [1]
    root = 1
    for _ in range(degree):
        for j in range(degree):
            out[j] = _gf_mul(out[j], root)
            if j + 1 < degree:
                out[j] ^= out[j + 1]
        root = _gf_mul(root, 0x02)
    return out


def _rs_remainder(data: list[int], divisor: list[int]) -> list[int]:
    out = [0] * len(divisor)
    for b in data:
        factor = b ^ out.pop(0)
        out.append(0)
        for i, coef in enumerate(divisor):
            out[i] ^= _gf_mul(coef, factor)
    return out


def _interleaved(data: list[int], ver: int, level: str) -> list[int]:
    """The data split into blocks, each given its error correction, and the blocks interleaved."""
    nblocks, ecc = _BLOCKS[level][ver], _ECC_PER_BLOCK[level][ver]
    raw = _raw_modules(ver) // 8
    short = nblocks - raw % nblocks
    short_len = raw // nblocks
    div = _rs_divisor(ecc)
    blocks, k = [], 0
    for i in range(nblocks):
        dat = data[k:k + short_len - ecc + (0 if i < short else 1)]
        k += len(dat)
        e = _rs_remainder(dat, div)
        if i < short:
            dat = dat + [0]                              # a gap, so every block lines up for interleaving
        blocks.append(dat + e)
    out = []
    for i in range(len(blocks[0])):
        for j, b in enumerate(blocks):
            if i != short_len - ecc or j >= short:
                out.append(b[i])
    return out


def _alignment(ver: int) -> list[int]:
    if ver == 1:
        return []
    n = ver // 7 + 2
    step = (ver * 8 + n * 3 + 5) // (n * 4 - 4) * 2
    return [6] + [ver * 4 + 10 - i * step for i in range(n - 1)][::-1]


class _Matrix:
    def __init__(self, ver: int):
        self.ver = ver
        self.size = ver * 4 + 17
        self.dark = [[False] * self.size for _ in range(self.size)]
        self.fixed = [[False] * self.size for _ in range(self.size)]

    def set(self, x, y, dark):
        self.dark[y][x] = dark
        self.fixed[y][x] = True

    def function_patterns(self):
        n = self.size
        for i in range(n):
            self.set(6, i, i % 2 == 0)
            self.set(i, 6, i % 2 == 0)
        for cx, cy in ((3, 3), (n - 4, 3), (3, n - 4)):
            for dy in range(-4, 5):
                for dx in range(-4, 5):
                    x, y = cx + dx, cy + dy
                    if 0 <= x < n and 0 <= y < n:
                        d = max(abs(dx), abs(dy))
                        self.set(x, y, d not in (2, 4))
        pos = _alignment(self.ver)
        for i, a in enumerate(pos):
            for j, b in enumerate(pos):
                if (i == 0 and j == 0) or (i == 0 and j == len(pos) - 1) or (i == len(pos) - 1 and j == 0):
                    continue
                for dy in range(-2, 3):
                    for dx in range(-2, 3):
                        self.set(a + dx, b + dy, max(abs(dx), abs(dy)) != 1)
        self.format_bits(LEVELS["M"], 0)                 # placeholders, written for real once the mask is chosen
        self.version_bits()

    def format_bits(self, level_bits: int, mask: int):
        data = level_bits << 3 | mask
        rem = data
        for _ in range(10):
            rem = (rem << 1) ^ ((rem >> 9) * 0x537)
        bits = (data << 10 | rem) ^ 0x5412
        bit = lambda i: (bits >> i) & 1 != 0                # noqa: E731
        n = self.size
        for i in range(6):
            self.set(8, i, bit(i))
        self.set(8, 7, bit(6))
        self.set(8, 8, bit(7))
        self.set(7, 8, bit(8))
        for i in range(9, 15):
            self.set(14 - i, 8, bit(i))
        for i in range(8):
            self.set(n - 1 - i, 8, bit(i))
        for i in range(8, 15):
            self.set(8, n - 15 + i, bit(i))
        self.set(8, n - 8, True)                          # the module that is always dark

    def version_bits(self):
        if self.ver < 7:
            return
        rem = self.ver
        for _ in range(12):
            rem = (rem << 1) ^ ((rem >> 11) * 0x1F25)
        bits = self.ver << 12 | rem
        for i in range(18):
            b = (bits >> i) & 1 != 0
            a, c = self.size - 11 + i % 3, i // 3
            self.set(a, c, b)
            self.set(c, a, b)

    def codewords(self, data: list[int]):
        n = self.size
        i = 0
        right = n - 1
        while right >= 1:
            if right == 6:
                right = 5
            for vert in range(n):
                for j in range(2):
                    x = right - j
                    upward = (right + 1) & 2 == 0
                    y = n - 1 - vert if upward else vert
                    if not self.fixed[y][x] and i < len(data) * 8:
                        self.dark[y][x] = (data[i >> 3] >> (7 - (i & 7))) & 1 != 0
                        i += 1
            right -= 2

    def apply_mask(self, mask: int):
        f = [lambda x, y: (x + y) % 2 == 0, lambda x, y: y % 2 == 0, lambda x, y: x % 3 == 0,
             lambda x, y: (x + y) % 3 == 0, lambda x, y: (x // 3 + y // 2) % 2 == 0,
             lambda x, y: x * y % 2 + x * y % 3 == 0, lambda x, y: (x * y % 2 + x * y % 3) % 2 == 0,
             lambda x, y: ((x + y) % 2 + x * y % 3) % 2 == 0][mask]
        for y in range(self.size):
            for x in range(self.size):
                if not self.fixed[y][x] and f(x, y):
                    self.dark[y][x] = not self.dark[y][x]

    def penalty(self) -> int:
        n, m = self.size, self.dark
        score = 0

        def runs(line):
            nonlocal score
            run, prev = 0, False                          # a light run before the first module
            hist = [0] * 7
            for v in line:
                if v == prev:
                    run += 1
                    if run == 5:
                        score += 3
                    elif run > 5:
                        score += 1
                else:
                    _finder_history(hist, run, n)
                    if not prev:
                        score += _finder_count(hist) * 40
                    prev, run = v, 1
            score += _finder_terminate(hist, prev, run, n) * 40

        for y in range(n):
            runs(m[y])
        for x in range(n):
            runs([m[y][x] for y in range(n)])
        for y in range(n - 1):
            for x in range(n - 1):
                c = m[y][x]
                if c == m[y][x + 1] == m[y + 1][x] == m[y + 1][x + 1]:
                    score += 3
        dark = sum(sum(r) for r in m)
        total = n * n
        k = (abs(dark * 20 - total * 10) + total - 1) // total - 1
        return score + k * 10


def _finder_history(hist, run, size):
    if hist[0] == 0:
        run += size                                       # the light border before the first module
    hist.insert(0, run)
    hist.pop()


def _finder_count(hist) -> int:
    n = hist[1]
    core = n > 0 and hist[2] == n and hist[3] == n * 3 and hist[4] == n and hist[5] == n
    return (int(core and hist[0] >= n * 4 and hist[6] >= n) + int(core and hist[6] >= n * 4 and hist[0] >= n))


def _finder_terminate(hist, color, run, size) -> int:
    if color:
        _finder_history(hist, run, size)
        run = 0
    run += size
    _finder_history(hist, run, size)
    return _finder_count(hist)


def modules(data: str | bytes, level: str = "M", version: int | None = None, mask: int | None = None) -> list[list[bool]]:
    """The modules of a QR code holding `data` (text is UTF-8): rows top to bottom, True for dark. The smallest version
    that holds it at `level` (L, M, Q, H: 7, 15, 25, 30% of it can be damaged), unless `version` is given."""
    raw = data.encode("utf-8") if isinstance(data, str) else bytes(data)
    level = level.upper()
    if level not in LEVELS:
        raise ValueError(f"QR error correction is L, M, Q or H, not {level!r}")
    for ver in ([version] if version else range(1, 41)):
        count_bits = 8 if ver < 10 else 16
        need = 4 + count_bits + 8 * len(raw)
        if need <= _data_codewords(ver, level) * 8:
            break
    else:
        raise ValueError(f"{len(raw)} bytes don't fit in a QR code at level {level}")
    cap = _data_codewords(ver, level) * 8
    if need > cap:
        raise ValueError(f"{len(raw)} bytes don't fit in a version {ver} QR code at level {level}")
    bits = [0, 1, 0, 0]                                  # byte mode
    bits += [(len(raw) >> i) & 1 for i in range(count_bits - 1, -1, -1)]
    for b in raw:
        bits += [(b >> i) & 1 for i in range(7, -1, -1)]
    bits += [0] * min(4, cap - len(bits))
    bits += [0] * (-len(bits) % 8)
    words = [int("".join(map(str, bits[i:i + 8])), 2) for i in range(0, len(bits), 8)]
    pad = 0xEC
    while len(words) < cap // 8:
        words.append(pad)
        pad ^= 0xEC ^ 0x11
    m = _Matrix(ver)
    m.function_patterns()
    m.codewords(_interleaved(words, ver, level))
    if mask is None:
        best = None
        for k in range(8):
            m.apply_mask(k)
            m.format_bits(LEVELS[level], k)
            p = m.penalty()
            if best is None or p < best[0]:
                best = (p, k)
            m.apply_mask(k)                              # masking twice takes it off again
        mask = best[1]
    m.apply_mask(mask)
    m.format_bits(LEVELS[level], mask)
    return m.dark
