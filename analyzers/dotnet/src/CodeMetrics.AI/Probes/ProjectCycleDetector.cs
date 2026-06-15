using System.Diagnostics;
using System.Xml.Linq;

namespace CodeMetrics.AI.Probes;

internal static class ProjectCycleDetector
{
    public static List<List<string>> Detect(string solutionDir)
    {
        if (!Directory.Exists(solutionDir))
            return [];

        var adjacency = ProjectReferenceGraphBuilder.Build(solutionDir);
        return FindCycles(adjacency);
    }

    private static List<List<string>> FindCycles(Dictionary<string, List<string>> adjacency)
    {
        var color = adjacency.Keys.ToDictionary(
            node => node,
            _ => 0,
            StringComparer.OrdinalIgnoreCase);
        var allCycles = new List<List<string>>();
        var cycleSignatures = new HashSet<string>();
        var stack = new List<string>();

        foreach (var node in adjacency.Keys)
        {
            if (color[node] == 0)
                Dfs(node, adjacency, color, stack, allCycles, cycleSignatures);
        }

        return allCycles;
    }

    private static void Dfs(
        string node,
        Dictionary<string, List<string>> adjacency,
        Dictionary<string, int> color,
        List<string> stack,
        List<List<string>> allCycles,
        HashSet<string> cycleSignatures)
    {
        color[node] = 1;
        stack.Add(node);

        if (adjacency.TryGetValue(node, out var neighbors))
            AddCyclesFromNeighbors(neighbors, adjacency, color, stack, allCycles, cycleSignatures);

        stack.RemoveAt(stack.Count - 1);
        color[node] = 2;
    }

    private static void AddCyclesFromNeighbors(
        List<string> neighbors,
        Dictionary<string, List<string>> adjacency,
        Dictionary<string, int> color,
        List<string> stack,
        List<List<string>> allCycles,
        HashSet<string> cycleSignatures)
    {
        foreach (var neighbor in neighbors)
        {
            if (!color.ContainsKey(neighbor))
                continue;

            if (color[neighbor] == 1)
                AddCycle(neighbor, stack, allCycles, cycleSignatures);
            else if (color[neighbor] == 0)
                Dfs(neighbor, adjacency, color, stack, allCycles, cycleSignatures);
        }
    }

    private static void AddCycle(
        string neighbor,
        List<string> stack,
        List<List<string>> allCycles,
        HashSet<string> cycleSignatures)
    {
        var cycleStart = stack.IndexOf(neighbor);
        if (cycleStart < 0)
            return;

        var cycle = stack.Skip(cycleStart).ToList();
        var signature = string.Join(",", cycle.OrderBy(x => x));
        if (cycleSignatures.Add(signature))
            allCycles.Add(cycle);
    }
}

internal static class ProjectReferenceGraphBuilder
{
    public static Dictionary<string, List<string>> Build(string solutionDir)
    {
        var adjacency = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var csprojFile in FindProjectFiles(solutionDir))
            AddProjectReferences(csprojFile, adjacency);

        return adjacency;
    }

    private static string[] FindProjectFiles(string solutionDir)
    {
        var enumOptions = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
        };

        return Directory.GetFiles(solutionDir, "*.csproj", enumOptions);
    }

    private static void AddProjectReferences(
        string csprojFile,
        Dictionary<string, List<string>> adjacency)
    {
        var projectName = Path.GetFileNameWithoutExtension(csprojFile);
        if (!adjacency.ContainsKey(projectName))
            adjacency[projectName] = [];

        try
        {
            foreach (var referenceName in ReadProjectReferences(csprojFile))
                AddReference(adjacency[projectName], referenceName);
        }
        catch (Exception ex)
        {
            LogSkippedProjectReferenceFile(csprojFile, ex);
        }
    }

    private static IEnumerable<string> ReadProjectReferences(string csprojFile)
    {
        var doc = XDocument.Load(csprojFile);
        return doc.Descendants()
            .Where(e => e.Name.LocalName == "ProjectReference")
            .Select(e => e.Attribute("Include")?.Value)
            .Where(v => v != null)
            .Select(v => Path.GetFileNameWithoutExtension(v!.Replace('\\', '/')))
            .Where(n => !string.IsNullOrEmpty(n))!;
    }

    private static void AddReference(List<string> references, string referenceName)
    {
        if (!references.Contains(referenceName, StringComparer.OrdinalIgnoreCase))
            references.Add(referenceName);
    }

    private static void LogSkippedProjectReferenceFile(string projectFile, Exception exception)
    {
        Debug.WriteLine($"Skipping project reference scan for '{projectFile}': {exception.Message}");
    }
}
