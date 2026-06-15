using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeMetrics.AI.Metrics;

internal static class MemberSyntaxInfo
{
    public static string? GetName(MemberDeclarationSyntax member)
    {
        return GetMethodName(member)
            ?? GetConstructorName(member)
            ?? GetPropertyName(member)
            ?? GetEventName(member)
            ?? GetFieldName(member)
            ?? GetEventFieldName(member)
            ?? GetOperatorName(member)
            ?? GetConversionOperatorName(member)
            ?? GetIndexerName(member)
            ?? GetDestructorName(member);
    }

    public static bool HasBody(MemberDeclarationSyntax member)
    {
        return HasMethodBody(member)
            || HasConstructorBody(member)
            || HasPropertyBody(member)
            || HasIndexerBody(member)
            || HasOperatorBody(member)
            || HasConversionOperatorBody(member)
            || HasDestructorBody(member);
    }

    private static string? GetMethodName(MemberDeclarationSyntax member)
    {
        return member is MethodDeclarationSyntax method ? method.Identifier.Text : null;
    }

    private static string? GetConstructorName(MemberDeclarationSyntax member)
    {
        return member is ConstructorDeclarationSyntax constructor ? constructor.Identifier.Text : null;
    }

    private static string? GetPropertyName(MemberDeclarationSyntax member)
    {
        return member is PropertyDeclarationSyntax property ? property.Identifier.Text : null;
    }

    private static string? GetEventName(MemberDeclarationSyntax member)
    {
        return member is EventDeclarationSyntax @event ? @event.Identifier.Text : null;
    }

    private static string? GetFieldName(MemberDeclarationSyntax member)
    {
        return member is FieldDeclarationSyntax field
            ? field.Declaration.Variables.FirstOrDefault()?.Identifier.Text
            : null;
    }

    private static string? GetEventFieldName(MemberDeclarationSyntax member)
    {
        return member is EventFieldDeclarationSyntax eventField
            ? eventField.Declaration.Variables.FirstOrDefault()?.Identifier.Text
            : null;
    }

    private static string? GetOperatorName(MemberDeclarationSyntax member)
    {
        return member is OperatorDeclarationSyntax operatorDeclaration
            ? $"operator {operatorDeclaration.OperatorToken.Text}"
            : null;
    }

    private static string? GetConversionOperatorName(MemberDeclarationSyntax member)
    {
        return member is ConversionOperatorDeclarationSyntax conversionOperator
            ? $"operator {conversionOperator.Type}"
            : null;
    }

    private static string? GetIndexerName(MemberDeclarationSyntax member)
    {
        return member is IndexerDeclarationSyntax ? "this[]" : null;
    }

    private static string? GetDestructorName(MemberDeclarationSyntax member)
    {
        return member is DestructorDeclarationSyntax destructor ? $"~{destructor.Identifier.Text}" : null;
    }

    private static bool HasMethodBody(MemberDeclarationSyntax member)
    {
        return member is MethodDeclarationSyntax method && HasBodyOrExpressionBody(method);
    }

    private static bool HasConstructorBody(MemberDeclarationSyntax member)
    {
        return member is ConstructorDeclarationSyntax constructor && HasBodyOrExpressionBody(constructor);
    }

    private static bool HasPropertyBody(MemberDeclarationSyntax member)
    {
        return member is PropertyDeclarationSyntax property
            && (HasAccessorBody(property.AccessorList) || property.ExpressionBody != null);
    }

    private static bool HasIndexerBody(MemberDeclarationSyntax member)
    {
        return member is IndexerDeclarationSyntax indexer
            && (HasAccessorBody(indexer.AccessorList) || indexer.ExpressionBody != null);
    }

    private static bool HasOperatorBody(MemberDeclarationSyntax member)
    {
        return member is OperatorDeclarationSyntax operatorDeclaration
            && HasBodyOrExpressionBody(operatorDeclaration);
    }

    private static bool HasConversionOperatorBody(MemberDeclarationSyntax member)
    {
        return member is ConversionOperatorDeclarationSyntax conversionOperator
            && HasBodyOrExpressionBody(conversionOperator);
    }

    private static bool HasDestructorBody(MemberDeclarationSyntax member)
    {
        return member is DestructorDeclarationSyntax destructor && HasBodyOrExpressionBody(destructor);
    }

    private static bool HasBodyOrExpressionBody(BaseMethodDeclarationSyntax member)
    {
        return member.Body != null || member.ExpressionBody != null;
    }

    private static bool HasAccessorBody(AccessorListSyntax? accessorList)
    {
        return accessorList?.Accessors.Any(a => a.Body != null || a.ExpressionBody != null) == true;
    }
}
