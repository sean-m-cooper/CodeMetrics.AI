function straight(value: number) {
  return value;
}
function branches(value: number) {
  if (value > 0) return value;
  if (value === 0) return 1;
  if (value === -1) return 2;
  if (value === -2) return 3;
  return -value;
}
function outer(value: number) {
  function inner(item: number) {
    return item ? item : 0;
  }
  return inner(value);
}
