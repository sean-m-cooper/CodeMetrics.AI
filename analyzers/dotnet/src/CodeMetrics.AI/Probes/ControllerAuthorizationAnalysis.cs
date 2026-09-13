using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeMetrics.AI.Probes;

internal static class ControllerAuthorizationAnalysis
{
    internal sealed record Controller(INamedTypeSymbol Symbol, ClassDeclarationSyntax Declaration);
    internal sealed record MissingIntent(Controller Controller, int UnannotatedActionCount);
    private sealed record ProjectControllers(bool UsesAuthorize, IReadOnlyList<Controller> Controllers);

    internal static IEnumerable<MissingIntent> FindMissingIntent(Compilation compilation, string? solutionDir)
    {
        var project = CollectProject(compilation, solutionDir);
        if (!project.UsesAuthorize)
            yield break;

        foreach (var controller in project.Controllers)
        {
            if (CountUnannotatedActions(controller.Symbol) is { } count)
                yield return new MissingIntent(controller, count);
        }
    }

    private static ProjectControllers CollectProject(Compilation compilation, string? solutionDir)
    {
        var controllers = new Dictionary<INamedTypeSymbol, Controller>(SymbolEqualityComparer.Default);
        var usesAuthorize = false;
        foreach (var tree in SourceFileFilter.AnalyzableTrees(compilation, solutionDir))
        {
            var model = compilation.GetSemanticModel(tree);
            foreach (var member in tree.GetRoot().DescendantNodes().OfType<MemberDeclarationSyntax>())
            {
                var symbol = model.GetDeclaredSymbol(member);
                usesAuthorize |= symbol != null && HasAttribute(symbol, "Authorize");
                if (member is ClassDeclarationSyntax declaration &&
                    declaration.Identifier.Text.EndsWith("Controller", StringComparison.Ordinal) &&
                    symbol is INamedTypeSymbol controllerSymbol)
                {
                    // Keep one symbol and its first included declaration, in source traversal order.
                    // Symbol attributes and members still aggregate all partial declarations.
                    controllers.TryAdd(controllerSymbol, new Controller(controllerSymbol, declaration));
                }
            }
        }

        return new ProjectControllers(usesAuthorize, controllers.Values.ToArray());
    }

    // Null means explicit intent was found. Zero preserves the existing review finding
    // for controllers with no eligible actions when the project uses authorization.
    private static int? CountUnannotatedActions(INamedTypeSymbol controller)
    {
        if (HasAttributeInTypeHierarchy(controller, "Authorize") ||
            HasAttributeInTypeHierarchy(controller, "AllowAnonymous"))
            return null;

        var actionCount = 0;
        var unannotatedCount = 0;
        foreach (var action in controller.GetMembers().OfType<IMethodSymbol>().Where(IsControllerAction))
        {
            actionCount++;
            if (!HasAttribute(action, "Authorize") && !HasAttribute(action, "AllowAnonymous"))
                unannotatedCount++;
        }

        return actionCount > 0 && unannotatedCount == 0 ? null : unannotatedCount;
    }

    private static bool HasAttribute(ISymbol symbol, string attributeName)
    {
        return symbol.GetAttributes().Any(attribute =>
        {
            var name = attribute.AttributeClass?.Name;
            return name == attributeName || name == attributeName + "Attribute";
        });
    }

    private static bool HasAttributeInTypeHierarchy(INamedTypeSymbol type, string attributeName)
    {
        for (var current = type; current != null; current = current.BaseType)
        {
            if (HasAttribute(current, attributeName))
                return true;
        }

        return false;
    }

    private static bool IsControllerAction(IMethodSymbol method)
    {
        return method.MethodKind == MethodKind.Ordinary &&
               method.DeclaredAccessibility == Accessibility.Public &&
               !method.IsStatic &&
               !method.IsImplicitlyDeclared &&
               !HasAttribute(method, "NonAction");
    }
}
