using CodeMetrics.AI.Metrics;
using CodeMetrics.AI.Probes;
using CodeMetrics.AI.Tests.Helpers;
using FluentAssertions;
using Microsoft.CodeAnalysis;

namespace CodeMetrics.AI.Tests.Probes;

public class ArchitectureProbeTests
{
    private static DimensionResult Analyze(
        string code,
        IReadOnlyList<TypeMetrics>? metrics = null,
        string? solutionDir = null)
    {
        var (_, _, compilation) = RoslynTestHelper.CompileCode(code);
        var projects = new List<(string, Compilation)> { ("TestProject", compilation) };
        return ArchitectureProbe.Analyze(
            projects,
            metrics ?? [],
            solutionDir ?? CreateIsolatedTempDir());
    }

    private static DimensionResult AnalyzeWithDir(
        string solutionDir,
        IReadOnlyList<TypeMetrics>? metrics = null)
    {
        var projects = new List<(string, Compilation)>();
        return ArchitectureProbe.Analyze(
            projects,
            metrics ?? [],
            solutionDir);
    }

    private static string CreateIsolatedTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        return dir;
    }

    // ── 1. Cycle detection ────────────────────────────────────────────────────

    [Fact]
    public void CycleDetection_TwoProjectsReferencingEachOther_FindsProjectCycle()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(Path.Combine(tempDir, "A"));
        Directory.CreateDirectory(Path.Combine(tempDir, "B"));

        var projA = """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup><ProjectReference Include="..\B\B.csproj" /></ItemGroup>
            </Project>
            """;

        var projB = """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup><ProjectReference Include="..\A\A.csproj" /></ItemGroup>
            </Project>
            """;

        File.WriteAllText(Path.Combine(tempDir, "A", "A.csproj"), projA);
        File.WriteAllText(Path.Combine(tempDir, "B", "B.csproj"), projB);

        try
        {
            var result = AnalyzeWithDir(tempDir);

            result.Findings.Should().Contain(f => f.Category == "projectCycle");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void CycleDetection_TwoProjects_CycleSeverityIsError()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(Path.Combine(tempDir, "A"));
        Directory.CreateDirectory(Path.Combine(tempDir, "B"));

        var projA = """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup><ProjectReference Include="..\B\B.csproj" /></ItemGroup>
            </Project>
            """;

        var projB = """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup><ProjectReference Include="..\A\A.csproj" /></ItemGroup>
            </Project>
            """;

        File.WriteAllText(Path.Combine(tempDir, "A", "A.csproj"), projA);
        File.WriteAllText(Path.Combine(tempDir, "B", "B.csproj"), projB);

        try
        {
            var result = AnalyzeWithDir(tempDir);

            result.Findings.Where(f => f.Category == "projectCycle")
                .Should().AllSatisfy(f => f.Severity.Should().Be("error"));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void CycleDetection_SingleProjectNoRefs_NoCycleFindings()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(Path.Combine(tempDir, "A"));

        var projA = """
            <Project Sdk="Microsoft.NET.Sdk">
            </Project>
            """;

        File.WriteAllText(Path.Combine(tempDir, "A", "A.csproj"), projA);

        try
        {
            var result = AnalyzeWithDir(tempDir);

            result.Findings.Should().NotContain(f => f.Category == "projectCycle");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    // ── 2. Controller data dependency ─────────────────────────────────────────

    [Fact]
    public void ControllerDataDependency_ControllerWithDbContext_FindsControllerDataDependency()
    {
        const string code = """
            public class AppDbContext { }
            public class MyController {
                public MyController(AppDbContext db) { }
            }
            """;

        var result = Analyze(code);

        result.Findings.Should().Contain(f => f.Category == "controllerDataDependency");
    }

    [Fact]
    public void ControllerDataDependency_SeverityIsError()
    {
        const string code = """
            public class AppDbContext { }
            public class MyController {
                public MyController(AppDbContext db) { }
            }
            """;

        var result = Analyze(code);

        result.Findings.Where(f => f.Category == "controllerDataDependency")
            .Should().AllSatisfy(f => f.Severity.Should().Be("error"));
    }

    [Fact]
    public void ControllerDataDependency_ControllerWithRepositoryParam_FindsControllerDataDependency()
    {
        const string code = """
            public class UserRepository { }
            public class UserController {
                public UserController(UserRepository repo) { }
            }
            """;

        var result = Analyze(code);

        result.Findings.Should().Contain(f => f.Category == "controllerDataDependency");
    }

    [Fact]
    public void ControllerDataDependency_ControllerWithILoggerOnly_NoCrossCuttingViolation()
    {
        const string code = """
            public interface ILogger<T> { }
            public class MyController {
                public MyController(ILogger<MyController> logger) { }
            }
            """;

        var result = Analyze(code);

        result.Findings.Should().NotContain(f => f.Category == "controllerDataDependency");
    }

    // ── 3. Service with concrete infrastructure dependency ────────────────────

    [Fact]
    public void ConcreteInfrastructureDependency_ServiceWithSqlGateway_FindsConcreteInfrastructureDependency()
    {
        const string code = """
            public class SqlGateway { }
            public class MyService {
                public MyService(SqlGateway gateway) { }
            }
            """;

        var result = Analyze(code);

        result.Findings.Should().Contain(f => f.Category == "concreteInfrastructureDependency");
    }

    [Fact]
    public void ConcreteInfrastructureDependency_SeverityIsWarning()
    {
        const string code = """
            public class SqlGateway { }
            public class MyService {
                public MyService(SqlGateway gateway) { }
            }
            """;

        var result = Analyze(code);

        result.Findings.Where(f => f.Category == "concreteInfrastructureDependency")
            .Should().AllSatisfy(f => f.Severity.Should().Be("warning"));
    }

    [Fact]
    public void ConcreteInfrastructureDependency_ServiceWithInterfaceGateway_NoFinding()
    {
        const string code = """
            public interface ISqlGateway { }
            public class MyService {
                public MyService(ISqlGateway gateway) { }
            }
            """;

        var result = Analyze(code);

        result.Findings.Should().NotContain(f => f.Category == "concreteInfrastructureDependency");
    }

    [Fact]
    public void ConcreteInfrastructureDependency_ServiceWithMicrosoftExtensionsContext_NoFinding()
    {
        const string code = """
            namespace Microsoft.Extensions.Hosting { public class HostContext { } }
            public class MyService {
                public MyService(Microsoft.Extensions.Hosting.HostContext context) { }
            }
            """;

        var result = Analyze(code);

        result.Findings.Should().NotContain(f => f.Category == "concreteInfrastructureDependency");
    }

    [Fact]
    public void ConcreteInfrastructureDependency_ServiceWithAspNetCoreHttpContext_NoFinding()
    {
        const string code = """
            namespace Microsoft.AspNetCore.Http { public class HttpContext { } }
            public class MyService {
                public MyService(Microsoft.AspNetCore.Http.HttpContext context) { }
            }
            """;

        var result = Analyze(code);

        result.Findings.Should().NotContain(f => f.Category == "concreteInfrastructureDependency");
    }

    // ── 4. Metric hotspots ────────────────────────────────────────────────────

    [Fact]
    public void MetricHotspots_HighCyclomaticComplexity_FindsHighCyclomaticComplexity()
    {
        var metrics = new List<TypeMetrics>
        {
            new()
            {
                Project = "TestProject",
                Namespace = "MyNs",
                Type = "BigClass",
                FilePath = "BigClass.cs",
                CyclomaticComplexity = 100,
                ClassCoupling = 5,
                LinesOfSource = 50
            }
        };

        const string code = "class Placeholder { }";
        var result = Analyze(code, metrics);

        result.Findings.Should().Contain(f => f.Category == "highCyclomaticComplexity");
    }

    [Fact]
    public void MetricHotspots_HighCyclomaticComplexity_SeverityIsWarning()
    {
        var metrics = new List<TypeMetrics>
        {
            new()
            {
                Project = "TestProject",
                Namespace = "MyNs",
                Type = "BigClass",
                FilePath = "BigClass.cs",
                CyclomaticComplexity = 100,
                ClassCoupling = 5,
                LinesOfSource = 50
            }
        };

        const string code = "class Placeholder { }";
        var result = Analyze(code, metrics);

        result.Findings.Where(f => f.Category == "highCyclomaticComplexity")
            .Should().AllSatisfy(f => f.Severity.Should().Be("warning"));
    }

    [Fact]
    public void MetricHotspots_HighCoupling_FindsHighCoupling()
    {
        var metrics = new List<TypeMetrics>
        {
            new()
            {
                Project = "TestProject",
                Namespace = "MyNs",
                Type = "CoupledClass",
                FilePath = "CoupledClass.cs",
                CyclomaticComplexity = 5,
                ClassCoupling = 35,
                LinesOfSource = 50
            }
        };

        const string code = "class Placeholder { }";
        var result = Analyze(code, metrics);

        result.Findings.Should().Contain(f => f.Category == "highCoupling");
    }

    [Fact]
    public void MetricHotspots_ControllerUsesHigherCouplingThreshold()
    {
        var metrics = new List<TypeMetrics>
        {
            new()
            {
                Project = "TestProject",
                Namespace = "MyNs",
                Type = "OrdersController",
                FilePath = "OrdersController.cs",
                CyclomaticComplexity = 5,
                ClassCoupling = 35,
                LinesOfSource = 50
            }
        };

        var result = Analyze("class Placeholder { }", metrics);

        result.Findings.Should().NotContain(f => f.Category == "highCoupling");
    }

    [Fact]
    public void MetricHotspots_LargeClass_FindsLargeClass()
    {
        var metrics = new List<TypeMetrics>
        {
            new()
            {
                Project = "TestProject",
                Namespace = "MyNs",
                Type = "HugeClass",
                FilePath = "HugeClass.cs",
                CyclomaticComplexity = 5,
                ClassCoupling = 5,
                LinesOfSource = 600
            }
        };

        const string code = "class Placeholder { }";
        var result = Analyze(code, metrics);

        result.Findings.Should().Contain(f => f.Category == "largeClass");
    }

    [Fact]
    public void MetricHotspots_BelowThresholds_NoHotspotFindings()
    {
        var metrics = new List<TypeMetrics>
        {
            new()
            {
                Project = "TestProject",
                Namespace = "MyNs",
                Type = "CleanClass",
                FilePath = "CleanClass.cs",
                CyclomaticComplexity = 10,
                ClassCoupling = 5,
                LinesOfSource = 100
            }
        };

        const string code = "class Placeholder { }";
        var result = Analyze(code, metrics);

        result.Findings.Should().NotContain(f =>
            f.Category == "highCyclomaticComplexity" ||
            f.Category == "highCoupling" ||
            f.Category == "largeClass");
    }

    // ── 5. Clean code / scoring ───────────────────────────────────────────────

    [Fact]
    public void CleanCode_NoFindings_ReturnsScore10()
    {
        const string code = """
            public class CleanService {
                private readonly IUserService _svc;
                public CleanService(IUserService svc) { _svc = svc; }
            }
            public interface IUserService { }
            """;

        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
            var (_, _, compilation) = RoslynTestHelper.CompileCode(code);
            var projects = new List<(string, Compilation)> { ("TestProject", compilation) };
            var result = ArchitectureProbe.Analyze(projects, [], tempDir);

            result.Score.Should().Be(10);
            result.Findings.Should().BeEmpty();
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void Scoring_WithCycles_ReturnsScore2()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(Path.Combine(tempDir, "A"));
        Directory.CreateDirectory(Path.Combine(tempDir, "B"));

        File.WriteAllText(Path.Combine(tempDir, "A", "A.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup><ProjectReference Include="..\B\B.csproj" /></ItemGroup>
            </Project>
            """);

        File.WriteAllText(Path.Combine(tempDir, "B", "B.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup><ProjectReference Include="..\A\A.csproj" /></ItemGroup>
            </Project>
            """);

        try
        {
            var result = AnalyzeWithDir(tempDir);

            result.Score.Should().Be(2);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void Scoring_WithErrors_ReturnsScore2()
    {
        const string code = """
            public class AppDbContext { }
            public class MyController {
                public MyController(AppDbContext db) { }
            }
            """;

        var result = Analyze(code);

        result.Score.Should().Be(2);
    }

    [Fact]
    public void Scoring_WithWarningsMoreThan2_ReturnsScore4()
    {
        // Three services with concrete infrastructure dependencies
        const string code = """
            public class SqlGateway { }
            public class HttpClientWrapper { }
            public class DataRepository { }
            public class UserService { public UserService(SqlGateway g) { } }
            public class OrderService { public OrderService(HttpClientWrapper c) { } }
            public class ReportService { public ReportService(DataRepository r) { } }
            """;

        var result = Analyze(code);

        result.Score.Should().Be(4);
    }

    [Fact]
    public void Scoring_With1To2Warnings_ReturnsScore6()
    {
        const string code = """
            public class SqlGateway { }
            public class UserService { public UserService(SqlGateway g) { } }
            """;

        var result = Analyze(code);

        result.Score.Should().Be(6);
    }

    // ── 6. Extra data ─────────────────────────────────────────────────────────

    [Fact]
    public void Result_HasCyclesInExtraData()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(Path.Combine(tempDir, "A"));
        Directory.CreateDirectory(Path.Combine(tempDir, "B"));

        File.WriteAllText(Path.Combine(tempDir, "A", "A.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup><ProjectReference Include="..\B\B.csproj" /></ItemGroup>
            </Project>
            """);

        File.WriteAllText(Path.Combine(tempDir, "B", "B.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup><ProjectReference Include="..\A\A.csproj" /></ItemGroup>
            </Project>
            """);

        try
        {
            var result = AnalyzeWithDir(tempDir);

            result.Extra.Should().ContainKey("cycles");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void Result_HasHotspotsInExtraData()
    {
        const string code = "class Placeholder { }";

        var result = Analyze(code);

        result.Extra.Should().ContainKey("hotspots");
    }

    [Fact]
    public void Result_StatusIsScored()
    {
        const string code = "class Placeholder { }";

        var result = Analyze(code);

        result.Status.Should().Be("scored");
    }

    // ── 7. Edge cases ─────────────────────────────────────────────────────────

    [Fact]
    public void EmptyProjectList_EmptyMetrics_EmptyDir_ReturnsScore10()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
            var result = ArchitectureProbe.Analyze([], [], tempDir);

            result.Status.Should().Be("scored");
            result.Score.Should().Be(10);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void MetricHotspots_LimitedToTop10()
    {
        // Create 12 classes each with CC >= 80
        var metrics = Enumerable.Range(1, 12)
            .Select(i => new TypeMetrics
            {
                Project = "TestProject",
                Namespace = "MyNs",
                Type = $"BigClass{i}",
                FilePath = $"BigClass{i}.cs",
                CyclomaticComplexity = 80 + i,
                ClassCoupling = 5,
                LinesOfSource = 50
            })
            .ToList();

        const string code = "class Placeholder { }";
        var result = Analyze(code, metrics);

        result.Findings.Where(f => f.Category == "highCyclomaticComplexity")
            .Should().HaveCountLessThanOrEqualTo(10);
    }
}
