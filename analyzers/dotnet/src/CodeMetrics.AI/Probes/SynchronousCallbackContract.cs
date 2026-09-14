using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace CodeMetrics.AI.Probes;

/// <summary>A bounded catalog of synchronous options, repository-factory and lifecycle callbacks.</summary>
internal static class SynchronousCallbackContract
{
    public static bool IsRecognized(ExpressionSyntax callback, SemanticModel model)
    {
        if (callback is AnonymousFunctionExpressionSyntax lambda && !lambda.AsyncKeyword.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.None))
            return false;
        SyntaxNode expression = callback;
        while (expression.Parent is ParenthesizedExpressionSyntax) expression = expression.Parent;
        if (expression.Parent is not ArgumentSyntax argument ||
            model.GetOperation(argument) is not IArgumentOperation { Parameter.Type: INamedTypeSymbol delegateType } operation ||
            delegateType.DelegateInvokeMethod is not { } invoke || SynchronousBoundaryContext.IsAwaitable(invoke.ReturnType))
            return false;
        var consumer = operation.Parent switch
        {
            IInvocationOperation call => call.TargetMethod,
            IObjectCreationOperation creation => creation.Constructor,
            _ => null
        };
        if (consumer == null) return false;
        var type = consumer.ContainingType.OriginalDefinition.ToDisplayString();
        return (consumer.Name == "Configure" && consumer.ContainingType.MetadataName == "OptionsBuilder`1" &&
                consumer.ContainingNamespace.ToDisplayString() == "Microsoft.Extensions.Options" && invoke.ReturnsVoid) ||
            (consumer.MethodKind == MethodKind.Constructor &&
             type == "Microsoft.AspNetCore.DataProtection.StackExchangeRedis.RedisXmlRepository" &&
             invoke.ReturnType.ToDisplayString() == "StackExchange.Redis.IDatabase") ||
            (consumer.Name == "Register" && type == "System.Threading.CancellationToken" && invoke.ReturnsVoid);
    }
}
