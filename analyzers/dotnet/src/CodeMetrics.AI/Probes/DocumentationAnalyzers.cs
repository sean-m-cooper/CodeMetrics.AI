using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeMetrics.AI.Probes;

internal sealed record DocumentationMetrics(
    bool HasReadme,
    int ReadmeNonBlankLines,
    bool HasDocsDir,
    int ArchitectureDocCount,
    bool HasAiInstructions,
    double LibraryXmlDocRatio,
    double PublicApiDocCoverage,
    int StaleMarkerCount,
    bool AllLibraryProjectsHaveXmlDocs);

internal static class DocumentationMetricsCollector
{
    public static DocumentationMetrics Collect(
        string solutionDir,
        IReadOnlyList<(string Name, Compilation Compilation, string? ProjectFilePath)> projects)
    {
        var docs = DocumentationFileInspector.Inspect(solutionDir);
        var libraryProjects = LibraryProjectSelector.GetLibraryProjects(projects);
        var libraryXmlDoc = LibraryXmlDocInspector.Inspect(libraryProjects);
        var publicApiDocCoverage = PublicApiDocumentationCounter.Coverage(libraryProjects, solutionDir);
        var staleMarkerCount = StaleMarkerCounter.Count(solutionDir, docs);

        return new DocumentationMetrics(
            docs.HasReadme,
            docs.ReadmeNonBlankLines,
            docs.HasDocsDir,
            docs.ArchitectureDocCount,
            AiInstructionInspector.Exists(solutionDir),
            libraryXmlDoc.Ratio,
            publicApiDocCoverage,
            staleMarkerCount,
            libraryXmlDoc.AllLibraryProjectsHaveXmlDocs);
    }
}

internal sealed record DocumentationFileMetrics(
    bool HasReadme,
    string ReadmePath,
    int ReadmeNonBlankLines,
    string? DocsDir,
    bool HasDocsDir,
    int ArchitectureDocCount);

internal static class DocumentationFileInspector
{
    public static DocumentationFileMetrics Inspect(string solutionDir)
    {
        var readmePath = Path.Combine(solutionDir, "README.md");
        var hasReadme = File.Exists(readmePath);
        var docsDir = DocsDirectoryFinder.Find(solutionDir);

        return new DocumentationFileMetrics(
            hasReadme,
            readmePath,
            CountReadmeLines(readmePath, hasReadme),
            docsDir,
            docsDir != null,
            CountArchitectureDocs(docsDir));
    }

    private static int CountReadmeLines(string readmePath, bool hasReadme)
    {
        return hasReadme
            ? File.ReadAllLines(readmePath).Count(line => !string.IsNullOrWhiteSpace(line))
            : 0;
    }

    private static int CountArchitectureDocs(string? docsDir)
    {
        if (docsDir == null)
            return 0;

        return Directory.GetFiles(docsDir, "*.md", SearchOption.AllDirectories)
            .Count(IsArchitectureDoc);
    }

    private static bool IsArchitectureDoc(string file)
    {
        var name = Path.GetFileNameWithoutExtension(file);
        return name.Contains("architecture", StringComparison.OrdinalIgnoreCase)
            || name.Contains("design", StringComparison.OrdinalIgnoreCase);
    }
}

internal static class DocsDirectoryFinder
{
    public static string? Find(string solutionDir)
    {
        var docsLower = Path.Combine(solutionDir, "docs");
        if (Directory.Exists(docsLower))
            return docsLower;

        var docsPascal = Path.Combine(solutionDir, "Docs");
        return Directory.Exists(docsPascal) ? docsPascal : null;
    }
}

internal static class AiInstructionInspector
{
    public static bool Exists(string solutionDir)
    {
        return File.Exists(Path.Combine(solutionDir, "AGENTS.md"))
            || File.Exists(Path.Combine(solutionDir, ".github", "copilot-instructions.md"))
            || File.Exists(Path.Combine(solutionDir, "CLAUDE.md"));
    }
}

internal static class LibraryProjectSelector
{
    public static List<(string Name, Compilation Compilation, string? ProjectFilePath)> GetLibraryProjects(
        IReadOnlyList<(string Name, Compilation Compilation, string? ProjectFilePath)> projects)
    {
        return projects
            .Where(IsLibraryProject)
            .ToList();
    }

    private static bool IsLibraryProject((string Name, Compilation Compilation, string? ProjectFilePath) project)
    {
        return project.ProjectFilePath == null
            || !File.Exists(project.ProjectFilePath)
            || !File.ReadAllText(project.ProjectFilePath)
                .Contains("<OutputType>Exe</OutputType>", StringComparison.OrdinalIgnoreCase);
    }
}

internal sealed record LibraryXmlDocMetrics(double Ratio, bool AllLibraryProjectsHaveXmlDocs);

internal static class LibraryXmlDocInspector
{
    public static LibraryXmlDocMetrics Inspect(
        IReadOnlyList<(string Name, Compilation Compilation, string? ProjectFilePath)> libraryProjects)
    {
        var enabledCount = libraryProjects.Count(ProjectHasXmlDocsEnabled);
        var projectCount = libraryProjects.Count;
        var ratio = projectCount > 0 ? (double)enabledCount / projectCount : 1.0;

        return new LibraryXmlDocMetrics(
            ratio,
            projectCount == 0 || enabledCount == projectCount);
    }

    private static bool ProjectHasXmlDocsEnabled(
        (string Name, Compilation Compilation, string? ProjectFilePath) project)
    {
        if (project.ProjectFilePath == null || !File.Exists(project.ProjectFilePath))
            return false;

        var content = File.ReadAllText(project.ProjectFilePath);
        return content.Contains("<GenerateDocumentationFile>true</GenerateDocumentationFile>", StringComparison.OrdinalIgnoreCase)
            || content.Contains("<DocumentationFile>", StringComparison.OrdinalIgnoreCase);
    }
}

internal static class PublicApiDocumentationCounter
{
    public static double Coverage(
        IReadOnlyList<(string Name, Compilation Compilation, string? ProjectFilePath)> libraryProjects,
        string solutionDir)
    {
        var (total, documented) = Count(libraryProjects, solutionDir);
        return total > 0 ? (double)documented / total : 1.0;
    }

    private static (int Total, int Documented) Count(
        IReadOnlyList<(string Name, Compilation Compilation, string? ProjectFilePath)> libraryProjects,
        string solutionDir)
    {
        var total = 0;
        var documented = 0;

        foreach (var (_, compilation, _) in libraryProjects)
            CountCompilation(compilation, solutionDir, ref total, ref documented);

        return (total, documented);
    }

    private static void CountCompilation(
        Compilation compilation,
        string solutionDir,
        ref int total,
        ref int documented)
    {
        foreach (var tree in compilation.SyntaxTrees)
        {
            if (!SourceFileFilter.ShouldAnalyze(tree.FilePath, solutionDir))
                continue;

            CountRoot(tree.GetRoot(), ref total, ref documented);
        }
    }

    private static void CountRoot(SyntaxNode root, ref int total, ref int documented)
    {
        foreach (var type in root.DescendantNodes().OfType<TypeDeclarationSyntax>().Where(IsPublic))
        {
            CountNode(type, ref total, ref documented);
            foreach (var member in type.Members.Where(m => m is not TypeDeclarationSyntax).Where(IsPublicMember))
                CountNode(member, ref total, ref documented);
        }
    }

    private static void CountNode(SyntaxNode node, ref int total, ref int documented)
    {
        total++;
        if (HasXmlDocTrivia(node))
            documented++;
    }

    private static bool IsPublic(TypeDeclarationSyntax type)
    {
        return type.Modifiers.Any(m => m.IsKind(SyntaxKind.PublicKeyword));
    }

    private static bool IsPublicMember(MemberDeclarationSyntax member)
    {
        return member.Modifiers.Any(m => m.IsKind(SyntaxKind.PublicKeyword));
    }

    private static bool HasXmlDocTrivia(SyntaxNode node)
    {
        return node.GetLeadingTrivia().Any(t =>
            t.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia) ||
            t.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia));
    }
}

internal static class StaleMarkerCounter
{
    public static int Count(string solutionDir, DocumentationFileMetrics docs)
    {
        var count = docs.HasReadme && ContainsStaleMarker(docs.ReadmePath) ? 1 : 0;
        if (docs.HasDocsDir && docs.DocsDir != null)
            count += CountDocsMarkers(docs.DocsDir);

        return count;
    }

    private static int CountDocsMarkers(string docsDir)
    {
        return Directory.GetFiles(docsDir, "*.md", SearchOption.AllDirectories)
            .Count(ContainsStaleMarker);
    }

    private static bool ContainsStaleMarker(string path)
    {
        var content = File.ReadAllText(path);
        return content.Contains("TODO", StringComparison.OrdinalIgnoreCase)
            || content.Contains("TBD", StringComparison.OrdinalIgnoreCase);
    }
}

internal static class DocumentationResultBuilder
{
    public static DimensionResult Build(DocumentationMetrics metrics)
    {
        if (!metrics.HasReadme && !metrics.HasDocsDir)
        {
            return new DimensionResult
            {
                Status = "scored",
                Score = 0,
                Basis = "Neither README.md nor docs/ directory found.",
                Extra = BuildExtra(metrics)
            };
        }

        return new DimensionResult
        {
            Status = "scored",
            Score = Score(metrics),
            Basis = Basis(metrics),
            Extra = BuildExtra(metrics)
        };
    }

    private static double Score(DocumentationMetrics metrics)
    {
        var score = 10.0;
        score -= metrics.ReadmeNonBlankLines < 20 ? 3 : 0;
        score -= !metrics.HasDocsDir ? 2 : 0;
        score -= metrics.ArchitectureDocCount == 0 ? 1 : 0;
        score -= !metrics.HasAiInstructions ? 1 : 0;
        score -= !metrics.AllLibraryProjectsHaveXmlDocs ? 2 : 0;
        score -= metrics.PublicApiDocCoverage < 0.5 ? 1 : 0;
        score -= metrics.StaleMarkerCount > 0 ? 1 : 0;
        return Math.Max(0, Math.Min(10, score));
    }

    private static string Basis(DocumentationMetrics metrics)
    {
        return $"hasReadme={metrics.HasReadme}, readmeNonBlankLines={metrics.ReadmeNonBlankLines}, " +
               $"hasDocsDir={metrics.HasDocsDir}, architectureDocs={metrics.ArchitectureDocCount}, " +
               $"hasAiInstructions={metrics.HasAiInstructions}, " +
               $"libraryXmlDocRatio={metrics.LibraryXmlDocRatio:F2}, " +
               $"publicApiDocCoverage={metrics.PublicApiDocCoverage:F2}, " +
               $"staleMarkers={metrics.StaleMarkerCount}.";
    }

    private static Dictionary<string, object?> BuildExtra(DocumentationMetrics metrics)
    {
        var data = new
        {
            hasReadme = metrics.HasReadme,
            readmeNonBlankLines = metrics.ReadmeNonBlankLines,
            hasDocsDir = metrics.HasDocsDir,
            architectureDocCount = metrics.ArchitectureDocCount,
            hasAiInstructions = metrics.HasAiInstructions,
            libraryXmlDocRatio = Math.Round(metrics.LibraryXmlDocRatio, 4),
            publicApiDocCoverage = Math.Round(metrics.PublicApiDocCoverage, 4),
            staleMarkerCount = metrics.StaleMarkerCount
        };

        return new Dictionary<string, object?>
        {
            ["documentationMetrics"] = JsonSerializer.SerializeToElement(data)
        };
    }
}
