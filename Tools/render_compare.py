"""修复前后对比渲染：Blender -b -P Tools/render_compare.py -- <a.glb> <b.glb> <out.png>
每个模型 4 个角度（前/后/左/3/4 俯），EEVEE 带贴图 + 背面剔除（漏面会透出背景）。上排 a，下排 b。"""
import bpy, sys, os, tempfile, math
from mathutils import Vector
argv = sys.argv[sys.argv.index('--') + 1:]
A, Bp, out = argv
tmp = tempfile.mkdtemp(); tiles = []
for row, fp in enumerate((A, Bp)):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=fp)
    ms = [o for o in bpy.context.scene.objects if o.type == 'MESH']
    for o in ms:
        for m in o.data.materials:
            if m: m.use_backface_culling = True
    pts = [o.matrix_world @ Vector(c) for o in ms for c in o.bound_box]
    mn = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
    mx = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    c = (mn + mx) / 2; r = (mx - mn).length
    sc = bpy.context.scene
    sc.render.engine = 'BLENDER_EEVEE_NEXT' if 'BLENDER_EEVEE_NEXT' in [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties['engine'].enum_items] else 'BLENDER_EEVEE'
    sc.render.resolution_x = sc.render.resolution_y = 512
    w = bpy.data.worlds.new('w'); sc.world = w; w.use_nodes = True
    w.node_tree.nodes['Background'].inputs[0].default_value = (0.9, 0.2, 0.6, 1); w.node_tree.nodes['Background'].inputs[1].default_value = 1.0
    cd = bpy.data.cameras.new('c'); cd.type = 'ORTHO'; cd.ortho_scale = r * 1.05
    cam = bpy.data.objects.new('c', cd); sc.collection.objects.link(cam); sc.camera = cam
    for i, d in enumerate([(0, -1, 0.1), (0, 1, 0.1), (-1, 0, 0.1), (0.6, -0.6, 0.7)]):
        dv = Vector(d).normalized(); cam.location = c + dv * r * 2
        cam.rotation_euler = (c - cam.location).to_track_quat('-Z', 'Y').to_euler()
        p = os.path.join(tmp, f'{row}_{i}.png'); sc.render.filepath = p
        bpy.ops.render.render(write_still=True); tiles.append(p)
import subprocess
print('TILES', ' '.join(tiles))
