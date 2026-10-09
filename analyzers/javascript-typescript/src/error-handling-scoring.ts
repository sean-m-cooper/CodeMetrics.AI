import { firstMatch } from "./scoring-decision.js";
import { fraction, roundScore } from "./score-arithmetic.js";

export function errorHandlingDecision(total: number, unexplained: number, unknown: number, unresolvedUses: number) {
  if (![total, unexplained, unknown, unresolvedUses].every(value => Number.isSafeInteger(value) && value >= 0) || unexplained + unknown > total)
    throw new Error("Invalid handler population.");
  if (!total || unknown || unresolvedUses) return undefined;
  const populationScore = roundScore(fraction(10n * BigInt(total - unexplained), BigInt(total)));
  const cap = unexplained ? 9 : 10;
  return firstMatch("javascript-typescript/errorHandling/failure-disposition-v1",
    { countingUnit: "distinctHandlerBody", totalHandlers: total, assessedHandlers: total, unexplainedSwallowing: unexplained,
      unknownHandlers: unknown, unresolvedCallbackUses: unresolvedUses, populationScore, cap,
      formula: "min(10 - 10 * unexplainedSwallowing / assessedHandlers, unexplainedSwallowing > 0 ? 9 : 10)", rounding: "half-up-one-decimal" },
    [{ id: "issueCap", condition: "unexplainedSwallowing > 0 and populationScore > 9", matched: unexplained > 0 && populationScore > 9,
      score: 9, categories: ["unexplainedSwallowing"] },
    { id: "population", condition: "otherwise", matched: true, score: populationScore, categories: ["unexplainedSwallowing"] }]);
}
