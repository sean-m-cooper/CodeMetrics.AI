#!/usr/bin/env node
import { parseArgs } from "node:util";
import { compare, gate, readEvidence, sarif } from "./evidence-tools.js";
import { distinctOutputs, write } from "./io.js";
import { inspectEvidence } from "./evidence-reader.js";
try {
  const { values } = parseArgs({ options: { input: { type: "string" }, baseline: { type: "string" }, output: { type: "string" },
    sarif: { type: "string" }, "allow-incompatible": { type: "boolean" }, "fail-on-new": { type: "string" },
    "max-score-drop": { type: "string" }, "inspect-output": { type: "string" },
    "expected-ecosystem": { type: "string" }, "expected-version": { type: "string" }, "expected-run-id": { type: "string" }, "expected-audit-id": { type: "string" },
    "expected-entry-point": { type: "string" }, "expected-variant": { type: "string" }, "expected-root": { type: "string" },
    help: { type: "boolean", short: "h" } } });
  if (values.help) console.log([
    "codemetrics-evidence — validate v2/v3, compare and export v3 evidence from any ecosystem",
    "--input <evidence.json> [--baseline <baseline.json> --output <comparison.json>]",
    "--sarif <results.sarif> --fail-on-new <info|warning|error> --max-score-drop <number>",
    "--inspect-output <inspection.json> reads v2/v3 without upgrading historical evidence.",
    "--expected-ecosystem <id> --expected-version <version> --expected-entry-point <path> --expected-variant <variant> --expected-root <path>",
    "--expected-run-id <uuid> --expected-audit-id <uuid> reject findings from another invocation, including files with missing IDs.",
    "--allow-incompatible permits exploratory comparison with null score deltas; gates remain disabled.",
    "Exit codes: 0 success, 1 quality gate, 2 invalid/incompatible/incomplete evidence."
  ].join("\n"));
  else {
    if (!values.input) throw new Error("--input is required.");
    distinctOutputs([values.input, values.baseline], [values.output, values.sarif, values["inspect-output"]]);
    const inspection = inspectEvidence(values.input, { ecosystem: values["expected-ecosystem"], version: values["expected-version"],
      entryPoint: values["expected-entry-point"], variant: values["expected-variant"], root: values["expected-root"],
      runId: values["expected-run-id"], auditId: values["expected-audit-id"] });
    if (values["inspect-output"]) write(values["inspect-output"], inspection);
    if (inspection.evidence.schemaVersion === 2) {
      if (values.baseline || values.output || values.sarif || values["fail-on-new"] !== undefined || values["max-score-drop"] !== undefined)
        throw new Error("V2 is compatibility-only: comparison, gates and SARIF require v3. Re-run the pinned analyzer.");
      console.log("V2 evidence is valid; completeness, scope and comparison provenance are unknown.");
      process.exitCode = inspection.usable ? 0 : 2;
    } else {
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
  }
} catch (error) { console.error(error instanceof Error ? error.message : error); process.exitCode = 2; }
