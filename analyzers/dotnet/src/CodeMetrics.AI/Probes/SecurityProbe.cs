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
            foreach (var tree in SourceFileFilter.AnalyzableTrees(compilation, solutionDir))
            {
                var root = tree.GetRoot();
                var filePath = tree.FilePath;

                AnalyzeHardcodedSecrets(root, filePath, projectName, findings);
                AnalyzeRawSqlInterpolation(root, filePath, projectName, findings);
                AnalyzeUnsafeDeserialization(root, filePath, projectName, findings);
                AnalyzeAllowAnyOriginWithCredentials(root, compilation.GetSemanticModel(tree), filePath, projectName, findings);
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

        var decision = ScoringDecision.FirstMatch("dotnet/security/identifier-cors-flow-v2", new()
        {
            ["hardcodedSecrets"] = hardcodedSecrets,
            ["allowAnyOriginWithCreds"] = allowAnyOriginWithCreds,
            ["rawSqlCount"] = rawSqlCount,
            ["unsafeDeser"] = unsafeDeser,
            ["importedVulnerabilities"] = importedVulnerabilities,
            ["warnings"] = warnings
        },
        ScoringStep.Rule("manySecrets", "hardcodedSecrets > 2", hardcodedSecrets > 2, 0, ["hardcodedSecret"]),
        ScoringStep.Rule("unsafeCors", "allowAnyOriginWithCreds > 0", allowAnyOriginWithCreds > 0, 0, ["allowAnyOriginWithCredentials"]),
        ScoringStep.Rule("secret", "hardcodedSecrets > 0", hardcodedSecrets > 0, 2, ["hardcodedSecret"]),
        ScoringStep.Rule("rawSql", "rawSqlCount > 0", rawSqlCount > 0, 2, ["rawSqlInterpolation"]),
        ScoringStep.Rule("importedVulnerabilities", "importedVulnerabilities > 0", importedVulnerabilities > 0, 2, []),
        ScoringStep.Rule("unsafeDeserialization", "unsafeDeser > 0", unsafeDeser > 0, 4, ["unsafeDeserialization"]),
        ScoringStep.Rule("manyWarnings", "warnings > 2", warnings > 2, 6, findings.Where(f => f.Severity == "warning").Select(f => f.Category).Distinct().ToArray()),
        ScoringStep.Rule("warnings", "warnings > 0", warnings > 0, 8, findings.Where(f => f.Severity == "warning").Select(f => f.Category).Distinct().ToArray()),
        ScoringStep.Rule("clean", "otherwise", true, 10, []));

        var basis = $"Findings: {findings.Count} (errors: {errors}, warnings: {warnings}). " +
                    $"hardcodedSecrets={hardcodedSecrets}, rawSql={rawSqlCount}, " +
                    $"unsafeDeserialization={unsafeDeser}, allowAnyOriginWithCredentials={allowAnyOriginWithCreds}, " +
                    $"importedVulnerabilities={importedVulnerabilities}.";

        return new DimensionResult
        {
            Status = "scored",
            Score = decision.FinalScore,
            ScoringDecision = decision,
            Basis = basis,
            Findings = findings
        };
    }

    // ── Finding 1: Hardcoded Secrets ─────────────────────────────────────────

    private static void AnalyzeHardcodedSecrets(
        SyntaxNode root, string filePath, string projectName, List<Finding> findings)
    {
        // Preserve declaration-before-assignment finding order, even when source
        // locations interleave. Both forms use the same literal/placeholder policy.
        foreach (var variable in root.DescendantNodes().OfType<VariableDeclaratorSyntax>())
        {
            var name = variable.Identifier.Text;
            if (IsSecretLiteral(name, variable.Initializer?.Value))
                findings.Add(CreateSecretFinding(variable, filePath, projectName,
                    $"Variable '{name}' appears to contain a hardcoded secret."));
        }
        foreach (var assignment in root.DescendantNodes().OfType<AssignmentExpressionSyntax>())
        {
            var name = AssignmentName(assignment);
            if (IsSecretLiteral(name, assignment.Right))
                findings.Add(CreateSecretFinding(assignment, filePath, projectName,
                    $"Assignment to '{name}' appears to contain a hardcoded secret."));
        }
    }

    private static string AssignmentName(AssignmentExpressionSyntax assignment)
    {
        var text = assignment.Left.ToString();
        return text[(text.LastIndexOf('.') + 1)..];
    }

    private static bool IsSecretLiteral(string name, ExpressionSyntax? expression) =>
        ContainsSecretKeyword(name) && expression is LiteralExpressionSyntax literal &&
        literal.IsKind(SyntaxKind.StringLiteralExpression) && literal.Token.ValueText.Length >= 16 &&
        !ContainsSafePlaceholder(literal.Token.ValueText) &&
        !SecretIdentifierRecognition.IsIdentifier(name, literal.Token.ValueText);

    private static Finding CreateSecretFinding(SyntaxNode node, string filePath, string projectName, string message) => new()
    {
        Category = "hardcodedSecret",
        Severity = "error",
        File = filePath,
        Line = GetLine(node),
        Project = projectName,
        Type = GetContainingTypeName(node),
        Message = message
    };

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
        SyntaxNode root, SemanticModel model, string filePath, string projectName, List<Finding> findings)
    {
        foreach (var node in CorsPolicyAnalysis.FindUnsafePairs(root, model))
        {
            findings.Add(new Finding
            {
                Category = "allowAnyOriginWithCredentials",
                Severity = "error",
                File = filePath,
                Line = GetLine(node),
                Project = projectName,
                Type = GetContainingTypeName(node),
                Message = "The same CORS builder enables AllowAnyOrigin() and AllowCredentials() without a recognized rejecting guard."
            });
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
        foreach (var issue in ControllerAuthorizationAnalysis.FindMissingIntent(compilation, solutionDir))
            findings.Add(CreateMissingAuthorizationFinding(issue, projectName));
    }

    private static Finding CreateMissingAuthorizationFinding(
        ControllerAuthorizationAnalysis.MissingIntent issue, string projectName)
    {
        var controller = issue.Controller;
        return new Finding
        {
            Category = "missingAuthorization",
            Severity = "warning",
            Confidence = "medium",
            File = controller.Declaration.SyntaxTree.FilePath,
            Line = GetLine(controller.Declaration),
            Project = projectName,
            Type = controller.Symbol.Name,
            Message = $"Controller '{controller.Symbol.ToDisplayString()}' has no explicit class-level " +
                      $"[Authorize]/[AllowAnonymous] intent and {issue.UnannotatedActionCount} public action(s) " +
                      "also lack an explicit authorization attribute. Global filters or a fallback policy " +
                      "may still protect the endpoint; verify the effective policy."
        };
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
