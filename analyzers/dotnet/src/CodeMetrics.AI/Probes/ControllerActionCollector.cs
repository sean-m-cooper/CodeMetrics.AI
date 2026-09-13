using CodeMetrics.AI.Metrics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeMetrics.AI.Probes;

internal static class ControllerActionCollector
{
    public static void Collect(SyntaxNode root, SemanticModel model, string projectName,
        List<ControllerActionObservation> observations)
    {
        foreach (var declaration in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
        {
            if (model.GetDeclaredSymbol(declaration) is not INamedTypeSymbol controller ||
                !WebTypeClassifier.IsController(controller))
                continue;

            var dependencies = ConstructorDependencyCollector.Collect(declaration, model)
                .Select(parameter => parameter.TypeSymbol).OfType<INamedTypeSymbol>()
                .Select(type => type.OriginalDefinition.ToDisplayString())
                .Distinct(StringComparer.Ordinal).ToList();
            foreach (var (syntax, symbol) in Actions(declaration, model))
                observations.Add(Observe(projectName, controller, syntax, symbol, model, dependencies));
        }
    }

    private static IEnumerable<(MethodDeclarationSyntax Syntax, IMethodSymbol Symbol)> Actions(
        ClassDeclarationSyntax declaration, SemanticModel model)
    {
        // Inspect authored members of this declaration, retaining partial and source order.
        foreach (var action in declaration.Members.OfType<MethodDeclarationSyntax>())
        {
            if (model.GetDeclaredSymbol(action) is not IMethodSymbol symbol ||
                symbol.MethodKind != MethodKind.Ordinary ||
                symbol.DeclaredAccessibility != Accessibility.Public || symbol.IsStatic ||
                HasAttribute(symbol, "NonActionAttribute"))
                continue;
            yield return (action, symbol);
        }
    }

    private static ControllerActionObservation Observe(string projectName, INamedTypeSymbol controller,
        MethodDeclarationSyntax action, IMethodSymbol symbol, SemanticModel model, List<string> dependencies)
    {
        return new ControllerActionObservation(
            projectName, controller.ContainingNamespace?.ToDisplayString() ?? "", controller.Name,
            symbol.Name, action.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
            ClassCouplingCalculator.CalculateAction(action, model),
            ClassCouplingCalculator.CalculateStructuralAction(action, model),
            symbol.Parameters.Count(parameter => HasAttribute(parameter, "FromServicesAttribute")), dependencies);
    }

    private static bool HasAttribute(ISymbol symbol, string name)
    {
        return symbol.GetAttributes().Any(attribute => attribute.AttributeClass?.Name == name);
    }
}
