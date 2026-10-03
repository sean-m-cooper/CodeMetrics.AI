import ts from "typescript";
import { hash } from "./evidence.js";
import { isFunction, type FunctionNode } from "./function-nodes.js";
import { complexityBreakdown, decisionCategory, emptyDecisionCounts, type ComplexityBreakdown } from "./complexity-breakdown.js";

export interface FunctionMeasurement {
  id: string; file: string; project: string; member: string; line: number; sourceSpanStart: number;
  ownComplexity: number; ownSourceLines: number; ownHalsteadVolume: number; ownMaintainabilityIndex: number;
  complexityBreakdown?: ComplexityBreakdown;
}

function excluded(node: ts.Node): boolean {
  return isFunction(node) || ts.isTypeNode(node) || ts.isInterfaceDeclaration(node) ||
    ts.isTypeAliasDeclaration(node) || ts.isClassDeclaration(node) || ts.isClassExpression(node) || ts.isEnumDeclaration(node);
}

function assertedExpression(node: ts.Node): ts.Expression | undefined {
  if (ts.isAsExpression(node) || ts.isTypeAssertionExpression(node) || ts.isSatisfiesExpression(node) ||
      ts.isNonNullExpression(node) || ts.isExpressionWithTypeArguments(node)) return node.expression;
  return undefined;
}

function runtimeChildren(node: ts.Node, source: ts.SourceFile): ts.Node[] {
  const children = node.getChildren(source);
  const typed = node as ts.Node & { type?: ts.TypeNode; typeArguments?: ts.NodeArray<ts.TypeNode>; exclamationToken?: ts.Node };
  const argumentsIndex = typed.typeArguments ? children.findIndex(child => child.kind === ts.SyntaxKind.SyntaxList &&
    child.pos === typed.typeArguments!.pos && child.end === typed.typeArguments!.end) : -1;
  return children.filter((child, index) => {
    // Delimiters and commas around erased type arguments are not runtime tokens.
    if (argumentsIndex >= 0 && index >= argumentsIndex - 1 && index <= argumentsIndex + 1) return false;
    if (typed.type && (child === typed.type || (child.kind === ts.SyntaxKind.ColonToken && children[index + 1] === typed.type))) return false;
    return child !== typed.exclamationToken;
  });
}

// This scored measurement is deliberately separate from the historical CSV metric.
export function measureFunction(node: FunctionNode, source: ts.SourceFile, file: string, project: string, member: string): FunctionMeasurement {
  const body = node.body!;
  const decisionCounts = emptyDecisionCounts();
  const operators: string[] = [], operands: string[] = [], lines = new Set<number>();
  function decisions(current: ts.Node) {
    const expression = assertedExpression(current);
    if (expression) { decisions(expression); return; }
    if (excluded(current)) return;
    const category = decisionCategory(current);
    if (category) decisionCounts[category]++;
    ts.forEachChild(current, decisions);
  }
  function tokens(current: ts.Node) {
    const expression = assertedExpression(current);
    if (expression) { tokens(expression); return; }
    if (excluded(current) || ts.isJsxText(current)) return;
    const children = runtimeChildren(current, source);
    if (children.length) { children.forEach(tokens); return; }
    if (current.kind === ts.SyntaxKind.EndOfFileToken || current.getWidth(source) === 0) return;
    const text = current.getText(source);
    if (!["{", "}", ";"].includes(text)) {
      const start = source.getLineAndCharacterOfPosition(current.getStart(source)).line;
      const end = source.getLineAndCharacterOfPosition(current.end - 1).line;
      for (let line = start; line <= end; line++) lines.add(line);
    }
    if (ts.isIdentifier(current) || ts.isLiteralExpression(current)) operands.push(text);
    else operators.push(text);
  }
  decisions(body);
  const breakdown = complexityBreakdown(decisionCounts);
  const complexity = breakdown.total;
  tokens(body);
  const vocabulary = new Set(operators).size + new Set(operands).size;
  const volume = vocabulary > 1 ? (operators.length + operands.length) * Math.log2(vocabulary) : 0;
  const mi = lines.size === 0 ? 100 : Math.min(100, Math.max(0,
    (171 - 5.2 * Math.log(Math.max(volume, 1)) - 0.23 * complexity - 16.2 * Math.log(Math.max(lines.size, 1))) * 100 / 171));
  const start = node.getStart(source);
  return { id: hash(`${file}|${start}|${node.kind}`), file, project, member, sourceSpanStart: start,
    line: source.getLineAndCharacterOfPosition(start).line + 1, ownComplexity: complexity,
    ownSourceLines: lines.size, ownHalsteadVolume: volume, ownMaintainabilityIndex: mi, complexityBreakdown: breakdown };
}
