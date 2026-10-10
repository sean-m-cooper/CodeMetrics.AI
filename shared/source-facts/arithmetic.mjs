// Decimal inputs retain their measurement precision; subsequent arithmetic is exact.
export function fraction(numerator, denominator = 1n) {
  if (denominator <= 0n) throw new Error('Positive denominator required.');
  let a = numerator < 0n ? -numerator : numerator, b = denominator;
  while (b) { const remainder = a % b; a = b; b = remainder; }
  return { numerator: numerator / a, denominator: denominator / a };
}
export function decimal(value) {
  if (!Number.isFinite(value)) throw new Error('Finite score measurement required.');
  const [mantissa, exponent = '0'] = String(value).toLowerCase().split('e');
  const places = (mantissa.split('.')[1] ?? '').length - Number(exponent);
  const numerator = BigInt(mantissa.replace('.', ''));
  return places >= 0 ? fraction(numerator, 10n ** BigInt(places)) : fraction(numerator * 10n ** BigInt(-places));
}
export const add = (a, b) => fraction(a.numerator * b.denominator + b.numerator * a.denominator, a.denominator * b.denominator);
export const multiply = (a, b) => fraction(a.numerator * b.numerator, a.denominator * b.denominator);
export const subtract = (a, b) => add(a, { ...b, numerator: -b.numerator });
export const numeric = a => Number(a.numerator) / Number(a.denominator);
export function roundScore(a) {
  if (a.numerator < 0n) throw new Error('Score must be nonnegative.');
  return Number((a.numerator * 20n + a.denominator) / (2n * a.denominator)) / 10;
}
export function interpolate(value, anchors) {
  if (!Number.isFinite(value)) throw new Error('Finite score measurement required.');
  if (value <= anchors[0][0]) return decimal(anchors[0][1]);
  for (let index = 1; index < anchors.length; index++) {
    const [x, y] = anchors[index], [previousX, previousY] = anchors[index - 1];
    if (value <= x) return add(decimal(previousY), multiply(subtract(decimal(value), decimal(previousX)),
      fraction(BigInt(y - previousY), BigInt(x - previousX))));
  }
  return decimal(anchors[anchors.length - 1][1]);
}
