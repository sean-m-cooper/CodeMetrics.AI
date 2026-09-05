#!/usr/bin/env node
import fs from "node:fs";
import { parseArgs } from "node:util";
import { analyze } from "./analyzer.js";
import { defaultEvidencePath, defaultMetricsPath } from "./scorecard-contract.js";
import { compare, gate, readEvidence, sarif, validateEvidence } from "./evidence-tools.js";
import { distinctOutputs, write } from "./io.js";
try {
  const { values } = parseArgs({ options: {
    project: { type: "string" }, tsconfig: { type: "string" }, "run-id": { type: "string" }, "audit-id": { type: "string" }, output: { type: "string", default: defaultMetricsPath },
    "scorecard-output": { type: "string", default: defaultEvidencePath }, baseline: { type: "string" },
    "comparison-output": { type: "string" }, sarif: { type: "string" }, "fail-on-new": { type: "string" },
    "max-score-drop": { type: "string" }, help: { type: "boolean", short: "h" }, version: { type: "boolean" }
  } });
  const version: string = JSON.parse(fs.readFileSync(new URL("../package.json", import.meta.url), "utf8")).version;
  if (values.help) console.log([
    "codemetrics-ai — JavaScript, TypeScript and React source analysis",
    "--project <package.json> --tsconfig <tsconfig.json>",
    "--run-id <uuid> --audit-id <uuid> (generated when omitted)",
    "--output <metrics.csv> --scorecard-output <evidence.json>",
    "--baseline <evidence.json> --comparison-output <comparison.json>",
    "--fail-on-new <info|warning|error> --max-score-drop <number> --sarif <results.sarif>",
    "Scores are uncalibrated across ecosystems. Unsupported dimensions are skipped.",
    "Exit codes: 0 success, 1 quality gate, 2 invalid input/incomplete analysis."
  ].join("\n"));
  else if (values.version) console.log(version);
  else {
    if ((values["fail-on-new"] || values["max-score-drop"] || values["comparison-output"]) && !values.baseline) throw new Error("Comparison and quality gates require --baseline.");
    distinctOutputs([values.project ?? "package.json", values.tsconfig, values.baseline], [values.output, values["scorecard-output"], values["comparison-output"], values.sarif]);
    const result = analyze({ ...values, runId: values["run-id"], auditId: values["audit-id"] }, version); validateEvidence(result.evidence);
    distinctOutputs(result.inputs, [values.output, values["scorecard-output"], values["comparison-output"], values.sarif]);
    const comparison = values.baseline ? compare(result.evidence, readEvidence(values.baseline)) : undefined;
    const failed = comparison ? gate(comparison, values["fail-on-new"], values["max-score-drop"] === undefined ? undefined : Number(values["max-score-drop"])) : false;
    write(values.output!, result.csv); write(values["scorecard-output"]!, result.evidence);
    if (values.sarif) write(values.sarif, sarif(result.evidence));
    if (values["comparison-output"]) write(values["comparison-output"], comparison);
    console.log("Analyzed " + result.evidence.filters.analyzedUnits + " files; " + result.metrics.length + " members. Evidence: " + values["scorecard-output"]);
    process.exitCode = result.evidence.analysis.status === "incomplete" ? 2 : failed ? 1 : 0;
  }
} catch (error) { console.error(error instanceof Error ? error.message : error); process.exitCode = 2; }
