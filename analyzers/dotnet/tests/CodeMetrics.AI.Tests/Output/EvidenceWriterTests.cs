using System.Text.Json;
using CodeMetrics.AI.Output;
using FluentAssertions;

namespace CodeMetrics.AI.Tests.Output;

public class EvidenceWriterTests
{
    [Fact]
    public async Task WriteAsync_ProducesValidJson_WithCorrectSchemaVersion()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var model = new EvidenceModel
            {
                Solution = new SolutionInfo { Path = "C:/test/my.sln", Configuration = "Release" },
                Filters = new FilterInfo
                {
                    TotalProjects = 3,
                    AnalyzedProjects = 2,
                    Skipped =
                    [
                        new SkippedProjectInfo { Name = "MyProject.Tests", Reason = "Test project" }
                    ]
                },
                Population = new PopulationInfo { Types = 42, Members = 120 },
                Dimensions = new Dictionary<string, object>
                {
                    ["codeQuality"] = "placeholder"
                }
            };

            await EvidenceWriter.WriteAsync(tempFile, model);

            var json = await File.ReadAllTextAsync(tempFile);
            json.Should().NotBeNullOrWhiteSpace();

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            root.GetProperty("schemaVersion").GetInt32().Should().Be(1);
            root.GetProperty("solution").GetProperty("path").GetString().Should().Be("C:/test/my.sln");
            root.GetProperty("solution").GetProperty("configuration").GetString().Should().Be("Release");
            root.GetProperty("filters").GetProperty("totalProjects").GetInt32().Should().Be(3);
            root.GetProperty("filters").GetProperty("analyzedProjects").GetInt32().Should().Be(2);
            root.GetProperty("population").GetProperty("types").GetInt32().Should().Be(42);
            root.GetProperty("population").GetProperty("members").GetInt32().Should().Be(120);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task WriteAsync_CreatesDirectory_WhenItDoesNotExist()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"evidence-test-{Guid.NewGuid():N}");
        var outputPath = Path.Combine(tempDir, "subdir", "evidence.json");
        try
        {
            var model = new EvidenceModel();

            await EvidenceWriter.WriteAsync(outputPath, model);

            File.Exists(outputPath).Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task WriteAsync_UsedCamelCasePropertyNames()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var model = new EvidenceModel
            {
                Population = new PopulationInfo { Types = 5, Members = 10 }
            };

            await EvidenceWriter.WriteAsync(tempFile, model);

            var json = await File.ReadAllTextAsync(tempFile);

            // Verify camelCase keys exist
            json.Should().Contain("\"schemaVersion\"");
            json.Should().Contain("\"generatedAtUtc\"");
            json.Should().Contain("\"population\"");

            // Should NOT contain PascalCase keys
            json.Should().NotContain("\"SchemaVersion\"");
            json.Should().NotContain("\"GeneratedAtUtc\"");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task WriteAsync_ToolInfo_ContainsNameAndVersion()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var model = new EvidenceModel();

            await EvidenceWriter.WriteAsync(tempFile, model);

            var json = await File.ReadAllTextAsync(tempFile);
            using var doc = JsonDocument.Parse(json);
            var tool = doc.RootElement.GetProperty("tool");

            tool.GetProperty("name").GetString().Should().Be("CodeMetrics.AI");
            tool.GetProperty("version").GetString().Should().NotBeNullOrEmpty();
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}
