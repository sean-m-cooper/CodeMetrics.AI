using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeMetrics.AI.Probes;

public static class DocumentationProbe
{
    public static DimensionResult Analyze(
        string solutionDir,
        IReadOnlyList<(string Name, Compilation Compilation, string? ProjectFilePath)> projects)
    {
        // ── 1. README check ──────────────────────────────────────────────────────
        var readmePath = Path.Combine(solutionDir, "README.md");
        bool hasReadme = File.Exists(readmePath);
        int readmeNonBlankLines = 0;

        if (hasReadme)
        {
            var lines = File.ReadAllLines(readmePath);
            readmeNonBlankLines = lines.Count(l => !string.IsNullOrWhiteSpace(l));
        }

        // ── 2. docs/ directory check ─────────────────────────────────────────────
        var docsDir = FindDocsDirectory(solutionDir);
        bool hasDocsDir = docsDir != null;

        // ── 3. Architecture/design docs ──────────────────────────────────────────
        int architectureDocCount = 0;
        if (hasDocsDir)
        {
            var mdFiles = Directory.GetFiles(docsDir!, "*.md", SearchOption.AllDirectories);
            architectureDocCount = mdFiles.Count(f =>
            {
                var name = Path.GetFileNameWithoutExtension(f);
                return name.IndexOf("architecture", StringComparison.OrdinalIgnoreCase) >= 0
                    || name.IndexOf("design", StringComparison.OrdinalIgnoreCase) >= 0;
            });
        }

        // ── 4. AI/onboarding instructions ────────────────────────────────────────
        bool hasAiInstructions = File.Exists(Path.Combine(solutionDir, "AGENTS.md"))
            || File.Exists(Path.Combine(solutionDir, ".github", "copilot-instructions.md"))
            || File.Exists(Path.Combine(solutionDir, "CLAUDE.md"));

        // ── 5. Library projects XML docs ─────────────────────────────────────────
        var libraryProjects = GetLibraryProjects(projects);
        int libraryProjectCount = libraryProjects.Count;
        int libraryXmlDocEnabledCount = 0;

        foreach (var (_, _, projectFilePath) in libraryProjects)
        {
            if (projectFilePath != null && File.Exists(projectFilePath))
            {
                var csprojContent = File.ReadAllText(projectFilePath);
                if (csprojContent.Contains("<GenerateDocumentationFile>true</GenerateDocumentationFile>", StringComparison.OrdinalIgnoreCase)
                    || csprojContent.Contains("<DocumentationFile>", StringComparison.OrdinalIgnoreCase))
                {
                    libraryXmlDocEnabledCount++;
                }
            }
        }

        double libraryXmlDocRatio = libraryProjectCount > 0
            ? (double)libraryXmlDocEnabledCount / libraryProjectCount
            : 1.0; // no library projects → not penalized

        bool allLibraryProjectsHaveXmlDocs = libraryProjectCount == 0 || libraryXmlDocEnabledCount == libraryProjectCount;

        // ── 6. Public API doc coverage ───────────────────────────────────────────
        var (publicMemberCount, documentedMemberCount) = CountPublicApiDocCoverage(libraryProjects, solutionDir);
        double publicApiDocCoverage = publicMemberCount > 0
            ? (double)documentedMemberCount / publicMemberCount
            : 1.0; // no public members → not penalized

        // ── 7. Stale markers ─────────────────────────────────────────────────────
        int staleMarkerCount = CountStaleMarkers(solutionDir, hasReadme, readmePath, hasDocsDir, docsDir);

        // ── Scoring ───────────────────────────────────────────────────────────────
        if (!hasReadme && !hasDocsDir)
        {
            return new DimensionResult
            {
                Status = "scored",
                Score = 0,
                Basis = "Neither README.md nor docs/ directory found.",
                Extra = BuildExtra(hasReadme, readmeNonBlankLines, hasDocsDir, architectureDocCount,
                    hasAiInstructions, libraryXmlDocRatio, publicApiDocCoverage, staleMarkerCount)
            };
        }

        double score = 10.0;

        if (readmeNonBlankLines < 20)
            score -= 3;

        if (!hasDocsDir)
            score -= 2;

        if (architectureDocCount == 0)
            score -= 1;

        if (!hasAiInstructions)
            score -= 1;

        if (!allLibraryProjectsHaveXmlDocs)
            score -= 2;

        if (publicApiDocCoverage < 0.5)
            score -= 1;

        if (staleMarkerCount > 0)
            score -= 1;

        // Clamp to [0, 10]
        score = Math.Max(0, Math.Min(10, score));

        var basis = $"hasReadme={hasReadme}, readmeNonBlankLines={readmeNonBlankLines}, " +
                    $"hasDocsDir={hasDocsDir}, architectureDocs={architectureDocCount}, " +
                    $"hasAiInstructions={hasAiInstructions}, " +
                    $"libraryXmlDocRatio={libraryXmlDocRatio:F2}, " +
                    $"publicApiDocCoverage={publicApiDocCoverage:F2}, " +
                    $"staleMarkers={staleMarkerCount}.";

        return new DimensionResult
        {
            Status = "scored",
            Score = score,
            Basis = basis,
            Extra = BuildExtra(hasReadme, readmeNonBlankLines, hasDocsDir, architectureDocCount,
                hasAiInstructions, libraryXmlDocRatio, publicApiDocCoverage, staleMarkerCount)
        };
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private static string? FindDocsDirectory(string solutionDir)
    {
        var docsLower = Path.Combine(solutionDir, "docs");
        if (Directory.Exists(docsLower))
            return docsLower;

        var docsPascal = Path.Combine(solutionDir, "Docs");
        if (Directory.Exists(docsPascal))
            return docsPascal;

        return null;
    }

    private static List<(string Name, Compilation Compilation, string? ProjectFilePath)> GetLibraryProjects(
        IReadOnlyList<(string Name, Compilation Compilation, string? ProjectFilePath)> projects)
    {
        var result = new List<(string Name, Compilation Compilation, string? ProjectFilePath)>();

        foreach (var project in projects)
        {
            if (project.ProjectFilePath != null && File.Exists(project.ProjectFilePath))
            {
                var content = File.ReadAllText(project.ProjectFilePath);
                // Exe output type means it's a console/executable project
                if (content.Contains("<OutputType>Exe</OutputType>", StringComparison.OrdinalIgnoreCase))
                    continue;
            }
            result.Add(project);
        }

        return result;
    }

    private static (int total, int documented) CountPublicApiDocCoverage(
        IReadOnlyList<(string Name, Compilation Compilation, string? ProjectFilePath)> libraryProjects,
        string solutionDir)
    {
        int total = 0;
        int documented = 0;

        foreach (var (_, compilation, _) in libraryProjects)
        {
            foreach (var tree in compilation.SyntaxTrees)
            {
                if (!SourceFileFilter.ShouldAnalyze(tree.FilePath, solutionDir))
                    continue;

                var root = tree.GetRoot();

                // Public types
                var publicTypes = root.DescendantNodes()
                    .OfType<TypeDeclarationSyntax>()
                    .Where(IsPublic);

                foreach (var type in publicTypes)
                {
                    total++;
                    if (HasXmlDocTrivia(type))
                        documented++;

                    // Public members within the type
                    var publicMembers = type.Members
                        .Where(m => m is not TypeDeclarationSyntax) // nested types counted separately
                        .Where(IsPublicMember);

                    foreach (var member in publicMembers)
                    {
                        total++;
                        if (HasXmlDocTrivia(member))
                            documented++;
                    }
                }
            }
        }

        return (total, documented);
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
        var leadingTrivia = node.GetLeadingTrivia();
        return leadingTrivia.Any(t =>
            t.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia) ||
            t.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia));
    }

    private static int CountStaleMarkers(
        string solutionDir,
        bool hasReadme,
        string readmePath,
        bool hasDocsDir,
        string? docsDir)
    {
        int count = 0;

        if (hasReadme && File.Exists(readmePath))
        {
            var content = File.ReadAllText(readmePath);
            if (content.IndexOf("TODO", StringComparison.OrdinalIgnoreCase) >= 0 ||
                content.IndexOf("TBD", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                count++;
            }
        }

        if (hasDocsDir && docsDir != null)
        {
            var mdFiles = Directory.GetFiles(docsDir, "*.md", SearchOption.AllDirectories);
            foreach (var file in mdFiles)
            {
                var content = File.ReadAllText(file);
                if (content.IndexOf("TODO", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    content.IndexOf("TBD", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static Dictionary<string, object?> BuildExtra(
        bool hasReadme,
        int readmeNonBlankLines,
        bool hasDocsDir,
        int architectureDocCount,
        bool hasAiInstructions,
        double libraryXmlDocRatio,
        double publicApiDocCoverage,
        int staleMarkerCount)
    {
        var data = new
        {
            hasReadme,
            readmeNonBlankLines,
            hasDocsDir,
            architectureDocCount,
            hasAiInstructions,
            libraryXmlDocRatio = Math.Round(libraryXmlDocRatio, 4),
            publicApiDocCoverage = Math.Round(publicApiDocCoverage, 4),
            staleMarkerCount
        };

        return new Dictionary<string, object?>
        {
            ["documentationMetrics"] = JsonSerializer.SerializeToElement(data)
        };
    }
}
