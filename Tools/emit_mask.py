"""从模型贴图里找出“亮着的窗 / 灯”（暖黄、高饱和、高亮）做自发光贴图：
python3 Tools/emit_mask.py <albedo.png> <emit.png> [--min-v 0.72]
输出：发光处 = 原色（提亮），其余黑；边缘略羽化。返回发光像素占比。"""
import sys
import numpy as np
from PIL import Image, ImageFilter

src, dst = sys.argv[1], sys.argv[2]
min_v = float(sys.argv[sys.argv.index('--min-v') + 1]) if '--min-v' in sys.argv else 0.72
im = Image.open(src).convert('RGB')
hsv = np.asarray(im.convert('HSV')).astype(np.float32) / 255
h, s, v = hsv[..., 0] * 360, hsv[..., 1], hsv[..., 2]
m = ((h > 28) & (h < 58) & (s > 0.42) & (v > min_v)).astype(np.float32)
mi = Image.fromarray((m * 255).astype(np.uint8)).filter(ImageFilter.MaxFilter(3)).filter(ImageFilter.GaussianBlur(1.2))
m = np.asarray(mi).astype(np.float32)[..., None] / 255
rgb = np.asarray(im).astype(np.float32)
out = np.clip(rgb * 1.15 * m + 255 * 0.08 * m, 0, 255).astype(np.uint8)
Image.fromarray(out).save(dst)
print(f'{float((m > 0.5).mean()) * 100:.2f}%')
