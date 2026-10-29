import { add, decimal, fraction, interpolate, multiply, numeric, roundScore, subtract } from './arithmetic.mjs';
import { validateSourceFacts } from './contract.mjs';

export const complexityAnchors = Object.freeze([[3, 10], [5, 8], [10, 6], [20, 4], [40, 0]].map(Object.freeze));

/** Shared policy mechanics; selecting/calibrating a language policy remains explicit. */
export function aggregatePopulation(measurements, anchors, higherIsWorse, weakCount) {
  if (!measurements.length) return null;
  if (!Number.isSafeInteger(weakCount) || weakCount < 1 || weakCount > measurements.length) throw new Error('Invalid weakest population size.');
  if (!Array.isArray(anchors) || anchors.length < 2 || anchors.some(([x, y], i) => !Number.isSafeInteger(x) ||
      !Number.isSafeInteger(y) || y < 0 || y > 10 || (i > 0 && x <= anchors[i - 1][0]))) throw new Error('Invalid score anchors.');
  const ids = new Set();
  for (const item of measurements) {
    if (!item.id || ids.has(item.id) || !Number.isFinite(item.value)) throw new Error('Unique IDs and finite measurements required.');
    ids.add(item.id);
  }
  const ordered = measurements.map(item => ({ item, score: interpolate(item.value, anchors) }))
    .sort((a, b) => (higherIsWorse ? b.item.value - a.item.value : a.item.value - b.item.value) ||
      (a.item.id < b.item.id ? -1 : a.item.id > b.item.id ? 1 : 0));
  const restCount = ordered.length - weakCount;
  const weakWeight = restCount ? fraction(2n, 5n * BigInt(weakCount)) : fraction(1n, BigInt(weakCount));
  const restWeight = restCount ? fraction(3n, 5n * BigInt(restCount)) : decimal(0);
  const losses = { weakest: decimal(0), remainder: decimal(0) };
  const contributions = ordered.map(({ item, score }, index) => {
    const group = index < weakCount ? 'weakest' : 'remainder';
    const weight = index < weakCount ? weakWeight : restWeight;
    const weightedLoss = multiply(subtract(decimal(10), score), weight);
    losses[group] = add(losses[group], weightedLoss);
    return { id: item.id, group, individualScore: numeric(score), weightNumerator: Number(weight.numerator),
      weightDenominator: Number(weight.denominator), weightedScoreLoss: numeric(weightedLoss) };
  });
  const total = subtract(decimal(10), add(losses.weakest, losses.remainder));
  return { weakCount, restCount, contributions, losses: { weakest: numeric(losses.weakest), remainder: numeric(losses.remainder) },
    unroundedScore: numeric(total), score: roundScore(total) };
}

export function complexityPopulation(facts) {
  validateSourceFacts(facts);
  if (facts.status !== 'complete' || facts.capabilities.functionComplexity !== 'available') return null;
  return aggregatePopulation(facts.functions.map(item => ({ id: item.id, value: item.complexity.value })), complexityAnchors, true, 1);
}
