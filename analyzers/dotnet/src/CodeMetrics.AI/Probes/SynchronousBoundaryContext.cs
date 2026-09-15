using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace CodeMetrics.AI.Probes;

/// <summary>Recognizes synchronous contracts without transferring them into unrelated deferred work.</summary>
internal static class SynchronousBoundaryContext
{
    public static string? Reason(SyntaxNode node, SemanticModel model) =>
        Reason(node, model, new HashSet<ISymbol>(SymbolEqualityComparer.Default), 0);

    private static string? Reason(SyntaxNode node, SemanticModel model, HashSet<ISymbol> visiting, int depth)
    {
        if (depth > 4) return null;
        var owner = node.Ancestors().FirstOrDefault(candidate => PerformanceFindingContext.IsFunction(candidate) ||
            candidate is PropertyDeclarationSyntax or IndexerDeclarationSyntax);
        if (owner is AnonymousFunctionExpressionSyntax callback)
            return ScriptingCallbackContract.IsRecognized(callback, model) ? "synchronousScriptingContract" :
                SynchronousCallbackContract.IsRecognized(callback, model) ? "synchronousCallbackContract" : null;
        var symbol = owner switch
        {
            MethodDeclarationSyntax method => model.GetDeclaredSymbol(method),
            AccessorDeclarationSyntax accessor => model.GetDeclaredSymbol(accessor),
            PropertyDeclarationSyntax property => model.GetDeclaredSymbol(property)?.GetMethod,
            IndexerDeclarationSyntax indexer => model.GetDeclaredSymbol(indexer)?.GetMethod,
            _ => null
        };
        if (symbol == null || symbol.IsAsync || IsAwaitable(symbol.ReturnType)) return null;
        if (owner!.GetLeadingTrivia().Any(trivia => trivia.IsKind(SyntaxKind.SingleLineCommentTrivia) &&
            trivia.ToString().Contains("amp-metrics: sync-required", StringComparison.OrdinalIgnoreCase)))
            return "declaredSynchronousBoundary";
        if (IsContract(symbol)) return "synchronousContract";
        var tracked = symbol.AssociatedSymbol ?? (ISymbol)symbol;
        if (symbol.MethodKind is not (MethodKind.Ordinary or MethodKind.PropertyGet or MethodKind.PropertySet) ||
            tracked.DeclaredAccessibility != Accessibility.Private || !visiting.Add(tracked.OriginalDefinition)) return null;
        try
        {
            return AllUsesHaveContext(tracked, model, visiting, depth) ? "synchronousContractCallChain" : null;
        }
        finally
        {
            visiting.Remove(tracked.OriginalDefinition);
        }
    }

    private static bool IsContract(IMethodSymbol method)
    {
        ISymbol implemented = method.AssociatedSymbol ?? (ISymbol)method;
        return method.IsOverride || method.ExplicitInterfaceImplementations.Length > 0 ||
            method.ContainingType.AllInterfaces.SelectMany(type => type.GetMembers())
                .Any(contract => SymbolEqualityComparer.Default.Equals(
                    method.ContainingType.FindImplementationForInterfaceMember(contract), implemented));
    }

    internal static bool IsAwaitable(ITypeSymbol type) => TaskTypes.IsTaskLike(type) ||
        type.GetMembers("GetAwaiter").OfType<IMethodSymbol>().Any(method => !method.IsStatic && method.Parameters.Length == 0);

    private static bool AllUsesHaveContext(ISymbol method, SemanticModel model, HashSet<ISymbol> visiting, int depth)
    {
        // A private member's visible uses lie in its enclosing type (including partial and
        // nested declarations). Method groups must resolve to a known callback contract;
        // an arbitrary delegate escape or any async caller keeps the helper actionable.
        var scope = method.ContainingType;
        while (scope.ContainingType is { } outer) scope = outer;
        var found = false;
        foreach (var reference in scope.DeclaringSyntaxReferences)
        {
            var declaration = reference.GetSyntax();
            var callerModel = model.Compilation.GetSemanticModel(declaration.SyntaxTree);
            foreach (var name in declaration.DescendantNodes().OfType<SimpleNameSyntax>()
                .Where(name => name.Identifier.ValueText == method.Name))
            {
                // Dynamic/ambiguous calls cannot establish that all uses have been checked.
                if (callerModel.GetSymbolInfo(name).Symbol is not { } target) return false;
                if (!SymbolEqualityComparer.Default.Equals(target.OriginalDefinition, method.OriginalDefinition)) continue;
                found = true;
                var expression = name.Parent is MemberAccessExpressionSyntax member ? (ExpressionSyntax)member : name;
                if (target is IPropertySymbol && callerModel.GetOperation(expression) is IPropertyReferenceOperation)
                {
                    if (Reason(expression, callerModel, visiting, depth + 1) == null) return false;
                }
                else if (expression.Parent is InvocationExpressionSyntax invocation && invocation.Expression == expression)
                {
                    if (Reason(invocation, callerModel, visiting, depth + 1) == null) return false;
                }
                else if (!SynchronousCallbackContract.IsRecognized(expression, callerModel)) return false;
            }
        }
        return found;
    }
}
