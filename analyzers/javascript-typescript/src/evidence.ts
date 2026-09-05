import { createHash } from "node:crypto";
import { dimensionKeys, type DimensionKey } from "./scorecard-contract.js";

export const hash = (text: string) => createHash("sha256").update(text).digest("hex");
export interface Finding {
  category: string; ruleId: string; fingerprint: string;
  severity: "info" | "warning" | "error"; confidence: "high" | "medium" | "low";
  message: string; file?: string; line?: number; project?: string; type?: string; member?: string;
  package?: string; observations: Record<string, unknown>;
}
export interface Dimension {
  scope?: { id: string; coverage: "partial" | "unsupported"; includes: string[]; excludes: string[] };
  status: "scored" | "skipped" | "failed"; score?: number; basis: string; findings: Finding[];
  [key: string]: unknown;
}
export interface Evidence {
  schemaVersion: number; generatedAtUtc: string;
  tool: { name: string; version: string; ecosystem: string };
  subject: { root: string; entryPoint: string; name?: string; variant?: string };
  filters: { totalUnits: number; analyzedUnits: number; skipped: { name: string; reason: string }[] };
  population: { types: number; members: number };
  dimensions: Record<DimensionKey, Dimension>;
  analysis: {
    status: "complete" | "incomplete"; ruleset: string; calibration: string;
    configurationFingerprint: string; diagnostics: { kind: string; message: string; project?: string }[];
    suppressions: { file: string; line: number; categories: string[]; reason?: string; status: string }[];
  };
}
export function skippedDimensions(): Record<DimensionKey, Dimension> {
  return Object.fromEntries(dimensionKeys.map(key => [key, {
    status: "skipped", basis: "Not implemented by this analyzer version.", findings: [],
    scope: { id: `javascript-typescript/${key}/unsupported`, coverage: "unsupported", includes: [], excludes: [key] }
  }])) as unknown as Record<DimensionKey, Dimension>;
}
export function scored(score: number, basis: string, findings: Finding[], observations: Record<string, unknown>): Dimension {
  return { status: "scored", score, basis, findings: findings.sort((a, b) => a.fingerprint.localeCompare(b.fingerprint, "en")),
    scoring: { algorithm: "dimension-policy", finalScore: score, aggregateScoreLoss: 10 - score,
      contributionMode: "aggregate; findings are not independent deductions", observations } };
}
