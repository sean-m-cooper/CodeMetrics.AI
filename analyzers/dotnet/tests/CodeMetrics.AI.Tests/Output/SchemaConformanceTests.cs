using System.Text.Json;
using CodeMetrics.AI.Output;
using CodeMetrics.AI.Probes;
using FluentAssertions;
using Json.Schema;

namespace CodeMetrics.AI.Tests.Output;

public class SchemaConformanceTests
{
    private static readonly Lazy<Task<JsonSchema>> Schema = new(LoadSchemaCoreAsync);

    [Theory]
    [InlineData("dotnet-evidence.json")]
    [InlineData("javascript-typescript-evidence.json")]
    public async Task SharedExamples_ValidateAgainstEvidenceSchemaV2(string exampleFile)
    {
        var schema = await LoadSchemaAsync();
        var examplePath = Path.Combine(
            FindRepositoryRoot(),
            "shared",
            "scorecard-schema",
            "examples",
            exampleFile);

        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(examplePath));
        var result = schema.Evaluate(json.RootElement, new EvaluationOptions
        {
            OutputFormat = OutputFormat.List
        });

        result.IsValid.Should().BeTrue(result.ToString());
    }

    [Fact]
    public async Task GeneratedDotnetEvidence_ValidatesAgainstEvidenceSchemaV2()
    {
        var schema = await LoadSchemaAsync();
        var model = new EvidenceModel
        {
            Subject = new SubjectInfo
            {
                Root = "/repo",
                EntryPoint = "/repo/MyApp.slnx",
                Name = "MyApp",
                Variant = "Debug"
            },
            Filters = new FilterInfo
            {
                TotalUnits = 1,
                AnalyzedUnits = 1
            },
            Population = new PopulationInfo
            {
                Types = 1,
                Members = 1
            },
            Dimensions = BuildDimensions()
        };

        var tempFile = Path.GetTempFileName();
        try
        {
            await EvidenceWriter.WriteAsync(tempFile, model);
            using var json = JsonDocument.Parse(await File.ReadAllTextAsync(tempFile));

            var result = schema.Evaluate(json.RootElement, new EvaluationOptions
            {
                OutputFormat = OutputFormat.List
            });

            result.IsValid.Should().BeTrue(result.ToString());
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    private static async Task<JsonSchema> LoadSchemaAsync()
    {
        return await Schema.Value;
    }

    private static async Task<JsonSchema> LoadSchemaCoreAsync()
    {
        var schemaPath = Path.Combine(
            FindRepositoryRoot(),
            "shared",
            "scorecard-schema",
            "evidence.schema.v2.json");

        return JsonSchema.FromText(await File.ReadAllTextAsync(schemaPath));
    }

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "shared", "scorecard-schema")))
                return dir.FullName;

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not find repository root containing shared/scorecard-schema.");
    }

    private static Dictionary<string, object> BuildDimensions()
    {
        DimensionResult Scored(string basis) => new()
        {
            Status = "scored",
            Score = 10,
            Basis = basis
        };

        return new Dictionary<string, object>
        {
            ["codeQuality"] = Scored("Example."),
            ["maintainability"] = Scored("Example."),
            ["errorHandling"] = Scored("Example."),
            ["performanceAsync"] = Scored("Example."),
            ["security"] = Scored("Example."),
            ["testing"] = Scored("Example."),
            ["documentation"] = Scored("Example."),
            ["dependencyManagement"] = new DimensionResult
            {
                Status = "skipped",
                Basis = "Skipped for test."
            },
            ["architecture"] = Scored("Example.")
        };
    }
}
