using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeMetrics.AI.Probes;

/// <summary>
/// Finds fan-out bodies that pass captured state to authored methods which mutate
/// that parameter-reachable object graph. The analysis is deliberately narrow: it
/// reports a demonstrated write, not merely a non-generic Task or a captured client.
/// </summary>
internal static class ConcurrentFanOutProbe
{
    private static readonly HashSet<string> MutationMethods = new(StringComparer.Ordinal)
    {
        "Add", "AddRange", "Clear", "Dequeue", "Enqueue", "Insert", "Remove",
        "RemoveAll", "RemoveAt", "TryAdd", "TryDequeue", "TryRemove", "TryUpdate"
    };

    private sealed record MethodEntry(
        IMethodSymbol Symbol,
        MethodDeclarationSyntax Syntax,
        SemanticModel Model);

    public static List<Finding> Analyze(
        IReadOnlyList<(string ProjectName, Compilation Compilation)> projects,
        string? solutionDir)
    {
        var declarations = CollectDeclarations(projects, solutionDir);
        var mutatedParameters = BuildMutationSummaries(
            declarations.Methods,
            declarations.DeclaredTypes);
        var findings = new List<Finding>();

        foreach (var (projectName, compilation) in projects)
        {
            foreach (var tree in SourceFileFilter.AnalyzableTrees(compilation, solutionDir))
                findings.AddRange(AnalyzeTree(projectName, compilation.GetSemanticModel(tree), mutatedParameters));
        }

        return findings;
    }

    private static IEnumerable<Finding> AnalyzeTree(
        string projectName, SemanticModel model, IReadOnlyDictionary<string, HashSet<int>> mutatedParameters)
    {
        foreach (var whenAll in model.SyntaxTree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (!IsTaskWhenAll(whenAll) || whenAll.ArgumentList.Arguments.Count != 1 ||
                !TryGetProjectionLambda(whenAll.ArgumentList.Arguments[0].Expression, out var lambda))
                continue;

            var mutation = FindCapturedMutation(lambda, model, mutatedParameters);
            if (mutation != null)
                yield return CreateFinding(projectName, whenAll, mutation);
        }
    }

    private sealed record CapturedMutation(string CapturedName, string MethodName);

    private static CapturedMutation? FindCapturedMutation(
        LambdaExpressionSyntax lambda, SemanticModel model,
        IReadOnlyDictionary<string, HashSet<int>> mutatedParameters)
    {
        var lambdaParameterNames = LambdaParameterNames(lambda);
        foreach (var call in lambda.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (model.GetSymbolInfo(call).Symbol is not IMethodSymbol calledMethod ||
                !mutatedParameters.TryGetValue(BackpressureMethodClassifier.MethodKey(calledMethod), out var indexes))
                continue;

            var captured = FindCapturedArgument(call, indexes, lambdaParameterNames, model);
            if (captured != null)
                return new CapturedMutation(captured.Identifier.Text, calledMethod.Name);
        }
        return null;
    }

    private static IdentifierNameSyntax? FindCapturedArgument(
        InvocationExpressionSyntax call, IEnumerable<int> indexes,
        IReadOnlySet<string> lambdaParameterNames, SemanticModel model)
    {
        foreach (var index in indexes.Where(index => index < call.ArgumentList.Arguments.Count))
        {
            var rootIdentifier = RootIdentifier(call.ArgumentList.Arguments[index].Expression);
            if (rootIdentifier == null || lambdaParameterNames.Contains(rootIdentifier.Identifier.Text))
                continue;

            if (model.GetSymbolInfo(rootIdentifier).Symbol is ILocalSymbol or IParameterSymbol or IFieldSymbol or IPropertySymbol)
                return rootIdentifier;
        }
        return null;
    }

    private static Finding CreateFinding(string projectName, InvocationExpressionSyntax whenAll, CapturedMutation mutation)
    {
        return new Finding
        {
            Category = "sharedStateMutationInFanOut",
            Severity = "error",
            Confidence = "high",
            File = whenAll.SyntaxTree.FilePath,
            Line = whenAll.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
            Observations = SourceFindings.Location(whenAll),
            Project = projectName,
            Type = whenAll.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault()?.Identifier.Text,
            Message = $"Concurrent fan-out passes captured state '{mutation.CapturedName}' " +
                      $"to '{mutation.MethodName}', whose implementation mutates that state."
        };
    }

    private static DeclarationIndex CollectDeclarations(
        IReadOnlyList<(string ProjectName, Compilation Compilation)> projects,
        string? solutionDir)
    {
        var methods = new List<MethodEntry>();
        var declaredTypes = new List<INamedTypeSymbol>();
        foreach (var (_, compilation) in projects)
        {
            foreach (var tree in SourceFileFilter.AnalyzableTrees(compilation, solutionDir))
            {
                var root = tree.GetRoot();
                var model = compilation.GetSemanticModel(tree);
                foreach (var declaration in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
                {
                    if (model.GetDeclaredSymbol(declaration) is INamedTypeSymbol type)
                        declaredTypes.Add(type);
                }

                foreach (var declaration in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
                {
                    if (model.GetDeclaredSymbol(declaration) is IMethodSymbol method)
                        methods.Add(new MethodEntry(method, declaration, model));
                }
            }
        }

        return new DeclarationIndex(methods, declaredTypes);
    }

    private sealed record DeclarationIndex(
        IReadOnlyList<MethodEntry> Methods,
        IReadOnlyList<INamedTypeSymbol> DeclaredTypes);

    private static Dictionary<string, HashSet<int>> BuildMutationSummaries(
        IReadOnlyList<MethodEntry> methods,
        IReadOnlyList<INamedTypeSymbol> declaredTypes)
    {
        // Aliases depend only on authored syntax, not on the evolving mutation summaries.
        // Reuse them through fixed-point propagation instead of rescanning each method.
        var analyzedMethods = methods.Select(entry =>
        {
            var aliases = BuildParameterAliases(entry);
            return (Entry: entry, Aliases: (IReadOnlyDictionary<string, int>)aliases,
                Calls: ResolveCalls(entry, aliases).ToArray());
        }).ToArray();
        var interfaceMethods = InterfaceMethods(declaredTypes).ToArray();
        var summaries = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);
        foreach (var (entry, aliases, _) in analyzedMethods)
        {
            var key = BackpressureMethodClassifier.MethodKey(entry.Symbol);
            if (!summaries.TryGetValue(key, out var mutations))
                summaries[key] = mutations = [];
            mutations.UnionWith(FindDirectMutations(entry, aliases));
        }

        bool changed;
        do
        {
            changed = PropagateMethodMutations(analyzedMethods, summaries);
            changed |= PropagateInterfaceMutations(interfaceMethods, summaries);
        } while (changed);

        return summaries;
    }

    private sealed record MutationCall(string MethodKey, int?[] ParameterIndexes);

    private static IEnumerable<MutationCall> ResolveCalls(MethodEntry entry, IReadOnlyDictionary<string, int> aliases)
    {
        // Symbol binding and argument roots do not depend on the growing mutation sets.
        foreach (var invocation in entry.Syntax.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (entry.Model.GetSymbolInfo(invocation).Symbol is IMethodSymbol called)
                yield return new MutationCall(BackpressureMethodClassifier.MethodKey(called),
                    invocation.ArgumentList.Arguments.Select(argument => ParameterIndex(argument.Expression, aliases)).ToArray());
        }
    }

    private static bool PropagateMethodMutations(
        IEnumerable<(MethodEntry Entry, IReadOnlyDictionary<string, int> Aliases, MutationCall[] Calls)> methods,
        IReadOnlyDictionary<string, HashSet<int>> summaries)
    {
        var changed = false;
        foreach (var (entry, _, calls) in methods)
        {
            var target = summaries[BackpressureMethodClassifier.MethodKey(entry.Symbol)];
            foreach (var call in calls)
            {
                if (!summaries.TryGetValue(call.MethodKey, out var calledMutations))
                    continue;
                // Iterate the mutation set in its original order to preserve the first
                // captured argument reported when several parameters are mutated.
                foreach (var index in calledMutations.Where(index => index < call.ParameterIndexes.Length))
                    if (call.ParameterIndexes[index] is { } parameterIndex)
                        changed |= target.Add(parameterIndex);
            }
        }
        return changed;
    }

    private static IEnumerable<(INamedTypeSymbol Type, IMethodSymbol Method)> InterfaceMethods(
        IEnumerable<INamedTypeSymbol> declaredTypes)
    {
        foreach (var type in declaredTypes)
            foreach (var interfaceType in type.AllInterfaces)
                foreach (var method in interfaceType.GetMembers().OfType<IMethodSymbol>())
                    yield return (type, method);
    }

    private static bool PropagateInterfaceMutations(
        IEnumerable<(INamedTypeSymbol Type, IMethodSymbol Method)> methods,
        IDictionary<string, HashSet<int>> summaries)
    {
        var changed = false;
        foreach (var (type, method) in methods)
            changed |= PropagateInterfaceMutation(type, method, summaries);
        return changed;
    }

    private static bool PropagateInterfaceMutation(
        INamedTypeSymbol type,
        IMethodSymbol interfaceMethod,
        IDictionary<string, HashSet<int>> summaries)
    {
        if (type.FindImplementationForInterfaceMember(interfaceMethod) is not IMethodSymbol implementation ||
            !summaries.TryGetValue(
                BackpressureMethodClassifier.MethodKey(implementation), out var implementationMutations))
        {
            return false;
        }

        var interfaceKey = BackpressureMethodClassifier.MethodKey(interfaceMethod);
        if (!summaries.TryGetValue(interfaceKey, out var interfaceMutations))
            summaries[interfaceKey] = interfaceMutations = [];

        var changed = false;
        foreach (var index in implementationMutations.Where(index =>
                     index < interfaceMethod.Parameters.Length))
        {
            changed |= interfaceMutations.Add(index);
        }

        return changed;
    }

    private static HashSet<int> FindDirectMutations(MethodEntry entry, IReadOnlyDictionary<string, int> aliases)
    {
        var assignments = entry.Syntax.DescendantNodes().OfType<AssignmentExpressionSyntax>()
            .Where(assignment => assignment.Left is not IdentifierNameSyntax)
            .Select(assignment => assignment.Left);
        var receivers = entry.Syntax.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Select(invocation => invocation.Expression).OfType<MemberAccessExpressionSyntax>()
            .Where(member => MutationMethods.Contains(member.Name.Identifier.Text))
            .Select(member => member.Expression);
        // Assignment observations precede mutating calls, preserving insertion order.
        return assignments.Concat(receivers).Select(expression => ParameterIndex(expression, aliases))
            .OfType<int>().ToHashSet();
    }

    private static int? ParameterIndex(ExpressionSyntax expression, IReadOnlyDictionary<string, int> aliases)
    {
        var root = RootIdentifier(expression);
        return root != null && aliases.TryGetValue(root.Identifier.Text, out var index) ? index : null;
    }

    private static Dictionary<string, int> BuildParameterAliases(MethodEntry entry)
    {
        var aliases = entry.Symbol.Parameters
            .Select((parameter, index) => (parameter.Name, index))
            .ToDictionary(pair => pair.Name, pair => pair.index, StringComparer.Ordinal);

        bool changed;
        do
        {
            changed = false;
            foreach (var declarator in entry.Syntax.DescendantNodes().OfType<VariableDeclaratorSyntax>())
            {
                if (declarator.Initializer == null || aliases.ContainsKey(declarator.Identifier.Text))
                    continue;

                if (ParameterIndex(declarator.Initializer.Value, aliases) is { } index)
                {
                    aliases[declarator.Identifier.Text] = index;
                    changed = true;
                }
            }
        } while (changed);

        return aliases;
    }

    private static bool IsTaskWhenAll(InvocationExpressionSyntax invocation)
    {
        return invocation.Expression is MemberAccessExpressionSyntax
        {
            Expression: IdentifierNameSyntax { Identifier.Text: "Task" },
            Name.Identifier.Text: "WhenAll"
        };
    }

    private static bool TryGetProjectionLambda(
        ExpressionSyntax expression,
        out LambdaExpressionSyntax lambda)
    {
        var current = expression;
        while (current is InvocationExpressionSyntax invocation &&
               invocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            if (memberAccess.Name.Identifier.Text is "Select" or "SelectMany")
            {
                lambda = invocation.ArgumentList.Arguments
                    .Select(argument => argument.Expression)
                    .OfType<LambdaExpressionSyntax>()
                    .FirstOrDefault()!;
                return lambda != null;
            }

            current = memberAccess.Expression;
        }

        lambda = null!;
        return false;
    }

    private static HashSet<string> LambdaParameterNames(LambdaExpressionSyntax lambda)
    {
        return lambda switch
        {
            SimpleLambdaExpressionSyntax simple => [simple.Parameter.Identifier.Text],
            ParenthesizedLambdaExpressionSyntax parenthesized =>
                parenthesized.ParameterList.Parameters
                    .Select(parameter => parameter.Identifier.Text)
                    .ToHashSet(StringComparer.Ordinal),
            _ => []
        };
    }

    private static IdentifierNameSyntax? RootIdentifier(ExpressionSyntax expression)
    {
        return expression switch
        {
            IdentifierNameSyntax identifier => identifier,
            MemberAccessExpressionSyntax memberAccess => RootIdentifier(memberAccess.Expression),
            ElementAccessExpressionSyntax elementAccess => RootIdentifier(elementAccess.Expression),
            ConditionalAccessExpressionSyntax conditionalAccess => RootIdentifier(conditionalAccess.Expression),
            ParenthesizedExpressionSyntax parenthesized => RootIdentifier(parenthesized.Expression),
            _ => expression.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>().FirstOrDefault()
        };
    }
}
