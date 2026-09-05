export function outer(x) { const inner = (y) => y ? 1 : 0; return inner(x); }
