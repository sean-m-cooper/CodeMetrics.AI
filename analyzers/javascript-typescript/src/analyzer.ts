import ts from "typescript";
import { discover } from "./discovery.js";
import { analyzeFile, type Metric } from "./metrics.js";
import { hash, invocationIds, scored, skippedDimensions, type Evidence } from "./evidence.js";
import { metricsCsvHeader } from "./scorecard-contract.js";
import { firstMatch, thresholdDecision } from "./scoring-decision.js";

export function analyze(options: { project?: string; tsconfig?: string; runId?: string; auditId?: string }, version: string): { evidence: Evidence; metrics: Metric[]; csv: string; inputs: string[] } {
  const identity = invocationIds(options.runId, options.auditId);
  const discovery = discover(options.project, options.tsconfig);
  const dimensions = skippedDimensions();
  const diagnostics: Evidence["analysis"]["diagnostics"] = [];
  const metrics: Metric[] = [];
  const collected: ReturnType<typeof analyzeFile>["findings"] = [];
  let analyzedFiles = 0;
  let reactSupported = false;
  for (const pkg of discovery.packages) {
    const program = ts.createProgram(pkg.files, { ...pkg.options, noEmit: true });
    const checker = program.getTypeChecker();
    for (const filename of pkg.files) {
      const source = program.getSourceFile(filename);
      if (!source) { diagnostics.push({ kind: "sourceUnavailable", message: filename, project: pkg.name }); continue; }
      const errors = program.getSyntacticDiagnostics(source);
      if (errors.length) {
        diagnostics.push(...errors.map(error => ({ kind: "parseError", message: `${filename}: ${ts.flattenDiagnosticMessageText(error.messageText, " ")}`, project: pkg.name })));
        continue;
      }
      analyzedFiles++;
      const result = analyzeFile(source, checker, pkg.name, discovery.repositoryRoot);
      reactSupported ||= result.reactSupported;
      metrics.push(...result.metrics); collected.push(...result.findings);
    }
  }
  metrics.sort((a,b) => a.file < b.file ? -1 : a.file > b.file ? 1 : a.line - b.line);
  if (analyzedFiles === 0) diagnostics.push({ kind: "emptyPopulation", message: "No production source files were analyzed." });
  if (metrics.length) {
    const maximum = Math.max(...metrics.map(metric => metric.complexity));
    const quality = thresholdDecision("javascript-typescript/codeQuality/v1", maximum, [5, 10, 20, 40], false,
      [...new Set(collected.filter(item => item.dimension === "codeQuality").map(item => item.finding.category))]);
    dimensions.codeQuality = scored(quality, `Maximum member cyclomatic complexity=${maximum}; uncalibrated JS/TS policy.`,
      collected.filter(item => item.dimension === "codeQuality").map(item => item.finding), { maxMemberComplexity: maximum, thresholds: [5, 10, 20, 40] });
    const values = metrics.map(metric => metric.maintainabilityIndex).sort((a,b) => a-b);
    const median = (values[Math.floor((values.length-1)/2)] + values[Math.ceil((values.length-1)/2)]) / 2;
    dimensions.maintainability = scored(thresholdDecision("javascript-typescript/maintainability/v1", median, [85, 65, 40, 20], true),
      `Median member maintainability index=${median}; uncalibrated JS/TS policy.`, [], { median, thresholds: [85,65,40,20] });
    const performance = collected.filter(item => item.dimension === "performanceAsync").map(item => item.finding);
    const actionable = performance.filter(finding => finding.confidence !== "low");
    if (reactSupported) dimensions.performanceAsync = scored(firstMatch("javascript-typescript/performanceAsync/react-hooks/v1",
      { actionableFindings: actionable.length, advisoryFindings: performance.length - actionable.length }, [
        { id: "actionableHooks", condition: "actionableFindings > 0", matched: actionable.length > 0, score: 6, categories: [...new Set(actionable.map(f => f.category))] },
        { id: "noActionableHooks", condition: "otherwise", matched: true, score: 10 }
      ]),
      "Limited scope: React hook placement and effect callbacks. General async and concurrency analysis is not implemented.", performance,
      { actionableFindings: actionable.length, advisoryFindings: performance.length-actionable.length, scope: "react-hooks" }, finding => finding.confidence !== "low");
  }
  if (diagnostics.length) for (const key of ["codeQuality", "maintainability", "performanceAsync"] as const)
    dimensions[key] = { status: "failed", basis: "Incomplete source analysis; partial findings are unscored.", findings: dimensions[key].findings };
  for (const [key, includes, excludes] of [
    ["codeQuality", ["maximum-member-cyclomatic-complexity"], ["design-quality", "runtime-behavior"]],
    ["maintainability", ["median-member-maintainability-index"], ["change-cost", "comprehensive-human-review"]],
    ["performanceAsync", ["react-hook-placement", "react-effect-callbacks"], ["general-async", "concurrency", "runtime-performance"]]
  ] as const) dimensions[key].scope = { id: `javascript-typescript/${key}/v1`, coverage: "partial", includes: [...includes], excludes: [...excludes] };
  const evidence: Evidence = {
    schemaVersion: 3, generatedAtUtc: new Date().toISOString(), tool: { name: "codemetrics-ai", version, ecosystem: "javascript-typescript" },
    subject: { root: discovery.repositoryRoot, entryPoint: discovery.entryPoint, name: discovery.name, variant: "source" },
    filters: { totalUnits: discovery.packages.reduce((sum,pkg) => sum + pkg.files.length, 0) + discovery.skipped.length,
      analyzedUnits: analyzedFiles, skipped: discovery.skipped },
    population: { types: new Set(metrics.map(metric => `${metric.project}|${metric.file}|${metric.type}`)).size, members: metrics.length },
    dimensions, analysis: { ...identity, status: diagnostics.length ? "incomplete" : "complete", ruleset: "javascript-typescript-2026-09-05",
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
