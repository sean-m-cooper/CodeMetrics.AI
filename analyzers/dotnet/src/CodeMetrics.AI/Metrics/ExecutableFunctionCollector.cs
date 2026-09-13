using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeMetrics.AI.Metrics;

internal static class ExecutableFunctionCollector
{
    public static IEnumerable<ExecutableFunctionMetrics> Collect(TypeDeclarationSyntax declaration, SemanticModel model,
        bool forMaintainability = false)
    {
        foreach (var node in declaration.DescendantNodes(node => node == declaration || node is not TypeDeclarationSyntax))
        {
            var kind = FunctionKind(node);
            if (kind == null) continue;

            var walker = new CyclomaticComplexityWalker(includeNestedFunctions: false);
            walker.Visit(node);
            // Storage initialization with no decisions is not executable decomposition.
            if (!forMaintainability && kind == "initializer" && walker.Complexity == 1) continue;
            if (forMaintainability && node is EqualsValueClauseSyntax initializer &&
                model.GetConstantValue(initializer.Value).HasValue) continue;
            var maintainability = forMaintainability ? FunctionMaintainabilityCalculator.Measure(node, walker.Complexity) : null;
            // An initializer consisting solely of a nested lambda has no separately owned body.
            if (forMaintainability && kind == "initializer" && maintainability!.SourceLines == 0) continue;
            var name = node is AnonymousFunctionExpressionSyntax ? "callback"
                : model.GetDeclaredSymbol(node)?.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat) ?? kind;
            var start = FunctionStart(node);
            yield return new ExecutableFunctionMetrics(name, kind, node.SyntaxTree.FilePath,
                node.SyntaxTree.GetLineSpan(new(start, 0)).StartLinePosition.Line + 1, walker.Complexity)
            {
                SourceSpanStart = start,
                SourceSpanLength = node.Span.End - start,
                Maintainability = maintainability
            };
        }
    }

    // Anchor identity at the authored name/defining token, not attributes or a
    // return type whose inclusion can differ between target-framework parses.
    private static int FunctionStart(SyntaxNode node) => node switch
    {
        MethodDeclarationSyntax method => method.Identifier.SpanStart,
        ConstructorDeclarationSyntax constructor => constructor.Identifier.SpanStart,
        DestructorDeclarationSyntax destructor => destructor.Identifier.SpanStart,
        OperatorDeclarationSyntax op => op.OperatorToken.SpanStart,
        ConversionOperatorDeclarationSyntax conversion => conversion.ImplicitOrExplicitKeyword.SpanStart,
        LocalFunctionStatementSyntax local => local.Identifier.SpanStart,
        AccessorDeclarationSyntax accessor => accessor.Keyword.SpanStart,
        PropertyDeclarationSyntax property => property.Identifier.SpanStart,
        IndexerDeclarationSyntax indexer => indexer.ThisKeyword.SpanStart,
        LambdaExpressionSyntax lambda => lambda.ArrowToken.SpanStart,
        AnonymousMethodExpressionSyntax anonymous => anonymous.DelegateKeyword.SpanStart,
        EqualsValueClauseSyntax initializer => initializer.EqualsToken.SpanStart,
        _ => node.SpanStart
    };

    private static string? FunctionKind(SyntaxNode node) => node switch
    {
        BaseMethodDeclarationSyntax method when method.Body != null || method.ExpressionBody != null => "method",
        LocalFunctionStatementSyntax local when local.Body != null || local.ExpressionBody != null => "localFunction",
        AnonymousFunctionExpressionSyntax => "callback",
        AccessorDeclarationSyntax accessor when accessor.Body != null || accessor.ExpressionBody != null => "accessor",
        PropertyDeclarationSyntax { ExpressionBody: not null } => "property",
        IndexerDeclarationSyntax { ExpressionBody: not null } => "indexer",
        EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax { Parent.Parent: FieldDeclarationSyntax } } => "initializer",
        EqualsValueClauseSyntax { Parent: PropertyDeclarationSyntax } => "initializer",
        _ => null
    };
}
