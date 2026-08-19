#!/usr/bin/env python3
import os as _os
_REPO = _os.path.dirname(_os.path.dirname(_os.path.abspath(__file__)))
# 現状(本ブランチ実装後)の主要画面のトークン忠実モック。ドッグフーディング用。実装文言・レイアウトに一致させる。
from PIL import Image, ImageDraw, ImageFont
import os, math
JP = "/usr/share/fonts/truetype/fonts-japanese-gothic.ttf"
S = 2.75
# [Material 3 トーナル配色] 暖色ニュートラル(クリーム)＋ダスティブルー/ラベンダー/セージ。実測コントラスト充足。
C = dict(primary="#4A63B8", onPrimary="#FFFFFF", primaryContainer="#DEE1FF", onPrimaryContainer="#00164B",
    secondaryContainer="#E1E0FF", onSecondaryContainer="#161A4C", tertiary="#2E6A4F",
    background="#FBF8F2", onSurface="#221F19", surface="#F4EFE6", surfaceVariant="#E5E1D8", onSurfaceVariant="#4A4640",
    error="#BA1A1A", errorContainer="#FFDAD6", onErrorContainer="#410002", outline="#CCC7BA",
    green="#B2F1C9", onGreen="#00210F", amber="#FBEAD0", amberFg="#6B4E00", redline="#BA1A1A")
def dp(x): return int(x*S)
def font(sp): return ImageFont.truetype(JP, dp(sp))
W = dp(360)
def canv(h): im=Image.new("RGB",(W,dp(h)),C["background"]); return im,ImageDraw.Draw(im)
def rr(d,x,y,w,h,r,fill,outline=None,ow=0): d.rounded_rectangle([x,y,x+w,y+h],radius=dp(r),fill=fill,outline=outline,width=ow)
def tx(d,x,y,s,sp,col,b=False,anchor="la",maxw=None):
    f=font(sp)
    if maxw and d.textlength(s,font=f)>maxw:
        while s and d.textlength(s+"…",font=f)>maxw: s=s[:-1]
        s+="…"
    d.text((x,y),s,font=f,fill=col,anchor=anchor)
def ctr(d,cx,y,s,sp,col,b=False): d.text((cx,y),s,font=font(sp),fill=col,anchor="ma")
def chev(d,cx,cy,col,r=4):  # 雑誌校正: 豆腐になる▾の代わりに描画チェブロン(下)
    rr_=int(r*S); d.line([cx-rr_,cy-rr_//2,cx,cy+rr_//2],fill=col,width=dp(2)); d.line([cx,cy+rr_//2,cx+rr_,cy-rr_//2],fill=col,width=dp(2))
def dlg_actions(d,x,y,w,confirm,dismiss="キャンセル"):  # [校正] ダイアログ操作=ボタン化（取消=外枠/確定=塗り・右寄せ）
    cw_=int(d.textlength(confirm,font=font(13)))+dp(36); cx2=x+w-dp(22)-cw_
    rr(d,cx2,y,cw_,dp(40),20,C["primary"]); ctr(d,cx2+cw_//2,y+dp(10),confirm,13,C["onPrimary"])
    dw_=int(d.textlength(dismiss,font=font(13)))+dp(32); dx=cx2-dp(10)-dw_
    rr(d,dx,y,dw_,dp(40),20,C["surface"],outline=C["outline"],ow=2); ctr(d,dx+dw_//2,y+dp(10),dismiss,13,C["onSurfaceVariant"])
def dlg_dismiss(d,x,y,w,label="キャンセル"):  # [校正] 単一の取消/閉じる=外枠ボタン（OutlinedButton既定: 枠=outline/文字=primary）
    dw_=int(d.textlength(label,font=font(13)))+dp(36); dx=x+w-dp(22)-dw_
    rr(d,dx,y,dw_,dp(40),20,C["surface"],outline=C["outline"],ow=2); ctr(d,dx+dw_//2,y+dp(10),label,13,C["primary"])
def topbar(d,label,bg,fg):
    d.rectangle([0,0,W,dp(56)],fill=C["surface"]); rr(d,dp(16),dp(13),dp(64),dp(30),16,C["primary"])
    ctr(d,dp(48),dp(18),"MAGI",15,C["onPrimary"]); tx(d,dp(90),dp(18),"勤務表",15,C["onSurfaceVariant"])
    f=font(13); cw=int(d.textlength(label,font=f))+dp(24); rr(d,W-dp(16)-cw,dp(15),cw,dp(26),16,bg)
    tx(d,W-dp(16)-cw+dp(12),dp(20),label,13,fg); d.line([0,dp(56),W,dp(56)],fill=C["outline"],width=1)
def nav(d,H,sel):
    ny=H-dp(60); d.rectangle([0,ny,W,H],fill=C["surface"]); d.line([0,ny,W,ny],fill=C["outline"],width=1)
    for i,l in enumerate(["ホーム","勤務表","直す","ようす","設定"]):
        cx=int(W/5*(i+0.5))
        if i==sel: rr(d,cx-dp(24),ny+dp(6),dp(48),dp(24),14,C["secondaryContainer"])
        ctr(d,cx,ny+dp(34),l,10,C["primary"] if i==sel else C["onSurfaceVariant"])
def cmdbar(d,H,label,danger=False):
    cy=H-dp(60)-dp(72); rr(d,dp(16),cy+dp(8),W-dp(32),dp(56),18,C["errorContainer"] if danger else C["primary"])
    ctr(d,W//2,cy+dp(26),label,17,C["onErrorContainer"] if danger else C["onPrimary"])

# ---- Operator next-action card (実装一致) ----
def opcard(d,x,y,w,cont,fg,headline_lines,nums,big,helper,running=False):
    h=dp(40)+len(headline_lines)*dp(30)+dp(30)+(dp(30) if running else 0)+(dp(64) if big else 0)+(dp(52) if helper else dp(16))
    rr(d,x,y,w,h,20,cont)
    cy=y+dp(18)
    for ln in headline_lines:
        tx(d,x+dp(20),cy,ln,19,fg,True,maxw=w-dp(40)); cy+=dp(30)
    cy+=dp(2)
    tx(d,x+dp(20),cy,nums,13,fg,maxw=w-dp(40)); cy+=dp(28)
    if running:
        d.arc([x+dp(20),cy,x+dp(20)+dp(18),cy+dp(18)],30,300,fill=fg,width=dp(2))
        tx(d,x+dp(46),cy,"組み立て中…",14,fg); cy+=dp(30)
    if big:
        rr(d,x+dp(16),cy,w-dp(32),dp(52),16,C["primary"]); ctr(d,x+w//2,cy+dp(15),big,16,C["onPrimary"]); cy+=dp(60)
    # [校正] 補助操作は外枠ボタン（地色でも見える枠色=前景色）。テキストリンク廃止。None なら出さない。
    if helper:
        rr(d,x+dp(16),cy-dp(4),w-dp(32),dp(40),16,cont,outline=fg,ow=2); ctr(d,x+w//2,cy+dp(6),helper,14,fg)
    return y+h

def home(state):
    if state=="amber":
        H=dp(720); im,d=canv(720); topbar(d,"もう少し",C["amber"],C["amberFg"])
        y=opcard(d,dp(16),dp(72),W-dp(32),C["amber"],C["amberFg"],
                 ["もう少しです。7/28(水) が","人手不足です。"],
                 "人手が足りない日：1日 ・ できあがり度：78点","なおすのを手伝って","もう一度つくる")
        # 操作アシスト(CopilotCard): できあがり度バー＋ヒント（StatusHeroは重複のため削除済み）
        y+=dp(14); rr(d,dp(16),y,W-dp(32),dp(96),20,C["surface"])
        tx(d,dp(32),y+dp(16),"操作アシスト",15,C["onSurface"],True)
        tx(d,dp(32),y+dp(44),"できあがり度",13,C["onSurfaceVariant"]); rr(d,dp(140),y+dp(46),dp(140),dp(10),6,C["surfaceVariant"]); rr(d,dp(140),y+dp(46),dp(110),dp(10),6,C["primary"]); tx(d,dp(292),y+dp(40),"78点",15,C["onSurface"],True)
        tx(d,dp(32),y+dp(68),"前回と同じ設定です。『なおすのを手伝って』が早いです。",12,C["onSurfaceVariant"],maxw=W-dp(48))
        cmdbar(d,H,"もう一度つくる"); nav(d,H,0); return im
    if state=="green":
        H=dp(700); im,d=canv(700); topbar(d,"配れます",C["green"],C["onGreen"])
        y=opcard(d,dp(16),dp(72),W-dp(32),C["green"],C["onGreen"],
                 ["③ できました！","そのまま配れます。"],
                 "人手が足りない日：0日 ・ できあがり度：96点","印刷・書き出し","中身を見る")
        # 操作アシスト（StatusHeroは重複のため削除済み＝配れますの三重表示を解消）
        y+=dp(14); rr(d,dp(16),y,W-dp(32),dp(72),20,C["surface"])
        tx(d,dp(32),y+dp(16),"操作アシスト",15,C["onSurface"],True)
        tx(d,dp(32),y+dp(44),"できあがり度",13,C["onSurfaceVariant"]); rr(d,dp(140),y+dp(46),dp(140),dp(10),6,C["surfaceVariant"]); rr(d,dp(140),y+dp(46),dp(135),dp(10),6,C["tertiary"]); tx(d,dp(292),y+dp(40),"96点",15,C["onSurface"],True)
        cmdbar(d,H,"もう一度つくる"); nav(d,H,0); return im
    if state=="running":
        H=dp(720); im,d=canv(720); topbar(d,"実行中",C["primaryContainer"],C["onPrimaryContainer"])
        y=opcard(d,dp(16),dp(72),W-dp(32),C["primaryContainer"],C["onPrimaryContainer"],
                 ["いま、コンピューターが","組んでいます。","あと約 7 分。","閉じても大丈夫です。"],
                 "人手が足りない日：2日 ・ できあがり度：61点",None,None,running=True)
        # LiveScheduleCard expanded
        y+=dp(14); ch=dp(150); rr(d,dp(16),y,W-dp(32),ch,16,C["surface"])
        tx(d,dp(28),y+dp(12),"▲ 途中経過を隠す",14,C["primary"])
        tx(d,dp(28),y+dp(38),"状態遷移  赤枠＝今回変化 (6)",11,C["onSurfaceVariant"])
        SH=["#E5E7EB","#FCA5A5","#FDBA74","#FCD34D","#86EFAC","#93C5FD","#C4B5FD"]
        gx,gy=dp(28),y+dp(60); cell=dp(7); gap=dp(1)
        import random; random.seed(7); changed={(random.randint(0,9),random.randint(0,30)) for _ in range(6)}
        for i in range(10):
            for j in range(31):
                cx=gx+j*(cell+gap); cyy=gy+i*(cell+gap)
                col=SH[(i*3+j*7)%len(SH)]
                d.rounded_rectangle([cx,cyy,cx+cell,cyy+cell],radius=dp(1),fill=col)
                if (i,j) in changed: d.rounded_rectangle([cx,cyy,cx+cell,cyy+cell],radius=dp(1),outline=C["redline"],width=dp(2))  # [校正②] 変化マーカー太化
        cmdbar(d,H,"やめる",danger=True); nav(d,H,0); return im

# ---- Dialog: guided fix ----
def dialog(title,body_fn,h_card):
    H=dp(640); im,d=canv(640)
    # behind: faint home
    d.rectangle([0,0,W,H],fill="#E9EAEE")
    topbar(d,"もう少し",C["amber"],C["amberFg"])
    d.rectangle([0,dp(56),W,H],fill=(0,0,0)); im=Image.blend(im,Image.new("RGB",(W,H),(20,22,28)),0.0)
    # scrim
    scrim=Image.new("RGBA",(W,H),(0,0,0,120)); im=Image.alpha_composite(im.convert("RGBA"),scrim).convert("RGB"); d=ImageDraw.Draw(im)
    cx=dp(20); cy=(H-h_card)//2; cw=W-dp(40)
    rr(d,cx,cy,cw,h_card,24,C["surface"])
    tx(d,cx+dp(22),cy+dp(20),title,18,C["onSurface"],True)
    body_fn(d,cx,cy+dp(56),cw)
    return im

def guided_fix(d,x,y,w):
    tx(d,x+dp(22),y,"7/28(水) の「Dﾃ」が 1人 足りません。",15,C["onSurface"],True,maxw=w-dp(44)); y+=dp(28)
    tx(d,x+dp(22),y,"この日に動かせる人がいます。だれかを",13,C["onSurfaceVariant"],maxw=w-dp(44)); y+=dp(22)
    tx(d,x+dp(22),y,"「Dﾃ」に入れますか？",13,C["onSurfaceVariant"]); y+=dp(30)
    for nm in ["山本 昌幸（休み）","佐藤 直美（休み）","福澤 俊陽"]:
        rr(d,x+dp(22),y,w-dp(44),dp(48),14,C["primary"]); ctr(d,x+w//2,y+dp(14),f"{nm} を「Dﾃ」に入れる",14,C["onPrimary"]); y+=dp(56)
    tx(d,x+dp(22),y,"入れたら「元に戻す」でいつでも取り消せます。",11,C["onSurfaceVariant"]); y+=dp(34)
    dlg_dismiss(d,x,y-dp(6),w,"閉じる")  # 単一の閉じるボタン（やめる重複を排除）

def csv_choice(d,x,y,w):
    tx(d,x+dp(22),y,"この勤務表CSVを、どちらとして",13,C["onSurface"],maxw=w-dp(44)); y+=dp(22)
    tx(d,x+dp(22),y,"取り込みますか？",13,C["onSurface"]); y+=dp(30)
    tx(d,x+dp(22),y,"・勤務表：表のとおり読み込みます。",12,C["onSurfaceVariant"],maxw=w-dp(44)); y+=dp(22)
    tx(d,x+dp(22),y,"・希望シフト：希望として読み込み、空から",12,C["onSurfaceVariant"],maxw=w-dp(44)); y+=dp(20)
    tx(d,x+dp(34),y,"作成して最適化で尊重します。",12,C["onSurfaceVariant"]); y+=dp(34)
    rr(d,x+dp(22),y,w-dp(44),dp(46),14,C["primary"]); ctr(d,x+w//2,y+dp(13),"勤務表として取り込む",14,C["onPrimary"]); y+=dp(54)
    rr(d,x+dp(22),y,w-dp(44),dp(46),14,C["surface"],outline=C["primary"],ow=dp(1)); ctr(d,x+w//2,y+dp(13),"希望シフトとして取り込む",14,C["primary"]); y+=dp(54)
    dlg_dismiss(d,x,y-dp(6),w,"キャンセル")

def seg(d,x,y,w,labels,selidx,hpx=None):
    h=hpx or dp(40); rr(d,x,y,w,h,12,C["surfaceVariant"]); sw=w//len(labels)
    for i,l in enumerate(labels):
        # [Planner] 選択中はやわらかい色付きピル（primaryContainer）。
        if i==selidx: rr(d,x+i*sw+dp(3),y+dp(3),sw-dp(6),h-dp(6),10,C["primaryContainer"])
        ctr(d,x+i*sw+sw//2,y+(h-dp(15))//2,l,13,C["onPrimaryContainer"] if i==selidx else C["onSurfaceVariant"])

# ---- 編集: 初期設定の「担当シフト + 適切回数」エディタ（新規 groupShiftApt） ----
def ws1_apt():
    H=dp(720); im,d=canv(720); topbar(d,"編集中",C["secondaryContainer"],C["onSecondaryContainer"])
    x,y,w=dp(16),dp(72),W-dp(32)
    ix=x+dp(18); base=ix+dp(34); rightlim=x+w-dp(18); unit=dp(88)
    shifts=["A4","Aｱ","B4","Dﾃ","休","Pｼ"]; onmask={"柳":[1,1,0,1,1,0],"桐":[1,1,1,0,1,1]}
    aptvals={"柳":[("A4",""),("Aｱ",""),("Dﾃ","10"),("休","10"),("Pｼ","")],"桐":[("A4",""),("Aｱ","1"),("B4",""),("休","10"),("Pｼ","")]}
    # [校正] 適切回数の±ステッパーは右端で折り返す。カード高は中身にフィット（余白の間延びを解消）。
    def apt_lines(g):
        cx=base; n=1
        for _ in aptvals[g]:
            if cx+unit>rightlim: n+=1; cx=base
            cx+=unit
        return n
    apt_rows=sum(apt_lines(g) for g in ["柳","桐"])
    cardh=dp(18)+dp(30)+2*dp(30)+dp(20)+dp(24)+dp(40)+dp(28)+apt_rows*dp(34)+(len(aptvals))*dp(8)+dp(8)
    rr(d,x,y,w,cardh,16,C["surface"]); iy=y+dp(18)
    tx(d,ix,iy,"担当可能シフト (群 × シフト)",14,C["onSurface"],True); iy+=dp(30)
    for g in ["柳","桐"]:
        tx(d,ix,iy,f"{g}:",13,C["onSurfaceVariant"]); cx=ix+dp(34)
        for k,s in enumerate(shifts):
            on=onmask[g][k]; lab=f"[{s}]" if on else f" {s} "
            tx(d,cx,iy,lab,13,C["primary"] if on else C["onSurfaceVariant"]); cx+=int(d.textlength(lab+"  ",font=font(13)))
        iy+=dp(30)
    iy+=dp(8); d.line([ix,iy,x+w-dp(18),iy],fill=C["outline"],width=1); iy+=dp(12)
    tx(d,ix,iy,"適切回数（任意・1人あたり目標）",14,C["onSurface"],True); iy+=dp(24)
    tx(d,ix,iy,"ONのシフトに目標回数を設定すると、最適化が各人をその回数に近づけます。",11,C["onSurfaceVariant"],maxw=w-dp(36)); iy+=dp(20)
    tx(d,ix,iy,"（空欄＝目標なし）",11,C["onSurfaceVariant"]); iy+=dp(28)
    for g in ["柳","桐"]:
        tx(d,ix,iy,f"{g}:",13,C["onSurfaceVariant"]); cx=base
        for s,v in aptvals[g]:
            if cx+unit>rightlim: iy+=dp(34); cx=base
            tx(d,cx,iy+dp(2),s,12,C["onSurface"]); cx+=dp(22)
            ctr(d,cx+dp(8),iy,"−",16,C["primary"]); cx+=dp(20)
            tx(d,cx,iy+dp(2),v if v else "—",12,C["onSurface"]); cx+=dp(20)
            ctr(d,cx+dp(8),iy,"＋",16,C["primary"]); cx+=dp(26)
        iy+=dp(34)+dp(8)
    nav(d,H,2); return im

# ---- 勤務表: 見るだけ/直す + 違反手がかり(実線/破線) ----
def schedule_view():
    H=dp(720); im,d=canv(720); topbar(d,"配れます",C["green"],C["onGreen"])
    x,y,w=dp(16),dp(72),W-dp(32)
    rr(d,x,y,w,dp(96),16,C["surface"])
    seg(d,x+dp(14),y+dp(14),w-dp(28),["見るだけ","直す"],0)
    rr(d,x+dp(14),y+dp(60),dp(80),dp(24),8,C["surfaceVariant"]); tx(d,x+dp(24),y+dp(64),"読取専用",12,C["onSurfaceVariant"])
    tx(d,x+dp(104),y+dp(64),"押しても変わりません（直すには「直す」へ）",12,C["onSurfaceVariant"],maxw=w-dp(120))
    y+=dp(110)
    seg(d,x,y,w,["7日","カレンダー","1ヶ月"],0); y+=dp(52)
    # grid: 5 staff x 7 days
    names=["古泉 健一","山本 昌幸","福澤 俊陽","佐藤 直美","桒澤 美幸"]
    cells=[["休","Aｱ","休","Dﾃ","休","B4","休"],["A4","休","休","Dﾃ","Dﾃ","休","A4"],
           ["Dﾃ","休","A4","Cｵ","有","休","Dﾃ"],["Cｵ","A4","Dﾃ","休","休","Aｱ","Cｵ"],["B1","B1","休","B1","B1","休","B1"]]
    SHc={"休":"#A7B4C2","Aｱ":"#74BEB0","A4":"#84C4DC","Dﾃ":"#B79CE0","Cｵ":"#84C4DC","B4":"#E0B968","B1":"#8CBE89","有":"#C2B4A0"}
    cw=int((w-dp(86))/7); ch=dp(40); gx=x+dp(86)
    hard={(1,3),(2,4)}; soft={(3,1),(0,5)}
    for i,nm in enumerate(names):
        ry=y+i*(ch+dp(4))
        rr(d,x,ry,dp(82),ch,8,C["surfaceVariant"]); tx(d,x+dp(8),ry+dp(12),nm,11,C["onSurface"],maxw=dp(70))
        for j in range(7):
            cx=gx+j*cw; col=SHc.get(cells[i][j],"#E5E7EB")
            rr(d,cx,ry,cw-dp(3),ch,8,col)
            ctr(d,cx+(cw-dp(3))//2,ry+dp(11),cells[i][j],11,C["onSurface"])
            if (i,j) in hard:
                d.rounded_rectangle([cx,ry,cx+cw-dp(3),ry+ch],radius=dp(8),outline=C["error"],width=dp(2))
                d.ellipse([cx+cw-dp(16),ry+dp(3),cx+cw-dp(7),ry+dp(12)],fill=C["error"])
            elif (i,j) in soft:
                for t in range(0,cw-dp(3),dp(6)): d.line([cx+t,ry,cx+min(t+dp(3),cw-dp(3)),ry],fill=C["amberFg"],width=2)
                for t in range(0,cw-dp(3),dp(6)): d.line([cx+t,ry+ch,cx+min(t+dp(3),cw-dp(3)),ry+ch],fill=C["amberFg"],width=2)
                d.ellipse([cx+cw-dp(16),ry+dp(3),cx+cw-dp(7),ry+dp(12)],outline=C["amberFg"],width=2)
    ly=y+5*(ch+dp(4))+dp(8)
    d.line([x,ly+dp(8),x+dp(20),ly+dp(8)],fill=C["error"],width=dp(2)); tx(d,x+dp(26),ly+dp(2),"実線＝必須違反",11,C["onSurfaceVariant"])
    for t in range(0,dp(20),dp(6)): d.line([x+dp(150)+t,ly+dp(8),x+dp(150)+min(t+dp(3),dp(20)),ly+dp(8)],fill=C["amberFg"],width=2)
    tx(d,x+dp(176),ly+dp(2),"破線＝要調整",11,C["onSurfaceVariant"])
    nav(d,H,1); return im

def card(d,x,y,w,h,title=None):
    rr(d,x,y,w,h,16,C["surface"])
    if title: tx(d,x+dp(18),y+dp(16),title,15,C["onSurface"],True)
    return y+(dp(44) if title else dp(16))

# ---- ようす（分析） ----
def analysis_view():
    H=dp(720); im,d=canv(720); topbar(d,"配れます",C["green"],C["onGreen"])
    x,y,w=dp(16),dp(72),W-dp(32)
    iy=card(d,x,y,w,dp(200),"ようす（できぐあい）")
    # ring gauge
    cxp,cyp,rad=x+dp(70),iy+dp(60),dp(48)
    d.arc([cxp-rad,cyp-rad,cxp+rad,cyp+rad],-90,270,fill=C["surfaceVariant"],width=dp(10))
    d.arc([cxp-rad,cyp-rad,cxp+rad,cyp+rad],-90,270-10,fill=C["tertiary"],width=dp(10))
    ctr(d,cxp,cyp-dp(16),"100%",22,C["onSurface"],True); ctr(d,cxp,cyp+dp(14),"人員充足率",11,C["onSurfaceVariant"])
    # [校正] ラベル左・数値右寄せの3行（旧コードの同座標重ね描き＝文字化けを解消）
    bx=x+dp(150); rite=x+w-dp(18); my=iy+dp(22)
    for lab,val,col in [("守れていない約束","0",C["tertiary"]),("人手が足りない日","0",C["tertiary"]),("できあがり度","96点",C["primary"])]:
        tx(d,bx,my,lab,12,C["onSurfaceVariant"],maxw=rite-bx-dp(52))
        tx(d,rite,my-dp(3),val,16,col,True,anchor="ra"); my+=dp(38)
    y+=dp(214)
    iy=card(d,x,y,w,dp(190),"気になる点（内訳）")
    chips=[("守れていない約束",[("担当できない",0),("希望違反",0),("人手不足",0),("禁止の並び",0)]),
           ("できれば直したい",[("並びのルール",2),("回数の偏り",1),("過剰な配置",1),("窓の要件",0)])]
    for gt,items in chips:
        tx(d,x+dp(18),iy,gt,12,C["onSurfaceVariant"]); iy+=dp(22)
        for r in range(2):
            for c in range(2):
                lab,n=items[r*2+c]; cxp=x+dp(18)+c*((w-dp(44))//2+dp(8)); cwc=(w-dp(44))//2
                bg=C["errorContainer"] if (n>0 and gt.startswith("守")) else (C["amber"] if n>0 else C["surfaceVariant"])
                fg=C["onErrorContainer"] if (n>0 and gt.startswith("守")) else (C["amberFg"] if n>0 else C["onSurfaceVariant"])
                rr(d,cxp,iy,cwc,dp(34),8,bg); tx(d,cxp+dp(10),iy+dp(9),lab,12,fg,maxw=cwc-dp(40)); tx(d,cxp+cwc-dp(20),iy+dp(8),str(n),14,fg,anchor="ma")
            iy+=dp(40)
        iy+=dp(4)
    nav(d,H,3); return im

# ---- 設定 ----
def settings_view():
    H=dp(740); im,d=canv(740); topbar(d,"配れます",C["green"],C["onGreen"])
    x,y,w=dp(16),dp(72),W-dp(32)
    iy=card(d,x,y,w,dp(120),"外観")
    seg(d,x+dp(18),iy,w-dp(36),["自動","明るい","暗い","見やすさ"],3); iy+=dp(52)
    rr(d,x+dp(18),iy,dp(44),dp(26),13,C["primary"]); d.ellipse([x+dp(40),iy+dp(3),x+dp(60),iy+dp(23)],fill=C["onPrimary"]); tx(d,x+dp(74),iy+dp(4),"片手で使う（下に寄せる）",14,C["onSurface"])
    y+=dp(134)
    iy=card(d,x,y,w,dp(168),"計算の設定")
    tx(d,x+dp(18),iy,"じっくり度（短い⇄じっくり・最大5分）",13,C["onSurfaceVariant"]); iy+=dp(24)
    rr(d,x+dp(18),iy+dp(6),w-dp(36),dp(6),3,C["surfaceVariant"]); rr(d,x+dp(18),iy+dp(6),int((w-dp(36))*0.6),dp(6),3,C["primary"]); d.ellipse([x+dp(18)+int((w-dp(36))*0.6)-dp(10),iy-dp(2),x+dp(18)+int((w-dp(36))*0.6)+dp(10),iy+dp(18),],fill=C["primary"]); iy+=dp(28)
    tx(d,x+dp(18),iy,"だいたい 5分",13,C["onSurface"],True); iy+=dp(28)
    rr(d,x+dp(18),iy,dp(44),dp(26),13,C["primary"]); d.ellipse([x+dp(40),iy+dp(3),x+dp(60),iy+dp(23)],fill=C["onPrimary"]); tx(d,x+dp(74),iy+dp(4),"終わったら通知する",14,C["onSurface"])
    y+=dp(182)
    iy=card(d,x,y,w,dp(156),"データ")  # [校正] 2行ぶんの高さを確保（下段ボタンの見切れを解消）
    btns=[["データを開く","保存する"],["CSVを開く","CSVで保存"]]
    for r in range(2):
        for c in range(2):
            cxp=x+dp(18)+c*((w-dp(44))//2+dp(8)); cwc=(w-dp(44))//2
            rr(d,cxp,iy,cwc,dp(44),12,C["surface"],outline=C["outline"],ow=2); ctr(d,cxp+cwc//2,iy+dp(13),btns[r][c],13,C["primary"])
        iy+=dp(52)
    y+=dp(170)
    rr(d,x,y,w,dp(50),14,C["surface"]); tx(d,x+dp(18),y+dp(16),"詳細設定（上級者向け）",14,C["onSurface"])
    # [Planner] 丸＋アイコンシェブロン（開く）
    d.ellipse([x+w-dp(46),y+dp(13),x+w-dp(18),y+dp(41)],fill=C["surfaceVariant"]); cxp=x+w-dp(32); cyp=y+dp(25)
    d.line([cxp-dp(5),cyp-dp(2),cxp,cyp+dp(3)],fill=C["onSurfaceVariant"],width=dp(2)); d.line([cxp,cyp+dp(3),cxp+dp(5),cyp-dp(2)],fill=C["onSurfaceVariant"],width=dp(2))
    nav(d,H,4); return im

# ---- 編集タブ 年次マスター（スキルグループ）----
def edit_year():
    H=dp(792); im,d=canv(792); topbar(d,"編集中",C["primaryContainer"],C["onPrimaryContainer"])
    x,y,w=dp(16),dp(72),W-dp(32)
    seg(d,x,y,w,["月次（毎月）","年次マスター"],1); y+=dp(50)
    rr(d,x,y,w,dp(40),12,C["secondaryContainer"]); tx(d,x+dp(12),y+dp(11),"制度・人員が変わったときだけ編集してください。",12,C["onSecondaryContainer"],maxw=w-dp(24)); y+=dp(50)
    # SkillGroupCard
    rr(d,x,y,w,dp(282),16,C["surface"]); iy=y+dp(14)
    tx(d,x+dp(16),iy,"スキルグループ（年次・新ルール用）",14,C["onSurface"],True); iy+=dp(22)
    tx(d,x+dp(16),iy,"ユニットとは別の分類。担当可否には影響せず、スキル別の回数/組み合わせ禁止に使用（1人1スキル）。",10,C["onSurfaceVariant"],maxw=w-dp(32)); iy+=dp(28)
    for kg,nm in [("N","看護"),("C","介護")]:
        tx(d,x+dp(16),iy+dp(6),f"{kg}  {nm}",13,C["onSurface"])
        # [校正] 編集=トーナル塗り / 削除=エラー外枠 のボタン化（テキストリンク廃止）
        ew=dp(52); dw=dp(52); ey=iy
        ex=x+w-dp(16)-dw-dp(6)-ew
        rr(d,ex,ey,ew,dp(30),14,C["secondaryContainer"]); ctr(d,ex+ew//2,ey+dp(7),"編集",11,C["onSecondaryContainer"])
        dx2=ex+ew+dp(6)
        rr(d,dx2,ey,dw,dp(30),14,C["surface"],outline=C["error"],ow=2); ctr(d,dx2+dw//2,ey+dp(7),"削除",11,C["error"])
        iy+=dp(38)
    # +追加=外枠＋＋アイコン のボタン
    aw=dp(184); rr(d,x+dp(16),iy,aw,dp(34),16,C["surface"],outline=C["primary"],ow=2)
    ctr(d,x+dp(16)+aw//2,iy+dp(9),"＋ スキルグループ追加",11,C["primary"]); iy+=dp(42)
    d.line([x+dp(16),iy,x+w-dp(16),iy],fill=C["outline"]); iy+=dp(10)
    tx(d,x+dp(16),iy,"職員のスキル割当",13,C["onSurface"],True); iy+=dp(26)
    for nm,kg in [("古泉 健一","N"),("山本 昌幸","C")]:
        tx(d,x+dp(16),iy+dp(6),nm,12,C["onSurface"]); bx=x+w-dp(80); rr(d,bx,iy,dp(64),dp(34),10,C["surface"],outline=C["outline"],ow=2); tx(d,bx+dp(16),iy+dp(8),kg,13,C["primary"]); chev(d,bx+dp(48),iy+dp(18),C["primary"]); iy+=dp(42)
    y+=dp(294)
    rr(d,x,y,w,dp(56),16,C["surface"]); tx(d,x+dp(16),y+dp(18),"ルールの編集（並び・回数）",14,C["onSurface"]); chev(d,x+w-dp(28),y+dp(28),C["onSurfaceVariant"],5)
    nav(d,H,2); return im

# ---- シフト選択シート（ModalBottomSheet）: セルtap編集の担当可タイル ----
def shift_picker():
    H=dp(720); im,d=canv(720); topbar(d,"配れます",C["green"],C["onGreen"])
    d.rectangle([0,dp(56),W,H],fill=C["surface"])  # 背後の勤務表(簡略)
    scrim=Image.new("RGBA",(W,H),(0,0,0,110)); im=Image.alpha_composite(im.convert("RGBA"),scrim).convert("RGB"); d=ImageDraw.Draw(im)
    sh=dp(330); sy=H-sh; rr(d,0,sy,W,sh+dp(24),24,C["background"])
    rr(d,W//2-dp(18),sy+dp(12),dp(36),dp(5),3,C["outline"])  # ドラッグハンドル
    x=dp(20); y=sy+dp(32)
    tx(d,x,y,"古泉 健一 ・ 7日 のシフトを選ぶ",15,C["onSurface"],True); y+=dp(40)
    tiles=[("A4","#84C4DC"),("Aｱ","#74BEB0"),("B4","#E0B968"),("Dﾃ","#B79CE0"),("Cｵ","#84C4DC"),("休","#A7B4C2"),("Pｼ","#8CBE89"),("有","#C2B4A0")]
    sel=3  # Dﾃ=選択中(primary)
    cols=4; gap=dp(8); tw=(W-dp(40)-gap*(cols-1))//cols; th=dp(56)
    for idx,(sym,col) in enumerate(tiles):
        r=idx//cols; c=idx%cols; cx=x+c*(tw+gap); cyy=y+r*(th+gap)
        rr(d,cx,cyy,tw,th,16,C["primary"] if idx==sel else col)
        ctr(d,cx+tw//2,cyy+dp(18),sym,15,C["onPrimary"] if idx==sel else "#101010")
    return im

def skill_dialog(d,x,y,w):
    tx(d,x+dp(22),y,"記号（例: N）",12,C["onSurfaceVariant"]); y+=dp(20)
    rr(d,x+dp(22),y,w-dp(44),dp(44),10,C["surface"],outline=C["primary"],ow=2); tx(d,x+dp(34),y+dp(12),"N",15,C["onSurface"]); y+=dp(56)
    tx(d,x+dp(22),y,"名前（例: 看護）",12,C["onSurfaceVariant"]); y+=dp(20)
    rr(d,x+dp(22),y,w-dp(44),dp(44),10,C["surface"],outline=C["outline"],ow=2); tx(d,x+dp(34),y+dp(12),"看護",15,C["onSurface"]); y+=dp(60)
    dlg_actions(d,x,y,w,"OK")

def c41s_dialog(d,x,y,w):
    tx(d,x+dp(22),y,"スキル",12,C["onSurfaceVariant"]); rr(d,x+dp(22),y+dp(18),w-dp(44),dp(40),10,C["surface"],outline=C["outline"],ow=2); tx(d,x+dp(34),y+dp(28),"N（看護）",14,C["onSurface"]); chev(d,x+w-dp(40),y+dp(38),C["onSurfaceVariant"],5); y+=dp(68)
    tx(d,x+dp(22),y,"シフト",12,C["onSurfaceVariant"]); rr(d,x+dp(22),y+dp(18),w-dp(44),dp(40),10,C["surface"],outline=C["outline"],ow=2); tx(d,x+dp(34),y+dp(28),"Dﾃ（夜勤）",14,C["onSurface"]); chev(d,x+w-dp(40),y+dp(38),C["onSurfaceVariant"],5); y+=dp(68)
    tx(d,x+dp(22),y,"下限(空=0) / 上限(空=無制限)",12,C["onSurfaceVariant"]); y+=dp(20)
    rr(d,x+dp(22),y,int((w-dp(52))/2),dp(40),10,C["surface"],outline=C["outline"],ow=2); tx(d,x+dp(34),y+dp(11),"1",14,C["onSurface"])
    rr(d,x+dp(30)+int((w-dp(52))/2),y,int((w-dp(52))/2),dp(40),10,C["surface"],outline=C["outline"],ow=2); tx(d,x+dp(42)+int((w-dp(52))/2),y+dp(11),"2",14,C["onSurface"]); y+=dp(52)
    dlg_actions(d,x,y,w,"追加")

D=_os.path.join(_REPO, "tools")
out=[]
for n,im in [("df_cur_ws1_apt",ws1_apt()),("df_cur_schedule",schedule_view()),("df_cur_analysis",analysis_view()),("df_cur_settings",settings_view()),
             ("df_cur_edit_year",edit_year()),("df_cur_skill_dialog",dialog("スキルグループ追加",skill_dialog,dp(290))),("df_cur_c41s_dialog",dialog("スキル別の回数を追加",c41s_dialog,dp(330)))]:
    p=f"{D}/{n}.png"; im.save(p); out.append(p)
for n,im in [("df_cur_home_amber",home("amber")),("df_cur_home_green",home("green")),("df_cur_home_running",home("running")),
             ("df_cur_guided_fix",dialog("なおすのを手伝います",guided_fix,dp(372))),
             ("df_cur_csv_choice",dialog("CSVの取り込み方法",csv_choice,dp(348))),
             ("df_cur_shift_picker",shift_picker())]:
    p=f"{D}/{n}.png"; im.save(p); out.append(p)
print("WROTE", *out)
