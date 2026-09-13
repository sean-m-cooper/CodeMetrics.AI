using System.Diagnostics;
using System.Text.Json;
using CodeMetrics.AI.Probes;
using CodeMetrics.AI.Tests.Helpers;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CodeMetrics.AI.Tests.Probes;

public class DependencyCommandTests
{
    [Fact]
    public void StartInfo_PreservesWorkingDirectoryJsonFlagsAndSdkIsolation()
    {
        var start = DependencyCommandRunner.CreateStartInfo("path with spaces/App.slnx", "--outdated", "workspace");
        start.FileName.Should().Be("dotnet");
        start.Arguments.Should().Be("list \"path with spaces/App.slnx\" package --outdated --format json --output-version 1");
        start.WorkingDirectory.Should().Be("workspace");
        start.RedirectStandardOutput.Should().BeTrue();
        start.RedirectStandardError.Should().BeTrue();
        start.UseShellExecute.Should().BeFalse();
        start.CreateNoWindow.Should().BeTrue();
        start.Environment.Keys.Should().NotContain(["MSBUILD_EXE_PATH", "MSBuildSDKsPath", "MSBuildExtensionsPath"]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    public async Task Capture_DrainsBothStreamsWithoutInterpretingTheirContents(int exitCode)
    {
        await using var child = new ChildProgram();
        var result = await DependencyCommandRunner.CaptureAsync(child.Start(exitCode.ToString()),
            "--outdated", TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);

        result.Arguments.Should().Be("--outdated");
        result.ExitCode.Should().Be(exitCode);
        result.StandardOutput.Should().Be(new string('o', 131072));
        result.StandardError.Should().Be(new string('e', 131072));
        result.ExceptionType.Should().BeNull();
        result.Failed.Should().Be(exitCode != 0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Capture_TimeoutAndCallerCancellationTerminateTheChild(bool callerCancels)
    {
        await using var child = new ChildProgram();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var capture = DependencyCommandRunner.CaptureAsync(child.Start("wait"), "--outdated",
            TimeSpan.FromSeconds(callerCancels ? 15 : 5), cancellation.Token);
        var assertion = async () => await capture;
        if (callerCancels)
        {
            await child.WaitUntilStartedAsync(cancellation.Token);
            cancellation.Cancel();
            await assertion.Should().ThrowAsync<OperationCanceledException>();
        }
        else
        {
            await assertion.Should().ThrowAsync<TimeoutException>()
                .WithMessage("Package command exceeded five minutes.");
        }

        var pid = int.Parse(await File.ReadAllTextAsync(child.PidPath, TestContext.Current.CancellationToken));
        try
        {
            using var process = Process.GetProcessById(pid);
            process.HasExited.Should().BeTrue();
        }
        catch (ArgumentException)
        {
            // Process IDs are removed after exit; absence also establishes cleanup.
        }
    }

    [Fact]
    public async Task Capture_StartFailureRemainsAnExceptionForTheProbeToReport()
    {
        var start = new ProcessStartInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "missing"))
        {
            UseShellExecute = false,
            CreateNoWindow = true
        };
        var action = () => DependencyCommandRunner.CaptureAsync(start, "--outdated", TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);
        await action.Should().ThrowAsync<System.ComponentModel.Win32Exception>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("not JSON")]
    [InlineData("[]")]
    [InlineData("{\"version\":2,\"projects\":[]}")]
    [InlineData("{\"version\":1,\"projects\":[],\"logs\":[{\"level\":\"error\"}]}")]
    public void Validation_RejectsUnusableReportsDespiteZeroExit(string output)
    {
        var command = new DependencyProbe.DependencyCommandResult("--outdated", output, "diagnostic", 0);
        var action = () => DependencyProbe.ValidateCommandResult(command);
        action.Should().Throw<JsonException>();
    }

    [Theory]
    [InlineData(0, "{\"version\":1,\"projects\":[]}")]
    [InlineData(7, "not JSON")]
    public void Validation_PreservesValidOutputAndNonzeroExitDiagnostics(int exitCode, string output)
    {
        var command = new DependencyProbe.DependencyCommandResult("--outdated", output, "diagnostic", exitCode);
        DependencyProbe.ValidateCommandResult(command).Should().BeSameAs(command);
    }

    private sealed class ChildProgram : IAsyncDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "codemetrics-command-" + Guid.NewGuid().ToString("N"));
        internal string PidPath => Path.Combine(directory, "child.pid");

        internal ChildProgram()
        {
            Directory.CreateDirectory(directory);
            var (_, _, compilation) = RoslynTestHelper.CompileCode("""
                using System;
                using System.IO;
                using System.Threading;
                class Program {
                    static void Main(string[] args) {
                        if (args[0] == "wait") {
                            File.WriteAllText(args[1], Environment.ProcessId.ToString());
                            Thread.Sleep(Timeout.Infinite);
                        }
                        Console.Out.Write(new string('o', 131072));
                        Console.Error.Write(new string('e', 131072));
                        Environment.Exit(int.Parse(args[0]));
                    }
                }
                """, MetadataReference.CreateFromFile(typeof(Thread).Assembly.Location));
            var result = ((CSharpCompilation)compilation).WithOptions(new CSharpCompilationOptions(OutputKind.ConsoleApplication))
                .Emit(Path.Combine(directory, "Child.dll"), cancellationToken: TestContext.Current.CancellationToken);
            result.Success.Should().BeTrue(string.Join(Environment.NewLine, result.Diagnostics));
            File.WriteAllText(Path.Combine(directory, "Child.runtimeconfig.json"), JsonSerializer.Serialize(new
            {
                runtimeOptions = new { framework = new { name = "Microsoft.NETCore.App", version = $"{Environment.Version.Major}.0.0" } }
            }));
        }

        internal ProcessStartInfo Start(string mode)
        {
            var start = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = directory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            start.ArgumentList.Add(Path.Combine(directory, "Child.dll"));
            start.ArgumentList.Add(mode);
            start.ArgumentList.Add(PidPath);
            return start;
        }

        internal async Task WaitUntilStartedAsync(CancellationToken cancellationToken)
        {
            using var wait = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            wait.CancelAfter(TimeSpan.FromSeconds(10));
            while (!File.Exists(PidPath))
                await Task.Delay(20, wait.Token);
        }

        public async ValueTask DisposeAsync()
        {
            // Windows can briefly retain a mapped DLL after the process signals exit.
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    Directory.Delete(directory, recursive: true);
                    return;
                }
                catch (Exception exception) when (attempt < 10 && exception is IOException or UnauthorizedAccessException)
                {
                    await Task.Delay(50, CancellationToken.None);
                }
            }
        }
    }
}
