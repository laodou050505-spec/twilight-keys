"""背面剔除渲染（Blender 里运行）：Blender -b -P Tools/render_backface.py -- <输出目录> <a.fbx> [b.fbx ...]
背景洋红色，开启背面剔除：法线朝里的面、漏面都会变成透出洋红的洞。每个模型 6 个角度（前/后/右/左/仰/俯）。"""
import bpy, sys, os
from mathutils import Vector

argv = sys.argv[sys.argv.index('--') + 1:]
out, files = argv[0], argv[1:]
os.makedirs(out, exist_ok=True)
for fp in files:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    (bpy.ops.import_scene.gltf if fp.endswith('.glb') else bpy.ops.import_scene.fbx)(filepath=fp)
    ms = [o for o in bpy.context.scene.objects if o.type == 'MESH']
    pts = [o.matrix_world @ Vector(c) for o in ms for c in o.bound_box]
    c = sum(pts, Vector()) / len(pts)
    r = max((p - c).length for p in pts)
    sc = bpy.context.scene
    sc.render.engine = 'BLENDER_WORKBENCH'
    sc.render.resolution_x = sc.render.resolution_y = 420
    sc.display.shading.show_backface_culling = True
    sc.display.shading.light = 'STUDIO'
    sc.display.shading.color_type = 'SINGLE'
    sc.display.shading.single_color = (0.8, 0.78, 0.74)
    w = bpy.data.worlds.new('w'); sc.world = w; w.color = (1, 0, 1)
    cd = bpy.data.cameras.new('c'); cam = bpy.data.objects.new('c', cd); sc.collection.objects.link(cam); sc.camera = cam
    name = os.path.basename(fp).replace('.fbx', '').replace('.glb', '')
    for i, d in enumerate([(0, -1, 0.2), (0, 1, 0.2), (1, 0, 0.2), (-1, 0, 0.2), (0.4, -0.4, -0.8), (0.3, -0.3, 0.9)]):
        dv = Vector(d).normalized()
        cam.location = c + dv * r * 2.4
        cam.rotation_euler = (c - cam.location).to_track_quat('-Z', 'Y').to_euler()
        sc.render.filepath = os.path.join(out, f'{name}_{i}.png')
        bpy.ops.render.render(write_still=True)
    print('RENDERED', name)
