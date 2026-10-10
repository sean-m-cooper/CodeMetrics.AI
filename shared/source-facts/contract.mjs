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
  const v2 = value?.schemaVersion === 2;
  keys(value, ['schemaVersion', 'language', 'producer', 'measurementPolicy', 'status', 'capabilities', 'scope', 'diagnostics', 'functions', ...(v2 ? ['selection'] : [])], 'document');
  requireValue(value.schemaVersion === 1 || v2, 'unsupported schemaVersion');
  requireValue(text(value.language) && text(value.measurementPolicy), 'language and measurementPolicy required');
  keys(value.producer, ['name', 'version', 'parser', 'parserVersion'], 'producer');
  requireValue(['name', 'version', 'parser', 'parserVersion'].every(key => text(value.producer[key])), 'producer provenance required');
  requireValue(['complete', 'incomplete'].includes(value.status), 'invalid status');
  keys(value.capabilities, ['functionComplexity', 'coverage', 'maintainability'], 'capabilities');
  requireValue(['available', 'unsupported'].includes(value.capabilities.functionComplexity), 'invalid complexity capability');
  requireValue(value.capabilities.coverage === 'unsupported' && value.capabilities.maintainability === 'unsupported', 'this contract only exchanges complexity');
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
    keys(item, ['id', 'file', 'member', 'line', 'ownership', 'complexity', ...(v2 ? ['population'] : [])], 'function');
    requireValue(text(item.id) && !ids.has(item.id), 'function IDs must be nonempty and unique');
    ids.add(item.id);
    requireValue(text(item.file) && text(item.member) && count(item.line) && item.line > 0, 'function source location required');
    requireValue(item.ownership === 'exclusive-function-body', 'unsupported ownership policy');
    if (v2) requireValue(['production', 'test', 'support', 'generated'].includes(item.population), 'invalid function population');
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
  if (v2) validateSelection(value, ids);
}

function validateSelection(document, ids) {
  const value = document.selection;
  const digest = value => typeof value === 'string' && /^[a-f0-9]{64}$/.test(value);
  keys(value, ['policy', 'configurationFingerprint', 'options', 'files', 'prunedDirectories', 'excludedFunctions'], 'selection');
  requireValue(text(value.policy) && digest(value.configurationFingerprint), 'selection provenance required');
  const lists = ['include', 'exclude', 'testPaths', 'productionPaths'];
  const flags = ['includeTests', 'includeGenerated', 'includeSupport'];
  keys(value.options, [...lists, ...flags], 'selection options');
  requireValue(lists.every(key => Array.isArray(value.options[key]) && value.options[key].every(text)) &&
    flags.every(key => typeof value.options[key] === 'boolean'), 'invalid selection options');
  requireValue(Array.isArray(value.files) && Array.isArray(value.prunedDirectories) && Array.isArray(value.excludedFunctions), 'selection inventories required');
  const files = new Map();
  for (const item of value.files) {
    keys(item, ['file', 'category', 'disposition', 'reason', 'contentSha256'], 'selected file');
    requireValue(text(item.file) && !files.has(item.file) && text(item.reason), 'unique files and reasons required');
    requireValue(['production', 'test', 'support', 'generated', 'declaration'].includes(item.category) &&
      ['analyzed', 'excluded', 'failed'].includes(item.disposition), 'invalid file classification');
    requireValue(item.contentSha256 === null || digest(item.contentSha256), 'invalid source digest');
    requireValue(item.disposition !== 'analyzed' || (digest(item.contentSha256) && item.category !== 'declaration'), 'analyzed implementation source required');
    const flag = { test: 'includeTests', support: 'includeSupport', generated: 'includeGenerated' }[item.category];
    requireValue(item.disposition !== 'analyzed' || !flag || value.options[flag], 'population was not enabled by selection options');
    requireValue(item.disposition !== 'failed' || document.status === 'incomplete', 'failed files require incomplete analysis');
    files.set(item.file, item);
  }
  for (const item of value.prunedDirectories) {
    keys(item, ['path', 'reason'], 'pruned directory');
    requireValue(text(item.path) && text(item.reason), 'pruned directory provenance required');
  }
  for (const item of document.functions) {
    const file = files.get(item.file);
    requireValue(file?.disposition === 'analyzed' && file.category === item.population, 'function does not belong to its analyzed population');
  }
  for (const item of value.excludedFunctions) {
    keys(item, ['id', 'file', 'member', 'line', 'reason'], 'excluded function');
    requireValue(text(item.id) && !ids.has(item.id) && text(item.member) && count(item.line) && item.line > 0 && text(item.reason), 'invalid or duplicate excluded function');
    requireValue(files.get(item.file)?.disposition === 'analyzed', 'excluded declaration needs an analyzed file');
    ids.add(item.id);
  }
}
