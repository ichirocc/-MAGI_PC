using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace MagiEngine.Analyzers;

/// <summary>
/// Roslyn 診断アナライザー: <c>int[][]</c> の浅いコピー（単純代入）を検出する。
///
/// <para>スケジュールデータ（<c>int[][]</c>）を <see cref="ScheduleUtil.Copy2D"/> ではなく
/// 単純代入で複製すると、行（<c>int[]</c>）の参照が共有されたままになり、探索ループ内で
/// 意図せぬデータ共有が発生して最適化結果が破壊されるバグの原因になる。</para>
///
/// <para><b>「安全」とみなすメソッド呼出しは <see cref="ScheduleUtil.Copy2D"/> と
/// <see cref="ScheduleUtil.ToIntArray2D"/>（ここでは短名 "ToIntArray2D" のみ判定）だけに限定する。
/// <c>Array.Clone()</c>・<c>Enumerable.ToArray()</c> は <c>int[][]</c> に対しては浅いコピー
/// （外側の配列だけを複製し内側の <c>int[]</c> 行は共有する）であり、このアナライザが検出すべき
/// バグそのものを「安全」と誤判定してしまうため、意図的に安全リストから除外している。</b></para>
///
/// <para>ルール: <c>int[][]</c> 型の変数への代入式で、右辺が同じ <c>int[][]</c> 型の識別子・
/// メンバアクセス単体（メソッド呼出しの直接の戻り値でない）の場合に警告する。</para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ArrayCopyAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "MAGI001";
    private const string Category = "Reliability";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        title: "int[][] の浅いコピーの可能性",
        messageFormat: "'{0}' への代入が浅いコピーの可能性があります。ScheduleUtil.Copy2D() を使用してください",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "int[][] (jagged array) の単純代入は内側の行データ(int[])を共有します。探索ループ内で" +
            "意図せぬデータ共有を防ぐため、ScheduleUtil.Copy2D() でディープコピーしてください。" +
            "Array.Clone()/Enumerable.ToArray() は int[][] に対しては浅いコピーのため安全とはみなしません。");

    // 深いコピーを保証すると判定するメソッド名（短名一致）。
    private static readonly ImmutableHashSet<string> SafeMethods = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "Copy2D", "ToIntArray2D");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterSyntaxNodeAction(AnalyzeAssignment, SyntaxKind.SimpleAssignmentExpression);
        // ローカル変数の初期化子（`int[][] x = y;`）も同じ規則で見る。
        context.RegisterSyntaxNodeAction(AnalyzeLocalDeclaration, SyntaxKind.VariableDeclarator);
    }

    private static void AnalyzeAssignment(SyntaxNodeAnalysisContext context)
    {
        var assignment = (AssignmentExpressionSyntax)context.Node;
        CheckRhs(context, assignment.Left, assignment.Right);
    }

    private static void AnalyzeLocalDeclaration(SyntaxNodeAnalysisContext context)
    {
        var declarator = (VariableDeclaratorSyntax)context.Node;
        var init = declarator.Initializer?.Value;
        if (init is null) return;
        // 型は左辺の宣言型シンボルを直接引く（IdentifierNameSyntax を左辺として渡せないため専用経路）。
        var symbol = context.SemanticModel.GetDeclaredSymbol(declarator) as ILocalSymbol;
        if (symbol is null || !IsJaggedIntArray(symbol.Type)) return;
        CheckRhsExpr(context, init, symbol.Type);
    }

    private static void CheckRhs(SyntaxNodeAnalysisContext context, ExpressionSyntax left, ExpressionSyntax right)
    {
        var leftType = context.SemanticModel.GetTypeInfo(left).Type;
        if (leftType is null || !IsJaggedIntArray(leftType)) return;
        CheckRhsExpr(context, right, leftType);
    }

    private static void CheckRhsExpr(SyntaxNodeAnalysisContext context, ExpressionSyntax right, ITypeSymbol leftType)
    {
        // 右辺が識別子 or メンバアクセス（変数単体）以外は対象外（`new int[n][]`, `Array.Empty<int[]>()`,
        // 三項演算子等はここでは扱わない＝過検出を避けるため意図的に狭いスコープに留める）。
        bool isSimpleRef = right is IdentifierNameSyntax
            || right is MemberAccessExpressionSyntax { Name: IdentifierNameSyntax };
        if (!isSimpleRef) return;

        // 右辺そのものがメソッド呼出しの戻り値（`x.Copy2D()` の `x` 部分）ではないことを確認。
        if (right.Parent is InvocationExpressionSyntax) return;

        // 右辺の式が `<安全なメソッド>()` の呼出し結果であれば安全（例: `var a = b.Copy2D();`）。
        if (IsResultOfSafeCall(right)) return;

        var rightType = context.SemanticModel.GetTypeInfo(right).Type;
        if (rightType is null || !IsJaggedIntArray(rightType)) return;

        context.ReportDiagnostic(Diagnostic.Create(Rule, right.GetLocation(), right.ToString()));
    }

    private static bool IsJaggedIntArray(ITypeSymbol type) =>
        type is IArrayTypeSymbol
        {
            Rank: 1,
            ElementType: IArrayTypeSymbol { Rank: 1, ElementType.SpecialType: SpecialType.System_Int32 },
        };

    /// <summary>右辺の式を包む直近の呼出し式が、安全メソッド一覧のいずれかの呼出しであるか。</summary>
    private static bool IsResultOfSafeCall(ExpressionSyntax expr)
    {
        for (var parent = expr.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent is InvocationExpressionSyntax inv)
            {
                var name = inv.Expression switch
                {
                    IdentifierNameSyntax id => id.Identifier.Text,
                    MemberAccessExpressionSyntax ma => ma.Name.Identifier.Text,
                    _ => null,
                };
                if (name is not null && SafeMethods.Contains(name)) return true;
            }
            // 括弧や単純なキャストの中を透過的に辿る以外は打ち切り（誤検出源を広げない）。
            if (parent is not (ParenthesizedExpressionSyntax or CastExpressionSyntax)) break;
        }
        return false;
    }
}
