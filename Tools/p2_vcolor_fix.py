"""九霄雷境 · 不可靠贴图面改用顶点色（由翠梦雨林 rf_vcolor_fix_20261022.py 改写，2026-10-26）。
第二轮（用户：不许破面、不许糊）：补洞面原来逐角点取最近好面的颜色，红墙与白边交界处会拉出红白斜纹（宝塔墙面、主殿山墙）。
改为“按补洞片整块上色”：相连的标记面并成一片，取片边界外侧好面颜色的亮度中位数那一个（红或白，不混成粉色），整片同色并略压暗（像阴影里的凹面）。
以下为原说明：

Tripo 的贴图来自四个水平视角（前、左、后、右）。四个视角都看不到的面（叶片朝天的正面、被遮挡的内侧），
P2 给的 UV 常常是跨好几块图集的大三角或细长条：一个小面里压进一大片无关纹理，看起来就是密集的彩色条纹；
补洞面的 UV 取自洞边界的不同顶点，也会退化成细条。

判据（逐面）：
  - UV 异常：纹素线密度 d = sqrt(UV 面积 / 3D 面积) 相对面积加权中位数 < LOW 或 > HIGH，或 UV 三角形瘦成一条线；
  - 不可见：四个水平方向都没有“正对（|n·dir| ≥ 0.25）且不被遮挡”的视线（叶片双面，两面都算）。
  标记 = UV 异常 或（不可见 且 线密度偏离中位数 ±50% 以上）—— 不可见但 UV 正常的面多半是 P2 补画得还行的，保留贴图。
着色：标记面的每个角点取周围最近的若干个未标记面（按距离加权）的贴图颜色，写进顶点色（sRGB），alpha = 1；
未标记面 alpha = 0。着色器 RfModel 按 alpha 在贴图色与顶点色之间插值。
用法：被 rf_p2_blender_20261021.process 调用；也可单独跑对比图：
  Blender -b --python rf_vcolor_fix_20261022.py -- <in.glb> <输出目录>"""
import math

LOW, HIGH, SLIVER = .3, 3.0, .03
MILD = float(__import__("os").environ.get("RF_VC_MILD", ".5"))   # 不可见面：线密度偏离中位数超过 ±50%（< .67 或 > 1.5）也标记
K = 10


def tex_pixels(obj, res=1024):
    """取模型第一张贴图，缩到 res 后返回 (np 数组 H×W×4（sRGB 值，行 0 在底部）, 原图)"""
    import numpy as np
    for mat in obj.data.materials:
        if not mat or not mat.use_nodes: continue
        for node in mat.node_tree.nodes:
            if node.type == 'TEX_IMAGE' and node.image:
                im = node.image.copy(); im.scale(res, res)
                px = np.array(im.pixels[:], dtype=np.float32).reshape(res, res, 4)
                import bpy; bpy.data.images.remove(im)
                return px
    return None


def recolor(bm, obj, size):
    """在 bmesh 上标记不可靠面并写顶点色属性 'RfFix'（角点域）。返回统计字典。"""
    import numpy as np
    from mathutils import Vector
    from mathutils.bvhtree import BVHTree
    from mathutils.kdtree import KDTree
    uv = bm.loops.layers.uv.active
    px = tex_pixels(obj)
    if uv is None or px is None: return {'skipped': 'no uv/texture'}
    H, W = px.shape[:2]
    bm.faces.ensure_lookup_table()
    faces = list(bm.faces)
    a3 = np.zeros(len(faces)); au = np.zeros(len(faces)); sl = np.ones(len(faces))
    for i, f in enumerate(faces):
        a3[i] = f.calc_area()
        q = [l[uv].uv for l in f.loops]
        s = 0.0
        for j in range(len(q)):
            s += q[j].x * q[(j + 1) % len(q)].y - q[(j + 1) % len(q)].x * q[j].y
        au[i] = abs(s) * .5
        if len(q) == 3:
            lm = max((q[1] - q[0]).length, (q[2] - q[1]).length, (q[0] - q[2]).length)
            sl[i] = 2 * au[i] / max(lm * lm, 1e-20)
    ok = a3 > 0
    d = np.sqrt(au / np.maximum(a3, 1e-20))
    o = np.argsort(d[ok]); w = a3[ok][o]; cw = np.cumsum(w)
    med = d[ok][o][np.searchsorted(cw, cw[-1] * .5)]
    rel = d / max(med, 1e-20)
    anomaly = (rel < LOW) | (rel > HIGH) | (sl < SLIVER)
    # 可见性：四个水平方向（Blender 里 Z 向上）
    tree = BVHTree.FromBMesh(bm)
    dirs = [Vector((1, 0, 0)), Vector((-1, 0, 0)), Vector((0, 1, 0)), Vector((0, -1, 0))]
    vis = np.zeros(len(faces), bool)
    for i, f in enumerate(faces):
        if anomaly[i]: continue
        c = f.calc_center_median(); n = f.normal
        for dv in dirs:
            k = n.dot(dv)
            if abs(k) < .25: continue
            hit = tree.ray_cast(c + n * (1 if k > 0 else -1) * size * 1e-4, dv, size * 3)
            if hit[0] is None: vis[i] = True; break
    mild = np.ones(len(faces), bool) if MILD < 0 else (rel < 1 / (1 + MILD)) | (rel > 1 + MILD)   # MILD < 0：不可见面一律标记
    flag = anomaly | (~vis & mild)
    # 参考色：未标记面的贴图颜色（UV 重心 + 三个角点向重心收 1/3 处，四点平均，避开图集缝的出血）
    good = [i for i in range(len(faces)) if not flag[i] and ok[i]]
    if not good: return {'skipped': 'no reliable faces'}
    kd = KDTree(len(good)); cols = np.zeros((len(good), 3), np.float32)
    for j, i in enumerate(good):
        f = faces[i]; q = [l[uv].uv for l in f.loops]
        cu = sum((p.x for p in q)) / len(q); cv = sum((p.y for p in q)) / len(q)
        acc = np.zeros(3, np.float32)
        pts = [(cu, cv)] + [(cu + (p.x - cu) * .67, cv + (p.y - cv) * .67) for p in q[:3]]
        for (x, y) in pts:
            acc += px[min(H - 1, max(0, int((y % 1.0) * H))), min(W - 1, max(0, int((x % 1.0) * W))), :3]
        cols[j] = acc / len(pts)
        kd.insert(f.calc_center_median(), j)
    kd.balance()
    vcache = {}
    def vcol(v):
        if v.index in vcache: return vcache[v.index]
        acc = np.zeros(3, np.float32); ws = 0.0
        for (co, j, dist) in kd.find_n(v.co, K):
            wgt = 1.0 / (dist + size * 2e-3)
            acc += cols[j] * wgt; ws += wgt
        c = acc / max(ws, 1e-12); vcache[v.index] = c; return c
    layer = bm.loops.layers.float_color.get('RfFix') or bm.loops.layers.float_color.new('RfFix')
    # 按片上色：标记面的连通片 → 片边界外侧相邻的好面颜色 → 取亮度中位数的那个颜色，整片同色 × 0.88
    idx_of = {f: i for i, f in enumerate(faces)}
    gpos = {i: j for j, i in enumerate(good)}
    seen = np.zeros(len(faces), bool); patches = 0
    def to_lin(c): return [((x / 12.92) if x <= .04045 else ((x + .055) / 1.055) ** 2.4) for x in c]
    for i0 in range(len(faces)):
        if not flag[i0] or seen[i0]: continue
        stack = [i0]; seen[i0] = True; patch = []; ring = set()
        while stack:
            i = stack.pop(); patch.append(i)
            for e in faces[i].edges:
                for g in e.link_faces:
                    k = idx_of[g]
                    if flag[k]:
                        if not seen[k]: seen[k] = True; stack.append(k)
                    elif k in gpos: ring.add(gpos[k])
        if not ring:   # 片的四周没有好面：取片重心最近的 K 个好面
            ctr = sum((faces[i].calc_center_median() for i in patch), Vector()) / len(patch)
            ring = {j for (co, j, dd) in kd.find_n(ctr, K)}
        samples = cols[list(ring)]
        lum = samples @ np.array([.299, .587, .114], np.float32)
        c = samples[np.argsort(lum)[len(lum) // 2]] * .88
        lin = to_lin(c)
        for i in patch:
            for l in faces[i].loops: l[layer] = (lin[0], lin[1], lin[2], 1.0)
        patches += 1
    for i, f in enumerate(faces):
        if not flag[i]:
            for l in f.loops: l[layer] = (0.0, 0.0, 0.0, 0.0)
    fa = a3.sum()
    return {'faces': len(faces), 'flagged': int(flag.sum()), 'flaggedAreaPct': round(float(a3[flag].sum() / fa * 100), 2),
            'uvAnomaly': int(anomaly.sum()), 'patches': patches, 'invisibleMild': int((~vis & mild & ~anomaly).sum())}


if __name__ == '__main__':
    # 单独对比：原贴图 vs 修复后，俯视 + 斜视，无光照（只看颜色）
    import bpy, bmesh, sys, os, json
    a = sys.argv[sys.argv.index('--') + 1:]
    src, out = a[0], a[1]; os.makedirs(out, exist_ok=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=src)
    meshes = [o for o in bpy.data.objects if o.type == 'MESH']
    bpy.ops.object.select_all(action='DESELECT')
    for o in meshes: o.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]
    if len(meshes) > 1: bpy.ops.object.join()
    obj = bpy.context.view_layer.objects.active
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    size = max(obj.dimensions)
    bm = bmesh.new(); bm.from_mesh(obj.data)
    st = recolor(bm, obj, size); print('VCFIX', json.dumps(st))
    bm.to_mesh(obj.data); bm.free()
    # 无光照材质：贴图色与 RfFix 顶点色按 alpha 混合
    mat = obj.data.materials[0]; nt = mat.node_tree
    tex = [n for n in nt.nodes if n.type == 'TEX_IMAGE'][0]
    attr = nt.nodes.new('ShaderNodeVertexColor'); attr.layer_name = 'RfFix'
    mix = nt.nodes.new('ShaderNodeMix'); mix.data_type = 'RGBA'
    emi = nt.nodes.new('ShaderNodeEmission'); outn = [n for n in nt.nodes if n.type == 'OUTPUT_MATERIAL'][0]
    nt.links.new(attr.outputs['Alpha'], mix.inputs['Factor']); nt.links.new(tex.outputs['Color'], mix.inputs[6]); nt.links.new(attr.outputs['Color'], mix.inputs[7])
    nt.links.new(mix.outputs[2], emi.inputs['Color']); nt.links.new(emi.outputs['Emission'], outn.inputs['Surface'])
    sc = bpy.context.scene; sc.render.engine = 'BLENDER_EEVEE_NEXT' if 'BLENDER_EEVEE_NEXT' in [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties['engine'].enum_items] else 'BLENDER_EEVEE'
    sc.render.resolution_x = sc.render.resolution_y = 900
    sc.world = bpy.data.worlds.new('w'); sc.world.color = (.35, .38, .42)
    cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam')); sc.collection.objects.link(cam); sc.camera = cam
    cam.data.type = 'ORTHO'; cam.data.ortho_scale = size * 1.15
    ctr = sum((obj.matrix_world @ v.co for v in obj.data.vertices), __import__('mathutils').Vector()) / len(obj.data.vertices)
    from mathutils import Vector
    zero = nt.nodes.new('ShaderNodeValue'); zero.outputs[0].default_value = 0.0
    for name, dv in (('top', Vector((0, 0.001, 1))), ('oblique', Vector((1, -1, 1.2)))):
        for mode in ('orig', 'fixed'):
            nt.links.new(attr.outputs['Alpha'] if mode == 'fixed' else zero.outputs[0], mix.inputs['Factor'])
            cam.location = ctr + dv.normalized() * size * 3
            cam.rotation_euler = (ctr - cam.location).to_track_quat('-Z', 'Y').to_euler()
            sc.render.filepath = os.path.join(out, f'{name}_{mode}.png'); bpy.ops.render.render(write_still=True)
