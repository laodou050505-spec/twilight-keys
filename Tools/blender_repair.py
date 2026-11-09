"""拓扑修复（在 Blender 里运行）：
  Blender -b -P Tools/blender_repair.py -- <model.glb> <out.glb> <面数上限> [体素比例=0.0028] [贴图=2048]

Tripo 模型常见问题：开放边界（眼洞/嘴洞只有单层壳、漏面）、非流形边、法线翻转、内部碎片。
修复流程（保证结果是封闭流形，从任何角度看都没有露面/破面）：
  1. 合并为一个物体，复制一份作为“烘焙源”
  2. 向内 Solidify（厚度 = 1.5 体素）：单层壳和开口处长出厚度，体素化时不会丢失或穿孔
  3. Voxel Remesh（体素 = 包围盒最长边 × 比例）：输出必然水密、无自交、法线一致
  4. 删除小于 0.5% 面数的连通块（内部气泡/碎屑）
  5. Decimate 到面数上限，再三角化
  6. Smart UV 展开，从原模型烘焙 Base Color 到新贴图（Cycles，选中到活动）
  7. 审计：边界边、非流形、零面积、翻转面必须全为 0，否则退出码 2
"""
import bpy, bmesh, sys, os, json, math
argv = sys.argv[sys.argv.index('--') + 1:]
src, dst, cap = argv[0], argv[1], int(argv[2])
vox_ratio = float(argv[3]) if len(argv) > 3 else 0.0028
tex_res = int(argv[4]) if len(argv) > 4 else 2048

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
bpy.ops.object.select_all(action='DESELECT')
for o in meshes:
    o.select_set(True)
bpy.context.view_layer.objects.active = meshes[0]
if len(meshes) > 1:
    bpy.ops.object.join()
srcobj = bpy.context.view_layer.objects.active
bpy.ops.object.parent_clear(type='CLEAR_KEEP_TRANSFORM')
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
for o in list(bpy.context.scene.objects):
    if o != srcobj:
        bpy.data.objects.remove(o, do_unlink=True)
srcobj.name = 'bake_src'
dims = srcobj.dimensions
vox = max(dims) * vox_ratio

# ---- 新物体：几何重建 ----
new = srcobj.copy(); new.data = srcobj.data.copy(); new.name = 'repaired'
bpy.context.scene.collection.objects.link(new)
bpy.ops.object.select_all(action='DESELECT'); new.select_set(True); bpy.context.view_layer.objects.active = new
bm = bmesh.new(); bm.from_mesh(new.data)
bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=vox * 0.05)
bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
bm.to_mesh(new.data); bm.free()
m = new.modifiers.new('sol', 'SOLIDIFY'); m.thickness = vox * 1.5; m.offset = -1.0; m.use_even_offset = False; m.use_quality_normals = True
bpy.ops.object.modifier_apply(modifier=m.name)
m = new.modifiers.new('vox', 'REMESH'); m.mode = 'VOXEL'; m.voxel_size = vox; m.adaptivity = 0.0; m.use_smooth_shade = True
bpy.ops.object.modifier_apply(modifier=m.name)

def strip_islands(ob, frac):
    bm = bmesh.new(); bm.from_mesh(ob.data); bm.faces.ensure_lookup_table()
    seen = set(); comps = []
    for f in bm.faces:
        if f.index in seen: continue
        st = [f]; seen.add(f.index); c = []
        while st:
            g = st.pop(); c.append(g)
            for e in g.edges:
                for h in e.link_faces:
                    if h.index not in seen: seen.add(h.index); st.append(h)
        comps.append(c)
    nf = len(bm.faces); kill = [f for c in comps if len(c) < nf * frac for f in c]
    if kill: bmesh.ops.delete(bm, geom=kill, context='FACES')
    bm.to_mesh(ob.data); bm.free()
    return len(comps), len(kill)
islands, killed = strip_islands(new, 0.005)
faces_remesh = len(new.data.polygons)
if faces_remesh > cap:
    m = new.modifiers.new('dec', 'DECIMATE'); m.decimate_type = 'COLLAPSE'; m.ratio = cap / (faces_remesh * 2.0); m.use_collapse_triangulate = True
    bpy.ops.object.modifier_apply(modifier=m.name)
m = new.modifiers.new('tri', 'TRIANGULATE'); bpy.ops.object.modifier_apply(modifier=m.name)

def clean(ob):
    # 减面后偶发的非流形/零面积：删退化、再封小孔、重算法线
    bm = bmesh.new(); bm.from_mesh(ob.data)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=vox * 0.02)
    bmesh.ops.dissolve_degenerate(bm, dist=vox * 0.01, edges=bm.edges)
    bad = [e for e in bm.edges if len(e.link_faces) > 2]
    if bad:
        bmesh.ops.delete(bm, geom=list({f for e in bad for f in e.link_faces}), context='FACES')
    loose = [v for v in bm.verts if not v.link_faces]
    if loose: bmesh.ops.delete(bm, geom=loose, context='VERTS')
    bmesh.ops.holes_fill(bm, edges=bm.edges, sides=64)
    bmesh.ops.triangulate(bm, faces=bm.faces)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(ob.data); bm.free()
def kill_spikes(ob):
    import statistics
    bm = bmesh.new(); bm.from_mesh(ob.data)
    med = statistics.median(e.calc_length() for e in bm.edges)
    bad = [f for f in bm.faces if max(e.calc_length() for e in f.edges) > med * 30]
    if bad:
        bmesh.ops.delete(bm, geom=bad, context='FACES')
        bmesh.ops.holes_fill(bm, edges=bm.edges, sides=64)
        bmesh.ops.triangulate(bm, faces=bm.faces)
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(ob.data); bm.free(); return len(bad)
kill_spikes(new)
def topo_ok(ob):
    bm = bmesh.new(); bm.from_mesh(ob.data)
    ok = all(len(e.link_faces) == 2 for e in bm.edges) and all(f.calc_area() > 1e-12 for f in bm.faces)
    bm.free(); return ok
for _ in range(3):
    clean(new)
# 兜底：仍有问题的局部（多为减面塌缩处）→ 删掉问题边周围两圈面再封孔，最多 6 轮
for _ in range(6):
    if topo_ok(new):
        break
    bm = bmesh.new(); bm.from_mesh(new.data)
    bad = {v for e in bm.edges if len(e.link_faces) != 2 for v in e.verts}
    for _r in range(2):
        bad |= {w for v in list(bad) for e in v.link_edges for w in e.verts}
    fs = list({f for v in bad for f in v.link_faces})
    bmesh.ops.delete(bm, geom=fs, context='FACES')
    loose = [v for v in bm.verts if not v.link_faces]
    if loose: bmesh.ops.delete(bm, geom=loose, context='VERTS')
    bm.to_mesh(new.data); bm.free()
    clean(new)

for p in new.data.polygons: p.use_smooth = True
# ---- UV + 烘焙 ----
while new.data.uv_layers: new.data.uv_layers.remove(new.data.uv_layers[0])
new.data.uv_layers.new(name='UVMap')
bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
bpy.ops.uv.smart_project(angle_limit=math.radians(60), island_margin=0.004, area_weight=0.0, scale_to_bounds=True)
bpy.ops.object.mode_set(mode='OBJECT')
img = bpy.data.images.new('albedo', tex_res, tex_res, alpha=True)
img.generated_color = (0, 0, 0, 0)
mat = bpy.data.materials.new('repaired_mat'); mat.use_nodes = True
nt = mat.node_tree; bsdf = nt.nodes.get('Principled BSDF')
tn = nt.nodes.new('ShaderNodeTexImage'); tn.image = img
nt.links.new(tn.outputs['Color'], bsdf.inputs['Base Color'])
new.data.materials.clear(); new.data.materials.append(mat)
nt.nodes.active = tn
sc = bpy.context.scene; sc.render.engine = 'CYCLES'; sc.cycles.samples = 1
try:
    prefs = bpy.context.preferences.addons['cycles'].preferences; prefs.compute_device_type = 'METAL'; prefs.get_devices()
    for d in prefs.devices: d.use = True
    sc.cycles.device = 'GPU'
except Exception:
    pass
sc.render.bake.use_selected_to_active = True
# 射线只在原表面附近找：P2 的墙常是一片片贴片，片间有缝，射线走远了会穿缝打到墙内侧（颜色错乱、像撕破）。
# 没打中的像素留透明，烘焙后用周围像素推拉填补。
sc.render.bake.cage_extrusion = vox * 1.6
sc.render.bake.max_ray_distance = vox * 3.5
sc.render.bake.margin = 0
bpy.ops.object.select_all(action='DESELECT'); srcobj.select_set(True); new.select_set(True); bpy.context.view_layer.objects.active = new
bpy.ops.object.bake(type='DIFFUSE', pass_filter={'COLOR'})
import numpy as np
px = np.array(img.pixels[:], dtype=np.float32).reshape(tex_res, tex_res, 4)
hitmask = px[..., 3] > 0.5
miss_ratio = 1 - hitmask.mean()
def pushpull(rgb, m):
    """未命中像素用多级平均推拉填补（先缩小求加权平均，再逐级放大填回空处）。"""
    levels = [(rgb * m[..., None], m.astype(np.float32))]
    while levels[-1][1].shape[0] > 1:
        c, w = levels[-1]
        h2 = c.shape[0] // 2
        c2 = c[:h2 * 2].reshape(h2, 2, h2, 2, 3).sum((1, 3)); w2 = w[:h2 * 2].reshape(h2, 2, h2, 2).sum((1, 3))
        levels.append((c2, w2))
    fill = levels[-1][0] / np.maximum(levels[-1][1], 1e-6)[..., None]
    for c, w in reversed(levels[:-1]):
        up = np.repeat(np.repeat(fill, 2, 0), 2, 1)[:c.shape[0], :c.shape[1]]
        own = c / np.maximum(w, 1e-6)[..., None]
        fill = np.where((w > 0)[..., None], own, up)
    return fill
rgb = px[..., :3]
filled = pushpull(rgb, hitmask)
px[..., :3] = np.where(hitmask[..., None], rgb, filled); px[..., 3] = 1
img.pixels[:] = px.ravel()
png = os.path.splitext(dst)[0] + '_albedo.png'
img.filepath_raw = png; img.file_format = 'PNG'; img.save()
img.pack()

# ---- 审计 ----
bm = bmesh.new(); bm.from_mesh(new.data)
before = [f.normal.copy() for f in bm.faces]
bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
rep = dict(faces=len(bm.faces), boundary=sum(1 for e in bm.edges if len(e.link_faces) == 1),
           nonmanifold=sum(1 for e in bm.edges if len(e.link_faces) > 2), zero=sum(1 for f in bm.faces if f.calc_area() < 1e-12),
           flipped=sum(1 for f, n in zip(bm.faces, before) if f.normal.dot(n) < 0), islands_remesh=islands, killed=killed,
           voxel=round(vox, 5), faces_remesh=faces_remesh, bake_miss=round(float(miss_ratio), 4))
bm.free()
rep['pass'] = rep['boundary'] == 0 and rep['nonmanifold'] == 0 and rep['zero'] == 0 and rep['flipped'] == 0
bpy.data.objects.remove(srcobj, do_unlink=True)
bpy.ops.object.select_all(action='DESELECT'); new.select_set(True)
bpy.ops.export_scene.gltf(filepath=dst, use_selection=True, export_format='GLB')
open(os.path.splitext(dst)[0] + '_repair.json', 'w').write(json.dumps(rep, ensure_ascii=False, indent=2))
print('REPAIR', json.dumps(rep, ensure_ascii=False))
sys.exit(0 if rep['pass'] else 2)
