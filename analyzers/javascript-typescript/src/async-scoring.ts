import { scored, type Dimension, type Finding } from "./evidence.js";
import { firstMatch } from "./scoring-decision.js";

export function scoreAsyncUsage(findings: Finding[], reactSupported: boolean, asyncSupported: boolean): Dimension {
  const scope: Dimension["scope"] = {
    id: "javascript-typescript/performanceAsync/v2", coverage: "partial",
    includes: ["react-hook-placement", "react-effect-callbacks", "standard-promise-executors", "array-async-callback-review"],
    excludes: ["general-promise-flow", "exhaustive-hook-rules", "concurrency-safety", "runtime-performance"]
  };
  if (!reactSupported && !asyncSupported) return { status: "skipped", scope, findings,
    basis: "No executable React functions or recognized async usage were observed; async usage is unmeasured." };
  const actionable = findings.filter(finding => finding.severity !== "info");
  const observations = { actionableFindings: actionable.length, advisoryFindings: findings.length - actionable.length,
    reactSupported, asyncSupported, scope: "react-hooks-and-bounded-promise-usage" };
  const decision = firstMatch("javascript-typescript/performanceAsync/usage-v2", observations, [
    { id: "actionableUsage", condition: "actionableFindings > 0", matched: actionable.length > 0, score: 6,
      categories: [...new Set(actionable.map(finding => finding.category))] },
    { id: "noActionableUsage", condition: "otherwise", matched: true, score: 10 }
  ]);
  return { ...scored(decision,
    "Partial Async/Blocking Usage: React hook/effect checks and bounded standard-Promise/Array callback checks. Review leads are unscored; runtime speed and general promise flow are unmeasured.",
    findings, observations, finding => finding.severity !== "info"), scope };
}
