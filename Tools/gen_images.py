"""串行生图队列（Tripo nano banana）。用法：python3 -I Tools/gen_images.py <队列.json>
队列文件：[{"name": "...", "prompt": "...", "model": "banana_pro", "aspect": "16:9", "size": "2K"}, ...]
结果下载到队列文件同目录 <name>.png；已存在的跳过；遇到 429 等待后重试。"""
import json, pathlib, sys, time, urllib.request
sys.path.insert(0, '/Users/ld/.codex/skills/tripo-image/scripts')
import tripo_image as api

q = pathlib.Path(sys.argv[1]).resolve()
items = json.loads(q.read_text())
state_f = q.with_suffix('.state.json')
state = json.loads(state_f.read_text()) if state_f.exists() else {}

def save(): state_f.write_text(json.dumps(state, indent=1, ensure_ascii=False))

for it in items:
    out = q.parent / (it['name'] + '.png')
    out.parent.mkdir(parents=True, exist_ok=True)
    if out.exists():
        continue
    # 风格参考图：本地路径 → 上传一次，令牌缓存在状态文件里
    ref_tok = None
    if it.get('ref'):
        key = 'tok:' + it['ref']
        if key not in state:
            state[key] = api.upload_file(pathlib.Path(it['ref'])); save()
        ref_tok = state[key]
    tid = state.get(it['name'])
    while not tid:
        body = {'prompt': it['prompt'], 'model': it.get('model', 'banana_pro'), 'output_format': 'png'}
        if it.get('aspect'): body['aspect_ratio'] = it['aspect']
        if it.get('size'): body['size'] = it['size']
        if ref_tok: body['input'] = ref_tok
        path = '/generation/image-to-image' if ref_tok else '/generation/text-to-image'
        try:
            tid = api.request_json('POST', path, body)['data']['task_id']
            state[it['name']] = tid; save()
        except Exception as e:
            if '429' in str(e) or 'limit' in str(e).lower() or 'reach' in str(e) or 'SSL' in str(e):
                time.sleep(15); continue
            print('提交失败', it['name'], e, flush=True); break
    if not tid:
        continue
    while True:
        try:
            d = api.task(tid)
        except Exception as e:          # 网络抖动（SSL EOF 等）：等一下再查
            print('查询重试', it['name'], str(e)[:80], flush=True); time.sleep(10); continue
        s = d.get('status')
        if s == 'success':
            o = d.get('output') or {}
            url = o.get('generated_image_url') or o.get('image_url') or (o.get('images') or [None])[0]
            if isinstance(url, dict): url = url.get('url')
            for k in range(5):
                try:
                    urllib.request.urlretrieve(url, out); break
                except Exception as e:
                    print('下载重试', it['name'], str(e)[:80], flush=True); time.sleep(6)
            print('完成', it['name'], flush=True)
            break
        if s in ('failed', 'cancelled', 'banned', 'expired'):
            print('失败', it['name'], s, flush=True); state.pop(it['name'], None); save(); break
        time.sleep(6)
print('队列结束', flush=True)
