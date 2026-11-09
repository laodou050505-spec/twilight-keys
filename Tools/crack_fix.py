"""缝隙修复（Blender 里运行）：Blender -b -P Tools/crack_fix.py -- <a.fbx> [...]
导出→导入后仍存在的小边界环，几乎都是"零面积缝"：T 形接缝（一个顶点落在另一条边上）或两个几乎重合的顶点。
直接补面会补出退化三角形，FBX 导入端会丢掉它们，所以洞又回来。这里只处理边界环本身：
  1. 边界顶点离另一条边界边很近（< 缝宽阈值）→ 在投影点切开那条边，再把两点焊在一起（消掉 T 形缝）；
  2. 剩下的边界环里距离很近的两个顶点 → 焊接；
  3. 仍有的边界环（真小孔）→ 补面 + 三角化。
不碰边界环以外的任何面。覆盖写回原文件（坐标轴设置与管线一致），再导入复查。"""
import bpy, bmesh, sys
from mathutils import Vector
from mathutils.geometry import intersect_point_line
import os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from normals_util import orient_outward

files = sys.argv[sys.argv.index('--') + 1:]


def bad(bm):
    return [e for e in bm.edges if len(e.link_faces) != 2]


def groups(bm):
    """按"只跨越恰好两个面的边"划分面组（非流形边不连通）"""
    gid = {}; k = 0
    for f0 in bm.faces:
        if f0 in gid: continue
        st = [f0]; gid[f0] = k
        while st:
            f = st.pop()
            for e in f.edges:
                if len(e.link_faces) != 2: continue
                for g in e.link_faces:
                    if g not in gid: gid[g] = k; st.append(g)
        k += 1
    return gid


def despike(bm):
    """尖刺：某顶点所有连边都远长于全模中位边长（> 25 倍）→ 把它拉回邻点平均位置。返回处理数"""
    import statistics
    med = statistics.median(e.calc_length() for e in bm.edges)
    n = 0
    for _ in range(5):
        hit = [v for v in bm.verts if v.link_edges and min(e.calc_length() for e in v.link_edges) > 25 * med]
        if not hit: break
        for v in hit:
            nb = [e.other_vert(v).co for e in v.link_edges]
            v.co = sum(nb, Vector()) / len(nb); n += 1
    return n


def fix(bm, tol):
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-6)
    print('DESPIKE', despike(bm))
    # 0a) 游离的小开放面片（几片三角形组成的单面薄片）直接删掉
    gid = groups(bm); members = {}
    for f, g in gid.items(): members.setdefault(g, []).append(f)
    kill = [f for g, fs in members.items() if len(fs) < 16 and any(len(e.link_faces) == 1 for f in fs for e in f.edges) for f in fs]
    if kill:
        bmesh.ops.delete(bm, geom=kill, context='FACES')
        lv = [v for v in bm.verts if not v.link_faces]
        if lv: bmesh.ops.delete(bm, geom=lv, context='VERTS')
    # 0b) 两个封闭体在一条边上相接（一条边挂 4 个面）→ 把边和端点拆开，第二个体的拆出点往自身内部挪 0.5mm
    for e in [e for e in bm.edges if len(e.link_faces) > 2]:
        if not e.is_valid: continue
        gid = groups(bm)
        fs = list(e.link_faces); gs = sorted({gid[f] for f in fs})
        if len(gs) < 2: continue
        g2 = set(f for f in gid if gid[f] == gs[1])
        cen = sum((f.calc_center_median() for f in g2), Vector()) / len(g2)
        ends = [e.verts[0].co.copy(), e.verts[1].co.copy()]
        bmesh.ops.split_edges(bm, edges=[e], use_verts=True)
        moved = set()
        for f in g2:
            for v in f.verts:
                if v in moved or any(g not in g2 for g in v.link_faces): continue
                if min((v.co - p).length for p in ends) < 1e-6:
                    v.co += (cen - v.co).normalized() * 0.0005; moved.add(v)
    for _ in range(30):
        bnd = [e for e in bm.edges if len(e.link_faces) == 1]
        if not bnd: break
        bverts = {v for e in bnd for v in e.verts}
        done = False
        # 1) T 形缝：顶点到非相邻边界边的距离
        for v in bverts:
            for e in bnd:
                if v in e.verts or not e.is_valid: continue
                p, t = intersect_point_line(v.co, e.verts[0].co, e.verts[1].co)
                if 0.001 < t < 0.999 and (p - v.co).length < tol:
                    ne, nv = bmesh.utils.edge_split(e, e.verts[0], t)
                    bmesh.ops.pointmerge(bm, verts=[v, nv], merge_co=v.co)
                    done = True; break
            if done: break
        if done: continue
        # 2) 近重合顶点
        bl = list(bverts); pair = None; best = tol
        for i in range(len(bl)):
            for j in range(i + 1, len(bl)):
                d = (bl[i].co - bl[j].co).length
                if d < best and not any(bl[j] in e.verts for e in bl[i].link_edges): best, pair = d, (bl[i], bl[j])
        if pair:
            bmesh.ops.pointmerge(bm, verts=list(pair), merge_co=(pair[0].co + pair[1].co) / 2); continue
        break
    # 3) 剩余小孔：沿边界走出有序环，手工建面（holes_fill 遇到边界顶点非流形时会直接放弃）
    for _ in range(20):
        bnd = [e for e in bm.edges if len(e.link_faces) == 1]
        if not bnd: break
        e0 = bnd[0]; f0 = e0.link_faces[0]
        # 顺着相邻面的反方向走（新面与邻面朝向一致）
        lp = next(l for l in f0.loops if l.edge == e0)
        start, cur = lp.link_loop_next.vert, lp.vert     # 新面里这条边方向与邻面相反
        ring = [start, cur]; ok = True
        while True:
            nxt = [e for e in cur.link_edges if len(e.link_faces) == 1 and e.other_vert(cur) != ring[-2]]
            if not nxt: ok = False; break
            v = nxt[0].other_vert(cur)
            if v == start: break
            if v in ring or len(ring) > 64: ok = False; break
            ring.append(v); cur = v
        if not ok: break
        # 薄条形环（两条短"端边"把环分成两条长链）→ 拉链式成对连四边形，避免大 n 边形三角化后与已有面重合
        n = len(ring)
        L = [(ring[i].co - ring[(i + 1) % n].co).length for i in range(n)]
        srt = sorted(range(n), key=lambda i: L[i])
        made = []
        if n >= 6 and L[srt[1]] < 0.25 * (sum(L) / n):
            a, b = sorted(srt[:2])
            c1 = ring[a + 1:b + 1]; c2 = (ring[b + 1:] + ring[:a + 1])[::-1]
            if len(c1) == len(c2):
                for i in range(len(c1) - 1):
                    try: made.append(bm.faces.new((c1[i], c1[i + 1], c2[i + 1], c2[i])))
                    except ValueError: pass
        if not made:
            try: made = [bm.faces.new(ring)]
            except ValueError: break
        bmesh.ops.triangulate(bm, faces=made, quad_method='BEAUTY', ngon_method='BEAUTY')
        bm.normal_update()
    # 清掉退化面；每个封闭块按有向体积统一朝外
    bmesh.ops.dissolve_degenerate(bm, dist=1e-7, edges=bm.edges)
    orient_outward(bm, 1.0)


for fp in files:
    bpy.ops.wm.read_factory_settings(use_empty=True); bpy.ops.import_scene.fbx(filepath=fp)
    objs = [o for o in bpy.context.scene.objects if o.type == 'MESH']
    before = 0
    for o in objs:
        bm = bmesh.new(); bm.from_mesh(o.data); bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-6)
        before += len(bad(bm))
        tol = max(o.dimensions) * 0.004
        fix(bm, tol)
        bm.to_mesh(o.data); bm.free()
        for p in o.data.polygons: p.use_smooth = True
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs: o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.export_scene.fbx(filepath=fp, use_selection=True, apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL',
                             axis_forward='-Z', axis_up='Y', object_types={'MESH'}, mesh_smooth_type='FACE',
                             bake_space_transform=False, colors_type='LINEAR')
    bpy.ops.wm.read_factory_settings(use_empty=True); bpy.ops.import_scene.fbx(filepath=fp)
    after = 0
    for o in bpy.context.scene.objects:
        if o.type == 'MESH':
            bm = bmesh.new(); bm.from_mesh(o.data); bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-6); after += len(bad(bm)); bm.free()
    print('CRACK', fp.split('/')[-1], 'before', before, 'after', after)
