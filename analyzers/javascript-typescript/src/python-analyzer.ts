import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { spawnSync } from "node:child_process";
import { validateSourceFacts, type SourceFacts } from "#source-facts/contract.mjs";
import { invocationIds } from "./evidence.js";
import { pythonEvidence } from "./python-evidence.js";
import { validateDuplication, type PythonDuplication } from "./python-duplication.js";
import { validateCoverage, type PythonCoverage } from "./python-coverage.js";

import { validateOperations, type PythonOperations } from "./python-error-operations.js";
import { validateTransfers, type PythonTransfers } from "./python-error-transfers.js";
import { validateErrorContext, type PythonErrorContext } from "./python-error-context.js";
import { validateErrorHandling, type PythonErrorHandling } from "./python-error-handling.js";
import { validateDecomposition, type PythonDecomposition } from "./python-decomposition.js";

export interface PythonOptions {
  project: string; python?: string; runId?: string; auditId?: string;
  include?: string[]; exclude?: string[]; testPath?: string[]; productionPath?: string[];
  includeTests?: boolean; includeGenerated?: boolean; includeSupport?: boolean;
  coverageJson?: string; collectionManifest?: string;
}

export function pythonTarget(project: string) {
  const entryPoint = path.resolve(project);
  if (fs.lstatSync(entryPoint).isSymbolicLink()) throw new Error("Linked Python entry points are not supported.");
  const directory = fs.statSync(entryPoint).isDirectory();
  const manifest = path.basename(entryPoint) === "pyproject.toml";
  if (!directory && !manifest && !/\.pyi?$/.test(entryPoint)) throw new Error("Python entry point must be a directory, pyproject.toml, .py, or .pyi file.");
  const root = directory ? entryPoint : path.dirname(entryPoint);
  return { entryPoint, root, target: manifest ? root : entryPoint };
}

export function analyzePython(options: PythonOptions) {
  const ids = invocationIds(options.runId, options.auditId);
  const { target, root, entryPoint } = pythonTarget(options.project);
  const script = fileURLToPath(new URL("./python/analysis.py", import.meta.url));
  // Isolated stdlib interpreter; only the packaged analyzer directory is added to sys.path.
  const bootstrap = "import runpy,sys; from pathlib import Path; script=sys.argv[1]; sys.path.insert(0,str(Path(script).parent)); sys.argv=sys.argv[1:]; runpy.run_path(script,run_name='__main__')";
  const command = options.python ?? (process.platform === "win32" ? "py" : "python3");
  if (options.python && !path.isAbsolute(options.python)) throw new Error("--python must be an absolute interpreter path.");
  const args = [...(!options.python && process.platform === "win32" ? ["-3.12"] : []), "-I", "-S", "-B", "-c", bootstrap, script, target];
  for (const [flag, items] of [["include", options.include], ["exclude", options.exclude], ["test-path", options.testPath], ["production-path", options.productionPath]] as const)
    for (const item of items ?? []) args.push(`--${flag}`, item);
  for (const [flag, enabled] of [["include-tests", options.includeTests], ["include-generated", options.includeGenerated], ["include-support", options.includeSupport]] as const)
    if (enabled) args.push(`--${flag}`);
  let operations: PythonOperations | null = null;
  let transfers: PythonTransfers | null = null;
  let context: PythonErrorContext | null = null;
  let errorHandling: PythonErrorHandling | null = null;
  let facts: SourceFacts | null = null;
  let duplication: PythonDuplication | null = null;
  let decomposition: PythonDecomposition | null = null;
  let coverage: PythonCoverage | null = null;
  for (const [flag, value] of [["coverage-json", options.coverageJson], ["collection-manifest", options.collectionManifest]] as const)
    if (value) args.push(`--${flag}`, path.resolve(value));
  const subject = { root, entryPoint, name: path.basename(root), variant: "source" };
  args.push("--run-id", ids.runId, "--audit-id", ids.auditId);
  let error: string | undefined;
  const result = spawnSync(command, args, { cwd: path.dirname(script), encoding: "utf8", windowsHide: true, timeout: 1_800_000, maxBuffer: 128 * 1024 * 1024 });
  try {
    if (result.error) throw new Error(`Python 3.12 could not run: ${result.error.message}`);
    if (result.status !== 0 && result.status !== 2) throw new Error(`Python exited ${result.status}: ${result.stderr.slice(0, 2000)}`);
    if (!result.stdout.trim()) throw new Error(`Python emitted no facts (exit ${result.status}): ${result.stderr.slice(0, 2000)}`);
    const parsed = JSON.parse(result.stdout);
    validateSourceFacts(parsed.sourceFacts);
    validateDuplication(parsed.duplication, parsed.sourceFacts, subject, ids);
    validateCoverage(parsed.coverageReview, parsed.sourceFacts, subject, ids);
    validateDecomposition(parsed.decomposition, parsed.sourceFacts, subject, ids);
    validateErrorHandling(parsed.errorHandlingReview, parsed.sourceFacts, subject, ids);
    validateErrorContext(parsed.errorHandlingContext, parsed.errorHandlingReview);
    validateTransfers(parsed.errorHandlingTransfers, parsed.errorHandlingContext);
    validateOperations(parsed.errorHandlingOperations, parsed.errorHandlingTransfers);
    operations = parsed.errorHandlingOperations;
    transfers = parsed.errorHandlingTransfers;
    context = parsed.errorHandlingContext;
    errorHandling = parsed.errorHandlingReview;
    decomposition = parsed.decomposition;
    facts = parsed.sourceFacts;
    duplication = parsed.duplication;
    coverage = parsed.coverageReview;
    if ((result.status === 0) !== (facts!.status === "complete" && duplication!.status === "complete" && coverage!.status === "complete" && decomposition!.status === "complete" && errorHandling!.status === "complete" && context!.status === "complete" && transfers!.status === "complete" && operations!.status === "complete")) throw new Error("Python exit status and facts disagree.");
  } catch (failure) { operations = null; transfers = null; context = null; errorHandling = null; facts = null; decomposition = null; duplication = null; coverage = null; error = failure instanceof Error ? failure.message : String(failure); }
  return pythonEvidence(facts, subject, ids, error, duplication, coverage, decomposition, errorHandling, context, transfers, operations);
}
