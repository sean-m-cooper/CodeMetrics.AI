import ts from "typescript";
import { hash, type Dimension, type Finding } from "./evidence.js";
import { HandlerResolver, unwrapHandler } from "./handler-resolution.js";

type UsageKind = "catchClause" | "promiseCatch" | "promiseThenRejection";
interface Handler {
  id: string; file: string; project: string; line: number; kind: "catchClause" | "promiseRejection";
  classification: "unexplainedEmpty" | "documentedEmpty" | "containsCode"; comments: string[];
  uses: { file: string; line: number; kind: UsageKind; resolution: "inline" | "reference" }[];
}
function undefinedValue(node: ts.Expression, checker: ts.TypeChecker): boolean {
  node = unwrapHandler(node);
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
function rejectionArgument(call: ts.CallExpression, checker: ts.TypeChecker): { index: number; kind: UsageKind } | undefined {
  const declaration = checker.getResolvedSignature(call)?.declaration;
  if (!declaration || !ts.isMethodSignature(declaration) || !ts.isIdentifier(declaration.name) ||
    !ts.isInterfaceDeclaration(declaration.parent) || !["Promise", "PromiseLike"].includes(declaration.parent.name.text)) return;
  const source = declaration.getSourceFile();
  if (!source.hasNoDefaultLib || !/(?:^|[/\\])lib\.[^/\\]+\.d\.ts$/.test(source.fileName)) return;
  if (declaration.name.text === "catch") return { index: 0, kind: "promiseCatch" };
  if (declaration.name.text === "then" && call.arguments.length > 1) return { index: 1, kind: "promiseThenRejection" };
}
export function inspectErrorHandling(source: ts.SourceFile, checker: ts.TypeChecker, file: string, project: string, resolver: HandlerResolver) {
  const handlers = new Map<string, Handler>();
  const uninspectedCallbacks: { file: string; line: number; kind: UsageKind; reason: string }[] = [];
  function add(body: ts.ConciseBody, kind: UsageKind, at: ts.Node, resolution: "inline" | "reference") {
    const identity = resolver.identity(body);
    const existing = handlers.get(identity.id);
    const use = { file, line: source.getLineAndCharacterOfPosition(at.getStart(source)).line + 1, kind, resolution };
    if (existing) { existing.uses.push(use); return; }
    const empty = emptyBody(body, checker), comments = empty ? bodyComments(body, body.getSourceFile()) : [];
    const classification = !empty ? "containsCode" : comments.length ? "documentedEmpty" : "unexplainedEmpty";
    handlers.set(identity.id, { ...identity, kind: kind === "catchClause" ? "catchClause" : "promiseRejection", classification, comments, uses: [use] });
  }
  function visit(node: ts.Node) {
    if (ts.isTypeNode(node) && !ts.isExpressionWithTypeArguments(node)) return;
    if (ts.isCatchClause(node)) add(node.block, "catchClause", node, "inline");
    if (ts.isCallExpression(node)) {
      const rejection = rejectionArgument(node, checker);
      if (rejection) {
        const resolved = resolver.resolve(node.arguments[rejection.index]);
        if ("body" in resolved) add(resolved.body, rejection.kind, node, resolved.resolution);
        else uninspectedCallbacks.push({ file, line: source.getLineAndCharacterOfPosition(node.getStart(source)).line + 1,
          kind: rejection.kind, reason: resolved.reason });
      }
    }
    ts.forEachChild(node, visit);
  }
  visit(source); return { handlers: [...handlers.values()], uninspectedCallbacks };
}
function handlerFinding(handler: Handler): Finding {
  return { category: "unexplainedEmptyHandler", ruleId: "javascript-typescript/errorHandling/unexplainedEmptyHandler",
    fingerprint: hash(`${handler.id}|unexplainedEmptyHandler`), file: handler.file, project: handler.project, line: handler.line,
    severity: "info", confidence: "high",
    message: "Handler contains only empty syntax or an undefined return, without a body comment. Review whether discarding the failure is intentional.",
    observations: { handlerId: handler.id, kind: handler.kind, uses: handler.uses, classification: "reviewLead", scoreDisposition: "excludedUncalibrated" } };
}
export function errorHandlingEvidence(results: ReturnType<typeof inspectErrorHandling>[], incomplete: boolean): Dimension {
  const unique = new Map<string, Handler>();
  for (const row of results.flatMap(result => result.handlers)) {
    const existing = unique.get(row.id);
    if (existing) existing.uses.push(...row.uses);
    else unique.set(row.id, { ...row, uses: [...row.uses] });
  }
  const handlers = [...unique.values()].sort((a, b) => a.file.localeCompare(b.file, "en") || a.line - b.line);
  for (const handler of handlers) handler.uses.sort((a,b) => a.file.localeCompare(b.file, "en") || a.line - b.line || a.kind.localeCompare(b.kind, "en"));
  const uninspectedCallbacks = results.flatMap(result => result.uninspectedCallbacks)
    .sort((a,b) => a.file.localeCompare(b.file, "en") || a.line - b.line);
  const count = (classification: Handler["classification"]) => handlers.filter(handler => handler.classification === classification).length;
  const unexplained = count("unexplainedEmpty");
  return { status: incomplete ? "failed" : "skipped", basis: incomplete ? "Incomplete source; handler observations are diagnostic only." :
    "Handler syntax and documented intent are observed; error-handling scoring is not calibrated. Code presence is not proof of recovery.",
    scope: { id: "javascript-typescript/errorHandling/handler-evidence-v2", coverage: "partial",
      includes: ["catch-clauses", "inline-standard-promise-rejection-handlers", "body-comments", "selected-source-referenced-handlers"],
      excludes: ["error-handling-score", "general-exception-flow", "arbitrary-callback-value-flow", "correctness-of-recovery", "semantic-rationale-judgment"] },
    findings: handlers.filter(handler => handler.classification === "unexplainedEmpty").map(handlerFinding), handlerEvidence: { version: 2, countingUnit: "distinctHandlerBody",
      totalHandlers: handlers.length, unexplainedEmptyHandlers: unexplained, documentedEmptyHandlers: count("documentedEmpty"),
      handlersContainingCode: count("containsCode"), unexplainedEmptyPercent: handlers.length ? 100 * unexplained / handlers.length : null,
      handlerUseSites: handlers.reduce((sum, handler) => sum + handler.uses.length, 0),
      referencedCallbackUseSites: handlers.reduce((sum, handler) => sum + handler.uses.filter(use => use.resolution === "reference").length, 0),
      uninspectedRejectionCallbacks: uninspectedCallbacks.length, uninspectedCallbacks, handlers } };
}
