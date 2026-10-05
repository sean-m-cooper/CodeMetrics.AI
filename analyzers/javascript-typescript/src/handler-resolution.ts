import ts from "typescript";
import path from "node:path";
import { hash } from "./evidence.js";
import { isFunction } from "./function-nodes.js";

export function unwrapHandler(node: ts.Expression): ts.Expression {
  while (ts.isParenthesizedExpression(node) || ts.isAsExpression(node) || ts.isTypeAssertionExpression(node) ||
    ts.isNonNullExpression(node) || ts.isSatisfiesExpression(node)) node = node.expression;
  return node;
}
type Resolution = { body: ts.ConciseBody; resolution: "inline" | "reference" } | { reason: string };

/** Bounded declaration lookup, not a runtime value-flow or mutation proof. */
export class HandlerResolver {
  private readonly selected = new Set<ts.SourceFile>();
  private readonly written = new Set<ts.Symbol>();
  private readonly identities = new Map<ts.Node, { id: string; file: string; project: string; line: number }>();
  constructor(private readonly checker: ts.TypeChecker, sources: ts.SourceFile[], root: string, project: string) {
    for (const source of sources) {
      this.selected.add(source);
      const file = path.relative(root, source.fileName).replaceAll("\\", "/");
      let functions = 0, catches = 0;
      const visit = (node: ts.Node) => {
        if (ts.isTypeNode(node) && !ts.isExpressionWithTypeArguments(node)) return;
        const body = isFunction(node) ? node.body : ts.isCatchClause(node) ? node.block : undefined;
        if (body) {
          const key = ts.isCatchClause(node) ? `catch:${catches++}` : `function:${functions++}`;
          this.identities.set(body, { id: hash(`${project}|${file}|${key}`), file, project,
            line: source.getLineAndCharacterOfPosition(body.getStart(source)).line + 1 });
        }
        if (ts.isBinaryExpression(node) && node.operatorToken.kind >= ts.SyntaxKind.FirstAssignment &&
          node.operatorToken.kind <= ts.SyntaxKind.LastAssignment) this.recordWrite(node.left);
        if ((ts.isPrefixUnaryExpression(node) || ts.isPostfixUnaryExpression(node)) &&
          [ts.SyntaxKind.PlusPlusToken, ts.SyntaxKind.MinusMinusToken].includes(node.operator)) this.recordWrite(node.operand);
        if ((ts.isForInStatement(node) || ts.isForOfStatement(node)) && !ts.isVariableDeclarationList(node.initializer)) this.recordWrite(node.initializer);
        ts.forEachChild(node, visit);
      };
      visit(source);
    }
  }
  private recordWrite(node: ts.Node) {
    if (ts.isIdentifier(node)) {
      const symbol = this.checker.getSymbolAtLocation(node); if (symbol) this.written.add(symbol);
    } else if (!ts.isPropertyAccessExpression(node) && !ts.isElementAccessExpression(node)) {
      // Destructuring and parenthesized assignments are conservatively included.
      ts.forEachChild(node, child => this.recordWrite(child));
    }
  }
  identity(body: ts.ConciseBody) { return this.identities.get(body)!; }
  resolve(argument: ts.Expression | undefined): Resolution {
    if (!argument) return { reason: "missingArgument" };
    const direct = unwrapHandler(argument);
    if (isFunction(direct) && direct.body) return { body: direct.body, resolution: "inline" };
    return this.resolveReference(direct, new Set());
  }
  private resolveReference(node: ts.Expression, seen: Set<ts.Symbol>): Resolution {
    node = unwrapHandler(node);
    if (!ts.isIdentifier(node)) return { reason: "unsupportedExpression" };
    let symbol = this.checker.getSymbolAtLocation(node);
    if (!symbol) return { reason: "unresolvedSymbol" };
    if (this.written.has(symbol)) return { reason: "reassignedBinding" };
    if (symbol.flags & ts.SymbolFlags.Alias) symbol = this.checker.getAliasedSymbol(symbol);
    if (this.written.has(symbol)) return { reason: "reassignedBinding" };
    if (seen.has(symbol) || seen.size >= 32) return { reason: "aliasCycleOrDepth" };
    seen.add(symbol);
    const declarations = symbol.declarations ?? [];
    if (!declarations.length || declarations.some(declaration => !this.selected.has(declaration.getSourceFile())))
      return { reason: "outsideSelectedSource" };
    const implementations = declarations.filter(declaration => ts.isFunctionDeclaration(declaration) && declaration.body);
    if (implementations.length === 1) return { body: (implementations[0] as ts.FunctionDeclaration).body!, resolution: "reference" };
    if (declarations.length !== 1 || !ts.isVariableDeclaration(declarations[0])) return { reason: "unsupportedDeclaration" };
    const declaration = declarations[0];
    if (!ts.isVariableDeclarationList(declaration.parent) || !(declaration.parent.flags & ts.NodeFlags.Const))
      return { reason: "mutableBinding" };
    if (!declaration.initializer) return { reason: "missingInitializer" };
    const value = unwrapHandler(declaration.initializer);
    if (isFunction(value) && value.body) return { body: value.body, resolution: "reference" };
    return this.resolveReference(value, seen);
  }
}
