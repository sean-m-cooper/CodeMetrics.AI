#!/usr/bin/env node
import fs from "node:fs";
import path from "node:path";
import { parseArgs } from "node:util";
import { analyzePython } from "./python-analyzer.js";
import { pythonVersion } from "./python-evidence.js";
import { validateEvidence } from "./evidence-tools.js";

try {
  const { values } = parseArgs({ options: {
    project: { type: "string" }, python: { type: "string" }, "scorecard-output": { type: "string" },
    "run-id": { type: "string" }, "audit-id": { type: "string" },
    "coverage-json": { type: "string" }, "collection-manifest": { type: "string" },
    include: { type: "string", multiple: true }, exclude: { type: "string", multiple: true },
    "test-path": { type: "string", multiple: true }, "production-path": { type: "string", multiple: true },
    "include-tests": { type: "boolean" }, "include-generated": { type: "boolean" }, "include-support": { type: "boolean" },
    help: { type: "boolean", short: "h" }, version: { type: "boolean" }
  } });
  if (values.help) console.log([
    "codemetrics-python — experimental Python 3.12 complexity, decomposition, duplication, and coverage evidence (Node 20+)",
    "--project <directory|pyproject.toml|file.py> --scorecard-output <new-evidence.json>",
    "--python <absolute Python 3.12 interpreter path> (default: py -3.12 on Windows; python3 elsewhere)",
    "--include <pattern> --exclude <pattern> --test-path <pattern> --production-path <pattern> (repeatable)",
    "--include-tests --include-generated --include-support --run-id <uuid> --audit-id <uuid>",
    "--coverage-json <coverage.py JSON> --collection-manifest <optional collection receipt>",
    "Coverage/CRAP is unscored. Missing coverage stays unavailable; unbound imports stay conditional. Provenance mismatches fail.",
    "Emits evidence v3 with embedded facts and an experimental preview. No dimension scores, overall, CSV, or score gates.",
    "Exact whole-body duplication is review-only, with a five-statement minimum. Zero matches does not prove no duplication.",
    "Target code is parsed, never installed or executed. Exit 0: complete (possibly empty); 2: invalid/incomplete."
  ].join("\n"));
  else if (values.version) console.log(pythonVersion);
  else {
    if (!values.project || !values["scorecard-output"]) throw new Error("--project and --scorecard-output are required.");
    const output = path.resolve(values["scorecard-output"]);
    if (path.extname(output) !== ".json" || fs.existsSync(output)) throw new Error("Evidence output must be a new .json file; existing files are never overwritten.");
    const evidence = analyzePython({ project: values.project, python: values.python, runId: values["run-id"], auditId: values["audit-id"],
      coverageJson: values["coverage-json"], collectionManifest: values["collection-manifest"],
      include: values.include, exclude: values.exclude, testPath: values["test-path"], productionPath: values["production-path"],
      includeTests: values["include-tests"], includeGenerated: values["include-generated"], includeSupport: values["include-support"] });
    validateEvidence(evidence);
    fs.mkdirSync(path.dirname(output), { recursive: true });
    fs.writeFileSync(output, JSON.stringify(evidence, null, 2) + "\n", { flag: "wx" });
    console.log(`Python ${evidence.analysis.status}; ${evidence.population.members} implementation bodies. Evidence: ${output}`);
    process.exitCode = evidence.analysis.status === "complete" ? 0 : 2;
  }
} catch (error) { console.error(error instanceof Error ? error.message : error); process.exitCode = 2; }
