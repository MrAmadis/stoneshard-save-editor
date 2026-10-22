"""Spike: extract a few inventory sprites from Stoneshard data.win (GM QOI+BZ2 textures)."""
import bz2
import struct
import sys

from PIL import Image

DATA_WIN, OUT_DIR, *WANTED = sys.argv[1:]

data = open(DATA_WIN, 'rb').read()
chunks = {}
pos = 8
while pos < len(data):
    name = data[pos:pos + 4].decode()
    size = struct.unpack_from('<I', data, pos + 4)[0]
    chunks[name] = (pos + 8, size)
    pos += 8 + size

u32 = lambda o: struct.unpack_from('<I', data, o)[0]
u16 = lambda o: struct.unpack_from('<H', data, o)[0]


def gm_str(o):
    return data[o:o + u32(o - 4)].decode('utf8', 'replace')


def sx(v, bits):
    v &= (1 << bits) - 1
    return v - (1 << bits) if v >> (bits - 1) else v


def decode_qoi(buf):
    assert buf[:4] == b'fioq', buf[:4]
    w, h = struct.unpack_from('<HH', buf, 4)
    length = struct.unpack_from('<I', buf, 8)[0]
    p, end = 12, 12 + length
    out = bytearray(w * h * 4)
    index = [0] * 256
    r = g = b = 0
    a = 255
    run = 0
    for i in range(0, w * h * 4, 4):
        if run > 0:
            run -= 1
        elif p < end:
            b1 = buf[p]; p += 1
            if b1 & 0xC0 == 0x00:
                ip = (b1 & 0x3F) << 2
                r, g, b, a = index[ip], index[ip + 1], index[ip + 2], index[ip + 3]
            elif b1 & 0xE0 == 0x40:
                run = b1 & 0x1F
            elif b1 & 0xE0 == 0x60:
                run = (((b1 & 0x1F) << 8) | buf[p]) + 32; p += 1
            elif b1 & 0xC0 == 0x80:
                r = (r + sx(b1 >> 4, 2)) & 255
                g = (g + sx(b1 >> 2, 2)) & 255
                b = (b + sx(b1, 2)) & 255
            elif b1 & 0xE0 == 0xC0:
                m = (b1 << 8) | buf[p]; p += 1
                r = (r + sx(m >> 8, 5)) & 255
                g = (g + sx(m >> 4, 4)) & 255
                b = (b + sx(m, 4)) & 255
            elif b1 & 0xF0 == 0xE0:
                m = (b1 << 16) | (buf[p] << 8) | buf[p + 1]; p += 2
                r = (r + sx(m >> 15, 5)) & 255
                g = (g + sx(m >> 10, 5)) & 255
                b = (b + sx(m >> 5, 5)) & 255
                a = (a + sx(m, 5)) & 255
            else:
                if b1 & 8: r = buf[p]; p += 1
                if b1 & 4: g = buf[p]; p += 1
                if b1 & 2: b = buf[p]; p += 1
                if b1 & 1: a = buf[p]; p += 1
            ip = ((r ^ g ^ b ^ a) & 63) << 2
            index[ip:ip + 4] = [r, g, b, a]
        out[i:i + 4] = bytes((r, g, b, a))
    return Image.frombytes('RGBA', (w, h), bytes(out))


tex_cache = {}


def texture(idx):
    if idx in tex_cache:
        return tex_cache[idx]
    t0, _ = chunks['TXTR']
    entry = u32(t0 + 4 + 4 * idx)
    blob_off = u32(entry + 24)
    blob = data[blob_off:blob_off + 64 * 1024 * 1024]
    if blob[:4] == b'2zoq':
        bz_start = blob.index(b'BZh')
        qoi = bz2.BZ2Decompressor().decompress(blob[bz_start:])
        img = decode_qoi(qoi)
    elif blob[:4] == b'fioq':
        img = decode_qoi(blob)
    else:
        import io
        img = Image.open(io.BytesIO(blob)).convert('RGBA')
    tex_cache[idx] = img
    return img


s0, _ = chunks['SPRT']
sprites = {}
for i in range(u32(s0)):
    e = u32(s0 + 4 + 4 * i)
    sprites[gm_str(u32(e))] = e

o0, _ = chunks['OBJT']
obj_sprite = {}
sprite_names = list(sprites)
for i in range(u32(o0)):
    e = u32(o0 + 4 + 4 * i)
    idx = struct.unpack_from('<i', data, e + 4)[0]
    if idx >= 0:
        obj_sprite[gm_str(u32(e))] = sprite_names[idx]

for name in WANTED:
    if name.startswith('o_'):
        out_name, name = name, obj_sprite[name]
    else:
        out_name = name
    e = sprites[name]
    w, h = u32(e + 4), u32(e + 8)
    version = u32(e + 60)
    count_off = e + 76 + (4 if version >= 2 else 0) + (4 if version >= 3 else 0)
    n_frames = u32(count_off)
    tpag = u32(count_off + 4)
    src_x, src_y, src_w, src_h, tgt_x, tgt_y, tgt_w, tgt_h, bw, bh, tex = struct.unpack_from('<11H', data, tpag)
    page = texture(tex)
    frame = page.crop((src_x, src_y, src_x + src_w, src_y + src_h))
    canvas = Image.new('RGBA', (bw, bh))
    canvas.paste(frame.resize((tgt_w, tgt_h)), (tgt_x, tgt_y))
    canvas.save(f'{OUT_DIR}/{out_name}.png')
    print(out_name, name, 'frames', n_frames, 'size', (bw, bh), 'tex', tex)
