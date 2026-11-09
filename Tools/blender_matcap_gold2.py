"""第十三轮：抛光金材质球（matcap_gold2.png）。Blender -b -P Tools/blender_matcap_gold2.py -- <输出目录>
真金好看靠反差：亮处是一块块清楚的柔光箱 / 窗格反光，暗处反出深褐色的房间，边缘有一圈暖色轮廓。
之前的金只有一个模糊高光 + 整片橙色，看起来像塑料 / 卡通。"""
import bpy, sys, os, math
from mathutils import Vector

out = sys.argv[sys.argv.index('--') + 1]
os.makedirs(out, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
sc = bpy.context.scene
sc.render.engine = 'CYCLES'
sc.cycles.samples = 256
sc.cycles.use_denoising = True
sc.render.resolution_x = sc.render.resolution_y = 512
sc.view_settings.view_transform = 'Standard'

# 环境：深褐房间，上方稍亮（像傍晚的书房）
w = bpy.data.worlds.new('w')
sc.world = w
w.use_nodes = True
nt = w.node_tree
bg = nt.nodes['Background']
grad = nt.nodes.new('ShaderNodeTexGradient')
tc = nt.nodes.new('ShaderNodeTexCoord')
mp = nt.nodes.new('ShaderNodeMapping')
ramp = nt.nodes.new('ShaderNodeValToRGB')
mp.inputs['Rotation'].default_value = (0, -math.pi / 2, 0)
nt.links.new(tc.outputs['Generated'], mp.inputs['Vector'])
nt.links.new(mp.outputs['Vector'], grad.inputs['Vector'])
nt.links.new(grad.outputs['Fac'], ramp.inputs['Fac'])
nt.links.new(ramp.outputs['Color'], bg.inputs['Color'])
ramp.color_ramp.elements[0].color = (0.035, 0.02, 0.012, 1)
ramp.color_ramp.elements[1].color = (0.5, 0.36, 0.24, 1)
bg.inputs['Strength'].default_value = 0.8

bpy.ops.mesh.primitive_uv_sphere_add(segments=128, ring_count=64, radius=1)
ball = bpy.context.active_object
bpy.ops.object.shade_smooth()
cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam'))
sc.collection.objects.link(cam)
sc.camera = cam
cam.data.type = 'ORTHO'
cam.data.ortho_scale = 2.0
cam.location = (0, -5, 0)
cam.rotation_euler = (math.pi / 2, 0, 0)


def light(name, loc, sx, sy, energy, color, shape='RECTANGLE'):
    l = bpy.data.lights.new(name, 'AREA')
    l.shape = shape
    l.size = sx
    l.size_y = sy
    l.energy = energy
    l.color = color
    o = bpy.data.objects.new(name, l)
    sc.collection.objects.link(o)
    o.location = loc
    o.rotation_euler = (-Vector(loc)).to_track_quat('-Z', 'Y').to_euler()


light('key', (-2.2, -2.0, 3.0), 3.2, 2.2, 380, (1.0, 0.92, 0.78))       # 左上方大柔光箱：亮面占球的左上一大片
light('top', (0.6, 0.4, 4.0), 4.0, 2.0, 150, (1.0, 0.94, 0.85))        # 头顶大面光：上半球整体亮
light('win', (2.6, -1.8, 1.8), 0.5, 2.2, 240, (0.96, 0.93, 0.9))       # 右侧一道竖窗：清楚的锐利高光
light('front', (-0.4, -4.5, 0.2), 3.2, 1.6, 80, (1.0, 0.85, 0.62))    # 正前方补光：正对玩家的金面不发黑
light('rim', (2.4, 2.4, -0.2), 0.5, 3.0, 220, (1.0, 0.76, 0.46))       # 右后暖色轮廓
light('table', (-0.8, -1.4, -3.2), 4.0, 1.4, 120, (0.9, 0.5, 0.25))    # 下方桌面暖色反光（偏左，不对称）
m = bpy.data.materials.new('gold2')
m.use_nodes = True
p = m.node_tree.nodes['Principled BSDF']
p.inputs['Base Color'].default_value = (1.0, 0.70, 0.30, 1)
p.inputs['Metallic'].default_value = 1.0
p.inputs['Roughness'].default_value = 0.3
ball.data.materials.append(m)
sc.render.filepath = os.path.join(out, 'matcap_gold2.png')
bpy.ops.render.render(write_still=True)
print('matcap gold2')

# 第十五轮（用户：音乐盒不要太闪）：缎面金 matcap_goldsoft——粗糙度高、灯箱更大更柔，高光是一片柔光而不是一个个锐利的亮块；
# 头显里细金线、珍珠上的小亮点一动就跳（闪），缎面金不跳
for o in list(sc.objects):
    if o.type == 'LIGHT':
        bpy.data.objects.remove(o)
light('key', (-2.2, -2.0, 3.0), 4.5, 3.5, 300, (1.0, 0.92, 0.8))
light('top', (0.6, 0.4, 4.0), 5.0, 3.0, 120, (1.0, 0.95, 0.88))
light('front', (-0.4, -4.5, 0.2), 4.0, 2.4, 90, (1.0, 0.86, 0.66))
light('rim', (2.4, 2.4, -0.2), 1.2, 3.0, 120, (1.0, 0.8, 0.55))
light('table', (-0.8, -1.4, -3.2), 4.5, 2.0, 90, (0.9, 0.55, 0.3))
p.inputs['Roughness'].default_value = 0.48
p.inputs['Base Color'].default_value = (0.95, 0.72, 0.38, 1)
sc.render.filepath = os.path.join(out, 'matcap_goldsoft.png')
bpy.ops.render.render(write_still=True)
print('matcap goldsoft')
