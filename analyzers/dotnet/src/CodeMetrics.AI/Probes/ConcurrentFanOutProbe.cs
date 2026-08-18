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
        var methods = new List<MethodEntry>();
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
                        methods.Add(new MethodEntry(method, declaration, model));
                }
            }
        }

        var mutatedParameters = BuildMutationSummaries(methods, declaredTypes);
        var findings = new List<Finding>();

        foreach (var (projectName, compilation) in projects)
        {
            foreach (var tree in SourceFileFilter.AnalyzableTrees(compilation, solutionDir))
            {
                var root = tree.GetRoot();
                var model = compilation.GetSemanticModel(tree);
                foreach (var whenAll in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
                {
                    if (!IsTaskWhenAll(whenAll) ||
                        whenAll.ArgumentList.Arguments.Count != 1 ||
                        !TryGetProjectionLambda(whenAll.ArgumentList.Arguments[0].Expression, out var lambda))
                    {
                        continue;
                    }

                    var lambdaParameterNames = LambdaParameterNames(lambda);
                    foreach (var call in lambda.DescendantNodes().OfType<InvocationExpressionSyntax>())
                    {
                        if (model.GetSymbolInfo(call).Symbol is not IMethodSymbol calledMethod ||
                            !mutatedParameters.TryGetValue(
                                BackpressureMethodClassifier.MethodKey(calledMethod), out var indexes))
                        {
                            continue;
                        }

                        foreach (var index in indexes.Where(index => index < call.ArgumentList.Arguments.Count))
                        {
                            var argument = call.ArgumentList.Arguments[index].Expression;
                            var rootIdentifier = RootIdentifier(argument);
                            if (rootIdentifier == null || lambdaParameterNames.Contains(rootIdentifier.Identifier.Text))
                                continue;

                            var captured = model.GetSymbolInfo(rootIdentifier).Symbol;
                            if (captured is not (ILocalSymbol or IParameterSymbol or IFieldSymbol or IPropertySymbol))
                                continue;

                            findings.Add(new Finding
                            {
                                Category = "sharedStateMutationInFanOut",
                                Severity = "error",
                                Confidence = "high",
                                File = tree.FilePath,
                                Line = whenAll.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                                Project = projectName,
                                Type = whenAll.Ancestors().OfType<TypeDeclarationSyntax>()
                                    .FirstOrDefault()?.Identifier.Text,
                                Message = $"Concurrent fan-out passes captured state '{rootIdentifier.Identifier.Text}' " +
                                          $"to '{calledMethod.Name}', whose implementation mutates that state."
                            });
                            goto NextWhenAll;
                        }
                    }

                NextWhenAll:;
                }
            }
        }

        return findings;
    }

    private static Dictionary<string, HashSet<int>> BuildMutationSummaries(
        IReadOnlyList<MethodEntry> methods,
        IReadOnlyList<INamedTypeSymbol> declaredTypes)
    {
        var summaries = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);
        foreach (var entry in methods)
        {
            var key = BackpressureMethodClassifier.MethodKey(entry.Symbol);
            if (!summaries.TryGetValue(key, out var mutations))
                summaries[key] = mutations = [];
            mutations.UnionWith(FindDirectMutations(entry));
        }

        bool changed;
        do
        {
            changed = false;
            foreach (var entry in methods)
            {
                var aliases = BuildParameterAliases(entry);
                var target = summaries[BackpressureMethodClassifier.MethodKey(entry.Symbol)];
                foreach (var invocation in entry.Syntax.DescendantNodes().OfType<InvocationExpressionSyntax>())
                {
                    if (entry.Model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol called ||
                        !summaries.TryGetValue(
                            BackpressureMethodClassifier.MethodKey(called), out var calledMutations))
                    {
                        continue;
                    }

                    foreach (var index in calledMutations.Where(index => index < invocation.ArgumentList.Arguments.Count))
                    {
                        var root = RootIdentifier(invocation.ArgumentList.Arguments[index].Expression);
                        if (root != null && aliases.TryGetValue(root.Identifier.Text, out var parameterIndex))
                            changed |= target.Add(parameterIndex);
                    }
                }
            }

            foreach (var type in declaredTypes)
            {
                foreach (var interfaceType in type.AllInterfaces)
                {
                    foreach (var interfaceMethod in interfaceType.GetMembers().OfType<IMethodSymbol>())
                    {
                        if (type.FindImplementationForInterfaceMember(interfaceMethod) is not IMethodSymbol implementation ||
                            !summaries.TryGetValue(
                                BackpressureMethodClassifier.MethodKey(implementation), out var implementationMutations))
                        {
                            continue;
                        }

                        var interfaceKey = BackpressureMethodClassifier.MethodKey(interfaceMethod);
                        if (!summaries.TryGetValue(interfaceKey, out var interfaceMutations))
                            summaries[interfaceKey] = interfaceMutations = [];

                        foreach (var index in implementationMutations.Where(index => index < interfaceMethod.Parameters.Length))
                            changed |= interfaceMutations.Add(index);
                    }
                }
            }
        } while (changed);

        return summaries;
    }

    private static HashSet<int> FindDirectMutations(MethodEntry entry)
    {
        var aliases = BuildParameterAliases(entry);
        var mutated = new HashSet<int>();

        foreach (var assignment in entry.Syntax.DescendantNodes().OfType<AssignmentExpressionSyntax>())
        {
            var root = RootIdentifier(assignment.Left);
            if (root != null && aliases.TryGetValue(root.Identifier.Text, out var index) &&
                assignment.Left is not IdentifierNameSyntax)
            {
                mutated.Add(index);
            }
        }

        foreach (var invocation in entry.Syntax.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess ||
                !MutationMethods.Contains(memberAccess.Name.Identifier.Text))
            {
                continue;
            }

            var root = RootIdentifier(memberAccess.Expression);
            if (root != null && aliases.TryGetValue(root.Identifier.Text, out var index))
                mutated.Add(index);
        }

        return mutated;
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

                var root = RootIdentifier(declarator.Initializer.Value);
                if (root != null && aliases.TryGetValue(root.Identifier.Text, out var index))
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
