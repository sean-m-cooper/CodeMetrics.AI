using CodeMetrics.AI.Output;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Concurrent;
using System.Collections.Immutable;

namespace CodeMetrics.AI;

internal static class SolutionCompilationDiagnostics
{
    internal static async Task AppendAsync(
        SolutionAnalysisContext context,
        IEnumerable<(Project Project, Compilation? Compilation)> compiledProjects,
        SolutionProjectSelection selection,
        CancellationToken cancellationToken)
    {
        var references = new CompilationReferenceMetadata();
        foreach (var (project, compilation) in compiledProjects)
        {
            if (compilation == null)
            {
                context.Diagnostics.Add(new AnalysisDiagnostic("compilationUnavailable", "No compilation was available.", project.Name));
                if (selection.AnalyzedProjectIds.Contains(project.Id))
                    selection.SkippedProjects.Add(new SkippedProjectInfo { Name = project.Name, Reason = "Compilation unavailable" });
            }
            else
            {
                var diagnostics = await GetDiagnosticsAsync(project, compilation, context, references, cancellationToken);
                foreach (var diagnostic in diagnostics
                    .Where(diagnostic => !diagnostic.IsSuppressed && diagnostic.Severity == DiagnosticSeverity.Error).Take(20))
                    context.Diagnostics.Add(new AnalysisDiagnostic("compilationError", diagnostic.ToString(), project.Name));
            }
        }
        if (context.AnalyzedProjectNames.Count == 0)
            context.Diagnostics.Add(new AnalysisDiagnostic("emptyPopulation", "No production projects could be analyzed."));
    }

    private static async Task<ImmutableArray<Diagnostic>> GetDiagnosticsAsync(
        Project project, Compilation compilation, SolutionAnalysisContext context,
        CompilationReferenceMetadata references, CancellationToken cancellationToken)
    {
        var diagnostics = compilation.GetDiagnostics(cancellationToken);
        if (!diagnostics.Any(diagnostic => diagnostic.IsWarningAsError))
            return diagnostics;

        if (compilation.References.Any(reference => reference is CompilationReference))
        {
            try
            {
                compilation = references.Create(compilation, cancellationToken);
                diagnostics = compilation.GetDiagnostics(cancellationToken);
            }
            catch (InvalidOperationException ex)
            {
                context.Diagnostics.Add(new AnalysisDiagnostic("compilationUnavailable", ex.Message, project.Name));
                return diagnostics;
            }
        }

        var suppressors = project.AnalyzerReferences
            .SelectMany(reference => reference.GetAnalyzers(project.Language))
            .OfType<DiagnosticSuppressor>().Cast<DiagnosticAnalyzer>().ToImmutableArray();
        if (suppressors.IsEmpty)
            return diagnostics;

        // Match compiler suppression without running unrelated analyzer rules. Raw
        // compiler diagnostics do not apply NUnit/other project suppressors themselves.
        var failures = new ConcurrentQueue<Diagnostic>();
        var options = new CompilationWithAnalyzersOptions(project.AnalyzerOptions,
            (_, _, diagnostic) => failures.Enqueue(diagnostic), concurrentAnalysis: false,
            logAnalyzerExecutionTime: false, reportSuppressedDiagnostics: false);
        var effective = await compilation.WithAnalyzers(suppressors, options).GetAllDiagnosticsAsync(cancellationToken);
        foreach (var failure in failures)
            context.Diagnostics.Add(new AnalysisDiagnostic("compilationUnavailable",
                "Diagnostic suppression failed: " + failure, project.Name));
        return effective;
    }
}
