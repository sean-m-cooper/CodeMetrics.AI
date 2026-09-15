using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeMetrics.AI.Probes;

/// <summary>Separates observed operations from conclusions that require design or runtime context.</summary>
internal static class PerformanceFindingContext
{
    public static Dictionary<string, object?> Review(SyntaxNode node, string reason)
    {
        var observations = SourceFindings.Location(node);
        observations["classification"] = "reviewLead";
        observations["classificationReason"] = reason;
        observations["scoreDisposition"] = "excludedReviewLead";
        return observations;
    }

    public static void CompleteMetadata(IEnumerable<Finding> findings)
    {
        foreach (var finding in findings)
        {
            if (finding.Observations.ContainsKey("classification")) continue;
            var review = finding.Severity == "info";
            finding.Observations["classification"] = review ? "reviewLead" : "actionableSignal";
            finding.Observations["classificationReason"] = review ? "contextRequired" : "observedHazard";
            finding.Observations["scoreDisposition"] = review ? "excludedReviewLead" : "scored";
        }
    }

    public static string? SyncReason(SyntaxNode node, SemanticModel model)
    {
        return SynchronousBoundaryContext.Reason(node, model) ??
            (LocalPerformanceRationale.HasExplanation(node) ? "documentedLocalChoice" : null) ??
            (LocalPerformanceRationale.HasTaskDeclarationExplanation(node, model) ? "documentedTaskLocalChoice" : null);
    }

    internal static bool IsFunction(SyntaxNode node) => node is
        BaseMethodDeclarationSyntax or AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax or AccessorDeclarationSyntax;

    public static string PersistenceReason(InvocationExpressionSyntax invocation, SemanticModel model)
    {
        var loop = invocation.Ancestors().TakeWhile(node => !IsFunction(node))
            .FirstOrDefault(node => node is ForStatementSyntax or ForEachStatementSyntax or WhileStatementSyntax or DoStatementSyntax);
        if (loop != null && invocation.Expression is MemberAccessExpressionSyntax member &&
            model.GetSymbolInfo(member.Expression).Symbol is ILocalSymbol local &&
            local.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is VariableDeclaratorSyntax declaration &&
            loop.Span.Contains(declaration.Span) && declaration.Parent?.Parent is LocalDeclarationStatementSyntax statement &&
            statement.UsingKeyword.IsKind(SyntaxKind.UsingKeyword))
            return "iterationScopedPersistenceLifetime";
        return "transactionAndBatchingContextRequired";
    }
}
