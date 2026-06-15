using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeMetrics.AI.Metrics;

public static class ClassCouplingCalculator
{
    public static int Calculate(TypeDeclarationSyntax typeDecl, SemanticModel model)
    {
        var selfSymbol = model.GetDeclaredSymbol(typeDecl) as INamedTypeSymbol;
        var collector = new ClassCouplingCollector(model, selfSymbol);
        collector.CollectFromType(typeDecl);
        return collector.Count;
    }
}
