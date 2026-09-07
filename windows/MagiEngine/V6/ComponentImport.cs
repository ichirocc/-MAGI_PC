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
/// <param name="Samples">
/// 解釈できなかった行の例（最大 <see cref="MaxSamples"/> 件、利用者へどこが悪いか示すため）。
/// [Android 3.474.0 同期/外部レビュー#76] 旧は最初の1行しか保持せず、複数種類の原因が混在する CSV では
/// 1回の取込結果から1つしか原因が分からず、修正のたびに再アップロードが必要だった。
/// </param>
public sealed record ComponentImport
{
    public MagiState State { get; }
    public int Accepted { get; }
    public int Rejected { get; }
    public IReadOnlyList<string> Samples { get; }
    /// <summary>先頭の例（旧 API 互換。無ければ空文字）。</summary>
    public string Sample => Samples.Count > 0 ? Samples[0] : "";
    public const int MaxSamples = 3;

    public ComponentImport(MagiState state, int accepted, int rejected, IReadOnlyList<string> samples)
    {
        State = state; Accepted = accepted; Rejected = rejected;
        // 上限は構築時にここで保証する（呼出側の Take() に頼らない）。
        Samples = samples.Take(MaxSamples).ToList();
    }
}
