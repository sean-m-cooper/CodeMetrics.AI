using CodeMetrics.AI.Output;

namespace CodeMetrics.AI;

internal static class AnalysisOutputWriter
{
    public static async Task WriteCsvAsync(
        CliOptions options,
        MetricsAnalysisResult metrics,
        CancellationToken cancellationToken)
    {
        await CsvWriter.WriteAsync(
            options.Output,
            metrics.TypeMetrics,
            metrics.MemberMetrics,
            cancellationToken);
        Console.WriteLine($"CSV: {options.Output}");
    }

    public static async Task WriteEvidenceAsync(
        CliOptions options,
        EvidenceModel evidence,
        CancellationToken cancellationToken)
    {
        await EvidenceWriter.WriteAsync(options.ScorecardOutput, evidence, cancellationToken);
        Console.WriteLine($"Evidence: {options.ScorecardOutput}");
    }
}
