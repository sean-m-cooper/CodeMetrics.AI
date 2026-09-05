#!/usr/bin/env node
import { parseArgs } from "node:util";
import { compare, gate, readEvidence, sarif } from "./evidence-tools.js";
import { distinctOutputs, write } from "./io.js";
try {
  const { values } = parseArgs({ options: { input: { type: "string" }, baseline: { type: "string" }, output: { type: "string" },
    sarif: { type: "string" }, "allow-incompatible": { type: "boolean" }, "fail-on-new": { type: "string" },
    "max-score-drop": { type: "string" }, help: { type: "boolean", short: "h" } } });
  if (values.help) console.log([
    "codemetrics-evidence — validate, compare and export schema-v3 evidence from any ecosystem",
    "--input <evidence.json> [--baseline <baseline.json> --output <comparison.json>]",
    "--sarif <results.sarif> --fail-on-new <info|warning|error> --max-score-drop <number>",
    "--allow-incompatible permits exploratory comparison with null score deltas; gates remain disabled.",
    "Exit codes: 0 success, 1 quality gate, 2 invalid/incompatible/incomplete evidence."
  ].join("\n"));
  else {
    if (!values.input) throw new Error("--input is required.");
    distinctOutputs([values.input, values.baseline], [values.output, values.sarif]);
    const current = readEvidence(values.input);
    if (!values.baseline && (values.output || values["fail-on-new"] || values["max-score-drop"])) throw new Error("Comparison and quality gates require --baseline.");
    let failed = false;
    if (values.baseline) {
      const result = compare(current, readEvidence(values.baseline), values["allow-incompatible"]);
      if (values["fail-on-new"] !== undefined || values["max-score-drop"] !== undefined)
        failed = gate(result, values["fail-on-new"], values["max-score-drop"] === undefined ? undefined : Number(values["max-score-drop"]));
      if (values.output) write(values.output, result);
      else console.log(JSON.stringify(result, null, 2));
    }
    if (values.sarif) write(values.sarif, sarif(current));
    process.exitCode = current.analysis.status !== "complete" || Object.values(current.dimensions).some(d => d.status === "failed") ? 2 : failed ? 1 : 0;
    if (!values.baseline) console.log("Evidence is valid.");
  }
} catch (error) { console.error(error instanceof Error ? error.message : error); process.exitCode = 2; }
