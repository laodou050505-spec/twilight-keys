"""从手绘贴图的明暗推一张法线贴图（木纹凹凸、金属锤纹）：python3 Tools/make_normal.py <输入.png> <输出.png> [强度=2.0] [模糊=1.5]
亮 = 凸。按平铺方式取边（wrap），所以无缝贴图出来的法线也是无缝的。"""
import sys, cv2, numpy as np
src, dst = sys.argv[1], sys.argv[2]
k = float(sys.argv[3]) if len(sys.argv) > 3 else 2.0
blur = float(sys.argv[4]) if len(sys.argv) > 4 else 1.5
g = cv2.cvtColor(cv2.imread(src), cv2.COLOR_BGR2GRAY).astype(np.float32) / 255
P = 32                                                   # OpenCV 的模糊不支持 BORDER_WRAP：先按平铺补边，算完裁掉
g = np.pad(g, P, mode='wrap')
g = cv2.GaussianBlur(g, (0, 0), blur)
dx = cv2.Sobel(g, cv2.CV_32F, 1, 0, ksize=3)[P:-P, P:-P]
dy = cv2.Sobel(g, cv2.CV_32F, 0, 1, ksize=3)[P:-P, P:-P]
g = g[P:-P, P:-P]
n = np.dstack([-dx * k, dy * k, np.ones_like(g)])      # 图像 y 向下 → 法线 y 取反（OpenGL 约定，Unity 用）
n /= np.linalg.norm(n, axis=2, keepdims=True)
rgb = ((n * 0.5 + 0.5) * 255).astype(np.uint8)
cv2.imwrite(dst, rgb[..., ::-1])
print(dst, 'z 均值', float(n[..., 2].mean()))
