"""天空全景预处理：python3 -I Tools/prep_sky.py
- 裁掉底部生硬色带，横向首尾交叉淡化成 360° 无缝，缩到 8192 宽；
- 三张天空（黄昏 / 余晖 / 夜）竖着拼成 8192×6144 → Unity 里按 4×3 切成 12 片 2048² 的 2D Array；
- 远山：裁掉上方重影，抠绿（带去绿溢色）→ 8192×1600，再切成 4 张 2048×1600。
另输出每张天空顶部 / 地平线平均色（给着色器做天顶与地平线过渡）。"""
import json
import numpy as np
from PIL import Image

SRC, DST = 'ArtSource/Sky', 'Assets/_Twilight/Art/Sky'
W, B = 8192, 384           # 输出宽、首尾交叉淡化宽度

def seamless(a):
    h, w = a.shape[:2]
    t = (np.arange(B) / (B - 1))[None, :, None]
    out = a[:, :w - B].copy()
    out[:, :B] = a[:, w - B:] * (1 - t) + a[:, :B] * t
    return out

def load(n, y0=0, y1=None):
    a = np.asarray(Image.open(f'{SRC}/{n}.png').convert('RGB')).astype(np.float32)
    return a[y0:y1]

info, rows = {}, []
for n, y1 in (('sky_dusk', 2005), ('sky_afterglow', 1820), ('sky_night', None)):
    a = seamless(load(n, 0, y1))
    im = Image.fromarray(a.clip(0, 255).astype(np.uint8)).resize((W, 2048), Image.LANCZOS)
    rows.append(im)
    arr = np.asarray(im).astype(np.float32) / 255
    info[n] = {'zenith': arr[:24].mean((0, 1)).round(4).tolist(), 'horizon': arr[-24:].mean((0, 1)).round(4).tolist()}
atlas = Image.new('RGB', (W, 2048 * 3))
for i, r in enumerate(rows): atlas.paste(r, (0, i * 2048))
atlas.save(f'{DST}/SkyAtlas.png')

# 远山：抠绿
a = load('mountains_band', 450, None)
r, g, b = a[..., 0], a[..., 1], a[..., 2]
green = g - np.maximum(r, b)                       # 绿得越多越透明
alpha = 1 - np.clip((green - 30) / 90, 0, 1)
g2 = np.minimum(g, np.maximum(r, b) + 12)          # 去溢色
rgb = np.stack([r, np.where(alpha < 1, g2, g), b], -1)
rgba = np.concatenate([seamless(rgb), seamless(alpha[..., None] * 255)], -1)
im = Image.fromarray(rgba.clip(0, 255).astype(np.uint8), 'RGBA').resize((W, 1600), Image.LANCZOS)
for i in range(4):
    im.crop((i * 2048, 0, (i + 1) * 2048, 1600)).save(f'{DST}/Mountains_{i}.png')
json.dump(info, open(f'{DST}/sky_colors.json', 'w'), indent=1)
print(json.dumps(info))
