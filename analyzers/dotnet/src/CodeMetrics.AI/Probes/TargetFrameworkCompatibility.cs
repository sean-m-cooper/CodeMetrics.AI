namespace CodeMetrics.AI.Probes;

internal static class TargetFrameworkCompatibility
{
    public static bool? IsCompatible(
        string projectTargetFramework,
        IEnumerable<string> packageTargetFrameworks)
    {
        var packageFrameworkValues = packageTargetFrameworks.ToList();
        var projectFramework = TargetFrameworkParser.Parse(projectTargetFramework);
        if (projectFramework == null)
            return null;

        var packageFrameworks = packageFrameworkValues.Select(TargetFrameworkParser.Parse).ToList();
        return EvaluateAssets(projectFramework, packageFrameworks);
    }

    private static bool? EvaluateAssets(ParsedFramework project, IReadOnlyList<ParsedFramework?> packages)
    {
        if (packages.Count == 0)
            return true;
        if (packages.Any(package => package != null && IsCompatible(project, package)))
            return true;
        return packages.Any(package => package == null) ? null : false;
    }

    private static bool IsCompatible(ParsedFramework project, ParsedFramework package)
    {
        if (package.Family == FrameworkFamily.Any)
            return true;
        return IsPlatformCompatible(project, package) &&
               SupportedVersion(project, package.Family) is { } maximum && package.Version <= maximum;
    }

    private static readonly Version NetCoreAppMaximum = new(3, 1);
    // Descending minimum versions retain the previous first-matching support bands.
    private static readonly IReadOnlyDictionary<FrameworkFamily, (Version Minimum, Version Standard)[]> NetStandardSupport =
        new Dictionary<FrameworkFamily, (Version, Version)[]>
        {
            [FrameworkFamily.ModernDotNet] = [(new(0, 0), new(2, 1))],
            [FrameworkFamily.NetCoreApp] = [(new(3, 0), new(2, 1)), (new(2, 0), new(2, 0))],
            [FrameworkFamily.NetFramework] =
            [
                (new(4, 6, 1), new(2, 0)), (new(4, 6), new(1, 3)),
                (new(4, 5, 1), new(1, 2)), (new(4, 5), new(1, 1))
            ]
        };

    private static Version? SupportedVersion(ParsedFramework project, FrameworkFamily packageFamily)
    {
        if (project.Family == packageFamily)
            return project.Version;
        if (packageFamily == FrameworkFamily.NetStandard)
            return NetStandardVersion(project);
        if (project.Family == FrameworkFamily.ModernDotNet && packageFamily == FrameworkFamily.NetCoreApp)
            return NetCoreAppMaximum;
        return null;
    }

    private static Version? NetStandardVersion(ParsedFramework project)
    {
        if (!NetStandardSupport.TryGetValue(project.Family, out var bands))
            return null;
        return bands.FirstOrDefault(band => project.Version >= band.Minimum).Standard;
    }

    private static bool IsPlatformCompatible(ParsedFramework project, ParsedFramework package)
    {
        if (package.Platform == null)
            return true;
        if (!string.Equals(project.Platform, package.Platform, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return package.PlatformVersion == null ||
               project.PlatformVersion != null && project.PlatformVersion >= package.PlatformVersion;
    }
}
