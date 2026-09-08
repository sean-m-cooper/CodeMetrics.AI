using System.Collections.Concurrent;
using CodeMetrics.AI.Output;
using Microsoft.Build.Logging;
using Microsoft.CodeAnalysis;

namespace CodeMetrics.AI;

// Roslyn 5.9 reports both MSBuild warnings and errors as WorkspaceDiagnosticKind.Failure.
// Correlate with events from that exact design-time build, never with advisory text alone.
internal sealed class WorkspaceDiagnostics : IDisposable
{
    private readonly string directory = Directory.CreateTempSubdirectory("codemetrics-msbuild-").FullName;
    private readonly ConcurrentQueue<WorkspaceDiagnostic> diagnostics = new();

    public BinaryLogger CreateLogger() => new() { Parameters = Path.Combine(directory, "workspace.binlog") };

    public void Record(WorkspaceDiagnostic diagnostic) => diagnostics.Enqueue(diagnostic);

    public IReadOnlyList<AnalysisDiagnostic> Read(CancellationToken cancellationToken)
    {
        var warnings = new HashSet<string>(StringComparer.Ordinal);
        var errors = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(directory, "*.binlog").Order(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var replay = new BinaryLogReplayEventSource();
            replay.WarningRaised += (_, e) => Add(warnings, e.ProjectFile, e.Message, e.File, e.LineNumber, e.ColumnNumber);
            replay.ErrorRaised += (_, e) => Add(errors, e.ProjectFile, e.Message, e.File, e.LineNumber, e.ColumnNumber);
            replay.Replay(file, cancellationToken);
        }

        var result = diagnostics.Select(diagnostic => new AnalysisDiagnostic(
            diagnostic.Kind == WorkspaceDiagnosticKind.Warning ||
                (warnings.Contains(diagnostic.Message) && !errors.Contains(diagnostic.Message))
                ? "workspaceWarning" : "workspace",
            diagnostic.Message)).Distinct().ToList();
        // An actual build error must remain blocking even if Roslyn omits its callback.
        foreach (var error in errors.Where(error => !result.Any(diagnostic => diagnostic.Message == error)))
            result.Add(new AnalysisDiagnostic("workspace", error));
        return result;
    }

    private static void Add(HashSet<string> messages, string? project, string? message, string? file, int line, int column)
    {
        // The out-of-process host can serialize either the base message or its located form.
        messages.Add($"Msbuild failed when processing the file '{project}' with message: {message}");
        if (!string.IsNullOrEmpty(file))
            messages.Add($"Msbuild failed when processing the file '{project}' with message: {file}: ({line}, {column}): {message}");
    }

    public void Dispose()
    {
        try { Directory.Delete(directory, recursive: true); }
        catch (IOException ex) { Console.Error.WriteLine($"Could not remove temporary MSBuild logs: {ex.Message}"); }
        catch (UnauthorizedAccessException ex) { Console.Error.WriteLine($"Could not remove temporary MSBuild logs: {ex.Message}"); }
    }
}
