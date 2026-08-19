#!/usr/bin/env python3
import os as _os
_REPO = _os.path.dirname(_os.path.dirname(_os.path.abspath(__file__)))
# 思考誘導・指1本・スマホ特化 の完成予想モック（operator_ux.md 用）。トークン忠実・実装ではない。
from PIL import Image, ImageDraw, ImageFont
JP = "/usr/share/fonts/truetype/fonts-japanese-gothic.ttf"
S = 2.75
C = dict(primary="#3B82F6", onPrimary="#FFFFFF", primaryContainer="#DCE9FE", onPrimaryContainer="#0B2E66",
    secondaryContainer="#E6E9FF", onSecondaryContainer="#1E1B4B", tertiary="#22C55E", tertiaryContainer="#DCFCE7", onTertiaryContainer="#065F36",
    background="#F5F5F7", onSurface="#111318", surface="#FFFFFF", surfaceVariant="#F0F1F4", onSurfaceVariant="#6B7280",
    error="#EF4444", onError="#FFFFFF", errorContainer="#FEE2E2", onErrorContainer="#7F1D1D", outline="#D9DCE3", amberBg="#FEF3C7", amberFg="#7C5800")
def dp(x): return int(x*S)
def font(sp,b=False): return ImageFont.truetype(JP, dp(sp))
W = dp(360)
def canv(h): im=Image.new("RGB",(W,dp(h)),C["background"]); return im,ImageDraw.Draw(im)
def rr(d,x,y,w,h,r,fill,outline=None,ow=0): d.rounded_rectangle([x,y,x+w,y+h],radius=dp(r),fill=fill,outline=outline,width=ow)
def tx(d,x,y,s,sp,col,b=False,anchor="la",maxw=None):
    f=font(sp,b)
    if maxw and d.textlength(s,font=f)>maxw:
        while s and d.textlength(s+"…",font=f)>maxw: s=s[:-1]
        s+="…"
    d.text((x,y),s,font=f,fill=col,anchor=anchor)
def ctr(d,cx,y,s,sp,col,b=False): d.text((cx,y),s,font=font(sp,b),fill=col,anchor="ma")
def topbar(d,label,bg,fg):
    d.rectangle([0,0,W,dp(56)],fill=C["surface"]); rr(d,dp(16),dp(13),dp(64),dp(30),16,C["primary"])
    ctr(d,dp(48),dp(18),"MAGI",15,C["onPrimary"],True); tx(d,dp(90),dp(18),"勤務表",15,C["onSurfaceVariant"])
    f=font(13); cw=int(d.textlength(label,font=f))+dp(24); rr(d,W-dp(16)-cw,dp(15),cw,dp(26),16,bg)
    tx(d,W-dp(16)-cw+dp(12),dp(20),label,13,fg); d.line([0,dp(56),W,dp(56)],fill=C["outline"],width=1)
def nav(d,H,sel):
    ny=H-dp(64); d.rectangle([0,ny,W,H],fill=C["surface"]); d.line([0,ny,W,ny],fill=C["outline"],width=1)
    for i,l in enumerate(["ホーム","勤務表","直す","ようす","設定"]):
        cx=int(W/5*(i+0.5))
        if i==sel: rr(d,cx-dp(26),ny+dp(8),dp(52),dp(26),16,C["secondaryContainer"])
        ctr(d,cx,ny+dp(38),l,11,C["primary"] if i==sel else C["onSurfaceVariant"])
def bigbtn(d,H,label,col=None,fg=None):
    cy=H-dp(64)-dp(84); rr(d,dp(16),cy+dp(12),W-dp(32),dp(64),20,col or C["primary"])
    ctr(d,W//2,cy+dp(32),label,19,fg or C["onPrimary"],True)
def subbtn(d,H,label):
    cy=H-dp(64)-dp(84); ctr(d,W//2,cy-dp(2),label,14,C["primary"])

# ① 思考誘導ホーム：勤務表をつくる
def home_make():
    H=dp(680); im,d=canv(680); topbar(d,"まだ作っていません",C["surfaceVariant"],C["onSurfaceVariant"])
    x,y,w=dp(16),dp(72),W-dp(32)
    rr(d,x,y,w,dp(210),28,C["primaryContainer"])
    ctr(d,W//2,y+dp(26),"いま、これをしましょう",14,C["onPrimaryContainer"])
    tx(d,x+dp(28),y+dp(58),"② ボタンひとつで、",24,C["onPrimaryContainer"],True,maxw=w-dp(56))
    tx(d,x+dp(28),y+dp(96),"勤務表を作ります",24,C["onPrimaryContainer"],True,maxw=w-dp(56))
    tx(d,x+dp(28),y+dp(140),"スタッフ10名・31日ぶんを、コンピューターが",14,C["onPrimaryContainer"],maxw=w-dp(56))
    tx(d,x+dp(28),y+dp(164),"自動で組みます。だいたい10分。閉じても大丈夫。",14,C["onPrimaryContainer"],maxw=w-dp(56))
    y+=dp(228)
    rr(d,x,y,w,dp(72),20,C["surface"])
    tx(d,x+dp(20),y+dp(16),"いまの出来ぐあい",13,C["onSurfaceVariant"])
    tx(d,x+dp(20),y+dp(40),"まだ作っていません（下のボタンを押してください）",14,C["onSurface"],maxw=w-dp(40))
    subbtn(d,H,"下書きだけ作る"); bigbtn(d,H,"勤務表をつくる"); nav(d,H,0); return im

# ② 思考誘導ホーム：もう少し・なおす
def home_fix():
    H=dp(700); im,d=canv(700); topbar(d,"もう少し",C["amberBg"],C["amberFg"])
    x,y,w=dp(16),dp(72),W-dp(32)
    rr(d,x,y,w,dp(196),28,C["amberBg"])
    ctr(d,W//2,y+dp(24),"いま、これをしましょう",14,C["amberFg"])
    tx(d,x+dp(28),y+dp(54),"もう少しです。",24,C["amberFg"],True,maxw=w-dp(56))
    tx(d,x+dp(28),y+dp(92),"7/28(水)が人手不足です",22,C["amberFg"],True,maxw=w-dp(56))
    tx(d,x+dp(28),y+dp(134),"夜勤がひとり足りません。",14,C["amberFg"],maxw=w-dp(56))
    tx(d,x+dp(28),y+dp(158),"下のボタンで、1タップずつ直せます。",14,C["amberFg"],maxw=w-dp(56))
    y+=dp(214)
    rr(d,x,y,w,dp(96),20,C["surface"])
    tx(d,x+dp(20),y+dp(14),"いまの出来ぐあい",13,C["onSurfaceVariant"])
    tx(d,x+dp(20),y+dp(40),"できあがり度：78点",16,C["onSurface"],True)
    tx(d,x+dp(20),y+dp(66),"人手が足りない日：1日（7/28）",15,C["error"],True)
    subbtn(d,H,"もう一度つくる"); bigbtn(d,H,"なおすのを手伝って"); nav(d,H,0); return im

# ③ 直すお手伝い（人手不足を1タップで）
def guided_fix():
    H=dp(720); im,d=canv(720); topbar(d,"もう少し",C["amberBg"],C["amberFg"])
    x,y,w=dp(16),dp(72),W-dp(32)
    rr(d,x,y,w,dp(120),24,C["surface"])
    tx(d,x+dp(20),y+dp(18),"7/28(水) 夜勤の人手が足りません",18,C["onSurface"],True,maxw=w-dp(40))
    tx(d,x+dp(20),y+dp(52),"この日に動かせる人がいます。",15,C["onSurfaceVariant"],maxw=w-dp(40))
    tx(d,x+dp(20),y+dp(78),"だれかを夜勤に入れますか？（押すだけ）",15,C["onSurfaceVariant"],maxw=w-dp(40))
    y+=dp(140)
    cands=["山本さんを夜勤に入れる","佐藤さんを夜勤に入れる","鈴木さんを夜勤に入れる"]
    for c in cands:
        rr(d,x,y,w,dp(64),18,C["primaryContainer"])
        d.ellipse([x+dp(16),y+dp(18),x+dp(44),y+dp(46)],fill=C["primary"])
        ctr(d,x+dp(30),y+dp(24),"＋",16,C["onPrimary"],True)
        tx(d,x+dp(56),y+dp(20),c,16,C["onPrimaryContainer"],True,maxw=w-dp(80))
        y+=dp(76)
    rr(d,x,y+dp(6),w,dp(52),16,C["surface"],outline=C["outline"],ow=2)
    ctr(d,W//2,y+dp(20),"あとで決める",15,C["onSurface"])
    nav(d,H,1); return im

screens=[("op_01_home_make",home_make()),("op_02_home_fix",home_fix()),("op_03_guided_fix",guided_fix())]
import os; D=_os.path.join(_REPO, "docs/screens"); os.makedirs(D,exist_ok=True)
for n,im in screens: im.save(f"{D}/{n}.png")
print("WROTE", *[n for n,_ in screens])
