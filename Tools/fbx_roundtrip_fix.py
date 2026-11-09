"""FBX 往返修复：Blender -b -P Tools/fbx_roundtrip_fix.py -- <file.fbx> [轮数=5]
导入 FBX → 检查边界/非流形 → 删问题周围两圈面、封孔、三角化、重算法线 → 覆盖导出 → 再导入复查，直到封闭。
用于导出后才暴露的问题（导入端剔除退化面）。"""
import bpy, bmesh, sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from normals_util import orient_outward
argv = sys.argv[sys.argv.index('--') + 1:]
fp = argv[0]; rounds = int(argv[1]) if len(argv) > 1 else 5
GENTLE = len(argv) > 2 and argv[2] == 'gentle'   # 只封小孔（≤8 边的边界环），不删周围面

def load():
    bpy.ops.wm.read_factory_settings(use_empty=True); bpy.ops.import_scene.fbx(filepath=fp)
    return [o for o in bpy.context.scene.objects if o.type == 'MESH']

def bad_count(o):
    bm = bmesh.new(); bm.from_mesh(o.data)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-6)
    n = sum(1 for e in bm.edges if len(e.link_faces) != 2)
    bm.free(); return n

for it in range(rounds):
    objs = load()
    total = sum(bad_count(o) for o in objs)
    print('ROUND', it, 'bad edges', total)
    if total == 0:
        break
    for o in objs:
        bm = bmesh.new(); bm.from_mesh(o.data)
        bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
        # 先删重复面（同一组顶点的面），导入端会把它们当重复剔除
        seen = {}; dup = []
        for f in bm.faces:
            k = tuple(sorted(v.index for v in f.verts))
            if k in seen: dup.append(f)
            else: seen[k] = f
        if dup: bmesh.ops.delete(bm, geom=dup, context='FACES')
        bad = {v for e in bm.edges if len(e.link_faces) != 2 for v in e.verts}
        bad |= {v for f in bm.faces if f.calc_area() < 1e-7 for v in f.verts}
        if bad and GENTLE:
            res = bmesh.ops.holes_fill(bm, edges=[e for e in bm.edges if len(e.link_faces) == 1], sides=8)
            nf_ = res.get('faces', [])
            if nf_: bmesh.ops.triangulate(bm, faces=nf_, quad_method='BEAUTY', ngon_method='BEAUTY')
        elif bad:
            for _ in range(2):
                bad |= {w for v in list(bad) for e in v.link_edges for w in e.verts}
            bmesh.ops.delete(bm, geom=list({f for v in bad for f in v.link_faces}), context='FACES')
            loose = [v for v in bm.verts if not v.link_faces]
            if loose: bmesh.ops.delete(bm, geom=loose, context='VERTS')
            res = bmesh.ops.holes_fill(bm, edges=bm.edges, sides=0)
            # 只对补出来的面三角化 + beauty（原有面不动）
            nf_ = res.get('faces', [])
            if nf_:
                tri = bmesh.ops.triangulate(bm, faces=nf_, quad_method='BEAUTY', ngon_method='BEAUTY')
                tf = tri['faces']
                try:
                    bmesh.ops.beautify_fill(bm, faces=tf, edges=[e for f in tf for e in f.edges if all(x in tf for x in e.link_faces)])
                except Exception:
                    pass
        orient_outward(bm, max(o.dimensions) or 1.0)
        bm.to_mesh(o.data); bm.free()
        for p in o.data.polygons: p.use_smooth = True
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs: o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.export_scene.fbx(filepath=fp, use_selection=True, apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL',
                             axis_forward='-Z', axis_up='Y', object_types={'MESH'}, mesh_smooth_type='FACE',
                             bake_space_transform=False, colors_type='LINEAR')
objs = load(); print('FINAL', sum(bad_count(o) for o in objs))
