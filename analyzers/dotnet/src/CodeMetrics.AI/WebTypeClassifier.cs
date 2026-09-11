using Microsoft.CodeAnalysis;

namespace CodeMetrics.AI;

internal static class WebTypeClassifier
{
    public static bool IsController(INamedTypeSymbol? type)
    {
        if (type == null || type.GetAttributes().Any(a =>
                a.AttributeClass?.ToDisplayString() == "Microsoft.AspNetCore.Mvc.NonControllerAttribute"))
            return false;

        for (var current = type; current != null; current = current.BaseType)
        {
            if (current.ToDisplayString() is "Microsoft.AspNetCore.Mvc.ControllerBase" or
                "Microsoft.AspNetCore.Mvc.Controller" or "System.Web.Mvc.Controller" or
                "System.Web.Http.ApiController")
                return true;
            if (current.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() is
                    "Microsoft.AspNetCore.Mvc.ControllerAttribute" or "Microsoft.AspNetCore.Mvc.ApiControllerAttribute"))
                return true;
        }
        return false;
    }

    public static bool IsDataDependency(ITypeSymbol? type)
    {
        if (type is not INamedTypeSymbol named || named.TypeKind == TypeKind.Delegate)
            return false;
        for (var current = named; current != null; current = current.BaseType)
        {
            if (current.ToDisplayString() is "Microsoft.EntityFrameworkCore.DbContext" or "System.Data.Entity.DbContext")
                return true;
        }
        // Repository/DAL remain explicitly named architectural conventions. A generic argument
        // called Context, or an execution context by itself, does not establish data access.
        return named.Name.Contains("Repository", StringComparison.OrdinalIgnoreCase) ||
               named.Name.EndsWith("DbContext", StringComparison.Ordinal) ||
               named.Name.EndsWith("DAL", StringComparison.Ordinal);
    }
}
