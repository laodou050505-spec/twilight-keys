"""虚拟木桌（识别到真实桌子后出现，和真桌面对齐）：Blender -b -P Tools/blender_table.py -- <输出 fbx>
桌面 1.30 × 0.80 × 0.045 m（顶面在原点高度 0.74 m 处 → 导出时原点 = 桌面顶面中心，Unity 里直接放在桌面高度），
围板、四条车削腿（Screw 旋转出轮廓）、腿间横撑。UV：真实尺寸立方体投影（木纹平铺）。正面（玩家一侧）朝 -Y（Unity -Z）。"""
import bpy, bmesh, math, sys
out = sys.argv[sys.argv.index('--') + 1]
bpy.ops.object.select_all(action='SELECT'); bpy.ops.object.delete()
W, D, T, H = 1.30, 0.80, 0.045, 0.74
m = bpy.data.materials.new('TableWood')

def box(cx, cy, cz, sx, sy, sz, bev=0.004, segs=2):
    bpy.ops.mesh.primitive_cube_add(size=1, location=(cx, cy, cz))
    o = bpy.context.active_object; o.scale = (sx, sy, sz); bpy.ops.object.transform_apply(scale=True)
    o.data.materials.append(m)
    if bev:
        b = o.modifiers.new('bv', 'BEVEL'); b.width = bev; b.segments = segs; b.limit_method = 'ANGLE'
        bpy.ops.object.modifier_apply(modifier='bv')
    return o

parts = []
# 桌面（顶面在 z = 0）
parts.append(box(0, 0, -T / 2, W, D, T, 0.012, 3))
# 围板
inset, ah, at = 0.07, 0.085, 0.022
for sx, sy, px, py in ((W - 2 * inset, at, 0, D / 2 - inset), (W - 2 * inset, at, 0, -D / 2 + inset),
                       (at, D - 2 * inset, W / 2 - inset, 0), (at, D - 2 * inset, -W / 2 + inset, 0)):
    parts.append(box(px, py, -T - ah / 2, sx, sy, ah, 0.003))
# 车削腿：轮廓（半径, 高度）自上而下，Screw 旋转
prof = [(0.030, 0.0), (0.030, -0.10), (0.024, -0.12), (0.034, -0.15), (0.022, -0.20), (0.020, -0.42), (0.026, -0.46),
        (0.018, -0.50), (0.016, -0.64), (0.022, -0.665), (0.020, -0.695)]
legh = H - T
def leg(x, y):
    me = bpy.data.meshes.new('leg'); bm = bmesh.new()
    vs = [bm.verts.new((r, 0, z * legh / 0.695)) for r, z in prof]
    for a, b in zip(vs, vs[1:]): bm.edges.new((a, b))
    bm.to_mesh(me); bm.free()
    o = bpy.data.objects.new('leg', me); bpy.context.scene.collection.objects.link(o)
    bpy.context.view_layer.objects.active = o; o.select_set(True)
    s = o.modifiers.new('sc', 'SCREW'); s.steps = 16; s.render_steps = 16; s.use_merge_vertices = True
    bpy.ops.object.modifier_apply(modifier='sc')
    # 封住上下端
    bm = bmesh.new(); bm.from_mesh(o.data)
    bmesh.ops.holes_fill(bm, edges=[e for e in bm.edges if e.is_boundary], sides=0)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces); bm.to_mesh(o.data); bm.free()
    o.location = (x, y, -T)
    o.data.materials.append(m)
    bpy.ops.object.select_all(action='DESELECT'); o.select_set(True); bpy.context.view_layer.objects.active = o
    bpy.ops.object.transform_apply(location=True)
    bpy.ops.object.shade_smooth()
    return o
lx, ly = W / 2 - inset - 0.005, D / 2 - inset - 0.005
for x in (-lx, lx):
    for y in (-ly, ly):
        parts.append(leg(x, y))
# 横撑（离地 12 cm）
zst = -H + 0.12
parts.append(box(0, ly, zst, 2 * lx, 0.02, 0.025, 0.003))
parts.append(box(0, -ly, zst, 2 * lx, 0.02, 0.025, 0.003))
parts.append(box(0, 0, zst, 0.02, 2 * ly, 0.025, 0.003))
bpy.ops.object.select_all(action='DESELECT')
for o in parts: o.select_set(True)
bpy.context.view_layer.objects.active = parts[0]; bpy.ops.object.join()
t = bpy.context.active_object; t.name = 'Table'
bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT'); bpy.ops.uv.cube_project(cube_size=1.0); bpy.ops.object.mode_set(mode='OBJECT')
print('faces', len(t.data.polygons))
bpy.ops.export_scene.fbx(filepath=out, use_selection=True, apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL',
                         axis_forward='-Z', axis_up='Y', object_types={'MESH'}, bake_space_transform=True)
print('TABLE EXPORTED')
