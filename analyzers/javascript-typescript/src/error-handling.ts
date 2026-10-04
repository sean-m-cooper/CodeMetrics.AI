import ts from "typescript";
import { hash, type Dimension, type Finding } from "./evidence.js";
import { isFunction } from "./function-nodes.js";

interface Handler {
  id: string; file: string; project: string; line: number; kind: "catchClause" | "promiseCatch" | "promiseThenRejection";
  classification: "unexplainedEmpty" | "documentedEmpty" | "containsCode"; comments: string[];
}
function unwrap(node: ts.Expression): ts.Expression {
  while (ts.isParenthesizedExpression(node) || ts.isAsExpression(node) || ts.isTypeAssertionExpression(node) || ts.isNonNullExpression(node) || ts.isSatisfiesExpression(node)) node = node.expression;
  return node;
}
function undefinedValue(node: ts.Expression, checker: ts.TypeChecker): boolean {
  node = unwrap(node);
  if (ts.isVoidExpression(node)) return ts.isNumericLiteral(node.expression);
  if (!ts.isIdentifier(node) || node.text !== "undefined") return false;
  const declarations = checker.getSymbolAtLocation(node)?.declarations ?? [];
  return declarations.length === 0 || declarations.every(declaration => declaration.getSourceFile().hasNoDefaultLib);
}
function emptyBody(body: ts.ConciseBody, checker: ts.TypeChecker): boolean {
  if (!ts.isBlock(body)) return undefinedValue(body, checker);
  return body.statements.every(statement => ts.isEmptyStatement(statement) ||
    (ts.isReturnStatement(statement) && (!statement.expression || undefinedValue(statement.expression, checker))));
}
function bodyComments(body: ts.ConciseBody, source: ts.SourceFile): string[] {
  const scanner = ts.createScanner(ts.ScriptTarget.Latest, false, source.languageVariant, source.text.slice(body.pos, body.end));
  const comments: string[] = [];
  for (let token = scanner.scan(); token !== ts.SyntaxKind.EndOfFileToken; token = scanner.scan()) {
    if (token !== ts.SyntaxKind.SingleLineCommentTrivia && token !== ts.SyntaxKind.MultiLineCommentTrivia) continue;
    const text = scanner.getTokenText().replace(/^\/\/?\*?|\*\/$/g, "").trim();
    if (/[\p{L}\p{N}]/u.test(text)) comments.push(text);
  }
  return comments;
}
function rejectionArgument(call: ts.CallExpression, checker: ts.TypeChecker): { index: number; kind: Handler["kind"] } | undefined {
  const declaration = checker.getResolvedSignature(call)?.declaration;
  if (!declaration || !ts.isMethodSignature(declaration) || !ts.isIdentifier(declaration.name) ||
    !ts.isInterfaceDeclaration(declaration.parent) || !["Promise", "PromiseLike"].includes(declaration.parent.name.text)) return;
  const source = declaration.getSourceFile();
  if (!source.hasNoDefaultLib || !/(?:^|[/\\])lib\.[^/\\]+\.d\.ts$/.test(source.fileName)) return;
  if (declaration.name.text === "catch") return { index: 0, kind: "promiseCatch" };
  if (declaration.name.text === "then" && call.arguments.length > 1) return { index: 1, kind: "promiseThenRejection" };
}
export function inspectErrorHandling(source: ts.SourceFile, checker: ts.TypeChecker, file: string, project: string) {
  const handlers: Handler[] = [], findings: Finding[] = [];
  let uninspectedRejectionCallbacks = 0;
  const occurrences = new Map<string, number>();
  function add(body: ts.ConciseBody, kind: Handler["kind"]) {
    const empty = emptyBody(body, checker), comments = empty ? bodyComments(body, source) : [];
    const classification = !empty ? "containsCode" : comments.length ? "documentedEmpty" : "unexplainedEmpty";
    const ordinal = occurrences.get(kind) ?? 0; occurrences.set(kind, ordinal + 1);
    const handler: Handler = { id: hash(`${file}|${project}|${kind}|${ordinal}`), file, project,
      line: source.getLineAndCharacterOfPosition(body.getStart(source)).line + 1, kind, classification, comments };
    handlers.push(handler);
    if (classification === "unexplainedEmpty") findings.push({ category: "unexplainedEmptyHandler",
      ruleId: "javascript-typescript/errorHandling/unexplainedEmptyHandler", fingerprint: hash(`${handler.id}|unexplainedEmptyHandler`),
      file, project, line: handler.line, severity: "info", confidence: "high",
      message: "Handler contains only empty syntax or an undefined return, without a body comment. Review whether discarding the failure is intentional.",
      observations: { handlerId: handler.id, kind, classification: "reviewLead", scoreDisposition: "excludedUncalibrated" } });
  }
  function visit(node: ts.Node) {
    if (ts.isTypeNode(node) && !ts.isExpressionWithTypeArguments(node)) return;
    if (ts.isCatchClause(node)) add(node.block, "catchClause");
    if (ts.isCallExpression(node)) {
      const rejection = rejectionArgument(node, checker);
      if (rejection) {
        const argument = node.arguments[rejection.index];
        const callback = argument && unwrap(argument);
        if (callback && isFunction(callback) && callback.body) add(callback.body, rejection.kind);
        else uninspectedRejectionCallbacks++;
      }
    }
    ts.forEachChild(node, visit);
  }
  visit(source); return { handlers, findings, uninspectedRejectionCallbacks };
}
export function errorHandlingEvidence(results: ReturnType<typeof inspectErrorHandling>[], incomplete: boolean): Dimension {
  const handlers = results.flatMap(result => result.handlers).sort((a, b) => a.file.localeCompare(b.file, "en") || a.line - b.line);
  const count = (classification: Handler["classification"]) => handlers.filter(handler => handler.classification === classification).length;
  const unexplained = count("unexplainedEmpty");
  return { status: incomplete ? "failed" : "skipped", basis: incomplete ? "Incomplete source; handler observations are diagnostic only." :
    "Handler syntax and documented intent are observed; error-handling scoring is not calibrated. Code presence is not proof of recovery.",
    scope: { id: "javascript-typescript/errorHandling/handler-evidence-v1", coverage: "partial",
      includes: ["catch-clauses", "inline-standard-promise-rejection-handlers", "body-comments"],
      excludes: ["error-handling-score", "general-exception-flow", "referenced-callback-bodies", "correctness-of-recovery", "semantic-rationale-judgment"] },
    findings: results.flatMap(result => result.findings), handlerEvidence: { version: 1, countingUnit: "handlerOccurrence",
      totalHandlers: handlers.length, unexplainedEmptyHandlers: unexplained, documentedEmptyHandlers: count("documentedEmpty"),
      handlersContainingCode: count("containsCode"), unexplainedEmptyPercent: handlers.length ? 100 * unexplained / handlers.length : null,
      uninspectedRejectionCallbacks: results.reduce((sum, result) => sum + result.uninspectedRejectionCallbacks, 0), handlers } };
}
