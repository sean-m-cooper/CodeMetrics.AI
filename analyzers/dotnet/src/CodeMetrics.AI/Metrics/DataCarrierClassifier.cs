using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeMetrics.AI.Metrics;

/// <summary>
/// Identifies passive request/response-style types that carry state without implementing behavior.
/// </summary>
public static class DataCarrierClassifier
{
    /// <summary>
    /// Returns whether a source-declared class, record, or struct only carries state.
    /// </summary>
    public static bool IsPassiveDataCarrier(INamedTypeSymbol typeSymbol)
    {
        if (typeSymbol.TypeKind is not (TypeKind.Class or TypeKind.Struct) ||
            typeSymbol.IsStatic ||
            typeSymbol.IsAbstract ||
            HasBehavioralBaseType(typeSymbol))
        {
            return false;
        }

        var declarations = typeSymbol.DeclaringSyntaxReferences
            .Select(reference => reference.GetSyntax())
            .OfType<TypeDeclarationSyntax>()
            .ToList();

        if (declarations.Count == 0 ||
            declarations.SelectMany(declaration => declaration.Members).Any(member => !IsPassiveMember(member)))
        {
            return false;
        }

        return declarations.Any(HasDeclaredState);
    }

    private static bool HasBehavioralBaseType(INamedTypeSymbol typeSymbol)
    {
        var baseType = typeSymbol.BaseType;
        return baseType != null &&
               baseType.SpecialType is not (SpecialType.System_Object or SpecialType.System_ValueType);
    }

    private static bool HasDeclaredState(TypeDeclarationSyntax declaration)
    {
        if (declaration is RecordDeclarationSyntax { ParameterList.Parameters.Count: > 0 })
            return true;

        return declaration.Members.Any(member =>
            member is PropertyDeclarationSyntax or FieldDeclarationSyntax);
    }

    private static bool IsPassiveMember(MemberDeclarationSyntax member) => member switch
    {
        PropertyDeclarationSyntax property => IsAutoProperty(property),
        FieldDeclarationSyntax field => IsPassiveField(field),
        ConstructorDeclarationSyntax constructor => IsAssignmentOnlyConstructor(constructor),
        _ => false
    };

    private static bool IsAutoProperty(PropertyDeclarationSyntax property)
    {
        if (property.Modifiers.Any(modifier => modifier.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.StaticKeyword)) ||
            property.ExpressionBody != null ||
            property.AccessorList == null ||
            property.AccessorList.Accessors.Any(accessor => accessor.Body != null || accessor.ExpressionBody != null))
        {
            return false;
        }

        return property.Initializer == null || IsPassiveValue(property.Initializer.Value);
    }

    private static bool IsPassiveField(FieldDeclarationSyntax field)
    {
        if (field.Modifiers.Any(modifier =>
                modifier.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.StaticKeyword) ||
                modifier.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.ConstKeyword)))
        {
            return false;
        }

        return field.Declaration.Variables.All(variable =>
            variable.Initializer == null || IsPassiveValue(variable.Initializer.Value));
    }

    private static bool IsAssignmentOnlyConstructor(ConstructorDeclarationSyntax constructor)
    {
        if (constructor.ExpressionBody != null)
            return constructor.ExpressionBody.Expression is AssignmentExpressionSyntax assignment &&
                   IsPassiveValue(assignment.Right);

        return constructor.Body != null && constructor.Body.Statements.All(statement =>
            statement is ExpressionStatementSyntax
            {
                Expression: AssignmentExpressionSyntax assignment
            } && IsPassiveValue(assignment.Right));
    }

    private static bool IsPassiveValue(ExpressionSyntax expression) => expression switch
    {
        IdentifierNameSyntax => true,
        LiteralExpressionSyntax => true,
        DefaultExpressionSyntax => true,
        MemberAccessExpressionSyntax memberAccess => IsPassiveValue(memberAccess.Expression),
        ParenthesizedExpressionSyntax parenthesized => IsPassiveValue(parenthesized.Expression),
        CastExpressionSyntax cast => IsPassiveValue(cast.Expression),
        PostfixUnaryExpressionSyntax postfix
            when postfix.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.SuppressNullableWarningExpression) =>
            IsPassiveValue(postfix.Operand),
        CollectionExpressionSyntax collection => collection.Elements.All(element =>
            element is ExpressionElementSyntax expressionElement && IsPassiveValue(expressionElement.Expression)),
        ObjectCreationExpressionSyntax creation =>
            creation.ArgumentList?.Arguments.Count == 0 && creation.Initializer == null,
        ImplicitObjectCreationExpressionSyntax creation =>
            creation.ArgumentList.Arguments.Count == 0 && creation.Initializer == null,
        InvocationExpressionSyntax invocation => IsPassiveFactoryInvocation(invocation),
        _ => false
    };

    private static bool IsPassiveFactoryInvocation(InvocationExpressionSyntax invocation)
    {
        if (invocation.ArgumentList.Arguments.Count != 0 ||
            invocation.Expression is not MemberAccessExpressionSyntax
            {
                Name.Identifier.Text: "Empty"
            } memberAccess)
        {
            return false;
        }

        return memberAccess.Expression.ToString() is "Array" or "System.Array";
    }
}
