"""手绘木牌：按轮廓（cutout_board.py 输出的 _outline.json）挤出厚度、倒角，正面 / 背面按原图平面投影 UV（侧面取边缘颜色）。
Blender -b -P Tools/blender_board.py -- <outline.json> <输出 fbx> <高度米> <厚度米>
约定同其它道具：Blender +Y → Unity +Z，正面朝 Blender -Y（导进 Unity 后转 180°），底边中点在原点。"""
import bpy, bmesh, json, sys
from mathutils import Vector

args = sys.argv[sys.argv.index('--') + 1:]
outline, out, H, T = args[0], args[1], float(args[2]), float(args[3])
d = json.load(open(outline))
W = H * d['aspect']
bpy.ops.object.select_all(action='SELECT'); bpy.ops.object.delete()

bm = bmesh.new()
verts = [bm.verts.new(((u - 0.5) * W, 0.0, v * H)) for u, v in d['points']]
face = bm.faces.new(verts)
bmesh.ops.triangulate(bm, faces=[face], quad_method='BEAUTY', ngon_method='EAR_CLIP')
# 挤出厚度（往 +Y，正面留在 Y = 0 → 之后整体往 -Y 挪半个厚度，让中心在原点）
geom = bm.faces[:]
res = bmesh.ops.extrude_face_region(bm, geom=geom)
for v in [e for e in res['geom'] if isinstance(e, bmesh.types.BMVert)]:
    v.co.y += T
for v in bm.verts: v.co.y -= T / 2
bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
me = bpy.data.meshes.new('Board'); bm.to_mesh(me); bm.free()
o = bpy.data.objects.new('Board', me); bpy.context.collection.objects.link(o)
bpy.context.view_layer.objects.active = o; o.select_set(True)
# 倒角（软一点，手绘风格的圆润边）
b = o.modifiers.new('bv', 'BEVEL'); b.width = T * 0.32; b.segments = 3; b.limit_method = 'ANGLE'; b.angle_limit = 0.6
bpy.ops.object.modifier_apply(modifier='bv')
# UV：所有面按正面平面投影（x → u，z → v）；背面镜像
uv = o.data.uv_layers.new(name='UV')
for poly in o.data.polygons:
    for li in poly.loop_indices:
        co = o.data.vertices[o.data.loops[li].vertex_index].co
        u = co.x / W + 0.5
        uv.data[li].uv = (u if poly.normal.y <= 0.5 else 1 - u, co.z / H)
o.data.materials.append(bpy.data.materials.new('Board'))
bpy.ops.object.shade_smooth()
try:
    bpy.ops.object.shade_smooth_by_angle(angle=0.6)
except Exception:
    pass
print('面数', len(o.data.polygons), '尺寸', round(W, 3), 'x', H, 'x', T)
bpy.ops.export_scene.fbx(filepath=out, use_selection=False, apply_unit_scale=True, apply_scale_options='FBX_SCALE_UNITS',
                         axis_forward='-Z', axis_up='Y', object_types={'MESH'}, mesh_smooth_type='FACE')
