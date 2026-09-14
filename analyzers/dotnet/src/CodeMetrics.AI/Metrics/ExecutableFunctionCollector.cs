using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeMetrics.AI.Metrics;

internal static class ExecutableFunctionCollector
{
    public static ExecutableTypeMetrics Collect(
        IEnumerable<(TypeDeclarationSyntax Declaration, SemanticModel Model)> declarations)
    {
        var complexityFunctions = new List<ExecutableFunctionMetrics>();
        var maintainabilityFunctions = new List<ExecutableFunctionMetrics>();
        foreach (var (declaration, model) in declarations)
            foreach (var function in CollectFunctions(declaration, model))
            {
                // Each population retains its own eligibility and evidence contract.
                if (function.Kind != "initializer" || function.OwnCyclomaticComplexity > 1)
                    complexityFunctions.Add(function with { Maintainability = null });
                if (function.Maintainability != null)
                    maintainabilityFunctions.Add(function);
            }
        return new(complexityFunctions.ToArray()) { MaintainabilityFunctions = maintainabilityFunctions.ToArray() };
    }

    private static IEnumerable<ExecutableFunctionMetrics> CollectFunctions(TypeDeclarationSyntax declaration, SemanticModel model)
    {
        foreach (var node in declaration.DescendantNodes(node => node == declaration || node is not TypeDeclarationSyntax))
        {
            var kind = FunctionKind(node);
            if (kind == null) continue;

            yield return MeasureFunction(node, model, kind);
        }
    }

    private static ExecutableFunctionMetrics MeasureFunction(SyntaxNode node, SemanticModel model, string kind)
    {
        var walker = new CyclomaticComplexityWalker(includeNestedFunctions: false);
        walker.Visit(node);
        var name = node is AnonymousFunctionExpressionSyntax ? "callback"
            : model.GetDeclaredSymbol(node)?.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat) ?? kind;
        var start = FunctionStart(node);
        return new(name, kind, node.SyntaxTree.FilePath,
            node.SyntaxTree.GetLineSpan(new(start, 0)).StartLinePosition.Line + 1, walker.Complexity)
        {
            SourceSpanStart = start,
            SourceSpanLength = node.Span.End - start,
            Maintainability = MeasureMaintainability(node, model, kind, walker.Complexity)
        };
    }

    private static FunctionMaintainabilityMetrics? MeasureMaintainability(
        SyntaxNode node, SemanticModel model, string kind, int complexity)
    {
        if (node is EqualsValueClauseSyntax initializer && model.GetConstantValue(initializer.Value).HasValue)
            return null;
        var measurement = FunctionMaintainabilityCalculator.Measure(node, complexity);
        // A lambda-only initializer has no separately owned body; its callback counts.
        return kind == "initializer" && measurement.SourceLines == 0 ? null : measurement;
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
