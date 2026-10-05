// Exact rational arithmetic after converting a measurement's decimal representation.
// Logarithms remain IEEE-754 measurements; interpolation and final .5 ties do not.
export interface Fraction { numerator: bigint; denominator: bigint; }
export function fraction(numerator: bigint, denominator = 1n): Fraction {
  if (denominator <= 0n) throw new Error("Positive denominator required.");
  let a = numerator < 0n ? -numerator : numerator, b = denominator;
  while (b) { const remainder = a % b; a = b; b = remainder; }
  return { numerator: numerator / a, denominator: denominator / a };
}
export function decimal(value: number): Fraction {
  if (!Number.isFinite(value)) throw new Error("Finite score measurement required.");
  const [mantissa, exponent = "0"] = String(value).toLowerCase().split("e");
  const places = (mantissa.split(".")[1] ?? "").length - Number(exponent);
  const numerator = BigInt(mantissa.replace(".", ""));
  return places >= 0 ? fraction(numerator, 10n ** BigInt(places)) : fraction(numerator * 10n ** BigInt(-places));
}
export const add = (a: Fraction, b: Fraction) => fraction(a.numerator * b.denominator + b.numerator * a.denominator, a.denominator * b.denominator);
export const multiply = (a: Fraction, b: Fraction) => fraction(a.numerator * b.numerator, a.denominator * b.denominator);
export const subtract = (a: Fraction, b: Fraction) => add(a, { ...b, numerator: -b.numerator });
export const numeric = (a: Fraction) => Number(a.numerator) / Number(a.denominator);
export function roundScore(a: Fraction): number {
  if (a.numerator < 0n) throw new Error("Score must be nonnegative.");
  return Number((a.numerator * 20n + a.denominator) / (2n * a.denominator)) / 10;
}
export function interpolate(value: number, anchors: readonly (readonly [number, number])[]): Fraction {
  if (!Number.isFinite(value)) throw new Error("Finite score measurement required.");
  if (value <= anchors[0][0]) return decimal(anchors[0][1]);
  for (let index = 1; index < anchors.length; index++) {
    const [x, y] = anchors[index], [previousX, previousY] = anchors[index - 1];
    if (value <= x) return add(decimal(previousY), multiply(subtract(decimal(value), decimal(previousX)),
      fraction(BigInt(y - previousY), BigInt(x - previousX))));
  }
  return decimal(anchors[anchors.length - 1][1]);
}
