import ts from "typescript";

export interface ModuleReference {
  node: ts.Node; literal?: ts.StringLiteralLike;
  form: "import" | "reExport" | "importEquals" | "importType" | "require" | "dynamicImport";
  usage: "value" | "typeOnly" | "mixed";
}

function bindingUsage(typeOnly: boolean, elements: readonly { isTypeOnly: boolean }[] | undefined, hasDefault = false): ModuleReference["usage"] {
  if (typeOnly) return "typeOnly";
  const typed = elements?.filter(element => element.isTypeOnly).length ?? 0;
  if (typed && typed === elements!.length && !hasDefault) return "typeOnly";
  return typed ? "mixed" : "value";
}
function ambientNodeName(node: ts.Identifier, checker: ts.TypeChecker): boolean {
  const declarations = checker.getSymbolAtLocation(node)?.declarations;
  return !declarations?.length || declarations.every(declaration => declaration.getSourceFile().isDeclarationFile &&
    declaration.getSourceFile().fileName.replaceAll("\\", "/").includes("/node_modules/@types/node/"));
}
function isRequire(node: ts.Expression, checker: ts.TypeChecker): boolean {
  if (ts.isIdentifier(node)) return node.text === "require" && ambientNodeName(node, checker);
  return ts.isPropertyAccessExpression(node) && node.name.text === "require" && ts.isIdentifier(node.expression) &&
    node.expression.text === "module" && ambientNodeName(node.expression, checker);
}
export function moduleReferences(source: ts.SourceFile, checker: ts.TypeChecker): ModuleReference[] {
  const references: ModuleReference[] = [];
  function add(node: ts.Node, expression: ts.Node | undefined, form: ModuleReference["form"], usage: ModuleReference["usage"]) {
    references.push({ node, literal: expression && ts.isStringLiteralLike(expression) ? expression : undefined, form, usage });
  }
  function visit(node: ts.Node) {
    if (ts.isImportDeclaration(node)) {
      const clause = node.importClause;
      const bindings = clause?.namedBindings;
      add(node, node.moduleSpecifier, "import", bindingUsage(clause?.isTypeOnly ?? false,
        bindings && ts.isNamedImports(bindings) ? bindings.elements : undefined, !!clause?.name));
    } else if (ts.isExportDeclaration(node) && node.moduleSpecifier) {
      add(node, node.moduleSpecifier, "reExport", bindingUsage(node.isTypeOnly,
        node.exportClause && ts.isNamedExports(node.exportClause) ? node.exportClause.elements : undefined));
    } else if (ts.isImportEqualsDeclaration(node) && ts.isExternalModuleReference(node.moduleReference)) {
      add(node, node.moduleReference.expression, "importEquals", node.isTypeOnly ? "typeOnly" : "value");
    } else if (ts.isImportTypeNode(node) && ts.isLiteralTypeNode(node.argument)) {
      add(node, node.argument.literal, "importType", "typeOnly");
    } else if (ts.isCallExpression(node)) {
      if (node.expression.kind === ts.SyntaxKind.ImportKeyword) add(node, node.arguments[0], "dynamicImport", "value");
      else if (isRequire(node.expression, checker)) add(node, node.arguments[0], "require", "value");
    }
    ts.forEachChild(node, visit);
  }
  visit(source);
  return references;
}
