using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeMetrics.AI.Probes;

internal static class ArchitectureLayeringAnalyzer
{
    private static readonly string[] CrossCuttingPrefixes =
    [
        "ILogger", "IMapper", "IMediator", "IConfiguration", "IOptions",
        "IHttpClientFactory", "IMemoryCache", "IDistributedCache"
    ];

    private static readonly string[] InfrastructureKeywords =
    [
        "Gateway", "Client", "Context", "Repository", "Infrastructure"
    ];

    private static readonly string[] DataKeywords =
    [
        "DbContext", "Context", "Repository", "DAL"
    ];

    public static void Analyze(
        SyntaxNode root,
        SemanticModel semanticModel,
        string filePath,
        string projectName,
        List<Finding> findings)
    {
        foreach (var typeDecl in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
            AnalyzeType(typeDecl, semanticModel, filePath, projectName, findings);
    }

    private static void AnalyzeType(
        TypeDeclarationSyntax typeDecl,
        SemanticModel semanticModel,
        string filePath,
        string projectName,
        List<Finding> findings)
    {
        var typeName = typeDecl.Identifier.Text;
        var constructorParams = ConstructorParameterReader.GetAll(typeDecl, semanticModel);

        if (typeName.EndsWith("Controller", StringComparison.Ordinal))
            AddControllerFindings(typeName, constructorParams, filePath, projectName, findings);
        else if (typeName.EndsWith("Service", StringComparison.Ordinal))
            AddServiceFindings(typeName, constructorParams, filePath, projectName, findings);
    }

    private static void AddControllerFindings(
        string typeName,
        List<ConstructorParameterInfo> constructorParams,
        string filePath,
        string projectName,
        List<Finding> findings)
    {
        foreach (var parameter in constructorParams)
        {
            if (IsCrossCuttingType(parameter.TypeName) || !IsDataType(parameter.TypeName))
                continue;

            findings.Add(new Finding
            {
                Category = "controllerDataDependency",
                Severity = "error",
                File = filePath,
                Line = parameter.Line,
                Project = projectName,
                Type = typeName,
                Message = $"Controller '{typeName}' directly depends on data-layer type '{parameter.TypeName}'. " +
                          "Controllers should not depend on DbContext, Repository, or DAL types."
            });
        }
    }

    private static void AddServiceFindings(
        string typeName,
        List<ConstructorParameterInfo> constructorParams,
        string filePath,
        string projectName,
        List<Finding> findings)
    {
        foreach (var parameter in constructorParams)
        {
            if (IsAbstractedOrFrameworkType(parameter) || !IsInfrastructureType(parameter.TypeName))
                continue;

            findings.Add(new Finding
            {
                Category = "concreteInfrastructureDependency",
                Severity = "warning",
                File = filePath,
                Line = parameter.Line,
                Project = projectName,
                Type = typeName,
                Message = $"Service '{typeName}' depends on concrete infrastructure type '{parameter.TypeName}'. " +
                          "Prefer depending on abstractions (interfaces)."
            });
        }
    }

    private static bool IsAbstractedOrFrameworkType(ConstructorParameterInfo parameter)
    {
        return GetUnqualifiedTypeName(parameter.TypeName).StartsWith("I", StringComparison.Ordinal)
            || IsFrameworkNamespace(parameter.Namespace);
    }

    private static bool IsDataType(string typeName)
    {
        return DataKeywords.Any(keyword =>
            typeName.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsInfrastructureType(string typeName)
    {
        return InfrastructureKeywords.Any(keyword =>
            typeName.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsCrossCuttingType(string typeName)
    {
        return CrossCuttingPrefixes.Any(prefix =>
            typeName.StartsWith(prefix, StringComparison.Ordinal));
    }

    private static bool IsFrameworkNamespace(string? namespaceName)
    {
        return namespaceName?.StartsWith("Microsoft.Extensions.", StringComparison.Ordinal) == true ||
               namespaceName?.StartsWith("Microsoft.AspNetCore.", StringComparison.Ordinal) == true;
    }

    private static string GetUnqualifiedTypeName(string typeName)
    {
        var genericTick = typeName.IndexOf('<');
        var withoutGeneric = genericTick >= 0 ? typeName[..genericTick] : typeName;
        var dot = withoutGeneric.LastIndexOf('.');
        return dot >= 0 ? withoutGeneric[(dot + 1)..] : withoutGeneric;
    }
}

internal sealed record ConstructorParameterInfo(string TypeName, string? Namespace, int Line);

internal static class ConstructorParameterReader
{
    public static List<ConstructorParameterInfo> GetAll(
        TypeDeclarationSyntax typeDecl,
        SemanticModel semanticModel)
    {
        var result = new List<ConstructorParameterInfo>();

        AddConstructorParameters(typeDecl, semanticModel, result);
        AddRecordPrimaryConstructorParameters(typeDecl, semanticModel, result);
        AddClassPrimaryConstructorParameters(typeDecl, semanticModel, result);

        return result;
    }

    private static void AddConstructorParameters(
        TypeDeclarationSyntax typeDecl,
        SemanticModel semanticModel,
        List<ConstructorParameterInfo> result)
    {
        foreach (var ctor in typeDecl.Members.OfType<ConstructorDeclarationSyntax>())
        {
            foreach (var param in ctor.ParameterList.Parameters)
                AddParameterType(param, semanticModel, result);
        }
    }

    private static void AddRecordPrimaryConstructorParameters(
        TypeDeclarationSyntax typeDecl,
        SemanticModel semanticModel,
        List<ConstructorParameterInfo> result)
    {
        if (typeDecl is not RecordDeclarationSyntax { ParameterList: not null } record)
            return;

        foreach (var param in record.ParameterList.Parameters)
            AddParameterType(param, semanticModel, result);
    }

    private static void AddClassPrimaryConstructorParameters(
        TypeDeclarationSyntax typeDecl,
        SemanticModel semanticModel,
        List<ConstructorParameterInfo> result)
    {
        if (typeDecl is not ClassDeclarationSyntax { ParameterList: not null } classDecl)
            return;

        foreach (var param in classDecl.ParameterList.Parameters)
            AddParameterType(param, semanticModel, result);
    }

    private static void AddParameterType(
        ParameterSyntax parameter,
        SemanticModel semanticModel,
        List<ConstructorParameterInfo> result)
    {
        var typeName = parameter.Type?.ToString();
        if (string.IsNullOrEmpty(typeName))
            return;

        var typeSymbol = semanticModel.GetTypeInfo(parameter.Type!).Type;
        var namespaceName = typeSymbol?.ContainingNamespace?.ToDisplayString();
        var line = parameter.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
        result.Add(new ConstructorParameterInfo(typeName, namespaceName, line));
    }
}
