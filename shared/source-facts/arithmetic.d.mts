export interface Fraction { numerator: bigint; denominator: bigint; }
export function fraction(numerator: bigint, denominator?: bigint): Fraction;
export function decimal(value: number): Fraction;
export function add(a: Fraction, b: Fraction): Fraction;
export function multiply(a: Fraction, b: Fraction): Fraction;
export function subtract(a: Fraction, b: Fraction): Fraction;
export function numeric(a: Fraction): number;
export function roundScore(a: Fraction): number;
export function interpolate(value: number, anchors: readonly (readonly [number, number])[]): Fraction;
