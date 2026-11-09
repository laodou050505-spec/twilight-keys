"""曲谱校验（规则和游戏里的 Assets/_Twilight/Scripts/Logic/Chart.cs 一致）：
python3 Tools/chart_check.py <歌曲.json> [...]      每个文件一首歌（见 Tools/songs_extra/README.md）
检查：音名合法（含 b / # / E# / B# / Cb / Fb）、音域 21..108、每个记号都有音、@偏移在记号时值内且递增、
      小节拍数 = meter（第一小节可以是弱起、最后一小节可以不满）、弹奏谱最快每秒 5.5 下、旋律音至少 5 个不同音高、
      第一个音必须是旋律（游戏按每个记号的第一个音分轨道）、版权字段齐全。
通过打印 OK，不通过打印每条问题并返回 1。"""
import json, sys

NAMES = ['C', 'C#', 'D', 'D#', 'E', 'F', 'F#', 'G', 'G#', 'A', 'A#', 'B']
FLAT = {'Db': 'C#', 'Eb': 'D#', 'Gb': 'F#', 'Ab': 'G#', 'Bb': 'A#', 'Cb': 'B', 'Fb': 'E', 'E#': 'F', 'B#': 'C'}


def note(n):
    n = n.strip()
    if len(n) < 2: raise ValueError(f'音名太短：{n!r}')
    letter = n[0].upper(); i = 1
    if i < len(n) and n[i] in '#b': letter += n[i]; i += 1
    octv = int(n[i:])
    if letter in FLAT:
        if letter == 'Cb': octv -= 1
        if letter == 'B#': octv += 1
        letter = FLAT[letter]
    if letter not in NAMES: raise ValueError(f'音名不对：{n!r}')
    m = (octv + 1) * 12 + NAMES.index(letter)
    if not 21 <= m <= 108: raise ValueError(f'超出钢琴音域：{n!r}')
    return m


def parse(score):
    """→ (事件列表, 小节列表)。事件 = (起拍, 时值, 旋律音, 全部音)；小节 = 拍数。"""
    events, bars, beat, bar = [], [], 0.0, 0.0
    for raw in score.split():
        if raw == '|':
            bars.append(bar); bar = 0.0; continue
        body, colon, d = raw.rpartition(':') if ':' in raw else (raw, '', '1')
        if not colon: body = raw
        dur = float(d)
        if dur <= 0: raise ValueError(f'时值必须 > 0：{raw}')
        allnotes, first, last_at = [], None, -1.0
        for part in body.split(','):
            p = part
            soft = p.startswith('~')
            if soft: p = p[1:]
            at = 0.0
            if '@' in p:
                p, a = p.split('@'); at = float(a)
            if at < last_at - 1e-9: raise ValueError(f'@偏移要递增：{raw}')
            if at >= dur - 1e-9 and at > 0: raise ValueError(f'@偏移超出记号时值：{raw}')
            last_at = at
            ns = [note(x) for x in p.split('+') if x.strip()]
            if not ns: raise ValueError(f'空的音组：{raw}')
            if first is None: first = ns[0]
            allnotes += ns
        events.append((beat, dur, first, allnotes))
        beat += dur; bar += dur
    bars.append(bar)
    return events, bars


CATS = ('classical', 'romantic', 'folk', 'kids')


def check(song, path='?'):
    errs = []
    patch = song.get('patch', False)          # 给曲库里已有的歌补 master（只要 id、meter、master）
    need = ('id', 'meter', 'master') if patch else ('id', 'title', 'subtitle', 'en', 'difficulty', 'bpm', 'meter', 'category', 'composer', 'pd', 'score', 'master')
    for k in need:
        if k not in song: errs.append(f'缺字段 {k}')
    if errs: return errs
    if patch:
        base = {x['id']: x for x in json.load(open('Assets/_Twilight/Resources/songs.json', encoding='utf-8'))['songs']}
        if song['id'] not in base: return [f'曲库里没有 {song["id"]}']
        song = dict(base[song['id']], **song)
    elif song['category'] not in CATS: errs.append(f'category 要是 {CATS} 之一')
    if not patch and song['difficulty'] not in (1, 2, 3): errs.append('difficulty 要是 1 / 2 / 3')
    if not (40 <= song['bpm'] <= 200): errs.append(f'bpm {song["bpm"]} 不在 40..200')
    meter = song['meter']
    for key in (('master',) if patch else ('score', 'master')):        # 补 master 时原 score 不再检查（老曲子不动）
        if key not in song or not song[key]: continue
        try:
            ev, bars = parse(song[key])
        except Exception as e:
            errs.append(f'{key}: {e}'); continue
        if patch:
            # 补老歌的大师谱：逐小节对照曲库里原来的 score（原 score 自己的小节可能不统一，照它来）
            ob = parse(song['score'])[1]
            if len(ob) != len(bars): errs.append(f'master 有 {len(bars)} 个小节，原 score 有 {len(ob)} 个（小节线要一一对应）')
            else:
                for i, (b, o) in enumerate(zip(bars, ob)):
                    if abs(b - o) > 0.011: errs.append(f'master 第 {i + 1} 小节 {b:.3f} 拍 ≠ 原 score 的 {o:.3f} 拍')
        else:
            for i, b in enumerate(bars):
                first, last = i == 0, i == len(bars) - 1
                if (first or last) and b <= meter + 0.011: continue
                if abs(b - meter) > 0.011: errs.append(f'{key} 第 {i + 1} 小节 {b:.3f} 拍 ≠ {meter}')
        mel = {e[2] for e in ev}
        if len(mel) < 5: errs.append(f'{key}: 旋律只有 {len(mel)} 个不同音高（至少 5 个，四条轨道才分得开）')
        if key == 'master':
            sb = sum(e[1] for e in parse(song['score'])[0]); mb = sum(e[1] for e in ev)
            if abs(sb - mb) > 0.011: errs.append(f'master 总拍数 {mb:g} ≠ score 总拍数 {sb:g}（两份谱要一起开始、一起结束）')
            notes = sum(len(e[3]) for e in ev)
            if notes < 1.6 * len(parse(song['score'])[0]): errs.append(f'master 只有 {notes} 个音，太稀（至少是 score 记号数的 1.6 倍：大师演奏要双手、和弦、分解和弦）')
        if key == 'score':
            fastest = min(e[1] for e in ev) * 60 / song['bpm']
            if fastest < 0.18: errs.append(f'score: 最短的一下只有 {fastest:.3f} 秒（弹奏谱最快每秒 5.5 下 → 最短 0.18 秒；太密的写进 master）')
            if not 24 <= len(ev) <= 260: errs.append(f'score: {len(ev)} 个记号（要 24..260）')
        total = sum(e[1] for e in ev) * 60 / song['bpm']
        if not patch and not 20 <= total <= 240: errs.append(f'{key}: 时长 {total:.0f} 秒（要 20..240）')   # 老歌的长短由原 score 决定
    return errs


if __name__ == '__main__':
    bad = 0
    for p in sys.argv[1:]:
        try:
            s = json.load(open(p, encoding='utf-8'))
        except Exception as e:
            print(f'{p}: JSON 读不了：{e}'); bad += 1; continue
        e = check(s, p)
        if e:
            bad += 1
            for x in e: print(f'{p}: {x}')
        else:
            if s.get('patch'):
                print(f'{p}: OK（补 master，{len(parse(s["master"])[0])} 个记号）'); continue
            ev, _ = parse(s['score'])
            extra = f'，master {len(parse(s["master"])[0])} 个记号' if s.get('master') else ''
            print(f'{p}: OK（score {len(ev)} 个记号{extra}）')
    sys.exit(1 if bad else 0)
