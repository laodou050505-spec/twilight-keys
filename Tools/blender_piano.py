"""《黄昏琴村》桌上的琴：两八度（C4~B5）真实尺寸键盘 + 胡桃木琴身（参照 ArtSource/Concept/C03_piano_closeup.png）。
在 Blender 里运行：Blender -b -P Tools/blender_piano.py -- <输出 fbx>
坐标（Blender，Z 向上，-Y 指向玩家）：原点 = 键盘左右居中、白键前沿、桌面高度。导出 FBX 时转成 Unity 坐标。
物体：Case（琴身，材质 Walnut）、Felt（红毡条）、Brass（黄铜细线）、Key_60..Key_83（每键一个，轴心在键尾下沿）。
白键按真实钢琴挖出黑键缺口；黑键位置按真实偏移（C# 偏左、D# 偏右、F# 偏左、G# 居中、A# 偏右）。"""
import bpy, bmesh, math, sys
from mathutils import Vector

out = sys.argv[sys.argv.index('--') + 1] if '--' in sys.argv else '/tmp/piano.fbx'
MM = 0.001

# ---------- 尺寸（毫米） ----------
W = 23.5            # 白键间距
GAP = 0.9           # 白键缝
WL = 150.0          # 白键长（可见部分）
WH = 22.0           # 白键厚
BW = 13.7           # 黑键宽（底）
BWT = 10.2          # 黑键顶面宽
BL = 95.0           # 黑键长
BH = 12.0           # 黑键高出白键顶面
NOTCH_CLR = 0.7     # 白键缺口与黑键的间隙
OCT = 7 * W
# 一个八度内黑键左边缘（毫米，从 C 左沿起）
BLACK_LEFT = {1: 15.0, 3: 43.5, 6: 85.5, 8: 112.0, 10: 138.5}
WHITES = [0, 2, 4, 5, 7, 9, 11]
LOW, HIGH = 60, 83
NW = 14
KB_W = NW * W                    # 键盘宽 329 mm
X0 = -KB_W / 2
CHEEK = 30.0                     # 琴颊宽
FALL_D = 46.0                    # 名板（键后）深度
BACK_D = 34.0                    # 后盖深度
KEY_Y0 = 0.0                     # 白键前沿
KEY_Y1 = WL                      # 白键后沿（向后为 +Y，导出前翻转）
BED = 6.0                        # 键床厚（白键底面离桌面）

def clear():
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.object.delete()
    for m in list(bpy.data.meshes): bpy.data.meshes.remove(m)

def mat(name, rgb):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.diffuse_color = (*rgb, 1)
    return m

def poly_prism(name, pts, z0, z1, material, bevel=0.0, segs=2):
    """二维轮廓（毫米，XY）挤出成柱体，可倒角。"""
    bm = bmesh.new()
    vs0 = [bm.verts.new((x * MM, y * MM, z0 * MM)) for x, y in pts]
    vs1 = [bm.verts.new((x * MM, y * MM, z1 * MM)) for x, y in pts]
    bm.faces.new(list(reversed(vs0)))
    bm.faces.new(vs1)
    n = len(pts)
    for i in range(n):
        j = (i + 1) % n
        bm.faces.new([vs0[i], vs0[j], vs1[j], vs1[i]])
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me); bm.free()
    ob = bpy.data.objects.new(name, me)
    bpy.context.collection.objects.link(ob)
    ob.data.materials.append(material)
    if bevel > 0:
        mod = ob.modifiers.new('bevel', 'BEVEL')
        mod.width = bevel * MM; mod.segments = segs; mod.limit_method = 'ANGLE'; mod.angle_limit = math.radians(40)
        mod.harden_normals = True
    return ob

def box(name, x0, x1, y0, y1, z0, z1, material, bevel=0.0, segs=2):
    return poly_prism(name, [(x0, y0), (x1, y0), (x1, y1), (x0, y1)], z0, z1, material, bevel, segs)

def apply_mods(ob):
    bpy.context.view_layer.objects.active = ob
    ob.select_set(True)
    for m in list(ob.modifiers):
        bpy.ops.object.modifier_apply(modifier=m.name)
    ob.select_set(False)

def is_black(m): return (m % 12) in BLACK_LEFT

def black_x(m):
    """黑键左右边缘（毫米，键盘坐标）。"""
    octv = (m - LOW) // 12
    l = X0 + octv * OCT + BLACK_LEFT[m % 12]
    return l, l + BW

def white_index(m):
    return sum(1 for k in range(LOW, m) if not is_black(k))

def build():
    clear()
    walnut = mat('Walnut', (0.36, 0.2, 0.11))
    ivory = mat('Ivory', (0.93, 0.89, 0.8))
    ebony = mat('Ebony', (0.05, 0.04, 0.035))
    felt = mat('Felt', (0.45, 0.04, 0.06))
    brass = mat('Brass', (0.8, 0.62, 0.3))
    keys = []
    # ---------- 白键 ----------
    for m in range(LOW, HIGH + 1):
        if is_black(m): continue
        i = white_index(m)
        l, r = X0 + i * W + GAP / 2, X0 + (i + 1) * W - GAP / 2
        # 后段缺口：左右相邻黑键占的范围
        nl, nr = l, r
        if m - 1 >= LOW and is_black(m - 1): nl = black_x(m - 1)[1] + NOTCH_CLR
        if m + 1 <= HIGH and is_black(m + 1): nr = black_x(m + 1)[0] - NOTCH_CLR
        # 两端的 C4 左侧 / B5 右侧没有黑键，保持全宽
        yb = KEY_Y1 - BL - NOTCH_CLR      # 缺口起点（前段全宽到这里）
        pts = [(l, KEY_Y0), (r, KEY_Y0), (r, yb), (nr, yb), (nr, KEY_Y1), (nl, KEY_Y1), (nl, yb), (l, yb)]
        # 去掉重复点（没有缺口的一侧）
        clean = []
        for p in pts:
            if not clean or (abs(clean[-1][0] - p[0]) > 1e-6 or abs(clean[-1][1] - p[1]) > 1e-6): clean.append(p)
        if abs(clean[0][0] - clean[-1][0]) < 1e-6 and abs(clean[0][1] - clean[-1][1]) < 1e-6: clean.pop()
        # 共线点去掉
        def collinear(a, b, c): return abs((b[0]-a[0])*(c[1]-a[1]) - (b[1]-a[1])*(c[0]-a[0])) < 1e-6
        k = 0
        while k < len(clean):
            a, b, c = clean[k - 1], clean[k], clean[(k + 1) % len(clean)]
            if collinear(a, b, c): clean.pop(k)
            else: k += 1
        ob = poly_prism(f'Key_{m}', clean, BED, BED + WH, ivory, bevel=1.2, segs=2)
        keys.append((m, ob))
    # ---------- 黑键（顶面收窄、前端斜切） ----------
    for m in range(LOW, HIGH + 1):
        if not is_black(m): continue
        l, r = black_x(m)
        cx = (l + r) / 2
        z0, z1 = BED + WH - 2.0, BED + WH + BH
        y0, y1 = KEY_Y1 - BL, KEY_Y1
        bm = bmesh.new()
        hb, ht = BW / 2, BWT / 2
        # 底面矩形 + 顶面收窄且前端后缩 6mm（斜面）
        b = [(cx - hb, y0, z0), (cx + hb, y0, z0), (cx + hb, y1, z0), (cx - hb, y1, z0)]
        t = [(cx - ht, y0 + 6, z1), (cx + ht, y0 + 6, z1), (cx + ht, y1, z1), (cx - ht, y1, z1)]
        vb = [bm.verts.new((x * MM, y * MM, z * MM)) for x, y, z in b]
        vt = [bm.verts.new((x * MM, y * MM, z * MM)) for x, y, z in t]
        bm.faces.new(list(reversed(vb))); bm.faces.new(vt)
        for i in range(4):
            j = (i + 1) % 4
            bm.faces.new([vb[i], vb[j], vt[j], vt[i]])
        me = bpy.data.meshes.new(f'Key_{m}')
        bm.to_mesh(me); bm.free()
        ob = bpy.data.objects.new(f'Key_{m}', me)
        bpy.context.collection.objects.link(ob)
        ob.data.materials.append(ebony)
        mod = ob.modifiers.new('bevel', 'BEVEL'); mod.width = 1.0 * MM; mod.segments = 2; mod.harden_normals = True
        keys.append((m, ob))
    # ---------- 琴身 ----------
    xL, xR = X0 - CHEEK, -X0 + CHEEK
    yF, yK = -4.0, KEY_Y1                      # 前挡板前沿 / 琴键后沿
    yFall, yBack = yK + FALL_D, yK + FALL_D + BACK_D
    parts = []
    # 键床（琴键下面的底板）
    parts.append(box('bed', X0 - 1, -X0 + 1, yF, yBack, 0, BED, walnut, bevel=0.8))
    # 前挡板（琴键下方，比白键顶低一点）
    parts.append(box('slip', X0 - 1, -X0 + 1, yF - 10, yF, 0, BED + WH - 6, walnut, bevel=2.5, segs=3))
    # 琴颊：侧视轮廓 = 前端低圆、往后升高的弧（参照 C03）
    def cheek(name, x0, x1):
        prof = []
        for s in range(0, 13):
            t = s / 12
            y = (yF - 10) + t * (yFall - (yF - 10))
            # 前端 28mm，往后用 S 形升到 64mm
            h = 28 + 36 * (3 * t * t - 2 * t * t * t)
            prof.append((y, h))
        prof += [(yBack, 64), (yBack, 0), (yF - 10, 0)]
        bm = bmesh.new()
        v0 = [bm.verts.new((x0 * MM, y * MM, z * MM)) for y, z in prof]
        v1 = [bm.verts.new((x1 * MM, y * MM, z * MM)) for y, z in prof]
        bm.faces.new(v0); bm.faces.new(list(reversed(v1)))
        n = len(prof)
        for i in range(n):
            j = (i + 1) % n
            bm.faces.new([v0[j], v0[i], v1[i], v1[j]])
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
        ob = bpy.data.objects.new(name, me); bpy.context.collection.objects.link(ob)
        ob.data.materials.append(walnut)
        mod = ob.modifiers.new('bevel', 'BEVEL'); mod.width = 4 * MM; mod.segments = 4; mod.limit_method = 'ANGLE'; mod.harden_normals = True
        return ob
    parts.append(cheek('cheekL', xL, X0 - 1))
    parts.append(cheek('cheekR', -X0 + 1, xR))
    # 名板：键后竖起的一块，顶部前倾一点
    parts.append(box('fallboard', X0 - 1, -X0 + 1, yK + 8, yFall, BED, 58, walnut, bevel=2.0, segs=3))
    # 后盖：名板后面盖住的平台（音块从这里滑上琴键）
    parts.append(box('lid', xL + 2, xR - 2, yFall - 2, yBack, 58, 64, walnut, bevel=2.5, segs=3))
    case = parts[0]
    for p in parts: apply_mods(p)
    bpy.ops.object.select_all(action='DESELECT')
    for p in parts: p.select_set(True)
    bpy.context.view_layer.objects.active = case
    bpy.ops.object.join()
    case.name = 'Case'
    # 红毡条：琴键后沿与名板之间
    fe = box('Felt', X0, -X0, yK + 0.5, yK + 8, BED + WH - 4, BED + WH + 2, felt, bevel=1.5, segs=3); apply_mods(fe)
    # 黄铜细线：名板正面一条 + 前挡板一条
    br1 = box('brass_fall', X0 + 4, -X0 - 4, yK + 7.6, yK + 8.4, BED + WH + 12, BED + WH + 13.5, brass)
    br2 = box('brass_slip', X0 + 2, -X0 - 2, yF - 10.4, yF - 9.6, 10, 11.5, brass)
    for b in (br1, br2): apply_mods(b)
    bpy.ops.object.select_all(action='DESELECT')
    br1.select_set(True); br2.select_set(True); bpy.context.view_layer.objects.active = br1
    bpy.ops.object.join(); br1.name = 'Brass'
    # 琴键：应用倒角，把轴心放到键尾下沿（Unity 里绕 X 轴转就是按下）
    for m, ob in keys:
        apply_mods(ob)
        bpy.context.scene.cursor.location = (ob.location.x, KEY_Y1 * MM, BED * MM)
        # 用该键自身的 x 中心
        xs = [v.co.x for v in ob.data.vertices]
        bpy.context.scene.cursor.location = ((min(xs) + max(xs)) / 2, KEY_Y1 * MM, BED * MM)
        bpy.ops.object.select_all(action='DESELECT'); ob.select_set(True); bpy.context.view_layer.objects.active = ob
        bpy.ops.object.origin_set(type='ORIGIN_CURSOR')
    # UV：木头按真实尺寸立方体投影（1 米 = 1 个 UV 单位 ×2，贴图在材质里平铺）
    for ob in bpy.data.objects:
        if ob.type != 'MESH': continue
        bpy.ops.object.select_all(action='DESELECT'); ob.select_set(True); bpy.context.view_layer.objects.active = ob
        bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
        bpy.ops.uv.cube_project(cube_size=0.5, correct_aspect=True, scale_to_bounds=False)
        bpy.ops.object.mode_set(mode='OBJECT')
        for p in ob.data.polygons: p.use_smooth = True
    # 键盘整体翻到“-Y 指向玩家”：琴键前沿在原点，琴身朝 +Y 延伸 → 导出后 Unity 里琴身朝 +Z（远离玩家时再在 Unity 转）
    bpy.ops.object.select_all(action='SELECT')
    tris = sum(len(o.data.polygons) for o in bpy.data.objects if o.type == 'MESH')
    print('objects', len(bpy.data.objects), 'faces', tris, flush=True)
    bpy.ops.export_scene.fbx(filepath=out, use_selection=True, apply_unit_scale=True, apply_scale_options='FBX_SCALE_UNITS',
                             axis_forward='-Z', axis_up='Y', object_types={'MESH'}, mesh_smooth_type='FACE', use_mesh_modifiers=True)
    print('PIANO EXPORTED', out, flush=True)

build()
