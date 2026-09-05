import fs from "node:fs";
import path from "node:path";
import { Ajv2020 } from "ajv/dist/2020.js";
import addFormats from "ajv-formats";
import type { Evidence, Dimension, Finding } from "./evidence.js";
import type { DimensionKey } from "./scorecard-contract.js";
import { validateEvidence } from "./evidence-tools.js";

export type LegacyEvidence = Omit<Evidence, "analysis" | "dimensions"> & {
  dimensions: Record<DimensionKey, Omit<Dimension, "findings"> & {
    findings: Omit<Finding, "ruleId" | "fingerprint" | "confidence" | "observations">[]
  }>;
};
const ajv = new Ajv2020({ allErrors: true, strict: false });
(addFormats as unknown as (instance: Ajv2020) => void)(ajv);
let legacyValidator: ReturnType<Ajv2020["compile"]> | undefined;

/** Read historical evidence without fabricating v3 metadata or enabling comparisons. */
export function readCompatibleEvidence(file: string): Evidence | LegacyEvidence {
  const value = JSON.parse(fs.readFileSync(file, "utf8"));
  if (value?.schemaVersion === 3) validateEvidence(value);
  else if (value?.schemaVersion === 2) {
    legacyValidator ??= ajv.compile(JSON.parse(fs.readFileSync(new URL("./evidence.schema.v2.json", import.meta.url), "utf8")));
    if (!legacyValidator(value)) throw new Error("Invalid v2 evidence: " + ajv.errorsText(legacyValidator.errors));
    if (value.filters.analyzedUnits > value.filters.totalUnits) throw new Error("Analyzed units exceed total units.");
  } else throw new Error("Unsupported evidence schema; supported versions are 2 and 3.");
  return value;
}

export function inspectEvidence(file: string, expected: { ecosystem?: string; version?: string; entryPoint?: string; variant?: string; root?: string } = {}) {
  const evidence = readCompatibleEvidence(file);
  for (const key of ["ecosystem", "version"] as const)
    if (expected[key] !== undefined && evidence.tool[key] !== expected[key]) throw new Error(`Provenance mismatch: tool.${key}`);
  if (expected.variant !== undefined && evidence.subject.variant !== expected.variant) throw new Error("Provenance mismatch: subject.variant");
  const normalize = (value: string) => {
    const absolute = path.resolve(value);
    // Native resolution handles Windows 8.3 names as well as junctions/symlinks.
    // Historical paths may no longer exist; retain lexical matching for those.
    const resolved = fs.existsSync(absolute) ? fs.realpathSync.native(absolute) : absolute;
    return process.platform === "win32" ? resolved.toLowerCase() : resolved;
  };
  if (expected.root !== undefined && normalize(evidence.subject.root) !== normalize(expected.root)) throw new Error("Provenance mismatch: subject.root");
  if (expected.entryPoint !== undefined && normalize(path.resolve(evidence.subject.root, evidence.subject.entryPoint)) !== normalize(expected.entryPoint))
    throw new Error("Provenance mismatch: subject.entryPoint");
  const legacy = evidence.schemaVersion === 2;
  const failed = Object.values(evidence.dimensions).some(dimension => dimension.status === "failed");
  const incomplete = !legacy && (evidence as Evidence).analysis.status !== "complete";
  return { evidence, compatibility: { mode: legacy ? "legacy" : "native", comparisonAvailable: !legacy && !failed && !incomplete,
    analysisStatus: legacy ? "unknown" : (evidence as Evidence).analysis.status,
    scopeAvailable: !legacy && Object.values(evidence.dimensions).every(dimension => dimension.scope !== undefined) },
    usable: !failed && !incomplete };
}
