"""尖刺/形变检测：Blender -b -P Tools/spike_check.py -- <out.json> <name> <orig.glb> <repaired.glb> [...成对]
指标：
  - bbox_grow：修复后包围盒每轴超出原包围盒的最大比例（>1.5% 视为长出尖刺）
  - long_edges：长度 > 中位边长 × 25 的边数（尖刺/拉丝面）
  - far_verts：离原模型表面 > 2% 包围盒对角线的顶点数（BVH 最近点）
"""
import bpy, bmesh, sys, json, statistics
from mathutils import Vector
from mathutils.bvhtree import BVHTree
argv = sys.argv[sys.argv.index('--') + 1:]
out = argv[0]; rest = argv[1:]; rep = {}
def load(fp):
    bpy.ops.wm.read_factory_settings(use_empty=True); bpy.ops.import_scene.gltf(filepath=fp)
    bm = bmesh.new()
    for o in bpy.context.scene.objects:
        if o.type == 'MESH':
            t = bmesh.new(); t.from_mesh(o.data); t.transform(o.matrix_world)
            me = bpy.data.meshes.new('t'); t.to_mesh(me); t.free(); bm.from_mesh(me)
    return bm
for i in range(0, len(rest), 3):
    name, a, b = rest[i:i + 3]
    A = load(a); B = load(b)
    def bb(bm):
        return [min(v.co[k] for v in bm.verts) for k in range(3)], [max(v.co[k] for v in bm.verts) for k in range(3)]
    (amn, amx), (bmn, bmx) = bb(A), bb(B)
    diag = (Vector(amx) - Vector(amn)).length
    grow = max(max(amn[k] - bmn[k], bmx[k] - amx[k]) / max(1e-6, amx[k] - amn[k]) for k in range(3))
    lens = [e.calc_length() for e in B.edges]; med = statistics.median(lens)
    longe = sum(1 for l in lens if l > med * 25)
    A.verts.ensure_lookup_table(); A.faces.ensure_lookup_table()
    tree = BVHTree.FromBMesh(A)
    far = 0
    for v in B.verts:
        hit = tree.find_nearest(v.co)
        if hit[0] is None or hit[3] > diag * 0.02: far += 1
    r = dict(bbox_grow=round(grow, 4), long_edges=longe, far_verts=far, verts=len(B.verts))
    r['pass'] = grow < 0.015 and longe == 0 and far < max(5, len(B.verts) * 0.001)
    rep[name] = r; print('SPIKE', name, json.dumps(r))
open(out, 'w').write(json.dumps(rep, indent=2))
