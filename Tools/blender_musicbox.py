"""封面八音盒 v13：圆鼓象牙白瓷盒（参照 ArtSource/Concepts/musicbox_v14/round），盒身手绘玫瑰瓷，盒身 + 盒盖（能开合）+ 正面三个按钮 + 侧面发条钥匙。
Blender -b -P Tools/blender_musicbox.py -- <输出 fbx>
约定同其它道具：Blender Z 向上，-Y = 正面（朝玩家）；导出 axis_forward=-Z, axis_up=Y（导进 Unity 后正面朝 +Z，用时转 180°）。底面中心在原点。
物体（Unity 里按名字找）：Base（盒身）、Lid（盒盖，原点在后上沿合页处）、BtnLeft / BtnRight（铭牌两边换歌）/ BtnStart / BtnExit（下排长圆钮；原点都在按钮正面中心）、BtnSettings（设置，中间圆钮）、Drum / Tooth00..14 / Movement（机芯）、Spindle（小岛立柱，原点在柱底）。
材质槽：Wood 手绘木纹、WoodDark 深色描边 / 凹槽、Gold 画出来的金色（合页、包角、按钮圈）、Velvet 丝绒内衬、Panel 盒盖内侧写字的浅色面板。"""
import bpy, mathutils, bmesh, math, sys
from mathutils import Vector

out = sys.argv[sys.argv.index('--') + 1]
bpy.ops.object.select_all(action='SELECT'); bpy.ops.object.delete()

W, D, H = 0.26, 0.19, 0.088          # 盒身外尺寸
R = 0.022                            # 圆角半径
WALL = 0.014                         # 壁厚
FLOOR = 0.02                         # 底厚（丝绒内衬面）
LH = 0.036                           # 盒盖高
G0, GH = 0.019, 0.062                # 四面刻线框：底边高度、框高


def mat(name):
    return bpy.data.materials.get(name) or bpy.data.materials.new(name)


def rounded_prism(name, w, d, r, z0, z1, segs=8, material='Wood'):
    """圆角矩形柱（上下封口）。"""
    bm = bmesh.new()
    pts = []
    for cx, cy, a0 in [(w / 2 - r, d / 2 - r, 0), (-w / 2 + r, d / 2 - r, 90), (-w / 2 + r, -d / 2 + r, 180), (w / 2 - r, -d / 2 + r, 270)]:
        for k in range(segs + 1):
            a = math.radians(a0 + 90 * k / segs)
            pts.append((cx + r * math.cos(a), cy + r * math.sin(a)))
    bot = [bm.verts.new((x, y, z0)) for x, y in pts]
    top = [bm.verts.new((x, y, z1)) for x, y in pts]
    n = len(pts)
    for i in range(n):
        j = (i + 1) % n
        bm.faces.new([bot[i], bot[j], top[j], top[i]])
    bm.faces.new(list(reversed(bot))); bm.faces.new(top)
    bm.normal_update()
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    o = bpy.data.objects.new(name, me); bpy.context.collection.objects.link(o)
    o.data.materials.append(mat(material))
    return o


def boolean(target, cutter, op='DIFFERENCE'):
    m = target.modifiers.new('b', 'BOOLEAN'); m.operation = op; m.object = cutter; m.solver = 'EXACT'
    bpy.context.view_layer.objects.active = target
    bpy.ops.object.modifier_apply(modifier='b')
    bpy.data.objects.remove(cutter, do_unlink=True)


def bevel(o, width, segs=3, angle=40):
    bpy.context.view_layer.objects.active = o
    m = o.modifiers.new('bv', 'BEVEL'); m.width = width; m.segments = segs; m.limit_method = 'ANGLE'; m.angle_limit = math.radians(angle)
    bpy.ops.object.modifier_apply(modifier='bv')


def cyl(name, r, depth, loc, rot=(0, 0, 0), verts=32, material='Gold'):
    bpy.ops.mesh.primitive_cylinder_add(vertices=verts, radius=r, depth=depth, location=loc, rotation=rot)
    o = bpy.context.active_object; o.name = name
    o.data.materials.append(mat(material))
    return o


def join(objs, name):
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs: o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.join()
    o = bpy.context.active_object; o.name = name
    return o


def finish(o, uv=0.12, smooth=35):
    bpy.ops.object.select_all(action='DESELECT'); o.select_set(True); bpy.context.view_layer.objects.active = o
    # 贴图四边形（Ornament / Relief 浮雕金带 / Painting 盖内油画）保留自己的 UV，其余面盒式投影
    keep = {o.data.materials.find(n) for n in KEEP_UV} - {-1}
    bpy.ops.object.mode_set(mode='EDIT')
    bm = bmesh.from_edit_mesh(o.data)
    for v in bm.verts: v.select_set(False)
    for f in bm.faces: f.select_set(f.material_index not in keep)
    bm.select_flush_mode(); bmesh.update_edit_mesh(o.data)
    bpy.ops.uv.cube_project(cube_size=uv)
    bpy.ops.object.mode_set(mode='OBJECT')
    # 合并时这些四边形自己的 UV 会丢（并进来的物体没有同名 UV 层）：按顶点重新算
    if keep:
        lay = o.data.uv_layers.active.data
        names = [m.name if m else '' for m in o.data.materials]
        for p in o.data.polygons:
            if p.material_index not in keep: continue
            kind = names[p.material_index].split('.')[0]
            vs = [o.data.vertices[o.data.loops[li].vertex_index].co for li in p.loop_indices]
            c = sum(vs, Vector()) / len(vs)
            if kind == 'Enamel':
                pn = p.normal
                for li, co in zip(p.loop_indices, vs):
                    u = co.x if abs(pn.y) >= abs(pn.x) else co.y
                    lay[li].uv = (u / ((EN_Z1 - EN_Z0) * ENAMEL_ASPECT) + (0.5 if pn.y > 0 or pn.x > 0 else 0.0), (co.z - EN_Z0) / (EN_Z1 - EN_Z0))
                continue
            if kind == 'Painting':
                # 盖内朝下的面：开盖后 −y（盖前沿）在上，+x 在玩家右手 → u 沿 +x，v 沿 −y
                xs = [v.x for v in vs]; ys = [v.y for v in vs]
                for li, co in zip(p.loop_indices, vs):
                    lay[li].uv = ((co.x - min(xs)) / (max(xs) - min(xs)), (max(ys) - co.y) / (max(ys) - min(ys)))
                continue
            e1, e2 = vs[1] - vs[0], vs[2] - vs[1]
            ua = (e1 if e1.length >= e2.length else e2); va = (e2 if e1.length >= e2.length else e1)
            ul, vl = ua.length, va.length; ua = ua.normalized(); va = va.normalized()
            if va.z < -0.5 or (abs(va.z) <= 0.5 and va.y < 0): va = -va      # 竖面：v 朝上；顶面：v 朝后
            us = ul / vl / RELIEF_ASPECT if kind == 'Relief' else (ul / vl / ENAMEL_ASPECT if kind == 'Enamel' else 1.0)           # 浮雕带：按长度横向平铺（贴图 8:1），花纹不拉伸
            for li, co in zip(p.loop_indices, vs):
                lay[li].uv = (((co - c).dot(ua) / ul + 0.5) * us, (co - c).dot(va) / vl + 0.5)
    try:
        bpy.ops.object.shade_smooth_by_angle(angle=math.radians(smooth))
    except Exception:
        bpy.ops.object.shade_smooth()


def set_origin(o, p):
    bpy.context.scene.cursor.location = p
    bpy.ops.object.select_all(action='DESELECT'); o.select_set(True); bpy.context.view_layer.objects.active = o
    bpy.ops.object.origin_set(type='ORIGIN_CURSOR')


RELIEF_ASPECT = 2048 / 256   # relief_frieze.png 宽高比

def basis_obj(name, center, ax, ay, az, sx, sy, sz, material='Gold', segs=12, rings=6):
    """椭球：三个轴向 ax/ay/az（单位向量），半径 sx/sy/sz。"""
    bpy.ops.mesh.primitive_uv_sphere_add(segments=segs, ring_count=rings, radius=1)
    o = bpy.context.active_object; o.name = name
    ax, ay, az = Vector(ax).normalized(), Vector(ay).normalized(), Vector(az).normalized()
    o.matrix_world = mathutils.Matrix(((ax.x * sx, ay.x * sy, az.x * sz, center[0]), (ax.y * sx, ay.y * sy, az.y * sz, center[1]),
                                       (ax.z * sx, ay.z * sy, az.z * sz, center[2]), (0, 0, 0, 1)))
    o.data.materials.append(mat(material))
    return o


def shell(name, center, n, up, size):
    """贝壳（扇形棱纹）：铰点在下，七道棱向上张开，凸出 n 方向。"""
    n, up = Vector(n).normalized(), Vector(up).normalized(); rt = up.cross(n).normalized()
    c = Vector(center); parts = []
    for k in range(7):
        a = math.radians(18 + 144 * k / 6)
        d = rt * math.cos(a) + up * math.sin(a)
        side = d.cross(n).normalized()
        parts.append(basis_obj(name + str(k), c + d * size * 0.48 + n * size * 0.1, d, side, n, size * 0.5, size * 0.13, size * 0.2))
    parts.append(basis_obj(name + 'hub', c + n * size * 0.08, rt, up, n, size * 0.2, size * 0.14, size * 0.18))
    return parts



# ================= 第十三轮：圆鼓象牙白瓷盒（参照 ArtSource/Concepts/musicbox_v14/round/aa31bb74.png） =================
# 横截面 = 超椭圆（|x/A|^n + |y/B|^n = 1，n = 2.4：比椭圆饱满，正面不至于太尖）；竖剖面 = 鼓腹（下收、腰最鼓、上沿再收一点）。
# 盒身手绘玫瑰瓷（Porcelain）、上沿金边 + 珍珠串、镂空卷草金台座 + 四只卷脚；盒盖 = 同截面的拱盖，盖内象牙白瓷面 + 圆形村庄画 + 金圈，
# 标题刻在盖前沿伸出来的一块金色卷边铭牌上（开盖后在最上方，小岛挡不到）。按钮贴着弧面、各自转向面法线。
A, B, NSE = 0.13, 0.095, 2.4
BODY_Z0 = 0.026        # 台座顶面 = 盒身底（台座加高：参考图里镂空金台座约占三分之一高）
FLOORZ = BODY_Z0 + 0.003   # 盒内底面
S_PROF = [(BODY_Z0, 0.84), (0.031, 0.915), (0.039, 0.97), (0.05, 1.0), (0.062, 1.0), (0.073, 0.98), (0.082, 0.945), (H, 0.915)]   # 参考图：鼓腹更饱满，上下明显收口
S_TOP = 0.915
SEG = 128       # 一圈的分段数（近看不出棱）
WALLR = 0.010   # 上沿壁厚
KEEP_UV = ('Porcelain', 'Relief', 'Painting', 'TitleArt', 'LidInner')


def s_at(z):
    if z <= S_PROF[0][0]:
        return S_PROF[0][1]
    for (z0, s0), (z1, s1) in zip(S_PROF, S_PROF[1:]):
        if z <= z1:
            t = (z - z0) / max(1e-6, z1 - z0)
            t = t * t * (3 - 2 * t)
            return s0 + (s1 - s0) * t
    return S_PROF[-1][1]


def se_pt(t, a, b):
    c, s = math.cos(t), math.sin(t)
    e = 2.0 / NSE
    return (a * math.copysign(abs(c) ** e, c), b * math.copysign(abs(s) ** e, s))


def se_ring(a, b, n=SEG, t0=math.pi / 2):
    """超椭圆一圈 n 个点：从正后方（+y）开始逆时针。"""
    return [se_pt(t0 + 2 * math.pi * k / n, a, b) for k in range(n)]


def se_even(a, b, step):
    """按弧长均匀取点（排珍珠用）。"""
    dense = se_ring(a, b, 2000)
    L = [0.0]
    for p, q in zip(dense, dense[1:] + dense[:1]):
        L.append(L[-1] + math.dist(p, q))
    m = max(8, round(L[-1] / step))
    out = []
    j = 0
    for k in range(m):
        target = L[-1] * k / m
        while L[j + 1] < target:
            j += 1
        p, q = dense[j], dense[(j + 1) % len(dense)]
        f = (target - L[j]) / max(1e-9, L[j + 1] - L[j])
        out.append((p[0] + (q[0] - p[0]) * f, p[1] + (q[1] - p[1]) * f))
    return out


def surface_y(x, z):
    """正面（−y 一侧）在 (x, z) 处的外表面 y。"""
    s = s_at(z)
    a, b = A * s, B * s
    u = min(0.999, abs(x) / a)
    return -b * (1 - u ** NSE) ** (1 / NSE)


def surface_yaw(x, z):
    """正面 (x, z) 处外法线在水平面上的偏角（0 = 正对 −y；x > 0 时法线偏向 +x，绕 z 转 +yaw）。"""
    e = 1e-4
    dydx = (surface_y(x + e, z) - surface_y(x - e, z)) / (2 * e)      # 外法线 ∝ (dy/dx, −1)
    return math.atan2(dydx, 1.0)


def new_obj(name, bm, mats):
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    o = bpy.data.objects.new(name, me)
    bpy.context.collection.objects.link(o)
    for mn in mats:
        o.data.materials.append(mat(mn))
    if not o.data.uv_layers:
        o.data.uv_layers.new(name='UVMap')
    return o


def se_loft(name, zs, scale_of, out=0.0, mat_of=lambda zc: 'Gold', uv_rep=3.0, v_of=None, cap=True):
    """沿竖剖面放样的超椭圆壳（每圈 scale_of(z)；out = 外扩的绝对量）。UV：u 沿一圈（从正后方起，uv_rep 次），v = v_of(z)。"""
    bm = bmesh.new()
    uvl = bm.loops.layers.uv.new('UVMap')
    vv = v_of or (lambda z: (z - zs[0]) / (zs[-1] - zs[0]))
    loops = []
    for z in zs:
        s = scale_of(z)
        loops.append([bm.verts.new((x, y, z)) for x, y in se_ring(A * s + out, B * s + out)])
    names = []
    for k in range(len(zs) - 1):
        mn = mat_of((zs[k] + zs[k + 1]) / 2)
        if mn not in names:
            names.append(mn)
        for i in range(SEG):
            j = (i + 1) % SEG
            f = bm.faces.new([loops[k][i], loops[k][j], loops[k + 1][j], loops[k + 1][i]])
            f.material_index = names.index(mn)
            for lp, (ii, kk) in zip(f.loops, [(i, k), (i + 1, k), (i + 1, k + 1), (i, k + 1)]):
                lp[uvl].uv = (ii / SEG * uv_rep, vv(zs[kk]))
    if cap:
        for ring, z in ((list(reversed(loops[0])), zs[0]), (loops[-1], zs[-1])):
            mn = mat_of(z)
            if mn not in names:
                names.append(mn)
            f = bm.faces.new(ring)
            f.material_index = names.index(mn)
    bm.normal_update()
    return new_obj(name, bm, names)


def se_annulus(name, z0, z1, a_out, b_out, a_in, b_in, material='Gold'):
    """超椭圆环带（外壁 + 内壁 + 上下面），金边、盖沿用。"""
    bm = bmesh.new()
    ro = [[bm.verts.new((x, y, z)) for x, y in se_ring(a_out, b_out)] for z in (z0, z1)]
    ri = [[bm.verts.new((x, y, z)) for x, y in se_ring(a_in, b_in)] for z in (z0, z1)]
    for i in range(SEG):
        j = (i + 1) % SEG
        bm.faces.new([ro[0][i], ro[0][j], ro[1][j], ro[1][i]])
        bm.faces.new([ri[0][j], ri[0][i], ri[1][i], ri[1][j]])
        bm.faces.new([ro[1][i], ro[1][j], ri[1][j], ri[1][i]])
        bm.faces.new([ro[0][j], ro[0][i], ri[0][i], ri[0][j]])
    bm.normal_update()
    return new_obj(name, bm, [material])


def se_slab(name, a, b, z0, z1, material):
    """超椭圆实心板（上下封口）。"""
    bm = bmesh.new()
    lo = [bm.verts.new((x, y, z0)) for x, y in se_ring(a, b)]
    hi = [bm.verts.new((x, y, z1)) for x, y in se_ring(a, b)]
    for i in range(SEG):
        j = (i + 1) % SEG
        bm.faces.new([lo[i], lo[j], hi[j], hi[i]])
    bm.faces.new(list(reversed(lo)))
    bm.faces.new(hi)
    bm.normal_update()
    return new_obj(name, bm, [material])


def pearls(name, a, b, z, rad, step, material='Pearl'):
    bm = bmesh.new()
    for x, y in se_even(a, b, step):
        bmesh.ops.create_uvsphere(bm, u_segments=16, v_segments=10, radius=rad, matrix=mathutils.Matrix.Translation((x, y, z)))
    return new_obj(name, bm, [material])


def disc(name, cx, cy, z, r, n, material, down=True, uv=None):
    bm = bmesh.new()
    uvl = bm.loops.layers.uv.new('UVMap')
    vs = [bm.verts.new((cx + r * math.cos(2 * math.pi * k / n), cy + r * math.sin(2 * math.pi * k / n), z)) for k in range(n)]
    f = bm.faces.new(list(reversed(vs)) if down else vs)
    if uv:
        for lp in f.loops:
            lp[uvl].uv = uv(lp.vert.co)
    bm.normal_update()
    return new_obj(name, bm, [material])


def ring_flat(name, cx, cy, z0, z1, r_in, r_out, n, material='Gold'):
    bm = bmesh.new()
    o = [[bm.verts.new((cx + r_out * math.cos(2 * math.pi * k / n), cy + r_out * math.sin(2 * math.pi * k / n), z)) for k in range(n)] for z in (z0, z1)]
    i_ = [[bm.verts.new((cx + r_in * math.cos(2 * math.pi * k / n), cy + r_in * math.sin(2 * math.pi * k / n), z)) for k in range(n)] for z in (z0, z1)]
    for k in range(n):
        j = (k + 1) % n
        bm.faces.new([o[0][k], o[0][j], o[1][j], o[1][k]])
        bm.faces.new([i_[0][j], i_[0][k], i_[1][k], i_[1][j]])
        bm.faces.new([o[1][k], o[1][j], i_[1][j], i_[1][k]])
        bm.faces.new([o[0][j], o[0][k], i_[0][k], i_[0][j]])
    bm.normal_update()
    return new_obj(name, bm, [material])


def tube(name, pts, minor, pn=(0, 0, 1), nseg=16, material='Gold'):
    """沿闭合折线 pts（3D）扫一个半径 minor 的小圆；pn = 路径所在平面的法线。"""
    bm = bmesh.new()
    pn = Vector(pn).normalized()
    n = len(pts)
    rings = []
    for i in range(n):
        p0, p1, p2 = Vector(pts[i - 1]), Vector(pts[i]), Vector(pts[(i + 1) % n])
        t = (p2 - p0).normalized()
        out = t.cross(pn).normalized()
        rings.append([bm.verts.new(p1 + (out * math.cos(2 * math.pi * k / nseg) + pn * math.sin(2 * math.pi * k / nseg)) * minor) for k in range(nseg)])
    for i in range(n):
        a_, b_ = rings[i], rings[(i + 1) % n]
        for k in range(nseg):
            j = (k + 1) % nseg
            bm.faces.new([a_[k], b_[k], b_[j], a_[j]])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    return new_obj(name, bm, [material])


def se_tube(name, a, b, z, minor, n=SEG):
    return tube(name, [(x, y, z) for x, y in se_ring(a, b, n)], minor)


def stadium_pts(w, h, r, n=12):
    """圆角矩形轮廓（xz 平面，逆时针），圆角半径 r。"""
    pts = []
    for cx, cz, a0 in [(w / 2 - r, h / 2 - r, 0), (-w / 2 + r, h / 2 - r, 90), (-w / 2 + r, -h / 2 + r, 180), (w / 2 - r, -h / 2 + r, 270)]:
        for k in range(n + 1):
            a = math.radians(a0 + 90 * k / n)
            pts.append((cx + r * math.cos(a), cz + r * math.sin(a)))
    return pts
def finish2(o, uv=0.3, smooth=35):
    """KEEP_UV 材质的面保留放样时算好的 UV，其余面盒式投影；按角度平滑。"""
    bpy.ops.object.select_all(action='DESELECT')
    o.select_set(True)
    bpy.context.view_layer.objects.active = o
    keep = {i for i, m in enumerate(o.data.materials) if m and m.name.split('.')[0] in KEEP_UV}
    bpy.ops.object.mode_set(mode='EDIT')
    bm = bmesh.from_edit_mesh(o.data)
    for v in bm.verts:
        v.select_set(False)
    for f in bm.faces:
        f.select_set(f.material_index not in keep)
    bm.select_flush_mode()
    bmesh.update_edit_mesh(o.data)
    bpy.ops.uv.cube_project(cube_size=uv)
    bpy.ops.object.mode_set(mode='OBJECT')
    try:
        bpy.ops.object.shade_smooth_by_angle(angle=math.radians(smooth))
    except Exception:
        bpy.ops.object.shade_smooth()


def join2(objs, name):
    for o in objs:
        if o.type == 'MESH' and not o.data.uv_layers:
            o.data.uv_layers.new(name='UVMap')
    return join(objs, name)


def apply_all(o):
    bpy.ops.object.select_all(action='DESELECT')
    o.select_set(True)
    bpy.context.view_layer.objects.active = o
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)


def surf_point(x, z):
    return Vector((x, surface_y(x, z), z))


def surf_normal(x, z):
    """正面曲面在 (x, z) 处朝外的单位法线（横向 + 竖向都考虑：鼓腹上下两头是斜的）。"""
    e = 1e-4
    tx = surf_point(x + e, z) - surf_point(x - e, z)
    tz = surf_point(x, z + e) - surf_point(x, z - e)
    n = tz.cross(tx).normalized()
    if n.y > 0:
        n = -n
    return n


def place_button(objs, name, x, z, yaw, push=0.0):
    """第十六轮（用户：封面按钮要跟着音乐盒的弧度）：按钮不再是一块平板斜插在弧面上——
    每个顶点按它在按钮上的横向 / 竖向位置，落到盒身曲面对应的点上，再沿那里的法线往外抬（抬多少 = 它原来离按钮正面多远）。
    原点 = 按钮正面中心；物体本地 +Z = 那里的外法线、+Y 朝上（导进 Unity 后按钮节点本地 +Z 朝外，与旧模型一致）。"""
    b = join2(objs, name)
    apply_all(b)
    if b.dimensions.x > 0.03:   # 长圆钮：先细分，正面弯得圆顺
        bpy.ops.object.select_all(action='DESELECT'); b.select_set(True); bpy.context.view_layer.objects.active = b
        bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT'); bpy.ops.mesh.subdivide(number_cuts=2); bpy.ops.object.mode_set(mode='OBJECT')
    n0 = surf_normal(x, z)
    origin = surf_point(x, z) + n0 * push
    right = Vector((0, 0, 1)).cross(n0).normalized()
    if right.x < 0:
        right = -right
    up = n0.cross(right).normalized()
    M = mathutils.Matrix((right, up, n0)).transposed().to_4x4()
    M.translation = origin
    Minv = M.inverted()
    for v in b.data.vertices:
        lx, ly, lz = v.co.x, v.co.y, v.co.z          # 建模坐标：x 右、z 上、y 往盒里（正面 y = 0）
        p = surf_point(x + lx, z + lz) + surf_normal(x + lx, z + lz) * (push - ly)
        v.co = Minv @ p
    b.matrix_world = M
    b.data.update()
    return b
# ---------------- 盒身 ----------------
zs = [BODY_Z0 + (H - BODY_Z0) * k / 48 for k in range(49)]
base = se_loft('Base', zs, s_at, mat_of=lambda zc: 'Porcelain', uv_rep=1.0, v_of=lambda z: 0.31 + 0.38 * (z - BODY_Z0) / (H - BODY_Z0))
cav = se_slab('cav', A * S_TOP - WALLR, B * S_TOP - WALLR, FLOORZ, H + 0.05, 'Velvet')
boolean(base, cav)
# 鼓腹上下两道细金线
fillets = [se_tube('FilletLo', A * s_at(0.0306), B * s_at(0.0306), 0.0306, 0.0008),
           se_tube('FilletHi', A * s_at(0.0806), B * s_at(0.0806), 0.0806, 0.0008)]   # 第十四轮：金线改成细圆线（用户：金边太粗太土）
# 上沿：金边（压住盒口）+ 一圈珍珠
rim = se_annulus('Rim', H - 0.0035, H + 0.0004, A * S_TOP + 0.0006, B * S_TOP + 0.0006, A * S_TOP - WALLR + 0.002, B * S_TOP - WALLR + 0.002)
bevel(rim, 0.0008, 2, 50)
rim = join2([rim, se_tube('RimBead', A * S_TOP + 0.0006, B * S_TOP + 0.0006, H - 0.0016, 0.0019)], 'Rim')
rim_pearls = pearls('RimPearls', A * s_at(H - 0.0098) + 0.0012, B * s_at(H - 0.0098) + 0.0012, H - 0.0098, 0.0024, 0.0054)


# 台座：ogee 弧线鎏金台座，中段一圈镂空卷草（Relief 浮雕贴图沿一圈平铺）
def plinth_s(z):
    t = (z - 0.004) / (BODY_Z0 - 0.004)
    return 0.965 - 0.07 * (t * t * (3 - 2 * t)) + 0.018 * math.sin(math.pi * min(1.0, t * 1.15)) + (0.012 if t > 0.86 else 0.0)


PZ0, PZ1 = 0.008, 0.021     # 浮雕带（加高：镂空卷草看得清）
pzs = sorted(set([round(0.004 + (BODY_Z0 - 0.004) * k / 20, 6) for k in range(21)] + [PZ0, PZ1]))
plinth = se_loft('Plinth', pzs, plinth_s, mat_of=lambda zc: 'Relief' if PZ0 <= zc <= PZ1 else 'Gold',
          uv_rep=round(2 * math.pi * 0.115 / ((PZ1 - PZ0) * RELIEF_ASPECT)), v_of=lambda z: (z - PZ0) / (PZ1 - PZ0))
base_pearls = pearls('BasePearls', A * 0.885 + 0.0012, B * 0.885 + 0.0012, BODY_Z0 + 0.0012, 0.0022, 0.005)
base_pearls2 = pearls('BasePearls2', A * plinth_s(PZ1 + 0.0022) + 0.0012, B * plinth_s(PZ1 + 0.0022) + 0.0012, PZ1 + 0.0022, 0.0017, 0.0042)
feet = []   # 第十四轮：用户要求底座不要脚，台座直接落在桌上
# 盒底内衬（奶油色缎面）
def satin():
    bm = bmesh.new()
    NR = 30
    rows = []
    for ir in range(NR + 1):
        r = ir / NR
        row = []
        for k in range(SEG):
            t = math.pi / 2 + 2 * math.pi * k / SEG
            x, y = se_pt(t, A * S_TOP - WALLR - 0.0012, B * S_TOP - WALLR - 0.0012)
            # 参考图：缎子从盒底一直堆到盒沿下，沿内壁起一道道放射状的软褶
            crease = math.sin(11 * t + 2.5 * math.sin(3 * t) + 1.7 * r)
            fold = (0.5 + 0.5 * crease) ** 1.6 * (0.7 + 0.3 * math.sin(4 * r * math.pi + 2 * t))
            z = FLOORZ + 0.002 + (H - 0.009 - FLOORZ) * r ** 2.4 + 0.0075 * fold * r ** 1.3 * (1 - 0.6 * r ** 8)
            row.append(bm.verts.new((x * r, y * r, z)))
        rows.append(row)
    for ir in range(NR):
        for k in range(SEG):
            j = (k + 1) % SEG
            if ir == 0:
                continue
            bm.faces.new([rows[ir][k], rows[ir][j], rows[ir + 1][j], rows[ir + 1][k]])
    c0 = bm.verts.new((0, 0, FLOORZ + 0.002))
    for k in range(SEG):
        bm.faces.new([c0, rows[1][k], rows[1][(k + 1) % SEG]])
    bmesh.ops.remove_doubles(bm, verts=bm.verts[:], dist=1e-7)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    for f in bm.faces:
        if f.normal.z < 0:
            f.normal_flip()
    return new_obj('Velvet', bm, ['Velvet'])


velvet = satin()

# 正面铭牌（写歌名）：参考图是一块白瓷面 + 素金边，正好卡在上下两道珍珠之间
PLQ_Z = 0.068
PLQ_FRONT = surface_y(0, PLQ_Z) - 0.0016   # 铭牌正面 y（Unity 刻字贴在它前面 0.1 mm）
# 第十六轮：铭牌也顺着盒身弧面弯（平板两头会翘出来、挡住下排按钮）。先在原点建（x 右、z 上、y 往盒里），再逐顶点裹到曲面上
plaque = rounded_prism('Plaque', 0.106, 0.02, 0.006, 0.0, 0.012, segs=24, material='Ivory')
plaque.rotation_euler = (-math.pi / 2, 0, 0)
apply_all(plaque)
bevel(plaque, 0.0018, 3, 50)
def dense(pts, step=0.0015):
    """闭合折线按弧长加密（弯到曲面上之前，长直边要有足够的点，不然中段是一条弦、会切进盒身）。"""
    out = []
    for i in range(len(pts)):
        p0, p1 = pts[i], pts[(i + 1) % len(pts)]
        d = math.dist(p0, p1); n = max(1, int(d / step))
        out += [(p0[0] + (p1[0] - p0[0]) * k / n, p0[1] + (p1[1] - p0[1]) * k / n) for k in range(n)]
    return out


pframe = tube('PlaqueFrame', [(x, 0.0008, z) for x, z in dense(stadium_pts(0.1085, 0.0225, 0.0068, 20))], 0.001, pn=(0, -1, 0))


def wrap(o, xc, zc, push):
    for v in o.data.vertices:
        lx, ly, lz = v.co.x, v.co.y, v.co.z
        v.co = surf_point(xc + lx, zc + lz) + surf_normal(xc + lx, zc + lz) * (push - ly)
    o.data.update()


# 铭牌横向细分一下，弯得圆顺
bpy.ops.object.select_all(action='DESELECT'); plaque.select_set(True); bpy.context.view_layer.objects.active = plaque
bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT'); bpy.ops.mesh.subdivide(number_cuts=3); bpy.ops.object.mode_set(mode='OBJECT')
wrap(plaque, 0.0, PLQ_Z, 0.0016)
wrap(pframe, 0.0, PLQ_Z, 0.0016)
# 后面两个合页
HINGE_Y = B * S_TOP + 0.0035
hinges = [cyl('hinge', 0.0048, 0.03, (sx * 0.045, HINGE_Y - 0.001, H), rot=(0, math.pi / 2, 0), verts=24) for sx in (-1, 1)]
# 第十五轮（用户：音乐盒不要太闪 / 太花）：去掉上沿珍珠、台座第二排珍珠、腰上两道细金线——只留台座上一排珍珠和盒口金边
for _o in [rim_pearls, base_pearls2] + fillets:
    bpy.data.objects.remove(_o, do_unlink=True)
base = join2([base, plinth, rim, base_pearls, velvet, plaque, pframe] + feet + hinges, 'Base')
finish2(base, 0.3)

# ---------------- 盒盖（原点在合页） ----------------
LID_Z0, LID_ZW = H + 0.0033, H + 0.016   # 盖沿：竖直的一圈
DOME_H = 0.024
CEIL = H + 0.0105   # 盖内顶面（象牙白瓷面贴在它下面）
lid = se_slab('Lid', A * S_TOP, B * S_TOP, LID_Z0, LID_ZW, 'Ivory')
dzs = [LID_ZW + DOME_H * math.sin(k / 14 * math.pi / 2) for k in range(15)]


def dome_s(z):
    u = min(1.0, max(0.0, (z - LID_ZW) / DOME_H))
    return S_TOP * (1 - 0.55 * (1 - math.sqrt(max(0.0, 1 - u * u))) ** 0.75)


dome = se_loft('Dome', dzs, dome_s, mat_of=lambda zc: 'Ivory', cap=True)
lid = join2([lid, dome], 'Lid')
boolean(lid, se_slab('lidcav', A * S_TOP - 0.007, B * S_TOP - 0.007, LID_Z0 - 0.01, CEIL, 'Ivory'))
# 第三十五轮（用户：盖沿那圈金圈和盖子之间有缝）：精确布尔挖出来的盖内腔侧壁法线全朝外（背对空腔），
# 开盖后从下面 / 正面看过去全是背面、被剔除，透出后面的墙。把内腔侧壁（离外沿 7 mm 那一圈、盖沿到盖内顶面之间）一律翻成朝向空腔。
_bm = bmesh.new(); _bm.from_mesh(lid.data)
_flip = []
for _f in _bm.faces:
    _c = _f.calc_center_median()
    if not (LID_Z0 - 0.002 < _c.z < CEIL + 0.001):
        continue
    _q = (abs(_c.x) / (A * S_TOP - 0.007)) ** NSE + (abs(_c.y) / (B * S_TOP - 0.007)) ** NSE
    if 0.9 < _q < 1.1 and abs(_f.normal.z) < 0.5 and _f.normal.dot(Vector((-_c.x, -_c.y, 0.0))) < 0:
        _flip.append(_f)
bmesh.ops.reverse_faces(_bm, faces=_flip)
_bm.to_mesh(lid.data); _bm.free()
print('LID CAVITY FLIP', len(_flip))
lid_rim = se_annulus('LidRim', H + 0.0012, H + 0.0048, A * S_TOP + 0.0006, B * S_TOP + 0.0006, A * S_TOP - 0.0075, B * S_TOP - 0.0075)
bevel(lid_rim, 0.0006, 2, 50)
lid_rim = join2([lid_rim, se_tube('LidBead', A * S_TOP + 0.0006, B * S_TOP + 0.0006, H + 0.003, 0.0019)], 'LidRim')
lid_fillet = se_tube('LidFillet', A * S_TOP + 0.0002, B * S_TOP + 0.0002, LID_ZW + 0.0002, 0.0008)
lid_pearls = pearls('LidPearls', A * S_TOP * 0.975, B * S_TOP * 0.975, LID_ZW + 0.0024, 0.0015, 0.0042)
# 穹顶正中：金花托 + 一颗珍珠顶珠
TOPZ = LID_ZW + DOME_H
fin = [cyl('finialCup', 0.0085, 0.0025, (0, 0, TOPZ + 0.0008), verts=24), cyl('finialStem', 0.0032, 0.007, (0, 0, TOPZ + 0.0045), verts=16)]
bpy.ops.mesh.primitive_uv_sphere_add(segments=20, ring_count=10, radius=0.0058, location=(0, 0, TOPZ + 0.0115))
fg = bpy.context.active_object
fg.data.materials.append(mat('Pearl'))
fin.append(fg)
for k in range(8):
    a_ = 2 * math.pi * k / 8
    fin.append(basis_obj('leaf', (0.011 * math.cos(a_), 0.011 * math.sin(a_), TOPZ - 0.0004), (math.cos(a_), math.sin(a_), 0), (-math.sin(a_), math.cos(a_), 0), (0, 0, 1), 0.007, 0.0032, 0.0016))
# 盖内：象牙白瓷面 + 圆形村庄画（金圈 + 一圈小珍珠）
inner = se_slab('LidInner', A * S_TOP - 0.0072, B * S_TOP - 0.0072, CEIL - 0.0016, CEIL + 0.0002, 'Ivory')
# 第十七轮（用户：盖内写标题那一面质感太差）：盖内朝下的那一面换成一张手绘瓷面（金色花边 + 四角小玫瑰，Tex/lid_inner.png）。
# 先删掉 slab 自带的底面，再单独建一个顶点独立的面（不和侧面共用顶点，自动投影不会覆盖它的 UV）
_bm = bmesh.new(); _bm.from_mesh(inner.data)
for _f in [f for f in _bm.faces if f.normal.z < -0.9]:
    _bm.faces.remove(_f)
_bm.to_mesh(inner.data); _bm.free()
IA, IB = A * S_TOP - 0.0072, B * S_TOP - 0.0072
_bm = bmesh.new(); _uv = _bm.loops.layers.uv.new('UVMap')
_vs = [_bm.verts.new((x, y, CEIL - 0.0016)) for x, y in se_ring(IA, IB)]
_f = _bm.faces.new(list(reversed(_vs)))
for _l in _f.loops:
    _l[_uv].uv = (0.5 + _l.vert.co.x / (2 * IA), 0.5 - _l.vert.co.y / (2 * IB))   # 开盖后 −y（盖前沿）在上
_bm.normal_update()
inner_face = new_obj('LidInnerFace', _bm, ['LidInner'])
inner = join2([inner, inner_face], 'LidInner')
PY, PR = 0.017, 0.05      # 画心（偏合页一侧：开盖后在下方，给上方的标题牌让位）
PZ = CEIL - 0.0017
asp = 669 / 1024
painting = disc('Painting', 0, PY, PZ, PR, 96, 'Painting', down=True,
                uv=lambda co: (0.5 + co.x / (2 * PR) * asp, 0.5 - (co.y - PY) / (2 * PR)))
pring = ring_flat('PaintFrame', 0, PY, PZ - 0.0016, PZ + 0.0012, PR - 0.0004, PR + 0.0058, 96)
bevel(pring, 0.0007, 2, 50)
bm_ = bmesh.new()
for k in range(64):
    a_ = 2 * math.pi * k / 64
    bmesh.ops.create_uvsphere(bm_, u_segments=8, v_segments=5, radius=0.0011, matrix=mathutils.Matrix.Translation((math.cos(a_) * (PR + 0.0076), PY + math.sin(a_) * (PR + 0.0076), PZ - 0.0002)))
pbeads = new_obj('PaintPearls', bm_, ['Pearl'])
# 标题牌（第十四轮，用户：标题要做得好看）：一块超椭圆象牙瓷牌，牌面是烘好的金箔字贴图（Tools/make_title_art.py，参数要一致），
# 外圈一道细圆金线，两端各一个小金卷草、上沿（开盖后）一只小贝壳。开盖后在盖内最上面，小岛挡不到。
TP_Y, TP_W, TP_D, TP_N = -0.0612, 0.114, 0.036, 3.2
TP_Z1 = CEIL - 0.0016      # 贴着瓷面
tp_out = [(math.copysign(abs(math.cos(2 * math.pi * k / 96)) ** (2 / TP_N), math.cos(2 * math.pi * k / 96)) * TP_W / 2,
            math.copysign(abs(math.sin(2 * math.pi * k / 96)) ** (2 / TP_N), math.sin(2 * math.pi * k / 96)) * TP_D / 2) for k in range(96)]
bm_ = bmesh.new()
uvl = bm_.loops.layers.uv.new('UVMap')
TZ0 = TP_Z1 - 0.0026
lo = [bm_.verts.new((x, TP_Y + y, TZ0)) for x, y in tp_out]
hi = [bm_.verts.new((x, TP_Y + y, TP_Z1)) for x, y in tp_out]
face = bm_.faces.new(list(reversed([bm_.verts.new(v.co) for v in lo])))   # 朝下（开盖后朝玩家）的牌面；顶点单独一份，不和侧面共用（否则自动投影会连带选中它，UV 被覆盖）
face.material_index = 1
for lp in face.loops:
    co = lp.vert.co
    lp[uvl].uv = ((co.x + TP_W / 2) / TP_W, (TP_Y + TP_D / 2 - co.y) / TP_D)
bm_.faces.new(hi)
for i in range(len(lo)):
    j = (i + 1) % len(lo)
    bm_.faces.new([lo[i], lo[j], hi[j], hi[i]])
bm_.normal_update()
tplq = new_obj('TitlePlaque', bm_, ['Ivory', 'TitleArt'])
tframe = tube('TitleFrame', [(x * (1 + 0.0026 / TP_W * 2), TP_Y + y * (1 + 0.0026 / TP_D * 2), TZ0 + 0.0004) for x, y in tp_out], 0.0012)
tdeco = []
for sx in (-1, 1):
    tdeco.append(basis_obj('tcurl', (sx * (TP_W / 2 + 0.0042), TP_Y, TZ0 + 0.0008), (0, 1, 0), (0, 0, 1), (1, 0, 0), 0.0058, 0.0019, 0.003))
tdeco += shell('tcrest', (0, TP_Y - TP_D / 2 - 0.0028, TZ0 + 0.0006), (0, 0, -1), (0, -1, 0), 0.0055)
TITLE_UNDER = TZ0      # 牌面（朝下）的 z
for _o in (lid_pearls, pbeads):
    bpy.data.objects.remove(_o, do_unlink=True)
for _o in [tframe] + tdeco:
    bpy.data.objects.remove(_o, do_unlink=True)
lid = join2([lid, lid_rim, lid_fillet, inner, painting, pring, tplq] + fin, 'Lid')
finish2(lid, 0.3)
set_origin(lid, (0, HINGE_Y, H))

# ---------------- 按钮（象牙白瓷钮 + 金圈；贴着弧面、转向法线；原点在按钮正面中心） ----------------
ARROW_Z, ARROW_X, ARROW_R = 0.068, 0.0735, 0.0085
PILL_Z, PILL_X, PILL_W, PILL_H = 0.0435, 0.052, 0.04, 0.0165
GEAR_R = 0.0085


def round_btn(name, r, depth=0.009):
    """圆钮：车削出来的白瓷钮，正面微微鼓起（像瓷扣），外圈一道细圆金线。原点在正面最高点，−y 朝外。"""
    bm = bmesh.new()
    prof = [(0.0, 0.0)]
    for k in range(1, 9):                       # 鼓面：中心 0，边上往里 1.4 mm
        t = k / 8
        prof.append((r * 0.9 * t, 0.0014 * t * t))
    prof += [(r * 0.97, 0.0019), (r, 0.0027), (r, depth)]
    N = 48
    rings = [[bm.verts.new((rr * math.cos(2 * math.pi * k / N), yy, rr * math.sin(2 * math.pi * k / N))) for k in range(N)] if rr > 0 else [bm.verts.new((0, yy, 0))] for rr, yy in prof]
    for a_, b_ in zip(rings, rings[1:]):
        for k in range(N):
            j = (k + 1) % N
            if len(a_) == 1:
                bm.faces.new([a_[0], b_[j], b_[k]])
            else:
                bm.faces.new([a_[k], a_[j], b_[j], b_[k]])
    bm.faces.new(rings[-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    b = new_obj(name, bm, ['Ivory'])
    ring = tube(name + 'Ring', [(math.cos(2 * math.pi * k / 96) * (r + 0.0011), 0.0017, math.sin(2 * math.pi * k / 96) * (r + 0.0011)) for k in range(96)], 0.0011, pn=(0, -1, 0))
    return [b, ring]


def pill_slab(nm, ww, hh, rr, d0, d1, material):
    o = rounded_prism(nm, ww, hh, rr, d0, d1, segs=12, material=material)
    o.rotation_euler = (-math.pi / 2, 0, 0)   # 本地 z（厚度）→ 世界 +y（往盒里）
    apply_all(o)
    return o


def pill_btn(name, w, h, depth=0.009):
    """长圆钮：圆角大一点的白瓷钮（边缘倒成圆鼓），外圈一道细圆金线。"""
    b = pill_slab(name, w, h, h / 2 - 0.0002, 0, depth, 'Ivory')
    bevel(b, h * 0.22, 5, 30)
    ring = tube(name + 'Ring', [(x, 0.0017, z) for x, z in dense(stadium_pts(w + 0.0022, h + 0.0022, h / 2 + 0.0011, 20))], 0.0011, pn=(0, -1, 0))
    return [b, ring]
def gear_icon():
    bm = bmesh.new()
    N = 8 * 4
    ro = []
    for k in range(N):
        r_ = 0.0056 if (k % 4) in (1, 2) else 0.0043
        ro.append((r_ * math.cos(2 * math.pi * k / N), r_ * math.sin(2 * math.pi * k / N)))
    hole = [(0.0019 * math.cos(2 * math.pi * k / N), 0.0019 * math.sin(2 * math.pi * k / N)) for k in range(N)]
    y0, y1 = -0.0012, 0.0006
    vo0 = [bm.verts.new((x_, y0, z_)) for x_, z_ in ro]
    vo1 = [bm.verts.new((x_, y1, z_)) for x_, z_ in ro]
    vi0 = [bm.verts.new((x_, y0, z_)) for x_, z_ in hole]
    vi1 = [bm.verts.new((x_, y1, z_)) for x_, z_ in hole]
    for k in range(N):
        j = (k + 1) % N
        bm.faces.new([vo0[k], vo0[j], vi0[j], vi0[k]])
        bm.faces.new([vo1[k], vi1[k], vi1[j], vo1[j]])
        bm.faces.new([vo0[k], vo1[k], vo1[j], vo0[j]])
        bm.faces.new([vi0[k], vi0[j], vi1[j], vi1[k]])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    return new_obj('gearicon', bm, ['Gold'])


def tri_icon(sgn):
    bm = bmesh.new()
    pts = [(sgn * 0.0045, 0), (-sgn * 0.003, 0.0045), (-sgn * 0.003, -0.0045)]
    vs0 = [bm.verts.new((px, -0.0012, pz)) for px, pz in pts]
    vs1 = [bm.verts.new((px, 0.0006, pz)) for px, pz in pts]
    bm.faces.new(vs0)
    bm.faces.new(list(reversed(vs1)))
    for i in range(3):
        j = (i + 1) % 3
        bm.faces.new([vs0[i], vs1[i], vs1[j], vs0[j]])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    return new_obj('tri', bm, ['Gold'])


btns = {}
for name, parts, x, z in [
        ('BtnLeft', round_btn('BtnLeft', ARROW_R) + [tri_icon(-1)], -ARROW_X, ARROW_Z),
        ('BtnRight', round_btn('BtnRight', ARROW_R) + [tri_icon(1)], ARROW_X, ARROW_Z),
        ('BtnStart', pill_btn('BtnStart', PILL_W, PILL_H, 0.01), -PILL_X, PILL_Z),
        ('BtnExit', pill_btn('BtnExit', PILL_W, PILL_H, 0.008), PILL_X, PILL_Z),
        ('BtnSettings', round_btn('BtnSettings', GEAR_R) + [gear_icon()], 0.0, PILL_Z)]:
    b = place_button(parts, name, x, z, surface_yaw(x, z), 0.0034)
    finish2(b, 0.05)
    btns[name] = b
    print('BTN', name, tuple(round(c, 5) for c in b.location), 'yaw', round(math.degrees(surface_yaw(x, z)), 1))
print('GEOM HINGE_Y', round(HINGE_Y, 5), 'PLQ_FRONT', round(PLQ_FRONT, 5), 'PLQ_Z', PLQ_Z, 'TITLE_UNDER', round(TITLE_UNDER, 5), 'TP_Y', round(TP_Y, 5), 'TP_D', TP_D, 'H', H)


# ---------------- 八音盒机芯（盒里靠前；能看见滚筒转、梳齿被拨） ----------------
# Drum：带钉的黄铜滚筒（原点在转轴，沿 X）+ 一端的小齿轮；Tooth00..14：钢梳齿（原点在齿根，绕 X 轴微微振）；Movement：底板、支架、梳背（不动）
FLOOR_TOP = FLOORZ + 0.004
# 前半部垫一层深色木台，把机芯抬到盒沿下面（玩家从斜上方看得见滚筒和梳齿）
SHELF_Z, SHELF_Y = 0.062, -0.028
shelf = rounded_prism('Shelf', W - 2 * WALL - 0.001, (SHELF_Y + D / 2 - WALL) - 0.0005, 0.002, FLOOR_TOP - 0.002, SHELF_Z, segs=2, material='Velvet')   # 参考图：机芯直接垫在缎子上，不要深色木台
shelf.location.y = (SHELF_Y - (D / 2 - WALL)) / 2
bpy.ops.object.select_all(action='DESELECT'); shelf.select_set(True); bpy.context.view_layer.objects.active = shelf; bpy.ops.object.transform_apply(location=True)
bevel(shelf, 0.0015, 2, 50)
shelf_top = rounded_prism('ShelfVelvet', W - 2 * WALL - 0.006, (SHELF_Y + D / 2 - WALL) - 0.006, 0.002, SHELF_Z, SHELF_Z + 0.0015, segs=2, material='Velvet')
shelf_top.location.y = (SHELF_Y - (D / 2 - WALL)) / 2
bpy.ops.object.select_all(action='DESELECT'); shelf_top.select_set(True); bpy.context.view_layer.objects.active = shelf_top; bpy.ops.object.transform_apply(location=True)
# 圆鼓盒：木台和缎面按内腔超椭圆裁边（不戳出弧形前壁）
boolean(shelf, se_slab('shelfclip', A * S_TOP - WALLR - 0.0008, B * S_TOP - WALLR - 0.0008, 0, H, 'Velvet'), op='INTERSECT')
boolean(shelf_top, se_slab('shelfclip2', A * S_TOP - WALLR - 0.003, B * S_TOP - WALLR - 0.003, 0, H, 'Velvet'), op='INTERSECT')
SPINDLE_Z = FLOOR_TOP
FLOOR_TOP = SHELF_Z + 0.0015                     # 以下机芯都坐在木台上
DY, DZ, DR, DL = -0.048, FLOOR_TOP + 0.0125, 0.0095, 0.09
drum = cyl('Drum', DR, DL, (0, DY, DZ), rot=(0, math.pi / 2, 0), verts=40, material='Gold')
bevel(drum, 0.0012, 2, 50)
import random
random.seed(11)
pins = []
for k in range(70):
    a_ = random.uniform(0, 2 * math.pi); x_ = random.choice([i for i in range(15)])
    px = -0.042 + x_ * 0.006
    bpy.ops.mesh.primitive_cylinder_add(vertices=6, radius=0.0004, depth=0.003, location=(px, DY + (DR + 0.0008) * math.cos(a_), DZ + (DR + 0.0008) * math.sin(a_)),
                                        rotation=(a_ - math.pi / 2, 0, 0))
    pn = bpy.context.active_object; pn.data.materials.append(mat('Steel')); pins.append(pn)
# 一端的齿轮
bm = bmesh.new()
gpts = []
for k in range(48):
    a_ = 2 * math.pi * k / 48
    r_ = 0.0125 if (k % 4) in (1, 2) else 0.0108
    gpts.append((r_ * math.cos(a_), r_ * math.sin(a_)))
gx0, gx1 = DL / 2 + 0.002, DL / 2 + 0.0045
g0 = [bm.verts.new((gx0, DY + y_, DZ + z_)) for y_, z_ in gpts]
g1 = [bm.verts.new((gx1, DY + y_, DZ + z_)) for y_, z_ in gpts]
bm.faces.new(g0); bm.faces.new(list(reversed(g1)))
for k in range(48):
    j = (k + 1) % 48
    bm.faces.new([g0[k], g1[k], g1[j], g0[j]])
bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
me = bpy.data.meshes.new('cog'); bm.to_mesh(me); bm.free()
cog = bpy.data.objects.new('cog', me); bpy.context.collection.objects.link(cog); cog.data.materials.append(mat('Gold'))
axle = cyl('axle', 0.0018, DL + 0.016, (0, DY, DZ), rot=(0, math.pi / 2, 0), verts=12, material='Steel')
drum = join([drum, cog, axle] + pins, 'Drum')
finish(drum, 0.05)
set_origin(drum, (0, DY, DZ))
# 底板 + 两个支架 + 梳背
plate = rounded_prism('Plate', 0.13, 0.05, 0.004, FLOOR_TOP, FLOOR_TOP + 0.002, segs=4, material='Gold')
plate.location.y = -0.054
bpy.ops.object.select_all(action='DESELECT'); plate.select_set(True); bpy.context.view_layer.objects.active = plate; bpy.ops.object.transform_apply(location=True)
brackets = []
for sx in (-1, 1):
    bpy.ops.mesh.primitive_cube_add(size=1, location=(sx * (DL / 2 + 0.009), DY, (FLOOR_TOP + DZ) / 2 + 0.002))
    b_ = bpy.context.active_object; b_.scale = (0.004, 0.012, DZ - FLOOR_TOP + 0.006); b_.data.materials.append(mat('Gold')); brackets.append(b_)
CY0, CY1 = -0.076, -0.066                       # 梳背（靠玩家一侧）
bpy.ops.mesh.primitive_cube_add(size=1, location=(0, (CY0 + CY1) / 2, FLOOR_TOP + 0.006))
cb = bpy.context.active_object; cb.scale = (0.095, CY1 - CY0, 0.008); cb.data.materials.append(mat('Steel'))
screws = [cyl('screw', 0.0016, 0.001, (sx * 0.035, (CY0 + CY1) / 2, FLOOR_TOP + 0.0105), verts=12, material='Gold') for sx in (-1, 0, 1)]
# 发条盒（参考图机芯左边那个金色圆筒）+ 顶上一颗小螺丝帽
barrel = [cyl('barrel', 0.0125, 0.02, (-0.068, DY + 0.006, FLOOR_TOP + 0.0125), rot=(0, 0, 0), verts=40, material='Gold'),
          cyl('barrelCap', 0.0135, 0.0018, (-0.068, DY + 0.006, FLOOR_TOP + 0.0234), verts=40, material='Gold'),
          cyl('barrelNut', 0.0035, 0.003, (-0.068, DY + 0.006, FLOOR_TOP + 0.0255), verts=6, material='Steel'),
          cyl('barrelFoot', 0.0145, 0.002, (-0.068, DY + 0.006, FLOOR_TOP + 0.001), verts=40, material='Gold')]
for bb in barrel[:2]:
    bevel(bb, 0.0008, 2, 50)
movement = join([plate, cb, shelf, shelf_top] + brackets + screws + barrel, 'Movement')
bevel(movement, 0.0006, 1, 50)
finish(movement, 0.05)
# 梳齿：15 根，从梳背伸向滚筒，长度从左（低音，长）到右（高音，短）
TZ = FLOOR_TOP + 0.0085
for i in range(15):
    x_ = -0.042 + i * 0.006
    tip_y = DY - math.sqrt(max(1e-8, (DR + 0.0011) ** 2 - (TZ - DZ) ** 2))   # 齿尖刚好碰到钉
    bpy.ops.mesh.primitive_cube_add(size=1, location=(x_, (CY1 + tip_y) / 2, TZ))
    t_ = bpy.context.active_object; t_.scale = (0.0042, tip_y - CY1, 0.0012); t_.data.materials.append(mat('Steel'))
    bpy.ops.object.transform_apply(scale=True)
    t_.name = f'Tooth{i:02d}'
    finish(t_, 0.05)
    set_origin(t_, (x_, CY1, TZ))
# 立柱：小岛立在上面（Unity 里随小岛升起伸长；原点在柱底）
prof = [(0.0115, 0), (0.0115, 0.002), (0.008, 0.0035), (0.0045, 0.006), (0.0035, 0.01), (0.0035, 0.05), (0.005, 0.053), (0.0035, 0.056), (0.0035, 0.06)]
bm = bmesh.new()
rings_ = []
for r_, z_ in prof:
    rings_.append([bm.verts.new((r_ * math.cos(2 * math.pi * k / 24), 0.012 + r_ * math.sin(2 * math.pi * k / 24), SPINDLE_Z + z_)) for k in range(24)])
for a_, b_ in zip(rings_, rings_[1:]):
    for k in range(24):
        j = (k + 1) % 24
        bm.faces.new([a_[k], a_[j], b_[j], b_[k]])
bm.faces.new(list(reversed(rings_[0]))); bm.faces.new(rings_[-1])
bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
me = bpy.data.meshes.new('Spindle'); bm.to_mesh(me); bm.free()
sp = bpy.data.objects.new('Spindle', me); bpy.context.collection.objects.link(sp); sp.data.materials.append(mat('Gold'))
finish(sp, 0.05)
set_origin(sp, (0, 0.012, SPINDLE_Z))

for o in bpy.data.objects:
    print(o.name, len(o.data.polygons) if o.type == 'MESH' else '-')
bpy.ops.export_scene.fbx(filepath=out, use_selection=False, apply_unit_scale=True, apply_scale_options='FBX_SCALE_UNITS',
                         axis_forward='-Z', axis_up='Y', object_types={'MESH'}, mesh_smooth_type='FACE')
print('导出', out)
