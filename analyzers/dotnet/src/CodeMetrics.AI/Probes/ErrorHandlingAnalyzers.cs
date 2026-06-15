using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeMetrics.AI.Probes;

internal static class ErrorHandlingProjectAnalyzer
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
        foreach (var tree in compilation.SyntaxTrees)
        {
            if (solutionDir != null && !SourceFileFilter.ShouldAnalyze(tree.FilePath, solutionDir))
                continue;

            ErrorHandlingFileAnalyzer.Analyze(tree.GetRoot(), tree.FilePath, projectName, findings);
        }
    }
}

internal static class ErrorHandlingFileAnalyzer
{
    public static void Analyze(SyntaxNode root, string filePath, string projectName, List<Finding> findings)
    {
        CatchBlockAnalyzer.Analyze(root, filePath, projectName, findings);
        SyncBlockingCallAnalyzer.Analyze(root, filePath, projectName, findings);
        ConsoleWriteLineAnalyzer.Analyze(root, filePath, projectName, findings);
        MissingLoggerAnalyzer.Analyze(root, filePath, projectName, findings);
    }
}

internal static class CatchBlockAnalyzer
{
    public static void Analyze(SyntaxNode root, string filePath, string projectName, List<Finding> findings)
    {
        foreach (var catchClause in root.DescendantNodes().OfType<CatchClauseSyntax>())
            AnalyzeCatch(catchClause, filePath, projectName, findings);
    }

    private static void AnalyzeCatch(
        CatchClauseSyntax catchClause,
        string filePath,
        string projectName,
        List<Finding> findings)
    {
        if (catchClause.Block.Statements.Count == 0)
        {
            findings.Add(CreateFinding("emptyCatch", "error", catchClause, filePath, projectName, "Empty catch block suppresses exceptions silently."));
            return;
        }

        AddThrowExFindings(catchClause, filePath, projectName, findings);
        AddBroadCatchFindings(catchClause, filePath, projectName, findings);
    }

    private static void AddThrowExFindings(
        CatchClauseSyntax catchClause,
        string filePath,
        string projectName,
        List<Finding> findings)
    {
        var caughtVarName = catchClause.Declaration?.Identifier.Text;
        if (string.IsNullOrEmpty(caughtVarName))
            return;

        foreach (var throwStmt in catchClause.Block.DescendantNodes().OfType<ThrowStatementSyntax>())
        {
            if (throwStmt.Expression is not IdentifierNameSyntax id || id.Identifier.Text != caughtVarName)
                continue;

            findings.Add(CreateFinding(
                "throwEx",
                "error",
                throwStmt,
                filePath,
                projectName,
                $"'throw {caughtVarName};' loses the original stack trace. Use bare 'throw;' instead."));
        }
    }

    private static void AddBroadCatchFindings(
        CatchClauseSyntax catchClause,
        string filePath,
        string projectName,
        List<Finding> findings)
    {
        if (!CatchBlockFacts.IsBroadCatch(catchClause))
            return;

        if (!CatchBlockFacts.HasLoggingCall(catchClause.Block) && !CatchBlockFacts.HasBareRethrow(catchClause.Block))
            findings.Add(CreateFinding("broadCatchWithoutLoggingOrRethrow", "warning", catchClause, filePath, projectName, "Broad catch block without logging or rethrow swallows exceptions."));

        if (CatchBlockFacts.ReturnsDefault(catchClause.Block))
            findings.Add(CreateFinding("broadCatchReturnsDefault", "error", catchClause, filePath, projectName, "Broad catch block returns a default value, hiding exceptions."));
    }

    private static Finding CreateFinding(
        string category,
        string severity,
        SyntaxNode node,
        string filePath,
        string projectName,
        string message)
    {
        return new Finding
        {
            Category = category,
            Severity = severity,
            File = filePath,
            Line = SyntaxLocation.GetLine(node),
            Project = projectName,
            Type = SyntaxLocation.GetContainingTypeName(node),
            Message = message
        };
    }
}

internal static class CatchBlockFacts
{
    public static bool IsBroadCatch(CatchClauseSyntax catchClause)
    {
        if (catchClause.Filter != null)
            return false;
        if (catchClause.Declaration == null)
            return true;

        var typeName = catchClause.Declaration.Type.ToString();
        return typeName == "Exception" || typeName == "System.Exception";
    }

    public static bool HasLoggingCall(BlockSyntax block)
    {
        return block.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Any(inv => inv.Expression.ToString().Contains("Log", StringComparison.OrdinalIgnoreCase));
    }

    public static bool HasBareRethrow(BlockSyntax block)
    {
        return block.DescendantNodes()
            .OfType<ThrowStatementSyntax>()
            .Any(statement => statement.Expression == null);
    }

    public static bool ReturnsDefault(BlockSyntax block)
    {
        return block.DescendantNodes()
            .OfType<ReturnStatementSyntax>()
            .Any(ReturnExpressionFacts.IsDefaultLike);
    }
}

internal static class ReturnExpressionFacts
{
    public static bool IsDefaultLike(ReturnStatementSyntax returnStatement)
    {
        return returnStatement.Expression switch
        {
            null => false,
            LiteralExpressionSyntax literal => IsDefaultLiteral(literal),
            DefaultExpressionSyntax => true,
            MemberAccessExpressionSyntax memberAccess => IsStringEmpty(memberAccess),
            _ => false
        };
    }

    private static bool IsDefaultLiteral(LiteralExpressionSyntax literal)
    {
        return literal.IsKind(SyntaxKind.NullLiteralExpression)
            || literal.IsKind(SyntaxKind.FalseLiteralExpression)
            || literal.IsKind(SyntaxKind.DefaultLiteralExpression)
            || literal.IsKind(SyntaxKind.NumericLiteralExpression) && literal.Token.ValueText == "0";
    }

    private static bool IsStringEmpty(MemberAccessExpressionSyntax memberAccess)
    {
        return memberAccess.Expression.ToString() == "string"
            && memberAccess.Name.Identifier.Text == "Empty";
    }
}

internal static class SyncBlockingCallAnalyzer
{
    public static void Analyze(SyntaxNode root, string filePath, string projectName, List<Finding> findings)
    {
        foreach (var memberAccess in root.DescendantNodes().OfType<MemberAccessExpressionSyntax>())
            AddMemberAccessFinding(memberAccess, filePath, projectName, findings);

        foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            AddWaitInvocationFinding(invocation, filePath, projectName, findings);
    }

    private static void AddMemberAccessFinding(
        MemberAccessExpressionSyntax memberAccess,
        string filePath,
        string projectName,
        List<Finding> findings)
    {
        var memberName = memberAccess.Name.Identifier.Text;
        if (memberName == "Result")
            findings.Add(CreateFinding(memberAccess, filePath, projectName, "'.Result' blocks the calling thread and can cause deadlocks. Use 'await' instead."));
        else if (IsGetAwaiterGetResult(memberAccess))
            findings.Add(CreateFinding(memberAccess, filePath, projectName, "'.GetAwaiter().GetResult()' blocks the calling thread and can cause deadlocks. Use 'await' instead."));
    }

    private static void AddWaitInvocationFinding(
        InvocationExpressionSyntax invocation,
        string filePath,
        string projectName,
        List<Finding> findings)
    {
        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess &&
            memberAccess.Name.Identifier.Text == "Wait")
        {
            findings.Add(CreateFinding(invocation, filePath, projectName, "'.Wait()' blocks the calling thread and can cause deadlocks. Use 'await' instead."));
        }
    }

    private static bool IsGetAwaiterGetResult(MemberAccessExpressionSyntax memberAccess)
    {
        return memberAccess.Name.Identifier.Text == "GetResult"
            && memberAccess.Expression is InvocationExpressionSyntax invocation
            && invocation.Expression is MemberAccessExpressionSyntax innerMemberAccess
            && innerMemberAccess.Name.Identifier.Text == "GetAwaiter";
    }

    private static Finding CreateFinding(SyntaxNode node, string filePath, string projectName, string message)
    {
        return new Finding
        {
            Category = "syncBlockingCall",
            Severity = "warning",
            File = filePath,
            Line = SyntaxLocation.GetLine(node),
            Project = projectName,
            Type = SyntaxLocation.GetContainingTypeName(node),
            Message = message
        };
    }
}

internal static class ConsoleWriteLineAnalyzer
{
    public static void Analyze(SyntaxNode root, string filePath, string projectName, List<Finding> findings)
    {
        foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (!IsConsoleWriteLine(invocation))
                continue;

            findings.Add(new Finding
            {
                Category = "consoleWriteLine",
                Severity = "info",
                File = filePath,
                Line = SyntaxLocation.GetLine(invocation),
                Project = projectName,
                Type = SyntaxLocation.GetContainingTypeName(invocation),
                Message = "Console.WriteLine found. Prefer structured logging."
            });
        }
    }

    private static bool IsConsoleWriteLine(InvocationExpressionSyntax invocation)
    {
        return invocation.Expression is MemberAccessExpressionSyntax memberAccess
            && memberAccess.Expression is IdentifierNameSyntax identifier
            && identifier.Identifier.Text == "Console"
            && memberAccess.Name.Identifier.Text == "WriteLine";
    }
}

internal static class MissingLoggerAnalyzer
{
    public static void Analyze(SyntaxNode root, string filePath, string projectName, List<Finding> findings)
    {
        foreach (var typeDecl in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
        {
            var catchCount = typeDecl.DescendantNodes().OfType<CatchClauseSyntax>().Count();
            if (catchCount < 2 || LoggerMemberInspector.HasLoggerMember(typeDecl))
                continue;

            findings.Add(new Finding
            {
                Category = "missingLoggerForMultipleCatches",
                Severity = "warning",
                File = filePath,
                Line = SyntaxLocation.GetLine(typeDecl),
                Project = projectName,
                Type = typeDecl.Identifier.Text,
                Message = $"Type '{typeDecl.Identifier.Text}' has {catchCount} catch blocks but no ILogger field/property/parameter."
            });
        }
    }
}

internal static class LoggerMemberInspector
{
    public static bool HasLoggerMember(TypeDeclarationSyntax typeDecl)
    {
        return HasLoggerField(typeDecl)
            || HasLoggerProperty(typeDecl)
            || HasLoggerConstructorParameter(typeDecl);
    }

    private static bool HasLoggerField(TypeDeclarationSyntax typeDecl)
    {
        return typeDecl.Members
            .OfType<FieldDeclarationSyntax>()
            .Any(field => field.Declaration.Type.ToString().Contains("ILogger"));
    }

    private static bool HasLoggerProperty(TypeDeclarationSyntax typeDecl)
    {
        return typeDecl.Members
            .OfType<PropertyDeclarationSyntax>()
            .Any(property => property.Type.ToString().Contains("ILogger"));
    }

    private static bool HasLoggerConstructorParameter(TypeDeclarationSyntax typeDecl)
    {
        return typeDecl.Members
            .OfType<ConstructorDeclarationSyntax>()
            .SelectMany(ctor => ctor.ParameterList.Parameters)
            .Any(parameter => parameter.Type?.ToString().Contains("ILogger") == true);
    }
}

internal sealed record ErrorHandlingFindingCounts(
    int EmptyCatches,
    int ThrowExes,
    int BroadDefaults,
    bool HasSyncBlockingCall,
    int Warnings,
    int Errors)
{
    public static ErrorHandlingFindingCounts From(IReadOnlyList<Finding> findings)
    {
        return new ErrorHandlingFindingCounts(
            findings.Count(f => f.Category == "emptyCatch"),
            findings.Count(f => f.Category == "throwEx"),
            findings.Count(f => f.Category == "broadCatchReturnsDefault"),
            findings.Any(f => f.Category == "syncBlockingCall"),
            findings.Count(f => f.Severity == "warning"),
            findings.Count(f => f.Severity == "error"));
    }
}
