"""字体子集化：扫运行时脚本 + 曲库里出现的所有字，只保留这些字（外加 ASCII、常用标点），并改字体名（OFL 保留字体名要求）。
源字体在 ArtSource/Fonts（霞鹜文楷 / 马善政 / Cormorant Garamond，均为 SIL OFL 1.1），输出到 Assets/_Twilight/Resources/Fonts。
用法：python3 Tools/subset_fonts.py（在工程根目录）。改了界面文字后重新跑一次。"""
import glob, json, os, re, shutil, sys
from fontTools import subset
from fontTools.ttLib import TTFont
from fontTools.varLib import instancer

root = os.getcwd()
src = os.path.join(root, 'ArtSource/Fonts')
out = os.path.join(root, 'Assets/_Twilight/Resources/Fonts')
os.makedirs(out, exist_ok=True)

text = ''
# 第十五轮：运行时 + 编辑器脚本（搭场景时写进去的字也算）+ 曲库每首歌的所有文字字段（中文名、英文名、副标题……）。
# 之前只收 Runtime 脚本和曲库的 title / subtitle，后补的民歌名、英文名里的 é / ü、界面里的 ‹ › 都没收进来，
# 头显上这些字退回系统字体，看起来“字体不一样”。
for p in glob.glob(os.path.join(root, 'Assets/_Twilight/Scripts/**/*.cs'), recursive=True):
    text += open(p, encoding='utf-8').read()
songs = json.load(open(os.path.join(root, 'Assets/_Twilight/Resources/songs.json'), encoding='utf-8'))
for s in songs['songs']:
    for v in s.values():
        if isinstance(v, str): text += v
cjk = set(ch for ch in text if ord(ch) > 0x7E and ch.isprintable())
latin = set(chr(c) for c in range(0x20, 0x7F)) | set('×·—–…‘’“”%，。：；、！？（）《》·　')
chars = ''.join(sorted(cjk | latin))
print('字数', len(chars), '（中文', len(cjk), '）')

def rename(font, family, style='Regular'):
    for rec in font['name'].names:
        if rec.nameID in (1, 16): rec.string = family
        elif rec.nameID == 2 or rec.nameID == 17: rec.string = style
        elif rec.nameID == 4: rec.string = f'{family} {style}'
        elif rec.nameID == 6: rec.string = f'{family.replace(" ", "")}-{style}'
        elif rec.nameID == 3: rec.string = f'{family};{style};twilightkeys'

def make(srcname, outname, family, style='Regular', text_=chars, wght=None):
    f = TTFont(os.path.join(src, srcname))
    if wght is not None and 'fvar' in f:
        f = instancer.instantiateVariableFont(f, {'wght': wght})
    opts = subset.Options(); opts.layout_features = ['*']; opts.name_IDs = ['*']; opts.name_languages = ['*']; opts.notdef_outline = True
    s = subset.Subsetter(opts); s.populate(text=text_); s.subset(f)
    rename(f, family, style)
    f.save(os.path.join(out, outname))
    print(outname, os.path.getsize(os.path.join(out, outname)) // 1024, 'KB')

make('LXGWWenKai-Regular.ttf', 'TKKai.ttf', 'TK Kai')
make('MaShanZheng.ttf', 'TKBrush.ttf', 'TK Brush')
latin_only = ''.join(sorted(latin)) + '0123456789'
make('Cormorant.ttf', 'TKSerif.ttf', 'TK Serif', 'Medium', latin_only, 560)
make('Cormorant-Italic.ttf', 'TKSerifItalic.ttf', 'TK Serif', 'Italic', latin_only, 500)
make('Nunito.ttf', 'TKRound.ttf', 'TK Round', 'ExtraBold', latin_only, 800)
for lic in ('LXGWWenKai_OFL.txt', 'MaShanZheng_OFL.txt', 'Cormorant_OFL.txt', 'Nunito_OFL.txt'):
    shutil.copy(os.path.join(src, lic), os.path.join(out, lic.replace('.txt', '.txt.bytes') if False else lic))
open(os.path.join(out, 'README.txt'), 'w', encoding='utf-8').write(
    '本目录字体均为 SIL Open Font License 1.1 授权字体的子集（Modified Version），已按 OFL 要求改名：\n'
    '- TK Kai ← 霞鹜文楷 LXGW WenKai（github.com/lxgw/LxgwWenKai，许可见 LXGWWenKai_OFL.txt）\n'
    '- TK Brush ← 马善政楷书 Ma Shan Zheng（github.com/googlefonts/mashanzheng，许可见 MaShanZheng_OFL.txt）\n'
    '- TK Serif / TK Serif Italic ← Cormorant Garamond（github.com/CatharsisFonts/Cormorant，许可见 Cormorant_OFL.txt）\n'
    '- TK Round ← Nunito（github.com/googlefonts/nunito，许可见 Nunito_OFL.txt）\n'
    '子集只含游戏里用到的字，由 Tools/subset_fonts.py 生成。\n')
