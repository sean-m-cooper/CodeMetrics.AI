using Microsoft.VisualStudio.SolutionPersistence.Model;
using Microsoft.VisualStudio.SolutionPersistence.Serializer;

namespace CodeMetrics.AI;

public sealed record SolutionScope(IReadOnlySet<string> ProjectPaths, IReadOnlySet<string> DisabledPaths)
{
    internal static StringComparer PathComparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    public static async Task<SolutionScope> ReadAsync(string entryPoint, string configuration, string platform, CancellationToken token)
    {
        if (Path.GetExtension(entryPoint).Equals(".csproj", StringComparison.OrdinalIgnoreCase))
            return new(new HashSet<string>([Path.GetFullPath(entryPoint)], PathComparer), new HashSet<string>(PathComparer));
        var serializer = SolutionSerializers.GetSerializerByMoniker(entryPoint)
            ?? throw new ArgumentException("Unsupported solution format.");
        var model = await serializer.OpenAsync(entryPoint, token);
        var projects = new HashSet<string>(PathComparer);
        var disabled = new HashSet<string>(PathComparer);
        foreach (var project in model.SolutionProjects.Where(p => Path.GetExtension(p.FilePath).Equals(".csproj", StringComparison.OrdinalIgnoreCase)))
        {
            var file = Path.GetFullPath(project.FilePath.Replace('\\', Path.DirectorySeparatorChar), Path.GetDirectoryName(entryPoint)!);
            projects.Add(file);
            if (!project.GetProjectConfiguration(configuration, platform).Item3)
                disabled.Add(file);
        }
        return new(projects, disabled);
    }

    // Package listing does not honor solution Build=false. Use a disposable solution
    // containing the selected build projects; never rewrite the user's solution.
    public async Task<string> WriteDependencySolutionAsync(string directory, CancellationToken token)
    {
        var model = new SolutionModel();
        foreach (var file in ProjectPaths.Except(DisabledPaths, PathComparer).Order(PathComparer))
            model.AddProject(file, null, null);
        var filePath = Path.Combine(directory, "Dependencies.sln");
        await SolutionSerializers.SlnFileV12.SaveAsync(filePath, model, token);
        return filePath;
    }
}
