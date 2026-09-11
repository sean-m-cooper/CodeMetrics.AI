import { createHash, randomUUID } from "node:crypto";
import { dimensionKeys, type DimensionKey } from "./scorecard-contract.js";
import { attachFindings, type ScoringDecision } from "./scoring-decision.js";

export const hash = (text: string) => createHash("sha256").update(text).digest("hex");
export function invocationIds(runId = randomUUID() as string, auditId = runId) {
  for (const [name, value] of Object.entries({ runId, auditId }))
    if (!/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value)) throw new Error(`${name} must be a UUID.`);
  return { runId: runId.toLowerCase(), auditId: auditId.toLowerCase() };
}
export interface Finding {
  category: string; ruleId: string; fingerprint: string;
  severity: "info" | "warning" | "error"; confidence: "high" | "medium" | "low";
  message: string; file?: string; line?: number; project?: string; type?: string; member?: string;
  package?: string; observations: Record<string, unknown>;
}
export interface Dimension {
  scoringDecision?: ScoringDecision;
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
    runId?: string; auditId?: string;
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
export function scored(decision: ScoringDecision, basis: string, findings: Finding[], observations: Record<string, unknown>, eligible?: (finding: Finding) => boolean): Dimension {
  const score = decision.finalScore;
  attachFindings(decision, findings, eligible);
  return { status: "scored", score, basis, scoringDecision: decision, findings: findings.sort((a, b) => a.fingerprint.localeCompare(b.fingerprint, "en")),
    scoring: { algorithm: "dimension-policy", finalScore: score, aggregateScoreLoss: 10 - score,
      contributionMode: "aggregate; findings are not independent deductions", observations } };
}
