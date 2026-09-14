using System.Diagnostics;

namespace CodeMetrics.AI.Probes;

internal static class DependencyCommandRunner
{
    internal static Task<DependencyProbe.DependencyCommandResult> RunAsync(
        string solutionPath, string args, CancellationToken cancellationToken, string workingDirectory)
    {
        var start = CreateStartInfo(solutionPath, args, workingDirectory);
        Console.Error.WriteLine($"Dependency check: {args}");
        return CaptureAsync(start, args, TimeSpan.FromMinutes(5), cancellationToken);
    }

    internal static ProcessStartInfo CreateStartInfo(string solutionPath, string args, string workingDirectory)
    {
        var start = new ProcessStartInfo("dotnet", $"list \"{solutionPath}\" package {args} --format json --output-version 1")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory
        };
        // MSBuildLocator mutates the parent environment. Child SDK selection must
        // resolve from its own global.json, not inherit targets from the analyzer SDK.
        foreach (var key in new[] { "MSBUILD_EXE_PATH", "MSBuildSDKsPath", "MSBuildExtensionsPath" })
            start.Environment.Remove(key);
        return start;
    }

    internal static async Task<DependencyProbe.DependencyCommandResult> CaptureAsync(
        ProcessStartInfo start, string args, TimeSpan commandTimeout, CancellationToken cancellationToken)
    {
        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("dotnet process could not be started.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(commandTimeout);
        var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await Task.WhenAll(stdout, stderr, process.WaitForExitAsync(timeout.Token));
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            if (cancellationToken.IsCancellationRequested) throw;
            throw new TimeoutException("Package command exceeded five minutes.");
        }

        return new DependencyProbe.DependencyCommandResult(args, await stdout, await stderr, process.ExitCode);
    }
}
