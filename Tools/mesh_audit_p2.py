"""《黄昏琴村》TwilightKeys · 白模审计（在 Blender 里运行）：
  碎片 / 悬浮块 / 开放边与洞 / 非流形边 / 自穿插 / 薄片 + 素模六视图（灰模，检查破面、漏面、结构）
用法: Blender -b --python Tools/mesh_audit_p2.py -- <模型.glb> <输出目录> <名称>
输出 <输出目录>/<名称>_audit.json 与 <名称>_views.png（2×3 拼图）"""
import bpy, bmesh, json, math, pathlib, sys
from mathutils import Vector
from mathutils.bvhtree import BVHTree

argv = sys.argv[sys.argv.index('--') + 1:]
src, out, name = pathlib.Path(argv[0]), pathlib.Path(argv[1]), argv[2]
out.mkdir(parents=True, exist_ok=True)

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=str(src))
meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
bpy.ops.object.select_all(action='DESELECT')
for o in meshes: o.select_set(True)
bpy.context.view_layer.objects.active = meshes[0]
if len(meshes) > 1: bpy.ops.object.join()
obj = bpy.context.view_layer.objects.active
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

bm = bmesh.new(); bm.from_mesh(obj.data)
size = max(obj.dimensions)
bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=size * 5e-5)
bm.faces.ensure_lookup_table(); bm.verts.ensure_lookup_table()

# 连通块
seen, groups = set(), []
for f in bm.faces:
    if f.index in seen: continue
    st, g = [f], []; seen.add(f.index)
    while st:
        x = st.pop(); g.append(x)
        for e in x.edges:
            for y in e.link_faces:
                if y.index not in seen: seen.add(y.index); st.append(y)
    groups.append(g)
groups.sort(key=len, reverse=True)
debris = sum(1 for g in groups if len(g) < 6)
boxes = []
for g in groups:
    vs = [v.co for f in g for v in f.verts]
    boxes.append((Vector((min(v.x for v in vs), min(v.y for v in vs), min(v.z for v in vs))), Vector((max(v.x for v in vs), max(v.y for v in vs), max(v.z for v in vs)))))
pad = size * .01
floating = 0
for k, g in enumerate(groups):
    if len(g) < 6 or k == 0: continue
    lo, hi = boxes[k]
    touch = any(m != k and all(lo[i] - pad <= boxes[m][1][i] and hi[i] + pad >= boxes[m][0][i] for i in range(3)) for m in range(len(groups)))
    if not touch: floating += 1

# 开放边与洞（开放边环）
bnd = [e for e in bm.edges if e.is_boundary]
nonman = sum(1 for e in bm.edges if len(e.link_faces) > 2)
used, holes, big = set(), 0, 0
for e0 in bnd:
    if e0 in used: continue
    st, n = [e0], 0; used.add(e0); L = 0
    while st:
        e = st.pop(); n += 1; L += e.calc_length()
        for v in e.verts:
            for e2 in v.link_edges:
                if e2.is_boundary and e2 not in used: used.add(e2); st.append(e2)
    holes += 1
    if L > size * .05: big += 1

# 自穿插：不同连通块之间、以及同一块内不相邻面的交叠（BVH）
tree = BVHTree.FromBMesh(bm, epsilon=0.0)
pairs = tree.overlap(tree)
selfx = 0
for a, b in pairs:
    if a >= b: continue
    fa, fb = bm.faces[a], bm.faces[b]
    if set(fa.verts) & set(fb.verts): continue
    selfx += 1

# 薄片：大块里厚度极小的连通块（主成分最小轴 < 1.5% 最大轴）
thin = 0
for g in groups[1:]:
    if len(g) < 30: continue
    vs = [v.co for f in g for v in f.verts]
    c = sum(vs, Vector()) / len(vs)
    import numpy as np
    pts = np.array([tuple(v - c) for v in vs])
    ev = np.sqrt(np.maximum(np.linalg.eigvalsh(np.cov(pts.T)), 0))
    if ev[-1] > size * .03 and ev[0] < .015 * ev[-1]: thin += 1

res = {'name': name, 'faces': len(bm.faces), 'verts': len(bm.verts), 'size': [round(x, 3) for x in obj.dimensions],
       'islands': len(groups), 'debris': debris, 'floating_islands': floating, 'holes': holes, 'big_holes': big,
       'boundary_edges': len(bnd), 'nonmanifold_edges': nonman, 'self_intersections': selfx, 'thin_plates': thin,
       'largest_island_share': round(len(groups[0]) / max(1, len(bm.faces)), 3)}
bm.free()
(out / f'{name}_audit.json').write_text(json.dumps(res, indent=2))
print('AUDIT', json.dumps(res), flush=True)

# 素模六视图：灰色材质（背面染红，看漏面），工作台光照
mat = bpy.data.materials.new('clay'); mat.use_nodes = True
nt = mat.node_tree; nt.nodes.clear()
o_ = nt.nodes.new('ShaderNodeOutputMaterial'); bsdf = nt.nodes.new('ShaderNodeBsdfDiffuse'); geo = nt.nodes.new('ShaderNodeNewGeometry'); mix = nt.nodes.new('ShaderNodeMixShader')
red = nt.nodes.new('ShaderNodeEmission'); red.inputs[0].default_value = (1, 0, 0, 1)
bsdf.inputs[0].default_value = (.72, .72, .72, 1)
nt.links.new(geo.outputs['Backfacing'], mix.inputs[0]); nt.links.new(bsdf.outputs[0], mix.inputs[1]); nt.links.new(red.outputs[0], mix.inputs[2]); nt.links.new(mix.outputs[0], o_.inputs[0])
obj.data.materials.clear(); obj.data.materials.append(mat)
sc = bpy.context.scene
sc.render.engine = 'BLENDER_EEVEE_NEXT' if 'BLENDER_EEVEE_NEXT' in [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties['engine'].enum_items] else 'BLENDER_EEVEE'
sc.render.resolution_x = sc.render.resolution_y = 640
sc.world = bpy.data.worlds.new('w'); sc.world.use_nodes = True; sc.world.node_tree.nodes['Background'].inputs[0].default_value = (.25, .27, .3, 1); sc.world.node_tree.nodes['Background'].inputs[1].default_value = .6
for d, e in [((1, -1, 2), 3.0), ((-1, 1, 1), 1.2)]:
    ld = bpy.data.lights.new('sun', 'SUN'); ld.energy = e; lo = bpy.data.objects.new('sun', ld); sc.collection.objects.link(lo)
    lo.rotation_euler = Vector(d).to_track_quat('Z', 'Y').to_euler()
c = sum((obj.matrix_world @ Vector(b) for b in obj.bound_box), Vector()) / 8
r = max(obj.dimensions) * 1.05
cd = bpy.data.cameras.new('cam'); cd.type = 'ORTHO'; cd.ortho_scale = r * 1.15
cam = bpy.data.objects.new('cam', cd); sc.collection.objects.link(cam); sc.camera = cam
views = [('front', (0, -1, 0)), ('back', (0, 1, 0)), ('left', (-1, 0, 0)), ('right', (1, 0, 0)), ('top', (0, 0, 1)), ('bottom', (0, 0, -1))]
files = []
for vn, d in views:
    cam.location = c + Vector(d) * r * 3
    if vn == 'top': cam.rotation_euler = (0, 0, 0)
    elif vn == 'bottom': cam.rotation_euler = (math.pi, 0, 0)
    else: cam.rotation_euler = (-Vector(d)).to_track_quat('-Z', 'Y').to_euler()
    cd.clip_end = r * 10
    f = out / f'_{vn}.png'; sc.render.filepath = str(f); bpy.ops.render.render(write_still=True); files.append(f)
# 拼图
imgs = [bpy.data.images.load(str(f)) for f in files]
W = 640; big = bpy.data.images.new('grid', W * 3, W * 2)
import numpy as np
canvas = np.zeros((W * 2, W * 3, 4), dtype=np.float32)
for k, im in enumerate(imgs):
    px = np.array(im.pixels[:], dtype=np.float32).reshape(W, W, 4)
    row, col = 1 - k // 3, k % 3
    canvas[row * W:(row + 1) * W, col * W:(col + 1) * W] = px
big.pixels = canvas.ravel(); big.filepath_raw = str(out / f'{name}_views.png'); big.file_format = 'PNG'; big.save()
for f in files: f.unlink(missing_ok=True)
print('VIEWS', out / f'{name}_views.png', flush=True)
