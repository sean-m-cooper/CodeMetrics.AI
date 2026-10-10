/** Internal adapter contract, deliberately separate from scorecard evidence v3. */
export interface SourceFacts {
  schemaVersion: 1;
  language: string;
  producer: { name: string; version: string; parser: string; parserVersion: string };
  measurementPolicy: string;
  status: 'complete' | 'incomplete';
  capabilities: { functionComplexity: 'available' | 'unsupported'; coverage: 'unsupported'; maintainability: 'unsupported' };
  scope: { description: string; excludes: string[] };
  diagnostics: { code: string; message: string; file?: string }[];
  functions: FunctionFact[];
}
export interface FunctionFact {
  id: string;
  file: string;
  member: string;
  line: number;
  /** Each body owns its decisions; nested function/class bodies are separate. */
  ownership: 'exclusive-function-body';
  complexity: { value: number; baseline: 1; decisions: Record<string, number> | null } | null;
}
export function validateSourceFacts(value: unknown): asserts value is SourceFacts;
