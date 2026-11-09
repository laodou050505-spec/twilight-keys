"""手绘木牌图（白底）→ 透明底贴图 + 外轮廓（归一化坐标，供 Blender 挤出成牌子）。
用法：python3 Tools/cutout_board.py <输入 png> <输出前缀>   → <前缀>.png（RGBA，最长边 2048）+ <前缀>_outline.json"""
import json, sys
import cv2
import numpy as np

src, out = sys.argv[1], sys.argv[2]
img = cv2.imread(src, cv2.IMREAD_COLOR)
h, w = img.shape[:2]
# 白底：从四周泛洪（容差），剩下的就是木牌
mask = np.zeros((h + 2, w + 2), np.uint8)
ff = img.copy()
for seed in [(0, 0), (w - 1, 0), (0, h - 1), (w - 1, h - 1)]:
    cv2.floodFill(ff, mask, seed, (255, 0, 255), (18, 18, 18), (18, 18, 18), cv2.FLOODFILL_FIXED_RANGE | (255 << 8))
bg = mask[1:-1, 1:-1] > 0
fg = (~bg).astype(np.uint8) * 255
fg = cv2.morphologyEx(fg, cv2.MORPH_OPEN, np.ones((5, 5), np.uint8))
# 只留最大的一块
n, lab, stats, _ = cv2.connectedComponentsWithStats(fg)
big = 1 + np.argmax(stats[1:, cv2.CC_STAT_AREA])
fg = np.where(lab == big, 255, 0).astype(np.uint8)
fg = cv2.morphologyEx(fg, cv2.MORPH_CLOSE, np.ones((9, 9), np.uint8))
# 裁到包围盒（留 8 px）
ys, xs = np.where(fg > 0)
x0, x1, y0, y1 = max(xs.min() - 8, 0), min(xs.max() + 9, w), max(ys.min() - 8, 0), min(ys.max() + 9, h)
crop, m = img[y0:y1, x0:x1], fg[y0:y1, x0:x1]
# 边缘往外“出血”一点颜色（防 mip 时白边）
inner = cv2.erode(m, np.ones((3, 3), np.uint8))
bled = cv2.inpaint(crop, 255 - inner, 6, cv2.INPAINT_TELEA)
alpha = cv2.GaussianBlur(m, (3, 3), 0)
rgba = np.dstack([bled, alpha])
ch, cw = rgba.shape[:2]
s = 2048 / max(ch, cw)
rgba = cv2.resize(rgba, (int(cw * s), int(ch * s)), interpolation=cv2.INTER_AREA)
cv2.imwrite(out + '.png', rgba)
# 外轮廓：简化到约 1.5 px 误差，坐标归一化到 [0,1]（y 向上）
cs, _ = cv2.findContours(m, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_NONE)
c = max(cs, key=cv2.contourArea)
c = cv2.approxPolyDP(c, 1.5 * max(ch, cw) / 1000, True)[:, 0, :]
pts = [[float(x) / cw, 1.0 - float(y) / ch] for x, y in c]
json.dump({'aspect': cw / ch, 'points': pts}, open(out + '_outline.json', 'w'))
print('尺寸', rgba.shape[1], 'x', rgba.shape[0], '轮廓点', len(pts), '宽高比', round(cw / ch, 3))
