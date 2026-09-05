import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { spawnSync } from "node:child_process";
import { expect, it } from "vitest";
import { inspectEvidence } from "../dist/evidence-reader.js";
import { analyze } from "../src/analyzer.js";
import { compare, sarif } from "../dist/evidence-tools.js";
import { randomUUID } from "node:crypto";

it("inspects canonical v2 without inventing v3 provenance and refuses v2 gates", () => {
  const file = path.resolve("dist/contract-examples/dotnet-evidence.json");
  const result = inspectEvidence(file);
  expect(result.compatibility).toEqual({ mode: "legacy", comparisonAvailable: false, analysisStatus: "unknown", scopeAvailable: false });
  expect(result.evidence).not.toHaveProperty("analysis");
  expect(() => inspectEvidence(file, { ecosystem: "javascript-typescript" })).toThrow("Provenance mismatch");
  const cli = spawnSync(process.execPath, ["dist/evidence-cli.js", "--input", file, "--baseline", file, "--max-score-drop", "0"], { encoding: "utf8" });
  expect(cli.status).toBe(2); expect(cli.stderr).toContain("compatibility-only");
});

it("validates native scope, explicit provenance, unsupported schemas and incomplete evidence", () => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), "reader-"));
  const alias = root + "-alias";
  try {
    fs.writeFileSync(path.join(root, "package.json"), '{"name":"reader"}');
    fs.writeFileSync(path.join(root, "app.tsx"), "import {useEffect} from 'react'; export function App(){useEffect(async()=>{},[]);return null;}");
    const evidence = analyze({ project: path.join(root, "package.json") }, "0.2.0").evidence;
    const file = path.join(root, "evidence.json");
    fs.writeFileSync(file, JSON.stringify(evidence));
    expect(inspectEvidence(file, { version: "0.2.0", entryPoint: path.join(root, "package.json"), root, variant: "source" }).usable).toBe(true);
    expect(inspectEvidence(file, { runId: evidence.analysis.runId, auditId: evidence.analysis.auditId }).usable).toBe(true);
    expect(() => inspectEvidence(file, { runId: randomUUID() })).toThrow("analysis.runId");
    expect(() => inspectEvidence(file, { auditId: randomUUID() })).toThrow("analysis.auditId");
    const rerun = analyze({ project: path.join(root, "package.json") }, "0.2.0").evidence;
    expect(rerun.analysis.runId).not.toBe(evidence.analysis.runId);
    expect(compare(rerun, evidence).compatible).toBe(true);
    expect(compare(rerun, evidence).unchanged).toHaveLength(1);
    expect(compare(rerun, evidence).currentRun.runId).toBe(rerun.analysis.runId);
    expect(sarif(evidence).runs[0].properties.runId).toBe(evidence.analysis.runId);
    const historical = structuredClone(evidence); delete historical.analysis.runId; delete historical.analysis.auditId;
    fs.writeFileSync(file, JSON.stringify(historical));
    expect(inspectEvidence(file).usable).toBe(true);
    expect(() => inspectEvidence(file, { runId: evidence.analysis.runId })).toThrow("analysis.runId");
    fs.writeFileSync(file, JSON.stringify(evidence));
    expect(() => analyze({ project: path.join(root, "package.json"), runId: "invalid" }, "0.2.0")).toThrow("UUID");
    fs.symlinkSync(root, alias, process.platform === "win32" ? "junction" : "dir");
    expect(inspectEvidence(file, { root: alias, entryPoint: path.join(alias, "package.json") }).usable).toBe(true);
    expect(evidence.dimensions.performanceAsync.scope?.excludes).toContain("general-async");
    for (const expected of [{ version: "9" }, { entryPoint: path.join(root, "wrong.json") }, { variant: "Release" }, { root: path.dirname(root) }])
      expect(() => inspectEvidence(file, expected)).toThrow("Provenance mismatch");
    const changed = structuredClone(evidence);
    changed.dimensions.performanceAsync.scope!.excludes.reverse();
    expect(compare(changed, evidence).compatible).toBe(true);
    changed.dimensions.performanceAsync.scope!.id = "changed";
    expect(() => compare(changed, evidence)).toThrow("scope differs");
    evidence.analysis.status = "incomplete"; fs.writeFileSync(file, JSON.stringify(evidence));
    expect(inspectEvidence(file).usable).toBe(false);
    fs.writeFileSync(file, '{"schemaVersion":99}');
    expect(() => inspectEvidence(file)).toThrow("Unsupported evidence schema");
  } finally { fs.rmSync(alias, { recursive: true, force: true }); fs.rmSync(root, { recursive: true, force: true }); }
});
