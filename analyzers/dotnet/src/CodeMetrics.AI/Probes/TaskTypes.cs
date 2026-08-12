using Microsoft.CodeAnalysis;

namespace CodeMetrics.AI.Probes;

/// <summary>
/// Decides whether a receiver expression is task-like, so sync-over-async probes only flag
/// members such as '.Result' or '.Wait()' when the receiver really is awaitable. Matching on
/// the member name alone reports ordinary domain properties that block nothing.
/// </summary>
public static class TaskTypes
{
    private static readonly HashSet<string> TaskLikeMetadataNames = new(StringComparer.Ordinal)
    {
        "System.Threading.Tasks.Task",
        "System.Threading.Tasks.Task`1",
        "System.Threading.Tasks.ValueTask",
        "System.Threading.Tasks.ValueTask`1",
        // Result of ConfigureAwait(...), which is the receiver of GetAwaiter() in the
        // very common 'task.ConfigureAwait(false).GetAwaiter().GetResult()' shape.
        "System.Runtime.CompilerServices.ConfiguredTaskAwaitable",
        "System.Runtime.CompilerServices.ConfiguredTaskAwaitable`1",
        "System.Runtime.CompilerServices.ConfiguredValueTaskAwaitable",
        "System.Runtime.CompilerServices.ConfiguredValueTaskAwaitable`1",
    };

    /// <summary>
    /// True when the type is Task, ValueTask, a generic form of either, or a configured
    /// awaitable. Unresolved and unknown types return false: this check exists to suppress
    /// false positives, so a receiver that cannot be proven awaitable is not reported.
    /// </summary>
    public static bool IsTaskLike(ITypeSymbol? type)
    {
        if (type == null || type is not INamedTypeSymbol named)
            return false;

        var definition = named.OriginalDefinition;
        var containingNamespace = definition.ContainingNamespace;
        if (containingNamespace == null || containingNamespace.IsGlobalNamespace)
            return false;

        return TaskLikeMetadataNames.Contains(
            $"{containingNamespace.ToDisplayString()}.{definition.MetadataName}");
    }
}
