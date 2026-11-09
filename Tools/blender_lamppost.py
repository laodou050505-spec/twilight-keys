"""村庄路灯（程序建模，替换 Tripo 生成的歪灯杆）：笔直的铸铁灯杆 + 底座 + 卷草吊臂 + 六角吊灯笼。
Blender -b -P Tools/blender_lamppost.py -- <输出 fbx>
约定：Blender Z 向上，底面中心在原点，吊臂伸向 +X；导出 axis_forward=-Z, axis_up=Y（与其它道具一致）。
材质槽：Iron（铸铁）、Glass（灯笼玻璃，Unity 里做暖色自发光）。物体：LampPost（一个网格），总高约 3.2 m。"""
import bpy, bmesh, math, sys
from mathutils import Vector, Matrix

out = sys.argv[sys.argv.index('--') + 1]
bpy.ops.object.select_all(action='SELECT'); bpy.ops.object.delete()

H = 3.2          # 总高
ARM = 0.62       # 吊臂伸出长度
LZ = 2.66       # 灯笼顶（挂钩）高度：离吊臂只留一小段链子


def mat(n):
    return bpy.data.materials.get(n) or bpy.data.materials.new(n)


def lathe(name, prof, segs=24, material='Iron'):
    """旋转体：prof = [(半径, 高度), ...] 从下到上；上下自动封口。"""
    bm = bmesh.new()
    rings = []
    for r, z in prof:
        ring = [bm.verts.new((r * math.cos(2 * math.pi * k / segs), r * math.sin(2 * math.pi * k / segs), z)) for k in range(segs)]
        rings.append(ring)
    for a, b in zip(rings, rings[1:]):
        for k in range(segs):
            j = (k + 1) % segs
            bm.faces.new([a[k], a[j], b[j], b[k]])
    bm.faces.new(list(reversed(rings[0]))); bm.faces.new(rings[-1])
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    o = bpy.data.objects.new(name, me); bpy.context.collection.objects.link(o)
    o.data.materials.append(mat(material))
    return o


def tube(name, pts, r, segs=10, material='Iron'):
    """沿折线 pts 的圆管（两端封口），用来做吊臂和卷草。"""
    bm = bmesh.new()
    rings = []
    n = len(pts)
    for i, p in enumerate(pts):
        t = (pts[min(i + 1, n - 1)] - pts[max(i - 1, 0)]).normalized()
        a = Vector((0, 1, 0)) if abs(t.y) < 0.9 else Vector((1, 0, 0))
        u = t.cross(a).normalized(); v = t.cross(u)
        rr = r * (0.55 + 0.45 * min(1, (n - 1 - i) / 4)) if i > n - 5 else r      # 末端收细
        rings.append([bm.verts.new(p + (u * math.cos(2 * math.pi * k / segs) + v * math.sin(2 * math.pi * k / segs)) * rr) for k in range(segs)])
    for a_, b_ in zip(rings, rings[1:]):
        for k in range(segs):
            j = (k + 1) % segs
            bm.faces.new([a_[k], a_[j], b_[j], b_[k]])
    bm.faces.new(list(reversed(rings[0]))); bm.faces.new(rings[-1])
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    o = bpy.data.objects.new(name, me); bpy.context.collection.objects.link(o)
    o.data.materials.append(mat(material))
    return o


def spiral(c, r0, r1, a0, turns, n=40, y=0.0):
    """XZ 平面里的阿基米德螺线（卷草）。"""
    pts = []
    for i in range(n + 1):
        t = i / n
        a = a0 + turns * 2 * math.pi * t
        r = r0 + (r1 - r0) * t
        pts.append(Vector((c[0] + r * math.cos(a), y, c[1] + r * math.sin(a))))
    return pts


parts = []
# 底座 + 灯杆（笔直、带收分和几道箍）
parts.append(lathe('Pole', [
    (0.0, 0.0), (0.17, 0.0), (0.17, 0.05), (0.14, 0.07), (0.14, 0.12), (0.11, 0.15),
    (0.085, 0.2), (0.075, 0.55), (0.09, 0.58), (0.09, 0.64), (0.055, 0.68),
    (0.042, 1.2), (0.036, 2.55), (0.05, 2.58), (0.05, 2.63), (0.034, 2.66),
    (0.034, 2.92), (0.05, 2.95), (0.05, 2.99), (0.03, 3.02),
    (0.045, 3.06), (0.05, 3.1), (0.04, 3.15), (0.015, 3.19), (0.0, H)], segs=28))
# 吊臂：从灯杆顶下方水平伸出、末端下弯成挂钩；下方一条 S 形卷草撑臂
arm = [Vector((0.03, 0, 2.9))]
for i in range(1, 25):
    t = i / 24
    x = 0.03 + ARM * t
    z = 2.9 + 0.08 * math.sin(math.pi * t) - 0.02 * t
    arm.append(Vector((x, 0, z)))
for i in range(1, 9):                                   # 末端下弯
    a = math.pi / 2 * i / 8
    arm.append(Vector((0.03 + ARM + 0.06 * math.sin(a), 0, 2.88 - 0.06 * (1 - math.cos(a)) - 0.0 * i)))
parts.append(tube('Arm', arm, 0.022))
parts.append(tube('Hook', [Vector((0.03 + ARM + 0.06, 0, 2.82)), Vector((0.03 + ARM + 0.06, 0, LZ + 0.02))], 0.016))
# S 形撑臂：靠杆一侧卷一圈，靠外一侧反卷一圈
s1 = spiral((0.16, 2.72), 0.0, 0.1, math.pi * 1.0, 1.1)
s2 = spiral((0.42, 2.78), 0.0, 0.085, 0.0, -1.05)
mid = [Vector((0.16 + 0.1 * math.cos(math.pi * 1.0 + 2.2 * math.pi), 0, 2.72 + 0.1 * math.sin(math.pi * 1.0 + 2.2 * math.pi)))]
parts.append(tube('ScrollA', s1, 0.012, 8))
parts.append(tube('ScrollB', s2, 0.012, 8))
parts.append(tube('Brace', [Vector((0.035, 0, 2.62)), Vector((0.16, 0, 2.66)), Vector((0.3, 0, 2.74)), Vector((0.42, 0, 2.8)), Vector((0.52, 0, 2.87))], 0.014, 8))
# 灯笼：六角形，顶盖 + 玻璃 + 底盘 + 六根立柱
LX = 0.03 + ARM + 0.06
def hexlathe(name, prof, material):
    o = lathe(name, prof, segs=6, material=material)
    o.rotation_euler.z = math.radians(30); o.location = (LX, 0, 0)
    return o
parts.append(hexlathe('Cap', [(0.0, LZ - 0.0), (0.03, LZ - 0.005), (0.05, LZ - 0.03), (0.13, LZ - 0.1), (0.132, LZ - 0.118), (0.112, LZ - 0.125), (0.0, LZ - 0.125)], 'Iron'))
parts.append(hexlathe('Ring', [(0.0, LZ - 0.0), (0.035, LZ), (0.035, LZ + 0.04), (0.0, LZ + 0.04)], 'Iron'))
parts.append(hexlathe('Glass', [(0.0, LZ - 0.125), (0.11, LZ - 0.125), (0.1, LZ - 0.36), (0.0, LZ - 0.36)], 'Glass'))
parts.append(hexlathe('Base', [(0.0, LZ - 0.42), (0.04, LZ - 0.42), (0.12, LZ - 0.37), (0.125, LZ - 0.35), (0.0, LZ - 0.35)], 'Iron'))
for k in range(6):
    a = math.radians(30 + 60 * k)
    c = Vector((LX + 0.112 * math.cos(a), 0.112 * math.sin(a), 0))
    parts.append(tube('Post', [c + Vector((0, 0, LZ - 0.125)), c + Vector((0.004 * math.cos(a) * -1, 0.004 * math.sin(a) * -1, LZ - 0.36))], 0.0042, 6))   # 第十六轮：灯笼竖框变细（远看像一圈黑描边）
parts.append(lathe('Finial', [(0.0, LZ - 0.42), (0.02, LZ - 0.43), (0.0, LZ - 0.48)], segs=12))
parts[-1].location.x = LX

bpy.ops.object.select_all(action='DESELECT')
for o in parts: o.select_set(True)
bpy.context.view_layer.objects.active = parts[0]
bpy.ops.object.join()
lp = bpy.context.active_object; lp.name = 'LampPost'
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
bpy.ops.uv.cube_project(cube_size=0.5); bpy.ops.object.mode_set(mode='OBJECT')
try:
    bpy.ops.object.shade_smooth_by_angle(angle=math.radians(40))
except Exception:
    bpy.ops.object.shade_smooth()
print('LampPost', len(lp.data.polygons), 'glass at', (LX, 0, LZ - 0.24))
bpy.ops.export_scene.fbx(filepath=out, use_selection=False, apply_unit_scale=True, apply_scale_options='FBX_SCALE_UNITS',
                         axis_forward='-Z', axis_up='Y', object_types={'MESH'}, mesh_smooth_type='FACE')
print('导出', out)
