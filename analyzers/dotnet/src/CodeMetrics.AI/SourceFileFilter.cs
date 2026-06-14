namespace CodeMetrics.AI;

public static class SourceFileFilter
{
    public static bool ShouldAnalyze(string? filePath, string solutionRoot)
    {
        if (string.IsNullOrWhiteSpace(filePath) || string.IsNullOrWhiteSpace(solutionRoot))
            return true;

        var fullPath = Path.GetFullPath(filePath);
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(solutionRoot));

        if (!fullPath.Equals(fullRoot, StringComparison.OrdinalIgnoreCase) &&
            !fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var normalizedPath = fullPath
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        var nugetPackageSegment = $"{Path.DirectorySeparatorChar}.nuget{Path.DirectorySeparatorChar}packages{Path.DirectorySeparatorChar}";

        return normalizedPath.IndexOf(nugetPackageSegment, StringComparison.OrdinalIgnoreCase) < 0;
    }
}
