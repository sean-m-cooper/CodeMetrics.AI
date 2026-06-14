using Microsoft.CodeAnalysis;

namespace CodeMetrics.AI.Metrics;

public static class DepthOfInheritanceCalculator
{
    public static int Calculate(INamedTypeSymbol typeSymbol)
    {
        if (typeSymbol.TypeKind is TypeKind.Interface or TypeKind.Enum or TypeKind.Delegate)
            return 1;

        if (typeSymbol.SpecialType == SpecialType.System_Object)
            return 0;

        int depth = 0;
        var current = typeSymbol;
        while (current.BaseType != null)
        {
            depth++;
            current = current.BaseType;
        }
        return depth;
    }
}
