using Microsoft.CodeAnalysis;

namespace CodeMetrics.AI;

internal sealed record CompiledProject(
    string Name,
    Compilation Compilation,
    string? ProjectFilePath);
