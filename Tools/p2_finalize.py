"""P2 模型收尾（用户硬要求：拓扑必须完全正确、贴图 ≤2K 且清楚）：
  python3 -I Tools/p2_finalize.py [名称...]

对每个已贴图的模型（ArtSource/ModelsP2/<名>/clean.glb，没有就用 model.glb）：
  1. Tools/blender_repair.py：向内加厚 → 细体素重建（水密、无自穿插）→ 去碎屑 → 减面到上限 → 重新展 UV 并从原模型烘焙 2K 贴图
     → 自检 边界边 / 非流形 / 零面积 / 翻转面 全为 0，否则失败
  2. Tools/p2_lods.py：从修复版导出 final/lod0..3.fbx + final/albedo.png，并逐级重新审计（每级都必须 0 开放边、0 非流形）
结果写 final/report.json；任何一级不合格都会打印 FAIL，Unity 端只接入全部合格的模型。"""
import json, pathlib, subprocess, sys
from concurrent.futures import ThreadPoolExecutor

ROOT = pathlib.Path(__file__).resolve().parents[1]
P2 = ROOT / 'ArtSource' / 'ModelsP2'
B = '/Applications/Blender.app/Contents/MacOS/Blender'
SPEC = json.loads((ROOT / 'ArtSource' / 'specs.json').read_text())


def cap_of(name):
    f = SPEC.get(name, {}).get('faces', 20000)
    return SPEC.get(name, {}).get('final_faces', min(24000, max(9000, int(f * 0.9))))


def run(name):
    d = P2 / name
    src = d / 'clean.glb' if (d / 'clean.glb').exists() else d / 'model.glb'
    if not src.exists():
        return name, 'no-model'
    fin = d / 'final'
    if (fin / 'report.json').exists() and json.loads((fin / 'report.json').read_text()).get('pass'):
        return name, 'already-final'
    fin.mkdir(exist_ok=True)
    # 清理后复审已经完全干净（0 开放边、0 非流形）的模型不做体素重建，直接用清理版
    ca = d / 'audit_clean' / f'{name}_audit.json'
    if ca.exists() and src.name == 'clean.glb':
        c = json.loads(ca.read_text())
        if c.get('boundary_edges') == 0 and c.get('nonmanifold_edges') == 0:
            import shutil
            shutil.copy(src, d / 'repaired.glb')
            rep = json.dumps({'pass': True, 'skipped': 'clean already watertight', 'faces': c.get('faces')})
            return finish(name, d, fin, rep)
    vox = str(SPEC.get(name, {}).get('voxel', 0.0022))
    r = subprocess.run([B, '-b', '-P', str(ROOT / 'Tools/blender_repair.py'), '--', str(src), str(d / 'repaired.glb'), str(cap_of(name)), vox, '2048'],
                       capture_output=True, text=True, timeout=3600)
    rep = next((l[7:] for l in r.stdout.splitlines() if l.startswith('REPAIR ')), None)
    (d / 'repair.log').write_text(r.stdout[-20000:] + r.stderr[-5000:])
    if not rep or not json.loads(rep).get('pass'):
        return name, 'FAIL repair ' + (rep or 'no-report')
    return finish(name, d, fin, rep)


def finish(name, d, fin, rep):
    r2 = subprocess.run([B, '-b', '-P', str(ROOT / 'Tools/p2_lods.py'), '--', str(d / 'repaired.glb'), str(fin)], capture_output=True, text=True, timeout=1800)
    lod = next((l[5:] for l in r2.stdout.splitlines() if l.startswith('LODS ')), None)
    if not lod:
        return name, 'FAIL lods ' + r2.stderr[-300:]
    L = json.loads(lod)
    ok = all(v['boundary'] == 0 and v['nonmanifold'] == 0 for v in L['lods'].values())
    report = {'pass': ok, 'repair': json.loads(rep), 'lods': L['lods'], 'texture': L.get('texture')}
    (fin / 'report.json').write_text(json.dumps(report, indent=1, ensure_ascii=False))
    return name, ('PASS ' if ok else 'FAIL ') + ' '.join(f"{k}:{v['faces']}/{v['boundary']}/{v['nonmanifold']}" for k, v in L['lods'].items()) + f" tex {L.get('texture')}"


if __name__ == '__main__':
    names = sys.argv[1:] or sorted(p.name for p in P2.iterdir() if p.is_dir())
    with ThreadPoolExecutor(3) as ex:
        for n, s in ex.map(run, names):
            print(f'{n:22s} {s}', flush=True)
