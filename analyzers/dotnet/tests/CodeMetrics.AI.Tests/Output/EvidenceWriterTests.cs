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
                Subject = new SubjectInfo
                {
                    Root = "C:/test",
                    EntryPoint = "C:/test/my.sln",
                    Name = "my",
                    Variant = "Release"
                },
                Filters = new FilterInfo
                {
                    TotalUnits = 3,
                    AnalyzedUnits = 2,
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

            root.GetProperty("schemaVersion").GetInt32().Should().Be(2);
            root.GetProperty("subject").GetProperty("root").GetString().Should().Be("C:/test");
            root.GetProperty("subject").GetProperty("entryPoint").GetString().Should().Be("C:/test/my.sln");
            root.GetProperty("subject").GetProperty("name").GetString().Should().Be("my");
            root.GetProperty("subject").GetProperty("variant").GetString().Should().Be("Release");
            root.GetProperty("filters").GetProperty("totalUnits").GetInt32().Should().Be(3);
            root.GetProperty("filters").GetProperty("analyzedUnits").GetInt32().Should().Be(2);
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
            tool.GetProperty("ecosystem").GetString().Should().Be("dotnet");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task WriteAsync_OmitsScore_ForSkippedDimensions()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var model = new EvidenceModel
            {
                Dimensions = new Dictionary<string, object>
                {
                    ["dependencyManagement"] = new
                    {
                        status = "skipped",
                        basis = "Skipped for test.",
                        findings = Array.Empty<object>()
                    }
                }
            };

            await EvidenceWriter.WriteAsync(tempFile, model);

            var json = await File.ReadAllTextAsync(tempFile);
            using var doc = JsonDocument.Parse(json);
            var dimension = doc.RootElement
                .GetProperty("dimensions")
                .GetProperty("dependencyManagement");

            dimension.GetProperty("status").GetString().Should().Be("skipped");
            dimension.TryGetProperty("score", out _).Should().BeFalse();
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}
