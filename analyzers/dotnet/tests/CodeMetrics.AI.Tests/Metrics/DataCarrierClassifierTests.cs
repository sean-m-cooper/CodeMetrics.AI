using CodeMetrics.AI.Metrics;
using CodeMetrics.AI.Tests.Helpers;
using FluentAssertions;

namespace CodeMetrics.AI.Tests.Metrics;

public class DataCarrierClassifierTests
{
    private static readonly string Root =
        Path.GetFullPath(Path.Combine(Path.GetTempPath(), "CodeMetricsDataCarrierRoot"));

    private static TypeMetrics MetricFor(string code, string typeName)
    {
        var file = Path.Combine(Root, $"{typeName}.cs");
        var (_, _, compilation) = RoslynTestHelper.CompileCodeAtPath(code, file);
        var (types, _) = MetricsCollector.Collect("TestProject", compilation, Root);
        return types.Single(type => type.Type == typeName);
    }

    [Fact]
    public void PositionalRecord_IsDataCarrier()
    {
        var metric = MetricFor(
            "public sealed record SearchRequest(string Query, int Limit);",
            "SearchRequest");

        metric.IsDataCarrier.Should().BeTrue();
    }

    [Fact]
    public void DataCarrier_RetainsRawCouplingMetric()
    {
        const string code = """
            public sealed class SearchFilter { }
            public sealed record SearchRequest(SearchFilter Filter);
            """;

        var metric = MetricFor(code, "SearchRequest");

        metric.IsDataCarrier.Should().BeTrue();
        metric.ClassCoupling.Should().BeGreaterThan(0);
    }

    [Fact]
    public void AutoPropertyClassWithAttributes_IsDataCarrier()
    {
        const string code = """
            public sealed class SearchResponse
            {
                [System.Obsolete]
                public string Name { get; init; } = "";
                public int Rank { get; init; }
            }
            """;

        MetricFor(code, "SearchResponse").IsDataCarrier.Should().BeTrue();
    }

    [Fact]
    public void CommonPassiveInitializers_AreDataCarriers()
    {
        const string code = """
            public sealed class SearchResponse
            {
                public string Name { get; init; } = default!;
                public string[] Values { get; init; } = System.Array.Empty<string>();
            }
            """;

        MetricFor(code, "SearchResponse").IsDataCarrier.Should().BeTrue();
    }

    [Fact]
    public void AssignmentOnlyConstructor_IsDataCarrier()
    {
        const string code = """
            public sealed class SearchRequest
            {
                public SearchRequest(string query) { Query = query; }
                public string Query { get; }
            }
            """;

        MetricFor(code, "SearchRequest").IsDataCarrier.Should().BeTrue();
    }

    [Fact]
    public void ComputedProperty_IsNotDataCarrier()
    {
        const string code = """
            public sealed class SearchResponse
            {
                public string FirstName { get; init; } = "";
                public string LastName { get; init; } = "";
                public string DisplayName => FirstName + " " + LastName;
            }
            """;

        MetricFor(code, "SearchResponse").IsDataCarrier.Should().BeFalse();
    }

    [Fact]
    public void BehaviorMethod_IsNotDataCarrier()
    {
        const string code = """
            public sealed class SearchRequest
            {
                public string Query { get; init; } = "";
                public string Normalize() => Query.Trim();
            }
            """;

        MetricFor(code, "SearchRequest").IsDataCarrier.Should().BeFalse();
    }

    [Fact]
    public void ConstructorValidationLogic_IsNotDataCarrier()
    {
        const string code = """
            public sealed class SearchRequest
            {
                public SearchRequest(string query)
                {
                    System.ArgumentException.ThrowIfNullOrEmpty(query);
                    Query = query;
                }

                public string Query { get; }
            }
            """;

        MetricFor(code, "SearchRequest").IsDataCarrier.Should().BeFalse();
    }
}
