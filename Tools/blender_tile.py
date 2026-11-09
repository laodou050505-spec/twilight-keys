"""音块：圆角磨砂玻璃块（宽 × 高 × 长 = 0.066 × 0.018 × 0.05 m），底面中心在原点，+Y 向上，长度沿 Unity +Z。
Blender -b -P Tools/blender_tile.py -- <输出 fbx>"""
import bpy, sys
out = sys.argv[sys.argv.index('--') + 1]
bpy.ops.object.select_all(action='SELECT'); bpy.ops.object.delete()
W, H, L = 0.066, 0.018, 0.05
bpy.ops.mesh.primitive_cube_add(size=1, location=(0, 0, H / 2))
o = bpy.context.active_object; o.name = 'Tile'; o.scale = (W, L, H)
bpy.ops.object.transform_apply(scale=True)
b = o.modifiers.new('bv', 'BEVEL'); b.width = 0.0065; b.segments = 5; b.harden_normals = False
bpy.ops.object.modifier_apply(modifier='bv')
bpy.ops.object.shade_smooth()
bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT'); bpy.ops.uv.cube_project(cube_size=0.07); bpy.ops.object.mode_set(mode='OBJECT')
print('faces', len(o.data.polygons))
bpy.ops.export_scene.fbx(filepath=out, use_selection=False, apply_unit_scale=True, apply_scale_options='FBX_SCALE_UNITS', axis_forward='-Z', axis_up='Y', object_types={'MESH'})
