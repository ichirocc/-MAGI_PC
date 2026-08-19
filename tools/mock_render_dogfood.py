#!/usr/bin/env python3
import os as _os
_REPO = _os.path.dirname(_os.path.dirname(_os.path.abspath(__file__)))
# Dogfooding mock of the MAGI operator loop: 初期解 → 手動修正 → 最適化.
# Token-accurate (colors/radii/type from MainActivity.kt + MagiApp.kt). Not an
# emulator screenshot — a faithful mock to eyeball one-finger ergonomics and the
# HR-manager workflow across every screen / popup / message.
from PIL import Image, ImageDraw, ImageFont

JP = "/usr/share/fonts/truetype/fonts-japanese-gothic.ttf"
S = 2.75  # dp -> px

C = dict(
    primary="#3B82F6", onPrimary="#FFFFFF",
    primaryContainer="#DCE9FE", onPrimaryContainer="#0B2E66",
    secondary="#6366F1", secondaryContainer="#E6E9FF", onSecondaryContainer="#1E1B4B",
    tertiary="#22C55E", tertiaryContainer="#DCFCE7", onTertiaryContainer="#065F36",
    background="#F5F5F7", onBackground="#111318",
    surface="#FFFFFF", onSurface="#111318",
    surfaceVariant="#F0F1F4", onSurfaceVariant="#6B7280",
    error="#EF4444", onError="#FFFFFF",
    errorContainer="#FEE2E2", onErrorContainer="#7F1D1D",
    outline="#D9DCE3",
    amberBg="#FEF3C7", amberFg="#7C5800",
    # MagiAccent shift palette
    aBlue="#3B82F6", aGreen="#22C55E", aOrange="#F59E0B", aPurple="#A855F7",
    aPink="#EC4899", aRed="#EF4444", aGray="#9CA3AF",
)

def dp(x): return int(x * S)
def font(sp, bold=False): return ImageFont.truetype(JP, dp(sp))
W = dp(360)

def new_canvas(h_dp):
    img = Image.new("RGB", (W, dp(h_dp)), C["background"])
    return img, ImageDraw.Draw(img)

def rrect(d, x, y, w, h, r, fill, outline=None, ow=0):
    d.rounded_rectangle([x, y, x + w, y + h], radius=dp(r), fill=fill, outline=outline, width=ow)

def dashed_rrect(d, x, y, w, h, color, ow=2, dash=9, gap=6):
    """SOFT violation cue: dashed border (Pillow has no dash; emulate on 4 edges)."""
    step = dash + gap
    xx = x
    while xx < x + w:
        d.line([xx, y, min(xx + dash, x + w), y], fill=color, width=ow)
        d.line([xx, y + h, min(xx + dash, x + w), y + h], fill=color, width=ow)
        xx += step
    yy = y
    while yy < y + h:
        d.line([x, yy, x, min(yy + dash, y + h)], fill=color, width=ow)
        d.line([x + w, yy, x + w, min(yy + dash, y + h)], fill=color, width=ow)
        yy += step

def text(d, x, y, s, sp, color, bold=False, anchor="la", maxw=None):
    f = font(sp, bold)
    if maxw and d.textlength(s, font=f) > maxw:
        while s and d.textlength(s + "…", font=f) > maxw:
            s = s[:-1]
        s += "…"
    d.text((x, y), s, font=f, fill=color, anchor=anchor)
    return f

def center(d, cx, y, s, sp, color, bold=False):
    d.text((cx, y), s, font=font(sp, bold), fill=color, anchor="ma")

def topbar(d, status_label, status_bg, status_fg):
    d.rectangle([0, 0, W, dp(56)], fill=C["surface"])
    bw = dp(64)
    rrect(d, dp(16), dp(13), bw, dp(30), 16, C["primary"])
    center(d, dp(16) + bw // 2, dp(18), "MAGI", 15, C["onPrimary"], True)
    text(d, dp(16) + bw + dp(10), dp(18), "勤務表", 15, C["onSurfaceVariant"])
    f = font(13)
    tw = d.textlength(status_label, font=f)
    cw = int(tw) + dp(24)
    rrect(d, W - dp(16) - cw, dp(15), cw, dp(26), 16, status_bg)
    text(d, W - dp(16) - cw + dp(12), dp(20), status_label, 13, status_fg)
    d.line([0, dp(56), W, dp(56)], fill=C["outline"], width=1)

def bottom_bars(d, H, sel, primary_label, primary_color=None, primary_fg=None):
    navh = dp(64); cmdh = dp(80)
    cmd_y = H - navh - cmdh
    d.rectangle([0, cmd_y, W, cmd_y + cmdh], fill=C["surface"])
    d.line([0, cmd_y, W, cmd_y], fill=C["outline"], width=1)
    ux = dp(16)
    rrect(d, ux, cmd_y + dp(10), dp(96), dp(60), 18, C["surface"], outline=C["outline"], ow=2)
    center(d, ux + dp(48), cmd_y + dp(28), "元に戻す", 14, C["onSurfaceVariant"])
    bx = ux + dp(96) + dp(10)
    rrect(d, bx, cmd_y + dp(10), W - dp(16) - bx, dp(60), 18, primary_color or C["primary"])
    center(d, (bx + W - dp(16)) // 2 - dp(10), cmd_y + dp(26), primary_label, 16, primary_fg or C["onPrimary"], True)
    ny = H - navh
    d.rectangle([0, ny, W, H], fill=C["surface"])
    d.line([0, ny, W, ny], fill=C["outline"], width=1)
    items = ["ホーム", "勤務表", "編集", "分析", "設定"]
    cwid = W / 5
    for i, lab in enumerate(items):
        cx = int(cwid * (i + 0.5))
        if i == sel:
            rrect(d, cx - dp(26), ny + dp(8), dp(52), dp(26), 16, C["secondaryContainer"])
        col = C["primary"] if i == sel else C["onSurfaceVariant"]
        center(d, cx, ny + dp(38), lab, 11, col)

def card(d, x, y, w, h, fill=None, r=22):
    rrect(d, x, y, w, h, r, fill or C["surface"])

def bigstat(d, x, y, w, value, label, vcol=None):
    h = dp(72)
    rrect(d, x, y, w, h, 22, C["surfaceVariant"])
    center(d, x + w // 2, y + dp(14), value, 22, vcol or C["onSurface"], True)
    center(d, x + w // 2, y + dp(46), label, 12, C["onSurfaceVariant"])
    return h

def segmented(d, x, y, w, options, sel):
    h = dp(48)
    rrect(d, x, y, w, h, 24, C["surfaceVariant"])
    n = len(options); seg = (w - dp(8)) // n
    for i, lab in enumerate(options):
        sx = x + dp(4) + i * seg
        if i == sel:
            rrect(d, sx, y + dp(4), seg, h - dp(8), 20, C["surface"])
        center(d, sx + seg // 2, y + dp(14), lab, 14, C["onSurface"] if i == sel else C["onSurfaceVariant"], i == sel)
    return h

def tagchip(d, x, y, label, color):
    f = font(13); tw = int(d.textlength(label, font=f))
    cw = tw + dp(20)
    # 16% tint bg
    rrect(d, x, y, cw, dp(24), 12, _tint(color, 0.16))
    text(d, x + dp(10), y + dp(4), label, 13, color, True)
    return cw

def _tint(hexc, a):
    r = int(hexc[1:3], 16); g = int(hexc[3:5], 16); b = int(hexc[5:7], 16)
    br, bg, bb = 255, 255, 255
    return "#%02X%02X%02X" % (int(r*a+br*(1-a)), int(g*a+bg*(1-a)), int(b*a+bb*(1-a)))

# ===== 1. HOME (初期解の状態 + 最適化導線) =================================
def home():
    H = dp(760); img, d = new_canvas(760)
    topbar(d, "要確認", C["amberBg"], C["amberFg"])
    x, y, w = dp(16), dp(70), W - dp(32)
    hh = dp(120)
    rrect(d, x, y, w, hh, 28, C["primaryContainer"])
    d.ellipse([x+dp(20), y+dp(24), x+dp(60), y+dp(64)], outline=C["onPrimaryContainer"], width=dp(2))
    center(d, x+dp(40), y+dp(30), "!", 22, C["onPrimaryContainer"], True)
    text(d, x+dp(76), y+dp(22), "あと一息です", 22, C["onPrimaryContainer"], True)
    text(d, x+dp(76), y+dp(54), "必須の人員不足が 1 件。最適化で解消を試せます。", 13, C["onPrimaryContainer"], maxw=w-dp(90))
    text(d, x+dp(76), y+dp(82), "充足 99%   不足 1   必要 240", 14, C["onPrimaryContainer"], True)
    y += hh + dp(14)
    # Copilot: satisfaction gauge
    gh = dp(108)
    card(d, x, y, w, gh)
    text(d, x+dp(16), y+dp(14), "操作アシスト", 17, C["onSurface"], True)
    center(d, x+w//2, y+dp(40), "78", 30, C["primary"], True)
    rrect(d, x+dp(16), y+dp(80), w-dp(32), dp(8), 4, C["surfaceVariant"])
    rrect(d, x+dp(16), y+dp(80), int((w-dp(32))*0.78), dp(8), 4, C["primary"])
    y += gh + dp(14)
    # Coverage diagnosis card
    dh = dp(176)
    card(d, x, y, w, dh)
    text(d, x+dp(16), y+dp(14), "人員不足の原因", 17, C["onSurface"], True)
    text(d, x+dp(16), y+dp(42), "不足 1 件 — 充足不可 0 枠 / 充足可能 1 枠。", 13, C["onSurfaceVariant"], maxw=w-dp(32))
    rrect(d, x+dp(16), y+dp(66), w-dp(32), dp(86), 20, C["secondaryContainer"])
    text(d, x+dp(28), y+dp(76), "6/13(金)  夜  必要3/現状2（不足1）", 14, C["onSecondaryContainer"], True, maxw=w-dp(150))
    tagchip(d, x+w-dp(96), y+dp(74), "充足可能", C["aBlue"])
    text(d, x+dp(28), y+dp(104), "担当可能5人。他シフトから1人移せば理論上は", 12, C["onSecondaryContainer"], maxw=w-dp(56))
    text(d, x+dp(28), y+dp(124), "充足可能（制約により最適化が未到達）", 12, C["onSecondaryContainer"], maxw=w-dp(56))
    bottom_bars(d, H, 0, "▶  最適化する")
    return img

# ===== 2. SCHEDULE 7-day (手動修正: セルタップ) ============================
def schedule7():
    H = dp(760); img, d = new_canvas(760)
    topbar(d, "要確認", C["amberBg"], C["amberFg"])
    x, y, w = dp(16), dp(70), W - dp(32)
    ch = dp(456)
    card(d, x, y, w, ch)
    text(d, x+dp(16), y+dp(14), "勤務表", 17, C["onSurface"], True)
    segmented(d, x+dp(16), y+dp(44), w-dp(32), ["7日表示", "カレンダー", "1ヶ月"], 0)
    text(d, x+dp(16), y+dp(100), "◀ ▶ かスワイプで日付を送り、セルをタップでシフト選択。", 11, C["onSurfaceVariant"])
    # nav row
    rrect(d, x+dp(16), y+dp(120), dp(72), dp(48), 16, C["primary"])
    center(d, x+dp(52), y+dp(134), "◀ 前", 14, C["onPrimary"], True)
    center(d, x+w//2, y+dp(134), "8〜14日 / 全31日", 15, C["onSurface"], True)
    rrect(d, x+w-dp(88), y+dp(120), dp(72), dp(48), 16, C["primary"])
    center(d, x+w-dp(52), y+dp(134), "次 ▶", 14, C["onPrimary"], True)
    # grid
    syms = ["休","日","夜","明","有","P","日","夜"]
    names = ["古泉 健一","山本 昌幸","福澤 俊陽","佐藤 美和","鈴木 隆"]
    gx, gy = x+dp(16), y+dp(184)
    cw, chh, gap = dp(34), dp(40), dp(4)
    for j in range(7):
        center(d, gx+dp(70)+j*(cw+gap)+cw//2, gy, str(j+8), 11, C["onSurfaceVariant"])
    gy += dp(22)
    import random; random.seed(7)
    for ri, nm in enumerate(names):
        text(d, gx, gy+ri*(chh+gap)+dp(10), nm, 12, C["onSurface"], maxw=dp(66))
        for j in range(7):
            k = (ri+j) % len(syms)
            sev = 0
            if (ri==2 and j==4): sev = 2   # HARD
            elif (ri==0 and j==2) or (ri==3 and j==5): sev = 1  # SOFT
            cxp = gx+dp(70)+j*(cw+gap); cyp = gy+ri*(chh+gap)
            rrect(d, cxp, cyp, cw, chh, 12, C["surfaceVariant"])
            center(d, cxp+cw//2, cyp+dp(11), syms[k], 15, C["onSurface"], True)
            if sev == 2:
                rrect(d, cxp, cyp, cw, chh, 12, None, outline=C["error"], ow=dp(2))
                rrect(d, cxp+cw-dp(9), cyp+dp(2), dp(7), dp(7), 3, C["error"])  # filled dot
            elif sev == 1:
                dashed_rrect(d, cxp, cyp, cw, chh, C["error"], ow=dp(2))
                d.ellipse([cxp+cw-dp(9), cyp+dp(2), cxp+cw-dp(2), cyp+dp(9)], outline=C["error"], width=dp(2))  # ring dot
            wmap = {(0, 1): (1, "日"), (2, 0): (1, "夜"), (3, 1): (1, "有"), (1, 3): (0, "夜"), (4, 5): (0, "休")}
            wd = wmap.get((ri, j))
            if wd is not None:
                wmet, wsym = wd
                wcol = C["tertiary"] if wmet else "#EC4899"
                bw, bhh = dp(15), dp(13)
                bx, by = cxp+cw-bw-dp(1), cyp+chh-bhh-dp(1)
                rrect(d, bx, by, bw, bhh, 4, wcol)  # wish badge (bottom-right): green=met / pink=unmet
                d.rounded_rectangle([bx, by, bx+bw, by+bhh], radius=dp(4), outline=C["surface"], width=dp(1))
                center(d, bx+bw//2, by+dp(1), wsym, 8, "#FFFFFF", True)  # wish symbol in white
    # legend
    ly = gy + 5*(chh+gap) + dp(6)
    rrect(d, gx, ly, dp(18), dp(14), 4, None, outline=C["error"], ow=dp(2))
    text(d, gx+dp(24), ly, "実線＝必須違反", 11, C["onSurfaceVariant"])
    dashed_rrect(d, gx+dp(150), ly, dp(18), dp(14), C["error"], ow=dp(2))
    text(d, gx+dp(174), ly, "破線＝要調整", 11, C["onSurfaceVariant"])
    ly2 = ly + dp(22)
    rrect(d, gx+dp(4), ly2+dp(1), dp(13), dp(11), 3, C["tertiary"])
    text(d, gx+dp(24), ly2, "緑＝希望どおり（反映済）", 11, C["onSurfaceVariant"])
    rrect(d, gx+dp(180), ly2+dp(1), dp(13), dp(11), 3, "#EC4899")
    text(d, gx+dp(200), ly2, "桃＝希望が未反映", 11, C["onSurfaceVariant"])
    bottom_bars(d, H, 1, "▶  最適化する")
    return img

# ===== 3. SHIFT PICKER SHEET (手動修正の主操作) ============================
def picker():
    H = dp(760); img, d = new_canvas(760)
    # dim base (schedule behind)
    schedule_bg = schedule7().resize((W, dp(760)))
    img.paste(schedule_bg, (0, 0))
    scrim = Image.new("RGBA", (W, dp(760)), (17, 19, 24, 130))
    img.paste(Image.alpha_composite(img.convert("RGBA"), scrim).convert("RGB"), (0, 0))
    d = ImageDraw.Draw(img)
    # bottom sheet
    sh = dp(470); sy = dp(760) - sh
    d.rounded_rectangle([0, sy, W, dp(760)+dp(28)], radius=dp(28), fill=C["surface"])
    rrect(d, W//2-dp(18), sy+dp(10), dp(36), dp(5), 3, C["outline"])
    text(d, dp(20), sy+dp(28), "佐藤 美和 ・ 12日 のシフト", 17, C["onSurface"], True)
    _xx = W - dp(46); _xy = sy + dp(28)
    d.line([_xx, _xy, _xx + dp(18), _xy + dp(18)], fill=C["onSurfaceVariant"], width=dp(2))
    d.line([_xx + dp(18), _xy, _xx, _xy + dp(18)], fill=C["onSurfaceVariant"], width=dp(2))
    text(d, dp(20), sy+dp(56), "担当できるシフトから選んでください（指1本・親指届く下段）", 12, C["onSurfaceVariant"])
    segmented(d, dp(20), sy+dp(82), W-dp(40), ["割当を変更", "希望を変更"], 0)
    rrect(d, dp(20), sy+dp(138), W-dp(40), dp(48), 16, C["primary"])
    center(d, W//2, sy+dp(152), "希望どおり 夜 にする", 15, C["onPrimary"], True)
    tiles = [("日","#22C55E",True),("夜","#F59E0B",False),("明","#A855F7",False),
             ("休","#9CA3AF",False),("有","#3B82F6",False),("P","#EC4899",False)]
    tx0, ty0 = dp(20), sy+dp(196); tw = (W-dp(40)-2*dp(10))//3; th = dp(64)
    for i,(s,col,seld) in enumerate(tiles):
        r,c = divmod(i,3)
        tx = tx0 + c*(tw+dp(10)); ty = ty0 + r*(th+dp(12))
        rrect(d, tx, ty, tw, th, 18, _tint(col,0.18))
        if seld: rrect(d, tx, ty, tw, th, 18, None, outline=col, ow=dp(3))
        d.ellipse([tx+dp(10), ty+dp(18), tx+dp(38), ty+dp(46)], fill=col)
        center(d, tx+dp(24), ty+dp(24), s, 16, "#FFFFFF", True)
        text(d, tx+dp(46), ty+dp(22), s+"勤", 15, C["onSurface"], True)
    cancel_y = ty0 + 2*(th+dp(12)) + dp(6)
    rrect(d, dp(20), cancel_y, W-dp(40), dp(52), 18, C["surfaceVariant"])
    center(d, W//2, cancel_y+dp(15), "閉じる", 16, C["onSurface"], True)
    return img

# ===== 4. CALENDAR month (問題日の発見) ===================================
def calendar():
    H = dp(760); img, d = new_canvas(760)
    topbar(d, "要確認", C["amberBg"], C["amberFg"])
    x, y, w = dp(16), dp(70), W - dp(32)
    ch = dp(470)
    card(d, x, y, w, ch)
    text(d, x+dp(16), y+dp(14), "勤務表", 17, C["onSurface"], True)
    segmented(d, x+dp(16), y+dp(44), w-dp(32), ["7日表示", "カレンダー", "1ヶ月"], 1)
    text(d, x+dp(16), y+dp(100), "日付タップでその週へ。色は多いシフト、赤枠は人員不足。", 11, C["onSurfaceVariant"])
    wd = ["月","火","水","木","金","土","日"]
    gx = x+dp(16); cw = (w-dp(32)-6*dp(4))//7
    for i,wl in enumerate(wd):
        col = C["aBlue"] if i==5 else (C["aRed"] if i==6 else C["onSurfaceVariant"])
        center(d, gx+i*(cw+dp(4))+cw//2, y+dp(124), wl, 11, col)
    pills = [("日","#22C55E"),("夜","#F59E0B")]
    import random; random.seed(2)
    start_dow = 2  # 1日が水曜
    cy = y+dp(146); ccw=cw; chh=dp(60)
    for cell in range(35):
        di = cell - start_dow
        cxp = gx + (cell%7)*(cw+dp(4)); yy = cy + (cell//7)*(chh+dp(4))
        if di < 0 or di >= 31:
            continue
        short = di in (12, 18, 25)
        rrect(d, cxp, yy, ccw, chh, 16, C["surfaceVariant"])
        if short:
            rrect(d, cxp, yy, ccw, chh, 16, None, outline=C["error"], ow=dp(2))
        text(d, cxp+dp(6), yy+dp(4), str(di+1), 11, C["onSurface"], True)
        if short:
            d.ellipse([cxp+ccw-dp(11), yy+dp(5), cxp+ccw-dp(5), yy+dp(11)], fill=C["error"])
        for pi,(s,col) in enumerate(pills):
            py = yy+dp(20)+pi*dp(15)
            rrect(d, cxp+dp(4), py, ccw-dp(8), dp(12), 3, col)
            text(d, cxp+dp(7), py-dp(1), f"{s}{3+pi}", 8, "#FFFFFF", True)
    bottom_bars(d, H, 1, "▶  最適化する")
    return img

# ===== 5. WISH-APPLY CONFIRM (メッセージ/ダイアログ) ======================
def dialog():
    H = dp(760); img, d = new_canvas(760)
    base = home().resize((W, dp(760)))
    img.paste(base, (0, 0))
    scrim = Image.new("RGBA", (W, dp(760)), (17, 19, 24, 140))
    img.paste(Image.alpha_composite(img.convert("RGBA"), scrim).convert("RGB"), (0, 0))
    d = ImageDraw.Draw(img)
    dw = W - dp(56); dx = dp(28); dy = dp(250); dh = dp(240)
    rrect(d, dx, dy, dw, dh, 28, C["surface"])
    text(d, dx+dp(24), dy+dp(24), "希望を反映しますか？", 20, C["onSurface"], True)
    msg = ["選択中の希望シフトを勤務表に上書きします。","担当外シフトの希望が 2 件あり、これらは",
           "反映されません（後でログから確認できます）。","この操作は「元に戻す」で取り消せます。"]
    for i,m in enumerate(msg):
        text(d, dx+dp(24), dy+dp(64)+i*dp(26), m, 13, C["onSurfaceVariant"], maxw=dw-dp(48))
    by = dy+dh-dp(64)
    rrect(d, dx+dp(24), by, dp(120), dp(48), 16, C["surfaceVariant"])
    center(d, dx+dp(24)+dp(60), by+dp(14), "キャンセル", 15, C["onSurface"])
    rrect(d, dx+dw-dp(24)-dp(130), by, dp(130), dp(48), 16, C["primary"])
    center(d, dx+dw-dp(24)-dp(65), by+dp(14), "反映する", 15, C["onPrimary"], True)
    return img

# ---- extra helpers --------------------------------------------------------
def slider(d, x, y, w, label, val_text, ratio):
    text(d, x, y, label, 14, C["onSurface"], True)
    text(d, x + w, y, val_text, 14, C["primary"], True, anchor="ra")
    ty = y + dp(26)
    rrect(d, x, ty, w, dp(6), 3, C["surfaceVariant"])
    rrect(d, x, ty, int(w * ratio), dp(6), 3, C["primary"])
    kx = x + int(w * ratio)
    d.ellipse([kx - dp(8), ty - dp(6), kx + dp(8), ty + dp(10)], fill=C["primary"])

def switch(d, x, y, on):
    rrect(d, x, y, dp(44), dp(26), 13, C["primary"] if on else "#CBD0DA")
    kx = x + (dp(20) if on else dp(2))
    d.ellipse([kx, y + dp(2), kx + dp(22), y + dp(24)], fill="#FFFFFF")

def listrow(d, x, y, w, icon_col, title, sub, tag=None, tagcol=None):
    h = dp(56)
    d.ellipse([x, y + dp(14), x + dp(28), y + dp(42)], fill=_tint(icon_col, 0.25))
    d.ellipse([x + dp(8), y + dp(22), x + dp(20), y + dp(34)], fill=icon_col)
    text(d, x + dp(40), y + dp(12), title, 15, C["onSurface"], True, maxw=w - dp(150))
    if sub:
        text(d, x + dp(40), y + dp(32), sub, 12, C["onSurfaceVariant"], maxw=w - dp(150))
    if tag:
        tw = int(d.textlength(tag, font=font(13))) + dp(20)
        tagchip(d, x + w - tw, y + dp(16), tag, tagcol or C["aBlue"])
    d.line([x, y + h, x + w, y + h], fill=C["outline"], width=1)
    return h

# ===== 6. EMPTY (未読込) ===================================================
def empty():
    H = dp(680); img, d = new_canvas(680)
    topbar(d, "未読込", C["surfaceVariant"], C["onSurfaceVariant"])
    x, y, w = dp(16), dp(150), W - dp(32)
    ch = dp(330); card(d, x, y, w, ch)
    d.ellipse([x + w // 2 - dp(34), y + dp(30), x + w // 2 + dp(34), y + dp(98)], outline=C["primary"], width=dp(3))
    center(d, x + w // 2, y + dp(48), "＋", 30, C["primary"], True)
    center(d, x + w // 2, y + dp(118), "勤務表を読み込みましょう", 19, C["onSurface"], True)
    center(d, x + w // 2, y + dp(148), "JSONを開くか、サンプルで試せます。", 13, C["onSurfaceVariant"])
    rrect(d, x + dp(24), y + dp(196), w - dp(48), dp(56), 18, C["primary"])
    center(d, x + w // 2, y + dp(212), "JSONを開く", 16, C["onPrimary"], True)
    rrect(d, x + dp(24), y + dp(262), w - dp(48), dp(56), 18, C["surface"], outline=C["outline"], ow=2)
    center(d, x + w // 2, y + dp(278), "サンプルを試す", 16, C["onSurface"], True)
    bottom_bars(d, H, 0, "▶  最適化する")
    return img

# ===== 7. HOME 実行中 ======================================================
def home_running():
    H = dp(760); img, d = new_canvas(760)
    topbar(d, "実行中", C["primaryContainer"], C["onPrimaryContainer"])
    x, y, w = dp(16), dp(70), W - dp(32)
    hh = dp(120); rrect(d, x, y, w, hh, 28, C["primaryContainer"])
    d.arc([x + dp(20), y + dp(28), x + dp(58), y + dp(66)], 30, 300, fill=C["primary"], width=dp(4))
    text(d, x + dp(76), y + dp(24), "計算中…", 22, C["onPrimaryContainer"], True)
    text(d, x + dp(76), y + dp(58), "残り約 540 秒 ・ 未解決 8 ・ 反復 1.2M", 13, C["onPrimaryContainer"], maxw=w - dp(90))
    text(d, x + dp(76), y + dp(84), "途中で閉じても入力は自動保存されます", 12, C["onPrimaryContainer"], maxw=w - dp(90))
    y += hh + dp(14)
    gh = dp(120); card(d, x, y, w, gh)
    text(d, x + dp(16), y + dp(14), "操作アシスト", 17, C["onSurface"], True)
    rrect(d, x + dp(16), y + dp(52), w - dp(32), dp(10), 5, C["surfaceVariant"])
    rrect(d, x + dp(16), y + dp(52), int((w - dp(32)) * 0.45), dp(10), 5, C["primary"])
    text(d, x + dp(16), y + dp(78), "5つの仮説を並列探索中（W0..W4）。最良解を随時反映。", 12, C["onSurfaceVariant"], maxw=w - dp(32))
    bottom_bars(d, H, 0, "■  計算を止める", primary_color=C["error"], primary_fg=C["onError"])
    return img

# ===== 8. 1ヶ月 色マトリクス ================================================
def month_matrix():
    H = dp(640); img, d = new_canvas(640)
    topbar(d, "要確認", C["amberBg"], C["amberFg"])
    x, y, w = dp(16), dp(70), W - dp(32)
    ch = dp(420); card(d, x, y, w, ch)
    text(d, x + dp(16), y + dp(14), "勤務表", 17, C["onSurface"], True)
    segmented(d, x + dp(16), y + dp(44), w - dp(32), ["7日表示", "カレンダー", "1ヶ月"], 2)
    text(d, x + dp(16), y + dp(100), "月全体を色で確認。日付タップでその週の7日表示へ。", 11, C["onSurfaceVariant"])
    syms = [("休", "#9CA3AF"), ("日", "#22C55E"), ("夜", "#F59E0B"), ("明", "#A855F7"), ("有", "#3B82F6"), ("P", "#EC4899")]
    names = ["古泉", "山本", "福澤", "佐藤", "鈴木", "田中", "渡辺", "伊藤", "中村", "小林"]
    gx = x + dp(16); staffW = dp(34); cells = 31
    cw = (w - dp(32) - staffW) // cells
    gy = y + dp(122)
    for j in range(cells):
        if (j + 1) % 5 == 0 or j == 0:
            center(d, gx + staffW + j * cw + cw // 2, gy, str(j + 1), 7, C["onSurfaceVariant"])
    gy += dp(14)
    import random; random.seed(5); chh = dp(20)
    for ri, nm in enumerate(names):
        text(d, gx, gy + ri * (chh + dp(3)) + dp(4), nm, 9, C["onSurface"], maxw=staffW - dp(2))
        for j in range(cells):
            s, col = syms[random.randrange(len(syms))]
            cxp = gx + staffW + j * cw; cyp = gy + ri * (chh + dp(3))
            rrect(d, cxp, cyp, max(cw - dp(1), dp(3)), chh, 2, col)
            if random.random() < 0.04:
                rrect(d, cxp, cyp, max(cw - dp(1), dp(3)), chh, 2, None, outline=C["error"], ow=dp(1))
    bottom_bars(d, H, 1, "▶  最適化する")
    return img

# ===== 9. 編集（希望の反映 / 一覧） =========================================
def edit():
    H = dp(740); img, d = new_canvas(740)
    topbar(d, "要確認", C["amberBg"], C["amberFg"])
    x, y, w = dp(16), dp(70), W - dp(32)
    y += segmented(d, x, y, w, ["今月の調整", "シフト希望", "基本マスター"], 2) + dp(10)
    rrect(d, x, y, w, dp(42), 14, C["amberBg"])
    text(d, x + dp(14), y + dp(12), "制度・人員が変わったときだけ編集します。", 12, C["amberFg"], maxw=w - dp(28))
    y += dp(42) + dp(10)
    secs = [("\u25bc  \u2460 \u30b7\u30d5\u30c8\u30fb\u30b0\u30eb\u30fc\u30d7\u30fb\u30b9\u30bf\u30c3\u30d5", "勤務の種類・グループ・職員を追加/編集"),
            ("\u25b6  \u2461 \u30b9\u30ad\u30eb\u30b0\u30eb\u30fc\u30d7", "似た技能をまとめる"),
            ("\u25b6  \u2462 回数（1人あたり）", "目標・個人の下限上限・グループ一括"),
            ("\u25b6  \u2463 人数と組み合わせ", "1日の必要数・禁止ペア（C41 / C42）"),
            ("\u25b6  \u2464 並び・くり返し", "連続や間隔のルール（並び希望 / 並び回避 ほか）")]
    for title, note in secs:
        rh = dp(62); card(d, x, y, w, rh)
        text(d, x + dp(16), y + dp(13), title, 14, C["onSurface"], True)
        text(d, x + dp(16), y + dp(38), note, 11, C["onSurfaceVariant"], maxw=w - dp(32))
        y += rh + dp(8)
    bottom_bars(d, H, 2, "\u25b6  最適化する")
    return img

# ===== 10. 分析（ゲージ＋統計＋リスク＋内訳） ================================
def analysis():
    H = dp(880); img, d = new_canvas(880)
    topbar(d, "配布可", C["tertiaryContainer"], C["onTertiaryContainer"])
    x, y, w = dp(16), dp(70), W - dp(32)
    y += segmented(d, x, y, w, ["一般", "プロ"], 0) + dp(10)
    # ようす（gauge）
    ch = dp(178); card(d, x, y, w, ch)
    text(d, x + dp(16), y + dp(14), "ようす（俯瞰）", 15, C["onSurface"], True)
    center(d, x + w // 2, y + dp(50), "100", 30, C["tertiary"], True)
    center(d, x + w // 2, y + dp(92), "/ 100", 12, C["onSurfaceVariant"])
    rrect(d, x + dp(24), y + dp(116), w - dp(48), dp(8), 4, C["surfaceVariant"])
    rrect(d, x + dp(24), y + dp(116), (w - dp(48)), dp(8), 4, C["tertiary"])
    center(d, x + w // 2, y + dp(132), "人員充足率（必要240を充足）", 12, C["onSurface"])
    y += ch + dp(10)
    # チェック概要
    ch2 = dp(58); card(d, x, y, w, ch2)
    text(d, x + dp(16), y + dp(12), "チェック概要", 13, C["onSurface"], True)
    text(d, x + dp(16), y + dp(33), "配れます（守るべき約束はすべて守れています）", 12, C["tertiary"], True)
    y += ch2 + dp(10)
    # 違反の内訳（3群・全18種=100%）
    bh = dp(300); card(d, x, y, w, bh)
    text(d, x + dp(16), y + dp(14), "違反の内訳", 14, C["onSurface"], True)
    text(d, x + w - dp(16), y + dp(16), "全18種を表示（100%）", 11, C["onSurfaceVariant"], anchor="ra")
    gy = y + dp(44)
    groups = [("必須（満たすべき）", [("希望不一致", 0, True), ("人数不足", 0, True)]),
              ("人数の範囲", [("下限割れ", 0, True), ("目標とのズレ", 3, False)]),
              ("任意（できれば）", [("連続パターン", 7, False), ("公平化のズレ", 6, False)])]
    for gname, items in groups:
        text(d, x + dp(16), gy, gname, 12, C["onSurface"], True); gy += dp(22)
        for lab, val, okv in items:
            bg, fg = (C["tertiaryContainer"], C["onTertiaryContainer"]) if okv else (C["amberBg"], C["amberFg"])
            rrect(d, x + dp(16), gy, w - dp(32), dp(24), 12, bg)
            text(d, x + dp(28), gy + dp(4), lab, 12, fg)
            text(d, x + w - dp(28), gy + dp(4), str(val), 12, fg, True, anchor="ra")
            gy += dp(28)
        gy += dp(6)
    bottom_bars(d, H, 3, "\u25b6  最適化する")
    return img

# ===== 11. 設定（スライダー/スイッチ/外観/データ） ==========================
def settings():
    H = dp(900); img, d = new_canvas(900)
    topbar(d, "要確認", C["amberBg"], C["amberFg"])
    x, y, w = dp(16), dp(70), W - dp(32)
    ch = dp(184); card(d, x, y, w, ch)
    text(d, x + dp(16), y + dp(14), "計算の設定", 17, C["onSurface"], True)
    slider(d, x + dp(16), y + dp(50), w - dp(32), "実行時間", "600 秒", 0.7)
    slider(d, x + dp(16), y + dp(112), w - dp(32), "並列数", "8", 1.0)
    y += ch + dp(12)
    th = dp(196); card(d, x, y, w, th)
    text(d, x + dp(16), y + dp(14), "通知と操作", 17, C["onSurface"], True)
    text(d, x + dp(16), y + dp(50), "完了時に通知する", 14, C["onSurface"]); switch(d, x + w - dp(60), y + dp(46), True)
    text(d, x + dp(16), y + dp(86), "片手モード（下方寄せ）", 14, C["onSurface"]); switch(d, x + w - dp(60), y + dp(82), False)
    text(d, x + dp(16), y + dp(120), "外観", 13, C["onSurfaceVariant"], True)
    segmented(d, x + dp(16), y + dp(140), w - dp(32), ["自動", "明", "暗", "UD"], 0)
    y += th + dp(12)
    dh2 = dp(170); card(d, x, y, w, dh2)
    text(d, x + dp(16), y + dp(14), "データ / ログ", 17, C["onSurface"], True)
    btns = [("JSONを開く", 0, 0), ("JSONを保存", 1, 0), ("CSVで保存", 0, 1), ("ログ出力", 1, 1)]
    bw2 = (w - dp(32) - dp(10)) // 2
    for lab, cc, rr in btns:
        bxp = x + dp(16) + cc * (bw2 + dp(10)); byp = y + dp(48) + rr * dp(56)
        rrect(d, bxp, byp, bw2, dp(48), 16, C["surface"], outline=C["outline"], ow=2)
        center(d, bxp + bw2 // 2, byp + dp(14), lab, 14, C["onSurface"])
    bottom_bars(d, H, 4, "▶  最適化する")
    return img

# ===== 12. 中断復帰バナー ===================================================
def interrupted():
    H = dp(760); img, d = new_canvas(760)
    topbar(d, "要確認", C["amberBg"], C["amberFg"])
    x, y, w = dp(16), dp(70), W - dp(32)
    bh = dp(160); card(d, x, y, w, bh)
    text(d, x + dp(16), y + dp(14), "前回の計算は中断されました", 17, C["onSurface"], True)
    text(d, x + dp(16), y + dp(46), "前回の計算は完了前に中断されました。", 13, C["onSurfaceVariant"], maxw=w - dp(32))
    text(d, x + dp(16), y + dp(66), "入力は自動保存済みです。もう一度実行できます。", 13, C["onSurfaceVariant"], maxw=w - dp(32))
    bwid = w - dp(32) - dp(96)
    rrect(d, x + dp(16), y + dp(100), bwid, dp(48), 16, C["primary"])
    center(d, x + dp(16) + bwid // 2, y + dp(114), "もう一度実行", 15, C["onPrimary"], True)
    rrect(d, x + w - dp(16) - dp(84), y + dp(100), dp(84), dp(48), 16, C["surface"], outline=C["outline"], ow=2)
    center(d, x + w - dp(16) - dp(42), y + dp(114), "閉じる", 15, C["onSurface"])
    y += bh + dp(14)
    hh = dp(110); rrect(d, x, y, w, hh, 28, C["primaryContainer"])
    text(d, x + dp(24), y + dp(22), "あと一息です", 22, C["onPrimaryContainer"], True)
    text(d, x + dp(24), y + dp(56), "必須の人員不足が 1 件。最適化で解消を試せます。", 13, C["onPrimaryContainer"], maxw=w - dp(48))
    text(d, x + dp(24), y + dp(82), "充足 99%  不足 1  必要 240", 14, C["onPrimaryContainer"], True)
    bottom_bars(d, H, 0, "▶  最適化する")
    return img

screens = [
    ("06_empty", empty()), ("01_home", home()), ("07_home_running", home_running()),
    ("02_schedule7", schedule7()), ("03_picker", picker()), ("04_calendar", calendar()),
    ("08_month_matrix", month_matrix()), ("09_edit", edit()), ("10_analysis", analysis()),
    ("11_settings", settings()), ("05_dialog", dialog()), ("12_interrupted", interrupted()),
]
import os
DOCS = _os.path.join(_REPO, "docs/screens")
os.makedirs(DOCS, exist_ok=True)
paths = []
for name, im in screens:
    # 仕様書に載せる版は docs/screens/（gitに含める）、PDF用にも使う
    p = f"{DOCS}/{name}.png"
    im.save(p); paths.append(p)

from reportlab.lib.pagesizes import A4
from reportlab.pdfgen import canvas as rcanvas
from reportlab.lib.utils import ImageReader
pdf_path = _os.path.join(_REPO, "tools/magi_dogfood.pdf")
pw, ph = A4
c = rcanvas.Canvas(pdf_path, pagesize=A4)
labels = ["00 未読込（EmptyState）",
          "01 ホーム（状態・不足の原因・最適化導線）",
          "02 ホーム 実行中（進捗・停止）",
          "03 勤務表 7日（HARD実線・SOFT破線＋凡例）",
          "04 シフト選択シート（手動修正・親指ゾーン）",
          "05 カレンダー月表示（問題日・シフト色ピル）",
          "06 勤務表 1ヶ月 色マトリクス",
          "07 編集（今月の調整/シフト希望/基本マスター5節）",
          "08 分析（一般/プロ・ようす・チェック概要・違反内訳18/18）",
          "09 設定（時間/並列・通知/片手・外観・データ）",
          "10 希望反映の確認ダイアログ（メッセージ）",
          "11 中断復帰バナー（プロセスkill耐性）"]
for p, lab in zip(paths, labels):
    im = Image.open(p); iw, ih = im.size
    scale = min(pw / iw, (ph - 60) / ih) * 0.95
    dw, dh = iw * scale, ih * scale
    c.setFont("Helvetica", 11)
    c.drawString(40, ph - 36, lab.encode("ascii", "replace").decode())
    c.drawImage(ImageReader(p), (pw - dw) / 2, (ph - dh) / 2 - 20, dw, dh)
    c.showPage()
c.save()
print("WROTE:", *paths, pdf_path, sep="\n")
