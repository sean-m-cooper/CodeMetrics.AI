import ts from "typescript";
import { isFunction } from "./function-nodes.js";
import type { FunctionMeasurement } from "./function-measurements.js";

function ownedShape(root: ts.Node) {
  // A concise function body has an implicit return, independent of formatting.
  let statements = ts.isExpression(root) ? 1 : 0, dataLiteralEntries = 0;
  function visit(node: ts.Node) {
    if (ts.isExpressionWithTypeArguments(node)) { visit(node.expression); return; }
    if (isFunction(node) || ts.isTypeNode(node) || ts.isClassDeclaration(node) || ts.isClassExpression(node) ||
      ts.isInterfaceDeclaration(node) || ts.isTypeAliasDeclaration(node) || ts.isEnumDeclaration(node)) return;
    if (ts.isImportDeclaration(node) || ts.isImportEqualsDeclaration(node) || ts.isExportDeclaration(node)) return;
    if (ts.isVariableStatement(node) && !node.declarationList.declarations.some(declaration => declaration.initializer)) return;
    if (ts.isStatement(node) && !ts.isBlock(node) && !ts.isEmptyStatement(node) && !ts.isLabeledStatement(node)) statements++;
    if (ts.isObjectLiteralExpression(node)) dataLiteralEntries += node.properties.length;
    if (ts.isArrayLiteralExpression(node)) dataLiteralEntries += node.elements.filter(element => !ts.isOmittedExpression(element)).length;
    ts.forEachChild(node, visit);
  }
  visit(root); return { ownedStatements: statements, dataLiteralEntries };
}
export function decompositionModule(source: ts.SourceFile, file: string, project: string, functions: FunctionMeasurement[]) {
  const byStart = new Map(functions.map(item => [item.sourceSpanStart, item]));
  const rows: { id: string; member: string; line: number; ownedSourceLines: number; ownedStatements: number; dataLiteralEntries: number }[] = [];
  function visit(node: ts.Node) {
    if (isFunction(node) && node.body) {
      const item = byStart.get(node.getStart(source));
      if (item) rows.push({ id: item.id, member: item.member, line: item.line, ownedSourceLines: item.ownSourceLines, ...ownedShape(node.body) });
    }
    if (!ts.isTypeNode(node) || ts.isExpressionWithTypeArguments(node)) ts.forEachChild(node, visit);
  }
  visit(source);
  return { file, project, implementedFunctions: rows.length, sumOwnedFunctionLines: rows.reduce((sum, row) => sum + row.ownedSourceLines, 0),
    sumOwnedFunctionStatements: rows.reduce((sum, row) => sum + row.ownedStatements, 0),
    initializer: ownedShape(source), directReExports: source.statements.filter(node => ts.isExportDeclaration(node) && node.moduleSpecifier).length,
    typeDeclarations: source.statements.filter(node => ts.isTypeAliasDeclaration(node) || ts.isInterfaceDeclaration(node)).length,
    enums: source.statements.filter(ts.isEnumDeclaration).length, functions: rows };
}
export function decompositionEvidence(modules: ReturnType<typeof decompositionModule>[], incomplete: boolean) {
  const ordered = [...modules].sort((a, b) => a.file.localeCompare(b.file, "en") || a.project.localeCompare(b.project, "en"));
  const functions = ordered.flatMap(module => module.functions.map(row => ({ ...row, file: module.file, project: module.project })));
  const sizes = functions.map(row => row.ownedSourceLines).sort((a, b) => a - b);
  const sum = sizes.reduce((total, size) => total + size, 0);
  const statements = functions.map(row => row.ownedStatements).sort((a, b) => a - b);
  const statementSum = statements.reduce((total, size) => total + size, 0);
  return { version: 2, status: incomplete ? "failed" : "unscored", score: null,
    measurement: "owned-executable-statements-v2", primaryMeasure: "ownedStatements",
    basis: "Descriptive size evidence; no responsibility inference, threshold, finding or score contribution.",
    exclusions: ["nested-function-double-counting", "class-and-enum-initializers", "parameter-initializers", "semantic-responsibility"],
    population: { modules: ordered.length, functions: functions.length, sumOwnedFunctionLines: sum,
      sumOwnedFunctionStatements: statementSum,
      medianFunctionStatements: statements.length ? statements[Math.ceil(statements.length * 0.5) - 1] : null,
      p90FunctionStatements: statements.length ? statements[Math.ceil(statements.length * 0.9) - 1] : null,
      medianFunctionLines: sizes.length ? sizes[Math.ceil(sizes.length * 0.5) - 1] : null,
      p90FunctionLines: sizes.length ? sizes[Math.ceil(sizes.length * 0.9) - 1] : null }, modules: ordered,
    largestFunctions: [...functions].sort((a, b) => b.ownedStatements - a.ownedStatements || a.id.localeCompare(b.id, "en")).slice(0, 10),
    largestFunctionContainers: ordered.filter(module => module.sumOwnedFunctionStatements > 0)
      .sort((a, b) => b.sumOwnedFunctionStatements - a.sumOwnedFunctionStatements || a.file.localeCompare(b.file, "en")).slice(0, 10)
      .map(({ functions: _functions, ...module }) => ({ ...module,
        shareOfOwnedFunctionStatements: statementSum ? module.sumOwnedFunctionStatements / statementSum : null,
        shareOfOwnedFunctionLines: sum ? module.sumOwnedFunctionLines / sum : null })) };
}
