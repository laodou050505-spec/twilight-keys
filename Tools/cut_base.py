"""切掉 P2 棋子自带的底座（底座大小参差、比队列间距还宽，会互相穿插），换成统一的程序化底座（Unity 里加）。
用法：Blender -b -P Tools/cut_base.py -- <名称...>
对 ArtSource/ModelsP2/<名>/final/lod0..2.fbx：
  1. 找底座顶面：最下方 20% 高度里，朝上平面面积最大的高度（底座顶是一大片平面）；
  2. 在顶面上方 0.4% 身高处水平切开，扔掉下面，切口逐环补面（脚底、剑尖都变成平的）；
  3. 移到原点：切口在 y=0，水平居中按原来的底座中心；导出 final/cut_lodN.fbx，并写 final/cut.json（切掉的底座半径、身高等）。
导出后复查开放边 / 非流形，不为 0 就报 FAIL。"""
import bpy, bmesh, json, pathlib, sys
from mathutils import Vector

ROOT = pathlib.Path(__file__).resolve().parents[1] / 'ArtSource' / 'ModelsP2'
names = sys.argv[sys.argv.index('--') + 1:]


def load(path):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(path))
    meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
    bpy.ops.object.select_all(action='DESELECT')
    for o in meshes: o.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]
    if len(meshes) > 1: bpy.ops.object.join()
    obj = bpy.context.view_layer.objects.active
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    return obj


def base_top(bm, up):
    """竖直轴上（up = 1 是 Z 轴，FBX 导入后 Blender 里是 Z 朝上），找朝上平面面积最大的高度。"""
    zs = [v.co.z for v in bm.verts]
    lo, hi = min(zs), max(zs)
    h = hi - lo
    bins = {}
    for f in bm.faces:
        if f.normal.z < 0.97: continue
        c = f.calc_center_median().z
        if c > lo + h * 0.2: continue
        k = round((c - lo) / h, 3)
        bins[k] = bins.get(k, 0.0) + f.calc_area()
    k = max(bins, key=bins.get)
    return lo + k * h, lo, h


def run(name):
    fin = ROOT / name / 'final'
    if not (fin / 'lod0.fbx').exists():
        print(name, 'no final'); return
    info = {}
    for i in range(3):
        src = fin / f'lod{i}.fbx'
        if not src.exists(): continue
        obj = load(src)
        bm = bmesh.new(); bm.from_mesh(obj.data)
        bm.normal_update()
        if i == 0:
            top, lo, h = base_top(bm, 1)
            cut = top + h * 0.004
            # 底座中心与半径：取底座顶面那一层的顶点
            ring = [v.co for v in bm.verts if abs(v.co.z - top) < h * 0.004]
            cx = sum(p.x for p in ring) / len(ring); cy = sum(p.y for p in ring) / len(ring)
            r = max(((p.x - cx) ** 2 + (p.y - cy) ** 2) ** 0.5 for p in ring)
            info = {'cut': cut, 'lo': lo, 'height_full': h, 'base_top': top, 'base_radius': r, 'center': [cx, cy]}
        cut, cx, cy = info['cut'], info['center'][0], info['center'][1]
        geom = bm.verts[:] + bm.edges[:] + bm.faces[:]
        res = bmesh.ops.bisect_plane(bm, geom=geom, plane_co=(0, 0, cut), plane_no=(0, 0, 1), clear_inner=True)
        edges = [e for e in res['geom_cut'] if isinstance(e, bmesh.types.BMEdge)]
        # 切口逐环补面
        bnd = [e for e in bm.edges if e.is_boundary]
        filled = bmesh.ops.holes_fill(bm, edges=bnd, sides=0)
        bmesh.ops.triangulate(bm, faces=filled['faces'])
        # 兜底：holes_fill 补不上的环（低 LOD 切口自相接触），沿开放边逐环走，中心加一点扇形补面
        for _ in range(4):
            rest = [e for e in bm.edges if e.is_boundary]
            if not rest: break
            used = set()
            for e0 in rest:
                if e0 in used: continue
                loop, v, e = [e0.verts[0]], e0.verts[1], e0
                used.add(e0)
                while v is not loop[0] and len(loop) < 5000:
                    loop.append(v)
                    nxt = [x for x in v.link_edges if x.is_boundary and x not in used]
                    if not nxt: break
                    e = nxt[0]; used.add(e); v = e.other_vert(v)
                if len(loop) < 3: continue
                c = sum((x.co for x in loop), Vector()) / len(loop)
                cv = bm.verts.new(c)
                for a, b in zip(loop, loop[1:] + loop[:1]):
                    try: bm.faces.new((a, b, cv))
                    except ValueError: pass
            bm.normal_update()
        loose = [v for v in bm.verts if not v.link_faces]
        if loose: bmesh.ops.delete(bm, geom=loose, context='VERTS')
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        for v in bm.verts: v.co -= Vector((cx, cy, cut))
        bnd = sum(1 for e in bm.edges if e.is_boundary)
        nm = sum(1 for e in bm.edges if not e.is_manifold and not e.is_boundary)
        bm.to_mesh(obj.data); bm.free()
        obj.data.update()
        bpy.ops.export_scene.fbx(filepath=str(fin / f'cut_lod{i}.fbx'), use_selection=False, apply_scale_options='FBX_SCALE_ALL', bake_space_transform=True, path_mode='COPY', embed_textures=False)
        info[f'lod{i}'] = {'boundary': bnd, 'nonmanifold': nm, 'tris': len(obj.data.polygons)}
        print(name, f'lod{i}', 'boundary', bnd, 'nonmanifold', nm, 'PASS' if bnd == 0 and nm == 0 else 'FAIL', flush=True)
    info['height_cut'] = info['height_full'] - (info['cut'] - info['lo'])
    (fin / 'cut.json').write_text(json.dumps(info, indent=1))


for n in names:
    run(n)
