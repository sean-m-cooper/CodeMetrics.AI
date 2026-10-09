import ts from "typescript";
import { hash, scored, type Dimension, type Finding } from "./evidence.js";
import { HandlerResolver, unwrapHandler } from "./handler-resolution.js";
import { classifyDisposition, type HandlerDisposition } from "./handler-disposition.js";
import { errorHandlingDecision } from "./error-handling-scoring.js";

type UsageKind = "catchClause" | "promiseCatch" | "promiseThenRejection";
interface Handler {
  id: string; file: string; project: string; line: number; kind: "catchClause" | "promiseRejection";
  classification: "unexplainedEmpty" | "documentedEmpty" | "containsCode"; comments: string[];
  disposition: HandlerDisposition;
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
  const nested: { start: number; end: number }[] = [];
  function visit(node: ts.Node) {
    if (node !== body && (ts.isFunctionLike(node) || ts.isLiteralExpression(node) || ts.isTemplateExpression(node))) {
      nested.push({ start: ts.isFunctionLike(node) ? node.pos : node.getStart(source), end: node.end }); return;
    }
    ts.forEachChild(node, visit);
  }
  visit(body);
  const scanner = ts.createScanner(ts.ScriptTarget.Latest, false, source.languageVariant, source.text.slice(body.pos, body.end));
  const comments: string[] = [];
  for (let token = scanner.scan(); token !== ts.SyntaxKind.EndOfFileToken; token = scanner.scan()) {
    if (token !== ts.SyntaxKind.SingleLineCommentTrivia && token !== ts.SyntaxKind.MultiLineCommentTrivia) continue;
    if (nested.some(range => body.pos + scanner.getTokenPos() >= range.start && body.pos + scanner.getTokenPos() < range.end)) continue;
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
    const empty = emptyBody(body, checker), comments = bodyComments(body, body.getSourceFile());
    const disposition = classifyDisposition(body, checker, empty, comments, at);
    if (existing) {
      existing.uses.push(use);
      if (existing.disposition.classification !== disposition.classification)
        existing.disposition = { classification: "unknown", reason: "Uses of the shared handler have different failure contexts." };
      return;
    }
    const classification = !empty ? "containsCode" : comments.length ? "documentedEmpty" : "unexplainedEmpty";
    handlers.set(identity.id, { ...identity, kind: kind === "catchClause" ? "catchClause" : "promiseRejection", classification, comments,
      disposition, uses: [use] });
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
function handlerFinding(handler: Handler, scoreAvailable: boolean): Finding {
  const actionable = handler.disposition.classification === "unexplainedSwallowing";
  const category = actionable ? "unexplainedSwallowing" : "unknownHandlerDisposition";
  return { category, ruleId: `javascript-typescript/errorHandling/${category}`,
    fingerprint: hash(`${handler.id}|${category}`), file: handler.file, project: handler.project, line: handler.line,
    severity: actionable && scoreAvailable ? "warning" : "info", confidence: actionable ? "high" : "low",
    message: handler.disposition.reason,
    observations: { handlerId: handler.id, kind: handler.kind, uses: handler.uses, classification: handler.disposition.classification,
      scoreDisposition: !actionable ? "excludedUnknown" : scoreAvailable ? "policyInput" : "excludedUnavailablePopulation" } };
}
export function errorHandlingEvidence(results: ReturnType<typeof inspectErrorHandling>[], incomplete: boolean): Dimension {
  const unique = new Map<string, Handler>();
  for (const row of results.flatMap(result => result.handlers)) {
    const existing = unique.get(row.id);
    if (existing) {
      existing.uses.push(...row.uses);
      if (existing.disposition.classification !== row.disposition.classification)
        existing.disposition = { classification: "unknown", reason: "Selected project contexts disagree about the shared handler disposition." };
    }
    else unique.set(row.id, { ...row, uses: [...row.uses] });
  }
  const handlers = [...unique.values()].sort((a, b) => a.file.localeCompare(b.file, "en") || a.line - b.line);
  for (const handler of handlers) handler.uses.sort((a,b) => a.file.localeCompare(b.file, "en") || a.line - b.line || a.kind.localeCompare(b.kind, "en"));
  const uninspectedCallbacks = results.flatMap(result => result.uninspectedCallbacks)
    .sort((a,b) => a.file.localeCompare(b.file, "en") || a.line - b.line);
  const count = (classification: Handler["classification"]) => handlers.filter(handler => handler.classification === classification).length;
  const unexplained = count("unexplainedEmpty");
  const dispositions = Object.fromEntries(["propagated", "failureResult", "reported", "fallback", "documented", "unexplainedSwallowing", "unknown"]
    .map(kind => [kind, handlers.filter(handler => handler.disposition.classification === kind).length]));
  const decision = incomplete ? undefined : errorHandlingDecision(handlers.length, dispositions.unexplainedSwallowing, dispositions.unknown, uninspectedCallbacks.length);
  const findings = handlers.filter(handler => ["unexplainedSwallowing", "unknown"].includes(handler.disposition.classification))
    .map(handler => handlerFinding(handler, !!decision));
  const scope: Dimension["scope"] = { id: "javascript-typescript/errorHandling/failure-disposition-v1", coverage: "partial",
    includes: ["catch-clauses", "inline-standard-promise-rejection-handlers", "attached-intent", "selected-source-referenced-handlers", "bounded-failure-continuations"],
    excludes: ["general-exception-flow", "arbitrary-callback-value-flow", "correctness-of-recovery", "semantic-rationale-judgment"] };
  const dispositionEvidence = { version: 1, countingUnit: "distinctHandlerBody", totalHandlers: handlers.length,
    assessedHandlers: handlers.length - dispositions.unknown, unknownHandlers: dispositions.unknown, counts: dispositions,
    assessedPercent: handlers.length ? 100 * (handlers.length - dispositions.unknown) / handlers.length : null,
    unresolvedCallbackUses: uninspectedCallbacks.length, populationComplete: !incomplete && !dispositions.unknown && !uninspectedCallbacks.length };
  const dimension: Dimension = decision ? scored(decision,
    "Partial Error Handling: unexplained swallowing among distinct assessed handler bodies. Documented intent is honored; failure outcomes are signals, not proof of correct recovery.",
    findings, dispositionEvidence) : { status: incomplete ? "failed" : "skipped", findings,
      basis: incomplete ? "Incomplete source; handler observations are diagnostic only." : !handlers.length ? "No assessed handler population; error handling is unavailable." :
      "Unknown handler dispositions or unresolved callback uses prevent a reliable population score." };
  return { ...dimension, scope, dispositionEvidence, handlerEvidence: { version: 2, countingUnit: "distinctHandlerBody",
      totalHandlers: handlers.length, unexplainedEmptyHandlers: unexplained, documentedEmptyHandlers: count("documentedEmpty"),
      handlersContainingCode: count("containsCode"), unexplainedEmptyPercent: handlers.length ? 100 * unexplained / handlers.length : null,
      handlerUseSites: handlers.reduce((sum, handler) => sum + handler.uses.length, 0),
      referencedCallbackUseSites: handlers.reduce((sum, handler) => sum + handler.uses.filter(use => use.resolution === "reference").length, 0),
      uninspectedRejectionCallbacks: uninspectedCallbacks.length, uninspectedCallbacks, handlers } };
}
