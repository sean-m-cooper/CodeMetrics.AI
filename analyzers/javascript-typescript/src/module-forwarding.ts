import ts from "typescript";
import type { ModuleReference } from "./module-references.js";

export type DependencyRole = "implementation" | "reExport";
interface BindingUse { forwarded: boolean; local: boolean; mutated: boolean; }

function unparenthesized(node: ts.Expression): ts.Expression {
  while (ts.isParenthesizedExpression(node)) node = node.expression;
  return node;
}
function staticBase(node: ts.Expression): ts.Expression {
  node = unparenthesized(node);
  while ((ts.isPropertyAccessExpression(node) && !node.questionDotToken) ||
    (ts.isElementAccessExpression(node) && !node.questionDotToken && ts.isStringLiteralLike(node.argumentExpression)))
    node = unparenthesized(node.expression);
  return node;
}
function member(node: ts.Expression): { owner: ts.Expression; name: string } | undefined {
  node = unparenthesized(node);
  if (ts.isPropertyAccessExpression(node) && !node.questionDotToken) return { owner: node.expression, name: node.name.text };
  if (ts.isElementAccessExpression(node) && !node.questionDotToken && ts.isStringLiteralLike(node.argumentExpression))
    return { owner: node.expression, name: node.argumentExpression.text };
  return undefined;
}
function nodeName(node: ts.Expression, name: string, checker: ts.TypeChecker) {
  node = unparenthesized(node);
  if (!ts.isIdentifier(node) || node.text !== name) return false;
  // In JS, getSymbolAtLocation can return the synthetic exports symbol even
  // for a shadowed parameter. Ask for the lexical value/alias symbol instead.
  const declarations = checker.getSymbolsInScope(node, ts.SymbolFlags.Value | ts.SymbolFlags.Alias)
    .find(symbol => symbol.name === name)?.declarations;
  return !declarations?.length || declarations.every(declaration =>
    (ts.isIdentifier(declaration) && ts.isPropertyAccessExpression(declaration.parent) && declaration.parent.expression === declaration) ||
    (declaration.getSourceFile().isDeclarationFile && declaration.getSourceFile().fileName.replaceAll("\\", "/").includes("/node_modules/@types/node/")));
}
function moduleExports(node: ts.Expression, checker: ts.TypeChecker): boolean {
  const property = member(node);
  return !!property && property.name === "exports" && nodeName(property.owner, "module", checker);
}
function exportTarget(node: ts.Expression, checker: ts.TypeChecker): boolean {
  if (moduleExports(node, checker)) return true;
  const property = member(node);
  return !!property && (nodeName(property.owner, "exports", checker) || moduleExports(property.owner, checker));
}
function bindingNames(reference: ModuleReference): ts.Identifier[] {
  const node = reference.node;
  if (ts.isImportDeclaration(node)) {
    const clause = node.importClause, bindings = clause?.namedBindings;
    return [...(clause?.name ? [clause.name] : []), ...(bindings ? ts.isNamespaceImport(bindings) ? [bindings.name] :
      bindings.elements.filter(element => !element.isTypeOnly).map(element => element.name) : [])];
  }
  if (ts.isImportEqualsDeclaration(node)) return [node.name];
  if (reference.form === "require") {
    let expression = node;
    while (ts.isParenthesizedExpression(expression.parent)) expression = expression.parent;
    const parent = expression.parent;
    if (ts.isVariableDeclaration(parent) && parent.initializer === expression && ts.isIdentifier(parent.name)) return [parent.name];
  }
  return [];
}

function assignmentRoot(node: ts.Node): ts.Node {
  const parent = node.parent;
  if (ts.isParenthesizedExpression(parent) || ts.isArrayLiteralExpression(parent) || ts.isObjectLiteralExpression(parent) ||
    ts.isShorthandPropertyAssignment(parent) || ts.isSpreadElement(parent) || ts.isSpreadAssignment(parent) ||
    (ts.isPropertyAssignment(parent) && parent.initializer === node)) return assignmentRoot(parent);
  return node;
}

// One source walk tracks explicit forwarding and other uses of the same local
// binding. This is symbol-based syntax evidence, not alias/dataflow analysis.
export function dependencyRoles(source: ts.SourceFile, checker: ts.TypeChecker, references: ModuleReference[]) {
  const bindings = new Map<ts.Symbol, BindingUse>();
  const declared = new Set<ts.Node>(), forwardedNodes = new Set<ts.Node>();
  const perReference = new Map<ModuleReference, BindingUse[]>();
  const directCalls = new Set(references.filter(reference => reference.form === "require").map(reference => reference.node));
  for (const reference of references) {
    const uses: BindingUse[] = [];
    if (reference.usage !== "typeOnly") for (const name of bindingNames(reference)) {
      const symbol = checker.getSymbolAtLocation(name);
      if (!symbol) continue;
      const use = { forwarded: false, local: false, mutated: false };
      bindings.set(symbol, use); uses.push(use); declared.add(name);
    }
    perReference.set(reference, uses);
  }
  function forward(expression: ts.Expression, symbol?: ts.Symbol) {
    const base = staticBase(expression);
    const use = bindings.get(symbol ?? (ts.isIdentifier(base) ? checker.getSymbolAtLocation(base) : undefined)!);
    if (use) { use.forwarded = true; forwardedNodes.add(base); }
    else if (directCalls.has(base)) forwardedNodes.add(base);
  }
  function local(node: ts.Identifier, symbol = checker.getSymbolAtLocation(node)) {
    const use = symbol && bindings.get(symbol);
    if (!use || declared.has(node) || forwardedNodes.has(node)) return;
    use.local = true;
    const expression = assignmentRoot(node);
    const parent = expression.parent;
    if ((ts.isBinaryExpression(parent) && parent.left === expression &&
      parent.operatorToken.kind >= ts.SyntaxKind.FirstAssignment && parent.operatorToken.kind <= ts.SyntaxKind.LastAssignment) ||
      (ts.isVariableDeclaration(parent) && parent.name === expression && !!parent.initializer) ||
      (ts.isBindingElement(parent) && parent.name === expression) ||
      ((ts.isForOfStatement(parent) || ts.isForInStatement(parent)) && parent.initializer === expression) ||
      ((ts.isPrefixUnaryExpression(parent) || ts.isPostfixUnaryExpression(parent)) &&
        (parent.operator === ts.SyntaxKind.PlusPlusToken || parent.operator === ts.SyntaxKind.MinusMinusToken))) use.mutated = true;
  }
  function visit(node: ts.Node) {
    if (ts.isImportDeclaration(node) || ts.isImportEqualsDeclaration(node)) return;
    if (ts.isExportDeclaration(node)) {
      if (!node.moduleSpecifier && !node.isTypeOnly && node.exportClause && ts.isNamedExports(node.exportClause))
        for (const element of node.exportClause.elements) if (!element.isTypeOnly)
          forward(element.propertyName ?? element.name, checker.getExportSpecifierLocalTargetSymbol(element));
      return;
    }
    if (ts.isExportAssignment(node)) forward(node.expression);
    if (ts.isBinaryExpression(node) && node.operatorToken.kind === ts.SyntaxKind.EqualsToken && exportTarget(node.left, checker)) {
      forward(node.right); visit(node.right); return;
    }
    if (ts.isShorthandPropertyAssignment(node)) local(node.name, checker.getShorthandAssignmentValueSymbol(node));
    else if (ts.isIdentifier(node)) local(node);
    ts.forEachChild(node, visit);
  }
  visit(source);
  return new Map(references.map(reference => {
    const uses = perReference.get(reference)!;
    const roles: DependencyRole[] = [];
    if (reference.usage !== "typeOnly") {
      const reExport = reference.form === "reExport" || forwardedNodes.has(reference.node) || uses.some(use => use.forwarded && !use.mutated);
      if (!reExport || uses.some(use => use.local || !use.forwarded || use.mutated)) roles.push("implementation");
      if (reExport) roles.push("reExport");
    }
    return [reference.node, roles];
  }));
}
