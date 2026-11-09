"""渲染材质球（matcap）：金、清漆高光、钢。Blender -b -P Tools/blender_matcap.py -- <输出目录>
正交相机正对一个球，暖色大柔光箱在左上、冷色轮廓光在右后、底下一点暖色反光；Cycles 渲染 512×512。
金 / 钢：整球颜色（着色器里直接当颜色用）；清漆：黑底上只有高光（着色器里叠加到木头上）。"""
import bpy, sys, os, math
out = sys.argv[sys.argv.index('--') + 1]
os.makedirs(out, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
sc = bpy.context.scene
sc.render.engine = 'CYCLES'; sc.cycles.samples = 128; sc.cycles.use_denoising = True
sc.render.resolution_x = sc.render.resolution_y = 512
sc.render.film_transparent = False
sc.view_settings.view_transform = 'Standard'
w = bpy.data.worlds.new('w'); sc.world = w; w.use_nodes = True
bg = w.node_tree.nodes['Background']
# 环境：上暖下暗的渐变（像傍晚的小房间）
nt = w.node_tree
grad = nt.nodes.new('ShaderNodeTexGradient'); tc = nt.nodes.new('ShaderNodeTexCoord'); mp = nt.nodes.new('ShaderNodeMapping')
ramp = nt.nodes.new('ShaderNodeValToRGB')
mp.inputs['Rotation'].default_value = (0, -math.pi / 2, 0)
nt.links.new(tc.outputs['Generated'], mp.inputs['Vector']); nt.links.new(mp.outputs['Vector'], grad.inputs['Vector'])
nt.links.new(grad.outputs['Fac'], ramp.inputs['Fac']); nt.links.new(ramp.outputs['Color'], bg.inputs['Color'])
ramp.color_ramp.elements[0].color = (0.05, 0.03, 0.025, 1); ramp.color_ramp.elements[1].color = (0.55, 0.42, 0.32, 1)
bg.inputs['Strength'].default_value = 0.6

bpy.ops.mesh.primitive_uv_sphere_add(segments=96, ring_count=48, radius=1)
ball = bpy.context.active_object; bpy.ops.object.shade_smooth()
cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam')); sc.collection.objects.link(cam); sc.camera = cam
cam.data.type = 'ORTHO'; cam.data.ortho_scale = 2.0; cam.location = (0, -5, 0); cam.rotation_euler = (math.pi / 2, 0, 0)

def area(name, loc, size, energy, color):
    l = bpy.data.lights.new(name, 'AREA'); l.size = size; l.energy = energy; l.color = color
    o = bpy.data.objects.new(name, l); sc.collection.objects.link(o); o.location = loc
    d = -__import__('mathutils').Vector(loc); o.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler()
area('key', (-2.6, -2.4, 2.8), 2.4, 260, (1.0, 0.86, 0.66))     # 左上前方暖色柔光箱
area('rim', (2.8, 1.8, 1.4), 1.2, 160, (0.75, 0.82, 1.0))       # 右后冷色轮廓
area('fill', (2.2, -2.6, -0.8), 3.0, 40, (1.0, 0.7, 0.5))       # 右下暖色补光
area('bounce', (0, -1.0, -3.0), 3.0, 50, (1.0, 0.62, 0.4))      # 底下桌面反光
area('front', (0.4, -4.5, 0.9), 4.5, 170, (1.0, 0.9, 0.74))     # 正前方大柔光（正对玩家的金面不发黑：铭牌、按钮圈）

def shade(name, base, metal, rough, coat=0.0, spec_only=False):
    m = bpy.data.materials.new(name); m.use_nodes = True
    p = m.node_tree.nodes['Principled BSDF']
    p.inputs['Base Color'].default_value = (*base, 1); p.inputs['Metallic'].default_value = metal; p.inputs['Roughness'].default_value = rough
    if 'Coat Weight' in p.inputs: p.inputs['Coat Weight'].default_value = coat; p.inputs['Coat Roughness'].default_value = 0.3
    ball.data.materials.clear(); ball.data.materials.append(m)
    sc.render.filepath = os.path.join(out, f'matcap_{name}.png'); bpy.ops.render.render(write_still=True)
    print('matcap', name)

shade('gold', (1.0, 0.72, 0.36), 1.0, 0.34)
shade('goldsatin', (0.86, 0.66, 0.38), 1.0, 0.55)              # 缎面香槟金（拉丝感、高光宽而淡，不刺眼）：包边、嵌线、包角用
shade('steel', (0.82, 0.84, 0.88), 1.0, 0.3)
shade('varnish', (0.0, 0.0, 0.0), 0.0, 0.35, coat=0.6)        # 黑底：只剩清漆高光（缎面：高光宽而柔，不油）

# 钢琴漆（参考图：深玫瑰木 + 镜面漆，边上一条条细而清晰的反光，不是一大片油光）：
# 关掉大柔光箱，换成几根细长的条形灯（像窗框 / 灯管），漆面很光滑 → 球上只留几道锐利的细高光
for o in list(sc.objects):
    if o.type == 'LIGHT': bpy.data.objects.remove(o)
def strip(name, loc, sx, sy, energy, color):
    l = bpy.data.lights.new(name, 'AREA'); l.shape = 'RECTANGLE'; l.size = sx; l.size_y = sy; l.energy = energy; l.color = color
    o = bpy.data.objects.new(name, l); sc.collection.objects.link(o); o.location = loc
    d = -__import__('mathutils').Vector(loc); o.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler()
strip('s1', (-2.4, -2.6, 2.6), 3.2, 0.18, 900, (1.0, 0.9, 0.75))     # 左上一道长条
strip('s2', (-2.0, -2.9, 2.0), 3.0, 0.08, 500, (1.0, 0.92, 0.8))     # 紧挨着一道细的（双线，像窗框）
strip('s3', (2.9, 1.5, 1.0), 0.12, 2.6, 380, (0.8, 0.86, 1.0))       # 右后冷色竖条（轮廓）
strip('s4', (0.0, -1.5, -3.0), 3.0, 0.25, 160, (1.0, 0.66, 0.42))    # 底下一道暖色（桌面反光）
bg.inputs['Strength'].default_value = 0.0      # 环境光关掉：漆面只反射那几根灯条
m = bpy.data.materials.new('lacquer'); m.use_nodes = True
p = m.node_tree.nodes['Principled BSDF']
p.inputs['Base Color'].default_value = (0, 0, 0, 1); p.inputs['Metallic'].default_value = 0; p.inputs['Roughness'].default_value = 0.5
p.inputs['Coat Weight'].default_value = 1.0; p.inputs['Coat Roughness'].default_value = 0.04
if 'Specular IOR Level' in p.inputs: p.inputs['Specular IOR Level'].default_value = 0.0   # 底层不反光：只留漆面那几道细高光（不然整球一层灰雾，叠到木头上发灰）
ball.data.materials.clear(); ball.data.materials.append(m)
sc.render.filepath = os.path.join(out, 'matcap_lacquer.png'); bpy.ops.render.render(write_still=True)
print('matcap', 'lacquer')
