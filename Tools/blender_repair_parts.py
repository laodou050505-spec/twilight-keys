"""分部件拓扑修复（在 Blender 里运行）：
  Blender -b -P Tools/blender_repair_parts.py -- <model.glb> <out.glb> <面数上限> <规则名>

与 blender_repair.py 的区别：原模型里本来就是独立连通块的部件（糖桶里的每颗糖、每簇火苗……）按规则分组，
每组单独做 封闭化 → 体素重建 → 减面 → 清理，互相不会被体素熔成一块；
然后所有组共用一张贴图：合并后统一展 UV、从原模型烘焙颜色，再按组拆回独立物体（物体名 = 组名）。
后续 blender_process.py 会保留这些物体名（PART_ 前缀），Unity 导入后成为预制体的子物体，玩法/特效可以单独控制。

规则（RULES）：输入每个连通块的 面数/包围盒/平均贴图色/在全体中的排名，返回组名；返回 None 表示丢弃（碎屑）。
"""
import bpy, bmesh, sys, os, json, math
import numpy as np
from mathutils import Vector

argv = sys.argv[sys.argv.index('--') + 1:]
src, dst, cap, rule_name = argv[0], argv[1], int(argv[2]), argv[3]


def is_flame(c):
    r, g, b = c['col']
    return r > 0.85 and (r - b) > 0.3 and g > 0.5 and c['faces'] < 600


RULES = {
    # 糖桶：最大块（南瓜桶身）+ 黑色提手 → Bucket；其余每颗糖单独一组
    'bucket': lambda c: 'Bucket' if c['rank'] == 0 or (max(c['col']) < 0.2 and c['faces'] > 800) else (f"Candy_{c['rank']:02d}" if c['faces'] >= 60 else None),
    # 烛簇 / 骷髅烛台：火苗每个单独一组，其余（蜡、烛芯、底座、骷髅）合为 Body
    'candles': lambda c: (f"Flame_{c['rank']:02d}" if is_flame(c) else ('Body' if c['faces'] >= 20 else None)),
}

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
bpy.ops.object.select_all(action='DESELECT')
for o in meshes: o.select_set(True)
bpy.context.view_layer.objects.active = meshes[0]
if len(meshes) > 1: bpy.ops.object.join()
srcobj = bpy.context.view_layer.objects.active
bpy.ops.object.parent_clear(type='CLEAR_KEEP_TRANSFORM')
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
for o in list(bpy.context.scene.objects):
    if o != srcobj: bpy.data.objects.remove(o, do_unlink=True)
srcobj.name = 'bake_src'
dims = srcobj.dimensions
model_size = max(dims)

# ---- 贴图（用于按颜色分组）----
img = None
for slot in srcobj.material_slots:
    if slot.material and slot.material.use_nodes:
        for nd in slot.material.node_tree.nodes:
            if nd.type == 'TEX_IMAGE' and nd.image: img = nd.image
px = np.array(img.pixels[:]).reshape(img.size[1], img.size[0], 4) if img else None

# ---- 连通块 → 分组 ----
bm = bmesh.new(); bm.from_mesh(srcobj.data)
bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-6)
uv = bm.loops.layers.uv.active
bm.faces.ensure_lookup_table()
seen = set(); comps = []
for f in bm.faces:
    if f.index in seen: continue
    st = [f]; seen.add(f.index); c = []
    while st:
        g = st.pop(); c.append(g.index)
        for e in g.edges:
            for h in e.link_faces:
                if h.index not in seen: seen.add(h.index); st.append(h)
    comps.append(c)
comps.sort(key=len, reverse=True)
info = []
for rank, c in enumerate(comps):
    vs = np.array([v.co[:] for fi in c for v in bm.faces[fi].verts])
    col = [0.5, 0.5, 0.5]
    if px is not None and uv:
        cs = []
        for fi in c[:400]:
            u = bm.faces[fi].loops[0][uv].uv
            cs.append(px[int((u.y % 1) * (img.size[1] - 1)), int((u.x % 1) * (img.size[0] - 1)), :3])
        col = np.mean(cs, 0).tolist()
    info.append(dict(rank=rank, faces=len(c), lo=vs.min(0).tolist(), hi=vs.max(0).tolist(), col=col))
groups = {}
rule = RULES[rule_name]
for c, inf in zip(comps, info):
    g = rule(inf)
    if g: groups.setdefault(g, []).extend(c)
print('GROUPS', {k: len(v) for k, v in groups.items()})


def extract(face_ids, name):
    b2 = bm.copy()
    keep = set(face_ids)
    b2.faces.ensure_lookup_table()
    bmesh.ops.delete(b2, geom=[f for f in b2.faces if f.index not in keep], context='FACES')
    loose = [v for v in b2.verts if not v.link_faces]
    if loose: bmesh.ops.delete(b2, geom=loose, context='VERTS')
    me = bpy.data.meshes.new(name); b2.to_mesh(me); b2.free()
    ob = bpy.data.objects.new(name, me); bpy.context.scene.collection.objects.link(ob)
    return ob


def apply_mod(ob, m):
    bpy.ops.object.select_all(action='DESELECT'); ob.select_set(True); bpy.context.view_layer.objects.active = ob
    bpy.ops.object.modifier_apply(modifier=m.name)


def clean(ob, vox):
    b = bmesh.new(); b.from_mesh(ob.data)
    bmesh.ops.remove_doubles(b, verts=b.verts, dist=vox * 0.02)
    bmesh.ops.dissolve_degenerate(b, dist=vox * 0.01, edges=b.edges)
    bad = [e for e in b.edges if len(e.link_faces) > 2]
    if bad: bmesh.ops.delete(b, geom=list({f for e in bad for f in e.link_faces}), context='FACES')
    loose = [v for v in b.verts if not v.link_faces]
    if loose: bmesh.ops.delete(b, geom=loose, context='VERTS')
    bmesh.ops.holes_fill(b, edges=b.edges, sides=24)
    bmesh.ops.triangulate(b, faces=b.faces)
    bmesh.ops.recalc_face_normals(b, faces=b.faces)
    b.to_mesh(ob.data); b.free()


def topo_ok(ob):
    b = bmesh.new(); b.from_mesh(ob.data)
    ok = all(len(e.link_faces) == 2 for e in b.edges) and all(f.calc_area() > 1e-12 for f in b.faces)
    b.free(); return ok


def strip_small(ob, frac):
    b = bmesh.new(); b.from_mesh(ob.data); b.faces.ensure_lookup_table()
    seen_ = set(); cs = []
    for f in b.faces:
        if f.index in seen_: continue
        st = [f]; seen_.add(f.index); c = []
        while st:
            g = st.pop(); c.append(g)
            for e in g.edges:
                for h in e.link_faces:
                    if h.index not in seen_: seen_.add(h.index); st.append(h)
        cs.append(c)
    nf = len(b.faces); kill = [f for c in cs if len(c) < nf * frac for f in c]
    if kill: bmesh.ops.delete(b, geom=kill, context='FACES')
    b.to_mesh(ob.data); b.free()


total_src_faces = sum(len(v) for v in groups.values())
parts = []
for gi, (gname, fids) in enumerate(sorted(groups.items())):
    ob = extract(fids, gname)
    gsize = max(ob.dimensions)
    # 体素：组越小体素越细（保住火苗/糖果的形状），但不低于模型尺寸的 0.08%
    vox = max(model_size * 0.0008, min(model_size * 0.0028, gsize * 0.018))
    b = bmesh.new(); b.from_mesh(ob.data)
    bmesh.ops.remove_doubles(b, verts=b.verts, dist=vox * 0.05)
    bmesh.ops.recalc_face_normals(b, faces=b.faces)
    b.to_mesh(ob.data); b.free()
    m = ob.modifiers.new('sol', 'SOLIDIFY'); m.thickness = vox * 1.5; m.offset = -1.0; m.use_quality_normals = True; apply_mod(ob, m)
    m = ob.modifiers.new('vox', 'REMESH'); m.mode = 'VOXEL'; m.voxel_size = vox; m.adaptivity = 0.0; apply_mod(ob, m)
    strip_small(ob, 0.01)
    # 面数预算按原面数占比分配（每组至少 300）
    budget = max(300, int(cap * len(fids) / max(1, total_src_faces)))
    nf = len(ob.data.polygons)
    if nf > budget:
        m = ob.modifiers.new('dec', 'DECIMATE'); m.decimate_type = 'COLLAPSE'; m.ratio = budget / (nf * 2.0); m.use_collapse_triangulate = True; apply_mod(ob, m)
    m = ob.modifiers.new('tri', 'TRIANGULATE'); apply_mod(ob, m)
    for _ in range(3): clean(ob, vox)
    for _ in range(6):
        if topo_ok(ob): break
        b = bmesh.new(); b.from_mesh(ob.data)
        bad = {v for e in b.edges if len(e.link_faces) != 2 for v in e.verts}
        for _r in range(2): bad |= {w for v in list(bad) for e in v.link_edges for w in e.verts}
        bmesh.ops.delete(b, geom=list({f for v in bad for f in v.link_faces}), context='FACES')
        loose = [v for v in b.verts if not v.link_faces]
        if loose: bmesh.ops.delete(b, geom=loose, context='VERTS')
        b.to_mesh(ob.data); b.free()
        clean(ob, vox)
    for p in ob.data.polygons: p.use_smooth = True
    mat = bpy.data.materials.new('PART_' + gname)
    ob.data.materials.append(mat)
    parts.append((gname, ob, vox))
    print('PART', gname, 'src_faces', len(fids), 'faces', len(ob.data.polygons), 'vox', round(vox, 5), 'ok', topo_ok(ob))

# ---- 合并 → 展 UV → 烘焙 → 按材质拆回 ----
bpy.ops.object.select_all(action='DESELECT')
for _, ob, _ in parts: ob.select_set(True)
bpy.context.view_layer.objects.active = parts[0][1]
bpy.ops.object.join()
joined = bpy.context.view_layer.objects.active
joined.name = 'joined'
while joined.data.uv_layers: joined.data.uv_layers.remove(joined.data.uv_layers[0])
joined.data.uv_layers.new(name='UVMap')
bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
bpy.ops.uv.smart_project(angle_limit=math.radians(60), island_margin=0.003, area_weight=0.0, scale_to_bounds=True)
bpy.ops.object.mode_set(mode='OBJECT')
tex = bpy.data.images.new('albedo', 2048, 2048, alpha=False)
for m in joined.data.materials:
    m.use_nodes = True
    nt = m.node_tree; bsdf = nt.nodes.get('Principled BSDF')
    tn = nt.nodes.new('ShaderNodeTexImage'); tn.image = tex
    nt.links.new(tn.outputs['Color'], bsdf.inputs['Base Color'])
    nt.nodes.active = tn
sc = bpy.context.scene; sc.render.engine = 'CYCLES'; sc.cycles.samples = 1
try:
    prefs = bpy.context.preferences.addons['cycles'].preferences; prefs.compute_device_type = 'METAL'; prefs.get_devices()
    for d in prefs.devices: d.use = True
    sc.cycles.device = 'GPU'
except Exception:
    pass
vmin = min(v for _, _, v in parts)
sc.render.bake.use_selected_to_active = True
sc.render.bake.cage_extrusion = vmin * 6
sc.render.bake.max_ray_distance = vmin * 30
sc.render.bake.margin = 8
bpy.ops.object.select_all(action='DESELECT'); srcobj.select_set(True); joined.select_set(True); bpy.context.view_layer.objects.active = joined
bpy.ops.object.bake(type='DIFFUSE', pass_filter={'COLOR'})
png = os.path.splitext(dst)[0] + '_albedo.png'
tex.filepath_raw = png; tex.file_format = 'PNG'; tex.save(); tex.pack()
bpy.data.objects.remove(srcobj, do_unlink=True)
bpy.ops.object.select_all(action='DESELECT'); joined.select_set(True); bpy.context.view_layer.objects.active = joined
bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
bpy.ops.mesh.separate(type='MATERIAL')
bpy.ops.object.mode_set(mode='OBJECT')
rep = {'parts': {}}
for ob in [o for o in bpy.context.scene.objects if o.type == 'MESH']:
    mname = ob.data.materials[0].name if ob.data.materials else 'PART_?'
    ob.name = mname   # PART_<组名>
    b = bmesh.new(); b.from_mesh(ob.data)
    rep['parts'][mname] = dict(faces=len(b.faces), boundary=sum(1 for e in b.edges if len(e.link_faces) == 1),
                               nonmanifold=sum(1 for e in b.edges if len(e.link_faces) > 2))
    b.free()
rep['pass'] = all(p['boundary'] == 0 and p['nonmanifold'] == 0 for p in rep['parts'].values())
bpy.ops.object.select_all(action='SELECT')
bpy.ops.export_scene.gltf(filepath=dst, use_selection=True, export_format='GLB')
open(os.path.splitext(dst)[0] + '_repair.json', 'w').write(json.dumps(rep, ensure_ascii=False, indent=2))
print('REPAIR', json.dumps({'pass': rep['pass'], 'n': len(rep['parts'])}))
sys.exit(0 if rep['pass'] else 2)
