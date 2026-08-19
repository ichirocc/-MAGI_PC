#!/usr/bin/env python3
import os as _os
_REPO = _os.path.dirname(_os.path.dirname(_os.path.abspath(__file__)))
# プロ編集モード（上級者）完成予想モック。power_user_ux.md 用。トークン忠実・実装ではない。
from PIL import Image, ImageDraw, ImageFont
JP="/usr/share/fonts/truetype/fonts-japanese-gothic.ttf"; S=2.75
C=dict(primary="#3B82F6",onPrimary="#FFFFFF",primaryContainer="#DCE9FE",onPrimaryContainer="#0B2E66",
  secondaryContainer="#E6E9FF",onSecondaryContainer="#1E1B4B",tertiary="#22C55E",tertiaryContainer="#DCFCE7",onTertiaryContainer="#065F36",
  background="#F5F5F7",onSurface="#111318",surface="#FFFFFF",surfaceVariant="#F0F1F4",onSurfaceVariant="#6B7280",
  error="#EF4444",onError="#FFFFFF",errorContainer="#FEE2E2",onErrorContainer="#7F1D1D",outline="#D9DCE3",amberBg="#FEF3C7",amberFg="#7C5800",
  aGreen="#22C55E",aOrange="#F59E0B",aPurple="#A855F7",aBlue="#3B82F6",aPink="#EC4899",aGray="#9CA3AF")
def dp(x): return int(x*S)
def font(sp,b=False): return ImageFont.truetype(JP,dp(sp))
W=dp(360)
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
    for i,l in enumerate(["ホーム","勤務表","編集","分析","設定"]):
        cx=int(W/5*(i+0.5))
        if i==sel: rr(d,cx-dp(26),ny+dp(8),dp(52),dp(26),16,C["secondaryContainer"])
        ctr(d,cx,ny+dp(38),l,11,C["primary"] if i==sel else C["onSurfaceVariant"])
def cmd2(d,H,a,b):
    cy=H-dp(64)-dp(72); d.rectangle([0,cy,W,cy+dp(72)],fill=C["surface"]); d.line([0,cy,W,cy],fill=C["outline"],width=1)
    rr(d,dp(16),cy+dp(12),dp(110),dp(48),16,C["surface"],outline=C["outline"],ow=2); ctr(d,dp(16)+dp(55),cy+dp(24),a,14,C["onSurface"])
    rr(d,dp(136),cy+dp(12),W-dp(16)-dp(136),dp(48),16,C["primary"]); ctr(d,(dp(136)+W-dp(16))//2,cy+dp(24),b,15,C["onPrimary"],True)

# ① プロ勤務表エディタ：複数選択＋一括編集＋診断ストリップ
def grid_editor():
    H=dp(700); im,d=canv(700); topbar(d,"編集中・必須3",C["amberBg"],C["amberFg"])
    x,y,w=dp(12),dp(64),W-dp(24)
    # selection toolbar
    rr(d,x,y,w,dp(46),12,C["secondaryContainer"]); tx(d,x+dp(10),y+dp(14),"選択12マス",13,C["onSecondaryContainer"],True)
    bx=x+dp(108)
    for lab,wd in [("シフト ▼",dp(72)),("休",dp(30)),("クリア",dp(48)),("入替",dp(42))]:
        rr(d,bx,y+dp(8),wd,dp(30),8,C["surface"]); ctr(d,bx+wd//2,y+dp(14),lab,12,C["onSurface"]); bx+=wd+dp(5)
    y+=dp(56)
    # dense month grid
    rr(d,x,y,w,dp(360),16,C["surface"]); tx(d,x+dp(12),y+dp(10),"月グリッド（31日・職員10名）— ドラッグで範囲選択",11,C["onSurfaceVariant"])
    syms=[("休","#9CA3AF"),("日","#22C55E"),("夜","#F59E0B"),("明","#A855F7"),("有","#3B82F6"),("P","#EC4899")]
    names=["古泉","山本","福澤","佐藤","鈴木","田中","渡辺","伊藤","中村","小林"]
    gx=x+dp(12); sw=dp(28); cells=31; cw=(w-dp(24)-sw)//cells; gy=y+dp(30)
    for j in range(cells):
        if (j+1)%5==0 or j==0: ctr(d,gx+sw+j*cw+cw//2,gy,str(j+1),7,C["onSurfaceVariant"])
    gy+=dp(12)
    import random; random.seed(7); chh=dp(20)
    for ri,nm in enumerate(names):
        tx(d,gx,gy+ri*(chh+dp(2))+dp(4),nm,9,C["onSurface"],maxw=sw-dp(2))
        for j in range(cells):
            s,col=syms[random.randrange(len(syms))]; cxp=gx+sw+j*cw; cyp=gy+ri*(chh+dp(2))
            rr(d,cxp,cyp,max(cw-1,dp(3)),chh,2,col)
            if random.random()<0.045: rr(d,cxp,cyp,max(cw-1,dp(3)),chh,2,None,outline=C["error"],ow=dp(1))
    # selection region highlight (rows 2-4, days 12-15)
    selx=gx+sw+12*cw; sely=gy+2*(chh+dp(2)); selw=4*cw; selh=3*(chh+dp(2))
    rr(d,selx-dp(1),sely-dp(1),selw,selh,3,None,outline=C["primary"],ow=dp(2))
    # diagnostics strip
    dy=y+dp(372)
    rr(d,x,dy,w,dp(40),10,C["surfaceVariant"])
    tx(d,x+dp(12),dy+dp(6),"必須3  [c3n2 covU1]   任意319   24,764 iter/s",12,C["onSurface"],True,maxw=w-dp(24))
    tx(d,x+dp(12),dy+dp(22),"スコア 3,000,319 ・ RSI++ ・ 仮説5本",11,C["onSurfaceVariant"],maxw=w-dp(24))
    cmd2(d,H,"元に戻す","▶ 再最適化"); nav(d,H,1); return im

# ② プロ診断パネル：族別の実数・スコア・ジャンプ
def diagnostics():
    H=dp(700); im,d=canv(700); topbar(d,"編集中・必須3",C["amberBg"],C["amberFg"])
    x,y,w=dp(16),dp(70),W-dp(32); ch=dp(486); rr(d,x,y,w,ch,22,C["surface"])
    tx(d,x+dp(16),y+dp(14),"違反の内訳（数値・族別）",17,C["onSurface"],True)
    # filter chips
    fx=x+dp(16); fy=y+dp(46)
    for lab,on in [("すべて",True),("重大のみ",False),("必須",False),("任意",False)]:
        f=font(12); cw=int(d.textlength(lab,font=f))+dp(20); rr(d,fx,fy,cw,dp(26),13,C["primary"] if on else C["surfaceVariant"])
        tx(d,fx+dp(10),fy+dp(5),lab,12,C["onPrimary"] if on else C["onSurfaceVariant"],True); fx+=cw+dp(8)
    rows=[("groupViol 担当外",0,C["aGray"],"必須"),("c3n 禁止の並び",14,C["error"],"必須"),
          ("covU 人手不足",4,C["error"],"必須"),("pref 希望ずれ",0,C["aGray"],"必須"),
          ("c1 連続/休",72,C["aOrange"],"任意"),("c3m 推奨並び",135,C["amberFg"],"任意"),
          ("c42 ペア",13,C["aPurple"],"任意"),("covO 過剰",17,C["aBlue"],"任意"),
          ("c2 回数",9,C["aGray"],"任意"),("high 上限超",2,C["aGray"],"任意")]
    ry=fy+dp(40); maxv=135
    for lab,val,col,kind in rows:
        tx(d,x+dp(16),ry,lab,13,C["onSurface"],True,maxw=dp(190))
        # bar
        bw=int((w-dp(120))*(val/maxv)); rr(d,x+dp(180),ry+dp(3),max(bw,dp(2)),dp(12),3,col if val>0 else C["surfaceVariant"])
        tx(d,x+w-dp(16),ry,str(val),13,C["onSurface"],True,anchor="ra")
        ry+=dp(30)
    rr(d,x+dp(16),ry+dp(4),w-dp(32),dp(1),0,C["outline"])
    tx(d,x+dp(16),ry+dp(14),"スコア 3,000,319 ・ 24,764 iter/s ・ RSI++ phase2",12,C["onSurfaceVariant"],maxw=w-dp(32))
    tx(d,x+dp(16),ry+dp(36),"行タップで該当セルへジャンプ。できあがり度：78点（共通）",12,C["onSurfaceVariant"],maxw=w-dp(32))
    cmd2(d,H,"元に戻す","▶ 再最適化"); nav(d,H,3); return im

screens=[("power_01_grid_editor",grid_editor()),("power_02_diagnostics",diagnostics())]
import os; D=_os.path.join(_REPO, "docs/screens"); os.makedirs(D,exist_ok=True)
for n,im in screens: im.save(f"{D}/{n}.png")
print("WROTE",*[n for n,_ in screens])
