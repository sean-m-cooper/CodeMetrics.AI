import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { randomUUID } from "node:crypto";
import { spawnSync } from "node:child_process";
import { expect, it } from "vitest";
import { analyze } from "../src/analyzer.js";
import { pythonEvidence } from "../dist/python-evidence.js";
import { compare, gate, sarif, validateEvidence } from "../dist/evidence-tools.js";
import { inspectEvidence } from "../dist/evidence-reader.js";

it("reads legacy identities without aliasing cross-brand baselines or weakening gates", () => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), "brand-migration-"));
  try {
    fs.writeFileSync(path.join(root, "package.json"), '{"name":"migration"}');
    fs.writeFileSync(path.join(root, "app.ts"), "export async function work() { return 1; }");
    const current = analyze({ project: path.join(root, "package.json") }, "0.6.0-preview.3").evidence;
    expect(current.tool.name).toBe("CodeTestament.JavaScript");
    const legacy = structuredClone(current);
    legacy.tool.name = "codemetrics-ai";
    legacy.tool.version = "0.6.0-preview.2";
    validateEvidence(legacy);
    expect(() => compare(current, legacy)).toThrow("tool.name");
    const report = compare(current, legacy, true);
    expect(report.compatible).toBe(false);
    expect(() => gate(report, "warning", 0)).toThrow();
    expect(sarif(current).runs[0].tool.driver.name).toBe("CodeTestament.JavaScript");
    const file = path.join(root, "evidence.json");
    fs.writeFileSync(file, JSON.stringify(current));
    expect(inspectEvidence(file, { name: current.tool.name, version: current.tool.version }).usable).toBe(true);
    expect(() => inspectEvidence(file, { name: legacy.tool.name })).toThrow("tool.name");
    const run = (name: string) => spawnSync(process.execPath, ["dist/evidence-cli.js", "--input", file, "--expected-name", name], { encoding: "utf8" });
    expect(run(current.tool.name).status).toBe(0);
    expect(run(legacy.tool.name).status).toBe(2);
    fs.writeFileSync(file, JSON.stringify(legacy));
    expect(inspectEvidence(file, { name: legacy.tool.name }).evidence).toEqual(legacy);
  } finally { fs.rmSync(root, { recursive: true, force: true }); }
});

it("accepts the current Python profile under either exact brand and rejects invented combinations", () => {
  const evidence = pythonEvidence(null, { name: "migration", root: "/migration", entryPoint: ".", variant: "source" },
    { runId: randomUUID(), auditId: randomUUID() }, "Source unavailable");
  expect(evidence.tool.name).toBe("CodeTestament.Python");
  validateEvidence(evidence);
  const legacy = structuredClone(evidence);
  legacy.tool.name = "CodeMetrics.AI.Python";
  validateEvidence(legacy);
  for (const name of ["CodeTestament.Python.Fake", "CodeMetrics.AI.Python.Fake", "CodeTestament.JavaScript"]) {
    const bad = structuredClone(evidence); bad.tool.name = name;
    expect(() => validateEvidence(bad)).toThrow("identity/profile");
  }
  const historical = structuredClone(evidence);
  historical.tool.version = "0.14.0";
  historical.analysis.ruleset = "python-2026-10-24-async-adapters-review-v1";
  expect(() => validateEvidence(historical)).toThrow("identity/profile");
});
