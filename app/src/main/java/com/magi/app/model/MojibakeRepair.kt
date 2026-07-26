package com.magi.app.model

/**
 * 二重エンコード文字化け（UTF-8 のバイト列を ISO-8859-1(Latin-1) として読み、再び UTF-8 で保存した
 * いわゆる「å¤æ³…」型）の自動修復。
 * [3.282.0/監査で訂正] 旧コメントは CP1252 型も対象と主張していたが、CP1252 誤読は 0x80-0x9F 帯が
 * ƒ''Œ 等の U+00FF 超文字になり下の「本物の多バイト文字」ガードで必ず弾かれる＝構造的に修復対象外
 * （安全側に不修復で返るだけで破壊はしない）。CP932 型（「縺」パターン）も同様に対象外。
 *
 * MAGI 自身の読み書きは UTF-8 固定なので化けは作らないが、外部ツールや旧 Web 書き出しで
 * 二重エンコードされた JSON/CSV を読み込んだ場合に、表示が文字化けする。これを安全に元へ戻す。
 *
 * 安全策（誤変換を避ける）:
 *  - 既に正しい多バイト文字(U+00FF 超＝本来の日本語)が含まれる → 化けではない → そのまま。
 *  - 0x80..0xFF の拡張ラテン文字が無い(ASCII のみ) → 変換不要 → そのまま。
 *  - 変換結果に U+FFFD(置換文字)が増える(＝不正な UTF-8 並び＝本物の Latin-1) → 失敗とみなし元を返す。
 *  これらにより、二重エンコード UTF-8 のときだけ復元され、本物の Latin-1 テキストは保護される。
 */
object MojibakeRepair {
    fun repair(s: String): String {
        if (s.isEmpty()) return s
        // 先頭 UTF-8 BOM(U+FEFF) は常に除去（trim()でも消えず、ヘッダ判定や contains を壊すため）。
        val t = if (s[0] == '\uFEFF') s.substring(1) else s
        if (t.any { it.code > 0xFF }) return t                 // 本物の日本語等が既にある
        if (t.none { it.code in 0x80..0xFF }) return t         // ASCII のみ
        return try {
            val decoded = String(t.toByteArray(Charsets.ISO_8859_1), Charsets.UTF_8)
            // [3.282.0] 旧: `after <= before` だったが、U+FFFD を含む入力は上の >0xFF ガードで既に return 済み
            //   ＝before は常に0で実質 `after == 0`。実態どおりに単純化（全バイト列が正しい UTF-8 として
            //   復元できた場合のみ採用＝1バイトでも不正なら全体を諦める all-or-nothing、部分修復は起きない）。
            val after = decoded.count { it == '�' }
            if (after == 0 && decoded.any { it.code > 0xFF }) decoded else t
        } catch (_: Exception) {
            t
        }
    }

    /** 修復が必要(=変化する)かの判定。ログ表示用。 */
    fun looksMojibake(s: String): Boolean = repair(s) != s

    /**
     * [3.282.0] 「本当に二重エンコードを復号したか」の判定。repair() は BOM 除去だけでも新しい String を
     * 返すため、呼出側の参照比較(`!==`)では「健全な BOM付きUTF-8 ファイル」でも毎回
     * 『文字化けを自動修復』という誤った警告が出ていた（BOM は Windows 系エディタ由来で正常なファイル）。
     * BOM を除いた本文が変化したときだけ true。
     */
    fun wasDecoded(original: String, repaired: String): Boolean =
        repaired != original.removePrefix("\uFEFF")
}
