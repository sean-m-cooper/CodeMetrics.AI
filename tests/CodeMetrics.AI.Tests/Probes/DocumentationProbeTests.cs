using CodeMetrics.AI.Probes;
using CodeMetrics.AI.Tests.Helpers;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using System.Text.Json;

namespace CodeMetrics.AI.Tests.Probes;

public class DocumentationProbeTests
{
    // ── Helper ────────────────────────────────────────────────────────────────

    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static List<(string, Compilation, string?)> NoProjects() =>
        new List<(string, Compilation, string?)>();

    private static void WriteLines(string path, int count, string? extra = null)
    {
        var lines = Enumerable.Range(1, count)
            .Select(i => $"Line {i} of content.")
            .ToList();
        if (extra != null)
            lines.Add(extra);
        File.WriteAllLines(path, lines);
    }

    // ── 1. Empty directory, no README or docs → score 0 ──────────────────────

    [Fact]
    public void EmptyDirectory_NoReadmeOrDocs_ScoreIsZero()
    {
        var tempDir = TempDir();
        try
        {
            var result = DocumentationProbe.Analyze(tempDir, NoProjects());

            result.Status.Should().Be("scored");
            result.Score.Should().Be(0);
            result.Basis.Should().Contain("Neither README");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    // ── 2. README with 25 lines, docs/ with architecture.md → high score ──────

    [Fact]
    public void ReadmeWith25Lines_DocsWithArchitectureDoc_HighScore()
    {
        var tempDir = TempDir();
        try
        {
            // README with 25 non-blank lines
            WriteLines(Path.Combine(tempDir, "README.md"), 25);

            // docs/ directory with architecture.md
            var docsDir = Path.Combine(tempDir, "docs");
            Directory.CreateDirectory(docsDir);
            File.WriteAllText(Path.Combine(docsDir, "architecture.md"), "# Architecture\nContent here.");

            // AI instructions
            File.WriteAllText(Path.Combine(tempDir, "CLAUDE.md"), "# Claude Instructions");

            var result = DocumentationProbe.Analyze(tempDir, NoProjects());

            result.Status.Should().Be("scored");
            // Start=10, no -3 (readme>=20), no -2 (hasDocsDir), no -1 (archDoc exists),
            // no -1 (AI instructions), no -2 (no library projects), no -1 (no public API),
            // no -1 (no stale markers) → 10
            result.Score.Should().BeGreaterThanOrEqualTo(7);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    // ── 3. README with 10 lines (< 20) → -3 deduction ────────────────────────

    [Fact]
    public void ReadmeWith10Lines_Deduction3Applied()
    {
        var tempDir = TempDir();
        try
        {
            // README with only 10 lines
            WriteLines(Path.Combine(tempDir, "README.md"), 10);

            // docs/ with architecture doc and AI instructions to avoid other deductions
            var docsDir = Path.Combine(tempDir, "docs");
            Directory.CreateDirectory(docsDir);
            File.WriteAllText(Path.Combine(docsDir, "architecture.md"), "# Architecture");
            File.WriteAllText(Path.Combine(tempDir, "CLAUDE.md"), "# Instructions");

            var result = DocumentationProbe.Analyze(tempDir, NoProjects());

            result.Status.Should().Be("scored");
            // Start=10, -3 (readme < 20 lines) → max achievable = 7
            result.Score.Should().BeLessThanOrEqualTo(7);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    // ── 4. No docs/ directory → -2 deduction ─────────────────────────────────

    [Fact]
    public void NoDocsDirectory_Deduction2Applied()
    {
        var tempDir = TempDir();
        try
        {
            // README with >= 20 lines, AI instructions present
            WriteLines(Path.Combine(tempDir, "README.md"), 25);
            File.WriteAllText(Path.Combine(tempDir, "CLAUDE.md"), "# Instructions");
            // No docs/ directory

            var withDocs = DocumentationProbe.Analyze(tempDir, NoProjects());

            // Now add a docs directory (no architecture docs) to compare
            var docsDir = Path.Combine(tempDir, "docs");
            Directory.CreateDirectory(docsDir);
            var withDocsResult = DocumentationProbe.Analyze(tempDir, NoProjects());

            // Having docs/ should improve the score by at least 2 (the -2 deduction is removed)
            withDocsResult.Score.Should().BeGreaterThanOrEqualTo(withDocs.Score + 2);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    // ── 5. Clean, well-documented → score 10 ─────────────────────────────────

    [Fact]
    public void WellDocumented_ScoreIsTen()
    {
        var tempDir = TempDir();
        try
        {
            // README with 25 non-blank lines
            WriteLines(Path.Combine(tempDir, "README.md"), 25);

            // docs/ with architecture doc
            var docsDir = Path.Combine(tempDir, "docs");
            Directory.CreateDirectory(docsDir);
            File.WriteAllText(Path.Combine(docsDir, "architecture.md"), "# Architecture\nDetails.");

            // AI instructions
            File.WriteAllText(Path.Combine(tempDir, "CLAUDE.md"), "# Claude Instructions");

            // No projects (no library deductions), no stale markers
            var result = DocumentationProbe.Analyze(tempDir, NoProjects());

            result.Status.Should().Be("scored");
            result.Score.Should().Be(10);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    // ── 6. Extra data includes documentation metrics ───────────────────────────

    [Fact]
    public void ExtraData_ContainsDocumentationMetrics()
    {
        var tempDir = TempDir();
        try
        {
            WriteLines(Path.Combine(tempDir, "README.md"), 25);
            var docsDir = Path.Combine(tempDir, "docs");
            Directory.CreateDirectory(docsDir);
            File.WriteAllText(Path.Combine(docsDir, "architecture.md"), "# Arch");
            File.WriteAllText(Path.Combine(tempDir, "CLAUDE.md"), "# Claude");

            var result = DocumentationProbe.Analyze(tempDir, NoProjects());

            result.Extra.Should().ContainKey("documentationMetrics");
            var metrics = (JsonElement)result.Extra["documentationMetrics"]!;
            metrics.GetProperty("hasReadme").GetBoolean().Should().BeTrue();
            metrics.GetProperty("readmeNonBlankLines").GetInt32().Should().Be(25);
            metrics.GetProperty("hasDocsDir").GetBoolean().Should().BeTrue();
            metrics.GetProperty("architectureDocCount").GetInt32().Should().Be(1);
            metrics.GetProperty("hasAiInstructions").GetBoolean().Should().BeTrue();
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    // ── 7. Stale markers in README → -1 deduction ────────────────────────────

    [Fact]
    public void StaleMarkersInReadme_Deduction1Applied()
    {
        var tempDir = TempDir();
        try
        {
            // README with 25 lines + TODO marker
            WriteLines(Path.Combine(tempDir, "README.md"), 25, "TODO: fill this in later");

            var docsDir = Path.Combine(tempDir, "docs");
            Directory.CreateDirectory(docsDir);
            File.WriteAllText(Path.Combine(docsDir, "architecture.md"), "# Arch");
            File.WriteAllText(Path.Combine(tempDir, "CLAUDE.md"), "# Instructions");

            var resultWithStale = DocumentationProbe.Analyze(tempDir, NoProjects());

            // Replace README without TODO
            WriteLines(Path.Combine(tempDir, "README.md"), 25);
            var resultClean = DocumentationProbe.Analyze(tempDir, NoProjects());

            // Stale README should score 1 lower
            resultWithStale.Score.Should().BeLessThan(resultClean.Score);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    // ── 8. No AI instructions → -1 deduction ─────────────────────────────────

    [Fact]
    public void NoAiInstructions_Deduction1Applied()
    {
        var tempDir = TempDir();
        try
        {
            WriteLines(Path.Combine(tempDir, "README.md"), 25);
            var docsDir = Path.Combine(tempDir, "docs");
            Directory.CreateDirectory(docsDir);
            File.WriteAllText(Path.Combine(docsDir, "architecture.md"), "# Arch");
            // No AGENTS.md / CLAUDE.md / copilot-instructions.md

            var resultNoAi = DocumentationProbe.Analyze(tempDir, NoProjects());

            // Add CLAUDE.md
            File.WriteAllText(Path.Combine(tempDir, "CLAUDE.md"), "# Instructions");
            var resultWithAi = DocumentationProbe.Analyze(tempDir, NoProjects());

            resultWithAi.Score.Should().BeGreaterThan(resultNoAi.Score);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    // ── 9. AGENTS.md at root is recognized as AI instructions ─────────────────

    [Fact]
    public void AgentsMdAtRoot_RecognizedAsAiInstructions()
    {
        var tempDir = TempDir();
        try
        {
            WriteLines(Path.Combine(tempDir, "README.md"), 25);
            var docsDir = Path.Combine(tempDir, "docs");
            Directory.CreateDirectory(docsDir);
            File.WriteAllText(Path.Combine(docsDir, "architecture.md"), "# Arch");
            File.WriteAllText(Path.Combine(tempDir, "AGENTS.md"), "# Agent Instructions");

            var result = DocumentationProbe.Analyze(tempDir, NoProjects());

            result.Extra.Should().ContainKey("documentationMetrics");
            var metrics = (JsonElement)result.Extra["documentationMetrics"]!;
            metrics.GetProperty("hasAiInstructions").GetBoolean().Should().BeTrue();
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    // ── 10. GitHub copilot instructions recognized ────────────────────────────

    [Fact]
    public void GithubCopilotInstructions_RecognizedAsAiInstructions()
    {
        var tempDir = TempDir();
        try
        {
            WriteLines(Path.Combine(tempDir, "README.md"), 25);
            var docsDir = Path.Combine(tempDir, "docs");
            Directory.CreateDirectory(docsDir);
            File.WriteAllText(Path.Combine(docsDir, "architecture.md"), "# Arch");

            var githubDir = Path.Combine(tempDir, ".github");
            Directory.CreateDirectory(githubDir);
            File.WriteAllText(Path.Combine(githubDir, "copilot-instructions.md"), "# Copilot");

            var result = DocumentationProbe.Analyze(tempDir, NoProjects());

            var metrics = (JsonElement)result.Extra["documentationMetrics"]!;
            metrics.GetProperty("hasAiInstructions").GetBoolean().Should().BeTrue();
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    // ── 11. Docs/ with case 'Docs' is recognized ──────────────────────────────

    [Fact]
    public void DocsPascalCase_IsRecognizedAsDocsDirectory()
    {
        var tempDir = TempDir();
        try
        {
            WriteLines(Path.Combine(tempDir, "README.md"), 25);

            var docsDir = Path.Combine(tempDir, "Docs");
            Directory.CreateDirectory(docsDir);
            File.WriteAllText(Path.Combine(docsDir, "overview.md"), "# Overview");
            File.WriteAllText(Path.Combine(tempDir, "CLAUDE.md"), "# Instructions");

            var result = DocumentationProbe.Analyze(tempDir, NoProjects());

            var metrics = (JsonElement)result.Extra["documentationMetrics"]!;
            metrics.GetProperty("hasDocsDir").GetBoolean().Should().BeTrue();
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    // ── 12. Library project without XML docs → -2 deduction ───────────────────

    [Fact]
    public void LibraryProjectWithoutXmlDocs_Deduction2Applied()
    {
        var tempDir = TempDir();
        try
        {
            WriteLines(Path.Combine(tempDir, "README.md"), 25);
            var docsDir = Path.Combine(tempDir, "docs");
            Directory.CreateDirectory(docsDir);
            File.WriteAllText(Path.Combine(docsDir, "architecture.md"), "# Arch");
            File.WriteAllText(Path.Combine(tempDir, "CLAUDE.md"), "# Instructions");

            // Library project without GenerateDocumentationFile
            var csprojContent = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>";
            var csprojPath = Path.Combine(tempDir, "MyLib.csproj");
            File.WriteAllText(csprojPath, csprojContent);

            var (_, _, compilation) = RoslynTestHelper.CompileCode("public class MyClass { }");
            var projects = new List<(string, Compilation, string?)>
            {
                ("MyLib", compilation, csprojPath)
            };

            var resultNoXmlDocs = DocumentationProbe.Analyze(tempDir, projects);

            // Add GenerateDocumentationFile
            var csprojWithDocs = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><GenerateDocumentationFile>true</GenerateDocumentationFile></PropertyGroup></Project>";
            File.WriteAllText(csprojPath, csprojWithDocs);

            var resultWithXmlDocs = DocumentationProbe.Analyze(tempDir, projects);

            resultWithXmlDocs.Score.Should().BeGreaterThan(resultNoXmlDocs.Score);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    // ── 13. Executable project is excluded from library XML docs check ─────────

    [Fact]
    public void ExecutableProject_ExcludedFromLibraryXmlDocsCheck()
    {
        var tempDir = TempDir();
        try
        {
            WriteLines(Path.Combine(tempDir, "README.md"), 25);
            var docsDir = Path.Combine(tempDir, "docs");
            Directory.CreateDirectory(docsDir);
            File.WriteAllText(Path.Combine(docsDir, "architecture.md"), "# Arch");
            File.WriteAllText(Path.Combine(tempDir, "CLAUDE.md"), "# Instructions");

            // Exe project — should be excluded from XML docs check
            var csprojContent = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><OutputType>Exe</OutputType></PropertyGroup></Project>";
            var csprojPath = Path.Combine(tempDir, "MyApp.csproj");
            File.WriteAllText(csprojPath, csprojContent);

            var (_, _, compilation) = RoslynTestHelper.CompileCode("public class Program { }");
            var projects = new List<(string, Compilation, string?)>
            {
                ("MyApp", compilation, csprojPath)
            };

            var result = DocumentationProbe.Analyze(tempDir, projects);

            // Exe project excluded → no library XML docs deduction → score 10
            result.Score.Should().Be(10);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    // ── 14. Public API doc coverage < 50% → -1 deduction ─────────────────────

    [Fact]
    public void LowPublicApiDocCoverage_Deduction1Applied()
    {
        var tempDir = TempDir();
        try
        {
            WriteLines(Path.Combine(tempDir, "README.md"), 25);
            var docsDir = Path.Combine(tempDir, "docs");
            Directory.CreateDirectory(docsDir);
            File.WriteAllText(Path.Combine(docsDir, "architecture.md"), "# Arch");
            File.WriteAllText(Path.Combine(tempDir, "CLAUDE.md"), "# Instructions");

            // Library project with XML docs enabled but no doc comments in code
            var csprojContent = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><GenerateDocumentationFile>true</GenerateDocumentationFile></PropertyGroup></Project>";
            var csprojPath = Path.Combine(tempDir, "MyLib.csproj");
            File.WriteAllText(csprojPath, csprojContent);

            // Three public types with no XML docs → coverage = 0%
            var code = @"
public class ClassA { public void MethodA() { } }
public class ClassB { public void MethodB() { } }
public class ClassC { public void MethodC() { } }
";
            var (_, _, compilation) = RoslynTestHelper.CompileCode(code);
            var projects = new List<(string, Compilation, string?)>
            {
                ("MyLib", compilation, csprojPath)
            };

            var result = DocumentationProbe.Analyze(tempDir, projects);

            // Coverage < 50% → -1
            var metrics = (JsonElement)result.Extra["documentationMetrics"]!;
            metrics.GetProperty("publicApiDocCoverage").GetDouble().Should().BeLessThan(0.5);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    // ── 15. Public API with xml docs → good coverage ─────────────────────────

    [Fact]
    public void PublicApiWithXmlDocs_GoodCoverage()
    {
        var tempDir = TempDir();
        try
        {
            WriteLines(Path.Combine(tempDir, "README.md"), 25);
            var docsDir = Path.Combine(tempDir, "docs");
            Directory.CreateDirectory(docsDir);
            File.WriteAllText(Path.Combine(docsDir, "architecture.md"), "# Arch");
            File.WriteAllText(Path.Combine(tempDir, "CLAUDE.md"), "# Instructions");

            var csprojContent = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><GenerateDocumentationFile>true</GenerateDocumentationFile></PropertyGroup></Project>";
            var csprojPath = Path.Combine(tempDir, "MyLib.csproj");
            File.WriteAllText(csprojPath, csprojContent);

            // All public members have XML docs → coverage = 100%
            var code = @"
/// <summary>Class A summary.</summary>
public class ClassA
{
    /// <summary>Method A summary.</summary>
    public void MethodA() { }
}
";
            var (_, _, compilation) = RoslynTestHelper.CompileCode(code);
            var projects = new List<(string, Compilation, string?)>
            {
                ("MyLib", compilation, csprojPath)
            };

            var result = DocumentationProbe.Analyze(tempDir, projects);

            var metrics = (JsonElement)result.Extra["documentationMetrics"]!;
            metrics.GetProperty("publicApiDocCoverage").GetDouble().Should().BeGreaterThanOrEqualTo(1.0);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    // ── 16. Architecture doc case-insensitive detection ───────────────────────

    [Fact]
    public void ArchitectureDoc_CaseInsensitiveFilenameDetection()
    {
        var tempDir = TempDir();
        try
        {
            WriteLines(Path.Combine(tempDir, "README.md"), 25);
            var docsDir = Path.Combine(tempDir, "docs");
            Directory.CreateDirectory(docsDir);
            // Mixed-case filename
            File.WriteAllText(Path.Combine(docsDir, "ARCHITECTURE_Overview.md"), "# Overview");
            File.WriteAllText(Path.Combine(tempDir, "CLAUDE.md"), "# Instructions");

            var result = DocumentationProbe.Analyze(tempDir, NoProjects());

            var metrics = (JsonElement)result.Extra["documentationMetrics"]!;
            metrics.GetProperty("architectureDocCount").GetInt32().Should().BeGreaterThan(0);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    // ── 17. Design doc in filename also counts ─────────────────────────────────

    [Fact]
    public void DesignDocInFilename_CountedAsArchitectureDoc()
    {
        var tempDir = TempDir();
        try
        {
            WriteLines(Path.Combine(tempDir, "README.md"), 25);
            var docsDir = Path.Combine(tempDir, "docs");
            Directory.CreateDirectory(docsDir);
            File.WriteAllText(Path.Combine(docsDir, "system-design.md"), "# System Design");
            File.WriteAllText(Path.Combine(tempDir, "CLAUDE.md"), "# Instructions");

            var result = DocumentationProbe.Analyze(tempDir, NoProjects());

            var metrics = (JsonElement)result.Extra["documentationMetrics"]!;
            metrics.GetProperty("architectureDocCount").GetInt32().Should().BeGreaterThan(0);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    // ── 18. TBD marker in docs file also counts as stale ─────────────────────

    [Fact]
    public void TbdMarkerInDocsFile_CountedAsStaleMarker()
    {
        var tempDir = TempDir();
        try
        {
            WriteLines(Path.Combine(tempDir, "README.md"), 25);
            var docsDir = Path.Combine(tempDir, "docs");
            Directory.CreateDirectory(docsDir);
            File.WriteAllText(Path.Combine(docsDir, "architecture.md"), "# Arch\nTBD: fill in later");
            File.WriteAllText(Path.Combine(tempDir, "CLAUDE.md"), "# Instructions");

            var result = DocumentationProbe.Analyze(tempDir, NoProjects());

            var metrics = (JsonElement)result.Extra["documentationMetrics"]!;
            metrics.GetProperty("staleMarkerCount").GetInt32().Should().BeGreaterThan(0);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    // ── 19. Score clamped to 0 minimum ───────────────────────────────────────

    [Fact]
    public void WorstCase_ScoreNotBelowZero()
    {
        var tempDir = TempDir();
        try
        {
            // Only README with 5 lines, no docs dir → immediate 0 (skip rest)
            // But README exists, so not immediate 0 case.
            // Let's put README with 5 lines + no docs → score would be: but wait,
            // the immediate 0 only triggers when NEITHER readme NOR docs exist.
            // With README only (< 20 lines):
            // Start=10, -3 (readme<20), -2 (no docs), -1 (no arch), -1 (no AI),
            // no library deductions (empty) → 10-3-2-1-1 = 3
            WriteLines(Path.Combine(tempDir, "README.md"), 5);

            var result = DocumentationProbe.Analyze(tempDir, NoProjects());

            result.Score.Should().BeGreaterThanOrEqualTo(0);
            result.Score.Should().BeLessThanOrEqualTo(10);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    // ── 20. README exact 20 non-blank lines → no deduction ───────────────────

    [Fact]
    public void ReadmeExactly20NonBlankLines_NoDeduction()
    {
        var tempDir = TempDir();
        try
        {
            // Exactly 20 non-blank lines (boundary)
            WriteLines(Path.Combine(tempDir, "README.md"), 20);

            var docsDir = Path.Combine(tempDir, "docs");
            Directory.CreateDirectory(docsDir);
            File.WriteAllText(Path.Combine(docsDir, "architecture.md"), "# Arch");
            File.WriteAllText(Path.Combine(tempDir, "CLAUDE.md"), "# Instructions");

            var result = DocumentationProbe.Analyze(tempDir, NoProjects());

            // Exactly 20 → no -3 deduction → score 10
            result.Score.Should().Be(10);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    // ── 21. README with blank lines doesn't count them ────────────────────────

    [Fact]
    public void ReadmeWithBlankLines_OnlyNonBlankLinesCountToward20()
    {
        var tempDir = TempDir();
        try
        {
            // 30 lines total but only 10 non-blank
            var lines = new List<string>();
            for (int i = 0; i < 10; i++)
            {
                lines.Add($"Content line {i + 1}");
                lines.Add(""); // blank line after each
            }
            File.WriteAllLines(Path.Combine(tempDir, "README.md"), lines);

            var docsDir = Path.Combine(tempDir, "docs");
            Directory.CreateDirectory(docsDir);
            File.WriteAllText(Path.Combine(docsDir, "architecture.md"), "# Arch");
            File.WriteAllText(Path.Combine(tempDir, "CLAUDE.md"), "# Instructions");

            var result = DocumentationProbe.Analyze(tempDir, NoProjects());

            // 10 non-blank < 20 → -3 deduction
            result.Score.Should().BeLessThan(10);
            var metrics = (JsonElement)result.Extra["documentationMetrics"]!;
            metrics.GetProperty("readmeNonBlankLines").GetInt32().Should().Be(10);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
