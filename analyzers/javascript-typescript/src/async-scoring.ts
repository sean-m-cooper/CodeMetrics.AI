import { scored, type Dimension, type Finding } from "./evidence.js";
import { firstMatch } from "./scoring-decision.js";
import type { AsyncOwner } from "./async-population.js";

export function scoreAsyncUsage(findings: Finding[], population: AsyncOwner[]): Dimension {
  const scope: Dimension["scope"] = { id: "javascript-typescript/performanceAsync/owner-population-v3", coverage: "partial",
    includes: ["react-hook-placement", "react-effect-callbacks", "standard-promise-executors", "array-async-callback-review", "eligible-owner-population"],
    excludes: ["general-promise-flow", "exhaustive-hook-rules", "concurrency-safety", "runtime-performance"] };
  const owners = [...new Map(population.map(owner => [owner.id, owner])).values()].sort((a, b) => a.id.localeCompare(b.id, "en"));
  const actionable = findings.filter(finding => finding.severity !== "info");
  if (!owners.length) return { status: actionable.length ? "failed" : "skipped", scope, findings,
    basis: actionable.length ? "Actionable findings have no eligible owners." : "No eligible async/React owners; usage is unmeasured." };
  const eligible = new Set(owners.map(owner => owner.id));
  const affected = new Set(actionable.map(finding => String(finding.observations.asyncOwnerId)));
  if ([...affected].some(id => !eligible.has(id))) return { status: "failed", scope, findings,
    basis: "An actionable finding has no eligible owner; no population score can be established." };
  const n = owners.length, b = affected.size;
  const observations = { eligibleOwners: n, affectedOwners: b, affectedPercent: 100 * b / n,
    actionableFindings: actionable.length, advisoryFindings: findings.length - actionable.length,
    countingUnit: "distinct-function-or-module-owner", owners: owners.map(owner => ({ ...owner, affected: affected.has(owner.id) })) };
  const categories = [...new Set(actionable.map(finding => finding.category))];
  const rules = [
    { id: "underOne", condition: "affectedOwners = 0 or rate < 1%", matched: b === 0 || 100 * b < n, score: 10 },
    { id: "underTwo", condition: "rate < 2%", matched: 100 * b < 2 * n, score: 9 },
    { id: "underFive", condition: "rate < 5%", matched: 100 * b < 5 * n, score: 8 },
    { id: "throughTen", condition: "rate <= 10%", matched: 100 * b <= 10 * n, score: 6 },
    { id: "throughFifteen", condition: "rate <= 15%", matched: 100 * b <= 15 * n, score: 4 },
    { id: "throughTwenty", condition: "rate <= 20%", matched: 100 * b <= 20 * n, score: 2 },
    { id: "overTwenty", condition: "otherwise", matched: true, score: 0 }
  ];
  const decision = firstMatch("javascript-typescript/performanceAsync/owner-rate-v3", observations,
    rules.map(rule => ({ ...rule, categories })));
  return { ...scored(decision, "Partial Async/Blocking Usage: affected eligible owners divided by all eligible owners. Each owner counts once; review leads add no penalty. Product-policy ladder, not runtime performance.",
    findings, observations, finding => finding.severity !== "info"), scope };
}
