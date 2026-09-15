using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Emit;

namespace CodeMetrics.AI;

/// <summary>Build-equivalent project references for compiler diagnostic checks only.</summary>
internal sealed class CompilationReferenceMetadata
{
    private readonly Dictionary<Compilation, PortableExecutableReference> references = new();

    public Compilation Create(Compilation compilation, CancellationToken cancellationToken)
    {
        var replacements = new List<MetadataReference>();
        foreach (var reference in compilation.References)
        {
            cancellationToken.ThrowIfCancellationRequested();
            replacements.Add(reference is CompilationReference source
                ? GetReference(source.Compilation, cancellationToken).WithProperties(source.Properties)
                : reference);
        }
        return compilation.WithReferences(replacements);
    }

    private PortableExecutableReference GetReference(Compilation compilation, CancellationToken cancellationToken)
    {
        if (references.TryGetValue(compilation, out var reference))
            return reference;

        // Emit current source in memory, never trust a possibly stale bin/obj assembly.
        // Method bodies are unnecessary for consumer binding; metrics retain source references.
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream, options: new EmitOptions(metadataOnly: true), cancellationToken: cancellationToken);
        if (!result.Success)
            throw new InvalidOperationException($"Could not emit reference metadata for '{compilation.AssemblyName}': " +
                string.Join("; ", result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Take(20)));
        reference = MetadataReference.CreateFromImage(stream.ToArray());
        references.Add(compilation, reference);
        return reference;
    }
}
