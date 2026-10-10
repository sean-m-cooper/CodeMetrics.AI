// Validate at the producer/consumer boundary. Unknown data must never imply a pass.
function requireValue(ok, message) {
  if (!ok) throw new Error(`Invalid source facts: ${message}`);
}
const object = value => value !== null && typeof value === 'object' && !Array.isArray(value);
const text = value => typeof value === 'string' && value.trim().length > 0;
const count = value => Number.isSafeInteger(value) && value >= 0;
function keys(value, allowed, label) {
  requireValue(object(value), `${label} must be an object`);
  requireValue(Object.keys(value).every(key => allowed.includes(key)), `${label} has unknown fields`);
}
export function validateSourceFacts(value) {
  keys(value, ['schemaVersion', 'language', 'producer', 'measurementPolicy', 'status', 'capabilities', 'scope', 'diagnostics', 'functions'], 'document');
  requireValue(value.schemaVersion === 1, 'unsupported schemaVersion');
  requireValue(text(value.language) && text(value.measurementPolicy), 'language and measurementPolicy required');
  keys(value.producer, ['name', 'version', 'parser', 'parserVersion'], 'producer');
  requireValue(['name', 'version', 'parser', 'parserVersion'].every(key => text(value.producer[key])), 'producer provenance required');
  requireValue(['complete', 'incomplete'].includes(value.status), 'invalid status');
  keys(value.capabilities, ['functionComplexity', 'coverage', 'maintainability'], 'capabilities');
  requireValue(['available', 'unsupported'].includes(value.capabilities.functionComplexity), 'invalid complexity capability');
  requireValue(value.capabilities.coverage === 'unsupported' && value.capabilities.maintainability === 'unsupported', 'v1 only exchanges complexity');
  keys(value.scope, ['description', 'excludes'], 'scope');
  requireValue(text(value.scope.description) && Array.isArray(value.scope.excludes) && value.scope.excludes.every(text), 'invalid scope');
  requireValue(Array.isArray(value.diagnostics), 'diagnostics must be an array');
  for (const diagnostic of value.diagnostics) {
    keys(diagnostic, ['code', 'message', 'file'], 'diagnostic');
    requireValue(text(diagnostic.code) && text(diagnostic.message) && (diagnostic.file === undefined || text(diagnostic.file)), 'invalid diagnostic');
  }
  requireValue(value.status !== 'incomplete' || value.diagnostics.length > 0, 'incomplete analysis needs a diagnostic');
  requireValue(value.status !== 'complete' || value.diagnostics.length === 0, 'complete analysis cannot contain failure diagnostics');
  requireValue(Array.isArray(value.functions), 'functions must be an array');
  const ids = new Set();
  for (const item of value.functions) {
    keys(item, ['id', 'file', 'member', 'line', 'ownership', 'complexity'], 'function');
    requireValue(text(item.id) && !ids.has(item.id), 'function IDs must be nonempty and unique');
    ids.add(item.id);
    requireValue(text(item.file) && text(item.member) && count(item.line) && item.line > 0, 'function source location required');
    requireValue(item.ownership === 'exclusive-function-body', 'unsupported ownership policy');
    if (value.capabilities.functionComplexity === 'unsupported') {
      requireValue(item.complexity === null, 'unsupported complexity must be null');
      continue;
    }
    keys(item.complexity, ['value', 'baseline', 'decisions'], 'complexity');
    requireValue(count(item.complexity.value) && item.complexity.value >= 1 && item.complexity.baseline === 1, 'invalid complexity value/baseline');
    const decisions = item.complexity.decisions;
    requireValue(decisions === null || object(decisions), 'decisions must be counts or null');
    if (decisions !== null) {
      requireValue(Object.entries(decisions).every(([key, n]) => text(key) && count(n)), 'invalid decision counts');
      requireValue(1 + Object.values(decisions).reduce((a, b) => a + b, 0) === item.complexity.value, 'decision counts disagree with complexity');
    }
  }
}
