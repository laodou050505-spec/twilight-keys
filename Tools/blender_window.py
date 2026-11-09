"""《黄昏琴村》墙上的窗：窗框（线脚 + 窗台 + 檐板）+ 两扇推开的玻璃窗扇（参照 ArtSource/Concept/C01_mr_overview.png）。
Blender -b -P Tools/blender_window.py -- <输出 fbx>
窗洞（传送门）内框 1.20 × 0.90 m，中心在原点，墙面在 Y=0，房间一侧是 -Y（导出后 Unity 里是 +Z 朝房间）。
物体：Frame（木）、Sash_L / Sash_R（木，轴心在合页一侧外沿）、Glass_L / Glass_R（玻璃，挂在窗扇下）。"""
import bpy, bmesh, math, sys

out = sys.argv[sys.argv.index('--') + 1] if '--' in sys.argv else '/tmp/window.fbx'
OW, OH = 1.20, 0.90          # 窗洞
CW, CD = 0.075, 0.05         # 窗框宽、出墙深度
SILL_D, SILL_T, SILL_X = 0.085, 0.035, 0.09   # 窗台出墙、厚、两端多出
SW = 0.045                   # 窗扇边框宽
SD = 0.035                   # 窗扇厚

def clear():
    bpy.ops.object.select_all(action='SELECT'); bpy.ops.object.delete()

def mat(name, rgb):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name); m.diffuse_color = (*rgb, 1); return m

def box(name, cx, cy, cz, sx, sy, sz, m, bevel=0.004, segs=2):
    bpy.ops.mesh.primitive_cube_add(size=1, location=(cx, cy, cz))
    o = bpy.context.active_object; o.name = name; o.scale = (sx, sy, sz)
    bpy.ops.object.transform_apply(scale=True)
    o.data.materials.append(m)
    if bevel:
        b = o.modifiers.new('bv', 'BEVEL'); b.width = bevel; b.segments = segs; b.harden_normals = True; b.limit_method = 'ANGLE'
        bpy.ops.object.modifier_apply(modifier='bv')
    return o

def join(objs, name):
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs: o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.join(); objs[0].name = name
    return objs[0]

def ring(prefix, w, h, bw, depth, y, m, bevel):
    """矩形框（内框 w × h、边宽 bw），中心在 (0, y, 0)。"""
    parts = [box(prefix + 't', 0, y, h / 2 + bw / 2, w + 2 * bw, depth, bw, m, bevel),
             box(prefix + 'b', 0, y, -h / 2 - bw / 2, w + 2 * bw, depth, bw, m, bevel),
             box(prefix + 'l', -w / 2 - bw / 2, y, 0, bw, depth, h, m, bevel),
             box(prefix + 'r', w / 2 + bw / 2, y, 0, bw, depth, h, m, bevel)]
    return parts

def sash(side, wood, glass):
    """一扇窗：内框宽 OW/2，3 行 2 列窗格；轴心在合页（外侧竖边）。"""
    w, h = OW / 2 - 0.006, OH - 0.006
    cx = -w / 2 if side == 'L' else w / 2          # 关上时的中心
    parts = []
    parts += ring('s', w - 2 * SW, h - 2 * SW, SW, SD, 0, wood, 0.003)
    for o in parts: o.location.x += cx
    # 窗格：一根竖棂 + 两根横棂
    parts.append(box('mv', cx, 0, 0, 0.018, SD * 0.8, h - 2 * SW, wood, 0.002))
    for z in (-(h - 2 * SW) / 6, (h - 2 * SW) / 6):
        parts.append(box('mh', cx, 0, z * 2, w - 2 * SW, SD * 0.8, 0.018, wood, 0.002))
    fr = join(parts, f'Sash_{side}')
    gl = box(f'Glass_{side}', cx, 0, 0, w - 2 * SW + 0.01, 0.004, h - 2 * SW + 0.01, glass, 0)
    # 轴心放到合页一侧（窗洞外沿）
    hinge_x = -OW / 2 if side == 'L' else OW / 2
    for o in (fr, gl):
        bpy.context.scene.cursor.location = (hinge_x, 0, 0)
        bpy.ops.object.select_all(action='DESELECT'); o.select_set(True); bpy.context.view_layer.objects.active = o
        bpy.ops.object.origin_set(type='ORIGIN_CURSOR')
    gl.parent = fr
    gl.location = (0, 0, 0)
    gl.matrix_parent_inverse.identity()
    fr.location = (hinge_x, -CD + SD / 2, 0)        # 窗扇嵌在窗框里、靠房间一侧
    return fr, gl

def build():
    clear()
    wood = mat('WindowWood', (0.45, 0.28, 0.16))
    glass = mat('WindowGlass', (0.8, 0.9, 0.95))
    parts = []
    # 窗框：外框（出墙 CD）+ 内侧一圈窄“止口”
    parts += ring('c', OW, OH, CW, CD, -CD / 2, wood, 0.006)
    parts += ring('i', OW, OH, 0.018, CD + 0.02, -(CD + 0.02) / 2 + 0.01, wood, 0.003)
    # 窗台：比窗框宽、出墙更多，前沿倒圆
    parts.append(box('sill', 0, -SILL_D / 2, -OH / 2 - CW - SILL_T / 2 + 0.012, OW + 2 * CW + 2 * SILL_X, SILL_D, SILL_T, wood, 0.012, 3))
    # 窗台下的托板
    parts.append(box('apron', 0, -0.015, -OH / 2 - CW - SILL_T - 0.03, OW + 2 * CW, 0.03, 0.06, wood, 0.005))
    # 檐板：上方两层
    parts.append(box('head1', 0, -0.035, OH / 2 + CW + 0.018, OW + 2 * CW + 0.08, 0.07, 0.036, wood, 0.008, 3))
    parts.append(box('head2', 0, -0.05, OH / 2 + CW + 0.048, OW + 2 * CW + 0.12, 0.1, 0.026, wood, 0.008, 3))
    frame = join(parts, 'Frame')
    sl, _ = sash('L', wood, glass)
    sr, _ = sash('R', wood, glass)
    # UV：立方体投影（真实尺寸）
    for o in bpy.data.objects:
        if o.type != 'MESH': continue
        bpy.ops.object.select_all(action='DESELECT'); o.select_set(True); bpy.context.view_layer.objects.active = o
        bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
        bpy.ops.uv.cube_project(cube_size=0.6); bpy.ops.object.mode_set(mode='OBJECT')
    bpy.ops.object.select_all(action='SELECT')
    print('faces', sum(len(o.data.polygons) for o in bpy.data.objects if o.type == 'MESH'), flush=True)
    bpy.ops.export_scene.fbx(filepath=out, use_selection=True, apply_unit_scale=True, apply_scale_options='FBX_SCALE_UNITS',
                             axis_forward='-Z', axis_up='Y', object_types={'MESH'}, bake_space_transform=False)
    print('WINDOW EXPORTED', out, flush=True)

build()
