# 把 Salamander Grand Piano（CC-BY 3.0，Alexander Holm）v10 力度层处理成游戏用的 88 个 mp3：最近的录音移调 ±1 半音：剪掉开头静音、按原采样同键起音段响度对齐、3.6 秒带淡出
import subprocess, sys, numpy as np, os
FF = os.path.expanduser('~/twilight-keys-xr/node_modules/ffmpeg-static/ffmpeg')
SRC, ORIG, OUT = sys.argv[1], sys.argv[2], sys.argv[3]
SR = 44100
def dec(p, semis=0):
    af = [] if semis == 0 else ['-af', f'asetrate={48000 * 2 ** (semis / 12):.3f},aresample={SR}:resampler=swr:filter_size=64']
    r = subprocess.run([FF, '-v', 'error', '-i', p, *af, '-f', 'f32le', '-ac', '2', '-ar', str(SR), '-'], capture_output=True, check=True)
    return np.frombuffer(r.stdout, np.float32).reshape(-1, 2).copy()
def name(m):
    n = ["C","C#","D","D#","E","F","F#","G","G#","A","A#","B"][m % 12]; o = m // 12 - 1
    s = "s" if len(n) == 2 else ""; L = n[0]
    b = L.upper() + "-" + str(2 - o) if o <= 2 else (L.lower() if o == 3 else L.lower() + str(o - 3))
    return s + b + ".mp3"
def onset(x, rel_db):
    a = np.abs(x).max(1); pk = a.max()
    return int(np.argmax(a > pk * 10 ** (rel_db / 20)))
def rms(x, i):
    seg = x[i + int(.05 * SR): i + int(.40 * SR)]
    return float(np.sqrt((seg ** 2).mean()))
LEN, FADE = 3.6, 0.6
for m in range(21, 109):
    src = 21 + 3 * round((m - 21) / 3); d = m - src
    nm = ["C","C#","D","D#","E","F","F#","G","G#","A","A#","B"][src % 12] + str(src // 12 - 1)
    o = dec(os.path.join(ORIG, name(m))); n = dec(os.path.join(SRC, f"{nm}v10.flac"), d)
    io = onset(o, -30); i = max(0, onset(n, -30) - int(.004 * SR))     # 起音前留 4 ms
    n = n[i:i + int(LEN * SR)]
    if len(n) < int(LEN * SR): n = np.pad(n, ((0, int(LEN * SR) - len(n)), (0, 0)))
    f = int(FADE * SR); n[-f:] *= np.linspace(1, 0, f)[:, None] ** 2
    n[:int(.002 * SR)] *= np.linspace(0, 1, int(.002 * SR))[:, None]     # 2 ms 淡入，防咔哒
    g = rms(o, io) / max(1e-6, rms(n, int(.004 * SR)))
    pk = np.abs(n).max() * g
    if pk > .97: g *= .97 / pk
    n *= g
    out = os.path.join(OUT, name(m))
    subprocess.run([FF, '-v', 'error', '-y', '-f', 'f32le', '-ac', '2', '-ar', str(SR), '-i', '-', '-c:a', 'libmp3lame', '-b:a', '128k', out], input=n.astype(np.float32).tobytes(), check=True)
    print(f"{m:3d} {name(m):8s} ← {nm}{d:+d} 增益 {20*np.log10(g):+6.1f} dB  峰值 {np.abs(n).max():.2f}  原起音 {io/SR*1000:5.0f} ms")
