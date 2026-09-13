using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeMetrics.AI.Probes;

// Scoped to one syntax tree analysis. Recognition is shared by catch and type rules.
internal sealed class CatchObservation
{
    private readonly Lazy<SyntaxNode[]> activeNodes;
    private readonly Lazy<SyntaxNode[]> allNodes;
    private readonly Lazy<ISymbol?> caughtSymbol;
    private readonly Lazy<bool> handled;

    public CatchObservation(CatchClauseSyntax clause, SemanticModel semanticModel)
    {
        Clause = clause;
        SemanticModel = semanticModel;
        activeNodes = new(() => clause.Block.DescendantNodes(ShouldDescend).ToArray());
        allNodes = new(() => clause.Block.DescendantNodes().ToArray());
        caughtSymbol = new(() => clause.Declaration == null ? null : semanticModel.GetDeclaredSymbol(clause.Declaration));
        handled = new(() => CatchHandlingRecognition.IsHandled(this));
    }

    public CatchClauseSyntax Clause { get; }
    public SemanticModel SemanticModel { get; }
    public IReadOnlyList<SyntaxNode> ActiveNodes => activeNodes.Value;
    public IReadOnlyList<SyntaxNode> AllNodes => allNodes.Value;
    public ISymbol? CaughtSymbol => caughtSymbol.Value;
    public bool IsHandled => handled.Value;
    public bool RequiresLoggingSupport =>
        !ActiveNodes.Any(node => node is ReturnStatementSyntax or ContinueStatementSyntax or ThrowStatementSyntax) && !IsHandled;

    // Keep deferred bodies out of handling recognition. The legacy throw-ex and
    // default-return classifiers deliberately retain their unrestricted traversal.
    public static bool ShouldDescend(SyntaxNode node) =>
        node is not LocalFunctionStatementSyntax and not AnonymousFunctionExpressionSyntax;
}
