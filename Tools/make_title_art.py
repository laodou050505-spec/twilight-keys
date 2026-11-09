"""第十三轮：盖内标题牌的贴图（Tex/title_art.png）。用法：python3 Tools/make_title_art.py（在工程根目录）。
象牙白瓷底 + 一圈双金线（跟着牌子的超椭圆轮廓）+ 凸起的金箔字“黄昏琴村”（霞鹜文楷，和全游戏同一款字）+ 金色花饰分隔线 + 拉开字距的 TWILIGHT KEYS。
字的立体感是算出来的：字形遮罩模糊后当高度图，按左上方来光算明暗（浮雕 / emboss），再叠一点镜面高光和投影。
牌子轮廓参数要和 Tools/blender_musicbox.py 里 TP_W / TP_D / TP_N 一致。"""
import os
import numpy as np
from PIL import Image, ImageDraw, ImageFont
from scipy import ndimage

ROOT = os.getcwd()
FONT = os.path.join(ROOT, 'ArtSource/Fonts/LXGWWenKai-Regular.ttf')
OUT = os.path.join(ROOT, 'Assets/_Twilight/Art/Models/MusicBox/Tex/title_art.png')
TP_W, TP_D, TP_N = 0.114, 0.036, 3.2
W = 1536
H = int(round(W * TP_D / TP_W))
SS = 2                      # 先画 2 倍再缩小（抗锯齿）
w, h = W * SS, H * SS

yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
u = (xx + 0.5) / w * 2 - 1          # -1..1
v = (yy + 0.5) / h * 2 - 1


def se(scale):
    """超椭圆内部：|u/s|^n + |v/s'|^n <= 1（s 按毫米往里收）。"""
    sx = 1 - scale / (TP_W / 2)
    sy = 1 - scale / (TP_D / 2)
    return (np.abs(u / sx) ** TP_N + np.abs(v / sy) ** TP_N) <= 1


# ---- 底：象牙白瓷，中间亮、往边上微微压暗，上方一道很淡的釉光 ----
ivory = np.array([0.965, 0.935, 0.870], np.float32)
r = np.sqrt(u ** 2 + (v * 1.2) ** 2)
img = ivory[None, None, :] * (1.0 - 0.07 * np.clip(r, 0, 1.4) ** 2)[..., None]
img += (0.03 * np.exp(-((v + 0.55) / 0.18) ** 2))[..., None]

# ---- 双金线边框 ----
def ring(a, b):
    return (se(a) & ~se(b)).astype(np.float32)


border = ring(0.0018, 0.0026) + ring(0.0033, 0.0036)
border = np.clip(border, 0, 1)


def text_mask(txt, size, cy, spacing=0.0, font=FONT):
    f = ImageFont.truetype(font, size)
    m = Image.new('L', (w, h), 0)
    d = ImageDraw.Draw(m)
    widths = [d.textlength(c, font=f) for c in txt]
    total = sum(widths) + spacing * size * (len(txt) - 1)
    x = (w - total) / 2
    for c, cw in zip(txt, widths):
        bb = d.textbbox((0, 0), c, font=f)
        d.text((x, cy - (bb[1] + bb[3]) / 2), c, font=f, fill=255)
        x += cw + spacing * size
    return np.asarray(m, np.float32) / 255


title = text_mask('黄昏琴村', int(h * 0.46), h * 0.39, spacing=0.14)
title = np.clip(ndimage.grey_dilation(title, size=(3, 3)), 0, 1)        # 文楷偏细：加粗一点，金箔才有面积
sub = text_mask('TWILIGHT  KEYS', int(h * 0.15), h * 0.80, spacing=0.3)

# ---- 花饰分隔线：两边渐细的金线 + 中间一颗菱形 + 两颗小点 ----
orn = np.zeros((h, w), np.float32)
cy = h * 0.655
half = w * 0.2
t = np.clip(1 - np.abs(xx - w / 2) / half, 0, 1)
thick = 1.2 * SS + 2.2 * SS * t
orn += ((np.abs(yy - cy) < thick / 2) & (np.abs(xx - w / 2) > 18 * SS) & (np.abs(xx - w / 2) < half)).astype(np.float32)
orn += ((np.abs(xx - w / 2) / (12 * SS) + np.abs(yy - cy) / (7 * SS)) <= 1).astype(np.float32)
for sx in (-1, 1):
    orn += (((xx - (w / 2 + sx * (half + 10 * SS))) ** 2 + (yy - cy) ** 2) <= (3.2 * SS) ** 2).astype(np.float32)
orn = np.clip(orn, 0, 1)


def gilt(mask, blur, light=(-0.55, -0.75), depth=1.0):
    """金箔：遮罩模糊当高度，按左上来光算浮雕明暗 → 暗金到亮金的渐变 + 镜面高光。"""
    hgt = ndimage.gaussian_filter(mask, blur)
    gy, gx = np.gradient(hgt)
    n = np.stack([-gx * depth * 12, -gy * depth * 12, np.ones_like(hgt)], -1)
    n /= np.linalg.norm(n, axis=-1, keepdims=True)
    L = np.array([light[0], light[1], 0.6]); L /= np.linalg.norm(L)
    lam = np.clip((n * L).sum(-1), 0, 1)
    lo = np.array([0.40, 0.25, 0.08]); mid = np.array([0.70, 0.50, 0.20]); hi = np.array([0.90, 0.76, 0.46])
    k = lam[..., None]
    col = np.where(k < 0.75, lo + (mid - lo) * (k / 0.75), mid + (hi - mid) * ((k - 0.75) / 0.25))
    spec = np.clip((n * np.array([0.0, 0.0, 1.0]) * 0 + (n * (L + np.array([0, 0, 1])) / np.linalg.norm(L + np.array([0, 0, 1])))).sum(-1), 0, 1) ** 40
    col = col + spec[..., None] * np.array([1.0, 0.95, 0.85]) * 0.25
    # 竖直方向再叠一层金箔的冷暖变化（上浅下深）
    col *= (1.06 - 0.12 * (yy / h))[..., None]
    return np.clip(col, 0, 1)


def comp(base, mask, col, shadow=True, outline=True):
    if shadow:
        sh = ndimage.gaussian_filter(np.roll(np.roll(mask, 3 * SS, 0), 2 * SS, 1), 2.5 * SS)
        base = base * (1 - 0.22 * sh[..., None])
    if outline:
        ol = np.clip(ndimage.grey_dilation(mask, size=(2 * SS + 1, 2 * SS + 1)) - mask, 0, 1)
        base = base * (1 - 0.55 * ol[..., None]) + np.array([0.36, 0.22, 0.08]) * 0.55 * ol[..., None]
    return base * (1 - mask[..., None]) + col * mask[..., None]


# 第十六轮（用户：标题太丑；英文模式标题也要英文）：中英各一张，只写一行大标题 + 下面一道细花饰；
# 去掉双层金框（牌子边缘只有瓷面自然压暗），字更大、金箔更沉稳（不发亮）。
def bake(text, size_k, spacing, outname, cy_k):
    t = text_mask(text, int(h * size_k), h * cy_k, spacing=spacing)
    t = np.clip(ndimage.grey_dilation(t, size=(3, 3)), 0, 1)
    o = np.zeros((h, w), np.float32)
    cy = h * 0.83
    half = w * 0.17
    tt = np.clip(1 - np.abs(xx - w / 2) / half, 0, 1)
    thick = 0.9 * SS + 1.8 * SS * tt
    o += ((np.abs(yy - cy) < thick / 2) & (np.abs(xx - w / 2) > 14 * SS) & (np.abs(xx - w / 2) < half)).astype(np.float32)
    o += ((np.abs(xx - w / 2) / (9 * SS) + np.abs(yy - cy) / (5 * SS)) <= 1).astype(np.float32)
    o = np.clip(o, 0, 1)
    im = comp(img.copy(), o, gilt(o, 1.2 * SS, depth=0.7), shadow=True, outline=False)
    im = comp(im, t, gilt(t, 2.0 * SS, depth=0.9))
    out = Image.fromarray((np.clip(im * 0.97, 0, 1) * 255).astype(np.uint8)).resize((W, H), Image.LANCZOS)
    path = os.path.join(os.path.dirname(OUT), outname)
    out.save(path)
    print('saved', path, out.size)


bake('黄昏琴村', 0.56, 0.16, 'title_zh.png', 0.46)
bake('Twilight Keys', 0.37, 0.03, 'title_en.png', 0.48)
