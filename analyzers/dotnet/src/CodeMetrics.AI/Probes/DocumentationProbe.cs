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
        var findings = FindUnresolvedCrefs(projects, solutionDir);
        var (readmePath, hasReadme, readmeNonBlankLines) = InspectReadme(solutionDir);
        var docsDir = FindDocsDirectory(solutionDir);
        var libraryDocumentation = InspectLibraryDocumentation(projects, solutionDir);
        var snapshot = new DocumentationSnapshot(
            hasReadme,
            readmeNonBlankLines,
            docsDir != null,
            CountArchitectureDocuments(docsDir),
            HasAiInstructions(solutionDir),
            libraryDocumentation.XmlDocRatio,
            libraryDocumentation.AllHaveXmlDocs,
            libraryDocumentation.PublicApiCoverage,
            CountStaleMarkers(solutionDir, hasReadme, readmePath, docsDir != null, docsDir),
            findings.Count);
        return CreateResult(snapshot, findings);
    }

    private static (string Path, bool Exists, int NonBlankLines) InspectReadme(string solutionDir)
    {
        var path = Path.Combine(solutionDir, "README.md");
        var exists = File.Exists(path);
        var nonBlankLines = exists
            ? File.ReadLines(path).Count(line => !string.IsNullOrWhiteSpace(line))
            : 0;
        return (path, exists, nonBlankLines);
    }

    private static int CountArchitectureDocuments(string? docsDirectory)
    {
        if (docsDirectory == null)
            return 0;

        return Directory.GetFiles(docsDirectory, "*.md", SearchOption.AllDirectories)
            .Count(file =>
            {
                var name = Path.GetFileNameWithoutExtension(file);
                return name.Contains("architecture", StringComparison.OrdinalIgnoreCase) ||
                       name.Contains("design", StringComparison.OrdinalIgnoreCase);
            });
    }

    private static bool HasAiInstructions(string solutionDir)
    {
        return File.Exists(Path.Combine(solutionDir, "AGENTS.md")) ||
               File.Exists(Path.Combine(solutionDir, ".github", "copilot-instructions.md")) ||
               File.Exists(Path.Combine(solutionDir, "CLAUDE.md"));
    }

    private static LibraryDocumentation InspectLibraryDocumentation(
        IReadOnlyList<(string Name, Compilation Compilation, string? ProjectFilePath)> projects,
        string solutionDir)
    {
        var libraryProjects = GetLibraryProjects(projects);
        var xmlDocEnabledCount = libraryProjects.Count(project =>
            HasXmlDocumentationEnabled(project.ProjectFilePath));
        var xmlDocRatio = libraryProjects.Count > 0
            ? (double)xmlDocEnabledCount / libraryProjects.Count
            : 1.0;
        var (publicMembers, documentedMembers) = CountPublicApiDocCoverage(
            libraryProjects,
            solutionDir);
        var publicApiCoverage = publicMembers > 0
            ? (double)documentedMembers / publicMembers
            : 1.0;
        return new LibraryDocumentation(
            xmlDocRatio,
            libraryProjects.Count == xmlDocEnabledCount,
            publicApiCoverage);
    }

    private static bool HasXmlDocumentationEnabled(string? projectFilePath)
    {
        if (projectFilePath == null || !File.Exists(projectFilePath))
            return false;

        var content = File.ReadAllText(projectFilePath);
        return content.Contains(
                   "<GenerateDocumentationFile>true</GenerateDocumentationFile>",
                   StringComparison.OrdinalIgnoreCase) ||
               content.Contains("<DocumentationFile>", StringComparison.OrdinalIgnoreCase);
    }

    private static DimensionResult CreateResult(
        DocumentationSnapshot snapshot,
        List<Finding> findings)
    {
        var basis = !snapshot.HasReadme && !snapshot.HasDocsDirectory
            ? $"Neither README.md nor docs/ directory found. unresolvedCrefs={snapshot.UnresolvedCrefs}."
            : $"hasReadme={snapshot.HasReadme}, readmeNonBlankLines={snapshot.ReadmeNonBlankLines}, " +
              $"hasDocsDir={snapshot.HasDocsDirectory}, architectureDocs={snapshot.ArchitectureDocuments}, " +
              $"hasAiInstructions={snapshot.HasAiInstructions}, " +
              $"libraryXmlDocRatio={snapshot.LibraryXmlDocRatio:F2}, " +
              $"publicApiDocCoverage={snapshot.PublicApiDocCoverage:F2}, " +
              $"staleMarkers={snapshot.StaleMarkers}, unresolvedCrefs={snapshot.UnresolvedCrefs}.";
        var decision = CalculateDecision(snapshot);
        return new DimensionResult
        {
            Status = "scored",
            Score = decision.FinalScore,
            ScoringDecision = decision,
            Basis = basis,
            Findings = findings,
            Extra = BuildExtra(snapshot)
        };
    }

    private static ScoringDecision CalculateDecision(DocumentationSnapshot snapshot)
    {
        var inputs = new Dictionary<string, object?>
        {
            ["hasReadme"] = snapshot.HasReadme,
            ["hasDocsDirectory"] = snapshot.HasDocsDirectory,
            ["readmeNonBlankLines"] = snapshot.ReadmeNonBlankLines,
            ["architectureDocuments"] = snapshot.ArchitectureDocuments,
            ["hasAiInstructions"] = snapshot.HasAiInstructions,
            ["allLibraryProjectsHaveXmlDocs"] = snapshot.AllLibraryProjectsHaveXmlDocs,
            ["publicApiDocCoverage"] = snapshot.PublicApiDocCoverage,
            ["staleMarkers"] = snapshot.StaleMarkers,
            ["unresolvedCrefs"] = snapshot.UnresolvedCrefs
        };
        if (!snapshot.HasReadme && !snapshot.HasDocsDirectory)
            return ScoringDecision.FirstMatch("dotnet/documentation/v1", inputs,
                ScoringStep.Rule("noDocumentation", "!hasReadme && !hasDocsDirectory", true, 0));
        return ScoringDecision.Deductions("dotnet/documentation/v1", inputs,
            ScoringStep.Rule("shortReadme", "readmeNonBlankLines < 20", snapshot.ReadmeNonBlankLines < 20, 3),
            ScoringStep.Rule("noDocsDirectory", "!hasDocsDirectory", !snapshot.HasDocsDirectory, 2),
            ScoringStep.Rule("noArchitectureDocs", "architectureDocuments == 0", snapshot.ArchitectureDocuments == 0, 1),
            ScoringStep.Rule("noAiInstructions", "!hasAiInstructions", !snapshot.HasAiInstructions, 1),
            ScoringStep.Rule("missingLibraryXmlDocs", "!allLibraryProjectsHaveXmlDocs", !snapshot.AllLibraryProjectsHaveXmlDocs, 2),
            ScoringStep.Rule("lowPublicApiCoverage", "publicApiDocCoverage < 0.5", snapshot.PublicApiDocCoverage < .5, 1),
            ScoringStep.Rule("staleMarkers", "staleMarkers > 0", snapshot.StaleMarkers > 0, 1),
            ScoringStep.Rule("unresolvedCrefs", "unresolvedCrefs > 0", snapshot.UnresolvedCrefs > 0, 1, "unresolvedCref"));
    }

    private sealed record LibraryDocumentation(
        double XmlDocRatio,
        bool AllHaveXmlDocs,
        double PublicApiCoverage);

    private sealed record DocumentationSnapshot(
        bool HasReadme,
        int ReadmeNonBlankLines,
        bool HasDocsDirectory,
        int ArchitectureDocuments,
        bool HasAiInstructions,
        double LibraryXmlDocRatio,
        bool AllLibraryProjectsHaveXmlDocs,
        double PublicApiDocCoverage,
        int StaleMarkers,
        int UnresolvedCrefs);

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
            foreach (var tree in SourceFileFilter.AnalyzableTrees(compilation, solutionDir))
            {
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

    private static List<Finding> FindUnresolvedCrefs(
        IReadOnlyList<(string Name, Compilation Compilation, string? ProjectFilePath)> projects,
        string solutionDir)
    {
        var findings = new List<Finding>();

        foreach (var (projectName, compilation, _) in projects)
        {
            foreach (var tree in SourceFileFilter.AnalyzableTrees(compilation, solutionDir))
            {
                var root = tree.GetRoot();
                var semanticModel = compilation.GetSemanticModel(tree);
                foreach (var attribute in root.DescendantNodes(descendIntoTrivia: true)
                             .OfType<XmlCrefAttributeSyntax>())
                {
                    var symbolInfo = semanticModel.GetSymbolInfo(attribute.Cref);
                    if (symbolInfo.Symbol != null)
                        continue;

                    findings.Add(new Finding
                    {
                        Category = "unresolvedCref",
                        Severity = "warning",
                        File = tree.FilePath,
                        Line = attribute.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                        Project = projectName,
                        Type = attribute.Ancestors()
                            .OfType<TypeDeclarationSyntax>()
                            .FirstOrDefault()?.Identifier.Text,
                        Message = $"XML documentation reference '{attribute.Cref}' does not resolve to a symbol."
                    });
                }
            }
        }

        return findings;
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

    private static Dictionary<string, object?> BuildExtra(DocumentationSnapshot snapshot)
    {
        var data = new
        {
            hasReadme = snapshot.HasReadme,
            readmeNonBlankLines = snapshot.ReadmeNonBlankLines,
            hasDocsDir = snapshot.HasDocsDirectory,
            architectureDocCount = snapshot.ArchitectureDocuments,
            hasAiInstructions = snapshot.HasAiInstructions,
            libraryXmlDocRatio = Math.Round(snapshot.LibraryXmlDocRatio, 4),
            publicApiDocCoverage = Math.Round(snapshot.PublicApiDocCoverage, 4),
            staleMarkerCount = snapshot.StaleMarkers,
            unresolvedCrefCount = snapshot.UnresolvedCrefs
        };

        return new Dictionary<string, object?>
        {
            ["documentationMetrics"] = JsonSerializer.SerializeToElement(data)
        };
    }
}
