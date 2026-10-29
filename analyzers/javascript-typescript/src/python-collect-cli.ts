#!/usr/bin/env node
import path from "node:path";
import { fileURLToPath } from "node:url";
import { spawnSync } from "node:child_process";

// Explicit opt-in test execution; never called by the scorecard analyzer.
try {
  const args = process.argv.slice(2);
  const separator = args.indexOf("--");
  const options = separator < 0 ? args : args.slice(0, separator);
  const index = options.indexOf("--python");
  const supplied = index >= 0 ? options[index + 1] : undefined;
  if (supplied && !path.isAbsolute(supplied)) throw new Error("--python must be an absolute Python 3.12 interpreter path.");
  if (!supplied && !options.includes("--help") && !options.includes("-h"))
    throw new Error("Explicit collection executes tests. Supply --python with an environment containing coverage.py and the project's test dependencies.");
  const script = fileURLToPath(new URL("./python/coverage_collect.py", import.meta.url));
  const bootstrap = "import runpy,sys; from pathlib import Path; script=sys.argv[1]; sys.path.insert(0,str(Path(script).parent)); sys.argv=sys.argv[1:]; runpy.run_path(script,run_name='__main__')";
  const command = supplied ?? (process.platform === "win32" ? "py" : "python3");
  const result = spawnSync(command, [...(!supplied && process.platform === "win32" ? ["-3.12"] : []),
    "-I", "-S", "-B", "-c", bootstrap, script, ...args], { stdio: "inherit", windowsHide: true });
  if (result.error) throw result.error;
  process.exitCode = result.status === 0 ? 0 : 2;
} catch (error) { console.error(error instanceof Error ? error.message : error); process.exitCode = 2; }
