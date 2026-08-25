#!/usr/bin/env python3
"""日本の祝日データ生成（祝日法＝「国民の祝日に関する法律」の規則に基づく厳密計算）。

対象: 2026年〜2036年（今日=2026-08-25から少なくとも10年をカバーする暦年単位のレンジ。
      勤務表は月単位のスナップショットのため日精度でなく暦年で切る）。
出力: app/src/main/assets/japan_holidays.json （"YYYY-MM-DD" -> 祝日名 のフラットな辞書、日付昇順）。

祝日法の規則（すべて条文に基づく。特定年の日付をハードコードせず一般ロジックとして実装）:
  - 固定日（第2条）: 1/1 元日, 2/11 建国記念の日, 2/23 天皇誕生日(令和), 4/29 昭和の日,
    5/3 憲法記念日, 5/4 みどりの日, 5/5 こどもの日, 8/11 山の日, 11/3 文化の日, 11/23 勤労感謝の日
  - ハッピーマンデー（第2条）: 1月第2月曜=成人の日, 7月第3月曜=海の日, 9月第3月曜=敬老の日,
    10月第2月曜=スポーツの日
  - 春分の日/秋分の日（第2条）: 国立天文台が毎年2月に前年の官報で確定するため未来日は厳密には
    「確定前」だが、広く使われる近似式（1980〜2099年で有効）で計算する。
  - 国民の休日（第3条第3項）: 前日・翌日がともに祝日で、その日自体が祝日でない日は休日とする。
  - 振替休日（第3条第2項）: 祝日が日曜のとき、その後の直近の「祝日でない日」を休日とする
    （2007年改正により、祝日が連続していてもその先まで飛ばす。日付昇順で処理し、既に確定した
    振替休日も「祝日」として扱う）。

検算の根拠: 2026年9月22日(火)は「敬老の日(9/21・月)」と「秋分の日(9/23・水、上記近似式で算出)」に
挟まれた実在の「国民の休日」インスタンス（ユーザー提示のモックアップと同じ月内）。この実例が
main() の assert で毎回検証される＝この規則を年ごとの特殊ケースでなく一般ロジックとして実装した根拠。
"""
import json
import os as _os
from datetime import date, timedelta

_REPO = _os.path.dirname(_os.path.dirname(_os.path.abspath(__file__)))
OUT = _os.path.join(_REPO, "app/src/main/assets/japan_holidays.json")

START_YEAR = 2026
END_YEAR = 2036  # 2026-08-25 起点で10年後(2036-08-25)までを暦年単位で確実に包含


def nth_weekday(year: int, month: int, weekday: int, n: int) -> date:
    """month内でweekday(0=月..6=日, Pythonのdate.weekday()と同一)がn番目に現れる日を返す。"""
    d = date(year, month, 1)
    add = (weekday - d.weekday()) % 7
    return d + timedelta(days=add + 7 * (n - 1))


def vernal_equinox(year: int) -> int:
    """春分の日（3月）。国立天文台方式の近似式（1980〜2099年で有効）。"""
    return int(20.8431 + 0.242194 * (year - 1980) - int((year - 1980) / 4))


def autumnal_equinox(year: int) -> int:
    """秋分の日（9月）。同上の近似式。"""
    return int(23.2488 + 0.242194 * (year - 1980) - int((year - 1980) / 4))


def base_holidays(year: int) -> dict:
    """祝日法 第2条（固定日・ハッピーマンデー・春分秋分）のみ。振替/国民の休日はまだ含めない。"""
    h = {}
    h[date(year, 1, 1)] = "元日"
    h[nth_weekday(year, 1, 0, 2)] = "成人の日"
    h[date(year, 2, 11)] = "建国記念の日"
    h[date(year, 2, 23)] = "天皇誕生日"
    h[date(year, 3, vernal_equinox(year))] = "春分の日"
    h[date(year, 4, 29)] = "昭和の日"
    h[date(year, 5, 3)] = "憲法記念日"
    h[date(year, 5, 4)] = "みどりの日"
    h[date(year, 5, 5)] = "こどもの日"
    h[nth_weekday(year, 7, 0, 3)] = "海の日"
    h[date(year, 8, 11)] = "山の日"
    h[nth_weekday(year, 9, 0, 3)] = "敬老の日"
    h[date(year, 9, autumnal_equinox(year))] = "秋分の日"
    h[nth_weekday(year, 10, 0, 2)] = "スポーツの日"
    h[date(year, 11, 3)] = "文化の日"
    h[date(year, 11, 23)] = "勤労感謝の日"
    return h


def build(start_year: int, end_year: int) -> dict:
    holidays: dict = {}
    # 年境界（12/31の振替が翌年1/1に掛かる等）の判定に使う前後1年分もあわせて計算してから範囲で切る。
    for y in range(start_year - 1, end_year + 2):
        holidays.update(base_holidays(y))

    # 国民の休日（第3条第3項）: 前日・翌日が祝日で当日が祝日でない平日。
    additions = {}
    for d in list(holidays.keys()):
        mid = d + timedelta(days=1)
        if mid not in holidays and (mid - timedelta(days=1)) in holidays and (mid + timedelta(days=1)) in holidays:
            additions[mid] = "国民の休日"
    holidays.update(additions)

    # 振替休日（第3条第2項）: 祝日が日曜なら、その後の最初の「祝日でない日」を休日にする（日付昇順で処理）。
    for d in sorted(holidays.keys()):
        if d.weekday() == 6:  # 日曜（date.weekday(): 月=0..日=6）
            sub = d + timedelta(days=1)
            while sub in holidays:
                sub += timedelta(days=1)
            holidays[sub] = "振替休日"

    return {
        dt.isoformat(): name
        for dt, name in sorted(holidays.items())
        if start_year <= dt.year <= end_year
    }


def main() -> None:
    data = build(START_YEAR, END_YEAR)
    _os.makedirs(_os.path.dirname(OUT), exist_ok=True)
    with open(OUT, "w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False, indent=2)
        f.write("\n")
    print(f"wrote {len(data)} holidays ({START_YEAR}-{END_YEAR}) to {OUT}")
    # 検算: 2026年9月の国民の休日インスタンス（ユーザー提示のモックアップと同一月）を明示確認。
    assert data.get("2026-09-21") == "敬老の日", data.get("2026-09-21")
    assert data.get("2026-09-22") == "国民の休日", data.get("2026-09-22")
    assert data.get("2026-09-23") == "秋分の日", data.get("2026-09-23")
    print("verified: 2026-09-22 is 国民の休日 (敬老の日 9/21 と 秋分の日 9/23 に挟まれた実例)")


if __name__ == "__main__":
    main()
