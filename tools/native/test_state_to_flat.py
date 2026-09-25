#!/usr/bin/env python3
"""state_to_flat.py の休（meta の restIdx）・盤面の欠損セル・skillIdx の既定が Kotlin と同じかを見る。

実行: python3 tools/native/test_state_to_flat.py（native-parity.yml が変換の前に回す）
"""
import json
import os
import subprocess
import sys
import tempfile
import unittest

HERE = os.path.dirname(os.path.abspath(__file__))
CONVERTER = os.path.join(HERE, "state_to_flat.py")


def convert(shifts, schedule=None, staff=None):
    st = {
        "startDate": "2026-08-01", "endDate": "2026-08-02", "shifts": shifts,
        "groups": [{"name": "G", "kigou": "G"}], "staff": staff or [{"name": "s", "groupIdx": 0}],
        "groupShift": [[1] * len(shifts)], "schedule": schedule or [[0, 0]],
    }
    with tempfile.TemporaryDirectory() as d:
        src, out = os.path.join(d, "s.json"), os.path.join(d, "s.flat")
        with open(src, "w", encoding="utf-8") as f:
            json.dump(st, f, ensure_ascii=False)
        subprocess.run([sys.executable, CONVERTER, src, out], check=True, stdout=subprocess.DEVNULL)
        with open(out) as f:
            lines = f.read().split("\n")
    return [[int(x) for x in lines[i].split()] for i in range(2, len(lines) - 1, 2)]   # 長さ行を飛ばした各セクション


def rest_idx_of(shifts):
    return convert(shifts)[0][4]   # meta(S T K G restIdx ...)


def sh(kigou, role=None):
    s = {"name": kigou, "kigou": kigou, "need1": "", "need2": ""}
    if role is not None:
        s["role"] = role
    return s


class RestIdxTest(unittest.TestCase):
    def test_role_rest_wins_over_symbol(self):
        self.assertEqual(2, rest_idx_of([sh("休", "none"), sh("A", "none"), sh("公", "rest")]))

    def test_explicit_no_rest_is_minus_one(self):
        # NativeEval は restIdx ?: -1（旧: 記号"休"の index を渡していた）
        self.assertEqual(-1, rest_idx_of([sh("A", "none"), sh("休", "none")]))

    def test_no_rest_and_no_symbol_is_minus_one_not_zero(self):
        self.assertEqual(-1, rest_idx_of([sh("A"), sh("B")]))

    def test_legacy_and_blank_role_fall_back_to_symbol(self):
        self.assertEqual(1, rest_idx_of([sh("A"), sh("休")]))
        self.assertEqual(1, rest_idx_of([sh("A", ""), sh("休", "")]))


class BoardTest(unittest.TestCase):
    def test_missing_and_null_cells_are_minus_one(self):
        # MirrorCore.normalizeSchedule（3.475.0）: 欠損セルは -1（旧: 0＝先頭シフトの勤務）
        board = convert([sh("休"), sh("A")], schedule=[[1, None]])[-1]
        self.assertEqual([1, -1], board)


class SkillIdxTest(unittest.TestCase):
    def test_missing_skill_idx_is_unassigned(self):
        # StateParser の optInt("skillIdx", -1)（backlog #38）: キーの無い職員は未所属 -1（旧: 0＝先頭のスキルグループ）
        staff = [{"name": "a", "groupIdx": 0}, {"name": "b", "groupIdx": 0, "skillIdx": 0}]
        self.assertEqual([0, 0, -1, 0], convert([sh("休")], schedule=[[0, 0], [0, 0]], staff=staff)[1])   # sgrp + ssk


if __name__ == "__main__":
    unittest.main()
