"""窗户所在的老石墙残垣（世界铺开后出现，窗户就嵌在这段断墙里）：Blender -b -P Tools/blender_ruinwall.py -- <输出 fbx>
原点 = 窗洞中心；墙面（房间一侧）在 Y=0，墙厚 0.32 m 往 +Y（窗外）；导出后 Unity：房间一侧朝 -Z、窗外 +Z。
墙洞 = 窗框外沿 1.35 × 1.05 m；两侧边缘台阶状断口、顶部参差（中间高两边低）；底边埋到地下（窗洞中心下 1.45 m）。"""
import bpy, bmesh, math, random, sys
out = sys.argv[sys.argv.index('--') + 1]
random.seed(4)
bpy.ops.object.select_all(action='SELECT'); bpy.ops.object.delete()
HW, HH, TH = 1.35 / 2, 1.05 / 2, 0.32
BOT = -1.45
# 外轮廓（XZ 平面，逆时针）：底边 → 右边台阶 → 参差的顶 → 左边台阶
pts = [(-1.55, BOT), (1.70, BOT)]
# 右边：往上逐级内收的断口
x, z = 1.70, BOT
for step in range(5):
    z += random.uniform(0.28, 0.42); pts.append((x, z))
    x -= random.uniform(0.08, 0.2); pts.append((x, z))
# 顶：从右到左，中间高（窗洞上方 0.45 m 左右）、两边低，加小起伏
n = 16
for i in range(n + 1):
    t = i / n
    xx = x + (-1.35 - x) * t
    hump = math.cos((xx / 1.6) * math.pi / 2)
    pts.append((xx, HH + 0.12 + 0.42 * max(0.0, hump) + random.uniform(-0.06, 0.06)))
# 左边：往下逐级外扩到底
x, z = -1.35, pts[-1][1]
while z > BOT + 0.45:
    z -= random.uniform(0.3, 0.45); pts.append((x, max(z, BOT + 0.3)))
    x -= random.uniform(0.04, 0.09); pts.append((x, max(z, BOT + 0.3)))
pts.append((-1.55, BOT + 0.3))
me = bpy.data.meshes.new('wall'); bm = bmesh.new()
vs = [bm.verts.new((px, 0, pz)) for px, pz in pts]
f = bm.faces.new(vs)
bmesh.ops.recalc_face_normals(bm, faces=[f])
r = bmesh.ops.extrude_face_region(bm, geom=[f])
for v in [e for e in r['geom'] if isinstance(e, bmesh.types.BMVert)]: v.co.y += TH
bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
bm.to_mesh(me); bm.free()
wall = bpy.data.objects.new('RuinWall', me); bpy.context.scene.collection.objects.link(wall)
# 开窗洞
bpy.ops.mesh.primitive_cube_add(size=1, location=(0, TH / 2, 0)); cut = bpy.context.active_object
cut.scale = (HW * 2, TH * 3, HH * 2); bpy.ops.object.transform_apply(scale=True)
bpy.context.view_layer.objects.active = wall; wall.select_set(True)
bo = wall.modifiers.new('cut', 'BOOLEAN'); bo.operation = 'DIFFERENCE'; bo.object = cut; bo.solver = 'EXACT'
bpy.ops.object.modifier_apply(modifier='cut')
bpy.data.objects.remove(cut, do_unlink=True)
# 倒边让石墙边缘圆润一点
bpy.ops.object.select_all(action='DESELECT'); wall.select_set(True); bpy.context.view_layer.objects.active = wall
b = wall.modifiers.new('bv', 'BEVEL'); b.width = 0.025; b.segments = 2; b.limit_method = 'ANGLE'
bpy.ops.object.modifier_apply(modifier='bv')
bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT'); bpy.ops.uv.cube_project(cube_size=1.0); bpy.ops.object.mode_set(mode='OBJECT')
wall.data.materials.append(bpy.data.materials.new('Stone'))
print('faces', len(wall.data.polygons))
bpy.ops.export_scene.fbx(filepath=out, use_selection=True, apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL',
                         axis_forward='-Z', axis_up='Y', object_types={'MESH'}, bake_space_transform=True)
print('WALL EXPORTED')
