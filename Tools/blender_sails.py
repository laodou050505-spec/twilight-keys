"""风车叶（参照 ArtSource/Refs/windmill_sails/image.png）：四片格栅帆 + 帆布 + 轮毂。
Blender -b -P Tools/blender_sails.py -- <输出 fbx>
叶片在 Blender XZ 平面、正面朝 -Y（导出后 Unity：叶片在 XY 平面、正面朝 +Z，绕本地 Z 转），轴心 = 轮毂中心。
尺寸按 11 m 高的塔：叶片从轮毂外 0.5 m 到 5.6 m。物体：Sails（木）、Cloth（帆布）、Hub（木）。"""
import bpy, math, sys
out = sys.argv[sys.argv.index('--') + 1]
bpy.ops.object.select_all(action='SELECT'); bpy.ops.object.delete()

def mat(name, rgb):
    m = bpy.data.materials.new(name); m.diffuse_color = (*rgb, 1); return m
WOOD, CLOTH = mat('SailWood', (0.42, 0.28, 0.16)), mat('SailCloth', (0.93, 0.88, 0.76))

def box(cx, cy, cz, sx, sy, sz, m, bev=0.02):
    bpy.ops.mesh.primitive_cube_add(size=1, location=(cx, cy, cz))
    o = bpy.context.active_object; o.scale = (sx, sy, sz); bpy.ops.object.transform_apply(scale=True)
    o.data.materials.append(m)
    if bev:
        b = o.modifiers.new('bv', 'BEVEL'); b.width = bev; b.segments = 1; bpy.ops.object.modifier_apply(modifier='bv')
    return o

def join(objs, name):
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs: o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]; bpy.ops.object.join(); objs[0].name = name; return objs[0]

R0, R1, W = 0.55, 5.6, 1.35         # 叶片起止半径、宽度
wood, cloth = [], []
for k in range(4):
    parts = []
    # 主梁（从轮毂到叶尖）
    parts.append(box(0, 0, (R1 + 0.1) / 2, 0.16, 0.16, R1 + 0.1, WOOD))
    # 格栅：两根长边 + 横档（叶片在主梁一侧，像参考图）
    x0, x1 = 0.08, 0.08 + W
    for x in (x0 + 0.04, x1):
        parts.append(box(x, -0.02, (R0 + R1) / 2, 0.08, 0.08, R1 - R0, WOOD))
    n = 8
    for i in range(n + 1):
        z = R0 + (R1 - R0) * i / n
        parts.append(box((x0 + x1) / 2, -0.02, z, W, 0.07, 0.07, WOOD))
    # 一根竖向中棂
    parts.append(box((x0 + x1) / 2, -0.03, (R0 + R1) / 2, 0.05, 0.05, R1 - R0, WOOD, 0.01))
    w = join(parts, f'blade{k}')
    # 帆布：格栅后面一点，略收边
    c = box((x0 + x1) / 2 + 0.02, 0.03, (R0 + R1) / 2 + 0.05, W - 0.1, 0.012, R1 - R0 - 0.2, CLOTH, 0)
    for o in (w, c):
        o.rotation_euler = (0, math.radians(90 * k), 0)
        bpy.ops.object.select_all(action='DESELECT'); o.select_set(True); bpy.context.view_layer.objects.active = o
        bpy.ops.object.transform_apply(rotation=True)
    wood.append(w); cloth.append(c)
# 轮毂
bpy.ops.mesh.primitive_cylinder_add(vertices=16, radius=0.38, depth=0.45, location=(0, -0.12, 0), rotation=(math.radians(90), 0, 0))
hub = bpy.context.active_object; hub.data.materials.append(WOOD)
b = hub.modifiers.new('bv', 'BEVEL'); b.width = 0.06; b.segments = 2; bpy.ops.object.modifier_apply(modifier='bv')
sails = join(wood + [hub], 'Sails')
cl = join(cloth, 'Cloth')
for o in (sails, cl):
    bpy.ops.object.select_all(action='DESELECT'); o.select_set(True); bpy.context.view_layer.objects.active = o
    bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT'); bpy.ops.uv.cube_project(cube_size=1.0); bpy.ops.object.mode_set(mode='OBJECT')
print('faces', sum(len(o.data.polygons) for o in bpy.data.objects if o.type == 'MESH'))
bpy.ops.object.select_all(action='SELECT')
bpy.ops.export_scene.fbx(filepath=out, use_selection=True, apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL',
                         axis_forward='-Z', axis_up='Y', object_types={'MESH'}, bake_space_transform=True)
print('SAILS EXPORTED', out)
