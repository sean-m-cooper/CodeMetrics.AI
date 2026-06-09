using CodeMetrics.AI.Metrics;

namespace CodeMetrics.AI.Output;

public static class CsvWriter
{
    private const string Header = "Scope,Project,Namespace,Type,Member,Maintainability Index,Cyclomatic Complexity,Depth of Inheritance,Class Coupling,Lines of Source code,Lines of Executable code";

    public static async Task WriteAsync(string path, IReadOnlyList<TypeMetrics> types,
        IReadOnlyList<MemberMetrics> members)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        using var writer = new StreamWriter(path);
        await writer.WriteLineAsync(Header);

        foreach (var t in types.OrderBy(t => t.Project).ThenBy(t => t.Namespace).ThenBy(t => t.Type))
        {
            await writer.WriteLineAsync($"Type,{Escape(t.Project)},{Escape(t.Namespace)},{Escape(t.Type)},,{t.MaintainabilityIndex},{t.CyclomaticComplexity},{t.DepthOfInheritance},{t.ClassCoupling},{t.LinesOfSource},{t.LinesOfExecutable}");

            var typeMembers = members
                .Where(m => m.Project == t.Project && m.Namespace == t.Namespace && m.Type == t.Type)
                .OrderBy(m => m.Member);

            foreach (var m in typeMembers)
            {
                await writer.WriteLineAsync($"Member,{Escape(m.Project)},{Escape(m.Namespace)},{Escape(m.Type)},{Escape(m.Member)},{m.MaintainabilityIndex},{m.CyclomaticComplexity},0,0,{m.LinesOfSource},{m.LinesOfExecutable}");
            }
        }
    }

    private static string Escape(string value) =>
        value.Contains(',') || value.Contains('"') ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
}
