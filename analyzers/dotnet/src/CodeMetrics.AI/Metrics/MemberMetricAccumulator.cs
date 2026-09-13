using Microsoft.CodeAnalysis;

namespace CodeMetrics.AI.Metrics;

// One accumulator belongs to one logical type. Included declarations are measured
// by the caller; symbol references must never import generated implementations.
internal sealed class MemberMetricAccumulator
{
    private readonly Dictionary<ISymbol, int> _positions = new(SymbolEqualityComparer.Default);
    public List<MemberMetrics> Members { get; } = [];

    public void Add(ISymbol? symbol, MemberMetrics metrics)
    {
        var definition = symbol switch
        {
            IMethodSymbol method => method.PartialDefinitionPart ?? method,
            IPropertySymbol property => (ISymbol?)property.PartialDefinitionPart ?? property,
            _ => null
        };
        if (definition != null && _positions.TryGetValue(definition, out var index))
        {
            // Keep the original position, replacing a signature only with an authored body.
            if (metrics.HasBody)
                Members[index] = metrics;
            return;
        }
        if (definition != null)
            _positions[definition] = Members.Count;
        Members.Add(metrics);
    }
}
