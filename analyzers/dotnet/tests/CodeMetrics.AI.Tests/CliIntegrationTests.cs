using System.Diagnostics;
using System.Text.Json;
using FluentAssertions;

namespace CodeMetrics.AI.Tests;

public class CliIntegrationTests
{
    [Fact]
    public async Task Cli_ValidatesInput_HonorsConfiguration_AndReportsIncompleteCompilations()
    {
        var root = Path.Combine(Path.GetTempPath(), "codemetrics-cli-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var repository = new DirectoryInfo(AppContext.BaseDirectory);
            while (repository != null && !Directory.Exists(Path.Combine(repository.FullName, "shared", "scorecard-schema"))) repository = repository.Parent;
            repository.Should().NotBeNull();
            var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
            var tool = Path.Combine(repository!.FullName, "analyzers", "dotnet", "src", "CodeMetrics.AI", "bin", configuration, "net10.0", "CodeMetrics.AI.dll");
            var missing = await Run(root, tool, "--solution", "missing.slnx");
            missing.Code.Should().Be(2, missing.Output);
            await File.WriteAllTextAsync(Path.Combine(root, "Sample.slnx"), "<Solution><Project Path='Sample.csproj'/></Solution>", TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(Path.Combine(root, "Sample.csproj"), "<Project Sdk='Microsoft.NET.Sdk'><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>", TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(Path.Combine(root, "Sample.cs"), """
                public class Sample
                {
                    public int Always() => 1;
                #if DEBUG
                    public int DebugOnly() => 2;
                #endif
                }
                """, TestContext.Current.CancellationToken);
            var restore = await Run(root, "restore", "Sample.csproj", "--ignore-failed-sources");
            restore.Code.Should().Be(0, restore.Output);
            async Task<JsonDocument> Analyze(string variant)
            {
                var result = await Run(root, tool, "--solution", "Sample.slnx", "--configuration", variant, "--skip-dependency-probe", "--scorecard-output", variant + ".json");
                result.Code.Should().Be(0, result.Output);
                return JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root, variant + ".json"), TestContext.Current.CancellationToken));
            }
            using var debug = await Analyze("Debug");
            using var release = await Analyze("Release");
            var debugId = debug.RootElement.GetProperty("analysis").GetProperty("runId").GetString();
            Guid.TryParseExact(debugId, "D", out _).Should().BeTrue();
            release.RootElement.GetProperty("analysis").GetProperty("runId").GetString().Should().NotBe(debugId);
            var suppliedRunId = Guid.NewGuid().ToString("D");
            var suppliedAuditId = Guid.NewGuid().ToString("D");
            debug.RootElement.GetProperty("population").GetProperty("members").GetInt32().Should().Be(2);
            release.RootElement.GetProperty("population").GetProperty("members").GetInt32().Should().Be(1);
            var projectResult = await Run(root, tool, "--solution", "Sample.csproj", "--configuration", "Release", "--skip-dependency-probe", "--scorecard-output", "project.json", "--run-id", suppliedRunId, "--audit-id", suppliedAuditId);
            projectResult.Code.Should().Be(0, projectResult.Output);
            using var projectEvidence = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root, "project.json"), TestContext.Current.CancellationToken));
            projectEvidence.RootElement.GetProperty("subject").GetProperty("entryPoint").GetString().Should().EndWith("Sample.csproj");
            projectEvidence.RootElement.GetProperty("population").GetProperty("members").GetInt32().Should().Be(1);
            projectEvidence.RootElement.GetProperty("analysis").GetProperty("runId").GetString().Should().Be(suppliedRunId);
            projectEvidence.RootElement.GetProperty("analysis").GetProperty("auditId").GetString().Should().Be(suppliedAuditId);
            (await Run(root, tool, "--run-id", "invalid")).Code.Should().Be(2);
            projectEvidence.RootElement.GetProperty("dimensions").GetProperty("performanceAsync").GetProperty("scope").GetProperty("coverage").GetString().Should().Be("partial");
            await File.WriteAllTextAsync(Path.Combine(root, "Other.slnx"), "<Solution/>", TestContext.Current.CancellationToken);
            (await Run(root, tool)).Code.Should().Be(2);
            await File.AppendAllTextAsync(Path.Combine(root, "Sample.cs"), "\npublic class Broken { MissingType value; }", TestContext.Current.CancellationToken);
            var incomplete = await Run(root, tool, "--solution", "Sample.slnx", "--skip-dependency-probe", "--scorecard-output", "failed.json");
            incomplete.Code.Should().Be(2, incomplete.Output);
            using var failed = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root, "failed.json"), TestContext.Current.CancellationToken));
            failed.RootElement.GetProperty("analysis").GetProperty("status").GetString().Should().Be("incomplete");
            failed.RootElement.GetProperty("dimensions").GetProperty("codeQuality").TryGetProperty("score", out _).Should().BeFalse();
            Directory.CreateDirectory(Path.Combine(root, ".git"));
            Directory.CreateDirectory(Path.Combine(root, "nested"));
            File.Copy(Path.Combine(root, "Sample.csproj"), Path.Combine(root, "nested", "Sample.csproj"));
            File.Copy(Path.Combine(root, "Sample.slnx"), Path.Combine(root, "nested", "Sample.slnx"));
            await File.WriteAllTextAsync(Path.Combine(root, "nested", "Source.cs"), "using System; public class Nested { public void Run() { try { throw new Exception(); } catch { } } }", TestContext.Current.CancellationToken);
            (await Run(root, "restore", "nested/Sample.csproj", "--ignore-failed-sources")).Code.Should().Be(0);
            var nestedResult = await Run(root, tool, "--solution", "nested/Sample.slnx", "--skip-dependency-probe", "--scorecard-output", "nested.json");
            nestedResult.Code.Should().Be(0, nestedResult.Output);
            using var nested = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root, "nested.json"), TestContext.Current.CancellationToken));
            nested.RootElement.GetProperty("subject").GetProperty("root").GetString().Should().Be(root);
            nested.RootElement.GetProperty("dimensions").GetProperty("errorHandling").GetProperty("findings").EnumerateArray()
                .First(finding => finding.GetProperty("category").GetString() == "emptyCatch")
                .GetProperty("file").GetString().Should().Be("nested/Source.cs");
            Directory.CreateDirectory(Path.Combine(root, "referenced"));
            File.Copy(Path.Combine(root, "Sample.csproj"), Path.Combine(root, "referenced", "Referenced.csproj"));
            var referencedSource = Path.Combine(root, "referenced", "Referenced.cs");
            await File.WriteAllTextAsync(referencedSource, "public class Referenced { }", TestContext.Current.CancellationToken);
            var nestedProject = Path.Combine(root, "nested", "Sample.csproj");
            var projectXml = await File.ReadAllTextAsync(nestedProject, TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(nestedProject, projectXml.Replace("</Project>", "<ItemGroup><ProjectReference Include='../referenced/Referenced.csproj'/></ItemGroup></Project>"), TestContext.Current.CancellationToken);
            (await Run(root, "restore", nestedProject, "--ignore-failed-sources")).Code.Should().Be(0);
            var overwriteReference = await Run(root, tool, "--solution", nestedProject, "--skip-dependency-probe", "--output", referencedSource);
            overwriteReference.Code.Should().Be(2, overwriteReference.Output);
            (await File.ReadAllTextAsync(referencedSource, TestContext.Current.CancellationToken)).Should().Be("public class Referenced { }");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static async Task<(int Code, string Output)> Run(string root, params string[] arguments)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(90));
        var start = new ProcessStartInfo("dotnet") { WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var error = process.StandardError.ReadToEndAsync(timeout.Token);
        try { await process.WaitForExitAsync(timeout.Token); }
        catch { if (!process.HasExited) process.Kill(entireProcessTree: true); throw; }
        return (process.ExitCode, await output + await error);
    }
}
