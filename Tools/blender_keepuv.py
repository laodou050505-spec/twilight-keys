"""保留原 UV / 原贴图的轻修复（Blender 里运行）：Blender -b -P Tools/blender_keepuv.py -- <clean.glb> <out.glb>
体素重建 + 重新烘焙会把 P2 的贴图烤糊、烤花（墙面贴片之间有缝，烘焙射线穿缝打到内侧），所以村庄这类远景模型改走这条路：
  1. 焊接重复点（glTF 在 UV 接缝处拆开的点；UV 存在面角上，不受影响）
  2. 删退化面、孤立点
  3. 所有边界环补面（≤ 400 边），新面的 UV 取同一顶点上原有面角的 UV（不会取到贴图上无关的颜色）
  4. 重算法线朝外
原 UV、原贴图（≤2048 由 p2_lods 保存）不动。输出 KEEPUV {...} 一行报告。"""
import bpy, bmesh, json, sys
argv = sys.argv[sys.argv.index('--') + 1:]
src, dst = argv[0], argv[1]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
ms = [o for o in bpy.context.scene.objects if o.type == 'MESH']
bpy.ops.object.select_all(action='DESELECT')
for o in ms: o.select_set(True)
bpy.context.view_layer.objects.active = ms[0]
if len(ms) > 1: bpy.ops.object.join()
o = bpy.context.view_layer.objects.active
bpy.ops.object.parent_clear(type='CLEAR_KEEP_TRANSFORM')
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
for x in list(bpy.context.scene.objects):
    if x != o: bpy.data.objects.remove(x, do_unlink=True)

size = max(o.dimensions)
bm = bmesh.new(); bm.from_mesh(o.data)
uv = bm.loops.layers.uv.active
def stats():
    return dict(faces=len(bm.faces), boundary=sum(1 for e in bm.edges if len(e.link_faces) == 1),
                nonmanifold=sum(1 for e in bm.edges if len(e.link_faces) > 2))
before = None
bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=size * 1e-5)
bmesh.ops.dissolve_degenerate(bm, dist=size * 1e-6, edges=bm.edges)
loose = [v for v in bm.verts if not v.link_faces]
if loose: bmesh.ops.delete(bm, geom=loose, context='VERTS')
before = stats()
old = set(bm.faces)
new_faces = []
fill_patches = []        # [(该洞补出的面, 洞边一圈顶点)]
unfilled = 0
def uv_of(v):
    l2 = next((l for l in v.link_loops if l.face in old), None)
    return l2[uv].uv.copy() if (l2 is not None and uv is not None) else None
for _round in range(3):
    bm.edges.ensure_lookup_table()
    bnd = [e for e in bm.edges if len(e.link_faces) == 1]
    if not bnd: break
    # 开放边连成闭合环才是洞（P2 里大多数开放边是贴片 / 薄板的自由边，连不成环，看不出破，不用补）。（不看方向：P2 的面朝向在非流形处常常不一致）
    adj = {}
    for e in bnd:
        for v in e.verts: adj.setdefault(v, []).append(e)
    used_e = set()
    for e0 in bnd:
        if e0 in used_e: continue
        loop = [e0.verts[0], e0.verts[1]]; edges = [e0]; used_e.add(e0); ok = False
        while len(loop) < 500:
            tail = loop[-1]
            cands = [e for e in adj.get(tail, []) if e not in used_e]
            if not cands: break
            e = cands[0]; used_e.add(e); edges.append(e)
            w = e.other_vert(tail)
            if w == loop[0]: ok = True; break
            if w in loop:                 # 打了个结：只取结之后的那一圈
                loop = loop[loop.index(w):]; ok = True; break
            loop.append(w)
        if not ok or len(loop) < 3: continue
        # 朝向：和第一条边所在的原面相反
        f0 = e0.link_faces[0]
        for l in f0.loops:
            if l.edge == e0 and l.vert == loop[0] and l.link_loop_next.vert == loop[1]:
                loop.reverse(); break
        try:
            made = []
            if len(loop) == 3:
                made.append(bm.faces.new(loop))
            else:
                c = bm.verts.new(sum((v.co for v in loop), loop[0].co * 0) / len(loop))
                for i in range(len(loop)):
                    try: made.append(bm.faces.new((loop[i], loop[(i + 1) % len(loop)], c)))
                    except ValueError: pass
            new_faces += made
            fill_patches.append((made, list(loop)))
        except ValueError:
            unfilled += 1
# 补出来的面：一个洞的整片补面统一用同一个 UV（取洞边一圈里亮度偏暗的那个点的颜色），
# 否则每个顶点各取各的 UV，三角形横跨贴图上不相干的区域，会拉出彩色条纹（树冠底面最明显）。
import numpy as np
img = None
for mslot in o.data.materials:
    if mslot and mslot.use_nodes:
        for nd in mslot.node_tree.nodes:
            if nd.type == 'TEX_IMAGE' and nd.image: img = nd.image; break
    if img: break
px = np.array(img.pixels[:], dtype=np.float32).reshape(img.size[1], img.size[0], 4) if img else None
def lum_at(u):
    if px is None or u is None: return 0.5
    x = int((u.x % 1.0) * (px.shape[1] - 1)); y = int((u.y % 1.0) * (px.shape[0] - 1))
    c = px[y, x, :3]; return float(c[0] * 0.3 + c[1] * 0.59 + c[2] * 0.11)
patch_of = {}
for pid, faces in enumerate(fill_patches):
    ring = [v for v in faces[1] if uv_of(v) is not None]
    uvs = sorted((uv_of(v) for v in ring), key=lum_at)
    pick = uvs[int(len(uvs) * 0.3)] if uvs else None
    for f in faces[0]:
        if f.is_valid and pick is not None and uv is not None:
            for l in f.loops: l[uv].uv = pick
new_faces = [f for f in new_faces if f.is_valid]
after = stats()
bm.to_mesh(o.data); bm.free()
for p in o.data.polygons: p.use_smooth = True
bpy.ops.object.select_all(action='DESELECT'); o.select_set(True)
bpy.ops.export_scene.gltf(filepath=dst, use_selection=True, export_format='GLB')
print('KEEPUV ' + json.dumps({'before': before, 'after': after, 'filled': len(new_faces), 'unfilled_loops': unfilled, 'pass': unfilled == 0}))
