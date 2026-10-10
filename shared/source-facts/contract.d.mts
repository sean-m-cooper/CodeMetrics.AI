/** Internal adapter contract, deliberately separate from scorecard evidence v3. */
export interface SourceFacts {
  schemaVersion: 1 | 2;
  language: string;
  producer: { name: string; version: string; parser: string; parserVersion: string };
  measurementPolicy: string;
  status: 'complete' | 'incomplete';
  capabilities: { functionComplexity: 'available' | 'unsupported'; coverage: 'unsupported'; maintainability: 'unsupported' };
  scope: { description: string; excludes: string[] };
  diagnostics: { code: string; message: string; file?: string }[];
  functions: FunctionFact[];
  /** Required for v2; forbidden for v1. */
  selection?: SelectionInventory;
}
export interface FunctionFact {
  id: string;
  file: string;
  member: string;
  line: number;
  /** Required for v2 facts. V1 facts retain the producing analyzer's selection. */
  population?: 'production' | 'test' | 'support' | 'generated';
  /** Each body owns its decisions; nested function/class bodies are separate. */
  ownership: 'exclusive-function-body';
  complexity: { value: number; baseline: 1; decisions: Record<string, number> | null } | null;
}
export interface SelectionInventory {
  policy: string; configurationFingerprint: string;
  options: { include: string[]; exclude: string[]; testPaths: string[]; productionPaths: string[];
    includeTests: boolean; includeGenerated: boolean; includeSupport: boolean };
  files: { file: string; category: 'production' | 'test' | 'support' | 'generated' | 'declaration';
    disposition: 'analyzed' | 'excluded' | 'failed'; reason: string; contentSha256: string | null }[];
  prunedDirectories: { path: string; reason: string }[];
  excludedFunctions: { id: string; file: string; member: string; line: number; reason: string }[];
}
export function validateSourceFacts(value: unknown): asserts value is SourceFacts;
