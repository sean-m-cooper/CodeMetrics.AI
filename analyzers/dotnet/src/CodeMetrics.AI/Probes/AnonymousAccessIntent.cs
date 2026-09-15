using Microsoft.CodeAnalysis;

namespace CodeMetrics.AI.Probes;

internal static class AnonymousAccessIntent
{
    public static bool IsRecognized(INamedTypeSymbol? attribute) => attribute != null &&
        (attribute.ToDisplayString() is "Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute" or
            "System.Web.Mvc.AllowAnonymousAttribute" or "System.Web.Http.AllowAnonymousAttribute" ||
         attribute.AllInterfaces.Any(type => type.ToDisplayString() == "Microsoft.AspNetCore.Authorization.IAllowAnonymous"));

    public static bool IsDeclared(ISymbol symbol) => symbol.GetAttributes().Any(attribute => IsRecognized(attribute.AttributeClass));
}
