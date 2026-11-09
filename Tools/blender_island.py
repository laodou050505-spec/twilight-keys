"""封面音乐盒里升起的小浮岛（参照 ArtSource/Concepts/cover_b）：微微隆起的草地圆盘（边缘不规则）+ 一侧的小湖 + 下面凹凸的岩石倒锥。
Blender -b -P Tools/blender_island.py -- <输出 fbx>
约定同其它道具（Blender Z 向上、-Y 朝玩家）。原点 = 草地中心（高度 0）。物体：Island（材质 Grass / Rock / Lakebed）、Water（湖面）。
湖心在 (LX, LY)、半径 LR（Unity 里摆东西时避开）。"""
import bpy, bmesh, math, random, sys

out = sys.argv[sys.argv.index('--') + 1]
bpy.ops.object.select_all(action='SELECT'); bpy.ops.object.delete()
random.seed(5)
R = 0.09                 # 草地半径
DEPTH = 0.1              # 岩石倒锥深
LX, LY, LR = 0.026, -0.022, 0.028
WZ = 0.0015             # 水位


def mat(name):
    return bpy.data.materials.get(name) or bpy.data.materials.new(name)


def edge_r(a):
    """边缘半径随角度起伏（不规则）。"""
    return R * (1 + 0.06 * math.sin(3 * a + 0.7) + 0.04 * math.sin(7 * a + 2.1) + 0.02 * math.sin(13 * a))


def top_z(x, y):
    r = math.hypot(x, y) / R
    z = 0.007 * max(0.0, 1 - r * r) + 0.0012 * math.sin(x * 90) * math.cos(y * 80)
    dl = math.hypot(x - LX, y - LY) / LR
    # 湖：水位 WZ，湖底是浅碗；岸边平滑过渡（交界线是水面和草坡的自然等高线，不会锯齿）
    w = min(1.0, max(0.0, (1.18 - dl) / 0.3)); w = w * w * (3 - 2 * w)
    bowl = WZ - 0.0015 - 0.005 * max(0.0, 1 - dl * dl)
    return z * (1 - w) + bowl * w


bm = bmesh.new()
SEG, RINGS, DOWN = 72, 16, 14
grass, rock, bed = 0, 1, 2
# 上表面：同心圆网格（中心一个点）
center = bm.verts.new((0, 0, top_z(0, 0)))
rings = []
for i in range(1, RINGS + 1):
    t = i / RINGS
    ring = []
    for k in range(SEG):
        a = 2 * math.pi * k / SEG
        rr = edge_r(a) * t
        x, y = rr * math.cos(a), rr * math.sin(a)
        ring.append(bm.verts.new((x, y, top_z(x, y) - (0.002 if i == RINGS else 0))))
    rings.append(ring)
faces_top = []
for k in range(SEG):
    faces_top.append(bm.faces.new([center, rings[0][k], rings[0][(k + 1) % SEG]]))
for i in range(RINGS - 1):
    for k in range(SEG):
        a, b = rings[i][k], rings[i][(k + 1) % SEG]
        c, d = rings[i + 1][(k + 1) % SEG], rings[i + 1][k]
        faces_top.append(bm.faces.new([a, d, c, b]))
# 草地边缘往下翻一个小圆角（草皮厚度）
lip = []
for k in range(SEG):
    a = 2 * math.pi * k / SEG
    rr = edge_r(a) * 1.02
    lip.append(bm.verts.new((rr * math.cos(a), rr * math.sin(a), -0.008)))
faces_lip = []
for k in range(SEG):
    a, b = rings[-1][k], rings[-1][(k + 1) % SEG]
    faces_lip.append(bm.faces.new([a, b, lip[(k + 1) % SEG], lip[k]]))
# 岩石倒锥：一圈圈往下收，带凹凸
prev = lip
faces_rock = []
for j in range(1, DOWN + 1):
    t = j / DOWN
    ring = []
    for k in range(SEG):
        a = 2 * math.pi * k / SEG
        # 圆滚滚的石头底：平滑起伏（不要逐点随机，会长刺），上半段鼓、下半段收
        noise = 1 + 0.07 * math.sin(5 * a + j * 0.25) + 0.045 * math.sin(9 * a - j * 0.35) + 0.03 * math.sin(3 * a + 1.3) * (1 - t)
        rr = edge_r(a) * 1.02 * (1 - t) ** 0.75 * (1 + 0.25 * math.sin(math.pi * min(1, t * 1.6))) * noise
        ring.append(bm.verts.new((rr * math.cos(a), rr * math.sin(a), -0.008 - DEPTH * t)))
    for k in range(SEG):
        faces_rock.append(bm.faces.new([prev[k], prev[(k + 1) % SEG], ring[(k + 1) % SEG], ring[k]]))
    prev = ring
tip = bm.verts.new((0.004, -0.003, -0.008 - DEPTH * 1.08))
for k in range(SEG):
    faces_rock.append(bm.faces.new([prev[k], prev[(k + 1) % SEG], tip]))
bm.normal_update()
bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
for f in faces_top:
    c = f.calc_center_median()
    f.material_index = bed if math.hypot(c.x - LX, c.y - LY) < LR * 0.88 else grass
for f in faces_lip + faces_rock: f.material_index = rock
me = bpy.data.meshes.new('Island'); bm.to_mesh(me); bm.free()
o = bpy.data.objects.new('Island', me); bpy.context.collection.objects.link(o)
for n in ('Grass', 'Rock', 'Lakebed'): o.data.materials.append(mat(n))
bpy.context.view_layer.objects.active = o; o.select_set(True)
bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT'); bpy.ops.uv.cube_project(cube_size=0.06); bpy.ops.object.mode_set(mode='OBJECT')
bpy.ops.object.shade_smooth()
# 湖面
bpy.ops.mesh.primitive_circle_add(vertices=48, radius=LR * 1.03, fill_type='TRIFAN', location=(LX, LY, WZ))
w = bpy.context.active_object; w.name = 'Water'; w.data.materials.append(mat('Water'))
print('Island', len(o.data.polygons))
bpy.ops.export_scene.fbx(filepath=out, use_selection=False, apply_unit_scale=True, apply_scale_options='FBX_SCALE_UNITS',
                         axis_forward='-Z', axis_up='Y', object_types={'MESH'}, mesh_smooth_type='FACE')
print('导出', out)
