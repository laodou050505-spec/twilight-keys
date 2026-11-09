"""破洞定位（Blender 里运行）：Blender -b -P Tools/hole_report.py -- <a.fbx> [...]
焊接重合顶点后找边界环，报告每个环的边数、周长、中心位置（相对包围盒的比例），判断洞在哪、多大。"""
import bpy, bmesh, sys
from mathutils import Vector
for fp in sys.argv[sys.argv.index('--') + 1:]:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=fp)
    bm = bmesh.new()
    for o in bpy.context.scene.objects:
        if o.type == 'MESH':
            t = bmesh.new(); t.from_mesh(o.data); t.transform(o.matrix_world); me = bpy.data.meshes.new('t'); t.to_mesh(me); t.free(); bm.from_mesh(me)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-6)
    lo = Vector([min(v.co[i] for v in bm.verts) for i in range(3)]); hi = Vector([max(v.co[i] for v in bm.verts) for i in range(3)])
    bnd = [e for e in bm.edges if len(e.link_faces) == 1]; seen = set(); loops = []
    for e in bnd:
        if e in seen: continue
        st, comp = [e], []
        seen.add(e)
        while st:
            x = st.pop(); comp.append(x)
            for v in x.verts:
                for y in v.link_edges:
                    if y not in seen and len(y.link_faces) == 1: seen.add(y); st.append(y)
        loops.append(comp)
    print('HOLES', fp.split('/')[-1], 'loops', len(loops), 'size', [round(x, 2) for x in (hi - lo)])
    for c in loops:
        cen = sum((v.co for e in c for v in e.verts), Vector()) / (2 * len(c))
        rel = [round((cen[i] - lo[i]) / max(hi[i] - lo[i], 1e-6), 2) for i in range(3)]
        print('   loop edges', len(c), 'perim', round(sum(e.calc_length() for e in c), 4), 'rel', rel)
