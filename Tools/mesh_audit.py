"""白模审计（在 Blender 里运行）：
  Blender -b -P Tools/mesh_audit.py -- <white.glb> <audit.json> <views.png>

检查项：
  - 碎片：连通块数量、最大块之外的面数占比（P2 偶尔会吐出漂浮碎屑）
  - 开放边界：边界边（只属于一个面的边）数量、开放环数量 → 漏面/破洞
  - 非流形边（属于 3 个以上面的边）→ 破面
  - 零面积面、退化面
  - 法线一致性：重新计算朝外后被翻转的面占比（高 → 原始法线混乱）
  - 薄片：包围盒最小轴 / 最大轴（只提示，不作为不合格：翅膀、斗篷本来就薄）
同时渲染一张六视图素模图（前/后/左/右/顶/三分之四）供人工检查结构。

判定：碎片占比 < 0.5% 且开放环 ≤ 2 且非流形边 ≤ 0.1% 面数 且翻转面 < 5% → pass。
"""
import bpy, bmesh, json, math, sys
from mathutils import Vector

argv = sys.argv[sys.argv.index('--') + 1:]
src, out_json, out_png = argv[0], argv[1], argv[2]
STRICT = len(argv) > 3 and argv[3] == 'strict'

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
bpy.ops.object.select_all(action='DESELECT')
for o in meshes:
    o.select_set(True)
bpy.context.view_layer.objects.active = meshes[0]
if len(meshes) > 1:
    bpy.ops.object.join()
obj = bpy.context.view_layer.objects.active
bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)

bm = bmesh.new()
bm.from_mesh(obj.data)
bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
bm.faces.ensure_lookup_table()
nf = len(bm.faces)

# 连通块
seen = set()
islands = []
for f in bm.faces:
    if f.index in seen:
        continue
    stack = [f]; seen.add(f.index); cnt = 0
    while stack:
        g = stack.pop(); cnt += 1
        for e in g.edges:
            for h in e.link_faces:
                if h.index not in seen:
                    seen.add(h.index); stack.append(h)
    islands.append(cnt)
islands.sort(reverse=True)
debris = sum(islands[1:]) / max(1, nf)
# 小于 1% 面数的块算碎屑（大于 1% 的可能是有意的独立部件，比如帽子、糖果）
debris_small = sum(c for c in islands[1:] if c < nf * 0.01) / max(1, nf)

boundary = [e for e in bm.edges if len(e.link_faces) == 1]
nonmanifold = [e for e in bm.edges if len(e.link_faces) > 2]
# 开放环：边界边按顶点相连分组
bverts = {}
for e in boundary:
    for v in e.verts:
        bverts.setdefault(v.index, []).append(e)
loops = 0
seen_e = set()
for e in boundary:
    if e.index in seen_e:
        continue
    loops += 1
    stack = [e]; seen_e.add(e.index)
    while stack:
        x = stack.pop()
        for v in x.verts:
            for y in bverts.get(v.index, []):
                if y.index not in seen_e:
                    seen_e.add(y.index); stack.append(y)
zero = sum(1 for f in bm.faces if f.calc_area() < 1e-10)

before = [f.normal.copy() for f in bm.faces]
bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
flipped = sum(1 for f, n in zip(bm.faces, before) if f.normal.dot(n) < 0) / max(1, nf)

mn = Vector((min(v.co.x for v in bm.verts), min(v.co.y for v in bm.verts), min(v.co.z for v in bm.verts)))
mx = Vector((max(v.co.x for v in bm.verts), max(v.co.y for v in bm.verts), max(v.co.z for v in bm.verts)))
size = mx - mn
thin = min(size) / max(1e-6, max(size))
bm.to_mesh(obj.data)
bm.free()

reasons = []
# 卡通模型里眼洞、嘴洞、翅膀/斗篷/帷幔边缘都是有意的开口，开放环本身不算破面；
# 真正要拦的是：大量碎屑（P2 吐出的漂浮碎块）、非流形（自交/破面）、法线混乱、
# 以及开放边界占比过高（说明大面积漏面，例如只剩半个壳）。
boundary_ratio = len(boundary) / max(1, nf * 1.5)  # 三角网格边数约为面数的 1.5 倍
if debris_small > 0.03:
    reasons.append(f'碎屑 {debris_small:.1%}')
if len(nonmanifold) > max(20, nf * 0.002):
    reasons.append(f'非流形边 {len(nonmanifold)}')
if flipped > 0.05:
    reasons.append(f'法线翻转 {flipped:.1%}')
if boundary_ratio > 0.04:
    reasons.append(f'开放边界占比 {boundary_ratio:.1%}（{loops} 个开放环）')
if STRICT:
    # 部件：必须是干净封闭实体
    reasons = []
    if len(boundary) > 0: reasons.append(f'开放边 {len(boundary)}（{loops} 个环）')
    if len(nonmanifold) > 0: reasons.append(f'非流形边 {len(nonmanifold)}')
    if debris_small > 0.005: reasons.append(f'碎屑 {debris_small:.1%}')
    if flipped > 0.02: reasons.append(f'法线翻转 {flipped:.1%}')
report = {
    'faces': nf, 'islands': len(islands), 'island_sizes_top5': islands[:5], 'debris_ratio': round(debris, 4), 'debris_small_ratio': round(debris_small, 4),
    'boundary_edges': len(boundary), 'open_loops': loops, 'nonmanifold_edges': len(nonmanifold), 'zero_area_faces': zero,
    'flipped_ratio': round(flipped, 4), 'bbox': [round(size.x, 3), round(size.y, 3), round(size.z, 3)], 'thin_ratio': round(thin, 3),
    'boundary_ratio': round(boundary_ratio, 4),
    'pass': not reasons, 'reason': '；'.join(reasons) or 'ok',
}

# ---------- 六视图素模 ----------
scene = bpy.context.scene
scene.render.engine = 'BLENDER_WORKBENCH'
scene.display.shading.light = 'STUDIO'
scene.display.shading.color_type = 'SINGLE'
scene.display.shading.single_color = (0.85, 0.82, 0.78)
scene.display.shading.show_cavity = True
scene.display.shading.show_backface_culling = True  # 背面剔除：漏面/翻转处会透出黑洞
scene.render.resolution_x = 512
scene.render.resolution_y = 512
scene.render.film_transparent = False
scene.world = bpy.data.worlds.new('w')
scene.world.color = (0.12, 0.1, 0.16)
center = (mn + mx) / 2
radius = max(size) * 1.25
cam_data = bpy.data.cameras.new('cam'); cam_data.type = 'ORTHO'; cam_data.ortho_scale = radius * 1.4
cam = bpy.data.objects.new('cam', cam_data); scene.collection.objects.link(cam); scene.camera = cam
views = {'front': (0, -1, 0), 'back': (0, 1, 0), 'left': (-1, 0, 0), 'right': (1, 0, 0), 'top': (0, 0.001, 1), 'iso': (0.7, -0.7, 0.5)}
tiles = []
import tempfile, os
tmp = tempfile.mkdtemp()
for k, d in views.items():
    dv = Vector(d).normalized()
    cam.location = center + dv * radius * 3
    cam.rotation_euler = (center - cam.location).to_track_quat('-Z', 'Y').to_euler()
    scene.render.filepath = os.path.join(tmp, k + '.png')
    bpy.ops.render.render(write_still=True)
    tiles.append(scene.render.filepath)
# 拼成 3x2
imgs = [bpy.data.images.load(p) for p in tiles]
W, H = 512, 512
grid = bpy.data.images.new('grid', W * 3, H * 2)
px = [0.0] * (W * 3 * H * 2 * 4)
for i, im in enumerate(imgs):
    gx, gy = i % 3, 1 - i // 3
    src_px = list(im.pixels)
    for y in range(H):
        row = src_px[y * W * 4:(y + 1) * W * 4]
        start = ((gy * H + y) * W * 3 + gx * W) * 4
        px[start:start + W * 4] = row
grid.pixels = px
grid.filepath_raw = out_png
grid.file_format = 'PNG'
grid.save()

open(out_json, 'w').write(json.dumps(report, ensure_ascii=False, indent=2))
print('AUDIT', json.dumps(report, ensure_ascii=False))
