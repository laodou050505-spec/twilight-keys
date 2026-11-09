"""村庄远景模型收尾（保留 P2 原 UV / 原贴图，不做体素重建和重新烘焙）：python3 -I Tools/p2_finalize_keepuv.py <名称...>
model.glb（P2 原始带贴图模型）→ Tools/blender_keepuv.py（焊点、补洞、法线）→ repaired.glb → Tools/p2_lods.py（lod0..3 + albedo ≤2048）。
合格：所有闭合的洞都补上（剩下的开放边是贴片 / 薄板的自由边，连不成环，看不出破）。报告写 final/report.json（mode = keepuv）。"""
import json, pathlib, shutil, subprocess, sys
ROOT = pathlib.Path(__file__).resolve().parents[1]
P2 = ROOT / 'ArtSource' / 'ModelsP2'
B = '/Applications/Blender.app/Contents/MacOS/Blender'
for name in sys.argv[1:]:
    d = P2 / name
    src = d / 'model.glb'                # 不用 clean.glb：P2 流程的清理步骤会改坏 UV（墙面撕裂）
    fin = d / 'final'
    if fin.exists():                     # 旧结果移到废纸篓（不直接删）
        trash = pathlib.Path.home() / '.Trash' / 'tk_final_old'; trash.mkdir(parents=True, exist_ok=True)
        shutil.move(str(fin), str(trash / f'{name}_{int(__import__("time").time())}'))
    fin.mkdir()
    r = subprocess.run([B, '-b', '-P', str(ROOT / 'Tools/blender_keepuv.py'), '--', str(src), str(d / 'repaired.glb')], capture_output=True, text=True)
    rep = next((json.loads(l[7:]) for l in r.stdout.splitlines() if l.startswith('KEEPUV ')), None)
    if not rep:
        print(name, 'FAIL keepuv', r.stderr[-400:]); continue
    r2 = subprocess.run([B, '-b', '-P', str(ROOT / 'Tools/p2_lods.py'), '--', str(d / 'repaired.glb'), str(fin)], capture_output=True, text=True)
    lod = next((json.loads(l[5:]) for l in r2.stdout.splitlines() if l.startswith('LODS ')), None)
    if not lod:
        print(name, 'FAIL lods', r2.stderr[-400:]); continue
    ok = rep['pass']        # 闭合洞全部补上；剩下的开放边是贴片自由边
    (fin / 'report.json').write_text(json.dumps({'pass': ok, 'mode': 'keepuv', 'repair': rep, 'lods': lod['lods'], 'texture': lod.get('texture')}, indent=1, ensure_ascii=False))
    print(f"{name:20s} {'PASS' if ok else 'FAIL'} 补洞前 {rep['before']} → 后 {rep['after']}；" + ' '.join(f"{k}:{v['faces']}/{v['boundary']}/{v['nonmanifold']}" for k, v in lod['lods'].items()) + f" tex {lod.get('texture')}", flush=True)
