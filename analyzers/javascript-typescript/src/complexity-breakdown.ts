import ts from "typescript";

export interface DecisionCounts {
  ifStatements: number; switchCases: number; loops: number; catchClauses: number;
  ternaryExpressions: number; logicalAnd: number; logicalOr: number; nullishCoalescing: number;
}
export interface ComplexityBreakdown {
  version: 1; baseline: 1; counts: DecisionCounts; decisionIncrements: number; total: number;
}
export function emptyDecisionCounts(): DecisionCounts {
  return { ifStatements: 0, switchCases: 0, loops: 0, catchClauses: 0,
    ternaryExpressions: 0, logicalAnd: 0, logicalOr: 0, nullishCoalescing: 0 };
}
export function decisionCategory(node: ts.Node): keyof DecisionCounts | undefined {
  switch (node.kind) {
    case ts.SyntaxKind.IfStatement: return "ifStatements";
    case ts.SyntaxKind.CaseClause: return "switchCases";
    case ts.SyntaxKind.ForStatement:
    case ts.SyntaxKind.ForInStatement:
    case ts.SyntaxKind.ForOfStatement:
    case ts.SyntaxKind.WhileStatement:
    case ts.SyntaxKind.DoStatement: return "loops";
    case ts.SyntaxKind.CatchClause: return "catchClauses";
    case ts.SyntaxKind.ConditionalExpression: return "ternaryExpressions";
  }
  if (!ts.isBinaryExpression(node)) return undefined;
  switch (node.operatorToken.kind) {
    case ts.SyntaxKind.AmpersandAmpersandToken: return "logicalAnd";
    case ts.SyntaxKind.BarBarToken: return "logicalOr";
    case ts.SyntaxKind.QuestionQuestionToken: return "nullishCoalescing";
    default: return undefined;
  }
}
export function complexityBreakdown(counts: DecisionCounts): ComplexityBreakdown {
  const decisionIncrements = Object.values(counts).reduce((sum, count) => sum + count, 0);
  return { version: 1, baseline: 1, counts, decisionIncrements, total: 1 + decisionIncrements };
}
