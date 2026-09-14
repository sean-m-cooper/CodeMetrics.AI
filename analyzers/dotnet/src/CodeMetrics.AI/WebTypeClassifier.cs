using Microsoft.CodeAnalysis;

namespace CodeMetrics.AI;

internal static class WebTypeClassifier
{
    public static bool IsController(INamedTypeSymbol? type)
    {
        if (type == null || type.GetAttributes().Any(a =>
                a.AttributeClass?.ToDisplayString() == "Microsoft.AspNetCore.Mvc.NonControllerAttribute"))
            return false;

        return Hierarchy(type).Any(IsControllerType);
    }

    public static bool IsDataDependency(ITypeSymbol? type)
    {
        if (type is not INamedTypeSymbol named || named.TypeKind == TypeKind.Delegate)
            return false;
        return Hierarchy(named).Any(current => current.ToDisplayString() is
                   "Microsoft.EntityFrameworkCore.DbContext" or "System.Data.Entity.DbContext") ||
               HasDataLayerName(named.Name);
    }

    private static IEnumerable<INamedTypeSymbol> Hierarchy(INamedTypeSymbol type)
    {
        for (var current = type; current != null; current = current.BaseType)
            yield return current;
    }

    private static bool IsControllerType(INamedTypeSymbol type)
    {
        return type.ToDisplayString() is "Microsoft.AspNetCore.Mvc.ControllerBase" or
                   "Microsoft.AspNetCore.Mvc.Controller" or "System.Web.Mvc.Controller" or
                   "System.Web.Http.ApiController" ||
               type.GetAttributes().Any(attribute => attribute.AttributeClass?.ToDisplayString() is
                   "Microsoft.AspNetCore.Mvc.ControllerAttribute" or "Microsoft.AspNetCore.Mvc.ApiControllerAttribute");
    }

    private static bool HasDataLayerName(string name)
    {
        // Repository/DAL remain explicitly named architectural conventions. A generic argument
        // called Context, or an execution context by itself, does not establish data access.
        return name.Contains("Repository", StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith("DbContext", StringComparison.Ordinal) ||
               name.EndsWith("DAL", StringComparison.Ordinal);
    }
}
