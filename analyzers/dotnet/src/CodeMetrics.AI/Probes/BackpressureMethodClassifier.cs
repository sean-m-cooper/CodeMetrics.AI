using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeMetrics.AI.Probes;

/// <summary>
/// Propagates framework back-pressure semantics through authored wrappers and
/// their interface contracts. This lets callers depend on an abstraction without
/// losing the fact that awaiting the call is the admission-control mechanism.
/// </summary>
internal static class BackpressureMethodClassifier
{
    private static readonly HashSet<string> BackpressureTypes = new(StringComparer.Ordinal)
    {
        "System.Threading.Channels.ChannelWriter",
        "System.Threading.Channels.ChannelReader",
        "System.Threading.SemaphoreSlim"
    };

    private static readonly HashSet<string> SequentialIoTypes = new(StringComparer.Ordinal)
    {
        "System.IO.Stream",
        "System.IO.TextReader",
        "System.IO.TextWriter"
    };

    public static HashSet<string> Build(
        IReadOnlyList<(string ProjectName, Compilation Compilation)> projects,
        string? solutionDir)
    {
        var declarations = CollectDeclarations(projects, solutionDir);
        var implementations = BuildInterfaceImplementations(declarations.DeclaredTypes);
        var classified = new HashSet<string>(StringComparer.Ordinal);
        bool changed;
        do
        {
            changed = ClassifyAuthoredMethods(declarations.Methods, classified);
            changed |= ClassifyInterfaces(implementations, classified);
        } while (changed);

        return classified;
    }

    private static BackpressureDeclarations CollectDeclarations(
        IReadOnlyList<(string ProjectName, Compilation Compilation)> projects,
        string? solutionDir)
    {
        var methods = new List<(IMethodSymbol Symbol, MethodDeclarationSyntax Syntax, SemanticModel Model)>();
        var declaredTypes = new List<INamedTypeSymbol>();

        foreach (var (_, compilation) in projects)
        {
            foreach (var tree in SourceFileFilter.AnalyzableTrees(compilation, solutionDir))
            {
                var root = tree.GetRoot();
                var model = compilation.GetSemanticModel(tree);

                foreach (var typeDeclaration in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
                {
                    if (model.GetDeclaredSymbol(typeDeclaration) is INamedTypeSymbol type)
                        declaredTypes.Add(type);
                }

                foreach (var declaration in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
                {
                    if (model.GetDeclaredSymbol(declaration) is IMethodSymbol method)
                        methods.Add((method, declaration, model));
                }
            }
        }

        return new BackpressureDeclarations(methods, declaredTypes);
    }

    private static bool ClassifyAuthoredMethods(
        IEnumerable<(IMethodSymbol Symbol, MethodDeclarationSyntax Syntax, SemanticModel Model)> methods,
        HashSet<string> classified)
    {
        var changed = false;
        foreach (var (symbol, syntax, model) in methods)
        {
            var key = MethodKey(symbol);
            if (classified.Contains(key))
                continue;

            var callsBackpressure = syntax.DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Select(invocation => model.GetSymbolInfo(invocation).Symbol as IMethodSymbol)
                .Where(method => method != null)
                .Any(method => IsFrameworkPrimitive(method!) || classified.Contains(MethodKey(method!)));
            if (callsBackpressure)
                changed |= classified.Add(key);
        }

        return changed;
    }

    private static Dictionary<string, HashSet<string>> BuildInterfaceImplementations(
        IEnumerable<INamedTypeSymbol> declaredTypes)
    {
        var implementations = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var type in declaredTypes)
        {
            foreach (var interfaceType in type.AllInterfaces)
            {
                foreach (var interfaceMethod in interfaceType.GetMembers().OfType<IMethodSymbol>())
                {
                    AddInterfaceImplementation(implementations, type, interfaceMethod);
                }
            }
        }

        return implementations;
    }

    private static void AddInterfaceImplementation(
        IDictionary<string, HashSet<string>> implementations,
        INamedTypeSymbol type,
        IMethodSymbol interfaceMethod)
    {
        if (type.FindImplementationForInterfaceMember(interfaceMethod) is not IMethodSymbol implementation ||
            implementation.DeclaringSyntaxReferences.Length == 0)
        {
            return;
        }

        var interfaceKey = MethodKey(interfaceMethod);
        if (!implementations.TryGetValue(interfaceKey, out var implementationKeys))
            implementations[interfaceKey] = implementationKeys = [];
        implementationKeys.Add(MethodKey(implementation));
    }

    private static bool ClassifyInterfaces(
        IReadOnlyDictionary<string, HashSet<string>> implementations,
        HashSet<string> classified)
    {
        var changed = false;
        foreach (var (interfaceKey, implementationKeys) in implementations)
        {
            if (implementationKeys.Count > 0 && implementationKeys.All(classified.Contains))
                changed |= classified.Add(interfaceKey);
        }

        return changed;
    }

    private sealed record BackpressureDeclarations(
        IReadOnlyList<(IMethodSymbol Symbol, MethodDeclarationSyntax Syntax, SemanticModel Model)> Methods,
        IReadOnlyList<INamedTypeSymbol> DeclaredTypes);

    public static bool IsBackpressureInvocation(
        SemanticModel semanticModel,
        InvocationExpressionSyntax invocation,
        IReadOnlySet<string> classifiedMethods)
    {
        if (semanticModel.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method)
            return false;

        return IsFrameworkPrimitive(method) || classifiedMethods.Contains(MethodKey(method));
    }

    public static bool IsSequentialIoInvocation(
        SemanticModel semanticModel,
        InvocationExpressionSyntax invocation)
    {
        if (semanticModel.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method)
            return false;

        for (var owner = method.ContainingType; owner != null; owner = owner.BaseType)
        {
            var qualifiedName = $"{owner.ContainingNamespace?.ToDisplayString()}.{owner.Name}";
            if (SequentialIoTypes.Contains(qualifiedName))
                return true;
        }

        return false;
    }

    internal static string MethodKey(IMethodSymbol method)
    {
        method = method.OriginalDefinition;
        var assembly = method.ContainingAssembly?.Identity.Name ?? "<source>";
        var owner = method.ContainingType?.OriginalDefinition.ToDisplayString(
            SymbolDisplayFormat.FullyQualifiedFormat) ?? "<global>";
        var parameters = string.Join(",", method.Parameters.Select(parameter =>
            parameter.Type.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
        return $"{assembly}:{owner}.{method.MetadataName}({parameters})";
    }

    private static bool IsFrameworkPrimitive(IMethodSymbol method)
    {
        var owner = method.ContainingType?.OriginalDefinition;
        if (owner == null)
            return false;

        var qualifiedName = $"{owner.ContainingNamespace?.ToDisplayString()}.{owner.Name}";
        return BackpressureTypes.Contains(qualifiedName);
    }
}
