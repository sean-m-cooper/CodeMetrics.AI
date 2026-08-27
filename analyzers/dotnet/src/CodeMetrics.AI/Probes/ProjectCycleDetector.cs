using System.Xml.Linq;

namespace CodeMetrics.AI.Probes;

internal static class ProjectCycleDetector
{
    public static List<List<string>> Find(string solutionDir)
    {
        return Directory.Exists(solutionDir)
            ? FindCycles(BuildGraph(solutionDir))
            : [];
    }

    private static Dictionary<string, List<string>> BuildGraph(string solutionDir)
    {
        var graph = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true
        };

        foreach (var projectFile in Directory.GetFiles(solutionDir, "*.csproj", options))
        {
            var projectName = Path.GetFileNameWithoutExtension(projectFile);
            graph[projectName] = ReadReferences(projectFile)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        return graph;
    }

    private static IEnumerable<string> ReadReferences(string projectFile)
    {
        try
        {
            return XDocument.Load(projectFile)
                .Descendants()
                .Where(element => element.Name.LocalName == "ProjectReference")
                .Select(element => element.Attribute("Include")?.Value)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => Path.GetFileNameWithoutExtension(value!.Replace('\\', '/')))
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name!);
        }
        catch (Exception error) when (
            error is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            return [];
        }
    }

    private static List<List<string>> FindCycles(
        IReadOnlyDictionary<string, List<string>> graph)
    {
        var colors = graph.Keys.ToDictionary(
            node => node, _ => 0, StringComparer.OrdinalIgnoreCase);
        var cycles = new List<List<string>>();
        var signatures = new HashSet<string>(StringComparer.Ordinal);

        foreach (var node in graph.Keys.Where(node => colors[node] == 0))
            Explore(node, graph, colors, [], cycles, signatures);

        return cycles;
    }

    private static void Explore(
        string node,
        IReadOnlyDictionary<string, List<string>> graph,
        Dictionary<string, int> colors,
        List<string> stack,
        List<List<string>> cycles,
        HashSet<string> signatures)
    {
        colors[node] = 1;
        stack.Add(node);

        if (graph.TryGetValue(node, out var neighbors))
        {
            foreach (var neighbor in neighbors.Where(colors.ContainsKey))
            {
                if (colors[neighbor] == 0)
                {
                    Explore(neighbor, graph, colors, stack, cycles, signatures);
                    continue;
                }

                if (colors[neighbor] != 1)
                    continue;

                var cycleStart = stack.IndexOf(neighbor);
                if (cycleStart < 0)
                    continue;

                var cycle = stack.Skip(cycleStart).ToList();
                var signature = string.Join(",", cycle.OrderBy(name => name));
                if (signatures.Add(signature))
                    cycles.Add(cycle);
            }
        }

        stack.RemoveAt(stack.Count - 1);
        colors[node] = 2;
    }
}
