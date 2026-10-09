import ts from "typescript";
import path from "node:path";
import { discover } from "./discovery.js";
import { analyzeFile, type Metric } from "./metrics.js";
import { hash, invocationIds, skippedDimensions, type Evidence } from "./evidence.js";
import { metricsCsvHeader } from "./scorecard-contract.js";
import { scoreFunctions } from "./function-scoring.js";
import { scoreAsyncUsage } from "./async-scoring.js";
import { ModuleGraphCollector } from "./module-graph.js";
import { asyncPopulation, type AsyncOwner } from "./async-population.js";
import { decompositionModule, decompositionEvidence } from "./decomposition-evidence.js";
import { HandlerResolver } from "./handler-resolution.js";
import { inspectErrorHandling, errorHandlingEvidence } from "./error-handling.js";

export function analyze(options: { project?: string; tsconfig?: string; runId?: string; auditId?: string }, version: string): { evidence: Evidence; metrics: Metric[]; csv: string; inputs: string[] } {
  const identity = invocationIds(options.runId, options.auditId);
  const discovery = discover(options.project, options.tsconfig);
  const dimensions = skippedDimensions();
  const diagnostics: Evidence["analysis"]["diagnostics"] = [];
  const metrics: Metric[] = [];
  const functions: ReturnType<typeof analyzeFile>["functions"] = [];
  const collected: ReturnType<typeof analyzeFile>["findings"] = [];
  const moduleGraph = new ModuleGraphCollector(discovery.repositoryRoot, discovery.packages);
  let analyzedFiles = 0;
  const asyncOwners: AsyncOwner[] = [];
  const decompositionModules: ReturnType<typeof decompositionModule>[] = [];
  const errorHandling: ReturnType<typeof inspectErrorHandling>[] = [];
  for (const pkg of discovery.packages) {
    const program = ts.createProgram(pkg.files, { ...pkg.options, noEmit: true });
    const checker = program.getTypeChecker();
    const handlerSources = pkg.files.map(filename => program.getSourceFile(filename))
      .filter((source): source is ts.SourceFile => !!source && !program.getSyntacticDiagnostics(source).length);
    const handlerResolver = new HandlerResolver(checker, handlerSources, discovery.repositoryRoot, pkg.name);
    const collectModules = moduleGraph.forPackage(pkg, program);
    for (const filename of pkg.files) {
      const source = program.getSourceFile(filename);
      if (!source) { diagnostics.push({ kind: "sourceUnavailable", message: filename, project: pkg.name }); continue; }
      const errors = program.getSyntacticDiagnostics(source);
      if (errors.length) {
        diagnostics.push(...errors.map(error => ({ kind: "parseError", message: `${filename}: ${ts.flattenDiagnosticMessageText(error.messageText, " ")}`, project: pkg.name })));
        continue;
      }
      analyzedFiles++;
      collectModules(source);
      const result = analyzeFile(source, checker, pkg.name, discovery.repositoryRoot);
      const file = path.relative(discovery.repositoryRoot, filename).replaceAll("\\", "/");
      asyncOwners.push(...asyncPopulation(source, checker, file, pkg.name, result.functions,
        result.findings.filter(item => item.dimension === "performanceAsync").map(item => item.finding)));
      decompositionModules.push(decompositionModule(source, file, pkg.name, result.functions));
      errorHandling.push(inspectErrorHandling(source, checker, file, pkg.name, handlerResolver));
      metrics.push(...result.metrics); collected.push(...result.findings);
      functions.push(...result.functions);
    }
  }
  metrics.sort((a,b) => a.file < b.file ? -1 : a.file > b.file ? 1 : a.line - b.line);
  if (analyzedFiles === 0) diagnostics.push({ kind: "emptyPopulation", message: "No production source files were analyzed." });
  dimensions.codeQuality = scoreFunctions(functions, "codeQuality", collected.filter(item => item.dimension === "codeQuality").map(item => item.finding));
  dimensions.maintainability = scoreFunctions(functions, "maintainability");
  dimensions.performanceAsync = scoreAsyncUsage(collected.filter(item => item.dimension === "performanceAsync").map(item => item.finding),
    asyncOwners);
  if (dimensions.performanceAsync.status === "failed") diagnostics.push({ kind: "asyncOwnership", message: dimensions.performanceAsync.basis });
  if (diagnostics.length) for (const key of ["codeQuality", "maintainability", "performanceAsync"] as const)
    dimensions[key] = { status: "failed", basis: "Incomplete source analysis; partial findings are unscored.", findings: dimensions[key].findings, scope: dimensions[key].scope };
  dimensions.architecture = moduleGraph.finish(diagnostics.length > 0);
  dimensions.errorHandling = errorHandlingEvidence(errorHandling, diagnostics.length > 0);
  dimensions.codeQuality.componentDetails = { ...(dimensions.codeQuality.componentDetails as object ?? {}),
    decomposition: decompositionEvidence(decompositionModules, diagnostics.length > 0) };
  const evidence: Evidence = {
    schemaVersion: 3, generatedAtUtc: new Date().toISOString(), tool: { name: "codemetrics-ai", version, ecosystem: "javascript-typescript" },
    subject: { root: discovery.repositoryRoot, entryPoint: discovery.entryPoint, name: discovery.name, variant: "source" },
    filters: { totalUnits: discovery.packages.reduce((sum,pkg) => sum + pkg.files.length, 0) + discovery.skipped.length,
      analyzedUnits: analyzedFiles, skipped: discovery.skipped },
    population: { types: new Set(metrics.map(metric => `${metric.project}|${metric.file}|${metric.type}`)).size, members: metrics.length },
    dimensions, analysis: { ...identity, status: diagnostics.length ? "incomplete" : "complete", ruleset: "javascript-typescript-2026-10-06-handler-disposition",
      calibration: "uncalibrated", configurationFingerprint: hash(JSON.stringify(canonicalConfiguration(discovery.packages.map(pkg => ({ name: pkg.name,
        options: pkg.options, selection: pkg.selection })), discovery.repositoryRoot))), diagnostics, suppressions: [] }
  };
  return { evidence, metrics, csv: toCsv(metrics), inputs: discovery.packages.flatMap(pkg => [pkg.entryPoint, ...pkg.files]) };
}
function canonicalConfiguration(value: unknown, root: string): unknown {
  if (typeof value === "string") return value.replaceAll("\\", "/").replace(root.replaceAll("\\", "/") + "/", "<root>/");
  if (Array.isArray(value)) return value.map(item => canonicalConfiguration(item, root));
  if (value && typeof value === "object") return Object.fromEntries(Object.entries(value).sort(([a],[b]) => a < b ? -1 : 1)
    .map(([key,item]) => [key,canonicalConfiguration(item,root)]));
  return value;
}
function toCsv(metrics: Metric[]) {
  const escape = (value: unknown) => `"${String(value).replaceAll('"', '""')}"`;
  const rows: unknown[][] = [];
  const types = new Map<string, Metric[]>();
  for (const metric of metrics) {
    const key = `${metric.project}|${metric.file}|${metric.type}`;
    const group = types.get(key) ?? []; group.push(metric); types.set(key, group);
  }
  for (const members of types.values()) {
    const first = members[0];
    rows.push(["Type", first.project, first.file, first.type, "", Math.round(members.reduce((sum,m) => sum+m.maintainabilityIndex,0)/members.length),
      members.reduce((sum,m) => sum+m.complexity,0), Math.max(...members.map(m=>m.inheritance)), first.coupling,
      members.reduce((sum,m) => sum+m.sourceLines,0), members.reduce((sum,m)=>sum+m.executableLines,0)]);
    for (const member of members) rows.push(["Member", member.project, member.file, member.type, member.member, member.maintainabilityIndex,
      member.complexity, member.inheritance, member.coupling, member.sourceLines, member.executableLines]);
  }
  return metricsCsvHeader + "\n" + rows.map(row => row.map(escape).join(",")).join("\n") + "\n";
}
