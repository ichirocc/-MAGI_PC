package com.magi.app.ui

import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Add
import androidx.compose.material.icons.filled.Close
import androidx.compose.material.icons.filled.Warning
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.FilledTonalButton
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp

// [校正] テキストリンク状の操作を「用途別に強調を最適化したボタン」へ統一する共有部品。
//   追加=外枠＋＋アイコン / 編集=トーナル塗り / 削除=外枠(エラー) /
//   ダイアログ 確定=塗り・取消=外枠・破壊的=エラー塗り。
//   タッチ標的は Compose の最小インタラクティブサイズ(48dp)で担保。
private val CompactPad = PaddingValues(horizontal = 12.dp, vertical = 6.dp)

/** 一覧の「＋ ◯◯追加」: 追加と分かる外枠ボタン＋＋アイコン。 */
@Composable
fun AddRowButton(text: String, onClick: () -> Unit, enabled: Boolean = true, modifier: Modifier = Modifier) {
    OutlinedButton(onClick = onClick, enabled = enabled, modifier = modifier.heightIn(min = 48.dp)) {
        Icon(Icons.Filled.Add, contentDescription = null, modifier = Modifier.padding(end = 6.dp))
        Text(text)
    }
}

/** 一覧行の「編集」: トーナル塗りで控えめだが明確に押せる。 */
@Composable
fun EditRowButton(onClick: () -> Unit, enabled: Boolean = true, text: String = "編集") {
    FilledTonalButton(
        onClick = onClick, enabled = enabled,
        contentPadding = CompactPad, modifier = Modifier.heightIn(min = 48.dp),
    ) { Text(text) }
}

/** 一覧行の「削除」: エラー色の外枠で破壊的と分かる。 */
@Composable
fun DeleteRowButton(onClick: () -> Unit, enabled: Boolean = true, text: String = "削除") {
    OutlinedButton(
        onClick = onClick, enabled = enabled, contentPadding = CompactPad,
        modifier = Modifier.heightIn(min = 48.dp),
        colors = ButtonDefaults.outlinedButtonColors(contentColor = MaterialTheme.colorScheme.error),
    ) { Text(text) }
}

/** フォーム系ダイアログの統一ヘッダー: タイトル＋右上に閉じる(✕)。閉じる操作を画面上部にも置き発見性を上げる。 */
@Composable
fun DialogHeader(title: String, onClose: () -> Unit) {
    Row(verticalAlignment = Alignment.CenterVertically) {
        Text(title, style = MaterialTheme.typography.titleLarge, modifier = Modifier.weight(1f))
        IconButton(onClick = onClose) {
            Icon(Icons.Filled.Close, contentDescription = "閉じる")
        }
    }
}

/** ダイアログの確定（肯定の主操作）: 塗りボタン。一本指向けに最低48dp高。 */
@Composable
fun DialogConfirmButton(text: String, onClick: () -> Unit, enabled: Boolean = true) {
    Button(onClick = onClick, enabled = enabled, modifier = Modifier.heightIn(min = 48.dp)) { Text(text) }
}

/** ダイアログの取消/閉じる: 外枠ボタン。一本指向けに最低48dp高。 */
@Composable
fun DialogDismissButton(onClick: () -> Unit, text: String = "キャンセル") {
    OutlinedButton(onClick = onClick, modifier = Modifier.heightIn(min = 48.dp)) { Text(text) }
}

/** ダイアログの破壊的確定（削除など）: エラー色＋警告アイコンで誤タップを抑止。最低48dp高。 */
@Composable
fun DialogDangerButton(text: String, onClick: () -> Unit, enabled: Boolean = true) {
    Button(
        onClick = onClick, enabled = enabled,
        modifier = Modifier.heightIn(min = 48.dp),
        colors = ButtonDefaults.buttonColors(
            containerColor = MaterialTheme.colorScheme.error,
            contentColor = MaterialTheme.colorScheme.onError,
        ),
    ) {
        Icon(Icons.Filled.Warning, contentDescription = null, modifier = Modifier.size(18.dp))
        Spacer(Modifier.width(6.dp))
        Text(text)
    }
}

/**
 * 下限>上限 のときに出す一言。**同じ間違いは同じ言葉で示す**ため4つのダイアログ
 * （群のレンジ・スキル群のレンジ・個人別の回数・グループ単位の回数）で共有する。
 * 判定そのものは `V6SanityPort.rangeOrderConflict`（事後診断と同一ソース）。
 */
const val RANGE_ORDER_HINT = "下限は上限以下にしてください"

/**
 * [design-review] 上と同じ判定(`V6SanityPort.rangeOrderConflict`)を、必要人数(Shift.need1/need2)の
 * 3つの編集面（Ws1Editor のシフト追加/編集・NeedDayEditor の基本値/日別例外）へ広げたときの専用文言。
 * これらは全て「最低人数」「上限人数」という field label で統一されており、RANGE_ORDER_HINT の
 * 「下限」「上限」という語彙とは一致しない（そのまま使うと自分自身の項目名と矛盾する）ため、
 * 同じ判定・同じ見せ方（枠を赤くする＋直下に赤字）のまま文言だけ項目名に合わせて分けた。
 */
const val NEED_ORDER_HINT = "最低人数は上限人数以下にしてください"

/**
 * [実機報告「適用が押せない」] 下限・上限とも「なし」のまま確定しようとすると、`ok` 判定
 * （`lo.isNotBlank() || hi.isNotBlank()`）が偽のまま適用ボタンが無効になる。ボタンだけが灰色で
 * 理由が画面のどこにも無かった＝聞き返される形（3.396.0）。RANGE_ORDER_HINT と同じ「共有ヒント」
 * パターンで、個人別の回数・グループ単位の回数の2ダイアログが使う。
 */
const val RANGE_REQUIRED_HINT = "下限か上限のどちらかに数値を設定してください（＋で入力）"
