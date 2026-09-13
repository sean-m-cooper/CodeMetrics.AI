using System.Text.RegularExpressions;

namespace CodeMetrics.AI.Probes;

internal static class TargetFrameworkParser
{
    public static ParsedFramework? Parse(string value)
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
            if (MatchedVersion(match) is { } version)
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
        if (MatchedVersion(match) is not { } version)
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
        if (MatchedVersion(match) is not { } version ||
            version.Major < 5)
        {
            return null;
        }

        return new ParsedFramework(
            FrameworkFamily.ModernDotNet,
            version,
            match.Groups["platform"].Success
                ? match.Groups["platform"].Value.ToLowerInvariant()
                : null,
            OptionalVersion(match.Groups["platformVersion"].Value));
    }

    private static Version? MatchedVersion(Match match)
    {
        return match.Success ? OptionalVersion(match.Groups["version"].Value) : null;
    }

    private static Version? OptionalVersion(string value)
    {
        return Version.TryParse(value, out var version) ? version : null;
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

}
