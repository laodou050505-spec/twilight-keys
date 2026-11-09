"""把多张小图拼成一张审图用的对照表（只用 macOS 自带的 sips + 纯 Python，不依赖 PIL）。
用法：python3 -I Tools/contact_sheet.py <输出.jpg> <列数> <图1> <图2> ...
做法：先用 sips 把每张缩到统一大小并转 BMP（无压缩，易于拼接），再按行写一张大 BMP，最后用 sips 转 JPG。"""
import os, struct, subprocess, sys, tempfile

out, cols, files = sys.argv[1], int(sys.argv[2]), sys.argv[3:]
S = int(__import__("os").environ.get("SHEET_S", "300"))
tmp = tempfile.mkdtemp()


def load_bmp(path):
    b = open(path, 'rb').read()
    off = struct.unpack_from('<I', b, 10)[0]
    w, h = struct.unpack_from('<ii', b, 18)
    bpp = struct.unpack_from('<H', b, 28)[0]
    bypp = bpp // 8
    row = (w * bypp + 3) & ~3
    px = []
    for y in range(abs(h)):
        ry = (abs(h) - 1 - y) if h > 0 else y
        r = b[off + ry * row: off + ry * row + w * bypp]
        px.append([r[i * bypp:i * bypp + 3] for i in range(w)])
    return w, abs(h), px


tiles = []
for i, f in enumerate(files):
    t = os.path.join(tmp, f'{i}.bmp')
    subprocess.run(['sips', '-s', 'format', 'bmp', '-z', str(S), str(S), f, '--out', t], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    tiles.append(load_bmp(t) if os.path.exists(t) else (S, S, [[b'\x30\x30\x30'] * S for _ in range(S)]))

rows = (len(tiles) + cols - 1) // cols
W, H = cols * S, rows * S
rowbytes = (W * 3 + 3) & ~3
data = bytearray(rowbytes * H)
for idx, (w, h, px) in enumerate(tiles):
    cx, cy = (idx % cols) * S, (idx // cols) * S
    for y in range(min(h, S)):
        dy = H - 1 - (cy + y)
        base = dy * rowbytes + cx * 3
        line = b''.join(px[y][:S])
        data[base:base + len(line)] = line
hdr = b'BM' + struct.pack('<IHHI', 54 + len(data), 0, 0, 54) + struct.pack('<IiiHHIIiiII', 40, W, H, 1, 24, 0, len(data), 2835, 2835, 0, 0)
bmp = os.path.join(tmp, 'sheet.bmp')
open(bmp, 'wb').write(hdr + data)
subprocess.run(['sips', '-s', 'format', 'jpeg', bmp, '--out', out], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
print(out)
