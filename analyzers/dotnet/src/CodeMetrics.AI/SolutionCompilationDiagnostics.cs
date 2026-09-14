using CodeMetrics.AI.Output;
using Microsoft.CodeAnalysis;

namespace CodeMetrics.AI;

internal static class SolutionCompilationDiagnostics
{
    internal static void Append(
        SolutionAnalysisContext context,
        IEnumerable<(Project Project, Compilation? Compilation)> compiledProjects,
        SolutionProjectSelection selection,
        CancellationToken cancellationToken)
    {
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
                foreach (var diagnostic in compilation.GetDiagnostics(cancellationToken)
                    .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).Take(20))
                    context.Diagnostics.Add(new AnalysisDiagnostic("compilationError", diagnostic.ToString(), project.Name));
            }
        }
        if (context.AnalyzedProjectNames.Count == 0)
            context.Diagnostics.Add(new AnalysisDiagnostic("emptyPopulation", "No production projects could be analyzed."));
    }
}
