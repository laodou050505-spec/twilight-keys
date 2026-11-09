"""把一个或多个 FBX 导入 Blender，用 Eevee 渲染一张审查图（三分之四俯视 + 背面剔除，查结构、漏面）。
  Blender -b -P Tools/render_fbx.py -- <输出.png> <a.fbx> [b.fbx ...]"""
import bpy, math, sys
from mathutils import Vector
argv = sys.argv[sys.argv.index('--') + 1:]
out, files = argv[0], argv[1:]
bpy.ops.wm.read_factory_settings(use_empty=True)
for f in files:
    bpy.ops.import_scene.fbx(filepath=f)
objs = [o for o in bpy.context.scene.objects if o.type == 'MESH']
mn = Vector((1e9,) * 3); mx = Vector((-1e9,) * 3)
for o in objs:
    for c in o.bound_box:
        w = o.matrix_world @ Vector(c)
        mn = Vector(map(min, mn, w)); mx = Vector(map(max, mx, w))
    m = bpy.data.materials.new('clay'); m.use_nodes = True
    m.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = (0.78, 0.74, 0.7, 1)
    m.node_tree.nodes['Principled BSDF'].inputs['Roughness'].default_value = 0.8
    m.use_backface_culling = True
    o.data.materials.clear(); o.data.materials.append(m)
c = (mn + mx) / 2; size = max(mx - mn)
sc = bpy.context.scene
sc.render.engine = 'BLENDER_EEVEE_NEXT' if 'BLENDER_EEVEE_NEXT' in [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties['engine'].enum_items] else 'BLENDER_EEVEE'
sc.render.resolution_x = 1400; sc.render.resolution_y = 900
w = bpy.data.worlds.new('w'); sc.world = w; w.use_nodes = True
w.node_tree.nodes['Background'].inputs['Color'].default_value = (0.16, 0.13, 0.22, 1)
w.node_tree.nodes['Background'].inputs['Strength'].default_value = 0.6
sun = bpy.data.objects.new('sun', bpy.data.lights.new('sun', 'SUN')); sun.data.energy = 3.5
sun.rotation_euler = (math.radians(50), math.radians(10), math.radians(35)); sc.collection.objects.link(sun)
cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam')); sc.collection.objects.link(cam); sc.camera = cam
cam.data.lens = 40
d = Vector((0.9, -1.2, 0.75)).normalized()
cam.location = c + d * size * 1.25
cam.rotation_euler = (c - cam.location).to_track_quat('-Z', 'Y').to_euler()
cam.data.clip_end = size * 20
sc.render.filepath = out
bpy.ops.render.render(write_still=True)
print('RENDER', out)
