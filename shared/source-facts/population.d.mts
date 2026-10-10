import type { SourceFacts } from './contract.mjs';
export const complexityAnchors: readonly (readonly [number, number])[];
export interface Contribution {
  id: string; group: 'weakest' | 'remainder'; individualScore: number;
  weightNumerator: number; weightDenominator: number; weightedScoreLoss: number;
}
export interface PopulationResult {
  weakCount: number; restCount: number; contributions: Contribution[];
  losses: Record<string, number>; unroundedScore: number; score: number;
}
export function aggregatePopulation(measurements: {id: string; value: number}[], anchors: readonly (readonly [number, number])[], higherIsWorse: boolean, weakCount: number): PopulationResult | null;
export function complexityPopulation(facts: SourceFacts): PopulationResult | null;
