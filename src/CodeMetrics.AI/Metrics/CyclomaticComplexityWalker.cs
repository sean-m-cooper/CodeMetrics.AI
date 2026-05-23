// src/CodeMetrics.AI/Metrics/CyclomaticComplexityWalker.cs
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeMetrics.AI.Metrics;

public sealed class CyclomaticComplexityWalker : CSharpSyntaxWalker
{
    public int Complexity { get; private set; } = 1;

    public override void VisitIfStatement(IfStatementSyntax node) { Complexity++; base.VisitIfStatement(node); }
    public override void VisitWhileStatement(WhileStatementSyntax node) { Complexity++; base.VisitWhileStatement(node); }
    public override void VisitDoStatement(DoStatementSyntax node) { Complexity++; base.VisitDoStatement(node); }
    public override void VisitForStatement(ForStatementSyntax node) { Complexity++; base.VisitForStatement(node); }
    public override void VisitForEachStatement(ForEachStatementSyntax node) { Complexity++; base.VisitForEachStatement(node); }
    public override void VisitForEachVariableStatement(ForEachVariableStatementSyntax node) { Complexity++; base.VisitForEachVariableStatement(node); }

    public override void VisitSwitchSection(SwitchSectionSyntax node)
    {
        Complexity += node.Labels.Count;
        base.VisitSwitchSection(node);
    }

    public override void VisitSwitchExpressionArm(SwitchExpressionArmSyntax node)
    {
        if (node.Pattern is not DiscardPatternSyntax)
            Complexity++;
        base.VisitSwitchExpressionArm(node);
    }

    public override void VisitCatchClause(CatchClauseSyntax node) { Complexity++; base.VisitCatchClause(node); }
    public override void VisitConditionalExpression(ConditionalExpressionSyntax node) { Complexity++; base.VisitConditionalExpression(node); }

    public override void VisitBinaryExpression(BinaryExpressionSyntax node)
    {
        if (node.IsKind(SyntaxKind.LogicalAndExpression) ||
            node.IsKind(SyntaxKind.LogicalOrExpression) ||
            node.IsKind(SyntaxKind.CoalesceExpression))
        {
            Complexity++;
        }
        base.VisitBinaryExpression(node);
    }

    public override void VisitAssignmentExpression(AssignmentExpressionSyntax node)
    {
        if (node.IsKind(SyntaxKind.CoalesceAssignmentExpression))
            Complexity++;
        base.VisitAssignmentExpression(node);
    }
}
