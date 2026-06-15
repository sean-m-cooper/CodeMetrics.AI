using CodeMetrics.AI.Metrics;
using System.Text;

namespace CodeMetrics.AI.Output;

public static class CsvWriter
{
    private const string Header = "Scope,Project,Namespace,Type,Member,Maintainability Index,Cyclomatic Complexity,Depth of Inheritance,Class Coupling,Lines of Source code,Lines of Executable code";

    public static async Task WriteAsync(
        string path,
        IReadOnlyList<TypeMetrics> types,
        IReadOnlyList<MemberMetrics> members,
        CancellationToken cancellationToken = default)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var csv = Build(types, members);
        await File.WriteAllTextAsync(path, csv, cancellationToken);
    }

    private static string Build(IReadOnlyList<TypeMetrics> types, IReadOnlyList<MemberMetrics> members)
    {
        var builder = new StringBuilder();
        builder.AppendLine(Header);

        foreach (var t in types.OrderBy(t => t.Project).ThenBy(t => t.Namespace).ThenBy(t => t.Type))
        {
            builder.AppendLine($"Type,{Escape(t.Project)},{Escape(t.Namespace)},{Escape(t.Type)},,{t.MaintainabilityIndex},{t.CyclomaticComplexity},{t.DepthOfInheritance},{t.ClassCoupling},{t.LinesOfSource},{t.LinesOfExecutable}");

            var typeMembers = members
                .Where(m => m.Project == t.Project && m.Namespace == t.Namespace && m.Type == t.Type)
                .OrderBy(m => m.Member);

            foreach (var m in typeMembers)
            {
                builder.AppendLine($"Member,{Escape(m.Project)},{Escape(m.Namespace)},{Escape(m.Type)},{Escape(m.Member)},{m.MaintainabilityIndex},{m.CyclomaticComplexity},0,0,{m.LinesOfSource},{m.LinesOfExecutable}");
            }
        }

        return builder.ToString();
    }

    private static string Escape(string value) =>
        value.Contains(',') || value.Contains('"') ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
}
