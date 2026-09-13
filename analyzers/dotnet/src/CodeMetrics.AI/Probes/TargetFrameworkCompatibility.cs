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

        var parsedPackageFrameworks = packageFrameworkValues
            .Select(value => (Value: value, Framework: TargetFrameworkParser.Parse(value)))
            .ToList();
        var packageFrameworks = parsedPackageFrameworks
            .Where(item => item.Framework != null)
            .Select(item => item.Framework!)
            .Distinct()
            .ToList();

        if (packageFrameworks.Count == 0)
            return packageFrameworkValues.Count == 0 ? true : null;

        if (packageFrameworks.Any(packageFramework =>
                IsCompatible(projectFramework, packageFramework)))
        {
            return true;
        }

        return parsedPackageFrameworks.Any(item => item.Framework == null) ? null : false;
    }

    private static bool IsCompatible(ParsedFramework project, ParsedFramework package)
    {
        if (package.Family == FrameworkFamily.Any)
            return true;

        if (!IsPlatformCompatible(project, package))
            return false;

        if (project.Family == package.Family)
            return project.Version >= package.Version;

        if (package.Family == FrameworkFamily.NetStandard)
            return IsNetStandardCompatible(project, package.Version);

        return project.Family == FrameworkFamily.ModernDotNet &&
               package.Family == FrameworkFamily.NetCoreApp &&
               package.Version <= new Version(3, 1);
    }

    private static bool IsNetStandardCompatible(ParsedFramework project, Version packageVersion)
    {
        return project.Family switch
        {
            FrameworkFamily.ModernDotNet => packageVersion <= new Version(2, 1),
            FrameworkFamily.NetCoreApp when project.Version >= new Version(3, 0) =>
                packageVersion <= new Version(2, 1),
            FrameworkFamily.NetCoreApp when project.Version >= new Version(2, 0) =>
                packageVersion <= new Version(2, 0),
            FrameworkFamily.NetFramework when project.Version >= new Version(4, 6, 1) =>
                packageVersion <= new Version(2, 0),
            FrameworkFamily.NetFramework when project.Version >= new Version(4, 6) =>
                packageVersion <= new Version(1, 3),
            FrameworkFamily.NetFramework when project.Version >= new Version(4, 5, 1) =>
                packageVersion <= new Version(1, 2),
            FrameworkFamily.NetFramework when project.Version >= new Version(4, 5) =>
                packageVersion <= new Version(1, 1),
            _ => false
        };
    }

    private static bool IsPlatformCompatible(ParsedFramework project, ParsedFramework package)
    {
        if (package.Platform == null)
            return true;
        if (project.Platform == null ||
            !project.Platform.Equals(package.Platform, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return package.PlatformVersion == null ||
               project.PlatformVersion != null && project.PlatformVersion >= package.PlatformVersion;
    }
}
