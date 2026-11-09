"""把 Tools/songs_extra/ 里校验通过的曲谱并进 Assets/_Twilight/Resources/songs.json：
· <id>.json：新曲（整首加入；id 已存在就替换）；
· master_<id>.json：给已有的歌补大师演奏谱；
· 老歌补上分类、英文名、作曲家（曲目面板分页用）。
没通过 Tools/chart_check.py 的文件跳过并列出来。可以反复跑（结果一样）。
python3 Tools/merge_songs.py（在工程根目录）。"""
import glob, json, os, sys
sys.path.insert(0, os.path.dirname(__file__))
from chart_check import check

PATH = 'Assets/_Twilight/Resources/songs.json'
OLD = {   # 老歌：分类、英文名、作曲家
    'twinkle': ('kids', 'Twinkle, Twinkle, Little Star', 'French folk melody (18th c.)'),
    'tigers': ('kids', 'Frère Jacques', 'French folk song'),
    'ode': ('classical', 'Ode to Joy', 'Ludwig van Beethoven (1770–1827)'),
    'jingle': ('kids', 'Jingle Bells', 'James Lord Pierpont (1822–1893)'),
    'elise': ('classical', 'Für Elise', 'Ludwig van Beethoven (1770–1827)'),
    'canon': ('classical', 'Canon in D', 'Johann Pachelbel (1653–1706)'),
    'auldlangsyne': ('folk', 'Auld Lang Syne', 'Scottish folk song'),
    'birthday': ('kids', 'Happy Birthday to You', 'Mildred & Patty Hill (1893)'),
    'largo': ('romantic', 'Going Home (Largo)', 'Antonín Dvořák (1841–1904)'),
    'londonbridge': ('kids', 'London Bridge Is Falling Down', 'English nursery rhyme'),
    'marylamb': ('kids', 'Mary Had a Little Lamb', 'American nursery rhyme'),
    'merryxmas': ('kids', 'We Wish You a Merry Christmas', 'English carol'),
    'minuet': ('classical', 'Minuet in G', 'Christian Petzold (1677–1733)'),
    'newyear': ('kids', 'Happy New Year', 'English children’s song'),
    'sakura': ('folk', 'Sakura Sakura', 'Japanese folk song'),
    'silentnight': ('kids', 'Silent Night', 'Franz Xaver Gruber (1787–1863)'),
    'songbie': ('folk', 'Farewell', 'John P. Ordway (1824–1880) / 李叔同'),
}
KEEP = ('id', 'title', 'subtitle', 'en', 'category', 'composer', 'difficulty', 'bpm', 'score', 'master')

lib = json.load(open(PATH, encoding='utf-8'))
by = {s['id']: s for s in lib['songs']}
for k, (cat, en, comp) in OLD.items():
    if k in by:
        by[k].setdefault('category', cat); by[k].setdefault('en', en); by[k].setdefault('composer', comp)

added, mastered, skipped = [], [], []
files = sorted(glob.glob('Tools/songs_extra/*.json'))
for p in [f for f in files if not os.path.basename(f).startswith('master_')] + [f for f in files if os.path.basename(f).startswith('master_')]:
    s = json.load(open(p, encoding='utf-8'))
    errs = check(s, p)
    if errs:
        skipped.append(f'{os.path.basename(p)}：{errs[0]}' + (f'（另有 {len(errs) - 1} 条）' if len(errs) > 1 else '')); continue
    if s.get('patch'):
        if s['id'] in by: by[s['id']]['master'] = s['master']; mastered.append(s['id'])
        continue
    n = {k: s[k] for k in KEEP if k in s}
    n['speeds'] = None
    if s['id'] in by: by[s['id']].update(n)
    else: lib['songs'].append(n); by[s['id']] = n
    added.append(s['id'])

json.dump(lib, open(PATH, 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
cats = {}
for s in lib['songs']: cats[s.get('category', '?')] = cats.get(s.get('category', '?'), 0) + 1
print(f'新曲 {len(added)} 首：{" ".join(added)}')
print(f'补大师谱 {len(mastered)} 首：{" ".join(mastered)}')
print(f'曲库共 {len(lib["songs"])} 首，分类 {cats}，有大师谱 {sum(1 for s in lib["songs"] if s.get("master"))} 首')
if skipped: print('跳过（没通过校验）：\n  ' + '\n  '.join(skipped))
