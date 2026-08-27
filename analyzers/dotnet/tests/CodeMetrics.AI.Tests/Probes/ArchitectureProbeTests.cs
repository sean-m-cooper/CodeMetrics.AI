using System.Text.Json;
using CodeMetrics.AI.Metrics;
using CodeMetrics.AI.Probes;
using CodeMetrics.AI.Tests.Helpers;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

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
    public void ConcreteInfrastructureDependency_AliasedInterfaceWithoutIPrefix_NoFinding()
    {
        const string code = """
            using CRM = Contracts;
            namespace Contracts { public interface AccountRepository { } }
            public class CustomerSearchService {
                public CustomerSearchService(CRM.AccountRepository repository) { }
            }
            """;

        var result = Analyze(code);

        result.Findings.Should().NotContain(f => f.Category == "concreteInfrastructureDependency");
    }

    [Fact]
    public void ConcreteInfrastructureDependency_IPrefixedConcreteGateway_FindsFinding()
    {
        const string code = """
            public class ISqlGateway { }
            public class MyService {
                public MyService(ISqlGateway gateway) { }
            }
            """;

        var result = Analyze(code);

        result.Findings.Should().ContainSingle(f => f.Category == "concreteInfrastructureDependency");
    }

    [Fact]
    public void ConcreteInfrastructureDependency_AbstractGateway_NoFinding()
    {
        const string code = """
            public abstract class SqlGateway { }
            public class MyService {
                public MyService(SqlGateway gateway) { }
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
    public void MetricHotspots_ExecutableProgram_ExcludesOnlyHighCoupling()
    {
        var metrics = new List<TypeMetrics>
        {
            new()
            {
                Project = "TestProject",
                Namespace = "",
                Type = "Program",
                FilePath = "Program.cs",
                CyclomaticComplexity = 100,
                ClassCoupling = 85,
                LinesOfSource = 50
            }
        };
        var (_, _, compilation) = RoslynTestHelper.CompileCode(
            "class Program { static void Main() { } }");
        var executableCompilation = compilation.WithOptions(
            ((CSharpCompilationOptions)compilation.Options)
            .WithOutputKind(OutputKind.ConsoleApplication));

        var result = ArchitectureProbe.Analyze(
            [("TestProject", executableCompilation)],
            metrics,
            CreateIsolatedTempDir());

        result.Findings.Should().NotContain(f => f.Category == "highCoupling");
        result.Findings.Should().Contain(f => f.Category == "highCyclomaticComplexity");
        result.Extra["excludedApplicationCompositionRoots"].Should().Be(1);
    }

    [Fact]
    public void HighCouplingEvidence_ListsContributingTypeSymbols()
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
                CoupledTypes = ["MyNs.FirstDependency", "MyNs.SecondDependency"],
                LinesOfSource = 50
            }
        };

        var result = Analyze("class Placeholder { }", metrics);

        var provenance = JsonSerializer.SerializeToElement(
            result.Extra["couplingProvenance"]);
        provenance.GetArrayLength().Should().Be(1);
        provenance[0].GetProperty("CoupledTypes").EnumerateArray()
            .Select(item => item.GetString())
            .Should().Equal("MyNs.FirstDependency", "MyNs.SecondDependency");
    }

    [Fact]
    public void ControllerActionCoupling_IsReportedAsSupplementalEvidence()
    {
        const string code = """
            namespace Microsoft.AspNetCore.Mvc {
                public sealed class FromServicesAttribute : System.Attribute { }
                public sealed class NonActionAttribute : System.Attribute { }
            }
            public interface IControllerDependency { }
            public interface ILogger { }
            public class ActionService { }
            public class Response { }
            public class OrdersController {
                private readonly IControllerDependency dependency;
                public OrdersController(IControllerDependency dependency, ILogger logger) {
                    this.dependency = dependency;
                }
                public Response Save(
                    [Microsoft.AspNetCore.Mvc.FromServices] ActionService service) {
                    _ = dependency;
                    return new Response();
                }
                [Microsoft.AspNetCore.Mvc.NonAction]
                public Response Helper() => new Response();
            }
            """;

        var result = Analyze(code);

        var summaries = JsonSerializer.SerializeToElement(
            result.Extra["controllerActionCoupling"]);
        var summaryItems = summaries.EnumerateArray().ToList();
        summaryItems.Should().ContainSingle();
        var summary = summaryItems[0];
        summary.GetProperty("ConstructorDependencyCount").GetInt32().Should().Be(2);
        summary.GetProperty("MaxFromServicesParameters").GetInt32().Should().Be(1);
        summary.GetProperty("MaxActionTypeCoupling").GetInt32().Should().BeGreaterThanOrEqualTo(3);

        var actions = summary.GetProperty("Actions").EnumerateArray().ToList();
        actions.Should().ContainSingle();
        actions[0].GetProperty("Method").GetString().Should().Be("Save");
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

    [Fact]
    public void MetricHotspots_DependencyInjectionExtensionType_NoHotspotFindings()
    {
        const string code = """
            namespace Microsoft.Extensions.DependencyInjection {
                public interface IServiceCollection { }
            }
            namespace MyNs {
                using Microsoft.Extensions.DependencyInjection;
                public static class ServiceCollectionExtensions {
                    public static IServiceCollection AddInfrastructure(
                        this IServiceCollection services) => services;

                    private static void ConfigureDefaults() { }
                }
            }
            """;
        var metrics = new List<TypeMetrics>
        {
            new()
            {
                Project = "TestProject",
                Namespace = "MyNs",
                Type = "ServiceCollectionExtensions",
                FilePath = "ServiceCollectionExtensions.cs",
                CyclomaticComplexity = 100,
                ClassCoupling = 50,
                LinesOfSource = 600
            }
        };

        var result = Analyze(code, metrics);

        result.Findings.Should().NotContain(f =>
            f.Category == "highCyclomaticComplexity" ||
            f.Category == "highCoupling" ||
            f.Category == "largeClass");
        result.Extra["excludedDependencyInjectionExtensionTypes"].Should().Be(1);
    }

    [Fact]
    public void MetricHotspots_PassiveDataCarrier_NoHotspotFindings()
    {
        var metrics = new List<TypeMetrics>
        {
            new()
            {
                Project = "TestProject",
                Namespace = "MyNs",
                Type = "LargeResponse",
                FilePath = "LargeResponse.cs",
                CyclomaticComplexity = 100,
                ClassCoupling = 50,
                LinesOfSource = 600,
                IsDataCarrier = true
            }
        };

        var result = Analyze("namespace MyNs { public sealed record LargeResponse(string Value); }", metrics);

        result.Findings.Should().NotContain(f =>
            f.Category == "highCyclomaticComplexity" ||
            f.Category == "highCoupling" ||
            f.Category == "largeClass");
        result.Extra["excludedPassiveDataCarriers"].Should().Be(1);
    }

    [Fact]
    public void MetricHotspots_UnrelatedExtensionType_StillFindsHotspot()
    {
        const string code = """
            namespace MyNs {
                public static class StringExtensions {
                    public static string NormalizeValue(this string value) => value.Trim();
                }
            }
            """;
        var metrics = new List<TypeMetrics>
        {
            new()
            {
                Project = "TestProject",
                Namespace = "MyNs",
                Type = "StringExtensions",
                FilePath = "StringExtensions.cs",
                CyclomaticComplexity = 100,
                ClassCoupling = 5,
                LinesOfSource = 50
            }
        };

        var result = Analyze(code, metrics);

        result.Findings.Should().Contain(f => f.Category == "highCyclomaticComplexity");
    }

    [Fact]
    public void MetricHotspots_DiExtensionTypeWithUnrelatedExposedMethod_StillFindsHotspot()
    {
        const string code = """
            namespace Microsoft.Extensions.DependencyInjection {
                public interface IServiceCollection { }
            }
            namespace MyNs {
                using Microsoft.Extensions.DependencyInjection;
                public static class MixedExtensions {
                    public static IServiceCollection AddInfrastructure(
                        this IServiceCollection services) => services;
                    public static void RunMaintenance() { }
                }
            }
            """;
        var metrics = new List<TypeMetrics>
        {
            new()
            {
                Project = "TestProject",
                Namespace = "MyNs",
                Type = "MixedExtensions",
                FilePath = "MixedExtensions.cs",
                CyclomaticComplexity = 5,
                ClassCoupling = 35,
                LinesOfSource = 50
            }
        };

        var result = Analyze(code, metrics);

        result.Findings.Should().Contain(f => f.Category == "highCoupling");
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
    public void Scoring_WithCycles_ReturnsScore0()
    {
        // A cyclic project graph is an absent architecture, not merely a poor one: the
        // dependency direction the layering rules check against does not exist. It gets
        // rung 0 rather than sharing rung 2 with ordinary layering errors.
        var tempDir = CreateCyclicSolution();

        try
        {
            var result = AnalyzeWithDir(tempDir);

            result.Score.Should().Be(0);
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
    public void Scoring_WithHotspotsButNoCycles_ReturnsScore2()
    {
        // Rung 2 stays reachable on its own terms now that cycles have moved to 0.
        var hotspot = new TypeMetrics
        {
            Project = "TestProject",
            Namespace = "MyNs",
            Type = "BigClass",
            FilePath = "BigClass.cs",
            CyclomaticComplexity = 100,
            ClassCoupling = 5,
            LinesOfSource = 50
        };

        var result = Analyze("public class GodClass { }", [hotspot]);

        result.Findings.Should().NotContain(f => f.Category == "projectCycle");
        result.Score.Should().Be(2);
    }

    [Fact]
    public void EveryRungIsReachable_NoLadderGaps()
    {
        // Guards against a rung being unreachable, which is what left 0 missing and made a
        // broken architecture indistinguishable from an untidy one.
        const string errorRung = """
            public class AppDbContext { }
            public class MyController { public MyController(AppDbContext db) { } }
            """;
        const string noisy = """
            public class SqlGateway { }
            public class HttpClientWrapper { }
            public class DataRepository { }
            public class UserService { public UserService(SqlGateway g) { } }
            public class OrderService { public OrderService(HttpClientWrapper c) { } }
            public class ReportService { public ReportService(DataRepository r) { } }
            """;
        const string several = """
            public class SqlGateway { }
            public class HttpClientWrapper { }
            public class UserService { public UserService(SqlGateway g) { } }
            public class OrderService { public OrderService(HttpClientWrapper c) { } }
            """;
        const string minor = """
            public class SqlGateway { }
            public class UserService { public UserService(SqlGateway g) { } }
            """;
        const string clean = """
            public class Money { public int Amount { get; init; } }
            """;

        var cyclicDir = CreateCyclicSolution();
        try
        {
            AnalyzeWithDir(cyclicDir).Score.Should().Be(0);
        }
        finally
        {
            Directory.Delete(cyclicDir, recursive: true);
        }

        Analyze(errorRung).Score.Should().Be(2);
        Analyze(noisy).Score.Should().Be(4);
        Analyze(several).Score.Should().Be(6);
        Analyze(minor).Score.Should().Be(8);
        Analyze(clean).Score.Should().Be(10);
    }

    /// <summary>Two projects referencing each other, in a fresh temp dir the caller deletes.</summary>
    private static string CreateCyclicSolution()
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

        return tempDir;
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
    public void Scoring_With2Warnings_ReturnsScore6()
    {
        const string code = """
            public class SqlGateway { }
            public class HttpClientWrapper { }
            public class UserService { public UserService(SqlGateway g) { } }
            public class OrderService { public OrderService(HttpClientWrapper c) { } }
            """;

        var result = Analyze(code);

        result.Findings.Count(f => f.Severity == "warning").Should().Be(2);
        result.Score.Should().Be(6);
    }

    [Fact]
    public void Scoring_WithSingleWarning_ReturnsScore8()
    {
        const string code = """
            public class SqlGateway { }
            public class UserService { public UserService(SqlGateway g) { } }
            """;

        var result = Analyze(code);

        result.Findings.Count(f => f.Severity == "warning").Should().Be(1);
        result.Score.Should().Be(8);
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
    public void MetricHotspots_ReportsPopulationAndTop10SampleSeparately()
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
            .Should().HaveCount(12);
        result.Extra["hotspotCount"].Should().Be(12);
        result.Extra["hotspotsTruncated"].Should().Be(true);
        ((IReadOnlyCollection<object>)result.Extra["hotspots"]!).Should().HaveCount(10);
        result.Basis.Should().Contain("hotspots: 12 (showing 10)");
    }

    [Fact]
    public void MetricHotspots_FrameworkAuthenticationHandler_SuppressesOnlyCoupling()
    {
        const string code = """
            namespace Microsoft.AspNetCore.Authentication
            {
                public abstract class AuthenticationHandler<TOptions> { }
            }

            namespace MyApp
            {
                public sealed class ApiKeyHandler
                    : Microsoft.AspNetCore.Authentication.AuthenticationHandler<string> { }
            }
            """;
        var metrics = new[]
        {
            new TypeMetrics
            {
                Project = "TestProject",
                Namespace = "MyApp",
                Type = "ApiKeyHandler",
                FilePath = "ApiKeyHandler.cs",
                CyclomaticComplexity = 80,
                ClassCoupling = 40,
                LinesOfSource = 500
            }
        };

        var result = Analyze(code, metrics);

        result.Findings.Should().NotContain(f => f.Category == "highCoupling");
        result.Findings.Should().Contain(f => f.Category == "highCyclomaticComplexity");
        result.Findings.Should().Contain(f => f.Category == "largeClass");
        result.Extra["excludedFrameworkCouplingArchetypeTypes"].Should().Be(1);
    }
}
