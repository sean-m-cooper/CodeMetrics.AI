using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeMetrics.AI.Probes;

public static class SecurityProbe
{
    private static readonly HashSet<string> SecretKeywords =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "apikey", "password", "token", "secret", "connectionstring"
        };

    private static readonly HashSet<string> SafePlaceholders =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "example", "placeholder", "localhost"
        };

    public static DimensionResult Analyze(
        IReadOnlyList<(string ProjectName, Compilation Compilation)> projects,
        int importedVulnerabilities = 0,
        string? solutionDir = null)
    {
        var findings = new List<Finding>();

        foreach (var (projectName, compilation) in projects)
        {
            foreach (var tree in compilation.SyntaxTrees)
            {
                if (solutionDir != null && !SourceFileFilter.ShouldAnalyze(tree.FilePath, solutionDir))
                    continue;

                var root = tree.GetRoot();
                var filePath = tree.FilePath;

                AnalyzeHardcodedSecrets(root, filePath, projectName, findings);
                AnalyzeRawSqlInterpolation(root, filePath, projectName, findings);
                AnalyzeUnsafeDeserialization(root, filePath, projectName, findings);
                AnalyzeAllowAnyOriginWithCredentials(root, filePath, projectName, findings);
                AnalyzeAllowAnonymous(root, filePath, projectName, findings);
            }

            // missingAuthorization needs full project view (all trees)
            AnalyzeMissingAuthorization(compilation, projectName, findings, solutionDir);
        }

        var hardcodedSecrets = findings.Count(f => f.Category == "hardcodedSecret");
        var allowAnyOriginWithCreds = findings.Count(f => f.Category == "allowAnyOriginWithCredentials");
        var rawSqlCount = findings.Count(f => f.Category == "rawSqlInterpolation");
        var unsafeDeser = findings.Count(f => f.Category == "unsafeDeserialization");
        var errors = findings.Count(f => f.Severity == "error");
        var warnings = findings.Count(f => f.Severity == "warning");

        double score;
        if (hardcodedSecrets > 2 || allowAnyOriginWithCreds > 0)
            score = 0;
        else if (hardcodedSecrets > 0 || rawSqlCount > 0 || importedVulnerabilities > 0)
            score = 2;
        else if (unsafeDeser > 0)
            score = 4;
        else if (warnings > 2)
            score = 6;
        else if (warnings > 0)
            score = 8;
        else
            score = 10;

        var basis = $"Findings: {findings.Count} (errors: {errors}, warnings: {warnings}). " +
                    $"hardcodedSecrets={hardcodedSecrets}, rawSql={rawSqlCount}, " +
                    $"unsafeDeserialization={unsafeDeser}, allowAnyOriginWithCredentials={allowAnyOriginWithCreds}, " +
                    $"importedVulnerabilities={importedVulnerabilities}.";

        return new DimensionResult
        {
            Status = "scored",
            Score = score,
            Basis = basis,
            Findings = findings
        };
    }

    // ── Finding 1: Hardcoded Secrets ─────────────────────────────────────────

    private static void AnalyzeHardcodedSecrets(
        SyntaxNode root, string filePath, string projectName, List<Finding> findings)
    {
        // Variable declarations: string ApiKey = "abcdef...";
        var varDecls = root.DescendantNodes().OfType<VariableDeclaratorSyntax>();
        foreach (var varDecl in varDecls)
        {
            var name = varDecl.Identifier.Text;
            if (!ContainsSecretKeyword(name))
                continue;

            if (varDecl.Initializer?.Value is LiteralExpressionSyntax lit &&
                lit.IsKind(SyntaxKind.StringLiteralExpression))
            {
                var value = lit.Token.ValueText;
                if (value.Length >= 16 && !ContainsSafePlaceholder(value))
                {
                    findings.Add(new Finding
                    {
                        Category = "hardcodedSecret",
                        Severity = "error",
                        File = filePath,
                        Line = GetLine(varDecl),
                        Project = projectName,
                        Type = GetContainingTypeName(varDecl),
                        Message = $"Variable '{name}' appears to contain a hardcoded secret."
                    });
                }
            }
        }

        // Assignment expressions: ApiKey = "abcdef...";
        var assignments = root.DescendantNodes().OfType<AssignmentExpressionSyntax>();
        foreach (var assignment in assignments)
        {
            var leftText = assignment.Left.ToString();
            // Extract just the identifier name (last segment if member access)
            var namePart = leftText.Contains('.')
                ? leftText.Substring(leftText.LastIndexOf('.') + 1)
                : leftText;

            if (!ContainsSecretKeyword(namePart))
                continue;

            if (assignment.Right is LiteralExpressionSyntax lit &&
                lit.IsKind(SyntaxKind.StringLiteralExpression))
            {
                var value = lit.Token.ValueText;
                if (value.Length >= 16 && !ContainsSafePlaceholder(value))
                {
                    findings.Add(new Finding
                    {
                        Category = "hardcodedSecret",
                        Severity = "error",
                        File = filePath,
                        Line = GetLine(assignment),
                        Project = projectName,
                        Type = GetContainingTypeName(assignment),
                        Message = $"Assignment to '{namePart}' appears to contain a hardcoded secret."
                    });
                }
            }
        }
    }

    private static bool ContainsSecretKeyword(string name)
    {
        var lower = name.ToLowerInvariant();
        return SecretKeywords.Any(kw => lower.Contains(kw));
    }

    private static bool ContainsSafePlaceholder(string value)
    {
        var lower = value.ToLowerInvariant();
        return SafePlaceholders.Any(ph => lower.Contains(ph));
    }

    // ── Finding 2: Raw SQL Interpolation ─────────────────────────────────────

    private static void AnalyzeRawSqlInterpolation(
        SyntaxNode root, string filePath, string projectName, List<Finding> findings)
    {
        var invocations = root.DescendantNodes().OfType<InvocationExpressionSyntax>();

        foreach (var inv in invocations)
        {
            // Check if method name contains "Sql" (case-insensitive)
            var methodName = GetMethodName(inv);
            if (methodName == null ||
                methodName.IndexOf("Sql", StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            foreach (var arg in inv.ArgumentList.Arguments)
            {
                var expr = arg.Expression;
                if (expr is InterpolatedStringExpressionSyntax ||
                    IsBinaryStringConcatenation(expr))
                {
                    findings.Add(new Finding
                    {
                        Category = "rawSqlInterpolation",
                        Severity = "error",
                        File = filePath,
                        Line = GetLine(inv),
                        Project = projectName,
                        Type = GetContainingTypeName(inv),
                        Message = $"Method '{methodName}' called with interpolated/concatenated SQL string — potential SQL injection."
                    });
                    break; // one finding per invocation
                }
            }
        }
    }

    private static string? GetMethodName(InvocationExpressionSyntax inv)
    {
        return inv.Expression switch
        {
            MemberAccessExpressionSyntax ma => ma.Name.Identifier.Text,
            IdentifierNameSyntax id => id.Identifier.Text,
            _ => null
        };
    }

    private static bool IsBinaryStringConcatenation(ExpressionSyntax expr)
    {
        return expr is BinaryExpressionSyntax bin &&
               bin.IsKind(SyntaxKind.AddExpression);
    }

    // ── Finding 3: Unsafe Deserialization ────────────────────────────────────

    private static void AnalyzeUnsafeDeserialization(
        SyntaxNode root, string filePath, string projectName, List<Finding> findings)
    {
        var objectCreations = root.DescendantNodes()
            .OfType<ObjectCreationExpressionSyntax>();

        foreach (var creation in objectCreations)
        {
            var typeName = creation.Type.ToString();
            // Strip namespace prefix if present
            var shortName = typeName.Contains('.')
                ? typeName.Substring(typeName.LastIndexOf('.') + 1)
                : typeName;

            if (shortName is "BinaryFormatter" or "NetDataContractSerializer")
            {
                findings.Add(new Finding
                {
                    Category = "unsafeDeserialization",
                    Severity = "error",
                    File = filePath,
                    Line = GetLine(creation),
                    Project = projectName,
                    Type = GetContainingTypeName(creation),
                    Message = $"Use of '{shortName}' is unsafe and vulnerable to deserialization attacks."
                });
            }
        }
    }

    // ── Finding 4: AllowAnyOrigin + AllowCredentials ──────────────────────────

    private static void AnalyzeAllowAnyOriginWithCredentials(
        SyntaxNode root, string filePath, string projectName, List<Finding> findings)
    {
        var statements = root.DescendantNodes().OfType<ExpressionStatementSyntax>();

        foreach (var stmt in statements)
        {
            var invocationNames = stmt.DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Select(inv => GetMethodName(inv))
                .Where(n => n != null)
                .ToHashSet(StringComparer.Ordinal)!;

            if (invocationNames.Contains("AllowAnyOrigin") &&
                invocationNames.Contains("AllowCredentials"))
            {
                findings.Add(new Finding
                {
                    Category = "allowAnyOriginWithCredentials",
                    Severity = "error",
                    File = filePath,
                    Line = GetLine(stmt),
                    Project = projectName,
                    Type = GetContainingTypeName(stmt),
                    Message = "Combining AllowAnyOrigin() and AllowCredentials() is a CORS misconfiguration that violates the spec."
                });
            }
        }
    }

    // ── Finding 5: AllowAnonymous ─────────────────────────────────────────────

    private static void AnalyzeAllowAnonymous(
        SyntaxNode root, string filePath, string projectName, List<Finding> findings)
    {
        // Check member declarations and type declarations for [AllowAnonymous]
        var membersWithAttribs = root.DescendantNodes()
            .Where(n => n is MemberDeclarationSyntax or TypeDeclarationSyntax)
            .OfType<MemberDeclarationSyntax>();

        foreach (var member in membersWithAttribs)
        {
            if (HasAttribute(member, "AllowAnonymous"))
            {
                var typeName = member is TypeDeclarationSyntax td
                    ? td.Identifier.Text
                    : GetContainingTypeName(member);

                findings.Add(new Finding
                {
                    Category = "allowAnonymous",
                    Severity = "warning",
                    File = filePath,
                    Line = GetLine(member),
                    Project = projectName,
                    Type = typeName,
                    Message = $"[AllowAnonymous] found — verify this endpoint intentionally bypasses authentication."
                });
            }
        }
    }

    // ── Finding 6: Missing Authorization on Controllers ───────────────────────

    private static void AnalyzeMissingAuthorization(
        Compilation compilation, string projectName, List<Finding> findings, string? solutionDir)
    {
        // First pass: does the project use [Authorize] at all?
        bool projectUsesAuthorize = false;
        var allRoots = compilation.SyntaxTrees
            .Where(t => solutionDir == null || SourceFileFilter.ShouldAnalyze(t.FilePath, solutionDir))
            .Select(t => t.GetRoot())
            .ToList();

        foreach (var root in allRoots)
        {
            var allMembers = root.DescendantNodes().OfType<MemberDeclarationSyntax>();
            if (allMembers.Any(m => HasAttribute(m, "Authorize")))
            {
                projectUsesAuthorize = true;
                break;
            }
        }

        if (!projectUsesAuthorize)
            return;

        // Second pass: find controller classes missing both [Authorize] and [AllowAnonymous].
        // Partial classes can place attributes on any declaration, so evaluate the union.
        var controllersByName = allRoots
            .SelectMany(r => r.DescendantNodes().OfType<ClassDeclarationSyntax>())
            .Where(c => c.Identifier.Text.EndsWith("Controller", StringComparison.Ordinal))
            .GroupBy(c => c.Identifier.Text, StringComparer.Ordinal);

        foreach (var controllerGroup in controllersByName)
        {
            bool hasAuthorize = controllerGroup.Any(cls => HasAttribute(cls, "Authorize"));
            bool hasAllowAnonymous = controllerGroup.Any(cls => HasAttribute(cls, "AllowAnonymous"));

            if (!hasAuthorize && !hasAllowAnonymous)
            {
                var cls = controllerGroup.First();
                findings.Add(new Finding
                {
                    Category = "missingAuthorization",
                    Severity = "warning",
                    File = cls.SyntaxTree.FilePath,
                    Line = GetLine(cls),
                    Project = projectName,
                    Type = controllerGroup.Key,
                    Message = $"Controller '{controllerGroup.Key}' has no [Authorize] or [AllowAnonymous] attribute, but the project uses authorization."
                });
            }
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static bool HasAttribute(MemberDeclarationSyntax member, string attributeName)
    {
        return member.AttributeLists
            .SelectMany(al => al.Attributes)
            .Any(a =>
            {
                var name = a.Name.ToString();
                // Match "AllowAnonymous" or "AllowAnonymousAttribute"
                return name == attributeName ||
                       name == attributeName + "Attribute" ||
                       name.EndsWith("." + attributeName, StringComparison.Ordinal) ||
                       name.EndsWith("." + attributeName + "Attribute", StringComparison.Ordinal);
            });
    }

    private static string? GetContainingTypeName(SyntaxNode node)
    {
        return node.Ancestors()
            .OfType<TypeDeclarationSyntax>()
            .FirstOrDefault()
            ?.Identifier.Text;
    }

    private static int GetLine(SyntaxNode node)
    {
        return node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
    }
}
