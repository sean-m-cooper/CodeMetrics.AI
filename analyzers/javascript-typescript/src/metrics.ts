import path from "node:path";
import ts from "typescript";
import { hash, type Finding } from "./evidence.js";

export interface Metric {
  project: string; file: string; type: string; member: string; kind: "function" | "component" | "hook" | "method";
  line: number; complexity: number; sourceLines: number; executableLines: number;
  halsteadVolume: number; maintainabilityIndex: number; coupling: number; inheritance: number;
}
type FunctionNode = ts.FunctionDeclaration | ts.FunctionExpression | ts.ArrowFunction | ts.MethodDeclaration | ts.ConstructorDeclaration | ts.GetAccessorDeclaration | ts.SetAccessorDeclaration;
function isFunction(node: ts.Node): node is FunctionNode {
  return ts.isFunctionDeclaration(node) || ts.isFunctionExpression(node) || ts.isArrowFunction(node) ||
    ts.isMethodDeclaration(node) || ts.isConstructorDeclaration(node) || ts.isGetAccessorDeclaration(node) || ts.isSetAccessorDeclaration(node);
}
function nameOf(node: FunctionNode, source: ts.SourceFile): string {
  if (node.name) return node.name.getText(source);
  if (ts.isConstructorDeclaration(node)) return "constructor";
  if (ts.isVariableDeclaration(node.parent) || ts.isPropertyAssignment(node.parent)) return node.parent.name.getText(source);
  if (ts.isCallExpression(node.parent)) return `${node.parent.expression.getText(source)}:callback${node.parent.arguments.indexOf(node as ts.Expression)}`;
  return "anonymous";
}
function ownVisit(node: ts.Node, visit: (node: ts.Node) => void) {
  if (isFunction(node) || ts.isTypeNode(node) || ts.isInterfaceDeclaration(node) || ts.isTypeAliasDeclaration(node)) return;
  visit(node);
  ts.forEachChild(node, child => ownVisit(child, visit));
}
export function maintainability(volume: number, complexity: number, lines: number): number {
  return Math.round(Math.max(0, (171 - 5.2 * Math.log(Math.max(volume, 1)) - 0.23 * complexity - 16.2 * Math.log(Math.max(lines, 1))) * 100 / 171));
}
export function analyzeFile(source: ts.SourceFile, checker: ts.TypeChecker, project: string, root: string) {
  const file = path.relative(root, source.fileName).replaceAll("\\", "/");
  const metrics: Metric[] = [];
  const findings: { dimension: "codeQuality" | "performanceAsync"; finding: Finding }[] = [];
  const importCount = new Set(source.statements.filter(ts.isImportDeclaration).map(node => node.moduleSpecifier.getText(source))).size;
  const identities = new Map<string, number>();
  function emit(category: string, dimension: "codeQuality" | "performanceAsync", node: ts.Node, member: string,
    message: string, observations: Record<string, unknown>, confidence: Finding["confidence"] = "high") {
    const ruleId = `javascript-typescript/${dimension}/${category}`;
    const identity = `${ruleId}|${file}|${project}|${member}`;
    const occurrence = identities.get(identity) ?? 0; identities.set(identity, occurrence + 1);
    findings.push({ dimension, finding: { category, ruleId, fingerprint: hash(`${identity}|${occurrence}`),
      severity: "warning", confidence, file, line: source.getLineAndCharacterOfPosition(node.getStart(source)).line + 1,
      project, member, message, observations } });
  }
  function visit(node: ts.Node, parentName = "") {
    if (isFunction(node) && node.body) {
      const name = nameOf(node, source);
      const member = parentName ? `${parentName}/${name}` : name;
      const parentClass = ts.isClassDeclaration(node.parent) || ts.isClassExpression(node.parent) ? node.parent : undefined;
      let complexity = 1, executableLines = 0, hasJsx = false;
      ownVisit(node.body, child => {
        if (ts.isIfStatement(child) || ts.isForStatement(child) || ts.isForInStatement(child) || ts.isForOfStatement(child) ||
            ts.isWhileStatement(child) || ts.isDoStatement(child) || ts.isCatchClause(child) || ts.isCaseClause(child) || ts.isConditionalExpression(child)) complexity++;
        if (ts.isBinaryExpression(child) && [ts.SyntaxKind.AmpersandAmpersandToken, ts.SyntaxKind.BarBarToken, ts.SyntaxKind.QuestionQuestionToken].includes(child.operatorToken.kind)) complexity++;
        if (ts.isStatement(child) && !ts.isBlock(child) && !ts.isEmptyStatement(child)) executableLines++;
        if (ts.isJsxElement(child) || ts.isJsxSelfClosingElement(child) || ts.isJsxFragment(child)) hasJsx = true;
        if (ts.isCallExpression(child)) {
          const symbol = checker.getSymbolAtLocation(ts.isPropertyAccessExpression(child.expression) ? child.expression.name : child.expression);
          const declarations = symbol?.declarations ?? [];
          const reactHook = declarations.some(declaration => {
            let cursor: ts.Node | undefined = declaration;
            while (cursor && !ts.isImportDeclaration(cursor)) cursor = cursor.parent;
            return cursor && ts.isStringLiteral(cursor.moduleSpecifier) && cursor.moduleSpecifier.text === "react";
          });
          const hookName = declarations.find(ts.isImportSpecifier);
          const calledName = hookName ? (hookName.propertyName ?? hookName.name).text : ts.isPropertyAccessExpression(child.expression) ? child.expression.name.text : "";
          const namespaceSymbol = ts.isPropertyAccessExpression(child.expression) ? checker.getSymbolAtLocation(child.expression.expression) : undefined;
          const reactNamespace = namespaceSymbol?.declarations?.some(declaration => {
            let cursor: ts.Node | undefined = declaration;
            while (cursor && !ts.isImportDeclaration(cursor)) cursor = cursor.parent;
            return cursor && ts.isStringLiteral(cursor.moduleSpecifier) && cursor.moduleSpecifier.text === "react";
          });
          if ((reactHook || reactNamespace) && /^use[A-Z]/.test(calledName)) {
            let ancestor = child.parent;
            while (ancestor && ancestor !== node.body) {
              if (ts.isIfStatement(ancestor) || ts.isConditionalExpression(ancestor) || ts.isForStatement(ancestor) || ts.isForOfStatement(ancestor) || ts.isForInStatement(ancestor) || ts.isWhileStatement(ancestor) || ts.isDoStatement(ancestor) ||
                  ts.isBinaryExpression(ancestor) && [ts.SyntaxKind.AmpersandAmpersandToken, ts.SyntaxKind.BarBarToken, ts.SyntaxKind.QuestionQuestionToken].includes(ancestor.operatorToken.kind)) {
                emit("conditionalHook", "performanceAsync", child, member, `${calledName} is called within a conditional or loop.`, { hook: calledName }); break;
              }
              ancestor = ancestor.parent;
            }
            if (["useEffect", "useLayoutEffect"].includes(calledName)) {
              const callback = child.arguments[0];
              if (callback && isFunction(callback) && callback.modifiers?.some(modifier => modifier.kind === ts.SyntaxKind.AsyncKeyword))
                emit("asyncEffectCallback", "performanceAsync", child, member, "An effect callback is async and returns a Promise instead of cleanup or undefined.", { hook: calledName });
              if (child.arguments.length < 2)
                emit("effectWithoutDependencies", "performanceAsync", child, member, "Effect runs after every render; verify that this is intentional.", { hook: calledName }, "low");
            }
          }
        }
      });
      if (!ts.isBlock(node.body)) executableLines++;
      const operators: string[] = [], operands: string[] = [], sourceLineSet = new Set<number>();
      function tokens(current: ts.Node) {
        if (current !== node && (isFunction(current) || ts.isTypeNode(current))) return;
        // Markup contributes source lines, but not Halstead operators or operands.
        if (ts.isJsxText(current)) return;
        const children = current.getChildren(source);
        if (children.length) { children.forEach(tokens); return; }
        if (current.kind === ts.SyntaxKind.EndOfFileToken || current.getWidth(source) === 0) return;
        const text = current.getText(source);
        if (!["{", "}", ";"].includes(text)) sourceLineSet.add(source.getLineAndCharacterOfPosition(current.getStart(source)).line);
        if (ts.isIdentifier(current) || ts.isLiteralExpression(current)) operands.push(text);
        else operators.push(text);
      }
      tokens(node);
      const vocabulary = new Set(operators).size + new Set(operands).size;
      const volume = vocabulary > 1 ? (operators.length + operands.length) * Math.log2(vocabulary) : 0;
      let inheritance = 0;
      if (parentClass) {
        const seen = new Set<ts.Type>();
        let current: ts.Type | undefined = checker.getTypeAtLocation(parentClass);
        while (current && !seen.has(current)) { seen.add(current); current = current.getBaseTypes()?.[0]; if (current) inheritance++; }
      }
      const kind = parentClass ? "method" : /^use[A-Z]/.test(name) ? "hook" : hasJsx && /^[A-Z]/.test(name) ? "component" : "function";
      const type = parentClass?.name?.text ?? (parentName || name);
      const metric: Metric = { project, file, type, member, kind, line: source.getLineAndCharacterOfPosition(node.getStart(source)).line + 1,
        complexity, sourceLines: Math.max(1, sourceLineSet.size), executableLines, halsteadVolume: volume,
        maintainabilityIndex: maintainability(volume, complexity, Math.max(1, sourceLineSet.size)), coupling: importCount, inheritance };
      metrics.push(metric);
      if (complexity > 10) emit("highFunctionComplexity", "codeQuality", node, member,
        `${member} has cyclomatic complexity ${complexity} (threshold: 10).`, { measured: complexity, threshold: 10, metric: "cyclomaticComplexity", kind });
      ts.forEachChild(node, child => visit(child, member));
      return;
    }
    if (!ts.isTypeNode(node)) ts.forEachChild(node, child => visit(child, parentName));
  }
  visit(source);
  const reactSupported = source.statements.some(statement => ts.isImportDeclaration(statement) && !statement.importClause?.isTypeOnly &&
    ts.isStringLiteral(statement.moduleSpecifier) && statement.moduleSpecifier.text === "react");
  return { metrics, findings, reactSupported };
}
