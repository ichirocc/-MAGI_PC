#!/usr/bin/env python3
import os as _os
_REPO = _os.path.dirname(_os.path.dirname(_os.path.abspath(__file__)))
# MAGI ランチャーアイコン（アダプティブ）の前景PNG＋モノクロPNGを各密度で生成し、プレビューも出力。
# デザイン: 白いカレンダー(青ヘッダ＋綴じタブ) + 3色のシフト行 + 緑の「完成」チェックバッジ。
# 背景は vector グラデ(別XML)。前景は安全領域(中央72dp)内に配置。座標は108dp系を4倍した432px。
from PIL import Image, ImageDraw
import os

RES = _os.path.join(_REPO, "app/src/main/res")
SZ = 432  # xxxhdpi (108dp * 4)
K = SZ / 108.0  # dp -> px

def u(v): return int(round(v * K))

def rrect(d, x0, y0, x1, y1, r, fill):
    d.rounded_rectangle([u(x0), u(y0), u(x1), u(y1)], radius=u(r), fill=fill)

def circle(d, cx, cy, r, fill):
    d.ellipse([u(cx-r), u(cy-r), u(cx+r), u(cy+r)], fill=fill)

WHITE=(255,255,255,255); BLUE=(59,130,246,255); PURPLE=(168,85,247,255)
ORANGE=(245,158,11,255); GREEN=(34,197,94,255)

def foreground():
    im = Image.new("RGBA", (SZ, SZ), (0,0,0,0)); d = ImageDraw.Draw(im)
    # 綴じタブ(白) — カレンダー上部から少し飛び出す
    rrect(d, 38,18, 45,33, 3.5, WHITE); rrect(d, 63,18, 70,33, 3.5, WHITE)
    # カレンダー本体(白) 角丸
    rrect(d, 24,26, 84,86, 11, WHITE)
    # 青ヘッダ(上だけ角丸→上端は本体に合わせ、下端は直線)
    head = Image.new("RGBA", (SZ, SZ), (0,0,0,0)); hd = ImageDraw.Draw(head)
    rrect(hd, 24,26, 84,52, 11, BLUE)
    hd.rectangle([u(24), u(42), u(84), u(52)], fill=BLUE)  # 角丸の下側を埋め直線化
    im.alpha_composite(head)
    # 3色のシフト行(丸ピル)
    rrect(d, 32,58, 76,66, 4, BLUE)
    rrect(d, 32,69, 62,77, 4, PURPLE)
    rrect(d, 32,80, 60,88, 4, ORANGE)  # ← 88は本体86を超えるので調整
    return im

def foreground_v2():
    im = Image.new("RGBA", (SZ, SZ), (0,0,0,0)); d = ImageDraw.Draw(im)
    rrect(d, 38,18, 45,33, 3.5, WHITE); rrect(d, 63,18, 70,33, 3.5, WHITE)
    rrect(d, 24,26, 84,86, 11, WHITE)
    head = Image.new("RGBA", (SZ, SZ), (0,0,0,0)); hd = ImageDraw.Draw(head)
    rrect(hd, 24,26, 84,52, 11, BLUE); hd.rectangle([u(24), u(42), u(84), u(52)], fill=BLUE)
    im.alpha_composite(head)
    # シフト行（本体内に収める: 56,67,78）
    rrect(d, 32,56, 76,64, 4, BLUE)
    rrect(d, 32,67, 61,75, 4, PURPLE)
    rrect(d, 32,78, 59,84, 3, ORANGE)
    # 緑の完成チェックバッジ（右下・本体角に重ねる）
    circle(d, 76,77, 13, WHITE)     # 白リング
    circle(d, 76,77, 11, GREEN)     # 緑
    d.line([(u(70),u(77)),(u(74.5),u(81.5)),(u(82),u(72))], fill=WHITE, width=u(3.0), joint="curve")
    # 端点を丸く
    for px,py in [(70,77),(82,72)]:
        d.ellipse([u(px-1.5),u(py-1.5),u(px+1.5),u(py+1.5)], fill=WHITE)
    return im

def monochrome():
    # テーマアイコン用: 単色シルエット(白)。システムが着色する。
    im = Image.new("RGBA", (SZ, SZ), (0,0,0,0)); d = ImageDraw.Draw(im)
    rrect(d, 38,18, 45,33, 3.5, WHITE); rrect(d, 63,18, 70,33, 3.5, WHITE)
    rrect(d, 24,26, 84,86, 11, WHITE)
    # ヘッダと行は穴(透明)で表現
    d.rounded_rectangle([u(30),u(56),u(78),u(64)], radius=u(4), fill=(0,0,0,0))
    rrect(d, 32,56, 76,64, 4, (0,0,0,0)); rrect(d, 32,67, 61,75, 4, (0,0,0,0)); rrect(d, 32,78, 59,84, 3, (0,0,0,0))
    # チェック
    d.line([(u(70),u(77)),(u(74.5),u(81.5)),(u(82),u(72))], fill=WHITE, width=u(3.0), joint="curve")
    return im

DENS = {"xxxhdpi":432, "xxhdpi":324, "xhdpi":216, "hdpi":162, "mdpi":108}

def save_all(master, name):
    for dens, px in DENS.items():
        out = master.resize((px, px), Image.LANCZOS)
        folder = f"{RES}/mipmap-{dens}"; os.makedirs(folder, exist_ok=True)
        out.save(f"{folder}/{name}.png")

fg = foreground_v2()
mo = monochrome()
save_all(fg, "ic_launcher_foreground")
save_all(mo, "ic_launcher_monochrome")

# プレビュー（藍グラデ地 + 前景 + squircleマスク）
def gradient(sz, c0, c1):
    g = Image.new("RGB", (sz, sz)); px = g.load()
    for y in range(sz):
        for x in range(sz):
            t = (x + y) / (2*sz)
            px[x,y] = tuple(int(c0[i]*(1-t)+c1[i]*t) for i in range(3))
    return g
bg = gradient(SZ, (59,130,246), (79,70,229)).convert("RGBA")
prev = Image.alpha_composite(bg, fg)
mask = Image.new("L", (SZ, SZ), 0); md = ImageDraw.Draw(mask)
md.rounded_rectangle([0,0,SZ-1,SZ-1], radius=int(SZ*0.235), fill=255)  # squircle近似
prev.putalpha(mask)
prev2 = Image.new("RGBA",(SZ,SZ),(245,245,247,255)); prev2.alpha_composite(prev)
prev2.convert("RGB").save(_os.path.join(_REPO, "tools/icon_preview.png"))
# 円マスク版も
maskc = Image.new("L",(SZ,SZ),0); ImageDraw.Draw(maskc).ellipse([0,0,SZ-1,SZ-1],fill=255)
pc = Image.alpha_composite(bg, fg); pc.putalpha(maskc)
pc2 = Image.new("RGBA",(SZ,SZ),(245,245,247,255)); pc2.alpha_composite(pc)
pc2.convert("RGB").save(_os.path.join(_REPO, "tools/icon_preview_round.png"))
print("WROTE foreground+monochrome for", *DENS.keys(), "+ previews")
