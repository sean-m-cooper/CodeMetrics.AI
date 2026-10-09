import ts from "typescript";
import path from "node:path";
import { exclusion } from "./discovery.js";
import { unwrapHandler } from "./handler-resolution.js";

export function standardConsole(call: ts.CallExpression, checker: ts.TypeChecker): boolean {
  const member = call.expression;
  if (!ts.isPropertyAccessExpression(member) || !["error", "warn", "info", "log", "debug", "trace"].includes(member.name.text) ||
    !ts.isIdentifier(member.expression) || member.expression.text !== "console" || !call.arguments.length || call.questionDotToken || member.questionDotToken) return false;
  const declarations = checker.getSymbolAtLocation(member.expression)?.declarations ?? [];
  return declarations.length > 0 && declarations.every(declaration => declaration.getSourceFile().isDeclarationFile &&
    /(?:lib\.dom\.d\.ts|[/\\]@types[/\\]node[/\\](?:web-globals[/\\])?(?:console|globals)\.d\.ts)$/.test(declaration.getSourceFile().fileName));
}
const descendants = (node: ts.Node, match: (node: ts.Node) => boolean): boolean => match(node) || (ts.forEachChild(node, child => descendants(child, match)) ?? false);
function enclosingFunction(node: ts.Node): ts.SignatureDeclaration | undefined {
  for (let at = node.parent; at; at = at.parent) if (ts.isFunctionLike(at)) return at;
}
function documentation(node: ts.Node): string {
  return (ts.getLeadingCommentRanges(node.getSourceFile().text, node.getFullStart()) ?? [])
    .map(range => node.getSourceFile().text.slice(range.pos, range.end)).join("\n");
}

/** Source-backed contracts, not name-based exemptions or a runtime data-flow proof. */
export class FailureContracts {
  private readonly references = new Map<ts.Symbol, ts.Identifier[]>();
  private readonly writes = new Set<ts.Symbol>();
  private readonly local = new Set<ts.SourceFile>();
  constructor(private readonly checker: ts.TypeChecker, sources: readonly ts.SourceFile[], private readonly selected: readonly ts.SourceFile[], root: string) {
    for (const source of sources) {
      const relative = path.relative(root, source.fileName).replaceAll("\\", "/");
      if (source.isDeclarationFile || relative.startsWith("../") || exclusion(relative)) continue;
      this.local.add(source);
      const visit = (node: ts.Node) => {
        if (ts.isIdentifier(node)) {
          const symbol = ts.isShorthandPropertyAssignment(node.parent)
            ? checker.getShorthandAssignmentValueSymbol(node.parent) : checker.getSymbolAtLocation(node);
          if (symbol) { const refs = this.references.get(symbol) ?? []; refs.push(node); this.references.set(symbol, refs); }
        }
        if (ts.isBinaryExpression(node) && node.operatorToken.kind >= ts.SyntaxKind.FirstAssignment && node.operatorToken.kind <= ts.SyntaxKind.LastAssignment) {
          const record = (target: ts.Node) => {
            if (ts.isIdentifier(target) || ts.isPropertyAccessExpression(target) || ts.isElementAccessExpression(target)) {
              const symbol = checker.getSymbolAtLocation(target); if (symbol) this.writes.add(symbol);
            } else ts.forEachChild(target, record);
          };
          record(node.left);
        }
        if ((ts.isPrefixUnaryExpression(node) || ts.isPostfixUnaryExpression(node)) &&
          [ts.SyntaxKind.PlusPlusToken,ts.SyntaxKind.MinusMinusToken].includes(node.operator)) {
          const symbol=checker.getSymbolAtLocation(node.operand); if(symbol) this.writes.add(symbol);
        }
        ts.forEachChild(node, visit);
      };
      visit(source);
    }
  }
  forwardsCaughtError(call: ts.CallExpression): boolean {
    if (call.questionDotToken || !ts.isIdentifier(call.expression) || !call.arguments.length) return false;
    const target = this.checker.getSymbolAtLocation(call.expression), error = unwrapHandler(call.arguments[0]);
    const declaration = target?.declarations?.[0];
    if (!target || this.writes.has(target) || !declaration || !ts.isParameter(declaration) || declaration.initializer ||
      declaration.getSourceFile() !== call.getSourceFile() || call.pos < declaration.parent.pos || call.end > declaration.parent.end) return false;
    const caughtSymbol = (symbol: ts.Symbol | undefined): boolean => {
      const binding = symbol?.declarations?.[0];
      return !!symbol && !!binding && ts.isVariableDeclaration(binding) && ts.isCatchClause(binding.parent) &&
        !this.writes.has(symbol) && call.pos >= binding.parent.block.pos && call.end <= binding.parent.block.end;
    };
    // Only plain payload expressions, never an unrelated call taking the error.
    const payload = (node: ts.Node): boolean => {
      if (ts.isIdentifier(node) && caughtSymbol(this.checker.getSymbolAtLocation(node))) return true;
      if (ts.isPropertyAccessExpression(node)) return payload(node.expression);
      if (ts.isObjectLiteralExpression(node)) return node.properties.some(property =>
        ts.isPropertyAssignment(property) ? payload(property.initializer) : ts.isShorthandPropertyAssignment(property) &&
          caughtSymbol(this.checker.getShorthandAssignmentValueSymbol(property)));
      return false;
    };
    return payload(error);
  }
  explicitResult(expression: ts.Expression | undefined, depth = 0): boolean {
    if (!expression || depth > 4) return false;
    expression = unwrapHandler(expression);
    if (ts.isObjectLiteralExpression(expression) || ts.isArrayLiteralExpression(expression) || ts.isNewExpression(expression)) return true;
    if (!ts.isIdentifier(expression)) return false;
    const symbol = this.checker.getSymbolAtLocation(expression), declarations = symbol?.declarations ?? [];
    if (!symbol || this.writes.has(symbol) || declarations.length !== 1 || !ts.isVariableDeclaration(declarations[0])) return false;
    const declaration = declarations[0];
    if (!ts.isVariableDeclarationList(declaration.parent) || !(declaration.parent.flags & ts.NodeFlags.Const) || !declaration.initializer) return false;
    const value=unwrapHandler(declaration.initializer);
    return ts.isLiteralExpression(value) || [ts.SyntaxKind.NullKeyword,ts.SyntaxKind.TrueKeyword,ts.SyntaxKind.FalseKeyword].includes(value.kind) || this.explicitResult(value, depth + 1);
  }
  reports(call: ts.CallExpression): boolean {
    return this.reachesConsole(call, new Set(), 0);
  }
  private reachesConsole(call: ts.CallExpression, seen: Set<ts.Node>, depth: number,
    payloadPositions = new Set(call.arguments.map((_, index) => index))): boolean {
    if (call.questionDotToken || (ts.isPropertyAccessExpression(call.expression) && call.expression.questionDotToken)) return false;
    if (standardConsole(call, this.checker)) return true;
    const symbol = this.checker.getSymbolAtLocation(call.expression);
    if (symbol && this.writes.has(symbol)) return false;
    if (ts.isPropertyAccessExpression(call.expression) && ts.isIdentifier(call.expression.expression)) {
      const receiver = this.checker.getSymbolAtLocation(call.expression.expression);
      if (receiver && this.writes.has(receiver)) return false;
    }
    const declaration = this.checker.getResolvedSignature(call)?.declaration;
    if (!declaration || depth >= 3 || seen.has(declaration) || !this.local.has(declaration.getSourceFile()) ||
      !(ts.isFunctionDeclaration(declaration) || ts.isMethodDeclaration(declaration) || ts.isFunctionExpression(declaration) || ts.isArrowFunction(declaration)) || !declaration.body) return false;
    seen = new Set(seen).add(declaration);
    const parameters = new Set(declaration.parameters.filter((parameter,index)=>
      payloadPositions.has(index) || parameter.dotDotDotToken && [...payloadPositions].some(position=>position>=index))
      .map(parameter => this.checker.getSymbolAtLocation(parameter.name)).filter(symbol=>symbol && !this.writes.has(symbol)));
    const declaredLogger = /\b(?:log|write)\b[^\n]*\b(?:message|exception|error|warn)\b/i.test(documentation(declaration));
    // Ordinary wrappers require an unconditional forwarding call. A documented
    // logger may have runtime level filters; prove its payload reaches a console sink.
    const candidates: ts.CallExpression[] = [];
    const visit = (node: ts.Node) => {
      if (node !== declaration.body && ts.isFunctionLike(node)) return;
      if (ts.isCallExpression(node)) candidates.push(node);
      ts.forEachChild(node, visit);
    };
    if (declaredLogger) visit(declaration.body);
    else if (ts.isBlock(declaration.body)) {
      for (const statement of declaration.body.statements) {
        if (ts.isExpressionStatement(statement) && ts.isCallExpression(statement.expression)) candidates.push(statement.expression);
        else if (ts.isReturnStatement(statement) && statement.expression && ts.isCallExpression(statement.expression)) { candidates.push(statement.expression); break; }
        else if (!ts.isVariableStatement(statement) && !ts.isEmptyStatement(statement)) break;
      }
    } else if (ts.isCallExpression(declaration.body)) candidates.push(declaration.body);
    return candidates.some(candidate => {
      const forwarded = new Set(candidate.arguments.flatMap((arg,index)=>descendants(arg,
        node => ts.isIdentifier(node) && parameters.has(this.checker.getSymbolAtLocation(node))) ? [index] : []));
      return forwarded.size > 0 && this.reachesConsole(candidate, seen, depth + 1, forwarded);
    });
  }
  guardedUndefinedResult(body: ts.ConciseBody): boolean {
    const fn = enclosingFunction(body);
    if (!fn || !ts.isFunctionDeclaration(fn) || !fn.name || ts.getCombinedModifierFlags(fn) & ts.ModifierFlags.Export) return false;
    const symbol = this.checker.getSymbolAtLocation(fn.name);
    if (!symbol || this.writes.has(symbol)) return false;
    const module = this.checker.getSymbolAtLocation(fn.getSourceFile());
    if (!module && !this.commonJsFile(fn.getSourceFile()) && !enclosingFunction(fn)) return false;
    if (module && this.checker.getExportsOfModule(module).some(exported =>
      (exported.flags & ts.SymbolFlags.Alias ? this.checker.getAliasedSymbol(exported) : exported) === symbol)) return false;
    const uses = (this.references.get(symbol) ?? []).filter(node => node !== fn.name);
    return uses.length > 0 && uses.every(node => this.selected.includes(node.getSourceFile()) &&
      ts.isCallExpression(node.parent) && node.parent.expression === node && this.guardedCallResult(node.parent));
  }
  private commonJsFile(source: ts.SourceFile): boolean {
    const exportRoot = (node: ts.Expression): ts.Identifier | undefined => {
      if (!ts.isPropertyAccessExpression(node)) return;
      if (ts.isIdentifier(node.expression)) {
        const base=node.expression;
        return base.text==="exports" || base.text==="module" && node.name.text==="exports" ? base : undefined;
      }
      return exportRoot(node.expression);
    };
    const inferredExport = (node: ts.Node): boolean => {
      // Depending on the assignments present, the JS binder uses either the
      // assignment or its root identifier as the inferred module declaration.
      while (ts.isPropertyAccessExpression(node.parent) && node.parent.expression===node) node=node.parent;
      if (ts.isBinaryExpression(node.parent) && node.parent.left===node) node=node.parent;
      return ts.isBinaryExpression(node) && node.operatorToken.kind===ts.SyntaxKind.EqualsToken && !!exportRoot(node.left);
    };
    return source.statements.some(statement => {
      if (!ts.isExpressionStatement(statement) || !ts.isBinaryExpression(statement.expression) ||
        statement.expression.operatorToken.kind !== ts.SyntaxKind.EqualsToken) return false;
      const base=exportRoot(statement.expression.left);
      if (!base) return false;
      const declarations=this.checker.getSymbolAtLocation(base)?.declarations ?? [];
      // TypeScript binds bare CommonJS `module` to its export assignment when
      // Node declarations are absent; that inferred declaration is not shadowing.
      return declarations.every(declaration=>inferredExport(declaration) ||
        declaration.getSourceFile().isDeclarationFile && /[/\\]@types[/\\]node[/\\]/.test(declaration.getSourceFile().fileName));
    });
  }
  private guardedCallResult(call: ts.CallExpression): boolean {
    const parent = call.parent;
    let identifier: ts.Identifier, statement: ts.Node;
    if (ts.isVariableDeclaration(parent) && ts.isIdentifier(parent.name)) { identifier=parent.name;statement=parent.parent.parent; }
    else if (ts.isBinaryExpression(parent) && parent.operatorToken.kind === ts.SyntaxKind.EqualsToken && ts.isIdentifier(parent.left)) { identifier=parent.left;statement=parent.parent; }
    else return false;
    if (!ts.isBlock(statement.parent)) return false;
    const next=statement.parent.statements[statement.parent.statements.indexOf(statement as ts.Statement)+1];
    const symbol=this.checker.getSymbolAtLocation(identifier);
    if (!symbol || !next || !ts.isIfStatement(next) || !this.truthyGuard(next.expression,symbol)) return false;
    return (this.references.get(symbol) ?? []).every(node => {
      if (ts.isVariableDeclaration(node.parent) && node.parent.name===node) return true;
      if (ts.isBinaryExpression(node.parent) && node.parent.left===node && node.parent.operatorToken.kind===ts.SyntaxKind.EqualsToken)
        return ts.isCallExpression(node.parent.right) && this.checker.getSymbolAtLocation(node.parent.right.expression)===this.checker.getSymbolAtLocation(call.expression) &&
          node.parent.parent.parent===statement.parent;
      if (ts.isPrefixUnaryExpression(node.parent) || ts.isPostfixUnaryExpression(node.parent)) return false;
      for(let at:ts.Node=node;at.parent && !ts.isFunctionLike(at.parent);at=at.parent) {
        const parent=at.parent;
        if(ts.isBinaryExpression(parent) && parent.operatorToken.kind===ts.SyntaxKind.AmpersandAmpersandToken && this.truthyGuard(parent.left,symbol)) return true;
        if(ts.isIfStatement(parent) && (parent.expression===at || parent.thenStatement===at) && this.truthyGuard(parent.expression,symbol)) return true;
      }
      return false;
    });
  }
  private truthyGuard(expression:ts.Expression,symbol:ts.Symbol):boolean {
    expression=unwrapHandler(expression);
    return ts.isIdentifier(expression) ? this.checker.getSymbolAtLocation(expression)===symbol :
      ts.isBinaryExpression(expression) && expression.operatorToken.kind===ts.SyntaxKind.AmpersandAmpersandToken && this.truthyGuard(expression.left,symbol);
  }
}
