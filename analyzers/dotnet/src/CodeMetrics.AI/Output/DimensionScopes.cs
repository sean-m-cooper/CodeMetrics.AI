using CodeMetrics.AI.Probes;

namespace CodeMetrics.AI.Output;

internal static class DimensionScopes
{
    public static void Apply(Dictionary<string, object> dimensions)
    {
        var includes = new Dictionary<string, string[]>
        {
            ["codeQuality"] = ["production-type-complexity", "member-complexity"],
            ["maintainability"] = ["production-type-maintainability-index"],
            ["errorHandling"] = ["static-exception-patterns"],
            ["performanceAsync"] = ["static-async-and-blocking-patterns"],
            ["security"] = ["static-security-patterns", "dependency-vulnerability-observations"],
            ["testing"] = ["test-project-signals", "supplied-coverage-report"],
            ["documentation"] = ["documentation-presence-and-content-signals"],
            ["dependencyManagement"] = ["package-version-and-feed-observations"],
            ["architecture"] = ["static-coupling-and-project-structure"]
        };
        foreach (var (key, value) in dimensions)
        {
            var result = (DimensionResult)value;
            var functionMi = key == "maintainability" &&
                result.Extra.GetValueOrDefault("measurementPolicy") as string == FunctionMaintainabilityProbe.MeasurementPolicy;
            result.Extra["scope"] = new
            {
                id = $"dotnet/{key}/v1",
                coverage = "partial",
                includes = functionMi ? ["production-executable-function-maintainability-index"] : includes[key],
                excludes = new[] { "runtime-behavior", "comprehensive-human-review" }
            };
        }
    }
}
