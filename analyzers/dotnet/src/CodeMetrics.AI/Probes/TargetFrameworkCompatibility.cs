using System.Text.RegularExpressions;

namespace CodeMetrics.AI.Probes;

internal static class TargetFrameworkCompatibility
{
    private sealed record ParsedFramework(
        FrameworkFamily Family,
        Version Version,
        string? Platform = null,
        Version? PlatformVersion = null);

    private enum FrameworkFamily
    {
        ModernDotNet,
        NetCoreApp,
        NetStandard,
        NetFramework,
        Any
    }

    public static bool? IsCompatible(
        string projectTargetFramework,
        IEnumerable<string> packageTargetFrameworks)
    {
        var packageFrameworkValues = packageTargetFrameworks.ToList();
        var projectFramework = Parse(projectTargetFramework);
        if (projectFramework == null)
            return null;

        var parsedPackageFrameworks = packageFrameworkValues
            .Select(value => (Value: value, Framework: Parse(value)))
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

    private static ParsedFramework? Parse(string value)
    {
        var normalized = value.Trim();
        if (normalized.Equals("any", StringComparison.OrdinalIgnoreCase))
            return new ParsedFramework(FrameworkFamily.Any, new Version(0, 0));

        return ParseCanonicalOrAlias(normalized) ??
               ParseStandardOrCore(normalized) ??
               ParseModern(normalized) ??
               ParseNetFramework(normalized);
    }

    private static ParsedFramework? ParseCanonicalOrAlias(string value)
    {
        string[] patterns =
        [
            "^\\.(?<family>NETCoreApp|NETStandard|NETFramework),?Version=v?(?<version>\\d+(?:\\.\\d+){1,3})$",
            "^\\.?(?<family>NETCoreApp|NETStandard|NETFramework)(?<version>\\d+(?:\\.\\d+){1,3})$"
        ];
        foreach (var pattern in patterns)
        {
            var match = Regex.Match(value, pattern, RegexOptions.IgnoreCase);
            if (match.Success && Version.TryParse(match.Groups["version"].Value, out var version))
                return new ParsedFramework(ParseFamily(match.Groups["family"].Value, version), version);
        }

        return null;
    }

    private static ParsedFramework? ParseStandardOrCore(string value)
    {
        var match = Regex.Match(
            value,
            "^(?<family>netstandard|netcoreapp)(?<version>\\d+(?:\\.\\d+){1,3})$",
            RegexOptions.IgnoreCase);
        if (!match.Success || !Version.TryParse(match.Groups["version"].Value, out var version))
            return null;

        var family = match.Groups["family"].Value.Equals(
            "netstandard",
            StringComparison.OrdinalIgnoreCase)
            ? FrameworkFamily.NetStandard
            : FrameworkFamily.NetCoreApp;
        return new ParsedFramework(family, version);
    }

    private static ParsedFramework? ParseModern(string value)
    {
        var match = Regex.Match(
            value,
            "^net(?<version>\\d+\\.\\d+)(?:-(?<platform>[a-z]+)(?<platformVersion>\\d+(?:\\.\\d+){0,3})?)?$",
            RegexOptions.IgnoreCase);
        if (!match.Success ||
            !Version.TryParse(match.Groups["version"].Value, out var version) ||
            version.Major < 5)
        {
            return null;
        }

        var platformVersion = match.Groups["platformVersion"].Success &&
                              Version.TryParse(
                                  match.Groups["platformVersion"].Value,
                                  out var parsedPlatformVersion)
            ? parsedPlatformVersion
            : null;
        return new ParsedFramework(
            FrameworkFamily.ModernDotNet,
            version,
            match.Groups["platform"].Success
                ? match.Groups["platform"].Value.ToLowerInvariant()
                : null,
            platformVersion);
    }

    private static ParsedFramework? ParseNetFramework(string value)
    {
        var match = Regex.Match(value, "^net(?<version>\\d{2,4})$", RegexOptions.IgnoreCase);
        if (!match.Success)
            return null;

        var components = match.Groups["version"].Value
            .Select(character => character - '0')
            .ToArray();
        return components.Length switch
        {
            2 => new ParsedFramework(
                FrameworkFamily.NetFramework,
                new Version(components[0], components[1])),
            3 => new ParsedFramework(
                FrameworkFamily.NetFramework,
                new Version(components[0], components[1], components[2])),
            4 => new ParsedFramework(
                FrameworkFamily.NetFramework,
                new Version(
                    components[0],
                    components[1],
                    components[2] * 10 + components[3])),
            _ => null
        };
    }

    private static FrameworkFamily ParseFamily(string value, Version version)
    {
        return value.ToUpperInvariant() switch
        {
            "NETCOREAPP" => version.Major >= 5
                ? FrameworkFamily.ModernDotNet
                : FrameworkFamily.NetCoreApp,
            "NETSTANDARD" => FrameworkFamily.NetStandard,
            "NETFRAMEWORK" => FrameworkFamily.NetFramework,
            _ => throw new InvalidOperationException("Unknown framework family.")
        };
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
