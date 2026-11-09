"""《黄昏琴村》TwilightKeys · Tripo P2 模型管线（由 pico2 tr_p2_blender_20261026.py 改写；在 Blender 里运行）：
  参考图（nano banana，放在 ArtSource/Refs/<名称>/image.png）→ 四视图 → P2 白模（texture=false；面数 ≤2.5 万时出四边面）
  → 白模审计（Tools/mesh_audit_p2.py：碎片 / 悬浮块 / 开放边与洞 / 非流形 / 自穿插 / 薄片 + 素模六视图）
  → 【闸门】人工看过素模图后 approve（不合格 reject 重做）才进入贴图
  → 白模贴图（/models/texture，四视图作参考，detailed）→ 下载 → Blender 清理（去碎片、补洞、修贴图条纹）→ 导出 lod0..3.fbx + albedo.png（≤2048）
资产表在 ArtSource/specs.json：{"名称": {"faces": 面数上限}}。进度在 ArtSource/ModelsP2/<名称>/state.json，可随时中断续跑。
用法：
  Blender -b -P Tools/p2_pipeline.py -- run [名称...]       推进到“等待审批”或完成（会等待）
  ... -- step [名称...]      只推进一轮
  ... -- approve 名称...      审批通过
  ... -- reject 名称... [--keep-mv]   白模不合格：重做（--keep-mv 只换种子重做白模）
  ... -- retex 名称...        只重做贴图
  ... -- process [名称...]    只做 Blender 清理与导出"""
import json, pathlib, subprocess, sys, time, urllib.request

sys.path.insert(0, '/Users/ld/.codex/skills/tripo-image/scripts')
import tripo_image as api

PROJ = pathlib.Path('/Users/ld/Documents/ChatGPT/TwilightKeys')
ROOT = PROJ / 'ArtSource'
OUT = ROOT / 'ModelsP2'
BLENDER = '/Applications/Blender.app/Contents/MacOS/Blender'
# 资产表：ArtSource/specs.json  {"名称": {"faces": 面数上限}}（P2 对过高的上限偶尔报 1004，≤4.5 万稳妥）
_SPEC = json.loads((ROOT / 'specs.json').read_text()) if (ROOT / 'specs.json').exists() else {}
SPECS = {k: v['faces'] for k, v in _SPEC.items()}
LODS = [('lod0.fbx', 1.0), ('lod1.fbx', .35), ('lod2.fbx', .1), ('lod3.fbx', .03)]
EXTREME = set()            # 用户要求贴图最高 2K：全部 detailed，导出时再限制到 2048
MAX_TEX = 2048
ROCKY = set()
DONE = ('success',)
BAD = ('failed', 'cancelled', 'banned', 'expired')


def ref_of(name):
    p = ROOT / 'Refs' / name / 'image.png'
    return p if p.exists() else None


class Busy(Exception):
    pass


def post(path, body):
    try:
        return api.request_json('POST', path, body)['data']['task_id']
    except Exception as e:
        msg = str(e)
        if '429' in msg or 'limit' in msg.lower():
            raise Busy()
        raise


def fetch(url, dst):
    for k in range(6):
        try:
            urllib.request.urlretrieve(url, dst); return True
        except Exception as e:
            print('download retry', dst.name, str(e)[:80], flush=True); time.sleep(6)
    return False


def fbx_to_glb(path):
    """P2 开 quad 后返回的是 FBX：保留原始四边面文件（*.quad.fbx），再转成 GLB 给审计与清理用"""
    path = pathlib.Path(path)
    if not path.exists() or path.read_bytes()[:7] != b'Kaydara':
        return False
    import bpy
    q = path.with_suffix('.quad.fbx')
    path.replace(q)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(q))
    bpy.ops.export_scene.gltf(filepath=str(path), export_format='GLB')
    print(path.name, 'FBX→GLB', flush=True)
    return True


def audit(name):
    folder = OUT / name
    r = subprocess.run([BLENDER, '-b', '--python', str(PROJ / 'Tools/mesh_audit_p2.py'), '--', str(folder / 'white.glb'), str(folder / 'audit'), name],
                       capture_output=True, text=True, timeout=1800)
    p = folder / 'audit' / f'{name}_audit.json'
    if not p.exists():
        print(name, 'AUDIT FAILED', r.stdout[-600:], r.stderr[-600:], flush=True); return None
    return json.loads(p.read_text())


def advance(name):
    folder = OUT / name; folder.mkdir(parents=True, exist_ok=True)
    sf = folder / 'state.json'
    st = json.loads(sf.read_text()) if sf.exists() else {}
    save = lambda: sf.write_text(json.dumps(st, indent=2, ensure_ascii=False))

    def poll(key):
        d = api.task(st[key])
        status = d.get('status')
        if status in DONE:
            st[key + '_result'] = d; save()
        elif status in BAD:
            st[key + '_error'] = d; save()
        return status

    if (folder / 'model.glb').exists():
        return 'downloaded'
    if 'mv' not in st:
        ref = ref_of(name)
        if not ref:
            return 'no-ref'
        st['ref'] = str(ref.relative_to(ROOT))
        if 'ref_token' not in st:
            st['ref_token'] = api.upload_file(ref); save()
        st['mv'] = post('/generation/image-to-multiview', {'input': st['ref_token']}); save()
        return 'mv-submitted'
    if 'mv_result' not in st:
        s = poll('mv')
        if s in BAD:
            del st['mv']; save(); return 'mv-failed(retry)'
        if s not in DONE:
            return 'mv-' + str(s)
        out = st['mv_result']['output']
        for k in ('front', 'left', 'back', 'right'):
            u = out.get(k + '_view_url')
            if u: fetch(u, folder / f'view_{k}.png')
        return 'mv-done'
    if 'white' not in st:
        body = {'inputs': [{'task_id': st['mv']}], 'model': 'P2-20260801', 'face_limit': SPECS[name], 'texture': False, 'pbr': False,
                'model_seed': 2026103100 + list(SPECS).index(name) + 1000 * st.get('retries', 0)}
        if SPECS[name] <= 25000 and not _SPEC.get(name, {}).get('tri'):
            body['quad'] = True   # 四边面：网格更规整、更容易封闭
        st['white_request'] = body
        st['white'] = post('/generation/multiview-to-model', body); save()
        return 'white-submitted'
    if 'white_result' not in st:
        s = poll('white')
        if s in BAD:
            del st['white']; save(); return 'white-failed(retry)'
        if s not in DONE:
            return 'white-' + str(s)
        out = st['white_result']['output']
        u = out.get('model_url') or out.get('model') or out.get('pbr_model_url')
        if not (u and fetch(u, folder / 'white.glb')):
            del st['white_result']; save(); return 'white-download-failed'
        return 'white-done'
    # 白模审计（每个白模只跑一次）
    if 'audit' not in st:
        if not (folder / 'white.glb').exists():
            u = st['white_result']['output'].get('model_url'); fetch(u, folder / 'white.glb')
        fbx_to_glb(folder / 'white.glb')
        a = audit(name)
        if a is None:
            return 'audit-error'
        st['audit'] = a; save()
        return 'audited ' + summary(a)
    # 闸门：人工核对素模图后审批
    if (folder / 'rejected').exists():
        return 'rejected (fix ref then: -- reject)'
    if not (folder / 'approved').exists():
        return 'await-approval ' + summary(st['audit'])
    if 'tex' not in st:
        out = st['mv_result']['output']
        images = [{k: out[k + '_view_url']} for k in ('front', 'left', 'back', 'right') if out.get(k + '_view_url')]
        body = {'input': st['white'], 'model': 'v3.0-20250812', 'texture_prompt': {'images': images},
                'texture_quality': 'extreme' if name in EXTREME else 'detailed', 'pbr': False, 'texture_alignment': st.get('tex_align', 'original_image'), 'texture_seed': st.get('tex_seed', 77)}
        st['tex_request'] = {k: v for k, v in body.items() if k != 'texture_prompt'}
        st['tex'] = post('/models/texture', body); save()
        return 'tex-submitted'
    if 'tex_result' not in st:
        s = poll('tex')
        if s in BAD:
            del st['tex']; save(); return 'tex-failed(retry)'
        if s not in DONE:
            return 'tex-' + str(s)
    out = st['tex_result']['output']
    u = out.get('model_url') or out.get('pbr_model_url') or out.get('model')
    if not fetch(u, folder / 'model.glb'):
        return 'tex-download-failed'
    fbx_to_glb(folder / 'model.glb')
    if isinstance(out.get('rendered_image_url'), str):
        fetch(out['rendered_image_url'], folder / 'rendered.webp')
    return 'downloaded'


def summary(a):
    return f"faces={a['faces']} islands={a['islands']} debris={a['debris']} floating={a['floating_islands']} holes={a['holes']} bigHoles={a.get('big_holes')} boundary={a['boundary_edges']} nonman={a['nonmanifold_edges']} selfX={a['self_intersections']}"


def reject(name):
    folder = OUT / name; sf = folder / 'state.json'
    st = json.loads(sf.read_text()) if sf.exists() else {}
    keep = {'retries': st.get('retries', 0) + 1, 'history': st.get('history', []) + [{k: st.get(k) for k in ('mv', 'white', 'audit')}]}
    if '--keep-mv' in sys.argv and 'mv_result' in st:   # 四视图没问题时只重做白模（新种子）
        for k in ('ref', 'ref_token', 'mv', 'mv_result'): keep[k] = st[k]
    sf.write_text(json.dumps(keep, indent=2, ensure_ascii=False))
    for f in ('white.glb', 'approved', 'rejected') + (() if '--keep-mv' in sys.argv else ('view_front.png', 'view_left.png', 'view_back.png', 'view_right.png')):
        (folder / f).unlink(missing_ok=True)
    print(name, 'reset for regeneration, retries', keep['retries'], flush=True)


def process(name):
    import bpy, bmesh
    folder = OUT / name
    src = folder / 'model.glb'
    if not src.exists():
        print(name, 'no model.glb', flush=True); return
    report = {}
    for out_name, ratio in LODS:
        bpy.ops.wm.read_factory_settings(use_empty=True)
        bpy.ops.import_scene.gltf(filepath=str(src))
        meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
        bpy.ops.object.select_all(action='DESELECT')
        for o in meshes:
            o.select_set(True)
        bpy.context.view_layer.objects.active = meshes[0]
        if len(meshes) > 1:
            bpy.ops.object.join()
        obj = bpy.context.view_layer.objects.active
        bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
        bm = bmesh.new(); bm.from_mesh(obj.data)
        size = max(obj.dimensions)
        bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=size * 5e-5)
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        bm.faces.ensure_lookup_table()
        seen, groups = set(), []
        for f in bm.faces:
            if f.index in seen: continue
            stack, group = [f], []
            seen.add(f.index)
            while stack:
                g = stack.pop(); group.append(g)
                for e in g.edges:
                    for nf in e.link_faces:
                        if nf.index not in seen:
                            seen.add(nf.index); stack.append(nf)
            groups.append(group)
        small = [f for g in groups if len(g) < 6 for f in g]
        if name in ROCKY:
            import numpy as np
            groups.sort(key=len, reverse=True)
            for g in groups[1:]:
                if len(g) < 6 or len(g) > len(bm.faces) * .15: continue
                pts = np.array([tuple(v.co) for f in g for v in f.verts]); pts -= pts.mean(0)
                ev = np.sqrt(np.maximum(np.linalg.eigvalsh(np.cov(pts.T)), 0))
                if ev[-1] > size * .02 and ev[0] < .06 * ev[-1]:
                    small.extend(g); print(name, 'remove plate', len(g), 'faces', flush=True)
        # 悬浮块：包围盒（放大 1%）不碰任何其他块、且面数 < 5% 的块（脱离主体的碎片）
        import numpy as np
        big = [g for g in groups if len(g) >= 6]
        boxes = []
        for g in big:
            vs = np.array([tuple(v.co) for f in g for v in f.verts]); boxes.append((vs.min(0), vs.max(0)))
        padm = size * .01
        for k, g in enumerate(big):
            if len(g) > len(bm.faces) * .05: continue
            lk, hk = boxes[k]
            if not any(m != k and np.all(lk - padm <= boxes[m][1]) and np.all(hk + padm >= boxes[m][0]) for m in range(len(big))):
                small.extend(g); print(name, 'remove floating', len(g), 'faces', flush=True)
        removed = len(small)
        if small:
            bmesh.ops.delete(bm, geom=list(set(small)), context='FACES')
        # 补洞：白模审计里所有开放边环（叶团底部的空壳口、鼓包片的边、树根底面）都封成面。
        # 补面的 UV 统一取该洞边界上颜色最接近中位色的那个顶点的 UV —— 补面是一块与周围同色的实底，不会把贴图图集拉成色块
        uv = bm.loops.layers.uv.active
        img = None
        for mat in obj.data.materials:
            if mat and mat.use_nodes:
                for node in mat.node_tree.nodes:
                    if node.type == 'TEX_IMAGE' and node.image: img = node.image; break
        tex = np.array(img.pixels[:], dtype=np.float32).reshape(img.size[1], img.size[0], 4) if img else None
        def color_at(u):
            if tex is None: return np.zeros(3)
            h, w = tex.shape[:2]; return tex[min(h - 1, max(0, int((u[1] % 1) * h))), min(w - 1, max(0, int((u[0] % 1) * w))), :3]
        before = set(bm.faces)
        orig_faces = set(bm.faces)   # 补洞前的原始面：它们的 UV 是对的
        bnd = [e for e in bm.edges if e.is_boundary]
        filled = 0
        if bnd:
            # 先记下每个边界顶点原来的 UV
            vuv = {}
            if uv:
                for e in bnd:
                    for v in e.verts:
                        if v not in vuv and v.link_loops: vuv[v] = tuple(v.link_loops[0][uv].uv)
            res = bmesh.ops.holes_fill(bm, edges=bnd, sides=0)
            newf = [f for f in res['faces']]
            bmesh.ops.triangulate(bm, faces=newf, quad_method='BEAUTY', ngon_method='BEAUTY')
            newf = [f for f in bm.faces if f not in before]
            # 按连通的补面分组（每个洞一组），每组取一个代表 UV
            seenf = set()
            for f in newf:
                if f in seenf: continue
                grp, st = [], [f]; seenf.add(f)
                while st:
                    g = st.pop(); grp.append(g)
                    for e in g.edges:
                        for h in e.link_faces:
                            if h not in before and h not in seenf: seenf.add(h); st.append(h)
                filled += 1
                if not uv: continue
                cands = [vuv[v] for g in grp for v in g.verts if v in vuv]
                if not cands: continue
                cols = np.array([color_at(c) for c in cands]); med = np.median(cols, 0)
                best = cands[int(np.argmin(((cols - med) ** 2).sum(1)))]
                for g in grp:
                    for l in g.loops: l[uv].uv = best
            # 捏合点（一个顶点挂着 ≠2 条开放边，“8”字形洞）：删掉它周围一圈面，把洞扩成简单环，再整体补一次
            for _pass in range(4):
                pinch = [v for v in bm.verts if sum(1 for e in v.link_edges if e.is_boundary) not in (0, 2)]
                if not pinch: break
                kill = {f for v in pinch for f in v.link_faces}
                bmesh.ops.delete(bm, geom=list(kill), context='FACES')
                loose = [v for v in bm.verts if not v.link_faces]
                if loose: bmesh.ops.delete(bm, geom=loose, context='VERTS')
                bnd2 = [e for e in bm.edges if e.is_boundary]
                before2 = set(bm.faces)
                bmesh.ops.holes_fill(bm, edges=bnd2, sides=0)
                nf2 = [f for f in bm.faces if f not in before2]
                if nf2: bmesh.ops.triangulate(bm, faces=nf2, quad_method='BEAUTY', ngon_method='BEAUTY')
                if uv:
                    for f in nf2:
                        if not f.is_valid: continue
                        for l in f.loops:
                            nb = [vuv[x] for x in [l.vert] if x in vuv]
                            if nb: l[uv].uv = nb[0]
                filled += len(nf2)
            # 兜底：holes_fill 填不上的环（经过非流形顶点、自相接触），沿开放边逐环行走，中心加一点做扇形补面
            for _pass in range(3):
                rest = [e for e in bm.edges if e.is_boundary]
                if not rest: break
                used = set()
                for e0 in rest:
                    if e0 in used: continue
                    ring = [e0.verts[0]]; cur = e0.verts[1]; used.add(e0); prev_e = e0
                    ok = True
                    for _ in range(20000):
                        ring.append(cur)
                        nxt = [e for e in cur.link_edges if e.is_boundary and e not in used]
                        if not nxt: break
                        e = nxt[0]; used.add(e); cur = e.other_vert(cur)
                        if cur is ring[0]: break
                    if cur is not ring[0] or len(ring) < 3: continue
                    cvec = sum((v.co for v in ring), ring[0].co * 0) / len(ring)
                    cv = bm.verts.new(cvec)
                    luv = vuv.get(ring[0]) if uv else None
                    for k in range(len(ring)):
                        a_, b_ = ring[k], ring[(k + 1) % len(ring)]
                        if a_ is b_: continue
                        try:
                            f = bm.faces.new((b_, a_, cv))
                        except ValueError:
                            continue
                        if uv:
                            u0 = vuv.get(a_) or luv or (0.5, 0.5)
                            for l in f.loops: l[uv].uv = u0
                    filled += 1
            # 细缝：只焊开放边上的顶点（容差 0.2% 尺寸，内部顶点不动），焊完再补一次
            bv = list({v for e in bm.edges if e.is_boundary for v in e.verts})
            if bv:
                bmesh.ops.remove_doubles(bm, verts=bv, dist=size * 2e-3)
                bnd3 = [e for e in bm.edges if e.is_boundary]
                before3 = set(bm.faces)
                if bnd3: bmesh.ops.holes_fill(bm, edges=bnd3, sides=0)
                nf3 = [f for f in bm.faces if f not in before3]
                if nf3: bmesh.ops.triangulate(bm, faces=nf3, quad_method='BEAUTY', ngon_method='BEAUTY')
                if uv:
                    for f in [f for f in bm.faces if f not in before3]:
                        for l in f.loops:
                            if l.vert in vuv: l[uv].uv = vuv[l.vert]
                filled += len(nf3)
            # 退化三角形（三点共线的补面）：导出器会丢掉它们、留下零宽细缝 → 先消解（合并共线边），再补一次，循环两轮
            for _pass in range(3):
                bmesh.ops.dissolve_degenerate(bm, edges=bm.edges[:], dist=size * 2e-4)
                bnd4 = [e for e in bm.edges if e.is_boundary]
                if bnd4:
                    before4 = set(bm.faces)
                    bmesh.ops.holes_fill(bm, edges=bnd4, sides=0)
                    nf4 = [f for f in bm.faces if f not in before4]
                    if nf4: bmesh.ops.triangulate(bm, faces=nf4, quad_method='BEAUTY', ngon_method='BEAUTY')
                    if uv:
                        for f in [f for f in bm.faces if f not in before4]:
                            for l in f.loops:
                                if l.vert in vuv: l[uv].uv = vuv[l.vert]
                bmesh.ops.triangulate(bm, faces=bm.faces[:])
                thin = [f for f in bm.faces if f.calc_area() < (size * 2e-4) ** 2]
                if not thin and not [e for e in bm.edges if e.is_boundary]: break
            # 最后一道：仍剩的开放边（贴地边缘的锯齿缝、帽沿方孔）——开放边顶点按 0.6% 尺寸焊接，再补一次
            for _pass in range(2):
                bv2 = list({v for e in bm.edges if e.is_boundary for v in e.verts})
                if not bv2: break
                bmesh.ops.remove_doubles(bm, verts=bv2, dist=size * 6e-3)
                bnd5 = [e for e in bm.edges if e.is_boundary]
                if bnd5:
                    before5 = set(bm.faces)
                    bmesh.ops.holes_fill(bm, edges=bnd5, sides=0)
                    nf5 = [f for f in bm.faces if f not in before5]
                    if nf5: bmesh.ops.triangulate(bm, faces=nf5, quad_method='BEAUTY', ngon_method='BEAUTY')
                    if uv:
                        for f in nf5:
                            for l in f.loops:
                                if l.vert in vuv: l[uv].uv = vuv[l.vert]
            # 缝合 T 型裂缝：剩下的开放边多是“开放的链”（两端各一个只挂一条开放边的端点）——两片表面沿一条线相邻但不共享顶点。
            # 做法：每个开放边顶点，若离某条“不含它的开放边”的距离 < 0.4% 尺寸，就在该边上按投影位置拆出一个新顶点并焊到它身上；
            # 两侧顶点一一对应后，按距离焊接即闭合
            from mathutils.kdtree import KDTree
            from mathutils.geometry import intersect_point_line
            for _pass in range(3):
                bedges = [e for e in bm.edges if e.is_boundary]
                if not bedges: break
                mids = KDTree(len(bedges))
                for k, e in enumerate(bedges): mids.insert((e.verts[0].co + e.verts[1].co) * .5, k)
                mids.balance()
                tol = size * 4e-3; splits = []
                bverts = list({v for e in bedges for v in e.verts})
                claimed = set()
                for v in bverts:
                    best = None
                    for co, k, dd in mids.find_range(v.co, tol + max(e.calc_length() for e in v.link_edges) if v.link_edges else tol):
                        e = bedges[k]
                        if v in e.verts or k in claimed: continue
                        pt, fac = intersect_point_line(v.co, e.verts[0].co, e.verts[1].co)
                        if not (0.02 < fac < 0.98): continue
                        d = (pt - v.co).length
                        if d < tol and (best is None or d < best[0]): best = (d, k, fac)
                    if best: splits.append((v, best[1], best[2])); claimed.add(best[1])
                if not splits: break
                weld = {}
                for v, k, fac in splits:
                    e = bedges[k]
                    if not e.is_valid: continue
                    ne, nv = bmesh.utils.edge_split(e, e.verts[0], fac)
                    weld[nv] = v
                if weld: bmesh.ops.weld_verts(bm, targetmap=weld)
                bmesh.ops.remove_doubles(bm, verts=list({x for e in bm.edges if e.is_boundary for x in e.verts}), dist=size * 1e-3)
                print(name, 'stitch pass', _pass, 'splits', len(weld), 'boundary left', sum(1 for e in bm.edges if e.is_boundary), flush=True)
            # 桥接窄缝：剩下的开放边多是窄缝两侧的两条“开放链”（相距约 0.5–1% 尺寸）。
            # 对每个开放边顶点找“不在同一条链上、距离 < 1.5% 尺寸”的最近开放边顶点，两两配对后，
            # 对每条开放边 (a,b)：若 a、b 的配对点 a'、b' 相邻（同一条开放边）→ 补两个三角形 (a,b,b')(a,b',a')；若 a'==b' → 补一个三角形 (a,b,a')
            from mathutils.kdtree import KDTree
            for _pass in range(4):
                bedges = [e for e in bm.edges if e.is_boundary]
                if not bedges: break
                bverts = list({v for e in bedges for v in e.verts})
                # 链编号：开放边连通分量
                comp = {}; cid = 0
                for v in bverts:
                    if v in comp: continue
                    st = [v]; comp[v] = cid
                    while st:
                        x = st.pop()
                        for e in x.link_edges:
                            if e.is_boundary:
                                y = e.other_vert(x)
                                if y not in comp: comp[y] = cid; st.append(y)
                    cid += 1
                kd = KDTree(len(bverts))
                for k, v in enumerate(bverts): kd.insert(v.co, k)
                kd.balance()
                mate = {}
                for v in bverts:
                    for co, k, dd in kd.find_n(v.co, 12):
                        w = bverts[k]
                        if w is v or dd > size * .015: continue
                        if comp[w] == comp[v] and any(w in e.verts for e in v.link_edges): continue
                        mate[v] = w; break
                made = 0
                for e in bedges:
                    if not e.is_valid or not e.is_boundary: continue
                    a, b = e.verts
                    a2, b2 = mate.get(a), mate.get(b)
                    if a2 is None or b2 is None: continue
                    tris = []
                    if a2 is b2: tris = [(a, b, a2)]
                    elif any(b2 in x.verts for x in a2.link_edges if x.is_boundary): tris = [(a, b, b2), (a, b2, a2)]
                    for t in tris:
                        if len(set(t)) < 3: continue
                        try: bm.faces.new(t); made += 1
                        except ValueError: pass
                print(name, 'bridge pass', _pass, 'faces', made, 'boundary left', sum(1 for e in bm.edges if e.is_boundary), flush=True)
                if not made: break
            # 终极封盖：剩下的开放边按“环”行走，遇到走过的顶点就切出一个简单子环（拆开 8 字形 / 多重经过的环），
            # 每个简单子环用环上顶点做扇形三角化——不新建顶点，UV 由下面的统一赋值步骤从原始面取
            for _pass in range(4):
                left = [e for e in bm.edges if e.is_boundary]
                if not left: break
                used_e = set(); made = 0
                for e0 in left:
                    if e0 in used_e or not e0.is_valid: continue
                    path = [e0.verts[0]]; pos = {e0.verts[0]: 0}; cur = e0.verts[1]; used_e.add(e0)
                    for _ in range(50000):
                        if cur in pos:
                            loop = path[pos[cur]:]
                            if len(loop) >= 3:
                                for k in range(1, len(loop) - 1):
                                    try: bm.faces.new((loop[0], loop[k + 1], loop[k])); made += 1
                                    except ValueError: pass
                            for x in loop[1:]: pos.pop(x, None)
                            path = path[:pos[cur] + 1]
                        else:
                            pos[cur] = len(path); path.append(cur)
                        nxt = [e for e in cur.link_edges if e.is_boundary and e not in used_e]
                        if not nxt: break
                        e = nxt[0]; used_e.add(e); cur = e.other_vert(cur)
                if not made: break
            bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        # 统一给所有新补的面赋 UV：每个角点取该顶点所在原始面的 UV；顶点不在任何原始面上（扇形中心、捏合点删面后裸露的顶点）
        # 时沿网格广度优先找最近的、有原始 UV 的顶点。之前只按“首次记录的边界顶点”赋值，后几轮新暴露的顶点落到 (0,0)，
        # 贴图图集的空白角是白色 → 场景里看到的“白膜”补丁
        zero_uv = 0
        if uv:
            orig_alive = {f for f in orig_faces if f.is_valid}
            cache = {}
            def uv_of(v):
                if v in cache: return cache[v]
                from collections import deque
                q = deque([v]); seen_v = {v}
                while q:
                    x = q.popleft()
                    for l in x.link_loops:
                        if l.face in orig_alive:
                            cache[v] = tuple(l[uv].uv); return cache[v]
                    for e in x.link_edges:
                        y = e.other_vert(x)
                        if y not in seen_v and len(seen_v) < 400: seen_v.add(y); q.append(y)
                cache[v] = None; return None
            for f in bm.faces:
                if f in orig_alive: continue
                for l in f.loops:
                    u = uv_of(l.vert)
                    if u is None: zero_uv += 1
                    else: l[uv].uv = u
        report['fillUvMissing'] = zero_uv
        # 第三轮（2026-10-22）：四个视角都看不到、或 UV 异常（跨图集的大三角 / 细长条 / 补洞面）的面改用顶点色（周围可靠面的贴图色），
        # 消除叶面上的乱条纹；着色器 RfModel 按顶点色 alpha 混合
        sys.path.insert(0, str(PROJ / 'Tools'))
        import p2_vcolor_fix as vcfix   # 第二轮：补洞片整块同色（不拉红白斜纹）
        vst = vcfix.recolor(bm, obj, size)
        if out_name == 'lod0.fbx': report['vcolorFix'] = vst
        print(name, 'vcolor fix', vst, flush=True)
        keys = {}
        dup = []
        for f in bm.faces:
            k = tuple(sorted(v.index for v in f.verts))
            if k in keys: dup.append(f)
            else: keys[k] = f
        # 重复面 = 补上的“单层鳍片”的背面（背靠背的同顶点三角形，零厚度双面片）。Unity 的 FBX 导入保留它们，网格是封闭的；
        # 只有 Blender 重新导入 glTF 时会按顶点集合去重、在复审里显示成开放边，所以复审的开放边数要减去这部分
        print(name, 'back-to-back fin faces', len(dup), 'degenerate', sum(1 for f in bm.faces if f.calc_area() < (size * 1e-5) ** 2), flush=True)
        bm.verts.ensure_lookup_table()
        closed = {'boundaryEdges': sum(1 for e in bm.edges if e.is_boundary), 'nonmanifoldEdges': sum(1 for e in bm.edges if len(e.link_faces) > 2), 'finFaces': len(dup)}
        if out_name == 'lod0.fbx': report['closedCheck'] = closed
        print(name, 'after fill:', closed, flush=True)
        bm.to_mesh(obj.data); bm.free()
        ca = obj.data.color_attributes
        for at in [x for x in ca if x.name != 'RfFix']: ca.remove(at)
        if 'RfFix' in ca: ca.active_color = ca['RfFix']; ca.render_color_index = ca.find('RfFix')
        report.setdefault('holesFilled', filled)
        if ratio <= .1:
            # 远处级别：删掉面数 < 0.4% 的独立小块（叶团表面的鳞片壳、小花、碎石），远看看不出来，
            # 而减面算法无法把独立块减到零（榕树 500 片鳞片壳让 1% 级别仍有 2400 面）
            bm2 = bmesh.new(); bm2.from_mesh(obj.data); bm2.faces.ensure_lookup_table()
            seen2, kill = set(), []
            lim = len(bm2.faces) * .004
            for f in bm2.faces:
                if f.index in seen2: continue
                st2, grp = [f], []; seen2.add(f.index)
                while st2:
                    g = st2.pop(); grp.append(g)
                    for e in g.edges:
                        for h in e.link_faces:
                            if h.index not in seen2: seen2.add(h.index); st2.append(h)
                if len(grp) < lim: kill.extend(grp)
            if kill: bmesh.ops.delete(bm2, geom=kill, context='FACES')
            bm2.to_mesh(obj.data); bm2.free()
        if ratio < 1:
            mod = obj.modifiers.new('decimate', 'DECIMATE')
            mod.decimate_type = 'COLLAPSE'; mod.ratio = ratio; mod.use_collapse_triangulate = True
            bpy.ops.object.modifier_apply(modifier=mod.name)
        try:
            bpy.ops.object.shade_auto_smooth(angle=0.70)
        except Exception:
            bpy.ops.object.shade_smooth()
        tris = sum(len(p.vertices) - 2 for p in obj.data.polygons)
        if out_name == 'lod0.fbx':
            for mat in obj.data.materials:
                if not mat or not mat.use_nodes: continue
                for node in mat.node_tree.nodes:
                    if node.type == 'TEX_IMAGE' and node.image:
                        img = node.image
                        if max(img.size) > MAX_TEX: img.scale(min(img.size[0], MAX_TEX), min(img.size[1], MAX_TEX))
                        img.filepath_raw = str(folder / 'albedo.png'); img.file_format = 'PNG'; img.save()
                        break
        bpy.ops.export_scene.fbx(filepath=str(folder / out_name), use_selection=True, path_mode='STRIP', embed_textures=False,
                                 mesh_smooth_type='OFF', apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y',
                                 bake_space_transform=True, use_mesh_modifiers=True, add_leaf_bones=False, colors_type='SRGB')
        if out_name == 'lod0.fbx':
            bpy.ops.export_scene.gltf(filepath=str(folder / 'clean.glb'), export_format='GLB', use_selection=True)
        report[out_name] = {'triangles': tris, 'removedFragmentFaces': removed}
        print(name, out_name, tris, 'tris, removed fragments', removed, flush=True)
    (folder / 'process.json').write_text(json.dumps(report, indent=2))
    # 清理后复审：开放边必须为 0（补洞是否成功的硬证据）
    r = subprocess.run([BLENDER, '-b', '--python', str(PROJ / 'Tools/mesh_audit_p2.py'), '--', str(folder / 'clean.glb'), str(folder / 'audit_clean'), name],
                       capture_output=True, text=True, timeout=1800)
    pa = folder / 'audit_clean' / f'{name}_audit.json'
    print(name, 'CLEAN AUDIT', summary(json.loads(pa.read_text())) if pa.exists() else 'failed', flush=True)


if __name__ == '__main__':
    argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
    cmd, rest = (argv[0], argv[1:]) if argv else ('step', [])
    names = [n for n in SPECS if not rest or n in rest]
    if cmd == 'approve':
        for n in rest: (OUT / n).mkdir(parents=True, exist_ok=True); (OUT / n / 'approved').write_text(time.strftime('%F %T')); print(n, 'approved')
    elif cmd == 'retex':
        # 只重做贴图（白模不动）：换贴图种子，改用按几何对齐（看不全的面也按几何补画）
        for n in rest:
            sf = OUT / n / 'state.json'; st = json.loads(sf.read_text())
            hist = st.setdefault('tex_history', []); hist.append({k: st.get(k) for k in ('tex', 'tex_request')})
            for k in ('tex', 'tex_result', 'tex_request'): st.pop(k, None)
            st['tex_seed'] = st.get('tex_seed', 77) + 101; st['tex_align'] = 'geometry'
            sf.write_text(json.dumps(st, indent=2, ensure_ascii=False))
            for f in ('model.glb', 'lod0.fbx', 'lod1.fbx', 'lod2.fbx', 'lod3.fbx', 'albedo.png', 'process.json', 'clean.glb'): (OUT / n / f).unlink(missing_ok=True)
            print(n, 'retex seed', st['tex_seed'], flush=True)
    elif cmd == 'reject':
        for n in rest: reject(n)
    elif cmd == 'lods':
        for n in names:
            if (OUT / n / 'lod0.fbx').exists() and not (OUT / n / 'lod4.fbx').exists():
                keep = LODS[:]; LODS[:] = [l for l in keep if not (OUT / n / l[0]).exists()]
                process(n); LODS[:] = keep
    elif cmd == 'process':
        for n in names:
            if (OUT / n / 'vat.json').exists():
                print(n, 'skip (creature meshes come from the rig bake)', flush=True); continue
            process(n)
    elif cmd in ('step', 'run'):
        while True:
            stages = {}
            for n in names:
                try:
                    stages[n] = advance(n)
                except Busy:
                    stages[n] = 'waiting-slot'
                except Exception as e:
                    msg = str(e)
                    stages[n] = ('waiting-net: ' if 'reach' in msg or 'SSL' in msg or 'timed out' in msg or 'Connection' in msg else 'error: ') + msg[:120]
            print(time.strftime('%H:%M:%S'), json.dumps(stages, ensure_ascii=False), flush=True)
            final = lambda v: v == 'downloaded' or v.startswith('error') or v.startswith('await-approval') or v.startswith('rejected') or v == 'no-ref'
            if cmd == 'step' or all(final(v) for v in stages.values()):
                break
            time.sleep(20)
        if cmd == 'run':
            for n in names:
                if (OUT / n / 'model.glb').exists() and not (OUT / n / 'lod0.fbx').exists(): process(n)
    print('TwilightKeys P2 DONE', cmd, flush=True)
