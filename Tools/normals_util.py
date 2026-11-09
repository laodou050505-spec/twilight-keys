"""法线朝向工具（被 blender_repair_min / blender_process / proc_audit 共用）：
recalc_face_normals 在自交、多层壳的卡通模型上会猜错朝外方向。这里按连通块做射线奇偶投票：
从面中心沿法线方向发射线，与本连通块相交次数为奇数 → 法线指向体内（朝里）。多数朝里就整块翻转。"""
import bmesh
from mathutils import Vector
from mathutils.bvhtree import BVHTree


def components(b):
    b.faces.ensure_lookup_table()
    seen = set(); out = []
    for f in b.faces:
        if f.index in seen: continue
        st = [f]; seen.add(f.index); c = []
        while st:
            g = st.pop(); c.append(g)
            for e in g.edges:
                for h in e.link_faces:
                    if h.index not in seen: seen.add(h.index); st.append(h)
        out.append(c)
    return out


def _parity_inward(bvh, f, eps):
    n = f.normal
    if n.length < 0.5: return None
    o = f.calc_center_median() + n * eps
    hits = 0
    for _ in range(64):
        loc, nrm, idx, dist = bvh.ray_cast(o, n, 1e4)
        if loc is None: break
        hits += 1; o = loc + n * eps
    return hits % 2 == 1


def inward_faces(b, size, samples=600):
    """返回 (朝里的面数估计, 抽样数)，按连通块各自建 BVH"""
    bad = 0; tot = 0
    for c in components(b):
        verts = sorted({v for f in c for v in f.verts}, key=lambda v: v.index)
        vi = {v: i for i, v in enumerate(verts)}
        bvh = BVHTree.FromPolygons([v.co.copy() for v in verts], [[vi[v] for v in f.verts] for f in c])
        step = max(1, len(b.faces) // max(1, samples))
        for f in c[::step]:
            r = _parity_inward(bvh, f, size * 1e-5)
            if r is None: continue
            tot += 1; bad += r
    return bad, tot


def signed_volume(faces):
    """封闭连通块的有向体积（散度定理）：法线朝外为正。对自交模型也基本可靠（多数体积决定符号）"""
    vol = 0.0
    for f in faces:
        vs = [v.co for v in f.verts]
        for i in range(1, len(vs) - 1):
            vol += vs[0].dot(vs[i].cross(vs[i + 1]))
    return vol / 6.0


def orient_outward(b, size):
    """先让每块内部朝向一致，再按有向体积决定是否整块翻转。返回翻转的块数"""
    bmesh.ops.recalc_face_normals(b, faces=b.faces)
    flipped = 0
    for c in components(b):
        if signed_volume(c) < 0:
            bmesh.ops.reverse_faces(b, faces=c)
            flipped += 1
    return flipped


def inward_components(b):
    """有向体积为负（整体朝里）的连通块：返回 (朝里的面数, 总面数)"""
    bad = 0
    for c in components(b):
        if signed_volume(c) < 0: bad += len(c)
    return bad, len(b.faces)
