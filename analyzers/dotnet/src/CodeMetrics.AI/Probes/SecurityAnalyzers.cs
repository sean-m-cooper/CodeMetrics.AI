using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeMetrics.AI.Probes;

internal static class SecurityProjectAnalyzer
{
    public static List<Finding> Analyze(
        IReadOnlyList<(string ProjectName, Compilation Compilation)> projects,
        string? solutionDir)
    {
        var findings = new List<Finding>();

        foreach (var (projectName, compilation) in projects)
            AnalyzeProject(projectName, compilation, findings, solutionDir);

        return findings;
    }

    private static void AnalyzeProject(
        string projectName,
        Compilation compilation,
        List<Finding> findings,
        string? solutionDir)
    {
        foreach (var tree in compilation.SyntaxTrees.Where(tree => ShouldAnalyze(tree.FilePath, solutionDir)))
            SecurityFileAnalyzer.Analyze(tree.GetRoot(), tree.FilePath, projectName, findings);

        MissingAuthorizationAnalyzer.Analyze(compilation, projectName, findings, solutionDir);
    }

    private static bool ShouldAnalyze(string filePath, string? solutionDir)
    {
        return solutionDir == null || SourceFileFilter.ShouldAnalyze(filePath, solutionDir);
    }
}

internal static class SecurityFileAnalyzer
{
    public static void Analyze(SyntaxNode root, string filePath, string projectName, List<Finding> findings)
    {
        HardcodedSecretAnalyzer.Analyze(root, filePath, projectName, findings);
        RawSqlInterpolationAnalyzer.Analyze(root, filePath, projectName, findings);
        UnsafeDeserializationAnalyzer.Analyze(root, filePath, projectName, findings);
        CorsMisconfigurationAnalyzer.Analyze(root, filePath, projectName, findings);
        AllowAnonymousAnalyzer.Analyze(root, filePath, projectName, findings);
    }
}

internal static class HardcodedSecretAnalyzer
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

    public static void Analyze(SyntaxNode root, string filePath, string projectName, List<Finding> findings)
    {
        foreach (var variable in root.DescendantNodes().OfType<VariableDeclaratorSyntax>())
            AnalyzeVariable(variable, filePath, projectName, findings);

        foreach (var assignment in root.DescendantNodes().OfType<AssignmentExpressionSyntax>())
            AnalyzeAssignment(assignment, filePath, projectName, findings);
    }

    private static void AnalyzeVariable(
        VariableDeclaratorSyntax variable,
        string filePath,
        string projectName,
        List<Finding> findings)
    {
        var name = variable.Identifier.Text;
        if (!ContainsSecretKeyword(name) || !IsUnsafeStringLiteral(variable.Initializer?.Value))
            return;

        findings.Add(CreateFinding(name, variable, filePath, projectName, $"Variable '{name}' appears to contain a hardcoded secret."));
    }

    private static void AnalyzeAssignment(
        AssignmentExpressionSyntax assignment,
        string filePath,
        string projectName,
        List<Finding> findings)
    {
        var name = ExtractAssignedName(assignment.Left.ToString());
        if (!ContainsSecretKeyword(name) || !IsUnsafeStringLiteral(assignment.Right))
            return;

        findings.Add(CreateFinding(name, assignment, filePath, projectName, $"Assignment to '{name}' appears to contain a hardcoded secret."));
    }

    private static bool IsUnsafeStringLiteral(ExpressionSyntax? expression)
    {
        return expression is LiteralExpressionSyntax literal
            && literal.IsKind(SyntaxKind.StringLiteralExpression)
            && literal.Token.ValueText.Length >= 16
            && !ContainsSafePlaceholder(literal.Token.ValueText);
    }

    private static Finding CreateFinding(
        string name,
        SyntaxNode node,
        string filePath,
        string projectName,
        string message)
    {
        return new Finding
        {
            Category = "hardcodedSecret",
            Severity = "error",
            File = filePath,
            Line = SyntaxLocation.GetLine(node),
            Project = projectName,
            Type = SyntaxLocation.GetContainingTypeName(node),
            Message = message
        };
    }

    private static string ExtractAssignedName(string leftText)
    {
        return leftText.Contains('.')
            ? leftText[(leftText.LastIndexOf('.') + 1)..]
            : leftText;
    }

    private static bool ContainsSecretKeyword(string name)
    {
        var lower = name.ToLowerInvariant();
        return SecretKeywords.Any(lower.Contains);
    }

    private static bool ContainsSafePlaceholder(string value)
    {
        var lower = value.ToLowerInvariant();
        return SafePlaceholders.Any(lower.Contains);
    }
}

internal static class RawSqlInterpolationAnalyzer
{
    public static void Analyze(SyntaxNode root, string filePath, string projectName, List<Finding> findings)
    {
        foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            AnalyzeInvocation(invocation, filePath, projectName, findings);
    }

    private static void AnalyzeInvocation(
        InvocationExpressionSyntax invocation,
        string filePath,
        string projectName,
        List<Finding> findings)
    {
        var methodName = SecuritySyntaxNames.GetMethodName(invocation);
        if (methodName == null || !methodName.Contains("Sql", StringComparison.OrdinalIgnoreCase))
            return;

        if (!invocation.ArgumentList.Arguments.Any(arg => IsUnsafeSqlArgument(arg.Expression)))
            return;

        findings.Add(new Finding
        {
            Category = "rawSqlInterpolation",
            Severity = "error",
            File = filePath,
            Line = SyntaxLocation.GetLine(invocation),
            Project = projectName,
            Type = SyntaxLocation.GetContainingTypeName(invocation),
            Message = $"Method '{methodName}' called with interpolated/concatenated SQL string - potential SQL injection."
        });
    }

    private static bool IsUnsafeSqlArgument(ExpressionSyntax expression)
    {
        return expression is InterpolatedStringExpressionSyntax ||
               expression is BinaryExpressionSyntax binary && binary.IsKind(SyntaxKind.AddExpression);
    }
}

internal static class UnsafeDeserializationAnalyzer
{
    public static void Analyze(SyntaxNode root, string filePath, string projectName, List<Finding> findings)
    {
        foreach (var creation in root.DescendantNodes().OfType<ObjectCreationExpressionSyntax>())
        {
            var shortName = SecuritySyntaxNames.GetShortTypeName(creation.Type.ToString());
            if (shortName is not ("BinaryFormatter" or "NetDataContractSerializer"))
                continue;

            findings.Add(new Finding
            {
                Category = "unsafeDeserialization",
                Severity = "error",
                File = filePath,
                Line = SyntaxLocation.GetLine(creation),
                Project = projectName,
                Type = SyntaxLocation.GetContainingTypeName(creation),
                Message = $"Use of '{shortName}' is unsafe and vulnerable to deserialization attacks."
            });
        }
    }
}

internal static class CorsMisconfigurationAnalyzer
{
    public static void Analyze(SyntaxNode root, string filePath, string projectName, List<Finding> findings)
    {
        foreach (var statement in root.DescendantNodes().OfType<ExpressionStatementSyntax>())
        {
            var invocationNames = statement.DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Select(SecuritySyntaxNames.GetMethodName)
                .Where(name => name != null)
                .ToHashSet(StringComparer.Ordinal)!;

            if (!invocationNames.Contains("AllowAnyOrigin") || !invocationNames.Contains("AllowCredentials"))
                continue;

            findings.Add(new Finding
            {
                Category = "allowAnyOriginWithCredentials",
                Severity = "error",
                File = filePath,
                Line = SyntaxLocation.GetLine(statement),
                Project = projectName,
                Type = SyntaxLocation.GetContainingTypeName(statement),
                Message = "Combining AllowAnyOrigin() and AllowCredentials() is a CORS misconfiguration that violates the spec."
            });
        }
    }
}

internal static class AllowAnonymousAnalyzer
{
    public static void Analyze(SyntaxNode root, string filePath, string projectName, List<Finding> findings)
    {
        foreach (var member in root.DescendantNodes().OfType<MemberDeclarationSyntax>())
        {
            if (!SecurityAttributeInspector.HasAttribute(member, "AllowAnonymous"))
                continue;

            findings.Add(new Finding
            {
                Category = "allowAnonymous",
                Severity = "warning",
                File = filePath,
                Line = SyntaxLocation.GetLine(member),
                Project = projectName,
                Type = member is TypeDeclarationSyntax type ? type.Identifier.Text : SyntaxLocation.GetContainingTypeName(member),
                Message = "[AllowAnonymous] found - verify this endpoint intentionally bypasses authentication."
            });
        }
    }
}

internal static class MissingAuthorizationAnalyzer
{
    public static void Analyze(
        Compilation compilation,
        string projectName,
        List<Finding> findings,
        string? solutionDir)
    {
        var roots = compilation.SyntaxTrees
            .Where(tree => solutionDir == null || SourceFileFilter.ShouldAnalyze(tree.FilePath, solutionDir))
            .Select(tree => tree.GetRoot())
            .ToList();

        if (!ProjectUsesAuthorize(roots))
            return;

        foreach (var controllerGroup in GetControllerGroups(roots))
            AddMissingAuthorizationFinding(controllerGroup, projectName, findings);
    }

    private static bool ProjectUsesAuthorize(List<SyntaxNode> roots)
    {
        return roots
            .SelectMany(root => root.DescendantNodes().OfType<MemberDeclarationSyntax>())
            .Any(member => SecurityAttributeInspector.HasAttribute(member, "Authorize"));
    }

    private static IEnumerable<IGrouping<string, ClassDeclarationSyntax>> GetControllerGroups(List<SyntaxNode> roots)
    {
        return roots
            .SelectMany(root => root.DescendantNodes().OfType<ClassDeclarationSyntax>())
            .Where(cls => cls.Identifier.Text.EndsWith("Controller", StringComparison.Ordinal))
            .GroupBy(cls => cls.Identifier.Text, StringComparer.Ordinal);
    }

    private static void AddMissingAuthorizationFinding(
        IGrouping<string, ClassDeclarationSyntax> controllerGroup,
        string projectName,
        List<Finding> findings)
    {
        var hasAuthorize = controllerGroup.Any(cls => SecurityAttributeInspector.HasAttribute(cls, "Authorize"));
        var hasAllowAnonymous = controllerGroup.Any(cls => SecurityAttributeInspector.HasAttribute(cls, "AllowAnonymous"));
        if (hasAuthorize || hasAllowAnonymous)
            return;

        var cls = controllerGroup.First();
        findings.Add(new Finding
        {
            Category = "missingAuthorization",
            Severity = "warning",
            File = cls.SyntaxTree.FilePath,
            Line = SyntaxLocation.GetLine(cls),
            Project = projectName,
            Type = controllerGroup.Key,
            Message = $"Controller '{controllerGroup.Key}' has no [Authorize] or [AllowAnonymous] attribute, but the project uses authorization."
        });
    }
}

internal static class SecurityAttributeInspector
{
    public static bool HasAttribute(MemberDeclarationSyntax member, string attributeName)
    {
        return member.AttributeLists
            .SelectMany(al => al.Attributes)
            .Any(attribute => IsNamedAttribute(attribute, attributeName));
    }

    private static bool IsNamedAttribute(AttributeSyntax attribute, string attributeName)
    {
        var name = attribute.Name.ToString();
        return name == attributeName ||
               name == attributeName + "Attribute" ||
               name.EndsWith("." + attributeName, StringComparison.Ordinal) ||
               name.EndsWith("." + attributeName + "Attribute", StringComparison.Ordinal);
    }
}

internal static class SecuritySyntaxNames
{
    public static string? GetMethodName(InvocationExpressionSyntax invocation)
    {
        return invocation.Expression switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.Text,
            IdentifierNameSyntax identifier => identifier.Identifier.Text,
            _ => null
        };
    }

    public static string GetShortTypeName(string typeName)
    {
        return typeName.Contains('.')
            ? typeName[(typeName.LastIndexOf('.') + 1)..]
            : typeName;
    }
}

internal sealed record SecurityFindingCounts(
    int HardcodedSecrets,
    int AllowAnyOriginWithCredentials,
    int RawSqlInterpolation,
    int UnsafeDeserialization,
    int Errors,
    int Warnings)
{
    public static SecurityFindingCounts From(IReadOnlyList<Finding> findings)
    {
        return new SecurityFindingCounts(
            findings.Count(f => f.Category == "hardcodedSecret"),
            findings.Count(f => f.Category == "allowAnyOriginWithCredentials"),
            findings.Count(f => f.Category == "rawSqlInterpolation"),
            findings.Count(f => f.Category == "unsafeDeserialization"),
            findings.Count(f => f.Severity == "error"),
            findings.Count(f => f.Severity == "warning"));
    }
}
