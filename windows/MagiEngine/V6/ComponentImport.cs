using MagiEngine.Model;

namespace MagiEngine.V6;

/// <summary>
/// [フェーズ7ピース11] Kotlin原本 <c>ComponentImport</c>（<c>ScheduleCsvBridge.kt</c> 530〜536行）の移植。
///
/// コンポーネント別CSV取込（<see cref="WishesCsvIO"/>/<see cref="ConstraintsCsvIO"/>）の結果。
///
/// [3.329.0/外部レビュー H-02 移植元] これらの取込は**既存を全置換**する（希望なら
/// <see cref="MagiState.Wishes"/> を丸ごと差し替える）。旧実装は未知の氏名・記号・日付の行を
/// 黙って捨て、1行でも有効なら置換を実行していた。つまり「80行のうち79行が誤記のCSV」を読ませると、
/// **残り79件の希望が消える**。中身が空でない行を1つでも解釈できなかったら、呼び出し側が置換を
/// 中止できるように件数を返す。
/// </summary>
/// <param name="State">更新後の状態（全置換済み）。</param>
/// <param name="Accepted">取り込めた件数。</param>
/// <param name="Rejected">解釈できなかった件数。</param>
/// <param name="Sample">解釈できなかった最初の行（利用者へどこが悪いか示すため）。</param>
public sealed record ComponentImport(MagiState State, int Accepted, int Rejected, string Sample);
