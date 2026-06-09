using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeMetrics.AI.Metrics;

public static class ClassCouplingCalculator
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

    public static int Calculate(TypeDeclarationSyntax typeDecl, SemanticModel model)
    {
        var selfSymbol = model.GetDeclaredSymbol(typeDecl) as INamedTypeSymbol;
        var coupled = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);

        foreach (var node in typeDecl.DescendantNodesAndSelf())
        {
            CollectFromTypeInfo(model.GetTypeInfo(node).Type, coupled, selfSymbol);
            CollectFromTypeInfo(model.GetTypeInfo(node).ConvertedType, coupled, selfSymbol);

            var symbolInfo = model.GetSymbolInfo(node);
            if (symbolInfo.Symbol is IMethodSymbol method)
            {
                CollectFromTypeInfo(method.ReturnType, coupled, selfSymbol);
                foreach (var p in method.Parameters)
                    CollectFromTypeInfo(p.Type, coupled, selfSymbol);
            }
        }

        // Base types and interfaces
        if (typeDecl is ClassDeclarationSyntax classDecl && classDecl.BaseList != null)
        {
            foreach (var baseType in classDecl.BaseList.Types)
            {
                var baseSymbol = model.GetTypeInfo(baseType.Type).Type;
                CollectFromTypeInfo(baseSymbol, coupled, selfSymbol);
            }
        }

        // Attributes on the type
        foreach (var attrList in typeDecl.AttributeLists)
            foreach (var attr in attrList.Attributes)
                CollectFromTypeInfo(model.GetTypeInfo(attr).Type, coupled, selfSymbol);

        // Attributes on members
        foreach (var member in typeDecl.Members)
            foreach (var attrList in member.AttributeLists)
                foreach (var attr in attrList.Attributes)
                    CollectFromTypeInfo(model.GetTypeInfo(attr).Type, coupled, selfSymbol);

        return coupled.Count;
    }

    private static void CollectFromTypeInfo(ITypeSymbol? type, HashSet<INamedTypeSymbol> set,
        INamedTypeSymbol? self)
    {
        if (type is null) return;

        while (type is IArrayTypeSymbol arrayType)
            type = arrayType.ElementType;

        if (type is ITypeParameterSymbol) return;
        if (type.TypeKind is TypeKind.Error or TypeKind.Dynamic) return;

        if (type is INamedTypeSymbol named)
        {
            if (SymbolEqualityComparer.Default.Equals(named.OriginalDefinition, self?.OriginalDefinition))
                return;
            if (named.ContainingType != null &&
                SymbolEqualityComparer.Default.Equals(named.ContainingType.OriginalDefinition, self?.OriginalDefinition))
                return;
            if (PrimitiveTypes.Contains(named.SpecialType))
                return;

            set.Add(named.OriginalDefinition);

            foreach (var typeArg in named.TypeArguments)
                CollectFromTypeInfo(typeArg, set, self);
        }
    }
}
