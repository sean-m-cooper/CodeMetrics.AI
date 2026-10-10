import ts from 'typescript';
import type { FunctionMeasurement } from './function-measurements.js';
import type { SourceFacts } from '#source-facts/contract.mjs';

/** Adapt the existing selected population; this does not rediscover or remeasure files. */
export function functionSourceFacts(functions: FunctionMeasurement[]): SourceFacts {
  return {
    schemaVersion: 1, language: 'javascript-typescript',
    producer: { name: 'codemetrics-ai/function-adapter', version: '1', parser: 'typescript', parserVersion: ts.version },
    measurementPolicy: 'owned-function-body-v2-type-erasure', status: 'complete',
    capabilities: { functionComplexity: 'available', coverage: 'unsupported', maintainability: 'unsupported' },
    scope: { description: 'Existing selected implemented-function population supplied by the JS/TS analyzer.',
      excludes: ['nested-function-and-class-bodies', 'type-only-syntax', 'parameter-initializers', 'module-and-field-initializers'] },
    diagnostics: [],
    functions: functions.map(item => ({ id: item.id, file: item.file, member: item.member, line: item.line,
      ownership: 'exclusive-function-body', complexity: { value: item.ownComplexity, baseline: 1,
        decisions: item.complexityBreakdown ? { ...item.complexityBreakdown.counts } : null } }))
  };
}
