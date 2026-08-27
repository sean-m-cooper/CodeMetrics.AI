using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeMetrics.AI.Metrics;

public static class ClassCouplingCalculator
{
    private const string NonStructuralUsage = "nonStructuralUsage";

    private static readonly HashSet<SpecialType> PrimitiveTypes =
    [
        SpecialType.System_Boolean, SpecialType.System_Byte, SpecialType.System_SByte,
        SpecialType.System_Char, SpecialType.System_Decimal, SpecialType.System_Double,
        SpecialType.System_Single, SpecialType.System_Int16, SpecialType.System_Int32,
        SpecialType.System_Int64, SpecialType.System_UInt16, SpecialType.System_UInt32,
        SpecialType.System_UInt64, SpecialType.System_String, SpecialType.System_Object,
        SpecialType.System_Void, SpecialType.System_IntPtr, SpecialType.System_UIntPtr
    ];

    private static readonly HashSet<string> NonStructuralValueTypes =
    [
        "System.DateOnly", "System.DateTime", "System.DateTimeOffset", "System.Guid",
        "System.IO.Path", "System.Math", "System.TimeOnly", "System.TimeSpan", "System.Type", "System.Uri",
        "System.Threading.CancellationToken", "System.Array", "System.Linq.Enumerable", "System.Linq.Queryable"
    ];

    private static readonly string[] NonStructuralTypePrefixes =
    [
        "System.Action`", "System.Func`", "System.Tuple`", "System.Threading.Tasks.Task",
        "System.Threading.Tasks.ValueTask", "System.EventHandler", "System.Lazy`",
        "System.Nullable`", "System.Predicate`"
    ];

    private static readonly HashSet<string> FrameworkPresentationTypes =
    [
        "Microsoft.AspNetCore.Http.IResult",
        "Microsoft.AspNetCore.Mvc.ActionResult",
        "Microsoft.AspNetCore.Mvc.ActionResult`1",
        "Microsoft.AspNetCore.Mvc.Controller",
        "Microsoft.AspNetCore.Mvc.ControllerBase",
        "Microsoft.AspNetCore.Mvc.IActionResult"
    ];

    public static int Calculate(TypeDeclarationSyntax typeDecl, SemanticModel model)
    {
        return CalculateTypes(typeDecl, model).Count;
    }

    /// <summary>
    /// Returns the existing broad class-coupling census. This remains the raw evidence
    /// contract even though Architecture findings use <see cref="Analyze"/> structural coupling.
    /// </summary>
    public static IReadOnlyList<string> CalculateTypes(
        TypeDeclarationSyntax typeDecl,
        SemanticModel model)
    {
        var selfSymbol = model.GetDeclaredSymbol(typeDecl) as INamedTypeSymbol;
        return Display(CollectRawTypeSymbols(typeDecl, model, selfSymbol));
    }

    /// <summary>
    /// Separates broad raw type references from dependencies that provide behavior to the type.
    /// Method payloads, return values, attributes, passive data carriers, value/container types,
    /// and framework presentation contracts remain visible as excluded provenance.
    /// </summary>
    public static CouplingAnalysis Analyze(TypeDeclarationSyntax typeDecl, SemanticModel model)
    {
        var selfSymbol = model.GetDeclaredSymbol(typeDecl) as INamedTypeSymbol;
        var raw = CollectRawTypeSymbols(typeDecl, model, selfSymbol);
        var candidates = CollectStructuralTypeSymbols(typeDecl, model, selfSymbol);
        var structural = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        var exclusions = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        foreach (var type in candidates)
        {
            var reason = StructuralExclusionReason(type);
            if (reason == null)
                structural.Add(type);
            else
                AddExclusion(exclusions, reason, type);
        }

        foreach (var type in raw)
        {
            if (structural.Contains(type))
                continue;

            var reason = StructuralExclusionReason(type) ?? NonStructuralUsage;
            AddExclusion(exclusions, reason, type);
        }

        return new CouplingAnalysis(
            Display(raw),
            Display(structural),
            exclusions
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .ToDictionary(
                    pair => pair.Key,
                    pair => (IReadOnlyList<string>)pair.Value
                        .OrderBy(type => type, StringComparer.Ordinal)
                        .ToList(),
                    StringComparer.Ordinal));
    }

    /// <summary>
    /// Calculates raw type coupling for one action method. This compatibility value includes
    /// payload and result types and remains supplemental evidence.
    /// </summary>
    public static int CalculateAction(MethodDeclarationSyntax methodDecl, SemanticModel model)
    {
        var selfSymbol = model.GetDeclaredSymbol(methodDecl)?.ContainingType;
        var coupled = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);

        foreach (var node in methodDecl.DescendantNodesAndSelf())
        {
            if (node.AncestorsAndSelf().OfType<AttributeSyntax>().Any())
                continue;

            CollectFromTypeInfo(model.GetTypeInfo(node).Type, coupled, selfSymbol);
            CollectFromTypeInfo(model.GetTypeInfo(node).ConvertedType, coupled, selfSymbol);

            if (model.GetSymbolInfo(node).Symbol is not IMethodSymbol method)
                continue;

            CollectFromTypeInfo(method.ReturnType, coupled, selfSymbol);
            foreach (var parameter in method.Parameters)
                CollectFromTypeInfo(parameter.Type, coupled, selfSymbol);
        }

        return coupled.Count;
    }

    /// <summary>
    /// Counts behavioral dependencies coordinated by one action, including method-injected
    /// services but excluding payload, result, compiler-generated, and presentation-only types.
    /// </summary>
    public static int CalculateStructuralAction(MethodDeclarationSyntax methodDecl, SemanticModel model)
    {
        var selfSymbol = model.GetDeclaredSymbol(methodDecl)?.ContainingType;
        var candidates = CollectStructuralTypeSymbols(methodDecl, model, selfSymbol);
        return candidates.Count(type => StructuralExclusionReason(type) == null);
    }

    private static HashSet<INamedTypeSymbol> CollectRawTypeSymbols(
        TypeDeclarationSyntax typeDecl,
        SemanticModel model,
        INamedTypeSymbol? selfSymbol)
    {
        var coupled = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        var fromServicesParameters = typeDecl.DescendantNodes()
            .OfType<ParameterSyntax>()
            .Where(IsFromServicesParameter)
            .Select(parameter => model.GetDeclaredSymbol(parameter))
            .OfType<IParameterSymbol>()
            .ToHashSet<IParameterSymbol>(SymbolEqualityComparer.Default);

        foreach (var node in typeDecl.DescendantNodesAndSelf())
        {
            if (IsInsideFromServicesParameter(node) ||
                model.GetSymbolInfo(node).Symbol is IParameterSymbol parameterSymbol &&
                fromServicesParameters.Contains(parameterSymbol))
            {
                continue;
            }

            CollectFromTypeInfo(model.GetTypeInfo(node).Type, coupled, selfSymbol);
            CollectFromTypeInfo(model.GetTypeInfo(node).ConvertedType, coupled, selfSymbol);

            var symbolInfo = model.GetSymbolInfo(node);
            if (symbolInfo.Symbol is IMethodSymbol method)
            {
                CollectFromTypeInfo(method.ReturnType, coupled, selfSymbol);
                foreach (var parameter in GetAnalyzableParameters(node, method))
                    CollectFromTypeInfo(parameter.Type, coupled, selfSymbol);
            }
        }

        if (typeDecl.BaseList != null)
        {
            foreach (var baseType in typeDecl.BaseList.Types)
                CollectFromTypeInfo(model.GetTypeInfo(baseType.Type).Type, coupled, selfSymbol);
        }

        foreach (var attrList in typeDecl.AttributeLists)
            foreach (var attr in attrList.Attributes)
                CollectFromTypeInfo(model.GetTypeInfo(attr).Type, coupled, selfSymbol);

        foreach (var member in typeDecl.Members)
            foreach (var attrList in member.AttributeLists)
                foreach (var attr in attrList.Attributes)
                    CollectFromTypeInfo(model.GetTypeInfo(attr).Type, coupled, selfSymbol);

        return coupled;
    }

    private static HashSet<INamedTypeSymbol> CollectStructuralTypeSymbols(
        SyntaxNode scope,
        SemanticModel model,
        INamedTypeSymbol? selfSymbol)
    {
        var coupled = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);

        if (scope is TypeDeclarationSyntax typeDecl)
            CollectDeclaredDependencies(typeDecl, model, coupled, selfSymbol);

        CollectFromServicesDependencies(scope, model, coupled, selfSymbol);
        CollectUsageDependencies(scope, model, coupled, selfSymbol);
        return coupled;
    }

    private static void CollectDeclaredDependencies(
        TypeDeclarationSyntax typeDeclaration,
        SemanticModel model,
        HashSet<INamedTypeSymbol> coupled,
        INamedTypeSymbol? selfSymbol)
    {
        if (typeDeclaration.BaseList != null)
        {
            foreach (var baseType in typeDeclaration.BaseList.Types)
                CollectStructuralType(model.GetTypeInfo(baseType.Type).Type, coupled, selfSymbol);
        }

        foreach (var member in typeDeclaration.Members)
            CollectMemberDeclarationDependency(member, model, coupled, selfSymbol);
        foreach (var parameter in GetPrimaryConstructorParameters(typeDeclaration))
            CollectStructuralType(model.GetTypeInfo(parameter.Type!).Type, coupled, selfSymbol);
    }

    private static void CollectMemberDeclarationDependency(
        MemberDeclarationSyntax member,
        SemanticModel model,
        HashSet<INamedTypeSymbol> coupled,
        INamedTypeSymbol? selfSymbol)
    {
        switch (member)
        {
            case FieldDeclarationSyntax field:
                CollectStructuralType(model.GetTypeInfo(field.Declaration.Type).Type, coupled, selfSymbol);
                break;
            case EventFieldDeclarationSyntax eventField:
                CollectStructuralType(model.GetTypeInfo(eventField.Declaration.Type).Type, coupled, selfSymbol);
                break;
            case PropertyDeclarationSyntax property:
                CollectStructuralType(model.GetTypeInfo(property.Type).Type, coupled, selfSymbol);
                break;
            case EventDeclarationSyntax eventDeclaration:
                CollectStructuralType(model.GetTypeInfo(eventDeclaration.Type).Type, coupled, selfSymbol);
                break;
            case ConstructorDeclarationSyntax constructor:
                foreach (var parameter in constructor.ParameterList.Parameters)
                    CollectStructuralType(model.GetTypeInfo(parameter.Type!).Type, coupled, selfSymbol);
                break;
        }
    }

    private static IEnumerable<ParameterSyntax> GetPrimaryConstructorParameters(
        TypeDeclarationSyntax typeDeclaration)
    {
        return typeDeclaration switch
        {
            ClassDeclarationSyntax { ParameterList: not null } declaration =>
                declaration.ParameterList.Parameters,
            RecordDeclarationSyntax { ParameterList: not null } declaration =>
                declaration.ParameterList.Parameters,
            _ => []
        };
    }

    private static void CollectFromServicesDependencies(
        SyntaxNode scope,
        SemanticModel model,
        HashSet<INamedTypeSymbol> coupled,
        INamedTypeSymbol? selfSymbol)
    {
        foreach (var parameter in DescendantsWithinContainingType(scope)
                     .OfType<ParameterSyntax>()
                     .Where(IsFromServicesParameter))
        {
            CollectStructuralType(model.GetTypeInfo(parameter.Type!).Type, coupled, selfSymbol);
        }
    }

    private static void CollectUsageDependencies(
        SyntaxNode scope,
        SemanticModel model,
        HashSet<INamedTypeSymbol> coupled,
        INamedTypeSymbol? selfSymbol)
    {
        foreach (var node in DescendantsWithinContainingType(scope))
            CollectUsageDependency(node, model, coupled, selfSymbol);
    }

    private static void CollectUsageDependency(
        SyntaxNode node,
        SemanticModel model,
        HashSet<INamedTypeSymbol> coupled,
        INamedTypeSymbol? selfSymbol)
    {
        switch (node)
        {
            case ObjectCreationExpressionSyntax creation:
                CollectStructuralType(model.GetTypeInfo(creation).Type, coupled, selfSymbol);
                break;
            case ImplicitObjectCreationExpressionSyntax creation:
                CollectStructuralType(model.GetTypeInfo(creation).Type, coupled, selfSymbol);
                break;
            case TypeOfExpressionSyntax typeOfExpression:
                CollectStructuralType(model.GetTypeInfo(typeOfExpression.Type).Type, coupled, selfSymbol);
                break;
            case InvocationExpressionSyntax invocation
                when GetInvokedMethod(invocation, model) is { } method:
                CollectStructuralType(
                    method.ReducedFrom != null ? method.ReceiverType : method.ContainingType,
                    coupled,
                    selfSymbol);
                foreach (var typeArgument in method.TypeArguments)
                    CollectStructuralType(typeArgument, coupled, selfSymbol);
                break;
        }
    }

    private static IEnumerable<SyntaxNode> DescendantsWithinContainingType(SyntaxNode scope)
    {
        return scope.DescendantNodesAndSelf(node =>
            ReferenceEquals(node, scope) || node is not TypeDeclarationSyntax);
    }

    private static IMethodSymbol? GetInvokedMethod(
        InvocationExpressionSyntax invocation,
        SemanticModel model)
    {
        var symbolInfo = model.GetSymbolInfo(invocation);
        return symbolInfo.Symbol as IMethodSymbol ??
               symbolInfo.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();
    }

    private static string? StructuralExclusionReason(INamedTypeSymbol type)
    {
        if (IsCompilerGeneratedCarrier(type))
            return "compilerGenerated";
        if (DerivesFromQualifiedName(type, "System.Attribute"))
            return "attribute";
        if (IsFrameworkRepresentation(type))
            return "frameworkRepresentation";
        if (DataCarrierClassifier.IsPassiveDataCarrier(type))
            return "passiveDataCarrier";
        if (FrameworkPresentationTypes.Contains(QualifiedMetadataName(type)))
            return "frameworkPresentation";
        if (IsNonStructuralValueOrContainer(type))
            return "valueOrContainer";

        return null;
    }

    private static bool IsFrameworkRepresentation(INamedTypeSymbol type)
    {
        var namespaceName = type.OriginalDefinition.ContainingNamespace?.ToDisplayString() ?? "";
        return namespaceName.Equals("Microsoft.CodeAnalysis", StringComparison.Ordinal) ||
               namespaceName.StartsWith("Microsoft.CodeAnalysis.", StringComparison.Ordinal) ||
               namespaceName.Equals("System.Xml.Linq", StringComparison.Ordinal);
    }

    private static bool IsCompilerGeneratedCarrier(INamedTypeSymbol type)
    {
        var definition = type.OriginalDefinition;
        return type.IsAnonymousType || type.IsTupleType ||
               definition.IsAnonymousType || definition.IsTupleType ||
               definition.MetadataName.StartsWith("<>f__AnonymousType", StringComparison.Ordinal) ||
               QualifiedMetadataName(definition).StartsWith("System.ValueTuple`", StringComparison.Ordinal);
    }

    private static bool IsNonStructuralValueOrContainer(INamedTypeSymbol type)
    {
        var definition = type.OriginalDefinition;
        var qualifiedName = QualifiedMetadataName(definition);
        var namespaceName = definition.ContainingNamespace?.ToDisplayString() ?? "";

        return definition.TypeKind == TypeKind.Enum ||
               NonStructuralValueTypes.Contains(qualifiedName) ||
               namespaceName.StartsWith("System.Collections", StringComparison.Ordinal) ||
               namespaceName.StartsWith("System.Linq", StringComparison.Ordinal) ||
               NonStructuralTypePrefixes.Any(prefix =>
                   qualifiedName.StartsWith(prefix, StringComparison.Ordinal)) ||
               DerivesFromQualifiedName(type, "System.Exception") ||
               DerivesFromQualifiedName(type, "System.EventArgs");
    }

    private static bool DerivesFromQualifiedName(INamedTypeSymbol type, string qualifiedName)
    {
        for (var current = type; current != null; current = current.BaseType)
        {
            if (current.ToDisplayString() == qualifiedName)
                return true;
        }

        return false;
    }

    private static string QualifiedMetadataName(INamedTypeSymbol type)
    {
        var definition = type.OriginalDefinition;
        var namespaceName = definition.ContainingNamespace?.ToDisplayString();
        return string.IsNullOrEmpty(namespaceName)
            ? definition.MetadataName
            : $"{namespaceName}.{definition.MetadataName}";
    }

    private static void AddExclusion(
        Dictionary<string, HashSet<string>> exclusions,
        string reason,
        INamedTypeSymbol type)
    {
        if (!exclusions.TryGetValue(reason, out var types))
        {
            types = new HashSet<string>(StringComparer.Ordinal);
            exclusions[reason] = types;
        }

        types.Add(type.ToDisplayString());
    }

    private static IReadOnlyList<string> Display(IEnumerable<INamedTypeSymbol> types)
    {
        return types
            .Select(type => type.ToDisplayString())
            .OrderBy(type => type, StringComparer.Ordinal)
            .ToList();
    }

    private static IEnumerable<IParameterSymbol> GetAnalyzableParameters(SyntaxNode node, IMethodSymbol method)
    {
        var parameterSyntax = node switch
        {
            BaseMethodDeclarationSyntax methodDecl => methodDecl.ParameterList.Parameters,
            LocalFunctionStatementSyntax localFunction => localFunction.ParameterList.Parameters,
            AnonymousFunctionExpressionSyntax anonymousFunction => anonymousFunction switch
            {
                ParenthesizedLambdaExpressionSyntax lambda => lambda.ParameterList.Parameters,
                _ => default
            },
            _ => default
        };

        if (parameterSyntax.Count == 0)
            return method.Parameters;

        var skippedOrdinals = parameterSyntax
            .Select((parameter, index) => (parameter, index))
            .Where(item => IsFromServicesParameter(item.parameter))
            .Select(item => item.index)
            .ToHashSet();

        return method.Parameters.Where((_, index) => !skippedOrdinals.Contains(index));
    }

    private static bool IsInsideFromServicesParameter(SyntaxNode node)
    {
        var parameter = node.AncestorsAndSelf().OfType<ParameterSyntax>().FirstOrDefault();
        return parameter != null && IsFromServicesParameter(parameter);
    }

    private static bool IsFromServicesParameter(ParameterSyntax parameter)
    {
        return parameter.AttributeLists
            .SelectMany(list => list.Attributes)
            .Any(attribute =>
            {
                var name = attribute.Name.ToString();
                return name == "FromServices" ||
                       name == "FromServicesAttribute" ||
                       name.EndsWith(".FromServices", StringComparison.Ordinal) ||
                       name.EndsWith(".FromServicesAttribute", StringComparison.Ordinal);
            });
    }

    private static void CollectStructuralType(
        ITypeSymbol? type,
        HashSet<INamedTypeSymbol> set,
        INamedTypeSymbol? self)
    {
        if (type is null)
            return;

        while (type is IArrayTypeSymbol arrayType)
            type = arrayType.ElementType;

        if (type is ITypeParameterSymbol || type.TypeKind is TypeKind.Error or TypeKind.Dynamic)
            return;

        if (type is not INamedTypeSymbol named)
            return;

        var definition = named.OriginalDefinition;
        if (SymbolEqualityComparer.Default.Equals(definition, self?.OriginalDefinition) ||
            named.ContainingType != null &&
            SymbolEqualityComparer.Default.Equals(
                named.ContainingType.OriginalDefinition, self?.OriginalDefinition) ||
            PrimitiveTypes.Contains(named.SpecialType))
        {
            return;
        }

        set.Add(definition);

        if (IsCompilerGeneratedCarrier(named) ||
            DataCarrierClassifier.IsPassiveDataCarrier(definition) ||
            FrameworkPresentationTypes.Contains(QualifiedMetadataName(definition)))
        {
            return;
        }

        foreach (var typeArgument in named.TypeArguments)
            CollectStructuralType(typeArgument, set, self);
    }

    private static void CollectFromTypeInfo(
        ITypeSymbol? type,
        HashSet<INamedTypeSymbol> set,
        INamedTypeSymbol? self)
    {
        if (type is null)
            return;

        while (type is IArrayTypeSymbol arrayType)
            type = arrayType.ElementType;

        if (type is ITypeParameterSymbol || type.TypeKind is TypeKind.Error or TypeKind.Dynamic)
            return;

        if (type is INamedTypeSymbol named)
        {
            if (SymbolEqualityComparer.Default.Equals(named.OriginalDefinition, self?.OriginalDefinition))
                return;
            if (named.ContainingType != null &&
                SymbolEqualityComparer.Default.Equals(
                    named.ContainingType.OriginalDefinition, self?.OriginalDefinition))
            {
                return;
            }

            if (PrimitiveTypes.Contains(named.SpecialType))
                return;

            set.Add(named.OriginalDefinition);

            foreach (var typeArgument in named.TypeArguments)
                CollectFromTypeInfo(typeArgument, set, self);
        }
    }
}
