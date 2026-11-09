"""按原始模型去尖刺（Blender 里运行）：
Blender -b -P Tools/despike_ref.py -- <修复版.glb> <原始 model.glb> <输出.glb>
修复版（封孔/重建过的）与原始 Tripo 模型同一坐标系。修复过程偶尔把个别顶点拉成长刺：
它们离原始表面很远（> 0.8% 包围盒对角线）且连着很长的边。把这些顶点吸回原始表面上的最近点，反复几轮直到没有。
只动这些顶点，其余几何、UV、贴图原样保留。输出前后都报告：远点数、超长边数。"""
import bpy, bmesh, sys, statistics
from mathutils import Vector
from mathutils.bvhtree import BVHTree

src, ref, out = sys.argv[sys.argv.index('--') + 1:][:3]


def load(fp):
    bpy.ops.wm.read_factory_settings(use_empty=True); bpy.ops.import_scene.gltf(filepath=fp)
    return [o for o in bpy.context.scene.objects if o.type == 'MESH']


rms = load(ref)
rb = bmesh.new()
for o in rms:
    t = bmesh.new(); t.from_mesh(o.data); t.transform(o.matrix_world); me = bpy.data.meshes.new('t'); t.to_mesh(me); t.free(); rb.from_mesh(me)
RT = BVHTree.FromBMesh(rb)
lo = Vector([min(v.co[i] for v in rb.verts) for i in range(3)]); hi = Vector([max(v.co[i] for v in rb.verts) for i in range(3)])
diag = (hi - lo).length
rcoords = [v.co.copy() for v in rb.verts]
rb.free()

ms = load(src)
total_fixed = 0
for o in ms:
    mw = o.matrix_world; inv = mw.inverted()
    bm = bmesh.new(); bm.from_mesh(o.data)
    med = statistics.median(e.calc_length() for e in bm.edges) * max(mw.to_scale())
    def scan():
        bad = []
        for v in bm.verts:
            if not v.link_edges: continue
            w = mw @ v.co
            loc, n, i, d = RT.find_nearest(w)
            if d is None or d < 0.008 * diag: continue
            if max(e.calc_length() for e in v.link_edges) * max(mw.to_scale()) > 6 * med:
                bad.append((v, loc))
        return bad
    b0 = scan(); n0 = len(b0)
    for _ in range(6):
        bad = scan()
        if not bad: break
        for v, loc in bad: v.co = inv @ loc
        total_fixed += len(bad)
    print('DESPIKE', o.name, 'far_before', n0, 'far_after', len(scan()))
    bm.to_mesh(o.data); bm.free(); o.data.update()
bpy.ops.object.select_all(action='SELECT')
bpy.ops.export_scene.gltf(filepath=out, export_format='GLB', use_selection=False)
print('DESPIKE_DONE fixed', total_fixed)
