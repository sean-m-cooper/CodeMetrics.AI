using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeMetrics.AI.Metrics;

internal sealed class ClassCouplingCollector(SemanticModel model, INamedTypeSymbol? selfSymbol)
{
    private readonly HashSet<INamedTypeSymbol> coupled = new(SymbolEqualityComparer.Default);

    public int Count => coupled.Count;

    public void CollectFromType(TypeDeclarationSyntax typeDecl)
    {
        CollectFromNodes(typeDecl);
        CollectFromBaseTypes(typeDecl);
        CollectFromAttributes(typeDecl.AttributeLists);
        CollectFromMemberAttributes(typeDecl.Members);
    }

    private void CollectFromNodes(TypeDeclarationSyntax typeDecl)
    {
        foreach (var node in typeDecl.DescendantNodesAndSelf())
        {
            if (FromServicesParameterFilter.IsInsideFromServicesParameter(node))
                continue;

            CollectFromTypeInfo(model.GetTypeInfo(node).Type);
            CollectFromTypeInfo(model.GetTypeInfo(node).ConvertedType);
            CollectFromSymbolInfo(node);
        }
    }

    private void CollectFromSymbolInfo(SyntaxNode node)
    {
        if (model.GetSymbolInfo(node).Symbol is not IMethodSymbol method)
            return;

        CollectFromTypeInfo(method.ReturnType);
        foreach (var parameter in MethodParameterFilter.GetAnalyzableParameters(node, method))
            CollectFromTypeInfo(parameter.Type);
    }

    private void CollectFromBaseTypes(TypeDeclarationSyntax typeDecl)
    {
        if (typeDecl is not ClassDeclarationSyntax { BaseList: not null } classDecl)
            return;

        foreach (var baseType in classDecl.BaseList.Types)
            CollectFromTypeInfo(model.GetTypeInfo(baseType.Type).Type);
    }

    private void CollectFromMemberAttributes(SyntaxList<MemberDeclarationSyntax> members)
    {
        foreach (var member in members)
            CollectFromAttributes(member.AttributeLists);
    }

    private void CollectFromAttributes(SyntaxList<AttributeListSyntax> attributeLists)
    {
        foreach (var attrList in attributeLists)
        {
            foreach (var attr in attrList.Attributes)
                CollectFromTypeInfo(model.GetTypeInfo(attr).Type);
        }
    }

    private void CollectFromTypeInfo(ITypeSymbol? type)
    {
        CoupledTypeSymbolCollector.Collect(type, coupled, selfSymbol);
    }
}

internal static class MethodParameterFilter
{
    public static IEnumerable<IParameterSymbol> GetAnalyzableParameters(SyntaxNode node, IMethodSymbol method)
    {
        var parameterSyntax = GetParameterSyntax(node);
        if (parameterSyntax.Count == 0)
            return method.Parameters;

        var skippedOrdinals = parameterSyntax
            .Select((parameter, index) => (parameter, index))
            .Where(item => FromServicesParameterFilter.IsFromServicesParameter(item.parameter))
            .Select(item => item.index)
            .ToHashSet();

        return method.Parameters.Where((_, index) => !skippedOrdinals.Contains(index));
    }

    private static SeparatedSyntaxList<ParameterSyntax> GetParameterSyntax(SyntaxNode node)
    {
        return node switch
        {
            BaseMethodDeclarationSyntax methodDecl => methodDecl.ParameterList.Parameters,
            LocalFunctionStatementSyntax localFunction => localFunction.ParameterList.Parameters,
            ParenthesizedLambdaExpressionSyntax lambda => lambda.ParameterList.Parameters,
            _ => default
        };
    }
}

internal static class FromServicesParameterFilter
{
    public static bool IsInsideFromServicesParameter(SyntaxNode node)
    {
        var parameter = node.AncestorsAndSelf().OfType<ParameterSyntax>().FirstOrDefault();
        return parameter != null && IsFromServicesParameter(parameter);
    }

    public static bool IsFromServicesParameter(ParameterSyntax parameter)
    {
        return parameter.AttributeLists
            .SelectMany(list => list.Attributes)
            .Any(attribute => AttributeNameMatches(attribute.Name.ToString()));
    }

    private static bool AttributeNameMatches(string name)
    {
        return name == "FromServices" ||
               name == "FromServicesAttribute" ||
               name.EndsWith(".FromServices", StringComparison.Ordinal) ||
               name.EndsWith(".FromServicesAttribute", StringComparison.Ordinal);
    }
}

internal static class CoupledTypeSymbolCollector
{
    private static readonly HashSet<SpecialType> PrimitiveTypes =
    [
        SpecialType.System_Boolean, SpecialType.System_Byte, SpecialType.System_SByte,
        SpecialType.System_Char, SpecialType.System_Decimal, SpecialType.System_Double,
        SpecialType.System_Single, SpecialType.System_Int16, SpecialType.System_Int32,
        SpecialType.System_Int64, SpecialType.System_UInt16, SpecialType.System_UInt32,
        SpecialType.System_UInt64, SpecialType.System_String, SpecialType.System_Object,
        SpecialType.System_Void, SpecialType.System_IntPtr, SpecialType.System_UIntPtr
    ];

    public static void Collect(
        ITypeSymbol? type,
        HashSet<INamedTypeSymbol> set,
        INamedTypeSymbol? self)
    {
        if (type is null)
            return;

        type = UnwrapArrayType(type);
        if (ShouldSkip(type))
            return;

        if (type is INamedTypeSymbol named)
            CollectNamedType(named, set, self);
    }

    private static ITypeSymbol UnwrapArrayType(ITypeSymbol type)
    {
        while (type is IArrayTypeSymbol arrayType)
            type = arrayType.ElementType;

        return type;
    }

    private static bool ShouldSkip(ITypeSymbol type)
    {
        return type is ITypeParameterSymbol
            || type.TypeKind is TypeKind.Error or TypeKind.Dynamic;
    }

    private static void CollectNamedType(
        INamedTypeSymbol named,
        HashSet<INamedTypeSymbol> set,
        INamedTypeSymbol? self)
    {
        if (IsSelfReference(named, self) || PrimitiveTypes.Contains(named.SpecialType))
            return;

        set.Add(named.OriginalDefinition);
        foreach (var typeArg in named.TypeArguments)
            Collect(typeArg, set, self);
    }

    private static bool IsSelfReference(INamedTypeSymbol named, INamedTypeSymbol? self)
    {
        return SymbolEqualityComparer.Default.Equals(named.OriginalDefinition, self?.OriginalDefinition)
            || named.ContainingType != null
            && SymbolEqualityComparer.Default.Equals(named.ContainingType.OriginalDefinition, self?.OriginalDefinition);
    }
}
