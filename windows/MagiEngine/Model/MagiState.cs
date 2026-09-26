using System.Text.Json;

namespace MagiEngine.Model;

/// <summary>
/// MAGI shift-scheduling problem state.
///
/// Field names and semantics mirror the Kotlin/Android app's <c>MagiState</c> exactly
/// (which itself mirrors the Web app's <c>state</c> object), so JSON exported from the
/// Android version round-trips through this C# port without conversion.
///
///  - Shifts[k]      : a shift type. Need1/Need2 = default per-day required count
///                      for pattern P1/P2 ("" = no requirement).
///  - Groups[g]       : a staff group (skill class). Kigou = symbol used in constraints.
///  - StaffList[i]    : a person; GroupIdx points into Groups.
///  - GroupShift[g]   : per-group 0/1 mask of which shifts the group may take.
///  - GroupShiftApt[g][k] : V6 "適切回数" target for group×shift (blank = unset).
///  - Use2Patterns    : whether the P2 coverage generation is active (MIN=OR with P1).
///  - Schedule[i][j]  : initial assignment = shift index for staff i on day j.
///  - Wishes["i,j"]   : desired shift index for a cell (hard-ish preference).
///  - StaffRange["i,k"] = {Lo,Hi} : per-staff per-shift count range (LimMin/LimMax).
///  - NeedDay1/NeedDay2["k,j"]    : per-day need override for shift k on day j.
///  - Cons1..Cons42s  : the constraint families (see Problem / Evaluator, phases 2-3).
/// </summary>
/// <summary>[backlog#24] シフトの特別な役割。休みの識別を記号"休"の字面一致から切り離すために導入（Kotlin 3.603.0 同期）。</summary>
public enum ShiftRole { None, Rest }

public sealed record Shift(string Name, string Kigou, string Need1, string Need2, ShiftRole Role = ShiftRole.None);

public sealed record Group(string Name, string Kigou);

/// <summary>
/// StaffList[i]: GroupIdx -&gt; ユニットグループ(既存・担当可否/covU)、
/// SkillIdx -&gt; スキルグループ(新設・新C41s/C42s専用)。
/// SkillIdx の既定は -1＝未所属（backlog#38。0 は先頭のスキルグループ）。
/// </summary>
public sealed record Staff(string Name, int GroupIdx, int SkillIdx = -1);

public sealed record Range(string Lo, string Hi);

// Raw constraint rows (as authored), resolved later into index form (phase 2, Problem).
public sealed record C1Row(string Day1, string ShiftKigou, string Day2);
public sealed record C2Row(string ShiftKigou, string Count);
public sealed record C3Row(IReadOnlyList<string> Pattern);
public sealed record C41Row(string GroupKigou, string ShiftKigou, string L, string U);
public sealed record C42Row(string G1Kigou, string G2Kigou, string S1Kigou, string S2Kigou);

/// <summary>[3.542.0] 希望(ws3)で固定した WishKigou の前日に PrevKigou を置けない。素の連続禁止は Cons3n。</summary>
public sealed record C3wRow(string WishKigou, string PrevKigou);
/// <summary>[#41] 手動固定 1 件＝職員 Staff の Day 日目を Shift に固定（最適化器は書き換えない。手の編集は可で、値はそれに追従する）。</summary>
public sealed record ManualPin(int Staff, int Day, int Shift);

/// <summary>
/// Immutable snapshot of the full scheduling problem + current draft schedule.
///
/// NOTE on record equality: this is a C# <c>record</c> for cheap <c>with</c>-expression
/// copies (mirrors Kotlin's pervasive <c>.copy(...)</c> usage), but its list/dictionary-typed
/// properties do NOT get deep structural equality from the compiler-generated
/// Equals/GetHashCode (<see cref="List{T}"/> and <see cref="Dictionary{TKey,TValue}"/> use
/// reference equality). Do not rely on <c>state1 == state2</c> for content comparison —
/// fixture round-trip tests compare field-by-field instead.
/// </summary>
public sealed record MagiState(
    string StartDate,
    string EndDate,
    IReadOnlyList<Shift> Shifts,
    IReadOnlyList<Group> Groups,
    IReadOnlyList<Staff> StaffList,
    bool Use2Patterns,
    IReadOnlyList<IReadOnlyList<int>> GroupShift,
    IReadOnlyList<IReadOnlyList<string>> GroupShiftApt,
    IReadOnlyList<IReadOnlyList<int>> Schedule,
    IReadOnlyDictionary<string, int> Wishes,
    IReadOnlyDictionary<string, Range> StaffRange,
    IReadOnlyDictionary<string, string> NeedDay1,
    IReadOnlyDictionary<string, string> NeedDay2,
    IReadOnlyList<C1Row> Cons1,
    IReadOnlyList<C2Row> Cons2,
    IReadOnlyList<C3Row> Cons3,
    IReadOnlyList<C3Row> Cons3n,
    IReadOnlyList<C3Row> Cons3m,
    IReadOnlyList<C3Row> Cons3mn,
    IReadOnlyList<C41Row> Cons41,
    IReadOnlyList<C42Row> Cons42,
    /// <summary>[スキルグループ新設] ユニットとは別の第2分類。担当可否には使わず、下のCons41s/Cons42sだけが参照。</summary>
    IReadOnlyList<Group> SkillGroups,
    /// <summary>スキルグループの C41 相当: スキル群 X のシフト Y を1日に [l,u] 回（既存C41のスキル版）。</summary>
    IReadOnlyList<C41Row> Cons41s,
    /// <summary>スキルグループの C42 相当: スキル群 g1 の s1 と スキル群 g2 の s2 が同日に併存不可（既存C42のスキル版）。</summary>
    IReadOnlyList<C42Row> Cons42s,
    /// <summary>Per-shift display colour overrides, keyed by shift kigou -&gt; "#rrggbb". Display only (no engine effect).</summary>
    IReadOnlyDictionary<string, string> ShiftColors,
    /// <summary>Anything we do not model yet, kept verbatim (cloned JsonElements) so export round-trips losslessly.</summary>
    IReadOnlyDictionary<string, JsonElement> Extras,
    /// <summary>[3.542.0] 希望の前日に禁止（HARD、c3n と同格）。既存 JSON にキーが無ければ null＝空として読む
    /// （既存の22箇所の <c>new MagiState(...)</c> 呼出元を変えずに済むよう既定値つきの末尾パラメータにする）。</summary>
    IReadOnlyList<C3wRow>? Cons3w = null,
    /// <summary>[#41] 手動固定（1 セル 1 件）。null＝空。採点・希望の意味は変えない。</summary>
    IReadOnlyList<ManualPin>? ManualPins = null
)
{
    public int StaffCount => StaffList.Count;
    public int DayCount => Schedule.Count > 0 ? Schedule[0].Count : 0;
    public int ShiftCount => Shifts.Count;
    public int GroupCount => Groups.Count;
    public int SkillGroupCount => SkillGroups.Count;
}
